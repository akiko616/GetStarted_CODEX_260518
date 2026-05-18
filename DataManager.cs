using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.Analytics.IAnalytic;


namespace TRAINEE
{
    public class DataManager : Singleton<DataManager>
    {
        // InGame 관련 데이터를 관리
        // MapData, ScenarioData...etc

        // 로컬 데이터
        private Dictionary<EDataType, Dictionary<string, DataBase>> _loadData = new Dictionary<EDataType, Dictionary<string, DataBase>>();
        private Dictionary<EDataType, Action> _loaderMapping = new Dictionary<EDataType, Action>();

        private string _serverData;
        private bool _isServerData = false;
        public string ServerData
        {
            set { _serverData = value; }
        }

        public bool IsServer
        {
            get { return _isServerData; }
        }
        protected override void Awake()
        {
            base.Awake();
            Init();
        }
        protected override void Start()
        {
            InitLoaderMapping();
        }

        protected override void Init()
        {
            LoadingHelper.RegisterLoading(new Loading(OnLocalDataLoadingAsync));
        }

        private void InitLoaderMapping()
        {
            // 로컬 데이터
            _loaderMapping.Add(EDataType.BuildingData, () => LoadTable<BuildingData>(EDataType.BuildingData, "BuildingData",true));
            _loaderMapping.Add(EDataType.FloorData, () => LoadTable<FloorData>(EDataType.FloorData, "FloorData",true));
            _loaderMapping.Add(EDataType.RoomData, () => LoadTable<RoomData>(EDataType.RoomData, "RoomData", true));
            _loaderMapping.Add(EDataType.ScenarioData, () => LoadTable<ScenarioData>(EDataType.ScenarioData, "ScenarioData", true));
            _loaderMapping.Add(EDataType.TerrainData, () => LoadTable<TerrainData>(EDataType.TerrainData, "TerrainData", true));
            _loaderMapping.Add(EDataType.StringData, () => LoadTable<StringData>(EDataType.StringData, "StringData", true));
            _loaderMapping.Add(EDataType.EtcduData, () => LoadTable<EtceduData>(EDataType.EtcduData, "EtceduData", true));
            _loaderMapping.Add(EDataType.EquipData, () => LoadTable<EquipData>(EDataType.EquipData, "EquipData", true));
            _loaderMapping.Add(EDataType.EquipSpawnPointData, () => LoadTable<EquipSpawnPointData>(EDataType.EquipSpawnPointData, "EquipSpawnPointData", true));
            _loaderMapping.Add(EDataType.SpawnPointData, () => LoadTable<SpawnPointData>(EDataType.SpawnPointData, "SpawnPointData", true));
            _loaderMapping.Add(EDataType.SpriteData, () => LoadTable<SpriteData>(EDataType.SpriteData, "SpriteData", true));
            _loaderMapping.Add(EDataType.RescueeData, () => LoadTable<RescueeData>(EDataType.RescueeData, "RescueeData", true));
            _loaderMapping.Add(EDataType.RoomSpawnData, () => LoadTable<RoomSpawnData>(EDataType.RoomSpawnData, "RoomSpawnData", true));
            _loaderMapping.Add(EDataType.CharEquipData, () => LoadTable<CharEquipData>(EDataType.CharEquipData, "CharEquipData", true));
            _loaderMapping.Add(EDataType.TrainingLobbyEquipData, () => LoadTable<TrainingLobbyEquipData>(EDataType.TrainingLobbyEquipData, "TrainingLobbyEquipData", true));
            _loaderMapping.Add(EDataType.TleeeduData, () => LoadTable<TleeeduData>(EDataType.TleeeduData, "TleeeduData", true));
            _loaderMapping.Add(EDataType.TleemovieData, () => LoadTable<TleemovieData>(EDataType.TleemovieData, "TleemovieData", true));
            _loaderMapping.Add(EDataType.DisasterData, () => LoadTable<DisasterData>(EDataType.DisasterData, "DisasterData", true));

            // 서버 데이터
            _loaderMapping.Add(EDataType.RoomElementData, () => LoadTable<RoomElementData>(EDataType.RoomElementData, "RoomElementData", true));
            _loaderMapping.Add(EDataType.MoviespeachData, () => LoadTable<MoviespeachData>(EDataType.MoviespeachData, "MoviespeachData", true));
            _loaderMapping.Add(EDataType.ScenariomovieData, () => LoadTable<ScenariomovieData>(EDataType.ScenariomovieData, "ScenariomovieData", true));
            _loaderMapping.Add(EDataType.MovieidData, () => LoadTable<MovieidData>(EDataType.MovieidData, "MovieidData", true));
            _loaderMapping.Add(EDataType.TerrainsetData, () => LoadTable<TerrainsetData>(EDataType.TerrainsetData, "TerrainsetData", true));

            // 서버 데이터 테스트용
            _loaderMapping.Add(EDataType.TrainingData, () => LoadTable<SaveFileDatas>(EDataType.TrainingData, "TrainingData", true));
            _loaderMapping.Add(EDataType.SeverRoomElementData, () => LoadTable<RoomElementData>(EDataType.SeverRoomElementData, "RoomElementData", false));
            _loaderMapping.Add(EDataType.ServerScenariomovieData, () => LoadTable<ScenariomovieData>(EDataType.ServerScenariomovieData, "ScenariomovieData", false));
            _loaderMapping.Add(EDataType.ServerMovieSpeachData, () => LoadTable<MoviespeachData>(EDataType.ServerMovieSpeachData, "MoviespeachData", false));
            _loaderMapping.Add(EDataType.ServerMovieidData, () => LoadTable<MovieidData>(EDataType.ServerMovieidData, "MovieidData", false));
            _loaderMapping.Add(EDataType.ServerTerrainsetData, () => LoadTable<TerrainsetData>(EDataType.ServerTerrainsetData, "TerrainsetData", false));
            _loaderMapping.Add(EDataType.ServerRescueeSaveData, () => LoadTable<RescueeSaveData>(EDataType.ServerRescueeSaveData, "RescueeSaveData", false));
            _loaderMapping.Add(EDataType.ServerDisasterSaveData, () => LoadTable<DisasterSaveData>(EDataType.ServerDisasterSaveData, "ServerDisasterSaveData", false));
        }
        public void LoadData()
        {
            // Resources/Data 폴더에 있는 모든 Json 데이터를 읽어 온다.
            // Json 형식으로 되어 있는 파일을 파싱한다.
            //LoadTable<>("BuildingData");

            foreach (EDataType data in Enum.GetValues(typeof(EDataType)))
            {
                if (data == EDataType.None) continue;

                if (_loaderMapping.TryGetValue(data, out Action loader))
                {
                    loader.Invoke(); // LoadTable<T>가 실행됨
                }
                else
                {
                    Debug.LogWarning($"[DataManager] 매핑되지 않은 데이터 타입입니다: {data}");
                }
            }
        }

        public async UniTask OnLocalDataLoadingAsync(Action<float, string> onProgress, int delaytime)
        {
            EDataType[] allTypes = (EDataType[])Enum.GetValues(typeof(EDataType));

            // None 은 갯수에서 삭제
            int totalCnt = allTypes.Length -1;
            int currentCnt = 0;

            for (int i = 0; i < allTypes.Length; i++)
            {
                EDataType data = allTypes[i];

                if (data == EDataType.None)
                {
                    continue;
                }

                if (_loaderMapping.TryGetValue(data, out Action loader))
                {
                    loader?.Invoke(); // LoadTable<T>가 실행됨
                }
                else
                {
                    Debug.LogWarning($"[DataManager] 매핑되지 않은 데이터 타입입니다: {data}");
                }
                await UniTask.Delay(delaytime);

                currentCnt++;

                float progress = (float)currentCnt / totalCnt;

                onProgress?.Invoke(progress, $"{data} 데이터 로드 중...");

            }
        }

        public async UniTask OnServerDataLoadingAsync(Action<float, string> onProgress, int delaytime)
        {
            int currentCnt = 0;

            List<SaveFileDatas> savedata = GetAllData<SaveFileDatas>(EDataType.TrainingData);
            
            int totalCnt = savedata.Count;

            for (int i = 0; i < savedata.Count ; i++)
            {
                TrainingDatas trainingData = savedata[i].trainingData;

                if(trainingData != null)
                {
                    // 시나리오 타입 세팅
                    GameManager.Instance.SetCurrentScenario(trainingData.scenarioNum);

                    ReSettingData<TerrainsetData>(trainingData.terrainsetData,EDataType.ServerTerrainsetData);
                    ReSettingData<ScenariomovieData>(trainingData.scenariomovieData, EDataType.ServerScenariomovieData);
                    ReSettingData<RoomElementData>(trainingData.roomElementDatas, EDataType.SeverRoomElementData);
                    ReSettingData<MoviespeachData>(trainingData.moviespeachDatas, EDataType.ServerMovieSpeachData);
                    ReSettingData<RescueeSaveData>(trainingData.rescueeDatas, EDataType.ServerRescueeSaveData);
                    ReSettingData<DisasterSaveData>(trainingData.disasterSaveDatas, EDataType.ServerDisasterSaveData);
                }


                await UniTask.Delay(delaytime);

                currentCnt++;

                float progress = (float)currentCnt / totalCnt;

                onProgress?.Invoke(progress, $" 서버 데이터 준비 중...");
            }
        }

        private void ReSettingData<T>(T data, EDataType type) where T : DataBase
        {
            if (data == null) return;

            // 해당 타입의 딕셔너리가 없으면 새로 만들어줍니다.
            if (!_loadData.ContainsKey(type))
            {
                _loadData[type] = new Dictionary<string, DataBase>();
            }
            else
            {
                // 이미 있다면, 서버 데이터로 덮어씌우기 위해 기존 데이터를 날려줍니다.
                // (만약 누적해야 한다면 Clear()를 지우시면 됩니다!)
                _loadData[type].Clear();
            }

            // DataBase를 상속받았기 때문에 data.Id를 키값으로 쓸 수 있습니다!
            _loadData[type].Add(data.Id, data);

            Debug.Log($"[DataManager] 서버 데이터 세팅 완료 (단일) - 타입: {type}, ID: {data.Id}");
        }

        private void ReSettingData<T>(List<T> dataList, EDataType type) where T : DataBase 
        {
            if (dataList == null || dataList.Count == 0) return;

            if (!_loadData.ContainsKey(type))
            {
                _loadData[type] = new Dictionary<string, DataBase>();
            }
            else
            {
                _loadData[type].Clear();
            }

            for (int i = 0; i < dataList.Count; i++)
            {
                T data = dataList[i];
                if (data == null) continue;

                // 혹시 모를 ID 중복 파싱 방어 코드
                if (_loadData[type].ContainsKey(data.Id))
                {
                    Debug.LogWarning($"[DataManager] 서버 데이터 중복 ID 덮어쓰기! 타입: {type}, ID: {data.Id}");
                    _loadData[type][data.Id] = data;
                }
                else
                {
                    _loadData[type].Add(data.Id, data);
                }
            }

            Debug.Log($"[DataManager] 서버 데이터 세팅 완료 (리스트) - 타입: {type}, 갯수: {dataList.Count}개");
        }

        public void OnServerDataSetup()
        {
            SaveFileDatas data = JsonHelper.FromJson<SaveFileDatas>(_serverData);

            if (_loadData[EDataType.TrainingData] != null) 
            {
                _loadData[EDataType.TrainingData].Clear();
            }

            Dictionary<string, DataBase> dict = new Dictionary<string, DataBase>();
            dict.Add(data.Id, data);

            _loadData[EDataType.TrainingData] = dict;

            //서버로부터 받은 트레이닝 데이터를 셋업한다.
            _isServerData = true;
           LoadingHelper.RegisterLoading(new Loading(OnServerDataLoadingAsync));
        }


        public void OnServerDataSetup(SaveFileDatas data)
        {
            if (_loadData[EDataType.TrainingData] != null)
            {
                _loadData[EDataType.TrainingData].Clear();
            }

            Dictionary<string, DataBase> dict = new Dictionary<string, DataBase>();
            dict.Add(data.Id, data);

            _loadData[EDataType.TrainingData] = dict;

            //서버로부터 받은 트레이닝 데이터를 셋업한다.
            _isServerData = true;
            LoadingHelper.RegisterLoading(new Loading(OnServerDataLoadingAsync));
        }


        private void LoadTable<T>(EDataType dataType,string tableName,bool isLocal) where T : DataBase
        {
            Dictionary<string, DataBase> dict = new Dictionary<string, DataBase>();

            if (isLocal)
            {
                TextAsset[] jsonFiles = Resources.LoadAll<TextAsset>($"{Const.Path.BUILT_IN_LOCAL_DATA_PATH}{tableName}");

                foreach (TextAsset jsonFile in jsonFiles)
                {
                    Debug.Log($"jsonFile - {jsonFile.name}");

                    if (dataType == EDataType.TrainingData)
                    {
                        T data = JsonHelper.FromJson<T>(jsonFile.text);

                        if(data != null)
                        {
                            dict.Add(data.Id, data);

                        }
                    }
                    else
                    {
                        List<T> dataList = JsonHelper.FromJsonList<T>(jsonFile.text);

                        foreach (T data in dataList)
                        {
                            if (dict.ContainsKey(data.Id))
                            {
                                Debug.LogError($"[LoadTable] 중복 ID 발생! Table: {tableName}, ID: {data.Id}");
                                continue;
                            }

                            dict.Add(data.Id, data);
                        }
                    }
                }
            }
            else
            {
                Debug.Log($"서버 보관용 {dataType}을 생성");
            }


            if (_loadData.ContainsKey(dataType))
                _loadData[dataType] = dict;
            else
                _loadData.Add(dataType, dict);

            
        }
        public T GetData<T>(EDataType type , string id) where T : DataBase
        {
            if (_loadData == null) return null;

            if (_loadData.TryGetValue(type, out var dict))
            {
                if (dict.TryGetValue(id, out var data))
                {
                    return data as T;
                }
            }

            return null;
        }
        public List<T> GetAllData<T>(EDataType type) where T : DataBase
        {

            if (_loadData == null) return null;

            if (_loadData.TryGetValue(type, out var dict))
            {
                List<T> list = new List<T>();
                foreach (var item in dict.Values)
                {
                    list.Add(item as T);
                }
                return list;
            }

            return null;
        }

        public string GetDisplayName(string id)
        {
            StringData data = GetData<StringData>(EDataType.StringData, id);

            if (data == null)
            {
                Debug.Log($"{id}를찾을 수 없습니다.");
                return null;
            }


            return data.displayName;
        }


    }
}
