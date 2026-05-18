using UnityEngine;


namespace TRAINEE
{
    public class EnvironWeatherRain : EnvironViewWeather
    {
        [SerializeField] private string _vfxPrefabName = "";
        [SerializeField] private GameObject _vfxPrefab;

        public string VFXName { get { return _vfxPrefabName; } }
        public GameObject VFXPrefab { get { return _vfxPrefab; } }
    }
}
