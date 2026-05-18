using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Disaster.Network.FTP
{
    /// <summary>
    /// TcpClient 기반 FTP 클라이언트 — FtpWebRequest 대체
    /// UniTask 비동기 처리, Passive 모드 지원, CancellationToken 기반 안전 종료
    /// </summary>
    public class FtpClient : IDisposable
    {
        private TcpClient _controlClient;
        private NetworkStream _controlStream;
        private StreamReader _reader;
        private StreamWriter _writer;

        private readonly string _host;
        private readonly int _port;
        private readonly string _username;
        private readonly string _password;
        private readonly bool _usePassive;

        private bool _connected;
        private bool _disposed;

        /// <summary>인스턴스 수명 전용 CTS. Dispose 시 취소되어 진행 중인 모든 I/O를 중단시킨다.</summary>
        private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();

        public bool IsConnected => _connected && _controlClient != null && _controlClient.Connected;

        /// <summary>
        /// FTPData 기반 생성자
        /// </summary>
        public FtpClient(FTPData ftpData)
        {
            var uri = new Uri(ftpData.ftpBaseUri);
            _host = uri.Host;
            _port = uri.Port > 0 ? uri.Port : 21;
            _username = ftpData.userName;
            _password = ftpData.password;
            _usePassive = ftpData.usePassive;
        }

        /// <summary>
        /// 직접 지정 생성자
        /// </summary>
        public FtpClient(string host, int port = 21, string username = "anonymous",
            string password = "", bool usePassive = true)
        {
            _host = host;
            _port = port;
            _username = username;
            _password = password;
            _usePassive = usePassive;
        }

        #region Connection

        /// <summary>
        /// FTP 서버 연결 + 로그인
        /// </summary>
        public async UniTask ConnectAsync(CancellationToken ct = default)
        {
            if (_connected) return;
            ThrowIfDisposed();

            using var linked = LinkToken(ct);
            var token = linked.Token;

            _controlClient = new TcpClient();

            // 취소 시 소켓을 강제로 닫아야 블로킹된 ConnectAsync/ReadAsync가 즉시 풀린다.
            using (token.Register(AbortControlSocket))
            {
                try
                {
                    await _controlClient.ConnectAsync(_host, _port).AsUniTask()
                        .AttachExternalCancellation(token);

                    _controlStream = _controlClient.GetStream();
                    var utf8NoBom = new UTF8Encoding(false);
                    _reader = new StreamReader(_controlStream, utf8NoBom);
                    _writer = new StreamWriter(_controlStream, utf8NoBom) { AutoFlush = true };

                    // 서버 환영 메시지 읽기
                    var welcome = await ReadResponseAsync(token);
                    if (!welcome.StartsWith("220"))
                        throw new FtpException($"Connection failed: {welcome}");

                    // 로그인
                    await SendCommandAsync($"USER {_username}", token);
                    var userResp = await ReadResponseAsync(token);

                    if (userResp.StartsWith("331"))
                    {
                        await SendCommandAsync($"PASS {_password}", token);
                        var passResp = await ReadResponseAsync(token);
                        if (!passResp.StartsWith("230"))
                            throw new FtpException($"Login failed: {passResp}");
                    }
                    else if (!userResp.StartsWith("230"))
                    {
                        throw new FtpException($"Login failed: {userResp}");
                    }

                    // Binary 모드 설정
                    await SendCommandAsync("TYPE I", token);
                    await ReadResponseAsync(token);

                    _connected = true;
                    Debug.Log($"[FTP] Connected to {_host}:{_port}");
                }
                catch (Exception) when (token.IsCancellationRequested)
                {
                    // 취소로 인한 예외는 OperationCanceledException으로 통일
                    Cleanup();
                    throw new OperationCanceledException(token);
                }
                catch
                {
                    Cleanup();
                    throw;
                }
            }
        }

        /// <summary>
        /// 연결 해제 (QUIT 전송 후 소켓 정리)
        /// </summary>
        public async UniTask DisconnectAsync(CancellationToken ct = default)
        {
            if (!_connected) return;

            try
            {
                // QUIT은 최대 2초만 대기 — 서버가 응답 못하면 포기
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));

                await SendCommandAsync("QUIT", timeout.Token);
                await ReadResponseAsync(timeout.Token);
            }
            catch { /* 종료 중 오류는 무시 */ }

            Cleanup();
            Debug.Log("[FTP] Disconnected");
        }

        #endregion

        #region Download

        /// <summary>
        /// 파일 다운로드 → 로컬 경로에 저장
        /// </summary>
        public async UniTask DownloadFileAsync(string remotePath, string localPath,
            IProgress<float> progress = null, CancellationToken ct = default)
        {
            EnsureConnected();
            using var linked = LinkToken(ct);
            var token = linked.Token;

            // 경로에 디렉토리가 포함된 경우 CWD로 이동 후 파일명만으로 RETR
            string remoteDir = null;
            string remoteFile = remotePath;
            int lastSlash = remotePath.Replace('\\', '/').LastIndexOf('/');
            if (lastSlash >= 0)
            {
                remoteDir = remotePath.Substring(0, lastSlash);
                remoteFile = remotePath.Substring(lastSlash + 1);
                await ChangeDirectoryAsync(remoteDir, token);
            }

            try
            {
                long fileSize = await GetFileSizeAsync(remoteFile, token);

                using (var dataStream = await OpenDataConnectionAsync(token))
                using (token.Register(() => SafeClose(dataStream)))
                {
                    await SendCommandAsync($"RETR {remoteFile}", token);
                    var resp = await ReadResponseAsync(token);
                    if (!resp.StartsWith("150") && !resp.StartsWith("125"))
                        throw new FtpException($"Download failed: {resp}");

                    var dir = Path.GetDirectoryName(localPath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);

                    using (var fs = File.Create(localPath))
                    {
                        var buffer = new byte[8192];
                        long totalRead = 0;
                        int read;

                        while ((read = await dataStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                        {
                            await fs.WriteAsync(buffer, 0, read, token);
                            totalRead += read;

                            if (fileSize > 0)
                                progress?.Report((float)totalRead / fileSize);
                        }
                    }
                }

                var finalResp = await ReadResponseAsync(token);
                if (!finalResp.StartsWith("226"))
                    Debug.LogWarning($"[FTP] Download transfer response: {finalResp}");

                progress?.Report(1f);
                Debug.Log($"[FTP] Downloaded: {remotePath} -> {localPath}");
            }
            finally
            {
                if (remoteDir != null && IsConnected)
                {
                    // 취소 중이어도 루트 복귀는 시도 (실패 무시)
                    try { await ChangeDirectoryAsync("/", CancellationToken.None); }
                    catch { }
                }
            }
        }

        /// <summary>
        /// 파일 다운로드 → byte[] 반환
        /// </summary>
        public async UniTask<byte[]> DownloadBytesAsync(string remotePath,
            IProgress<float> progress = null, CancellationToken ct = default)
        {
            EnsureConnected();
            using var linked = LinkToken(ct);
            var token = linked.Token;

            string dir = null;
            string fileName = remotePath;
            int lastSlash = remotePath.Replace('\\', '/').LastIndexOf('/');
            if (lastSlash >= 0)
            {
                dir = remotePath.Substring(0, lastSlash);
                fileName = remotePath.Substring(lastSlash + 1);
                await ChangeDirectoryAsync(dir, token);
            }

            try
            {
                long fileSize = await GetFileSizeAsync(fileName, token);

                using (var dataStream = await OpenDataConnectionAsync(token))
                using (token.Register(() => SafeClose(dataStream)))
                {
                    await SendCommandAsync($"RETR {fileName}", token);
                    var resp = await ReadResponseAsync(token);
                    if (!resp.StartsWith("150") && !resp.StartsWith("125"))
                        throw new FtpException($"Download failed: {resp}");

                    using (var ms = new MemoryStream())
                    {
                        var buffer = new byte[8192];
                        long totalRead = 0;
                        int read;

                        while ((read = await dataStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                        {
                            ms.Write(buffer, 0, read);
                            totalRead += read;

                            if (fileSize > 0)
                                progress?.Report((float)totalRead / fileSize);
                        }

                        var finalResp = await ReadResponseAsync(token);
                        progress?.Report(1f);
                        return ms.ToArray();
                    }
                }
            }
            finally
            {
                if (dir != null && IsConnected)
                {
                    try { await ChangeDirectoryAsync("/", CancellationToken.None); }
                    catch { }
                }
            }
        }

        /// <summary>
        /// 파일 다운로드 → 문자열 반환
        /// </summary>
        public async UniTask<string> DownloadStringAsync(string remotePath, CancellationToken ct = default)
        {
            var bytes = await DownloadBytesAsync(remotePath, null, ct);
            return Encoding.UTF8.GetString(bytes);
        }

        #endregion

        #region Upload

        /// <summary>
        /// 로컬 파일 업로드
        /// </summary>
        public async UniTask UploadFileAsync(string localPath, string remotePath,
            IProgress<float> progress = null, CancellationToken ct = default)
        {
            EnsureConnected();
            using var linked = LinkToken(ct);
            var token = linked.Token;

            if (!File.Exists(localPath))
                throw new FileNotFoundException($"Local file not found: {localPath}");

            string dir = null;
            string fileName = remotePath;
            int lastSlash = remotePath.Replace('\\', '/').LastIndexOf('/');
            if (lastSlash >= 0)
            {
                dir = remotePath.Substring(0, lastSlash);
                fileName = remotePath.Substring(lastSlash + 1);
                await ChangeDirectoryAsync(dir, token);
            }

            try
            {
                var fileInfo = new FileInfo(localPath);
                long fileSize = fileInfo.Length;

                using (var dataStream = await OpenDataConnectionAsync(token))
                using (token.Register(() => SafeClose(dataStream)))
                {
                    await SendCommandAsync($"STOR {fileName}", token);
                    var resp = await ReadResponseAsync(token);
                    if (!resp.StartsWith("150") && !resp.StartsWith("125"))
                        throw new FtpException($"Upload failed: {resp}");

                    using (var fs = File.OpenRead(localPath))
                    {
                        var buffer = new byte[8192];
                        long totalSent = 0;
                        int read;

                        while ((read = await fs.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                        {
                            await dataStream.WriteAsync(buffer, 0, read, token);
                            totalSent += read;

                            if (fileSize > 0)
                                progress?.Report((float)totalSent / fileSize);
                        }
                    }
                }

                var finalResp = await ReadResponseAsync(token);
                if (!finalResp.StartsWith("226"))
                    Debug.LogWarning($"[FTP] Upload transfer response: {finalResp}");

                progress?.Report(1f);
                Debug.Log($"[FTP] Uploaded: {localPath} -> {remotePath}");
            }
            finally
            {
                if (dir != null && IsConnected)
                {
                    try { await ChangeDirectoryAsync("/", CancellationToken.None); }
                    catch { }
                }
            }
        }

        /// <summary>
        /// byte[] 데이터 업로드
        /// </summary>
        public async UniTask UploadBytesAsync(byte[] data, string remotePath,
            IProgress<float> progress = null, CancellationToken ct = default)
        {
            EnsureConnected();
            using var linked = LinkToken(ct);
            var token = linked.Token;

            string dir = null;
            string fileName = remotePath;
            int lastSlash = remotePath.Replace('\\', '/').LastIndexOf('/');
            if (lastSlash >= 0)
            {
                dir = remotePath.Substring(0, lastSlash);
                fileName = remotePath.Substring(lastSlash + 1);
                await ChangeDirectoryAsync(dir, token);
            }

            try
            {
                using (var dataStream = await OpenDataConnectionAsync(token))
                using (token.Register(() => SafeClose(dataStream)))
                {
                    await SendCommandAsync($"STOR {fileName}", token);
                    var resp = await ReadResponseAsync(token);
                    if (!resp.StartsWith("150") && !resp.StartsWith("125"))
                        throw new FtpException($"Upload failed: {resp}");

                    int chunkSize = 8192;
                    long totalSent = 0;

                    for (int offset = 0; offset < data.Length; offset += chunkSize)
                    {
                        token.ThrowIfCancellationRequested();

                        int count = Math.Min(chunkSize, data.Length - offset);
                        await dataStream.WriteAsync(data, offset, count, token);
                        totalSent += count;
                        progress?.Report((float)totalSent / data.Length);
                    }
                }

                var finalResp = await ReadResponseAsync(token);
                progress?.Report(1f);
                Debug.Log($"[FTP] Uploaded bytes -> {remotePath} ({data.Length} bytes)");
            }
            finally
            {
                if (dir != null && IsConnected)
                {
                    try { await ChangeDirectoryAsync("/", CancellationToken.None); }
                    catch { }
                }
            }
        }

        /// <summary>
        /// 문자열 데이터 업로드 (JSON 등)
        /// </summary>
        public async UniTask UploadStringAsync(string content, string remotePath, CancellationToken ct = default)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            await UploadBytesAsync(bytes, remotePath, null, ct);
        }

        #endregion

        #region Directory Operations

        /// <summary>
        /// 디렉토리 목록 조회 (파일명 리스트)
        /// </summary>
        public async UniTask<List<string>> ListDirectoryAsync(string remotePath = "", CancellationToken ct = default)
        {
            EnsureConnected();
            using var linked = LinkToken(ct);
            var token = linked.Token;

            var result = new List<string>();

            using (var dataStream = await OpenDataConnectionAsync(token))
            using (token.Register(() => SafeClose(dataStream)))
            {
                string cmd = string.IsNullOrEmpty(remotePath) ? "NLST" : $"NLST {remotePath}";
                await SendCommandAsync(cmd, token);
                var resp = await ReadResponseAsync(token);

                // 550 = 빈 디렉토리
                if (resp.StartsWith("550"))
                    return result;

                if (!resp.StartsWith("150") && !resp.StartsWith("125"))
                    throw new FtpException($"List failed: {resp}");

                using (var sr = new StreamReader(dataStream, Encoding.UTF8))
                {
                    string line;
                    while ((line = await ReadLineWithCancelAsync(sr, token)) != null)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            result.Add(line.Trim());
                    }
                }
            }

            await ReadResponseAsync(token); // 226
            return result;
        }

        /// <summary>
        /// 디렉토리 상세 목록 (LIST)
        /// </summary>
        public async UniTask<List<FtpListEntry>> ListDirectoryDetailsAsync(string remotePath = "", CancellationToken ct = default)
        {
            EnsureConnected();
            using var linked = LinkToken(ct);
            var token = linked.Token;

            var result = new List<FtpListEntry>();

            using (var dataStream = await OpenDataConnectionAsync(token))
            using (token.Register(() => SafeClose(dataStream)))
            {
                string cmd = string.IsNullOrEmpty(remotePath) ? "LIST" : $"LIST {remotePath}";
                await SendCommandAsync(cmd, token);
                var resp = await ReadResponseAsync(token);

                if (resp.StartsWith("550"))
                    return result;

                if (!resp.StartsWith("150") && !resp.StartsWith("125"))
                    throw new FtpException($"List failed: {resp}");

                using (var sr = new StreamReader(dataStream, Encoding.UTF8))
                {
                    string line;
                    while ((line = await ReadLineWithCancelAsync(sr, token)) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        var entry = ParseListLine(line);
                        if (entry != null)
                            result.Add(entry);
                    }
                }
            }

            await ReadResponseAsync(token); // 226
            return result;
        }

        /// <summary>
        /// 디렉토리 생성 (재귀적)
        /// </summary>
        public async UniTask MakeDirectoryAsync(string remotePath, bool recursive = true, CancellationToken ct = default)
        {
            EnsureConnected();
            using var linked = LinkToken(ct);
            var token = linked.Token;

            if (recursive)
            {
                var parts = remotePath.Replace('\\', '/').Trim('/').Split('/');
                string current = "";

                foreach (var part in parts)
                {
                    token.ThrowIfCancellationRequested();
                    current = string.IsNullOrEmpty(current) ? part : $"{current}/{part}";
                    await TryMakeDirectoryAsync(current, token);
                }
            }
            else
            {
                await TryMakeDirectoryAsync(remotePath, token);
            }
        }

        /// <summary>
        /// 디렉토리 삭제
        /// </summary>
        public async UniTask RemoveDirectoryAsync(string remotePath, CancellationToken ct = default)
        {
            EnsureConnected();
            using var linked = LinkToken(ct);
            var token = linked.Token;

            await SendCommandAsync($"RMD {remotePath}", token);
            var resp = await ReadResponseAsync(token);
            if (!resp.StartsWith("250"))
                throw new FtpException($"Remove directory failed: {resp}");
        }

        /// <summary>
        /// 파일 삭제
        /// </summary>
        public async UniTask DeleteFileAsync(string remotePath, CancellationToken ct = default)
        {
            EnsureConnected();
            using var linked = LinkToken(ct);
            var token = linked.Token;

            await SendCommandAsync($"DELE {remotePath}", token);
            var resp = await ReadResponseAsync(token);
            if (!resp.StartsWith("250"))
                throw new FtpException($"Delete failed: {resp}");
        }

        /// <summary>
        /// NOOP 명령으로 연결 유효성 확인. 실패하면 내부 상태를 초기화하고 false 반환.
        /// </summary>
        public async UniTask<bool> CheckAliveAsync(CancellationToken ct = default)
        {
            if (!IsConnected) return false;
            try
            {
                using var linked = LinkToken(ct);
                await SendCommandAsync("NOOP", linked.Token);
                var resp = await ReadResponseAsync(linked.Token);
                return resp.StartsWith("200");
            }
            catch
            {
                Cleanup(); // _connected = false, 소켓 정리
                return false;
            }
        }

        /// <summary>
        /// 파일 크기 조회
        /// </summary>
        public async UniTask<long> GetFileSizeAsync(string remotePath, CancellationToken ct = default)
        {
            EnsureConnected();
            using var linked = LinkToken(ct);
            var token = linked.Token;

            await SendCommandAsync($"SIZE {remotePath}", token);
            var resp = await ReadResponseAsync(token);

            if (resp.StartsWith("213") && long.TryParse(resp.Substring(4).Trim(), out long size))
                return size;

            return -1; // 크기 알 수 없음
        }

        /// <summary>
        /// 파일/디렉토리 존재 확인
        /// </summary>
        public async UniTask<bool> ExistsAsync(string remotePath, CancellationToken ct = default)
        {
            EnsureConnected();
            using var linked = LinkToken(ct);
            var token = linked.Token;

            await SendCommandAsync($"SIZE {remotePath}", token);
            var resp = await ReadResponseAsync(token);

            if (resp.StartsWith("213"))
                return true;

            // 디렉토리 확인
            try
            {
                await SendCommandAsync($"CWD {remotePath}", token);
                var cwdResp = await ReadResponseAsync(token);
                if (cwdResp.StartsWith("250"))
                {
                    // 원래 디렉토리로 복귀
                    await SendCommandAsync("CWD /", token);
                    await ReadResponseAsync(token);
                    return true;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }

            return false;
        }

        #endregion

        #region Internal

        /// <summary>
        /// 외부 토큰과 인스턴스 수명 토큰을 묶은 linked CTS 생성.
        /// Dispose/Cancel 중 어느 쪽이든 취소되면 작업이 중단된다.
        /// </summary>
        private CancellationTokenSource LinkToken(CancellationToken ct)
        {
            return CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, ct);
        }

        /// <summary>
        /// 작업 디렉토리 변경 (CWD)
        /// </summary>
        private async UniTask ChangeDirectoryAsync(string path, CancellationToken ct)
        {
            await SendCommandAsync($"CWD {path}", ct);
            var resp = await ReadResponseAsync(ct);
            if (!resp.StartsWith("250"))
                throw new FtpException($"CWD failed: {resp}");
        }

        private async UniTask TryMakeDirectoryAsync(string path, CancellationToken ct)
        {
            await SendCommandAsync($"MKD {path}", ct);
            var resp = await ReadResponseAsync(ct);
            // 257 = 생성됨, 550 = 이미 존재
            if (!resp.StartsWith("257") && !resp.StartsWith("550"))
                throw new FtpException($"MKD failed: {resp}");
        }

        private async UniTask<Stream> OpenDataConnectionAsync(CancellationToken ct)
        {
            if (_usePassive)
                return await OpenPassiveConnectionAsync(ct);
            else
                throw new NotSupportedException("Active mode is not supported. Use passive mode.");
        }

        private async UniTask<Stream> OpenPassiveConnectionAsync(CancellationToken ct)
        {
            await SendCommandAsync("PASV", ct);
            var resp = await ReadResponseAsync(ct);

            if (!resp.StartsWith("227"))
                throw new FtpException($"PASV failed: {resp}");

            // "227 Entering Passive Mode (h1,h2,h3,h4,p1,p2)"
            var match = Regex.Match(resp, @"\((\d+),(\d+),(\d+),(\d+),(\d+),(\d+)\)");
            if (!match.Success)
                throw new FtpException($"Cannot parse PASV response: {resp}");

            string dataHost = $"{match.Groups[1]}.{match.Groups[2]}.{match.Groups[3]}.{match.Groups[4]}";
            int dataPort = int.Parse(match.Groups[5].Value) * 256 + int.Parse(match.Groups[6].Value);

            // PASV 주소가 0.0.0.0 이면 원래 호스트 사용
            if (dataHost == "0.0.0.0")
                dataHost = _host;

            var dataClient = new TcpClient();

            // 데이터 연결도 취소 시 즉시 닫히도록 등록
            using (ct.Register(() => { try { dataClient.Close(); } catch { } }))
            {
                try
                {
                    await dataClient.ConnectAsync(dataHost, dataPort).AsUniTask()
                        .AttachExternalCancellation(ct);
                }
                catch (Exception) when (ct.IsCancellationRequested)
                {
                    throw new OperationCanceledException(ct);
                }
            }

            return new FtpDataStream(dataClient);
        }

        private async UniTask SendCommandAsync(string command, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // WriteLineAsync는 CancellationToken 오버로드가 Unity Mono에 없다.
            // AttachExternalCancellation으로 UniTask 레벨에서 취소, 실제 소켓은 Dispose/ct.Register로 닫힘.
            await _writer.WriteLineAsync(command).AsUniTask().AttachExternalCancellation(ct);
            await _writer.FlushAsync().AsUniTask().AttachExternalCancellation(ct);
        }

        private async UniTask<string> ReadResponseAsync(CancellationToken ct)
        {
            var sb = new StringBuilder();
            string line = await ReadLineWithCancelAsync(_reader, ct);

            if (line == null)
                throw new FtpException("Connection closed by server.");

            sb.AppendLine(line);

            // 멀티라인 응답 처리 (예: "220-welcome\r\n220 ready")
            if (line.Length >= 4 && line[3] == '-')
            {
                string code = line.Substring(0, 3);
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    line = await ReadLineWithCancelAsync(_reader, ct);
                    if (line == null) break;
                    sb.AppendLine(line);

                    if (line.StartsWith(code + " "))
                        break;
                }
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// StreamReader.ReadLineAsync는 토큰 오버로드가 없으므로 UniTask 레벨에서 취소를 결합한다.
        /// 실제로 블로킹된 Read는 소켓이 닫혀야 풀리므로 token.Register에서 소켓을 닫는 쪽이 병행 동작한다.
        /// </summary>
        private static async UniTask<string> ReadLineWithCancelAsync(StreamReader reader, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return await reader.ReadLineAsync().AsUniTask().AttachExternalCancellation(ct);
        }

        private FtpListEntry ParseListLine(string line)
        {
            // Unix 형식: "drwxr-xr-x 2 user group 4096 Mar 17 10:00 dirname"
            // Windows 형식: "03-17-26  10:00AM       <DIR>          dirname"
            var entry = new FtpListEntry();

            if (line.Length > 0 && (line[0] == 'd' || line[0] == '-' || line[0] == 'l'))
            {
                entry.IsDirectory = line[0] == 'd';
                var parts = Regex.Split(line, @"\s+");
                if (parts.Length >= 9)
                {
                    entry.Name = string.Join(" ", parts, 8, parts.Length - 8);
                    if (long.TryParse(parts[4], out long size))
                        entry.Size = size;
                }
                else return null;
            }
            else if (line.Contains("<DIR>"))
            {
                entry.IsDirectory = true;
                int idx = line.IndexOf("<DIR>", StringComparison.OrdinalIgnoreCase);
                entry.Name = line.Substring(idx + 5).Trim();
                entry.Size = 0;
            }
            else
            {
                entry.IsDirectory = false;
                var parts = Regex.Split(line.Trim(), @"\s+");
                if (parts.Length >= 4)
                {
                    entry.Name = string.Join(" ", parts, 3, parts.Length - 3);
                    if (long.TryParse(parts[2], out long size))
                        entry.Size = size;
                }
                else return null;
            }

            if (entry.Name == "." || entry.Name == "..") return null;
            return entry;
        }

        private void EnsureConnected()
        {
            ThrowIfDisposed();
            if (!_connected || _controlClient == null || !_controlClient.Connected)
                throw new InvalidOperationException("Not connected. Call ConnectAsync() first.");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FtpClient));
        }

        /// <summary>
        /// 제어 소켓 강제 종료 — 블로킹된 I/O를 즉시 풀기 위해 예외 없이 Close만 수행
        /// </summary>
        private void AbortControlSocket()
        {
            try { _controlClient?.Close(); } catch { }
        }

        private static void SafeClose(Stream stream)
        {
            try { stream?.Dispose(); } catch { }
        }

        private void Cleanup()
        {
            _connected = false;
            try { _reader?.Dispose(); } catch { }
            try { _writer?.Dispose(); } catch { }
            try { _controlStream?.Dispose(); } catch { }
            try { _controlClient?.Close(); } catch { }
            _reader = null;
            _writer = null;
            _controlStream = null;
            _controlClient = null;
        }

        /// <summary>
        /// OnDestroy 등 async를 쓸 수 없는 컨텍스트 전용 동기 종료.
        /// QUIT을 전송한 뒤 소켓을 정리한다. Dispose() 호출 전에 사용할 것.
        /// </summary>
        public void DisconnectSync()
        {
            if (!_connected) return;
            try
            {
                _writer?.WriteLine("QUIT");
                _writer?.Flush();
                // 221 응답을 1초 내에서 best-effort로 읽어 서버가 슬롯을 즉시 해제하도록 유도
                if (_controlClient != null)
                    _controlClient.ReceiveTimeout = 1000;
                _reader?.ReadLine();
            }
            catch { }
            Cleanup();
        }

        /// <summary>
        /// 인스턴스 파괴 — 진행 중인 모든 I/O를 취소하고 소켓을 정리한다.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // 먼저 토큰을 취소해서 진행 중인 작업에 예외를 전파
            try
            {
                if (!_disposeCts.IsCancellationRequested)
                    _disposeCts.Cancel();
            }
            catch (ObjectDisposedException) { }

            Cleanup();
            _disposeCts.Dispose();
        }

        #endregion
    }

    /// <summary>
    /// FTP 데이터 연결 스트림 — TcpClient 생명주기 관리
    /// </summary>
    internal class FtpDataStream : Stream
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;

        public FtpDataStream(TcpClient client)
        {
            _client = client;
            _stream = client.GetStream();
        }

        public override bool CanRead => _stream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => _stream.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _stream.Read(buffer, offset, count);

        public override void Write(byte[] buffer, int offset, int count) =>
            _stream.Write(buffer, offset, count);

        public override System.Threading.Tasks.Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _stream.ReadAsync(buffer, offset, count, cancellationToken);

        public override System.Threading.Tasks.Task WriteAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _stream.WriteAsync(buffer, offset, count, cancellationToken);

        public override void Flush() => _stream.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _stream?.Dispose(); } catch { }
                try { _client?.Close(); } catch { }
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// LIST 응답 파싱 결과
    /// </summary>
    public class FtpListEntry
    {
        public string Name;
        public bool IsDirectory;
        public long Size;
    }

    /// <summary>
    /// FTP 프로토콜 에러
    /// </summary>
    public class FtpException : Exception
    {
        public FtpException(string message) : base(message) { }
    }
}