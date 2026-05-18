using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace TRAINEE
{
    public class EnvironCloud : EnvironModule<EnvironViewBase>
    {
        private VolumetricClouds _cloud = null;
        private float _currentDensity = 0f;
        private float _currentTime = 0f;

        private EnvironViewCloud _cloudView = null;
        public EnvironCloud(EnvironViewBase view, Volume volume) : base(view,volume)
        {

        }

        public override void Init()
        {
            if(_view == null || _view.Volume == null)
            {
                Debug.Log($"Environ Setup Missing view : {_view} / volume : {_view.Volume} ");
                return;
            }


            _cloudView = _view as EnvironViewCloud;

            VolumeProfile profile = _view.Volume.sharedProfile;

            if (profile.TryGet<VolumetricClouds>(out _cloud))
            {
                _currentDensity = _cloud.densityMultiplier.value;
            }
            else
            {
                Debug.LogError($"Volume Not Clouds Component");
            }

            _cloud.densityMultiplier.overrideState = true;
        }

        public void SetCloudData(float time,float density)
        {
            _currentDensity = density * 0.01f;
            _currentTime = time;
            _cloudView.MaxDensityMultiplier = _currentDensity;
        }

        public override void OnUpdate(float deltaTime)
        {
            if (!GameManager.Instance.GetGameStart)
            {
                return;
            }

            if (_view == null || _cloud == null)
            {
                return;
            }

            float curveValue = _cloudView.AnimationCurve.Evaluate(_currentTime);
            float calculatedTarget = curveValue * _cloudView.MaxDensityMultiplier;

            float speed = _cloudView.LerpSpeed > 0 ? _cloudView.LerpSpeed : 1f;
            _currentDensity = Mathf.Lerp(_currentDensity, calculatedTarget, deltaTime * speed);

            // 3. 값 적용
            _cloud.densityMultiplier.value = _currentDensity;
        }

        public override void Clear()
        {
            base.Clear();
            _cloud = null;
        }

        public override void OnFixedUpdate(float deltaTime)
        {
            if (!GameManager.Instance.GetGameStart)
            {
                return;
            }
        }

        public override void OnLateUpdate(float deltaTime)
        {
            if (!GameManager.Instance.GetGameStart)
            {
                return;
            }
        }
    }
}
