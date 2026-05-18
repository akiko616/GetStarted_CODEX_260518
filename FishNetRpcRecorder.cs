using FishNet.Object;
using UnityEngine;

namespace ReplaySystem.Network
{
    /// <summary>
    /// FishNet RPC 호출을 자동으로 기록하기 위한 헬퍼 클래스입니다.
    /// [ServerRpc] / [ObserversRpc] 구현부 첫 줄에 Record()를 호출하면 됩니다.
    /// </summary>
    public static class FishNetRpcRecorder
    {
        private static NetworkReplaySync networkSync;

        /// <summary>초기화: FishNetReplayManager가 호출합니다.</summary>
        public static void Initialize(NetworkReplaySync sync)
        {
            networkSync = sync;
        }

        /// <summary>인자 없는 RPC 호출을 기록합니다.</summary>
        public static void Record(NetworkBehaviour nb, string rpcName)
        {
            if (networkSync == null || !networkSync.IsRecording || nb == null || nb.NetworkObject == null) return;
            byte[] serializedArgs = FishNetSerializationBridge.SerializeArgs();
            int replayId = networkSync.ResolveToReplayId(nb.NetworkObject.ObjectId);
            networkSync.RecordRpc(replayId, rpcName, serializedArgs);
        }

        /// <summary>1개의 인자를 가진 RPC 호출을 기록합니다. (Boxing 방지)</summary>
        public static void Record<T1>(NetworkBehaviour nb, string rpcName, T1 arg1)
        {
            if (networkSync == null || !networkSync.IsRecording || nb == null || nb.NetworkObject == null) return;
            byte[] serializedArgs = FishNetSerializationBridge.SerializeArgs(arg1);
            int replayId = networkSync.ResolveToReplayId(nb.NetworkObject.ObjectId);
            networkSync.RecordRpc(replayId, rpcName, serializedArgs);
        }

        /// <summary>2개의 인자를 가진 RPC 호출을 기록합니다. (Boxing 방지)</summary>
        public static void Record<T1, T2>(NetworkBehaviour nb, string rpcName, T1 arg1, T2 arg2)
        {
            if (networkSync == null || !networkSync.IsRecording || nb == null || nb.NetworkObject == null) return;
            byte[] serializedArgs = FishNetSerializationBridge.SerializeArgs(arg1, arg2);
            int replayId = networkSync.ResolveToReplayId(nb.NetworkObject.ObjectId);
            networkSync.RecordRpc(replayId, rpcName, serializedArgs);
        }

        /// <summary>3개의 인자를 가진 RPC 호출을 기록합니다. (Boxing 방지)</summary>
        public static void Record<T1, T2, T3>(NetworkBehaviour nb, string rpcName, T1 arg1, T2 arg2, T3 arg3)
        {
            if (networkSync == null || !networkSync.IsRecording || nb == null || nb.NetworkObject == null) return;
            byte[] serializedArgs = FishNetSerializationBridge.SerializeArgs(arg1, arg2, arg3);
            int replayId = networkSync.ResolveToReplayId(nb.NetworkObject.ObjectId);
            networkSync.RecordRpc(replayId, rpcName, serializedArgs);
        }

        /// <summary>4개의 인자를 가진 RPC 호출을 기록합니다. (Boxing 방지)</summary>
        public static void Record<T1, T2, T3, T4>(NetworkBehaviour nb, string rpcName, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            if (networkSync == null || !networkSync.IsRecording || nb == null || nb.NetworkObject == null) return;
            byte[] serializedArgs = FishNetSerializationBridge.SerializeArgs(arg1, arg2, arg3, arg4);
            int replayId = networkSync.ResolveToReplayId(nb.NetworkObject.ObjectId);
            networkSync.RecordRpc(replayId, rpcName, serializedArgs);
        }
    }
}
