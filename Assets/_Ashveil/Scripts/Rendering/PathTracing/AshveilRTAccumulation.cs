using UnityEngine;

namespace Ashveil.Rendering.PathTracing
{
    /// <summary>
    /// Persistente Puffer (§59–60, §66): Summe der Strahldichte in 32 Bit (rgb) mit Probenzahl je Pixel (a),
    /// Momente der Luminanz fuer die Varianz, Albedo/Normale/Tiefe des ersten Treffers als Hilfsbilder fuer das
    /// Entrauschen, Kachelmaske fuer adaptive Abtastung und Zwischenpuffer des Entrauschers.
    /// Ausgabe = Summe / Anzahl. Keine Kahan-Summe noetig: 32-Bit-Gleitkomma haelt bei 4096 Proben ~12 Bit Reserve.
    /// </summary>
    public sealed class AshveilRTAccumulation : System.IDisposable
    {
        public RenderTexture Accum, Moments, Albedo, NormalDepth, Active, IrrA, IrrB, Guide, Denoised;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Tile { get; private set; } = 8;
        public int SamplesTaken { get; set; }        // hoechste Probenzahl eines Pixels (gleichmaessig ohne Adaptivitaet)
        public int Frames { get; set; }
        public float StartedAt { get; set; }
        public bool PendingReset { get; set; } = true;
        public ulong Bytes { get; private set; }

        public bool Ensure(int w, int h, int tile)
        {
            if (w == Width && h == Height && tile == Tile && Accum != null && Accum.IsCreated()) return false;
            Release();
            Width = w; Height = h; Tile = Mathf.Max(1, tile);
            Accum = Make("PT_Summe", w, h, RenderTextureFormat.ARGBFloat);
            Moments = Make("PT_Momente", w, h, RenderTextureFormat.ARGBFloat);
            Albedo = Make("PT_Albedo", w, h, RenderTextureFormat.ARGBFloat);
            NormalDepth = Make("PT_NormaleTiefe", w, h, RenderTextureFormat.ARGBFloat);
            IrrA = Make("PT_EntrauschenA", w, h, RenderTextureFormat.ARGBHalf);
            IrrB = Make("PT_EntrauschenB", w, h, RenderTextureFormat.ARGBHalf);
            Guide = Make("PT_Fuehrung", w, h, RenderTextureFormat.ARGBHalf);
            Denoised = Make("PT_Entrauscht", w, h, RenderTextureFormat.ARGBHalf);
            Active = Make("PT_Kacheln", (w + Tile - 1) / Tile, (h + Tile - 1) / Tile, RenderTextureFormat.RFloat);
            Bytes = (ulong)w * (ulong)h * (16ul * 4 + 8ul * 4);
            PendingReset = true;
            return true;
        }

        private static RenderTexture Make(string name, int w, int h, RenderTextureFormat f)
        {
            var rt = new RenderTexture(w, h, 0, f, RenderTextureReadWrite.Linear)
            {
                name = name, enableRandomWrite = true, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                useMipMap = false, hideFlags = HideFlags.DontSave
            };
            rt.Create();
            return rt;
        }

        public void Reset()
        {
            PendingReset = true;
            SamplesTaken = 0;
            Frames = 0;
            StartedAt = Time.realtimeSinceStartup;
        }

        public void Release()
        {
            foreach (RenderTexture rt in new[] { Accum, Moments, Albedo, NormalDepth, Active, IrrA, IrrB, Guide, Denoised })
                if (rt != null) { rt.Release(); Object.Destroy(rt); }
            Accum = Moments = Albedo = NormalDepth = Active = IrrA = IrrB = Guide = Denoised = null;
            Width = Height = 0;
        }

        public void Dispose() => Release();
    }
}
