using System.Collections.Generic;
using TMPro;
using UnityEngine;


namespace TRAINEE
{
    public class UiInteractionPopup : PopupBase
    {
        [SerializeField] private TextMeshProUGUI _buttonText;
        [SerializeField] private UIComponent_ImageAnimate _imageAnimate;

        Sprite[] _blinkImages;

        List<UIComponent> _uiComponents = new();

        public override void Init()
        {
            base.Init();

            InitAddListener();

            _blinkImages = SpriteManager.Instance.GetItemFrames("cursor_blink");
        }

        public void SetupButtonText(string msg)
        {
            _buttonText.text = msg;
        }

        private void InitAddListener()
        {

        }
        public override void UIComponentRegister(UIComponent component)
        {
            base.UIComponentRegister(component);

            _uiComponents.Add(component);

        }
        public override void Show()
        {
            base.Show();
            _imageAnimate.Play(_blinkImages);
        }

        public override void Hide()
        {
            base.Hide();
            _imageAnimate.Stop();
        }
    }
}
