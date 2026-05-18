using UnityEngine;


namespace TRAINEE
{
    public abstract class ElementModelBase : MapModelBase
    {
        protected RoomElementData _data = null;
        protected ElementModelBase(RoomElementData data) : base(data.Id)
        {
            _data = data;
        }
    }
}
