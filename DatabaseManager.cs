using DarkRift;
using DarkRift.Server;
using DatabasePlugin;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DatabasePlugin
{
    /// <summary>
    /// DarkRift2 데이터베이스 플러그인
    /// Account 테이블 CRUD 처리 + 세션 관리 (스레드 안전)
    /// </summary>
    public class DatabaseManager : Plugin
    {
        public override Version Version => new Version(2, 1, 0);
        public override bool ThreadSafe => true;

        private SQLiteManager dbManager;
        public SQLiteManager Db => dbManager;
        private string dbPath;

        // 세션 관리
        private SessionManager sessionManager;

        // 초기화 상태 관리
        private volatile bool isInitialized = false;
        private readonly SemaphoreSlim initSemaphore = new SemaphoreSlim(1, 1);
        private Task initTask;

        // 태그 정의 (MessageTags에서 참조)
        private const ushort TAG_LOGIN = MessageTags.Login;
        private const ushort TAG_REGISTER = MessageTags.Register;
        private const ushort TAG_GET_ACCOUNT = MessageTags.GetAccount;
        private const ushort TAG_UPDATE_ACCOUNT = MessageTags.UpdateAccount;
        private const ushort TAG_DELETE_ACCOUNT = MessageTags.DeleteAccount;
        private const ushort TAG_CHANGE_PASSWORD = MessageTags.ChangePassword;
        private const ushort TAG_GET_TYPE_ACCOUNTS = MessageTags.GetTypeAccounts;

        // 관리자 알림 태그 (MessageTags에서 참조)
        private const ushort TAG_ADMIN_USER_LOGIN_NOTIFICATION = MessageTags.AdminUserLoginNotification;
        private const ushort TAG_ADMIN_USER_LOGOUT_NOTIFICATION = MessageTags.AdminUserLogoutNotification;
        private const ushort TAG_ADMIN_GET_ONLINE_USERS = MessageTags.AdminGetOnlineUsers;

        // 응답 코드
        public enum ResponseCode : byte
        {
            Success = 0,
            Failed = 1,
            AlreadyExists = 2,
            NotFound = 3,
            InvalidCredentials = 4,
            DatabaseError = 5,
            WeakPassword = 6,
            InvalidData = 7,
            NotInitialized = 8
        }

        public DatabaseManager(PluginLoadData pluginLoadData) : base(pluginLoadData)
        {
            dbPath = "./DB/database.db";  // 기본값

            // 세션 관리자 초기화
            sessionManager = new SessionManager();

            ClientManager.ClientConnected += OnClientConnected;
            ClientManager.ClientDisconnected += OnClientDisconnected;

            // 비동기 초기화 시작 (백그라운드)
            initTask = Task.Run(async () =>
            {
                try
                {
                    await InitializeDatabaseAsync();

                    // 초기화 완료 확인
                    Logger.Info($"Initialization check - _isInitialized: {isInitialized}, _dbManager: {(dbManager != null ? "OK" : "NULL")}");
                }
                catch (Exception ex)
                {
                    Logger.Fatal($"Failed to initialize database: {ex.Message}");
                    Logger.Fatal($"Stack trace: {ex.StackTrace}");
                }
            });

            Logger.Info("DatabasePlugin constructor completed");
        }

        // 0=미시작, 1=진행중, 2=완료
        private int _dependencyInjectionState = 0;

        /// <summary>
        /// 의존성 주입을 지연 실행 (첫 클라이언트 연결 시 또는 타이머로)
        /// DarkRift는 생성자에서 PluginManager.GetPluginByType 사용을 금지함
        /// LoadedEventArgs가 internal이므로 Loaded 오버라이드 대신 지연 주입 패턴 사용
        /// Interlocked.CompareExchange로 원자적 플래그 관리 (스레드 경쟁 방지)
        /// </summary>
        private void EnsureDependenciesInjected()
        {
            if (Interlocked.CompareExchange(ref _dependencyInjectionState, 1, 0) != 0)
                return; // 다른 스레드가 이미 시작했거나 완료됨

            Task.Run(async () =>
            {
                try
                {
                    await initTask;
                    InjectSessionManagerToPartyManager();
                    Interlocked.Exchange(ref _dependencyInjectionState, 2); // 완료
                }
                catch (Exception ex)
                {
                    Logger.Error($"Deferred dependency injection failed: {ex.Message}");
                    Interlocked.Exchange(ref _dependencyInjectionState, 0); // 재시도 허용
                }
            });
        }

        /// <summary>
        /// PartyManager에 SessionManager 주입 및 GameInstanceManager에 PartyManager/SessionManager 주입
        /// </summary>
        private void InjectSessionManagerToPartyManager()
        {
            try
            {
                // PartyManager 플러그인 찾기
                var partyManager = PluginManager.GetPluginByType<PartyManagerPlugin.PartyManager>();
                if (partyManager != null)
                {
                    partyManager.SetSessionManager(sessionManager);
                    Logger.Info("SessionManager injected to PartyManager successfully");
                }
                else
                {
                    Logger.Warning("PartyManager not found - session injection skipped");
                }

                // GameInstanceManager 플러그인 찾기 및 주입
                var gameInstanceManager = PluginManager.GetPluginByType<GameInstancePlugin.GameInstanceManager>();
                if (gameInstanceManager != null)
                {
                    gameInstanceManager.SetSessionManager(sessionManager);

                    if (partyManager != null)
                    {
                        gameInstanceManager.SetPartyManager(partyManager);
                        Logger.Info("PartyManager injected to GameInstanceManager successfully");

                        // PartyManager에도 GameInstanceManager 주입 (파티 제거 시 인스턴스 정리용)
                        partyManager.SetGameInstanceManager(gameInstanceManager);
                    }

                    Logger.Info("SessionManager injected to GameInstanceManager successfully");
                }
                else
                {
                    Logger.Warning("GameInstanceManager not found - injection skipped");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to inject dependencies: {ex.Message}");
            }
        }

        /// <summary>
        /// 데이터베이스 초기화
        /// </summary>
        private async Task InitializeDatabaseAsync()
        {
            await initSemaphore.WaitAsync();
            try
            {
                if (isInitialized)
                {
                    Logger.Info("Database already initialized");
                    return;
                }

                Logger.Info("Starting database initialization...");
                Logger.Info($"Database path: {dbPath}");

                // SQLiteManager 생성
                dbManager = new SQLiteManager(dbPath);

                if (dbManager == null)
                {
                    throw new Exception("Failed to create SQLiteManager instance");
                }

                Logger.Info("SQLiteManager created");

                // DB 초기화
                await dbManager.InitializeAsync();

                Logger.Info("SQLiteManager.InitializeAsync completed");

                // 초기화 완료 플래그 설정
                isInitialized = true;

                Logger.Info("DatabasePlugin initialized successfully.");
            }
            catch (Exception ex)
            {
                Logger.Fatal($"Database initialization failed: {ex.GetType().Name}");
                Logger.Fatal($"Error message: {ex.Message}");
                Logger.Fatal($"Stack trace: {ex.StackTrace}");

                // 실패 시 null로 설정
                dbManager = null;
                isInitialized = false;

                throw;
            }
            finally
            {
                initSemaphore.Release();
            }
        }

        /// <summary>
        /// 초기화 완료 대기 (타임아웃 포함)
        /// </summary>
        private async Task<bool> EnsureInitializedAsync()
        {
            if (isInitialized)
                return true;

            // 초기화 작업 완료 대기 (최대 30초)
            try
            {
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
                var completedTask = await Task.WhenAny(initTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    Logger.Fatal("Database initialization timeout (30 seconds)");
                    return false;
                }

                // 예외가 발생했는지 확인
                await initTask;
            }
            catch (Exception ex)
            {
                Logger.Fatal($"Initialization error: {ex.Message}");
                return false;
            }

            if (!isInitialized)
            {
                Logger.Error("Database initialization completed but _isInitialized is still false");
                return false;
            }

            if (dbManager == null)
            {
                Logger.Fatal("Database initialization completed but _dbManager is still null");
                return false;
            }

            return true;
        }

        private void OnClientConnected(object sender, ClientConnectedEventArgs e)
        {
            // 첫 클라이언트 연결 시 의존성 지연 주입
            EnsureDependenciesInjected();

            e.Client.MessageReceived += OnMessageReceived;
            Logger.Info($"Client connected: {e.Client.ID}");
        }

        private void OnClientDisconnected(object sender, ClientDisconnectedEventArgs e)
        {
            e.Client.MessageReceived -= OnMessageReceived;

            // 로그아웃 알림 전송 (관리자에게)
            string accountId = sessionManager.GetAccountId(e.Client);
            if (!string.IsNullOrWhiteSpace(accountId))
            {
                NotifyAdminsUserLogout(accountId, e.Client.ID);
            }

            // 세션 제거
            sessionManager.RemoveSessionByClient(e.Client.ID);

            Logger.Info($"Client disconnected: {e.Client.ID}");
        }

        private void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            // Message를 Clone하여 비동기 처리에서도 안전하게 사용
            // 원본 Message도 반드시 Dispose해야 ObjectCache 경고 방지
            Message message;
            using (Message original = e.GetMessage())
            {
                message = original.Clone();
            }
            ushort tag = message.Tag;
            IClient client = e.Client;

            // 비동기 처리 (스레드 풀에서 실행)
            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleMessageAsync(client, message);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error handling message (Tag: {tag}): {ex.Message}");
                    Logger.Error($"Stack trace: {ex.StackTrace}");
                    SendErrorResponse(client, tag, ResponseCode.DatabaseError);
                }
                finally
                {
                    message.Dispose();
                }
            });
        }

        private async Task HandleMessageAsync(IClient client, Message message)
        {
            try
            {
                // 초기화 확인
                if (!isInitialized)
                {
                    Logger.Warning("Database not initialized yet, waiting...");
                }

                bool initialized = await EnsureInitializedAsync();

                if (!initialized)
                {
                    Logger.Error("Database initialization failed or timeout");
                    SendErrorResponse(client, message.Tag, ResponseCode.NotInitialized);
                    return;
                }

                if (dbManager == null)
                {
                    Logger.Fatal("CRITICAL: _dbManager is null even after initialization");
                    SendErrorResponse(client, message.Tag, ResponseCode.DatabaseError);
                    return;
                }

                Logger.Trace($"Handling message tag: {message.Tag}");

                switch (message.Tag)
                {
                    case TAG_LOGIN:
                        await HandleLoginAsync(client, message);
                        break;

                    case TAG_REGISTER:
                        await HandleRegisterAsync(client, message);
                        break;

                    case TAG_GET_ACCOUNT:
                        await HandleGetAccountAsync(client, message);
                        break;

                    case TAG_GET_TYPE_ACCOUNTS:
                        await HandleGetTypeAccountAsync(client, message);
                        break;

                    case TAG_UPDATE_ACCOUNT:
                        await HandleUpdateAccountAsync(client, message);
                        break;

                    case TAG_DELETE_ACCOUNT:
                        await HandleDeleteAccountAsync(client, message);
                        break;

                    case TAG_CHANGE_PASSWORD:
                        await HandleChangePasswordAsync(client, message);
                        break;
                    case TAG_ADMIN_GET_ONLINE_USERS:
                        HandleGetOnlineUsers(client, message);
                        break;

                    // PartyManager에서 처리하는 태그 → 여기서는 무시
                    case MessageTags.PartyCreate:             // 100
                    case MessageTags.PartyDestroy:            // 101
                    case MessageTags.PartyMove:               // 102
                    case MessageTags.PartyChangeRole:         // 103
                    case MessageTags.PartyChangeSetting:      // 104
                    case MessageTags.PartyGetList:            // 105
                    case MessageTags.PartyJoin:               // 106
                    case MessageTags.PartyLeave:              // 107
                    case MessageTags.PartyMemberNotification: // 108

                    // GameInstanceManager에서 처리하는 태그 → 여기서는 무시
                    case MessageTags.TrainingStart:           // 200
                    case MessageTags.InstanceRegister:        // 201
                    case MessageTags.InstanceReady:           // 202
                    case MessageTags.InstanceHeartbeat:       // 203
                    case MessageTags.InstanceShutdown:        // 204
                    case MessageTags.InstanceSignalReady:     // 210
                    case MessageTags.InstanceSignalStart:     // 211
                    case MessageTags.InstanceSignalStop:      // 212
                    case MessageTags.InstanceSignalBroadcast: // 213
                    case MessageTags.PartyConfirm:            // 220
                    case MessageTags.TrainingPrepare:         // 221
                    case MessageTags.TrainingSetup:           // 222
                    case MessageTags.TrainingBegin:           // 224
                    case MessageTags.TrainingStatusNotification: // 225
                    case MessageTags.TrainingWake:            // 226
                    case MessageTags.TrainingWakeResult:      // 227
                    case MessageTags.TrainingCreate:          // 228
                    case MessageTags.TrainingCreated:         // 229
                    case MessageTags.TrainingBeginReady:      // 230
                    case MessageTags.TrainingMidJoin:         // 234
                    case MessageTags.TrainingMidLeave:        // 235
                    case MessageTags.TrainResultPush:         // 236
                    case MessageTags.TrainResultPull:         // 237
                    case MessageTags.ClientLog:               // 280
                        // 다른 플러그인에서 처리 — 여기서는 무시
                        break;

                    default:
                        Logger.Warning($"Unknown message tag: {message.Tag}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Fatal($"HandleMessage critical error: {ex.GetType().Name} - {ex.Message}");
                Logger.Fatal($"Stack trace: {ex.StackTrace}");

                try
                {
                    SendErrorResponse(client, message.Tag, ResponseCode.DatabaseError);
                }
                catch (Exception sendEx)
                {
                    Logger.Error($"Failed to send error response: {sendEx.Message}");
                }
            }
        }

        #region Message Handlers

        /// <summary>
        /// 로그인 처리 (ID, PW 검증) + 세션 등록 + 관리자 알림
        /// </summary>
        private async Task HandleLoginAsync(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    string id = reader.ReadString();
                    string password = reader.ReadString();

                    Logger.Info($"Login attempt: {id}");

                    AccountData account = await dbManager.AuthenticateAsync(id, password);

                    using (DarkRiftWriter writer = DarkRiftWriter.Create())
                    {
                        if (account != null)
                        {
                            bool sessionRegistered = sessionManager.RegisterSession(id, client, account.AccountType);

                            writer.Write((byte)ResponseCode.Success);
                            writer.Write(account.ToResponse());  // PasswordHash 제외

                            Logger.Info($"Login success: {id} (Index: {account.AccountIndex}, Type: {account.AccountType}, Session: {sessionRegistered})");

                            // 관리자들에게 로그인 알림 전송
                            NotifyAdminsUserLogin(account, client.ID);
                        }
                        else
                        {
                            writer.Write((byte)ResponseCode.InvalidCredentials);
                            Logger.Warning($"Login failed: {id}");
                        }

                        using (Message response = Message.Create(TAG_LOGIN, writer))
                        {
                            client.SendMessage(response, SendMode.Reliable);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleLogin error: {ex.Message}");
                SendErrorResponse(client, TAG_LOGIN, ResponseCode.DatabaseError);
            }
        }

        /// <summary>
        /// 계정 등록 처리
        /// </summary>
        private async Task HandleRegisterAsync(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    string id = reader.ReadString();
                    string password = reader.ReadString();
                    int accountType = reader.ReadInt32();

                    Logger.Info($"Register attempt: {id}");

                    // 중복 확인
                    bool exists = await dbManager.AccountExistsAsync(id);
                    if (exists)
                    {
                        using (DarkRiftWriter writer = DarkRiftWriter.Create())
                        {
                            writer.Write((byte)ResponseCode.AlreadyExists);

                            using (Message response = Message.Create(TAG_REGISTER, writer))
                            {
                                client.SendMessage(response, SendMode.Reliable);
                            }
                        }

                        Logger.Warning($"Register failed: {id} already exists");
                        return;
                    }

                    // 비밀번호 해싱
                    string passwordHash = PasswordHasher.HashPassword(password);

                    // 계정 생성
                    AccountData newAccount = new AccountData(id, passwordHash, 0, accountType);
                    bool success = await dbManager.CreateAccountAsync(newAccount);

                    using (DarkRiftWriter writer = DarkRiftWriter.Create())
                    {
                        writer.Write(success ? (byte)ResponseCode.Success : (byte)ResponseCode.Failed);

                        if (success)
                        {
                            writer.Write(newAccount.ToResponse());  // PasswordHash 제외
                            Logger.Info($"Register success: {id}");
                        }

                        using (Message response = Message.Create(TAG_REGISTER, writer))
                        {
                            client.SendMessage(response, SendMode.Reliable);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleRegister error: {ex.Message}");
                SendErrorResponse(client, TAG_REGISTER, ResponseCode.DatabaseError);
            }
        }

        /// <summary>
        /// 계정 정보 조회
        /// </summary>
        private async Task HandleGetAccountAsync(IClient client, Message message)
        {
            string id = null;

            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    id = reader.ReadString();

                    if (string.IsNullOrWhiteSpace(id))
                    {
                        Logger.Warning("HandleGetAccount error: id is null or empty");
                        SendErrorResponse(client, TAG_GET_ACCOUNT, ResponseCode.InvalidData);
                        return;
                    }

                    Logger.Info($"GetAccount request: ID={id}");

                    // DB 조회
                    AccountData account = await dbManager.GetAccountByIdAsync(id);

                    // 응답 전송
                    using (DarkRiftWriter writer = DarkRiftWriter.Create())
                    {
                        if (account != null)
                        {
                            writer.Write((byte)ResponseCode.Success);
                            writer.Write(account.ToResponse());  // PasswordHash 제외
                            Logger.Info($"GetAccount success: {id}");
                        }
                        else
                        {
                            writer.Write((byte)ResponseCode.NotFound);
                            Logger.Warning($"GetAccount not found: {id}");
                        }

                        using (Message response = Message.Create(TAG_GET_ACCOUNT, writer))
                        {
                            client.SendMessage(response, SendMode.Reliable);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleGetAccount error: {ex.GetType().Name} - {ex.Message}");
                Logger.Error($"Stack trace: {ex.StackTrace}");
                Logger.Error($"ID was: {id ?? "null"}");
            }
        }

        /// <summary>
        /// 계정 타입별 전체조회(최대100개)
        /// </summary>
        private async Task HandleGetTypeAccountAsync(IClient client, Message message)
        {
            int type = 0;

            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    type = reader.ReadInt32();

                    if (type > 2)
                    {
                        Logger.Warning("HandleGetTypeAccounts error: type data is over range");
                        SendErrorResponse(client, TAG_GET_TYPE_ACCOUNTS, ResponseCode.InvalidData);
                        return;
                    }

                    Logger.Info($"GetTypeAccounts request: Type={type}");

                    // DB 조회
                    List<AccountData> accounts = await dbManager.GetAllAcountsTypeOfAsync(type);

                    // 응답 전송
                    using (DarkRiftWriter writer = DarkRiftWriter.Create())
                    {
                        if (accounts.Count > 0)
                        {
                            writer.Write((byte)ResponseCode.Success);
                            writer.Write(accounts.Count);
                            accounts.ForEach(x => writer.Write(x.ToResponse()));  // PasswordHash 제외
                            Logger.Info($"GetAccountType success: {accounts.Count}");
                        }
                        else
                        {
                            writer.Write((byte)ResponseCode.NotFound);
                            Logger.Warning($"GetAccount not found: {type}");
                        }

                        using (Message response = Message.Create(TAG_GET_TYPE_ACCOUNTS, writer))
                        {
                            client.SendMessage(response, SendMode.Reliable);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Handle Get Type Accounts error: {ex.GetType().Name} - {ex.Message}");
                SendErrorResponse(client, TAG_GET_TYPE_ACCOUNTS, ResponseCode.DatabaseError);
            }
        }

        /// <summary>
        /// 계정 정보 업데이트
        /// </summary>
        private async Task HandleUpdateAccountAsync(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    AccountData account = reader.ReadSerializable<AccountData>();

                    if (!account.IsValid())
                    {
                        SendErrorResponse(client, TAG_UPDATE_ACCOUNT, ResponseCode.InvalidData);
                        return;
                    }

                    bool success = await dbManager.UpdateAccountAsync(account);

                    using (DarkRiftWriter writer = DarkRiftWriter.Create())
                    {
                        writer.Write(success ? (byte)ResponseCode.Success : (byte)ResponseCode.NotFound);

                        using (Message response = Message.Create(TAG_UPDATE_ACCOUNT, writer))
                        {
                            client.SendMessage(response, SendMode.Reliable);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleUpdateAccount error: {ex.Message}");
                SendErrorResponse(client, TAG_UPDATE_ACCOUNT, ResponseCode.DatabaseError);
            }
        }

        /// <summary>
        /// 계정 삭제
        /// </summary>
        private async Task HandleDeleteAccountAsync(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    string id = reader.ReadString();

                    bool success = await dbManager.DeleteAccountAsync(id);

                    using (DarkRiftWriter writer = DarkRiftWriter.Create())
                    {
                        writer.Write(success ? (byte)ResponseCode.Success : (byte)ResponseCode.NotFound);

                        using (Message response = Message.Create(TAG_DELETE_ACCOUNT, writer))
                        {
                            client.SendMessage(response, SendMode.Reliable);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleDeleteAccount error: {ex.Message}");
                SendErrorResponse(client, TAG_DELETE_ACCOUNT, ResponseCode.DatabaseError);
            }
        }

        /// <summary>
        /// 비밀번호 변경
        /// </summary>
        private async Task HandleChangePasswordAsync(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    string id = reader.ReadString();
                    string oldPassword = reader.ReadString();
                    string newPassword = reader.ReadString();

                    // 새 비밀번호 강도 검증
                    if (!PasswordHasher.IsPasswordStrong(newPassword))
                    {
                        SendErrorResponse(client, TAG_CHANGE_PASSWORD, ResponseCode.WeakPassword);
                        return;
                    }

                    // 기존 비밀번호 검증
                    AccountData account = await dbManager.AuthenticateAsync(id, oldPassword);
                    if (account == null)
                    {
                        using (DarkRiftWriter writer = DarkRiftWriter.Create())
                        {
                            writer.Write((byte)ResponseCode.InvalidCredentials);

                            using (Message response = Message.Create(TAG_CHANGE_PASSWORD, writer))
                            {
                                client.SendMessage(response, SendMode.Reliable);
                            }
                        }
                        return;
                    }

                    // 비밀번호 해싱 및 업데이트
                    string newPasswordHash = PasswordHasher.HashPassword(newPassword);
                    bool success = await dbManager.UpdatePasswordAsync(id, newPasswordHash);

                    using (DarkRiftWriter writer = DarkRiftWriter.Create())
                    {
                        writer.Write(success ? (byte)ResponseCode.Success : (byte)ResponseCode.Failed);

                        using (Message response = Message.Create(TAG_CHANGE_PASSWORD, writer))
                        {
                            client.SendMessage(response, SendMode.Reliable);
                        }
                    }

                    if (success)
                    {
                        Logger.Info($"Password changed: {id}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleChangePassword error: {ex.Message}");
                SendErrorResponse(client, TAG_CHANGE_PASSWORD, ResponseCode.DatabaseError);
            }
        }
        #endregion

        #region Admin Notification

        /// <summary>
        /// 관리자들에게 사용자 로그인 알림 전송
        /// </summary>
        private void NotifyAdminsUserLogin(AccountData account, ushort clientId)
        {
            if (sessionManager == null || account == null)
                return;

            // 자기 자신이 관리자면 알림 불필요
            if (account.AccountType == (int)AccountTypeEnum.Admin)
                return;

            var adminClients = sessionManager.GetAdminClients();
            if (adminClients.Count == 0)
                return;

            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write(account.ToResponse());  // PasswordHash 제외
                    writer.Write(clientId);
                    writer.Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                    using (Message notification = Message.Create(TAG_ADMIN_USER_LOGIN_NOTIFICATION, writer))
                    {
                        foreach (var adminClient in adminClients)
                        {
                            try
                            {
                                adminClient.SendMessage(notification, SendMode.Reliable);
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"Failed to send login notification to admin: {ex.Message}");
                            }
                        }
                    }
                }

                Logger.Info($"Login notification sent to {adminClients.Count} admins: {account.Id}");
            }
            catch (Exception ex)
            {
                Logger.Error($"NotifyAdminsUserLogin error: {ex.Message}");
            }
        }

        /// <summary>
        /// 관리자들에게 사용자 로그아웃 알림 전송
        /// </summary>
        private void NotifyAdminsUserLogout(string accountId, ushort clientId)
        {
            if (sessionManager == null || string.IsNullOrWhiteSpace(accountId))
                return;

            // 관리자 로그아웃은 알림 불필요
            if (sessionManager.IsAdmin(accountId))
                return;

            var adminClients = sessionManager.GetAdminClients();
            if (adminClients.Count == 0)
                return;

            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write(accountId);
                    writer.Write(clientId);
                    writer.Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                    using (Message notification = Message.Create(TAG_ADMIN_USER_LOGOUT_NOTIFICATION, writer))
                    {
                        foreach (var adminClient in adminClients)
                        {
                            try
                            {
                                adminClient.SendMessage(notification, SendMode.Reliable);
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"Failed to send logout notification to admin: {ex.Message}");
                            }
                        }
                    }
                }

                Logger.Info($"Logout notification sent to {adminClients.Count} admins: {accountId}");
            }
            catch (Exception ex)
            {
                Logger.Error($"NotifyAdminsUserLogout error: {ex.Message}");
            }
        }

        #region Admin - Online Users

        /// <summary>
        /// 현재 접속자 목록 조회 (관리자 전용)
        /// </summary>
        private void HandleGetOnlineUsers(IClient client, Message message)
        {
            try
            {
                // 관리자 권한 확인
                if (!sessionManager.IsAdmin(client))
                {
                    Logger.Warning($"Unauthorized access to online users list: ClientID={client.ID}");
                    SendErrorResponse(client, TAG_ADMIN_GET_ONLINE_USERS, ResponseCode.Failed);
                    return;
                }

                using (DarkRiftReader reader = message.GetReader())
                {
                    // 필터 타입 읽기 (-1: 전체, 0: 일반사용자, 1: 관리자)
                    int filterType = -1;
                    if (reader.Length >= 4)
                    {
                        filterType = reader.ReadInt32();
                    }

                    List<OnlineUserInfo> users;

                    if (filterType >= 0)
                    {
                        users = sessionManager.GetOnlineUsersByType(filterType);
                    }
                    else
                    {
                        users = sessionManager.GetAllOnlineUsers();
                    }

                    using (DarkRiftWriter writer = DarkRiftWriter.Create())
                    {
                        writer.Write((byte)ResponseCode.Success);
                        writer.Write(users.Count);

                        foreach (var user in users)
                        {
                            writer.Write(user);
                        }

                        // 요약 정보
                        writer.Write(sessionManager.SessionCount);  // 전체 접속자
                        writer.Write(sessionManager.AdminCount);    // 관리자 수
                        writer.Write(sessionManager.UserCount);     // 일반 사용자 수

                        using (Message response = Message.Create(TAG_ADMIN_GET_ONLINE_USERS, writer))
                        {
                            client.SendMessage(response, SendMode.Reliable);
                        }
                    }

                    Logger.Info($"Online users list sent: {users.Count} users (Filter: {filterType})");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleGetOnlineUsers error: {ex.Message}");
                SendErrorResponse(client, TAG_ADMIN_GET_ONLINE_USERS, ResponseCode.DatabaseError);
            }
        }

        #endregion

        #endregion

        #region Public Session API

        /// <summary>
        /// SQLiteManager 반환 (다른 플러그인에서 사용 — FTP 인증 등)
        /// </summary>
        public SQLiteManager GetSQLiteManager()
        {
            return dbManager;
        }

        /// <summary>
        /// SessionManager 반환 (다른 플러그인에서 사용)
        /// </summary>
        public SessionManager GetSessionManager()
        {
            return sessionManager;
        }

        /// <summary>
        /// account.id로 IClient 조회
        /// </summary>
        public IClient GetClient(string accountId)
        {
            return sessionManager?.GetClient(accountId);
        }

        /// <summary>
        /// 전체 로그인된 클라이언트 목록
        /// </summary>
        public IReadOnlyList<IClient> GetAllClients()
        {
            return sessionManager?.GetAllClients() ?? new List<IClient>();
        }

        /// <summary>
        /// 현재 로그인된 사용자 수
        /// </summary>
        public int SessionCount => sessionManager?.SessionCount ?? 0;

        #endregion

        /// <summary>
        /// 에러 응답 전송
        /// </summary>
        private void SendErrorResponse(IClient client, ushort tag, ResponseCode code)
        {
            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write((byte)code);

                    using (Message response = Message.Create(tag, writer))
                    {
                        client.SendMessage(response, SendMode.Reliable);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to send error response: {ex.Message}");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                initSemaphore?.Dispose();
                dbManager?.Dispose();
                sessionManager?.ClearAllSessions();
                Logger.Info("DatabasePlugin disposed.");
            }

            base.Dispose(disposing);
        }
    }
}