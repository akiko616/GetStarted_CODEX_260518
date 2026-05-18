using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;

namespace LiveKitStreaming.Samples
{
    /// <summary>
    /// LiveKit 스트리밍 샘플 컨트롤러
    /// 토큰 생성 서버와 연동하여 완전한 예제 제공
    /// </summary>
    public class LiveKitSampleController : MonoBehaviour
    {
        #region Serialized Fields

        [Header("=== Config ===")]
        [SerializeField] private LiveKitStreamingConfig config;

        [Header("=== 설정 (Config 없을 때) ===")]
        [SerializeField] private StreamingMode mode = StreamingMode.Publisher;
        [SerializeField] private string liveKitUrl = "ws://192.168.0.228:7880";
        [SerializeField] private string tokenServerUrl = "http://192.168.0.228:3000";
        [SerializeField] private string roomName = "Room1";
        [SerializeField] private string identity = "unity-user";

        [Header("=== Publisher ===")]
        [SerializeField] private Camera captureCamera;

        [Header("=== Subscriber ===")]
        [SerializeField] private RawImage display;

        [Header("=== UI ===")]
        [SerializeField] private Button startButton;
        [SerializeField] private Button stopButton;
        [SerializeField] private Text statusText;
        [SerializeField] private InputField tokenInput;

        #endregion

        #region Private Fields

        private LiveKitStreamingManager manager;
        private string currentToken;
        private bool isStarting;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            SetupManager();
            SetupUI();
        }

        private void OnDestroy()
        {
            if (manager != null)
            {
                manager.OnConnected -= HandleConnected;
                manager.OnDisconnected -= HandleDisconnected;
                manager.OnReconnecting -= HandleReconnecting;
                manager.OnReconnected -= HandleReconnected;
                manager.OnStarted -= HandleStarted;
                manager.OnStopped -= HandleStopped;
                manager.OnError -= HandleError;
            }
        }

        #endregion

        #region Setup

        private void SetupManager()
        {
            manager = gameObject.AddComponent<LiveKitStreamingManager>();

            // Config가 있으면 Config 사용, 없으면 런타임 생성
            if (config != null)
            {
                manager.ApplyConfig(config);
            }
            else
            {
                // 런타임 Config 생성
                var runtimeConfig = LiveKitStreamingConfig.CreateDefault();
                manager.ApplyConfig(runtimeConfig);
            }

            // 모드 설정
            manager.SetMode(mode);

            // 서버 URL 설정
            var serverUrl = config?.ServerUrl ?? liveKitUrl;
            manager.SetServerUrl(serverUrl);

            // 이벤트 연결
            manager.OnConnected += HandleConnected;
            manager.OnDisconnected += HandleDisconnected;
            manager.OnReconnecting += HandleReconnecting;
            manager.OnReconnected += HandleReconnected;
            manager.OnStarted += HandleStarted;
            manager.OnStopped += HandleStopped;
            manager.OnError += HandleError;

            UpdateStatus("Ready");
        }

        private void SetupUI()
        {
            if (startButton != null)
            {
                startButton.onClick.AddListener(OnStartClicked);
            }

            if (stopButton != null)
            {
                stopButton.onClick.AddListener(OnStopClicked);
            }

            if (tokenInput != null)
            {
                tokenInput.onEndEdit.AddListener(OnTokenChanged);
            }

            UpdateButtonStates();
        }

        #endregion

        #region UI Event Handlers

        private void OnStartClicked()
        {
            StartStreamingAsync().Forget();
        }

        private void OnStopClicked()
        {
            manager?.Stop();
        }

        private void OnTokenChanged(string value)
        {
            currentToken = value;
        }

        #endregion

        #region Streaming

        private async UniTaskVoid StartStreamingAsync()
        {
            if (isStarting || manager == null) return;

            isStarting = true;
            UpdateButtonStates();
            UpdateStatus("Starting...");

            try
            {
                // 토큰이 없으면 토큰 서버에서 가져오기
                if (string.IsNullOrEmpty(currentToken))
                {
                    await FetchTokenAsync();
                }

                if (string.IsNullOrEmpty(currentToken))
                {
                    UpdateStatus("Failed to get token");
                    return;
                }

                manager.SetToken(currentToken);
                await manager.StartStreamingAsync();
            }
            catch (Exception e)
            {
                UpdateStatus($"Error: {e.Message}");
                Debug.LogError($"[Sample] Streaming error: {e}");
            }
            finally
            {
                isStarting = false;
                UpdateButtonStates();
            }
        }

        private async UniTask FetchTokenAsync()
        {
            var tokenUrl = config?.TokenServerUrl ?? tokenServerUrl;
            string endpoint = mode == StreamingMode.Publisher
                ? $"{tokenUrl}/token/publisher/{roomName}/{identity}"
                : $"{tokenUrl}/token/subscriber/{roomName}/{identity}";

            UpdateStatus("Fetching token...");

            try
            {
                using var request = UnityWebRequest.Get(endpoint);
                request.timeout = 10;

                await request.SendWebRequest().ToUniTask();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var response = JsonUtility.FromJson<TokenResponse>(request.downloadHandler.text);
                    currentToken = response.token;

                    Debug.Log($"[Sample] Token received for {response.identity} in room {response.room}");

                    if (tokenInput != null)
                    {
                        tokenInput.text = currentToken;
                    }
                }
                else
                {
                    Debug.LogError($"[Sample] Token fetch failed: {request.error}");
                    UpdateStatus($"Token error: {request.error}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Sample] Token fetch exception: {e.Message}");
                UpdateStatus($"Token error: {e.Message}");
            }
        }

        #endregion

        #region Manager Event Handlers

        private void HandleConnected()
        {
            UpdateStatus("Connected");
            UpdateButtonStates();
        }

        private void HandleDisconnected()
        {
            UpdateStatus("Disconnected");
            UpdateButtonStates();
        }

        private void HandleReconnecting()
        {
            UpdateStatus("Reconnecting...");
        }

        private void HandleReconnected()
        {
            UpdateStatus("Reconnected");
        }

        private void HandleStarted()
        {
            UpdateStatus(mode == StreamingMode.Publisher ? "Publishing" : "Subscribed");
            UpdateButtonStates();
        }

        private void HandleStopped()
        {
            UpdateStatus("Stopped");
            UpdateButtonStates();
        }

        private void HandleError(string message)
        {
            UpdateStatus($"Error: {message}");
            UpdateButtonStates();
        }

        #endregion

        #region UI Helpers

        private void UpdateStatus(string status)
        {
            if (statusText != null)
            {
                statusText.text = status;
            }
            Debug.Log($"[Sample] Status: {status}");
        }

        private void UpdateButtonStates()
        {
            bool isActive = manager?.IsActive ?? false;
            bool isConnected = manager?.IsConnected ?? false;

            if (startButton != null)
            {
                startButton.interactable = !isStarting && !isActive;
            }

            if (stopButton != null)
            {
                stopButton.interactable = isConnected || isActive;
            }
        }

        #endregion

        #region Debug GUI

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void OnGUI()
        {
            if (config != null && !config.ShowDebugGUI) return;

            GUILayout.BeginArea(new Rect(10, 410, 400, 200));
            GUI.Box(new Rect(0, 0, 400, 200), "");

            GUILayout.Label("<b>LiveKit Sample Controller</b>");
            GUILayout.Space(5);

            GUILayout.Label($"Mode: {mode}");
            GUILayout.Label($"Room: {roomName}");
            GUILayout.Label($"Identity: {identity}");
            GUILayout.Label($"Token Server: {config?.TokenServerUrl ?? tokenServerUrl}");
            GUILayout.Label($"Connected: {manager?.IsConnected ?? false}");
            GUILayout.Label($"Active: {manager?.IsActive ?? false}");

            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Start", GUILayout.Height(25)))
            {
                OnStartClicked();
            }
            if (GUILayout.Button("Stop", GUILayout.Height(25)))
            {
                OnStopClicked();
            }
            if (GUILayout.Button("Fetch Token", GUILayout.Height(25)))
            {
                FetchTokenAsync().Forget();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(5);
            GUILayout.Label("Token (paste or fetch):");
            currentToken = GUILayout.TextField(currentToken ?? "", GUILayout.Width(380));

            GUILayout.EndArea();
        }
#endif

        #endregion

        #region Data Structures

        [Serializable]
        private class TokenResponse
        {
            public string token;
            public string room;
            public string identity;
        }

        #endregion
    }
}
