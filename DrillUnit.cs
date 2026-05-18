using Cysharp.Threading.Tasks;
using GameInstancePlugin;
using PartyManagerPlugin;
using System.Collections.Generic;
using TMPro;
using TRAINEE;
using UnityEngine;
using UnityEngine.UI;

namespace DrillSergeant
{
    public class DrillUnit : MonoBehaviour
    {
        [SerializeField] private VacantBox[] _vacantBoxes;
        [SerializeField] private Image _titleIcon;
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _purposeText;
        [SerializeField] private TextMeshProUGUI _participantText;

        [SerializeField] private Button _duplicateBtn;
        [SerializeField] private Button _deleteBtn;

        [SerializeField] private TextMeshProUGUI _elapsedTime;

        // 스탑,퍼즈,리쥼의 리퀘스트는 각DrillUnit에서발송, 리스폰스는 DeploymentHandler
        [SerializeField] private Button _stopBtn;
        [SerializeField] private Button _pauseBtn;
        [SerializeField] private Button _resumeBtn;

        [SerializeField] private GameObject _outline;

        [SerializeField] private GameObject _unitBlur;
        [SerializeField] private TextMeshProUGUI _theEndAnnounce;
        [SerializeField] private Button _theEndBtn;



        private DrillState _drillState = DrillState.None;

        private float _currentTime = 0f;
        private bool _isTimerRunning = false;
        private int _lastSecond = -1;

        private SaveFileDatas _copiedData;
        private int _partyId = -1;

        private Party _setParty = new();
        private string _fishnetServerIp = null;
        private int _fishnetPort = -1;

        private int _totalRescues = -1;

        private TrainResult _resultData = null;

        private DeploymentHandler _handler;
        private NetworkManager _net;



        public enum DrillState { None, Setup, Confirming, InProgress, Pause, Stop, Complited };

        public DrillState CurrentState
        {
            get { return _drillState; }
            set { _drillState = value; OnStateChangedAppearance(_drillState); }
        }

        public int PartyId { get => _partyId; set => _partyId = value; }
        public Party SetParty { get => _setParty; set => _setParty = value; }

        public string FishnetServerIp { get => _fishnetServerIp; set => _fishnetServerIp = value; }
        public int FishnetPort { get => _fishnetPort; set => _fishnetPort = value; }

        public TrainResult TrainResultData { get => _resultData; set => _resultData = value; }
        public DeploymentHandler Handler { set => _handler = value; }

        public SaveFileDatas CopiedData { get => _copiedData; set => _copiedData = value; }

        public string Title { set => _titleText.text = value; }

        public int TotalRescues { set => _totalRescues = value; }
        public string SubjectCountInTheEnd
        {
            set { _theEndAnnounce.text = $"훈련 종료 : 요구조자 {value}명 구조 완료."; }
        }



        private void Update()
        {
            if (!_isTimerRunning)
                return;

            _currentTime += Time.deltaTime;

            int currentSecond = Mathf.FloorToInt(_currentTime);
            if (currentSecond != _lastSecond)
            {
                UpdateTimerUI(_currentTime);
                _lastSecond = currentSecond;
            }
        }


        public void SetDrillUnit(SaveFileDatas ftpData)
        {
            _net = NetworkManager.Instance;

            _copiedData = ServerHubManager.Instance.Clone(ftpData);
            TrainingDatas data = _copiedData.trainingData;

            CurrentState = DrillState.Setup;

            _titleIcon.sprite = ScenarioRoleData.Instance.GetScenarioIcon(data.scenarioNum);
            _titleText.text = data.scenarioName;
            _purposeText.text = data.scenarioGoals;
            _participantText.text = data.scenarioPeople;


            TraineeRoleInfo[] info = ScenarioRoleData.Instance.GetTraineeRoles(data.scenarioNum).ToArray();
            for (int i = 0; i < _vacantBoxes.Length; ++i)
            {
                if (i < info.Length)
                {
                    //_vacantBoxes[i].RoleText = localData.taskRole[i];
                    _vacantBoxes[i].RoleText = info[i].stringData;
                    _vacantBoxes[i].RoleId = info[i].roleId;
                }
                else
                    _vacantBoxes[i].gameObject.SetActive(false);
            }


            GetComponent<Button>().onClick.AddListener(() => UnitClick());

            _duplicateBtn.onClick.AddListener(() => DuplicateBtn(ftpData));
            _deleteBtn.onClick.AddListener(DeleteBtn);
            _stopBtn.onClick.AddListener(StopBtn);
            _pauseBtn.onClick.AddListener(PauseBtn);
            _resumeBtn.onClick.AddListener(ResumeBtn);
            _theEndBtn.onClick.AddListener(TheEndBtn);

            InitSetupAsync();
        }

        private void InitSetupAsync()
        {
            // 스크롤이동
            _handler.FocusItem(GetComponent<RectTransform>());
            FishnetCheck();
        }


        private void UnitClick()
        {
            if (DrillManager.Instance.DrillUnit == this)
            {
                Debug.Log("이미 선택된 훈련입니다. UI 갱신을 생략합니다.");
                return;
            }

            FishnetCheck();
        }


        // [todo] 대시보드 이벤트 셋팅 
        public void SetDashboard()
        {

            FishnetCheck();
        }

        public void SetResult()
        {

            FishnetCheck();
        }

        private void FishnetCheck()
        {
            _handler.ClickedUnit = this;

            if (DrillManager.Instance.DrillUnit == null)
            {
                MatchingDrill(this).Forget();
                return;
            }

            // 클릭하는 Unit이 아닌 이전 DrillUnit을 체크
            if (NetworkManager.Instance.FishNetManager.ClientManager.Started)
            {
                Debug.Log($"[FichnetCheck] 기존연결 감지됨. 해당파티 피쉬넷 해지중 {DrillManager.Instance.DrillUnit.PartyId}");
                NetworkManager.Instance.OnReqTrainingMidLeave(DrillManager.Instance.DrillUnit.PartyId);
            }
            else
                MatchingDrill(this).Forget();
        }

        /// <summary>
        /// 1. 이 코드의 목적: 유닛의 State에 따라 띄워줄 popup을 정하고, 해당 팝업에서 Show와 Refresh를 해주기 위함
        /// 2. 핵심 로직 흐름: 
        /// 3. 왜 이렇게 구현했는지: 리퀘스트는 여러개의 DrillUnit중 하나가 발송됨, 리스폰스는 한군데에서 받아야하므로 DrillUnit의 상위단에서 실행함
        /// 4. 리스크: 상태값 자체에서 변경이 이뤄질때 실행되는 코드라서, 상태값 변경에 대한 혼용이 생길 경우에 따른 위험성
        /// 5. 예외: 
        /// </summary>
        public async UniTask MatchingDrill(DrillUnit unit)
        {
            DrillManager.Instance.DrillUnit = unit;

            unit.HidePopups(EPopupType.UiTrainingListPopup);
            unit.HidePopups(EPopupType.UiSettingPopup);
            unit.HidePopups(EPopupType.UiDashboardPopup);
            unit.HidePopups(EPopupType.UiResultPopup);

            unit.OutlineOneOnly();

            EPopupType popupType = EPopupType.None;

            if (_drillState == DrillState.Setup || _drillState == DrillState.Confirming)
            {
                popupType = EPopupType.UiSettingPopup;
            }
            else if (_drillState == DrillState.InProgress || _drillState == DrillState.Pause)
            {
                popupType = EPopupType.UiDashboardPopup;
            }
            else if (_drillState == DrillState.Stop || _drillState == DrillState.Complited)
            {
                popupType = EPopupType.UiResultPopup;
            }
            else
            {
                Debug.LogError($"[MatchingDrill] DrillState 오류 발생 : {_drillState}");
                return;
            }


            PopupBase popup = UiManager.Instance.FindPopup<PopupBase>(popupType);
            if (popup == null)
            {
                popup = await UiManager.Instance.ShowPopup(popupType);
            }
            else
            {
                popup.Show();
            }

            if (popup is ISergeantPopups sergeantPopup)
            {
                sergeantPopup.Refresh();
            }
            else
            {
                Debug.LogWarning($"[MatchingDrill] {popupType}에 ISergeantPopups 인터페이스가 구현되지 않았습니다.");
            }

            _handler.ClickedUnit = null;
        }

        private void DuplicateBtn(SaveFileDatas ftpData)
        {
            _handler.RequestCreateDrillUnit(ftpData);
        }

        private void DeleteBtn()
        {
            Debug.Log($"파티 딜리트 버튼클릭!! 현재유닛에 등록된 파티아이디: {PartyId}");
            _handler.RequestDestroyParty(this);
        }

        public void DeleteMedium()
        {
            foreach (VacantBox item in _vacantBoxes)
            {
                if (item == null)
                    continue;

                if (item.Occupied != null)
                    item.Occupied.ReturnHome();
            }

            HidePopups(EPopupType.UiTrainingListPopup);
            HidePopups(EPopupType.UiSettingPopup);
            HidePopups(EPopupType.UiDashboardPopup);
            HidePopups(EPopupType.UiResultPopup);

            _partyId = -1;
            _copiedData = null;
        }

        private void StopBtn()
        {
            _net.OnReqTrainingStop(_partyId);
        }

        private void PauseBtn()
        {
            _net.OnReqTrainingPause(_partyId);
        }

        private void ResumeBtn()
        {
            _net.OnReqTrainingPause(_partyId); // 재개하는것도 똑같이 보냄
        }

        // 훈련 마침
        private void TheEndBtn()
        {
            _handler.RequestDestroyParty(this);
        }

        public List<TraineeUnit> OnStartup()
        {
            List<TraineeUnit> traineeUnits = new List<TraineeUnit>();
            int maxPlayer = 0;
            for (int i = 0; i < _vacantBoxes.Length; ++i)
            {
                if (_vacantBoxes[i].gameObject.activeSelf == false)
                    continue;

                if (_vacantBoxes[i].Occupied != null)
                {
                    // 못움직이게 고정
                    _vacantBoxes[i].Occupied.traineeValues.isDragEnabled = false;
                    traineeUnits.Add(_vacantBoxes[i].Occupied);
                    maxPlayer++;
                }
                else
                {
                    // Trainee가 지정되지 않은곳
                    _vacantBoxes[i].IsAiPlayer = false; // 공석으로
                    _vacantBoxes[i].GetComponent<CanvasGroup>().blocksRaycasts = false; // 드롭 불가능
                    maxPlayer++;
                }
            }

            return traineeUnits;
        }


        public void HandlePartyCreated(int id)
        {
            _partyId = id;
            Debug.Log($"내 파티 ID는 {id}입니다.");
        }

        public int SergeantCountUsersInDrill()
        {
            int vacancyCount = 0;
            foreach (VacantBox count in _vacantBoxes)
            {
                if (count.Occupied)
                    vacancyCount++;
            }
            return vacancyCount;
        }

        private void OutlineOneOnly()
        {
            List<DrillUnit> siblings = new();
            siblings.AddRange(transform.parent.GetComponentsInChildren<DrillUnit>(true));

            if (siblings.Count == 0)
                return;
            else
            {
                foreach (var item in siblings)
                {
                    item._outline.SetActive(false);
                }
            }
            _outline.SetActive(true);
        }

        private void HidePopups(EPopupType type)
        {
            PopupBase popup = UiManager.Instance.FindPopup<PopupBase>(type);

            if (popup == null)
                return;

            UiManager.Instance.Hide<PopupBase>(popup);
        }

        private void UpdateTimerUI(float timeInSeconds)
        {
            System.TimeSpan t = System.TimeSpan.FromSeconds(timeInSeconds);

            // 0:00:00 (시:분:초) 형식
            _elapsedTime.text = $"{t.Hours:D1}:{t.Minutes:D2}:{t.Seconds:D2}";
        }

        private void AllVacantBoxInteractableBoolean()
        {
            foreach (VacantBox box in _vacantBoxes)
            {
                box.CanvasGroup.interactable = false;
                box.CanvasGroup.blocksRaycasts = false;
            }
        }


        private void OnStateChangedAppearance(DrillState newState)
        {
            UiMainExcurtionLayer mainLayer = UiManager.Instance.FindLayer<UiMainExcurtionLayer>(ELayerType.UiMainExcurtionLayer);

            _duplicateBtn.transform.parent.gameObject.SetActive(false);
            _deleteBtn.transform.parent.gameObject.SetActive(false);
            _elapsedTime.transform.parent.gameObject.SetActive(false);
            _stopBtn.transform.parent.gameObject.SetActive(false);
            _pauseBtn.transform.parent.gameObject.SetActive(false);
            _resumeBtn.transform.parent.gameObject.SetActive(false);
            _unitBlur.SetActive(false);
            _theEndBtn.gameObject.SetActive(false);
            mainLayer.DrillUnitBlur(false);
            _isTimerRunning = false;

            switch (newState)
            {
                case DrillState.Setup:
                    _duplicateBtn.transform.parent.gameObject.SetActive(true);
                    _deleteBtn.transform.parent.gameObject.SetActive(true);
                    _currentTime = 0f;
                    UpdateTimerUI(0);
                    break;

                case DrillState.Confirming:
                    mainLayer.DrillUnitBlur(true);
                    AllVacantBoxInteractableBoolean();
                    break;

                case DrillState.InProgress:
                    mainLayer.DrillUnitBlur(false);
                    _pauseBtn.transform.parent.gameObject.SetActive(true);
                    _stopBtn.transform.parent.gameObject.SetActive(true);
                    _isTimerRunning = true;
                    break;

                case DrillState.Pause:
                    _resumeBtn.transform.parent.gameObject.SetActive(true);
                    _stopBtn.transform.parent.gameObject.SetActive(true);
                    break;

                case DrillState.Stop:
                    _theEndAnnounce.text = "훈련을 종료합니다. 훈련종료 영상교육 중입니다...";
                    _unitBlur.SetActive(true);
                    break;

                case DrillState.Complited:
                    _theEndAnnounce.text = $"훈련 종료 : 요구조자 {_totalRescues}명 구조 완료.";
                    _unitBlur.SetActive(true);
                    _theEndBtn.gameObject.SetActive(true);
                    break;

                case DrillState.None:
                default:
                    // 아무것도 켜지 않음
                    break;
            }
        }
    }
}