using System.Collections.Generic;
using System.Linq;
using Ashveil.Data;
using Ashveil.EditorTools;
using Ashveil.Gameplay.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// 21.10.2026 (Tripo-Kleidung, DEC-20261021-001): echte verformbare Kleidung je Körper. Geprüft wird am echten Körper CHR_W_TR01
    /// mit einem erzeugten Kleid (Kopie des Körper-FBX mit dessen Rig und Stufen – also genau das, was ashveil_tripo_kleidung.py
    /// liefert: gleiche Knochen, gleiche Bindposen, _Stufe1/_Stufe2). Die echten KLD_W_TR01-FBX werden zusätzlich geprüft, sobald
    /// sie importiert sind (sonst „ignoriert“). Nichts wird gespeichert, kein Katalogbau.
    /// </summary>
    public sealed class TripoKleidungTests
    {
        private readonly List<Object> _weg = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _weg)
            {
                if (o is GameObject go && go != null && go.GetComponent<CharacterMerge>() is CharacterMerge cm) cm.Release();
                if (o != null) Object.DestroyImmediate(o);
            }
            _weg.Clear();
        }

        private T Merk<T>(T o) where T : Object { _weg.Add(o); return o; }

        // ================================================================== Daten

        [Test]
        public void Auswahl_je_Koerper_nach_exaktem_Netznamen()
        {
            var d = Merk(ScriptableObject.CreateInstance<AppearanceDefinition>());
            var m = Merk(new GameObject("KLD_TEST"));
            d.modelle.Add(new SkinnedModel { body = "CHR_W_TR01", model = m });
            Assert.AreSame(m, d.ModellFuer("CHR_W_TR01")?.model);
            Assert.IsNull(d.ModellFuer("CHR_W_MPFB01"), "MPFB-Frau bleibt bei Schalen");
            Assert.IsNull(d.ModellFuer("Superhero_Female_FullBody"), "Quaternius bleibt bei Schalen");
            Assert.IsNull(d.ModellFuer("CHR_W_TR01_Stufe1"), "nur exakter Name");
            Assert.IsNull(d.ModellFuer("CHR_W_TR02"), "neue Körperversion fällt still auf Schalen zurück – Test meldet es");
            Assert.IsNull(d.ModellFuer(null));
            Assert.IsNull(d.ModellFuer(""));
        }

        [Test]
        public void Signatur_enthaelt_das_Kleidungsmodell()
        {
            var d = Merk(ScriptableObject.CreateInstance<AppearanceDefinition>());
            string ohne = d.GeometrySignature();
            d.modelle.Add(new SkinnedModel { body = "CHR_W_TR01", modelPath = "Art/Characters/Kleidung/KLD_W_TR01_Tunika.fbx" });
            Assert.AreNotEqual(ohne, d.GeometrySignature());
            StringAssert.Contains("CHR_W_TR01", d.GeometrySignature());
        }

        // 22.10.: Max – die Tripo-Kleidung ist für den Mann gedacht (KLD_M_TR01, Beinausrichtung im Blender-Skript)
        [TestCase("frayed_gambeson", "Art/Characters/Kleidung/KLD_M_TR01_Tunika.fbx")]
        [TestCase("travel_trousers", "Art/Characters/Kleidung/KLD_M_TR01_Hose.fbx")]
        public void Json_Eintrag_hat_Modell_fuer_den_Tripo_Mann_und_behaelt_Schalen(string key, string pfad)
        {
            AppearanceDefinition d = AppearanceContentBuilder.LeseEintrag(key);
            Assert.IsNotNull(d, key + " fehlt in appearances.json");
            Merk(d);
            Assert.AreEqual(1, d.modelle.Count);
            SkinnedModel km = d.ModellFuer(TripoKoerper.MaleName);
            Assert.IsNotNull(km, "Eintrag für " + TripoKoerper.MaleName);
            Assert.AreEqual(pfad, km.modelPath);
            Assert.AreEqual(SurfaceKind.Cloth, km.surface);
            Assert.Greater(km.hide.Count, 0, "Haut darunter muss ausgeblendet werden");
            Assert.IsTrue(km.hide.All(h => h.hideBody && h.regions != BodyRegion.None && h.yMax > h.yMin));
            Assert.IsFalse(km.hide.Any(h => (h.regions & (BodyRegion.Forearms | BodyRegion.Feet | BodyRegion.Head | BodyRegion.Hands)) != 0),
                "Unterarme, Füße, Kopf und Hände werden nie verdeckt");
            Assert.Greater(d.shells.Count, 0, "Schalen bleiben für alle anderen Körper");
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<GameObject>(ProjectSetup.Root + "/" + pfad), km.model, "Modell aus dem Pfad aufgelöst (oder null, solange nicht importiert)");
        }

        [Test]
        public void Modellverweis_wird_bei_jedem_Bau_neu_aufgeloest()
        {
            var d = Merk(ScriptableObject.CreateInstance<AppearanceDefinition>());
            const string vorhanden = "Art/Characters/Bodies/CHR_W_TR01.fbx";   // irgendein vorhandenes Modell
            var erwartet = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectSetup.Root + "/" + vorhanden);
            if (erwartet == null) Assert.Ignore(vorhanden + " fehlt");
            d.modelle.Add(new SkinnedModel { body = "CHR_W_TR01", modelPath = vorhanden });
            Assert.IsTrue(AppearanceContentBuilder.ModelleAufloesen(d), "erster Bau setzt den Verweis");
            Assert.AreSame(erwartet, d.modelle[0].model);
            Assert.IsFalse(AppearanceContentBuilder.ModelleAufloesen(d), "zweiter Bau: nichts zu ändern");
            Assert.AreSame(erwartet, d.modelle[0].model, "Verweis bleibt erhalten");
            d.modelle[0].modelPath = "Art/Characters/Kleidung/GIBT_ES_NICHT.fbx";
            LogAssertWarnung();
            Assert.IsTrue(AppearanceContentBuilder.ModelleAufloesen(d));
            Assert.IsNull(d.modelle[0].model, "fehlt das FBX, fällt das Spiel auf Schalen zurück");
        }

        private static void LogAssertWarnung() =>
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Kleidungsmodell .* fehlt"));

        // ================================================================== Am Körper

        [Test]
        public void Tripo_Mann_traegt_Kleid_statt_Schalen_mit_Stufen_und_Verdeckung()   // 22.10.: Kleidung gehört zum Mann
        {
            GameObject koerperAsset = Laden(TripoKoerper.MalePath);
            GameObject tunika = KleidVorlage(koerperAsset, "KLD_TEST_M_Tunika");
            GameObject hose = KleidVorlage(koerperAsset, "KLD_TEST_M_Hose");
            AppearanceDefinition gambeson = Aussehen("frayed_gambeson", EquipType.Chest, tunika);
            AppearanceDefinition trousers = Aussehen("travel_trousers", EquipType.Legs, hose);
            PruefeKleidAmKoerper(koerperAsset, gambeson, trousers, 3);
        }

        [TestCase("Tunika", "frayed_gambeson", EquipType.Chest)]
        [TestCase("Hose", "travel_trousers", EquipType.Legs)]
        public void Echte_Kleidung_KLD_M_TR01(string teil, string key, EquipType platz)
        {
            string pfad = TripoKleidung.Pfad(TripoKleidung.StandardPraefix, teil);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(pfad) == null) Assert.Ignore(pfad + " noch nicht importiert (Auftrag kleidung_import)");
            Assert.IsTrue(TripoKleidung.Pruefen(pfad, TripoKoerper.MalePath, out string info), info);
            TestContext.WriteLine(teil + ": " + info);
            GameObject koerperAsset = Laden(TripoKoerper.MalePath);
            AppearanceDefinition d = Merk(AppearanceContentBuilder.LeseEintrag(key));
            Assert.IsNotNull(d.ModellFuer(TripoKoerper.MaleName)?.model, "appearances.json verweist auf " + pfad);
            PruefeKleidAmKoerper(koerperAsset, platz == EquipType.Chest ? d : null, platz == EquipType.Legs ? d : null, TripoKleidung.Stufen);
        }

        [TestCase("MPFB-Frau", CharacterBodies.MpfbFemalePath)]
        [TestCase("Quaternius-Frau", QuaterniusImport.BaseFemalePath)]
        [TestCase("MPFB-Mann", CharacterBodies.MpfbMalePath)]          // 21.10. Integration: Mann ohne eigene Kleidung → Schalen
        [TestCase("Tripo-Frau", TripoKoerper.FemalePath)]               // 22.10.: Kleidung gehört zum Mann – die Tripo-Frau behält Schalen
        public void Andere_Koerper_behalten_Schalen_und_Grundformen(string wer, string pfad)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(pfad);
            if (asset == null) Assert.Ignore(wer + " fehlt: " + pfad);
            var platzhalter = Merk(new GameObject("KLD_TEST_Platzhalter"));
            AppearanceDefinition mit = Aussehen("frayed_gambeson", EquipType.Chest, platzhalter);
            AppearanceDefinition ohne = Merk(Object.Instantiate(mit));
            ohne.modelle = new List<SkinnedModel>();

            GameObject inst = Merk(Object.Instantiate(asset));
            var rig = inst.AddComponent<AppearanceRig>();
            rig.Init(inst.transform);
            Assume.That(rig.Ready, wer + ": Rig nicht bereit");
            var look = new CharacterAppearanceData();
            rig.Apply(Defs(EquipmentSlot.Chest, mit), look, null);
            int mitAnzahl = rig.RendererCount(EquipmentSlot.Chest);
            Assert.AreEqual(0, Namen(inst, "Kleid").Count, wer + ": kein Kleid auf fremdem Körper");
            Assert.Greater(Namen(inst, "Equip Shell").Count + Namen(inst, "Equip Part").Count, 0, wer + ": Schalen/Grundformen wie bisher");
            rig.Apply(Defs(EquipmentSlot.Chest, null), look, null);
            rig.Apply(Defs(EquipmentSlot.Chest, ohne), look, null);
            Assert.AreEqual(rig.RendererCount(EquipmentSlot.Chest), mitAnzahl, wer + ": dieselben Teile wie ohne Modell-Eintrag");
        }

        [Test]
        public void Pruefen_erkennt_passende_und_verschobene_Bindposen()
        {
            GameObject koerperAsset = Laden(TripoKoerper.FemalePath);
            GameObject koerper = Merk(Object.Instantiate(koerperAsset));
            GameObject kleid = KleidVorlage(koerperAsset, "KLD_TEST_W_Tunika");
            Assert.IsTrue(TripoKleidung.Pruefen(kleid, koerper, out string info), info);
            StringAssert.Contains("3 Renderer", info);

            SkinnedMeshRenderer s0 = kleid.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(s => AppearanceRig.KleidStufe(s.name) == 0);
            Matrix4x4[] bp = s0.sharedMesh.bindposes;
            bp[0] = Matrix4x4.Translate(new Vector3(0f, 0.01f, 0f)) * bp[0];   // Körper neu exportiert: 1 cm verschoben
            s0.sharedMesh.bindposes = bp;
            Assert.IsFalse(TripoKleidung.Pruefen(kleid, koerper, out info), "1 cm Abweichung muss auffallen: " + info);
            StringAssert.Contains("Bindposen weichen ab", info);
        }

        [TestCase("KLD_W_TR01_Tunika", 0)]
        [TestCase("KLD_W_TR01_Tunika_Stufe1", 1)]
        [TestCase("KLD_W_TR01_Hose_Stufe2", 2)]
        [TestCase("Equip Chest Kleid S0", 0)]
        public void Stufe_aus_dem_Namen(string name, int stufe) => Assert.AreEqual(stufe, AppearanceRig.KleidStufe(name));

        // ================================================================== Zusammenfassen (CharacterLod Stufe 1)

        [Test]
        public void Zusammenfassen_nimmt_nur_eine_Detailstufe()
        {
            var root = Merk(new GameObject("Figur"));
            Animator anim = root.AddComponent<Animator>();
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material mat = Merk(new Material(sh));
            SkinnedMeshRenderer l0 = Teil(root, "L0", 8, mat), l1 = Teil(root, "L1", 6, mat), l2 = Teil(root, "L2", 4, mat), schale = Teil(root, "Schale", 5, mat);
            LODGroup g = root.AddComponent<LODGroup>();
            g.SetLODs(new[] { new LOD(0.3f, new Renderer[] { l0 }), new LOD(0.14f, new Renderer[] { l1 }), new LOD(0.06f, new Renderer[] { l2 }) });
            var merge = root.AddComponent<CharacterMerge>();
            var alle = new List<Renderer> { l0, l1, l2, schale };
            bool vorher = CharacterMerge.NurEineStufe;
            try
            {
                // Beleg des Nebenbefunds: altes Verhalten nimmt alle Stufen zugleich auf (8 + 6 + 4 + 5 Punkte)
                CharacterMerge.NurEineStufe = false;
                Assert.IsTrue(merge.Build(anim, alle));
                Assert.AreEqual(8 + 6 + 4 + 5, merge.Merged.sharedMesh.vertexCount, "altes Verhalten: alle Detailstufen im zusammengefassten Netz");
                merge.Release();

                CharacterMerge.NurEineStufe = true;
                Assert.IsTrue(merge.Build(anim, alle));
                Assert.AreEqual(6 + 5, merge.Merged.sharedMesh.vertexCount, "nur Stufe 1 + Teile ausserhalb der LODGroup");
                Assert.IsTrue(l0.forceRenderingOff && l1.forceRenderingOff && l2.forceRenderingOff && schale.forceRenderingOff, "zusammengefasst: Einzelteile und andere Stufen aus");
                Assert.IsTrue(merge.StillMatches(alle), "ausgeblendete andere Stufen gelten nicht als neues Teil");
                merge.Release();
                Assert.IsFalse(l0.forceRenderingOff || l1.forceRenderingOff || l2.forceRenderingOff || schale.forceRenderingOff, "nach dem Aufheben wieder gezeichnet");
            }
            finally { CharacterMerge.NurEineStufe = vorher; }
        }

        // ================================================================== Hilfen

        private void PruefeKleidAmKoerper(GameObject koerperAsset, AppearanceDefinition brust, AppearanceDefinition beine, int stufenJeKleid)
        {
            GameObject inst = Merk(Object.Instantiate(koerperAsset));
            var rig = inst.AddComponent<AppearanceRig>();
            rig.Init(inst.transform);
            Assert.IsTrue(rig.Ready);
            Mesh original = rig.Body.sharedMesh;
            TestContext.WriteLine("Körpernetz: " + original.name + " / Renderer " + rig.Body.name);
            int dreiecke = original.triangles.Length;
            // Knochen des Körpers vor dem Einhängen (gleiche Reihenfolge wie AppearanceRig.Init: erster Name gewinnt)
            var koerperKnochen = new Dictionary<string, Transform>();
            foreach (Transform t in inst.GetComponentsInChildren<Transform>(true)) if (!koerperKnochen.ContainsKey(t.name)) koerperKnochen[t.name] = t;
            var defs = new AppearanceDefinition[CharacterAppearanceData.SlotCount];
            if (brust != null) defs[(int)EquipmentSlot.Chest] = brust;
            if (beine != null) defs[(int)EquipmentSlot.Legs] = beine;
            rig.Apply(defs, new CharacterAppearanceData(), null);

            string summary = rig.Summary();
            TestContext.WriteLine("Summary: " + summary);
            var plaetze = new List<EquipmentSlot>();
            if (brust != null) plaetze.Add(EquipmentSlot.Chest);
            if (beine != null) plaetze.Add(EquipmentSlot.Legs);
            foreach (EquipmentSlot p in plaetze) StringAssert.Contains(p + "=" + defs[(int)p].Id + "/" + stufenJeKleid, summary);
            Assert.AreEqual(0, Namen(inst, "Equip Shell").Count, "keine Schalen für diese Plätze");
            Assert.AreEqual(0, Namen(inst, "Equip Part").Count, "keine Grundformen für diese Plätze");

            LODGroup g = rig.Body.transform.parent.GetComponent<LODGroup>();
            Assert.IsNotNull(g, "Körper-LODGroup");
            LOD[] lods = g.GetLODs();
            foreach (EquipmentSlot p in plaetze)
            {
                List<SkinnedMeshRenderer> kleid = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.name.StartsWith("Equip " + p + " Kleid S")).ToList();
                Assert.AreEqual(stufenJeKleid, kleid.Count, p + ": Kleid mit allen Stufen");
                foreach (SkinnedMeshRenderer s in kleid)
                {
                    Assert.IsFalse(s.bones.Any(b => b == null), s.name + ": keine fehlenden Knochen");
                    foreach (Transform b in s.bones)
                        Assert.IsTrue(koerperKnochen.TryGetValue(b.name, out Transform kb) && kb == b, s.name + ": Knochen '" + b.name + "' ist der Knochen des Körpers");
                    Assert.IsTrue(s.rootBone != null && koerperKnochen.TryGetValue(s.rootBone.name, out Transform rb) && rb == s.rootBone, s.name + ": Wurzelknochen des Körpers");
                    StringAssert.DoesNotContain("(angepasst)", s.sharedMesh.name, "Bindposen gleich – RebindToBody ändert nichts");
                    Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.On, s.shadowCastingMode);
                    int stufe = int.Parse(s.name.Substring(s.name.Length - 1));
                    if (stufe < lods.Length) Assert.IsTrue(lods[stufe].renderers.Contains(s), s.name + " in LOD " + stufe);
                    else Assert.IsFalse(s.enabled, s.name + ": Stufe ohne Körperstufe ist aus");
                }
                // Stufe 0 ist der gerenderte Hauptteil, nicht in den fernen Stufen
                Assert.IsFalse(lods.Skip(1).Any(l => l.renderers.Contains(kleid.First(s => s.name.EndsWith("S0")))), p + ": Stufe 0 nur in LOD 0");
            }
            Assert.IsTrue(lods[0].renderers.Contains(rig.Body), "Körper bleibt in LOD 0");
            Assert.IsTrue(rig.BodyMasked, "Haut unter der Kleidung ausgeblendet");
            int sichtbar = rig.Body.sharedMesh.triangles.Length;
            TestContext.WriteLine($"Körper: {dreiecke / 3} Dreiecke, sichtbar {sichtbar / 3} ({100f * (dreiecke - sichtbar) / dreiecke:0.0} % verdeckt)");
            Assert.Less(sichtbar, dreiecke);
            Assert.Greater(sichtbar, dreiecke / 5, "Kopf, Hände, Unterarme und Füße bleiben sichtbar");

            // Ablegen: Kleid weg, LODGroup wieder nur Körper, Haut wieder ganz
            rig.Apply(new AppearanceDefinition[CharacterAppearanceData.SlotCount], new CharacterAppearanceData(), null);
            Assert.AreEqual(0, inst.GetComponentsInChildren<SkinnedMeshRenderer>(true).Count(s => s.name.Contains(" Kleid S")), "Kleid entfernt");
            Assert.IsTrue(g.GetLODs().All(l => l.renderers.All(r => r != null && !r.name.StartsWith("Equip"))), "LODGroup ohne Kleid");
            Assert.IsFalse(rig.BodyMasked);
        }

        /// <summary>Kleid wie aus ashveil_tripo_kleidung.py: Kopie des Körper-FBX (Rig + Netz + _Stufe1/_Stufe2), umbenannt.</summary>
        private GameObject KleidVorlage(GameObject koerperAsset, string name)
        {
            GameObject v = Merk(Object.Instantiate(koerperAsset));
            v.name = name;
            foreach (SkinnedMeshRenderer s in v.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                int stufe = AppearanceRig.KleidStufe(s.name);
                s.name = stufe > 0 ? name + "_Stufe" + stufe : name;
                Mesh kopie = Merk(Object.Instantiate(s.sharedMesh));
                kopie.name = s.name;
                s.sharedMesh = kopie;
            }
            return v;
        }

        private AppearanceDefinition Aussehen(string key, EquipType slot, GameObject modell)
        {
            AppearanceDefinition d = AppearanceContentBuilder.LeseEintrag(key);
            if (d == null)
            {
                d = ScriptableObject.CreateInstance<AppearanceDefinition>();
                d.slot = slot;
                d.shells.Add(new ShellLayer { regions = slot == EquipType.Chest ? BodyRegion.Chest | BodyRegion.Belly : BodyRegion.Thighs, yMin = 0f, yMax = 1f });
                d.parts.Add(new RigidPart { shape = ProcShape.Torus, bone = "pelvis", size = new Vector3(0.3f, 0.05f, 0.3f), surface = SurfaceKind.Cloth });
            }
            Merk(d);
            if (d.modelle.Count == 0)
                d.modelle.Add(new SkinnedModel { body = TripoKoerper.MaleName, hide = new List<ShellLayer> { new ShellLayer { regions = BodyRegion.Pelvis, yMin = 0f, yMax = 1f } } });
            d.modelle[0].model = modell;
            return d;
        }

        private static AppearanceDefinition[] Defs(EquipmentSlot slot, AppearanceDefinition d)
        {
            var defs = new AppearanceDefinition[CharacterAppearanceData.SlotCount];
            defs[(int)slot] = d;
            return defs;
        }

        private static List<string> Namen(GameObject root, string teil) =>
            root.GetComponentsInChildren<Transform>(true).Select(t => t.name).Where(n => n.Contains(teil)).ToList();

        private static GameObject Laden(string pfad)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(pfad);
            if (go == null) Assert.Ignore(pfad + " fehlt (erst tripo_import)");
            return go;
        }

        private SkinnedMeshRenderer Teil(GameObject root, string name, int punkte, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var m = Merk(new Mesh { name = name });
            var v = new Vector3[punkte];
            for (int i = 0; i < punkte; i++) v[i] = new Vector3(Mathf.Cos(i), Mathf.Sin(i), 0f);
            var t = new List<int>();
            for (int i = 1; i < punkte - 1; i++) { t.Add(0); t.Add(i); t.Add(i + 1); }
            m.vertices = v;
            m.SetTriangles(t, 0);
            var w = new BoneWeight[punkte];
            for (int i = 0; i < punkte; i++) w[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            m.boneWeights = w;
            m.bindposes = new[] { Matrix4x4.identity };
            m.RecalculateNormals();
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = m;
            smr.bones = new[] { root.transform };
            smr.rootBone = root.transform;
            smr.sharedMaterial = mat;
            return smr;
        }
    }
}
