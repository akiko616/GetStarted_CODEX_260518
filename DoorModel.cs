using UnityEngine;


namespace TRAINEE
{
    public class DoorModel : ElementModelBase
    {
        protected EDoorState _state = EDoorState.None;
        protected bool _isOpen = false;                          // 문이 열린 상태인지 체크

        protected bool _isBreake = false;
        public DoorModel(RoomElementData data) : base(data)
        {
            Debug.Log($"Door Model : {data.element}");

            if(data.property != null && !data.property.Equals(""))
            {
                DoorProperty property = JsonHelper.FromJson<DoorProperty>(data.property);

                _state = property._state;
                _isOpen = property._isOpen;
            }
        }

        public override GameModelEventBase EventData(GameModelEventBase request)
        {
            if (request is DoorEventData doorReq)
            {
                request.isServerConfirmed = true;

                _isOpen = doorReq._isOpen;
                _state = doorReq.state;

                Debug.Log($"[Server] 상호작용 요청: {request._id} / {request.isServerConfirmed}");

                PublishEvent(EEventType.UpdateView, doorReq);
                return request;
            }

            return null;
        }

        public override void Init()
        {
            switch (_state)
            {
                case EDoorState.Open:
                    _isOpen = true;
                    break;
                case EDoorState.Close:
                    _isOpen = false;
                    break;
                case EDoorState.Lock:
                    _isOpen = true;
                    break;
            }

            // 임시 예외처리 코드
            DoorEventData door = new DoorEventData();
            door._isOpen = _isOpen;
            door.state = _state;

            GameEvent game = new GameEvent();
            game._id = this._id;
            game._type = EEventType.UpdateView;
            game._param = door;
                        
            if (GameEventManager.Instance) // 에디터 에러때문에 임시 처리 (신현재 26.03.20)
                GameEventManager.Instance.Publish(game);
        }

        public override void UpdateModel(GameEvent data)
        {
            // 데이터 업데이트 실행
            // 변경된 데이터로 새로운 데이터 생성

            if(data?._param == null)
            {
                Debug.Log($"{this._id}에 이벤트 값이 비어있습니다.");
                return;
            }

            if (data._param is DoorEventData doorData)
            {
                if (!doorData.isServerConfirmed)
                {
                    if (this._isOpen)
                    {
                        Debug.Log($"[DoorModel] {_id} 이미 열려있으므로 서버에 요청하지 않습니다.");
                        return;
                    }

                    Debug.Log($"[DoorModel] {_id} 로컬 클릭 감지! 컨트롤러에 서버 검토 요청.");

                    DoorEventData requestPayload = new DoorEventData();
                    requestPayload._id = this._id;
                    requestPayload._isOpen = true; // 닫혀있으면 열기(true), 열려있으면 닫기(false)
                    requestPayload.state = EDoorState.Open;
                    requestPayload.isServerConfirmed = false;

                    PublishEvent(EEventType.Server, requestPayload);

                    return;
                }

                Debug.Log($"[DoorModel] 상호작용 : {doorData._isOpen} / {doorData.state}");
                _isOpen = doorData._isOpen;
                _state = doorData.state;

                PublishEvent(EEventType.UpdateView, doorData);
            }



        }

        private void PublishEvent(EEventType eventType, DoorEventData payload)
        {
            GameEvent game = new GameEvent();
            game._id = this._id;
            game._type = eventType;
            game._param = payload;

            GameEventManager.Instance.Publish(game);
        }
    }
}
