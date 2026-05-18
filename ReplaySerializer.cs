using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using ReplaySystem.Compression;
using ReplaySystem.Core;
using UnityEngine;

namespace ReplaySystem.Serialization
{
    /// <summary>저장 결과</summary>
    public struct SaveResult
    {
        public bool Success;
        public string ErrorMessage;
        public string FilePath;
        public long FileSize;
        public float CompressionRatio;

        public static SaveResult Ok(string path, long size = 0, float ratio = 0f) =>
            new SaveResult { Success = true, FilePath = path, FileSize = size, CompressionRatio = ratio };
        public static SaveResult Fail(string error) => new SaveResult { Success = false, ErrorMessage = error };
    }

    /// <summary>리플레이 데이터 바이너리 직렬화입니다.</summary>
    public static class ReplaySerializer
    {
        /// <summary>기본 압축 타입</summary>
        public static ReCompressionType DefaultCompressionType { get; set; } = ReCompressionType.GZip;

        /// <summary>리플레이 데이터를 바이너리 파일로 저장합니다 (기본 압축 타입 사용).</summary>
        public static async UniTask<SaveResult> SaveAsync(ReplayData data, string filePath, CancellationToken ct)
        {
            return await SaveAsync(data, filePath, DefaultCompressionType, ct);
        }

        /// <summary>리플레이 데이터를 바이너리 파일로 저장합니다 (압축 타입 지정).</summary>
        public static async UniTask<SaveResult> SaveAsync(ReplayData data, string filePath, ReCompressionType compressionType, CancellationToken ct)
        {
            if (data == null)
            {
                return SaveResult.Fail("ReplayData is null");
            }

            // 메타데이터에 압축 타입 설정
            data.Metadata.Compression = compressionType;

            await UniTask.SwitchToThreadPool();

            try
            {
                var directory = Path.GetDirectoryName(filePath);

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                long uncompressedSize;
                long compressedSize;

                // 먼저 비압축 데이터 생성
                byte[] uncompressedData;
                using (var ms = new MemoryStream())
                using (var writer = new BinaryWriter(ms, Encoding.UTF8))
                {
                    WriteHeader(writer, data);
                    WriteMetadata(writer, data.Metadata);
                    WriteInitialState(writer, data.InitialState);
                    WriteChunkHeaders(writer, data.ChunkHeaders);
                    WriteEvents(writer, data.Events);
                    writer.Flush();
                    uncompressedData = ms.ToArray();
                }

                uncompressedSize = uncompressedData.Length;

                // 압축 적용
                byte[] compressedData = await CompressionHelper.CompressAsync(uncompressedData, compressionType, ct);
                compressedSize = compressedData.Length;

                // 파일에 쓰기
                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
                {
                    await fileStream.WriteAsync(compressedData, 0, compressedData.Length, ct);
                }

                float compressionRatio = uncompressedSize > 0 ? (float)compressedSize / uncompressedSize : 1f;

                Debug.Log($"[ReplaySerializer] Saved replay to: {filePath} ({compressionType}, {compressedSize:N0} bytes, {compressionRatio:P1} ratio)");
                return SaveResult.Ok(filePath, compressedSize, compressionRatio);
            }
            catch (IOException e)
            {
                Debug.LogError($"[ReplaySerializer] IO error during save: {e.Message}");
                return SaveResult.Fail($"IO error: {e.Message}");
            }
            catch (UnauthorizedAccessException e)
            {
                Debug.LogError($"[ReplaySerializer] Access denied: {e.Message}");
                return SaveResult.Fail($"Access denied: {e.Message}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ReplaySerializer] Unexpected error: {e.Message}");
                return SaveResult.Fail($"Unexpected error: {e.Message}");
            }
            finally
            {
                await UniTask.SwitchToMainThread(ct);
            }
        }

        /// <summary>리플레이 데이터를 byte 배열로 직렬화합니다 (기본 압축 타입 사용).</summary>
        public static byte[] SerializeToBytes(ReplayData data)
        {
            return SerializeToBytes(data, DefaultCompressionType);
        }

        /// <summary>리플레이 데이터를 byte 배열로 직렬화합니다 (압축 타입 지정).</summary>
        public static byte[] SerializeToBytes(ReplayData data, ReCompressionType compressionType)
        {
            data.Metadata.Compression = compressionType;

            byte[] uncompressedData;
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms, Encoding.UTF8))
            {
                WriteHeader(writer, data);
                WriteMetadata(writer, data.Metadata);
                WriteInitialState(writer, data.InitialState);
                WriteChunkHeaders(writer, data.ChunkHeaders);
                WriteEvents(writer, data.Events);
                writer.Flush();
                uncompressedData = ms.ToArray();
            }

            return CompressionHelper.Compress(uncompressedData, compressionType);
        }

        /// <summary>청크 데이터를 압축된 바이트 배열로 직렬화합니다.</summary>
        public static byte[] SerializeChunkToBytes(ReplayChunk chunk, ReCompressionType compressionType)
        {
            byte[] uncompressedData;
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms, Encoding.UTF8))
            {
                WriteChunk(writer, chunk);
                writer.Flush();
                uncompressedData = ms.ToArray();
            }

            return CompressionHelper.Compress(uncompressedData, compressionType);
        }

        /// <summary>헤더를 기록합니다.</summary>
        public static void WriteHeader(BinaryWriter writer, ReplayData data)
        {
            writer.Write(ReplayData.MAGIC);
            writer.Write(ReplayData.VERSION);
        }

        /// <summary>메타데이터를 기록합니다.</summary>
        public static void WriteMetadata(BinaryWriter writer, ReplayMetadata metadata)
        {
            writer.Write(metadata.RecordedAtTicks);
            writer.Write(metadata.Duration);
            writer.Write(metadata.FrameRate);
            writer.Write(metadata.KeyframeInterval);
            writer.Write(metadata.FramesPerChunk);
            writer.Write(metadata.SceneName ?? string.Empty);
            writer.Write(metadata.TrackedObjectCount);
            writer.Write(metadata.ChunkCount);
            writer.Write(metadata.HasPhysicsData);
            writer.Write(metadata.HasNetworkData);
            writer.Write(metadata.HasAnimatorData);
            writer.Write((byte)metadata.Compression);
        }

        /// <summary>초기 상태를 기록합니다.</summary>
        public static void WriteInitialState(BinaryWriter writer, SceneSnapshot snapshot)
        {
            writer.Write(snapshot?.Objects?.Count ?? 0);

            if (snapshot?.Objects != null)
            {
                foreach (var obj in snapshot.Objects)
                {
                    writer.Write(obj.Id);
                    writer.Write(obj.PrefabName ?? string.Empty);
                    writer.Write(obj.PrefabPath ?? string.Empty);
                    writer.Write(obj.HierarchyPath ?? string.Empty);
                    writer.Write(obj.IsNetworkObject);
                    WriteCompressedTransform(writer, obj.Transform);
                    writer.Write(obj.HasRigidbody);

                    if (obj.HasRigidbody)
                    {
                        WriteCompressedRigidbody(writer, obj.Rigidbody);
                    }

                    writer.Write(obj.HasAnimator);

                    if (obj.HasAnimator)
                    {
                        WriteCompressedAnimatorState(writer, obj.Animator);
                    }

                    WriteByteArray(writer, obj.CustomData);
                }
            }

            writer.Write(snapshot?.DynamicObjectPrefabs?.Count ?? 0);

            if (snapshot?.DynamicObjectPrefabs != null)
            {
                foreach (var prefab in snapshot.DynamicObjectPrefabs)
                {
                    writer.Write(prefab.PrefabPath ?? string.Empty);
                    writer.Write(prefab.PrefabHash);
                }
            }
        }

        /// <summary>청크 헤더를 기록합니다.</summary>
        public static void WriteChunkHeaders(BinaryWriter writer, List<ReplayChunkHeader> headers)
        {
            writer.Write(headers?.Count ?? 0);

            if (headers == null)
            {
                return;
            }

            foreach (var header in headers)
            {
                writer.Write(header.Index);
                writer.Write(header.StartFrame);
                writer.Write(header.EndFrame);
                writer.Write(header.StartTime);
                writer.Write(header.EndTime);
                writer.Write(header.FileOffset);
                writer.Write(header.CompressedSize);
                writer.Write(header.ContainsKeyframe);
            }
        }

        /// <summary>이벤트를 기록합니다.</summary>
        public static void WriteEvents(BinaryWriter writer, List<ReplayEvent> events)
        {
            writer.Write(events?.Count ?? 0);

            if (events == null)
            {
                return;
            }

            foreach (var evt in events)
            {
                writer.Write(evt.Time);
                writer.Write(evt.FrameIndex);
                writer.Write(evt.TargetObjectId);
                writer.Write((byte)evt.EventType);
                writer.Write(evt.EventName ?? string.Empty);
                writer.Write(evt.NetworkTick);
                writer.Write(evt.SourceClientId);
                WriteByteArray(writer, evt.Parameters);
            }
        }

        /// <summary>청크 데이터를 기록합니다.</summary>
        public static void WriteChunk(BinaryWriter writer, ReplayChunk chunk)
        {
            writer.Write(chunk.Keyframe != null);

            if (chunk.Keyframe != null)
            {
                WriteKeyframe(writer, chunk.Keyframe);
            }

            writer.Write(chunk.Frames.Count);

            foreach (var frame in chunk.Frames)
            {
                WriteFrame(writer, frame);
            }

            writer.Write(chunk.Events.Count);

            foreach (var evt in chunk.Events)
            {
                writer.Write(evt.Time);
                writer.Write(evt.FrameIndex);
                writer.Write(evt.TargetObjectId);
                writer.Write((byte)evt.EventType);
                writer.Write(evt.EventName ?? string.Empty);
                writer.Write(evt.NetworkTick);
                writer.Write(evt.SourceClientId);
                WriteByteArray(writer, evt.Parameters);
            }
        }

        /// <summary>키프레임 데이터를 기록합니다.</summary>
        public static void WriteKeyframe(BinaryWriter writer, ReplayKeyframe keyframe)
        {
            writer.Write(keyframe.FrameIndex);
            writer.Write(keyframe.Time);
            writer.Write(keyframe.States.Count);

            foreach (var state in keyframe.States)
            {
                writer.Write(state.ObjectId);
                WriteCompressedTransform(writer, state.Transform);
                writer.Write(state.HasRigidbody);

                if (state.HasRigidbody)
                {
                    WriteCompressedRigidbody(writer, state.Rigidbody);
                }

                writer.Write(state.HasAnimator);

                if (state.HasAnimator)
                {
                    WriteCompressedAnimatorState(writer, state.Animator);
                }
            }
        }

        /// <summary>프레임 데이터를 기록합니다.</summary>
        public static void WriteFrame(BinaryWriter writer, ReplayFrame frame)
        {
            writer.Write(frame.Index);
            writer.Write(frame.Time);
            writer.Write(frame.DeltaTime);
            writer.Write(frame.NetworkTick);

            writer.Write(frame.TransformDeltas?.Count ?? 0);

            if (frame.TransformDeltas != null)
            {
                foreach (var delta in frame.TransformDeltas)
                {
                    writer.Write(delta.ObjectId);
                    writer.Write((byte)delta.Flags);
                    WriteCompressedTransform(writer, delta.Transform);
                }
            }

            writer.Write(frame.RigidbodyDeltas?.Count ?? 0);

            if (frame.RigidbodyDeltas != null)
            {
                foreach (var delta in frame.RigidbodyDeltas)
                {
                    writer.Write(delta.ObjectId);
                    writer.Write(delta.HasLinearVelocity);
                    writer.Write(delta.HasAngularVelocity);
                    WriteCompressedRigidbody(writer, delta.Rigidbody);
                }
            }

            writer.Write(frame.AnimatorDeltas?.Count ?? 0);

            if (frame.AnimatorDeltas != null)
            {
                foreach (var delta in frame.AnimatorDeltas)
                {
                    writer.Write(delta.ObjectId);
                    WriteCompressedAnimatorState(writer, delta.State);
                }
            }
        }

        /// <summary>압축된 Transform을 기록합니다.</summary>
        public static void WriteCompressedTransform(BinaryWriter writer, CompressedTransform t)
        {
            writer.Write(t.PosX);
            writer.Write(t.PosY);
            writer.Write(t.PosZ);
            writer.Write(t.Rotation);
            writer.Write(t.ScaleX);
            writer.Write(t.ScaleY);
            writer.Write(t.ScaleZ);
            writer.Write(t.Flags);
        }

        /// <summary>압축된 Rigidbody를 기록합니다.</summary>
        public static void WriteCompressedRigidbody(BinaryWriter writer, CompressedRigidbody rb)
        {
            writer.Write(rb.VelX);
            writer.Write(rb.VelY);
            writer.Write(rb.VelZ);
            writer.Write(rb.AngVelX);
            writer.Write(rb.AngVelY);
            writer.Write(rb.AngVelZ);
        }

        /// <summary>압축된 Animator 상태를 기록합니다.</summary>
        public static void WriteCompressedAnimatorState(BinaryWriter writer, CompressedAnimatorState anim)
        {
            writer.Write(anim.LayerCount);

            if (anim.LayerCount > 0) WriteAnimatorLayerState(writer, anim.Layer0);
            if (anim.LayerCount > 1) WriteAnimatorLayerState(writer, anim.Layer1);
            if (anim.LayerCount > 2) WriteAnimatorLayerState(writer, anim.Layer2);
            if (anim.LayerCount > 3) WriteAnimatorLayerState(writer, anim.Layer3);

            WriteByteArray(writer, anim.ParameterData);
        }

        /// <summary>Animator 레이어 상태를 기록합니다.</summary>
        private static void WriteAnimatorLayerState(BinaryWriter writer, CompressedAnimatorLayerState layer)
        {
            writer.Write(layer.StateHash);
            writer.Write(layer.NormalizedTime);
            writer.Write(layer.Weight);
            writer.Write(layer.IsInTransition);

            if (layer.IsInTransition)
            {
                writer.Write(layer.NextStateHash);
                writer.Write(layer.TransitionNormalizedTime);
            }
        }

        /// <summary>byte 배열을 기록합니다.</summary>
        public static void WriteByteArray(BinaryWriter writer, byte[] data)
        {
            if (data == null)
            {
                writer.Write(-1);
                return;
            }

            writer.Write(data.Length);
            writer.Write(data);
        }
    }
}
