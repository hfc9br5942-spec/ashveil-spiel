using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ashveil.FloraValidation
{
    /// <summary>
    /// Automatischer Messlauf der isolierten Flora-Validation-Szene (FloraValidation_FV1). Wird von
    /// „Ashveil › Flora-Validation › 3 Messlauf starten (Play)“ gestartet und beendet den Play Mode am Ende selbst.
    /// Misst Leistung (CPU/GPU, Batches, SetPass, Dreiecke, Schatten, Speicher, GC), Instancing/SRP-Batcher, LOD-Uebergaenge
    /// (Crossfade an/aus, Pop-Metrik aus Bilddifferenzen), Overdraw, Impostor, Wind und Saison und schreibt Bilder, Clips und
    /// Bericht nach Logs/Ashveil/FloraValidation. Beruehrt keine produktive Szene und keine Projekt-Einstellung dauerhaft.
    /// </summary>
    public partial class FloraValidationRunner : MonoBehaviour
    {
        [Header("Szene (vom Szenenbau gesetzt)")]
        public Camera cam;
        public Light sun;
        public FloraEnvironment env;
        public Transform areaHero, areaGroup, areaHabitat, areaStress, areaInstSame, areaInstSeeds, areaInstMixed, areaLineupF01, areaLineupF04;
        public Transform areaF10Full, areaF10Lod, areaF19Full, areaF19Lod;
        public bool autoRun = true;
        [Tooltip("0 = Messlauf (Validierung), 1 = v3-Kalibrierung, 2 = GPU-Resident-Drawer-Test")]
        public int mode;
        [Tooltip("Nur GRD-Test: Kopie des PC-Pipeline-Assets mit GPU Resident Drawer (wird nur zur Laufzeit gesetzt und zurueckgestellt)")]
        public RenderPipelineAsset grdPipeline;
        [Tooltip("Kurzlauf: weniger Messzeit und kuerzere Clips (Funktionstest)")]
        public bool quick;

        [Header("Shader")]
        public Shader debugShader;

        private readonly List<Transform> _areas = new List<Transform>();
        private readonly List<GameObject> _grounds = new List<GameObject>();
        private string _out;
        private FloraReport _report = new FloraReport();
        private readonly StringBuilder _log = new StringBuilder();
        private readonly FrameTiming[] _timing = new FrameTiming[1];
        private double[] _ft = new double[6];

        /// <summary>Profiler-Marker unabhaengig von der Kategorie finden (Development Player und Editor).</summary>
        private static ProfilerRecorder StartAny(string name)
        {
            foreach (ProfilerCategory c in new[] { ProfilerCategory.Internal, ProfilerCategory.Scripts, ProfilerCategory.Render })
            {
                var r = ProfilerRecorder.StartNew(c, name, 1);
                if (r.Valid) return r;
                r.Dispose();
            }
            return ProfilerRecorder.StartNew(ProfilerCategory.Internal, name, 1);
        }
        private bool _srpBatcherInitial;
        private int _vSyncInitial, _targetFpsInitial;

        private void Start()
        {
            if (!autoRun) return;
            StartCoroutine(RunAll());
        }

        private IEnumerator RunAll()
        {
#if !UNITY_EDITOR
            // Development Player Build: Standard ist der Player-Messlauf (Modus 3); "-fvMode <n>" ueberschreibt.
            mode = 3;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-fvMode" && int.TryParse(args[i + 1], out int m)) mode = m;
            if (mode == 1 || mode == 2) mode = 3;   // Kalibrierung/GRD nur im Editor
#endif
            _out = Ashveil.Core.ProjectPaths.Logs("FloraValidation");
            // v3: Kalibrier- und GRD-Lauf in eigene Unterordner, damit der Messlauf (Modus 0) direkt mit v1 vergleichbar bleibt.
            if (mode == 1) _out = Path.Combine(_out, "Kalibrierung_v6_StepE");
            else if (mode == 2) _out = Path.Combine(_out, "GRD_Test");
            else if (mode == 3) _out = Path.Combine(_out, "PlayerBuild");
            else if (mode == 5) _out = Path.Combine(_out, "Blind_StepE");
            Directory.CreateDirectory(_out);
            Directory.CreateDirectory(Path.Combine(_out, "Bilder"));
            Directory.CreateDirectory(Path.Combine(_out, "Clips"));
            // Kalibrierbereich nur im Kalibrierlauf: sonst entfernen, damit Speicher-/Leistungswerte mit v1 vergleichbar bleiben
            if (mode != 1 && areaCalib != null)
            {
                Destroy(areaCalib.gameObject); areaCalib = null;
                yield return null;
                yield return Resources.UnloadUnusedAssets();
            }
            _areas.Clear();
            foreach (Transform t in new[] { areaHero, areaGroup, areaHabitat, areaStress, areaInstSame, areaInstSeeds, areaInstMixed, areaLineupF01, areaLineupF04,
                                             areaF10Full, areaF10Lod, areaF19Full, areaF19Lod, areaCalib })
                if (t != null) _areas.Add(t);
            _grounds.Clear();
            foreach (Transform a in _areas) { Transform g = a.Find(FloraValidationLayout.GroundName); if (g != null) _grounds.Add(g.gameObject); }
            _srpBatcherInitial = GraphicsSettings.useScriptableRenderPipelineBatching;
            _vSyncInitial = QualitySettings.vSyncCount; _targetFpsInitial = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            FillHardware();
            // LOD-Abstaende normieren: Prefabs sind fuer LOD-Bias 1 gebaut; zur Laufzeit gilt der Bias der aktiven Grafikstufe.
            // Nur Laufzeitkopie der Szene (Play Mode), keine Asset- oder Projektaenderung.
            float lb = Mathf.Max(0.01f, QualitySettings.lodBias); int ng = 0;
            foreach (Transform a in _areas)
                foreach (LODGroup g in a.GetComponentsInChildren<LODGroup>(true)) { g.size /= lb; ng++; }
            Log($"LOD-Bias zur Laufzeit {lb:0.00} -> {ng} LODGroups auf die geplanten Abstaende normiert (35/80/160 m Baeume, 18/50 m F10, 12/30 m F19)");
            Log($"Flora-Validation gestartet {DateTime.Now:yyyy-MM-dd HH:mm} · {_report.hardware.gpu} · {_report.hardware.cpu} · Bild {Screen.width}x{Screen.height}");

            bool ok = true;
            IEnumerator[] steps = mode == 1 ? new[] { Step("Micro-Pass-Kalibrierung (Impostor-Beleuchtung)", CalibrationSuiteV5()) }
                : mode == 2 ? new[] { Step("GPU Resident Drawer", GrdSuite()) }
                : mode == 5 ? new[] { Step("Micro-Pass: Blindvergleich und Crossfade-Kosten", BlindCrossfadeSuite()) }
                : mode == 3 ? new IEnumerator[]
                {
                    // Development Player Build: dieselben Abschnitte wie im Editor-Messlauf, ohne Bildvergleiche
                    Step("Leistung", PerfSuite()),
                    Step("LOD-Fahrten", LodDriveSuite()),
                    Step("Wind", WindSuite()),
                    Step("Saison", SeasonSuite()),
                    Step("Schnelle Bewegung", FastMoveSuite()),
                    Step("Speicher", MemorySuite()),
                }
                : new IEnumerator[]
            {
                Step("Leistung", PerfSuite()),
                Step("Instancing", InstancingSuite()),
                Step("Schatten", ShadowSuite()),
                Step("LOD-Vergleich (statisch)", LodStaticSuite()),
                Step("LOD-Fahrten", LodDriveSuite()),
                Step("LOD-Pop am Umschaltpunkt", PopSuite()),
                Step("Micro-Pass: Blindvergleich und Crossfade-Kosten", BlindCrossfadeSuite()),
                Step("Impostor", ImpostorSuite()),
                Step("Abnahmebilder", ScreenshotSuite()),
                Step("Wind", WindSuite()),
                Step("Saison", SeasonSuite()),
                Step("Overdraw", OverdrawSuite()),
                Step("LOD-/Schatten-Debug", DebugViewSuite()),
                Step("Schnelle Bewegung", FastMoveSuite()),
                Step("Speicher", MemorySuite()),
            };
            foreach (IEnumerator s in steps)
            {
                yield return s;
            }
            GraphicsSettings.useScriptableRenderPipelineBatching = _srpBatcherInitial;
            QualitySettings.vSyncCount = _vSyncInitial; Application.targetFrameRate = _targetFpsInitial;
            Time.captureFramerate = 0;
            WriteReport(ok);
            Log("Fertig.");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#else
            Application.Quit();
#endif
        }

        /// <summary>Fuehrt einen Abschnitt aus; Fehler werden protokolliert, der Lauf geht weiter.</summary>
        private IEnumerator Step(string name, IEnumerator inner, bool top = true)
        {
            if (top) Log($"== {name}");
            float t0 = Time.realtimeSinceStartup;
            while (true)
            {
                object cur;
                try
                {
                    if (!inner.MoveNext()) break;
                    cur = inner.Current;
                }
                catch (Exception e)
                {
                    Log($"FEHLER in {name}: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
                    _report.errors.Add($"{name}: {e.Message}");
                    break;
                }
                if (cur is IEnumerator nested) yield return Step(name, nested, false);
                else yield return cur;
            }
            if (top) Log($"   ({Time.realtimeSinceStartup - t0:0.0} s)");
        }

        // ------------------------------------------------------------------ Bereiche / Kamera

        private void SetGrounds(bool on)
        {
            foreach (GameObject g in _grounds) g.SetActive(on);
        }

        private void Activate(params Transform[] keep)
        {
            foreach (Transform a in _areas) a.gameObject.SetActive(Array.IndexOf(keep, a) >= 0);
        }

        private void Pose(Vector3 pos, Vector3 target, float fov = 60f)
        {
            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.LookRotation(target - pos, Vector3.up);
            cam.fieldOfView = fov;
        }

        /// <summary>Typische Spielkamera (MMOCameraController: Drehpunkt 1,65 m, Zoom 7 m, leicht von oben).</summary>
        private void ThirdPerson(Vector3 player, Vector3 lookDir, float zoom = 7f, float pitchDeg = 8f)
        {
            lookDir.y = 0; lookDir.Normalize();
            Vector3 pivot = player + Vector3.up * 1.65f;
            Vector3 back = Quaternion.AngleAxis(pitchDeg, Vector3.Cross(Vector3.up, lookDir)) * -lookDir;
            Vector3 pos = pivot + back * zoom;
            Pose(pos, pivot + lookDir * 18f + Vector3.up * 3.5f, 60f);
        }

        private Transform Find(Transform root, string name)
        {
            Transform t = root.Find(name);
            if (t == null) throw new Exception($"Objekt {name} fehlt unter {root.name}");
            return t;
        }

        // ------------------------------------------------------------------ Messung

        private IEnumerator Measure(string scenario, string variant, float seconds, Func<float, bool> mover = null, Transform lodRoot = null)
        {
            if (quick) seconds = Mathf.Min(seconds, 1.5f);
            yield return new WaitForSecondsRealtime(quick ? 0.3f : 1.0f);   // Einschwingen (Streaming, Shader-Varianten)
            var frames = new List<float>(512);
            double cpu = 0, gpu = 0, mainT = 0; int gpuN = 0, cpuN = 0, n = 0;
            double batches = 0, setpass = 0, draws = 0, tris = 0, verts = 0, shadow = 0, gc = 0;
            long gcMax = 0;
            using (var rMain = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1))
            using (var rBatches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count"))
            using (var rSetPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count"))
            using (var rDraws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count"))
            using (var rTris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count"))
            using (var rVerts = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count"))
            using (var rShadow = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count"))
            using (var rGc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
            using (var rLoop = StartAny("PlayerLoop"))
            using (var rWaitPresent = StartAny("Gfx.WaitForPresentOnGfxThread"))
            using (var rWaitGfx = StartAny("Gfx.WaitForGfxCommandsFromMainThread"))
            {
                double loopMs = 0, waitPMs = 0, cpuMainRaw = 0, presentW = 0, cpuRender = 0, cpuTotal = 0; int ftN = 0;
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < seconds)
                {
                    if (mover != null) mover(Time.realtimeSinceStartup - t0);
                    yield return null;
                    n++;
                    frames.Add(Time.unscaledDeltaTime * 1000f);
                    if (rMain.Valid) mainT += rMain.LastValue * 1e-6;
                    if (rBatches.Valid) batches += rBatches.LastValue;
                    if (rSetPass.Valid) setpass += rSetPass.LastValue;
                    if (rDraws.Valid) draws += rDraws.LastValue;
                    if (rTris.Valid) tris += rTris.LastValue;
                    if (rVerts.Valid) verts += rVerts.LastValue;
                    if (rShadow.Valid) shadow += rShadow.LastValue;
                    if (rGc.Valid) { gc += rGc.LastValue; gcMax = Math.Max(gcMax, rGc.LastValue); }
                    if (rLoop.Valid) loopMs += rLoop.LastValue * 1e-6;
                    if (rWaitPresent.Valid) waitPMs += rWaitPresent.LastValue * 1e-6;
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, _timing) > 0)
                    {
                        if (_timing[0].cpuMainThreadFrameTime > 0) { cpu += _timing[0].cpuMainThreadFrameTime - _timing[0].cpuMainThreadPresentWaitTime; cpuN++; }
                        if (_timing[0].cpuMainThreadFrameTime > 0)
                        {
                            cpuMainRaw += _timing[0].cpuMainThreadFrameTime; presentW += _timing[0].cpuMainThreadPresentWaitTime;
                            cpuRender += _timing[0].cpuRenderThreadFrameTime; cpuTotal += _timing[0].cpuFrameTime; ftN++;
                        }
                        if (_timing[0].gpuFrameTime > 0) { gpu += _timing[0].gpuFrameTime; gpuN++; }
                    }
                }
                int fn = Mathf.Max(1, ftN), ln = Mathf.Max(1, n);
                _ft = new double[] { ftN > 0 ? cpuMainRaw / fn : -1, ftN > 0 ? presentW / fn : -1, ftN > 0 ? cpuRender / fn : -1, ftN > 0 ? cpuTotal / fn : -1,
                                     rLoop.Valid ? loopMs / ln : -1, rWaitPresent.Valid ? waitPMs / ln : -1 };
            }
            n = Mathf.Max(1, n);
            var sorted = new List<float>(frames); sorted.Sort();
            float sum = 0; foreach (float f in frames) sum += f;
            int k1 = Mathf.Max(1, sorted.Count / 100); float worst = 0; for (int i = sorted.Count - k1; i < sorted.Count; i++) worst += sorted[i];
            float median = sorted.Count > 0 ? sorted[sorted.Count / 2] : 0;
            int spikes = 0; foreach (float f in frames) if (f > median * 2f && f > 8f) spikes++;
            var r = new PerfResult
            {
                scenario = scenario, variant = variant, seconds = seconds, frames = frames.Count,
                avgFps = frames.Count / Mathf.Max(0.001f, sum / 1000f), low1Fps = k1 / Mathf.Max(0.001f, worst / 1000f),
                frameMsAvg = sum / Mathf.Max(1, frames.Count), frameMsMax = sorted.Count > 0 ? sorted[sorted.Count - 1] : 0, spikes = spikes,
                cpuMs = cpuN > 0 ? (float)(cpu / cpuN) : (float)(mainT / n), cpuSource = cpuN > 0 ? "FrameTiming (ohne Present-Wartezeit)" : "Main Thread",
                gpuMs = gpuN > 0 ? (float)(gpu / gpuN) : -1,
                batches = (float)(batches / n), setPass = (float)(setpass / n), drawCalls = (float)(draws / n),
                triangles = (float)(tris / n), vertices = (float)(verts / n), shadowCasters = (float)(shadow / n),
                gcBytesPerFrame = (float)(gc / n), gcBytesMax = gcMax,
                srpBatcher = GraphicsSettings.useScriptableRenderPipelineBatching, lodBias = QualitySettings.lodBias,
                screen = $"{Screen.width}x{Screen.height}",
            };
            // v4: belastbare CPU-Werte auch im Player (Marker statt Differenz der FrameTiming-Felder)
            r.cpuMainRawMs = (float)_ft[0]; r.presentWaitMs = (float)_ft[1]; r.cpuRenderMs = (float)_ft[2]; r.cpuFrameMs = (float)_ft[3];
            r.playerLoopMs = (float)_ft[4]; r.waitPresentMarkerMs = (float)_ft[5];
            // v4: FrameTiming.cpuMainThreadFrameTime enthaelt die Present-Wartezeit NICHT (Main + PresentWait ≈ Frame, Player-Messung 01.10.);
            // v3 zog sie ein zweites Mal ab (negative Werte). PlayerLoop-Marker nur Diagnose (enthaelt GPU-Wartezeit).
            r.cpuMainWorkMs = r.cpuMainRawMs > 0 ? r.cpuMainRawMs : ((_ft[4] > 0 && _ft[5] > 0.01) ? (float)(_ft[4] - _ft[5]) : -1);
            if (lodRoot != null) r.lodDistribution = LodDistribution(lodRoot);
            _report.perf.Add(r);
            Log($"   {scenario,-28} {variant,-26} {r.avgFps,6:0} fps · 1% {r.low1Fps,5:0} · Frame {r.frameMsAvg,5:0.00} · MainArbeit {r.cpuMainWorkMs,5:0.00} · Render {r.cpuRenderMs,5:0.00} · CPU {r.cpuMs,5:0.00} · GPU {(r.gpuMs < 0 ? "n.v." : r.gpuMs.ToString("0.00")),5} ms · " +
                $"DrawCalls {r.drawCalls,6:0} · SetPass {r.setPass,5:0} · Tris {r.triangles / 1e6f,5:0.00} M · Schatten {r.shadowCasters,5:0} · GC {r.gcBytesPerFrame,6:0} B · Spitzen {spikes} · {r.lodDistribution}");
        }

        /// <summary>LOD-Verteilung nach derselben Formel wie Unity (relative Hoehe = Groesse / (Abstand · 2 tan(fov/2)) · lodBias).</summary>
        private string LodDistribution(Transform root)
        {
            var counts = new int[6];
            float k = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            foreach (LODGroup g in root.GetComponentsInChildren<LODGroup>(false))
            {
                LOD[] lods = g.GetLODs();
                Vector3 wc = g.transform.TransformPoint(g.localReferencePoint);
                float scale = Mathf.Max(Mathf.Abs(g.transform.lossyScale.x), Mathf.Max(Mathf.Abs(g.transform.lossyScale.y), Mathf.Abs(g.transform.lossyScale.z)));
                float d = Vector3.Distance(cam.transform.position, wc);
                float rel = g.size * scale / Mathf.Max(0.01f, d * k) * QualitySettings.lodBias;
                int lvl = lods.Length;     // = ausgeblendet
                for (int i = 0; i < lods.Length; i++) if (rel >= lods[i].screenRelativeTransitionHeight) { lvl = i; break; }
                counts[Mathf.Min(lvl, 5)]++;
            }
            return $"LOD0 {counts[0]} · LOD1 {counts[1]} · LOD2 {counts[2]} · LOD3 {counts[3]} · aus {counts[4] + counts[5]}";
        }

        // ------------------------------------------------------------------ Bericht

        private void FillHardware()
        {
            _report.hardware = new HardwareInfo
            {
                gpu = SystemInfo.graphicsDeviceName, gpuApi = SystemInfo.graphicsDeviceType.ToString(), gpuMemoryMB = SystemInfo.graphicsMemorySize,
                cpu = SystemInfo.processorType, cpuCores = SystemInfo.processorCount, ramMB = SystemInfo.systemMemorySize,
                unity = Application.unityVersion, editor = Application.isEditor, screen = $"{Screen.width}x{Screen.height}",
                quality = QualitySettings.names[QualitySettings.GetQualityLevel()], lodBias = QualitySettings.lodBias,
                urpAsset = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "?",
                lodCrossFade = ReadBoolField(GraphicsSettings.currentRenderPipeline, "m_EnableLODCrossFade"),
                frameTiming = FrameTimingManager.IsFeatureEnabled(),
            };
        }

        private static bool ReadBoolField(object o, string field)
        {
            if (o == null) return false;
            var f = o.GetType().GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            return f != null && f.GetValue(o) is bool b && b;
        }

        private void Log(string s)
        {
            Debug.Log("[FloraValidation] " + s);
            _log.AppendLine(s);
            if (!string.IsNullOrEmpty(_out)) File.WriteAllText(Path.Combine(_out, "FloraValidation_Protokoll.txt"), _log.ToString());
        }

        private void WriteReport(bool ok)
        {
            File.WriteAllText(Path.Combine(_out, "FloraValidation_Bericht.json"), JsonUtility.ToJson(_report, true));
            File.WriteAllText(Path.Combine(_out, "FloraValidation_Protokoll.txt"), _log.ToString());
        }
    }

    [Serializable]
    public class FloraReport
    {
        public HardwareInfo hardware;
        public List<PerfResult> perf = new List<PerfResult>();
        public List<PopResult> pops = new List<PopResult>();
        public List<StaticLodResult> lodStatic = new List<StaticLodResult>();
        public List<OverdrawResult> overdraw = new List<OverdrawResult>();
        public List<MemoryResult> memory = new List<MemoryResult>();
        public List<string> images = new List<string>();
        public List<string> errors = new List<string>();
    }

    [Serializable]
    public class HardwareInfo
    {
        public string gpu, gpuApi, cpu, unity, screen, quality, urpAsset;
        public int gpuMemoryMB, cpuCores, ramMB;
        public bool editor, lodCrossFade, frameTiming;
        public float lodBias;
    }

    [Serializable]
    public class PerfResult
    {
        public string scenario, variant, cpuSource, lodDistribution, screen;
        public float seconds, avgFps, low1Fps, frameMsAvg, frameMsMax, cpuMs, gpuMs, batches, setPass, drawCalls, triangles, vertices, shadowCasters, gcBytesPerFrame, lodBias;
        /// <summary>v4: FrameTiming-Rohwerte und Marker (PlayerLoop − Gfx.WaitForPresentOnGfxThread = Hauptthread-Arbeit).</summary>
        public float cpuMainRawMs, presentWaitMs, cpuRenderMs, cpuFrameMs, playerLoopMs, waitPresentMarkerMs, cpuMainWorkMs;
        public long gcBytesMax;
        public int frames, spikes;
        public bool srpBatcher;
    }

    [Serializable]
    public class PopResult
    {
        public string asset, mode, clip;
        public int frames;
        public float medianDiff, maxDiff, popScore, p99Diff, coverage, jumpInTree;
        public List<float> diffs = new List<float>();
    }

    [Serializable]
    public class StaticLodResult
    {
        public string asset, transition;
        public float distance, iou, dLum, areaChange;
        public int pxHeight;
    }

    [Serializable]
    public class OverdrawResult
    {
        public string view;
        public float avgLayersCovered, avgLayersScreen, coverage, pctOver4, pctOver8, pctOver16, maxLayers;
        public string image;
    }

    [Serializable]
    public class MemoryResult
    {
        public string group;
        public int meshes, textures;
        public long meshBytes, textureBytes;
        public float totalUsedMB, gfxUsedMB, textureMB, meshMB;
    }
}
