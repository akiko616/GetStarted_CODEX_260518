using FishNet.Object;
using System;
using System.Collections.Generic;
using TRAINEE;
using UnityEngine;

public class TrainingLobbyGameLogic : NetworkBehaviour
{
    [SerializeField] private List<SystemBase> _systems = null;

    public void Init()
    {
        //기존 시나리오 데이터 리셋
        GameManager.Instance.ResetScenarioData();
        GameManager.Instance.SetCurrentScenario("100");

        GameManager.Instance.Systems = _systems;

        for (int i = 0; i < _systems.Count; i++)
        {
            _systems[i].Init();
        }

        VideoManager.Instance.VideoManagerSetup();


    }
    private void LobbyStart()
    {
        for (int i = 0; i < _systems.Count; i++)
        {
            _systems[i].OnGameStart();
        }
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();

        TRAINEE.NetworkManager.Instance.TimeManaager.OnUpdate += OnUpdate;
        TRAINEE.NetworkManager.Instance.TimeManaager.OnFixedUpdate += OnFixedUpdate;
        TRAINEE.NetworkManager.Instance.TimeManaager.OnLateUpdate += OnLateUpdate;

        LobbyStart();
    }


    public override void OnStopNetwork()
    {
        base.OnStopNetwork();

        TRAINEE.NetworkManager.Instance.TimeManaager.OnUpdate -= OnUpdate;
        TRAINEE.NetworkManager.Instance.TimeManaager.OnFixedUpdate -= OnFixedUpdate;
        TRAINEE.NetworkManager.Instance.TimeManaager.OnLateUpdate -= OnLateUpdate;
    }
    private void OnUpdate()
    {
        if(!TRAINEE.NetworkManager.Instance.IsLobby)
        {
            return;
        }

        float time = Time.deltaTime;

        for (int i = 0; i < _systems.Count; i++)
        {
            _systems[i].OnUpdate(time);
        }
    }
    private void OnFixedUpdate()
    {
        if (!TRAINEE.NetworkManager.Instance.IsLobby)
        {
            return;
        }

        float time = Time.fixedDeltaTime;

        for (int i = 0; i < _systems.Count; i++)
        {
            _systems[i].OnFixedUpdate(time);
        }
    }

    private void OnLateUpdate()
    {
        if (!TRAINEE.NetworkManager.Instance.IsLobby)
        {
            return;
        }

        float time = Time.deltaTime;

        for (int i = 0; i < _systems.Count; i++)
        {
            _systems[i].OnLateUpdate(time);
        }
    }




}
