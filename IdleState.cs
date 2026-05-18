using UnityEngine;

namespace TRAINEE
{
    public class IdleState : StateBase
    {
        public IdleState(ControllerBase controller, EStateType type) : base(controller, type) { }

        protected override void Init()
        {
            base.RegisterCommand<MoveCommand>(OnMove);
            base.RegisterCommand<LookCommand>(OnLook);
        }
        public override void Enter()
        {
            Debug.Log($"Idle State");
        }
        public override void OnUpdate(float deltaTime)
        {
            _controller.AnimationHandler.SetFloat(EAnimParam.Move, 0f);

        }
        public override void OnFixedUpdate(float deltaTime)
        {
        }

        public override void Exit()
        {
        }

        private void OnMove(MoveCommand cmd)
        {
            if (cmd.GetDirection.sqrMagnitude > 0.001f)
            {
                cmd.Execute();
                _controller.ChangeState(EStateType.Walk);
            }
        }

        private void OnLook(LookCommand cmd)
        {
            cmd.Execute();
        }
    }
}
