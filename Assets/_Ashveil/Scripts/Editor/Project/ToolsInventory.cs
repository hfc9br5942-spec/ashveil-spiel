using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Ashveil.Core;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Werkzeug-Inventur (Toolchain-Direktive Max 05.10., §1): prueft NUR LESEND, welche Produktionswerkzeuge auf diesem
    /// Rechner liegen (Unity, Pakete, Blender, Git, Git LFS, Material Maker, Meshroom, QGIS, IDE, Python, FFmpeg, Node,
    /// .NET) und wie gross die Projektordner sind. Startet keine Programme, laedt nichts, aendert nichts – schreibt nur den
    /// Bericht nach claude/werkzeuge/. Grundlage fuer claude/TOOLS_AUDIT.md.
    /// </summary>
    public static class ToolsInventory
    {
        [MenuItem("Ashveil/Projekt/Werkzeug-Inventur (nur lesen)", priority = 900)]
        public static void RunMenu()
        {
            string path = Run();
            UnityEngine.Debug.Log("[Ashveil] Werkzeug-Inventur geschrieben: " + path);
        }

        public static string Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("ASHVEIL Werkzeug-Inventur (nur lesen) · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("Rechner: " + Environment.MachineName + " · " + Environment.OSVersion + " · 64 Bit: " + Environment.Is64BitOperatingSystem);
            sb.AppendLine();

            sb.AppendLine("== Unity");
            sb.AppendLine("  laufender Editor: " + Application.unityVersion + " · " + EditorApplication.applicationPath);
            foreach (string d in SafeDirs(Path.Combine(ProgramFiles(), "Unity", "Hub", "Editor")))
                sb.AppendLine("  installierter Editor: " + Path.GetFileName(d));
            Describe(sb, "Unity Hub", new[] { Path.Combine(ProgramFiles(), "Unity Hub", "Unity Hub.exe") });
            sb.AppendLine();

            sb.AppendLine("== Unity-Pakete (offiziell, Toolchain-Direktive §3)");
            var packages = new Dictionary<string, string>();
            foreach (UnityEditor.PackageManager.PackageInfo p in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
                packages[p.name] = p.version + " (" + p.source + ")";
            foreach (string id in new[] { "com.unity.terrain-tools", "com.unity.splines", "com.unity.probuilder", "com.unity.ai.navigation",
                                          "com.unity.memoryprofiler", "com.unity.performance.profile-analyzer", "com.unity.test-framework",
                                          "com.unity.render-pipelines.universal", "com.firstgeargames.fishnet" })
                sb.AppendLine("  " + id.PadRight(42) + (packages.TryGetValue(id, out string v) ? v : "– nicht installiert"));
            sb.AppendLine("  (insgesamt " + packages.Count + " registrierte Pakete)");
            sb.AppendLine();

            sb.AppendLine("== Programme (Fundorte: PATH, Programme, Programme (x86), AppData\\Local\\Programs, C:\\Tools, C:\\Projekte\\AshveilTools)");
            string pf = ProgramFiles(), pf86 = ProgramFilesX86();
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Describe(sb, "Blender", Glob(pf, "Blender Foundation", "*", "blender.exe").Concat(Glob(local, "Programs", "Blender*", "blender.exe"))
                                       .Concat(OnPath("blender.exe")));
            Describe(sb, "Git", OnPath("git.exe").Concat(new[] { Path.Combine(pf, "Git", "cmd", "git.exe") }));
            Describe(sb, "Git LFS", OnPath("git-lfs.exe").Concat(new[] { Path.Combine(pf, "Git", "mingw64", "bin", "git-lfs.exe") }));
            Describe(sb, "Material Maker", Glob(pf, "*aterial*aker*", "material_maker.exe").Concat(Glob(@"C:\Tools", "*aterial*aker*", "material_maker.exe"))
                                              .Concat(Glob(@"C:\Projekte\AshveilTools", "*aterial*aker*", "material_maker.exe")).Concat(OnPath("material_maker.exe")));
            Describe(sb, "Meshroom", Glob(pf, "Meshroom*", "Meshroom.exe").Concat(Glob(@"C:\Tools", "Meshroom*", "Meshroom.exe")));
            Describe(sb, "QGIS", Glob(pf, "QGIS*", "bin", "qgis-bin.exe").Concat(Glob(pf, "QGIS*", "bin", "qgis-ltr-bin.exe")));
            Describe(sb, "Visual Studio", Glob(pf, "Microsoft Visual Studio", "*", "*", "Common7", "IDE", "devenv.exe")
                                             .Concat(Glob(pf86, "Microsoft Visual Studio", "*", "*", "Common7", "IDE", "devenv.exe")));
            Describe(sb, "JetBrains Rider", Glob(pf, "JetBrains", "*Rider*", "bin", "rider64.exe").Concat(Glob(local, "Programs", "Rider*", "bin", "rider64.exe")));
            Describe(sb, "VS Code", Glob(local, "Programs", "Microsoft VS Code", "Code.exe").Concat(Glob(pf, "Microsoft VS Code", "Code.exe")));
            Describe(sb, "Python", OnPath("python.exe").Concat(OnPath("py.exe")).Concat(Glob(local, "Programs", "Python", "Python*", "python.exe")));
            Describe(sb, "FFmpeg", OnPath("ffmpeg.exe").Concat(Glob(@"C:\Tools", "ffmpeg*", "bin", "ffmpeg.exe")));
            Describe(sb, "Node.js", OnPath("node.exe").Concat(new[] { Path.Combine(pf, "nodejs", "node.exe") }));
            Describe(sb, ".NET SDK/Runtime", OnPath("dotnet.exe").Concat(new[] { Path.Combine(pf, "dotnet", "dotnet.exe") }));
            sb.AppendLine();

            sb.AppendLine("== Versionsverwaltung im Projekt");
            string root = ProjectPaths.ProjectRoot;
            sb.AppendLine("  Projekt: " + root);
            sb.AppendLine("  .git vorhanden: " + (Directory.Exists(Path.Combine(root, ".git")) || File.Exists(Path.Combine(root, ".git"))));
            sb.AppendLine("  .gitignore vorhanden: " + File.Exists(Path.Combine(root, ".gitignore")));
            string attributes = Path.Combine(root, ".gitattributes");
            sb.AppendLine("  .gitattributes: " + (File.Exists(attributes)
                ? File.ReadAllLines(attributes).Count(l => l.Contains("filter=lfs")) + " LFS-Regeln"
                : "fehlt"));
            sb.AppendLine();

            sb.AppendLine("== Ordnergroessen (Dateigroessen-Kontrolle, Toolchain-Direktive §12)");
            foreach (string sub in new[] { "Assets", "Packages", "ProjectSettings", "SourceArt", "claude", "Tools", "Builds", "Library" })
            {
                string p = Path.Combine(root, sub);
                if (!Directory.Exists(p)) { sb.AppendLine("  " + sub.PadRight(16) + "–"); continue; }
                (long bytes, int files) = FolderSize(p);
                sb.AppendLine("  " + sub.PadRight(16) + (bytes / (1024.0 * 1024.0)).ToString("0.0").PadLeft(10) + " MB in " + files + " Dateien");
            }
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(root));
                sb.AppendLine("  frei auf " + drive.Name + ": " + (drive.AvailableFreeSpace / (1024.0 * 1024 * 1024)).ToString("0.0") + " GB von " +
                              (drive.TotalSize / (1024.0 * 1024 * 1024)).ToString("0") + " GB");
            }
            catch (Exception e) { sb.AppendLine("  Laufwerk: " + e.Message); }

            string dir = Path.Combine(root, "claude", "werkzeuge");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "tools_inventory_" + DateTime.Now.ToString("yyyy-MM-dd_HHmm") + ".txt");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            return file;
        }

        // ------------------------------------------------------------------ Hilfen (nur Dateisystem, keine Prozesse)

        private static string ProgramFiles() => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        private static string ProgramFilesX86() => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        private static IEnumerable<string> SafeDirs(string path)
        {
            try { return Directory.Exists(path) ? Directory.GetDirectories(path) : Array.Empty<string>(); }
            catch (Exception) { return Array.Empty<string>(); }
        }

        /// <summary>Pfadmuster mit * nur in einzelnen Ordnernamen (keine Rekursion), letzter Teil = Dateiname.</summary>
        private static IEnumerable<string> Glob(string root, params string[] parts)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) yield break;
            var current = new List<string> { root };
            for (int i = 0; i < parts.Length; i++)
            {
                bool last = i == parts.Length - 1;
                var next = new List<string>();
                foreach (string c in current)
                {
                    try
                    {
                        if (last) next.AddRange(Directory.GetFiles(c, parts[i]));
                        else if (parts[i].Contains("*")) next.AddRange(Directory.GetDirectories(c, parts[i]));
                        else if (Directory.Exists(Path.Combine(c, parts[i]))) next.Add(Path.Combine(c, parts[i]));
                    }
                    catch (Exception) { /* kein Zugriff: auslassen */ }
                }
                current = next;
                if (current.Count == 0) yield break;
            }
            foreach (string f in current) yield return f;
        }

        private static IEnumerable<string> OnPath(string exe)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string d in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(d)) continue;
                string f;
                try { f = Path.Combine(d.Trim().Trim('"'), exe); }
                catch (Exception) { continue; }
                if (File.Exists(f)) yield return f;
            }
        }

        private static void Describe(StringBuilder sb, string tool, IEnumerable<string> candidates)
        {
            var found = candidates.Where(File.Exists).Select(p => Path.GetFullPath(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (found.Count == 0) { sb.AppendLine("  " + tool.PadRight(18) + "– nicht gefunden"); return; }
            foreach (string f in found)
            {
                string version = "";
                try
                {
                    FileVersionInfo vi = FileVersionInfo.GetVersionInfo(f);
                    version = !string.IsNullOrEmpty(vi.ProductVersion) ? vi.ProductVersion : vi.FileVersion;
                }
                catch (Exception) { /* ohne Versionsangabe */ }
                sb.AppendLine("  " + tool.PadRight(18) + (string.IsNullOrEmpty(version) ? "?" : version).PadRight(22) + f +
                              " · " + File.GetLastWriteTime(f).ToString("yyyy-MM-dd"));
            }
        }

        private static (long bytes, int files) FolderSize(string path)
        {
            long bytes = 0;
            int files = 0;
            try
            {
                foreach (string f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { bytes += new FileInfo(f).Length; files++; }
                    catch (Exception) { /* gesperrt: auslassen */ }
                }
            }
            catch (Exception) { /* kein Zugriff */ }
            return (bytes, files);
        }
    }
}
