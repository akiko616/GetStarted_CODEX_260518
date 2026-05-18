using FishNet.Object;
using UnityEngine;



namespace TRAINEE
{
    public abstract class CharacterBase : NetworkBehaviour
    {
        [SerializeField] protected Animator _animator = null;
        [SerializeField] protected Transform _rHand = null;
        [SerializeField] protected Transform _lHand = null;
        [SerializeField] protected Transform _chestAnchor = null; // 가슴 위치 앵커
        [SerializeField] protected bool _isLeftHand = false;

        [SerializeField] protected CharacterInfo characterInfo;

        protected InteractionHandler _interactionHandler = null;
        protected AnimationHandler _animationHandler = null;
        protected CollisionHandler _collisionHandler = null;
        protected EquipHandler _equipHandler = null;
        protected IKHandler _ikHandler = null;
        public InteractionHandler InteractionHandler { get => _interactionHandler; }
        public AnimationHandler AnimationHandler { get => _animationHandler; }

        public CollisionHandler CollisionHandler { get => _collisionHandler; }

        public IKHandler IKHandler { get => _ikHandler; }

        public EquipHandler EquipHandler { get => _equipHandler; }

        public Transform MainHand
        {
            get { return _isLeftHand ? _lHand : _rHand; }
        }
        public Animator GetAnimator
        {
            get { return _animator; }
        }

        public Transform Chest
        {
            get
            {
                return _chestAnchor;
            }
        }
    }
}
