using System;
using System.Collections.Generic;


namespace TRAINEE
{
    public abstract class StateBase
    {
        protected ControllerBase _controller = null;
        protected EStateType _stateType = EStateType.None;
        protected Dictionary<Type, CommandHandler> _commands = new Dictionary<Type, CommandHandler>();

        public EStateType StateType { get { return _stateType; } }
        protected StateBase(ControllerBase controller, EStateType type) 
        {
            _controller = controller;
            _stateType = type;
            Init();

            RegisterCommand<MLCCommand>(OnInteract);
        }

        protected virtual void Init() { }

        public abstract void Enter();
        public abstract void OnUpdate(float deltaTime);
        public abstract void OnFixedUpdate(float deltaTime);
        public abstract void Exit();

        protected void RegisterCommand<T>(Action<T> handler) where T : CommandBase
        {
            CommandHandler wrapper = (commandBase) =>
            {
                T cmd = commandBase as T;

                if (cmd != null)
                {
                    handler(cmd);
                }
            };

            _commands[typeof(T)] = wrapper;
        }

        public virtual void HandleCommand(CommandBase command)
        {
            if (command == null) return;

            Type type = command.GetType();

            if (_commands.TryGetValue(type, out CommandHandler handler))
            {
                handler(command);
            }
        }

        // 여기에 들어가는 메소드 아님
        // StateBase -> PlayerState,NPCState
        // PlayerState -> PlayerIdleState,PlayerMoveState

        protected virtual void OnInteract(MLCCommand command)
        {
            if (_controller.InteractionHandler != null)
            {
                _controller.InteractionHandler.HandleClickInteraction();
            }
        }
    }
}
