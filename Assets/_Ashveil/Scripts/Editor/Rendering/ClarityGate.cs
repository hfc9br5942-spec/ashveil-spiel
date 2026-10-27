using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Ashveil.EditorTools.Rendering
{
    /// <summary>
    /// Visual Clarity Gate (02.10.2026): baut einen eigenen Messbuild (Builds/ClarityProbe, Development) und startet ihn
    /// nacheinander mit verschiedenen Aufloesungen. Jeder Lauf schreibt per ClarityProbe Messwerte und ein Bildschirmfoto
    /// nach Logs/Ashveil/Clarity/&lt;Durchgang&gt;/ und beendet sich selbst.
    ///   standard  – ohne Aufloesungs-Argumente (so startet Max den Build)
    ///   nativ     – Desktopaufloesung
    ///   1280x720, 1920x1080, 2560x1440, 3840x2160 – soweit nicht groesser als der Desktop
    /// Der P4-Build wird nicht angefasst. Profil "_clarity" (Spielstand im Messbuild-Ordner).
    /// </summary>
    public static class ClarityGate
    {
        public const string Folder = "Builds/ClarityProbe";
        private static readonly List<(string label, string args)> Runs = new List<(string, string)>();
        private static int _index;
        private static Process _proc;
        private static double _deadline;
        private static string _outDir;

        [MenuItem("Ashveil/Rendering/Visual Clarity Gate: Vergleich (vorher)", priority = 440)]
        public static void RunBefore() => Run("vorher", true);

        [MenuItem("Ashveil/Rendering/Visual Clarity Gate: Vergleich (nachher)", priority = 441)]
        public static void RunAfter() => Run("nachher", true);

        public static void Run(string pass, bool build)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (build && !NetworkSetupBuilder.BuildPlaytest(Folder)) return;
            NetworkSetupBuilder.StopTestClients();
            _outDir = Ashveil.Core.ProjectPaths.Logs("Clarity", pass);
            Directory.CreateDirectory(_outDir);
            Resolution desk = Screen.currentResolution;
            int w = desk.width, h = desk.height;
            Runs.Clear();
            Runs.Add(("standard", ""));
            Runs.Add(($"nativ_{w}x{h}", $"-screen-fullscreen 1 -screen-width {w} -screen-height {h}"));
            foreach (Vector2Int r in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(3840, 2160) })
                if (r.x <= w && r.y <= h && !(r.x == w && r.y == h)) Runs.Add(($"{r.x}x{r.y}", $"-screen-fullscreen 1 -screen-width {r.x} -screen-height {r.y}"));
            File.WriteAllText(Path.Combine(_outDir, "_laeufe.txt"), $"Desktop laut Editor: {w}×{h}\n" + string.Join("\n", Runs.ConvertAll(r => r.label + ": " + r.args)));
            _index = -1;
            Next();
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Next()
        {
            _index++;
            if (_index >= Runs.Count)
            {
                EditorApplication.update -= Tick;
                Debug.Log("[Ashveil] Visual Clarity Gate fertig → " + _outDir);
                return;
            }
            (string label, string args) = Runs[_index];
            string exe = Path.GetFullPath(Path.Combine(Folder, "Ashveil.exe"));
            string all = $"-ashveil-host -ashveil-profile _clarity -ashveil-autocreate {NetworkSetupBuilder.ZeitGestaltet} -ashveil-clarity \"{_outDir}\" -ashveil-clarity-label {label} {args}";
            _proc = Process.Start(new ProcessStartInfo(exe, all) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = false });
            _deadline = EditorApplication.timeSinceStartup + 120.0;
            Debug.Log($"[Ashveil] Clarity-Lauf {_index + 1}/{Runs.Count}: {label}");
        }

        private static void Tick()
        {
            if (_proc == null) { Next(); return; }
            if (!_proc.HasExited && EditorApplication.timeSinceStartup < _deadline) return;
            if (!_proc.HasExited)
            {
                try { _proc.Kill(); } catch (Exception) { }
                Debug.LogWarning("[Ashveil] Clarity-Lauf haengt – beendet: " + Runs[_index].label);
            }
            _proc = null;
            Next();
        }
    }
}
