using System.Collections.Generic;
using System.Linq;
using Ashveil.Gameplay.Enemies;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// 22.10.2026 (COMBAT-026a/b): Gliedtrennung an einem echten menschlichen Gegner (Enemy_Deserteur, unverknüpfte Kopie wie zur
    /// Laufzeit). Geprüft: Trennpunktwahl aus Trefferhöhe/-seite, kein Abtrennen ohne Hiebwaffe, das Teil bekommt genau die
    /// Dreiecke, die der Körper verliert, und Wiederherstellen macht den Gegner wieder ganz (Respawn).
    /// </summary>
    public sealed class ZerstueckelungTests
    {
        private const string Prefab = "Assets/_Ashveil/Prefabs/Enemies/Enemy_Deserteur.prefab";
        private GameObject _g;

        [TearDown]
        public void TearDown()
        {
            if (_g != null) Object.DestroyImmediate(_g);
        }

        private Animator Aufbauen()
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            if (p == null) Assert.Ignore("Prefab fehlt: " + Prefab);
            _g = Object.Instantiate(p);
            Animator a = _g.GetComponentInChildren<Animator>();
            if (a == null || !a.isHuman) Assert.Ignore("kein Humanoid-Animator");
            a.Rebind();
            a.Update(0f);
            return a;
        }

        private static int Dreiecke(Animator a) =>
            a.GetComponentsInChildren<SkinnedMeshRenderer>().Where(s => s.sharedMesh != null).Sum(s => s.sharedMesh.triangles.Length / 3);

        [Test]
        public void Trennpunkt_aus_Trefferhoehe_und_Seite()
        {
            Animator a = Aufbauen();
            Transform kopf = a.GetBoneTransform(HumanBodyBones.Head);
            Transform knieL = a.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            Assert.AreEqual(Zerstueckelung.Trennpunkt.Kopf, Zerstueckelung.Waehle(a, kopf.position, Vector3.right));
            Zerstueckelung.Trennpunkt bein = Zerstueckelung.Waehle(a, knieL.position, Vector3.right);
            Assert.IsTrue(bein == Zerstueckelung.Trennpunkt.OberschenkelL || bein == Zerstueckelung.Trennpunkt.OberschenkelR, "tiefer Treffer trennt ein Bein, war " + bein);
        }

        [Test]
        public void Nur_Hiebwaffen_trennen()
        {
            Assert.IsTrue(Zerstueckelung.TrenntKlasse(Ashveil.Data.WeaponClass.Sword));
            Assert.IsTrue(Zerstueckelung.TrenntKlasse(Ashveil.Data.WeaponClass.TwoHandedAxe));
            Assert.IsFalse(Zerstueckelung.TrenntKlasse(Ashveil.Data.WeaponClass.Dagger), "Stich trennt nicht");
            Assert.IsFalse(Zerstueckelung.TrenntKlasse(Ashveil.Data.WeaponClass.Hammer), "Wucht trennt nicht");
            Assert.IsFalse(Zerstueckelung.TrenntKlasse(Ashveil.Data.WeaponClass.Bow));
        }

        /// <summary>
        /// Codex-Review 22.10. (P1): der Server entscheidet am Treffer selbst – Waffen-Id des Treffers, kritisch, tödlich. Zauber
        /// (Id ist keine Waffe), nicht kritische und nicht tödliche Treffer trennen nie, egal was der Angreifer in der Hand hält.
        /// </summary>
        [Test]
        public void Server_entscheidet_am_Treffer()
        {
            Animator a = Aufbauen();
            var db = AssetDatabase.LoadAssetAtPath<Ashveil.Data.DefinitionDatabase>("Assets/_Ashveil/Data/DefinitionDatabase.asset");
            if (db == null) Assert.Inconclusive("Datenbank fehlt");
            Ashveil.Data.WeaponDefinition schwert = db.All.OfType<Ashveil.Data.WeaponDefinition>().FirstOrDefault(w => w.weaponClass == Ashveil.Data.WeaponClass.Sword);
            Ashveil.Data.WeaponDefinition dolch = db.All.OfType<Ashveil.Data.WeaponDefinition>().FirstOrDefault(w => w.weaponClass == Ashveil.Data.WeaponClass.Dagger);
            if (schwert == null) Assert.Inconclusive("kein Schwert in den Daten");
            var dbFeld = typeof(Ashveil.Data.GameData).GetField("_db", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            object vorher = dbFeld.GetValue(null);
            try
            {
                Ashveil.Data.GameData.Register(db);
                Vector3 kopf = a.GetBoneTransform(HumanBodyBones.Head).position;
                var hieb = new Ashveil.Gameplay.Combat.DamageInfo { Amount = 50f, IsCritical = true, WeaponId = schwert.Id, HitPoint = kopf, Direction = Vector3.right };
                Assert.AreEqual((byte)Zerstueckelung.Trennpunkt.Kopf, Zerstueckelung.TrennpunktFuer(hieb, 0f, _g), "kritischer tödlicher Schwerthieb an den Hals");

                var zauber = hieb; zauber.WeaponId = "spell.frost_bolt";
                Assert.AreEqual(0, Zerstueckelung.TrennpunktFuer(zauber, 0f, _g), "Zauber trennt nie");
                var ohneId = hieb; ohneId.WeaponId = null;
                Assert.AreEqual(0, Zerstueckelung.TrennpunktFuer(ohneId, 0f, _g), "ohne Waffen-Id nie");
                var normal = hieb; normal.IsCritical = false;
                Assert.AreEqual(0, Zerstueckelung.TrennpunktFuer(normal, 0f, _g), "nicht kritisch");
                Assert.AreEqual(0, Zerstueckelung.TrennpunktFuer(hieb, 12f, _g), "nicht tödlich");
                var ausgewichen = hieb; ausgewichen.Dodged = true;
                Assert.AreEqual(0, Zerstueckelung.TrennpunktFuer(ausgewichen, 0f, _g), "ausgewichen");
                if (dolch != null)
                {
                    var stich = hieb; stich.WeaponId = dolch.Id;
                    Assert.AreEqual(0, Zerstueckelung.TrennpunktFuer(stich, 0f, _g), "Dolchstich trennt nicht");
                }
            }
            finally { dbFeld.SetValue(null, vorher); }
        }

        /// <summary>23.10. (COMBAT-026b): Zusatzbeute nur bei Zerstückelung, je Berechtigtem gewürfelt, in die persönliche Leichenbeute.</summary>
        [Test]
        public void Zusatzbeute_nur_bei_Zerstueckelung()
        {
            var db = AssetDatabase.LoadAssetAtPath<Ashveil.Data.DefinitionDatabase>("Assets/_Ashveil/Data/DefinitionDatabase.asset");
            if (db == null) Assert.Inconclusive("Datenbank fehlt");
            Ashveil.Data.ItemDefinition item = db.All.OfType<Ashveil.Data.ItemDefinition>().FirstOrDefault(i => !string.IsNullOrEmpty(i.Id));
            if (item == null) Assert.Inconclusive("kein Gegenstand");
            var tabelle = ScriptableObject.CreateInstance<Ashveil.Data.LootTableDefinition>();
            tabelle.entries.Add(new Ashveil.Data.LootTableDefinition.Entry { item = item, chance = 1f, minCount = 1, maxCount = 1 });
            var go = new GameObject("Leiche");
            try
            {
                var leiche = go.AddComponent<Ashveil.Gameplay.Items.CorpseLoot>();
                string[] wer = { "profil.a", "profil.b" };
                Assert.AreEqual(0, Zerstueckelung.ZusatzBeute(0, leiche, tabelle, wer, null, 7, null, 1f), "ohne Zerstückelung keine Zusatzbeute");
                Assert.IsFalse(leiche.HasLootFor("profil.a"));
                Assert.AreEqual(0, Zerstueckelung.ZusatzBeute(1, leiche, tabelle, wer, null, 7, null, 0f), "Chance 0: nichts");
                Assert.AreEqual(2, Zerstueckelung.ZusatzBeute(1, leiche, tabelle, wer, null, 7, null, 1f), "Chance 1: jeder Berechtigte");
                Assert.IsTrue(leiche.HasLootFor("profil.a") && leiche.HasLootFor("profil.b"));
                Assert.AreEqual(item.Id, leiche.Get("profil.a")[0].id);
                Assert.AreEqual(0, Zerstueckelung.ZusatzBeute(1, leiche, tabelle, wer, null, 7, (p, i) => false, 1f), "Auftragsbeute-Regel greift");
                Assert.That(Zerstueckelung.ZusatzChance, Is.InRange(0.03f, 0.2f), "geringe Chance");
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(tabelle); }
        }

        private static int EigeneNetze() =>
            Resources.FindObjectsOfTypeAll<Mesh>().Count(m => m != null && (m.name.EndsWith(" (Teil)") || m.name.EndsWith(" (getrennt)")));

        /// <summary>
        /// Codex-Review 22.10. (P1): gebackene Teile und Körperkopien werden freigegeben – bei Respawn, wenn das Teil vorher verschwindet
        /// (Liegezeit/Budget) und wenn die ganze Leiche verschwindet. Die Zahl der erzeugten Meshes steigt nicht dauerhaft.
        /// </summary>
        [Test]
        public void Erzeugte_Netze_werden_freigegeben()
        {
            int vorher = EigeneNetze();
            Animator a = Aufbauen();
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(Zerstueckelung.Ausfuehren(a, Zerstueckelung.Trennpunkt.OberarmL, Vector3.right, _g.transform));
                Assert.Greater(EigeneNetze(), vorher, "Zyklus " + i + ": Netze erzeugt");
                Zerstueckelung.Wiederherstellen(a.gameObject);
                Assert.AreEqual(vorher, EigeneNetze(), "Zyklus " + i + ": nach Respawn freigegeben");
            }
            // Teil verschwindet vor dem Respawn (Liegezeit, Budget)
            Assert.IsTrue(Zerstueckelung.Ausfuehren(a, Zerstueckelung.Trennpunkt.Kopf, Vector3.right, _g.transform));
            Object.DestroyImmediate(_g.transform.Find("Abgetrennt " + Zerstueckelung.Trennpunkt.Kopf).gameObject);
            Zerstueckelung.Wiederherstellen(a.gameObject);
            Assert.AreEqual(vorher, EigeneNetze(), "Teil vorher weg");
            // ganze Leiche verschwindet
            Assert.IsTrue(Zerstueckelung.Ausfuehren(a, Zerstueckelung.Trennpunkt.OberschenkelR, Vector3.right, _g.transform));
            Object.DestroyImmediate(_g);
            _g = null;
            Assert.AreEqual(vorher, EigeneNetze(), "Leiche weg");
        }

        [TestCase(Zerstueckelung.Trennpunkt.Kopf)]
        [TestCase(Zerstueckelung.Trennpunkt.OberarmL)]
        [TestCase(Zerstueckelung.Trennpunkt.UnterarmR)]
        [TestCase(Zerstueckelung.Trennpunkt.OberschenkelR)]
        public void Teil_bekommt_genau_die_Dreiecke_die_dem_Koerper_fehlen_und_Wiederherstellen_macht_ganz(Zerstueckelung.Trennpunkt t)
        {
            Animator a = Aufbauen();
            var vorher = a.GetComponentsInChildren<SkinnedMeshRenderer>().ToDictionary(s => s, s => s.sharedMesh);
            int ganz = Dreiecke(a);

            Assert.IsTrue(Zerstueckelung.Ausfuehren(a, t, Vector3.right, _g.transform), "getrennt");
            Transform teil = _g.transform.Find("Abgetrennt " + t);
            Assert.IsNotNull(teil, "abgetrenntes Teil unter der Leiche");
            int imTeil = teil.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh != null && f.sharedMesh.name.EndsWith(" (Teil)"))   // nur gebackene Haut, nicht Kappen/Waffenkopie
                .Sum(f => f.sharedMesh.triangles.Length / 3);
            int rest = Dreiecke(a);
            Assert.Greater(imTeil, 50, "Teil hat Geometrie");
            Assert.AreEqual(ganz, rest + imTeil, "keine Dreiecke verloren oder doppelt");
            Assert.IsNotNull(teil.GetComponent<Rigidbody>(), "Teil fällt mit Physik");

            Zerstueckelung.Wiederherstellen(a.gameObject);
            Assert.AreEqual(ganz, Dreiecke(a), "wieder ganz");
            foreach (KeyValuePair<SkinnedMeshRenderer, Mesh> kv in vorher) Assert.AreSame(kv.Value, kv.Key.sharedMesh, "Originalnetz zurück: " + kv.Key.name);
            Assert.IsTrue(teil == null, "Teil entfernt");
        }
    }
}
