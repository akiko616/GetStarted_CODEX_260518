using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;



#if LIVEKIT_SDK
//using Google.Protobuf.Collections;
using LiveKit;
#endif

namespace LiveKitStreaming
{
    /// <summary>
    /// LiveKit 비디오 퍼블리셔 (송신자)
    /// 실제 LiveKit Unity SDK API 사용
    /// SFU 서버로 한 번만 업로드, 서버가 N명에게 복제
    /// </summary>
    public class LiveKitVideoPublisher : MonoBehaviour
    {
        #region Serialized Fields

        [Header("캡처 설정")]
        [SerializeField] private Camera targetCamera;
        [SerializeField] private int captureWidth = 1280;
        [SerializeField] private int captureHeight = 720;
        [SerializeField] private int frameRate = 30;

        [Header("인코딩 설정")]
        [SerializeField] private VideoCodecType videoCodec = VideoCodecType.Vp8;
        [SerializeField] private int maxBitrate = 2500000;
        [SerializeField] private bool simulcast = true;

        [Header("Room Manager")]
        [SerializeField] private LiveKitRoomManager roomManager;

        [Header("디버그")]
        [SerializeField] private bool enableLogging = true;
        [SerializeField] private bool showDebugGUI = false;

        #endregion

        #region Public Properties

        /// <summary>발행 상태</summary>
        public PublishState State { get; private set; } = PublishState.Idle;

        /// <summary>전송된 프레임 수 (실제 인코딩 프레임)</summary>
        public long FramesSent { get; private set; }

        /// <summary>현재 해상도</summary>
        public Vector2Int Resolution => new(captureWidth, captureHeight);

        /// <summary>현재 프레임 레이트</summary>
        public int CurrentFrameRate => frameRate;

        /// <summary>현재 비트레이트</summary>
        public int CurrentBitrate => maxBitrate;

        /// <summary>Simulcast 활성화 여부</summary>
        public bool IsSimulcastEnabled => simulcast;

        /// <summary>현재 코덱</summary>
        public VideoCodecType CurrentCodec => videoCodec;

        #endregion

        #region Events

        public event Action OnPublishStarted;
        public event Action OnPublishStopped;
        public event Action<string> OnError;
        public event Action<long> OnFrameSent;

        #endregion

        #region Private Fields

#if LIVEKIT_SDK
        private LocalVideoTrack videoTrack;
        private TextureVideoSource videoSource;
#endif

        private RenderTexture renderTexture;
        private Coroutine updateCoroutine;
        private CancellationTokenSource publishCts;
        private bool isInitialized;
        private RenderTexture originalCameraTarget;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (roomManager == null)
            {
                roomManager = GetComponent<LiveKitRoomManager>();
            }
        }

        private void OnDestroy()
        {
            publishCts?.Cancel();
            publishCts?.Dispose();
            publishCts=null;
            StopPublishing();
        }

        private void Update()
        {
            // 발행 중일 때 프레임 카운트 (SDK Update 코루틴과 동기화)
            if (State == PublishState.Publishing)
            {
                FramesSent++;
                OnFrameSent?.Invoke(FramesSent);
            }
        }

        #endregion

        #region Public API - Initialize

        /// <summary>Config로 초기화</summary>
        public void Initialize(LiveKitRoomManager room, LiveKitStreamingConfig config)
        {
            if (config == null)
            {
                LogError("Config가 null입니다");
                return;
            }

            roomManager = room;
            captureWidth = config.Video.Width;
            captureHeight = config.Video.Height;
            frameRate = config.Video.FrameRate;
            maxBitrate = config.Video.MaxBitrate;
            simulcast = config.Video.Simulcast;
            videoCodec = config.Video.Codec;
            enableLogging = config.EnableLogging;
            showDebugGUI = config.ShowDebugGUI;
            isInitialized = true;

            Log($"초기화 완료: {config.Video.ResolutionString}, {config.Video.BitrateString}");
        }

        /// <summary>직접 설정으로 초기화</summary>
        public void Initialize(LiveKitRoomManager room, Camera camera, VideoSettings settings)
        {
            roomManager = room;
            targetCamera = camera ?? Camera.main;
            captureWidth = settings.Width;
            captureHeight = settings.Height;
            frameRate = settings.FrameRate;
            maxBitrate = settings.MaxBitrate;
            simulcast = settings.Simulcast;
            videoCodec = settings.Codec;
            isInitialized = true;

            Log($"초기화 완료: {settings.ResolutionString}");
        }

        /// <summary>카메라 설정</summary>
        public void SetCamera(Camera camera)
        {
            if (State == PublishState.Publishing)
            {
                LogError("발행 중에는 카메라를 변경할 수 없습니다");
                return;
            }
            targetCamera = camera;
        }

        #endregion

        #region Public API - Publishing (Coroutine)

        /// <summary>비디오 발행 시작 (Coroutine)</summary>
        public IEnumerator StartPublishingCoroutine()
        {
            bool completed = false;
            bool success = false;
            string errorMessage = null;

            RunPublish().Forget();
            async UniTaskVoid RunPublish()
            {
                try
                {
                    success = await StartPublishingAsync();
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
                LogError($"퍼블리싱 실패: {errorMessage}");
            }
        }

        /// <summary>MonoBehaviour에서 호출용</summary>
        public void StartPublishing(Action<bool> callback = null)
        {
            StartPublishingAsync().ContinueWith(result =>
            {
                callback?.Invoke(result);
            }).Forget();
        }

        #endregion

        #region Public API - Publishing (UniTask)

        /// <summary>비디오 발행 시작 (UniTask)</summary>
        public async UniTask<bool> StartPublishingAsync(CancellationToken ct = default)
        {
            if (State != PublishState.Idle)
            {
                Log("이미 발행 중이거나 시작 중");
                return State == PublishState.Publishing;
            }

#if LIVEKIT_SDK
            if (roomManager == null || !roomManager.IsConnected || roomManager.Room == null)
            {
                LogError("Room에 연결되지 않음");
                OnError?.Invoke("Room not connected");
                return false;
            }

            if (targetCamera == null)
            {
                LogError("타겟 카메라가 없음");
                OnError?.Invoke("Target camera is null");
                return false;
            }

            publishCts?.Cancel();
            publishCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            State = PublishState.Starting;

            try
            {
                // 기존 카메라 타겟 저장
                originalCameraTarget = targetCamera.targetTexture;

                // RenderTexture 생성
                CreateRenderTexture();

                // 카메라 타겟 설정
                targetCamera.targetTexture = renderTexture;

                // TextureVideoSource 생성
                videoSource = new TextureVideoSource(renderTexture);

                // LocalVideoTrack 생성
                videoTrack = LocalVideoTrack.CreateVideoTrack("unity-camera", videoSource, roomManager.Room);

                // 발행 옵션 설정
                var options = new LiveKit.Proto.TrackPublishOptions
                {
                    VideoCodec = ConvertCodec(videoCodec),
                    Simulcast = simulcast,
                    Source = LiveKit.Proto.TrackSource.SourceCamera,
                    VideoEncoding = new LiveKit.Proto.VideoEncoding
                    {
                        MaxBitrate = (uint)maxBitrate,
                        MaxFramerate = (uint)frameRate
                    }
                };

                // 트랙 발행 (SDK는 Coroutine 기반)
                var publish = roomManager.Room.LocalParticipant.PublishTrack(videoTrack, options);

                // YieldInstruction을 폴링하여 완료 대기
                bool publishComplete = false;
                Coroutine publishCoroutine = StartCoroutine(WaitForPublish());
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

                // VideoSource 시작 (SDK 문서 패턴)
                videoSource.Start();
                updateCoroutine = StartCoroutine(videoSource.Update());

                State = PublishState.Publishing;
                FramesSent = 0;

                Log($"비디오 발행 시작: {captureWidth}x{captureHeight}@{frameRate}fps, Codec: {videoCodec}, Simulcast: {simulcast}");

                await UniTask.SwitchToMainThread();
                OnPublishStarted?.Invoke();

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

        /// <summary>비디오 발행 중지</summary>
        public void StopPublishing()
        {
            if (State == PublishState.Idle)
            {
                return;
            }

            State = PublishState.Stopping;
            publishCts?.Cancel();

            // 업데이트 코루틴 중지
            if (updateCoroutine != null)
            {
                StopCoroutine(updateCoroutine);
                updateCoroutine = null;
            }

#if LIVEKIT_SDK
            // 트랙 발행 취소 (먼저!)
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

            // VideoSource 중지
            videoSource?.Stop();
            videoSource = null;

            // 트랙 정리
            videoTrack = null;
#endif

            // 카메라 타겟 복원
            if (targetCamera != null)
            {
                targetCamera.targetTexture = originalCameraTarget;
            }

            // RenderTexture 정리
            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
                renderTexture = null;
            }

            State = PublishState.Idle;
            Log($"비디오 발행 중지됨 (총 {FramesSent} 프레임 전송)");

            UnityMainThreadDispatcher.Enqueue(() => OnPublishStopped?.Invoke());
        }

        #endregion

        #region Public API - Settings

        /// <summary>해상도 변경 (재발행 필요)</summary>
        public async UniTask SetResolutionAsync(int width, int height)
        {
            if (captureWidth == width && captureHeight == height)
            {
                return;
            }

            bool wasPublishing = State == PublishState.Publishing;
            if (wasPublishing)
            {
                StopPublishing();
            }

            captureWidth = width;
            captureHeight = height;

            Log($"해상도 변경: {width}x{height}");

            if (wasPublishing)
            {
                await StartPublishingAsync();
            }
        }

        /// <summary>비트레이트 변경</summary>
        public void SetBitrate(int bitrate)
        {
            maxBitrate = bitrate;
            Log($"비트레이트 변경: {bitrate / 1000} kbps");
            // 런타임 변경은 SDK에서 지원 시 추가
        }

        #endregion

        #region Internal

        private void CreateRenderTexture()
        {
            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
            }

            renderTexture = new RenderTexture(captureWidth, captureHeight, 24, RenderTextureFormat.ARGB32);
            renderTexture.Create();

            Log($"RenderTexture 생성: {captureWidth}x{captureHeight}");
        }

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

            GUILayout.BeginArea(new Rect(10, 10, 300, 200));
            GUI.Box(new Rect(0, 0, 300, 200), "");

            GUILayout.Label("<b>LiveKit Video Publisher (SFU)</b>");
            GUILayout.Space(5);

            GUILayout.Label($"State: {State}");
            GUILayout.Label($"Resolution: {captureWidth}x{captureHeight}@{frameRate}fps");
            GUILayout.Label($"Codec: {videoCodec}");
            GUILayout.Label($"Max Bitrate: {maxBitrate / 1000} kbps");
            GUILayout.Label($"Simulcast: {(simulcast ? "ON" : "OFF")}");
            GUILayout.Label($"Frames Sent: {FramesSent:N0}");

            GUILayout.Space(10);

            if (State == PublishState.Idle)
            {
                if (GUILayout.Button("Start Publishing"))
                {
                    StartPublishing();
                }
            }
            else if (State == PublishState.Publishing)
            {
                if (GUILayout.Button("Stop Publishing"))
                {
                    StopPublishing();
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
                Debug.Log($"[LiveKitPublisher] {message}");
            }
        }

        private void LogError(string message)
        {
            Debug.LogError($"[LiveKitPublisher] {message}");
        }

        #endregion
    }

    #region Enums

    public enum PublishState
    {
        Idle,
        Starting,
        Publishing,
        Stopping
    }

    public enum VideoCodecType
    {
        Vp8,
        Vp9,
        H264,
        Av1
    }

    #endregion
}
