using UnityEngine;

namespace TRAINEE
{
    public interface ICommand
    {
       public void Execute();
    }

    public abstract class CommandBase : ICommand
    {
        protected MovementBase _movement = null;
        public abstract void Execute();

    }
    public class MoveCommand : CommandBase
    {
        private Vector2 _dir = Vector2.zero;

        public Vector2 GetDirection { get => _dir; }
        public MoveCommand(MovementBase movement, Vector2 dir)
        {
            _movement = movement;
            _dir = dir;
        }

        public override void Execute()
        {
            _movement?.Move(_dir);
        }
    }

    public class LookCommand : CommandBase
    {
        private Vector2 _lookDelta = Vector2.zero;

        public LookCommand(MovementBase movement, Vector2 lookDelta)
        {
            _movement = movement; 
            _lookDelta = lookDelta;
        }

        public override void Execute()
        {
            _movement?.Look(_lookDelta);
        }

    }

    /// <summary>
    /// MLC : Mouse Left Click
    /// </summary>
    public class MLCCommand : CommandBase
    {
        public MLCCommand()
        {

        }

        public override void Execute()
        {

        }
    }

    public class EquipActionCommand : CommandBase
    {
        public EquipActionCommand()
        {

        }
        public override void Execute()
        {
        }
    }

}
