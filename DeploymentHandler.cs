using Cysharp.Threading.Tasks;
using DatabasePlugin;
using GameInstancePlugin;
using NetworkResponeData;
using PartyManagerPlugin;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using TRAINEE;
using UnityEngine;
using UnityEngine.UI;
using static DrillSergeant.DrillUnit;

namespace DrillSergeant
{
    public class DeploymentHandler : MonoBehaviour
    {
        [SerializeField] private GameObject _drillUnitPrefab;

        [SerializeField] private ScrollRect _scrollRect;
        [SerializeField] private Transform _drillUnitParent;
        [SerializeField] private Button _createDrillBtn;
        [SerializeField] private Button _closeBtn;

        [SerializeField] private TextMeshProUGUI _sergeantDashboard;
        [SerializeField] private Transform _traineeBox;

        [SerializeField] private Transform _contentsBox;

        private UiMainExcurtionLayer _mainLayer;
        private UiSubExcurtionLayer _subLayer;
        private SaveFileDatas _currentFtpData = null;
        private NetworkManager _net;
        private DrillUnit _clickedUnit = null;

        private string _pendingTraineeId = null;
        private string _pendingRole = null;
        private int _pendingPartyId = -1;

        public Transform TraineeBox { get => _traineeBox; }
        public DrillUnit ClickedUnit { get => _clickedUnit; set => _clickedUnit = value; }

        public void Init(UiMainExcurtionLayer layer)
        {
            _net = NetworkManager.Instance;

            _mainLayer = layer;
           
            _sergeantDashboard.text = $"\"{ServerHubManager.Instance.mainLoginedInput}\" 님";
        }

        public void Setup()
        {
            InitAddListeners();

            NetworkManager.Instance.OnReqAdminGetOnlineUsers(-1);
        }

        private void InitAddListeners()
        {
            _createDrillBtn.onClick.AddListener(ShowListPopup);

            _net.OnUserGetReceived += OnlineUserListUpSuccess;

            _net.OnPartyCreateSuccess += PartyCreateSuccess;
            _net.OnPartyCreateFailed += PartyCreateFailed;

            _net.OnPartyDestorySuccess += PartyDestroySuccess;
            _net.OnPartyDestoryFailed += PartyDestroyFailed;



            _net.OnPartyJoinSuccess += PartyJoinSuccess;
            _net.OnPartyJoinFailed += PartyJoinFailed;

            _net.OnPartyLeaveSuccess += PartyLeaveSuccess;
            _net.OnPartyLeaveFailed += PartyLeaveFailed;

            _net.OnPartyMoveSuccess += PartyMoveSuccess;
            _net.OnPartyMoveFailed += PartyMoveFailed;

            _net.OnPartyChangeRoleSuccess += PartyChangeRoleSuccess;
            _net.OnPartyChangeRoleFailed += PartyChangeRoleFailed;



            _net.OnTrainingPauseSuccess += TrainingPauseSuccess;
            _net.OnTrainingPauseFailed += TrainingPauseFailed;

            _net.OnTrainingStopSuccess += TrainingStopSuccess;
            _net.OnTrainingStopFailed += TrainingStopFailed;

            _net.OnTrainingResultSuccess += TrainingResultSuccess;
            _net.OnTrainingResultFailed += TrainingResultFailed;



            _net.OnTrainingMidLeaveSuccess += TrainingMidLeaveSuccessAsync;
            _net.OnTrainingMidLeaveFailed += TrainingMidLeaveFailed;

            _net.OnTrainingGracefulQuitReceived += TrainingGracefulQuit;
        }

        private void OnDestroy()
        {
            _createDrillBtn.onClick.RemoveListener(ShowListPopup);

            _net.OnUserGetReceived -= OnlineUserListUpSuccess;

            _net.OnPartyCreateSuccess -= PartyCreateSuccess;
            _net.OnPartyCreateFailed -= PartyCreateFailed;

            _net.OnPartyDestorySuccess -= PartyDestroySuccess;
            _net.OnPartyDestoryFailed -= PartyDestroyFailed;

            _net.OnPartyJoinSuccess -= PartyJoinSuccess;
            _net.OnPartyJoinFailed -= PartyJoinFailed;

            _net.OnPartyLeaveSuccess -= PartyLeaveSuccess;
            _net.OnPartyLeaveFailed -= PartyLeaveFailed;

            _net.OnPartyMoveSuccess -= PartyMoveSuccess;
            _net.OnPartyMoveFailed -= PartyMoveFailed;

            _net.OnPartyChangeRoleSuccess -= PartyChangeRoleSuccess;
            _net.OnPartyChangeRoleFailed -= PartyChangeRoleFailed;

            _net.OnTrainingPauseSuccess -= TrainingPauseSuccess;
            _net.OnTrainingPauseFailed -= TrainingPauseFailed;

            _net.OnTrainingStopSuccess -= TrainingStopSuccess;
            _net.OnTrainingStopFailed -= TrainingStopFailed;

            _net.OnTrainingResultSuccess -= TrainingResultSuccess;
            _net.OnTrainingResultFailed -= TrainingResultFailed;

            _net.OnTrainingMidLeaveSuccess -= TrainingMidLeaveSuccessAsync;
            _net.OnTrainingMidLeaveFailed -= TrainingMidLeaveFailed;

            _net.OnTrainingGracefulQuitReceived -= TrainingGracefulQuit;
        }

        private void ShowListPopup()
        {
            _subLayer = UiManager.Instance.FindLayer<UiSubExcurtionLayer>(ELayerType.UiSubExcurtionLayer);

            UiManager.Instance.ShowPopup(EPopupType.UiTrainingListPopup).Forget();

            _subLayer.SetSubAnnounce(false, AnnounceMode.DrillCreated);
        }

        private void OnlineUserListUpSuccess(ResAdminGetOnlineUsers res)
        {
            List<DatabasePlugin.OnlineUserInfo> users = res.users;
            foreach (OnlineUserInfo user in users)
            {
                Debug.Log($"접속한 유저.  ID: {user.AccountId}");
                _mainLayer.TraineeDictionary.TryGetValue(user.AccountId, out TraineeUnit unit);

                if (unit != null)
                    unit.TraineeConnection = TraineeConnection.Connected;
            }
        }

        public void RequestCreateDrillUnit(SaveFileDatas ftpData)
        {
            Party template = ExtractedFromFtpOrigin(ftpData, out int maxPlayer);
            int max = maxPlayer;
            _currentFtpData = ftpData;

            NetworkManager.Instance.OnReqPartyCreate(template, max);
        }


        private void PartyCreateSuccess(ResPartyCreate res)
        {

            GameObject unit = Instantiate(_drillUnitPrefab, _drillUnitParent, false);

            // 빌드본에서 프리팹 사이즈가 작게 나타나는거에 대한 설정
            //unit.transform.localScale = Vector3.one;

            if (unit.TryGetComponent<DrillUnit>(out DrillUnit com))
            {
                com.PartyId = res.party.PartyId;
                com.SetParty.SceneSetFile = res.party.PartyId.ToString(); // 파티생성성공시 SceneSetFile도 채워줌
                com.Handler = this;
                com.SetDrillUnit(_currentFtpData);
                com.HandlePartyCreated(res.party.PartyId);

                Debug.Log($"파티생성 성공. 파티아이디: {res.party.PartyId}, 코드: {res.code}");
            }

            _createDrillBtn.transform.SetAsLastSibling();
            _currentFtpData = null;
        }

        private void PartyCreateFailed(ResPartyCreate res)
        {
            Debug.Log($"파티생성 실패 {res.code}");
        }

        public void RequestDestroyParty(DrillUnit unit)
        {
            _net.OnReqPartyDestory(unit.PartyId);
        }

        private void PartyDestroySuccess(ResPartyDestory res)
        {
            DrillUnit unit = FindInPartyId(res.partyid);
            unit.DeleteMedium();
            Destroy(unit.gameObject);

            Debug.Log($"파티삭제 성공 {res.partyid}");
            return;

        }

        private void PartyDestroyFailed(ResPartyDestory res)
        {
            Debug.Log($"파티삭제 실패 {res.partyid}, {res.code}");
        }

        private void PartyJoinSuccess(ResPartyJoin res)
        {
            Debug.Log("파티조인 성공");
            CheckAndFireChangeRole(res.party.PartyId);
        }

        private void PartyJoinFailed(ResPartyJoin res)
        {
            Debug.Log($"{res.party.PartyId}. 배치 실패. 서버 요청이 거부되었습니다.");
        }

        private void PartyLeaveSuccess(ResPartyLeave res)
        {
            Debug.Log($"파티탈퇴 성공 {res.code}");
        }

        private void PartyLeaveFailed(ResPartyLeave res)
        {
            Debug.Log($"파티탈퇴 실패 {res.code}");
        }
        private void PartyMoveSuccess(ResPartyMove res)
        {
            Debug.Log($"파티무브 성공 {res.code}");
            CheckAndFireChangeRole(res.party.PartyId);
        }

        private void PartyMoveFailed(ResPartyMove res)
        {
            Debug.Log($"파티무브 실패 {res.code}");
        }

        private void PartyChangeRoleSuccess(ResPartyChangeRole res)
        {
            List<string> roles = res.party.AccountIdAndRole.Values.ToList();
            Debug.Log($"파티롤변경 성공, Role전부:  " + string.Join(", ", roles));
        }

        private void PartyChangeRoleFailed(ResPartyChangeRole res)
        {
            List<string> roles = res.party.AccountIdAndRole.Values.ToList();
            Debug.Log($"파티롤변경 실패, Role전부:  " + string.Join(", ", roles));
        }

        /// <summary>
        /// 1. 이 코드의 목적: 훈련 퍼즈버튼 클릭후 서버로부터 success를 받은 후 실행사항
        /// 2. 핵심 로직 흐름: 퍼즈시킬 훈련을 찾는다, 상태값을 변경해준다, 변경된 상태값에 따라 액션을 구분하여 실행 
        /// 3. 왜 이렇게 구현했는지: 리퀘스트는 여러개의 DrillUnit중 하나가 발송됨, 리스폰스는 한군데에서 받아야하므로 DrillUnit의 상위단에서 실행함
        /// 4. 리스크: 상태값 자체에서 변경이 이뤄질때 실행되는 코드라서, 상태값 변경에 대한 혼용이 생길 경우에 따른 위험성
        /// 5. 예외: 
        /// </summary>
        private void TrainingPauseSuccess(ResTrainingPause res)
        {
            DrillUnit thatUnit = FindInPartyId(res.partyId);

            if (res.instanceSignalType == InstanceSignalType.Pause)
                thatUnit.CurrentState = DrillState.Pause;
            else if (res.instanceSignalType == InstanceSignalType.Resume)
                thatUnit.CurrentState = DrillState.InProgress;
            else
                Debug.Log($"확인된 시그널이 Pause/Resume가 아닙니다. 들어온 시그널: {res.instanceSignalType}");
        }

        private void TrainingPauseFailed(ResTrainingPause res)
        {
            Debug.Log($"트레이닝 퍼즈기능 실패.");
        }

        private void TrainingStopSuccess(ResTrainingStop res)
        {
            DrillUnit thatUnit = FindInPartyId(res.partyId);
            thatUnit.CurrentState = DrillState.Stop;

            // 이후 서버에서 TrainResult에 대한걸 보내줘야 할것으로 예상
        }

        private void TrainingStopFailed(ResTrainingStop res)
        {
            Debug.Log($"트레이닝 스톱 실패.");
        }


        /// <summary>
        /// 1. 이 코드의 목적: TrainingStop 성공 후 결과에 대한 정보를 서버로부터 받고 후속작업을 한다
        /// 2. 핵심 로직 흐름: TrainingStop 성공 후 서버에서 결과를 받는다, 받아온 파티아이디를 토대로 해당 유닛의 스테이트를 변경, 현재 클릭된(모니터링중) 유닛인지 여부에 따라 서브모니터 팝업 변경
        /// 3. 왜 이렇게 구현했는지: 다른 유닛을 모니터링중에도 종료된 유닛에 대한 후속작업이 이뤄지도록
        /// 4. 리스크: 블러화면으로 가려져서 클릭부분이 문제가 될지, 제이슨파일 후속작업에 따른 ui처리부분
        /// 5. 예외: 
        /// </summary>
        private void TrainingResultSuccess(ResTrainingResult res)
        {
            DrillUnit thatUnit = FindInPartyId(res.partyId);
            thatUnit.TrainResultData = JsonHelper.FromJson<TrainResult>(res.resultJson);

            Debug.Log($"[TrainingResult] 디시리얼라이즈 된것 확인 파티아이디: {thatUnit.TrainResultData.partyId}, 플레이타임: {thatUnit.TrainResultData.playTime} \n 리저트 몇개: {thatUnit.TrainResultData.results.Length}");
            Debug.Log($"[TrainingResult] 제이슨으로 뭐가 들어왔는지 확인필요 \n{res.resultJson}");

            thatUnit.CurrentState = DrillState.Stop;

            if (thatUnit == DrillManager.Instance.DrillUnit)
                thatUnit.MatchingDrill(thatUnit).Forget();
                        

            Debug.Log($"트레이닝리저트 풀링 성공");
        }

        private void TrainingResultFailed(ResTrainingResult res)
        {
            Debug.Log($"트레이닝리저트 풀링 실패. {res.code}");
        }

        /// <summary>
        /// 1. 이 코드의 목적: 훈련의 최종 마무리 콜백함수
        /// 2. 핵심 로직 흐름: 훈련이 종료되면서 result가 들어오고, 훈련생들의 모든 마무리(영상시청, 결과집계 등)가 되고, 최종적으로 Quit
        /// 3. 왜 이렇게 구현했는지: 각 유닛의 DrillState 에 따라 준비될 ui셋팅들이 다르기 때문
        /// 4. 리스크: if (thatUnit == DrillManager.Instance.DrillUnit) 부분에 대한 우려
        /// 5. 예외: 
        /// </summary>
        private void TrainingGracefulQuit(ResTrainingGracefulQuit res)
        {
            DrillUnit thatUnit = FindInPartyId(res.partyId);

            thatUnit.CurrentState = DrillState.Complited;

            if (thatUnit == DrillManager.Instance.DrillUnit)
                thatUnit.MatchingDrill(thatUnit).Forget();

            // 최종 종료(theEnd) 버튼클릭으로 마무리 예정 (PartyDestroySuccess)

            Debug.Log($"트레이닝 그레이스풀콰이트 성공");
        }



        private void DisconnectFishNetProcess()
        {
            NetworkManager.Instance.DisconnectFishNet();

            _clickedUnit.MatchingDrill(_clickedUnit).Forget();
        }

        private void TrainingMidLeaveSuccessAsync(ResTrainingMidLeave res)
        {
            DisconnectFishNetProcess();
            Debug.Log($"트레이닝 미드리브 성공. {res.code}");
        }
        private void TrainingMidLeaveFailed(ResTrainingMidLeave res)
        {
            Debug.Log($"트레이닝 미드리브 실패. {res.code}");
        }




        private DrillUnit FindInPartyId(int partyId)
        {
            foreach (DrillUnit unit in _contentsBox.GetComponentsInChildren<DrillUnit>())
            {
                if (unit.PartyId == partyId)
                    return unit;
            }
            Debug.Log($"파티를 찾을 수 없습니다. 파티아이디: {partyId}");
            return null;
        }

        public void ProcessTraineeMovement(string traineeId, MovementStatus status, int lastPartyId, int targetPartyId, string targetRole)
        {
            switch (status)
            {
                case MovementStatus.Join:
                    Debug.Log($"[메모장 작성 및 Join 발송] ID: {traineeId}");

                    _pendingTraineeId = traineeId;
                    _pendingPartyId = targetPartyId;
                    _pendingRole = targetRole;

                    _net.OnReqPartyJoin(traineeId, targetPartyId);
                    break;

                case MovementStatus.Leave:
                    Debug.Log($"[Leave 발송] ID: {traineeId}");
                    _net.OnReqPartyLeave(traineeId, lastPartyId);
                    break;

                case MovementStatus.Move:
                    Debug.Log($"[메모장 작성 및 Move 발송] ID: {traineeId}");

                    _pendingTraineeId = traineeId;
                    _pendingPartyId = targetPartyId;
                    _pendingRole = targetRole;
                    _net.OnReqPartyMove(lastPartyId, targetPartyId, traineeId);
                    break;

                case MovementStatus.ChangeRole:
                    Debug.Log($"[ChangeRole 직접 발송] ID: {traineeId}");
                    _net.OnReqPartyChangeRole(targetPartyId, traineeId, targetRole);
                    break;
            }
        }

        private void CheckAndFireChangeRole(int currentPartyId)
        {
            if (!string.IsNullOrEmpty(_pendingTraineeId) && _pendingPartyId == currentPartyId)
            {
                _net.OnReqPartyChangeRole(_pendingPartyId, _pendingTraineeId, _pendingRole);

                _pendingTraineeId = null;
                _pendingRole = null;
                _pendingPartyId = -1;
            }
        }

        public void FocusItem(RectTransform target)
        {
            Canvas.ForceUpdateCanvases();

            Vector2 contentPos = _scrollRect.transform.InverseTransformPoint(_scrollRect.content.position);
            Vector2 targetPos = _scrollRect.transform.InverseTransformPoint(target.position);

            Vector2 targetPosition = contentPos - targetPos;

            _scrollRect.content.anchoredPosition = new Vector2(_scrollRect.content.anchoredPosition.x, targetPosition.y);
        }

        private Party ExtractedFromFtpOrigin(SaveFileDatas ftpData, out int maxPlayer)
        {
            TrainingDatas trainingData = ftpData.trainingData;
            maxPlayer = int.Parse(trainingData.scenarioPeople);

            Party extractedParty = new()
            {
                SceneFile = ftpData.fileDate,
                PartyName = trainingData.scenarioName,
                PartyInfo = trainingData.scenarioStory,
                MaxPlayer = maxPlayer
            };

            return extractedParty;
        }

        
    }
}