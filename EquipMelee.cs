using UnityEngine;

namespace TRAINEE
{
    /// <summary>
    /// 망치,유압절단기?
    /// </summary>
    public class EquipMelee : EquipBase
    {
        public EquipMelee(string id, EInventoryType inventoryType) : base(id, inventoryType)
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
