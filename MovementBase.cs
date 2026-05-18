using UnityEngine;
using UnityEngine.AI;


namespace TRAINEE
{
    public abstract class MovementBase : MonoBehaviour
    {
        [Header("Move Settings")]
        [SerializeField] protected float _moveSpeed = 5f;
        [SerializeField] protected float _gravity = -9.81f;
        
        protected Transform _cameraRoot = null;

        protected CharacterController _cc;
        public float Speed { get => _moveSpeed; }
        public virtual float CurrentSpeed { get; }
        public abstract void Init();
        public abstract void Move(Vector2 direction);
        public abstract void Look(Vector2 mouseDelta);

        public abstract void OnUpdate(float deltaTime);

        public abstract void OnFixedUpdate(float deltaTime);

        public abstract void OnLateUpdate(float deltaTime);

        public virtual void SetupCameraTrans(Transform trans)
        {
            _cameraRoot = trans;
        }
    }
}
