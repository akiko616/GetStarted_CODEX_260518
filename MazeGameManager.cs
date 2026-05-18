using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Connection;
using Channel = FishNet.Transporting.Channel;
using FishNet.Object;
using FishNet.Broadcast;
using GameInstancePlugin;
using DatabasePlugin.Client;

namespace GameInstancePlugin.Client.MazeGame
{
    /// <summary>
    /// 미로 시드 동기화용 Broadcast 구조체
    /// </summary>
    public struct MazeSeedBroadcast : IBroadcast
    {
        public int Seed;
    }

    /// <summary>
    /// 게임 일시정지/재개 동기화용 Broadcast 구조체
    /// </summary>
    public struct GamePauseBroadcast : IBroadcast
    {
        public bool IsPaused;
    }

    /// <summary>
    /// 미로 게임 씬의 부트스트랩 매니저.
    ///
    /// 프로덕션 플로우:
    ///   1. GameInstanceClient가 DarkRift 로직서버에 접속
    ///   2. TrainingWakeResult에서 씬 정보(미로 시드) 수신
    ///   3. GameInstanceClient가 FishNet 서버 시작
    ///   4. MazeGameManager가 미로 생성 + 플레이어 스폰 처리
    ///
    /// SessionConnectionHandler.OnSpawnPlayerOverride를 사용하여
    /// 기존 세션 시스템의 인증 + 스폰 흐름에 통합됩니다.   => 기본 스포너에 델리게이트 후킹 샘플
    ///
    /// 독립 테스트:
    ///   GameInstanceClient가 없거나 DarkRift 미연결 시 OnGUI 테스트 HUD 제공.
    /// </summary>
    public class MazeGameManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private NetworkManager fishnetManager;
        [SerializeField] private MazeGenerator mazeGenerator;
        [SerializeField] private DedicateServerClient logicBridge;
        [SerializeField] private FishNetManagetHandler connectionHandler;

        [Header("Session Handler")]
        [SerializeField] private DedicateServerHandler sessionHandler;

        [Header("Client Mode (TraineeClient)")]
        [SerializeField] private TraineeClient traineeClient;
        [SerializeField] private TraineeClientHandler traineeHandler;

        [Header("Power-Up")]
        [SerializeField] private PowerUpSpawner powerUpSpawner;

        [Header("Spawn Settings")]
        [SerializeField] private NetworkObject playerPrefab;
        [SerializeField] private bool addToDefaultScene = true;

        [Header("NPC Settings")]
        [SerializeField] private NetworkObject npcPrefab;
        [SerializeField] private int npcCount = 3;

        [Header("Standalone Test (DarkRift 미연결 시)")]
        [SerializeField] private string serverAddress = "localhost";
        [SerializeField] private ushort port = 7770;

        private readonly Dictionary<NetworkConnection, NetworkObject> spawnedPlayers = new();
        private readonly List<NetworkObject> spawnedNPCs = new();
        private int mazeSeed;
        private bool mazeBuilt;

        // 독립 테스트 모드 상태
        private bool standaloneRunning;

        // 비동기 작업 취소용
        private CancellationTokenSource disableCts;

        // OnGUI 스타일 캐싱 (매 프레임 new 방지)
        private GUIStyle cachedBoldLabel;
        private GUIStyle cachedYellowLabel;

        /// <summary>프로덕션 모드 여부 (DarkRift 연결됨 — 서버 또는 클라이언트)</summary>
        private bool IsProductionMode =>
            (logicBridge != null && logicBridge.IsRegistered) ||
            (traineeClient != null && traineeClient.isActiveAndEnabled);

        // ─────────────── 초기화 ───────────────

        private void Awake()
        {
            if (fishnetManager == null)
                fishnetManager = FindFirstObjectByType<NetworkManager>();
            if (logicBridge == null)
                logicBridge = FindFirstObjectByType<DedicateServerClient>();
            if (connectionHandler == null)
                connectionHandler = FindFirstObjectByType<FishNetManagetHandler>();
            if (traineeClient == null)
                traineeClient = FindFirstObjectByType<TraineeClient>();
            if (traineeHandler == null)
                traineeHandler = FindFirstObjectByType<TraineeClientHandler>();
            if (powerUpSpawner == null)
                powerUpSpawner = FindFirstObjectByType<PowerUpSpawner>();
            if (sessionHandler == null)
                sessionHandler = FindFirstObjectByType<DedicateServerHandler>();
        }

        private void OnEnable()
        {
            disableCts = new CancellationTokenSource();

            // ── DarkRift 세션 이벤트 (DedicateServer 모드) ──
            if (logicBridge != null)
            {
                logicBridge.OnTrainingCreateSuccess += OnTrainingCreateSuccess;
                logicBridge.OnTrainingBegin += OnTrainingBeginReceived;
                logicBridge.OnInstanceSignal += OnInstanceSignalReceived;
            }

            // ── DarkRift 세션 이벤트 (TraineeClient 모드) ──
            if (traineeClient != null)
            {
                traineeClient.OnTrainingCreatedReceived += OnTrainingCreatedReceived;
            }

            // ── SessionConnectionHandler: 커스텀 스폰 오버라이드 ──
            if (connectionHandler != null)
            {
                connectionHandler.OnSpawnPlayerOverride += OnSpawnPlayerOverride;
            }

            // ── FishNet 이벤트 ──
            if (fishnetManager != null)
            {
                fishnetManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
                fishnetManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
                fishnetManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
                fishnetManager.ClientManager.RegisterBroadcast<MazeSeedBroadcast>(OnMazeSeedReceived);
                fishnetManager.ClientManager.RegisterBroadcast<GamePauseBroadcast>(OnGamePauseReceived);
            }

            // ── NPC 스폰/디스폰 오버라이드 훅 (DedicateServerHandler에 병합됨) ──
            if (sessionHandler != null)
            {
                sessionHandler.OnSpawnNPCsOverride += OnSpawnNPCsOverride;
                sessionHandler.OnDespawnNPCsOverride += OnDespawnNPCsOverride;
            }
        }

        private void OnDisable()
        {
            // 진행 중인 비동기 작업 일괄 취소
            disableCts?.Cancel();
            disableCts?.Dispose();
            disableCts = null;

            if (logicBridge != null)
            {
                logicBridge.OnTrainingCreateSuccess -= OnTrainingCreateSuccess;
                logicBridge.OnTrainingBegin -= OnTrainingBeginReceived;
                logicBridge.OnInstanceSignal -= OnInstanceSignalReceived;
            }

            if (traineeClient != null)
            {
                traineeClient.OnTrainingCreatedReceived -= OnTrainingCreatedReceived;
            }

            if (connectionHandler != null)
            {
                connectionHandler.OnSpawnPlayerOverride -= OnSpawnPlayerOverride;
            }

            if (fishnetManager != null)
            {
                if (fishnetManager.ServerManager != null)
                {
                    fishnetManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
                    fishnetManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
                }
                if (fishnetManager.ClientManager != null)
                {
                    fishnetManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
                    fishnetManager.ClientManager.UnregisterBroadcast<MazeSeedBroadcast>(OnMazeSeedReceived);
                    fishnetManager.ClientManager.UnregisterBroadcast<GamePauseBroadcast>(OnGamePauseReceived);
                }
            }

            // ── NPC 스폰/디스폰 오버라이드 훅 해제 ──
            if (sessionHandler != null)
            {
                sessionHandler.OnSpawnNPCsOverride -= OnSpawnNPCsOverride;
                sessionHandler.OnDespawnNPCsOverride -= OnDespawnNPCsOverride;
            }
        }

        // ─────────────── DarkRift 세션 이벤트 (프로덕션) ───────────────

        /// <summary>
        /// [DedicateServer] TrainingCreate 성공 → FTP+FishNet 모두 준비 완료 시점.
        /// 미로를 생성합니다. 시드는 OnTrainingWakeResult에서 이미 설정됨.
        /// </summary>
        private void OnTrainingCreateSuccess()
        {
            Debug.Log("[MazeGameManager] TrainingCreate 성공 → 미로 생성");
            BuildMazeIfNeeded();
        }

        /// <summary>
        /// [TraineeClient] TrainingCreated 수신 → 서버 접속 정보 + 미로 시드 추출 후 생성.
        /// </summary>
        private void OnTrainingCreatedReceived(GameInstancePlugin.TrainingCreatedData data)
        {
            Debug.Log($"[MazeGameManager] TrainingCreated 수신. 미로 시드: {mazeSeed} " +
                      $"(Party={data.PartyId}, Instance={data.InstanceId})");
            BuildMazeIfNeeded();
        }

        /// <summary>
        /// DarkRift 게임 시작 시그널 수신 (InstanceSignalType.Start → OnTrainingBegin).
        /// 파워업 스포너를 활성화합니다.
        /// </summary>
        private void OnTrainingBeginReceived(int instanceId, int partyId)
        {
            Debug.Log($"[MazeGameManager] 훈련 시작 신호 수신! (Instance={instanceId}, Party={partyId}) → 파워업 스폰 활성화");
            if (powerUpSpawner)
                powerUpSpawner.StartSpawning();
            // 크리처 스폰은 DedicateServerHandler가 InGame 상태 진입 시 OnSpawnNPCsOverride 훅으로 처리
        }

        /// <summary>
        /// 미로 시드를 생성합니다.
        /// 1순위: SceneSetJson에서 "mazeseed" 키 파싱 (DedicateServerHandler 또는 TraineeClientHandler)
        /// 2순위: PartyId + InstanceId 기반 결정론적 해시
        /// </summary>
        private int GenerateMazeSeed()
        {
            // 살아있는 Handler에서 SceneSetJson 읽기
            string json = null;
            if (sessionHandler != null && !string.IsNullOrEmpty(sessionHandler.SceneSetJson))
                json = sessionHandler.SceneSetJson;
            else if (traineeHandler != null && !string.IsNullOrEmpty(traineeHandler.SceneSetJson))
                json = traineeHandler.SceneSetJson;

            // JSON에서 mazeseed 키 추출
            if (!string.IsNullOrEmpty(json))
            {
                int seed = ParseMazeSeedFromJson(json);
                if (seed != 0)
                {
                    Debug.Log($"[MazeGameManager] SceneSetJson에서 미로 시드 파싱: {seed}");
                    return seed;
                }
            }
            // 폴백: 매직키 해시
            return 442;
        }

        /// <summary>
        /// SceneSetJson에서 "mazeseed" 키의 값을 파싱합니다.
        /// JsonUtility로 파싱하거나, 단순 문자열 검색으로 추출합니다.
        /// </summary>
        private int ParseMazeSeedFromJson(string json)
        {
            try
            {
                // 간단한 JSON 파싱: {"mazeseed":12345, ...}
                // JsonUtility는 래퍼 클래스가 필요하므로 문자열 검색 사용
                string key = "\"mazeseed\"";
                int keyIndex = json.IndexOf(key, System.StringComparison.OrdinalIgnoreCase);
                if (keyIndex < 0) return 0;

                int colonIndex = json.IndexOf(':', keyIndex + key.Length);
                if (colonIndex < 0) return 0;

                // 콜론 뒤 숫자 추출
                int start = colonIndex + 1;
                while (start < json.Length && (json[start] == ' ' || json[start] == '"'))
                    start++;

                int end = start;
                while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-'))
                    end++;

                if (end > start && int.TryParse(json.Substring(start, end - start), out int seed))
                    return seed;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[MazeGameManager] mazeseed JSON 파싱 실패: {ex.Message}");
            }

            return 0;
        }

        // ─────────────── Pause/Resume 동기화 ───────────────

        /// <summary>
        /// 서버: DarkRift 인스턴스 시그널 수신 → Pause/Resume을 FishNet 클라이언트에 전파.
        /// </summary>
        private void OnInstanceSignalReceived(InstanceSignalType signalType, int instanceId, int partyId, string additionalData)
        {
            if (!fishnetManager.IsServerStarted) return;

            switch (signalType)
            {
                case InstanceSignalType.Pause:
                    Debug.Log("[MazeGameManager] Pause 시그널 → 클라이언트 전파");
                    fishnetManager.ServerManager.Broadcast(new GamePauseBroadcast { IsPaused = true });
                    break;

                case InstanceSignalType.Resume:
                    Debug.Log("[MazeGameManager] Resume 시그널 → 클라이언트 전파");
                    fishnetManager.ServerManager.Broadcast(new GamePauseBroadcast { IsPaused = false });
                    break;
            }
        }

        /// <summary>
        /// 클라이언트: 서버로부터 Pause/Resume 브로드캐스트 수신 → Time.timeScale 설정.
        /// </summary>
        private void OnGamePauseReceived(GamePauseBroadcast data, Channel channel)
        {
            Time.timeScale = data.IsPaused ? 0f : 1f;
            Debug.Log($"[MazeGameManager] Pause 수신: {(data.IsPaused ? "일시정지" : "재개")} (timeScale={Time.timeScale})");
        }

        // ─────────────── FishNet 서버 이벤트 ───────────────

        /// <summary>
        /// FishNet 서버 상태 변경. 독립 테스트 모드의 미로 생성 폴백.
        /// </summary>
        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                // 프로덕션: OnFishnetServerStarted에서 이미 처리됨
                // 독립 테스트: 여기서 미로 생성 + 파워업 스폰 즉시 시작
                if (!IsProductionMode)
                {
                    if (mazeSeed == 0)
                    {
                        mazeSeed = Random.Range(1, int.MaxValue);
                        Debug.Log($"[MazeGameManager] 독립 테스트 모드. 랜덤 시드: {mazeSeed}");
                    }
                    BuildMazeIfNeeded();

                    // 독립 테스트: 미로 생성 후 자동 활성화 (지연)
                    if (disableCts != null)
                        StandaloneDelayedActivationAsync(disableCts.Token).Forget();
                }
            }
        }

        /// <summary>
        /// 클라이언트 접속/해제 처리 (서버 측).
        /// 접속 시 시드 전송, 해제 시 플레이어 디스폰.
        /// 독립 테스트 모드에서는 AuthBroadcast 없이 직접 스폰합니다.
        /// </summary>
        private void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == RemoteConnectionState.Started)
            {
                Debug.Log($"[MazeGameManager] 클라이언트 접속: {conn.ClientId}");

                if (disableCts != null)
                {
                    SendSeedWhenReadyAsync(conn, disableCts.Token).Forget();

                    // 독립 테스트 모드: AuthBroadcast 없이 직접 스폰
                    if (!IsProductionMode)
                    {
                        WaitMazeAndSpawnDirectAsync(conn, disableCts.Token).Forget();
                    }
                }

            }
            else if (args.ConnectionState == RemoteConnectionState.Stopped)
            {
                Debug.Log($"[MazeGameManager] 클라이언트 해제: {conn.ClientId}");

                if (spawnedPlayers.TryGetValue(conn, out NetworkObject nob))
                {
                    if (nob != null && nob.IsSpawned)
                    {
                        fishnetManager.ServerManager.Despawn(nob);
                    }
                    spawnedPlayers.Remove(conn);
                }

            }
        }

        /// <summary>
        /// 인증 + 스타트씬 로드 완료 후 미로 시드를 전송합니다.
        /// </summary>
        private async UniTask SendSeedWhenReadyAsync(NetworkConnection conn, CancellationToken ct)
        {
            // 인증 완료 대기
            await UniTask.WaitUntil(() => !conn.IsActive || conn.IsAuthenticated, cancellationToken: ct);
            if (!conn.IsActive) return;

            // 스타트 씬 로드 완료 대기 (5초 타임아웃)
            bool startScenesLoaded = false;
            conn.OnLoadedStartScenes += (_, __) => startScenesLoaded = true;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(5000);

            bool isCanceled = await UniTask.WaitUntil(
                () => !conn.IsActive || startScenesLoaded,
                cancellationToken: timeoutCts.Token
            ).SuppressCancellationThrow();

            if (!conn.IsActive) return;

            // 시드 전송
            fishnetManager.ServerManager.Broadcast(conn, new MazeSeedBroadcast { Seed = mazeSeed });
            Debug.Log($"[MazeGameManager] 시드 전송: {mazeSeed} → Client {conn.ClientId}");
        }

        /// <summary>
        /// 독립 테스트 전용: 미로 빌드 완료 후 파워업/크리처 활성화.
        /// 프로덕션에서는 OnTrainingBeginReceived에서만 활성화됩니다.
        /// </summary>
        private async UniTask StandaloneDelayedActivationAsync(CancellationToken ct)
        {
            // 미로 빌드 완료 대기
            await UniTask.WaitUntil(() => mazeGenerator == null || mazeGenerator.IsMazeReady, cancellationToken: ct);

            // 프로덕션 모드로 전환된 경우 중단 (DarkRift 신호로 처리)
            if (IsProductionMode) return;

            await UniTask.Delay(1000, cancellationToken: ct);

            // 이중 체크: 아직 독립 테스트 모드인 경우만 활성화
            if (!IsProductionMode)
            {
                Debug.Log("[MazeGameManager] 독립 테스트: 파워업 + NPC 스폰 활성화");
                if (powerUpSpawner)
                    powerUpSpawner.StartSpawning();
                if (sessionHandler != null)
                    sessionHandler.TriggerSpawn();
            }
        }

        // ─────────────── 클라이언트 이벤트 ───────────────

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                if (fishnetManager.IsServerStarted)
                {
                    Debug.Log("[MazeGameManager] Host 로컬 클라이언트 접속. 미로는 서버에서 이미 생성됨.");
                }
            }
        }

        /// <summary>
        /// 서버로부터 미로 시드 수신 (클라이언트 전용).
        /// </summary>
        private void OnMazeSeedReceived(MazeSeedBroadcast data, Channel channel)
        {
            if (fishnetManager.IsServerStarted) return;

            Debug.Log($"[MazeGameManager] 미로 시드 수신: {data.Seed}");
            mazeSeed = data.Seed;

            if (mazeGenerator && !mazeGenerator.IsMazeReady)
            {
                mazeGenerator.BuildMazeFromSeed(data.Seed);
                mazeBuilt = true;
            }
        }

        // ─────────────── 미로 생성 ───────────────

        /// <summary>
        /// 미로가 아직 생성되지 않았으면 생성합니다.
        /// </summary>
        private void BuildMazeIfNeeded()
        {
            mazeSeed = GenerateMazeSeed();
            if (mazeBuilt || !mazeGenerator) return;
            if (mazeSeed == 0) return;

            mazeGenerator.BuildMazeFromSeed(mazeSeed);
            mazeBuilt = true;
            Debug.Log($"[MazeGameManager] 미로 생성 완료. Seed={mazeSeed}");
        }

        // ─────────────── 플레이어 스폰 (SessionConnectionHandler 오버라이드) ───────────────

        /// <summary>
        /// SessionConnectionHandler의 스폰 오버라이드.
        /// 인증된 플레이어를 미로 내부 랜덤 위치에 스폰합니다.
        /// </summary>
        private void OnSpawnPlayerOverride(NetworkConnection conn, AuthBroadcastData data, NetworkManager manager)
        {
            // viewer는 관전 전용 — 캐릭터 생성하지 않음
            if (string.Equals(data.Role, "viewer", System.StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"[MazeGameManager] 관전자 접속 (스폰 스킵): {conn.ClientId}");
                return;
            }

            if (!playerPrefab)
            {
                Debug.LogWarning("[MazeGameManager] playerPrefab이 할당되지 않았습니다.");
                return;
            }

            if (!mazeGenerator || !mazeGenerator.IsMazeReady)
            {
                Debug.Log($"[MazeGameManager] 미로 대기 중... 스폰 지연: {conn.ClientId}");
                if (disableCts != null)
                    WaitMazeAndSpawnAsync(conn, manager, data, disableCts.Token).Forget();
                return;
            }

            DoSpawnPlayer(conn, manager, data.PlayerId, data.Role);
        }

        /// <summary>
        /// 미로 빌드 완료 대기 후 플레이어를 스폰합니다.
        /// </summary>
        private async UniTask WaitMazeAndSpawnAsync(NetworkConnection conn, NetworkManager manager, AuthBroadcastData data, CancellationToken ct)
        {
            await UniTask.WaitUntil(() => mazeGenerator == null || mazeGenerator.IsMazeReady, cancellationToken: ct);
            if (!conn.IsActive) return;

            DoSpawnPlayer(conn, manager, data.PlayerId, data.Role);
        }

        private void DoSpawnPlayer(NetworkConnection conn, NetworkManager manager, int playerId = 0, string nickname = null)
        {
            Vector3 spawnPos = mazeGenerator
                ? mazeGenerator.GetRandomSpawnPosition()
                : Vector3.up;

            Quaternion spawnRot = Quaternion.identity;

            NetworkObject nob = manager.GetPooledInstantiated(playerPrefab, spawnPos, spawnRot, true);

            // SyncVar 초기값 설정 (Spawn 전에 호출 → FishNet 초기 동기화에 포함)
            if (nob.TryGetComponent(out MazePlayerController mpc))
            {
                string resolvedNickname = nickname ?? $"Player_{conn.ClientId}";
                mpc.InitializeIdentity(playerId, resolvedNickname);
            }

            manager.ServerManager.Spawn(nob, conn);

            if (addToDefaultScene)
            {
                manager.SceneManager.AddOwnerToDefaultScene(nob);
            }

            spawnedPlayers[conn] = nob;
            Debug.Log($"[MazeGameManager] 플레이어 스폰: {conn.ClientId} ({nickname ?? $"Player_{conn.ClientId}"}) at {spawnPos}");
        }

        // ─────────────── 독립 테스트용 스폰 (AuthBroadcast 없이 직접 스폰) ───────────────

        /// <summary>
        /// 독립 테스트 모드: 미로 준비 완료 후 직접 스폰.
        /// 프로덕션 모드에서는 SessionConnectionHandler의 AuthBroadcast 인증 후
        /// OnSpawnPlayerOverride를 통해 스폰되므로 이 경로는 사용되지 않습니다.
        /// </summary>
        private async UniTask WaitMazeAndSpawnDirectAsync(NetworkConnection conn, CancellationToken ct)
        {
            // 미로 생성 완료 대기
            await UniTask.WaitUntil(() => mazeGenerator == null || mazeGenerator.IsMazeReady, cancellationToken: ct);
            if (!conn.IsActive) return;

            // 이미 다른 경로(프로덕션 인증 플로우)로 스폰된 경우 스킵
            if (spawnedPlayers.ContainsKey(conn)) return;

            Debug.Log($"[MazeGameManager] 독립 테스트: 직접 스폰 - Client {conn.ClientId}");
            DoSpawnPlayer(conn, fishnetManager, 0, $"Player_{conn.ClientId}");
        }

        // ─────────────── NPC 스폰 오버라이드 (DedicateServerHandler 훅) ───────────────

        /// <summary>
        /// DedicateServerHandler의 NPC 스폰 오버라이드.
        /// 미로 내 랜덤 위치에 NPC를 스폰합니다.
        /// </summary>
        private void OnSpawnNPCsOverride(NetworkManager manager)
        {
            if (disableCts != null)
                SpawnNPCsAsync(manager, disableCts.Token).Forget();
        }

        /// <summary>
        /// 미로 준비 대기 후 NPC를 일괄 스폰합니다.
        /// </summary>
        private async UniTask SpawnNPCsAsync(NetworkManager manager, CancellationToken ct)
        {
            // 미로 준비 완료 대기
            await UniTask.WaitUntil(() => mazeGenerator == null || mazeGenerator.IsMazeReady, cancellationToken: ct);

            if (!npcPrefab)
            {
                Debug.LogWarning("[MazeGameManager] npcPrefab이 할당되지 않았습니다.");
                return;
            }

            for (int i = 0; i < npcCount; i++)
            {
                Vector3 pos = mazeGenerator.GetRandomSpawnPosition();
                Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

                NetworkObject nob = manager.GetPooledInstantiated(npcPrefab, pos, rot, true);
                manager.ServerManager.Spawn(nob);

                if (addToDefaultScene)
                {
                    manager.SceneManager.AddOwnerToDefaultScene(nob);
                }

                spawnedNPCs.Add(nob);

                // 서버 부하 분산 (100ms 간격)
                await UniTask.Delay(100, cancellationToken: ct);
            }

            Debug.Log($"[MazeGameManager] NPC {npcCount}마리 스폰 완료 (미로 내)");
        }

        /// <summary>
        /// DedicateServerHandler의 NPC 디스폰 오버라이드.
        /// MazeGameManager가 관리하는 NPC를 일괄 디스폰합니다.
        /// </summary>
        private void OnDespawnNPCsOverride()
        {
            int count = spawnedNPCs.Count;

            for (int i = 0; i < spawnedNPCs.Count; i++)
            {
                if (spawnedNPCs[i] && spawnedNPCs[i].IsSpawned)
                {
                    fishnetManager.ServerManager.Despawn(spawnedNPCs[i]);
                }
            }

            spawnedNPCs.Clear();

            if (count > 0)
            {
                Debug.Log($"[MazeGameManager] NPC {count}마리 디스폰 완료.");
            }
        }

        // ─────────────── OnGUI: 독립 테스트 HUD ───────────────

        private void OnGUI()
        {
            // 프로덕션 모드에서는 테스트 HUD 숨김
            if (IsProductionMode) return;

            // GUIStyle 캐싱 (최초 1회만 생성)
            if (cachedBoldLabel == null)
            {
                cachedBoldLabel = new GUIStyle(GUI.skin.label) { richText = true };
                cachedYellowLabel = new GUIStyle(GUI.skin.label) { richText = true };
            }

            GUILayout.BeginArea(new Rect(10, 10, 280, 220));
            GUILayout.BeginVertical("box");

            GUILayout.Label("<b>Maze Game (Standalone Test)</b>", cachedBoldLabel);

            if (logicBridge)
            {
                GUILayout.Label("<color=yellow>DarkRift 미연결 - 독립 테스트 모드</color>", cachedYellowLabel);
            }

            if (!standaloneRunning)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("IP:", GUILayout.Width(25));
                serverAddress = GUILayout.TextField(serverAddress, GUILayout.Width(120));
                GUILayout.Label("Port:", GUILayout.Width(35));
                string portStr = GUILayout.TextField(port.ToString(), GUILayout.Width(50));
                if (ushort.TryParse(portStr, out ushort newPort))
                    port = newPort;
                GUILayout.EndHorizontal();

                GUILayout.Space(5);

                if (GUILayout.Button("Start Host (Server + Client)"))
                {
                    SetTransportPort(port);
                    fishnetManager.ServerManager.StartConnection();
                    fishnetManager.ClientManager.StartConnection();
                    standaloneRunning = true;
                }

                if (GUILayout.Button("Start Server Only"))
                {
                    SetTransportPort(port);
                    fishnetManager.ServerManager.StartConnection();
                    standaloneRunning = true;
                }

                if (GUILayout.Button("Start Client Only"))
                {
                    SetTransportAddress(serverAddress);
                    SetTransportPort(port);
                    fishnetManager.ClientManager.StartConnection();
                    standaloneRunning = true;
                }
            }
            else
            {
                string mode = "";
                if (fishnetManager.IsServerStarted && fishnetManager.IsClientStarted)
                    mode = "Host";
                else if (fishnetManager.IsServerStarted)
                    mode = "Server";
                else if (fishnetManager.IsClientStarted)
                    mode = "Client";

                GUILayout.Label($"Running as: {mode}");
                GUILayout.Label($"Maze Seed: {mazeSeed}");
                GUILayout.Label($"Players: {spawnedPlayers.Count}");

                if (GUILayout.Button("Stop"))
                {
                    if (fishnetManager.IsServerStarted)
                        fishnetManager.ServerManager.StopConnection(true);
                    if (fishnetManager.IsClientStarted)
                        fishnetManager.ClientManager.StopConnection();
                    standaloneRunning = false;
                }
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        // ─────────────── Transport 헬퍼 (독립 테스트용) ───────────────

        private void SetTransportPort(ushort targetPort)
        {
            var transport = fishnetManager.TransportManager.Transport;
            if (transport == null) return;

            var portProp = transport.GetType().GetProperty("Port");
            if (portProp != null)
            {
                portProp.SetValue(transport, targetPort);
            }
        }

        private void SetTransportAddress(string address)
        {
            var transport = fishnetManager.TransportManager.Transport;
            if (transport == null) return;

            var addrField = transport.GetType().GetField("_clientAddress",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (addrField != null)
            {
                addrField.SetValue(transport, address);
            }
        }
    }
}
