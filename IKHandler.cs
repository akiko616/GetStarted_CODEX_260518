using UnityEngine;
using UnityEngine.Animations.Rigging;


namespace TRAINEE
{
    public interface IIKControl
    {
        public void SetIKWeight(float weight = 0);
        public void SetLayerWeight(float weight = 0);
    }
    public class IKHandler : IHandler, IIKControl
    {
        private IkSetup _leftIk = null;
        private TwoBoneIKConstraint _leftRigWeight = null;
        private ControllerBase _controller = null;

        private MultiAimConstraint _spineAimConstraint;
        private Transform _aimTarget;

        private float _targetSpineWeight = 0f;
        private float _currentSpineWeight = 0f;
        private float _blendSpeed = 5f;

        private int _layer = 0;
        public IKHandler() { }

        public void Init(ControllerBase controller, IkSetup leftIk, TwoBoneIKConstraint leftRigWeight,
                         MultiAimConstraint spineAim = null, Transform aimTarget = null)
        {
            _controller = controller;
            _leftIk = leftIk;
            _leftRigWeight=leftRigWeight;

            _spineAimConstraint = spineAim;
            _aimTarget = aimTarget;

            if (_spineAimConstraint != null)
                _spineAimConstraint.weight = 0f;
        }

        public void OnUpdate(float deltaTime)
        {
            if (_spineAimConstraint == null) return;

            if (Mathf.Abs(_currentSpineWeight - _targetSpineWeight) > 0.001f)
            {
                _currentSpineWeight = Mathf.Lerp(_currentSpineWeight, _targetSpineWeight, deltaTime * _blendSpeed);
                _spineAimConstraint.weight = _currentSpineWeight;
            }
        }

        public void OnFixedUpdate(float deltaTime)
        {
        }

        public void SetLayer(int layer = 0)
        {
            _layer = layer;
        }

        public void SetIKWeight(float weight = 0)
        {
            if (_leftRigWeight != null)
            {
                _leftRigWeight.weight = weight;
            }
        }

        public void SetLayerWeight(float weight = 0)
        {
            if (_controller != null && _controller.GetAnimator != null)
            {
                // 장비 레이어(1번)의 가중치 조절
                _controller.GetAnimator.SetLayerWeight(_layer, weight);
            }
        }

        public void SetupLeftIk(Transform target = null, Transform hint = null)
        {
            if (_leftIk != null)
            {
                _leftIk.SetIk(target, hint);
            }
        }

        public void SetSpineAimTarget(Transform targetObj, float weight)
        {
            if (_aimTarget != null && targetObj != null)
            {
                _aimTarget.position = targetObj.position;
            }
            _targetSpineWeight = weight; // 목표값만 설정 (보간은 OnUpdate에서)
        }

        public void ClearSpineAimTarget()
        {
            _targetSpineWeight = 0f;
        }
    }
}
