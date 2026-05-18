using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;

namespace GameInstancePlugin.Client.MazeGame
{
    /// <summary>
    /// 파워업 아이템 타입.
    /// </summary>
    public enum PowerUpType : byte
    {
        Red = 0,   // 폭탄 크기 3배
        Blue = 1,  // 사거리 2배
        Green = 2  // 지속시간 1.5배
    }

    /// <summary>
    /// 미로에 스폰되는 파워업 큐브 아이템.
    /// - 3가지 타입: Red(크기3x), Blue(사거리2x), Green(지속1.5x)
    /// - 회전 + 상하 흔들림 애니메이션
    /// - 플레이어 접촉 시 파워업 적용 → 디스폰
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    [RequireComponent(typeof(Rigidbody))]
    public class PowerUpCube : NetworkBehaviour
    {
        [Header("비주얼")]
        [SerializeField] private float rotateSpeed = 90f;
        [SerializeField] private float bobAmplitude = 0.3f;
        [SerializeField] private float bobSpeed = 2f;

        // ── Network Sync ──
        private readonly SyncVar<byte> powerUpType = new SyncVar<byte>();

        // ── Private ──
        private Vector3 basePosition;
        private MeshRenderer meshRenderer;
        private bool isPositionInitialized;

        private static readonly Color[] TypeColors = { Color.red, Color.blue, Color.green };

        // ── 서버측 활성 카운트 (FindObjectsByType 대체) ──
        /// <summary>현재 씬에 활성화된 PowerUpCube 수 (서버 전용)</summary>
        public static int ActiveCount { get; private set; }

        // ─────────────── 초기화 ───────────────

        private void Awake()
        {
            meshRenderer = GetComponent<MeshRenderer>();

            // 트리거 + 키네마틱 설정 (OnTriggerEnter 감지용)
            var col = GetComponent<BoxCollider>();
            col.isTrigger = true;

            var rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            powerUpType.OnChange += OnTypeChanged;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            ActiveCount++;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            ActiveCount--;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            basePosition = transform.position;
            isPositionInitialized = true;
            UpdateColor(powerUpType.Value);
        }

        /// <summary>
        /// 서버에서 스폰 직후 호출: 파워업 타입 설정.
        /// </summary>
        public void Initialize(PowerUpType type)
        {
            powerUpType.Value = (byte)type;
            UpdateColor((byte)type);
        }

        // ─────────────── 타입 동기화 + 비주얼 ───────────────

        private void OnTypeChanged(byte prev, byte next, bool asServer)
        {
            UpdateColor(next);
        }

        private void UpdateColor(byte type)
        {
            if (!meshRenderer) return;
            if (type >= TypeColors.Length) return;

            // HDRP: material instance에 직접 색상 설정
            meshRenderer.material.SetColor("_BaseColor", TypeColors[type]);
        }

        // ─────────────── 애니메이션 ───────────────

        private void Update()
        {
            if (!isPositionInitialized) return;

            // 회전
            transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime);

            // 상하 흔들림
            Vector3 pos = basePosition;
            pos.y += Mathf.Sin(Time.time * bobSpeed) * bobAmplitude;
            transform.position = pos;
        }

        // ─────────────── 충돌 (서버) ───────────────

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServerInitialized) return;

            var player = other.GetComponent<MazePlayerController>();
            if (!player) return;

            // 파워업 적용
            player.ApplyPowerUp((PowerUpType)powerUpType.Value);
            Debug.Log($"[PowerUpCube] {(PowerUpType)powerUpType.Value} 획득! (Client: {player.Owner?.ClientId})");

            // 디스폰
            ServerManager.Despawn(NetworkObject);
        }
    }
}
