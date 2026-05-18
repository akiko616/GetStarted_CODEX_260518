using System;
using System.Collections.Generic;
using DarkRift;
using DarkRift.Client;
using DarkRift.Client.Unity;
using UnityEngine;
using PartyManagerPlugin;
using GameInstancePlugin;
using MT = DatabasePlugin.MessageTags;

namespace DatabasePlugin.Client
{
    /// <summary>
    /// 교관(Drill Instructor) 전용 클라이언트
    /// - 계정 관리 (로그인, 회원가입, 계정 CRUD)
    /// - 파티 관리 (생성, 파괴, 이동, 역할 변경, 설정 변경)
    /// - 훈련 제어 (훈련 시작)
    /// - 모든 알림 수신 (파티, 사용자 로그인/아웃, 인스턴스)
    /// - 인스턴스 조회 및 관전
    /// </summary>
    public class DrillInstructorClient : MonoBehaviour
    {
        #region Serialized Fields
        [Header("연결 설정")]
        [SerializeField] private UnityClient client;
        #endregion

        #region Private Fields
        private bool isInitialized = false;
        private bool isAdmin = false;
        private DatabasePlugin.AccountResponse currentAccount;
        
        // 캐시
        private List<PartyNotification> notificationHistory = new List<PartyNotification>();
        private List<UserLoginNotification> loginHistory = new List<UserLoginNotification>();
        private Dictionary<string, DatabasePlugin.OnlineUserInfo> cachedOnlineUsers = new Dictionary<string, DatabasePlugin.OnlineUserInfo>();
        private const int MAX_HISTORY = 100;

        // 훈련 시작 준비 완료 플래그 (파티ID → TrainingBeginReady 수신 여부)
        private Dictionary<int, bool> partyBeginReadyFlags = new Dictionary<int, bool>();
        #endregion

        // 태그 → DatabasePlugin.MessageTags (using MT) 사용
        // TrainingType 범위만 로컬 상수 유지 (MessageTags에 Min/Max 없음)
        private const ushort TAG_TRAINING_TYPE_MIN = 1000;
        private const ushort TAG_TRAINING_TYPE_MAX = 1019;

        #region Response Codes
        /// <summary>응답 코드</summary>
        public enum ResponseCode : byte
        {
            Success = 0,
            Failed = 1,
            AlreadyExists = 2,
            NotFound = 3,
            InvalidCredentials = 4,
            DatabaseError = 5,
            WeakPassword = 6,
            InvalidData = 7,
            NotInitialized = 8
        }

        /// <summary>파티 응답 코드</summary>
        public enum PartyResponseCode : byte
        {
            Success = 0,
            Failed = 1,
            NotFound = 2,
            AlreadyInParty = 3,
            InvalidData = 4,
            NoPermission = 5,
            PartyFull = 6
        }

        /// <summary>훈련 응답 코드</summary>
        public enum TrainingResponseCode : byte
        {
            Success = 0,
            Failed = 1,
            NoAvailableInstance = 2,
            PartyNotFound = 3,
            InstanceNotFound = 4,
            AlreadyRunning = 5,
            InvalidData = 6,
            ServerError = 7,
            Unauthorized = 8,
            InstanceError = 9,
            AlreadyExists = 10
        }
        #endregion

        #region Notification Types
        /// <summary>교관 알림 타입</summary>
        public enum InstructorNotificationType : byte
        {
            PartyCreated = 0,
            PartyDestroyed = 1,
            PartyMemberJoined = 2,
            PartyMemberLeft = 3,
            PartyRoleChanged = 4,
            PartySettingChanged = 5
        }

        /// <summary>파티 알림 데이터</summary>
        public class PartyNotification
        {
            public InstructorNotificationType Type { get; set; }
            public Party Party { get; set; }
            public string AffectedAccountId { get; set; }
            public string AdditionalInfo { get; set; }
            public string Timestamp { get; set; }

            public override string ToString()
            {
                return $"[{Timestamp}] {Type}: 파티 {Party?.PartyId.ToString() ?? "null"}, 계정: {AffectedAccountId}, 정보: {AdditionalInfo}";
            }
        }

        // OnlineUserInfo는 DatabasePlugin.OnlineUserInfo (ClientSharedTypes.cs)를 사용

        /// <summary>접속자 목록 응답 데이터</summary>
        public class OnlineUsersResponse
        {
            public List<DatabasePlugin.OnlineUserInfo> Users { get; set; } = new List<DatabasePlugin.OnlineUserInfo>();
            public int TotalCount { get; set; }
            public int AdminCount { get; set; }
            public int UserCount { get; set; }
        }

        /// <summary>사용자 로그인 알림</summary>
        public class UserLoginNotification
        {
            public AccountResponse Account { get; set; }
            public ushort ClientId { get; set; }
            public string Timestamp { get; set; }

            public override string ToString()
            {
                return $"[{Timestamp}] 로그인: {Account?.Id ?? "null"} (Type: {Account?.AccountType}, ClientID: {ClientId})";
            }
        }

        /// <summary>사용자 로그아웃 알림</summary>
        public class UserLogoutNotification
        {
            public string AccountId { get; set; }
            public ushort ClientId { get; set; }
            public string Timestamp { get; set; }

            public override string ToString()
            {
                return $"[{Timestamp}] 로그아웃: {AccountId} (ClientID: {ClientId})";
            }
        }

        /// <summary>파티원 핑/하드웨어 상태 항목</summary>
        public class PingStatusEntry
        {
            public string AccountId { get; set; }
            public int PartyId { get; set; }
            public int PingMs { get; set; }
            public int HardwareStatus { get; set; }
        }

        /// <summary>인스턴스 신호 데이터</summary>
        public class InstanceSignalData
        {
            public int InstanceId { get; set; }
            public int PartyId { get; set; }
            public InstanceSignalType SignalType { get; set; }
            public string Timestamp { get; set; }

            public override string ToString()
            {
                return $"[{Timestamp}] Instance {InstanceId} → Party {PartyId}: {SignalType}";
            }
        }

        /// <summary>훈련 상태 알림 데이터 (파티원 준비 상태)</summary>
        public class TrainingStatusData
        {
            public int PartyId { get; set; }
            public int TotalMembers { get; set; }
            public int ReadyMembers { get; set; }
            public string[] ReadyAccountIds { get; set; }
            public bool AllReady { get; set; }

            public TrainingStatusData()
            {
                PartyId = 0;
                TotalMembers = 0;
                ReadyMembers = 0;
                ReadyAccountIds = Array.Empty<string>();
                AllReady = false;
            }

            public override string ToString()
            {
                return $"TrainingStatus[Party:{PartyId}, Ready:{ReadyMembers}/{TotalMembers}, AllReady:{AllReady}]";
            }
        }

        // TrainingBeginReadyData는 GameInstancePlugin.TrainingBeginReadyData (ClientSharedTypes.cs)를 사용
        #endregion

        #region Events
        // Auth Events
        public event Action<DatabasePlugin.AccountResponse> OnLoginSuccess;
        public event Action<ResponseCode> OnLoginFailed;
        public event Action<DatabasePlugin.AccountResponse> OnRegisterSuccess;
        public event Action<ResponseCode> OnRegisterFailed;
        public event Action<DatabasePlugin.AccountResponse> OnGetAccountSuccess;
        public event Action<ResponseCode> OnGetAccountFailed;
        public event Action<List<DatabasePlugin.AccountResponse>> OnGetTypeAccountsSuccess;
        public event Action<ResponseCode> OnGetTypeAccountsFailed;
        public event Action OnUpdateAccountSuccess;
        public event Action<ResponseCode> OnUpdateAccountFailed;
        public event Action OnDeleteAccountSuccess;
        public event Action<ResponseCode> OnDeleteAccountFailed;
        public event Action OnChangePasswordSuccess;
        public event Action<ResponseCode> OnChangePasswordFailed;

        // Party Events
        public event Action<Party> OnPartyCreateSuccess;
        public event Action<PartyResponseCode> OnPartyCreateFailed;
        public event Action<int> OnPartyDestroySuccess;
        public event Action<PartyResponseCode> OnPartyDestroyFailed;
        public event Action<Party> OnPartyMoveSuccess;
        public event Action<PartyResponseCode> OnPartyMoveFailed;
        public event Action<Party> OnPartyChangeRoleSuccess;
        public event Action<PartyResponseCode> OnPartyChangeRoleFailed;
        public event Action<Party> OnPartyChangeSettingSuccess;
        public event Action<PartyResponseCode> OnPartyChangeSettingFailed;
        public event Action<List<Party>> OnPartyGetListSuccess;
        public event Action<PartyResponseCode> OnPartyGetListFailed;
        public event Action<Party> OnPartyJoinSuccess;
        public event Action<PartyResponseCode> OnPartyJoinFailed;
        public event Action OnPartyLeaveSuccess;
        public event Action<PartyResponseCode> OnPartyLeaveFailed;

        // Training Events (기존)
        public event Action<string> OnTrainingStartAck;
        public event Action<GameInstanceData> OnGameServerReady;
        public event Action<TrainingResponseCode> OnTrainingStartFailed;

        // Training Control Events (교관 주도 플로우)
        public event Action OnPartyConfirmSuccess;
        public event Action<TrainingResponseCode> OnPartyConfirmFailed;
        public event Action OnTrainingSetupSuccess;
        public event Action<TrainingResponseCode> OnTrainingSetupFailed;
        public event Action OnTrainingBeginSuccess;
        public event Action<TrainingResponseCode> OnTrainingBeginFailed;
        public event Action<TrainingStatusData> OnTrainingStatusReceived;
        public event Action<GameInstancePlugin.TrainingBeginReadyData> OnTrainingBeginReadyReceived;

        // Training Pause/Stop Events
        /// <summary>Pause 토글 성공 — signalType: Pause 또는 Resume, partyId</summary>
        public event Action<InstanceSignalType, int> OnTrainingPauseToggled;
        public event Action<TrainingResponseCode> OnTrainingPauseFailed;
        /// <summary>훈련 종료 성공 — partyId</summary>
        public event Action<int> OnTrainingStopSuccess;
        public event Action<TrainingResponseCode> OnTrainingStopFailed;

        // Pin Info Events
        public event Action OnPinInfoSendSuccess;
        public event Action<TrainingResponseCode> OnPinInfoSendFailed;

        // Training MidJoin/MidLeave Events
        /// <summary>훈련 중간 참여 성공 — ip, port</summary>
        public event Action<string, int> OnTrainingMidJoinSuccess;
        public event Action<TrainingResponseCode> OnTrainingMidJoinFailed;
        public event Action OnTrainingMidLeaveSuccess;
        public event Action<TrainingResponseCode> OnTrainingMidLeaveFailed;

        // Training Type Events
        public event Action<ushort> OnTrainingTypeSendSuccess;
        public event Action<string> OnTrainingTypeSendFailed;

        // Admin Events
        public event Action<UserLoginNotification> OnUserLoginReceived;
        public event Action<UserLogoutNotification> OnUserLogoutReceived;
        public event Action<OnlineUsersResponse> OnOnlineUsersReceived;
        public event Action<PartyNotification> OnPartyNotificationReceived;
        public event Action<List<PingStatusEntry>> OnPartyPingStatusReceived;
        public event Action<List<GameInstanceData>> OnInstanceListReceived;
        public event Action<GameInstanceData> OnSpectateReady;
        public event Action<InstanceSignalData> OnInstanceSignalReceived;
        public event Action<string> OnError;
        #endregion

        #region Unity Lifecycle
        private void Start()
        {
            if (client == null && !TryGetComponent(out client))
            {
                client = gameObject.AddComponent<UnityClient>();
            }
        }

        private void OnDestroy()
        {
            if (client != null && isInitialized)
            {
                client.MessageReceived -= OnMessageReceived;
            }
        }
        #endregion

        #region Public Methods - Initialization
        /// <summary>클라이언트를 초기화합니다.</summary>
        public void Initialize(UnityClient uClient)
        {
            if (isInitialized)
            {
                Debug.LogWarning("[DrillInstructorClient] 이미 초기화되었습니다");
                return;
            }

            this.client = uClient;
            this.client.MessageReceived += OnMessageReceived;
            isInitialized = true;
            Debug.Log("[DrillInstructorClient] 초기화 완료");
        }

        /// <summary>관리자 상태를 설정합니다.</summary>
        public void SetAdminStatus(bool isAdmin)
        {
            this.isAdmin = isAdmin;
            Debug.Log($"[DrillInstructorClient] 관리자 상태 설정됨: {isAdmin}");
        }
        #endregion

        #region Public Methods - Auth
        /// <summary>로그인을 요청합니다.</summary>
        public void SendLogin(string id, string password)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(password))
            {
                Debug.LogError("[DrillInstructorClient] ID와 비밀번호는 비어있을 수 없습니다");
                OnLoginFailed?.Invoke(ResponseCode.InvalidData);
                return;
            }

            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                writer.Write(id);
                writer.Write(password);

                using (Message message = Message.Create(MT.Login, writer))
                {
                    client.SendMessage(message, SendMode.Reliable);
                }
            }

            Debug.Log($"[DrillInstructorClient] 로그인 요청 전송됨: {id}");
        }

        /// <summary>계정을 등록합니다.</summary>
        public void SendRegister(string id, string password, int accountType = 0)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(password))
            {
                Debug.LogError("[DrillInstructorClient] ID와 비밀번호는 비어있을 수 없습니다");
                OnRegisterFailed?.Invoke(ResponseCode.InvalidData);
                return;
            }

            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                writer.Write(id);
                writer.Write(password);
                writer.Write(accountType);

                using (Message message = Message.Create(MT.Register, writer))
                {
                    client.SendMessage(message, SendMode.Reliable);
                }
            }

            Debug.Log($"[DrillInstructorClient] 계정 등록 요청 전송됨: {id}");
        }

        /// <summary>계정 정보를 조회합니다.</summary>
        public void SendGetAccount(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                Debug.LogError("[DrillInstructorClient] ID는 비어있을 수 없습니다");
                return;
            }

            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                writer.Write(id);

                using (Message message = Message.Create(MT.GetAccount, writer))
                {
                    client.SendMessage(message, SendMode.Reliable);
                }
            }

            Debug.Log($"[DrillInstructorClient] 계정 조회 요청 전송됨: {id}");
        }

        /// <summary>계정 타입별로 조회합니다.</summary>
        public void SendGetTypeAccounts(int type)
        {
            if (type > 2)
            {
                Debug.LogError("[DrillInstructorClient] 계정 타입이 범위를 벗어났습니다");
                return;
            }

            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                writer.Write(type);

                using (Message message = Message.Create(MT.GetTypeAccounts, writer))
                {
                    client.SendMessage(message, SendMode.Reliable);
                }
            }
            Debug.Log($"[DrillInstructorClient] 타입별 계정 조회 요청 전송됨: {type}");
        }

        /// <summary>계정 정보를 업데이트합니다.</summary>
        public void SendUpdateAccount(DatabasePlugin.AccountData account)
        {
            if (account == null || !account.IsValid())
            {
                Debug.LogError("[DrillInstructorClient] 잘못된 계정 데이터입니다");
                OnUpdateAccountFailed?.Invoke(ResponseCode.InvalidData);
                return;
            }

            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                writer.Write(account);

                using (Message message = Message.Create(MT.UpdateAccount, writer))
                {
                    client.SendMessage(message, SendMode.Reliable);
                }
            }

            Debug.Log($"[DrillInstructorClient] 계정 업데이트 요청 전송됨: {account.Id}");
        }

        /// <summary>비밀번호를 변경합니다.</summary>
        public void SendChangePassword(string id, string oldPassword, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(id) ||
                string.IsNullOrWhiteSpace(oldPassword) ||
                string.IsNullOrWhiteSpace(newPassword))
            {
                Debug.LogError("[DrillInstructorClient] 모든 필드가 필요합니다");
                OnChangePasswordFailed?.Invoke(ResponseCode.InvalidData);
                return;
            }

            if (!IsPasswordStrong(newPassword))
            {
                Debug.LogWarning("[DrillInstructorClient] 새 비밀번호가 너무 약합니다");
                OnChangePasswordFailed?.Invoke(ResponseCode.WeakPassword);
                return;
            }

            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                writer.Write(id);
                writer.Write(oldPassword);
                writer.Write(newPassword);

                using (Message message = Message.Create(MT.ChangePassword, writer))
                {
                    client.SendMessage(message, SendMode.Reliable);
                }
            }

            Debug.Log($"[DrillInstructorClient] 비밀번호 변경 요청 전송됨: {id}");
        }

        /// <summary>계정을 삭제합니다.</summary>
        public void SendDeleteAccount(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                Debug.LogError("[DrillInstructorClient] ID는 비어있을 수 없습니다");
                return;
            }

            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                writer.Write(id);

                using (Message message = Message.Create(MT.DeleteAccount, writer))
                {
                    client.SendMessage(message, SendMode.Reliable);
                }
            }

            Debug.Log($"[DrillInstructorClient] 계정 삭제 요청 전송됨: {id}");
        }
        #endregion

        #region Public Methods - Party
        /// <summary>파티를 생성합니다.</summary>
        public void SendPartyCreate(int maxPlayer = 4)
        {
            SendPartyCreate(null, maxPlayer);
        }

        /// <summary>파티를 생성합니다 (템플릿 파티 사용 가능).</summary>
        public void SendPartyCreate(Party templateParty, int maxPlayer = 4)
        {
            if (client == null)
            {
                Debug.LogError("[DrillInstructorClient] 클라이언트가 초기화되지 않았습니다");
                OnPartyCreateFailed?.Invoke(PartyResponseCode.Failed);
                return;
            }

            if (maxPlayer < 1 || maxPlayer > 16)
            {
                Debug.LogWarning($"[DrillInstructorClient] 잘못된 최대 인원: {maxPlayer}. 기본값 사용: 4");
                maxPlayer = 4;
            }

            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                bool hasTemplate = templateParty != null;
                writer.Write(hasTemplate);

                if (hasTemplate)
                {
                    writer.Write(templateParty);
                    Debug.Log($"[DrillInstructorClient] 파티 생성 요청 전송됨 (템플릿 사용): Scene={templateParty.SceneFile}, MaxPlayer={templateParty.MaxPlayer}");
                }
                else
                {
                    writer.Write(maxPlayer);
                    Debug.Log($"[DrillInstructorClient] 파티 생성 요청 전송됨 (기본): MaxPlayer={maxPlayer}");
                }

                using (Message message = Message.Create(MT.PartyCreate, writer))
                {
                    client.SendMessage(message, SendMode.Reliable);
                }
            }
        }

        /// <summary>파티를 파괴합니다.</summary>
        public void SendPartyDestroy(int partyId)
        {
            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                writer.Write(partyId);

                using (Message message = Message.Create(MT.PartyDestroy, writer))
                {
                    client.SendMessage(message, SendMode.Reliable);
                }
            }

            Debug.Log($"[DrillInstructorClient] 파티 파괴 요청 전송됨: 파티ID={partyId}");
        }

        /// <summary>파티원을 다른 파티로 이동합니다.</summary>
        public void SendPartyMove(int fromPartyId, int toPartyId, string memberAccountId)
        {
            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                writer.Write(fromPartyId);
                writer.Write(toPartyId);
                writer.Write(memberAccountId);

                using (Message message = Message.Create(MT.PartyMove, writer))
                {
                    client.SendMessage(message, SendMode.Reliable);
                }
            }

            Debug.Log($"[DrillInstructorClient] 파티 이동 요청 전송됨: {memberAccountId} {fromPartyId} -> {toPartyId}");
        }

        /// <summary>파티원의 역할을 변경합니다.</summary>
        public void SendPartyChangeRole(int partyId, string targetAccountId, string newRole)
        {
            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                writer.Write(partyId);
                writer.Write(targetAccountId);
                writer.Write(newRole);

                using (Message message = Message.Create(MT.PartyChangeRole, writer))
                {
                    if(client.SendMessage(message, SendMode.Reliable))
                        Debug.Log($"[DrillInstructorClient] 역할 변경 요청 전송됨: 파티ID={partyId}, 계정={targetAccountId}, 역할={newRole}");
                }
            }

        }

        /// <summary>파티 설정을 변경합니다.</summary>
        public void SendPartyChangeSetting(int partyId, string sceneFile, string sceneSetFile, string partyName, string partyInfo)
        {
            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                writer.Write(partyId);
                writer.Write(sceneFile ?? string.Empty);
                writer.Write(sceneSetFile ?? string.Empty);
                writer.Write(partyName ?? string.Empty);
                writer.Write(partyInfo ?? string.Empty);

                using (Message message = Message.Create(MT.PartyChangeSetting, writer))
                {
                    client.SendMessage(message, SendMode.Reliable);
                }
            }

            Debug.Log($"[DrillInstructorClient] 파티 설정 변경 요청 전송됨: 파티ID={partyId}, Scene={sceneFile}, SceneSet={sceneSetFile}");
        }

        /// <summary>파티 설정을 변경합니다. (Party 객체에서 설정 추출)</summary>
        public void SendPartyChangeSetting(int partyId, Party party)
        {
            if (party == null)
            {
                Debug.LogError("[DrillInstructorClient] 파티가 null일 수 없습니다");
                return;
            }

            SendPartyChangeSetting(partyId, party.SceneFile, party.SceneSetFile, party.PartyName, party.PartyInfo);
        }

        /// <summary>파티 목록을 조회합니다.</summary>
        public void SendPartyGetList()
        {
            using (Message message = Message.CreateEmpty(MT.PartyGetList))
            {
                client.SendMessage(message, SendMode.Reliable);
            }

            Debug.Log("[DrillInstructorClient] 파티 목록 조회 요청 전송됨");
        }

        /// <summary>사용자를 파티에 가입시킵니다.</summary>
        public void SendPartyJoin(string accountId, int partyId)
        {
            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                writer.Write(accountId);
                writer.Write(partyId);

                using (Message message = Message.Create(MT.PartyJoin, writer))
                {
                    client.SendMessage(message, SendMode.Reliable);
                }
            }

            Debug.Log($"[DrillInstructorClient] 파티 가입 요청 전송됨: 계정={accountId}, 파티ID={partyId}");
        }

        /// <summary>사용자를 파티에서 탈퇴시킵니다.</summary>
        public void SendPartyLeave(string accountId, int partyId)
        {
            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                writer.Write(accountId);
                writer.Write(partyId);

                using (Message message = Message.Create(MT.PartyLeave, writer))
                {
                    client.SendMessage(message, SendMode.Reliable);
                }
            }

            Debug.Log($"[DrillInstructorClient] 파티 탈퇴 요청 전송됨: 계정={accountId}, 파티ID={partyId}");
        }
        /// <summary>훈련 중간 참여 — 교관을 파티에 view 역할로 추가합니다.</summary>
        public void SendTrainingMidJoin(int partyId)
        {
            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                writer.Write(partyId);

                using (Message message = Message.Create(MT.TrainingMidJoin, writer))
                {
                    client.SendMessage(message, SendMode.Reliable);
                }
            }

            Debug.Log($"[DrillInstructorClient] 훈련 중간 참여 요청 전송됨: 파티ID={partyId}");
        }

        /// <summary>훈련 중간 탈퇴 — 교관을 파티에서 제거합니다.</summary>
        public void SendTrainingMidLeave(int partyId)
        {
            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                writer.Write(partyId);

                using (Message message = Message.Create(MT.TrainingMidLeave, writer))
                {
                    client.SendMessage(message, SendMode.Reliable);
                }
            }

            Debug.Log($"[DrillInstructorClient] 훈련 중간 탈퇴 요청 전송됨: 파티ID={partyId}");
        }
        #endregion

        #region Public Methods - Training
        /// <summary>훈련 시작을 요청합니다. (기존 방식 - 호환성 유지)</summary>
        public void SendTrainingStart(int partyId, string sceneFile = "DefaultScene")
        {
            try
            {
                TrainingStartRequest request = new TrainingStartRequest(partyId, sceneFile);

                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write(request);

                    using (Message message = Message.Create(MT.TrainingStart, writer))
                    {
                        client.SendMessage(message, SendMode.Reliable);
                    }
                }

                Debug.Log($"[DrillInstructorClient] 훈련 시작 요청 전송됨: 파티ID={partyId}, 씬={sceneFile}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DrillInstructorClient] 훈련 시작 요청 오류: {ex.Message}");
                OnError?.Invoke($"훈련 시작 요청 실패: {ex.Message}");
            }
        }
        #endregion

        #region Public Methods - Training Control (교관 주도 플로우)
        /// <summary>
        /// 파티 확정을 요청합니다.
        /// 파티원들에게 최종 파티 구성 및 설정을 브로드캐스트합니다.
        /// </summary>
        public void SendPartyConfirm(int partyId)
        {
            if (!isAdmin)
            {
                Debug.LogWarning("[DrillInstructorClient] 관리자 계정이 아닙니다");
                OnPartyConfirmFailed?.Invoke(TrainingResponseCode.Failed);
                return;
            }

            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write(partyId);

                    using (Message message = Message.Create(MT.PartyConfirm, writer))
                    {
                        client.SendMessage(message, SendMode.Reliable);
                    }
                }

                Debug.Log($"[DrillInstructorClient] 파티 확정 요청 전송됨: 파티ID={partyId}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DrillInstructorClient] 파티 확정 요청 오류: {ex.Message}");
                OnError?.Invoke($"파티 확정 요청 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 훈련 설정을 요청합니다. (인스턴스 프로세스 실행)
        /// GameInstance 프로세스를 실행합니다.
        /// </summary>
        public void SendTrainingSetup(int partyId, string sceneFile = "DefaultScene")
        {
            if (!isAdmin)
            {
                Debug.LogWarning("[DrillInstructorClient] 관리자 계정이 아닙니다");
                OnTrainingSetupFailed?.Invoke(TrainingResponseCode.Failed);
                return;
            }

            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write(partyId);
                    writer.Write(sceneFile);

                    using (Message message = Message.Create(MT.TrainingSetup, writer))
                    {
                        client.SendMessage(message, SendMode.Reliable);
                    }
                }

                Debug.Log($"[DrillInstructorClient] 훈련 설정 요청 전송됨: 파티ID={partyId}, 씬={sceneFile}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DrillInstructorClient] 훈련 설정 요청 오류: {ex.Message}");
                OnError?.Invoke($"훈련 설정 요청 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 훈련 시작을 요청합니다. (새 플로우)
        /// 파티원들이 Fishnet 연결을 완료한 후 호출합니다.
        /// GameInstance에 훈련 시작 신호를 전송합니다.
        /// </summary>
        public void SendTrainingBegin(int partyId)
        {
            if (!isAdmin)
            {
                Debug.LogWarning("[DrillInstructorClient] 관리자 계정이 아닙니다");
                OnTrainingBeginFailed?.Invoke(TrainingResponseCode.Failed);
                return;
            }

            // TrainingBeginReady(230) 수신 여부 확인
            if (!partyBeginReadyFlags.TryGetValue(partyId, out bool ready) || !ready)
            {
                Debug.LogWarning($"[DrillInstructorClient] 파티 {partyId}의 훈련 시작 준비가 완료되지 않았습니다 (TrainingBeginReady 미수신)");
                OnTrainingBeginFailed?.Invoke(TrainingResponseCode.Failed);
                return;
            }

            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write(partyId);

                    using (Message message = Message.Create(MT.TrainingBegin, writer))
                    {
                        client.SendMessage(message, SendMode.Reliable);
                    }
                }

                // 전송 후 플래그 리셋 (재사용 방지)
                partyBeginReadyFlags[partyId] = false;

                Debug.Log($"[DrillInstructorClient] 훈련 시작 요청 전송됨: 파티ID={partyId}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DrillInstructorClient] 훈련 시작 요청 오류: {ex.Message}");
                OnError?.Invoke($"훈련 시작 요청 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 훈련 일시정지/재개 토글을 요청합니다.
        /// </summary>
        public void SendTrainingPause(int partyId)
        {
            if (!isAdmin)
            {
                Debug.LogWarning("[DrillInstructorClient] 관리자 계정이 아닙니다");
                OnTrainingPauseFailed?.Invoke(TrainingResponseCode.Failed);
                return;
            }

            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write(partyId);

                    using (Message message = Message.Create(MT.TrainingPause, writer))
                    {
                        client.SendMessage(message, SendMode.Reliable);
                    }
                }

                Debug.Log($"[DrillInstructorClient] 훈련 일시정지 토글 요청 전송됨: 파티ID={partyId}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DrillInstructorClient] 훈련 일시정지 요청 오류: {ex.Message}");
                OnError?.Invoke($"훈련 일시정지 요청 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 훈련 종료를 요청합니다.
        /// </summary>
        public void SendTrainingStop(int partyId)
        {
            if (!isAdmin)
            {
                Debug.LogWarning("[DrillInstructorClient] 관리자 계정이 아닙니다");
                OnTrainingStopFailed?.Invoke(TrainingResponseCode.Failed);
                return;
            }

            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write(partyId);

                    using (Message message = Message.Create(MT.TrainingExits, writer))
                    {
                        client.SendMessage(message, SendMode.Reliable);
                    }
                }

                Debug.Log($"[DrillInstructorClient] 훈련 종료 요청 전송됨: 파티ID={partyId}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DrillInstructorClient] 훈련 종료 요청 오류: {ex.Message}");
                OnError?.Invoke($"훈련 종료 요청 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 파티 교육생들에게 핀 정보를 전송합니다.
        /// </summary>
        public void SendPinInfo(int partyId, int pinType, float x, float y, float z)
        {
            if (!isAdmin)
            {
                Debug.LogWarning("[DrillInstructorClient] 관리자 계정이 아닙니다");
                OnPinInfoSendFailed?.Invoke(TrainingResponseCode.Failed);
                return;
            }

            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    var pinData = new GameInstancePlugin.PinInfoData(partyId, pinType, x, y, z);
                    writer.Write(pinData);

                    using (Message message = Message.Create(MT.PinInfo, writer))
                    {
                        client.SendMessage(message, SendMode.Reliable);
                    }
                }

                Debug.Log($"[DrillInstructorClient] 핀 정보 전송됨: 파티ID={partyId}, Type={pinType}, Pos=({x},{y},{z})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DrillInstructorClient] 핀 정보 전송 오류: {ex.Message}");
                OnError?.Invoke($"핀 정보 전송 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 파티 멤버에게 TrainingType 변경을 전파합니다.
        /// trainingType: 1000~1019 (TrainingType1~TrainingType20)
        /// </summary>
        public void SendTrainingType(int partyId, ushort trainingType)
        {
            if (trainingType < TAG_TRAINING_TYPE_MIN || trainingType > TAG_TRAINING_TYPE_MAX)
            {
                Debug.LogWarning($"[DrillInstructorClient] 유효하지 않은 TrainingType: {trainingType}");
                OnTrainingTypeSendFailed?.Invoke($"유효하지 않은 TrainingType: {trainingType}");
                return;
            }

            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write(partyId);

                    using (Message message = Message.Create(trainingType, writer))
                    {
                        client.SendMessage(message, SendMode.Reliable);
                    }
                }

                Debug.Log($"[DrillInstructorClient] TrainingType 전송됨: 파티ID={partyId}, Type={trainingType}");
                OnTrainingTypeSendSuccess?.Invoke(trainingType);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DrillInstructorClient] TrainingType 전송 오류: {ex.Message}");
                OnTrainingTypeSendFailed?.Invoke(ex.Message);
            }
        }
        #endregion

        #region Public Methods - Admin
        /// <summary>현재 접속자 목록을 요청합니다.</summary>
        /// <param name="filterType">-1: 전체, 0: 일반사용자, 1: 관리자</param>
        public void SendGetOnlineUsers(int filterType = -1)
        {
            if (!isAdmin)
            {
                Debug.LogWarning("[DrillInstructorClient] 관리자 계정이 아닙니다");
                OnError?.Invoke("관리자 권한이 필요합니다");
                return;
            }

            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write(filterType);

                    using (Message message = Message.Create(MT.AdminGetOnlineUsers, writer))
                    {
                        client.SendMessage(message, SendMode.Reliable);
                    }
                }

                string filterText = filterType switch
                {
                    0 => "일반사용자",
                    1 => "관리자",
                    _ => "전체"
                };

                Debug.Log($"[DrillInstructorClient] 접속자 목록 요청 전송됨 (필터: {filterText})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DrillInstructorClient] 접속자 목록 요청 오류: {ex.Message}");
                OnError?.Invoke($"접속자 목록 요청 실패: {ex.Message}");
            }
        }

        /// <summary>모든 게임 인스턴스 목록을 요청합니다.</summary>
        public void SendGetAllInstances()
        {
            if (!isAdmin)
            {
                Debug.LogWarning("[DrillInstructorClient] 관리자 계정이 아닙니다");
                OnError?.Invoke("관리자 권한이 필요합니다");
                return;
            }

            try
            {
                using (Message message = Message.CreateEmpty(MT.AdminGetAllInstances))
                {
                    client.SendMessage(message, SendMode.Reliable);
                }

                Debug.Log("[DrillInstructorClient] 모든 인스턴스 목록 요청 전송됨");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DrillInstructorClient] 인스턴스 목록 요청 오류: {ex.Message}");
                OnError?.Invoke($"인스턴스 요청 실패: {ex.Message}");
            }
        }

        /// <summary>게임 인스턴스 관전을 요청합니다.</summary>
        public void SendSpectateRequest(int instanceId)
        {
            if (!isAdmin)
            {
                Debug.LogWarning("[DrillInstructorClient] 관리자 계정이 아닙니다");
                OnError?.Invoke("관리자 권한이 필요합니다");
                return;
            }

            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write(instanceId);

                    using (Message message = Message.Create(MT.AdminSpectateRequest, writer))
                    {
                        client.SendMessage(message, SendMode.Reliable);
                    }
                }

                Debug.Log($"[DrillInstructorClient] 관전 요청 전송됨: 인스턴스ID={instanceId}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DrillInstructorClient] 관전 요청 오류: {ex.Message}");
                OnError?.Invoke($"관전 요청 실패: {ex.Message}");
            }
        }
        #endregion

        #region Message Handling
        private void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            using (Message message = e.GetMessage())
            {
                try
                {
                    switch (message.Tag)
                    {
                        // Auth responses
                        case MT.Login:
                            HandleLoginResponse(message);
                            break;
                        case MT.Register:
                            HandleRegisterResponse(message);
                            break;
                        case MT.GetAccount:
                            HandleGetAccountResponse(message);
                            break;
                        case MT.UpdateAccount:
                            HandleUpdateAccountResponse(message);
                            break;
                        case MT.DeleteAccount:
                            HandleDeleteAccountResponse(message);
                            break;
                        case MT.ChangePassword:
                            HandleChangePasswordResponse(message);
                            break;
                        case MT.GetTypeAccounts:
                            HandleGetTypeAccountsResponse(message);
                            break;

                        // Party responses
                        case MT.PartyCreate:
                            HandlePartyCreateResponse(message);
                            break;
                        case MT.PartyDestroy:
                            HandlePartyDestroyResponse(message);
                            break;
                        case MT.PartyMove:
                            HandlePartyMoveResponse(message);
                            break;
                        case MT.PartyChangeRole:
                            HandlePartyChangeRoleResponse(message);
                            break;
                        case MT.PartyChangeSetting:
                            HandlePartyChangeSettingResponse(message);
                            break;
                        case MT.PartyGetList:
                            HandlePartyGetListResponse(message);
                            break;
                        case MT.PartyJoin:
                            HandlePartyJoinResponse(message);
                            break;
                        case MT.PartyLeave:
                            HandlePartyLeaveResponse(message);
                            break;

                        // Training responses
                        case MT.TrainingInfo:
                            HandleTrainingInfoResponse(message);
                            break;

                        // Admin notifications
                        case MT.AdminUserLoginNotification:
                            HandleUserLoginNotification(message);
                            break;
                        case MT.AdminUserLogoutNotification:
                            HandleUserLogoutNotification(message);
                            break;
                        case MT.AdminGetOnlineUsers:
                            HandleGetOnlineUsersResponse(message);
                            break;
                        case MT.AdminPartyNotification:
                            HandlePartyNotification(message);
                            break;
                        case MT.PartyPingStatus:
                            HandlePartyPingStatus(message);
                            break;
                        case MT.AdminGetAllInstances:
                            HandleInstanceListResponse(message);
                            break;
                        case MT.AdminSpectateRequest:
                            HandleSpectateResponse(message);
                            break;
                        case MT.InstanceSignalBroadcast:
                            HandleInstanceSignalBroadcast(message);
                            break;

                        // Training Control responses (교관 주도 플로우)
                        case MT.PartyConfirm:
                            HandlePartyConfirmResponse(message);
                            break;
                        case MT.TrainingSetup:
                            HandleTrainingSetupResponse(message);
                            break;
                        case MT.TrainingBegin:
                            HandleTrainingBeginResponse(message);
                            break;
                        case MT.TrainingStatusNotification:
                            HandleTrainingStatusNotification(message);
                            break;
                        case MT.TrainingCreated:
                            break;
                        case MT.TrainingBeginReady:
                            HandleTrainingBeginReadyNotification(message);
                            break;
                        case MT.TrainingPause:
                            HandleTrainingPauseResponse(message);
                            break;
                        case MT.TrainingExits:
                            HandleTrainingStopResponse(message);
                            break;
                        case MT.PinInfo:
                            HandlePinInfoResponse(message);
                            break;
                        case MT.TrainingMidJoin:
                            HandleTrainingMidJoinResponse(message);
                            break;
                        case MT.TrainingMidLeave:
                            HandleTrainingMidLeaveResponse(message);
                            break;

                        default:
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[DrillInstructorClient] 메시지 처리 오류 (tag={message.Tag}): {ex}");
                }
            }
        }
        #endregion

        #region Response Handlers - Auth
        private void HandleLoginResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                ResponseCode code = (ResponseCode)reader.ReadByte();

                if (code == ResponseCode.Success)
                {
                    DatabasePlugin.AccountResponse account = reader.ReadSerializable<DatabasePlugin.AccountResponse>();
                    currentAccount = account;
                    isAdmin = account.AccountType == 1;
                    Debug.Log($"[DrillInstructorClient] 로그인 성공: {account}");
                    OnLoginSuccess?.Invoke(account);
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 로그인 실패: {code}");
                    OnLoginFailed?.Invoke(code);
                }
            }
        }

        private void HandleRegisterResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                ResponseCode code = (ResponseCode)reader.ReadByte();

                if (code == ResponseCode.Success)
                {
                    DatabasePlugin.AccountResponse account = reader.ReadSerializable<DatabasePlugin.AccountResponse>();
                    Debug.Log($"[DrillInstructorClient] 계정 등록 성공: {account}");
                    OnRegisterSuccess?.Invoke(account);
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 계정 등록 실패: {code}");
                    OnRegisterFailed?.Invoke(code);
                }
            }
        }

        private void HandleGetAccountResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                ResponseCode code = (ResponseCode)reader.ReadByte();

                if (code == ResponseCode.Success)
                {
                    DatabasePlugin.AccountResponse account = reader.ReadSerializable<DatabasePlugin.AccountResponse>();
                    Debug.Log($"[DrillInstructorClient] 계정 조회 성공: {account}");
                    OnGetAccountSuccess?.Invoke(account);
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 계정 조회 실패: {code}");
                    OnGetAccountFailed?.Invoke(code);
                }
            }
        }

        private void HandleGetTypeAccountsResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                ResponseCode code = (ResponseCode)reader.ReadByte();

                if (code == ResponseCode.Success)
                {
                    int length = reader.ReadInt32();
                    List<DatabasePlugin.AccountResponse> accounts = new();
                    for (int i = 0; i < length; i++)
                    {
                        accounts.Add(reader.ReadSerializable<DatabasePlugin.AccountResponse>());
                    }
                    Debug.Log($"[DrillInstructorClient] 타입별 계정 조회 성공: {accounts.Count}개");
                    OnGetTypeAccountsSuccess?.Invoke(accounts);
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 타입별 계정 조회 실패: {code}");
                    OnGetTypeAccountsFailed?.Invoke(code);
                }
            }
        }

        private void HandleUpdateAccountResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                ResponseCode code = (ResponseCode)reader.ReadByte();

                if (code == ResponseCode.Success)
                {
                    Debug.Log("[DrillInstructorClient] 계정 업데이트 성공");
                    OnUpdateAccountSuccess?.Invoke();
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 계정 업데이트 실패: {code}");
                    OnUpdateAccountFailed?.Invoke(code);
                }
            }
        }

        private void HandleDeleteAccountResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                ResponseCode code = (ResponseCode)reader.ReadByte();

                if (code == ResponseCode.Success)
                {
                    Debug.Log("[DrillInstructorClient] 계정 삭제 성공");
                    OnDeleteAccountSuccess?.Invoke();
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 계정 삭제 실패: {code}");
                    OnDeleteAccountFailed?.Invoke(code);
                }
            }
        }

        private void HandleChangePasswordResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                ResponseCode code = (ResponseCode)reader.ReadByte();

                if (code == ResponseCode.Success)
                {
                    Debug.Log("[DrillInstructorClient] 비밀번호 변경 성공");
                    OnChangePasswordSuccess?.Invoke();
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 비밀번호 변경 실패: {code}");
                    OnChangePasswordFailed?.Invoke(code);
                }
            }
        }
        #endregion

        #region Response Handlers - Party
        private void HandlePartyCreateResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                PartyResponseCode code = (PartyResponseCode)reader.ReadByte();

                if (code == PartyResponseCode.Success)
                {
                    Party party = reader.ReadSerializable<Party>();
                    Debug.Log($"[DrillInstructorClient] 파티 생성 성공: {party}");
                    OnPartyCreateSuccess?.Invoke(party);
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 파티 생성 실패: {code}");
                    OnPartyCreateFailed?.Invoke(code);
                }
            }
        }

        private void HandlePartyDestroyResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                PartyResponseCode code = (PartyResponseCode)reader.ReadByte();

                if (code == PartyResponseCode.Success)
                {
                    int partyId = reader.ReadInt32();
                    Debug.Log($"[DrillInstructorClient] 파티 파괴됨: 파티ID={partyId}");
                    OnPartyDestroySuccess?.Invoke(partyId);
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 파티 파괴 실패: {code}");
                    OnPartyDestroyFailed?.Invoke(code);
                }
            }
        }

        private void HandlePartyMoveResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                PartyResponseCode code = (PartyResponseCode)reader.ReadByte();

                if (code == PartyResponseCode.Success)
                {
                    Party party = reader.ReadSerializable<Party>();
                    Debug.Log($"[DrillInstructorClient] 파티 이동 성공: 새 파티={party}");
                    OnPartyMoveSuccess?.Invoke(party);
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 파티 이동 실패: {code}");
                    OnPartyMoveFailed?.Invoke(code);
                }
            }
        }

        private void HandlePartyChangeRoleResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                PartyResponseCode code = (PartyResponseCode)reader.ReadByte();

                if (code == PartyResponseCode.Success)
                {
                    Party party = reader.ReadSerializable<Party>();
                    Debug.Log($"[DrillInstructorClient] 역할 변경됨: {party}");
                    OnPartyChangeRoleSuccess?.Invoke(party);
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 역할 변경 실패: {code}");
                    OnPartyChangeRoleFailed?.Invoke(code);
                }
            }
        }

        private void HandlePartyChangeSettingResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                PartyResponseCode code = (PartyResponseCode)reader.ReadByte();

                if (code == PartyResponseCode.Success)
                {
                    Party party = reader.ReadSerializable<Party>();
                    Debug.Log($"[DrillInstructorClient] 파티 설정 변경됨: {party}");
                    OnPartyChangeSettingSuccess?.Invoke(party);
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 파티 설정 변경 실패: {code}");
                    OnPartyChangeSettingFailed?.Invoke(code);
                }
            }
        }

        private void HandlePartyGetListResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                PartyResponseCode code = (PartyResponseCode)reader.ReadByte();

                if (code == PartyResponseCode.Success)
                {
                    int count = reader.ReadInt32();
                    List<Party> parties = new List<Party>(count);

                    for (int i = 0; i < count; i++)
                    {
                        parties.Add(reader.ReadSerializable<Party>());
                    }

                    Debug.Log($"[DrillInstructorClient] 파티 목록 수신됨: {count}개");
                    OnPartyGetListSuccess?.Invoke(parties);
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 파티 목록 조회 실패: {code}");
                    OnPartyGetListFailed?.Invoke(code);
                }
            }
        }

        private void HandlePartyJoinResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                PartyResponseCode code = (PartyResponseCode)reader.ReadByte();

                if (code == PartyResponseCode.Success)
                {
                    Party party = reader.ReadSerializable<Party>();
                    Debug.Log($"[DrillInstructorClient] 파티 가입 성공: {party}");
                    OnPartyJoinSuccess?.Invoke(party);
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 파티 가입 실패: {code}");
                    OnPartyJoinFailed?.Invoke(code);
                }
            }
        }

        private void HandlePartyLeaveResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                PartyResponseCode code = (PartyResponseCode)reader.ReadByte();

                if (code == PartyResponseCode.Success)
                {
                    Debug.Log("[DrillInstructorClient] 파티 탈퇴 성공");
                    OnPartyLeaveSuccess?.Invoke();
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 파티 탈퇴 실패: {code}");
                    OnPartyLeaveFailed?.Invoke(code);
                }
            }
        }
        #endregion

        #region Response Handlers - Training
        private void HandleTrainingInfoResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                TrainingResponseCode code = (TrainingResponseCode)reader.ReadByte();

                if (code == TrainingResponseCode.Success)
                {
                    if (reader.Length >= 8)
                    {
                        int instanceId = reader.ReadInt32();
                        string statusMessage = reader.ReadString();

                        Debug.Log($"[DrillInstructorClient] 훈련 시작 확인됨: 인스턴스={instanceId}, 메시지={statusMessage}");
                        OnTrainingStartAck?.Invoke(statusMessage);
                    }
                    else
                    {
                        GameInstanceData instanceData = reader.ReadSerializable<GameInstanceData>();
                        Debug.Log($"[DrillInstructorClient] 게임 서버 준비 완료: {instanceData}");
                        OnGameServerReady?.Invoke(instanceData);
                    }
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 훈련 시작 실패: {code}");
                    OnTrainingStartFailed?.Invoke(code);

                    string errorMessage = code switch
                    {
                        TrainingResponseCode.NoAvailableInstance => "사용 가능한 게임 인스턴스가 없습니다.",
                        TrainingResponseCode.PartyNotFound => "파티를 찾을 수 없습니다.",
                        TrainingResponseCode.AlreadyRunning => "이 파티에 대한 훈련이 이미 실행 중입니다.",
                        TrainingResponseCode.ServerError => "서버 내부/의존성 문제로 수행할 수 없습니다.",
                        TrainingResponseCode.Unauthorized => "교관/관리자 권한을 확인할 수 없습니다.",
                        TrainingResponseCode.InstanceError => "서버 인스턴스 상태 문제(실행 실패/종료)입니다.",
                        TrainingResponseCode.AlreadyExists => "이미 해당 파티에 관전/참여 중입니다.",

                        _ => $"훈련 시작 실패: {code}"
                    };

                    OnError?.Invoke(errorMessage);
                }
            }
        }
        #endregion

        #region Response Handlers - Training Control (교관 주도 플로우)
        private void HandlePartyConfirmResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                TrainingResponseCode code = (TrainingResponseCode)reader.ReadByte();

                if (code == TrainingResponseCode.Success)
                {
                    Debug.Log("[DrillInstructorClient] 파티 확정 성공");
                    OnPartyConfirmSuccess?.Invoke();
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 파티 확정 실패: {code}");
                    OnPartyConfirmFailed?.Invoke(code);
                }
            }
        }

        private void HandleTrainingSetupResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                TrainingResponseCode code = (TrainingResponseCode)reader.ReadByte();

                if (code == TrainingResponseCode.Success)
                {
                    Debug.Log("[DrillInstructorClient] 훈련 설정 성공 - 인스턴스 프로세스 실행됨");
                    OnTrainingSetupSuccess?.Invoke();
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 훈련 설정 실패: {code}");
                    OnTrainingSetupFailed?.Invoke(code);

                    string errorMessage = code switch
                    {
                        TrainingResponseCode.NoAvailableInstance => "사용 가능한 게임 인스턴스가 없습니다.",
                        TrainingResponseCode.PartyNotFound => "파티를 찾을 수 없습니다.",
                        TrainingResponseCode.AlreadyRunning => "이 파티에 대한 훈련이 이미 실행 중입니다.",
                        TrainingResponseCode.ServerError => "서버 내부/의존성 문제로 수행할 수 없습니다.",
                        TrainingResponseCode.Unauthorized => "교관/관리자 권한을 확인할 수 없습니다.",
                        TrainingResponseCode.InstanceError => "서버 인스턴스 상태 문제(실행 실패/종료)입니다.",
                        TrainingResponseCode.AlreadyExists => "이미 해당 파티에 관전/참여 중입니다.",

                        _ => $"훈련 설정 실패: {code}"
                    };
                    OnError?.Invoke(errorMessage);
                }
            }
        }

        private void HandleTrainingBeginResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                TrainingResponseCode code = (TrainingResponseCode)reader.ReadByte();

                if (code == TrainingResponseCode.Success)
                {
                    Debug.Log("[DrillInstructorClient] 훈련 시작 성공");
                    OnTrainingBeginSuccess?.Invoke();
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 훈련 시작 실패: {code}");
                    OnTrainingBeginFailed?.Invoke(code);

                    string errorMessage = code switch
                    {
                        TrainingResponseCode.InstanceNotFound => "게임 인스턴스를 찾을 수 없습니다.",
                        TrainingResponseCode.PartyNotFound => "파티를 찾을 수 없습니다.",
                        TrainingResponseCode.ServerError => "서버 내부/의존성 문제로 수행할 수 없습니다.",
                        TrainingResponseCode.Unauthorized => "교관/관리자 권한을 확인할 수 없습니다.",
                        TrainingResponseCode.InstanceError => "서버 인스턴스 상태 문제(실행 실패/종료)입니다.",
                        TrainingResponseCode.AlreadyExists => "이미 해당 파티에 관전/참여 중입니다.",
                        _ => $"훈련 시작 실패: {code}"
                    };
                    OnError?.Invoke(errorMessage);
                }
            }
        }

        private void HandleTrainingPauseResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                TrainingResponseCode code = (TrainingResponseCode)reader.ReadByte();

                if (code == TrainingResponseCode.Success)
                {
                    InstanceSignalType resultSignal = (InstanceSignalType)reader.ReadByte();
                    int partyId = reader.ReadInt32();

                    string stateStr = resultSignal == InstanceSignalType.Pause ? "일시정지" : "재개";
                    Debug.Log($"[DrillInstructorClient] 훈련 {stateStr} 성공: 파티={partyId}");
                    OnTrainingPauseToggled?.Invoke(resultSignal, partyId);
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 훈련 일시정지 실패: {code}");
                    OnTrainingPauseFailed?.Invoke(code);
                    OnError?.Invoke($"훈련 일시정지 실패: {code}");
                }
            }
        }

        private void HandleTrainingStopResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                TrainingResponseCode code = (TrainingResponseCode)reader.ReadByte();

                if (code == TrainingResponseCode.Success)
                {
                    InstanceSignalType resultSignal = (InstanceSignalType)reader.ReadByte();
                    int partyId = reader.ReadInt32();

                    Debug.Log($"[DrillInstructorClient] 훈련 종료 성공: 파티={partyId}");
                    OnTrainingStopSuccess?.Invoke(partyId);
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 훈련 종료 실패: {code}");
                    OnTrainingStopFailed?.Invoke(code);

                    string errorMessage = code switch
                    {
                        TrainingResponseCode.InstanceNotFound => "게임 인스턴스를 찾을 수 없습니다.",
                        TrainingResponseCode.ServerError => "서버 내부/의존성 문제로 수행할 수 없습니다.",
                        TrainingResponseCode.Unauthorized => "교관/관리자 권한을 확인할 수 없습니다.",
                        TrainingResponseCode.InstanceError => "서버 인스턴스 상태 문제(실행 실패/종료)입니다.",
                        TrainingResponseCode.AlreadyExists => "이미 해당 파티에 관전/참여 중입니다.",
                        _ => $"훈련 종료 실패: {code}"
                    };
                    OnError?.Invoke(errorMessage);
                }
            }
        }

        private void HandlePinInfoResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                TrainingResponseCode code = (TrainingResponseCode)reader.ReadByte();

                if (code == TrainingResponseCode.Success)
                {
                    Debug.Log("[DrillInstructorClient] 핀 정보 전송 성공");
                    OnPinInfoSendSuccess?.Invoke();
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 핀 정보 전송 실패: {code}");
                    OnPinInfoSendFailed?.Invoke(code);
                }
            }
        }

        private void HandleTrainingStatusNotification(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                TrainingResponseCode code = (TrainingResponseCode)reader.ReadByte();

                if (code == TrainingResponseCode.Success)
                {
                    int partyId = reader.ReadInt32();
                    int totalMembers = reader.ReadInt32();
                    int readyMembers = reader.ReadInt32();
                    
                    int count = reader.ReadInt32();
                    string[] readyIds = new string[count];
                    for (int i = 0; i < count; i++)
                    {
                        readyIds[i] = reader.ReadString();
                    }
                    
                    bool allReady = reader.ReadBoolean();

                    TrainingStatusData status = new TrainingStatusData
                    {
                        PartyId = partyId,
                        TotalMembers = totalMembers,
                        ReadyMembers = readyMembers,
                        ReadyAccountIds = readyIds,
                        AllReady = allReady
                    };

                    Debug.Log($"[DrillInstructorClient] 훈련 상태 수신됨: {status}");
                    OnTrainingStatusReceived?.Invoke(status);
                }
            }
        }

        private void HandleTrainingBeginReadyNotification(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                TrainingResponseCode code = (TrainingResponseCode)reader.ReadByte();

                if (code == TrainingResponseCode.Success)
                {
                    int partyId = reader.ReadInt32();
                    int instanceId = reader.ReadInt32();
                    int connectedCount = reader.ReadInt32();
                    int expectedCount = reader.ReadInt32();
                    bool allConnected = reader.ReadBoolean();

                    GameInstancePlugin.TrainingBeginReadyData data = new GameInstancePlugin.TrainingBeginReadyData(partyId, instanceId, connectedCount, expectedCount);

                    // 모든 파티원 접속 완료 시 BeginReady 플래그 활성화
                    if (allConnected)
                    {
                        partyBeginReadyFlags[partyId] = true;
                        Debug.Log($"[DrillInstructorClient] 파티 {partyId} 훈련 시작 준비 완료 — TrainingBegin 전송 가능");
                    }

                    Debug.Log($"[DrillInstructorClient] 훈련 시작 준비 수신: {data} (allConnected={allConnected})");
                    OnTrainingBeginReadyReceived?.Invoke(data);
                }
            }
        }

        private void HandleTrainingMidJoinResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                TrainingResponseCode code = (TrainingResponseCode)reader.ReadByte();

                if (code == TrainingResponseCode.Success)
                {
                    string ip = reader.ReadString();
                    int port = reader.ReadInt32();
                    Debug.Log($"[DrillInstructorClient] 훈련 중간 참여 성공: {ip}:{port}");
                    OnTrainingMidJoinSuccess?.Invoke(ip, port);
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 훈련 중간 참여 실패: {code}");
                    OnTrainingMidJoinFailed?.Invoke(code);
                }
            }
        }

        private void HandleTrainingMidLeaveResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                TrainingResponseCode code = (TrainingResponseCode)reader.ReadByte();

                if (code == TrainingResponseCode.Success)
                {
                    Debug.Log("[DrillInstructorClient] 훈련 중간 탈퇴 성공");
                    OnTrainingMidLeaveSuccess?.Invoke();
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 훈련 중간 탈퇴 실패: {code}");
                    OnTrainingMidLeaveFailed?.Invoke(code);
                }
            }
        }
        #endregion

        #region Response Handlers - Admin
        private void HandleUserLoginNotification(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                UserLoginNotification notification = new UserLoginNotification
                {
                    Account = reader.ReadSerializable<AccountResponse>(),
                    ClientId = reader.ReadUInt16(),
                    Timestamp = reader.ReadString()
                };

                Debug.Log($"[DrillInstructorClient] 사용자 로그인: {notification}");

                if (notification.Account == null)
                {
                    Debug.LogError("[DrillInstructorClient] 로그인 알림: AccountResponse 역직렬화 실패");
                    return;
                }

                cachedOnlineUsers[notification.Account.Id] = new DatabasePlugin.OnlineUserInfo
                {
                    AccountId = notification.Account.Id,
                    ClientId = notification.ClientId,
                    AccountType = notification.Account.AccountType,
                    LoginTime = DateTime.Now
                };

                loginHistory.Add(notification);
                if (loginHistory.Count > MAX_HISTORY)
                {
                    loginHistory.RemoveAt(0);
                }

                OnUserLoginReceived?.Invoke(notification);
            }
        }

        private void HandleUserLogoutNotification(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                UserLogoutNotification notification = new UserLogoutNotification
                {
                    AccountId = reader.ReadString(),
                    ClientId = reader.ReadUInt16(),
                    Timestamp = reader.ReadString()
                };

                Debug.Log($"[DrillInstructorClient] 사용자 로그아웃: {notification}");

                cachedOnlineUsers.Remove(notification.AccountId);

                OnUserLogoutReceived?.Invoke(notification);
            }
        }

        private void HandleGetOnlineUsersResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                byte responseCode = reader.ReadByte();

                if (responseCode == 0) // Success
                {
                    int count = reader.ReadInt32();
                    var users = new List<DatabasePlugin.OnlineUserInfo>(count);

                    for (int i = 0; i < count; i++)
                    {
                        users.Add(reader.ReadSerializable<DatabasePlugin.OnlineUserInfo>());
                    }

                    int totalCount = reader.ReadInt32();
                    int adminCount = reader.ReadInt32();
                    int userCount = reader.ReadInt32();

                    var response = new OnlineUsersResponse
                    {
                        Users = users,
                        TotalCount = totalCount,
                        AdminCount = adminCount,
                        UserCount = userCount
                    };

                    cachedOnlineUsers.Clear();
                    foreach (var user in users)
                    {
                        cachedOnlineUsers[user.AccountId] = user;
                    }

                    Debug.Log($"[DrillInstructorClient] 접속자 목록 수신됨: {count}명 (관리자: {adminCount}, 사용자: {userCount})");
                    OnOnlineUsersReceived?.Invoke(response);
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 접속자 목록 조회 실패: 코드={responseCode}");
                    OnError?.Invoke($"접속자 목록 조회 실패: {responseCode}");
                }
            }
        }

        private void HandlePartyNotification(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                PartyNotification notification = new PartyNotification
                {
                    Type = (InstructorNotificationType)reader.ReadByte(),
                    Party = reader.ReadSerializable<Party>(),
                    AffectedAccountId = reader.ReadString(),
                    AdditionalInfo = reader.ReadString(),
                    Timestamp = reader.ReadString()
                };

                Debug.Log($"[DrillInstructorClient] 파티 알림: {notification}");

                notificationHistory.Add(notification);
                if (notificationHistory.Count > MAX_HISTORY)
                {
                    notificationHistory.RemoveAt(0);
                }

                OnPartyNotificationReceived?.Invoke(notification);
            }
        }

        private void HandlePartyPingStatus(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                int count = reader.ReadInt32();
                var entries = new List<PingStatusEntry>(count);
                for (int i = 0; i < count; i++)
                {
                    entries.Add(new PingStatusEntry
                    {
                        AccountId      = reader.ReadString(),
                        PartyId        = reader.ReadInt32(),
                        PingMs         = reader.ReadInt32(),
                        HardwareStatus = reader.ReadInt32()
                    });
                }
                OnPartyPingStatusReceived?.Invoke(entries);
            }
        }

        private void HandleInstanceListResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                byte responseCode = reader.ReadByte();

                if (responseCode == 0) // Success
                {
                    int count = reader.ReadInt32();
                    List<GameInstanceData> instances = new List<GameInstanceData>(count);

                    for (int i = 0; i < count; i++)
                    {
                        instances.Add(reader.ReadSerializable<GameInstanceData>());
                    }

                    Debug.Log($"[DrillInstructorClient] 인스턴스 목록 수신됨: {count}개");
                    OnInstanceListReceived?.Invoke(instances);
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 인스턴스 목록 가져오기 실패: 코드={responseCode}");
                    OnError?.Invoke($"인스턴스 목록 가져오기 실패: {responseCode}");
                }
            }
        }

        private void HandleSpectateResponse(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                byte responseCode = reader.ReadByte();

                if (responseCode == 0) // Success
                {
                    GameInstanceData instanceData = reader.ReadSerializable<GameInstanceData>();

                    Debug.Log($"[DrillInstructorClient] 관전 준비 완료: {instanceData}");
                    OnSpectateReady?.Invoke(instanceData);
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 관전 요청 실패: 코드={responseCode}");
                    OnError?.Invoke($"관전 요청 실패: {responseCode}");
                }
            }
        }

        private void HandleInstanceSignalBroadcast(Message message)
        {
            using (DarkRiftReader reader = message.GetReader())
            {
                InstanceSignalData signalData = new InstanceSignalData
                {
                    InstanceId = reader.ReadInt32(),
                    PartyId = reader.ReadInt32(),
                    SignalType = (InstanceSignalType)reader.ReadByte(),
                    Timestamp = reader.ReadString()
                };

                Debug.Log($"[DrillInstructorClient] 인스턴스 신호 수신됨: {signalData}");
                OnInstanceSignalReceived?.Invoke(signalData);
            }
        }
        #endregion

        #region Utility
        /// <summary>비밀번호 강도를 검증합니다.</summary>
        private bool IsPasswordStrong(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                return false;

            if (password.Length < 8 || password.Length > 100)
                return false;

            bool hasUpper = false;
            bool hasLower = false;
            bool hasDigit = false;

            foreach (char c in password)
            {
                if (char.IsUpper(c)) hasUpper = true;
                else if (char.IsLower(c)) hasLower = true;
                else if (char.IsDigit(c)) hasDigit = true;
            }

            return hasUpper && hasLower && hasDigit;
        }
        #endregion

        #region Public Properties
        /// <summary>현재 계정</summary>
        public DatabasePlugin.AccountResponse CurrentAccount => currentAccount;

        /// <summary>관리자 여부</summary>
        public bool IsAdmin => isAdmin;

        /// <summary>초기화 여부</summary>
        public bool IsInitialized => isInitialized;

        /// <summary>특정 파티의 훈련 시작 준비 완료 여부 (TrainingBeginReady 수신됨)</summary>
        public bool IsTrainingBeginReady(int partyId) => partyBeginReadyFlags.TryGetValue(partyId, out bool ready) && ready;

        /// <summary>캐시된 온라인 사용자 목록</summary>
        public IReadOnlyDictionary<string, DatabasePlugin.OnlineUserInfo> GetCachedOnlineUsers() => cachedOnlineUsers;

        /// <summary>캐시된 온라인 사용자 수</summary>
        public int CachedOnlineUserCount => cachedOnlineUsers.Count;

        /// <summary>로그인 히스토리</summary>
        public IReadOnlyList<UserLoginNotification> GetLoginHistory() => loginHistory;

        /// <summary>알림 히스토리</summary>
        public IReadOnlyList<PartyNotification> GetNotificationHistory() => notificationHistory;

        /// <summary>알림 히스토리 초기화</summary>
        public void ClearNotificationHistory()
        {
            notificationHistory.Clear();
            Debug.Log("[DrillInstructorClient] 알림 히스토리 초기화됨");
        }

        /// <summary>특정 타입의 알림만 필터링</summary>
        public List<PartyNotification> GetNotificationsByType(InstructorNotificationType type)
        {
            return notificationHistory.FindAll(n => n.Type == type);
        }

        /// <summary>특정 파티의 알림만 필터링</summary>
        public List<PartyNotification> GetNotificationsByParty(int partyId)
        {
            return notificationHistory.FindAll(n => n.Party.PartyId == partyId);
        }

        /// <summary>알림 개수</summary>
        public int NotificationCount => notificationHistory.Count;
        #endregion
    }
}
