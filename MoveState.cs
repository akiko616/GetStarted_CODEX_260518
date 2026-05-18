using UnityEngine;


namespace TRAINEE
{
    public class MoveState : StateBase
    {
        public MoveState(ControllerBase controller, EStateType type) : base(controller, type) { }

        protected override void Init()
        {
            base.RegisterCommand<MoveCommand>(OnMove);
            base.RegisterCommand<LookCommand>(OnLook);
        }
        public override void Enter()
        {

        }
        public override void OnUpdate(float deltaTime)
        {
            float speed = _controller.Movement.Speed;
            _controller.AnimationHandler.SetFloat(EAnimParam.Move, speed);
        }
        public override void OnFixedUpdate(float deltaTime)
        {
        }

        public override void Exit()
        {
        }

        private void OnMove(MoveCommand cmd)
        {

            if (cmd.GetDirection.sqrMagnitude < 0.001f)
            {
                cmd.Execute();

                _controller.ChangeState(EStateType.Idle);
                return;
            }

            cmd.Execute();
        }

        private void OnLook(LookCommand cmd)
        {
            cmd.Execute();
        }
    }
}
