using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;


namespace TRAINEE
{
    public abstract class MapControllerBase : MonoBehaviour, IEvent
    {
        [Header("Map Setting Roots")]
        [SerializeField] protected Transform _buildingRoot    = null;
        [SerializeField] protected Transform _terrainRoot     = null;
        [SerializeField] protected Transform _startPonitRoot  = null;

        protected PlayerController _player = null;                                                  // 플레이어
        // View
        protected Dictionary<string, IMapView> _views = new Dictionary<string, IMapView>();     // 맵에 세팅 된 모든 모델 뷰

        // Model
        protected Dictionary<string,MapModelBase> _models = new Dictionary<string,MapModelBase>();  // 맵에 세팅 된 모든 모델 순수 데이터

        protected Dictionary<string, DynamicProp[]> _dynamicProp = new Dictionary<string,DynamicProp[]>();
        public Dictionary<string, MapModelBase> GetMapModels { get => _models; }

        public DynamicPropData[] GetDynamicProps 
        { 
            get
            {
                if (_dynamicProp == null || _dynamicProp.Count == 0) return null;

                List<DynamicPropData> changedList = new List<DynamicPropData>();

                // 딕셔너리에 등록된 모든 뷰(View)의 프롭들을 순회
                foreach (var kvp in _dynamicProp)
                {
                    string viewId = kvp.Key;
                    DynamicProp[] props = kvp.Value;

                    if (props == null) continue;

                    for (int i = 0; i < props.Length; i++)
                    {
                        DynamicProp prop = props[i];

                        // 🌟 핵심: 프롭이 존재하고, Rigidbody가 잠들어있지 않은(움직이는) 경우만 수집
                        if (prop != null && prop.Rb != null && !prop.Rb.IsSleeping())
                        {
                            changedList.Add(new DynamicPropData
                            {
                                viewId = viewId,
                                propIndex = i,
                                position = prop.transform.localPosition, // 부모(View) 기준 로컬 좌표
                                rotation = prop.transform.localRotation
                            });
                        }
                    }
                }

                return changedList.Count > 0 ? changedList.ToArray() : null;
            }
        }

        public void ApplyDynamicProps(DynamicPropData[] datas)
        {
            if (datas == null) return;

            foreach (var data in datas)
            {
                // 1. viewId로 해당 묶음 찾기
                if (_dynamicProp.TryGetValue(data.viewId, out DynamicProp[] props))
                {
                    // 2. 인덱스 범위 확인
                    if (data.propIndex >= 0 && data.propIndex < props.Length)
                    {
                        DynamicProp target = props[data.propIndex];
                        if (target != null)
                        {
                            // 3. 서버의 로컬 좌표 적용 (클라이언트는 IsKinematic이므로 즉시 반영)
                            target.transform.localPosition = data.position;
                            target.transform.localRotation = data.rotation;
                        }
                    }
                }
            }
        }
        public virtual void Init()
        {
            if(_views.Count != 0)
                _views.Clear();

            if(_models.Count != 0)
                _models.Clear();

            if(_dynamicProp.Count != 0)
                _dynamicProp.Clear();


            LoadingHelper.RegisterLoading(new Loading(LoadingAsync));
            LoadingHelper.RegisterLoading(new Loading(LoadingEquipAsync));


            GameEventManager.Instance.Subscribe(EEventType.Interaction, this);
            GameEventManager.Instance.Subscribe(EEventType.UpdateView, this);
            GameEventManager.Instance.Subscribe(EEventType.Server, this);
        }

        public void OnNetworkEventReceived(GameModelEventBase eventData)
        {
            Debug.Log($"[Client] 상호작용 결과: {eventData._id} / {eventData.isServerConfirmed}");

            GameEvent localEvent = new GameEvent();
            localEvent._id = eventData._id;
            localEvent._type = EEventType.Interaction; // 다시 모델로 보냄!
            localEvent._param = eventData;

            GameEventManager.Instance.Publish(localEvent);
        }

        public void RequestServerValidation(GameModelEventBase requestData)
        {
            if (requestData != null)
            {
                Debug.Log($"[Client RequestServerValidation] : {requestData._id} / {requestData.isServerConfirmed}");
                GameManager.Instance.GetSystem<MapSystem>()?.RequestInteract(requestData);
            }
        }

        public GameModelEventBase ValidateEventOnServer(GameModelEventBase requestData)
        {
            Debug.Log($"_models count : {_models.Count}");
            Debug.Log($"requestData id : {requestData._id}");
            if (_models.TryGetValue(requestData._id, out MapModelBase model))
            {
                Debug.Log($"[Server] 상호작용 요청: {requestData._id}");
                return model.EventData(requestData);
            }
            return null;
        }

        public virtual void RegisterDynamicProp(string id, DynamicProp[] dynamicProps)
        {
            if(!_dynamicProp.ContainsKey(id))
            {
                _dynamicProp.Add(id, dynamicProps);
            }
            else
            {
                Debug.Log($"Same MapModel ID :{id}");
            }
        }

        public virtual void RegisterView(string id, IMapView view)
        {
            if(!_views.ContainsKey(id))
            {
                _views.Add(id, view);
            }
            else
            {
                Debug.Log($"Same MapModel ID :{id}");
            }
        }

        public virtual void UnRegisterView(string id, IMapView view)
        {
            if (_views.ContainsKey(id))
            {
                if (_views[id] == view)
                {
                    _views.Remove(id);
                }
                else
                {
                    Debug.Log($"[MapSystem] 해제 경고: ID({id})는 존재하지만 등록된 View 객체가 다릅니다.");
                }
            }
            else
            {
                Debug.Log($"[MapSystem] 해제 실패: 등록되지 않은 MapModel ID입니다. (ID: {id})");
            }
        }

        public virtual void RegisterModel(string id, MapModelBase model)
        {
            if (!_models.ContainsKey(id))
            {
                _models.Add(id, model);
            }
            else
            {
                Debug.LogWarning($"Same MapModel ID :{id}");
            }
        }

        public virtual void OnUpdate(float deltaTime)
        {
            foreach (var view in _views.Values)
            {
                view?.OnUpdate(deltaTime);
            }
        }

        public virtual void OnFixedUpdate(float deltaTime)
        {
            foreach (var model in _models.Values)
            {
                model?.OnFixedUpdate(deltaTime);
            }
        }

        public virtual void OnEvent(GameEvent data)
        {
            switch (data._type)
            {
                case EEventType.Interaction:
                    OnModelUpdate(data);
                    break;
                case EEventType.UpdateView:
                    OnViewUpdate(data);
                    break;
                case EEventType.Server:
                    RequestServerValidation(data._param as GameModelEventBase);
                    break;
            }
        }

        protected virtual void OnModelUpdate(GameEvent data)
        {
            MapModelBase model = null;

            if (_models.TryGetValue(data._id, out model))
            {
                model.UpdateModel(data);
            }
        }

        protected virtual void OnViewUpdate(GameEvent data)
        {
            IMapView view = null;

            if (_views.TryGetValue(data._id, out view))
            {
                view.UpdateView(data);
            }
        }

        protected virtual void OnDestroy()
        {
            if (_views.Count != 0)
                _views.Clear();

            if (_models.Count != 0)
                _models.Clear();

            if (GameEventManager.isInstance)
            {
                GameEventManager.Instance.UnSubscribe(EEventType.Interaction, this);
                GameEventManager.Instance.UnSubscribe(EEventType.UpdateView, this);
                GameEventManager.Instance.UnSubscribe(EEventType.Server, this);
            }

        }


        /// <summary>
        /// 월드 진입 시 맵 세팅 및 초기화
        /// </summary>
        /// <param name="onProgress"></param>
        /// <param name="delaytime"></param>
        /// <returns></returns>

        public async UniTask LoadingAsync(Action<float, string> onProgress,int delaytime)
        {
            MapBuildSystem.Build(this, new Transform[] { _buildingRoot, _terrainRoot, _startPonitRoot });

            int totalCnt = _models.Count;
            int currentCnt = 0;

            foreach (var model in _models.Values)
            {
                model.Init();

                currentCnt++;
                float progress = (float)currentCnt / totalCnt;

                onProgress?.Invoke(progress, $"월드 생성 중...");

                await UniTask.Delay(0);
            }
        }


        /// <summary>
        /// 월드 진입 시 장비 세팅
        /// 네트워크 기반으로 변경 예정
        /// 피쉬넷 서버
        /// </summary>
        /// <param name="onProgress"></param>
        /// <param name="delaytime"></param>
        /// <returns></returns>
        public async UniTask LoadingEquipAsync(Action<float, string> onProgress, int delaytime)
        {
#if GAMEINSTANCE
            int totalCnt = 0;
            int currentCnt = 0;

            List<EquipSpawnPointData> datas = DataManager.Instance.GetAllData<EquipSpawnPointData>(EDataType.EquipSpawnPointData);

            totalCnt = datas.Count;
            for (int i = 0; i < datas.Count; i++)
            {
                if (datas[i].scenario == GameManager.Instance.GetCurrentScenario)
                {
                    string id = datas[i].equipid;
                    Vector3 position = new Vector3(datas[i].PosX, datas[i].PosY, datas[i].PosZ);
                    Quaternion rotation = new Quaternion(datas[i].RotX, datas[i].RotY, datas[i].RotZ,1f);
                    ItemManager.Instance.ServerSpawnItem(id,"",position, rotation);
                }

                currentCnt++;
                float progress = (float)currentCnt / totalCnt;

                onProgress?.Invoke(progress, $"훈련 장비 준비 중...");
                await UniTask.Delay(delaytime);
            }
#elif DEV_MODE
            int totalCnt = 0;
            int currentCnt = 0;

            List<EquipSpawnPointData> datas = DataManager.Instance.GetAllData<EquipSpawnPointData>(EDataType.EquipSpawnPointData);

            totalCnt = datas.Count;
            for (int i = 0; i < datas.Count; i++)
            {
                if (datas[i].scenario == GameManager.Instance.GetCurrentScenario)
                {
                    string id = datas[i].equipid;
                    Vector3 position = new Vector3(datas[i].PosX, datas[i].PosY, datas[i].PosZ);
                    Quaternion rotation = new Quaternion(datas[i].RotX, datas[i].RotY, datas[i].RotZ, 1f);
                    ItemManager.Instance.LocalSpawnWorldItem(id, position, rotation);
                }

                currentCnt++;
                float progress = (float)currentCnt / totalCnt;

                onProgress?.Invoke(progress, $"{datas[i].equipid} 장비 로드 및 초기화");
                await UniTask.Delay(delaytime);
            }
#else
           // ItemManager.Instance.ClearLobbyItem();
            await UniTask.Delay(delaytime);
            onProgress?.Invoke(1f, $"훈련 장비 준비 중...");
#endif
        }
    }
}
