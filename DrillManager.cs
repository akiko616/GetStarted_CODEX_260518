using Cysharp.Threading.Tasks;
using TRAINEE;
using UnityEngine;
using UnityEngine.EventSystems;
using EPOOutline;

namespace DrillSergeant
{
    public class DrillManager : Singleton<DrillManager>
    {
        [SerializeField] private RenderTexture _renderTexture;
        private MonitoringBuilder _builder;
        private DrillMapController _drillMapController;
        private SceneBase _currentMonitoringScene;
        private SergeantExcurtionScene _sergeantExcurtionScene;
        private MonitoringCameraHandler _cameraHandler;
        private GameEventManager _gameEventManager;

        private bool _isTraineePerspective = false;
        private bool _isThroughBeginSuccess = false;
        private string _currentAdditiveScene = string.Empty;
        private string _clickedCameraUnitId = string.Empty;
        private int _clickedFloor = -1;

        private const string _sergeantRole = "viewer";
        private const string _monitoringBuilderName = "MonitoringBuilder";
        private const string _cameraObjName = "MonitoringCamera";
        private const string _mapControllerName = "DrillMapController";

        public RenderTexture RenderTexture { get => _renderTexture; }
        public DrillUnit DrillUnit { get; set; }
        public MonitoringBuilder Builder { get => _builder; }
        public DrillMapController MapController { get => _drillMapController; }
        public SceneBase CurrentMonitoringScene { get => _currentMonitoringScene; set => _currentMonitoringScene = value; }
        public SergeantExcurtionScene SergeantExcurtionScene { get => _sergeantExcurtionScene; set => _sergeantExcurtionScene = value; }
        public MonitoringCameraHandler CameraHandler { get => _cameraHandler; }
        public GameEventManager GameEventManager { get => _gameEventManager; }
        public string SergeantRole { get => _sergeantRole; }
        public string CurrentAdditiveScene { get => _currentAdditiveScene; set => _currentAdditiveScene = value; }
        public bool IsTraineePerspective { get => _isTraineePerspective; set => _isTraineePerspective = value; }
        public bool IsThroughBeginSuccess { get => _isThroughBeginSuccess; set => _isThroughBeginSuccess = value; }

        public int ClickedFloor { get => _clickedFloor; set => _clickedFloor = value; }


        public string ClickedCameraUnitId
        {
            get => _clickedCameraUnitId;
            set
            {
                _clickedCameraUnitId = value;
                Perspective(_clickedCameraUnitId);
            }
        }

        protected override void Awake()
        {
            base.Awake();
        }

        protected override void Start()
        {
            base.Start();

            // 빌더 생성
            if (_builder == null)
            {
                GameObject builderObj = new GameObject(_monitoringBuilderName);
                _builder = builderObj.AddComponent<MonitoringBuilder>();
                builderObj.transform.SetParent(this.transform);
            }

            if (_drillMapController == null)
            {
                string path = $"{Const.Path.BUILT_IN_MAP_PATH}{_mapControllerName}";

                _drillMapController = LdResources.Load<DrillMapController>(path, this.transform);
            }

            if (_cameraHandler == null)
            {
                SetupCamera();
            }
        }

        protected override void Init()
        {
            base.Init();
        }     

        private void SetupCamera()
        {
            if (_cameraHandler != null) return;

            _cameraHandler = GetComponentInChildren<MonitoringCameraHandler>();

            if (_cameraHandler == null)
            {
                GameObject camObj = new GameObject(_cameraObjName);
                _cameraHandler = camObj.AddComponent<MonitoringCameraHandler>();
                _cameraHandler.Init();
                camObj.transform.SetParent(_drillMapController.transform);

                Outliner outliner = camObj.AddComponent<Outliner>();
                outliner.DilateShift = 4;
                outliner.BlurShift = 18;
            }
        }

        /// <summary>
        /// 1. 이 코드의 목적: 카메라 렌더링 기능 등을 아예 껐다가 다시 켜는 하드리셋
        /// 2. 핵심 로직 흐름(3줄 이내): 씬에 오브젝트가 다 회수or파괴 되고 새로이 스폰되고, 카메라를끄고 렌더텍스쳐도릴리즈, 카메라다시키고 렌더텍스쳐 크리에이트
        /// 3. 왜 이렇게 구현했는지: 기존 렌더링(기존오브젝트) 잔상이 남아있는 문제에 대하여 카메라 기능을 아예 껐다 켜는 프로세스를 시도
        /// 4. 리스크: Create 되면서 문제점 발생할지 걱정
        /// 5. 예외: 
        /// </summary>
        public void RefreshCameraRenderer(bool isOnProcess)
        {
            if (_cameraHandler == null)
            {
                Debug.Log("[CameraHandler] 카메라핸들러 참조가 안되어 있습니다.");
                return;
            }

            if (isOnProcess == false)
            {
                _cameraHandler.Cam.enabled = false;
                _cameraHandler.Cam.targetTexture = null;
                _renderTexture.Release();
            }
            else
            {
                _renderTexture.Create();
                //GL.Clear(true, true, Color.clear);
                _cameraHandler.Cam.targetTexture = _renderTexture;
                _cameraHandler.Cam.enabled = true;
            }
        }


        /// <summary>
        /// 1. 이 코드의 목적: 카메라 관점에 대한 설정
        /// 2. 핵심 로직 흐름(3줄 이내): 탑뷰일경우 컨트롤러의 자식으로 이동하여 카메라셋팅, 트레이니일경우 해당 컨트롤러 헤드의 자식으로 이동하여 카메라셋팅
        /// 3. 왜 이렇게 구현했는지: 트랜스폼 관련 연동해야 할 해당 오브젝트의 자식으로 가는게 가장 쉽다고 판단
        /// 4. 리스크 / 예외: 회수문제, 부모트랜스폼과의 연동문제 걱정
        /// </summary>
        public void Perspective(string id = null)
        {
            UiDashboardPopup popup = UiManager.Instance.FindPopup<UiDashboardPopup>(EPopupType.UiDashboardPopup);

            if (id == null)
            {
                if (popup != null)
                    popup.FloorBtnParent.SetActive(true);

                _isTraineePerspective = false;
                _drillMapController.SetViewFloor(_clickedFloor);

                _cameraHandler.SetCameraPerspective(CameraViewMode.TopDown);
                return;
            }


            popup.FloorBtnParent.SetActive(false);
            _drillMapController.SetViewFloor(-1);
            string clickedId = id.GetHashCode().ToString();

            PlayerSpawnSystem spwanSystem = GameManager.Instance.GetSystem<PlayerSpawnSystem>();
            PlayerController playerController = spwanSystem.GetPlayerById(clickedId) as PlayerController;

            _cameraHandler.PlayerController = playerController;

            _cameraHandler.SetCameraPerspective(CameraViewMode.FirstPerson);

            _isTraineePerspective = true;
        }

        public async UniTask<bool> SwitchAdditiveSceneAsync(string targetSceneName)
        {
            // 기존에 켜져 있는 씬이 있다면 무조건 파괴
            if (!string.IsNullOrEmpty(_currentAdditiveScene))
            {
                Debug.Log($"[Switching] 기존 씬 파괴: {_currentAdditiveScene}");

                // 씬을 언로드하여 그 안의 모든 오브젝트를 제거합니다.
                await UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(_currentAdditiveScene);

                _sergeantExcurtionScene.VolumeLightOnOff(true);

                _currentMonitoringScene = null;
                _currentAdditiveScene = string.Empty;
            }

            // 새로운 씬이 빈 값이 아니라면 로드 (Additive)
            if (!string.IsNullOrEmpty(targetSceneName))
            {
                Debug.Log($"[Switching] 새 씬 생성 및 로드: {targetSceneName}");

                _sergeantExcurtionScene.VolumeLightOnOff(false);

                await UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(targetSceneName, UnityEngine.SceneManagement.LoadSceneMode.Additive);

                await UniTask.Yield();

                // 방금 로드된 씬을 정확히 찾아 SceneBase 초기화
                UnityEngine.SceneManagement.Scene newlyLoadedScene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(targetSceneName);

                if (newlyLoadedScene.isLoaded)
                {
                    bool isDone = UnityEngine.SceneManagement.SceneManager.SetActiveScene(newlyLoadedScene);
                    Debug.Log($"셋액티브씬 되었나?  {isDone}");

                    _gameEventManager = FindAnyObjectByType<GameEventManager>();
                    TrainingScene sceneBase = FindAnyObjectByType<TrainingScene>();
                    EventSystem[] eventSystem = FindObjectsByType<EventSystem>(FindObjectsSortMode.None);

                    if (sceneBase != null)
                    {
                        eventSystem[1].gameObject.SetActive(false);

                        _currentMonitoringScene = sceneBase;
                        _currentMonitoringScene.Init();
                        Debug.Log($"<color=green>[Switching] {sceneBase.name} 씬 생성 및 Init 완료</color>");
                    }
                }
            }

            _currentAdditiveScene = targetSceneName;


            return true;
        }
    }
}