using Cysharp.Threading.Tasks;
using Disaster.Network.FTP;
using DrillSergeant;
using NetworkResponeData;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TRAINEE
{
    public class UiSettingPopup : PopupBase, ISergeantPopups
    {
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_InputField _sceneTitle;
        [SerializeField] private TextMeshProUGUI _purpose;
        [SerializeField] private TMP_InputField _info;
        [SerializeField] private TMP_Dropdown _timeBlock;
        [SerializeField] private Toggle[] _weatherToggles = new Toggle[3];
        [SerializeField] private TextMeshProUGUI _subjectCountRatio;
        [SerializeField] private Toggle[] _modeToggles = new Toggle[2];
        [SerializeField] private Button _confirmBtn;
        [SerializeField] private Button _startBtn;

        [SerializeField] private Transform _camParent;
        [SerializeField] private Transform _spatialParent;
        [SerializeField] private RawImage _rawImage;

        [SerializeField] private Button[] _floorBtns = new Button[3];

        [SerializeField] private GameObject _loadingPart;
        [SerializeField] private TextMeshProUGUI _loadingText;
        [SerializeField] private Image _progressBar;

        private string _rescueeMaxCount = null;
        private bool _isInitialized = false;

        private DetailView[] _detailViews;
        private SpatialRow[] _spatialRows;
        private OccasionalRow[] _occationalRows; // 0. 요구조자, 1. 미정
        private string[] _currentFloorIds = new string[3]; // 데이터 주머니

        private string _uploadedPath = null;
        private const string _targetSceneName = "";


        public GameObject CamScreen
        {
            get => _rawImage.transform.parent.gameObject;
        }

        public override void Init()
        {
            base.Init();
            _canvas.worldCamera = UiManager.Instance.GetUiSubCamera;
            _canvas.targetDisplay = 1;

            _detailViews = _camParent.GetComponentsInChildren<DetailView>(true);
            _spatialRows = _spatialParent.GetComponentsInChildren<SpatialRow>(true);
            _occationalRows = _spatialParent.GetComponentsInChildren<OccasionalRow>(true);
            _occationalRows[0].gameObject.SetActive(false);
        }

        public override void Show()
        {
            base.Show();
        }

        public override void Hide()
        {
            base.Hide();
        }

        public void Refresh()
        {
            if (DrillManager.Instance.DrillUnit == null)
                return;

            ISergeantPopups.Active = this;

            if (!_isInitialized)
            {
                InitAddListener();
                _confirmBtn.gameObject.SetActive(true);
                _startBtn.gameObject.SetActive(false);

                _isInitialized = true; // 초기화 되었습니다
            }

            SubOptionSetting(); // 셋팅 설정
            SetupSceneLoading(false); // 로드
        }

        private void InitAddListener()
        {
            _sceneTitle.onValueChanged.AddListener((value) =>
            {
                DrillManager.Instance.DrillUnit.Title = value;
                DrillManager.Instance.DrillUnit.CopiedData.trainingData.scenarioName = value;
            });

            _info.onValueChanged.AddListener((value) =>
            {
                DrillManager.Instance.DrillUnit.CopiedData.trainingData.scenarioStory = value;
            });

            _timeBlock.onValueChanged.AddListener((value) =>
            {
                DrillManager.Instance.DrillUnit.CopiedData.trainingData.scenarioTime = value.ToString();
                DrillManager.Instance.DrillUnit.CopiedData.trainingData.terrainsetData.time = value.ToString();
            });

            for (int i = 0; i < _weatherToggles.Length; ++i)
            {
                int index = i;
                _weatherToggles[i].onValueChanged.AddListener((isOn) =>
                {
                    if (isOn)
                    {
                        DrillManager.Instance.DrillUnit.CopiedData.trainingData.scenarioWeather = index.ToString();

                        switch (index)
                        {
                            case 0: // Sun 
                                DrillManager.Instance.DrillUnit.CopiedData.trainingData.terrainsetData.sun = true;
                                break;

                            case 1: // Cloud
                                DrillManager.Instance.DrillUnit.CopiedData.trainingData.terrainsetData.cloud = "70";
                                break;

                            case 2: // Rain
                                DrillManager.Instance.DrillUnit.CopiedData.trainingData.terrainsetData.rain = true;
                                break;
                        }
                    }
                });
            }

            for (int i = 0; i < _floorBtns.Length; i++)
            {
                int index = i;
                _floorBtns[i].onClick.AddListener(() => OnFloorButtonClicked(index));
            }

            _confirmBtn.onClick.AddListener(() => SetupSceneLoading(true));
            // [todo]요구조자 변수 업데이트, 모드선택 이벤트설정 필요
            _startBtn.onClick.AddListener(RequestTrainingBegin);


            NetworkManager.Instance.FTPManager.OnProgress += OnFtpProgressGlobal;
            NetworkManager.Instance.FTPManager.OnTransferComplete += TransferComplete;

            NetworkManager.Instance.OnPartyChangeSettingSuccess += PartyChangeSettingSuccess;
            NetworkManager.Instance.OnPartyChangeSettingFailed += PartyChangeSettingFailed;
            NetworkManager.Instance.OnPartyConfirmSuccess += PartyConfirmSuccess;
            NetworkManager.Instance.OnPartyConfirmFailed += PartyConfirmFailed;

            //_net.OnTrainingCreatedSuccess += TrainingCreatedSuccess;
            //_net.OnTrainingCreatedFailed += TrainingCreatedFailed;

            // 전체 레디 확인까지
            NetworkManager.Instance.OnTrainingBeginReadyReceived += PartyBeginReadyResponse;

            NetworkManager.Instance.OnTrainingBeginSuccess += TrainingBeginSuccess;
            NetworkManager.Instance.OnTrainingBeginFailed += TrainingBeginFailed;
        }

        private void OnDestroy()
        {

            NetworkManager.Instance.FTPManager.OnProgress -= OnFtpProgressGlobal;
            NetworkManager.Instance.FTPManager.OnTransferComplete -= TransferComplete;

            NetworkManager.Instance.OnPartyChangeSettingSuccess -= PartyChangeSettingSuccess;
            NetworkManager.Instance.OnPartyChangeSettingFailed -= PartyChangeSettingFailed;
            NetworkManager.Instance.OnPartyConfirmSuccess -= PartyConfirmSuccess;

            //_net.OnTrainingCreatedSuccess -= TrainingCreatedSuccess;
            //_net.OnTrainingCreatedFailed -= TrainingCreatedFailed;

            NetworkManager.Instance.OnTrainingBeginReadyReceived -= PartyBeginReadyResponse;

            NetworkManager.Instance.OnTrainingBeginSuccess -= TrainingBeginSuccess;
            NetworkManager.Instance.OnTrainingBeginFailed -= TrainingBeginFailed;

        }

        private void SubOptionSetting()
        {
            TrainingDatas data = DrillManager.Instance.DrillUnit.CopiedData.trainingData;

            _icon.sprite = ScenarioRoleData.Instance.GetScenarioIcon(data.scenarioNum);
            _sceneTitle.text = data.scenarioName;
            _purpose.text = data.scenarioGoals;
            _info.text = data.scenarioStory;
            _timeBlock.value = int.Parse(data.terrainsetData.time);
            _subjectCountRatio.text = $"{data.rescueeCount} / {data.rescueeCount}";

            _rescueeMaxCount = data.rescueeCount; // 요구조자 maxCount 기록

            if (data.terrainsetData.sun == true)
                _weatherToggles[0].isOn = true;
            else if (data.terrainsetData.cloud != "0" || data.terrainsetData.cloud != null)
                _weatherToggles[1].isOn = true;
            else if (data.terrainsetData.rain == true)
                _weatherToggles[2].isOn = true;

        }


        private void SetupSceneLoading(bool isConfirmProcess = false)
        {
            if (DrillManager.Instance.DrillUnit.SergeantCountUsersInDrill() == 0 && isConfirmProcess == true)
            {
                Debug.Log($"해당 파티에 유저가 없습니다. {DrillManager.Instance.DrillUnit.PartyId}");
                return;
            }

            _progressBar.fillAmount = 0;
            _progressBar.transform.parent.gameObject.SetActive(true);
            _loadingPart.SetActive(true);

            if (isConfirmProcess == false)
                LoadOrReuseMonitoringScene().Forget();
            else
                ConfirmAndUploadProcess(); // 컨펌버튼 클릭시


        }



        #region "Confirm Process"
        private void ConfirmAndUploadProcess()
        {
            Debug.Log("파티체인지셋팅 리퀘스트");
            DrillUnit currentUnit = DrillManager.Instance.DrillUnit;
            NetworkManager.Instance.OnReqPartyChangeSetting(currentUnit.PartyId, currentUnit.SetParty.SceneFile, currentUnit.SetParty.SceneSetFile, currentUnit.SetParty.PartyName, currentUnit.SetParty.PartyInfo);
        }

        private void PartyChangeSettingSuccess(ResPartyChangeSetting res)
        {
            Debug.Log($"[PartConfirmimg]파티정보 셋팅 성공 {res.party.PartyId}");

            DrillUnit currentUnit = DrillManager.Instance.DrillUnit;

            currentUnit.CurrentState = DrillUnit.DrillState.Confirming;
            _confirmBtn.interactable = false;
            _loadingPart.SetActive(true);

            // CopiedData 최종점검 (요구조자수 입력, recueeSaveData 정리 등)
            ServerHubManager.Instance.ValidateCopiedData(DrillManager.Instance.DrillUnit);

            // ftp업로드 센딩 UploadStringAsync
            ServerHubManager.Instance.UploadSetDataToFTPAsync(DrillManager.Instance.DrillUnit).Forget();
            _loadingText.text = "훈련 데이터를 서버에 포장하는 중...";
        }

        private void PartyChangeSettingFailed(ResPartyChangeSetting res)
        {
            Debug.Log($"파티체인지셋팅 실패. 실패코드 : {res.code}");
        }

        private void OnFtpProgressGlobal(float progress)
        {
            if (_loadingPart != null && _loadingPart.activeSelf)
            {
                _progressBar.fillAmount = progress * 0.6f;
                _loadingText.text = $"훈련 데이터를 서버에 포장하는 중... {(progress * 100):F0}%";
            }
        }

        private void TransferComplete(string path, bool success)
        {
            if (success)
            {
                _uploadedPath = path;

                Debug.Log($"[PartyConfirmimg]파티컨펌 보내고 있는 파티 아이디   {DrillManager.Instance.DrillUnit.PartyId}");
                NetworkManager.Instance.OnReqPartyConfirm(DrillManager.Instance.DrillUnit.PartyId);

                _progressBar.fillAmount = 0.6f;
                _loadingText.text = "서버에 훈련 확정 승인 요청 중...";
            }
            else
                Debug.Log($"[PartyConfirmimg]FTP업로드중 리모트패스: {path},  성공여부: {success} ");
        }


        private void PartyConfirmSuccess(ResPartyConfirm res)
        {
            _progressBar.fillAmount = 0.8f;
            _loadingText.text = "교육생들의 접속을 기다리고 있습니다...";

            // trainingCreated & beginReady는 res만 들어옴
        }

        private void PartyConfirmFailed(ResPartyConfirm res)
        {
            _loadingText.text = $"<color=red>훈련 확정 승인에 실패했습니다. [실패코드: {res.code}]</color>";
        }

        private void PartyBeginReadyResponse(ResTrainingBeginReady res)
        {
            if (res.allConnected)
            {
                _loadingText.text = $"모든 교육생 접속 완료! [{res.data.ConnectedPlayerCount} / {res.data.ExpectedPlayerCount}]";

                _confirmBtn.gameObject.SetActive(false);
                _startBtn.gameObject.SetActive(true);

                _confirmBtn.interactable = true;

                _progressBar.fillAmount = 1f;
                _startBtn.GetComponent<Canvas>().sortingOrder = UiManager.SUPER_POPUP_SORTING + 1;

                Debug.Log($"<color=green>[PartConfirmimg]모든 훈련 세팅 완료. (저장된 FTP 경로: {_uploadedPath})</color>");
            }
            else
            {
                _loadingText.text = $"교육생 접속 대기 중...  [{res.data.ConnectedPlayerCount} / {res.data.ExpectedPlayerCount}]";

                if (res.data.ExpectedPlayerCount > 0)
                {
                    float ratio = (float)res.data.ConnectedPlayerCount / res.data.ExpectedPlayerCount;
                    _progressBar.fillAmount = 0.8f + (ratio * 0.15f);
                }

                Debug.Log($"[PartConfirmimg]교육생 대기 중...  [{res.data.ConnectedPlayerCount} / {res.data.ExpectedPlayerCount}]");
            }
        }
        #endregion


        #region "Begin Process"
        private void RequestTrainingBegin()
        {
            Debug.Log("트레이닝비긴 리퀘스트");
            NetworkManager.Instance.OnReqTrainingBegin(DrillManager.Instance.DrillUnit.PartyId);            
        }

        private void TrainingBeginSuccess(ResTrainingBegin res)
        {
            DrillManager.Instance.DrillUnit.CurrentState = DrillUnit.DrillState.InProgress;
            DrillManager.Instance.DrillUnit.SetDashboard();
            // 셋팅팝업에서 넘어간 대쉬보드일 경우 (BeginSuccess를 통해 넘어간)
            DrillManager.Instance.IsThroughBeginSuccess = true;

            Debug.Log($"[PartyBegining]파티 시작 성공. {res.code}");
        }

        private void TrainingBeginFailed(ResTrainingBegin res)
        {
            Debug.Log($"[PartyBegining]파티 시작 실패. {res.code}");
        }
        #endregion


        #region SetupProgress
        private async UniTask LoadOrReuseMonitoringScene()
        {
            DrillManager.Instance.RefreshCameraRenderer(false);

            await UniTask.Delay(400);

            DrillManager.Instance.MapController.ClearMap();

            _loadingPart.SetActive(true);
            _progressBar.fillAmount = 0;
            _loadingText.text = "미니맵 로딩중 ...";

            await DrillManager.Instance.SwitchAdditiveSceneAsync(_targetSceneName);

            _progressBar.fillAmount = 0.2f;
            await UniTask.Delay(400);

            await DrillManager.Instance.Builder.LoadMap(DrillManager.Instance.DrillUnit, DrillManager.Instance.MapController, progress =>
            {
                _progressBar.fillAmount = Mathf.Lerp(0.2f, 1f, progress);
            });

            _progressBar.fillAmount = 1;
            SetupToggleUi();

            DrillManager.Instance.CameraHandler.SetHovering();
            DrillManager.Instance.CameraHandler.SetDefaultPosition(CameraViewMode.TopDown);

            DrillManager.Instance.RefreshCameraRenderer(true);
            await UniTask.Delay(100);

            _loadingPart.SetActive(false);
        }

        // Minimap Buttons and Toggles 등록
        private void SetupToggleUi()
        {
            string scenarioId = DrillManager.Instance.DrillUnit.CopiedData.trainingData.scenarioNum;
            ScenarioData scenData = DataManager.Instance.GetData<ScenarioData>(EDataType.ScenarioData, scenarioId);

            string buildingId = ((int)scenData.buildingType).ToString();
            BuildingData targetBuilding = DataManager.Instance.GetData<BuildingData>(EDataType.BuildingData, buildingId);

            // 층수가 1개 초과일 경우만
            bool shouldShowButtons = targetBuilding.floors.Count > 1;

            for (int i = 0; i < _floorBtns.Length; ++i)
            {
                bool exists = i < targetBuilding.floors.Count;

                _floorBtns[i].gameObject.SetActive(exists && shouldShowButtons);

                if (exists)
                {
                    _currentFloorIds[i] = targetBuilding.floors[i];
                }
                else
                {
                    _currentFloorIds[i] = string.Empty;
                }
            }
            // 맨첫번째 버튼 발동
            _floorBtns[0].GetComponent<Button>().onClick.Invoke();
        }


        private void OnFloorButtonClicked(int btnIndex)
        {
            string realFloorId = _currentFloorIds[btnIndex];

            if (string.IsNullOrEmpty(realFloorId) == false)
            {
                int floorNum = btnIndex + 1;
                Debug.Log($"플로어넘버가 이상하게 들어오나??  {floorNum}");
                DrillManager.Instance.MapController.SetViewFloor(floorNum);
                DetailViewSet(realFloorId);
            }
        }

        private void DetailViewSet(string floorId)
        {
            foreach (DetailView view in _detailViews)
                view.gameObject.SetActive(false);

            FloorData floorData = DataManager.Instance.GetData<FloorData>(EDataType.FloorData, floorId);

            List<string> roomIds = floorData.rooms;

            for (int i = 0; i < roomIds.Count; i++)
            {
                string roomID = roomIds[i];
                RoomData roomData = DataManager.Instance.GetData<RoomData>(EDataType.RoomData, roomID);

                _detailViews[i].Refresh(roomData, SpatialRowSet);
                _detailViews[i].gameObject.SetActive(true);

                // occationalRow 셋팅
            }

            // 맨첫번째 토글 발동
            Toggle firstDetailView = _detailViews[0].GetComponent<Toggle>();
            firstDetailView.onValueChanged.Invoke(true);
            firstDetailView.SetIsOnWithoutNotify(true);
        }

        private void SpatialRowSet(string roomId)
        {
            foreach (SpatialRow row in _spatialRows)
                row.gameObject.SetActive(false);

            RoomData roomData = DataManager.Instance.GetData<RoomData>(EDataType.RoomData, roomId);

            List<string> elementIds = roomData.Elements;
            List<RoomElementData> trainingElements = DrillManager.Instance.DrillUnit.CopiedData.trainingData.roomElementDatas;

            for (int i = 0; i < elementIds.Count; ++i)
            {
                string elementId = elementIds[i];
                RoomElementData elementData = DataManager.Instance.GetData<RoomElementData>(EDataType.RoomElementData, elementId);

                // [Todo]요구조자 일경우 넘김 (요구조자는 따로 셋팅하는데 데이터에 왜 있는지 의문)
                // 곧 없앨 데이터로 조만간 삭제될 예정
                if (elementData.roomElementType == ERoomElementType.Rescuee)
                    continue;

                if (elementData != null && _spatialRows[i] != null)
                {
                    ERoomState currentState = ERoomState.Normal;

                    RoomElementData currentStatusData = trainingElements.Find(x => x.Id == elementId);
                    if (currentStatusData != null)
                    {
                        currentState = currentStatusData.roomState;
                    }

                    if (elementData != null && _spatialRows[i] != null)
                    {
                        _spatialRows[i].Refresh(elementData, currentState, OnRoomElementStateChanged);
                        _spatialRows[i].gameObject.SetActive(true);
                    }
                }
            }
            OccationalRowSet(roomId);
        }

        /// <summary>
        /// 1. 이 코드의 목적: 요구조자 혹은 그밖의 이벤트성 요소를 토글 선택으로 셋팅하기 위함
        /// 2. 핵심 로직 흐름(3줄 이내): SpatialRowSet이 끝나고 이벤트성(요구조자 등) 요소에 대한 토글을 만든다, 오브젝트풀링을 이용하지 않고 CopiedData에있는 rescueeDatas를 참고하여 셋팅, 토글기능으로 미니맵에 보여지는것 외에 CopiedData도 업데이트
        /// 3. 왜 이렇게 구현했는지: 이미 문제없이 잘 작동하는 SpatialRow는 그대로 사용하고 앞으로 추가되는 것들은 따로 관리하기 위함
        /// 4. 리스크: 새로운 훈련을 만들때마다 기존은 파괴하고 새로 만들어 데이터 효율성 떨어짐
        /// 5. 예외: 
        /// </summary>
        private void OccationalRowSet(string roomId)
        {
            foreach (OccasionalRow row in _occationalRows)
                row.gameObject.SetActive(false);

            RescueeSaveData rescueeData = DrillManager.Instance.DrillUnit.CopiedData.trainingData.rescueeDatas.Find(x => x.roomid == roomId);

            if (rescueeData == null)
                return;

            _occationalRows[0].Init(rescueeData.rescueeid, "요구조자", rescueeData.rescueeType, OnRescueeStateChanged);
            _occationalRows[0].gameObject.SetActive(true);
        }

        private void OnRescueeStateChanged(string rescueeId, ERescueeType newState)
        {
            UpdateRescueeCopiedData(rescueeId, (int)newState);

            DrillManager.Instance.MapController.ToggleRescueeSprite(rescueeId, (int)newState);


            // 여기 다시해야함
            int left = DrillManager.Instance.MapController.CountExceptForNoneRescuee();
            _subjectCountRatio.text = $"{left} / {_rescueeMaxCount}"; ;

            Debug.Log($"요구조자({rescueeId}) 상태 변경됨: {newState}");
        }

        private void UpdateRescueeCopiedData(string rescueeId, int newState)
        {
            RescueeSaveData targetData = DrillManager.Instance.DrillUnit.CopiedData.trainingData.rescueeDatas.Find(x => x.rescueeid == rescueeId);

            if (targetData != null)
            {
                targetData.rescueeType = (ERescueeType)newState;
            }
        }

        private void OnRoomElementStateChanged(string id, ERoomState state)
        {
            DrillManager.Instance.MapController.ChangeElementState(id, state);
            UpdateRoomElementData(id, state);
        }

        private void UpdateRoomElementData(string elementId, ERoomState newState)
        {
            List<RoomElementData> dataList = DrillManager.Instance.DrillUnit.CopiedData.trainingData.roomElementDatas;

            foreach (RoomElementData targetData in dataList)
            {
                if (targetData.Id == elementId)
                {
                    targetData.roomState = newState;
                    return;
                }
            }

            Debug.LogWarning($"[UpdateRoomElementData] CopiedData에 해당하는 데이터가 없습니다. (ID: {elementId})");

        }
        #endregion SetupProgress
    }
}