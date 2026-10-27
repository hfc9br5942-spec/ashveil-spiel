using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools.TerrainGen
{
    /// <summary>
    /// Liest die Hoehen des handgebauten Gelaendes (Kareth, Westweiden, Grenzgebirge-Kern) einmal auf dem Hauptthread
    /// und stellt sie threadsicher bereit (Vorgabe §10, §75). Der Generator schreibt nie hinein – er gleicht nur seine
    /// eigenen Raender daran an. Fehlt ein Asset, rechnet er mit der Hoehenfunktion des jeweiligen Builders nach.
    /// </summary>
    public sealed class ProtectedHeightSampler
    {
        internal sealed class Tile
        {
            public Rect Rect;
            public float[,] Heights;   // [z, x] in Weltmetern
            public int Res;
        }

        public sealed class Area
        {
            public string Id;
            public Rect Rect;
            internal readonly List<Tile> Tiles = new List<Tile>();
        }

        public readonly List<Area> Areas = new List<Area>();
        public readonly List<string> Notes = new List<string>();

        public static ProtectedHeightSampler Load(MacroWorldProfile profile)
        {
            var s = new ProtectedHeightSampler();
            foreach (ProtectedAreaDefinition def in profile.protectedAreas)
            {
                var area = new Area { Id = def.id, Rect = def.rect };
                foreach (string path in def.terrainDataPaths)
                {
                    var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
                    if (data == null) continue;
                    Tile t = FromTerrainData(data, def, path);
                    if (t != null) area.Tiles.Add(t);
                }
                if (area.Tiles.Count == 0)
                {
                    area.Tiles.Add(FromFunction(def));
                    s.Notes.Add($"{def.id}: TerrainData fehlt – Hoehenfunktion des Builders verwendet");
                }
                s.Areas.Add(area);
            }
            return s;
        }

        private static Tile FromTerrainData(TerrainData data, ProtectedAreaDefinition def, string path)
        {
            // Lage der Kachel: aus dem Namen nicht ablesbar – deshalb ueber die Groesse und die Lage im Bereich:
            // Kacheln liegen im 1200-m-Raster; Reihenfolge der Pfade im Profil = Reihenfolge der Builder.
            Vector3 size = data.size;
            Rect rect = TileRect(def, path, size);
            int res = data.heightmapResolution;
            float[,] n = data.GetHeights(0, 0, res, res);
            float baseY = BaseYFor(path);
            var h = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    h[z, x] = baseY + n[z, x] * size.y;
            return new Tile { Rect = rect, Heights = h, Res = res };
        }

        private static float BaseYFor(string path) => -40f;   // Kareth, Westweiden, Grenzgebirge: alle bei y = −40

        private static Rect TileRect(ProtectedAreaDefinition def, string path, Vector3 size)
        {
            string file = System.IO.Path.GetFileNameWithoutExtension(path);
            if (file.StartsWith("GG_"))
            {
                // Grenzgebirge-Kern: Entwurfskachel → Weltkachel (um 90° gedreht, BorderMountainsFrame)
                float x = file.Contains("_Ost_") ? BorderMountainsLayout.MinX + BorderMountainsLayout.TileSize : BorderMountainsLayout.MinX;
                float z = file.Contains("_Nord") ? BorderMountainsLayout.MinZ + BorderMountainsLayout.TileSize : BorderMountainsLayout.MinZ;
                foreach (BorderMountainsBuilder.Tile t in BorderMountainsBuilder.Tiles)
                    if (Mathf.Approximately(t.Origin.x, x) && Mathf.Approximately(t.Origin.y, z))
                    {
                        Vector2 o = BorderMountainsBuilder.WorldOrigin(t);
                        return new Rect(o.x, o.y, size.x, size.z);
                    }
            }
            return new Rect(def.rect.xMin, def.rect.yMin, size.x, size.z);
        }

        private static Tile FromFunction(ProtectedAreaDefinition def)
        {
            const int res = 257;
            var h = new float[res, res];
            Func<float, float, float> fn = def.id == "Westweiden" ? WestPasturesLayout.Height
                                         : def.id == "Grenzgebirge" ? (wx, wz) => { Vector2 l = Ashveil.World.BorderMountainsFrame.ToLocal(new Vector2(wx, wz)); return BorderMountainsLayout.Height(l.x, l.y); }
                                         : (Func<float, float, float>)KarethTerrainBuilder.Height;
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float wx = def.rect.xMin + def.rect.width * x / (res - 1);
                    float wz = def.rect.yMin + def.rect.height * z / (res - 1);
                    h[z, x] = fn(wx, wz);
                }
            return new Tile { Rect = def.rect, Heights = h, Res = res };
        }

        /// <summary>Hoehe im geschuetzten Gelaende (bilinear); false, wenn kein Bereich den Punkt enthaelt.</summary>
        public bool TrySample(float x, float z, out float height)
        {
            foreach (Area a in Areas)
                foreach (Tile t in a.Tiles)
                    if (t.Rect.Contains(new Vector2(x, z)) || OnEdge(t.Rect, x, z))
                    {
                        height = Sample(t, x, z);
                        return true;
                    }
            height = 0f;
            return false;
        }

        private static bool OnEdge(Rect r, float x, float z) => x >= r.xMin && x <= r.xMax && z >= r.yMin && z <= r.yMax;

        private static float Sample(Tile t, float x, float z)
        {
            float gx = Mathf.Clamp01((x - t.Rect.xMin) / t.Rect.width) * (t.Res - 1);
            float gz = Mathf.Clamp01((z - t.Rect.yMin) / t.Rect.height) * (t.Res - 1);
            int x0 = Mathf.Min((int)gx, t.Res - 2), z0 = Mathf.Min((int)gz, t.Res - 2);
            float fx = gx - x0, fz = gz - z0;
            float a = Mathf.Lerp(t.Heights[z0, x0], t.Heights[z0, x0 + 1], fx);
            float b = Mathf.Lerp(t.Heights[z0 + 1, x0], t.Heights[z0 + 1, x0 + 1], fx);
            return Mathf.Lerp(a, b, fz);
        }

        /// <summary>Exakter Kachelwert an einem Heightmap-Stuetzpunkt (fuer nahtlose Raender, falls Raster passt).</summary>
        public bool TrySampleExact(float x, float z, out float height)
        {
            foreach (Area a in Areas)
                foreach (Tile t in a.Tiles)
                    if (OnEdge(t.Rect, x, z))
                    {
                        float gx = (x - t.Rect.xMin) / t.Rect.width * (t.Res - 1);
                        float gz = (z - t.Rect.yMin) / t.Rect.height * (t.Res - 1);
                        int ix = Mathf.RoundToInt(gx), iz = Mathf.RoundToInt(gz);
                        if (Mathf.Abs(gx - ix) < 1e-3f && Mathf.Abs(gz - iz) < 1e-3f)
                        {
                            height = t.Heights[Mathf.Clamp(iz, 0, t.Res - 1), Mathf.Clamp(ix, 0, t.Res - 1)];
                            return true;
                        }
                        height = Sample(t, x, z);
                        return true;
                    }
            height = 0f;
            return false;
        }

        /// <summary>
        /// Abstand zum naechsten geschuetzten Bereich und dessen Randhoehe am naechsten Punkt – Grundlage der
        /// Uebergangszone (TerrainSeamBridge, Vorgabe §10).
        /// </summary>
        public float NearestEdge(float x, float z, out float edgeHeight, out string areaId)
        {
            float best = float.MaxValue;
            edgeHeight = 0f;
            areaId = "";
            foreach (Area a in Areas)
            {
                Rect r = a.Rect;
                float cx = Mathf.Clamp(x, r.xMin, r.xMax), cz = Mathf.Clamp(z, r.yMin, r.yMax);
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));
                if (d < best)
                {
                    best = d;
                    areaId = a.Id;
                    TrySample(cx, cz, out edgeHeight);
                }
            }
            return best;
        }

        public bool Inside(float x, float z)
        {
            foreach (Area a in Areas)
                if (a.Rect.Contains(new Vector2(x, z))) return true;
            return false;
        }

        /// <summary>Pruefsumme der Hoehen eines Bereichs (Kareth-Validator §98).</summary>
        public string AreaHash(string id)
        {
            unchecked
            {
                ulong h = 1469598103934665603UL;
                foreach (Area a in Areas)
                {
                    if (a.Id != id) continue;
                    foreach (Tile t in a.Tiles)
                        for (int z = 0; z < t.Res; z += 3)
                            for (int x = 0; x < t.Res; x += 3)
                            {
                                h ^= (uint)Mathf.RoundToInt(t.Heights[z, x] * 1000f);
                                h *= 1099511628211UL;
                            }
                }
                return h.ToString("x16");
            }
        }
    }
}
