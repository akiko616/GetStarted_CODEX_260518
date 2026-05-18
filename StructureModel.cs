using UnityEngine;


namespace TRAINEE
{
    public class StructureModel : ElementModelBase
    {
        public StructureModel(RoomElementData data) : base(data)
        {
            Debug.Log($"Structure Model : {data.element}");
        }

        public override void Init()
        {
        }

        public override void UpdateModel(GameEvent data)
        {
        }
    }
}
