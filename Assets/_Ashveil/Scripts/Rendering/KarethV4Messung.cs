using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Ashveil.Rendering
{
    /// <summary>
    /// Leistungsmessung Kareth v4 im fertigen Spiel (CORE-005, 04.10.2026). Wird nur vom Editor-Menü
    /// „Ashveil › Welt › Materialien (PBR) › 10 Kareth v4: Build-Messung“ für einen eigenen Mess-Build in die Szene gesetzt
    /// (Builds/Messung_KarethV4/), nie in Spiel-Builds. Läuft im Editor nicht.
    /// Fährt die Blickpunkte ab und misst je Variante: Bildrate, 1 %-Tiefstwert, längstes Bild, CPU-Hauptthread, Render-Thread,
    /// GPU, Dreiecke, Draw Calls, Speicher. Varianten: Boden kachelfrei an / aus (gleicher Shader, nur der Schalter).
    /// Bericht neben der .exe: KV4_Messung.txt (+ Bilder).
    /// </summary>
    public sealed class KarethV4Messung : MonoBehaviour
    {
        public string[] names = new string[0];
        public Vector3[] eyes = new Vector3[0];
        public Vector3[] targets = new Vector3[0];
        public float warmup = 1.5f, seconds = 4f;

        private readonly StringBuilder _report = new StringBuilder();
        private readonly List<float> _frames = new List<float>(2048);
        private readonly FrameTiming[] _timing = new FrameTiming[1];
        private double _cpu, _render, _gpu, _tris, _batches;
        private int _n, _gpuN, _renderN;
        private long _sys, _gfx, _tex;
        private int _errors;

        private string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private void Awake()
        {
            if (Application.isEditor) { enabled = false; return; }
        }

        private IEnumerator Start()
        {
            if (Application.isEditor) yield break;
            // #29: Zeit vom Programmstart bis zum ersten Start() der Szene (Laden inkl. Kollisionsnetze, wenn nicht vorgebacken)
            float ladezeit = Time.realtimeSinceStartup;
            Application.logMessageReceived += (m, s, t) => { if (t == LogType.Error || t == LogType.Exception) _errors++; };
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Line($"ASHVEIL Kareth v4 – Leistungsmessung im fertigen Spiel · {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Line($"  Grafikkarte: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB) · {SystemInfo.graphicsDeviceType}");
            Line($"  Prozessor:   {SystemInfo.processorType} ({SystemInfo.processorCount} Threads) · RAM {SystemInfo.systemMemorySize} MB");
            Line($"  Bild:        {Screen.width} x {Screen.height} · {(Debug.isDebugBuild ? "Entwicklungs-Build" : "Release-Build")} · Qualitätsstufe {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
            Line($"  Ladezeit:    {ladezeit:0.0} s vom Programmstart bis zur fertigen Szene");

            var camGo = new GameObject("Messkamera");
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.2f; cam.farClipPlane = 5000f; cam.fieldOfView = 62f; cam.allowHDR = true;
            camGo.tag = "MainCamera";
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true; camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            Terrain terrain = Terrain.activeTerrain;
            Material mat = terrain != null ? terrain.materialTemplate : null;
            bool hasSwitch = mat != null && mat.HasProperty("_AshKachelfrei");
            Line($"  Terrain-Material: {(mat != null ? mat.shader.name : "-")} · {(terrain != null ? terrain.terrainData.terrainLayers.Length : 0)} Layer · Schalter kachelfrei {(hasSwitch ? "ja" : "nein")}");
            Line($"  Szene: {FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude).Length} Mesh-Renderer");
            float origMakro = hasSwitch ? mat.GetFloat("_AshMakroStaerke") : 0f;
            Vector4 origTon = hasSwitch ? mat.GetVector("_AshMakroTon") : Vector4.zero;
            yield return new WaitForSecondsRealtime(3f);

            var sumA = new Sum(); var sumB = new Sum();
            for (int i = 0; i < eyes.Length; i++)
            {
                Line("");
                Line($"[{names[i]}]");
                cam.transform.SetPositionAndRotation(eyes[i], Quaternion.LookRotation(targets[i] - eyes[i]));
                for (int v = 0; v < (hasSwitch ? 2 : 1); v++)
                {
                    if (hasSwitch)
                    {
                        bool an = v == 0;
                        mat.SetFloat("_AshKachelfrei", an ? 1f : 0f);
                        mat.SetFloat("_AshMakroStaerke", an ? origMakro : 0f);
                        mat.SetVector("_AshMakroTon", an ? origTon : new Vector4(origTon.x, origTon.y, origTon.z, 0f));
                    }
                    float t0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t0 < warmup) yield return null;
                    yield return Measure(seconds);
                    string label = hasSwitch ? (v == 0 ? "kachelfrei an " : "kachelfrei aus") : "             ";
                    Line($"  {label} {Fps(),5:0} Bilder/s · 1 %: {Low(0.01f),4:0} · längstes {MaxMs(),5:0.0} ms · CPU {Avg(_cpu, _n),5:0.00} · " +
                         $"Render-Thread {Avg(_render, _renderN),5:0.00} · GPU {Avg(_gpu, _gpuN),5:0.00} ms · {_tris / Mathf.Max(1, _n) / 1000.0,5:0}k Dreiecke · " +
                         $"{_batches / Mathf.Max(1, _n),5:0} Draw Calls");
                    (v == 0 ? sumA : sumB).Add(Avg(_gpu, _gpuN), Avg(_cpu, _n), Fps());
                    if (i == 0 || i == eyes.Length - 1)
                    {
                        ScreenCapture.CaptureScreenshot(Path.Combine(Dir, $"KV4_{i + 1:00}_{(v == 0 ? "an" : "aus")}.png"));
                        yield return null; yield return null;
                    }
                }
            }
            if (hasSwitch) { mat.SetFloat("_AshKachelfrei", 1f); mat.SetFloat("_AshMakroStaerke", origMakro); mat.SetVector("_AshMakroTon", origTon); }

            Line("");
            Line($"Mittel über {eyes.Length} Blickpunkte: kachelfrei an GPU {sumA.Gpu:0.00} ms · CPU {sumA.Cpu:0.00} ms · {sumA.Fps:0} Bilder/s");
            if (hasSwitch)
                Line($"                                 kachelfrei aus GPU {sumB.Gpu:0.00} ms · CPU {sumB.Cpu:0.00} ms · {sumB.Fps:0} Bilder/s · Mehrkosten GPU {sumA.Gpu - sumB.Gpu:+0.00;-0.00} ms");
            Line($"Speicher am Ende: System {_sys / 1048576} MB · Grafik {_gfx / 1048576} MB · Texturen {_tex / 1048576} MB");
            Line($"Fehler/Ausnahmen im Log: {_errors}");
            try { File.WriteAllText(Path.Combine(Dir, "KV4_Messung.txt"), _report.ToString()); } catch (IOException) { }
            Application.Quit();
        }

        private sealed class Sum
        {
            private double g, c, f; private int n;
            public void Add(double gpu, double cpu, double fps) { g += gpu; c += cpu; f += fps; n++; }
            public double Gpu => n > 0 ? g / n : 0; public double Cpu => n > 0 ? c / n : 0; public double Fps => n > 0 ? f / n : 0;
        }

        private IEnumerator Measure(float secs)
        {
            _frames.Clear();
            _cpu = _render = _gpu = _tris = _batches = 0; _n = _gpuN = _renderN = 0;
            using (var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count"))
            using (var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count"))
            using (var sys = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory"))
            using (var gfx = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory"))
            using (var tex = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Texture Memory"))
            {
                float end = Time.realtimeSinceStartup + secs;
                while (Time.realtimeSinceStartup < end)
                {
                    yield return null;
                    _frames.Add(Time.unscaledDeltaTime); _n++;
                    if (tris.Valid) _tris += tris.LastValue;
                    if (batches.Valid) _batches += batches.LastValue;
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, _timing) > 0)
                    {
                        FrameTiming f = _timing[0];
                        if (f.cpuMainThreadFrameTime > 0.0) _cpu += f.cpuMainThreadFrameTime - f.cpuMainThreadPresentWaitTime;
                        if (f.cpuRenderThreadFrameTime > 0.0) { _render += f.cpuRenderThreadFrameTime; _renderN++; }
                        if (f.gpuFrameTime > 0.0 && f.gpuFrameTime < 1000.0) { _gpu += f.gpuFrameTime; _gpuN++; }
                    }
                }
                _sys = sys.Valid ? sys.LastValue : 0; _gfx = gfx.Valid ? gfx.LastValue : 0; _tex = tex.Valid ? tex.LastValue : 0;
            }
        }

        private float Fps() { float s = 0f; foreach (float f in _frames) s += f; return _frames.Count / Mathf.Max(0.0001f, s); }
        private float Low(float fraction)
        {
            if (_frames.Count == 0) return 0f;
            var sorted = new List<float>(_frames); sorted.Sort();
            int n = Mathf.Max(1, Mathf.RoundToInt(sorted.Count * fraction)); float s = 0f;
            for (int i = sorted.Count - n; i < sorted.Count; i++) s += sorted[i];
            return n / Mathf.Max(0.0001f, s);
        }
        private float MaxMs() { float m = 0f; foreach (float f in _frames) m = Mathf.Max(m, f); return m * 1000f; }
        private static double Avg(double s, int n) => n > 0 ? s / n : 0.0;
        private void Line(string s) { _report.AppendLine(s); Debug.Log("[Ashveil][KV4-Messung] " + s); }
    }
}
