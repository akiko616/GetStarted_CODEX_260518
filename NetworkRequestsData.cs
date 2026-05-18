using DarkRift;
using DatabasePlugin;
using GameInstancePlugin;
using PartyManagerPlugin;
using System;


namespace NetworkRequestsData
{
    #region Auth
    [System.Serializable]
    public struct ReqLogin : IDarkRiftSerializable
    {
        public string id;
        public string password;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(id);
            e.Writer.Write(password);
        }

        public void Deserialize(DeserializeEvent e)
        {
            id = e.Reader.ReadString();
            password = e.Reader.ReadString();
        }

    }

    [System.Serializable]
    public struct ReqAccountRegister : IDarkRiftSerializable
    {
        public string id;
        public string password;
        public int accountType;

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(id);
            e.Writer.Write(password);
            e.Writer.Write(accountType);
        }

        public void Deserialize(DeserializeEvent e)
        {
            id = e.Reader.ReadString();
            password = e.Reader.ReadString();
            accountType = e.Reader.ReadInt32();
        }

    }

    [System.Serializable]
    public struct ReqAccountGet : IDarkRiftSerializable
    {
        public string id;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(id);
        }
        public void Deserialize(DeserializeEvent e)
        {
            id = e.Reader.ReadString();
        }

    }

    [System.Serializable]
    public struct ReqAccountGetType : IDarkRiftSerializable
    {
        public int accountType;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(accountType);
        }
        public void Deserialize(DeserializeEvent e)
        {
            accountType = e.Reader.ReadInt32();
        }

    }

    [System.Serializable]
    public struct ReqAccountUpdate : IDarkRiftSerializable
    {
        public AccountData account;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(account);
        }
        public void Deserialize(DeserializeEvent e)
        {
            account = e.Reader.ReadSerializable<AccountData>();
        }

    }

    [System.Serializable]
    public struct ReqAccountDelete : IDarkRiftSerializable
    {
        public string id;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(id);
        }
        public void Deserialize(DeserializeEvent e)
        {
            id = e.Reader.ReadString();
        }

    }

    [System.Serializable]
    public struct ReqChangePassword : IDarkRiftSerializable
    {
        public string id;
        public string oldPassword;
        public string newPassword;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(id);
            e.Writer.Write(oldPassword);
            e.Writer.Write(newPassword);
        }
        public void Deserialize(DeserializeEvent e)
        {
            id = e.Reader.ReadString();
            oldPassword = e.Reader.ReadString();
            newPassword = e.Reader.ReadString();
        }

    }
    #endregion

    #region Party
    public struct ReqPartyCreate : IDarkRiftSerializable
    {
        public bool hasTemplate;
        public int maxPlayer;
        public Party party;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(hasTemplate);
            e.Writer.Write(party);
            e.Writer.Write(maxPlayer);
        }
        public void Deserialize(DeserializeEvent e)
        {
            hasTemplate = e.Reader.ReadBoolean();
            party = e.Reader.ReadSerializable<Party>();
            maxPlayer = e.Reader.ReadInt32();
        }
    }

    public struct ReqPartyDestory : IDarkRiftSerializable
    {
        public int partyid;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(partyid);
        }
        public void Deserialize(DeserializeEvent e)
        {
            partyid = e.Reader.ReadInt32();
        }
    }

    public struct ReqPartyMove : IDarkRiftSerializable
    {
        public int fromPartyId;
        public int toPartyId;
        public string memberAccountId;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(fromPartyId);
            e.Writer.Write(toPartyId);
            e.Writer.Write(memberAccountId);
        }
        public void Deserialize(DeserializeEvent e)
        {
            fromPartyId = e.Reader.ReadInt32();
            toPartyId = e.Reader.ReadInt32();
            memberAccountId = e.Reader.ReadString();
        }
    }

    public struct ReqPartyChangeRole : IDarkRiftSerializable
    {
        public int partyId;
        public string targetAccountId;
        public string newRole;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(partyId);
            e.Writer.Write(targetAccountId);
            e.Writer.Write(newRole);
        }
        public void Deserialize(DeserializeEvent e)
        {
            partyId = e.Reader.ReadInt32();
            targetAccountId = e.Reader.ReadString();
            newRole = e.Reader.ReadString();
        }
    }

    public struct ReqPartyChangeSetting : IDarkRiftSerializable
    {
        public int partyId;
        public string sceneFile;
        public string sceneSetFile;
        public string partyName;
        public string partyInfo;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(partyId);
            e.Writer.Write(sceneFile);
            e.Writer.Write(sceneSetFile);
            e.Writer.Write(partyName);
            e.Writer.Write(partyInfo);
        }
        public void Deserialize(DeserializeEvent e)
        {
            partyId = e.Reader.ReadInt32();
            sceneFile = e.Reader.ReadString();
            sceneSetFile = e.Reader.ReadString();
            partyName = e.Reader.ReadString();
            partyInfo = e.Reader.ReadString();
        }
    }

    public struct ReqPartyListGet : IDarkRiftSerializable
    {
        public void Serialize(SerializeEvent e)
        {
        }
        public void Deserialize(DeserializeEvent e)
        {
        }
    }

    public struct ReqPartyJoin : IDarkRiftSerializable
    {
        public string accountId;
        public int partyId;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(accountId);
            e.Writer.Write(partyId);
        }
        public void Deserialize(DeserializeEvent e)
        {
            accountId = e.Reader.ReadString();
            partyId = e.Reader.ReadInt32();
        }
    }

    public struct ReqPartyLeave : IDarkRiftSerializable
    {
        public string accountId;
        public int partyId;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(accountId);
            e.Writer.Write(partyId);
        }
        public void Deserialize(DeserializeEvent e)
        {
            accountId = e.Reader.ReadString();
            partyId = e.Reader.ReadInt32();
        }
    }
    #endregion

    #region GameInstance
    public struct ReqAdminGetAllInstances : IDarkRiftSerializable
    {
        public void Serialize(SerializeEvent e)
        {
        }
        public void Deserialize(DeserializeEvent e)
        {
        }

    }
    public struct ReqAdminSpectateRequest : IDarkRiftSerializable
    {
        public int instanceId;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(instanceId);
        }
        public void Deserialize(DeserializeEvent e)
        {
            instanceId = e.Reader.ReadInt32();
        }
    }

    public struct ReqAdminInstacneNotification : IDarkRiftSerializable
    {
        public void Serialize(SerializeEvent e)
        {
        }
        public void Deserialize(DeserializeEvent e)
        {
        }

    }

    public struct ReqTrainingStart : IDarkRiftSerializable
    {
        public TrainingStartRequest training;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(training);
        }
        public void Deserialize(DeserializeEvent e)
        {
            training = e.Reader.ReadSerializable<TrainingStartRequest>();
        }
    }
    #endregion

    #region Admin Notifications
    public struct ReqAdminGetOnlineUsers : IDarkRiftSerializable
    {
        public int filterType;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(filterType);
        }
        public void Deserialize(DeserializeEvent e)
        {
            filterType = e.Reader.ReadInt32();
        }
    }

    #endregion

    #region Training Control
    public struct ReqPartyConfirm : IDarkRiftSerializable
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

    public struct ReqTrainingCreated : IDarkRiftSerializable
    {
        public int partyId;
        public string accountId;
        public bool isReady;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(partyId);
            e.Writer.Write(accountId);
            e.Writer.Write(isReady);
        }
        public void Deserialize(DeserializeEvent e)
        {
            partyId = e.Reader.ReadInt32();
            accountId = e.Reader.ReadString();
            isReady = e.Reader.ReadBoolean();
        }
    }

    public struct ReqTrainingSetup : IDarkRiftSerializable
    {
        public int partyId;
        public string sceneFile;
        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(partyId);
            e.Writer.Write(sceneFile);
        }
        public void Deserialize(DeserializeEvent e)
        {
            partyId = e.Reader.ReadInt32();
            sceneFile = e.Reader.ReadString();
        }
    }

    public struct ReqTrainingBegin : IDarkRiftSerializable
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

    public struct ReqTrainingStatus : IDarkRiftSerializable
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

    public struct ReqTrainingPause : IDarkRiftSerializable
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

    public struct ReqTrainingStop : IDarkRiftSerializable
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

    public struct ReqTrainingMidJoin : IDarkRiftSerializable
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

    public struct ReqTrainingMidLeave : IDarkRiftSerializable
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

    public struct ReqTrainingGracefulEnd : IDarkRiftSerializable
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


    public struct ReqTrainingPinInfoData : IDarkRiftSerializable
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
