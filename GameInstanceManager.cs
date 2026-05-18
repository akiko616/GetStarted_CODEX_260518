using DarkRift;
using DarkRift.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PartyManagerPlugin;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace GameInstancePlugin
{
    /// <summary>
    /// 게임 인스턴스 관리 플러그인
    /// Headless Unity 게임 인스턴스를 동적으로 생성/관리
    /// </summary>
    public class GameInstanceManager : Plugin
    {
        public override Version Version => new Version(1, 0, 0);
        public override bool ThreadSafe => true;

        // 설정
        private const int MAX_INSTANCES = 10;
        private const int BASE_PORT = 7001;  // 7001-7010
        private static readonly string GAME_EXECUTABLE_PATH =
            ConfigurationManager.AppSettings["GameExecutablePath"]
            ?? @"D:\Projects\Unity\Disaster\Networking\ServerApp\disaster_dedicate\ArmyDisaster.exe";

        /// <summary>
        /// token-server URL (Node.js, hangil-disaster:3000)
        /// </summary>
        private static readonly string TOKEN_SERVER_URL =
            ConfigurationManager.AppSettings["TokenServerUrl"] ?? "http://hangil-disaster:3000";

        // LiveKit REST API 설정 (App.config AppSettings) + Fallback JWT 발급용
        private static readonly string LIVEKIT_API_URL =
            ConfigurationManager.AppSettings["LiveKitApiUrl"] ?? "http://localhost:7880";
        private static readonly string LIVEKIT_API_KEY =
            ConfigurationManager.AppSettings["LiveKitApiKey"] ?? "";
        private static readonly string LIVEKIT_API_SECRET =
            ConfigurationManager.AppSettings["LiveKitApiSecret"] ?? "";
        private static readonly string LIVEKIT_ROOM_NAME =
            ConfigurationManager.AppSettings["LiveKitRoomName"] ?? "disaster-stream";

        /// <summary>
        /// Thread-safe HttpClient 싱글톤
        /// </summary>
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        // 인스턴스 풀
        private readonly object instanceLock = new object();
        private readonly Dictionary<int, GameInstanceData> instances = new Dictionary<int, GameInstanceData>();

        /// <summary>
        /// 서버 전용: 인스턴스별 프로세스/클라이언트 관리 (GameInstanceData에서 분리)
        /// </summary>
        private readonly Dictionary<int, Process> instanceProcesses = new Dictionary<int, Process>();
        private readonly Dictionary<int, IClient> instanceClients = new Dictionary<int, IClient>();

        /// <summary>
        /// 파티 ID -> 인스턴스 ID 매핑
        /// </summary>
        private readonly Dictionary<int, int> partyToInstance = new Dictionary<int, int>();

        /// <summary>
        /// 파티 ID -> 교관 IClient 매핑 (훈련 생성완료 알림용)
        /// </summary>
        private readonly Dictionary<int, IClient> partyToInstructor = new Dictionary<int, IClient>();

        /// <summary>
        /// 파티 ID -> 준비 완료된 훈련생 목록 (Fishnet 연결 완료)
        /// </summary>
        private readonly Dictionary<int, HashSet<string>> partyReadyTrainees = new Dictionary<int, HashSet<string>>();

        /// <summary>
        /// 파티 ID -> GracefulEnd ACK 완료 훈련생 목록
        /// </summary>
        private readonly Dictionary<int, HashSet<string>> partyGracefulAcks = new Dictionary<int, HashSet<string>>();

        /// <summary>
        /// 파티 ID -> GracefulEndReady 수신 훈련생 목록
        /// </summary>
        private readonly Dictionary<int, HashSet<string>> partyGracefulEndReadyAcks = new Dictionary<int, HashSet<string>>();

        /// <summary>
        /// 파티 ID -> GracefulEndReady 150초 타임아웃 CTS
        /// </summary>
        private readonly Dictionary<int, CancellationTokenSource> partyGracefulEndReadyTimers = new Dictionary<int, CancellationTokenSource>();

        /// <summary>
        /// 파티 ID -> 훈련 시작 시각 (TrainingBegin 수신 시 기록)
        /// </summary>
        private readonly Dictionary<int, DateTime> partyTrainingStartTime = new Dictionary<int, DateTime>();

        /// <summary>
        /// 파티 ID -> 누적 일시정지 시간
        /// </summary>
        private readonly Dictionary<int, TimeSpan> partyTotalPausedTime = new Dictionary<int, TimeSpan>();

        /// <summary>
        /// 파티 ID -> 현재 일시정지 시작 시각 (Resume 시 제거)
        /// </summary>
        private readonly Dictionary<int, DateTime> partyPauseStartTime = new Dictionary<int, DateTime>();

        /// <summary>
        /// UnknownDedicate를 보낸 대기 중인 IClient (에디터 인스턴스)
        /// </summary>
        private IClient unknownClient;

        /// <summary>
        /// 일시정지된 파티 추적 (Pause 토글용)
        /// </summary>
        private readonly HashSet<int> pausedParties = new HashSet<int>();
        /// <summary>
        /// 일시정지된 비-파티 인스턴스 추적 (partyId와 instanceId 충돌 방지)
        /// </summary>
        private readonly HashSet<int> pausedInstances = new HashSet<int>();

        /// <summary>
        /// 파티별 채팅 큐 (파티 ID -> 메시지 큐, 최대 100개 저장) - 사용자 요청으로 100개 조정
        /// </summary>
        private const int MAX_CHAT_QUEUE_SIZE = 100;
        private readonly Dictionary<int, Queue<ChatMessageData>> partyChatQueues = new Dictionary<int, Queue<ChatMessageData>>();

        /// <summary>
        /// 스트림 상태 관리: userId -> (status, token)
        /// status: 0=Off, 1=On
        /// </summary>
        private readonly Dictionary<string, (int status, string token)> streamDataMap = new Dictionary<string, (int, string)>();
        private readonly object streamLock = new object();

        // SessionManager 참조
        private DatabasePlugin.SessionManager sessionManager;
        private volatile PartyManager partyManager;

        // 핑 측정
        private struct ClientPingInfo { public int PingMs; public int HardwareStatus; }
        private readonly Dictionary<ushort, ClientPingInfo> _allClientPings = new Dictionary<ushort, ClientPingInfo>();
        private Timer _pingProbeTimer;
        private const int PING_PROBE_INTERVAL_MS = 5000;

        // 하트비트 모니터링
        private Timer heartbeatTimer;
        private readonly TimeSpan heartbeatInterval = TimeSpan.FromSeconds(10);

        /// <summary>
        /// 교관이 관전 중인 파티 (1명 원칙)
        /// </summary>
        private int observingPartyId = -1;

        // 태그 정의 (MessageTags에서 참조)
        private const ushort TAG_INSTANCE_REGISTER = DatabasePlugin.MessageTags.InstanceRegister;     // 게임 인스턴스 -> 서버: 등록
        private const ushort TAG_INSTANCE_READY = DatabasePlugin.MessageTags.InstanceReady;           // 게임 인스턴스 -> 서버: 준비 완료
        private const ushort TAG_INSTANCE_HEARTBEAT = DatabasePlugin.MessageTags.InstanceHeartbeat;   // 게임 인스턴스 -> 서버: 하트비트
        private const ushort TAG_INSTANCE_SHUTDOWN = DatabasePlugin.MessageTags.InstanceShutdown;     // 게임 인스턴스 -> 서버: 종료
        private const ushort TAG_TRAINING_INFO = DatabasePlugin.MessageTags.TrainingInfo;             // 서버 -> 클라이언트: 게임 서버 정보
        private const ushort TAG_PARTY_DATA = DatabasePlugin.MessageTags.PartyData;                   // 서버 -> 게임 인스턴스: 파티 데이터
        private const ushort TAG_ADMIN_GET_ALL_INSTANCES = DatabasePlugin.MessageTags.AdminGetAllInstances;     // 관리자 -> 서버: 모든 인스턴스 조회
        private const ushort TAG_ADMIN_SPECTATE_REQUEST = DatabasePlugin.MessageTags.AdminSpectateRequest;      // 관리자 -> 서버: 관전 요청

        // 인스턴스 신호 태그 (Instance <-> Server <-> Party)
        private const ushort TAG_INSTANCE_SIGNAL_READY = DatabasePlugin.MessageTags.InstanceSignalReady;     // 준비 신호
        private const ushort TAG_INSTANCE_SIGNAL_START = DatabasePlugin.MessageTags.InstanceSignalStart;     // 시작 신호
        private const ushort TAG_INSTANCE_SIGNAL_STOP = DatabasePlugin.MessageTags.InstanceSignalStop;       // 종료 신호
        private const ushort TAG_INSTANCE_SIGNAL_BROADCAST = DatabasePlugin.MessageTags.InstanceSignalBroadcast; // 브로드캐스트

        // 교관 주도 훈련 플로우 태그 (Training Control)
        private const ushort TAG_PARTY_CONFIRM = DatabasePlugin.MessageTags.PartyConfirm;                   // 파티 확정
        private const ushort TAG_TRAINING_SETUP = DatabasePlugin.MessageTags.TrainingSetup;                 // 훈련 설정 (교관 -> 서버)
        private const ushort TAG_TRAINING_BEGIN = DatabasePlugin.MessageTags.TrainingBegin;                 // 훈련 시작
        private const ushort TAG_TRAINING_STATUS = DatabasePlugin.MessageTags.TrainingStatusNotification;   // 훈련 상태 알림

        // 새 훈련 플로우 태그 (226-230)
        private const ushort TAG_TRAINING_WAKE = DatabasePlugin.MessageTags.TrainingWake;                   // 인스턴스 기상
        private const ushort TAG_TRAINING_WAKE_RESULT = DatabasePlugin.MessageTags.TrainingWakeResult;      // 인스턴스 기상 응답
        private const ushort TAG_TRAINING_CREATE = DatabasePlugin.MessageTags.TrainingCreate;               // 훈련 생성 (인스턴스 -> 서버)
        private const ushort TAG_TRAINING_CREATED = DatabasePlugin.MessageTags.TrainingCreated;             // 훈련 생성됨 (서버 -> 훈련생)
        private const ushort TAG_TRAINING_BEGIN_READY = DatabasePlugin.MessageTags.TrainingBeginReady;      // 훈련 시작 준비 완료
        private const ushort TAG_TRAINING_PAUSE = DatabasePlugin.MessageTags.TrainingPause;              // 훈련 일시정지/재개 토글
        private const ushort TAG_TRAINING_EXITS = DatabasePlugin.MessageTags.TrainingExits;                // 훈련 종료
        private const ushort TAG_PIN_INFO = DatabasePlugin.MessageTags.PinInfo;                          // 핀 정보 (교관 -> 서버 -> 파티 교육생)
        private const ushort TAG_TRAINING_MID_JOIN = DatabasePlugin.MessageTags.TrainingMidJoin;      // 훈련 중간 참여 (교관 -> 서버)
        private const ushort TAG_TRAINING_MID_LEAVE = DatabasePlugin.MessageTags.TrainingMidLeave;    // 훈련 중간 탈퇴 (교관 -> 서버)
        private const ushort TAG_TRAIN_RESULT_PUSH = DatabasePlugin.MessageTags.TrainResultPush;      // 훈련 결과 저장 및 전송
        private const ushort TAG_TRAIN_RESULT_PULL = DatabasePlugin.MessageTags.TrainResultPull;      // 훈련 결과 배포
        private const ushort TAG_GRACEFUL_END = DatabasePlugin.MessageTags.GracefulEnd;              // 우아한 종료 (교관→서버→훈련생 / 훈련생→서버 ACK)
        private const ushort TAG_GRACEFUL_QUIT = DatabasePlugin.MessageTags.GracefulQuit;            // 데디케이트 종료 신호 (서버→데디케이트)
        private const ushort TAG_GRACEFUL_END_READY = DatabasePlugin.MessageTags.GracefulEndReady;   // 훈련생→서버: 종료 준비 완료
        private const ushort TAG_GRACEFUL_END_REQUEST = DatabasePlugin.MessageTags.GracefulEndRequest; // 서버→교관: 전원 준비 완료 알림
        private const ushort TAG_PING_PROBE     = DatabasePlugin.MessageTags.PingProbe;             // 서버→교육생: RTT 측정 프로브
        private const ushort TAG_PING_PROBE_ACK = DatabasePlugin.MessageTags.PingProbeAck;          // 교육생→서버: 프로브 응답
        private const ushort TAG_PARTY_PING_STATUS = DatabasePlugin.MessageTags.PartyPingStatus;    // 서버→교관: 파티원 핑 현황
        private const ushort TAG_UNKNOWN_DEDICATE = DatabasePlugin.MessageTags.UnknownDedicate;        // 테스트용 인스턴스 대기 전용
        private const ushort TAG_UNKNOWN_DEDICATE_RESPONE = DatabasePlugin.MessageTags.UnknownDedicateRespone;        // 테스트용 인스턴스 대기 전용 리스폰

        // 채팅 태그
        private const ushort TAG_ANNOUNCE_TEXT = DatabasePlugin.MessageTags.AnnounceText;              // 교관 공지
        private const ushort TAG_SEND_TEXT = DatabasePlugin.MessageTags.SendText;                      // 교육생 일반 채팅 / 전체 브로드캐스트

        // 스트림 제어 태그
        private const ushort TAG_ORDER_STREAM = DatabasePlugin.MessageTags.OrderStream;              // 교관 → 서버: 스트림 명령
        private const ushort TAG_PUBLISH_STREAM = DatabasePlugin.MessageTags.PublishStream;            // 서버 → 교육생: 스트림 배포

        // 클라이언트 로그 태그
        private const ushort TAG_CLIENT_LOG = DatabasePlugin.MessageTags.ClientLog;                    // 클라이언트/인스턴스 → 서버 콘솔 출력

        public enum ResponseCode : byte
        {
            Success = 0,
            Failed = 1,
            NoAvailableInstance = 2,
            PartyNotFound = 3,
            InstanceNotFound = 4,
            AlreadyRunning = 5,
            InvalidData = 6,
            ServerError = 7,
            Unauthorized = 8,
            InstanceError = 9,
            AlreadyExists = 10
        }

        #region Initialization & Injection

        private DatabasePlugin.DatabaseManager _databaseManager;

        public GameInstanceManager(PluginLoadData pluginLoadData) : base(pluginLoadData)
        {
            // 인스턴스 풀 초기화
            InitializeInstancePool();

            // 이벤트 구독
            ClientManager.ClientConnected += OnClientConnected;
            ClientManager.ClientDisconnected += OnClientDisconnected;

            // 프로세스 종료 시 인스턴스 강제 정리 (CMD 창 닫기 등)
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Console.CancelKeyPress += OnCancelKeyPress;

            // 하트비트 모니터링 시작
            heartbeatTimer = new Timer(CheckHeartbeats, null, heartbeatInterval, heartbeatInterval);
            _pingProbeTimer = new Timer(PingTick, null, PING_PROBE_INTERVAL_MS, PING_PROBE_INTERVAL_MS);
            Logger.Info($"GameInstanceManager initialized. TokenServer={TOKEN_SERVER_URL}");
        }

        /// <summary>
        /// 인스턴스 풀 초기화
        /// </summary>
        private void InitializeInstancePool()
        {
            lock (instanceLock)
            {
                for (int i = 1; i <= MAX_INSTANCES; i++)
                {
                    int port = BASE_PORT + (i - 1);
                    instances[i] = new GameInstanceData(i, port);
                }
            }

            Logger.Info($"Game instance pool initialized: {MAX_INSTANCES} slots (Ports {BASE_PORT}-{BASE_PORT + MAX_INSTANCES - 1})");
        }

        /// <summary>
        /// SessionManager 주입
        /// </summary>
        public void SetSessionManager(DatabasePlugin.SessionManager sessionManager)
        {
            this.sessionManager = sessionManager;
            Logger.Info("SessionManager injected to GameInstanceManager");
        }

        /// <summary>
        /// PartyManager 주입
        /// </summary>
        public void SetPartyManager(PartyManager partyManager)
        {
            this.partyManager = partyManager;
            Logger.Info("PartyManager injected to GameInstanceManager");
        }

        /// <summary>
        /// partyManager가 아직 주입되지 않은 경우 PluginManager에서 직접 검색 (lazy fallback)
        /// volatile 필드 + local 변수 패턴으로 스레드 안전 보장
        /// </summary>
        private PartyManager EnsurePartyManager()
        {
            var pm = partyManager; // volatile read
            if (pm != null)
                return pm;

            pm = PluginManager.GetPluginByType<PartyManager>();
            if (pm != null)
            {
                partyManager = pm; // volatile write
                Logger.Info("PartyManager resolved via PluginManager (lazy fallback)");
            }
            return pm;
        }

        private DatabasePlugin.DatabaseManager EnsureDatabaseManager()
        {
            var dm = _databaseManager;
            if (dm != null) return dm;

            dm = PluginManager.GetPluginByType<DatabasePlugin.DatabaseManager>();
            if (dm != null)
            {
                _databaseManager = dm;
            }
            return dm;
        }

        #endregion

        #region DarkRift Event Routing

        private void OnClientConnected(object sender, ClientConnectedEventArgs e)
        { e.Client.MessageReceived += OnMessageReceived; }

        private void OnClientDisconnected(object sender, ClientDisconnectedEventArgs e)
        {
            e.Client.MessageReceived -= OnMessageReceived;
            lock (instanceLock) { _allClientPings.Remove(e.Client.ID); }
            HandleInstanceDisconnect(e.Client);
        }

        private void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            using (Message message = e.GetMessage())
            {
                try
                {
                    switch (message.Tag)
                    {
                        case TAG_INSTANCE_REGISTER: HandleInstanceRegister(e.Client, message); break;
                        case TAG_INSTANCE_READY: HandleInstanceReady(e.Client, message); break;
                        case TAG_INSTANCE_HEARTBEAT: HandleInstanceHeartbeat(e.Client, message); break;
                        case TAG_INSTANCE_SHUTDOWN: HandleInstanceShutdown(e.Client, message); break;
                        case TAG_ADMIN_GET_ALL_INSTANCES: HandleAdminGetAllInstances(e.Client, message); break;
                        case TAG_ADMIN_SPECTATE_REQUEST: HandleAdminSpectateRequest(e.Client, message); break;
                        case TAG_INSTANCE_SIGNAL_READY: HandleInstanceSignal(e.Client, message, InstanceSignalType.Ready); break;
                        case TAG_INSTANCE_SIGNAL_START: HandleInstanceSignal(e.Client, message, InstanceSignalType.Start); break;
                        case TAG_INSTANCE_SIGNAL_STOP: HandleInstanceSignal(e.Client, message, InstanceSignalType.Stop); break;
                        case TAG_PARTY_CONFIRM: HandlePartyConfirm(e.Client, message); break;
                        case TAG_TRAINING_SETUP: HandleTrainingSetup(e.Client, message); break;
                        case TAG_TRAINING_WAKE: HandleTrainingWake(e.Client, message); break;
                        case TAG_TRAINING_CREATE: HandleTrainingCreate(e.Client, message); break;
                        case TAG_TRAINING_BEGIN_READY: HandleTrainingBeginReady(e.Client, message); break;
                        case TAG_TRAINING_BEGIN: HandleTrainingBegin(e.Client, message); break;
                        case TAG_TRAINING_PAUSE: HandleTrainingPause(e.Client, message); break;
                        case TAG_TRAINING_EXITS: HandleTrainingStop(e.Client, message); break;
                        case TAG_PIN_INFO: HandlePinInfo(e.Client, message); break;
                        case TAG_TRAINING_MID_JOIN: HandleTrainingMidJoin(e.Client, message); break;
                        case TAG_TRAINING_MID_LEAVE: HandleTrainingMidLeave(e.Client, message); break;
                        case TAG_TRAIN_RESULT_PUSH: HandleTrainResultPush(e.Client, message); break;
                        case TAG_GRACEFUL_END: HandleGracefulEnd(e.Client, message); break;
                        case TAG_GRACEFUL_END_READY: HandleGracefulEndReady(e.Client, message); break;
                        case TAG_PING_PROBE_ACK: HandlePingProbeAck(e.Client, message); break;
                        case TAG_ANNOUNCE_TEXT: HandleAnnounceText(e.Client, message); break;
                        case TAG_SEND_TEXT: HandleSendText(e.Client, message); break;
                        case TAG_ORDER_STREAM: HandleOrderStream(e.Client, message); break;
                        case TAG_CLIENT_LOG: HandleClientLog(e.Client, message); break;
                        case TAG_UNKNOWN_DEDICATE: HandleUnknownDedicate(e.Client); break;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error handling game instance message (Tag: {message.Tag}): {ex.Message}");
                }
            }
        }

        #endregion

        #region Stream Control (Tag 270 / Tag 271)

        /// <summary>
        /// Tag 270 HandleOrderStream
        /// </summary>
        /// <param name="instructorClient"></param>
        /// <param name="message"></param>) → POST /stream/order
        private void HandleOrderStream(IClient instructorClient, Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                string userId = reader.ReadString();
                int status = reader.ReadInt32(); // 0 = Off, 1 = On

                Logger.Info($"OrderStream: userId={userId}, status={status}");

                // 교관 권한 확인
                if (sessionManager == null || !sessionManager.IsAdmin(instructorClient))
                {
                    Logger.Warning("OrderStream denied: Not an admin");
                    SendOrderStreamResponse(instructorClient, ResponseCode.Unauthorized, userId, status);
                    return;
                }

                // 대상 유저 연결 확인
                IClient targetClient = sessionManager.GetClient(userId);
                if (targetClient == null)
                {
                    Logger.Warning($"OrderStream: Target user '{userId}' is not online");
                    SendOrderStreamResponse(instructorClient, ResponseCode.InvalidData, userId, status);
                    return;
                }

                // 파티 ID 조회 (token-server에 전달용)
                string partyId = GetPartyIdForUser(userId);

                // token-server에 스트림 명령 전달 (비동기 — DarkRift 스레드 블로킹 방지)
                Task.Run(async () =>
                {
                    string token = string.Empty;
                    string room = string.Empty;

                    string respJson = await PostToTokenServerAsync("/stream/order", new
                    {
                        studentId = userId,
                        status = status,
                        partyId = partyId
                    });

                    var resp = JObject.Parse(respJson);
                    token = resp["token"]?.Value<string>() ?? string.Empty;
                    room = resp["room"]?.Value<string>() ?? string.Empty;

                    // token-server가 토큰을 주지 않은 경우 로컬 JWT Fallback
                    if (status == 1 && string.IsNullOrEmpty(token))
                    {
                        Logger.Warning($"OrderStream: token-server에서 토큰 없음, 로컬 JWT Fallback: userId={userId}");
                        token = FetchLiveKitToken(userId);
                    }

                    // 스트림 상태 업데이트
                    lock (streamLock)
                    {
                        streamDataMap[userId] = (status, token);
                    }

                    // 교육생에게 PublishStream(271) 전송
                    SendPublishStream(targetClient, userId, status, token);

                    // 교관에게 성공 응답
                    SendOrderStreamResponse(instructorClient, ResponseCode.Success, userId, status);

                    Logger.Info($"OrderStream success: userId={userId}, status={status}, room={room}, tokenLen={token.Length}");
                });
            }
        }

        private string GetPartyIdForUser(string userId)
        {
            var pm = EnsurePartyManager();
            if (pm == null) return string.Empty;

            var allParties = pm.GetAllParties();
            foreach (var party in allParties)
                if (party.HasMember(userId))
                    return party.PartyId.ToString();

            return string.Empty;
        }

        /// <summary>
        /// 교관에게 OrderStream(270) 처리 결과 응답 전송
        /// payload: ResponseCode(byte) + userId(string) + status(int)
        /// </summary>
        private void SendOrderStreamResponse(IClient client, ResponseCode code, string userId, int status)
        {
            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write((byte)code);
                    writer.Write(userId);
                    writer.Write(status);
                    using (Message response = Message.Create(TAG_ORDER_STREAM, writer))
                    {
                        client.SendMessage(response, SendMode.Reliable);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"SendOrderStreamResponse error: {ex.Message}");
            }
        }

        /// <summary>
        /// 특정 교육생에게 PublishStream(271) 전송
        /// payload: status(int) + token(string)
        /// </summary>
        private void SendPublishStream(IClient targetClient, string userId, int status, string token)
        {
            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write(status);
                    writer.Write(token ?? string.Empty);
                    using (Message msg = Message.Create(TAG_PUBLISH_STREAM, writer))
                    {
                        targetClient.SendMessage(msg, SendMode.Reliable);
                        Logger.Info($"PublishStream sent: userId={userId}, status={status}, hasToken={!string.IsNullOrEmpty(token)}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"SendPublishStream error for user '{userId}': {ex.Message}");
            }
        }

        /// <summary>
        /// token-server에 JSON POST 요청을 보냅니다.
        /// ThreadSafe=true 이므로 반드시 Task.Run 안에서 await
        /// </summary>
        private async Task<string> PostToTokenServerAsync(string path, object body)
        {
            string json = JsonConvert.SerializeObject(body);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var resp = await _httpClient.PostAsync(TOKEN_SERVER_URL + path, content).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        // FetchLiveKitToken (로컬 JWT) → POST /stream/order
        /// <summary>
        /// LiveKit REST API를 통해 지정 userId의 참가자 토큰을 발급합니다.
        /// token-server 호출 실패 시 Fallback으로 사용되는 로컬 JWT 생성
        /// 설정: AppSettings[LiveKitApiKey / LiveKitApiSecret]
        /// </summary>
        private string FetchLiveKitToken(string userId)
        {
            try
            {
                if (string.IsNullOrEmpty(LIVEKIT_API_KEY) || string.IsNullOrEmpty(LIVEKIT_API_SECRET))
                {
                    Logger.Error("LiveKit API Key or Secret is not configured in AppSettings.");
                    return string.Empty;
                }

                string headerJson = "{\"alg\":\"HS256\",\"typ\":\"JWT\"}";
                string headerB64 = Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(headerJson));

                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                long exp = now + 18000;
                // Private Room 방식 (token-server와 동일한 room 규칙)
                string payloadJson = $"{{" +
                    $"\"iss\":\"{LIVEKIT_API_KEY}\"," +
                    $"\"sub\":\"{userId}\"," +
                    $"\"iat\":{now}," +
                    $"\"exp\":{exp}," +
                    $"\"nbf\":{now}," +
                    $"\"video\":{{\"room\":\"private-{userId}\",\"roomJoin\":true,\"canPublish\":true,\"canSubscribe\":false}}" +
                    $"}}";
                string payloadB64 = Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(payloadJson));

                string signingInput = $"{headerB64}.{payloadB64}";
                byte[] secretBytes = System.Text.Encoding.UTF8.GetBytes(LIVEKIT_API_SECRET);
                byte[] sigBytes;
                using (var hmac = new System.Security.Cryptography.HMACSHA256(secretBytes))
                {
                    sigBytes = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(signingInput));
                }

                string token = $"{signingInput}.{Base64UrlEncode(sigBytes)}";
                Logger.Warning($"FetchLiveKitToken (Fallback JWT): userId='{userId}', room='private-{userId}'");
                return token;
            }
            catch (Exception ex)
            {
                Logger.Error($"FetchLiveKitToken error for '{userId}': {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Base64Url 인코딩 (JWT 표준: +→-, /→_, 패딩 제거)
        /// </summary>
        private static string Base64UrlEncode(byte[] data)
            => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        #endregion

        #region Training Flow (token-server 연동)

        /// <summary>
        /// Tag 222 HandleTrainingSetup
        /// 성공 응답 후 POST /training/setup (Egress 녹화 시작)
        /// </summary>
        /// <param name="client"></param>
        /// <param name="message"></param>
        private void HandleTrainingSetup(IClient client, Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                int partyId = reader.ReadInt32();
                string sceneFile = reader.ReadString();

                Logger.Info($"Training setup request: PartyID={partyId}, Scene={sceneFile}");

                // 의존성 검증
                if (partyManager == null || sessionManager == null)
                {
                    Logger.Error("Training setup failed: PartyManager or SessionManager not injected");
                    SendErrorResponse(client, TAG_TRAINING_SETUP, ResponseCode.ServerError);
                    return;
                }

                // 교관 권한 확인
                if (!sessionManager.IsAdmin(client))
                {
                    Logger.Warning("Training setup denied: Not an admin");
                    SendErrorResponse(client, TAG_TRAINING_SETUP, ResponseCode.Unauthorized);
                    return;
                }

                // 파티 존재 여부 확인
                var party = partyManager.GetPartyById(partyId);
                if (party == null)
                {
                    Logger.Warning($"Training setup denied: Party {partyId} not found");
                    SendErrorResponse(client, TAG_TRAINING_SETUP, ResponseCode.PartyNotFound);
                    return;
                }

                // 교관에게 성공 응답
                SendSuccessResponse(client, TAG_TRAINING_SETUP);

                // token-server에 Egress 녹화 시작 요청 (파티 전원, fire-and-forget)
                var memberIds = partyManager.GetPartyMembers(partyId).ToList();
                Task.Run(async () =>
                {
                    string respJson = await PostToTokenServerAsync("/training/setup", new
                    {
                        partyId = partyId,
                        students = memberIds.Select(id => new { studentId = id }).ToList()
                    });

                    JObject resp = JObject.Parse(respJson);
                    Logger.Info($"Training setup Egress started: party={partyId} started={resp?["started"]}");
                });

                Logger.Info($"Training setup completed: PartyID={partyId}, Members={memberIds.Count}");
            }
        }

        /// <summary>
        /// Tag 232 HandleTrainingStop
        /// Stop 브로드캐스트 후 POST /training/stop (Egress 중지)
        /// </summary>
        /// <param name="client"></param>
        /// <param name="message"></param>
        private void HandleTrainingStop(IClient client, Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                int partyId = reader.ReadInt32();

                Logger.Info($"Training stop request: PartyID={partyId}");

                // 교관 권한 확인
                if (sessionManager == null || !sessionManager.IsAdmin(client))
                {
                    Logger.Warning("Training stop denied: Not an admin");
                    SendErrorResponse(client, TAG_TRAINING_EXITS, ResponseCode.Unauthorized);
                    return;
                }

                // 인스턴스 확인
                GameInstanceData instance;
                lock (instanceLock)
                {
                    if (!partyToInstance.TryGetValue(partyId, out int instanceId) ||
                        !instances.TryGetValue(instanceId, out instance))
                    {
                        Logger.Warning($"Training stop denied: No instance for party {partyId}");
                        SendErrorResponse(client, TAG_TRAINING_EXITS, ResponseCode.InstanceNotFound);
                        return;
                    }

                    if (instance.State != GameInstanceState.Running)
                    {
                        Logger.Warning($"Training stop denied: Instance {instanceId} is not running (State: {instance.State})");
                        SendErrorResponse(client, TAG_TRAINING_EXITS, ResponseCode.ServerError);
                        return;
                    }

                    // Stopping 상태로 전환 + 일시정지 상태 제거
                    instance.State = GameInstanceState.Stopping;
                    pausedParties.Remove(partyId);
                }

                // 데디케이트 서버에만 Stop 전송 (파티원에게는 전송하지 않음)
                IClient instanceClient;
                lock (instanceLock)
                {
                    instanceClients.TryGetValue(instance.InstanceId, out instanceClient);
                }
                if (instanceClient != null)
                {
                    using (DarkRiftWriter w = DarkRiftWriter.Create())
                    {
                        w.Write((byte)InstanceSignalType.Stop);
                        w.Write(partyId);
                        using (Message sig = Message.Create(TAG_TRAINING_EXITS, w))
                            instanceClient.SendMessage(sig, SendMode.Reliable);
                    }
                    Logger.Info($"Training stop signal sent to instance {instance.InstanceId} only");
                }
                else
                {
                    Logger.Warning($"Training stop: Instance {instance.InstanceId} client not found, skipping signal");
                }

                // 교관에게 성공 응답
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write((byte)ResponseCode.Success);
                    writer.Write((byte)InstanceSignalType.Stop);
                    writer.Write(partyId);
                    using (Message response = Message.Create(TAG_TRAINING_EXITS, writer))
                    {
                        client.SendMessage(response, SendMode.Reliable);
                    }
                }

                Logger.Info($"Training stop: Party={partyId}, Instance={instance.InstanceId} → Stopping");
                NotifyAdminsInstanceStateChange(instance, "Training stopped by instructor");

                // token-server에 Egress 전원 중지 요청 (fire-and-forget)
                Task.Run(async () =>
                {
                    string respJson = await PostToTokenServerAsync("/training/stop", new { partyId });
                    var resp = JObject.Parse(respJson);
                    Logger.Info($"Training stop Egress stopped: party={partyId} stopped={resp["stopped"]}");
                });

                // 10초 안전 타임아웃: 인스턴스가 자체 종료하지 않으면 강제 정리
                int safeInstanceId = instance.InstanceId;
                Task.Run(async () =>
                {
                    await Task.Delay(10000).ConfigureAwait(false);
                    GameInstanceData inst;
                    lock (instanceLock)
                    {
                        if (!instances.TryGetValue(safeInstanceId, out inst) ||
                            inst.State != GameInstanceState.Stopping)
                            return;
                    }
                    Logger.Warning($"Training stop safety timeout: Instance {safeInstanceId} still stopping after 10s, forcing cleanup");
                    CleanupInstance(inst);
                });
            }
        }

        /// <summary>
        /// Tag 236 HandleTrainResultPush
        /// DB 저장 성공 후 POST /result/push (token-server 캐시)
        /// </summary>
        /// <param name="client"></param>
        /// <param name="message"></param>
        private void HandleTrainResultPush(IClient client, Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                TrainResult pushData = reader.ReadSerializable<TrainResult>();

                Logger.Info($"TrainResultPush received for party: {pushData.partyId}");

                // 서버 측 플레이타임 계산 (TrainingBegin 기준, 일시정지 시간 제외)
                if (partyTrainingStartTime.TryGetValue(pushData.partyId, out DateTime trainStart))
                {
                    TimeSpan totalPaused = partyTotalPausedTime.TryGetValue(pushData.partyId, out TimeSpan tp) ? tp : TimeSpan.Zero;
                    // 현재 일시정지 중이면 진행 중인 정지 시간도 포함
                    if (partyPauseStartTime.TryGetValue(pushData.partyId, out DateTime ongoingPause))
                        totalPaused += DateTime.UtcNow - ongoingPause;
                    int playSeconds = Math.Max(0, (int)(DateTime.UtcNow - trainStart - totalPaused).TotalSeconds);
                    pushData.playTime = playSeconds;
                    Logger.Info($"TrainResultPush playTime calculated: {playSeconds}s (party={pushData.partyId})");
                }

                // results → JSON 변환 (DB, token-server용 — 1회만 수행)
                string resultJsonStr = JsonConvert.SerializeObject(pushData.results ?? new TrainResultData[0]);

                // 테스트 모드: results가 비어있으면 DB 업데이트 없이 바로 성공 처리
                if (pushData.results == null || pushData.results.Length == 0)
                {
                    Logger.Info($"TrainResultPush [TEST MODE] party={pushData.partyId} — skipping DB, distributing directly.");
                    SendTrainResultPull(pushData);
                    SendSuccessResponse(client, TAG_TRAIN_RESULT_PUSH);
                    Task.Run(async () =>
                        {
                            string respJson = await PostToTokenServerAsync("/result/push", new
                            {
                                partyId = pushData.partyId,
                                trainingType = pushData.trainingType,
                                executionTime = pushData.playTime.ToString("F0"),
                                resultJson = resultJsonStr
                            });
                            Logger.Info($"TrainResultPush token-server cache updated: party={pushData.partyId}");
                        });
                    return;
                }

                var dbPlugin = EnsureDatabaseManager();

                if (dbPlugin != null && dbPlugin.Db != null)
                {
                    // 비동기로 DB 저장 후 콜백(ContinueWith)을 통해 순차 보장 및 배포
                    dbPlugin.Db.SaveTrainResultAsync(pushData.partyId, pushData.trainingType, pushData.playTime.ToString("F0"), resultJsonStr)
                        .ContinueWith(task =>
                        {
                            bool dbUpdateSuccess = task.Status == TaskStatus.RanToCompletion && task.Result;

                            if (dbUpdateSuccess)
                            {
                                // 1. DB 저장 성공 시 파티원들과 교관에게 배포
                                SendTrainResultPull(pushData);
                                SendSuccessResponse(client, TAG_TRAIN_RESULT_PUSH);

                                // 2. token-server 결과 캐시 업데이트 (NVR 재생 탭 GET /result/pull 용)
                                Task.Run(async () =>
                                {
                                    string respJson = await PostToTokenServerAsync("/result/push", new
                                    {
                                        partyId = pushData.partyId,
                                        trainingType = pushData.trainingType,
                                        executionTime = pushData.playTime.ToString("F0"),
                                        resultJson = resultJsonStr
                                    });
                                    Logger.Info($"TrainResultPush token-server cache updated: party={pushData.partyId}");
                                });
                            }
                            else
                            {
                                Logger.Error($"DB Update failed or task faulted for Party: {pushData.partyId}");
                                SendErrorResponse(client, TAG_TRAIN_RESULT_PUSH, ResponseCode.ServerError);
                            }
                        });
                }
                else
                {
                    Logger.Error("DatabaseManager or SQLiteManager is not properly initialized.");
                    SendErrorResponse(client, TAG_TRAIN_RESULT_PUSH, ResponseCode.ServerError);
                }
            }
        }

        /// <summary>
        /// 훈련 결과를 해당 파티의 훈련생(교관 제외) 및 교관에게 배포
        /// </summary>
        private void SendTrainResultPull(TrainResult result)
        {
            if (sessionManager == null || partyManager == null) return;

            try
            {
                var partyMembers = partyManager.GetPartyMembers(result.partyId);
                var pullData = result;

                // 교관 클라이언트 및 ID 미리 확보
                IClient instructorClient = null;
                lock (instanceLock)
                {
                    partyToInstructor.TryGetValue(result.partyId, out instructorClient);
                }
                string instructorId = instructorClient != null ? sessionManager.GetAccountId(instructorClient) : null;

                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write((byte)ResponseCode.Success);
                    writer.Write(pullData);

                    using (Message notification = Message.Create(TAG_TRAIN_RESULT_PULL, writer))
                    {
                        // 1. 파티 훈련생 배포 (교관 제외)
                        int notifiedCount = 0;
                        foreach (var memberId in partyMembers)
                        {
                            if (memberId == instructorId) continue;

                            IClient memberClient = sessionManager.GetClient(memberId);
                            if (memberClient != null)
                            {
                                try
                                {
                                    memberClient.SendMessage(notification, SendMode.Reliable);
                                    notifiedCount++;
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error($"Failed to send TrainResultPull to member {memberId}: {ex.Message}");
                                }
                            }
                        }

                        // 2. 교관에게 무조건 전달
                        if (instructorClient != null)
                        {
                            try
                            {
                                instructorClient.SendMessage(notification, SendMode.Reliable);
                                notifiedCount++;
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"Failed to send TrainResultPull to instructor: {ex.Message}");
                            }
                        }

                        Logger.Info($"TrainResultPull sent to Party={result.partyId}, Notified={notifiedCount}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"SendTrainResultPull error: {ex.Message}");
            }
        }

        /// <summary>
        /// Tag 238 HandleGracefulEnd
        /// 교관이 보내면: partyGracefulAcks 초기화 후 훈련생 전체에 GracefulEnd 배포
        /// 훈련생이 보내면: ACK 누적, 전원 완료 시 1초 후 GracefulQuit → 데디케이트
        /// </summary>
        private void HandleGracefulEnd(IClient client, Message message)
        {
            if (sessionManager == null || partyManager == null) return;

            using (DarkRiftReader reader = message.GetReader())
            {
                int partyId = reader.ReadInt32();

                if (sessionManager.IsAdmin(client))
                {
                    // 교관 → 서버: ACK 추적 초기화 후 훈련생에게 배포
                    Logger.Info($"GracefulEnd from instructor: PartyID={partyId}");

                    lock (instanceLock)
                    {
                        partyGracefulAcks[partyId] = new HashSet<string>();
                    }

                    string instructorId = sessionManager.GetAccountId(client);
                    var partyMembers = partyManager.GetPartyMembers(partyId);

                    if (partyMembers == null || partyMembers.Count == 0)
                    {
                        Logger.Warning($"GracefulEnd: No trainees in party {partyId}, sending GracefulQuit immediately");
                        Task.Delay(1000).ContinueWith(_ => SendGracefulQuitToInstance(partyId, 0));
                        return;
                    }

                    using (DarkRiftWriter writer = DarkRiftWriter.Create())
                    {
                        writer.Write(partyId);
                        using (Message notification = Message.Create(TAG_GRACEFUL_END, writer))
                        {
                            int notifiedCount = 0;
                            foreach (var memberId in partyMembers)
                            {
                                if (memberId == instructorId) continue;
                                IClient memberClient = sessionManager.GetClient(memberId);
                                if (memberClient != null)
                                {
                                    try
                                    {
                                        memberClient.SendMessage(notification, SendMode.Reliable);
                                        notifiedCount++;
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.Error($"GracefulEnd send to {memberId} failed: {ex.Message}");
                                    }
                                }
                            }
                            Logger.Info($"GracefulEnd sent to Party={partyId}, Notified={notifiedCount}");
                        }
                    }
                }
                else
                {
                    // 훈련생 → 서버: ACK 처리
                    string memberId = sessionManager.GetAccountId(client);
                    int ackedCount = 0;

                    lock (instanceLock)
                    {
                        if (!partyGracefulAcks.TryGetValue(partyId, out var ackSet))
                        {
                            Logger.Warning($"GracefulEnd ACK from {memberId}: no ack set for party {partyId}");
                            return;
                        }
                        ackSet.Add(memberId);
                        ackedCount = ackSet.Count;
                    }

                    IClient instructorClient = null;
                    lock (instanceLock)
                    {
                        partyToInstructor.TryGetValue(partyId, out instructorClient);
                    }
                    string instructorId = instructorClient != null ? sessionManager.GetAccountId(instructorClient) : null;

                    var partyMembers = partyManager.GetPartyMembers(partyId);
                    int totalTrainees = partyMembers != null
                        ? partyMembers.Count(m => m != instructorId)
                        : 0;

                    Logger.Info($"GracefulEnd ACK: Party={partyId}, {ackedCount}/{totalTrainees} ({memberId})");

                    if (totalTrainees > 0 && ackedCount >= totalTrainees)
                    {
                        Logger.Info($"GracefulEnd all ACKs received for Party={partyId}, sending GracefulQuit in 1s");
                        Task.Delay(1000).ContinueWith(_ => SendGracefulQuitToInstance(partyId, ackedCount));
                    }
                }
            }
        }

        /// <summary>
        /// 데디케이트 서버에 GracefulQuit(239) 전송 — 프로그램 종료 지시
        /// </summary>
        private void SendGracefulQuitToInstance(int partyId, int ackedCount)
        {
            try
            {
                IClient instanceClient = null;

                lock (instanceLock)
                {
                    if (partyToInstance.TryGetValue(partyId, out int instanceId))
                        instanceClients.TryGetValue(instanceId, out instanceClient);
                    partyGracefulAcks.Remove(partyId);
                }

                if (instanceClient == null)
                {
                    Logger.Warning($"GracefulQuit: No dedicate client for party {partyId}");
                    return;
                }

                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write(partyId);
                    using (Message quit = Message.Create(TAG_GRACEFUL_QUIT, writer))
                    {
                        instanceClient.SendMessage(quit, SendMode.Reliable);
                        Logger.Info($"GracefulQuit sent to dedicate: Party={partyId}, AckedTrainees={ackedCount}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"SendGracefulQuitToInstance error: {ex.Message}");
            }
        }

        /// <summary>
        /// Tag 241 HandleGracefulEndReady
        /// 훈련생이 종료 준비 완료를 알림. 전원 완료 또는 150초 후 교관에게 GracefulEndRequest 전송.
        /// </summary>
        private void HandleGracefulEndReady(IClient client, Message message)
        {
            if (sessionManager == null || partyManager == null) return;

            using (DarkRiftReader reader = message.GetReader())
            {
                int partyId = reader.ReadInt32();
                string memberId = sessionManager.GetAccountId(client);

                bool isFirstReady = false;
                int readyCount = 0;

                lock (instanceLock)
                {
                    if (!partyGracefulEndReadyAcks.TryGetValue(partyId, out var readySet))
                    {
                        readySet = new HashSet<string>();
                        partyGracefulEndReadyAcks[partyId] = readySet;
                        isFirstReady = true;
                    }
                    readySet.Add(memberId);
                    readyCount = readySet.Count;
                }

                IClient instructorClient = null;
                lock (instanceLock) { partyToInstructor.TryGetValue(partyId, out instructorClient); }
                string instructorId = instructorClient != null ? sessionManager.GetAccountId(instructorClient) : null;

                var partyMembers = partyManager.GetPartyMembers(partyId);
                int totalTrainees = partyMembers != null ? partyMembers.Count(m => m != instructorId) : 0;

                Logger.Info($"GracefulEndReady: Party={partyId}, {readyCount}/{totalTrainees} ({memberId})");

                if (totalTrainees > 0 && readyCount >= totalTrainees)
                {
                    Logger.Info($"GracefulEndReady: all trainees ready for Party={partyId}, sending GracefulEndRequest");
                    CancelGracefulEndReadyTimer(partyId);
                    SendGracefulEndRequestToInstructor(partyId);
                    return;
                }

                if (isFirstReady)
                {
                    var cts = new CancellationTokenSource();
                    lock (instanceLock) { partyGracefulEndReadyTimers[partyId] = cts; }

                    Task.Delay(150000, cts.Token).ContinueWith(t =>
                    {
                        if (!t.IsCanceled)
                        {
                            Logger.Info($"GracefulEndReady: 150s timeout for Party={partyId}, sending GracefulEndRequest");
                            SendGracefulEndRequestToInstructor(partyId);
                        }
                    });
                }
            }
        }

        private void CancelGracefulEndReadyTimer(int partyId)
        {
            CancellationTokenSource cts = null;
            lock (instanceLock)
            {
                partyGracefulEndReadyTimers.TryGetValue(partyId, out cts);
                partyGracefulEndReadyTimers.Remove(partyId);
            }
            cts?.Cancel();
            cts?.Dispose();
        }

        private void SendGracefulEndRequestToInstructor(int partyId)
        {
            try
            {
                IClient instructorClient = null;
                lock (instanceLock)
                {
                    partyToInstructor.TryGetValue(partyId, out instructorClient);
                    partyGracefulEndReadyAcks.Remove(partyId);
                }

                if (instructorClient == null)
                {
                    Logger.Warning($"GracefulEndRequest: No instructor for party {partyId}");
                    return;
                }

                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write(partyId);
                    using (Message req = Message.Create(TAG_GRACEFUL_END_REQUEST, writer))
                    {
                        instructorClient.SendMessage(req, SendMode.Reliable);
                        Logger.Info($"GracefulEndRequest sent to instructor: Party={partyId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"SendGracefulEndRequestToInstructor error: {ex.Message}");
            }
        }

        #endregion

        #region Ping Protocol (TAG 243/244/246)

        private void PingTick(object state)
        {
            SendPingProbes();
            SendAllPartyPingStatus();
        }

        private void SendPingProbes()
        {
            try
            {
                var clients = ClientManager.GetAllClients();
                long sentMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write(sentMs);
                    using (Message probe = Message.Create(TAG_PING_PROBE, writer))
                    {
                        foreach (var client in clients)
                        {
                            if (sessionManager == null) continue;
                            if (sessionManager.IsAdmin(client)) continue;
                            bool isInstance = false;
                            lock (instanceLock) { isInstance = instanceClients.ContainsValue(client); }
                            if (isInstance) continue;
                            if (sessionManager.GetAccountId(client) == null) continue;
                            try { client.SendMessage(probe, SendMode.Unreliable); }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.Warning($"[Ping] SendPingProbes error: {ex.Message}"); }
        }

        private void HandlePingProbeAck(IClient client, Message message)
        {
            try
            {
                long sentMs;
                int hwStatus;
                using (DarkRiftReader reader = message.GetReader())
                {
                    sentMs   = reader.ReadInt64();
                    hwStatus = reader.ReadInt32();
                }
                int rtt = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - sentMs);
                if (rtt < 0 || rtt > 30000) return;

                lock (instanceLock)
                {
                    _allClientPings[client.ID] = new ClientPingInfo { PingMs = rtt, HardwareStatus = hwStatus };
                }
            }
            catch (Exception ex) { Logger.Warning($"[Ping] HandlePingProbeAck error: {ex.Message}"); }
        }

        private void SendAllPartyPingStatus()
        {
            Dictionary<int, IClient> snapshot;
            lock (instanceLock) { snapshot = new Dictionary<int, IClient>(partyToInstructor); }

            foreach (var kvp in snapshot)
            {
                if (kvp.Value == null) continue;
                try { SendPartyPingStatus(kvp.Value, kvp.Key); }
                catch (Exception ex) { Logger.Warning($"[Ping] SendAllPartyPingStatus error party={kvp.Key}: {ex.Message}"); }
            }
        }

        private void SendPartyPingStatus(IClient instructorClient, int partyId)
        {
            var pm = EnsurePartyManager();
            var sm = sessionManager;
            if (pm == null || sm == null) return;

            var members = pm.GetPartyMembers(partyId);
            if (members == null || members.Count == 0) return;

            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                writer.Write(members.Count);
                foreach (var accountId in members)
                {
                    int pingMs = 0, hwStatus = 0;
                    IClient memberClient = sm.GetClient(accountId);
                    if (memberClient != null)
                    {
                        lock (instanceLock)
                        {
                            if (_allClientPings.TryGetValue(memberClient.ID, out var info))
                            {
                                pingMs   = info.PingMs;
                                hwStatus = info.HardwareStatus;
                            }
                        }
                    }
                    writer.Write(accountId);
                    writer.Write(partyId);
                    writer.Write(pingMs);
                    writer.Write(hwStatus);
                }
                using (Message msg = Message.Create(TAG_PARTY_PING_STATUS, writer))
                {
                    instructorClient.SendMessage(msg, SendMode.Unreliable);
                }
            }
        }

        #endregion

        #region Instance Message Handler

        /// <summary>
        /// 게임 인스턴스 등록 처리
        /// 헤드리스 인스턴스가 실행 후 DarkRift에 연결하여 자신을 등록
        /// </summary>
        private void HandleInstanceRegister(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    int instanceId = reader.ReadInt32();
                    int port = reader.ReadInt32();

                    GameInstanceData instance;
                    lock (instanceLock)
                    {
                        if (!instances.TryGetValue(instanceId, out instance))
                        {
                            SendErrorResponse(client, TAG_INSTANCE_REGISTER, ResponseCode.InstanceNotFound);
                            return;
                        }

                        instance.State = GameInstanceState.Registered;

                        Logger.Info($"Game instance registered: {instance}");

                        // 등록 성공 응답
                        SendSuccessResponse(client, TAG_INSTANCE_REGISTER);

                        // 파티 데이터 전송
                        if (instance.AssignedPartyId.HasValue)
                        {
                            SendPartyDataToInstance(instance);
                        }
                    }

                    // 파티 + 인스턴스에 Created 신호 브로드캐스트 (lock 밖에서)
                    if (instance.AssignedPartyId.HasValue)
                    {
                        BroadcastSignalToPartyAndInstance(instance, InstanceSignalType.Created, $"Instance {instanceId} connected");
                        NotifyAdminsInstanceStateChange(instance, "Instance registered and connected");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleInstanceRegister error: {ex.Message}");
                SendErrorResponse(client, TAG_INSTANCE_REGISTER, ResponseCode.InstanceError);
            }
        }

        /// <summary>
        /// 게임 인스턴스 준비 완료 처리
        /// </summary>
        private void HandleInstanceReady(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    int instanceId = reader.ReadInt32();

                    GameInstanceData instance;
                    lock (instanceLock)
                    {
                        if (!instances.TryGetValue(instanceId, out instance))
                        {
                            return;
                        }

                        instance.State = GameInstanceState.Ready;
                        Logger.Info($"Game instance ready: {instance}");

                        NotifyAdminsInstanceStateChange(instance, "Instance is now ready");
                    }

                    // 파티 멤버들에게 게임 서버 정보 전송 (lock 밖에서)
                    if (instance.AssignedPartyId.HasValue)
                    {
                        // 기존 방식 (호환성 유지)
                        NotifyPartyMembersGameReady(instance);

                        // 새 플로우: Fishnet 접속 정보 전송
                        SendTrainingSetupToPartyMembers(instance);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleInstanceReady error: {ex.Message}");
            }
        }

        /// <summary>
        /// 하트비트 처리
        /// </summary>
        private void HandleInstanceHeartbeat(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    int instanceId = reader.ReadInt32();

                    lock (instanceLock)
                    {
                        if (instances.TryGetValue(instanceId, out GameInstanceData instance))
                        {
                            instance.LastHeartbeat = DateTime.Now;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleInstanceHeartbeat error: {ex.Message}");
            }
        }

        /// <summary>
        /// 인스턴스 종료 처리
        /// </summary>
        private void HandleInstanceShutdown(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    int instanceId = reader.ReadInt32();

                    GameInstanceData instance;
                    lock (instanceLock)
                    {
                        if (!instances.TryGetValue(instanceId, out instance))
                            return;
                    }

                    Logger.Info($"Game instance shutdown requested: {instance}");

                    // 브로드캐스트와 클린업은 lock 밖에서 수행
                    if (instance.AssignedPartyId.HasValue)
                    {
                        BroadcastSignalToPartyAndInstance(instance, InstanceSignalType.Stop, "Instance shutdown");
                    }

                    CleanupInstance(instance);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleInstanceShutdown error: {ex.Message}");
            }
        }

        /// <summary>
        /// 인스턴스 신호 처리 (Ready/Start/Stop)
        /// 인스턴스가 보낸 신호를 파티 전체 + 인스턴스에 브로드캐스트
        /// </summary>
        private void HandleInstanceSignal(IClient client, Message message, InstanceSignalType signalType)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    int instanceId = reader.ReadInt32();
                    string additionalData = reader.Length > 4 ? reader.ReadString() : string.Empty;

                    GameInstanceData instance;
                    lock (instanceLock)
                    {
                        if (!instances.TryGetValue(instanceId, out instance))
                        {
                            Logger.Warning($"Instance signal from unknown instance: {instanceId}");
                            SendErrorResponse(client, TAG_INSTANCE_SIGNAL_BROADCAST, ResponseCode.InstanceNotFound);
                            return;
                        }

                        // 인스턴스 클라이언트 검증
                        if (!instanceClients.TryGetValue(instance.InstanceId, out var registeredClient) || registeredClient != client)
                        {
                            Logger.Warning($"Instance signal from unauthorized client: InstanceID={instanceId}, ClientID={client.ID}");
                            SendErrorResponse(client, TAG_INSTANCE_SIGNAL_BROADCAST, ResponseCode.InstanceError);
                            return;
                        }

                        // 상태 업데이트
                        switch (signalType)
                        {
                            case InstanceSignalType.Created:
                                instance.State = GameInstanceState.Registered;
                                break;
                            case InstanceSignalType.Ready:
                                instance.State = GameInstanceState.Ready;
                                break;
                            case InstanceSignalType.Start:
                                instance.State = GameInstanceState.Running;
                                break;
                            case InstanceSignalType.Stop:
                                instance.State = GameInstanceState.Stopping;
                                break;
                            case InstanceSignalType.Pause:
                            case InstanceSignalType.Resume:
                                // 상태는 Running 유지
                                break;
                        }
                    }

                    Logger.Info($"Instance signal received: {signalType} from Instance {instanceId} (Party: {instance.AssignedPartyId})");

                    // 파티 + 인스턴스에 브로드캐스트
                    if (instance.AssignedPartyId.HasValue)
                    {
                        BroadcastSignalToPartyAndInstance(instance, signalType, additionalData);
                    }

                    // 관리자에게 상태 변경 알림
                    NotifyAdminsInstanceStateChange(instance, $"Signal: {signalType}");

                    // 인스턴스에 ACK 응답
                    SendSuccessResponse(client, TAG_INSTANCE_SIGNAL_BROADCAST);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleInstanceSignal error: {ex.Message}");
                SendErrorResponse(client, TAG_INSTANCE_SIGNAL_BROADCAST, ResponseCode.InstanceError);
            }
        }

        /// <summary>
        /// 인스턴스 연결 끊김 처리
        /// </summary>
        private void HandleInstanceDisconnect(IClient client)
        {
            GameInstanceData instance;
            lock (instanceLock)
            {
                instance = instances.Values.FirstOrDefault(i => instanceClients.TryGetValue(i.InstanceId, out var c) && c == client);
            }

            if (instance != null)
            {
                Logger.Warning($"Game instance disconnected: {instance}");
                CleanupInstance(instance);
            }
        }

        #endregion

        #region Instance Management

        /// <summary>
        /// 사용 가능한 인스턴스 찾기
        /// </summary>
        private GameInstanceData GetAvailableInstance()
        {
            lock (instanceLock)
            {
                return instances.Values.FirstOrDefault(i => i.IsAvailable);
            }
        }

        /// <summary>
        /// 게임 인스턴스 시작
        /// </summary>
        private bool StartGameInstance(GameInstanceData instance, int partyId, string sceneFile)
        {
            try
            {
                // 실행 파일 존재 확인
                if (!File.Exists(GAME_EXECUTABLE_PATH))
                {
                    Logger.Error($"Game executable not found: {GAME_EXECUTABLE_PATH}");
                    return false;
                }

                // 프로세스 시작 정보
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = GAME_EXECUTABLE_PATH,
                    Arguments = $"-batchmode -nographics -instanceId {instance.InstanceId} -port {instance.Port} -partyId {partyId} -scene {sceneFile}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // 프로세스 시작
                Process process = new Process { StartInfo = startInfo };

                bool started = process.Start();
                if (!started)
                {
                    Logger.Error($"Failed to start game instance {instance.InstanceId}");
                    return false;
                }

                // 인스턴스 정보 업데이트
                lock (instanceLock)
                {
                    instanceProcesses[instance.InstanceId] = process;
                    instance.State = GameInstanceState.Starting;
                    instance.AssignedPartyId = partyId;
                    instance.StartTime = DateTime.Now;
                }

                Logger.Info($"Game instance process started: PID={process.Id}, {instance}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"StartGameInstance error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 파티 데이터를 인스턴스로 전송
        /// </summary>
        private void SendPartyDataToInstance(GameInstanceData instance)
        {
            IClient instanceClient;
            lock (instanceLock)
            {
                if (!instanceClients.TryGetValue(instance.InstanceId, out instanceClient) || !instance.AssignedPartyId.HasValue)
                    return;
            }

            try
            {
                // PartyManager에서 파티 데이터 가져오기 (여기서는 간단하게 처리)
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write((byte)ResponseCode.Success);
                    writer.Write(instance.AssignedPartyId.Value);

                    using (Message response = Message.Create(TAG_PARTY_DATA, writer))
                    {
                        instanceClient.SendMessage(response, SendMode.Reliable);
                    }
                }

                Logger.Info($"Party data sent to instance {instance.InstanceId}");
            }
            catch (Exception ex)
            {
                Logger.Error($"SendPartyDataToInstance error: {ex.Message}");
            }
        }

        /// <summary>
        /// 파티 멤버들에게 게임 서버 준비 완료 알림
        /// </summary>
        private void NotifyPartyMembersGameReady(GameInstanceData instance)
        {
            if (sessionManager == null || !instance.AssignedPartyId.HasValue)
                return;

            try
            {
                int partyId = instance.AssignedPartyId.Value;

                // PartyManager에서 파티 멤버 목록 조회
                IReadOnlyList<string> partyMembers;
                if (partyManager != null)
                {
                    partyMembers = partyManager.GetPartyMembers(partyId);
                }
                else
                {
                    // PartyManager가 없으면 fallback으로 모든 클라이언트에게 전송 (비권장)
                    Logger.Warning($"PartyManager not available, broadcasting to all clients");
                    partyMembers = null;
                }

                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write((byte)ResponseCode.Success);
                    writer.Write(instance);

                    using (Message notification = Message.Create(TAG_TRAINING_INFO, writer))
                    {
                        int notifiedCount = 0;

                        if (partyMembers != null && partyMembers.Count > 0)
                        {
                            // 파티 멤버에게만 알림 전송
                            foreach (var memberId in partyMembers)
                            {
                                IClient memberClient = sessionManager.GetClient(memberId);
                                if (memberClient != null)
                                {
                                    try
                                    {
                                        memberClient.SendMessage(notification, SendMode.Reliable);
                                        notifiedCount++;
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.Error($"Failed to notify party member {memberId}: {ex.Message}");
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Fallback: 모든 클라이언트에게 전송
                            var allClients = sessionManager.GetAllClients();
                            foreach (var client in allClients)
                            {
                                try
                                {
                                    client.SendMessage(notification, SendMode.Reliable);
                                    notifiedCount++;
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error($"Failed to notify client: {ex.Message}");
                                }
                            }
                        }

                        Logger.Info($"Game ready notification sent for party {partyId}: {notifiedCount} clients notified");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"NotifyPartyMembersGameReady error: {ex.Message}");
            }
        }

        /// <summary>
        /// 인스턴스 클린업
        /// lock 내부에서는 상태 변경만, 블로킹 I/O(프로세스 종료)와 네트워크 I/O(알림)는 lock 밖에서 수행
        /// </summary>
        private void CleanupInstance(GameInstanceData instance)
        {
            Process procToKill = null;

            lock (instanceLock)
            {
                // 이미 정리된 인스턴스면 스킵 (TOCTOU 방지)
                if (instance.State == GameInstanceState.Idle)
                    return;

                try
                {
                    // 프로세스 참조 추출 (kill은 lock 밖에서)
                    if (instanceProcesses.TryGetValue(instance.InstanceId, out var proc) && !proc.HasExited)
                    {
                        procToKill = proc;
                    }

                    // 파티 매핑 제거
                    if (instance.AssignedPartyId.HasValue)
                    {
                        int partyId = instance.AssignedPartyId.Value;
                        partyToInstance.Remove(partyId);
                        partyToInstructor.Remove(partyId);
                        partyReadyTrainees.Remove(partyId);
                        partyGracefulAcks.Remove(partyId);
                        pausedParties.Remove(partyId);
                        partyTrainingStartTime.Remove(partyId);
                        partyTotalPausedTime.Remove(partyId);
                        partyPauseStartTime.Remove(partyId);
                        partyGracefulEndReadyAcks.Remove(partyId);
                        if (partyGracefulEndReadyTimers.TryGetValue(partyId, out var readyTimerCts))
                        {
                            readyTimerCts.Cancel();
                            readyTimerCts.Dispose();
                            partyGracefulEndReadyTimers.Remove(partyId);
                        }

                        // 관전 상태 초기화 (메모리 릭 방지)
                        if (observingPartyId == partyId)
                        {
                            observingPartyId = -1;
                            Logger.Info($"CleanupInstance: ObservingPartyId reset for Party {partyId}");
                        }
                    }

                    // 인스턴스 리셋
                    instanceProcesses.Remove(instance.InstanceId);
                    instanceClients.Remove(instance.InstanceId);
                    pausedInstances.Remove(instance.InstanceId);
                    instance.State = GameInstanceState.Idle;
                    instance.AssignedPartyId = null;
                    instance.LastHeartbeat = null;
                }
                catch (Exception ex)
                {
                    Logger.Error($"CleanupInstance state cleanup error: {ex.Message}");
                }
            }

            // 블로킹 I/O: lock 밖에서 프로세스 종료
            if (procToKill != null)
            {
                try
                {
                    if (!procToKill.HasExited)
                    {
                        procToKill.Kill();
                        if (!procToKill.WaitForExit(5000))
                        {
                            // 5초 내 종료 안 되면 프로세스 트리까지 강제 종료
                            try
                            {
                                using (var killer = new Process())
                                {
                                    killer.StartInfo.FileName = "taskkill";
                                    killer.StartInfo.Arguments = $"/F /T /PID {procToKill.Id}";
                                    killer.StartInfo.CreateNoWindow = true;
                                    killer.StartInfo.UseShellExecute = false;
                                    killer.Start();
                                    killer.WaitForExit(3000);
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"CleanupInstance taskkill fallback error: {ex.Message}");
                            }
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    // 이미 종료됨 — 무시
                }
                catch (Exception ex)
                {
                    Logger.Error($"CleanupInstance process kill error: {ex.Message}");
                }
                finally
                {
                    try { procToKill.Dispose(); } catch { }
                }
            }

            // 네트워크 I/O: lock 밖에서 관리자 알림
            try
            {
                NotifyAdminsInstanceStateChange(instance, "Instance shutting down");
            }
            catch (Exception ex)
            {
                Logger.Error($"CleanupInstance notify error: {ex.Message}");
            }

            Logger.Info($"Game instance cleaned up: ID={instance.InstanceId}");
        }

        /// <summary>
        /// 하트비트 체크 (타이머 콜백)
        /// </summary>
        private void CheckHeartbeats(object state)
        {
            List<GameInstanceData> timedOut = null;

            lock (instanceLock)
            {
                foreach (var instance in instances.Values)
                {
                    if (instance.State != GameInstanceState.Idle && instance.IsHeartbeatTimeout())
                    {
                        // 일시정지 중인 인스턴스는 하트비트 타임아웃 무시 (Time.timeScale=0 → 하트비트 중단)
                        if (instance.AssignedPartyId > 0 && pausedParties.Contains((int)instance.AssignedPartyId))
                            continue;
                        if (!instance.AssignedPartyId.HasValue && pausedInstances.Contains(instance.InstanceId))
                            continue;

                        if (timedOut == null) timedOut = new List<GameInstanceData>();
                        timedOut.Add(instance);
                    }
                }
            }

            // lock 밖에서 클린업 수행
            if (timedOut != null)
            {
                foreach (var instance in timedOut)
                {
                    Logger.Warning($"Game instance heartbeat timeout: {instance}");
                    CleanupInstance(instance);
                }
            }
        }

        /// <summary>
        /// 파티 멤버 + 인스턴스에 신호 브로드캐스트
        /// </summary>
        private void BroadcastSignalToPartyAndInstance(GameInstanceData instance, InstanceSignalType signalType, string additionalData = "")
        {
            if (!instance.AssignedPartyId.HasValue)
            {
                Logger.Warning($"BroadcastSignal: Instance {instance.InstanceId} has no assigned party");
                return;
            }

            int partyId = instance.AssignedPartyId.Value;

            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write((byte)ResponseCode.Success);
                    writer.Write((byte)signalType);
                    writer.Write(instance.InstanceId);
                    writer.Write(partyId);
                    writer.Write(additionalData ?? string.Empty);
                    writer.Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                    using (Message notification = Message.Create(TAG_INSTANCE_SIGNAL_BROADCAST, writer))
                    {
                        int notifiedCount = 0;

                        // 1. 파티 멤버들에게 브로드캐스트
                        var pm = EnsurePartyManager();
                        if (pm != null && sessionManager != null)
                        {
                            var partyMembers = pm.GetPartyMembers(partyId);
                            foreach (var memberId in partyMembers)
                            {
                                IClient memberClient = sessionManager.GetClient(memberId);
                                if (memberClient != null)
                                {
                                    try
                                    {
                                        memberClient.SendMessage(notification, SendMode.Reliable);
                                        notifiedCount++;
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.Error($"Failed to send signal to party member {memberId}: {ex.Message}");
                                    }
                                }
                            }
                        }

                        // 2. 인스턴스에게도 브로드캐스트 (다른 인스턴스나 자기 자신 확인용)
                        IClient instanceClient;
                        lock (instanceLock)
                        {
                            instanceClients.TryGetValue(instance.InstanceId, out instanceClient);
                        }
                        if (instanceClient != null)
                        {
                            try
                            {
                                instanceClient.SendMessage(notification, SendMode.Reliable);
                                notifiedCount++;
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"Failed to send signal to instance {instance.InstanceId}: {ex.Message}");
                            }
                        }

                        Logger.Info($"Signal broadcast: {signalType} for Party {partyId} - {notifiedCount} recipients");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"BroadcastSignalToPartyAndInstance error: {ex.Message}");
            }
        }

        /// <summary>
        /// 파티 없이 인스턴스에만 직접 시그널 전송 (ForceStart 등에서 사용)
        /// </summary>
        private void SendSignalToInstance(GameInstanceData instance, InstanceSignalType signalType, string additionalData = "")
        {
            try
            {
                IClient instanceClient;
                lock (instanceLock)
                {
                    if (!instanceClients.TryGetValue(instance.InstanceId, out instanceClient))
                    {
                        Logger.Warning($"SendSignalToInstance: Instance {instance.InstanceId} client not connected");
                        return;
                    }
                }

                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write((byte)ResponseCode.Success);
                    writer.Write((byte)signalType);
                    writer.Write(instance.InstanceId);
                    writer.Write(instance.AssignedPartyId ?? 0);
                    writer.Write(additionalData ?? string.Empty);
                    writer.Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                    using (Message notification = Message.Create(TAG_INSTANCE_SIGNAL_BROADCAST, writer))
                    {
                        instanceClient.SendMessage(notification, SendMode.Reliable);
                        Logger.Info($"SendSignalToInstance: {signalType} sent to Instance {instance.InstanceId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"SendSignalToInstance error: {ex.Message}");
            }
        }

        #endregion

        #region Training Control Handlers

        /// <summary>
        /// 파티 확정 처리 (교관 -> 서버 -> 파티원)
        /// 최종 파티 구성 및 설정을 파티원들에게 브로드캐스트
        /// </summary>
        private void HandlePartyConfirm(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    int partyId = reader.ReadInt32();

                    Logger.Info($"Party confirm request: PartyID={partyId}");

                    // 의존성 검증
                    if (partyManager == null || sessionManager == null)
                    {
                        Logger.Error("Party confirm failed: PartyManager or SessionManager not injected");
                        SendErrorResponse(client, TAG_PARTY_CONFIRM, ResponseCode.ServerError);
                        return;
                    }

                    // 교관 권한 확인
                    if (!sessionManager.IsAdmin(client))
                    {
                        Logger.Warning("Party confirm denied: Not an admin");
                        SendErrorResponse(client, TAG_PARTY_CONFIRM, ResponseCode.Unauthorized);
                        return;
                    }

                    // 파티 존재 여부 확인
                    var party = partyManager.GetPartyById(partyId);
                    if (party == null)
                    {
                        Logger.Warning($"Party confirm denied: Party {partyId} not found");
                        SendErrorResponse(client, TAG_PARTY_CONFIRM, ResponseCode.PartyNotFound);
                        return;
                    }

                    // 교관 등록 (훈련 생성완료 알림 수신용)
                    lock (instanceLock)
                    {
                        partyToInstructor[partyId] = client;
                        // 준비 상태 초기화
                        partyReadyTrainees[partyId] = new HashSet<string>();
                    }

                    // 파티원들에게 파티 확정 브로드캐스트
                    BroadcastPartyConfirmToMembers(party);

                    // 교관에게 성공 응답
                    SendSuccessResponse(client, TAG_PARTY_CONFIRM);

                    // 이미 실행 중인지 확인
                    lock (instanceLock)
                    {
                        if (partyToInstance.ContainsKey(partyId))
                        {
                            SendErrorResponse(client, TAG_PARTY_CONFIRM, ResponseCode.AlreadyRunning);
                            Logger.Warning($"Training already running for party {partyId}");
                            return;
                        }
                    }

                    // 사용 가능한 인스턴스 찾기
                    GameInstanceData instance = GetAvailableInstance();
                    if (instance == null)
                    {
                        SendErrorResponse(client, TAG_PARTY_CONFIRM, ResponseCode.NoAvailableInstance);
                        Logger.Warning("No available game instance");
                        return;
                    }

                    // 인스턴스 시작 (프로세스 실행)
                    bool started = StartGameInstance(instance, partyId, party.SceneSetFile);
                    if (!started)
                    {
                        SendErrorResponse(client, TAG_PARTY_CONFIRM, ResponseCode.ServerError);
                        return;
                    }

                    // 파티-인스턴스 매핑
                    lock (instanceLock)
                    {
                        partyToInstance[partyId] = instance.InstanceId;
                        partyToInstructor[partyId] = client;  // 교관 갱신
                    }

                    Logger.Info($"Party confirmed: PartyID={partyId}, Members={party.MemberCount}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandlePartyConfirm error: {ex.Message}");
                SendErrorResponse(client, TAG_PARTY_CONFIRM, ResponseCode.ServerError);
            }
        }

        /// <summary>
        /// 인스턴스 기상 처리 (인스턴스 -> 서버)
        /// 인스턴스가 실행되어 DarkRift에 연결 후 Fishnet IP/Port 전달
        /// </summary>
        private void HandleTrainingWake(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    TrainingWakeData wakeData = reader.ReadSerializable<TrainingWakeData>();

                    Logger.Info($"Training wake: {wakeData}");

                    // 의존성 검증
                    if (partyManager == null || sessionManager == null)
                    {
                        Logger.Error("Training wake failed: Dependencies not injected");
                        SendErrorResponse(client, TAG_TRAINING_WAKE_RESULT, ResponseCode.ServerError);
                        return;
                    }

                    GameInstanceData instance;
                    int partyId;
                    PartyManagerPlugin.Party party;

                    lock (instanceLock)
                    {
                        // 인스턴스 찾기
                        if (!instances.TryGetValue(wakeData.InstanceId, out instance))
                        {
                            Logger.Warning($"Training wake denied: Instance {wakeData.InstanceId} not found");
                            SendErrorResponse(client, TAG_TRAINING_WAKE_RESULT, ResponseCode.InstanceNotFound);
                            return;
                        }

                        // 파티 ID 확인 (상태 변경 전에 먼저 검증)
                        if (!instance.AssignedPartyId.HasValue)
                        {
                            Logger.Warning($"Training wake: Instance {wakeData.InstanceId} has no assigned party");
                            SendErrorResponse(client, TAG_TRAINING_WAKE_RESULT, ResponseCode.PartyNotFound);
                            return;
                        }

                        partyId = instance.AssignedPartyId.Value;

                        // 모든 검증 통과 후 인스턴스에 클라이언트 연결 및 상태 변경
                        instanceClients[instance.InstanceId] = client;
                        instance.State = GameInstanceState.Registered;
                        // LastHeartbeat는 설정하지 않음 — 클라이언트의 첫 실제 하트비트가 도착할 때까지 타임아웃 비활성
                        // (IsHeartbeatTimeout()은 LastHeartbeat == null이면 false 반환)
                    }

                    // 파티 정보 가져오기
                    party = partyManager.GetPartyById(partyId);
                    if (party == null)
                    {
                        Logger.Warning($"Training wake: Party {partyId} not found — full cleanup of Instance {instance.InstanceId}");
                        // 파티가 사라졌으므로 인스턴스를 완전히 정리 (프로세스 종료 + 매핑 제거 + 상태 리셋)
                        CleanupInstance(instance);
                        SendErrorResponse(client, TAG_TRAINING_WAKE_RESULT, ResponseCode.PartyNotFound);
                        return;
                    }

                    // 파티원 목록 가져오기
                    var memberIds = partyManager.GetPartyMembers(partyId);

                    // TrainingWakeResult 생성
                    TrainingWakeResultData wakeResult = new TrainingWakeResultData(
                        partyId,
                        instance.InstanceId,
                        wakeData.ServerAddress,
                        wakeData.ServerPort,
                        party.SceneFile,
                        party.SceneSetFile,
                        memberIds.ToArray()
                    );

                    // 인스턴스에 WakeResult 전송
                    SendTrainingWakeResultToInstance(client, wakeResult);

                    // 훈련생들에게 WakeResult 전송
                    SendTrainingWakeResultToTrainees(partyId, wakeResult);

                    Logger.Info($"Training wake completed: Instance={wakeData.InstanceId}, Party={partyId}, Members={memberIds.Count}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleTrainingWake error: {ex.Message}");
                SendErrorResponse(client, TAG_TRAINING_WAKE_RESULT, ResponseCode.InstanceError);
            }
        }

        /// <summary>
        /// 훈련 생성 처리 (인스턴스 -> 서버)
        /// 인스턴스 Fishnet 서버 준비 완료
        /// </summary>
        private void HandleTrainingCreate(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    TrainingCreateData createData = reader.ReadSerializable<TrainingCreateData>();

                    Logger.Info($"Training create: {createData}");

                    // 의존성 검증
                    if (partyManager == null || sessionManager == null)
                    {
                        Logger.Error("Training create failed: Dependencies not injected");
                        SendErrorResponse(client, TAG_TRAINING_CREATE, ResponseCode.ServerError);
                        return;
                    }

                    GameInstanceData instance;
                    lock (instanceLock)
                    {
                        // 인스턴스 찾기
                        if (!instances.TryGetValue(createData.InstanceId, out instance))
                        {
                            Logger.Warning($"Training create denied: Instance {createData.InstanceId} not found");
                            SendErrorResponse(client, TAG_TRAINING_CREATE, ResponseCode.InstanceNotFound);
                            return;
                        }

                        // 인스턴스 상태 업데이트
                        if (createData.IsReady)
                        {
                            instance.State = GameInstanceState.Ready;
                        }
                    }

                    // 파티 정보 가져오기
                    var party = partyManager.GetPartyById(createData.PartyId);
                    if (party == null)
                    {
                        Logger.Warning($"Training create: Party {createData.PartyId} not found");
                        SendErrorResponse(client, TAG_TRAINING_CREATE, ResponseCode.PartyNotFound);
                        return;
                    }

                    // 인스턴스에 성공 응답
                    SendSuccessResponse(client, TAG_TRAINING_CREATE);

                    // 훈련생들에게 TrainingCreated 전송 (Fishnet 접속 정보)
                    SendTrainingCreatedToTrainees(instance, party);

                    Logger.Info($"Training create completed: Instance={createData.InstanceId}, Party={createData.PartyId}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleTrainingCreate error: {ex.Message}");
                SendErrorResponse(client, TAG_TRAINING_CREATE, ResponseCode.ServerError);
            }
        }

        /// <summary>
        /// 훈련 시작 준비 완료 처리 (인스턴스 -> 서버 -> 교관)
        /// 모든 파티원 Fishnet 접속 완료
        /// </summary>
        private void HandleTrainingBeginReady(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    TrainingBeginReadyData readyData = reader.ReadSerializable<TrainingBeginReadyData>();

                    Logger.Info($"Training begin ready: {readyData}");

                    // 의존성 검증
                    if (sessionManager == null)
                    {
                        Logger.Error("Training begin ready failed: SessionManager not injected");
                        SendErrorResponse(client, TAG_TRAINING_BEGIN_READY, ResponseCode.ServerError);
                        return;
                    }

                    // 인스턴스에 성공 응답
                    SendSuccessResponse(client, TAG_TRAINING_BEGIN_READY);

                    // 교관에게 전달
                    IClient instructorClient;
                    lock (instanceLock)
                    {
                        if (!partyToInstructor.TryGetValue(readyData.PartyId, out instructorClient) || instructorClient == null)
                        {
                            Logger.Warning($"No instructor registered for party {readyData.PartyId}");
                            return;
                        }
                    }

                    // 교관에게 TrainingBeginReady 전송
                    try
                    {
                        using (DarkRiftWriter writer = DarkRiftWriter.Create())
                        {
                            writer.Write((byte)ResponseCode.Success);
                            writer.Write(readyData);

                            using (Message notification = Message.Create(TAG_TRAINING_BEGIN_READY, writer))
                            {
                                instructorClient.SendMessage(notification, SendMode.Reliable);
                            }
                        }

                        Logger.Info($"Training begin ready sent to instructor: Party={readyData.PartyId}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to send training begin ready to instructor: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleTrainingBeginReady error: {ex.Message}");
                SendErrorResponse(client, TAG_TRAINING_BEGIN_READY, ResponseCode.ServerError);
            }
        }

        /// <summary>
        /// 훈련 생성완료 처리 (파티원 -> 서버 -> 교관)
        /// 파티원이 Fishnet 연결 완료 후 서버에 알림
        /// </summary>
        private void HandleTrainingCreated(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    TrainingCreatedNotification notification = reader.ReadSerializable<TrainingCreatedNotification>();

                    Logger.Info($"Training created notification: {notification}");

                    // 의존성 검증
                    if (partyManager == null || sessionManager == null)
                    {
                        Logger.Error("Training created failed: PartyManager or SessionManager not injected");
                        SendErrorResponse(client, TAG_TRAINING_CREATED, ResponseCode.ServerError);
                        return;
                    }

                    // 요청자 계정 ID 확인
                    string accountId = sessionManager.GetAccountId(client);
                    if (string.IsNullOrWhiteSpace(accountId))
                    {
                        Logger.Warning("Training created denied: Not logged in");
                        SendErrorResponse(client, TAG_TRAINING_CREATED, ResponseCode.ServerError);
                        return;
                    }

                    // 파티 멤버인지 확인
                    if (!partyManager.IsMemberOfParty(accountId, notification.PartyId))
                    {
                        Logger.Warning($"Training created denied: {accountId} is not a member of party {notification.PartyId}");
                        SendErrorResponse(client, TAG_TRAINING_CREATED, ResponseCode.ServerError);
                        return;
                    }

                    // 준비 상태 업데이트
                    int totalMembers = 0;
                    int readyMembers = 0;
                    string[] readyIds;

                    lock (instanceLock)
                    {
                        if (!partyReadyTrainees.TryGetValue(notification.PartyId, out var readySet))
                        {
                            readySet = new HashSet<string>();
                            partyReadyTrainees[notification.PartyId] = readySet;
                        }

                        if (notification.IsReady)
                        {
                            readySet.Add(accountId);
                        }
                        else
                        {
                            readySet.Remove(accountId);
                        }

                        readyIds = readySet.ToArray();
                        readyMembers = readySet.Count;
                    }

                    // 파티 멤버 수 확인
                    var party = partyManager.GetPartyById(notification.PartyId);
                    totalMembers = party?.MemberCount ?? 0;

                    // 교관에게 상태 알림
                    NotifyInstructorTrainingStatus(notification.PartyId, totalMembers, readyMembers, readyIds);

                    // 클라이언트에게 성공 응답
                    SendSuccessResponse(client, TAG_TRAINING_CREATED);

                    Logger.Info($"Training created: Party={notification.PartyId}, Account={accountId}, Ready={readyMembers}/{totalMembers}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleTrainingCreated error: {ex.Message}");
                SendErrorResponse(client, TAG_TRAINING_CREATED, ResponseCode.ServerError);
            }
        }

        /// <summary>
        /// 훈련 시작 처리 (교관 -> 서버 -> GameInstance)
        /// 실제 게임플레이 활성화
        /// </summary>
        private void HandleTrainingBegin(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    int partyId = reader.ReadInt32();

                    Logger.Info($"Training begin request: PartyID={partyId}");

                    // 의존성 검증
                    if (partyManager == null || sessionManager == null)
                    {
                        Logger.Error("Training begin failed: PartyManager or SessionManager not injected");
                        SendErrorResponse(client, TAG_TRAINING_BEGIN, ResponseCode.ServerError);
                        return;
                    }

                    // 교관 권한 확인
                    if (!sessionManager.IsAdmin(client))
                    {
                        Logger.Warning("Training begin denied: Not an admin");
                        SendErrorResponse(client, TAG_TRAINING_BEGIN, ResponseCode.Unauthorized);
                        return;
                    }

                    // 인스턴스 확인
                    GameInstanceData instance;
                    lock (instanceLock)
                    {
                        if (!partyToInstance.TryGetValue(partyId, out int instanceId) ||
                            !instances.TryGetValue(instanceId, out instance))
                        {
                            Logger.Warning($"Training begin denied: No instance for party {partyId}");
                            SendErrorResponse(client, TAG_TRAINING_BEGIN, ResponseCode.InstanceNotFound);
                            return;
                        }

                        // 인스턴스가 Ready 상태인지 확인
                        if (instance.State != GameInstanceState.Ready)
                        {
                            Logger.Warning($"Training begin denied: Instance {instanceId} is not ready (State: {instance.State})");
                            SendErrorResponse(client, TAG_TRAINING_BEGIN, ResponseCode.ServerError);
                            return;
                        }
                    }

                    // GameInstance에 시작 신호 전송
                    bool hasInstanceClient;
                    lock (instanceLock)
                    {
                        hasInstanceClient = instanceClients.ContainsKey(instance.InstanceId);
                    }

                    if (hasInstanceClient)
                    {
                        SendTrainingBeginToInstance(instance, partyId);
                    }
                    else
                    {
                        Logger.Warning($"Training begin: Instance {instance.InstanceId} client is null");
                        SendErrorResponse(client, TAG_TRAINING_BEGIN, ResponseCode.InstanceNotFound);
                        return;
                    }

                    // 훈련생들에게 TrainingBegin 전송
                    SendTrainingBeginToTrainees(partyId, instance.InstanceId);

                    // 인스턴스 상태 업데이트
                    lock (instanceLock)
                    {
                        instance.State = GameInstanceState.Running;
                        partyTrainingStartTime[partyId] = DateTime.UtcNow;
                        partyTotalPausedTime[partyId] = TimeSpan.Zero;
                        partyPauseStartTime.Remove(partyId);
                    }

                    // 교관에게 성공 응답
                    SendSuccessResponse(client, TAG_TRAINING_BEGIN);

                    // 관리자에게 알림
                    NotifyAdminsInstanceStateChange(instance, "Training started");

                    Logger.Info($"Training begin: Party={partyId}, Instance={instance.InstanceId}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleTrainingBegin error: {ex.Message}");
                SendErrorResponse(client, TAG_TRAINING_BEGIN, ResponseCode.ServerError);
            }
        }

        /// <summary>
        /// 훈련 일시정지/재개 토글 (교관 → 서버 → 파티+인스턴스)
        /// pausedParties HashSet으로 토글 상태 관리
        /// </summary>
        private void HandleTrainingPause(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    int partyId = reader.ReadInt32();

                    Logger.Info($"Training pause toggle request: PartyID={partyId}");

                    // 교관 권한 확인
                    if (sessionManager == null || !sessionManager.IsAdmin(client))
                    {
                        Logger.Warning("Training pause denied: Not an admin");
                        SendErrorResponse(client, TAG_TRAINING_PAUSE, ResponseCode.Unauthorized);
                        return;
                    }

                    // 인스턴스 확인 + pausedParties 토글 (lock 내에서 원자적으로 수행)
                    GameInstanceData instance;
                    InstanceSignalType resultSignal;
                    lock (instanceLock)
                    {
                        if (!partyToInstance.TryGetValue(partyId, out int instanceId) ||
                            !instances.TryGetValue(instanceId, out instance))
                        {
                            Logger.Warning($"Training pause denied: No instance for party {partyId}");
                            SendErrorResponse(client, TAG_TRAINING_PAUSE, ResponseCode.InstanceNotFound);
                            return;
                        }

                        if (instance.State != GameInstanceState.Running)
                        {
                            Logger.Warning($"Training pause denied: Instance {instanceId} is not running (State: {instance.State})");
                            SendErrorResponse(client, TAG_TRAINING_PAUSE, ResponseCode.ServerError);
                            return;
                        }

                        // 토글: pausedParties에 있으면 Resume, 없으면 Pause
                        if (pausedParties.Contains(partyId))
                        {
                            pausedParties.Remove(partyId);
                            resultSignal = InstanceSignalType.Resume;
                            // Resume: 일시정지 경과 시간을 누적
                            if (partyPauseStartTime.TryGetValue(partyId, out DateTime pauseStart))
                            {
                                if (!partyTotalPausedTime.ContainsKey(partyId))
                                    partyTotalPausedTime[partyId] = TimeSpan.Zero;
                                partyTotalPausedTime[partyId] += DateTime.UtcNow - pauseStart;
                                partyPauseStartTime.Remove(partyId);
                            }
                        }
                        else
                        {
                            pausedParties.Add(partyId);
                            resultSignal = InstanceSignalType.Pause;
                            // Pause: 일시정지 시작 시각 기록
                            partyPauseStartTime[partyId] = DateTime.UtcNow;
                        }
                    }

                    // 파티+인스턴스에 신호 브로드캐스트
                    BroadcastSignalToPartyAndInstance(instance, resultSignal, "Training pause toggle");

                    // 교관에게 결과 응답 (tag 231: ResponseCode + resultSignalType + partyId)
                    using (DarkRiftWriter writer = DarkRiftWriter.Create())
                    {
                        writer.Write((byte)ResponseCode.Success);
                        writer.Write((byte)resultSignal);
                        writer.Write(partyId);
                        using (Message response = Message.Create(TAG_TRAINING_PAUSE, writer))
                        {
                            client.SendMessage(response, SendMode.Reliable);
                        }
                    }

                    Logger.Info($"Training pause toggle: Party={partyId}, Result={resultSignal}");
                    NotifyAdminsInstanceStateChange(instance, $"Training {resultSignal}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleTrainingPause error: {ex.Message}");
                SendErrorResponse(client, TAG_TRAINING_PAUSE, ResponseCode.ServerError);
            }
        }

        /// <summary>
        /// 훈련 중간 참여 — 교관을 파티에 view 역할로 추가
        /// </summary>
        private void HandleTrainingMidJoin(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    int partyId = reader.ReadInt32();

                    // 교관 검증
                    if (sessionManager == null || !sessionManager.IsAdmin(client))
                    {
                        Logger.Warning($"TrainingMidJoin denied: client {client.ID} is not admin");
                        SendErrorResponse(client, TAG_TRAINING_MID_JOIN, ResponseCode.ServerError);
                        return;
                    }

                    string accountId = sessionManager.GetAccountId(client);

                    // 1. 이미 관전중인 파티가 있는지 확인 (테스트 단계라 엄격하게 확인하는 단계 임시 해제)
                    // if (observingPartyId != -1)
                    // {
                    //     Logger.Warning($"TrainingMidJoin: {accountId} already observing party {observingPartyId}");
                    //     SendErrorResponse(client, TAG_TRAINING_MID_JOIN, ResponseCode.AlreadyExists);
                    //     return;
                    // }

                    // 파티 존재 확인
                    var party = partyManager?.GetPartyById(partyId);
                    if (party == null)
                    {
                        Logger.Warning($"TrainingMidJoin: Party {partyId} not found");
                        SendErrorResponse(client, TAG_TRAINING_MID_JOIN, ResponseCode.PartyNotFound);
                        return;
                    }

                    // 파티가 할당된 인스턴스 확인 (IP/Port 추출용)
                    GameInstanceData instance = null;
                    lock (instanceLock)
                    {
                        if (partyToInstance.TryGetValue(partyId, out int instanceId))
                        {
                            instances.TryGetValue(instanceId, out instance);
                        }
                    }

                    if (instance == null)
                    {
                        Logger.Warning($"TrainingMidJoin: Instance for Party {partyId} not found or not running");
                        SendErrorResponse(client, TAG_TRAINING_MID_JOIN, ResponseCode.InstanceNotFound);
                        return;
                    }

                    // 이미 멤버인지 확인
                    if (party.HasMember(accountId))
                    {
                        Logger.Warning($"TrainingMidJoin: {accountId} already in party {partyId}");
                        SendErrorResponse(client, TAG_TRAINING_MID_JOIN, ResponseCode.ServerError);
                        return;
                    }

                    // 파티에 viewer 역할로 추가
                    party.AddMember(accountId, "viewer");
                    observingPartyId = partyId; // 상태 업데이트

                    Logger.Info($"TrainingMidJoin: {accountId} joined Party {partyId} as viewer");

                    // 교관에게 성공 응답 + 서버 IP 및 포트
                    using (DarkRiftWriter writer = DarkRiftWriter.Create())
                    {
                        writer.Write((byte)ResponseCode.Success);
                        writer.Write(GetLocalIPAddress());
                        writer.Write(instance.Port);

                        using (Message response = Message.Create(TAG_TRAINING_MID_JOIN, writer))
                        {
                            client.SendMessage(response, SendMode.Reliable);
                        }
                    }

                    // 교관-파티 매핑 갱신
                    lock (instanceLock)
                    {
                        partyToInstructor[partyId] = client;
                    }

                    // 관리자에게 알림
                    NotifyAdminsInstanceStateChange(instance, $"Instructor {accountId} mid-joined Party {partyId}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleTrainingMidJoin error: {ex.Message}");
                SendErrorResponse(client, TAG_TRAINING_MID_JOIN, ResponseCode.ServerError);
            }
        }

        /// <summary>
        /// 훈련 중간 탈퇴 — 교관을 파티에서 제거
        /// </summary>
        private void HandleTrainingMidLeave(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    int partyId = reader.ReadInt32();

                    // 교관 검증
                    if (sessionManager == null || !sessionManager.IsAdmin(client))
                    {
                        Logger.Warning($"TrainingMidLeave denied: client {client.ID} is not admin");
                        SendErrorResponse(client, TAG_TRAINING_MID_LEAVE, ResponseCode.ServerError);
                        return;
                    }

                    string accountId = sessionManager.GetAccountId(client);

                    // 파티 존재 확인
                    var party = partyManager?.GetPartyById(partyId);
                    if (party == null)
                    {
                        Logger.Warning($"TrainingMidLeave: Party {partyId} not found");
                        SendErrorResponse(client, TAG_TRAINING_MID_LEAVE, ResponseCode.PartyNotFound);
                        return;
                    }

                    // 멤버인지 확인
                    if (!party.HasMember(accountId))
                    {
                        Logger.Warning($"TrainingMidLeave: {accountId} not in party {partyId}");
                        SendErrorResponse(client, TAG_TRAINING_MID_LEAVE, ResponseCode.ServerError);
                        return;
                    }

                    // 파티에서 제거
                    party.RemoveMember(accountId);
                    observingPartyId = -1; // 상태 초기화

                    Logger.Info($"TrainingMidLeave: {accountId} left Party {partyId}");

                    // 교관에게 성공 응답
                    SendSuccessResponse(client, TAG_TRAINING_MID_LEAVE);

                    // 교관-파티 매핑 제거
                    lock (instanceLock)
                    {
                        partyToInstructor.Remove(partyId);
                    }

                    // 인스턴스 조회 후 관리자 알림
                    GameInstanceData instance = null;
                    lock (instanceLock)
                    {
                        if (partyToInstance.TryGetValue(partyId, out int instanceId))
                        {
                            instances.TryGetValue(instanceId, out instance);
                        }
                    }

                    if (instance != null)
                    {
                        NotifyAdminsInstanceStateChange(instance, $"Instructor {accountId} mid-left Party {partyId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleTrainingMidLeave error: {ex.Message}");
                SendErrorResponse(client, TAG_TRAINING_MID_LEAVE, ResponseCode.ServerError);
            }
        }

        #endregion

        #region Training Send Helpers

        /// <summary>
        /// TrainingWakeResult를 인스턴스에 전송
        /// </summary>
        private void SendTrainingWakeResultToInstance(IClient instanceClient, TrainingWakeResultData wakeResult)
        {
            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write((byte)ResponseCode.Success);
                    writer.Write(wakeResult);

                    using (Message response = Message.Create(TAG_TRAINING_WAKE_RESULT, writer))
                    {
                        instanceClient.SendMessage(response, SendMode.Reliable);
                    }
                }

                Logger.Info($"Training wake result sent to instance: {wakeResult}");
            }
            catch (Exception ex)
            {
                Logger.Error($"SendTrainingWakeResultToInstance error: {ex.Message}");
            }
        }

        /// <summary>
        /// TrainingWakeResult를 훈련생들에게 전송
        /// </summary>
        private void SendTrainingWakeResultToTrainees(int partyId, TrainingWakeResultData wakeResult)
        {
            if (sessionManager == null || partyManager == null)
                return;

            try
            {
                var partyMembers = partyManager.GetPartyMembers(partyId);

                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write((byte)ResponseCode.Success);
                    writer.Write(wakeResult);

                    using (Message notification = Message.Create(TAG_TRAINING_WAKE_RESULT, writer))
                    {
                        int notifiedCount = 0;
                        foreach (var memberId in partyMembers)
                        {
                            IClient memberClient = sessionManager.GetClient(memberId);
                            if (memberClient != null)
                            {
                                try
                                {
                                    memberClient.SendMessage(notification, SendMode.Reliable);
                                    notifiedCount++;
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error($"Failed to send wake result to {memberId}: {ex.Message}");
                                }
                            }
                        }

                        Logger.Info($"Training wake result sent to trainees: Party={partyId}, Notified={notifiedCount}/{partyMembers.Count}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"SendTrainingWakeResultToTrainees error: {ex.Message}");
            }
        }

        /// <summary>
        /// TrainingCreated를 훈련생들에게 전송 (Fishnet 접속 정보)
        /// </summary>
        private void SendTrainingCreatedToTrainees(GameInstanceData instance, PartyManagerPlugin.Party party)
        {
            if (sessionManager == null || partyManager == null || !instance.AssignedPartyId.HasValue)
                return;

            int partyId = instance.AssignedPartyId.Value;

            try
            {
                // TrainingCreatedData 생성
                TrainingCreatedData createdData = new TrainingCreatedData(
                    partyId,
                    instance.InstanceId,
                    "127.0.0.1",  // TODO: 실제 서버 주소로 변경 필요
                    instance.Port,
                    party.SceneFile,
                    party.SceneSetFile
                );

                var partyMembers = partyManager.GetPartyMembers(partyId);

                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write((byte)ResponseCode.Success);
                    writer.Write(createdData);

                    using (Message notification = Message.Create(TAG_TRAINING_CREATED, writer))
                    {
                        int notifiedCount = 0;
                        foreach (var memberId in partyMembers)
                        {
                            IClient memberClient = sessionManager.GetClient(memberId);
                            if (memberClient != null)
                            {
                                try
                                {
                                    memberClient.SendMessage(notification, SendMode.Reliable);
                                    notifiedCount++;
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error($"Failed to send training created to {memberId}: {ex.Message}");
                                }
                            }
                        }

                        Logger.Info($"Training created sent to trainees: Party={partyId}, Instance={instance.InstanceId}, Notified={notifiedCount}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"SendTrainingCreatedToTrainees error: {ex.Message}");
            }
        }

        /// <summary>
        /// TrainingBegin을 훈련생들에게 전송
        /// </summary>
        private void SendTrainingBeginToTrainees(int partyId, int instanceId)
        {
            if (sessionManager == null || partyManager == null)
                return;

            try
            {
                var partyMembers = partyManager.GetPartyMembers(partyId);

                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write((byte)ResponseCode.Success);
                    writer.Write(instanceId);
                    writer.Write(partyId);

                    using (Message notification = Message.Create(TAG_TRAINING_BEGIN, writer))
                    {
                        int notifiedCount = 0;
                        foreach (var memberId in partyMembers)
                        {
                            IClient memberClient = sessionManager.GetClient(memberId);
                            if (memberClient != null)
                            {
                                try
                                {
                                    memberClient.SendMessage(notification, SendMode.Reliable);
                                    notifiedCount++;
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error($"Failed to send training begin to {memberId}: {ex.Message}");
                                }
                            }
                        }

                        Logger.Info($"Training begin sent to trainees: Party={partyId}, Notified={notifiedCount}/{partyMembers.Count}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"SendTrainingBeginToTrainees error: {ex.Message}");
            }
        }

        /// <summary>
        /// GameInstance에 훈련 시작 신호 전송
        /// </summary>
        private void SendTrainingBeginToInstance(GameInstanceData instance, int partyId)
        {
            IClient beginClient;
            lock (instanceLock)
            {
                if (!instanceClients.TryGetValue(instance.InstanceId, out beginClient))
                    return;
            }

            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write((byte)ResponseCode.Success);
                    writer.Write(instance.InstanceId);
                    writer.Write(partyId);

                    using (Message notification = Message.Create(TAG_TRAINING_BEGIN, writer))
                    {
                        beginClient.SendMessage(notification, SendMode.Reliable);
                    }
                }

                Logger.Info($"Training begin sent to instance {instance.InstanceId}");
            }
            catch (Exception ex)
            {
                Logger.Error($"SendTrainingBeginToInstance error: {ex.Message}");
            }
        }

        /// <summary>
        /// 훈련 생성정보(Fishnet 접속 정보)를 파티원들에게 전송
        /// HandleInstanceReady에서 호출됨
        /// </summary>
        private void SendTrainingSetupToPartyMembers(GameInstanceData instance)
        {
            if (sessionManager == null || partyManager == null || !instance.AssignedPartyId.HasValue)
                return;

            int partyId = instance.AssignedPartyId.Value;
            var party = partyManager.GetPartyById(partyId);
            if (party == null)
                return;

            try
            {
                // Fishnet 접속 정보 생성
                TrainingSetupData setupData = new TrainingSetupData(
                    partyId,
                    instance.InstanceId,
                    "127.0.0.1",  // TODO: 실제 서버 주소로 변경 필요
                    instance.Port,
                    party.SceneFile,
                    party.SceneSetFile
                );

                var partyMembers = partyManager.GetPartyMembers(partyId);

                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write((byte)ResponseCode.Success);
                    writer.Write(setupData);

                    using (Message notification = Message.Create(TAG_TRAINING_SETUP, writer))
                    {
                        int notifiedCount = 0;
                        foreach (var memberId in partyMembers)
                        {
                            IClient memberClient = sessionManager.GetClient(memberId);
                            if (memberClient != null)
                            {
                                try
                                {
                                    memberClient.SendMessage(notification, SendMode.Reliable);
                                    notifiedCount++;
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error($"Failed to send training setup to {memberId}: {ex.Message}");
                                }
                            }
                        }

                        Logger.Info($"Training setup sent: PartyID={partyId}, Instance={instance.InstanceId}, Notified={notifiedCount}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"SendTrainingSetupToPartyMembers error: {ex.Message}");
            }
        }

        /// <summary>
        /// 교관에게 훈련 상태 알림 전송
        /// </summary>
        private void NotifyInstructorTrainingStatus(int partyId, int totalMembers, int readyMembers, string[] readyIds)
        {
            IClient instructorClient;
            lock (instanceLock)
            {
                if (!partyToInstructor.TryGetValue(partyId, out instructorClient) || instructorClient == null)
                {
                    Logger.Warning($"No instructor registered for party {partyId}");
                    return;
                }
            }

            try
            {
                TrainingStatusNotification status = new TrainingStatusNotification(partyId, totalMembers, readyMembers, readyIds);

                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write((byte)ResponseCode.Success);
                    writer.Write(status);

                    using (Message notification = Message.Create(TAG_TRAINING_STATUS, writer))
                    {
                        instructorClient.SendMessage(notification, SendMode.Reliable);
                    }
                }

                Logger.Info($"Training status sent to instructor: {status}");
            }
            catch (Exception ex)
            {
                Logger.Error($"NotifyInstructorTrainingStatus error: {ex.Message}");
            }
        }

        #endregion

        #region Public API (HTTP / API)

        /// <summary>
        /// 현재 실행 중인 인스턴스 수
        /// </summary>
        public int ActiveInstanceCount
        {
            get
            {
                lock (instanceLock)
                {
                    return instances.Values.Count(i => !i.IsAvailable);
                }
            }
        }

        /// <summary>
        /// 사용 가능한 인스턴스 수
        /// </summary>
        public int AvailableInstanceCount
        {
            get
            {
                lock (instanceLock)
                {
                    return instances.Values.Count(i => i.IsAvailable);
                }
            }
        }

        /// <summary>
        /// 모든 인스턴스 조회 (HTTP API용)
        /// </summary>
        public IReadOnlyList<GameInstanceData> GetAllInstances()
        {
            lock (instanceLock)
            {
                return instances.Values.ToList();
            }
        }

        /// <summary>
        /// 특정 인스턴스 조회 (HTTP API용)
        /// </summary>
        public GameInstanceData GetInstance(int instanceId)
        {
            lock (instanceLock)
            {
                instances.TryGetValue(instanceId, out GameInstanceData data);
                return data;
            }
        }

        /// <summary>
        /// 특정 파티의 게임 인스턴스 조회
        /// </summary>
        public GameInstanceData GetInstanceForParty(int partyId)
        {
            lock (instanceLock)
            {
                if (partyToInstance.TryGetValue(partyId, out int instanceId) && instances.TryGetValue(instanceId, out GameInstanceData gameData))
                {
                    return gameData;
                }
            }
            return null;
        }

        /// <summary>
        /// 인스턴스 강제 종료 (HTTP API용)
        /// CleanupInstance 내부에 Idle 상태 가드가 있어 TOCTOU 경쟁 안전
        /// </summary>
        public bool ForceStopInstance(int instanceId)
        {
            GameInstanceData instance;
            lock (instanceLock)
            {
                if (!instances.TryGetValue(instanceId, out instance))
                {
                    return false;
                }

                if (instance.State == GameInstanceState.Idle)
                {
                    return false;
                }
            }

            Logger.Info($"[API] Force stopping instance: ID={instanceId}");
            CleanupInstance(instance);
            return true;
        }

        /// <summary>
        /// Ready 상태의 인스턴스에 Start 신호를 강제 전송 (HTTP API/TUI용)
        /// 파티 멤버 + 인스턴스에 Start 브로드캐스트
        /// </summary>
        public bool ForceStartInstance(int instanceId)
        {
            GameInstanceData instance;

            lock (instanceLock)
            {
                if (!instances.TryGetValue(instanceId, out instance))
                {
                    Logger.Warning($"ForceStartInstance: Instance {instanceId} not found");
                    return false;
                }

                if (instance.State != GameInstanceState.Ready)
                {
                    Logger.Warning($"ForceStartInstance: Instance {instanceId} is not Ready (State={instance.State})");
                    return false;
                }

                instance.State = GameInstanceState.Running;
            }

            if (instance.AssignedPartyId.HasValue)
                BroadcastSignalToPartyAndInstance(instance, InstanceSignalType.Start, "Force started via API");
            else
                SendSignalToInstance(instance, InstanceSignalType.Start, "Force started via API");

            NotifyAdminsInstanceStateChange(instance, "Force started via API");
            Logger.Info($"ForceStartInstance: Instance {instanceId} started (Party={instance.AssignedPartyId})");
            return true;
        }

        /// <summary>
        /// 인스턴스에 파티를 수동 할당 (HTTP API/TUI용 — 개발/테스트용)
        /// TrainingSetup 없이 Unity 에디터에서 직접 인스턴스를 실행할 때 사용
        /// </summary>
        public bool AssignPartyToInstance(int instanceId, int partyId)
        {
            var pm = EnsurePartyManager();
            if (pm == null)
            {
                Logger.Error("AssignPartyToInstance: PartyManager not available");
                return false;
            }

            var party = pm.GetPartyById(partyId);
            if (party == null)
            {
                Logger.Warning($"AssignPartyToInstance: Party {partyId} not found");
                return false;
            }

            lock (instanceLock)
            {
                if (!instances.TryGetValue(instanceId, out var instance))
                {
                    Logger.Warning($"AssignPartyToInstance: Instance {instanceId} not found");
                    return false;
                }

                if (instance.AssignedPartyId.HasValue && instance.AssignedPartyId.Value != partyId)
                {
                    Logger.Warning($"AssignPartyToInstance: Instance {instanceId} already assigned to party {instance.AssignedPartyId.Value}");
                    return false;
                }

                if (instance.AssignedPartyId.HasValue && instance.AssignedPartyId.Value == partyId)
                {
                    Logger.Info($"AssignPartyToInstance: Instance {instanceId} already assigned to party {partyId} (no-op)");
                    return true;
                }

                if (partyToInstance.ContainsKey(partyId))
                {
                    Logger.Warning($"AssignPartyToInstance: Party {partyId} already mapped to instance {partyToInstance[partyId]}");
                    return false;
                }

                instance.AssignedPartyId = partyId;
                partyToInstance[partyId] = instanceId;

                Logger.Info($"AssignPartyToInstance: Party {partyId} manually assigned to Instance {instanceId} (State={instance.State})");
                NotifyAdminsInstanceStateChange(instance, $"Party {partyId} manually assigned");
                return true;
            }
        }

        /// <summary>
        /// 파티에 인스턴스 할당 및 시작 (HTTP API용)
        /// 파티 데이터를 받아 사용 가능한 인스턴스를 찾아 게임 프로세스를 시작한다.
        /// </summary>
        /// <returns>할당된 GameInstanceData, 실패 시 null</returns>
        public GameInstanceData RequestInstanceForParty(int partyId, string sceneFile)
        {
            // 이미 실행 중인지 확인
            lock (instanceLock)
            {
                if (partyToInstance.ContainsKey(partyId))
                {
                    Logger.Warning($"[API] Training already running for party {partyId}");
                    return null;
                }
            }

            // 사용 가능한 인스턴스 찾기
            GameInstanceData instance = GetAvailableInstance();
            if (instance == null)
            {
                Logger.Warning("[API] No available game instance");
                return null;
            }

            // 인스턴스 시작
            bool started = StartGameInstance(instance, partyId, sceneFile ?? "");
            if (!started)
            {
                Logger.Error($"[API] Failed to start game instance for party {partyId}");
                return null;
            }

            // 파티-인스턴스 매핑
            lock (instanceLock)
            {
                partyToInstance[partyId] = instance.InstanceId;
            }

            Logger.Info($"[API] Instance {instance.InstanceId} assigned to party {partyId}, scene={sceneFile}");
            return instance;
        }

        /// <summary>
        /// 파티 인스턴스 대기 (HTTP API용)
        /// 프로세스를 시작하지 않고, 파티-인스턴스 매핑 + 임시 정보 저장 후
        /// unknownClient에 UnknownDedicate 메시지 전송 + TrainingSetup 성공 응답
        /// </summary>
        public string GraspInstanceForParty(int partyId, string sceneFile)
        {
            if (unknownClient == null)
                return "No unknown client registered. Instance must send UnknownDedicate first.";

            var pm = EnsurePartyManager();
            if (pm == null)
                return "PartyManager not available.";

            var party = pm.GetPartyById(partyId);
            if (party == null)
                return $"Party {partyId} not found.";

            // 이미 실행 중인지 확인
            lock (instanceLock)
            {
                if (partyToInstance.ContainsKey(partyId))
                    return $"Party {partyId} already has an instance assigned.";
            }

            // 사용 가능한 인스턴스 찾기
            GameInstanceData instance = GetAvailableInstance();
            if (instance == null)
                return "No available game instance.";

            // 인스턴스에 파티 할당 (프로세스 시작 안 함)
            lock (instanceLock)
            {
                instance.AssignedPartyId = partyId;
                instance.State = GameInstanceState.Ready;
                partyToInstance[partyId] = instance.InstanceId;
                // unknownClient를 이 인스턴스의 IClient로 등록
                instanceClients[instance.InstanceId] = unknownClient;
            }

            Logger.Info($"[API] WaitInstance: Instance {instance.InstanceId} (port {instance.Port}) reserved for party {partyId}, scene={sceneFile}");

            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write((byte)ResponseCode.Success);
                    writer.Write(instance.InstanceId);
                    writer.Write(instance.Port);
                    writer.Write(partyId);
                    writer.Write(sceneFile ?? string.Empty);

                    using (Message msg = Message.Create(TAG_UNKNOWN_DEDICATE_RESPONE, writer))
                    {
                        unknownClient.SendMessage(msg, SendMode.Reliable);
                    }
                }
                Logger.Info($"[API] UnknownDedicate sent to unknownClient (ClientID={unknownClient.ID})");


            }
            catch (Exception ex)
            {
                Logger.Error($"[API] Failed to send UnknownDedicate to unknownClient: {ex.Message}");
            }

            // 사용 완료 — 초기화
            unknownClient = null;

            return null; // null = 성공
        }

        /// <summary>
        /// 인스턴스 Pause/Resume 토글 (TUI/HTTP API용)
        /// </summary>
        public bool TogglePauseInstance(int instanceId)
        {
            GameInstanceData instance;
            InstanceSignalType resultSignal;
            bool hasParty;
            int partyId = 0;

            lock (instanceLock)
            {
                if (!instances.TryGetValue(instanceId, out instance))
                    return false;
                if (instance.State != GameInstanceState.Running)
                    return false;

                hasParty = instance.AssignedPartyId.HasValue;

                if (!hasParty)
                {
                    // 파티 없는 인스턴스: pausedInstances로 토글 (pausedParties와 분리)
                    resultSignal = pausedInstances.Contains(instanceId)
                        ? InstanceSignalType.Resume
                        : InstanceSignalType.Pause;

                    if (resultSignal == InstanceSignalType.Resume)
                        pausedInstances.Remove(instanceId);
                    else
                        pausedInstances.Add(instanceId);
                }
                else
                {
                    partyId = instance.AssignedPartyId.Value;
                    if (pausedParties.Contains(partyId))
                    {
                        pausedParties.Remove(partyId);
                        resultSignal = InstanceSignalType.Resume;
                    }
                    else
                    {
                        pausedParties.Add(partyId);
                        resultSignal = InstanceSignalType.Pause;
                    }
                }
            }

            // 네트워크 I/O는 lock 밖에서
            if (!hasParty)
            {
                SendSignalToInstance(instance, resultSignal, "Toggled via API");
                Logger.Info($"[API] TogglePauseInstance: Instance={instanceId}, Signal={resultSignal}");
            }
            else
            {
                BroadcastSignalToPartyAndInstance(instance, resultSignal, "Toggled via API");
                Logger.Info($"[API] TogglePauseInstance: Instance={instanceId}, Party={partyId}, Signal={resultSignal}");
            }
            return true;
        }

        /// <summary>
        /// 훈련 Graceful Stop (TUI/HTTP API용)
        /// </summary>
        public bool GracefulStopInstance(int instanceId)
        {
            GameInstanceData instance;
            lock (instanceLock)
            {
                if (!instances.TryGetValue(instanceId, out instance))
                    return false;
                if (instance.State != GameInstanceState.Running)
                    return false;

                instance.State = GameInstanceState.Stopping;
                if (instance.AssignedPartyId.HasValue)
                    pausedParties.Remove(instance.AssignedPartyId.Value);
                else
                    pausedInstances.Remove(instanceId);
            }

            if (instance.AssignedPartyId.HasValue)
                BroadcastSignalToPartyAndInstance(instance, InstanceSignalType.Stop, "Graceful stop via API");
            else
                SendSignalToInstance(instance, InstanceSignalType.Stop, "Graceful stop via API");

            Logger.Info($"[API] GracefulStopInstance: Instance={instanceId} → Stopping");
            NotifyAdminsInstanceStateChange(instance, "Graceful stop via API");

            // 10초 안전 타임아웃
            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(10000);
                GameInstanceData inst;
                lock (instanceLock)
                {
                    if (!instances.TryGetValue(instanceId, out inst) ||
                        inst.State != GameInstanceState.Stopping)
                        return;
                }
                Logger.Warning($"GracefulStop safety timeout: Instance {instanceId} still stopping after 10s, forcing cleanup");
                CleanupInstance(inst);
            });

            return true;
        }

        /// <summary>
        /// 파티가 제거될 때 호출 — 해당 파티에 할당된 인스턴스를 정리한다.
        /// PartyManager에서 파티 삭제 시 호출해야 함.
        /// </summary>
        public void OnPartyRemoved(int partyId)
        {
            GameInstanceData targetInstance = null;

            lock (instanceLock)
            {
                if (partyToInstance.TryGetValue(partyId, out int instanceId))
                {
                    if (instances.TryGetValue(instanceId, out targetInstance))
                    {
                        Logger.Warning($"OnPartyRemoved: Party {partyId} removed while Instance {instanceId} (State={targetInstance.State}) is assigned. Cleaning up.");
                    }
                }

                // 채팅 큐 자동 정리 (메모리 릭 방지)
                if (partyChatQueues.ContainsKey(partyId))
                {
                    partyChatQueues.Remove(partyId);
                    Logger.Info($"OnPartyRemoved: Cleaned up chat queue for Party {partyId}");
                }

                // 관전 상태 초기화 (메모리 릭 방지)
                if (observingPartyId == partyId)
                {
                    observingPartyId = -1;
                    Logger.Info($"OnPartyRemoved: ObservingPartyId reset for Party {partyId}");
                }
            }

            if (targetInstance != null)
                CleanupInstance(targetInstance);
        }

        /// <summary>
        /// 파티 확정 정보를 파티원들에게 브로드캐스트
        /// </summary>
        /// <summary>
        /// 파티원들에게 TAG_PARTY_CONFIRM(220) 브로드캐스트.
        /// 교관 주도 플로우 + HTTP API(ForceAddMember) 양쪽에서 호출됩니다.
        /// </summary>
        public void BroadcastPartyConfirmToMembers(PartyManagerPlugin.Party party)
        {
            if (sessionManager == null || party == null)
                return;

            try
            {
                var partyMembers = partyManager.GetPartyMembers(party.PartyId);

                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write((byte)ResponseCode.Success);
                    writer.Write(party);

                    using (Message notification = Message.Create(TAG_PARTY_CONFIRM, writer))
                    {
                        int notifiedCount = 0;
                        foreach (var memberId in partyMembers)
                        {
                            IClient memberClient = sessionManager.GetClient(memberId);
                            if (memberClient != null)
                            {
                                try
                                {
                                    memberClient.SendMessage(notification, SendMode.Reliable);
                                    notifiedCount++;
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error($"Failed to send party confirm to {memberId}: {ex.Message}");
                                }
                            }
                        }

                        Logger.Info($"Party confirm broadcast: PartyID={party.PartyId}, Notified={notifiedCount}/{partyMembers.Count}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"BroadcastPartyConfirmToMembers error: {ex.Message}");
            }
        }

        /// <summary>
        /// [API] partyId로 파티+인스턴스를 조회하여 TAG 227 (TrainingWakeResult) 훈련생 전송
        /// </summary>
        public (bool success, string message) SendWakeResultToTraineesByParty(int partyId)
        {
            if (partyManager == null || sessionManager == null)
                return (false, "PartyManager or SessionManager not available.");

            var party = partyManager.GetPartyById(partyId);
            if (party == null)
                return (false, $"Party {partyId} not found.");

            GameInstanceData instance = null;
            lock (instanceLock)
            {
                if (partyToInstance.TryGetValue(partyId, out int instanceId))
                {
                    instances.TryGetValue(instanceId, out instance);
                }
            }

            if (instance == null)
                return (false, $"No instance assigned to party {partyId}.");

            var memberIds = partyManager.GetPartyMembers(partyId);
            var wakeResult = new TrainingWakeResultData(
                partyId,
                instance.InstanceId,
                "127.0.0.1",
                instance.Port,
                party.SceneFile,
                party.SceneSetFile,
                memberIds.ToArray()
            );

            SendTrainingWakeResultToTrainees(partyId, wakeResult);
            return (true, $"TrainingWakeResult (TAG 227) sent to trainees of party {partyId}.");
        }

        /// <summary>
        /// [API] partyId로 파티+인스턴스를 조회하여 TAG 229 (TrainingCreated) 훈련생 전송
        /// </summary>
        public (bool success, string message) SendCreatedToTraineesByParty(int partyId)
        {
            if (partyManager == null || sessionManager == null)
                return (false, "PartyManager or SessionManager not available.");

            var party = partyManager.GetPartyById(partyId);
            if (party == null)
                return (false, $"Party {partyId} not found.");

            GameInstanceData instance = null;
            lock (instanceLock)
            {
                if (partyToInstance.TryGetValue(partyId, out int instanceId))
                    instances.TryGetValue(instanceId, out instance);
            }

            if (instance == null)
                return (false, $"No instance assigned to party {partyId}.");

            SendTrainingCreatedToTrainees(instance, party);
            return (true, $"TrainingCreated (TAG 229) sent to trainees of party {partyId}.");
        }

        /// <summary>
        /// [API] TAG 237 TrainResultPull을 파티 훈련생+교관에게 직접 배포 (DB 저장 없이 테스트용)
        /// </summary>
        public (bool success, string message) ApiTriggerTrainResultPull(int partyId, string resultJson)
        {
            if (sessionManager == null || partyManager == null)
                return (false, "SessionManager or PartyManager not available.");

            if (partyManager.GetPartyById(partyId) == null)
                return (false, $"Party {partyId} not found.");

            try
            {
                var result = string.IsNullOrWhiteSpace(resultJson)
                    ? new TrainResult { partyId = partyId }
                    : JsonConvert.DeserializeObject<TrainResult>(resultJson) ?? new TrainResult { partyId = partyId };
                result.partyId = partyId;
                SendTrainResultPull(result);
                return (true, $"TrainResultPull (TAG 237) sent to party {partyId}.");
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// [API] TAG 238 GracefulEnd를 파티 훈련생 전원에게 배포 (교관석 트리거 대역)
        /// </summary>
        public (bool success, string message) ApiTriggerGracefulEnd(int partyId)
        {
            if (sessionManager == null || partyManager == null)
                return (false, "SessionManager or PartyManager not available.");

            var partyMembers = partyManager.GetPartyMembers(partyId);
            if (partyMembers == null || partyMembers.Count == 0)
                return (false, $"Party {partyId} not found or has no members.");

            lock (instanceLock)
            {
                partyGracefulAcks[partyId] = new HashSet<string>();
            }

            IClient instructorClient = null;
            lock (instanceLock)
            {
                partyToInstructor.TryGetValue(partyId, out instructorClient);
            }
            string instructorId = instructorClient != null ? sessionManager.GetAccountId(instructorClient) : null;

            int notified = 0;
            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write(partyId);
                    using (Message notification = Message.Create(TAG_GRACEFUL_END, writer))
                    {
                        foreach (var memberId in partyMembers)
                        {
                            if (memberId == instructorId) continue;
                            IClient memberClient = sessionManager.GetClient(memberId);
                            if (memberClient != null)
                            {
                                try { memberClient.SendMessage(notification, SendMode.Reliable); notified++; }
                                catch (Exception ex) { Logger.Error($"GracefulEnd(API) send to {memberId}: {ex.Message}"); }
                            }
                        }
                    }
                }
                Logger.Info($"[API] GracefulEnd sent: Party={partyId}, Notified={notified}");
                return (true, $"GracefulEnd (TAG 238) sent to {notified} trainees in party {partyId}.");
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}");
            }
        }

        #endregion

        #region Admin Handlers

        /// <summary>
        /// 관리자 권한 확인
        /// </summary>
        private bool IsAdmin(IClient client)
        {
            if (sessionManager == null)
                return false;

            return sessionManager.IsAdmin(client);
        }

        /// <summary>
        /// 관리자용 모든 인스턴스 목록 조회
        /// </summary>
        private void HandleAdminGetAllInstances(IClient client, Message message)
        {
            try
            {
                // 관리자 권한 확인
                if (!IsAdmin(client))
                {
                    SendErrorResponse(client, TAG_ADMIN_GET_ALL_INSTANCES, ResponseCode.InstanceError);
                    Logger.Warning("Unauthorized access to admin instance list");
                    return;
                }

                lock (instanceLock)
                {
                    using (DarkRiftWriter writer = DarkRiftWriter.Create())
                    {
                        writer.Write((byte)ResponseCode.Success);
                        writer.Write(instances.Count);

                        foreach (var instance in instances.Values)
                        {
                            writer.Write(instance);
                        }

                        using (Message response = Message.Create(TAG_ADMIN_GET_ALL_INSTANCES, writer))
                        {
                            client.SendMessage(response, SendMode.Reliable);
                        }
                    }

                    Logger.Info($"Admin instance list sent: {instances.Count} instances");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleAdminGetAllInstances error: {ex.Message}");
                SendErrorResponse(client, TAG_ADMIN_GET_ALL_INSTANCES, ResponseCode.InstanceError);
            }
        }

        /// <summary>
        /// 관리자 관전 요청 처리
        /// </summary>
        private void HandleAdminSpectateRequest(IClient client, Message message)
        {
            try
            {
                // 관리자 권한 확인
                if (!IsAdmin(client))
                {
                    SendErrorResponse(client, TAG_ADMIN_SPECTATE_REQUEST, ResponseCode.ServerError);
                    Logger.Warning("Unauthorized spectate request");
                    return;
                }

                using (DarkRiftReader reader = message.GetReader())
                {
                    int instanceId = reader.ReadInt32();

                    lock (instanceLock)
                    {
                        if (!instances.TryGetValue(instanceId, out GameInstanceData instance))
                        {
                            SendErrorResponse(client, TAG_ADMIN_SPECTATE_REQUEST, ResponseCode.InstanceNotFound);
                            Logger.Warning($"Admin spectate failed: Instance {instanceId} not found");
                            return;
                        }

                        // 인스턴스가 실행 중인지 확인
                        if (instance.State != GameInstanceState.Ready &&
                            instance.State != GameInstanceState.Running)
                        {
                            SendErrorResponse(client, TAG_ADMIN_SPECTATE_REQUEST, ResponseCode.InstanceError);
                            Logger.Warning($"Admin spectate failed: Instance {instanceId} is {instance.State}");
                            return;
                        }

                        // 관전 정보 전송
                        using (DarkRiftWriter writer = DarkRiftWriter.Create())
                        {
                            writer.Write((byte)ResponseCode.Success);
                            writer.Write(instance);

                            using (Message response = Message.Create(TAG_ADMIN_SPECTATE_REQUEST, writer))
                            {
                                client.SendMessage(response, SendMode.Reliable);
                            }
                        }

                        string adminId = sessionManager?.GetAccountId(client) ?? "Unknown";
                        Logger.Info($"Admin spectate granted: Admin={adminId}, Instance={instanceId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleAdminSpectateRequest error: {ex.Message}");
                SendErrorResponse(client, TAG_ADMIN_SPECTATE_REQUEST, ResponseCode.ServerError);
            }
        }

        /// <summary>
        /// 관리자에게 인스턴스 상태 변경 알림
        /// </summary>
        private void NotifyAdminsInstanceStateChange(GameInstanceData instance, string additionalInfo = "")
        {
            if (sessionManager == null) return;

            var adminClients = sessionManager.GetAdminClients();

            if (adminClients.Count == 0) return;

            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write(instance);
                    writer.Write(additionalInfo ?? "");
                    writer.Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                    // MessageTags에서 참조
                    const ushort TAG_ADMIN_INSTANCE_NOTIFICATION = DatabasePlugin.MessageTags.AdminInstanceNotification;

                    using (Message notification = Message.Create(TAG_ADMIN_INSTANCE_NOTIFICATION, writer))
                    {
                        foreach (var adminClient in adminClients)
                        {
                            try
                            { adminClient.SendMessage(notification, SendMode.Reliable); }
                            catch (Exception ex)
                            { Logger.Error($"Failed to send instance notification to admin: {ex.Message}"); }
                        }
                    }
                }

                Logger.Trace($"Instance state notification sent to {adminClients.Count} admins: {instance}");
            }

            catch (Exception ex)
            {
                Logger.Error($"NotifyAdminsInstanceStateChange error: {ex.Message}");
            }
        }

        #endregion

        #region Chat / Announce

        /// <summary>
        /// 교관 공지 메시지 수신 핸들러 (isAlarm=true)
        /// </summary>
        private void HandleAnnounceText(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    ChatMessageData chatData = reader.ReadSerializable<ChatMessageData>();
                    chatData.IsAlarm = true; // 서버-사이드 강제 덮어쓰기 (교관 전용 알람 취급)

                    EnqueueAndBroadcastChat(client, TAG_ANNOUNCE_TEXT, chatData);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleAnnounceText error: {ex.Message}");
                SendErrorResponse(client, TAG_ANNOUNCE_TEXT, ResponseCode.ServerError);
            }
        }

        /// <summary>
        /// 일반 채팅 메시지 수신 핸들러 (isAlarm=false)
        /// </summary>
        private void HandleSendText(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    ChatMessageData chatData = reader.ReadSerializable<ChatMessageData>();
                    chatData.IsAlarm = false; // 일반 채팅 취급

                    EnqueueAndBroadcastChat(client, TAG_SEND_TEXT, chatData);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleSendText error: {ex.Message}");
                SendErrorResponse(client, TAG_SEND_TEXT, ResponseCode.ServerError);
            }
        }

        /// <summary>
        /// 공통 기능: 채팅 큐 적재 및 파티원 브로드캐스트
        /// </summary>
        private void EnqueueAndBroadcastChat(IClient client, ushort responseTag, ChatMessageData chatData)
        {
            var pm = EnsurePartyManager();
            if (pm == null)
            {
                SendErrorResponse(client, responseTag, ResponseCode.ServerError);
                return;
            }

            var party = pm.GetPartyById(chatData.PartyId);
            if (party == null)
            {
                Logger.Warning($"EnqueueAndBroadcastChat: Party {chatData.PartyId} not found");
                SendErrorResponse(client, responseTag, ResponseCode.PartyNotFound);
                return;
            }

            // 1. 큐에 적재 (크기 제한)
            lock (instanceLock)
            {
                if (!partyChatQueues.TryGetValue(chatData.PartyId, out Queue<ChatMessageData> queue))
                {
                    queue = new Queue<ChatMessageData>();
                    partyChatQueues[chatData.PartyId] = queue;
                }

                queue.Enqueue(chatData);

                // 큐 사이즈 제한
                while (queue.Count > MAX_CHAT_QUEUE_SIZE)
                {
                    queue.Dequeue();
                }
            }

            Logger.Info($"Chat message enqueued and broadcasting: {chatData}");

            bool isSenderInParty = false;

            // 2. 파티원들에게 송신 (태그는 항상 SEND_TEXT를 써서 클라이언트가 ChatMessageData의 isAlarm으로 분리 처리하게 유도)
            if (sessionManager != null)
            {
                var partyMembers = pm.GetPartyMembers(chatData.PartyId);
                string senderAccountId = sessionManager.GetAccountId(client);
                if (!string.IsNullOrEmpty(senderAccountId))
                {
                    isSenderInParty = partyMembers.Contains(senderAccountId);
                }

                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write((byte)ResponseCode.Success);
                    writer.Write(chatData);

                    using (Message broadcastMsg = Message.Create(TAG_SEND_TEXT, writer))
                    {
                        foreach (var memberId in partyMembers)
                        {
                            IClient memberClient = sessionManager.GetClient(memberId);
                            if (memberClient != null)
                            {
                                try
                                {
                                    memberClient.SendMessage(broadcastMsg, SendMode.Reliable);
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error($"Failed to broadcast chat to {memberId}: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }

            // 3. 발신자에게 성공 응답 (발신자가 브로드캐스트를 받지 못한 경우에만 빈 Success 응답 전송)
            if (!isSenderInParty)
            {
                SendSuccessResponse(client, responseTag);
            }
        }

        private void HandlePinInfo(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    PinInfoData pinData = reader.ReadSerializable<PinInfoData>();

                    Logger.Info($"Pin info received: {pinData}");

                    // 파티 존재 확인
                    var pm = EnsurePartyManager();
                    if (pm == null)
                    {
                        SendErrorResponse(client, TAG_PIN_INFO, ResponseCode.ServerError);
                        return;
                    }

                    var party = pm.GetPartyById(pinData.PartyId);
                    if (party == null)
                    {
                        Logger.Warning($"HandlePinInfo: Party {pinData.PartyId} not found");
                        SendErrorResponse(client, TAG_PIN_INFO, ResponseCode.PartyNotFound);
                        return;
                    }

                    // 교육생들에게 릴레이
                    SendPinInfoToTrainees(pinData);

                    // 교관에게 성공 응답
                    using (DarkRiftWriter writer = DarkRiftWriter.Create())
                    {
                        writer.Write((byte)ResponseCode.Success);
                        using (Message response = Message.Create(TAG_PIN_INFO, writer))
                        {
                            client.SendMessage(response, SendMode.Reliable);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandlePinInfo error: {ex.Message}");
                SendErrorResponse(client, TAG_PIN_INFO, ResponseCode.ServerError);
            }
        }

        /// <summary>
        /// 핀 정보를 파티 교육생들에게 전송
        /// </summary>
        private void SendPinInfoToTrainees(PinInfoData pinData)
        {
            if (sessionManager == null || partyManager == null)
                return;

            try
            {
                var partyMembers = partyManager.GetPartyMembers(pinData.PartyId);

                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write((byte)ResponseCode.Success);
                    writer.Write(pinData);

                    using (Message notification = Message.Create(TAG_PIN_INFO, writer))
                    {
                        int notifiedCount = 0;
                        foreach (var memberId in partyMembers)
                        {
                            IClient memberClient = sessionManager.GetClient(memberId);
                            if (memberClient != null)
                            {
                                try
                                {
                                    memberClient.SendMessage(notification, SendMode.Reliable);
                                    notifiedCount++;
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error($"Failed to send pin info to {memberId}: {ex.Message}");
                                }
                            }
                        }
                        Logger.Info($"Pin info sent to trainees: Party={pinData.PartyId}, Notified={notifiedCount}/{partyMembers.Count}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"SendPinInfoToTrainees error: {ex.Message}");
            }
        }

        #endregion

        #region Utilities & Lifecycle

        /// <summary>
        /// UnknownDedicate 수신 처리 — 에디터 인스턴스가 대기 등록
        /// </summary>
        private void HandleUnknownDedicate(IClient client)
        {
            unknownClient = client;
            Logger.Info($"[UnknownDedicate] 대기 클라이언트 등록: ClientID={client.ID}");
        }

        /// <summary>
        /// 클라이언트 로그 수신 처리
        /// 클라이언트/인스턴스가 서버 콘솔에 로그를 표시 요청
        /// Payload: LogLevel(byte) + Message(string)
        /// LogLevel: 0=Info, 1=Warning, 2=Error
        /// </summary>
        private void HandleClientLog(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    byte logLevel = reader.ReadByte();
                    string logMessage = reader.ReadString();

                    // 발신자 식별
                    string senderId = "Unknown";
                    if (sessionManager != null)
                    {
                        string accountId = sessionManager.GetAccountId(client);
                        if (!string.IsNullOrEmpty(accountId))
                            senderId = accountId;
                    }

                    string formattedLog = $"[ClientLog] [{senderId} (CID:{client.ID})] {logMessage}";

                    switch (logLevel)
                    {
                        case 0: // Info
                            Logger.Info(formattedLog);
                            break;
                        case 1: // Warning
                            Logger.Warning(formattedLog);
                            break;
                        case 2: // Error
                            Logger.Error(formattedLog);
                            break;
                        default:
                            Logger.Info(formattedLog);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleClientLog error: {ex.Message}");
            }
        }

        private void SendSuccessResponse(IClient client, ushort tag)
        {
            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write((byte)ResponseCode.Success);

                    using (Message response = Message.Create(tag, writer))
                    {
                        client.SendMessage(response, SendMode.Reliable);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"SendSuccessResponse error: {ex.Message}");
            }
        }

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
                Logger.Error($"SendErrorResponse error: {ex.Message}");
            }
        }

        private string GetLocalIPAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to get local IP address: {ex.Message}");
            }
            return "127.0.0.1";
        }

        private void OnProcessExit(object sender, EventArgs e)
        {
            KillAllInstanceProcesses();
        }

        private void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            KillAllInstanceProcesses();
        }

        /// <summary>
        /// 모든 활성 인스턴스 프로세스를 즉시 강제 종료
        /// </summary>
        private void KillAllInstanceProcesses()
        {
            List<Process> processesToKill;
            lock (instanceLock)
            {
                processesToKill = instanceProcesses.Values.ToList();
                instanceProcesses.Clear();
            }

            foreach (var proc in processesToKill)
            {
                try
                {
                    if (proc != null && !proc.HasExited)
                    {
                        proc.Kill();
                        proc.WaitForExit(3000);
                    }
                }
                catch { }
                finally
                {
                    try { proc?.Dispose(); } catch { }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 이벤트 해제
                AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
                try { Console.CancelKeyPress -= OnCancelKeyPress; } catch { }

                // 하트비트 타이머 정지
                heartbeatTimer?.Dispose();

                // 모든 인스턴스 프로세스 강제 종료
                KillAllInstanceProcesses();

                lock (instanceLock)
                {
                    instances.Clear();
                    partyToInstance.Clear();
                    pausedParties.Clear();
                    pausedInstances.Clear();
                    partyChatQueues.Clear();
                }

                Logger.Info("GameInstanceManager disposed.");
            }

            base.Dispose(disposing);
        }

        #endregion

    }
}
