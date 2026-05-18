using System.Collections.Generic;
using UnityEngine;


namespace TRAINEE
{
    public class Inventory
    {
        private Dictionary<EInventoryType, ItemBase> _inventory = null;

        public Inventory() 
        {
            _inventory = new Dictionary<EInventoryType, ItemBase>();
        }

        public ItemBase GetItem(EInventoryType type)
        {
            if (_inventory.TryGetValue(type, out ItemBase item))
            {
                return item;
            }

            return null;
        }

        public string GetItemByID(EInventoryType type)
        {
            if (_inventory.TryGetValue(type, out ItemBase item))
            {
                return item.ID;
            }

            return "";
        }

        public void SetItem(ItemBase item)
        {
            if (item != null)
            {
                _inventory[item.InventoryType] = item;
            }
        }

        public void RemoveItem(EInventoryType type)
        {
            if (_inventory.ContainsKey(type))
            {
                _inventory.Remove(type);
            }
        }

        public bool HasItem(EInventoryType type)
        {
            return _inventory.ContainsKey(type);
        }

        public void Clear()
        {
            _inventory.Clear();
        }
    }
}
