using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Grafik-Pipeline (Art-Bible "Asche & Glut"): Unity startet Blender im Hintergrund, das Python-Skript
    /// <c>Tools/Blender/ashveil_build_assets.py</c> erzeugt Pixeltexturen und Low-Poly-Modelle,
    /// speichert die Quelle als .blend (fuer Max in Blender zum Weiterbearbeiten) und exportiert FBX.
    /// Danach werden Materialien angelegt und die Modelle importiert.
    /// </summary>
    public static class ArtPipeline
    {
        public const string ArtRoot = ProjectSetup.Root + "/Art";
        public const string TextureFolder = ArtRoot + "/Textures";
        public const string MaterialFolder = ArtRoot + "/Materials";
        public const string ModelFolder = ArtRoot + "/Models";
        private const string BlenderPathKey = "Ashveil.BlenderPath";

        private static string ProjectDir => Ashveil.Core.ProjectPaths.ProjectRoot;
        private static string ScriptPath => Ashveil.Core.ProjectPaths.Tools("Blender", "ashveil_build_assets.py");
        private static string GiantScriptPath => Ashveil.Core.ProjectPaths.Tools("Blender", "ashveil_build_giant.py");
        private static string ExportDir => Ashveil.Core.ProjectPaths.SourceArt("Export");
        private static string BlendPath => Ashveil.Core.ProjectPaths.SourceArt("Blender", "Ashveil_Kareth_Props.blend");
        public static string LogPath => Ashveil.Core.ProjectPaths.Logs("Ashveil_Blender.txt");

        /// <summary>Materialien mit Wind-Shader (Vegetation): Hoehe fuer vollen Ausschlag, Staerke, Tempo.</summary>
        private static readonly (string name, float swayHeight, float strength, float speed, float normalUp)[] Foliage =
        {
            ("M_Grass", 0.55f, 0.07f, 1.4f, 0.85f),
            ("M_Fern", 0.5f, 0.05f, 1.1f, 0.7f),
            ("M_PineNeedles", 12f, 0.35f, 0.55f, 0.35f)
        };

        // ================================================================== Blender starten

        [MenuItem("Ashveil/Grafik/Blender-Modelle erzeugen", priority = 400)]
        public static void BuildWithBlenderMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play beenden.", "OK");
                return;
            }
            bool ok = BuildWithBlender(out string summary);
            EditorUtility.DisplayDialog("Ashveil", summary, "OK");
            if (!ok) Debug.LogError("[Ashveil] " + summary);
        }

        [MenuItem("Ashveil/Grafik/Riesen erzeugen (Blender)", priority = 402)]
        public static void BuildGiantMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play beenden.", "OK");
                return;
            }
            bool ok = BuildGiantWithBlender(out string summary);
            EditorUtility.DisplayDialog("Ashveil", summary, "OK");
            if (!ok) Debug.LogError("[Ashveil] " + summary);
        }

        /// <summary>M7: Zopf, Irokese, Kinnbart, Schnauzer als Blender-Modelle (Quelle zum Weiterbearbeiten) und danach
        /// den Aussehen-Katalog neu eintragen – der nimmt die Modelle statt der Grundformen.</summary>
        [MenuItem("Ashveil/Grafik/Haar- und Bartformen erzeugen (Blender)", priority = 403)]
        public static void BuildHairMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play beenden.", "OK");
                return;
            }
            bool ok = BuildHairWithBlender(out string summary);
            if (ok) AppearanceContentBuilder.BuildFromMenu();
            EditorUtility.DisplayDialog("Ashveil", summary, "OK");
            if (!ok) Debug.LogError("[Ashveil] " + summary);
        }

        public static bool BuildHairWithBlender(out string summary) =>
            RunBlender(Ashveil.Core.ProjectPaths.Tools("Blender", "ashveil_build_hair.py"),
                Ashveil.Core.ProjectPaths.SourceArt("Blender", "Ashveil_Haare_Baerte.blend"), "Haar- und Bartformen", out summary);

        /// <summary>Der versteinerte Beschuetzer (Nachtrag 26.09.): Module, LOD1, Schaedeldecke, Armreste.</summary>
        public static bool BuildGiantWithBlender(out string summary) =>
            RunBlender(GiantScriptPath, Ashveil.Core.ProjectPaths.SourceArt("Blender", "Ashveil_Riese.blend"), "den Riesen", out summary);

        public static bool BuildWithBlender(out string summary) => RunBlender(ScriptPath, BlendPath, "Modelle", out summary);

        private static bool RunBlender(string scriptPath, string blendPath, string what, out string summary)
        {
            string blender = FindBlender();
            if (blender == null)
            {
                summary = "Blender nicht gefunden. Erwartet unter C:/Program Files/Blender Foundation/Blender x.y/blender.exe " +
                          "(oder Pfad in EditorPrefs '" + BlenderPathKey + "').";
                return false;
            }
            if (!File.Exists(scriptPath))
            {
                summary = "Blender-Skript fehlt: " + scriptPath;
                return false;
            }

            string textures = Path.GetFullPath(Path.Combine(ProjectDir, TextureFolder));
            Directory.CreateDirectory(textures);
            Directory.CreateDirectory(ExportDir);
            Directory.CreateDirectory(Path.GetDirectoryName(blendPath));
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath));

            string logPath = scriptPath == GiantScriptPath ? LogPath.Replace(".txt", "_Riese.txt")
                : scriptPath.EndsWith("ashveil_build_hair.py") ? LogPath.Replace(".txt", "_Haare.txt") : LogPath;
            if (!RunBlenderProcess(blender, scriptPath, $"--textures \"{textures}\" --export \"{ExportDir}\" --blend \"{blendPath}\"",
                    logPath, what, out _, out summary))
                return false;

            // 1) Texturen importieren (Punktfilter), 2) Materialien, 3) Modelle kopieren und importieren
            AssetDatabase.Refresh();
            EnsureMaterials();
            ProjectSetup.EnsureFolder(ModelFolder);
            int models = 0;
            foreach (string fbx in Directory.GetFiles(ExportDir, "*.fbx"))
            {
                string dest = Path.Combine(ProjectDir, ModelFolder, Path.GetFileName(fbx));
                File.Copy(fbx, dest, true);
                models++;
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            summary = $"{summary}: {models} Modelle, Quelle {blendPath}. " +
                      "Szene neu erstellen, damit sie in Kareth erscheinen.";
            Debug.Log("[Ashveil] " + summary);
            return true;
        }

        /// <summary>
        /// Blender im Hintergrund mit einem Skript starten (Fortschrittsbalken, Protokoll-Datei, Abbruch nach 10 min).
        /// Erfolgreich, wenn Blender mit 0 endet und das Skript "[Ashveil] FERTIG" meldet.
        /// </summary>
        public static bool RunBlenderProcess(string blender, string scriptPath, string scriptArgs, string logPath, string what,
            out string output, out string summary)
        {
            var psi = new ProcessStartInfo
            {
                FileName = blender,
                Arguments = $"-b --factory-startup --python-exit-code 3 -P \"{scriptPath}\" -- {scriptArgs}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = ProjectDir,
                // Blender schreibt UTF-8; ohne Angabe liest Windows die Ausgabe in der OEM-Codepage („·“ wurde „┬À“, 05.10.)
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            var sb = new StringBuilder();
            int exit;
            var sw = Stopwatch.StartNew();
            try
            {
                using (Process p = Process.Start(psi))
                {
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine("ERR " + e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    while (!p.WaitForExit(250))
                    {
                        if (sw.Elapsed.TotalSeconds > 600) { p.Kill(); break; }
                        EditorUtility.DisplayProgressBar("Ashveil", $"Blender erzeugt {what} ... ({sw.Elapsed.TotalSeconds:0} s)",
                            (float)Math.Min(0.95, sw.Elapsed.TotalSeconds / 60.0));
                    }
                    p.WaitForExit();
                    exit = p.ExitCode;
                }
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                output = "";
                summary = "Blender konnte nicht gestartet werden: " + e.Message;
                return false;
            }
            EditorUtility.ClearProgressBar();
            lock (sb) output = sb.ToString();
            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
            File.WriteAllText(logPath, $"Blender: {blender}\nExit: {exit} ({sw.Elapsed.TotalSeconds:0.0} s)\n\n{output}");
            // 19.10.2026: Neuere Skripte (Figuren, Mocap) melden „ASHVEIL_ERGEBNIS: OK …“ statt „[Ashveil] FERTIG“ – beides gilt als
            // Erfolg. Vorher stand bei jedem erfolgreichen Figurenlauf „mit Fehler 0 beendet“ (echte Fehler nicht mehr erkennbar).
            bool fertig = output.Contains("[Ashveil] FERTIG") || output.Contains("ASHVEIL_ERGEBNIS: OK");
            if (exit != 0 || !fertig)
            {
                summary = exit != 0
                    ? $"Blender ist mit Fehler {exit} beendet worden. Protokoll: {logPath}"
                    : $"Blender beendet, aber ohne Erfolgsmeldung des Skripts (Python-Fehler?). Protokoll: {logPath}";
                return false;
            }
            summary = $"Blender fertig ({sw.Elapsed.TotalSeconds:0} s)";
            return true;
        }

        /// <summary>EditorPrefs: Testversionen (Alpha/Beta/RC) zulassen – Standard aus (Max 06.10.: neueste Version, aber stabil).</summary>
        public const string AllowTestBuildsKey = "Ashveil.Blender.Testversionen";

        /// <summary>
        /// Neueste installierte Blender-Version, die stabil ist (06.10.: 5.3 war noch Alpha – eine Testversion soll die Pipeline
        /// nicht stillschweigend übernehmen). Reihenfolge nach Versionsnummer, nicht alphabetisch („5.10“ nach „5.9“).
        /// Sind nur Testversionen installiert, wird die neueste davon genommen (der Bericht nennt die Version).
        /// </summary>
        public static string FindBlender()
        {
            string custom = EditorPrefs.GetString(BlenderPathKey, "");
            if (!string.IsNullOrEmpty(custom) && File.Exists(custom)) return custom;
            bool allowTest = EditorPrefs.GetBool(AllowTestBuildsKey, false);
            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                @"C:\Program Files",
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };
            var candidates = new System.Collections.Generic.List<(Version v, string exe)>();
            foreach (string root in roots.Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string foundation = Path.Combine(root, "Blender Foundation");
                if (!Directory.Exists(foundation)) continue;
                foreach (string dir in Directory.GetDirectories(foundation))
                {
                    string exe = Path.Combine(dir, "blender.exe");
                    if (File.Exists(exe)) candidates.Add((FolderVersion(dir), exe));
                }
            }
            string fallback = null;
            foreach (var c in candidates.OrderByDescending(k => k.v).ThenByDescending(k => k.exe, StringComparer.OrdinalIgnoreCase))
            {
                if (allowTest || IsStable(BlenderVersion(c.exe))) return c.exe;
                fallback = fallback ?? c.exe;
            }
            return fallback;
        }

        private static readonly System.Collections.Generic.Dictionary<string, string> VersionCache =
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Erste Zeile von „blender --version“, z. B. „Blender 5.2.2 LTS“ oder „Blender 5.3.0 Alpha“ („?“ bei Fehler).</summary>
        public static string BlenderVersion(string exe)
        {
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return "?";
            string key = exe + "|" + File.GetLastWriteTimeUtc(exe).Ticks;
            if (VersionCache.TryGetValue(key, out string cached)) return cached;
            string line = "?";
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe, Arguments = "--version", UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8
                };
                using (Process p = Process.Start(psi))
                {
                    var read = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(20000)) { try { p.Kill(); } catch (Exception) { } }
                    string all = read.Wait(3000) ? read.Result : "";
                    line = all.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("Blender ")) ?? "?";
                }
            }
            catch (Exception) { line = "?"; }
            VersionCache[key] = line;
            return line;
        }

        /// <summary>Stabil = keine Alpha-, Beta- oder Release-Candidate-Kennung in der Versionszeile.</summary>
        public static bool IsStable(string versionLine)
        {
            if (string.IsNullOrEmpty(versionLine) || versionLine == "?") return true;   // unbekannt: nicht ausschließen
            string v = versionLine.ToLowerInvariant();
            return !(v.Contains("alpha") || v.Contains("beta") || v.Contains("release candidate") || v.Contains(" rc"));
        }

        private static Version FolderVersion(string dir)
        {
            var m = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(dir) ?? "", @"(\d+)\.(\d+)(?:\.(\d+))?");
            if (!m.Success) return new Version(0, 0);
            return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
        }

        // ================================================================== Materialien

        public static void EnsureMaterials()
        {
            ProjectSetup.EnsureFolder(MaterialFolder);
            string texDir = Path.Combine(ProjectDir, TextureFolder);
            if (!Directory.Exists(texDir)) return;
            foreach (string png in Directory.GetFiles(texDir, "T_*.png"))
            {
                string key = Path.GetFileNameWithoutExtension(png).Substring(2);
                MaterialFor("M_" + key, true);
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Material zum Namen aus Blender (M_Xyz) – anlegen/aktualisieren mit Textur T_Xyz.</summary>
        public static Material MaterialFor(string name, bool createOrUpdate)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!createOrUpdate) return mat;

            var foliage = Foliage.FirstOrDefault(f => f.name == name);
            bool isFoliage = foliage.name != null;
            Shader shader = isFoliage ? Shader.Find("Ashveil/Foliage") : Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/T_{name.Substring(2)}.png");
            if (tex != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (isFoliage)
            {
                mat.SetFloat("_SwayHeight", foliage.swayHeight);
                mat.SetFloat("_WindStrength", foliage.strength);
                mat.SetFloat("_WindSpeed", foliage.speed);
                mat.SetFloat("_NormalUp", foliage.normalUp);
                // Gras und Farn werfen keine Schatten (sonst verschatten sich die dichten Bueschel gegenseitig schwarz)
                bool small = foliage.swayHeight < 2f;
                mat.SetShaderPassEnabled("ShadowCaster", !small);
                mat.SetColor("_BaseColor", small ? new Color(1.6f, 1.6f, 1.35f, 1f) : new Color(1.12f, 1.15f, 1.08f, 1f));
            }
            else
            {
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
                if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", 0f);
                if (mat.HasProperty("_EnvironmentReflections")) mat.SetFloat("_EnvironmentReflections", 0f);
                mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            }
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }

    /// <summary>Import-Regeln: Pixeltexturen scharf (Punktfilter), Modelle in Metern, eigene Materialien.</summary>
    public sealed class ArtImportRules : AssetPostprocessor
    {
        private bool IsArtTexture => assetPath.Replace('\\', '/').Contains("/_Ashveil/Art/Textures/");
        private bool IsArtModel => assetPath.Replace('\\', '/').Contains("/_Ashveil/Art/Models/");

        private void OnPreprocessTexture()
        {
            if (!IsArtTexture) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.filterMode = FilterMode.Point;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.anisoLevel = 0;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.maxTextureSize = 256;
            ti.sRGBTexture = true;
            ti.alphaIsTransparency = false;
        }

        private void OnPreprocessModel()
        {
            if (assetPath.Replace('\\', '/').Contains("/_Ashveil/Art/Models_Eigene/"))
            {
                // Eigene Modelle: Materialien und Achsen bleiben wie geliefert, nur lesbar fuer Kollision und Gras
                ((ModelImporter)assetImporter).isReadable = true;
                return;
            }
            if (!IsArtModel) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = false; // Achsen backt Blender beim Export (bake_space_transform)
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.None;
            mi.importAnimation = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = true; // MeshCollider und Terrain-Details brauchen lesbare Meshes
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        }

        private Material OnAssignMaterialModel(Material material, Renderer renderer)
        {
            if (!IsArtModel || material == null) return null;
            string name = material.name;
            int dot = name.IndexOf('.');
            if (dot > 0) name = name.Substring(0, dot); // "M_Rock.001" -> "M_Rock"
            return ArtPipeline.MaterialFor(name, false);
        }
    }
}
