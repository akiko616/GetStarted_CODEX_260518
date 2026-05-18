using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using TRAINEE;
using UnityEngine;

public class LobbyController : MapControllerBase
{
    public override void Init()
    {
        base.Init();

        GameEventManager.Instance.Subscribe(EEventType.Interaction, this);
        GameEventManager.Instance.Subscribe(EEventType.UpdateView, this);

        LoadingHelper.RegisterLoading(new Loading(LoadingLobbyItemAsync));

        UiManager.Instance.LoadPopup<UILobbyEquipPopup>(EPopupType.UILobbyEquipPopup).Forget();
        UiManager.Instance.LoadPopup<UiEduDialogPopup>(EPopupType.UiEduGuidePopup).Forget();
    }

    public override void RegisterModel(string id, MapModelBase model)
    {
        base.RegisterModel(id, model);
    }

    public override void RegisterView(string id, IMapView view)
    {
        base.RegisterView(id, view);
    }

    public override void OnEvent(GameEvent data)
    {
        switch (data._type)
        {
            case EEventType.Interaction:
                OnModelUpdate(data);
                break;
            case EEventType.UpdateView:
                OnViewUpdate(data);
                break;
        }
    }

    protected override void OnModelUpdate(GameEvent data)
    {
        MapModelBase model = null;

        if (_models.TryGetValue(data._id, out model))
        {
            model.UpdateModel(data);
        }
    }

    protected override void OnViewUpdate(GameEvent data)
    {
        IMapView view = null;

        if (_views.TryGetValue(data._id, out view))
        {
            view.UpdateView(data);
        }
    }
    public async UniTask LoadingLobbyItemAsync(Action<float, string> onProgress, int delaytime)
    {

        int totalCnt = 0;
        int currentCnt = 0;

        List<TrainingLobbyEquipData> datas = DataManager.Instance.GetAllData<TrainingLobbyEquipData>(EDataType.TrainingLobbyEquipData);

        totalCnt = datas.Count;
        for (int i = 0; i < datas.Count; i++)
        {
            if (datas[i].scenario == GameManager.Instance.GetCurrentScenario)
            {
                string id = datas[i].equipid;
                Vector3 position = new Vector3(datas[i].PosX, datas[i].PosY, datas[i].PosZ);
                Quaternion rotation = new Quaternion(datas[i].RotX, datas[i].RotY, datas[i].RotZ, 1f);
                ItemManager.Instance.LocalSpawnWorldItem(id, position, rotation);
            }

            currentCnt++;
            float progress = (float)currentCnt / totalCnt;

            onProgress?.Invoke(progress, $"로비 아이템 준비 중...");
            await UniTask.Delay(delaytime);
        }
    }


}
