using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class CloneVoiceTTSClient : MonoBehaviour
{
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  Inspector
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    [Header("Server")]
    [SerializeField] private string wsUrl = "ws://192.168.0.228:9772/ws/tts";
    [SerializeField] private bool acceptSelfSignedCert = true;
    [SerializeField] private float connectTimeoutSeconds = 3f;
    [SerializeField] private float responseTimeoutSeconds = 120f;

    [Header("Clone Voice")]
    [SerializeField] private string cloneVoiceId;
    [SerializeField] private string language = "Korean";

    [Header("Storage")]
    [Tooltip("StreamingAssets 하위 폴더명")]
    [SerializeField] private string saveFolder = "VoiceClips";
    [SerializeField] private bool usePersistentPath;

    [Header("Text Dictionary  (Key=ID, Value=Text)")]
    [SerializeField] private List<TextPair> textDict = new();

    [Serializable]
    public class TextPair
    {
        public string id;
        [TextArea(1, 4)] public string text;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  State
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private ClientWebSocket _ws;
    private CancellationTokenSource _cts;
    private bool _busy;
    private VoiceClipLibrary _library;
    private AudioSource _audioSource;

    private static CloneVoiceTTSClient _instance;

    public bool IsConnected => _ws?.State == WebSocketState.Open;
    public bool IsBusy => _busy;
    public IReadOnlyList<TextPair> TextEntries => textDict;
    public VoiceClipLibrary Library => _library;

    void Awake()
    {
        _instance = this;
        _library = new VoiceClipLibrary();
        _library.Load();

        _audioSource = GetComponent<AudioSource>();
        if (!_audioSource)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
        }
    }

    /// <summary>라이브러리에 저장된 TTS 클립을 ID로 재생 (static)</summary>
    public static bool PlayTTS(string id)
    {
        if (_instance == null)
        {
            Debug.LogWarning("[TTS] CloneVoiceTTSClient 인스턴스 없음");
            return false;
        }

        var clip = _instance._library?.Get(id);
        if (clip == null)
        {
            Debug.LogWarning($"[TTS] '{id}' 클립 없음");
            return false;
        }

        _instance._audioSource.Stop();
        _instance._audioSource.clip = clip;
        _instance._audioSource.Play();
        return true;
    }

    /// <summary>재생 중지 (static)</summary>
    public static void StopTTS()
    {
        if (_instance != null && _instance._audioSource != null)
            _instance._audioSource.Stop();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  Text Dictionary API
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>텍스트 딕셔너리에 항목 추가 (동일 id면 덮어쓰기)</summary>
    public void AddText(string id, string text)
    {
        if (string.IsNullOrEmpty(id)) { Debug.LogWarning("[TTS] id가 비어있음"); return; }

        var existing = textDict.Find(p => p.id == id);
        if (existing != null)
        {
            existing.text = text;
            return;
        }

        textDict.Add(new TextPair { id = id, text = text });
    }

    /// <summary>텍스트 딕셔너리에 여러 항목 일괄 추가</summary>
    public void AddTexts(IEnumerable<(string id, string text)> items)
    {
        foreach (var (id, text) in items)
            AddText(id, text);
    }

    /// <summary>텍스트 딕셔너리에서 항목 제거</summary>
    public bool RemoveText(string id) => textDict.RemoveAll(p => p.id == id) > 0;

    /// <summary>텍스트 딕셔너리 전체 삭제</summary>
    public void ClearTexts() => textDict.Clear();

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  Public API
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>Inspector textDict 전체를 일괄 생성</summary>
    [ContextMenu("Generate All Clips")]
    public async UniTaskVoid GenerateAll()
    {
        if (_busy) { Debug.LogWarning("[TTS] 이미 생성 중"); return; }
        if (_library == null) { Debug.LogError("[TTS] VoiceClipLibrary 미할당"); return; }
        if (string.IsNullOrEmpty(cloneVoiceId)) { Debug.LogError("[TTS] cloneVoiceId 비어있음"); return; }
        if (textDict.Count == 0) { Debug.LogWarning("[TTS] textDict 비어있음"); return; }

        _busy = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        int ok = 0, fail = 0;

        try
        {
            await Connect(_cts.Token);

            int index = 0;
            foreach (var pair in textDict)
            {
                index++;
                if (_cts.IsCancellationRequested) break;
                if (string.IsNullOrEmpty(pair.id) || string.IsNullOrEmpty(pair.text))
                { fail++; continue; }

                if (_library.HasMatchingEntry(pair.id, pair.text))
                {
                    Debug.Log($"[TTS] [{index}/{textDict.Count}] {pair.id} — 동일 텍스트 존재, skip");
                    ok++;
                    continue;
                }

                Debug.Log($"[TTS] [{index}/{textDict.Count}] {pair.id}");
                var clip = await RequestClip(pair.id, pair.text, _cts.Token);
                if (clip != null) ok++; else fail++;
            }

            Debug.Log($"[TTS] 완료 — 성공 {ok}, 실패 {fail}");
        }
        catch (OperationCanceledException)
        {
            Debug.LogWarning("[TTS] 취소됨 (서버 연결 실패 또는 사용자 취소)");
        }
        catch (WebSocketException ex)
        {
            Debug.LogError($"[TTS] 서버 연결 실패: {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TTS] {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                Debug.LogError($"[TTS] Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
        }
        finally
        {
            await Disconnect();
            _cts?.Dispose();
            _cts = null;
            _busy = false;

            _library?.Save();
        }
    }

    /// <summary>단건 요청 (연결 상태에서 호출). 동일 id+텍스트가 라이브러리에 있으면 기존 클립 반환</summary>
    public async UniTask<AudioClip> RequestClip(string textId, string text, CancellationToken ct = default)
    {
        if (_library != null && _library.HasMatchingEntry(textId, text))
        {
            Debug.Log($"[TTS] '{textId}' 동일 텍스트 존재, skip");
            return _library[textId];
        }

        if (!IsConnected) throw new InvalidOperationException("WebSocket 미연결");

        text = SanitizeText(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            Debug.LogWarning($"[TTS] '{textId}' 전처리 후 빈 텍스트");
            return null;
        }

        var reqId = Guid.NewGuid().ToString("N")[..8];
        var json = BuildRequestJson(reqId, text);

        await Send(json, ct);

        // 서버가 status 등 중간 메시지를 보낼 수 있으므로 response/error 올 때까지 대기
        WsResponse resp;
        while (true)
        {
            resp = await ReceiveResponse(ct);

            if (resp.type == "error")
            {
                Debug.LogError($"[TTS] '{textId}' 에러: {resp.data?.error}");
                return null;
            }
            if (resp.type == "response")
                break;

            Debug.Log($"[TTS] '{textId}' 중간 메시지: {resp.type} — {resp.data?.message ?? resp.data?.status}");
        }

        if (string.IsNullOrEmpty(resp.data?.audio_base64))
        {
            Debug.LogWarning($"[TTS] '{textId}' 응답에 audio_base64 없음");
            return null;
        }

        return await SaveAndRegister(textId, text, resp.data);
    }

    [ContextMenu("Cancel")]
    public void Cancel() => _cts?.Cancel();

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  SSL
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    static bool _sslBypassed;

    static void EnsureSslBypass()
    {
        if (_sslBypassed) return;
        _sslBypassed = true;

        ServicePointManager.SecurityProtocol =
            SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
        ServicePointManager.ServerCertificateValidationCallback = AcceptAllCerts;

        // Mono TLS 환경변수 (ClientWebSocket이 ServicePointManager를 무시하는 경우 대비)
        try { Environment.SetEnvironmentVariable("MONO_TLS_PROVIDER", "btls"); } catch { /* ignore */ }

        Debug.Log("[TTS] SSL bypass 설정 완료");
    }

    static bool AcceptAllCerts(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors errors) => true;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  Connection
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    public async UniTask Connect(CancellationToken ct = default)
    {
        if (IsConnected) return;

        if (acceptSelfSignedCert)
            EnsureSslBypass();

        _ws = new ClientWebSocket();

        using var connectCts = CreateConnectTimeoutToken(ct);
        await _ws.ConnectAsync(new Uri(wsUrl), connectCts.Token);

        var status = await ReceiveResponse(ct);
        Debug.Log($"[TTS] 연결됨 (client_id: {status.data?.client_id})");
    }

    public async UniTask Disconnect()
    {
        if (_ws == null) return;
        try
        {
            if (_ws.State == WebSocketState.Open)
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }
        catch { /* ignore */ }
        finally { _ws.Dispose(); _ws = null; }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  WebSocket I/O
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private async UniTask Send(string json, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private async UniTask<WsResponse> ReceiveResponse(CancellationToken ct)
    {
        using var timeoutCts = CreateResponseTimeoutToken(ct);
        string raw = await ReceiveRaw(timeoutCts.Token);

        if (string.IsNullOrEmpty(raw))
            throw new InvalidOperationException("WebSocket 연결이 닫혔습니다");

        return JsonUtility.FromJson<WsResponse>(raw);
    }

    private async UniTask<string> ReceiveRaw(CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buf = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buf, 0, result.Count);
        } while (!result.EndOfMessage);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private CancellationTokenSource CreateConnectTimeoutToken(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(connectTimeoutSeconds));
        return cts;
    }

    private CancellationTokenSource CreateResponseTimeoutToken(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(responseTimeoutSeconds));
        return cts;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  File I/O + Registration
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private async UniTask<AudioClip> SaveAndRegister(string textId, string sourceText, WsData data)
    {
        byte[] raw = Convert.FromBase64String(data.audio_base64);
        int sr = data.sample_rate > 0 ? data.sample_rate : 24000;
        byte[] wav = EnsureWav(raw, sr);

        string path = GetSavePath(textId);
        await UniTask.RunOnThreadPool(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, wav);
        });
        Debug.Log($"[TTS] 저장: {path} ({wav.Length / 1024}KB)");

        var clip = WavToClip(wav, textId);
        if (clip != null && _library != null)
            _library.Register(textId, sourceText, path, clip);

        return clip;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  JSON
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private string BuildRequestJson(string requestId, string text)
    {
        var req = new WsRequest
        {
            type = "request",
            request_id = requestId,
            data = new WsRequestData
            {
                voice_type = "clone",
                clone_voice_id = cloneVoiceId,
                text = text,
                language = language
            }
        };
        return JsonUtility.ToJson(req);
    }

    [Serializable] private class WsRequest { public string type; public string request_id; public WsRequestData data; }
    [Serializable] private class WsRequestData
    {
        public string voice_type;
        public string clone_voice_id;
        public string text;
        public string language;
    }

    [Serializable] private class WsResponse { public string type; public string request_id; public WsData data; }
    [Serializable] private class WsData
    {
        public string status;
        public string client_id;
        public string audio_base64;
        public int sample_rate;
        public float duration_seconds;
        public string error;
        public string message;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  텍스트 전처리
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex BracketRegex = new(@"[\(\（][^\)\）]*[\)\）]|[\[\［][^\]\］]*[\]\］]|[\{\｛][^\}\｝]*[\}\｝]", RegexOptions.Compiled);

    private static string SanitizeText(string text)
    {
        // /n, \n, \r, \t 리터럴 제거
        text = text.Replace("/n", " ").Replace("\\n", " ").Replace("\\r", " ").Replace("\\t", " ");
        text = text.Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");

        // HTML/리치텍스트 태그 제거 (<b>, <br>, <color=...> 등)
        text = TagRegex.Replace(text, "");

        // 괄호 + 내용 제거: (), [], {}, 전각 포함
        text = BracketRegex.Replace(text, "");

        // 연속 공백 정리
        text = Regex.Replace(text, @"\s{2,}", " ");

        return text.Trim();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  WAV 처리
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private string GetSavePath(string textId)
    {
#if UNITY_EDITOR
        string basePath = usePersistentPath
            ? Application.persistentDataPath
            : Application.streamingAssetsPath;
#else
        string basePath = Application.persistentDataPath;
#endif
        return Path.Combine(basePath, saveFolder, textId + ".wav");
    }

    private static byte[] EnsureWav(byte[] data, int sampleRate)
    {
        if (data.Length > 4 && data[0] == 'R' && data[1] == 'I'
                            && data[2] == 'F' && data[3] == 'F')
            return data;

        return WrapWav(data, sampleRate, 1, 16);
    }

    private static byte[] WrapWav(byte[] pcm, int sampleRate, int channels, int bitsPerSample)
    {
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        int blockAlign = channels * bitsPerSample / 8;

        using var ms = new MemoryStream(44 + pcm.Length);
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + pcm.Length);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);
        w.Write((short)1);
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write((short)blockAlign);
        w.Write((short)bitsPerSample);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(pcm.Length);
        w.Write(pcm);
        return ms.ToArray();
    }

    public static AudioClip WavToClip(byte[] wav, string clipName)
    {
        int audioFormat = BitConverter.ToInt16(wav, 20);
        int channels = BitConverter.ToInt16(wav, 22);
        int sampleRate = BitConverter.ToInt32(wav, 24);
        int bitsPerSample = BitConverter.ToInt16(wav, 34);

        int pos = 12;
        int dataSize = 0;
        while (pos < wav.Length - 8)
        {
            string id = Encoding.ASCII.GetString(wav, pos, 4);
            int size = BitConverter.ToInt32(wav, pos + 4);
            if (id == "data") { pos += 8; dataSize = size; break; }
            pos += 8 + size;
        }
        if (dataSize == 0) { Debug.LogError("[WAV] data 청크 없음"); return null; }

        float[] samples;
        int sampleCount;

        if (audioFormat == 3 && bitsPerSample == 32)
        {
            sampleCount = dataSize / 4;
            samples = new float[sampleCount];
            Buffer.BlockCopy(wav, pos, samples, 0, dataSize);
        }
        else if (audioFormat == 1 && bitsPerSample == 16)
        {
            sampleCount = dataSize / 2;
            samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
                samples[i] = BitConverter.ToInt16(wav, pos + i * 2) / 32768f;
        }
        else
        {
            Debug.LogError($"[WAV] 미지원 포맷: format={audioFormat}, bits={bitsPerSample}");
            return null;
        }

        var clip = AudioClip.Create(clipName, sampleCount / channels, channels, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
