using System;
using System.Collections.Generic;
using UnityEngine;



namespace TRAINEE
{
    // 2. 지형(Terrain) 데이터
    [Serializable]
    public class TerrainMapData : DataBase
    {
        public string TerrainPrefabPath;                // 지형 프리팹 경로 (예: "Maps/Terrain_01")
    }

    [Serializable]
    public class SaveFileDatas : DataBase 
    {
        public TrainingDatas trainingData = new TrainingDatas();
        public string fileDate;
        public string trainingVersion;
    }

    [Serializable]
    public class TrainingDatas : DataBase 
    {
        public string scenarioNum;
        public string scenarioName;
        public string scenarioDescription;
        public string scenarioCode;
        public string scenarioGoals;
        public string scenarioStory;
        public string scenarioTime;
        public string scenarioWeather;
        public string scenarioPeople;
        public string rescueeCount;
        public TerrainsetData terrainsetData = new TerrainsetData();
        public List<RoomElementData> roomElementDatas = new List<RoomElementData>();
        public ScenariomovieData scenariomovieData;
        public List<MoviespeachData> moviespeachDatas = new List<MoviespeachData>();
        public List<EventObjectData> eventObjectDatas = new List<EventObjectData>();
        public List<RescueeSaveData> rescueeDatas = new List<RescueeSaveData>();
        public List<DisasterSaveData> disasterSaveDatas = new List<DisasterSaveData>();
    }

    [Serializable]
    public class ItemData : DataBase
    {
        public EItemType _itemType;     // 장비 종류 (장착,전시,설치)
        public EInventoryType _slot;    // 장비 인벤토리 슬롯 위치 1. Main, 2. sub ,3.thrid
        public EEquipType _equipType;   // 장비 모델타입
        public bool _isOneHand;         // 장비가 한손인지 양손인지

        public string _aniId;           // 장비 별 플레이어 애니메이터 아이디
        public string _eduequipId;      // 장비 별 교육 ui id

        public string _iconId;          // 장비 아이콘 id
        public GameObject _prefab;       // 장비 오브젝트
    }

    [Serializable]
    public class RescueeSaveData : DataBase
    {
        public EScenario scenario;         // 1. 소속 시나리오 (예: 화재)
        public string rescueeid;           // 2. 고유 ID (예: rescuee_217_01)
        public string roomid;              // 3. 소속 방 ID (네비게이션이나 UI 표기용)

        public ERescueeType rescueeType;   // 4. 교관이 설정한 타입 (경상/중상)
        public ERescueeState rescueeState; // 5. 초기 상태 (대기 등)

        public Vector3 spawnPosition;
        public Quaternion spawnRotation;
    }

    [Serializable]
    public class DisasterSaveData : DataBase
    {
        public EScenario scenario;              // 1. 소속 시나리오 (예: 화재)
        public string disasterid;               // 2. 고유 ID (예: disaster_217_01)
        public string roomid;                   // 3. 소속 방 ID (네비게이션이나 UI 표기용)

        public ERoomElementType disasterType;   // 4. 교관 및 편집기 설정한 타입
        public EDisasterState disasterState;    // 5. 초기 상태 (대기 등)

        public Vector3 spawnPosition;
        public Quaternion spawnRotation;
    }
}
