using UnityEngine;


namespace TRAINEE
{
    public class PlayerMovement : MovementBase
    {
        [Header("FPV Settings")]
        [SerializeField] private float _mouseSensitivity = 15f; // 마우스 감도
        [SerializeField] private float _lookXMinLimit = 85f; // 목 꺾임 방지 (위아래 제한)
        [SerializeField] private float _lookXMaxLimit = 85f; // 목 꺾임 방지 (위아래 제한)

        private float _xRotation = 0f;
        private float _velocity = 0f;
        private Vector3 moveDir = Vector3.zero;
        private Vector2 lookInput = Vector2.zero;

        public override float CurrentSpeed
        {
            get
            {
                if (_cc != null)
                {
                    Vector3 horizontalVelocity = new Vector3(_cc.velocity.x, 0f, _cc.velocity.z);
                    return horizontalVelocity.magnitude;
                }
                return 0f;
            }
        }
        public override void Init()
        {
            _cc = GetComponent<CharacterController>();
        }

        public override void SetupCameraTrans(Transform trans)
        {
            base.SetupCameraTrans(trans);
        }

        public override void OnUpdate(float deltaTime)
        {
            ApplyGravity(deltaTime);

            HandleLook(deltaTime);

            HandleMove(deltaTime);
        }

        public override void OnFixedUpdate(float deltaTime)
        {
            
        }

        public override void OnLateUpdate(float deltaTime)
        {
            // 카메라(머리)의 로컬 회전만 변경
            if (_cameraRoot != null)
            {
                _cameraRoot.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);
            }
        }

        public override void Move(Vector2 direction)
        {
            moveDir = transform.right * direction.x + transform.forward * direction.y;
        }

        public override void Look(Vector2 mouseDelta)
        {
            lookInput = mouseDelta;
        }

        private void HandleMove(float deltaTime)
        {
            Vector3 move = (moveDir * _moveSpeed) + (Vector3.up * _velocity);

            _cc?.Move(move * deltaTime);

            moveDir = Vector3.zero;
        }

        private void HandleLook(float deltaTime)
        {
            float yRot = lookInput.x * _mouseSensitivity * deltaTime;
            transform.Rotate(Vector3.up * yRot);

            float xRot = lookInput.y * _mouseSensitivity * deltaTime;

            _xRotation -= xRot;
            _xRotation = Mathf.Clamp(_xRotation, _lookXMinLimit, _lookXMaxLimit);

            lookInput = Vector2.zero;
        }

        private void ApplyGravity(float deltaTime)
        {
            if (_cc.isGrounded && _velocity < 0f)
            {
                _velocity = _gravity;
            }

            _velocity += _gravity * deltaTime;
        }
    }
}
