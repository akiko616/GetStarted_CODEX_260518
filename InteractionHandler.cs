using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;


namespace TRAINEE
{
    public interface IInteractableView
    {
        public GameModelEventBase Param { get; }
        public Vector3 Anchor { get; }
    }

    public class InteractionHandler : IHandler
    {
        private Camera _camera = null;
        
        private float _detectRadius = 0f;           // 플레이어 기준 검색 콜라이더 지름
        private float _dotLimt = 0.75f;             // 오브젝트 중심 기준 걸리는 범위
        private float _distance = 0f;
        private RaycastHit[] _raycastHits = null;
        private IInteractableView _currentInteractView = null;
        private ControllerBase _controller = null;
        public InteractionHandler(Camera camera)
        {
            _camera = camera;
            _raycastHits = new RaycastHit[10];
        }
        public void Init(ControllerBase controller)
        {
            _controller = controller;
            _detectRadius = _controller.DetectRadius;
            _distance = _controller.DetectDist;
        }

        public void OnUpdate(float deltaTime)
        {

        }

        public void OnFixedUpdate(float deltaTime)
        {
            if (_controller != null)
            {
                Vector3 pos = _controller.Anchor.position;
                HandleDetectInteraction(pos);
            }
        }

        /// <summary>
        /// Detect 인터렉션
        /// </summary>
        /// <param name="position"></param>

        private void HandleDetectInteraction(Vector3 position)
        {
            if (_camera == null)
                return;

            IInteractableView bestCandidate = null;

            if (Cursor.lockState == CursorLockMode.Locked || GameManager.Instance.VRModeCheck())
            {
                Ray ray = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                int layerMask = LayerMask.GetMask("Default", "Item", "Rescuee", "UXO","Disaster");
                int hitCnt = Physics.SphereCastNonAlloc(ray, _detectRadius, _raycastHits, _distance, layerMask);

                string currentEquipId = _controller.GetCurrentEquipId;

                float closestDistSqr = Mathf.Infinity;

                for (int i = 0; i < hitCnt; i++)
                {
                    RaycastHit hit = _raycastHits[i];
                    IInteractableView view = hit.collider.GetComponentInParent<IInteractableView>();

                    if (view != null)
                    {
                        if(view is DisasterViewBase disasterView)
                        {
                            if(!disasterView.CheckEquip(currentEquipId))
                            {
                                continue;
                            }
                        }

                        if (!IsTargetOnScreen(view.Anchor))
                        {
                            continue;
                        }

                        float dSqr = (view.Anchor - _camera.transform.position).sqrMagnitude;

                        if (dSqr < closestDistSqr)
                        {
                            closestDistSqr = dSqr;
                            bestCandidate = view;
                        }
                    }
                }
            }

            if (bestCandidate != _currentInteractView)
            {
                Debug.Log($"UI 나옴 best {bestCandidate} / current {_currentInteractView}");
                _currentInteractView = bestCandidate;
                GameEvent gameEvent = new GameEvent();
                gameEvent._type = EEventType.UI;

                if (_currentInteractView != null)
                    gameEvent._param = _currentInteractView.Param;
                else
                    gameEvent._param = null;

                Debug.Log($"UI 나옴 gameEvent._param: {gameEvent._param}");

                GameEventManager.Instance.Publish(gameEvent);
            }
        }

        private bool IsTargetOnScreen(Vector3 viewAnchorPos)
        {
            // 월드 좌표 -> 뷰포트 좌표 변환
            Vector3 vp = _camera.WorldToViewportPoint(viewAnchorPos);
#if UNITY_EDITOR
            Debug.DrawRay(_camera.transform.position, _camera.transform.forward * 2f, Color.blue);
#endif
            bool inProjection = false;

            if (vp.z < 0)
            {
                //Debug.Log("화면 뒤쪽");
                return inProjection;
            }

            Vector3 dir = (viewAnchorPos - _camera.transform.position);
            dir.y = 0f;
            dir.Normalize();
#if UNITY_EDITOR
            Debug.DrawRay(_camera.transform.position, dir * 2f, Color.red);
#endif
            float dot = Vector3.Dot(_camera.transform.forward, dir);

            if (dot < _dotLimt)
                return false;

            return true;
        }

        private bool IsTargetOccluded(Vector3 targetPos)
        {
            // 카메라에서 대상까지 선을 그어봄
            Vector3 cameraPos = _camera.transform.position;
            Vector3 dir = targetPos - cameraPos;
            float dist = dir.magnitude;

            // 벽(Default)에 부딪히면 true (가려짐)
            // 플레이어 자신과 부딪히지 않게 주의 (레이어 설정 중요)
            //if (Physics.Raycast(cameraPos, dir, dist, _obstacleLayer))
            //{
            //    return true; // 벽이 가리고 있음
            //}

            return false; // 뚫려 있음
        }

        /// <summary>
        /// Clik 인터렉션
        /// </summary>
        public void HandleClickInteraction()
        {
            if(_currentInteractView != null)
            {
                if (_currentInteractView.Param == null)
                {
                    return;
                }

                GameEvent gameEvent = new GameEvent();

                if(_currentInteractView is DisasterViewBase view)
                {
                    gameEvent._type = EEventType.LocalAction;
                }
                else
                {
                    gameEvent._type = EEventType.Interaction;
                }


                if (_currentInteractView.Param != null)
                {
                    gameEvent._id = _currentInteractView.Param._id;
                }

                GameModelEventBase payload = _currentInteractView.Param;

                if (_controller != null)
                {
                    payload._callerId = _controller.Id;
                }

                gameEvent._param = payload;

                GameEventManager.Instance.Publish(gameEvent);

                _currentInteractView = null;
            }
        }
    }
}
