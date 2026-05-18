using System.Collections.Generic;


namespace TRAINEE
{
    public abstract class MapModelBase
    {

        protected string _id = string.Empty;                    // 모든 모델은 id를 가지고 있다.
        public string ID {  get => _id; }


        public MapModelBase(string id)
        {
            _id = id;
        }
        public abstract void Init();                            // 동적 데이터 초기화

        public abstract void UpdateModel(GameEvent data);       // 데이터 변경 및 업데이트


        public virtual void ModelUpdate() { }                   // 모델 업데이트?

        public virtual void OnFixedUpdate(float deltaTime) { }  // 모델 틱 기반 업데이트

        public virtual GameModelEventBase EventData(GameModelEventBase request)
        {
            return null;
        }
    }
}
