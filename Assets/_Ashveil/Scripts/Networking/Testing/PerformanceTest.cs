using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ashveil.Core;
using Ashveil.Core.Settings;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Visuals;
using Ashveil.Rendering;
using Ashveil.UI;
using Ashveil.World;
using Unity.Profiling;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Leistungstest (Ashveil &gt; Test &gt; Leistung): prueft die Grafikeinstellungen (Stufen, Einzelwerte, Erkennung
    /// der Grafikkarte, Fenster) und misst die Bildrate je Stufe – ohne und mit 50 zusaetzlichen Spielfiguren.
    ///
    /// Die 50 Figuren sind Darstellungs-Doubles: echte Koerper, Frisuren, zufaellige Ausruestung, Namensschilder,
    /// laufende Animation und Figurendetail wie echte Mitspieler – aber ohne Netzverbindung. Gemessen wird also die
    /// Last auf dem eigenen Rechner (Grafik + Animation). Die Serverlast von 50 echten Verbindungen ist ein eigener Test.
    /// Die Einstellung des Spielers wird nicht veraendert (Speichern waehrend des Tests aus, danach zurueck).
    /// </summary>
    public sealed class PerformanceTest : MonoBehaviour
    {
        public const string FlagFile = "Temp/ashveil_perftest.flag";
        public const int FigureCount = 50;

        private static string ReportPath => ProjectPaths.Logs(Application.isEditor ? "Ashveil_Leistung.txt" : "Ashveil_Leistung_Spiel.txt");

        private readonly StringBuilder _report = new StringBuilder();
        private readonly List<Transform> _figures = new List<Transform>();
        private readonly List<Animator> _figureAnims = new List<Animator>();
        private readonly List<float> _frameTimes = new List<float>(4096);
        private double _cpuMs, _gpuMs, _drawCalls, _batches, _tris, _skinned;
        private int _statFrames, _gpuFrames;
        private readonly FrameTiming[] _timing = new FrameTiming[1];
        private int _passed, _failed, _logErrors;
        private Vector3 _crowdCenter;

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int MoveZId = Animator.StringToHash("MoveZ");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int WalkingId = Animator.StringToHash("Walking");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CheckFlag()
        {
            if (!TestBuild.Enabled) return;
            bool requested = Environment.GetCommandLineArgs() is string[] args && Array.IndexOf(args, "-ashveil-perftest") >= 0;
            if (Application.isEditor && File.Exists(FlagFile))
            {
                requested = true;
                try { File.Delete(FlagFile); } catch (IOException) { }
            }
            if (!requested) return;
            ProfileSelection.Override = "perftest";
            var go = new GameObject("Leistungstest");
            DontDestroyOnLoad(go);
            go.AddComponent<PerformanceTest>();
        }

        private IEnumerator Start()
        {
            Application.logMessageReceived += OnLog;
            GraphicsOptions original = GraphicsSettingsStore.Current.Clone();
            GraphicsSettingsStore.PersistenceEnabled = false;
            Line($"ASHVEIL Leistungstest · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Line($"  Grafikkarte: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB) · {SystemInfo.graphicsDeviceType}");
            Line($"  Prozessor:   {SystemInfo.processorType} ({SystemInfo.processorCount} Threads) · RAM {SystemInfo.systemMemorySize} MB");
            Line($"  Bild:        {Screen.width} x {Screen.height} · {(Application.isEditor ? "Unity-Editor (Game-Fenster, Editor kostet zusaetzlich)" : "Spiel")}");

            PlayerCharacter player = null;
            float wait = 0f;
            while (player == null && wait < 40f)
            {
                player = FindLocalPlayer();
                wait += 0.5f;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Check(player != null, "Eigene Spielfigur ist da");
            if (player == null)
            {
                Finish(original);
                yield break;
            }
            yield return new WaitForSecondsRealtime(4f);

            yield return SettingsChecks(player);

            // ---------------------------------------------------------- Messung
            Section("Bildrate je Stufe (VSync aus, ohne Begrenzung)");
            var results = new List<string>();
            var baseline = new float[GraphicsCatalog.TierCount];
            for (int t = 0; t < GraphicsCatalog.TierCount; t++)
            {
                ApplyUncapped((GraphicsTier)t);
                yield return new WaitForSecondsRealtime(2.5f);
                yield return Measure(5f);
                baseline[t] = Avg();
                results.Add($"  {GraphicsCatalog.TierNames[t],-8} ohne Figuren:      {Avg(),6:0} Bilder/s  (1 % tiefste: {Low1(),5:0})");
                results.Add($"             {Stats()}");
                string est0 = Estimate();
                if (est0.Length > 0) results.Add("             " + est0);
            }

            // Hohe Aufloesungen ohne passenden Bildschirm: Kamera rendert in ein 4K- bzw. 8K-Bild (volle Pipeline)
            yield return ResolutionProbe(results, 3840, 2160, "4K");
            yield return ResolutionProbe(results, 7680, 4320, "8K");

            yield return Breakdown(results);

            SpawnCrowd(player);
            Check(_figures.Count == FigureCount, $"{FigureCount} Spielfiguren (Doubles) erzeugt", $"{_figures.Count}");
            yield return new WaitForSecondsRealtime(3f);
            Check(CharacterLod.Count >= FigureCount + 1, "Alle Figuren haben ein Figurendetail", $"{CharacterLod.Count}");

            for (int t = 0; t < GraphicsCatalog.TierCount; t++)
            {
                ApplyUncapped((GraphicsTier)t);
                yield return new WaitForSecondsRealtime(3f);
                yield return Measure(8f);
                CharacterLod.Tally(out int full, out int reduced, out int hidden);
                results.Add($"  {GraphicsCatalog.TierNames[t],-8} mit {FigureCount} Figuren:  {Avg(),6:0} Bilder/s  (1 % tiefste: {Low1(),5:0}) · " +
                            $"Figuren voll/reduziert/aus: {full}/{reduced}/{hidden}");
                results.Add($"             {Stats()}");
                string est = Estimate();
                if (est.Length > 0) results.Add("             " + est);
                int budget = GraphicsCatalog.FullDetailFigures[t] + 1;
                Check(full <= budget, $"{GraphicsCatalog.TierNames[t]}: hoechstens {budget - 1} fremde Figuren in voller Qualitaet", $"{full} voll");
                if (t == 0) Check(reduced > 0 || hidden > 0, "Niedrig: entfernte Figuren werden vereinfacht", $"{reduced} reduziert, {hidden} aus");
            }
            yield return ResolutionProbe(results, 7680, 4320, "8K", $" mit {FigureCount} Figuren");
            foreach (string r in results) Line(r);
            Line("  Ziel: Niedrig ≥ 60 Bilder/s ab RTX 3050/3060 bei 1080p, Mittel ≥ 60 auf Durchschnitts-PC (RTX 3060 Ti/4060).");
            Line("  Episch: fuer RTX 4090/5080/5090 bei 4K–8K; stabil heisst ≥ 60 Bilder/s bei 4K und ≥ 30 (besser 60) bei 8K, 1 % tiefste ≥ 70 % davon.");
            Line("  Die Werte gelten fuer DIESEN Rechner; im Editor liegt die Bildrate spuerbar unter dem fertigen Spiel.");
            Line("  Hochrechnung (≈): GPU-Zeit x3 (RTX 5080 → 3060) und auf 1080p umgerechnet, CPU-Zeit x1,6 (285K → 6-Kern-Mittelklasse).");

            ClearCrowd();
            Finish(original);
        }

        // ================================================================== Pruefungen der Einstellungen

        private IEnumerator SettingsChecks(PlayerCharacter player)
        {
            Section("Grafikeinstellungen");
            Check(GraphicsApplier.Instance != null, "Grafik-Anwender laeuft");
            GraphicsApplier ap = GraphicsApplier.Instance;
            for (int t = 0; t < GraphicsCatalog.TierCount; t++)
            {
                GraphicsSettingsStore.SetTier((GraphicsTier)t);
                yield return null;
                GraphicsOptions o = GraphicsSettingsStore.Current;
                string n = GraphicsCatalog.TierNames[t];
                Check(o.tier == (GraphicsTier)t, $"Stufe {n} gewaehlt");
                if (ap != null)
                {
                    float sd = o.shadows > 0 ? GraphicsCatalog.ShadowDistance[o.shadows] : 0f;
                    Check(Mathf.Abs(ap.PipelineShadowDistance - sd) < 0.5f, $"{n}: Schattenweite {sd:0} m in der Render-Pipeline", $"{ap.PipelineShadowDistance:0}");
                    Check(ap.PipelineMsaa == ap.ExpectedMsaa(o), $"{n}: MSAA passt", $"{ap.PipelineMsaa}");
                    int wantRes = GraphicsApplier.SupportedShadowResolution(GraphicsCatalog.ShadowResolution[o.shadows]);
                    Check(ap.PipelineShadowResolution == wantRes, $"{n}: Schattenaufloesung {wantRes}", $"{ap.PipelineShadowResolution}");
                }
                Terrain terrain = WorldGround.TerrainAt(Vector3.zero);
                if (terrain != null)
                    Check(Mathf.Abs(terrain.detailObjectDistance - GraphicsCatalog.DetailDistance[o.vegetation]) < 0.5f,
                        $"{n}: Gras bis {GraphicsCatalog.DetailDistance[o.vegetation]:0} m", $"{terrain.detailObjectDistance:0}");
                Camera cam = Camera.main;
                if (cam != null)
                    Check(Mathf.Abs(cam.farClipPlane - GraphicsCatalog.FarClip[o.viewDistance]) < 1f, $"{n}: Sichtweite {GraphicsCatalog.FarClip[o.viewDistance]:0} m", $"{cam.farClipPlane:0}");
                Check(Mathf.Approximately(QualitySettings.lodBias, GraphicsCatalog.LodBias[o.viewDistance]), $"{n}: Detailstufen-Abstand", $"{QualitySettings.lodBias}");
            }
            if (ap != null) Line("  · " + ap.LastSummary);

            // Einzelwert aendern -> Benutzerdefiniert, zurueck -> wieder Mittel
            GraphicsSettingsStore.SetTier(GraphicsTier.Mittel);
            GraphicsCatalog.Row shadowRow = Array.Find(GraphicsCatalog.Rows, r => r.Label == "Schatten");
            SettingsUi.Step(shadowRow, 1);
            Check(GraphicsSettingsStore.Current.tier == GraphicsTier.Benutzerdefiniert, "Einzelwert geaendert → Benutzerdefiniert");
            SettingsUi.Step(shadowRow, -1);
            Check(GraphicsSettingsStore.Current.tier == GraphicsTier.Mittel, "Wert zurueck → wieder Mittel");
            GraphicsOptions keep = GraphicsSettingsStore.Current.Clone();
            keep.frameLimit = 1;
            GraphicsSettingsStore.Set(keep);
            Check(GraphicsSettingsStore.Current.tier == GraphicsTier.Mittel, "Anzeige-Werte (Bildrate) aendern die Stufe nicht");
            Check(Application.targetFrameRate == 60, "Bildrate auf 60 begrenzbar", $"{Application.targetFrameRate}");

            // Speichern/Laden als Text (ohne die Datei des Spielers anzufassen)
            GraphicsOptions hi = GraphicsCatalog.Preset(GraphicsTier.Hoch);
            GraphicsOptions back = JsonUtility.FromJson<GraphicsOptions>(JsonUtility.ToJson(hi));
            Check(back != null && back.SameValues(hi) && back.tier == GraphicsTier.Hoch, "Einstellungen lassen sich speichern und laden");

            // Erkennung der Grafikkarte
            (string gpu, int vram, GraphicsTier want)[] cases =
            {
                ("NVIDIA GeForce RTX 3050", 8192, GraphicsTier.Niedrig), ("NVIDIA GeForce RTX 3060", 12288, GraphicsTier.Niedrig),
                ("NVIDIA GeForce RTX 3060 Ti", 8192, GraphicsTier.Mittel), ("NVIDIA GeForce RTX 3080", 10240, GraphicsTier.Mittel),
                ("NVIDIA GeForce RTX 4060", 8192, GraphicsTier.Mittel), ("NVIDIA GeForce RTX 4070 SUPER", 12288, GraphicsTier.Hoch),
                ("NVIDIA GeForce RTX 5070", 12288, GraphicsTier.Hoch), ("NVIDIA GeForce RTX 5090", 32768, GraphicsTier.Episch),
                ("NVIDIA GeForce RTX 5080", 16384, GraphicsTier.Episch), ("NVIDIA GeForce RTX 4090", 24576, GraphicsTier.Episch),
                ("NVIDIA GeForce RTX 4080 SUPER", 16384, GraphicsTier.Hoch), ("NVIDIA GeForce RTX 5070 Ti", 16384, GraphicsTier.Hoch),
                ("AMD Radeon RX 6600", 8192, GraphicsTier.Niedrig), ("AMD Radeon RX 7800 XT", 16384, GraphicsTier.Hoch),
                ("AMD Radeon RX 7900 XTX", 24576, GraphicsTier.Episch), ("AMD Radeon RX 9070 XT", 16384, GraphicsTier.Episch),
                ("AMD Radeon RX 9060 XT", 16384, GraphicsTier.Mittel),
                ("Unbekannte Karte", 16384, GraphicsTier.Hoch), ("Intel(R) UHD Graphics", 1024, GraphicsTier.Niedrig)
            };
            int ok = 0;
            var wrong = new StringBuilder();
            foreach (var c in cases)
            {
                GraphicsTier got = GraphicsSettingsStore.DetectTier(c.gpu, c.vram, out _);
                if (got == c.want) ok++;
                else wrong.Append($"{c.gpu}→{got} ");
            }
            Check(ok == cases.Length, $"Grafikkarte wird der richtigen Stufe zugeordnet ({cases.Length} Beispiele)", wrong.ToString());
            GraphicsSettingsStore.DetectTier(SystemInfo.graphicsDeviceName, SystemInfo.graphicsMemorySize, out string mine);
            Line("  · Dieser Rechner: " + mine);

            // Fenster
            SettingsUi ui = SettingsUi.Instance;
            Check(ui != null, "Einstellungsfenster vorhanden (Taste O)");
            if (ui != null)
            {
                ui.SetOpen(true);
                yield return null;
                yield return null;
                Check(ui.IsOpen, "Einstellungsfenster oeffnet");
                ui.SetOpen(false);
                yield return null;
            }
            Check(player.GetComponent<CharacterLod>() != null, "Eigene Figur hat Figurendetail (bleibt immer voll)");
        }

        // ================================================================== Messen

        /// <summary>
        /// Episch bei einer hohen Aufloesung messen, ohne einen solchen Bildschirm zu brauchen: die Spielkamera rendert
        /// (volle Pipeline inkl. Schatten, Nachbearbeitung, Kantenglaettung) in ein Bild dieser Groesse.
        /// </summary>
        private IEnumerator ResolutionProbe(List<string> results, int w, int h, string name, string suffix = "")
        {
            Camera cam = Camera.main;
            if (cam == null) yield break;
            GraphicsApplier.OutputSizeOverride = new Vector2Int(w, h);
            ApplyUncapped(GraphicsTier.Episch);
            RenderTexture rt = null;
            bool ok = true;
            try
            {
                rt = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR) { name = "Leistungstest " + name };
                ok = rt.Create();
            }
            catch (Exception e)
            {
                ok = false;
                Line($"  ! {name}-Bild nicht anlegbar: {e.Message}");
            }
            Check(ok, $"{name}-Messung: Bild {w} x {h} angelegt");
            if (!ok)
            {
                GraphicsApplier.OutputSizeOverride = null;
                yield break;
            }
            RenderTexture prev = cam.targetTexture;
            cam.targetTexture = rt;
            yield return new WaitForSecondsRealtime(2.5f);
            yield return Measure(6f);
            GraphicsApplier ap = GraphicsApplier.Instance;
            results.Add($"  Episch   {name} ({w} x {h}){suffix}: {Avg(),6:0} Bilder/s  (1 % tiefste: {Low1(),5:0}) · MSAA {(ap != null ? ap.PipelineMsaa : 0)}x");
            results.Add($"             {Stats()}");
            cam.targetTexture = prev;
            rt.Release();
            Destroy(rt);
            GraphicsApplier.OutputSizeOverride = null;
            ApplyUncapped(GraphicsTier.Episch);
            yield return new WaitForSecondsRealtime(1f);
        }

        private void ApplyUncapped(GraphicsTier tier)
        {
            GraphicsSettingsStore.SetTier(tier);
            GraphicsOptions o = GraphicsSettingsStore.Current.Clone();
            o.vSync = false;
            o.frameLimit = GraphicsCatalog.FrameLimits.Length - 1;
            GraphicsSettingsStore.Set(o);
        }

        /// <summary>
        /// Aufschluesselung (Mittel, ohne Figuren): einzelne Teile der Welt kurz abschalten und messen, was sie kosten.
        /// Nur Darstellung/Logik vor Ort – danach wird alles wieder eingeschaltet.
        /// </summary>
        private IEnumerator Breakdown(List<string> results)
        {
            ApplyUncapped(GraphicsTier.Mittel);
            yield return new WaitForSecondsRealtime(2f);
            yield return Measure(4f);
            double baseCpu = _cpuMs / Mathf.Max(1, _statFrames), baseGpu = _gpuFrames > 0 ? _gpuMs / _gpuFrames : 0;
            results.Add($"  Aufschluesselung (Mittel, ohne Figuren): {Avg():0} Bilder/s, CPU {baseCpu:0.0} ms, GPU {baseGpu:0.0} ms");
            var parts = new List<(string name, List<GameObject> objs, List<Behaviour> comps)>();
            var npcs = new List<GameObject>();
            foreach (var r in Ashveil.Gameplay.Npcs.NpcRoutineRegistry.All) if (r != null) npcs.Add(r.gameObject);
            List<Behaviour> NpcComps<T>() where T : Behaviour
            {
                var list = new List<Behaviour>();
                foreach (GameObject n in npcs) list.AddRange(n.GetComponentsInChildren<T>());
                return list;
            }
            parts.Add(("NPC-Tagesablauf (NpcRoutine)", null, NpcComps<Ashveil.Gameplay.Npcs.NpcRoutine>()));
            parts.Add(("NPC-Emotes (EmotePoser)", null, NpcComps<EmotePoser>()));
            parts.Add(("NPC-Haltung (StancePoser)", null, NpcComps<StancePoser>()));
            parts.Add(("NPC-Animatoren", null, NpcComps<Animator>()));
            parts.Add(("NPC-Figurendetail (CharacterLod)", null, NpcComps<CharacterLod>()));
            parts.Add(("NPC-Ausruestung (AppearanceRig)", null, NpcComps<AppearanceRig>()));
            parts.Add(("NPCs im Lager (ganz)", npcs, null));
            var animals = new List<GameObject>();
            foreach (var r in Ashveil.Gameplay.Npcs.AnimalRegistry.All) if (r != null) animals.Add(r.gameObject);
            parts.Add(("Tiere", animals, null));
            var lamps = new List<Behaviour>();
            foreach (LightFlicker f in FindObjectsByType<LightFlicker>())
                if (f.GetComponent<Light>() != null) lamps.Add(f.GetComponent<Light>());
            parts.Add(("Feuer- und Laternenlichter", null, lamps));
            if (DayNightCycle.Active != null) parts.Add(("Tag und Nacht", null, new List<Behaviour> { DayNightCycle.Active }));
            GameObject camp = GameObject.Find("Wachtfeuer (Spawn)");
            if (camp != null)
            {
                var campParts = new List<GameObject>();
                foreach (Transform c in camp.transform)
                    if (c.name != "PlayerSpawn" && c.name != "NPCs (Tagesablaeufe)" && c.GetComponentInChildren<FishNet.Object.NetworkObject>(true) == null) campParts.Add(c.gameObject);
                parts.Add(("Aufbauten des Startlagers", campParts, null));
            }
            GameObject giant = GameObject.Find("Der versteinerte Beschuetzer");
            if (giant != null) parts.Add(("Der versteinerte Beschuetzer", new List<GameObject> { giant }, null));
            foreach ((string name, List<GameObject> objs, List<Behaviour> comps) in parts)
            {
                if (objs != null) foreach (GameObject o in objs) if (o != null) o.SetActive(false);
                if (comps != null) foreach (Behaviour b in comps) if (b != null) b.enabled = false;
                yield return new WaitForSecondsRealtime(1.5f);
                yield return Measure(3f);
                double cpu = _cpuMs / Mathf.Max(1, _statFrames), gpu = _gpuFrames > 0 ? _gpuMs / _gpuFrames : 0;
                results.Add($"    ohne {name,-28} {Avg(),5:0} Bilder/s · CPU {cpu:0.0} ms ({cpu - baseCpu:+0.0;-0.0}) · GPU {gpu:0.0} ms ({gpu - baseGpu:+0.0;-0.0})");
                if (objs != null) foreach (GameObject o in objs) if (o != null) o.SetActive(true);
                if (comps != null) foreach (Behaviour b in comps) if (b != null) b.enabled = true;
                yield return new WaitForSecondsRealtime(1f);
            }

            // Dynamic Sky: Kosten je Darstellungsstufe und ganz ohne Himmel (Shader + Steuerung)
            var director = Ashveil.Rendering.Sky.SkyDirector.Active;
            if (director != null && RenderSettings.skybox != null)
            {
                Ashveil.Rendering.Sky.SkyQuality q0 = Ashveil.Rendering.Sky.SkyDirector.Quality;
                foreach (Ashveil.Rendering.Sky.SkyQuality q in new[] { Ashveil.Rendering.Sky.SkyQuality.Low, Ashveil.Rendering.Sky.SkyQuality.Medium, Ashveil.Rendering.Sky.SkyQuality.High })
                {
                    Ashveil.Rendering.Sky.SkyDirector.Quality = q;
                    yield return new WaitForSecondsRealtime(1f);
                    yield return Measure(3f);
                    double cq = _cpuMs / Mathf.Max(1, _statFrames), gq = _gpuFrames > 0 ? _gpuMs / _gpuFrames : 0;
                    results.Add($"    Himmel Stufe {q,-22} {Avg(),5:0} Bilder/s · CPU {cq:0.0} ms ({cq - baseCpu:+0.0;-0.0}) · GPU {gq:0.0} ms ({gq - baseGpu:+0.0;-0.0})");
                }
                Ashveil.Rendering.Sky.SkyDirector.Quality = q0;
                Material sky = RenderSettings.skybox;
                RenderSettings.skybox = null;
                director.enabled = false;
                yield return new WaitForSecondsRealtime(1.5f);
                yield return Measure(3f);
                double c2 = _cpuMs / Mathf.Max(1, _statFrames), g2 = _gpuFrames > 0 ? _gpuMs / _gpuFrames : 0;
                results.Add($"    ohne {"Himmel (Shader + Steuerung)",-28} {Avg(),5:0} Bilder/s · CPU {c2:0.0} ms ({c2 - baseCpu:+0.0;-0.0}) · GPU {g2:0.0} ms ({g2 - baseGpu:+0.0;-0.0})");
                RenderSettings.skybox = sky;
                director.enabled = true;
                yield return new WaitForSecondsRealtime(1f);
            }
        }

        private IEnumerator Measure(float seconds)
        {
            _frameTimes.Clear();
            _cpuMs = _gpuMs = _drawCalls = _batches = _tris = _skinned = 0;
            _statFrames = _gpuFrames = 0;
            using (ProfilerRecorder main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1))
            using (ProfilerRecorder draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count"))
            using (ProfilerRecorder batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count"))
            using (ProfilerRecorder tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count"))
            using (ProfilerRecorder skinned = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Visible Skinned Meshes Count"))
            {
                float end = Time.realtimeSinceStartup + seconds;
                while (Time.realtimeSinceStartup < end)
                {
                    yield return null;
                    _frameTimes.Add(Time.unscaledDeltaTime);
                    _statFrames++;
                    double mainMs = main.Valid ? main.LastValue * 1e-6 : 0.0;
                    if (draws.Valid) _drawCalls += draws.LastValue;
                    if (batches.Valid) _batches += batches.LastValue;
                    if (tris.Valid) _tris += tris.LastValue;
                    if (skinned.Valid) _skinned += skinned.LastValue;
                    FrameTimingManager.CaptureFrameTimings();
                    bool timed = FrameTimingManager.GetLatestTimings(1, _timing) > 0;
                    // CPU = Hauptthread ohne Warten auf Bildausgabe/GPU (sonst misst man nur die Bildrate)
                    double cpu = timed && _timing[0].cpuMainThreadFrameTime > 0.0
                        ? _timing[0].cpuMainThreadFrameTime - _timing[0].cpuMainThreadPresentWaitTime
                        : mainMs;
                    _cpuMs += cpu;
                    if (timed && _timing[0].gpuFrameTime > 0.0)
                    {
                        _gpuMs += _timing[0].gpuFrameTime;
                        _gpuFrames++;
                    }
                }
            }
        }

        private string Stats()
        {
            int n = Mathf.Max(1, _statFrames);
            string gpu = _gpuFrames > 0 ? $"{_gpuMs / _gpuFrames:0.0} ms" : "n. v.";
            // Drawcall-Zaehler gibt es nur im Editor/Entwicklungs-Build – sonst weglassen
            string draws = _drawCalls > 0 ? $" · {_drawCalls / n:0} Drawcalls · {_batches / n:0} Batches" : "";
            // Beim Rendern in ein eigenes Bild (4K/8K-Messung) liefert die Frame-Zeitmessung keinen gueltigen CPU-Wert
            string cpu = _cpuMs > 0 ? $"{_cpuMs / n:0.0} ms" : "n. v.";
            return $"CPU {cpu} · GPU {gpu}{draws} · {_tris / n / 1000:0}k Dreiecke · {_skinned / n:0} Haut-Meshes";
        }

        /// <summary>
        /// Grobe Hochrechnung auf die Zielhardware. Faktoren: GPU RTX 5080 → RTX 3060 ≈ 3,0x langsamer (Rasterleistung),
        /// Pixel ∝ Aufloesung; CPU Core Ultra 9 285K → 6-Kern-Mittelklasse (Ryzen 5 5600 / i5-12400) ≈ 1,6x langsamer.
        /// Nur fuer die Groessenordnung – echte Messung auf einer 3060 ersetzt das nicht.
        /// </summary>
        private string Estimate()
        {
            if (_gpuFrames == 0 || _statFrames == 0) return "";
            double cpu = _cpuMs / _statFrames, gpu = _gpuMs / _gpuFrames;
            double pixelFactor = (1920.0 * 1080.0) / Mathf.Max(1, Screen.width * Screen.height);
            bool is5080 = SystemInfo.graphicsDeviceName.Contains("5080");
            if (!is5080) return "";
            double gpu3060 = gpu * 3.0 * pixelFactor;
            double cpuMid = cpu * 1.6;
            double fps = 1000.0 / System.Math.Max(gpu3060, cpuMid);
            return $"≈ RTX 3060 + 6-Kerner @1080p: GPU {gpu3060:0.0} ms, CPU {cpuMid:0.0} ms → ~{fps:0} Bilder/s";
        }

        private float Avg()
        {
            if (_frameTimes.Count == 0) return 0f;
            float sum = 0f;
            foreach (float f in _frameTimes) sum += f;
            return _frameTimes.Count / Mathf.Max(0.0001f, sum);
        }

        private float Low1()
        {
            if (_frameTimes.Count == 0) return 0f;
            var sorted = new List<float>(_frameTimes);
            sorted.Sort();
            int n = Mathf.Max(1, sorted.Count / 100);
            float sum = 0f;
            for (int i = sorted.Count - n; i < sorted.Count; i++) sum += sorted[i];
            return n / Mathf.Max(0.0001f, sum);
        }

        // ================================================================== 50 Figuren

        private void SpawnCrowd(PlayerCharacter player)
        {
            AppearanceCatalog cat = GameDataBootstrap.Catalog;
            Animator playerAnim = player.GetComponentInChildren<Animator>();
            RuntimeAnimatorController controller = playerAnim != null ? playerAnim.runtimeAnimatorController : cat != null ? cat.previewController : null;
            if (cat == null || cat.bodies.Count == 0) return;

            // Vor der Kamera, damit alle im Bild sind
            Camera cam = Camera.main;
            Vector3 forward = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : Vector3.forward;
            _crowdCenter = player.transform.position + forward * 22f;

            var armor = new List<ItemDefinition>[9];
            var weapons = new List<ItemDefinition>();
            for (int s = 0; s < armor.Length; s++) armor[s] = new List<ItemDefinition>();
            if (GameData.Database != null)
                foreach (GameDefinition d in GameData.Database.All)
                {
                    if (!(d is ItemDefinition item) || item.appearance == null) continue;
                    for (int s = 0; s < armor.Length; s++) if (item.FitsSlot((EquipmentSlot)s)) armor[s].Add(item);
                    if (item.FitsSlot(EquipmentSlot.MainHand)) weapons.Add(item);
                }

            var rng = new System.Random(4711);
            var root = new GameObject("Lasttest-Figuren").transform;
            for (int i = 0; i < FigureCount; i++)
            {
                int body = i % Mathf.Min(2, cat.bodies.Count);
                GameObject prefab = cat.bodies[body].model;
                if (prefab == null) continue;
                var holder = new GameObject($"Lastfigur {i + 1:00}").transform;
                holder.SetParent(root, false);
                GameObject model = Instantiate(prefab, holder, false);
                Animator anim = model.GetComponentInChildren<Animator>(true) ?? model.AddComponent<Animator>();
                anim.applyRootMotion = false;
                if (controller != null) anim.runtimeAnimatorController = controller;
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                _figureAnims.Add(anim);

                var look = new CharacterAppearanceData
                {
                    body = body,
                    skin = rng.Next(0, cat.SkinCount(body)),
                    hair = rng.Next(0, Mathf.Max(1, cat.hairstyles.Count)),
                    hairColor = rng.Next(0, Mathf.Max(1, cat.hairColors.Count)),
                    beard = body == 0 ? rng.Next(0, Mathf.Max(1, cat.beards.Count)) : 0,
                    face = rng.Next(0, Mathf.Max(1, cat.faces.Count)),       // M7: Gesichtsebenen mitmessen
                    eyeColor = rng.Next(0, Mathf.Max(1, cat.eyeColors.Count)),
                    feature = rng.Next(0, Mathf.Max(1, cat.features.Count))
                };
                look.EnsureArrays();
                cat.Sanitize(look);
                var equipment = new ItemStack[CharacterAppearanceData.SlotCount];
                for (int s = 0; s < armor.Length; s++)
                    if (armor[s].Count > 0 && rng.NextDouble() < 0.75) equipment[s] = new ItemStack(armor[s][rng.Next(armor[s].Count)].Id, 1);
                if (weapons.Count > 0) equipment[(int)EquipmentSlot.MainHand] = new ItemStack(weapons[rng.Next(weapons.Count)].Id, 1);
                var rig = model.AddComponent<AppearanceRig>();
                rig.Init(holder);
                rig.Apply(EquipmentVisuals.Resolve(equipment, look, cat), look, cat);
                rig.PlaceWeapons(null, null);

                var label = holder.gameObject.AddComponent<WorldLabel>();
                label.Configure($"Testfigur {i + 1}", "", WorldLabelStyle.Friendly, 40f);
                holder.gameObject.AddComponent<CharacterLod>();
                _figures.Add(holder);
            }
        }

        private void Update()
        {
            // Figuren laufen in Kreisen um die Mitte (verschiedene Radien und Richtungen)
            float time = Time.time;
            for (int i = 0; i < _figures.Count; i++)
            {
                Transform f = _figures[i];
                if (f == null) continue;
                float radius = 4f + (i % 10) * 3.2f;
                float dir = i % 2 == 0 ? 1f : -1f;
                float speed = 1.6f + (i % 3) * 0.5f;
                float angle = dir * time * speed / radius + i * 0.7f;
                var pos = new Vector3(_crowdCenter.x + Mathf.Cos(angle) * radius, 0f, _crowdCenter.z + Mathf.Sin(angle) * radius);
                pos.y = Ground(pos);
                Vector3 tangent = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * dir;
                f.SetPositionAndRotation(pos, Quaternion.LookRotation(tangent, Vector3.up));
                Animator a = _figureAnims[i];
                if (a == null || a.runtimeAnimatorController == null) continue;
                a.SetFloat(SpeedId, speed);
                a.SetFloat(MoveZId, 1f);
                a.SetBool(GroundedId, true);
                a.SetBool(WalkingId, speed < 2f);
            }
        }

        private static float Ground(Vector3 p)
        {
            return WorldGround.SampleHeight(p, 0f);
        }

        private void ClearCrowd()
        {
            foreach (Transform f in _figures)
                if (f != null && f.parent != null) Destroy(f.parent.gameObject);
            _figures.Clear();
            _figureAnims.Clear();
        }

        // ================================================================== Bericht

        private void Finish(GraphicsOptions original)
        {
            Application.logMessageReceived -= OnLog;
            GraphicsSettingsStore.Set(original);
            GraphicsSettingsStore.PersistenceEnabled = true;
            Check(_logErrors == 0, "Keine Fehler/Ausnahmen im Log", $"{_logErrors}");
            Line("");
            Line($"ERGEBNIS: {_passed} bestanden, {_failed} fehlgeschlagen, {_logErrors} Fehler/Ausnahmen im Log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath, _report.ToString());
            }
            catch (IOException e)
            {
                Debug.LogWarning("[Ashveil][Leistung] Bericht nicht geschrieben: " + e.Message);
            }
            Debug.Log($"[Ashveil][Leistung] Fertig: {_passed} ok, {_failed} fehlgeschlagen → {ReportPath}");
            if (!Application.isEditor) Application.Quit();
        }

        private void OnLog(string msg, string stack, LogType type)
        {
            if ((type == LogType.Error || type == LogType.Exception) && !msg.Contains("assetPath hash") && !TestLog.IsEditorTooling(msg, stack)) _logErrors++;
        }

        private void Section(string title)
        {
            Line("");
            Line($"[{title}]");
        }

        private void Line(string s)
        {
            _report.AppendLine(s);
            Debug.Log("[Ashveil][Leistung] " + s);
        }

        private void Check(bool ok, string what, string detail = "")
        {
            if (ok) _passed++;
            else _failed++;
            Line($"  {(ok ? "OK  " : "FEHL")}  {what}{(ok || string.IsNullOrEmpty(detail) ? "" : "  (" + detail + ")")}");
        }

        private static PlayerCharacter FindLocalPlayer()
        {
            foreach (PlayerCharacter pc in FindObjectsByType<PlayerCharacter>(FindObjectsInactive.Exclude))
                if (pc.isActiveAndEnabled) return pc;
            return null;
        }
    }
}
