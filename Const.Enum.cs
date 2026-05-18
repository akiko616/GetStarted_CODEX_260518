using System;
using UnityEngine;

namespace TRAINEE
{
    public static partial class Const
    {
        

    }
    public enum ESceneType
    {
        None = -1,
        Login,              // 교육생 로그인 씬
        TrainingLobby,       // 교육생 훈련 대기 씬
        Training,           // 교육생 훈련 씬
        SergeantLogin,       // 교과석 로그인, 관리자관리 등
        SergeantExcurtion,     // 교관석 훈련모드, 학습관리모드
    }

    public enum EPopupType
    {
        None = -1,
        UiLoadingPopup,
        UiInteractionPopup,
        UiScenarioDialogPopup,
        UiSergeantLoadingPopup,     // 교관석 로딩 팝업
        UiTrainingListPopup,        // 교관석 훈련리스트 팝업
        UiSettingPopup,         // 교관석 훈련설정 팝업
        UiDashboardPopup,        // 교관석 훈련관리 팝업
        UiResultPopup,           // 교관석 평가관리 팝업


        UiEduGuidePopup = 100,
        UiEduPorcedurePopup = 101,
        UiEduSafeCheckPopup = 102,
        UiEduEquipCheckPopup = 103,
        UiEduFinalCheckPopup = 104,
        UiEduResultPopup =105,

        //로비 팝업 임시
        UiEnterTrainingLobbyPopup = 200,
        UILobbyEquipPopup = 201,
        UILobbyEquipTextPopup = 202
    }

    public enum EScenario
    {
        None = -1,
        Collapse,       // 붕괴
        Fire,           // 화재
        WildFire,       // 산불
        EarthQuake,     // 지진
        Tornado,        // 태풍
        Civilian,       // 불발탄 _대민지원
        Remove,         // 불발탄 _제거
        GeneralVehicle, // 일반차량
        TrackedVehicle, // 전술차량
        Hel_Landing,    // 헬리콥터 착륙
        Hel_Crash,      // 헬리콥터 추락
        Boat_Flood,     // 육경전 침수
        NuClear,         // 핵무기
        Complex1,        // 복합1
        Complex2,        // 복합2


        Lobby = 100,     // 로비 (임시)

    }

    public enum ELayerType
    {
        None = -1,
        UiLoginLayer,
        UiHudLayer,
        UiInteractionLayer,
        UiMainExcurtionLayer,
        UiSubExcurtionLayer,
        UIPauseLayer
    }

    public enum EDataType
    {
        None = -1,
        ScenarioData,
        BuildingData,
        FloorData,
        SectionData,
        RoomData,
        TerrainData,
        StringData,
        RoomElementData,                // 로컬 빌딩 데이터
        MoviespeachData,                // 로컬 훈련영상 자막 데이터
        ScenariomovieData,              // 로컬 훈련영상&자막 데이터
        EtcduData,                      // 로컬 훈련영상 팝업 데이터
        MovieidData,                    // 로컬 훈령영상 데이터
        TerrainsetData,                 // 로컬 환경데이터
        EquipData,                      // 로컬 장비데이터
        EquipSpawnPointData,            // 로컬 장비스폰데이터
        SpawnPointData,                 // 로컬 시나리오 별 플레이어 스폰데이터,
        RescueeData,                    // 로컬 요구조자 데이터
        RoomSpawnData,                  // 로컬 Room 별 스폰 위치 데이터 (요구조자, 불 등등 재난 오브젝트 설치시 필요)
        CharEquipData,                  // 로컬 시나리오 별 캐릭터 역할에 따른 착용 장비 데이터
        TrainingLobbyEquipData,         // 대기실 뿌리는 장비 정보
        TleeeduData,                    // 대기실에서 사용되는 교육 팝업 지정
        TleemovieData,                  // 대기실에서 사용되는 영상 지정
        DisasterData,                   // 로컬 재난데이터(편집기에서 사용)
        SpriteData,

        TrainingData,                   // 편집기 - 교관석 에서 수정된 최종 훈련 데이터
        SeverRoomElementData,           // 서버로부터 받은 훈련사항 빌딩 데이터
        ServerScenariomovieData,        // 서버로부터 받은 훈련 영상 데이터
        ServerMovieSpeachData,          // 서버로부터 받은 훈련 영상 자막 데이터
        ServerMovieidData,              // 서버로부터 받은 훈련 영상 데이터
        ServerTerrainsetData,           // 서버로부터 받은 환경 데이터
        ServerRescueeSaveData,          // 서버로부터 받은 요구조자 데이터
        ServerDisasterSaveData,         // 서버로부터 받은 재난 데이터


        // MinimapSpriteData,              // 교관석 미니맵 스프라이트 데이터
    }

    public enum EBuildingType
    {
        None = -1,
        Barracks,
        Mountain,
        UnexplodedSupport,
        UnexplodedOperation,
        VehicleRollover,
        HelicopterEmergencyLanding = 6,
        HelicopterCrash =7,
        PatrolBoat = 8,

        Lobby = 100,    
    }

    public enum ETerrainType
    {
        None = -1,
        Barracks_Terrain,
        Mountain_Terrain,
        UnexplodedSupport_Terrain,
        UnexplodedOperation_Terrain,
        VehicleRollover_Terrain,
        HelicopterEmergencyLanding_Terrain,
        HelicopterCrash_Terrain,
        PatrolBoat_Terrain = 8,

        Lobby_Terrain = 100,    
    }

    public enum ERoomState
    {
        None = -1,
        Normal,
        Collapse,
        Fire
    }

    public enum EModelEventType
    {
        None = -1,
        Break
    }

    public enum ERoomElementType
    {
        None = -1,
        Prop = 1,           
        SideWall = 2,
        Ceiling = 3,
        Floor = 4,
        OuterWall = 5,
        InnerWall = 6,
        WoodDoor = 7,
        GlassDoor = 8,
        SideGlassDoor = 9,
        Rescuee = 10,               // 요구조자
        Fire_Staircase = 11,        // 화재계단
        Collapse_Staircase = 12,    // 붕괴 계단
        Uxo_support = 400
    }

    public enum EDoorState
    {
        None =-1,
        Open,
        Close,
        Lock
    }

    #region Player

    public enum EStateType
    {
        None = -1,

        //공용
        Idle,
        Walk,
        EquipAction,
        Approach,
        //요구조자
        Follow
    }

    [Serializable]
    public enum EAnimParam
    {
        None = -1,

        // 플레이어
        Move,


        // 플레이어 장비 착용 시 애니메이션
        Start,
        Ing,
        End,


        FullAttack, // 임시




        // 모델링
        open = 100,
        close =101
    }

    [Serializable]
    public enum EAnimLayer : int
    {
        None = -1,
        Base = 0,
        Upper = 1,
        Full = 2,
    }

    public enum EPlayerCostume
    {
        None = -1,
        Base = 0,
        Mine = 1,
        Fire = 2,
        Unit = 3,
        ArmySee = 4,
        Nuclear = 5,
        Forest = 6,
        Tanker = 7,
        Pilot = 8,
        Rescuee = 9,
    }
    #endregion

    #region Rescuee
    public enum ERescueeType
    {
        None = -1, // 없음

        Minor = 0,  // 경상자
        Severe  // 중상자
    }
    public enum ERescueeState
    {
        Undiscovered,     // 0. 초기 상태 (발견 전)
        Discovered,       // 1. 발견 및 상부 보고 완료
        ConsciousChecked, // 2. 의식 확인 완료
        Following,        // 3. (경상자) 플레이어 따라가는 중
        Treated,          // 3. (중상자) 응급처치(치료) 완료
        OnStretcher,      // 4. (중상자) 바스켓 스트레쳐에 실림
        Transfer,         // 5. (중상자) 바스켓 스트레쳐로 안전지대로 이송
        SafeZone          // 6. 안전지대 도착 (최종 구출 완료)
    }
    #endregion
    #region Disaster
    public enum EDisasterType
    {
        None = -1,
    }

    public enum EDisasterState
    {
        None = -1,
    }


    #endregion



    #region Event
    public enum EEventType
    {
        None,
        Interaction,        // 상호작용 (3d 모델 클릭 - DataUpdate)
        UpdateView,
        UI,                 // 상호작용 (UI 클릭)
        Equip,
        LocalAction,

        Spawn,               // 서버용 (스폰)
        Server,
    }

    #endregion

    public enum EEnvironType
    {
        None = -1,
        Day,
        Cloud
    }

    public enum EWeather
    {
        None = -1,
        Sunny,
        Fog,
        Rain,
        Snow
    }

    public enum EDialogType
    {
        None = -1,
        Start,          // 시작
        Ready,          // 준비
        MissionBefore,  // 임무 전
        MissionAfter,   // 임무 후
        End             // 종료
    }


    public enum EItemType
    {
        None = -1,
        Equip,              // 장착용
        Display,            // 전시용
        Install,            // 설치용
    }

    public enum EEquipType
    {
        None = -1,

        Melee,
        Deployable,
        Radio,
    }

    public enum EInventoryType
    {
        None = -1,
        Main,
        Sub,
        Thrid
    }

    public enum ChatType
    {
        None = -1,
        Mine,
        Other,
    }
    public enum ChatChannel
    {
        None = -1,
        Channel1,
        Channel2,
    }
    public enum PauseType
    {
        Pause,
        Stop
    }
    public enum GameEventType
    {
        None = -1,
        Door = 0,
        Equip,
        Terrain,
        Rescuee,
        Disaster,

        LobbyItem
    }
    public enum ELobbyItemType
    {
        Text = 1,
        Video = 2

    }

    public enum EActionPhase
    {
        None, 
        Start, 
        Ing, 
        End
    }

}
