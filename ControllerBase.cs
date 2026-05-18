using Cysharp.Threading.Tasks;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;



namespace TRAINEE
{
    public abstract class ControllerBase : CharacterBase
    {
        [SerializeField] protected Transform _anchor = null;


        [Header("Detect Setting")]
        [SerializeField][Range(0f, 0.5f)] protected float _detectRadius = 0f;
        [SerializeField][Range(0f, 10f)] protected float _detectDist = 0f;


        protected MovementBase _movementBase = null;
        protected StateMachine _stateMachine = null;
        protected AnimationEventReceiver _animReceiver = null;
        protected bool _isOwner = false;
        protected Dictionary<EStateType, StateBase> _states = new Dictionary<EStateType, StateBase>();
        public MovementBase Movement { get=>_movementBase; private set=>_movementBase = value; }
        public StateMachine StateMachine { get => _stateMachine; private set => _stateMachine = value; }

        public float DetectRadius { get => _detectRadius; }

        public float DetectDist { get => _detectDist; }

        public string GetCurrentEquipId { get => _equipHandler?.CurrentActiveEquipId; }

        protected readonly SyncVar<string> _Id = new SyncVar<string>();

        public string Id { get => _Id.Value; private set => _Id.Value = value; }

        public StateBase FindState(EStateType state)
        {
            return _states[state];
        }
        public bool IsLocalPlayer
        {
            get
            {
                return _isOwner;
            }
     
        }
        public Transform Anchor
        {
            get
            {
                if(_anchor != null)
                {
                    return _anchor;
                }

                return this.transform;
            }
        }

        //public bool IsHoldAction
        //{
        //    get
        //    {
        //        return _isHoldAction;
        //    }

        //    set
        //    {
        //        _isHoldAction = value;
        //    }
        //}

        public int CurrentEquipLayerIndex
        {
            get
            {
                if (string.IsNullOrEmpty(GetCurrentEquipId))
                    return 0;

                return characterInfo.FindEquipAnimatorLayer(GetCurrentEquipId);
            }
        }

        public virtual void Init()
        {
            _movementBase = GetComponent<MovementBase>();

            _movementBase.Init();

            _stateMachine = new StateMachine();

            if (_animator != null)
            {
                _animReceiver = _animator.GetComponent<AnimationEventReceiver>();
                if (_animReceiver != null)
                {
                    _animReceiver.Init(this);
                }
            }
        }

        public virtual void Init(string Id)
        {
            Init();

            _Id.Value = Id;

            Debug.Log($"[ControllerBase] ID : {_Id.Value}");
        }

        public virtual void InitPlayer(string id, string role)
        {
            Init(id);
        }

        public virtual void Setup()
        {

        }

        public virtual void OnUpdate(float deltaTime)
        {
            if (_isOwner)
            {
                _movementBase.OnUpdate(deltaTime);

                if (_stateMachine._currentState != null)
                {
                    _stateMachine._currentState.OnUpdate(deltaTime);
                }
            }
        }

        public virtual void OnFixedUpdate(float deltaTime)
        {
            if (_isOwner)
            {
                _movementBase.OnFixedUpdate(deltaTime);

                if (_stateMachine._currentState != null)
                {
                    _stateMachine._currentState.OnFixedUpdate(deltaTime);
                }
            }
        }

        public virtual void OnLateUpdate(float deltaTime)
        {
            if (_isOwner)
            {
                _movementBase.OnLateUpdate(deltaTime);
            }
        }

        public void ChangeState(EStateType state)
        {
            if (_states.TryGetValue(state, out StateBase stateBase))
            {
                StateMachine.Change(stateBase);
            }
        }

        public void ProcessCommand(CommandBase command)
        {
            if(_stateMachine._currentState != null)
            {
                _stateMachine._currentState.HandleCommand(command);
            }
        }
        protected abstract void RegisterStates();

        public virtual void ApplyEquipVisuals(string itemId, string instanceId)
        {
            Debug.Log($"아이템 착용 : {itemId} / {instanceId} ");
            
            _equipHandler.SetupEquip(itemId, instanceId);

            UpdateAnimatorAndIK();
        }

        public virtual void ApplyEquipVisuals(int slotIdx)
        {
            if (_equipHandler.CurrentActiveSlot == (EInventoryType)slotIdx)
            {
                Debug.Log($"[클라] 현재 슬롯과 변경 슬롯이 같습니다.");
                return;
            }

            if (_equipHandler.ExecuteSwap(slotIdx))
            {
                Debug.Log($"[클라] 무기 스왑 실행: {slotIdx}번 슬롯");

                UpdateAnimatorAndIK();
            }
        }

        protected void UpdateAnimatorAndIK()
        {
            if (string.IsNullOrEmpty(_equipHandler.CurrentActiveEquipId))
            {
                return;
            }

            EInventoryType currentSlot = _equipHandler.CurrentActiveSlot;
            int layer = characterInfo.FindEquipAnimatorLayer(_equipHandler.CurrentActiveEquipId);
            Transform target = _equipHandler.CurrentEquip?.LeftHand;
            Transform hint = _equipHandler.CurrentEquip?.LeftHandHint;
            _animator.runtimeAnimatorController = characterInfo.FindEquipAnimator(_equipHandler.CurrentActiveEquipId);

            //_animator.Rebind();

            Debug.Log($"현재 장비 슬롯 : {currentSlot}");
            Debug.Log($"현재 장비 id : {_equipHandler.CurrentActiveEquipId}");
            Debug.Log($"현재 장비 레이어 : {layer}");
            
            IkSetting(layer,1,target,hint);
            if (currentSlot == EInventoryType.Sub)
            {

                if (_animationHandler != null)
                {
                    Debug.Log($"무전기 착용 상태");
                    _animationHandler.StartIKBlend(1f, 0f, 0.1f, false);
                }
            }
            else
            {
                //IkSetting(layer, 0, target, hint);

                if (_animationHandler != null)
                {
                    Debug.Log($"메인장비 착용 상태");
                    _animationHandler.StartIKBlend(0f, 1f, 0.3f, false);
                }
            }
        }

        protected void IkSetting(int layer = 0, float weight = 0, Transform target = null, Transform hint = null)
        {
            _ikHandler.SetLayer(layer);
            _ikHandler.SetLayerWeight(weight);

            if (target != null && hint != null)
            {
                _ikHandler.SetupLeftIk(target, hint);
            }
            
        }

        protected GameModelEventBase _pendingPayload;
        public virtual void OnAnimationHitEvent()
        {
            if (_equipHandler != null && _equipHandler.CurrentAnimEvent != null)
            {
                _equipHandler.CurrentAnimEvent.OnHit();
            }

            if (_pendingPayload != null)
            {
                // 네트워크/서버 전담 시스템으로 데이터 토스!
                GameManager.Instance.GetSystem<PlayerSpawnSystem>().RequestInteractionToServer(_pendingPayload);
                _pendingPayload = null;
            }
        }

        public virtual void OnAnimationHitEndEvent()
        {
            if (StateMachine._currentStateType == EStateType.EquipAction)
            {
                EquipActionState equipState = StateMachine._currentState as EquipActionState; 
                equipState.OnFinishAction();
            }
        }

    }
}
