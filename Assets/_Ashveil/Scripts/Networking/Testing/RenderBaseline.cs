using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Ashveil.Core;
using Ashveil.Core.Settings;
using Ashveil.Gameplay.CameraSystem;
using Ashveil.Gameplay.Characters;
using Unity.Profiling;
using UnityEngine;
using Ashveil.Rendering;

namespace Ashveil.Networking
{
    /// <summary>
    /// Rendering-Bibel Phase B: Leistungs-Grundlinie im fertigen Spiel. Die Kamera faehrt feste Blickpunkte ab
    /// (Startlager, Wald, Glockenmoor, Aschenfeld, Kolosseum-Vorplatz, Klippenschenke im Grenzgebirge, Salzgruben,
    /// Arena) und misst je Grafikstufe: Bildrate, 1 %/0,1 % tiefste, laengstes Bild, CPU-Hauptthread, Render-Thread,
    /// GPU, Dreiecke, Haut-Meshes, Speicher; einmal je Ort: sichtbare Renderer, Lichter (mit Schatten), Partikel,
    /// und die Spitze beim ersten Besuch (Shader/Streaming). Dazu je Ort ein Bild in Mittel und Episch
    /// (Golden-Scene-Referenz). Start: Spiel mit -ashveil-host -ashveil-renderbench.
    /// Bericht: Logs/Ashveil/Ashveil_RenderBaseline.txt (+ .csv, Bilder in Logs/Ashveil/RenderBaseline/).
    /// Zweiter Modus (-ashveil-renderbench-gpu, Rendering-Bibel §21/§22, Roadmap-Schritt 1): dieselben Orte in Mittel und
    /// Episch, je dreimal – GPU Resident Drawer aus / an / an + GPU-Occlusion-Culling – mit Bildvergleich (fehlt etwas?).
    /// Bericht: Logs/Ashveil/Ashveil_RenderGpuDriven.txt (+ .csv).
    /// </summary>
    public sealed class RenderBaseline : MonoBehaviour
    {
        internal struct Station
        {
            public string Name;
            public Vector3 Eye;      // y < -9000: Bodenhoehe + 1,8 m
            public Vector3 Target;   // y < -9000: Bodenhoehe + 2 m
            public Vector3 Stand;    // wo die eigene Figur steht (Streaming, Figurendetail)
        }

        private const float Ground = -9999f;

        internal static readonly Station[] Stations =
        {
            new Station { Name = "Startlager (Feuerplatz)", Eye = new Vector3(-7f, 1.8f, -12f), Target = new Vector3(4f, 3f, 20f), Stand = new Vector3(-7f, 0f, -10f) },
            new Station { Name = "Wald (Kiefernhang)", Eye = new Vector3(-200f, Ground, 150f), Target = new Vector3(-290f, Ground, 235f), Stand = new Vector3(-200f, 0f, 152f) },
            new Station { Name = "Glockenmoor", Eye = new Vector3(40f, Ground, 225f), Target = new Vector3(45f, Ground, 360f), Stand = new Vector3(40f, 0f, 227f) },
            new Station { Name = "Aschenfeld (Krater)", Eye = new Vector3(300f, Ground, -40f), Target = new Vector3(420f, 8f, -150f), Stand = new Vector3(300f, 0f, -38f) },
            new Station { Name = "Kolosseum-Vorplatz", Eye = new Vector3(462f, Ground, 283f), Target = new Vector3(517f, 8f, 285f), Stand = new Vector3(463f, 0f, 283f) },
            new Station { Name = "Klippenschenke (Grenzgebirge)", Eye = Ashveil.World.BorderMountainsFrame.ToWorld(new Vector3(1045f, 424.2f, 750.6f)), Target = Ashveil.World.BorderMountainsFrame.ToWorld(new Vector3(1255f, 66f, 520f)), Stand = Ashveil.World.BorderMountainsFrame.ToWorld(new Vector3(1043f, 422f, 752f)) },
            new Station { Name = "Salzgruben (Salzhalle)", Eye = new Vector3(-26003f, 2.2f, 55f), Target = new Vector3(-25997f, 4f, 85f), Stand = new Vector3(-26003f, 0.1f, 50f) },   // Instanzen seit 30.09. bei x = −26000
            new Station { Name = "Kolosseum-Arena", Eye = new Vector3(-25989.7f, 1.8f, 1171.8f), Target = new Vector3(-26000f, 4f, 1240f), Stand = new Vector3(-25990f, 0.1f, 1174f) },
        };

        private static bool GpuMode;
        private static string ReportPath => ProjectPaths.Logs(GpuMode ? "Ashveil_RenderGpuDriven.txt" : "Ashveil_RenderBaseline.txt");
        private static string CsvPath => ProjectPaths.Logs(GpuMode ? "Ashveil_RenderGpuDriven.csv" : "Ashveil_RenderBaseline.csv");

        private static readonly string[] VariantNames = { "GRD aus", "GRD an", "GRD+Occl" };
        private static string ShotDir => ProjectPaths.Logs("RenderBaseline");

        private readonly StringBuilder _report = new StringBuilder();
        private readonly StringBuilder _csv = new StringBuilder();
        private readonly List<float> _frames = new List<float>(4096);
        private readonly FrameTiming[] _timing = new FrameTiming[1];
        private double _cpu, _render, _gpu, _tris, _skinned;
        private int _n, _gpuN, _renderN;
        private long _sysMem, _gfxMem;
        private int _logErrors;
        private readonly List<string> _firstErrors = new List<string>();
        private static bool _drawerInBuild = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CheckFlag()
        {
            if (!TestBuild.Enabled) return;
            string[] args = Environment.GetCommandLineArgs();
            GpuMode = Array.IndexOf(args, "-ashveil-renderbench-gpu") >= 0;
            bool requested = GpuMode || Array.IndexOf(args, "-ashveil-renderbench") >= 0;
            if (!requested) return;
            ProfileSelection.Override = "perftest"; // vorhandene Messfigur (kein Charakter-Editor)
            var go = new GameObject("Rendering-Grundlinie");
            DontDestroyOnLoad(go);
            go.AddComponent<RenderBaseline>();
        }

        private IEnumerator Start()
        {
            Application.logMessageReceived += OnLog;
            GraphicsOptions original = GraphicsSettingsStore.Current.Clone();
            GraphicsSettingsStore.PersistenceEnabled = false;
            Line(GpuMode
                ? $"ASHVEIL GPU-Driven-Vergleich (Rendering-Bibel §21/§22) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                : $"ASHVEIL Rendering-Grundlinie (Rendering-Bibel Phase B) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Line($"  Grafikkarte: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB) · {SystemInfo.graphicsDeviceType} · " +
                 $"Raytracing {(SystemInfo.supportsRayTracing ? "ja" : "nein")}, Inline-RT {(SystemInfo.supportsInlineRayTracing ? "ja" : "nein")}");
            Line($"  Prozessor:   {SystemInfo.processorType} ({SystemInfo.processorCount} Threads) · RAM {SystemInfo.systemMemorySize} MB");
            Line($"  Bild:        {Screen.width} x {Screen.height} · {(Debug.isDebugBuild ? "Entwicklungs-Build" : "Release-Build")}");
            bool grdOriginal = GpuDrivenSettings.DrawerEnabled;
            // Ohne Drawer gebaut fehlen die DOTS-Shader-Varianten – ihn zur Laufzeit einzuschalten wirft Fehler in jedem Bild.
            _drawerInBuild = grdOriginal;
            bool occOriginal = GpuDrivenSettings.OcclusionEnabled;
            if (GpuMode) Line($"  Ausgangslage: GPU Resident Drawer {(grdOriginal ? "an (Instanced Drawing)" : "aus")}, GPU-Occlusion {(occOriginal ? "an" : "aus")}");
            _csv.AppendLine((GpuMode ? "ort;stufe;variante;" : "ort;stufe;") + "fps;p1_fps;p01_fps;max_ms;cpu_main_ms;render_thread_ms;gpu_ms;tris_k;skinned;sys_mem_mb;gfx_mem_mb");

            PlayerCharacter player = null;
            float wait = 0f;
            while (player == null && wait < 40f)
            {
                player = FindLocalPlayer();
                wait += 0.5f;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            if (player == null)
            {
                Line("FEHLER: keine eigene Spielfigur");
                RestoreGpu(grdOriginal, occOriginal);
                Finish(original);
                yield break;
            }
            yield return new WaitForSecondsRealtime(4f);

            Camera cam = Camera.main;
            var controller = cam != null ? cam.GetComponent<MMOCameraController>() : null;
            if (controller != null) controller.enabled = false;
            var motion = player.GetComponent<NetworkMotion>();
            Directory.CreateDirectory(ShotDir);

            foreach (Station st in Stations)
            {
                Line("");
                Line($"[{st.Name}]");
                // Figur hinstellen (Streaming der Zellen, Instanz-Stimmung), Kamera setzen, erste Bilder beobachten
                if (motion != null)
                {
                    Vector3 stand = st.Stand;
                    stand.y = WorldGround.SampleHeight(stand, stand.y) + 0.2f;
                    motion.ServerTeleport(stand, 0f);
                    WorldStreamer.Instance?.RequestPreload(stand, 30f);
                }
                float settle = 0f;
                while (settle < 12f && WorldStreamer.Instance != null && !WorldStreamer.Instance.IsReadyAt(st.Stand))
                {
                    settle += 0.25f;
                    yield return new WaitForSecondsRealtime(0.25f);
                }
                PlaceCamera(cam, st);
                ApplyUncapped(GraphicsTier.Mittel);
                float firstMax = 0f;
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 2.5f)
                {
                    yield return null;
                    PlaceCamera(cam, st);
                    firstMax = Mathf.Max(firstMax, Time.unscaledDeltaTime);
                }
                Line($"  Erstes Bild dieses Orts: laengstes Bild {firstMax * 1000f:0} ms (Shader, Streaming, Umschalten)");
                Line("  " + SceneCensus(cam));

                if (GpuMode)
                {
                    yield return GpuVariants(cam, st);
                    continue;
                }

                for (int t = 0; t < GraphicsCatalog.TierCount; t++)
                {
                    ApplyUncapped((GraphicsTier)t);
                    float s = 0f;
                    while (s < 2f) { yield return null; PlaceCamera(cam, st); s += Time.unscaledDeltaTime; }
                    yield return Measure(cam, st, 4f);
                    string tier = GraphicsCatalog.TierNames[t];
                    Line($"  {tier,-8} {Fps(),5:0} Bilder/s · 1 %: {Low(0.01f),4:0} · 0,1 %: {Low(0.001f),4:0} · laengstes {MaxMs(),5:0.0} ms · " +
                         $"CPU {Avg(_cpu, _n),4:0.0} · Render-Thread {Avg(_render, _renderN),4:0.0} · GPU {Avg(_gpu, _gpuN),4:0.0} ms · " +
                         $"{_tris / Mathf.Max(1, _n) / 1000.0,5:0}k Dreiecke · {_skinned / Mathf.Max(1, _n),3:0} Haut-Meshes · " +
                         $"RAM {_sysMem / 1048576,5} MB · Grafik {(_gfxMem > 0 ? (_gfxMem / 1048576).ToString() : "n. v."),5} MB");
                    _csv.AppendLine(string.Join(";", st.Name, tier, F(Fps()), F(Low(0.01f)), F(Low(0.001f)), F(MaxMs()), F(Avg(_cpu, _n)),
                        F(Avg(_render, _renderN)), F(Avg(_gpu, _gpuN)), F(_tris / Mathf.Max(1, _n) / 1000.0), F(_skinned / Mathf.Max(1, _n)),
                        (_sysMem / 1048576).ToString(), (_gfxMem / 1048576).ToString()));
                    if (t == (int)GraphicsTier.Mittel || t == (int)GraphicsTier.Episch)
                    {
                        string file = Path.Combine(ShotDir, $"{Array.IndexOf(Stations, st) + 1:00}_{Safe(st.Name)}_{tier}.png");
                        ScreenCapture.CaptureScreenshot(file);
                        yield return null;
                        yield return null;
                    }
                }
            }

            if (controller != null) controller.enabled = true;
            if (motion != null) motion.ServerTeleportToSpawn();
            RestoreGpu(grdOriginal, occOriginal);
            Finish(original);
        }

        /// <summary>Mittel und Episch je dreimal: GRD aus / an / an + GPU-Occlusion. Dazu Bildvergleich gegen "GRD aus".</summary>
        private IEnumerator GpuVariants(Camera cam, Station st)
        {
            if (!GpuDrivenSettings.Available) { Line("  FEHLER: kein URP-Asset"); yield break; }
            GraphicsTier[] tiers = { GraphicsTier.Mittel, GraphicsTier.Episch };
            foreach (GraphicsTier tier in tiers)
            {
                ApplyUncapped(tier);
                Texture2D reference = null;
                for (int v = 0; v < VariantNames.Length; v++)
                {
                    if (v >= 1 && !_drawerInBuild)
                    {
                        if (tier == GraphicsTier.Mittel && v == 1)
                            Line("  (GRD-Varianten übersprungen: der Build ist ohne GPU Resident Drawer erstellt – zum Vergleichen im URP-Asset einschalten und neu bauen)");
                        continue;
                    }
                    SetGpu(v >= 1, v == 2);
                    float s = 0f;
                    while (s < 2.5f) { yield return null; PlaceCamera(cam, st); s += Time.unscaledDeltaTime; }
                    yield return Measure(cam, st, 4f);
                    string tierName = GraphicsCatalog.TierNames[(int)tier];
                    yield return new WaitForEndOfFrame();
                    Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                    string diff = "";
                    if (v == 0) reference = shot;
                    else
                    {
                        diff = $" · Bildabweichung zu GRD aus {PixelDiff(reference, shot):0.00} %";
                        Destroy(shot);
                    }
                    Line($"  {tierName,-7} {VariantNames[v],-9} {Fps(),5:0} Bilder/s · 1 %: {Low(0.01f),4:0} · 0,1 %: {Low(0.001f),4:0} · laengstes {MaxMs(),5:0.0} ms · " +
                         $"CPU {Avg(_cpu, _n),4:0.00} · Render-Thread {Avg(_render, _renderN),4:0.00} · GPU {Avg(_gpu, _gpuN),4:0.00} ms · " +
                         $"{_tris / Mathf.Max(1, _n) / 1000.0,5:0}k Dreiecke{diff}");
                    _csv.AppendLine(string.Join(";", st.Name, tierName, VariantNames[v], F(Fps()), F(Low(0.01f)), F(Low(0.001f)), F(MaxMs()),
                        F2(Avg(_cpu, _n)), F2(Avg(_render, _renderN)), F2(Avg(_gpu, _gpuN)), F(_tris / Mathf.Max(1, _n) / 1000.0),
                        F(_skinned / Mathf.Max(1, _n)), (_sysMem / 1048576).ToString(), (_gfxMem / 1048576).ToString()));
                }
                if (reference != null) Destroy(reference);
            }
        }

        private static void SetGpu(bool drawer, bool occlusion)
        {
            GpuDrivenSettings.DrawerEnabled = drawer;
            GpuDrivenSettings.OcclusionEnabled = occlusion;
        }

        private static void RestoreGpu(bool drawer, bool occlusion)
        {
            if (!GpuMode) return;
            SetGpu(drawer, occlusion);
        }

        /// <summary>Anteil der Bildpunkte (jeder 4. in x/y), die sich deutlich unterscheiden – Himmel/Partikel bewegen sich immer etwas.</summary>
        private static float PixelDiff(Texture2D a, Texture2D b)
        {
            if (a == null || b == null || a.width != b.width || a.height != b.height) return -1f;
            Color32[] pa = a.GetPixels32(), pb = b.GetPixels32();
            int total = 0, differ = 0;
            for (int y = 0; y < a.height; y += 4)
                for (int x = 0; x < a.width; x += 4)
                {
                    int i = y * a.width + x;
                    int d = Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b);
                    total++;
                    if (d > 60) differ++;
                }
            return total > 0 ? 100f * differ / total : 0f;
        }

        internal static void PlaceCamera(Camera cam, Station st)
        {
            if (cam == null) return;
            Vector3 eye = st.Eye, target = st.Target;
            if (eye.y < -9000f) eye.y = WorldGround.SampleHeight(eye, 0f) + 1.8f;
            if (target.y < -9000f) target.y = WorldGround.SampleHeight(target, 0f) + 2f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
        }

        private static void ApplyUncapped(GraphicsTier tier)
        {
            GraphicsSettingsStore.SetTier(tier);
            GraphicsOptions o = GraphicsSettingsStore.Current.Clone();
            o.vSync = false;
            o.frameLimit = GraphicsCatalog.FrameLimits.Length - 1;
            GraphicsSettingsStore.Set(o);
        }

        private IEnumerator Measure(Camera cam, Station st, float seconds)
        {
            _frames.Clear();
            _cpu = _render = _gpu = _tris = _skinned = 0;
            _n = _gpuN = _renderN = 0;
            using (ProfilerRecorder tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count"))
            using (ProfilerRecorder skinned = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Visible Skinned Meshes Count"))
            using (ProfilerRecorder sys = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory"))
            using (ProfilerRecorder gfx = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory"))
            {
                float end = Time.realtimeSinceStartup + seconds;
                while (Time.realtimeSinceStartup < end)
                {
                    yield return null;
                    PlaceCamera(cam, st);
                    _frames.Add(Time.unscaledDeltaTime);
                    _n++;
                    if (tris.Valid) _tris += tris.LastValue;
                    if (skinned.Valid) _skinned += skinned.LastValue;
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, _timing) > 0)
                    {
                        FrameTiming f = _timing[0];
                        if (f.cpuMainThreadFrameTime > 0.0) _cpu += f.cpuMainThreadFrameTime - f.cpuMainThreadPresentWaitTime;
                        if (f.cpuRenderThreadFrameTime > 0.0) { _render += f.cpuRenderThreadFrameTime; _renderN++; }
                        // Ausreisser (Zeitstempel-Überlauf beim Stufenwechsel) verwerfen.
                        if (f.gpuFrameTime > 0.0 && f.gpuFrameTime < 1000.0) { _gpu += f.gpuFrameTime; _gpuN++; }
                    }
                }
                _sysMem = sys.Valid ? sys.LastValue : 0;
                _gfxMem = gfx.Valid ? gfx.LastValue : 0;
            }
        }

        /// <summary>Was liegt im Bild? Renderer, Haut-Meshes, Lichter (mit Schatten), Partikel – einmal je Ort.</summary>
        private static string SceneCensus(Camera cam)
        {
            if (cam == null) return "";
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
            int meshes = 0, skinned = 0, casters = 0;
            foreach (MeshRenderer r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (r.enabled && r.isVisible && GeometryUtility.TestPlanesAABB(planes, r.bounds))
                {
                    meshes++;
                    if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) casters++;
                }
            foreach (SkinnedMeshRenderer r in FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
                if (r.enabled && r.isVisible) skinned++;
            int lights = 0, shadowLights = 0;
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (!l.isActiveAndEnabled || l.type == LightType.Directional) continue;
                var b = new Bounds(l.transform.position, Vector3.one * l.range * 2f);
                if (!GeometryUtility.TestPlanesAABB(planes, b)) continue;
                lights++;
                if (l.shadows != LightShadows.None) shadowLights++;
            }
            int particles = 0, systems = 0;
            foreach (ParticleSystem ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                if (ps.isPlaying && ps.particleCount > 0 && Vector3.Distance(ps.transform.position, cam.transform.position) < cam.farClipPlane)
                {
                    systems++;
                    particles += ps.particleCount;
                }
            int terrains = Terrain.activeTerrains.Length;
            return $"Im Bild: {meshes} Mesh-Renderer ({casters} werfen Schatten), {skinned} Haut-Meshes, {lights} Zusatzlichter ({shadowLights} mit Schatten), " +
                   $"{systems} Partikelsysteme / {particles} Partikel, {terrains} Gelaendekacheln geladen";
        }

        private float Fps()
        {
            if (_frames.Count == 0) return 0f;
            float sum = 0f;
            foreach (float f in _frames) sum += f;
            return _frames.Count / Mathf.Max(0.0001f, sum);
        }

        private float Low(float fraction)
        {
            if (_frames.Count == 0) return 0f;
            var sorted = new List<float>(_frames);
            sorted.Sort();
            int n = Mathf.Max(1, Mathf.RoundToInt(sorted.Count * fraction));
            float sum = 0f;
            for (int i = sorted.Count - n; i < sorted.Count; i++) sum += sorted[i];
            return n / Mathf.Max(0.0001f, sum);
        }

        private float MaxMs()
        {
            float m = 0f;
            foreach (float f in _frames) m = Mathf.Max(m, f);
            return m * 1000f;
        }

        private static double Avg(double sum, int n) => n > 0 ? sum / n : 0.0;
        private static string F(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
        private static string F2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        private static string Safe(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }

        private void Finish(GraphicsOptions original)
        {
            Application.logMessageReceived -= OnLog;
            GraphicsSettingsStore.Set(original);
            GraphicsSettingsStore.PersistenceEnabled = true;
            Line("");
            Line($"Fehler/Ausnahmen im Log: {_logErrors}");
            foreach (string e in _firstErrors) Line("  " + e);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath, _report.ToString());
                File.WriteAllText(CsvPath, _csv.ToString());
            }
            catch (IOException e)
            {
                Debug.LogWarning("[Ashveil][Grundlinie] Bericht nicht geschrieben: " + e.Message);
            }
            Debug.Log("[Ashveil][Grundlinie] Fertig → " + ReportPath);
            if (!Application.isEditor) Application.Quit();
        }

        private void OnLog(string msg, string stack, LogType type)
        {
            if ((type == LogType.Error || type == LogType.Exception) && !TestLog.IsEditorTooling(msg, stack))
            {
                _logErrors++;
                string first = msg.Length > 200 ? msg.Substring(0, 200) : msg;
                if (_firstErrors.Count < 3 && !_firstErrors.Contains(first)) _firstErrors.Add(first);
            }
        }

        private void Line(string s)
        {
            _report.AppendLine(s);
            Debug.Log("[Ashveil][Grundlinie] " + s);
        }

        private static PlayerCharacter FindLocalPlayer()
        {
            foreach (PlayerCharacter pc in FindObjectsByType<PlayerCharacter>(FindObjectsInactive.Exclude))
                if (pc.isActiveAndEnabled) return pc;
            return null;
        }
    }
}
