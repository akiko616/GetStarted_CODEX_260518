using UnityEngine;


namespace TRAINEE
{
    public class ApproachState : StateBase
    {
        private Transform _target;
        private float _targetRange;
        public ApproachState(ControllerBase controller, EStateType type) : base(controller, type) { }

        protected override void Init()
        {
        }

        public void SetTarget(Transform target, float range)
        {
            _target = target;
            _targetRange = range;
        }
        public override void Enter()
        {
            Debug.Log("ApproachState Enter");
        }

        public override void OnUpdate(float deltaTime)
        {
            if (_target == null)
            {
                _controller.ChangeState(EStateType.Idle);
                return;
            }

            Vector3 playerPos = _controller.transform.position;
            Vector3 targetPos = _target.position;
            playerPos.y = 0;
            targetPos.y = 0;

            float currentDist = Vector3.Distance(playerPos, targetPos);

            if (currentDist <= _targetRange)
            {
                _controller.Movement.Move(Vector2.zero);
                _controller.AnimationHandler.SetFloat(EAnimParam.Move, 0f);

                StateBase find = _controller.FindState(EStateType.EquipAction);

                if (find != null)
                {
                    EquipActionState state = find as EquipActionState;
                    state.SetTarget(_target);
                }

                _controller.ChangeState(EStateType.EquipAction);
            }
            else
            {
                Vector3 dirToTarget = (targetPos - playerPos).normalized;

                if (dirToTarget != Vector3.zero)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dirToTarget);
                    _controller.transform.rotation = Quaternion.Slerp(_controller.transform.rotation, targetRot, deltaTime * 10f);
                }

                _controller.Movement.Move(new Vector2(0f, 1f));

                _controller.AnimationHandler.SetFloat(EAnimParam.Move, _controller.Movement.Speed);
            }
        }

        public override void OnFixedUpdate(float deltaTime)
        {
        }
        public override void Exit()
        {
            _controller.Movement.Move(Vector2.zero);
        }
    }
}
