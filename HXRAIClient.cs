
using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace HXRAI
{
    // ========================================================================
    // 설정
    // ========================================================================

    [Serializable]
    public class AIServerConfig
    {
        [Header("서버 설정")]
        [Tooltip("HTTPS 서버 주소 (끝에 / 제외)")]
        public string baseUrl = "https://hxr.iptime.org";

        [Header("타임아웃 (초)")]
        public int asrTimeout = 30;
        public int similarityTimeout = 20;
        public int healthTimeout = 5;

        [Header("오디오 설정")]
        [Tooltip("서버 ASR이 기대하는 샘플레이트")]
        public int targetSampleRate = 16000;

        [Header("SSL 설정")]
        [Tooltip("개발 환경에서 SSL 인증서 검증 우회 (프로덕션에서는 false)")]
        public bool bypassSSL = true;

        [Tooltip("인증서 핀닝용 SHA256 해시 (비어있으면 미사용)")]
        public string certPinHash = "";
    }

    // ========================================================================
    // 응답 모델
    // ========================================================================

    #region Response Models

    [Serializable]
    public class ASRResult
    {
        public bool success;
        public string text;
        public float confidence;
        public float durationSeconds;
        public float processingTimeSeconds;
        public string error;
        public string requestId;
    }

    [Serializable]
    public class DetailedScores
    {
        public float biEncoderScore;
        public float crossEncoderScore;
        public float biEncoderWeight;
        public float crossEncoderWeight;
    }

    [Serializable]
    public class SimilarityAnalysis
    {
        public string category;
        public string categoryDescription;
        public bool structureMatch;
        public bool contextMatch;
        public string interpretation;
    }

    [Serializable]
    public class SimilarityResult
    {
        public bool success;
        public string textA;
        public string textB;
        public float similarity;
        public float similarityPercent;
        public DetailedScores detailedScores;
        public SimilarityAnalysis analysis;
        public float processingTimeSeconds;
        public string error;
        public string requestId;
    }

    [Serializable]
    public class AudioSimilarityResult : SimilarityResult
    {
        public float asrConfidence;
        public float audioDurationSeconds;
    }

    #endregion

    // ========================================================================
    // 오디오 변환 유틸리티
    // ========================================================================

    public static class AudioConverter
    {
        /// <summary>
        /// AudioClip → 16-bit PCM WAV byte[] 변환
        /// 서버가 기대하는 16kHz 모노 WAV로 리샘플링 포함
        /// </summary>
        public static byte[] AudioClipToWav(AudioClip clip, int targetSampleRate = 16000)
        {
            if (clip == null)
                throw new ArgumentNullException(nameof(clip));

            // 원본 PCM 추출
            var samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);

            // 스테레오 → 모노
            float[] mono;
            if (clip.channels > 1)
            {
                int monoLength = clip.samples;
                mono = new float[monoLength];
                for (int i = 0; i < monoLength; i++)
                {
                    float sum = 0f;
                    for (int ch = 0; ch < clip.channels; ch++)
                        sum += samples[i * clip.channels + ch];
                    mono[i] = sum / clip.channels;
                }
            }
            else
            {
                mono = samples;
            }

            // 리샘플링 (선형 보간)
            float[] resampled;
            if (clip.frequency != targetSampleRate)
            {
                double ratio = (double)clip.frequency / targetSampleRate;
                int newLength = (int)(mono.Length / ratio);
                resampled = new float[newLength];

                for (int i = 0; i < newLength; i++)
                {
                    double srcIndex = i * ratio;
                    int idx = (int)srcIndex;
                    double frac = srcIndex - idx;

                    if (idx + 1 < mono.Length)
                        resampled[i] = (float)(mono[idx] * (1.0 - frac) + mono[idx + 1] * frac);
                    else
                        resampled[i] = mono[Mathf.Min(idx, mono.Length - 1)];
                }
            }
            else
            {
                resampled = mono;
            }

            // Float32 → Int16 PCM
            var pcm16 = new short[resampled.Length];
            for (int i = 0; i < resampled.Length; i++)
            {
                float clamped = Mathf.Clamp(resampled[i], -1f, 1f);
                pcm16[i] = (short)(clamped * 32767f);
            }

            // WAV 헤더 작성
            return WriteWav(pcm16, targetSampleRate, 1);
        }

        /// <summary>
        /// AudioClip → Base64 WAV 문자열
        /// </summary>
        public static string AudioClipToBase64(AudioClip clip, int targetSampleRate = 16000)
        {
            byte[] wav = AudioClipToWav(clip, targetSampleRate);
            return Convert.ToBase64String(wav);
        }

        private static byte[] WriteWav(short[] pcmData, int sampleRate, int channels)
        {
            int byteRate = sampleRate * channels * 2; // 16-bit = 2 bytes
            int dataSize = pcmData.Length * 2;
            int fileSize = 44 + dataSize;

            var wav = new byte[fileSize];
            int pos = 0;

            // RIFF header
            WriteString(wav, ref pos, "RIFF");
            WriteInt32(wav, ref pos, fileSize - 8);
            WriteString(wav, ref pos, "WAVE");

            // fmt chunk
            WriteString(wav, ref pos, "fmt ");
            WriteInt32(wav, ref pos, 16);            // chunk size
            WriteInt16(wav, ref pos, 1);             // PCM format
            WriteInt16(wav, ref pos, (short)channels);
            WriteInt32(wav, ref pos, sampleRate);
            WriteInt32(wav, ref pos, byteRate);
            WriteInt16(wav, ref pos, (short)(channels * 2)); // block align
            WriteInt16(wav, ref pos, 16);            // bits per sample

            // data chunk
            WriteString(wav, ref pos, "data");
            WriteInt32(wav, ref pos, dataSize);

            // PCM 데이터 (Little-Endian)
            Buffer.BlockCopy(pcmData, 0, wav, pos, dataSize);

            return wav;
        }

        private static void WriteString(byte[] buf, ref int pos, string val)
        {
            foreach (char c in val) buf[pos++] = (byte)c;
        }

        private static void WriteInt32(byte[] buf, ref int pos, int val)
        {
            buf[pos++] = (byte)(val & 0xFF);
            buf[pos++] = (byte)((val >> 8) & 0xFF);
            buf[pos++] = (byte)((val >> 16) & 0xFF);
            buf[pos++] = (byte)((val >> 24) & 0xFF);
        }

        private static void WriteInt16(byte[] buf, ref int pos, short val)
        {
            buf[pos++] = (byte)(val & 0xFF);
            buf[pos++] = (byte)((val >> 8) & 0xFF);
        }
    }

    // ========================================================================
    // 메인 클라이언트
    // ========================================================================

    public class HXRAIClient : IDisposable
    {
        private readonly AIServerConfig _config;
        private CancellationTokenSource _cts;

        public HXRAIClient(AIServerConfig config = null)
        {
            _config = config ?? new AIServerConfig();
            _cts = new CancellationTokenSource();
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }

        // ====================================================================
        // 1. ASR - AudioClip → 텍스트
        // ====================================================================

        /// <summary>
        /// AudioClip을 서버로 전송하여 음성인식 수행
        /// </summary>
        /// <param name="clip">녹음된 AudioClip</param>
        /// <param name="language">언어 코드: "ko", "en" 등</param>
        /// <param name="ct">취소 토큰 (선택)</param>
        public async UniTask<ASRResult> TranscribeAsync(
            AudioClip clip,
            string language = "ko",
            CancellationToken ct = default)
        {
            ct = ct == default ? _cts.Token : ct;

            string audioBase64 = AudioConverter.AudioClipToBase64(clip, _config.targetSampleRate);

            var body = new JObject
            {
                ["audio_base64"] = audioBase64,
                ["language"] = language
            };

            string json = await PostJsonAsync(
                $"{_config.baseUrl}/asr",
                body.ToString(Formatting.None),
                _config.asrTimeout,
                ct
            );

            var raw = JObject.Parse(json);
            return new ASRResult
            {
                success = raw.Value<bool>("success"),
                text = raw.Value<string>("text"),
                confidence = raw.Value<float?>("confidence") ?? 0f,
                durationSeconds = raw.Value<float?>("duration_seconds") ?? 0f,
                processingTimeSeconds = raw.Value<float?>("processing_time_seconds") ?? 0f,
                error = raw.Value<string>("error"),
                requestId = raw.Value<string>("request_id") ?? ""
            };
        }

        // ====================================================================
        // 2. 텍스트-텍스트 유사도
        // ====================================================================

        /// <summary>
        /// 두 텍스트 간 하이브리드 유사도 측정
        /// </summary>
        public async UniTask<SimilarityResult> CompareSimilarityAsync(
            string textA,
            string textB,
            CancellationToken ct = default)
        {
            ct = ct == default ? _cts.Token : ct;

            var body = new JObject
            {
                ["text_a"] = textA,
                ["text_b"] = textB
            };

            string json = await PostJsonAsync(
                $"{_config.baseUrl}/similarity",
                body.ToString(Formatting.None),
                _config.similarityTimeout,
                ct
            );

            return ParseSimilarityResult(json);
        }

        // ====================================================================
        // 3. 음성-텍스트 유사도 (ASR + 유사도 원샷)
        // ====================================================================

        /// <summary>
        /// AudioClip과 텍스트 간 유사도 측정
        /// 서버에서 ASR → 유사도 분석을 한번에 처리
        /// </summary>
        public async UniTask<AudioSimilarityResult> CompareAudioSimilarityAsync(
            AudioClip clip,
            string compareText,
            string language = "ko",
            CancellationToken ct = default)
        {
            ct = ct == default ? _cts.Token : ct;

            string audioBase64 = AudioConverter.AudioClipToBase64(clip, _config.targetSampleRate);

            var body = new JObject
            {
                ["audio_base64"] = audioBase64,
                ["text_b"] = compareText,
                ["language"] = language
            };

            string json = await PostJsonAsync(
                $"{_config.baseUrl}/similarity/audio",
                body.ToString(Formatting.None),
                _config.asrTimeout,       // ASR 포함이라 더 긴 타임아웃
                ct
            );

            var raw = JObject.Parse(json);
            var result = new AudioSimilarityResult
            {
                success = raw.Value<bool>("success"),
                textA = raw.Value<string>("text_a"),
                textB = raw.Value<string>("text_b"),
                similarity = raw.Value<float?>("similarity") ?? 0f,
                similarityPercent = raw.Value<float?>("similarity_percent") ?? 0f,
                processingTimeSeconds = raw.Value<float?>("processing_time_seconds") ?? 0f,
                error = raw.Value<string>("error"),
                requestId = raw.Value<string>("request_id") ?? "",
                asrConfidence = raw.Value<float?>("asr_confidence") ?? 0f,
                audioDurationSeconds = raw.Value<float?>("audio_duration_seconds") ?? 0f
            };

            var ds = raw["detailed_scores"];
            if (ds != null && ds.Type != JTokenType.Null)
            {
                result.detailedScores = new DetailedScores
                {
                    biEncoderScore = ds.Value<float>("bi_encoder_score"),
                    crossEncoderScore = ds.Value<float>("cross_encoder_score"),
                    biEncoderWeight = ds.Value<float>("bi_encoder_weight"),
                    crossEncoderWeight = ds.Value<float>("cross_encoder_weight")
                };
            }

            var an = raw["analysis"];
            if (an != null && an.Type != JTokenType.Null)
            {
                result.analysis = new SimilarityAnalysis
                {
                    category = an.Value<string>("category"),
                    categoryDescription = an.Value<string>("category_description"),
                    structureMatch = an.Value<bool>("structure_match"),
                    contextMatch = an.Value<bool>("context_match"),
                    interpretation = an.Value<string>("interpretation")
                };
            }

            return result;
        }

        // ====================================================================
        // 4. 헬스체크
        // ====================================================================

        public async UniTask<bool> HealthCheckAsync(CancellationToken ct = default)
        {
            ct = ct == default ? _cts.Token : ct;
            try
            {
                string json = await GetJsonAsync(
                    $"{_config.baseUrl}/health",
                    _config.healthTimeout,
                    ct
                );
                var raw = JObject.Parse(json);
                return raw.Value<string>("status") == "healthy";
            }
            catch
            {
                return false;
            }
        }

        // ====================================================================
        // HTTP 내부 유틸리티 (UniTask + UnityWebRequest)
        // ====================================================================

        private async UniTask<string> PostJsonAsync(
            string url, string jsonBody, int timeout, CancellationToken ct)
        {
            using var request = new UnityWebRequest(url, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = timeout;
            request.certificateHandler = CreateCertHandler();

            // UniTask: 메인스레드 블로킹 없이 대기
            await request.SendWebRequest().ToUniTask(cancellationToken: ct);

            if (request.result != UnityWebRequest.Result.Success)
            {
                string errorDetail = request.downloadHandler?.text ?? request.error;
                throw new Exception(
                    $"[KoreanAI] POST {url} failed ({request.responseCode}): {errorDetail}");
            }

            return request.downloadHandler.text;
        }

        private async UniTask<string> GetJsonAsync(
            string url, int timeout, CancellationToken ct)
        {
            using var request = UnityWebRequest.Get(url);
            request.timeout = timeout;
            request.certificateHandler = CreateCertHandler();

            await request.SendWebRequest().ToUniTask(cancellationToken: ct);

            if (request.result != UnityWebRequest.Result.Success)
                throw new Exception(
                    $"[KoreanAI] GET {url} failed ({request.responseCode}): {request.error}");

            return request.downloadHandler.text;
        }

        /// <summary>
        /// 설정에 따라 적절한 CertificateHandler 생성
        /// - certPinHash 있으면 → 핀닝 (프로덕션 권장)
        /// - bypassSSL true면 → 우회 (개발 전용)
        /// - 둘 다 아니면 → null (Unity 기본 검증)
        /// </summary>
        private CertificateHandler CreateCertHandler()
        {
            if (!string.IsNullOrEmpty(_config.certPinHash))
                return new PinnedCertHandler(_config.certPinHash);

            if (_config.bypassSSL)
                return new BypassCertHandler();

            return null;
        }

        // ====================================================================
        // JSON 파싱 헬퍼
        // ====================================================================

        private static SimilarityResult ParseSimilarityResult(string json)
        {
            var raw = JObject.Parse(json);
            var result = new SimilarityResult
            {
                success = raw.Value<bool>("success"),
                textA = raw.Value<string>("text_a"),
                textB = raw.Value<string>("text_b"),
                similarity = raw.Value<float?>("similarity") ?? 0f,
                similarityPercent = raw.Value<float?>("similarity_percent") ?? 0f,
                processingTimeSeconds = raw.Value<float?>("processing_time_seconds") ?? 0f,
                error = raw.Value<string>("error"),
                requestId = raw.Value<string>("request_id") ?? ""
            };

            var ds = raw["detailed_scores"];
            if (ds != null && ds.Type != JTokenType.Null)
            {
                result.detailedScores = new DetailedScores
                {
                    biEncoderScore = ds.Value<float>("bi_encoder_score"),
                    crossEncoderScore = ds.Value<float>("cross_encoder_score"),
                    biEncoderWeight = ds.Value<float>("bi_encoder_weight"),
                    crossEncoderWeight = ds.Value<float>("cross_encoder_weight")
                };
            }

            var an = raw["analysis"];
            if (an != null && an.Type != JTokenType.Null)
            {
                result.analysis = new SimilarityAnalysis
                {
                    category = an.Value<string>("category"),
                    categoryDescription = an.Value<string>("category_description"),
                    structureMatch = an.Value<bool>("structure_match"),
                    contextMatch = an.Value<bool>("context_match"),
                    interpretation = an.Value<string>("interpretation")
                };
            }

            return result;
        }
    }
}