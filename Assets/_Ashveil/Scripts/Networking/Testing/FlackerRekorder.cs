#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Ashveil.Gameplay.Controls;
using Ashveil.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashveil.Networking
{
    /// <summary>
    /// Flacker-Rekorder (10.10.2026 nachts): Die Flacker-Probe (feste Wege, sechs Grafikvarianten) findet im fertigen Spiel keine
    /// Schattenaussetzer – Max sieht sie beim Spielen trotzdem „ab und zu“. Der Rekorder behält deshalb im Entwicklungs-Build
    /// (Spieltest) ständig die letzten <see cref="Bilder"/> Bilder als kleines Graubild (480×270) im Speicher. Drückt Max <b>F11</b>,
    /// sobald er ein Flackern gesehen hat, schreibt er sie als Übersichtsbild (Raster), die drei größten Sprünge als Bildpaar mit
    /// Maske und eine CSV (Zeit, Bildzeit, Kamera, Mittelwert unten, springende Blöcke) nach Logs/Ashveil/Rekorder/&lt;Uhrzeit&gt;/.
    /// Dazu die Grafikzusammenfassung und der Sonnenstand. Ein großer Sprung wird außerdem von selbst gesichert (höchstens fünf
    /// je Sitzung, mindestens 20 s Abstand), damit auch ein Flackern festgehalten wird, bei dem die Taste zu spät kommt.
    /// Kosten: eine Bildkopie und ein asynchrones Rücklesen je Bild (kein Warten auf die GPU); das Schreiben (PNG, CSV) läuft in einem
    /// Hintergrundfaden mit eigenem PNG-Schreiber – ohne Texture2D, ohne Hochladen (die erste Fassung hielt beim Sichern 700 ms an,
    /// Ruckler-Messung 00:31). Der Selbstauslöser springt nur bei einem Puls (Sprung, der binnen sechs Bildern zurückkehrt) bei
    /// stehender Kamera – nicht bei Ortswechsel, Fenster oder Szenenschnitt. Im Auslieferungs-Build fehlt die Klasse.
    /// Läuft nicht neben Flacker-/Gras-Probe und nie im Editor.
    /// </summary>
    public sealed class FlackerRekorder : MonoBehaviour
    {
        public const string Argument = "-ashveil-ohne-rekorder";   // zum Abschalten (z. B. Ruckler-Messung ohne Rekorder-Kosten)
        private const int W = 480, H = 270, Bilder = 120;
        private const int BW = 8, BH = 8, BX = W / BW, BY = H / BH;    // 60×33 Blöcke
        private const float AutoSchwelle = 6f;                      // Mittelwert unten springt um mehr als 6 Stufen …
        private const float AutoRueckkehr = 3f;                     // … und ist binnen 6 Bildern wieder auf ±3 am alten Wert (Puls)
        private const int AutoFenster = 6;
        private const int AutoHoechstens = 5;
        private const float AutoAbstand = 20f;

        private sealed class Bild
        {
            public byte[] Grau = new byte[W * H];
            public bool Gueltig;
            public int Nummer;
            public float Zeit, Dt;
            public Vector3 Kamera;
            public float Yaw, Pitch;
            public float MittelUnten;
            public float BloeckeSpringend;   // Anteil der Blöcke, deren Mittel sich gegen das Vorbild um > 20 ändert
            public int GiBild;               // 1 = der GI-Bildpass hat in diesem Bild geliefert (sonst klassisches Umgebungslicht)
            public string GiGrund;           // letzter Auslassgrund des GI (Diagnose)
        }

        private readonly Bild[] _ring = new Bild[Bilder];
        private int _kopf;                   // nächster freier Platz
        private int _gefuellt;
        private RenderTexture _voll, _mittel, _klein;
        private int _nummer;
        private bool _schreibe;
        private int _auto;
        private float _letzteAuto = -1000f;
        private int _offen;                  // laufende Rücklesungen
        private int _pulsBild = -1;          // Selbstauslöser: Bildnummer des Sprungs (−1 = keiner offen)
        private float _pulsBasis;            // Mittelwert unten vor dem Sprung

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isEditor) return;
            // 11.10.2026 (Lasttest r2.16): ohne Grafik (Server, Bots mit -nographics) gibt es nichts aufzunehmen – das Rücklesen warnte
            // dort in jedem Bild (17 000 Warnungen mit Stapel je Prozess, 24 MB Protokoll in 5 min)
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsAsyncGPUReadback) return;
            string[] args = Environment.GetCommandLineArgs();
            foreach (string a in args)
                if (a.Equals(Argument, StringComparison.OrdinalIgnoreCase) || a.Equals(FlackerProbe.Argument, StringComparison.OrdinalIgnoreCase) ||
                    a.Equals(GrasProbe.Argument, StringComparison.OrdinalIgnoreCase))
                    return;
            var go = new GameObject("Flacker-Rekorder");
            DontDestroyOnLoad(go);
            go.AddComponent<FlackerRekorder>();
        }

        private IEnumerator Start()
        {
            for (int i = 0; i < Bilder; i++) _ring[i] = new Bild();
            Log("bereit – F11 sichert die letzten " + Bilder + " Bilder nach Logs/Ashveil/Rekorder/");
            var ende = new WaitForEndOfFrame();
            while (true)
            {
                yield return ende;
                if (_schreibe || _offen > 2) continue;
                if (_voll == null || _voll.width != Screen.width || _voll.height != Screen.height)
                {
                    Freigeben();
                    _voll = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32) { name = "Rekorder voll" };
                    _mittel = new RenderTexture(960, 540, 0, RenderTextureFormat.ARGB32) { name = "Rekorder mittel" };
                    _klein = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32) { name = "Rekorder klein" };
                }
                try
                {
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(_voll);
                    Graphics.Blit(_voll, _mittel);
                    Graphics.Blit(_mittel, _klein);
                }
                catch (Exception e) { Log("Aufnahme gestört – " + e.Message); yield break; }

                Bild b = _ring[_kopf];
                b.Gueltig = false;
                b.Nummer = ++_nummer;
                b.Zeit = Time.realtimeSinceStartup;
                b.Dt = Time.unscaledDeltaTime;
                Camera cam = Camera.main;
                if (cam != null)
                {
                    b.Kamera = cam.transform.position;
                    Vector3 e = cam.transform.eulerAngles;
                    b.Yaw = e.y; b.Pitch = e.x > 180f ? e.x - 360f : e.x;
                }
                b.GiBild = Ashveil.Rendering.GI.AshveilDynamicGI.LastScreenFrame == Time.frameCount ? 1 : 0;
                b.GiGrund = b.GiBild == 1 ? "" : Ashveil.Rendering.GI.AshveilDynamicGI.LastSkipReason;
                int platz = _kopf;
                _kopf = (_kopf + 1) % Bilder;
                if (_gefuellt < Bilder) _gefuellt++;
                _offen++;
                AsyncGPUReadback.Request(_klein, 0, TextureFormat.RGBA32, req => Fertig(req, platz));
            }
        }

        private void Fertig(AsyncGPUReadbackRequest req, int platz)
        {
            _offen = Mathf.Max(0, _offen - 1);
            if (req.hasError || _ring == null) return;
            Bild b = _ring[platz];
            if (b == null) return;
            var daten = req.GetData<byte>();
            if (daten.Length < W * H * 4) return;
            byte[] g = b.Grau;
            long summe = 0;
            for (int i = 0, n = W * H; i < n; i++)
            {
                int o = i * 4;
                g[i] = (byte)((daten[o] * 54 + daten[o + 1] * 183 + daten[o + 2] * 19) >> 8);
            }
            // Puffer liegt bei Direct3D kopfüber (Zeile 0 = oberer Bildrand): untere Bildhälfte = Zeilen H/2..H
            for (int y = H / 2; y < H; y++)
            for (int x = 0; x < W; x++) summe += g[y * W + x];
            b.MittelUnten = summe / (float)(W * H / 2);
            Bild vor = _ring[(platz + Bilder - 1) % Bilder];
            b.BloeckeSpringend = vor.Gueltig && vor.Nummer == b.Nummer - 1 ? Bloecke(vor.Grau, g, null) : 0f;
            b.Gueltig = true;

            // Selbstauslöser: nur ein Puls (Sprung und Rückkehr binnen sechs Bildern) bei fast stehender Kamera – ein Ortswechsel,
            // ein Fenster oder ein Szenenschnitt springt und bleibt, das ist kein Aussetzer
            bool folge = vor.Gueltig && vor.Nummer == b.Nummer - 1;
            if (_pulsBild < 0)
            {
                bool kameraRuhig = folge && (b.Kamera - vor.Kamera).sqrMagnitude < 4f && Mathf.Abs(Mathf.DeltaAngle(b.Yaw, vor.Yaw)) < 30f;
                if (kameraRuhig && Mathf.Abs(b.MittelUnten - vor.MittelUnten) > AutoSchwelle) { _pulsBild = b.Nummer; _pulsBasis = vor.MittelUnten; }
            }
            else if (b.Nummer - _pulsBild > AutoFenster) _pulsBild = -1;
            else if (Mathf.Abs(b.MittelUnten - _pulsBasis) < AutoRueckkehr)
            {
                _pulsBild = -1;
                if (_auto < AutoHoechstens && b.Zeit - _letzteAuto > AutoAbstand && b.Zeit > 30f && !_schreibe)
                {
                    _auto++;
                    _letzteAuto = b.Zeit;
                    StartCoroutine(Sichern("auto", 30));   // noch 30 Bilder weiter aufnehmen, damit die Rückkehr mit drin ist
                }
            }
        }

        private static float Bloecke(byte[] a, byte[] b, bool[] maske)
        {
            int springend = 0;
            for (int by = 0; by < BY; by++)
            for (int bx = 0; bx < BX; bx++)
            {
                int sa = 0, sb = 0;
                for (int y = 0; y < BH; y++)
                {
                    int z = (by * BH + y) * W + bx * BW;
                    for (int x = 0; x < BW; x++) { sa += a[z + x]; sb += b[z + x]; }
                }
                bool s = Mathf.Abs(sa - sb) > 20 * BW * BH;
                if (s) springend++;
                if (maske != null) maske[by * BX + bx] = s;
            }
            return springend / (float)(BX * BY);
        }

        private void Update()
        {
            if (!_schreibe && RawInput.WasPressed("Key:F11")) StartCoroutine(Sichern("taste", 0));
        }

        private IEnumerator Sichern(string grund, int nachlauf)
        {
            if (_schreibe) yield break;
            // Nachlauf: weiter aufnehmen, dann einfrieren
            if (nachlauf > 0)
            {
                int ziel = _nummer + nachlauf;
                while (_nummer < ziel) yield return null;
            }
            _schreibe = true;
            float bis = Time.realtimeSinceStartup + 2f;
            while (_offen > 0 && Time.realtimeSinceStartup < bis) yield return null;

            Ashveil.Core.Diagnostics.LogRotation.KeepNewestDirectories(Path.Combine(Application.dataPath, "..", "Logs", "Ashveil", "Rekorder"), 4); // Max 11.10.: nur die letzten 5 behalten
            string ordner = Path.Combine(Application.dataPath, "..", "Logs", "Ashveil", "Rekorder", DateTime.Now.ToString("HHmmss") + "_" + grund);
            // Momentaufnahme auf dem Hauptfaden (nur Kopien), alles Weitere im Hintergrund
            var reihe = new List<Bild>(_gefuellt);
            for (int i = 0; i < _gefuellt; i++)
            {
                Bild b = _ring[(_kopf - _gefuellt + i + Bilder) % Bilder];
                if (!b.Gueltig) continue;
                var k = new Bild { Gueltig = true, Nummer = b.Nummer, Zeit = b.Zeit, Dt = b.Dt, Kamera = b.Kamera, Yaw = b.Yaw, Pitch = b.Pitch,
                    MittelUnten = b.MittelUnten, BloeckeSpringend = b.BloeckeSpringend, GiBild = b.GiBild, GiGrund = b.GiGrund };
                Buffer.BlockCopy(b.Grau, 0, k.Grau, 0, W * H);
                reihe.Add(k);
            }
            string grafik = GraphicsApplier.Instance != null ? GraphicsApplier.Instance.LastSummary : "?";
            Light sonne = RenderSettings.sun;
            string sonneText = sonne != null ? $"Sonne: Richtung {sonne.transform.forward}, Stärke {sonne.intensity:0.00}, Schatten {sonne.shadows}, Schattenstärke {sonne.shadowStrength:0.00}" : "Sonne: keine";
            string kopf = $"ASHVEIL Flacker-Rekorder · {DateTime.Now:yyyy-MM-dd HH:mm:ss} · Grund: {grund} · {Screen.width}×{Screen.height} · {SystemInfo.graphicsDeviceType}";
            _gefuellt = 0; _kopf = 0;
            foreach (Bild b in _ring) b.Gueltig = false;
            _schreibe = false;
            int anzahl = reihe.Count;
            Task.Run(() =>
            {
                try { Schreiben(ordner, reihe, kopf, grafik, sonneText); }
                catch (Exception e) { Debug.LogWarning("[Ashveil] Flacker-Rekorder: Sichern fehlgeschlagen – " + e.Message); }
            });
            Log($"sichere ({grund}): {anzahl} Bilder nach {ordner}");
        }

        /// <summary>Hintergrundfaden: CSV, Übersicht (Raster), drei größte Sprünge, Bericht. Keine Unity-Objekte.</summary>
        private static void Schreiben(string ordner, List<Bild> reihe, string kopf, string grafik, string sonneText)
        {
            Directory.CreateDirectory(ordner);
            var sb = new StringBuilder();
            sb.AppendLine("Bild;Zeit;Bildzeit_ms;KamX;KamY;KamZ;Yaw;Pitch;MittelUnten;BloeckeSpringend_Prozent;GI_Bild;GI_Grund");
            foreach (Bild b in reihe)
                sb.AppendLine(string.Join(";", b.Nummer.ToString(), F(b.Zeit, "0.000"), F(b.Dt * 1000f, "0.0"), F(b.Kamera.x, "0.0"), F(b.Kamera.y, "0.0"),
                    F(b.Kamera.z, "0.0"), F(b.Yaw, "0"), F(b.Pitch, "0"), F(b.MittelUnten, "0.0"), F(b.BloeckeSpringend * 100f, "0.0"), b.GiBild.ToString(), b.GiGrund ?? ""));
            File.WriteAllText(Path.Combine(ordner, "Bilder.csv"), sb.ToString());

            // Übersicht: Raster 10 Spalten, älteste oben links. Zeile 0 des Rücklesepuffers ist der obere Bildrand (PNG ebenso) – kein Tausch
            int spalten = 10, zeilen = Mathf.Max(1, (reihe.Count + spalten - 1) / spalten);
            int rw = W * spalten, rh = H * zeilen;
            var raster = new byte[rw * rh];
            for (int i = 0; i < reihe.Count; i++)
            {
                int cx = (i % spalten) * W, cy = (i / spalten) * H;
                byte[] g = reihe[i].Grau;
                for (int y = 0; y < H; y++) Buffer.BlockCopy(g, y * W, raster, (cy + y) * rw + cx, W);
            }
            Png.Grau(Path.Combine(ordner, "Uebersicht.png"), raster, rw, rh);

            var kandidaten = new List<int>();
            for (int i = 1; i < reihe.Count; i++) kandidaten.Add(i);
            kandidaten.Sort((p, q) => Sprung(reihe[q], reihe[q - 1]).CompareTo(Sprung(reihe[p], reihe[p - 1])));
            var bericht = new StringBuilder();
            bericht.AppendLine(kopf);
            bericht.AppendLine("Grafik: " + grafik);
            bericht.AppendLine(sonneText);
            bericht.AppendLine($"Bilder: {reihe.Count}, Bildzeit im Mittel {MittelDt(reihe):0.0} ms");
            int ohneGi = 0; foreach (Bild b in reihe) if (b.GiBild == 0) ohneGi++;
            bericht.AppendLine($"GI-Bildpass ausgelassen in {ohneGi} von {reihe.Count} Bildern" + (ohneGi > 0 ? " (Gründe in Bilder.csv)" : ""));
            var maske = new bool[BX * BY];
            for (int k = 0; k < Mathf.Min(3, kandidaten.Count); k++)
            {
                int i = kandidaten[k];
                Bild b = reihe[i], v = reihe[i - 1];
                float bl = Bloecke(v.Grau, b.Grau, maske);
                bericht.AppendLine($"Sprung {k + 1}: Bild {b.Nummer} (t={b.Zeit:0.000}) Mittel unten {v.MittelUnten:0.0}→{b.MittelUnten:0.0}, Blöcke {bl * 100f:0.0} %, Kamera {b.Kamera} Yaw {b.Yaw:0}");
                Einzelbild(Path.Combine(ordner, $"sprung{k + 1}_vorher.png"), v.Grau, null);
                Einzelbild(Path.Combine(ordner, $"sprung{k + 1}_bild.png"), b.Grau, null);
                Einzelbild(Path.Combine(ordner, $"sprung{k + 1}_maske.png"), b.Grau, maske);
            }
            File.WriteAllText(Path.Combine(ordner, "Bericht.txt"), bericht.ToString());
        }

        private static float Sprung(Bild b, Bild v) => Mathf.Abs(b.MittelUnten - v.MittelUnten) + b.BloeckeSpringend * 30f;

        private static float MittelDt(List<Bild> r)
        {
            if (r.Count == 0) return 0f;
            float s = 0f; foreach (Bild b in r) s += b.Dt; return s / r.Count * 1000f;
        }

        private static void Einzelbild(string pfad, byte[] grau, bool[] maske)
        {
            if (maske == null) { Png.Grau(pfad, grau, W, H); return; }
            var rgb = new byte[W * H * 3];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                byte v = grau[y * W + x];
                bool m = maske[Mathf.Min(y / BH, BY - 1) * BX + Mathf.Min(x / BW, BX - 1)];   // 270 ist kein Vielfaches von 8
                int o = (y * W + x) * 3;
                if (m) { rgb[o] = 255; rgb[o + 1] = (byte)(v / 2); rgb[o + 2] = 0; }
                else { rgb[o] = v; rgb[o + 1] = v; rgb[o + 2] = v; }
            }
            Png.Rgb(pfad, rgb, W, H);
        }

        /// <summary>Kleiner PNG-Schreiber (Graustufen 8 Bit oder RGB), damit das Sichern ohne Texture2D im Hintergrund laufen kann.</summary>
        private static class Png
        {
            private static readonly uint[] Crc = Tabelle();

            private static uint[] Tabelle()
            {
                var t = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    t[n] = c;
                }
                return t;
            }

            public static void Grau(string pfad, byte[] daten, int w, int h) => Schreiben(pfad, daten, w, h, 1);
            public static void Rgb(string pfad, byte[] daten, int w, int h) => Schreiben(pfad, daten, w, h, 3);

            private static void Schreiben(string pfad, byte[] daten, int w, int h, int kanaele)
            {
                int zeile = w * kanaele;
                var roh = new byte[(zeile + 1) * h];   // je Zeile ein Filterbyte 0
                for (int y = 0; y < h; y++) Buffer.BlockCopy(daten, y * zeile, roh, y * (zeile + 1) + 1, zeile);
                byte[] idat;
                using (var ms = new MemoryStream())
                {
                    ms.WriteByte(0x78); ms.WriteByte(0x9C);   // zlib-Kopf
                    using (var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionLevel.Fastest, true)) ds.Write(roh, 0, roh.Length);
                    uint a = 1, b = 0;
                    foreach (byte v in roh) { a = (a + v) % 65521; b = (b + a) % 65521; }
                    uint adler = (b << 16) | a;
                    ms.WriteByte((byte)(adler >> 24)); ms.WriteByte((byte)(adler >> 16)); ms.WriteByte((byte)(adler >> 8)); ms.WriteByte((byte)adler);
                    idat = ms.ToArray();
                }
                using (var f = new FileStream(pfad, FileMode.Create, FileAccess.Write))
                {
                    f.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
                    var ihdr = new byte[13];
                    Be(ihdr, 0, (uint)w); Be(ihdr, 4, (uint)h);
                    ihdr[8] = 8; ihdr[9] = (byte)(kanaele == 1 ? 0 : 2); ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
                    Chunk(f, "IHDR", ihdr);
                    Chunk(f, "IDAT", idat);
                    Chunk(f, "IEND", Array.Empty<byte>());
                }
            }

            private static void Chunk(Stream f, string typ, byte[] daten)
            {
                var len = new byte[4]; Be(len, 0, (uint)daten.Length); f.Write(len, 0, 4);
                byte[] t = Encoding.ASCII.GetBytes(typ);
                f.Write(t, 0, 4); f.Write(daten, 0, daten.Length);
                uint c = 0xFFFFFFFFu;
                foreach (byte v in t) c = Crc[(c ^ v) & 0xFF] ^ (c >> 8);
                foreach (byte v in daten) c = Crc[(c ^ v) & 0xFF] ^ (c >> 8);
                c ^= 0xFFFFFFFFu;
                var cb = new byte[4]; Be(cb, 0, c); f.Write(cb, 0, 4);
            }

            private static void Be(byte[] z, int o, uint v) { z[o] = (byte)(v >> 24); z[o + 1] = (byte)(v >> 16); z[o + 2] = (byte)(v >> 8); z[o + 3] = (byte)v; }
        }

        private static string F(float v, string fmt) => v.ToString(fmt, CultureInfo.InvariantCulture);

        private void Freigeben()
        {
            if (_voll != null) { _voll.Release(); _voll = null; }
            if (_mittel != null) { _mittel.Release(); _mittel = null; }
            if (_klein != null) { _klein.Release(); _klein = null; }
        }

        private void OnDestroy() => Freigeben();

        private static void Log(string text) => Debug.Log("[Ashveil] Flacker-Rekorder: " + text);
    }
}
#endif
