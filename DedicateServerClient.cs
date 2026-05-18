using System;
using DarkRift;
using DarkRift.Client;
using DarkRift.Client.Unity;
using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;
using GameInstancePlugin;
using MT = DatabasePlugin.MessageTags;

namespace GameInstancePlugin.Client
{
    /// <summary>
    /// 게임 인스턴스 클라이언트 — 순수 DarkRift 메시지 I/O
    /// Headless Unity 빌드에서 Logic Server(DarkRift)와 통신:
    /// - 인스턴스 등록 및 관리
    /// - 훈련 플로우 메시지 송수신 (TrainingWake/WakeResult/Create/Begin/BeginReady)
    /// - 인스턴스 시그널 브로드캐스트 수신
    ///
    /// FishNet 서버 라이프사이클, FTP 다운로드, 플레이어 카운트 등
    /// 오케스트레이션 로직은 DedicateServerHandler에서 처리합니다.
    /// </summary>
    public class DedicateServerClient : MonoBehaviour
    {
        [Header("Logic Server 연결 (DarkRift)")]
        [SerializeField] private string logicServerAddress = "127.0.0.1";
        [SerializeField] private int logicServerPort = 4296;

        [Header("인스턴스 정보")]
        [SerializeField] private int instanceId;
        [SerializeField] private int instancePort;
        [SerializeField] private int assignedPartyId;
        [SerializeField] private string sceneFile;

        private UnityClient client;
        private CancellationTokenSource heartbeatCts;
        private readonly TimeSpan heartbeatInterval = TimeSpan.FromSeconds(5);

        /// <summary>응답 코드</summary>
        public enum ResponseCode : byte
        {
            Success = 0,
            Failed = 1,
            NoAvailableInstance = 2,
            PartyNotFound = 3,
            InstanceNotFound = 4,
            AlreadyRunning = 5,
            InvalidData = 6
        }

        #region Events

        public event Action OnConnectedToLogicServer;
        public event Action<int> OnPartyDataReceived;
        public event Action OnRegistrationSuccess;
        public event Action<string> OnError;

        /// <summary>훈련 시작 신호 수신 (교관 주도 플로우)</summary>
        public event Action<int, int> OnTrainingBegin; // instanceId, partyId

        /// <summary>TrainingWakeResult 수신 (파티/훈련 정보)</summary>
        public event Action<TrainingWakeResultData> OnTrainingWakeResult;

        /// <summary>TrainingCreate 응답 수신</summary>
        public event Action OnTrainingCreateSuccess;

        /// <summary>인스턴스 시그널 수신 (Start/Stop/Pause/Resume 등)</summary>
        public event Action<InstanceSignalType, int, int, string> OnInstanceSignal; // signalType, instanceId, partyId, additionalData

        /// <summary>GracefulQuit 수신 (서버→데디케이트 종료 지시) — 훈련생 전원 ACK 완료 후 전송됨</summary>
        public event Action<int> OnGracefulQuit; // partyId

        /// <summary>TrainingExits 수신 (서버→데디케이트 훈련 종료 지시)</summary>
        public event Action OnTrainingExits; 

        #endregion

        #region State

        private bool isRegistered = false;
        private bool isReady = false;

        // 파티/훈련 정보 (WakeResult에서 수신)
        private TrainingWakeResultData currentTrainingInfo;

        // 교관 확인 여부 (서버에서 TrainingBeginReady ACK 수신 시 true)
        private bool isInstructorCheckCompleted = false;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            ParseCommandLineArgs();

            if (!TryGetComponent(out client))
            {
                client = gameObject.AddComponent<UnityClient>();
            }

            // connectOnStart를 false로 설정 - DedicateServerClient가 직접 연결 관리
            var connectOnStartField = typeof(UnityClient).GetField("connectOnStart",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (connectOnStartField != null)
                connectOnStartField.SetValue(client, false);

            client.Host = logicServerAddress;
            client.Port = (ushort)logicServerPort;

            client.MessageReceived += OnMessageReceived;
            client.Disconnected += OnDisconnected;
        }

        private void Start()
        {
            Debug.Log($"[게임 인스턴스] 시작 중: 인스턴스ID={instanceId}, 포트={instancePort}, 파티={assignedPartyId}");
            ConnectToLogicServer();
        }

        private void OnDestroy()
        {
            CancelInvoke(nameof(ConnectToLogicServer));
            StopHeartbeat();

            if (isRegistered && client != null)
            {
                SendShutdownNotification();
            }

            if (client != null)
            {
                client.MessageReceived -= OnMessageReceived;
                client.Disconnected -= OnDisconnected;
            }
        }

        #endregion

        #region Command Line Args

        private void ParseCommandLineArgs()
        {
            string[] args = Environment.GetCommandLineArgs();

            for (int i = 0; i < args.Length - 1; i++)
            {
                switch (args[i].ToLower())
                {
                    case "-instanceid":
                        if (int.TryParse(args[i + 1], out int id))
                            instanceId = id;
                        break;
                    case "-port":
                        if (int.TryParse(args[i + 1], out int port))
                            instancePort = port;
                        break;
                    case "-partyid":
                        if (int.TryParse(args[i + 1], out int partyId))
                            assignedPartyId = partyId;
                        break;
                    case "-scene":
                        sceneFile = args[i + 1];
                        break;
                }
            }

            Debug.Log($"[게임 인스턴스] 인자 파싱 완료: ID={instanceId}, 포트={instancePort}, 파티={assignedPartyId}, 씬={sceneFile}");
        }

        #endregion

        #region DarkRift Connection

        private void ConnectToLogicServer()
        {
            if (client == null)
            {
                Debug.LogError("[게임 인스턴스] Unity Client가 null입니다");
                return;
            }

            client.ConnectInBackground(
                logicServerAddress,
                logicServerPort,
                true,
                OnConnectComplete
            );

            Debug.Log($"[게임 인스턴스] Logic Server 연결 중: {logicServerAddress}:{logicServerPort}");
        }

        private void OnConnectComplete(Exception e)
        {
            if (e != null)
            {
                Debug.LogError($"[게임 인스턴스] 연결 실패: {e.Message}");
                OnError?.Invoke($"Logic Server 연결 실패: {e.Message}");
                Invoke(nameof(ConnectToLogicServer), 5f);
                return;
            }

            Debug.Log("[게임 인스턴스] Logic Server 연결 완료");
            OnConnectedToLogicServer?.Invoke();

#if UNITY_EDITOR
            // 에디터에서는 UnknownDedicate 전송 (서버에서 인스턴스 정보를 받은 뒤 TrainingWake 수행)
            SendUnknownDedicate();
#else
            // TrainingWake 전송 (IP/Port 정보)
            SendTrainingWake();
#endif
        }

        private void OnDisconnected(object sender, DisconnectedEventArgs e)
        {
            Debug.LogWarning($"[게임 인스턴스] Logic Server 연결 끊김: {e.Error.ToString() ?? "알 수 없음"}");

            StopHeartbeat();
            isRegistered = false;
            isReady = false;

            Invoke(nameof(ConnectToLogicServer), 5f);
        }

        #endregion

        #region Message Send Methods

        /// <summary>에디터 전용: UnknownDedicate를 서버로 전송하여 인스턴스 대기 등록합니다.</summary>
        private void SendUnknownDedicate()
        {
            try
            {
                using (Message message = Message.CreateEmpty(MT.UnknownDedicate))
                {
                    client.SendMessage(message, SendMode.Reliable);
                }

                SendClientLog(0, "[게임 인스턴스] UnknownDedicate 전송됨 (서버 응답 대기)");
            }
            catch (Exception ex)
            {
                SendClientLog(2, $"[게임 인스턴스] UnknownDedicate 전송 오류: {ex.Message}");
                OnError?.Invoke($"UnknownDedicate 전송 실패: {ex.Message}");
            }
        }

        /// <summary>TrainingWake를 서버로 전송합니다 (IP/Port 정보).</summary>
        private void SendTrainingWake()
        {
            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write(instanceId);
                    writer.Write(GetLocalIPAddress());
                    writer.Write(instancePort);

                    using (Message message = Message.Create(MT.TrainingWake, writer))
                    {
                        client.SendMessage(message, SendMode.Reliable);
                    }
                }

                SendClientLog(0, $"[게임 인스턴스] TrainingWake 전송됨: ID={instanceId}, 포트={instancePort}");
            }
            catch (Exception ex)
            {
                SendClientLog(2, $"[게임 인스턴스] TrainingWake 전송 오류: {ex.Message}");
                OnError?.Invoke($"TrainingWake 전송 실패: {ex.Message}");
            }
        }

        /// <summary>TrainingCreate를 서버로 전송합니다 (Fishnet 서버 준비 완료).</summary>
        public void SendTrainingCreate()
        {
            if (currentTrainingInfo == null)
            {
                SendClientLog(1, "[게임 인스턴스] TrainingCreate 전송 불가: 훈련 정보 없음");
                return;
            }

            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write(currentTrainingInfo.PartyId);
                    writer.Write(instanceId);
                    writer.Write(true); // isReady

                    using (Message message = Message.Create(MT.TrainingCreate, writer))
                    {
                        client.SendMessage(message, SendMode.Reliable);
                    }
                }

                SendClientLog(0, $"[게임 인스턴스] TrainingCreate 전송됨: 파티={currentTrainingInfo.PartyId}");
            }
            catch (Exception ex)
            {
                SendClientLog(2, $"[게임 인스턴스] TrainingCreate 전송 오류: {ex.Message}");
                OnError?.Invoke($"TrainingCreate 전송 실패: {ex.Message}");
            }
        }

        /// <summary>TrainResultPush를 로직 서버로 전송합니다 (훈련 결과 저장 요청).</summary>
        public void SendTrainResultPush(TrainResult data)
        {
            if (data == null)
            {
                SendClientLog(1, "[게임 인스턴스] SendTrainResultPush: data가 null");
                return;
            }

            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write(data);
                    using (Message message = Message.Create(MT.TrainResultPush, writer))
                    {
                        client.SendMessage(message, SendMode.Reliable);
                    }
                }

                SendClientLog(0, $"[게임 인스턴스] TrainResultPush 전송됨: 파티={data.partyId}, 타입={data.trainingType}");
            }
            catch (Exception ex)
            {
                SendClientLog(2, $"[게임 인스턴스] TrainResultPush 전송 오류: {ex.Message}");
                OnError?.Invoke($"TrainResultPush 전송 실패: {ex.Message}");
            }
        }

        /// <summary>TrainingBeginReady를 서버로 전송합니다 (모든 파티원 접속 완료).</summary>
        public void SendTrainingBeginReady(int currentConnectedCount, int expectedPlayerCount)
        {
            if (currentTrainingInfo == null)
            {
                SendClientLog(1, "[게임 인스턴스] TrainingBeginReady 전송 불가: 훈련 정보 없음");
                return;
            }

            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write(currentTrainingInfo.PartyId);
                    writer.Write(instanceId);
                    writer.Write(currentConnectedCount);
                    writer.Write(expectedPlayerCount);
                    writer.Write(currentConnectedCount >= expectedPlayerCount);

                    using (Message message = Message.Create(MT.TrainingBeginReady, writer))
                    {
                        client.SendMessage(message, SendMode.Reliable);
                    }
                }

                SendClientLog(0, $"[게임 인스턴스] TrainingBeginReady 전송됨: 접속={currentConnectedCount}/{expectedPlayerCount}");
            }
            catch (Exception ex)
            {
                SendClientLog(2, $"[게임 인스턴스] TrainingBeginReady 전송 오류: {ex.Message}");
                OnError?.Invoke($"TrainingBeginReady 전송 실패: {ex.Message}");
            }
        }

        private string GetLocalIPAddress()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                SendClientLog(1, $"[게임 인스턴스] IP 주소 가져오기 실패: {ex.Message}");
            }
            return "127.0.0.1";
        }


        /// <summary>ClientLog를 서버로 전송합니다 (서버 콘솔에 게임 인스턴스 로그 출력).</summary>
        public void SendClientLog(byte logLevel, string logMessage)
        {
            if (logLevel == 0) Debug.Log(logMessage);
            else if (logLevel == 1) Debug.LogWarning(logMessage);
            else if (logLevel == 2) Debug.LogError(logMessage);

            if (client == null || client.ConnectionState != DarkRift.ConnectionState.Connected)
                return;

            try
            {
                using (DarkRift.DarkRiftWriter writer = DarkRift.DarkRiftWriter.Create())
                {
                    writer.Write(logLevel);
                    writer.Write(logMessage);

                    using (DarkRift.Message message = DarkRift.Message.Create(DatabasePlugin.MessageTags.ClientLog, writer))
                    {
                        client.SendMessage(message, DarkRift.SendMode.Reliable);
                    }
                }
            }
            catch (System.Exception ex)
            {
                SendClientLog(2, $"[게임 인스턴스] ClientLog 전송 오류: {ex.Message}");
            }
        }

        #endregion

        #region Heartbeat

        private void StartHeartbeat()
        {
            StopHeartbeat();
            heartbeatCts = new CancellationTokenSource();
            HeartbeatLoopAsync(heartbeatCts.Token).Forget();
            SendClientLog(0, "[게임 인스턴스] 하트비트 시작됨");
        }

        private void StopHeartbeat()
        {
            if (heartbeatCts != null)
            {
                heartbeatCts.Cancel();
                heartbeatCts.Dispose();
                heartbeatCts = null;
                SendClientLog(0, "[게임 인스턴스] 하트비트 중지됨");
            }
        }

        private async UniTaskVoid HeartbeatLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && isRegistered && client != null)
                {
                    SendHeartbeat();
                    await UniTask.Delay(heartbeatInterval, cancellationToken: ct);
                }
            }
            catch (OperationCanceledException)
            {
                SendClientLog(0, "[게임 인스턴스] 하트비트 루프 취소됨");
            }
            catch (Exception ex)
            {
                SendClientLog(2, $"[게임 인스턴스] 하트비트 루프 오류: {ex.Message}");
            }
        }

        private void SendHeartbeat()
        {
            if (!isRegistered || client == null)
                return;

            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write(instanceId);

                    using (Message message = Message.Create(MT.InstanceHeartbeat, writer))
                    {
                        client.SendMessage(message, SendMode.Reliable);
                    }
                }
            }
            catch (Exception ex)
            {
                SendClientLog(2, $"[게임 인스턴스] 하트비트 전송 오류: {ex.Message}");
            }
        }

        private void SendShutdownNotification()
        {
            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write(instanceId);

                    using (Message message = Message.Create(MT.InstanceShutdown, writer))
                    {
                        client.SendMessage(message, SendMode.Reliable);
                    }
                }

                SendClientLog(0, "[게임 인스턴스] 종료 알림 전송됨");
            }
            catch (Exception ex)
            {
                SendClientLog(2, $"[게임 인스턴스] 종료 알림 전송 오류: {ex.Message}");
            }
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
                        case MT.InstanceRegister:
                            OnRegistrationResponse(message);
                            break;
                        case MT.PartyData:
                            OnPartyDataResponse(message);
                            break;
                        case MT.TrainingWakeResult:
                            OnTrainingWakeResultReceived(message);
                            break;
                        case MT.TrainingCreate:
                            OnTrainingCreateResponse(message);
                            break;
                        case MT.TrainingBegin:
                            OnTrainingBeginReceived(message);
                            break;
                        case MT.TrainingBeginReady:
                            OnTrainingBeginReadyResponse(message);
                            break;
                        case MT.InstanceSignalBroadcast:
                            OnInstanceSignalBroadcastReceived(message);
                            break;
                        case MT.UnknownDedicateRespone:
                            OnUnknownDedicateReceived(message);
                            break;
                        case MT.GracefulQuit:
                            OnGracefulQuitReceived(message);
                            break;
                        case MT.TrainingExits:
                            OnTrainingExitsReceived(message);
                            break;
                        case MT.TrainResultPull:
                            OnTrainingRsultPush(message);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    SendClientLog(2, $"[게임 인스턴스] 메시지 처리 오류: {ex.Message}");
                }
            }
        }

        /// <summary>서버에서 UnknownDedicate 응답 수신 — 인스턴스 정보 업데이트 후 TrainingWake 전송</summary>
        private void OnUnknownDedicateReceived(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                ResponseCode code = (ResponseCode)reader.ReadByte();

                if (code == ResponseCode.Success)
                {
                    int newInstanceId = reader.ReadInt32();
                    int newPort = reader.ReadInt32();
                    int newPartyId = reader.ReadInt32();
                    string newSceneFile = reader.ReadString();

                    instanceId = newInstanceId;
                    instancePort = newPort;
                    assignedPartyId = newPartyId;
                    sceneFile = newSceneFile;

                    SendClientLog(0, $"[게임 인스턴스] UnknownDedicate 수신 — ID={instanceId}, Port={instancePort}, PartyID={assignedPartyId}, Scene={sceneFile}");

                    // 정보 업데이트 후 TrainingWake 전송
                    SendTrainingWake();
                }
                else
                {
                    SendClientLog(2, $"[게임 인스턴스] UnknownDedicate 실패: {code}");
                    OnError?.Invoke($"UnknownDedicate 실패: {code}");
                }
            }
        }

        private void OnRegistrationResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                ResponseCode code = (ResponseCode)reader.ReadByte();

                if (code == ResponseCode.Success)
                {
                    isRegistered = true;
                    SendClientLog(0, "[게임 인스턴스] 등록 성공");
                    OnRegistrationSuccess?.Invoke();
                }
                else
                {
                    SendClientLog(2, $"[게임 인스턴스] 등록 실패: {code}");
                    OnError?.Invoke($"등록 실패: {code}");
                }
            }
        }

        private void OnPartyDataResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                ResponseCode code = (ResponseCode)reader.ReadByte();

                if (code == ResponseCode.Success)
                {
                    int partyId = reader.ReadInt32();
                    SendClientLog(0, $"[게임 인스턴스] 파티 데이터 수신됨: 파티ID={partyId}");
                    OnPartyDataReceived?.Invoke(partyId);
                }
                else
                {
                    SendClientLog(2, $"[게임 인스턴스] 파티 데이터 수신 실패: {code}");
                }
            }
        }

        private void OnTrainingWakeResultReceived(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                ResponseCode code = (ResponseCode)reader.ReadByte();

                if (code == ResponseCode.Success)
                {
                    currentTrainingInfo = reader.ReadSerializable<TrainingWakeResultData>();

                    assignedPartyId = currentTrainingInfo.PartyId;
                    sceneFile = currentTrainingInfo.SceneFile;
                    isRegistered = true;

                    SendClientLog(0, $"[게임 인스턴스] TrainingWakeResult 수신: {currentTrainingInfo}");

                    // 하트비트 시작
                    StartHeartbeat();

                    // 이벤트 발생 (Handler가 FishNet 서버 시작 + FTP 다운로드 수행)
                    OnTrainingWakeResult?.Invoke(currentTrainingInfo);
                }
                else
                {
                    SendClientLog(2, $"[게임 인스턴스] TrainingWakeResult 수신 실패: {code}");
                    OnError?.Invoke($"TrainingWakeResult 실패: {code}");
                }
            }
        }

        private void OnTrainingCreateResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                ResponseCode code = (ResponseCode)reader.ReadByte();

                if (code == ResponseCode.Success)
                {
                    isReady = true;
                    SendClientLog(0, "[게임 인스턴스] TrainingCreate 성공 - 훈련생 접속 대기 중");
                    OnTrainingCreateSuccess?.Invoke();
                }
                else
                {
                    SendClientLog(2, $"[게임 인스턴스] TrainingCreate 실패: {code}");
                    OnError?.Invoke($"TrainingCreate 실패: {code}");
                }
            }
        }

        private void OnTrainingBeginReadyResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                ResponseCode code = (ResponseCode)reader.ReadByte();

                if (code == ResponseCode.Success)
                {
                    isInstructorCheckCompleted = true;
                    SendClientLog(0, "[게임 인스턴스] TrainingBeginReady ACK 수신 — 교관 확인 대기 중");
                }
                else
                {
                    SendClientLog(2, $"[게임 인스턴스] TrainingBeginReady 실패: {code}");
                }
            }
        }

        private void OnTrainingBeginReceived(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                ResponseCode code = (ResponseCode)reader.ReadByte();

                if (code == ResponseCode.Success)
                {
                    int receivedInstanceId = reader.ReadInt32();
                    int partyId = reader.ReadInt32();

                    SendClientLog(0, $"[게임 인스턴스] 훈련 시작 신호 수신: 인스턴스ID={receivedInstanceId}, 파티ID={partyId}");

                    Debug.Log($"[DedicateServerHandler] 모든 파티원들이 준비가 완료 되어서 필요 요구조자 스폰");
                    TRAINEE.GameEventManager.Instance.Publish(new TRAINEE.GameEvent() { _type = TRAINEE.EEventType.Spawn });

                    // 이벤트만 발화 (게임플레이 활성화는 Handler에서 처리)
                    OnTrainingBegin?.Invoke(receivedInstanceId, partyId);


                }
                else
                {
                    SendClientLog(2, $"[게임 인스턴스] 훈련 시작 신호 처리 실패: {code}");
                }
            }
        }

        private void OnInstanceSignalBroadcastReceived(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                ResponseCode code = (ResponseCode)reader.ReadByte();

                if (code == ResponseCode.Success)
                {
                    InstanceSignalType signalType = (InstanceSignalType)reader.ReadByte();
                    int receivedInstanceId = reader.ReadInt32();
                    int partyId = reader.ReadInt32();
                    string additionalData = reader.ReadString();
                    string timestamp = reader.ReadString();

                    SendClientLog(0, $"[게임 인스턴스] 시그널 수신: {signalType} (인스턴스={receivedInstanceId}, 파티={partyId}, 데이터={additionalData}, 시간={timestamp})");

                    // 이벤트만 발행 (게임플레이 액션은 Handler에서 처리)
                    OnInstanceSignal?.Invoke(signalType, receivedInstanceId, partyId, additionalData);

#if UNITY_EDITOR
                    //Debug.Log($"[DedicateServerHandler] 모든 파티원들이 준비가 완료 되어서 필요 요구조자 스폰");
                    TRAINEE.GameEventManager.Instance.Publish(new TRAINEE.GameEvent() { _type = TRAINEE.EEventType.Spawn });
#endif
                }
                else
                {
                    SendClientLog(2, $"[게임 인스턴스] 시그널 수신 실패: {code}");
                }
            }
        }

        private void OnGracefulQuitReceived(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                int partyId = reader.ReadInt32();
                SendClientLog(0, $"[게임 인스턴스] GracefulQuit 수신: 파티={partyId}");
                OnGracefulQuit?.Invoke(partyId);
            }
        }

        private void OnTrainingExitsReceived(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                InstanceSignalType signalType = (InstanceSignalType)reader.ReadByte();
                int partyId = reader.ReadInt32();
                SendClientLog(0, $"[게임 인스턴스] TrainingExits 수신: signal={signalType}, 파티={partyId}");
                OnTrainingExits?.Invoke();
            }
        }

        private void OnTrainingRsultPush(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                ResponseCode code = (ResponseCode)reader.ReadByte();

                if (code == ResponseCode.Success)
                {
                    SendClientLog(0, $"[게임 인스턴스] 훈련 종료 DB 저장 성공)");

                    // 이벤트만 발행 (게임플레이 액션은 Handler에서 처리)
                }
                else
                {
                    SendClientLog(2, $"[게임 인스턴스] 훈련 종료 DB 저장 실패: {code}");
                }
            }
        }

        #endregion

        #region Public Properties

        /// <summary>인스턴스 ID</summary>
        public int InstanceId => instanceId;

        /// <summary>인스턴스 포트</summary>
        public int InstancePort => instancePort;

        /// <summary>할당된 파티 ID</summary>
        public int AssignedPartyId => assignedPartyId;

        /// <summary>등록 여부</summary>
        public bool IsRegistered => isRegistered;

        /// <summary>준비 완료 여부</summary>
        public bool IsReady => isReady;

        /// <summary>현재 훈련 정보 (WakeResult에서 수신)</summary>
        public TrainingWakeResultData CurrentTrainingInfo => currentTrainingInfo;

        /// <summary>교관 확인 완료 여부 (TrainingBeginReady ACK 수신됨)</summary>
        public bool IsInstructorCheckCompleted => isInstructorCheckCompleted;

        #endregion
    }
}
