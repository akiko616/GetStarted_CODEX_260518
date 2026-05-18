using UnityEngine;


namespace TRAINEE
{
    public interface IUsable 
    {
        public void OnUse();
    }

    public interface IEquippable
    {
        public void OnEquip();
        public void OnUnEquip();
    }

    public interface IItem : IUsable, IEquippable
    {
        public string ID { get; }
    }



    public abstract class ItemBase :IItem
    {
        protected string _itemID;
        protected string _instanceID;               // 월드 id
        protected float _lastUseTime;
        protected Transform _owner;                 // 아이템 사용자 (Player)
        protected EInventoryType _inventoryType;
        public string ID => _itemID;               // 아이템 고유 id
        public string InstanceID { get => _instanceID; set => _instanceID = value; }
        public EInventoryType InventoryType { get => _inventoryType; }

        public ItemBase(string id,EInventoryType inventoryType)
        {
            _itemID = id;
            _inventoryType = inventoryType;
        }

        public void SetOwner(Transform owner)
        {
            _owner = owner;
        }

        public virtual bool CanUse()
        {
            return Time.time >= _lastUseTime;
        }

        public virtual void OnEquip()
        {
        }

        public virtual void OnUnEquip()
        {
        }

        public virtual void OnUse()
        {
        }
    }

}
