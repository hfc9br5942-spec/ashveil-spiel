using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Ashveil.Gameplay.Visuals;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// 21.10.2026 (Max: „gerne“ – Tripo-Kleidung einbauen; DEC-20261021-001): verformbare Kleidung je Körper. Die Kleidung wird in
    /// Blender einmal je Körper angepasst, gewichtet und mit GENAU dem Rig des Körpers exportiert (Tools/Blender/ashveil_tripo_kleidung.py
    /// → SourceArt/Characters/Tripo/Kleidung/KLD_&lt;Körper&gt;_&lt;Teil&gt;.fbx, Detailstufen _Stufe1/_Stufe2). Import nach
    /// Art/Characters/Kleidung – bewusst NICHT unter Bodies (CharacterImportRules würde Humanoid und M_&lt;Datei&gt; erzwingen).
    ///   Auftrag kleidung_import [Präfix]   – Standard KLD_W_TR01: FBX kopieren (Importschutz), prüfen, Aussehen-Katalog neu
    ///   Auftrag kleidung_pruefen [Präfix]  – nur prüfen (Stufen, lesbar, ≤ 4 Einflüsse, Knochen ⊂ Körper, Bindposen = Körper)
    /// Im Spiel: AppearanceDefinition.modelle (appearances.json) → AppearanceRig hängt das Kleid nach Knochennamen ein.
    /// Der Mann folgt mit demselben Skript (--ziel KLD_M_TR01) ohne Code-Änderung.
    /// </summary>
    [InitializeOnLoad]
    public static class TripoKleidung
    {
        public const string Folder = ProjectSetup.Root + "/Art/Characters/Kleidung";
        public const string QuellOrdner = "SourceArt/Characters/Tripo/Kleidung";
        public const string StandardPraefix = "KLD_M_TR01";   // 22.10.: die Tripo-Kleidung gehört zum Mann (Max)
        public static readonly string[] Teile = { "Tunika", "Hose" };
        /// <summary>Erwartete Renderer je Kleidungs-FBX: Stufe 0, 1, 2 (wie der Körper).</summary>
        public const int Stufen = 3;
        /// <summary>Höchste erlaubte Abweichung der Bindposen gegen den Körper (Matrixelemente) – sonst verschiebt RebindToBody.</summary>
        public const float BindToleranz = 1e-4f;
        private static readonly Regex PraefixMuster = new Regex(@"^KLD_[A-Z]_[A-Z0-9]+$");

        static TripoKleidung()
        {
            Auftragsordner.Registrieren("kleidung_import", (a, fertig) =>
            {
                bool ok = Import(Praefix(a.parameter), out string meldung);
                fertig(ok, meldung);
            }, false, 900);
            Auftragsordner.Registrieren("kleidung_pruefen", (a, fertig) =>
            {
                bool ok = PruefenAlle(Praefix(a.parameter), out string meldung);
                fertig(ok, meldung);
            }, false, 300);
            // 21.10.2026 (Integration, Max-Entscheidung über Claude: Reisehose in die Startausrüstung aller drei Klassen): nur die
            // Gegenstands-/Klassendaten nachziehen – ItemContentBuilder.Build wie beim Szenenaufbau, aber ohne Szene und ohne die
            // übrigen Teile von inhalte_28 (Gegner, Aufträge, Datenbank). Ohne Versionssprung legt Build nur Fehlendes an; die
            // Startausrüstung bekommt über StartNachtrag die Hose, wenn eine Klasse für die Beine noch nichts hat (idempotent).
            Auftragsordner.Registrieren("inhalte_items", (a, fertig) =>
            {
                ItemContentBuilder.Build();
                fertig(true, "Gegenstände/Klassen nachgezogen – Startausrüstung: " + StartAusruestung());
            }, false, 600);
        }

        /// <summary>Kurzbericht der Startausrüstung je Klasse (für die Auftragsmeldung).</summary>
        private static string StartAusruestung()
        {
            var sb = new StringBuilder();
            foreach (string guid in AssetDatabase.FindAssets("t:CharacterClassDefinition"))
            {
                var c = AssetDatabase.LoadAssetAtPath<Ashveil.Data.CharacterClassDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (c == null) continue;
                sb.Append(c.Id).Append(" [").Append(string.Join(", ", c.startEquipment.Where(d => d != null).Select(d => d.Id.Replace("item.", "")))).Append("] ");
            }
            return sb.ToString().Trim();
        }

        private static string Praefix(string p) => string.IsNullOrWhiteSpace(p) ? StandardPraefix : p.Trim();

        /// <summary>Körper zur Kleidung: KLD_W_TR01 → CHR_W_TR01.</summary>
        public static string KoerperZu(string praefix) => "CHR_" + praefix.Substring(4);
        public static string KoerperPfad(string praefix) => CharacterBodies.Folder + "/" + KoerperZu(praefix) + ".fbx";
        public static string Pfad(string praefix, string teil) => Folder + "/" + praefix + "_" + teil + ".fbx";

        /// <summary>FBX aus SourceArt kopieren und importieren (im Importschutz), jedes Teil prüfen; fehlerhafte Teile kommen nicht ins
        /// Spiel (Kopie wieder entfernt – die Quelle in SourceArt bleibt). Danach Aussehen-Katalog neu (Modellverweise).</summary>
        public static bool Import(string praefix, out string meldung)
        {
            if (!PraefixMuster.IsMatch(praefix)) { meldung = "Präfix ungültig (erwartet z. B. " + StandardPraefix + "): " + praefix; return false; }
            string root = Ashveil.Core.ProjectPaths.ProjectRoot;
            string koerperPfad = KoerperPfad(praefix);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(koerperPfad) == null) { meldung = "Körper fehlt: " + koerperPfad + " (erst tripo_import)"; return false; }
            var quellen = new List<(string Teil, string Quelle)>();
            foreach (string teil in Teile)
            {
                string q = Path.Combine(root, QuellOrdner, praefix + "_" + teil + ".fbx");
                if (File.Exists(q)) quellen.Add((teil, q));
            }
            if (quellen.Count == 0) { meldung = "keine Kleidung unter " + QuellOrdner + " für " + praefix + " (erst ashveil_tripo_kleidung.py)"; return false; }

            var info = new StringBuilder();
            bool ok = true;
            AnimationsImportSchutz.Ausfuehren(() =>
            {
                Directory.CreateDirectory(Path.Combine(root, Folder));
                AssetDatabase.StartAssetEditing();
                try { foreach (var qu in quellen) File.Copy(qu.Quelle, Path.Combine(root, Pfad(praefix, qu.Teil)), true); }
                finally { AssetDatabase.StopAssetEditing(); }
                foreach (var qu in quellen)
                {
                    string ziel = Pfad(praefix, qu.Teil);
                    AssetDatabase.ImportAsset(ziel, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                    bool teilOk = Pruefen(ziel, koerperPfad, out string t);
                    if (!teilOk) AssetDatabase.DeleteAsset(ziel);   // falsch sitzende Kleidung nie ins Spiel – lieber Schalen
                    ok &= teilOk;
                    info.Append(qu.Teil).Append(teilOk ? " OK – " : " FEHLER (nicht übernommen) – ").Append(t).Append(" | ");
                }
            }, true);
            foreach (string teil in Teile)
                if (!quellen.Any(x => x.Teil == teil)) info.Append(teil).Append(": keine Quelle | ");
            info.Append(VerdeckungHinweis(praefix));
            // Modellverweise der Aussehen (appearances.json „modelle“) werden bei jedem Bau neu aufgelöst
            AppearanceContentBuilder.Build();
            meldung = praefix + ": " + info + " · Aussehen-Katalog neu";
            return ok;
        }

        /// <summary>Alle vorhandenen Teile eines Präfixes prüfen (nur lesen).</summary>
        public static bool PruefenAlle(string praefix, out string meldung)
        {
            if (!PraefixMuster.IsMatch(praefix)) { meldung = "Präfix ungültig: " + praefix; return false; }
            var sb = new StringBuilder();
            bool ok = true;
            int gefunden = 0;
            foreach (string teil in Teile)
            {
                string pfad = Pfad(praefix, teil);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(pfad) == null) { sb.Append(teil).Append(": nicht importiert | "); continue; }
                gefunden++;
                bool teilOk = Pruefen(pfad, KoerperPfad(praefix), out string t);
                ok &= teilOk;
                sb.Append(teil).Append(teilOk ? " OK – " : " FEHLER – ").Append(t).Append(" | ");
            }
            sb.Append(VerdeckungHinweis(praefix));
            meldung = praefix + ": " + sb;
            return ok && gefunden > 0;
        }

        private static string VerdeckungHinweis(string praefix)
        {
            string vj = Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, QuellOrdner, praefix + "_verdeckung.json");
            return File.Exists(vj) ? "verdeckung.json vorhanden (Bänder in appearances.json „hide“ übernehmen)" : "verdeckung.json fehlt";
        }

        /// <summary>
        /// Prüft ein Kleidungs-FBX gegen seinen Körper: genau <see cref="Stufen"/> Renderer (Stufe 0/1/2), alle lesbar, höchstens
        /// 4 Einflüsse je Punkt, jeder Knochen ist ein Knochen des Körpers, Bindposen gleich (≤ <see cref="BindToleranz"/>) –
        /// dann ändert RebindToBody im Spiel nichts und das Kleid sitzt exakt wie in Blender.
        /// </summary>
        public static bool Pruefen(string pfad, string koerperPfad, out string info)
        {
            var kleid = AssetDatabase.LoadAssetAtPath<GameObject>(pfad);
            var koerper = AssetDatabase.LoadAssetAtPath<GameObject>(koerperPfad);
            if (kleid == null) { info = "Modell nicht geladen: " + pfad; return false; }
            if (koerper == null) { info = "Körper nicht geladen: " + koerperPfad; return false; }
            GameObject k = Object.Instantiate(kleid), b = Object.Instantiate(koerper);
            k.hideFlags = b.hideFlags = HideFlags.HideAndDontSave;
            try { return Pruefen(k, b, out info); }
            finally { Object.DestroyImmediate(k); Object.DestroyImmediate(b); }
        }

        /// <summary>Wie oben, an Instanzen (auch für Tests mit erzeugten Modellen).</summary>
        public static bool Pruefen(GameObject kleid, GameObject koerper, out string info)
        {
            var fehler = new List<string>();
            SkinnedMeshRenderer body = koerper.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(s => s.sharedMesh != null && AppearanceRig.KleidStufe(s.name) == 0 && s.name.IndexOf("_Stufe", StringComparison.OrdinalIgnoreCase) < 0)
                .OrderByDescending(s => s.sharedMesh.vertexCount).FirstOrDefault();
            if (body == null) { info = "Körper ohne Netz"; return false; }
            // Bindposen nur für Knochen, die die Haut des Körpers benutzt (wie RebindToBody); Namen gegen das ganze Skelett
            var index = new Dictionary<string, int>();
            Transform[] bb = body.bones;
            Matrix4x4[] bbind = body.sharedMesh.bindposes;
            for (int j = 0; j < bb.Length && j < bbind.Length; j++) if (bb[j] != null && !index.ContainsKey(bb[j].name)) index[bb[j].name] = j;
            var skelett = new HashSet<string>(koerper.GetComponentsInChildren<Transform>(true).Select(t => t.name));
            int ohneBindpose = 0;

            SkinnedMeshRenderer[] smrs = kleid.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var stufen = new SortedSet<int>();
            int maxEinfluss = 0, dreiecke0 = 0;
            float maxAbw = 0f;
            var knochen = new HashSet<string>();
            foreach (SkinnedMeshRenderer smr in smrs)
            {
                Mesh m = smr.sharedMesh;
                if (m == null) { fehler.Add(smr.name + " ohne Netz"); continue; }
                int stufe = AppearanceRig.KleidStufe(smr.name);
                if (!stufen.Add(stufe)) fehler.Add("Stufe " + stufe + " doppelt");
                if (stufe == 0) dreiecke0 += m.triangles.Length / 3;
                if (!m.isReadable) { fehler.Add(smr.name + " nicht lesbar (Read/Write)"); continue; }
                var proPunkt = m.GetBonesPerVertex();
                for (int i = 0; i < proPunkt.Length; i++) maxEinfluss = Mathf.Max(maxEinfluss, proPunkt[i]);
                Transform[] kb = smr.bones;
                Matrix4x4[] kbind = m.bindposes;
                for (int i = 0; i < kb.Length; i++)
                {
                    if (kb[i] == null) { fehler.Add(smr.name + ": Knochen " + i + " fehlt"); continue; }
                    knochen.Add(kb[i].name);
                    if (!skelett.Contains(kb[i].name)) { fehler.Add("Knochen '" + kb[i].name + "' nicht im Körper"); continue; }
                    if (!index.TryGetValue(kb[i].name, out int j) || i >= kbind.Length) { ohneBindpose++; continue; }
                    // Ruhelage im Weltraum (beide Modelle am Ursprung) – über das eigene Renderer-Transform wie RebindToBody
                    Matrix4x4 a = smr.transform.localToWorldMatrix * kbind[i].inverse;
                    Matrix4x4 c = body.transform.localToWorldMatrix * bbind[j].inverse;
                    for (int e = 0; e < 16; e++) maxAbw = Mathf.Max(maxAbw, Mathf.Abs(a[e] - c[e]));
                }
            }
            if (smrs.Length != Stufen) fehler.Add(smrs.Length + " Renderer statt " + Stufen + " (Stufe 0/1/2)");
            if (!stufen.Contains(0)) fehler.Add("Stufe 0 fehlt");
            if (maxEinfluss > 4) fehler.Add("bis " + maxEinfluss + " Einflüsse je Punkt (höchstens 4)");
            if (maxAbw > BindToleranz) fehler.Add($"Bindposen weichen ab (max {maxAbw:0.000000}) – Körper neu exportiert? Skript erneut laufen lassen");
            info = $"{smrs.Length} Renderer (Stufen {string.Join("/", stufen)}), Stufe 0 {dreiecke0} Dreiecke, max {maxEinfluss} Einflüsse, " +
                   $"{knochen.Count} Knochen, Bindposen-Abweichung {maxAbw:0.000000}" +
                   (ohneBindpose > 0 ? $" ({ohneBindpose} Knochenbezüge ohne Körper-Bindpose, nicht verglichen)" : "") + (fehler.Count > 0 ? " – " + string.Join("; ", fehler.Distinct()) : "");
            return fehler.Count == 0;
        }
    }

    /// <summary>Import der Kleidung (Art/Characters/Kleidung): Maßstab und Achsen wie der Körper (gleiche Bindposen), lesbar
    /// (CharacterMerge, Rebind), Generic ohne Avatar, keine Animation, kein Material (färbbarer Stoff im Spiel), keine UV/Tangenten.</summary>
    public sealed class KleidungImportRules : AssetPostprocessor
    {
        public override uint GetVersion() => 1;

        private bool IstKleid => assetPath.Replace('\\', '/').StartsWith(TripoKleidung.Folder + "/") && assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        private void OnPreprocessModel()
        {
            if (!IstKleid) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
            mi.importBlendShapes = false;
            mi.importVisibility = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importAnimation = false;
            mi.isReadable = true;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.None;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.skinWeights = ModelImporterSkinWeights.Standard;   // höchstens 4 Einflüsse je Punkt
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
        }
    }
}
