using System;
using System.Collections.Generic;
using System.Reflection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using ReplaySystem.Capture;
using UnityEngine;

namespace ReplaySystem.Network
{
    /// <summary>
    /// 개별 NetworkObject에 부착되어 해당 오브젝트의 SyncVar 변경과 소유권 변경을 자동 추적합니다.
    /// Reflection을 사용하여 동일 게임오브젝트에 있는 다른 NetworkBehaviour의 SyncVar<T>를 찾아 등록합니다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class FishNetReplayObserver : NetworkBehaviour
    {
        private NetworkReplaySync networkSync;

        // GC 최적화 캐시 (인스턴스별로 관리하여 re-entrancy 방지)
        private readonly List<NetworkBehaviour> _behaviourCache = new List<NetworkBehaviour>();
        
        // 메모리 누수 방지용 이벤트 핸들러 추적 (동일 타입 SyncVar 복수 지원)
        private readonly List<(object target, EventInfo eventInfo, Delegate handler)> _hookedEvents 
            = new List<(object, EventInfo, Delegate)>();

        private void Awake()
        {
            networkSync = FindFirstObjectByType<NetworkReplaySync>();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            if (networkSync == null) return;
            
            if (networkSync.IsRecording)
            {
                // FishNet ObjectId → Replay ObjectId 매핑 등록 (SyncVar/RPC 기록 시 올바른 ID 사용을 위해 조기 등록)
                // 참고: Spawn/Despawn 기록은 FishNetReplayManager에서 전담합니다.
                var transformCapture = GetComponent<TransformCapture>();
                if (transformCapture != null && transformCapture.ObjectId >= 0)
                {
                    networkSync.RegisterNetworkIdMapping(NetworkObject.ObjectId, transformCapture.ObjectId);
                }
            }

            // 동일 게임오브젝트의 모든 NetworkBehaviour 검사 (GC 없이 List 재사용)
            GetComponents(_behaviourCache);
            int count = _behaviourCache.Count;
            for (int i = 0; i < count; i++)
            {
                var nb = _behaviourCache[i];
                if (nb == this) continue; // 자기 자신(Observer) 제외
                DiscoverAndSubscribeSyncVars(nb);
            }
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            // 참고: Despawn 기록은 FishNetReplayManager에서 전담합니다.

            // 이벤트 핸들러 메모리 누수 방지 해제 로직
            foreach (var (target, eventInfo, handler) in _hookedEvents)
            {
                try
                {
                    eventInfo.RemoveEventHandler(target, handler);
                }
                catch (Exception) { /* 무시 */ }
            }
            _hookedEvents.Clear();
        }

        public override void OnOwnershipServer(FishNet.Connection.NetworkConnection prevOwner)
        {
            base.OnOwnershipServer(prevOwner);

            if (networkSync == null || !networkSync.IsRecording) return;
            
            int prevId = prevOwner != null ? prevOwner.ClientId : -1;
            int newId = Owner != null ? Owner.ClientId : -1;

            networkSync.RecordOwnershipChange(networkSync.ResolveToReplayId(NetworkObject.ObjectId), prevId, newId);
        }

        private void DiscoverAndSubscribeSyncVars(NetworkBehaviour nb)
        {
            // public/private/protected 인스턴스 필드 모두 검사
            var fields = nb.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            
            foreach (var field in fields)
            {
                var fieldType = field.FieldType;
                
                // SyncVar<T> 인지 확인
                if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(SyncVar<>))
                {
                    SubscribeToSyncVarChange(nb, field, fieldType);
                }
            }
        }

        private void SubscribeToSyncVarChange(NetworkBehaviour nb, FieldInfo field, Type syncVarType)
        {
            try
            {
                // SyncVar 인스턴스 가져오기
                object syncVarInstance = field.GetValue(nb);
                if (syncVarInstance == null) return;

                // SyncVar<T>의 T 타입 추출
                Type varType = syncVarType.GetGenericArguments()[0];

                // OnChange 이벤트 가져오기 (이벤트는 이벤트 필드와 add/remove 메서드로 구성됨)
                EventInfo onChangeEvent = syncVarType.GetEvent("OnChange");
                if (onChangeEvent == null) return; // FishNet V4 변경사항 확인용

                // 제네릭 헬퍼 메서드를 호출하여 타입 안전한 델리게이트를 생성하고 등록
                MethodInfo hookMethod = typeof(FishNetReplayObserver).GetMethod(
                    nameof(SubscribeGenericOnChange), 
                    BindingFlags.Instance | BindingFlags.NonPublic);
                    
                MethodInfo genericHookMethod = hookMethod.MakeGenericMethod(varType);
                
                // 실행: this.SubscribeGenericOnChange<T>(syncVarInstance, onChangeEvent, field.Name);
                genericHookMethod.Invoke(this, new object[] { syncVarInstance, onChangeEvent, field.Name });
                
                // Debug.Log($"[FishNetReplayObserver] Subscribed to SyncVar '{field.Name}' of type {varType.Name} on {nb.GetType().Name}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FishNetReplayObserver] Failed to subscribe to SyncVar '{field.Name}' on {nb.GetType().Name}: {e.Message}");
            }
        }

        // Reflection 런타임 호출용 제네릭 헬퍼
        private void SubscribeGenericOnChange<T>(object syncVarInstance, EventInfo onChangeEvent, string varName)
        {
            // 콜백 델리게이트 생성 (oldValue, newValue, asServer)
            Action<T, T, bool> handler = (oldValue, newValue, asServer) => 
            {
                HandleSyncVarChange(varName, oldValue, newValue, asServer);
            };

            // 이벤트 델리게이트 타입 확인 후 변환하여 AddEventHandler 호출
            Delegate d = Delegate.CreateDelegate(onChangeEvent.EventHandlerType, handler.Target, handler.Method);
            onChangeEvent.AddEventHandler(syncVarInstance, d);

            // 구동 종료 시 안전한 해제를 위해 추적
            _hookedEvents.Add((syncVarInstance, onChangeEvent, d));
        }

        // 실제 이벤트 퍼블리싱 로직
        private void HandleSyncVarChange<T>(string varName, T oldValue, T newValue, bool asServer)
        {
            // 서버측 데이터만 기록 (중복 기록 및 롤백 혼란 방지)
            if (!asServer || networkSync == null || !networkSync.IsRecording) return;
            
            // ReplaySystem ↔ FishNet 직렬화 변환 (byte[])
            // 최적화/수정: SerializeWithName은 varName을 중복 직렬화하므로 Serialize<T> 사용. EventName에 이미 varName이 저장됨.
            byte[] serializedOldValue = FishNetSerializationBridge.Serialize<T>(oldValue);
            byte[] serializedNewValue = FishNetSerializationBridge.Serialize<T>(newValue);
            
            networkSync.RecordNetworkVarChange(networkSync.ResolveToReplayId(NetworkObject.ObjectId), varName, serializedOldValue, serializedNewValue);
        }
    }
}
