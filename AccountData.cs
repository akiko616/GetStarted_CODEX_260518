using DarkRift;
using System;

namespace DatabasePlugin
{
    #region Message Tags
    /// <summary>
    /// 메시지 태그 중앙 관리
    /// </summary>
    public static class MessageTags
    {
        #region Auth (1-99)
        /// <summary>로그인</summary>
        public const ushort Login = 1;
        /// <summary>회원가입</summary>
        public const ushort Register = 2;
        /// <summary>계정 조회</summary>
        public const ushort GetAccount = 3;
        /// <summary>계정 업데이트</summary>
        public const ushort UpdateAccount = 4;
        /// <summary>계정 삭제</summary>
        public const ushort DeleteAccount = 5;
        /// <summary>비밀번호 변경</summary>
        public const ushort ChangePassword = 6;
        /// <summary>타입별 계정 조회</summary>
        public const ushort GetTypeAccounts = 7;
        #endregion

        #region Party (100-109)
        /// <summary>파티 생성</summary>
        public const ushort PartyCreate = 100;
        /// <summary>파티 파괴</summary>
        public const ushort PartyDestroy = 101;
        /// <summary>파티 이동</summary>
        public const ushort PartyMove = 102;
        /// <summary>파티 역할 변경</summary>
        public const ushort PartyChangeRole = 103;
        /// <summary>파티 설정 변경</summary>
        public const ushort PartyChangeSetting = 104;
        /// <summary>파티 목록 조회</summary>
        public const ushort PartyGetList = 105;
        /// <summary>파티 가입 (일반 사용자)</summary>
        public const ushort PartyJoin = 106;
        /// <summary>파티 탈퇴</summary>
        public const ushort PartyLeave = 107;
        /// <summary>파티 멤버 알림 (서버 -> 파티 멤버)</summary>
        public const ushort PartyMemberNotification = 108;
        /// <summary>관리자 파티 알림</summary>
        public const ushort AdminPartyNotification = 110;
        #endregion

        #region Game Instance (111-119, 200-219)
        /// <summary>관리자 모든 인스턴스 조회</summary>
        public const ushort AdminGetAllInstances = 111;
        /// <summary>관리자 관전 요청</summary>
        public const ushort AdminSpectateRequest = 112;
        /// <summary>관리자 인스턴스 상태 알림</summary>
        public const ushort AdminInstanceNotification = 113;

        /// <summary>[Deprecated] 훈련 시작 요청 — 교관 주도 플로우(TrainingSetup)로 대체됨</summary>
        public const ushort TrainingStart = 200;
        /// <summary>인스턴스 등록 (인스턴스 -> 서버)</summary>
        public const ushort InstanceRegister = 201;
        /// <summary>인스턴스 준비 완료 (인스턴스 -> 서버, 서버 -> 파티)</summary>
        public const ushort InstanceReady = 202;
        /// <summary>인스턴스 하트비트 (인스턴스 -> 서버)</summary>
        public const ushort InstanceHeartbeat = 203;
        /// <summary>인스턴스 종료 (인스턴스 -> 서버, 서버 -> 파티)</summary>
        public const ushort InstanceShutdown = 204;
        /// <summary>훈련 정보 (서버 -> 클라이언트)</summary>
        public const ushort TrainingInfo = 205;
        /// <summary>파티 데이터 (서버 -> 인스턴스)</summary>
        public const ushort PartyData = 206;

        // 인스턴스 신호 (Instance <-> Server <-> Party)
        /// <summary>훈련 준비 신호 (인스턴스 -> 서버 -> 파티+인스턴스)</summary>
        public const ushort InstanceSignalReady = 210;
        /// <summary>훈련 시작 신호 (인스턴스 -> 서버 -> 파티+인스턴스)</summary>
        public const ushort InstanceSignalStart = 211;
        /// <summary>훈련 종료 신호 (인스턴스 -> 서버 -> 파티+인스턴스)</summary>
        public const ushort InstanceSignalStop = 212;
        /// <summary>인스턴스 신호 브로드캐스트 (서버 -> 파티+인스턴스)</summary>
        public const ushort InstanceSignalBroadcast = 213;
        #endregion

        #region Admin Notifications (120-129)
        /// <summary>관리자 사용자 로그인 알림</summary>
        public const ushort AdminUserLoginNotification = 120;
        /// <summary>관리자 사용자 로그아웃 알림</summary>
        public const ushort AdminUserLogoutNotification = 121;
        /// <summary>관리자 온라인 사용자 조회</summary>
        public const ushort AdminGetOnlineUsers = 122;
        #endregion

        #region Training Control (220-239) - 교관 주도 훈련 플로우
        /// <summary>파티 확정 (교관 -> 서버 -> 파티원) - 최종 파티 구성 전송</summary>
        public const ushort PartyConfirm = 220;
        /// <summary>[Deprecated] 기존 훈련 생성준비 - TrainingSetup 사용</summary>
        public const ushort TrainingPrepare = 221;
        /// <summary>훈련 설정 (교관 -> 서버) - 인스턴스 프로세스 실행 요청</summary>
        public const ushort TrainingSetup = 222;
        /// <summary>[Deprecated] 기존 훈련 생성완료 - TrainingCreated(229) 사용</summary>
        public const ushort TrainingCreatedLegacy = 223;
        /// <summary>훈련 시작 (교관 -> 서버 -> 인스턴스/훈련생) - 실제 게임플레이 시작</summary>
        public const ushort TrainingBegin = 224;
        /// <summary>훈련 상태 알림 (서버 -> 교관) - 파티원들의 준비 상태</summary>
        public const ushort TrainingStatusNotification = 225;

        // 새 훈련 플로우 (226-239)
        /// <summary>인스턴스 기상 (인스턴스 -> 서버) - 인스턴스 실행 후 IP/Port 전달</summary>
        public const ushort TrainingWake = 226;
        /// <summary>인스턴스 기상 응답 (서버 -> 인스턴스/훈련생) - 파티 및 훈련 정보 전달</summary>
        public const ushort TrainingWakeResult = 227;
        /// <summary>훈련 생성 (인스턴스 -> 서버) - 인스턴스 Fishnet 서버 준비 완료</summary>
        public const ushort TrainingCreate = 228;
        /// <summary>훈련 생성됨 (서버 -> 훈련생) - Fishnet 접속 정보 및 준비 알림</summary>
        public const ushort TrainingCreated = 229;
        /// <summary>훈련 시작 준비 (인스턴스 -> 서버 -> 교관) - 모든 파티원 Fishnet 접속 완료</summary>
        public const ushort TrainingBeginReady = 230;
        /// <summary>훈련 일시정지/재개 토글 (교관 -> 서버 -> 파티+인스턴스)</summary>
        public const ushort TrainingPause = 231;
        /// <summary>훈련 종료 (교관 -> 서버 -> 파티+인스턴스)</summary>
        public const ushort TrainingExits = 232;
        /// <summary>핀 정보 (교관 -> 서버 -> 파티 교육생) - 위치 + 핀 종류</summary>
        public const ushort PinInfo = 233;
        /// <summary>훈련 중간 참여 (교관 -> 서버: 파티에 view 역할로 추가)</summary>
        public const ushort TrainingMidJoin = 234;
        /// <summary>훈련 중간 탈퇴 (교관 -> 서버: 파티에서 교관 제거)</summary>
        public const ushort TrainingMidLeave = 235;
        public const ushort TrainResultPush = 236;
        public const ushort TrainResultPull = 237;
        /// <summary>우아한 종료 (교관→서버→훈련생 배포 / 훈련생→서버 ACK)</summary>
        public const ushort GracefulEnd = 238;
        /// <summary>데디케이트 종료 신호 (서버→데디케이트) - 훈련생 전원 ACK 후 전송</summary>
        public const ushort GracefulQuit = 239;
        /// <summary>서버 공지 브로드캐스트 (서버 -> 전체 클라이언트)</summary>
        public const ushort ServerNotice = 240;
        /// <summary>훈련생 → 서버: 훈련 종료 준비 완료 알림 (partyId)</summary>
        public const ushort GracefulEndReady = 241;
        /// <summary>서버 → 교관: 파티 전원 종료 준비 완료 또는 150초 타임아웃 알림 (partyId)</summary>
        public const ushort GracefulEndRequest = 242;
        /// <summary>서버 → 교육생: RTT 측정용 핑 프로브</summary>
        public const ushort PingProbe = 243;
        /// <summary>교육생 → 서버: 핑 프로브 응답 (sentMs echo + 하드웨어 상태코드)</summary>
        public const ushort PingProbeAck = 244;
        /// <summary>서버 → 교관: 파티원별 핑/하드웨어 상태 현황</summary>
        public const ushort PartyPingStatus = 246;

        /// <summary>테스트용 - 인스턴스 대기 전용 (TUI -> 서버 -> 인스턴스 IClient)</summary>
        public const ushort UnknownDedicate = 250;
        public const ushort UnknownDedicateRespone = 251;
        #endregion

        #region Chat (260-269) - 문자 채팅
        /// <summary>교관 공지 메시지 (교관 → 서버, isAlarm=true)</summary>
        public const ushort AnnounceText = 260;
        /// <summary>일반 채팅 메시지 (교육생 → 서버 → 파티원, isAlarm=false) / 서버 → 파티원 브로드캐스트 공용</summary>
        public const ushort SendText = 261;
        #endregion

        #region Stream Control (270-279) - 스트림 제어
        /// <summary>스트림 명령 (교관 → 서버): userId + status(0=Off,1=On). 서버는 LiveKit 토큰 발급 후 교육생에게 PublishStream 전달</summary>
        public const ushort OrderStream = 270;
        /// <summary>스트림 배포 (서버 → 교육생): status + LiveKit 토큰. 특정 교육생 1인에게만 전송</summary>
        public const ushort PublishStream = 271;
        #endregion

        #region Client Log (280-289) - 클라이언트 로그
        /// <summary>클라이언트 로그 (클라이언트/인스턴스 → 서버 콘솔 출력)</summary>
        public const ushort ClientLog = 280;
        #endregion

        #region Training Type (1000-1019) - 훈련 유형
        /// <summary>훈련 유형 1</summary>
        public const ushort TrainingType1 = 1000;
        /// <summary>훈련 유형 2</summary>
        public const ushort TrainingType2 = 1001;
        /// <summary>훈련 유형 3</summary>
        public const ushort TrainingType3 = 1002;
        /// <summary>훈련 유형 4</summary>
        public const ushort TrainingType4 = 1003;
        /// <summary>훈련 유형 5</summary>
        public const ushort TrainingType5 = 1004;
        /// <summary>훈련 유형 6</summary>
        public const ushort TrainingType6 = 1005;
        /// <summary>훈련 유형 7</summary>
        public const ushort TrainingType7 = 1006;
        /// <summary>훈련 유형 8</summary>
        public const ushort TrainingType8 = 1007;
        /// <summary>훈련 유형 9</summary>
        public const ushort TrainingType9 = 1008;
        /// <summary>훈련 유형 10</summary>
        public const ushort TrainingType10 = 1009;
        /// <summary>훈련 유형 11</summary>
        public const ushort TrainingType11 = 1010;
        /// <summary>훈련 유형 12</summary>
        public const ushort TrainingType12 = 1011;
        /// <summary>훈련 유형 13</summary>
        public const ushort TrainingType13 = 1012;
        /// <summary>훈련 유형 14</summary>
        public const ushort TrainingType14 = 1013;
        /// <summary>훈련 유형 15</summary>
        public const ushort TrainingType15 = 1014;
        /// <summary>훈련 유형 16</summary>
        public const ushort TrainingType16 = 1015;
        /// <summary>훈련 유형 17</summary>
        public const ushort TrainingType17 = 1016;
        /// <summary>훈련 유형 18</summary>
        public const ushort TrainingType18 = 1017;
        /// <summary>훈련 유형 19</summary>
        public const ushort TrainingType19 = 1018;
        /// <summary>훈련 유형 20</summary>
        public const ushort TrainingType20 = 1019;
        #endregion
    }
    #endregion

    #region Response Codes
    /// <summary>
    /// 공통 응답 코드 (Auth 관련)
    /// </summary>
    public enum AuthResponseCode : byte
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

    /// <summary>
    /// 계정 타입 열거형
    /// </summary>
    public enum AccountTypeEnum : int
    {
        /// <summary>일반 사용자</summary>
        User = 0,
        /// <summary>관리자</summary>
        Admin = 1,
        /// <summary>슈퍼 관리자</summary>
        SuperAdmin = 2
    }
    #endregion

    #region Account Data Classes
    /// <summary>
    /// Account 테이블 데이터 구조 (서버 내부용, DB 저장용)
    /// </summary>
    public class AccountData : IDarkRiftSerializable
    {
        public string Id { get; set; }
        public string PasswordHash { get; set; }
        public int AccountIndex { get; set; }
        public int AccountType { get; set; }
        public string UserInfo { get; set; }
        public DateTime CreatedAt { get; set; }

        public AccountData()
        {
            Id = string.Empty;
            PasswordHash = string.Empty;
            AccountIndex = 0;
            AccountType = 0;
            UserInfo = string.Empty;
            CreatedAt = DateTime.MinValue;
        }

        public AccountData(string id, string passwordHash, int accountIndex = 0, int accountType = 0, string userInfo = "", DateTime? createdAt = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            PasswordHash = passwordHash ?? throw new ArgumentNullException(nameof(passwordHash));
            AccountIndex = accountIndex;
            AccountType = accountType;
            UserInfo = userInfo ?? string.Empty;
            CreatedAt = createdAt ?? DateTime.Now;
        }

        public void Deserialize(DeserializeEvent e)
        {
            Id = e.Reader.ReadString();
            PasswordHash = e.Reader.ReadString();
            AccountIndex = e.Reader.ReadInt32();
            AccountType = e.Reader.ReadInt32();
            UserInfo = e.Reader.ReadString();
            CreatedAt = DateTime.FromBinary(e.Reader.ReadInt64());
        }

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(Id);
            e.Writer.Write(PasswordHash);
            e.Writer.Write(AccountIndex);
            e.Writer.Write(AccountType);
            e.Writer.Write(UserInfo ?? string.Empty);
            e.Writer.Write(CreatedAt.ToBinary());
        }

        /// <summary>
        /// 데이터 유효성 검증
        /// </summary>
        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(Id) &&
                   !string.IsNullOrWhiteSpace(PasswordHash) &&
                   Id.Length >= 1 &&
                   Id.Length <= 50;
        }

        /// <summary>
        /// 클라이언트 전송용 응답 객체 변환 (PasswordHash 제외)
        /// </summary>
        public AccountResponse ToResponse()
        {
            return new AccountResponse(Id, AccountIndex, AccountType, UserInfo, CreatedAt);
        }

        public override string ToString()
        {
            return $"Account[ID:{Id}, Index:{AccountIndex}, Type:{AccountType}, UserInfo:{UserInfo}, Created:{CreatedAt:yyyy-MM-dd HH:mm:ss}]";
        }
    }

    /// <summary>
    /// 클라이언트 응답용 계정 정보 (PasswordHash 제외)
    /// </summary>
    public class AccountResponse : IDarkRiftSerializable
    {
        public string Id { get; set; }
        public int AccountIndex { get; set; }
        public int AccountType { get; set; }
        public string UserInfo { get; set; }
        public DateTime CreatedAt { get; set; }

        public AccountResponse()
        {
            Id = string.Empty;
            AccountIndex = 0;
            AccountType = 0;
            UserInfo = string.Empty;
            CreatedAt = DateTime.MinValue;
        }

        public AccountResponse(string id, int accountIndex, int accountType, string userInfo, DateTime createdAt)
        {
            Id = id ?? string.Empty;
            AccountIndex = accountIndex;
            AccountType = accountType;
            UserInfo = userInfo ?? string.Empty;
            CreatedAt = createdAt;
        }

        public void Deserialize(DeserializeEvent e)
        {
            Id = e.Reader.ReadString();
            AccountIndex = e.Reader.ReadInt32();
            AccountType = e.Reader.ReadInt32();
            UserInfo = e.Reader.ReadString();
            CreatedAt = DateTime.FromBinary(e.Reader.ReadInt64());
        }

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(Id);
            e.Writer.Write(AccountIndex);
            e.Writer.Write(AccountType);
            e.Writer.Write(UserInfo ?? string.Empty);
            e.Writer.Write(CreatedAt.ToBinary());
        }

        /// <summary>데이터 유효성 검증</summary>
        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(Id) &&
                   Id.Length >= 1 &&
                   Id.Length <= 50;
        }

        public override string ToString()
        {
            return $"AccountResponse[ID:{Id}, Index:{AccountIndex}, Type:{AccountType}, UserInfo:{UserInfo}, Created:{CreatedAt:yyyy-MM-dd HH:mm:ss}]";
        }
    }

    /// <summary>
    /// 온라인 사용자 정보 (관리자용)
    /// </summary>
    [Serializable]
    public class OnlineUserInfo : IDarkRiftSerializable
    {
        public string AccountId { get; set; }
        public ushort ClientId { get; set; }
        public int AccountType { get; set; }
        public DateTime LoginTime { get; set; }

        public OnlineUserInfo()
        {
            AccountId = string.Empty;
            ClientId = 0;
            AccountType = 0;
            LoginTime = DateTime.Now;
        }

        public void Deserialize(DeserializeEvent e)
        {
            AccountId = e.Reader.ReadString();
            ClientId = e.Reader.ReadUInt16();
            AccountType = e.Reader.ReadInt32();
            LoginTime = DateTime.FromBinary(e.Reader.ReadInt64());
        }

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(AccountId);
            e.Writer.Write(ClientId);
            e.Writer.Write(AccountType);
            e.Writer.Write(LoginTime.ToBinary());
        }

        /// <summary>접속 시간 (분 단위)</summary>
        public int OnlineMinutes => (int)(DateTime.Now - LoginTime).TotalMinutes;

        /// <summary>계정 타입 텍스트</summary>
        public string AccountTypeText => AccountType == 1 ? "관리자" : "사용자";

        public override string ToString()
        {
            return $"[{AccountTypeText}] {AccountId} (접속: {LoginTime:HH:mm:ss}, {OnlineMinutes}분)";
        }
    }
    #endregion
}
