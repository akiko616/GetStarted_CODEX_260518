using System;
using System.Collections.Generic;
using System.IO;
using ReplaySystem.Core;
using UnityEngine;

namespace ReplaySystem.Network
{
    /// <summary>네트워크 리플레이 동기화 시스템입니다.</summary>
    public class NetworkReplaySync : MonoBehaviour
    {
        [Header("Network Settings")]
        [SerializeField] private bool isServer;
        [SerializeField] private int localClientId;

        private readonly Dictionary<uint, float> tickToTimeMap = new Dictionary<uint, float>();
        private readonly Dictionary<int, NetworkObjectState> networkObjectStates = new Dictionary<int, NetworkObjectState>();

        // FishNet ObjectId → Replay ObjectId 매핑 (씬 리로드 후에도 안정적인 ID 보장)
        private readonly Dictionary<int, int> fishNetToReplayId = new Dictionary<int, int>();

        // 더블 버퍼 패턴 (GC 할당 방지)
        private List<NetworkReplayEvent> pendingNetworkEvents = new List<NetworkReplayEvent>(64);
        private List<NetworkReplayEvent> processingNetworkEvents = new List<NetworkReplayEvent>(64);

        private uint currentNetworkTick;
        private float tickRate = 60f;
        private float tickDuration;
        private bool isRecording;
        
        /// <summary>현재 녹화 중인지 여부</summary>
        public bool IsRecording => isRecording;

        /// <summary>현재 네트워크 틱</summary>
        public uint CurrentTick => currentNetworkTick;

        /// <summary>틱 레이트</summary>
        public float TickRate
        {
            get => tickRate;
            set
            {
                tickRate = Mathf.Max(1f, value);
                tickDuration = 1f / tickRate;
            }
        }

        /// <summary>로컬 클라이언트 ID</summary>
        public int LocalClientId
        {
            get => localClientId;
            set => localClientId = value;
        }

        /// <summary>서버 여부</summary>
        public bool IsServer
        {
            get => isServer;
            set => isServer = value;
        }

        private void Awake()
        {
            tickDuration = 1f / tickRate;
        }

        /// <summary>녹화 상태 설정</summary>
        public void SetRecordingState(bool recording)
        {
            isRecording = recording;

            if (!recording)
            {
                pendingNetworkEvents.Clear();
                processingNetworkEvents.Clear();
                fishNetToReplayId.Clear();
            }
        }

        /// <summary>FishNet ObjectId와 Replay ObjectId 매핑을 등록합니다.</summary>
        /// <param name="fishNetObjectId">FishNet의 동적 NetworkObject.ObjectId</param>
        /// <param name="replayObjectId">리플레이 시스템의 안정 ID (TransformCapture.ObjectId)</param>
        public void RegisterNetworkIdMapping(int fishNetObjectId, int replayObjectId)
        {
            fishNetToReplayId[fishNetObjectId] = replayObjectId;
        }

        /// <summary>FishNet ObjectId를 Replay ObjectId로 변환합니다. 매핑이 없으면 원본 ID를 반환합니다.</summary>
        public int ResolveToReplayId(int fishNetObjectId)
        {
            return fishNetToReplayId.TryGetValue(fishNetObjectId, out int replayId)
                ? replayId : fishNetObjectId;
        }

        /// <summary>네트워크 틱 업데이트</summary>
        public void UpdateTick(uint tick, float gameTime)
        {
            currentNetworkTick = tick;
            tickToTimeMap[tick] = gameTime;

            while (tickToTimeMap.Count > 1000)
            {
                uint oldestTick = currentNetworkTick - 1000;
                tickToTimeMap.Remove(oldestTick);
            }
        }

        /// <summary>틱을 게임 시간으로 변환</summary>
        public float TickToTime(uint tick)
        {
            if (tickToTimeMap.TryGetValue(tick, out float time))
            {
                return time;
            }

            return tick * tickDuration;
        }

        /// <summary>게임 시간을 틱으로 변환</summary>
        public uint TimeToTick(float time)
        {
            return (uint)(time / tickDuration);
        }

        /// <summary>RPC 호출을 기록합니다.</summary>
        public void RecordRpc(int targetObjectId, string rpcName, byte[] parameters, int sourceClientId = -1)
        {
            if (!isRecording)
            {
                return;
            }

            var evt = new NetworkReplayEvent
            {
                EventType = NetworkEventType.Rpc,
                Tick = currentNetworkTick,
                TargetObjectId = targetObjectId,
                EventName = rpcName,
                Parameters = parameters,
                SourceClientId = sourceClientId >= 0 ? sourceClientId : localClientId
            };

            pendingNetworkEvents.Add(evt);
        }

        /// <summary>NetworkVariable 변경을 기록합니다.</summary>
        public void RecordNetworkVarChange(int objectId, string varName, byte[] previousValue, byte[] newValue)
        {
            if (!isRecording)
            {
                return;
            }

            var evt = new NetworkReplayEvent
            {
                EventType = NetworkEventType.VarChange,
                Tick = currentNetworkTick,
                TargetObjectId = objectId,
                EventName = varName,
                Parameters = newValue,
                PreviousValue = previousValue,
                SourceClientId = localClientId
            };

            pendingNetworkEvents.Add(evt);
        }

        /// <summary>네트워크 SyncList 변경을 기록합니다.</summary>
        public void RecordSyncListChange(int targetObjectId, string listName, byte[] changeData)
        {
            if (!isRecording)
            {
                return;
            }

            var evt = new NetworkReplayEvent
            {
                EventType = NetworkEventType.SyncListChange,
                Tick = currentNetworkTick,
                TargetObjectId = targetObjectId,
                EventName = listName,
                Parameters = changeData,
                SourceClientId = localClientId
            };

            pendingNetworkEvents.Add(evt);
        }

        /// <summary>네트워크 SyncDictionary 변경을 기록합니다.</summary>
        public void RecordSyncDictionaryChange(int targetObjectId, string dictName, byte[] changeData)
        {
            if (!isRecording)
            {
                return;
            }

            var evt = new NetworkReplayEvent
            {
                EventType = NetworkEventType.SyncDictionaryChange,
                Tick = currentNetworkTick,
                TargetObjectId = targetObjectId,
                EventName = dictName,
                Parameters = changeData,
                SourceClientId = localClientId
            };

            pendingNetworkEvents.Add(evt);
        }

        /// <summary>소유권 변경을 기록합니다.</summary>
        public void RecordOwnershipChange(int objectId, int previousOwner, int newOwner)
        {
            if (!isRecording)
            {
                return;
            }

            var evt = new NetworkReplayEvent
            {
                EventType = NetworkEventType.OwnershipChange,
                Tick = currentNetworkTick,
                TargetObjectId = objectId,
                PreviousOwnerId = previousOwner,
                NewOwnerId = newOwner,
                SourceClientId = localClientId
            };

            pendingNetworkEvents.Add(evt);
        }

        /// <summary>네트워크 오브젝트 스폰을 기록합니다.</summary>
        public void RecordSpawn(int networkObjectId, string prefabName, int ownerId, Vector3 position, Quaternion rotation)
        {
            if (!isRecording)
            {
                return;
            }

            var evt = new NetworkReplayEvent
            {
                EventType = NetworkEventType.Spawn,
                Tick = currentNetworkTick,
                TargetObjectId = networkObjectId,
                EventName = prefabName,
                NewOwnerId = ownerId,
                Position = position,
                Rotation = rotation,
                SourceClientId = localClientId
            };

            pendingNetworkEvents.Add(evt);

            networkObjectStates[networkObjectId] = new NetworkObjectState
            {
                NetworkId = networkObjectId,
                PrefabName = prefabName,
                OwnerId = ownerId,
                IsSpawned = true
            };
        }

        /// <summary>네트워크 오브젝트 디스폰을 기록합니다.</summary>
        public void RecordDespawn(int networkObjectId)
        {
            if (!isRecording)
            {
                return;
            }

            var evt = new NetworkReplayEvent
            {
                EventType = NetworkEventType.Despawn,
                Tick = currentNetworkTick,
                TargetObjectId = networkObjectId,
                SourceClientId = localClientId
            };

            pendingNetworkEvents.Add(evt);

            if (networkObjectStates.TryGetValue(networkObjectId, out var state))
            {
                state.IsSpawned = false;
            }
        }

        /// <summary>네트워크 브로드캐스트를 기록합니다.</summary>
        public void RecordBroadcast(string broadcastTypeName, byte[] data)
        {
            if (!isRecording)
            {
                return;
            }

            var evt = new NetworkReplayEvent
            {
                EventType = NetworkEventType.Broadcast,
                Tick = currentNetworkTick,
                TargetObjectId = 0,
                EventName = broadcastTypeName,
                Parameters = data,
                SourceClientId = localClientId
            };

            pendingNetworkEvents.Add(evt);
        }

        /// <summary>대기 중인 네트워크 이벤트를 가져옵니다 (더블 버퍼 - GC 할당 없음).</summary>
        public List<NetworkReplayEvent> FlushEvents()
        {
            // 버퍼 스왑 (GC 할당 없음)
            (pendingNetworkEvents, processingNetworkEvents) = (processingNetworkEvents, pendingNetworkEvents);

            // 이전 처리 버퍼 클리어 (이제 새 pending 버퍼)
            pendingNetworkEvents.Clear();

            // 스왑된 버퍼 반환 (호출자가 처리)
            return processingNetworkEvents;
        }

        /// <summary>네트워크 이벤트를 ReplayEvent로 변환합니다.</summary>
        /// <param name="netEvent">네트워크 이벤트</param>
        /// <param name="gameTime">게임 시간 (리플레이 시간 기준)</param>
        /// <param name="frameIndex">현재 프레임 인덱스 (리플레이 프레임 기준)</param>
        public ReplayEvent ConvertToReplayEvent(NetworkReplayEvent netEvent, float gameTime, int frameIndex = -1)
        {
            return new ReplayEvent
            {
                Time = gameTime,
                // 프레임 인덱스가 명시적으로 주어지면 사용, 아니면 틱 기반 추정
                FrameIndex = frameIndex >= 0 ? frameIndex : EstimateFrameFromTick(netEvent.Tick),
                TargetObjectId = netEvent.TargetObjectId,
                EventType = ConvertEventType(netEvent.EventType),
                EventName = netEvent.EventName,
                Parameters = SerializeNetworkEvent(netEvent),
                NetworkTick = netEvent.Tick,
                SourceClientId = netEvent.SourceClientId
            };
        }

        /// <summary>네트워크 틱에서 대략적인 프레임 인덱스 추정</summary>
        private int EstimateFrameFromTick(uint tick)
        {
            // 틱레이트와 프레임레이트의 비율로 추정
            // 예: 틱레이트 60, 프레임레이트 60이면 1:1
            // 이 값은 ReplayRecorder의 targetFrameRate와 일치해야 정확함
            return (int)tick;
        }

        /// <summary>ReplayEvent에서 네트워크 이벤트를 재생합니다.</summary>
        public void ExecuteNetworkEvent(ReplayEvent evt, INetworkReplayHandler handler)
        {
            if (handler == null)
            {
                return;
            }

            var netEvent = DeserializeNetworkEvent(evt);

            switch (netEvent.EventType)
            {
                case NetworkEventType.Rpc:
                    handler.OnReplayRpc(netEvent.TargetObjectId, netEvent.EventName, netEvent.Parameters);
                    break;

                case NetworkEventType.VarChange:
                    handler.OnReplayVarChange(netEvent.TargetObjectId, netEvent.EventName, netEvent.Parameters);
                    break;

                case NetworkEventType.OwnershipChange:
                    handler.OnReplayOwnershipChange(netEvent.TargetObjectId, netEvent.NewOwnerId);
                    break;

                case NetworkEventType.Spawn:
                    handler.OnReplaySpawn(netEvent.TargetObjectId, netEvent.EventName, netEvent.NewOwnerId, netEvent.Position, netEvent.Rotation);
                    break;

                case NetworkEventType.Despawn:
                    handler.OnReplayDespawn(netEvent.TargetObjectId);
                    break;

                case NetworkEventType.SyncListChange:
                    handler.OnReplaySyncListChange(netEvent.TargetObjectId, netEvent.EventName, netEvent.Parameters);
                    break;

                case NetworkEventType.SyncDictionaryChange:
                    handler.OnReplaySyncDictionaryChange(netEvent.TargetObjectId, netEvent.EventName, netEvent.Parameters);
                    break;

                case NetworkEventType.Broadcast:
                    handler.OnReplayBroadcast(netEvent.EventName, netEvent.Parameters);
                    break;
            }
        }

        /// <summary>틱 기반 보간 계수 계산</summary>
        public float CalculateInterpolationFactor(uint fromTick, uint toTick, float currentTime)
        {
            float fromTime = TickToTime(fromTick);
            float toTime = TickToTime(toTick);
            float duration = toTime - fromTime;

            if (duration <= 0)
            {
                return 1f;
            }

            return Mathf.Clamp01((currentTime - fromTime) / duration);
        }

        private ReplayEventType ConvertEventType(NetworkEventType netType)
        {
            return netType switch
            {
                NetworkEventType.Rpc => ReplayEventType.NetworkRpc,
                NetworkEventType.VarChange => ReplayEventType.NetworkVarChange,
                NetworkEventType.OwnershipChange => ReplayEventType.OwnershipChange,
                NetworkEventType.SyncListChange => ReplayEventType.SyncListChange,
                NetworkEventType.SyncDictionaryChange => ReplayEventType.SyncDictionaryChange,
                NetworkEventType.Spawn => ReplayEventType.NetworkSpawn,
                NetworkEventType.Despawn => ReplayEventType.NetworkDespawn,
                NetworkEventType.Broadcast => ReplayEventType.NetworkBroadcast,
                _ => ReplayEventType.Custom
            };
        }

        private byte[] SerializeNetworkEvent(NetworkReplayEvent evt)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write((byte)evt.EventType);
                writer.Write(evt.SourceClientId);
                writer.Write(evt.PreviousOwnerId);
                writer.Write(evt.NewOwnerId);
                writer.Write(evt.Position.x);
                writer.Write(evt.Position.y);
                writer.Write(evt.Position.z);
                writer.Write(evt.Rotation.x);
                writer.Write(evt.Rotation.y);
                writer.Write(evt.Rotation.z);
                writer.Write(evt.Rotation.w);

                if (evt.Parameters != null)
                {
                    writer.Write(evt.Parameters.Length);
                    writer.Write(evt.Parameters);
                }
                else
                {
                    writer.Write(0);
                }

                if (evt.PreviousValue != null)
                {
                    writer.Write(evt.PreviousValue.Length);
                    writer.Write(evt.PreviousValue);
                }
                else
                {
                    writer.Write(0);
                }

                return ms.ToArray();
            }
        }

        private NetworkReplayEvent DeserializeNetworkEvent(ReplayEvent evt)
        {
            var netEvent = new NetworkReplayEvent
            {
                Tick = evt.NetworkTick,
                TargetObjectId = evt.TargetObjectId,
                EventName = evt.EventName
            };

            if (evt.Parameters == null || evt.Parameters.Length == 0)
            {
                return netEvent;
            }

            using (var ms = new MemoryStream(evt.Parameters))
            using (var reader = new BinaryReader(ms))
            {
                netEvent.EventType = (NetworkEventType)reader.ReadByte();
                netEvent.SourceClientId = reader.ReadInt32();
                netEvent.PreviousOwnerId = reader.ReadInt32();
                netEvent.NewOwnerId = reader.ReadInt32();

                netEvent.Position = new Vector3(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle());

                netEvent.Rotation = new Quaternion(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle());

                int paramLength = reader.ReadInt32();

                if (paramLength > 0)
                {
                    netEvent.Parameters = reader.ReadBytes(paramLength);
                }

                int prevLength = reader.ReadInt32();

                if (prevLength > 0)
                {
                    netEvent.PreviousValue = reader.ReadBytes(prevLength);
                }
            }

            return netEvent;
        }
    }

    /// <summary>네트워크 이벤트 타입</summary>
    public enum NetworkEventType : byte
    {
        Rpc = 0,
        VarChange = 1,
        OwnershipChange = 2,
        Spawn = 3,
        Despawn = 4,
        SyncListChange = 5,
        SyncDictionaryChange = 6,
        Broadcast = 7
    }

    /// <summary>네트워크 리플레이 이벤트</summary>
    public struct NetworkReplayEvent
    {
        public NetworkEventType EventType;
        public uint Tick;
        public int TargetObjectId;
        public string EventName;
        public byte[] Parameters;
        public byte[] PreviousValue;
        public int SourceClientId;
        public int PreviousOwnerId;
        public int NewOwnerId;
        public Vector3 Position;
        public Quaternion Rotation;
    }

    /// <summary>네트워크 오브젝트 상태</summary>
    public class NetworkObjectState
    {
        public int NetworkId;
        public string PrefabName;
        public int OwnerId;
        public bool IsSpawned;
    }

    /// <summary>네트워크 리플레이 핸들러 인터페이스</summary>
    public interface INetworkReplayHandler
    {
        void OnReplayRpc(int objectId, string rpcName, byte[] parameters);
        void OnReplayVarChange(int objectId, string varName, byte[] value);
        void OnReplayOwnershipChange(int objectId, int newOwnerId);
        void OnReplaySyncListChange(int objectId, string listName, byte[] changeData);
        void OnReplaySyncDictionaryChange(int objectId, string dictName, byte[] changeData);
        void OnReplaySpawn(int objectId, string prefabName, int ownerId, Vector3 position, Quaternion rotation);
        void OnReplayDespawn(int objectId);
        void OnReplayBroadcast(string broadcastTypeName, byte[] data);
    }
}
