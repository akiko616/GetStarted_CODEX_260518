using UnityEngine;
using FishNet.Object;
using FishNet.Managing;

namespace GameInstancePlugin.Client.MazeGame
{
    /// <summary>
    /// 미로 내 파워업 큐브 자동 스포너.
    /// - 게임 시작 신호(DarkRift InstanceSignal.Start) 이후 주기적으로 스폰
    /// - 미로 이동영역 내 랜덤 위치에 랜덤 타입 큐브 스폰
    /// - 씬 내 최대 큐브 수 제한 (기본 5개, PowerUpCube.ActiveCount 기반)
    /// - 서버에서만 동작 (IsServerInitialized 게이트)
    /// - MazeGameManager가 StartSpawning()을 호출하여 스폰 시작
    ///
    /// ── 설계 참고: 직접 참조 vs 델리게이트 훅 ──
    ///
    /// 이 클래스는 MazeGenerator를 FindFirstObjectByType으로 직접 참조합니다 (강결합).
    /// 파워업은 미로 게임 전용 콘텐츠이므로 범용 재사용이 불필요하기 때문입니다.
    ///
    /// 반면, 같은 프로젝트의 DedicatedServerNPCSpawner는 델리게이트 훅 패턴을 사용합니다:
    ///   - SpawnPositionProvider (Func&lt;Vector3&gt;) — 스폰 위치 주입
    ///   - SpawnReadinessCheck (Func&lt;bool&gt;) — 준비 완료 조건 주입
    ///   → 미로, 생존, 오픈월드 등 다양한 게임 모드에서 재사용 가능
    ///
    /// 패턴 선택 기준:
    ///   - 특정 게임 전용 + 단순한 로직 → 직접 참조 (이 클래스)
    ///   - 여러 게임에서 재사용 + 외부 의존성 변동 가능 → 델리게이트 훅 (DedicatedServerNPCSpawner)
    /// </summary>
    public class PowerUpSpawner : NetworkBehaviour
    {
        [Header("스포너 설정")]
        [SerializeField] private NetworkObject powerUpPrefab;
        [SerializeField] private float spawnInterval = 10f;
        [SerializeField] private float spawnHeight = 1f;
        [SerializeField] private int maxCubesInScene = 5;

        private float nextSpawnTime;
        private MazeGenerator mazeGen;
        private bool isSpawnEnabled;

        public override void OnStartServer()
        {
            base.OnStartServer();
            mazeGen = FindFirstObjectByType<MazeGenerator>();
            isSpawnEnabled = false;
            Debug.Log("[PowerUpSpawner] 서버 시작. 게임 시작 신호 대기중...");
        }

        /// <summary>
        /// 게임 시작 신호 수신 후 호출. 스폰을 시작합니다.
        /// MazeGameManager에서 OnTrainingBegin 수신 시 호출합니다.
        /// </summary>
        public void StartSpawning()
        {
            if (isSpawnEnabled) return;
            isSpawnEnabled = true;
            nextSpawnTime = Time.time + spawnInterval;
            Debug.Log("[PowerUpSpawner] 스폰 시작! (게임 시작 신호 수신)");
        }

        /// <summary>
        /// 게임 종료 시 스폰을 중지합니다.
        /// </summary>
        public void StopSpawning()
        {
            isSpawnEnabled = false;
            Debug.Log("[PowerUpSpawner] 스폰 중지.");
        }

        private void Update()
        {
            if (!IsServerInitialized) return;
            if (!isSpawnEnabled) return;
            if (!mazeGen || !mazeGen.IsMazeReady) return;

            if (Time.time >= nextSpawnTime)
            {
                TrySpawnPowerUp();
                nextSpawnTime = Time.time + spawnInterval;
            }
        }

        private void TrySpawnPowerUp()
        {
            // 정적 카운터로 씬 내 최대 개수 제한 (FindObjectsByType 대체)
            if (PowerUpCube.ActiveCount >= maxCubesInScene)
            {
                Debug.Log($"[PowerUpSpawner] 최대 수량 도달 ({PowerUpCube.ActiveCount}/{maxCubesInScene}). 스폰 스킵.");
                return;
            }

            Vector3 pos = mazeGen.GetRandomSpawnPosition();
            pos.y = spawnHeight;

            PowerUpType type = (PowerUpType)Random.Range(0, 3);

            NetworkManager nm = NetworkManager;
            if (!nm) return;

            NetworkObject nob = nm.GetPooledInstantiated(powerUpPrefab, pos, Quaternion.identity, true);
            nm.ServerManager.Spawn(nob);

            if (nob.TryGetComponent(out PowerUpCube cube))
            {
                cube.Initialize(type);
            }

            Debug.Log($"[PowerUpSpawner] {type} 큐브 스폰 at {pos}");
        }
    }
}
