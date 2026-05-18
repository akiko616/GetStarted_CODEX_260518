using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Video;

namespace LiveKitStreaming
{
    /// <summary>
    /// 비디오 파일 스트리밍 통합 매니저
    /// 동영상 파일과 자막을 LiveKit으로 송출하는 올인원 솔루션
    /// </summary>
    public class VideoFileStreamingManager : MonoBehaviour
    {
        #region Serialized Fields

        [Header("=== 서버 설정 ===")]
        [SerializeField] private string serverUrl = "ws://localhost:7880";
        [SerializeField] private string token = "";

        [Header("=== 비디오 소스 ===")]
        [SerializeField] private VideoSourceType sourceType = VideoSourceType.VideoClip;
        [SerializeField] private VideoClip videoClip;
        [SerializeField] private string videoUrl;

        [Header("=== 자막 ===")]
        [SerializeField] private TextAsset subtitleFile;
        [SerializeField] private string subtitleTopic = "subtitle";

        [Header("=== 출력 설정 ===")]
        [SerializeField] private int outputWidth = 1280;
        [SerializeField] private int outputHeight = 720;
        [SerializeField] private VideoCodecType codec = VideoCodecType.Vp8;
        [SerializeField] private int maxBitrate = 2500000;
        [SerializeField] private bool simulcast = true;

        [Header("=== 재생 설정 ===")]
        [SerializeField] private bool autoStart = false;
        [SerializeField] private bool loop = false;
        [SerializeField, Range(0.1f, 3f)] private float playbackSpeed = 1f;

        [Header("=== 디버그 ===")]
        [SerializeField] private bool enableLogging = true;
        [SerializeField] private bool showDebugGUI = false;

        #endregion

        #region Public Properties

        /// <summary>연결 상태</summary>
        public bool IsConnected => roomManager != null && roomManager.IsConnected;

        /// <summary>발행 상태</summary>
        public bool IsPublishing => filePublisher != null && filePublisher.State == VideoFilePublishState.Publishing;

        /// <summary>재생 중 여부</summary>
        public bool IsPlaying => filePublisher != null && filePublisher.IsPlaying;

        /// <summary>현재 재생 시간</summary>
        public double CurrentTime => filePublisher?.CurrentTime ?? 0;

        /// <summary>전체 길이</summary>
        public double Duration => filePublisher?.Duration ?? 0;

        /// <summary>Room Manager 접근</summary>
        public LiveKitRoomManager RoomManager => roomManager;

        /// <summary>File Publisher 접근</summary>
        public LiveKitVideoFilePublisher FilePublisher => filePublisher;

        #endregion

        #region Events

        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action OnStreamingStarted;
        public event Action OnStreamingStopped;
        public event Action OnVideoStarted;
        public event Action OnVideoEnded;
        public event Action<SubtitleEntry> OnSubtitleChanged;
        public event Action<string> OnError;

        #endregion

        #region Private Fields

        private LiveKitRoomManager roomManager;
        private LiveKitVideoFilePublisher filePublisher;
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
            StopStreaming();
        }

        #endregion

        #region Setup

        private void SetupComponents()
        {
            // Room Manager 생성
            roomManager = gameObject.AddComponent<LiveKitRoomManager>();
            roomManager.ServerUrl = serverUrl;

            // File Publisher 생성
            filePublisher = gameObject.AddComponent<LiveKitVideoFilePublisher>();

            // 이벤트 연결
            roomManager.OnConnected += HandleConnected;
            roomManager.OnDisconnected += HandleDisconnected;
            roomManager.OnError += HandleError;

            filePublisher.OnPublishStarted += HandlePublishStarted;
            filePublisher.OnPublishStopped += HandlePublishStopped;
            filePublisher.OnVideoStarted += HandleVideoStarted;
            filePublisher.OnVideoEnded += HandleVideoEnded;
            filePublisher.OnSubtitleChanged += HandleSubtitleChanged;
            filePublisher.OnError += HandleError;

            isInitialized = true;
            Log("컴포넌트 초기화 완료");
        }

        #endregion

        #region Public API - Streaming

        /// <summary>스트리밍 시작 (Coroutine)</summary>
        public IEnumerator StartStreamingCoroutine()
        {
            bool completed = false;
            bool success = false;

            StartStreamingAsync().ContinueWith(result =>
            {
                success = result;
                completed = true;
            }).Forget();

            while (!completed)
            {
                yield return null;
            }

            if (!success)
            {
                LogError("스트리밍 시작 실패");
            }
        }

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
                // 1. Room 연결
                var connected = await roomManager.ConnectAsync(token, streamingCts.Token);
                if (!connected)
                {
                    LogError("Room 연결 실패");
                    return false;
                }

                // 2. File Publisher 초기화
                if (sourceType == VideoSourceType.VideoClip && videoClip != null)
                {
                    filePublisher.Initialize(roomManager, videoClip);
                }
                else if (sourceType == VideoSourceType.Url && !string.IsNullOrEmpty(videoUrl))
                {
                    filePublisher.Initialize(roomManager, videoUrl);
                }
                else
                {
                    LogError("비디오 소스가 설정되지 않음");
                    OnError?.Invoke("No video source configured");
                    return false;
                }

                // 3. 자막 로드
                if (subtitleFile != null)
                {
                    filePublisher.LoadSubtitlesFromFile(subtitleFile);
                }

                // 4. 설정 적용
                filePublisher.SetLoop(loop);
                filePublisher.SetPlaybackSpeed(playbackSpeed);

                // 5. 발행 시작
                var published = await filePublisher.StartPublishingAsync(streamingCts.Token);
                if (!published)
                {
                    LogError("발행 시작 실패");
                    return false;
                }

                Log("스트리밍 시작 완료");
                return true;
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

        /// <summary>스트리밍 중지</summary>
        public void StopStreaming()
        {
            Log("스트리밍 중지 중...");

            streamingCts?.Cancel();
            filePublisher?.StopPublishing();
            roomManager?.Disconnect();

            UnityMainThreadDispatcher.Enqueue(() => OnStreamingStopped?.Invoke());
        }

        #endregion

        #region Public API - Playback Control

        /// <summary>재생</summary>
        public void Play()
        {
            filePublisher?.Play();
        }

        /// <summary>일시정지</summary>
        public void Pause()
        {
            filePublisher?.Pause();
        }

        /// <summary>정지</summary>
        public void Stop()
        {
            filePublisher?.Stop();
        }

        /// <summary>탐색</summary>
        public void Seek(double timeInSeconds)
        {
            filePublisher?.Seek(timeInSeconds);
        }

        /// <summary>재생 속도 설정</summary>
        public void SetPlaybackSpeed(float speed)
        {
            playbackSpeed = speed;
            filePublisher?.SetPlaybackSpeed(speed);
        }

        /// <summary>루프 설정</summary>
        public void SetLoop(bool enabled)
        {
            loop = enabled;
            filePublisher?.SetLoop(enabled);
        }

        /// <summary>볼륨 설정</summary>
        public void SetVolume(float volume)
        {
            filePublisher?.SetVolume(volume);
        }

        #endregion

        #region Public API - Configuration

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

        /// <summary>비디오 클립 설정</summary>
        public void SetVideoClip(VideoClip clip)
        {
            sourceType = VideoSourceType.VideoClip;
            videoClip = clip;
            videoUrl = null;
        }

        /// <summary>비디오 URL 설정</summary>
        public void SetVideoUrl(string url)
        {
            sourceType = VideoSourceType.Url;
            videoUrl = url;
            videoClip = null;
        }

        /// <summary>자막 파일 설정</summary>
        public void SetSubtitleFile(TextAsset file)
        {
            subtitleFile = file;
            if (filePublisher != null && file != null)
            {
                filePublisher.LoadSubtitlesFromFile(file);
            }
        }

        /// <summary>자막 내용 직접 설정</summary>
        public void SetSubtitleContent(string content)
        {
            filePublisher?.LoadSubtitles(content);
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

        private void HandlePublishStarted()
        {
            Log("발행 시작됨");
            OnStreamingStarted?.Invoke();
        }

        private void HandlePublishStopped()
        {
            Log("발행 중지됨");
            OnStreamingStopped?.Invoke();
        }

        private void HandleVideoStarted()
        {
            Log("비디오 재생 시작");
            OnVideoStarted?.Invoke();
        }

        private void HandleVideoEnded()
        {
            Log("비디오 재생 완료");
            OnVideoEnded?.Invoke();
        }

        private void HandleSubtitleChanged(SubtitleEntry entry)
        {
            OnSubtitleChanged?.Invoke(entry);
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
            if (!showDebugGUI) return;

            GUILayout.BeginArea(new Rect(10, 10, 400, 400));
            GUI.Box(new Rect(0, 0, 400, 400), "");

            GUILayout.Label("<b>Video File Streaming Manager</b>");
            GUILayout.Space(5);

            // 상태
            GUILayout.Label($"Connected: {IsConnected}");
            GUILayout.Label($"Publishing: {IsPublishing}");
            GUILayout.Label($"Playing: {IsPlaying}");
            GUILayout.Label($"Time: {CurrentTime:F2} / {Duration:F2}s");

            GUILayout.Space(10);

            // 토큰 입력
            GUILayout.Label("Token:");
            token = GUILayout.TextField(token, GUILayout.Width(380));

            GUILayout.Space(10);

            // 컨트롤
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Start Streaming", GUILayout.Height(30)))
            {
                StartStreamingAsync().Forget();
            }
            if (GUILayout.Button("Stop Streaming", GUILayout.Height(30)))
            {
                StopStreaming();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Play")) Play();
            if (GUILayout.Button("Pause")) Pause();
            if (GUILayout.Button("Stop")) Stop();
            GUILayout.EndHorizontal();

            // 시크바
            if (IsPublishing && Duration > 0)
            {
                GUILayout.Space(10);
                GUILayout.Label("Seek:");
                float newTime = GUILayout.HorizontalSlider((float)CurrentTime, 0f, (float)Duration);
                if (Mathf.Abs(newTime - (float)CurrentTime) > 0.5f)
                {
                    Seek(newTime);
                }
            }

            // 재생 속도
            GUILayout.Space(10);
            GUILayout.Label($"Speed: {playbackSpeed:F1}x");
            float newSpeed = GUILayout.HorizontalSlider(playbackSpeed, 0.1f, 3f);
            if (Mathf.Abs(newSpeed - playbackSpeed) > 0.05f)
            {
                SetPlaybackSpeed(newSpeed);
            }

            // 루프
            GUILayout.Space(5);
            bool newLoop = GUILayout.Toggle(loop, "Loop");
            if (newLoop != loop)
            {
                SetLoop(newLoop);
            }

            GUILayout.EndArea();
        }
#endif

        #endregion

        #region Helpers

        private void Log(string message)
        {
            if (enableLogging)
            {
                Debug.Log($"[VideoFileStreamingManager] {message}");
            }
        }

        private void LogError(string message)
        {
            Debug.LogError($"[VideoFileStreamingManager] {message}");
        }

        #endregion
    }

    #region Enums

    public enum VideoSourceType
    {
        VideoClip,
        Url
    }

    #endregion
}
