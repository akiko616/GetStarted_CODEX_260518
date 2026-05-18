using System;
using System.Collections.Generic;
using ReplaySystem.Core;
using UnityEngine;

namespace ReplaySystem.Compression
{
    /// <summary>델타 압축 처리기입니다.</summary>
    public class DeltaCompressor
    {
        private readonly Dictionary<int, CompressedTransform> lastTransforms = new Dictionary<int, CompressedTransform>();
        private readonly Dictionary<int, CompressedRigidbody> lastRigidbodies = new Dictionary<int, CompressedRigidbody>();

        private readonly float positionThresholdSq;
        private readonly float rotationThreshold;
        private readonly float velocityThresholdSq;

        public DeltaCompressor(float posThreshold = 0.001f, float rotThreshold = 0.001f, float velThreshold = 0.01f)
        {
            positionThresholdSq = posThreshold * posThreshold;
            rotationThreshold = rotThreshold;
            velocityThresholdSq = velThreshold * velThreshold;
        }

        /// <summary>상태 초기화</summary>
        public void Reset()
        {
            lastTransforms.Clear();
            lastRigidbodies.Clear();
        }

        /// <summary>Transform 델타 계산</summary>
        public CompressedTransformDelta? ComputeTransformDelta(int objectId, CompressedTransform current)
        {
            if (!lastTransforms.TryGetValue(objectId, out var last))
            {
                lastTransforms[objectId] = current;
                return new CompressedTransformDelta
                {
                    ObjectId = objectId,
                    Flags = TransformDeltaFlags.Position | TransformDeltaFlags.Rotation | TransformDeltaFlags.Scale | TransformDeltaFlags.Active,
                    Transform = current
                };
            }

            var flags = TransformDeltaFlags.None;

            float posDiffSq = CalculatePositionDiffSq(last, current);
            if (posDiffSq > positionThresholdSq)
            {
                flags |= TransformDeltaFlags.Position;
            }

            if (last.Rotation != current.Rotation)
            {
                flags |= TransformDeltaFlags.Rotation;
            }

            if (last.ScaleX != current.ScaleX || last.ScaleY != current.ScaleY || last.ScaleZ != current.ScaleZ)
            {
                flags |= TransformDeltaFlags.Scale;
            }

            if (last.Flags != current.Flags)
            {
                flags |= TransformDeltaFlags.Active;
            }

            if (flags == TransformDeltaFlags.None)
            {
                return null;
            }

            lastTransforms[objectId] = current;

            return new CompressedTransformDelta
            {
                ObjectId = objectId,
                Flags = flags,
                Transform = current
            };
        }

        /// <summary>Rigidbody 델타 계산</summary>
        public CompressedRigidbodyDelta? ComputeRigidbodyDelta(int objectId, CompressedRigidbody current)
        {
            if (!lastRigidbodies.TryGetValue(objectId, out var last))
            {
                lastRigidbodies[objectId] = current;
                return new CompressedRigidbodyDelta
                {
                    ObjectId = objectId,
                    HasLinearVelocity = true,
                    HasAngularVelocity = true,
                    Rigidbody = current
                };
            }

            bool linearChanged = CalculateVelocityDiffSq(
                last.VelX, last.VelY, last.VelZ,
                current.VelX, current.VelY, current.VelZ) > velocityThresholdSq;

            bool angularChanged = CalculateVelocityDiffSq(
                last.AngVelX, last.AngVelY, last.AngVelZ,
                current.AngVelX, current.AngVelY, current.AngVelZ) > velocityThresholdSq;

            if (!linearChanged && !angularChanged)
            {
                return null;
            }

            lastRigidbodies[objectId] = current;

            return new CompressedRigidbodyDelta
            {
                ObjectId = objectId,
                HasLinearVelocity = linearChanged,
                HasAngularVelocity = angularChanged,
                Rigidbody = current
            };
        }

        /// <summary>키프레임 적용 (전체 상태 복원)</summary>
        public void ApplyKeyframe(IEnumerable<KeyValuePair<int, CompressedTransform>> transforms,
            IEnumerable<KeyValuePair<int, CompressedRigidbody>> rigidbodies)
        {
            lastTransforms.Clear();
            lastRigidbodies.Clear();

            foreach (var kvp in transforms)
            {
                lastTransforms[kvp.Key] = kvp.Value;
            }

            if (rigidbodies != null)
            {
                foreach (var kvp in rigidbodies)
                {
                    lastRigidbodies[kvp.Key] = kvp.Value;
                }
            }
        }

        /// <summary>특정 오브젝트의 마지막 Transform 상태 조회</summary>
        public bool TryGetLastTransform(int objectId, out CompressedTransform transform)
        {
            return lastTransforms.TryGetValue(objectId, out transform);
        }

        /// <summary>특정 오브젝트의 마지막 Rigidbody 상태 조회</summary>
        public bool TryGetLastRigidbody(int objectId, out CompressedRigidbody rigidbody)
        {
            return lastRigidbodies.TryGetValue(objectId, out rigidbody);
        }

        /// <summary>오브젝트 상태 제거</summary>
        public void RemoveObject(int objectId)
        {
            lastTransforms.Remove(objectId);
            lastRigidbodies.Remove(objectId);
        }

        private float CalculatePositionDiffSq(CompressedTransform a, CompressedTransform b)
        {
            float dx = QuantizationUtils.HalfToFloat(a.PosX) - QuantizationUtils.HalfToFloat(b.PosX);
            float dy = QuantizationUtils.HalfToFloat(a.PosY) - QuantizationUtils.HalfToFloat(b.PosY);
            float dz = QuantizationUtils.HalfToFloat(a.PosZ) - QuantizationUtils.HalfToFloat(b.PosZ);
            return dx * dx + dy * dy + dz * dz;
        }

        private float CalculateVelocityDiffSq(short ax, short ay, short az, short bx, short by, short bz)
        {
            float dx = QuantizationUtils.DequantizeVelocity(ax) - QuantizationUtils.DequantizeVelocity(bx);
            float dy = QuantizationUtils.DequantizeVelocity(ay) - QuantizationUtils.DequantizeVelocity(by);
            float dz = QuantizationUtils.DequantizeVelocity(az) - QuantizationUtils.DequantizeVelocity(bz);
            return dx * dx + dy * dy + dz * dz;
        }
    }

    /// <summary>압축된 Transform 델타</summary>
    [Serializable]
    public struct CompressedTransformDelta
    {
        public int ObjectId;
        public TransformDeltaFlags Flags;
        public CompressedTransform Transform;
    }

    /// <summary>압축된 Rigidbody 델타</summary>
    [Serializable]
    public struct CompressedRigidbodyDelta
    {
        public int ObjectId;
        public bool HasLinearVelocity;
        public bool HasAngularVelocity;
        public CompressedRigidbody Rigidbody;
    }
}
