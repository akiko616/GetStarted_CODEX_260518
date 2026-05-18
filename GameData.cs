
using FishNet.Documenting;
using FishNet.Object.Synchronizing;
using FishNet.Object.Synchronizing.Internal;
using FishNet.Serializing;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace TRAINEE
{
    public class AnimCallbackData
    {
        public float _targetTime;
        public Action _callback;
        public int _layerIndex;
        public bool _isFired;

        public int _initialStateHash;     // 🌟 추가: 등록 당시의 상태 해시값
        public int? _lockedStateHash = null;
        public AnimCallbackData(float targetTime,int layerIndex ,Action callback, int initialStateHash)
        {
            _targetTime = targetTime;
            _layerIndex = layerIndex;
            _callback = callback;
            _initialStateHash = initialStateHash;
        }
    }

    public class GameEvent
    {
        public EEventType _type;
        public string _id;          // 이벤트 받는 대상의 Id
        public GameModelEventBase _param;       // 
    }

    public abstract class GameModelEventBase
    {
        public string _id;
        public bool isServerConfirmed = false;
        public string _callerId;
        public abstract GameEventType GetEventType();
    }

    public class EquipEventData : GameModelEventBase
    {
        public string _instanceId;
        public bool _isDrop;
        public override GameEventType GetEventType() => GameEventType.Equip;
    }
    public class LobbyItemEventData : GameModelEventBase
    {
        public string _instanceId;
        public ELobbyItemType _type;
        public override GameEventType GetEventType() => GameEventType.LobbyItem;
    }

    public class DoorEventData : GameModelEventBase
    {
        public EDoorState state;
        public bool _isOpen;
        public override GameEventType GetEventType() => GameEventType.Door;
    }

    public class TerrainEventData : GameModelEventBase
    {
        public EWeather _weather;
        public override GameEventType GetEventType() => GameEventType.Terrain;
    }

    public class RescueeEventData : GameModelEventBase
    {
        public string _rescueeId;
        public ERescueeState _currentState;
        public ERescueeType _rescueeType;
        public override GameEventType GetEventType() => GameEventType.Rescuee;
    }

    public class DisasterEventData : GameModelEventBase
    {
        public string _disasterid;
        public override GameEventType GetEventType() => GameEventType.Disaster;
    }

    public class ChatData
    {
        public ChatChannel channel;
        public ChatType chatType;
        public DateTime time;
        public string message;

        public ChatData(ChatChannel channel, ChatType chatType, string message, DateTime time)
        {
            this.channel = channel;
            this.chatType = chatType;
            this.message = message;
            this.time = time;
        }
    }

    [Serializable]
    public struct EventData
    {
        public EScenario _scenario;
        public List<ModelEvent> events;
    }

    [Serializable]
    public struct ModelEvent
    {
        public string id;                       // 1.id (이벤트가 발생하는애)
        public List<string> _reactions;         // 2.리스트로 연쇄 반응이 필요한 id 없으면 독립
        public EModelEventType _type;           // 3.발생할 이벤트 키 (화재,감전,점멸,낙상,부서짐 등등)
    }

    #region Model Init

    [Serializable]
    public struct DoorProperty
    {
        public string _id;              // 키 값
        public EDoorState _state;       // 문의 상태 : 열림,닫힘,잠김
        public bool _isOpen;            // 열려있는지
    }

    [Serializable]
    public struct StructureProperty
    {
        public string _id;

    }

    [Serializable]
    public struct RoomProperty
    {

    }
    #endregion

    #region DialogData

    public struct DialogData
    {
        public EDialogType _type;
        public string _movieId;
        public string _roleName;
        public string _speech;
        public int _starttime;
        public int _endtime;
    }


    public struct DialogPopupData
    {
        public EPopupType _type;
        public string _title;
        public string _speech1;
        public string _speech2;
    }

    #endregion

    [Serializable]
    public struct DynamicPropData
    {
        public string viewId;    
        public int propIndex;    
        public Vector3 position;
        public Quaternion rotation;
    }

    [Serializable]
    public struct PlayerEquipSetupData
    {
        public string _playerId;
        public string _playerRoleId;
        public string _mainEquipId;
        public string _mainEquipInsId;
        public string _subEquipId;
        public string _subEquipInsId;
        public int _currentSlotIdx;
    }

    [Serializable]
    public struct PlayerReqEquipData
    {
        public string _playerId;
        public string _reqEquipId;
        public string _reqEquipInsId;
        public int _slotIdx;
        public bool _isSwap;
    }
}