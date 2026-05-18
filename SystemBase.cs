using Cysharp.Threading.Tasks;
using System;
using UnityEngine;


namespace TRAINEE
{
    public class SystemBase : MonoBehaviour
    {
        public virtual bool IsExclusiveRunning => false;
        protected Loading _loadingTask = null;
        public virtual void Init()
        {
            _loadingTask = new Loading(LoadingAsync);
            LoadingHelper.RegisterLoading(_loadingTask);
        }

        public virtual void OnGameStart() 
        { 

        }

        public virtual void OnUpdate(float deltaTime)
        {

        }

        public virtual void OnFixedUpdate(float deltaTime)
        {

        }

        public virtual void OnLateUpdate(float deltaTime)
        {
        }

        public virtual async UniTask LoadingAsync(Action<float,string> onProgress,int delaytime)
        {
            await UniTask.Delay(0);
        }

        public virtual void OnDestroy()
        {
            if (_loadingTask != null)
            {
                LoadingHelper.UnRegisterLoading(_loadingTask);
                _loadingTask = null; // 안전하게 비워주기
            }
        }
    }
}
