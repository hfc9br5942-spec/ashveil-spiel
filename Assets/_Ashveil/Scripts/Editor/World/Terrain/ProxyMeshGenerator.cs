using System.Collections.Generic;
using Ashveil.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashveil.EditorTools.TerrainGen
{
    /// <summary>
    /// Distant Terrain Proxies (Vorgabe §64–66, §121): grobe Gitter-Meshes aus dem Makrofeld, in Brocken zu 4 km,
    /// Farbe je Vertex (Biom, Hoehe, Neigung), keine Collider. Wo echtes Gelaende steht (Handarbeit, GEN-Kacheln),
    /// liegt der Proxy 30 m tiefer – er wird vom echten Gelaende verdeckt und ragt nie hindurch. Ein grober Rand
    /// setzt die Welt ueber den Ausschnitt hinaus fort, damit der Horizont nie wie ein Levelende aussieht.
    /// </summary>
    public static class ProxyMeshGenerator
    {
        public const float Chunk = 4000f;
        public const float Step = 40f;
        public const float SkirtWidth = 18000f;
        public const float SkirtStep = 600f;
        public const string MaterialPath = TerrainGenPaths.ProxyFolder + "/GEN_Proxy.mat";

        public static List<Mesh> Generate(TerrainGenerationContext ctx, List<Rect> realTerrain)
        {
            ProjectSetup.EnsureFolder(TerrainGenPaths.ProxyFolder);
            MacroHeightField f = ctx.Field;
            Rect b = ctx.Profile.bounds;
            var meshes = new List<Mesh>();
            int cx = Mathf.CeilToInt(b.width / Chunk), cz = Mathf.CeilToInt(b.height / Chunk);
            try
            {
                int total = cx * cz + 1, k = 0;
                for (int j = 0; j < cz; j++)
                    for (int i = 0; i < cx; i++)
                    {
                        EditorUtility.DisplayProgressBar("ASHVEIL Terrain-Generator", "Fernansicht", (float)k++ / total);
                        var r = new Rect(b.xMin + i * Chunk, b.yMin + j * Chunk, Mathf.Min(Chunk, b.xMax - (b.xMin + i * Chunk)), Mathf.Min(Chunk, b.yMax - (b.yMin + j * Chunk)));
                        Mesh m = BuildGrid(ctx, r, Step, realTerrain, false, $"GEN_Proxy_{i:00}_{j:00}");
                        meshes.Add(Save(m));
                    }
                meshes.Add(Save(BuildSkirt(ctx)));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            EnsureMaterial();
            AssetDatabase.SaveAssets();
            ctx.Log.Add($"Fernansicht: {meshes.Count} Proxy-Meshes (Raster {Step} m, Rand {SkirtWidth / 1000f:0} km)");
            return meshes;
        }

        private static Mesh Save(Mesh m)
        {
            string path = $"{TerrainGenPaths.ProxyFolder}/{m.name}.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old != null)
            {
                EditorUtility.CopySerialized(m, old);
                Object.DestroyImmediate(m);
                EditorUtility.SetDirty(old);
                return old;
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        public static Material EnsureMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Shader sh = Shader.Find("Ashveil/DistantTerrain");
            if (mat == null)
            {
                mat = new Material(sh != null ? sh : Shader.Find("Universal Render Pipeline/Unlit")) { name = "GEN_Proxy" };
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            else if (sh != null && mat.shader != sh) mat.shader = sh;
            mat.enableInstancing = false;
            return mat;
        }

        private static Mesh BuildGrid(TerrainGenerationContext ctx, Rect r, float step, List<Rect> real, bool skirt, string name)
        {
            MacroHeightField f = ctx.Field;
            int nx = Mathf.Max(2, Mathf.RoundToInt(r.width / step) + 1), nz = Mathf.Max(2, Mathf.RoundToInt(r.height / step) + 1);
            var verts = new Vector3[nx * nz];
            var cols = new Color[nx * nz];
            float sea = ctx.Profile.seaLevel;
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    float wx = r.xMin + r.width * x / (nx - 1), wz = r.yMin + r.height * z / (nz - 1);
                    float h = f.SampleBilinear(wx, wz);
                    bool covered = false;
                    foreach (Rect rr in real)
                        if (wx > rr.xMin + step && wx < rr.xMax - step && wz > rr.yMin + step && wz < rr.yMax - step) covered = true;
                    Color c = ColorAt(ctx, wx, wz, h);
                    if (h < sea)
                    {
                        h = sea - 0.3f;
                        c = new Color(0.09f, 0.14f, 0.17f).linear;
                    }
                    if (covered) h -= 30f;
                    verts[z * nx + x] = new Vector3(wx, h, wz);
                    cols[z * nx + x] = c;
                }
            return MakeMesh(name, verts, cols, nx, nz);
        }

        /// <summary>Rand ausserhalb des Ausschnitts: Randhoehe fortgesetzt, grosse ruhige Formen, Meer bleibt Meer.</summary>
        private static Mesh BuildSkirt(TerrainGenerationContext ctx)
        {
            MacroHeightField f = ctx.Field;
            Rect b = ctx.Profile.bounds;
            var outer = new Rect(b.xMin - SkirtWidth, b.yMin - SkirtWidth, b.width + 2f * SkirtWidth, b.height + 2f * SkirtWidth);
            int nx = Mathf.RoundToInt(outer.width / SkirtStep) + 1, nz = Mathf.RoundToInt(outer.height / SkirtStep) + 1;
            var verts = new Vector3[nx * nz];
            var cols = new Color[nx * nz];
            float sea = ctx.Profile.seaLevel;
            int seed = AshNoise.Derive(ctx.WorldSeed, "skirt");
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    float wx = outer.xMin + x * SkirtStep, wz = outer.yMin + z * SkirtStep;
                    float cxp = Mathf.Clamp(wx, b.xMin, b.xMax), czp = Mathf.Clamp(wz, b.yMin, b.yMax);
                    float edge = f.SampleBilinear(cxp, czp);
                    float outside = Mathf.Max(Mathf.Abs(wx - cxp), Mathf.Abs(wz - czp));
                    float fade = Mathf.Clamp01(outside / (SkirtWidth * 0.7f));
                    float hills = (float)AshNoise.Ridged(wx, wz, 5200, 4, seed) * 900f * (edge > sea ? 1f : 0f);
                    float h = Mathf.Lerp(edge, edge > sea ? 180f + hills : sea - 60f, fade);
                    bool inside = outside <= 0f;
                    if (inside) h -= 60f;   // innen liegt das feine Proxy-Gitter
                    Color c = edge > sea ? Color.Lerp(new Color(0.34f, 0.38f, 0.30f), new Color(0.58f, 0.57f, 0.55f), Mathf.Clamp01(h / 1200f)).linear : new Color(0.09f, 0.14f, 0.17f).linear;
                    if (h < sea) h = sea - 0.3f;
                    verts[z * nx + x] = new Vector3(wx, h, wz);
                    cols[z * nx + x] = c;
                }
            return MakeMesh("GEN_Proxy_Rand", verts, cols, nx, nz);
        }

        private static Color ColorAt(TerrainGenerationContext ctx, float wx, float wz, float h)
        {
            MacroHeightField f = ctx.Field;
            int ix = Mathf.Clamp(Mathf.RoundToInt((wx - f.Origin.x) / f.CellSize), 0, f.Width - 1);
            int iz = Mathf.Clamp(Mathf.RoundToInt((wz - f.Origin.y) / f.CellSize), 0, f.Height - 1);
            var biome = ctx.Biomes != null ? (MacroBiome)ctx.Biomes[iz * f.Width + ix] : MacroBiome.TemperatePlains;
            float slope = f.SlopeDeg(ix, iz);
            Color c;
            switch (biome)
            {
                case MacroBiome.MixedForest: c = new Color(0.20f, 0.29f, 0.17f); break;
                case MacroBiome.AncientForest: c = new Color(0.14f, 0.23f, 0.13f); break;
                case MacroBiome.PineHighlands: c = new Color(0.17f, 0.24f, 0.19f); break;
                case MacroBiome.Wetland: case MacroBiome.Moor: c = new Color(0.33f, 0.33f, 0.24f); break;
                case MacroBiome.GranitePlateau: case MacroBiome.CanyonRim: c = new Color(0.45f, 0.43f, 0.40f); break;
                case MacroBiome.Alpine: c = new Color(0.78f, 0.80f, 0.84f); break;
                case MacroBiome.Ashland: c = new Color(0.30f, 0.29f, 0.28f); break;
                case MacroBiome.VeilScar: c = new Color(0.25f, 0.24f, 0.27f); break;
                case MacroBiome.CoastalCliff: c = new Color(0.52f, 0.50f, 0.44f); break;
                default: c = new Color(0.36f, 0.42f, 0.26f); break;
            }
            if (slope > 30f) c = Color.Lerp(c, new Color(0.42f, 0.40f, 0.38f), Mathf.Clamp01((slope - 30f) / 15f));
            // Schnee erst hoch oben und nur auf flacheren Lagen (steile Flanken bleiben Fels)
            float snow = Mathf.Clamp01((h - 1350f) / 350f) * (1f - Mathf.Clamp01((slope - 35f) / 15f));
            if (biome == MacroBiome.Alpine) c = new Color(0.46f, 0.46f, 0.47f);
            c = Color.Lerp(c, new Color(0.80f, 0.82f, 0.86f), snow);
            return c.linear;   // Vertexfarben werden im Shader linear gelesen
        }

        private static Mesh MakeMesh(string name, Vector3[] verts, Color[] cols, int nx, int nz)
        {
            var tris = new int[(nx - 1) * (nz - 1) * 6];
            int t = 0;
            for (int z = 0; z < nz - 1; z++)
                for (int x = 0; x < nx - 1; x++)
                {
                    int i = z * nx + x;
                    tris[t++] = i; tris[t++] = i + nx; tris[t++] = i + 1;
                    tris[t++] = i + 1; tris[t++] = i + nx; tris[t++] = i + nx + 1;
                }
            var m = new Mesh { name = name, indexFormat = verts.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.vertices = verts;
            m.colors = cols;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
