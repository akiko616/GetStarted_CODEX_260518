using Cysharp.Threading.Tasks;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.InputSystem;
using Cursor = UnityEngine.Cursor;


namespace TRAINEE
{

    public class PlayerController : ControllerBase, IEvent
    {
        private PlayerInputController _playerInputController = null;

        [SerializeField] private IkSetup _leftIk = null;
        [SerializeField] private TwoBoneIKConstraint _leftRigWeight = null;
        [SerializeField] private Transform _headBone = null;
        [SerializeField] private GameObject _headLight = null;

        [SerializeField] private MultiAimConstraint _spineAim = null;
        [SerializeField] private Transform _aimTarget = null;

        [SerializeField] private MultiAimConstraint _headAim = null;  
        [SerializeField] private Transform _lookTarget = null;        


        private float _camPitch = 0f;

        private readonly SyncVar<float> _headPitch = new SyncVar<float>();
        private PlayerFollwCam _playerCam = null;

        private readonly SyncVar<string> _playerRole = new SyncVar<string>();
        public string PlayerRole { get => _playerRole.Value; }
        public float HeadPitch { get => _headPitch.Value; }
        public Transform HeadBone { get => _headBone; }

        public override void Init(string Id)
        {
            base.Init(Id);
#if DEV_MODE || GAMEINSTANCE
            _animationHandler = new AnimationHandler();
            _collisionHandler = new CollisionHandler();
            _equipHandler = new EquipHandler();
            _ikHandler = new IKHandler();

            _collisionHandler.Init(this);
            _ikHandler.Init(this, _leftIk, _leftRigWeight, _spineAim,_aimTarget);
            _animationHandler.Init(_animator, _ikHandler);
            _equipHandler.Init(this);

            for (int i = 0; i < characterInfo._anis.Length; i++)
            {
                EAnimParam type = characterInfo._anis[i]._eAniType;
                string hash = type.ToString();
                EAnimLayer layer = characterInfo._anis[i]._eLayer;

                _animationHandler.RegisterHash(type, hash, layer);
            }

            _playerInputController = new PlayerInputController();
            _playerInputController.Player.Enable();
            _playerInputController.Player.LeftClick.performed += OnInteractPerformed;
            _playerInputController.Player.Focus.performed += OnMouseFocus;
            _playerInputController.Player.Swap.performed += OnSwapInput;

            IkSetting();
            RegisterStates();
            GameManager.Instance.GetSystem<PlayerSpawnSystem>().OnRegisterPlayer(_Id.Value, this);
           //GameEventManager.Instance.Subscribe(EEventType.Equip, this);
#endif

#if DEV_MODE
            _isOwner = true;
            this.GetComponent<NetworkTransform>().enabled = false;
            this.GetComponent<NetworkObject>().enabled = false;
            this.GetComponent<NetworkAnimator>().enabled = false;

            _interactionHandler = new InteractionHandler(Camera.main);
            _interactionHandler.Init(this);

            //GameManager.Instance.OnRegisterGamePlayer(_Id.Value, this);
#endif

        }

        public override void InitPlayer(string id, string role)
        {
            base.InitPlayer(id, role);
            _playerRole.Value = role;
        }

        public override void Setup()
        {
            base.Setup();

            EnvironSystem environ = GameManager.Instance.GetSystem<EnvironSystem>();
            if (environ != null)
            {
                bool isCurrentNight = environ.IsCurrentNight();
                _headLight.SetActive(isCurrentNight);
            }

        }
        public override void OnStartClient()
        {
            base.OnStartClient();
            
            Debug.Log($"[클라이언트] 캐릭터가 씬에 생성됨! (내 것인가? {base.IsOwner})");


            Debug.Log($"Player ID : {_Id.Value}");
            Debug.Log($"Player Role : {_playerRole.Value}");

            Init();

            _animationHandler = new AnimationHandler();
            _collisionHandler = new CollisionHandler();
            _equipHandler = new EquipHandler();
            _ikHandler = new IKHandler();


            _collisionHandler.Init(this);
            _ikHandler.Init(this, _leftIk, _leftRigWeight, _spineAim, _aimTarget);
            _animationHandler.Init(_animator, _ikHandler);
            _equipHandler.Init(this);

            for (int i = 0; i < characterInfo._anis.Length; i++)
            {
                EAnimParam type = characterInfo._anis[i]._eAniType;
                string hash = type.ToString();
                EAnimLayer layer = characterInfo._anis[i]._eLayer;

                _animationHandler.RegisterHash(type, hash, layer);
            }

            IkSetting();
            RegisterStates();

            _headLight.SetActive(false);

            if (!TRAINEE.NetworkManager.Instance.IsLobby)
                GameManager.Instance.GetSystem<PlayerSpawnSystem>().OnRegisterPlayer(_Id.Value, this);
            
            
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);
            Debug.Log("로컬 캐릭터 생성");
#if !GAMEINSTANCE

            _isOwner = base.IsOwner;

            if (_isOwner)
            {
                _playerCam = LdResources.Load<PlayerFollwCam>("1.Prefabs/4.Camera/Camera", this.transform);
                Debug.Log($"Player Camera : {_playerCam}");
                _playerCam.Init(_headBone);
                _movementBase.SetupCameraTrans(_playerCam.transform);

                _playerInputController = new PlayerInputController();
                _playerInputController.Player.Enable();
                _playerInputController.Player.LeftClick.performed += OnInteractPerformed;
                _playerInputController.Player.Focus.performed += OnMouseFocus;
                _playerInputController.Player.Swap.performed += OnSwapInput;

                _interactionHandler = new InteractionHandler(_playerCam.MainCamera);
                _interactionHandler.Init(this);
                GameEventManager.Instance.Subscribe(EEventType.Interaction, this);
                GameEventManager.Instance.Subscribe(EEventType.LocalAction, this);
                //GameEventManager.Instance.Subscribe(EEventType.Equip, this);

                if (!TRAINEE.NetworkManager.Instance.IsLobby)
                {
                    GameManager.Instance.GetSystem<PlayerSpawnSystem>().OnRegisterLocalPlayer(this);
                }
                else
                {
                    GameManager.Instance.GetSystem<LobbyPlayerSpawnSystem>().SpawnLocalPlayer(this);
                }

            }
#endif
        }

        protected override void RegisterStates()
        {
            _states.Add(EStateType.Idle, new IdleState(this, EStateType.Idle));
            _states.Add(EStateType.Walk, new MoveState(this, EStateType.Walk));
            _states.Add(EStateType.EquipAction, new EquipActionState(this, EStateType.EquipAction));
            _states.Add(EStateType.Approach, new ApproachState(this, EStateType.Approach));
            _stateMachine.Init(_states[EStateType.Idle]);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();

#if !GAMEINSTANCE
            if (_isOwner)
            {
                if (_playerInputController != null)
                {
                    _playerInputController.Player.Swap.performed -= OnSwapInput;
                    _playerInputController.Player.LeftClick.performed -= OnInteractPerformed;
                    _playerInputController.Player.Focus.performed -= OnMouseFocus;
                    _playerInputController.Player.Disable();
                    _playerInputController = null; 
                }

                if (GameEventManager.isInstance)
                {
                    GameEventManager.Instance.UnSubscribe(EEventType.Interaction, this);
                    GameEventManager.Instance.UnSubscribe(EEventType.LocalAction, this);
                }

                if (_playerCam != null)
                {
                    Destroy(_playerCam.gameObject);
                    _playerCam = null;
                }
            }

            if (!TRAINEE.NetworkManager.isInstance)
            {
                GameManager.Instance?.GetSystem<PlayerSpawnSystem>()?.OnUnRegisterPlayer(_Id.Value, this);
            }
#endif
        }

        public override void OnDespawnServer(NetworkConnection connection)
        {
            base.OnDespawnServer(connection);

            Debug.Log($"[Server] 플레이어({_Id.Value}) 서버 디스폰 처리 (Connection: {connection.ClientId})");

            if (!TRAINEE.NetworkManager.isInstance)
            {
                GameManager.Instance?.GetSystem<PlayerSpawnSystem>()?.OnUnRegisterPlayer(_Id.Value, this);
            }

        }
        public override void OnUpdate(float deltaTime)
        {
            base.OnUpdate(deltaTime);

            if (_isOwner)
            {
                OnMovement();
                OnLook(deltaTime);
            }

            _animationHandler?.OnUpdate(deltaTime);
            _ikHandler?.OnUpdate(deltaTime);

            UpdateAimTarget();
        }

        public override void OnFixedUpdate(float deltaTime)
        {
            base.OnFixedUpdate(deltaTime);

            if (_isOwner)
            {
                _interactionHandler?.OnFixedUpdate(deltaTime);
            }


            _collisionHandler?.OnFixedUpdate(deltaTime);
        }

        public override void OnLateUpdate(float deltaTime)
        {
            base.OnLateUpdate(deltaTime);

            if (_isOwner)
            {
                _playerCam?.OnLateUpdate(_camPitch);
            }

        }

        private void OnInteractPerformed(InputAction.CallbackContext context)
        {
#if !GAMEINSTANCE
            if (_isOwner)
            {
                base.ProcessCommand(new MLCCommand());
            }
#endif
        }

        private void OnMovement()
        {
#if !GAMEINSTANCE
            if (_isOwner)
            {
                Vector2 input = _playerInputController.Player.Move.ReadValue<Vector2>();

                base.ProcessCommand(new MoveCommand(_movementBase, input));
            }
#endif
        }

        private void OnLook(float deltaTime)
        {
#if !GAMEINSTANCE
            if (_isOwner)
            {
                Vector2 lookInput = Vector2.zero;

                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    lookInput = _playerInputController.Player.Look.ReadValue<Vector2>();
                }

                if (lookInput.sqrMagnitude > 0.01f)
                {
                    base.ProcessCommand(new LookCommand(_movementBase, lookInput));

                    //= _playerCam.MainCamera.transform.localEulerAngles.x;

                    _camPitch += lookInput.y * 15f * deltaTime;

                    _camPitch = Mathf.Clamp(_camPitch, -60f, 50f);

                    if (Mathf.Abs(_headPitch.Value - _camPitch) > 1f)
                    {
                        OnUpdateHeadPitch(_camPitch);
                    }
                }
            }
#endif
        }


        [ServerRpc]
        private void OnUpdateHeadPitch(float pitch)
        {
            _headPitch.Value = pitch;
        }

        private void UpdateAimTarget()
        {
            if (_lookTarget != null && _headBone != null)
            {
                float currentPitch = base.IsOwner ? _camPitch : _headPitch.Value;
                Quaternion aimRotation = Quaternion.Euler(-currentPitch, transform.eulerAngles.y, 0f);

                _lookTarget.position = _headBone.position + (aimRotation * Vector3.forward * 5f);
            }
        }

        private void OnMouseFocus(InputAction.CallbackContext context)
        {
#if !GAMEINSTANCE
            if (_isOwner)
            {
                if (context.performed)
                {
                    if (Cursor.lockState == CursorLockMode.Locked)
                    {
                        Cursor.lockState = CursorLockMode.None;
                        Cursor.visible = true;
                    }
                    else
                    {
                        Cursor.lockState = CursorLockMode.Locked;
                        Cursor.visible = false;
                    }
                }
            }
#endif
        }

        public void OnEvent(GameEvent data)
        {
            switch (data._type)
            {
                case EEventType.Interaction:
                    {
                        if (data._param is EquipEventData equipData)
                        {
                            if (base.IsOwner)
                            {
                                Debug.Log("아이템 착용 호출");
                                int slotidx = ItemManager.Instance.GetItemSlotIndex(equipData._id);
                                GameManager.Instance.GetSystem<PlayerSpawnSystem>().OnRequestEquipToServer(_Id.Value, equipData._id, equipData._instanceId, slotidx);
                            }
                        }

                        if (data._param is LobbyItemEventData lobbyItemData)
                        {
                            UiInteractionPopup popup = UiManager.Instance.FindPopup<UiInteractionPopup>(EPopupType.UiInteractionPopup);

                            if (!popup.IsShow) return;

                            switch (lobbyItemData._type)
                            {
                                case ELobbyItemType.Video:
                                    UiManager.Instance.FindPopup<UILobbyEquipPopup>(EPopupType.UILobbyEquipPopup).SetupData(lobbyItemData._id);
                                    UiManager.Instance.ShowPopup(EPopupType.UILobbyEquipPopup).Forget();
                                    break;

                                case ELobbyItemType.Text:
                                    TleeeduData eduData = DataManager.Instance.GetData<TleeeduData>(EDataType.TleeeduData, lobbyItemData._id);

                                    UiManager.Instance.FindPopup<UiEduDialogPopup>(EPopupType.UiEduGuidePopup).SetupEduDialog(eduData.title[0], eduData.speech[0], eduData.speech2[0], null);
                                    UiManager.Instance.ShowPopup(EPopupType.UiEduGuidePopup).Forget();
                                    break;

                            }
                            return;
                        }

                    }
                    break;
                case EEventType.LocalAction:
                    {

                        if (data._param is DisasterEventData disasterData)
                        {
                            if (base.IsOwner)
                            {
                                _pendingPayload = data._param;

                                if (_pendingPayload is DisasterEventData)
                                {
                                    ItemPrefabMapping? equipData = ItemManager.Instance.GetItemData(GetCurrentEquipId);
                                    float range = equipData.HasValue ? equipData.Value._actionDist : 1.5f;

                                    Transform target = GameManager.Instance.GetSystem<DisasterSystem>().GetFindDisaster(_pendingPayload._id).GetTrans;

                                    
                                    ApproachState approachState = _states[EStateType.Approach] as ApproachState;
                                    approachState.SetTarget(target, range);

                                    base.ChangeState(EStateType.Approach);

                                    //base.ChangeState(EStateType.EquipAction);
                                }
                            }
                        }
                    }
                    break;
            }
        }
        private void OnSwapInput(InputAction.CallbackContext context)
        {
#if !GAMEINSTANCE
            if (base.IsOwner)
            {
                // 🌟 기획에 따라 무기 슬롯 개수를 설정하세요.
                // 메인(0), 서브(1) 2개만 쓴다면 2로 설정.
                // 근접 무기(2)까지 쓴다면 3으로 설정.
                int maxSlots = 2;
                int currentSlotIndex = (int)_equipHandler.CurrentActiveSlot;
                // 인덱스를 1 증가시키고, 최대 개수를 넘어가면 다시 0으로 돌려보냅니다.
                int targetIndex = (currentSlotIndex + 1) % maxSlots;

                GameManager.Instance.GetSystem<PlayerSpawnSystem>().OnRequestEquipToServer(_Id.Value, targetIndex);

                Debug.Log($"[Input] 단일 스왑 키 눌림 ➡️ {currentSlotIndex}번 슬롯 장착 요청");
            }
#endif
        }


#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_collisionHandler != null)
                _collisionHandler.DrawGizmos();

            Gizmos.color = Color.yellow;

            // 실제 로직이 '카메라'에서 나가므로, 기즈모도 카메라 기준으로 그려야 정확합니다.
            Camera cam = Camera.main;

            // 에디터에서 플레이 중이 아닐 때 Camera.main을 못 찾을 수도 있으니 예외 처리
            if (cam == null) return;

            Vector3 origin = cam.transform.position;
            Vector3 direction = cam.transform.forward;

            // InteractionHandler에 하드코딩된 값과 맞춰주세요 (0.2f)
            float castRadius = _detectRadius;
            float distance = _detectDist;

            // (A) 중심선 그리기 (레이의 뼈대)
            Gizmos.DrawRay(origin, direction * distance);

            // (B) 끝 지점 구 그리기 (최대 사거리)
            Vector3 endPoint = origin + (direction * distance);
            Gizmos.DrawWireSphere(endPoint, castRadius);

            // (C) 시작 지점 구 그리기 (선택 사항)
            // 너무 눈앞에 그려져서 시야를 가린다면 주석 처리하세요.
            Gizmos.DrawWireSphere(origin + (direction * 0.5f), castRadius);
        }
#endif
    }
}
