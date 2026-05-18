using System;
using System.Collections.Generic;
using ReplaySystem.Core;
using UnityEngine;

namespace ReplaySystem.Capture
{
    /// <summary>Animator 캡처 컴포넌트입니다.</summary>
    [RequireComponent(typeof(TransformCapture))]
    public class AnimatorCapture : MonoBehaviour, IAnimatorCapture
    {
        #region Constants

        /// <summary>지원하는 최대 레이어 수</summary>
        public const int MAX_LAYERS = 4;

        /// <summary>정규화 시간 델타 임계값 (이 이상 변화 시 기록)</summary>
        private const float NORMALIZED_TIME_THRESHOLD = 0.01f;

        /// <summary>16-bit 정규화 시간 양자화 최대값</summary>
        private const ushort NORMALIZED_TIME_MAX = ushort.MaxValue;

        /// <summary>8-bit 가중치 양자화 최대값</summary>
        private const byte WEIGHT_MAX = byte.MaxValue;

        #endregion

        #region Serialized Fields

        [Header("Settings")]
        [SerializeField, Tooltip("파라미터 캡처 활성화 여부")]
        private bool captureParameters = true;

        [SerializeField, Tooltip("전환(Transition) 상태 캡처 활성화 여부")]
        private bool captureTransitions = true;

        [SerializeField, Tooltip("역재생 시 Animator 업데이트 강제 적용")]
        private bool forceUpdateOnReverse = true;

        #endregion

        #region Private Fields

        private Animator cachedAnimator;
        private TransformCapture transformCapture;
        private CompressedAnimatorState lastState;
        private bool hasLastState;

        // 파라미터 캐싱 (GC 할당 최소화)
        private AnimatorControllerParameter[] cachedParameters;
        private List<byte> parameterBuffer;

        #endregion

        #region Public Properties

        /// <inheritdoc/>
        public int ObjectId => transformCapture?.ObjectId ?? -1;

        /// <inheritdoc/>
        public bool HasAnimator => cachedAnimator != null;

        /// <summary>캐시된 Animator (Job System용)</summary>
        public Animator CachedAnimator => cachedAnimator;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            cachedAnimator = GetComponent<Animator>();
            transformCapture = GetComponent<TransformCapture>();

            if (cachedAnimator != null && captureParameters)
            {
                cachedParameters = cachedAnimator.parameters;
                parameterBuffer = new List<byte>(64);
            }
        }

        #endregion

        #region Public Methods

        /// <inheritdoc/>
        public CompressedAnimatorState CaptureState()
        {
            if (cachedAnimator == null)
            {
                return default;
            }

            var state = new CompressedAnimatorState
            {
                LayerCount = (byte)Mathf.Min(cachedAnimator.layerCount, MAX_LAYERS)
            };

            // 레이어 상태 캡처
            if (state.LayerCount > 0) state.Layer0 = CaptureLayerState(0);
            if (state.LayerCount > 1) state.Layer1 = CaptureLayerState(1);
            if (state.LayerCount > 2) state.Layer2 = CaptureLayerState(2);
            if (state.LayerCount > 3) state.Layer3 = CaptureLayerState(3);

            // 파라미터 캡처
            if (captureParameters && cachedParameters != null && cachedParameters.Length > 0)
            {
                state.ParameterData = CaptureParameters();
            }

            return state;
        }

        /// <inheritdoc/>
        public CompressedAnimatorDelta? CaptureDelta()
        {
            if (cachedAnimator == null)
            {
                return null;
            }

            var current = CaptureState();

            if (!hasLastState)
            {
                hasLastState = true;
                lastState = current;

                return new CompressedAnimatorDelta
                {
                    ObjectId = ObjectId,
                    State = current
                };
            }

            // 변경 감지
            if (!HasStateChanged(lastState, current))
            {
                return null;
            }

            lastState = current;

            return new CompressedAnimatorDelta
            {
                ObjectId = ObjectId,
                State = current
            };
        }

        /// <inheritdoc/>
        public void ApplyState(CompressedAnimatorState state)
        {
            if (cachedAnimator == null)
            {
                return;
            }

            // 레이어 상태 적용
            if (state.LayerCount > 0) ApplyLayerState(0, state.Layer0);
            if (state.LayerCount > 1) ApplyLayerState(1, state.Layer1);
            if (state.LayerCount > 2) ApplyLayerState(2, state.Layer2);
            if (state.LayerCount > 3) ApplyLayerState(3, state.Layer3);

            // 파라미터 적용
            if (captureParameters && state.ParameterData != null && state.ParameterData.Length > 0)
            {
                ApplyParameters(state.ParameterData);
            }

            lastState = state;
            hasLastState = true;
        }

        /// <inheritdoc/>
        public void ApplyInterpolatedState(CompressedAnimatorState from, CompressedAnimatorState to, float t)
        {
            if (cachedAnimator == null)
            {
                return;
            }

            // Animator는 상태 해시 보간이 불가능하므로, 목표 상태의 정규화 시간만 보간
            byte layerCount = Math.Max(from.LayerCount, to.LayerCount);

            if (layerCount > 0) ApplyInterpolatedLayerState(0, from.Layer0, to.Layer0, t);
            if (layerCount > 1) ApplyInterpolatedLayerState(1, from.Layer1, to.Layer1, t);
            if (layerCount > 2) ApplyInterpolatedLayerState(2, from.Layer2, to.Layer2, t);
            if (layerCount > 3) ApplyInterpolatedLayerState(3, from.Layer3, to.Layer3, t);

            // 파라미터는 보간하지 않음 (to 상태 사용)
            if (captureParameters && to.ParameterData != null && to.ParameterData.Length > 0)
            {
                ApplyParameters(to.ParameterData);
            }
        }

        /// <inheritdoc/>
        public void ResetState()
        {
            hasLastState = false;
        }

        /// <summary>역재생을 위한 상태 복원 (강제 업데이트 포함)</summary>
        public void ApplyStateForReverse(CompressedAnimatorState state)
        {
            if (cachedAnimator == null)
            {
                return;
            }

            // 파라미터 먼저 적용 (상태 전환에 영향 줄 수 있음)
            if (captureParameters && state.ParameterData != null && state.ParameterData.Length > 0)
            {
                ApplyParameters(state.ParameterData);
            }

            // 레이어 상태 적용
            if (state.LayerCount > 0) ApplyLayerStateForReverse(0, state.Layer0);
            if (state.LayerCount > 1) ApplyLayerStateForReverse(1, state.Layer1);
            if (state.LayerCount > 2) ApplyLayerStateForReverse(2, state.Layer2);
            if (state.LayerCount > 3) ApplyLayerStateForReverse(3, state.Layer3);

            // 강제 업데이트로 상태 즉시 반영
            if (forceUpdateOnReverse)
            {
                cachedAnimator.Update(0f);
            }

            lastState = state;
            hasLastState = true;
        }

        /// <summary>역재생용 레이어 상태 적용 (CrossFade 사용)</summary>
        private void ApplyLayerStateForReverse(int layerIndex, CompressedAnimatorLayerState layerState)
        {
            if (layerIndex >= cachedAnimator.layerCount)
            {
                return;
            }

            // 레이어 가중치 적용
            if (layerIndex > 0)
            {
                cachedAnimator.SetLayerWeight(layerIndex, DequantizeWeight(layerState.Weight));
            }

            float normalizedTime = DequantizeNormalizedTime(layerState.NormalizedTime);

            // 전환 중이었다면 CrossFade로 부드럽게 처리
            if (layerState.IsInTransition && layerState.NextStateHash != 0)
            {
                // 현재 상태로 먼저 이동
                cachedAnimator.Play(layerState.StateHash, layerIndex, normalizedTime);

                // 다음 상태로 CrossFade (전환 진행도에 맞춰)
                float transitionTime = DequantizeNormalizedTime(layerState.TransitionNormalizedTime);
                cachedAnimator.CrossFade(layerState.NextStateHash, 0.1f, layerIndex, transitionTime);
            }
            else
            {
                // 단순 상태 적용
                cachedAnimator.Play(layerState.StateHash, layerIndex, normalizedTime);
            }
        }

        #endregion

        #region Private Methods - Capture

        private CompressedAnimatorLayerState CaptureLayerState(int layerIndex)
        {
            var stateInfo = cachedAnimator.GetCurrentAnimatorStateInfo(layerIndex);

            var layerState = new CompressedAnimatorLayerState
            {
                StateHash = stateInfo.fullPathHash,
                NormalizedTime = QuantizeNormalizedTime(stateInfo.normalizedTime),
                Weight = QuantizeWeight(cachedAnimator.GetLayerWeight(layerIndex))
            };

            if (captureTransitions && cachedAnimator.IsInTransition(layerIndex))
            {
                var nextInfo = cachedAnimator.GetNextAnimatorStateInfo(layerIndex);
                var transitionInfo = cachedAnimator.GetAnimatorTransitionInfo(layerIndex);

                layerState.IsInTransition = true;
                layerState.NextStateHash = nextInfo.fullPathHash;
                layerState.TransitionNormalizedTime = QuantizeNormalizedTime(transitionInfo.normalizedTime);
            }

            return layerState;
        }

        private byte[] CaptureParameters()
        {
            parameterBuffer.Clear();

            foreach (var param in cachedParameters)
            {
                switch (param.type)
                {
                    case AnimatorControllerParameterType.Float:
                        WriteFloat(parameterBuffer, cachedAnimator.GetFloat(param.nameHash));
                        break;

                    case AnimatorControllerParameterType.Int:
                        WriteInt(parameterBuffer, cachedAnimator.GetInteger(param.nameHash));
                        break;

                    case AnimatorControllerParameterType.Bool:
                        parameterBuffer.Add(cachedAnimator.GetBool(param.nameHash) ? (byte)1 : (byte)0);
                        break;

                    case AnimatorControllerParameterType.Trigger:
                        // Trigger는 일시적이므로 저장하지 않음
                        break;
                }
            }

            return parameterBuffer.ToArray();
        }

        #endregion

        #region Private Methods - Apply

        private void ApplyLayerState(int layerIndex, CompressedAnimatorLayerState layerState)
        {
            if (layerIndex >= cachedAnimator.layerCount)
            {
                return;
            }

            // 레이어 가중치 적용
            if (layerIndex > 0)
            {
                cachedAnimator.SetLayerWeight(layerIndex, DequantizeWeight(layerState.Weight));
            }

            // 상태 적용 (Play로 강제 전환)
            float normalizedTime = DequantizeNormalizedTime(layerState.NormalizedTime);
            cachedAnimator.Play(layerState.StateHash, layerIndex, normalizedTime);
        }

        private void ApplyInterpolatedLayerState(int layerIndex, CompressedAnimatorLayerState from, CompressedAnimatorLayerState to, float t)
        {
            if (layerIndex >= cachedAnimator.layerCount)
            {
                return;
            }

            // 같은 상태면 정규화 시간만 보간
            if (from.StateHash == to.StateHash)
            {
                float fromTime = DequantizeNormalizedTime(from.NormalizedTime);
                float toTime = DequantizeNormalizedTime(to.NormalizedTime);
                float interpolatedTime = Mathf.Lerp(fromTime, toTime, t);

                cachedAnimator.Play(to.StateHash, layerIndex, interpolatedTime);
            }
            else
            {
                // 다른 상태면 목표 상태로 전환
                float normalizedTime = DequantizeNormalizedTime(to.NormalizedTime);
                cachedAnimator.Play(to.StateHash, layerIndex, normalizedTime);
            }

            // 레이어 가중치 보간
            if (layerIndex > 0)
            {
                float fromWeight = DequantizeWeight(from.Weight);
                float toWeight = DequantizeWeight(to.Weight);
                cachedAnimator.SetLayerWeight(layerIndex, Mathf.Lerp(fromWeight, toWeight, t));
            }
        }

        private void ApplyParameters(byte[] data)
        {
            if (cachedParameters == null || data == null)
            {
                return;
            }

            int offset = 0;

            foreach (var param in cachedParameters)
            {
                if (offset >= data.Length)
                {
                    break;
                }

                switch (param.type)
                {
                    case AnimatorControllerParameterType.Float:
                        if (offset + 4 <= data.Length)
                        {
                            cachedAnimator.SetFloat(param.nameHash, ReadFloat(data, ref offset));
                        }
                        break;

                    case AnimatorControllerParameterType.Int:
                        if (offset + 4 <= data.Length)
                        {
                            cachedAnimator.SetInteger(param.nameHash, ReadInt(data, ref offset));
                        }
                        break;

                    case AnimatorControllerParameterType.Bool:
                        if (offset < data.Length)
                        {
                            cachedAnimator.SetBool(param.nameHash, data[offset++] != 0);
                        }
                        break;

                    case AnimatorControllerParameterType.Trigger:
                        // Trigger는 적용하지 않음
                        break;
                }
            }
        }

        #endregion

        #region Private Methods - Comparison

        private bool HasStateChanged(CompressedAnimatorState a, CompressedAnimatorState b)
        {
            if (a.LayerCount != b.LayerCount)
            {
                return true;
            }

            if (a.LayerCount > 0 && HasLayerChanged(a.Layer0, b.Layer0)) return true;
            if (a.LayerCount > 1 && HasLayerChanged(a.Layer1, b.Layer1)) return true;
            if (a.LayerCount > 2 && HasLayerChanged(a.Layer2, b.Layer2)) return true;
            if (a.LayerCount > 3 && HasLayerChanged(a.Layer3, b.Layer3)) return true;

            // 파라미터 변경 체크
            if (!ArrayEquals(a.ParameterData, b.ParameterData))
            {
                return true;
            }

            return false;
        }

        private bool HasLayerChanged(CompressedAnimatorLayerState a, CompressedAnimatorLayerState b)
        {
            // 상태 해시 변경
            if (a.StateHash != b.StateHash)
            {
                return true;
            }

            // 정규화 시간 변경 (임계값 이상)
            int timeDiff = Math.Abs(a.NormalizedTime - b.NormalizedTime);
            if (timeDiff > (int)(NORMALIZED_TIME_THRESHOLD * NORMALIZED_TIME_MAX))
            {
                return true;
            }

            // 전환 상태 변경
            if (a.IsInTransition != b.IsInTransition)
            {
                return true;
            }

            // 가중치 변경
            if (a.Weight != b.Weight)
            {
                return true;
            }

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

        #endregion

        #region Private Methods - Quantization

        private static ushort QuantizeNormalizedTime(float normalizedTime)
        {
            // 정규화 시간은 0-1 범위지만, 루프 애니메이션은 1 이상일 수 있음
            // fractional part만 저장 (정수 부분은 무시)
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

        #endregion

        #region Private Methods - Binary IO

        private static void WriteFloat(List<byte> buffer, float value)
        {
            var bytes = BitConverter.GetBytes(value);
            buffer.AddRange(bytes);
        }

        private static void WriteInt(List<byte> buffer, int value)
        {
            var bytes = BitConverter.GetBytes(value);
            buffer.AddRange(bytes);
        }

        private static float ReadFloat(byte[] data, ref int offset)
        {
            float value = BitConverter.ToSingle(data, offset);
            offset += 4;
            return value;
        }

        private static int ReadInt(byte[] data, ref int offset)
        {
            int value = BitConverter.ToInt32(data, offset);
            offset += 4;
            return value;
        }

        #endregion
    }
}
