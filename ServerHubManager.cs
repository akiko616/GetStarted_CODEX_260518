using Cysharp.Threading.Tasks;
using Disaster.Network.FTP;
using NaughtyAttributes;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TRAINEE;
using UnityEngine;

namespace DrillSergeant
{
    public class ServerHubManager : Singleton<ServerHubManager>
    {
        private List<SaveFileDatas> _ftpDatas = new();
        private const int ftpCount = 10; // 근래의 ftp 데이터 가져올 갯수

        public List<SaveFileDatas> FTPDatas => _ftpDatas;


        [ReadOnly] public string mainLoginedInput = null;

        [Header("Local FTP Use")]
        [SerializeField] private bool _isOriginFtpLocalUse = true;
        private const string _localFilePath = "Json/SaveData/SergeantLocal"; // origin FTP 파일을 로컬로 받을때 경로 

        protected override void Awake()
        {
            base.Awake();
        }

        protected override void Start()
        {
            base.Start();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }

        //private async void OnApplicationQuit()
        //{
        //    await NetworkManager.Instance.FTPManager.DisconnectAsync();
        //    Debug.Log("군재난대응 교관석 프로그램을 종료");
        //}

        public async UniTask FTPDownLoading(Action<float> onProgress)
        {
            onProgress?.Invoke(0);
            _ftpDatas.Clear();

            if (_isOriginFtpLocalUse)
                _ftpDatas.AddRange(LoadAllDataFromLocal(-1)); // -1일경우 전부

            else
            {
                List<SaveFileDatas> downloadedData = await LoadAllDataFromFtpAsync(ftpCount);
                _ftpDatas.AddRange(downloadedData);
            }

            onProgress?.Invoke(0.7f);

            await UniTask.Delay(100);

            onProgress?.Invoke(1f);
        }


        #region FTP Download & Upload
        private List<SaveFileDatas> LoadAllDataFromLocal(int takeCount)
        {
            List<SaveFileDatas> dataList = new List<SaveFileDatas>();

            string localDir = Path.Combine(Application.streamingAssetsPath, _localFilePath);

            string[] files = Directory.GetFiles(localDir, "*.json");
            IEnumerable<string> targetFiles = files.OrderByDescending(f => f);

            if (takeCount != -1)
            {
                targetFiles = targetFiles.Take(takeCount);
            }

            foreach (string filePath in targetFiles)
            {
                string jsonContent = File.ReadAllText(filePath);

                if (!string.IsNullOrEmpty(jsonContent))
                {
                    SaveFileDatas data = JsonHelper.FromJson<SaveFileDatas>(jsonContent);
                    if (data != null)
                    {
                        dataList.Add(data);
                        Debug.Log($"[Local Load] <color=cyan>로컬 데이터 로드 완료: {Path.GetFileName(filePath)}</color>");
                    }
                }
            }
            return dataList;
        }

        private async UniTask<List<SaveFileDatas>> LoadAllDataFromFtpAsync(int takeCount)
        {
            List<SaveFileDatas> dataList = new List<SaveFileDatas>();

            try
            {
                List<string> files = await NetworkManager.Instance.FTPManager.ListAsync(FtpContentType.OriginScenario);

                foreach (string fileName in files)
                {
                    string jsonContent = await NetworkManager.Instance.FTPManager.DownloadStringAsync(FtpContentType.OriginScenario, fileName);

                    if (!string.IsNullOrEmpty(jsonContent))
                    {
                        SaveFileDatas data = JsonHelper.FromJson<SaveFileDatas>(jsonContent);
                        if (data != null) dataList.Add(data);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FTP Load Error] {ex.Message}");
            }

            return dataList;
        }

        public async UniTask UploadSetDataToFTPAsync(DrillUnit drillUnit)
        {
            string fileName = drillUnit.SetParty.SceneSetFile;

            // Vector3, Queternion 변수에 대한 normalized 무한 루프 대비 제이슨 셋팅
            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                Converters = new List<JsonConverter>
                {
                     new Vector3Converter(),
                     new QuaternionConverter()
                }
            };

            string jsonString = JsonConvert.SerializeObject(drillUnit.CopiedData, settings);
            Debug.Log($"[PartyConfirming]카피드데이터: {drillUnit.CopiedData},    씬셋파일: {drillUnit.SetParty.SceneSetFile}");
            Debug.Log($"{jsonString}");
            await NetworkManager.Instance.FTPManager.UploadStringAsync(FtpContentType.ConfirmScenario, jsonString, fileName);
        }

        // CopiedData 최종점검 (요구조자 관련)
        public void ValidateCopiedData(DrillUnit drillUnit)
        {
            TrainingDatas trainingData = drillUnit.CopiedData.trainingData;

            if (trainingData.rescueeDatas != null)
            {
                trainingData.rescueeDatas.RemoveAll(x => x.rescueeType == ERescueeType.None);
                trainingData.rescueeCount = trainingData.rescueeDatas.Count.ToString();
            }
        }
        #endregion

        #region Clone
        public SaveFileDatas Clone(SaveFileDatas _ftpDatas)
        {
            if (_ftpDatas == null) return null;

            SaveFileDatas newNode = new SaveFileDatas();
            newNode.Id = _ftpDatas.Id;
            newNode.fileDate = _ftpDatas.fileDate;
            newNode.trainingVersion = _ftpDatas.trainingVersion;

            TrainingDatas beforeData = _ftpDatas.trainingData;
            TrainingDatas newData = newNode.trainingData;

            if (beforeData != null)
            {
                newData.Id = beforeData.Id;
                newData.scenarioNum = beforeData.scenarioNum;
                newData.scenarioName = string.IsNullOrEmpty(beforeData.scenarioName) ? "NoNamedScenario" : beforeData.scenarioName;
                newData.scenarioDescription = string.IsNullOrEmpty(beforeData.scenarioDescription) ? "NoDescription" : beforeData.scenarioDescription;
                newData.scenarioCode = beforeData.scenarioCode;
                newData.scenarioGoals = string.IsNullOrEmpty(beforeData.scenarioGoals) ? "대피/수색/보고" : beforeData.scenarioGoals;
                newData.scenarioStory = string.IsNullOrEmpty(beforeData.scenarioStory) ? "NoStory" : beforeData.scenarioStory;
                newData.scenarioTime = string.IsNullOrEmpty(beforeData.scenarioTime) ? "0" : beforeData.scenarioTime;
                newData.scenarioWeather = string.IsNullOrEmpty(beforeData.scenarioWeather) ? "0" : beforeData.scenarioWeather;
                newData.rescueeCount = beforeData.rescueeDatas.Count.ToString();


                string peopleCount = beforeData.scenarioPeople;
                if (string.IsNullOrEmpty(peopleCount))
                {
                    peopleCount = ScenarioRoleData.Instance.GetTraineeRoles(beforeData.scenarioNum).Count().ToString();
                }
                newData.scenarioPeople = peopleCount;

                // 터레인 셋팅
                if (beforeData.terrainsetData != null)
                {
                    newData.terrainsetData = new TerrainsetData();
                    newData.terrainsetData.Id = beforeData.terrainsetData.Id;
                    newData.terrainsetData.scenario = beforeData.terrainsetData.scenario;
                    newData.terrainsetData.time = beforeData.terrainsetData.time;
                    newData.terrainsetData.sun = beforeData.terrainsetData.sun;
                    newData.terrainsetData.fog = beforeData.terrainsetData.fog;
                    newData.terrainsetData.rain = beforeData.terrainsetData.rain;
                    newData.terrainsetData.snow = beforeData.terrainsetData.snow;
                    newData.terrainsetData.cloud = beforeData.terrainsetData.cloud;
                    newData.terrainsetData.weather = beforeData.terrainsetData.weather;
                }

                // 룸엘리먼트 셋팅
                newData.roomElementDatas = new List<RoomElementData>();
                if (beforeData.roomElementDatas != null)
                {
                    foreach (RoomElementData roomElement in beforeData.roomElementDatas)
                    {
                        newData.roomElementDatas.Add(new RoomElementData()
                        {
                            Id = roomElement.Id,
                            element = roomElement.element,
                            displayName = roomElement.displayName,
                            roomElementType = roomElement.roomElementType,
                            roomState = roomElement.roomState,
                            property = roomElement.property
                        });
                    }
                }

                // 시나리오 무비 셋팅
                if (beforeData.scenariomovieData != null)
                {
                    newData.scenariomovieData = new ScenariomovieData();
                    newData.scenariomovieData.Id = beforeData.scenariomovieData.Id;
                    newData.scenariomovieData.scenario = beforeData.scenariomovieData.scenario;

                    newData.scenariomovieData.dialogType = beforeData.scenariomovieData.dialogType != null ? new List<EDialogType>(beforeData.scenariomovieData.dialogType) : new List<EDialogType>();
                    newData.scenariomovieData.movieids = beforeData.scenariomovieData.movieids != null ? new List<string>(beforeData.scenariomovieData.movieids) : new List<string>();
                    newData.scenariomovieData.speachids = beforeData.scenariomovieData.speachids != null ? new List<string>(beforeData.scenariomovieData.speachids) : new List<string>();
                    newData.scenariomovieData.edupopups = beforeData.scenariomovieData.edupopups != null ? new List<string>(beforeData.scenariomovieData.edupopups) : new List<string>();
                    newData.scenariomovieData.edupopups2 = beforeData.scenariomovieData.edupopups2 != null ? new List<string>(beforeData.scenariomovieData.edupopups2) : new List<string>();
                }

                // 무비 스피치 셋팅
                newData.moviespeachDatas = new List<MoviespeachData>();
                if (beforeData.moviespeachDatas != null)
                {
                    foreach (MoviespeachData movieSpeach in beforeData.moviespeachDatas)
                    {
                        newData.moviespeachDatas.Add(new MoviespeachData()
                        {
                            Id = movieSpeach.Id,
                            speachid = movieSpeach.speachid,
                            characterName = movieSpeach.characterName != null ? new List<string>(movieSpeach.characterName) : new List<string>(),
                            speechs = movieSpeach.speechs != null ? new List<string>(movieSpeach.speechs) : new List<string>(),
                            starttime = movieSpeach.starttime != null ? new List<int>(movieSpeach.starttime) : new List<int>(),
                            endtime = movieSpeach.endtime != null ? new List<int>(movieSpeach.endtime) : new List<int>()
                        });
                    }
                }

                // 이벤트 오브젝트 셋팅
                newData.eventObjectDatas = new List<EventObjectData>();
                if (beforeData.eventObjectDatas != null)
                {
                    foreach (EventObjectData eventObject in beforeData.eventObjectDatas)
                    {
                        newData.eventObjectDatas.Add(new EventObjectData()
                        {
                            //id = (eventObject as DataBase)?.Id,
                            id = eventObject.id,
                            property = eventObject.property
                        });
                    }
                }

                // 레스큐세이브 데이터 셋팅
                newData.rescueeDatas = new List<RescueeSaveData>();
                if (beforeData.rescueeDatas != null)
                {
                    foreach (RescueeSaveData rescueeSave in beforeData.rescueeDatas)
                    {
                        RoomSpawnData localData = DataManager.Instance.GetData<RoomSpawnData>(EDataType.RoomSpawnData, rescueeSave.Id);

                        newData.rescueeDatas.Add(new RescueeSaveData()
                        {
                            Id = rescueeSave.Id,
                            scenario = rescueeSave.scenario,
                            rescueeid = rescueeSave.rescueeid,
                            roomid = rescueeSave.roomid,
                            rescueeType = rescueeSave.rescueeType,
                            rescueeState = rescueeSave.rescueeState,
                            spawnPosition = new Vector3(localData.posX, localData.posY, localData.posZ),
                            spawnRotation = Quaternion.Euler(localData.rotX, localData.rotY, localData.rotZ)
                        });
                    }
                }
            }

            return newNode;
        }
        #endregion
    }

    public enum ENetworkState
    {
        None,
        Requesting,
        Success,
        Failed
    }

}
