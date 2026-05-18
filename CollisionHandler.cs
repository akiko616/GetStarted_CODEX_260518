using UnityEngine;

namespace TRAINEE
{
    public class CollisionHandler : IHandler
    {
        private ControllerBase _controller;

        private CharacterController _cc; 

        private CapsuleCollider _col;
        private float _checkDistance = 0.5f; // 감지 거리
        private int _pushableLayerMask;      // 밀 수 있는 물체의 레이어

        private Vector3 _lastPosition;
        public CollisionHandler()
        {

        }

        public void Init(ControllerBase controller)
        {
            _controller = controller;

            if (_controller is PlayerController player)
            {
                _cc = player.GetComponent<CharacterController>();

                if (_cc != null)
                {
                    _lastPosition = _cc.transform.position; // 초기 위치 저장
                }
            }

            if (_controller is PlayerController_VR player_VR)
            {
                _cc = player_VR.GetComponent<CharacterController>();

                if (_cc != null)
                {
                    _lastPosition = _cc.transform.position; // 초기 위치 저장
                }
            }

            _pushableLayerMask = ~LayerMask.GetMask("Player", "Default");
        }

        public void OnUpdate(float deltaTime)
        {

        }
        public void OnFixedUpdate(float deltaTime)
        {
            if (_controller != null)
            {
                HandleCollision(deltaTime);

                _lastPosition = _cc.transform.position;
            }
        }


        /// <summary>
        /// 충돌 검사 및 물리 연출
        /// </summary>

        private void HandleCollision(float deltaTime)
        {

            if (_cc == null)
            {
                return;
            }

            Vector3 realVelocity = (_cc.transform.position - _lastPosition) / deltaTime;

            if (realVelocity.sqrMagnitude < 0.01f) 
            {
                return;
            }

            Vector3 worldCenter = _cc.transform.TransformPoint(_cc.center);
            float radius = _cc.radius;
            float height = _cc.height;
            float offset = Mathf.Max(0, (height * 0.5f) - radius);

            Vector3 point1 = worldCenter + Vector3.up * offset;
            Vector3 point2 = worldCenter - Vector3.up * offset;

            RaycastHit hit;
            if (Physics.CapsuleCast(point1, point2, radius, _cc.transform.forward, out hit, _checkDistance, _pushableLayerMask))
            {
                Rigidbody body = hit.collider.attachedRigidbody;

                if (body != null)
                {
                    if (!body.isKinematic)
                    {
                        Vector3 pushDir = realVelocity;
                        pushDir.y = 0;
                        pushDir.Normalize();

                        body.AddForce(pushDir * 5f, ForceMode.Impulse);
                    }
                }
            }
            else
            {

            }


            //Vector3 worldCenter = _cc.transform.TransformPoint(_cc.center);

            //float radius = _cc.radius;
            //float height = _cc.height;

            //float offset = (height * 0.5f) - radius;
            //if (offset < 0) offset = 0;

            //Vector3 point1 = worldCenter + Vector3.up * offset; // 상단 구 중심
            //Vector3 point2 = worldCenter - Vector3.up * offset; // 하단 구 중심

            //RaycastHit hit;
            //if (Physics.CapsuleCast(point1, point2, radius, _cc.transform.forward, out hit, _checkDistance, _pushableLayerMask))
            //{
            //    Rigidbody body = hit.collider.attachedRigidbody;

            //    if (body != null && !body.isKinematic)
            //    {

            //        Vector3 pushDir = _cc.transform.forward;
            //        pushDir.y = 0; 
            //        pushDir.Normalize();

            //        body.AddForce(pushDir * 5f, ForceMode.Impulse);
            //    }
            //}
        }

        public void DrawGizmos()
        {
            if (_cc == null) return;

            // 1. 캡슐 정보 계산 (HandleCollision과 동일 로직)
            Vector3 worldCenter = _cc.transform.TransformPoint(_cc.center);
            float radius = _cc.radius;
            float height = _cc.height;
            float offset = (height * 0.5f) - radius;
            if (offset < 0) offset = 0;

            Vector3 point1 = worldCenter + Vector3.up * offset;   // 상단 구 중심
            Vector3 point2 = worldCenter - Vector3.up * offset;   // 하단 구 중심

            // 2. 현재 위치의 캡슐 그리기 (초록색)
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(point1, radius);
            Gizmos.DrawWireSphere(point2, radius);

            // 3. 캡슐이 나아갈 앞쪽 예상 위치 그리기 (빨간색)
            // CapsuleCast가 검사하는 '미래의 위치'를 보여줍니다.
            Gizmos.color = Color.red;
            Vector3 castDir = _cc.transform.forward * _checkDistance;
            Gizmos.DrawWireSphere(point1 + castDir, radius);
            Gizmos.DrawWireSphere(point2 + castDir, radius);

            // 두 위치를 연결하는 선 (시각적 도움)
            Gizmos.DrawLine(point1, point1 + castDir);
            Gizmos.DrawLine(point2, point2 + castDir);
        }
    }
}
