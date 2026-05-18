using FishNet.Connection;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace TRAINEE
{
    /// <summary>
    /// 요구조자 컨트롤러
    /// </summary>
    public class RescueeController : ControllerBase, IInteractableView, IMapView
    {
        public struct RescueeSyncData
        {
            public ERescueeState State;
            public string TargetId;
        }

        protected readonly SyncVar<ERescueeType> _currentType = new SyncVar<ERescueeType>();
        protected readonly SyncVar<RescueeSyncData> _currenSyncData = new SyncVar<RescueeSyncData>();

        private Transform _target = null;
        public Transform TargetPlayer { get; private set; }
        public GameModelEventBase Param
        {
            get
            {
                RescueeEventData gameModelEventBase = new RescueeEventData();
                gameModelEventBase._id = base.Id;
                gameModelEventBase._rescueeId = base.Id;
                gameModelEventBase._rescueeType = _currentType.Value;
                gameModelEventBase._currentState = _currenSyncData.Value.State;
                return gameModelEventBase;

            }
        }

        Vector3 IInteractableView.Anchor => this.transform.position + (Vector3.up * 1.0f);

        public override void Init()
        {
            base.Init();
        }

        public void InitRescuee(string id, ERescueeType rescueeType)
        {
            base.Init(id);

            _currentType.Value = rescueeType;

            _currenSyncData.Value = new RescueeSyncData
            {
                State = ERescueeState.Undiscovered,
                TargetId = ""
            };

            _animationHandler = new AnimationHandler();
            _animationHandler.Init(_animator);
            for (int i = 0; i < characterInfo._anis.Length; i++)
            {
                EAnimParam type = characterInfo._anis[i]._eAniType;
                string hash = type.ToString();
                EAnimLayer layer = characterInfo._anis[i]._eLayer;

                _animationHandler.RegisterHash(type, hash, layer);
            }
            RegisterSystem();
            RegisterStates();

            _currenSyncData.OnChange += OnStateChanged;
        }

        public override void Setup()
        {
            base.Setup();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            Init();

            _animationHandler = new AnimationHandler();
            _animationHandler.Init(_animator);
            for (int i = 0; i < characterInfo._anis.Length; i++)
            {
                EAnimParam type = characterInfo._anis[i]._eAniType;
                string hash = type.ToString();
                EAnimLayer layer = characterInfo._anis[i]._eLayer;

                _animationHandler.RegisterHash(type, hash, layer);
            }

            RegisterSystem();
            RegisterStates();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();

            if (GameManager.isInstance)
            {
                GameManager.Instance.GetSystem<RescueeSystem>().UnregisterRescuee(base.Id, this);
                GameManager.Instance.GetSystem<MapSystem>().MapController.UnRegisterView(base.Id, this);
            }

            _target = null;
            TargetPlayer = null;
        }


        public override void OnDespawnServer(NetworkConnection connection)
        {
            base.OnDespawnServer(connection);

            Debug.Log($"[서버-Rescuee] 요구조자({_Id.Value})가 네트워크에서 Despawn 됩니다. 자가 정리 시작!");

            if (GameManager.isInstance)
            {
                RescueeSystem rescueeSystem = GameManager.Instance.GetSystem<RescueeSystem>();
                if (rescueeSystem != null)
                {
                    rescueeSystem.UnregisterRescuee(base.Id, this);
                }

                MapSystem mapSystem = GameManager.Instance.GetSystem<MapSystem>();
                if (mapSystem != null)
                {
                    mapSystem.MapController.UnRegisterView(base.Id, this); // (구현되어 있다면 호출)
                }
            }

            _target = null;
            TargetPlayer = null;

            if (_stateMachine != null)
            {
                base.ChangeState(EStateType.Idle); // FSM 변경
            }
        }

        private void RegisterSystem()
        {
            string rescueeId = _Id.Value;

            RescueeSystem rescueeSystem = GameManager.Instance.GetSystem<RescueeSystem>();
            rescueeSystem.RegisterRescuee(rescueeId, this);

            MapSystem mapSystem = GameManager.Instance.GetSystem<MapSystem>();
            mapSystem.MapController.RegisterView(rescueeId, this);

            Debug.Log($"[클라이언트] 서버가 보내준 요구조자({rescueeId}) 수신! 로컬 Model 생성 및 등록 완료.");
        }
        protected override void RegisterStates()
        {
            _states.Add(EStateType.Idle, new IdleState(this, EStateType.Idle));
            _states.Add(EStateType.Walk, new MoveState(this, EStateType.Walk));
            _states.Add(EStateType.Follow, new FollowState(this,EStateType.Follow));

            _stateMachine.Init(_states[EStateType.Idle]);
        }

        private void OnStateChanged(RescueeSyncData oldData, RescueeSyncData newData, bool asServer)
        {
            if (asServer)
            {
                return;
            }

            Debug.Log($"[Client Controller] SyncVar 수신! 화면 갱신 및 로컬 모델 동기화 시작: {newData.State}");



            if (!string.IsNullOrEmpty(newData.TargetId))
            {
                PlayerController player = GameManager.Instance.GetSystem<PlayerSpawnSystem>().GetPlayerById(newData.TargetId) as PlayerController;

                if (player != null)
                {
                    _target = player.transform;
                }
                else
                {
                    PlayerController_VR playerVR = GameManager.Instance.GetSystem<PlayerSpawnSystem>().GetPlayerById(newData.TargetId) as PlayerController_VR;
                    if (playerVR != null)
                    {
                        _target = playerVR.transform; 
                    }
                }
            }

            UpdateVisuals(newData.State);

            GameEvent gameEvent = new GameEvent();

            gameEvent._type = EEventType.UI;
            gameEvent._id = base.Id;
            gameEvent._param = this.Param;

            GameEventManager.Instance.Publish(gameEvent);

        }

        public override void OnUpdate(float deltaTime)
        {
            if (base.IsServerInitialized)
            {
                _movementBase.OnUpdate(deltaTime);

                if (_stateMachine._currentState != null)
                {
                    _stateMachine._currentState.OnUpdate(deltaTime);
                }
            }
        }
        public override void OnFixedUpdate(float deltaTime)
        {
            if (base.IsServerInitialized)
            {
                _movementBase.OnFixedUpdate(deltaTime);

                if (_stateMachine._currentState != null)
                {
                    _stateMachine._currentState.OnFixedUpdate(deltaTime);
                }
            }
        }

        public override void OnLateUpdate(float deltaTime)
        {
            if (base.IsServerInitialized)
            {
                _movementBase.OnLateUpdate(deltaTime);
            }
        }

        public void UpdateView(GameEvent data)
        {
            if (data._param is RescueeEventData rescueeData)
            {

                if (!string.IsNullOrEmpty(rescueeData._callerId))
                {
                    PlayerController player = GameManager.Instance.GetSystem<PlayerSpawnSystem>().GetPlayerById(rescueeData._callerId) as PlayerController;
                    if (player != null)
                    {
                        _target = player.transform;
                    }
                    else
                    {
                        PlayerController_VR playerVR = GameManager.Instance.GetSystem<PlayerSpawnSystem>().GetPlayerById(rescueeData._callerId) as PlayerController_VR;
                        if (playerVR != null)
                        {
                            _target = playerVR.transform;
                        }   
                    }
                }

                if (base.IsServerInitialized)
                {
                    Debug.Log($"[Server] 상태 시각화 업데이트: {rescueeData._currentState}");

                    _currenSyncData.Value = new RescueeSyncData
                    {
                        State = rescueeData._currentState,
                        TargetId = rescueeData._callerId
                    };

                    UpdateVisuals(rescueeData._currentState);

                }

                if (base.IsClientInitialized)
                {
                    Debug.Log($"[Client] 상태 시각화 업데이트: {rescueeData._currentState}");
                }
            }
        }

        private void UpdateVisuals(ERescueeState state)
        {
            switch (state)
            {
                case ERescueeState.Following:
                    if (_target != null)
                    {
                        this.TargetPlayer = _target;
                        base.ChangeState(EStateType.Follow); // FSM 변경
                        Debug.Log($"[연출] {_target.name}을(를) 따라갑니다!");
                    }
                    else
                    {
                        Debug.LogWarning("[연출 실패] Following 상태지만 타겟(_target)을 찾지 못했습니다.");
                    }
                    break;

                case ERescueeState.OnStretcher:
                    break;
            }
        }
    }
}
