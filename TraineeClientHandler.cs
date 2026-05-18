using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Broadcast;
using GameInstancePlugin;
using GameInstancePlugin.Client;
using Disaster.Network.FTP;
using PartyManagerPlugin;

namespace DatabasePlugin.Client
{
    /// <summary>
    /// Trainee 측 오케스트레이터: TraineeClient(DarkRift) ↔ FishNet/FTP 브릿지
    ///
    /// 프로세스 플로우:
    /// 1. PartyConfirm(220) → FTP 다운로드 시작 (SceneSetFile)
    /// 2. TrainingCreated(229) → FTP 대기 → 씬 생성(stub) → FishNet 접속
    /// 3. FishNet 접속 →  SendTrainingCreated() 로 피시넷서버에 준비완료 (외부에서 호출 해줘야함)
    /// 4. TrainingBegin(224) → 훈련 시작 이벤트
    /// 5. TrainingStop → FishNet 연결 해제
    /// </summary>
    public class TraineeClientHandler : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TraineeClient traineeClient;
        [SerializeField] private FtpTransferManager ftpManager;
        [SerializeField] private NetworkManager fishnetManager;
        [SerializeField] private Transport fishnetTransport;

        private string sceneSetJson = string.Empty;
        public string SceneSetJson
        {
            get { return sceneSetJson; }
        }
        // 상태
        private bool isFtpDownloadComplete;
        private bool isConnectedToGameServer;
        private CancellationTokenSource lifecycleCts;

        /// <summary>훈련 시작됨 이벤트</summary>
        public event Action OnTrainingStarted;
        /// <summary>게임 서버 연결 해제됨</summary>
        public event Action OnDisconnectedFromGameServer;
        /// <summary>에러 이벤트</summary>
        public event Action<string> OnError;

        #region Unity Lifecycle

        private void Awake()
        {
            if (traineeClient == null) traineeClient = FindFirstObjectByType<TraineeClient>();
            if (fishnetManager == null) fishnetManager = FindFirstObjectByType<NetworkManager>();
            if (fishnetManager != null && fishnetTransport == null)
                fishnetTransport = fishnetManager.TransportManager.Transport;
            lifecycleCts = new CancellationTokenSource();
            if (ftpManager == null) ftpManager = FindFirstObjectByType<FtpTransferManager>();

        }

        private void OnEnable()
        {
            if (traineeClient != null)
            {
                traineeClient.OnPartyConfirmReceived += OnPartyConfirmed;
                traineeClient.OnTrainingCreatedReceived += OnTrainingCreated;
                traineeClient.OnTrainingBeginReceived += OnTrainingBegin;
                traineeClient.OnTrainingStopReceived += OnTrainingStop;
                traineeClient.OnPingProbeReceived += OnPingProbeReceived;
            }

            if (fishnetManager != null)
            {
                fishnetManager.ClientManager.OnClientConnectionState += OnFishnetConnectionState;
                fishnetManager.ClientManager.RegisterBroadcast<SessionStartBroadcast>(OnSessionStartBroadcast);
                fishnetManager.ClientManager.RegisterBroadcast<SessionEndBroadcast>(OnSessionEndBroadcast);
            }
        }

        private void OnDisable()
        {
            if (traineeClient != null)
            {
                traineeClient.OnPartyConfirmReceived -= OnPartyConfirmed;
                traineeClient.OnTrainingCreatedReceived -= OnTrainingCreated;
                traineeClient.OnTrainingBeginReceived -= OnTrainingBegin;
                traineeClient.OnTrainingStopReceived -= OnTrainingStop;
                traineeClient.OnPingProbeReceived -= OnPingProbeReceived;
            }

            if (fishnetManager != null)
            {
                fishnetManager.ClientManager.OnClientConnectionState -= OnFishnetConnectionState;
                fishnetManager.ClientManager.UnregisterBroadcast<SessionStartBroadcast>(OnSessionStartBroadcast);
                fishnetManager.ClientManager.UnregisterBroadcast<SessionEndBroadcast>(OnSessionEndBroadcast);
            }

            lifecycleCts?.Cancel();
            lifecycleCts?.Dispose();
            lifecycleCts = null;
        }

        #endregion

        #region Step 1: PartyConfirm → FTP 다운로드

        private void OnPartyConfirmed(Party party)
        {
            Debug.Log($"[TraineeClientHandler] 파티 확정 수신: PartyId={party.PartyId}");

            //SceneSetFile FTP 다운로드 시작
            // TODO: Party 클래스에서 SceneSetFile 필드 확인 후 활성화
             string sceneSetFile = party.SceneSetFile;
            if (!string.IsNullOrEmpty(sceneSetFile))
            {
                isFtpDownloadComplete = false;
                DownloadSceneSetFileAsync(sceneSetFile, lifecycleCts.Token).Forget();
            }
            else
            {
                isFtpDownloadComplete = true;
            }
        }

        #endregion

        #region Step 4: TrainingCreated → FTP 대기 → 씬 생성 → FishNet 접속

        private void OnTrainingCreated(TrainingCreatedData data)
        {
            Debug.Log($"[TraineeClientHandler] TrainingCreated 수신: {data.ServerAddress}:{data.ServerPort}");
            HandleTrainingCreatedAsync(data, lifecycleCts.Token).Forget();
        }

        private async UniTaskVoid HandleTrainingCreatedAsync(TrainingCreatedData data, CancellationToken ct)
        {
            try
            {
                // FTP 다운로드 완료 대기
                if (!isFtpDownloadComplete)
                {
                    Debug.Log("[TraineeClientHandler] FTP 다운로드 완료 대기 중...");
                    await UniTask.WaitUntil(() => isFtpDownloadComplete, cancellationToken: ct);
                }

                // 씬 생성 (비동기 stub)
                await CreateSceneAsync(data.SceneSetFile, ct);

                // FishNet 게임 서버에 접속
                ConnectToGameServer(data.ServerAddress, data.ServerPort);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[TraineeClientHandler] TrainingCreated 처리 취소됨");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TraineeClientHandler] TrainingCreated 처리 오류: {ex.Message}");
                OnError?.Invoke($"TrainingCreated 처리 실패: {ex.Message}");
            }
        }

        #endregion

        #region Step 5: FishNet 접속 → AuthBroadcast + SendTrainingCreated

        private void OnFishnetConnectionState(ClientConnectionStateArgs args)
        {
            switch (args.ConnectionState)
            {
                case LocalConnectionState.Started:
                    isConnectedToGameServer = true;
                    Debug.Log("[TraineeClientHandler] FishNet 게임 서버 연결됨");

                    // TraineeClient에 연결 상태 알림
                    traineeClient.NotifyGameServerConnectionChanged(true);

                    // AuthBroadcast 전송
                    SendAuthBroadcast();
                    break;

                case LocalConnectionState.Stopped:
                    isConnectedToGameServer = false;
                    Debug.Log("[TraineeClientHandler] FishNet 게임 서버 연결 끊김");

                    traineeClient.NotifyGameServerConnectionChanged(false);
                    OnDisconnectedFromGameServer?.Invoke();
                    break;

                case LocalConnectionState.Starting:
                    Debug.Log("[TraineeClientHandler] FishNet 게임 서버 연결 중...");
                    break;

                case LocalConnectionState.Stopping:
                    Debug.Log("[TraineeClientHandler] FishNet 게임 서버 연결 종료 중...");
                    break;
            }
        }

        private void SendAuthBroadcast()
        {
            if (traineeClient.CurrentAccount == null || traineeClient.CurrentParty == null)
            {
                Debug.LogWarning("[TraineeClientHandler] AuthBroadcast 전송 불가: 계정 또는 파티 정보 없음");
                return;
            }

            var authData = new AuthBroadcastData
            {
                PlayerId = traineeClient.CurrentAccount.Id.GetHashCode(),
                PartyId = traineeClient.CurrentParty.PartyId,
                Role = traineeClient.CurrentAccount.Id
            };

            fishnetManager.ClientManager.Broadcast(authData);
            Debug.Log($"[TraineeClientHandler] AuthBroadcast 전송: PlayerId={authData.PlayerId}, PartyId={authData.PartyId}");
        }

        /// <summary>
        /// 외부에서 준비 완료 시 호출.
        /// FishNet PlayerReadyBroadcast → 서버 카운트 증가 → BeginReady 판정
        /// </summary>
        public void SendTrainingCreated()
        {
            var readyData = new PlayerReadyBroadcast
            {
                PlayerId = traineeClient.CurrentAccount != null
                    ? traineeClient.CurrentAccount.Id.GetHashCode() : 0
            };
            fishnetManager.ClientManager.Broadcast(readyData);
            Debug.Log("[TraineeClientHandler] PlayerReadyBroadcast 전송 (준비 완료)");
        }

        #endregion

        #region Step 8: TrainingBegin → 시작 이벤트

        private void OnTrainingBegin(int instanceId, int partyId)
        {
            Debug.Log($"[TraineeClientHandler] 훈련 시작: 인스턴스={instanceId}, 파티={partyId}");
            OnTrainingStarted?.Invoke();
        }

        #endregion

        #region Session Broadcasts (서버 → 클라이언트)

        private void OnSessionStartBroadcast(SessionStartBroadcast broadcast, FishNet.Transporting.Channel channel)
        {
            Debug.Log("[TraineeClientHandler] 세션 시작 브로드캐스트 수신! 게임 시작!");
        }

        private void OnSessionEndBroadcast(SessionEndBroadcast broadcast, FishNet.Transporting.Channel channel)
        {
            Debug.Log($"[TraineeClientHandler] 세션 종료 브로드캐스트 수신: {broadcast.Message}");

            ClientSessionUI ui = FindFirstObjectByType<ClientSessionUI>();
            if (ui != null)
            {
                ui.ShowEndScreenAndReturn(broadcast.Message);
            }
        }

        #endregion

        #region TrainingStop → 연결 해제

        private void OnTrainingStop(int instanceId, int partyId)
        {
            Debug.Log($"[TraineeClientHandler] 훈련 종료: 인스턴스={instanceId}, 파티={partyId}");
            DisconnectFromGameServer();
        }

        #endregion

        #region FishNet 연결 (TraineeClient에서 이동)

        public void ConnectToGameServer(string address, int port)
        {
            if (fishnetManager == null)
            {
                Debug.LogError("[TraineeClientHandler] NetworkManager가 할당되지 않았습니다");
                OnError?.Invoke("NetworkManager를 찾을 수 없습니다");
                return;
            }

            if (isConnectedToGameServer)
            {
                Debug.LogWarning("[TraineeClientHandler] 이미 게임 서버에 연결되어 있습니다");
                return;
            }

            try
            {
                SetTransportAddress(fishnetTransport, address, port);
                fishnetManager.ClientManager.StartConnection();
                Debug.Log($"[TraineeClientHandler] 게임 서버 연결 중: {address}:{port}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TraineeClientHandler] 게임 서버 연결 오류: {ex.Message}");
                OnError?.Invoke($"게임 서버 연결 실패: {ex.Message}");
            }
        }

        public void DisconnectFromGameServer()
        {
            if (fishnetManager == null || !isConnectedToGameServer)
                return;

            try
            {
                fishnetManager.ClientManager.StopConnection();
                isConnectedToGameServer = false;
                Debug.Log("[TraineeClientHandler] 게임 서버 연결 종료됨");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TraineeClientHandler] 게임 서버 연결 종료 오류: {ex.Message}");
            }
        }

        private void SetTransportAddress(Transport transport, string address, int port)
        {
            // Tugboat (기본 Transport)
            if (transport is FishNet.Transporting.Tugboat.Tugboat tugboat)
            {
                tugboat.SetClientAddress(address);
                tugboat.SetPort((ushort)port);
                Debug.Log($"[TraineeClientHandler] Tugboat 설정: {address}:{port}");
                return;
            }

#if !UNITY_WEBGL || UNITY_EDITOR
            // Bayou (WebGL Transport)
            var bayouType = transport.GetType();
            if (bayouType.Name == "Bayou")
            {
                var addressField = bayouType.GetField("_clientAddress", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var portField = bayouType.GetField("_port", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (addressField != null) addressField.SetValue(transport, address);
                if (portField != null) portField.SetValue(transport, (ushort)port);

                Debug.Log($"[TraineeClientHandler] Bayou 설정: {address}:{port}");
                return;
            }
#endif

            // 기타 Transport — 리플렉션
            try
            {
                var type = transport.GetType();

                var addressProperty = type.GetProperty("ClientAddress");
                var addressField2 = type.GetField("_clientAddress", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (addressProperty != null && addressProperty.CanWrite)
                    addressProperty.SetValue(transport, address);
                else if (addressField2 != null)
                    addressField2.SetValue(transport, address);

                var portProperty = type.GetProperty("Port");
                var portField2 = type.GetField("_port", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (portProperty != null && portProperty.CanWrite)
                    portProperty.SetValue(transport, (ushort)port);
                else if (portField2 != null)
                    portField2.SetValue(transport, (ushort)port);

                Debug.Log($"[TraineeClientHandler] Transport 설정: {address}:{port}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TraineeClientHandler] Transport 설정 오류: {ex.Message}");
            }
        }

        #endregion

        #region FTP 다운로드

        private async UniTask DownloadSceneSetFileAsync(string sceneSetFile, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(sceneSetFile) || ftpManager == null)
            {
                isFtpDownloadComplete = true;
                return;
            }

            try
            {
                string localPath = Path.Combine(Application.persistentDataPath, "SceneSet", sceneSetFile);

                string dir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                Debug.Log($"[TraineeClientHandler] FTP 다운로드 시작: {sceneSetFile}");
                sceneSetJson = await ftpManager.DownloadStringAsync(FtpContentType.ConfirmScenario, sceneSetFile);
                isFtpDownloadComplete = true;
                if (string.IsNullOrEmpty(sceneSetJson))
                {
                    Debug.LogWarning($"[TraineeClientHandler] FTP 다운로드 실패 (파일 없음): {sceneSetFile}");
                    OnError?.Invoke($"SceneSetFile 다운로드 실패: 서버에 파일이 없습니다 ({sceneSetFile})");
                }
                else
                {
                    Debug.Log($"[TraineeClientHandler] FTP 다운로드 완료: {sceneSetFile}");
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[TraineeClientHandler] FTP 다운로드 취소됨");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TraineeClientHandler] FTP 다운로드 실패: {ex.Message}");
                OnError?.Invoke($"SceneSetFile 다운로드 실패: {ex.Message}");
                isFtpDownloadComplete = true; // 실패해도 플로우 진행 허용
            }
        }

        #endregion

        #region 씬 생성 (비동기 stub)

        /// <summary>
        /// 다운로드된 SceneSetFile 기반 씬 생성.
        /// TODO: 실제 씬 생성 로직 구현
        /// </summary>
        private async UniTask CreateSceneAsync(string sceneSetFile, CancellationToken ct)
        {
            Debug.Log($"[TraineeClientHandler] 씬 생성 시작 (stub): {sceneSetFile}");
            await UniTask.CompletedTask;
            Debug.Log($"[TraineeClientHandler] 씬 생성 완료 (stub)");
        }

        #endregion

        #region Ping

        private void OnPingProbeReceived(long sentMs)
        {
            traineeClient?.SendPingProbeAck(sentMs, 0);
        }

        #endregion

        #region Public Properties

        public bool IsConnectedToGameServer => isConnectedToGameServer;
        public bool IsFtpDownloadComplete => isFtpDownloadComplete;

        #endregion
    }
}
