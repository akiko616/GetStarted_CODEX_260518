using Cysharp.Threading.Tasks;
using TRAINEE;
using UnityEngine;

public class UiEnterTrainingLobbyPopup : PopupBase
{
    [SerializeField] UIComponent_Fade _fade;
    public override void Hide()
    {
        base.Hide();
    }
    public override async void Show()
    {
        base.Show();

        _fade.AlphaControl(1f);

        await UniTask.Delay(1000);
        FadeOutHide();
    }
    public void FadeOutHide()
    {
        _fade.Fade(1f, 0f, 1f);
    }
    public override void Init()
    {
        base.Init();

        _fade = GetComponent<UIComponent_Fade>();

        _fade.OnFadeEnd += () =>
        {
            UiManager.Instance.Hide(EPopupType.UiEnterTrainingLobbyPopup);
        };
    }
    
}
