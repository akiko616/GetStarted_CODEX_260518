using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using GameInstancePlugin.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRAINEE
{
    // 모든 플레이어 훈련 기본 장착 장비 브로드 캐스트
    public struct PlayerEquipSetupBroadcast : IBroadcast
    {
        public PlayerEquipSetupData[] _players;
    }

    // 플레이어 훈련 장착 장비 교체 브로드 캐스트
    public struct PlayerEquipBroadcast : IBroadcast
    {
        public PlayerReqEquipData _player;
    }


    public class PlayerSpawnSystem : SystemBase,IEvent
    {
        private Dictionary<string, ControllerBase> _playerDict = new Dictionary<string, ControllerBase>();      // 현재 참여한 플레이어 검색용
        private ControllerBase _playerController = null;        // 실제 인게임 조정하는 플레이어
        private List<ControllerBase> _players = null;           // 플레이어 리스트

        private Vector3 _spawnPos;
        private Quaternion _spawnRot;


        public ControllerBase Player { get => _playerController; }


        private List<CharEquipData> _currentEquipDatas = null;
        private List<SpawnPointData> _currentSpawnDatas = null;
        private Dictionary<string, PlayerEquipSetupData> _currentPlayerSpawnData = null;
#if GAMEINSTANCE
        private void Start()
        {
            var fishNetHandler = FindAnyObjectByType<FishNetManagetHandler>();
            // 핸들러의 스폰 요청을 내가 가로채서 직접 프리팹을 생성하겠다!
            fishNetHandler.OnSpawnPlayerOverride += CustomServerSpawnLogic;
        }


        private void CustomServerSpawnLogic(NetworkConnection conn, AuthBroadcastData data, FishNet.Managing.NetworkManager manager)
        {
            if (string.Equals(data.Role, "viewer", System.StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"관전자 접속 (스폰 스킵): {conn.ClientId}");
                PlayerEquipSetupBroadcast syncMsg = new PlayerEquipSetupBroadcast
                {
                    _players = _currentPlayerSpawnData.Values.ToArray() 
                };

                manager.ServerManager.Broadcast(conn, syncMsg);
                return;
            }

            // 코스튬 및 장착 아이템 찾기
            CharEquipData equip= FindCharacterEquip(data.Role);
            string mainInsId = !string.IsNullOrEmpty(equip.equipid) ? $"{equip.equipid}_{System.Guid.NewGuid()}" : "";
            string subInsId = !string.IsNullOrEmpty(equip.subequipid) ? $"{equip.subequipid}_{System.Guid.NewGuid()}" : "";

            PlayerEquipSetupData newState = new PlayerEquipSetupData
            {
                _playerId = data.PlayerId.ToString(),
                _playerRoleId = data.Role,
                _mainEquipId = equip.equipid,
                _mainEquipInsId = mainInsId,
                _subEquipId = equip.subequipid,
                _subEquipInsId = subInsId,
                _currentSlotIdx = 0,
            };

            _currentPlayerSpawnData[data.PlayerId.ToString()] = newState;

            // 장착 아이템 아이템 매니저에 생성
            SetupPlayerEquipItem(equip.equipid, mainInsId,data.PlayerId.ToString());
            SetupPlayerEquipItem(equip.subequipid, subInsId, data.PlayerId.ToString());

            // 플레이어 코스튬 세팅
            ControllerBase player = SetupPlayerCostume(data.PlayerId.ToString(), data.Role, equip.PlayerCostume.ToString());

            if (player != null)
            {
                manager.ServerManager.Spawn(player.gameObject, conn);
            }
        }
        private ControllerBase SetupPlayerCostume(string playerid, string role, string cosid)
        {
            Vector3 spawnPos = Vector3.zero;
            Quaternion spawnRot = Quaternion.identity;
            if(_currentSpawnDatas != null)
            {
                for(int i = 0; i < _currentSpawnDatas.Count; i++)
                {
                    if (_currentSpawnDatas[i].spawnid == role)
                    {
                        spawnPos = new Vector3(_currentSpawnDatas[i].PosX, _currentSpawnDatas[i].PosY, _currentSpawnDatas[i].PosZ);
                        spawnRot = Quaternion.Euler(_currentSpawnDatas[i].RotX, _currentSpawnDatas[i].RotY, _currentSpawnDatas[i].RotZ);
                        break;
                    }
                }

                Debug.Log($"[서버] Player SpawnData 포지션,로테이션 세팅");
            }

            string path = $"{Const.Path.BUILT_IN_CHARACTER_PATH}{cosid}/Player";

            ControllerBase player = LdResources.Load<ControllerBase>(path, spawnPos, spawnRot, this.transform);

            if (player != null)
            {
                player.InitPlayer(playerid, role);

                if (!_playerDict.ContainsKey(player.Id))
                {
                    _playerDict.Add(player.Id, player);
                    _players.Add(player);
                }

                return player;
            }

            return null;
        }

        private void SetupPlayerEquipItem(string mainid, string mainInsId,string playerid)
        {
            if (!string.IsNullOrEmpty(mainid) && !mainid.Equals("-1"))
            {
                ItemManager.Instance.ServerSpawnItem(mainid, mainInsId, Vector3.zero,Quaternion.identity, playerid, false);
            }
        }

        private CharEquipData FindCharacterEquip(string roleId)
        {
            if(_currentEquipDatas != null)
            {
                for(int i = 0; i < _currentEquipDatas.Count; i++)
                {
                    if (_currentEquipDatas[i].roleid.Equals(roleId))
                    {
                        return _currentEquipDatas[i];
                    }
                }
            }

            return null;
        }
#endif
        public override void Init()
        {
            if (_players == null)
                _players = new List<ControllerBase>();
            else
                _players.Clear();

            if (_playerDict == null)
                _playerDict = new Dictionary<string, ControllerBase>();
            else
                _playerDict.Clear();

            if (_currentEquipDatas == null)
                _currentEquipDatas = new List<CharEquipData>();
            else
                _currentEquipDatas.Clear();

            if (_currentPlayerSpawnData == null)
                _currentPlayerSpawnData = new Dictionary<string, PlayerEquipSetupData>();
            else
                _currentPlayerSpawnData.Clear();

            if(_currentSpawnDatas == null)
            {
                _currentSpawnDatas = new List<SpawnPointData>();
            }
            else
            {
                _currentSpawnDatas.Clear();
            }

#if !GAMEINSTANCE
            if (NetworkManager.Instance.ClientManager != null)
            {
                NetworkManager.Instance.ClientManager.RegisterBroadcast<PlayerEquipSetupBroadcast>(OnClientEquipSyncReceived);
                NetworkManager.Instance.ClientManager.RegisterBroadcast<PlayerEquipBroadcast>(OnClientEquipReceived);
            }
#else
            if (InstanceFinder.ServerManager != null)
            {
                InstanceFinder.ServerManager.RegisterBroadcast<PlayerEquipBroadcast>(OnServerEquipReceived);
            }

            GameEventManager.Instance.Subscribe(EEventType.Spawn, this);
#endif
            base.Init();
        }

        public override void OnGameStart()
        {
            base.OnGameStart();

            for (int i = _players.Count - 1; i >= 0; i--)
            {
                Debug.Log($"{_players[i].gameObject.name} try setup ");
                if (_players[i] == null) continue;

                _players[i].Setup();

            }
        }

        public override void OnDestroy()
        {
            _currentPlayerSpawnData.Clear();
            _currentEquipDatas.Clear();
            _playerDict.Clear();
            _players.Clear();
            _playerController = null;
            _currentSpawnDatas = null;
#if !GAMEINSTANCE
            if (NetworkManager.Instance.ClientManager != null)
            {
                NetworkManager.Instance.ClientManager.UnregisterBroadcast<PlayerEquipSetupBroadcast>(OnClientEquipSyncReceived);
                NetworkManager.Instance.ClientManager.UnregisterBroadcast<PlayerEquipBroadcast>(OnClientEquipReceived);
            }
#else
            GameEventManager.Instance.UnSubscribe(EEventType.Spawn, this);

            var fishNetHandler = FindFirstObjectByType<FishNetManagetHandler>();
            if (fishNetHandler != null)
            {
                fishNetHandler.OnSpawnPlayerOverride -= CustomServerSpawnLogic;
            }

            if (InstanceFinder.ServerManager != null)
            {
                InstanceFinder.ServerManager.UnregisterBroadcast<PlayerEquipBroadcast>(OnServerEquipReceived);
            }
#endif
        }

        public void OnRegisterPlayer(string id, ControllerBase player)
        {
            if (_players != null)
            {
                if (!_playerDict.ContainsKey(id))
                {
                    player.transform.SetParent(this.transform, false);
                    _playerDict.Add(id, player);
                    _players.Add(player);
#if DrillSergeant
                    // 플레이어 레이어 설정
                    //MonitoringPoolSystem.Instance.SetLayerRecursively(player.gameObject, LayerMask.NameToLayer("Minimap"));

                    // 플레이어 비콘 설정

#endif
                    Debug.Log($"[시스템] 플레이어 {id} 등록 완료! (현재 총 {_playerDict.Count}명)");
                }

                //호출 타이밍 문제로 함수화 시켜서 따로 세팅하도록 변경
                /*                if (player.IsLocalPlayer)
                                {
                                    _playerController = player;
                                }*/
            }
        }

        public void OnRegisterLocalPlayer(ControllerBase player)
        {
            if (_playerController == null && player.IsLocalPlayer)
            {
                Debug.Log($"[클라] 로컬 플레이어 등록 완료)");
                _playerController = player;
            }
        }

        public void OnUnRegisterPlayer(string id, ControllerBase player)
        {
#if !GAMEINSTANCE
            if (_playerDict.ContainsKey(id))
            {
                if (_playerController == player)
                {
                    _playerController = null;
                }

                _playerDict.Remove(id);
                _players.Remove(player);
                Debug.Log($"[시스템] 플레이어 {id} 퇴장 처리 완료. (남은 인원: {_playerDict.Count})");
            }
#endif
        }

        public ControllerBase GetPlayerById(string id)
        {
            if (_playerDict.TryGetValue(id, out ControllerBase player))
            {
                return player;
            }
            Debug.Log($"[시스템] ID가 {id}인 플레이어를 찾을 수 없습니다!");
            return null;
        }

        public override void OnUpdate(float deltaTime)
        {
            for (int i = _players.Count - 1; i >= 0; i--)
            {
                if (_players[i] == null) continue;

                _players[i].OnUpdate(deltaTime);
            }
        }

        public override void OnFixedUpdate(float deltaTime)
        {

            for (int i = _players.Count - 1; i >= 0; i--)
            {
                if (_players[i] == null) continue;

                _players[i].OnFixedUpdate(deltaTime);
            }
        }

        public override void OnLateUpdate(float deltaTime)
        {
            for (int i = _players.Count - 1; i >= 0; i--)
            {
                if (_players[i] == null) continue;

                _players[i].OnLateUpdate(deltaTime);
            }
        }

        public override async UniTask LoadingAsync(Action<float, string> onProgress, int delaytime)
        {
#if GAMEINSTANCE
            // 시나리오 별 플레이어 기본 장착 장비 데이터 로드 및 준비
            if (_currentEquipDatas == null)
            {
                _currentEquipDatas = new List<CharEquipData>();
            }
            else
            {
                _currentEquipDatas.Clear();
            }

            int totalCnt = 0;
            int currentCnt = 0;
            float progress = 0f;
            List<CharEquipData> charEquipDatas = DataManager.Instance.GetAllData<CharEquipData>(EDataType.CharEquipData);

            totalCnt = charEquipDatas.Count + 1;
            for (int i = 0; i < charEquipDatas.Count; i++)
            {
                if (GameManager.Instance.GetCurrentScenario == charEquipDatas[i].scenario)
                {
                    _currentEquipDatas.Add(charEquipDatas[i]);
                }

                progress = (float)currentCnt / totalCnt;
                onProgress?.Invoke(progress, $"스폰 데이터 로드 중...");
            }

            List<SpawnPointData> spawnData = DataManager.Instance.GetAllData<SpawnPointData>(EDataType.SpawnPointData);

            for (int i = 0; i < spawnData.Count; i++)
            {
                if (spawnData[i].scenario == GameManager.Instance.GetCurrentScenario)
                {
                    _currentSpawnDatas.Add(spawnData[i]);
                }
            }

            progress = (float)currentCnt + 1 / totalCnt;
            onProgress?.Invoke(progress, $"스폰 데이터 로드 중...");

            await UniTask.Delay(delaytime);
#endif
            await UniTask.Delay(delaytime);
            onProgress?.Invoke(1f, $"교육생 준비 중...");
        }



        public void OnEvent(GameEvent data)
        {
#if GAMEINSTANCE
            switch(data._type)
            {
                case EEventType.Spawn:
                    {
                        Debug.Log("플레이어 장비 착용 시작");
                        OnPlayerEquipItem();
                    }
                    break;
            }
#endif
        }

        private void OnPlayerEquipItem()
        {
            if (InstanceFinder.ServerManager == null)
            {
                return;
            }

            foreach (var data in _currentPlayerSpawnData.Values)
            {
                if (_playerDict.TryGetValue(data._playerId, out ControllerBase player))
                {
                    // 1. 메인 무기 세팅
                    if (!string.IsNullOrEmpty(data._mainEquipId) && !data._mainEquipId.Equals("-1"))
                    {
                        player.ApplyEquipVisuals(data._mainEquipId, data._mainEquipInsId);
                    }

                    // 2. 서브 무기 세팅
                    if (!string.IsNullOrEmpty(data._subEquipId) && !data._subEquipId.Equals("-1"))
                    {
                        player.ApplyEquipVisuals(data._subEquipId, data._subEquipInsId);
                    }

                    // 3. 기본 활성화 슬롯으로 스왑 (보통 0번 슬롯)
                    int startSlot = data._currentSlotIdx != 0 ? data._currentSlotIdx : 0;
                    player.ApplyEquipVisuals(startSlot);
                }
            }

            PlayerEquipSetupBroadcast syncMsg = new PlayerEquipSetupBroadcast
            {
                _players = _currentPlayerSpawnData.Values.ToArray()
            };

            Debug.Log($"[서버] 플레이어 장비 착용 브로드캐스트");
            InstanceFinder.ServerManager.Broadcast(syncMsg);
        }

        private void OnPlayerEquipItem(PlayerEquipBroadcast msg)
        {
            if (InstanceFinder.ServerManager == null)
            {
                return;
            }

            Debug.Log($"[서버] 플레이어 장비 교체 브로드캐스트");
            InstanceFinder.ServerManager.Broadcast(msg);
        }

        private void OnClientEquipSyncReceived(PlayerEquipSetupBroadcast msg, FishNet.Transporting.Channel channel)
        {
            // 클라가 장비 장착에 대한 브로드 캐스트 받는부분
            Debug.Log($"[클라] 플레이어 장비 착용 브로드캐스트 수신");
            PlayerEquipSetupData[] data = msg._players;

            for (int i = 0; i < data.Length; i++)
            {
                if (_playerDict.TryGetValue(data[i]._playerId, out ControllerBase player))
                {
                    ProcessClientEquipAsync(data[i]).Forget();
                }
            }
        }

        private void OnClientEquipReceived(PlayerEquipBroadcast msg, FishNet.Transporting.Channel channel)
        {
            // 클라가 장비 장착에 대한 브로드 캐스트 받는부분
            Debug.Log($"[클라] 플레이어 장비 착용 브로드캐스트 수신");
            PlayerReqEquipData data = msg._player;

            if (_playerDict.TryGetValue(data._playerId, out ControllerBase player))
            {
                if (data._isSwap)
                {
                    Debug.Log($"[클라] {data._playerId} 스왑 브로드캐스트 수신 -> {data._slotIdx}번 슬롯");
                    player.ApplyEquipVisuals(data._slotIdx);
                }
                else
                {
                    Debug.Log($"[클라] {data._playerId} 줍기 브로드캐스트 수신 -> {data._reqEquipId}");
                    ProcessClientEquipAsync(player, data).Forget();
                }
            }
        }

        public void OnRequestEquipToServer(string playerId, string equipId,string instanceId,int slotIdx)
        {
            if (NetworkManager.Instance.ClientManager == null)
            {
                return;
            }

            PlayerEquipBroadcast request = new PlayerEquipBroadcast
            {

                _player = new PlayerReqEquipData
                {
                    _playerId = playerId,
                    _reqEquipId = equipId,
                    _reqEquipInsId = instanceId,
                    _slotIdx = slotIdx,
                    _isSwap = false
                }
            };
            NetworkManager.Instance.ClientManager.Broadcast(request);

            Debug.Log($"[클라이언트] {playerId}가 서버로 아이템({equipId}) 줍기 요청 발송!");
        }

        public void OnRequestEquipToServer(string playerId, int slotIdx)
        {
            if (NetworkManager.Instance.ClientManager == null)
            {
                return;
            }

            PlayerEquipBroadcast request = new PlayerEquipBroadcast
            {

                _player = new PlayerReqEquipData
                {
                    _playerId = playerId,
                    _slotIdx = slotIdx,
                    _isSwap = true
                }
            };
            NetworkManager.Instance.ClientManager.Broadcast(request);
            Debug.Log($"[클라이언트] 서버로 {slotIdx}번 슬롯 스왑 요청 발송!");
        }

        private async UniTask ProcessClientEquipAsync(PlayerEquipSetupData spawnData)
        {
            await UniTask.WaitUntil(() => _playerDict.ContainsKey(spawnData._playerId));
            ControllerBase player = _playerDict[spawnData._playerId];

            if (player != null)
            {
                Debug.Log($"[클라] 장비 착용");

                if (!string.IsNullOrEmpty(spawnData._mainEquipId) && !spawnData._mainEquipId.Equals("-1"))
                {
                    await UniTask.WaitUntil(() => ItemManager.Instance.IsItem(spawnData._mainEquipId, spawnData._mainEquipInsId));

                    player.ApplyEquipVisuals(spawnData._mainEquipId,spawnData._mainEquipInsId);

                    Debug.Log($"[클라] 메인 장비 착용 : {spawnData._mainEquipId}");
                }

                // 3. 서브 무기(무전기 등) 대기 후 장착!
                if (!string.IsNullOrEmpty(spawnData._subEquipId) && !spawnData._subEquipId.Equals("-1"))
                {
                    await UniTask.WaitUntil(() => ItemManager.Instance.IsItem(spawnData._subEquipId, spawnData._subEquipInsId));
                    player.ApplyEquipVisuals(spawnData._subEquipId, spawnData._subEquipInsId);

                    Debug.Log($"[클라] 서브 장비 착용 : {spawnData._subEquipId}");
                }
            }
            else
            {
                Debug.LogError($"[클라] Player가 존재 하지 않습니다 : {spawnData._playerId}");
            }
        }

        private async UniTask ProcessClientEquipAsync(ControllerBase player, PlayerReqEquipData data)
        {
            await UniTask.WaitUntil(() => ItemManager.Instance.IsItem(data._reqEquipId, data._reqEquipInsId));

            // 도착하면 손에 쥐여줌
            player.ApplyEquipVisuals(data._reqEquipId, data._reqEquipInsId);

            // 🌟 주운 직후 손에 들게 하려면 해당 슬롯으로 자동 스왑 (선택 사항)
            player.ApplyEquipVisuals(data._slotIdx);

        }

        public void OnChangeLocalPlayerState(EStateType state, int slotidx = 0, bool isHold = false)
        {
            if(_playerController != null)
            {
                if (_playerController.EquipHandler.CurrentActiveSlot != (EInventoryType)slotidx)
                {
                    Debug.Log("OnChangeLocalPlayerState");
                    OnRequestEquipToServer(_playerController.Id, slotidx);

                    _playerController.ApplyEquipVisuals(slotidx);
                }
                
                //_playerController.IsHoldAction = isHold;
                _playerController.ChangeState(state);
            }
        }

        public void OnLocalPlayerActionExit()
        {
            if (_playerController != null)
            {
                Debug.Log("OnLocalPlayerActionExit");
                _playerController.ProcessCommand(new EquipActionCommand());
            }
        }

        public void RequestInteractionToServer(GameModelEventBase payload)
        {
            if(payload != null)
            {
                GameEvent gameEvent = new GameEvent();
                gameEvent._type = EEventType.Interaction;
                gameEvent._id = payload._id;
                gameEvent._param = payload;

                GameEventManager.Instance.Publish(gameEvent);

                Debug.Log($"[PlayerSpawnSystem] {payload._callerId} ➡️ {payload._id} 최종 상호작용(Interaction) 패킷 발송 완료");
            }
        }

#if GAMEINSTANCE
        private void OnServerEquipReceived(NetworkConnection conn, PlayerEquipBroadcast msg, FishNet.Transporting.Channel channel)
        {
            if (msg._player._isSwap)
            {
                Debug.Log($"[서버] {msg._player._playerId}의 {msg._player._slotIdx}번 슬롯 스왑 요청 승인");

                if (_currentPlayerSpawnData.TryGetValue(msg._player._playerId, out PlayerEquipSetupData data))
                {
                    data._currentSlotIdx = msg._player._slotIdx; 
                    _currentPlayerSpawnData[msg._player._playerId] = data;

                    if (_playerDict.TryGetValue(msg._player._playerId, out ControllerBase player))
                    {
                        player.ApplyEquipVisuals(msg._player._slotIdx);
                    }

                    OnPlayerEquipItem(msg);
                }
                else
                {
                    Debug.LogError($"[서버] {msg._player._playerId}의 장착 교체 거절됨 (잘못된 플레이어 아이디)");
                }

            }
            else
            {
                if (ItemManager.Instance.OnPickUpItem(msg._player._reqEquipId, msg._player._reqEquipInsId, msg._player._playerId))
                {
                    if (_currentPlayerSpawnData.TryGetValue(msg._player._playerId, out PlayerEquipSetupData data))
                    {
                        if (msg._player._slotIdx == 0)
                        {
                            data._mainEquipId = msg._player._reqEquipId;
                            data._mainEquipInsId = msg._player._reqEquipInsId;
                        }
                        else if (msg._player._slotIdx == 1)
                        {
                            data._subEquipId = msg._player._reqEquipId;
                            data._subEquipInsId = msg._player._reqEquipInsId;
                        }

                        // 장부 덮어쓰기
                        _currentPlayerSpawnData[msg._player._playerId] = data;

                        if (_playerDict.TryGetValue(msg._player._playerId, out ControllerBase player))
                        {
                            // 장비 풀에서 꺼내어 인벤토리에 세팅
                            player.ApplyEquipVisuals(msg._player._reqEquipId, msg._player._reqEquipInsId);

                            // 방금 주운 무기를 바로 손에 들게 하려면 스왑까지 연계
                            player.ApplyEquipVisuals(msg._player._slotIdx);
                        }

                        Debug.Log($"[서버] {msg._player._playerId} 플레이어의 {msg._player._slotIdx}번 장비 상태가 갱신되었습니다. 브로드캐스트를 발송합니다.");

                        OnPlayerEquipItem(msg);
                    }
                }
                else
                {
                    Debug.LogError($"[서버] {msg._player._playerId}의 장착 요청 거절됨 (이미 누가 주웠거나 없는 아이템)");
                }
            }
        }
#endif
    }
}
