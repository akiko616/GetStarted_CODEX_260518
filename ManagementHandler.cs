using DatabasePlugin;
using NetworkResponeData;
using System.Collections.Generic;
using TMPro;
using TRAINEE;
using UnityEngine;
using UnityEngine.UI;

namespace DrillSergeant
{
    public class ManagementHandler : MonoBehaviour
    {
        [SerializeField] private Button _addManagerBtn;
        [SerializeField] private Transform _managersParent;
        [SerializeField] private GameObject _managerRowPrefab;

        [SerializeField] private GameObject _deleteNoticeBox;
        [SerializeField] private TextMeshProUGUI _deleteNoticeText;
        [SerializeField] private Button _noticeCancelBtn;
        [SerializeField] private Button _noticeDeleteBtn;

        [SerializeField] private GameObject _warningBox;
        [SerializeField] private TextMeshProUGUI _warningNoticeText;
        [SerializeField] private Button _verifyBtn;

        private List<ManagerRow> _rows = new();
        private List<ManagerRow> _pendingRows = new();
        private int _receivedAccountCount = 0;

        private void Start()
        {
            AddListeners();

            if (_rows.Count == 0)
                AccountReceive();

            _pendingRows.Clear();
        }

        private void AddListeners()
        {
            NetworkManager net = NetworkManager.Instance;

            net.OnGetTypeAccountsSuccess += GetTypeAccountsSuccess;
            net.OnGetTypeAccountsFailed += GetTypeAccountsFailed;

            net.OnRegisterAccountSuccess += RegisterSuccess;
            net.OnRegisterAccountFailed += RegisterFailed;

            net.OnUpdateAccountSuccess += UpdateSuccess;
            net.OnUpdateAccountFailed += UpdateFailed;

            net.OnDeleteAccountSuccess += DeleteAccountSuccess;
            net.OnDeleteAccountFailed += DeleteAccountFailed;


            _noticeDeleteBtn.onClick.AddListener(DeleteBtnClicked);
            _noticeCancelBtn.onClick.AddListener(CancelBtnClicked);
            _addManagerBtn.onClick.AddListener(AddNewManager);
            _verifyBtn.onClick.AddListener(VerifyBtnClicked);
        }

        private void OnDestroy()
        {
            NetworkManager net = NetworkManager.Instance;

            NetworkManager.Instance.OnGetTypeAccountsSuccess -= GetTypeAccountsSuccess;
            NetworkManager.Instance.OnGetTypeAccountsFailed -= GetTypeAccountsFailed;

            net.OnRegisterAccountSuccess -= RegisterSuccess;
            net.OnRegisterAccountFailed -= RegisterFailed;

            net.OnUpdateAccountSuccess -= UpdateSuccess;
            net.OnUpdateAccountFailed -= UpdateFailed;

            net.OnDeleteAccountSuccess -= DeleteAccountSuccess;
            net.OnDeleteAccountFailed -= DeleteAccountFailed;

            _noticeDeleteBtn.onClick.RemoveListener(DeleteBtnClicked);
            _noticeCancelBtn.onClick.RemoveListener(CancelBtnClicked);
            _addManagerBtn.onClick.RemoveListener(AddNewManager);
            _verifyBtn.onClick.RemoveListener(VerifyBtnClicked);
        }

        public void AccountReceive()
        {
            _rows.Clear();
            _receivedAccountCount = 0;

            // 교생 리스트
            NetworkManager.Instance.OnReqGetTypeAccount(0);
            // 교관 리스트
            NetworkManager.Instance.OnReqGetTypeAccount(1);

        }

        // 관리자화면 초기에만
        private void GetTypeAccountsSuccess(ResAccountGetType res)
        {
            foreach (AccountResponse account in res.accounts)
            {
                GameObject rowObj = Instantiate(_managerRowPrefab, _managersParent);
                ManagerRow row = rowObj.GetComponent<ManagerRow>();

                row.IsInitial = false;
                row.InputDataManagerRow(false, this, account.Id, "****", account.AccountType, account.UserInfo, account.CreatedAt.ToString());
                row.Number = (_rows.Count + 1).ToString();
                row.IsEditMode(false);

                _rows.Add(row);
            }
            _receivedAccountCount++;

            if (_receivedAccountCount == 2)
            {
                NetworkManager.Instance.OnGetTypeAccountsSuccess -= GetTypeAccountsSuccess;
                NetworkManager.Instance.OnGetTypeAccountsFailed -= GetTypeAccountsFailed;

                _addManagerBtn.transform.parent.SetAsLastSibling();
            }
        }

        private void GetTypeAccountsFailed(ResAccountGetType res)
        {
            Debug.Log($"어카운트 조회 실패 {res.code}");

            NetworkManager.Instance.OnGetTypeAccountsSuccess -= GetTypeAccountsSuccess;
            NetworkManager.Instance.OnGetTypeAccountsFailed -= GetTypeAccountsFailed;
        }
        private void AddNewManager()
        {
            GameObject rowPrefab = Instantiate(_managerRowPrefab, _managersParent);
            ManagerRow rowScr = rowPrefab.GetComponent<ManagerRow>();

            rowScr.InputDataManagerRow(true, this);
            rowScr.IsInitial = true;

            _addManagerBtn.transform.parent.SetAsLastSibling();

            _rows.Add(rowScr);
            rowScr.Number = _rows.Count.ToString();
        }

        public void RequestRegister(ManagerRow row, string id, string pw, int type)
        {
            _pendingRows.Add(row);
            NetworkManager.Instance.OnReqRegisterAccount(id, pw, type);
        }

        private void RegisterSuccess(ResAccountRegister res)
        {
            if (_pendingRows.Count < 1)
                return;

            _pendingRows[0].OnSaveProcessFinished(true);
            _pendingRows[0].SaveBtnEvent();

            Debug.Log($"어카운트 등록 성공, 등록아이디: {res.account.Id},  {res.code}");
        }

        private void RegisterFailed(ResAccountRegister res)
        {
            _pendingRows.RemoveAt(0);
            Debug.Log($"어카운트 등록 실패 {res.code}");
        }

        public void RequestUpdate(ManagerRow row, AccountData data)
        {
            if (_pendingRows[0].IsInitial == true)
                NetworkManager.Instance.OnReqUpdateAccount(data);
            else
            {
                _pendingRows.Add(row);
                NetworkManager.Instance.OnReqUpdateAccount(data);
            }
        }

        private void UpdateSuccess(ResAccountUpdate res)
        {
            Debug.Log($"계정 업데이트 성공, 계정아이디: {_pendingRows[0].Id},   {res.code}");

            _pendingRows[0].OnSaveProcessFinished(true);
            _pendingRows.RemoveAt(0);
        }

        private void UpdateFailed(ResAccountUpdate res)
        {
            Debug.Log($"계정 업데이트 실패, 계정아이디: {_pendingRows[0].Id},   {res.code}");
            _pendingRows.RemoveAt(0);
        }

        public void ManagerDeleteNotice(string info, string id, ManagerRow row)
        {
            _noticeDeleteBtn.interactable = true;
            _noticeCancelBtn.interactable = true;

            _deleteNoticeBox.transform.parent.gameObject.SetActive(true);
            _deleteNoticeBox.gameObject.SetActive(true);
            _warningBox.gameObject.SetActive(false);

            _deleteNoticeText.text = $"[ {info} ] [ {id} ]";
            _pendingRows.Add(row);
        }

        private void DeleteBtnClicked()
        {
            _noticeDeleteBtn.interactable = false;
            _noticeCancelBtn.interactable = false;

            NetworkManager.Instance.OnReqDeleteAccount(_pendingRows[0].Id);
        }

        private void CancelBtnClicked()
        {
            _noticeDeleteBtn.interactable = false;
            _noticeCancelBtn.interactable = false;

            _pendingRows.RemoveAt(0);
            _deleteNoticeText.text = $"[ INFO ] [ ID ]";

            _deleteNoticeBox.transform.parent.gameObject.SetActive(false);
        }

        private void DeleteAccountSuccess(ResAccountDelete res)
        {
            _rows.Remove(_pendingRows[0]);
            Destroy(_pendingRows[0].gameObject);

            RearrangeNumber();

            _deleteNoticeBox.transform.parent.gameObject.SetActive(false);

            Debug.Log($"어카운트 삭제 성공  {res.code}");
        }

        private void DeleteAccountFailed(ResAccountDelete res)
        {
            Debug.Log($"어카운트 삭제 실패  {res.code}");
        }



        public void WarningNotice()
        {
            _warningNoticeText.text = "모든 필드를 입력해주세요";


            _warningBox.transform.parent.gameObject.SetActive(true);

            _deleteNoticeBox.gameObject.SetActive(false);
            _warningBox.gameObject.SetActive(true);
        }

        private void VerifyBtnClicked()
        {
            _warningBox.transform.parent.gameObject.SetActive(false);
        }

        public void RearrangeNumber()
        {
            for (int i = 0; i < _rows.Count; ++i)
            {
                _rows[i].Number = (i + 1).ToString();
            }
        }
    }
}