using TRAINEE;
using UnityEngine;

public class LocalEquip : EquipBase
{
    ELobbyItemType type; 
    public LocalEquip(string id, EInventoryType inventoryType) : base(id, inventoryType)
    {
    }
    public override void Init()
    {
        base.Init();
    }
}
