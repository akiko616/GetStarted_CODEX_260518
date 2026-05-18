using DarkRift;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GameInstancePlugin
{
    /// <summary>
    /// 게임 인스턴스 상태
    /// </summary>
    public enum GameInstanceState : byte
    {
        Idle = 0,           // 유휴 상태 (사용 가능)
        Starting = 1,       // 프로세스 시작 중
        Registered = 2,     // Logic Server에 등록 완료
        Ready = 3,          // 훈련 준비 완료
        Running = 4,        // 훈련 진행 중
        Stopping = 5        // 종료 중
    }

    /// <summary>
    /// 인스턴스 신호 타입 (인스턴스 <-> 서버 <-> 파티 브로드캐스트)
    /// </summary>
    public enum InstanceSignalType : byte
    {
        /// <summary>인스턴스 생성 완료 (프로세스 실행 후 DarkRift 연결/등록 완료)</summary>
        Created = 0,
        /// <summary>훈련 준비 완료 (모든 참가자 씬 로드 완료 등)</summary>
        Ready = 1,
        /// <summary>훈련 시작 (게임 플레이 시작)</summary>
        Start = 2,
        /// <summary>훈련 종료 (게임 플레이 종료)</summary>
        Stop = 3,
        /// <summary>훈련 일시정지</summary>
        Pause = 4,
        /// <summary>훈련 재개</summary>
        Resume = 5
    }

    /// <summary>
    /// 게임 인스턴스 정보 (공유 - 클라이언트/서버 공통)
    /// </summary>
    public class GameInstanceData : IDarkRiftSerializable
    {
        public int InstanceId { get; set; }              // 인스턴스 ID (1-10)
        public int Port { get; set; }                    // 게임 서버 포트 (7001-7010)
        public GameInstanceState State { get; set; }      // 현재 상태
        public int? AssignedPartyId { get; set; }        // 할당된 파티 ID
        public DateTime StartTime { get; set; }          // 시작 시간
        public DateTime? LastHeartbeat { get; set; }     // 마지막 하트비트

        public GameInstanceData()
        {
            InstanceId = 0;
            Port = 0;
            State = GameInstanceState.Idle;
            AssignedPartyId = null;
            StartTime = DateTime.MinValue;
            LastHeartbeat = null;
        }

        public GameInstanceData(int instanceId, int port)
        {
            InstanceId = instanceId;
            Port = port;
            State = GameInstanceState.Idle;
            AssignedPartyId = null;
            StartTime = DateTime.MinValue;
            LastHeartbeat = null;
        }

        public void Deserialize(DeserializeEvent e)
        {
            InstanceId = e.Reader.ReadInt32();
            Port = e.Reader.ReadInt32();
            State = (GameInstanceState)e.Reader.ReadByte();

            bool hasPartyId = e.Reader.ReadBoolean();
            AssignedPartyId = hasPartyId ? e.Reader.ReadInt32() : (int?)null;
        }

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(InstanceId);
            e.Writer.Write(Port);
            e.Writer.Write((byte)State);

            bool hasPartyId = AssignedPartyId.HasValue;
            e.Writer.Write(hasPartyId);
            if (hasPartyId)
            {
                e.Writer.Write(AssignedPartyId.Value);
            }
        }

        /// <summary>
        /// 인스턴스가 사용 가능한지 확인
        /// </summary>
        public bool IsAvailable => State == GameInstanceState.Idle;

        /// <summary>
        /// 하트비트 타임아웃 확인 (30초)
        /// </summary>
        public bool IsHeartbeatTimeout()
        {
            if (!LastHeartbeat.HasValue)
                return false;

            return (DateTime.Now - LastHeartbeat.Value).TotalSeconds > 30;
        }

        public override string ToString()
        {
            return $"GameInstance[ID:{InstanceId}, Port:{Port}, State:{State}, Party:{AssignedPartyId?.ToString() ?? "None"}]";
        }
    }

    /// <summary>
    /// [Obsolete] 훈련 시작 요청 데이터 — 교관 주도 플로우(TrainingSetup)로 대체됨
    /// </summary>
    [Obsolete("교관 주도 훈련 플로우(TrainingSetup)로 대체됨. 제거 예정.")]
    public class TrainingStartRequest : IDarkRiftSerializable
    {
        public int PartyId { get; set; }
        public string SceneFile { get; set; }
        public string SceneSetFile { get; set; }

        public TrainingStartRequest()
        {
            PartyId = 0;
            SceneFile = string.Empty;
            SceneSetFile = string.Empty;
        }

        public TrainingStartRequest(int partyId, string sceneFile, string sceneSetFile = "")
        {
            PartyId = partyId;
            SceneFile = sceneFile ?? string.Empty;
            SceneSetFile = sceneSetFile ?? string.Empty;
        }

        public void Deserialize(DeserializeEvent e)
        {
            PartyId = e.Reader.ReadInt32();
            SceneFile = e.Reader.ReadString();
            SceneSetFile = e.Reader.ReadString();
        }

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(PartyId);
            e.Writer.Write(SceneFile);
            e.Writer.Write(SceneSetFile);
        }
    }

    #region Training Control Data (교관 주도 훈련 플로우)

    /// <summary>
    /// [Obsolete] 훈련 설정 요청 — 서버에서 수동 Read로 처리하므로 역직렬화에 사용되지 않음
    /// </summary>
    [Obsolete("서버에서 수동 Reader.Read로 처리. 제거 예정.")]
    public class TrainingSetupRequest : IDarkRiftSerializable
    {
        public int PartyId { get; set; }
        public string SceneFile { get; set; }
        public string SceneSetFile { get; set; }

        public TrainingSetupRequest()
        {
            PartyId = 0;
            SceneFile = string.Empty;
            SceneSetFile = string.Empty;
        }

        public TrainingSetupRequest(int partyId, string sceneFile, string sceneSetFile = "")
        {
            PartyId = partyId;
            SceneFile = sceneFile ?? string.Empty;
            SceneSetFile = sceneSetFile ?? string.Empty;
        }

        public void Deserialize(DeserializeEvent e)
        {
            PartyId = e.Reader.ReadInt32();
            SceneFile = e.Reader.ReadString();
            SceneSetFile = e.Reader.ReadString();
        }

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(PartyId);
            e.Writer.Write(SceneFile);
            e.Writer.Write(SceneSetFile);
        }

        public override string ToString()
        {
            return $"TrainingSetupRequest[Party:{PartyId}, Scene:{SceneFile}, SceneSet:{SceneSetFile}]";
        }
    }

    /// <summary>
    /// 인스턴스 기상 (인스턴스 -> 서버)
    /// 인스턴스 실행 후 Fishnet IP/Port 전달
    /// </summary>
    public class TrainingWakeData : IDarkRiftSerializable
    {
        public int InstanceId { get; set; }
        public string ServerAddress { get; set; }
        public int ServerPort { get; set; }

        public TrainingWakeData()
        {
            InstanceId = 0;
            ServerAddress = string.Empty;
            ServerPort = 0;
        }

        public TrainingWakeData(int instanceId, string address, int port)
        {
            InstanceId = instanceId;
            ServerAddress = address ?? "127.0.0.1";
            ServerPort = port;
        }

        public void Deserialize(DeserializeEvent e)
        {
            InstanceId = e.Reader.ReadInt32();
            ServerAddress = e.Reader.ReadString();
            ServerPort = e.Reader.ReadInt32();
        }

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(InstanceId);
            e.Writer.Write(ServerAddress);
            e.Writer.Write(ServerPort);
        }

        public override string ToString()
        {
            return $"TrainingWake[Instance:{InstanceId}, {ServerAddress}:{ServerPort}]";
        }
    }

    /// <summary>
    /// 인스턴스 기상 응답 (서버 -> 인스턴스/훈련생)
    /// 파티 및 훈련 정보 전달
    /// </summary>
    public class TrainingWakeResultData : IDarkRiftSerializable
    {
        public int PartyId { get; set; }
        public int InstanceId { get; set; }
        public string ServerAddress { get; set; }
        public int ServerPort { get; set; }
        public string SceneFile { get; set; }
        public string SceneSetFile { get; set; }
        public string[] PartyMemberIds { get; set; }
        public int ExpectedPlayerCount { get; set; }

        public TrainingWakeResultData()
        {
            PartyId = 0;
            InstanceId = 0;
            ServerAddress = string.Empty;
            ServerPort = 0;
            SceneFile = string.Empty;
            SceneSetFile = string.Empty;
            PartyMemberIds = Array.Empty<string>();
            ExpectedPlayerCount = 0;
        }

        public TrainingWakeResultData(int partyId, int instanceId, string address, int port, string sceneFile, string sceneSetFile, string[] memberIds)
        {
            PartyId = partyId;
            InstanceId = instanceId;
            ServerAddress = address ?? "127.0.0.1";
            ServerPort = port;
            SceneFile = sceneFile ?? string.Empty;
            SceneSetFile = sceneSetFile ?? string.Empty;
            PartyMemberIds = memberIds ?? Array.Empty<string>();
            ExpectedPlayerCount = PartyMemberIds.Length;
        }

        public void Deserialize(DeserializeEvent e)
        {
            PartyId = e.Reader.ReadInt32();
            InstanceId = e.Reader.ReadInt32();
            ServerAddress = e.Reader.ReadString();
            ServerPort = e.Reader.ReadInt32();
            SceneFile = e.Reader.ReadString();
            SceneSetFile = e.Reader.ReadString();

            int count = e.Reader.ReadInt32();
            PartyMemberIds = new string[count];
            for (int i = 0; i < count; i++)
            {
                PartyMemberIds[i] = e.Reader.ReadString();
            }
            ExpectedPlayerCount = e.Reader.ReadInt32();
        }

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(PartyId);
            e.Writer.Write(InstanceId);
            e.Writer.Write(ServerAddress);
            e.Writer.Write(ServerPort);
            e.Writer.Write(SceneFile);
            e.Writer.Write(SceneSetFile);

            e.Writer.Write(PartyMemberIds.Length);
            foreach (var id in PartyMemberIds)
            {
                e.Writer.Write(id);
            }
            e.Writer.Write(ExpectedPlayerCount);
        }

        public override string ToString()
        {
            return $"TrainingWakeResult[Party:{PartyId}, Instance:{InstanceId}, {ServerAddress}:{ServerPort}, Members:{ExpectedPlayerCount}]";
        }
    }

    /// <summary>
    /// 훈련 생성 (인스턴스 -> 서버)
    /// 인스턴스 Fishnet 서버 준비 완료
    /// </summary>
    public class TrainingCreateData : IDarkRiftSerializable
    {
        public int PartyId { get; set; }
        public int InstanceId { get; set; }
        public bool IsReady { get; set; }

        public TrainingCreateData()
        {
            PartyId = 0;
            InstanceId = 0;
            IsReady = false;
        }

        public TrainingCreateData(int partyId, int instanceId, bool isReady)
        {
            PartyId = partyId;
            InstanceId = instanceId;
            IsReady = isReady;
        }

        public void Deserialize(DeserializeEvent e)
        {
            PartyId = e.Reader.ReadInt32();
            InstanceId = e.Reader.ReadInt32();
            IsReady = e.Reader.ReadBoolean();
        }

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(PartyId);
            e.Writer.Write(InstanceId);
            e.Writer.Write(IsReady);
        }

        public override string ToString()
        {
            return $"TrainingCreate[Party:{PartyId}, Instance:{InstanceId}, Ready:{IsReady}]";
        }
    }

    /// <summary>
    /// 훈련 생성됨 (서버 -> 훈련생)
    /// Fishnet 접속 정보 및 준비 알림
    /// </summary>
    public class TrainingCreatedData : IDarkRiftSerializable
    {
        public int PartyId { get; set; }
        public int InstanceId { get; set; }
        public string ServerAddress { get; set; }
        public int ServerPort { get; set; }
        public string SceneFile { get; set; }
        public string SceneSetFile { get; set; }

        public TrainingCreatedData()
        {
            PartyId = 0;
            InstanceId = 0;
            ServerAddress = string.Empty;
            ServerPort = 0;
            SceneFile = string.Empty;
            SceneSetFile = string.Empty;
        }

        public TrainingCreatedData(int partyId, int instanceId, string address, int port, string sceneFile, string sceneSetFile = "")
        {
            PartyId = partyId;
            InstanceId = instanceId;
            ServerAddress = address ?? "127.0.0.1";
            ServerPort = port;
            SceneFile = sceneFile ?? string.Empty;
            SceneSetFile = sceneSetFile ?? string.Empty;
        }

        public void Deserialize(DeserializeEvent e)
        {
            PartyId = e.Reader.ReadInt32();
            InstanceId = e.Reader.ReadInt32();
            ServerAddress = e.Reader.ReadString();
            ServerPort = e.Reader.ReadInt32();
            SceneFile = e.Reader.ReadString();
            SceneSetFile = e.Reader.ReadString();
        }

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(PartyId);
            e.Writer.Write(InstanceId);
            e.Writer.Write(ServerAddress);
            e.Writer.Write(ServerPort);
            e.Writer.Write(SceneFile);
            e.Writer.Write(SceneSetFile);
        }

        public override string ToString()
        {
            return $"TrainingCreated[Party:{PartyId}, Instance:{InstanceId}, {ServerAddress}:{ServerPort}]";
        }
    }

    /// <summary>
    /// 훈련 시작 준비 완료 (인스턴스 -> 서버 -> 교관)
    /// 모든 파티원 Fishnet 접속 완료
    /// </summary>
    public class TrainingBeginReadyData : IDarkRiftSerializable
    {
        public int PartyId { get; set; }
        public int InstanceId { get; set; }
        public int ConnectedPlayerCount { get; set; }
        public int ExpectedPlayerCount { get; set; }
        public bool AllConnected { get; set; }

        public TrainingBeginReadyData()
        {
            PartyId = 0;
            InstanceId = 0;
            ConnectedPlayerCount = 0;
            ExpectedPlayerCount = 0;
            AllConnected = false;
        }

        public TrainingBeginReadyData(int partyId, int instanceId, int connected, int expected)
        {
            PartyId = partyId;
            InstanceId = instanceId;
            ConnectedPlayerCount = connected;
            ExpectedPlayerCount = expected;
            AllConnected = expected > 0 && connected >= expected;
        }

        public void Deserialize(DeserializeEvent e)
        {
            PartyId = e.Reader.ReadInt32();
            InstanceId = e.Reader.ReadInt32();
            ConnectedPlayerCount = e.Reader.ReadInt32();
            ExpectedPlayerCount = e.Reader.ReadInt32();
            AllConnected = e.Reader.ReadBoolean();
        }

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(PartyId);
            e.Writer.Write(InstanceId);
            e.Writer.Write(ConnectedPlayerCount);
            e.Writer.Write(ExpectedPlayerCount);
            e.Writer.Write(AllConnected);
        }

        public override string ToString()
        {
            return $"TrainingBeginReady[Party:{PartyId}, Instance:{InstanceId}, Connected:{ConnectedPlayerCount}/{ExpectedPlayerCount}]";
        }
    }

    /// <summary>
    /// 훈련 생성정보 (서버 -> 파티원)
    /// Fishnet 게임 서버 접속 정보 포함
    /// </summary>
    public class TrainingSetupData : IDarkRiftSerializable
    {
        public int PartyId { get; set; }
        public int InstanceId { get; set; }
        public string ServerAddress { get; set; }
        public int ServerPort { get; set; }
        public string SceneFile { get; set; }
        public string SceneSetFile { get; set; }

        public TrainingSetupData()
        {
            PartyId = 0;
            InstanceId = 0;
            ServerAddress = string.Empty;
            ServerPort = 0;
            SceneFile = string.Empty;
            SceneSetFile = string.Empty;
        }

        public TrainingSetupData(int partyId, int instanceId, string address, int port, string sceneFile, string sceneSetFile = "")
        {
            PartyId = partyId;
            InstanceId = instanceId;
            ServerAddress = address ?? "127.0.0.1";
            ServerPort = port;
            SceneFile = sceneFile ?? string.Empty;
            SceneSetFile = sceneSetFile ?? string.Empty;
        }

        public void Deserialize(DeserializeEvent e)
        {
            PartyId = e.Reader.ReadInt32();
            InstanceId = e.Reader.ReadInt32();
            ServerAddress = e.Reader.ReadString();
            ServerPort = e.Reader.ReadInt32();
            SceneFile = e.Reader.ReadString();
            SceneSetFile = e.Reader.ReadString();
        }

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(PartyId);
            e.Writer.Write(InstanceId);
            e.Writer.Write(ServerAddress);
            e.Writer.Write(ServerPort);
            e.Writer.Write(SceneFile);
            e.Writer.Write(SceneSetFile);
        }

        public override string ToString()
        {
            return $"TrainingSetup[Party:{PartyId}, Instance:{InstanceId}, {ServerAddress}:{ServerPort}, Scene:{SceneFile}]";
        }
    }

    /// <summary>
    /// 훈련 생성완료 알림 (파티원 -> 서버)
    /// </summary>
    public class TrainingCreatedNotification : IDarkRiftSerializable
    {
        public int PartyId { get; set; }
        public string AccountId { get; set; }
        public bool IsReady { get; set; }

        public TrainingCreatedNotification()
        {
            PartyId = 0;
            AccountId = string.Empty;
            IsReady = false;
        }

        public TrainingCreatedNotification(int partyId, string accountId, bool isReady)
        {
            PartyId = partyId;
            AccountId = accountId ?? string.Empty;
            IsReady = isReady;
        }

        public void Deserialize(DeserializeEvent e)
        {
            PartyId = e.Reader.ReadInt32();
            AccountId = e.Reader.ReadString();
            IsReady = e.Reader.ReadBoolean();
        }

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(PartyId);
            e.Writer.Write(AccountId);
            e.Writer.Write(IsReady);
        }

        public override string ToString()
        {
            return $"TrainingCreated[Party:{PartyId}, Account:{AccountId}, Ready:{IsReady}]";
        }
    }

    /// <summary>
    /// 훈련 상태 알림 (서버 -> 교관)
    /// 파티원들의 Fishnet 연결 상태 집계
    /// </summary>
    public class TrainingStatusNotification : IDarkRiftSerializable
    {
        public int PartyId { get; set; }
        public int TotalMembers { get; set; }
        public int ReadyMembers { get; set; }
        public string[] ReadyAccountIds { get; set; }
        public bool AllReady { get; set; }

        public TrainingStatusNotification()
        {
            PartyId = 0;
            TotalMembers = 0;
            ReadyMembers = 0;
            ReadyAccountIds = Array.Empty<string>();
            AllReady = false;
        }

        public TrainingStatusNotification(int partyId, int total, int ready, string[] readyIds)
        {
            PartyId = partyId;
            TotalMembers = total;
            ReadyMembers = ready;
            ReadyAccountIds = readyIds ?? Array.Empty<string>();
            AllReady = total > 0 && ready >= total;
        }

        public void Deserialize(DeserializeEvent e)
        {
            PartyId = e.Reader.ReadInt32();
            TotalMembers = e.Reader.ReadInt32();
            ReadyMembers = e.Reader.ReadInt32();

            int count = e.Reader.ReadInt32();
            ReadyAccountIds = new string[count];
            for (int i = 0; i < count; i++)
            {
                ReadyAccountIds[i] = e.Reader.ReadString();
            }

            AllReady = e.Reader.ReadBoolean();
        }

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(PartyId);
            e.Writer.Write(TotalMembers);
            e.Writer.Write(ReadyMembers);

            e.Writer.Write(ReadyAccountIds.Length);
            foreach (var id in ReadyAccountIds)
            {
                e.Writer.Write(id);
            }

            e.Writer.Write(AllReady);
        }

        public override string ToString()
        {
            return $"TrainingStatus[Party:{PartyId}, Ready:{ReadyMembers}/{TotalMembers}, AllReady:{AllReady}]";
        }
    }

    #endregion

    #region Pin Info

    /// <summary>
    /// 핀 정보 (교관 -> 서버 -> 파티 교육생)
    /// 위치(x, y, z) + 핀 종류(int)
    /// </summary>
    public class PinInfoData : IDarkRiftSerializable
    {
        public int PartyId { get; set; }
        public int PinType { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public PinInfoData()
        {
            PartyId = 0;
            PinType = 0;
            X = 0f;
            Y = 0f;
            Z = 0f;
        }

        public PinInfoData(int partyId, int pinType, float x, float y, float z)
        {
            PartyId = partyId;
            PinType = pinType;
            X = x;
            Y = y;
            Z = z;
        }

        public void Deserialize(DeserializeEvent e)
        {
            PartyId = e.Reader.ReadInt32();
            PinType = e.Reader.ReadInt32();
            X = e.Reader.ReadSingle();
            Y = e.Reader.ReadSingle();
            Z = e.Reader.ReadSingle();
        }

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(PartyId);
            e.Writer.Write(PinType);
            e.Writer.Write(X);
            e.Writer.Write(Y);
            e.Writer.Write(Z);
        }

        public override string ToString()
        {
            return $"PinInfo[Party:{PartyId}, Type:{PinType}, Pos:({X:F2},{Y:F2},{Z:F2})]";
        }
    }

    #endregion

    #region Chat Data

    /// <summary>
    /// 채팅 메시지 데이터 (교관/교육생 → 서버 → 파티원)
    /// int PartyId, bool IsAlarm, string Context
    /// </summary>
    public class ChatMessageData : IDarkRiftSerializable
    {
        /// <summary>대상 파티 ID</summary>
        public int PartyId { get; set; }

        /// <summary>
        /// true = 교관 공지(알람) → 수신측에서 announceAlarm 델리게이트 인보크
        /// false = 일반 채팅 → 수신측에서 announceText 델리게이트 인보크
        /// </summary>
        public bool IsAlarm { get; set; }

        /// <summary>메시지 내용</summary>
        public string Context { get; set; }

        public ChatMessageData()
        {
            PartyId = 0;
            IsAlarm = false;
            Context = string.Empty;
        }

        public ChatMessageData(int partyId, bool isAlarm, string context)
        {
            PartyId = partyId;
            IsAlarm = isAlarm;
            Context = context ?? string.Empty;
        }

        public void Deserialize(DeserializeEvent e)
        {
            PartyId = e.Reader.ReadInt32();
            IsAlarm = e.Reader.ReadBoolean();
            Context = e.Reader.ReadString();
        }

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(PartyId);
            e.Writer.Write(IsAlarm);
            e.Writer.Write(Context);
        }

        public override string ToString()
        {
            return $"ChatMessage[Party:{PartyId}, IsAlarm:{IsAlarm}, Context:{(Context.Length > 30 ? Context.Substring(0, 30) + "..." : Context)}]";
        }
    }

    #endregion

    #region Train Result Data (훈련 결과 데이터)

    /// <summary>
    /// 훈련 결과 패킷 — Push(데디케이트→서버) / Pull(서버→파티원) 공용
    /// </summary>
    [Serializable]
    public class TrainResult : IDarkRiftSerializable
    {
        public int partyId;
        public string trainingType;
        public float playTime;
        public TrainResultData[] results;

        // id = role 기준으로 결과 추출
        public TrainResultData GetResultData(string id)
        {
            return results?.FirstOrDefault(r => r.identified == id);
        }

        // 단일 결과 업데이트
        public bool SetResultData(TrainResultData data)
        {
            if (data == null || string.IsNullOrEmpty(data.identified))
                return false;

            if (results == null)
                results = new TrainResultData[0];

            for (int i = 0; i < results.Length; i++)
            {
                if (results[i].identified == data.identified)
                {
                    results[i] = data;
                    return true;
                }
            }

            if (results.Length >= 11)
                return false;

            var newArray = new TrainResultData[results.Length + 1];
            Array.Copy(results, newArray, results.Length);
            newArray[newArray.Length - 1] = data;
            results = newArray;
            return true;
        }


        public bool SetResultData(string role, string keydata = default, int data = default)
        {
            if (role == null || string.IsNullOrEmpty(role))
            {
                return false;
            }

            if (results == null)
            {
                results = new TrainResultData[0];
            }
            
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i].identified == role)
                {
                    if(string.IsNullOrEmpty(keydata))
                    {
                        return true;
                    }

                    if(results[i].eventsData.ContainsKey(keydata))
                        results[i].eventsData[keydata] += data;
                    else
                        results[i].eventsData.Add(keydata,data);
                    return true;
                }
            }

            var newArray = new TrainResultData[results.Length + 1];
            Array.Copy(results, newArray, results.Length);
            newArray[newArray.Length-1].identified = role;
            results = newArray;
            return false;
        }
        // 플레이어 목록 조회
        public List<string> GetPlayers()
        {
            return results?.Where(r => r.identified != "Total")
                           .Select(r => r.identified).ToList() ?? new List<string>();
        }

        // 모든 플레이어 결과 조회
        public List<TrainResultData> GetPlayersResult()
        {
            var playerIds = GetPlayers();
            var playerResults = new List<TrainResultData>();
            foreach (var id in playerIds)
            {
                var data = GetResultData(id);
                if (data != null) playerResults.Add(data);
            }
            return playerResults;
        }

        // 훈련 전체 설정 값 조회
        public TrainResultData GetTotalCountResult() => GetResultData("Total");

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(partyId);
            e.Writer.Write(trainingType ?? string.Empty);
            e.Writer.Write(playTime);
            int count = results?.Length ?? 0;
            e.Writer.Write(count);
            if (results != null)
                foreach (var item in results)
                    e.Writer.Write(item);
        }

        public void Deserialize(DeserializeEvent e)
        {
            partyId = e.Reader.ReadInt32();
            trainingType = e.Reader.ReadString();
            playTime = e.Reader.ReadSingle();
            int count = e.Reader.ReadInt32();
            results = new TrainResultData[count];
            for (int i = 0; i < count; i++)
                results[i] = e.Reader.ReadSerializable<TrainResultData>();
        }
    }

    /// <summary>
    /// 훈련 결과 항목 — "Total" 또는 플레이어 ID별 이벤트 수치
    /// </summary>
    [Serializable]
    public class TrainResultData : IDarkRiftSerializable
    {
        public string identified;
        public Dictionary<string, int> eventsData;

        public TrainResultData()
        {
            identified = string.Empty;
            eventsData = new Dictionary<string, int>();
        }

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(identified ?? string.Empty);
            int count = eventsData?.Count ?? 0;
            e.Writer.Write(count);
            if (eventsData != null)
                foreach (var kv in eventsData)
                {
                    e.Writer.Write(kv.Key);
                    e.Writer.Write(kv.Value);
                }
        }

        public void Deserialize(DeserializeEvent e)
        {
            identified = e.Reader.ReadString();
            int count = e.Reader.ReadInt32();
            eventsData = new Dictionary<string, int>(count);
            for (int i = 0; i < count; i++)
            {
                string key = e.Reader.ReadString();
                int val = e.Reader.ReadInt32();
                eventsData[key] = val;
            }
        }
    }

    #endregion
}
