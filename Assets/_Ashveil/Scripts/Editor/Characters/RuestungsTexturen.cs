using System;
using System.IO;
using Ashveil.Data;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Auftrag „ruestungs_texturen“ (22.10.2026, Max: „Kleidung und Ausrüstung so, wie sie in WoW an den Figuren sitzen – dürfen
    /// Soulslike aussehen, sollen aber so anliegen“): WoW malt Brust/Arme/Beine als Textur auf den Körper (eng, ohne Durchstechen).
    /// Unsere Schalen sind genau das – versetzte Hautkopien am selben Skelett –, waren aber nur einfarbig. Hier entstehen kachelbare
    /// Graustufen-Texturen je Oberfläche (Farbe kommt vom Gegenstand) samt Normalmap, prozedural, ohne Download und ohne Kosten:
    ///   Metall: übereinanderliegende Schienen mit Nieten, abgewetzten Kanten, Kratzern und Patina in den Fugen
    ///   Kette: versetzte Ringe mit dunklen Zwischenräumen · Leder: Narbung, Flecken, Naht oben/unten
    ///   Stoff: Gewebe mit Flecken · Fell: Strähnen · Knochen: Risse · Holz: Maserung
    /// Ablage: Resources/Ruestung/T_Ruestung_{Art}.png und _N.png (Normalmap-Import). Lädt SurfaceMaterials zur Laufzeit.
    /// </summary>
    [InitializeOnLoad]
    public static class RuestungsTexturen
    {
        public const string Ordner = "Assets/_Ashveil/Resources/Ruestung";   // zur Laufzeit per Resources.Load (SurfaceMaterials)
        private const int Size = 512;

        static RuestungsTexturen()
        {
            Auftragsordner.Registrieren("ruestungs_texturen", (a, fertig) => fertig(true, Erzeugen()), false, 300);
        }

        [MenuItem("Ashveil/Charaktere/Rüstungstexturen erzeugen")]
        public static string Erzeugen()
        {
            Directory.CreateDirectory(Ordner);
            string info = "";
            foreach (SurfaceKind k in new[] { SurfaceKind.Metal, SurfaceKind.Mail, SurfaceKind.Leather, SurfaceKind.Cloth, SurfaceKind.Fur, SurfaceKind.Bone, SurfaceKind.Wood })
            {
                var h = new float[Size * Size];
                var a = new float[Size * Size];
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        float u = x / (float)Size, v = y / (float)Size;
                        Muster(k, u, v, out h[y * Size + x], out a[y * Size + x]);
                    }
                Speichern(k, h, a, Staerke(k));
                info += k + " ";
            }
            BandMaske();
            info += "Band ";
            AssetDatabase.Refresh();
            Import(Ordner + "/T_Ruestung_Band.png", false);
            foreach (SurfaceKind k in new[] { SurfaceKind.Metal, SurfaceKind.Mail, SurfaceKind.Leather, SurfaceKind.Cloth, SurfaceKind.Fur, SurfaceKind.Bone, SurfaceKind.Wood })
            {
                Import(Pfad(k, false), false);
                Import(Pfad(k, true), true);
            }
            return "Rüstungstexturen erzeugt (" + Size + "²): " + info.Trim();
        }

        public static string Pfad(SurfaceKind k, bool normal) => Ordner + "/T_Ruestung_" + k + (normal ? "_N" : "") + ".png";

        private static float Staerke(SurfaceKind k) => k == SurfaceKind.Mail ? 6f : k == SurfaceKind.Metal ? 5f : k == SurfaceKind.Cloth ? 3f : 3.5f;

        // ------------------------------------------------------------------ Muster (Höhe 0..1, Helligkeit 0..1), kachelbar

        private static void Muster(SurfaceKind k, float u, float v, out float h, out float a)
        {
            float grime = Fbm(u, v, 4, 5, 11);                 // großflächiger Schmutz/Abnutzung
            switch (k)
            {
                case SurfaceKind.Metal:
                {
                    // vier übereinanderliegende Schienen (wie Plattenröcke/Armschienen), jede oben unter der nächsten
                    float t = Frac(v * 4f);
                    float lame = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.07f)) * (1f - 0.18f * t);
                    // Nieten knapp unter der Schienenkante, 8 je Umlauf (Abstand in Texturkoordinaten: Zelle 1/8 breit, Schiene 1/4 hoch)
                    float dx = (Frac(u * 8f) - 0.5f) / 8f, dy = (t - 0.15f) / 4f;
                    float niet = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / 0.012f);
                    niet = Mathf.Sqrt(niet);
                    // 22.10. Souls-Optik: Dellen (grobes Rauschen in der Höhe), wenige feine Kratzer in beliebiger Richtung,
                    // dunkle Patina/Rost fleckig und in den Fugen, blank nur an den abgewetzten Kanten und Nieten
                    float delle = Fbm(u, v, 3, 6, 51);
                    float kratz = Mathf.Clamp01(1f - Mathf.Abs(Fbm(u * 2f + v, v, 3, 20, 5) - 0.5f) * 90f) * 0.5f;
                    float rost = Mathf.Clamp01((Fbm(u, v, 4, 5, 61) - 0.48f) * 3.5f);
                    h = 0.15f + lame * 0.7f + niet * 0.25f - kratz * 0.04f + (delle - 0.5f) * 0.12f + (Fbm(u, v, 3, 32, 2) - 0.5f) * 0.04f;
                    float fuge = 1f - Mathf.Clamp01(t / 0.06f);                // Patina/Dreck in der Fuge
                    float kante = Mathf.Clamp01(1f - Mathf.Abs(t - 0.075f) / 0.025f); // abgewetzte, blanke Kante
                    a = 0.42f + 0.12f * grime + 0.3f * kante + 0.25f * niet + 0.2f * kratz - 0.3f * fuge - 0.18f * rost;
                    break;
                }
                case SurfaceKind.Mail:
                {
                    const int n = 28;
                    float row = v * n;
                    int r = Mathf.FloorToInt(row);
                    float best = 9f;
                    for (int dr = -1; dr <= 1; dr++)
                    {
                        int rr = r + dr;
                        float off = (rr & 1) == 0 ? 0f : 0.5f;
                        float cy = rr + 0.5f;
                        float cxF = u * n - off;
                        int c = Mathf.FloorToInt(cxF);
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            float cx = c + dc + 0.5f;
                            float dx = cxF - cx, dy = (row - cy) * 1.15f;
                            best = Mathf.Min(best, Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - 0.56f));   // 22.10.: Ringe greifen ineinander (Radius > halber Abstand)
                        }
                    }
                    float ring = Mathf.Clamp01(1f - best / 0.13f);
                    h = Mathf.Sqrt(ring);
                    float rostK = Mathf.Clamp01((Fbm(u, v, 4, 5, 63) - 0.5f) * 3f);
                    a = Mathf.Lerp(0.08f, 0.62f + 0.12f * grime - 0.2f * rostK, ring) * (0.85f + 0.15f * Fbm(u, v, 3, 40, 9));
                    break;
                }
                case SurfaceKind.Leather:
                {
                    // 22.10. Souls-Optik: kräftigere Narbung, Knitterfalten quer, abgewetzte helle Stellen, dunkler Grundton
                    float narbe = Fbm(u, v, 4, 48, 3);
                    float flecken = Fbm(u, v, 3, 6, 7);
                    float falte = Mathf.Clamp01(1f - Mathf.Abs(Fbm(u, v * 2f, 3, 4, 71) - 0.5f) * 25f);
                    float naht = Naht(u, v, 0.05f) + Naht(u, v, 0.95f);
                    h = 0.5f + (narbe - 0.5f) * 0.6f - naht * 0.25f - falte * 0.15f;
                    a = 0.42f + 0.3f * flecken + 0.2f * (narbe - 0.5f) - 0.3f * naht - 0.12f * falte;
                    break;
                }
                case SurfaceKind.Cloth:
                {
                    const float f = 96f;
                    float wu = Mathf.Sin(u * f * Mathf.PI * 2f) * 0.5f + 0.5f;
                    float wv = Mathf.Sin(v * f * Mathf.PI * 2f) * 0.5f + 0.5f;
                    bool ueber = ((Mathf.FloorToInt(u * f) + Mathf.FloorToInt(v * f)) & 1) == 0;
                    float web = ueber ? wu : wv;
                    h = 0.4f + web * 0.4f;
                    a = 0.66f + 0.14f * web + 0.22f * (grime - 0.5f) - 0.15f * Mathf.Clamp01((Fbm(u, v, 3, 4, 13) - 0.62f) * 6f);
                    break;
                }
                case SurfaceKind.Fur:
                {
                    float s = Fbm(u, v, 4, 64, 17);   // ganzzahlige Faktoren – sonst kachelt die Textur nicht nahtlos
                    h = s;
                    a = 0.45f + 0.5f * s * (0.8f + 0.2f * grime);
                    break;
                }
                case SurfaceKind.Bone:
                {
                    float riss = Mathf.Clamp01(1f - Mathf.Abs(Fbm(u, v, 4, 8, 21) - 0.5f) * 40f);
                    h = 0.5f + (Fbm(u, v, 4, 12, 23) - 0.5f) * 0.3f - riss * 0.3f;
                    a = 0.78f + 0.15f * grime - 0.45f * riss;
                    break;
                }
                default: // Holz
                {
                    float mas = Mathf.Sin((u * 24f + Fbm(u, v, 3, 4, 31) * 3f) * Mathf.PI * 2f) * 0.5f + 0.5f;
                    h = 0.4f + mas * 0.3f;
                    a = 0.5f + 0.3f * mas + 0.15f * (grime - 0.5f);
                    break;
                }
            }
            h = Mathf.Clamp01(h);
            a = Mathf.Clamp01(a);
        }

        private static float Naht(float u, float v, float lage)
        {
            float d = Mathf.Abs(v - lage);
            if (d > 0.012f) return 0f;
            float strich = Frac(u * 48f) < 0.6f ? 1f : 0f;     // gestrichelte Naht
            return strich * (1f - d / 0.012f);
        }

        // ------------------------------------------------------------------ kachelbares Rauschen

        private static float Frac(float x) => x - Mathf.Floor(x);

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 2147483647;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }

        /// <summary>Wertrauschen mit Periode 'p' Zellen (kachelt in u und v).</summary>
        private static float Noise(float u, float v, int p, int seed)
        {
            float x = Frac(u) * p, y = Frac(v) * p;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            int x1 = (x0 + 1) % p, y1 = (y0 + 1) % p;
            float a = Hash(x0, y0, seed), b = Hash(x1, y0, seed), c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static float Fbm(float u, float v, int oktaven, int p, int seed)
        {
            float s = 0f, amp = 0.5f, sum = 0f;
            for (int i = 0; i < oktaven; i++)
            {
                s += Noise(u, v, p << i, seed + i * 31) * amp;
                sum += amp;
                amp *= 0.5f;
            }
            return s / sum;
        }

        // ------------------------------------------------------------------ Speichern

        private static void Speichern(SurfaceKind k, float[] h, float[] a, float staerke)
        {
            var alb = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var nrm = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var ca = new Color32[Size * Size];
            var cn = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int i = y * Size + x;
                    byte g = (byte)Mathf.RoundToInt(a[i] * 255f);
                    ca[i] = new Color32(g, g, g, 255);
                    float hl = h[y * Size + (x + Size - 1) % Size], hr = h[y * Size + (x + 1) % Size];
                    float hd = h[((y + Size - 1) % Size) * Size + x], hu = h[((y + 1) % Size) * Size + x];
                    Vector3 n = new Vector3((hl - hr) * staerke, (hd - hu) * staerke, 1f / Size * 64f).normalized;
                    cn[i] = new Color32((byte)Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f), (byte)Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f), 255);
                }
            alb.SetPixels32(ca); alb.Apply();
            nrm.SetPixels32(cn); nrm.Apply();
            File.WriteAllBytes(Pfad(k, false), alb.EncodeToPNG());
            File.WriteAllBytes(Pfad(k, true), nrm.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(alb);
            UnityEngine.Object.DestroyImmediate(nrm);
        }

        /// <summary>
        /// Maske der Bänder (Säume, Manschetten, Steppnähte, Leisten): UV 0,25–0,75 = Band (deckend), außen durchsichtig (Alpha-Schnitt).
        /// Die Bandschale ist etwas breiter als das Band – erst die Maske schneidet die Kante sauber, unabhängig von der Dreiecksgröße
        /// des Körpers (Tripo ~2–3 cm, Band 1,4 cm). Dunkle Randlinie wie eine Naht, leicht fleckig.
        /// </summary>
        private static void BandMaske()
        {
            const int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var c = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float du = Mathf.Abs(u - 0.5f) / 0.25f, dv = Mathf.Abs(v - 0.5f) / 0.25f;   // 1 = Bandkante
                    float rand = Mathf.Max(du, dv);
                    byte alpha = (byte)(rand <= 1f ? 255 : 0);
                    float hell = 0.8f + 0.12f * (Noise(u, v, 16, 41) - 0.5f) - (rand > 0.8f && rand <= 1f ? 0.35f : 0f);
                    byte g = (byte)Mathf.RoundToInt(Mathf.Clamp01(hell) * 255f);
                    c[y * n + x] = new Color32(g, g, g, alpha);
                }
            t.SetPixels32(c); t.Apply();
            File.WriteAllBytes(Ordner + "/T_Ruestung_Band.png", t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
        }

        private static void Import(string pfad, bool normal)
        {
            var imp = AssetImporter.GetAtPath(pfad) as TextureImporter;
            if (imp == null) return;
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = !normal;
            bool band = pfad.EndsWith("_Band.png");
            imp.wrapMode = band ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            if (band) { imp.alphaIsTransparency = true; imp.alphaSource = TextureImporterAlphaSource.FromInput; }
            imp.mipmapEnabled = true;
            imp.anisoLevel = 4;
            imp.maxTextureSize = Size;
            imp.SaveAndReimport();
        }
    }
}
