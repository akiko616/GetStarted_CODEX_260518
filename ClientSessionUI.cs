using UnityEngine;
using FishNet.Managing;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;

namespace GameInstancePlugin.Client
{
    /// <summary>
    /// GameSessionManager의 RpcNotifySessionEnd를 받아 로비로 복귀하는 클라이언트 측 로직.
    /// (일반적으로 UI 매니저나 씬 전환 매니저에 연결됨)
    /// </summary>
    public class ClientSessionUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private NetworkManager fishnetManager;
        [SerializeField] private string lobbySceneName = "LobbyScene"; // 돌아갈 씬 이름

        private void Awake()
        {
            if (fishnetManager == null) fishnetManager = FindObjectOfType<NetworkManager>();
        }

        /// <summary>
        /// (서버의 RpcNotifySessionEnd 내부 등에서 호출됨) 
        /// 서버가 방을 파괴했으므로 안내 메시지를 띄우고 로비로 돌아갑니다.
        /// </summary>
        public void ShowEndScreenAndReturn(string message)
        {
            Debug.Log($"[ClientSessionUI] 세션 종료 처리 중... 사유: {message}");

            // TODO: 암전/페이드아웃 UI 연출

            ReturnToLobbyAsync().Forget();
        }

        private async UniTaskVoid ReturnToLobbyAsync()
        {
            // 약간 대기 (페이드아웃 시간 확보 등)
            await UniTask.Delay(1000);

            // 클라이언트 접속 강제 종료 (어차피 닫힐 거지만 확실하게 처리)
            if (fishnetManager != null && fishnetManager.ClientManager != null)
            {
                fishnetManager.ClientManager.StopConnection();
            }

            // DarkRift 베이스 로비 씬으로 되돌아감
            Debug.Log("[ClientSessionUI] 로비 씬으로 귀환합니다.");
            // SceneManager.LoadScene(lobbySceneName); 
        }
    }
}
