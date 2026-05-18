using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using ReplaySystem.Compression;
using ReplaySystem.Core;
using UnityEngine;

namespace ReplaySystem.Serialization
{
    /// <summary>역직렬화 결과</summary>
    public readonly struct LoadResult
    {
        public readonly bool Success;
        public readonly ReplayData Data;
        public readonly string ErrorMessage;
        public readonly ReCompressionType DetectedCompression;

        public LoadResult(ReplayData data, ReCompressionType compression = ReCompressionType.None)
        {
            Success = true;
            Data = data;
            ErrorMessage = null;
            DetectedCompression = compression;
        }

        public LoadResult(string errorMessage)
        {
            Success = false;
            Data = null;
            ErrorMessage = errorMessage;
            DetectedCompression = ReCompressionType.None;
        }
    }

    /// <summary>리플레이 데이터 바이너리 역직렬화입니다.</summary>
    public static class ReplayDeserializer
    {
        /// <summary>바이너리 파일에서 리플레이 데이터를 로드합니다 (압축 자동 감지).</summary>
        public static async UniTask<LoadResult> LoadAsync(string filePath, CancellationToken ct)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError($"[ReplayDeserializer] File not found: {filePath}");
                return new LoadResult($"File not found: {filePath}");
            }

            await UniTask.SwitchToThreadPool();

            try
            {
                // 파일 전체 읽기
                byte[] fileData = await ReadFileAsync(filePath, ct);

                // 압축 타입 자동 감지
                var compressionType = CompressionHelper.DetectCompressionType(fileData);
                Debug.Log($"[ReplayDeserializer] Detected compression: {compressionType}");

                // 압축 해제
                byte[] decompressedData = await CompressionHelper.DecompressAsync(fileData, compressionType, ct);

                // 역직렬화
                using (var ms = new MemoryStream(decompressedData))
                using (var reader = new BinaryReader(ms, Encoding.UTF8))
                {
                    var replayData = ReadReplayData(reader);
                    return new LoadResult(replayData, compressionType);
                }
            }
            catch (IOException e)
            {
                Debug.LogError($"[ReplayDeserializer] IO error loading replay: {e.Message}");
                return new LoadResult($"IO error: {e.Message}");
            }
            catch (InvalidDataException e)
            {
                Debug.LogError($"[ReplayDeserializer] Invalid replay data: {e.Message}");
                return new LoadResult($"Invalid data: {e.Message}");
            }
            catch (UnauthorizedAccessException e)
            {
                Debug.LogError($"[ReplayDeserializer] Access denied: {e.Message}");
                return new LoadResult($"Access denied: {e.Message}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ReplayDeserializer] Unexpected error: {e.Message}");
                return new LoadResult($"Unexpected error: {e.Message}");
            }
            finally
            {
                await UniTask.SwitchToMainThread(ct);
            }
        }

        /// <summary>헤더만 로드합니다 (청크 스트리밍용, 압축 자동 감지)</summary>
        public static async UniTask<LoadResult> LoadHeaderAsync(string filePath, CancellationToken ct)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError($"[ReplayDeserializer] File not found: {filePath}");
                return new LoadResult($"File not found: {filePath}");
            }

            await UniTask.SwitchToThreadPool();

            try
            {
                // 파일 전체 읽기
                byte[] fileData = await ReadFileAsync(filePath, ct);

                // 압축 타입 자동 감지
                var compressionType = CompressionHelper.DetectCompressionType(fileData);

                // 압축 해제
                byte[] decompressedData = await CompressionHelper.DecompressAsync(fileData, compressionType, ct);

                // 역직렬화
                using (var ms = new MemoryStream(decompressedData))
                using (var reader = new BinaryReader(ms, Encoding.UTF8))
                {
                    var replayData = ReadReplayData(reader);
                    return new LoadResult(replayData, compressionType);
                }
            }
            catch (IOException e)
            {
                Debug.LogError($"[ReplayDeserializer] IO error loading header: {e.Message}");
                return new LoadResult($"IO error: {e.Message}");
            }
            catch (InvalidDataException e)
            {
                Debug.LogError($"[ReplayDeserializer] Invalid replay header: {e.Message}");
                return new LoadResult($"Invalid data: {e.Message}");
            }
            catch (UnauthorizedAccessException e)
            {
                Debug.LogError($"[ReplayDeserializer] Access denied: {e.Message}");
                return new LoadResult($"Access denied: {e.Message}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ReplayDeserializer] Unexpected error: {e.Message}");
                return new LoadResult($"Unexpected error: {e.Message}");
            }
            finally
            {
                await UniTask.SwitchToMainThread(ct);
            }
        }

        /// <summary>byte 배열에서 리플레이 데이터를 역직렬화합니다 (압축 자동 감지).</summary>
        public static ReplayData DeserializeFromBytes(byte[] data)
        {
            var compressionType = CompressionHelper.DetectCompressionType(data);
            byte[] decompressedData = CompressionHelper.Decompress(data, compressionType);

            using (var ms = new MemoryStream(decompressedData))
            using (var reader = new BinaryReader(ms, Encoding.UTF8))
            {
                return ReadReplayData(reader);
            }
        }

        /// <summary>청크 데이터를 역직렬화합니다.</summary>
        public static ReplayChunk DeserializeChunkFromBytes(byte[] data, int chunkIndex, ReCompressionType compressionType)
        {
            byte[] decompressedData = CompressionHelper.Decompress(data, compressionType);

            using (var ms = new MemoryStream(decompressedData))
            using (var reader = new BinaryReader(ms, Encoding.UTF8))
            {
                return ReadChunk(reader, chunkIndex);
            }
        }

        /// <summary>파일을 비동기로 읽습니다.</summary>
        private static async Task<byte[]> ReadFileAsync(string filePath, CancellationToken ct)
        {
            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
            {
                byte[] data = new byte[fileStream.Length];
                await fileStream.ReadAsync(data, 0, data.Length, ct);
                return data;
            }
        }

        /// <summary>청크 데이터를 읽습니다.</summary>
        public static ReplayChunk ReadChunk(BinaryReader reader, int index)
        {
            var chunk = new ReplayChunk { Index = index };

            bool hasKeyframe = reader.ReadBoolean();

            if (hasKeyframe)
            {
                chunk.Keyframe = ReadKeyframe(reader);
            }

            int frameCount = reader.ReadInt32();
            chunk.Frames = new List<ReplayFrame>(frameCount);

            for (int i = 0; i < frameCount; i++)
            {
                chunk.Frames.Add(ReadFrame(reader));
            }

            int eventCount = reader.ReadInt32();
            chunk.Events = new List<ReplayEvent>(eventCount);

            for (int i = 0; i < eventCount; i++)
            {
                chunk.Events.Add(ReadEvent(reader));
            }

            return chunk;
        }

        private static ReplayKeyframe ReadKeyframe(BinaryReader reader)
        {
            var keyframe = new ReplayKeyframe
            {
                FrameIndex = reader.ReadInt32(),
                Time = reader.ReadSingle()
            };

            int stateCount = reader.ReadInt32();
            keyframe.States = new List<CompressedObjectState>(stateCount);

            for (int i = 0; i < stateCount; i++)
            {
                keyframe.States.Add(ReadCompressedObjectState(reader));
            }

            return keyframe;
        }

        private static CompressedObjectState ReadCompressedObjectState(BinaryReader reader)
        {
            var state = new CompressedObjectState
            {
                ObjectId = reader.ReadInt32(),
                Transform = ReadCompressedTransform(reader),
                HasRigidbody = reader.ReadBoolean()
            };

            if (state.HasRigidbody)
            {
                state.Rigidbody = ReadCompressedRigidbody(reader);
            }

            state.HasAnimator = reader.ReadBoolean();

            if (state.HasAnimator)
            {
                state.Animator = ReadCompressedAnimatorState(reader);
            }

            return state;
        }

        private static ReplayFrame ReadFrame(BinaryReader reader)
        {
            var frame = new ReplayFrame
            {
                Index = reader.ReadInt32(),
                Time = reader.ReadSingle(),
                DeltaTime = reader.ReadSingle(),
                NetworkTick = reader.ReadUInt32()
            };

            int transformDeltaCount = reader.ReadInt32();
            frame.TransformDeltas = new List<CompressedTransformDelta>(transformDeltaCount);

            for (int i = 0; i < transformDeltaCount; i++)
            {
                frame.TransformDeltas.Add(ReadTransformDelta(reader));
            }

            int rigidbodyDeltaCount = reader.ReadInt32();
            frame.RigidbodyDeltas = new List<CompressedRigidbodyDelta>(rigidbodyDeltaCount);

            for (int i = 0; i < rigidbodyDeltaCount; i++)
            {
                frame.RigidbodyDeltas.Add(ReadRigidbodyDelta(reader));
            }

            int animatorDeltaCount = reader.ReadInt32();
            frame.AnimatorDeltas = new List<CompressedAnimatorDelta>(animatorDeltaCount);

            for (int i = 0; i < animatorDeltaCount; i++)
            {
                frame.AnimatorDeltas.Add(ReadAnimatorDelta(reader));
            }

            return frame;
        }

        private static CompressedTransformDelta ReadTransformDelta(BinaryReader reader)
        {
            var delta = new CompressedTransformDelta
            {
                ObjectId = reader.ReadInt32(),
                Flags = (TransformDeltaFlags)reader.ReadByte()
            };

            if (delta.Flags != TransformDeltaFlags.None)
            {
                delta.Transform = ReadCompressedTransform(reader);
            }

            return delta;
        }

        private static CompressedRigidbodyDelta ReadRigidbodyDelta(BinaryReader reader)
        {
            return new CompressedRigidbodyDelta
            {
                ObjectId = reader.ReadInt32(),
                HasLinearVelocity = reader.ReadBoolean(),
                HasAngularVelocity = reader.ReadBoolean(),
                Rigidbody = ReadCompressedRigidbody(reader)
            };
        }

        private static ReplayEvent ReadEvent(BinaryReader reader)
        {
            var evt = new ReplayEvent
            {
                Time = reader.ReadSingle(),
                FrameIndex = reader.ReadInt32(),
                TargetObjectId = reader.ReadInt32(),
                EventType = (ReplayEventType)reader.ReadByte(),
                EventName = reader.ReadString(),
                NetworkTick = reader.ReadUInt32(),
                SourceClientId = reader.ReadInt32()
            };

            int paramLength = reader.ReadInt32();

            if (paramLength > 0)
            {
                evt.Parameters = reader.ReadBytes(paramLength);
            }

            return evt;
        }

        private static ReplayData ReadReplayData(BinaryReader reader)
        {
            var replayData = new ReplayData();

            uint magic = reader.ReadUInt32();

            if (magic != ReplayData.MAGIC)
            {
                throw new InvalidDataException("Invalid replay file format");
            }

            int version = reader.ReadInt32();

            if (version > ReplayData.VERSION)
            {
                throw new InvalidDataException($"Unsupported replay version: {version}");
            }

            replayData.Metadata = ReadMetadata(reader);
            replayData.InitialState = ReadInitialState(reader);
            replayData.ChunkHeaders = ReadChunkHeaders(reader);
            replayData.Events = ReadEvents(reader);

            return replayData;
        }

        private static ReplayMetadata ReadMetadata(BinaryReader reader)
        {
            return new ReplayMetadata
            {
                RecordedAtTicks = reader.ReadInt64(),
                Duration = reader.ReadSingle(),
                FrameRate = reader.ReadInt32(),
                KeyframeInterval = reader.ReadInt32(),
                FramesPerChunk = reader.ReadInt32(),
                SceneName = reader.ReadString(),
                TrackedObjectCount = reader.ReadInt32(),
                ChunkCount = reader.ReadInt32(),
                HasPhysicsData = reader.ReadBoolean(),
                HasNetworkData = reader.ReadBoolean(),
                HasAnimatorData = reader.ReadBoolean(),
                Compression = (ReCompressionType)reader.ReadByte()
            };
        }

        private static SceneSnapshot ReadInitialState(BinaryReader reader)
        {
            var snapshot = new SceneSnapshot();
            int objectCount = reader.ReadInt32();

            for (int i = 0; i < objectCount; i++)
            {
                var state = new ObjectState
                {
                    Id = reader.ReadInt32(),
                    PrefabName = reader.ReadString(),
                    PrefabPath = reader.ReadString(),
                    HierarchyPath = reader.ReadString(),
                    IsNetworkObject = reader.ReadBoolean(),
                    Transform = ReadCompressedTransform(reader),
                    HasRigidbody = reader.ReadBoolean()
                };

                if (state.HasRigidbody)
                {
                    state.Rigidbody = ReadCompressedRigidbody(reader);
                }

                state.HasAnimator = reader.ReadBoolean();

                if (state.HasAnimator)
                {
                    state.Animator = ReadCompressedAnimatorState(reader);
                }

                state.CustomData = ReadByteArray(reader);
                snapshot.Objects.Add(state);
            }

            int prefabCount = reader.ReadInt32();

            for (int i = 0; i < prefabCount; i++)
            {
                snapshot.DynamicObjectPrefabs.Add(new DynamicObjectInfo
                {
                    PrefabPath = reader.ReadString(),
                    PrefabHash = reader.ReadInt32()
                });
            }

            return snapshot;
        }

        private static List<ReplayChunkHeader> ReadChunkHeaders(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            var headers = new List<ReplayChunkHeader>(count);

            for (int i = 0; i < count; i++)
            {
                headers.Add(new ReplayChunkHeader
                {
                    Index = reader.ReadInt32(),
                    StartFrame = reader.ReadInt32(),
                    EndFrame = reader.ReadInt32(),
                    StartTime = reader.ReadSingle(),
                    EndTime = reader.ReadSingle(),
                    FileOffset = reader.ReadInt64(),
                    CompressedSize = reader.ReadInt32(),
                    ContainsKeyframe = reader.ReadBoolean()
                });
            }

            return headers;
        }

        private static List<ReplayEvent> ReadEvents(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            var events = new List<ReplayEvent>(count);

            for (int i = 0; i < count; i++)
            {
                events.Add(new ReplayEvent
                {
                    Time = reader.ReadSingle(),
                    FrameIndex = reader.ReadInt32(),
                    TargetObjectId = reader.ReadInt32(),
                    EventType = (ReplayEventType)reader.ReadByte(),
                    EventName = reader.ReadString(),
                    NetworkTick = reader.ReadUInt32(),
                    SourceClientId = reader.ReadInt32(),
                    Parameters = ReadByteArray(reader)
                });
            }

            return events;
        }

        /// <summary>압축된 Transform을 읽습니다.</summary>
        public static CompressedTransform ReadCompressedTransform(BinaryReader reader)
        {
            return new CompressedTransform
            {
                PosX = reader.ReadUInt16(),
                PosY = reader.ReadUInt16(),
                PosZ = reader.ReadUInt16(),
                Rotation = reader.ReadUInt32(),
                ScaleX = reader.ReadByte(),
                ScaleY = reader.ReadByte(),
                ScaleZ = reader.ReadByte(),
                Flags = reader.ReadByte()
            };
        }

        /// <summary>압축된 Rigidbody를 읽습니다.</summary>
        public static CompressedRigidbody ReadCompressedRigidbody(BinaryReader reader)
        {
            return new CompressedRigidbody
            {
                VelX = reader.ReadInt16(),
                VelY = reader.ReadInt16(),
                VelZ = reader.ReadInt16(),
                AngVelX = reader.ReadInt16(),
                AngVelY = reader.ReadInt16(),
                AngVelZ = reader.ReadInt16()
            };
        }

        /// <summary>압축된 Animator 상태를 읽습니다.</summary>
        public static CompressedAnimatorState ReadCompressedAnimatorState(BinaryReader reader)
        {
            var state = new CompressedAnimatorState
            {
                LayerCount = reader.ReadByte()
            };

            if (state.LayerCount > 0) state.Layer0 = ReadAnimatorLayerState(reader);
            if (state.LayerCount > 1) state.Layer1 = ReadAnimatorLayerState(reader);
            if (state.LayerCount > 2) state.Layer2 = ReadAnimatorLayerState(reader);
            if (state.LayerCount > 3) state.Layer3 = ReadAnimatorLayerState(reader);

            state.ParameterData = ReadByteArray(reader);

            return state;
        }

        /// <summary>Animator 레이어 상태를 읽습니다.</summary>
        private static CompressedAnimatorLayerState ReadAnimatorLayerState(BinaryReader reader)
        {
            var layer = new CompressedAnimatorLayerState
            {
                StateHash = reader.ReadInt32(),
                NormalizedTime = reader.ReadUInt16(),
                Weight = reader.ReadByte(),
                IsInTransition = reader.ReadBoolean()
            };

            if (layer.IsInTransition)
            {
                layer.NextStateHash = reader.ReadInt32();
                layer.TransitionNormalizedTime = reader.ReadUInt16();
            }

            return layer;
        }

        /// <summary>Animator 델타를 읽습니다.</summary>
        private static CompressedAnimatorDelta ReadAnimatorDelta(BinaryReader reader)
        {
            return new CompressedAnimatorDelta
            {
                ObjectId = reader.ReadInt32(),
                State = ReadCompressedAnimatorState(reader)
            };
        }

        /// <summary>byte 배열을 읽습니다.</summary>
        public static byte[] ReadByteArray(BinaryReader reader)
        {
            int length = reader.ReadInt32();

            if (length < 0)
            {
                return null;
            }

            return reader.ReadBytes(length);
        }
    }
}
