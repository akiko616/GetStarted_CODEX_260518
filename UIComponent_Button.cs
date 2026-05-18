using TRAINEE;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class UIComponent_Button : UIComponent
{
    Image _buttonImage;
    Button _targetButton;


    UnityAction _onClick;

    public override void Init()
    {
        base.Init();

        _buttonImage = GetComponent<Image>();
        _targetButton = GetComponent<Button>();
    }
    
    public void AddClickListener(UnityAction action)
    {
        _targetButton.onClick.RemoveAllListeners();

        _onClick = action;

        _targetButton.onClick.AddListener(() =>
        {
            _onClick?.Invoke();
        });
    }

    public void SelectButton(bool isSelected)
    {
        //일단 이미지만 변경
        if (_buttonImage == null) return;

        _buttonImage.color = new Color(
            _buttonImage.color.r,
            _buttonImage.color.g,
            _buttonImage.color.b,
            isSelected ? 1f : 0.392f
        );
    }

}
