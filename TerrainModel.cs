using UnityEngine;


namespace TRAINEE
{
    public class TerrainModel : MapModelBase
    {
        protected TerrainData _data;

        public TerrainModel(TerrainData data) : base(data.Id)
        {
            _data = data;
        }

        public override void Init()
        {
        }

        public override void UpdateModel(GameEvent data)
        {
            // 데이터 업데이트 실행
            // 변경된 데이터로 새로운 데이터 생성

            if (data?._param == null)
            {
                Debug.Log($"{this._id}에 이벤트 값이 비어있습니다.");
                return;
            }

            GameEvent game = new GameEvent();
            game._id = this._id;
            game._type = EEventType.UpdateView;
            game._param = data._param;

            GameEventManager.Instance.Publish(game);
        }
    }
}
