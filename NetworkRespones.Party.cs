using DarkRift;
using NetworkResponeData;
using PartyManagerPlugin;
using System.Collections.Generic;
using UnityEngine;

namespace TRAINEE
{
    public partial class NetworkManager
    {
        public bool OnResPartyCreate(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResPartyCreate resData = _reader.ReadSerializable<ResPartyCreate>();

                if(resData.code == PartyResponseCode.Success)
                {
                    Debug.Log($"[DrillInstructorClient] 파티 생성 성공: {resData.party}");
                    OnPartyCreateSuccess.Invoke(resData);
                }
                else
                {
                    Debug.Log($"[DrillInstructorClient] 파티 생성 실패: {resData.code}");
                    OnPartyCreateFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 파티 생성 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResPartyDestory(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResPartyDestory resData = _reader.ReadSerializable<ResPartyDestory>();

                if (resData.code == PartyResponseCode.Success)
                {
                    Debug.Log($"[DrillInstructorClient] 파티 파괴됨: 파티ID= {resData.partyid}");
                    OnPartyDestorySuccess.Invoke(resData);
                }
                else
                {
                    Debug.Log($"[DrillInstructorClient] 파티 파괴 실패: {resData.code}");
                    OnPartyDestoryFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 파티 파괴 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResPartyMove(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResPartyMove resData = _reader.ReadSerializable<ResPartyMove>();

                if (resData.code == PartyResponseCode.Success)
                {
                    Debug.Log($"[DrillInstructorClient] 파티 이동 성공: {resData.party}");
                    OnPartyMoveSuccess.Invoke(resData);
                }
                else
                {
                    Debug.Log($"[DrillInstructorClient] 파티 이동 실패: {resData.code}");
                    OnPartyMoveFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 파티 이동 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResPartyChangeRole(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResPartyChangeRole resData = _reader.ReadSerializable<ResPartyChangeRole>();

                if (resData.code == PartyResponseCode.Success)
                {
                    Debug.Log($"[DrillInstructorClient] 역할 변경 성공: {resData.party}");
                    OnPartyChangeRoleSuccess.Invoke(resData);
                }
                else
                {
                    Debug.Log($"[DrillInstructorClient] 역할 변경 실패: {resData.code}");
                    OnPartyChangeRoleFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 역할 변경 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResPartyChangeSetting(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResPartyChangeSetting resData = _reader.ReadSerializable<ResPartyChangeSetting>();

                if (resData.code == PartyResponseCode.Success)
                {
                    Debug.Log($"[DrillInstructorClient] 파티 설정 변경 성공: {resData.party}");
                    OnPartyChangeSettingSuccess.Invoke(resData);
                }
                else
                {
                    Debug.Log($"[DrillInstructorClient] 파티 설정 변경 실패: {resData.code}");
                    OnPartyChangeSettingFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 파티 설정 변경 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResPartyListGet(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResPartyListGet resData = _reader.ReadSerializable<ResPartyListGet>();

                if (resData.code == PartyResponseCode.Success)
                {
                    Debug.Log($"[DrillInstructorClient] 파티 목록 성공: {resData.count}");
                    OnPartyGetListSuccess.Invoke(resData);
                }
                else
                {
                    Debug.Log($"[DrillInstructorClient] 파티 목록 실패: {resData.code}");
                    OnPartyGetListFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 파티 목록 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResPartyJoin(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResPartyJoin resData = _reader.ReadSerializable<ResPartyJoin>();

                if (resData.code == PartyResponseCode.Success)
                {

                    Debug.Log($"[DrillInstructorClient] 파티 가입 성공: {resData.party}");
                    OnPartyJoinSuccess.Invoke(resData);
                }
                else
                {
                    Debug.Log($"[DrillInstructorClient] 파티 가입 실패: {resData.code}");
                    OnPartyJoinFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 파티 가입 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResPartyLeave(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResPartyLeave resData = _reader.ReadSerializable<ResPartyLeave>();

                if (resData.code == PartyResponseCode.Success)
                {

                    Debug.Log($"[DrillInstructorClient] 파티 탈퇴 성공: {resData.code}");
                    OnPartyLeaveSuccess.Invoke(resData);
                }
                else
                {
                    Debug.Log($"[DrillInstructorClient] 파티 탈퇴 실패: {resData.code}");
                    OnPartyLeaveFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 파티 탈퇴 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResPartyConfirm(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResPartyConfirm resData = _reader.ReadSerializable<ResPartyConfirm>();

                if (resData.code == TrainingResponseCode.Success)
                {

                    Debug.Log($"[DrillInstructorClient] 파티 확정 성공: {resData.code}");
                    OnPartyConfirmSuccess.Invoke(resData);
                }
                else
                {
                    Debug.Log($"[DrillInstructorClient] 파티 확정 실패: {resData.code}");
                    OnPartyConfirmFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 파티 확정 파싱 실패: {_reader}");
            return false;
        }


    }
}
