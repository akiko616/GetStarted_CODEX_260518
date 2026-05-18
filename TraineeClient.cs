using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using DarkRift;
using DarkRift.Client;
using DarkRift.Client.Unity;
using UnityEngine;
using GameInstancePlugin;
using PartyManagerPlugin;
using MT = DatabasePlugin.MessageTags;

namespace DatabasePlugin.Client
{
    /// <summary>
    /// 훈련생(Trainee) 전용 클라이언트 — 순수 DarkRift 메시지 I/O
    /// - 로그인/세션 관리
    /// - StreamingAssets/account.txt 기반 자동 로그인
    /// - 교관 주도 훈련 플로우 지원:
    ///   - PartyConfirm(220) → 파티 확정 알림 수신
    ///   - TrainingWakeResult(227) → 훈련 정보 수신
    ///   - TrainingCreated(229) → Fishnet 접속 정보 수신
    ///   - TrainingBegin(224) → 훈련 시작 신호 수신
    ///
    /// FishNet 연결, AuthBroadcast, 씬 생성 등 오케스트레이션은
    /// TraineeClientHandler에서 처리합니다.
    /// </summary>
    public class TraineeClient : MonoBehaviour
    {
        #region Serialized Fields
        [Header("연결 설정")]
        [SerializeField] private UnityClient logicClient;

        [Header("자동 로그인 설정")]
        [SerializeField] private bool enableAutoLogin = true;
        [SerializeField] private string accountFileName = "account.txt";
        #endregion

        #region Private Fields
        private bool isInitialized = false;
        private bool isAutoLoginAttempted = false;
        private AccountResponse currentAccount;
        private Party currentParty;
        private GameInstanceData currentGameInstance;
        private GameInstancePlugin.TrainingWakeResultData currentTrainingWakeResult;
        private GameInstancePlugin.TrainingCreatedData currentTrainingCreated;
        private CancellationTokenSource autoLoginCts;
        #endregion

        // Training Type 범위 로컬 상수
        private const ushort TAG_TRAINING_TYPE_MIN = 1000;
        private const ushort TAG_TRAINING_TYPE_MAX = 1019;

        #region Response Codes
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

        public enum PartyResponseCode : byte
        {
            Success = 0,
            Failed = 1,
            NotFound = 2,
            AlreadyInParty = 3,
            InvalidData = 4,
            NoPermission = 5,
            PartyFull = 6
        }

        public enum TrainingResponseCode : byte
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
        #endregion

        #region Data Types
        public readonly struct AccountCredentials
        {
            public string Id { get; }
            public string Password { get; }
            public bool IsValid => !string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(Password);

            public AccountCredentials(string id, string password)
            {
                Id = id ?? string.Empty;
                Password = password ?? string.Empty;
            }

            public override string ToString()
            {
                return $"AccountCredentials[ID:{Id}, HasPassword:{!string.IsNullOrEmpty(Password)}]";
            }
        }
        #endregion

        #region Events
        // Auth Events
        public event Action<AccountResponse> OnLoginSuccess;
        public event Action<ResponseCode> OnLoginFailed;

        // Auto Login Events
        public event Action OnAutoLoginStarted;
        public event Action<AccountResponse> OnAutoLoginSuccess;
        public event Action<string> OnAutoLoginFailed;

        // Training Events
        public event Action<GameInstanceData> OnGameServerReady;
        public event Action<TrainingResponseCode> OnTrainingFailed;

        // Training Control Events (교관 주도 플로우)
        public event Action<Party> OnPartyConfirmReceived;
        public event Action<TrainingResponseCode> OnPartyConfirmFailed;
        public event Action<GameInstancePlugin.TrainingWakeResultData> OnTrainingWakeResultReceived;
        public event Action<TrainingResponseCode> OnTrainingWakeResultFailed;
        public event Action<GameInstancePlugin.TrainingCreatedData> OnTrainingCreatedReceived;
        public event Action<TrainingResponseCode> OnTrainingCreatedFailed;
        public event Action<int, int> OnTrainingBeginReceived; // instanceId, partyId

        // Training Pause/Stop Events
        public event Action<InstanceSignalType, int, int> OnTrainingPaused;
        public event Action<int, int> OnTrainingStopReceived;

        // Pin Info
        public event Action<GameInstancePlugin.PinInfoData> OnPinInfoReceived;

        // Training Type
        public event Action<ushort> OnTrainingTypeReceived;

        // Ping
        public event Action<long> OnPingProbeReceived;

        // Game Server Connection (Handler에서 발화)
        public event Action<bool> OnGameServerConnectionChanged;

        /// <summary>게임 서버 연결 해제 이벤트 (Handler에서 발화)</summary>
        public event Action OnDisconnectedFromGameServer;

        // Error
        public event Action<string> OnError;
        #endregion

        #region Unity Lifecycle
        private void Start()
        {
            if (logicClient == null && !TryGetComponent(out logicClient))
            {
                logicClient = gameObject.AddComponent<UnityClient>();
            }

            // DarkRift 연결 이벤트 등록
            if (logicClient != null)
            {
                logicClient.ConnectInBackground(logicClient.Address, logicClient.Port, true, OnLogicClientConnected);
                logicClient.Disconnected += OnLogicClientDisconnected;
            }
        }

        private void OnDestroy()
        {
            autoLoginCts?.Cancel();
            autoLoginCts?.Dispose();
            autoLoginCts = null;

            if (logicClient != null)
            {
                if (isInitialized)
                {
                    logicClient.MessageReceived -= OnMessageReceived;
                }
                logicClient.Disconnected -= OnLogicClientDisconnected;
            }
        }
        #endregion

        #region DarkRift Connection Events
        private void OnLogicClientConnected(Exception e)
        {
            Debug.Log("[TraineeClient] DarkRift 서버에 연결됨");

            if (!isInitialized)
            {
                logicClient.MessageReceived += OnMessageReceived;
                isInitialized = true;
            }

            if (enableAutoLogin && !isAutoLoginAttempted)
            {
                autoLoginCts?.Cancel();
                autoLoginCts = new CancellationTokenSource();
                AutoLoginAsync(autoLoginCts.Token).Forget();
            }
        }

        private void OnLogicClientDisconnected(object sender, DisconnectedEventArgs e)
        {
            Debug.Log($"[TraineeClient] DarkRift 서버 연결 해제됨 (LocalDisconnect: {e.LocalDisconnect})");

            isAutoLoginAttempted = false;
            currentAccount = null;
            currentParty = null;

            autoLoginCts?.Cancel();
        }
        #endregion

        #region Auto Login
        private async UniTaskVoid AutoLoginAsync(CancellationToken ct)
        {
            try
            {
                isAutoLoginAttempted = true;
                OnAutoLoginStarted?.Invoke();

                Debug.Log("[TraineeClient] 자동 로그인 시작...");

                var credentials = await LoadAccountCredentialsAsync(ct);

                if (!credentials.IsValid)
                {
                    string errorMsg = "계정 파일을 찾을 수 없거나 형식이 올바르지 않습니다.";
                    Debug.LogWarning($"[TraineeClient] 자동 로그인 실패: {errorMsg}");
                    OnAutoLoginFailed?.Invoke(errorMsg);
                    return;
                }

                Debug.Log($"[TraineeClient] 계정 정보 로드됨: {credentials}");
                SendLogin(credentials.Id, credentials.Password);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[TraineeClient] 자동 로그인 취소됨");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TraineeClient] 자동 로그인 오류: {ex.Message}");
                OnAutoLoginFailed?.Invoke(ex.Message);
            }
        }

        private async UniTask<AccountCredentials> LoadAccountCredentialsAsync(CancellationToken ct)
        {
            string filePath = Path.Combine(Application.streamingAssetsPath, accountFileName);

            Debug.Log($"[TraineeClient] 계정 파일 경로: {filePath}");

#if UNITY_ANDROID && !UNITY_EDITOR
            return await LoadAccountCredentialsFromAndroidAsync(filePath, ct);
#elif UNITY_WEBGL && !UNITY_EDITOR
            return await LoadAccountCredentialsFromWebGLAsync(filePath, ct);
#else
            return await LoadAccountCredentialsFromFileAsync(filePath, ct);
#endif
        }

        private async UniTask<AccountCredentials> LoadAccountCredentialsFromFileAsync(string filePath, CancellationToken ct)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"[TraineeClient] 계정 파일을 찾을 수 없음: {filePath}");
                return default;
            }

            string content = await UniTask.RunOnThreadPool(() => File.ReadAllText(filePath), cancellationToken: ct);
            return ParseAccountCredentials(content);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private async UniTask<AccountCredentials> LoadAccountCredentialsFromAndroidAsync(string filePath, CancellationToken ct)
        {
            using var request = UnityEngine.Networking.UnityWebRequest.Get(filePath);
            await request.SendWebRequest().WithCancellation(ct);

            if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[TraineeClient] Android 계정 파일 로드 실패: {request.error}");
                return default;
            }

            return ParseAccountCredentials(request.downloadHandler.text);
        }
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
        private async UniTask<AccountCredentials> LoadAccountCredentialsFromWebGLAsync(string filePath, CancellationToken ct)
        {
            using var request = UnityEngine.Networking.UnityWebRequest.Get(filePath);
            await request.SendWebRequest().WithCancellation(ct);

            if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[TraineeClient] WebGL 계정 파일 로드 실패: {request.error}");
                return default;
            }

            return ParseAccountCredentials(request.downloadHandler.text);
        }
#endif

        private AccountCredentials ParseAccountCredentials(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return default;
            }

            string id = "1234";
            string password = "1234";

            string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();

                if (trimmedLine.StartsWith("#") || trimmedLine.StartsWith("//"))
                    continue;

                int separatorIndex = trimmedLine.IndexOf(':');
                if (separatorIndex < 0)
                    separatorIndex = trimmedLine.IndexOf('=');

                if (separatorIndex > 0)
                {
                    string key = trimmedLine.Substring(0, separatorIndex).Trim().ToLowerInvariant();
                    string value = trimmedLine.Substring(separatorIndex + 1).Trim();

                    switch (key)
                    {
                        case "id":
                        case "account":
                        case "username":
                        case "user":
                            id = value;
                            break;
                        case "password":
                        case "pass":
                        case "pw":
                            password = value;
                            break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(password))
            {
                Debug.LogWarning("[TraineeClient] 계정 파일에서 ID 또는 Password를 찾을 수 없음");
                return default;
            }

            return new AccountCredentials(id, password);
        }

        public void RetryAutoLogin()
        {
            if (!logicClient.ConnectionState.HasFlag(ConnectionState.Connected))
            {
                Debug.LogWarning("[TraineeClient] 서버에 연결되어 있지 않습니다");
                OnAutoLoginFailed?.Invoke("서버에 연결되어 있지 않습니다");
                return;
            }

            isAutoLoginAttempted = false;
            autoLoginCts?.Cancel();
            autoLoginCts = new CancellationTokenSource();
            AutoLoginAsync(autoLoginCts.Token).Forget();
        }
        #endregion

        #region Public Methods - Initialization
        public void Initialize(UnityClient client)
        {
            if (isInitialized)
            {
                Debug.LogWarning("[TraineeClient] 이미 초기화되었습니다");
                return;
            }

            this.logicClient = client;
            this.logicClient.MessageReceived += OnMessageReceived;
            isInitialized = true;
            Debug.Log("[TraineeClient] 초기화 완료");
        }
        #endregion

        #region Public Methods - Auth
        public void SendLogin(string id, string password)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(password))
            {
                Debug.LogError("[TraineeClient] ID와 비밀번호는 비어있을 수 없습니다");
                OnLoginFailed?.Invoke(ResponseCode.InvalidData);
                return;
            }

            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                writer.Write(id);
                writer.Write(password);

                using (Message message = Message.Create(MT.Login, writer))
                {
                    logicClient.SendMessage(message, SendMode.Reliable);
                }
            }

            Debug.Log($"[TraineeClient] 로그인 요청 전송됨: {id}");
        }
        #endregion

        #region Message Handling
        private void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            using (Message message = e.GetMessage())
            {
                try
                {
                    switch (message.Tag)
                    {
                        case MT.Login:
                            HandleLoginResponse(message);
                            break;
                        case MT.TrainingInfo:
                            HandleTrainingInfoResponse(message);
                            break;
                        case MT.PartyConfirm:
                            HandlePartyConfirm(message);
                            break;
                        case MT.TrainingWakeResult:
                            HandleTrainingWakeResult(message);
                            break;
                        case MT.TrainingCreated:
                            HandleTrainingCreated(message);
                            break;
                        case MT.TrainingBegin:
                            HandleTrainingBegin(message);
                            break;
                        case MT.InstanceSignalBroadcast:
                            HandleInstanceSignalBroadcast(message);
                            break;
                        case MT.PinInfo:
                            HandlePinInfoReceived(message);
                            break;
                        case MT.PingProbe:
                            HandlePingProbe(message);
                            break;
                        default:
                            if (message.Tag >= TAG_TRAINING_TYPE_MIN && message.Tag <= TAG_TRAINING_TYPE_MAX)
                            {
                                HandleTrainingTypeReceived(message);
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[TraineeClient] 메시지 처리 오류: {ex.Message}");
                }
            }
        }

        private void HandleTrainingTypeReceived(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                ushort receivedType = reader.ReadUInt16();
                Debug.Log($"[TraineeClient] TrainingType 수신: {receivedType}");
                OnTrainingTypeReceived?.Invoke(receivedType);
            }
        }

        private void HandlePingProbe(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                long sentMs = reader.ReadInt64();
                OnPingProbeReceived?.Invoke(sentMs);
            }
        }
        #endregion

        #region Ping
        public void SendPingProbeAck(long sentMs, int hardwareStatus)
        {
            if (logicClient == null || !logicClient.Connected) return;
            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                writer.Write(sentMs);
                writer.Write(hardwareStatus);
                using (Message msg = Message.Create((ushort)MT.PingProbeAck, writer))
                {
                    logicClient.SendMessage(msg, SendMode.Unreliable);
                }
            }
        }
        #endregion

        #region Response Handlers
        private void HandleLoginResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                ResponseCode code = (ResponseCode)reader.ReadByte();

                if (code == ResponseCode.Success)
                {
                    AccountResponse account = reader.ReadSerializable<AccountResponse>();
                    currentAccount = account;
                    Debug.Log($"[TraineeClient] 로그인 성공: {account}");
                    OnLoginSuccess?.Invoke(account);

                    if (isAutoLoginAttempted)
                    {
                        OnAutoLoginSuccess?.Invoke(account);
                    }
                }
                else
                {
                    Debug.LogWarning($"[TraineeClient] 로그인 실패: {code}");
                    OnLoginFailed?.Invoke(code);

                    if (isAutoLoginAttempted)
                    {
                        OnAutoLoginFailed?.Invoke($"로그인 실패: {code}");
                    }
                }
            }
        }

        private void HandleTrainingInfoResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                TrainingResponseCode code = (TrainingResponseCode)reader.ReadByte();

                if (code == TrainingResponseCode.Success)
                {
                    GameInstanceData instanceData = reader.ReadSerializable<GameInstanceData>();
                    currentGameInstance = instanceData;

                    Debug.Log($"[TraineeClient] 게임 서버 준비 완료: {instanceData}");
                    OnGameServerReady?.Invoke(instanceData);
                }
                else
                {
                    Debug.LogWarning($"[TraineeClient] 훈련 정보 수신 실패: {code}");
                    OnTrainingFailed?.Invoke(code);

                    string errorMessage = code switch
                    {
                        TrainingResponseCode.NoAvailableInstance => "사용 가능한 게임 인스턴스가 없습니다.",
                        TrainingResponseCode.PartyNotFound => "파티를 찾을 수 없습니다.",
                        TrainingResponseCode.AlreadyRunning => "이 파티에 대한 훈련이 이미 실행 중입니다.",
                        TrainingResponseCode.ServerError => "서버 내부/의존성 문제로 수행할 수 없습니다.",
                        TrainingResponseCode.Unauthorized => "교관/관리자 권한을 확인할 수 없습니다.",
                        TrainingResponseCode.InstanceError => "서버 인스턴스 상태 문제(실행 실패/종료)입니다.",
                        TrainingResponseCode.AlreadyExists => "이미 해당 파티에 관전/참여 중입니다.",
                        _ => $"훈련 정보 수신 실패: {code}"
                    };

                    OnError?.Invoke(errorMessage);
                }
            }
        }

        private void HandlePartyConfirm(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                TrainingResponseCode code = (TrainingResponseCode)reader.ReadByte();

                if (code == TrainingResponseCode.Success)
                {
                    Party party = reader.ReadSerializable<Party>();
                    currentParty = party;

                    Debug.Log($"[TraineeClient] 파티 확정 수신됨: {party}");
                    OnPartyConfirmReceived?.Invoke(party);
                }
                else
                {
                    Debug.LogWarning($"[TraineeClient] 파티 확정 실패: {code}");
                    OnPartyConfirmFailed?.Invoke(code);
                    OnError?.Invoke($"파티 확정 실패: {code}");
                }
            }
        }

        private void HandleTrainingWakeResult(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                TrainingResponseCode code = (TrainingResponseCode)reader.ReadByte();

                if (code == TrainingResponseCode.Success)
                {
                    GameInstancePlugin.TrainingWakeResultData wakeResult = reader.ReadSerializable<GameInstancePlugin.TrainingWakeResultData>();
                    currentTrainingWakeResult = wakeResult;

                    Debug.Log($"[TraineeClient] 훈련 기상 결과 수신됨: {wakeResult}");
                    OnTrainingWakeResultReceived?.Invoke(wakeResult);
                }
                else
                {
                    Debug.LogWarning($"[TraineeClient] 훈련 기상 결과 실패: {code}");
                    OnTrainingWakeResultFailed?.Invoke(code);
                    OnError?.Invoke($"훈련 기상 결과 실패: {code}");
                }
            }
        }

        /// <summary>훈련 생성됨 처리 — 이벤트만 발화 (FishNet 접속은 TraineeClientHandler에서)</summary>
        private void HandleTrainingCreated(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                TrainingResponseCode code = (TrainingResponseCode)reader.ReadByte();

                if (code == TrainingResponseCode.Success)
                {
                    GameInstancePlugin.TrainingCreatedData createdData = reader.ReadSerializable<GameInstancePlugin.TrainingCreatedData>();
                    currentTrainingCreated = createdData;

                    Debug.Log($"[TraineeClient] 훈련 생성됨 수신됨: {createdData}");
                    OnTrainingCreatedReceived?.Invoke(createdData);
                }
                else
                {
                    Debug.LogWarning($"[TraineeClient] 훈련 생성됨 실패: {code}");
                    OnTrainingCreatedFailed?.Invoke(code);
                    OnError?.Invoke($"훈련 생성 실패: {code}");
                }
            }
        }

        private void HandleTrainingBegin(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                int instanceId = reader.ReadInt32();
                int partyId = reader.ReadInt32();

                Debug.Log($"[TraineeClient] 훈련 시작 수신됨: 인스턴스={instanceId}, 파티={partyId}");
                OnTrainingBeginReceived?.Invoke(instanceId, partyId);
            }
        }

        private void HandleInstanceSignalBroadcast(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                byte responseCode = reader.ReadByte();
                InstanceSignalType signalType = (InstanceSignalType)reader.ReadByte();
                int instanceId = reader.ReadInt32();
                int partyId = reader.ReadInt32();
                string additionalData = reader.ReadString();
                string timestamp = reader.ReadString();

                Debug.Log($"[TraineeClient] 인스턴스 신호 수신: {signalType}, Instance={instanceId}, Party={partyId}, Time={timestamp}");

                switch (signalType)
                {
                    case InstanceSignalType.Stop:
                        Debug.Log($"[TraineeClient] 훈련 종료 신호 수신!");
                        currentGameInstance = null;
                        currentTrainingWakeResult = null;
                        OnTrainingStopReceived?.Invoke(instanceId, partyId);
                        break;
                    case InstanceSignalType.Pause:
                    case InstanceSignalType.Resume:
                        string stateStr = signalType == InstanceSignalType.Pause ? "일시정지" : "재개";
                        Debug.Log($"[TraineeClient] 훈련 {stateStr} 신호 수신!");
                        OnTrainingPaused?.Invoke(signalType, instanceId, partyId);
                        break;
                    default:
                        Debug.Log($"[TraineeClient] 기타 인스턴스 신호: {signalType}");
                        break;
                }
            }
        }

        private void HandlePinInfoReceived(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                byte code = reader.ReadByte();

                if (code == 0)
                {
                    var pinData = reader.ReadSerializable<GameInstancePlugin.PinInfoData>();
                    Debug.Log($"[TraineeClient] 핀 정보 수신: {pinData}");
                    OnPinInfoReceived?.Invoke(pinData);
                }
                else
                {
                    Debug.LogWarning($"[TraineeClient] 핀 정보 수신 실패: code={code}");
                }
            }
        }
        #endregion

        #region Public Properties
        public AccountResponse CurrentAccount => currentAccount;
        public Party CurrentParty => currentParty;
        public GameInstanceData CurrentGameInstance => currentGameInstance;
        public bool IsInitialized => isInitialized;
        public bool IsInParty => currentParty != null;
        public bool IsLoggedIn => currentAccount != null;

        public bool EnableAutoLogin
        {
            get => enableAutoLogin;
            set => enableAutoLogin = value;
        }

        public GameInstancePlugin.TrainingWakeResultData CurrentTrainingWakeResult => currentTrainingWakeResult;
        public GameInstancePlugin.TrainingCreatedData CurrentTrainingCreated => currentTrainingCreated;

        /// <summary>게임 서버 연결 상태 (Handler가 설정)</summary>
        public bool IsConnectedToGameServer { get; set; }
        #endregion

        #region Handler Bridge Methods
        /// <summary>Handler에서 호출: 게임 서버 연결 상태 변경 알림</summary>
        public void NotifyGameServerConnectionChanged(bool connected)
        {
            IsConnectedToGameServer = connected;
            OnGameServerConnectionChanged?.Invoke(connected);
            if (!connected)
                OnDisconnectedFromGameServer?.Invoke();
        }

        /// <summary>Handler에서 호출: 게임 서버 연결 해제</summary>
        public void RequestDisconnectFromGameServer()
        {
            // TraineeClientHandler가 실제 연결 해제 수행
            // 여기서는 이벤트만 발화 (Handler가 없는 환경에서의 폴백)
            OnGameServerConnectionChanged?.Invoke(false);
        }
        #endregion
    }
}
