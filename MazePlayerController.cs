using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AI;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Managing;
using TMPro;

namespace GameInstancePlugin.Client.MazeGame
{
    /// <summary>
    /// 미로 게임 플레이어 컨트롤러.
    /// - Owner만 입력 처리 (Non-owner는 NavMeshAgent 비활성화, NetworkTransform으로 동기화)
    /// - WASD/화살표 키로 8방향 NavMeshAgent 이동
    /// - 마우스 좌클릭으로 큐브 폭탄 발사 (ServerRpc → 서버 스폰)
    /// - 아이소메트릭 탑다운 카메라 (Owner 전용)
    /// - HP 10칸 (SyncVar 동기화), 피격 시 -1, HP 0이면 리스폰+회복
    /// - 파워업 아이템: Red(크기3x), Blue(사거리2x), Green(지속1.5x) — 20초 유지
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class MazePlayerController : NetworkBehaviour
    {
        [Header("이동")]
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float rotationSpeed = 720f;

        [Header("발사")]
        [SerializeField] private NetworkObject bombPrefab;
        [SerializeField] private float throwForce = 1.5f;
        [SerializeField] private float throwArcForce = 5f;
        [SerializeField] private float shootCooldown = 0.5f;

        [Header("카메라")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Vector3 cameraOffset = new Vector3(0, 15, -10);

        [Header("HP")]
        [SerializeField] private int maxHp = 10;
        [SerializeField] private float respawnInvincibleTime = 2f;

        [Header("파워업")]
        [SerializeField] private float powerUpDuration = 20f;

        // ── Network Sync (FishNet V4: SyncVar<T> 클래스 방식) ──
        private readonly SyncVar<int> currentHp = new SyncVar<int>();
        private readonly SyncVar<byte> activePowerUp = new SyncVar<byte>();
        private readonly SyncVar<int> playerId = new SyncVar<int>();
        private readonly SyncVar<string> nickname = new SyncVar<string>(string.Empty);

        // ── Private ──
        private NavMeshAgent agent;
        private float lastShootTime;
        private Transform cameraTransform;
        private float invincibleUntil;
        private float powerUpExpiry;
        private MazeGenerator mazeGen;

        // ── HP Bar UI ──
        private GameObject hpBarRoot;
        private Image[] hpSegments;
        private Camera billboardCamera;

        // ── Power-Up Visual ──
        private GameObject powerUpIndicator;
        private static readonly Color[] PowerUpColors = { Color.red, Color.blue, Color.green };

        // ── Nickname UI ──
        private GameObject nicknameRoot;
        private TextMeshProUGUI nicknameText;

        // ── 서버측 활성 플레이어 추적 (FindObjectsByType 대체) ──
        public static readonly List<MazePlayerController> ActivePlayers = new();

        // ─────────────── 초기화 ───────────────

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            currentHp.OnChange += OnHpChanged;
            activePowerUp.OnChange += OnPowerUpChanged;
            nickname.OnChange += OnNicknameChanged;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            ActivePlayers.Add(this);

            mazeGen = FindFirstObjectByType<MazeGenerator>();

            // 서버에서 HP 초기화 + 파워업 없음
            currentHp.Value = maxHp;
            activePowerUp.Value = 255; // None

            // 데디케이트 서버: 카메라 불필요
            if (!IsClientStarted && playerCamera)
            {
                playerCamera.gameObject.SetActive(false);
            }
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            ActivePlayers.Remove(this);
        }

        /// <summary>
        /// 서버에서 호출: 스폰 직전 PlayerId/Nickname 설정.
        /// MazeGameManager.DoSpawnPlayer()에서 Spawn() 호출 전에 호출합니다.
        /// SyncVar 초기값이 Spawn()의 초기 동기화에 포함됩니다.
        /// </summary>
        public void InitializeIdentity(int id, string name)
        {
            playerId.Value = id;
            nickname.Value = name;
            gameObject.name = $"MazePlayer_{name}";
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // UI: 모든 클라이언트에서 모든 플레이어에 대해 생성
            CreateHpBar();
            CreateNicknameUI();
            UpdateNicknameText(nickname.Value);
            UpdateHpBarVisual(currentHp.Value);
            UpdatePowerUpVisual(activePowerUp.Value);

            if (IsOwner)
            {
                agent.enabled = true;
                agent.speed = moveSpeed;
                agent.angularSpeed = rotationSpeed;
                agent.acceleration = 20f;
                agent.stoppingDistance = 0f;
                agent.autoBraking = false;

                SetupCamera();
                Debug.Log("[MazePlayerController] 내 캐릭터 스폰. 조작 가능.");
            }
            else
            {
                // Non-owner: NetworkTransform이 위치를 동기화
                agent.enabled = false;

                if (playerCamera)
                    playerCamera.gameObject.SetActive(false);
            }
        }

        // ─────────────── HP 시스템 ───────────────

        /// <summary>
        /// 서버에서 호출: 대미지 적용. CubeBomb.OnCollisionEnter → 여기로 호출.
        /// </summary>
        public void TakeDamage(int amount)
        {
            if (!IsServerInitialized) return;
            if (Time.time < invincibleUntil) return;

            currentHp.Value = Mathf.Max(0, currentHp.Value - amount);
            Debug.Log($"[MazePlayerController] 피격! HP: {currentHp.Value}/{maxHp} (Client: {Owner?.ClientId})");

            if (currentHp.Value <= 0)
            {
                Respawn();
            }
        }

        /// <summary>
        /// HP 0 → 다른 스폰포인트로 이동, HP 회복, 무적 부여, 파워업 해제.
        /// </summary>
        private void Respawn()
        {
            Vector3 newPos = mazeGen
                ? mazeGen.GetRandomSpawnPosition()
                : Vector3.up;

            if (agent && agent.isOnNavMesh)
            {
                agent.Warp(newPos);
            }
            else
            {
                transform.position = newPos;
            }

            currentHp.Value = maxHp;
            invincibleUntil = Time.time + respawnInvincibleTime;
            activePowerUp.Value = 255;

            Debug.Log($"[MazePlayerController] 리스폰: Client {Owner?.ClientId} → {newPos} (무적 {respawnInvincibleTime}s)");
        }

        // ─────────────── 파워업 시스템 ───────────────

        /// <summary>
        /// 서버에서 호출: 파워업 적용. PowerUpCube.OnTriggerEnter → 여기로 호출.
        /// </summary>
        public void ApplyPowerUp(PowerUpType type)
        {
            if (!IsServerInitialized) return;
            activePowerUp.Value = (byte)type;
            powerUpExpiry = Time.time + powerUpDuration;
            Debug.Log($"[MazePlayerController] 파워업 획득: {type} (Client: {Owner?.ClientId}, {powerUpDuration}초)");
        }

        /// <summary>
        /// 서버 Update에서 호출: 파워업 시간 만료 체크.
        /// </summary>
        private void CheckPowerUpExpiry()
        {
            if (activePowerUp.Value != 255 && Time.time >= powerUpExpiry)
            {
                Debug.Log($"[MazePlayerController] 파워업 만료 (Client: {Owner?.ClientId})");
                activePowerUp.Value = 255;
            }
        }

        // ─────────────── HP 바 UI (World Space) ───────────────

        private void OnHpChanged(int prev, int next, bool asServer)
        {
            if (!asServer)
                UpdateHpBarVisual(next);
        }

        private void CreateHpBar()
        {
            hpBarRoot = new GameObject("HpBar");
            hpBarRoot.transform.SetParent(transform);
            hpBarRoot.transform.localPosition = new Vector3(0f, 2.5f, 0f);

            var canvas = hpBarRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rt = hpBarRoot.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(200f, 20f);
            rt.localScale = new Vector3(0.005f, 0.005f, 0.005f);

            // 배경 (어두운 바)
            var bg = new GameObject("BG");
            bg.transform.SetParent(hpBarRoot.transform, false);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.1f, 0.1f, 0.1f, 0.7f);
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            // 10칸 세그먼트
            hpSegments = new Image[maxHp];
            float segW = 1f / maxHp;
            for (int i = 0; i < maxHp; i++)
            {
                var seg = new GameObject($"HP_{i}");
                seg.transform.SetParent(hpBarRoot.transform, false);
                var img = seg.AddComponent<Image>();
                img.color = Color.green;
                var segRt = seg.GetComponent<RectTransform>();
                segRt.anchorMin = new Vector2(i * segW, 0f);
                segRt.anchorMax = new Vector2((i + 1) * segW, 1f);
                segRt.offsetMin = new Vector2(1f, 1f);
                segRt.offsetMax = new Vector2(-1f, -1f);
                hpSegments[i] = img;
            }
        }

        private void UpdateHpBarVisual(int hp)
        {
            if (hpSegments == null) return;

            Color activeColor = hp > 6 ? Color.green : hp > 3 ? Color.yellow : Color.red;
            Color depletedColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);

            for (int i = 0; i < hpSegments.Length; i++)
            {
                if (hpSegments[i])
                    hpSegments[i].color = i < hp ? activeColor : depletedColor;
            }
        }

        // ─────────────── 닉네임 UI (World Space) ───────────────

        private void OnNicknameChanged(string prev, string next, bool asServer)
        {
            if (!asServer)
                UpdateNicknameText(next);
        }

        /// <summary>
        /// 닉네임 텍스트 UI 생성 (World Space Canvas, HP 바 위).
        /// CreateHpBar()와 동일한 패턴.
        /// </summary>
        private void CreateNicknameUI()
        {
            nicknameRoot = new GameObject("NicknameUI");
            nicknameRoot.transform.SetParent(transform);
            nicknameRoot.transform.localPosition = new Vector3(0f, 2.9f, 0f);

            var canvas = nicknameRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rt = nicknameRoot.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(300f, 40f);
            rt.localScale = new Vector3(0.005f, 0.005f, 0.005f);

            var textObj = new GameObject("NicknameText");
            textObj.transform.SetParent(nicknameRoot.transform, false);

            nicknameText = textObj.AddComponent<TextMeshProUGUI>();
            nicknameText.text = "";
            nicknameText.fontSize = 28;
            nicknameText.alignment = TextAlignmentOptions.Center;
            nicknameText.color = Color.white;
            nicknameText.fontStyle = FontStyles.Bold;

            var textRt = textObj.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
        }

        private void UpdateNicknameText(string name)
        {
            if (nicknameText)
                nicknameText.text = name ?? "";
        }

        // ─────────────── 파워업 비주얼 ───────────────

        private void OnPowerUpChanged(byte prev, byte next, bool asServer)
        {
            if (!asServer)
                UpdatePowerUpVisual(next);
        }

        /// <summary>
        /// 파워업 활성 시 머리 위에 색상 큐브 표시, 비활성 시 제거.
        /// </summary>
        private void UpdatePowerUpVisual(byte type)
        {
            if (powerUpIndicator)
            {
                Destroy(powerUpIndicator);
                powerUpIndicator = null;
            }

            if (type == 255 || type >= PowerUpColors.Length) return;

            powerUpIndicator = GameObject.CreatePrimitive(PrimitiveType.Cube);
            powerUpIndicator.name = "PowerUpIndicator";
            powerUpIndicator.transform.SetParent(transform);
            powerUpIndicator.transform.localPosition = new Vector3(0f, 3.2f, 0f);
            powerUpIndicator.transform.localScale = Vector3.one * 0.3f;

            // 콜라이더 제거 (충돌 방지)
            var col = powerUpIndicator.GetComponent<Collider>();
            if (col) Destroy(col);

            // HDRP: material instance에 직접 색상 설정
            var meshRenderer = powerUpIndicator.GetComponent<MeshRenderer>();
            if (meshRenderer)
            {
                meshRenderer.material.SetColor("_BaseColor", PowerUpColors[type]);
            }
        }

        // ─────────────── 카메라 ───────────────

        private void SetupCamera()
        {
            if (!playerCamera) return;
            cameraTransform = playerCamera.transform;
            cameraTransform.SetParent(null);
            UpdateCameraPosition();
            cameraTransform.rotation = Quaternion.Euler(55f, 0f, 0f);
        }

        private void UpdateCameraPosition()
        {
            if (!cameraTransform) return;
            cameraTransform.position = transform.position + cameraOffset;
        }

        // ─────────────── Update ───────────────

        private void Update()
        {
            // 서버: 파워업 만료 체크
            if (IsServerInitialized)
                CheckPowerUpExpiry();

            if (!IsOwner) return;
            if (Time.timeScale <= 0f) return; // Pause 시그널(timeScale=0) 중 입력 차단
            HandleMovement();
            HandleShooting();
        }

        private void LateUpdate()
        {
            if (IsOwner)
                UpdateCameraPosition();

            // HP 바 + 닉네임 빌보드: 항상 카메라를 향하도록 (Camera.main 캐싱)
            if (hpBarRoot || nicknameRoot)
            {
                if (!billboardCamera) billboardCamera = Camera.main;
                if (billboardCamera)
                {
                    Vector3 fwd = billboardCamera.transform.forward;
                    if (hpBarRoot)
                        hpBarRoot.transform.forward = fwd;
                    if (nicknameRoot)
                        nicknameRoot.transform.forward = fwd;
                }
            }

            // 파워업 인디케이터 회전
            if (powerUpIndicator)
            {
                powerUpIndicator.transform.Rotate(Vector3.up, 180f * Time.deltaTime);
            }
        }

        // ─────────────── 이동 ───────────────

        private void HandleMovement()
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            Vector3 inputDir = new Vector3(h, 0f, v).normalized;

            if (inputDir.sqrMagnitude > 0.01f)
            {
                agent.velocity = inputDir * moveSpeed;
                Quaternion targetRot = Quaternion.LookRotation(inputDir);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }
            else
            {
                agent.velocity = Vector3.zero;
            }
        }

        // ─────────────── 발사 ───────────────

        private void HandleShooting()
        {
            if (!Input.GetMouseButtonDown(0)) return;
            if (Time.time - lastShootTime < shootCooldown) return;
            if (!playerCamera) return;

            Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

            if (groundPlane.Raycast(ray, out float distance))
            {
                Vector3 hitPoint = ray.GetPoint(distance);
                Vector3 direction = (hitPoint - transform.position).normalized;
                direction.y = 0f;

                Vector3 spawnPos = transform.position + Vector3.up * 1.2f + direction * 0.8f;
                lastShootTime = Time.time;
                CmdShootBomb(spawnPos, direction);
            }
        }

        /// <summary>
        /// [Client → Server] 폭탄 스폰 요청. 파워업 효과 적용.
        /// </summary>
        [ServerRpc]
        private void CmdShootBomb(Vector3 spawnPos, Vector3 direction)
        {
            if (!bombPrefab) return;
            NetworkManager nm = NetworkManager;
            if (!nm) return;

            NetworkObject bombNob = nm.GetPooledInstantiated(bombPrefab, spawnPos, Quaternion.identity, true);
            nm.ServerManager.Spawn(bombNob);

            if (bombNob.TryGetComponent(out CubeBomb bomb))
            {
                float rangeMultiplier = 1f;
                float sizeMultiplier = 1f;
                float durationMultiplier = 1f;

                if (activePowerUp.Value != 255)
                {
                    switch ((PowerUpType)activePowerUp.Value)
                    {
                        case PowerUpType.Red:
                            sizeMultiplier = 3f;
                            break;
                        case PowerUpType.Blue:
                            rangeMultiplier = 2f;
                            break;
                        case PowerUpType.Green:
                            durationMultiplier = 1.5f;
                            break;
                    }
                }

                Vector3 velocity = direction * throwForce * rangeMultiplier + Vector3.up * throwArcForce;
                bomb.Launch(velocity, NetworkObject, sizeMultiplier, durationMultiplier);
            }
        }

        // ─────────────── 정리 ───────────────

        private void OnDestroy()
        {
            if (cameraTransform && IsOwner)
                Destroy(cameraTransform.gameObject);

            if (powerUpIndicator)
                Destroy(powerUpIndicator);

            if (nicknameRoot)
                Destroy(nicknameRoot);
        }
    }
}
