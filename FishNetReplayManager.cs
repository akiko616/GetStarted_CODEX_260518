using System;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using ReplaySystem.Capture;
using ReplaySystem.Core;
using UnityEngine;

namespace ReplaySystem.Network
{
    /// <summary>
    /// FishNet과 ReplaySystem을 연결하는 중앙 관리자입니다.
    /// 전역 네트워크 이벤트(Tick, Spawn/Despawn)를 후킹하여 ReplayRecorder와 NetworkReplaySync에 전달합니다.
    /// Spawn/Despawn 기록의 유일한 권한자 — FishNetReplayObserver에서는 기록하지 않습니다.
    /// </summary>
    public class FishNetReplayManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private ReplayRecorder recorder;
        [SerializeField] private NetworkReplaySync networkSync;

        [Header("Settings")]
        [SerializeField] private bool autoRecordOnServerStart = true;

        private void Start()
        {
            if (networkManager == null)
            {
                networkManager = FindFirstObjectByType<NetworkManager>();
            }

            if (recorder == null)
            {
                recorder = FindFirstObjectByType<ReplayRecorder>();
            }

            if (networkSync == null)
            {
                networkSync = FindFirstObjectByType<NetworkReplaySync>();
            }

            if (networkManager != null)
            {
                networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
                networkManager.TimeManager.OnPostTick += OnPostTick;

                if (networkManager.ServerManager.Objects != null)
                {
                    networkManager.ServerManager.Objects.OnSpawnedAdd += OnNetworkObjectSpawned;
                    networkManager.ServerManager.Objects.OnSpawnedRemove += OnNetworkObjectDespawned;
                }
            }

            // Initialize Recorders
            if (networkSync != null)
            {
                FishNetRpcRecorder.Initialize(networkSync);
                FishNetBroadcastRecorder.Initialize(networkSync);
            }
        }

        private void OnDestroy()
        {
            if (networkManager != null)
            {
                networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
                networkManager.TimeManager.OnPostTick -= OnPostTick;
                
                if (networkManager.ServerManager != null && networkManager.ServerManager.Objects != null)
                {
                    networkManager.ServerManager.Objects.OnSpawnedAdd -= OnNetworkObjectSpawned;
                    networkManager.ServerManager.Objects.OnSpawnedRemove -= OnNetworkObjectDespawned;
                }
            }
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == FishNet.Transporting.LocalConnectionState.Started)
            {
                if (autoRecordOnServerStart && recorder != null && !recorder.IsRecording)
                {
                    recorder.StartRecording();
                    Debug.Log("[FishNetReplayManager] Auto-started recording on Server Start");
                }
            }
            else if (args.ConnectionState == FishNet.Transporting.LocalConnectionState.Stopped)
            {
                if (autoRecordOnServerStart && recorder != null && recorder.IsRecording)
                {
                    recorder.StopRecording();
                    Debug.Log("[FishNetReplayManager] Auto-stopped recording on Server Stop");
                }
            }
        }

        private void OnPostTick()
        {
            if (networkManager == null || networkSync == null || !networkSync.IsRecording) return;
            
            // FishNet의 틱 시스템과 Replay 틱 시스템 연동
            networkSync.UpdateTick(networkManager.TimeManager.Tick, (float)networkManager.TimeManager.TicksToTime());
        }

        /// <summary>
        /// 네트워크 오브젝트 스폰 시 Replay ID로 통일하여 기록합니다.
        /// TransformCapture가 있으면 FishNet ID → Replay ID 매핑을 등록합니다.
        /// </summary>
        private void OnNetworkObjectSpawned(int objectId, NetworkObject nob)
        {
            if (networkSync == null || !networkSync.IsRecording) return;
            
            // GC 최적화: Replace 대신 IndexOf + Substring 사용
            string objName = nob.name;
            int cloneIdx = objName.IndexOf("(Clone)", StringComparison.Ordinal);
            string prefabName = cloneIdx >= 0 ? objName.Substring(0, cloneIdx).Trim() : objName;
            
            // TransformCapture가 있으면 ID 매핑 등록 후 Replay ID로 통일
            var transformCapture = nob.GetComponent<TransformCapture>();
            int finalId = objectId;
            
            if (transformCapture != null && transformCapture.ObjectId >= 0)
            {
                networkSync.RegisterNetworkIdMapping(objectId, transformCapture.ObjectId);
                finalId = transformCapture.ObjectId;
            }
            else
            {
                Debug.LogWarning($"[FishNetReplayManager] NetworkObject '{prefabName}' (FishNetId: {objectId}) has no TransformCapture with valid ID. " +
                                 "Transform tracking will be unavailable during replay.");
            }
            
            networkSync.RecordSpawn(finalId, prefabName, nob.OwnerId, nob.transform.position, nob.transform.rotation);
        }

        /// <summary>
        /// 네트워크 오브젝트 디스폰 시 Replay ID로 변환하여 기록합니다.
        /// </summary>
        private void OnNetworkObjectDespawned(int objectId, NetworkObject nob)
        {
            if (networkSync == null || !networkSync.IsRecording) return;

            int finalId = networkSync.ResolveToReplayId(objectId);
            networkSync.RecordDespawn(finalId);
        }
    }
}
