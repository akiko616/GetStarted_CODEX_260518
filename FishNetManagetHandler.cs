using System;
using UnityEngine;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Connection;
using FishNet.Broadcast;
using System.Collections.Generic;

namespace GameInstancePlugin.Client
{
    // --- 통신용 구조체 (브로드캐스트) ---
    public struct AuthBroadcastData : IBroadcast
    {
        public int PlayerId;
        public int PartyId;
        public string Role;
    }

    /// <summary>클라이언트가 씬 로딩 등 준비 완료 후 서버에 전송하는 브로드캐스트</summary>
    public struct PlayerReadyBroadcast : IBroadcast
    {
        public int PlayerId;
    }

    /// <summary>
    /// FishNet 클라이언트 접속 관리 + 인증(AuthBroadcast) + 플레이어 스폰 처리.
    /// 독립형: Client 클래스를 참조하지 않음. Handler가 RequiredPartyId를 설정하여 파티 검증.
    ///
    /// ── 스폰 오버라이드 훅 시스템 ──

    ///
    /// 훅 동작 (OnSpawnPlayerOverride 설정 시):
    ///   기본 스폰 로직을 완전히 대체합니다. 외부 게임 매니저가 자체 스폰 로직
    ///   (맵별 위치, 커스텀 프리팹 등)을 주입할 수 있습니다.
    /// </summary>
    public class FishNetManagetHandler : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private NetworkManager fishnetManager;

        /// <summary>
        /// 플레이어 스폰을 대체하는 커스텀 훅.
        /// 이 델리게이트가 설정되어 있으면 기본 스폰 로직 대신 호출됩니다.
        ///
        /// 매개변수:
        ///   - NetworkConnection: 인증된 클라이언트 연결
        ///   - AuthBroadcastData: PlayerId, PartyId, Role 등 인증 정보
        ///   - NetworkManager: FishNet 매니저 (GetPooledInstantiated/Spawn 호출용)
        ///
        /// 훅 설정자는 OnEnable에서 등록하고 OnDisable에서 해제해야 합니다.
        /// </summary>
        public Action<NetworkConnection, AuthBroadcastData, NetworkManager> OnSpawnPlayerOverride { get; set; }

        public Action<GameInstancePlugin.TrainResult> OnTrainResultPushRequest { get; set; }

        /// <summary>파티 검증용 ID. DedicateServerHandler가 설정. 0이면 검증 안 함.</summary>
        public int RequiredPartyId { get; set; }

        /// <summary>준비 완료 플레이어 수 변경 이벤트 (총 카운트)</summary>
        public event Action<int> OnReadyPlayerCountChanged;

        /// <summary>인증된 플레이어 수</summary>
        public int AuthenticatedPlayerCount => authenticatedPlayers.Count;

        /// <summary>준비 완료된 플레이어 수</summary>
        public int ReadyPlayerCount => readyPlayers.Count;

        public int ConnectedPlayerCount { get; private set; } = 0;
        private Dictionary<NetworkConnection, int> authenticatedPlayers = new Dictionary<NetworkConnection, int>();
        private HashSet<NetworkConnection> readyPlayers = new HashSet<NetworkConnection>();

        private void Awake()
        {
            if (fishnetManager == null) fishnetManager = FindFirstObjectByType<NetworkManager>();
        }

        private void OnEnable()
        {
            if (fishnetManager != null)
            {
                fishnetManager.ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;
                fishnetManager.ServerManager.RegisterBroadcast<AuthBroadcastData>(OnAuthBroadcastReceived);
                fishnetManager.ServerManager.RegisterBroadcast<PlayerReadyBroadcast>(OnPlayerReadyReceived);
            }
        }

        private void OnDisable()
        {
            if (fishnetManager != null)
            {
                if (fishnetManager.ServerManager != null)
                {
                    fishnetManager.ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;
                    fishnetManager.ServerManager.UnregisterBroadcast<AuthBroadcastData>(OnAuthBroadcastReceived);
                    fishnetManager.ServerManager.UnregisterBroadcast<PlayerReadyBroadcast>(OnPlayerReadyReceived);
                }
            }
        }

        private void ServerManager_OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == RemoteConnectionState.Started)
            {
                ConnectedPlayerCount++;
                Debug.Log($"[SessionConnectionHandler] 클라이언트 접속됨. ConnectionID: {connection.ClientId}");
            }
            else if (args.ConnectionState == RemoteConnectionState.Stopped)
            {
                ConnectedPlayerCount--;
                readyPlayers.Remove(connection);
                if (authenticatedPlayers.ContainsKey(connection))
                {
                    authenticatedPlayers.Remove(connection);
                }
            }
        }

        private void OnAuthBroadcastReceived(NetworkConnection conn, AuthBroadcastData data, Channel channel)
        {
            if (RequiredPartyId != 0 && data.PartyId != RequiredPartyId)
            {
                Debug.LogWarning($"[Security] 잘못된 파티 유저 킥 (인증 실패): ConnectionID {conn.ClientId}");
                conn.Disconnect(true);
                return;
            }

            if (!authenticatedPlayers.ContainsKey(conn))
            {
                authenticatedPlayers.Add(conn, data.PlayerId);
                SpawnPlayerForConnection(conn, data);
            }
        }
        
        private void OnPlayerReadyReceived(NetworkConnection conn, PlayerReadyBroadcast data, Channel channel)
        {
            if (!authenticatedPlayers.ContainsKey(conn))
            {
                Debug.LogWarning($"[FishNetManagetHandler] 미인증 클라이언트의 Ready 수신 무시: {conn.ClientId}");
                return;
            }

            if (readyPlayers.Add(conn))
            {
                Debug.Log($"[FishNetManagetHandler] 플레이어 준비 완료: {conn.ClientId} ({readyPlayers.Count}명)");
                OnReadyPlayerCountChanged?.Invoke(readyPlayers.Count);
            }
        }

        private void SpawnPlayerForConnection(NetworkConnection conn, AuthBroadcastData data)
        {
            // viewer는 관전 전용 — 캐릭터 생성하지 않음
            if (string.Equals(data.Role, "viewer", System.StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"[SessionConnectionHandler] 관전자 접속 (스폰 스킵): {conn.ClientId}");
                return;
            }

            if (OnSpawnPlayerOverride != null)
            {
                OnSpawnPlayerOverride.Invoke(conn, data, fishnetManager);
                return;
            }
        }

    }
}
