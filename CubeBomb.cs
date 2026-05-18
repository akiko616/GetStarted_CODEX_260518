using UnityEngine;
using FishNet.Object;
using FishNet.Managing;

namespace GameInstancePlugin.Client.MazeGame
{
    /// <summary>
    /// 서버 권한 큐브 폭탄 투사체.
    /// - Rigidbody + useGravity로 높은 포물선 궤적
    /// - 벽/바닥에서는 물리 바운스 (디스폰 안함)
    /// - 플레이어에 닿으면 HP -1 후 디스폰
    /// - 자기가 쏜 폭탄은 자기에게 피해 없음
    /// - 5초 경과 시 자동 디스폰
    /// - 파워업: Red(크기3x), Blue(사거리2x-Launch), Green(지속1.5x)
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CubeBomb : NetworkBehaviour
    {
        [Header("폭탄 설정")]
        [SerializeField] private float lifetime = 5f;
        [SerializeField] private int damage = 1;

        private const float BaseScale = 0.3f;
        private Rigidbody rb;
        private float spawnTime;
        private bool hasExploded;
        private NetworkObject ownerNob;
        private float activeLifetime;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            spawnTime = Time.time;
            hasExploded = false;
            activeLifetime = lifetime;
            transform.localScale = Vector3.one * BaseScale; // 풀 재사용 시 크기 초기화

            // 서버: 물리 시뮬레이션 활성화
            if (!rb) rb = GetComponent<Rigidbody>();
            rb.isKinematic = false;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // 클라이언트 전용(서버 아닌 경우): Rigidbody를 kinematic으로 설정
            // → NetworkTransform이 위치를 동기화하므로 로컬 물리 시뮬레이션 비활성화
            if (!IsServerInitialized)
            {
                if (!rb) rb = GetComponent<Rigidbody>();
                rb.isKinematic = true;
            }
        }

        /// <summary>
        /// 하위호환: owner/파워업 없이 발사.
        /// </summary>
        public void Launch(Vector3 velocity)
        {
            Launch(velocity, null, 1f, 1f);
        }

        /// <summary>
        /// 하위호환: 파워업 없이 발사.
        /// </summary>
        public void Launch(Vector3 velocity, NetworkObject owner)
        {
            Launch(velocity, owner, 1f, 1f);
        }

        /// <summary>
        /// 서버에서 호출. 초기 속도 + 발사자(owner) + 파워업 배율 적용.
        /// </summary>
        public void Launch(Vector3 velocity, NetworkObject owner, float sizeMultiplier, float lifetimeMultiplier)
        {
            ownerNob = owner;
            if (!rb) rb = GetComponent<Rigidbody>();
            rb.linearVelocity = velocity;

            // 크기 동기화 (풀 재사용 시 이전 크기 잔류 방지)
            transform.localScale = Vector3.one * BaseScale * sizeMultiplier;
            RpcSetScale(sizeMultiplier);

            // Green 파워업: 지속시간 증가
            activeLifetime = lifetime * lifetimeMultiplier;
        }

        private void Update()
        {
            if (!IsServerInitialized) return;
            if (hasExploded) return;

            if (Time.time - spawnTime >= activeLifetime)
            {
                Explode();
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsServerInitialized) return;
            if (hasExploded) return;

            // 크리처에 닿았을 때 대미지 + 디스폰
            var creature = collision.gameObject.GetComponent<MazeCreature>();
            if (creature)
            {
                creature.TakeDamage(damage);
                Explode();
                return;
            }

            // 플레이어에 닿았을 때만 대미지 + 디스폰
            var player = collision.gameObject.GetComponent<MazePlayerController>();
            if (!player) return; // 벽/바닥 → 물리 바운스

            // 자기가 쏜 폭탄은 자기에게 피해 없음
            if (ownerNob && player.NetworkObject == ownerNob) return;

            player.TakeDamage(damage);
            Explode();
        }

        private void Explode()
        {
            hasExploded = true;
            RpcOnExplode(transform.position);
            ServerManager.Despawn(NetworkObject);
        }

        /// <summary>
        /// 클라이언트에 크기 변경을 동기화합니다 (Red 파워업).
        /// </summary>
        [ObserversRpc]
        private void RpcSetScale(float multiplier)
        {
            transform.localScale = Vector3.one * BaseScale * multiplier;
        }

        /// <summary>
        /// 모든 클라이언트에 폭발 이펙트를 재생합니다.
        /// </summary>
        [ObserversRpc]
        private void RpcOnExplode(Vector3 position)
        {
            Debug.Log($"[CubeBomb] 폭발! 위치: {position}");
        }
    }
}
