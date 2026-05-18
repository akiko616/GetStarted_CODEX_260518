using Cysharp.Threading.Tasks;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TRAINEE
{
    public class UIPauseLayer : LayerBase
    {
        [Header("Panel Objects")]      
        [SerializeField] GameObject pausePanel;
        [SerializeField] GameObject stopPanel;

        [Header("UIComponent Fade")]
        [SerializeField] UIComponent_Fade _fade;
        public override void Init()
        {
            base.Init();

            _fade = GetComponent<UIComponent_Fade>();

            _fade.OnFadeEnd += () =>
            {
                UiManager.Instance.Hide(ELayerType.UIPauseLayer);
            };
        }

        public override void Show()
        {
            base.Show();

            _fade.AlphaControl(1f);
            SetSortingOrder(UiManager.SUPER_POPUP_SORTING);
        }

        public override void Hide()
        {
            base.Hide();
        }
        public void FadeOutHide()
        {
            _fade.Fade(1f, 0f, 1f);
        }

        public void SetupData(PauseType type)
        {
            pausePanel.SetActive(type == PauseType.Pause);
            stopPanel.SetActive(type == PauseType.Stop);      
        }



    }
    
}
