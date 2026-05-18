using System;
using System.Collections.Generic;
using UnityEngine;

namespace TRAINEE
{
    public class AnimationHandler : IHandler
    {
        public struct AniData
        {
            public int _hash;
            public int _layerIndex;

            public AniData(int hash, int layerIndex)
            {
                _hash = hash;
                _layerIndex = layerIndex;
            }
        }
        private struct PendingIKRequest
        {
            public int LayerIndex;
            public float TargetWeight;
            public bool IsActive;
        }



        private Animator _animator = null;
        private Animator _equipAnimator = null;

        private Dictionary<EAnimParam, AniData> _paramHashs = new Dictionary<EAnimParam, AniData>();
        private List<AnimCallbackData> _callbacks = new List<AnimCallbackData>();
        private Dictionary<int, int> _layerStateHashs = new Dictionary<int, int>();

        private bool _isIkBlending = false;
        private bool _isLayerBlending = false;
        private float _ikCurrentTime = 0f;
        private float _ikDuration = 0f;
        private float _ikStartWeight = 0f;
        private float _ikTargetWeight = 0f;

        private IIKControl _ikControl = null;
        private PendingIKRequest _pendingRequest;

        public AnimationHandler()
        {
        }

        public void Init(Animator animator)
        {
            _animator = animator;
            _paramHashs.Clear();
        }
        public void Init(Animator animator, IIKControl ikControl)
        {
            _animator = animator;
            _ikControl = ikControl;
            _paramHashs.Clear();
        }

        public void RegisterHash(EAnimParam aniType, string hash, EAnimLayer layer)
        {
            if (aniType == EAnimParam.None) return;

            int _hash = Animator.StringToHash(hash);

            // Enum을 int로 변환하여 레이어 인덱스로 저장
            int layerIndex = (int)layer;

            // 맵에 등록 (중복 체크 권장)
            if (!_paramHashs.ContainsKey(aniType))
            {
                _paramHashs.Add(aniType, new AniData(_hash, layerIndex));
            }
        }

        public void OnUpdate(float deltaTime)
        {
            if(_animator ==null)
            {
                return;
            }

            if (_pendingRequest.IsActive)
            {
                ProcessAutoIKRequest();
            }


            if (_isIkBlending)
            {
                ProcessIKBlending(deltaTime);
            }

            if (_callbacks.Count == 0)
            {
                return;
            }

            for (int i = _callbacks.Count - 1; i >= 0; i--)
            {
                var data = _callbacks[i];
                int layer = data._layerIndex;

                if (layer >= _animator.layerCount) continue;

                AnimatorStateInfo stateInfo = _animator.GetCurrentAnimatorStateInfo(layer);
                bool isTransitioning = _animator.IsInTransition(layer);

                if (data._lockedStateHash == null)
                {
                    if (isTransitioning)
                    {
                        
                        data._lockedStateHash = _animator.GetNextAnimatorStateInfo(layer).shortNameHash;
                        continue; 
                    }
                    else if (stateInfo.shortNameHash != data._initialStateHash)
                    {
                     
                        data._lockedStateHash = stateInfo.shortNameHash;
                    }
                    else
                    {
                        
                        continue;
                    }
                }

                int currentActiveHash = isTransitioning
                    ? _animator.GetNextAnimatorStateInfo(layer).shortNameHash
                    : stateInfo.shortNameHash;


                if (currentActiveHash != data._lockedStateHash)
                {
                    _callbacks.RemoveAt(i);
                    continue;
                }


                float currentTime = stateInfo.normalizedTime % 1.0f;


                if (!data._isFired && !isTransitioning && currentTime >= data._targetTime)
                {
                    data._callback?.Invoke();
                    data._isFired = true;
                }
            }

            _callbacks.RemoveAll(x => x._isFired);
        }
        public void OnFixedUpdate(float deltaTime)
        {
        }

        private void SetFloat(EAnimParam param,float value)
        {
            if(_paramHashs.TryGetValue(param, out AniData data))
                _animator.SetFloat(data._hash, value, 0.01f, Time.deltaTime);
        }

        public void SetFloat(EAnimParam param, float value ,bool isEvent =false, float time = 0f, Action callBack = null)
        {
            SetFloat(param,value);

            if(isEvent)
            {
                int layerIndex = _paramHashs.ContainsKey(param) ? _paramHashs[param]._layerIndex : 0;

                RegisterEvent(time,layerIndex,callBack);
            }
        }

        private void SetBool(EAnimParam param, bool value)
        {
            if (_paramHashs.TryGetValue(param, out AniData data))
                _animator.SetBool(data._hash, value);
        }

        public void SetBool(EAnimParam param, bool value, bool isEvent = false, float time = 0f, Action callBack = null)
        {
            SetBool(param, value);

            if (isEvent)
            {
                int layerIndex = _paramHashs.ContainsKey(param) ? _paramHashs[param]._layerIndex : 0;
                RegisterEvent(time, layerIndex, callBack);
            }
        }

        private void SetTrriger(EAnimParam param)
        {
            if (_paramHashs.TryGetValue(param, out AniData data))
                _animator.SetTrigger(data._hash);
        }

        public void SetTrriger(EAnimParam param,bool isEvent = false, float time = 0f, Action callBack = null)
        {
            SetTrriger(param);

            if(isEvent)
            {
                int layerIndex = _paramHashs.ContainsKey(param) ? _paramHashs[param]._layerIndex : 0;
                Debug.Log($"Select LayerIndex : {layerIndex}");
                RegisterEvent(time, layerIndex, callBack);
            }
        }

        //public void SetTrriger(EAnimParam param, bool useAutoIK = false, float targetIKWeight = 1f)
        //{
        //    // 1. 트리거 발동 (기존 로직)
        //    SetTrriger(param);

        //    // 2. 오토 IK를 쓴다면?
        //    if (useAutoIK)
        //    {
        //        // A. 공격 시작하니까 즉시 IK 끄기 (손 꼬임 방지)
        //        SetIKWeightImmediate(0f);

        //        // B. "다음 프레임에 애니메이션 길이 재서 블렌딩 해줘" 라고 예약 걸기
        //        int layerIndex = _paramHashs.ContainsKey(param) ? _paramHashs[param]._layerIndex : 0;

        //        _pendingRequest = new PendingIKRequest()
        //        {
        //            LayerIndex = layerIndex,
        //            TargetWeight = targetIKWeight,
        //            IsActive = true
        //        };
        //    }
        //}

        private void ProcessIKBlending(float deltaTime)
        {

            _ikCurrentTime += deltaTime;

            float ratio = _ikCurrentTime / _ikDuration;
            ratio = Mathf.Clamp01(ratio); 


            float nextWeight = Mathf.Lerp(_ikStartWeight, _ikTargetWeight, ratio);


            _ikControl?.SetIKWeight(nextWeight);

            if(_isLayerBlending)
                _ikControl?.SetLayerWeight(nextWeight);


            // 종료 체크
            if (_ikCurrentTime >= _ikDuration)
            {

                _isIkBlending = false;
                _ikControl?.SetIKWeight(_ikTargetWeight); 

                if (_isLayerBlending)
                    _ikControl?.SetLayerWeight(_ikTargetWeight);
            }
        }

        // B. 자동 IK 요청 감지 (애니메이션 길이 측정)
        private void ProcessAutoIKRequest()
        {
            // 현재 레이어의 상태 정보
            int layer = _pendingRequest.LayerIndex;
            AnimatorStateInfo stateInfo = _animator.GetCurrentAnimatorStateInfo(layer);
            AnimatorStateInfo nextInfo = _animator.GetNextAnimatorStateInfo(layer);

            // Case 1: 트랜지션 중일 때 (다음 애니메이션이 미리 감지됨)
            if (_animator.IsInTransition(layer))
            {
                float clipLength = nextInfo.length;
                if (clipLength > 0)
                {
                    // 다음 애니메이션 길이만큼 블렌딩 시작
                    StartIKBlend(0f, _pendingRequest.TargetWeight, clipLength);
                    _pendingRequest.IsActive = false; // 요청 완료
                }
            }
            // Case 2: 트랜지션 없이 바로 시작됐을 때 (초반부)
            else if (stateInfo.normalizedTime < 0.1f)
            {
                float clipLength = stateInfo.length;
                if (clipLength > 0)
                {
                    StartIKBlend(0f, _pendingRequest.TargetWeight, clipLength);
                    _pendingRequest.IsActive = false;
                }
            }
        }

        // C. 블렌딩 시작 함수
        public void StartIKBlend(float startWeight, float targetWeight, float duration, bool isLayerWeight = false)
        {
            Debug.Log($"StartIKBlend : {targetWeight} / {duration}");
            _ikStartWeight = startWeight;
            _ikTargetWeight = targetWeight;
            _ikDuration = duration;
            _ikCurrentTime = 0f;
            _isIkBlending = true;
            _isLayerBlending = isLayerWeight;
            // 시작 값 즉시 적용
            _ikControl?.SetIKWeight(startWeight);
        }

        public void SetIKWeightImmediate(float weight)
        {
            _isIkBlending = false;
            _ikControl?.SetIKWeight(0f);
        }

        private void RegisterEvent(float time, int layerIndex, Action callback)
        {
            int initialHash = 0;
            if (_animator != null)
            {
                AnimatorStateInfo stateInfo = _animator.GetCurrentAnimatorStateInfo(layerIndex);
                initialHash = stateInfo.shortNameHash;
                _layerStateHashs[layerIndex] = stateInfo.shortNameHash;
            }

            _callbacks.Add(new AnimCallbackData(time,layerIndex,callback, initialHash));
        }

        public void SetEquipAnimator(Animator equipAnim)
        {
            _equipAnimator = equipAnim;
        }

        public void TriggerEquipAnimation(string triggerName)
        {
            if (_equipAnimator != null)
            {
                _equipAnimator.SetTrigger(triggerName);
            }
        }
    }
}
