using Cysharp.Threading.Tasks;
using TMPro;
using TRAINEE;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로비 아이템 인터렉션 시 나오는 비디오 팝업
/// </summary>

public class UILobbyEquipPopup : PopupBase
{
    [SerializeField] TMP_Text itemName;
    [SerializeField] Button _closeButton;
    [SerializeField] RawImage _video;
    public override void Hide()
    {
        base.Hide();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
    public override void Init()
    {
        //base.Init();

        _closeButton.onClick.AddListener(()=>UiManager.Instance.Hide(this));

    }
    public void SetupData(string id)
    {
        EquipData equipData = DataManager.Instance.GetData<EquipData>(EDataType.EquipData, id);
        string name = DataManager.Instance.GetDisplayName(equipData.displayName);
        itemName.text = name;

        VideoManager.Instance.PrepareVideo(id, _video);
    }
    public override void Show()
    {
        base.Show();

        SetSortingOrder(UiManager.SUPER_POPUP_SORTING);

        VideoManager.Instance.PlayVideo();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

    }
    public override void UIComponentRegister(UIComponent component)
    {
        base.UIComponentRegister(component);
    }

}
