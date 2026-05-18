using UnityEngine;


namespace TRAINEE
{
    public partial class NetworkManager
    {
        public enum EProtocolCode : ushort
        {
            // Auth (1-7)
            LOGIN = 1,
            REGISTER = 2,
            GET_ACCOUNT = 3,
            UPDATE_ACCOUNT = 4,
            DELETE_ACCOUNT = 5,
            CHANGE_PASSWORD = 6,
            GET_TYPE_ACCOUNTS = 7,

            // Party (100-110)
            PARTY_CREATE = 100,
            PARTY_DESTROY = 101,
            PARTY_MOVE = 102,
            PARTY_CHANGE_ROLE = 103,
            PARTY_CHANGE_SETTING = 104,
            PARTY_GET_LIST = 105,
            PARTY_JOIN = 106,
            PARTY_LEAVE = 107,
            ADMIN_PARTY_NOTIFICATION = 110,

            // Admin Notifications (120-122)
            ADMIN_USER_LOGIN_NOTIFICATION = 120,
            ADMIN_USER_LOGOUT_NOTIFICATION = 121,
            ADMIN_GET_ONLINE_USERS = 122,

            // Game Instance (111-113, 200-213)
            ADMIN_GET_ALL_INSTANCES = 111,
            ADMIN_SPECTATE_REQUEST = 112,
            ADMIN_INSTANCE_NOTIFICATION = 113,

            // Training & Instance (200-213)
            TRAINING_START = 200,
            TRAINING_INFO = 205,
            INSTANCE_SIGNAL_BROADCAST = 213,

            // Training Control (220-230) - 교관 주도 훈련 플로우
            PARTY_CONFIRM = 220,
            TRAINING_SETUP = 222,
            TRAINING_BEGIN = 224,
            TRAINING_STATUS = 225,
            TRAINING_WAKE_RESULT = 227,
            TRAINING_CREATED = 229,
            TRAINING_BEGIN_READY = 230,
            TRAINING_PAUSE = 231,
            TRAINING_STOP = 232,
            TRAINING_PININFO = 233,
            TRAINING_MIDJOIN = 234,
            TRAINING_MIDLEAVE = 235,
            TRAINING_RESULT = 237,
            TRAINING_GRACEFUL_END = 238,
            TRAINING_GRACEFUL_QUIT = 239
        }

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

        public enum InstructorNotificationType : byte
        {
            PartyCreated = 0,
            PartyDestroyed = 1,
            PartyMemberJoined = 2,
            PartyMemberLeft = 3,
            PartyRoleChanged = 4,
            PartySettingChanged = 5
        }
    }
}
