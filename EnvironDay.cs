using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;


namespace TRAINEE
{
    public class EnvironDay : EnvironModule<EnvironViewBase>
    {
        private float _defaultTime = 0f;
        private float _currentTime = 0f;

        private HDAdditionalLightData _sunHD = null;
        private HDAdditionalLightData _moonHD = null;
        private PhysicallyBasedSky _skySetting = null;
        private Exposure _exposureSetting = null;
        private EnvironViewDay _dayView = null;


        private float _normalizedTime = 0f;
        private float _sunWeight = 0f;
        private float _moonWeight = 0f;

        public bool IsNight => _sunWeight <= 0.1f;
        public EnvironDay(EnvironViewBase view, Volume volume) : base(view, volume)
        {

        }

        public override void Init()
        {
            if (_view == null || _view.Volume == null)
            {
                Debug.Log($"Environ Setup Missing view : {_view} / volume : {_view.Volume} ");
                return;
            }
            _dayView = _view as EnvironViewDay;

            _defaultTime = _dayView.DefaultTime;

            _normalizedTime = 0f;
            _sunWeight = 0f;
            _moonWeight = 0f;

            if (_dayView.SunLight != null)
            {
                _sunHD = _dayView.SunLight.GetComponent<HDAdditionalLightData>();
            }

            if (_dayView.MoonLight != null)
            {
                _moonHD = _dayView.MoonLight.GetComponent<HDAdditionalLightData>();
            }


            if (_view.Volume != null && _view.Volume.sharedProfile != null)
            {
                _view.Volume.profile.TryGet(out _skySetting);
                _view.Volume.profile.TryGet(out _exposureSetting);
            }
        }

        public void SetTimeData(float time)
        {
            _currentTime = time;
            _normalizedTime = _currentTime / _defaultTime;

            _sunWeight = SunWeight(_currentTime);
            _moonWeight = 1f - _sunWeight;

            SetupLight();
            UpdateDay();
        }

        public override void OnUpdate(float deltaTime)
        {
            if (!GameManager.Instance.GetGameStart)
            {
                return;
            }

            if (_view == null)
            {
                return;
            }

            UpdateSky(_normalizedTime, deltaTime);
        }

        private void UpdateDay(float deltaTime = 0f)
        {
            SunAndMoonSetting(_normalizedTime);
            UpdateLight(_dayView.SunLight, _sunHD, _dayView.SunIntensity, _dayView.SunIntensityCurve, _dayView.SunTemperatureCurve, _normalizedTime, _sunWeight);
            UpdateLight(_dayView.MoonLight, _moonHD, _dayView.MoonIntensity, _dayView.MoonIntensityCurve, _dayView.MoonTemperatureCurve, _normalizedTime, _moonWeight);
        }

        private void SetupLight()
        {
            bool isActive = _sunWeight > 0f;

            if (_dayView.SunLight != null)
            {
                _dayView.SunLight.gameObject.SetActive(isActive);
            }

            isActive = _moonWeight > 0f;

            if (_dayView.MoonLight != null)
            {
                _dayView.MoonLight.gameObject.SetActive(isActive);
            }
        }

        private void SunAndMoonSetting(float time)
        {
            float sunRot = (time * 360f) - 90f;

            if (_dayView.SunLight != null)
                _dayView.SunLight.transform.rotation = Quaternion.Euler(sunRot, _dayView.SunPositionOffset, 0f);

            if (_dayView.MoonLight != null)
                _dayView.MoonLight.transform.rotation = Quaternion.Euler(sunRot + 180f, _dayView.SunPositionOffset, 0f);

            float currentExposure = _dayView.ExposureCurve.Evaluate(time);

            _exposureSetting.mode.overrideState = true;
            _exposureSetting.mode.value = ExposureMode.Fixed;

            _exposureSetting.fixedExposure.overrideState = true;
            Debug.Log($"TIME {time} , EXPOSURE SETTING {_exposureSetting.fixedExposure.value} => {currentExposure}");
            _exposureSetting.fixedExposure.value = currentExposure;
        }

        private void UpdateLight(Light light, HDAdditionalLightData hdData, float baseIntensity, AnimationCurve intensityCurve, AnimationCurve tempCurve, float time, float weight)
        {

            if (light == null) return;

            float curveVal = intensityCurve != null ? intensityCurve.Evaluate(time) : 1f;
            float finalIntensity = curveVal * baseIntensity;

            if (hdData != null)
            {
                hdData.intensity = finalIntensity * weight;

            }
            else
            {
                light.intensity = finalIntensity * weight;
            }

            if (tempCurve != null)
            {
                light.useColorTemperature = true;
                light.colorTemperature = tempCurve.Evaluate(time) * 10000f;
            }


        }

        private float SunWeight(float time)
        {
            if (time >= _dayView.SunRiseEnd && time <= _dayView.SunSetStart)
            {
                return 1f;
            }

            // 밤 (완전한 달)
            if (time <= _dayView.SunRiseStart || time >= _dayView.SunSetEnd)
            {
                return 0f;
            }

            // 일출 (Fade In)
            if (time > _dayView.SunRiseStart && time < _dayView.SunRiseEnd)
            {
                return Mathf.InverseLerp(_dayView.SunRiseStart, _dayView.SunRiseEnd, time);
            }

            // 일몰 (Fade Out)
            if (time > _dayView.SunSetStart && time < _dayView.SunSetEnd)
            {
                return 1f - Mathf.InverseLerp(_dayView.SunSetStart, _dayView.SunSetEnd, time);
            }

            return 0f;
        }

        private void UpdateSky(float time, float deltaTime)
        {
            if (_skySetting == null)
            {
                return;
            }

            // 별 밝기
            if (_dayView.StarIntensityCurve != null)
            {
                float starVal = _sunWeight >= 1f ? 0f : _dayView.StarIntensityCurve.Evaluate(time) * _dayView.StarIntenstiy;
                _skySetting.spaceEmissionMultiplier.overrideState = true;
                _skySetting.spaceEmissionMultiplier.value = starVal;
            }

            // 별 회전
            if (_dayView.IsRotStar && deltaTime > 0 && _sunWeight < 1f)
            {
                bool isDay = _sunWeight >= 1f;

                if (!isDay)
                {
                    Vector3 currentRot = _skySetting.spaceRotation.value;
                    currentRot.x = Mathf.Repeat(currentRot.x + _dayView.StarRotSpeed * deltaTime, 360f);
                    currentRot.y = Mathf.Repeat(currentRot.y + _dayView.StarRotSpeed * deltaTime, 360f);

                    _skySetting.spaceRotation.overrideState = true;
                    _skySetting.spaceRotation.value = currentRot;
                }
            }
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
        public override void Clear()
        {
            if (_view != null && _view.Volume != null && _view.Volume.HasInstantiatedProfile())
            {
                Object.Destroy(_view.Volume.profile);
            }
            base.Clear();
        }

    }
}
