using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.EditorTools.TerrainGen
{
    /// <summary>
    /// Grobes Hoehenfeld der Makrowelt (Terrain-Vorgabe §6, §95): ein float[] in Weltmetern, Zelle i = (ix, iz),
    /// Weltposition = origin + (ix, iz) · cellSize. Wiederverwendbare Puffer statt Kopien.
    /// </summary>
    public sealed class MacroHeightField
    {
        public readonly int Width, Height;
        public readonly float CellSize;
        public readonly Vector2 Origin;
        public readonly float[] H;

        public MacroHeightField(int width, int height, float cellSize, Vector2 origin)
        {
            Width = width;
            Height = height;
            CellSize = cellSize;
            Origin = origin;
            H = new float[width * height];
        }

        public int Count => H.Length;
        public int Index(int x, int z) => z * Width + x;
        public Vector2 World(int x, int z) => new Vector2(Origin.x + x * CellSize, Origin.y + z * CellSize);
        public Vector2 World(int i) => World(i % Width, i / Width);

        public float Get(int x, int z)
        {
            x = x < 0 ? 0 : x >= Width ? Width - 1 : x;
            z = z < 0 ? 0 : z >= Height ? Height - 1 : z;
            return H[z * Width + x];
        }

        public bool Contains(float wx, float wz) =>
            wx >= Origin.x && wz >= Origin.y && wx <= Origin.x + (Width - 1) * CellSize && wz <= Origin.y + (Height - 1) * CellSize;

        public float SampleBilinear(float wx, float wz) => SampleBilinear(H, wx, wz);

        public float SampleBilinear(float[] data, float wx, float wz)
        {
            float gx = (wx - Origin.x) / CellSize, gz = (wz - Origin.y) / CellSize;
            int x0 = Mathf.FloorToInt(gx), z0 = Mathf.FloorToInt(gz);
            float fx = gx - x0, fz = gz - z0;
            float a = Mathf.Lerp(Get(data, x0, z0), Get(data, x0 + 1, z0), fx);
            float b = Mathf.Lerp(Get(data, x0, z0 + 1), Get(data, x0 + 1, z0 + 1), fx);
            return Mathf.Lerp(a, b, fz);
        }

        private float Get(float[] data, int x, int z)
        {
            x = x < 0 ? 0 : x >= Width ? Width - 1 : x;
            z = z < 0 ? 0 : z >= Height ? Height - 1 : z;
            return data[z * Width + x];
        }

        /// <summary>Catmull-Rom (bikubisch) – fuer das Abtasten hoch aufgeloester Kacheln ohne 40-m-Facetten.</summary>
        public float SampleBicubic(float wx, float wz) => SampleBicubic(H, wx, wz);

        public float SampleBicubic(float[] data, float wx, float wz)
        {
            float gx = (wx - Origin.x) / CellSize, gz = (wz - Origin.y) / CellSize;
            int x1 = Mathf.FloorToInt(gx), z1 = Mathf.FloorToInt(gz);
            float fx = gx - x1, fz = gz - z1;
            float r0 = Cr(Get(data, x1 - 1, z1 - 1), Get(data, x1, z1 - 1), Get(data, x1 + 1, z1 - 1), Get(data, x1 + 2, z1 - 1), fx);
            float r1 = Cr(Get(data, x1 - 1, z1), Get(data, x1, z1), Get(data, x1 + 1, z1), Get(data, x1 + 2, z1), fx);
            float r2 = Cr(Get(data, x1 - 1, z1 + 1), Get(data, x1, z1 + 1), Get(data, x1 + 1, z1 + 1), Get(data, x1 + 2, z1 + 1), fx);
            float r3 = Cr(Get(data, x1 - 1, z1 + 2), Get(data, x1, z1 + 2), Get(data, x1 + 1, z1 + 2), Get(data, x1 + 2, z1 + 2), fx);
            return Cr(r0, r1, r2, r3, fz);
        }

        private static float Cr(float p0, float p1, float p2, float p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// <summary>Neigung in Grad an Zelle (zentrale Differenzen).</summary>
        public float SlopeDeg(int x, int z)
        {
            float dx = Get(x + 1, z) - Get(x - 1, z);
            float dz = Get(x, z + 1) - Get(x, z - 1);
            return Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz) / (2f * CellSize)) * Mathf.Rad2Deg;
        }

        public void MinMax(out float min, out float max)
        {
            min = float.MaxValue;
            max = float.MinValue;
            foreach (float h in H)
            {
                if (h < min) min = h;
                if (h > max) max = h;
            }
        }

        /// <summary>Pruefsumme fuer Determinismus-Test und GeneratedHash (§4).</summary>
        public string Hash()
        {
            unchecked
            {
                ulong h = 1469598103934665603UL;
                for (int i = 0; i < H.Length; i++)
                {
                    int q = Mathf.RoundToInt(H[i] * 100f);
                    h ^= (uint)q;
                    h *= 1099511628211UL;
                }
                return h.ToString("x16");
            }
        }
    }

    /// <summary>Benannte Masken (0…1) gleicher Aufloesung wie das Hoehenfeld (Vorgabe §34, §78).</summary>
    public sealed class MaskSet
    {
        private readonly int _count;
        private readonly Dictionary<string, float[]> _masks = new Dictionary<string, float[]>();

        public MaskSet(int count) => _count = count;

        public float[] Get(string name)
        {
            if (!_masks.TryGetValue(name, out float[] m))
            {
                m = new float[_count];
                _masks[name] = m;
            }
            return m;
        }

        public bool Has(string name) => _masks.ContainsKey(name);
        public IEnumerable<string> Names => _masks.Keys;

        public const string Protected = "ProtectedMask";
        public const string Transition = "TransitionMask";
        public const string River = "RiverMask";
        public const string Road = "RoadMask";
        public const string Cliff = "CliffMask";
        public const string Mountain = "MountainMask";
        public const string Valley = "ValleyMask";
        public const string Plateau = "PlateauMask";
        public const string ScarInner = "VeilScarMask";
        public const string ScarRim = "CanyonRimMask";
        public const string VeilGlass = "VeilGlassMask";
        public const string MeltRock = "MeltRockMask";
        public const string Fracture = "FractureMask";
        public const string AncientMaterial = "AncientMaterialMask";
        public const string VeilInfluence = "VeilInfluence";
        public const string Land = "LandMask";
        public const string Lake = "LakeMask";
        public const string TreeDensity = "TreeDensityMask";
        public const string ShrubDensity = "ShrubDensityMask";
        public const string GrassDensity = "GrassDensityMask";
        public const string Temperature = "Temperature";
        public const string Moisture = "Moisture";
        public const string Settlement = "SettlementSuitability";
    }

    /// <summary>Alles, was ein Durchlauf braucht (Vorgabe §2 TerrainGenerationContext).</summary>
    public sealed class TerrainGenerationContext
    {
        public readonly MacroWorldProfile Profile;
        public readonly MacroHeightField Field;
        public readonly MaskSet Masks;
        public readonly int WorldSeed, RegionSeed, TerrainSeed, BiomeSeed, DetailSeed;
        public readonly List<string> Log = new List<string>();
        public ProtectedHeightSampler Protected;
        public HydrologyResult Hydrology;
        public byte[] Biomes;
        /// <summary>Fortschritt melden; true = abbrechen (Vorgabe §96).</summary>
        public Func<string, float, bool> Progress = (_, __) => false;

        public TerrainGenerationContext(MacroWorldProfile profile)
        {
            Profile = profile;
            Field = new MacroHeightField(profile.GridWidth, profile.GridHeight, profile.cellSize, profile.bounds.min);
            Masks = new MaskSet(Field.Count);
            WorldSeed = profile.worldSeed;
            RegionSeed = Ashveil.World.AshNoise.Derive(WorldSeed, "region");
            TerrainSeed = Ashveil.World.AshNoise.Derive(WorldSeed, "terrain");
            BiomeSeed = Ashveil.World.AshNoise.Derive(WorldSeed, "biome");
            DetailSeed = Ashveil.World.AshNoise.Derive(WorldSeed, "detail");
        }

        /// <summary>Jede Zelle parallel bearbeiten (deterministisch: jede Zelle haengt nur von ihrer Lage ab).</summary>
        public void ForEachCell(Action<int, Vector2> body)
        {
            MacroHeightField f = Field;
            System.Threading.Tasks.Parallel.For(0, f.Height, z =>
            {
                for (int x = 0; x < f.Width; x++)
                {
                    int i = z * f.Width + x;
                    body(i, f.World(x, z));
                }
            });
        }
    }
}
