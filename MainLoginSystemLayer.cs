using Cysharp.Threading.Tasks;
using DatabasePlugin;
using NetworkResponeData;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DrillSergeant;

namespace TRAINEE
{
    public class MainLoginSystemLayer : LayerBase
    {

        [SerializeField] private TMP_InputField _inputID;
        [SerializeField] private TMP_InputField _inputPW;
        [SerializeField] private GameObject _persistentMessage;
        [SerializeField] private TextMeshProUGUI _anounceText;
        [SerializeField] private Button _loginBtn;

        private float _blinkDuration = -1;
        private float _blinkEndTime = 0;


        public AccountResponse loginedSergeantInfo = null;


        private void Start()
        {
            Init();
        }
        public override void Init()
        {
            base.Init();

            _inputID.ActivateInputField();
            _inputID.Select();

            InitAddListener();
        }

        private void Update()
        {
            BlinkRoutine();

            ForceTabNavigation(_inputID, _inputPW);
            ForceTabNavigation(_inputPW, _inputID);
        }

        private void InitAddListener()
        {
            NetworkManager.Instance.OnLoginSuccess += MainLoginSuccess;
            NetworkManager.Instance.OnLoginFailed += MainLoginFailed;

            _loginBtn.onClick.AddListener(LoginButtonClicked);
            _inputID.onValidateInput = ValidateChar;
            _inputPW.onValidateInput = ValidateChar;

        }

        private void OnDestroy()
        {
            NetworkManager.Instance.OnLoginSuccess -= MainLoginSuccess;
            NetworkManager.Instance.OnLoginFailed -= MainLoginFailed;

            _loginBtn.onClick.RemoveListener(LoginButtonClicked);
            _inputID.onValidateInput = null;
            _inputPW.onValidateInput = null;

        }


        private char ValidateChar(string text, int charIndex, char addedChar)
        {
            char lowerChar = char.ToLower(addedChar);

            if (Regex.IsMatch(lowerChar.ToString(), "^[a-z0-9]$"))
            {
                return lowerChar;
            }

            return '\0';
        }

        private void MainLoginSuccess(ResLogin res)
        {
            if (res.account.AccountType == 0)
                return;

            GameManager.Instance.PlayerID = res.account.Id;
            GameManager.Instance.PlayerRole = DrillManager.Instance.SergeantRole;

            ServerHubManager.Instance.mainLoginedInput = _inputID.text;
            UiManager.Instance.ShowPopup(EPopupType.UiSergeantLoadingPopup).Forget();

            Debug.Log($"메인 로그인 성공, 접속한유저는  {res.account.Id}, {res.account.AccountType} ");
        }

        private void MainLoginFailed(ResLogin account)
        {
            _persistentMessage.SetActive(false);

            _blinkDuration = 3;
            _blinkEndTime = Time.time;
            _anounceText.enabled = true;
        }

        private void MainLoginConsole()
        {
            //
        }

        private void LoginButtonClicked()
        {
            NetworkManager.Instance.OnReqLogin(_inputID.text, _inputPW.text);
        }

        private void BlinkRoutine()
        {
            if (Time.time > _blinkEndTime + _blinkDuration)
            {
                if (_anounceText.enabled)
                {
                    _anounceText.enabled = false;
                    _persistentMessage.SetActive(true);
                }
                return;
            }
            float interval = 0.4f;
            _anounceText.enabled = ((Time.time - _blinkEndTime) % interval) < (interval / 2);
        }

        private void ForceTabNavigation(TMP_InputField from, TMP_InputField to = null)
        {
            if (!from.isFocused)
                return;

            if (to != null && Input.GetKeyDown(KeyCode.Tab))
            {
                to.ActivateInputField();
                to.Select();
            }
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
