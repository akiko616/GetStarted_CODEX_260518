using UnityEngine;


namespace TRAINEE
{
    public class EnvironViewCloud : EnvironViewBase
    {
        [SerializeField] private AnimationCurve _densityCurve;

        [SerializeField][Range(0.01f, 0.99f)] private float _maxDensityMultiplier = 0.5f;
        [SerializeField] private float _lerpSpeed = 1f;

        public AnimationCurve AnimationCurve { get { return _densityCurve; } }
        public float MaxDensityMultiplier { get { return _maxDensityMultiplier; } set { _maxDensityMultiplier = value; } }
        public float LerpSpeed { get { return _lerpSpeed; } }
    }
}
