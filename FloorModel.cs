using System.Collections.Generic;
using UnityEngine;


namespace TRAINEE
{
    public class FloorModel : MapModelBase
    {
        protected FloorData _data;

        public FloorModel(FloorData data) : base(data.Id) 
        {
            _data = data;
        }

        public override void Init()
        {
        }

        public override void UpdateModel(GameEvent data)
        {
        }
    }
}
