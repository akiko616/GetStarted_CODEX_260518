using System;
using System.IO;
using UnityEngine;

namespace ReplaySystem.Compression
{
    /// <summary>프리셋 기반 양자화 인코더입니다.</summary>
    public class PresetQuantizer
    {
        private readonly QuantizationPreset preset;

        /// <summary>사용 중인 프리셋</summary>
        public QuantizationPreset Preset => preset;

        public PresetQuantizer(QuantizationPreset preset)
        {
            this.preset = preset ?? throw new ArgumentNullException(nameof(preset));
        }

        #region Position

        /// <summary>Position을 양자화합니다.</summary>
        public QuantizedPosition QuantizePosition(Vector3 position)
        {
            return QuantizePosition(position, preset.Position);
        }

        /// <summary>오버라이드 설정으로 Position을 양자화합니다.</summary>
        public QuantizedPosition QuantizePosition(Vector3 position, Vector3QuantizationSettings settings)
        {
            var quantized = settings.Quantize(position);

            return new QuantizedPosition
            {
                x = (ushort)quantized.x,
                y = (ushort)quantized.y,
                z = (ushort)quantized.z
            };
        }

        /// <summary>양자화된 Position을 복원합니다.</summary>
        public Vector3 DequantizePosition(QuantizedPosition quantized)
        {
            return DequantizePosition(quantized, preset.Position);
        }

        /// <summary>오버라이드 설정으로 양자화된 Position을 복원합니다.</summary>
        public Vector3 DequantizePosition(QuantizedPosition quantized, Vector3QuantizationSettings settings)
        {
            var vec3uint = new Vector3UInt
            {
                x = quantized.x,
                y = quantized.y,
                z = quantized.z
            };

            return settings.Dequantize(vec3uint);
        }

        #endregion

        #region Rotation

        /// <summary>Rotation을 양자화합니다.</summary>
        public uint QuantizeRotation(Quaternion rotation)
        {
            return QuantizeRotation(rotation, preset.Rotation);
        }

        /// <summary>오버라이드 설정으로 Rotation을 양자화합니다.</summary>
        public uint QuantizeRotation(Quaternion rotation, QuaternionQuantizationSettings settings)
        {
            if (!settings.useSmallest3)
            {
                return PackQuaternionFull(rotation);
            }

            return PackQuaternionSmallest3(rotation, settings.bitsPerComponent);
        }

        /// <summary>양자화된 Rotation을 복원합니다.</summary>
        public Quaternion DequantizeRotation(uint quantized)
        {
            return DequantizeRotation(quantized, preset.Rotation);
        }

        /// <summary>오버라이드 설정으로 양자화된 Rotation을 복원합니다.</summary>
        public Quaternion DequantizeRotation(uint quantized, QuaternionQuantizationSettings settings)
        {
            if (!settings.useSmallest3)
            {
                return UnpackQuaternionFull(quantized);
            }

            return UnpackQuaternionSmallest3(quantized, settings.bitsPerComponent);
        }

        private uint PackQuaternionSmallest3(Quaternion q, int bitsPerComponent)
        {
            q = q.normalized;

            int maxIndex = 0;
            float maxValue = Mathf.Abs(q.x);

            if (Mathf.Abs(q.y) > maxValue) { maxIndex = 1; maxValue = Mathf.Abs(q.y); }
            if (Mathf.Abs(q.z) > maxValue) { maxIndex = 2; maxValue = Mathf.Abs(q.z); }
            if (Mathf.Abs(q.w) > maxValue) { maxIndex = 3; }

            float sign = maxIndex switch
            {
                0 => Mathf.Sign(q.x),
                1 => Mathf.Sign(q.y),
                2 => Mathf.Sign(q.z),
                _ => Mathf.Sign(q.w)
            };

            if (sign < 0)
            {
                q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            }

            float a, b, c;
            switch (maxIndex)
            {
                case 0: a = q.y; b = q.z; c = q.w; break;
                case 1: a = q.x; b = q.z; c = q.w; break;
                case 2: a = q.x; b = q.y; c = q.w; break;
                default: a = q.x; b = q.y; c = q.z; break;
            }

            int maxVal = (1 << bitsPerComponent) - 1;
            float range = 1f / Mathf.Sqrt(2f);

            uint qa = (uint)Mathf.Clamp(Mathf.RoundToInt((a / range * 0.5f + 0.5f) * maxVal), 0, maxVal);
            uint qb = (uint)Mathf.Clamp(Mathf.RoundToInt((b / range * 0.5f + 0.5f) * maxVal), 0, maxVal);
            uint qc = (uint)Mathf.Clamp(Mathf.RoundToInt((c / range * 0.5f + 0.5f) * maxVal), 0, maxVal);

            return (uint)maxIndex | (qa << 2) | (qb << (2 + bitsPerComponent)) | (qc << (2 + bitsPerComponent * 2));
        }

        private Quaternion UnpackQuaternionSmallest3(uint packed, int bitsPerComponent)
        {
            int maxIndex = (int)(packed & 0x3);
            int maxVal = (1 << bitsPerComponent) - 1;
            uint mask = (uint)maxVal;

            uint qa = (packed >> 2) & mask;
            uint qb = (packed >> (2 + bitsPerComponent)) & mask;
            uint qc = (packed >> (2 + bitsPerComponent * 2)) & mask;

            float range = 1f / Mathf.Sqrt(2f);
            float a = (qa / (float)maxVal * 2f - 1f) * range;
            float b = (qb / (float)maxVal * 2f - 1f) * range;
            float c = (qc / (float)maxVal * 2f - 1f) * range;

            float d = Mathf.Sqrt(Mathf.Max(0f, 1f - a * a - b * b - c * c));

            return maxIndex switch
            {
                0 => new Quaternion(d, a, b, c),
                1 => new Quaternion(a, d, b, c),
                2 => new Quaternion(a, b, d, c),
                _ => new Quaternion(a, b, c, d)
            };
        }

        private uint PackQuaternionFull(Quaternion q)
        {
            byte x = (byte)Mathf.Clamp(Mathf.RoundToInt((q.x * 0.5f + 0.5f) * 255f), 0, 255);
            byte y = (byte)Mathf.Clamp(Mathf.RoundToInt((q.y * 0.5f + 0.5f) * 255f), 0, 255);
            byte z = (byte)Mathf.Clamp(Mathf.RoundToInt((q.z * 0.5f + 0.5f) * 255f), 0, 255);
            byte w = (byte)Mathf.Clamp(Mathf.RoundToInt((q.w * 0.5f + 0.5f) * 255f), 0, 255);
            return (uint)(x | (y << 8) | (z << 16) | (w << 24));
        }

        private Quaternion UnpackQuaternionFull(uint packed)
        {
            float x = ((packed & 0xFF) / 255f) * 2f - 1f;
            float y = (((packed >> 8) & 0xFF) / 255f) * 2f - 1f;
            float z = (((packed >> 16) & 0xFF) / 255f) * 2f - 1f;
            float w = (((packed >> 24) & 0xFF) / 255f) * 2f - 1f;
            return new Quaternion(x, y, z, w).normalized;
        }

        #endregion

        #region Scale

        /// <summary>Scale을 양자화합니다.</summary>
        public QuantizedScale QuantizeScale(Vector3 scale)
        {
            return QuantizeScale(scale, preset.Scale, preset.UseLogScale);
        }

        /// <summary>오버라이드 설정으로 Scale을 양자화합니다.</summary>
        public QuantizedScale QuantizeScale(Vector3 scale, Vector3QuantizationSettings settings, bool useLog)
        {
            Vector3 processedScale = scale;

            if (useLog)
            {
                processedScale = new Vector3(
                    Mathf.Log(Mathf.Max(scale.x, 0.0001f)),
                    Mathf.Log(Mathf.Max(scale.y, 0.0001f)),
                    Mathf.Log(Mathf.Max(scale.z, 0.0001f)));

                var logSettings = new Vector3QuantizationSettings
                {
                    x = QuantizationChannel.Create(
                        Mathf.Log(Mathf.Max(settings.x.minValue, 0.0001f)),
                        Mathf.Log(Mathf.Max(settings.x.maxValue, 0.0001f)),
                        settings.x.bitCount),
                    y = QuantizationChannel.Create(
                        Mathf.Log(Mathf.Max(settings.y.minValue, 0.0001f)),
                        Mathf.Log(Mathf.Max(settings.y.maxValue, 0.0001f)),
                        settings.y.bitCount),
                    z = QuantizationChannel.Create(
                        Mathf.Log(Mathf.Max(settings.z.minValue, 0.0001f)),
                        Mathf.Log(Mathf.Max(settings.z.maxValue, 0.0001f)),
                        settings.z.bitCount),
                    useUniform = settings.useUniform
                };

                var quantized = logSettings.Quantize(processedScale);

                return new QuantizedScale
                {
                    x = (byte)Mathf.Clamp(quantized.x, 0, 255),
                    y = (byte)Mathf.Clamp(quantized.y, 0, 255),
                    z = (byte)Mathf.Clamp(quantized.z, 0, 255)
                };
            }

            var directQuantized = settings.Quantize(processedScale);

            return new QuantizedScale
            {
                x = (byte)Mathf.Clamp(directQuantized.x, 0, 255),
                y = (byte)Mathf.Clamp(directQuantized.y, 0, 255),
                z = (byte)Mathf.Clamp(directQuantized.z, 0, 255)
            };
        }

        /// <summary>양자화된 Scale을 복원합니다.</summary>
        public Vector3 DequantizeScale(QuantizedScale quantized)
        {
            return DequantizeScale(quantized, preset.Scale, preset.UseLogScale);
        }

        /// <summary>오버라이드 설정으로 양자화된 Scale을 복원합니다.</summary>
        public Vector3 DequantizeScale(QuantizedScale quantized, Vector3QuantizationSettings settings, bool useLog)
        {
            if (useLog)
            {
                var logSettings = new Vector3QuantizationSettings
                {
                    x = QuantizationChannel.Create(
                        Mathf.Log(Mathf.Max(settings.x.minValue, 0.0001f)),
                        Mathf.Log(Mathf.Max(settings.x.maxValue, 0.0001f)),
                        settings.x.bitCount),
                    y = QuantizationChannel.Create(
                        Mathf.Log(Mathf.Max(settings.y.minValue, 0.0001f)),
                        Mathf.Log(Mathf.Max(settings.y.maxValue, 0.0001f)),
                        settings.y.bitCount),
                    z = QuantizationChannel.Create(
                        Mathf.Log(Mathf.Max(settings.z.minValue, 0.0001f)),
                        Mathf.Log(Mathf.Max(settings.z.maxValue, 0.0001f)),
                        settings.z.bitCount),
                    useUniform = settings.useUniform
                };

                var vec3uint = new Vector3UInt { x = quantized.x, y = quantized.y, z = quantized.z };
                var logScale = logSettings.Dequantize(vec3uint);

                return new Vector3(
                    Mathf.Exp(logScale.x),
                    Mathf.Exp(logScale.y),
                    Mathf.Exp(logScale.z));
            }

            var directVec = new Vector3UInt { x = quantized.x, y = quantized.y, z = quantized.z };
            return settings.Dequantize(directVec);
        }

        #endregion

        #region Velocity

        /// <summary>Velocity를 양자화합니다.</summary>
        public QuantizedVelocity QuantizeVelocity(Vector3 velocity)
        {
            return QuantizeVelocity(velocity, preset.Velocity);
        }

        /// <summary>오버라이드 설정으로 Velocity를 양자화합니다.</summary>
        public QuantizedVelocity QuantizeVelocity(Vector3 velocity, Vector3QuantizationSettings settings)
        {
            var quantized = settings.Quantize(velocity);

            return new QuantizedVelocity
            {
                x = (short)(quantized.x - 32768),
                y = (short)(quantized.y - 32768),
                z = (short)(quantized.z - 32768)
            };
        }

        /// <summary>양자화된 Velocity를 복원합니다.</summary>
        public Vector3 DequantizeVelocity(QuantizedVelocity quantized)
        {
            return DequantizeVelocity(quantized, preset.Velocity);
        }

        /// <summary>오버라이드 설정으로 양자화된 Velocity를 복원합니다.</summary>
        public Vector3 DequantizeVelocity(QuantizedVelocity quantized, Vector3QuantizationSettings settings)
        {
            var vec3uint = new Vector3UInt
            {
                x = (uint)(quantized.x + 32768),
                y = (uint)(quantized.y + 32768),
                z = (uint)(quantized.z + 32768)
            };

            return settings.Dequantize(vec3uint);
        }

        #endregion

        #region Angular Velocity

        /// <summary>Angular Velocity를 양자화합니다.</summary>
        public QuantizedVelocity QuantizeAngularVelocity(Vector3 angularVelocity)
        {
            return QuantizeAngularVelocity(angularVelocity, preset.AngularVelocity);
        }

        /// <summary>오버라이드 설정으로 Angular Velocity를 양자화합니다.</summary>
        public QuantizedVelocity QuantizeAngularVelocity(Vector3 angularVelocity, Vector3QuantizationSettings settings)
        {
            var quantized = settings.Quantize(angularVelocity);

            return new QuantizedVelocity
            {
                x = (short)(quantized.x - 32768),
                y = (short)(quantized.y - 32768),
                z = (short)(quantized.z - 32768)
            };
        }

        /// <summary>양자화된 Angular Velocity를 복원합니다.</summary>
        public Vector3 DequantizeAngularVelocity(QuantizedVelocity quantized)
        {
            return DequantizeAngularVelocity(quantized, preset.AngularVelocity);
        }

        /// <summary>오버라이드 설정으로 양자화된 Angular Velocity를 복원합니다.</summary>
        public Vector3 DequantizeAngularVelocity(QuantizedVelocity quantized, Vector3QuantizationSettings settings)
        {
            var vec3uint = new Vector3UInt
            {
                x = (uint)(quantized.x + 32768),
                y = (uint)(quantized.y + 32768),
                z = (uint)(quantized.z + 32768)
            };

            return settings.Dequantize(vec3uint);
        }

        #endregion

        #region Complete Transform

        /// <summary>전체 Transform을 양자화합니다.</summary>
        public QuantizedTransformData QuantizeTransform(Vector3 position, Quaternion rotation, Vector3 scale, bool isActive)
        {
            return new QuantizedTransformData
            {
                position = QuantizePosition(position),
                rotation = QuantizeRotation(rotation),
                scale = QuantizeScale(scale),
                flags = (byte)(isActive ? 1 : 0)
            };
        }

        /// <summary>오버라이드 설정으로 전체 Transform을 양자화합니다.</summary>
        public QuantizedTransformData QuantizeTransform(
            Vector3 position, Quaternion rotation, Vector3 scale, bool isActive,
            QuantizationOverride overrideComponent)
        {
            if (overrideComponent == null || !overrideComponent.UseOverride)
            {
                return QuantizeTransform(position, rotation, scale, isActive);
            }

            var posSettings = overrideComponent.GetPositionSettings(preset);
            var rotSettings = overrideComponent.GetRotationSettings(preset);
            var scaleSettings = overrideComponent.GetScaleSettings(preset);

            return new QuantizedTransformData
            {
                position = QuantizePosition(position, posSettings),
                rotation = QuantizeRotation(rotation, rotSettings),
                scale = QuantizeScale(scale, scaleSettings, preset.UseLogScale),
                flags = (byte)(isActive ? 1 : 0)
            };
        }

        /// <summary>양자화된 Transform을 복원합니다.</summary>
        public (Vector3 position, Quaternion rotation, Vector3 scale, bool isActive) DequantizeTransform(QuantizedTransformData data)
        {
            return (
                DequantizePosition(data.position),
                DequantizeRotation(data.rotation),
                DequantizeScale(data.scale),
                (data.flags & 1) != 0
            );
        }

        /// <summary>오버라이드 설정으로 양자화된 Transform을 복원합니다.</summary>
        public (Vector3 position, Quaternion rotation, Vector3 scale, bool isActive) DequantizeTransform(
            QuantizedTransformData data,
            QuantizationOverride overrideComponent)
        {
            if (overrideComponent == null || !overrideComponent.UseOverride)
            {
                return DequantizeTransform(data);
            }

            var posSettings = overrideComponent.GetPositionSettings(preset);
            var rotSettings = overrideComponent.GetRotationSettings(preset);
            var scaleSettings = overrideComponent.GetScaleSettings(preset);

            return (
                DequantizePosition(data.position, posSettings),
                DequantizeRotation(data.rotation, rotSettings),
                DequantizeScale(data.scale, scaleSettings, preset.UseLogScale),
                (data.flags & 1) != 0
            );
        }

        #endregion

        #region Serialization

        /// <summary>양자화된 Transform을 바이너리로 씁니다.</summary>
        public void WriteTransform(BinaryWriter writer, QuantizedTransformData data)
        {
            writer.Write(data.position.x);
            writer.Write(data.position.y);
            writer.Write(data.position.z);
            writer.Write(data.rotation);
            writer.Write(data.scale.x);
            writer.Write(data.scale.y);
            writer.Write(data.scale.z);
            writer.Write(data.flags);
        }

        /// <summary>바이너리에서 양자화된 Transform을 읽습니다.</summary>
        public QuantizedTransformData ReadTransform(BinaryReader reader)
        {
            return new QuantizedTransformData
            {
                position = new QuantizedPosition
                {
                    x = reader.ReadUInt16(),
                    y = reader.ReadUInt16(),
                    z = reader.ReadUInt16()
                },
                rotation = reader.ReadUInt32(),
                scale = new QuantizedScale
                {
                    x = reader.ReadByte(),
                    y = reader.ReadByte(),
                    z = reader.ReadByte()
                },
                flags = reader.ReadByte()
            };
        }

        /// <summary>양자화된 Velocity를 바이너리로 씁니다.</summary>
        public void WriteVelocity(BinaryWriter writer, QuantizedVelocity data)
        {
            writer.Write(data.x);
            writer.Write(data.y);
            writer.Write(data.z);
        }

        /// <summary>바이너리에서 양자화된 Velocity를 읽습니다.</summary>
        public QuantizedVelocity ReadVelocity(BinaryReader reader)
        {
            return new QuantizedVelocity
            {
                x = reader.ReadInt16(),
                y = reader.ReadInt16(),
                z = reader.ReadInt16()
            };
        }

        #endregion
    }

    #region Quantized Data Structures

    /// <summary>양자화된 Position</summary>
    [Serializable]
    public struct QuantizedPosition
    {
        public ushort x;
        public ushort y;
        public ushort z;

        public const int ByteSize = 6;
    }

    /// <summary>양자화된 Scale</summary>
    [Serializable]
    public struct QuantizedScale
    {
        public byte x;
        public byte y;
        public byte z;

        public const int ByteSize = 3;
    }

    /// <summary>양자화된 Velocity/AngularVelocity</summary>
    [Serializable]
    public struct QuantizedVelocity
    {
        public short x;
        public short y;
        public short z;

        public const int ByteSize = 6;
    }

    /// <summary>양자화된 전체 Transform 데이터</summary>
    [Serializable]
    public struct QuantizedTransformData
    {
        public QuantizedPosition position;
        public uint rotation;
        public QuantizedScale scale;
        public byte flags;

        public const int ByteSize = 14; // 6 + 4 + 3 + 1
    }

    /// <summary>양자화된 전체 Rigidbody 데이터</summary>
    [Serializable]
    public struct QuantizedRigidbodyData
    {
        public QuantizedVelocity velocity;
        public QuantizedVelocity angularVelocity;

        public const int ByteSize = 12; // 6 + 6
    }

    #endregion
}
