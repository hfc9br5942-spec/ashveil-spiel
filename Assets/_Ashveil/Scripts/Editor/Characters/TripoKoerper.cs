using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// 21.10.2026 (Max: „Übernehme bitte die Logik, die Mixamo hat“): Spielkörper aus Tripo mit Mixamo-Skelett, 1:1 wie geliefert
    /// (Tools/Blender/ashveil_tripo_koerper.py → SourceArt/Characters/Tripo). Anders als bei MPFB keine Kopie der Quaternius-
    /// Zuordnung: Unity erzeugt die Humanoid-Zuordnung selbst (Mixamo wird vollständig erkannt). Spielcode übersetzt die
    /// Knochennamen über SkelettNamen. Umschalter „Ashveil.TripoFrau“ (Standard aus) – bis Max die Figur abnimmt, bleibt die Frau,
    /// wie sie ist.
    ///   Auftrag tripo_import [Name]   – FBX + Texturen ins Projekt, Material, Humanoid prüfen
    ///   Auftrag tripo_frau an|aus      – Frau auf CHR_W_TR01 schalten + Aussehen-Katalog neu
    /// </summary>
    [InitializeOnLoad]
    public static class TripoKoerper
    {
        public const string FemaleName = "CHR_W_TR01";
        public const string FemalePath = CharacterBodies.Folder + "/" + FemaleName + ".fbx";
        public const string PrefKey = "Ashveil.TripoFrau";
        public static bool UseFemale => EditorPrefs.GetBool(PrefKey, false) && AssetDatabase.LoadAssetAtPath<GameObject>(FemalePath) != null;
        /// <summary>21.10.: Mann aus Tripo (texturiertes Modell + Tripo-Rig, Tools/Blender/ashveil_tripo_umriggen.py), gleicher Standard.</summary>
        public const string MaleName = "CHR_M_TR01";
        public const string MalePath = CharacterBodies.Folder + "/" + MaleName + ".fbx";
        public const string PrefKeyMale = "Ashveil.TripoMann";
        public static bool UseMale => EditorPrefs.GetBool(PrefKeyMale, false) && AssetDatabase.LoadAssetAtPath<GameObject>(MalePath) != null;
        public static string HautPfad(string name) => CharacterBodies.Folder + "/Textures/T_" + name + "_TR_Basis.png";

        static TripoKoerper()
        {
            Auftragsordner.Registrieren("tripo_import", (a, fertig) =>
            {
                string name = string.IsNullOrWhiteSpace(a.parameter) ? FemaleName : a.parameter.Trim();
                bool ok = Import(name, out string meldung);
                fertig(ok, meldung);
            }, false, 600);
            Auftragsordner.Registrieren("tripo_frau", (a, fertig) =>
            {
                bool an = (a.parameter ?? "").Trim().ToLowerInvariant() != "aus";
                if (an && AssetDatabase.LoadAssetAtPath<GameObject>(FemalePath) == null) { fertig(false, FemaleName + " fehlt – erst Auftrag tripo_import"); return; }
                EditorPrefs.SetBool(PrefKey, an);
                AppearanceContentBuilder.Build();
                fertig(true, (an ? "Tripo-Frau (" + FemaleName + ") eingeschaltet" : "Tripo-Frau aus") + " · Aussehen-Katalog neu");
            }, false, 300);
            // 21.10.: Spieler-Prefab (Sandbox_M1, Kareth_v4 …) bekommt die aktuellen Körper – nur Model_Male/Model_Female getauscht
            Auftragsordner.Registrieren("spieler_koerper", (a, fertig) =>
            {
                string pfad = SandboxSceneBuilder.PlayerPrefabPath;
                GameObject inhalt = PrefabUtility.LoadPrefabContents(pfad);
                try
                {
                    string m = PlayerRigBuilder.KoerperErneuern(inhalt);
                    if (m == null) { fertig(false, "kein CharacterBodySwitcher in " + pfad); return; }
                    PrefabUtility.SaveAsPrefabAsset(inhalt, pfad);
                    fertig(true, "Spieler-Prefab: " + m);
                }
                finally { PrefabUtility.UnloadPrefabContents(inhalt); }
            }, false, 300);
            // 21.10.: Diagnose schwebende Anbauteile – Ruhedrehung aus Bindposen (wie AppearanceRig) gegen tatsächliche Ruhedrehung
            Auftragsordner.Registrieren("koerper_bindung", (a, fertig) =>
            {
                string pfad = string.IsNullOrWhiteSpace(a.parameter) ? MalePath : CharacterBodies.Folder + "/" + a.parameter.Trim() + ".fbx";
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(pfad);
                if (src == null) { fertig(false, "fehlt: " + pfad); return; }
                GameObject inst = Object.Instantiate(src); inst.hideFlags = HideFlags.HideAndDontSave;
                try
                {
                    var smr = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(s => s.sharedMesh.vertexCount).First();
                    Matrix4x4[] bind = smr.sharedMesh.bindposes;
                    Quaternion toChar = Quaternion.Inverse(inst.transform.rotation) * smr.transform.rotation;
                    var sb = new System.Text.StringBuilder();
                    float maxW = 0f; string maxN = "";
                    for (int i = 0; i < smr.bones.Length && i < bind.Length; i++)
                    {
                        Transform b = smr.bones[i]; if (b == null) continue;
                        Quaternion ausBind = toChar * bind[i].inverse.rotation;
                        Quaternion echt = Quaternion.Inverse(inst.transform.rotation) * b.rotation;
                        float w = Quaternion.Angle(ausBind, echt);
                        Vector3 posBind = (Matrix4x4.TRS(Vector3.zero, toChar, Vector3.one) * bind[i].inverse).GetColumn(3);
                        float d = Vector3.Distance(smr.transform.localToWorldMatrix.MultiplyPoint3x4(bind[i].inverse.GetColumn(3)), b.position);
                        if (w > maxW) { maxW = w; maxN = b.name; }
                        if (b.name.Contains("Neck") || b.name.Contains("Head") || b.name.Contains("Leg") || b.name.Contains("Hips") || b.name.Contains("Spine") || b.name.Contains("Chest"))
                            sb.Append($"{b.name}: Drehung {w:0.0}°, Lage {d * 100f:0.0} cm, det {bind[i].determinant:0.000}, Skal {b.lossyScale.x:0.000}; ");
                    }
                    fertig(true, $"{System.IO.Path.GetFileName(pfad)}: größte Abweichung {maxW:0.0}° ({maxN}) · {sb}");
                }
                finally { Object.DestroyImmediate(inst); }
            }, false, 120);
            // 21.10.: Trageorte messen – wo landet jede Scheide (SheathPose) im Figurenraum (x rechts, y oben, z vorn)?
            Auftragsordner.Registrieren("scheiden_diagnose", (a, fertig) =>
            {
                var sb = new System.Text.StringBuilder();
                foreach (string pfad in new[] { QuaterniusImport.BaseMalePath, CharacterBodies.MpfbMalePath, MalePath, FemalePath })
                {
                    var src = AssetDatabase.LoadAssetAtPath<GameObject>(pfad);
                    if (src == null) continue;
                    var root = new GameObject("Diagnose") { hideFlags = HideFlags.HideAndDontSave };
                    try
                    {
                        GameObject model = Object.Instantiate(src, root.transform);
                        var rig = model.AddComponent<Ashveil.Gameplay.Visuals.AppearanceRig>();
                        rig.Init(root.transform);
                        sb.Append(System.IO.Path.GetFileNameWithoutExtension(pfad)).Append(": ");
                        foreach (Ashveil.Data.WeaponSheath s in System.Enum.GetValues(typeof(Ashveil.Data.WeaponSheath)))
                        {
                            if (s == Ashveil.Data.WeaponSheath.None) continue;
                            if (!rig.SheathPose(s, out Transform bone, out Vector3 lp, out Quaternion lr, out float ls)) { sb.Append($"{s}=kein Knochen; "); continue; }
                            Vector3 p = root.transform.InverseTransformPoint(bone.TransformPoint(lp));
                            Vector3 klinge = root.transform.InverseTransformDirection(bone.rotation * lr * Vector3.forward);
                            sb.Append($"{s}@{bone.name}: Lage x{p.x:0.00} y{p.y:0.00} z{p.z:0.00}, Klinge ({klinge.x:0.0} {klinge.y:0.0} {klinge.z:0.0}); ");
                        }
                        sb.Append(" | ");
                    }
                    finally { Object.DestroyImmediate(root); }
                }
                fertig(true, sb.ToString());
            }, false, 120);
            Auftragsordner.Registrieren("tripo_mann", (a, fertig) =>
            {
                bool an = (a.parameter ?? "").Trim().ToLowerInvariant() != "aus";
                if (an && AssetDatabase.LoadAssetAtPath<GameObject>(MalePath) == null) { fertig(false, MaleName + " fehlt – erst Auftrag tripo_import|" + MaleName); return; }
                EditorPrefs.SetBool(PrefKeyMale, an);
                AppearanceContentBuilder.Build();
                fertig(true, (an ? "Tripo-Mann (" + MaleName + ") eingeschaltet" : "Tripo-Mann aus") + " · Aussehen-Katalog neu");
            }, false, 300);
        }

        public static bool Import(string name, out string meldung)
        {
            string root = Ashveil.Core.ProjectPaths.ProjectRoot;
            string quelle = Path.Combine(root, "SourceArt/Characters/Tripo", name + ".fbx");
            if (!File.Exists(quelle)) { meldung = "fehlt: " + quelle; return false; }
            string ziel = CharacterBodies.Folder + "/" + name + ".fbx";
            string texOrdner = CharacterBodies.Folder + "/Textures";
            string info = "nicht ausgeführt";
            bool ok = false;
            AnimationsImportSchutz.Ausfuehren(() =>
            {
                Directory.CreateDirectory(Path.Combine(root, texOrdner));
                Directory.CreateDirectory(Path.Combine(root, CharacterBodies.MaterialFolder));
                // Texturen zuerst, Material vor dem Modell (CharacterImportRules weist M_<Datei>.mat beim Import zu)
                Texture2D Tex(string teil, bool normal)
                {
                    string q = Path.Combine(root, "SourceArt/Characters/Tripo/Textures", $"T_{name}_TR_{teil}.png");
                    string z = $"{texOrdner}/T_{name}_TR_{teil}.png";
                    if (!File.Exists(q)) return null;
                    File.Copy(q, Path.Combine(root, z), true);
                    AssetDatabase.ImportAsset(z, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    if (AssetImporter.GetAtPath(z) is TextureImporter ti)
                    {
                        ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                        ti.maxTextureSize = normal ? 2048 : 4096;
                        ti.textureCompression = TextureImporterCompression.CompressedHQ;
                        ti.SaveAndReimport();
                    }
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(z);
                }
                Texture2D basis = Tex("Basis", false), normalTex = Tex("Normal", true);
                string mp = $"{CharacterBodies.MaterialFolder}/M_{name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, mp); }
                m.SetTexture("_BaseMap", basis); m.SetColor("_BaseColor", Color.white); m.SetFloat("_Smoothness", 0.3f);
                if (normalTex != null) { m.SetTexture("_BumpMap", normalTex); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssets();

                // Vor dem Überschreiben auf Generic (wie mpfb_import) – sonst zuerst Import mit alter Zuordnung
                if (AssetImporter.GetAtPath(ziel) is ModelImporter alt && alt.animationType == ModelImporterAnimationType.Human)
                {
                    alt.animationType = ModelImporterAnimationType.Generic;
                    alt.SaveAndReimport();
                }
                AssetDatabase.StartAssetEditing();
                try { File.Copy(quelle, Path.Combine(root, ziel), true); }
                finally { AssetDatabase.StopAssetEditing(); }
                AssetDatabase.ImportAsset(ziel, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                var imp = AssetImporter.GetAtPath(ziel) as ModelImporter;
                if (imp == null) { info = "kein ModelImporter"; return; }
                imp.isReadable = true;
                imp.importAnimation = false;
                imp.animationType = ModelImporterAnimationType.Human;
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.humanDescription = new HumanDescription();   // Unity ordnet das Mixamo-Skelett selbst zu
                imp.SaveAndReimport();
                ok = Pruefen(ziel, out info);
            }, true);
            meldung = name + ": " + info;
            return ok;
        }

        /// <summary>Humanoid gültig, alle Pflichtrollen und Finger zugeordnet, Mixamo-Namen unverändert, Größe.</summary>
        public static bool Pruefen(string pfad, out string info)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(pfad);
            if (model == null) { info = "Modell nicht geladen"; return false; }
            GameObject inst = Object.Instantiate(model);
            inst.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var anim = inst.GetComponent<Animator>();
                bool human = anim != null && anim.avatar != null && anim.avatar.isValid && anim.avatar.isHuman;
                if (!human) { info = "Humanoid-Avatar ungültig"; return false; }
                int rollen = 0, belegt = 0;
                foreach (HumanBodyBones hb in System.Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if (hb == HumanBodyBones.LastBone || hb == HumanBodyBones.Jaw || hb == HumanBodyBones.LeftEye || hb == HumanBodyBones.RightEye || hb == HumanBodyBones.UpperChest) continue;
                    rollen++;
                    if (anim.GetBoneTransform(hb) != null) belegt++;
                }
                Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                var smr = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(s => s.sharedMesh != null ? s.sharedMesh.vertexCount : 0).FirstOrDefault();
                // am verformten Netz messen (smr.bounds ist nur die grobe Hülle)
                float hoehe = 0f;
                if (smr != null)
                {
                    var gebacken = new Mesh();
                    smr.BakeMesh(gebacken);   // ohne Skalierung – TransformPoint bringt sie
                    float lo = float.MaxValue, hi = float.MinValue;
                    foreach (Vector3 v in gebacken.vertices) { float y = smr.transform.TransformPoint(v).y; lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); }
                    hoehe = hi - lo;
                    Object.DestroyImmediate(gebacken);
                }
                info = $"Humanoid gültig, {belegt}/{rollen} Rollen, Hüfte '{(hips != null ? hips.name : "-")}', Höhe {hoehe:0.00} m, " +
                       $"{(smr != null ? smr.sharedMesh.triangles.Length / 3 : 0)} Dreiecke, Stufen {inst.GetComponentsInChildren<SkinnedMeshRenderer>(true).Count(s => s.name.Contains("_Stufe"))}";
                // Mixamo- (Frau) oder Unity-Namen (Mann) – beide übersetzt SkelettNamen auf „pelvis“
                return belegt == rollen && hips != null && Ashveil.Gameplay.Visuals.SkelettNamen.Kanonisch(hips.name) == "pelvis";
            }
            finally { Object.DestroyImmediate(inst); }
        }
    }
}
