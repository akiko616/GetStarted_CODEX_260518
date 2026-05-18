using DatabasePlugin;
using GameInstancePlugin;
using NetworkRequestsData;
using NetworkResponeData;
using PartyManagerPlugin;
using System;
using System.Collections.Generic;
using UnityEngine;


namespace TRAINEE
{
    public partial class NetworkManager
    {
        public Action<ResLogin> OnLoginSuccess;
        public Action<ResLogin> OnLoginFailed;

        public Action<ResAccountRegister> OnRegisterAccountSuccess;
        public Action<ResAccountRegister> OnRegisterAccountFailed;

        public Action<ResAccountGet> OnGetAccountSuccess;
        public Action<ResAccountGet> OnGetAccountFailed;

        public Action<ResAccountGetType> OnGetTypeAccountsSuccess;
        public Action<ResAccountGetType> OnGetTypeAccountsFailed;

        public Action<ResAccountUpdate> OnUpdateAccountSuccess;
        public Action<ResAccountUpdate> OnUpdateAccountFailed;

        public Action<ResAccountDelete> OnDeleteAccountSuccess;
        public Action<ResAccountDelete> OnDeleteAccountFailed;

        public Action<ResChangePassword> OnChangePasswordSuccess;
        public Action<ResChangePassword> OnChangePasswordFailed;

        public Action<ResPartyCreate> OnPartyCreateSuccess;
        public Action<ResPartyCreate> OnPartyCreateFailed;

        public Action<ResPartyDestory> OnPartyDestorySuccess;
        public Action<ResPartyDestory> OnPartyDestoryFailed;

        public Action<ResPartyMove> OnPartyMoveSuccess;
        public Action<ResPartyMove> OnPartyMoveFailed;

        public Action<ResPartyChangeRole> OnPartyChangeRoleSuccess;
        public Action<ResPartyChangeRole> OnPartyChangeRoleFailed;

        public Action<ResPartyChangeSetting> OnPartyChangeSettingSuccess;
        public Action<ResPartyChangeSetting> OnPartyChangeSettingFailed;

        public Action<ResPartyListGet> OnPartyGetListSuccess;
        public Action<ResPartyListGet> OnPartyGetListFailed;

        public Action<ResPartyJoin> OnPartyJoinSuccess;
        public Action<ResPartyJoin> OnPartyJoinFailed;

        public Action<ResPartyLeave> OnPartyLeaveSuccess;
        public Action<ResPartyLeave> OnPartyLeaveFailed;

        public Action<ResAdminGetAllInstances> OnAdminGetAllInstancesSuccess;
        public Action<ResAdminGetAllInstances> OnAdminGetAllInstancesFailed;

        public Action<ResAdminSpectateRequest> OnAdminSpectateRequestSuccess;
        public Action<ResAdminSpectateRequest> OnAdminSpectateRequestFailed;

        public Action<ResTrainingInfo> OnTrainingStart;
        public Action<ResTrainingInfo> OnGameServerReady;
        public Action<ResTrainingInfo> OnTrainingStartFailed;

        public Action<ResInstanceSignalBroadcast> OnInstanceSignalBroadcastReceived;

        public Action<ResAdminUserLoginNotification> OnUserLoginReceived;

        public Action<ResAdminUserLogOutNotification> OnUserLogOutReceived;

        public Action<ResAdminGetOnlineUsers> OnUserGetReceived;

        public Action<ResPartyConfirm> OnPartyConfirmSuccess;
        public Action<ResPartyConfirm> OnPartyConfirmFailed;

        public Action<ResTrainingSetup> OnTrainingSetupSuccess;
        public Action<ResTrainingSetup> OnTrainingSetupFailed;

        public Action<ResTrainingBegin> OnTrainingBeginSuccess;
        public Action<ResTrainingBegin> OnTrainingBeginFailed;

        public Action<ResTrainingPause> OnTrainingPauseSuccess;
        public Action<ResTrainingPause> OnTrainingPauseFailed;

        public Action<ResTrainingStop> OnTrainingStopSuccess;
        public Action<ResTrainingStop> OnTrainingStopFailed;

        public Action<ResTrainingStatus> OnTrainingStatusReceived;

        public Action<ResTrainingBeginReady> OnTrainingBeginReadyReceived;

        public Action<ResAdminPartyNotication> OnAdminPartyNoticationReceived;

        public Action<ResTrainingWakeResult> OnTrainingWakeResultSuccess;
        public Action<ResTrainingWakeResult> OnTrainingWakeResultFailed;

        public Action OnTrainingCreatedSuccess;
        public Action<ResTrainingCreated> OnTrainingCreatedFailed;

        public Action<ResTrainingMidJoin> OnTrainingMidJoinSuccess;
        public Action<ResTrainingMidJoin> OnTrainingMidJoinFailed;
        public Action<ResTrainingMidLeave> OnTrainingMidLeaveSuccess;
        public Action<ResTrainingMidLeave> OnTrainingMidLeaveFailed;

        public Action<ResTrainingResult> OnTrainingResultSuccess;
        public Action<ResTrainingResult> OnTrainingResultFailed;

        public Action<ResTrainingGracefulQuit> OnTrainingGracefulQuitReceived;

        public Action<ResTrainingPinInfoData> OnTrainingPinInfoReceived;
    }
}
