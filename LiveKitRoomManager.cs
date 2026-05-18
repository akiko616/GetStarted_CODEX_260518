using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

#if LIVEKIT_SDK
using LiveKit;
#endif

namespace LiveKitStreaming
{
    /// <summary>
    /// LiveKit Room 관리자
    /// 실제 LiveKit Unity SDK (client-sdk-unity) 래핑
    /// </summary>
    public class LiveKitRoomManager : MonoBehaviour
    {
        #region Serialized Fields

        [Header("설정")]
        [SerializeField] private LiveKitStreamingConfig config;

        [Header("서버 설정 (Config 없을 때 사용)")]
        [SerializeField] private string serverUrl = "ws://192.168.0.228:7880";

        [Header("디버그")]
        [SerializeField] private bool enableLogging = true;

        #endregion

        #region Public Properties

        /// <summary>서버 URL</summary>
        public string ServerUrl
        {
            get => config != null ? config.ServerUrl : serverUrl;
            set => serverUrl = value;
        }

        /// <summary>연결 상태</summary>
        public bool IsConnected { get; private set; }

        /// <summary>재연결 중 여부</summary>
        public bool IsReconnecting { get; private set; }

        /// <summary>Room 이름</summary>
        public string RoomName { get; private set; }

        /// <summary>로컬 참가자 ID</summary>
        public string LocalParticipantSid { get; private set; }

        /// <summary>연결 통계</summary>
        public ConnectionStats Stats => stats;

        /// <summary>설정</summary>
        public LiveKitStreamingConfig Config => config;

#if LIVEKIT_SDK
        /// <summary>LiveKit Room 인스턴스 (외부 접근용)</summary>
        public Room Room { get; private set; }
#endif

        #endregion

        #region Events

        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action OnReconnecting;
        public event Action OnReconnected;
        public event Action<string> OnError;
        public event Action<string, string> OnParticipantConnected;
        public event Action<string> OnParticipantDisconnected;
        public event Action<RemoteTrackInfo> OnTrackSubscribed;
        public event Action<string> OnTrackUnsubscribed;

        #endregion

        #region Private Fields

        private readonly Dictionary<string, RemoteParticipantData> remoteParticipants = new();
        private ConnectionStats stats;
        private CancellationTokenSource connectionCts;
        private int reconnectAttempts;
        private float connectionStartTime;
        private bool isDisposed;

        #endregion

        #region Unity Lifecycle

        private void OnDestroy()
        {
            isDisposed = true;
            connectionCts?.Cancel();
            connectionCts?.Dispose();
            connectionCts = null;  // Dispose 후 null 처리
            Disconnect();
        }

        private void Update()
        {
            if (IsConnected)
            {
                stats.ConnectionDuration = Time.time - connectionStartTime;
                stats.LastUpdateTime = Time.time;
            }
        }

        #endregion

        #region Public API - Initialize

        /// <summary>Config로 초기화</summary>
        public void Initialize(LiveKitStreamingConfig streamingConfig)
        {
            config = streamingConfig;
            enableLogging = config?.EnableLogging ?? true;
            Log("Config로 초기화됨");
        }

        /// <summary>URL로 직접 초기화</summary>
        public void Initialize(string url)
        {
            serverUrl = url;
            Log($"서버 URL 설정: {url}");
        }

        #endregion

        #region Public API - Connection (Coroutine)

        /// <summary>Room 연결 (Coroutine)</summary>
        public IEnumerator ConnectCoroutine(string token)
        {
            bool completed = false;
            bool success = false;
            string errorMessage = null;

            RunConnect().Forget();

            async UniTaskVoid RunConnect()
            {
                try
                {
                    success = await ConnectAsync(token);
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
                LogError($"연결 실패: {errorMessage}");
            }
        }

        /// <summary>MonoBehaviour에서 호출용</summary>
        public void Connect(string token, Action<bool> callback = null)
        {
            ConnectAsync(token).ContinueWith(result =>
            {
                callback?.Invoke(result);
            }).Forget();
        }

        #endregion

        #region Public API - Connection (UniTask)

        /// <summary>Room 연결 (UniTask)</summary>
        public async UniTask<bool> ConnectAsync(string token, CancellationToken ct = default)
        {
            if (IsConnected)
            {
                Log("이미 연결됨");
                return true;
            }

            if (string.IsNullOrEmpty(token))
            {
                LogError("토큰이 비어있음");
                OnError?.Invoke("Token is empty");
                return false;
            }

#if LIVEKIT_SDK
            connectionCts?.Cancel();
            connectionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var timeout = config?.ConnectionTimeout ?? 10f;
            var url = ServerUrl;

            Log($"Room 연결 시도: {url}");

            try
            {
                Room = new Room();
                SetupRoomEvents();

                // RoomOptions 설정
                var options = CreateRoomOptions();

                // Room 연결 (SDK는 Coroutine 기반)
                var connect = Room.Connect(url, token, options);

                // YieldInstruction을 폴링하여 완료 대기 (타임아웃 포함)
                bool connectComplete = false;
                Coroutine connectCoroutine = StartCoroutine(WaitForConnect());
                IEnumerator WaitForConnect()
                {
                    yield return connect;
                    connectComplete = true;
                }

                float startTime = Time.time;
                while (!connectComplete)
                {
                    connectionCts.Token.ThrowIfCancellationRequested();

                    // 타임아웃 체크
                    if (Time.time - startTime > timeout)
                    {
                        StopCoroutine(connectCoroutine);
                        LogError($"연결 타임아웃 ({timeout}초)");
                        OnError?.Invoke("Connection timeout");
                        CleanupRoom();
                        return false;
                    }

                    await UniTask.Yield();
                }

                if (connect.IsError)
                {
                    LogError($"연결 실패: {connect.IsError}");
                    OnError?.Invoke(connect.IsError.ToString());
                    CleanupRoom();
                    return false;
                }

                IsConnected = true;
                RoomName = Room.Name;
                LocalParticipantSid = Room.LocalParticipant?.Sid;
                connectionStartTime = Time.time;
                reconnectAttempts = 0;
                stats.Reset();

                Log($"Room 연결 성공: {RoomName}, SID: {LocalParticipantSid}");

                // 메인 스레드에서 이벤트 호출
                await UniTask.SwitchToMainThread();
                OnConnected?.Invoke();

                return true;
            }
            catch (OperationCanceledException)
            {
                Log("연결 취소됨");
                CleanupRoom();
                return false;
            }
            catch (Exception e)
            {
                LogError($"연결 예외: {e.Message}");
                OnError?.Invoke(e.Message);
                CleanupRoom();
                return false;
            }
#else
            LogError("LiveKit SDK가 설치되지 않음");
            OnError?.Invoke("LiveKit SDK not installed");
            return false;
#endif
        }

        /// <summary>연결 해제</summary>
        public void Disconnect()
        {
#if LIVEKIT_SDK
            if (!IsConnected && Room == null)
            {
                return;
            }
#else
            if (!IsConnected)
            {
                return;
            }
#endif

            connectionCts?.Cancel();
            CleanupRoom();

            remoteParticipants.Clear();
            IsConnected = false;
            IsReconnecting = false;
            RoomName = null;
            LocalParticipantSid = null;

            Log("Room 연결 해제됨");

            // 메인 스레드 확인
            if (UnityMainThreadDispatcher.IsMainThread)
            {
                OnDisconnected?.Invoke();
            }
            else
            {
                UnityMainThreadDispatcher.Enqueue(() => OnDisconnected?.Invoke());
            }
        }

        #endregion

        #region Reconnection

        private async UniTaskVoid AttemptReconnect(string lastToken)
        {
            if (isDisposed || !Application.isPlaying)
            {
                return;
            }

            var maxAttempts = config?.MaxReconnectAttempts ?? 5;
            var delay = config?.ReconnectDelay ?? 2f;
            var autoReconnect = config?.AutoReconnect ?? true;

            if (!autoReconnect || reconnectAttempts >= maxAttempts)
            {
                LogError($"재연결 포기 (시도: {reconnectAttempts}/{maxAttempts})");
                OnError?.Invoke("Max reconnect attempts reached");
                return;
            }

            IsReconnecting = true;
            reconnectAttempts++;
            stats.ReconnectCount = reconnectAttempts;

            Log($"재연결 시도 {reconnectAttempts}/{maxAttempts} ({delay}초 후)");
            OnReconnecting?.Invoke();

            await UniTask.Delay(TimeSpan.FromSeconds(delay));

            if (isDisposed || !Application.isPlaying)
            {
                return;
            }

            var success = await ConnectAsync(lastToken);

            if (success)
            {
                IsReconnecting = false;
                Log("재연결 성공");
                OnReconnected?.Invoke();
            }
            else
            {
                // 재귀적으로 다시 시도
                AttemptReconnect(lastToken).Forget();
            }
        }

        #endregion

        #region Room Events

#if LIVEKIT_SDK
        private RoomOptions CreateRoomOptions()
        {
            return new RoomOptions
            {
                AutoSubscribe = config?.AutoSubscribe ?? true,
                Dynacast = config?.Dynacast ?? true,
                AdaptiveStream = config?.AdaptiveStream ?? true
            };
        }

        private void SetupRoomEvents()
        {
            if (Room == null) return;

            Room.ParticipantConnected += HandleParticipantConnected;
            Room.ParticipantDisconnected += HandleParticipantDisconnected;
            Room.TrackSubscribed += HandleTrackSubscribed;
            Room.TrackUnsubscribed += HandleTrackUnsubscribed;
            Room.Disconnected += HandleDisconnected;
            Room.Reconnecting += HandleReconnecting;
            Room.Reconnected += HandleReconnected;
        }

        private void CleanupRoomEvents()
        {
            if (Room == null) return;

            Room.ParticipantConnected -= HandleParticipantConnected;
            Room.ParticipantDisconnected -= HandleParticipantDisconnected;
            Room.TrackSubscribed -= HandleTrackSubscribed;
            Room.TrackUnsubscribed -= HandleTrackUnsubscribed;
            Room.Disconnected -= HandleDisconnected;
            Room.Reconnecting -= HandleReconnecting;
            Room.Reconnected -= HandleReconnected;
        }

        private void HandleParticipantConnected(Participant participant)
        {
            var data = new RemoteParticipantData
            {
                Sid = participant.Sid,
                Identity = participant.Identity,
                Name = participant.Name
            };
            remoteParticipants[participant.Sid] = data;

            Log($"참가자 연결됨: {participant.Identity} ({participant.Sid})");
            DispatchToMainThread(() => OnParticipantConnected?.Invoke(participant.Sid, participant.Identity));
        }

        private void HandleParticipantDisconnected(Participant participant)
        {
            remoteParticipants.Remove(participant.Sid);
            Log($"참가자 연결 해제됨: {participant.Identity}");
            DispatchToMainThread(() => OnParticipantDisconnected?.Invoke(participant.Sid));
        }

        private void HandleTrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
        {
            var info = new RemoteTrackInfo
            {
                TrackSid = track.Sid,
                ParticipantSid = participant.Sid,
                ParticipantIdentity = participant.Identity,
                IsVideo = track is RemoteVideoTrack,
                Track = track
            };

            Log($"트랙 구독됨: {track.Sid} from {participant.Identity}");
            DispatchToMainThread(() => OnTrackSubscribed?.Invoke(info));
        }

        private void HandleTrackUnsubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
        {
            Log($"트랙 구독 해제됨: {track.Sid}");
            DispatchToMainThread(() => OnTrackUnsubscribed?.Invoke(track.Sid));
        }

        private void HandleDisconnected(Room room)
        {
            Log("Room 연결 끊김");
            var wasConnected = IsConnected;
            IsConnected = false;

            DispatchToMainThread(() => OnDisconnected?.Invoke());

            // 자동 재연결 시도
            if (wasConnected && (config?.AutoReconnect ?? true))
            {
                // 토큰 저장 필요 - 현재는 재연결 시 새 토큰 필요
                Log("자동 재연결은 새 토큰이 필요합니다");
            }
        }

        private void HandleReconnecting(Room room)
        {
            Log("재연결 중...");
            IsReconnecting = true;
            DispatchToMainThread(() => OnReconnecting?.Invoke());
        }

        private void HandleReconnected(Room room)
        {
            Log("재연결 성공 (SDK 내부)");
            IsReconnecting = false;
            stats.ReconnectCount++;
            DispatchToMainThread(() => OnReconnected?.Invoke());
        }

        private void CleanupRoom()
        {
            CleanupRoomEvents();
            Room?.Disconnect();
            
            Room = null;
        }
#else
        private void CleanupRoom() { }
#endif

        #endregion

        #region Helpers

        private void DispatchToMainThread(Action action)
        {
            if (action == null) return;

            if (UnityMainThreadDispatcher.IsMainThread)
            {
                action();
            }
            else
            {
                UnityMainThreadDispatcher.Enqueue(action);
            }
        }

        private void Log(string message)
        {
            if (enableLogging || (config?.EnableLogging ?? false))
            {
                Debug.Log($"[LiveKitRoom] {message}");
            }
        }

        private void LogError(string message)
        {
            Debug.LogError($"[LiveKitRoom] {message}");
        }

        #endregion
    }

    #region Data Structures

    [Serializable]
    public class RemoteParticipantData
    {
        public string Sid;
        public string Identity;
        public string Name;
    }

    public class RemoteTrackInfo
    {
        public string TrackSid;
        public string ParticipantSid;
        public string ParticipantIdentity;
        public bool IsVideo;
#if LIVEKIT_SDK
        public IRemoteTrack Track;
#endif
    }

    #endregion

    #region Unity Main Thread Dispatcher

    /// <summary>
    /// 메인 스레드 디스패처 유틸리티
    /// </summary>
    public static class UnityMainThreadDispatcher
    {
        private static readonly Queue<Action> executionQueue = new();
        private static int mainThreadId;
        private static bool isInitialized;

        /// <summary>현재 메인 스레드인지 확인</summary>
        public static bool IsMainThread => System.Threading.Thread.CurrentThread.ManagedThreadId == mainThreadId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            isInitialized = true;

            // 업데이트 루프 등록
            var go = new GameObject("[MainThreadDispatcher]");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<MainThreadDispatcherBehaviour>();
        }

        /// <summary>메인 스레드에서 실행할 액션 추가</summary>
        public static void Enqueue(Action action)
        {
            if (action == null) return;

            if (!isInitialized)
            {
                // 초기화 전이면 직접 실행 (에디터 등)
                action();
                return;
            }

            lock (executionQueue)
            {
                executionQueue.Enqueue(action);
            }
        }

        internal static void ProcessQueue()
        {
            lock (executionQueue)
            {
                while (executionQueue.Count > 0)
                {
                    try
                    {
                        executionQueue.Dequeue()?.Invoke();
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
        }

        private class MainThreadDispatcherBehaviour : MonoBehaviour
        {
            private void Update()
            {
                ProcessQueue();
            }
        }
    }

    #endregion
}
