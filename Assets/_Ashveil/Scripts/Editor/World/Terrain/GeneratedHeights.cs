using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools.TerrainGen
{
    /// <summary>
    /// Hoehe des erzeugten Gelaendes (GEN-Kacheln) an einem Weltpunkt – fuer Bauteile der Handarbeit, die an erzeugtes
    /// Gelaende grenzen (z. B. Felsdach am Ostportal des Osttunnels). Liest nur vorhandene Assets.
    /// </summary>
    public static class GeneratedHeights
    {
        private static readonly Dictionary<Vector2Int, TerrainData> Cache = new Dictionary<Vector2Int, TerrainData>();

        public static void Clear() => Cache.Clear();

        public static float Sample(float x, float z, float fallback)
        {
            var coord = new Vector2Int(Mathf.FloorToInt((x + 600f) / TerrainTileGenerator.TileSize), Mathf.FloorToInt((z + 350f) / TerrainTileGenerator.TileSize));
            if (!Cache.TryGetValue(coord, out TerrainData data) || data == null)
            {
                data = AssetDatabase.LoadAssetAtPath<TerrainData>($"{TerrainGenPaths.TerrainFolder}/GEN_Terrain_{coord.x}_{coord.y}_TerrainData.asset");
                Cache[coord] = data;
            }
            if (data == null) return fallback;
            Vector2 o = TerrainTileGenerator.TileOrigin(coord);
            float u = Mathf.Clamp01((x - o.x) / TerrainTileGenerator.TileSize), v = Mathf.Clamp01((z - o.y) / TerrainTileGenerator.TileSize);
            return TerrainTileGenerator.BaseY + data.GetInterpolatedHeight(u, v);
        }
    }
}
