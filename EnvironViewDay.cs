using TRAINEE;
using UnityEngine;

namespace TRAINEE
{
    public class EnvironViewDay : EnvironViewBase
    {

        [SerializeField] private Light _sunLight;
        [SerializeField] private Light _moonLight;

        [Header("Sun Setting")]
        [SerializeField] private float _sunBaseIntensity = 100000f;
        [SerializeField] private float _sunPositionOffset = 0f;
        [SerializeField] private AnimationCurve _sunIntensityCurve;
        [SerializeField] private AnimationCurve _sunTemperatureCurve;

        [Header("Moon Setting")]
        [SerializeField] private float _moonBaseIntensity = 1000f;
        [SerializeField] private float _moonPositionOffset = 0f;
        [SerializeField] private AnimationCurve _moonIntensityCurve;
        [SerializeField] private AnimationCurve _moonTemperatureCurve;

        [Header("Star Setting")]
        [SerializeField] private float _starBaseIntensity = 1000f;
        [SerializeField] private float _starRotationSpeed = 1f;
        [SerializeField] private AnimationCurve _starIntensityCurve;
        [SerializeField] private bool _rotateStar = true;

        [Header("Time Setting")]
        [SerializeField][Range(0f, 24f)] private float _defaultTime = 24f;
        [SerializeField] private float _sunriseStart = 5.7f;
        [SerializeField] private float _sunriseEnd = 6.3f;
        [SerializeField] private float _sunsetStart = 17.7f;
        [SerializeField] private float _sunsetEnd = 18.3f;


        [SerializeField] private AnimationCurve _exposureCurve;



        public Light SunLight { get { return _sunLight; } }
        public Light MoonLight {  get { return _moonLight; } }

        public float SunIntensity { get { return _sunBaseIntensity; } }
        public float SunPositionOffset { get { return _sunPositionOffset; } }

        public AnimationCurve SunIntensityCurve { get { return _sunIntensityCurve; } }
        public AnimationCurve SunTemperatureCurve { get { return _sunTemperatureCurve; } }


        public float MoonIntensity { get { return _moonBaseIntensity; } }
        public float MoonPositionOffset { get { return _moonPositionOffset; } }

        public AnimationCurve MoonIntensityCurve { get { return _moonIntensityCurve; } }
        public AnimationCurve MoonTemperatureCurve { get { return _moonTemperatureCurve; } }



        public float StarIntenstiy { get { return _starBaseIntensity; } }
        public float StarRotSpeed { get { return _starRotationSpeed; } }

        public AnimationCurve StarIntensityCurve { get { return _starIntensityCurve; } }
        public bool IsRotStar { get { return _rotateStar; } }


        public float DefaultTime { get { return _defaultTime; }}

        public float SunRiseStart { get { return _sunriseStart; } }
        public float SunRiseEnd { get { return _sunriseEnd; } }

        public float SunSetStart { get { return _sunsetStart; } }
        public float SunSetEnd { get {return _sunsetEnd; } }

        public AnimationCurve ExposureCurve { get { return _exposureCurve; } }
    }
}
