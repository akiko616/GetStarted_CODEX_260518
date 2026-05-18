using UnityEngine;

namespace TRAINEE
{
    public class FollowState : StateBase
    {
        private RescueeController _rescuee;
        private RescueeMovement _movement;

        private float _stopDistance = 2.0f; // 이 거리보다 가까우면 멈춤
        private float _runDistance = 5.0f;  // 이 거리보다 멀면 뜀 (옵션)

        private float _speed = 0f;
        public FollowState(ControllerBase controller, EStateType type) : base(controller, type) 
        {
            _rescuee = controller as RescueeController;
            _movement = _rescuee.Movement as RescueeMovement;
        }
        public override void Enter()
        {
            _stopDistance = _movement.StopDistance;
            _speed = _movement.Speed;
        }

        public override void OnUpdate(float deltaTime)
        {
            if (_rescuee.TargetPlayer == null || _movement == null)
            {
                // 타겟이 없어지면 강제로 Idle로 되돌아갑니다.
                _rescuee.ChangeState(EStateType.Idle);
                return;
            }

            // 타겟과의 거리 계산
            Vector3 targetPos = _rescuee.TargetPlayer.position;
            float distance = Vector3.Distance(_rescuee.transform.position, targetPos);
            float speed = 0f;
            if (distance > _stopDistance)
            {
                speed = _speed;
                // 💡 멀리 있으면 타겟을 향해 이동! (RescueeMovement의 기능 호출)
                _movement.MoveToTarget(_rescuee.TargetPlayer);

                // 거리에 따라 걷기/뛰기 애니메이션 파라미터 조절 가능
                _rescuee.AnimationHandler.SetFloat(EAnimParam.Move, speed);
            }
            else
            {
                // 💡 가까워지면 멈춤!
                _movement.StopMove();
                _rescuee.AnimationHandler.SetFloat(EAnimParam.Move, speed);
            }
        }

        public override void OnFixedUpdate(float deltaTime)
        {
        }
        public override void Exit()
        {
            _movement?.StopMove();
        }

    }
}
