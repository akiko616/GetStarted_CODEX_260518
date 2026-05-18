using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;

namespace TRAINEE
{
    public class ObjectPoolManager : Singleton<ObjectPoolManager>
    {
        [SerializeField] protected Dictionary<string, Queue<GameObject>> _pools = new();

        private readonly int _maxPoolSize = 10;
        
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

        // 캐싱
        public async UniTask Preload (string path,int count = 1)
        {

            for (int i = 0; i < count; i++)
            {
                GameObject obj = await OnInstantiateFromResources(path);

                if (obj == null)
                {
                    continue;
                }

                obj.gameObject.SetActive(false);

                Queue<GameObject> pool = this.GetPool(path);

                pool.Enqueue(obj);
                obj.SetActive(false);
            }
        }

        // 사용
        public async UniTask<GameObject> Spawn(string path,Transform parent = null,bool isActive = true)
        {
            GameObject obj = null;

            Queue<GameObject> pool = this.GetPool(path);

            if (pool != null )
            {
                if (pool.Count == 0)
                {
                    obj = await OnInstantiateFromResources(path);

                    if (obj == null)
                    {
                        Debug.LogError($"Spawn Error - path : {path}");
                        return obj;
                    }
                }
                else
                {
                    obj = pool.Dequeue();
                }
            }

            if (parent != null)
            {
                obj.transform.SetParent (parent);
            }

            obj.gameObject.SetActive(isActive);

            return obj;
        }

        // 반납
        public void Despawn(string path,GameObject obj)
        {
            if(obj != null)
            {
                Queue<GameObject> pool = this.GetPool(path);

                if(pool.Count >= _maxPoolSize)
                {
                    // 삭제
                    Destroy(obj);
                }
                else
                {
                    pool.Enqueue(obj);

                    obj.gameObject.SetActive(false);

                    obj.transform.SetParent(this.transform);
                }

            }
        }

        private Queue<GameObject> GetPool(string path)
        {
            Queue<GameObject> pool = null;
            if (!_pools.TryGetValue(path, out pool))
            {
                pool = new Queue<GameObject>();
                _pools.Add(path, pool);
            }
            return pool;
        }

        protected async UniTask<GameObject> OnInstantiateFromResources(string _path)
        {
            GameObject obj = await LdResources.LoadAsync<GameObject>(_path);
            if (obj != null)
            {
                obj.transform.SetParent(this.transform);

                return obj;
            }

            return obj;
        }
    }
}
