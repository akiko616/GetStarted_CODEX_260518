using Cysharp.Threading.Tasks;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


namespace TRAINEE
{
    public class UiEduDialogPopup : PopupBase
    {
        [SerializeField] private TMP_Text _title_txt = null;
        [SerializeField] private TMP_Text _content_txt_1 = null;
        [SerializeField] private TMP_Text _content_txt_2 = null;

        [SerializeField] protected Button _confirmBtn = null;
        [SerializeField] private TMP_Text _confirmBtn_txt = null;

        private Action _onClickCallBack = null;
        public override void Init()
        {
            base.Init();
            InitAddListener();
        }

        protected virtual void InitAddListener()
        {
            if (_confirmBtn != null)
            {
                _confirmBtn.onClick.AddListener(OnClickConfirmButton);
                _confirmBtn_txt.text = "교육 완료";
            }
        }

        public void SetupEduDialog(string title, string content1, string content2, Action callback)
        {
            _title_txt.text = DataManager.Instance.GetDisplayName(title.ToString());

            if (_content_txt_1 != null && string.IsNullOrEmpty(content1) == false)
            {
                _content_txt_1.text = DataManager.Instance.GetDisplayName(content1.ToString());
            }

            if (_content_txt_2 != null && string.IsNullOrEmpty(content2) == false)
            {
                _content_txt_2.text = DataManager.Instance.GetDisplayName(content2.ToString());
            }

            _onClickCallBack = callback;
        }

        public override void Show()
        {
            base.Show();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public override void Hide()
        {
            base.Hide();

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

        }
        public virtual void OnClickConfirmButton()
        {
            _onClickCallBack?.Invoke();
            UiManager.Instance.Hide(this);
        }
    }
}
