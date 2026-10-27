using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Ashveil.FloraValidation.EditorTools
{
    /// <summary>
    /// v3-Validierungsgate: (1) ein Development Player Build NUR der isolierten Testszene (keine Build-Einstellungen geaendert,
    /// keine produktive Welt), danach Start mit Player-Messlauf; (2) isolierte Kopie der aktiven URP-Pipeline mit
    /// GPU Resident Drawer fuer den GRD-Test im Play Mode. Die Projekt-Pipeline und die Quality-Einstellungen bleiben unveraendert.
    /// </summary>
    public static class FloraValidationPlayerBuild
    {
        public const string BuildDir = "Builds/FloraValidation";
        public const string ExeName = "FloraValidation.exe";
        public const string GrdDir = "Assets/_Ashveil/Art/FloraValidation/GRD";

        public static void BuildAndRun()
        {
            if (EditorApplication.isPlaying) return;
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            {
                Debug.LogError("[FloraValidation] Player-Build abgebrochen: aktives Build-Ziel ist " + EditorUserBuildSettings.activeBuildTarget +
                               " (Ziel wird bewusst nicht umgestellt).");
                return;
            }
            string root = Ashveil.Core.ProjectPaths.ProjectRoot;
            string dir = Path.Combine(root, BuildDir);
            string exe = Path.Combine(dir, ExeName);
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { FloraValidationSceneBuilder.ScenePath },
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.Development,
            };
            // v4: sauberer Build nur auf Anforderung (Datei clean_build.flag im Build-Ordner) – fuer die vollstaendige Fehler-/Warnungsliste.
            string flag = Path.Combine(dir, "clean_build.flag");
            if (File.Exists(flag)) { opts.options |= BuildOptions.CleanBuildCache; File.Delete(flag); Debug.Log("[FloraValidation] Sauberer Build (CleanBuildCache)"); }
            var sw = Stopwatch.StartNew();
            BuildReport rep = BuildPipeline.BuildPlayer(opts);
            sw.Stop();
            BuildSummary sum = rep.summary;
            string info = $"Player-Build {sum.result} · Dauer {sw.Elapsed.TotalMinutes:0.0} min · Groesse {sum.totalSize / 1048576.0:0} MB · Fehler {sum.totalErrors} · Warnungen {sum.totalWarnings}";
            Debug.Log("[FloraValidation] " + info);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "build_info.txt"), info + "\n" + exe + "\n");
            WriteBuildMessages(rep, Path.Combine(dir, "build_messages.txt"));
            if (sum.result != BuildResult.Succeeded) return;

            var psi = new ProcessStartInfo(exe,
                $"-screen-width 1920 -screen-height 1080 -screen-fullscreen 0 -fvMode 3 -logFile \"{Path.Combine(dir, "player.log")}\"")
            { UseShellExecute = false, WorkingDirectory = dir };
            Process.Start(psi);
            Debug.Log("[FloraValidation] Player gestartet (Messlauf Modus 3). Ergebnisse: " + dir + "/Logs/Ashveil/FloraValidation/PlayerBuild");
        }

        /// <summary>v4: alle Fehler/Warnungen des Build-Berichts mit Schritt und grober Kategorie (Flora / Unity-URP / Projekt / Sonstiges).</summary>
        private static void WriteBuildMessages(BuildReport rep, string path)
        {
            var sb = new System.Text.StringBuilder();
            var cat = new System.Collections.Generic.Dictionary<string, int>();
            int ne = 0, nw = 0;
            foreach (BuildStep step in rep.steps)
                foreach (BuildStepMessage m in step.messages)
                {
                    if (m.type != LogType.Error && m.type != LogType.Exception && m.type != LogType.Warning && m.type != LogType.Assert) continue;
                    bool err = m.type != LogType.Warning;
                    if (err) ne++; else nw++;
                    string c = Category(m.content);
                    string key = (err ? "ERROR " : "WARN  ") + c;
                    cat[key] = cat.TryGetValue(key, out int v) ? v + 1 : 1;
                    if (err || c == "Flora" || c.StartsWith("Projekt") || cat[key] <= 25)
                        sb.AppendLine($"[{(err ? "ERROR" : "WARN")}] [{c}] [{step.name}] {m.content.Replace("\r", " ").Replace("\n", " ⏎ ")}");
                }
            var head = new System.Text.StringBuilder();
            head.AppendLine($"Build-Bericht: {rep.summary.result} · Fehler {rep.summary.totalErrors} · Warnungen {rep.summary.totalWarnings} · Meldungen in Schritten: Fehler {ne}, Warnungen {nw}");
            foreach (var kv in cat) head.AppendLine($"  {kv.Key,-40} {kv.Value}");
            head.AppendLine("----");
            File.WriteAllText(path, head.ToString() + sb.ToString());
        }

        private static string Category(string msg)
        {
            string m = msg ?? "";
            if (m.Contains("FloraValidation") || m.Contains("Ashveil/FloraValidation") || m.Contains("FloraLit") || m.Contains("FloraImpostor") || m.Contains("FloraDebug")) return "Flora";
            if (m.Contains("com.unity.render-pipelines") || m.Contains("Universal Render Pipeline") || m.Contains("URP") || m.Contains("Hidden/Universal")) return "Unity/URP";
            if (m.Contains("Assets/_Ashveil") || m.Contains("Ashveil.")) return "Projekt (Ashveil, nicht Flora)";
            if (m.Contains("Shader") || m.Contains("shader")) return "Shader (sonstige)";
            return "Sonstiges/Unity";
        }

        /// <summary>Kopie der aktiven Pipeline mit GPU Resident Drawer (InstancedDrawing). Wird NUR vom Messlauf zur Laufzeit gesetzt.</summary>
        public static RenderPipelineAsset GrdTestPipeline()
        {
            RenderPipelineAsset src = QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline;
            if (src == null) { Debug.LogError("[FloraValidation] Keine aktive Render-Pipeline gefunden."); return null; }
            string srcPath = AssetDatabase.GetAssetPath(src);
            FloraValidationImporter.EnsureFolder(GrdDir);
            string dst = GrdDir + "/" + Path.GetFileNameWithoutExtension(srcPath) + "_GRDTest.asset";
            AssetDatabase.DeleteAsset(dst);
            if (!AssetDatabase.CopyAsset(srcPath, dst)) { Debug.LogError("[FloraValidation] Pipeline-Kopie fehlgeschlagen: " + srcPath); return null; }
            var copy = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(dst);
            var so = new SerializedObject(copy);
            SerializedProperty p = so.FindProperty("m_GPUResidentDrawerMode");
            if (p == null) { Debug.LogError("[FloraValidation] m_GPUResidentDrawerMode nicht gefunden."); return null; }
            p.intValue = 1; // InstancedDrawing
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"[FloraValidation] GRD-Testkopie: {dst} (Quelle {srcPath} unveraendert)");
            return copy;
        }
    }
}
