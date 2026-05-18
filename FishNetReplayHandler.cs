using System;
using System.Collections.Generic;
using System.Reflection;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Serializing;
using ReplaySystem.Playback;
using UnityEngine;

namespace ReplaySystem.Network
{
    /// <summary>
    /// FishNet 네트워크 이벤트를 리플레이 재생 시 실제로 실행하는 핸들러입니다.
    /// RPC는 Reflection으로 직접 호출하고, SyncVar는 Value 프로퍼티를 통해 설정합니다.
    /// FishNet 로컬 호스트 모드에서 동작하므로 서버/클라이언트 콜백이 모두 정상 발동됩니다.
    /// </summary>
    public class FishNetReplayHandler : MonoBehaviour, INetworkReplayHandler
    {
        /// <summary>
        /// 리플레이 재생 모드 여부. 사용자 코드에서 소유권 체크를 우회할 때 사용합니다.
        /// 예: if (!IsOwner && !FishNetReplayHandler.IsReplayMode) return;
        /// </summary>
        public static bool IsReplayMode { get; private set; }

        private ReplayPlayer replayPlayer;
        private NetworkManager networkManager;
        private SceneReconstructor sceneReconstructor;

        // Reflection 캐시: (Type, methodName) → MethodInfo
        private readonly Dictionary<(Type, string), MethodInfo> rpcMethodCache
            = new Dictionary<(Type, string), MethodInfo>();

        // Reflection 캐시: (Type, fieldName) → (FieldInfo, valueType)
        private readonly Dictionary<(Type, string), (FieldInfo field, Type valueType)> syncVarFieldCache
            = new Dictionary<(Type, string), (FieldInfo, Type)>();

        // FishNet Reader.Read<T>() 제네릭 메서드 캐시
        private static MethodInfo _readerReadGenericDef;
        private static readonly Dictionary<Type, MethodInfo> _readMethodCache = new Dictionary<Type, MethodInfo>();

        /// <summary>핸들러를 초기화합니다.</summary>
        public void Initialize(ReplayPlayer player, NetworkManager nm, SceneReconstructor reconstructor)
        {
            replayPlayer = player;
            networkManager = nm;
            sceneReconstructor = reconstructor;
            IsReplayMode = true;
        }

        private void OnDestroy()
        {
            IsReplayMode = false;
        }

        private GameObject ResolveObject(int objectId)
        {
            return replayPlayer != null ? replayPlayer.ResolveObject(objectId) : null;
        }

        #region INetworkReplayHandler

        public void OnReplayRpc(int objectId, string rpcName, byte[] parameters)
        {
            var go = ResolveObject(objectId);
            if (go == null)
            {
                Debug.LogWarning($"[FishNetReplayHandler] RPC target not found: objectId={objectId}, rpc={rpcName}");
                return;
            }

            var behaviours = go.GetComponents<NetworkBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                var nb = behaviours[i];
                var method = FindRpcMethod(nb.GetType(), rpcName);
                if (method == null) continue;

                try
                {
                    var args = DeserializeRpcArgs(method, parameters);
                    method.Invoke(nb, args);
                }
                catch (TargetInvocationException e)
                {
                    Debug.LogWarning($"[FishNetReplayHandler] RPC '{rpcName}' execution error: {e.InnerException?.Message}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[FishNetReplayHandler] RPC '{rpcName}' invoke failed: {e.Message}");
                }
                return;
            }

            Debug.LogWarning($"[FishNetReplayHandler] RPC method '{rpcName}' not found on object {objectId}");
        }

        public void OnReplayVarChange(int objectId, string varName, byte[] value)
        {
            var go = ResolveObject(objectId);
            if (go == null) return;

            var behaviours = go.GetComponents<NetworkBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                var nb = behaviours[i];
                var result = FindSyncVarField(nb.GetType(), varName);
                if (result.field == null) continue;

                var syncVarInstance = result.field.GetValue(nb);
                if (syncVarInstance == null) continue;

                try
                {
                    object deserializedValue = DeserializeFishNetValue(result.valueType, value);

                    var valueProp = result.field.FieldType.GetProperty("Value");
                    if (valueProp != null)
                    {
                        valueProp.SetValue(syncVarInstance, deserializedValue);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[FishNetReplayHandler] SyncVar '{varName}' set failed: {e.Message}");
                }
                return;
            }
        }

        public void OnReplayOwnershipChange(int objectId, int newOwnerId)
        {
            var go = ResolveObject(objectId);
            if (go == null) return;

            var nob = go.GetComponent<NetworkObject>();
            if (nob == null || networkManager == null || !networkManager.ServerManager.Started) return;

            // 로컬 호스트에서는 실제 클라이언트 연결이 호스트뿐이므로,
            // 소유권 변경은 로그만 기록하고 실제 변경은 시도하지 않습니다.
            // 실제 멀티클라이언트 리플레이 시에는 여기서 GiveOwnership 호출 필요.
            if (newOwnerId >= 0)
            {
                foreach (var conn in networkManager.ServerManager.Clients.Values)
                {
                    if (conn.ClientId == newOwnerId)
                    {
                        nob.GiveOwnership(conn);
                        return;
                    }
                }
            }
            else
            {
                nob.RemoveOwnership();
            }
        }

        public void OnReplaySpawn(int objectId, string prefabName, int ownerId, Vector3 position, Quaternion rotation)
        {
            // 리플레이 중간에 발생하는 동적 스폰 이벤트 처리
            if (sceneReconstructor == null) return;

            // 이미 존재하는 오브젝트면 스킵
            if (ResolveObject(objectId) != null) return;

            var prefab = LoadPrefab(prefabName);
            if (prefab == null)
            {
                Debug.LogWarning($"[FishNetReplayHandler] Spawn prefab not found: {prefabName}");
                return;
            }

            var instance = Instantiate(prefab, position, rotation);
            instance.name = prefabName;

            // FishNet 스폰
            if (networkManager != null && networkManager.ServerManager.Started)
            {
                var nob = instance.GetComponent<NetworkObject>();
                if (nob != null)
                {
                    networkManager.ServerManager.Spawn(nob);
                }
            }

            // ReplayPlayer에 등록
            var capture = instance.GetComponent<Capture.TransformCapture>();
            if (capture == null)
            {
                capture = instance.AddComponent<Capture.TransformCapture>();
            }
            capture.PrefabName = prefabName;
            capture.Initialize(objectId);

            if (replayPlayer != null)
            {
                replayPlayer.RegisterTransform(objectId, capture);
            }
        }

        public void OnReplayDespawn(int objectId)
        {
            var go = ResolveObject(objectId);
            if (go == null) return;

            var nob = go.GetComponent<NetworkObject>();
            if (nob != null && nob.IsSpawned && networkManager != null && networkManager.ServerManager.Started)
            {
                networkManager.ServerManager.Despawn(nob);
            }
            else
            {
                Destroy(go);
            }
        }

        public void OnReplaySyncListChange(int objectId, string listName, byte[] changeData)
        {
            var go = ResolveObject(objectId);
            if (go == null) return;

            ApplySyncListOperation(go, listName, changeData);
        }

        public void OnReplaySyncDictionaryChange(int objectId, string dictName, byte[] changeData)
        {
            var go = ResolveObject(objectId);
            if (go == null) return;

            ApplySyncDictionaryOperation(go, dictName, changeData);
        }

        public void OnReplayBroadcast(string broadcastTypeName, byte[] data)
        {
            if (networkManager == null) return;

            Type broadcastType = Type.GetType(broadcastTypeName);
            if (broadcastType == null)
            {
                // 외부 어셈블리까지 검색
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    broadcastType = asm.GetType(broadcastTypeName);
                    if (broadcastType != null) break;
                }
            }

            if (broadcastType == null)
            {
                Debug.LogWarning($"[FishNetReplayHandler] Broadcast type not found: {broadcastTypeName}");
                return;
            }

            try
            {
                object deserializedBroadcast = DeserializeFishNetValue(broadcastType, data);
                if (deserializedBroadcast == null) return;

                // [주의] 리플레이 환경에서 브로드캐스트 작동
                // 단순히 ClientManager.Broadcast 로 전송하면 서버로 보내는 것이 되어버림.
                // 수신 호출(콜백 트리거)을 흉내내거나 필요한 로직에 연결해야 합니다.
                Debug.Log($"[FishNetReplayHandler] OnReplayBroadcast processed: {broadcastTypeName}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FishNetReplayHandler] Broadcast replay failed: {e.Message}");
            }
        }

        #endregion

        #region RPC Reflection

        private MethodInfo FindRpcMethod(Type behaviourType, string rpcName)
        {
            var key = (behaviourType, rpcName);
            if (rpcMethodCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            // public + private 인스턴스 메서드 검색
            var method = behaviourType.GetMethod(rpcName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            rpcMethodCache[key] = method;
            return method;
        }

        private static object[] DeserializeRpcArgs(MethodInfo method, byte[] data)
        {
            if (data == null || data.Length == 0) return Array.Empty<object>();

            var reader = ReaderPool.Retrieve(data, null);
            try
            {
                byte argCount = reader.ReadByte();
                var paramInfos = method.GetParameters();

                // NetworkConnection 파라미터 필터링 (FishNet이 주입하는 파라미터)
                var deserializableParams = new List<ParameterInfo>(paramInfos.Length);
                for (int i = 0; i < paramInfos.Length; i++)
                {
                    if (!typeof(NetworkConnection).IsAssignableFrom(paramInfos[i].ParameterType))
                    {
                        deserializableParams.Add(paramInfos[i]);
                    }
                }

                var args = new object[paramInfos.Length];

                int readIndex = 0;
                for (int i = 0; i < paramInfos.Length; i++)
                {
                    if (typeof(NetworkConnection).IsAssignableFrom(paramInfos[i].ParameterType))
                    {
                        // FishNet 주입 파라미터 → 기본값 사용
                        args[i] = paramInfos[i].HasDefaultValue ? paramInfos[i].DefaultValue : null;
                    }
                    else if (readIndex < argCount)
                    {
                        args[i] = ReadFromReader(reader, paramInfos[i].ParameterType);
                        readIndex++;
                    }
                    else
                    {
                        args[i] = paramInfos[i].HasDefaultValue ? paramInfos[i].DefaultValue : null;
                    }
                }

                return args;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FishNetReplayHandler] RPC args deserialization failed: {e.Message}");
                return Array.Empty<object>();
            }
            finally
            {
                reader.Store();
            }
        }

        #endregion

        #region SyncVar Reflection

        private (FieldInfo field, Type valueType) FindSyncVarField(Type behaviourType, string varName)
        {
            var key = (behaviourType, varName);
            if (syncVarFieldCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var fields = behaviourType.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                if (field.Name != varName) continue;

                var fieldType = field.FieldType;
                if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(SyncVar<>))
                {
                    var valueType = fieldType.GetGenericArguments()[0];
                    var result = (field, valueType);
                    syncVarFieldCache[key] = result;
                    return result;
                }
            }

            syncVarFieldCache[key] = (null, null);
            return (null, null);
        }

        #endregion

        #region SyncCollection Replay

        /// <summary>SyncList 오퍼레이션을 재생합니다. 직렬화 형식: [byte:argCount=3] [byte:op] [int:index] [T:item]</summary>
        private void ApplySyncListOperation(GameObject go, string listName, byte[] changeData)
        {
            var behaviours = go.GetComponents<NetworkBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                var nb = behaviours[i];
                var field = nb.GetType().GetField(listName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null) continue;

                var fieldType = field.FieldType;
                if (!fieldType.IsGenericType) continue;
                if (fieldType.GetGenericTypeDefinition() != typeof(SyncList<>)) continue;

                var syncList = field.GetValue(nb);
                if (syncList == null) continue;

                Type itemType = fieldType.GetGenericArguments()[0];

                try
                {
                    // SerializeArgs format: [byte:argCount] [byte:op] [int:index] [T:item]
                    var reader = ReaderPool.Retrieve(changeData, null);
                    try
                    {
                        byte argCount = reader.ReadByte();
                        var op = (SyncListOperation)reader.ReadByte();
                        int index = reader.ReadInt32();
                        object item = (argCount >= 3) ? ReadFromReader(reader, itemType) : null;

                        switch (op)
                        {
                            case SyncListOperation.Add:
                                fieldType.GetMethod("Add")?.Invoke(syncList, new[] { item });
                                break;
                            case SyncListOperation.Insert:
                                fieldType.GetMethod("Insert")?.Invoke(syncList, new[] { (object)index, item });
                                break;
                            case SyncListOperation.Set:
                                // list[index] = item via indexer
                                var indexer = fieldType.GetProperty("Item");
                                indexer?.SetValue(syncList, item, new object[] { index });
                                break;
                            case SyncListOperation.RemoveAt:
                                fieldType.GetMethod("RemoveAt")?.Invoke(syncList, new object[] { index });
                                break;
                            case SyncListOperation.Clear:
                                fieldType.GetMethod("Clear")?.Invoke(syncList, null);
                                break;
                        }
                    }
                    finally
                    {
                        reader.Store();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[FishNetReplayHandler] SyncList '{listName}' replay failed: {e.Message}");
                }
                return;
            }
        }

        /// <summary>SyncDictionary 오퍼레이션을 재생합니다. 직렬화 형식: [byte:argCount=3] [byte:op] [TKey:key] [TValue:value]</summary>
        private void ApplySyncDictionaryOperation(GameObject go, string dictName, byte[] changeData)
        {
            var behaviours = go.GetComponents<NetworkBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                var nb = behaviours[i];
                var field = nb.GetType().GetField(dictName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null) continue;

                var fieldType = field.FieldType;
                if (!fieldType.IsGenericType) continue;
                if (fieldType.GetGenericTypeDefinition() != typeof(SyncDictionary<,>)) continue;

                var syncDict = field.GetValue(nb);
                if (syncDict == null) continue;

                Type[] typeArgs = fieldType.GetGenericArguments();
                Type keyType = typeArgs[0];
                Type valueType = typeArgs[1];

                try
                {
                    // SerializeArgs format: [byte:argCount] [byte:op] [TKey:key] [TValue:value]
                    var reader = ReaderPool.Retrieve(changeData, null);
                    try
                    {
                        byte argCount = reader.ReadByte();
                        var op = (SyncDictionaryOperation)reader.ReadByte();
                        object key = (argCount >= 2) ? ReadFromReader(reader, keyType) : null;
                        object value = (argCount >= 3) ? ReadFromReader(reader, valueType) : null;

                        switch (op)
                        {
                            case SyncDictionaryOperation.Add:
                                fieldType.GetMethod("Add")?.Invoke(syncDict, new[] { key, value });
                                break;
                            case SyncDictionaryOperation.Set:
                                // dict[key] = value via indexer
                                var indexer = fieldType.GetProperty("Item");
                                indexer?.SetValue(syncDict, value, new[] { key });
                                break;
                            case SyncDictionaryOperation.Remove:
                                fieldType.GetMethod("Remove")?.Invoke(syncDict, new[] { key });
                                break;
                            case SyncDictionaryOperation.Clear:
                                fieldType.GetMethod("Clear")?.Invoke(syncDict, null);
                                break;
                        }
                    }
                    finally
                    {
                        reader.Store();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[FishNetReplayHandler] SyncDictionary '{dictName}' replay failed: {e.Message}");
                }
                return;
            }
        }

        #endregion

        #region FishNet Deserialization

        private static object DeserializeFishNetValue(Type type, byte[] data)
        {
            if (data == null || data.Length == 0) return null;

            var reader = ReaderPool.Retrieve(data, null);
            try
            {
                return ReadFromReader(reader, type);
            }
            finally
            {
                reader.Store();
            }
        }

        private static object ReadFromReader(Reader reader, Type type)
        {
            if (!_readMethodCache.TryGetValue(type, out var method))
            {
                if (_readerReadGenericDef == null)
                {
                    // FishNet Reader의 Read<T>() 제네릭 메서드 정의 검색
                    var methods = typeof(Reader).GetMethods(BindingFlags.Instance | BindingFlags.Public);
                    for (int i = 0; i < methods.Length; i++)
                    {
                        var m = methods[i];
                        if (m.Name == "Read" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
                        {
                            _readerReadGenericDef = m;
                            break;
                        }
                    }
                }

                method = _readerReadGenericDef?.MakeGenericMethod(type);
                _readMethodCache[type] = method;
            }

            return method?.Invoke(reader, null);
        }

        #endregion

        #region Prefab Loading

        private GameObject LoadPrefab(string prefabName)
        {
            // Resources에서 로드 시도
            var prefab = Resources.Load<GameObject>(prefabName);
            if (prefab != null) return prefab;

            // FishNet의 PrefabObjects에서 검색
            if (networkManager != null && networkManager.SpawnablePrefabs != null)
            {
                int count = networkManager.SpawnablePrefabs.GetObjectCount();
                for (int i = 0; i < count; i++)
                {
                    var nob = networkManager.SpawnablePrefabs.GetObject(true, i);
                    if (nob != null && nob.name == prefabName)
                    {
                        return nob.gameObject;
                    }
                }
            }

            return null;
        }

        #endregion
    }
}
