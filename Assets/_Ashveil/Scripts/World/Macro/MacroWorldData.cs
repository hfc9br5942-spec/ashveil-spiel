using UnityEngine;

namespace Ashveil.World
{
    /// <summary>
    /// Ergebnis der Makro-Generierung fuer die Laufzeit (Terrain-Vorgabe §79, §123): ein grobes Hoehenfeld mit Biom- und
    /// Schleier-Karte. Die schweren Generatoren bleiben im Editor; das Spiel bekommt nur diese Daten.
    /// Hoehen als ushort (Binaerdatei), damit das Asset klein bleibt.
    /// </summary>
    public sealed class MacroWorldData : ScriptableObject
    {
        public const string ResourcePath = "World/MacroWorldData";

        [Tooltip("Welt-Ecke (x, z) der Zelle 0/0")] public Vector2 origin;
        public float cellSize = 40f;
        public int width, height;
        public float minHeight = -1200f, maxHeight = 2000f;
        public float seaLevel = -24f;
        public int worldSeed;
        public int generationVersion;
        public string generatedHash = "";
        [Tooltip("ushort je Zelle, little endian, zeilenweise (z aussen)")] public TextAsset heights;
        [Tooltip("MacroBiome je Zelle")] public TextAsset biomes;
        [Tooltip("Schleiereinfluss 0…255 je Zelle")] public TextAsset veil;

        private float[] _h;
        private byte[] _b, _v;

        public Rect Bounds => new Rect(origin.x, origin.y, width * cellSize, height * cellSize);

        public bool Ready
        {
            get
            {
                if (_h != null) return true;
                if (heights == null || width <= 0 || height <= 0) return false;
                byte[] raw = heights.bytes;
                if (raw.Length < width * height * 2) return false;
                _h = new float[width * height];
                float range = maxHeight - minHeight;
                for (int i = 0; i < _h.Length; i++)
                    _h[i] = minHeight + (raw[i * 2] | (raw[i * 2 + 1] << 8)) / 65535f * range;
                _b = biomes != null ? biomes.bytes : null;
                _v = veil != null ? veil.bytes : null;
                return true;
            }
        }

        private void OnValidate() => _h = null;

        private float H(int x, int z)
        {
            x = Mathf.Clamp(x, 0, width - 1);
            z = Mathf.Clamp(z, 0, height - 1);
            return _h[z * width + x];
        }

        public float SampleHeight(float wx, float wz)
        {
            if (!Ready) return 0f;
            float gx = (wx - origin.x) / cellSize, gz = (wz - origin.y) / cellSize;
            int x0 = Mathf.FloorToInt(gx), z0 = Mathf.FloorToInt(gz);
            float fx = gx - x0, fz = gz - z0;
            float a = Mathf.Lerp(H(x0, z0), H(x0 + 1, z0), fx);
            float b = Mathf.Lerp(H(x0, z0 + 1), H(x0 + 1, z0 + 1), fx);
            return Mathf.Lerp(a, b, fz);
        }

        public MacroBiome SampleBiome(float wx, float wz)
        {
            if (!Ready || _b == null) return MacroBiome.TemperatePlains;
            int x = Mathf.Clamp(Mathf.RoundToInt((wx - origin.x) / cellSize), 0, width - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt((wz - origin.y) / cellSize), 0, height - 1);
            int i = z * width + x;
            return i < _b.Length ? (MacroBiome)_b[i] : MacroBiome.TemperatePlains;
        }

        public float SampleVeil(float wx, float wz)
        {
            if (!Ready || _v == null) return 0f;
            int x = Mathf.Clamp(Mathf.RoundToInt((wx - origin.x) / cellSize), 0, width - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt((wz - origin.y) / cellSize), 0, height - 1);
            int i = z * width + x;
            return i < _v.Length ? _v[i] / 255f : 0f;
        }
    }

    /// <summary>
    /// Laufzeit-API der Makrowelt (Terrain-Vorgabe §79): Hoehe, Neigung, Biom, Schleiereinfluss an jeder Weltposition –
    /// auch ausserhalb geladener Terrains. Liefert neutrale Werte, solange keine Makrodaten erzeugt wurden.
    /// </summary>
    public static class MacroWorld
    {
        private static MacroWorldData _data;
        private static float _nextTry = -1f;

        /// <summary>Fuer Editor-Werkzeuge: Daten direkt setzen (statt aus Resources).</summary>
        public static void Override(MacroWorldData data) => _data = data;

        public static MacroWorldData Data
        {
            get
            {
                if (_data == null && Time.realtimeSinceStartup >= _nextTry)
                {
                    _nextTry = Time.realtimeSinceStartup + 5f;
                    _data = Resources.Load<MacroWorldData>(MacroWorldData.ResourcePath);
                }
                return _data;
            }
        }

        public static bool Available => Data != null && Data.Ready;

        public static float SampleMacroHeight(Vector3 worldPosition) => Available ? Data.SampleHeight(worldPosition.x, worldPosition.z) : 0f;

        /// <summary>Neigung in Grad (aus dem groben Feld, also grossraeumig).</summary>
        public static float SampleSlope(Vector3 worldPosition)
        {
            if (!Available) return 0f;
            float s = Data.cellSize;
            float x = worldPosition.x, z = worldPosition.z;
            float dx = Data.SampleHeight(x + s, z) - Data.SampleHeight(x - s, z);
            float dz = Data.SampleHeight(x, z + s) - Data.SampleHeight(x, z - s);
            return Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz) / (2f * s)) * Mathf.Rad2Deg;
        }

        public static MacroBiome SampleBiome(Vector3 worldPosition) => Available ? Data.SampleBiome(worldPosition.x, worldPosition.z) : MacroBiome.TemperatePlains;

        public static float SampleVeilInfluence(Vector3 worldPosition) => Available ? Data.SampleVeil(worldPosition.x, worldPosition.z) : 0f;
    }
}
