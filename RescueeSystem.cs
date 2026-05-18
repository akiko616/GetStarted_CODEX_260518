using Cysharp.Threading.Tasks;
using DrillSergeant;
using FishNet;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TRAINEE
{
    /// <summary>
    /// 요구조자 관리 시스템
    /// </summary>

    public class RescueeSystem : SystemBase, IEvent
    {
        private Dictionary<string, RescueeController> _rescueeDict = new Dictionary<string, RescueeController>();
        private List<RescueeController> _rescuees = new List<RescueeController>();

        public override void Init()
        {
            if (_rescuees == null)
            {
                _rescuees = new List<RescueeController>();
            }
            else
            {
                _rescuees.Clear();
            }

            if (_rescueeDict == null)
            {
                _rescueeDict = new Dictionary<string, RescueeController>();
            }
            else
            {
                _rescueeDict.Clear();
            }

            base.Init();
#if GAMEINSTANCE
            GameEventManager.Instance.Subscribe(EEventType.Spawn, this);
#endif
        }



        public override void OnGameStart()
        {
            base.OnGameStart();

            for (int i = _rescuees.Count - 1; i >= 0; i--)
            {
                if (_rescuees[i] == null)
                {
                    continue;
                }

                _rescuees[i].Setup();
            }
        }

        public override void OnUpdate(float deltaTime)
        {
            for (int i = _rescuees.Count - 1; i >= 0; i--)
            {
                if (_rescuees[i] == null)
                {
                    continue;
                }
                _rescuees[i].OnUpdate(deltaTime);
            }
        }

        public override void OnFixedUpdate(float deltaTime)
        {
            for (int i = _rescuees.Count - 1; i >= 0; i--)
            {
                if (_rescuees[i] == null)
                {
                    continue;
                }
                _rescuees[i].OnFixedUpdate(deltaTime);
            }
        }

        public override void OnLateUpdate(float deltaTime)
        {
            for (int i = _rescuees.Count - 1; i >= 0; i--)
            {
                if (_rescuees[i] == null)
                {
                    continue;
                }
                _rescuees[i].OnLateUpdate(deltaTime);
            }
        }

        public override void OnDestroy()
        {
#if GAMEINSTANCE
            GameEventManager.Instance.UnSubscribe(EEventType.Spawn, this);
#endif

            _rescueeDict.Clear();
            _rescuees.Clear();
            base.OnDestroy();
        }

        public void RegisterRescuee(string id, RescueeController rescuee)
        {
            if (_rescuees != null && !_rescueeDict.ContainsKey(id))
            {
                rescuee.transform.SetParent(this.transform, false);
                _rescueeDict.Add(id, rescuee);
                _rescuees.Add(rescuee);

#if DrillSergeant
                // 요구조자 Layer 설정
                //MonitoringPoolSystem.Instance.SetLayerRecursively(rescuee.gameObject, LayerMask.NameToLayer("Minimap"));
#endif
                Debug.Log($"[시스템] 요구조자 {id} 등록 완료! (현재 총 {_rescueeDict.Count}명)");
            }
        }

        public void UnregisterRescuee(string id, RescueeController rescuee)
        {
            if (_rescueeDict.ContainsKey(id))
            {
                _rescueeDict.Remove(id);
                _rescuees.Remove(rescuee);
                Debug.Log($"[시스템] 요구조자 {id} 해제 완료.");
            }
        }

        public RescueeController GetRescueeById(string id)
        {
            if (_rescueeDict.TryGetValue(id, out RescueeController rescuee))
            {
                return rescuee;
            }
            return null;
        }



        public override async UniTask LoadingAsync(Action<float, string> onProgress, int delaytime)
        {

            await UniTask.Delay(delaytime);

            float totalprogress = 1;
            onProgress?.Invoke(totalprogress, $"요구조자 로드 중...");
        }



        public void OnEvent(GameEvent data)
        {
#if GAMEINSTANCE
            switch(data._type)
            {
                case EEventType.Spawn:
                    {
                        SpawnRescuees();
                    }
                    break;
            }
#endif
        }

        private void SpawnRescuees()
        {
            Debug.Log("[서버] 로딩 완료! 요구조자 스폰을 시작합니다.");

            MapSystem mapSystem = GameManager.Instance.GetSystem<MapSystem>();
            GameObject prefab = LdResources.Load<GameObject>("1.Prefabs/1.Characters/2.Rescuee/Rescuee");

            foreach (var kvp in mapSystem.MapController.GetMapModels)
            {
                if (kvp.Value is RescueeModel rescueeModel)
                {

                    RescueeSaveData data = rescueeModel.GetRescuee;
                    Debug.Log($"[서버] 요구조자 : id [{data.Id}]");
                    GameObject instance = GameObject.Instantiate(prefab, data.spawnPosition, data.spawnRotation, this.transform);

                    RescueeController rescuee = instance.GetComponent<RescueeController>();
                    if (rescuee != null)
                    {
                        Debug.Log($"[서버] 요구조자 생성 완료");
                        // 초기화
                        rescuee.InitRescuee(data.rescueeid, data.rescueeType);
                        RegisterRescuee(data.rescueeid, rescuee);
                    }

                    var fishNetManager = InstanceFinder.ServerManager;
                    if (fishNetManager != null)
                    {
                        Debug.Log($"[서버] ->[클라] 요구조자 생성");
                        fishNetManager.Spawn(instance);
                    }
                }
            }
        }
    }
}
