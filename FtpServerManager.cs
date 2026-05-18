using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FubarDev.FtpServer;
using FubarDev.FtpServer.AccountManagement;
using FubarDev.FtpServer.FileSystem.DotNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using FubarDev.FtpServer.Commands;
using FubarDev.FtpServer.CommandHandlers;
using Microsoft.Extensions.Logging;

namespace FubarFTP

{
    /// <summary>
    /// FubarDev.FtpServer 3.1.2 기반 FTP 서버 관리자
    /// </summary>
    public class FtpServerManager : IDisposable
    {
        private readonly FtpConfig _config;
        private ServiceProvider _serviceProvider;
        private IFtpServerHost _ftpHost;
        private IFtpServer _ftpServer;
        private volatile bool _isRunning;

        public bool IsRunning => _isRunning;
        public FtpConfig Config => _config;

        public FtpServerManager(FtpConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// FTP 서버 시작
        /// </summary>
        public async Task StartAsync()
        {
            if (_isRunning)
            {
                Console.WriteLine("[FTP] Server is already running.");
                return;
            }

            // 루트 디렉토리 확인/생성
            EnsureRootDirectory();

            var services = new ServiceCollection();

            services.Configure<DotNetFileSystemOptions>(opt =>
            {
                opt.RootPath = _config.RootPath;
            });

            // 내부 로거를 통해 타임아웃 이벤트 감지
            services.AddLogging(builder =>
            {
                builder.AddProvider(new TimeoutLogProvider());
            });

            // FTP 서버 코어
            services.AddFtpServer(builder => builder.UseDotNetFileSystem());

            // 서버 옵션
            services.Configure<FtpServerOptions>(opt =>
            {
                opt.ServerAddress = _config.ServerAddress;
                opt.Port = _config.Port;
                opt.MaxActiveConnections = _config.MaxConnections;
                opt.ConnectionInactivityCheckInterval = TimeSpan.FromSeconds(10); // 유휴 연결 빠른 감지
            });

            // 유휴 연결(Inactivity) 타임아웃 옵션 설정 (자동으로 421 Timeout 신호를 클라이언트에게 보냄)
            services.Configure<FtpConnectionOptions>(opt =>
            {
                opt.InactivityTimeout = TimeSpan.FromMinutes(_config.IdleTimeoutMinutes);
            });

            // 다운로드(RETR) 로그 추가 미들웨어
            services.AddSingleton<IFtpCommandMiddleware, DownloadLogMiddleware>();

            // Passive 모드 설정
            services.Configure<SimplePasvOptions>(opt =>
            {
                opt.PasvMinPort = _config.PasvMinPort;
                opt.PasvMaxPort = _config.PasvMaxPort;

                // 외부 공개 IP 설정 (NAT/방화벽 뒤에서 사용)
                if (!string.IsNullOrWhiteSpace(_config.PasvPublicAddress))
                {
                    if (System.Net.IPAddress.TryParse(_config.PasvPublicAddress, out var publicIp))
                    {
                        opt.PublicAddress = publicIp;
                        Console.WriteLine($"[FTP] PASV public address: {publicIp}");
                    }
                    else
                    {
                        Console.WriteLine($"[FTP] WARNING: Invalid PASV public address: {_config.PasvPublicAddress}");
                    }
                }
            });

            // 사용자 인증 (AllowAnonymous 또는 Users 목록 기반)
            services.AddSingleton<IMembershipProvider>(new ConfigMembershipProvider(_config));

            _serviceProvider = services.BuildServiceProvider();
            _ftpHost = _serviceProvider.GetRequiredService<IFtpServerHost>();
            _ftpServer = _serviceProvider.GetRequiredService<IFtpServer>();

            // 커넥션/디스커넥션 이벤트 구독
#pragma warning disable 612, 618
            _ftpServer.ConfigureConnection += OnClientConnected;
#pragma warning restore 612, 618

            await _ftpHost.StartAsync(CancellationToken.None);
            _isRunning = true;

            Console.WriteLine($"[FTP] Server started on {_config.ServerAddress}:{_config.Port}");
            Console.WriteLine($"[FTP] Root: {_config.RootPath}");
            Console.WriteLine($"[FTP] PASV ports: {_config.PasvMinPort}-{_config.PasvMaxPort}");
            Console.WriteLine($"[FTP] Max connections: {_config.MaxConnections}");
        }

        /// <summary>
        /// FTP 서버 정지
        /// </summary>
        public async Task StopAsync()
        {
            if (!_isRunning)
            {
                Console.WriteLine("[FTP] Server is not running.");
                return;
            }

            if (_ftpServer != null)
            {
#pragma warning disable 612, 618
                _ftpServer.ConfigureConnection -= OnClientConnected;
#pragma warning restore 612, 618
            }

            if (_ftpHost != null)
            {
                await _ftpHost.StopAsync(CancellationToken.None);
            }

            _serviceProvider?.Dispose();
            _serviceProvider = null;
            _ftpHost = null;
            _ftpServer = null;
            _isRunning = false;

            Console.WriteLine("[FTP] Server stopped.");
        }

        /// <summary>
        /// 서브 폴더 생성
        /// </summary>
        public bool CreateSubFolder(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                Console.WriteLine("[FTP] Folder path cannot be empty.");
                return false;
            }

            // 경로 탐색 방지
            string normalized = relativePath.Replace('\\', '/').TrimStart('/');
            if (normalized.Contains(".."))
            {
                Console.WriteLine("[FTP] Invalid path: directory traversal not allowed.");
                return false;
            }

            string fullPath = Path.Combine(_config.RootPath, normalized);

            if (Directory.Exists(fullPath))
            {
                Console.WriteLine($"[FTP] Folder already exists: {normalized}");
                return true;
            }

            try
            {
                Directory.CreateDirectory(fullPath);
                Console.WriteLine($"[FTP] Created folder: {normalized}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FTP] Failed to create folder: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 폴더 목록 조회
        /// </summary>
        public List<FolderInfo> ListFolders(string relativePath = "")
        {
            var result = new List<FolderInfo>();
            string normalized = (relativePath ?? "").Replace('\\', '/').TrimStart('/');

            if (normalized.Contains(".."))
                return result;

            string targetPath = string.IsNullOrEmpty(normalized)
                ? _config.RootPath
                : Path.Combine(_config.RootPath, normalized);

            if (!Directory.Exists(targetPath))
                return result;

            foreach (var dir in Directory.GetDirectories(targetPath))
            {
                var info = new DirectoryInfo(dir);
                result.Add(new FolderInfo
                {
                    Name = info.Name,
                    FullPath = info.FullName,
                    Created = info.CreationTime,
                    FileCount = info.GetFiles().Length,
                    SubFolderCount = info.GetDirectories().Length
                });
            }

            foreach (var file in Directory.GetFiles(targetPath))
            {
                var info = new FileInfo(file);
                result.Add(new FolderInfo
                {
                    Name = info.Name,
                    FullPath = info.FullName,
                    Created = info.CreationTime,
                    IsFile = true,
                    FileSize = info.Length
                });
            }

            return result;
        }

        /// <summary>
        /// 서버 상태 정보
        /// </summary>
        public ServerStatus GetStatus()
        {
            long totalSize = 0;
            int fileCount = 0;
            int folderCount = 0;

            if (Directory.Exists(_config.RootPath))
            {
                CountRecursive(_config.RootPath, ref fileCount, ref folderCount, ref totalSize);
            }

            return new ServerStatus
            {
                IsRunning = _isRunning,
                Port = _config.Port,
                RootPath = _config.RootPath,
                ServerAddress = _config.ServerAddress,
                TotalFiles = fileCount,
                TotalFolders = folderCount,
                TotalSizeBytes = totalSize
            };
        }

        private void CountRecursive(string path, ref int files, ref int folders, ref long size)
        {
            try
            {
                foreach (var file in Directory.GetFiles(path))
                {
                    files++;
                    size += new FileInfo(file).Length;
                }

                foreach (var dir in Directory.GetDirectories(path))
                {
                    folders++;
                    CountRecursive(dir, ref files, ref folders, ref size);
                }
            }
            catch { }
        }

        private void EnsureRootDirectory()
        {
            if (!Directory.Exists(_config.RootPath))
            {
                Directory.CreateDirectory(_config.RootPath);
                Console.WriteLine($"[FTP] Created root directory: {_config.RootPath}");
            }
        }

        /// <summary>
        /// 클라이언트 접속 시 호출되는 이벤트 핸들러
        /// </summary>
        private void OnClientConnected(object sender, ConnectionEventArgs e)
        {
            var connection = e.Connection;
#pragma warning disable 618
            var remoteAddress = connection.RemoteAddress?.ToString() ?? "Unknown";
#pragma warning restore 618
            Console.WriteLine($"[FTP] Client connected: {remoteAddress}");

            // 디스커넥션 감지를 위해 Closed 이벤트 구독
            connection.Closed += (s, args) =>
            {
                Console.WriteLine($"[FTP] Client disconnected: {remoteAddress}");
            };
        }

        public void Dispose()
        {
            if (_isRunning)
            {
                StopAsync().GetAwaiter().GetResult();
            }
        }
    }

    /// <summary>
    /// 설정 기반 사용자 인증 MembershipProvider
    /// AllowAnonymous=true 이면 모든 접속 허용, false 이면 Users 목록 검증
    /// </summary>
    public class ConfigMembershipProvider : IMembershipProvider
    {
        private readonly FtpConfig _config;

        public ConfigMembershipProvider(FtpConfig config)
        {
            _config = config;
        }

        public Task<MemberValidationResult> ValidateUserAsync(string username, string password)
        {
            // 익명 허용 모드
            if (_config.AllowAnonymous)
                return Authenticated(username);

            // anonymous 사용자는 AllowAnonymous=false 이면 거부
            if (string.Equals(username, "anonymous", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(
                    new MemberValidationResult(MemberValidationStatus.InvalidLogin));
            }

            // 등록된 사용자 검증
            if (_config.Users != null)
            {
                foreach (var user in _config.Users)
                {
                    if (string.Equals(user.Username, username, StringComparison.OrdinalIgnoreCase)
                        && user.Password == password)
                    {
                        return Authenticated(username);
                    }
                }
            }

            Console.WriteLine($"[FTP] Login rejected: {username}");
            return Task.FromResult(
                new MemberValidationResult(MemberValidationStatus.InvalidLogin));
        }

        private static Task<MemberValidationResult> Authenticated(string username)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, "user")
            };
            var identity = new ClaimsIdentity(claims, "ftp");
            var principal = new ClaimsPrincipal(identity);

            return Task.FromResult(
                new MemberValidationResult(MemberValidationStatus.AuthenticatedUser, principal));
        }
    }

    /// <summary>
    /// FTP 서버 설정 — JSON 파일로 저장/로드 지원
    /// </summary>
    public class FtpConfig
    {
        private static readonly string DefaultConfigPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ftpconfig.json");

        /// <summary>
        /// exe 옆의 ftpconfig.json 또는 작업 디렉토리의 ftpconfig.json 탐색
        /// </summary>
        private static string ResolveConfigPath()
        {
            // 1순위: exe가 있는 디렉토리
            if (File.Exists(DefaultConfigPath))
                return DefaultConfigPath;

            // 2순위: 현재 작업 디렉토리 (배치 파일에서 실행 시 다를 수 있음)
            string cwdPath = Path.Combine(Environment.CurrentDirectory, "ftpconfig.json");
            if (File.Exists(cwdPath))
            {
                Console.WriteLine($"[CONFIG] Found config in working directory: {cwdPath}");
                return cwdPath;
            }

            // 기본값: exe 디렉토리에 새로 생성될 경로
            return DefaultConfigPath;
        }

        public string ServerAddress { get; set; } = "0.0.0.0";
        public int Port { get; set; } = 21;
        public string RootPath { get; set; }
        public int PasvMinPort { get; set; } = 49152;
        public int PasvMaxPort { get; set; } = 49200;
        public int MaxConnections { get; set; } = 20;
        public int IdleTimeoutMinutes { get; set; } = 5; // 기본값 5분
        public string PasvPublicAddress { get; set; } = "";
        public bool AllowAnonymous { get; set; } = true;
        public List<FtpUser> Users { get; set; } = new List<FtpUser>();

        [JsonIgnore]
        public string ConfigPath { get; set; }

        public FtpConfig()
        {
            RootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FtpRoot");
            ConfigPath = DefaultConfigPath;
        }

        /// <summary>
        /// 설정을 JSON 파일로 저장
        /// </summary>
        public void Save(string path = null)
        {
            string savePath = path ?? ConfigPath;
            try
            {
                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(savePath, json);
                Console.WriteLine($"[CONFIG] Saved to {savePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CONFIG] Failed to save: {ex.Message}");
            }
        }

        /// <summary>
        /// JSON 파일에서 설정 로드. 파일 없으면 기본값 반환.
        /// </summary>
        public static FtpConfig Load(string path = null)
        {
            string loadPath = path ?? ResolveConfigPath();

            Console.WriteLine($"[CONFIG] BaseDirectory: {AppDomain.CurrentDomain.BaseDirectory}");
            Console.WriteLine($"[CONFIG] WorkingDirectory: {Environment.CurrentDirectory}");
            Console.WriteLine($"[CONFIG] Looking for: {loadPath}");

            if (!File.Exists(loadPath))
            {
                Console.WriteLine($"[CONFIG] No config file found at {loadPath}, using defaults.");
                return new FtpConfig { ConfigPath = loadPath };
            }

            try
            {
                string json = File.ReadAllText(loadPath);
                var config = JsonConvert.DeserializeObject<FtpConfig>(json);
                config.ConfigPath = loadPath;
                Console.WriteLine($"[CONFIG] Loaded from {loadPath}");
                return config;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CONFIG] Failed to load: {ex.Message}, using defaults.");
                return new FtpConfig { ConfigPath = loadPath };
            }
        }
    }

    /// <summary>
    /// FTP 사용자 계정
    /// </summary>
    public class FtpUser
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public class FolderInfo
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public DateTime Created { get; set; }
        public bool IsFile { get; set; }
        public long FileSize { get; set; }
        public int FileCount { get; set; }
        public int SubFolderCount { get; set; }
    }

    public class ServerStatus
    {
        public bool IsRunning { get; set; }
        public int Port { get; set; }
        public string RootPath { get; set; }
        public string ServerAddress { get; set; }
        public int TotalFiles { get; set; }
        public int TotalFolders { get; set; }
        public long TotalSizeBytes { get; set; }
    }

    /// <summary>
    /// 다운로드(RETR)/업로드(STOR) 명령 발생 시 콘솔에 로그를 출력하는 미들웨어
    /// </summary>
    public class DownloadLogMiddleware : IFtpCommandMiddleware
    {
        public async Task InvokeAsync(FtpExecutionContext context, FtpCommandExecutionDelegate next)
        {
            var remoteAddress = context.Connection.RemoteAddress?.ToString() ?? "Unknown";
            var cmd = context.Command.Name.ToUpperInvariant();

            Console.WriteLine($"[FTP] CMD {cmd} {context.Command.Argument} (from {remoteAddress})");

            await next(context);

            Console.WriteLine($"[FTP] CMD {cmd} done (from {remoteAddress})");
        }
    }

    /// <summary>
    /// FubarDev.FtpServer 내부 로그를 모니터링하여 타임아웃 종료를 콘솔에 출력하는 로거 공급자
    /// </summary>
    public class TimeoutLogProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new TimeoutLogger(categoryName);
        }

        public void Dispose() { }
    }

    /// <summary>
    /// 타임아웃 관련 메시지만 필터링하여 콘솔에 출력하는 커스텀 로거
    /// </summary>
    public class TimeoutLogger : ILogger
    {
        private readonly string _categoryName;

        public TimeoutLogger(string categoryName)
        {
            _categoryName = categoryName;
        }

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (formatter == null) return;
            string message = formatter(state, exception);

            if (string.IsNullOrEmpty(message)) return;

            // FubarDev.FtpServer 내부 카테고리만 필터링
            if (_categoryName.StartsWith("FubarDev.FtpServer"))
            {
                // InactivityTimeout 또는 421 Timeout 신호에 대한 로그 검출
                if (message.IndexOf("421 Timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (message.IndexOf("inactive", StringComparison.OrdinalIgnoreCase) >= 0 && message.IndexOf("closing", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    Console.WriteLine($"[FTP] Client disconnected due to timeout: {message}");
                }
            }
        }
    }
}
