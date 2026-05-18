using System.Collections.Generic;
using TMPro;
using TRAINEE;
using UnityEngine;
using UnityEngine.UI;

public class LoadingImageTemplate : UIComponent
{

    [SerializeField] private List<ESceneType> sceneTypes;
    [SerializeField] private GameObject progress;
    [SerializeField] private Image progressBar;
    [SerializeField] private TMP_Text msgText;
    public List<ESceneType> SceneType => sceneTypes;

    public override void Init()
    {
        base.Init();
    }
    public void Apply(UiLoadingPopup popup)
    {
        popup.SetLoadingUI(progress, progressBar, msgText);
    }


}
