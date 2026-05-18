using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Triggers;
using DarkRift;
using DarkRift.Client;
using DarkRift.Client.Unity;
using DatabasePlugin;
using Disaster.Network.FTP;
using System;
using System.Collections.Generic;
using UnityEngine;


namespace TRAINEE
{
    public partial class NetworkManager : Singleton<NetworkManager>
    {
        public delegate bool PacketParserDelegate(DarkRiftReader _reader);
        public delegate bool PacketReadDelegate(DarkRiftReader _reader);

        [SerializeField] private string _host = "";
        [SerializeField] private ushort _port;

        [SerializeField] private UnityClient _client = null;
        [SerializeField] private FishNet.Managing.NetworkManager _fishNetManager = null;
        [SerializeField] private FtpTransferManager _ftpTransferManager = null;

        private Dictionary<EProtocolCode, PacketReadDelegate> _packetDispatcher = new Dictionary<EProtocolCode, PacketReadDelegate>();

        private bool _isAdmin = false;
        private bool _isConnectedToGameServer = false;
        private bool _isConnectedToNetwork = false;

        private AccountResponse _currentAccount = null;



        private int _minPlayer = 1;
        private int _maxPlayer = 16;
        private int _defaultPlayer = 4;

        private string _fishNetAddress;
        private ushort _fishNetPort;
        private bool _isConnectedToFishNet = false;


        public bool IsConnectedToNetwork => _isConnectedToNetwork;

        public FishNet.Managing.NetworkManager FishNetManager => _fishNetManager;
        public FishNet.Managing.Client.ClientManager ClientManager => _fishNetManager.ClientManager;
        public FishNet.Managing.Server.ServerManager FishNetServerManager => _fishNetManager.ServerManager;
        public FishNet.Managing.Timing.TimeManager TimeManaager => _fishNetManager?.TimeManager;
        public bool IsServer => _fishNetManager != null && _fishNetManager.ServerManager.Started;

        public FtpTransferManager FTPManager => _ftpTransferManager;
        protected override void Awake()
        {
            base.Awake();
        }
        protected override void Start()
        {
            base.Start();
            Init();
        }

        protected override void OnDestroy()
        {
            _ftpTransferManager.DisconnectAsync().Forget();
            DisconnectFishNet();
            base.OnDestroy();
        }

        private async void OnApplicationQuit()
        {
            OnClientEventUnResgister();
            await NetworkManager.Instance.FTPManager.DisconnectAsync();
            Debug.Log("FTP DisconnectAsync 완료");
        }
        
        protected override void Init()
        {
            base.Init();
            OnClientSetup();

            RegistPacketDispatcher();
        }

        public void OnSend<T>(EProtocolCode protocol, T data) where T : struct, IDarkRiftSerializable
        {
            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                writer.Write<T>(data);

                using (Message message = Message.Create((ushort)protocol, writer))
                {
                    _client.SendMessage(message, SendMode.Reliable);

                    Debug.Log($"OnSend : {protocol}");
                }
            }
        }

        private void RegistPacketDispatcher()
        {
#if DrillSergeant
            AddPacketDispatcher(EProtocolCode.LOGIN, OnResLogin);
            AddPacketDispatcher(EProtocolCode.PARTY_CONFIRM, OnResPartyConfirm);
            AddPacketDispatcher(EProtocolCode.TRAINING_PAUSE, OnResTrainingPause);
            AddPacketDispatcher(EProtocolCode.TRAINING_STOP, OnResTrainingStop);
            
            AddPacketDispatcher(EProtocolCode.TRAINING_INFO, OnResTraininigInfo);
            AddPacketDispatcher(EProtocolCode.INSTANCE_SIGNAL_BROADCAST, OnResInstanceSignalBroadcast);
            AddPacketDispatcher(EProtocolCode.TRAINING_BEGIN, OnResTrainingBegin);
            AddPacketDispatcher(EProtocolCode.TRAINING_WAKE_RESULT, OnResTrainingWakeResult);
            AddPacketDispatcher(EProtocolCode.TRAINING_CREATED, OnResTrainingCreated);
            AddPacketDispatcher(EProtocolCode.TRAINING_BEGIN_READY, OnResTrainingBeginReady);

            //AddPacketDispatcher(EProtocolCode.GET_ACCOUNT, OnResGetAccount);
            //AddPacketDispatcher(EProtocolCode.CHANGE_PASSWORD, OnResChangePassword);
            //AddPacketDispatcher(EProtocolCode.ADMIN_PARTY_NOTIFICATION, OnResAdminPartyNotification);
            //AddPacketDispatcher(EProtocolCode.ADMIN_GET_ALL_INSTANCES, OnResAdminGetAllInstances);
            //AddPacketDispatcher(EProtocolCode.ADMIN_SPECTATE_REQUEST, OnResAdminSpectateRequest);
            //AddPacketDispatcher(EProtocolCode.TRAINING_SETUP, OnResTrainingSetup);
            //AddPacketDispatcher(EProtocolCode.TRAINING_STATUS, OnResTrainingStatus);

            AddPacketDispatcher(EProtocolCode.REGISTER, OnResRegisterAccount);
            AddPacketDispatcher(EProtocolCode.UPDATE_ACCOUNT, OnResUpdateAccount);
            AddPacketDispatcher(EProtocolCode.DELETE_ACCOUNT, OnResDeleteAccount);
            AddPacketDispatcher(EProtocolCode.GET_TYPE_ACCOUNTS, OnResGetTypeAccount);
            AddPacketDispatcher(EProtocolCode.PARTY_CREATE, OnResPartyCreate);
            AddPacketDispatcher(EProtocolCode.PARTY_DESTROY, OnResPartyDestory);
            AddPacketDispatcher(EProtocolCode.PARTY_MOVE, OnResPartyMove);
            AddPacketDispatcher(EProtocolCode.PARTY_CHANGE_ROLE, OnResPartyChangeRole);
            AddPacketDispatcher(EProtocolCode.PARTY_CHANGE_SETTING, OnResPartyChangeSetting);
            AddPacketDispatcher(EProtocolCode.PARTY_GET_LIST, OnResPartyListGet);
            AddPacketDispatcher(EProtocolCode.PARTY_JOIN, OnResPartyJoin);
            AddPacketDispatcher(EProtocolCode.PARTY_LEAVE, OnResPartyLeave);
            AddPacketDispatcher(EProtocolCode.ADMIN_USER_LOGIN_NOTIFICATION, OnResAdminUserLoginNotification);
            AddPacketDispatcher(EProtocolCode.ADMIN_USER_LOGOUT_NOTIFICATION, OnResAdminUserLogOutNotification);
            AddPacketDispatcher(EProtocolCode.ADMIN_GET_ONLINE_USERS, OnResAdminGetOnlineUsers);
            AddPacketDispatcher(EProtocolCode.TRAINING_MIDLEAVE, OnResTrainingMidLeave);
            AddPacketDispatcher(EProtocolCode.TRAINING_MIDJOIN, OnResTrainingMidJoin);
            AddPacketDispatcher(EProtocolCode.TRAINING_RESULT, OnResTrainingResult);
            AddPacketDispatcher(EProtocolCode.TRAINING_GRACEFUL_QUIT, OnResTrainingGracefulQuit);
            AddPacketDispatcher(EProtocolCode.TRAINING_PININFO, OnResTrainingPinInfo);
#else
            AddPacketDispatcher(EProtocolCode.LOGIN, OnResLogin);
            AddPacketDispatcher(EProtocolCode.REGISTER, OnResRegisterAccount);
            AddPacketDispatcher(EProtocolCode.TRAINING_INFO, OnResTraininigInfo);
            AddPacketDispatcher(EProtocolCode.INSTANCE_SIGNAL_BROADCAST, OnResInstanceSignalBroadcast);
            AddPacketDispatcher(EProtocolCode.PARTY_CONFIRM, OnResPartyConfirm);
            AddPacketDispatcher(EProtocolCode.TRAINING_BEGIN, OnResTrainingBegin);
            AddPacketDispatcher(EProtocolCode.TRAINING_WAKE_RESULT, OnResTrainingWakeResult);
            AddPacketDispatcher(EProtocolCode.TRAINING_CREATED, OnResTrainingCreated);
            AddPacketDispatcher(EProtocolCode.TRAINING_BEGIN_READY, OnResTrainingBeginReady);
            AddPacketDispatcher(EProtocolCode.TRAINING_PAUSE, OnResTrainingPause);
            AddPacketDispatcher(EProtocolCode.TRAINING_STOP, OnResTrainingStop);
            AddPacketDispatcher(EProtocolCode.TRAINING_RESULT, OnResTrainingResult);
            AddPacketDispatcher(EProtocolCode.TRAINING_PININFO, OnResTrainingPinInfo);
#endif
        }

        private void AddPacketDispatcher(EProtocolCode _key, PacketReadDelegate _action)
        {

            if (_packetDispatcher.ContainsKey(_key))
            {
                Debug.LogError("AddPacketDispatcher Already has key");
                return;
            }

            _packetDispatcher.Add(_key, _action);
        }


        private void OnClientSetup()
        {
            _client.Host = _host;
            _client.Port = _port;

            OnClientEventResgister();
            OnConnectedClient();
        }

        private void OnClientEventResgister()
        {
            _client.MessageReceived += OnMessageReceived;
            _client.Disconnected += OnLogicClientDisconnected;
        }

        private void OnClientEventUnResgister()
        {
            _client.MessageReceived -= OnMessageReceived;
            _client.Disconnected -= OnLogicClientDisconnected;
        }

        private void OnConnectedClient()
        {
            if (_client != null)
            {
                _client.ConnectInBackground(_host, _port, true, OnLogicClientConnected);
            }
        }

        private void OnLogicClientConnected(Exception e)
        {
            Debug.Log($"[TraineeClient] DarkRift 서버에 연결됨 {e}");

            _isConnectedToNetwork = true;
        }

        private void OnLogicClientDisconnected(object sender, DisconnectedEventArgs e)
        {
            Debug.Log($"[TraineeClient] DarkRift 서버 연결 해제됨 (LocalDisconnect: {e.LocalDisconnect})");

            _isConnectedToNetwork = false;
        }

        private void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            using (Message message = e.GetMessage())
            {
                EProtocolCode protocol = (EProtocolCode)message.Tag;

                using (DarkRiftReader reader = message.GetReader())
                {
                    if (_packetDispatcher.TryGetValue(protocol, out PacketReadDelegate handler))
                    {
                        handler(reader); // HandleLoginResponse 등이 여기서 실행됨
                    }
                    else
                    {
                        Debug.LogWarning($"[NetworkManager] 등록되지 않은 프로토콜입니다: {protocol}");
                    }
                }
            }
        }

    }
}
