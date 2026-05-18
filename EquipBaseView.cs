using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;


namespace TRAINEE
{
    // 장비 View Base
    public class EquipBaseView : NetworkBehaviour, IInteractableView
    {
        [SerializeField] protected Rigidbody _rb;
        [SerializeField] protected Collider _col;
        [SerializeField] protected Transform _left_Hand;
        [SerializeField] protected Transform _left_Hint_Hand;
        [SerializeField] protected EquipAnimEventBase _equipAnimEvent;


        protected ItemBase _item;
        protected readonly SyncVar<string> _itemid = new SyncVar<string>();
        protected readonly SyncVar<string> _instanceId = new SyncVar<string>();
        protected readonly SyncVar<string> _ownerId = new SyncVar<string>();

        protected readonly SyncVar<bool> _isDrop = new SyncVar<bool>();

        public string ItemID { get => _itemid.Value;  set => _itemid.Value = value; }
        public string InstanceID { get => _instanceId.Value;  set => _instanceId.Value = value; }
        public string OwnerID { get => _ownerId.Value;  set => _ownerId.Value = value; }
        public virtual Transform LeftHand => _left_Hand;
        public virtual Transform LeftHandHint => _left_Hint_Hand;

        public EquipAnimEventBase EquipAnimEvent { get => _equipAnimEvent; set => _equipAnimEvent = value; }
        public GameModelEventBase Param
        {
            get
            {
                if (_isDrop.Value)
                {
                    EquipEventData gameModelEventBase = new EquipEventData();
                    gameModelEventBase._id = _itemid.Value;
                    gameModelEventBase._instanceId = _instanceId.Value;
                    gameModelEventBase._isDrop = _isDrop.Value;
                    return gameModelEventBase;
                }

                return null;
            }
        }

        public Vector3 Anchor
        {
            get
            {
                return (this.transform.position);
            }
        }

        public virtual void Init(string itemid, string instanceid, string ownerId)
        {
            _itemid.Value = itemid;
            _instanceId.Value = instanceid;
            _ownerId.Value = ownerId;


            _isDrop.Value = false;
            SetPhysics(_isDrop.Value);

            OnRegisterEvent();
        }

        public virtual void OnRegisterEvent()
        {
            _ownerId.OnChange += OnOwnerChanged;
            _isDrop.OnChange += OnDropChanged;
        }

        public virtual void OnUnRegisterEvent()
        {
            _ownerId.OnChange -= OnOwnerChanged;
            _isDrop.OnChange -= OnDropChanged;
        }

        public virtual void SetupTransform(Vector3 position , Quaternion rotation)
        {

        }

        private void OnOwnerChanged(string prevOwner, string nextOwner, bool asServer)
        {
            if (asServer)
            {
                Debug.Log($"기존 소유주 : {prevOwner} / 새로운 소유주 : {nextOwner}");

                if (string.IsNullOrEmpty(nextOwner))
                {
                    ToDropState();
                }
                else
                {
                    ToEquipState();
                }
            }
        }

        private void OnDropChanged(bool prev, bool next, bool asServer)
        {

            if(next)
            {
                SetLayer(LayerMask.NameToLayer("Item"));
            }
            else
            {

            }

            SetPhysics(next);
        }

        [Server]
        public virtual void ToDropState()
        {
            _isDrop.Value = true;
        }

        [Server]
        public virtual void ToEquipState()
        {
            _isDrop.Value = false;
        }

        protected void SetPhysics(bool isActive)
        {
            if (_rb != null)
            {
                if (base.IsServerStarted)
                {
                    // 서버만 물리연산
                    _rb.isKinematic = !isActive;
                    _rb.useGravity = isActive;
                }
                else
                {
                    _rb.isKinematic = true; 
                    _rb.useGravity = false;
                }
            }

            if (_col != null)
            {
                _col.enabled = isActive;
                _col.isTrigger = !isActive;
            }
        }

        // 레이어 변경 헬퍼 (자식까지 싹 다 바꿈)
        public virtual void SetLayer(int layer)
        {
            gameObject.layer = layer;
            foreach (Transform child in transform)
            {
                child.gameObject.layer = layer;
            }
        }
    }
}
