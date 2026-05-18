using UnityEngine;
using UnityEngine.Rendering;


namespace TRAINEE
{
    public abstract class EnvironBase
    {
        public abstract void Init();
        public abstract void OnUpdate(float deltaTime);

        public abstract void OnFixedUpdate(float deltaTime);
        public abstract void OnLateUpdate(float deltaTime);
        public virtual void SetupWeather() { }

        public abstract void Clear();

        public abstract EnvironViewBase EnvironViewBase { get; }

    }

    public abstract class EnvironModule<T> : EnvironBase where T : EnvironViewBase
    {
        protected T _view;

        public override EnvironViewBase EnvironViewBase => _view;

        public EnvironModule(T view, Volume volume)
        {
            _view = view;
            _view.Volume = volume;
        }

        public override void Clear()
        {
            if(_view != null)
            {
                Object.Destroy(_view);
                _view = null;
            }
        }
    }
}
