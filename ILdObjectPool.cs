using UnityEngine;

namespace TRAINEE
{
    public interface ILdObjectPool
    {
        public void OnAlloc();
        public void OnFree();

        public void Release();
    }
}
