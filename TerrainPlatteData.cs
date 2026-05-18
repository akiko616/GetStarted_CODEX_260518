using UnityEngine;
using System.Collections.Generic;

namespace TRAINEE
{
    [CreateAssetMenu(fileName = "NewTerrainPalette", menuName = "TRAINEE/Terrain Palette")]
    public class TerrainPaletteData : ScriptableObject
    {
        public List<TerrainLayer> paletteLayers = new List<TerrainLayer>();
    }
}
