using FishNet.Serializing;
using UnityEngine;

namespace ReplaySystem.Network
{
    /// <summary>
    /// FishNet의 PooledWriter/PooledReader와 Replay System의 byte[] 간의
    /// 변환을 담당하는 직렬화 브릿지입니다.
    /// </summary>
    public static class FishNetSerializationBridge
    {
        /// <summary>값을 FishNet 직렬화 방식으로 byte 배열로 변환합니다.</summary>
        public static byte[] Serialize<T>(T value)
        {
            var writer = WriterPool.Retrieve();
            try
            {
                writer.Write(value);
                return writer.GetArraySegment().ToArray();
            }
            finally
            {
                writer.Store();
            }
        }

        /// <summary>튜플을 FishNet 직렬화 방식으로 byte 배열로 변환합니다.</summary>
        public static byte[] Serialize<T1, T2>(T1 value1, T2 value2)
        {
            var writer = WriterPool.Retrieve();
            try
            {
                writer.Write(value1);
                writer.Write(value2);
                return writer.GetArraySegment().ToArray();
            }
            finally
            {
                writer.Store();
            }
        }

        /// <summary>이름과 값을 튜플 형태로 직렬화합니다 (디버깅/식별 용이).</summary>
        public static byte[] SerializeWithName<T>(string name, T value)
        {
            var writer = WriterPool.Retrieve();
            try
            {
                writer.Write(name);
                writer.Write(value);
                return writer.GetArraySegment().ToArray();
            }
            finally
            {
                writer.Store();
            }
        }

        /// <summary>byte 배열에서 FishNet 직렬화 방식으로 값을 복원합니다.</summary>
        public static T Deserialize<T>(byte[] data)
        {
            if (data == null || data.Length == 0) return default;
            
            var reader = ReaderPool.Retrieve(data, null);
            try
            {
                return reader.Read<T>();
            }
            finally
            {
                reader.Store();
            }
        }
        
        /// <summary>인자가 없는 RPC 기록용</summary>
        public static byte[] SerializeArgs()
        {
            var writer = WriterPool.Retrieve();
            try
            {
                writer.Write((byte)0);
                return writer.GetArraySegment().ToArray();
            }
            finally
            {
                writer.Store();
            }
        }

        /// <summary>1개의 인자를 가지는 RPC 기록용 (Boxing 방지)</summary>
        public static byte[] SerializeArgs<T1>(T1 arg1)
        {
            var writer = WriterPool.Retrieve();
            try
            {
                writer.Write((byte)1);
                writer.Write(arg1);
                return writer.GetArraySegment().ToArray();
            }
            finally
            {
                writer.Store();
            }
        }

        /// <summary>2개의 인자를 가지는 RPC 기록용 (Boxing 방지)</summary>
        public static byte[] SerializeArgs<T1, T2>(T1 arg1, T2 arg2)
        {
            var writer = WriterPool.Retrieve();
            try
            {
                writer.Write((byte)2);
                writer.Write(arg1);
                writer.Write(arg2);
                return writer.GetArraySegment().ToArray();
            }
            finally
            {
                writer.Store();
            }
        }

        /// <summary>3개의 인자를 가지는 RPC 기록용 (Boxing 방지)</summary>
        public static byte[] SerializeArgs<T1, T2, T3>(T1 arg1, T2 arg2, T3 arg3)
        {
            var writer = WriterPool.Retrieve();
            try
            {
                writer.Write((byte)3);
                writer.Write(arg1);
                writer.Write(arg2);
                writer.Write(arg3);
                return writer.GetArraySegment().ToArray();
            }
            finally
            {
                writer.Store();
            }
        }
        
        /// <summary>4개의 인자를 가지는 RPC 기록용 (Boxing 방지)</summary>
        public static byte[] SerializeArgs<T1, T2, T3, T4>(T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            var writer = WriterPool.Retrieve();
            try
            {
                writer.Write((byte)4);
                writer.Write(arg1);
                writer.Write(arg2);
                writer.Write(arg3);
                writer.Write(arg4);
                return writer.GetArraySegment().ToArray();
            }
            finally
            {
                writer.Store();
            }
        }
    }
}
