using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Visuals;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// 21.10.2026 (Tripo-Kleidung, Max: „gerne“; DEC-20261021-001): Sichtprüfung der verformbaren Kleidung – ein Spieler,
    /// Sandbox_M1, ohne Test-Client. Der Tripo-Mann (CHR_M_TR01) steht im Aussehen-Katalog (Charakter-Editor) und im Motion-Lab;
    /// die Spielerfigur der Szene trägt den beim letzten Szenenaufbau eingebauten Körper (die Szene wird hier nicht neu gebaut).
    ///   22.10.2026: umgestellt auf den Tripo-Mann (Max: „die Kleidung war nicht für die Frau gedacht“).
    ///   A. Daten: Katalog-Körper 0 = CHR_M_TR01, frayed_gambeson/travel_trousers verweisen auf ihr Kleidungsmodell, alle drei
    ///      Klassen starten mit der Reisehose (nur neue Figuren), die neue Testfigur trägt sie.
    ///   B. Vorschau (echter Weg des Charakter-Editors: CharacterPreviewStage → EquipmentVisuals.Resolve → AppearanceRig):
    ///      Tripo-Mann mit Gambeson + Reisehose – Kleid statt Schalen/Grundformen, Knochen = Körperknochen, keine Bindposen-Anpassung,
    ///      Stufen in der LODGroup, beidseitiger färbbarer Stoff, Haut darunter verdeckt. Bilder vorn/seite/hinten, Nahaufnahmen
    ///      (Schulter, Achsel, Rücken/Kragen, Schritt, Knie, Saum) im Stand, beim Sitzen (Hüfte stark gebeugt) und Jubeln (Arme hoch),
    ///      Detailstufen 1 und 2 erzwungen.
    ///   C. Startausrüstung je Klasse in der Vorschau (wie der Charakter-Editor): Kleid am Tripo-Mann.
    ///   D. Rückfall: Katalog-Körper 1 (Tripo-Frau) und die Spielerfigur der Szene zeigen Schalen/Grundformen wie bisher.
    /// Bewegung (Gehen, Rennen, Springen, Kampf, Rolle) zeigt die Motion-Lab-Aufnahme „kleidung-mann“.
    /// Bilder: Logs/Ashveil/Kleidung/. Bericht: Logs/Ashveil/Ashveil_KleidungTest.txt. Start: Auftrag „spieltest kleidung“.
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string KleidungFlag = "kleidung";
        private static bool _kleidungMode;
        private const string KleidungProfile = "duo-kleidung";
        private const string KleidKoerper = "CHR_M_TR01";   // 22.10.: Max – die Tripo-Kleidung gehört zum Mann
        private const string Gambeson = "item.frayed_gambeson", Reisehose = "item.travel_trousers";

        private IEnumerator KleidungSuite()
        {
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(KleidungProfile)) != null, 40f);
            Check(a != null, "Spieler angemeldet (neue Figur)");
            if (a == null) yield break;
            a.GetComponent<Health>()?.SetInvulnerableFor(3600f);
            yield return new WaitForSecondsRealtime(2f);
            yield return WaitUntil(() => !Ashveil.Gameplay.Story.StoryDirector.Active, 25f);

            string ordner = Ashveil.Core.ProjectPaths.Logs("Kleidung");
            Directory.CreateDirectory(ordner);
            foreach (string alt in Directory.GetFiles(ordner, "*.png"))
                try { File.Delete(alt); } catch (IOException) { }
            int bilder = 0;

            // ------------------------------------------------------------------ A. Daten
            Line("");
            Line("[A. Daten]");
            AppearanceCatalog cat = GameDataBootstrap.Catalog;
            string frauName = cat != null && cat.bodies.Count > 1 ? KoerperNetz(cat.bodies[1].model) : "-";
            string mannName = cat != null && cat.bodies.Count > 0 ? KoerperNetz(cat.bodies[0].model) : "-";
            Info($"Katalog-Körper: 0 = {mannName}, 1 = {frauName}");
            Check(mannName == KleidKoerper, $"Katalog-Körper 0 (Mann) ist {KleidKoerper} – Kleidung wird je Körpernetz gewählt");
            foreach (string id in new[] { Gambeson, Reisehose })
            {
                AppearanceDefinition d = GameData.Item(id)?.appearance;
                SkinnedModel km = d != null ? d.ModellFuer(KleidKoerper) : null;
                Check(km != null && km.model != null, $"{id}: Kleidungsmodell für {KleidKoerper} = {(km?.model != null ? km.model.name : "fehlt")}, " +
                      $"{km?.hide?.Count ?? 0} Haut-Bänder, Schalen für andere Körper {d?.shells.Count ?? 0}");
            }
            foreach (string cid in new[] { "class.warden", "class.ashstalker", "class.emberweaver" })
            {
                CharacterClassDefinition c = GameData.Class(cid);
                bool hose = c != null && c.startEquipment.Any(i => i != null && i.Id == Reisehose);
                bool gamb = c != null && c.startEquipment.Any(i => i != null && i.Id == Gambeson);
                Check(hose && gamb, $"{cid}: Startausrüstung mit Gambeson und Reisehose ({(c != null ? string.Join(", ", c.startEquipment.Where(i => i != null).Select(i => i.Id.Replace("item.", ""))) : "Klasse fehlt")})");
            }
            Check(Equipped(a, EquipmentSlot.Legs) == Reisehose && Equipped(a, EquipmentSlot.Chest) == Gambeson,
                $"Neue Testfigur ({a.ServerModel.Class?.DisplayName}) trägt Brust={Equipped(a, EquipmentSlot.Chest) ?? "-"}, Beine={Equipped(a, EquipmentSlot.Legs) ?? "-"}");

            // ------------------------------------------------------------------ B. Vorschau Tripo-Mann
            Line("");
            Line("[B. Vorschau: Tripo-Mann mit Gambeson + Reisehose]");
            CharacterPreviewStage stage = CharacterPreviewStage.Ensure();
            stage.Show(true);
            var traeger = new CharacterAppearanceData { body = 0, hairColor = 1 };
            if (cat != null) traeger.hair = AppearanceDefaults.FirstStyle(cat.hairstyles, cat, 0);
            traeger.EnsureArrays();
            var beide = new ItemStack[InventoryModel.EquipCount];
            beide[(int)EquipmentSlot.Chest] = new ItemStack(Gambeson, 1);
            beide[(int)EquipmentSlot.Legs] = new ItemStack(Reisehose, 1);
            stage.SetState(beide, traeger);
            stage.SetZoom(0f, true);
            stage.SetYaw(0f);
            yield return new WaitForSeconds(0.6f);
            AppearanceRig rig = stage.Rig;
            Check(rig != null && rig.Ready && rig.Body != null, "Vorschau-Figur aufgebaut");
            if (rig == null || rig.Body == null) { stage.Show(false); yield break; }
            Info($"Vorschau-Körper: Renderer {rig.Body.name}, Netz {rig.Body.sharedMesh.name}; Ausrüstung {rig.Summary()}");
            PruefeKleid(rig, cat, true, "Tripo-Mann");

            foreach ((string name, float yaw) in new[] { ("vorn", 0f), ("seite", 90f), ("hinten", 180f), ("dreiviertel", 35f) })
            {
                stage.SetYaw(yaw);
                yield return null;
                yield return null;
                if (stage.Capture(Path.Combine(ordner, "B_mann_" + name + ".png"))) bilder++;
            }
            stage.SetYaw(0f);
            yield return null;
            bilder += KleidNah(stage, ordner, "B_nah_stand");

            // Sitzen (Hüfte stark gebeugt – entspricht der Hocke in Blender) und Jubeln (Arme hoch – Achsel)
            stage.PlayEmote(EmoteId.Sit);
            yield return new WaitForSeconds(2.4f);
            stage.SetYaw(35f);
            yield return null;
            if (stage.Capture(Path.Combine(ordner, "B_mann_sitzen.png"))) bilder++;
            stage.SetYaw(0f);
            yield return null;
            bilder += KleidNah(stage, ordner, "B_nah_sitzen");
            Info("Sitzen: " + DurchstichInfo(rig));
            stage.PlayEmote(EmoteId.None);
            yield return new WaitForSeconds(0.5f);
            stage.PlayEmote(EmoteId.Cheer);
            yield return new WaitForSeconds(0.9f);
            if (stage.Capture(Path.Combine(ordner, "B_mann_jubeln.png"))) bilder++;
            bilder += KleidNah(stage, ordner, "B_nah_jubeln");
            stage.PlayEmote(EmoteId.None);
            yield return new WaitForSeconds(0.5f);

            // Detailstufen erzwingen (Körper + Kleid derselben Stufe)
            LODGroup lg = rig.Body.transform.parent != null ? rig.Body.transform.parent.GetComponent<LODGroup>() : null;
            if (lg != null)
            {
                for (int i = 1; i < Mathf.Min(3, lg.lodCount); i++)
                {
                    lg.ForceLOD(i);
                    yield return null;
                    if (stage.Capture(Path.Combine(ordner, $"B_mann_stufe{i}.png"))) bilder++;
                }
                lg.ForceLOD(-1);
            }

            // ------------------------------------------------------------------ C. Startausrüstung je Klasse
            Line("");
            Line("[C. Charakter-Editor: Startausrüstung je Klasse am Tripo-Mann]");
            foreach (string cid in new[] { "class.warden", "class.ashstalker", "class.emberweaver" })
            {
                CharacterClassDefinition c = GameData.Class(cid);
                if (c == null) { Check(false, cid + " fehlt"); continue; }
                stage.SetState(StartGear(c), traeger);
                stage.SetYaw(20f);
                yield return new WaitForSeconds(0.3f);
                AppearanceRig r = stage.Rig;
                int kc = KleidRenderer(r, EquipmentSlot.Chest).Count, kl = KleidRenderer(r, EquipmentSlot.Legs).Count;
                Check(kc == 3 && kl == 3 && r.BodyMasked, $"{c.DisplayName}: Kleid an Brust {kc}/3 und Beinen {kl}/3 Stufen, Haut verdeckt={r.BodyMasked} ({r.Summary()})");
                if (stage.Capture(Path.Combine(ordner, "C_start_" + cid.Replace("class.", "") + ".png"))) bilder++;
            }

            // ------------------------------------------------------------------ D. Rückfall: andere Körper
            Line("");
            Line("[D. Rückfall: andere Körper zeigen Schalen und Grundformen]");
            var frau = new CharacterAppearanceData { body = 1, hairColor = 1 };
            if (cat != null) frau.hair = AppearanceDefaults.FirstStyle(cat.hairstyles, cat, 1);
            frau.EnsureArrays();
            stage.SetState(beide, frau);
            stage.SetYaw(20f);
            yield return new WaitForSeconds(0.4f);
            AppearanceRig rm = stage.Rig;
            if (rm != null && rm.Body != null)
            {
                Info($"Katalog-Frau: Netz {rm.Body.sharedMesh.name}; Ausrüstung {rm.Summary()}");
                PruefeKleid(rm, cat, rm.Body.name == KleidKoerper || NetzName(rm) == KleidKoerper, "Katalog-Frau");
                if (stage.Capture(Path.Combine(ordner, "D_frau_vorn.png"))) bilder++;
            }
            else Check(false, "Katalog-Frau in der Vorschau aufgebaut");
            stage.Show(false);

            // Spielerfigur der Szene (Netz, EquipmentVisuals) – weiblich und männlich
            var vis = a.GetComponent<EquipmentVisuals>();
            foreach (int body in new[] { 1, 0 })
            {
                yield return Ask(a, "look", $"body={body} skin=0 hair=1 hairColor=1 beard=0 hideHelm=0 hideCloak=0");
                yield return new WaitForSecondsRealtime(0.8f);
                AppearanceRig sr = vis != null ? vis.Rig : null;
                if (sr == null || sr.Body == null) { Check(false, $"Spielerfigur (Körper {body}) hat eine Ausrüstungsdarstellung"); continue; }
                bool tripo = sr.Body.name == KleidKoerper || NetzName(sr) == KleidKoerper;
                Info($"Spielerfigur Körper {body}: Netz {NetzName(sr)}; Ausrüstung {sr.Summary()}");
                PruefeKleid(sr, cat, tripo, $"Spielerfigur der Szene (Körper {body}, {NetzName(sr)})");
                Camera main = Camera.main;
                if (main != null && CaptureUtil.Capture(main, Path.Combine(ordner, $"D_spieler_koerper{body}.png"), 960, 540)) bilder++;
            }

            Line("");
            Check(bilder >= 30, $"{bilder} Bilder unter Logs/Ashveil/Kleidung");
        }

        /// <summary>Kleid-Prüfung an einem Rig: erwartet = Tripo-Mann (Kleid statt Schalen), sonst Rückfall (Schalen, kein Kleid).</summary>
        private void PruefeKleid(AppearanceRig rig, AppearanceCatalog cat, bool erwartet, string wer)
        {
            List<SkinnedMeshRenderer> brust = KleidRenderer(rig, EquipmentSlot.Chest), beine = KleidRenderer(rig, EquipmentSlot.Legs);
            int schalen = rig.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("Equip Shell"));
            int teile = rig.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("Equip Part"));
            if (!erwartet)
            {
                Check(brust.Count == 0 && beine.Count == 0 && schalen > 0,
                    $"{wer}: kein Kleid, Schalen {schalen}, Grundformen {teile} wie bisher (Haut verdeckt={rig.BodyMasked})");
                return;
            }
            Check(brust.Count == 3 && beine.Count == 3, $"{wer}: Kleid statt Schalen – Brust {brust.Count}, Beine {beine.Count} Stufen-Renderer");
            // 22.10.: je Platz zählen – die Spielerfigur der Szene trägt auch Fußwickel und Waffen (deren Schalen/Grundformen zählten mit)
            int extraBrust = rig.RendererCount(EquipmentSlot.Chest) - brust.Count, extraBeine = rig.RendererCount(EquipmentSlot.Legs) - beine.Count;
            Check(extraBrust == 0 && extraBeine == 0, $"{wer}: keine Schalen/Grundformen für Brust ({extraBrust}) und Beine ({extraBeine}) – im ganzen Rig Schalen {schalen}, Grundformen {teile} (andere Plätze)");
            int fremd = 0, fehlend = 0, angepasst = 0, einseitig = 0;
            var knochen = new HashSet<string>();
            foreach (SkinnedMeshRenderer s in brust.Concat(beine))
            {
                Transform inst = s.transform.parent != null ? s.transform.parent : s.transform;
                foreach (Transform b in s.bones)
                {
                    if (b == null) { fehlend++; continue; }
                    knochen.Add(b.name);
                    if (!b.IsChildOf(rig.transform) || b.IsChildOf(inst)) fremd++;
                }
                if (s.rootBone == null || s.rootBone.IsChildOf(inst)) fremd++;
                if (s.sharedMesh != null && s.sharedMesh.name.Contains("(angepasst)")) angepasst++;
                foreach (Material m in s.sharedMaterials)
                    if (m == null || (m.HasProperty("_Cull") && m.GetFloat("_Cull") > 0.5f)) einseitig++;
            }
            Check(fehlend == 0 && fremd == 0, $"{wer}: Kleid-Knochen sind die Knochen des Körpers ({knochen.Count} Namen, fehlend {fehlend}, fremd {fremd})");
            Check(angepasst == 0, $"{wer}: Bindposen gleich dem Körper – RebindToBody hat nichts verschoben ({angepasst} angepasste Netze)");
            Check(einseitig == 0, $"{wer}: färbbarer, beidseitiger Stoff ({einseitig} einseitige/fehlende Materialien)");
            LODGroup lg = rig.Body.transform.parent != null ? rig.Body.transform.parent.GetComponent<LODGroup>() : null;
            if (lg != null)
            {
                LOD[] lods = lg.GetLODs();
                int ok = 0, soll = 0;
                for (int i = 0; i < lods.Length; i++)
                {
                    soll += 2;
                    string stufe = " Kleid S" + Mathf.Min(i, 2);
                    if (lods[i].renderers.Any(r => r != null && r.name == "Equip Chest" + stufe)) ok++;
                    if (lods[i].renderers.Any(r => r != null && r.name == "Equip Legs" + stufe)) ok++;
                }
                Check(ok == soll, $"{wer}: Detailstufen – {lods.Length} LOD-Stufen, Kleidstufe je Stufe {ok}/{soll}");
            }
            else Info($"{wer}: Körper ohne LODGroup – nur Kleidstufe 0 sichtbar");
            Mesh voll = OriginalNetz(cat, rig);
            int sichtbar = rig.Body.sharedMesh.triangles.Length / 3, alle = voll != null ? voll.triangles.Length / 3 : 0;
            float anteil = alle > 0 ? 1f - sichtbar / (float)alle : 0f;
            Check(rig.BodyMasked && anteil > 0.1f && anteil < 0.7f,
                $"{wer}: Haut unter der Kleidung verdeckt – sichtbar {sichtbar} von {alle} Dreiecken ({anteil * 100f:0.0} % verdeckt)");
        }

        private static List<SkinnedMeshRenderer> KleidRenderer(AppearanceRig rig, EquipmentSlot slot) =>
            rig == null ? new List<SkinnedMeshRenderer>() :
            rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.name.StartsWith("Equip " + slot + " Kleid S")).ToList();

        private static string NetzName(AppearanceRig rig)
        {
            if (rig == null || rig.Body == null || rig.Body.sharedMesh == null) return "-";
            string n = rig.Body.sharedMesh.name;
            int i = n.IndexOf(" (bedeckt)", System.StringComparison.Ordinal);
            return i > 0 ? n.Substring(0, i) : n;
        }

        /// <summary>Unverdecktes Körpernetz aus dem Katalog (gleicher Name wie das Netz des Rigs).</summary>
        private static Mesh OriginalNetz(AppearanceCatalog cat, AppearanceRig rig)
        {
            string n = NetzName(rig);
            if (cat != null)
                foreach (AppearanceCatalog.BodyEntry b in cat.bodies)
                {
                    if (b.model == null) continue;
                    foreach (SkinnedMeshRenderer s in b.model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        if (s.sharedMesh != null && s.sharedMesh.name == n) return s.sharedMesh;
                }
            return null;
        }

        private static string KoerperNetz(GameObject model)
        {
            if (model == null) return "-";
            SkinnedMeshRenderer best = null;
            foreach (SkinnedMeshRenderer s in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (s.sharedMesh != null && s.name.IndexOf("_stufe", System.StringComparison.OrdinalIgnoreCase) < 0 && (best == null || s.sharedMesh.vertexCount > best.sharedMesh.vertexCount)) best = s;
            return best != null ? best.sharedMesh.name : "-";
        }

        /// <summary>Startausrüstung einer Klasse wie im Charakter-Editor (CharacterCreatorUi: Rucksack, dann automatisch anlegen).</summary>
        private static ItemStack[] StartGear(CharacterClassDefinition c)
        {
            var model = new InventoryModel { Class = c, Level = 1 };
            foreach (ItemDefinition def in c.startEquipment)
            {
                if (def == null) continue;
                int slot = model.FirstFreeBagSlot();
                if (model.Add(new ItemStack(def.Id, 1)) > 0 && slot >= 0) model.AutoEquip(slot);
            }
            return (ItemStack[])model.Equipment.Clone();
        }

        /// <summary>Wie weit die Kleidung in der aktuellen Pose (verformt) in den Körper geht: Punkte des Kleids, deren nächster
        /// Körperpunkt hinter der Hautnormale liegt (grob, nur Bericht).</summary>
        private static string DurchstichInfo(AppearanceRig rig)
        {
            if (rig == null || rig.Body == null) return "-";
            var koerper = new Mesh();
            var kleid = new Mesh();
            try
            {
                SkinnedMeshRenderer body = rig.Body;
                body.BakeMesh(koerper, true);
                Vector3[] kv = koerper.vertices, kn = koerper.normals;
                Matrix4x4 kw = body.transform.localToWorldMatrix;
                var pos = new Vector3[kv.Length];
                var nrm = new Vector3[kv.Length];
                for (int i = 0; i < kv.Length; i++) { pos[i] = kw.MultiplyPoint3x4(kv[i]); nrm[i] = kw.MultiplyVector(i < kn.Length ? kn[i] : Vector3.up).normalized; }
                // grobes Raster (5 cm) für die Nachbarsuche
                var raster = new Dictionary<Vector3Int, List<int>>();
                Vector3Int Zelle(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / 0.05f), Mathf.FloorToInt(p.y / 0.05f), Mathf.FloorToInt(p.z / 0.05f));
                for (int i = 0; i < pos.Length; i++)
                {
                    Vector3Int z = Zelle(pos[i]);
                    if (!raster.TryGetValue(z, out List<int> l)) raster[z] = l = new List<int>();
                    l.Add(i);
                }
                int innen = 0, alle = 0;
                float tiefste = 0f;
                foreach (SkinnedMeshRenderer s in KleidRenderer(rig, EquipmentSlot.Chest).Concat(KleidRenderer(rig, EquipmentSlot.Legs)))
                {
                    if (!s.name.EndsWith("S0")) continue;
                    s.BakeMesh(kleid, true);
                    Matrix4x4 w = s.transform.localToWorldMatrix;
                    foreach (Vector3 v in kleid.vertices)
                    {
                        Vector3 p = w.MultiplyPoint3x4(v);
                        alle++;
                        Vector3Int z = Zelle(p);
                        int best = -1;
                        float bd = float.MaxValue;
                        for (int dx = -1; dx <= 1; dx++)
                            for (int dy = -1; dy <= 1; dy++)
                                for (int dz = -1; dz <= 1; dz++)
                                    if (raster.TryGetValue(new Vector3Int(z.x + dx, z.y + dy, z.z + dz), out List<int> l))
                                        foreach (int i in l) { float d = (pos[i] - p).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
                        if (best < 0) continue;
                        float tief = Vector3.Dot(p - pos[best], nrm[best]);
                        if (tief < -0.003f) { innen++; tiefste = Mathf.Min(tiefste, tief); }
                    }
                }
                return alle > 0 ? $"Kleid-Punkte hinter der Haut (> 3 mm, grob über den nächsten Körperpunkt) {innen} von {alle} ({100f * innen / alle:0.0} %), tiefster {-tiefste * 100f:0.0} cm" : "kein Kleid";
            }
            finally { Object.Destroy(koerper); Object.Destroy(kleid); }
        }

        /// <summary>Nahaufnahmen der Kleidung an der Vorschau-Figur: Schulter, Achsel, Rücken/Kragen, Schritt, Knie, Saum.</summary>
        private static int KleidNah(CharacterPreviewStage stage, string ordner, string praefix)
        {
            AppearanceRig rig = stage != null ? stage.Rig : null;
            Animator anim = rig != null ? rig.GetComponentInChildren<Animator>() : null;
            if (anim == null || !anim.isHuman) return 0;
            Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
            Vector3 P(HumanBodyBones b, Vector3 ersatz) { Transform x = B(b); return x != null ? x.position : ersatz; }
            Transform ls = B(HumanBodyBones.LeftUpperArm), rs = B(HumanBodyBones.RightUpperArm);
            if (ls == null || rs == null) return 0;
            Vector3 vorn = Vector3.Cross(Vector3.ProjectOnPlane(rs.position - ls.position, Vector3.up).normalized, Vector3.up);
            Vector3 rechts = Vector3.Cross(Vector3.up, vorn);
            Vector3 hips = P(HumanBodyBones.Hips, anim.transform.position + Vector3.up);
            int layer = LayerMask.NameToLayer(CharacterPreviewStage.LayerName);
            if (layer < 0) layer = 31;
            var go = new GameObject("Kleidung Nahkamera");
            int n = 0;
            try
            {
                Camera cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.cullingMask = 1 << layer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.05f, 0.05f, 0.06f, 1f);
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 10f;
                cam.fieldOfView = 32f;
                foreach (SkinnedMeshRenderer smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.forceMatrixRecalculationPerRender = true;
                var ansichten = new (string name, Vector3 focus, Vector3 dir, float dist)[]
                {
                    ("schulter_rechts", P(HumanBodyBones.RightUpperArm, hips) - Vector3.up * 0.04f, vorn * 0.6f + rechts * 0.75f + Vector3.up * 0.25f, 0.75f),
                    ("achsel_links", P(HumanBodyBones.LeftUpperArm, hips) - Vector3.up * 0.13f + rechts * 0.04f, vorn * 0.85f - rechts * 0.45f - Vector3.up * 0.2f, 0.75f),
                    ("ruecken_kragen", P(HumanBodyBones.Neck, hips) - Vector3.up * 0.12f, -vorn * 0.85f + rechts * 0.3f + Vector3.up * 0.35f, 0.9f),
                    ("schritt_vorn", hips - Vector3.up * 0.14f, vorn + Vector3.up * 0.08f, 0.95f),
                    ("knie_rechts", P(HumanBodyBones.RightLowerLeg, hips - Vector3.up * 0.45f), rechts * 0.85f + vorn * 0.45f + Vector3.up * 0.1f, 0.8f),
                    ("saum_hinten", hips - Vector3.up * 0.2f, -vorn + Vector3.up * 0.05f, 1.1f),
                };
                foreach (var v in ansichten)
                {
                    cam.transform.position = v.focus + v.dir.normalized * v.dist;
                    cam.transform.LookAt(v.focus);
                    if (CaptureUtil.Capture(cam, Path.Combine(ordner, praefix + "_" + v.name + ".png"), 520, 520)) n++;
                }
            }
            finally { Object.Destroy(go); }
            return n;
        }
    }
}
