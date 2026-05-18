using Cysharp.Threading.Tasks;
using NetworkResponeData;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Management;

namespace TRAINEE
{
    public class GameManager : Singleton<GameManager>
    {
        private PlayerController _player = null;
        
        private MapModelBase _curMapModel = null;
        private MapViewBase _curMapTerrain = null;

        private EScenario _curScenario = EScenario.None;
        private EBuildingType _curBuilding = EBuildingType.None;
        private ETerrainType _curTerrain = ETerrainType.None;

        private string _curScenarioId = string.Empty;

        private bool _isGameStart = false;

        private string _playerId = string.Empty;
        private string _playerRole = string.Empty;

        private List<SystemBase> _systems = null;
        public PlayerController GetPlayer { get => _player; }

        public MapModelBase GetCurrentMapModel { get => _curMapModel; }
        public MapViewBase CurrentMapTerrainModel { get => _curMapTerrain; set => _curMapTerrain = value; }

        public EScenario GetCurrentScenario { get => _curScenario;}

        public List<SystemBase> Systems { private get => _systems; set => _systems = value; }


        public event Action OnGameStart;    // 훈련 시작
        public event Action OnGameStop;     // 훈련 종료
        public event Action OnGamePause;    // 훈련 정지
        public event Action<string> OnGameResult;   // 훈련 결과

        public Action<string, ControllerBase> OnSpawnPlayer;
        public Action<string, ControllerBase> OnDeSpawnPlayer;
        public Action<string, ControllerBase> OnLeftPlayer;

        public T GetSystem<T>() where T : SystemBase
        {
            for (int i = 0; i < _systems.Count; i++)
            {
                if (_systems[i] is T targetSystem)
                {
                    return targetSystem;
                }
            }

            return null;
        }

        public string GetCurrentScenarioId 
        {
            get
            {
                if(string.IsNullOrEmpty(_curScenarioId))
                {

                    int id = (int)_curScenario;
                    _curScenarioId = id.ToString();
                }
                
                return _curScenarioId;
            }
        }
        public EBuildingType GetCurrentBuilding
        {
            get
            {
                if (_curBuilding == EBuildingType.None)
                {
                    ScenarioData data = DataManager.Instance.GetData<ScenarioData>(EDataType.ScenarioData, GetCurrentScenarioId);

                    _curBuilding = data.buildingType;
                }

                return _curBuilding;
            }
        }
        public ETerrainType GetCurrentTerrain 
        {
            get
            {
                if (_curTerrain == ETerrainType.None)
                {
                    ScenarioData data = DataManager.Instance.GetData<ScenarioData>(EDataType.ScenarioData, GetCurrentScenarioId);

                    _curTerrain = data.terrainType;
                }

                return _curTerrain;
            }
        }


        public bool GetGameStart { get => _isGameStart; set => _isGameStart = value; }

        public string PlayerID { get => _playerId; set => _playerId = value; }
        public string PlayerRole { get => _playerRole; set => _playerRole = value; }


        public Action<ControllerBase> OnSpawnLocalPlayer;

        protected override void Awake()
        {
            base.Awake();
            Init();
        }
        protected override void Start()
        {
        }

        protected override void Init()
        {
            base.Init();

#if !GAMEINSTANCE && !DrillSergeant

            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.OnTrainingPauseSuccess += GamePause;
                NetworkManager.Instance.OnTrainingStopSuccess += GameStop;
                NetworkManager.Instance.OnTrainingResultSuccess += GameResult;
            }
#endif
        }

        public void SetCurrentScenario(string scenario)
        {
            EScenario[] allTypes = (EScenario[])Enum.GetValues(typeof(EScenario));

            for (int i = 0; i < allTypes.Length; i++)
            {
                if (allTypes[i] == EScenario.None)
                    continue;

                int scenarioType = (int)allTypes[i];
                int findType = int.Parse(scenario);

                if(findType == scenarioType)
                {
                    _curScenario = allTypes[i];
                    int id = (int)_curScenario;
                    _curScenarioId = id.ToString();
                    Debug.Log($"시나리오 타입 : {allTypes[i].ToString()}");
                    return;
                }
            }
        }
        public void LoadMapModel()
        {
            List<ScenarioData> data = DataManager.Instance.GetAllData<ScenarioData>(EDataType.ScenarioData);

            for (int i = 0; i < data.Count; i++)
            {
                if(data[i].scenario == _curScenario)
                {
                    _curScenarioId = data[i].Id;
                    _curBuilding = data[i].buildingType;
                    _curTerrain = data[i].terrainType;
                }
            }
        }

        public void OnRegisterGamePlayer(string id, ControllerBase player)
        {
            OnSpawnPlayer?.Invoke(id, player);
        }

        public void OnUnRegisterGamePlayer(string id, ControllerBase player)
        {
            OnDeSpawnPlayer?.Invoke(id,player);
        }

        public void GameStart()
        {
            _isGameStart = true;

            OnGameStart?.Invoke();

            OnGameStart = null;
        }

        public void GameStop(ResTrainingStop result)
        {
            _isGameStart = false;
            OnGameStop?.Invoke();
        }

        public void GamePause(ResTrainingPause result)
        {
            OnGamePause?.Invoke();

            _isGameStart = !_isGameStart;
        }

        public void GameResult(ResTrainingResult result)
        {
            OnGameResult?.Invoke(result.resultJson);
            
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

#if !GAMEINSTANCE && !DrillSergeant
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.OnTrainingPauseSuccess -= GamePause;
                NetworkManager.Instance.OnTrainingStopSuccess -= GameStop;
                NetworkManager.Instance.OnTrainingResultSuccess -= GameResult;
            }
#endif
        }

        public void ResetScenarioData()
        {
            _curScenarioId = null;
            _curBuilding = EBuildingType.None;
            _curTerrain = ETerrainType.None;
        }

        // false : PC 모드, true : VR 모드
        public bool VRModeCheck()
        {
            // VR 사용 여부 확인
            var manager = XRGeneralSettings.Instance.Manager;

            bool isVRActive = manager != null && manager.activeLoader != null;

            if (isVRActive)
            {
                return true;
            }
            else
            {
                return false;
            }

        }
    }
}
