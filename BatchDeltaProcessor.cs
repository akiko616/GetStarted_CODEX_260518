using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using ReplaySystem.Core;

namespace ReplaySystem.Compression
{
    /// <summary>Burst/Jobs 기반 배치 델타 처리기입니다.</summary>
    public class BatchDeltaProcessor : IDisposable
    {
        #region Constants

        private const int DEFAULT_CAPACITY = 1024;
        private const float DEFAULT_POSITION_THRESHOLD_SQ = 0.001f * 0.001f;
        private const float DEFAULT_VELOCITY_THRESHOLD_SQ = 0.01f * 0.01f;

        #endregion

        #region Native Arrays

        private NativeArray<TransformData> currentTransforms;
        private NativeArray<TransformData> previousTransforms;
        private NativeArray<RigidbodyData> currentRigidbodies;
        private NativeArray<RigidbodyData> previousRigidbodies;
        private NativeArray<DeltaResult> transformResults;
        private NativeArray<DeltaResult> rigidbodyResults;
        private NativeArray<int> objectIds;
        private NativeArray<byte> hasRigidbody;

        #endregion

        #region Settings

        private readonly float positionThresholdSq;
        private readonly float velocityThresholdSq;

        #endregion

        #region State

        private int capacity;
        private int activeCount;
        private bool isInitialized;
        private bool isDisposed;

        #endregion

        /// <summary>현재 용량</summary>
        public int Capacity => capacity;

        /// <summary>활성 오브젝트 수</summary>
        public int ActiveCount => activeCount;

        /// <summary>초기화 여부</summary>
        public bool IsInitialized => isInitialized;

        public BatchDeltaProcessor(int initialCapacity = DEFAULT_CAPACITY,
            float posThreshold = 0.001f, float velThreshold = 0.01f)
        {
            capacity = initialCapacity;
            positionThresholdSq = posThreshold * posThreshold;
            velocityThresholdSq = velThreshold * velThreshold;

            AllocateArrays(capacity);
            isInitialized = true;
        }

        #region Public Methods

        /// <summary>활성 오브젝트 수 설정 (매 프레임 호출)</summary>
        public void SetActiveCount(int count)
        {
            if (count > capacity)
            {
                Resize(math.max(count, capacity * 2));
            }

            activeCount = count;
        }

        /// <summary>Transform 데이터 설정 (메인 스레드에서 호출)</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetTransformData(int index, int objectId, Vector3 position, Quaternion rotation,
            Vector3 scale, bool isActive)
        {
            objectIds[index] = objectId;
            currentTransforms[index] = new TransformData
            {
                PosX = (ushort)math.f32tof16(position.x),
                PosY = (ushort)math.f32tof16(position.y),
                PosZ = (ushort)math.f32tof16(position.z),
                Rotation = PackQuaternionSmallest3(rotation),
                ScaleX = QuantizeScale(scale.x),
                ScaleY = QuantizeScale(scale.y),
                ScaleZ = QuantizeScale(scale.z),
                Flags = (byte)(isActive ? 1 : 0)
            };
        }

        /// <summary>Rigidbody 데이터 설정</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetRigidbodyData(int index, Vector3 velocity, Vector3 angularVelocity)
        {
            hasRigidbody[index] = 1;
            currentRigidbodies[index] = new RigidbodyData
            {
                VelX = QuantizeVelocity(velocity.x),
                VelY = QuantizeVelocity(velocity.y),
                VelZ = QuantizeVelocity(velocity.z),
                AngVelX = QuantizeVelocity(angularVelocity.x, 50f),
                AngVelY = QuantizeVelocity(angularVelocity.y, 50f),
                AngVelZ = QuantizeVelocity(angularVelocity.z, 50f)
            };
        }

        /// <summary>Rigidbody 없음 표시</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetNoRigidbody(int index)
        {
            hasRigidbody[index] = 0;
        }

        /// <summary>델타 계산 Job 스케줄 (멀티스레드 실행)</summary>
        public JobHandle ScheduleDeltaComputation(JobHandle dependency = default)
        {
            var transformJob = new ComputeTransformDeltasJob
            {
                Current = currentTransforms,
                Previous = previousTransforms,
                Results = transformResults,
                PositionThresholdSq = positionThresholdSq
            };

            var rigidbodyJob = new ComputeRigidbodyDeltasJob
            {
                Current = currentRigidbodies,
                Previous = previousRigidbodies,
                HasRigidbody = hasRigidbody,
                Results = rigidbodyResults,
                VelocityThresholdSq = velocityThresholdSq
            };

            // Transform과 Rigidbody 델타 계산을 병렬로 실행
            var transformHandle = transformJob.Schedule(activeCount, 64, dependency);
            var rigidbodyHandle = rigidbodyJob.Schedule(activeCount, 64, dependency);

            return JobHandle.CombineDependencies(transformHandle, rigidbodyHandle);
        }

        /// <summary>델타 결과 수집 (Job 완료 후 메인 스레드에서 호출)</summary>
        public void CollectResults(
            Action<int, CompressedTransformDelta> onTransformDelta,
            Action<int, CompressedRigidbodyDelta> onRigidbodyDelta)
        {
            for (int i = 0; i < activeCount; i++)
            {
                var tResult = transformResults[i];

                if (tResult.HasDelta != 0)
                {
                    var current = currentTransforms[i];
                    var delta = new CompressedTransformDelta
                    {
                        ObjectId = objectIds[i],
                        Flags = (TransformDeltaFlags)tResult.Flags,
                        Transform = new CompressedTransform
                        {
                            PosX = current.PosX,
                            PosY = current.PosY,
                            PosZ = current.PosZ,
                            Rotation = current.Rotation,
                            ScaleX = current.ScaleX,
                            ScaleY = current.ScaleY,
                            ScaleZ = current.ScaleZ,
                            Flags = current.Flags
                        }
                    };
                    onTransformDelta?.Invoke(objectIds[i], delta);
                }

                if (hasRigidbody[i] != 0)
                {
                    var rbResult = rigidbodyResults[i];

                    if (rbResult.HasDelta != 0)
                    {
                        var current = currentRigidbodies[i];
                        var delta = new CompressedRigidbodyDelta
                        {
                            ObjectId = objectIds[i],
                            HasLinearVelocity = (rbResult.Flags & 1) != 0,
                            HasAngularVelocity = (rbResult.Flags & 2) != 0,
                            Rigidbody = new CompressedRigidbody
                            {
                                VelX = current.VelX,
                                VelY = current.VelY,
                                VelZ = current.VelZ,
                                AngVelX = current.AngVelX,
                                AngVelY = current.AngVelY,
                                AngVelZ = current.AngVelZ
                            }
                        };
                        onRigidbodyDelta?.Invoke(objectIds[i], delta);
                    }
                }
            }
        }

        /// <summary>프레임 종료 - 현재 상태를 이전 상태로 복사</summary>
        public JobHandle SwapBuffers(JobHandle dependency = default)
        {
            var copyTransformJob = new CopyArrayJob<TransformData>
            {
                Source = currentTransforms,
                Destination = previousTransforms
            };

            var copyRigidbodyJob = new CopyArrayJob<RigidbodyData>
            {
                Source = currentRigidbodies,
                Destination = previousRigidbodies
            };

            var h1 = copyTransformJob.Schedule(activeCount, 128, dependency);
            var h2 = copyRigidbodyJob.Schedule(activeCount, 128, dependency);

            return JobHandle.CombineDependencies(h1, h2);
        }

        /// <summary>상태 리셋 (녹화 시작 시)</summary>
        public void Reset()
        {
            activeCount = 0;

            // 이전 상태 초기화
            for (int i = 0; i < capacity; i++)
            {
                previousTransforms[i] = default;
                previousRigidbodies[i] = default;
            }
        }

        #endregion

        #region Private Methods

        private void AllocateArrays(int size)
        {
            currentTransforms = new NativeArray<TransformData>(size, Allocator.Persistent);
            previousTransforms = new NativeArray<TransformData>(size, Allocator.Persistent);
            currentRigidbodies = new NativeArray<RigidbodyData>(size, Allocator.Persistent);
            previousRigidbodies = new NativeArray<RigidbodyData>(size, Allocator.Persistent);
            transformResults = new NativeArray<DeltaResult>(size, Allocator.Persistent);
            rigidbodyResults = new NativeArray<DeltaResult>(size, Allocator.Persistent);
            objectIds = new NativeArray<int>(size, Allocator.Persistent);
            hasRigidbody = new NativeArray<byte>(size, Allocator.Persistent);
        }

        private void Resize(int newSize)
        {
            Debug.Log($"[BatchDeltaProcessor] Resizing from {capacity} to {newSize}");

            var newCurrentTransforms = new NativeArray<TransformData>(newSize, Allocator.Persistent);
            var newPreviousTransforms = new NativeArray<TransformData>(newSize, Allocator.Persistent);
            var newCurrentRigidbodies = new NativeArray<RigidbodyData>(newSize, Allocator.Persistent);
            var newPreviousRigidbodies = new NativeArray<RigidbodyData>(newSize, Allocator.Persistent);
            var newTransformResults = new NativeArray<DeltaResult>(newSize, Allocator.Persistent);
            var newRigidbodyResults = new NativeArray<DeltaResult>(newSize, Allocator.Persistent);
            var newObjectIds = new NativeArray<int>(newSize, Allocator.Persistent);
            var newHasRigidbody = new NativeArray<byte>(newSize, Allocator.Persistent);

            // 기존 데이터 복사
            NativeArray<TransformData>.Copy(currentTransforms, newCurrentTransforms, capacity);
            NativeArray<TransformData>.Copy(previousTransforms, newPreviousTransforms, capacity);
            NativeArray<RigidbodyData>.Copy(currentRigidbodies, newCurrentRigidbodies, capacity);
            NativeArray<RigidbodyData>.Copy(previousRigidbodies, newPreviousRigidbodies, capacity);
            NativeArray<int>.Copy(objectIds, newObjectIds, capacity);
            NativeArray<byte>.Copy(hasRigidbody, newHasRigidbody, capacity);

            // 기존 배열 해제
            DisposeArrays();

            // 새 배열 할당
            currentTransforms = newCurrentTransforms;
            previousTransforms = newPreviousTransforms;
            currentRigidbodies = newCurrentRigidbodies;
            previousRigidbodies = newPreviousRigidbodies;
            transformResults = newTransformResults;
            rigidbodyResults = newRigidbodyResults;
            objectIds = newObjectIds;
            hasRigidbody = newHasRigidbody;

            capacity = newSize;
        }

        private void DisposeArrays()
        {
            if (currentTransforms.IsCreated) currentTransforms.Dispose();
            if (previousTransforms.IsCreated) previousTransforms.Dispose();
            if (currentRigidbodies.IsCreated) currentRigidbodies.Dispose();
            if (previousRigidbodies.IsCreated) previousRigidbodies.Dispose();
            if (transformResults.IsCreated) transformResults.Dispose();
            if (rigidbodyResults.IsCreated) rigidbodyResults.Dispose();
            if (objectIds.IsCreated) objectIds.Dispose();
            if (hasRigidbody.IsCreated) hasRigidbody.Dispose();
        }

        #endregion

        #region Quantization Helpers

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint PackQuaternionSmallest3(Quaternion q)
        {
            float4 components = new float4(q.x, q.y, q.z, q.w);
            float4 absComponents = math.abs(components);

            int largestIndex = 0;
            float largestValue = absComponents.x;

            if (absComponents.y > largestValue) { largestIndex = 1; largestValue = absComponents.y; }
            if (absComponents.z > largestValue) { largestIndex = 2; largestValue = absComponents.z; }
            if (absComponents.w > largestValue) { largestIndex = 3; }

            if (components[largestIndex] < 0)
            {
                components = -components;
            }

            const float SQRT2_OVER_2 = 0.7071068f;
            const int BITS_PER_COMPONENT = 10;
            const int MAX_VALUE = (1 << BITS_PER_COMPONENT) - 1;

            uint packed = (uint)largestIndex;
            int shift = 2;

            for (int i = 0; i < 4; i++)
            {
                if (i == largestIndex) continue;

                float normalized = (components[i] / SQRT2_OVER_2 + 1f) * 0.5f;
                uint quantized = (uint)math.clamp(math.round(normalized * MAX_VALUE), 0, MAX_VALUE);
                packed |= quantized << shift;
                shift += BITS_PER_COMPONENT;
            }

            return packed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte QuantizeScale(float scale)
        {
            float log = math.log10(math.clamp(scale, 0.01f, 10f));
            float normalized = (log + 2f) / 3f;
            return (byte)(normalized * 255f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static short QuantizeVelocity(float velocity, float maxVelocity = 500f)
        {
            float normalized = math.clamp(velocity / maxVelocity, -1f, 1f);
            return (short)(normalized * short.MaxValue);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (isDisposed) return;

            isDisposed = true;
            isInitialized = false;

            DisposeArrays();

            Debug.Log("[BatchDeltaProcessor] Disposed native arrays");
        }

        #endregion
    }

    #region Native Data Structures

    /// <summary>Transform 데이터 (Burst 호환)</summary>
    public struct TransformData
    {
        public ushort PosX;
        public ushort PosY;
        public ushort PosZ;
        public uint Rotation;
        public byte ScaleX;
        public byte ScaleY;
        public byte ScaleZ;
        public byte Flags;
    }

    /// <summary>Rigidbody 데이터 (Burst 호환)</summary>
    public struct RigidbodyData
    {
        public short VelX;
        public short VelY;
        public short VelZ;
        public short AngVelX;
        public short AngVelY;
        public short AngVelZ;
    }

    /// <summary>델타 계산 결과</summary>
    public struct DeltaResult
    {
        public byte HasDelta;
        public byte Flags;
    }

    #endregion

    #region Burst Jobs

    /// <summary>Transform 델타 계산 Job</summary>
    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast)]
    public struct ComputeTransformDeltasJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<TransformData> Current;
        [ReadOnly] public NativeArray<TransformData> Previous;
        [WriteOnly] public NativeArray<DeltaResult> Results;
        public float PositionThresholdSq;

        public void Execute(int index)
        {
            var curr = Current[index];
            var prev = Previous[index];

            byte flags = 0;

            // Position 비교
            float dx = math.f16tof32(curr.PosX) - math.f16tof32(prev.PosX);
            float dy = math.f16tof32(curr.PosY) - math.f16tof32(prev.PosY);
            float dz = math.f16tof32(curr.PosZ) - math.f16tof32(prev.PosZ);
            float posDiffSq = dx * dx + dy * dy + dz * dz;

            if (posDiffSq > PositionThresholdSq)
            {
                flags |= (byte)TransformDeltaFlags.Position;
            }

            // Rotation 비교
            if (curr.Rotation != prev.Rotation)
            {
                flags |= (byte)TransformDeltaFlags.Rotation;
            }

            // Scale 비교
            if (curr.ScaleX != prev.ScaleX || curr.ScaleY != prev.ScaleY || curr.ScaleZ != prev.ScaleZ)
            {
                flags |= (byte)TransformDeltaFlags.Scale;
            }

            // Active 비교
            if (curr.Flags != prev.Flags)
            {
                flags |= (byte)TransformDeltaFlags.Active;
            }

            Results[index] = new DeltaResult
            {
                HasDelta = (byte)(flags != 0 ? 1 : 0),
                Flags = flags
            };
        }
    }

    /// <summary>Rigidbody 델타 계산 Job</summary>
    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast)]
    public struct ComputeRigidbodyDeltasJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<RigidbodyData> Current;
        [ReadOnly] public NativeArray<RigidbodyData> Previous;
        [ReadOnly] public NativeArray<byte> HasRigidbody;
        [WriteOnly] public NativeArray<DeltaResult> Results;
        public float VelocityThresholdSq;

        public void Execute(int index)
        {
            if (HasRigidbody[index] == 0)
            {
                Results[index] = default;
                return;
            }

            var curr = Current[index];
            var prev = Previous[index];

            byte flags = 0;
            const float MAX_VEL = 500f;
            const float MAX_ANG_VEL = 50f;

            // Linear Velocity 비교
            float ldx = DequantizeVelocity(curr.VelX, MAX_VEL) - DequantizeVelocity(prev.VelX, MAX_VEL);
            float ldy = DequantizeVelocity(curr.VelY, MAX_VEL) - DequantizeVelocity(prev.VelY, MAX_VEL);
            float ldz = DequantizeVelocity(curr.VelZ, MAX_VEL) - DequantizeVelocity(prev.VelZ, MAX_VEL);

            if (ldx * ldx + ldy * ldy + ldz * ldz > VelocityThresholdSq)
            {
                flags |= 1;
            }

            // Angular Velocity 비교
            float adx = DequantizeVelocity(curr.AngVelX, MAX_ANG_VEL) - DequantizeVelocity(prev.AngVelX, MAX_ANG_VEL);
            float ady = DequantizeVelocity(curr.AngVelY, MAX_ANG_VEL) - DequantizeVelocity(prev.AngVelY, MAX_ANG_VEL);
            float adz = DequantizeVelocity(curr.AngVelZ, MAX_ANG_VEL) - DequantizeVelocity(prev.AngVelZ, MAX_ANG_VEL);

            if (adx * adx + ady * ady + adz * adz > VelocityThresholdSq)
            {
                flags |= 2;
            }

            Results[index] = new DeltaResult
            {
                HasDelta = (byte)(flags != 0 ? 1 : 0),
                Flags = flags
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float DequantizeVelocity(short quantized, float maxVelocity)
        {
            return (quantized / (float)short.MaxValue) * maxVelocity;
        }
    }

    /// <summary>배열 복사 Job</summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct CopyArrayJob<T> : IJobParallelFor where T : struct
    {
        [ReadOnly] public NativeArray<T> Source;
        [WriteOnly] public NativeArray<T> Destination;

        public void Execute(int index)
        {
            Destination[index] = Source[index];
        }
    }

    #endregion
}
