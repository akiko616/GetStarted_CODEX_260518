using UnityEngine;
using UnityEngine.Rendering;

namespace TRAINEE
{
    public class EnvironSunny : EnvironModule<EnvironViewBase>
    {
        private EnvironWeatherSunny _sunnyView = null;
        public EnvironSunny(EnvironViewBase view, Volume volume) : base(view, volume)
        {
        }

        public override void Init()
        {
            if (_view == null || _view.Volume == null)
            {
                Debug.Log($"Environ Setup Missing view : {_view} / volume : {_view.Volume} ");
                return;
            }

            _sunnyView = _view as EnvironWeatherSunny;
        }

        public override void SetupWeather()
        {
            // 터레인 세팅
            if (_sunnyView.TerrainPalte != null && GameManager.Instance.CurrentMapTerrainModel != null)
            {
                Terrain terrain = GameManager.Instance.CurrentMapTerrainModel.GetComponent<Terrain>();

                terrain.terrainData.terrainLayers = _sunnyView.TerrainPalte.paletteLayers.ToArray();

                Debug.Log($"[EnvironSunny] Setup Complete.");
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

        public override void OnUpdate(float deltaTime)
        {
            if (!GameManager.Instance.GetGameStart)
            {
                return;
            }
        }
    }
}
