using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Art-Pass 3 (Art-Direction-Bible § 51–53): eigene Basiskoerper CHR_M_Base01 / CHR_F_Base01 als Greybox.
    ///
    /// Eine Stelle fuer die Koerperwahl (Spieler, NPCs, Gegner, Charakter-Editor): <see cref="MalePath"/>/<see cref="FemalePath"/>.
    /// Die neuen Koerper sind **zuschaltbar** (Ashveil › Figuren › Neue Basiskoerper verwenden, Standard aus) – bis Max sie
    /// freigibt, bleiben alle Figuren und Speicherstaende auf den Quaternius-Koerpern. Koerper-Index 0 = Mann, 1 = Frau bleibt.
    ///
    /// Erzeugen: Tools/Characters/make_base_bodies.py (Proportionstabelle -> Daten) und Tools/Blender/ashveil_build_body.py
    /// (Skelett mit Quaternius-Knochennamen, Gewichte, Reduzieren, FBX, Kontrollbilder).
    /// </summary>
    public static class CharacterBodies
    {
        public const string Folder = ProjectSetup.Root + "/Art/Characters/Bodies";
        public const string MaterialFolder = Folder + "/Materials";
        public const string NewMalePath = Folder + "/CHR_M_Base01.fbx";
        public const string NewFemalePath = Folder + "/CHR_F_Base01.fbx";
        /// <summary>17.10. abends (Max: „Jetzt ins Bewegungslabor“): MPFB2-Körper (MakeHuman, CC0) als Probe statt der Greybox.
        /// Quelle Tools/Blender/ashveil_mpfb_koerper.py → SourceArt/Characters/MPFB → Auftrag „mpfb_import“. Gleiche Knochennamen
        /// wie Quaternius (Skelett-Vertrag), darum gleiche Humanoid-Zuordnung. Umschalter „Ashveil.MPFBMann“ (Auftrag basiskoerper|mpfb).</summary>
        public const string MpfbMalePath = Folder + "/CHR_M_MPFB01.fbx";
        public const string MpfbPrefKey = "Ashveil.MPFBMann";
        public static bool UseMpfbMale => EditorPrefs.GetBool(MpfbPrefKey, false) && AssetDatabase.LoadAssetAtPath<GameObject>(MpfbMalePath) != null;
        /// <summary>18.10. (Max: „mach dann bitte mit dem frauenmodell weiter“): MPFB-Frau, gleicher Umschalter wie der Mann.</summary>
        public const string MpfbFemalePath = Folder + "/CHR_W_MPFB01.fbx";
        public static bool UseMpfbFemale => EditorPrefs.GetBool(MpfbPrefKey, false) && AssetDatabase.LoadAssetAtPath<GameObject>(MpfbFemalePath) != null;
        private const string PrefKey = "Ashveil.NeueBasiskoerper";
        private const string ToggleMenu = "Ashveil/Figuren/Neue Basiskörper verwenden";

        public static string DataDir => Ashveil.Core.ProjectPaths.SourceArt("Characters");
        public static string ExportDir => Ashveil.Core.ProjectPaths.SourceArt("Export", "Characters");
        public static string BlendPath => Ashveil.Core.ProjectPaths.SourceArt("Blender", "Ashveil_BaseCharacters.blend");
        public static string PreviewDir => Ashveil.Core.ProjectPaths.Logs("Koerper");
        public static string ReportPath => Ashveil.Core.ProjectPaths.Logs("Ashveil_Koerper.txt");

        public static bool NewBodiesAvailable =>
            AssetDatabase.LoadAssetAtPath<GameObject>(NewMalePath) != null && AssetDatabase.LoadAssetAtPath<GameObject>(NewFemalePath) != null;

        /// <summary>Neue Basiskoerper eingeschaltet (und vorhanden)?</summary>
        public static bool UseNew => EditorPrefs.GetBool(PrefKey, false) && NewBodiesAvailable;

        /// <summary>Koerpermodell Mann: neue Basis (falls eingeschaltet) &gt; Quaternius-Figur &gt; graues Mannequin.</summary>
        public static string MalePath => TripoKoerper.UseMale ? TripoKoerper.MalePath : UseMpfbMale ? MpfbMalePath : UseNew ? NewMalePath
            : QuaterniusImport.BaseCharactersImported ? QuaterniusImport.BaseMalePath : QuaterniusImport.MaleModelPath;

        // 21.10.: Tripo-Frau mit Mixamo-Skelett (TripoKoerper, Schalter „Ashveil.TripoFrau“) hat Vorrang
        public static string FemalePath => TripoKoerper.UseFemale ? TripoKoerper.FemalePath : UseMpfbFemale ? MpfbFemalePath : UseNew ? NewFemalePath
            : QuaterniusImport.BaseCharactersImported ? QuaterniusImport.BaseFemalePath : QuaterniusImport.FemaleModelPath;

        public static string PathFor(bool female) => female ? FemalePath : MalePath;

        [MenuItem(ToggleMenu, priority = 520)]
        private static void Toggle()
        {
            bool on = !EditorPrefs.GetBool(PrefKey, false);
            if (on && !NewBodiesAvailable)
            {
                EditorUtility.DisplayDialog("Ashveil", "Die neuen Basiskörper sind noch nicht erzeugt (Ashveil › Figuren › Basiskörper erzeugen).", "OK");
                return;
            }
            EditorPrefs.SetBool(PrefKey, on);
            EditorUtility.DisplayDialog("Ashveil", on
                ? "Neue Basiskörper (Greybox) eingeschaltet. Jetzt Ashveil › Daten › Aussehen-Katalog aktualisieren und die Szene neu erstellen."
                : "Zurück auf die Quaternius-Körper. Aussehen-Katalog aktualisieren und die Szene neu erstellen.", "OK");
        }

        [MenuItem(ToggleMenu, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(ToggleMenu, EditorPrefs.GetBool(PrefKey, false));
            return true;
        }

        // ================================================================== Erzeugen

        [MenuItem("Ashveil/Figuren/Basiskörper erzeugen (Blender)", priority = 500)]
        public static void BuildMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play beenden.", "OK");
                return;
            }
            bool ok = Build(out string summary);
            EditorUtility.DisplayDialog("Ashveil", summary, "OK");
            if (!ok) Debug.LogError("[Ashveil] " + summary);
        }

        public static bool Build(out string summary)
        {
            string blender = ArtPipeline.FindBlender();
            if (blender == null)
            {
                summary = "Blender nicht gefunden (C:/Program Files/Blender Foundation/…).";
                return false;
            }
            string script = Ashveil.Core.ProjectPaths.Tools("Blender", "ashveil_build_body.py");
            if (!File.Exists(Path.Combine(DataDir, "CHR_M_Base01.json")))
            {
                summary = "Körperdaten fehlen in " + DataDir + " (Tools/Characters/make_base_bodies.py ausführen).";
                return false;
            }
            string reference = Path.GetFullPath(Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, QuaterniusImport.BaseCharactersFolder));
            Directory.CreateDirectory(ExportDir);
            foreach (string old in Directory.GetFiles(ExportDir, "*.fbx")) File.Delete(old);
            string args = $"--data \"{DataDir}\" --reference \"{reference}\" --export \"{ExportDir}\" --blend \"{BlendPath}\" --preview \"{PreviewDir}\"";
            if (!ArtPipeline.RunBlenderProcess(blender, script, args, Ashveil.Core.ProjectPaths.Logs("Ashveil_Blender_Koerper.txt"),
                    "die Basiskörper", out string output, out summary))
                return false;

            EnsureMaterials();
            ProjectSetup.EnsureFolder(Folder);
            int n = 0;
            foreach (string fbx in Directory.GetFiles(ExportDir, "*.fbx"))
            {
                File.Copy(fbx, Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, Folder, Path.GetFileName(fbx)), true);
                n++;
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            ConfigureAvatar(NewMalePath, QuaterniusImport.BaseMalePath);
            ConfigureAvatar(NewFemalePath, QuaterniusImport.BaseFemalePath);
            bool checkOk = Check(out string report);
            string blenderLines = string.Join("\n", output.Split('\n').Where(l => l.StartsWith("[Ashveil]") && l.Contains("Dreiecke")).Select(l => l.Trim()));
            summary = $"{summary}: {n} Körper, Quelle {BlendPath}.\n{blenderLines}\n\nPrüfung: {(checkOk ? "bestanden" : "FEHLER – siehe Bericht")} ({ReportPath}).\n" +
                      "Kontrollbilder: Logs/Ashveil/Koerper. Einschalten: Ashveil › Figuren › Neue Basiskörper verwenden.";
            Debug.Log("[Ashveil] " + summary + "\n" + report);
            return checkOk;
        }

        /// <summary>Material je Koerper: URP Lit mit der Quaternius-Hauttextur (die UVs liegen auf einer einfarbigen Hautstelle).</summary>
        public static void EnsureMaterials()
        {
            ProjectSetup.EnsureFolder(MaterialFolder);
            Material(MaterialFolder + "/M_CHR_M_Base01.mat", QuaterniusImport.BaseCharactersFolder + "/T_Superhero_Male_Ligh.png");
            Material(MaterialFolder + "/M_CHR_F_Base01.mat", QuaterniusImport.BaseCharactersFolder + "/T_Superhero_Female_Light_BaseColor.png");
            AssetDatabase.SaveAssets();
        }

        private static void Material(string path, string texPath)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (mat == null)
            {
                mat = new Material(lit);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = lit;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex != null && mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.28f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
        }

        /// <summary>
        /// Humanoid-Avatar mit exakt der Knochenzuordnung der Quaternius-Figur (gleiche Namen) und der Ruhepose des neuen
        /// Koerpers. Dadurch laufen alle Quaternius-Animationen per Retargeting (Bible § 64).
        /// </summary>
        public static void ConfigureAvatar(string path, string referencePath)
        {
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            var refImp = AssetImporter.GetAtPath(referencePath) as ModelImporter;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (imp == null || refImp == null || model == null) return;
            HumanDescription desc = refImp.humanDescription;
            var skeleton = new List<SkeletonBone>();
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                skeleton.Add(new SkeletonBone
                {
                    name = t == model.transform ? model.name + "(Clone)" : t.name,
                    position = t.localPosition, rotation = t.localRotation, scale = t.localScale
                });
            }
            desc.skeleton = skeleton.ToArray();
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.humanDescription = desc;
            imp.SaveAndReimport();
        }

        // ================================================================== Pruefen

        [MenuItem("Ashveil/Figuren/Basiskörper prüfen", priority = 501)]
        public static void CheckMenu()
        {
            bool ok = Check(out string report);
            EditorUtility.DisplayDialog("Ashveil", (ok ? "Basiskörper in Ordnung." : "Basiskörper mit Fehlern.") + "\nBericht: " + ReportPath, "OK");
            Debug.Log("[Ashveil] " + report);
        }

        public static bool Check(out string report)
        {
            var sb = new StringBuilder();
            var c = CultureInfo.InvariantCulture;
            sb.AppendLine($"ASHVEIL Basiskörper-Prüfung (Art-Pass 3) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            bool all = true;
            foreach (var (label, path, refPath) in new[]
                     {
                         ("CHR_M_Base01", NewMalePath, QuaterniusImport.BaseMalePath),
                         ("CHR_F_Base01", NewFemalePath, QuaterniusImport.BaseFemalePath)
                     })
            {
                sb.AppendLine();
                sb.AppendLine($"[{label}]");
                bool ok = CheckOne(path, refPath, sb, c);
                all &= ok;
            }
            sb.AppendLine();
            sb.AppendLine($"ERGEBNIS: {(all ? "bestanden" : "NICHT bestanden")} · neue Körper im Spiel: {(UseNew ? "EIN" : "aus (Standard)")}");
            report = sb.ToString();
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, report);
            return all;
        }

        private static bool CheckOne(string path, string refPath, StringBuilder sb, CultureInfo c)
        {
            bool ok = true;
            void Line(bool pass, string text)
            {
                sb.AppendLine((pass ? "  OK    " : "  FEHLER ") + text);
                ok &= pass;
            }
            void Info(string text) => sb.AppendLine("  ·     " + text);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var refPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(refPath);
            Line(prefab != null, "FBX importiert: " + path);
            if (prefab == null || refPrefab == null) return false;
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            Line(imp != null && imp.isReadable, "Mesh lesbar (Schalen, Gesichtsebenen und Standhaltung lesen die Punkte)");

            GameObject a = UnityEngine.Object.Instantiate(prefab), b = UnityEngine.Object.Instantiate(refPrefab);
            a.hideFlags = b.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var animA = a.GetComponent<Animator>();
                var animB = b.GetComponent<Animator>();
                bool human = animA != null && animA.avatar != null && animA.avatar.isValid && animA.avatar.isHuman;
                Line(human, "Humanoid-Avatar gültig");

                // Knochen: Namen und Eltern wie Quaternius
                var ta = a.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name, t => t);
                var refBones = b.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.GetComponentInParent<Animator>() != null && IsBone(t)).ToList();
                var missing = refBones.Where(t => !ta.ContainsKey(t.name)).Select(t => t.name).ToList();
                var wrongParent = refBones.Where(t => ta.TryGetValue(t.name, out Transform x) && t.parent != null && x.parent != null &&
                                                      t.parent.name != x.parent.name && IsBone(t.parent)).Select(t => t.name).ToList();
                Line(missing.Count == 0 && wrongParent.Count == 0,
                    $"Skelett-Vertrag: {refBones.Count} Knochen wie Quaternius" +
                    (missing.Count > 0 ? $" – fehlen: {string.Join(", ", missing.Take(8))}" : "") +
                    (wrongParent.Count > 0 ? $" – andere Eltern: {string.Join(", ", wrongParent.Take(8))}" : ""));

                // Humanoid-Zuordnung: jede Rolle auf denselben Knochennamen (inkl. aller Finger)
                int roles = 0, same = 0;
                var diff = new List<string>();
                if (human && animB != null)
                {
                    foreach (HumanBodyBones hb in Enum.GetValues(typeof(HumanBodyBones)))
                    {
                        if (hb == HumanBodyBones.LastBone) continue;
                        Transform x = animA.GetBoneTransform(hb), y = animB.GetBoneTransform(hb);
                        if (y == null) continue;
                        roles++;
                        if (x != null && x.name == y.name) same++;
                        else diff.Add($"{hb}={(x != null ? x.name : "-")}");
                    }
                }
                Line(roles > 0 && same == roles, $"Humanoid-Zuordnung: {same}/{roles} Rollen gleich (Finger, Schultern, Hüfte, Füße – Bible § 64)" +
                                                 (diff.Count > 0 ? " – " + string.Join(", ", diff.Take(6)) : ""));

                // Mesh
                var smrs = a.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Line(smrs.Length == 1, $"Ein Körper-Renderer ({smrs.Length}) – AppearanceRig nimmt den größten");
                if (smrs.Length > 0)
                {
                    var smr = smrs.OrderByDescending(s => s.sharedMesh != null ? s.sharedMesh.vertexCount : 0).First();
                    Mesh m = smr.sharedMesh;
                    int tris = m.triangles.Length / 3;
                    Line(tris <= 16000, $"{tris} Dreiecke, {m.vertexCount} Punkte (Budget LOD0 ≤ 16 000 für die Greybox; Quaternius {RefTris(b)})");
                    var bw = m.GetAllBoneWeights();
                    var per = m.GetBonesPerVertex();
                    int maxPer = 0;
                    for (int i = 0; i < per.Length; i++) maxPer = Math.Max(maxPer, per[i]);
                    Line(maxPer <= 4 && bw.Length > 0, $"Höchstens {maxPer} Knochen je Punkt");
                    Line(smr.bones.Length > 60, $"{smr.bones.Length} Knochen am Renderer");
                    Bounds wb = smr.bounds; // Weltmeter in der Ruhepose
                    Info($"Höhe {wb.max.y.ToString("0.00", c)} m, Spannweite {wb.size.x.ToString("0.00", c)} m (T-Pose)");
                    var uv = m.uv;
                    int face = 0;
                    int[] t = m.triangles;
                    for (int i = 0; i < t.Length; i += 3)
                    {
                        Vector2 u0 = uv.Length > 0 ? uv[t[i]] : Vector2.zero;
                        if (u0.x < 0.375f && u0.y > 0.625f) face++;
                    }
                    Info(face == 0
                        ? "Gesichtsebenen (Narben, Bemalung, Stoppeln) bleiben auf der Greybox leer – designte Köpfe folgen in Pass 6"
                        : $"{face} Dreiecke im Gesichts-UV-Bereich");
                }
                Info("Proportionen: Tools/Characters/make_base_bodies.py (Tabelle) · Kontrollbilder: Logs/Ashveil/Koerper");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(a);
                UnityEngine.Object.DestroyImmediate(b);
            }
            return ok;
        }

        private static bool IsBone(Transform t) =>
            t.GetComponent<Renderer>() == null && t.GetComponent<Animator>() == null && t.name != "Armature";

        private static int RefTris(GameObject b)
        {
            var smr = b.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(s => s.sharedMesh != null ? s.sharedMesh.vertexCount : 0).FirstOrDefault();
            return smr != null && smr.sharedMesh != null ? smr.sharedMesh.triangles.Length / 3 : 0;
        }
    }

    /// <summary>Import der eigenen Figuren (Art/Characters/Bodies): wie die Quaternius-Figuren – Humanoid, lesbar, Meter.</summary>
    public sealed class CharacterImportRules : AssetPostprocessor
    {
        public override uint GetVersion() => 2;

        private bool IsBody => assetPath.Replace('\\', '/').StartsWith(CharacterBodies.Folder + "/") && assetPath.EndsWith(".fbx");

        private void OnPreprocessModel()
        {
            if (!IsBody) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
            mi.importBlendShapes = true;
            mi.importAnimation = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.isReadable = true;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            if (mi.animationType != ModelImporterAnimationType.Human)
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            }
        }

        /// <summary>Teil eines MPFB-Körpers aus Material- oder Objektnamen (Blender: NAME_Augen, NAME_Brauen, … ; Material
        /// low-poly, eyebrow001, eyelashes01, short02, body). null = kein MPFB-Name erkannt.</summary>
        public static string MpfbTeil(string matName, string objName)
        {
            string n = (matName + " " + objName).ToLowerInvariant();
            if (n.Contains("haarkarten")) return "haarkarten";   // 19.10.: Strähnen-Karten (Alpha-Atlas) – eigenes Material, nicht Haut
            if (n.Contains("zopf")) return "zopf";               // 19.10. abends: Meshy-Pferdeschwanz (ashveil_zopf_meshy.py), eigene gebackene Farbe
            if (n.Contains("eyebrow") || n.Contains("_brauen")) return "brauen";
            if (n.Contains("eyelash") || n.Contains("_wimpern")) return "wimpern";
            if (n.Contains("low-poly") || n.Contains("high-poly") || n.Contains("_augen")) return "augen";
            if (n.Contains("short0") || n.Contains("hair") || n.Contains("_haare")) return "haare";
            if (n.Contains("mpfb")) return "haut";
            return null;
        }

        private Material OnAssignMaterialModel(Material material, Renderer renderer)
        {
            if (!IsBody) return null;
            string file = Path.GetFileNameWithoutExtension(assetPath);
            // 17.10. nachts: MPFB-Körper haben eigene Teile (Haut, Augen, Brauen, Wimpern, Haare) – je Teil das passende
            // Material M_<Datei>_<teil>.mat (angelegt von Auftrag mpfb_import). Vorher bekam jeder Teil M_<Datei>.mat →
            // Haare und Augen sahen aus wie einfarbige Haut.
            string teil = MpfbTeil(material != null ? material.name : "", renderer != null ? renderer.name : "");
            if (teil != null)
            {
                Material m = AssetDatabase.LoadAssetAtPath<Material>($"{CharacterBodies.MaterialFolder}/M_{file}_{teil}.mat");
                if (m != null) return m;
            }
            return AssetDatabase.LoadAssetAtPath<Material>($"{CharacterBodies.MaterialFolder}/M_{file}.mat");
        }
    }
}
