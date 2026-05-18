using Cysharp.Threading.Tasks;
using DrillSergeant;
using NetworkResponeData;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


namespace TRAINEE
{
    public class UiDashboardPopup : PopupBase, ISergeantPopups
    {
        [SerializeField] private TextMeshProUGUI _subjectCount;
        [SerializeField] private Button _targetDesignationBtn;
        [SerializeField] private TMP_InputField _textCommandInput;
        [SerializeField] private Button _textCommandSendBtn;

        [SerializeField] private ToggleGroup _camUnitsParent;
        [SerializeField] private GameObject _floorBtnParent;
        [SerializeField] private Button[] _floorBtns = new Button[3];
        [SerializeField] private Toggle _wholeMapBtn;
        [SerializeField] private RawImage _rawImage;

        [SerializeField] private GameObject _announceScreen;
        [SerializeField] private GameObject _loadingPart;
        [SerializeField] private TextMeshProUGUI _loadingText;
        [SerializeField] private Image _progressBar;

        private const string _targetSceneName = "Training";
        private CameraUnit[] cameraUnits;



        public GameObject CamScreen
        {
            get => _rawImage.transform.parent.gameObject;
        }

        public GameObject FloorBtnParent { get => _floorBtnParent; }

        public override void Init()
        {
            base.Init();

            _canvas.worldCamera = UiManager.Instance.GetUiSubCamera;
            _canvas.targetDisplay = 1;

            InitSetting();
        }

        private void InitSetting()
        {
            cameraUnits = GetComponentsInChildren<CameraUnit>(true);
            _targetDesignationBtn.onClick.AddListener(TargetIndicating);
            _wholeMapBtn.onValueChanged.AddListener((isOn) => DrillManager.Instance.Perspective());
            _wholeMapBtn.isOn = true; // 초기에 한번 클릭

            foreach (CameraUnit item in cameraUnits)
            {
                item.GetComponent<Toggle>().onValueChanged.AddListener((isOn) => DrillManager.Instance.ClickedCameraUnitId = item.ID);
            }

            NetworkManager.Instance.OnTrainingMidJoinSuccess += TrainingMidJoinSuccess;
            NetworkManager.Instance.OnTrainingMidJoinFailed += TrainingMidJoinFailed;
            // AnnounceText 프로토콜 추가
        }

        private void OnDestroy()
        {
            NetworkManager.Instance.OnTrainingMidJoinSuccess -= TrainingMidJoinSuccess;
            NetworkManager.Instance.OnTrainingMidJoinFailed -= TrainingMidJoinFailed;
        }



        public void Refresh()
        {
            if (DrillManager.Instance.DrillUnit == null)
                return;

            ISergeantPopups.Active = this;

            //DrillManager.Instance.CameraHandler.Cam.targetTexture.Release();

            SetupSceneLoading();
            CameraUnitSet();

            // 대쉬보드로 넘어가면서 모든 OccationalSprite 끔
            DrillManager.Instance.MapController.AllOccationSetOff();


            _wholeMapBtn.onValueChanged.Invoke(true);
        }

        // 이게 맞는지 확인 필요함
        private void SetupSceneLoading()
        {
            _progressBar.fillAmount = 0;
            _progressBar.transform.parent.gameObject.SetActive(true);
            _loadingPart.SetActive(true);

            LoadOrReuseMonitoringScene();
        }

        private void LoadOrReuseMonitoringScene()
        {
            if (DrillManager.Instance.IsThroughBeginSuccess == false)
            {
                DrillManager.Instance.MapController.ClearMap();
            }

            _loadingPart.SetActive(true);
            _progressBar.fillAmount = 0;
            _loadingText.text = "미니맵 로딩중 ...";

            NetworkManager.Instance.OnReqTrainingMidJoin(DrillManager.Instance.DrillUnit.PartyId);
        }

        private async UniTask MidJoinProcess()
        {
            Debug.Log($"트레이닝 미드조인 성공.");
            DrillManager.Instance.RefreshCameraRenderer(false);
            await UniTask.Delay(400);

            // 게임매니저에 셋팅할것들
            string currentScenarioNum = DrillManager.Instance.DrillUnit.CopiedData.trainingData.scenarioNum;
            GameManager.Instance.SetCurrentScenario(currentScenarioNum);

            // 데이터매니저에 서버관련 데이터 셋팅
            DataManager.Instance.OnServerDataSetup(DrillManager.Instance.DrillUnit.CopiedData);

            await DrillManager.Instance.SwitchAdditiveSceneAsync(_targetSceneName);

            _progressBar.fillAmount = 0.2f;

            if (DrillManager.Instance.IsThroughBeginSuccess == false)
            {
                await DrillManager.Instance.Builder.LoadMap(DrillManager.Instance.DrillUnit, DrillManager.Instance.MapController, progress =>
                {
                    _progressBar.fillAmount = Mathf.Lerp(0f, 0.5f, progress);
                });
            }
            FloorBtnSet();

            DrillManager.Instance.MapController.InitRegisteredViewsModels();

            await LoadingHelper.LoadingAsync((progress, msg) =>
            {
                _progressBar.fillAmount = 0.5f + (progress * 0.5f);
                _loadingText.text = msg;
            }, 100);

            LoadingHelper.AllClear();


            await NetworkManager.Instance.SergeantConnectFishNetAsync(DrillManager.Instance.DrillUnit.FishnetServerIp, DrillManager.Instance.DrillUnit.FishnetPort);



            Debug.Log("[Dashboard] 중간 진입 완료. 게임을 시작합니다.");
            GameManager.Instance.GameStart();

            _progressBar.fillAmount = 1f;
            DrillManager.Instance.CameraHandler.SetHovering();

            DrillManager.Instance.RefreshCameraRenderer(true);
            await UniTask.Delay(100); 

            _loadingPart.SetActive(false);

            DrillManager.Instance.IsThroughBeginSuccess = false; // 초기화
        }

        private void TrainingMidJoinSuccess(ResTrainingMidJoin res)
        {
            DrillManager.Instance.DrillUnit.FishnetPort = res.port;
            DrillManager.Instance.DrillUnit.FishnetServerIp = res.ip;
            MidJoinProcess().Forget();
        }

        private void TrainingMidJoinFailed(ResTrainingMidJoin res)
        {
            Debug.Log($"트레이닝 미드조인 실패. {res.code}");
        }

        public void FloorBtnSet()
        {
            string scenarioId = DrillManager.Instance.DrillUnit.CopiedData.trainingData.scenarioNum;
            ScenarioData scenData = DataManager.Instance.GetData<ScenarioData>(EDataType.ScenarioData, scenarioId);

            string buildingId = ((int)scenData.buildingType).ToString();
            BuildingData targetBuilding = DataManager.Instance.GetData<BuildingData>(EDataType.BuildingData, buildingId);

            // 층수가 1개 초과일 경우만
            bool shouldShowButtons = targetBuilding.floors.Count > 1;

            for (int i = 0; i < _floorBtns.Length; ++i)
            {
                int floorNum = i + 1;
                bool exists = i < targetBuilding.floors.Count;

                _floorBtns[i].gameObject.SetActive(exists && shouldShowButtons);

                if (exists)
                {
                    _floorBtns[i].onClick.RemoveAllListeners();

                    string realFloorId = targetBuilding.floors[i];

                    _floorBtns[i].onClick.AddListener(() =>
                    {
                        DrillManager.Instance.ClickedFloor = i;
                        DrillManager.Instance.MapController.SetViewFloor(floorNum);
                    });
                }
            }


            // 맨첫번째 버튼 발동
            _floorBtns[0].GetComponent<Button>().onClick.Invoke();
        }

        private void CameraUnitSet()
        {
            for (int i = 0; i < cameraUnits.Length; ++i)
            {
                cameraUnits[i].gameObject.SetActive(false);
            }
            _camUnitsParent.SetAllTogglesOff(); // 모든토글 isOn = false;


            List<TraineeUnit> passedOver = DrillManager.Instance.DrillUnit.OnStartup();
            Debug.Log($"Setting에서 넘어온 훈련생수 {passedOver.Count}");

            for (int k = 0; k < passedOver.Count; ++k)
            {
                // 인포셋팅하면서 setactive도 함께 실행
                cameraUnits[k].SetCameraUnitInfo(passedOver[k].TraineeTitle, passedOver[k].TraineeRole, passedOver[k].ThisTraineeID);
            }
        }

        private void TargetIndicating()
        {
           
        }


        private void AnnounceTextSuccess()
        {

        }

        private void AnnounceTextFailed()
        {

        }

                

        public override void Show()
        {
            base.Show();
        }

        public override void Hide()
        {
            base.Hide();
        }
    }
}
