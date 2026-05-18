using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Disaster.Network.FTP
{
    /// <summary>
    /// FTP 콘텐츠 타입 — 서브 폴더 매핑
    /// </summary>
    public enum FtpContentType
    {
        Media,
        Replay,
        OriginScenario,
        ConfirmScenario
    }

    /// <summary>
    /// Unity MonoBehaviour FTP 전송 관리자
    /// 타입별 서브 폴더 자동 생성/관리 + 업로드/다운로드
    /// 각 전송 작업마다 연결-해제 수행 (온디맨드 연결)
    /// MonoBehaviour 수명 + 앱 종료 + 외부 CancellationToken 기반 안전 종료
    /// </summary>
    public class FtpTransferManager : MonoBehaviour
    {
        [SerializeField] private string localPath;

        [Header("FTP Server")]
        [SerializeField] private string _host = "localhost";
        [SerializeField] private int _port = 2121;
        [SerializeField] private string _username = "anonymous";
        [SerializeField] private string _password = "";
        [SerializeField] private bool _usePassive = true;

        [Header("Folder Mapping")]
        [SerializeField] private string _mediaFolder = "media";
        [SerializeField] private string _replayFolder = "replay";
        [SerializeField] private string _originScenarioFolder = "origin_scenario";
        [SerializeField] private string _confirmScenarioFolder = "confirm_scenario";

        private FtpClient _client;
        private readonly HashSet<string> _ensuredFolders = new HashSet<string>();

        /// <summary>
        /// 이 매니저의 생명주기 토큰 — OnDestroy 또는 애플리케이션 종료 시 취소
        /// </summary>
        private CancellationTokenSource _lifecycleCts;

        public FtpClient Client => _client;
        public bool IsConnected => _client != null && _client.IsConnected;

        /// <summary>전체 수명 토큰 (외부 공개용)</summary>
        public CancellationToken LifecycleToken => _lifecycleCts?.Token ?? new CancellationToken(true);

        /// <summary>
        /// 전송 진행률 이벤트 (0~1)
        /// </summary>
        public event Action<float> OnProgress;

        /// <summary>
        /// 전송 완료 이벤트 (remotePath, success)
        /// </summary>
        public event Action<string, bool> OnTransferComplete;

        private void Awake()
        {
            var destroyToken = this.GetCancellationTokenOnDestroy();
            var appQuitToken = Application.exitCancellationToken;
            _lifecycleCts = CancellationTokenSource.CreateLinkedTokenSource(destroyToken, appQuitToken);

            _client = new FtpClient(_host, _port, _username, _password, _usePassive);

            localPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "disaster");

            if (!Directory.Exists(localPath))
            {
                Directory.CreateDirectory(localPath);
                Debug.Log($"[FTP] Local directory created: {localPath}");
            }

            foreach (FtpContentType type in Enum.GetValues(typeof(FtpContentType)))
            {
                string subDir = Path.Combine(localPath, GetFolder(type));
                if (!Directory.Exists(subDir))
                {
                    Directory.CreateDirectory(subDir);
                    Debug.Log($"[FTP] Local sub-directory created: {subDir}");
                }
            }
        }

        private void OnDestroy()
        {
            try
            {
                if (_lifecycleCts != null && !_lifecycleCts.IsCancellationRequested)
                    _lifecycleCts.Cancel();
            }
            catch (ObjectDisposedException) { }

            // Dispose() 전에 QUIT 전송 — 소켓 닫기 전 서버에 정상 종료를 알려 슬롯을 즉시 해제시킨다.
            // (cancel 후 finally 블록의 DisconnectAsync가 실행되기 전에 Dispose가 먼저 실행되는
            //  경쟁 조건으로 QUIT이 누락되는 문제를 막기 위함)
            _client?.DisconnectSync();
            _client?.Dispose();

            _lifecycleCts?.Dispose();
            _lifecycleCts = null;
        }

        #region Configuration

        public void Configure(FTPData ftpData)
        {
            _client?.DisconnectSync();
            _client?.Dispose();
            _client = new FtpClient(ftpData);
            _ensuredFolders.Clear();
        }

        public void Configure(string host, int port, string username, string password,
            bool usePassive = true)
        {
            _client?.DisconnectSync();
            _client?.Dispose();
            _client = new FtpClient(host, port, username, password, usePassive);
            _ensuredFolders.Clear();
        }

        #endregion

        #region Connection (수동 제어용)

        public async UniTask ConnectAsync(CancellationToken ct = default)
        {
            using var token = Resolve(ct);
            try
            {
                await _client.ConnectAsync(token.Token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FTP] Connect failed: {ex.Message}");
                throw;
            }
        }

        public async UniTask DisconnectAsync(CancellationToken ct = default)
        {
            using var token = Resolve(ct);
            if (_client != null)
                await _client.DisconnectAsync(token.Token);
        }

        #endregion

        #region Type-based Upload

        /// <summary>
        /// 타입별 서브 폴더에 로컬 파일 업로드
        /// </summary>
        public async UniTask UploadAsync(FtpContentType type, string localPath, string fileName = null,
            CancellationToken ct = default)
        {
            using var token = Resolve(ct);
            await _client.ConnectAsync(token.Token);
            try
            {
                string name = fileName ?? System.IO.Path.GetFileName(localPath);
                string remotePath = await BuildRemotePathAsync(type, name, token.Token);

                bool success = false;
                try
                {
                    var progress = new Progress<float>(p => OnProgress?.Invoke(p));
                    await _client.UploadFileAsync(localPath, remotePath, progress, token.Token);
                    success = true;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.LogError($"[FTP] Upload error ({type}): {ex.Message}");
                }

                OnTransferComplete?.Invoke(remotePath, success);
            }
            finally
            {
                await _client.DisconnectAsync(CancellationToken.None);
            }
        }

        /// <summary>
        /// 타입별 서브 폴더에 byte[] 업로드
        /// </summary>
        public async UniTask UploadBytesAsync(FtpContentType type, byte[] data, string fileName,
            CancellationToken ct = default)
        {
            using var token = Resolve(ct);
            await _client.ConnectAsync(token.Token);
            try
            {
                string remotePath = await BuildRemotePathAsync(type, fileName, token.Token);

                bool success = false;
                try
                {
                    var progress = new Progress<float>(p => OnProgress?.Invoke(p));
                    await _client.UploadBytesAsync(data, remotePath, progress, token.Token);
                    success = true;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.LogError($"[FTP] Upload error ({type}): {ex.Message}");
                }

                OnTransferComplete?.Invoke(remotePath, success);
            }
            finally
            {
                await _client.DisconnectAsync(CancellationToken.None);
            }
        }

        /// <summary>
        /// 타입별 서브 폴더에 문자열(JSON) 업로드
        /// </summary>
        public async UniTask UploadStringAsync(FtpContentType type, string content, string fileName,
            CancellationToken ct = default)
        {
            using var token = Resolve(ct);
            await _client.ConnectAsync(token.Token);
            try
            {
                string remotePath = await BuildRemotePathAsync(type, fileName, token.Token);

                bool success = false;
                try
                {
                    await _client.UploadStringAsync(content, remotePath, token.Token);
                    success = true;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.LogError($"[FTP] Upload error ({type}): {ex.Message}");
                }

                OnTransferComplete?.Invoke(remotePath, success);
            }
            finally
            {
                await _client.DisconnectAsync(CancellationToken.None);
            }
        }

        #endregion

        #region Type-based Download

        /// <summary>
        /// 타입별 서브 폴더에서 파일 다운로드 → 로컬 저장
        /// </summary>
        public async UniTask DownloadAsync(FtpContentType type, string fileName, CancellationToken ct = default)
        {
            using var token = Resolve(ct);
            await _client.ConnectAsync(token.Token);
            try
            {
                fileName = EnsureExtension(type, fileName);
                string folder = GetFolder(type);
                string remotePath = $"{folder}/{fileName}";
                string savePath = Path.Combine(localPath, folder, fileName);

                bool success = false;
                try
                {
                    var progress = new Progress<float>(p => OnProgress?.Invoke(p));
                    await _client.DownloadFileAsync(remotePath, savePath, progress, token.Token);
                    success = true;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.LogError($"[FTP] Download error ({type}): {ex.Message}");
                }

                OnTransferComplete?.Invoke(remotePath, success);
            }
            finally
            {
                await _client.DisconnectAsync(CancellationToken.None);
            }
        }

        /// <summary>
        /// 타입별 서브 폴더에서 파일 다운로드 → byte[] 반환
        /// </summary>
        public async UniTask<byte[]> DownloadBytesAsync(FtpContentType type, string fileName,
            CancellationToken ct = default)
        {
            using var token = Resolve(ct);
            await _client.ConnectAsync(token.Token);
            try
            {
                fileName = EnsureExtension(type, fileName);
                string folder = GetFolder(type);
                string remotePath = $"{folder}/{fileName}";

                try
                {
                    var progress = new Progress<float>(p => OnProgress?.Invoke(p));
                    return await _client.DownloadBytesAsync(remotePath, progress, token.Token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.LogError($"[FTP] Download error ({type}): {ex.Message}");
                    OnTransferComplete?.Invoke(remotePath, false);
                    return null;
                }
            }
            finally
            {
                await _client.DisconnectAsync(CancellationToken.None);
            }
        }

        /// <summary>
        /// 타입별 서브 폴더에서 파일 다운로드 → 문자열 반환
        /// </summary>
        public async UniTask<string> DownloadStringAsync(FtpContentType type, string fileName,
            CancellationToken ct = default)
        {
            using var token = Resolve(ct);
            await _client.ConnectAsync(token.Token);
            try
            {
                fileName = EnsureExtension(type, fileName);
                string folder = GetFolder(type);
                string remotePath = $"{folder}/{fileName}";

                try
                {
                    return await _client.DownloadStringAsync(remotePath, token.Token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.LogError($"[FTP] Download error ({type}): {ex.Message}");
                    return null;
                }
            }
            finally
            {
                await _client.DisconnectAsync(CancellationToken.None);
            }
        }

        #endregion

        #region Type-based Directory

        /// <summary>
        /// 타입별 서브 폴더 파일 목록 조회
        /// </summary>
        public async UniTask<List<string>> ListAsync(FtpContentType type, CancellationToken ct = default)
        {
            using var token = Resolve(ct);
            await _client.ConnectAsync(token.Token);
            try
            {
                string folder = GetFolder(type);
                try
                {
                    return await _client.ListDirectoryAsync(folder, token.Token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.LogError($"[FTP] List error ({type}): {ex.Message}");
                    return new List<string>();
                }
            }
            finally
            {
                await _client.DisconnectAsync(CancellationToken.None);
            }
        }

        /// <summary>
        /// 타입별 서브 폴더 상세 목록
        /// </summary>
        public async UniTask<List<FtpListEntry>> ListDetailsAsync(FtpContentType type, CancellationToken ct = default)
        {
            using var token = Resolve(ct);
            await _client.ConnectAsync(token.Token);
            try
            {
                string folder = GetFolder(type);
                try
                {
                    return await _client.ListDirectoryDetailsAsync(folder, token.Token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.LogError($"[FTP] List error ({type}): {ex.Message}");
                    return new List<FtpListEntry>();
                }
            }
            finally
            {
                await _client.DisconnectAsync(CancellationToken.None);
            }
        }

        /// <summary>
        /// 타입별 서브 폴더에서 파일 삭제
        /// </summary>
        public async UniTask DeleteAsync(FtpContentType type, string fileName, CancellationToken ct = default)
        {
            using var token = Resolve(ct);
            await _client.ConnectAsync(token.Token);
            try
            {
                string folder = GetFolder(type);
                string remotePath = $"{folder}/{fileName}";

                try
                {
                    await _client.DeleteFileAsync(remotePath, token.Token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.LogError($"[FTP] Delete error ({type}): {ex.Message}");
                }
            }
            finally
            {
                await _client.DisconnectAsync(CancellationToken.None);
            }
        }

        #endregion

        #region Raw (non-typed) access

        public async UniTask UploadFileAsync(string localPath, string remotePath, CancellationToken ct = default)
        {
            using var token = Resolve(ct);
            await _client.ConnectAsync(token.Token);
            try
            {
                bool success = false;
                try
                {
                    var progress = new Progress<float>(p => OnProgress?.Invoke(p));
                    await _client.UploadFileAsync(localPath, remotePath, progress, token.Token);
                    success = true;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.LogError($"[FTP] Upload error: {ex.Message}");
                }

                OnTransferComplete?.Invoke(remotePath, success);
            }
            finally
            {
                await _client.DisconnectAsync(CancellationToken.None);
            }
        }

        public async UniTask DownloadFileAsync(string remotePath, string localPath, CancellationToken ct = default)
        {
            using var token = Resolve(ct);
            await _client.ConnectAsync(token.Token);
            try
            {
                bool success = false;
                try
                {
                    var progress = new Progress<float>(p => OnProgress?.Invoke(p));
                    await _client.DownloadFileAsync(remotePath, localPath, progress, token.Token);
                    success = true;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.LogError($"[FTP] Download error: {ex.Message}");
                }

                OnTransferComplete?.Invoke(remotePath, success);
            }
            finally
            {
                await _client.DisconnectAsync(CancellationToken.None);
            }
        }

        public async UniTask<List<string>> ListDirectoryAsync(string remotePath = "", CancellationToken ct = default)
        {
            using var token = Resolve(ct);
            await _client.ConnectAsync(token.Token);
            try
            {
                try
                {
                    return await _client.ListDirectoryAsync(remotePath, token.Token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.LogError($"[FTP] List error: {ex.Message}");
                    return new List<string>();
                }
            }
            finally
            {
                await _client.DisconnectAsync(CancellationToken.None);
            }
        }

        public async UniTask MakeDirectoryAsync(string remotePath, bool recursive = true,
            CancellationToken ct = default)
        {
            using var token = Resolve(ct);
            await _client.ConnectAsync(token.Token);
            try
            {
                try
                {
                    await _client.MakeDirectoryAsync(remotePath, recursive, token.Token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.LogError($"[FTP] MkDir error: {ex.Message}");
                }
            }
            finally
            {
                await _client.DisconnectAsync(CancellationToken.None);
            }
        }

        #endregion

        #region Internal

        private CancellationTokenSource Resolve(CancellationToken external)
        {
            var lifecycle = _lifecycleCts?.Token ?? CancellationToken.None;
            return CancellationTokenSource.CreateLinkedTokenSource(lifecycle, external);
        }

        private string GetFolder(FtpContentType type)
        {
            switch (type)
            {
                case FtpContentType.Media:           return _mediaFolder;
                case FtpContentType.Replay:          return _replayFolder;
                case FtpContentType.OriginScenario:  return _originScenarioFolder;
                case FtpContentType.ConfirmScenario: return _confirmScenarioFolder;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        private string GetDefaultExtension(FtpContentType type)
        {
            switch (type)
            {
                case FtpContentType.Media:           return ".mp4";
                case FtpContentType.Replay:          return ".rpl";
                case FtpContentType.OriginScenario:  return ".json";
                case FtpContentType.ConfirmScenario: return ".json";
                default: return "";
            }
        }

        private string EnsureExtension(FtpContentType type, string fileName)
        {
            if (Path.HasExtension(fileName))
                return fileName;
            return fileName + GetDefaultExtension(type);
        }

        private async UniTask<string> BuildRemotePathAsync(FtpContentType type, string fileName, CancellationToken ct)
        {
            fileName = EnsureExtension(type, fileName);
            string folder = GetFolder(type);

            if (!_ensuredFolders.Contains(folder))
            {
                await _client.MakeDirectoryAsync(folder, recursive: true, ct);
                _ensuredFolders.Add(folder);
                Debug.Log($"[FTP] Ensured folder: {folder}");
            }

            return $"{folder}/{fileName}";
        }

        public string FindLocalFile(FtpContentType type, string fileName)
        {
            fileName = EnsureExtension(type, fileName);
            string folder = GetFolder(type);
            string filePath = Path.Combine(localPath, folder, fileName);
            return File.Exists(filePath) ? filePath : null;
        }

        public bool LocalFileExists(FtpContentType type, string fileName)
        {
            fileName = EnsureExtension(type, fileName);
            string folder = GetFolder(type);
            string filePath = Path.Combine(localPath, folder, fileName);
            return File.Exists(filePath);
        }

        public string GetLocalFolder(FtpContentType type)
        {
            return Path.Combine(localPath, GetFolder(type));
        }

        public string[] GetLocalFiles(FtpContentType type, string searchPattern = "*")
        {
            string dir = GetLocalFolder(type);
            if (!Directory.Exists(dir))
                return Array.Empty<string>();
            return Directory.GetFiles(dir, searchPattern);
        }

        #endregion
    }
}
