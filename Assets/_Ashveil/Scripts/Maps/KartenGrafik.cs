using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace Ashveil.Maps
{
    /// <summary>
    /// Kleine Grafikhelfer für die Karten: Formen als Sprites aus Code, Bilder aus Dateien, Farben je PvP-Art und der
    /// Pergament-Schleier über unbekannten Flächen (UI-203: „Unbekannte Gebiete sind verhüllt und decken sich beim Erkunden auf“).
    /// </summary>
    internal static class KartenGrafik
    {
        public static readonly Color Freundlich = new Color(0.42f, 0.86f, 0.38f);
        public static readonly Color Feindlich = new Color(0.95f, 0.32f, 0.26f);
        public static readonly Color Umkaempft = new Color(0.98f, 0.76f, 0.30f);
        public static readonly Color Zuflucht = new Color(0.55f, 0.80f, 0.98f);
        public static readonly Color Gold = new Color(0.93f, 0.80f, 0.48f);
        public static readonly Color Gruppe = new Color(0.45f, 0.78f, 1f);
        /// <summary>Tinte für Beschriftungen auf Pergament.</summary>
        public static readonly Color Tinte = new Color(0.20f, 0.13f, 0.07f);

        public static Color FarbeFuer(PvpArt p)
        {
            switch (p)
            {
                case PvpArt.Freundlich: return Freundlich;
                case PvpArt.Feindlich: return Feindlich;
                case PvpArt.Zuflucht:
                case PvpArt.Neutral: return Zuflucht;
                default: return Umkaempft;
            }
        }

        public static string TextFuer(PvpArt p)
        {
            switch (p)
            {
                case PvpArt.Freundlich: return "Freundliches Gebiet";
                case PvpArt.Feindlich: return "Feindliches Gebiet";
                case PvpArt.Zuflucht: return "Zuflucht (kein PvP)";
                case PvpArt.Neutral: return "Neutral (kein PvP)";
                default: return "Umkämpftes Gebiet";
            }
        }

        private static Sprite _kreis, _ring, _pfeil;

        /// <summary>Weiche Kreisscheibe (Maske der Minimap, Punkte).</summary>
        public static Sprite Kreis => _kreis != null ? _kreis : (_kreis = Form("Kreis", 128, (x, y) => Mathf.Clamp01((1f - Mathf.Sqrt(x * x + y * y)) * 64f)));

        /// <summary>Kreisring (Rahmen der Minimap, Questgebiete).</summary>
        public static Sprite Ring => _ring != null ? _ring : (_ring = Form("Ring", 128, (x, y) =>
        {
            float r = Mathf.Sqrt(x * x + y * y);
            return Mathf.Clamp01((1f - r) * 64f) * Mathf.Clamp01((r - 0.9f) * 64f);
        }));

        /// <summary>Spielerpfeil, Spitze nach oben.</summary>
        public static Sprite Pfeil => _pfeil != null ? _pfeil : (_pfeil = Form("Pfeil", 64, (x, y) =>
        {
            // Dreieck mit Kerbe: Spitze (0, 0.9), Füße (±0.6, −0.7), Kerbe (0, −0.35)
            float yy = (y + 0.7f) / 1.6f;                       // 0 unten … 1 Spitze
            if (yy < 0f || yy > 1f) return 0f;
            float halb = 0.6f * (1f - yy);
            float kerbe = y < -0.35f ? (-0.35f - y) / 0.35f * 0.6f : 0f;
            float ax = Mathf.Abs(x);
            return ax <= halb && ax >= kerbe ? 1f : 0f;
        }));

        private delegate float FormFn(float x, float y);

        private static Sprite Form(string name, int n, FormFn fn)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Karten " + name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float x = (i + 0.5f) / n * 2f - 1f, y = (j + 0.5f) / n * 2f - 1f;
                    byte a = (byte)(Mathf.Clamp01(fn(x, y)) * 255f);
                    px[j * n + i] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Bild aus einer Datei (PNG/JPG); null, wenn es fehlt. Nicht lesbar (spart Speicher).</summary>
        public static Texture2D BildLaden(string pfad, bool mipmaps = false)
        {
            if (!File.Exists(pfad)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipmaps) { name = Path.GetFileNameWithoutExtension(pfad), wrapMode = TextureWrapMode.Clamp,
                                                                            filterMode = mipmaps ? FilterMode.Trilinear : FilterMode.Bilinear };
            if (!tex.LoadImage(File.ReadAllBytes(pfad), true))
            {
                Object.Destroy(tex);
                return null;
            }
            return tex;
        }

        /// <summary>
        /// Bild aus einer Datei laden, ohne das Spiel anzuhalten (CORE-005, Ruckler-Messung 09.10.2026: das erste Öffnen der Karte
        /// kostete 55–60 ms, vor allem für das Entpacken der 2 048-px-Bilder). Unity entpackt JPG/PNG auf einem Hilfsfaden
        /// (UnityWebRequestTexture); im Hauptfaden bleibt nur das Hochladen. Nicht lesbar, sRGB, wahlweise mit Mipmaps – wie
        /// <see cref="BildLaden"/>. <paramref name="fertig"/> bekommt null, wenn die Datei fehlt oder das Laden scheitert;
        /// dann bleibt <see cref="BildLaden"/> als Ersatz. Offene Anfragen stehen in <paramref name="offen"/>, damit der Besitzer
        /// sie beim Zerstören abbrechen kann.
        /// </summary>
        public static IEnumerator BildLadenNebenbei(string pfad, bool mipmaps, List<UnityWebRequest> offen, Action<Texture2D> fertig)
        {
            if (string.IsNullOrEmpty(pfad) || !File.Exists(pfad)) { fertig?.Invoke(null); yield break; }
            UnityWebRequest uwr = null;
            try
            {
                DownloadedTextureParams p = DownloadedTextureParams.Default;
                p.readable = false;
                p.mipmapChain = mipmaps;
                p.linearColorSpace = false;
                uwr = UnityWebRequestTexture.GetTexture(new Uri(Path.GetFullPath(pfad)).AbsoluteUri, p);
            }
            catch (Exception) { uwr = null; }
            if (uwr == null) { fertig?.Invoke(null); yield break; }
            offen?.Add(uwr);
            yield return uwr.SendWebRequest();
            Texture2D tex = null;
            try
            {
                if (uwr.result == UnityWebRequest.Result.Success)
                {
                    tex = DownloadHandlerTexture.GetContent(uwr);
                    if (tex != null)
                    {
                        tex.name = Path.GetFileNameWithoutExtension(pfad);
                        tex.wrapMode = TextureWrapMode.Clamp;
                        tex.filterMode = mipmaps ? FilterMode.Trilinear : FilterMode.Bilinear;
                    }
                }
            }
            catch (Exception) { tex = null; }
            finally
            {
                offen?.Remove(uwr);
                uwr.Dispose();
            }
            fertig?.Invoke(tex);
        }

        /// <summary>Erstes vorhandenes Bild aus mehreren Namen (z. B. .jpg vor .png).</summary>
        public static string ErstesBild(string ordner, string name)
        {
            foreach (string endung in new[] { ".jpg", ".png" })
            {
                string p = Path.Combine(ordner, name + endung);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        /// <summary>Hex-Farbe (#rrggbb) mit Ersatz.</summary>
        public static Color Hex(string hex, Color ersatz)
        {
            return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out Color c) ? c : ersatz;
        }

        // ---------------------------------------------------------------- Pergament-Schleier

        /// <summary>
        /// Schleierbild aus einer Deckung je Pixel (1 = verhüllt, 0 = frei): weich gezeichnet, Pergamentton mit Wolkenrauschen
        /// und etwas dunklerem, „verwischtem“ Rand – wie ungezeichnetes Pergament. w × h Pixel, Zeile 0 = Süden.
        /// Deckkraft: Gebietskarte fast deckend (wie Classic), Weltkarte etwas lichter – Küsten und Gebirge ahnt man.
        /// </summary>
        public static void Schleier(float[] deckung, int w, int h, Color32[] ziel, int weich, float saat, float deckkraft = 0.95f)
        {
            Weichzeichnen(deckung, w, h, weich);
            Weichzeichnen(deckung, w, h, weich);
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    int k = j * w + i;
                    float a = Mathf.Clamp01(deckung[k]);
                    if (a <= 0.003f) { ziel[k] = new Color32(0, 0, 0, 0); continue; }
                    float n = Rauschen(i * 0.045f + saat, j * 0.045f - saat) * 0.65f + Rauschen(i * 0.13f - saat, j * 0.13f + saat) * 0.35f;
                    float rand = 4f * a * (1f - a);                       // 1 an der Kante, 0 innen und außen
                    float hell = 0.92f + (n - 0.5f) * 0.16f - rand * 0.22f;
                    var c = new Color(0.86f * hell, 0.77f * hell, 0.60f * hell, Mathf.Clamp01(a * 1.08f) * deckkraft);
                    ziel[k] = c;
                }
        }

        /// <summary>Kastenfilter in beide Richtungen (Radius r), in place.</summary>
        public static void Weichzeichnen(float[] a, int w, int h, int r)
        {
            if (r <= 0) return;
            var tmp = new float[a.Length];
            float f = 1f / (2 * r + 1);
            for (int j = 0; j < h; j++)
            {
                int zeile = j * w;
                for (int i = 0; i < w; i++)
                {
                    float s = 0f;
                    for (int d = -r; d <= r; d++) s += a[zeile + Mathf.Clamp(i + d, 0, w - 1)];
                    tmp[zeile + i] = s * f;
                }
            }
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    float s = 0f;
                    for (int d = -r; d <= r; d++) s += tmp[Mathf.Clamp(j + d, 0, h - 1) * w + i];
                    a[j * w + i] = s * f;
                }
        }

        /// <summary>Glattes Wertrauschen 0 … 1 (deterministisch, ohne Allokation).</summary>
        public static float Rauschen(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(xi, yi), b = Hash(xi + 1, yi), c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
            }
        }
    }

    /// <summary>Einfacher LRU-Speicher für geladene Bilder (Minimap-Kacheln, Gebietskarten).</summary>
    internal sealed class BildSpeicher
    {
        private readonly int _max;
        private readonly Dictionary<string, Texture2D> _bilder = new Dictionary<string, Texture2D>();
        private readonly LinkedList<string> _reihe = new LinkedList<string>();

        public BildSpeicher(int max) { _max = Mathf.Max(max, 1); }

        public Texture2D Holen(string pfad, bool mipmaps = false)
        {
            if (string.IsNullOrEmpty(pfad)) return null;
            if (_bilder.TryGetValue(pfad, out Texture2D t))
            {
                _reihe.Remove(pfad); _reihe.AddFirst(pfad);
                return t;
            }
            t = KartenGrafik.BildLaden(pfad, mipmaps);
            _bilder[pfad] = t;                                  // auch "fehlt" merken (null), damit nicht jedes Bild neu gesucht wird
            _reihe.AddFirst(pfad);
            Kuerzen();
            return t;
        }

        /// <summary>Schon im Speicher (auch als „fehlt“)?</summary>
        public bool Hat(string pfad) => !string.IsNullOrEmpty(pfad) && _bilder.ContainsKey(pfad);

        /// <summary>
        /// Nebenbei geladenes Bild ablegen. Liegt das Bild inzwischen schon im Speicher (synchron geladen), wird das neue
        /// verworfen. null wird nicht gemerkt: Dann versucht es <see cref="Holen"/> später noch einmal selbst.
        /// </summary>
        public void Ablegen(string pfad, Texture2D t)
        {
            if (t == null) return;
            if (string.IsNullOrEmpty(pfad) || _bilder.ContainsKey(pfad)) { Object.Destroy(t); return; }
            _bilder[pfad] = t;
            _reihe.AddFirst(pfad);
            Kuerzen();
        }

        private void Kuerzen()
        {
            while (_reihe.Count > _max)
            {
                string alt = _reihe.Last.Value; _reihe.RemoveLast();
                if (_bilder.TryGetValue(alt, out Texture2D a) && a != null) Object.Destroy(a);
                _bilder.Remove(alt);
            }
        }

        public void Leeren()
        {
            foreach (Texture2D t in _bilder.Values) if (t != null) Object.Destroy(t);
            _bilder.Clear(); _reihe.Clear();
        }
    }
}
