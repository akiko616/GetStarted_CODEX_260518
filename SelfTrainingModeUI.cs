using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SelfTraining;

namespace SelfTraining.UI
{
    /// <summary>
    /// SelfTrainingMode를 제어하기 위한 간단한 UI 컴포넌트
    /// 개발/테스트 용도로 사용
    /// </summary>
    public class SelfTrainingModeUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SelfTrainingMode selfTrainingMode;

        [Header("UI Elements")]
        [SerializeField] private Button buttonStart;
        [SerializeField] private Button buttonStop;
        [SerializeField] private Button buttonPause;
        [SerializeField] private Button buttonResume;
        [SerializeField] private TMP_Text textStatus;
        [SerializeField] private TMP_InputField inputPlayerName;
        [SerializeField] private TMP_InputField inputSceneName;
        [SerializeField] private TMP_Dropdown dropdownScenario;

        [Header("Status Panel")]
        [SerializeField] private GameObject panelStatus;
        [SerializeField] private TMP_Text textPartyId;
        [SerializeField] private TMP_Text textInstanceId;
        [SerializeField] private TMP_Text textTrainingTime;

        private float trainingStartTime;
        private bool isTraining;

        private void Start()
        {
            // SelfTrainingMode 찾기
            if (selfTrainingMode == null)
            {
                selfTrainingMode = SelfTrainingMode.Instance;
                if (selfTrainingMode == null)
                {
                    selfTrainingMode = FindFirstObjectByType<SelfTrainingMode>();
                }
            }

            SetupUI();
            SetupEvents();
            UpdateUI();
        }

        private void OnDestroy()
        {
            RemoveEvents();
        }

        private void Update()
        {
            if (isTraining && textTrainingTime != null)
            {
                float elapsed = Time.realtimeSinceStartup - trainingStartTime;
                textTrainingTime.text = $"훈련 시간: {FormatTime(elapsed)}";
            }
        }

        private void SetupUI()
        {
            if (buttonStart != null)
                buttonStart.onClick.AddListener(OnClickStart);

            if (buttonStop != null)
                buttonStop.onClick.AddListener(OnClickStop);

            if (buttonPause != null)
                buttonPause.onClick.AddListener(OnClickPause);

            if (buttonResume != null)
                buttonResume.onClick.AddListener(OnClickResume);

            // 기본값 설정
            if (inputPlayerName != null && string.IsNullOrEmpty(inputPlayerName.text))
                inputPlayerName.text = "훈련생";

            if (inputSceneName != null && string.IsNullOrEmpty(inputSceneName.text))
                inputSceneName.text = "TrainingScene";
        }

        private void SetupEvents()
        {
            if (selfTrainingMode == null) return;

            selfTrainingMode.OnHostStarted += OnHostStarted;
            selfTrainingMode.OnHostStopped += OnHostStopped;
            selfTrainingMode.OnTrainingStarted += OnTrainingStarted;
            selfTrainingMode.OnTrainingEnded += OnTrainingEnded;
            selfTrainingMode.OnError += OnError;
        }

        private void RemoveEvents()
        {
            if (selfTrainingMode == null) return;

            selfTrainingMode.OnHostStarted -= OnHostStarted;
            selfTrainingMode.OnHostStopped -= OnHostStopped;
            selfTrainingMode.OnTrainingStarted -= OnTrainingStarted;
            selfTrainingMode.OnTrainingEnded -= OnTrainingEnded;
            selfTrainingMode.OnError -= OnError;
        }

        #region Button Handlers
        private void OnClickStart()
        {
            if (selfTrainingMode == null)
            {
                SetStatus("SelfTrainingMode를 찾을 수 없습니다", Color.red);
                return;
            }

            // 커스텀 데이터 생성
            string playerName = inputPlayerName != null ? inputPlayerName.text : "훈련생";
            string sceneName = inputSceneName != null ? inputSceneName.text : "TrainingScene";
            int scenarioType = dropdownScenario != null ? dropdownScenario.value : 0;

            var trainingData = selfTrainingMode.CreateTrainingData(
                sceneName,
                $"player_{System.Guid.NewGuid():N}".Substring(0, 16),
                playerName,
                scenarioType
            );

            SetStatus("Self Training 시작 중...", Color.yellow);
            selfTrainingMode.StartSelfTraining(trainingData);
        }

        private void OnClickStop()
        {
            if (selfTrainingMode == null) return;

            SetStatus("Self Training 중지 중...", Color.yellow);
            selfTrainingMode.StopSelfTraining();
        }

        private void OnClickPause()
        {
            if (selfTrainingMode == null) return;

            selfTrainingMode.PauseTraining();
            SetStatus("훈련 일시정지됨", Color.cyan);
            UpdateUI();
        }

        private void OnClickResume()
        {
            if (selfTrainingMode == null) return;

            selfTrainingMode.ResumeTraining();
            SetStatus("훈련 재개됨", Color.green);
            UpdateUI();
        }
        #endregion

        #region Event Handlers
        private void OnHostStarted()
        {
            SetStatus("Host 시작됨", Color.green);
            UpdateUI();
        }

        private void OnHostStopped()
        {
            SetStatus("Host 중지됨", Color.gray);
            isTraining = false;
            UpdateUI();
        }

        private void OnTrainingStarted(SelfTrainingMode.SelfTrainingData data)
        {
            SetStatus($"훈련 시작: {data.PlayerName}", Color.green);
            trainingStartTime = Time.realtimeSinceStartup;
            isTraining = true;

            if (textPartyId != null)
                textPartyId.text = $"파티 ID: {data.PartyId}";

            if (textInstanceId != null)
                textInstanceId.text = $"인스턴스 ID: {data.InstanceId}";

            UpdateUI();
        }

        private void OnTrainingEnded()
        {
            SetStatus("훈련 종료됨", Color.gray);
            isTraining = false;
            UpdateUI();
        }

        private void OnError(string error)
        {
            SetStatus($"오류: {error}", Color.red);
            UpdateUI();
        }
        #endregion

        #region UI Helpers
        private void SetStatus(string message, Color color)
        {
            if (textStatus != null)
            {
                textStatus.text = message;
                textStatus.color = color;
            }
            Debug.Log($"[SelfTrainingUI] {message}");
        }

        private void UpdateUI()
        {
            bool canStart = selfTrainingMode != null && !selfTrainingMode.IsHostRunning;
            bool canStop = selfTrainingMode != null && selfTrainingMode.IsHostRunning;
            bool canPauseResume = selfTrainingMode != null && selfTrainingMode.IsTrainingActive;

            if (buttonStart != null)
                buttonStart.interactable = canStart;

            if (buttonStop != null)
                buttonStop.interactable = canStop;

            if (buttonPause != null)
                buttonPause.interactable = canPauseResume && Time.timeScale > 0;

            if (buttonResume != null)
                buttonResume.interactable = canPauseResume && Time.timeScale == 0;

            if (panelStatus != null)
                panelStatus.SetActive(isTraining);

            // 입력 필드 비활성화 (훈련 중)
            if (inputPlayerName != null)
                inputPlayerName.interactable = canStart;

            if (inputSceneName != null)
                inputSceneName.interactable = canStart;

            if (dropdownScenario != null)
                dropdownScenario.interactable = canStart;
        }

        private string FormatTime(float seconds)
        {
            int mins = Mathf.FloorToInt(seconds / 60f);
            int secs = Mathf.FloorToInt(seconds % 60f);
            return $"{mins:D2}:{secs:D2}";
        }
        #endregion
    }
}
