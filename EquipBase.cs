using UnityEngine;



namespace TRAINEE
{
    // 장비 기본 베이스
    public class EquipBase : ItemBase
    {
        public EquipBase(string  id, EInventoryType inventoryType) : base(id,inventoryType)
        {
        }

        public virtual void Init()
        {
        }
    }
}
