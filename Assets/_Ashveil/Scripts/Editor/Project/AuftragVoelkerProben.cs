using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Auftrag „voelker_proben“ (15.10.2026, Figurenphase): holt die Greybox-Körper der Vorschlags-Völker (Zwerg, Nharim –
    /// Tools/Characters/make_volk_massbuehne.py → Tools/Blender/ashveil_build_body.py) ins Projekt, gibt ihnen den
    /// Humanoid-Avatar mit der Knochenzuordnung der Spielfigur und prüft Gehen/Joggen/Rennen mit 60 Bildern je Sekunde
    /// samt Rundum-Bildern (Logs/Ashveil/Motion/Mocap/Rundum_&lt;Körper&gt;_&lt;Clip&gt;.png).
    /// Die Körper sind VORSCHLAG (Max 15.10. „ok“ zu den Größen als Arbeitsmaße) und noch nicht im Spiel.
    /// </summary>
    [InitializeOnLoad]
    public static class AuftragVoelkerProben
    {
        public const string Ziel = "Assets/_Ashveil/Art/Characters/Voelker";

        static AuftragVoelkerProben()
        {
            Auftragsordner.Registrieren("voelker_proben", Starten, false, 900);
        }

        private static void Starten(Auftragsordner.Auftrag a, Action<bool, string> fertig)
        {
            string quelle = Ashveil.Core.ProjectPaths.SourceArt("Export", "Characters", "Voelker");
            if (!Directory.Exists(quelle)) { fertig(false, "keine Körper in " + quelle); return; }
            string zielVoll = Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, Ziel);
            Directory.CreateDirectory(zielVoll);
            var dateien = Directory.GetFiles(quelle, "*.fbx").OrderBy(x => x).ToArray();
            foreach (string f in dateien) File.Copy(f, Path.Combine(zielVoll, Path.GetFileName(f)), true);
            AnimationsImportSchutz.Ausfuehren(AssetDatabase.Refresh, immer: true);

            var zeilen = new List<string>();
            bool alleOk = true;
            foreach (string f in dateien)
            {
                string name = Path.GetFileNameWithoutExtension(f);
                string pfad = Ziel + "/" + name + ".fbx";
                AssetDatabase.ImportAsset(pfad, ImportAssetOptions.ForceUpdate);
                bool frau = name.EndsWith("_F");
                string referenz = frau ? QuaterniusImport.BaseFemalePath : QuaterniusImport.BaseMalePath;
                // Achsumrechnung backen wie bei den Grundkörpern (CharacterBodies-Importregel gilt nur unter Bodies/). Ohne sie
                // legt Unity die 180°-Drehung auf die Wurzel, der Avatar entsteht verdreht: Arme über dem Kopf (Prüfung 15.10.).
                var imp = AssetImporter.GetAtPath(pfad) as ModelImporter;
                if (imp != null) { imp.bakeAxisConversion = true; imp.materialImportMode = ModelImporterMaterialImportMode.None; imp.SaveAndReimport(); }
                string lage = Lage(AssetDatabase.LoadAssetAtPath<GameObject>(pfad));
                if (lage != null) { alleOk = false; zeilen.Add($"{name}: Ausrichtung FALSCH – {lage} – kein Avatar"); continue; }
                CharacterBodies.ConfigureAvatar(pfad, referenz);
                imp = AssetImporter.GetAtPath(pfad) as ModelImporter;
                if (imp != null) imp.SaveAndReimport();
                var av = AssetDatabase.LoadAllAssetsAtPath(pfad).OfType<Avatar>().FirstOrDefault();
                bool mensch = av != null && av.isValid && av.isHuman;
                if (!mensch) { alleOk = false; zeilen.Add($"{name}: Avatar UNGÜLTIG"); continue; }
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(pfad);
                var rs = model.GetComponentsInChildren<Renderer>(true);
                float hoehe = 0f;
                if (rs.Length > 0) { var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); hoehe = b.max.y - b.min.y; }
                Messen(pfad, name, out List<string> mz);
                // nur die Clips, die die Spielfigur nimmt (Gehen, Joggen, Rennen)
                var benutzt = PlayerAnimatorBuilder.Aufnahmen.Select(x => x.cmu).ToArray();
                var relevant = mz.Where(z => benutzt.Any(c => z.StartsWith(c + ":"))).ToList();
                bool ok = relevant.Count == benutzt.Length && relevant.All(z => z.EndsWith("→ OK"));
                alleOk &= ok;
                zeilen.Add($"{name} ({hoehe:0.00} m, Avatar gültig): " + (ok ? "Gehen/Joggen/Rennen OK" : "ABWEICHUNG · " + string.Join(" | ", relevant)));
            }
            // Vergleich: der bestehende Menschen-Grundkörper aus derselben Werkzeugkette (September, ältere Blender-Version)
            foreach (string pfad in new[] { CharacterBodies.NewMalePath, QuaterniusImport.BaseMalePath })
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(pfad) == null) continue;
                string n = Path.GetFileNameWithoutExtension(pfad);
                Messen(pfad, n, out List<string> mz2);
                zeilen.Add($"Vergleich {n}: " + string.Join(" | ", mz2.Where(z => z.StartsWith("CMU_16_15"))) + " · " + Aufbau(pfad));
            }
            foreach (string f in dateien) zeilen.Add("Aufbau " + Path.GetFileNameWithoutExtension(f) + ": " + Aufbau(Ziel + "/" + Path.GetFileName(f)));
            string bericht = Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, "Logs", "Ashveil", "Motion", "Mocap", "Voelker_Proben.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(bericht));
            File.WriteAllLines(bericht, zeilen);
            fertig(alleOk, string.Join(" · ", zeilen));
        }

        /// <summary>null = links bei -X, Zehen bei +Z, Wurzel ohne Drehung; sonst Fehlerbeschreibung (gedreht vs. gespiegelt).</summary>
        public static string Lage(GameObject m)
        {
            if (m == null) return "Modell fehlt";
            var t = m.GetComponentsInChildren<Transform>(true).GroupBy(x => x.name).ToDictionary(g => g.Key, g => g.First());
            if (!t.ContainsKey("thigh_l") || !t.ContainsKey("ball_leaf_l")) return "Knochen fehlen";
            if (Quaternion.Angle(m.transform.localRotation, Quaternion.identity) > 0.5f) return "Wurzel gedreht (Achsumrechnung nicht gebacken)";
            Vector3 P(string n) => m.transform.InverseTransformPoint(t[n].position);
            bool links = P("thigh_l").x < P("thigh_r").x;
            bool vorn = P("ball_leaf_l").z > P("foot_l").z;
            if (links && vorn) return null;
            return links == vorn ? "um 180° gedreht (Blick -Z)" : "links/rechts GESPIEGELT";
        }

        /// <summary>Knotenkette bis zur Hüfte mit Drehungen, Blickrichtung aus links/rechts und Armrichtung in der Ruhelage.</summary>
        private static string Aufbau(string pfad)
        {
            var m = AssetDatabase.LoadAssetAtPath<GameObject>(pfad);
            if (m == null) return "–";
            var inst = (GameObject)UnityEngine.Object.Instantiate(m);
            try
            {
                Transform T(string n) => inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
                var kette = new List<string>();
                for (var t = T("pelvis"); t != null; t = t.parent) kette.Insert(0, $"{t.name}{t.localRotation.eulerAngles:F0}");
                Vector3 L(string n) { var t = T(n); return t == null ? Vector3.zero : inst.transform.InverseTransformPoint(t.position); }
                Vector3 rechts = L("thigh_r") - L("thigh_l");
                Vector3 vorn = Vector3.Cross(rechts, Vector3.up);
                Vector3 arm = L("hand_l") - L("upperarm_l");
                return $"Kette {string.Join("/", kette)} · Blick aus l/r {vorn.normalized:F2} · linker Arm {arm.normalized:F2} · Kopf {L("Head"):F2}";
            }
            finally { UnityEngine.Object.DestroyImmediate(inst); }
        }

        private static void Messen(string pfad, string name, out List<string> zeilen)
        {
            AuftragMocapProbe.Messen(true, out zeilen, pfad, name + "_");
        }
    }
}
