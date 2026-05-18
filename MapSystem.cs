using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using System;
using UnityEngine;
using DrillSergeant;

namespace TRAINEE
{
    public struct MapEventBroadcast : IBroadcast
    {
        // 우리가 만든 시리얼라이저가 이 녀석을 알아서 포장/해체해 줍니다!
        public GameModelEventBase EventData;
    }

    public struct PropSyncBroadcast : IBroadcast
    {
        public DynamicPropData[] PropDatas;
    }

    public class MapSystem : SystemBase
    {
        private MapControllerBase _mapController = null;
        private float _propSyncTimer = 0f;
        public MapControllerBase MapController { get => _mapController; }
        public override void Init()
        {
            base.Init();

#if GAMEINSTANCE
            // 서버 처리

            if (InstanceFinder.ServerManager != null)
            {
                Debug.Log($"[Server] OnServerReceived 등록");
                InstanceFinder.ServerManager.RegisterBroadcast<MapEventBroadcast>(OnServerReceived);
            }
#else
            if (NetworkManager.Instance.ClientManager != null)
            {
                NetworkManager.Instance.ClientManager.RegisterBroadcast<MapEventBroadcast>(OnClientReceived);
                NetworkManager.Instance.ClientManager.RegisterBroadcast<PropSyncBroadcast>(OnClientPropSyncReceived);
            }
#endif

//#if DrillSergeant
//            _mapController = DrillManager.Instance.MapController;
//            _mapController.transform.SetParent(this.transform);
//            _mapController.transform.SetPositionAndRotation(UnityEngine.Vector3.zero, UnityEngine.Quaternion.identity);
//            _mapController.Init();
//            DrillManager.Instance.CameraHandler.SetDefaultPosition(CameraViewMode.TopDown);
//#endif

        }

        private void OnServerReceived(NetworkConnection conn, MapEventBroadcast msg, FishNet.Transporting.Channel channel)
        {
            GameModelEventBase confirmedEvent = _mapController?.ValidateEventOnServer(msg.EventData);

            if (confirmedEvent != null)
            {
                msg.EventData = confirmedEvent;
                InstanceFinder.ServerManager.Broadcast(msg);

                Debug.Log($"[Server] 상호작용 요청 완료: {msg.EventData._id} / {msg.EventData.isServerConfirmed}");
            }
            else
            {
                Debug.Log($"[Server] 비정상적인 상호작용 요청 거절됨: {msg.EventData._id}");
            }
        }

        private void OnClientReceived(MapEventBroadcast msg, FishNet.Transporting.Channel channel)
        {
            Debug.Log($"[Server] -> [Client] 상호작용 결과: {msg.EventData._id} / {msg.EventData.isServerConfirmed}");

            _mapController?.OnNetworkEventReceived(msg.EventData);
        }

        public void RequestInteract(GameModelEventBase eventData)
        {
            MapEventBroadcast msg = new MapEventBroadcast
            {
                EventData = eventData
            };

#if GAMEINSTANCE
            if (InstanceFinder.ServerManager != null)
            {
                // conn 자리에 null(또는 빈 커넥션)을 넣어서 서버 자체 요청임을 명시합니다.
                OnServerReceived(null, msg, FishNet.Transporting.Channel.Reliable);
            }
#else
            if (NetworkManager.Instance.ClientManager != null)
            {
                Debug.Log($"[Client] -> [Server] 상호작용 요청 : {msg.EventData._id} / {msg.EventData.isServerConfirmed}");
                NetworkManager.Instance.ClientManager.Broadcast(msg);
            }
#endif
        }

        public override void OnUpdate(float deltaTime)
        {
            if(_mapController != null)
                _mapController.OnUpdate(deltaTime);
        }

        public override void OnFixedUpdate(float deltaTime)
        {
            if (_mapController != null)
                _mapController.OnFixedUpdate(deltaTime);

#if GAMEINSTANCE
            // 🌟 서버 전용: 0.1초마다 맵 컨트롤러에게 "움직인 의자들 좌표 다 가져와!" 명령
            if (InstanceFinder.ServerManager != null)
            {
                _propSyncTimer += deltaTime;

                if (_propSyncTimer >= 0.1f)
                {
                    _propSyncTimer = 0f;
                    DynamicPropData[] changedProps = _mapController.GetDynamicProps;

                    if (changedProps != null && changedProps.Length > 0)
                    {
                        // 클라이언트들에게 묶음 전송
                        PropSyncBroadcast msg = new PropSyncBroadcast { PropDatas = changedProps };
                        InstanceFinder.ServerManager.Broadcast(msg);
                    }
                }
            }
#endif
        }

        public  override void OnLateUpdate(float deltaTime)
        {

        }

        public void RegisterDynamicProp(string viewId, DynamicProp[] props)
        {
            if (_mapController != null)
            {
                // 맵 컨트롤러 쪽에 Dictionary를 두고 저장하도록 전달!
                _mapController.RegisterDynamicProp(viewId, props);
            }
            else
            {
                Debug.LogWarning("[MapSystem] MapController가 아직 로드되지 않았는데 프롭 등록 시도가 있었습니다.");
            }
        }

        private void OnClientPropSyncReceived(PropSyncBroadcast msg, FishNet.Transporting.Channel channel)
        {
            if (msg.PropDatas != null && _mapController != null)
            {
                // 맵 컨트롤러에게 좌표 덮어씌우라고 전달
                _mapController.ApplyDynamicProps(msg.PropDatas);
            }
        }

        public override async UniTask LoadingAsync(Action<float, string> onProgress, int delaytime)
        {
#if DrillSergeant
            _mapController = DrillManager.Instance.MapController;
            _mapController.transform.SetParent(this.transform);
            _mapController.transform.SetPositionAndRotation(UnityEngine.Vector3.zero, UnityEngine.Quaternion.identity);
            _mapController.Init();
#else
            // 시나리오 별로 맵 컨트롤러를 세팅한다.
            string buildingName = GameManager.Instance.GetCurrentBuilding.ToString();
            Debug.Log($"buildingName : {buildingName}");
            string path = $"{Const.Path.BUILT_IN_MAP_PATH}{buildingName}/MapController";

            _mapController = LdResources.Load<MapControllerBase>(path, this.transform);
            _mapController?.Init();
#endif
            await UniTask.Delay(delaytime);
            
            float totalprogress = 1;

            onProgress?.Invoke(totalprogress, $"맵 로드 중...");

        }

        public override void OnDestroy()
        {
            base.OnDestroy();

#if GAMEINSTANCE
            // 서버 처리

            if (InstanceFinder.ServerManager != null)
            {
                // 서버: 클라이언트들이 보내는 우편물을 OnServerReceived 함수로 받겠다!
                InstanceFinder.ServerManager.UnregisterBroadcast<MapEventBroadcast>(OnServerReceived);
            }
#else
            if (NetworkManager.Instance.ClientManager != null)
            {
                NetworkManager.Instance.ClientManager.UnregisterBroadcast<MapEventBroadcast>(OnClientReceived);
            }
#endif
        }
    }
}
