using NetworkRequestsData;
using PartyManagerPlugin;
using UnityEngine;

namespace TRAINEE
{
    public partial class NetworkManager
    {
        public void OnReqPartyCreate(Party party, int maxPlayer = 4)
        {
            if (maxPlayer < _minPlayer || maxPlayer > _maxPlayer)
            {
                maxPlayer = _defaultPlayer;
            }

            ReqPartyCreate reqData = new ReqPartyCreate
            {
                hasTemplate = party != null,
                party = party,
                maxPlayer = maxPlayer,
            };

            OnSend<ReqPartyCreate>(EProtocolCode.PARTY_CREATE, reqData);
        }

        public void OnReqPartyDestory(int partyId)
        {
            ReqPartyDestory reqData = new ReqPartyDestory
            {
                partyid = partyId
            };

            OnSend<ReqPartyDestory>(EProtocolCode.PARTY_DESTROY, reqData);
        }

        public void OnReqPartyMove(int fromPartyId, int toPartyId,string memberAccountId)
        {
            ReqPartyMove reqData = new ReqPartyMove
            {
                fromPartyId = fromPartyId,
                toPartyId = toPartyId
            };

            OnSend<ReqPartyMove>(EProtocolCode.PARTY_MOVE, reqData);
        }

        public void OnReqPartyChangeRole(int partyId, string targetAccountId, string newRole)
        {
            ReqPartyChangeRole reqData = new ReqPartyChangeRole
            {
                partyId = partyId,
                targetAccountId = targetAccountId,
                newRole = newRole
            };

            OnSend<ReqPartyChangeRole>(EProtocolCode.PARTY_CHANGE_ROLE, reqData);
        }

        public void OnReqPartyChangeSetting(int partyId, string sceneFile, string sceneSetFile, string partyName, string partyInfo)
        {
            ReqPartyChangeSetting reqData = new ReqPartyChangeSetting
            {
                partyId = partyId,
                sceneFile = sceneFile,
                sceneSetFile = sceneSetFile,
                partyName = partyName,
                partyInfo = partyInfo
            };

            OnSend<ReqPartyChangeSetting>(EProtocolCode.PARTY_CHANGE_SETTING, reqData);
        }

        public void OnReqPartyListGet()
        {
            ReqPartyListGet reqData = new ReqPartyListGet
            {

            };

            OnSend<ReqPartyListGet>(EProtocolCode.PARTY_GET_LIST, reqData);
        }

        public void OnReqPartyJoin(string accountId, int partyid)
        {
            ReqPartyJoin reqData = new ReqPartyJoin
            {
                accountId = accountId,
                partyId = partyid
            };

            OnSend<ReqPartyJoin>(EProtocolCode.PARTY_JOIN, reqData);
        }

        public void OnReqPartyLeave(string accountId, int partyid)
        {
            ReqPartyLeave reqData = new ReqPartyLeave
            {
                accountId = accountId,
                partyId = partyid
            };

            OnSend<ReqPartyLeave>(EProtocolCode.PARTY_LEAVE, reqData);
        }
    }
}
