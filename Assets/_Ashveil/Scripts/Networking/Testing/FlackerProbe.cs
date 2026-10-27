#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Ashveil.Core;
using Ashveil.Core.Events;
using Ashveil.Core.Settings;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Items;
using Ashveil.UI.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashveil.Networking
{
    /// <summary>
    /// Flacker-Probe (09.10.2026, Spieltest r2.2: „schwarz flackernde Grashalme beim Laufen, alle Schatten flackern beim Laufen
    /// und Umsehen“ – Max). Nimmt im fertigen Spiel Bild für Bild auf und misst, wie viele Pixel zwischen zwei Bildern springen
    /// und im dritten wieder zurück sind (Flackern), getrennt für Stehen, Laufen und Umsehen – und das je Grafikvariante:
    /// normal, Schatten aus, dynamisches GI aus, Gras aus. So steht nach einem Lauf fest, welcher Teil flackert.
    /// Nur Entwicklungs-Build, nur mit „-ashveil-flacker-probe“ (Auftrag „flacker_probe“); im Editor nie.
    ///
    /// Messung: Jedes Bild wird in ein 480×270-Bild verkleinert und als Helligkeit (0–255) gelesen. Ein Pixel flackert, wenn
    /// er sich zum Vorbild um mehr als 48 Stufen ändert, zum Vor-Vorbild aber um weniger als 20 (hin und zurück). Ein Pixel
    /// „fällt schwarz“, wenn er von über 80 auf unter 30 fällt. Je Phase: Anteil im Mittel und im schlimmsten Bild, dazu die
    /// Zahl der Bilder mit mehr als 1 % flackernden Pixeln. Vom schlimmsten Bild jeder Variante werden das Vorbild, das Bild
    /// und die Maske als PNG (960×540) nach Logs/Ashveil/Flacker/ geschrieben. Bericht ebenda (Bericht.txt) und im Unity-Log
    /// („[Ashveil] Flacker-Probe: …“).
    ///
    /// Seit v2 (Max: „es flackert ab und zu auf dem Boden und im Schatten immer noch“): zusätzlich ein Block-Maß – das Bild in
    /// 8×8-Pixel-Blöcke (60×33) geteilt, je Block die mittlere Helligkeit; ein Block „springt“, wenn er sich zum Vorbild um mehr
    /// als 20 Stufen ändert. Viele springende Blöcke auf einmal = ein Schatten oder eine Fläche, die kurz wegbricht (das
    /// Pixel-Maß übersieht das, wenn der Zustand mehrere Bilder bleibt). Varianten seit v2: normal, Schatten Hoch/Mittel/Niedrig/
    /// aus, Terrain grob (Pixelfehler 10), Terrain fein (1), Gras aus, GI klassisch, Kantenglättung aus.
    ///
    /// Seit v3: Puls-Maß – mittlere Helligkeit der unteren Bildhälfte je Bild (Boden und Schatten); ein Puls ist ein Sprung um
    /// mehr als 6 Stufen zum Vorbild, der binnen 6 Bildern wieder zurückgeht (Schatten, der kurz wegbricht). Die Reihe je Phase
    /// steht als CSV (Bild, Mittel unten, springende Blöcke) unter Logs/Ashveil/Flacker/<Variante>_<Phase>.csv.
    ///
    /// Grafikwerte ändert die Probe nur im Speicher (GraphicsSettingsStore.PersistenceEnabled = false): Max' grafik.json bleibt
    /// unberührt; am Ende werden die Ausgangswerte wieder angewendet.
    /// </summary>
    public sealed class FlackerProbe : MonoBehaviour
    {
        public const string Argument = "-ashveil-flacker-probe";

        private const int W = 480, H = 270, BW = 60, BH = 33, BlockSchwelle = 20;
        private const int SchwelleSprung = 48, SchwelleRueck = 20, HellVorher = 80, DunkelDanach = 30;
        private const float StehSekunden = 2.5f, LaufSekunden = 3f, DrehSekunden = 4.5f;   // 4,5 s bei 0,5 ≈ eine volle Drehung

        private GameObject _spieler;
        private int _fehler;
        private RenderTexture _voll, _klein, _mittel;
        private readonly byte[][] _ring = new byte[3][];
        private readonly byte[] _maske = new byte[W * H];
        private int _ringIndex, _ringGefuellt;
        private readonly float[] _blockJetzt = new float[BW * BH], _blockVorher = new float[BW * BH];
        private bool _blockVorhanden;
        private int _pulsStart; private float _pulsWert; private bool _pulsOffen;
        private bool _aufnahme;
        private string _ordner;
        private readonly StringBuilder _bericht = new StringBuilder();

        private sealed class Phase
        {
            public string Name;
            public int Bilder, BilderMit1Prozent;
            public float SummeFlackern, MaxFlackern, SummeSchwarz, MaxSchwarz;
            public byte[] SchlimmVorher, SchlimmBild, SchlimmMaske;
            public float SummeBlock, MaxBlock; public int BilderBlock2Prozent;
            public int Pulse; public float MaxPuls; public readonly List<float> Mittelwerte = new List<float>(); public readonly List<float> Blockreihe = new List<float>();
            public byte[] BlockVorher, BlockBild, BlockMaske;
            public float MittelBlock => Bilder > 0 ? SummeBlock / Bilder : 0f;
            public float Mittel => Bilder > 0 ? SummeFlackern / Bilder : 0f;
            public float MittelSchwarz => Bilder > 0 ? SummeSchwarz / Bilder : 0f;
        }

        private Phase _phase;
        private readonly List<(string variante, Phase p)> _ergebnisse = new List<(string, Phase)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isEditor || ArgWert(Argument) == null) return;
            var go = new GameObject("Flacker-Probe");
            DontDestroyOnLoad(go);
            go.AddComponent<FlackerProbe>();
        }

        private void OnEnable() => EventBus<LocalPlayerSpawnedEvent>.Subscribe(OnSpieler);
        private void OnDisable() => EventBus<LocalPlayerSpawnedEvent>.Unsubscribe(OnSpieler);

        private void OnSpieler(LocalPlayerSpawnedEvent e)
        {
            if (e.Player != null) _spieler = e.Player;
        }

        private IEnumerator Start()
        {
            _ordner = Path.Combine(Application.dataPath, "..", "Logs", "Ashveil", "Flacker");
            try { Directory.CreateDirectory(_ordner); } catch (Exception e) { Fehler("Ordner: " + e.Message); }
            Log("beginnt");
            Bericht($"ASHVEIL Flacker-Probe · {DateTime.Now:yyyy-MM-dd HH:mm:ss} · {Screen.width}×{Screen.height} · {SystemInfo.graphicsDeviceType}");

            float bis = Time.realtimeSinceStartup + 150f;
            while (Time.realtimeSinceStartup < bis && !SpielerBereit()) yield return null;
            if (!SpielerBereit())
            {
                Fehler("keine Spielfigur nach 150 s, Probe bricht ab");
                yield return Ende();
                yield break;
            }
            yield return Warten(6f);
            var input = _spieler.GetComponent<PlayerInputReader>();
            var motion = _spieler.GetComponent<NetworkMotion>();
            if (input == null) Fehler("keine Eingabe an der Spielfigur");

            // Startpunkt: aus dem Argument („x:z:Blick“), sonst 20 m vom Wachtfeuer weg (Gras beginnt außerhalb des Lagers)
            string arg = ArgWert(Argument);
            Vector3 start = new Vector3(0f, 0f, 30f);
            float yaw = 0f;
            if (!string.IsNullOrEmpty(arg))
            {
                string[] w = arg.Split(':');
                if (w.Length >= 2 && float.TryParse(w[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(w[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                {
                    start = new Vector3(x, 0f, z);
                    if (w.Length > 2) float.TryParse(w[2], NumberStyles.Float, CultureInfo.InvariantCulture, out yaw);
                }
            }
            if (motion != null)
            {
                start.y = WorldGround.TrySample(start, out float h) ? h + 0.5f : 60f;
                motion.RequestTeleport(start, yaw);
                yield return Warten(4f);
            }
            Bericht($"Startpunkt {start.x:0.0} / {start.z:0.0}, Blick {yaw:0}°, Spielfigur bei {_spieler.transform.position}");

            // Aufnahme vorbereiten
            _voll = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32) { name = "Flacker voll" };
            _mittel = new RenderTexture(960, 540, 0, RenderTextureFormat.ARGB32) { name = "Flacker mittel" };
            _klein = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32) { name = "Flacker klein" };
            for (int i = 0; i < 3; i++) _ring[i] = new byte[W * H];
            StartCoroutine(Aufnehmen());

            GraphicsSettingsStore.PersistenceEnabled = false;   // nichts davon landet in Max' grafik.json
            GraphicsOptions ausgang = GraphicsSettingsStore.Current.Clone();
            Bericht($"Grafik: Stufe {ausgang.tier}, Schatten {ausgang.shadows}, GI {ausgang.globalIllumination}, Gras {ausgang.vegetation}, Kantenglättung {ausgang.antiAliasing}, Skalierung {ausgang.renderScale}");

            var varianten = new (string name, Action<GraphicsOptions> aendern, float grasWeite)[]
            {
                ("normal", null, -1f),
                ("Schatten aus", o => o.shadows = 0, -1f),
                ("Zusatzlichter Niedrig (ohne Lichtschatten)", o => o.lights = 0, -1f),
                ("Schatten Hoch (2048, 3 Kaskaden)", o => o.shadows = 3, -1f),
                ("Schatten Niedrig (1024, 2, hart)", o => o.shadows = 1, -1f),
                ("GI klassisch", o => o.globalIllumination = 0, -1f),
            };
            float[] grasWeiten = null;
            // v5 (10.10. nachts): „x:z:Blick:s“ – Sonder-Varianten zur Ursachensuche am selben Punkt (Schattenwerte direkt am
            // URP-Asset, Terrain-/Netz-Schatten getrennt abgeschaltet). Der Rekorder fand am Muldentor einen Helligkeitssprung
            // des nahen Bodens beim Umsehen (42→58, 20 Bilder), den die Stufen-Varianten nicht erklären.
            if (arg != null && arg.Split(':').Length > 3 && arg.Split(':')[3].StartsWith("s", StringComparison.Ordinal))
            {
                // „s“ = alle Sonder-Varianten; „s7“ = Variante Nr. 7 als erste (der Helligkeitssprung tritt nur beim ersten
                // Ankommen auf – welche Variante zuerst läuft, entscheidet, was geprüft wird)
                int erste = 0;
                int.TryParse(arg.Split(':')[3].Substring(1), out erste);
                yield return SonderVarianten(input, motion, start, yaw, erste);
                GraphicsSettingsStore.Set(ausgang);
                _aufnahme = false;
                Zusammenfassen();
                Log(_fehler == 0 ? "fertig" : $"fertig mit {_fehler} Fehler(n)");
                yield return Ende();
                yield break;
            }
            foreach (var v in varianten)
            {
                GraphicsOptions o = ausgang.Clone();
                v.aendern?.Invoke(o);
                GraphicsSettingsStore.Set(o);
                if (v.grasWeite >= 0f) grasWeiten = GrasSetzen(v.grasWeite);
                float[] pixelFehler = v.grasWeite == -2f ? PixelfehlerSetzen(1f) : null;
                yield return Warten(2f);   // Grafik umstellen, Bild beruhigen
                yield return Messen(v.name, input, motion, start, yaw);
                if (grasWeiten != null) { GrasZurueck(grasWeiten); grasWeiten = null; }
                if (pixelFehler != null) PixelfehlerZurueck(pixelFehler);
            }
            GraphicsSettingsStore.Set(ausgang);
            _aufnahme = false;

            Zusammenfassen();
            Log(_fehler == 0 ? "fertig" : $"fertig mit {_fehler} Fehler(n)");
            yield return Ende();
        }

        // ================================================================== Sonder-Varianten (Ursachensuche)

        private IEnumerator SonderVarianten(PlayerInputReader input, NetworkMotion motion, Vector3 start, float yaw, int erste)
        {
            var ga = Ashveil.Rendering.GraphicsApplier.Instance;
            if (ga == null) { Fehler("kein GraphicsApplier"); yield break; }
            (float weite, int kaskaden, int aufl) = ga.ShadowRaw;
            Bericht($"Sonder-Varianten: Schattenweite {weite:0} m, Kaskaden {kaskaden}, Auflösung {aufl}");
            var sonder = new (string name, Func<Action> an)[]
            {
                ("normal", () => () => { }),
                ("normal 2 (gleich danach)", () => () => { }),
                ("normal 3 (gleich danach)", () => () => { }),
                ("1 Kaskade", () => { ga.ShadowRawSet(weite, 1, aufl); return () => ga.ShadowRawSet(weite, kaskaden, aufl); }),
                ("2 Kaskaden", () => { ga.ShadowRawSet(weite, 2, aufl); return () => ga.ShadowRawSet(weite, kaskaden, aufl); }),
                ("Schattenweite 120 m", () => { ga.ShadowRawSet(120f, kaskaden, aufl); return () => ga.ShadowRawSet(weite, kaskaden, aufl); }),
                ("Schattenweite 500 m", () => { ga.ShadowRawSet(500f, kaskaden, aufl); return () => ga.ShadowRawSet(weite, kaskaden, aufl); }),
                ("Auflösung 4096", () => { ga.ShadowRawSet(weite, kaskaden, 4096); return () => ga.ShadowRawSet(weite, kaskaden, aufl); }),
                ("Terrain wirft keinen Schatten", () =>
                {
                    var alt = new List<(Terrain, UnityEngine.Rendering.ShadowCastingMode)>();
                    foreach (Terrain t in Terrain.activeTerrains) { alt.Add((t, t.shadowCastingMode)); t.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
                    return () => { foreach (var (t, m) in alt) if (t != null) t.shadowCastingMode = m; };
                }),
                ("Netze werfen keinen Schatten (nur Terrain)", () =>
                {
                    var alt = new List<(Renderer, UnityEngine.Rendering.ShadowCastingMode)>();
                    foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    {
                        if (r is MeshRenderer || r is SkinnedMeshRenderer) { if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) { alt.Add((r, r.shadowCastingMode)); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; } }
                    }
                    Bericht($"  Netze ohne Schatten: {alt.Count}");
                    return () => { foreach (var (r, m) in alt) if (r != null) r.shadowCastingMode = m; };
                }),
                ("GI klassisch (dynamisches GI aus)", () =>
                {
                    bool war = Ashveil.Rendering.GI.AshveilDynamicGI.IsRunning;
                    Ashveil.Rendering.GI.AshveilDynamicGI.SetEnabled(false);
                    Bericht($"  dynamisches GI lief: {war}");
                    return () => { if (war) Ashveil.Rendering.GI.AshveilDynamicGI.SetEnabled(true); };
                }),
                ("Fernansicht aus", () =>
                {
                    var alt = new List<GameObject>();
                    foreach (var p in FindObjectsByType<Ashveil.Rendering.DistantTerrainProxyRoot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) { alt.Add(p.gameObject); p.gameObject.SetActive(false); }
                    Bericht($"  Fernansicht-Wurzeln aus: {alt.Count}");
                    return () => { foreach (var g in alt) if (g != null) g.SetActive(true); };
                }),
            };
            var reihenfolge = new List<(string name, Func<Action> an)>(sonder);
            if (erste > 0 && erste < reihenfolge.Count) { var e = reihenfolge[erste]; reihenfolge.RemoveAt(erste); reihenfolge.Insert(0, e); }
            Bericht("Reihenfolge: " + string.Join(" · ", reihenfolge.ConvertAll(v => v.name)));
            foreach (var v in reihenfolge)
            {
                Action zurueck = null;
                try { zurueck = v.an(); } catch (Exception e) { Fehler($"Variante {v.name}: {e.Message}"); continue; }
                yield return Warten(2f);
                yield return Messen(v.name, input, motion, start, yaw);
                try { zurueck?.Invoke(); } catch (Exception e) { Fehler($"Variante {v.name} zurück: {e.Message}"); }
            }
        }

        // ================================================================== Messen

        private IEnumerator Messen(string variante, PlayerInputReader input, NetworkMotion motion, Vector3 start, float yaw)
        {
            // immer vom selben Punkt aus, damit die Varianten vergleichbar sind
            if (motion != null)
            {
                motion.RequestTeleport(start, yaw);
                yield return Warten(1.5f);
            }
            yield return PhaseMessen(variante, "stehen", null, StehSekunden, input);
            // v4 (10.10. nachts): erst umsehen, dann laufen – so dreht sich die Kamera am Startpunkt selbst (der Rekorder fand den
            // Helligkeitssprung am Muldentor beim Umsehen im Stand; nach dem Laufen stand die Probe woanders), und eine volle Drehung
            yield return PhaseMessen(variante, "umsehen", i => i.InjectTurn(0.5f), DrehSekunden, input);
            if (input != null) input.InjectTurn(0f);
            yield return Warten(0.5f);
            yield return PhaseMessen(variante, "laufen", i => i.InjectMovement(1f, false), LaufSekunden, input);
            if (input != null) input.InjectMovement(0f, false);
            yield return Warten(0.5f);
        }

        private IEnumerator PhaseMessen(string variante, string name, Action<PlayerInputReader> eingabe, float sekunden, PlayerInputReader input)
        {
            _ringGefuellt = 0;
            _blockVorhanden = false;
            _pulsOffen = false;
            _phase = new Phase { Name = name };
            _aufnahme = true;
            float bis = Time.realtimeSinceStartup + sekunden;
            while (Time.realtimeSinceStartup < bis)
            {
                if (eingabe != null && input != null) eingabe(input);
                yield return null;
            }
            _aufnahme = false;
            yield return null;
            Phase p = _phase;
            _phase = null;
            _ergebnisse.Add((variante, p));
            Bericht($"  {variante,-36} {name,-8} Bilder {p.Bilder,4} · flackernd im Mittel {p.Mittel * 100f:0.00} %, schlimmstes Bild {p.MaxFlackern * 100f:0.00} % · " +
                    $"Bilder über 1 %: {p.BilderMit1Prozent} · schwarz fallend im Mittel {p.MittelSchwarz * 100f:0.00} %, schlimmstes {p.MaxSchwarz * 100f:0.00} % · " +
                    $"Blöcke springend im Mittel {p.MittelBlock * 100f:0.00} %, schlimmstes {p.MaxBlock * 100f:0.00} %, Bilder über 2 %: {p.BilderBlock2Prozent}");
            if (p.SchlimmBild != null && p.MaxFlackern >= 0.003f) Bilder(variante, name, p);
            if (p.BlockBild != null && p.MaxBlock >= 0.02f) BlockBilder(variante, name, p);
            Bericht($"  {variante,-36} {name,-8} Puls-Maß: Pulse {p.Pulse}, größter Sprung {p.MaxPuls:0.0} Stufen; Mittel unten {(p.Mittelwerte.Count > 0 ? p.Mittelwerte[0] : 0f):0}→{(p.Mittelwerte.Count > 0 ? p.Mittelwerte[p.Mittelwerte.Count - 1] : 0f):0}");
            try
            {
                var csv = new StringBuilder("Bild;MittelUnten;BloeckeSpringend\n");
                for (int i = 0; i < p.Mittelwerte.Count; i++)
                    csv.Append(i).Append(';').Append(p.Mittelwerte[i].ToString("0.0", CultureInfo.InvariantCulture)).Append(';')
                       .Append((i < p.Blockreihe.Count ? p.Blockreihe[i] * 100f : 0f).ToString("0.00", CultureInfo.InvariantCulture)).Append('\n');
                File.WriteAllText(Path.Combine(_ordner, $"{Sicher(variante)}_{name}.csv"), csv.ToString());
            }
            catch (Exception e) { Fehler("CSV: " + e.Message); }
        }

        /// <summary>Jedes Bild am Bildende lesen; Auswertung gegen die beiden Vorbilder (nur während einer Phase).</summary>
        private IEnumerator Aufnehmen()
        {
            var ende = new WaitForEndOfFrame();
            while (_voll != null)
            {
                yield return ende;
                if (!_aufnahme || _phase == null) continue;
                try
                {
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(_voll);
                    Graphics.Blit(_voll, _mittel);
                    Graphics.Blit(_mittel, _klein);
                }
                catch (Exception e)
                {
                    Fehler("Aufnahme: " + e.Message);
                    _aufnahme = false;
                    continue;
                }
                AsyncGPUReadbackRequest req = AsyncGPUReadback.Request(_klein, 0, TextureFormat.RGBA32);
                req.WaitForCompletion();
                if (req.hasError) { Fehler("Rücklesen fehlgeschlagen"); _aufnahme = false; continue; }
                var daten = req.GetData<byte>();
                byte[] ziel = _ring[_ringIndex];
                for (int i = 0, n = W * H; i < n; i++)
                {
                    int o = i * 4;
                    ziel[i] = (byte)((daten[o] * 54 + daten[o + 1] * 183 + daten[o + 2] * 19) >> 8);
                }
                Auswerten();
                _ringIndex = (_ringIndex + 1) % 3;
                if (_ringGefuellt < 3) _ringGefuellt++;
            }
        }

        private void Auswerten()
        {
            Phase p = _phase;
            if (p == null) return;
            if (_ringGefuellt < 2) return;
            byte[] jetzt = _ring[_ringIndex];
            byte[] vorher = _ring[(_ringIndex + 2) % 3];
            byte[] vorvor = _ringGefuellt >= 3 ? _ring[(_ringIndex + 1) % 3] : null;
            int flackern = 0, schwarz = 0, n = W * H;
            for (int i = 0; i < n; i++)
            {
                int d1 = jetzt[i] - vorher[i];
                int a1 = d1 < 0 ? -d1 : d1;
                bool f = false;
                if (a1 > SchwelleSprung)
                {
                    if (vorvor != null)
                    {
                        int d2 = jetzt[i] - vorvor[i];
                        if ((d2 < 0 ? -d2 : d2) < SchwelleRueck) f = true;
                    }
                    if (vorher[i] > HellVorher && jetzt[i] < DunkelDanach) schwarz++;
                }
                _maske[i] = f ? (byte)255 : (byte)0;
                if (f) flackern++;
            }
            // Block-Maß (8×8 Pixel): springende Blöcke zum Vorbild
            for (int by = 0; by < BH; by++)
            for (int bx = 0; bx < BW; bx++)
            {
                int sum = 0;
                for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++) sum += jetzt[(by * 8 + y) * W + bx * 8 + x];
                _blockJetzt[by * BW + bx] = sum / 64f;
            }
            int bloecke = 0;
            if (_blockVorhanden)
            {
                for (int i = 0; i < BW * BH; i++)
                {
                    float d = _blockJetzt[i] - _blockVorher[i];
                    if (d > BlockSchwelle || d < -BlockSchwelle) bloecke++;
                }
                float anteilBlock = (float)bloecke / (BW * BH);
                p.SummeBlock += anteilBlock;
                if (anteilBlock > 0.02f) p.BilderBlock2Prozent++;
                if (anteilBlock > p.MaxBlock)
                {
                    p.MaxBlock = anteilBlock;
                    p.BlockVorher = (byte[])vorher.Clone();
                    p.BlockBild = (byte[])jetzt.Clone();
                    var bm = new byte[W * H];
                    for (int i = 0; i < BW * BH; i++)
                    {
                        float d = _blockJetzt[i] - _blockVorher[i];
                        if (!(d > BlockSchwelle || d < -BlockSchwelle)) continue;
                        int bx = i % BW, by = i / BW;
                        for (int y = 0; y < 8; y++)
                        for (int x = 0; x < 8; x++) bm[(by * 8 + y) * W + bx * 8 + x] = 255;
                    }
                    p.BlockMaske = bm;
                }
            }
            Array.Copy(_blockJetzt, _blockVorher, BW * BH);
            _blockVorhanden = true;
            p.Blockreihe.Add(_blockVorhanden ? (float)bloecke / (BW * BH) : 0f);

            // Puls-Maß: mittlere Helligkeit der unteren Bildhälfte
            long summeUnten = 0;
            for (int i = (H / 2) * W; i < n; i++) summeUnten += jetzt[i];
            float mittel = summeUnten / (float)(n - (H / 2) * W);
            p.Mittelwerte.Add(mittel);
            int m = p.Mittelwerte.Count;
            if (m >= 2)
            {
                float d = mittel - p.Mittelwerte[m - 2];
                float ad = d < 0 ? -d : d;
                if (ad > 6f)
                {
                    // zurück binnen 6 Bildern? (nachträglich prüfen: das letzte Ereignis merken)
                    _pulsStart = m - 2; _pulsWert = p.Mittelwerte[m - 2]; _pulsOffen = true;
                    if (ad > p.MaxPuls) p.MaxPuls = ad;
                }
                else if (_pulsOffen && m - 1 - _pulsStart <= 6)
                {
                    float r = mittel - _pulsWert;
                    if ((r < 0 ? -r : r) < 3f) { p.Pulse++; _pulsOffen = false; }
                }
                else if (_pulsOffen && m - 1 - _pulsStart > 6) _pulsOffen = false;
            }

            float anteil = (float)flackern / n, anteilSchwarz = (float)schwarz / n;
            p.Bilder++;
            p.SummeFlackern += anteil;
            p.SummeSchwarz += anteilSchwarz;
            if (anteil > 0.01f) p.BilderMit1Prozent++;
            if (anteilSchwarz > p.MaxSchwarz) p.MaxSchwarz = anteilSchwarz;
            if (anteil > p.MaxFlackern)
            {
                p.MaxFlackern = anteil;
                p.SchlimmVorher = (byte[])vorher.Clone();
                p.SchlimmBild = (byte[])jetzt.Clone();
                p.SchlimmMaske = (byte[])_maske.Clone();
            }
        }

        // ================================================================== Bilder und Bericht

        private void Bilder(string variante, string phase, Phase p)
        {
            string stamm = Path.Combine(_ordner, $"{Sicher(variante)}_{phase}");
            try
            {
                Schreiben(stamm + "_vorher.png", p.SchlimmVorher, false);
                Schreiben(stamm + "_bild.png", p.SchlimmBild, false);
                Schreiben(stamm + "_maske.png", p.SchlimmMaske, true);
            }
            catch (Exception e) { Fehler("PNG: " + e.Message); }
        }

        private void BlockBilder(string variante, string phase, Phase p)
        {
            string stamm = Path.Combine(_ordner, $"{Sicher(variante)}_{phase}_block");
            try
            {
                Schreiben(stamm + "_vorher.png", p.BlockVorher, false);
                Schreiben(stamm + "_bild.png", p.BlockBild, false);
                Schreiben(stamm + "_maske.png", p.BlockMaske, true);
            }
            catch (Exception e) { Fehler("PNG: " + e.Message); }
        }

        private static float[] PixelfehlerSetzen(float wert)
        {
            Terrain[] ts = Terrain.activeTerrains;
            var alt = new float[ts.Length];
            for (int i = 0; i < ts.Length; i++)
            {
                if (ts[i] == null) continue;
                alt[i] = ts[i].heightmapPixelError;
                ts[i].heightmapPixelError = wert;
            }
            return alt;
        }

        private static void PixelfehlerZurueck(float[] alt)
        {
            Terrain[] ts = Terrain.activeTerrains;
            for (int i = 0; i < ts.Length && i < alt.Length; i++)
                if (ts[i] != null) ts[i].heightmapPixelError = alt[i];
        }

        private static void Schreiben(string pfad, byte[] hell, bool maske)
        {
            var t = new Texture2D(W, H, TextureFormat.RGB24, false);
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                byte v = hell[y * W + x];
                px[y * W + x] = maske ? (v > 0 ? new Color32(255, 40, 40, 255) : new Color32(0, 0, 0, 255)) : new Color32(v, v, v, 255);
            }
            t.SetPixels32(px);
            t.Apply();
            File.WriteAllBytes(pfad, t.EncodeToPNG());
            Destroy(t);
        }

        private void Zusammenfassen()
        {
            Bericht("");
            Bericht("=== Zusammenfassung (flackernde Pixel im Mittel je Phase, in %) ===");
            Bericht($"  {"Variante",-36} {"stehen",8} {"laufen",8} {"umsehen",8}   schwarz fallend (laufen, schlimmstes) · Blöcke springend laufen/umsehen (Mittel, schlimmstes, Bilder über 2 %)");
            var namen = new List<string>();
            foreach (var e in _ergebnisse) if (!namen.Contains(e.variante)) namen.Add(e.variante);
            foreach (string v in namen)
            {
                Phase s = null, l = null, u = null;
                foreach (var e in _ergebnisse)
                    if (e.variante == v) { if (e.p.Name == "stehen") s = e.p; else if (e.p.Name == "laufen") l = e.p; else u = e.p; }
                Bericht($"  {v,-36} {Pz(s),8} {Pz(l),8} {Pz(u),8}   {(l != null ? (l.MaxSchwarz * 100f).ToString("0.00", CultureInfo.InvariantCulture) : "-")} · {Bz(l)} / {Bz(u)}");
            }
            Bericht("Lesart: Verschwindet das Flackern beim Laufen mit „Schatten aus“, liegt es an den Schatten; mit „Gras aus“ am Gras; mit „GI klassisch“ am dynamischen GI.");
            try { File.WriteAllText(Path.Combine(_ordner, "Bericht.txt"), _bericht.ToString()); }
            catch (Exception e) { Fehler("Bericht: " + e.Message); }
        }

        private static string Bz(Phase p) => p == null ? "-" : $"{p.MittelBlock * 100f:0.00} {p.MaxBlock * 100f:0.00} {p.BilderBlock2Prozent}";

        private static string Pz(Phase p) => p == null ? "-" : (p.Mittel * 100f).ToString("0.00", CultureInfo.InvariantCulture);

        private void Bericht(string zeile)
        {
            _bericht.AppendLine(zeile);
            Debug.Log("[Ashveil] Flacker-Probe: " + zeile);
        }

        // ================================================================== Gras

        private static float[] GrasSetzen(float weite)
        {
            Terrain[] ts = Terrain.activeTerrains;
            var alt = new float[ts.Length];
            for (int i = 0; i < ts.Length; i++)
            {
                if (ts[i] == null) continue;
                alt[i] = ts[i].detailObjectDistance;
                ts[i].detailObjectDistance = weite;
            }
            return alt;
        }

        private static void GrasZurueck(float[] alt)
        {
            Terrain[] ts = Terrain.activeTerrains;
            for (int i = 0; i < ts.Length && i < alt.Length; i++)
                if (ts[i] != null) ts[i].detailObjectDistance = alt[i];
        }

        // ================================================================== Hilfen

        private IEnumerator Ende()
        {
            _aufnahme = false;
            yield return null;
            if (_voll != null) { _voll.Release(); _voll = null; }
            if (_mittel != null) { _mittel.Release(); _mittel = null; }
            if (_klein != null) { _klein.Release(); _klein = null; }
            yield return Warten(1f);
            Application.Quit();
        }

        private bool SpielerBereit() => _spieler != null && InventoryClient.Current != null && InventoryClient.Current.Ready;

        private static IEnumerator Warten(float sekunden)
        {
            float bis = Time.realtimeSinceStartup + sekunden;
            while (Time.realtimeSinceStartup < bis) yield return null;
        }

        private void Fehler(string text)
        {
            _fehler++;
            Bericht("FEHLER – " + text);
        }

        private static void Log(string text) => Debug.Log("[Ashveil] Flacker-Probe: " + text);

        private static string Sicher(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }

        private static string ArgWert(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) continue;
                // Ein Wert mit Doppelpunkt („-196:235:120“) ist ein Startpunkt, kein neues Argument (10.10.: Muldenrand liegt bei x < 0)
                if (i + 1 >= args.Length) return string.Empty;
                string w = args[i + 1];
                return !w.StartsWith("-", StringComparison.Ordinal) || w.Contains(":") ? w : string.Empty;
            }
            return null;
        }
    }
}
#endif
