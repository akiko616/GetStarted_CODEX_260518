using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using TRAINEE;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Cysharp.Threading.Tasks;

public class PlayerController_VR : ControllerBase, IEvent
{
    private struct TrackingData
    {
        public bool valid;
        public Vector3 pos;
        public Quaternion rot;
        public bool forceSnap;
    }

    private XROrigin _xrOrigin;

    private readonly SyncVar<string> _playerRole = new SyncVar<string>();
    
    [SerializeField] private ContinuousMoveProvider _move = null;
    [SerializeField] private GameObject headLight = null;
    [SerializeField] private TwoBoneIKConstraint leftHandConstraint;
    [SerializeField] private TwoBoneIKConstraint rightHandConstraint;
    [SerializeField] private IkSetup leftIk = null;
    [SerializeField] private Rig _rig;
    [SerializeField] private Transform _headBone = null;


    [SerializeField]
    private Transform headTrackingSource;
    [SerializeField]
    private Transform LeftTrackingSource;
    [SerializeField]
    private Transform RightTrackingSource;

    PlayerFollwCam_VR _playerCam = null;

    public Transform leftHandIKTarget;
    public Transform rightHandIKTarget;
    public Transform headIKTarget;

    public Transform leftController;
    public Transform rightController;
    public Transform hmd;
    public Transform body;

    // (0: Position, 1: Rotation)
    public Vector3[] leftOffset = new Vector3[2];
    public Vector3[] rightOffset = new Vector3[2];
    public Vector3[] headOffset = new Vector3[2];

    // OpenXR controller devices
    private InputDevice _hmdDevice;
    private InputDevice _leftDevice;
    private InputDevice _rightDevice;

    private TrackingData _headData;
    private TrackingData _leftData;
    private TrackingData _rightData;

    bool wasTrackingHMD = true;
    bool wasTrackingLeft = true;
    bool wasTrackingRight = true;
    bool wasGripBtnPressed = false;
    bool wasPrimaryBtnPressed = false;

    int _currentSwapIndex = 0;

    float _deviceCheckTimer;

    private Quaternion _lastHeadRot;
    private Vector3 _lastHeadPos;
    public float _moveEpsilon = 0.001f;   // 약 1mm 이동 시 업데이트
    public float _rotateEpsilon = 0.05f;  // 약 0.05도 회전 시 업데이트
    private const float _bodyRotationThreshold = 5.0f; // 고개가 5도 이상 돌아가면 몸 회전

    public string PlayerRole { get => _playerRole.Value; }

    public override void Init(string Id)
    {
        base.Init(Id);

#if DEV_MODE || GAMEINSTANCE
            _animationHandler = new AnimationHandler();
            _collisionHandler = new CollisionHandler();
            _equipHandler = new EquipHandler();
            _ikHandler = new IKHandler();

            _collisionHandler.Init(this);
            _ikHandler.Init(this, leftIk, leftHandConstraint);
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

            GameManager.Instance.GetSystem<PlayerSpawnSystem>().OnRegisterPlayer(_Id.Value, this);
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
            headLight.SetActive(isCurrentNight);
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        Debug.Log($"[클라이언트] 캐릭터 생성 (내 것인가? {base.IsOwner})");


        Debug.Log($"Player ID : {_Id.Value}");
        Debug.Log($"Player Role : {_playerRole.Value}");

        Init();

        _animationHandler = new AnimationHandler();
        _collisionHandler = new CollisionHandler();
        _equipHandler = new EquipHandler();
        _ikHandler = new IKHandler();

        _collisionHandler.Init(this);
        _ikHandler.Init(this, leftIk, leftHandConstraint);
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

        headLight.SetActive(false);

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
            OwnerSetup_VR();
        }
#endif
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        Debug.Log("캐릭터 t스탑");
#if !GAMEINSTANCE

        if (_isOwner)
        {
            GameEventManager.Instance.UnSubscribe(EEventType.Interaction, this);
            GameEventManager.Instance.UnSubscribe(EEventType.Equip, this);

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
            UpdateInputDevices();
            CacheTrackingState();
        }

        _animationHandler?.OnUpdate(deltaTime);
        _ikHandler?.OnUpdate(deltaTime);
    }

    public override void OnFixedUpdate(float deltaTime)
    {
        base.OnFixedUpdate(deltaTime);

        if (_isOwner)
        {
            _interactionHandler?.OnFixedUpdate(deltaTime);
            OnInteraction(deltaTime);
        }

        _collisionHandler?.OnFixedUpdate(deltaTime);
    }

    public override void OnLateUpdate(float deltaTime)
    {
        base.OnLateUpdate(deltaTime);

        if (_isOwner)
        {
            if (_headData.valid)
            {
                UpdateVRTransforms(deltaTime);
                MappingBodyTransform(deltaTime);
            }
            OnMovement();
            UpdateRigWeight(deltaTime);


        }
        UpdateItemIKTracking();
    }

    void OwnerSetup_VR()
    {
        _playerCam = LdResources.Load<PlayerFollwCam_VR>("1.Prefabs/4.Camera/Camera_VR", this.transform.GetChild(0));
        _xrOrigin = GetComponent<XROrigin>();
        _playerCam.Init(gameObject.GetComponent<CharacterController>());
        _xrOrigin.Camera = _playerCam.MainCamera;
        hmd = _playerCam.MainCamera.transform;
        _move.forwardSource = _playerCam.MainCamera.transform;
        _movementBase.SetupCameraTrans(_playerCam.transform);
        gameObject.GetComponent<XRInputModalityManager>().enabled = true;

        _interactionHandler = new InteractionHandler(_playerCam.MainCamera);
        _interactionHandler.Init(this);

        if (leftHandIKTarget != null)
        {
            leftHandIKTarget.localPosition = leftOffset[0];
            leftHandIKTarget.localRotation = Quaternion.Euler(leftOffset[1]);
        }
        if (rightHandIKTarget != null)
        {
            rightHandIKTarget.localPosition = rightOffset[0];
            rightHandIKTarget.localRotation = Quaternion.Euler(rightOffset[1]);
        }

        SwitchToXRInput();

        GameEventManager.Instance.Subscribe(EEventType.Interaction, this);
        GameEventManager.Instance.Subscribe(EEventType.LocalAction, this);
        //GameEventManager.Instance.Subscribe(EEventType.Equip, this);

        UiManager.Instance.VRCameraRegisterUI(_playerCam.MainCamera);

        if (!TRAINEE.NetworkManager.Instance.IsLobby)
        {
            GameManager.Instance.GetSystem<PlayerSpawnSystem>().OnRegisterLocalPlayer(this);
        }
        else
        {
            GameManager.Instance.GetSystem<LobbyPlayerSpawnSystem>().SpawnLocalPlayer(this);
        }

    }

    private void UpdateInputDevices()
    {
        _deviceCheckTimer -= Time.deltaTime;
        if (_deviceCheckTimer > 0f) return;

        _deviceCheckTimer = 1f;

        if (!_hmdDevice.isValid)
            _hmdDevice = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);

        if (!_leftDevice.isValid)
            _leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

        if (!_rightDevice.isValid)
            _rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
    }

    void CacheTrackingState()
    {
        bool hmdTracking = IsTrackingVaildCheck(_hmdDevice);
        bool leftTracking = IsTrackingVaildCheck(_leftDevice);
        bool rightTracking = IsTrackingVaildCheck(_rightDevice);

        _headData.valid = hmdTracking;
        if (hmdTracking)
        {
            _headData.pos = headTrackingSource.position;
            _headData.rot = headTrackingSource.rotation;
            _headData.forceSnap = !wasTrackingHMD;
        }

        _leftData.valid = leftTracking;
        if (leftTracking)
        {
            _leftData.pos = LeftTrackingSource.position;
            _leftData.rot = LeftTrackingSource.rotation * Quaternion.Euler(leftOffset[1]);
            _leftData.forceSnap = !wasTrackingLeft;
        }

        _rightData.valid = rightTracking;
        if (rightTracking)
        {
            _rightData.pos = RightTrackingSource.position;
            _rightData.rot = RightTrackingSource.rotation * Quaternion.Euler(rightOffset[1]);
            _rightData.forceSnap = !wasTrackingRight;
        }

        wasTrackingHMD = hmdTracking;
        wasTrackingLeft = leftTracking;
        wasTrackingRight = rightTracking;
    }

    private void UpdateItemIKTracking()
    {
        if (string.IsNullOrEmpty(_equipHandler.CurrentActiveEquipId) || _equipHandler.CurrentEquip?.LeftHand == null) return;

        bool isEquipped = _equipHandler.CurrentActiveEquipId != "" && _equipHandler.CurrentEquip.LeftHand != null;

        if (isEquipped)
        {
            int layer = characterInfo.FindEquipAnimatorLayer(_equipHandler.CurrentActiveEquipId);

            _ikHandler.SetLayer(layer);

            _ikHandler.SetupLeftIk(_equipHandler.CurrentEquip.LeftHand, _equipHandler.CurrentEquip.LeftHandHint);

            _ikHandler.SetIKWeight(1f);
        }
    }

    private void UpdateRigWeight(float deltaTime)
    {
        float targetRigWeight = _headData.valid ? 1f : 0f;

        float t = 1f - Mathf.Exp(-10f * deltaTime);
        _rig.weight = Mathf.Lerp(_rig.weight, targetRigWeight, t);

        // 왼손/오른손 개별 가중치 설정
        if (leftHandConstraint != null)
        {
            float targetLeftWeight = _leftData.valid ? 1f : 0f;
            leftHandConstraint.weight = Mathf.Lerp(leftHandConstraint.weight, targetLeftWeight, t);
        }

        if (rightHandConstraint != null)
        {
            float targetRightWeight = _rightData.valid ? 1f : 0f;
            rightHandConstraint.weight = Mathf.Lerp(rightHandConstraint.weight, targetRightWeight, t);
        }
    }

    void MappingBodyTransform(float deltaTime)
    {
        if (hmd == null || body == null) return;

        Vector3 forward = hmd.forward;
        forward.y = 0;
        if (forward.sqrMagnitude < 0.01f) return;

        Quaternion targetRotation = Quaternion.LookRotation(forward);

        // 현재 몸 방향과 HMD 방향의 차이가 일정 각도 이상일 때만 Slerp 실행
        float angleDiff = Quaternion.Angle(body.rotation, targetRotation);

        if (angleDiff > _bodyRotationThreshold)
        {
            body.rotation = Quaternion.Slerp(body.rotation, targetRotation, deltaTime * 10f);
        }
    }

    private bool IsTrackingVaildCheck(InputDevice device)
    {
        if (!device.isValid) return false;

        if (device.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState state))
        {
            return (state & (InputTrackingState.Position | InputTrackingState.Rotation)) == (InputTrackingState.Position | InputTrackingState.Rotation);
        }
        return false;
    }
    protected override void RegisterStates()
    {
        _states.Add(EStateType.Idle, new IdleState(this, EStateType.Idle));
        _states.Add(EStateType.Walk, new MoveState(this, EStateType.Walk));
        _states.Add(EStateType.EquipAction, new EquipActionState(this, EStateType.EquipAction));
        _states.Add(EStateType.Approach, new ApproachState(this, EStateType.Approach));
        _stateMachine.Init(_states[EStateType.Idle]);
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
                            }
                        }
                    }
                }
                break;
        }
    }

    //이동 관련
    private void OnMovement()
    {
#if !GAMEINSTANCE

        if (!_leftDevice.isValid)
        {
            _leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        }

        if (_leftDevice.isValid && _leftDevice.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 leftAxis))
        {
            base.ProcessCommand(new MoveCommand(_movementBase, leftAxis));
        }
#endif
    }

    //인터렉션 관련
    private void OnInteraction(float deltaTime)
    {
#if !GAMEINSTANCE

        if (_rightDevice.TryGetFeatureValue(CommonUsages.gripButton, out bool gripBtnPressed))
        {
            if (gripBtnPressed && !wasGripBtnPressed)
            {
                base.ProcessCommand(new MLCCommand());

                Debug.Log("상호작용(Grip Btn Pressed)");
            }
            wasGripBtnPressed = gripBtnPressed;
        }

        if (_rightDevice.TryGetFeatureValue(CommonUsages.primaryButton, out bool primaryBtnPressed))
        {
            if (primaryBtnPressed && !wasPrimaryBtnPressed)
            {
                Debug.Log("A 버튼 눌림: 아이템 스왑 시작");
                OnSwapItemInput();
            }
            wasPrimaryBtnPressed = primaryBtnPressed;
        }
#endif
    }

    private void OnSwapItemInput()
    {
#if !GAMEINSTANCE
        if (base.IsOwner)
        {

            // 🌟 기획에 따라 무기 슬롯 개수를 설정하세요.
            // 메인(0), 서브(1) 2개만 쓴다면 2로 설정.
            // 근접 무기(2)까지 쓴다면 3으로 설정.
            int maxSlots = 2;
            int currentSlotIndex = (int)_equipHandler.CurrentActiveSlot;
            int targetIndex = (currentSlotIndex + 1) % maxSlots;

            GameManager.Instance.GetSystem<PlayerSpawnSystem>().OnRequestEquipToServer(_Id.Value, targetIndex);
            Debug.Log($"[Input] 단일 스왑 키 눌림 ➡️ {_currentSwapIndex}번 슬롯 장착 요청");

        }
#endif
    }


    /// <summary>
    /// // 현재 로그인씬에 존재하는 InputSystemUIInputModule 컴포넌트로는 VR 상호작용이 안돼서 XRUIInputModule 추가해줘야함
    /// </summary>
    void SwitchToXRInput()
    {
        EventSystem eventSystem = EventSystem.current;

        if (eventSystem != null)
        {
            InputSystemUIInputModule inputSystemModule = eventSystem.GetComponent<InputSystemUIInputModule>();

            if (inputSystemModule != null)
            {
                // UI 상호작용 안될 수 있으니 일단 삭제
                // 는 재시작 할 수도 있으니 일단 비활성화
                inputSystemModule.enabled = false;
            }

            if (eventSystem.GetComponent<XRUIInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<XRUIInputModule>();
            }
        }
        else
        {
            Debug.LogWarning("EventSystem is null");
        }
    }

    /// <summary>
    /// 트래킹 끊겼을 때 플레이어 위치 재조정
    /// </summary>
    //void RecenterXR()
    //{
    //    if (hmd == null) return;

    //    Vector3 cameraOffset = _playerCam.MainCamera.transform.localPosition;

    //    gameObject.transform.position = body.position - new Vector3(cameraOffset.x, 0, cameraOffset.z);

    //    Vector3 forward = hmd.forward;
    //    forward.y = 0;

    //    if (forward.sqrMagnitude < 0.001f) return;

    //    gameObject.transform.rotation = Quaternion.LookRotation(forward);
    //}

    //void ExcuteRecenter(bool hmdTracking)
    //{
    //    bool isTrackingLost = !hmdTracking;

    //    if (wasTrackingLost && hmdTracking)
    //    {
    //        RecenterXR();
    //    }
    //    wasTrackingLost = isTrackingLost;
    //}

    private void UpdateVRTransforms(float dt)
    {
        if (!_headData.valid || headIKTarget == null || _playerCam == null) return;

        float posDiff = Vector3.SqrMagnitude(_headData.pos - _lastHeadPos);
        float rotDiff = Quaternion.Angle(_headData.rot, _lastHeadRot);

        if (posDiff > _moveEpsilon * _moveEpsilon || rotDiff > _rotateEpsilon)
        {
            _lastHeadPos = _headData.pos;
            _lastHeadRot = _headData.rot;

            _playerCam.transform.position = _headBone.position;
            _playerCam.transform.rotation = _headData.rot;
            headIKTarget.SetPositionAndRotation(_headData.pos, _headData.rot);
        }

        float lerpT = 1f - Mathf.Exp(-50 * dt);
        Quaternion invHmdRot = Quaternion.Inverse(_headData.rot);

        ApplySmoothedHandTarget(leftHandIKTarget, _leftData, invHmdRot, lerpT);
        ApplySmoothedHandTarget(rightHandIKTarget, _rightData, invHmdRot, lerpT);
    }

    private void ApplySmoothedHandTarget(Transform ikTarget, TrackingData data, Quaternion invHmdRot, float lerpT)
    {
        if (ikTarget == null || !data.valid) return;

        // 1. 현실 HMD 기준 로컬 오프셋 계산 (회전 시 튀는 현상 방지 핵심)
        Vector3 localPos = invHmdRot * (data.pos - _headData.pos);
        Quaternion localRot = invHmdRot * data.rot;

        // 2. 현재 캐릭터 머리(카메라) 월드 좌표 기준으로 타겟 위치 재구성
        Vector3 targetWorldPos = _playerCam.transform.position + (_playerCam.transform.rotation * localPos);
        Quaternion targetWorldRot = _playerCam.transform.rotation * localRot;

        // 3. 지수 보간 적용 (덜덜거림 제거)
        ikTarget.position = Vector3.Lerp(ikTarget.position, targetWorldPos, lerpT);
        ikTarget.rotation = Quaternion.Slerp(ikTarget.rotation, targetWorldRot, lerpT);
    }
}
