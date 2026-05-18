using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class UIComponent_Toggle : UIComponent, IPointerDownHandler, IPointerUpHandler
{
    Button _targetButton;

    [Header("Toggle")]
    Sprite _targetButtonImage;
    Sprite _prevButtonImageOnClick;

    UnityAction _onPointUp;
    UnityAction _onPointDown;

    bool isOn;
    protected override void Update()
    {
        base.Update();
    }
    /// <summary>
    /// Toggle 버튼 On,Off시 _targetButton이 바뀔 이미지에 대한 설정
    /// </summary>
    /// <param name="ButtonOff"></param>
    /// <param name="ButtonOn"></param>
    public void SetupToggleImage(Sprite ButtonOff, Sprite ButtonOn)
    {
        _prevButtonImageOnClick = ButtonOff;
        _targetButtonImage = ButtonOn;
    }
    public override void Init()
    {
        base.Init();
        _targetButton = GetComponent<Button>();
    }

    public void AddToggleListener(UnityAction<bool> action)
    {
        _targetButton.onClick.RemoveAllListeners();

        _targetButton.onClick.AddListener(() =>
        {
            ChangeButtonBackGround();

            action?.Invoke(isOn);
        });
    }
    public void AddPressListener(UnityAction onDown, UnityAction onUp)
    {
        _onPointUp = onUp;
        _onPointDown = onDown;
    }
    public void ChangeButtonBackGround() 
    {
        isOn = !isOn;
        _targetButton.image.sprite = isOn ? _targetButtonImage : _prevButtonImageOnClick; 
    
    }

    public void Click()
    {
        _targetButton.onClick.Invoke();
    }
    public void OnPointerUp(PointerEventData eventData)
    {
        _onPointUp?.Invoke();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _onPointDown?.Invoke();
    }


}
