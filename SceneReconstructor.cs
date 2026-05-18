using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FishNet.Managing;
using FishNet.Object;
using ReplaySystem.Capture;
using ReplaySystem.Core;
using ReplaySystem.Network;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ReplaySystem.Playback
{
    /// <summary>
    /// 리플레이 재생을 위한 씬 재구성 시스템입니다.
    /// FishNet 로컬 호스트를 기동하여 네트워크 오브젝트의 RPC/SyncVar가 정상 동작하도록 합니다.
    /// </summary>
    public class SceneReconstructor : MonoBehaviour
    {
        /// <summary>동적 오브젝트 판별 기준 ID (DynamicObjectTracker.nextDynamicId 기본값, IsNetworkObject 플래그 없을 때 폴백)</summary>
        private const int DYNAMIC_OBJECT_ID_THRESHOLD = 100000;

        [Header("Settings")]
        [SerializeField, Tooltip("씬 로드 후 네트워크 오브젝트 자동 생성")]
        private bool autoSpawnNetworkObjects = true;

        [SerializeField, Tooltip("씬 로드 모드")]
        private LoadSceneMode loadSceneMode = LoadSceneMode.Single;

        [Header("References")]
        [SerializeField] private ReplayPlayer replayPlayer;
        [SerializeField] private NetworkManager fishNetManager;

        // 프리팹 이름 -> 프리팹 GameObject 수동 등록용
        private readonly Dictionary<string, GameObject> prefabRegistry = new Dictionary<string, GameObject>();

        // 재구성된 네트워크 오브젝트 추적
        private readonly List<GameObject> spawnedNetworkObjects = new List<GameObject>();

        private bool isHostStarted;
        private FishNetReplayHandler replayHandler;

        /// <summary>재구성 완료 이벤트</summary>
        public event Action<int> OnReconstructionComplete;

        /// <summary>프리팹을 수동 등록합니다 (Resources에 없는 프리팹용).</summary>
        public void RegisterPrefab(string prefabName, GameObject prefab)
        {
            prefabRegistry[prefabName] = prefab;
        }

        /// <summary>여러 프리팹을 일괄 등록합니다.</summary>
        public void RegisterPrefabs(IEnumerable<KeyValuePair<string, GameObject>> prefabs)
        {
            foreach (var kvp in prefabs)
            {
                prefabRegistry[kvp.Key] = kvp.Value;
            }
        }

        /// <summary>
        /// 리플레이 데이터 기반으로 씬을 재구성합니다.
        /// 1단계: 씬 로드 (비네트워크 오브젝트 포함)
        /// 2단계: FishNet 로컬 호스트 기동
        /// 3단계: 네트워크 오브젝트 스폰 (SceneSnapshot 기반 + FishNet Spawn)
        /// 4단계: ReplayPlayer에 오브젝트 매핑
        /// </summary>
        public async UniTask<bool> ReconstructAsync(ReplayData replayData, CancellationToken ct)
        {
            if (replayData == null)
            {
                Debug.LogError("[SceneReconstructor] ReplayData is null");
                return false;
            }

            // 1단계: 씬 로드
            string sceneName = replayData.Metadata.SceneName;
            bool sceneLoaded = await LoadSceneAsync(sceneName, ct);

            if (!sceneLoaded)
            {
                Debug.LogError($"[SceneReconstructor] Failed to load scene: {sceneName}");
                return false;
            }

            await UniTask.Yield(ct);

            // 2단계: FishNet 로컬 호스트 기동 (네트워크 데이터가 있는 경우)
            int spawnedCount = 0;

            if (autoSpawnNetworkObjects && replayData.InitialState != null && replayData.Metadata.HasNetworkData)
            {
                bool hostReady = await StartLocalHostAsync(ct);

                if (!hostReady)
                {
                    Debug.LogWarning("[SceneReconstructor] FishNet local host failed, falling back to non-network spawn");
                    spawnedCount = SpawnObjectsWithoutNetwork(replayData.InitialState);
                }
                else
                {
                    // 3단계: FishNet Spawn으로 네트워크 오브젝트 생성
                    spawnedCount = SpawnNetworkObjectsViaFishNet(replayData.InitialState);
                }
            }
            else if (autoSpawnNetworkObjects && replayData.InitialState != null)
            {
                // 네트워크 데이터 없는 리플레이: 동적 오브젝트만 스폰
                spawnedCount = SpawnObjectsWithoutNetwork(replayData.InitialState);
            }

            // 4단계: ReplayPlayer 매핑
            if (replayPlayer != null)
            {
                replayPlayer.AutoMapObjects();
            }

            // 5단계: 네트워크 이벤트 핸들러 설정 (RPC/SyncVar 재생용)
            if (replayPlayer != null && replayData.Metadata.HasNetworkData)
            {
                SetupNetworkReplayHandler();
            }

            OnReconstructionComplete?.Invoke(spawnedCount);

            Debug.Log($"[SceneReconstructor] Reconstruction complete - Scene: {sceneName}, Network objects spawned: {spawnedCount}, Host: {isHostStarted}");
            return true;
        }

        /// <summary>
        /// 리플레이 파일 로드 + 씬 재구성 + 재생 준비를 한번에 수행합니다.
        /// </summary>
        public async UniTask<bool> LoadAndReconstructAsync(string replayFilePath, CancellationToken ct)
        {
            if (replayPlayer == null)
            {
                Debug.LogError("[SceneReconstructor] ReplayPlayer is not assigned");
                return false;
            }

            bool loaded = await replayPlayer.LoadAsync(replayFilePath, ct);

            if (!loaded)
            {
                return false;
            }

            return true;
        }

        #region FishNet Local Host

        /// <summary>FishNet 로컬 호스트를 기동합니다 (서버 + 클라이언트, 로컬 전용).</summary>
        private async UniTask<bool> StartLocalHostAsync(CancellationToken ct)
        {
            if (isHostStarted)
            {
                return true;
            }

            if (fishNetManager == null)
            {
                fishNetManager = FindFirstObjectByType<NetworkManager>();
            }

            if (fishNetManager == null)
            {
                Debug.LogWarning("[SceneReconstructor] NetworkManager not found");
                return false;
            }

            try
            {
                // 서버 시작
                fishNetManager.ServerManager.StartConnection();

                await UniTask.WaitUntil(() => fishNetManager.ServerManager.Started,
                    cancellationToken: ct);

                // 클라이언트 연결 (Host 모드)
                fishNetManager.ClientManager.StartConnection();

                await UniTask.WaitUntil(() => fishNetManager.ClientManager.Started,
                    cancellationToken: ct);

                isHostStarted = true;
                Debug.Log("[SceneReconstructor] FishNet local host started");
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SceneReconstructor] FishNet host start failed: {e.Message}");
                return false;
            }
        }

        /// <summary>FishNet 호스트를 종료합니다.</summary>
        private void StopLocalHost()
        {
            if (!isHostStarted || fishNetManager == null)
            {
                return;
            }

            if (fishNetManager.ClientManager.Started)
            {
                fishNetManager.ClientManager.StopConnection();
            }

            if (fishNetManager.ServerManager.Started)
            {
                fishNetManager.ServerManager.StopConnection(true);
            }

            isHostStarted = false;
            Debug.Log("[SceneReconstructor] FishNet local host stopped");
        }

        #endregion

        #region Scene Loading

        private async UniTask<bool> LoadSceneAsync(string sceneName, CancellationToken ct)
        {
            if (SceneManager.GetActiveScene().name == sceneName)
            {
                Debug.Log($"[SceneReconstructor] Scene '{sceneName}' already loaded, skipping load");
                return true;
            }

            try
            {
                var asyncOp = SceneManager.LoadSceneAsync(sceneName, loadSceneMode);

                if (asyncOp == null)
                {
                    Debug.LogError($"[SceneReconstructor] Scene not found in build settings: {sceneName}");
                    return false;
                }

                while (!asyncOp.isDone)
                {
                    ct.ThrowIfCancellationRequested();
                    await UniTask.Yield(ct);
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SceneReconstructor] Scene load failed: {e.Message}");
                return false;
            }
        }

        #endregion

        #region Spawning

        /// <summary>FishNet Spawn을 통해 네트워크 오브젝트를 생성합니다. RPC/SyncVar 정상 동작.</summary>
        private int SpawnNetworkObjectsViaFishNet(SceneSnapshot snapshot)
        {
            var prefabHashToPath = BuildPrefabHashMap(snapshot);
            int spawnedCount = 0;

            foreach (var objState in snapshot.Objects)
            {
                bool isNetworkOrDynamic = objState.IsNetworkObject || objState.Id >= DYNAMIC_OBJECT_ID_THRESHOLD;

                if (!isNetworkOrDynamic)
                {
                    continue;
                }

                GameObject prefab = ResolvePrefab(objState, prefabHashToPath);

                if (prefab == null)
                {
                    Debug.LogWarning($"[SceneReconstructor] Prefab not found: {objState.PrefabName} (ID: {objState.Id})");
                    continue;
                }

                Vector3 position = objState.Transform.GetPosition();
                Quaternion rotation = objState.Transform.GetRotation();

                var instance = Instantiate(prefab, position, rotation);
                instance.name = objState.PrefabName;

                // FishNet 서버 스폰 (NetworkObject 초기화 + OnStartServer/Client 호출)
                var nob = instance.GetComponent<NetworkObject>();

                if (nob != null && fishNetManager != null && fishNetManager.ServerManager.Started)
                {
                    fishNetManager.ServerManager.Spawn(nob);

                    // 리플레이 모드: 호스트 클라이언트에 소유권 부여 (IsOwner == true 보장)
                    GiveOwnershipToHost(nob);
                }

                // Transform 상태 복원 (FishNet Spawn 후 적용)
                objState.Transform.ApplyToTransform(instance.transform);

                // 캡처 컴포넌트 설정
                SetupCaptureComponents(instance, objState);

                spawnedNetworkObjects.Add(instance);
                spawnedCount++;
            }

            return spawnedCount;
        }

        /// <summary>네트워크 없이 동적 오브젝트를 생성합니다 (네트워크 데이터 없는 리플레이용).</summary>
        private int SpawnObjectsWithoutNetwork(SceneSnapshot snapshot)
        {
            var prefabHashToPath = BuildPrefabHashMap(snapshot);
            int spawnedCount = 0;

            foreach (var objState in snapshot.Objects)
            {
                bool isNetworkOrDynamic = objState.IsNetworkObject || objState.Id >= DYNAMIC_OBJECT_ID_THRESHOLD;

                if (!isNetworkOrDynamic)
                {
                    continue;
                }

                GameObject prefab = ResolvePrefab(objState, prefabHashToPath);

                if (prefab == null)
                {
                    Debug.LogWarning($"[SceneReconstructor] Prefab not found: {objState.PrefabName} (ID: {objState.Id})");
                    continue;
                }

                Vector3 position = objState.Transform.GetPosition();
                Quaternion rotation = objState.Transform.GetRotation();

                // 비활성 상태로 생성 → 네트워크 컴포넌트 제거 → 활성화
                prefab.SetActive(false);
                var instance = Instantiate(prefab, position, rotation);
                prefab.SetActive(true);
                instance.name = objState.PrefabName;

                StripNetworkComponents(instance);

                objState.Transform.ApplyToTransform(instance.transform);
                SetupCaptureComponents(instance, objState);

                instance.SetActive(true);

                spawnedNetworkObjects.Add(instance);
                spawnedCount++;
            }

            return spawnedCount;
        }

        /// <summary>캡처 컴포넌트를 설정합니다.</summary>
        private static void SetupCaptureComponents(GameObject instance, ObjectState objState)
        {
            var capture = instance.GetComponent<TransformCapture>();

            if (capture == null)
            {
                capture = instance.AddComponent<TransformCapture>();
            }

            capture.PrefabName = objState.PrefabName;
            capture.Initialize(objState.Id);

            if (objState.HasRigidbody)
            {
                var rbCapture = instance.GetComponent<RigidbodyCapture>();

                if (rbCapture == null && instance.GetComponent<Rigidbody>() != null)
                {
                    rbCapture = instance.AddComponent<RigidbodyCapture>();
                }
            }

            if (objState.HasAnimator)
            {
                var animCapture = instance.GetComponent<AnimatorCapture>();

                if (animCapture == null && instance.GetComponent<Animator>() != null)
                {
                    animCapture = instance.AddComponent<AnimatorCapture>();
                }
            }
        }

        /// <summary>FishNet 네트워크 컴포넌트를 즉시 제거합니다 (폴백용, 비활성 상태에서 호출).</summary>
        private static void StripNetworkComponents(GameObject instance)
        {
            var networkBehaviours = instance.GetComponentsInChildren<NetworkBehaviour>(true);

            for (int i = networkBehaviours.Length - 1; i >= 0; i--)
            {
                DestroyImmediate(networkBehaviours[i]);
            }

            var networkObjects = instance.GetComponentsInChildren<NetworkObject>(true);

            for (int i = networkObjects.Length - 1; i >= 0; i--)
            {
                DestroyImmediate(networkObjects[i]);
            }
        }

        #endregion

        #region Prefab Resolution

        private static Dictionary<int, string> BuildPrefabHashMap(SceneSnapshot snapshot)
        {
            var map = new Dictionary<int, string>();

            if (snapshot.DynamicObjectPrefabs != null)
            {
                foreach (var info in snapshot.DynamicObjectPrefabs)
                {
                    map[info.PrefabHash] = info.PrefabPath;
                }
            }

            return map;
        }

        private GameObject ResolvePrefab(ObjectState objState, Dictionary<int, string> prefabHashToPath)
        {
            string prefabName = objState.PrefabName;

            // 1. 수동 등록된 프리팹
            if (prefabRegistry.TryGetValue(prefabName, out var registered))
            {
                return registered;
            }

            // 2. ObjectState.PrefabPath → Resources.Load
            if (!string.IsNullOrEmpty(objState.PrefabPath))
            {
                var prefab = TryLoadFromResources(objState.PrefabPath);

                if (prefab != null)
                {
                    return prefab;
                }
            }

            // 3. PrefabName → Resources.Load
            if (!string.IsNullOrEmpty(prefabName))
            {
                var prefab = Resources.Load<GameObject>(prefabName);

                if (prefab != null)
                {
                    return prefab;
                }
            }

            // 4. DynamicObjectPrefabs → PrefabPath
            int prefabHash = prefabName.GetHashCode();

            if (prefabHashToPath.TryGetValue(prefabHash, out string prefabPath))
            {
                if (prefabRegistry.TryGetValue(prefabPath, out var registeredByPath))
                {
                    return registeredByPath;
                }

                var prefab = TryLoadFromResources(prefabPath);

                if (prefab != null)
                {
                    return prefab;
                }
            }

            return null;
        }

        /// <summary>경로에서 Resources.Load를 시도합니다. Assets/ 전체 경로에서 Resources/ 추출도 수행.</summary>
        private static GameObject TryLoadFromResources(string path)
        {
            var prefab = Resources.Load<GameObject>(path);

            if (prefab != null)
            {
                return prefab;
            }

            int idx = path.IndexOf("/Resources/");

            if (idx >= 0)
            {
                string resPath = path.Substring(idx + "/Resources/".Length);

                if (resPath.EndsWith(".prefab"))
                {
                    resPath = resPath.Substring(0, resPath.Length - ".prefab".Length);
                }

                return Resources.Load<GameObject>(resPath);
            }

            return null;
        }

        #endregion

        #region Network Replay Handler

        /// <summary>호스트 클라이언트에 소유권을 부여합니다 (IsOwner == true 보장).</summary>
        private void GiveOwnershipToHost(NetworkObject nob)
        {
            if (fishNetManager == null || !fishNetManager.ClientManager.Started) return;

            var hostConnection = fishNetManager.ClientManager.Connection;
            if (hostConnection != null && hostConnection.IsActive)
            {
                nob.GiveOwnership(hostConnection);
            }
        }

        /// <summary>FishNet 네트워크 이벤트 핸들러를 생성하고 ReplayPlayer에 연결합니다.</summary>
        private void SetupNetworkReplayHandler()
        {
            // 기존 핸들러가 있으면 제거
            if (replayHandler != null)
            {
                Destroy(replayHandler);
            }

            replayHandler = gameObject.AddComponent<FishNetReplayHandler>();
            replayHandler.Initialize(replayPlayer, fishNetManager, this);
            replayPlayer.SetNetworkHandler(replayHandler);

            Debug.Log("[SceneReconstructor] Network replay handler initialized");
        }

        #endregion

        #region Cleanup

        /// <summary>재구성된 오브젝트를 정리하고 호스트를 종료합니다.</summary>
        public void CleanupSpawnedObjects()
        {
            foreach (var obj in spawnedNetworkObjects)
            {
                if (obj == null)
                {
                    continue;
                }

                // FishNet Despawn 후 Destroy
                var nob = obj.GetComponent<NetworkObject>();

                if (nob != null && nob.IsSpawned && fishNetManager != null && fishNetManager.ServerManager.Started)
                {
                    fishNetManager.ServerManager.Despawn(nob);
                }
                else
                {
                    Destroy(obj);
                }
            }

            spawnedNetworkObjects.Clear();

            if (replayHandler != null)
            {
                Destroy(replayHandler);
                replayHandler = null;
            }

            StopLocalHost();
        }

        private void OnDestroy()
        {
            CleanupSpawnedObjects();
        }

        #endregion
    }
}
