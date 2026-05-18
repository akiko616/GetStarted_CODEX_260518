using Cysharp.Threading.Tasks;
using DrillSergeant;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


namespace TRAINEE
{
    public class UiResultPopup : PopupBase, ISergeantPopups
    {
        [SerializeField] private TMP_InputField _finalEvaluation;
        [SerializeField] private Image _icon;
        [SerializeField] private TextMeshProUGUI _sceneTitle;
        [SerializeField] private TextMeshProUGUI _purpose;
        [SerializeField] private TextMeshProUGUI _info;
        [SerializeField] private TMP_Dropdown _timeBlock;
        [SerializeField] private Toggle[] _weatherToggles = new Toggle[3];
        [SerializeField] private TMP_Dropdown _subject;
        [SerializeField] private Toggle[] _modeToggles = new Toggle[2];
        [SerializeField] private Button _saveBtn;

        [SerializeField] private Transform _camParent;
        [SerializeField] private RawImage _minimap;
        [SerializeField] private Transform _outcomeParent;

        [SerializeField] private GameObject _loadingPart;
        [SerializeField] private TextMeshProUGUI loadingText;
        [SerializeField] private Image _progressBar;

        private OutcomeRow[] _outcomeRows;
        private bool _isInitialized = false;
        private DrillUnit _drillUnitMain = null;

        public DrillUnit DrillUnit
        {
            get => _drillUnitMain;
            set => _drillUnitMain = value;
        }

        public GameObject CamScreen
        {
            get => _minimap.transform.parent.gameObject;
        }

        public override void Init()
        {
            base.Init();
            _canvas.worldCamera = UiManager.Instance.GetUiSubCamera;
            _canvas.targetDisplay = 1;
        }
        public void Refresh()
        {
            if (_drillUnitMain == null)
                return;

            ISergeantPopups.Active = this;

            if (!_isInitialized)
            {
                InitSetting();
                _isInitialized = true;
            }

            SubResultSet();
            Show();

        }

        private void InitSetting()
        {
            _outcomeRows = _outcomeParent.GetComponentsInChildren<OutcomeRow>(true);
            foreach (OutcomeRow row  in _outcomeRows)
            {
                row.gameObject.SetActive(false);
            }
            _saveBtn.onClick.AddListener(SaveBtn);

        }

        private void SubResultSet()
        {
            TrainingDatas data = _drillUnitMain.CopiedData.trainingData;

            _icon.sprite = ScenarioRoleData.Instance.GetScenarioIcon(data.scenarioNum);
            _sceneTitle.text = data.scenarioName;
            _purpose.text = data.scenarioGoals;
            _info.text = data.scenarioStory;
            _timeBlock.value = int.Parse(data.scenarioTime);
            
            // 요구조자 설정 필요

            if (data.terrainsetData.sun == true)
                _weatherToggles[0].isOn = true;
            else if (data.terrainsetData.cloud != "0" || data.terrainsetData.cloud != null)
                _weatherToggles[1].isOn = true;
            else if (data.terrainsetData.rain == true)
                _weatherToggles[2].isOn = true;

        }

        private void SaveBtn()
        {
            //= _finalEvaluation.text; // 어딘가 서버로 보내고 저장
            // main Unit 정리하고 Popup 정리
        }

        public override void Show()
        {
            base.Show();


        }

        public override void Hide()
        {
            base.Hide();
        }

        private async void PopupEffect()
        {

            await UniTask.Delay(500);

            //UiManager.Instance.Hide<UiLoadingPopup>(this);
        }
    }
}
