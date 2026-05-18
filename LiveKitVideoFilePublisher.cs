using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Video;

#if LIVEKIT_SDK
using LiveKit;
#endif

namespace LiveKitStreaming
{
    /// <summary>
    /// LiveKit 비디오 파일 퍼블리셔
    /// VideoPlayer를 통해 동영상 파일을 RenderTexture로 렌더링 후 LiveKit으로 송출
    /// GPU 파이프라인으로 CPU readback 없이 최적 성능 제공
    /// </summary>
    public class LiveKitVideoFilePublisher : MonoBehaviour
    {
        #region Serialized Fields

        [Header("비디오 소스")]
        [SerializeField] private VideoPlayer videoPlayer;
        [SerializeField] private VideoClip videoClip;
        [SerializeField] private string videoUrl;

        [Header("출력 설정")]
        [SerializeField] private int outputWidth = 1280;
        [SerializeField] private int outputHeight = 720;

        [Header("인코딩 설정")]
        [SerializeField] private VideoCodecType videoCodec = VideoCodecType.Vp8;
        [SerializeField] private int maxBitrate = 2500000;
        [SerializeField] private bool simulcast = true;

        [Header("재생 설정")]
        [SerializeField] private bool loop = false;
        [SerializeField] private bool playOnStart = false;
        [SerializeField, Range(0.1f, 3f)] private float playbackSpeed = 1f;

        [Header("자막")]
        [SerializeField] private TextAsset subtitleFile;
        [SerializeField] private string subtitleTopic = "subtitle";

        [Header("Room Manager")]
        [SerializeField] private LiveKitRoomManager roomManager;

        [Header("디버그")]
        [SerializeField] private bool enableLogging = true;
        [SerializeField] private bool showDebugGUI = false;

        #endregion

        #region Public Properties

        /// <summary>발행 상태</summary>
        public VideoFilePublishState State { get; private set; } = VideoFilePublishState.Idle;

        /// <summary>현재 재생 시간 (초)</summary>
        public double CurrentTime => videoPlayer != null ? videoPlayer.time : 0;

        /// <summary>전체 길이 (초)</summary>
        public double Duration => videoPlayer != null ? videoPlayer.length : 0;

        /// <summary>재생 중 여부</summary>
        public bool IsPlaying => videoPlayer != null && videoPlayer.isPlaying;

        /// <summary>전송된 프레임 수</summary>
        public long FramesSent { get; private set; }

        /// <summary>현재 해상도</summary>
        public Vector2Int Resolution => new(outputWidth, outputHeight);

        /// <summary>VideoPlayer 접근</summary>
        public VideoPlayer Player => videoPlayer;

        #endregion

        #region Events

        public event Action OnPublishStarted;
        public event Action OnPublishStopped;
        public event Action OnVideoStarted;
        public event Action OnVideoEnded;
        public event Action<string> OnError;
        public event Action<SubtitleEntry> OnSubtitleChanged;

        #endregion

        #region Private Fields

#if LIVEKIT_SDK
        private LocalVideoTrack videoTrack;
        private TextureVideoSource videoSource;
#endif

        private RenderTexture renderTexture;
        private Coroutine updateCoroutine;
        private CancellationTokenSource publishCts;
        private SubtitleParser subtitleParser;
        private int currentSubtitleIndex = -1;
        private bool isInitialized;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            SetupVideoPlayer();

            if (roomManager == null)
            {
                roomManager = GetComponent<LiveKitRoomManager>();
            }
        }

        private void Start()
        {
            if (subtitleFile != null)
            {
                LoadSubtitles(subtitleFile.text);
            }
        }

        private void Update()
        {
            if (State == VideoFilePublishState.Publishing && videoPlayer.isPlaying)
            {
                FramesSent++;
                //UpdateSubtitle();
            }
        }

        private void OnDestroy()
        {
            publishCts?.Cancel();
            publishCts?.Dispose();
            StopPublishing();
            CleanupRenderTexture();
        }

        #endregion

        #region Setup

        private void SetupVideoPlayer()
        {
            if (videoPlayer == null)
            {
                videoPlayer = gameObject.AddComponent<VideoPlayer>();
            }

            videoPlayer.playOnAwake = false;
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.skipOnDrop = true;
            videoPlayer.isLooping = loop;
            videoPlayer.playbackSpeed = playbackSpeed;

            // 이벤트 연결
            videoPlayer.prepareCompleted += OnVideoPrepared;
            videoPlayer.started += OnVideoPlayerStarted;
            videoPlayer.loopPointReached += OnVideoLoopPointReached;
            videoPlayer.errorReceived += OnVideoError;
        }

        private void CreateRenderTexture()
        {
            CleanupRenderTexture();

            renderTexture = new RenderTexture(outputWidth, outputHeight, 0, RenderTextureFormat.ARGB32);
            renderTexture.Create();

            videoPlayer.targetTexture = renderTexture;

            Log($"RenderTexture 생성: {outputWidth}x{outputHeight}");
        }

        private void CleanupRenderTexture()
        {
            if (renderTexture != null)
            {
                if (videoPlayer != null)
                {
                    videoPlayer.targetTexture = null;
                }
                renderTexture.Release();
                Destroy(renderTexture);
                renderTexture = null;
            }
        }

        #endregion

        #region Public API - Initialize

        /// <summary>비디오 클립으로 초기화</summary>
        public void Initialize(LiveKitRoomManager room, VideoClip clip)
        {
            roomManager = room;
            videoClip = clip;
            videoUrl = null;
            videoPlayer.source = VideoSource.VideoClip;
            videoPlayer.clip = clip;
            isInitialized = true;

            Log($"VideoClip으로 초기화: {clip.name}");
        }

        /// <summary>URL로 초기화</summary>
        public void Initialize(LiveKitRoomManager room, string url)
        {
            roomManager = room;
            videoUrl = url;
            videoClip = null;
            videoPlayer.source = VideoSource.Url;
            videoPlayer.url = url;
            isInitialized = true;

            Log($"URL로 초기화: {url}");
        }

        /// <summary>자막 로드</summary>
        public void LoadSubtitles(string subtitleContent)
        {
            subtitleParser = new SubtitleParser();
            subtitleParser.Parse(subtitleContent);
            currentSubtitleIndex = -1;

            Log($"자막 로드 완료: {subtitleParser.Entries.Count}개 항목");
        }

        /// <summary>자막 파일 로드</summary>
        public void LoadSubtitlesFromFile(TextAsset file)
        {
            if (file != null)
            {
                LoadSubtitles(file.text);
            }
        }

        #endregion

        #region Public API - Publishing

        /// <summary>발행 시작 (Coroutine)</summary>
        public IEnumerator StartPublishingCoroutine()
        {
            bool completed = false;
            bool success = false;

            StartPublishingAsync().ContinueWith(result =>
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
                LogError("발행 시작 실패");
            }
        }

        /// <summary>발행 시작 (UniTask)</summary>
        public async UniTask<bool> StartPublishingAsync(CancellationToken ct = default)
        {
            if (State != VideoFilePublishState.Idle)
            {
                Log("이미 발행 중이거나 준비 중");
                return State == VideoFilePublishState.Publishing;
            }

#if LIVEKIT_SDK
            if (roomManager == null || !roomManager.IsConnected || roomManager.Room == null)
            {
                LogError("Room에 연결되지 않음");
                OnError?.Invoke("Room not connected");
                return false;
            }

            publishCts?.Cancel();
            publishCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            State = VideoFilePublishState.Preparing;

            try
            {
                // RenderTexture 생성
                CreateRenderTexture();

                // 비디오 소스 설정
                if (videoClip != null)
                {
                    videoPlayer.source = VideoSource.VideoClip;
                    videoPlayer.clip = videoClip;
                }
                else if (!string.IsNullOrEmpty(videoUrl))
                {
                    videoPlayer.source = VideoSource.Url;
                    videoPlayer.url = videoUrl;
                }
                else
                {
                    LogError("비디오 소스가 설정되지 않음");
                    OnError?.Invoke("No video source");
                    State = VideoFilePublishState.Idle;
                    return false;
                }

                // 비디오 준비
                videoPlayer.Prepare();

                // 준비 완료 대기
                while (!videoPlayer.isPrepared)
                {
                    publishCts.Token.ThrowIfCancellationRequested();
                    await UniTask.Yield();
                }

                // TextureVideoSource 생성
                videoSource = new TextureVideoSource(renderTexture);

                // LocalVideoTrack 생성
                videoTrack = LocalVideoTrack.CreateVideoTrack("video-file", videoSource, roomManager.Room);

                // 발행 옵션
                var options = new LiveKit.Proto.TrackPublishOptions
                {
                    VideoCodec = ConvertCodec(videoCodec),
                    Simulcast = simulcast,
                    Source = LiveKit.Proto.TrackSource.SourceCamera,
                    VideoEncoding = new LiveKit.Proto.VideoEncoding
                    {
                        MaxBitrate = (uint)maxBitrate,
                        MaxFramerate = (uint)Mathf.CeilToInt((float)videoPlayer.frameRate)
                    }
                };

                // 트랙 발행
                var publish = roomManager.Room.LocalParticipant.PublishTrack(videoTrack, options);

                bool publishComplete = false;
                StartCoroutine(WaitForPublish());
                IEnumerator WaitForPublish()
                {
                    yield return publish;
                    publishComplete = true;
                }

                while (!publishComplete)
                {
                    publishCts.Token.ThrowIfCancellationRequested();
                    await UniTask.Yield();
                }

                if (publish.IsError)
                {
                    LogError($"트랙 발행 실패: {publish.IsError}");
                    OnError?.Invoke(publish.IsError.ToString());
                    await CleanupAsync();
                    return false;
                }

                // VideoSource 시작
                videoSource.Start();
                updateCoroutine = StartCoroutine(videoSource.Update());

                State = VideoFilePublishState.Publishing;
                FramesSent = 0;

                Log($"발행 시작: {outputWidth}x{outputHeight}, {videoPlayer.frameRate}fps");

                await UniTask.SwitchToMainThread();
                OnPublishStarted?.Invoke();

                // 자동 재생
                if (playOnStart)
                {
                    Play();
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                Log("발행 취소됨");
                await CleanupAsync();
                return false;
            }
            catch (Exception e)
            {
                LogError($"발행 예외: {e.Message}");
                OnError?.Invoke(e.Message);
                await CleanupAsync();
                return false;
            }
#else
            LogError("LiveKit SDK가 설치되지 않음");
            OnError?.Invoke("LiveKit SDK not installed");
            return false;
#endif
        }

        /// <summary>발행 중지</summary>
        public void StopPublishing()
        {
            if (State == VideoFilePublishState.Idle)
            {
                return;
            }

            State = VideoFilePublishState.Stopping;
            publishCts?.Cancel();

            // 비디오 정지
            videoPlayer.Stop();

            // 업데이트 코루틴 중지
            if (updateCoroutine != null)
            {
                StopCoroutine(updateCoroutine);
                updateCoroutine = null;
            }

#if LIVEKIT_SDK
            // 트랙 발행 취소
            if (videoTrack != null && roomManager?.Room?.LocalParticipant != null)
            {
                try
                {
                    roomManager.Room.LocalParticipant.UnpublishTrack(videoTrack, true);
                }
                catch (Exception e)
                {
                    Log($"UnpublishTrack 예외 (무시): {e.Message}");
                }
            }

            videoSource?.Stop();
            videoSource = null;
            videoTrack = null;
#endif

            CleanupRenderTexture();

            State = VideoFilePublishState.Idle;
            Log($"발행 중지됨 (총 {FramesSent} 프레임 전송)");

            UnityMainThreadDispatcher.Enqueue(() => OnPublishStopped?.Invoke());
        }

        #endregion

        #region Public API - Playback Control

        /// <summary>재생</summary>
        public void Play()
        {
            if (State != VideoFilePublishState.Publishing)
            {
                LogError("발행 중이 아님 - 먼저 StartPublishingAsync 호출 필요");
                return;
            }

            videoPlayer.Play();
            Log("재생 시작");
        }

        /// <summary>일시정지</summary>
        public void Pause()
        {
            videoPlayer.Pause();
            Log("일시정지");
        }

        /// <summary>정지 (처음으로)</summary>
        public void Stop()
        {
            videoPlayer.Stop();
            videoPlayer.time = 0;
            currentSubtitleIndex = -1;
            Log("정지");
        }

        /// <summary>탐색</summary>
        public void Seek(double timeInSeconds)
        {
            videoPlayer.time = Mathf.Clamp((float)timeInSeconds, 0f, (float)videoPlayer.length);
            //UpdateSubtitleIndex();
            Log($"탐색: {timeInSeconds:F2}s");
        }

        /// <summary>재생 속도 설정</summary>
        public void SetPlaybackSpeed(float speed)
        {
            playbackSpeed = Mathf.Clamp(speed, 0.1f, 3f);
            videoPlayer.playbackSpeed = playbackSpeed;
            Log($"재생 속도: {playbackSpeed}x");
        }

        /// <summary>루프 설정</summary>
        public void SetLoop(bool enabled)
        {
            loop = enabled;
            videoPlayer.isLooping = enabled;
            Log($"루프: {(enabled ? "ON" : "OFF")}");
        }

        /// <summary>볼륨 설정</summary>
        public void SetVolume(float volume)
        {
            if (videoPlayer.audioTrackCount > 0)
            {
                videoPlayer.SetDirectAudioVolume(0, Mathf.Clamp01(volume));
            }
        }

        #endregion

//         #region Subtitle Handling

//         private void UpdateSubtitle()
//         {
//             if (subtitleParser == null || subtitleParser.Entries.Count == 0)
//             {
//                 return;
//             }

//             double currentTime = videoPlayer.time;
//             var entry = subtitleParser.GetEntryAtTime(currentTime);

//             if (entry != null)
//             {
//                 int newIndex = subtitleParser.Entries.IndexOf(entry);
//                 if (newIndex != currentSubtitleIndex)
//                 {
//                     currentSubtitleIndex = newIndex;
//                     SendSubtitle(entry);
//                 }
//             }
//             else if (currentSubtitleIndex >= 0)
//             {
//                 // 자막 없는 구간
//                 currentSubtitleIndex = -1;
//                 SendSubtitleClear();
//             }
//         }

//         private void UpdateSubtitleIndex()
//         {
//             if (subtitleParser == null)
//             {
//                 return;
//             }

//             double currentTime = videoPlayer.time;
//             var entry = subtitleParser.GetEntryAtTime(currentTime);
//             currentSubtitleIndex = entry != null ? subtitleParser.Entries.IndexOf(entry) : -1;
//         }

//         private void SendSubtitle(SubtitleEntry entry)
//         {
// #if LIVEKIT_SDK
//             if (roomManager?.Room?.LocalParticipant == null)
//             {
//                 return;
//             }

//             try
//             {
//                 var data = new SubtitleData
//                 {
//                     text = entry.Text,
//                     startTime = entry.StartTime,
//                     endTime = entry.EndTime,
//                     index = entry.Index
//                 };

//                 var json = JsonUtility.ToJson(data);
//                 var bytes = System.Text.Encoding.UTF8.GetBytes(json);

//                 // PublishData는 Coroutine 기반이므로 Fire-and-forget으로 시작
//                 StartCoroutine(PublishSubtitleData(bytes));

//                 OnSubtitleChanged?.Invoke(entry);
//                 Log($"자막 전송: [{entry.Index}] {entry.Text}");
//             }
//             catch (Exception e)
//             {
//                 LogError($"자막 전송 실패: {e.Message}");
//             }
// #endif
//         }

//         private void SendSubtitleClear()
//         {
// #if LIVEKIT_SDK
//             if (roomManager?.Room?.LocalParticipant == null)
//             {
//                 return;
//             }

//             try
//             {
//                 var data = new SubtitleData { text = "", startTime = 0, endTime = 0, index = -1 };
//                 var json = JsonUtility.ToJson(data);
//                 var bytes = System.Text.Encoding.UTF8.GetBytes(json);

//                 StartCoroutine(PublishSubtitleData(bytes));
//             }
//             catch (Exception e)
//             {
//                 LogError($"자막 클리어 전송 실패: {e.Message}");
//             }
// #endif
//         }

// #if LIVEKIT_SDK
//         private IEnumerator PublishSubtitleData(byte[] data)
//         {
//             var publishOp = roomManager.Room.LocalParticipant.PublishData(
//                 data,
//                 reliable: true,
//                 topic: subtitleTopic
//             );
//             yield return publishOp;
//         }
// #endif

//         #endregion

        #region VideoPlayer Events

        private void OnVideoPrepared(VideoPlayer vp)
        {
            Log($"비디오 준비 완료: {vp.width}x{vp.height}, {vp.frameRate}fps, {vp.length:F2}s");

            // 비디오 해상도가 설정과 다르면 조정 가능
            if (outputWidth == 0 || outputHeight == 0)
            {
                outputWidth = (int)vp.width;
                outputHeight = (int)vp.height;
            }
        }

        private void OnVideoPlayerStarted(VideoPlayer vp)
        {
            Log("비디오 재생 시작됨");
            OnVideoStarted?.Invoke();
        }

        private void OnVideoLoopPointReached(VideoPlayer vp)
        {
            Log("비디오 끝 도달");
            currentSubtitleIndex = -1;
            OnVideoEnded?.Invoke();
        }

        private void OnVideoError(VideoPlayer vp, string message)
        {
            LogError($"VideoPlayer 에러: {message}");
            OnError?.Invoke(message);
        }

        #endregion

        #region Internal

#if LIVEKIT_SDK
        private LiveKit.Proto.VideoCodec ConvertCodec(VideoCodecType codec)
        {
            return codec switch
            {
                VideoCodecType.Vp8 => LiveKit.Proto.VideoCodec.Vp8,
                VideoCodecType.Vp9 => LiveKit.Proto.VideoCodec.Vp9,
                VideoCodecType.H264 => LiveKit.Proto.VideoCodec.H264,
                VideoCodecType.Av1 => LiveKit.Proto.VideoCodec.Av1,
                _ => LiveKit.Proto.VideoCodec.Vp8
            };
        }

        private async UniTask CleanupAsync()
        {
            await UniTask.SwitchToMainThread();
            StopPublishing();
        }
#endif

        #endregion

        #region Debug GUI

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void OnGUI()
        {
            if (!showDebugGUI) return;

            GUILayout.BeginArea(new Rect(10, 410, 350, 250));
            GUI.Box(new Rect(0, 0, 350, 250), "");

            GUILayout.Label("<b>LiveKit Video File Publisher</b>");
            GUILayout.Space(5);

            GUILayout.Label($"State: {State}");
            GUILayout.Label($"Playing: {IsPlaying}");
            GUILayout.Label($"Time: {CurrentTime:F2} / {Duration:F2}s");
            GUILayout.Label($"Frames Sent: {FramesSent:N0}");
            GUILayout.Label($"Resolution: {outputWidth}x{outputHeight}");

            if (videoPlayer != null && videoPlayer.isPrepared)
            {
                GUILayout.Label($"FPS: {videoPlayer.frameRate:F1}");
            }

            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Play")) Play();
            if (GUILayout.Button("Pause")) Pause();
            if (GUILayout.Button("Stop")) Stop();
            GUILayout.EndHorizontal();

            // 시크바
            if (videoPlayer != null && videoPlayer.isPrepared)
            {
                GUILayout.Space(5);
                float newTime = GUILayout.HorizontalSlider((float)CurrentTime, 0f, (float)Duration);
                if (Mathf.Abs(newTime - (float)CurrentTime) > 0.5f)
                {
                    Seek(newTime);
                }
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
                Debug.Log($"[VideoFilePublisher] {message}");
            }
        }

        private void LogError(string message)
        {
            Debug.LogError($"[VideoFilePublisher] {message}");
        }

        #endregion
    }

    #region Enums & Data Structures

    public enum VideoFilePublishState
    {
        Idle,
        Preparing,
        Publishing,
        Stopping
    }

    [Serializable]
    public class SubtitleData
    {
        public string text;
        public double startTime;
        public double endTime;
        public int index;
    }

    #endregion
}
