using TRAINEE;
using UnityEngine;

public class MountainController : MapControllerBase
{
    public override void Init()
    {
        base.Init();

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
        base.OnEvent(data);
    }

    protected override void OnModelUpdate(GameEvent data)
    {
        base.OnModelUpdate(data);
    }

    protected override void OnViewUpdate(GameEvent data)
    {
        base.OnViewUpdate(data);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
    }
}
