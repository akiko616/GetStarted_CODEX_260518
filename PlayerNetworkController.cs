using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using TMPro;

namespace GameInstancePlugin.Client
{
    /// <summary>
    /// FishNet을 통해 스폰되는 실제 플레이어 객체의 코어 로직
    /// - 최신 FishNet V4+의 SyncVar<T> 클래스 방식 적용
    /// 레퍼런스형 스크립트
    /// </summary>
    public class PlayerNetworkController : NetworkBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TextMeshProUGUI nicknameText;

        // FishNet: SyncVar<T> 클래스 방식 사용 (반드시 readonly로 선언)
        public readonly SyncVar<int> PlayerId = new SyncVar<int>();
        public readonly SyncVar<string> Nickname = new SyncVar<string>();
        private readonly SyncVar<float> _health = new SyncVar<float>(100f); // 초기 체력

        private void Awake()
        {
            // 이벤트 구독 (값이 변경될 때 콜백 발생)
            PlayerId.OnChange += OnPlayerIdChanged;
            Nickname.OnChange += OnNicknameChanged;
            _health.OnChange += OnHealthChanged;
        }

        // --- 콜백 메서드들 ---
        private void OnPlayerIdChanged(int oldValue, int newValue, bool asServer)
        {
            // ID 갱신 로직 (보통 닉네임과 묶어서 처리)
        }

        private void OnNicknameChanged(string oldValue, string newValue, bool asServer)
        {
            // 닉네임 UI 업데이트
            if (nicknameText != null)
            {
                nicknameText.text = newValue;
            }
            gameObject.name = $"Player_{newValue}";
        }

        private void OnHealthChanged(float oldValue, float newValue, bool asServer)
        {
            // 서버/클라이언트 모두 로그가 찍히거나 체력바 UI가 갱신됨
            if (!asServer) 
            {
                Debug.Log($"[클라이언트] {Nickname.Value} 체력 변경: {oldValue} -> {newValue}");
            }
        }

        // --- 라이프 사이클 ---
        public override void OnStartServer()
        {
            base.OnStartServer();
            // 서버 시작 시 초기 세팅 확인 (값 접근 시 .Value 사용)
            Debug.Log($"[PlayerNetworkController] 서버 스폰: ID={PlayerId.Value}, Nick={Nickname.Value}, Health={_health.Value}");
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // 클라이언트에 스폰될 때 닉네임 UI 초기화
            if (nicknameText != null)
            {
                nicknameText.text = Nickname.Value;
            }
            gameObject.name = $"Player_{Nickname.Value}";

            if (IsOwner)
            {
                Debug.Log($"[PlayerNetworkController] 내 캐릭터({Nickname.Value}) 스폰됨. 조작 권한 획득.");
            }
            else
            {
                Debug.Log($"[PlayerNetworkController] 원격 캐릭터({Nickname.Value}) 스폰됨.");
            }
        }

        // --- 예시: RPC 통신 (피격) ---
        [ServerRpc]
        public void RpcTakeDamage(float damage)
        {
            // 값 할당 시에도 .Value 를 사용합니다
            _health.Value -= damage;
            
            if (_health.Value <= 0)
            {
                _health.Value = 0;
                Debug.Log($"[서버] {Nickname.Value} 사망!");
                
                // 캐릭터 사망 알림 브로드캐스트 또는 ObserversRpc 전송
                RpcOnDeath();
            }
        }

        [ObserversRpc]
        private void RpcOnDeath()
        {
            Debug.Log($"[이펙트] {Nickname.Value} 사망 애니메이션 재생!");
        }
    }
}
