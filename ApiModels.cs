using System;
using System.Collections.Generic;

namespace DrkRft_Shared.Api
{
    /// <summary>
    /// API 응답 래퍼
    /// </summary>
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public T Data { get; set; }
        public string Error { get; set; }

        public static ApiResponse<T> Ok(T data)
        {
            return new ApiResponse<T> { Success = true, Data = data };
        }

        public static ApiResponse<T> Fail(string error)
        {
            return new ApiResponse<T> { Success = false, Error = error };
        }
    }

    /// <summary>
    /// 액션 결과 (킥, 해산, 중지 등)
    /// </summary>
    public class ActionResultDto
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// 서버 전체 상태
    /// </summary>
    public class ServerStatusDto
    {
        public string Status { get; set; }
        public DateTime Timestamp { get; set; }
        public int ActiveSessions { get; set; }
        public int AdminCount { get; set; }
        public int UserCount { get; set; }
        public int ActiveInstances { get; set; }
        public int TotalInstances { get; set; }
        public int PartyCount { get; set; }
        public double UptimeSeconds { get; set; }
    }

    /// <summary>
    /// 온라인 유저 정보
    /// </summary>
    public class OnlineUserDto
    {
        public string AccountId { get; set; }
        public ushort ClientId { get; set; }
        public int AccountType { get; set; }
        public string AccountTypeText { get; set; }
        public DateTime LoginTime { get; set; }
        public double OnlineMinutes { get; set; }
        public bool IsVirtual { get; set; }
    }

    /// <summary>
    /// 파티 정보
    /// </summary>
    public class PartyInfoDto
    {
        public int PartyId { get; set; }
        public string PartyName { get; set; }
        public string PartyInfo { get; set; }
        public string SceneFile { get; set; }
        public string SceneSetFile { get; set; }
        public int MaxPlayer { get; set; }
        public int MemberCount { get; set; }
        public List<PartyMemberDto> Members { get; set; }
    }

    /// <summary>
    /// 파티 멤버 정보
    /// </summary>
    public class PartyMemberDto
    {
        public string AccountId { get; set; }
        public string Role { get; set; }
    }

    /// <summary>
    /// 게임 인스턴스 정보
    /// </summary>
    public class InstanceInfoDto
    {
        public int InstanceId { get; set; }
        public int Port { get; set; }
        public string State { get; set; }
        public int? AssignedPartyId { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? LastHeartbeat { get; set; }
        public bool IsAvailable { get; set; }
    }

    /// <summary>
    /// 브로드캐스트 요청
    /// </summary>
    public class BroadcastRequest
    {
        public string Message { get; set; }
    }

    /// <summary>
    /// 킥 요청
    /// </summary>
    public class KickRequest
    {
        public string Reason { get; set; }
    }

    /// <summary>
    /// 파티 생성 요청
    /// </summary>
    public class CreatePartyRequest
    {
        public string PartyName { get; set; }
        public string PartyInfo { get; set; }
        public string SceneFile { get; set; }
        public string SceneSetFile { get; set; }
        public int MaxPlayer { get; set; }
        public Dictionary<string, string> Members { get; set; }
        public bool RequestInstance { get; set; }
    }

    /// <summary>
    /// 파티 생성 + 인스턴스 요청 결과
    /// </summary>
    public class CreatePartyResultDto
    {
        public PartyInfoDto Party { get; set; }
        public InstanceInfoDto Instance { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// 가상 유저 등록 요청
    /// </summary>
    public class RegisterVirtualUserRequest
    {
        public string AccountId { get; set; }
        public int AccountType { get; set; }
    }

    /// <summary>
    /// 파티 멤버 추가 요청
    /// </summary>
    public class AddPartyMemberRequest
    {
        public string AccountId { get; set; }
        public string Role { get; set; }
    }

    /// <summary>
    /// 파티 멤버 제거 요청
    /// </summary>
    public class RemovePartyMemberRequest
    {
        public string AccountId { get; set; }
    }

    /// <summary>
    /// 파티 멤버 역할 변경 요청
    /// </summary>
    public class ChangeRoleRequest
    {
        public string AccountId { get; set; }
        public string Role { get; set; }
    }

    /// <summary>
    /// 인스턴스에 파티 할당 요청 (개발/테스트용)
    /// </summary>
    public class AssignPartyRequest
    {
        public int PartyId { get; set; }
    }

    /// <summary>POST /api/parties/{partyId}/train-result-push 요청 바디</summary>
    public class TrainResultPushApiRequest
    {
        public string TrainingType { get; set; } = "Manual";
    }
}
