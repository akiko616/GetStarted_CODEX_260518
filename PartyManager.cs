using System;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using DarkRift;
using DarkRift.Server;

namespace PartyManagerPlugin
{
    /// <summary>
    /// 파티 관리 플러그인 (관리자 알림 기능 추가)
    /// 파티 생성/파괴/이동/롤변경/설정변경 처리 + 관리자 실시간 모니터링
    /// </summary>
    public class PartyManager : Plugin
    {
        public override Version Version => new Version(1, 1, 0);
        public override bool ThreadSafe => true;

        // 파티 목록 (스레드 안전)
        private readonly object partyLock = new object();
        private readonly List<Party> parties = new List<Party>();

        // 파티 ID 생성기 (YYYYMMDD + 순차번호)
        private string lastDatePrefix = "";
        private int dailySequence = 0;
        private readonly object idGenLock = new object();

        // SessionManager 참조 (DatabasePlugin에서 주입)
        private DatabasePlugin.SessionManager sessionManager;

        // GameInstanceManager 참조 (DatabasePlugin에서 주입) — 파티 제거 시 인스턴스 정리용
        private GameInstancePlugin.GameInstanceManager gameInstanceManager;

        // 태그 정의 (MessageTags에서 참조)
        private const ushort TAG_PARTY_CREATE = DatabasePlugin.MessageTags.PartyCreate;
        private const ushort TAG_PARTY_DESTROY = DatabasePlugin.MessageTags.PartyDestroy;
        private const ushort TAG_PARTY_MOVE = DatabasePlugin.MessageTags.PartyMove;
        private const ushort TAG_PARTY_CHANGE_ROLE = DatabasePlugin.MessageTags.PartyChangeRole;
        private const ushort TAG_PARTY_CHANGE_SETTING = DatabasePlugin.MessageTags.PartyChangeSetting;
        private const ushort TAG_PARTY_GET_LIST = DatabasePlugin.MessageTags.PartyGetList;
        private const ushort TAG_PARTY_JOIN = DatabasePlugin.MessageTags.PartyJoin;
        private const ushort TAG_PARTY_LEAVE = DatabasePlugin.MessageTags.PartyLeave;

        // 관리자 알림 태그 (MessageTags에서 참조)
        private const ushort TAG_ADMIN_PARTY_NOTIFICATION = DatabasePlugin.MessageTags.AdminPartyNotification;

        // TrainingType 태그 범위 (1000-1019)
        private const ushort TAG_TRAINING_TYPE_MIN = DatabasePlugin.MessageTags.TrainingType1;   // 1000
        private const ushort TAG_TRAINING_TYPE_MAX = DatabasePlugin.MessageTags.TrainingType20;  // 1019

        // 응답 코드
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

        // 알림 타입
        public enum AdminNotificationType : byte
        {
            PartyCreated = 0,
            PartyDestroyed = 1,
            PartyMemberJoined = 2,
            PartyMemberLeft = 3,
            PartyRoleChanged = 4,
            PartySettingChanged = 5
        }

        /// <summary>
        /// 파티 ID 생성: YYMMDD * 1000 + 순차번호 (예: 260317001, 260317002)
        /// </summary>
        private int GeneratePartyId()
        {
            lock (idGenLock)
            {
                string today = DateTime.Now.ToString("yyMMdd");
                if (today != lastDatePrefix)
                {
                    lastDatePrefix = today;
                    dailySequence = 0;
                }
                dailySequence++;

                // YYMMDD * 1000 + seq (최대 999개/일)
                int partyId = int.Parse(today) * 1000 + dailySequence;

                // 중복 체크 (안전장치)
                while (FindPartyById(partyId) != null)
                {
                    dailySequence++;
                    partyId = int.Parse(today) * 1000 + dailySequence;
                }

                return partyId;
            }
        }

        public PartyManager(PluginLoadData pluginLoadData) : base(pluginLoadData)
        {
            ClientManager.ClientConnected += OnClientConnected;
            ClientManager.ClientDisconnected += OnClientDisconnected;

            Logger.Info("PartyManager initialized (Admin notification enabled)");
        }

        /// <summary>
        /// SessionManager 주입 (DatabasePlugin에서 호출)
        /// </summary>
        public void SetSessionManager(DatabasePlugin.SessionManager sessionManager)
        {
            this.sessionManager = sessionManager;
            Logger.Info("SessionManager injected to PartyManager");
        }

        /// <summary>
        /// GameInstanceManager 주입 (DatabasePlugin에서 호출)
        /// 파티 제거 시 연결된 인스턴스를 정리하기 위해 필요
        /// </summary>
        public void SetGameInstanceManager(GameInstancePlugin.GameInstanceManager gim)
        {
            this.gameInstanceManager = gim;
            Logger.Info("GameInstanceManager injected to PartyManager");
        }

        private void OnClientConnected(object sender, ClientConnectedEventArgs e)
        {
            e.Client.MessageReceived += OnMessageReceived;
        }

        private void OnClientDisconnected(object sender, ClientDisconnectedEventArgs e)
        {
            e.Client.MessageReceived -= OnMessageReceived;

            // 연결 끊김 시 파티에서 제거
            if (sessionManager != null)
            {
                string accountId = sessionManager.GetAccountId(e.Client);
                if (!string.IsNullOrWhiteSpace(accountId))
                {
                    RemoveMemberFromAllParties(accountId);
                }
            }
        }

        private void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            using (Message message = e.GetMessage())
            {
                try
                {
                    switch (message.Tag)
                    {
                        case TAG_PARTY_CREATE:
                            HandlePartyCreate(e.Client, message);
                            break;

                        case TAG_PARTY_DESTROY:
                            HandlePartyDestroy(e.Client, message);
                            break;

                        case TAG_PARTY_MOVE:
                            HandlePartyMove(e.Client, message);
                            break;

                        case TAG_PARTY_CHANGE_ROLE:
                            HandlePartyChangeRole(e.Client, message);
                            break;

                        case TAG_PARTY_CHANGE_SETTING:
                            HandlePartyChangeSetting(e.Client, message);
                            break;

                        case TAG_PARTY_GET_LIST:
                            HandlePartyGetList(e.Client, message);
                            break;

                        case TAG_PARTY_JOIN:
                            HandlePartyJoin(e.Client, message);
                            break;

                        case TAG_PARTY_LEAVE:
                            HandlePartyLeave(e.Client, message);
                            break;

                        default:
                            if (message.Tag >= TAG_TRAINING_TYPE_MIN && message.Tag <= TAG_TRAINING_TYPE_MAX)
                            {
                                HandleTrainingTypeChange(e.Client, message);
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error handling party message (Tag: {message.Tag}): {ex.Message}");
                }
            }
        }

        #region Message Handlers

        /// <summary>
        /// 파티 생성 처리 (관리자 전용, 템플릿 파티 지원)
        /// </summary>
        private void HandlePartyCreate(IClient client, Message message)
        {
            try
            {
                if (sessionManager == null)
                {
                    SendErrorResponse(client, TAG_PARTY_CREATE, PartyResponseCode.Failed);
                    return;
                }

                string accountId = sessionManager.GetAccountId(client);
                if (string.IsNullOrWhiteSpace(accountId))
                {
                    SendErrorResponse(client, TAG_PARTY_CREATE, PartyResponseCode.NoPermission);
                    return;
                }

                // 관리자 권한 검증 (AccountType == 1)
                if (!sessionManager.IsAdmin(client))
                {
                    Logger.Warning($"Party create denied: {accountId} is not admin");
                    SendErrorResponse(client, TAG_PARTY_CREATE, PartyResponseCode.NoPermission);
                    return;
                }

                // 요청 데이터 파싱
                int maxPlayer = 4;
                string sceneFile = string.Empty;
                string sceneSetFile = string.Empty;
                string partyName = string.Empty;
                string partyInfo = string.Empty;

                using (DarkRiftReader reader = message.GetReader())
                {
                    // 템플릿 파티 존재 여부 확인
                    bool hasTemplate = reader.ReadBoolean();

                    if (hasTemplate)
                    {
                        // 템플릿 파티에서 설정 추출
                        Party templateParty = reader.ReadSerializable<Party>();

                        if (templateParty != null)
                        {
                            sceneFile = templateParty.SceneFile ?? string.Empty;
                            sceneSetFile = templateParty.SceneSetFile ?? string.Empty;
                            maxPlayer = templateParty.MaxPlayer;
                            partyName = templateParty.PartyName ?? string.Empty;
                            partyInfo = templateParty.PartyInfo ?? string.Empty;

                            Logger.Info($"Party create from template: Scene={sceneFile}, SceneSet={sceneSetFile}, MaxPlayer={maxPlayer}");
                        }
                    }
                    else
                    {
                        // 기존 방식: maxPlayer만 전송
                        if (reader.Length >= 4)
                        {
                            maxPlayer = reader.ReadInt32();
                        }
                    }

                    // 최대 인원 검증 (1~16)
                    if (maxPlayer < 1 || maxPlayer > 16)
                    {
                        maxPlayer = 4;
                    }
                }

                lock (partyLock)
                {
                    // 새 파티 ID 생성 (YYYYMMDD + 순차번호)
                    int newPartyId = GeneratePartyId();

                    Party newParty = new Party(newPartyId, sceneFile, sceneSetFile, maxPlayer, partyName, partyInfo);
                    // 생성 요청자는 파티에 가입하지 않음 (교관이 별도로 멤버 추가)

                    parties.Add(newParty);

                    Logger.Info($"Party created: ID={newPartyId}, CreatedBy={accountId}, MaxPlayer={maxPlayer}, Scene={sceneFile}");

                    // 성공 응답 전송
                    SendPartyResponse(client, TAG_PARTY_CREATE, PartyResponseCode.Success, newParty);

                    // 관리자에게 알림
                    NotifyAdmins(AdminNotificationType.PartyCreated, newParty, accountId);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandlePartyCreate error: {ex.Message}");
                SendErrorResponse(client, TAG_PARTY_CREATE, PartyResponseCode.Failed);
            }
        }

        /// <summary>
        /// 파티 파괴 처리
        /// </summary>
        private void HandlePartyDestroy(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    int partyId = reader.ReadInt32();

                    if (sessionManager == null)
                    {
                        SendErrorResponse(client, TAG_PARTY_DESTROY, PartyResponseCode.Failed);
                        return;
                    }

                    string requesterId = sessionManager.GetAccountId(client);
                    if (string.IsNullOrWhiteSpace(requesterId))
                    {
                        SendErrorResponse(client, TAG_PARTY_DESTROY, PartyResponseCode.NoPermission);
                        return;
                    }

                    lock (partyLock)
                    {
                        Party party = FindPartyById(partyId);
                        if (party == null)
                        {
                            SendErrorResponse(client, TAG_PARTY_DESTROY, PartyResponseCode.NotFound);
                            return;
                        }

                        // 파티원들에게 파괴 알림
                        NotifyPartyDestroyed(party);

                        // 파티 제거
                        parties.Remove(party);

                        // 연결된 인스턴스 클린업 (버그 수정: 인스턴스 누수 및 이전 설정 반영 문제 해결)
                        gameInstanceManager?.OnPartyRemoved(partyId);

                        Logger.Info($"Party destroyed: ID={partyId}, Members={party.MemberCount}");

                        // 요청자에게 성공 응답
                        SendSimpleResponse(client, TAG_PARTY_DESTROY, partyId, PartyResponseCode.Success);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandlePartyDestroy error: {ex.Message}");
                SendErrorResponse(client, TAG_PARTY_DESTROY, PartyResponseCode.Failed);
            }
        }

        /// <summary>
        /// 파티 이동 처리
        /// </summary>
        private void HandlePartyMove(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    int fromPartyId = reader.ReadInt32();
                    int toPartyId = reader.ReadInt32();
                    string accountId = reader.ReadString();

                    if (sessionManager == null)
                    {
                        SendErrorResponse(client, TAG_PARTY_MOVE, PartyResponseCode.Failed);
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(accountId))
                    {
                        SendErrorResponse(client, TAG_PARTY_MOVE, PartyResponseCode.NoPermission);
                        return;
                    }

                    lock (partyLock)
                    {
                        Party fromParty = FindPartyById(fromPartyId);
                        Party toParty = FindPartyById(toPartyId);

                        if (fromParty == null || toParty == null)
                        {
                            SendErrorResponse(client, TAG_PARTY_MOVE, PartyResponseCode.NotFound);
                            return;
                        }

                        // 대상 파티가 가득 찼는지 확인
                        if (toParty.IsFull)
                        {
                            SendErrorResponse(client, TAG_PARTY_MOVE, PartyResponseCode.PartyFull);
                            Logger.Warning($"Party move failed: Party {toPartyId} is full ({toParty.MemberCount}/{toParty.MaxPlayer})");
                            return;
                        }

                        // 기존 파티에서 제거
                        if (!fromParty.RemoveMember(accountId))
                        {
                            SendErrorResponse(client, TAG_PARTY_MOVE, PartyResponseCode.NotFound);
                            return;
                        }

                        // 새 파티에 추가 (역할 초기화)
                        if (!toParty.AddMember(accountId, ""))
                        {
                            // 실패 시 원래 파티로 복구
                            fromParty.AddMember(accountId, "");
                            SendErrorResponse(client, TAG_PARTY_MOVE, PartyResponseCode.PartyFull);
                            Logger.Error($"Party move failed: Could not add to party {toPartyId}");
                            return;
                        }

                        Logger.Info($"Party move: {accountId} from Party {fromPartyId} to Party {toPartyId} (Now: {toParty.MemberCount}/{toParty.MaxPlayer})");

                        // 성공 응답 - 새 파티 정보 전송
                        SendPartyResponse(client, TAG_PARTY_MOVE, PartyResponseCode.Success, toParty);

                        // 관리자에게 알림
                        NotifyAdmins(AdminNotificationType.PartyMemberJoined, toParty, accountId, $"Moved from Party {fromPartyId}");
                        NotifyAdmins(AdminNotificationType.PartyMemberLeft, fromParty, accountId, $"Moved to Party {toPartyId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandlePartyMove error: {ex.Message}");
                SendErrorResponse(client, TAG_PARTY_MOVE, PartyResponseCode.Failed);
            }
        }

        /// <summary>
        /// 역할 변경 처리
        /// </summary>
        private void HandlePartyChangeRole(IClient client, Message message)
        {
            try
            {
                // 권한 검증
                if (sessionManager == null || !sessionManager.IsAdmin(client))
                {
                    Logger.Warning($"PartyChangeRole denied: client {client.ID} is not admin");
                    SendErrorResponse(client, TAG_PARTY_CHANGE_ROLE, PartyResponseCode.NoPermission);
                    return;
                }

                using (DarkRiftReader reader = message.GetReader())
                {
                    int partyId = reader.ReadInt32();
                    string targetAccountId = reader.ReadString();
                    string newRole = reader.ReadString();

                    lock (partyLock)
                    {
                        Party party = FindPartyById(partyId);
                        if (party == null)
                        {
                            SendErrorResponse(client, TAG_PARTY_CHANGE_ROLE, PartyResponseCode.NotFound);
                            return;
                        }

                        if (!party.ChangeRole(targetAccountId, newRole))
                        {
                            SendErrorResponse(client, TAG_PARTY_CHANGE_ROLE, PartyResponseCode.NotFound);
                            return;
                        }

                        Logger.Info($"Party role changed: PartyID={partyId}, Account={targetAccountId}, Role={newRole}");

                        // 성공 응답
                        SendPartyResponse(client, TAG_PARTY_CHANGE_ROLE, PartyResponseCode.Success, party);

                        // 관리자에게 알림
                        NotifyAdmins(AdminNotificationType.PartyRoleChanged, party, targetAccountId, $"New role: {newRole}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandlePartyChangeRole error: {ex.Message}");
                SendErrorResponse(client, TAG_PARTY_CHANGE_ROLE, PartyResponseCode.Failed);
            }
        }

        /// <summary>
        /// 파티 설정 변경 처리
        /// </summary>
        private void HandlePartyChangeSetting(IClient client, Message message)
        {
            try
            {
                // 권한 검증
                if (sessionManager == null || !sessionManager.IsAdmin(client))
                {
                    Logger.Warning($"PartyChangeSetting denied: client {client.ID} is not admin");
                    SendErrorResponse(client, TAG_PARTY_CHANGE_SETTING, PartyResponseCode.NoPermission);
                    return;
                }

                using (DarkRiftReader reader = message.GetReader())
                {
                    int partyId = reader.ReadInt32();
                    string sceneFile = reader.ReadString();
                    string sceneSetFile = reader.ReadString();
                    string partyName = reader.ReadString();
                    string partyInfo = reader.ReadString();

                    lock (partyLock)
                    {
                        Party party = FindPartyById(partyId);
                        if (party == null)
                        {
                            SendErrorResponse(client, TAG_PARTY_CHANGE_SETTING, PartyResponseCode.NotFound);
                            return;
                        }

                        // 설정 업데이트
                        party.SceneFile = sceneFile;
                        party.SceneSetFile = sceneSetFile;
                        party.PartyName = partyName;
                        party.PartyInfo = partyInfo;

                        Logger.Info($"Party setting changed: PartyID={partyId}, Scene={sceneFile}, SceneSet={sceneSetFile}");

                        // 성공 응답
                        SendPartyResponse(client, TAG_PARTY_CHANGE_SETTING, PartyResponseCode.Success, party);

                        // 관리자에게 알림
                        NotifyAdmins(AdminNotificationType.PartySettingChanged, party, "", $"Scene: {sceneFile}, SceneSet: {sceneSetFile}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandlePartyChangeSetting error: {ex.Message}");
                SendErrorResponse(client, TAG_PARTY_CHANGE_SETTING, PartyResponseCode.Failed);
            }
        }

        /// <summary>
        /// 파티 목록 조회
        /// </summary>
        private void HandlePartyGetList(IClient client, Message message)
        {
            try
            {
                lock (partyLock)
                {
                    using (DarkRiftWriter writer = DarkRiftWriter.Create())
                    {
                        writer.Write((byte)PartyResponseCode.Success);
                        writer.Write(parties.Count);

                        foreach (var party in parties)
                        {
                            writer.Write(party);
                        }

                        using (Message response = Message.Create(TAG_PARTY_GET_LIST, writer))
                        {
                            client.SendMessage(response, SendMode.Reliable);
                        }
                    }

                    Logger.Trace($"Party list sent: {parties.Count} parties");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandlePartyGetList error: {ex.Message}");
                SendErrorResponse(client, TAG_PARTY_GET_LIST, PartyResponseCode.Failed);
            }
        }

        /// <summary>
        /// 파티 가입 처리 (일반 사용자용)
        /// </summary>
        private void HandlePartyJoin(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    // 클라이언트: accountId(string) → partyId(int) 순서로 전송
                    string accountId = reader.ReadString();
                    int partyId = reader.ReadInt32();

                    if (sessionManager == null)
                    {
                        SendErrorResponse(client, TAG_PARTY_JOIN, PartyResponseCode.Failed);
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(accountId))
                    {
                        SendErrorResponse(client, TAG_PARTY_JOIN, PartyResponseCode.NoPermission);
                        return;
                    }

                    lock (partyLock)
                    {
                        // 이미 다른 파티에 속해있는지 확인
                        Party existingParty = FindPartyByMember(accountId);
                        if (existingParty != null)
                        {
                            SendErrorResponse(client, TAG_PARTY_JOIN, PartyResponseCode.AlreadyInParty);
                            Logger.Warning($"Party join denied: {accountId} already in party {existingParty.PartyId}");
                            return;
                        }

                        // 대상 파티 찾기
                        Party targetParty = FindPartyById(partyId);
                        if (targetParty == null)
                        {
                            SendErrorResponse(client, TAG_PARTY_JOIN, PartyResponseCode.NotFound);
                            return;
                        }

                        // 파티가 가득 찼는지 확인
                        if (targetParty.IsFull)
                        {
                            SendErrorResponse(client, TAG_PARTY_JOIN, PartyResponseCode.PartyFull);
                            Logger.Warning($"Party join denied: Party {partyId} is full ({targetParty.MemberCount}/{targetParty.MaxPlayer})");
                            return;
                        }

                        // 파티에 추가 (역할 없음)
                        if (!targetParty.AddMember(accountId, ""))
                        {
                            SendErrorResponse(client, TAG_PARTY_JOIN, PartyResponseCode.Failed);
                            Logger.Error($"Party join failed: Could not add {accountId} to party {partyId}");
                            return;
                        }

                        Logger.Info($"Party join: {accountId} joined Party {partyId} (Now: {targetParty.MemberCount}/{targetParty.MaxPlayer})");

                        // 성공 응답 - 파티 정보 전송
                        SendPartyResponse(client, TAG_PARTY_JOIN, PartyResponseCode.Success, targetParty);

                        // 관리자에게 알림
                        NotifyAdmins(AdminNotificationType.PartyMemberJoined, targetParty, accountId);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandlePartyJoin error: {ex.Message}");
                SendErrorResponse(client, TAG_PARTY_JOIN, PartyResponseCode.Failed);
            }
        }

        /// <summary>
        /// 파티 탈퇴 처리
        /// </summary>
        private void HandlePartyLeave(IClient client, Message message)
        {
            try
            {
                using (DarkRiftReader reader = message.GetReader())
                {
                    // 클라이언트: accountId(string) → partyId(int) 순서로 전송
                    string accountId = reader.ReadString();
                    int partyId = reader.ReadInt32();

                    if (sessionManager == null)
                    {
                        SendErrorResponse(client, TAG_PARTY_LEAVE, PartyResponseCode.Failed);
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(accountId))
                    {
                        SendErrorResponse(client, TAG_PARTY_LEAVE, PartyResponseCode.NoPermission);
                        return;
                    }

                    lock (partyLock)
                    {
                        Party party = FindPartyById(partyId);
                        if (party == null)
                        {
                            SendErrorResponse(client, TAG_PARTY_LEAVE, PartyResponseCode.NotFound);
                            return;
                        }

                        // 파티에서 제거
                        if (!party.RemoveMember(accountId))
                        {
                            SendErrorResponse(client, TAG_PARTY_LEAVE, PartyResponseCode.NotFound);
                            Logger.Warning($"Party leave failed: {accountId} not in party {partyId}");
                            return;
                        }

                        Logger.Info($"Party leave: {accountId} left Party {partyId} (Now: {party.MemberCount}/{party.MaxPlayer})");

                        // 성공 응답
                        SendSimpleResponse(client, TAG_PARTY_LEAVE, PartyResponseCode.Success);

                        // 관리자에게 알림
                        NotifyAdmins(AdminNotificationType.PartyMemberLeft, party, accountId);

                        // // 파티가 비었으면 제거
                        // if (party.IsEmpty)
                        // {
                        //     int removedPartyId = party.PartyId;
                        //     parties.Remove(party);
                        //     Logger.Info($"Empty party removed: ID={removedPartyId}");
                        //     NotifyAdmins(AdminNotificationType.PartyDestroyed, party, "", "Empty party auto-removed");

                        //     // 연결된 인스턴스 정리
                        //     gameInstanceManager?.OnPartyRemoved(removedPartyId);
                        // }
                        
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"HandlePartyLeave error: {ex.Message}");
                SendErrorResponse(client, TAG_PARTY_LEAVE, PartyResponseCode.Failed);
            }
        }

        /// <summary>
        /// TrainingType 변경 수신 처리 - 요청자가 속한 파티 멤버에게 전파
        /// </summary>
        private void HandleTrainingTypeChange(IClient client, Message message)
        {
            try
            {
                if (sessionManager == null) return;

                string accountId = sessionManager.GetAccountId(client);
                if (string.IsNullOrWhiteSpace(accountId)) return;

                ushort trainingType = message.Tag;
                int partyId;

                using (DarkRiftReader reader = message.GetReader())
                {
                    partyId = reader.ReadInt32();
                }

                SendTrainingTypeToParty(partyId, trainingType);
            }
            catch (Exception ex)
            {
                Logger.Error($"HandleTrainingTypeChange error: {ex.Message}");
            }
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// 파티 ID로 찾기
        /// </summary>
        private Party FindPartyById(int partyId)
        {
            Party fparty = parties.FirstOrDefault(p => p.PartyId == partyId);
            if(fparty == default)
                Logger.Warning($"Party NotFound : ID={partyId}");
            return fparty;

        }

        /// <summary>
        /// 멤버로 파티 찾기
        /// </summary>
        private Party FindPartyByMember(string accountId)
        {
            return parties.FirstOrDefault(p => p.HasMember(accountId));
        }

        /// <summary>
        /// 모든 파티에서 멤버 제거
        /// </summary>
        private void RemoveMemberFromAllParties(string accountId)
        {
            lock (partyLock)
            {
                foreach (var party in parties.ToList())
                {
                    if (party.RemoveMember(accountId))
                    {
                        Logger.Info($"Member removed from party: {accountId} from Party {party.PartyId}");

                        // 관리자에게 알림
                        NotifyAdmins(AdminNotificationType.PartyMemberLeft, party, accountId, "Disconnected");

                        // 파티가 비었으면 제거
                        if (party.IsEmpty)
                        {
                            int removedPartyId = party.PartyId;
                            parties.Remove(party);
                            Logger.Info($"Empty party removed: ID={removedPartyId}");

                            // 관리자에게 파티 파괴 알림
                            NotifyAdmins(AdminNotificationType.PartyDestroyed, party, "", "Empty party auto-removed");

                            // 연결된 인스턴스 정리
                            gameInstanceManager?.OnPartyRemoved(removedPartyId);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 파티 파괴 알림 전송
        /// </summary>
        private void NotifyPartyDestroyed(Party party)
        {
            if (sessionManager == null || party == null)
            {
                return;
            }

            foreach (var accountId in party.AccountIdAndRole.Keys)
            {
                IClient memberClient = sessionManager.GetClient(accountId);
                if (memberClient != null)
                {
                    try
                    {
                        using (DarkRiftWriter writer = DarkRiftWriter.Create())
                        {
                            writer.Write((byte)PartyResponseCode.Success);
                            writer.Write(party.PartyId);

                            using (Message notification = Message.Create(TAG_PARTY_DESTROY, writer))
                            {
                                memberClient.SendMessage(notification, SendMode.Reliable);
                            }
                        }

                        Logger.Trace($"Party destroy notification sent to: {accountId}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to notify party destroyed to {accountId}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 관리자에게 알림 전송
        /// </summary>
        private void NotifyAdmins(AdminNotificationType notificationType, Party party, string affectedAccountId = "", string additionalInfo = "")
        {
            if (sessionManager == null || party == null)
            {
                return;
            }

            var adminClients = sessionManager.GetAdminClients();
            if (adminClients.Count == 0)
            {
                return;
            }

            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write((byte)notificationType);
                    writer.Write(party);
                    writer.Write(affectedAccountId ?? "");
                    writer.Write(additionalInfo ?? "");
                    writer.Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                    using (Message notification = Message.Create(TAG_ADMIN_PARTY_NOTIFICATION, writer))
                    {
                        foreach (var adminClient in adminClients)
                        {
                            try
                            {
                                adminClient.SendMessage(notification, SendMode.Reliable);
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"Failed to send admin notification: {ex.Message}");
                            }
                        }
                    }
                }

                Logger.Trace($"Admin notification sent: {notificationType}, Party {party.PartyId}, {adminClients.Count} admins");
            }
            catch (Exception ex)
            {
                Logger.Error($"NotifyAdmins error: {ex.Message}");
            }
        }

        /// <summary>
        /// 파티 정보 응답 전송
        /// </summary>
        private void SendPartyResponse(IClient client, ushort tag, PartyResponseCode code, Party party)
        {
            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write((byte)code);
                    writer.Write(party);

                    using (Message response = Message.Create(tag, writer))
                    {
                        client.SendMessage(response, SendMode.Reliable);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to send party response: {ex.Message}");
            }
        }

        /// <summary>
        /// 간단한 성공/실패 응답
        /// </summary>
        private void SendSimpleResponse(IClient client, ushort tag, PartyResponseCode code)
        {
            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write((byte)code);

                    using (Message response = Message.Create(tag, writer))
                    {
                        client.SendMessage(response, SendMode.Reliable);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to send simple response: {ex.Message}");
            }
        }

        /// <summary>
        /// 간단한 성공/실패 응답2
        /// </summary>
        private void SendSimpleResponse(IClient client, ushort tag, int responeValue, PartyResponseCode code)
        {
            try
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write((byte)code);
                    writer.Write(responeValue);

                    using (Message response = Message.Create(tag, writer))
                    {
                        client.SendMessage(response, SendMode.Reliable);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to send simple response: {ex.Message}");
            }
        }
        /// <summary>
        /// 에러 응답 전송
        /// </summary>
        private void SendErrorResponse(IClient client, ushort tag, PartyResponseCode code)
        {
            SendSimpleResponse(client, tag, code);
        }

        #endregion

        #region Public API

        /// <summary>
        /// 현재 파티 수
        /// </summary>
        public int PartyCount
        {
            get
            {
                lock (partyLock)
                {
                    return parties.Count;
                }
            }
        }

        /// <summary>
        /// 모든 파티 목록 반환
        /// </summary>
        public IReadOnlyList<Party> GetAllParties()
        {
            lock (partyLock)
            {
                return parties.ToList();
            }
        }

        /// <summary>
        /// 파티 ID로 파티 조회 (외부 API용)
        /// </summary>
        public Party GetPartyById(int partyId)
        {
            lock (partyLock)
            {
                return FindPartyById(partyId);
            }
        }

        /// <summary>
        /// 멤버 ID로 파티 조회 (외부 API용)
        /// </summary>
        public Party GetPartyByMember(string accountId)
        {
            lock (partyLock)
            {
                return FindPartyByMember(accountId);
            }
        }

        /// <summary>
        /// 특정 계정이 특정 파티에 속해있는지 확인
        /// </summary>
        public bool IsMemberOfParty(string accountId, int partyId)
        {
            lock (partyLock)
            {
                Party party = FindPartyById(partyId);
                return party != null && party.HasMember(accountId);
            }
        }

        /// <summary>
        /// 특정 파티의 멤버 목록 반환
        /// </summary>
        public IReadOnlyList<string> GetPartyMembers(int partyId)
        {
            lock (partyLock)
            {
                Party party = FindPartyById(partyId);
                if (party == null)
                {
                    return new List<string>();
                }
                return party.AccountIdAndRole.Keys.ToList();
            }
        }

        /// <summary>
        /// 파티 생성 (HTTP API용) - 클라이언트 연결 없이 파티 데이터를 직접 생성
        /// </summary>
        public Party CreatePartyFromApi(string sceneFile, string sceneSetFile, int maxPlayer, string partyName, string partyInfo, Dictionary<string, string> members = null)
        {
            if (maxPlayer < 1 || maxPlayer > 16) maxPlayer = 4;

            lock (partyLock)
            {
                // 새 파티 ID 생성 (YYYYMMDD + 순차번호)
                int newPartyId = GeneratePartyId();

                Party newParty = new Party(newPartyId, sceneFile, sceneSetFile, maxPlayer, partyName, partyInfo);

                // 멤버가 지정된 경우 추가
                if (members != null)
                {
                    foreach (var kvp in members)
                    {
                        newParty.AddMember(kvp.Key, kvp.Value);
                    }
                }

                parties.Add(newParty);

                Logger.Info($"[API] Party created: ID={newPartyId}, Name={partyName}, MaxPlayer={maxPlayer}, Scene={sceneFile}, Members={newParty.MemberCount}");

                NotifyAdmins(AdminNotificationType.PartyCreated, newParty, "API");

                return newParty;
            }
        }

        /// <summary>
        /// 파티에 멤버 강제 추가 (HTTP API/TUI용)
        /// </summary>
        public bool ForceAddMember(int partyId, string accountId, string role = "")
        {
            if (string.IsNullOrWhiteSpace(accountId)) return false;

            lock (partyLock)
            {
                Party party = FindPartyById(partyId);
                if (party == null)
                {
                    Logger.Warning($"[API] ForceAddMember: Party {partyId} not found");
                    return false;
                }

                if (party.HasMember(accountId))
                {
                    Logger.Warning($"[API] ForceAddMember: {accountId} already in party {partyId}");
                    return false;
                }

                if (party.IsFull)
                {
                    Logger.Warning($"[API] ForceAddMember: Party {partyId} is full ({party.MemberCount}/{party.MaxPlayer})");
                    return false;
                }

                // 다른 파티에서 먼저 제거
                Party existingParty = FindPartyByMember(accountId);
                if (existingParty != null)
                {
                    existingParty.RemoveMember(accountId);
                    Logger.Info($"[API] ForceAddMember: {accountId} removed from party {existingParty.PartyId} before adding to {partyId}");
                    NotifyAdmins(AdminNotificationType.PartyMemberLeft, existingParty, accountId, "Force moved");
                }

                if (!party.AddMember(accountId, role ?? ""))
                {
                    return false;
                }

                Logger.Info($"[API] ForceAddMember: {accountId} added to party {partyId} with role '{role}' (Now: {party.MemberCount}/{party.MaxPlayer})");
                NotifyAdmins(AdminNotificationType.PartyMemberJoined, party, accountId, "Force added via API");
                return true;
            }
        }

        /// <summary>
        /// 파티에서 멤버 강제 제거 (HTTP API/TUI용)
        /// </summary>
        public bool ForceRemoveMember(int partyId, string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId)) return false;

            lock (partyLock)
            {
                Party party = FindPartyById(partyId);
                if (party == null)
                {
                    Logger.Warning($"[API] ForceRemoveMember: Party {partyId} not found");
                    return false;
                }

                if (!party.RemoveMember(accountId))
                {
                    Logger.Warning($"[API] ForceRemoveMember: {accountId} not in party {partyId}");
                    return false;
                }

                Logger.Info($"[API] ForceRemoveMember: {accountId} removed from party {partyId} (Now: {party.MemberCount}/{party.MaxPlayer})");
                NotifyAdmins(AdminNotificationType.PartyMemberLeft, party, accountId, "Force removed via API");

                // 파티가 비었으면 제거
                if (party.IsEmpty)
                {
                    int removedPartyId = party.PartyId;
                    parties.Remove(party);
                    Logger.Info($"[API] Empty party removed: ID={removedPartyId}");
                    NotifyAdmins(AdminNotificationType.PartyDestroyed, party, "", "Empty party auto-removed");

                    // 연결된 인스턴스 정리
                    gameInstanceManager?.OnPartyRemoved(removedPartyId);
                }

                return true;
            }
        }

        /// <summary>
        /// 파티 멤버 역할 변경 (HTTP API/TUI용)
        /// </summary>
        public bool ForceChangeRole(int partyId, string accountId, string newRole)
        {
            if (string.IsNullOrWhiteSpace(accountId)) return false;

            lock (partyLock)
            {
                Party party = FindPartyById(partyId);
                if (party == null)
                {
                    Logger.Warning($"[API] ForceChangeRole: Party {partyId} not found");
                    return false;
                }

                if (!party.ChangeRole(accountId, newRole ?? ""))
                {
                    Logger.Warning($"[API] ForceChangeRole: {accountId} not in party {partyId}");
                    return false;
                }

                Logger.Info($"[API] ForceChangeRole: {accountId} in party {partyId} -> role '{newRole}'");
                NotifyAdmins(AdminNotificationType.PartyRoleChanged, party, accountId, $"New role: {newRole}");
                return true;
            }
        }

        /// <summary>
        /// 파티 강제 해산 (HTTP API용)
        /// </summary>
        public bool ForceDisbandParty(int partyId)
        {
            lock (partyLock)
            {
                Party party = FindPartyById(partyId);
                if (party == null)
                {
                    return false;
                }

                Logger.Info($"[API] Force disbanding party: ID={partyId}, Members={party.MemberCount}");

                // 파티원들에게 파괴 알림
                NotifyPartyDestroyed(party);

                // 관리자에게 알림
                NotifyAdmins(AdminNotificationType.PartyDestroyed, party, "API");

                // 파티 제거
                parties.Remove(party);

                // 연결된 인스턴스 정리
                gameInstanceManager?.OnPartyRemoved(partyId);

                return true;
            }
        }

        /// <summary>
        /// 파티 ID에 해당하는 모든 멤버에게 TrainingType 메시지 전송
        /// </summary>
        public void SendTrainingTypeToParty(int partyId, ushort trainingType)
        {
            if (sessionManager == null) return;

            lock (partyLock)
            {
                Party party = FindPartyById(partyId);
                if (party == null)
                {
                    Logger.Warning($"SendTrainingTypeToParty: Party {partyId} not found");
                    return;
                }

                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    writer.Write(trainingType);

                    using (Message msg = Message.Create(trainingType, writer))
                    {
                        foreach (var accountId in party.AccountIdAndRole.Keys)
                        {
                            IClient memberClient = sessionManager.GetClient(accountId);
                            if (memberClient != null)
                            {
                                try
                                {
                                    memberClient.SendMessage(msg, SendMode.Reliable);
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error($"SendTrainingTypeToParty: Failed to send to {accountId}: {ex.Message}");
                                }
                            }
                        }
                    }
                }

                Logger.Info($"TrainingType {trainingType} sent to party {partyId} ({party.MemberCount} members)");
            }
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (partyLock)
                {
                    parties.Clear();
                }
                Logger.Info("PartyManager disposed.");
            }

            base.Dispose(disposing);
        }
    }
}