using UnityEngine;


namespace TRAINEE
{
    public class DynamicProp : MonoBehaviour
    {
        private Rigidbody _rb = null;
        private Collider _col = null;
        public Rigidbody Rb { get => _rb;}
        public Collider Col { get => _col;}
        public void Init()
        {
            if (_rb == null)
            {
                _rb = GetComponent<Rigidbody>();
            }
            if (_col == null)
            {
                _col = GetComponent<Collider>();
            }

            _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }

        public void SetupPhysicsAuth(bool isServer)
        {
            if (_rb == null) return;

            if (isServer)
            {
                // 서버: 내가 직접 물리 연산을 수행하고 굴러다녀야 함
                _rb.isKinematic = false;
                _rb.useGravity = true;
            }
            else
            {
                // 클라이언트: 물리 연산을 끄고 오직 서버가 보내는 좌표값에만 복종함
                _rb.isKinematic = true;
                _rb.useGravity = false;
            }
        }
    }
}
