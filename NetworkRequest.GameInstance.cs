using NetworkRequestsData;
using PartyManagerPlugin;
using UnityEngine;

namespace TRAINEE
{
    public partial class NetworkManager
    {
        public void OnReqAdminGetAllInstances()
        {
            ReqAdminGetAllInstances reqData = new ReqAdminGetAllInstances
            {
            };

            OnSend<ReqAdminGetAllInstances>(EProtocolCode.ADMIN_GET_ALL_INSTANCES, reqData);
        }

        public void OnReqAdminSpectateRequest(int instanceId)
        {
            ReqAdminSpectateRequest reqData = new ReqAdminSpectateRequest
            {
                instanceId = instanceId
            };

            OnSend<ReqAdminSpectateRequest>(EProtocolCode.ADMIN_SPECTATE_REQUEST, reqData);
        }

        public void OnReqAdminInstacneNotification()
        {
            ReqAdminInstacneNotification reqData = new ReqAdminInstacneNotification
            {
            };

            OnSend<ReqAdminInstacneNotification>(EProtocolCode.ADMIN_INSTANCE_NOTIFICATION, reqData);
        }

        public void OnReqAdminGetOnlineUsers(int filterType = -1)
        {
            ReqAdminGetOnlineUsers reqData = new ReqAdminGetOnlineUsers
            {
                filterType = filterType
            };

            OnSend<ReqAdminGetOnlineUsers>(EProtocolCode.ADMIN_GET_ONLINE_USERS, reqData);
        }
    }
}