using UnityEngine;
using FishNet.Object;

namespace GameInstancePlugin.Client.MazeGame
{
    /// <summary>
    /// 크리처가 발사하는 구체 투사체.
    /// - 중력 없이 천천히 직선 이동
    /// - 플레이어에 충돌 시 대미지 + 제거
    /// - 벽에 충돌 시 제거
    /// - 8초 후 자동 제거
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CreatureProjectile : NetworkBehaviour
    {
        [Header("투사체 설정")]
        [SerializeField] private float lifetime = 8f;
        [SerializeField] private int damage = 1;

        private Rigidbody rb;
        private float spawnTime;
        private bool hasHit;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.useGravity = false;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            spawnTime = Time.time;
            hasHit = false;

            // 서버: 물리 시뮬레이션 활성화 (풀 재사용 대비)
            if (!rb) rb = GetComponent<Rigidbody>();
            rb.isKinematic = false;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // 클라이언트: Rigidbody를 kinematic으로 설정
            // → NetworkTransform이 위치를 동기화하므로 로컬 물리 비활성화
            if (!IsServerInitialized)
            {
                if (!rb) rb = GetComponent<Rigidbody>();
                rb.isKinematic = true;
            }
        }

        /// <summary>
        /// 서버에서 호출: 투사체 발사 속도 설정.
        /// </summary>
        public void Launch(Vector3 velocity)
        {
            if (!rb) rb = GetComponent<Rigidbody>();
            rb.linearVelocity = velocity;
        }

        private void Update()
        {
            if (!IsServerInitialized) return;
            if (hasHit) return;

            if (Time.time - spawnTime >= lifetime)
            {
                DestroyProjectile();
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsServerInitialized) return;
            if (hasHit) return;

            var player = collision.gameObject.GetComponent<MazePlayerController>();
            if (player)
            {
                player.TakeDamage(damage);
            }

            DestroyProjectile();
        }

        private void DestroyProjectile()
        {
            hasHit = true;
            ServerManager.Despawn(NetworkObject);
        }
    }
}
