using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

#if LIVEKIT_SDK
using LiveKit;
#endif

namespace LiveKitStreaming
{
    /// <summary>
    /// LiveKit 비디오 구독자 (시청자)
    /// 실제 LiveKit Unity SDK API 사용
    /// SFU 서버에서 비디오 수신
    /// </summary>
    public class LiveKitVideoSubscriber : MonoBehaviour
    {
        #region Serialized Fields

        [Header("디스플레이")]
        [SerializeField] private RawImage displayImage;
        [SerializeField] private bool autoResize = true;

        [Header("Room Manager")]
        [SerializeField] private LiveKitRoomManager roomManager;

        [Header("자동 구독")]
        [SerializeField] private bool autoSubscribeFirst = true;

        [Header("디버그")]
        [SerializeField] private bool enableLogging = true;
        [SerializeField] private bool showDebugGUI = false;

        #endregion

        #region Public Properties

        /// <summary>구독 상태</summary>
        public SubscribeState State { get; private set; } = SubscribeState.Idle;

        /// <summary>현재 구독 중인 참가자 ID</summary>
        public string SubscribedParticipantSid { get; private set; }

        /// <summary>수신 해상도</summary>
        public Vector2Int Resolution { get; private set; }

        /// <summary>수신된 프레임 수</summary>
        public long FramesReceived { get; private set; }

        /// <summary>사용 가능한 비디오 트랙 목록</summary>
        public IReadOnlyList<AvailableVideoTrack> AvailableTracks => availableTracks;

        /// <summary>현재 텍스처</summary>
        public Texture CurrentTexture { get; private set; }

        #endregion

        #region Events

        public event Action OnSubscribed;
        public event Action OnUnsubscribed;
        public event Action<Texture> OnVideoFrame;
        public event Action<string> OnError;
        public event Action<AvailableVideoTrack> OnTrackAvailable;
        public event Action<string> OnTrackRemoved;

        #endregion

        #region Private Fields

#if LIVEKIT_SDK
        private VideoStream videoStream;
        private RemoteVideoTrack subscribedTrack;
#endif

        private readonly List<AvailableVideoTrack> availableTracks = new();
        private Coroutine updateCoroutine;
        private CancellationTokenSource subscribeCts;
        private bool isInitialized;
        private bool eventsRegistered;
        private Texture lastTexture;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (roomManager == null)
            {
                roomManager = GetComponent<LiveKitRoomManager>();
            }
        }

        private void Start()
        {
            RegisterRoomEvents();
        }

        private void OnDestroy()
        {
            subscribeCts?.Cancel();
            subscribeCts?.Dispose();
            UnregisterRoomEvents();
            Unsubscribe();
        }

        #endregion

        #region Public API - Initialize

        /// <summary>Config로 초기화</summary>
        public void Initialize(LiveKitRoomManager room, LiveKitStreamingConfig config)
        {
            roomManager = room;
            autoSubscribeFirst = config?.AutoSubscribe ?? true;
            enableLogging = config?.EnableLogging ?? true;
            showDebugGUI = config?.ShowDebugGUI ?? false;
            isInitialized = true;

            RegisterRoomEvents();
            Log("Config로 초기화됨");
        }

        /// <summary>직접 설정으로 초기화</summary>
        public void Initialize(LiveKitRoomManager room, RawImage display, bool autoSubscribe = true)
        {
            roomManager = room;
            displayImage = display;
            autoSubscribeFirst = autoSubscribe;
            isInitialized = true;

            RegisterRoomEvents();
            Log("직접 설정으로 초기화됨");
        }

        /// <summary>디스플레이 이미지 설정</summary>
        public void SetDisplayImage(RawImage image)
        {
            displayImage = image;

            // 현재 텍스처가 있으면 적용
            if (CurrentTexture != null && displayImage != null)
            {
                displayImage.texture = CurrentTexture;
            }
        }

        #endregion

        #region Room Events Registration

        private void RegisterRoomEvents()
        {
            if (eventsRegistered || roomManager == null) return;

            roomManager.OnTrackSubscribed += HandleTrackSubscribed;
            roomManager.OnTrackUnsubscribed += HandleTrackUnsubscribed;
            roomManager.OnParticipantDisconnected += HandleParticipantDisconnected;
            roomManager.OnDisconnected += HandleRoomDisconnected;
            eventsRegistered = true;

            Log("Room 이벤트 등록됨");
        }

        private void UnregisterRoomEvents()
        {
            if (!eventsRegistered || roomManager == null) return;

            roomManager.OnTrackSubscribed -= HandleTrackSubscribed;
            roomManager.OnTrackUnsubscribed -= HandleTrackUnsubscribed;
            roomManager.OnParticipantDisconnected -= HandleParticipantDisconnected;
            roomManager.OnDisconnected -= HandleRoomDisconnected;
            eventsRegistered = false;

            Log("Room 이벤트 해제됨");
        }

        #endregion

        #region Public API - Subscribe (Sync)

        /// <summary>특정 비디오 트랙 구독</summary>
        public void SubscribeToTrack(RemoteTrackInfo trackInfo)
        {
            SubscribeToTrackAsync(trackInfo).Forget();
        }

        /// <summary>첫 번째 사용 가능한 비디오 트랙 구독</summary>
        public bool SubscribeFirstAvailable()
        {
            if (availableTracks.Count == 0)
            {
                Log("사용 가능한 비디오 트랙 없음");
                return false;
            }

            var first = availableTracks[0];
            if (first.TrackInfo != null)
            {
                SubscribeToTrack(first.TrackInfo);
                return true;
            }

            return false;
        }

        /// <summary>특정 참가자의 트랙 구독</summary>
        public bool SubscribeToParticipant(string participantSid)
        {
            var track = availableTracks.Find(t => t.ParticipantSid == participantSid);
            if (track?.TrackInfo != null)
            {
                SubscribeToTrack(track.TrackInfo);
                return true;
            }

            LogError($"참가자 {participantSid}의 트랙을 찾을 수 없음");
            return false;
        }

        #endregion

        #region Public API - Subscribe (UniTask)

        /// <summary>특정 비디오 트랙 구독 (UniTask)</summary>
        public async UniTask<bool> SubscribeToTrackAsync(RemoteTrackInfo trackInfo, CancellationToken ct = default)
        {
            if (trackInfo == null || !trackInfo.IsVideo)
            {
                LogError("유효하지 않은 비디오 트랙");
                OnError?.Invoke("Invalid video track");
                return false;
            }

#if LIVEKIT_SDK
            // 기존 구독 해제
            if (State == SubscribeState.Subscribed)
            {
                Unsubscribe();
            }

            subscribeCts?.Cancel();
            subscribeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            State = SubscribeState.Subscribing;

            try
            {
                var remoteTrack = trackInfo.Track as RemoteVideoTrack;
                if (remoteTrack == null)
                {
                    LogError("RemoteVideoTrack으로 캐스팅 실패");
                    State = SubscribeState.Idle;
                    OnError?.Invoke("Failed to cast to RemoteVideoTrack");
                    return false;
                }

                subscribedTrack = remoteTrack;
                SubscribedParticipantSid = trackInfo.ParticipantSid;

                // VideoStream 생성 및 시작 (SDK 문서 패턴)
                videoStream = new VideoStream(remoteTrack);
                videoStream.TextureReceived += HandleTextureReceived;
                videoStream.Start();
                updateCoroutine = StartCoroutine(videoStream.Update());

                State = SubscribeState.Subscribed;
                FramesReceived = 0;

                Log($"비디오 구독 시작: {trackInfo.ParticipantIdentity}");

                await UniTask.SwitchToMainThread();
                OnSubscribed?.Invoke();

                return true;
            }
            catch (OperationCanceledException)
            {
                Log("구독 취소됨");
                State = SubscribeState.Idle;
                return false;
            }
            catch (Exception e)
            {
                LogError($"구독 예외: {e.Message}");
                OnError?.Invoke(e.Message);
                State = SubscribeState.Idle;
                return false;
            }
#else
            LogError("LiveKit SDK가 설치되지 않음");
            OnError?.Invoke("LiveKit SDK not installed");
            return false;
#endif
        }

        /// <summary>구독 해제</summary>
        public void Unsubscribe()
        {
            if (State == SubscribeState.Idle)
            {
                return;
            }

            subscribeCts?.Cancel();

            // 업데이트 코루틴 중지
            if (updateCoroutine != null)
            {
                StopCoroutine(updateCoroutine);
                updateCoroutine = null;
            }

#if LIVEKIT_SDK
            if (videoStream != null)
            {
                videoStream.TextureReceived -= HandleTextureReceived;
                videoStream.Dispose();
                videoStream = null;
            }

            subscribedTrack = null;
#endif

            SubscribedParticipantSid = null;
            CurrentTexture = null;
            State = SubscribeState.Idle;

            if (displayImage != null)
            {
                displayImage.texture = null;
            }

            Log($"비디오 구독 해제됨 (총 {FramesReceived} 프레임 수신)");

            UnityMainThreadDispatcher.Enqueue(() => OnUnsubscribed?.Invoke());
        }

        #endregion

        #region Event Handlers

        private void HandleTrackSubscribed(RemoteTrackInfo trackInfo)
        {
            if (!trackInfo.IsVideo)
            {
                return;
            }

            // 사용 가능한 트랙 목록에 추가
            var available = new AvailableVideoTrack
            {
                ParticipantSid = trackInfo.ParticipantSid,
                ParticipantIdentity = trackInfo.ParticipantIdentity,
                TrackSid = trackInfo.TrackSid,
                TrackInfo = trackInfo
            };
            availableTracks.Add(available);

            Log($"새 비디오 트랙 감지: {trackInfo.ParticipantIdentity}");
            OnTrackAvailable?.Invoke(available);

            // 자동 구독
            if (autoSubscribeFirst && State == SubscribeState.Idle)
            {
                SubscribeToTrack(trackInfo);
            }
        }

        private void HandleTrackUnsubscribed(string trackSid)
        {
            // 목록에서 제거
            var removed = availableTracks.Find(t => t.TrackSid == trackSid);
            availableTracks.RemoveAll(t => t.TrackSid == trackSid);

            if (removed != null)
            {
                OnTrackRemoved?.Invoke(trackSid);
            }

#if LIVEKIT_SDK
            // 현재 구독 중인 트랙이면 해제
            if (subscribedTrack?.Sid == trackSid)
            {
                Log("구독 중인 트랙이 제거됨");
                Unsubscribe();

                // 다른 트랙으로 자동 전환
                if (autoSubscribeFirst && availableTracks.Count > 0)
                {
                    SubscribeFirstAvailable();
                }
            }
#endif
        }

        private void HandleParticipantDisconnected(string participantSid)
        {
            // 해당 참가자의 트랙 모두 제거
            availableTracks.RemoveAll(t => t.ParticipantSid == participantSid);

            if (SubscribedParticipantSid == participantSid)
            {
                Log($"구독 중인 참가자 연결 해제: {participantSid}");
                Unsubscribe();

                // 다른 트랙으로 자동 전환
                if (autoSubscribeFirst && availableTracks.Count > 0)
                {
                    SubscribeFirstAvailable();
                }
            }
        }

        private void HandleRoomDisconnected()
        {
            Log("Room 연결 해제됨 - 모든 트랙 정리");
            availableTracks.Clear();
            Unsubscribe();
        }

#if LIVEKIT_SDK
        private void HandleTextureReceived(Texture texture)
        {
            FramesReceived++;
            Resolution = new Vector2Int(texture.width, texture.height);
            CurrentTexture = texture;
            lastTexture = texture;

            // 디스플레이 업데이트 (매 프레임 할당 최적화)
            if (displayImage != null && displayImage.texture != texture)
            {
                displayImage.texture = texture;

                if (autoResize)
                {
                    UpdateDisplayAspectRatio(texture);
                }
            }

            OnVideoFrame?.Invoke(texture);
        }

#endif

        private void UpdateDisplayAspectRatio(Texture texture)
        {
            if (displayImage == null || texture == null) return;

            var rt = displayImage.rectTransform;
            float aspect = (float)texture.width / texture.height;
            rt.sizeDelta = new Vector2(rt.sizeDelta.y * aspect, rt.sizeDelta.y);
        }

        #endregion

        #region Debug GUI

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void OnGUI()
        {
            if (!showDebugGUI) return;

            GUILayout.BeginArea(new Rect(320, 10, 300, 280));
            GUI.Box(new Rect(0, 0, 300, 280), "");

            GUILayout.Label("<b>LiveKit Video Subscriber (SFU)</b>");
            GUILayout.Space(5);

            GUILayout.Label($"State: {State}");
            GUILayout.Label($"Subscribed: {SubscribedParticipantSid ?? "None"}");
            GUILayout.Label($"Resolution: {Resolution.x}x{Resolution.y}");
            GUILayout.Label($"Frames Received: {FramesReceived:N0}");
            GUILayout.Label($"Available Tracks: {availableTracks.Count}");

            GUILayout.Space(10);

            // 사용 가능한 트랙 목록
            if (availableTracks.Count > 0)
            {
                GUILayout.Label("Available:");
                foreach (var track in availableTracks)
                {
                    bool isSubscribed = track.ParticipantSid == SubscribedParticipantSid;
                    string label = isSubscribed ? $"[*] {track.ParticipantIdentity}" : track.ParticipantIdentity;

                    if (GUILayout.Button(label))
                    {
                        if (track.TrackInfo != null && !isSubscribed)
                        {
                            SubscribeToTrack(track.TrackInfo);
                        }
                    }
                }
            }

            GUILayout.Space(10);

            if (State == SubscribeState.Subscribed)
            {
                if (GUILayout.Button("Unsubscribe"))
                {
                    Unsubscribe();
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
                Debug.Log($"[LiveKitSubscriber] {message}");
            }
        }

        private void LogError(string message)
        {
            Debug.LogError($"[LiveKitSubscriber] {message}");
        }

        #endregion
    }

    #region Data Structures

    public enum SubscribeState
    {
        Idle,
        Subscribing,
        Subscribed
    }

    [Serializable]
    public class AvailableVideoTrack
    {
        public string ParticipantSid;
        public string ParticipantIdentity;
        public string TrackSid;
        public RemoteTrackInfo TrackInfo;
    }

    #endregion
}
