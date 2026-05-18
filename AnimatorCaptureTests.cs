using System.IO;
using NUnit.Framework;
using ReplaySystem.Core;
using UnityEngine;

namespace ReplaySystem.Tests
{
    /// <summary>
    /// AnimatorCapture 관련 유닛 테스트
    /// 주의: 실제 Animator 컴포넌트가 필요한 테스트는 PlayMode 테스트로 분리
    /// </summary>
    [TestFixture]
    public class AnimatorCaptureTests
    {
        #region CompressedAnimatorState Tests

        [Test]
        public void CompressedAnimatorState_DefaultValues_AreZero()
        {
            var state = new CompressedAnimatorState();

            Assert.AreEqual(0, state.LayerCount, "LayerCount should be 0 by default");
            Assert.AreEqual(0, state.Layer0.StateHash, "Layer0 StateHash should be 0 by default");
            Assert.IsNull(state.ParameterData, "ParameterData should be null by default");
        }

        [Test]
        public void CompressedAnimatorState_LayerCount_MaxFour()
        {
            var state = new CompressedAnimatorState { LayerCount = 4 };

            Assert.AreEqual(4, state.LayerCount);
        }

        [Test]
        public void CompressedAnimatorState_LayerStates_IndependentStorage()
        {
            var state = new CompressedAnimatorState
            {
                LayerCount = 4,
                Layer0 = new CompressedAnimatorLayerState { StateHash = 100 },
                Layer1 = new CompressedAnimatorLayerState { StateHash = 200 },
                Layer2 = new CompressedAnimatorLayerState { StateHash = 300 },
                Layer3 = new CompressedAnimatorLayerState { StateHash = 400 }
            };

            Assert.AreEqual(100, state.Layer0.StateHash);
            Assert.AreEqual(200, state.Layer1.StateHash);
            Assert.AreEqual(300, state.Layer2.StateHash);
            Assert.AreEqual(400, state.Layer3.StateHash);
        }

        #endregion

        #region CompressedAnimatorLayerState Tests

        [Test]
        public void CompressedAnimatorLayerState_NormalizedTime_QuantizationRange()
        {
            // 16-bit quantization: 0-65535
            var layerMin = new CompressedAnimatorLayerState { NormalizedTime = 0 };
            var layerMax = new CompressedAnimatorLayerState { NormalizedTime = ushort.MaxValue };

            Assert.AreEqual(0, layerMin.NormalizedTime);
            Assert.AreEqual(65535, layerMax.NormalizedTime);
        }

        [Test]
        public void CompressedAnimatorLayerState_Weight_QuantizationRange()
        {
            // 8-bit quantization: 0-255
            var layerMin = new CompressedAnimatorLayerState { Weight = 0 };
            var layerMax = new CompressedAnimatorLayerState { Weight = byte.MaxValue };

            Assert.AreEqual(0, layerMin.Weight);
            Assert.AreEqual(255, layerMax.Weight);
        }

        [Test]
        public void CompressedAnimatorLayerState_Transition_StoresNextState()
        {
            var layer = new CompressedAnimatorLayerState
            {
                StateHash = 12345,
                IsInTransition = true,
                NextStateHash = 67890,
                TransitionNormalizedTime = 32768 // 50%
            };

            Assert.IsTrue(layer.IsInTransition);
            Assert.AreEqual(12345, layer.StateHash);
            Assert.AreEqual(67890, layer.NextStateHash);
            Assert.AreEqual(32768, layer.TransitionNormalizedTime);
        }

        [Test]
        public void CompressedAnimatorLayerState_NoTransition_NextStateIgnored()
        {
            var layer = new CompressedAnimatorLayerState
            {
                StateHash = 12345,
                IsInTransition = false,
                NextStateHash = 0
            };

            Assert.IsFalse(layer.IsInTransition);
            Assert.AreEqual(0, layer.NextStateHash);
        }

        #endregion

        #region CompressedAnimatorDelta Tests

        [Test]
        public void CompressedAnimatorDelta_StoresObjectIdAndState()
        {
            var delta = new CompressedAnimatorDelta
            {
                ObjectId = 42,
                State = new CompressedAnimatorState
                {
                    LayerCount = 1,
                    Layer0 = new CompressedAnimatorLayerState { StateHash = 999 }
                }
            };

            Assert.AreEqual(42, delta.ObjectId);
            Assert.AreEqual(1, delta.State.LayerCount);
            Assert.AreEqual(999, delta.State.Layer0.StateHash);
        }

        #endregion

        #region Quantization Helper Tests

        [Test]
        public void QuantizeNormalizedTime_ZeroReturnsZero()
        {
            ushort result = QuantizeNormalizedTime(0f);
            Assert.AreEqual(0, result);
        }

        [Test]
        public void QuantizeNormalizedTime_OneReturnsMax()
        {
            ushort result = QuantizeNormalizedTime(1f);
            Assert.AreEqual(ushort.MaxValue, result);
        }

        [Test]
        public void QuantizeNormalizedTime_HalfReturnsMiddle()
        {
            ushort result = QuantizeNormalizedTime(0.5f);
            // 0.5 * 65535 = 32767.5 -> 32767 or 32768
            Assert.That(result, Is.InRange(32767, 32768));
        }

        [Test]
        public void QuantizeNormalizedTime_LoopingAnimation_UsesFractional()
        {
            // normalizedTime 2.5 -> fractional 0.5
            ushort result = QuantizeNormalizedTime(2.5f);
            Assert.That(result, Is.InRange(32767, 32768), "Should use fractional part only");
        }

        [Test]
        public void DequantizeNormalizedTime_RoundTrip_PreservesValue()
        {
            float original = 0.75f;
            ushort quantized = QuantizeNormalizedTime(original);
            float restored = DequantizeNormalizedTime(quantized);

            Assert.AreEqual(original, restored, 0.001f, "Round-trip should preserve value within tolerance");
        }

        [Test]
        public void QuantizeWeight_ZeroReturnsZero()
        {
            byte result = QuantizeWeight(0f);
            Assert.AreEqual(0, result);
        }

        [Test]
        public void QuantizeWeight_OneReturnsMax()
        {
            byte result = QuantizeWeight(1f);
            Assert.AreEqual(255, result);
        }

        [Test]
        public void DequantizeWeight_RoundTrip_PreservesValue()
        {
            float original = 0.75f;
            byte quantized = QuantizeWeight(original);
            float restored = DequantizeWeight(quantized);

            Assert.AreEqual(original, restored, 0.01f, "Round-trip should preserve value within tolerance");
        }

        #endregion

        #region Parameter Serialization Tests

        [Test]
        public void ParameterSerialization_Float_RoundTrip()
        {
            float original = 3.14159f;
            byte[] data = SerializeFloat(original);
            float restored = DeserializeFloat(data);

            Assert.AreEqual(original, restored, 0.00001f);
        }

        [Test]
        public void ParameterSerialization_Int_RoundTrip()
        {
            int original = -12345;
            byte[] data = SerializeInt(original);
            int restored = DeserializeInt(data);

            Assert.AreEqual(original, restored);
        }

        [Test]
        public void ParameterSerialization_Bool_True_RoundTrip()
        {
            byte[] data = SerializeBool(true);
            bool restored = DeserializeBool(data);

            Assert.IsTrue(restored);
        }

        [Test]
        public void ParameterSerialization_Bool_False_RoundTrip()
        {
            byte[] data = SerializeBool(false);
            bool restored = DeserializeBool(data);

            Assert.IsFalse(restored);
        }

        [Test]
        public void ParameterSerialization_MultipleParams_OrderPreserved()
        {
            // Simulate parameter buffer: float, int, bool
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(1.5f);  // float
                writer.Write(42);    // int
                writer.Write(true);  // bool (as byte)
                var data = ms.ToArray();

                using (var readMs = new MemoryStream(data))
                using (var reader = new BinaryReader(readMs))
                {
                    Assert.AreEqual(1.5f, reader.ReadSingle(), 0.001f);
                    Assert.AreEqual(42, reader.ReadInt32());
                    Assert.AreEqual(true, reader.ReadBoolean());
                }
            }
        }

        #endregion

        #region Delta Compression Logic Tests

        [Test]
        public void HasLayerChanged_SameState_ReturnsFalse()
        {
            var a = new CompressedAnimatorLayerState
            {
                StateHash = 12345,
                NormalizedTime = 1000,
                Weight = 128,
                IsInTransition = false
            };

            var b = new CompressedAnimatorLayerState
            {
                StateHash = 12345,
                NormalizedTime = 1000,
                Weight = 128,
                IsInTransition = false
            };

            Assert.IsFalse(HasLayerChanged(a, b));
        }

        [Test]
        public void HasLayerChanged_DifferentStateHash_ReturnsTrue()
        {
            var a = new CompressedAnimatorLayerState { StateHash = 100 };
            var b = new CompressedAnimatorLayerState { StateHash = 200 };

            Assert.IsTrue(HasLayerChanged(a, b));
        }

        [Test]
        public void HasLayerChanged_DifferentWeight_ReturnsTrue()
        {
            var a = new CompressedAnimatorLayerState { StateHash = 100, Weight = 100 };
            var b = new CompressedAnimatorLayerState { StateHash = 100, Weight = 200 };

            Assert.IsTrue(HasLayerChanged(a, b));
        }

        [Test]
        public void HasLayerChanged_TransitionStateChange_ReturnsTrue()
        {
            var a = new CompressedAnimatorLayerState { StateHash = 100, IsInTransition = false };
            var b = new CompressedAnimatorLayerState { StateHash = 100, IsInTransition = true };

            Assert.IsTrue(HasLayerChanged(a, b));
        }

        [Test]
        public void HasLayerChanged_SmallTimeChange_BelowThreshold_ReturnsFalse()
        {
            // Threshold is 0.01 * 65535 = ~655
            var a = new CompressedAnimatorLayerState { StateHash = 100, NormalizedTime = 1000 };
            var b = new CompressedAnimatorLayerState { StateHash = 100, NormalizedTime = 1100 }; // diff = 100 < 655

            Assert.IsFalse(HasLayerChanged(a, b), "Small time difference should be ignored");
        }

        [Test]
        public void HasLayerChanged_LargeTimeChange_AboveThreshold_ReturnsTrue()
        {
            // Threshold is 0.01 * 65535 = ~655
            var a = new CompressedAnimatorLayerState { StateHash = 100, NormalizedTime = 1000 };
            var b = new CompressedAnimatorLayerState { StateHash = 100, NormalizedTime = 2000 }; // diff = 1000 > 655

            Assert.IsTrue(HasLayerChanged(a, b), "Large time difference should trigger change");
        }

        [Test]
        public void HasStateChanged_SameLayerCount_SameStates_ReturnsFalse()
        {
            var a = new CompressedAnimatorState
            {
                LayerCount = 1,
                Layer0 = new CompressedAnimatorLayerState { StateHash = 100, NormalizedTime = 1000 }
            };

            var b = new CompressedAnimatorState
            {
                LayerCount = 1,
                Layer0 = new CompressedAnimatorLayerState { StateHash = 100, NormalizedTime = 1000 }
            };

            Assert.IsFalse(HasStateChanged(a, b));
        }

        [Test]
        public void HasStateChanged_DifferentLayerCount_ReturnsTrue()
        {
            var a = new CompressedAnimatorState { LayerCount = 1 };
            var b = new CompressedAnimatorState { LayerCount = 2 };

            Assert.IsTrue(HasStateChanged(a, b));
        }

        [Test]
        public void HasStateChanged_DifferentParameterData_ReturnsTrue()
        {
            var a = new CompressedAnimatorState
            {
                LayerCount = 1,
                ParameterData = new byte[] { 1, 2, 3 }
            };

            var b = new CompressedAnimatorState
            {
                LayerCount = 1,
                ParameterData = new byte[] { 1, 2, 4 }
            };

            Assert.IsTrue(HasStateChanged(a, b));
        }

        [Test]
        public void HasStateChanged_BothNullParameterData_ReturnsFalse()
        {
            var a = new CompressedAnimatorState { LayerCount = 1, ParameterData = null };
            var b = new CompressedAnimatorState { LayerCount = 1, ParameterData = null };

            Assert.IsFalse(HasStateChanged(a, b));
        }

        #endregion

        #region Helper Methods (mirroring AnimatorCapture logic)

        private const float NORMALIZED_TIME_THRESHOLD = 0.01f;
        private const ushort NORMALIZED_TIME_MAX = ushort.MaxValue;
        private const byte WEIGHT_MAX = byte.MaxValue;

        private static ushort QuantizeNormalizedTime(float normalizedTime)
        {
            float fractional = normalizedTime - Mathf.Floor(normalizedTime);
            return (ushort)(Mathf.Clamp01(fractional) * NORMALIZED_TIME_MAX);
        }

        private static float DequantizeNormalizedTime(ushort quantized)
        {
            return quantized / (float)NORMALIZED_TIME_MAX;
        }

        private static byte QuantizeWeight(float weight)
        {
            return (byte)(Mathf.Clamp01(weight) * WEIGHT_MAX);
        }

        private static float DequantizeWeight(byte quantized)
        {
            return quantized / (float)WEIGHT_MAX;
        }

        private static bool HasLayerChanged(CompressedAnimatorLayerState a, CompressedAnimatorLayerState b)
        {
            if (a.StateHash != b.StateHash) return true;
            if (a.IsInTransition != b.IsInTransition) return true;
            if (a.Weight != b.Weight) return true;

            int timeDiff = Mathf.Abs(a.NormalizedTime - b.NormalizedTime);
            if (timeDiff > (int)(NORMALIZED_TIME_THRESHOLD * NORMALIZED_TIME_MAX)) return true;

            return false;
        }

        private static bool HasStateChanged(CompressedAnimatorState a, CompressedAnimatorState b)
        {
            if (a.LayerCount != b.LayerCount) return true;

            if (a.LayerCount > 0 && HasLayerChanged(a.Layer0, b.Layer0)) return true;
            if (a.LayerCount > 1 && HasLayerChanged(a.Layer1, b.Layer1)) return true;
            if (a.LayerCount > 2 && HasLayerChanged(a.Layer2, b.Layer2)) return true;
            if (a.LayerCount > 3 && HasLayerChanged(a.Layer3, b.Layer3)) return true;

            if (!ArrayEquals(a.ParameterData, b.ParameterData)) return true;

            return false;
        }

        private static bool ArrayEquals(byte[] a, byte[] b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }

            return true;
        }

        private static byte[] SerializeFloat(float value)
        {
            return System.BitConverter.GetBytes(value);
        }

        private static float DeserializeFloat(byte[] data)
        {
            return System.BitConverter.ToSingle(data, 0);
        }

        private static byte[] SerializeInt(int value)
        {
            return System.BitConverter.GetBytes(value);
        }

        private static int DeserializeInt(byte[] data)
        {
            return System.BitConverter.ToInt32(data, 0);
        }

        private static byte[] SerializeBool(bool value)
        {
            return new byte[] { value ? (byte)1 : (byte)0 };
        }

        private static bool DeserializeBool(byte[] data)
        {
            return data[0] != 0;
        }

        #endregion
    }
}
