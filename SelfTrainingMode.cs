using System;
using System.Collections.Generic;
using UnityEngine;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Connection;
using Cysharp.Threading.Tasks;

namespace SelfTraining
{
    /// <summary>
    /// 혼자서 훈련을 진행할 수 있는 Self Training Mode
    /// - 서버 연결 없이 로컬에서 Host 모드로 동작
    /// - 개인용 파티 설정 데이터 생성
    /// - Fishnet Host (Server + Client) 동시 실행
    /// </summary>
    public class SelfTrainingMode : MonoBehaviour
    {
        #region Singleton
        private static SelfTrainingMode _instance;
        public static SelfTrainingMode Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<SelfTrainingMode>();
                }
                return _instance;
            }
        }
        #endregion

        #region Serialized Fields
        [Header("Fishnet 설정")]
        [SerializeField] private NetworkManager fishnetManager;
        [SerializeField] private Transport fishnetTransport;
        [SerializeField] private ushort hostPort = 7777;

        [Header("기본 훈련 설정")]
        [SerializeField] private string defaultSceneFile = "TrainingScene";
        [SerializeField] private string defaultPlayerId = "SelfTrainingPlayer";
        [SerializeField] private string defaultPlayerName = "훈련생";

        [Header("디버그")]
        [SerializeField] private bool autoStartOnAwake = false;
        [SerializeField] private bool enableDebugLogs = true;
        #endregion

        #region Private Fields
        private bool isInitialized = false;
        private bool isHostRunning = false;
        private bool isTrainingActive = false;
        private SelfTrainingData currentTrainingData;
        #endregion

        #region Events
        /// <summary>Host 모드 시작됨</summary>
        public event Action OnHostStarted;
        
        /// <summary>Host 모드 중지됨</summary>
        public event Action OnHostStopped;
        
        /// <summary>훈련 시작됨</summary>
        public event Action<SelfTrainingData> OnTrainingStarted;
        
        /// <summary>훈련 종료됨</summary>
        public event Action OnTrainingEnded;
        
        /// <summary>클라이언트 연결됨 (Host 모드에서 자신)</summary>
        public event Action<NetworkConnection> OnClientConnected;
        
        /// <summary>오류 발생</summary>
        public event Action<string> OnError;
        #endregion

        #region Data Classes
        /// <summary>Self Training 데이터 (파티/훈련 설정)</summary>
        [Serializable]
        public class SelfTrainingData
        {
            public int PartyId;
            public int InstanceId;
            public string SceneFile;
            public string PlayerId;
            public string PlayerName;
            public List<SelfTrainingMember> Members;
            public SelfTrainingSettings Settings;
            public DateTime StartTime;

            public SelfTrainingData()
            {
                PartyId = 1;
                InstanceId = 1;
                SceneFile = string.Empty;
                PlayerId = string.Empty;
                PlayerName = string.Empty;
                Members = new List<SelfTrainingMember>();
                Settings = new SelfTrainingSettings();
                StartTime = DateTime.Now;
            }

            public override string ToString()
            {
                return $"SelfTraining[Party:{PartyId}, Player:{PlayerName}, Scene:{SceneFile}, Members:{Members.Count}]";
            }
        }

        /// <summary>Self Training 멤버 정보</summary>
        [Serializable]
        public class SelfTrainingMember
        {
            public string MemberId;
            public string MemberName;
            public bool IsHost;
            public bool IsConnected;

            public SelfTrainingMember(string id, string name, bool isHost = false)
            {
                MemberId = id;
                MemberName = name;
                IsHost = isHost;
                IsConnected = false;
            }
        }

        /// <summary>Self Training 추가 설정</summary>
        [Serializable]
        public class SelfTrainingSettings
        {
            public int ScenarioType;
            public int TimeOfDay;
            public int WeatherType;
            public string ScenarioDescription;
            public Dictionary<string, object> CustomData;

            public SelfTrainingSettings()
            {
                ScenarioType = 0;
                TimeOfDay = 12;
                WeatherType = 0;
                ScenarioDescription = string.Empty;
                CustomData = new Dictionary<string, object>();
            }
        }
        #endregion

        #region Unity Lifecycle
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            Initialize();

            if (autoStartOnAwake)
            {
                StartSelfTraining();
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                StopSelfTraining();
                _instance = null;
            }
        }
        #endregion

        #region Initialization
        /// <summary>SelfTrainingMode를 초기화합니다.</summary>
        public void Initialize()
        {
            if (isInitialized)
            {
                LogDebug("이미 초기화되었습니다");
                return;
            }

            // Fishnet NetworkManager 찾기
            if (fishnetManager == null)
            {
                fishnetManager = FindFirstObjectByType<NetworkManager>();
                if (fishnetManager == null)
                {
                    LogError("NetworkManager를 찾을 수 없습니다! 씬에 Fishnet NetworkManager를 추가해주세요.");
                    return;
                }
            }

            // Transport 찾기
            if (fishnetTransport == null && fishnetManager != null)
            {
                fishnetTransport = fishnetManager.TransportManager.Transport;
            }

            // Fishnet 이벤트 등록
            if (fishnetManager != null)
            {
                fishnetManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
                fishnetManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
                fishnetManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            }

            isInitialized = true;
            LogDebug("SelfTrainingMode 초기화 완료");
        }
        #endregion

        #region Public Methods - Training Control
        /// <summary>
        /// Self Training을 시작합니다 (기본 설정 사용).
        /// </summary>
        public void StartSelfTraining()
        {
            StartSelfTraining(null);
        }

        /// <summary>
        /// Self Training을 시작합니다.
        /// </summary>
        /// <param name="customData">커스텀 훈련 데이터 (null이면 기본값 사용)</param>
        public void StartSelfTraining(SelfTrainingData customData)
        {
            if (!isInitialized)
            {
                LogError("초기화되지 않았습니다. Initialize()를 먼저 호출하세요.");
                OnError?.Invoke("SelfTrainingMode가 초기화되지 않았습니다");
                return;
            }

            if (isHostRunning)
            {
                LogWarning("이미 Host가 실행 중입니다");
                return;
            }

            // 훈련 데이터 생성
            currentTrainingData = customData ?? CreateDefaultTrainingData();

            LogDebug($"Self Training 시작: {currentTrainingData}");

            // Host 모드 시작
            StartHostAsync().Forget();
        }

        /// <summary>
        /// Self Training을 중지합니다.
        /// </summary>
        public void StopSelfTraining()
        {
            if (!isHostRunning)
            {
                LogDebug("Host가 실행 중이 아닙니다");
                return;
            }

            LogDebug("Self Training 중지 중...");

            // 훈련 종료 이벤트
            if (isTrainingActive)
            {
                isTrainingActive = false;
                OnTrainingEnded?.Invoke();
            }

            // Host 중지
            StopHost();

            currentTrainingData = null;
        }

        /// <summary>
        /// 훈련을 일시정지합니다.
        /// </summary>
        public void PauseTraining()
        {
            if (!isTrainingActive)
            {
                LogWarning("훈련이 활성화되지 않았습니다");
                return;
            }

            LogDebug("훈련 일시정지");
            Time.timeScale = 0f;
        }

        /// <summary>
        /// 훈련을 재개합니다.
        /// </summary>
        public void ResumeTraining()
        {
            if (!isTrainingActive)
            {
                LogWarning("훈련이 활성화되지 않았습니다");
                return;
            }

            LogDebug("훈련 재개");
            Time.timeScale = 1f;
        }
        #endregion

        #region Public Methods - Configuration
        /// <summary>
        /// 훈련 설정을 생성합니다.
        /// </summary>
        public SelfTrainingData CreateTrainingData(
            string sceneFile,
            string playerId,
            string playerName,
            int scenarioType = 0,
            int timeOfDay = 12,
            int weatherType = 0)
        {
            var data = new SelfTrainingData
            {
                PartyId = GeneratePartyId(),
                InstanceId = GenerateInstanceId(),
                SceneFile = sceneFile,
                PlayerId = playerId,
                PlayerName = playerName,
                StartTime = DateTime.Now
            };

            // Host 멤버 추가
            data.Members.Add(new SelfTrainingMember(playerId, playerName, true));

            // 설정 적용
            data.Settings.ScenarioType = scenarioType;
            data.Settings.TimeOfDay = timeOfDay;
            data.Settings.WeatherType = weatherType;

            return data;
        }

        /// <summary>
        /// AI 또는 더미 멤버를 추가합니다 (향후 AI 훈련생 지원용).
        /// </summary>
        public void AddAIMember(string memberId, string memberName)
        {
            if (currentTrainingData == null)
            {
                LogWarning("훈련 데이터가 없습니다. StartSelfTraining을 먼저 호출하세요.");
                return;
            }

            var aiMember = new SelfTrainingMember(memberId, memberName, false);
            currentTrainingData.Members.Add(aiMember);
            LogDebug($"AI 멤버 추가됨: {memberName}");
        }

        /// <summary>
        /// Host 포트를 설정합니다 (시작 전에 호출).
        /// </summary>
        public void SetHostPort(ushort port)
        {
            if (isHostRunning)
            {
                LogWarning("Host 실행 중에는 포트를 변경할 수 없습니다");
                return;
            }
            hostPort = port;
        }
        #endregion

        #region Host Management
        /// <summary>Host 모드를 시작합니다 (비동기).</summary>
        private async UniTaskVoid StartHostAsync()
        {
            try
            {
                // Transport 포트 설정
                SetTransportPort(fishnetTransport, hostPort);

                // 서버 시작
                LogDebug($"Fishnet Server 시작 중... (포트: {hostPort})");
                fishnetManager.ServerManager.StartConnection();

                // 서버 시작 대기
                await UniTask.WaitUntil(() => fishnetManager.ServerManager.Started, 
                    cancellationToken: this.GetCancellationTokenOnDestroy());

                LogDebug("Fishnet Server 시작됨, Client 연결 중...");

                // 클라이언트 연결 (Host 모드)
                SetTransportClientAddress(fishnetTransport, "127.0.0.1", hostPort);
                fishnetManager.ClientManager.StartConnection();

                // 클라이언트 연결 대기
                await UniTask.WaitUntil(() => fishnetManager.ClientManager.Started,
                    cancellationToken: this.GetCancellationTokenOnDestroy());

                isHostRunning = true;
                LogDebug("Host 모드 시작 완료!");

                OnHostStarted?.Invoke();

                // 훈련 활성화
                await UniTask.Delay(TimeSpan.FromMilliseconds(500), 
                    cancellationToken: this.GetCancellationTokenOnDestroy());

                ActivateTraining();
            }
            catch (OperationCanceledException)
            {
                LogDebug("Host 시작이 취소되었습니다");
            }
            catch (Exception ex)
            {
                LogError($"Host 시작 실패: {ex.Message}");
                OnError?.Invoke($"Host 시작 실패: {ex.Message}");
            }
        }

        /// <summary>Host를 중지합니다.</summary>
        private void StopHost()
        {
            try
            {
                // 클라이언트 먼저 중지
                if (fishnetManager.ClientManager.Started)
                {
                    fishnetManager.ClientManager.StopConnection();
                }

                // 서버 중지
                if (fishnetManager.ServerManager.Started)
                {
                    fishnetManager.ServerManager.StopConnection(true);
                }

                isHostRunning = false;
                LogDebug("Host 모드 중지됨");

                OnHostStopped?.Invoke();
            }
            catch (Exception ex)
            {
                LogError($"Host 중지 오류: {ex.Message}");
            }
        }

        /// <summary>훈련을 활성화합니다.</summary>
        private void ActivateTraining()
        {
            if (currentTrainingData == null)
            {
                LogError("훈련 데이터가 없습니다");
                return;
            }

            isTrainingActive = true;
            currentTrainingData.StartTime = DateTime.Now;

            // Host 멤버 연결 상태 업데이트
            foreach (var member in currentTrainingData.Members)
            {
                if (member.IsHost)
                {
                    member.IsConnected = true;
                }
            }

            LogDebug($"훈련 활성화됨: {currentTrainingData}");
            OnTrainingStarted?.Invoke(currentTrainingData);
        }
        #endregion

        #region Transport Configuration
        /// <summary>Transport 포트를 설정합니다.</summary>
        private void SetTransportPort(Transport transport, int port)
        {
            if (transport == null)
            {
                LogWarning("Transport가 null입니다");
                return;
            }

            // Tugboat
            if (transport is FishNet.Transporting.Tugboat.Tugboat tugboat)
            {
                tugboat.SetPort((ushort)port);
                LogDebug($"Tugboat 포트 설정됨: {port}");
                return;
            }

            // 리플렉션으로 처리
            try
            {
                var type = transport.GetType();
                var portProperty = type.GetProperty("Port");
                var portField = type.GetField("_port", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (portProperty != null && portProperty.CanWrite)
                    portProperty.SetValue(transport, (ushort)port);
                else if (portField != null)
                    portField.SetValue(transport, (ushort)port);

                LogDebug($"Transport 포트 설정됨: {port}");
            }
            catch (Exception ex)
            {
                LogError($"Transport 포트 설정 오류: {ex.Message}");
            }
        }

        /// <summary>Transport 클라이언트 주소를 설정합니다.</summary>
        private void SetTransportClientAddress(Transport transport, string address, int port)
        {
            if (transport == null)
            {
                LogWarning("Transport가 null입니다");
                return;
            }

            // Tugboat
            if (transport is FishNet.Transporting.Tugboat.Tugboat tugboat)
            {
                tugboat.SetClientAddress(address);
                tugboat.SetPort((ushort)port);
                LogDebug($"Tugboat 클라이언트 설정됨: {address}:{port}");
                return;
            }

            // 리플렉션으로 처리
            try
            {
                var type = transport.GetType();

                // 주소 설정
                var addressProperty = type.GetProperty("ClientAddress");
                var addressField = type.GetField("_clientAddress", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (addressProperty != null && addressProperty.CanWrite)
                    addressProperty.SetValue(transport, address);
                else if (addressField != null)
                    addressField.SetValue(transport, address);

                // 포트 설정
                var portProperty = type.GetProperty("Port");
                var portField = type.GetField("_port", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (portProperty != null && portProperty.CanWrite)
                    portProperty.SetValue(transport, (ushort)port);
                else if (portField != null)
                    portField.SetValue(transport, (ushort)port);

                LogDebug($"Transport 클라이언트 설정됨: {address}:{port}");
            }
            catch (Exception ex)
            {
                LogError($"Transport 클라이언트 설정 오류: {ex.Message}");
            }
        }
        #endregion

        #region Fishnet Event Handlers
        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            switch (args.ConnectionState)
            {
                case LocalConnectionState.Started:
                    LogDebug("Fishnet Server 시작됨");
                    break;
                case LocalConnectionState.Stopped:
                    LogDebug("Fishnet Server 중지됨");
                    break;
                case LocalConnectionState.Starting:
                    LogDebug("Fishnet Server 시작 중...");
                    break;
                case LocalConnectionState.Stopping:
                    LogDebug("Fishnet Server 중지 중...");
                    break;
            }
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            switch (args.ConnectionState)
            {
                case LocalConnectionState.Started:
                    LogDebug("Fishnet Client 연결됨 (Host 모드)");
                    break;
                case LocalConnectionState.Stopped:
                    LogDebug("Fishnet Client 연결 해제됨");
                    break;
                case LocalConnectionState.Starting:
                    LogDebug("Fishnet Client 연결 중...");
                    break;
                case LocalConnectionState.Stopping:
                    LogDebug("Fishnet Client 연결 해제 중...");
                    break;
            }
        }

        private void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == RemoteConnectionState.Started)
            {
                LogDebug($"클라이언트 연결됨: ConnectionId={conn.ClientId}");
                OnClientConnected?.Invoke(conn);
            }
            else if (args.ConnectionState == RemoteConnectionState.Stopped)
            {
                LogDebug($"클라이언트 연결 해제됨: ConnectionId={conn.ClientId}");
            }
        }
        #endregion

        #region Helper Methods
        /// <summary>기본 훈련 데이터를 생성합니다.</summary>
        private SelfTrainingData CreateDefaultTrainingData()
        {
            return CreateTrainingData(
                defaultSceneFile,
                defaultPlayerId,
                defaultPlayerName
            );
        }

        /// <summary>파티 ID를 생성합니다.</summary>
        private int GeneratePartyId()
        {
            // Self Training은 항상 음수 또는 특정 범위의 ID 사용 (서버 파티와 구분)
            return -UnityEngine.Random.Range(1000, 9999);
        }

        /// <summary>인스턴스 ID를 생성합니다.</summary>
        private int GenerateInstanceId()
        {
            return -UnityEngine.Random.Range(1000, 9999);
        }

        private void LogDebug(string message)
        {
            if (enableDebugLogs)
            {
                Debug.Log($"[SelfTrainingMode] {message}");
            }
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[SelfTrainingMode] {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[SelfTrainingMode] {message}");
        }
        #endregion

        #region Public Properties
        /// <summary>초기화 여부</summary>
        public bool IsInitialized => isInitialized;

        /// <summary>Host 실행 여부</summary>
        public bool IsHostRunning => isHostRunning;

        /// <summary>훈련 활성화 여부</summary>
        public bool IsTrainingActive => isTrainingActive;

        /// <summary>현재 훈련 데이터</summary>
        public SelfTrainingData CurrentTrainingData => currentTrainingData;

        /// <summary>Fishnet NetworkManager</summary>
        public NetworkManager FishnetManager => fishnetManager;

        /// <summary>Host 포트</summary>
        public ushort HostPort => hostPort;

        /// <summary>Self Training 모드 여부 (서버 연결 없이 로컬)</summary>
        public bool IsSelfTrainingMode => isHostRunning && currentTrainingData != null && currentTrainingData.PartyId < 0;
        #endregion
    }
}
