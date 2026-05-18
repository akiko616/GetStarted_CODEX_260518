using UnityEngine;

namespace TRAINEE
{
    public class RescueeMovement : MovementBase
    {
        [SerializeField] private float _stopDistance = 2.0f;

        private Transform _target = null;
        private float _velocity = 0f;

        public float StopDistance { get => _stopDistance; }
        public override void Init()
        {
            _cc = GetComponent<CharacterController>();
        }

        public void StopMove()
        {
            _target = null;
        }

        public override void Look(Vector2 mouseDelta)
        {
        }

        public override void Move(Vector2 direction)
        {
        }

        public void MoveToTarget(Transform target)
        {
            _target = target;
        }
        public override void OnUpdate(float deltaTime)
        {
            if (_cc == null)
            {
                return;
            }

            ApplyGravity(deltaTime);
            Vector3 direction = Vector3.zero;
            Vector3 move = Vector3.zero;

            if (_target != null)
            {
                direction = _target.position - transform.position;
                direction.y = 0f;

                if (direction.magnitude > _stopDistance)
                {
                    move = direction.normalized * _moveSpeed;

                    if (direction != Vector3.zero)
                    {
                        Quaternion toRotation = Quaternion.LookRotation(direction);
                        transform.rotation = Quaternion.Slerp(transform.rotation, toRotation, 10f * deltaTime);
                    }
                }
            }

            move.y = _velocity;
            _cc.Move(move * deltaTime);


        }

        public override void OnFixedUpdate(float deltaTime)
        {
        }

        public override void OnLateUpdate(float deltaTime)
        {
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
