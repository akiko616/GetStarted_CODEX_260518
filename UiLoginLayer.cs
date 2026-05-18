using Cysharp.Threading.Tasks;
using NetworkResponeData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace TRAINEE
{
    public class UiLoginLayer : LayerBase
    {

        [Header("DevMode")]
        [SerializeField] private GameObject _devMode = null;
        [SerializeField] private Button _btnStart = null;
        [SerializeField] private TMP_Dropdown _ddnScenario = null;
        [SerializeField] private TMP_Dropdown _ddnJob = null;
        [SerializeField] private GameObject _labelScenario = null;
        [SerializeField] private GameObject _labelJob = null;

        [Header("Live")]
        [SerializeField] private GameObject _liveMode = null;

        [Header("Login")]
        [SerializeField] private GameObject _labelLogin = null;

        public override void Init()
        {
            base.Init();

            InitAddListener();
#if UNITY_EDITOR || DEV_MODE
            ScenarioDropDownOption();

#else
#endif
        }

        private void InitActive()
        {
#if UNITY_EDITOR || DEV_MODE
            _devMode?.SetActive(true);
            //_liveMode?.SetActive(false);

#else
            _devMode?.SetActive(false);
            _liveMode?.SetActive(true);

#endif
        }

        private void ScenarioDropDownOption()
        {
            _ddnScenario.options.Clear();

            List<ScenarioData> data = DataManager.Instance.GetAllData<ScenarioData>(EDataType.ScenarioData);

            for (int i = 0; i < data.Count; i++)
            {
                TMP_Dropdown.OptionData option = new TMP_Dropdown.OptionData();
                option.text = DataManager.Instance.GetDisplayName(data[i].displayName.ToString());
                _ddnScenario.options.Add(option);
            }


        }

        // 버튼 등록
        private void InitAddListener()
        {
#if DEV_MODE
            _btnStart.onClick.AddListener(OnClickEventStart);
            _ddnScenario.onValueChanged.AddListener(OnClickEventSelectScenario);
            _ddnJob.onValueChanged.AddListener(OnClickEventSelectJob);
#else
    
#endif
        }

        private void InitActionRegister()
        {
            NetworkManager.Instance.OnLoginSuccess += OnLoginSuccess;
            NetworkManager.Instance.OnLoginFailed += OnLoginFailed;

        }

        public void OnClickEventSelectScenario(int id)
        {
            Debug.Log($"Scenario DropDown Option : {id}");

            _labelScenario.gameObject.SetActive(true);
            GameManager.Instance.SetCurrentScenario(id.ToString());
            //GameManager.Instance.GetCurrentScenario = (EScenario)id;
            CheckStartBtnShow();

            List<XRDisplaySubsystem> xrs = new List<XRDisplaySubsystem>();

            SubsystemManager.GetSubsystems<XRDisplaySubsystem>(xrs);

            bool isConnected = false;

            for (int i = 0; i < xrs.Count; i++)
            {

                if (xrs[i].running)

                {

                    isConnected = true;

                }

            }

            if (isConnected)
            {

                Debug.Log("VR 장비 연결 되어있음");
            }
        }

        public void OnClickEventSelectJob(int id)
        {
            Debug.Log($"Job DropDown Option : {id}");

            _labelJob.gameObject.SetActive(true);

            CheckStartBtnShow();
        }

        public void OnClickEventStart()
        {

            SceneManager.Instance.ChangeScene(ESceneType.Training).Forget();
            UiManager.Instance.Hide<UiLoginLayer>(this);
        }

        private void CheckStartBtnShow()
        {
            if (_labelScenario.activeSelf && _labelJob.activeSelf)
            {
                _btnStart.gameObject.SetActive(true);
            }
        }
        private CancellationTokenSource autoLoginCts;
        public async void AutoLogin()
        {
            // 오토 로그인 진행 로직

            if (_labelLogin != null)
            {
                _labelLogin.SetActive(true);
            }

            while (!NetworkManager.Instance.IsConnectedToNetwork)
            {
                // 다크 리프트 연결이 되었다면
                // 로그인을 진행한다.

                await UniTask.Delay(500);

            }

            autoLoginCts?.Cancel();
            autoLoginCts = new CancellationTokenSource();
            AutoLoginAsync(autoLoginCts.Token).Forget();
        }

        private async UniTaskVoid AutoLoginAsync(CancellationToken ct)
        {

            await UniTask.Delay(3000);
            var credentials = await LoadAccountCredentialsAsync(ct);

            if (!credentials.IsValid)
            {
                string errorMsg = "계정 파일을 찾을 수 없거나 형식이 올바르지 않습니다.";
                Debug.LogWarning($"[TraineeClient] 자동 로그인 실패: {errorMsg}");
                return;
            }

            Debug.Log($"Id : {credentials.Id} , Pw : {credentials.Password}");

            NetworkManager.Instance.OnReqLogin(credentials.Id, credentials.Password);
        }

        private string accountFileName = "account.txt";
        public readonly struct AccountCredentials
        {
            public string Id { get; }
            public string Password { get; }
            public bool IsValid => !string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(Password);

            public AccountCredentials(string id, string password)
            {
                Id = id ?? string.Empty;
                Password = password ?? string.Empty;
            }

            public override string ToString()
            {
                return $"AccountCredentials[ID:{Id}, HasPassword:{!string.IsNullOrEmpty(Password)}]";
            }
        }

        private async UniTask<AccountCredentials> LoadAccountCredentialsAsync(CancellationToken ct)
        {
            string filePath = Path.Combine(Application.streamingAssetsPath, accountFileName);

            Debug.Log($"[TraineeClient] 계정 파일 경로: {filePath}");

#if UNITY_ANDROID && !UNITY_EDITOR
            return await LoadAccountCredentialsFromAndroidAsync(filePath, ct);
#elif UNITY_WEBGL && !UNITY_EDITOR
            return await LoadAccountCredentialsFromWebGLAsync(filePath, ct);
#else
            return await LoadAccountCredentialsFromFileAsync(filePath, ct);
#endif
        }

        private async UniTask<AccountCredentials> LoadAccountCredentialsFromFileAsync(string filePath, CancellationToken ct)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"[TraineeClient] 계정 파일을 찾을 수 없음: {filePath}");
                return default;
            }

            string content = await UniTask.RunOnThreadPool(() => File.ReadAllText(filePath), cancellationToken: ct);
            return ParseAccountCredentials(content);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private async UniTask<AccountCredentials> LoadAccountCredentialsFromAndroidAsync(string filePath, CancellationToken ct)
        {
            using var request = UnityEngine.Networking.UnityWebRequest.Get(filePath);
            await request.SendWebRequest().WithCancellation(ct);

            if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[TraineeClient] Android 계정 파일 로드 실패: {request.error}");
                return default;
            }

            return ParseAccountCredentials(request.downloadHandler.text);
        }
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
        private async UniTask<AccountCredentials> LoadAccountCredentialsFromWebGLAsync(string filePath, CancellationToken ct)
        {
            using var request = UnityEngine.Networking.UnityWebRequest.Get(filePath);
            await request.SendWebRequest().WithCancellation(ct);

            if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[TraineeClient] WebGL 계정 파일 로드 실패: {request.error}");
                return default;
            }

            return ParseAccountCredentials(request.downloadHandler.text);
        }
#endif

        private AccountCredentials ParseAccountCredentials(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return default;
            }

            string id = "1234";
            string password = "1234";

            string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();

                if (trimmedLine.StartsWith("#") || trimmedLine.StartsWith("//"))
                    continue;

                int separatorIndex = trimmedLine.IndexOf(':');
                if (separatorIndex < 0)
                    separatorIndex = trimmedLine.IndexOf('=');

                if (separatorIndex > 0)
                {
                    string key = trimmedLine.Substring(0, separatorIndex).Trim().ToLowerInvariant();
                    string value = trimmedLine.Substring(separatorIndex + 1).Trim();

                    switch (key)
                    {
                        case "id":
                        case "account":
                        case "username":
                        case "user":
                            id = value;
                            break;
                        case "password":
                        case "pass":
                        case "pw":
                            password = value;
                            break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(password))
            {
                Debug.LogWarning("[TraineeClient] 계정 파일에서 ID 또는 Password를 찾을 수 없음");
                return default;
            }

            return new AccountCredentials(id, password);
        }

        public void OnLoginSuccess(ResLogin result)
        {
            Debug.Log("[UiLoginLayer] 로그인 성공!");

            if (_labelLogin != null)
            {
                _labelLogin.SetActive(false);
            }

            GameManager.Instance.PlayerID = result.account.Id;

            UiManager.Instance.Hide(ELayerType.UiLoginLayer);
            SceneManager.Instance.ChangeScene(ESceneType.TrainingLobby).Forget();
#if DEV_MODE
            InitActive();
#else

#endif
        }

        public async void OnLoginFailed(ResLogin result)
        {
            Debug.LogWarning($"[UiLoginLayer] 로그인 실패! (Code: {result.code}). 2초 후 재시도합니다...");

            await UniTask.Delay(System.TimeSpan.FromSeconds(2));

            AutoLogin();
        }

        public override void Show()
        {
            base.Show();
            
            _labelLogin.SetActive(false);
            InitActionRegister();

            AutoLogin();
        }





        public override void Hide()
        {
            base.Hide();

            _labelLogin.SetActive(false);

            NetworkManager.Instance.OnLoginSuccess -= OnLoginSuccess;
            NetworkManager.Instance.OnLoginFailed -= OnLoginFailed;
        }
    }
}
