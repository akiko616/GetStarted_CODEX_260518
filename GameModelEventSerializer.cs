using FishNet.Serializing;
using UnityEngine;

namespace TRAINEE
{
    public static class GameModelEventSerializer
    {
        public static void WriteGameModelEventBase(this Writer writer, GameModelEventBase data)
        {
            bool isNull = (data == null);
            writer.WriteBoolean(isNull);

            if (isNull) return;
            Debug.Log($"[Serializer Write] 포장 시작! 들어온 ID: {data?._id}");
            // 1. 타입 식별자 쓰기
            writer.WriteUInt8Unpacked((byte)data.GetEventType());
            // 2. 공통 변수 쓰기
            writer.WriteString(data._id);
            writer.WriteBoolean(data.isServerConfirmed);
            writer.WriteString(data._callerId);

            // 3. 자식 클래스 캐스팅 및 고유 변수 쓰기 (Switch문 지옥)
            switch (data.GetEventType())
            {
                case GameEventType.Door:
                    var door = (DoorEventData)data;
                    writer.WriteUInt8Unpacked((byte)door.state);
                    writer.WriteBoolean(door._isOpen);
                    break;
                case GameEventType.Equip:
                    var equip = (EquipEventData)data;
                    writer.WriteString(equip._instanceId);
                    writer.WriteBoolean(equip._isDrop);
                    break;
                case GameEventType.Terrain:
                    var terrain = (TerrainEventData)data;
                    writer.WriteUInt8Unpacked((byte)terrain._weather);
                    break;
                case GameEventType.Rescuee:
                    var rescuee = (RescueeEventData)data;
                    writer.WriteString(rescuee._rescueeId);
                    writer.WriteUInt8Unpacked((byte)rescuee._currentState);
                    writer.WriteUInt8Unpacked((byte)rescuee._rescueeType);
                    break;
                case GameEventType.Disaster:
                    var disaster = (DisasterEventData)data;
                    writer.WriteString(disaster._disasterid);
                    break;

            }
        }

        public static GameModelEventBase ReadGameModelEventBase(this Reader reader)
        {
            bool isNull = reader.ReadBoolean();
            if (isNull) return null; // 맞으면 안전하게 Null 반환

            byte typeByte = reader.ReadUInt8Unpacked();
            GameEventType eventType = (GameEventType)typeByte;

            string id = reader.ReadStringAllocated();
            bool isConfirmed = reader.ReadBoolean();
            string callerid = reader.ReadStringAllocated();
            GameModelEventBase newData = null;

            // 타입 식별자를 보고 알맞은 자식 객체를 new로 생성!
            switch (eventType)
            {
                case GameEventType.Door:
                    newData = new DoorEventData()
                    {
                        _id = id,
                        _callerId = callerid,
                        isServerConfirmed = isConfirmed,
                        state = (EDoorState)reader.ReadUInt8Unpacked(),
                        _isOpen = reader.ReadBoolean()
                    };
                    break;
                case GameEventType.Equip:
                    newData = new EquipEventData()
                    {
                        _id = id,
                        _callerId = callerid,
                        isServerConfirmed = isConfirmed,
                        _instanceId = reader.ReadStringAllocated(),
                        _isDrop = reader.ReadBoolean()
                    };
                    break;
                case GameEventType.Terrain:
                    newData = new TerrainEventData()
                    {
                        _id = id,
                        _callerId = callerid,
                        isServerConfirmed = isConfirmed,
                        _weather = (EWeather)reader.ReadUInt8Unpacked()
                    };
                    break;
                case GameEventType.Rescuee:
                    newData = new RescueeEventData()
                    {
                        _id = id,
                        _callerId = callerid,
                        isServerConfirmed = isConfirmed,
                        _rescueeId = reader.ReadStringAllocated(),
                        _currentState = (ERescueeState)reader.ReadUInt8Unpacked(),
                        _rescueeType = (ERescueeType)reader.ReadUInt8Unpacked()
                    };
                    break;
                case GameEventType.Disaster:
                    newData = new DisasterEventData()
                    {
                        _id = id,
                        _callerId = callerid,
                        isServerConfirmed = isConfirmed,
                        _disasterid = reader.ReadStringAllocated(),
                    };
                    break;
                    
            }

            Debug.Log($"[Serializer Read] 해체 완료! 복원된 ID: {newData?._id}");

            return newData;
        }

        public static void WriteMapEventBroadcast(this Writer writer, MapEventBroadcast msg)
        {
            writer.WriteGameModelEventBase(msg.EventData);
        }

        public static MapEventBroadcast ReadMapEventBroadcast(this Reader reader)
        {
            return new MapEventBroadcast
            {
                EventData = reader.ReadGameModelEventBase()
            };
        }
    }
}
