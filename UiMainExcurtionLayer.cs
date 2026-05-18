using DrillSergeant;
using NetworkResponeData;
using PartyManagerPlugin;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TRAINEE
{
    public class UiMainExcurtionLayer : LayerBase
    {
        [SerializeField] private TextMeshProUGUI _sergeantBillboard;
        [SerializeField] private GameObject _blurMain;

        [SerializeField] private ModeSelectingHandler _modeSelectingHandler;
        [SerializeField] private DeploymentHandler _deploymentHandler;
        [SerializeField] private ReviewHandler _reviewHandler;

        private Dictionary<string, TraineeUnit> _traineeDictionary = new();

        // 일단 수기로 입력된 정보로 훈련생 ID 셋팅
        private List<TraineeInitData> _traineeInfos = new()
        {
            new TraineeInitData { id = "test1", equip = TraineeEquip.PC },
            new TraineeInitData { id = "test2", equip = TraineeEquip.PC },
            new TraineeInitData { id = "test3", equip = TraineeEquip.PC },
            new TraineeInitData { id = "test4", equip = TraineeEquip.PC },
            new TraineeInitData { id = "test5", equip = TraineeEquip.PC },
            new TraineeInitData { id = "test6", equip = TraineeEquip.PC },
            new TraineeInitData { id = "test7", equip = TraineeEquip.VR_PC },
            new TraineeInitData { id = "test8", equip = TraineeEquip.VR_PC },
            new TraineeInitData { id = "test9", equip = TraineeEquip.VR_TREADMILL },
            new TraineeInitData { id = "1234", equip = TraineeEquip.VR_TREADMILL }
        };

        private struct TraineeInitData
        {
            public string id;
            public TraineeEquip equip;
        }

        public ModeSelectingHandler ModeSelectingHandler { get => _modeSelectingHandler; }
        public DeploymentHandler DeploymentHandler { get => _deploymentHandler; }
        public ReviewHandler ReviewHandler { get => _reviewHandler; }


        public Dictionary<string, TraineeUnit> TraineeDictionary { get => _traineeDictionary; }


        public override void Init()
        {
            base.Init();

            InitAddListener();
            SetupInitialTraineeUnits();
            HandlerSetup();

            NetworkManager.Instance.OnReqPartyListGet();
        }


        private void InitAddListener()
        {
            NetworkManager.Instance.OnUserLoginReceived += UpdateTraineeConnection;
            NetworkManager.Instance.OnUserLogOutReceived += UpdateTraineeDisConnection;

            NetworkManager.Instance.OnPartyGetListSuccess += CurrentPartyGetListSuccess;
            NetworkManager.Instance.OnPartyGetListFailed += CurrentPartyGetListFailed;
        }


        private void OnDestroy()
        {
            NetworkManager.Instance.OnUserLoginReceived -= UpdateTraineeConnection;
            NetworkManager.Instance.OnUserLogOutReceived -= UpdateTraineeDisConnection;

            NetworkManager.Instance.OnPartyGetListSuccess -= CurrentPartyGetListSuccess;
            NetworkManager.Instance.OnPartyGetListFailed -= CurrentPartyGetListFailed;
        }

        private void HandlerSetup()
        {
            _modeSelectingHandler.Init(this);
            _deploymentHandler.Init(this);
            _reviewHandler.Init(this);

            _modeSelectingHandler.Setup();
            _deploymentHandler.Setup();
            _reviewHandler.Setup();
        }

        private void UpdateTraineeConnection(ResAdminUserLoginNotification res)
        {
            Debug.Log($"어카운트아이디 들어온것:  {res.account.Id}");
            if (_traineeDictionary.TryGetValue(res.account.Id, out TraineeUnit trainee))
            {
                Debug.Log($"[유저 접속 확인] 지금 접속되어있는 유저ID: {res.account.Id}");
                trainee.TraineeConnection = TraineeConnection.Connected;
            }
        }

        private void UpdateTraineeDisConnection(ResAdminUserLogOutNotification noti)
        {
            if (_traineeDictionary.TryGetValue(noti.clientId.ToString(), out TraineeUnit trainee))
            {
                trainee.TraineeConnection = TraineeConnection.Disconnected;
            }
        }

        private void SetupInitialTraineeUnits()
        {
            _traineeDictionary.Clear();

            for (int i = 0; i < _deploymentHandler.TraineeBox.childCount; ++i)
            {
                if (_deploymentHandler.TraineeBox.GetChild(i).TryGetComponent(out TraineeUnit trainee))
                {
                    TraineeInitData initData = _traineeInfos[i];
                    trainee.TraineeSetup();
                    trainee.SetInitialInfo(initData.id, initData.equip);
                    trainee.ReservedSeatNum = (i + 1).ToString();

                    if (!_traineeDictionary.ContainsKey(initData.id))
                        _traineeDictionary.Add(initData.id, trainee);
                }
            }
        }

        public void TurnOnJustOne(MonoBehaviour handler)
        {
            _modeSelectingHandler.gameObject.SetActive(_modeSelectingHandler == handler as ModeSelectingHandler);
            _deploymentHandler.gameObject.SetActive(_deploymentHandler == handler as DeploymentHandler);
            _reviewHandler.gameObject.SetActive(_reviewHandler == handler as ReviewHandler);
        }

        public void CreateMatchedDrill(SaveFileDatas ftpData)
        {

            _deploymentHandler.RequestCreateDrillUnit(ftpData);
        }


        private void CurrentPartyGetListSuccess(ResPartyListGet res)
        {
            Debug.Log($"남아있는 파티리스트 업데이트 성공. 남은파티: {res.parties.Count}");

            if (res.parties.Count < 1)
                return;

            // 교관석 접속시 만들어져있는 파티가 있으면 안되어서 전부 폐기
            foreach (Party eachParty in res.parties)
            {
                NetworkManager.Instance.OnReqPartyDestory(eachParty.PartyId);
            }
        }

        private void CurrentPartyGetListFailed(ResPartyListGet res)
        {
            Debug.Log($"파티 리스트업 실패 {res}");
        }

        public void DrillUnitBlur(bool isActiveTrue, string text = null)
        {
            if (isActiveTrue == false)
            {
                _blurMain.SetActive(isActiveTrue);
                return;
            }

            TextMeshProUGUI textMeshPro = _blurMain.GetComponentInChildren<TextMeshProUGUI>(true);

            if (text == null)
                textMeshPro.text = "훈련 생성중 ...";
            else
                textMeshPro.text = text;

            _blurMain.SetActive(isActiveTrue);
        }

        private char ValidateChar(string text, int charIndex, char addedChar)
        {
            if (addedChar > 127)  // ASCII 초과 = 한글/특수문자
                return '\0';

            char lowerChar = char.ToLower(addedChar);

            if ((lowerChar >= 'a' && lowerChar <= 'z') || (lowerChar >= '0' && lowerChar <= '9'))
            {
                return lowerChar;
            }

            return '\0';
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
