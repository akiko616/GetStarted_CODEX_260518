using DarkRift;
using DrillSergeant;
using FishNet.Connection;
using FishNet.Transporting;
using GameInstancePlugin.Client;
using System;
using UnityEngine;

namespace TRAINEE
{
    public partial class NetworkManager
    {
        private int _currentPartyId = 0;
        private string _currentAccountId = string.Empty;
        public int CurrentPartyId => _currentPartyId;
        public string CurrentAccountId => _currentAccountId;

        public Action<NetworkConnection, AuthBroadcastData, FishNet.Managing.NetworkManager> OnSpawnPlayerOverride { get; set; }

        private bool _isLobby = false;
        public bool IsLobby => _isLobby;
        public async Cysharp.Threading.Tasks.UniTaskVoid OnLobbyConnection(string address, int port)
        {
            Debug.LogWarning("로컬 서버 연결...");
            // 이전 피쉬넷 연결 해제
            if (_fishNetManager.ServerManager.Started)
            {
                DisconnectFishNet();

                await Cysharp.Threading.Tasks.UniTask.WaitUntil(() => !_fishNetManager.ServerManager.Started);

                await Cysharp.Threading.Tasks.UniTask.Delay(100);
            }

            OnSetTransportAddress(_fishNetManager.TransportManager.Transport, address, (ushort)port);

            _fishNetManager.ServerManager.StartConnection();
            _fishNetManager.ClientManager.StartConnection();

            _isLobby = true;
        }

        public async Cysharp.Threading.Tasks.UniTaskVoid OnConnectToFishNet(string address, int port)
        {
            _isLobby = false;
            if (_fishNetManager == null)
            {
                if(!OnFindToFishNet())
                {
                    Debug.LogError($"Fish Net 서버가 할당이 안되어있습니다. / _fishNetManager : {_fishNetManager} ");
                    return;
                }
            }

            // 이전 피쉬넷 연결 해제
            if (_fishNetManager.ServerManager.Started)
            {
                DisconnectFishNet();

                await Cysharp.Threading.Tasks.UniTask.WaitUntil(() => !_fishNetManager.ServerManager.Started);

                await Cysharp.Threading.Tasks.UniTask.Delay(100);
            }

            if (_isConnectedToFishNet)
            {
                Debug.LogWarning("Fish Net 서버에 연결되어 있습니다");
                return;
            }

            Debug.Log($"Fish Net 서버에 연결 시도중");

            OnSetTransportAddress(_fishNetManager.TransportManager.Transport, address, (ushort)port);
            RegisterFishNetBroadcasts();

            _fishNetManager.ClientManager.StartConnection();

            OnTrainingCreatedSuccess.Invoke();
        }

        private bool OnFindToFishNet()
        {
            _fishNetManager = FishNet.Managing.NetworkManager.Instances[0];

            if(_fishNetManager != null)
            {
                return true;
            }

            return false;
        }

        private void OnSetTransportAddress(Transport transport, string address, ushort port)
        {
            if (transport == null)
            {
                Debug.LogError($"Transport가 할당이 안되어있습니다. / transport : {transport} ");
                return;
            }

            transport.SetClientAddress(address);
            transport.SetPort(port);

            Debug.Log($"Transport 설정됨: {address}:{port}");
        }

        public void DisconnectFishNet()
        {
            if (_fishNetManager != null)
            {
                _fishNetManager.ClientManager.OnClientConnectionState -= OnFishNetConnectionState;
                UnregisterFishNetBroadcasts();

                if (_fishNetManager.ClientManager.Started)
                    _fishNetManager.ClientManager.StopConnection();

                if (_fishNetManager.ServerManager.Started)
                    _fishNetManager.ServerManager.StopConnection(true);
            }
        }

        private void OnFishNetConnectionState(ClientConnectionStateArgs args)
        {
            switch (args.ConnectionState)
            {
                case LocalConnectionState.Started:
                    _isConnectedToFishNet = true;

#if DrillSergeant
                    SendAuthBroadcast();
#else
                    if (!_isLobby)
                    {
                        SendAuthBroadcast();
                    }
#endif
                    Debug.Log("Fishnet 게임 서버 연결됨");
                    break;
                case LocalConnectionState.Stopped:
                    _isConnectedToFishNet = false;
                    UnregisterFishNetBroadcasts();
                    Debug.Log("Fishnet 게임 서버 연결 끊김");
                    break;

                case LocalConnectionState.Starting:
                    Debug.Log("Fishnet 게임 서버 연결 중...");
                    break;

                case LocalConnectionState.Stopping:
                    Debug.Log("Fishnet 게임 서버 연결 종료 중...");
                    break;
                   
            }
        }

        private void RegisterFishNetBroadcasts()
        {
            if (_fishNetManager != null)
            {
                _fishNetManager.ClientManager.OnClientConnectionState += OnFishNetConnectionState;
                _fishNetManager.ClientManager.RegisterBroadcast<SessionStartBroadcast>(OnSessionStartReceived);
                _fishNetManager.ClientManager.RegisterBroadcast<SessionEndBroadcast>(OnSessionEndReceived);
            }
        }

        private void UnregisterFishNetBroadcasts()
        {
            if (_fishNetManager != null && _fishNetManager.ClientManager != null)
            {
                _fishNetManager.ClientManager.OnClientConnectionState -= OnFishNetConnectionState;
                _fishNetManager.ClientManager.UnregisterBroadcast<SessionStartBroadcast>(OnSessionStartReceived);
                _fishNetManager.ClientManager.UnregisterBroadcast<SessionEndBroadcast>(OnSessionEndReceived);
            }
        }

        private void SendAuthBroadcast()
        {
            AuthBroadcastData authData = new AuthBroadcastData
            {
#if DrillSergeant
                PlayerId = ServerHubManager.Instance.mainLoginedInput.GetHashCode(),
                PartyId = DrillManager.Instance.DrillUnit.PartyId,
                Role = DrillManager.Instance.SergeantRole
#else
                PlayerId = _currentAccountId.GetHashCode(),
                PartyId = _currentPartyId,
                Role = GameManager.Instance.PlayerRole
#endif
            };

            Debug.Log($"[NetworkManager] 서버로 인증 브로드캐스트 전송 (PlayerId: {authData.PlayerId}, PartyId: {authData.PartyId})");
            _fishNetManager.ClientManager.Broadcast(authData);
        }

        public void SendTrainingCreated()
        {
            Debug.Log($"훈련 정보 및 모델 로딩 끝 준비 완료 상태 송신 : {_currentAccountId}");

            if(string.IsNullOrEmpty(_currentAccountId))
            {
                return;
            }

            var readyData = new PlayerReadyBroadcast
            {
                PlayerId = _currentAccountId.GetHashCode()
            };

            Debug.Log("[TraineeClientHandler] PlayerReadyBroadcast 전송 (준비 완료)");

            _fishNetManager.ClientManager.Broadcast(readyData);
        }

        // [핵심 2] 교관이 훈련을 시작했을 때 수신
        private void OnSessionStartReceived(SessionStartBroadcast broadcast, Channel channel)
        {
            Debug.Log("[NetworkManager] 세션 시작 신호 수신! 훈련 시작.");
        }

        // 훈련이 종료되었을 때 수신
        private void OnSessionEndReceived(SessionEndBroadcast broadcast, Channel channel)
        {
            Debug.Log($"[NetworkManager] 세션 종료 신호 수신: {broadcast.Message}");
        }

        public bool OnResTrainingPinInfo(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                NetworkResponeData.ResTrainingPinInfoData resData = _reader.ReadSerializable<NetworkResponeData.ResTrainingPinInfoData>();

                Debug.Log($"[네트워크PinInfo] 표적지시 핀 수신 완료: 파티 {resData.partyId}, 타입 {resData.pinType}");

                OnTrainingPinInfoReceived?.Invoke(resData);

                return true;
            }

            Debug.LogError($"[네트워크PinInfo] 표적지시 핀 파싱 실패: {_reader}");
            return false;
        }
    }
}
