using UnityEngine;
using UnityEngine.AI;

namespace TRAINEE
{
    public class WoodDoorView : DoorViewBase
    {
        public override void Init(string id)
        {
            Init();
            _id = id;

            Debug.Log($"Wood Door : {id}");
        }

        public override void UpdateView(GameEvent data)
        {
            _data = data._param as DoorEventData;

            if (_data != null)
            {
                if(_data._isOpen)
                {
                    _animationHandler.SetBool(EAnimParam.open, _data._isOpen, true, 0f, () =>
                    {
                        GameEvent gameEvent = new GameEvent();

                        gameEvent._type = EEventType.UI;
                        gameEvent._id = _id;
                        gameEvent._param = null;

                        GameEventManager.Instance.Publish(gameEvent);
                    });
                }
                else
                {
                    _animationHandler.SetBool(EAnimParam.open, _data._isOpen);
                }
            }
            else
            {

            }
        }
    }
}
