using DarkRift;
using DatabasePlugin;
using GameInstancePlugin;
using PartyManagerPlugin;
using System.Collections.Generic;
using static TRAINEE.NetworkManager;

namespace NetworkResponeData
{
    #region Auth
    [System.Serializable]
    public struct ResLogin : IDarkRiftSerializable
    {
        public ResponseCode code;
        public AccountResponse account; // 로그인 성공 시에만 채워짐
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            // 성공했을 때만 계정 정보를 쓴다
            if (code == ResponseCode.Success)
            {
                e.Writer.Write(account);
            }
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (ResponseCode)e.Reader.ReadByte();

            // 성공했을 때만 계정 정보를 읽는다
            if (code == ResponseCode.Success)
            {
                account = e.Reader.ReadSerializable<AccountResponse>();
            }
        }

    }

    [System.Serializable]
    public struct ResAccountRegister : IDarkRiftSerializable
    {
        public ResponseCode code;
        public AccountResponse account;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            // 성공했을 때만 계정 정보를 쓴다
            if (code == ResponseCode.Success)
            {
                e.Writer.Write(account);
            }
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (ResponseCode)e.Reader.ReadByte();

            // 성공했을 때만 계정 정보를 읽는다
            if (code == ResponseCode.Success)
            {
                account = e.Reader.ReadSerializable<AccountResponse>();
            }
        }

    }

    [System.Serializable]
    public struct ResAccountGet : IDarkRiftSerializable
    {
        public ResponseCode code;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (ResponseCode)e.Reader.ReadByte();
        }

    }

    [System.Serializable]
    public struct ResAccountGetType : IDarkRiftSerializable
    {
        public ResponseCode code;

        public List<AccountResponse> accounts;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            // 성공했을 때만 계정 정보를 쓴다
            if (code == ResponseCode.Success)
            {
                if (accounts != null)
                {
                    e.Writer.Write(accounts.Count);

                    for (int i = 0; i < accounts.Count; i++)
                    {
                        e.Writer.Write(accounts[i]);
                    }
                }
                else
                {
                    e.Writer.Write(0);
                }
            }
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (ResponseCode)e.Reader.ReadByte();

            if (code == ResponseCode.Success)
            {
                int length = e.Reader.ReadInt32();

                accounts = new List<AccountResponse>();

                for (int i = 0; i < length; i++)
                {
                    accounts.Add(e.Reader.ReadSerializable<AccountResponse>());
                }
            }
        }
    }

    [System.Serializable]
    public struct ResAccountUpdate : IDarkRiftSerializable
    {
        public ResponseCode code;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (ResponseCode)e.Reader.ReadByte();
        }

    }

    [System.Serializable]
    public struct ResAccountDelete : IDarkRiftSerializable
    {
        public ResponseCode code;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (ResponseCode)e.Reader.ReadByte();
        }
    }

    [System.Serializable]
    public struct ResChangePassword : IDarkRiftSerializable
    {
        public ResponseCode code;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (ResponseCode)e.Reader.ReadByte();
        }
    }
    #endregion

    #region Party
    public struct ResPartyCreate : IDarkRiftSerializable
    {
        public PartyResponseCode code;
        public Party party;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            if (code == PartyResponseCode.Success)
            {
                e.Writer.Write(party);
            }

        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (PartyResponseCode)e.Reader.ReadByte();

            if (code == PartyResponseCode.Success)
            {
                party = e.Reader.ReadSerializable<Party>();
            }

        }
    }

    public struct ResPartyDestory : IDarkRiftSerializable
    {
        public PartyResponseCode code;
        public int partyid;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            if (code == PartyResponseCode.Success)
            {
                e.Writer.Write(partyid);
            }

        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (PartyResponseCode)e.Reader.ReadByte();

            if (code == PartyResponseCode.Success)
            {
                partyid = e.Reader.ReadInt32();
            }
        }
    }

    public struct ResPartyMove : IDarkRiftSerializable
    {
        public PartyResponseCode code;
        public Party party;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            if (code == PartyResponseCode.Success)
            {
                e.Writer.Write(party);
            }
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (PartyResponseCode)e.Reader.ReadByte();

            if (code == PartyResponseCode.Success)
            {
                party = e.Reader.ReadSerializable<Party>();
            }
        }
    }

    public struct ResPartyChangeRole : IDarkRiftSerializable
    {
        public PartyResponseCode code;
        public Party party;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            if (code == PartyResponseCode.Success)
            {
                e.Writer.Write(party);
            }
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (PartyResponseCode)e.Reader.ReadByte();

            if (code == PartyResponseCode.Success)
            {
                party = e.Reader.ReadSerializable<Party>();
            }
        }
    }

    public struct ResPartyChangeSetting : IDarkRiftSerializable
    {
        public PartyResponseCode code;
        public Party party;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            if (code == PartyResponseCode.Success)
            {
                e.Writer.Write(party);
            }
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (PartyResponseCode)e.Reader.ReadByte();

            if (code == PartyResponseCode.Success)
            {
                party = e.Reader.ReadSerializable<Party>();
            }
        }
    }

    public struct ResPartyListGet : IDarkRiftSerializable
    {
        public PartyResponseCode code;
        public int count;
        public List<Party> parties;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            if (code == PartyResponseCode.Success)
            {
                e.Writer.Write(count);

                if (parties != null)
                {
                    // 1. 유저 수(count)를 먼저 씁니다.
                    e.Writer.Write(parties.Count);

                    // 2. 유저 수만큼 루프를 돌며 정보를 씁니다.
                    for (int i = 0; i < parties.Count; i++)
                    {
                        e.Writer.Write(parties[i]);
                    }
                }
                else
                {
                    e.Writer.Write(0); // 유저가 없을 경우 count는 0
                }
            }
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (PartyResponseCode)e.Reader.ReadByte();

            if (code == PartyResponseCode.Success)
            {
                count = e.Reader.ReadInt32();

                parties = new List<Party>(count);

                for (int i = 0; i < count; i++)
                {
                    parties.Add(e.Reader.ReadSerializable<Party>());
                }
            }
        }
    }

    public struct ResPartyJoin : IDarkRiftSerializable
    {
        public PartyResponseCode code;
        public Party party;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            if (code == PartyResponseCode.Success)
            {
                e.Writer.Write(party);
            }
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (PartyResponseCode)e.Reader.ReadByte();

            if (code == PartyResponseCode.Success)
            {
                party = e.Reader.ReadSerializable<Party>();
            }
        }
    }

    public struct ResPartyLeave : IDarkRiftSerializable
    {
        public PartyResponseCode code;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (PartyResponseCode)e.Reader.ReadByte();
        }
    }
    #endregion

    #region GameInstance
    public struct ResAdminGetAllInstances : IDarkRiftSerializable
    {
        public ResponseCode code;
        public int count;
        public List<GameInstanceData> instances;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            if (code == ResponseCode.Success)
            {
                e.Writer.Write(count);
            }
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (ResponseCode)e.Reader.ReadByte();

            if (code == ResponseCode.Success)
            {
                count = e.Reader.ReadInt32();

                instances = new List<GameInstanceData>(count);

                for (int i = 0; i < instances.Count; i++)
                {
                    instances.Add(e.Reader.ReadSerializable<GameInstanceData>());
                }
            }
        }

    }
    public struct ResAdminSpectateRequest : IDarkRiftSerializable
    {
        public ResponseCode code;
        public GameInstanceData instance;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            if (code == ResponseCode.Success)
            {
                e.Writer.Write(instance);
            }
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (ResponseCode)e.Reader.ReadByte();

            if (code == ResponseCode.Success)
            {
                instance = e.Reader.ReadSerializable<GameInstanceData>();
            }
        }
    }

    public struct ResAdminInstacneNotification : IDarkRiftSerializable
    {

        public void Serialize(SerializeEvent e)
        {

        }
        public void Deserialize(DeserializeEvent e)
        {

        }

    }

    public struct ResTrainingInfo : IDarkRiftSerializable
    {
        public TrainingResponseCode code;
        public int instanceId;
        public string statusMessage;
        public GameInstanceData instance;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            if (code == TrainingResponseCode.Success)
            {
                e.Writer.Write(instanceId);
                e.Writer.Write(statusMessage);
                e.Writer.Write(instance);
            }
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (TrainingResponseCode)e.Reader.ReadByte();

            if (code == TrainingResponseCode.Success)
            {
                instanceId = e.Reader.ReadInt32();
                statusMessage = e.Reader.ReadString();
                instance = e.Reader.ReadSerializable<GameInstanceData>();
            }
        }
    }

    public struct ResInstanceSignalBroadcast : IDarkRiftSerializable
    {
        public ResponseCode code;
        public InstanceSignalType signalType;
        public int instanceId;
        public int partyId;
        public string additionalData;
        public string timeStamp;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            if (code == ResponseCode.Success)
            {
                e.Writer.Write((byte)signalType);
                e.Writer.Write(instanceId);
                e.Writer.Write(partyId);
                e.Writer.Write(additionalData);
                e.Writer.Write(timeStamp);
            }
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (ResponseCode)e.Reader.ReadByte();

            if (code == ResponseCode.Success)
            {
                signalType = (InstanceSignalType)e.Reader.ReadByte();
                instanceId = e.Reader.ReadInt32();
                partyId = e.Reader.ReadInt32();
                additionalData = e.Reader.ReadString();
                timeStamp = e.Reader.ReadString();
            }
        }
    }
    #endregion

    #region Admin Notifications
    public struct ResAdminUserLoginNotification : IDarkRiftSerializable
    {
        public AccountResponse account;
        public ushort clientId;
        public string timeStamp;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(account);
            e.Writer.Write(clientId);
            e.Writer.Write(timeStamp);
        }
        public void Deserialize(DeserializeEvent e)
        {
            account = e.Reader.ReadSerializable<AccountResponse>();
            clientId = e.Reader.ReadUInt16();
            timeStamp = e.Reader.ReadString();
        }
    }

    public struct ResAdminUserLogOutNotification : IDarkRiftSerializable
    {
        public string accountId;
        public ushort clientId;
        public string timeStamp;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(accountId);
            e.Writer.Write(clientId);
            e.Writer.Write(timeStamp);
        }
        public void Deserialize(DeserializeEvent e)
        {
            accountId = e.Reader.ReadString();
            clientId = e.Reader.ReadUInt16();
            timeStamp = e.Reader.ReadString();
        }
    }


    public struct ResAdminGetOnlineUsers : IDarkRiftSerializable
    {
        public ResponseCode code;
        public List<DatabasePlugin.OnlineUserInfo> users;
        public int count;
        public int totalCount;
        public int adminCount;
        public int userCount;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            if (code == ResponseCode.Success)
            {
                if (users != null)
                {
                    // 1. 유저 수(count)를 먼저 씁니다.
                    e.Writer.Write(users.Count);

                    // 2. 유저 수만큼 루프를 돌며 정보를 씁니다.
                    for (int i = 0; i < users.Count; i++)
                    {
                        e.Writer.Write(users[i]);
                    }
                }
                else
                {
                    e.Writer.Write(0); // 유저가 없을 경우 count는 0
                }

                // 3. 나머지 카운트 정보들을 씁니다.
                e.Writer.Write(totalCount);
                e.Writer.Write(adminCount);
                e.Writer.Write(userCount);
            }
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (ResponseCode)e.Reader.ReadByte();

            if (code == ResponseCode.Success)
            {
                // 1. 유저 수(count)를 읽어옵니다.
                int count = e.Reader.ReadInt32();

                // 2. 리스트를 할당합니다.
                users = new List<DatabasePlugin.OnlineUserInfo>(count);

                // 3. 루프를 돌며 유저 정보를 채워 넣습니다.
                for (int i = 0; i < count; i++)
                {
                    users.Add(e.Reader.ReadSerializable<DatabasePlugin.OnlineUserInfo>());
                }

                // 4. 나머지 카운트 정보들을 읽어옵니다.
                totalCount = e.Reader.ReadInt32();
                adminCount = e.Reader.ReadInt32();
                userCount = e.Reader.ReadInt32();
            }
        }
    }

    public struct ResAdminPartyNotication : IDarkRiftSerializable
    {
        public InstructorNotificationType instructorNotificationType;
        public Party party;
        public string affectedAccountId;
        public string additionalInfo;
        public string timeStamp;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)instructorNotificationType);
            e.Writer.Write(party);
            e.Writer.Write(affectedAccountId);
            e.Writer.Write(additionalInfo);
            e.Writer.Write(timeStamp);
        }
        public void Deserialize(DeserializeEvent e)
        {
            instructorNotificationType = (InstructorNotificationType)e.Reader.ReadByte();
            party = e.Reader.ReadSerializable<Party>();
            affectedAccountId = e.Reader.ReadString();
            additionalInfo = e.Reader.ReadString();
            timeStamp = e.Reader.ReadString();
        }
    }

    #endregion

    #region Training Control
    public struct ResPartyConfirm : IDarkRiftSerializable
    {
        public TrainingResponseCode code;

#if !DrillSergeant
        public Party party;
#endif
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);
#if !DrillSergeant
            if (code == TrainingResponseCode.Success)
            {
                e.Writer.Write(party);
            }
#endif
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (TrainingResponseCode)e.Reader.ReadByte();

#if !DrillSergeant
            if (code == TrainingResponseCode.Success)
            {
                party = e.Reader.ReadSerializable<Party>();
            }
#endif
        }
    }

    public struct ResTrainingCreated : IDarkRiftSerializable
    {
        public TrainingResponseCode code;
        public TrainingCreatedData trainingCreatedData;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            if (code == TrainingResponseCode.Success)
            {
                e.Writer.Write(trainingCreatedData);
            }
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (TrainingResponseCode)e.Reader.ReadByte();

            if (code == TrainingResponseCode.Success)
            {
                trainingCreatedData = e.Reader.ReadSerializable<TrainingCreatedData>();
            }
        }
    }

    public struct ResTrainingSetup : IDarkRiftSerializable
    {
        public TrainingResponseCode code;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (TrainingResponseCode)e.Reader.ReadByte();
        }
    }

    public struct ResTrainingBegin : IDarkRiftSerializable
    {
        public TrainingResponseCode code;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (TrainingResponseCode)e.Reader.ReadByte();
        }
    }

    public struct ResTrainingStatus : IDarkRiftSerializable
    {
        public TrainingResponseCode code;
        public int partyId;
        public int totalMembers;
        public int readyMembers;

        public string[] readyIds;
        public bool allReady;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            if (code == TrainingResponseCode.Success)
            {
                e.Writer.Write(partyId);
                e.Writer.Write(totalMembers);
                e.Writer.Write(readyMembers);

                if (readyIds != null && readyIds.Length > 0)
                {
                    e.Writer.Write(readyIds.Length);

                    for (int i = 0; i < readyIds.Length; i++)
                    {
                        e.Writer.Write(readyIds[i]);
                    }
                }
                else
                {
                    e.Writer.Write(0);
                }

                e.Writer.Write(allReady);
            }
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (TrainingResponseCode)e.Reader.ReadByte();

            if (code == TrainingResponseCode.Success)
            {
                partyId = e.Reader.ReadInt32();
                totalMembers = e.Reader.ReadInt32();
                readyMembers = e.Reader.ReadInt32();

                int count = e.Reader.ReadInt32();

                readyIds = new string[count];

                for (int i = 0; i < count; i++)
                {
                    readyIds[i] = e.Reader.ReadString();
                }

                allReady = e.Reader.ReadBoolean();
            }
        }
    }

    public struct ResTrainingPause : IDarkRiftSerializable
    {
        public TrainingResponseCode code;
        public InstanceSignalType instanceSignalType;
        public int partyId;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            if (code == TrainingResponseCode.Success)
            {
                e.Writer.Write((byte)instanceSignalType);
                e.Writer.Write(partyId);
            }
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (TrainingResponseCode)e.Reader.ReadByte();

            if (code == TrainingResponseCode.Success)
            {
                instanceSignalType = (InstanceSignalType)e.Reader.ReadByte();
                partyId = e.Reader.ReadInt32();
            }
        }
    }

    public struct ResTrainingStop : IDarkRiftSerializable
    {
        public TrainingResponseCode code;
        public InstanceSignalType instanceSignalType;
        public int partyId;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            if (code == TrainingResponseCode.Success)
            {
                e.Writer.Write((byte)instanceSignalType);
                e.Writer.Write(partyId);
            }
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (TrainingResponseCode)e.Reader.ReadByte();

            if (code == TrainingResponseCode.Success)
            {
                instanceSignalType = (InstanceSignalType)e.Reader.ReadByte();
                partyId = e.Reader.ReadInt32();
            }
        }
    }

    public struct ResTrainingBeginReady : IDarkRiftSerializable
    {
        public TrainingResponseCode code;
        public int partyId;
        public int instanceId;
        public int connectedCount;
        public int expectedCount;
        public bool allConnected;

        public TrainingBeginReadyData data;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            if (code == TrainingResponseCode.Success)
            {
                e.Writer.Write(partyId);
                e.Writer.Write(instanceId);
                e.Writer.Write(connectedCount);
                e.Writer.Write(expectedCount);
                e.Writer.Write(allConnected);
            }
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (TrainingResponseCode)e.Reader.ReadByte();

            if (code == TrainingResponseCode.Success)
            {
                partyId = e.Reader.ReadInt32();
                instanceId = e.Reader.ReadInt32();
                connectedCount = e.Reader.ReadInt32();
                expectedCount = e.Reader.ReadInt32();
                allConnected = e.Reader.ReadBoolean();

                data = new TrainingBeginReadyData
                    (
                        partyId,
                        instanceId,
                        connectedCount,
                        expectedCount
                    );
            }
        }
    }

    public struct ResTrainingWakeResult : IDarkRiftSerializable
    {
        public TrainingResponseCode code;
        public TrainingWakeResultData trainingWakeResultData;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            if (code == TrainingResponseCode.Success)
            {
                e.Writer.Write(trainingWakeResultData);
            }
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (TrainingResponseCode)e.Reader.ReadByte();

            if (code == TrainingResponseCode.Success)
            {
                trainingWakeResultData = e.Reader.ReadSerializable<TrainingWakeResultData>();
            }
        }
    }

    public struct ResTrainingMidJoin : IDarkRiftSerializable
    {
        public TrainingResponseCode code;
        public string ip;
        public int port;

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            if (code == TrainingResponseCode.Success)
            {
                e.Writer.Write(ip);
                e.Writer.Write(port);
            }
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (TrainingResponseCode)e.Reader.ReadByte();

            if (code == TrainingResponseCode.Success)
            {
                ip = e.Reader.ReadString();
                port = e.Reader.ReadInt32();
            }
        }
    }

    public struct ResTrainingMidLeave : IDarkRiftSerializable
    {
        public TrainingResponseCode code;

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (TrainingResponseCode)e.Reader.ReadByte();
        }
    }

    public struct ResTrainingResult : IDarkRiftSerializable
    {
        public TrainingResponseCode code;
        public int partyId;
        public string resultJson;


        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            if (code == TrainingResponseCode.Success)
            {
                e.Writer.Write(partyId);
                e.Writer.Write(resultJson);
            }
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (TrainingResponseCode)e.Reader.ReadByte();

            if (code == TrainingResponseCode.Success)
            {
                partyId = e.Reader.ReadInt32();
                resultJson = e.Reader.ReadString();
            }
        }
    }

    public struct ResTrainingGracefulEnd : IDarkRiftSerializable
    {
        public TrainingResponseCode code;
        public int partyId;


        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            if (code == TrainingResponseCode.Success)
            {
                e.Writer.Write(partyId);
            }
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (TrainingResponseCode)e.Reader.ReadByte();

            if (code == TrainingResponseCode.Success)
            {
                partyId = e.Reader.ReadInt32();
            }
        }
    }

    public struct ResTrainingGracefulQuit : IDarkRiftSerializable
    {
        public int partyId;

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(partyId);
        }
        public void Deserialize(DeserializeEvent e)
        {
            partyId = e.Reader.ReadInt32();
        }
    }

    public struct ResTrainingPinInfoReq : IDarkRiftSerializable
    {
        public TrainingResponseCode code;
        public int partyId;

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write((byte)code);

            if (code == TrainingResponseCode.Success)
            {
                e.Writer.Write(partyId);
            }
        }
        public void Deserialize(DeserializeEvent e)
        {
            code = (TrainingResponseCode)e.Reader.ReadByte();

            if (code == TrainingResponseCode.Success)
            {
                partyId = e.Reader.ReadInt32();
            }
        }
    }

    public struct ResTrainingPinInfoData : IDarkRiftSerializable
    {
        public int partyId;
        public int pinType;
        public float x;
        public float y;
        public float z;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(partyId);
            e.Writer.Write(pinType);
            e.Writer.Write(x);
            e.Writer.Write(y);
            e.Writer.Write(z);
        }
        public void Deserialize(DeserializeEvent e)
        {
            partyId = e.Reader.ReadInt32();
            pinType = e.Reader.ReadInt32();
            x = e.Reader.ReadSingle();
            y = e.Reader.ReadSingle();
            z = e.Reader.ReadSingle();
        }
    }



    #endregion
}
