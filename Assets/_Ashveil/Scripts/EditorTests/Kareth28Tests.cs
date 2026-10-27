using System.Collections.Generic;
using System.Linq;
using Ashveil.Data;
using Ashveil.Gameplay.Quests;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using KC = Ashveil.Data.KarethCatalog;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// #28 Kareth v4, Welle 0: Daten der elf Ergänzungsaufträge und der neuen Greybox-Tiere (nach Auftrag „inhalte_28“).
    /// Aufträge (Stufen, EP, Szenenkennzeichen, Texte), Szenenfilter (nur Kareth v4), Tiere (neutral je Aggro-Profil, Schlickwurf,
    /// Beute, Balance-Rolle), Belohnungen nach Klasse (genau eine Klasse je Teil) und die Figuren mit Gruß nach Marke.
    /// </summary>
    public class Kareth28Tests
    {
        private static QuestDefinition Quest(string id)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:QuestDefinition"))
            {
                var q = AssetDatabase.LoadAssetAtPath<QuestDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (q != null && q.Id == id) return q;
            }
            return null;
        }

        private static T Def<T>(string id) where T : GameDefinition
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var d = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (d != null && d.Id == id) return d;
            }
            return null;
        }

        [Test]
        public void Elf_Auftraege_wie_in_der_Liste()
        {
            Loc.Reload();
            List<QuestDefinition> qs = KC.Quests.Select(Quest).ToList();
            Assert.IsTrue(qs.All(q => q != null), "Alle elf Auftrags-Assets (erst Auftrag „inhalte_28“)");
            Assert.AreEqual(KC.QuestXpTotal, qs.Sum(q => q.xp), "EP-Summe (Auftragsliste: K-A–K-D 1 850, U-1–U-7 6 600)");
            CollectionAssert.AreEqual(new[] { 1, 2, 2, 4, 5, 5, 6, 7, 8, 9, 7 }, qs.Select(q => q.minLevel).ToArray(), "Stufen");
            foreach (QuestDefinition q in qs)
            {
                Assert.AreEqual(KC.SceneTag, q.requiredSceneTag, q.Id + ": nur in Kareth v4");
                foreach (string suffix in new[] { ".name", ".offer", ".progress", ".complete", ".summary" })
                    Assert.IsTrue(Loc.Has(q.textKey + suffix), $"{q.Id}: Text {q.textKey}{suffix} (kareth.txt)");
                foreach (QuestObjective o in q.objectives)
                    Assert.IsFalse(o.Label.StartsWith("["), $"{q.Id}: Zielbeschriftung {o.labelKey}");
                Assert.AreEqual(q.Id == KC.U7, q.unmarked, q.Id + ": nur U-7 ist unmarkiert");
            }
            Assert.IsTrue(Quest(KC.U2).grantsFlags.Contains(KC.FlagJugReturned), "U-2 setzt die Marke „Krug zurück“");
        }

        [Test]
        public void Auftraege_nur_in_Szenen_mit_Kennzeichen()
        {
            QuestDefinition ka = Quest(KC.KA);
            Assert.IsNotNull(ka, "K-A-Asset");
            var go = new GameObject("Test Szenenkennzeichen");
            try
            {
                System.Func<string, bool> done = id => id != ka.Id;   // Q1 erledigt, K-A noch nicht
                SceneQuestFilter f = go.AddComponent<SceneQuestFilter>();
                f.Configure("Noch nicht verfügbar.");
                Assert.AreEqual(SceneQuestFilter.ElsewhereReason, QuestRules.CanAccept(ka, 5, done, _ => false, 0), "ohne Kennzeichen (Sandbox): nicht vergeben");
                Assert.IsFalse(QuestRules.Offered(ka, KC.Wenna, 5, done, _ => false), "Wenna bietet K-A ohne Kennzeichen nicht an");
                f.SetTags(KC.SceneTag);
                Assert.IsNull(QuestRules.CanAccept(ka, 5, done, _ => false, 0), "mit Kennzeichen kareth_v4: vergeben");
                Assert.AreEqual("Auftrag läuft bereits.", QuestRules.CanAccept(ka, 5, done, id => id == ka.Id, 0), "laufend bleibt laufend");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Tiere_neutral_je_Gebiet_und_mit_Beute()
        {
            var expected = new Dictionary<string, (bool neutral, string loot)>
            {
                { KC.EnemyRooter, (true, "loot.furrow_rooter") }, { KC.EnemyClacker, (true, "loot.shore_clacker") },
                { KC.EnemySlinger, (false, "loot.silt_slinger") }, { KC.EnemyBellows, (true, "loot.whisper_bellows") },
                { KC.EnemyReedDeserter, (false, "loot.reed_deserter") }, { KC.EnemyWreckSailor, (false, "loot.wreck_sailor") },
            };
            foreach (KeyValuePair<string, (bool neutral, string loot)> kv in expected)
            {
                EnemyDefinition d = Def<EnemyDefinition>(kv.Key);
                Assert.IsNotNull(d, kv.Key + " (erst Auftrag „inhalte_28“)");
                Assert.AreEqual(kv.Value.neutral, d.neutral, kv.Key + ": neutral");
                Assert.AreEqual(kv.Value.neutral, d.ShowsNeutral(AggroProfile.Passive), kv.Key + ": passiv = gelb, wenn neutral");
                Assert.IsFalse(d.ShowsNeutral(AggroProfile.Territorial), kv.Key + ": territorial (Nest, Gebiet) = rot");
                Assert.IsNotNull(d.lootTable, kv.Key + ": Beutetabelle");
                Assert.AreEqual(kv.Value.loot, d.lootTable.Id, kv.Key + ": Beutetabelle");
                Assert.AreEqual(BalanceRole.Normal, d.balanceRole, kv.Key + ": Balance-Rolle");
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Ashveil/Prefabs/Enemies/" + d.name + ".prefab"), kv.Key + ": Prefab");
            }
            EnemyDefinition slinger = Def<EnemyDefinition>(KC.EnemySlinger);
            Assert.IsTrue(slinger.Throws && slinger.throwMinRange >= 5f && slinger.throwRange <= 16f, "Schlickwurf: begrenzte Reichweite, nicht aus nächster Nähe");
            Assert.AreEqual(AggroProfile.Territorial, slinger.aggroProfile, "Schlickwerfer territorial");
            Assert.IsFalse(Def<EnemyDefinition>(KC.EnemyClacker).Throws, "Uferklapper werfen nicht");
            // Wolf und Mähnenreißer bleiben rot, auch wenn sie passiv sind (neutral ist eine Eigenschaft der Art, nicht des Profils)
            EnemyDefinition wolf = Def<EnemyDefinition>(KC.EnemyWolf);
            Assert.IsNotNull(wolf, "Aschwolf");
            Assert.IsFalse(wolf.ShowsNeutral(AggroProfile.Defensive), "Aschwolf (defensiv am Lager) bleibt feindlich");
        }

        [Test]
        public void Belohnungen_nach_Klasse_genau_ein_Teil()
        {
            string[][] sets =
            {
                new[] { "item.k28_studded_girdle", "item.k28_knife_loop_belt", "item.k28_braided_cord" },
                new[] { "item.k28_wagon_plate_cuirass", "item.k28_tarp_leather_jerkin", "item.k28_wagon_canvas_smock" },
                new[] { "item.k28_watch_greaves", "item.k28_scout_breeches", "item.k28_loden_trousers" },
            };
            string[] classes = { "class.warden", "class.ashstalker", "class.emberweaver" };
            foreach (string[] set in sets)
            {
                var seen = new HashSet<string>();
                foreach (string id in set)
                {
                    ItemDefinition d = Def<ItemDefinition>(id);
                    Assert.IsNotNull(d, id);
                    Assert.AreEqual(1, d.allowedClasses.Count, id + ": genau eine Klasse");
                    Assert.IsTrue(seen.Add(d.allowedClasses[0].Id), id + ": jede Klasse einmal");
                }
                CollectionAssert.AreEquivalent(classes, seen.ToArray(), "alle drei Klassen versorgt");
            }
            foreach (string w in new[] { "item.k28_yard_sword", "item.k28_yard_bow", "item.k28_workshop_staff", "item.k28_workshop_dagger" })
            {
                ItemDefinition d = Def<ItemDefinition>(w);
                Assert.IsNotNull(d, w);
                Assert.IsNotNull(d.weapon, w + ": Waffe");
                Assert.Greater(d.weapon.maxDamage, d.weapon.minDamage, w + ": Schaden aus dem Waffenbudget");
            }
            foreach (string q in new[] { KC.ItemJug, KC.ItemLogPage, KC.ItemGifts })
            {
                ItemDefinition d = Def<ItemDefinition>(q);
                Assert.IsNotNull(d, q);
                Assert.AreEqual(ItemCategory.Quest, d.category, q + ": Auftragsgegenstand");
                Assert.IsFalse(string.IsNullOrEmpty(d.questId), q + ": gehört zu einem Auftrag");
            }
        }

        [Test]
        public void Figuren_mit_Texten_und_Gruss_nach_Marke()
        {
            Loc.Reload();
            foreach (string id in new[] { KC.Wenna, KC.Jorn, KC.Ulf, KC.Oswald, KC.Ebba })
            {
                NpcDefinition n = Def<NpcDefinition>(id);
                Assert.IsNotNull(n, id);
                Assert.IsFalse(n.Name.StartsWith("[") || n.Greeting.StartsWith("["), id + ": Name und Gruß aus kareth.txt");
            }
            NpcDefinition oswald = Def<NpcDefinition>(KC.Oswald), ebba = Def<NpcDefinition>(KC.Ebba);
            Assert.AreNotEqual(oswald.GreetingFor(_ => false), oswald.GreetingFor(f => f == KC.FlagJugReturned), "Oswald grüßt nach U-2 anders");
            Assert.AreNotEqual(ebba.GreetingFor(_ => false), ebba.GreetingFor(f => f == KC.FlagJugReturned), "Ebba grüßt nach U-2 anders");
            Assert.AreEqual("Ebba Salweg", ebba.Name);
        }
    }
}
