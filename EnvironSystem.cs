using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TRAINEE
{
    public class EnvironSystem : SystemBase
    {

        private Volume _currentVolume = null;
        private List<EnvironBase> _currentEnvi = null;

        private float _currentTime = 0f;
        private float _cloudDensity = 0f;
        public override void Init()
        {
            if (_currentVolume != null)
                _currentVolume = null;

            if (_currentEnvi != null)
                _currentEnvi.Clear();
            else
                _currentEnvi = new List<EnvironBase>();

            base.Init();
        }

        public override void OnGameStart()
        {
            base.OnGameStart();

            Debug.Log("OnGameStart Environ System");

            EnvironmentSetup();
        }

        private void EnvironmentSetup()
        {
            for (int i = 0; i < _currentEnvi.Count; i++)
            {
                _currentEnvi[i].SetupWeather();

                if (_currentEnvi[i] is EnvironDay day)
                {
                    day.SetTimeData(_currentTime);
                }
                else if(_currentEnvi[i] is EnvironCloud cloud)
                {
                    cloud.SetCloudData(_currentTime, _cloudDensity);
                }
            }
        }

        public override void OnUpdate(float deltaTime)
        {
            if (!GameManager.Instance.GetGameStart)
            {
                return;
            }


            // 환경 요소 업데이트
            for (int i = 0; i < _currentEnvi.Count; i++)
            {
                _currentEnvi[i].OnUpdate(deltaTime);
            }
        }

        public override void OnFixedUpdate(float deltaTime)
        {
            if (!GameManager.Instance.GetGameStart)
            {
                return;
            }


            // 환경 요소 업데이트
            for (int i = 0; i < _currentEnvi.Count; i++)
            {
                _currentEnvi[i].OnFixedUpdate(deltaTime);
            }
        }

        public override void OnLateUpdate(float deltaTime)
        {
            if (!GameManager.Instance.GetGameStart)
            {
                return;
            }


            // 환경 요소 업데이트
            for (int i = 0; i < _currentEnvi.Count; i++)
            {
                _currentEnvi[i].OnLateUpdate(deltaTime);
            }
        }

        public override async UniTask LoadingAsync(Action<float, string> onProgress, int delaytime)
        {
            string id = GameManager.Instance.GetCurrentScenarioId;
            TerrainsetData localData = null;

            if (DataManager.Instance.IsServer)
            {
                localData = DataManager.Instance.GetData<TerrainsetData>(EDataType.ServerTerrainsetData, id);
            }
            else
            {
                localData = DataManager.Instance.GetData<TerrainsetData>(EDataType.TerrainsetData, id);
            }


            string buildingName = GameManager.Instance.GetCurrentBuilding.ToString();
            string basePath = $"{Const.Path.BUILT_IN_MAP_PATH}{buildingName}/Enviroment/";
            string volumName = $"Global Volume_{buildingName}";
            string volumPath = $"{basePath}{volumName}";
            string volumProfilPath = $"{basePath}Volume/{volumName}";


            // 시나리오 맞는 환경데이터 세팅
            _currentTime = float.Parse(localData.time);
            _cloudDensity = float.Parse(localData.cloud);

            _currentVolume = LdResources.Load<Volume>(volumPath,transform);

            // 시나리오 맞는 프로파일 로드
            VolumeProfile profile = LdResources.Load<VolumeProfile>(volumProfilPath);

            // 시나리오에 맞는 볼륨 세팅
            if(_currentVolume != null)
            {
                if (profile != null)
                {
                    _currentVolume.sharedProfile = profile;
                    _currentVolume.isGlobal = true;
                    _currentVolume.weight = 1.0f;
                    _currentVolume.priority = 100f;
                }
            }

            // 맵에 세팅 되어야하는 기본 환경 로드
            EEnvironType[] allTypes = (EEnvironType[])Enum.GetValues(typeof(EEnvironType));
            string environName = "Environ";
            string prefabBasePath = $"{basePath}{environName}";

            for (int i = 0; i < allTypes.Length; i++)
            {
                EEnvironType type = allTypes[i];

                if(type == EEnvironType.None)
                {
                    continue;
                }

               string prefabPath =$"{prefabBasePath}{type.ToString()}";

               CreateEnvironView(prefabPath);
            }


            // 세팅된 날씨에 따른 환경 로드

            EWeather weather = localData.weather;

            if (localData.weather != EWeather.None)
            {
                string prefabPath = $"{prefabBasePath}{weather.ToString()}";

                CreateEnvironView(prefabPath);
            }


            await UniTask.Delay(delaytime);

            float totalprogress = 1;

            onProgress?.Invoke(totalprogress, $"환경 데이터 로드 중...");
        }

        private void CreateEnvironView(string path)
        {
            EnvironViewBase view = LdResources.Load<EnvironViewBase>(path,transform);

            if(view != null)
            {
                EnvironBase env = CreateEnviron(view);

                if (env != null)
                {
                    env.Init();
                    _currentEnvi.Add(env);
                }
            }
        }

        private EnvironBase CreateEnviron(EnvironViewBase view)
        {
            switch (view)
            {
                case EnvironViewDay dayView:
                    return new EnvironDay(dayView, _currentVolume);
                case EnvironViewCloud cloudView:
                    return new EnvironCloud(cloudView, _currentVolume);
                case EnvironWeatherRain rainView:
                    return new EnvironRain(rainView, _currentVolume);
                case EnvironWeatherSunny sunnyView:
                    return new EnvironSunny(sunnyView, _currentVolume);
                case EnvironWeatherFog fogView:
                    return new EnvironFog(fogView, _currentVolume);
                case EnvironWeatherSnow snow:
                    return new EnvironSnow(snow, _currentVolume);

                default:
                    Debug.LogError($"[Error] 매칭 실패! 들어온 타입: {view.GetType().Name}");
                    return null;
            }
        }

        public bool IsCurrentNight()
        {
            for (int i = 0; i < _currentEnvi.Count; i++)
            {
                if (_currentEnvi[i] is EnvironDay day)
                {
                    return day.IsNight;
                }
            }
            return false;
        }
    }
}
