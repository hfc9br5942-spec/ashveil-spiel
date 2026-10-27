#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Ashveil.Core.Diagnostics;
using Ashveil.Core.Platform;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using Debug = UnityEngine.Debug;

namespace Ashveil.UI.Diagnostics
{
    /// <summary>
    /// Bild-Spitzen-Messung v2 (Ruckler, CORE-005; 09.10.2026 nach dem Spieltest r2). Nur Editor und Entwicklungs-Build.
    ///
    /// Warum v2: Die erste Fassung las die Messpunkte im Update des Folgebildes und fand viele Abschnitte der Hauptschleife gar
    /// nicht (sie werden erst beim ersten Gebrauch angemeldet). Im r2-Log standen deshalb Hänger von 400 ms ohne Ursache.
    ///
    /// Was v2 misst:
    /// - Eigene Stoppuhr an vier Stellen jeder Spielschleife (Player Loop): ganz am Anfang, am Ende des Zeit-Abschnitts (TimeUpdate:
    ///   Warten auf die Anzeige des vorigen Bildes, Bildraten-Grenze), nach dem Profiler-Bildstart (ProfilerStartFrame) und am Ende.
    ///   Daraus: Warten auf Grafikkarte/Anzeige, Zeit in der Schleife und Zeit dazwischen (Windows-Nachrichten). So zeigt sich, wo
    ///   ein Hänger liegt.
    /// - Messpunkte (ProfilerRecorder): im fertigen Spiel alle Zeit-Messpunkte (≤ 4 000; die Probe vom 09.10. zeigte keinen
    ///   messbaren Aufwand), im Editor die bekannten Verdächtigen (≤ 400). Später angemeldete Messpunkte werden nach 2, 10 und 30 s
    ///   und danach jede Minute nachgeladen. Zu jedem Bild ab 50 ms zwei Schnappschüsse (gleich danach und ein Bild später), der
    ///   Abgleich über „PlayerLoop“ und ein Filter gegen über alle Fäden summierte Werte (Leerlauf, Auftragsverwaltung).
    ///   v2.3: nach einem langen Warten am Bildanfang ein dritter Schnappschuss Z – das Warten zählt der Profiler noch zum vorigen
    ///   Profiler-Bild, A und B zeigen es darum nicht. v2.4: Grafikspeicher, den Unity beim Treiber angelegt hat (je Hänger und in
    ///   der Zusammenfassung).
    /// - v2.5 (nach Max' r2.1-Sitzungen: der Hauptfaden stand etwa einmal je Sekunde 50–460 ms zwischen dem Zeit-Abschnitt und dem
    ///   Profiler-Bildstart, wo Unity keine Messpunkte hat): ein fünfter Haken am Anfang von Initialization trennt den Übergang vom
    ///   Profiler-Bildstart; je Bild die Rechenzeit des Hauptfadens (rechnet er oder wartet er?) und die Zahl der
    ///   Speicherbereinigungen; im fertigen Spiel unter Windows Stapelproben während eines Hängers am Bildanfang
    ///   (<see cref="BildanfangWache"/>); im Kopf der Sitzungsdatei der Aufbau des Schleifenanfangs.
    /// - v2.6 (die Stapelproben zeigten: der Hauptfaden wartet beim Verarbeiten der Maus-Nachrichten in Windows): Zustand des
    ///   Mauszeigers (frei, für die Kamera gesperrt, unsichtbar) je Hänger, als Notiz bei jedem Wechsel und als Rate je Zustand
    ///   in der Zusammenfassung; zu jeder Stapelprobe ein Systemblick (Wartegrund, aktive Fäden und Programme); im Kopf die
    ///   geladenen Programmteile nach Herkunft.
    /// - v2.7 (Max 09.10.2026 abends: Mausfilter fest einbauen): Die Fenster-Nachrichten zählt jetzt der Laufzeit-Baustein
    ///   <see cref="Mausfilter"/> (Ashveil.Core.Platform; der Filter selbst ist in jedem Build an). Die Messung liest je Bild seine
    ///   Zähler und schreibt in die Zusammenfassung, was er zurückgehalten, nachgereicht und selbst beantwortet hat.
    /// - Bild-Zeiten von Unity (FrameTimingManager): Hauptfaden, Grafikfaden, GPU.
    /// - Nur im fertigen Spiel: Aufzeichnung der Grafik-Zustände (GraphicsStateCollection, „PSO“) ab dem Laden der ersten Szene,
    ///   je Bild die neu entstandenen Zustände; am Ende gespeichert. Das ist die Grundlage, um Shader beim Laden vorzuwärmen.
    /// - Fensterwechsel (Fokus) und Abschnitte der Ruckler-Probe.
    ///
    /// Ausgabe: im fertigen Spiel je Sitzung Logs/Ashveil/Ruckler/Sitzung_&lt;Zeit&gt;.txt (mit Zusammenfassung am Ende) und die
    /// Grafik-Zustände daneben; jede Minute ein Zwischenstand Sitzung_&lt;Zeit&gt;_Stand.txt (falls das Spiel ohne Abschluss endet,
    /// etwa über das Fenster). Im Editor wie bisher Logs/Ashveil/Spitzen_Ursachen.txt.
    /// </summary>
    public sealed class FrameSpikeProfiler : MonoBehaviour
    {
        /// <summary>Ruckler-Probe des Auftrags „ruckler_messung“: zusätzlich die Liste aller Messpunkte schreiben.</summary>
        public const string VollArgument = "-ashveil-ruckler-voll";
        /// <summary>
        /// 11.10.2026 (Lasttest r2.16): Ohne Grafik (dedizierter Server, Lasttest-Bots mit -nographics, batchmode) läuft die Messung
        /// nicht – 51 Prozesse mit je 2 500 Messpunkten, Stapel-Wache (hält den Hauptfaden alle 2 ms an) und Grafik-Aufzeichnung
        /// verfälschten den Lasttest und ließen das Beenden hängen. Nur mit diesem Startargument auch dort (Diagnose).
        /// </summary>
        public const string OhneGrafikArgument = "-ashveil-ruckler-auch-ohne-grafik";
        private static bool OhneGrafik =>
            (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) && !HatArgument(OhneGrafikArgument);

        private const int R = 64;
        private const long SchnappMinNs = 2_000_000;   // 2 ms
        private const int SchnappMax = 40;
        private const int ErsteBilder = 30;

        private struct AshveilBildAnfang { }
        private struct AshveilZeitEnde { }
        private struct AshveilInitAnfang { }
        private struct AshveilBildStart { }
        private struct AshveilBildEnde { }

        private sealed class Punkt
        {
            public string Name;
            public ProfilerRecorder Rec;
        }

        // Bekannte Verdächtige (normaler Modus): genaue Namen …
        private static readonly string[] Namen =
        {
            "PlayerLoop",
            "EarlyUpdate.UpdatePreloading",
            "Loading.AwakeFromLoad",
            "Loading.ReadObject",
            "Application.Integrate Assets in Background",
            "Application.IntegrateAssetsInBackground",
            "Preload Single Step",
            "GC.Collect",
            "Shader.CreateGPUProgram",
            "Update.ScriptRunBehaviourUpdate",
            "PreLateUpdate.ScriptRunBehaviourLateUpdate",
            "FixedUpdate.ScriptRunBehaviourFixedUpdate",
            "Update.ScriptRunDelayedDynamicFrameRate",
            "FixedUpdate.PhysicsFixedUpdate",
            "PostLateUpdate.FinishFrameRendering",
            "PostLateUpdate.UpdateAllRenderers",
            "PostLateUpdate.UpdateAllSkinnedMeshes",
            "PostLateUpdate.PlayerUpdateCanvases",
            "PostLateUpdate.PlayerEmitCanvasGeometry",
            "PostLateUpdate.PresentAfterDraw",
            "PreLateUpdate.DirectorUpdateAnimationBegin",
            "PreLateUpdate.DirectorUpdateAnimationEnd",
            "Gfx.WaitForPresentOnGfxThread",
            "Gfx.WaitForRenderThread",
            "Gfx.WaitForGfxCommandsFromMainThread",
            "Gfx.WaitForCommands",
            "Gfx.PresentFrame",
            "Gfx.ProcessCommands",
            "Mesh.UploadMeshData",
            "Texture.AwakeFromLoad",
            "Mesh.AwakeFromLoad",
            "Instantiate",
            "Destroy",
        };

        // … und Namensteile
        private static readonly string[] Teile =
        {
            "Load", "Awake", "Integrate", "Scene", "Instantiate", "Shader", "GC.", "NavMesh", "Bake", "Terrain", "ScriptRun",
            "Gfx.", "Upload", "Collider", "Network", "FishNet", "Spawn", "Lightmap", "Reflection", "Probe", "GPUProgram", "Pipeline",
            "PSO", "Canvas", "Layout", "TMP", "Font", "Glyph", "Texture", "Present", "Update()", "Coroutine", "[Invoke]", "Audio",
            "Input", "Cursor", "Initialization.", "EarlyUpdate.", "FixedUpdate.", "PreUpdate.", "Update.", "PreLateUpdate.", "PostLateUpdate."
        };

        private static FrameSpikeProfiler _inst;

#if !UNITY_EDITOR
        private static UnityEngine.Rendering.GraphicsStateCollection _gsc;
#endif
        private static bool _gscAn;
        private static string _gscStatus = "aus (nur im fertigen Spiel)";

        // eigene Stoppuhr je Bild (Ring über die letzten R Bilder; Index = laufende Nummer modulo R)
        private long _n;
        private long _nBeimUpdate = -1;
        private int _ohneStart;
        private readonly long[] _anfang = new long[R], _start = new long[R], _ende = new long[R];
        private long _anfangTick;
        // Ende des Zeit-Abschnitts (TimeUpdate: Warten auf das vorige Bild, Bildraten-Grenze); 0 = nicht gemessen
        private readonly long[] _zeitEnde = new long[R];
        private long _zeitEndeTick;
        // v2.5: Anfang von Initialization (vor dem Profiler-Bildstart); 0 = nicht gemessen
        private readonly long[] _initAnfang = new long[R];
        private long _initTick;
        // v2.5: Rechenzeit des Hauptfadens in Takten am Bildanfang, am Ende des Zeit-Abschnitts und nach dem Profiler-Bildstart
        // (0 = unbekannt) und die Zahl der Speicherbereinigungen am Bildanfang und nach dem Profiler-Bildstart
        private readonly long[] _zyk0 = new long[R], _zykZ = new long[R], _zyk1 = new long[R];
        private readonly int[] _gc0 = new int[R], _gc1 = new int[R];
        private long _zyk0Tick, _zykZTick;
        private int _gc0Tick;
        // v2.5: Hänger am Bildanfang nach dem Zeit-Abschnitt (ab 25 ms), eingeteilt nach der Rechenzeit des Hauptfadens
        private const double AnfangHaengerMs = 25.0;
        private int _anfangHaenger, _anfangRechnet, _anfangWartet, _anfangGemischt, _anfangGc;
        private double _anfangHaengerMs;
        // v2.6: Mauszeiger je Bild (0 frei, 1 für die Kamera gesperrt, 2 unsichtbar), Hänger am Bildanfang und Zeit je Zustand
        private static readonly string[] ZeigerTexte = { "frei", "gesperrt (Kamera drehen)", "unsichtbar" };
        private readonly int[] _zeiger = new int[R];
        private int _zeigerJetzt = -1;
        // v2.6: Anzeige (Modus, Auflösung, VSync, Bildraten-Grenze) – Notiz bei jeder Änderung
        private string _anzeigeZuletzt;
        // v2.6: Fenster-Nachrichten je Bild und gesamt (Arten: Mausfilter.NachrichtenArten; seit v2.7 zählt der Mausfilter)
        private readonly int[] _nachrJetzt = new int[6];
        private readonly int[,] _nachr = new int[R, 6];
        private readonly long[] _nachrSumme = new long[6];
        private double _nachrSek;
        private readonly int[] _anfangJeZeiger = new int[3];
        private readonly double[] _sekJeZeiger = new double[3];
        private readonly int[] _frame = new int[R];
        private readonly int[] _zust = new int[R], _var = new int[R];
        private readonly double[] _pl = new double[R];
        private readonly Dictionary<long, List<KeyValuePair<string, double>>> _schnapp = new Dictionary<long, List<KeyValuePair<string, double>>>();
        // Z: gelesen gleich nach einem langen Warten am Bildanfang. Das Warten (TimeUpdate) zählt der Profiler noch zum
        // vorigen Profiler-Bild – A und B zeigen es darum nicht.
        private readonly Dictionary<long, List<KeyValuePair<string, double>>> _schnappAnzeige = new Dictionary<long, List<KeyValuePair<string, double>>>();
        private const double AnzeigeSchnappMs = 40.0;
        private readonly HashSet<long> _schnappFaellig = new HashSet<long>();
        private readonly List<Offen> _offen = new List<Offen>();

        private struct Offen
        {
            public long Bild, Ab;
            public SpitzenStatistik.Teil Teil;
        }

        private readonly List<Punkt> _punkte = new List<Punkt>();
        private readonly HashSet<string> _bekannt = new HashSet<string>();
        private Punkt _playerLoop;
        private bool _voll, _liste;
        private int _obergrenze;
        private int _scans;
        private float _naechsterScan;

        private readonly FrameTiming[] _timings = new FrameTiming[16];
        private bool _frameTiming;

        private SpitzenStatistik _stat;
        private string _datei;
        private string _stempel;
        private double _fokusZeit = -100.0;
        private int _fokusWechsel;
        private bool _abgeschlossen;
        private float _naechsterStand = 60f;
        // Grafikspeicher, den Unity beim Treiber angelegt hat (nur Editor und Entwicklungs-Build), jede Sekunde gelesen. v2.4:
        // Die Hänger am Bildanfang warten auf die Grafikkarte; ist der Grafikspeicher knapp (Editor und Spiel auf derselben Karte),
        // lagert Windows aus und die Karte steht.
        private long _gfxAnfang = -1, _gfxMax, _gfxJetzt;
        private float _naechsterSpeicher;
#if !UNITY_EDITOR
        private double _zustLesenUs;
        private long _zustLesenN;
#endif

        private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        private static double Sek(long ticks) => ticks / (double)Stopwatch.Frequency;
        private static int Idx(long n) => (int)(((n % R) + R) % R);
        private static string F0(double v) => v.ToString("0", CultureInfo.InvariantCulture);

        // ================================================================== Start

        /// <summary>Grafik-Zustände so früh wie möglich aufzeichnen (vor dem Laden der ersten Szene).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void GrafikAufzeichnungStarten()
        {
#if !UNITY_EDITOR
            if (_gsc != null || OhneGrafik) return;
            try
            {
                _gsc = new UnityEngine.Rendering.GraphicsStateCollection();
                _gscAn = _gsc.BeginTrace();
                _gscStatus = _gscAn ? "Aufzeichnung an" : "Aufzeichnung nicht gestartet";
            }
            catch (Exception e)
            {
                _gsc = null;
                _gscAn = false;
                _gscStatus = "nicht verfügbar (" + e.GetType().Name + ")";
            }
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (OhneGrafik) return;
            var go = new GameObject("Bild-Spitzen-Messung");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<FrameSpikeProfiler>();
        }

        private void OnEnable()
        {
            _inst = this;
            _voll = !Application.isEditor;
            _liste = _voll && HatArgument(VollArgument);
            _obergrenze = Application.isEditor ? 400 : _voll ? 4000 : 1500;
            _frameTiming = FrameTimingManager.IsFeatureEnabled();
            _stat = new SpitzenStatistik("Start");
            Scannen();
            _naechsterScan = Time.realtimeSinceStartup + 2f;
            DateiAnlegen();
            BildanfangWache.Starten(HatArgument(BildanfangWache.AusArgument));
            HakenSetzen();
            Notiz("Spielschleife (Anfang): " + SchleifenAufbau());
            Notiz("Stapelproben am Bildanfang: " + BildanfangWache.Status + " · Rechenzeit des Hauptfadens: " +
                  (BildanfangWache.ZyklenJeMs > 0.0 ? "an (" + (BildanfangWache.ZyklenJeMs / 1000.0).ToString("0", CultureInfo.InvariantCulture) + " Mio. Takte je s)" : "aus"));
            string teile = BildanfangWache.ProgrammteileText();
            if (teile != null) Notiz("Programmteile im Spiel: " + teile);
            Notiz("Mausfilter: " + Mausfilter.Status + " · Fenster-Nachrichten zählen: " + (Mausfilter.Zaehlt ? "an" : "aus"));
            Debug.Log($"[Ashveil] Bild-Spitzen-Messung v2: {_punkte.Count} Messpunkte ({(_voll ? "voll" : "normal")}), " +
                      $"Bild-Zeiten {(_frameTiming ? "an" : "aus")}, Grafik-Zustände {_gscStatus}, Stapelproben {BildanfangWache.Status} → {_datei}");
        }

        private void OnDisable()
        {
            Abschliessen();
            HakenEntfernen();
            BildanfangWache.Beenden();
            foreach (Punkt p in _punkte) p.Rec.Dispose();
            _punkte.Clear();
            _bekannt.Clear();
            _playerLoop = null;
            if (_inst == this) _inst = null;
        }

        private void OnApplicationQuit()
        {
            Abschliessen();
        }

        private void OnApplicationFocus(bool aktiv)
        {
            if (_stat == null) return;
            _fokusWechsel++;
            _fokusZeit = Sek(Stopwatch.GetTimestamp());
            Notiz(aktiv ? "Fenster aktiv" : "Fenster nicht mehr aktiv");
        }

        /// <summary>Abschnitt der Ruckler-Probe (oder eines Tests): folgende Bilder und Hänger zählen dort.</summary>
        public static void Abschnitt(string name)
        {
            if (_inst == null || _inst._stat == null) return;
            _inst._stat.Abschnitt(name);
            _inst.Notiz("--- Abschnitt: " + name);
        }

        /// <summary>Freie Notiz mit Uhrzeit in die Sitzungsdatei (z. B. Meldungen der Probe).</summary>
        public static void Vermerk(string text)
        {
            if (_inst != null) _inst.Notiz(text);
        }

        private void Update()
        {
            // Die Haken laufen nicht mehr (jemand hat die Spielschleife neu gesetzt)? Dann neu setzen.
            if (_n == _nBeimUpdate)
            {
                if (++_ohneStart >= 3)
                {
                    _ohneStart = 0;
                    HakenSetzen();
                    Notiz("Haken in der Spielschleife neu gesetzt");
                }
            }
            else _ohneStart = 0;
            _nBeimUpdate = _n;

            int zeiger = ZeigerZustand();
            if (zeiger != _zeigerJetzt)
            {
                _zeigerJetzt = zeiger;
                Notiz("Mauszeiger: " + ZeigerTexte[zeiger]);
            }

            if (Time.realtimeSinceStartup >= _naechsterScan)
            {
                Scannen();
                _naechsterScan = Time.realtimeSinceStartup + (_scans <= 2 ? 8f : _scans == 3 ? 20f : 60f);
            }
            if (Time.realtimeSinceStartup >= _naechsterSpeicher)
            {
                _naechsterSpeicher = Time.realtimeSinceStartup + 1f;
                SpeicherLesen();
                string anzeige = Screen.fullScreenMode + " " + Screen.width + "×" + Screen.height + " · VSync " + QualitySettings.vSyncCount +
                                 " · Ziel " + Application.targetFrameRate + " Bilder/s";
                if (anzeige != _anzeigeZuletzt)
                {
                    _anzeigeZuletzt = anzeige;
                    Notiz("Anzeige: " + anzeige);
                }
            }
            if (!Application.isEditor && !_abgeschlossen && Time.realtimeSinceStartup >= _naechsterStand)
            {
                _naechsterStand = Time.realtimeSinceStartup + 60f;
                ZwischenstandSchreiben();
            }
        }

        /// <summary>v2.6: 0 frei und sichtbar, 1 für die Kamera gesperrt, 2 frei, aber unsichtbar.</summary>
        private static int ZeigerZustand()
        {
            if (Cursor.lockState == CursorLockMode.Locked) return 1;
            return Cursor.visible ? 0 : 2;
        }

        private void SpeicherLesen()
        {
            long g = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver();
            if (g <= 0) return;
            _gfxJetzt = g;
            if (_gfxAnfang < 0) _gfxAnfang = g;
            if (g > _gfxMax) _gfxMax = g;
        }

        private static string Mb(long bytes) => (bytes / (1024L * 1024L)).ToString("0", CultureInfo.InvariantCulture);

        private string SpeicherText() =>
            _gfxAnfang < 0 ? null
                : $"am Anfang {Mb(_gfxAnfang)} MB · höchstens {Mb(_gfxMax)} MB · zuletzt {Mb(_gfxJetzt)} MB · Grafikkarte {SystemInfo.graphicsMemorySize} MB";

        /// <summary>Zusammenfassung bis jetzt in eine eigene Datei (wird jede Minute überschrieben).</summary>
        private void ZwischenstandSchreiben()
        {
            try
            {
                string pfad = Path.Combine(Ordner, $"Sitzung_{_stempel}_Stand.txt");
                Directory.CreateDirectory(Ordner);
                File.WriteAllText(pfad, $"Zwischenstand {DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} (Sitzung {_stempel}, Modus {(_voll ? "voll" : "normal")})" +
                                        Environment.NewLine + _stat.Text(_gscAn) + AnfangText());
            }
            catch (Exception) { }
        }

        // ================================================================== Haken in der Spielschleife

        private static void BildAnfang()
        {
            FrameSpikeProfiler p = _inst;
            if (p == null) return;
            long t = Stopwatch.GetTimestamp();
            p._anfangTick = t;
            p._zyk0Tick = BildanfangWache.Zyklen();
            p._gc0Tick = GC.CollectionCount(0);
            BildanfangWache.FensterAuf(p._n + 1, t, p._zyk0Tick);
        }

        private static void ZeitEnde()
        {
            FrameSpikeProfiler p = _inst;
            if (p == null) return;
            p._zeitEndeTick = Stopwatch.GetTimestamp();
            p._zykZTick = BildanfangWache.Zyklen();
            BildanfangWache.Phase(2);
        }

        private static void InitAnfang()
        {
            FrameSpikeProfiler p = _inst;
            if (p == null) return;
            p._initTick = Stopwatch.GetTimestamp();
            BildanfangWache.Phase(3);
        }

        private static void BildStart()
        {
            if (_inst == null) return;
            try { _inst.AmStart(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private static void BildEnde()
        {
            if (_inst == null) return;
            try { _inst.AmEnde(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private static void HakenSetzen()
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            Entfernen(ref loop);
            PlayerLoopSystem[] oben = loop.subSystemList;
            if (oben == null || oben.Length == 0) return;
            // ganz am Anfang des ersten Abschnitts (TimeUpdate): vor dem Warten auf die Anzeige des vorigen Bildes
            var erste = new List<PlayerLoopSystem>(oben[0].subSystemList ?? new PlayerLoopSystem[0]);
            erste.Insert(0, new PlayerLoopSystem { type = typeof(AshveilBildAnfang), updateDelegate = BildAnfang });
            // … und an seinem Ende: trennt das Warten selbst vom Profiler-Bildstart danach
            erste.Add(new PlayerLoopSystem { type = typeof(AshveilZeitEnde), updateDelegate = ZeitEnde });
            oben[0].subSystemList = erste.ToArray();
            for (int i = 0; i < oben.Length; i++)
            {
                if (oben[i].type == typeof(Initialization))
                {
                    var subs = new List<PlayerLoopSystem>(oben[i].subSystemList ?? new PlayerLoopSystem[0]);
                    int at = 0;
                    for (int k = 0; k < subs.Count; k++)
                        if (subs[k].type != null && subs[k].type.Name == "ProfilerStartFrame") { at = k + 1; break; }
                    subs.Insert(at, new PlayerLoopSystem { type = typeof(AshveilBildStart), updateDelegate = BildStart });
                    // v2.5: ganz am Anfang von Initialization (trennt den Übergang vom Profiler-Bildstart)
                    if (at > 0) subs.Insert(0, new PlayerLoopSystem { type = typeof(AshveilInitAnfang), updateDelegate = InitAnfang });
                    oben[i].subSystemList = subs.ToArray();
                }
                else if (oben[i].type == typeof(PostLateUpdate))
                {
                    var subs = new List<PlayerLoopSystem>(oben[i].subSystemList ?? new PlayerLoopSystem[0]);
                    subs.Add(new PlayerLoopSystem { type = typeof(AshveilBildEnde), updateDelegate = BildEnde });
                    oben[i].subSystemList = subs.ToArray();
                }
            }
            loop.subSystemList = oben;
            PlayerLoop.SetPlayerLoop(loop);
        }

        /// <summary>v2.5: die ersten beiden Abschnitte der Spielschleife mit ihren Schritten (für den Kopf der Sitzungsdatei).</summary>
        private static string SchleifenAufbau()
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            if (loop.subSystemList == null) return "?";
            var sb = new StringBuilder();
            for (int i = 0; i < loop.subSystemList.Length && i < 2; i++)
            {
                PlayerLoopSystem s = loop.subSystemList[i];
                if (i > 0) sb.Append(" · ");
                sb.Append(s.type != null ? s.type.Name : "?").Append(" [");
                if (s.subSystemList != null)
                    for (int k = 0; k < s.subSystemList.Length; k++)
                    {
                        if (k > 0) sb.Append(", ");
                        sb.Append(s.subSystemList[k].type != null ? s.subSystemList[k].type.Name : "?");
                    }
                sb.Append(']');
            }
            return sb.ToString();
        }

        private static void HakenEntfernen()
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            if (Entfernen(ref loop)) PlayerLoop.SetPlayerLoop(loop);
        }

        private static bool Entfernen(ref PlayerLoopSystem s)
        {
            if (s.subSystemList == null) return false;
            bool weg = false;
            var neu = new List<PlayerLoopSystem>(s.subSystemList.Length);
            for (int i = 0; i < s.subSystemList.Length; i++)
            {
                PlayerLoopSystem c = s.subSystemList[i];
                if (c.type == typeof(AshveilBildAnfang) || c.type == typeof(AshveilZeitEnde) || c.type == typeof(AshveilInitAnfang) ||
                    c.type == typeof(AshveilBildStart) || c.type == typeof(AshveilBildEnde)) { weg = true; continue; }
                if (Entfernen(ref c)) weg = true;
                neu.Add(c);
            }
            if (weg) s.subSystemList = neu.ToArray();
            return weg;
        }

        // ================================================================== je Bild

        private void AmStart()
        {
            long jetzt = Stopwatch.GetTimestamp();
            BildanfangWache.FensterZu();
            long zyk = BildanfangWache.Zyklen();
            int gc = GC.CollectionCount(0);
            long m = ++_n;
            int i = Idx(m);
            _start[i] = jetzt;
            // Anfang dieses Bildes (vor dem Warten auf die Anzeige); fehlt der Haken, gilt der Start
            bool anfangDa = _anfangTick > 0 && _anfangTick <= jetzt && Sek(jetzt - _anfangTick) < 5.0;
            _anfang[i] = anfangDa ? _anfangTick : jetzt;
            _anfangTick = 0;
            _zeitEnde[i] = _zeitEndeTick >= _anfang[i] && _zeitEndeTick <= jetzt ? _zeitEndeTick : 0;
            _zeitEndeTick = 0;
            _initAnfang[i] = _initTick >= _anfang[i] && _initTick <= jetzt && _zeitEnde[i] > 0 && _initTick >= _zeitEnde[i] ? _initTick : 0;
            _initTick = 0;
            _zyk0[i] = anfangDa ? _zyk0Tick : 0;
            _zykZ[i] = _zeitEnde[i] > 0 ? _zykZTick : 0;
            _zyk1[i] = zyk;
            _gc0[i] = anfangDa ? _gc0Tick : gc;
            _gc1[i] = gc;
            _zyk0Tick = 0;
            _zykZTick = 0;
            _zeiger[i] = _zeigerJetzt < 0 ? 0 : _zeigerJetzt;
            if (m > 1 && _frame[Idx(m - 1)] > ErsteBilder)
                _sekJeZeiger[_zeiger[Idx(m - 1)]] += Sek(_anfang[i] - _anfang[Idx(m - 1)]);
            Mausfilter.NachrichtenAbholen(_nachrJetzt);
            for (int k = 0; k < 6; k++)
            {
                _nachr[i, k] = _nachrJetzt[k];
                _nachrSumme[k] += _nachrJetzt[k];
            }
            if (m > 1) _nachrSek += Sek(_anfang[i] - _anfang[Idx(m - 1)]);
            _ende[i] = 0;
            ZustaendeLesen(i);
            if (_frame[Idx(m - 1)] > ErsteBilder) AnfangZaehlen(i);
            if (Ms(jetzt - _anfang[i]) >= AnzeigeSchnappMs && _frame[Idx(m - 1)] > ErsteBilder) _schnappAnzeige[m] = Schnappschuss();
            if (_schnappAnzeige.Count > 8)
            {
                var alt = new List<long>();
                foreach (long k in _schnappAnzeige.Keys) if (k < m - 8) alt.Add(k);
                foreach (long k in alt) _schnappAnzeige.Remove(k);
            }

            long v = m - 1;   // das gerade beendete Bild
            if (v < 1) return;
            int iv = Idx(v);
            _pl[iv] = _playerLoop != null && _playerLoop.Rec.Valid ? _playerLoop.Rec.LastValue * 1e-6 : 0.0;
            if (_schnappFaellig.Remove(v) && !_schnapp.ContainsKey(v)) _schnapp[v] = Schnappschuss();

            double gesamt = Ms(_anfang[i] - _anfang[iv]);
            if (_frame[iv] > ErsteBilder)
            {
                _stat.Bild(gesamt, _zust[i] - _zust[iv], _var[i] - _var[iv]);
                if (gesamt >= SpitzenAuswertung.DetailMs)
                {
                    if (!_schnapp.ContainsKey(v)) _schnapp[v] = Schnappschuss();   // A: gleich danach
                    _schnappFaellig.Add(v + 1);                                     // B: ein Bild später
                    _offen.Add(new Offen { Bild = v, Ab = v + 4, Teil = _stat.Aktuell });
                }
            }
            for (int k = _offen.Count - 1; k >= 0; k--)
            {
                if (m < _offen[k].Ab) continue;
                Offen o = _offen[k];
                _offen.RemoveAt(k);
                Bericht(o);
            }
            if (_offen.Count == 0 && _schnappFaellig.Count == 0 && _schnapp.Count > 0) _schnapp.Clear();
        }

        /// <summary>v2.5: Hänger am Bildanfang nach dem Zeit-Abschnitt zählen und nach der Rechenzeit des Hauptfadens einteilen.</summary>
        private void AnfangZaehlen(int i)
        {
            if (_zeitEnde[i] == 0) return;
            double danach = Ms(_start[i] - _zeitEnde[i]);
            if (danach < AnfangHaengerMs) return;
            _anfangHaenger++;
            _anfangHaengerMs += danach;
            _anfangJeZeiger[_zeiger[i]]++;
            double rechnet = RechenMs(_zykZ[i], _zyk1[i]);
            if (rechnet >= 0.0)
            {
                double anteil = rechnet / danach;
                if (anteil >= 0.7) _anfangRechnet++;
                else if (anteil <= 0.2) _anfangWartet++;
                else _anfangGemischt++;
            }
            if (_gc1[i] != _gc0[i]) _anfangGc++;
        }

        /// <summary>Rechenzeit des Hauptfadens zwischen zwei Takt-Ständen in ms; &lt; 0, wenn unbekannt.</summary>
        private static double RechenMs(long z0, long z1)
        {
            double jeMs = BildanfangWache.ZyklenJeMs;
            if (jeMs <= 0.0 || z0 <= 0 || z1 < z0) return -1.0;
            return (z1 - z0) / jeMs;
        }

        /// <summary>v2.5: Zeilen zu den Hängern am Bildanfang (Zusammenfassung und Zwischenstand).</summary>
        private string AnfangText()
        {
            var sb = new StringBuilder();
            sb.Append("  Hänger am Bildanfang nach dem Zeit-Abschnitt (ab 25 ms): ").Append(_anfangHaenger);
            if (_anfangHaenger > 0)
            {
                sb.Append(" · zusammen ").Append(F0(_anfangHaengerMs)).Append(" ms");
                if (BildanfangWache.ZyklenJeMs > 0.0)
                    sb.Append(" · Hauptfaden rechnete dabei (ab 70 %): ").Append(_anfangRechnet).Append(" · wartete (bis 20 %): ")
                      .Append(_anfangWartet).Append(" · gemischt: ").Append(_anfangGemischt);
                sb.Append(" · mit Speicherbereinigung: ").Append(_anfangGc);
            }
            sb.AppendLine();
            sb.Append("    je Mauszeiger (Hänger · Spielzeit · je Minute):");
            for (int z = 0; z < 3; z++)
            {
                if (_sekJeZeiger[z] < 1.0 && _anfangJeZeiger[z] == 0) continue;
                sb.Append(' ').Append(ZeigerTexte[z]).Append(' ').Append(_anfangJeZeiger[z]).Append(" · ").Append(F0(_sekJeZeiger[z])).Append(" s · ")
                  .Append(_sekJeZeiger[z] >= 1.0 ? (_anfangJeZeiger[z] * 60.0 / _sekJeZeiger[z]).ToString("0.0", CultureInfo.InvariantCulture) : "–").Append(';');
            }
            sb.AppendLine();
            long alle = 0;
            for (int k = 0; k < 6; k++) alle += _nachrSumme[k];
            if (alle > 0 && _nachrSek > 1.0)
            {
                sb.Append("  Fenster-Nachrichten an Unitys Fenster (gesamt · je Sekunde):");
                for (int k = 0; k < 6; k++)
                    sb.Append(' ').Append(Mausfilter.NachrichtenArten[k]).Append(' ').Append(_nachrSumme[k]).Append(" · ")
                      .Append((_nachrSumme[k] / _nachrSek).ToString("0", CultureInfo.InvariantCulture)).Append(';');
                sb.AppendLine();
            }
            string filter = Mausfilter.FilterText();
            if (filter != null) sb.AppendLine(filter);
            string wache = BildanfangWache.Zusammenfassung();
            if (wache != null) sb.Append(wache);
            return sb.ToString();
        }

        private void AmEnde()
        {
            int i = Idx(_n);
            _ende[i] = Stopwatch.GetTimestamp();
            _frame[i] = Time.frameCount;
            if (_frameTiming) FrameTimingManager.CaptureFrameTimings();
        }

        private void ZustaendeLesen(int i)
        {
#if !UNITY_EDITOR
            if (!_gscAn || _gsc == null) return;
            long t0 = Stopwatch.GetTimestamp();
            try
            {
                _zust[i] = _gsc.totalGraphicsStateCount;
                _var[i] = _gsc.variantCount;
            }
            catch (Exception)
            {
                _gscAn = false;
                _gscStatus = "beim Lesen ausgefallen";
            }
            _zustLesenUs += Ms(Stopwatch.GetTimestamp() - t0) * 1000.0;
            _zustLesenN++;
#endif
        }

        private List<KeyValuePair<string, double>> Schnappschuss()
        {
            var l = new List<KeyValuePair<string, double>>();
            foreach (Punkt p in _punkte)
            {
                if (!p.Rec.Valid) continue;
                long ns = p.Rec.LastValue;
                // über 10 s ist kein Zeitwert eines Bildes; Leerlauf und Auftragsverwaltung sagen nichts (SpitzenAuswertung.Rauschen)
                if (ns < SchnappMinNs || ns > 10_000_000_000L || SpitzenAuswertung.Rauschen(p.Name, 0.0, 1e12)) continue;
                l.Add(new KeyValuePair<string, double>(p.Name, ns * 1e-6));
            }
            l.Sort((a, b) => b.Value.CompareTo(a.Value));
            if (l.Count > SchnappMax) l.RemoveRange(SchnappMax, l.Count - SchnappMax);
            return l;
        }

        // ================================================================== Messpunkte

        private void Scannen()
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var kandidaten = new List<KeyValuePair<ProfilerRecorderHandle, string>>();
            foreach (ProfilerRecorderHandle h in handles)
            {
                ProfilerRecorderDescription d = ProfilerRecorderHandle.GetDescription(h);
                if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
                string name = d.Name;
                if (string.IsNullOrEmpty(name) || _bekannt.Contains(name)) continue;
                kandidaten.Add(new KeyValuePair<ProfilerRecorderHandle, string>(h, name));
            }
            var gesucht = new HashSet<string>(Namen);
            int neu = 0;
            // erst die genauen Namen, dann der Rest (so verdrängt die Obergrenze nie die wichtigen)
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var kv in kandidaten)
                {
                    if (_bekannt.Contains(kv.Value)) continue;
                    bool genau = gesucht.Contains(kv.Value);
                    if (pass == 0 && !genau) continue;
                    if (pass == 1 && !_voll && !TeilPasst(kv.Value)) { _bekannt.Add(kv.Value); continue; }
                    if (_punkte.Count >= _obergrenze) break;
                    _bekannt.Add(kv.Value);
                    var rec = new ProfilerRecorder(kv.Key, 2, ProfilerRecorderOptions.Default);
                    rec.Start();
                    var p = new Punkt { Name = kv.Value, Rec = rec };
                    _punkte.Add(p);
                    if (kv.Value == "PlayerLoop") _playerLoop = p;
                    neu++;
                }
            }
            _scans++;
            if (_scans > 1 && neu > 0) Notiz($"Messpunkte nachgeladen: +{neu} (jetzt {_punkte.Count})");
        }

        private static bool TeilPasst(string name)
        {
            foreach (string t in Teile)
                if (name.IndexOf(t, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        // ================================================================== Bericht

        private void Bericht(Offen o)
        {
            long v = o.Bild;
            int iv = Idx(v), i1 = Idx(v + 1), i2 = Idx(v + 2);
            double gesamt = Ms(_anfang[i1] - _anfang[iv]);
            double anzeige = Ms(_start[iv] - _anfang[iv]);
            bool endeDa = _ende[iv] > _start[iv];
            double schleife = endeDa ? Ms(_ende[iv] - _start[iv]) : gesamt - anzeige;
            double aussen = endeDa ? Ms(_anfang[i1] - _ende[iv]) : 0.0;
            SpitzenAuswertung.Art art = SpitzenAuswertung.Einordnen(gesamt, schleife, anzeige, aussen);
            int zustNeu = _zust[i1] - _zust[iv], zustDanach = _zust[i2] - _zust[i1];
            int varNeu = _var[i1] - _var[iv], varDanach = _var[i2] - _var[i1];
            double t0 = Sek(_start[iv]), t1 = Sek(_start[i1]);
            bool fenster = _fokusZeit >= t0 - 1.0 && _fokusZeit <= t1 + 0.5;
            // Der Messpunkt „PlayerLoop“ umfasst das Warten auf die Anzeige und die Schleife
            int abgleich = SpitzenAuswertung.Abgleich(anzeige + schleife, _pl[iv], _pl[i1]);
            _schnapp.TryGetValue(v, out List<KeyValuePair<string, double>> a);
            _schnapp.TryGetValue(v + 1, out List<KeyValuePair<string, double>> b);
            _schnappAnzeige.TryGetValue(v, out List<KeyValuePair<string, double>> z);
            // Beim Warten am Bildanfang zeigt Z, worauf gewartet wurde; sonst der passende Schnappschuss
            List<KeyValuePair<string, double>> gewaehlt = art == SpitzenAuswertung.Art.Anzeige && z != null ? z : abgleich == 1 ? b : a;
            if (gewaehlt != null)
                foreach (var kv in gewaehlt)
                    if (kv.Key != "PlayerLoop" && kv.Key != "Main Thread" && !SpitzenAuswertung.Rauschen(kv.Key, kv.Value, gesamt))
                        _stat.Ursache(kv.Key, kv.Value);
            _stat.Haenger(o.Teil, art, zustNeu + zustDanach > 0, fenster);

            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append(" Bild ").Append(_frame[iv]).Append(": ")
              .Append(F0(gesamt)).Append(" ms (Warten auf Anzeige ").Append(F0(anzeige)).Append(" · Spielschleife ").Append(F0(schleife))
              .Append(" · außerhalb ").Append(F0(aussen)).Append(") – ")
              .Append(SpitzenAuswertung.Text(art));
            if (_gscAn) sb.Append(" · neue Grafik-Zustände ").Append(zustNeu).Append(" + ").Append(zustDanach)
                          .Append(" (Shader-Varianten ").Append(varNeu).Append(" + ").Append(varDanach).Append(')');
            if (fenster) sb.Append(" · Fensterwechsel");
            sb.Append(" · Mauszeiger ").Append(ZeigerTexte[_zeiger[iv]]);
            sb.Append(" · Abschnitt „").Append(o.Teil != null ? o.Teil.Name : "?").Append('“').AppendLine();
            string zeiten = BildZeiten(gesamt);
            if (zeiten != null) sb.Append("    Bild-Zeiten: ").AppendLine(zeiten);
            SpeicherLesen();
            if (_gfxAnfang >= 0) sb.Append("    Grafikspeicher (Unity): ").Append(Mb(_gfxJetzt)).Append(" MB von ").Append(SystemInfo.graphicsMemorySize).AppendLine(" MB");
            if (anzeige >= 20.0 && _zeitEnde[iv] > 0)
            {
                double zeitTeil = Ms(_zeitEnde[iv] - _anfang[iv]);
                sb.Append("    Am Bildanfang: Zeit-Abschnitt (Warten auf das vorige Bild, Bildraten-Grenze) ").Append(F0(zeitTeil))
                  .Append(" ms · danach bis zum Profiler-Bildstart ").Append(F0(Math.Max(0.0, anzeige - zeitTeil))).Append(" ms");
                if (_initAnfang[iv] > 0)
                    sb.Append(" (Übergang ").Append(F0(Ms(_initAnfang[iv] - _zeitEnde[iv]))).Append(" · Profiler-Bildstart ")
                      .Append(F0(Ms(_start[iv] - _initAnfang[iv]))).Append(')');
                sb.AppendLine();
                double r1 = RechenMs(_zyk0[iv], _zykZ[iv]), r2 = RechenMs(_zykZ[iv], _zyk1[iv]);
                if (r1 >= 0.0 && r2 >= 0.0)
                    sb.Append("    Hauptfaden rechnete dabei: im Zeit-Abschnitt ").Append(F0(r1)).Append(" ms · danach ").Append(F0(r2))
                      .Append(" ms · Speicherbereinigungen ").Append(_gc1[iv] - _gc0[iv]).AppendLine();
            }
            int nachrichten = 0;
            for (int k = 0; k < 6; k++) nachrichten += _nachr[iv, k];
            if (nachrichten > 0)
            {
                sb.Append("    Fenster-Nachrichten seit dem vorigen Bild:");
                for (int k = 0; k < 6; k++)
                    if (_nachr[iv, k] > 0) sb.Append(' ').Append(Mausfilter.NachrichtenArten[k]).Append(' ').Append(_nachr[iv, k]).Append(';');
                sb.AppendLine();
            }
            List<BildanfangWache.Probe> proben = BildanfangWache.Abholen(v);
            if (proben != null)
                foreach (BildanfangWache.Probe p in proben)
                {
                    sb.Append("    Stapelprobe nach ").Append(F0(p.NachMs)).Append(" ms (").Append(BildanfangWache.PhasenText(p.Phase));
                    if (p.RechenMs >= 0.0) sb.Append(", Hauptfaden rechnete bis dahin ").Append(F0(p.RechenMs)).Append(" ms");
                    sb.Append("): ").AppendLine(p.Stapel);
                    if (p.SystemText != null) sb.Append("      System: ").AppendLine(p.SystemText);
                }
            sb.Append("    Abgleich PlayerLoop: A ").Append(F0(_pl[iv])).Append(" ms, B ").Append(F0(_pl[i1])).Append(" ms, eigene Messung ")
              .Append(F0(anzeige + schleife)).Append(" ms → ").AppendLine(abgleich == 0 ? "A" : abgleich == 1 ? "B" : "keiner passt");
            if (z != null) Liste(sb, "Z (Warten am Bildanfang)", z, gesamt);
            Liste(sb, "A", a, gesamt);
            Liste(sb, "B", b, gesamt);
            _schnappAnzeige.Remove(v);
            Schreiben(sb.ToString());

            // Schnappschüsse, die kein offener Bericht mehr braucht
            var alt = new List<long>();
            foreach (long k in _schnapp.Keys) if (k <= v) alt.Add(k);
            foreach (long k in alt)
            {
                bool gebraucht = false;
                foreach (Offen x in _offen) if (x.Bild == k || x.Bild + 1 == k) gebraucht = true;
                if (!gebraucht) _schnapp.Remove(k);
            }
        }

        private static void Liste(StringBuilder sb, string label, List<KeyValuePair<string, double>> l, double gesamt)
        {
            sb.Append("    ").Append(label).Append(": ");
            int n = 0;
            if (l != null)
                foreach (var kv in l)
                {
                    if (SpitzenAuswertung.Rauschen(kv.Key, kv.Value, gesamt)) continue;
                    if (n++ > 0) sb.Append(" · ");
                    sb.Append(kv.Key).Append(' ').Append(F0(kv.Value));
                }
            sb.AppendLine(n == 0 ? "–" : "");
        }

        /// <summary>
        /// Unitys Bild-Zeiten: das Bild der letzten 16, dessen Gesamtzeit am nächsten an der eigenen Messung liegt (die Zeitstempel
        /// von Unity lassen sich nicht mit der eigenen Stoppuhr vergleichen).
        /// </summary>
        private string BildZeiten(double gesamtMs)
        {
            if (!_frameTiming) return null;
            uint n = FrameTimingManager.GetLatestTimings((uint)_timings.Length, _timings);
            if (n == 0) return "keine Daten";
            int best = 0;
            for (int k = 1; k < n; k++)
                if (Math.Abs(_timings[k].cpuFrameTime - gesamtMs) < Math.Abs(_timings[best].cpuFrameTime - gesamtMs)) best = k;
            FrameTiming f = _timings[best];
            return $"Hauptfaden {F0(f.cpuMainThreadFrameTime)} ms (Warten auf Anzeige {F0(f.cpuMainThreadPresentWaitTime)}) · " +
                   $"Grafikfaden {F0(f.cpuRenderThreadFrameTime)} ms · GPU {F0(f.gpuFrameTime)} ms · gesamt {F0(f.cpuFrameTime)} ms " +
                   $"(nächstes der letzten {n})";
        }

        // ================================================================== Datei

        private string Ordner => Path.GetFullPath(Path.Combine("Logs", "Ashveil", "Ruckler"));

        private void DateiAnlegen()
        {
            _stempel = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
            var kopf = new StringBuilder();
            if (Application.isEditor)
            {
                _datei = Path.GetFullPath(Path.Combine("Logs", "Ashveil", "Spitzen_Ursachen.txt"));
                kopf.Append("=== Editor-Sitzung ").Append(_stempel).Append(" · v2 · ").Append(_punkte.Count).AppendLine(" Messpunkte");
            }
            else
            {
                Ashveil.Core.Diagnostics.LogRotation.KeepNewestStampedFiles(Ordner, 4); // Max 11.10.: nur die letzten 5 Sitzungen behalten
                _datei = Path.Combine(Ordner, $"Sitzung_{_stempel}.txt");
                kopf.Append("ASHVEIL Bild-Spitzen (Ruckler, CORE-005) · Sitzung ").Append(_stempel.Replace('_', ' ')).Append(" · Version ")
                    .Append(Application.version).Append(Debug.isDebugBuild ? " · Entwicklungs-Build" : "").AppendLine();
                kopf.Append("Messung v2.7 · Modus ").Append(_voll ? "voll (alle Zeit-Messpunkte)" : "normal").Append(" · ").Append(_punkte.Count)
                    .Append(" Messpunkte · Bild-Zeiten ").Append(_frameTiming ? "an" : "aus").Append(" · Grafik-Zustände ").Append(_gscStatus).AppendLine();
                kopf.Append("Grafik: ").Append(SystemInfo.graphicsDeviceType).Append(" · ").Append(SystemInfo.graphicsDeviceName).Append(" · ")
                    .Append(SystemInfo.renderingThreadingMode).Append(" · ").Append(Screen.width).Append('×').Append(Screen.height).Append(' ')
                    .Append(Screen.fullScreenMode).Append(" · Qualität ").Append(QualitySettings.names[QualitySettings.GetQualityLevel()])
                    .Append(" · VSync ").Append(QualitySettings.vSyncCount).Append(" · Ziel ").Append(Application.targetFrameRate).AppendLine(" Bilder/s");
                kopf.AppendLine("Bilder ab 50 ms mit Messpunkten (Hänger ab 100 ms). Warten auf Anzeige = auf das vorige Bild (Grafikkarte/Treiber); „Am Bildanfang“ trennt das Warten selbst vom Profiler-Bildstart danach. A = Messpunkte gleich nach dem langen Bild gelesen, B = ein Bild später, Z = gleich nach einem langen Warten am Bildanfang (Werte in ms, über alle Fäden summiert; Leerlauf und Auftragsverwaltung ausgefiltert).");
            }
            Schreiben(kopf.ToString());
        }

        private void Notiz(string text)
        {
            Schreiben(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + text + Environment.NewLine);
        }

        private void Schreiben(string text)
        {
            if (string.IsNullOrEmpty(_datei)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_datei));
                File.AppendAllText(_datei, text);
            }
            catch (Exception) { }
        }

        private void Abschliessen()
        {
            if (_abgeschlossen || _stat == null) return;
            _abgeschlossen = true;
            var sb = new StringBuilder();
            sb.AppendLine().Append("=== Zusammenfassung ").Append(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).AppendLine(" ===");
            sb.Append(_stat.Text(_gscAn));
            sb.Append("  Fensterwechsel: ").Append(_fokusWechsel).Append(" · Messpunkte am Ende: ").Append(_punkte.Count)
              .Append(" (Modus ").Append(_voll ? "voll" : "normal").AppendLine(")");
            sb.Append(AnfangText());
            SpeicherLesen();
            string speicher = SpeicherText();
            if (speicher != null) sb.Append("  Grafikspeicher (Unity, beim Treiber angelegt): ").AppendLine(speicher);
#if !UNITY_EDITOR
            if (_zustLesenN > 0)
                sb.Append("  Zählen der Grafik-Zustände: ").Append((_zustLesenUs / _zustLesenN).ToString("0.0", CultureInfo.InvariantCulture)).AppendLine(" µs je Bild");
            if (_gsc != null)
            {
                try
                {
                    if (_gsc.isTracing) _gsc.EndTrace();
                    string pfad = Path.Combine(Ordner, $"Grafikzustaende_{_stempel}.graphicsstate");
                    Directory.CreateDirectory(Ordner);
                    bool ok = _gsc.SaveToFile(pfad);
                    sb.Append("  Grafik-Zustände am Ende: ").Append(_gsc.totalGraphicsStateCount).Append(" in ").Append(_gsc.variantCount)
                      .Append(" Shader-Varianten · ").Append(_gsc.graphicsDeviceType).Append(" · ").AppendLine(ok ? "gespeichert: " + pfad : "nicht gespeichert");
                }
                catch (Exception e)
                {
                    sb.Append("  Grafik-Zustände: ").AppendLine(e.GetType().Name + ": " + e.Message);
                }
            }
            if (_liste) MesspunkteListeSchreiben();
#endif
            Schreiben(sb.ToString());
        }

        /// <summary>Volle Messung: alle angemeldeten Messpunkte mit Kategorie (zum Nachschlagen der Namen).</summary>
        private void MesspunkteListeSchreiben()
        {
            try
            {
                var handles = new List<ProfilerRecorderHandle>();
                ProfilerRecorderHandle.GetAvailable(handles);
                var zeilen = new List<string>(handles.Count);
                var aufgenommen = new HashSet<string>();
                foreach (Punkt p in _punkte) aufgenommen.Add(p.Name);
                foreach (ProfilerRecorderHandle h in handles)
                {
                    ProfilerRecorderDescription d = ProfilerRecorderHandle.GetDescription(h);
                    zeilen.Add($"{d.Name}\t{d.Category.Name}\t{d.UnitType}\t{(aufgenommen.Contains(d.Name) ? "aufgenommen" : "-")}");
                }
                zeilen.Sort(StringComparer.Ordinal);
                Directory.CreateDirectory(Ordner);
                File.WriteAllLines(Path.Combine(Ordner, "Messpunkte_Liste.txt"), zeilen);
            }
            catch (Exception) { }
        }

        private static bool HatArgument(string name)
        {
            foreach (string a in Environment.GetCommandLineArgs())
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
#endif
