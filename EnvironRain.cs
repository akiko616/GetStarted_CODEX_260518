using UnityEngine;
using UnityEngine.Rendering;

namespace TRAINEE
{
    public class EnvironRain : EnvironModule<EnvironViewBase>
    {
        //private VfxBase _vfx;
        private EnvironWeatherRain _rainView = null;
        private PlayerController _player = null;
        public EnvironRain(EnvironViewBase view, Volume volume) : base(view, volume)
        {
        }

        public override void Init()
        {
            if (_view == null || _view.Volume == null)
            {
                Debug.Log($"Environ Setup Missing view : {_view} / volume : {_view.Volume} ");
                return;
            }

            _rainView = _view as EnvironWeatherRain;
        }

        public override void SetupWeather()
        {
            // 터레인 세팅

            TerrainEventData eventData = new TerrainEventData();
            eventData._weather = EWeather.Rain;

            GameEvent gameEvent = new GameEvent();
            gameEvent._id = GameManager.Instance.GetCurrentTerrain.ToString();
            gameEvent._type = EEventType.UpdateView;
            gameEvent._param = eventData;

            GameEventManager.Instance.Publish(gameEvent);

            Shader.SetGlobalFloat("_isGlobalRain", 1f);
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
