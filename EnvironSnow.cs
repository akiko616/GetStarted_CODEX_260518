using UnityEngine;
using UnityEngine.Rendering;

namespace TRAINEE
{
    public class EnvironSnow : EnvironModule<EnvironViewBase>
    {
        private EnvironWeatherSnow _snowView = null;
        public EnvironSnow(EnvironViewBase view, Volume volume) : base(view, volume)
        {
        }

        public override void Init()
        {
            if (_view == null || _view.Volume == null)
            {
                Debug.Log($"Environ Setup Missing view : {_view} / volume : {_view.Volume} ");
                return;
            }

            _snowView = _view as EnvironWeatherSnow;
        }

        public override void SetupWeather()
        {
            TerrainEventData eventData = new TerrainEventData();
            eventData._weather = EWeather.Snow;

            GameEvent gameEvent = new GameEvent();
            gameEvent._id = GameManager.Instance.GetCurrentTerrain.ToString();
            gameEvent._type = EEventType.UpdateView;
            gameEvent._param = eventData;

            GameEventManager.Instance.Publish(gameEvent);

            Shader.SetGlobalFloat("_GlobalSnowIntensity", 5f);
        }

        public override void OnUpdate(float deltaTime)
        {
            if (!GameManager.Instance.GetGameStart)
            {

                return;
            }
        }
        public override void OnFixedUpdate(float deltaTime)
        {
            if (!GameManager.Instance.GetGameStart)
            {

                return;
            }
        }

        public override void OnLateUpdate(float deltaTime)
        {
            if (!GameManager.Instance.GetGameStart)
            {

                return;
            }
        }

    }
}
