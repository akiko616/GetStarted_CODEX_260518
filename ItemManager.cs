using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


namespace TRAINEE
{
    public class Items
    {
        public ItemBase Item { get; private set; }
        public EquipBaseView View { get; private set; }
        public LocalEquipBaseView DisplayView { get; private set; }

        public Items(ItemBase item, EquipBaseView view)
        {
            Item = item;
            View = view;
        }
        public Items(ItemBase item, LocalEquipBaseView view)
        {
            Item = item;
            DisplayView = view;
        }
    }

    public class ItemManager : Singleton<ItemManager>, IEvent
    {
        [SerializeField] private ItemDataSO _prefabCatalog = null;

        // Item Cached Data
        private Dictionary<string, ItemData> _cachedItem = null;

        private Dictionary<string, List<Items>> _poolItems = new Dictionary<string, List<Items>>();
        private Dictionary<string, Dictionary<string, Items>> _worldItems = new Dictionary<string, Dictionary<string, Items>>();
        private List<Items> _lobbyItems = new List<Items>();

        private Transform _worldItemContainer = null;
        private Transform _poolContainer = null;
        private Transform _lobbyItemContainer = null;

        protected override void Awake()
        {
            base.Awake();

            Init();
        }

        protected override void Start()
        {
            base.Start();
        }

        protected override void Init()
        {
            base.Init();


            if (_cachedItem == null)
            {
                _cachedItem = new Dictionary<string, ItemData>();
            }
            else
            {
                _cachedItem.Clear();
            }

            if (_worldItemContainer == null)
            {
                GameObject go = new GameObject("WorldItemContainer");
                go.transform.SetParent(this.transform);
                _worldItemContainer = go.transform;
            }

            if (_poolContainer == null)
            {
                GameObject go = new GameObject("PoolContainer");
                go.transform.SetParent(this.transform);
                _poolContainer = go.transform;
            }


            _prefabCatalog.Init();
            LoadingHelper.RegisterLoading(new Loading(LoadingItem));
        }

        private Items FindItem(string id, List<Items> data)
        {
            for (int i = 0; i < data.Count; i++)
            {
                if (data[i].Item.InstanceID.Equals(id))
                {
                    return data[i];
                }
            }

            return null;
        }

        public int GetItemSlotIndex(string itemId)
        {
            if (_cachedItem != null && _cachedItem.TryGetValue(itemId, out ItemData data))
            {
                // Enum인 EInventoryType을 int로 캐스팅하여 반환합니다. 
                // (예: Main = 0, Sub = 1 등 설정하신 Enum 순서대로 반환됨)
                return (int)data._slot;
            }

            Debug.LogWarning($"[ItemManager] {itemId} 아이템의 슬롯 정보를 찾을 수 없어 기본 슬롯(0)을 반환합니다.");
            return 0;
        }

        public bool IsItem(string id, string instanceid)
        {
            if (_poolItems.TryGetValue(id, out var item))
            {
                if (!string.IsNullOrEmpty(instanceid))
                {
                    Items foundItem = FindItem(instanceid, item);

                    if (foundItem != null)
                    {
                        return true;
                    }
                }
            }


            if (_worldItems.TryGetValue(id, out var worldItem))
            {
                if (!string.IsNullOrEmpty(instanceid))
                {
                    if (worldItem.TryGetValue(instanceid, out var Item))
                    {
                        return true;
                    }
                }
            }


            return false;
        }


        // pool에 생성 아이템이 존재하는지 확인 후 전달
        public Items GetItem(string itemid, string instanceid = "", Transform parent = null)
        {
            Debug.Log($"[아이템 등록 찾기] {itemid} (고유번호: {instanceid})");

            if (_poolItems.TryGetValue(itemid, out var item))
            {
                if (!string.IsNullOrEmpty(instanceid))
                {
                    Items foundItem = FindItem(instanceid, item);

                    if (foundItem != null)
                    {
                        item.Remove(foundItem);
                        foundItem.View.gameObject.SetActive(true);

                        if (parent != null)
                        {
                            foundItem.View.transform.SetParent(parent, false);
                        }

                        return foundItem;
                    }
                }
                else
                {
                    if (item.Count > 0)
                    {
                        Items newItem = item.First();

                        item.Remove(newItem);
                        newItem.View.gameObject.SetActive(true);

                        if (parent != null)
                        {
                            newItem.View.transform.SetParent(parent, false);
                        }

                        return newItem;
                    }
                }

            }

            if (!string.IsNullOrEmpty(instanceid) && _worldItems.TryGetValue(itemid, out var worldDict))
            {
                if (worldDict.TryGetValue(instanceid, out Items worldItem))
                {
                    // 월드 명부에서 꺼냅니다. (바닥에서 주움)
                    worldDict.Remove(instanceid);

                    // 불을 켜주고 손(parent)에 쥐여줍니다!
                    worldItem.View.gameObject.SetActive(true);
                    if (parent != null)
                        worldItem.View.transform.SetParent(parent, false);

                    // Debug.Log($"[클라이언트] 월드에 있던 진짜 네트워크 아이템({itemid})을 손에 쥐었습니다!");
                    return worldItem;
                }
            }


            Debug.LogError($"[치명적 동기화 에러] 서버가 장착을 명령한 아이템({itemid} / {instanceid})이 클라이언트의 풀/월드에 존재하지 않습니다!");
            return null;
        }

        private Items CreateItem(string itemid, Transform parent = null)
        {
            if (!_cachedItem.TryGetValue(itemid, out ItemData data))
            {
                Debug.LogError($"[ItemManager] ID를 찾을 수 없습니다: {itemid}");
                return null;
            }

            ItemBase itembase = CreateItemData(itemid, "");

            if (itembase == null)
            {
                //Debug.LogError($"아이템 생성 시 장비 타입 문제 : {data.Id}");
                return null;
            }


            EquipBaseView view = null;

            if (data._prefab != null)
            {
                GameObject prefab = Instantiate(data._prefab);
                view = prefab.GetComponent<EquipBaseView>();
                view.Init(itembase.ID, itembase.InstanceID, "");
                view.gameObject.SetActive(false);
                if (parent != null)
                {
                    view.transform.SetParent(parent, false);

                }
            }

            if (view == null)
            {
                //Debug.LogError($"아이템 생성 시 prefab 문제 : {data.Id}");
                return null;
            }

            return new Items(itembase, view);
        }

        private void SetPoolItem(string id, int poolCnt = 1)
        {
            for (int i = 0; i < poolCnt; i++)
            {
                Items items = CreateItem(id, _poolContainer);

                if (items != null)
                {
                    if (!_poolItems.ContainsKey(id))
                    {
                        _poolItems.Add(id, new List<Items>());
                    }

                    _poolItems[id].Add(items);
                }
            }
        }




        public void OnEvent(GameEvent data)
        {
            //switch (data._type)
            //{
            //    case EEventType.Interaction:
            //        if (data._param is EquipEventData)
            //        {
            //            EquipEventData equipData = data._param as EquipEventData;
            //            OnPickUpWorldItem(equipData);
            //        }
            //        break;
            //}
        }

        public void ItemDropToWorld(Items items)
        {
            if (items == null) return;

            string id = items.Item.ID;
            string instanceid = items.Item.InstanceID;

            items.View.transform.SetParent(_worldItemContainer);

            items.View.gameObject.SetActive(true);
            items.View.ToDropState();

            if (!_worldItems.ContainsKey(id))
            {
                _worldItems.Add(id, new Dictionary<string, Items>());
            }

            if (!_worldItems[id].ContainsKey(instanceid))
            {
                _worldItems[id].Add(instanceid, items);
            }
        }

        public void ItemRetrunPool(Items items)
        {
            if (items == null) return;

            string id = items.Item.ID;
            string instanceid = items.Item.InstanceID;

            items.View.transform.SetParent(_poolContainer);

            items.View.gameObject.SetActive(false);

            if (!_poolItems.ContainsKey(id))
            {
                _poolItems[id] = new List<Items>();
            }

            _poolItems[id].Add(items);
        }

        public ItemPrefabMapping? GetItemData(string itemid)
        {
            if (string.IsNullOrEmpty(itemid))
            {
                return null;
            }

            ItemPrefabMapping? data = _prefabCatalog.GetItemMapping(itemid);

            if(data == null)
            {
                Debug.Log($"아이템 데이터를 찾을 수 없습니다 : {itemid}");
                return null;
            }

            return data;
        }


        private ItemBase CreateItemData(string itemid, string instanceid)
        {
            ItemBase itembase = null;

            if (!_cachedItem.TryGetValue(itemid, out ItemData data))
            {
                return itembase;
            }

            string finalInstanceId = string.IsNullOrEmpty(instanceid) ? $"{data.Id}_{Guid.NewGuid()}" : instanceid;

            switch (data._equipType)
            {
                case EEquipType.Melee:
                    {
                        EquipMelee newitem = new EquipMelee(data.Id, data._slot);
                        newitem.InstanceID = finalInstanceId;

                        itembase = newitem;
                    }
                    break;
                case EEquipType.Radio:
                    {
                        EquipMelee newitem = new EquipMelee(data.Id, data._slot);
                        newitem.InstanceID = finalInstanceId;

                        itembase = newitem;
                    }
                    break;
            }


            return itembase;
        }

        private EquipBaseView CreateItemView(string itemid, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            EquipBaseView view = null;
            if (!_cachedItem.TryGetValue(itemid, out ItemData data))
            {
                return view;
            }

            GameObject prefab = Instantiate(data._prefab, position, rotation);
            view = prefab.GetComponent<EquipBaseView>();

            if (parent != null)
            {
                view.transform.SetParent(parent, false);

            }

            return view;
        }


        /// <summary>
        /// 서버 환경일 경우 스폰
        /// </summary>
        /// <param name="id"></param>
        /// <param name="position"></param>
        /// <param name="rotation"></param>
#if GAMEINSTANCE
        public void OnDropWorldItems()
        {
            foreach (var itemDict in _worldItems.Values)
            {
                foreach (var item in itemDict.Values)
                {
                    if (item != null && item.View != null)
                    {
                        // 앞서 수정했던 서버 전용 ToDropState() 호출
                        item.View.ToDropState();
                    }
                }
            }
            Debug.Log("[ItemManager] 훈련 시작! 모든 아이템 중력 활성화 완료.");
        }

        public bool OnPickUpItem(string itemId, string instanceId, string playerId)
        {
            Debug.Log($"아이템 집기 시작");

            if (_worldItems.TryGetValue(itemId, out var dict))
            {
                if (dict.TryGetValue(instanceId, out var item))
                {
                    // 2. 월드 딕셔너리에서 삭제 (다른 사람이 못 줍게 뺌)
                    dict.Remove(instanceId);

                    // 3. 소유권(OwnerID) 부여
                    item.View.OwnerID = playerId;

                    // 4. 아이템 뷰의 물리 상태 초기화 및 비활성화 (장착 전 대기 상태)
                    item.View.transform.position = Vector3.zero;
                    item.View.transform.rotation = Quaternion.identity;
                    item.View.gameObject.SetActive(false);

                    // 5. 풀 컨테이너로 이동 후 보관
                    item.View.transform.SetParent(_poolContainer, false);
                    if (!_poolItems.ContainsKey(itemId))
                    {                        
                        _poolItems.Add(itemId, new List<Items>());
                    }

                    _poolItems[itemId].Add(item);

                    Debug.Log($"[서버] {playerId} 플레이어가 {itemId} 줍기 성공!");
                    return true;
                }
            }
            Debug.LogWarning($"[서버] {playerId}가 이미 다른 사람이 주운 아이템을 주우려 했습니다.");
            return false;

        }


#endif

        public void ServerSpawnItem(string itemid, string instanceid, Vector3 position, Quaternion rotation, string ownerid = "", bool isWorld = true)
        {
            if (!_cachedItem.TryGetValue(itemid, out ItemData data))
            {
                return;
            }

            // world or pool 선택
            Transform parent = isWorld == true ? _worldItemContainer : _poolContainer;

            // 아이템 생성
            ItemBase itemData = CreateItemData(itemid, instanceid);
            EquipBaseView view = CreateItemView(itemid, position, rotation, parent);

            // 아이템 초기화
            view.Init(itemid, itemData.InstanceID, ownerid);

            Items item = new Items(itemData, view);
            Debug.Log($"[월드 아이템 등록 등록] {itemid} (고유번호: {itemData.InstanceID}) 월드 : {_worldItemContainer}");


            if (isWorld)
            {
                // 월드 컨테이너에 보관

                if (!_worldItems.ContainsKey(itemid))
                {
                    _worldItems.Add(itemid, new Dictionary<string, Items>());
                }

                if (!_worldItems[itemid].ContainsKey(itemData.InstanceID))
                {
                    _worldItems[itemid].Add(itemData.InstanceID, item);

                    Debug.Log($"[월드 아이템 등록 등록 완료] {itemid} (고유번호: {itemData.InstanceID})");
                }
                else
                {
                    Debug.Log($"[ItemManager] 이미 존재하는 인스턴스 ID 입니다: {itemData.InstanceID}");
                }

                Debug.Log($"[서버] {itemid}가 월드에 생성 되었습니다.");
            }
            else
            {
                if (!_poolItems.ContainsKey(itemid))
                {
                    _poolItems.Add(itemid, new List<Items>());
                }

                _poolItems[itemid].Add(item);

                Debug.Log($"[서버] {itemid}가 보관함에 생성 되었습니다.");
            }

            FishNet.InstanceFinder.ServerManager.Spawn(view.gameObject);
        }
        public void DropItemToWorld(Items dropItem)
        {
            if (dropItem == null || dropItem.View == null) return;

            EquipBaseView view = dropItem.View;
            string itemId = dropItem.Item.ID;
            string instanceId = dropItem.Item.InstanceID;

            if (_worldItemContainer != null)
                view.transform.SetParent(_worldItemContainer);
            else
                view.transform.SetParent(null);

            view.gameObject.SetActive(true);

            if (!_worldItems.ContainsKey(itemId))
            {
                _worldItems.Add(itemId, new Dictionary<string, Items>());
            }

            if (!_worldItems[itemId].ContainsKey(instanceId))
            {
                _worldItems[itemId].Add(instanceId, dropItem);
                Debug.Log($"[ItemManager] 아이템 월드 장부 재등록 완료: {itemId} / {instanceId}");
            }

            view.OwnerID = "";
            view.ToDropState();
        }

        //public Items ServerSpawnItemForPlayer(string itemid, string instanceid, FishNet.Connection.NetworkConnection ownerConn)
        //{
        //    if (!_cachedItem.TryGetValue(itemid, out ItemData data)) return null;

        //    // 1. 데이터 및 뷰 생성 (아직 네트워크엔 안 올라감)
        //    ItemBase itemData = CreateItemData(itemid, instanceid);
        //    if (itemData != null) itemData.InstanceID = instanceid;

        //    EquipBaseView view = CreateItemView(itemid, Vector3.zero, Quaternion.identity);

        //    string ownerIdString = ownerConn != null ? ownerConn.ClientId.ToString() : "";

        //    view.Init(itemid, instanceid, ownerIdString);

        //    Items item = new Items(itemData, view);

        //    view.transform.SetParent(_poolContainer);
        //    view.gameObject.SetActive(false); 

        //    if (!_poolItems.ContainsKey(itemid))
        //    {
        //        _poolItems.Add(itemid, new List<Items>());
        //    }
        //    _poolItems[itemid].Add(item);

        //    Debug.Log($"[ItemManager] 서버가 {itemid} (ID:{instanceid})를 스폰하여 지급 준비를 마쳤습니다.");

        //    FishNet.InstanceFinder.ServerManager.Spawn(view.gameObject, ownerConn);
        //    return item;
        //}

        public void RegisterNetworkItemToClient(FishNet.Object.NetworkObject nob, string itemId, string instanceId)
        {
            if (nob == null) return;

            EquipBaseView view = nob.GetComponent<EquipBaseView>();
            if (view == null) return;


            if (!_cachedItem.TryGetValue(itemId, out ItemData cachedData)) return;

            ItemBase itemData = CreateItemData(itemId, instanceId);
            itemData.InstanceID = instanceId;

            // 3. View와 Model을 결합
            Items newItem = new Items(itemData, view);

            // 🌟 4. 로컬 풀(장부)에 정식으로 입고 처리!
            if (!_poolItems.ContainsKey(itemId))
            {
                _poolItems.Add(itemId, new List<Items>());
            }
            _poolItems[itemId].Add(newItem);

            // 5. 물리적 위치 정리
            view.transform.SetParent(_poolContainer);

            Debug.Log($"[ItemManager] 클라이언트가 서버 스폰 아이템을 장부에 성공적으로 등록했습니다: {itemId} / {instanceId}");
        }

        /// <summary>
        /// 로비 환경일 경우 스폰
        /// </summary>
        /// <param name="id"></param>
        /// <param name="position"></param>
        /// <param name="rotation"></param>
        public void LocalSpawnWorldItem(string id, Vector3 position, Quaternion rotation)
        {
            if (!_cachedItem.TryGetValue(id, out ItemData data))
            {
                return;
            }

            ItemBase itemData = CreateLobbyItemData(id, "");
            LocalEquipBaseView view = CreateLobbyItemView(id, position, rotation, _lobbyItemContainer);

            if (view == null)
            {
                Debug.Log($"아이템 ID : {id}는 아이템카탈로그에 프리팹이 등록되지 않았음");
                return;
            }

            //비디오, 텍스트 구분 조건이 정해지기 전까지 임시로 아이템ID의 100 단위로 구분하겠습니다

            int hundredsDigit = (Mathf.Abs(int.Parse(id)) / 100) % 10;
            ELobbyItemType lobbyItemType = (ELobbyItemType)hundredsDigit;

            view.Init(id, itemData.InstanceID, lobbyItemType);


            Items item = new Items(itemData, view);

            _lobbyItems.Add(item);
        }
        private ItemBase CreateLobbyItemData(string itemid, string instanceid)
        {
            ItemBase itembase = null;

            //캐시된 아이템 아니면 널 리턴;
            if (!_cachedItem.TryGetValue(itemid, out ItemData data))
                return itembase;

            //전시용 아이템이 아니면 널 리턴
            if (data._itemType != EItemType.Display) return itembase;

            string finalInstanceId = string.IsNullOrEmpty(instanceid) ? $"{data.Id}_{Guid.NewGuid()}" : instanceid;

            LocalEquip newitem = new LocalEquip(data.Id, data._slot);
            newitem.InstanceID = finalInstanceId;

            itembase = newitem;

            return itembase;
        }
        private LocalEquipBaseView CreateLobbyItemView(string itemid, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            LocalEquipBaseView view = null;
            if (!_cachedItem.TryGetValue(itemid, out ItemData data))
            {
                return view;
            }

            if (data._prefab != null)
            {
                GameObject prefab = Instantiate(data._prefab, position, rotation);
                view = prefab.GetComponent<LocalEquipBaseView>();
            }

            if (parent != null)
            {
                view?.transform.SetParent(parent, false);

            }

            return view;
        }
        public void ClearLobbyItem()
        {
            foreach (Items item in _lobbyItems)
            {
                if (item != null)
                {
                    Destroy(item.DisplayView.gameObject);
                }
            }

            _lobbyItems.Clear();
        }
        public void SpawnEquippedItem(string id)
        {

        }

        public void OnRegisterItem(EquipBaseView view)
        {
            string itemId = view.ItemID;
            string instanceId = view.InstanceID;

            Debug.Log($"[아이템 등록 등록] {itemId} (고유번호: {instanceId})");

            if (string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(instanceId))
            {
                Debug.LogWarning("[ItemManager] 유효하지 않은 아이템이 등록을 시도했습니다.");
                return;
            }

            ItemBase itemData = CreateItemData(itemId, instanceId);

            if (itemData == null)
            {
                Debug.LogError($"[ItemManager] 도감에 없는 아이템입니다! ID: {itemId}");
                return;
            }

            Items newItem = new Items(itemData, view);

            view.transform.SetParent(_worldItemContainer);

            if (!_worldItems.ContainsKey(itemId))
            {
                _worldItems.Add(itemId, new Dictionary<string, Items>());
            }

            if (!_worldItems[itemId].ContainsKey(instanceId))
            {
                _worldItems[itemId].Add(instanceId, newItem);
                Debug.Log($"[아이템 등록 완료] {itemId} (고유번호: {instanceId}) - 월드에 배치되었습니다.");
            }
        }

        /// <summary>
        /// 씬에서 월드 정리 시 사용
        /// </summary>
        public void OnClearWorldItem()
        {

        }

        /// <summary>
        /// 게임 종료시 아이템 정리
        /// </summary>
        public void OnClear()
        {

        }
        // Item Table을 읽고 PreLoad
        public async UniTask LoadingItem(Action<float, string> onProgress, int delaytime)
        {
            if (_cachedItem == null)
            {
                _cachedItem = new Dictionary<string, ItemData>();
            }

            // 필요한 데이터
            // EquipData
            List<EquipData> data = DataManager.Instance.GetAllData<EquipData>(EDataType.EquipData);

            if (data == null)
            {
                Debug.LogError("[ItemManager] EquipData를 찾을 수 없습니다! 로딩을 중단합니다.");
                onProgress?.Invoke(1f, "아이템 데이터 로딩 실패");
                return;
            }

            // 우선조건..?
            // 로딩시 캐싱데이터뿐만 아니라
            // 미리 오프젝트 풀링으로 한개씩 생산해둔다.

            int totalCnt = data.Count;

            for (int i = 0; i < data.Count; i++)
            {
                ItemData item = new ItemData();

                item.Id = data[i].Id;
                item._itemType = data[i].ItemType;
                item._slot = data[i].InventoryType;
                item._equipType = data[i].equipType;
                item._isOneHand = data[i].isOneHand;
                item._aniId = data[i].aniid;
                item._eduequipId = data[i].eduequipid;
                item._iconId = data[i].iconid;

                item._prefab = _prefabCatalog.GetPrefab(data[i].Id);


                if (!_cachedItem.ContainsKey(item.Id))
                {
                    _cachedItem.Add(item.Id, item);
                }

                //SetPoolItem(item.Id);

                await UniTask.Delay(delaytime);
                float totalprogress = (i + 1) / (float)totalCnt;

                onProgress?.Invoke(totalprogress, $"Item 데이터 로드 중...");
            }
        }
    }
}
