using System.Threading;
using Cysharp.Threading.Tasks;
using ReplaySystem.Core;
using ReplaySystem.Playback;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ReplaySystem.UI
{
    /// <summary>리플레이 UI 컨트롤러입니다.</summary>
    public class ReplayUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ReplayPlayer player;
        [SerializeField] private ReplayRecorder recorder;

        [Header("Playback Controls")]
        [SerializeField] private Button playButton;
        [SerializeField] private Button pauseButton;
        [SerializeField] private Button stopButton;
        [SerializeField] private Button reverseButton;
        [SerializeField] private Slider timelineSlider;
        [SerializeField] private Slider speedSlider;

        [Header("Record Controls")]
        [SerializeField] private Button recordButton;
        [SerializeField] private Button saveButton;

        [Header("Display")]
        [SerializeField] private TextMeshProUGUI timeText;
        [SerializeField] private TextMeshProUGUI stateText;
        [SerializeField] private TextMeshProUGUI speedText;

        [Header("Settings")]
        [SerializeField] private string defaultSavePath = "Replays/replay.rpl";

        private bool isDraggingTimeline;

        private void Awake()
        {
            SetupButtons();
            SetupSliders();
            SetupEvents();
        }

        private void OnDestroy()
        {
            RemoveEvents();
        }

        private void SetupButtons()
        {
            if (playButton != null)
            {
                playButton.onClick.AddListener(OnPlayClicked);
            }

            if (pauseButton != null)
            {
                pauseButton.onClick.AddListener(OnPauseClicked);
            }

            if (stopButton != null)
            {
                stopButton.onClick.AddListener(OnStopClicked);
            }

            if (reverseButton != null)
            {
                reverseButton.onClick.AddListener(OnReverseClicked);
            }

            if (recordButton != null)
            {
                recordButton.onClick.AddListener(OnRecordClicked);
            }

            if (saveButton != null)
            {
                saveButton.onClick.AddListener(OnSaveClicked);
            }
        }

        private void SetupSliders()
        {
            if (timelineSlider != null)
            {
                timelineSlider.onValueChanged.AddListener(OnTimelineChanged);
                timelineSlider.minValue = 0f;
                timelineSlider.maxValue = 1f;
            }

            if (speedSlider != null)
            {
                speedSlider.onValueChanged.AddListener(OnSpeedChanged);
                speedSlider.minValue = -4f;
                speedSlider.maxValue = 4f;
                speedSlider.value = 1f;
            }
        }

        private void SetupEvents()
        {
            if (player != null)
            {
                player.OnStateChanged += OnPlayerStateChanged;
                player.OnTimeChanged += OnPlayerTimeChanged;
                player.OnPlaybackComplete += OnPlaybackComplete;
            }

            if (recorder != null)
            {
                recorder.OnRecordingStarted += OnRecordingStarted;
                recorder.OnRecordingStopped += OnRecordingStopped;
            }
        }

        private void RemoveEvents()
        {
            if (player != null)
            {
                player.OnStateChanged -= OnPlayerStateChanged;
                player.OnTimeChanged -= OnPlayerTimeChanged;
                player.OnPlaybackComplete -= OnPlaybackComplete;
            }

            if (recorder != null)
            {
                recorder.OnRecordingStarted -= OnRecordingStarted;
                recorder.OnRecordingStopped -= OnRecordingStopped;
            }
        }

        private void OnPlayClicked()
        {
            player?.Play();
        }

        private void OnPauseClicked()
        {
            player?.Pause();
        }

        private void OnStopClicked()
        {
            player?.Stop();
        }

        private void OnReverseClicked()
        {
            player?.PlayReverse();
        }

        private void OnRecordClicked()
        {
            if (recorder == null)
            {
                return;
            }

            if (recorder.IsRecording)
            {
                recorder.StopRecording();
            }
            else
            {
                recorder.StartRecording();
            }
        }

        private void OnSaveClicked()
        {
            if (recorder == null || !recorder.IsRecording)
            {
                return;
            }

            var ct = this.GetCancellationTokenOnDestroy();
            SaveRecordingAsync(ct).Forget();
        }

        private async UniTaskVoid SaveRecordingAsync(CancellationToken ct)
        {
            string path = System.IO.Path.Combine(Application.persistentDataPath, defaultSavePath);
            await recorder.SaveRecordingAsync(path, ct);
        }

        private void OnTimelineChanged(float value)
        {
            if (player != null && !isDraggingTimeline)
            {
                player.SeekToProgress(value);
            }
        }

        private void OnSpeedChanged(float value)
        {
            if (player != null)
            {
                player.PlaybackSpeed = value;
            }

            UpdateSpeedText(value);
        }

        private void OnPlayerStateChanged(PlaybackState state)
        {
            UpdateStateText(state);
            UpdateButtonStates(state);
        }

        private void OnPlayerTimeChanged(float time)
        {
            UpdateTimeText(time, player?.Duration ?? 0f);

            if (timelineSlider != null && player != null)
            {
                timelineSlider.SetValueWithoutNotify(player.Progress);
            }
        }

        private void OnPlaybackComplete()
        {
            Debug.Log("[ReplayUI] Playback complete");
        }

        private void OnRecordingStarted()
        {
            if (recordButton != null)
            {
                var text = recordButton.GetComponentInChildren<TextMeshProUGUI>();

                if (text != null)
                {
                    text.text = "Stop";
                }
            }
        }

        private void OnRecordingStopped(ReplayData data)
        {
            if (recordButton != null)
            {
                var text = recordButton.GetComponentInChildren<TextMeshProUGUI>();

                if (text != null)
                {
                    text.text = "Record";
                }
            }
        }

        private void UpdateTimeText(float current, float total)
        {
            if (timeText != null)
            {
                timeText.text = $"{FormatTime(current)} / {FormatTime(total)}";
            }
        }

        private void UpdateStateText(PlaybackState state)
        {
            if (stateText != null)
            {
                stateText.text = state.ToString();
            }
        }

        private void UpdateSpeedText(float speed)
        {
            if (speedText != null)
            {
                speedText.text = $"{speed:F1}x";
            }
        }

        private void UpdateButtonStates(PlaybackState state)
        {
            bool isPlaying = state == PlaybackState.Playing;
            bool isReversing = state == PlaybackState.Reversing;

            if (playButton != null)
            {
                playButton.interactable = !isPlaying;
            }

            if (pauseButton != null)
            {
                pauseButton.interactable = isPlaying || isReversing;
            }

            if (reverseButton != null)
            {
                reverseButton.interactable = !isReversing;
            }
        }

        private static string FormatTime(float seconds)
        {
            int minutes = (int)(seconds / 60);
            int secs = (int)(seconds % 60);
            int ms = (int)((seconds % 1) * 100);
            return $"{minutes:D2}:{secs:D2}.{ms:D2}";
        }
    }
}
