using TRAINEE;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace DrillSergeant
{
    public class MonitoringCameraHandler : MonoBehaviour
    {
        private Camera _camera;
        private float _fixedInitialZoom = 36f;
        private float _currentZoom = 36f;
        private Vector3 _lastMousePosition;
        private bool _isHover = false;

        private Bounds _mapBounds;
        private float _minZoom = 5f;
        private float _maxZoom = 50f;

        public Vector3 _offset = new Vector3(0, 0.16f, 0.4f);


        private PlayerController _playerController;

        public Camera Cam => _camera;
        public float CurrentZoom { get => _currentZoom; }

        public PlayerController PlayerController { get => _playerController; set => _playerController = value; }



        public void Init()
        {
            _camera = GetComponent<Camera>();

            if (_camera == null)
                _camera = gameObject.AddComponent<Camera>();

            _camera.targetTexture = DrillManager.Instance.RenderTexture;

            Debug.Log("카메라 생성이 안되나?");

            // 탑뷰로 셋팅
            SetCameraPerspective(CameraViewMode.TopDown);
        }

        public void SetHovering()
        {
            ISergeantPopups current = ISergeantPopups.Active;
            if (current != null)
            {
                GameObject screen = current.CamScreen;
                MinimapHoverTrigger hover = screen.GetComponent<MinimapHoverTrigger>();
                hover.Init(this);

            }
        }

        // 빌딩 생성 전 임시 위치 세팅
        public void SetDefaultPosition(CameraViewMode mode)
        {
            if (mode == CameraViewMode.FirstPerson)
                //transform.position = new Vector3(0, 1.8f, 0.05f);
                return;

            else
            {
                if (DrillManager.Instance.DrillUnit == null)
                    transform.position = new Vector3(0, 100, 0);

                else
                {
                    switch (DrillManager.Instance.DrillUnit.CopiedData.trainingData.scenarioNum)
                    {
                        case "0":
                            transform.position = new Vector3(5, 100, 0);
                            break;
                        case "1":
                            transform.position = new Vector3(0, 100, 0);
                            break;
                        case "2":
                            transform.position = new Vector3(190, 100, 70);
                            break;
                        case "3":
                            transform.position = new Vector3(0, 100, 0);
                            break;
                        case "4":
                            transform.position = new Vector3(0, 100, 0);
                            break;
                        case "5":
                            transform.position = new Vector3(140, 100, 120);
                            break;
                        case "6":
                            transform.position = new Vector3(0, 100, 0);
                            break;
                        case "7":
                            transform.position = new Vector3(120, 100, 90);
                            break;
                        case "8":
                            transform.position = new Vector3(0, 100, 0);
                            break;
                        case "9":
                            transform.position = new Vector3(0, 100, 0);
                            break;
                        case "10":
                            transform.position = new Vector3(0, 100, 0);
                            break;
                        case "11":
                            transform.position = new Vector3(150, 100, 127);
                            break;
                        case "12":
                            transform.position = new Vector3(0, 100, 0);
                            break;
                        case "13":
                            transform.position = new Vector3(0, 100, 0);
                            break;
                        case "14":
                            transform.position = new Vector3(0, 100, 0);
                            break;
                        default:
                            transform.position = new Vector3(0, 100, 0);
                            break;
                    }
                }
            }

        }

        /// <summary>
        /// 1. 이 코드의 목적: 빌딩 생성 후 카메라가 비출 범위
        /// 2. 핵심 로직 흐름(3줄 이내): SetInitialBuildingFocus에서 바운드계산, FocusToBounds에서 범위한정, ClampCameraPosition에서 마우스 입력에 따른 계산
        /// 3. 왜 이렇게 구현했는지: 시나리오마다 건물의 크기가 달라 카메라로 비출 범위를 어느정도 비슷하게 가져가고싶어서
        /// 4. 리스크: 빌딩 범위를 보이는게 정말 맞을지
        /// 5. 예외: 카메라의 이동이 터레인까지 되도록 수정
        /// </summary>
        public void FocusToBounds(Bounds terrainBounds)
        {
            // [추가] 터레인 바운드가 너무 작거나 0일 경우 고정값(500)으로 강제 세팅
            if (terrainBounds.size.x < 100f || terrainBounds.size.z < 100f)
            {
                Debug.LogWarning($"[MonitoringCameraHandler] 터레인 바운드가 부적절합니다({terrainBounds.size}). 500 사이즈로 보정합니다.");
                terrainBounds = new Bounds(terrainBounds.center, new Vector3(500f, 0f, 500f));
            }

            _mapBounds = terrainBounds;

            float screenRatio = (float)Screen.width / (float)Screen.height;
            float maxZoomX = (terrainBounds.size.x / 2f) / screenRatio;
            float maxZoomZ = terrainBounds.size.z / 2f;
            _maxZoom = Mathf.Max(maxZoomX, maxZoomZ);

            if (_maxZoom < _minZoom)
                _maxZoom = _minZoom;

            // 줌 설정은 고정값 기준
            _currentZoom = Mathf.Clamp(_fixedInitialZoom, _minZoom, _maxZoom);
            _camera.orthographicSize = _currentZoom;

            ClampCameraPosition();
        }

        /// <summary>
        /// 1. 이 코드의 목적: 탑뷰와 플레이어시점의 카메라 사용
        /// 2. 핵심 로직 흐름(3줄 이내): IsTraineePerspective 변수로 분기처리, 특정이미지에 호버됐을시 탑뷰카메라이동, 스폰된 플레이어 로테이션값 동기화
        /// 3. 왜 이렇게 구현했는지: 트랜스폼 관련된 서버와의 연동은 모든 Update의 마지막단에서 시행
        /// 4. 리스크: 서버와의 연동이 잘 될지, 카메라의 로테이션값이 잘 먹힐지
        /// 5. 예외: 카메라 쓰임에따라 update문 변경
        /// </summary>

        private void Update()
        {
            if (DrillManager.Instance.IsTraineePerspective == false)
            {
                if (_isHover)
                {
                    HandleZoom();
                    HandlePan();
                }
            }
        }


        private void FixedUpdate()
        {
            if (DrillManager.Instance.IsTraineePerspective == true)
            {
                if (_playerController == null)
                {
                    Debug.Log($"_playerController is Null");
                    return;
                }

                Quaternion cleanRotation = Quaternion.Euler(-_playerController.HeadPitch, _playerController.HeadBone.eulerAngles.y, 0f);
                Vector3 targetPosition = _playerController.HeadBone.position + (cleanRotation * _offset);

                transform.position = targetPosition;
                transform.rotation = cleanRotation;
            }
        }


        public void SetHoverState(bool state)
        {
            _isHover = state;
        }

        private void HandleZoom()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0)
            {
                _currentZoom = Mathf.Clamp(_currentZoom - scroll * 10f, _minZoom, _maxZoom);
                _camera.orthographicSize = _currentZoom;

                ClampCameraPosition();
            }
        }

        private void HandlePan()
        {
            if (Input.GetMouseButtonDown(2) || Input.GetMouseButtonDown(1))
            {
                _lastMousePosition = Input.mousePosition;
            }

            if (Input.GetMouseButton(2) || Input.GetMouseButton(1))
            {
                //Vector3 delta = Input.mousePosition - _lastMousePosition;
                //float moveSpeed = _camera.orthographicSize * 0.002f;
                //transform.Translate(-delta.x * moveSpeed, -delta.y * moveSpeed, 0);
                //_lastMousePosition = Input.mousePosition;

                Vector3 currentPos = Input.mousePosition;
                Vector3 delta = currentPos - _lastMousePosition;
                float moveSpeed = _camera.orthographicSize * 0.002f;
                Vector3 nextPos = transform.position;
                nextPos.x -= delta.x * moveSpeed;
                nextPos.z -= delta.y * moveSpeed;
                nextPos.y = 100f;

                transform.position = nextPos;
                _lastMousePosition = currentPos;

                ClampCameraPosition();
            }
        }

        private void ClampCameraPosition()
        {
            if (_mapBounds.size == Vector3.zero)
                return;

            float camHeight = _camera.orthographicSize;
            float camWidth = camHeight * _camera.aspect;

            float minX = _mapBounds.min.x + camWidth;
            float maxX = _mapBounds.max.x - camWidth;
            float minZ = _mapBounds.min.z + camHeight;
            float maxZ = _mapBounds.max.z - camHeight;

            if (minX > maxX)
                minX = maxX = _mapBounds.center.x;
            if (minZ > maxZ)
                minZ = maxZ = _mapBounds.center.z;

            Vector3 clampedPos = transform.position;
            clampedPos.x = Mathf.Clamp(clampedPos.x, minX, maxX);
            clampedPos.z = Mathf.Clamp(clampedPos.z, minZ, maxZ);

            transform.position = clampedPos;
        }

        /// <summary>
        /// 1. 이 코드의 목적: 플레이어 관점 카메라 설정
        /// 2. 핵심 로직 흐름(3줄 이내): 탑다운과 일인칭 시점에 대해 분기처리, 각 셋팅에 맞는 카메라 옵션설정
        /// 3. 왜 이렇게 구현했는지: 사용지점에 따라 카메라셋팅값을 바꿔야 하므로 
        /// 4. 리스크: 실제 플레이어 카메라 셋팅과 똑같이 해야할지 고민
        /// 5. 예외: 
        /// </summary>        
        public void SetCameraPerspective(CameraViewMode mode)
        {
            //_camera.TryGetComponent(out HDAdditionalCameraData hdCamData);

            //if (hdCamData == null)
            //{
            //    hdCamData = _camera.gameObject.AddComponent<HDAdditionalCameraData>();
            //    Debug.Log("[시스템] HDAdditionalCameraData가 없어서 코드로 강제 추가했습니다!");
            //}

            switch (mode)
            {
                case CameraViewMode.TopDown:
                    //_camera.targetTexture.Release();
                    _camera.orthographic = true;
                    _camera.orthographicSize = _currentZoom;
                    _camera.clearFlags = CameraClearFlags.Skybox;
                    _camera.backgroundColor = Color.black;
                    _camera.cullingMask = LayerMask.GetMask("Player", "Item", "Minimap", "SafeZone", "Rescuee"); // 터레인 관련도 넣어야할까, 비콘설정되면 수정필요
                    _camera.nearClipPlane = 0.1f;
                    _camera.farClipPlane = 400f;
                    _camera.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                    //hdCamData.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
                    //hdCamData.backgroundColorHDR = Color.black;

                    //hdCamData.customRenderingSettings = true; // 글로벌 그래픽 설정 무시 활성화

                    //// 프레임 세팅에서 포스트 프로세싱 끄기
                    //hdCamData.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.Postprocess] = true;
                    //hdCamData.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.Postprocess, false);

                    //// 안티앨리어싱(TAA) 끄기
                    //hdCamData.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                    break;

                case CameraViewMode.FirstPerson:
                    //_camera.targetTexture.Release();
                    _camera.orthographic = false;
                    _camera.fieldOfView = 80f;
                    _camera.clearFlags = CameraClearFlags.Skybox;
                    _camera.cullingMask = -1; // everything
                    _camera.nearClipPlane = 0.05f;
                    _camera.farClipPlane = 1000f;
                    _camera.transform.localPosition = Vector3.zero;
                    _camera.transform.localRotation = Quaternion.identity;

                    //hdCamData.clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;

                    //// 포스트 프로세싱 다시 켜기 (TAA 등 원상복귀)
                    //hdCamData.customRenderingSettings = false;
                    break;
            }
            SetDefaultPosition(mode);
        }
    }

    public enum CameraViewMode
    {
        TopDown,        // 교관석 미니맵 뷰
        FirstPerson     // 훈련생 1인칭 뷰
    }
}