using DarkRift;
using DatabasePlugin;
using NetworkRequestsData;
using Unity.VisualScripting;
using UnityEngine;

namespace TRAINEE
{
    public partial class NetworkManager
    {
        public void OnReqLogin(string id, string password)
        {
            ReqLogin reqData = new ReqLogin
            {
                id = id,
                password = password
            };

            OnSend<ReqLogin>(EProtocolCode.LOGIN, reqData);
        }

        public void OnReqRegisterAccount(string id, string password,int accountType)
        {
            ReqAccountRegister reqData = new ReqAccountRegister
            {
                id = id,
                password = password, 
                accountType = accountType
            };

            OnSend<ReqAccountRegister>(EProtocolCode.REGISTER, reqData);
        }

        public void OnReqGetAccount(string id)
        {
            ReqAccountGet reqData = new ReqAccountGet
            {
                id = id
            };

            OnSend<ReqAccountGet>(EProtocolCode.GET_ACCOUNT, reqData);
        }

        public void OnReqGetTypeAccount(int accountType)
        {
            ReqAccountGetType reqData = new ReqAccountGetType
            {
                accountType = accountType
            };

            OnSend<ReqAccountGetType>(EProtocolCode.GET_TYPE_ACCOUNTS, reqData);
        }

        public void OnReqUpdateAccount(AccountData account)
        {
            ReqAccountUpdate reqData = new ReqAccountUpdate
            {
                account = account
            };

            OnSend<ReqAccountUpdate>(EProtocolCode.UPDATE_ACCOUNT,reqData);
        }

        public void OnReqDeleteAccount(string id)
        {
            ReqAccountDelete reqData = new ReqAccountDelete
            {
                id = id
            };

            OnSend<ReqAccountDelete>(EProtocolCode.DELETE_ACCOUNT,reqData);
        }

        public void OnReqChangePassword(string id, string oldPassword, string newPassword)
        {
            ReqChangePassword reqData = new ReqChangePassword
            {
                id = id,
                oldPassword = oldPassword,
                newPassword = newPassword
            };

            OnSend<ReqChangePassword>(EProtocolCode.CHANGE_PASSWORD,reqData);
        }

        public void OnReqPinInfo(int partyId, int pinType, float x, float y, float z)
        {
            NetworkRequestsData.ReqTrainingPinInfoData reqData = new NetworkRequestsData.ReqTrainingPinInfoData
            {
                partyId = partyId,
                pinType = pinType,
                x = x,
                y = y,
                z = z
            };

            OnSend<NetworkRequestsData.ReqTrainingPinInfoData>(EProtocolCode.TRAINING_PININFO, reqData);
        }
    }

    
}
