using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ashveil.Core;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Haelt die Ordnerstruktur sauber (Soll-Aufbau: Docs/Ordnerstruktur.md).
    ///
    /// "Aufraeumen" verschiebt Altlasten GUID-sicher ueber die AssetDatabase (Verweise in Szenen/Prefabs bleiben
    /// erhalten) und ordnet die Projektordner neben Assets. Idempotent: mehrfach ausfuehren schadet nicht.
    /// "Pruefen" meldet nur, was nicht zum Soll-Aufbau passt – aendert nichts.
    /// </summary>
    public static class ProjectStructure
    {
        private const string Scripts = ProjectSetup.Root + "/Scripts";

        /// <summary>Editor-Skripte nach Aufgabe. Neue Editor-Skripte bitte in den passenden Unterordner legen.</summary>
        private static readonly Dictionary<string, string[]> EditorGroups = new Dictionary<string, string[]>
        {
            ["Project"] = new[] { "ProjectSetup", "ProjectCleanup", "ProjectStructure", "TmpResources", "DefinitionValidator" },
            ["Network"] = new[] { "NetworkSetupBuilder" },
            ["World"] = new[] { "SandboxSceneBuilder", "KarethTerrainBuilder", "GiantBuilder", "KarethLayout", "HandworkKeeper", "KarethBackup", "GreyboxKit" },
            ["Art"] = new[] { "ArtPipeline", "ArtModels", "ArtSwap", "ArtStyleSetup", "StyleShots", "QuaterniusImport" },
            ["Content"] = new[] { "AppearanceContentBuilder", "ItemContentBuilder", "QuestContentBuilder", "EnemyBuilder" },
            ["Characters"] = new[] { "PlayerRigBuilder", "PlayerAnimatorBuilder" }
        };

        /// <summary>Automatische Tests (laufen im Spiel, darum im Networking-Assembly) – getrennt vom Spielcode.</summary>
        private static readonly string[] NetworkingTests =
        {
            "DuoTestDirector", "DuoTestDirector.Group", "DuoTestDirector.Kareth", "DuoTestDirector.Progress",
            "DuoTestDirector.Social", "SelfTestRunner", "SelfTestAgent"
        };

        private const string LegacyGreybox = ProjectSetup.Root + "/Materials/Greybox";

        [MenuItem("Ashveil/Projekt/Ordnerstruktur aufraeumen", priority = 104)]
        public static void CleanupMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play beenden.", "OK");
                return;
            }
            string report = Cleanup();
            Debug.Log("[Ashveil] Ordnerstruktur:\n" + report);
            EditorUtility.DisplayDialog("Ashveil", report, "OK");
        }

        [MenuItem("Ashveil/Projekt/Ordnerstruktur pruefen", priority = 105)]
        public static void CheckMenu()
        {
            List<string> issues = Check();
            string report = issues.Count == 0 ? "Ordnerstruktur OK." : "Abweichungen:\n- " + string.Join("\n- ", issues);
            if (issues.Count == 0) Debug.Log("[Ashveil] " + report);
            else Debug.LogWarning("[Ashveil] " + report);
            EditorUtility.DisplayDialog("Ashveil", report, "OK");
        }

        public static string Cleanup()
        {
            var log = new StringBuilder();
            ProjectSetup.EnsureFolders();

            // Zielordner zuerst anlegen (MoveAsset braucht registrierte Ordner), dann einzeln verschieben.
            foreach (string group in EditorGroups.Keys) ProjectSetup.EnsureFolder($"{Scripts}/Editor/{group}");
            ProjectSetup.EnsureFolder($"{Scripts}/Networking/Testing");
            AssetDatabase.Refresh();
            {
                // 1) Editor-Skripte in Aufgaben-Ordner
                foreach (KeyValuePair<string, string[]> g in EditorGroups)
                {
                    foreach (string name in g.Value)
                        Move($"{Scripts}/Editor/{name}.cs", $"{Scripts}/Editor/{g.Key}/{name}.cs", log);
                }

                // 2) Tests aus dem Netzwerk-Spielcode herausloesen
                foreach (string name in NetworkingTests)
                    Move($"{Scripts}/Networking/{name}.cs", $"{Scripts}/Networking/Testing/{name}.cs", log);

                // 3) Greybox-Materialien zu den uebrigen Materialien (Art/Materials/Greybox)
                if (AssetDatabase.IsValidFolder(LegacyGreybox))
                {
                    foreach (string guid in AssetDatabase.FindAssets("", new[] { LegacyGreybox }))
                    {
                        string from = AssetDatabase.GUIDToAssetPath(guid);
                        if (AssetDatabase.IsValidFolder(from)) continue;
                        string to = ProjectSetup.GreyboxMaterials + "/" + Path.GetFileName(from);
                        if (AssetDatabase.LoadMainAssetAtPath(to) != null)
                        {
                            // Bereits neu erzeugt: die alte Datei ist ueberzaehlig – nur loeschen, wenn nichts auf sie zeigt.
                            log.AppendLine($"Doppelt (bleibt liegen, bitte pruefen): {from}");
                            continue;
                        }
                        Move(from, to, log);
                    }
                }
            }

            // Leere Altordner entfernen
            foreach (string folder in new[] { LegacyGreybox, ProjectSetup.Root + "/Materials", "Assets/Resources" })
                if (IsEmptyFolder(folder) && AssetDatabase.DeleteAsset(folder)) log.AppendLine("Leeren Ordner entfernt: " + folder);

            // 4) Projektordner neben Assets
            MoveDirectory(ProjectPaths.Get("Art"), ProjectPaths.SourceArt(), log);
            MoveDirectory(ProjectPaths.Get("_Import"), ProjectPaths.SourceArt("ThirdParty"), log);
            // Ashveil-Berichte aus Unitys Logs/ nach Logs/Ashveil/
            foreach (string sub in new[] { "Riese", "Stil", "Ausruestung" })
                MoveDirectory(ProjectPaths.Get(ProjectPaths.LogsDir, sub), ProjectPaths.Logs(sub), log);
            string unityLogs = ProjectPaths.Get(ProjectPaths.LogsDir);
            if (Directory.Exists(unityLogs))
                foreach (string file in Directory.GetFiles(unityLogs, "Ashveil_*.txt"))
                {
                    string target = ProjectPaths.Logs(Path.GetFileName(file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(file, target);
                    log.AppendLine($"Logs/{Path.GetFileName(file)}  ->  Logs/{ProjectPaths.ReportsSubDir}/");
                }

            // 5) Lose Dateien im Projektordner (z. B. Downloads, die im falschen Ordner gelandet sind) einsortieren
            SortRootFiles(log);
            foreach (string legacy in new[] { "Art", "_Import" })
            {
                string dir = ProjectPaths.Get(legacy);
                if (Directory.Exists(dir) && Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length == 0 &&
                    TryDeleteDirectory(dir, log))
                    log.AppendLine($"Leeren Altordner entfernt: {legacy}/");
            }

            // Python-Cache ist in .gitignore – Loeschen nur versuchen (OneDrive sperrt Ordner gelegentlich)
            string cache = ProjectPaths.Tools("Blender", "__pycache__");
            if (Directory.Exists(cache) && TryDeleteDirectory(cache, log)) log.AppendLine("Python-Cache entfernt: Tools/Blender/__pycache__");
            foreach (string dir in new[] { ProjectPaths.DocsDir, ProjectPaths.SourceArtDir, ProjectPaths.ToolsDir })
                Directory.CreateDirectory(ProjectPaths.Get(dir));

            AssetDatabase.Refresh();
            List<string> issues = Check();
            log.AppendLine(issues.Count == 0 ? "Pruefung: alles am Soll-Platz." : "Pruefung – offen:\n- " + string.Join("\n- ", issues));
            return log.Length > 0 ? log.ToString().TrimEnd() : "Nichts zu tun.";
        }

        public static List<string> Check()
        {
            var issues = new List<string>();

            // Skripte direkt in Editor/ (ausser asmdef) gehoeren in einen Unterordner
            foreach (string file in Directory.GetFiles(Path.Combine(ProjectPaths.ProjectRoot, Scripts, "Editor"), "*.cs"))
                issues.Add("Editor-Skript ohne Aufgaben-Ordner: " + Path.GetFileName(file));
            foreach (string name in NetworkingTests)
                if (File.Exists(Path.Combine(ProjectPaths.ProjectRoot, Scripts, "Networking", name + ".cs")))
                    issues.Add("Test liegt noch im Spielcode: Networking/" + name + ".cs");

            if (AssetDatabase.IsValidFolder(ProjectSetup.Root + "/Materials"))
                issues.Add("Altordner vorhanden: " + ProjectSetup.Root + "/Materials (gehoert nach Art/Materials)");

            // Lose Dateien direkt in Assets/_Ashveil
            foreach (string file in Directory.GetFiles(Path.Combine(ProjectPaths.ProjectRoot, ProjectSetup.Root)))
                if (!file.EndsWith(".meta") && Path.GetFileName(file) != "README.md")
                    issues.Add("Lose Datei in _Ashveil: " + Path.GetFileName(file));

            // Eigene Inhalte ausserhalb von _Ashveil (Unity-/Paket-Ordner sind erlaubt)
            var allowed = new HashSet<string> { "_Ashveil", "Settings", "TextMesh Pro", "FishNet", "Plugins" };
            foreach (string dir in Directory.GetDirectories(Application.dataPath))
                if (!allowed.Contains(Path.GetFileName(dir)))
                    issues.Add("Unerwarteter Ordner in Assets: " + Path.GetFileName(dir));

            foreach (string legacy in new[] { "Art", "_Import" })
                if (Directory.Exists(ProjectPaths.Get(legacy)) &&
                    Directory.GetFiles(ProjectPaths.Get(legacy), "*", SearchOption.AllDirectories).Length > 0)
                    issues.Add($"Altordner neben Assets: {legacy} (gehoert nach {ProjectPaths.SourceArtDir})");

            foreach (string legacy in new[] { "Art", "_Import" })
                if (Directory.Exists(ProjectPaths.Get(legacy)) &&
                    Directory.GetFiles(ProjectPaths.Get(legacy), "*", SearchOption.AllDirectories).Length == 0)
                    issues.Add($"Leerer Altordner neben Assets: {legacy}");
            foreach (string file in LooseRootFiles())
                issues.Add($"Lose Datei im Projektordner: {Path.GetFileName(file)} (gehoert nach {Path.GetRelativePath(ProjectPaths.ProjectRoot, TargetFor(file) ?? ProjectPaths.ProjectRoot)})");

            if (!File.Exists(ProjectPaths.Get("README.md"))) issues.Add("README.md im Projektordner fehlt");
            if (!File.Exists(ProjectPaths.Get(ProjectPaths.DocsDir, "Ordnerstruktur.md"))) issues.Add("Docs/Ordnerstruktur.md fehlt");
            return issues;
        }

        // ------------------------------------------------------------------ Lose Dateien im Projektordner

        /// <summary>Dateien, die im Projektordner stehen duerfen (von Unity/IDE erzeugt oder Projekt-Stammdateien).</summary>
        private static bool AllowedInRoot(string name)
        {
            string ext = Path.GetExtension(name).ToLowerInvariant();
            return name == "README.md" || name == ".gitignore" || name == ".gitattributes" || name == ".vsconfig" ||
                   ext == ".sln" || ext == ".csproj" || ext == ".user";
        }

        private static IEnumerable<string> LooseRootFiles() =>
            Directory.GetFiles(ProjectPaths.ProjectRoot).Where(f => !AllowedInRoot(Path.GetFileName(f)));

        private static readonly string[] ArtSourceExt = { ".zip", ".7z", ".rar", ".unitypackage", ".fbx", ".blend", ".glb", ".gltf", ".obj", ".wav", ".ogg", ".mp3", ".flac" };
        private static readonly string[] ReferenceExt = { ".pdf", ".docx", ".doc", ".txt", ".md", ".png", ".jpg", ".jpeg", ".webp", ".psd" };

        /// <summary>Soll-Ordner fuer eine lose Datei (null = unbekannt, bleibt liegen).</summary>
        private static string TargetFor(string file)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ArtSourceExt.Contains(ext)) return ProjectPaths.SourceArt("ThirdParty");
            if (ReferenceExt.Contains(ext)) return ProjectPaths.Get(ProjectPaths.DocsDir, "Design", "Referenzen");
            return null;
        }

        /// <summary>
        /// Downloads und Referenzen aus dem Projektordner an ihren Platz: Pakete/Modelle/Klaenge → SourceArt/ThirdParty,
        /// Dokumente/Bilder → Docs/Design/Referenzen. Liegt dieselbe Datei (gleicher Inhalt) schon im Projekt, wird die
        /// Kopie entfernt. Gleicher Name mit anderem Inhalt: die neue bekommt das Datum angehaengt, nichts wird ueberschrieben.
        /// </summary>
        private static void SortRootFiles(StringBuilder log)
        {
            foreach (string file in LooseRootFiles().ToList())
            {
                string name = Path.GetFileName(file);
                string targetDir = TargetFor(file);
                if (targetDir == null)
                {
                    log.AppendLine($"Unbekannte Datei im Projektordner bleibt liegen (bitte pruefen): {name}");
                    continue;
                }
                try
                {
                    string twin = FindIdentical(file, ProjectPaths.SourceArt(), ProjectPaths.Get(ProjectPaths.DocsDir));
                    if (twin != null)
                    {
                        File.Delete(file);
                        log.AppendLine($"Doppelte Datei entfernt: {name} (liegt schon in {Path.GetRelativePath(ProjectPaths.ProjectRoot, twin)})");
                        continue;
                    }
                    Directory.CreateDirectory(targetDir);
                    string target = Path.Combine(targetDir, name);
                    if (File.Exists(target))
                        target = Path.Combine(targetDir, Path.GetFileNameWithoutExtension(name) + " (" + System.DateTime.Now.ToString("yyyy-MM-dd HHmm") + ")" + Path.GetExtension(name));
                    File.Move(file, target);
                    log.AppendLine($"{name}  ->  {Path.GetRelativePath(ProjectPaths.ProjectRoot, target)}");
                }
                catch (System.Exception e) when (e is IOException || e is System.UnauthorizedAccessException)
                {
                    log.AppendLine($"Hinweis: {name} ist gesperrt (noch im Download/OneDrive?) – spaeter erneut aufraeumen.");
                }
            }
        }

        /// <summary>Gleiche Datei (Groesse + SHA-256) irgendwo unter den Ordnern? Rueckgabe: Pfad oder null.</summary>
        private static string FindIdentical(string file, params string[] roots)
        {
            long size = new FileInfo(file).Length;
            string hash = null;
            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (string other in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    if (new FileInfo(other).Length != size) continue;
                    hash ??= Hash(file);
                    if (Hash(other) == hash) return other;
                }
            }
            return null;
        }

        private static string Hash(string file)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            using FileStream fs = File.OpenRead(file);
            return System.Convert.ToBase64String(sha.ComputeHash(fs));
        }

        private static void Move(string from, string to, StringBuilder log)
        {
            if (AssetDatabase.LoadMainAssetAtPath(from) == null && !File.Exists(from)) return;
            if (File.Exists(to))
            {
                log.AppendLine($"Ziel existiert schon, nicht verschoben: {from}");
                return;
            }
            string error = AssetDatabase.MoveAsset(from, to);
            log.AppendLine(string.IsNullOrEmpty(error) ? $"{from}  ->  {to}" : $"FEHLER {from}: {error}");
        }

        private static void MoveDirectory(string from, string to, StringBuilder log)
        {
            if (!Directory.Exists(from)) return;
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string rel = file.Substring(from.Length).TrimStart('\\', '/');
                string target = Path.Combine(to, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (File.Exists(target)) File.Delete(target); // neuere Kopie gewinnt nicht – Altordner ist die Quelle
                File.Move(file, target);
            }
            TryDeleteDirectory(from, log);
            log.AppendLine($"{Path.GetFileName(from)}/  ->  {Path.GetRelativePath(ProjectPaths.ProjectRoot, to)}/");
        }

        private static bool TryDeleteDirectory(string dir, StringBuilder log)
        {
            try
            {
                // Kopien aus OneDrive tragen oft „Schreibgeschuetzt“ – vor dem Loeschen zuruecksetzen
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                foreach (string d in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories)) new DirectoryInfo(d).Attributes = FileAttributes.Directory;
                new DirectoryInfo(dir).Attributes = FileAttributes.Directory;
                Directory.Delete(dir, true);
                return true;
            }
            catch (System.Exception e) when (e is IOException || e is System.UnauthorizedAccessException)
            {
                log.AppendLine($"Hinweis: Ordner {Path.GetRelativePath(ProjectPaths.ProjectRoot, dir)} ist gesperrt (OneDrive/Blender?) und bleibt vorerst – harmlos, spaeter erneut aufraeumen oder von Hand loeschen.");
                return false;
            }
        }

        private static bool IsEmptyFolder(string folder) =>
            AssetDatabase.IsValidFolder(folder) &&
            AssetDatabase.FindAssets("", new[] { folder }).Length == 0 &&
            AssetDatabase.GetSubFolders(folder).Length == 0;
    }

    /// <summary>Beim Start des Editors: Hinweis, wenn Downloads oder Dateien lose im Projektordner liegen.</summary>
    [InitializeOnLoad]
    internal static class LooseFileNotice
    {
        static LooseFileNotice()
        {
            if (SessionState.GetBool("Ashveil.LooseFileNotice", false)) return;
            SessionState.SetBool("Ashveil.LooseFileNotice", true);
            EditorApplication.delayCall += () =>
            {
                try
                {
                    var loose = ProjectStructure.Check().Where(i => i.StartsWith("Lose Datei im Projektordner") || i.StartsWith("Leerer Altordner")).ToList();
                    if (loose.Count > 0)
                        Debug.LogWarning("[Ashveil] Im Projektordner liegt etwas am falschen Platz – Ashveil › Projekt › Ordnerstruktur aufraeumen sortiert es ein:\n- " + string.Join("\n- ", loose));
                }
                catch (System.Exception) { }
            };
        }
    }
}
