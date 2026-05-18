using System;
using System.Collections.Generic;
using UnityEngine;

namespace TRAINEE
{
    [Serializable]
    public struct ItemPrefabMapping
    {
        public string _equipid;      // 엑셀의 ID와 일치해야 함 (예: "100004")
        public GameObject _prefab;   // 연결할 프리팹

        public EActionPhase[] _eventAction;
        public string _eventFunctionName;
        public float _hitDelayTime;

        public bool _hasOwnAnim;
        public bool _isHoldAction;

        public float _actionDist;
        public bool _useSpineIk;
        public float _ikWeight;
    }

    [CreateAssetMenu(fileName = "EquipPrefabCatalog", menuName = "Scriptable Object Aesset/EquipPrefabCatalog")]
    public class ItemDataSO : ScriptableObject
    {
        [Header("Editor Setting")]
        public List<ItemPrefabMapping> _prefabList;

        // 런타임 캐싱용 딕셔너리 (검색 속도 최적화)
        private Dictionary<string, ItemPrefabMapping> _mappingDict = new Dictionary<string, ItemPrefabMapping>();

        // 게임 시작 시 ItemManager가 한 번 호출해 줍니다.
        public void Init()
        {
            _mappingDict.Clear();
            foreach (var mapping in _prefabList)
            {
                if (!_mappingDict.ContainsKey(mapping._equipid))
                {
                    _mappingDict.Add(mapping._equipid, mapping);
                }
                else
                {
                    Debug.LogWarning($"[EquipPrefabCatalog] 중복된 장비 ID가 있습니다: {mapping._equipid}");
                }
            }
        }
        public ItemPrefabMapping? GetItemMapping(string equipid)
        {
            if (_mappingDict.TryGetValue(equipid, out ItemPrefabMapping mapping))
            {
                return mapping;
            }
            return null; 
        }
        public GameObject GetPrefab(string equipid)
        {
            if (_mappingDict.TryGetValue(equipid, out ItemPrefabMapping mapping))
            {
                return mapping._prefab;
            }

            return null;
        }
    }
}
