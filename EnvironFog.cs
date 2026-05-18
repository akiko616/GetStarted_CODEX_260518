using UnityEngine;
using UnityEngine.Rendering;

namespace TRAINEE
{
    public class EnvironFog : EnvironModule<EnvironViewBase>
    {
        private EnvironWeatherFog _fogView = null;
        public EnvironFog(EnvironViewBase view, Volume volume) : base(view, volume)
        {
        }

        public override void Init()
        {
            if (_view == null || _view.Volume == null)
            {
                Debug.Log($"Environ Setup Missing view : {_view} / volume : {_view.Volume} ");
                return;
            }

            _fogView = _view as EnvironWeatherFog;
        }

        public override void SetupWeather()
        {
            // 터레인 세팅
            if (_fogView.TerrainPalte != null && GameManager.Instance.CurrentMapTerrainModel != null)
            {
                Terrain[] terrains = GameManager.Instance.CurrentMapTerrainModel.GetComponentsInChildren<Terrain>();
                
                foreach(Terrain terrain in terrains)
                {
                    terrain.terrainData.terrainLayers = _fogView.TerrainPalte.paletteLayers.ToArray();
                }

                Debug.Log($"[EnvironFog] Setup Complete.");
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
