using System.Threading;
using UnityEngine;
using UnityEngine.AI;
using Cysharp.Threading.Tasks;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Managing;

namespace GameInstancePlugin.Client.MazeGame
{
    /// <summary>
    /// 크리처 AI 상태.
    /// </summary>
    public enum CreatureState : byte
    {
        PatrolSearch = 0,
        Patrol = 1,
        Chase = 2,
        Attack = 3,
        Stunned = 4
    }

    /// <summary>
    /// 미로 크리처 AI 컨트롤러.
    /// - 랜덤 패트롤 → 플레이어 탐지 → 추적 → 공격 → 복귀 사이클
    /// - 공격: 사격 애니메이션 후 구체 투사체 발사 (중력 없이 천천히)
    /// - 10회 피격 → RED 변환 + 5초 스턴 → 패트롤 재시작
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class MazeCreature : NetworkBehaviour
    {
        [Header("패트롤")]
        [SerializeField] private int patrolPointCount = 4;

        [Header("탐지")]
        [SerializeField] private float detectionRange = 12f;
        [SerializeField] private float loseTargetRange = 18f;

        [Header("전투")]
        [SerializeField] private NetworkObject projectilePrefab;
        [SerializeField] private float attackRange = 8f;
        [SerializeField] private float attackCooldown = 3f;
        [SerializeField] private float projectileSpeed = 5f;
        [SerializeField] private int maxHp = 10;
        [SerializeField] private float stunDuration = 5f;

        // ── Network Sync ──
        private readonly SyncVar<int> currentHp = new SyncVar<int>();
        private readonly SyncVar<byte> creatureState = new SyncVar<byte>();

        // ── Private ──
        private NavMeshAgent agent;
        private Animator animator;
        private MazeGenerator mazeGen;
        private Vector3[] patrolPoints;
        private int currentPatrolIndex;
        private MazePlayerController targetPlayer;
        private float lastAttackTime;
        private float stunEndTime;
        private bool isAttacking;
        private bool isInitialized;
        private bool isActivated;

        // ── 비동기 작업 취소 ──
        private CancellationTokenSource serverCts;

        // ── Visual ──
        private MeshRenderer[] bodyRenderers;
        private static readonly Color DefaultColor = new Color(0.7f, 0.1f, 0.1f);
        private static readonly int AttackTrigger = Animator.StringToHash("Attack");

        // ── 탐지 최적화: 제곱 거리 캐싱 ──
        private float detectionRangeSqr;
        private float loseTargetRangeSqr;
        private float attackRangeSqr;
        private float attackRangeExitSqr;

        // ─────────────── 초기화 ───────────────

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            animator = GetComponent<Animator>();
            currentHp.OnChange += OnHpChanged;
            creatureState.OnChange += OnStateChanged;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            mazeGen = FindFirstObjectByType<MazeGenerator>();
            agent.enabled = false;
            currentHp.Value = maxHp;
            creatureState.Value = (byte)CreatureState.PatrolSearch;
            isInitialized = false;
            isActivated = true; // 스폰 시 자동 활성화 (DedicatedServerNPCSpawner가 InGame 시점에 스폰)

            // 제곱 거리 사전 계산
            detectionRangeSqr = detectionRange * detectionRange;
            loseTargetRangeSqr = loseTargetRange * loseTargetRange;
            attackRangeSqr = attackRange * attackRange;
            attackRangeExitSqr = (attackRange * 1.3f) * (attackRange * 1.3f);

            // 패트롤 포인트 배열 사전 할당
            patrolPoints = new Vector3[patrolPointCount];

            // 비동기 작업용 CTS
            serverCts = new CancellationTokenSource();

            Debug.Log("[MazeCreature] 서버 시작. 게임 시작 신호 대기중...");
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            serverCts?.Cancel();
            serverCts?.Dispose();
            serverCts = null;
        }

        /// <summary>
        /// 게임 시작 신호 수신 후 호출. 크리처 AI를 활성화합니다.
        /// </summary>
        public void Activate()
        {
            if (isActivated) return;
            isActivated = true;
            Debug.Log("[MazeCreature] 활성화! (게임 시작 신호 수신)");
        }

        /// <summary>
        /// 게임 종료 시 크리처 AI를 비활성화합니다.
        /// </summary>
        public void Deactivate()
        {
            isActivated = false;
            agent.ResetPath();
            isAttacking = false;
            targetPlayer = null;
            Debug.Log("[MazeCreature] 비활성화.");
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            bodyRenderers = GetComponentsInChildren<MeshRenderer>();

            // 클라이언트: NavMeshAgent 비활성화 (NetworkTransform 충돌 방지)
            if (!IsServerInitialized)
            {
                if (!agent) agent = GetComponent<NavMeshAgent>();
                agent.enabled = false;
            }
        }

        // ─────────────── AI 업데이트 (서버 전용) ───────────────

        private void Update()
        {
            if (!IsServerInitialized) return;
            if (!isActivated) return;
            if (!mazeGen || !mazeGen.IsMazeReady) return;

            if (!isInitialized)
            {
                agent.enabled = true;
                Vector3 startPos = mazeGen.GetRandomSpawnPosition();
                agent.Warp(startPos);
                isInitialized = true;
                Debug.Log($"[MazeCreature] 초기화 완료. 시작 위치: {startPos}");
            }

            switch ((CreatureState)creatureState.Value)
            {
                case CreatureState.PatrolSearch:
                    DoPatrolSearch();
                    break;
                case CreatureState.Patrol:
                    DoPatrol();
                    break;
                case CreatureState.Chase:
                    DoChase();
                    break;
                case CreatureState.Attack:
                    DoAttack();
                    break;
                case CreatureState.Stunned:
                    DoStunned();
                    break;
            }
        }

        // ─────────────── 패트롤 ───────────────

        private void DoPatrolSearch()
        {
            // 사전 할당된 배열 재사용
            for (int i = 0; i < patrolPointCount; i++)
            {
                patrolPoints[i] = mazeGen.GetRandomSpawnPosition();
            }
            currentPatrolIndex = 0;
            agent.SetDestination(patrolPoints[0]);
            creatureState.Value = (byte)CreatureState.Patrol;
            Debug.Log("[MazeCreature] 새 패트롤 경로 설정");
        }

        private void DoPatrol()
        {
            var target = FindNearestVisiblePlayer();
            if (target)
            {
                targetPlayer = target;
                creatureState.Value = (byte)CreatureState.Chase;
                Debug.Log("[MazeCreature] 플레이어 발견! → 추적 시작");
                return;
            }

            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.5f)
            {
                currentPatrolIndex++;
                if (currentPatrolIndex >= patrolPoints.Length)
                {
                    creatureState.Value = (byte)CreatureState.PatrolSearch;
                    return;
                }
                agent.SetDestination(patrolPoints[currentPatrolIndex]);
            }
        }

        // ─────────────── 추적 ───────────────

        private void DoChase()
        {
            if (!IsTargetValid())
            {
                ReturnToPatrol();
                return;
            }

            float sqrDist = (transform.position - targetPlayer.transform.position).sqrMagnitude;

            if (sqrDist > loseTargetRangeSqr || !CanSeeTarget(targetPlayer.transform.position))
            {
                ReturnToPatrol();
                return;
            }

            if (sqrDist <= attackRangeSqr && !isAttacking)
            {
                agent.ResetPath();
                creatureState.Value = (byte)CreatureState.Attack;
                return;
            }

            agent.SetDestination(targetPlayer.transform.position);
        }

        // ─────────────── 공격 ───────────────

        private void DoAttack()
        {
            if (!IsTargetValid())
            {
                ReturnToPatrol();
                return;
            }

            float sqrDist = (transform.position - targetPlayer.transform.position).sqrMagnitude;

            if (sqrDist > attackRangeExitSqr)
            {
                creatureState.Value = (byte)CreatureState.Chase;
                return;
            }

            // 타겟 방향 바라보기
            Vector3 dir = targetPlayer.transform.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(dir.normalized);

            if (!isAttacking && Time.time - lastAttackTime >= attackCooldown)
            {
                if (serverCts != null)
                    AttackSequenceAsync(serverCts.Token).Forget();
            }
        }

        /// <summary>
        /// 공격 시퀀스: 애니메이션 → 대기 → 투사체 발사.
        /// </summary>
        private async UniTask AttackSequenceAsync(CancellationToken ct)
        {
            isAttacking = true;
            lastAttackTime = Time.time;

            RpcTriggerAttack();

            // 사격 자세 대기
            await UniTask.Delay(1000, cancellationToken: ct);

            if (IsTargetValid())
            {
                FireProjectile();
            }

            await UniTask.Delay(500, cancellationToken: ct);
            isAttacking = false;
        }

        private void FireProjectile()
        {
            if (!projectilePrefab) return;
            NetworkManager nm = NetworkManager;
            if (!nm) return;

            Vector3 spawnPos = transform.position + Vector3.up * 1.0f + transform.forward * 0.6f;
            Vector3 targetPos = targetPlayer.transform.position + Vector3.up * 0.8f;
            Vector3 dir = (targetPos - spawnPos).normalized;

            NetworkObject nob = nm.GetPooledInstantiated(projectilePrefab, spawnPos, Quaternion.LookRotation(dir), true);
            nm.ServerManager.Spawn(nob);

            if (nob.TryGetComponent(out CreatureProjectile proj))
            {
                proj.Launch(dir * projectileSpeed);
            }

            Debug.Log("[MazeCreature] 투사체 발사!");
        }

        // ─────────────── 복귀 ───────────────

        private void ReturnToPatrol()
        {
            targetPlayer = null;
            isAttacking = false;

            if (patrolPoints != null && currentPatrolIndex < patrolPoints.Length)
            {
                agent.SetDestination(patrolPoints[currentPatrolIndex]);
                creatureState.Value = (byte)CreatureState.Patrol;
            }
            else
            {
                creatureState.Value = (byte)CreatureState.PatrolSearch;
            }
            Debug.Log("[MazeCreature] 패트롤 복귀");
        }

        // ─────────────── 탐지 헬퍼 ───────────────

        private MazePlayerController FindNearestVisiblePlayer()
        {
            // 정적 리스트 참조로 FindObjectsByType 대체
            var players = MazePlayerController.ActivePlayers;
            float closestSqrDist = detectionRangeSqr;
            MazePlayerController closest = null;

            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (!player || !player.NetworkObject || !player.NetworkObject.IsSpawned) continue;

                float sqrDist = (transform.position - player.transform.position).sqrMagnitude;
                if (sqrDist < closestSqrDist && CanSeeTarget(player.transform.position))
                {
                    closestSqrDist = sqrDist;
                    closest = player;
                }
            }

            return closest;
        }

        private bool CanSeeTarget(Vector3 targetPos)
        {
            Vector3 origin = transform.position + Vector3.up * 1.0f;
            Vector3 targetEye = targetPos + Vector3.up * 0.8f;
            Vector3 dir = targetEye - origin;
            float dist = dir.magnitude;

            if (Physics.Raycast(origin, dir.normalized, out RaycastHit hit, dist))
            {
                return hit.collider.GetComponent<MazePlayerController>() != null;
            }
            return true;
        }

        private bool IsTargetValid()
        {
            return targetPlayer &&
                   targetPlayer.NetworkObject &&
                   targetPlayer.NetworkObject.IsSpawned;
        }

        // ─────────────── 피격 / 스턴 ───────────────

        /// <summary>
        /// 서버에서 호출: 큐브 폭탄에 의한 피격.
        /// </summary>
        public void TakeDamage(int amount)
        {
            if (!IsServerInitialized) return;
            if ((CreatureState)creatureState.Value == CreatureState.Stunned) return;

            currentHp.Value = Mathf.Max(0, currentHp.Value - amount);
            Debug.Log($"[MazeCreature] 피격! HP: {currentHp.Value}/{maxHp}");

            if (currentHp.Value <= 0)
            {
                EnterStunned();
            }
        }

        private void EnterStunned()
        {
            creatureState.Value = (byte)CreatureState.Stunned;
            agent.ResetPath();
            isAttacking = false;
            targetPlayer = null;
            stunEndTime = Time.time + stunDuration;
            RpcSetColor(true);
            Debug.Log($"[MazeCreature] 스턴! RED 전환. {stunDuration}초 후 재시작");
        }

        private void DoStunned()
        {
            if (Time.time >= stunEndTime)
            {
                currentHp.Value = maxHp;
                RpcSetColor(false);
                creatureState.Value = (byte)CreatureState.PatrolSearch;
                Debug.Log("[MazeCreature] 스턴 해제 → 패트롤 재시작");
            }
        }

        // ─────────────── RPCs ───────────────

        [ObserversRpc(BufferLast = true)]
        private void RpcTriggerAttack()
        {
            if (animator)
                animator.SetTrigger(AttackTrigger);
        }

        [ObserversRpc(BufferLast = true)]
        private void RpcSetColor(bool isRed)
        {
            if (bodyRenderers == null) return;
            Color color = isRed ? new Color(1f, 0f, 0f) : DefaultColor;
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                if (bodyRenderers[i]) bodyRenderers[i].material.SetColor("_BaseColor", color);
            }
        }

        // ─────────────── SyncVar Callbacks ───────────────

        private void OnHpChanged(int prev, int next, bool asServer) { }

        private void OnStateChanged(byte prev, byte next, bool asServer)
        {
            if (asServer) return;

            var state = (CreatureState)next;

            // 스턴 색상 동기화 (늦은 접속자 포함)
            if (bodyRenderers != null)
            {
                Color color = (state == CreatureState.Stunned) ? new Color(1f, 0f, 0f) : DefaultColor;
                for (int i = 0; i < bodyRenderers.Length; i++)
                {
                    if (bodyRenderers[i]) bodyRenderers[i].material.SetColor("_BaseColor", color);
                }
            }

            // 애니메이션 상태 반영
            if (animator)
            {
                animator.speed = (state == CreatureState.Stunned) ? 0f : 1f;
            }
        }
    }
}
