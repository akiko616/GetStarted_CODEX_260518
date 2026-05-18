using UnityEngine;

namespace TRAINEE
{
    public class EquipRadio : EquipBase
    {
        public EquipRadio(string id, EInventoryType inventoryType) : base(id, inventoryType)
        {
        }

        public override void Init()
        {
            base.Init();
        }

        public override void OnEquip()
        {
            base.OnEquip();
        }

        public override void OnUnEquip()
        {
            base.OnUnEquip();
        }

        public override void OnUse()
        {
            base.OnUse();

            if (_owner == null) return;
        }
    }
}
