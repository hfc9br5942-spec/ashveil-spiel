using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// MOV-127 (echte Bewegungsaufnahmen): Auftrag „mocap_probe“ zeigt jede CMU-Aufnahme aus
    /// <c>Animations/Mocap/CMU</c> (Humanoid) auf der heutigen Spielfigur und – zum Vergleich – den bisherigen Quaternius-Gang
    /// „Walk_Loop“. Leere, nicht gespeicherte Szene, Seitenansicht, 6 Bilder je Clip: Logs/Ashveil/Motion/Mocap/&lt;Clip&gt;_k.png.
    /// Prüft außerdem, ob der Avatar der Aufnahme gültig ist. Ändert keine Szene.
    /// </summary>
    [InitializeOnLoad]
    public static class AuftragMocapProbe
    {
        public const string Ordner = ProjectSetup.Root + "/Animations/Mocap/CMU";
        /// <summary>Vorraum fuer Blender-Retargets ausserhalb von Assets/ (Unity importiert dort nichts von selbst).</summary>
        public const string VorraumRetarget = "SourceArt/Referenz/CMU/fbx_retarget";

        static AuftragMocapProbe()
        {
            Auftragsordner.Registrieren("mocap_probe", Probe, false, 600);
            Auftragsordner.Registrieren("mocap_pruefen", Pruefen, false, 600);
            // 17.10. abends (Max: „Eigenen ASHVEIL-Menschenkoerper einbauen“): Umschalter wie Menue „Ashveil › Figuren › Neue
            // Basiskoerper verwenden“ (CHR_M/F_Base01 statt Quaternius) + Aussehen-Katalog. Wirkt auf das Motion-Lab (wird je
            // Aufnahme neu gebaut) und auf kuenftige Szenenaufbauten; bestehende Szenen von Max bleiben unangetastet.
            Auftragsordner.Registrieren("basiskoerper", (a, fertig) =>
            {
                string was = (a.parameter ?? "").Trim().ToLowerInvariant();
                // 17.10. abends: „mpfb“ = MPFB2-Mann (CHR_M_MPFB01) fuer den Mann, Frau bleibt wie eingestellt.
                if (was == "mpfb")
                {
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(CharacterBodies.MpfbMalePath) == null) { fertig(false, "CHR_M_MPFB01 fehlt – erst Auftrag mpfb_import"); return; }
                    EditorPrefs.SetBool(CharacterBodies.MpfbPrefKey, true);
                    AppearanceContentBuilder.Build();
                    fertig(true, "MPFB-Mann (CHR_M_MPFB01) eingeschaltet · Aussehen-Katalog neu · Lab baut beim naechsten Auftrag damit");
                    return;
                }
                EditorPrefs.SetBool(CharacterBodies.MpfbPrefKey, false);
                bool neu = was == "neu" || was == "ashveil";
                if (neu && !CharacterBodies.NewBodiesAvailable) { fertig(false, "CHR_M_Base01/CHR_F_Base01 fehlen in Art/Characters/Bodies"); return; }
                EditorPrefs.SetBool("Ashveil.NeueBasiskoerper", neu);
                AppearanceContentBuilder.Build();
                fertig(true, (neu ? "ASHVEIL-Basiskoerper (CHR_*_Base01)" : "Quaternius-Koerper") + " eingeschaltet · Aussehen-Katalog neu · Lab baut beim naechsten Auftrag damit");
            }, false, 300);
            // 17.10. abends: MPFB2-Koerper aus dem Vorraum SourceArt/Characters/MPFB nach Art/Characters/Bodies holen – geschuetzt wie
            // mocap_import (leere Szene, angehaltene Asset-Datenbank), dann lesbar, Hautmaterial, Humanoid mit Quaternius-Zuordnung.
            Auftragsordner.Registrieren("mpfb_import", (a, fertig) =>
            {
                // Parameter: [Name][|auto]  – „auto“ = Unity erzeugt die Humanoid-Zuordnung selbst (statt Quaternius-Kopie)
                string[] teile = (a.parameter ?? "").Split('|');
                string name = string.IsNullOrWhiteSpace(teile[0]) ? "CHR_M_MPFB01" : teile[0].Trim();
                // 18.10.: „auto“ ist jetzt Standard – die Quaternius-Zuordnung baute das Skelett ohne Eltern und mit falschen Ruhedrehungen
                // (neue Clips 110 m tief, Übersetzungs-Warnungen an allen Knochen). „quaternius“ erzwingt den alten Weg.
                bool auto = !teile.Any(t => t.Trim().ToLowerInvariant() == "quaternius");
                string quelle = Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, "SourceArt/Characters/MPFB", name + ".fbx");
                if (!File.Exists(quelle)) { fertig(false, "fehlt: " + quelle); return; }
                string ziel = CharacterBodies.Folder + "/" + name + ".fbx";
                string info = "nicht ausgefuehrt";
                AnimationsImportSchutz.Ausfuehren(() =>
                {
                    // 19.10.: Vor dem Überschreiben auf Generic stellen – sonst importierte Unity die neue Form zuerst mit der alten
                    // Humanoid-Zuordnung („Rig Error: Avatar Rig Configuration mis-match“, Oberschenkel/Ballen 3–13 mm daneben);
                    // die Zuordnung wird weiter unten ohnehin neu erzeugt.
                    if (auto && AssetImporter.GetAtPath(ziel) is ModelImporter alt && alt.animationType == ModelImporterAnimationType.Human)
                    {
                        alt.animationType = ModelImporterAnimationType.Generic;
                        alt.SaveAndReimport();
                    }
                    AssetDatabase.StartAssetEditing();
                    try { File.Copy(quelle, Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, ziel), true); }
                    finally { AssetDatabase.StopAssetEditing(); }
                    AssetDatabase.ImportAsset(ziel, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                    var imp = AssetImporter.GetAtPath(ziel) as ModelImporter;
                    if (imp == null) { info = "kein ModelImporter"; return; }
                    imp.isReadable = true;
                    imp.importAnimation = false;
                    Directory.CreateDirectory(CharacterBodies.MaterialFolder);
                    // 17.10. spätabends: Haut, Augen, Brauen, Wimpern, Haare (MakeHuman-System-Assets CC0) – Texturen ins Projekt,
                    // je Teil ein Material; Zuordnung über den Materialnamen aus Blender (CHR_M_MPFB01.body / .low-poly / …).
                    string mh = Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, "SourceArt/Referenz/MakeHuman/system_assets");
                    string texOrdner = CharacterBodies.Folder + "/Textures";
                    Directory.CreateDirectory(Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, texOrdner));
                    string sauber = Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, "SourceArt/Characters/MPFB/Textures") + "/";
                    string gs = name.Contains("_W_") ? "W" : "M";
                    var stuecke = new (string schluessel, string quelle, string ziel, bool ausschnitt, bool beidseitig, float glanz)[]
                    {
                        // 18.10.: gesäuberte Haut (ohne Stoppeln/Körperhaar/dunkle Achseln, Tools/Blender/haut_saeubern.py) – stilisierter Look der Referenz
                        ("haut", sauber + "T_Haut_" + gs + "_Hell_Sauber.png", "T_" + name + "_Haut.png", false, false, 0.35f),
                        ("haut_dunkel", sauber + "T_Haut_" + gs + "_Dunkel_Sauber.png", "T_" + name + "_Haut_Dunkel.png", false, false, 0.35f),
                        ("augen", "eyes/materials/brown_eye.png", "T_" + name + "_Augen.png", false, false, 0.85f),
                        ("brauen", "eyebrows/eyebrow008/eyebrow008_neutral.png", "T_" + name + "_Brauen_008.png", true, true, 0.1f),   // 18.10.: vollere Braue, neutral grau (Farbe = Haarfarbe)
                        ("wimpern", "eyelashes/eyelashes01/eyelashes01.png", "T_" + name + "_Wimpern.png", true, true, 0.1f),
                        ("haare", gs == "W" ? sauber + "T_Haare_Pixie_Neutral.png" : "hair/short02/short02_neutral.png", "T_" + name + "_Haare_Neutral.png", true, true, 0.3f),   // Frau: eigene Pixie-Frisur (ashveil_frisur_pixie.py, Vorlage Mensch_W_Balanced)   // 18.10.: neutral grau (Ø 0,56) wie alle Frisuren – Farbe kommt aus dem Katalog
                    };
                    // 18.10. nachts (ART-122, Max: „du weist doch langsam wo ich hinwill“): Figur nach Vorlage – Haut und Haar-Schale aus
                    // Tools/Blender/ashveil_ki3d_figur.py (Farben direkt aus Max' Vorlagenbildern). Gibt es diese Texturen, ersetzen sie Haut
                    // und Haare; die Haare sind dann eine geschlossene Schale (kein Ausschnitt, einseitig) mit eigener Farbe.
                    string vorlageHaut = sauber + "T_" + name + "_Vorlage_Haut.png", vorlageHaare = sauber + "T_" + name + "_Vorlage_Haare.png";
                    bool vorlage = File.Exists(vorlageHaut);
                    if (vorlage)
                        for (int i = 0; i < stuecke.Length; i++)
                        {
                            if (stuecke[i].schluessel == "haut") stuecke[i] = ("haut", vorlageHaut, "T_" + name + "_Vorlage_Haut.png", false, false, 0.3f);
                            if (stuecke[i].schluessel == "haare" && File.Exists(vorlageHaare)) stuecke[i] = ("haare", vorlageHaare, "T_" + name + "_Vorlage_Haare.png", false, false, 0.08f);   // 18.10. nachts: matt – Glanz ist schon in der Vorlage gemalt (sonst Plastik-Helm)
                        }
                    // 19.10. (ART-122): Haarkarten – Strähnen-Streifen mit Alpha (Tools/Blender/ashveil_ki3d_figur.py, Atlas aus haarkarten.py)
                    // 19.10. abends (DEC-20261019-002): Form aus Higgsfield/Meshy auf eigenem Netz (ashveil_wrap_meshy.py) – die gebackene
                    // Farbe ersetzt die Haut, die gebackene Normal-Map weiter unten die Muskelkarte. Vorrang vor „Vorlage“.
                    string hfBasis = sauber + "T_" + name + "_HF_Basis.png";
                    bool hf = File.Exists(hfBasis);
                    if (hf)
                        for (int i = 0; i < stuecke.Length; i++)
                        {
                            if (stuecke[i].schluessel == "haut") stuecke[i] = ("haut", hfBasis, "T_" + name + "_HF_Basis.png", false, false, 0.3f);
                            // 20.10.: Hautton „Dunkel“ aus der HF-Haut (ashveil_kopf_anpassen.py --dunkel_variante 1), sonst MakeHuman-Haut
                            string hfDunkel = sauber + "T_" + name + "_HF_Basis_Dunkel.png";
                            if (stuecke[i].schluessel == "haut_dunkel" && File.Exists(hfDunkel)) stuecke[i] = ("haut_dunkel", hfDunkel, "T_" + name + "_Haut_Dunkel.png", false, false, 0.3f);
                        }
                    // 19.10. abends: Pferdeschwanz/Strähnen aus der Meshy-Form (ashveil_zopf_meshy.py) – eigenes Teil „<Name>_Zopf“, Farbe gebacken
                    string hfZopf = sauber + "T_" + name + "_HF_Zopf.png";
                    if (hf && File.Exists(hfZopf))
                        stuecke = stuecke.Append(("zopf", hfZopf, "T_" + name + "_HF_Zopf.png", false, true, 0.25f)).ToArray();
                    string vorlageKarten = sauber + "T_" + name + "_Vorlage_Haarkarten.png";
                    if (vorlage && File.Exists(vorlageKarten))
                        stuecke = stuecke.Append(("haarkarten", vorlageKarten, "T_" + name + "_Vorlage_Haarkarten.png", true, true, 0.28f)).ToArray();
                    var mats = new Dictionary<string, Material>();
                    foreach (var t in stuecke)
                    {
                        string q = Path.Combine(mh, t.quelle), z = texOrdner + "/" + t.ziel;
                        string zAbs = Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, z);
                        // 18.10.: auch ersetzen, wenn die Quelle neuer ist (gesäuberte Haut ersetzt die alte Kopie; Katalog behält die GUID)
                        if (File.Exists(q) && (!File.Exists(zAbs) || File.GetLastWriteTimeUtc(q) > File.GetLastWriteTimeUtc(zAbs)))
                        {
                            File.Copy(q, zAbs, true);
                            AssetDatabase.ImportAsset(z, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                            if (AssetImporter.GetAtPath(z) is TextureImporter ti)
                            {
                                ti.maxTextureSize = t.ziel.Contains("_Vorlage_Haut") || t.ziel.Contains("_HF_Basis") || (hf && t.schluessel == "haut_dunkel") ? 4096 : 2048; ti.textureCompression = TextureImporterCompression.CompressedHQ;   // 19.10. spät: HF-Haut in 4K (Gesicht hat in der MPFB-UV wenig Fläche)
                                ti.alphaIsTransparency = t.ausschnitt;
                                if (t.schluessel == "haarkarten") { ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.38f; ti.maxTextureSize = 4096; }   // Strähnen werden in der Ferne nicht dünner
                                ti.SaveAndReimport();
                            }
                        }
                        string mp = CharacterBodies.MaterialFolder + "/M_" + name + "_" + t.schluessel + ".mat";
                        var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
                        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, mp); }
                        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(z);
                        m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white);
                        m.SetFloat("_Smoothness", t.glanz);
                        if (!t.ausschnitt) { m.SetFloat("_AlphaClip", 0f); m.DisableKeyword("_ALPHATEST_ON"); }
                        if (!t.beidseitig) m.SetFloat("_Cull", 2f);
                        if (t.ausschnitt) { m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", t.schluessel == "brauen" ? 0.3f : 0.38f); m.EnableKeyword("_ALPHATEST_ON"); }
                        if (t.beidseitig) m.SetFloat("_Cull", 0f);
                        if (t.schluessel == "haut" || t.schluessel == "haut_dunkel")   // 18.10.: Muskel-Normalmap (selbst gebacken, Tools/Blender/ashveil_muskel_normal.py)
                        {
                            string nq = Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, "SourceArt/Characters/MPFB/Textures/T_" + name + (hf ? "_HF_Normal.png" : "_Muskel_Normal.png"));
                            string nz = texOrdner + "/T_" + name + "_Haut_Normal.png", nzAbs = Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, nz);
                            if (File.Exists(nq) && (!File.Exists(nzAbs) || File.GetLastWriteTimeUtc(nq) > File.GetLastWriteTimeUtc(nzAbs)))
                            {
                                File.Copy(nq, nzAbs, true);
                                AssetDatabase.ImportAsset(nz, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                                if (AssetImporter.GetAtPath(nz) is TextureImporter ni) { ni.textureType = TextureImporterType.NormalMap; ni.maxTextureSize = 2048; ni.textureCompression = TextureImporterCompression.CompressedHQ; ni.SaveAndReimport(); }
                            }
                            var ntex = AssetDatabase.LoadAssetAtPath<Texture2D>(nz);
                            if (ntex != null) { m.SetTexture("_BumpMap", ntex); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
                        }
                        if (t.schluessel == "haare" && gs == "M" && !t.ziel.Contains("_Vorlage_"))   // 18.10.: Haar-Normalmap (MakeHuman short02, CC0) – Strähnen statt glatter Fläche
                        {
                            string nq = Path.Combine(mh, "hair/short02/short02_normal.png"), nz = texOrdner + "/T_" + name + "_Haare_Normal.png";
                            if (File.Exists(nq) && !File.Exists(Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, nz)))
                            {
                                File.Copy(nq, Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, nz));
                                AssetDatabase.ImportAsset(nz, ImportAssetOptions.ForceSynchronousImport);
                                if (AssetImporter.GetAtPath(nz) is TextureImporter ni) { ni.textureType = TextureImporterType.NormalMap; ni.maxTextureSize = 1024; ni.SaveAndReimport(); }
                            }
                            var ntex = AssetDatabase.LoadAssetAtPath<Texture2D>(nz);
                            if (ntex != null) { m.SetTexture("_BumpMap", ntex); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
                        }
                        EditorUtility.SetDirty(m);
                        mats[t.schluessel] = m;
                    }
                    AssetDatabase.SaveAssets();
                    Material Wahl(string matName)
                    {
                        string n = matName.ToLowerInvariant();
                        if (n.Contains("low-poly") || n.Contains("high-poly") || n.Contains("eye") && !n.Contains("eyebrow") && !n.Contains("eyelash")) return mats["augen"];
                        if (n.Contains("haarkarten") && mats.ContainsKey("haarkarten")) return mats["haarkarten"];
                        if (n.Contains("zopf") && mats.ContainsKey("zopf")) return mats["zopf"];
                        if (n.Contains("eyebrow")) return mats["brauen"];
                        if (n.Contains("eyelash")) return mats["wimpern"];
                        if (n.Contains("short") || n.Contains("hair") || n.Contains("bob") || n.Contains("long") || n.Contains("ponytail")) return mats["haare"];
                        return mats["haut"];
                    }
                    // alte Zuordnungen (erster Import: alles auf ein Material) entfernen, damit die echten Blender-Namen sichtbar sind
                    foreach (var kv in imp.GetExternalObjectMap().ToList())
                        if (kv.Key.type == typeof(Material)) imp.RemoveRemap(kv.Key);
                    imp.SaveAndReimport();
                    imp = AssetImporter.GetAtPath(ziel) as ModelImporter;
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(ziel);
                    int remap = 0;
                    var gesehen = new HashSet<string>();
                    // 17.10. nachts: Zuordnung macht jetzt CharacterImportRules.OnAssignMaterialModel (je Teil M_<Name>_<teil>);
                    // die frühere Umbenennung hier sah nur noch ein Material (die Regel hatte alles auf M_<Name> gesetzt).
                    var teileLog = new List<string>();
                    foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                        foreach (var m in r.sharedMaterials)
                        {
                            if (m != null && gesehen.Add(m.name)) remap++;
                            teileLog.Add(r.name + "→" + (m != null ? m.name : "null"));
                        }
                    Debug.Log("[Ashveil] mpfb_import Teile/Materialien: " + string.Join(", ", teileLog));
                    if (auto)
                    {
                        imp = AssetImporter.GetAtPath(ziel) as ModelImporter;
                        imp.animationType = ModelImporterAnimationType.Generic;   // alte Zuordnung verwerfen …
                        imp.SaveAndReimport();
                        imp.animationType = ModelImporterAnimationType.Human;     // … und neu automatisch erzeugen lassen
                        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                        imp.humanDescription = new HumanDescription();
                        imp.SaveAndReimport();
                        // 18.10. abends (Lab Mann + Frau: Knie im Stand nie unter 11,5°, in Blender 2–5°): Unity klemmt die Kniestreckung
                        // an der Muskelgrenze des Avatars. Grenze „Unterschenkel strecken“ beidseitig um 15° erweitert (Clips nutzen
                        // denselben Avatar, Umrechnung bleibt gleich) – das gestreckte Bein der Aufnahme kommt jetzt an.
                        KnieGrenzeErweitern(ziel, 15f);
                    }
                    else CharacterBodies.ConfigureAvatar(ziel, QuaterniusImport.BaseMalePath);
                    // Diagnose: Arm-/Bein-Zuordnung beider Figuren und Ruhedrehungen
                    string Zuordnung(string pfad)
                    {
                        var mi = AssetImporter.GetAtPath(pfad) as ModelImporter;
                        if (mi == null) return "-";
                        var hb = mi.humanDescription.human;
                        string[] wichtig = { "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand", "LeftUpperLeg", "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head" };
                        return string.Join(", ", wichtig.Select(w => w + "=" + (hb.FirstOrDefault(h => h.humanName == w).boneName ?? "–")));
                    }
                    Debug.Log("[Ashveil] mpfb_import Zuordnung MPFB: " + Zuordnung(ziel));
                    Debug.Log("[Ashveil] mpfb_import Zuordnung Quaternius: " + Zuordnung(QuaterniusImport.BaseMalePath));
                    var av = AssetDatabase.LoadAllAssetsAtPath(ziel).OfType<Avatar>().FirstOrDefault();
                    model = AssetDatabase.LoadAssetAtPath<GameObject>(ziel);
                    // 19.10.: Körper = Renderer mit dem Namen der Figur (vorher der erste – oft Augen/Haare, „Höhe 0,38“); Höhe in
                    // Weltmetern an einer kurz erzeugten Kopie (Mesh-Grenzen liegen im Knochenraum)
                    var smr = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == name)
                              ?? model.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    float hoehe = 0f;
                    if (smr != null)
                    {
                        var kopie = (GameObject)UnityEngine.Object.Instantiate(model);
                        try
                        {
                            var kr = kopie.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == smr.name);
                            if (kr != null)
                            {
                                var b = new Mesh(); kr.BakeMesh(b, false);   // ohne Skalierung: Punkte = Drehung des Renderers, Weltmass
                                float lo = float.MaxValue, hi = float.MinValue;
                                foreach (Vector3 v in b.vertices) { float y = (kr.transform.rotation * v).y; lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); }
                                hoehe = hi - lo;
                                UnityEngine.Object.DestroyImmediate(b);
                            }
                        }
                        finally { UnityEngine.Object.DestroyImmediate(kopie); }
                    }
                    info = (av != null && av.isValid && av.isHuman ? "Avatar OK (Humanoid)" : "Avatar FEHLER " + (av == null ? "fehlt" : $"valid={av.isValid} human={av.isHuman}"))
                           + $" · Materialien {remap} · Knochen {(smr != null ? smr.bones.Length : 0)} · Koerper {(smr != null ? smr.name : "-")} Hoehe {hoehe:F2} m";
                }, true, szeneZurueck: false);
                fertig(info.StartsWith("Avatar OK"), name + (auto ? " (Auto-Zuordnung)" : " (Quaternius-Zuordnung)") + ": " + info);
            }, false, 600);
            // 17.10. spätabends (Bewegung v3): Bewegungen, die Blender DIREKT auf das MPFB-Skelett übertragen hat
            // (Tools/Blender/ashveil_mpfb_retarget.py → SourceArt/Characters/MPFB/Anim/ANM_M_*.fbx), geschützt nach
            // Animations/Mocap/MPFB holen: Humanoid mit dem Avatar des Körpers CHR_M_MPFB01 (gleiches Skelett → keine
            // Umrechnungsfehler), Schleife, auf der Stelle. Neue Dateinamen statt Überschreiben (Absturz bei Reimport genutzter Clips).
            // 18.10.2026 abends (Max: „alles was du bisher gemacht hast löschen“, Klick „Alles archivieren“): die bisherigen
            // Bewegungs-Übertragungen (CMU, MPFB Mann/Frau, Testläufe) aus dem Spiel nehmen und nach Archiv/ verschieben –
            // wiederherstellbar, nichts wird gelöscht (Regel 02.10.). Feste Liste, keine freien Pfade. Vorher Spieler-Animator
            // neu (UAL pur), damit nichts mehr auf die Clips zeigt.
            Auftragsordner.Registrieren("bewegung_archivieren", (a, fertig) =>
            {
                PlayerAnimatorBuilder.Build();
                string root = Ashveil.Core.ProjectPaths.ProjectRoot;
                string ziel = Path.Combine(root, "Archiv", "2026-10-18_Bewegung_v1");
                string[] liste = { "Assets/_Ashveil/Animations/Mocap/MPFB", "Assets/_Ashveil/Animations/Mocap/CMU",
                                   "SourceArt/Characters/MPFB/Anim", "SourceArt/Characters/MPFB/AnimTest", "SourceArt/Referenz/CMU/fbx_retarget" };
                var log = new List<string>();
                foreach (string p in liste)
                {
                    string src = Path.Combine(root, p), dst = Path.Combine(ziel, p);
                    if (!Directory.Exists(src)) { log.Add(p + " fehlt"); continue; }
                    if (Directory.Exists(dst)) { log.Add(p + " schon im Archiv"); continue; }
                    int n = Directory.GetFiles(src, "*", SearchOption.AllDirectories).Length;
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    Directory.Move(src, dst);
                    if (File.Exists(src + ".meta")) File.Move(src + ".meta", dst + ".meta");
                    log.Add(p + " → Archiv (" + n + " Dateien)");
                }
                AssetDatabase.Refresh();
                fertig(true, string.Join(" · ", log));
            }, false, 600);
            Auftragsordner.Registrieren("mpfb_anim_import", (a, fertig) =>
            {
                string quelle = Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, "SourceArt/Characters/MPFB/Anim");
                string ziel = PlayerAnimatorBuilder.MpfbOrdner;
                var avatar = AssetDatabase.LoadAllAssetsAtPath(CharacterBodies.MpfbMalePath).OfType<Avatar>().FirstOrDefault();
                if (avatar == null || !avatar.isHuman) { fertig(false, "Avatar von CHR_M_MPFB01 fehlt/ungültig – erst mpfb_import"); return; }
                if (!Directory.Exists(quelle)) { fertig(false, "fehlt: " + quelle); return; }
                var berichte = new List<string>();
                AnimationsImportSchutz.Ausfuehren(() =>
                {
                    Directory.CreateDirectory(Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, ziel));
                    var neu = new List<string>();
                    AssetDatabase.StartAssetEditing();
                    try
                    {
                        foreach (string q in Directory.GetFiles(quelle, "*.fbx"))
                        {
                            string z = ziel + "/" + Path.GetFileName(q);
                            if (File.Exists(z)) { berichte.Add(Path.GetFileName(q) + " schon da (nicht überschrieben)"); continue; }
                            File.Copy(q, Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, z)); neu.Add(z);
                        }
                    }
                    finally { AssetDatabase.StopAssetEditing(); }
                    foreach (string z in neu)
                    {
                        AssetDatabase.ImportAsset(z, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                        var mi = AssetImporter.GetAtPath(z) as ModelImporter;
                        if (mi == null) continue;
                        // 17.10. (Film: Figur lief rückwärts, X-Beine): Einstellungen wie beim Körper CHR_M_MPFB01 – Unity-eigene
                        // Humanoid-Zuordnung (gleiches Skelett), KEINE eingebackene Achsen-Umrechnung (Körper hat sie auch nicht).
                        mi.animationType = ModelImporterAnimationType.Human;
                        // 18.10. (Clip-Vergleich roh ↔ Humanoid: Knie 17–71° wurden zu 0–38°, Beine steif, Schwung halbiert):
                        // Ursache – Clip und Körper hatten je einen EIGENEN Avatar (Clip: eigene Auto-Zuordnung ohne Achsen-Einbacken,
                        // Körper: mit Einbacken). Jetzt nutzen Clips ab Namensendung _k den Avatar des Körpers CHR_M_MPFB01 selbst
                        // (gleiches Skelett, gleiche Achsen-Einstellung) → keine Umrechnung zwischen zwei verschiedenen Ruhelagen.
                        // 18.10.: Frauen-Clips (ANM_W_*, auf CHR_W_MPFB01 übertragen) nutzen den Avatar der Frau
                        string koerperPfad = Path.GetFileName(z).StartsWith("ANM_W_") ? CharacterBodies.MpfbFemalePath : CharacterBodies.MpfbMalePath;
                        var koerperImp = AssetImporter.GetAtPath(koerperPfad) as ModelImporter;
                        var koerperAvatar = AssetDatabase.LoadAllAssetsAtPath(koerperPfad).OfType<Avatar>().FirstOrDefault();
                        bool kopie = koerperAvatar != null && koerperImp != null;   // alle neuen MPFB-Clips (ab _k)
                        if (kopie)
                        {
                            mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                            mi.sourceAvatar = koerperAvatar;
                            mi.bakeAxisConversion = koerperImp.bakeAxisConversion;
                            mi.globalScale = koerperImp.globalScale; mi.useFileScale = koerperImp.useFileScale;
                        }
                        else
                        {
                            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                            mi.humanDescription = new HumanDescription();
                            mi.bakeAxisConversion = false;
                        }
                        mi.importAnimation = true;
                        var basis = mi.defaultClipAnimations;
                        string datei = Path.GetFileNameWithoutExtension(z);
                        var clips = new List<ModelImporterClipAnimation>();
                        ModelImporterClipAnimation Teil(string n, float von, float bis, bool schleife, bool posenSchluss)
                        {
                            var c = new ModelImporterClipAnimation
                            {
                                name = n, takeName = basis[0].takeName,
                                firstFrame = basis[0].firstFrame + von, lastFrame = Mathf.Min(basis[0].lastFrame, basis[0].firstFrame + bis),
                                loopTime = schleife, loopPose = posenSchluss,
                                lockRootRotation = true, keepOriginalOrientation = false,   // nach Körperrichtung ausrichten
                                // 18.10. (Labor: Füße beim Gehen schon im reinen Animator ~9 cm über dem Boden): Höhe nach den
                                // FÜSSEN ausrichten statt nach der Datei – ab Endung _r.
                                lockRootHeightY = true, keepOriginalPositionY = !datei.EndsWith("_r"), heightFromFeet = datei.EndsWith("_r"),   // _r verworfen (Füße 8 cm im Boden)
                                lockRootPositionXZ = true, keepOriginalPositionXZ = true,
                            };
                            return c;
                        }
                        // Phasen nach Messung der Fußhöhen (17.10., Bilder der 60-fps-Aufnahme ab 0): Laufsprung 127_25 abheben 16 /
                        // aufsetzen 67, Sprung aus dem Stand 16_03 abheben 93 / aufsetzen 124. Im Spiel springt die Figur sofort
                        // (kein Ausholen) – Luft = Abheben−2 … Aufsetzen, Landung = Aufsetzen … Stand.
                        if (datei.Contains("Laufsprung")) { clips.Add(Teil(datei + "_Luft", 14, 67, false, false)); clips.Add(Teil(datei + "_Landung", 66, 89, false, false)); }
                        else if (datei.Contains("Sprung")) { clips.Add(Teil(datei + "_Luft", 91, 124, false, false)); clips.Add(Teil(datei + "_Landung", 123, 165, false, false)); }
                        else if (datei.Contains("Anfahren")) clips.Add(Teil(datei, 0, 99999, false, false));
                        else if (datei.Contains("Stehen")) clips.Add(Teil(datei, 0, 99999, true, true));   // ruhige Schleife, Enden angeglichen
                        else clips.Add(Teil(datei, 0, 99999, true, false));
                        mi.clipAnimations = clips.ToArray();
                        mi.SaveAndReimport();
                        // 18.10. (ab Endung _s): Höhe messen statt schätzen – Clip auf dem Körper abtasten, tiefster Fußpunkt je Bild
                        // (Zehenspitze bzw. Ferse ≈ Knöchel − 7 cm) über der Wurzel; 25-%-Wert = Standphase → als Höhenversatz abziehen.
                        if (datei.EndsWith("_s") || datei.EndsWith("_t") || datei.EndsWith("_u") || datei.EndsWith("_v") || datei.EndsWith("_w") || datei.EndsWith("_x") || datei.EndsWith("_y") || datei.EndsWith("_z") || datei.EndsWith("_zc") || datei.EndsWith("_zd") || datei.EndsWith("_ze") || datei.EndsWith("_n2") || datei.EndsWith("_n3") || datei.EndsWith("_n4") || datei.EndsWith("_n5") || datei.EndsWith("_n6") || datei.EndsWith("_n7") || datei.EndsWith("_n8"))
                        {
                            var cl = mi.clipAnimations;
                            var gK = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CharacterBodies.MpfbMalePath));
                            var an = gK.GetComponent<Animator>() ?? gK.AddComponent<Animator>();
                            an.avatar = AssetDatabase.LoadAllAssetsAtPath(CharacterBodies.MpfbMalePath).OfType<Avatar>().FirstOrDefault();
                            an.applyRootMotion = false;
                            Transform Sp(Transform t) => t != null && t.childCount > 0 ? t.GetChild(0) : t;
                            for (int ci = 0; ci < cl.Length; ci++)
                            {
                                var clip = AssetDatabase.LoadAllAssetsAtPath(z).OfType<AnimationClip>().FirstOrDefault(x => x.name == cl[ci].name);
                                if (clip == null) continue;
                                var tief = new List<float>();
                                for (int i = 0; i < 60; i++)
                                {
                                    clip.SampleAnimation(gK, clip.length * i / 60f);
                                    float y0 = gK.transform.position.y;
                                    float m = float.MaxValue;
                                    foreach (var (fb, tb) in new[] { (HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes), (HumanBodyBones.RightFoot, HumanBodyBones.RightToes) })
                                    {
                                        Transform f = an.GetBoneTransform(fb), t = an.GetBoneTransform(tb);
                                        if (f != null) m = Mathf.Min(m, f.position.y - 0.075f - y0);
                                        if (t != null) m = Mathf.Min(m, Sp(t).position.y - 0.01f - y0);
                                    }
                                    tief.Add(m);
                                }
                                tief.Sort();
                                float p25 = tief[tief.Count / 4];
                                // 18.10. (_n2): Messung lieferte -110 m (Abtastung direkt nach dem ersten Import unbrauchbar) → Versatz 110 m,
                                // Figur im Labor 110 m tief. Unplausible Werte (> 30 cm) durch die im Labor bestätigten Werte ersetzen.
                                if (Mathf.Abs(p25) > 0.3f || float.IsNaN(p25))
                                {
                                    float ersatz = datei.Contains("Gehen") ? 0.034f : datei.Contains("Joggen") ? 0.027f : datei.Contains("Rennen") ? 0.035f : datei.Contains("Stehen") ? 0.004f : 0f;
                                    berichte.Add($"{cl[ci].name}: Messung unplausibel ({p25 * 100f:F1} cm) → Laborwert {ersatz * 100f:F1} cm");
                                    p25 = ersatz;
                                }
                                // _s: Vorzeichen falsch (Füße stiegen um den Versatz). _t: positiver Versatz senkt; dazu die 5 cm, um die das
                                // Labor die Füße im reinen Animator zusätzlich über dem Boden misst (Hautdicke des CharacterControllers).
                                cl[ci].heightOffset = datei.EndsWith("_t") ? p25 + 0.05f : -p25;
                                // _u (18.10., nach Labormessung _t): Gehen lag richtig (8,6 cm), Joggen/Rennen/Stehen 4,5 cm zu tief → je Clip
                                // der im Labor bestätigte Wert (Standphase des tiefsten Fusspunkts auf dem Boden).
                                // _v: gemessener Wert + im Labor bestätigter Zuschlag je Gangart (Gehen +5 cm, sonst +0,5 cm)
                                if (datei.EndsWith("_v")) cl[ci].heightOffset = p25 + (datei.Contains("Gehen") ? 0.05f : 0.005f);
                                if (datei.EndsWith("_w") || datei.EndsWith("_x") || datei.EndsWith("_y") || datei.EndsWith("_z") || datei.EndsWith("_zc") || datei.EndsWith("_zd") || datei.EndsWith("_ze") || datei.EndsWith("_n2") || datei.EndsWith("_n3") || datei.EndsWith("_n4") || datei.EndsWith("_n5") || datei.EndsWith("_n6") || datei.EndsWith("_n7") || datei.EndsWith("_n8")) cl[ci].heightOffset = p25 + (datei.Contains("Gehen") ? 0.0f : 0.005f);   // 18.10.: _v Gehen +5 cm ließ die Füße 4,9 cm einsinken
                                if (datei.EndsWith("_u"))
                                    cl[ci].heightOffset = datei.Contains("Gehen") ? 0.086f : datei.Contains("Joggen") ? 0.050f : datei.Contains("Rennen") ? 0.076f : datei.Contains("Stehen") ? -0.011f : p25;
                                berichte.Add($"{cl[ci].name}: Füße {p25 * 100f:F1} cm über Boden → Versatz {-p25 * 100f:F1} cm");
                            }
                            UnityEngine.Object.DestroyImmediate(gK);
                            mi.clipAnimations = cl;
                            mi.SaveAndReimport();
                        }
                        var alle = AssetDatabase.LoadAllAssetsAtPath(z).OfType<AnimationClip>().Where(x => !x.name.StartsWith("__preview__")).ToList();
                        berichte.Add(Path.GetFileName(z) + ": " + (alle.Count == 0 ? "KEIN Clip" : string.Join(", ", alle.Select(c => $"{c.name} {c.length:F2} s human={c.isHumanMotion}"))));
                    }
                }, true, szeneZurueck: false);
                bool ok = berichte.Count > 0 && !berichte.Any(x => x.Contains("KEIN") || x.Contains("human=False"));
                fertig(ok, string.Join(" · ", berichte));
            }, false, 600);
            // 16.10. spaet: CMU-FBX neu importieren (z. B. nach geaenderten Importregeln), geschuetzt wie beim Blender-Export.
            // 17.10. abends: Blender schreibt die Retarget-FBX NICHT mehr nach Assets/ (6 Abstuerze an einem Tag, immer beim
            // automatischen Import waehrend des Schreibens), sondern in den Vorraum SourceArt/Referenz/CMU/fbx_retarget/.
            // Erst dieser Auftrag kopiert sie – in der leeren Szene, mit angehaltener Asset-Datenbank – nach Assets/ und importiert.
            Auftragsordner.Registrieren("mocap_import", (a, fertig) =>
            {
                string ziel = "Assets/_Ashveil/Animations/Mocap/CMU";
                string vorraum = Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, VorraumRetarget);
                int kopiert = 0;
                AnimationsImportSchutz.Ausfuehren(() =>
                {
                    if (Directory.Exists(vorraum))
                    {
                        AssetDatabase.StartAssetEditing();
                        try
                        {
                            foreach (string q in Directory.GetFiles(vorraum, "*.fbx"))
                            {
                                string z = Path.Combine(ziel, Path.GetFileName(q));
                                if (File.Exists(z) && File.GetLastWriteTimeUtc(z) >= File.GetLastWriteTimeUtc(q)) continue;
                                File.Copy(q, z, true); kopiert++;
                            }
                        }
                        finally { AssetDatabase.StopAssetEditing(); }
                    }
                    string[] fbx = Directory.GetFiles(ziel, "*.fbx");
                    foreach (string f in fbx) AssetDatabase.ImportAsset(f.Replace('\\', '/'), ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                }, true, szeneZurueck: false);   // Szene erst mit neuem Animator wieder laden (Absturz 17.10. abends)
                fertig(true, kopiert + " FBX aus dem Vorraum uebernommen · " + Directory.GetFiles(ziel, "*.fbx").Length + " CMU-FBX geschuetzt neu importiert · leere Szene bleibt offen bis spieler_animator/motionlab");
            }, false, 600);
            // Spieler-Animator neu bauen (GUID bleibt), z. B. nach Umschalten von PlayerAnimatorBuilder.MocapFortbewegung.
            // 18.10. (Max: Fehler in Beinen/Haltung): Wo verändert Unity die Bewegung? Derselbe Clip einmal ROH (Kopie als Generic,
            // exakt die Blender-Knochen) und einmal über Humanoid auf dem Körper CHR_M_MPFB01 abgetastet → zwei Skelett-Mitschriften
            // (Format wie das Labor) für gang_pruefung.py. Parameter: Clipdatei ohne Endung (Ordner Animations/Mocap/MPFB)[:Clipname].
            Auftragsordner.Registrieren("clip_vergleich", (a, fertig) =>
            {
                string[] tl = (a.parameter ?? "ANM_M_Gehen_b").Split(':');
                string datei = tl[0].Trim(), clipName = tl.Length > 1 ? tl[1].Trim() : null;
                string quelle = PlayerAnimatorBuilder.MpfbOrdner + "/" + datei + ".fbx";
                string tmpOrdner = ProjectSetup.Root + "/Temp/ClipRoh";
                string roh = tmpOrdner + "/" + datei + "_roh.fbx";
                string aus = Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, "Logs/Ashveil/Motion/ClipVergleich");
                Directory.CreateDirectory(aus);
                string info = "nicht ausgefuehrt";
                AnimationsImportSchutz.Ausfuehren(() =>
                {
                    Directory.CreateDirectory(Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, tmpOrdner));
                    File.Copy(Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, quelle), Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, roh), true);
                    AssetDatabase.ImportAsset(roh, ImportAssetOptions.ForceSynchronousImport);
                    var mi = AssetImporter.GetAtPath(roh) as ModelImporter;
                    mi.animationType = ModelImporterAnimationType.Generic; mi.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
                    mi.importAnimation = true; mi.bakeAxisConversion = false; mi.materialImportMode = ModelImporterMaterialImportMode.None;
                    mi.SaveAndReimport();
                    AnimationClip cRoh = AssetDatabase.LoadAllAssetsAtPath(roh).OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__preview__"));
                    AnimationClip cHum = AssetDatabase.LoadAllAssetsAtPath(quelle).OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__preview__") && (clipName == null || x.name == clipName));
                    if (cRoh == null || cHum == null) { info = "Clip fehlt (roh " + (cRoh != null) + ", humanoid " + (cHum != null) + ")"; return; }
                    // Knochen: Blender-Namen (roh) bzw. Humanoid-Zuordnung (Körper)
                    var namen = new (string u, string b, HumanBodyBones h)[]
                    {
                        ("Hips", "pelvis", HumanBodyBones.Hips), ("Spine", "spine_01", HumanBodyBones.Spine), ("Chest", "spine_02", HumanBodyBones.Chest),
                        ("UpperChest", "spine_03", HumanBodyBones.UpperChest), ("Neck", "neck_01", HumanBodyBones.Neck), ("Head", "Head", HumanBodyBones.Head),
                        ("LeftUpperLeg", "thigh_l", HumanBodyBones.LeftUpperLeg), ("LeftLowerLeg", "calf_l", HumanBodyBones.LeftLowerLeg), ("LeftFoot", "foot_l", HumanBodyBones.LeftFoot), ("LeftToes", "ball_l", HumanBodyBones.LeftToes),
                        ("RightUpperLeg", "thigh_r", HumanBodyBones.RightUpperLeg), ("RightLowerLeg", "calf_r", HumanBodyBones.RightLowerLeg), ("RightFoot", "foot_r", HumanBodyBones.RightFoot), ("RightToes", "ball_r", HumanBodyBones.RightToes),
                        ("LeftShoulder", "clavicle_l", HumanBodyBones.LeftShoulder), ("LeftUpperArm", "upperarm_l", HumanBodyBones.LeftUpperArm), ("LeftLowerArm", "lowerarm_l", HumanBodyBones.LeftLowerArm), ("LeftHand", "hand_l", HumanBodyBones.LeftHand),
                        ("RightShoulder", "clavicle_r", HumanBodyBones.RightShoulder), ("RightUpperArm", "upperarm_r", HumanBodyBones.RightUpperArm), ("RightLowerArm", "lowerarm_r", HumanBodyBones.RightLowerArm), ("RightHand", "hand_r", HumanBodyBones.RightHand),
                    };
                    string Kopf() => "t;tempo;" + string.Join(";", namen.Select(n => n.u + "_x;" + n.u + "_y;" + n.u + "_z")) + ";LeftToeTip_x;LeftToeTip_y;LeftToeTip_z;RightToeTip_x;RightToeTip_y;RightToeTip_z";
                    string P(Transform root, Transform t)
                    {
                        Vector3 v = t != null ? root.InverseTransformPoint(t.position) : Vector3.zero;
                        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.0000};{1:0.0000};{2:0.0000}", v.x, v.y, v.z);
                    }
                    Transform Finde(Transform r, string n) { if (r.name == n) return r; foreach (Transform k in r) { var x = Finde(k, n); if (x != null) return x; } return null; }
                    Transform Spitze(Transform t) => t != null && t.childCount > 0 ? t.GetChild(0) : null;
                    int n = Mathf.Max(2, Mathf.RoundToInt(cRoh.length * 60f));
                    // ROH
                    var gRoh = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(roh));
                    var sb = new System.Text.StringBuilder(Kopf() + "\n");
                    for (int i = 0; i < n; i++)
                    {
                        float t = cRoh.length * i / n; cRoh.SampleAnimation(gRoh, t);
                        Transform r = gRoh.transform;
                        sb.Append(t.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)).Append(";0;")
                          .Append(string.Join(";", namen.Select(x => P(r, Finde(r, x.b))))).Append(';').Append(P(r, Spitze(Finde(r, "ball_l")))).Append(';').Append(P(r, Spitze(Finde(r, "ball_r")))).Append('\n');
                    }
                    File.WriteAllText(Path.Combine(aus, datei + "_roh_skelett.csv"), sb.ToString());
                    UnityEngine.Object.DestroyImmediate(gRoh);
                    // HUMANOID auf dem Körper
                    var gK = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CharacterBodies.MpfbMalePath));
                    var an = gK.GetComponent<Animator>() ?? gK.AddComponent<Animator>();
                    an.avatar = AssetDatabase.LoadAllAssetsAtPath(CharacterBodies.MpfbMalePath).OfType<Avatar>().FirstOrDefault();
                    an.applyRootMotion = false;
                    sb = new System.Text.StringBuilder(Kopf() + "\n");
                    for (int i = 0; i < n; i++)
                    {
                        float t = cHum.length * i / n; cHum.SampleAnimation(gK, t);
                        Transform r = gK.transform;
                        sb.Append(t.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)).Append(";0;")
                          .Append(string.Join(";", namen.Select(x => P(r, an.GetBoneTransform(x.h))))).Append(';').Append(P(r, Spitze(an.GetBoneTransform(HumanBodyBones.LeftToes)))).Append(';').Append(P(r, Spitze(an.GetBoneTransform(HumanBodyBones.RightToes)))).Append('\n');
                    }
                    File.WriteAllText(Path.Combine(aus, datei + "_humanoid_skelett.csv"), sb.ToString());
                    UnityEngine.Object.DestroyImmediate(gK);
                    var bi = AssetImporter.GetAtPath(CharacterBodies.MpfbMalePath) as ModelImporter;
                    info = $"{datei}: {n} Bilder roh+humanoid · Körper bakeAxis={bi?.bakeAxisConversion} · Clip bakeAxis={(AssetImporter.GetAtPath(quelle) as ModelImporter)?.bakeAxisConversion} → Logs/Ashveil/Motion/ClipVergleich";
                }, true, szeneZurueck: false);
                fertig(info.Contains("Bilder"), info);
            }, false, 600);
            Auftragsordner.Registrieren("spieler_animator", (a, fertig) =>
            {
                // Sperre (15.10.2026): Mit CMU-Fortbewegung nur bauen, wenn die Messung (60 Bilder/s) besteht.
                // Geprüft werden die Clips, die der Animator wirklich nimmt (Gehen, Joggen, Rennen) – nicht jede Datei im Ordner.
                List<string> mz = null;
                if (PlayerAnimatorBuilder.MocapFortbewegung && !Messen(false, out mz)
                    && !PlayerAnimatorBuilder.Aufnahmen.All(x => mz.Any(z => z.StartsWith(x.cmu + ":") && z.EndsWith("→ OK"))))
                {
                    fertig(false, "GESPERRT: CMU-Clips bestehen die Messung nicht – Animator NICHT neu gebaut · " + string.Join(" | ", mz));
                    return;
                }
                bool ok = PlayerAnimatorBuilder.Build();
                var c = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(PlayerAnimatorBuilder.ControllerPath);
                string walk = "?";
                if (c != null)
                    foreach (var s in c.layers[0].stateMachine.states)
                        if (s.state.name == "Locomotion" && s.state.motion is UnityEditor.Animations.BlendTree bt)
                            walk = string.Join(", ", bt.children.Select(k => k.motion != null ? k.motion.name : "–"));
                fertig(ok && c != null, "Spieler-Animator " + (ok ? "neu gebaut" : "FEHLER") + " · GUID " +
                    AssetDatabase.AssetPathToGUID(PlayerAnimatorBuilder.ControllerPath) + " · Fortbewegung: " + walk);
            }, true, 600);
        }

        private static void KnieGrenzeErweitern(string pfad, float grad)
        {
            var imp = AssetImporter.GetAtPath(pfad) as ModelImporter;
            if (imp == null) return;
            var hd = imp.humanDescription; var hb = hd.human;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(pfad);
            if (hb == null || model == null) return;
            var alle = model.GetComponentsInChildren<Transform>(true);
            Transform Knochen(string human) { int k = Array.FindIndex(hb, h => h.humanName == human); return k < 0 ? null : alle.FirstOrDefault(t => t.name == hb[k].boneName); }
            var log = new List<string>();
            foreach (string seite in new[] { "Left", "Right" })
            {
                string name = seite + "LowerLeg";
                int bi = Array.IndexOf(HumanTrait.BoneName, name);
                int k = Array.FindIndex(hb, h => h.humanName == name);
                if (bi < 0 || k < 0) continue;
                Vector3 mn = Vector3.zero, mx = Vector3.zero;
                for (int dof = 0; dof < 3; dof++)
                {
                    int m = HumanTrait.MuscleFromBone(bi, dof);
                    if (m < 0) continue;
                    mn[dof] = HumanTrait.GetMuscleDefaultMin(m); mx[dof] = HumanTrait.GetMuscleDefaultMax(m);
                    if (HumanTrait.MuscleName[m].Contains("Stretch")) { mn[dof] -= grad; mx[dof] += grad; log.Add($"{HumanTrait.MuscleName[m]} {mn[dof]:0}…{mx[dof]:0}°"); }
                }
                Transform unten = Knochen(name), fuss = Knochen(seite + "Foot");
                float laenge = unten != null && fuss != null ? Vector3.Distance(unten.position, fuss.position) : 0f;
                hb[k].limit = new HumanLimit { useDefaultValues = false, min = mn, max = mx, center = Vector3.zero, axisLength = laenge };
            }
            hd.human = hb; imp.humanDescription = hd; imp.SaveAndReimport();
            Debug.Log("[Ashveil] Kniegrenze erweitert: " + string.Join(", ", log));
        }

        private static void Probe(Auftragsordner.Auftrag a, Action<bool, string> fertig)
        {
            string vorher = SceneManager.GetActiveScene().path;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var bericht = new List<string>();
            try
            {
                string figurPfad = AssetDatabase.LoadAssetAtPath<GameObject>(QuaterniusImport.BaseMalePath) != null ? QuaterniusImport.BaseMalePath : QuaterniusImport.MaleModelPath;
                var figurQuelle = AssetDatabase.LoadAssetAtPath<GameObject>(figurPfad);
                if (figurQuelle == null) { fertig(false, "Spielfigur nicht gefunden: " + figurPfad); return; }
                var clips = new List<(string name, AnimationClip clip, string info)>();
                foreach (string g in AssetDatabase.FindAssets("t:Model", new[] { Ordner }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    var c = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__preview__"));
                    var av = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Avatar>().FirstOrDefault();
                    if (c != null) clips.Add((Path.GetFileNameWithoutExtension(p), c, $"humanoid={c.isHumanMotion} avatar={(av != null && av.isValid && av.isHuman ? "gültig" : "UNGÜLTIG")} länge={c.length:0.00}s"));
                }
                var ual = AssetDatabase.LoadAllAssetsAtPath(QuaterniusImport.MaleModelPath).OfType<AnimationClip>().FirstOrDefault(x => x.name == "Walk_Loop");
                if (ual != null) clips.Add(("Quaternius_Walk_Loop", ual, "Vergleich (bisher)"));

                var boden = GameObject.CreatePrimitive(PrimitiveType.Plane); boden.transform.localScale = new Vector3(5f, 1f, 5f);
                boden.GetComponent<Renderer>().sharedMaterial = GreyboxKit.Mat("Probe_Boden", new Color(0.32f, 0.36f, 0.28f));
                var sonne = new GameObject("Sonne").AddComponent<Light>(); sonne.type = LightType.Directional; sonne.shadows = LightShadows.Soft;
                sonne.transform.rotation = Quaternion.Euler(45f, -40f, 0f); sonne.intensity = 1.4f;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.5f, 0.52f, 0.56f);
                var cam = new GameObject("Kamera").AddComponent<Camera>();
                cam.fieldOfView = 30f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.62f, 0.7f, 0.8f);
                cam.transform.position = new Vector3(5.2f, 1.0f, 0f); cam.transform.LookAt(new Vector3(0f, 0.9f, 0f));
                var rt = new RenderTexture(480, 600, 24) { antiAliasing = 4 }; var tex = new Texture2D(480, 600, TextureFormat.RGB24, false);
                cam.targetTexture = rt;
                string ziel = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "Ashveil", "Motion", "Mocap");
                Directory.CreateDirectory(ziel);
                int bilder = 0;
                foreach (var (name, clip, info) in clips)
                {
                    var figur = (GameObject)UnityEngine.Object.Instantiate(figurQuelle);
                    figur.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    var anim = figur.GetComponent<Animator>();
                    if (anim == null) anim = figur.AddComponent<Animator>();
                    if (anim.avatar == null) anim.avatar = AssetDatabase.LoadAllAssetsAtPath(figurPfad).OfType<Avatar>().FirstOrDefault();
                    anim.applyRootMotion = false;
                    if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
                    for (int k = 0; k < 6; k++)
                    {
                        AnimationMode.BeginSampling();
                        AnimationMode.SampleAnimationClip(figur, clip, clip.length * k / 6f);
                        AnimationMode.EndSampling();
                        // Figur am Ort halten (Wurzel ggf. verschoben)
                        var temp = Gebacken(figur.transform);
                        cam.Render();
                        foreach (var t in temp) { UnityEngine.Object.DestroyImmediate(t.Key.GetComponent<MeshFilter>().sharedMesh); UnityEngine.Object.DestroyImmediate(t.Key); t.Value.enabled = true; }
                        RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 480, 600), 0, 0); tex.Apply(); RenderTexture.active = null;
                        File.WriteAllBytes(Path.Combine(ziel, $"{name}_{k}.png"), tex.EncodeToPNG()); bilder++;
                    }
                    AnimationMode.StopAnimationMode();
                    UnityEngine.Object.DestroyImmediate(figur);
                    // Gegenprobe: die Aufnahme auf ihrem eigenen Skelett (Strichfigur) – zeigt, ob ein Fehler in der Datei oder im Übertragen liegt
                    string quelle = AssetDatabase.GetAssetPath(clip);
                    var eigen = AssetDatabase.LoadAssetAtPath<GameObject>(quelle);
                    if (eigen != null && quelle.Contains("/Mocap/"))
                    {
                        var sk = (GameObject)UnityEngine.Object.Instantiate(eigen);
                        sk.transform.position = Vector3.zero;
                        var mat = GreyboxKit.Mat("Probe_Strich", new Color(0.15f, 0.15f, 0.2f));
                        if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
                        for (int k = 0; k < 3; k++)
                        {
                            AnimationMode.BeginSampling();
                            AnimationMode.SampleAnimationClip(sk, clip, clip.length * k / 3f);
                            AnimationMode.EndSampling();
                            var striche = new List<GameObject>();
                            foreach (Transform t in sk.GetComponentsInChildren<Transform>())
                            {
                                if (t.parent == null || t.parent == sk.transform) continue;
                                Vector3 a0 = t.parent.position, b0 = t.position; float l = Vector3.Distance(a0, b0);
                                if (l < 0.005f) continue;
                                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                                c.GetComponent<Renderer>().sharedMaterial = mat;
                                c.transform.position = (a0 + b0) / 2f; c.transform.rotation = Quaternion.LookRotation(b0 - a0);
                                c.transform.localScale = new Vector3(0.03f, 0.03f, l);
                                striche.Add(c);
                                if (t.name.StartsWith("l")) c.GetComponent<Renderer>().sharedMaterial = GreyboxKit.Mat("Probe_Links", new Color(0.8f, 0.2f, 0.2f));
                            }
                            cam.Render();
                            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 480, 600), 0, 0); tex.Apply(); RenderTexture.active = null;
                            File.WriteAllBytes(Path.Combine(ziel, $"{name}_Skelett_{k}.png"), tex.EncodeToPNG()); bilder++;
                            foreach (var c in striche) UnityEngine.Object.DestroyImmediate(c);
                        }
                        AnimationMode.StopAnimationMode();
                        UnityEngine.Object.DestroyImmediate(sk);
                    }
                    bericht.Add(name + ": " + info);
                }
                cam.targetTexture = null; UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex);
                File.WriteAllLines(Path.Combine(ziel, "bericht.txt"), bericht);
                fertig(true, $"Mocap-Probe: {clips.Count} Clips, {bilder} Bilder in {ziel} · " + string.Join(" | ", bericht));
            }
            catch (Exception e) { fertig(false, "Mocap-Probe: " + e.GetType().Name + ": " + e.Message); }
            finally
            {
                if (!string.IsNullOrEmpty(vorher)) EditorSceneManager.OpenScene(vorher, OpenSceneMode.Single);
            }
        }

        /// <summary>
        /// „mocap_pruefen“ (15.10.2026, nach Max' Spieltest „läuft rückwärts“): misst statt Bilder anzusehen. Je Clip auf der
        /// Spielfigur (Blick +Z, links = −X): linker Fuß links? Knie nach vorn gebeugt? Standbein gleitet nach hinten (= Vorwärtsgang)?
        /// Körper blickt nach +Z? Vergleich mit Quaternius „Walk_Loop“. Ein Clip besteht nur, wenn alle Vorzeichen wie beim Vergleich sind.
        /// </summary>
        private static void Pruefen(Auftragsordner.Auftrag a, Action<bool, string> fertig)
        {
            try
            {
                bool alle = Messen(true, out List<string> zeilen);
                fertig(alle, (alle ? "alle Clips wie Quaternius" : "ABWEICHUNG") + " · " + string.Join(" | ", zeilen));
            }
            catch (Exception e) { fertig(false, "mocap_pruefen: " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>Misst alle CMU-Clips mit 60 Bildern je Sekunde; mit <paramref name="bilder"/> zusätzlich Rundum-Bilder
        /// (8 Blickrichtungen × 4 Zeitpunkte) je Clip nach Logs/Ashveil/Motion/Mocap/Rundum_&lt;Clip&gt;.png.</summary>
        /// <summary>Konsolenmeldungen (Fehler/Warnungen), die den Text enthalten – über die interne LogEntries-Schnittstelle.</summary>
        private static IEnumerable<string> KonsolenFehler(string enthaelt)
        {
            var erg = new List<string>();
            try
            {
                var asm = typeof(EditorWindow).Assembly;
                var le = asm.GetType("UnityEditor.LogEntries"); var entryT = asm.GetType("UnityEditor.LogEntry");
                var bf = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                int n = (int)le.GetMethod("StartGettingEntries", bf).Invoke(null, null);
                var e = Activator.CreateInstance(entryT);
                var get = le.GetMethod("GetEntryInternal", bf);
                var msgF = entryT.GetField("message") ?? entryT.GetField("condition");
                for (int i = Math.Max(0, n - 400); i < n; i++)
                {
                    get.Invoke(null, new object[] { i, e });
                    string m = msgF?.GetValue(e) as string;
                    if (m != null && m.Contains(enthaelt)) erg.Add(string.Join(" / ", m.Split('\n').Take(4)).Trim());
                }
                le.GetMethod("EndGettingEntries", bf).Invoke(null, null);
            }
            catch (Exception ex) { erg.Add("(Konsole nicht lesbar: " + ex.GetType().Name + ")"); }
            if (erg.Count == 0) erg.Add("keine Meldung");
            return erg.AsEnumerable().Reverse();
        }

        public static bool Messen(bool bilder, out List<string> zeilen, string figurPfadAnders = null, string bildPraefix = "")
        {
            string vorher = SceneManager.GetActiveScene().path;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            zeilen = new List<string>(); bool alle = true;
            try
            {
                string figurPfad = figurPfadAnders ?? (AssetDatabase.LoadAssetAtPath<GameObject>(QuaterniusImport.BaseMalePath) != null ? QuaterniusImport.BaseMalePath : QuaterniusImport.MaleModelPath);
                var figurQuelle = AssetDatabase.LoadAssetAtPath<GameObject>(figurPfad);
                var clips = new List<(string, AnimationClip)>();
                var ual = AssetDatabase.LoadAllAssetsAtPath(QuaterniusImport.MaleModelPath).OfType<AnimationClip>().FirstOrDefault(x => x.name == "Walk_Loop");
                if (ual != null) clips.Add(("Quaternius_Walk_Loop", ual));
                // Erst frisch importieren: geänderte Import-Regeln greifen sonst nicht bei Dateien, die Unity schon kennt (15.10.)
                // geschützt: Neu-Import bei gebundenem Animator lässt Unity 6000.6.3f1 abstürzen (AnimationsImportSchutz)
                AnimationsImportSchutz.Ausfuehren(() =>
                {
                    // KEINE Figuren hier neu importieren (15.10. (3), Absturz op_1165): Nach einem Neu-Import der Spielfigur
                    // (Avatar) stuerzt Unity beim naechsten Laden einer Szene mit Spieler-Animator ab (Animator::Rebind →
                    // AnimationClip::IsMecanimDataValid), auch wenn beim Import keine Szene offen war. Figuren-Importregeln
                    // (AvatarRegeln) greifen beim naechsten regulaeren Import; danach Unity neu starten.
                    foreach (string g in AssetDatabase.FindAssets("t:Model", new[] { Ordner }))
                        AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(g), ImportAssetOptions.ForceUpdate);
                }, immer: true);
                foreach (string g in AssetDatabase.FindAssets("t:Model", new[] { Ordner }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    var c = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__preview__"));
                    if (c != null) clips.Add((Path.GetFileNameWithoutExtension(p), c));
                    else
                    {
                        // 15.10.: Datei ohne Clip nicht still überspringen – sagen, warum (Avatar, Knochen)
                        var mi = AssetImporter.GetAtPath(p) as ModelImporter;
                        var av = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Avatar>().FirstOrDefault();
                        var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                        string knochen = go == null ? "–" : string.Join(",", go.GetComponentsInChildren<Transform>().Take(8).Select(t => t.name));
                        string takes = mi?.importedTakeInfos == null ? "–" : string.Join("/", mi.importedTakeInfos.Select(t => $"{t.name} {t.startTime:0.00}-{t.stopTime:0.00}s"));
                        string meldungen = string.Join(" ¦ ", KonsolenFehler(Path.GetFileNameWithoutExtension(p)).Take(3));
                        zeilen.Add($"{Path.GetFileNameWithoutExtension(p)}: KEIN CLIP · Typ {mi?.animationType} · Takes {takes} · Clips {mi?.clipAnimations?.Length} · Mensch-Knochen {mi?.humanDescription.human?.Length} · Unity: {meldungen} · Avatar-Einstellung {mi?.avatarSetup} · Quelle {(mi?.sourceAvatar != null ? mi.sourceAvatar.name : "–")} · Avatar {(av == null ? "fehlt" : av.isValid ? (av.isHuman ? "gültig, menschlich" : "gültig, generisch") : "UNGÜLTIG")} · Knoten {knochen}");
                        alle = false;
                    }
                }
                float[] soll = null;
                foreach (var (name, clip) in clips)
                {
                    var figur = (GameObject)UnityEngine.Object.Instantiate(figurQuelle);
                    figur.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    var anim = figur.GetComponent<Animator>() ?? figur.AddComponent<Animator>();
                    if (anim.avatar == null) anim.avatar = AssetDatabase.LoadAllAssetsAtPath(figurPfad).OfType<Avatar>().FirstOrDefault();
                    Transform B(HumanBodyBones h) => anim.GetBoneTransform(h);
                    if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
                    int N = Math.Max(10, Mathf.RoundToInt(clip.length * 60f));   // 60 Bilder je Sekunde
                    float seite = 0, knie = 0, stand = 0, blick = 0, arme = 0; int nStand = 0;
                    float minL = float.MaxValue, minR = float.MaxValue;   // tiefster Knöchel je Seite (Asymmetrie-Suche 15.10. (3))
                    var laufband = new List<float>(); var hoehen = new List<(float h, float dz)>();   // Laufband-Tempo (15.10. (3))
                    float schritt = 0f, spreiz = 0f;   // groesster Abstand der Fuesse vor/zurueck und seitlich (Schrittweite, 15.10. (3))
                    var spur = new System.Text.StringBuilder("t;fl_x;fl_y;fl_z;fr_x;fr_y;fr_z;hl_x;hl_y;hl_z;hr_x;hr_y;hr_z\n");
                    Vector3? vorL = null, vorR = null;
                    for (int k = 0; k <= N; k++)
                    {
                        AnimationMode.BeginSampling(); AnimationMode.SampleAnimationClip(figur, clip, clip.length * k / N); AnimationMode.EndSampling();
                        Vector3 L(Transform t) => figur.transform.InverseTransformPoint(t.position);
                        Vector3 fl = L(B(HumanBodyBones.LeftFoot)), fr = L(B(HumanBodyBones.RightFoot));
                        Vector3 hl = L(B(HumanBodyBones.LeftUpperLeg)), hr = L(B(HumanBodyBones.RightUpperLeg));
                        Vector3 kl = L(B(HumanBodyBones.LeftLowerLeg)), kr = L(B(HumanBodyBones.RightLowerLeg));
                        minL = Mathf.Min(minL, fl.y); minR = Mathf.Min(minR, fr.y);
                        schritt = Mathf.Max(schritt, Mathf.Abs(fl.z - fr.z)); spreiz = Mathf.Max(spreiz, Mathf.Abs(fl.x - fr.x));
                        spur.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.000};{1:0.000};{2:0.000};{3:0.000};{4:0.000};{5:0.000};{6:0.000};{7:0.000};{8:0.000};{9:0.000};{10:0.000};{11:0.000};{12:0.000}\n",
                            clip.length * k / N, fl.x, fl.y, fl.z, fr.x, fr.y, fr.z, hl.x, hl.y, hl.z, hr.x, hr.y, hr.z));
                        seite += (fl.x - fr.x);                                                     // links − rechts: soll < 0
                        knie += (kl.z - (hl.z + fl.z) / 2f) + (kr.z - (hr.z + fr.z) / 2f);           // soll > 0
                        Vector3 kopf = L(B(HumanBodyBones.Head)), hdl = L(B(HumanBodyBones.LeftHand)), hdr = L(B(HumanBodyBones.RightHand));
                        arme += kopf.y - (hdl.y + hdr.y) / 2f;                                     // Hände unter dem Kopf: soll > 0
                        Vector3 rechts = hr - hl; blick += Vector3.Cross(rechts, Vector3.up).z;     // Körpervorn·Z (rechts × oben = vorn): soll > 0
                        if (vorL.HasValue)
                        {
                            bool linksUnten = fl.y < fr.y;
                            float dz = linksUnten ? fl.z - vorL.Value.z : fr.z - vorR.Value.z;      // Standbein: soll nach hinten (dz < 0)
                            stand += dz; nStand++;
                            hoehen.Add((linksUnten ? fl.y : fr.y, dz));
                        }
                        vorL = fl; vorR = fr;
                    }
                    if (bilder) Rundum(figur, clip, bildPraefix + name);
                    { string sp = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "Ashveil", "Motion", "Mocap"); Directory.CreateDirectory(sp); File.WriteAllText(Path.Combine(sp, bildPraefix + name + "_spur.csv"), spur.ToString()); }
                    AnimationMode.StopAnimationMode();
                    UnityEngine.Object.DestroyImmediate(figur);
                    var w = new[] { seite / (N + 1), knie / (N + 1), stand / Math.Max(1, nStand) * 1000f, blick / (N + 1), arme / (N + 1) };
                    string zeile = $"{name}: Fußseite(L−R x) {w[0]:+0.000;-0.000} · Knie vorn {w[1]:+0.000;-0.000} · Standbein dz {w[2]:+0.0;-0.0} mm/Bild · Körperblick {w[3]:+0.000;-0.000} · Kopf über Händen {w[4]:+0.00;-0.00} m";
                    // Laufband: Rückwärtstempo des tieferen Fusses in den Bildern, in denen er höchstens 3 cm über dem Clip-Minimum
                    // liegt (Standphase) – Median, in m/s bei 60 Abtastungen je Sekunde. Vergleich mit Blender (Retarget-Log).
                    if (hoehen.Count > 0)
                    {
                        float hmin = hoehen.Min(x => x.h);
                        laufband = hoehen.Where(x => x.h < hmin + 0.03f).Select(x => -x.dz * 60f).OrderBy(x => x).ToList();
                    }
                    string lb = laufband.Count > 0 ? $"{laufband[laufband.Count / 2]:0.00} m/s ({laufband.Count} Standbilder)" : "–";
                    zeile += $" · tiefster Knöchel L {minL * 100f:0.0} / R {minR * 100f:0.0} cm · Laufband {lb} · Clip {clip.length:0.000} s @ {clip.frameRate:0.#} Hz · Schrittweite max {schritt * 100f:0.0} cm, seitlich max {spreiz * 100f:0.0} cm" + Ruhelage(name);
                    if (soll == null) soll = w;
                    else
                    {
                        // 16.10. (3): Standbein-Versatz nur bei Fortbewegung vergleichen – im Stand (Sprung, Landung) ist er ~0 und
                        // sein Vorzeichen Rauschen (Landung: +0,4 mm/Bild → faelschlich „FALSCH“, Animator-Bau gesperrt)
                        bool ok = Enumerable.Range(0, 5).All(i => (i == 2 && Math.Abs(w[2]) < 2f) || Math.Sign(w[i]) == Math.Sign(soll[i]));
                        if (!ok) alle = false;
                        zeile += ok ? " → OK" : " → FALSCH (Vorzeichen anders als Quaternius)";
                    }
                    zeilen.Add(zeile);
                }
                zeilen.Add(TPoseGegenRuhe(figurPfad));
                string ziel = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "Ashveil", "Motion", "Mocap");
                Directory.CreateDirectory(ziel); File.WriteAllLines(Path.Combine(ziel, "pruefung.txt"), zeilen);
                return alle;
            }
            finally { if (!string.IsNullOrEmpty(vorher)) EditorSceneManager.OpenScene(vorher, OpenSceneMode.Single); }
        }

        /// <summary>
        /// 15.10. (3): Grundhaltung der Clip-Datei gegen die Spielfigur (Superhero) – Unity baut den Avatar einer Datei ohne
        /// Skelettbeschreibung aus deren Grundhaltung; weicht sie (z. B. erstes Bild statt Ruhelage) je Bein ab, sind die
        /// Beine im Spiel unterschiedlich verdreht. Winkel je Knochen (Grad, lokale Drehung).
        /// </summary>
        private static string Ruhelage(string clipName)
        {
            string pfad = Ordner + "/" + clipName + ".fbx";
            var a = AssetDatabase.LoadAssetAtPath<GameObject>(pfad);
            var b = AssetDatabase.LoadAssetAtPath<GameObject>(QuaterniusImport.BaseMalePath);
            if (a == null || b == null) return "";
            var ta = a.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            var tb = b.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            // Weltlage statt lokaler Drehung (lokale Achsen unterscheiden sich je Exportweg): Richtung Oberschenkel→Schienbein
            // und Schienbein→Fuss sowie Fusshoehe, beides im Modellraum – zeigt, ob die Datei-Grundhaltung wirklich die
            // Ruhelage ist (Beine gerade nach unten) oder ein Bild mitten im Schritt.
            string Lage(Dictionary<string, Transform> t, Transform wurzel)
            {
                var teile = new List<string>();
                foreach (string seite in new[] { "l", "r" })
                {
                    if (!t.TryGetValue("thigh_" + seite, out var th) || !t.TryGetValue("calf_" + seite, out var ca) || !t.TryGetValue("foot_" + seite, out var fo)) continue;
                    Vector3 P(Transform x) => wurzel.InverseTransformPoint(x.position);
                    Vector3 d1 = (P(ca) - P(th)).normalized, d2 = (P(fo) - P(ca)).normalized;
                    teile.Add($"{seite}: Oberschenkel {Vector3.Angle(d1, Vector3.down):0}° Schienbein {Vector3.Angle(d2, Vector3.down):0}° Fuss {P(fo).y * 100f:0.0} cm");
                }
                return string.Join(" / ", teile);
            }
            return $" · Grundhaltung Datei [{Lage(ta, a.transform)}] gegen Spielfigur [{Lage(tb, b.transform)}]";
        }

        /// <summary>
        /// 15.10. (3): Weicht die gespeicherte T-Pose des Avatars der Spielfigur (humanDescription.skeleton) von der Ruhelage
        /// der Datei ab? Die CMU-Clips bekommen ihren Avatar aus der Datei-Ruhelage; stimmt die T-Pose der Figur nicht mit
        /// der Ruhelage ueberein, verformt Unity die Bewegung beim Uebertragen (ggf. je Bein verschieden).
        /// </summary>
        private static string TPoseGegenRuhe(string figurPfad)
        {
            var mi = AssetImporter.GetAtPath(figurPfad) as ModelImporter;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(figurPfad);
            if (mi == null || go == null) return "T-Pose-Prüfung: Figur fehlt";
            var sk = mi.humanDescription.skeleton;
            var t = go.GetComponentsInChildren<Transform>(true).GroupBy(x => x.name).ToDictionary(g => g.Key, g => g.First());
            var ab = new List<string>();
            int n = 0;
            foreach (var b in sk)
            {
                if (!t.TryGetValue(b.name, out var tr)) continue;
                n++;
                float w = Quaternion.Angle(b.rotation, tr.localRotation);
                float d = (b.position - tr.localPosition).magnitude;
                if (w > 1f || d > 0.005f) ab.Add($"{b.name} {w:0}°/{d * 100f:0.0} cm");
            }
            return $"T-Pose der Figur ({Path.GetFileNameWithoutExtension(figurPfad)}, {sk.Length} Knochen gespeichert, {n} gefunden) gegen Datei-Ruhelage: "
                + (ab.Count == 0 ? "gleich" : "ABWEICHUNG " + string.Join(", ", ab));
        }

        /// <summary>Bildbogen: Zeilen = 4 Zeitpunkte, Spalten = 8 Blickrichtungen (0° = von vorn, dann im Uhrzeigersinn).
        /// Ein roter Pfeil am Boden zeigt die Blickrichtung der Figur (+Z) – Rückwärtslaufen ist so sofort sichtbar.</summary>
        private static void Rundum(GameObject figur, AnimationClip clip, string name)
        {
            const int S = 8, Z = 4, W = 200, H = 260;
            var boden = GameObject.CreatePrimitive(PrimitiveType.Plane); boden.transform.localScale = new Vector3(2f, 1f, 2f);
            boden.GetComponent<Renderer>().sharedMaterial = GreyboxKit.Mat("Probe_Boden", new Color(0.32f, 0.36f, 0.28f));
            var pfeil = GameObject.CreatePrimitive(PrimitiveType.Cube); pfeil.transform.position = new Vector3(0f, 0.01f, 0.6f);
            pfeil.transform.localScale = new Vector3(0.08f, 0.02f, 0.8f);
            pfeil.GetComponent<Renderer>().sharedMaterial = GreyboxKit.Mat("Probe_Vorn", new Color(0.9f, 0.15f, 0.1f));
            var licht = new GameObject("Licht").AddComponent<Light>(); licht.type = LightType.Directional; licht.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.55f, 0.57f, 0.6f);
            var cam = new GameObject("Kamera").AddComponent<Camera>(); cam.fieldOfView = 32f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.62f, 0.7f, 0.8f);
            var rt = new RenderTexture(W, H, 24) { antiAliasing = 4 }; cam.targetTexture = rt;
            var bogen = new Texture2D(W * S, H * Z, TextureFormat.RGB24, false); var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            float hoehe = 1.8f;
            {
                var rs = figur.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0) { var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds); hoehe = Mathf.Clamp(b.max.y - figur.transform.position.y, 1f, 2.6f); }
            }
            for (int z = 0; z < Z; z++)
            {
                AnimationMode.BeginSampling(); AnimationMode.SampleAnimationClip(figur, clip, clip.length * z / Z); AnimationMode.EndSampling();
                var temp = Gebacken(figur.transform);
                for (int sp = 0; sp < S; sp++)
                {
                    float w = sp * 360f / S; var dir = Quaternion.Euler(0f, w, 0f) * Vector3.forward;
                    // Bildausschnitt nach Körpergröße (15.10.: Zwerge 1,3 m bis Nharim 2,15 m)
                    cam.transform.position = new Vector3(0f, hoehe * 0.6f, 0f) + dir * hoehe * 2.55f; cam.transform.LookAt(new Vector3(0f, hoehe * 0.5f, 0f));
                    cam.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = null;
                    bogen.SetPixels(sp * W, (Z - 1 - z) * H, W, H, tex.GetPixels());
                }
                foreach (var t in temp) { UnityEngine.Object.DestroyImmediate(t.Key.GetComponent<MeshFilter>().sharedMesh); UnityEngine.Object.DestroyImmediate(t.Key); t.Value.enabled = true; }
            }
            bogen.Apply();
            string ziel = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "Ashveil", "Motion", "Mocap");
            Directory.CreateDirectory(ziel); File.WriteAllBytes(Path.Combine(ziel, "Rundum_" + name + ".png"), bogen.EncodeToPNG());
            cam.targetTexture = null;
            foreach (var o in new UnityEngine.Object[] { rt, bogen, tex, boden, pfeil, licht.gameObject, cam.gameObject }) UnityEngine.Object.DestroyImmediate(o);
        }

        private static Dictionary<GameObject, SkinnedMeshRenderer> Gebacken(Transform wurzel)
        {
            var d = new Dictionary<GameObject, SkinnedMeshRenderer>();
            foreach (SkinnedMeshRenderer sk in wurzel.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
                var m = new Mesh(); sk.BakeMesh(m, true);
                var go = new GameObject("Gebacken"); go.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterials = sk.sharedMaterials;
                sk.enabled = false; d[go] = sk;
            }
            return d;
        }
    }

    /// <summary>Import-Regeln für CMU-Aufnahmen (MOV-127): Die Bewegung liegt seit 15.10. schon in Blender auf dem Skelett der
    /// Spielfigur (Tools/Blender/ashveil_mensch_retarget.py) – darum übernimmt Unity einfach den Avatar der Spielfigur.
    /// Clip als Schleife, Wurzel: Drehung und Höhe in die Pose gebacken, Vorwärtsbewegung bleibt draußen (Clips laufen am Platz).</summary>
    public sealed class CmuImportRegeln : AssetPostprocessor
    {
        private const string Koerper = "Assets/_Ashveil/Art/ThirdParty/Quaternius/BaseCharacters/Superhero_Male_FullBody.fbx";
        /// <summary>Neue Version = Unity importiert die CMU-Dateien neu (15.10.: Avatar der Spielfigur, Clips aus den Takes).</summary>
        public override uint GetVersion() => 5;   // NICHT erhoehen: Neu-Import ausserhalb des Importschutzes kann Unity abstuerzen lassen – mocap_pruefen importiert geschuetzt neu
        private bool Hier => assetPath.Replace('\\', '/').Contains("/_Ashveil/Animations/Mocap/CMU/");

        // Vorher (bis 15.10. mittags): eigener Avatar aus dem CMU-Skelett mit fester Knochenzuordnung. Unity drehte die Bewegung
        // dabei um (Knie nach hinten, Arme über dem Kopf), obwohl die Quelle in Blender richtig lag (ashveil_bvh_pruefen.py).

        private void OnPreprocessModel()
        {
            if (!Hier) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f; mi.useFileScale = true; mi.bakeAxisConversion = true;
            mi.importAnimation = true; mi.animationType = ModelImporterAnimationType.Human;
            // Knochenzuordnung und T-Pose der Spielfigur übernehmen. Nicht „CopyFromOther“: Blender schreibt den Knoten
            // „Armature“ nicht mit, der Pfad unterscheidet sich, und Unity verwarf den Avatar (15.10., „Avatar fehlt“).
            var koerper = AssetImporter.GetAtPath(Koerper) as ModelImporter;
            if (koerper != null)
            {
                HumanDescription d = koerper.humanDescription;
                // Nur die Knochenzuordnung übernehmen; die Ruhelage nimmt Unity aus der Datei selbst. Die gespeicherte Ruhelage der
                // Spielfigur passt nicht: Blender schreibt die Knochenachsen beim Export anders (Messung 15.10.: Knie nach hinten).
                d.skeleton = new SkeletonBone[0];
                AvatarRegeln.OhneVerdrehungsverteilung(ref d);   // wie die Figuren (15.10. (3))
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.humanDescription = d;
            }
            else Debug.LogWarning("[Ashveil] CMU-Import: Spielfigur nicht gefunden – " + Koerper);
            mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importCameras = false; mi.importLights = false;
        }

        private void OnPreprocessAnimation()
        {
            if (!Hier) return;
            var mi = (ModelImporter)assetImporter;
            // Aus den Takes der Datei selbst bauen: die alten Einstellungen (Take „Scene“, Bild 1–36) blieben sonst nach dem
            // Neuexport stehen und zeigten auf einen Take, den es nicht mehr gibt – Unity erzeugte keinen Clip (15.10.).
            var takes = mi.importedTakeInfos;
            var clips = takes != null && takes.Length > 0
                // 15.10. (3): Take „AshveilRuhe0…“ = Bild 0 ist die Ruhelage (Grundhaltung fuer den Avatar, Unity liest sie bei
                // Zeit 0 ueber den FBX-Evaluator) – gehoert nicht in den Clip, der Clip beginnt bei Bild 1.
                // 16.10. spaet: „Rand2“ = Bild 1 Vorlauf und zwei Nachlaufbilder (schleifenrichtige Tangenten an den Clip-Enden,
                // siehe ashveil_mensch_retarget.py BILD0) – der Clip ist Bild 2 … letztes Bild − 2.
                ? takes.Select(t => new ModelImporterClipAnimation { takeName = t.name,
                    firstFrame = t.startTime * t.sampleRate + (t.name.Contains("Rand2") ? 2f : t.name.Contains("AshveilRuhe0") ? 1f : 0f),
                    lastFrame = t.stopTime * t.sampleRate - (t.name.Contains("Rand2") ? 2f : 0f) }).ToArray()
                : mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = Path.GetFileNameWithoutExtension(assetPath);
                // 16.10. (3): Sprung- und Landungs-Ausschnitte laufen einmal (Jump_Start wird ueber JumpProgress abgetastet,
                // Jump_Land spielt einmal bis zum Stand) – alles andere sind Schleifen auf der Stelle.
                bool einmal = c.name.Contains("Sprung") || c.name.Contains("Landung");
                c.loopTime = !einmal; c.loopPose = !einmal;
                // 15.10. (3), Fund ueber Bild-fuer-Bild-Vergleich Blender/Unity: „Blickrichtung nach Koerper“ (keepOriginalOrientation=false)
                // drehte den Clip um die von Unity geschaetzte Koerperrichtung – beim Joggen 14° schief: beide Fuesse liefen
                // diagonal (vorderer Fuss 12–14 cm nach links versetzt), das Laufband hatte 1 m/s Seitanteil, der Standfuss
                // rutschte. Blender richtet die Clips schon aus der Bewegung aus → Original-Ausrichtung behalten.
                c.lockRootRotation = true; c.keepOriginalOrientation = true;
                // 15.10. (3): Hoehe IN DIE POSE backen (nach Original). Vorher nicht gebacken – die Auf-und-ab-Bewegung des
                // Koerpers ging als Wurzelbewegung verloren (die Spielfigur nutzt keine Wurzelbewegung): im Rennen/Sprint
                // erreichte im Lab (ohne prozedurale Schicht) kein Fuss den Boden (links ab 6 / rechts ab 15 cm), beim Gehen
                // rutschte der rechte Standfuss. In Blender sind dieselben Clips am Boden und gleitfrei.
                c.lockRootHeightY = true; c.keepOriginalPositionY = true; c.heightFromFeet = false;
                c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = false;
            }
            mi.clipAnimations = clips;
            // 16.10. spaet (Feinabtastung im Motion-Lab): Die Clips sind auf den Bildern nahtlos, aber ZWISCHEN dem letzten und
            // ersten Bild schwingt die Kurve aus (Unterarm 7° in 4 ms, Becken ebenso) – die Unterarme ruckten im Spiel genau
            // alle 0,68 s bei Clip-Zeit 0,02. Verdacht: Kurvenreduktion/Tangenten am Clipanfang. Darum ohne Kompression.
            mi.animationCompression = ModelImporterAnimationCompression.Off;
            mi.resampleCurves = true;
        }
    }
}
