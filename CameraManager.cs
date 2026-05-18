using UnityEngine;

namespace TRAINEE
{
    public class CameraManager : Singleton<CameraManager>
    {
        [SerializeField] private Camera _mainCamera;

        protected override void Awake()
        {
            base.Awake();
        }

        protected override void Start()
        {
            base.Start();
        }

        protected override void Init()
        {
            base.Init();
        }


    }
}
