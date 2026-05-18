using UnityEngine;


namespace TRAINEE
{
    public class EquipActionState : StateBase
    {
        private EActionPhase _currentPhase = EActionPhase.None;
        private EAnimParam _currentAnimParam = EAnimParam.None;
        private bool _isHoldAction = false;

        private EquipAnimEventBase _currentAnimEvent = null;
        private Transform _actionTarget = null;
        public EquipActionState(ControllerBase controller, EStateType type) : base(controller,type) { }

        protected override void Init()
        {
            base.RegisterCommand<MoveCommand>(OnMove);
            base.RegisterCommand<LookCommand>(OnLook);
            base.RegisterCommand<EquipActionCommand>(OnAction);
        }

        public void SetTarget(Transform target)
        {
            _actionTarget = target;
        }
        public override void Enter()
        {
            Debug.Log("EquipActionState Enter");

            _currentAnimEvent = _controller.EquipHandler.CurrentAnimEvent;

            if (_currentAnimEvent == null)
            {
                _controller.ChangeState(EStateType.Idle);
                return;
            }

            _currentAnimEvent.Init(_controller);

            _currentPhase = EActionPhase.Start;
            _currentAnimParam = EAnimParam.Start;
            _isHoldAction = _currentAnimEvent.IsHoldAction;

            if (_actionTarget != null)
            {
                // 2. 캐릭터의 발(방향)을 타겟을 향해 자연스럽게 돌려줍니다. (Y축 회전만)
                Vector3 lookPos = new Vector3(_actionTarget.position.x, _controller.transform.position.y, _actionTarget.position.z);
                _controller.transform.LookAt(lookPos);

                // 3. 장비 데이터 조회
                string equipId = _controller.GetCurrentEquipId;
                var equipData = ItemManager.Instance.GetItemData(equipId);

                // 4. 데이터에 IK 사용 여부가 체크되어 있다면, 정해진 가중치만큼 허리를 꺾습니다.
                if (equipData.HasValue && equipData.Value._useSpineIk)
                {
                    float ikWeight = equipData.Value._ikWeight; // 예: 소화기 1.0, 망치 0.5
                    _controller.IKHandler.SetSpineAimTarget(_actionTarget, ikWeight);
                }
            }

            _controller.AnimationHandler.SetTrriger(_currentAnimParam, isEvent: true, time: 0.9f, callBack: OnStartFinished);
        }
        public override void OnUpdate(float deltaTime)
        {
            float speed = _controller.Movement.CurrentSpeed;
            _controller.AnimationHandler.SetFloat(EAnimParam.Move, speed);
        }
        public override void OnFixedUpdate(float deltaTime)
        {
        }

        public override void Exit()
        {
            _currentPhase = EActionPhase.None;
            _isHoldAction = false;

            _currentAnimEvent = null;

            Animator animator = _controller.GetAnimator;
            if (animator != null)
            {
                animator.ResetTrigger("Start");
                animator.ResetTrigger("Ing");
                animator.ResetTrigger("End");

                int layerIndex = _controller.CurrentEquipLayerIndex;
                animator.CrossFade("Move", 0.1f, layerIndex);
            }

            _controller.IKHandler.ClearSpineAimTarget();
            _actionTarget = null;
        }

        private void OnStartFinished()
        {
            if (_currentPhase != EActionPhase.None && _controller.StateMachine._currentState != this)
            {
                return;
            }

            _currentPhase = EActionPhase.Ing;
            _currentAnimParam = EAnimParam.Ing;

            if (_isHoldAction)
            {
                _controller.AnimationHandler.SetTrriger(_currentAnimParam);
            }
            else
            {
                _controller.AnimationHandler.SetTrriger(_currentAnimParam, isEvent: true, time: 0.9f, callBack: OnIngFinished);
            }

        }

        private void OnIngFinished()
        {
            if (_currentPhase != EActionPhase.Start && _controller.StateMachine._currentState != this)
            {
                return;
            }

            OnFinishAction();
        }

        private void OnEndFinished()
        {
            if (_controller.StateMachine._currentState != this)
            {
                return;
            }
           
            Debug.Log("EquipActionState OnEndFinished");

            _currentPhase = EActionPhase.None;
            _controller.ChangeState(EStateType.Idle);
        }

        public void OnFinishAction()
        {
            if (_controller.StateMachine._currentState != this || _currentPhase == EActionPhase.End)
                return;

            Debug.Log("[EquipActionState] FinishAction: 장비 액션 완료 -> End 애니메이션 진입");

            _currentPhase = EActionPhase.End;
            _currentAnimParam = EAnimParam.End;

            _controller.AnimationHandler.SetTrriger(_currentAnimParam, isEvent: true, time: 0.3f, callBack: OnEndFinished);
        }


        private void OnLook(LookCommand cmd)
        {
            cmd.Execute();
        }

        private void OnMove(MoveCommand cmd)
        {
            cmd.Execute();
        }

        private void OnAction(EquipActionCommand cmd)
        {
            Debug.Log($"EquipActionState OnAction : {_isHoldAction} / {_currentPhase}");

            _isHoldAction = false;

            if (_currentPhase == EActionPhase.Ing || _currentPhase == EActionPhase.Start)
            {
                OnFinishAction();
            }
        }
    }
}
