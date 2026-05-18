using Cysharp.Threading.Tasks;
using FishNet;
using System;
using System.Collections.Generic;
using UnityEngine;


namespace TRAINEE
{
    public class DisasterSystem : SystemBase, IEvent
    {
        private Dictionary<string, DisasterViewBase> _disasterDict = new Dictionary<string, DisasterViewBase>();
        private List<DisasterViewBase> _disasters = new List<DisasterViewBase>();
        public override void Init()
        {
            if(_disasters == null)
            {
                _disasters = new List<DisasterViewBase>();
            }
            else
            {
                _disasters.Clear();
            }

            if(_disasterDict == null)
            {
                _disasterDict = new Dictionary<string, DisasterViewBase>();
            }
            else
            {
                _disasterDict.Clear();
            }

            base.Init();

#if GAMEINSTANCE
            GameEventManager.Instance.Subscribe(EEventType.Spawn, this);
#endif
        }

        public DisasterViewBase GetFindDisaster(string id)
        {
            DisasterViewBase view = null;
            if (_disasterDict.TryGetValue(id, out view))
            {
            }

            return view;
        }

        public override void OnGameStart()
        {
            base.OnGameStart();
        }

        public override void OnUpdate(float deltaTime)
        {
            for(int i = _disasters.Count - 1; i >= 0; i--)
            {
                if( _disasters[i] == null )
                {
                    continue;
                }

                _disasters[i].OnUpdate(deltaTime);
            }
        }

        public override void OnFixedUpdate(float deltaTime)
        {
        }

        public override void OnLateUpdate(float deltaTime)
        {
        }

        public override void OnDestroy()
        {
#if GAMEINSTANCE
            GameEventManager.Instance.UnSubscribe(EEventType.Spawn, this);
#endif
            _disasters.Clear();
            base.OnDestroy();
        }


        public void RegisterDisaster(string id,DisasterViewBase disaster)
        {
            if (disaster != null && !_disasterDict.ContainsKey(id))
            {
                disaster.transform.SetParent(this.transform, false);
                _disasterDict.Add(id, disaster);
                _disasters.Add(disaster);

                Debug.Log($"[시스템] 재난 - {id} 등록 완료.");
            }
        }

        public void UnRegisterDisaster(string id,DisasterViewBase disaster)
        {
            if (_disasterDict.ContainsKey(id))
            {
                _disasterDict.Remove(id);
                _disasters.Remove(disaster);
                Debug.Log($"[시스템] 재난 - {id} 해제 완료.");
            }
        }

        public override async UniTask LoadingAsync(Action<float, string> onProgress, int delaytime)
        {
            await UniTask.Delay(delaytime);

            float totalprogress = 1;
            onProgress?.Invoke(totalprogress, $"재난 로드 중...");
        }

        public void OnEvent(GameEvent data)
        {
#if GAMEINSTANCE
            switch(data._type)
            {
                case EEventType.Spawn:
                    {
                        SpawnDisasters();
                    }
                    break;
            }
#endif
        }

        private void SpawnDisasters()
        {
            Debug.Log("[서버] 로딩 완료! 재난 설치 시작");

            MapSystem mapSystem = GameManager.Instance.GetSystem<MapSystem>();

            foreach (var kvp in mapSystem.MapController.GetMapModels)
            {
                if (kvp.Value is DisasterModel disasterModel)
                {
                    DisasterSaveData data = disasterModel.GetDisaster;
                    Debug.Log($"[서버] 재난 id : {data.disasterid}");
                    string id = data.disasterid;
                    GameObject prefab = LdResources.Load<GameObject>($"1.Prefabs/7.Disaster/{id}");
                    GameObject instance = GameObject.Instantiate(prefab, this.transform);

                    DisasterViewBase disaster = instance.GetComponent<DisasterViewBase>();
                    if (disaster != null)
                    {
                        disaster.Init(id);

                        RegisterDisaster(id, disaster);

                        Debug.Log($"[서버] 재난 생성 완료");
                    }

                    var fishNetManager = InstanceFinder.ServerManager;
                    if (fishNetManager != null)
                    {
                        Debug.Log($"[서버] ->[클라] 재난 생성");
                        fishNetManager.Spawn(instance);
                    }
                }
            }
        }
    }
}
