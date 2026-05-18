using DarkRift;
using GameInstancePlugin;
using NetworkResponeData;
using UnityEngine;

namespace TRAINEE
{
    public partial class NetworkManager
    {
        public bool OnResAdminGetAllInstances(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResAdminGetAllInstances resData = _reader.ReadSerializable<ResAdminGetAllInstances>();

                if (resData.code == ResponseCode.Success)
                {
                    Debug.Log($"[DrillInstructorClient] 게임 인스턴스 목록 성공: {resData.code}");
                    OnAdminGetAllInstancesSuccess.Invoke(resData);
                }
                else
                {
                    Debug.Log($"[DrillInstructorClient] 게임 인스턴스 목록 실패: {resData.code}");
                    OnAdminGetAllInstancesFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 게임 인스턴스 목록 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResAdminSpectateRequest(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResAdminSpectateRequest resData = _reader.ReadSerializable<ResAdminSpectateRequest>();

                if (resData.code == ResponseCode.Success)
                {
                    Debug.Log($"[DrillInstructorClient] 관전 준비 성공: {resData.code}");
                    OnAdminSpectateRequestSuccess.Invoke(resData);
                }
                else
                {
                    Debug.Log($"[DrillInstructorClient] 관전 준비 실패: {resData.code}");
                    OnAdminSpectateRequestFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 관전 준비 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResInstanceSignalBroadcast(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResInstanceSignalBroadcast resData = _reader.ReadSerializable<ResInstanceSignalBroadcast>();

                switch (resData.signalType)
                {
                    case InstanceSignalType.Created:
                        Debug.Log($"[TraineeClient] 훈련 {resData.signalType.ToString()} 신호 수신!");
                        break;
                    case InstanceSignalType.Ready:
                        Debug.Log($"[TraineeClient] 훈련 {resData.signalType.ToString()} 신호 수신!");
                        break;
                    case InstanceSignalType.Start:
                        Debug.Log($"[TraineeClient] 훈련 {resData.signalType.ToString()} 신호 수신!");
                        OnTrainingBeginSuccess.Invoke(new ResTrainingBegin());
                        break;
                    case InstanceSignalType.Stop:
                        break;
                    case InstanceSignalType.Pause:
                    case InstanceSignalType.Resume:
                        string stateStr = resData.signalType == InstanceSignalType.Pause ? "일시정지" : "재개";
                        Debug.Log($"[TraineeClient] 훈련 {stateStr} 신호 수신!");
                        break;
                }


                //OnInstanceSignalBroadcastReceived.Invoke(resData);

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 인스턴스 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResAdminUserLoginNotification(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResAdminUserLoginNotification resData = _reader.ReadSerializable<ResAdminUserLoginNotification>();

                if (OnUpdateOnlineUser(resData))
                {
                    OnUserLoginReceived?.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 유저 로그인 파싱 실패: {_reader}");
            return false;
        }


        public bool OnResAdminUserLogOutNotification(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResAdminUserLogOutNotification resData = _reader.ReadSerializable<ResAdminUserLogOutNotification>();

                if (OnUpdateOnlineUser(resData))
                {
                    OnUserLogOutReceived.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 유저 로그아웃 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResAdminGetOnlineUsers(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResAdminGetOnlineUsers resData = _reader.ReadSerializable<ResAdminGetOnlineUsers>();

                if (resData.code == ResponseCode.Success)
                {
                    if (OnUpdateAllOnlineUser(resData))
                    {
                        OnUserGetReceived.Invoke(resData);
                    }

                    Debug.Log($"[DrillInstructorClient] 접속자 목록 수신됨: {resData.count}명 (관리자: {resData.adminCount}, 사용자: {resData.userCount})");
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 접속자 목록 조회 실패: {resData.code}");
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 유저 로그아웃 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResAdminPartyNotification(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResAdminPartyNotication resData = _reader.ReadSerializable<ResAdminPartyNotication>();

                Debug.Log($"[DrillInstructorClient] 파티 알림: {resData.instructorNotificationType}");

                OnAdminPartyNoticationReceived.Invoke(resData);
                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 유저 로그아웃 파싱 실패: {_reader}");
            return false;
        }
    }
}
