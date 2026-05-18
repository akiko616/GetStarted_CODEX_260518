using ReplaySystem.Compression;
using UnityEngine;

namespace ReplaySystem.Capture
{
    /// <summary>Rigidbody 캡처 컴포넌트입니다.</summary>
    [RequireComponent(typeof(TransformCapture))]
    public class RigidbodyCapture : MonoBehaviour, IRigidbodyCapture
    {
        [SerializeField] private float velocityThreshold = 0.01f;
        [SerializeField] private float angularVelocityThreshold = 0.01f;

        private Rigidbody cachedRigidbody;
        private TransformCapture transformCapture;
        private CompressedRigidbody lastState;
        private bool hasLastState;

        /// <inheritdoc/>
        public int ObjectId => transformCapture?.ObjectId ?? -1;

        /// <inheritdoc/>
        public bool HasRigidbody => cachedRigidbody != null;

        /// <summary>캐시된 Rigidbody (Job System용)</summary>
        public Rigidbody CachedRigidbody => cachedRigidbody;

        private void Awake()
        {
            cachedRigidbody = GetComponent<Rigidbody>();
            transformCapture = GetComponent<TransformCapture>();
        }

        /// <inheritdoc/>
        public CompressedRigidbody CaptureState()
        {
            if (cachedRigidbody == null)
            {
                return default;
            }

            return CompressedRigidbody.FromRigidbody(cachedRigidbody);
        }

        /// <inheritdoc/>
        public CompressedRigidbodyDelta? CaptureDelta()
        {
            if (cachedRigidbody == null)
            {
                return null;
            }

            var current = CaptureState();

            if (!hasLastState)
            {
                hasLastState = true;
                lastState = current;

                return new CompressedRigidbodyDelta
                {
                    ObjectId = ObjectId,
                    HasLinearVelocity = true,
                    HasAngularVelocity = true,
                    Rigidbody = current
                };
            }

            bool linearChanged = HasVelocityChanged(
                lastState.VelX, lastState.VelY, lastState.VelZ,
                current.VelX, current.VelY, current.VelZ,
                velocityThreshold);

            bool angularChanged = HasVelocityChanged(
                lastState.AngVelX, lastState.AngVelY, lastState.AngVelZ,
                current.AngVelX, current.AngVelY, current.AngVelZ,
                angularVelocityThreshold);

            if (!linearChanged && !angularChanged)
            {
                return null;
            }

            lastState = current;

            return new CompressedRigidbodyDelta
            {
                ObjectId = ObjectId,
                HasLinearVelocity = linearChanged,
                HasAngularVelocity = angularChanged,
                Rigidbody = current
            };
        }

        /// <inheritdoc/>
        public void ApplyState(CompressedRigidbody state)
        {
            if (cachedRigidbody == null)
            {
                return;
            }

            state.ApplyToRigidbody(cachedRigidbody);
            lastState = state;
            hasLastState = true;
        }

        /// <inheritdoc/>
        public void ApplyInterpolatedState(CompressedRigidbody from, CompressedRigidbody to, float t)
        {
            if (cachedRigidbody == null)
            {
                return;
            }

            cachedRigidbody.linearVelocity = Vector3.Lerp(from.GetLinearVelocity(), to.GetLinearVelocity(), t);
            cachedRigidbody.angularVelocity = Vector3.Lerp(from.GetAngularVelocity(), to.GetAngularVelocity(), t);
        }

        /// <inheritdoc/>
        public void ResetState()
        {
            hasLastState = false;
        }

        private bool HasVelocityChanged(short ax, short ay, short az, short bx, short by, short bz, float threshold)
        {
            float dx = QuantizationUtils.DequantizeVelocity(ax) - QuantizationUtils.DequantizeVelocity(bx);
            float dy = QuantizationUtils.DequantizeVelocity(ay) - QuantizationUtils.DequantizeVelocity(by);
            float dz = QuantizationUtils.DequantizeVelocity(az) - QuantizationUtils.DequantizeVelocity(bz);

            return (dx * dx + dy * dy + dz * dz) > threshold * threshold;
        }
    }
}
