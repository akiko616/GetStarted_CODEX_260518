using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace LiveKitStreaming
{
    /// <summary>
    /// LiveKit 스트리밍 통합 매니저
    /// 단일 컴포넌트로 Publisher/Subscriber 설정
    /// </summary>
    public class LiveKitStreamingManager : MonoBehaviour
    {
        #region Serialized Fields

        [Header("=== 설정 ===")]
        [SerializeField] private LiveKitStreamingConfig config;

        [Header("=== 모드 ===")]
        [SerializeField] private StreamingMode mode = StreamingMode.Publisher;

        [Header("=== 서버 설정 (Config 없을 때) ===")]
        [SerializeField] private string serverUrl = "ws://localhost:7880";
        [SerializeField] private string token = "";

        [Header("=== Publisher 설정 ===")]
        [SerializeField] private Camera captureCamera;

        [Header("=== Subscriber 설정 ===")]
        [SerializeField] private RawImage displayImage;

        [Header("=== 자동 시작 ===")]
        [SerializeField] private bool autoStart = false;

        #endregion

        #region Public Properties

        /// <summary>현재 모드</summary>
        public StreamingMode Mode => mode;

        /// <summary>연결 상태</summary>
        public bool IsConnected => roomManager != null && roomManager.IsConnected;

        /// <summary>재연결 중 여부</summary>
        public bool IsReconnecting => roomManager?.IsReconnecting ?? false;

        /// <summary>활성 상태</summary>
        public bool IsActive
        {
            get
            {
                if (mode == StreamingMode.Publisher)
                    return publisher != null && publisher.State == PublishState.Publishing;
                else
                    return subscriber != null && subscriber.State == SubscribeState.Subscribed;
            }
        }

        /// <summary>Room Manager</summary>
        public LiveKitRoomManager RoomManager => roomManager;

        /// <summary>Publisher</summary>
        public LiveKitVideoPublisher Publisher => publisher;

        /// <summary>Subscriber</summary>
        public LiveKitVideoSubscriber Subscriber => subscriber;

        /// <summary>연결 통계</summary>
        public ConnectionStats Stats => roomManager?.Stats ?? default;

        /// <summary>설정</summary>
        public LiveKitStreamingConfig Config => config;

        #endregion

        #region Events

        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action OnReconnecting;
        public event Action OnReconnected;
        public event Action OnStarted;
        public event Action OnStopped;
        public event Action<string> OnError;

        #endregion

        #region Private Fields

        private LiveKitRoomManager roomManager;
        private LiveKitVideoPublisher publisher;
        private LiveKitVideoSubscriber subscriber;
        private CancellationTokenSource streamingCts;
        private bool isInitialized;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            SetupComponents();
        }

        private IEnumerator Start()
        {
            if (autoStart && !string.IsNullOrEmpty(token))
            {
                yield return new WaitForSeconds(0.5f);
                yield return StartCoroutine(StartStreamingCoroutine());
            }
        }

        private void OnDestroy()
        {
            streamingCts?.Cancel();
            streamingCts?.Dispose();
            Stop();
        }

        #endregion

        #region Setup

        private void SetupComponents()
        {
            // Config가 없으면 기본값 생성
            if (config == null)
            {
                config = LiveKitStreamingConfig.CreateDefault();
            }

            // Room Manager 설정
            roomManager = gameObject.AddComponent<LiveKitRoomManager>();
            roomManager.Initialize(config);

            // URL 오버라이드 (Inspector에서 직접 설정한 경우)
            if (!string.IsNullOrEmpty(serverUrl) && serverUrl != "ws://localhost:7880")
            {
                roomManager.ServerUrl = serverUrl;
            }

            // 이벤트 연결
            roomManager.OnConnected += HandleConnected;
            roomManager.OnDisconnected += HandleDisconnected;
            roomManager.OnReconnecting += HandleReconnecting;
            roomManager.OnReconnected += HandleReconnected;
            roomManager.OnError += HandleError;

            // 모드에 따른 컴포넌트 설정
            if (mode == StreamingMode.Publisher)
            {
                SetupPublisher();
            }
            else
            {
                SetupSubscriber();
            }

            isInitialized = true;
            Log($"초기화 완료 (Mode: {mode})");
        }

        private void SetupPublisher()
        {
            publisher = gameObject.AddComponent<LiveKitVideoPublisher>();
            publisher.Initialize(roomManager, captureCamera ?? Camera.main, config.Video);
        }

        private void SetupSubscriber()
        {
            subscriber = gameObject.AddComponent<LiveKitVideoSubscriber>();
            subscriber.Initialize(roomManager, displayImage, config.AutoSubscribe);
        }

        #endregion

        #region Public API - Streaming (Coroutine)

        /// <summary>스트리밍 시작 (Coroutine)</summary>
        public IEnumerator StartStreamingCoroutine()
        {
            bool completed = false;
            bool success = false;
            string errorMessage = null;

            RunStreaming().Forget();
            async UniTaskVoid RunStreaming()
            {
                try
                {
                    success = await StartStreamingAsync();
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                }
                finally
                {
                    completed = true;
                }
            }

            while (!completed)
            {
                yield return null;
            }

            if (errorMessage != null)
            {
                LogError($"스트리밍 실패: {errorMessage}");
            }
        }

        /// <summary>스트리밍 시작 (간편)</summary>
        public void StartStreaming(Action<bool> callback = null)
        {
            StartStreamingAsync().ContinueWith(result =>
            {
                callback?.Invoke(result);
            }).Forget();
        }

        #endregion

        #region Public API - Streaming (UniTask)

        /// <summary>스트리밍 시작 (UniTask)</summary>
        public async UniTask<bool> StartStreamingAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(token))
            {
                LogError("토큰이 설정되지 않음");
                OnError?.Invoke("Token not set");
                return false;
            }

            streamingCts?.Cancel();
            streamingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            Log("스트리밍 시작 중...");

            try
            {
                // Room 연결
                var connected = await roomManager.ConnectAsync(token, streamingCts.Token);

                if (!connected)
                {
                    LogError("Room 연결 실패");
                    return false;
                }

                // 모드에 따른 시작
                if (mode == StreamingMode.Publisher)
                {
                    var published = await publisher.StartPublishingAsync(streamingCts.Token);

                    if (published)
                    {
                        await UniTask.SwitchToMainThread();
                        OnStarted?.Invoke();
                        return true;
                    }

                    return false;
                }
                else
                {
                    // Subscriber는 자동 구독 대기 또는 수동 구독
                    await UniTask.SwitchToMainThread();
                    OnStarted?.Invoke();
                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                Log("스트리밍 시작 취소됨");
                return false;
            }
            catch (Exception e)
            {
                LogError($"스트리밍 시작 예외: {e.Message}");
                OnError?.Invoke(e.Message);
                return false;
            }
        }

        /// <summary>중지</summary>
        public void Stop()
        {
            Log("스트리밍 중지 중...");

            streamingCts?.Cancel();

            if (mode == StreamingMode.Publisher)
            {
                publisher?.StopPublishing();
            }
            else
            {
                subscriber?.Unsubscribe();
            }

            roomManager?.Disconnect();

            UnityMainThreadDispatcher.Enqueue(() => OnStopped?.Invoke());
        }

        #endregion

        #region Public API - Settings

        /// <summary>토큰 설정</summary>
        public void SetToken(string newToken)
        {
            token = newToken;
        }

        /// <summary>서버 URL 설정</summary>
        public void SetServerUrl(string url)
        {
            serverUrl = url;
            if (roomManager != null)
            {
                roomManager.ServerUrl = url;
            }
        }

        /// <summary>모드 변경 (재초기화 필요)</summary>
        public void SetMode(StreamingMode newMode)
        {
            if (IsActive || IsConnected)
            {
                LogError("활성 상태에서는 모드를 변경할 수 없습니다. 먼저 Stop()을 호출하세요.");
                return;
            }

            if (mode == newMode) return;

            mode = newMode;

            // 기존 컴포넌트 제거
            if (publisher != null)
            {
                Destroy(publisher);
                publisher = null;
            }
            if (subscriber != null)
            {
                Destroy(subscriber);
                subscriber = null;
            }

            // 새 컴포넌트 생성
            if (mode == StreamingMode.Publisher)
            {
                SetupPublisher();
            }
            else
            {
                SetupSubscriber();
            }

            Log($"모드 변경됨: {mode}");
        }

        /// <summary>Config로 재설정</summary>
        public void ApplyConfig(LiveKitStreamingConfig newConfig)
        {
            if (IsActive)
            {
                LogError("활성 상태에서는 설정을 변경할 수 없습니다");
                return;
            }

            config = newConfig;
            roomManager?.Initialize(config);

            if (publisher != null)
            {
                publisher.Initialize(roomManager, captureCamera ?? Camera.main, config.Video);
            }
            if (subscriber != null)
            {
                subscriber.Initialize(roomManager, displayImage, config.AutoSubscribe);
            }

            Log("Config 적용됨");
        }

        #endregion

        #region Event Handlers

        private void HandleConnected()
        {
            Log("Room 연결됨");
            OnConnected?.Invoke();
        }

        private void HandleDisconnected()
        {
            Log("Room 연결 해제됨");
            OnDisconnected?.Invoke();
        }

        private void HandleReconnecting()
        {
            Log("재연결 중...");
            OnReconnecting?.Invoke();
        }

        private void HandleReconnected()
        {
            Log("재연결 성공");
            OnReconnected?.Invoke();
        }

        private void HandleError(string message)
        {
            OnError?.Invoke(message);
        }

        #endregion

        #region Debug GUI

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void OnGUI()
        {
            if (config != null && !config.ShowDebugGUI) return;

            GUILayout.BeginArea(new Rect(10, 220, 600, 180));
            GUI.Box(new Rect(0, 0, 600, 180), "");

            GUILayout.Label("<b>LiveKit Streaming Manager (SFU)</b>");
            GUILayout.Space(5);

            GUILayout.BeginHorizontal();

            // 상태
            GUILayout.BeginVertical(GUILayout.Width(280));
            GUILayout.Label($"Mode: {mode}");
            GUILayout.Label($"Server: {roomManager?.ServerUrl ?? serverUrl}");
            GUILayout.Label($"Connected: {IsConnected}");
            GUILayout.Label($"Reconnecting: {IsReconnecting}");
            GUILayout.Label($"Active: {IsActive}");

            if (mode == StreamingMode.Publisher && publisher != null)
            {
                GUILayout.Label($"Frames Sent: {publisher.FramesSent:N0}");
                GUILayout.Label($"State: {publisher.State}");
            }
            else if (subscriber != null)
            {
                GUILayout.Label($"Frames Received: {subscriber.FramesReceived:N0}");
                GUILayout.Label($"State: {subscriber.State}");
            }
            GUILayout.EndVertical();

            // 컨트롤
            GUILayout.BeginVertical(GUILayout.Width(280));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Start", GUILayout.Height(30)))
            {
                StartStreaming();
            }
            if (GUILayout.Button("Stop", GUILayout.Height(30)))
            {
                Stop();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            // 토큰 입력
            GUILayout.Label("Token:");
            token = GUILayout.TextField(token, GUILayout.Width(250));

            // 통계
            if (IsConnected)
            {
                GUILayout.Space(5);
                GUILayout.Label($"Duration: {Stats.ConnectionDuration:F1}s");
                GUILayout.Label($"Reconnects: {Stats.ReconnectCount}");
            }

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }
#endif

        #endregion

        #region Helpers

        private void Log(string message)
        {
            if (config?.EnableLogging ?? true)
            {
                Debug.Log($"[LiveKitManager] {message}");
            }
        }

        private void LogError(string message)
        {
            Debug.LogError($"[LiveKitManager] {message}");
        }

        #endregion
    }

    #region Enums

    public enum StreamingMode
    {
        /// <summary>송신자 (비디오 발행)</summary>
        Publisher,

        /// <summary>수신자 (비디오 구독)</summary>
        Subscriber
    }

    #endregion
}
