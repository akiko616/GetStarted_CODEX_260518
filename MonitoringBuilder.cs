using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using TRAINEE;
using UnityEngine;

namespace DrillSergeant
{
    public class MonitoringBuilder : MonoBehaviour
    {
        public async UniTask LoadMap(DrillUnit unit, DrillMapController controller, System.Action<float> onProgress)
        {
            if (unit.CopiedData.trainingData == null)
            {
                onProgress?.Invoke(1f);
                return;
            }

            TrainingDatas trainingData = unit.CopiedData.trainingData;
            //if (!CacheServerElementStates(trainingData, controller))
            //{
            //    onProgress?.Invoke(1f);
            //    return;
            //}

            string scenarioId = trainingData.scenarioNum;
            ScenarioData scenarioData = TRAINEE.DataManager.Instance.GetData<ScenarioData>(EDataType.ScenarioData, scenarioId);

            if (scenarioData == null)
            {
                onProgress?.Invoke(1f);
                return;
            }

            EScenario currentScenario = EScenario.None;
            if (int.TryParse(scenarioId, out int scenarioNum))
            {
                if (Enum.IsDefined(typeof(EScenario), scenarioNum))
                    currentScenario = (EScenario)scenarioNum;
            }

            string buildingKey = ((int)scenarioData.buildingType).ToString();
            BuildingData buildingData = TRAINEE.DataManager.Instance.GetData<BuildingData>(EDataType.BuildingData, buildingKey);

            if (buildingData != null)
            {
                SpawnTerrain(scenarioData, currentScenario, controller);
                onProgress?.Invoke(0.4f);
                SpawnBuilding(buildingData, currentScenario, controller, trainingData);
                onProgress?.Invoke(0.8f);

                await UniTask.Yield();

                SetInitialBuildingFocus(controller);

                onProgress?.Invoke(1f);
            }
            else
            {
                onProgress?.Invoke(1f);
            }
        }

        private void SpawnTerrain(ScenarioData scenarioData, EScenario currentScenario, DrillMapController controller)
        {
            EBuildingType buildingType = scenarioData.buildingType;
            ETerrainType terrainType = scenarioData.terrainType;

            GameObject terrainObj = MonitoringPoolSystem.Instance.GetTerrain(buildingType, terrainType);

            if (terrainObj != null)
            {
                // 생성한 객체를 빌더가 아닌 '컨트롤러의 루트'로 보냅니다.
                terrainObj.transform.SetParent(controller.TerrainRoot, false);
                terrainObj.SetActive(true);

                List<TRAINEE.TerrainData> terrains = TRAINEE.DataManager.Instance.GetAllData<TRAINEE.TerrainData>(EDataType.TerrainData);
                TRAINEE.TerrainData terrainData = terrains.Find(x => x.terrainType == terrainType);

                if (terrainData != null)
                {
                    string id = ((int)terrainData.terrainType).ToString();

                    MapViewBase view = terrainObj.GetComponent<MapViewBase>();
                    if (view != null)
                    {
                        // 컨트롤러의 딕셔너리에 등록합니다.
                        controller.RegisterView(id, view);
                    }

                    TerrainModel model = ModelFactoryHelper.CreateTerrainModel(terrainData, currentScenario);
                    controller.RegisterModel(id, model);
                }
            }
        }

        private void SpawnBuilding(BuildingData buildingData, EScenario currentScenario, DrillMapController controller, TrainingDatas trainingData)
        {
            string buildingName = buildingData.buildingType.ToString();
            string buildingPath = $"{Const.Path.BUILT_IN_MAP_PATH}{buildingName}/{buildingName}";

            GameObject buildingPrefab = Resources.Load<GameObject>(buildingPath);
            Transform floorParent = controller.BuildingRoot;

            if (buildingPrefab != null)
            {
                GameObject buildingObj = Instantiate(buildingPrefab, controller.BuildingRoot);
                buildingObj.name = buildingName;
                floorParent = buildingObj.transform;

                // layer 설정
                MonitoringPoolSystem.Instance.SetLayerRecursively(buildingObj, LayerMask.NameToLayer("Minimap"));

                // [NeedToCheck] "minimap" layer설정 안해야하는 오브젝트 체크
                if (buildingData.buildingType == EBuildingType.Barracks)
                {
                    GameObject exceptFor = buildingObj.transform.Find("roof_top").gameObject;
                    MonitoringPoolSystem.Instance.SetLayerRecursively(exceptFor, LayerMask.GetMask("Default"));
                }

                string id = buildingName;
                MapViewBase view = buildingObj.GetComponent<MapViewBase>();
                if (view != null)
                {
                    controller.RegisterView(id, view);
                }

                BuildingModel model = ModelFactoryHelper.CreateBuildingModel(buildingData, currentScenario);
                controller.RegisterModel(id, model);
            }

            controller.Floors.Clear();

            foreach (var floorName in buildingData.floors)
            {
                string floorPath = $"{Const.Path.BUILT_IN_MAP_PATH}{buildingName}/{floorName}/{floorName}";
                GameObject floorPrefab = Resources.Load<GameObject>(floorPath);
                GameObject floorObj = null;

                if (floorPrefab != null)
                    floorObj = Instantiate(floorPrefab, floorParent);

                // Layer 지정
                MonitoringPoolSystem.Instance.SetLayerRecursively(floorObj, LayerMask.NameToLayer("Minimap"));

                controller.Floors.Add(floorObj);

                FloorData floorData = TRAINEE.DataManager.Instance.GetData<FloorData>(EDataType.FloorData, floorName);

                if (floorData == null)
                    continue;

                string floorId = floorData.Id;
                MapViewBase floorView = floorObj.GetComponent<MapViewBase>();
                if (floorView != null)
                {
                    controller.RegisterView(floorId, floorView);
                }

                FloorModel fModel = new FloorModel(floorData);
                controller.RegisterModel(floorId, fModel);

                foreach (string roomId in floorData.rooms)
                {
                    string roomPath = $"{Const.Path.BUILT_IN_MAP_PATH}{buildingName}/{floorName}/Room/{roomId}";
                    GameObject roomPrefab = Resources.Load<GameObject>(roomPath);
                    Transform elementParent = floorObj.transform;
                    GameObject roomObj;

                    if (roomPrefab != null)
                        roomObj = Instantiate(roomPrefab, floorObj.transform);
                    else
                    {
                        roomObj = new GameObject(roomId);
                        roomObj.transform.SetParent(floorObj.transform, false);
                    }
                    elementParent = roomObj.transform;

                    // Layer 지정
                    MonitoringPoolSystem.Instance.SetLayerRecursively(roomObj, LayerMask.NameToLayer("Minimap"));

                    RoomData roomData = TRAINEE.DataManager.Instance.GetData<RoomData>(EDataType.RoomData, roomId);

                    if (roomData == null)
                        continue;

                    string roomDataId = roomData.Id;
                    MapViewBase roomView = roomObj.GetComponent<MapViewBase>();
                    if (roomView != null)
                    {
                        controller.RegisterView(roomDataId, roomView);
                    }

                    RoomModel rModel = ModelFactoryHelper.CreateRoomModel(roomData);
                    controller.RegisterModel(roomDataId, rModel);


                    foreach (string elementId in roomData.Elements)
                    {
                        RoomElementData savedElementData = trainingData.roomElementDatas.Find(x => x.Id == elementId);
                        if (savedElementData == null)
                        {
                            Debug.Log($"[MonitoringBuilder] 매칭하는 데이터가 없습니다. {elementId}");
                            continue;
                        }

                        SpawnElement(elementId, savedElementData.roomState, elementParent, controller);
                    }

                    //foreach (string elementId in roomData.Elements)
                    //{
                    //    ERoomState targetState = ERoomState.Normal;
                    //    if (controller.ServerElementStates.TryGetValue(elementId, out ERoomState serverState))
                    //        targetState = serverState;

                    //    SpawnElement(elementId, targetState, elementParent, controller);
                    //}

                    // 요구조자 Sprite 생성
                    SpawnRescueeSprite(trainingData, controller, roomId, roomObj);
                }
            }
        }

        private void SpawnElement(string id, ERoomState state, Transform parent, DrillMapController controller)
        {
            RoomElementData elementData = TRAINEE.DataManager.Instance.GetData<RoomElementData>(EDataType.RoomElementData, id);
            if (elementData == null) return;

            ElementModelBase model = ModelFactoryHelper.CreateElementModel(elementData);
            controller.RegisterModel(id, model);

            GameObject viewObj = MonitoringPoolSystem.Instance.GetElement(id, state);

            if (viewObj == null) return;

            viewObj.transform.SetParent(parent, false);
            viewObj.SetActive(true);

            MapViewBase view = viewObj.GetComponent<MapViewBase>();
            if (view != null)
            {
                controller.RegisterView(id, view);
            }
        }


        /// <summary>
        /// 1. 이 코드의 목적: UiSettingPopup에서 요구조자 설정을 하기위한 스프라이트 만들기
        /// 2. 핵심 로직 흐름(3줄 이내): 스펀빌딩중 룸을 생성할때, 요구조자를 생성(스프라이트 프리팹)하여 룸의 자식으로 지정, 프리팹의 내부 스크립트로 스프라이트 키고끔 조정
        /// 3. 왜 이렇게 구현했는지: 다른 오브젝트 풀링에 비해 휘발성 데이터이며(셋팅할때만 사용), 개수가 많지 않을 것으로 예상
        /// 4. 리스크: 로테이션관련 부모오브젝트와 결합시 문제없을지, Order 문제 있을지, 다른 오브젝트나 다른 훈련생등과 어우러질때 문제가 없을지,
        ///                  FindAll 쓰는부분 괜찮을지
        /// 5. 예외: 3D 오브젝트들과 함꼐 있으니 OrderInLayer는 상관이 없다, 지금 스프라이트는 단발성 데이터로 룸의 중앙에만 두면된다
        /// </summary>
        public void SpawnRescueeSprite(TrainingDatas trainingData, DrillMapController controller, string roomId, GameObject roomObj)
        {
            var rescueesInRoom = trainingData.rescueeDatas.FindAll(x => x.roomid == roomId);

            foreach (RescueeSaveData rescueeData in rescueesInRoom)
            {
                GameObject spritePrefab = Resources.Load<GameObject>("1.Prefabs/8.Sergeant/OccationalSprite");
                if (spritePrefab != null)
                {
                    GameObject spriteObj = Instantiate(spritePrefab, roomObj.transform);
                    spriteObj.name = $"Rescuee_{rescueeData.rescueeid}";

                    if (TryCalculateBounds(roomObj, out Bounds roomBounds))
                    {
                        // X, Z는 방의 정중앙 / Y는 방의 가장 높은 곳
                        spriteObj.transform.position = new Vector3(roomBounds.center.x, roomBounds.max.y, roomBounds.center.z);
                    }
                    else
                    {
                        spriteObj.transform.position = roomObj.transform.position + Vector3.up * 1.0f;
                    }

                    spriteObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                    OccationalSprite occSprite = spriteObj.GetComponent<OccationalSprite>();
                    controller.RegisterOccationalSprite(rescueeData.rescueeid, occSprite);

                }
            }
        }


        //private bool CacheServerElementStates(TrainingDatas trainingData, DrillMapController controller)
        //{
        //    controller.ServerElementStates.Clear();
        //    if (trainingData.roomElementDatas != null)
        //    {
        //        foreach (RoomElementData data in trainingData.roomElementDatas)
        //        {
        //            if (!string.IsNullOrEmpty(data.Id) && !controller.ServerElementStates.ContainsKey(data.Id))
        //            {
        //                controller.ServerElementStates.Add(data.Id, data.roomState);
        //            }
        //        }
        //        return true;
        //    }
        //    else
        //        return false;
        //}


        /// <summary>
        /// 1. 이 코드의 목적: UiSettingPopup에서 요구조자 설정을 하기위한 스프라이트 만들기
        /// 2. 핵심 로직 흐름(3줄 이내): 스펀빌딩중 룸을 생성할때, 요구조자를 생성(스프라이트 프리팹)하여 룸의 자식으로 지정, 프리팹의 내부 스크립트로 스프라이트 키고끔 조정
        /// 3. 왜 이렇게 구현했는지: 다른 오브젝트 풀링에 비해 휘발성 데이터이며(셋팅할때만 사용), 개수가 많지 않을 것으로 예상
        /// 4. 리스크: 로테이션관련 부모오브젝트와 결합시 문제없을지, Order 문제 있을지, 다른 오브젝트나 다른 훈련생등과 어우러질때 문제가 없을지,
        ///           FindAll 쓰는부분 괜찮을지
        /// 5. 예외: 3D 오브젝트들과 함꼐 있으니 OrderInLayer는 상관이 없다, 지금 스프라이트는 단발성 데이터로 룸의 중앙에만 두면된다
        /// </summary>
        private void SetInitialBuildingFocus(DrillMapController controller)
        {
            if (controller.BuildingRoot == null || DrillManager.Instance.CameraHandler == null)
                return;

            //TryCalculateBounds(controller.BuildingRoot.gameObject, out Bounds totalbuildingBounds);
            TryCalculateBounds(controller.TerrainRoot.gameObject, out Bounds totalterrainBounds);

            DrillManager.Instance.CameraHandler.FocusToBounds(totalterrainBounds);

        }

        private bool TryCalculateBounds(GameObject targetObj, out Bounds totalBounds)
        {
            totalBounds = new Bounds(Vector3.zero, Vector3.zero);

            Renderer[] renderers = targetObj.GetComponentsInChildren<Renderer>(false);

            if (renderers.Length == 0)
                return false;

            bool hasBounds = false;
            foreach (Renderer renderer in renderers)
            {
                if ((renderer is MeshRenderer || renderer is SkinnedMeshRenderer) && renderer.enabled)
                {
                    if (!hasBounds)
                    {
                        totalBounds = renderer.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        totalBounds.Encapsulate(renderer.bounds);
                    }
                }
            }
            return hasBounds;
        }
    }
}