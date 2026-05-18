using Cysharp.Threading.Tasks;
using UnityEngine;


namespace TRAINEE
{
    public static class LdResources 
    {

        public async static UniTask<T> LoadUI<T>(string path,Transform _parent) where T : UiBase
        {
            GameObject obj = await LoadAsync<GameObject>(path);
            if (obj != null)
            {
                GameObject instance = GameObject.Instantiate(obj, _parent);
                if (instance != null)
                {
                    T ui = instance.GetComponent<T>();
                    return ui;
                }
            }
            return null;
        }

        public async static UniTask<T> LoadAsync<T>(string path) where T : Object
        {
            ResourceRequest requset = Resources.LoadAsync<T>(path);

            while(!requset.isDone)
            {
#if UNITY_EDITOR || DEV_MODE
                Debug.Log($"Resource Load Progress : {requset.progress}");
#endif

                await UniTask.Yield();
            }


            if (requset.asset == null)
            {
                Debug.LogError($"Resource Load Error : {path}");
                return null;
            }

            return requset.asset as T;
        }

        public static T LoadMap<T>(string path, Transform _parent) where T : MapViewBase
        {
            GameObject obj = Load<GameObject>(path);
            if (obj != null)
            {
                GameObject instance = GameObject.Instantiate(obj, _parent);

                if (instance != null)
                {
                    T map = instance.GetComponent<T>();
                    return map;
                }
            }
            return null;
        }

        public static T Load<T>(string path, Transform _parent) where T : MonoBehaviour
        {
            GameObject obj = Load<GameObject>(path);
            if (obj != null)
            {
                GameObject instance = GameObject.Instantiate(obj, _parent);
                if (instance != null)
                {
                    T component = instance.GetComponent<T>();
                    return component;
                }
            }
            return null;
        }

        public static T Load<T>(string path, Vector3 pos, Quaternion rot, Transform _parent) where T : MonoBehaviour
        {
            GameObject obj = Load<GameObject>(path);
            if (obj != null)
            {
                GameObject instance = GameObject.Instantiate(obj, pos, rot, _parent);
                if (instance != null)
                {
                    T component = instance.GetComponent<T>();
                    return component;
                }
            }
            return null;
        }

        public static T Load<T>(string path) where T : Object
        {
            return Resources.Load<T>(path);
        }

        public static T[] LoadAll<T>(string path) where T : Object
        {
            return Resources.LoadAll<T>(path);
        }
    }
}
