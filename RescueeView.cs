using UnityEngine;

namespace TRAINEE
{
    public class RescueeView : MapViewBase
    {
        public override void Init(string id)
        {
            _id = id;
        }

        public override void UpdateView(GameEvent data)
        {
            if (data._param is RescueeEventData rescueeData)
            {

                switch (rescueeData._currentState)
                {
                    case ERescueeState.Following:
                        break;
                    case ERescueeState.OnStretcher:
                        break;
                }
            }
        }
    }
}
