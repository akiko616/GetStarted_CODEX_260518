using System;
using UnityEngine;

namespace LiveKitStreaming
{
    /// <summary>
    /// LiveKit 스트리밍 설정 ScriptableObject
    /// 프로젝트 전체에서 재사용 가능한 설정
    /// </summary>
    [CreateAssetMenu(fileName = "LiveKitConfiguration", menuName = "LiveKit/Streaming Config")]
    public class LiveKitStreamingConfig : ScriptableObject
    {
        #region Server Settings

        [Header("서버 설정")]
        [SerializeField, Tooltip("LiveKit 서버 URL (ws:// 또는 wss://)")]
        private string serverUrl = "ws://localhost:7880";

        [SerializeField, Tooltip("토큰 서버 URL (옵션)")]
        private string tokenServerUrl = "http://localhost:3000";

        /// <summary>LiveKit 서버 URL</summary>
        public string ServerUrl => serverUrl;

        /// <summary>토큰 서버 URL</summary>
        public string TokenServerUrl => tokenServerUrl;

        #endregion

        #region Video Settings

        [Header("비디오 설정")]
        [SerializeField] private VideoSettings videoSettings = new();

        /// <summary>비디오 설정</summary>
        public VideoSettings Video => videoSettings;

        #endregion

        #region Room Options

        [Header("Room 옵션")]
        [SerializeField, Tooltip("자동으로 트랙 구독")]
        private bool autoSubscribe = true;

        [SerializeField, Tooltip("시청자가 없으면 퍼블리싱 일시정지")]
        private bool dynacast = true;

        [SerializeField, Tooltip("UI 크기에 따라 품질 자동 조절")]
        private bool adaptiveStream = true;

        /// <summary>자동 구독 여부</summary>
        public bool AutoSubscribe => autoSubscribe;

        /// <summary>Dynacast 활성화 여부</summary>
        public bool Dynacast => dynacast;

        /// <summary>AdaptiveStream 활성화 여부</summary>
        public bool AdaptiveStream => adaptiveStream;

        #endregion

        #region Connection Settings

        [Header("연결 설정")]
        [SerializeField, Tooltip("연결 타임아웃 (초)")]
        private float connectionTimeout = 10f;

        [SerializeField, Tooltip("재연결 대기 시간 (초)")]
        private float reconnectDelay = 2f;

        [SerializeField, Tooltip("최대 재연결 시도 횟수")]
        private int maxReconnectAttempts = 5;

        [SerializeField, Tooltip("자동 재연결 활성화")]
        private bool autoReconnect = true;

        /// <summary>연결 타임아웃 (초)</summary>
        public float ConnectionTimeout => connectionTimeout;

        /// <summary>재연결 대기 시간 (초)</summary>
        public float ReconnectDelay => reconnectDelay;

        /// <summary>최대 재연결 시도 횟수</summary>
        public int MaxReconnectAttempts => maxReconnectAttempts;

        /// <summary>자동 재연결 활성화</summary>
        public bool AutoReconnect => autoReconnect;

        #endregion

        #region Debug Settings

        [Header("디버그")]
        [SerializeField] private bool enableLogging = true;
        [SerializeField] private bool showDebugGUI = false;

        /// <summary>로깅 활성화</summary>
        public bool EnableLogging => enableLogging;

        /// <summary>디버그 GUI 표시</summary>
        public bool ShowDebugGUI => showDebugGUI;

        #endregion

        #region Factory Methods

        /// <summary>기본 설정으로 런타임 생성</summary>
        public static LiveKitStreamingConfig CreateDefault()
        {
            var config = CreateInstance<LiveKitStreamingConfig>();
            config.name = "LiveKitConfig_Runtime";
            return config;
        }

        /// <summary>설정 복사본 생성</summary>
        public LiveKitStreamingConfig Clone()
        {
            var clone = CreateInstance<LiveKitStreamingConfig>();
            clone.serverUrl = serverUrl;
            clone.tokenServerUrl = tokenServerUrl;
            clone.videoSettings = videoSettings.Clone();
            clone.autoSubscribe = autoSubscribe;
            clone.dynacast = dynacast;
            clone.adaptiveStream = adaptiveStream;
            clone.connectionTimeout = connectionTimeout;
            clone.reconnectDelay = reconnectDelay;
            clone.maxReconnectAttempts = maxReconnectAttempts;
            clone.autoReconnect = autoReconnect;
            clone.enableLogging = enableLogging;
            clone.showDebugGUI = showDebugGUI;
            return clone;
        }

        #endregion
    }

    #region Video Settings

    /// <summary>비디오 캡처/인코딩 설정</summary>
    [Serializable]
    public class VideoSettings
    {
        [Header("해상도")]
        [SerializeField, Range(320, 3840)] private int width = 1280;
        [SerializeField, Range(240, 2160)] private int height = 720;
        [SerializeField, Range(15, 60)] private int frameRate = 30;

        [Header("인코딩")]
        [SerializeField] private VideoCodecType codec = VideoCodecType.Vp8;
        [SerializeField, Range(500000, 10000000)] private int maxBitrate = 2500000;
        [SerializeField, Tooltip("다중 품질 레이어 활성화")] private bool simulcast = true;

        /// <summary>캡처 너비</summary>
        public int Width => width;

        /// <summary>캡처 높이</summary>
        public int Height => height;

        /// <summary>프레임 레이트</summary>
        public int FrameRate => frameRate;

        /// <summary>비디오 코덱</summary>
        public VideoCodecType Codec => codec;

        /// <summary>최대 비트레이트 (bps)</summary>
        public int MaxBitrate => maxBitrate;

        /// <summary>Simulcast 활성화 여부</summary>
        public bool Simulcast => simulcast;

        /// <summary>설정 복사</summary>
        public VideoSettings Clone()
        {
            return new VideoSettings
            {
                width = width,
                height = height,
                frameRate = frameRate,
                codec = codec,
                maxBitrate = maxBitrate,
                simulcast = simulcast
            };
        }

        /// <summary>해상도 문자열</summary>
        public string ResolutionString => $"{width}x{height}@{frameRate}fps";

        /// <summary>비트레이트 문자열 (kbps)</summary>
        public string BitrateString => $"{maxBitrate / 1000} kbps";
    }

    #endregion

    #region Connection Stats

    /// <summary>연결 통계 정보</summary>
    [Serializable]
    public struct ConnectionStats
    {
        /// <summary>연결 시간 (초)</summary>
        public float ConnectionDuration;

        /// <summary>재연결 횟수</summary>
        public int ReconnectCount;

        /// <summary>전송된 프레임 수</summary>
        public long FramesSent;

        /// <summary>수신된 프레임 수</summary>
        public long FramesReceived;

        /// <summary>현재 비트레이트 (bps)</summary>
        public int CurrentBitrate;

        /// <summary>패킷 손실률 (%)</summary>
        public float PacketLossPercent;

        /// <summary>RTT (ms)</summary>
        public float RoundTripTime;

        /// <summary>마지막 업데이트 시간</summary>
        public float LastUpdateTime;

        /// <summary>통계 초기화</summary>
        public void Reset()
        {
            ConnectionDuration = 0f;
            ReconnectCount = 0;
            FramesSent = 0;
            FramesReceived = 0;
            CurrentBitrate = 0;
            PacketLossPercent = 0f;
            RoundTripTime = 0f;
            LastUpdateTime = 0f;
        }
    }

    #endregion
}
