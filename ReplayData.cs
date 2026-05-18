using System;
using System.Collections.Generic;
using ReplaySystem.Compression;
using UnityEngine;

namespace ReplaySystem.Core
{
    /// <summary>리플레이 파일 전체 데이터입니다 (v2.0)</summary>
    [Serializable]
    public class ReplayData
    {
        public const int VERSION = 3;
        public const uint MAGIC = 0x52504C32; // "RPL2"

        public ReplayMetadata Metadata;
        public SceneSnapshot InitialState;
        public List<ReplayChunkHeader> ChunkHeaders;
        public List<ReplayEvent> Events;

        /// <summary>청크 데이터 (스트리밍용, 직접 저장되지 않음)</summary>
        [NonSerialized]
        public List<ReplayChunk> LoadedChunks;

        public ReplayData()
        {
            ChunkHeaders = new List<ReplayChunkHeader>();
            Events = new List<ReplayEvent>();
            LoadedChunks = new List<ReplayChunk>();
        }
    }

    /// <summary>리플레이 메타데이터 (v2.0)</summary>
    [Serializable]
    public struct ReplayMetadata
    {
        public long RecordedAtTicks;
        public float Duration;
        public int FrameRate;
        public int KeyframeInterval;
        public string SceneName;
        public int TrackedObjectCount;

        /// <summary>청크 수</summary>
        public int ChunkCount;

        /// <summary>청크당 프레임 수</summary>
        public int FramesPerChunk;

        /// <summary>물리 캡처 활성화 여부</summary>
        public bool HasPhysicsData;

        /// <summary>네트워크 동기화 데이터 포함 여부</summary>
        public bool HasNetworkData;

        /// <summary>애니메이터 데이터 포함 여부</summary>
        public bool HasAnimatorData;

        /// <summary>압축 타입</summary>
        public ReCompressionType Compression;

        /// <summary>녹화 시작 시간 (DateTime)</summary>
        public DateTime RecordedAt => new DateTime(RecordedAtTicks, DateTimeKind.Utc);
    }

    /// <summary>압축 타입</summary>
    public enum ReCompressionType : byte
    {
        None = 0,
        GZip = 1,
        LZ4 = 2
    }

    /// <summary>초기 씬 스냅샷 (v2.0)</summary>
    [Serializable]
    public class SceneSnapshot
    {
        public List<ObjectState> Objects;
        public List<DynamicObjectInfo> DynamicObjectPrefabs;

        public SceneSnapshot()
        {
            Objects = new List<ObjectState>();
            DynamicObjectPrefabs = new List<DynamicObjectInfo>();
        }
    }

    /// <summary>개별 오브젝트 초기 상태 (v2.0)</summary>
    [Serializable]
    public struct ObjectState
    {
        public int Id;
        public string PrefabName;
        public string PrefabPath;
        public string HierarchyPath;
        public bool IsNetworkObject;
        public CompressedTransform Transform;
        public bool HasRigidbody;
        public CompressedRigidbody Rigidbody;
        public bool HasAnimator;
        public CompressedAnimatorState Animator;
        public byte[] CustomData;
    }

    /// <summary>동적 오브젝트 프리팹 정보</summary>
    [Serializable]
    public struct DynamicObjectInfo
    {
        public string PrefabPath;
        public int PrefabHash;
    }

    /// <summary>청크 헤더 (인덱스용)</summary>
    [Serializable]
    public struct ReplayChunkHeader
    {
        /// <summary>청크 인덱스</summary>
        public int Index;

        /// <summary>시작 프레임</summary>
        public int StartFrame;

        /// <summary>종료 프레임</summary>
        public int EndFrame;

        /// <summary>시작 시간</summary>
        public float StartTime;

        /// <summary>종료 시간</summary>
        public float EndTime;

        /// <summary>파일 내 오프셋 (바이트)</summary>
        public long FileOffset;

        /// <summary>압축된 크기</summary>
        public int CompressedSize;

        /// <summary>키프레임 포함 여부</summary>
        public bool ContainsKeyframe;
    }

    /// <summary>리플레이 청크 (메모리 로드 단위)</summary>
    [Serializable]
    public class ReplayChunk
    {
        public int Index;
        public ReplayKeyframe Keyframe;
        public List<ReplayFrame> Frames;
        public List<ReplayEvent> Events;

        public ReplayChunk()
        {
            Frames = new List<ReplayFrame>();
            Events = new List<ReplayEvent>();
        }
    }

    /// <summary>키프레임 (v2.0 - 압축 적용)</summary>
    [Serializable]
    public class ReplayKeyframe
    {
        public int FrameIndex;
        public float Time;
        public List<CompressedObjectState> States;

        public ReplayKeyframe()
        {
            States = new List<CompressedObjectState>();
        }
    }

    /// <summary>압축된 오브젝트 상태</summary>
    [Serializable]
    public struct CompressedObjectState
    {
        public int ObjectId;
        public CompressedTransform Transform;
        public bool HasRigidbody;
        public CompressedRigidbody Rigidbody;
        public bool HasAnimator;
        public CompressedAnimatorState Animator;
    }

    /// <summary>일반 프레임 (v2.0 - 압축 적용)</summary>
    [Serializable]
    public struct ReplayFrame
    {
        public int Index;
        public float Time;
        public float DeltaTime;
        public List<CompressedTransformDelta> TransformDeltas;
        public List<CompressedRigidbodyDelta> RigidbodyDeltas;
        public List<CompressedAnimatorDelta> AnimatorDeltas;

        /// <summary>네트워크 틱 (멀티플레이어용)</summary>
        public uint NetworkTick;

        /// <summary>안전하게 초기화된 ReplayFrame을 생성합니다 (null List 방지).</summary>
        /// <param name="transformCapacity">Transform 델타 리스트 초기 용량</param>
        /// <param name="rigidbodyCapacity">Rigidbody 델타 리스트 초기 용량</param>
        /// <param name="animatorCapacity">Animator 델타 리스트 초기 용량</param>
        public static ReplayFrame Create(int transformCapacity = 64, int rigidbodyCapacity = 32, int animatorCapacity = 16)
        {
            return new ReplayFrame
            {
                TransformDeltas = new List<CompressedTransformDelta>(transformCapacity),
                RigidbodyDeltas = new List<CompressedRigidbodyDelta>(rigidbodyCapacity),
                AnimatorDeltas = new List<CompressedAnimatorDelta>(animatorCapacity)
            };
        }

        /// <summary>리스트가 초기화되어 있는지 확인합니다.</summary>
        public readonly bool IsInitialized => TransformDeltas != null && RigidbodyDeltas != null && AnimatorDeltas != null;

        /// <summary>모든 델타 리스트를 초기화합니다 (null인 경우에만).</summary>
        public void EnsureInitialized(int transformCapacity = 64, int rigidbodyCapacity = 32, int animatorCapacity = 16)
        {
            TransformDeltas ??= new List<CompressedTransformDelta>(transformCapacity);
            RigidbodyDeltas ??= new List<CompressedRigidbodyDelta>(rigidbodyCapacity);
            AnimatorDeltas ??= new List<CompressedAnimatorDelta>(animatorCapacity);
        }
    }

    /// <summary>압축된 Animator 델타</summary>
    [Serializable]
    public struct CompressedAnimatorDelta
    {
        public int ObjectId;
        public CompressedAnimatorState State;
    }

    /// <summary>압축된 Animator 상태</summary>
    [Serializable]
    public struct CompressedAnimatorState
    {
        /// <summary>레이어별 상태 (최대 4개 레이어 지원)</summary>
        public CompressedAnimatorLayerState Layer0;
        public CompressedAnimatorLayerState Layer1;
        public CompressedAnimatorLayerState Layer2;
        public CompressedAnimatorLayerState Layer3;

        /// <summary>활성 레이어 수</summary>
        public byte LayerCount;

        /// <summary>파라미터 데이터 (압축된 바이너리)</summary>
        public byte[] ParameterData;
    }

    /// <summary>압축된 Animator 레이어 상태</summary>
    [Serializable]
    public struct CompressedAnimatorLayerState
    {
        /// <summary>현재 상태 해시</summary>
        public int StateHash;

        /// <summary>정규화된 시간 (0-1, 16-bit 양자화)</summary>
        public ushort NormalizedTime;

        /// <summary>레이어 가중치 (0-1, 8-bit 양자화)</summary>
        public byte Weight;

        /// <summary>전환 중 여부</summary>
        public bool IsInTransition;

        /// <summary>전환 중일 때 다음 상태 해시</summary>
        public int NextStateHash;

        /// <summary>전환 정규화 시간</summary>
        public ushort TransitionNormalizedTime;
    }

    /// <summary>Transform 델타 플래그</summary>
    [Flags]
    public enum TransformDeltaFlags : byte
    {
        None = 0,
        Position = 1 << 0,
        Rotation = 1 << 1,
        Scale = 1 << 2,
        Active = 1 << 3
    }

    /// <summary>리플레이 이벤트 (v2.0)</summary>
    [Serializable]
    public struct ReplayEvent
    {
        public float Time;
        public int FrameIndex;
        public int TargetObjectId;
        public ReplayEventType EventType;
        public string EventName;
        public byte[] Parameters;

        /// <summary>네트워크 틱 (멀티플레이어용)</summary>
        public uint NetworkTick;

        /// <summary>발신 클라이언트 ID (멀티플레이어용)</summary>
        public int SourceClientId;
    }

    /// <summary>리플레이 이벤트 타입 (v2.0)</summary>
    public enum ReplayEventType : byte
    {
        MethodCall = 0,
        Instantiate = 1,
        Destroy = 2,
        Animation = 3,
        Audio = 4,
        Particle = 5,

        /// <summary>네트워크 RPC</summary>
        NetworkRpc = 10,

        /// <summary>네트워크 변수 변경</summary>
        NetworkVarChange = 11,

        /// <summary>소유권 변경</summary>
        OwnershipChange = 12,

        /// <summary>SyncList 변경 (Add/Remove/Set/Clear/Insert)</summary>
        SyncListChange = 13,

        /// <summary>SyncDictionary 변경</summary>
        SyncDictionaryChange = 14,
        
        /// <summary>네트워크 Spawn</summary>
        NetworkSpawn = 15,

        /// <summary>네트워크 Despawn</summary>
        NetworkDespawn = 16,

        /// <summary>네트워크 전역 브로드캐스트</summary>
        NetworkBroadcast = 17,

        Custom = 255
    }

    /// <summary>이벤트 처리 단계 (상태 델타 적용 전/후)</summary>
    public enum EventProcessingPhase
    {
        /// <summary>상태 델타 적용 전 (오브젝트 생성 등)</summary>
        PreState,
        /// <summary>상태 델타 적용 후 (애니메이션 트리거, 사운드 등)</summary>
        PostState
    }

    /// <summary>이벤트 타입 유틸리티</summary>
    public static class ReplayEventTypeExtensions
    {
        /// <summary>이벤트 타입의 처리 단계를 반환합니다.</summary>
        public static EventProcessingPhase GetProcessingPhase(this ReplayEventType eventType)
        {
            return eventType switch
            {
                // 오브젝트 생성은 상태 적용 전에 실행 (생성 후 상태 적용 가능하도록)
                ReplayEventType.Instantiate => EventProcessingPhase.PreState,
                // 네트워크 스폰도 상태 적용 전에 실행 (스폰 후 Transform 상태 적용 가능하도록)
                ReplayEventType.NetworkSpawn => EventProcessingPhase.PreState,
                // 나머지는 상태 적용 후 실행
                _ => EventProcessingPhase.PostState
            };
        }

        /// <summary>역재생 시 되돌릴 수 있는 이벤트인지 확인합니다.</summary>
        public static bool IsReversible(this ReplayEventType eventType)
        {
            return eventType switch
            {
                ReplayEventType.Instantiate => true,  // 역재생: 파괴
                ReplayEventType.Destroy => true,      // 역재생: 복원
                _ => false
            };
        }

        /// <summary>상태 캡처와 충돌 가능한 이벤트인지 확인합니다.</summary>
        public static bool CanConflictWithStateCapture(this ReplayEventType eventType)
        {
            return eventType switch
            {
                ReplayEventType.Animation => true,  // AnimatorCapture와 충돌 가능
                _ => false
            };
        }
    }
}
