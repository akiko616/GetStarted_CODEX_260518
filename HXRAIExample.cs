// ============================================================================
// HXRAIExample.cs
// MonoBehaviour 사용 예제 - 마이크 녹음 → ASR / 유사도 분석
// ============================================================================

using System.Threading;
using Cysharp.Threading.Tasks;
using HXRAI;
using UnityEngine;
using UnityEngine.UI;

public class HXRAIExample : MonoBehaviour
{
    [Header("서버 설정")]
    [SerializeField] private AIServerConfig serverConfig = new();

    [Header("UI (선택)")]
    [SerializeField] private Text resultText;
    [SerializeField] private Button recordButton;
    [SerializeField] private Button similarityButton;

    private HXRAIClient _client;
    private AudioClip _recordedClip;
    private bool _isRecording;
    private CancellationTokenSource _cts;

    // ========================================================================
    // Lifecycle
    // ========================================================================

    private void Start()
    {
        _client = new HXRAIClient(serverConfig);
        _cts = new CancellationTokenSource();

        // 시작 시 서버 연결 확인
        CheckServerAsync().Forget();
    }

    private void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _client?.Dispose();
    }

    // ========================================================================
    // 1. 서버 헬스체크
    // ========================================================================

    private async UniTaskVoid CheckServerAsync()
    {
        bool ok = await _client.HealthCheckAsync(_cts.Token);
        Debug.Log(ok ? "✅ 서버 연결 성공" : "❌ 서버 연결 실패");
    }

    // ========================================================================
    // 2. 마이크 녹음 → ASR
    // ========================================================================

    /// <summary>
    /// 버튼에 연결하거나 직접 호출
    /// </summary>
    public void ToggleRecording()
    {
        if (_isRecording)
            StopAndTranscribe().Forget();
        else
            StartRecording();
    }

    private void StartRecording()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("마이크가 없습니다");
            return;
        }

        // 16kHz 모노로 최대 30초 녹음
        _recordedClip = Microphone.Start(null, false, 30, 16000);
        _isRecording = true;
        Debug.Log("🎙️ 녹음 시작...");
    }

    private async UniTaskVoid StopAndTranscribe()
    {
        Microphone.End(null);
        _isRecording = false;
        Debug.Log("🛑 녹음 종료, ASR 전송 중...");

        if (_recordedClip == null) return;

        try
        {
            // ── AudioClip → 서버 ASR ──
            ASRResult result = await _client.TranscribeAsync(
                _recordedClip,
                language: "ko",
                ct: _cts.Token
            );

            if (result.success)
            {
                Debug.Log($"📝 인식 결과: {result.text}");
                Debug.Log($"   신뢰도: {result.confidence:P1}, 처리시간: {result.processingTimeSeconds:F2}s");

                if (resultText != null)
                    resultText.text = result.text;
            }
            else
            {
                Debug.LogError($"ASR 실패: {result.error}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"ASR 예외: {e.Message}");
        }
    }

    // ========================================================================
    // 3. 텍스트 유사도
    // ========================================================================

    public void CompareTest() => TestTextSimilarityAsync().Forget();
    /// <summary>
    /// 두 텍스트 비교 예제
    /// </summary>
    ///
    public async UniTask TestTextSimilarityAsync()
    {
        try
        {
            SimilarityResult result = await _client.CompareSimilarityAsync(
                "왼쪽으로 돌아주세요",
                "좌회전 해주세요",
                _cts.Token
            );

            if (result.success)
            {
                Debug.Log($"유사도: {result.similarityPercent:F1}%");
                Debug.Log($"  구조: {result.detailedScores?.biEncoderScore:F3}");
                Debug.Log($"  의미: {result.detailedScores?.crossEncoderScore:F3}");
                Debug.Log($"  분석: {result.analysis?.interpretation}");
            }
            else
            {
                Debug.LogError($"유사도 실패: {result.error}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"유사도 예외: {e.Message}");
        }
    }

    // ========================================================================
    // 4. 음성-텍스트 유사도 (발음 평가 등)
    // ========================================================================

    /// <summary>
    /// 녹음된 음성과 정답 텍스트 비교
    /// 서버에서 ASR + 유사도를 한번에 처리
    /// </summary>
    public async UniTask TestAudioSimilarityAsync(string correctText)
    {
        if (_recordedClip == null)
        {
            Debug.LogError("먼저 녹음을 해주세요");
            return;
        }

        try
        {
            AudioSimilarityResult result = await _client.CompareAudioSimilarityAsync(
                _recordedClip,
                correctText,
                language: "ko",
                ct: _cts.Token
            );

            if (result.success)
            {
                Debug.Log($"🎤 인식된 텍스트: {result.textA}");
                Debug.Log($"📄 정답 텍스트:   {result.textB}");
                Debug.Log($"📊 유사도: {result.similarityPercent:F1}%");
                Debug.Log($"   ASR 신뢰도: {result.asrConfidence:F2}");
                Debug.Log($"   분석: {result.analysis?.interpretation}");
            }
            else
            {
                Debug.LogError($"음성 유사도 실패: {result.error}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"음성 유사도 예외: {e.Message}");
        }
    }

    // ========================================================================
    // 5. 이미 있는 AudioClip으로 바로 ASR (녹음 없이)
    // ========================================================================

    /// <summary>
    /// Resources나 에셋에서 로드한 AudioClip도 바로 사용 가능
    /// </summary>
    public async UniTask<string> TranscribeClipAsync(AudioClip clip)
    {
        ASRResult result = await _client.TranscribeAsync(clip, "ko", _cts.Token);
        return result.success ? result.text : null;
    }
}