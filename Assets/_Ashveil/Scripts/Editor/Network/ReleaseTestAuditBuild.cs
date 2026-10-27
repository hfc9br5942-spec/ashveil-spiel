using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashveil.Core;
using Ashveil.Networking;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Ashveil.EditorTools
{
    /// <summary>Enger Gegenpruefungs-Build; schreibt ausschliesslich in einen neuen eigenen Ausgabeordner.</summary>
    [InitializeOnLoad]
    public static class ReleaseTestAuditBuild
    {
        static ReleaseTestAuditBuild()
        {
            Auftragsordner.Registrieren("release_test_audit", Run, true, 3600);
        }

        private static void Run(Auftragsordner.Auftrag job, Action<bool, string> finish)
        {
            string mode = (job.parameter ?? "").Trim().ToLowerInvariant();
            if (mode != "release" && mode != "test")
            {
                finish(false, "Parameter muss release oder test sein.");
                return;
            }
            // Ein global gesetztes Testsymbol wuerde den Release-Vergleich entwerten. Nur melden, nie Settings aendern.
            string globals = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
            if (globals.Split(';').Any(s => s.Trim() == TestBuild.Symbol))
            {
                finish(false, "ASHVEIL_TEST_CLIENT ist global gesetzt; kein gueltiger Release-Vergleich. Keine Einstellung geaendert.");
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                finish(false, "Play laeuft; kein Build waehrend Play.");
                return;
            }
            var scenes = new List<string> { SandboxSceneBuilder.ScenePath };
            scenes.AddRange(WorldManifestBuilder.CellScenes());
            string folder = ProjectPaths.Get(ProjectPaths.BuildsDir, "ReleaseAudit",
                DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8), mode);
            if (Directory.Exists(folder))
            {
                finish(false, "Ausgabeordner existiert bereits; nichts ueberschrieben.");
                return;
            }
            Directory.CreateDirectory(folder);
            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes.ToArray(),
                    locationPathName = Path.Combine(folder, "Ashveil.exe"),
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                    extraScriptingDefines = mode == "test" ? new[] { TestBuild.Symbol } : Array.Empty<string>()
                });
            }
            finally
            {
                Ashveil.EditorTools.Rendering.DynamicGIBuildSupport.SetActiveForBuild(false);
            }
            bool ok = report.summary.result == BuildResult.Succeeded;
            finish(ok, $"Release-Test-Gegenprobe ({mode}): {report.summary.result}, {report.summary.totalErrors} Fehler, " +
                $"{report.summary.totalTime.TotalSeconds:0} s; Ausgabe: {folder}. Kein Player gestartet, keine Spielstaende angefasst.");
        }
    }
}
