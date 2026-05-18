using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ReplaySystem.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReplaySystem.Capture
{
    /// <summary>동적 오브젝트 추적 시스템입니다.</summary>
    public class DynamicObjectTracker : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private bool autoTrackInstantiates = true;
        [SerializeField] private List<GameObject> trackablePrefabs = new List<GameObject>();

    private readonly Dictionary<int, TrackedDynamicObject> trackedObjects = new Dictionary<int, TrackedDynamicObject>();
        private readonly Dictionary<int, string> prefabHashToPath = new Dictionary<int, string>();

        // 더블 버퍼 패턴 (GC 할당 최소화)
        private List<DynamicObjectEvent> pendingEvents = new List<DynamicObjectEvent>();
        private List<DynamicObjectEvent> processingEvents = new List<DynamicObjectEvent>();

        private ReplayRecorder recorder;
        private int nextDynamicId = 100000;
        private bool isRecording;

        /// <summary>추적 중인 동적 오브젝트 수</summary>
        public int TrackedCount => trackedObjects.Count;

        /// <summary>동적 오브젝트 생성 이벤트</summary>
        public event Action<int, GameObject> OnDynamicObjectCreated;

        /// <summary>동적 오브젝트 파괴 이벤트</summary>
        public event Action<int> OnDynamicObjectDestroyed;

        private void Awake()
        {
            RegisterPrefabs();
        }

        /// <summary>프리팹 목록을 등록합니다.</summary>
        public void RegisterPrefabs()
        {
            foreach (var prefab in trackablePrefabs)
            {
                if (prefab == null)
                {
                    continue;
                }

                int hash = prefab.name.GetHashCode();
                prefabHashToPath[hash] = prefab.name;
            }
        }

        /// <summary>프리팹을 추가 등록합니다.</summary>
        public void RegisterPrefab(GameObject prefab, string path = null)
        {
            if (prefab == null)
            {
                return;
            }

            string prefabPath = path ?? prefab.name;
            int hash = prefabPath.GetHashCode();

            prefabHashToPath[hash] = prefabPath;

            if (!trackablePrefabs.Contains(prefab))
            {
                trackablePrefabs.Add(prefab);
            }
        }

        /// <summary>녹화 시작 설정</summary>
        public void SetRecorder(ReplayRecorder rec)
        {
            recorder = rec;
        }

        /// <summary>녹화 상태 설정</summary>
        public void SetRecordingState(bool recording)
        {
            isRecording = recording;

            if (!recording)
            {
                pendingEvents.Clear();
                processingEvents.Clear();
            }
        }

        /// <summary>오브젝트 Instantiate를 추적합니다.</summary>
        public GameObject TrackInstantiate(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            var instance = Object.Instantiate(prefab, position, rotation, parent);
            RegisterDynamicObject(instance, prefab.name);
            return instance;
        }

        /// <summary>오브젝트 Instantiate를 추적합니다 (비동기)</summary>
        public async UniTask<GameObject> TrackInstantiateAsync(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent, CancellationToken ct)
        {
            var op = Object.InstantiateAsync(prefab, parent,position, rotation);

            while (!op.isDone)
            {
                await UniTask.Yield(ct);
            }

            if (op.Result == null || op.Result.Length == 0)
            {
                return null;
            }

            var instance = op.Result[0];
            RegisterDynamicObject(instance, prefab.name);
            return instance;
        }

        /// <summary>기존 오브젝트를 동적 추적에 등록합니다.</summary>
        public int RegisterDynamicObject(GameObject obj, string prefabName)
        {
            if (obj == null)
            {
                return -1;
            }

            int dynamicId = nextDynamicId++;

            var capture = obj.GetComponent<TransformCapture>();

            if (capture == null)
            {
                capture = obj.AddComponent<TransformCapture>();
            }

            capture.PrefabName = prefabName;
            capture.Initialize(dynamicId);

            var rigidbodyCapture = obj.GetComponent<RigidbodyCapture>();

            if (rigidbodyCapture == null && obj.GetComponent<Rigidbody>() != null)
            {
                rigidbodyCapture = obj.AddComponent<RigidbodyCapture>();
            }

            var tracked = new TrackedDynamicObject
            {
                Id = dynamicId,
                GameObject = obj,
                PrefabName = prefabName,
                PrefabHash = prefabName.GetHashCode(),
                TransformCapture = capture,
                RigidbodyCapture = rigidbodyCapture,
                CreationTime = Time.time
            };

            trackedObjects[dynamicId] = tracked;

            if (isRecording)
            {
                var evt = new DynamicObjectEvent
                {
                    Type = DynamicEventType.Create,
                    ObjectId = dynamicId,
                    PrefabHash = tracked.PrefabHash,
                    Position = obj.transform.position,
                    Rotation = obj.transform.rotation,
                    Time = Time.time
                };

                pendingEvents.Add(evt);
                recorder?.RegisterObject(capture);
            }

            OnDynamicObjectCreated?.Invoke(dynamicId, obj);

            return dynamicId;
        }

        /// <summary>오브젝트 파괴를 추적합니다.</summary>
        public void TrackDestroy(GameObject obj)
        {
            if (obj == null)
            {
                return;
            }

            var capture = obj.GetComponent<TransformCapture>();

            if (capture == null)
            {
                Object.Destroy(obj);
                return;
            }

            int objectId = capture.ObjectId;

            if (trackedObjects.TryGetValue(objectId, out var tracked))
            {
                if (isRecording)
                {
                    var evt = new DynamicObjectEvent
                    {
                        Type = DynamicEventType.Destroy,
                        ObjectId = objectId,
                        Time = Time.time
                    };

                    pendingEvents.Add(evt);
                }

                trackedObjects.Remove(objectId);
                OnDynamicObjectDestroyed?.Invoke(objectId);
            }

            Object.Destroy(obj);
        }

        /// <summary>대기 중인 이벤트를 가져옵니다 (더블 버퍼 패턴 - GC 할당 최소화).</summary>
        public List<DynamicObjectEvent> FlushEvents()
        {
            // 버퍼 스왑 (할당 없이 참조만 교환)
            (pendingEvents, processingEvents) = (processingEvents, pendingEvents);

            // 이전 pending (현재 processing)은 클리어하지 않고 그대로 반환
            // 호출자가 처리 완료 후 Clear() 호출 필요 없음 - 다음 스왑에서 재사용됨
            pendingEvents.Clear(); // 새 pending 버퍼 초기화

            return processingEvents;
        }

        /// <summary>동적 오브젝트 정보를 SceneSnapshot에 추가합니다.</summary>
        public void PopulateSnapshot(SceneSnapshot snapshot)
        {
            foreach (var kvp in prefabHashToPath)
            {
                snapshot.DynamicObjectPrefabs.Add(new DynamicObjectInfo
                {
                    PrefabHash = kvp.Key,
                    PrefabPath = kvp.Value
                });
            }
        }

        /// <summary>재생 시 동적 오브젝트를 생성합니다.</summary>
        public GameObject RecreateObject(int objectId, string prefabName, Vector3 position, Quaternion rotation)
        {
            GameObject prefab = FindPrefab(prefabName);

            if (prefab == null)
            {
                Debug.LogWarning($"[DynamicObjectTracker] Prefab not found: {prefabName}");
                return null;
            }

            var instance = Object.Instantiate(prefab, position, rotation);
            var capture = instance.GetComponent<TransformCapture>();

            if (capture == null)
            {
                capture = instance.AddComponent<TransformCapture>();
            }

            capture.PrefabName = prefabName;
            capture.Initialize(objectId);

            var rigidbodyCapture = instance.GetComponent<RigidbodyCapture>();

            if (rigidbodyCapture == null && instance.GetComponent<Rigidbody>() != null)
            {
                rigidbodyCapture = instance.AddComponent<RigidbodyCapture>();
            }

            var tracked = new TrackedDynamicObject
            {
                Id = objectId,
                GameObject = instance,
                PrefabName = prefabName,
                PrefabHash = prefabName.GetHashCode(),
                TransformCapture = capture,
                RigidbodyCapture = rigidbodyCapture,
                CreationTime = Time.time
            };

            trackedObjects[objectId] = tracked;

            return instance;
        }

        /// <summary>캐시에서 오브젝트를 복원합니다 (역재생용).</summary>
        public GameObject RestoreFromCache(int objectId, string prefabName, Vector3 position, Quaternion rotation, bool hasRigidbody)
        {
            // 이미 존재하는 경우 기존 오브젝트 반환
            if (trackedObjects.TryGetValue(objectId, out var existing) && existing.GameObject != null)
            {
                Debug.LogWarning($"[DynamicObjectTracker] Object {objectId} already exists, returning existing");
                return existing.GameObject;
            }

            GameObject prefab = FindPrefab(prefabName);

            if (prefab == null)
            {
                Debug.LogWarning($"[DynamicObjectTracker] Prefab not found for restore: {prefabName}");
                return null;
            }

            var instance = Object.Instantiate(prefab, position, rotation);
            var capture = instance.GetComponent<TransformCapture>();

            if (capture == null)
            {
                capture = instance.AddComponent<TransformCapture>();
            }

            capture.PrefabName = prefabName;
            capture.Initialize(objectId);

            RigidbodyCapture rigidbodyCapture = null;

            if (hasRigidbody)
            {
                rigidbodyCapture = instance.GetComponent<RigidbodyCapture>();

                if (rigidbodyCapture == null && instance.GetComponent<Rigidbody>() != null)
                {
                    rigidbodyCapture = instance.AddComponent<RigidbodyCapture>();
                }
            }

            var tracked = new TrackedDynamicObject
            {
                Id = objectId,
                GameObject = instance,
                PrefabName = prefabName,
                PrefabHash = prefabName.GetHashCode(),
                TransformCapture = capture,
                RigidbodyCapture = rigidbodyCapture,
                CreationTime = Time.time
            };

            trackedObjects[objectId] = tracked;

            Debug.Log($"[DynamicObjectTracker] Restored object from cache: {objectId} ({prefabName})");

            return instance;
        }

        /// <summary>재생 시 동적 오브젝트를 파괴합니다.</summary>
        public void DestroyTrackedObject(int objectId)
        {
            if (trackedObjects.TryGetValue(objectId, out var tracked))
            {
                if (tracked.GameObject != null)
                {
                    Object.Destroy(tracked.GameObject);
                }

                trackedObjects.Remove(objectId);
            }
        }

        /// <summary>ID로 추적 오브젝트 조회</summary>
        public TrackedDynamicObject GetTrackedObject(int objectId)
        {
            return trackedObjects.TryGetValue(objectId, out var tracked) ? tracked : null;
        }

        /// <summary>모든 동적 오브젝트 정리</summary>
        public void ClearAll()
        {
            foreach (var kvp in trackedObjects)
            {
                if (kvp.Value.GameObject != null)
                {
                    Object.Destroy(kvp.Value.GameObject);
                }
            }

            trackedObjects.Clear();
            pendingEvents.Clear();
            processingEvents.Clear();
        }

        private GameObject FindPrefab(string prefabName)
        {
            foreach (var prefab in trackablePrefabs)
            {
                if (prefab != null && prefab.name == prefabName)
                {
                    return prefab;
                }
            }

            return Resources.Load<GameObject>(prefabName);
        }
    }

    /// <summary>추적 중인 동적 오브젝트 정보</summary>
    public class TrackedDynamicObject
    {
        public int Id;
        public GameObject GameObject;
        public string PrefabName;
        public int PrefabHash;
        public TransformCapture TransformCapture;
        public RigidbodyCapture RigidbodyCapture;
        public float CreationTime;
    }

    /// <summary>동적 오브젝트 이벤트</summary>
    public struct DynamicObjectEvent
    {
        public DynamicEventType Type;
        public int ObjectId;
        public int PrefabHash;
        public Vector3 Position;
        public Quaternion Rotation;
        public float Time;
    }

    /// <summary>동적 이벤트 타입</summary>
    public enum DynamicEventType
    {
        Create,
        Destroy
    }
}
