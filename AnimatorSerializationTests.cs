using System.IO;
using NUnit.Framework;
using ReplaySystem.Core;
using ReplaySystem.Serialization;

namespace ReplaySystem.Tests
{
    /// <summary>
    /// Animator 상태 직렬화/역직렬화 테스트
    /// </summary>
    [TestFixture]
    public class AnimatorSerializationTests
    {
        #region WriteCompressedAnimatorState Tests

        [Test]
        public void WriteCompressedAnimatorState_SingleLayer_RoundTrip()
        {
            var original = new CompressedAnimatorState
            {
                LayerCount = 1,
                Layer0 = new CompressedAnimatorLayerState
                {
                    StateHash = 12345,
                    NormalizedTime = 32768,
                    Weight = 255,
                    IsInTransition = false
                },
                ParameterData = new byte[] { 1, 2, 3, 4 }
            };

            byte[] serialized = SerializeAnimatorState(original);
            var restored = DeserializeAnimatorState(serialized);

            Assert.AreEqual(original.LayerCount, restored.LayerCount);
            Assert.AreEqual(original.Layer0.StateHash, restored.Layer0.StateHash);
            Assert.AreEqual(original.Layer0.NormalizedTime, restored.Layer0.NormalizedTime);
            Assert.AreEqual(original.Layer0.Weight, restored.Layer0.Weight);
            Assert.AreEqual(original.Layer0.IsInTransition, restored.Layer0.IsInTransition);
            CollectionAssert.AreEqual(original.ParameterData, restored.ParameterData);
        }

        [Test]
        public void WriteCompressedAnimatorState_MultipleLayer_RoundTrip()
        {
            var original = new CompressedAnimatorState
            {
                LayerCount = 4,
                Layer0 = new CompressedAnimatorLayerState { StateHash = 100, NormalizedTime = 1000, Weight = 255 },
                Layer1 = new CompressedAnimatorLayerState { StateHash = 200, NormalizedTime = 2000, Weight = 200 },
                Layer2 = new CompressedAnimatorLayerState { StateHash = 300, NormalizedTime = 3000, Weight = 150 },
                Layer3 = new CompressedAnimatorLayerState { StateHash = 400, NormalizedTime = 4000, Weight = 100 }
            };

            byte[] serialized = SerializeAnimatorState(original);
            var restored = DeserializeAnimatorState(serialized);

            Assert.AreEqual(4, restored.LayerCount);
            Assert.AreEqual(100, restored.Layer0.StateHash);
            Assert.AreEqual(200, restored.Layer1.StateHash);
            Assert.AreEqual(300, restored.Layer2.StateHash);
            Assert.AreEqual(400, restored.Layer3.StateHash);
        }

        [Test]
        public void WriteCompressedAnimatorState_WithTransition_RoundTrip()
        {
            var original = new CompressedAnimatorState
            {
                LayerCount = 1,
                Layer0 = new CompressedAnimatorLayerState
                {
                    StateHash = 12345,
                    NormalizedTime = 16384,
                    Weight = 255,
                    IsInTransition = true,
                    NextStateHash = 67890,
                    TransitionNormalizedTime = 32768
                }
            };

            byte[] serialized = SerializeAnimatorState(original);
            var restored = DeserializeAnimatorState(serialized);

            Assert.IsTrue(restored.Layer0.IsInTransition);
            Assert.AreEqual(67890, restored.Layer0.NextStateHash);
            Assert.AreEqual(32768, restored.Layer0.TransitionNormalizedTime);
        }

        [Test]
        public void WriteCompressedAnimatorState_NoParameterData_RoundTrip()
        {
            var original = new CompressedAnimatorState
            {
                LayerCount = 1,
                Layer0 = new CompressedAnimatorLayerState { StateHash = 999 },
                ParameterData = null
            };

            byte[] serialized = SerializeAnimatorState(original);
            var restored = DeserializeAnimatorState(serialized);

            Assert.IsNull(restored.ParameterData);
        }

        [Test]
        public void WriteCompressedAnimatorState_EmptyParameterData_RoundTrip()
        {
            var original = new CompressedAnimatorState
            {
                LayerCount = 1,
                Layer0 = new CompressedAnimatorLayerState { StateHash = 999 },
                ParameterData = new byte[0]
            };

            byte[] serialized = SerializeAnimatorState(original);
            var restored = DeserializeAnimatorState(serialized);

            Assert.IsNotNull(restored.ParameterData);
            Assert.AreEqual(0, restored.ParameterData.Length);
        }

        #endregion

        #region WriteAnimatorLayerState Tests

        [Test]
        public void WriteAnimatorLayerState_BasicState_RoundTrip()
        {
            var original = new CompressedAnimatorLayerState
            {
                StateHash = -123456789,
                NormalizedTime = 45000,
                Weight = 128,
                IsInTransition = false
            };

            byte[] serialized = SerializeLayerState(original);
            var restored = DeserializeLayerState(serialized);

            Assert.AreEqual(original.StateHash, restored.StateHash);
            Assert.AreEqual(original.NormalizedTime, restored.NormalizedTime);
            Assert.AreEqual(original.Weight, restored.Weight);
            Assert.AreEqual(original.IsInTransition, restored.IsInTransition);
        }

        [Test]
        public void WriteAnimatorLayerState_NegativeStateHash_Preserved()
        {
            // StateHash는 음수일 수 있음 (Unity Animator.StringToHash 결과)
            var original = new CompressedAnimatorLayerState
            {
                StateHash = int.MinValue,
                IsInTransition = false
            };

            byte[] serialized = SerializeLayerState(original);
            var restored = DeserializeLayerState(serialized);

            Assert.AreEqual(int.MinValue, restored.StateHash);
        }

        [Test]
        public void WriteAnimatorLayerState_MaxValues_RoundTrip()
        {
            var original = new CompressedAnimatorLayerState
            {
                StateHash = int.MaxValue,
                NormalizedTime = ushort.MaxValue,
                Weight = byte.MaxValue,
                IsInTransition = true,
                NextStateHash = int.MaxValue,
                TransitionNormalizedTime = ushort.MaxValue
            };

            byte[] serialized = SerializeLayerState(original);
            var restored = DeserializeLayerState(serialized);

            Assert.AreEqual(int.MaxValue, restored.StateHash);
            Assert.AreEqual(ushort.MaxValue, restored.NormalizedTime);
            Assert.AreEqual(byte.MaxValue, restored.Weight);
            Assert.AreEqual(int.MaxValue, restored.NextStateHash);
            Assert.AreEqual(ushort.MaxValue, restored.TransitionNormalizedTime);
        }

        #endregion

        #region Binary Size Tests

        [Test]
        public void AnimatorLayerState_BinarySize_NoTransition_Is7Bytes()
        {
            // StateHash (4) + NormalizedTime (2) + Weight (1) + IsInTransition (1) = 8 bytes
            var state = new CompressedAnimatorLayerState
            {
                StateHash = 12345,
                NormalizedTime = 32768,
                Weight = 255,
                IsInTransition = false
            };

            byte[] serialized = SerializeLayerState(state);

            // 4 (StateHash) + 2 (NormalizedTime) + 1 (Weight) + 1 (IsInTransition) = 8
            Assert.AreEqual(8, serialized.Length);
        }

        [Test]
        public void AnimatorLayerState_BinarySize_WithTransition_Is14Bytes()
        {
            // StateHash (4) + NormalizedTime (2) + Weight (1) + IsInTransition (1)
            // + NextStateHash (4) + TransitionNormalizedTime (2) = 14 bytes
            var state = new CompressedAnimatorLayerState
            {
                StateHash = 12345,
                NormalizedTime = 32768,
                Weight = 255,
                IsInTransition = true,
                NextStateHash = 67890,
                TransitionNormalizedTime = 16384
            };

            byte[] serialized = SerializeLayerState(state);

            // 8 (basic) + 4 (NextStateHash) + 2 (TransitionNormalizedTime) = 14
            Assert.AreEqual(14, serialized.Length);
        }

        [Test]
        public void AnimatorState_BinarySize_SingleLayerNoParams()
        {
            var state = new CompressedAnimatorState
            {
                LayerCount = 1,
                Layer0 = new CompressedAnimatorLayerState
                {
                    StateHash = 12345,
                    IsInTransition = false
                },
                ParameterData = null
            };

            byte[] serialized = SerializeAnimatorState(state);

            // LayerCount (1) + Layer0 (8) + ParameterDataLength (-1 for null, stored as 4 bytes) = 13
            Assert.AreEqual(13, serialized.Length);
        }

        #endregion

        #region CompressedAnimatorDelta Tests

        [Test]
        public void CompressedAnimatorDelta_RoundTrip()
        {
            var original = new CompressedAnimatorDelta
            {
                ObjectId = 42,
                State = new CompressedAnimatorState
                {
                    LayerCount = 2,
                    Layer0 = new CompressedAnimatorLayerState { StateHash = 111 },
                    Layer1 = new CompressedAnimatorLayerState { StateHash = 222, Weight = 128 }
                }
            };

            byte[] serialized = SerializeAnimatorDelta(original);
            var restored = DeserializeAnimatorDelta(serialized);

            Assert.AreEqual(42, restored.ObjectId);
            Assert.AreEqual(2, restored.State.LayerCount);
            Assert.AreEqual(111, restored.State.Layer0.StateHash);
            Assert.AreEqual(222, restored.State.Layer1.StateHash);
            Assert.AreEqual(128, restored.State.Layer1.Weight);
        }

        #endregion

        #region Helper Methods

        private static byte[] SerializeAnimatorState(CompressedAnimatorState state)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                ReplaySerializer.WriteCompressedAnimatorState(writer, state);
                return ms.ToArray();
            }
        }

        private static CompressedAnimatorState DeserializeAnimatorState(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                return ReplayDeserializer.ReadCompressedAnimatorState(reader);
            }
        }

        private static byte[] SerializeLayerState(CompressedAnimatorLayerState layer)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
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

                return ms.ToArray();
            }
        }

        private static CompressedAnimatorLayerState DeserializeLayerState(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
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
        }

        private static byte[] SerializeAnimatorDelta(CompressedAnimatorDelta delta)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(delta.ObjectId);
                ReplaySerializer.WriteCompressedAnimatorState(writer, delta.State);
                return ms.ToArray();
            }
        }

        private static CompressedAnimatorDelta DeserializeAnimatorDelta(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                return new CompressedAnimatorDelta
                {
                    ObjectId = reader.ReadInt32(),
                    State = ReplayDeserializer.ReadCompressedAnimatorState(reader)
                };
            }
        }

        #endregion
    }
}
