using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace TRAINEE
{
    public abstract class DisasterViewBase : NetworkBehaviour, IMapView, IInteractableView
    {
        [SerializeField] protected Transform _anchor = null;
        protected readonly SyncVar<string> _id = new SyncVar<string>();

        public GameModelEventBase Param
        {
            get
            {
                DisasterEventData gameModelEventBase = new DisasterEventData();
                gameModelEventBase._id = _id.Value;
                return gameModelEventBase;
            }
        }

        public bool CheckEquip(string id)
        {

            return true;
        }

        public Vector3 Anchor => _anchor.position;
        public Transform GetTrans => _anchor;
        public virtual void Init(string id)
        {
            _id.Value = id;
        }

        public virtual void Setup()
        {

        }

        public virtual void OnUpdate(float deltaTime)
        {
        }

        public virtual void OnFixedUpdate(float deltaTime)
        {

        }

        public virtual void OnLateUpdate(float deltaTime)
        {

        }

        public virtual void UpdateView(GameEvent data)
        {
        }
    }
}
