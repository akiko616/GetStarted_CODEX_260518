using System.Collections.Generic;
using UnityEngine;

namespace TRAINEE
{
    /// <summary>
    /// 맵 빌드 시스템
    /// </summary>
    public static class MapBuildSystem
    {

        public static void Build(MapControllerBase controller, Transform[] transRoot)
        {
            SettingBuilding(controller, transRoot[0]);
            SettingTerrain(controller, transRoot[1]);
        }

        #region 빌딩 세팅
        private static void SettingBuilding(MapControllerBase controller, Transform transRoot)
        {
            List<BuildingData> buildings = DataManager.Instance.GetAllData<BuildingData>(EDataType.BuildingData);


            if (buildings == null)
            {
                //Debug.LogError("Building Data is Null");
                return;
            }

            BuildingData building = buildings.Find(x => x.buildingType == GameManager.Instance.GetCurrentBuilding);
            string buildingName = GameManager.Instance.GetCurrentBuilding.ToString();
            string path = $"{Const.Path.BUILT_IN_MAP_PATH}{buildingName}/";
            
            string id = buildingName;

            Debug.Log($"Path : {path}");
            // 모델 생성
            BuildingModel model = ModelFactoryHelper.CreateBuildingModel(building);
            controller.RegisterModel(id, model);

            // 뷰 생성
            MapViewBase view = LdResources.LoadMap<MapViewBase>($"{path}{buildingName}", transRoot);
            controller.RegisterView(id, view);

            view.Init(id);
            // 층 구성

            for (int i = 0; i < building.floors.Count; i++) 
            {
                string key = building.floors[i];
                FloorData floor = DataManager.Instance.GetData<FloorData>(EDataType.FloorData, key);

                if (floor != null)
                {
                    model.AddFloor(CreateFloor(controller,floor, path, view.transform));
                }
            }

        }

        private static FloorModel CreateFloor(MapControllerBase controller, FloorData floorData, string _path, Transform parent)
        {
            string path = $"{_path}{floorData.floor}/";

            Debug.Log($"Path : {path}");

            MapViewBase view = LdResources.LoadMap<MapViewBase>($"{path}{floorData.floor}", parent);

            if (view == null)
            {
                //Debug.Log($"Not Find Floor Prefab :{floorData.floor}");
                return null;
            }

            string id = floorData.Id;

            // 모델 생성
            FloorModel model = new FloorModel(floorData);
            controller.RegisterModel(id, model);

            // 뷰 생성
            controller.RegisterView(id, view);

            // 섹션 구성
            for (int i = 0; i < floorData.rooms.Count; i++)
            {
                string key = floorData.rooms[i];

                RoomData room = DataManager.Instance.GetData<RoomData>(EDataType.RoomData, key);

                if (room != null)
                {
                    CreateRoom(controller, room, path, view.transform);
                }
            }

            return model;

        }

        private static void CreateRoom(MapControllerBase controller, RoomData roomData, string path, Transform parent)
        {
            MapViewBase view = LdResources.LoadMap<MapViewBase>($"{path}Room/{roomData.room}", parent);

            if (view == null)
            {
                //Debug.Log($"Not Find Room Prefab :{roomData.Room}");
                return;
            }

            string id = roomData.Id;

            // 모델 생성
            RoomModel model = ModelFactoryHelper.CreateRoomModel(roomData);
            controller.RegisterModel(id, model);

            // 뷰 생성
            controller.RegisterView(id, view);
            view.Init(id);

            for (int i = 0; i < roomData.Elements.Count; i++)
            {
                string key = roomData.Elements[i];

                RoomElementData element = null;
                if (DataManager.Instance.IsServer)
                {
                    element = DataManager.Instance.GetData<RoomElementData>(EDataType.SeverRoomElementData,key);
                }
                else
                {
                    element = DataManager.Instance.GetData<RoomElementData>(EDataType.RoomElementData, key);
                }


                if (element != null)
                {
                    CreateRoomElement(controller, element, path, view.transform);
                }
            }
            
            CreateRescueeModels(controller, id);
            CreateDisasterModels(controller, id);

        }

        private static void CreateRoomElement(MapControllerBase controller, RoomElementData elementData, string path, Transform parent)
        {
            string roomState = elementData.roomState.ToString();

            MapViewBase view = LdResources.LoadMap<MapViewBase>($"{path}Elements/{roomState}/{elementData.element}", parent);

            if (view == null)
            {
                //Debug.Log($"Not Find RoomElement Prefab :{elementData.roomElement}");
                return;
            }

            string id = elementData.Id;

            // 뷰 생성
            view.Init(id);
            controller.RegisterView(id, view);

            // 모델 생성
            ElementModelBase model = ModelFactoryHelper.CreateElementModel(elementData);
            controller.RegisterModel(id, model);

        }

        private static void CreateRescueeModels(MapControllerBase controller,string id)
        {
            RescueeSaveData rescuee = null;
            RoomElementData elementData = null;
            RescueeModel model = null;

            if (DataManager.Instance.IsServer)
            {
                rescuee = DataManager.Instance.GetData<RescueeSaveData>(EDataType.ServerRescueeSaveData, id);

                if (rescuee == null)
                {
                    return;
                }

                elementData = new RoomElementData 
                { 
                    Id = rescuee.rescueeid, 
                    element = rescuee.rescueeid, 
                    roomElementType = ERoomElementType.Rescuee 
                };

                model = ModelFactoryHelper.CreateElementModel(elementData) as RescueeModel;

            }
            else
            {
                RescueeData localRescuee = DataManager.Instance.GetData<RescueeData>(EDataType.RescueeData, id);


                if (localRescuee == null)
                {
                    return;
                }


                rescuee = new RescueeSaveData
                {
                    Id = localRescuee.Id,
                    scenario = GameManager.Instance.GetCurrentScenario,
                    rescueeid = localRescuee.Id,
                    roomid = localRescuee.roomid,
                    rescueeType = localRescuee.rescueeType,
                    rescueeState = localRescuee.rescueeState,
                    spawnPosition = Vector3.zero,
                    spawnRotation = Quaternion.identity,
                };

                elementData = new RoomElementData { Id = id, element = localRescuee.Id, roomElementType = ERoomElementType.Rescuee };
                model = ModelFactoryHelper.CreateElementModel(elementData) as RescueeModel;
            }


            if (model != null)
            {
                model.SetupModelData(rescuee);
                controller.RegisterModel(rescuee.rescueeid, model);
            }
        }

        private static void CreateDisasterModels(MapControllerBase controller, string id)
        {
            DisasterSaveData disaster = null;
            RoomElementData elementData = null;
            DisasterModel model = null;

            if (DataManager.Instance.IsServer)
            {
                disaster = DataManager.Instance.GetData<DisasterSaveData>(EDataType.ServerDisasterSaveData, id);

                if (disaster == null)
                {
                    return;
                }

                elementData = new RoomElementData
                {
                    Id = disaster.disasterid,
                    element = disaster.disasterid,
                    roomElementType = disaster.disasterType
                };

                model = ModelFactoryHelper.CreateElementModel(elementData) as DisasterModel;

            }
            else
            {
                List<DisasterData> list = DataManager.Instance.GetAllData<DisasterData>(EDataType.DisasterData);
                DisasterData localDisaster = null;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].roomid == id)
                    {
                        localDisaster = list[i];
                        break;
                    }
                }

                if (localDisaster == null)
                {
                    return;
                }


                disaster = new DisasterSaveData
                {
                    Id = localDisaster.disasterid,
                    scenario = GameManager.Instance.GetCurrentScenario,
                    disasterid = localDisaster.disasterid,
                    disasterType = localDisaster.roomElementType,
                    disasterState = localDisaster.disasterState,
                    spawnPosition = Vector3.zero,
                    spawnRotation = Quaternion.identity,
                };

                elementData = new RoomElementData { Id = id, element = disaster.Id, roomElementType = disaster.disasterType };
                model = ModelFactoryHelper.CreateElementModel(elementData) as DisasterModel;
            }


            if (model != null)
            {
                model.SetupModelData(disaster);
                controller.RegisterModel(disaster.disasterid, model);
            }
        }

        #endregion

        #region 터레인 세팅
        private static void SettingTerrain(MapControllerBase controller, Transform transRoot)
        {
            string buildingName = GameManager.Instance.GetCurrentBuilding.ToString();
            string terrainName = GameManager.Instance.GetCurrentTerrain.ToString();
            string path = $"{Const.Path.BUILT_IN_MAP_PATH}{buildingName}/{terrainName}";

            List<TerrainData> terrains = DataManager.Instance.GetAllData<TerrainData>(EDataType.TerrainData);

            if (terrains == null)
            {
                //Debug.LogError("Building Data is Null");
                return;
            }

            TerrainData terrain = terrains.Find(x => x.terrainType == GameManager.Instance.GetCurrentTerrain);

            string id = terrainName;
            // 모델 생성
            TerrainModel model = ModelFactoryHelper.CreateTerrainModel(terrain);
            controller.RegisterModel(id, model);

            // 뷰 생성
            MapViewBase view = LdResources.LoadMap<MapViewBase>($"{path}", transRoot);
            controller.RegisterView(id, view);

            GameManager.Instance.CurrentMapTerrainModel = view;

        }
        #endregion
    }
}
