using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor.SceneManagement;
using Ashveil.Rendering.PathTracing;
using Ashveil.Rendering.PathTracing.Lab;

namespace Ashveil.EditorTools.Rendering
{
    /// <summary>
    /// Pathtracing PT0: Bestandsaufnahme (Unity/URP, Grafik-API, Raytracing-Faehigkeit, Renderer Features, Materialien,
    /// Himmel, Streaming) als Bericht Logs/Ashveil/Ashveil_PathTracingAudit.txt. Dazu die Umschaltung der Grafik-API
    /// (DirectX 12 mit DirectX 11 als Rueckfall bzw. zurueck) inklusive Editor-Neustart.
    /// </summary>
    public static class PathTracingAudit
    {
        public const string ReportPath = "Logs/Ashveil/Ashveil_PathTracingAudit.txt";

        [MenuItem("Ashveil/Rendering/Pathtracing-Bestandsaufnahme (PT0)", priority = 400)]
        public static void Run()
        {
            var sb = new StringBuilder();
            void L(string s = "") => sb.AppendLine(s);

            L("ASHVEIL – Pathtracing-Bestandsaufnahme (PT0)");
            L($"Zeit: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            L();
            L("== Engine");
            L($"  Unity {Application.unityVersion}");
            var urp = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UniversalRenderPipeline).Assembly);
            L($"  URP {(urp != null ? urp.version : "?")}");
            L($"  Farbraum: {PlayerSettings.colorSpace}");
            L();
            L("== Grafik-API");
            L($"  Laufender Editor: {SystemInfo.graphicsDeviceType} ({SystemInfo.graphicsDeviceVersion})");
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64);
            L($"  Windows-Liste: {string.Join(" > ", apis)} (automatisch: {PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64)})");
            L($"  Grafikkarte: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB), Treiber {SystemInfo.graphicsDeviceVersion}");
            L();
            L("== Raytracing-Faehigkeit (dieses Geraet, diese API)");
            L($"  supportsRayTracing:        {SystemInfo.supportsRayTracing}");
            L($"  supportsRayTracingShaders: {SystemInfo.supportsRayTracingShaders}");
            L($"  supportsInlineRayTracing:  {SystemInfo.supportsInlineRayTracing}");
            L($"  supportsComputeShaders:    {SystemInfo.supportsComputeShaders}");
            L($"  Ergebnis: {Ashveil.Rendering.PathTracing.PathTracingCapability.Describe()}");
            L();
            L("== Render-Pipeline");
            var rp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            var q = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
            L($"  Standard-Asset: {(rp != null ? rp.name : "-")}, aktive Qualitaet: {QualitySettings.names[QualitySettings.GetQualityLevel()]} → {(q != null ? q.name : "-")}");
            var asset = q != null ? q : rp;
            if (asset != null)
            {
                L($"  HDR: {asset.supportsHDR}, Renderskalierung {asset.renderScale}, MSAA {asset.msaaSampleCount}");
                var so = new SerializedObject(asset);
                var list = so.FindProperty("m_RendererDataList");
                for (int i = 0; list != null && i < list.arraySize; i++)
                {
                    var data = list.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererData;
                    if (data == null) continue;
                    L($"  Renderer {i}: {data.name} ({data.GetType().Name})");
                    foreach (var f in data.rendererFeatures)
                        if (f != null) L($"    Feature: {f.name} ({f.GetType().Name}, aktiv {f.isActive})");
                    if (data is UniversalRendererData ud) L($"    Pfad: {ud.renderingMode}, Tiefenpriming {ud.depthPrimingMode}");
                }
            }
            L("  Eigene Passes (per Skript eingereiht): AtmosphereFogPass (nach Himmel, Render Graph)");
            L();
            L("== Materialien (offene Szenen)");
            var shaders = new Dictionary<string, int>();
            int renderers = 0, skinned = 0, terrains = 0, lights = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                renderers++;
                if (r is SkinnedMeshRenderer) skinned++;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null) continue;
                    shaders.TryGetValue(m.shader.name, out int n);
                    shaders[m.shader.name] = n + 1;
                }
            }
            terrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Length;
            lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length;
            L($"  Renderer {renderers} (davon Skinned {skinned}), Terrains {terrains}, Lichter {lights}");
            foreach (var kv in shaders.OrderByDescending(k => k.Value)) L($"    {kv.Value,5} × {kv.Key}");
            L();
            L("== Himmel");
            L($"  Skybox: {(RenderSettings.skybox != null ? RenderSettings.skybox.shader.name : "-")}, Sonne: {(RenderSettings.sun != null ? RenderSettings.sun.name : "-")}");
            L($"  Umgebungslicht: {RenderSettings.ambientMode}");
            L();
            L("== Streaming");
            L("  WorldStreamer (Networking) laedt Zellen-Szenen additiv; der Pathtracer beobachtet Szenen-Laden/Entladen");
            L("  und nimmt nur geladene Renderer auf (keine eigene Ladelogik, WorldStreamer bleibt Autoritaet).");
            L();
            L("== Bewertung");
            bool dx12 = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12;
            L(dx12 && SystemInfo.supportsRayTracingShaders
                ? "  Bereit fuer PT1: DirectX 12 laeuft, Raytracing-Shader verfuegbar."
                : "  Noch nicht bereit: Editor laeuft nicht mit DirectX 12 oder die Karte kann kein Raytracing. " +
                  "Menue 'Ashveil/Rendering/Grafik-API: DirectX 12 (DX11 Rueckfall)' stellt um und startet den Editor neu.");

            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, sb.ToString());
            Debug.Log("[Ashveil] Pathtracing-Bestandsaufnahme geschrieben: " + ReportPath + "\n" + sb);
        }

        [MenuItem("Ashveil/Rendering/Grafik-API: DirectX 12 (DX11 Rueckfall)", priority = 401)]
        public static void UseDx12() => SetApis(new[] { GraphicsDeviceType.Direct3D12, GraphicsDeviceType.Direct3D11 });

        [MenuItem("Ashveil/Rendering/Grafik-API: DirectX 11 (DX12 zweite Wahl)", priority = 402)]
        public static void UseDx11() => SetApis(new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.Direct3D12 });

        private static void SetApis(GraphicsDeviceType[] apis)
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play beenden.", "OK");
                return;
            }
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, apis);
            AssetDatabase.SaveAssets();
            Debug.Log("[Ashveil] Windows-Grafik-API: " + string.Join(" > ", apis));
            if (SystemInfo.graphicsDeviceType != apis[0] &&
                EditorUtility.DisplayDialog("Ashveil", $"Der Editor laeuft mit {SystemInfo.graphicsDeviceType}. Jetzt neu starten, damit {apis[0]} aktiv wird?", "Neu starten", "Spaeter"))
            {
                UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
                EditorApplication.OpenProject(Directory.GetCurrentDirectory());
            }
        }
        // ------------------------------------------------------------------ Labor + Spiel

        private const string ReturnKey = "AshveilPTLabReturn";

        [MenuItem("Ashveil/Test/Pathtracing-Labor (Pruefszenen, PT1–PT4)", priority = 340)]
        public static void RunLab() => StartLab("");

        [MenuItem("Ashveil/Test/Pathtracing-Labor – nur Weissofen + Cornell", priority = 341)]
        public static void RunLabQuick() => StartLab("ofen,cornell");

        [MenuItem("Ashveil/Test/Pathtracing-Labor – nur Entrauschen + Adaptiv", priority = 342)]
        public static void RunLabDenoise() => StartLab("entrauschen,adaptiv");

        private static void StartLab(string only)
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play beenden.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            SessionState.SetString(ReturnKey, UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var lab = new GameObject("Pathtracing-Labor").AddComponent<PathTracingLab>();
            lab.only = only;
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void HookReturn()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredEditMode) return;
                string path = SessionState.GetString(ReturnKey, "");
                if (string.IsNullOrEmpty(path)) return;
                SessionState.EraseString(ReturnKey);
                if (File.Exists(path)) EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            };
        }

        [MenuItem("Ashveil/Rendering/Pathtracing an – aus (Play-Modus)", priority = 410)]
        public static void TogglePathTracing()
        {
            if (!EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Ashveil", "Nur im Play-Modus.", "OK"); return; }
            bool on = !AshveilPathTracer.IsRunning;
            if (!AshveilPathTracer.SetEnabled(on))
                EditorUtility.DisplayDialog("Ashveil", "Pathtracing " + PathTracingCapability.Describe(), "OK");
        }

        [MenuItem("Ashveil/Rendering/Pathtracing: naechste Stufe (Vorschau, Foto, Ultra, Kino)", priority = 411)]
        public static void NextPreset()
        {
            var t = AshveilPathTracer.Instance;
            if (t == null) return;
            t.settings.ApplyPreset((PathTracePreset)(((int)t.settings.preset + 1) % 4));
            t.ResetAccumulation();
            Debug.Log("[Ashveil] Pathtracing-Stufe: " + t.settings.preset);
        }

        [MenuItem("Ashveil/Rendering/Pathtracing: naechste Ansicht", priority = 412)]
        public static void NextView()
        {
            var t = AshveilPathTracer.Instance;
            if (t == null) return;
            t.settings.view = (PathTraceView)(((int)t.settings.view + 1) % 12);
            Debug.Log("[Ashveil] Pathtracing-Ansicht: " + t.settings.view);
        }

        [MenuItem("Ashveil/Rendering/Pathtracing: Bild speichern (PNG + EXR)", priority = 413)]
        public static void SaveShot()
        {
            var t = AshveilPathTracer.Instance;
            if (t == null) return;
            string path = Path.Combine("Logs/Ashveil/Pathtracing", "Aufnahme_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            t.SaveCapture(path, true, true, w => Debug.Log("[Ashveil] Pathtracing-Bild gespeichert: " + w));
        }
    }
}
