using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace LiveKitStreaming.Samples
{
    /// <summary>
    /// 비디오 파일 스트리밍 샘플
    /// VideoFileStreamingManager 사용 예제
    /// </summary>
    public class VideoFileStreamingSample : MonoBehaviour
    {
        #region Serialized Fields

        [Header("=== 스트리밍 매니저 ===")]
        [SerializeField] private VideoFileStreamingManager streamingManager;

        [Header("=== 비디오 소스 ===")]
        [SerializeField] private VideoClip sampleVideoClip;
        [SerializeField] private string sampleVideoUrl = "https://example.com/video.mp4";

        [Header("=== 자막 ===")]
        [SerializeField] private TextAsset sampleSubtitle;

        [Header("=== 서버 설정 ===")]
        [SerializeField] private string tokenServerUrl = "http://localhost:3000";
        [SerializeField] private string roomName = "video-room";
        [SerializeField] private string identity = "video-publisher";

        [Header("=== UI 참조 ===")]
        [SerializeField] private InputField tokenInput;
        [SerializeField] private Button startButton;
        [SerializeField] private Button stopButton;
        [SerializeField] private Button playButton;
        [SerializeField] private Button pauseButton;
        [SerializeField] private Slider seekSlider;
        [SerializeField] private Slider speedSlider;
        [SerializeField] private Toggle loopToggle;
        [SerializeField] private Text statusText;
        [SerializeField] private Text timeText;
        [SerializeField] private Text subtitleText;

        #endregion

        #region Private Fields

        private bool isUpdatingSlider;

        #endregion

        #region Unity Lifecycle

        private void Start()
        {
            SetupManager();
            SetupUI();
            BindEvents();
        }

        private void Update()
        {
            UpdateTimeDisplay();
            UpdateSeekSlider();
        }

        private void OnDestroy()
        {
            UnbindEvents();
        }

        #endregion

        #region Setup

        private void SetupManager()
        {
            if (streamingManager == null)
            {
                streamingManager = gameObject.AddComponent<VideoFileStreamingManager>();
            }

            // 비디오 소스 설정
            if (sampleVideoClip != null)
            {
                streamingManager.SetVideoClip(sampleVideoClip);
            }
            else if (!string.IsNullOrEmpty(sampleVideoUrl))
            {
                streamingManager.SetVideoUrl(sampleVideoUrl);
            }

            // 자막 설정
            if (sampleSubtitle != null)
            {
                streamingManager.SetSubtitleFile(sampleSubtitle);
            }
        }

        private void SetupUI()
        {
            // 버튼 이벤트
            if (startButton != null)
            {
                startButton.onClick.AddListener(OnStartClicked);
            }

            if (stopButton != null)
            {
                stopButton.onClick.AddListener(OnStopClicked);
            }

            if (playButton != null)
            {
                playButton.onClick.AddListener(OnPlayClicked);
            }

            if (pauseButton != null)
            {
                pauseButton.onClick.AddListener(OnPauseClicked);
            }

            // 슬라이더 이벤트
            if (seekSlider != null)
            {
                seekSlider.onValueChanged.AddListener(OnSeekChanged);
            }

            if (speedSlider != null)
            {
                speedSlider.minValue = 0.1f;
                speedSlider.maxValue = 3f;
                speedSlider.value = 1f;
                speedSlider.onValueChanged.AddListener(OnSpeedChanged);
            }

            // 토글 이벤트
            if (loopToggle != null)
            {
                loopToggle.onValueChanged.AddListener(OnLoopChanged);
            }

            UpdateStatus("준비됨");
        }

        private void BindEvents()
        {
            streamingManager.OnConnected += () => UpdateStatus("연결됨");
            streamingManager.OnDisconnected += () => UpdateStatus("연결 해제됨");
            streamingManager.OnStreamingStarted += () => UpdateStatus("스트리밍 중");
            streamingManager.OnStreamingStopped += () => UpdateStatus("중지됨");
            streamingManager.OnVideoStarted += () => Debug.Log("[Sample] 비디오 재생 시작");
            streamingManager.OnVideoEnded += () => Debug.Log("[Sample] 비디오 재생 완료");
            streamingManager.OnSubtitleChanged += OnSubtitleReceived;
            streamingManager.OnError += OnErrorReceived;
        }

        private void UnbindEvents()
        {
            // 이벤트 해제는 Manager OnDestroy에서 처리
        }

        #endregion

        #region UI Event Handlers

        private void OnStartClicked()
        {
            // 토큰 설정
            string token = tokenInput != null ? tokenInput.text : "";
            if (!string.IsNullOrEmpty(token))
            {
                streamingManager.SetToken(token);
            }

            streamingManager.StartStreamingAsync().Forget();
            UpdateStatus("연결 중...");
        }

        private void OnStopClicked()
        {
            streamingManager.StopStreaming();
        }

        private void OnPlayClicked()
        {
            streamingManager.Play();
        }

        private void OnPauseClicked()
        {
            streamingManager.Pause();
        }

        private void OnSeekChanged(float value)
        {
            if (isUpdatingSlider) return;

            if (streamingManager.Duration > 0)
            {
                double seekTime = value * streamingManager.Duration;
                streamingManager.Seek(seekTime);
            }
        }

        private void OnSpeedChanged(float value)
        {
            streamingManager.SetPlaybackSpeed(value);
        }

        private void OnLoopChanged(bool value)
        {
            streamingManager.SetLoop(value);
        }

        #endregion

        #region Manager Event Handlers

        private void OnSubtitleReceived(SubtitleEntry entry)
        {
            if (subtitleText != null)
            {
                subtitleText.text = entry.Text;
            }
        }

        private void OnErrorReceived(string error)
        {
            UpdateStatus($"에러: {error}");
            Debug.LogError($"[Sample] 에러: {error}");
        }

        #endregion

        #region UI Updates

        private void UpdateStatus(string status)
        {
            if (statusText != null)
            {
                statusText.text = status;
            }
        }

        private void UpdateTimeDisplay()
        {
            if (timeText != null && streamingManager != null)
            {
                var current = TimeSpan.FromSeconds(streamingManager.CurrentTime);
                var total = TimeSpan.FromSeconds(streamingManager.Duration);
                timeText.text = $"{current:mm\\:ss} / {total:mm\\:ss}";
            }
        }

        private void UpdateSeekSlider()
        {
            if (seekSlider != null && streamingManager != null && streamingManager.Duration > 0)
            {
                isUpdatingSlider = true;
                seekSlider.value = (float)(streamingManager.CurrentTime / streamingManager.Duration);
                isUpdatingSlider = false;
            }
        }

        #endregion

        #region Public API - Token Server Integration

        /// <summary>토큰 서버에서 토큰 가져오기 (예제)</summary>
        public async void FetchTokenFromServer()
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                var url = $"{tokenServerUrl}/token/publisher/{roomName}/{identity}";
                var response = await client.GetStringAsync(url);

                // JSON 파싱 (간단한 예제)
                var tokenData = JsonUtility.FromJson<TokenResponse>(response);

                if (!string.IsNullOrEmpty(tokenData.token))
                {
                    streamingManager.SetToken(tokenData.token);

                    if (tokenInput != null)
                    {
                        tokenInput.text = tokenData.token;
                    }

                    Debug.Log("[Sample] 토큰 가져오기 성공");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Sample] 토큰 가져오기 실패: {e.Message}");
            }
        }

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
