using UnityEngine;

namespace ReplaySystem.Compression
{
    /// <summary>오브젝트별 양자화 프리셋 오버라이드 컴포넌트입니다.</summary>
    [DisallowMultipleComponent]
    public class QuantizationOverride : MonoBehaviour
    {
        [Header("Override Settings")]
        [Tooltip("오버라이드 활성화")]
        [SerializeField] private bool useOverride = true;

        [Tooltip("사용할 프리셋 (null이면 기본 프리셋 사용)")]
        [SerializeField] private QuantizationPreset preset;

        [Header("Individual Overrides")]
        [Tooltip("개별 채널 오버라이드 활성화")]
        [SerializeField] private bool useIndividualOverrides;

        [Tooltip("Position 오버라이드")]
        [SerializeField] private ChannelOverride positionOverride;

        [Tooltip("Rotation 오버라이드")]
        [SerializeField] private RotationOverride rotationOverride;

        [Tooltip("Scale 오버라이드")]
        [SerializeField] private ChannelOverride scaleOverride;

        [Tooltip("Velocity 오버라이드")]
        [SerializeField] private ChannelOverride velocityOverride;

        [Tooltip("Angular Velocity 오버라이드")]
        [SerializeField] private ChannelOverride angularVelocityOverride;

        /// <summary>오버라이드 활성화 여부</summary>
        public bool UseOverride => useOverride;

        /// <summary>할당된 프리셋</summary>
        public QuantizationPreset Preset => preset;

        /// <summary>개별 오버라이드 사용 여부</summary>
        public bool UseIndividualOverrides => useIndividualOverrides;

        /// <summary>Position 설정 가져오기</summary>
        public Vector3QuantizationSettings GetPositionSettings(QuantizationPreset basePreset)
        {
            var effectivePreset = preset != null ? preset : basePreset;

            if (effectivePreset == null)
            {
                return Vector3QuantizationSettings.CreateUniform(-1000f, 1000f, 16);
            }

            if (!useIndividualOverrides || !positionOverride.enabled)
            {
                return effectivePreset.Position;
            }

            return positionOverride.ApplyTo(effectivePreset.Position);
        }

        /// <summary>Rotation 설정 가져오기</summary>
        public QuaternionQuantizationSettings GetRotationSettings(QuantizationPreset basePreset)
        {
            var effectivePreset = preset != null ? preset : basePreset;

            if (effectivePreset == null)
            {
                return QuaternionQuantizationSettings.Create(10, true);
            }

            if (!useIndividualOverrides || !rotationOverride.enabled)
            {
                return effectivePreset.Rotation;
            }

            return rotationOverride.ApplyTo(effectivePreset.Rotation);
        }

        /// <summary>Scale 설정 가져오기</summary>
        public Vector3QuantizationSettings GetScaleSettings(QuantizationPreset basePreset)
        {
            var effectivePreset = preset != null ? preset : basePreset;

            if (effectivePreset == null)
            {
                return Vector3QuantizationSettings.CreateUniform(0.01f, 100f, 8);
            }

            if (!useIndividualOverrides || !scaleOverride.enabled)
            {
                return effectivePreset.Scale;
            }

            return scaleOverride.ApplyTo(effectivePreset.Scale);
        }

        /// <summary>Velocity 설정 가져오기</summary>
        public Vector3QuantizationSettings GetVelocitySettings(QuantizationPreset basePreset)
        {
            var effectivePreset = preset != null ? preset : basePreset;

            if (effectivePreset == null)
            {
                return Vector3QuantizationSettings.CreateUniform(-100f, 100f, 16);
            }

            if (!useIndividualOverrides || !velocityOverride.enabled)
            {
                return effectivePreset.Velocity;
            }

            return velocityOverride.ApplyTo(effectivePreset.Velocity);
        }

        /// <summary>Angular Velocity 설정 가져오기</summary>
        public Vector3QuantizationSettings GetAngularVelocitySettings(QuantizationPreset basePreset)
        {
            var effectivePreset = preset != null ? preset : basePreset;

            if (effectivePreset == null)
            {
                return Vector3QuantizationSettings.CreateUniform(-50f, 50f, 16);
            }

            if (!useIndividualOverrides || !angularVelocityOverride.enabled)
            {
                return effectivePreset.AngularVelocity;
            }

            return angularVelocityOverride.ApplyTo(effectivePreset.AngularVelocity);
        }

        /// <summary>유효한 프리셋 가져오기</summary>
        public QuantizationPreset GetEffectivePreset(QuantizationPreset basePreset)
        {
            return preset != null ? preset : basePreset;
        }
    }

    /// <summary>Vector3 채널 오버라이드 설정</summary>
    [System.Serializable]
    public struct ChannelOverride
    {
        [Tooltip("오버라이드 활성화")]
        public bool enabled;

        [Tooltip("X축 오버라이드")]
        public AxisOverride x;

        [Tooltip("Y축 오버라이드")]
        public AxisOverride y;

        [Tooltip("Z축 오버라이드")]
        public AxisOverride z;

        [Tooltip("균일 설정 (X 설정을 모든 축에 적용)")]
        public bool useUniform;

        /// <summary>오버라이드 적용</summary>
        public Vector3QuantizationSettings ApplyTo(Vector3QuantizationSettings original)
        {
            if (!enabled)
            {
                return original;
            }

            var result = original;

            if (useUniform)
            {
                result.x = x.ApplyTo(original.x);
                result.y = x.ApplyTo(original.y);
                result.z = x.ApplyTo(original.z);
                result.useUniform = true;
            }
            else
            {
                result.x = x.ApplyTo(original.x);
                result.y = y.ApplyTo(original.y);
                result.z = z.ApplyTo(original.z);
                result.useUniform = false;
            }

            return result;
        }
    }

    /// <summary>단일 축 오버라이드 설정</summary>
    [System.Serializable]
    public struct AxisOverride
    {
        [Tooltip("이 축 오버라이드 활성화")]
        public bool enabled;

        [Tooltip("최소값 오버라이드")]
        public bool overrideMin;
        public float minValue;

        [Tooltip("최대값 오버라이드")]
        public bool overrideMax;
        public float maxValue;

        [Tooltip("비트 수 오버라이드")]
        public bool overrideBits;
        [Range(8, 16)]
        public int bitCount;

        /// <summary>오버라이드 적용</summary>
        public QuantizationChannel ApplyTo(QuantizationChannel original)
        {
            if (!enabled)
            {
                return original;
            }

            var result = original;

            if (overrideMin)
            {
                result.minValue = minValue;
            }

            if (overrideMax)
            {
                result.maxValue = maxValue;
            }

            if (overrideBits)
            {
                result.bitCount = bitCount;
            }

            return result;
        }
    }

    /// <summary>Rotation 오버라이드 설정</summary>
    [System.Serializable]
    public struct RotationOverride
    {
        [Tooltip("오버라이드 활성화")]
        public bool enabled;

        [Tooltip("비트 수 오버라이드")]
        public bool overrideBits;
        [Range(8, 12)]
        public int bitsPerComponent;

        [Tooltip("Smallest-3 사용 오버라이드")]
        public bool overrideSmallest3;
        public bool useSmallest3;

        /// <summary>오버라이드 적용</summary>
        public QuaternionQuantizationSettings ApplyTo(QuaternionQuantizationSettings original)
        {
            if (!enabled)
            {
                return original;
            }

            var result = original;

            if (overrideBits)
            {
                result.bitsPerComponent = bitsPerComponent;
            }

            if (overrideSmallest3)
            {
                result.useSmallest3 = useSmallest3;
            }

            return result;
        }
    }
}
