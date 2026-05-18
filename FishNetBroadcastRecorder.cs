using FishNet.Broadcast;
using FishNet.Object;
using UnityEngine;

namespace ReplaySystem.Network
{
    /// <summary>
    /// FishNet Broadcast를 수동으로 기록하기 위한 헬퍼 클래스입니다.
    /// ServerManager나 ClientManager에서 커스텀 Broadcast를 보낼 때 이 클래스의 Record()를 호출하여 리플레이에 기록합니다.
    /// </summary>
    public static class FishNetBroadcastRecorder
    {
        private static NetworkReplaySync networkSync;

        /// <summary>초기화: FishNetReplayManager가 호출합니다.</summary>
        public static void Initialize(NetworkReplaySync sync)
        {
            networkSync = sync;
        }

        /// <summary>Broadcast 호출을 기록합니다.</summary>
        public static void Record<T>(T broadcast) where T : struct, IBroadcast
        {
            if (networkSync == null || !networkSync.IsRecording) return;
            
            byte[] serializedArgs = FishNetSerializationBridge.Serialize(broadcast);
            
            // Broadcast 이벤트 타입 식별을 위해 구조체 이름을 사용
            networkSync.RecordBroadcast(typeof(T).FullName, serializedArgs);
        }
    }
}
