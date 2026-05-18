using UnityEngine;


namespace TRAINEE
{
    public abstract class EquipAnimEventBase : MonoBehaviour
    {
        protected ControllerBase _controller;
        protected ItemPrefabMapping? _equipData;

        public bool IsHoldAction { get => _equipData.HasValue &&  _equipData.Value._isHoldAction ; }

        public float HitDelayTime { get => _equipData.HasValue ? _equipData.Value._hitDelayTime : 0.5f; }


        public void Init(ControllerBase controller)
        {
            _controller = controller;


            string currentEquipId = _controller.GetCurrentEquipId;
            _equipData = ItemManager.Instance.GetItemData(currentEquipId);

            if (_equipData == null)
            {
                Debug.LogError($"[{gameObject.name}] ItemManager에서 장비 데이터를 찾을 수 없습니다! ID: {currentEquipId}");
                return;
            }

            if (!_equipData.Value._hasOwnAnim)
            {
                Debug.Log($"{currentEquipId} 장비 애니메이션 이벤트 등록");
                OnRegisterAnimEvent();
            }
        }

        public virtual void OnHit()
        {
            Debug.Log($"Equip 애니메이션 Hit : {_controller}");
            //if(_controller != null)
            //{
            //    _controller.OnAnimationHitEvent();
            //}

            OnHitEffect();
        }

        public virtual void OnHitEnd()
        {
            Debug.Log($"Equip 애니메이션 HitEnd : {_controller}");
            //if (_controller != null)
            //{
            //    _controller.OnAnimationHitEndEvent();
            //}
        }

        private void OnRegisterAnimEvent()
        {
            if (_controller == null)
            {
                Debug.Log("컨트롤러가 없습니다.");
                return;
            }

            if (_controller.GetAnimator == null)
            {
                Debug.Log("애니메이터가 없습니다.");
                return;
            }

            Animator animator = _controller.GetAnimator;

            RuntimeAnimatorController runtimeAnimator = animator.runtimeAnimatorController;

            if (runtimeAnimator == null)
            {
                Debug.Log("애니메이터가 없습니다.");
                return;
            }

            
            string targetFuncName = _equipData.Value._eventFunctionName;

            for (int i = 0; i < runtimeAnimator.animationClips.Length; i++)
            {
                AnimationClip clip = runtimeAnimator.animationClips[i];

                if (clip != null)
                {
                    for (int j = 0; j < _equipData.Value._eventAction.Length; j++)
                    {
                        string targetPhaseKeyword = _equipData.Value._eventAction[j].ToString();
                        Debug.Log($"keyWord : {targetPhaseKeyword} / Cilp.Name : {clip.name}");
                        if (clip.name.Contains(targetPhaseKeyword))
                        {
                            if (clip.events.Length > 0)
                            {
                                bool alreadyHasEvent = false;

                                // 1. 먼저 클립의 전체 이벤트를 쭉 훑어봅니다.
                                for (int x = 0; x < clip.events.Length; x++)
                                {
                                    AnimationEvent ev = clip.events[x];

                                    if (ev.functionName == targetFuncName)
                                    {
                                        alreadyHasEvent = true;
                                        break; // 찾았으면 루프 종료
                                    }
                                }

                                // 🌟 2. for문이 다 끝난 '밖'에서 검사하고 주입해야 안전합니다!
                                if (!alreadyHasEvent)
                                {
                                    AnimationEvent hitEvent = new AnimationEvent();
                                    hitEvent.time = HitDelayTime;
                                    hitEvent.functionName = targetFuncName;

                                    clip.AddEvent(hitEvent);
                                    Debug.Log($"[애니메이션 이벤트 자동 주입 완료] 클립: {clip.name} / 시간: {hitEvent.time}초");
                                }
                            }
                            else
                            {
                                AnimationEvent hitEvent = new AnimationEvent();
                                hitEvent.time = HitDelayTime;
                                hitEvent.functionName = targetFuncName;

                                clip.AddEvent(hitEvent);
                                Debug.Log($"[애니메이션 이벤트 자동 주입 완료] 클립: {clip.name} / 시간: {hitEvent.time}초");
                            }
                        }
                    }
                }
            }
        }

        protected abstract void OnHitEffect();
    }
}
