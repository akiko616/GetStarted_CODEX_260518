using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TRAINEE
{
    public class SubLoginSystemLayer : LayerBase
    {
        private const string pcFilePath = "DrillSergeant/SupervisorPW";

        [Header("CanvasGroup")]
        [SerializeField] private CanvasGroup _management;

        [Header("Components")]
        [SerializeField] private TMP_InputField _inputPassCode;
        [SerializeField] private Button _managerLogoutBtn;
        [SerializeField] private GameObject _mainBlur;

        private TextAsset _pcAsset;
        private float _blinkDuration = -1;
        private float _blinkEndTime = 0;

       
        private void Start()
        {
            Init();

        }
        public override void Init()
        {
            base.Init();
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = UiManager.Instance.GetUiSubCamera;
            _canvas.targetDisplay = 1;

            _pcAsset = Resources.Load<TextAsset>(pcFilePath);

            InitAddListener();
        }

        private void InitAddListener()
        {
            _managerLogoutBtn.onClick.AddListener(ManagerLogout);
            _inputPassCode.onValueChanged.AddListener(OnPassCodeChanged);
            _inputPassCode.onValidateInput = ValidateChar;
        }
        private void OnDestroy()
        {
            _managerLogoutBtn.onClick.RemoveListener(ManagerLogout);
            _inputPassCode.onValueChanged.RemoveListener(OnPassCodeChanged);
            _inputPassCode.onValidateInput = null;

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
        private void OnPassCodeChanged(string inputText)
        {
            if (_pcAsset.text == inputText)
            {
                _management.gameObject.SetActive(true);
                _mainBlur.gameObject.SetActive(true);
                _inputPassCode.text = null;
            }           
        }

        private void ManagerLogout()
        {
            _management.gameObject.SetActive(false);
            _mainBlur.gameObject.SetActive(false);
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
