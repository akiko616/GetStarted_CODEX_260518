using System.Collections.Generic;
using UnityEngine;

namespace TRAINEE
{
    public class EquipHandler : IHandler
    {
        private ControllerBase _controller;
        private Transform _handAnchor = null;
        private Transform _chestAnchor = null;

        private Inventory _inventory = null;
        private Items _currentEquip = null;

        private Dictionary<EInventoryType, Items> _equippedVisuals = new Dictionary<EInventoryType, Items>();

        private EInventoryType _activeHandSlot = EInventoryType.None;

        public string CurrentActiveEquipId
        {
            get { return _inventory.GetItemByID(_activeHandSlot); }
        }

        public EquipBaseView CurrentEquip
        {
            get
            {
                if (_equippedVisuals.TryGetValue(_activeHandSlot, out Items item))
                    return item.View;
                return null;
            }
        }

        public EquipAnimEventBase CurrentAnimEvent
        {
            get
            {
                if (_equippedVisuals.TryGetValue(_activeHandSlot, out Items item) && item.View != null)
                {
                    return item.View.EquipAnimEvent;
                }
                return null;
            }
        }

        public EInventoryType CurrentActiveSlot => _activeHandSlot;

        public EquipHandler() { }

        // 초기화 (PlayerController에서 호출)
        public void Init(ControllerBase controller)
        {
            _controller = controller;
            _handAnchor = controller.MainHand;

            _chestAnchor = controller.Chest;
            _inventory = new Inventory();

            _currentEquip = null;

            _equippedVisuals.Clear();

            _activeHandSlot = EInventoryType.Main;
        }

        public void OnUpdate(float deltaTime)
        {
            // 필요하다면 쿨타임 UI 갱신 등의 로직 추가
        }

        public void OnFixedUpdate(float deltaTime)
        {
            // 필요하다면 물리 관련 로직 추가
        }

        public void SetupEquip(string itemid, string instanceid = "")
        {
            if (string.IsNullOrEmpty(itemid))
            {
                Debug.LogError($"아이템 id가 없습니다.");
                return;
            }
            Items newitem = ItemManager.Instance.GetItem(itemid, instanceid, _handAnchor);

            if (newitem != null)
            {
                EInventoryType invenType = newitem.Item.InventoryType;

                // 2. 인벤토리에 등록
                _inventory.SetItem(newitem.Item);

                // 3. 메인 무기라면 바로 손에 쥐여줌
                EquipVisual(newitem, invenType);
            }
        }


        private void EquipVisual(Items item, EInventoryType slot)
        {
            if (item == null) return;

            if (_equippedVisuals.TryGetValue(slot, out Items oldItem))
            {
                oldItem.Item.OnUnEquip();
                _equippedVisuals.Remove(slot);

                ItemManager.Instance.DropItemToWorld(oldItem);

                Debug.Log($"기존 장비 {oldItem.Item.ID}를 밀어내고 드랍했습니다.");
            }

            // 새 아이템 등록 및 초기화
            _equippedVisuals[slot] = item;
            item.Item.OnEquip();
            item.Item.SetOwner(_controller.transform);
            item.View.SetLayer(LayerMask.NameToLayer("Player"));

            // 🌟 3. 슬롯 타입에 따른 라우팅(Routing) 핵심 로직
            // (기획에 맞게 EInventoryType enum 이름은 적절히 수정하세요)
            if (slot == EInventoryType.Main || slot == EInventoryType.Thrid)
            {
                // 손에 드는 무기일 경우
                item.View.transform.SetParent(_handAnchor, false);

                // 지금 막 세팅된 무기가 Main이라면 즉시 활성화, Sub(보조무기)로 들어왔다면 일단 안 보이게 숨김
                if (slot == EInventoryType.Main)
                {
                    item.View.gameObject.SetActive(true);
                    _activeHandSlot = slot;
                }
                else
                {
                    item.View.gameObject.SetActive(false); // 보조무기는 스왑하기 전까지 안 보임
                }
            }
            else // Radio, Accessory 등 몸에 붙는 장비일 경우
            {
                // 무조건 활성화하고 가슴에 붙임 (손 무기와 스왑 연관 없음!)
                item.View.transform.SetParent(_chestAnchor, false);
                item.View.gameObject.SetActive(true);
            }

            item.View.transform.localPosition = Vector3.zero;
            item.View.transform.localRotation = Quaternion.identity;

            Debug.Log($"장비 시각화 완료: 부위 - {slot} / id - {item.Item.ID}");
        }

        public void DropEquip(EInventoryType slot)
        {
            if (_equippedVisuals.TryGetValue(slot, out Items dropItem))
            {
                // 1. 아이템 논리적 사용 해제
                dropItem.Item.OnUnEquip();

                // 2. 장부에서 완벽하게 제거
                _equippedVisuals.Remove(slot);
                // _inventory.RemoveItem(slot); // (Inventory 클래스에 Remove 기능이 있다면 호출하여 데이터도 비워줍니다)

                // 3. ItemManager에게 월드로 반납하라고 지시!
                ItemManager.Instance.DropItemToWorld(dropItem);

                // 4. 만약 방금 버린 게 현재 손에 들고 있던 거라면, 활성화 슬롯 초기화
                if (_activeHandSlot == slot)
                {
                    _activeHandSlot = EInventoryType.None;
                }

                Debug.Log($"[EquipHandler] {slot} 슬롯의 장비({dropItem.Item.ID})를 월드로 드랍했습니다.");
            }
        }
        public bool ExecuteSwap(int slotIndex)
        {
            EInventoryType targetSlot = (EInventoryType)slotIndex;
            ItemBase slotItem = _inventory.GetItem(targetSlot);

            Debug.Log($"인벤토리에서 장비 교체 : 현재 슬롯  - {_activeHandSlot.ToString()} " +
                $"교체 슬롯 - {targetSlot.ToString()} / slotItem - {slotItem}");

            if (targetSlot == _activeHandSlot)
            {
                return false;
            }

            if (slotItem != null)
            {
                if (_equippedVisuals.TryGetValue(_activeHandSlot, out Items prevItem))
                {
                    if (_activeHandSlot != EInventoryType.Sub)
                    {
                        prevItem.View.gameObject.SetActive(false);
                    }
                }

                // 2. 바꿀 무기를 손에 쥐고 보이게 켬
                if (_equippedVisuals.TryGetValue(targetSlot, out Items nextItem))
                {
                    Debug.Log($"인벤토리에서 장비 교체 완료: 부위 - {targetSlot.ToString()} / _activeHandSlot - {_activeHandSlot}");
                    nextItem.View.gameObject.SetActive(true);

                }
                
                _activeHandSlot = targetSlot;

                return true;
            }

            return false;
        }

        public ItemBase GetExecuteSwapItem(int slotIndex)
        {
            EInventoryType targetSlot = (EInventoryType)slotIndex;
            return _inventory.GetItem(targetSlot);

        }
    }
}
