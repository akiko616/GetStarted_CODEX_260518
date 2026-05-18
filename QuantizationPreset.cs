using System;
using UnityEngine;

namespace ReplaySystem.Compression
{
    /// <summary>양자화 채널 설정입니다.</summary>
    [Serializable]
    public struct QuantizationChannel
    {
        [Tooltip("채널 활성화 여부")]
        public bool enabled;

        [Tooltip("최소값")]
        public float minValue;

        [Tooltip("최대값")]
        public float maxValue;

        [Tooltip("비트 수 (8, 10, 12, 16)")]
        [Range(8, 16)]
        public int bitCount;

        /// <summary>양자화 범위</summary>
        public float Range => maxValue - minValue;

        /// <summary>양자화 스텝 크기</summary>
        public float StepSize => Range / ((1 << bitCount) - 1);

        /// <summary>최대 오차</summary>
        public float MaxError => StepSize * 0.5f;

        /// <summary>기본 채널 생성</summary>
        public static QuantizationChannel Create(float min, float max, int bits = 16)
        {
            return new QuantizationChannel
            {
                enabled = true,
                minValue = min,
                maxValue = max,
                bitCount = bits
            };
        }

        /// <summary>비활성화된 채널</summary>
        public static QuantizationChannel Disabled => new QuantizationChannel { enabled = false };

        /// <summary>값을 양자화합니다.</summary>
        public uint Quantize(float value)
        {
            if (!enabled)
            {
                return 0;
            }

            float normalized = Mathf.Clamp01((value - minValue) / Range);
            uint maxVal = (uint)((1 << bitCount) - 1);
            return (uint)Mathf.RoundToInt(normalized * maxVal);
        }

        /// <summary>양자화된 값을 복원합니다.</summary>
        public float Dequantize(uint quantized)
        {
            if (!enabled)
            {
                return 0f;
            }

            uint maxVal = (uint)((1 << bitCount) - 1);
            float normalized = quantized / (float)maxVal;
            return minValue + normalized * Range;
        }
    }

    /// <summary>Vector3 양자화 설정입니다.</summary>
    [Serializable]
    public struct Vector3QuantizationSettings
    {
        [Tooltip("X축 설정")]
        public QuantizationChannel x;

        [Tooltip("Y축 설정")]
        public QuantizationChannel y;

        [Tooltip("Z축 설정")]
        public QuantizationChannel z;

        [Tooltip("균일 설정 사용 (X 설정을 Y, Z에도 적용)")]
        public bool useUniform;

        /// <summary>총 비트 수</summary>
        public int TotalBits => x.bitCount + (useUniform ? x.bitCount * 2 : y.bitCount + z.bitCount);

        /// <summary>총 바이트 수</summary>
        public int TotalBytes => Mathf.CeilToInt(TotalBits / 8f);

        /// <summary>균일 설정으로 생성</summary>
        public static Vector3QuantizationSettings CreateUniform(float min, float max, int bits = 16)
        {
            var channel = QuantizationChannel.Create(min, max, bits);
            return new Vector3QuantizationSettings
            {
                x = channel,
                y = channel,
                z = channel,
                useUniform = true
            };
        }

        /// <summary>개별 축 설정으로 생성</summary>
        public static Vector3QuantizationSettings Create(
            float minX, float maxX,
            float minY, float maxY,
            float minZ, float maxZ,
            int bits = 16)
        {
            return new Vector3QuantizationSettings
            {
                x = QuantizationChannel.Create(minX, maxX, bits),
                y = QuantizationChannel.Create(minY, maxY, bits),
                z = QuantizationChannel.Create(minZ, maxZ, bits),
                useUniform = false
            };
        }

        /// <summary>채널 가져오기</summary>
        public QuantizationChannel GetChannel(int axis)
        {
            if (useUniform)
            {
                return x;
            }

            return axis switch
            {
                0 => x,
                1 => y,
                2 => z,
                _ => x
            };
        }

        /// <summary>Vector3를 양자화합니다.</summary>
        public Vector3UInt Quantize(Vector3 value)
        {
            var chX = GetChannel(0);
            var chY = GetChannel(1);
            var chZ = GetChannel(2);

            return new Vector3UInt
            {
                x = chX.Quantize(value.x),
                y = chY.Quantize(value.y),
                z = chZ.Quantize(value.z)
            };
        }

        /// <summary>양자화된 값을 복원합니다.</summary>
        public Vector3 Dequantize(Vector3UInt quantized)
        {
            var chX = GetChannel(0);
            var chY = GetChannel(1);
            var chZ = GetChannel(2);

            return new Vector3(
                chX.Dequantize(quantized.x),
                chY.Dequantize(quantized.y),
                chZ.Dequantize(quantized.z));
        }
    }

    /// <summary>Quaternion 양자화 설정입니다.</summary>
    [Serializable]
    public struct QuaternionQuantizationSettings
    {
        [Tooltip("컴포넌트당 비트 수 (8, 10, 12)")]
        [Range(8, 12)]
        public int bitsPerComponent;

        [Tooltip("Smallest-3 압축 사용")]
        public bool useSmallest3;

        /// <summary>총 비트 수</summary>
        public int TotalBits => useSmallest3 ? bitsPerComponent * 3 + 2 : 32;

        /// <summary>총 바이트 수</summary>
        public int TotalBytes => Mathf.CeilToInt(TotalBits / 8f);

        /// <summary>최대 각도 오차 (도)</summary>
        public float MaxAngleError => useSmallest3 ? 180f / (1 << bitsPerComponent) : 0f;

        /// <summary>기본 설정 생성</summary>
        public static QuaternionQuantizationSettings Create(int bits = 10, bool smallest3 = true)
        {
            return new QuaternionQuantizationSettings
            {
                bitsPerComponent = bits,
                useSmallest3 = smallest3
            };
        }
    }

    /// <summary>양자화된 Vector3 구조체</summary>
    [Serializable]
    public struct Vector3UInt
    {
        public uint x;
        public uint y;
        public uint z;
    }

    /// <summary>양자화 프리셋 ScriptableObject입니다.</summary>
    [CreateAssetMenu(fileName = "QuantizationPreset", menuName = "ReplaySystem/Quantization Preset", order = 1)]
    public class QuantizationPreset : ScriptableObject
    {
        [Header("Preset Info")]
        [Tooltip("프리셋 이름")]
        [SerializeField] private string presetName = "Custom";

        [Tooltip("프리셋 설명")]
        [TextArea(2, 4)]
        [SerializeField] private string description = "";

        [Header("Position Settings")]
        [Tooltip("위치 양자화 설정")]
        [SerializeField] private Vector3QuantizationSettings position = Vector3QuantizationSettings.CreateUniform(-1000f, 1000f, 16);

        [Header("Rotation Settings")]
        [Tooltip("회전 양자화 설정")]
        [SerializeField] private QuaternionQuantizationSettings rotation = QuaternionQuantizationSettings.Create(10, true);

        [Header("Scale Settings")]
        [Tooltip("스케일 양자화 설정")]
        [SerializeField] private Vector3QuantizationSettings scale = Vector3QuantizationSettings.CreateUniform(0.01f, 100f, 8);

        [Tooltip("로그 스케일 양자화 사용")]
        [SerializeField] private bool useLogScale = true;

        [Header("Velocity Settings")]
        [Tooltip("속도 양자화 설정")]
        [SerializeField] private Vector3QuantizationSettings velocity = Vector3QuantizationSettings.CreateUniform(-100f, 100f, 16);

        [Header("Angular Velocity Settings")]
        [Tooltip("각속도 양자화 설정")]
        [SerializeField] private Vector3QuantizationSettings angularVelocity = Vector3QuantizationSettings.CreateUniform(-50f, 50f, 16);

        /// <summary>프리셋 이름</summary>
        public string PresetName => presetName;

        /// <summary>설명</summary>
        public string Description => description;

        /// <summary>위치 설정</summary>
        public Vector3QuantizationSettings Position => position;

        /// <summary>회전 설정</summary>
        public QuaternionQuantizationSettings Rotation => rotation;

        /// <summary>스케일 설정</summary>
        public Vector3QuantizationSettings Scale => scale;

        /// <summary>로그 스케일 사용 여부</summary>
        public bool UseLogScale => useLogScale;

        /// <summary>속도 설정</summary>
        public Vector3QuantizationSettings Velocity => velocity;

        /// <summary>각속도 설정</summary>
        public Vector3QuantizationSettings AngularVelocity => angularVelocity;

        /// <summary>Transform당 총 바이트 수</summary>
        public int TransformByteSize => position.TotalBytes + rotation.TotalBytes + scale.TotalBytes + 1;

        /// <summary>Rigidbody당 총 바이트 수</summary>
        public int RigidbodyByteSize => velocity.TotalBytes + angularVelocity.TotalBytes;

        /// <summary>예상 오차 정보를 반환합니다.</summary>
        public QuantizationErrorInfo GetErrorInfo()
        {
            return new QuantizationErrorInfo
            {
                positionError = new Vector3(
                    position.GetChannel(0).MaxError,
                    position.GetChannel(1).MaxError,
                    position.GetChannel(2).MaxError),
                rotationError = rotation.MaxAngleError,
                scaleError = new Vector3(
                    scale.GetChannel(0).MaxError,
                    scale.GetChannel(1).MaxError,
                    scale.GetChannel(2).MaxError),
                velocityError = new Vector3(
                    velocity.GetChannel(0).MaxError,
                    velocity.GetChannel(1).MaxError,
                    velocity.GetChannel(2).MaxError),
                angularVelocityError = new Vector3(
                    angularVelocity.GetChannel(0).MaxError,
                    angularVelocity.GetChannel(1).MaxError,
                    angularVelocity.GetChannel(2).MaxError)
            };
        }

        #region Built-in Presets

        /// <summary>FPS 게임용 프리셋 생성</summary>
        public static QuantizationPreset CreateFPSPreset()
        {
            var preset = CreateInstance<QuantizationPreset>();
            preset.presetName = "FPS";
            preset.description = "FPS 게임용 (좁은 맵, 높은 정밀도)";
            preset.position = Vector3QuantizationSettings.CreateUniform(-500f, 500f, 16);
            preset.rotation = QuaternionQuantizationSettings.Create(10, true);
            preset.scale = Vector3QuantizationSettings.CreateUniform(0.1f, 10f, 8);
            preset.velocity = Vector3QuantizationSettings.CreateUniform(-50f, 50f, 16);
            preset.angularVelocity = Vector3QuantizationSettings.CreateUniform(-20f, 20f, 16);
            return preset;
        }

        /// <summary>레이싱 게임용 프리셋 생성</summary>
        public static QuantizationPreset CreateRacingPreset()
        {
            var preset = CreateInstance<QuantizationPreset>();
            preset.presetName = "Racing";
            preset.description = "레이싱 게임용 (넓은 트랙, 고속)";
            preset.position = Vector3QuantizationSettings.CreateUniform(-10000f, 10000f, 16);
            preset.rotation = QuaternionQuantizationSettings.Create(10, true);
            preset.scale = Vector3QuantizationSettings.CreateUniform(0.5f, 5f, 8);
            preset.velocity = Vector3QuantizationSettings.CreateUniform(-300f, 300f, 16);
            preset.angularVelocity = Vector3QuantizationSettings.CreateUniform(-30f, 30f, 16);
            return preset;
        }

        /// <summary>우주/대규모 게임용 프리셋 생성</summary>
        public static QuantizationPreset CreateSpacePreset()
        {
            var preset = CreateInstance<QuantizationPreset>();
            preset.presetName = "Space";
            preset.description = "우주/대규모 게임용 (극대 범위)";
            preset.position = Vector3QuantizationSettings.CreateUniform(-100000f, 100000f, 16);
            preset.rotation = QuaternionQuantizationSettings.Create(10, true);
            preset.scale = Vector3QuantizationSettings.CreateUniform(0.1f, 1000f, 10);
            preset.velocity = Vector3QuantizationSettings.CreateUniform(-10000f, 10000f, 16);
            preset.angularVelocity = Vector3QuantizationSettings.CreateUniform(-100f, 100f, 16);
            return preset;
        }

        /// <summary>미니어처/정밀 게임용 프리셋 생성</summary>
        public static QuantizationPreset CreateMicroPreset()
        {
            var preset = CreateInstance<QuantizationPreset>();
            preset.presetName = "Micro";
            preset.description = "미니어처/정밀 게임용 (좁은 범위, 극고 정밀도)";
            preset.position = Vector3QuantizationSettings.CreateUniform(-10f, 10f, 16);
            preset.rotation = QuaternionQuantizationSettings.Create(12, true);
            preset.scale = Vector3QuantizationSettings.CreateUniform(0.01f, 2f, 10);
            preset.velocity = Vector3QuantizationSettings.CreateUniform(-5f, 5f, 16);
            preset.angularVelocity = Vector3QuantizationSettings.CreateUniform(-10f, 10f, 16);
            return preset;
        }

        /// <summary>VR 게임용 프리셋 생성</summary>
        public static QuantizationPreset CreateVRPreset()
        {
            var preset = CreateInstance<QuantizationPreset>();
            preset.presetName = "VR";
            preset.description = "VR 게임용 (룸스케일, 손 추적 정밀도)";
            preset.position = Vector3QuantizationSettings.CreateUniform(-50f, 50f, 16);
            preset.rotation = QuaternionQuantizationSettings.Create(12, true);
            preset.scale = Vector3QuantizationSettings.CreateUniform(0.1f, 10f, 8);
            preset.velocity = Vector3QuantizationSettings.CreateUniform(-20f, 20f, 16);
            preset.angularVelocity = Vector3QuantizationSettings.CreateUniform(-30f, 30f, 16);
            return preset;
        }

        #endregion

        private void OnValidate()
        {
            if (position.x.bitCount < 8) position.x.bitCount = 8;
            if (position.y.bitCount < 8) position.y.bitCount = 8;
            if (position.z.bitCount < 8) position.z.bitCount = 8;

            if (rotation.bitsPerComponent < 8) rotation.bitsPerComponent = 8;
            if (rotation.bitsPerComponent > 12) rotation.bitsPerComponent = 12;
        }
    }

    /// <summary>양자화 오차 정보</summary>
    public struct QuantizationErrorInfo
    {
        public Vector3 positionError;
        public float rotationError;
        public Vector3 scaleError;
        public Vector3 velocityError;
        public Vector3 angularVelocityError;
    }
}
