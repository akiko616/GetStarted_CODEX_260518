using UnityEngine;

namespace TRAINEE
{
    public class DisasterModel : ElementModelBase
    {
        private DisasterSaveData _disaster;

        public DisasterSaveData GetDisaster { get => _disaster; }
        public DisasterModel(RoomElementData data) : base(data)
        {
            Debug.Log($"DisasterModel : {data.element}");
        }

        public override void Init()
        {
        }

        public void SetupModelData(DisasterSaveData disaster)
        {
            _disaster = disaster;
        }

        public override GameModelEventBase EventData(GameModelEventBase request)
        {
            if (request is DisasterEventData Req)
            {
                request.isServerConfirmed = true;
                string callerId = Req._callerId;
                PlayerController callerPlayer = GameManager.Instance.GetSystem<PlayerSpawnSystem>().GetPlayerById(callerId) as PlayerController;

                // 타켓 플레이어를 찾고 해당 플레이어가 현재 착용중인 장비 아이디를 확인하고
                // 해당 장비 아이디와 데이터 중에서 일치하는 장비 데이터가 있는지 확인한다.
                // 일치하는 장비가 있을경우 업데이트

                // 아닐 경우 다른 UI를 보여준다.


                Debug.Log($"[Server] 상호작용 요청: {request._id} / {request.isServerConfirmed}");

                PublishEvent(EEventType.UpdateView, Req);
                return request;
            }

            return null;
        }


        public override void UpdateModel(GameEvent data)
        {
            if (data._param is DisasterEventData disasterData)
            {
                if (!disasterData.isServerConfirmed)
                {
                    Debug.Log($"[DisasterModel] {_id} 로컬 클릭 감지! 컨트롤러에 서버 검토 요청.");

                    DisasterEventData requestPayload = new DisasterEventData();
                    requestPayload._id = this._id;
                    requestPayload._callerId = disasterData._callerId;
                    requestPayload._disasterid = this._id;
                    requestPayload.isServerConfirmed = false;

                    PublishEvent(EEventType.Server, requestPayload);

                    return;
                }

                //Debug.Log($"[DisasterModel] 상호작용 : {doorData._isOpen} / {doorData.state}");

                PublishEvent(EEventType.UpdateView, disasterData);
            }
        }

        private void PublishEvent(EEventType eventType, DisasterEventData payload)
        {
            GameEvent game = new GameEvent();
            game._id = this._id;
            game._type = eventType;
            game._param = payload;

            GameEventManager.Instance.Publish(game);
        }
    }
}
