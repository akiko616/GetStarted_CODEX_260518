using UnityEngine;


namespace TRAINEE
{
    public abstract class DoorViewBase : MapViewBase, IInteractableView
    {
        [SerializeField] protected Animator _animator = null;
        [SerializeField] protected Transform _anchor = null;
        protected AnimationHandler _animationHandler = null;
        protected DoorEventData _data = null;

        public GameModelEventBase Param
        {
            get
            {
                if (_data != null)
                {
                    if (!_data._isOpen)
                    {
                        _data._id = _id;
                        return _data;
                    }
                }

                return null;
            }
        }

        public Vector3 Anchor
        {
            get
            {
                if(_anchor != null)
                {
                    return _anchor.position;
                }

                return (this.transform.position);
            }
        }

        protected virtual void Init()
        {
            _animationHandler = new AnimationHandler();
            _animationHandler.Init(_animator,null);
            _animationHandler.RegisterHash(EAnimParam.open, EAnimParam.open.ToString(), EAnimLayer.Base);
            _animationHandler.RegisterHash(EAnimParam.close, EAnimParam.close.ToString(), EAnimLayer.Base);
        }

        public override void OnUpdate(float deltaTime)
        {
            base.OnUpdate(deltaTime);

            if(_animationHandler != null)
            {
                _animationHandler.OnUpdate(deltaTime);
            }
        }
    }
}
