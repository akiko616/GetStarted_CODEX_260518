using UnityEngine;


namespace TRAINEE
{
    public class EnvironViewWeather : EnvironViewBase
    {
        [Header("Setting")]
        [SerializeField] protected string _globalShaderPropertyName;

        [Header("Terrain")]
        [SerializeField] protected TerrainPaletteData _terrainPalte;


        public string GlobalShaderPropertyName { get => _globalShaderPropertyName; }
        public TerrainPaletteData TerrainPalte { get => _terrainPalte; }
    }
}
