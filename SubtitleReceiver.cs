using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
//using TMPro;

#if LIVEKIT_SDK
using LiveKit;
#endif

namespace LiveKitStreaming
{
    /// <summary>
    /// 자막 수신기
    /// LiveKit DataChannel을 통해 자막을 수신하고 UI에 표시
    /// </summary>
    public class SubtitleReceiver : MonoBehaviour
    {
//         #region Serialized Fields

//         [Header("Room Manager")]
//         [SerializeField] private LiveKitRoomManager roomManager;

//         [Header("UI 표시")]
//         [SerializeField] private Text subtitleText;
//         [SerializeField] private TextMeshProUGUI subtitleTMP;

//         [Header("설정")]
//         [SerializeField] private string subtitleTopic = "subtitle";
//         [SerializeField] private bool autoHide = true;
//         [SerializeField] private float hideDelay = 0.5f;

//         [Header("스타일")]
//         [SerializeField] private Color textColor = Color.white;
//         [SerializeField] private Color outlineColor = Color.black;
//         [SerializeField] private float outlineWidth = 1f;

//         [Header("디버그")]
//         [SerializeField] private bool enableLogging = false;

//         #endregion

//         #region Public Properties

//         /// <summary>현재 자막 텍스트</summary>
//         public string CurrentText { get; private set; } = "";

//         /// <summary>현재 자막 데이터</summary>
//         public SubtitleData CurrentSubtitle { get; private set; }

//         /// <summary>자막 히스토리</summary>
//         public IReadOnlyList<SubtitleData> History => subtitleHistory;

//         #endregion

//         #region Events

//         public event Action<SubtitleData> OnSubtitleReceived;
//         public event Action OnSubtitleCleared;

//         #endregion

//         #region Private Fields

//         private readonly List<SubtitleData> subtitleHistory = new();
//         private float lastSubtitleTime;
//         private bool isRegistered;

//         #endregion

//         #region Unity Lifecycle

//         private void Awake()
//         {
//             if (roomManager == null)
//             {
//                 roomManager = GetComponent<LiveKitRoomManager>();
//             }
//         }

//         private void Start()
//         {
//             RegisterDataHandler();
//             ApplyStyle();
//         }

//         private void Update()
//         {
//             // 자동 숨김 처리
//             if (autoHide && !string.IsNullOrEmpty(CurrentText))
//             {
//                 if (CurrentSubtitle != null && CurrentSubtitle.endTime > 0)
//                 {
//                     // endTime 기반 숨김은 송신측에서 처리
//                 }
//                 else if (Time.time - lastSubtitleTime > hideDelay)
//                 {
//                     ClearSubtitle();
//                 }
//             }
//         }

//         private void OnDestroy()
//         {
//             UnregisterDataHandler();
//         }

//         private void OnEnable()
//         {
//             RegisterDataHandler();
//         }

//         private void OnDisable()
//         {
//             UnregisterDataHandler();
//         }

//         #endregion

//         #region Public API

//         /// <summary>수동으로 자막 설정</summary>
//         public void SetSubtitle(string text)
//         {
//             CurrentText = text;
//             lastSubtitleTime = Time.time;
//             UpdateUI();
//         }

//         /// <summary>자막 클리어</summary>
//         public void ClearSubtitle()
//         {
//             CurrentText = "";
//             CurrentSubtitle = null;
//             UpdateUI();
//             OnSubtitleCleared?.Invoke();
//         }

//         /// <summary>히스토리 클리어</summary>
//         public void ClearHistory()
//         {
//             subtitleHistory.Clear();
//         }

//         /// <summary>토픽 설정</summary>
//         public void SetTopic(string topic)
//         {
//             if (subtitleTopic != topic)
//             {
//                 UnregisterDataHandler();
//                 subtitleTopic = topic;
//                 RegisterDataHandler();
//             }
//         }

//         /// <summary>UI 참조 설정</summary>
//         public void SetUI(Text text)
//         {
//             subtitleText = text;
//             subtitleTMP = null;
//             ApplyStyle();
//             UpdateUI();
//         }

//         /// <summary>UI 참조 설정 (TMP)</summary>
//         public void SetUI(TextMeshProUGUI tmp)
//         {
//             subtitleTMP = tmp;
//             subtitleText = null;
//             ApplyStyle();
//             UpdateUI();
//         }

//         #endregion

//         #region Data Handler

//         private void RegisterDataHandler()
//         {
// #if LIVEKIT_SDK
//             if (isRegistered || roomManager == null) return;

//             if (roomManager.Room != null)
//             {
//                 roomManager.Room.DataReceived += HandleDataReceived;
//                 isRegistered = true;
//                 Log("DataChannel 핸들러 등록됨");
//             }
//             else
//             {
//                 // Room 연결 대기
//                 roomManager.OnConnected += OnRoomConnected;
//             }
// #endif
//         }

//         private void UnregisterDataHandler()
//         {
// #if LIVEKIT_SDK
//             if (!isRegistered) return;

//             if (roomManager?.Room != null)
//             {
//                 roomManager.Room.DataReceived -= HandleDataReceived;
//             }

//             if (roomManager != null)
//             {
//                 roomManager.OnConnected -= OnRoomConnected;
//             }

//             isRegistered = false;
//             Log("DataChannel 핸들러 해제됨");
// #endif
//         }

// #if LIVEKIT_SDK
//         private void OnRoomConnected()
//         {
//             if (roomManager?.Room != null && !isRegistered)
//             {
//                 roomManager.Room.DataReceived += HandleDataReceived;
//                 isRegistered = true;
//                 Log("Room 연결 후 DataChannel 핸들러 등록됨");
//             }
//         }

//         private void HandleDataReceived(byte[] data, RemoteParticipant participant, DataPacketKind? kind)
//         {
//             // 참고: topic 필터링은 LiveKit Unity SDK WebGL 버전에서는 지원되지 않을 수 있음
//             // Native SDK에서는 topic이 포함된 오버로드가 있을 수 있음

//             try
//             {
//                 var json = System.Text.Encoding.UTF8.GetString(data);
                
//                 // JSON 내용으로 자막 데이터인지 확인
//                 if (!json.Contains("\"text\"") || !json.Contains("\"startTime\""))
//                 {
//                     return; // 자막 데이터가 아님
//                 }
                
//                 var subtitle = JsonUtility.FromJson<SubtitleData>(json);

//                 if (subtitle != null)
//                 {
//                     ProcessSubtitle(subtitle);
//                 }
//             }
//             catch (Exception e)
//             {
//                 Log($"자막 파싱 실패: {e.Message}");
//             }
//         }
// #endif

//         private void ProcessSubtitle(SubtitleData subtitle)
//         {
//             CurrentSubtitle = subtitle;
//             lastSubtitleTime = Time.time;

//             if (string.IsNullOrEmpty(subtitle.text))
//             {
//                 // 빈 텍스트 = 자막 클리어
//                 ClearSubtitle();
//                 return;
//             }

//             CurrentText = subtitle.text;
//             subtitleHistory.Add(subtitle);

//             // 히스토리 크기 제한 (최대 100개)
//             if (subtitleHistory.Count > 100)
//             {
//                 subtitleHistory.RemoveAt(0);
//             }

//             UpdateUI();
//             OnSubtitleReceived?.Invoke(subtitle);

//             Log($"자막 수신: [{subtitle.index}] {subtitle.text}");
//         }

//         #endregion

//         #region UI Update

//         private void UpdateUI()
//         {
//             if (subtitleTMP != null)
//             {
//                 subtitleTMP.text = CurrentText;
//                 subtitleTMP.gameObject.SetActive(!string.IsNullOrEmpty(CurrentText));
//             }
//             else if (subtitleText != null)
//             {
//                 subtitleText.text = CurrentText;
//                 subtitleText.gameObject.SetActive(!string.IsNullOrEmpty(CurrentText));
//             }
//         }

//         private void ApplyStyle()
//         {
//             if (subtitleTMP != null)
//             {
//                 subtitleTMP.color = textColor;
//                 subtitleTMP.outlineColor = outlineColor;
//                 subtitleTMP.outlineWidth = outlineWidth;
//             }
//             else if (subtitleText != null)
//             {
//                 subtitleText.color = textColor;

//                 // Outline 컴포넌트가 있으면 적용
//                 var outline = subtitleText.GetComponent<Outline>();
//                 if (outline != null)
//                 {
//                     outline.effectColor = outlineColor;
//                     outline.effectDistance = new Vector2(outlineWidth, -outlineWidth);
//                 }
//             }
//         }

//         #endregion

//         #region Helpers

//         private void Log(string message)
//         {
//             if (enableLogging)
//             {
//                 Debug.Log($"[SubtitleReceiver] {message}");
//             }
//         }

//         #endregion
    }
}
