using Cysharp.Threading.Tasks;
using DarkRift;
using GameInstancePlugin;
using NetworkResponeData;
using PartyManagerPlugin;
using UnityEngine;

namespace TRAINEE
{
    public partial class NetworkManager
    {
        public bool OnResTraininigInfo(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResTrainingInfo resData = _reader.ReadSerializable<ResTrainingInfo>();

                if (resData.code == TrainingResponseCode.Success)
                {
                    if (_reader.Length >= 8)
                    {
                        Debug.Log($"[DrillInstructorClient] 훈련 시작 확인됨: 인스턴스={resData.instanceId}, 메시지={resData.statusMessage}");
                        OnTrainingStart.Invoke(resData);
                    }
                    else
                    {
                        Debug.Log($"[DrillInstructorClient] 게임 서버 준비 완료: {resData.instance}");
                        OnGameServerReady.Invoke(resData);
                    }
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 훈련 시작 실패: {resData.code}");
                    OnTrainingStartFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 파티 가입 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResTrainingSetup(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResTrainingSetup resData = _reader.ReadSerializable<ResTrainingSetup>();

                if (resData.code == TrainingResponseCode.Success)
                {
                    Debug.Log("[DrillInstructorClient] 훈련 설정 성공 - 인스턴스 프로세스 실행됨");
                    OnTrainingSetupSuccess.Invoke(resData);
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 훈련 설정 실패: {resData.code}");
                    OnTrainingSetupFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 훈련 설정 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResTrainingBegin(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResTrainingBegin resData = _reader.ReadSerializable<ResTrainingBegin>();

                if (resData.code == TrainingResponseCode.Success)
                {
                    Debug.Log("[DrillInstructorClient] 훈련 시작 성공");
                    OnTrainingBeginSuccess.Invoke(resData);
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 훈련 시작 실패: {resData.code}");
                    OnTrainingBeginFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 훈련 시작 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResTrainingPause(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResTrainingPause resData = _reader.ReadSerializable<ResTrainingPause>();

                if (resData.code == TrainingResponseCode.Success)
                {
                    Debug.Log("[DrillInstructorClient] 훈련 정지 성공");
                    OnTrainingPauseSuccess.Invoke(resData);
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 훈련 정지 실패: {resData.code}");
                    OnTrainingPauseFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 훈련 정지 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResTrainingStop(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResTrainingStop resData = _reader.ReadSerializable<ResTrainingStop>();

                if (resData.code == TrainingResponseCode.Success)
                {
                    Debug.Log($"[DrillInstructorClient] 훈련 종료 성공: {resData.partyId}");
                    OnTrainingStopSuccess.Invoke(resData);
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 훈련 시작 실패: {resData.code}");
                    OnTrainingStopFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 훈련 시작 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResTrainingStatus(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResTrainingStatus resData = _reader.ReadSerializable<ResTrainingStatus>();

                if (resData.code == TrainingResponseCode.Success)
                {
                    Debug.Log($"[DrillInstructorClient] 훈련 상태 성공: {resData.partyId}");
                    OnTrainingStatusReceived.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 훈련 상태 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResTrainingBeginReady(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResTrainingBeginReady resData = _reader.ReadSerializable<ResTrainingBeginReady>();

                if (resData.code == TrainingResponseCode.Success)
                {
                    Debug.Log($"[DrillInstructorClient] 훈련 시작준비 성공: {resData.partyId}");

                    OnTrainingBeginReadyReceived.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 훈련 상태 파싱 실패: {_reader}");
            return false;

        }

        public bool OnResTrainingWakeResult(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResTrainingWakeResult resData = _reader.ReadSerializable<ResTrainingWakeResult>();

                if (resData.code == TrainingResponseCode.Success)
                {

                    Debug.Log($"[DrillInstructorClient] 훈련 기상 결과 성공: {resData.trainingWakeResultData}");

                    _fishNetAddress = resData.trainingWakeResultData.ServerAddress;
                    _fishNetPort = (ushort)resData.trainingWakeResultData.ServerPort;

                    //OnTrainingWakeResultSuccess.Invoke(resData);
                }
                else
                {
                    Debug.LogWarning($"[TraineeClient] 훈련 기상 결과 실패: {resData.code}");
                    OnTrainingWakeResultFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 훈련 기상 결과 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResTrainingCreated(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResTrainingCreated resData = _reader.ReadSerializable<ResTrainingCreated>();

                if (resData.code == TrainingResponseCode.Success)
                {

                    Debug.Log($"[DrillInstructorClient] 훈련 생성 성공: {resData.trainingCreatedData}");

                    _currentPartyId = resData.trainingCreatedData.PartyId;

                    OnConnectToFishNet(_fishNetAddress, _fishNetPort).Forget();

                    // 임시 작업
                    //GameManager.Instance.GetCurrentScenario = EScenario.Collapse;
                }
                else
                {
                    Debug.LogWarning($"[TraineeClient] 훈련 생성 실패: {resData.code}");
                    OnTrainingCreatedFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 훈련 생성 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResTrainingMidJoin(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResTrainingMidJoin resData = _reader.ReadSerializable<ResTrainingMidJoin>();

                if (resData.code == TrainingResponseCode.Success)
                {
                    Debug.Log($"[DrillInstructorClient] 훈련 중간진입 성공: {resData.ip} / {resData.port}");
                    OnTrainingMidJoinSuccess.Invoke(resData);
                }
                else
                {
                    Debug.Log($"[DrillInstructorClient] 훈련 중간진입 실패: {resData.code}, {resData.ip}, {resData.port}");
                    OnTrainingMidJoinFailed.Invoke(resData);
                }
                return true;
            }
            Debug.LogError($"[DrillInstructorClient] 훈련 중간진입 실패: {_reader}");
            return false;
        }

        public bool OnResTrainingMidLeave(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResTrainingMidLeave resData = _reader.ReadSerializable<ResTrainingMidLeave>();

                if (resData.code == TrainingResponseCode.Success)
                {
                    Debug.Log($"[DrillInstructorClient] 훈련 중간탈퇴 성공: {resData.code}");
                    OnTrainingMidLeaveSuccess.Invoke(resData);                    
                }
                else
                {
                    Debug.Log($"[DrillInstructorClient] 훈련 중간탈퇴 실패: {resData.code}");
                    OnTrainingMidLeaveFailed.Invoke(resData);
                }
                return true;
            }
            Debug.Log($"[DrillInstructorClient] 훈련 중간탈퇴 실패: {_reader}");
            return false;
        }

        public bool OnResTrainingResult(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResTrainingResult resData = _reader.ReadSerializable<ResTrainingResult>();

                if (resData.code == TrainingResponseCode.Success)
                {
                    Debug.Log($"[교관&교육생] 훈련 결과 성공: {resData.code}");
                    OnTrainingResultSuccess.Invoke(resData);
                }
                else
                {
                    Debug.Log($"[교관&교육생] 훈련 결과 실패: {resData.code}");
                    OnTrainingResultFailed.Invoke(resData);
                }
                return true;
            }

            Debug.Log($"[교관&교육생] 훈련 결과 실패: {_reader}");
            return false;
        }

        public bool OnResTrainingGracefulQuit(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResTrainingGracefulQuit resData = _reader.ReadSerializable<ResTrainingGracefulQuit>();

                Debug.Log($"[네트워크] 일반 종료(GracefulQuit) 수신 완료: 파티={resData.partyId}");

                OnTrainingGracefulQuitReceived?.Invoke(resData);

                return true;
            }

            Debug.LogError($"[네트워크] 일반 종료(GracefulQuit) 파싱 실패: {_reader}");
            return false;
        }       
    }
}