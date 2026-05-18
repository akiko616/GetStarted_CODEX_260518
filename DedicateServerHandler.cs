using System;
using System.IO;
using System.Threading;
using UnityEngine;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Broadcast;
using Cysharp.Threading.Tasks;
using Disaster.Network.FTP;
using DrkRft_Shared.Api;
using PartyManagerPlugin;

namespace GameInstancePlugin.Client
{
    /// <summary>세션 시작 브로드캐스트 (서버 → 클라이언트)</summary>
    public struct SessionStartBroadcast : IBroadcast { }

    /// <summary>세션 종료 브로드캐스트 (서버 → 클라이언트)</summary>
    public struct SessionEndBroadcast : IBroadcast
    {
        public string Message;
    }

    /// <summary>
    /// 데디케이트 서버 오케스트레이터 (MonoBehaviour)
    /// DedicateServerClient(DarkRift) ↔ FishNetManagetHandler(FishNet) 브릿지:
    ///
    /// 2단계 구독 모델:
    /// - Awake(): DarkRift 이벤트 구독 (FishNet 시작 전)
    /// - ServerManager.OnServerConnectionState: FishNet 이벤트 구독 (FishNet 시작 후)
    ///
    /// 담당:
    /// - FishNet 서버 라이프사이클 (시작/중지)
    /// - FTP SceneSetFile 다운로드
    /// - 이중 준비 게이트 (FTP + FishNet 모두 준비 → SendTrainingCreate)
    /// - 플레이어 카운트 모니터링 → SendTrainingBeginReady
    /// - 세션 상태 관리
    /// - InstanceSignal 게임플레이 처리 (Pause/Resume/Stop)
    /// </summary>
    public class DedicateServerHandler : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private DedicateServerClient logicBridge;
        [SerializeField] private FishNetManagetHandler connectionHandler;

        [Header("FTP")]
        [SerializeField] private FtpTransferManager ftpManager;

        [Header("FishNet")]
        [SerializeField] private NetworkManager fishnetManager;
        [SerializeField] private Transport fishnetTransport;

        private string sceneSetJson = string.Empty;
        public string SceneSetJson
        {
            get { return sceneSetJson; }
        }
        public enum SessionState
        {
            WaitingForPlayers,
            InGame,
            Ending
        }

        private SessionState _currentState = SessionState.WaitingForPlayers;
        public SessionState CurrentState => _currentState;

        /// <summary>세션 상태 변경 이벤트 (oldState, newState)</summary>
        public event Action<SessionState, SessionState> OnSessionStateChanged;

        /// <summary>FishNet 서버 시작됨 (MazeGameManager 등 외부 구독용)</summary>
        public event Action OnFishnetServerStarted;

        // ── NPC 스폰/디스폰 오버라이드 훅 (DedicatedServerNPCSpawner 병합) ──

        /// <summary>
        /// NPC 스폰 로직을 주입하는 훅. InGame 상태 진입 시 호출됩니다.
        /// 미설정 시 스폰이 수행되지 않습니다.
        /// </summary>
        public Action<NetworkManager> OnSpawnNPCsOverride { get; set; }

        /// <summary>
        /// NPC 디스폰 로직을 주입하는 훅. Ending 상태 진입 또는 OnDestroy 시 호출됩니다.
        /// 미설정 시 디스폰이 수행되지 않습니다.
        /// </summary>
        public Action OnDespawnNPCsOverride { get; set; }

        // 내부 상태
        private bool isFishnetServerRunning;
        private bool isFtpDownloadComplete;
        private bool isGameplayActive;
        private bool hasSpawnedNPCs;
        private CancellationTokenSource lifecycleCts;
        #region Unity Lifecycle (Phase 1: DarkRift 이벤트 구독)

        private void Awake()
        {
            if (logicBridge == null) logicBridge = FindFirstObjectByType<DedicateServerClient>();
            if (connectionHandler == null) connectionHandler = FindFirstObjectByType<FishNetManagetHandler>();
            if (fishnetManager == null) fishnetManager = FindFirstObjectByType<NetworkManager>();
            if (ftpManager == null) ftpManager = FindFirstObjectByType<FtpTransferManager>();
            if (fishnetManager != null && fishnetTransport == null)
                fishnetTransport = fishnetManager.TransportManager.Transport;

            lifecycleCts = new CancellationTokenSource();
            InitTrainResultPushDelegate();
            // 상태 변경은 SetState()에서 이벤트 발화

            // DarkRift 이벤트 (FishNet 시작 전에 구독 필요)
            if (logicBridge != null)
            {
                logicBridge.OnTrainingWakeResult += OnTrainingWakeResultReceived;
                logicBridge.OnTrainingBegin += StartGameSession;
                logicBridge.OnInstanceSignal += OnInstanceSignalReceived;
                logicBridge.OnGracefulQuit += OnGracefulQuitReceived;
                logicBridge.OnTrainingExits += OnTrainingExitsReceived;
            }

            // FishNet 서버 상태 이벤트 구독
            if (fishnetManager != null)
            {
                fishnetManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
            }
        }

        private void OnDestroy()
        {
            lifecycleCts?.Cancel();
            lifecycleCts?.Dispose();

            if (logicBridge != null)
            {
                logicBridge.OnTrainingWakeResult -= OnTrainingWakeResultReceived;
                logicBridge.OnTrainingBegin -= StartGameSession;
                logicBridge.OnInstanceSignal -= OnInstanceSignalReceived;
                logicBridge.OnGracefulQuit -= OnGracefulQuitReceived;
                logicBridge.OnTrainingExits -= OnTrainingExitsReceived;
            }

            if (fishnetManager != null)
            {
                fishnetManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
            }

            if (connectionHandler != null)
            {
                connectionHandler.OnTrainResultPushRequest -= HandleTrainResultPushRequest;
                connectionHandler.OnReadyPlayerCountChanged -= OnReadyPlayerCountChanged;
            }

            DespawnNPCs();

            if (isFishnetServerRunning)
                StopFishnetServer();
        }

        #endregion

        #region FishNet Lifecycle (Phase 2: ServerManager 이벤트)

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                SendClientLog(0, "[DedicateServerHandler] FishNet 서버 시작됨. 세션 대기 상태.");

                // FishNet이 실행 중인 상태에서만 구독
                if (connectionHandler != null)
                {
                    connectionHandler.OnReadyPlayerCountChanged += OnReadyPlayerCountChanged;
                }
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                if (connectionHandler != null)
                {
                    connectionHandler.OnReadyPlayerCountChanged -= OnReadyPlayerCountChanged;
                }
            }
        }

        #endregion

        #region TrainingWakeResult → FTP + FishNet 시작

        private void OnTrainingWakeResultReceived(TrainingWakeResultData data)
        {
            SendClientLog(0, $"[DedicateServerHandler] WakeResult 수신: 파티={data.PartyId}, SceneSetFile={data.SceneSetFile}");

            // 파티 검증 설정
            if (connectionHandler != null)
                connectionHandler.RequiredPartyId = data.PartyId;

            // FTP 다운로드 (비동기, 논블로킹)
            if (!string.IsNullOrEmpty(data.SceneSetFile) && ftpManager != null)
            {
                DownloadSceneSetFileAsync(data.SceneSetFile, lifecycleCts.Token).Forget();
                InitDataLoading();
            }
            else
            {
                isFtpDownloadComplete = true; // FTP 불필요 시 즉시 통과
            }

            // FishNet 서버 시작
            StartFishnetServer();
        }

        #endregion

        #region FTP Download

        private async UniTask DownloadSceneSetFileAsync(string sceneSetFile, CancellationToken ct)
        {
            try
            {
                string localPath = Path.Combine(Application.persistentDataPath, "SceneSet", sceneSetFile);

                //디렉토리 생성
                string dir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                SendClientLog(0, $"[DedicateServerHandler] FTP 다운로드 시작: {sceneSetFile} → {localPath}");
                sceneSetJson = await ftpManager.DownloadStringAsync(FtpContentType.ConfirmScenario, sceneSetFile);




                isFtpDownloadComplete = true;
                SendClientLog(0, $"[DedicateServerHandler] FTP 다운로드 완료: {sceneSetFile}");
            }
            catch (OperationCanceledException)
            {
                SendClientLog(0, "[DedicateServerHandler] FTP 다운로드 취소됨");
            }
            catch (Exception ex)
            {
                SendClientLog(2, $"[DedicateServerHandler] FTP 다운로드 실패: {ex.Message}");
                isFtpDownloadComplete = true; // 실패해도 플로우 진행 허용
            }
        }

        #endregion

        #region FishNet Server Lifecycle (DedicateServerClient에서 이동)

        private void StartFishnetServer()
        {
            if (fishnetManager == null)
            {
                SendClientLog(2, "[DedicateServerHandler] Fishnet 서버를 시작할 수 없음: NetworkManager가 null");
                return;
            }

            if (isFishnetServerRunning)
            {
                SendClientLog(1, "[DedicateServerHandler] Fishnet 서버가 이미 실행 중");
                return;
            }

            try
            {
                if (fishnetTransport != null && logicBridge != null)
                {
                    SetTransportPort(fishnetTransport, logicBridge.InstancePort);
                }

                fishnetManager.ServerManager.StartConnection();
                isFishnetServerRunning = true;

                SendClientLog(0, $"[DedicateServerHandler] Fishnet 서버 시작됨: 포트={logicBridge?.InstancePort}");
                OnFishnetServerStarted?.Invoke();

                // 이중 준비 게이트: FTP + FishNet 모두 준비되면 TrainingCreate 전송
                WaitForReadinessAndSendCreateAsync().Forget();
            }
            catch (Exception ex)
            {
                SendClientLog(2, $"[DedicateServerHandler] Fishnet 서버 시작 실패: {ex.Message}");
            }
        }

        private void StopFishnetServer()
        {
            if (fishnetManager == null || !isFishnetServerRunning)
                return;

            try
            {
                fishnetManager.ServerManager.StopConnection(true);
                isFishnetServerRunning = false;
                SendClientLog(0, "[DedicateServerHandler] Fishnet 서버 중지됨");
            }
            catch (Exception ex)
            {
                SendClientLog(2, $"[DedicateServerHandler] Fishnet 서버 중지 오류: {ex.Message}");
            }
        }

        private void SetTransportPort(Transport transport, int port)
        {
            if (transport is FishNet.Transporting.Tugboat.Tugboat tugboat)
            {
                tugboat.SetPort((ushort)port);
                SendClientLog(0, $"[DedicateServerHandler] Tugboat 포트 설정: {port}");
                return;
            }

#if !UNITY_WEBGL || UNITY_EDITOR
            var bayouType = transport.GetType();
            if (bayouType.Name == "Bayou")
            {
                var portField = bayouType.GetField("_port", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (portField != null)
                {
                    portField.SetValue(transport, (ushort)port);
                    SendClientLog(0, $"[DedicateServerHandler] Bayou 포트 설정: {port}");
                    return;
                }
            }
#endif

            try
            {
                var type = transport.GetType();
                var portProperty = type.GetProperty("Port");
                var portField2 = type.GetField("_port", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (portProperty != null && portProperty.CanWrite)
                {
                    portProperty.SetValue(transport, (ushort)port);
                }
                else if (portField2 != null)
                {
                    portField2.SetValue(transport, (ushort)port);
                }
                else
                {
                    SendClientLog(1, $"[DedicateServerHandler] Transport 포트 설정 실패: {type.Name}");
                }
            }
            catch (Exception ex)
            {
                SendClientLog(2, $"[DedicateServerHandler] Transport 포트 설정 오류: {ex.Message}");
            }
        }

        #endregion

        #region Readiness Gate (FTP + FishNet → SendTrainingCreate)

        private async UniTaskVoid WaitForReadinessAndSendCreateAsync()
        {
            //try
            //{
                var ct = lifecycleCts.Token;

                // FishNet 서버가 완전히 시작될 때까지 대기
                await UniTask.WaitUntil(() => fishnetManager.ServerManager.Started, cancellationToken: ct);

                await UniTask.WaitUntil(() => isLocalDataLoading, cancellationToken: ct);
                // FTP 다운로드 완료 대기
                await UniTask.WaitUntil(() => isFtpDownloadComplete, cancellationToken: ct);

                if (!string.IsNullOrEmpty(sceneSetJson))
                {
                    TRAINEE.DataManager.Instance.ServerData = sceneSetJson;
                    TRAINEE.DataManager.Instance.OnServerDataSetup();
                }

                ServerDataLoading();

                await UniTask.WaitUntil(() => isServerDataLoading, cancellationToken: ct);


                TRAINEE.SceneBase currentScene = FindFirstObjectByType<TRAINEE.SceneBase>();

                if (currentScene != null)
                {
                    currentScene.Init();
                    SendClientLog(0, "[DedicateServerHandler] 트레이닝 씬 수동 Init 완료");
                    SendClientLog(0, "[DedicateServerHandler] 네트워크 및 데이터 준비 완료. 시스템 로딩(Headless) 시작...");
                    await TRAINEE.LoadingHelper.LoadingAsync(SetProgress, 0);

                    SendClientLog(0, "[DedicateServerHandler] 시스템 로딩 완료! 클라이언트 접속을 허용합니다.");
                }
                else
                {
                    SendClientLog(1, "[DedicateServerHandler] 현재 씬에서 SceneBase를 찾을 수 없습니다.");
                }



                if (logicBridge != null)
                {
                    logicBridge.SendTrainingCreate();
                    SendClientLog(0, "[DedicateServerHandler] FTP+FishNet 준비 완료 → TrainingCreate 전송");
                }
            // }
            // catch (OperationCanceledException)
            // {
            //     SendClientLog(0, "[DedicateServerHandler] 준비 대기 취소됨");
            // }
            // catch (Exception ex)
            // {
            //     SendClientLog(2, $"[DedicateServerHandler] 준비 대기 오류: {ex.Message}");
            // }
        }

        private bool isLocalDataLoading = false;
        private bool isServerDataLoading = false;

        private void InitDataLoading()
        {
            LocalDataFileAsync(lifecycleCts.Token).Forget();
        }

        private void ServerDataLoading()
        {
            ServerDataFileAsync(lifecycleCts.Token).Forget();
        }

        private async UniTask LocalDataFileAsync(CancellationToken ct)
        {
            isLocalDataLoading = await TRAINEE.LoadingHelper.LoadingAsync(SetProgress, 0);
            if (!isLocalDataLoading)
            {
                TRAINEE.LoadingHelper.AllClear();
                isLocalDataLoading = true;
            }
        }

        private async UniTask ServerDataFileAsync(CancellationToken ct)
        {
            isServerDataLoading = await TRAINEE.LoadingHelper.LoadingAsync(SetProgress, 0);

            if (!isServerDataLoading)
            {
                TRAINEE.LoadingHelper.AllClear();
                isServerDataLoading = true;
            }
        }

        public void SetProgress(float progress, string msg)
        {
            if (progress >= 1f)
            {
                //TRAINEE.LoadingHelper.AllClear();
                SendClientLog(0, $"{msg}");
            }
        }

        #endregion

        #region Player Count → TrainingBeginReady

        private void OnReadyPlayerCountChanged(int count)
        {
            var info = logicBridge?.CurrentTrainingInfo;
            if (info != null && count >= info.ExpectedPlayerCount)
            {
                SendClientLog(0, $"[DedicateServerHandler] 모든 파티원 준비 완료: {count}/{info.ExpectedPlayerCount}");
                SendClientLog(0, $"[DedicateServerHandler] 모든 파티원들이 준비가 완료 되어서 필요 요구조자 스폰");
                //TRAINEE.GameEventManager.Instance.Publish(new TRAINEE.GameEvent() { _type = TRAINEE.EEventType.Spawn });
                logicBridge.SendTrainingBeginReady(count, info.ExpectedPlayerCount);
            }
        }

        #endregion

        #region Session State Management

        public void StartGameSession(int instanceId, int partyId)
        {
            if (_currentState != SessionState.WaitingForPlayers) return;

            isGameplayActive = true;
            SetState(SessionState.InGame);
            SendClientLog(0, $"[DedicateServerHandler] 인스턴스 {instanceId} / 파티 {partyId} 훈련 시작! 상태: InGame");
            BroadcastSessionStart();
            TRAINEE.GameManager.Instance.GameStart();

            SpawnNPCs();

        }

        public void InitiateGracefulShutdown(string reason = "서버가 훈련을 종료했습니다. 로비로 이동합니다.")
        {
            if (_currentState == SessionState.Ending) return;

            isGameplayActive = false;
            DespawnNPCs();
            SetState(SessionState.Ending);
            SendClientLog(0, $"[DedicateServerHandler] 우아한 종료 시작: {reason}");

            BroadcastSessionEnd(reason);
            ExecuteDelayedShutdownAsync().Forget();
        }

        private async UniTaskVoid ExecuteDelayedShutdownAsync()
        {
            await UniTask.Delay(TimeSpan.FromSeconds(2f));
            SendClientLog(0, "[DedicateServerHandler] 클라이언트 대피 완료 추정. 서버 접속 종료.");

            foreach (var conn in fishnetManager.ServerManager.Clients.Values)
            {
                conn.Disconnect(true);
            }
        }

        private void SetState(SessionState newState)
        {
            var oldState = _currentState;
            if (oldState == newState) return;
            _currentState = newState;
            SendClientLog(0, $"[DedicateServerHandler] 세션 상태 변경: {oldState} → {newState}");
            OnSessionStateChanged?.Invoke(oldState, newState);
        }

        private void BroadcastSessionStart()
        {
            SendClientLog(0, "[DedicateServerHandler] 세션 시작 브로드캐스트 전송");
            fishnetManager.ServerManager.Broadcast(new SessionStartBroadcast());
        }

        private void BroadcastSessionEnd(string message)
        {
            SendClientLog(0, $"[DedicateServerHandler] 세션 종료 브로드캐스트 전송: {message}");
            fishnetManager.ServerManager.Broadcast(new SessionEndBroadcast { Message = message });
        }

        #endregion

        #region InstanceSignal 게임플레이 처리 (DedicateServerClient에서 이동)

        private void OnInstanceSignalReceived(InstanceSignalType signalType, int instanceId, int partyId, string data)
        {
            switch (signalType)
            {
                case InstanceSignalType.Start:
                    SendClientLog(0, $"[DedicateServerHandler] 훈련 시작 시그널! 파티={partyId}");
                    StartGameSession(instanceId, partyId);
                    break;

                case InstanceSignalType.Stop:
                    SendClientLog(0, $"[DedicateServerHandler] 훈련 종료 시그널! 파티={partyId}");
                    Time.timeScale = 1f;
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    InitiateGracefulShutdown("훈련 종료 시그널");
                    UniTask.Delay(TimeSpan.FromSeconds(3f)).ContinueWith(() => Application.Quit()).Forget();
#endif
                    break;

                case InstanceSignalType.Pause:
                    SendClientLog(0, $"[DedicateServerHandler] 훈련 일시정지! 파티={partyId}");
                    Time.timeScale = 0f;
                    break;

                case InstanceSignalType.Resume:
                    SendClientLog(0, $"[DedicateServerHandler] 훈련 재개! 파티={partyId}");
                    Time.timeScale = 1f;
                    break;

                default:
                    SendClientLog(0, $"[DedicateServerHandler] 기타 시그널: {signalType}");
                    break;
            }
        }

        #endregion

        #region TrainResultPush (게임 로직 → 로직 서버)

        /// <summary>
        /// 게임 로직(훈련 결과 시스템)이 훈련 결과를 로직 서버로 전송 요청할 때 이 델리게이트를 호출합니다.
        /// SetTrainResultInit 이나 SetTrainResultTrainee 입력없이 
        ///  dedicateServerHandler.OnTrainResultPushRequest?.Invoke(); 보내면 테스트로 인식하고 db저장없이 종료 프로세스 진행됩니다.
        /// </summary>
        public Action<TrainResult> OnTrainResultPushRequest { get; private set; }

        public TrainResult trainResult = new();

        // 최초 생성 함수. 파티정보 + 시나리오에 배치된 전체 갯수를 TrainResultData형태로 입력
        public void SetTrainResultInit(Party party, TrainResultData totaldata)
        {
            trainResult.partyId = party.PartyId;
            trainResult.trainingType = party.PartyInfo;
            trainResult.playTime = 0;
            trainResult.SetResultData(totaldata);
            foreach (var role in party.AccountIdAndRole)
            {
                trainResult.SetResultData(role.Value);
            }
        }

        public void SetTrainResultInit(int partyid, string trainingType, TrainResultData totaldata)
        {
            trainResult.partyId = partyid;
            trainResult.trainingType = trainingType;
            trainResult.playTime = 0;
            trainResult.SetResultData(totaldata);

            //foreach (var role in party.AccountIdAndRole)
            //{
            //    trainResult.SetResultData(role.Value);
            //}
        }

        // Role 기반 이벤트 발생시 내용 추가 가능 (수행 카운트 업데이트)
        public void SetTrainResultTrainee(string role, string keydata, int addValue)
        {
            trainResult.SetResultData(role, keydata, addValue);
        }

        // base type
        public void SetTrainResult(TrainResultData data) => trainResult.SetResultData(data);



        private void InitTrainResultPushDelegate()
        {
            OnTrainResultPushRequest = HandleTrainResultPushRequest;
            connectionHandler.OnTrainResultPushRequest += HandleTrainResultPushRequest;
        }

        private async void HandleTrainResultPushRequest(GameInstancePlugin.TrainResult data)
        {
            await UniTask.WaitForEndOfFrame(); // 나중에 리플레이 파일 비동기 처리 부분으로 변경
            TrainResultPushRequest(trainResult);
        }

        private void TrainResultPushRequest(TrainResult data)
        {
            if (logicBridge == null)
            {
                SendClientLog(2, "[DedicateServerHandler] TrainResultPush 전송 불가: logicBridge가 null");
                return;
            }

            SendClientLog(0, $"[DedicateServerHandler] TrainResultPush 요청 수신 → 로직 서버 전송: 파티={data?.partyId}");
            logicBridge.SendTrainResultPush(data);
        }

        #endregion

        #region GracefulQuit (서버→데디케이트 종료 지시)

        /// <summary>
        /// 로직 서버가 훈련생 전원 ACK 확인 후 전송하는 종료 지시.
        /// FishNet 세션을 정리하고 프로세스를 종료합니다.
        /// </summary>
        private void OnGracefulQuitReceived(int partyId)
        {
            SendClientLog(0, $"[DedicateServerHandler] GracefulQuit 수신: 파티={partyId}. 종료 처리 시작.");

            InitiateGracefulShutdown("GracefulQuit — 훈련생 전원 확인 완료");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            UniTask.Delay(TimeSpan.FromSeconds(2f))
                .ContinueWith(() => Application.Quit())
                .Forget();
#endif
        }

        #endregion

        #region TrainingExits (서버→데디케이트 훈련 종료 지시)

        private void OnTrainingExitsReceived()
        {
            SendClientLog(0, "[DedicateServerHandler] TrainingExits 수신 — 훈련 종료 처리 시작.");
            InitiateGracefulShutdown("TrainingExits — 훈련 종료 지시");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            UniTask.Delay(TimeSpan.FromSeconds(2f))
                .ContinueWith(() => Application.Quit())
                .Forget();
#endif
        }

        #endregion

        #region NPC Spawn/Despawn (DedicatedServerNPCSpawner 병합)

        /// <summary>외부에서 수동으로 NPC 스폰을 트리거합니다.</summary>
        public void TriggerSpawn()
        {
            if (!hasSpawnedNPCs) SpawnNPCs();
        }

        /// <summary>외부에서 수동으로 NPC 디스폰을 트리거합니다.</summary>
        public void TriggerDespawn()
        {
            DespawnNPCs();
        }

        private void SpawnNPCs()
        {
            if (hasSpawnedNPCs) return;
            hasSpawnedNPCs = true;

            if (OnSpawnNPCsOverride != null)
            {
                SendClientLog(0, "[DedicateServerHandler] NPC 스폰 훅 호출");
                OnSpawnNPCsOverride.Invoke(fishnetManager);
            }
        }

        private void DespawnNPCs()
        {
            if (!hasSpawnedNPCs) return;

            if (OnDespawnNPCsOverride != null)
            {
                SendClientLog(0, "[DedicateServerHandler] NPC 디스폰 훅 호출");
                OnDespawnNPCsOverride.Invoke();
            }

            hasSpawnedNPCs = false;
        }
        #endregion

        #region Helper Methods

        /// <summary>
        /// 서버의 중앙 콘솔로 로그 메시지를 전송합니다 (MessageTags.ClientLog)
        /// </summary>
        /// <param name="logLevel">0: Info, 1: Warning, 2: Error</param>
        /// <param name="logMessage">콘솔에 출력할 메시지 내용</param>
        public void SendClientLog(byte logLevel, string logMessage)
        {
            if (logicBridge != null)
            {
                logicBridge.SendClientLog(logLevel, logMessage);
            }
            else
            {
                if (logLevel == 0) Debug.Log(logMessage);
                else if (logLevel == 1) Debug.LogWarning(logMessage);
                else if (logLevel == 2) Debug.LogError(logMessage);
            }
        }

        #endregion

        #region Public Properties

        public bool IsFishnetServerRunning => isFishnetServerRunning;
        public bool IsGameplayActive => isGameplayActive;
        public NetworkManager FishnetManager => fishnetManager;

        #endregion
    }
}