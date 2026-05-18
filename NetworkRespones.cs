using DarkRift;
using DatabasePlugin;
using NetworkResponeData;
using System.Collections.Generic;
using UnityEngine;


namespace TRAINEE
{
    public partial class NetworkManager
    {
        public bool OnResLogin(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResLogin resData = _reader.ReadSerializable<ResLogin>();

                if (resData.code == ResponseCode.Success)
                {
                    Debug.Log($"[TraineeClient] 로그인 성공: {resData.code}");

                    _currentAccount = resData.account;
                    _currentAccountId = _currentAccount.Id;

                    OnLoginSuccess.Invoke(resData);
                }
                else
                {
                    Debug.LogWarning($"[TraineeClient] 로그인 실패: {resData.code}");
                    OnLoginFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[TraineeClient] 로그인 응답 파싱 실패: {_reader}");

            return false;
        }

        public bool OnResRegisterAccount(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResAccountRegister resData = _reader.ReadSerializable<ResAccountRegister>();

                if (resData.code == ResponseCode.Success)
                {
                    Debug.Log($"[DrillInstructorClient] 계정 등록 성공: {resData.account}");
                    OnRegisterAccountSuccess.Invoke(resData);
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 계정 등록 실패: {resData.code}");
                    OnRegisterAccountFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 계정 등록 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResGetAccount(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResAccountGet resData = _reader.ReadSerializable<ResAccountGet>();

                if (resData.code == ResponseCode.Success)
                {
                    Debug.Log($"[DrillInstructorClient] 계정 조회 성공: {resData.code}");
                    OnGetAccountSuccess.Invoke(resData);
                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 계정 조회 실패: {resData.code}");
                    OnGetAccountFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 계정 조회 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResGetTypeAccount(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResAccountGetType resData = _reader.ReadSerializable<ResAccountGetType>();

                if (resData.code == ResponseCode.Success)
                {
                    Debug.Log($"[DrillInstructorClient] 타입별 계정 조회 성공: {resData.code}");

                    OnGetTypeAccountsSuccess.Invoke(resData);

                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 타입별 계정 조회 실패: {resData.code}");
                    OnGetTypeAccountsFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 타입별 계정 조회 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResUpdateAccount(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResAccountUpdate resData = _reader.ReadSerializable<ResAccountUpdate>();

                if (resData.code == ResponseCode.Success)
                {
                    Debug.Log($"[DrillInstructorClient] 계정 업데이트 성공: {resData.code}");

                    OnUpdateAccountSuccess.Invoke(resData);

                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 계정 업데이트 실패: {resData.code}");
                    OnUpdateAccountFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 계정 업데이트 파싱 실패: {_reader}");
            return false;
        }

        public bool OnResDeleteAccount(DarkRiftReader _reader)
        {
            if(_reader != null )
            {
                ResAccountDelete resData = _reader.ReadSerializable<ResAccountDelete>();

                if (resData.code == ResponseCode.Success)
                {
                    Debug.Log($"[DrillInstructorClient] 계정 삭제 성공: {resData.code}");

                    OnDeleteAccountSuccess.Invoke(resData);

                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 계정 삭제 실패: {resData.code}");
                    OnDeleteAccountFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 계정 삭제 파싱 실패: {_reader}");

            return false;
        }

        public bool OnResChangePassword(DarkRiftReader _reader)
        {
            if (_reader != null)
            {
                ResChangePassword resData = _reader.ReadSerializable<ResChangePassword>();

                if (resData.code == ResponseCode.Success)
                {
                    Debug.Log($"[DrillInstructorClient] 비밀번호 변경 성공: {resData.code}");

                    OnChangePasswordSuccess.Invoke(resData);

                }
                else
                {
                    Debug.LogWarning($"[DrillInstructorClient] 비밀번호 변경 실패: {resData.code}");
                    OnChangePasswordFailed.Invoke(resData);
                }

                return true;
            }

            Debug.LogError($"[DrillInstructorClient] 비밀번호 변경 파싱 실패: {_reader}");
            return false;
        }

    }
}
