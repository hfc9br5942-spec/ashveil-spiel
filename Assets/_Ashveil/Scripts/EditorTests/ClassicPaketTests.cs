using System.Collections.Generic;
using System.Linq;
using Ashveil.Data;
using NUnit.Framework;
using UnityEditor;
using KC = Ashveil.Data.KarethCatalog;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// Classic-Paket (Max 11.10.2026 abends, Classic-Bauplan §5): acht Botengänge, drei „Kopf von …“-Aufträge, verlängerte Ketten und
    /// sechs seltene Tiere (nach Auftrag „inhalte_28“). Prüft Geber/Abgabe, EP, Ketten, Texte, Auftragsbeute und die Werte der seltenen Tiere.
    /// </summary>
    public class ClassicPaketTests
    {
        private static T Def<T>(string id) where T : GameDefinition
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var d = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (d != null && d.Id == id) return d;
            }
            return null;
        }

        private static bool Prereq(QuestDefinition q, string id) => q.prerequisites != null && q.prerequisites.Any(p => p != null && p.Id == id);

        [Test]
        public void Botengaenge_und_Kopfauftraege()
        {
            Loc.Reload();
            List<QuestDefinition> qs = KC.ClassicQuests.Select(Def<QuestDefinition>).ToList();
            Assert.IsTrue(qs.All(q => q != null), "Alle elf Aufträge des Classic-Pakets (erst Auftrag „inhalte_28“)");
            Assert.AreEqual(KC.ClassicXpTotal, qs.Sum(q => q.xp), "EP-Summe");
            foreach (QuestDefinition q in qs)
            {
                Assert.AreEqual(KC.SceneTag, q.requiredSceneTag, q.Id + ": nur in Kareth v4");
                Assert.IsFalse(q.unmarked, q.Id + ": markiert („!“)");
                foreach (string suffix in new[] { ".name", ".offer", ".progress", ".complete", ".summary" })
                    Assert.IsTrue(Loc.Has(q.textKey + suffix), $"{q.Id}: Text {q.textKey}{suffix} (kareth.txt)");
                foreach (QuestObjective o in q.objectives)
                    Assert.IsFalse(o.Label.StartsWith("["), $"{q.Id}: Zielbeschriftung {o.labelKey}");
            }
            // Botengänge: genau ein Gespräch beim Abgabe-NPC, der nicht der Geber ist (Wege von Lager zu Lager)
            foreach (QuestDefinition q in qs.Take(8))
            {
                Assert.AreEqual(1, q.objectives.Count, q.Id + ": ein Ziel");
                Assert.AreEqual(QuestObjectiveType.TalkTo, q.objectives[0].type, q.Id + ": Gespräch");
                Assert.AreEqual(q.turnInNpc, q.objectives[0].targetId, q.Id + ": Gespräch beim Abgabe-NPC");
                Assert.AreNotEqual(q.giverNpc, q.turnInNpc, q.Id + ": Geber ≠ Abgabe");
                Assert.LessOrEqual(q.xp, 300, q.Id + ": Botengang bringt wenig EP");
            }
            // Ketten wie in Classic (im Mittel 2–3 Glieder): B1 → B2 → B3, U-5 → B7, K-B → H1, N08 → H2, N06 → H3
            Assert.IsTrue(Prereq(Def<QuestDefinition>(KC.B2), KC.B1) && Prereq(Def<QuestDefinition>(KC.B3), KC.B2), "B1 → B2 → B3");
            Assert.IsTrue(Prereq(Def<QuestDefinition>(KC.B7), KC.U5), "U-4 → U-5 → B7");
            Assert.IsTrue(Prereq(Def<QuestDefinition>(KC.H1), KC.KB), "K-A → K-B → H1");
            Assert.IsTrue(Prereq(Def<QuestDefinition>(KC.H2), KC.N08), "N03 → N08 → H2");
            Assert.IsTrue(Prereq(Def<QuestDefinition>(KC.H3), KC.N06), "N02 → N06 → H3");
            // „Kopf von …“: je ein Auftragsgegenstand, der nur beim seltenen Tier fällt
            var heads = new[] { (KC.H1, KC.ItemAltborsteTusk, KC.RareAltborste), (KC.H2, KC.ItemNarbenfangEar, KC.RareNarbenfang),
                                (KC.H3, KC.ItemKrummnadelSpine, KC.RareKrummnadel) };
            foreach ((string quest, string item, string rare) in heads)
            {
                QuestDefinition q = Def<QuestDefinition>(quest);
                Assert.AreEqual(QuestObjectiveType.Collect, q.objectives[0].type, quest + ": Sammelziel");
                Assert.AreEqual(item, q.objectives[0].targetId, quest + ": Gegenstand");
                EnemyDefinition e = Def<EnemyDefinition>(rare);
                Assert.IsNotNull(e?.lootTable, rare + ": Beutetabelle");
                Assert.IsTrue(e.lootTable.entries.Any(x => x.item != null && x.item.Id == item && x.chance >= 1f), $"{rare}: lässt {item} sicher fallen");
            }
        }

        [Test]
        public void Seltene_Tiere_staerker_seltener_mit_Beute()
        {
            Assert.AreEqual(6, KC.Rares.Length);
            var grundart = new Dictionary<string, string>
            {
                { KC.RareAltborste, KC.EnemyRooter }, { KC.RareRostschere, KC.EnemyClacker }, { KC.RareNarbenfang, KC.EnemyWolf },
                { KC.RareRaunebalg, KC.EnemyBellows }, { KC.RareKrummnadel, "enemy.needle_spitter" }, { KC.RareMoosbuckel, "enemy.peat_brute" },
            };
            foreach (string id in KC.Rares)
            {
                EnemyDefinition e = Def<EnemyDefinition>(id);
                EnemyDefinition g = Def<EnemyDefinition>(grundart[id]);
                Assert.IsNotNull(g, id + ": Grundart " + grundart[id]);
                Assert.IsNotNull(e, id + ": Definition (erst Auftrag „inhalte_28“)");
                Assert.AreEqual(e.minLevel, e.maxLevel, id + ": feste Stufe");
                Assert.GreaterOrEqual(e.respawnTime, 1200f, id + ": Wiederkehr ≥ 20 min");
                Assert.GreaterOrEqual(e.respawnTimeMax, 2700f, id + ": Wiederkehr bis 45 min");
                Assert.That(e.healthFactor / g.healthFactor, Is.EqualTo(1.8f).Within(0.01f), id + ": Leben ×1,8 der Grundart");
                Assert.That(e.damageFactor / g.damageFactor, Is.EqualTo(1.3f).Within(0.01f), id + ": Schaden ×1,3 der Grundart");
                Assert.That(e.scale / g.scale, Is.EqualTo(1.3f).Within(0.01f), id + ": Gestalt ×1,3 der Grundart");
                Assert.IsFalse(e.elite, id + ": selten, aber kein Elite");
                Assert.IsNotNull(e.lootTable, id + ": Beutetabelle");
                Assert.IsTrue(e.lootTable.groups.Any(g => g.chance >= 1f && g.items.Count > 0), id + ": sicher ein Ausrüstungsteil");
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(Ashveil.EditorTools.EnemyBuilder.PrefabFolder + "/" +
                    Ashveil.EditorTools.EnemyBuilder.KarethRareFiles[id] + ".prefab"), id + ": Prefab");
            }
        }
    }
}
