using System.Collections.Generic;
using UnityEngine;


namespace TRAINEE
{
    public class BuildingModel : MapModelBase
    {
        protected BuildingData _data;   // 정적 데이터

        private List<FloorModel> _floors = new List<FloorModel>();

        public BuildingModel(BuildingData data) : base(data.Id) 
        {
            _data = data;
            _floors.Clear();
        }

        public void AddFloor(FloorModel floor)
        {
            _floors.Add(floor);
        }

        public void GlobalEvent(string eventKey)
        {

        }

        public override void Init()
        {
        }

        public override void UpdateModel(GameEvent data)
        {
        }
    }
}
