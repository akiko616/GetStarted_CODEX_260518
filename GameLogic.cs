using Cysharp.Threading.Tasks;
using FishNet.Object;
using System.Collections.Generic;
using UnityEngine;

namespace TRAINEE
{
    // 1. 트레이닝 게임 메인 로직
    // 2. 월드 구축
    // 3. 씬이 넘어가기 전에 모든 데이터를 받은 상태라고 가정
    public class GameLogic : NetworkBehaviour
    {
        [SerializeField] private List<SystemBase> _systems = null;
        
#if GAMEINSTANCE
     FishNet.Managing.Timing.TimeManager _timeManager = null;   
#endif

        public void Init()
        {
            GameManager.Instance.GetGameStart = false;
            GameManager.Instance.Systems = _systems;

            for (int i = 0; i < _systems.Count; i++)
            {
                _systems[i].Init();
            }

#if !GAMEINSTANCE && !DrillSergeant
            VideoManager.Instance.VideoManagerSetup();
            GameManager.Instance.OnGameStop += GameStop;
            GameManager.Instance.OnGamePause += GamePause;
            GameManager.Instance.OnGameResult += GameResult;
#endif
            GameManager.Instance.OnGameStart += GameStart;
            
#if DEV_MODE
            if (TRAINEE.NetworkManager.Instance.TimeManaager != null)
            {
                TRAINEE.NetworkManager.Instance.TimeManaager.OnUpdate += OnUpdate;
                TRAINEE.NetworkManager.Instance.TimeManaager.OnFixedUpdate += OnFixedUpdate;
                TRAINEE.NetworkManager.Instance.TimeManaager.OnLateUpdate += OnLateUpdate;
            }
#endif

            Debug.Log("게임 로직 Init 완료");
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
#if !GAMEINSTANCE
            if (TRAINEE.NetworkManager.Instance.TimeManaager != null)
            {
                TRAINEE.NetworkManager.Instance.TimeManaager.OnUpdate += OnUpdate;
                TRAINEE.NetworkManager.Instance.TimeManaager.OnFixedUpdate += OnFixedUpdate;
                TRAINEE.NetworkManager.Instance.TimeManaager.OnLateUpdate += OnLateUpdate;
            }

#else
            
            if(_timeManager == null)
            {
               _timeManager = FishNet.InstanceFinder.TimeManager;
            }


            if(_timeManager != null)
            {
                _timeManager.OnUpdate +=OnUpdate;
                _timeManager.OnFixedUpdate +=OnFixedUpdate;
                _timeManager.OnLateUpdate +=OnLateUpdate;
            }
#endif
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();

#if !GAMEINSTANCE

            if (GameManager.isInstance)
            {
                GameManager.Instance.OnGameStop -= GameStop;
                GameManager.Instance.OnGamePause -= GamePause;
                GameManager.Instance.OnGameResult -= GameResult;
                GameManager.Instance.OnGameStart -= GameStart;
            }

            if (TRAINEE.NetworkManager.isInstance)
            {
                TRAINEE.NetworkManager.Instance.TimeManaager.OnUpdate -= OnUpdate;
                TRAINEE.NetworkManager.Instance.TimeManaager.OnFixedUpdate -= OnFixedUpdate;
                TRAINEE.NetworkManager.Instance.TimeManaager.OnLateUpdate -= OnLateUpdate;
            }
#else
            if(_timeManager != null)
            {
                _timeManager.OnUpdate -=OnUpdate;
                _timeManager.OnFixedUpdate -=OnFixedUpdate;
                _timeManager.OnLateUpdate -=OnLateUpdate;
            }
#endif
        }


        private void GameStart()
        {
            GameManager.Instance.OnGameStart -= GameStart;

            Debug.Log("GameStart");

            for (int i = 0; i < _systems.Count; i++)
            {
                _systems[i].OnGameStart();
            }

#if GAMEINSTANCE
            ItemManager.Instance.OnDropWorldItems();
#endif
        }


        public void OnUpdate()
        {
            if (!GameManager.Instance.GetGameStart)
            {
                return;
            }

            float time = Time.deltaTime;

            SystemBase exclusiveSystem = GetExclusiveSystem();

            if (exclusiveSystem != null)
            {
                exclusiveSystem.OnUpdate(time);
                return;
            }

            for (int i = 0; i < _systems.Count; i++)
            {
                _systems[i].OnUpdate(time);
            }
        }

        public void OnFixedUpdate()
        {
            if (!GameManager.Instance.GetGameStart)
            {
                return;
            }

            //float time = Time.fixedDeltaTime;
#if !GAMEINSTANCE
            float time = (float)TRAINEE.NetworkManager.Instance.TimeManaager.TickDelta;
#else
            float time = (float)_timeManager.TickDelta;
#endif

            SystemBase exclusiveSystem = GetExclusiveSystem();

            if (exclusiveSystem != null)
            {
                exclusiveSystem.OnFixedUpdate(time);
                return;
            }

            for (int i = 0; i < _systems.Count; i++)
            {
                _systems[i].OnFixedUpdate(time);
            }
        }

        public void OnLateUpdate()
        {
            if (!GameManager.Instance.GetGameStart)
            {
                return;
            }

            float time = Time.deltaTime;

            SystemBase exclusiveSystem = GetExclusiveSystem();

            if (exclusiveSystem != null)
            {
                exclusiveSystem.OnLateUpdate(time);
                return;
            }

            for (int i = 0; i < _systems.Count; i++)
            {
                _systems[i].OnLateUpdate(time);
            }
        }

        private SystemBase GetExclusiveSystem()
        {
            for (int i = 0; i < _systems.Count; i++)
            {
                if (_systems[i].IsExclusiveRunning)
                {
                   return _systems[i];
                }
            }

            return null;
        }

        private void GameStop()
        {
            Debug.Log("GameStop 호출됨");

            // 완료 팝업
            GameStopSequence().Forget(Debug.LogException);
        }

        private void GamePause()
        {
            Debug.Log("GamePasue 호출됨");

            // 게임 정지 팝업
            if (GameManager.Instance.GetGameStart)
            {
                UiManager.Instance.ShowLayer(ELayerType.UIPauseLayer).ContinueWith(layer =>
                {
                    var pause = layer as UIPauseLayer;
                    pause.SetupData(PauseType.Pause);
                }).Forget();
            }
            else
            {
                UIPauseLayer pauseLayer = UiManager.Instance.FindLayer<UIPauseLayer>(ELayerType.UIPauseLayer);
                pauseLayer.FadeOutHide();
            }
        }
        private async UniTask GameStopSequence()
        {
                LayerBase layer = await UiManager.Instance.ShowLayer(ELayerType.UIPauseLayer);
                if (layer is UIPauseLayer pause)
                {
                    pause.SetupData(PauseType.Stop);
                }

                await UniTask.Delay(1000);

                UiManager.Instance.AllHide();

                PopupBase popup = await UiManager.Instance.ShowPopup(EPopupType.UiEduResultPopup);

                //종료 데이터 얻는 방법이 정해지면 수정
                UiEduDialogPopup eduPopup = popup as UiEduDialogPopup;

                if (eduPopup == null)
                    return;
                

        }

        private void GameResult(string result)
        {
            //UiManager.Instance.AllHide();
            UiManager.Instance.Hide(ELayerType.UiInteractionLayer);
            UiManager.Instance.Hide(ELayerType.UiHudLayer);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            GameManager.Instance.GetSystem<ScenarioDialogSystem>().SetUpScenarioDialog(EDialogType.MissionAfter);

            UiEduDialogResultPopup popup = UiManager.Instance.FindPopup<UiEduDialogResultPopup>(EPopupType.UiEduResultPopup);

            // string result에 따라서 변경 예정
            popup.SetupEduDialogResult("");

            //UiManager.Instance.ShowPopup(EPopupType.UiEduResultPopup).Forget();

        }

    }
}
