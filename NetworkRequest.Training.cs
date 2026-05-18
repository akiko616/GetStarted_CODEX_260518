using DarkRift;
using NetworkRequestsData;
using UnityEngine;

namespace TRAINEE
{
    public partial class NetworkManager
    {
        public void OnReqTrainingStart(int partyId, string sceneFile = "DefaultScene")
        {
            ReqTrainingStart reqData = new ReqTrainingStart
            {
                training = new GameInstancePlugin.TrainingStartRequest(partyId, sceneFile)
            };

            OnSend<ReqTrainingStart>(EProtocolCode.TRAINING_START, reqData);
        }

        public void OnReqPartyConfirm(int partyId)
        {
            ReqPartyConfirm reqData = new ReqPartyConfirm
            {
                partyId = partyId
            };

            OnSend<ReqPartyConfirm>(EProtocolCode.PARTY_CONFIRM, reqData);
        }

        public void OnReqTrainingSetup(int partyId, string sceneFile = "DefaultScene")
        {
            ReqTrainingSetup reqData = new ReqTrainingSetup
            {
                partyId = partyId,
                sceneFile = sceneFile
            };

            OnSend<ReqTrainingSetup>(EProtocolCode.TRAINING_SETUP, reqData);
        }

        public void OnReqTrainingBegin(int partyId)
        {
            ReqTrainingBegin reqData = new ReqTrainingBegin
            {
                partyId = partyId,
            };

            OnSend<ReqTrainingBegin>(EProtocolCode.TRAINING_BEGIN, reqData);
        }

        public void OnReqTrainingPause(int partyId)
        {
            ReqTrainingPause reqData = new ReqTrainingPause
            {
                partyId = partyId,
            };

            OnSend<ReqTrainingPause>(EProtocolCode.TRAINING_PAUSE, reqData);
        }

        public void OnReqTrainingStop(int partyId)
        {
            ReqTrainingStop reqData = new ReqTrainingStop
            {
                partyId = partyId,
            };

            OnSend<ReqTrainingStop>(EProtocolCode.TRAINING_STOP, reqData);
        }

        public void OnReqTrainingStatus(int partyId, ushort trainingType)
        {
            ReqTrainingStatus reqData = new ReqTrainingStatus
            {
                partyId = partyId,
            };

            OnSend<ReqTrainingStatus>(EProtocolCode.TRAINING_STATUS, reqData);
        }

        public void OnReqTrainingCreated(int partyId, string accountId, bool isReady = true)
        {
            ReqTrainingCreated reqData = new ReqTrainingCreated
            {
                partyId = partyId,
                accountId = accountId,
                isReady = isReady
            };

            OnSend<ReqTrainingCreated>(EProtocolCode.TRAINING_CREATED, reqData);
        }

       

        public void OnReqTrainingMidJoin(int partyId)
        {
            ReqTrainingMidJoin reqData = new ReqTrainingMidJoin
            {
                partyId = partyId
            };

            OnSend<ReqTrainingMidJoin>(EProtocolCode.TRAINING_MIDJOIN, reqData);
        }

        public void OnReqTrainingMidLeave(int partyId)
        {
            ReqTrainingMidLeave reqData = new ReqTrainingMidLeave
            {
                partyId = partyId
            };

            OnSend<ReqTrainingMidLeave>(EProtocolCode.TRAINING_MIDLEAVE, reqData);
        }

        public void OnReqTrainingGraceFulEnd(int partyId)
        {
            ReqTrainingGracefulEnd reqData = new ReqTrainingGracefulEnd
            {
                partyId = partyId
            };

            OnSend<ReqTrainingGracefulEnd>(EProtocolCode.TRAINING_GRACEFUL_END, reqData);
        }

    }
}
