using Cysharp.Threading.Tasks;
using TRAINEE;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DrillSergeant
{
    public class MonitoringPoolSystem
    {
        private static MonitoringPoolSystem _instance;
        public static MonitoringPoolSystem Instance => _instance ??= new MonitoringPoolSystem();

        // 오브젝트풀링 (빌딩, 터레인)
        private Dictionary<(string id, ERoomState state), GameObject> _elementPool = new Dictionary<(string id, ERoomState state), GameObject>();
        private Dictionary<(EBuildingType, ETerrainType), GameObject> _terrainPool = new Dictionary<(EBuildingType, ETerrainType), GameObject>();

        // 각 ID별 가능한 state 모음
        private Dictionary<string, List<ERoomState>> _availableStates = new Dictionary<string, List<ERoomState>>();

        private HashSet<EBuildingType> _processedBuildings = new HashSet<EBuildingType>();

        // layer("Minimap") 지정 안할 오브젝트의 displayName 번호
        private static readonly HashSet<string> EXCLUDED_DISPLAY_NAMES = new HashSet<string>()
        {
            "1000" // 천장
        };

        private Transform _poolRoot;

        private void EnsurePoolRoot()
        {
            if (_poolRoot == null)
            {
                _poolRoot = new GameObject("[Global_Monitoring_Pool]").transform;
                UnityEngine.Object.DontDestroyOnLoad(_poolRoot.gameObject);
            }
        }

        public async UniTask WarmUpPool(Action<float> onProgress)
        {
            EnsurePoolRoot();

            await TRAINEE.DataManager.Instance.OnLocalDataLoadingAsync((p, s) => onProgress?.Invoke(p * 0.1f), 10);
            LoadingHelper.LoadingAsync(null, 0).Forget();


            List<ScenarioData> scenarios = TRAINEE.DataManager.Instance.GetAllData<ScenarioData>(EDataType.ScenarioData);

            _elementPool.Clear();
            _terrainPool.Clear();
            _availableStates.Clear();
            _processedBuildings.Clear();

            List<(RoomElementData data, string pathPrefix, EBuildingType buildingType)> elementTargets = new List<(RoomElementData, string, EBuildingType)>();
            foreach (ScenarioData scenario in scenarios)
            {
                EBuildingType buildingType = scenario.buildingType;
                ETerrainType terrainType = scenario.terrainType;

                string buildingName = buildingType.ToString();
                string buildingPath = $"{Const.Path.BUILT_IN_MAP_PATH}{buildingName}/";

                // 터레인 풀링 (중복 체크)
                if (!_terrainPool.ContainsKey((buildingType, terrainType)))
                {
                    PreloadTerrain(buildingType, terrainType, buildingPath);
                }

                // 빌딩 엘리먼트 풀링 (중복 체크)
                if (!_processedBuildings.Contains(buildingType))
                {
                    CollectBuildingElements(buildingType, buildingPath, elementTargets);
                    _processedBuildings.Add(buildingType);
                }
            }

            int totalCount = elementTargets.Count;
            ERoomState[] allStates = (ERoomState[])Enum.GetValues(typeof(ERoomState));
            int totalOperations = totalCount * allStates.Length;
            int currentOp = 0;

            int layerIndex = LayerMask.NameToLayer("Minimap");
            for (int i = 0; i < totalCount; i++)
            {
                (RoomElementData roomElementData, string pathPrefix, EBuildingType buildingType) = elementTargets[i];

                foreach (ERoomState state in allStates)
                {
                    if (state == ERoomState.None)
                    {
                        currentOp++;
                        continue;
                    }

                    (string id, ERoomState state) key = (roomElementData.Id, state);

                    if (_elementPool.ContainsKey(key))
                    {
                        currentOp++;
                        continue;
                    }

                    string fullPath = $"{pathPrefix}Elements/{state}/{roomElementData.element}";
                    GameObject prefab = Resources.Load<GameObject>(fullPath);

                    if (prefab != null)
                    {
                        GameObject obj = UnityEngine.Object.Instantiate(prefab, _poolRoot);

                        if (!EXCLUDED_DISPLAY_NAMES.Contains(roomElementData.displayName))
                        {
                            SetLayerRecursively(obj, layerIndex);
                        }                        

                        // Controller에서 FishNet 연결전에 view.Init 예정
                        //MapViewBase view = obj.GetComponent<MapViewBase>();
                        //if (view != null)
                        //    view.Init(roomElementData.Id);

                        obj.SetActive(false);

                        _elementPool[key] = obj;
                        AddAvailableState(roomElementData.Id, state);
                    }
                    currentOp++;
                }

                if (i % 20 == 0)
                {
                    float progress = 0.1f + ((float)currentOp / totalOperations) * 0.9f;
                    onProgress?.Invoke(progress);
                    await UniTask.Yield();
                }
            }
            onProgress?.Invoke(1.0f);
        }



        private void CollectBuildingElements(EBuildingType buildingType, string buildingPath, List<(RoomElementData, string, EBuildingType)> outList)
        {
            string buildingId = ((int)buildingType).ToString();
            BuildingData buildingData = TRAINEE.DataManager.Instance.GetData<BuildingData>(EDataType.BuildingData, buildingId);

            if (buildingData == null)
                return;

            foreach (string floorName in buildingData.floors)
            {
                string floorPath = $"{buildingPath}{floorName}/";
                FloorData floorData = TRAINEE.DataManager.Instance.GetData<FloorData>(EDataType.FloorData, floorName);
                if (floorData == null)
                    continue;

                foreach (var rKey in floorData.rooms)
                {
                    RoomData roomData = TRAINEE.DataManager.Instance.GetData<RoomData>(EDataType.RoomData, rKey);

                    if (roomData == null)
                        continue;

                    foreach (string eIement in roomData.Elements)
                    {
                        RoomElementData roomElementData = TRAINEE.DataManager.Instance.GetData<RoomElementData>(EDataType.RoomElementData, eIement);
                        if (roomElementData != null)
                        {
                            outList.Add((roomElementData, floorPath, buildingType));
                        }
                    }
                }
            }
        }

        private void PreloadTerrain(EBuildingType buildingType, ETerrainType terrainType, string buildingPath)
        {
            string terrainName = terrainType.ToString();
            string terrainPath = $"{buildingPath}{terrainName}";

            GameObject prefab = Resources.Load<GameObject>(terrainPath);

            if (prefab != null)
            {
                GameObject createdObj = UnityEngine.Object.Instantiate(prefab, _poolRoot);

                SetLayerRecursively(createdObj, LayerMask.NameToLayer("Minimap"));

                // 터레인 sprite 넣는 메서드
                //MinimapSpriteBuilder.AttachTerrainSprite(createdObj, buildingType, terrainType);

                createdObj.SetActive(false);

                if (!_terrainPool.ContainsKey((buildingType, terrainType)))
                {
                    _terrainPool.Add((buildingType, terrainType), createdObj);
                }
                else
                {
                    UnityEngine.Object.Destroy(createdObj);
                }
            }
        }

        public void SetLayerRecursively(GameObject obj, int newLayer)
        {
            if (obj == null)
                return;

            obj.layer = newLayer;

            foreach (Transform child in obj.transform)
            {
                if (child == null)
                    continue;

                SetLayerRecursively(child.gameObject, newLayer);
            }
        }

        private void AddAvailableState(string id, ERoomState state)
        {
            if (!_availableStates.ContainsKey(id))
            {
                _availableStates[id] = new List<ERoomState>();
            }

            if (!_availableStates[id].Contains(state))
            {
                _availableStates[id].Add(state);
            }
        }

        public List<ERoomState> GetAvailableStates(string id)
        {
            if (_availableStates.TryGetValue(id, out var list))
            {
                return list;
            }
            return new List<ERoomState>() { ERoomState.Normal };
        }

        public GameObject GetElement(string id, ERoomState state)
        {
            if (_elementPool.TryGetValue((id, state), out GameObject obj))
                return obj;

            return null;
        }

        public GameObject GetTerrain(EBuildingType bType, ETerrainType tType)
        {
            if (_terrainPool.TryGetValue((bType, tType), out GameObject obj))
            {
                return obj;
            }
            return null;
        }


        public void ReturnToPool(GameObject obj)
        {
            if (obj == null) return;

            obj.SetActive(false);

            if (_poolRoot != null)
                obj.transform.SetParent(_poolRoot, false);
        }

        public void ReturnAllToPool()
        {
            if (_poolRoot == null) return;

            foreach (GameObject elementObj in _elementPool.Values)
            {
                if (elementObj != null && elementObj.activeSelf)
                {
                    elementObj.SetActive(false);
                    elementObj.transform.SetParent(_poolRoot, false);
                }
            }

            foreach (GameObject terrainObj in _terrainPool.Values)
            {
                if (terrainObj != null && terrainObj.activeSelf)
                {
                    terrainObj.SetActive(false);
                    terrainObj.transform.SetParent(_poolRoot, false);
                }
            }

        }

    }
}