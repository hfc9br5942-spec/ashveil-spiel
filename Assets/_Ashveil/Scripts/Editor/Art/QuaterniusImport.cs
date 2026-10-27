using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Liest die Quaternius-Pakete (CC0) direkt aus "SourceArt/ThirdParty" neben "Assets" und kopiert nur
    /// die benoetigten Unity-Dateien ins Projekt. Die ZIP-Dateien bleiben unveraendert liegen.
    ///
    ///   Menue: Ashveil > Import > Quaternius-Animationen einlesen
    /// </summary>
    public static class QuaterniusImport
    {
        public const string ArtRoot = ProjectSetup.Root + "/Art/ThirdParty/Quaternius";
        public const string MaleModelPath = ArtRoot + "/UAL1/UAL1_Standard.fbx";
        public const string Library2Path = ArtRoot + "/UAL2/UAL2_Standard.fbx";
        public const string FemaleModelPath = ArtRoot + "/Mannequin_F/Mannequin_F.fbx";
        public const string BaseCharactersFolder = ArtRoot + "/BaseCharacters";
        public const string BaseMalePath = BaseCharactersFolder + "/Superhero_Male_FullBody.fbx";
        public const string BaseFemalePath = BaseCharactersFolder + "/Superhero_Female_FullBody.fbx";
        public const string HairstylesFolder = ArtRoot + "/Hairstyles";

        /// <summary>Animationsbibliotheken vorhanden?</summary>
        public static bool IsImported =>
            AssetDatabase.LoadAssetAtPath<GameObject>(MaleModelPath) != null &&
            AssetDatabase.LoadAssetAtPath<GameObject>(Library2Path) != null;

        /// <summary>Texturierte Grundfiguren (Universal Base Characters) vorhanden?</summary>
        public static bool BaseCharactersImported =>
            AssetDatabase.LoadAssetAtPath<GameObject>(BaseMalePath) != null &&
            AssetDatabase.LoadAssetAtPath<GameObject>(BaseFemalePath) != null;

        public static bool HasZipsToImport =>
            Directory.Exists(ImportFolder) && Directory.GetFiles(ImportFolder, "*.zip").Length > 0;

        /// <summary>
        /// Ordnet eine Datei im ZIP einem Ziel (relativ zu ArtRoot) zu, oder null = nicht benoetigt.
        /// Es werden nur die Unity-Varianten uebernommen (keine Godot/Unreal/Blender-Dateien).
        /// </summary>
        private static string MapEntry(string name, string zipPath)
        {
            string file = Path.GetFileName(name);
            if (string.IsNullOrEmpty(file)) return null;
            bool png = file.EndsWith(".png");
            bool fbx = file.EndsWith(".fbx");
            bool normal = file.Contains("_Normal");

            if (name.EndsWith("/Unity/UAL1_Standard.fbx")) return "UAL1/UAL1_Standard.fbx";
            if (name.EndsWith("/Unity/UAL2_Standard.fbx")) return "UAL2/UAL2_Standard.fbx";
            if (name.EndsWith("/Female Mannequin/Unity/Mannequin_F.fbx")) return "Mannequin_F/Mannequin_F.fbx";

            // Universal Base Characters: Figuren + Texturen (Normalmaps in der Unity-Variante)
            if (fbx && name.Contains("/Base Characters/Unity/")) return "BaseCharacters/" + file;
            if (png && name.Contains("/Base Characters/Textures/Normals Unity - Godot/")) return "BaseCharacters/" + file;
            if (png && name.Contains("/Base Characters/Textures/") && !normal &&
                !name.Contains("/Base Characters/Textures/Normals")) return "BaseCharacters/" + file;

            // Frisuren fuer den spaeteren Charaktereditor (am Kopfknochen geriggt)
            if (fbx && name.Contains("/Hairstyles/Rigged to Head Bone/FBX (Unity)/")) return "Hairstyles/" + file;
            if (png && name.Contains("/Hairstyles/Textures/Normals Unity - Godot/")) return "Hairstyles/" + file;
            if (png && name.Contains("/Hairstyles/Textures/") && !normal &&
                !name.Contains("/Hairstyles/Textures/Normals")) return "Hairstyles/" + file;

            if (file.StartsWith("License") && file.EndsWith(".txt"))
                return "Licenses/" + Path.GetFileNameWithoutExtension(zipPath) + "_" + file;
            return null;
        }

        /// <summary>Quaternius-ZIPs (CC0) liegen in SourceArt/ThirdParty (frueher "_Import" – wird weiter gelesen, falls noch vorhanden).</summary>
        public static string ImportFolder
        {
            get
            {
                string now = Ashveil.Core.ProjectPaths.SourceArt("ThirdParty");
                string legacy = Ashveil.Core.ProjectPaths.Get("_Import");
                return !Directory.Exists(now) && Directory.Exists(legacy) ? legacy : now;
            }
        }

        [MenuItem("Ashveil/Import/Quaternius-Animationen einlesen", priority = 300)]
        public static void ImportFromMenu()
        {
            int copied = Run(out string report);
            if (copied < 0)
            {
                EditorUtility.DisplayDialog("Ashveil – Import", report, "OK");
                return;
            }

            PlayerAnimatorBuilder.Build();
            EditorUtility.DisplayDialog("Ashveil – Import",
                report + "\n\nAnimator erstellt. Jetzt 'Ashveil > Sandbox-Szene erstellen' ausfuehren.\n" +
                "Die Dateien liegen unter " + ArtRoot, "OK");
        }

        /// <summary>Kopiert die Dateien. Rueckgabe: Anzahl, oder -1 bei Fehler (report enthaelt den Grund).</summary>
        public static int Run(out string report)
        {
            string folder = ImportFolder;
            if (!Directory.Exists(folder))
            {
                report = "Ordner nicht gefunden:\n" + folder + "\n\nBitte die Quaternius-ZIPs dort ablegen.";
                return -1;
            }

            string[] zips = Directory.GetFiles(folder, "*.zip");
            if (zips.Length == 0)
            {
                report = "Keine ZIP-Dateien in\n" + folder;
                return -1;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string artAbs = Path.Combine(projectRoot, ArtRoot);
            var copiedFiles = new List<string>();

            try
            {
                foreach (string zipPath in zips)
                {
                    using (FileStream fs = File.OpenRead(zipPath))
                    using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
                    {
                        foreach (ZipArchiveEntry entry in zip.Entries)
                        {
                            string name = entry.FullName.Replace('\\', '/');
                            string target = MapEntry(name, zipPath);
                            if (target == null) continue;

                            string dest = Path.Combine(artAbs, target);
                            if (File.Exists(dest) && new FileInfo(dest).Length == entry.Length) continue;

                            EditorUtility.DisplayProgressBar("Ashveil – Import", "Entpacke " + target, 0.5f);
                            Directory.CreateDirectory(Path.GetDirectoryName(dest));
                            using (Stream src = entry.Open())
                            using (FileStream dst = File.Create(dest))
                                src.CopyTo(dst);
                            copiedFiles.Add(target);
                        }
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            if (!IsImported)
            {
                report = "In den ZIPs wurden die Unity-Dateien der Universal Animation Library nicht gefunden.\n" +
                         "Erwartet: 'Universal Animation Library[Standard].zip' und '... 2[Standard].zip'.";
                return -1;
            }

            report = copiedFiles.Count == 0
                ? "Alle Dateien waren bereits importiert."
                : "Importiert:\n" + string.Join("\n", copiedFiles);
            Debug.Log("[Ashveil] Quaternius-Import: " + report);
            return copiedFiles.Count;
        }
    }

    /// <summary>
    /// Import-Einstellungen fuer Quaternius-Modelle gemaess Hersteller-Anleitung:
    /// Bake Axis Conversion an, Rig = Humanoid, Clips mit "_Loop" im Namen werden geloopt.
    /// Die Animationen laufen auf der Stelle (Bewegung kommt vom CharacterMotor, kein Root Motion).
    /// </summary>
    public sealed class QuaterniusModelPostprocessor : AssetPostprocessor
    {
        /// <summary>Erhoehen, wenn sich die Einstellungen aendern – Unity importiert dann neu.</summary>
        public override uint GetVersion() => 6;

        /// <summary>Quaternius benennt Takes "Armature|Idle_Loop" – wir nutzen nur "Idle_Loop".</summary>
        public static string CleanClipName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            int bar = name.LastIndexOf('|');
            return bar >= 0 ? name.Substring(bar + 1) : name;
        }

        private static bool IsQuaternius(string path) => path.Replace('\\', '/').StartsWith(QuaterniusImport.ArtRoot);
        private static bool IsLibrary(string path) => path.Contains("/UAL1/") || path.Contains("/UAL2/");

        private void OnPreprocessTexture()
        {
            if (!IsQuaternius(assetPath)) return;
            var ti = (TextureImporter)assetImporter;
            if (System.IO.Path.GetFileName(assetPath).Contains("_Normal"))
                ti.textureType = TextureImporterType.NormalMap;
            // Figuren gestochen scharf (Wunsch Max 27.09.): volle Aufloesung, hochwertige Kompression (BC7/BC5),
            // anisotrope Filterung fuer schraege Blicke, leicht geschaerfte Mip-Stufen auf Distanz
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.anisoLevel = 8;
            ti.filterMode = FilterMode.Trilinear;
            ti.mipMapBias = -0.3f;
        }

        private void OnPreprocessModel()
        {
            if (!IsQuaternius(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            mi.bakeAxisConversion = true;
            mi.useFileScale = true;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            AvatarRegeln.OhneVerdrehungsverteilung(mi);
            mi.importAnimation = IsLibrary(assetPath);
            mi.importBlendShapes = true;
            // Figuren lesbar lassen: die Standhaltung misst daraus die Koerperoberflaeche (Haende liegen auf, statt durchzugleiten)
            mi.isReadable = !IsLibrary(assetPath);
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        }

        private void OnPreprocessAnimation()
        {
            if (!IsQuaternius(assetPath) || !IsLibrary(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            ModelImporterClipAnimation[] clips = mi.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                clip.name = CleanClipName(clip.name);
                clip.loopTime = clip.name.EndsWith("_Loop") || clip.name == "Sword_Idle";
                clip.loopPose = false;
                // Auf der Stelle: Drehung, Hoehe und Position in die Pose backen
                clip.lockRootRotation = true;
                clip.keepOriginalOrientation = true;
                clip.lockRootHeightY = true;
                clip.keepOriginalPositionY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalPositionXZ = true;
            }
            mi.clipAnimations = clips;
        }
    }
}
