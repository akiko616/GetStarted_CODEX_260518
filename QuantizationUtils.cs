using System;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;

namespace ReplaySystem.Compression
{
    /// <summary>데이터 양자화 유틸리티입니다.</summary>
    public static class QuantizationUtils
    {
        /// <summary>Half-precision 변환 (16-bit float)</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort FloatToHalf(float value)
        {
            uint halfBits = math.f32tof16(value);
            return (ushort)(halfBits & 0xFFFF);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float HalfToFloat(ushort value)
        {
            return math.f16tof32(value);
        }

        /// <summary>Vector3를 Half-precision으로 압축 (6 bytes)</summary>
        public static void PackVector3Half(Vector3 v, Span<byte> buffer)
        {
            ushort x = FloatToHalf(v.x);
            ushort y = FloatToHalf(v.y);
            ushort z = FloatToHalf(v.z);

            buffer[0] = (byte)(x & 0xFF);
            buffer[1] = (byte)(x >> 8);
            buffer[2] = (byte)(y & 0xFF);
            buffer[3] = (byte)(y >> 8);
            buffer[4] = (byte)(z & 0xFF);
            buffer[5] = (byte)(z >> 8);
        }

        public static Vector3 UnpackVector3Half(ReadOnlySpan<byte> buffer)
        {
            ushort x = (ushort)(buffer[0] | (buffer[1] << 8));
            ushort y = (ushort)(buffer[2] | (buffer[3] << 8));
            ushort z = (ushort)(buffer[4] | (buffer[5] << 8));

            return new Vector3(HalfToFloat(x), HalfToFloat(y), HalfToFloat(z));
        }

        /// <summary>
        /// Smallest-3 Quaternion 압축 (4 bytes, 10-10-10-2 bit)
        /// 가장 큰 컴포넌트를 제외하고 3개만 저장
        /// </summary>
        public static uint PackQuaternionSmallest3(Quaternion q)
        {
            float[] components = { q.x, q.y, q.z, q.w };
            int largestIndex = 0;
            float largestValue = Mathf.Abs(components[0]);

            for (int i = 1; i < 4; i++)
            {
                float absValue = Mathf.Abs(components[i]);

                if (absValue > largestValue)
                {
                    largestValue = absValue;
                    largestIndex = i;
                }
            }

            if (components[largestIndex] < 0)
            {
                q.x = -q.x;
                q.y = -q.y;
                q.z = -q.z;
                q.w = -q.w;
                components[0] = q.x;
                components[1] = q.y;
                components[2] = q.z;
                components[3] = q.w;
            }

            const float SQRT2_OVER_2 = 0.7071068f;
            const int BITS_PER_COMPONENT = 10;
            const int MAX_VALUE = (1 << BITS_PER_COMPONENT) - 1;

            uint packed = (uint)largestIndex;
            int shift = 2;

            for (int i = 0; i < 4; i++)
            {
                if (i == largestIndex)
                {
                    continue;
                }

                float normalized = (components[i] / SQRT2_OVER_2 + 1f) * 0.5f;
                uint quantized = (uint)Mathf.Clamp(Mathf.RoundToInt(normalized * MAX_VALUE), 0, MAX_VALUE);
                packed |= quantized << shift;
                shift += BITS_PER_COMPONENT;
            }

            return packed;
        }

        public static Quaternion UnpackQuaternionSmallest3(uint packed)
        {
            const float SQRT2_OVER_2 = 0.7071068f;
            const int BITS_PER_COMPONENT = 10;
            const int MAX_VALUE = (1 << BITS_PER_COMPONENT) - 1;

            int largestIndex = (int)(packed & 0x3);
            float[] components = new float[4];

            int shift = 2;
            float sumSquares = 0f;

            for (int i = 0; i < 4; i++)
            {
                if (i == largestIndex)
                {
                    continue;
                }

                uint quantized = (packed >> shift) & (uint)MAX_VALUE;
                float normalized = quantized / (float)MAX_VALUE;
                float value = (normalized * 2f - 1f) * SQRT2_OVER_2;
                components[i] = value;
                sumSquares += value * value;
                shift += BITS_PER_COMPONENT;
            }

            components[largestIndex] = Mathf.Sqrt(1f - sumSquares);

            return new Quaternion(components[0], components[1], components[2], components[3]);
        }

        /// <summary>속도 양자화 (signed 16-bit, 범위: -500 ~ 500)</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short QuantizeVelocity(float velocity, float maxVelocity = 500f)
        {
            float normalized = Mathf.Clamp(velocity / maxVelocity, -1f, 1f);
            return (short)(normalized * short.MaxValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DequantizeVelocity(short quantized, float maxVelocity = 500f)
        {
            return (quantized / (float)short.MaxValue) * maxVelocity;
        }

        /// <summary>스케일 양자화 (8-bit, 범위: 0.01 ~ 10)</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte QuantizeScale(float scale)
        {
            float log = Mathf.Log10(Mathf.Clamp(scale, 0.01f, 10f));
            float normalized = (log + 2f) / 3f;
            return (byte)(normalized * 255f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DequantizeScale(byte quantized)
        {
            float normalized = quantized / 255f;
            float log = normalized * 3f - 2f;
            return Mathf.Pow(10f, log);
        }
    }

    /// <summary>압축된 Transform 데이터 (19 bytes)</summary>
    [Serializable]
    public struct CompressedTransform
    {
        /// <summary>Position (6 bytes - Half precision)</summary>
        public ushort PosX;
        public ushort PosY;
        public ushort PosZ;

        /// <summary>Rotation (4 bytes - Smallest-3)</summary>
        public uint Rotation;

        /// <summary>Scale (3 bytes - 8-bit quantized)</summary>
        public byte ScaleX;
        public byte ScaleY;
        public byte ScaleZ;

        /// <summary>Active flag (1 byte)</summary>
        public byte Flags;

        public static CompressedTransform FromTransform(Transform t, bool isActive)
        {
            var pos = t.position;
            var rot = t.rotation;
            var scale = t.localScale;

            return new CompressedTransform
            {
                PosX = QuantizationUtils.FloatToHalf(pos.x),
                PosY = QuantizationUtils.FloatToHalf(pos.y),
                PosZ = QuantizationUtils.FloatToHalf(pos.z),
                Rotation = QuantizationUtils.PackQuaternionSmallest3(rot),
                ScaleX = QuantizationUtils.QuantizeScale(scale.x),
                ScaleY = QuantizationUtils.QuantizeScale(scale.y),
                ScaleZ = QuantizationUtils.QuantizeScale(scale.z),
                Flags = (byte)(isActive ? 1 : 0)
            };
        }

        public void ApplyToTransform(Transform t)
        {
            t.position = new Vector3(
                QuantizationUtils.HalfToFloat(PosX),
                QuantizationUtils.HalfToFloat(PosY),
                QuantizationUtils.HalfToFloat(PosZ)
            );

            t.rotation = QuantizationUtils.UnpackQuaternionSmallest3(Rotation);

            t.localScale = new Vector3(
                QuantizationUtils.DequantizeScale(ScaleX),
                QuantizationUtils.DequantizeScale(ScaleY),
                QuantizationUtils.DequantizeScale(ScaleZ)
            );

            t.gameObject.SetActive((Flags & 1) != 0);
        }

        public Vector3 GetPosition()
        {
            return new Vector3(
                QuantizationUtils.HalfToFloat(PosX),
                QuantizationUtils.HalfToFloat(PosY),
                QuantizationUtils.HalfToFloat(PosZ)
            );
        }

        public Quaternion GetRotation()
        {
            return QuantizationUtils.UnpackQuaternionSmallest3(Rotation);
        }

        public Vector3 GetScale()
        {
            return new Vector3(
                QuantizationUtils.DequantizeScale(ScaleX),
                QuantizationUtils.DequantizeScale(ScaleY),
                QuantizationUtils.DequantizeScale(ScaleZ)
            );
        }

        public bool IsActive => (Flags & 1) != 0;
    }

    /// <summary>압축된 Rigidbody 데이터 (12 bytes)</summary>
    [Serializable]
    public struct CompressedRigidbody
    {
        /// <summary>Linear Velocity (6 bytes)</summary>
        public short VelX;
        public short VelY;
        public short VelZ;

        /// <summary>Angular Velocity (6 bytes)</summary>
        public short AngVelX;
        public short AngVelY;
        public short AngVelZ;

        public static CompressedRigidbody FromRigidbody(Rigidbody rb)
        {
            var vel = rb.linearVelocity;
            var angVel = rb.angularVelocity;

            return new CompressedRigidbody
            {
                VelX = QuantizationUtils.QuantizeVelocity(vel.x),
                VelY = QuantizationUtils.QuantizeVelocity(vel.y),
                VelZ = QuantizationUtils.QuantizeVelocity(vel.z),
                AngVelX = QuantizationUtils.QuantizeVelocity(angVel.x, 50f),
                AngVelY = QuantizationUtils.QuantizeVelocity(angVel.y, 50f),
                AngVelZ = QuantizationUtils.QuantizeVelocity(angVel.z, 50f)
            };
        }

        public void ApplyToRigidbody(Rigidbody rb)
        {
            rb.linearVelocity = new Vector3(
                QuantizationUtils.DequantizeVelocity(VelX),
                QuantizationUtils.DequantizeVelocity(VelY),
                QuantizationUtils.DequantizeVelocity(VelZ)
            );

            rb.angularVelocity = new Vector3(
                QuantizationUtils.DequantizeVelocity(AngVelX, 50f),
                QuantizationUtils.DequantizeVelocity(AngVelY, 50f),
                QuantizationUtils.DequantizeVelocity(AngVelZ, 50f)
            );
        }

        public Vector3 GetLinearVelocity()
        {
            return new Vector3(
                QuantizationUtils.DequantizeVelocity(VelX),
                QuantizationUtils.DequantizeVelocity(VelY),
                QuantizationUtils.DequantizeVelocity(VelZ)
            );
        }

        public Vector3 GetAngularVelocity()
        {
            return new Vector3(
                QuantizationUtils.DequantizeVelocity(AngVelX, 50f),
                QuantizationUtils.DequantizeVelocity(AngVelY, 50f),
                QuantizationUtils.DequantizeVelocity(AngVelZ, 50f)
            );
        }
    }
}
