using FishNet.Connection;
using UnityEngine;

namespace TRAINEE
{
    public class DisasterStairView : DisasterViewBase, IMapView
    {
        public override void Init(string id)
        {
            base.Init(id);

            DisasterSystem disasterSystem = GameManager.Instance.GetSystem<DisasterSystem>();
            disasterSystem.RegisterDisaster(_id.Value, this);

            MapSystem mapSystem = GameManager.Instance.GetSystem<MapSystem>();
            mapSystem.MapController.RegisterView(_id.Value, this);
        }
        public override void OnStartClient()
        {
            base.OnStartClient();


            DisasterSystem disasterSystem = GameManager.Instance.GetSystem<DisasterSystem>();
            disasterSystem.RegisterDisaster(_id.Value, this);

            MapSystem mapSystem = GameManager.Instance.GetSystem<MapSystem>();
            mapSystem.MapController.RegisterView(_id.Value, this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();

            if (GameManager.isInstance)
            {
                GameManager.Instance.GetSystem<DisasterSystem>().UnRegisterDisaster(_id.Value, this);
                GameManager.Instance.GetSystem<MapSystem>().MapController.UnRegisterView(_id.Value, this);
            }
        }

        public override void OnDespawnServer(NetworkConnection connection)
        {
            base.OnDespawnServer(connection);

            if (GameManager.isInstance)
            {
                GameManager.Instance.GetSystem<DisasterSystem>().UnRegisterDisaster(_id.Value, this);
                GameManager.Instance.GetSystem<MapSystem>().MapController.UnRegisterView(_id.Value, this);
            }
        }


        public override void OnUpdate(float deltaTime)
        {
            base.OnUpdate(deltaTime);
        }

        public override void OnFixedUpdate(float deltaTime)
        {
            base.OnFixedUpdate(deltaTime);
        }
        public override void OnLateUpdate(float deltaTime)
        {
            base.OnLateUpdate(deltaTime);
        }

        public override void UpdateView(GameEvent data)
        {
            Debug.Log($"재난 계단 비활성화!");
            this.gameObject.SetActive(false);
        }
    }
}
