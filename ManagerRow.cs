using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DrillSergeant
{
    public class ManagerRow : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _number;
        [SerializeField] private TMP_InputField _inputID;
        [SerializeField] private TMP_InputField _inputPW;
        [SerializeField] private Toggle[] _authorityToggles = new Toggle[2];
        [SerializeField] private TMP_InputField _infoField;
        [SerializeField] private TextMeshProUGUI _createdTime;
        [SerializeField] private Button _editBtn;
        [SerializeField] private Button _saveBtn;
        [SerializeField] private Button _deleteBtn;

        private DateTime dateTime;
        private bool _isInitial = false;
        private ManagementHandler _handler;

        private string _beforeEditId = null;
        private string _beforeEditPw = null;
        private int _beforeEditType = -1;
        private string _beforeEditInfo = null;

        public bool IsInitial { get => _isInitial; set => _isInitial = value; }

        public string Id { get => _inputID.text; }

        private void Start()
        {
            // 기본 이벤트 등 등록
            InitAddListener();
        }

        private void Update()
        {
            ForceTabNavigation(_inputID, _inputPW);
            ForceTabNavigation(_inputPW, _infoField);
            ForceTabNavigation(_infoField, _inputID);
        }

        private void InitAddListener()
        {
            _editBtn.onClick.AddListener(EditBtnEvent);
            _saveBtn.onClick.AddListener(SaveBtnEvent);
            _deleteBtn.onClick.AddListener(DeleteBtnEvent);
            _inputID.onValidateInput = ValidateChar;
            _inputPW.onValidateInput = ValidateChar;
        }

        private void OnDestroy()
        {
            _editBtn.onClick.RemoveListener(EditBtnEvent);
            _saveBtn.onClick.RemoveListener(SaveBtnEvent);
            _deleteBtn.onClick.RemoveListener(DeleteBtnEvent);
            _inputID.onValidateInput = null;
            _inputPW.onValidateInput = null;
        }


        // 프리팹 배치후 실행필요
        public void InputDataManagerRow(bool isNew, ManagementHandler handler, string id = null, string pw = null, int authority = 0, string info = null, string time = null)
        {
            _inputID.text = id;
            _inputPW.text = pw;
            _infoField.text = info;
            _handler = handler;

            if (authority >= 2)
                Debug.Log($"접근권한 번호가 0.교생 혹은 1.교관이 아닙니다. 타입번호: {authority}");
            else
            {
                if (authority == 0)
                    _authorityToggles[1].isOn = true;
                else
                    _authorityToggles[0].isOn = true;
            }

            if (isNew)
            {
                dateTime = DateTime.Now;
                _createdTime.text = DateTime.Now.ToString();
            }
            else if (time != null)
                _createdTime.text = time;
        }

        private void EditBtnEvent()
        {
            if (IsThereBlank())
            {
                _handler.WarningNotice();
                return;
            }

            _beforeEditId = _inputID.text;
            _beforeEditPw = _inputPW.text;
            _beforeEditType = GetCurrentAuthorityType();
            _beforeEditInfo = _infoField.text;

            IsEditMode(true);
        }

        public void SaveBtnEvent()
        {
            if (IsThereBlank())
            {
                _handler.WarningNotice();
                return;
            }
            IsEditMode(false);            

            if (_isInitial)
            {
                _handler.RequestRegister(this, _inputID.text, _inputPW.text, GetCurrentAuthorityType());
            }
            else if (_infoField.text != _beforeEditId || GetCurrentAuthorityType() != _beforeEditType || _infoField.text != _beforeEditInfo || _inputPW.text != _beforeEditPw)
            {
                // 패스워드 변경되는 업데이트는 서버에서 차후(약 8월)에 작업예정
                DatabasePlugin.AccountData data = new()
                {
                    Id = _inputID.text,
                    PasswordHash = _inputPW.text, 
                    AccountType = GetCurrentAuthorityType(),
                    UserInfo = _infoField.text
                };

                _handler.RequestUpdate(this, data);
            }
        }

        public void OnSaveProcessFinished(bool isSuccess)
        {
            if (isSuccess)
            {
                _isInitial = false;
            }
        }


        private void DeleteBtnEvent()
        {
            if (string.IsNullOrEmpty(_inputID.text))
            {
                Destroy(this.gameObject);

                _handler.RearrangeNumber();
                return;
            }

            _handler.ManagerDeleteNotice(_infoField.text, _inputID.text, this);

        }

        private void ForceTabNavigation(TMP_InputField from, TMP_InputField to)
        {
            if (!from.isFocused)
                return;

            if (to != null && Input.GetKeyDown(KeyCode.Tab))
            {
                to.ActivateInputField();
                to.Select();
            }
        }

        private bool IsThereBlank()
        {
            bool isBlank = false;

            if (string.IsNullOrWhiteSpace(_inputID.text) || string.IsNullOrWhiteSpace(_inputPW.text) || string.IsNullOrWhiteSpace(_infoField.text))
                isBlank = true;

            return isBlank;
        }


        public void IsEditMode(bool isEditting)
        {
            _inputID.interactable = false;
            _inputPW.interactable = false;
            _authorityToggles[0].interactable = isEditting;
            _authorityToggles[1].interactable = isEditting;
            _infoField.interactable = isEditting;
        }

        private char ValidateChar(string text, int charIndex, char addedChar)
        {
            char lowerChar = char.ToLower(addedChar);

            if ((lowerChar >= 'a' && lowerChar <= 'z') || (lowerChar >= '0' && lowerChar <= '9'))
            {
                return lowerChar;
            }

            return '\0';
        }

        // (화면Ui) 0번: 교관, 1번: 교생
        // (서버) 0번: 교생, 1번: 교관
        private int GetCurrentAuthorityType()
        {
            return _authorityToggles[0].isOn ? 1 : 0;
        }

        public string Number // 첫번째는 1번
        {
            get { return _number.text; }
            set
            {
                _number.text = value;

                if (int.Parse(_number.text) % 2 == 0)
                {
                    GetComponent<Image>().enabled = false;
                }
                else
                    GetComponent<Image>().enabled = true;
            }
        }
    }
}