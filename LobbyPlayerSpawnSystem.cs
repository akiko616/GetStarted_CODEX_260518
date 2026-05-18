using Cysharp.Threading.Tasks;
using FishNet;
using System;
using System.Collections.Generic;
using TRAINEE;
using UnityEngine;
public class LobbyPlayerSpawnSystem : SystemBase
{
    ControllerBase _localPlayer = null;
    private Vector3 _spawnPos;
    private Quaternion _spawnRot;

    public override void Init()
    {
        base.Init();
    }

    public override void OnUpdate(float deltaTime)
    {
        if (_localPlayer != null)
            _localPlayer.OnUpdate(deltaTime);
    }
    public override void OnFixedUpdate(float deltaTime)
    {
        if (_localPlayer != null)
            _localPlayer.OnFixedUpdate(deltaTime);
    }
    public override void OnLateUpdate(float deltaTime)
    {
        if (_localPlayer != null)
            _localPlayer.OnLateUpdate(deltaTime);
    }
    public void SpawnLocalPlayer(ControllerBase localPlayer)
    {
        _localPlayer = localPlayer;
    }

    public override async UniTask LoadingAsync(Action<float, string> onProgress, int delayTime)
    {
        List<SpawnPointData> spawnData = DataManager.Instance.GetAllData<SpawnPointData>(EDataType.SpawnPointData);

        SpawnPointData data = null;

        for (int i = 0; i < spawnData.Count; i++)
        {
            if (spawnData[i].scenario == GameManager.Instance.GetCurrentScenario)
            {
                data = spawnData[i];
            }
        }

        if (data != null)
        {
            _spawnPos = new Vector3(data.PosX, data.PosY, data.PosZ);
            _spawnRot = Quaternion.Euler(data.RotX, data.RotY, data.RotZ);
        }

        LoadingHelper.RegisterLoading(new Loading(TrySpawn));

        await UniTask.Delay(delayTime);

        onProgress?.Invoke(1, $"로비 플레이어 스폰 데이터 로드 중");
    }
    private async UniTask TrySpawn(Action<float, string> onProgress, int delaytime)
    {

        #region Net Check
        var nm = InstanceFinder.NetworkManager;

        if (nm == null)
        {
            onProgress?.Invoke(1f, "네트워크 매니저 없음");
            return;
        }

        onProgress?.Invoke(0f, "서버시작 대기중...");
        bool isServerTimeout = await UniTask.WaitUntil(() => nm.IsServerStarted)
                                            .TimeoutWithoutException(TimeSpan.FromSeconds(10));
        if (isServerTimeout)
        {
            onProgress?.Invoke(1f, "서버 타임아웃");
            return;
        }

        onProgress?.Invoke(0.3f, "클라이언트 연결중...");

        bool isClientTimeout = await UniTask.WaitUntil(() => nm.ClientManager.Started)
                                            .TimeoutWithoutException(TimeSpan.FromSeconds(10));
        if (isClientTimeout)
        {
            onProgress?.Invoke(1f, "클라이언트 연결 타임아웃");
            return;
        }
        #endregion

        #region Player Spawn
        if (nm.IsServerStarted)
        {
            GameObject prefab = LdResources.Load<GameObject>("1.Prefabs/1.Characters/Unit/Player");
            GameObject instance = Instantiate(prefab, _spawnPos, _spawnRot, this.transform);

            ControllerBase player = instance.GetComponent<ControllerBase>();

            if (player != null) player.Init("0");

            nm.ServerManager.Spawn(instance, nm.ClientManager.Connection);
            onProgress?.Invoke(1f, "스폰 완료");
        }
        #endregion
    }
}
