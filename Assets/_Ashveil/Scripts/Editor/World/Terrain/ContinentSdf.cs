using System;
using UnityEngine;

namespace Ashveil.EditorTools.TerrainGen
{
    /// <summary>
    /// Kuestenlinie aus Max' Weltkarte (Prototyp 30.09.): Land/Meer aus den Kartenfarben gewonnen, als vorzeichenbehafteter
    /// Abstand (m, innen positiv) im Raster 20 m je Kartenpixel. Datei "ContinentSdf.bytes":
    /// "ASDF", Breite, Hoehe (int), Ursprung x, z, Pixelgroesse (float), Min, Max (float), dann ushort-Zeilen von Sued nach Nord.
    /// </summary>
    public sealed class ContinentSdf
    {
        public readonly int Width, Height;
        public readonly float OriginX, OriginZ, Pixel;
        private readonly float[] _d;

        private ContinentSdf(int w, int h, float ox, float oz, float px, float[] d)
        {
            Width = w;
            Height = h;
            OriginX = ox;
            OriginZ = oz;
            Pixel = px;
            _d = d;
        }

        public static ContinentSdf Load(TextAsset asset)
        {
            if (asset == null) return null;
            byte[] b = asset.bytes;
            if (b.Length < 32 || b[0] != 'A' || b[1] != 'S' || b[2] != 'D' || b[3] != 'F') return null;
            int w = BitConverter.ToInt32(b, 4), h = BitConverter.ToInt32(b, 8);
            float ox = BitConverter.ToSingle(b, 12), oz = BitConverter.ToSingle(b, 16), px = BitConverter.ToSingle(b, 20);
            float mn = BitConverter.ToSingle(b, 24), mx = BitConverter.ToSingle(b, 28);
            if (b.Length < 32 + w * h * 2) return null;
            var d = new float[w * h];
            for (int i = 0; i < d.Length; i++) d[i] = mn + BitConverter.ToUInt16(b, 32 + i * 2) / 65535f * (mx - mn);
            return new ContinentSdf(w, h, ox, oz, px, d);
        }

        /// <summary>Abstand zur Kueste (m): positiv an Land, negativ auf See. Ausserhalb der Karte: offene See.</summary>
        public float Sample(float x, float z)
        {
            float gx = (x - OriginX) / Pixel, gz = (z - OriginZ) / Pixel;
            if (gx < 0f || gz < 0f || gx > Width - 1 || gz > Height - 1)
            {
                float out1 = Mathf.Max(Mathf.Max(-gx, gx - (Width - 1)), Mathf.Max(-gz, gz - (Height - 1))) * Pixel;
                return Mathf.Min(Get(Mathf.Clamp((int)gx, 0, Width - 1), Mathf.Clamp((int)gz, 0, Height - 1)), 0f) - out1;
            }
            int x0 = Mathf.Min((int)gx, Width - 2), z0 = Mathf.Min((int)gz, Height - 2);
            float fx = gx - x0, fz = gz - z0;
            float a = Mathf.Lerp(Get(x0, z0), Get(x0 + 1, z0), fx);
            float c = Mathf.Lerp(Get(x0, z0 + 1), Get(x0 + 1, z0 + 1), fx);
            return Mathf.Lerp(a, c, fz);
        }

        private float Get(int x, int z) => _d[z * Width + x];
    }
}
