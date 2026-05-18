using UnityEngine;

namespace TRAINEE
{
    public static class ModelFactoryHelper
    {
        public static BuildingModel CreateBuildingModel(BuildingData data)
        {
            return CreateBuildingModel(data, GameManager.Instance.GetCurrentScenario);
        }

        public static TerrainModel CreateTerrainModel(TerrainData data)
        {
            return CreateTerrainModel(data, GameManager.Instance.GetCurrentScenario);
        }

        public static BuildingModel CreateBuildingModel(BuildingData data, EScenario type)
        {
            switch (type)
            {
                case EScenario.Fire:
                case EScenario.Collapse:
                case EScenario.EarthQuake:
                case EScenario.Tornado:
                case EScenario.WildFire:
                case EScenario.GeneralVehicle:
                case EScenario.TrackedVehicle:
                case EScenario.Boat_Flood:
                    return new BuildingModel(data);
                case EScenario.Civilian:
                    return new BuildingModel(data);
                case EScenario.Remove:
                    return new BuildingModel(data);
                case EScenario.Hel_Crash:
                case EScenario.Hel_Landing:
                    return new BuildingModel(data);
                case EScenario.Lobby:
                    return new BuildingModel(data);
            }
            return null;
        }

        public static TerrainModel CreateTerrainModel(TerrainData data, EScenario type)
        {
            switch (type)
            {
                case EScenario.Fire:
                case EScenario.Collapse:
                case EScenario.EarthQuake:
                case EScenario.Tornado:
                case EScenario.WildFire:
                case EScenario.GeneralVehicle:
                case EScenario.TrackedVehicle:
                case EScenario.Boat_Flood:
                    return new TerrainModel(data);
                case EScenario.Civilian:
                    return new TerrainModel(data);
                case EScenario.Remove:
                    return new TerrainModel(data);
                case EScenario.Hel_Crash:
                    return new TerrainModel(data);
                case EScenario.Hel_Landing:
                    return new TerrainModel(data);
                case EScenario.Lobby:
                    return new TerrainModel(data);
            }
            return null;
        }
        public static RoomModel CreateRoomModel(RoomData data)
        {
            return new RoomModel(data);
        }

        public static ElementModelBase CreateElementModel(RoomElementData data)
        {

            switch (data.roomElementType)
            {
                case ERoomElementType.Prop:
                    return new StructureModel(data);
                case ERoomElementType.SideWall:
                case ERoomElementType.Ceiling:
                case ERoomElementType.Floor:
                case ERoomElementType.OuterWall:
                case ERoomElementType.InnerWall:
                    return new StructureModel(data);
                case ERoomElementType.WoodDoor:
                case ERoomElementType.GlassDoor:
                case ERoomElementType.SideGlassDoor:
                    return new DoorModel(data);
                case ERoomElementType.Rescuee:
                    return new RescueeModel(data);
                case ERoomElementType.Uxo_support:
                    return new StructureModel(data);
                case ERoomElementType.Fire_Staircase:
                case ERoomElementType.Collapse_Staircase:
                    return new DisasterModel(data);
            }

            return null;
        }
    }
}
