using UnityEngine;


namespace TRAINEE
{
    public class RescueeModel : ElementModelBase
    {
        private RescueeSaveData _rescuee;
        private ERescueeType _rescueeType;
        private ERescueeState _rescueeState;
        public RescueeSaveData GetRescuee { get => _rescuee; }
        public RescueeModel(RoomElementData data) : base(data)
        {
            Debug.Log($"RescueeModel : {data.element}");
        }

        public override void Init()
        {
        }

        public void SetupModelData(RescueeSaveData rescuee)
        {
            _rescuee = rescuee;

            _rescueeType = rescuee.rescueeType;
            _rescueeState = rescuee.rescueeState;
        }

        public override GameModelEventBase EventData(GameModelEventBase request)
        {
            if (request is RescueeEventData rescueeReq)
            {
                Debug.Log($"[Server] 상호작용 요청: {request._id} / {request.isServerConfirmed}");

                string callerId = rescueeReq._callerId;
                PlayerController callerPlayer = GameManager.Instance.GetSystem<PlayerSpawnSystem>().GetPlayerById(callerId) as PlayerController;
                string currentEquipId = callerPlayer != null ? callerPlayer.GetCurrentEquipId : "";

                ERescueeState nextState = CheckState(rescueeReq, currentEquipId);

                request.isServerConfirmed = true;
                _rescueeState = nextState;
                rescueeReq._currentState = nextState;

                _rescuee.rescueeState = nextState;
                PublishEvent(EEventType.UpdateView, rescueeReq);
                return rescueeReq;
            }
            return null;
        }

        public override void UpdateModel(GameEvent data)
        {
            if (data._param is RescueeEventData rescueeData)
            {
                if (!rescueeData.isServerConfirmed)
                {
                    Debug.Log($"[Client Model] {rescueeData._callerId}");
                    RescueeEventData requestPayload = new RescueeEventData();
                    requestPayload._id = rescueeData._id;
                    requestPayload._rescueeId = rescueeData._rescueeId;
                    requestPayload._callerId = rescueeData._callerId;
                    requestPayload._currentState = _rescueeState;
                    requestPayload.isServerConfirmed = false;

                    PublishEvent(EEventType.Server, requestPayload);
                    return;
                }
                    // 내 로컬 모델 데이터 동기화

                Debug.Log($"[Client Model] {this._id} 데이터 동기화 완료. View 업데이트 발행.");

                _rescueeState = rescueeData._currentState;
                PublishEvent(EEventType.UpdateView, rescueeData);

            }
        }

        private void PublishEvent(EEventType eventType, RescueeEventData payload)
        {
            GameEvent game = new GameEvent();
            game._id = this._id;
            game._type = eventType;
            game._param = payload;

            GameEventManager.Instance.Publish(game);
        }

        private ERescueeState CheckState(RescueeEventData rescueeReq, string equipId)
        {
            ERescueeState nextState = _rescueeState;

            switch (_rescueeState)
            {
                case ERescueeState.Undiscovered:
                    nextState = ERescueeState.Discovered;
                    break;

                case ERescueeState.Discovered:
                    nextState = ERescueeState.ConsciousChecked;
                    break;

                case ERescueeState.ConsciousChecked:
                    // 💡 [핵심] 플레이어의 정보에 따라 조건 분기!
                    if (_rescueeType == ERescueeType.Minor)
                    {
                        nextState = ERescueeState.Following; // 경상자는 바로 따라감
                    }
                    else if (_rescueeType == ERescueeType.Severe)
                    {
                        // 💡 중상자는 플레이어가 '들것(Stretcher)' 장비를 들고 있을 때만 이동 가능!
                        if (equipId == "Item_Stretcher")
                        {
                            nextState = ERescueeState.OnStretcher;
                        }
                        else
                        {

                        }
                    }
                    break;
            }

            Debug.Log($"[Server] 요구조자 {this._id} 상태 변경 확정: {rescueeReq._currentState}");

            return nextState;
        }
    }
}
