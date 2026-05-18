using UnityEngine;


namespace TRAINEE
{
    public class InteriorView : MapViewBase
    {

        private DynamicProp[] _dynamicProp = null;
        public override void Init(string id)
        {
            _id = id;

            _dynamicProp = GetComponentsInChildren<DynamicProp>(true);


            if (_dynamicProp != null)
            {
                if (_dynamicProp.Length > 0)
                {
                    for(int i = 0; i < _dynamicProp.Length; i++)
                    {
                        _dynamicProp[i].Init();

#if GAMEINSTANCE
                        _dynamicProp[i].SetupPhysicsAuth(true);
#else
                        _dynamicProp[i].SetupPhysicsAuth(false);
#endif
                    }

                    GameManager.Instance.GetSystem<MapSystem>().RegisterDynamicProp(base._id, _dynamicProp);
                }

            }

        }

        public override void UpdateView(GameEvent data)
        {

        }
    }
}
