using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Ashveil.Data;
using Ashveil.EditorTools;
using Ashveil.Gameplay.Items;
using Ashveil.Networking;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// 22.10.2026 (Runde 5): Lade-Bereinigung alter Questgegenstände (LADEN-01, <see cref="CharacterStore.QuestgegenstaendeAufraeumen"/>)
    /// an einem synthetischen Spielstand – ohne Server, ohne echte Spielstände. Dazu die Sicherung des Profilzeigers im Kurztest
    /// (RELEASE-02, <see cref="PlaytestSmoke.VerwaisteSicherungZurueck"/>) in einem Temp-Ordner.
    /// 22.10.2026 (Runde 6): Codex-Prüffälle 1–5 zu LADEN-01 (fehlende Definition erhält, nur feste Altlasten gehen) und die
    /// Prozessprüfung vor jeder Zeigeränderung (Codex-Paket A, <see cref="PlaytestSmoke.ZeigerVorbereiten"/>) mit ersetzter
    /// Prozessliste – es wird kein Spiel gestartet oder beendet.
    /// </summary>
    public sealed class LadeBereinigungTests
    {
        private readonly List<Object> _angelegt = new List<Object>();
        private readonly Dictionary<string, ItemDefinition> _items = new Dictionary<string, ItemDefinition>();
        private readonly Dictionary<string, QuestDefinition> _quests = new Dictionary<string, QuestDefinition>();
        private string _temp, _temp2;

        [SetUp]
        public void SetUp()
        {
            // 22.10.2026 (Runde 6): Zeigertests unabhängig von gerade laufenden Ashveil.exe – Standard „keiner läuft“
            PlaytestSmoke.ProzesseFuerTests = () => new PlaytestSmoke.SpielProzess[0];
        }

        [TearDown]
        public void TearDown()
        {
            PlaytestSmoke.ProzesseFuerTests = null;
            foreach (Object o in _angelegt)
                if (o != null) Object.DestroyImmediate(o);
            _angelegt.Clear();
            _items.Clear();
            _quests.Clear();
            if (_temp != null && Directory.Exists(_temp)) Directory.Delete(_temp, true);
            if (_temp2 != null && Directory.Exists(_temp2)) Directory.Delete(_temp2, true);
            _temp = _temp2 = null;
        }

        // ------------------------------------------------------------------ LADEN-01, synthetische Daten

        private T Neu<T>(string id) where T : GameDefinition
        {
            var d = ScriptableObject.CreateInstance<T>();
            d.name = id;
            var so = new SerializedObject(d);
            so.FindProperty("id").stringValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
            _angelegt.Add(d);
            return d;
        }

        private ItemDefinition Gegenstand(string id, ItemCategory kategorie, string questId)
        {
            ItemDefinition d = Neu<ItemDefinition>(id);
            d.category = kategorie;
            d.questId = questId;
            _items[id] = d;
            return d;
        }

        private QuestDefinition Auftrag(string id, string sammelziel = null, ItemDefinition startGegenstand = null)
        {
            QuestDefinition q = Neu<QuestDefinition>(id);
            if (sammelziel != null) q.objectives.Add(new QuestObjective { type = QuestObjectiveType.Collect, targetId = sammelziel, count = 3 });
            if (startGegenstand != null) q.startItems.Add(new QuestItemReward { item = startGegenstand, count = 1 });
            _quests[id] = q;
            return q;
        }

        private List<string> Aufraeumen(CharacterSaveData d) =>
            CharacterStore.QuestgegenstaendeAufraeumen(d, id => id != null && _items.TryGetValue(id, out ItemDefinition i) ? i : null,
                id => id != null && _quests.TryGetValue(id, out QuestDefinition q) ? q : null, _quests.Values.ToList());

        /// <summary>22.10.2026 (Runde 6): wie <see cref="Aufraeumen(CharacterSaveData)"/>, mit wählbarer Gesamtliste und Ladeweg-Laufliste.</summary>
        private List<string> Aufraeumen(CharacterSaveData d, IReadOnlyList<QuestDefinition> alle, IEnumerable<string> laufendVorBereinigung) =>
            CharacterStore.QuestgegenstaendeAufraeumen(d, id => id != null && _items.TryGetValue(id, out ItemDefinition i) ? i : null,
                id => id != null && _quests.TryGetValue(id, out QuestDefinition q) ? q : null, alle, laufendVorBereinigung);

        private static CharacterSaveData Stand(List<string> erledigt = null, params string[] laufend) => new CharacterSaveData
        {
            profileId = "lade-bereinigung-test",
            bag = new ItemStack[InventoryModel.BagSize],
            completedQuests = erledigt ?? new List<string>(),
            quests = laufend.Select(id => new QuestSave { id = id, progress = new int[1] }).ToList()
        };

        private static string[] Abbild(CharacterSaveData d) => d.bag.Select(s => s.IsEmpty ? "" : s.id + "×" + s.count).ToArray();

        private static int Anzahl(CharacterSaveData d, string id) => d.bag.Where(s => !s.IsEmpty && s.id == id).Sum(s => s.count);

        [Test]
        public void Alter_Spielstand_verliert_nur_Questgegenstaende_ohne_Bedarf()
        {
            // Aufträge: A abgeschlossen, B läuft, C aufgegeben (weder laufend noch erledigt), D offen (noch nicht angenommen)
            Gegenstand("item.t_splitter", ItemCategory.Quest, "quest.t_gibt_es_nicht");   // Fund ohne Auftragsdefinition: bleibt seit Runde 6
            Gegenstand("item.t_a_rest", ItemCategory.Quest, "quest.t_a");                 // Überschuss eines abgegebenen Auftrags
            Gegenstand("item.t_b_beute", ItemCategory.Quest, "quest.t_b");                // laufender Auftrag
            Gegenstand("item.t_c_brief", ItemCategory.Quest, "quest.t_c");                // aufgegebener Auftrag: behalten
            ItemDefinition buch = Gegenstand("item.t_buch", ItemCategory.Quest, "quest.t_a"); // questId erledigt, aber Startgegenstand von D
            Gegenstand("item.t_fell", ItemCategory.Material, "quest.t_a");                // andere Kategorie: nie anfassen
            Gegenstand("item.t_ohne_id", ItemCategory.Quest, "");                         // Questgegenstand ohne questId: nie anfassen
            Auftrag("quest.t_a", "item.t_a_rest");
            Auftrag("quest.t_b", "item.t_b_beute");
            Auftrag("quest.t_c", "item.t_c_brief");
            Auftrag("quest.t_d", null, buch);

            var d = new CharacterSaveData
            {
                profileId = "lade-bereinigung-test",
                version = 11,
                bag = new ItemStack[InventoryModel.BagSize],
                completedQuests = new List<string> { "quest.t_a" },
                quests = new List<QuestSave> { new QuestSave { id = "quest.t_b", progress = new int[1] } }
            };
            d.bag[0] = new ItemStack("item.t_splitter", 5, true);
            d.bag[1] = new ItemStack("item.t_a_rest", 3, true);
            d.bag[2] = new ItemStack("item.t_b_beute", 2, true);
            d.bag[3] = new ItemStack("item.t_c_brief", 1, true);
            d.bag[4] = new ItemStack("item.t_buch", 1, true);
            d.bag[5] = new ItemStack("item.t_fell", 7);
            d.bag[6] = new ItemStack("item.weg_aus_den_daten", 1);   // unbekannte ID: bleibt (wird nur gemeldet)
            d.bag[7] = new ItemStack("item.t_ohne_id", 1, true);
            d.bag[InventoryModel.BackpackSize + 3] = new ItemStack("item.t_splitter", 2, true);   // Zelle in einer Tasche

            List<string> weg = Aufraeumen(d);

            // 22.10.2026 (Runde 6, LADEN-01 nach Codex): eine fehlende Auftragsdefinition belegt keinen nutzlosen Gegenstand mehr –
            // der Fund ohne Definition bleibt (vorher 3 Stapel weg: zweimal Splitter, einmal Rest)
            Assert.AreEqual(1, weg.Count, "entfernte Stapel: " + string.Join(", ", weg));
            Assert.AreEqual(7, Anzahl(d, "item.t_splitter"), "Fund ohne Auftragsdefinition bleibt (Rucksack und Tasche)");
            Assert.AreEqual(0, Anzahl(d, "item.t_a_rest"), "Rest eines abgeschlossenen Auftrags");
            Assert.AreEqual(2, Anzahl(d, "item.t_b_beute"), "laufender Auftrag behält seine Gegenstände");
            Assert.AreEqual(1, Anzahl(d, "item.t_c_brief"), "aufgegebener Auftrag behält seine Gegenstände (Max: „beim Aufgeben behalten“)");
            Assert.AreEqual(1, Anzahl(d, "item.t_buch"), "Startgegenstand eines offenen Auftrags bleibt, auch wenn seine questId erledigt ist");
            Assert.AreEqual(7, Anzahl(d, "item.t_fell"), "Material bleibt");
            Assert.AreEqual(1, Anzahl(d, "item.weg_aus_den_daten"), "unbekannte ID bleibt");
            Assert.AreEqual(1, Anzahl(d, "item.t_ohne_id"), "Questgegenstand ohne questId bleibt");
            StringAssert.Contains("Auftrag quest.t_a abgeschlossen", weg[0]);

            Assert.AreEqual(0, Aufraeumen(d).Count, "idempotent: das zweite Laden entfernt nichts mehr (und meldet nichts)");
        }

        // ------------------------------------------------------------------ 22.10.2026 (Runde 6): Codex-Prüffälle 1–5 zu LADEN-01

        [Test]
        public void Codex1_Fehlende_Auftragsdefinition_ohne_laufenden_Auftrag_erhaelt_den_Gegenstand()
        {
            Gegenstand("item.t_fund", ItemCategory.Quest, "quest.t_fehlt");
            Gegenstand("item.t_fund_erledigt", ItemCategory.Quest, "quest.t_fehlt_erledigt");
            Auftrag("quest.t_sonst");   // Gesamtliste vorhanden, nennt die Gegenstände aber nicht
            CharacterSaveData d = Stand(new List<string> { "quest.t_fehlt_erledigt" });
            d.bag[0] = new ItemStack("item.t_fund", 2, true);
            d.bag[1] = new ItemStack("item.t_fund_erledigt", 1, true);
            List<string> weg = Aufraeumen(d);
            Assert.AreEqual(0, weg.Count, "entfernt: " + string.Join(", ", weg));
            Assert.AreEqual(2, Anzahl(d, "item.t_fund"), "Definition fehlt, kein laufender Auftrag: bleibt");
            Assert.AreEqual(1, Anzahl(d, "item.t_fund_erledigt"), "Definition fehlt, als erledigt geführt: bleibt trotzdem");
        }

        [Test]
        public void Codex2_Laufender_Auftrag_ohne_Definition_erhaelt_seine_Gegenstaende()
        {
            Gegenstand("item.t_fund", ItemCategory.Quest, "quest.t_fehlt");
            Gegenstand("item.bell_shard", ItemCategory.Quest, "quest.bell_shepherd");   // sogar eine feste Altlast bleibt, wenn ihr Auftrag läuft
            Gegenstand("item.t_a_rest", ItemCategory.Quest, "quest.t_a");
            Auftrag("quest.t_a", "item.t_a_rest");
            // a) Auftrag steht noch in d.quests (auch als erledigt geführt)
            CharacterSaveData d = Stand(new List<string> { "quest.t_a", "quest.t_fehlt" }, "quest.t_fehlt", "quest.bell_shepherd");
            d.bag[0] = new ItemStack("item.t_fund", 2, true);
            d.bag[1] = new ItemStack("item.bell_shard", 3, true);
            d.bag[2] = new ItemStack("item.t_a_rest", 1, true);
            Assert.AreEqual(0, Aufraeumen(d).Count, "laufende Aufträge ohne Definition: nichts entfernen");
            Assert.AreEqual(2, Anzahl(d, "item.t_fund"));
            Assert.AreEqual(3, Anzahl(d, "item.bell_shard"));
            Assert.AreEqual(1, Anzahl(d, "item.t_a_rest"), "Bedarf des laufenden Auftrags ohne Definition ist unbekannt – auch der Rest von t_a bleibt");
            // b) wie im Ladeweg: Sanitize hat den unbekannten Auftrag schon aus d.quests verworfen, die Laufliste davor schützt weiter
            CharacterSaveData e = Stand(new List<string> { "quest.t_a" });
            e.bag[0] = new ItemStack("item.t_fund", 2, true);
            e.bag[1] = new ItemStack("item.bell_shard", 3, true);
            Assert.AreEqual(0, Aufraeumen(e, _quests.Values.ToList(), new[] { "quest.bell_shepherd" }).Count);
            Assert.AreEqual(3, Anzahl(e, "item.bell_shard"), "Altlast mit laufendem Auftrag (nur in der Laufliste vor der Bereinigung): bleibt");
        }

        [Test]
        public void Codex3_Ohne_Gesamtliste_der_Auftraege_bleibt_alles()
        {
            Gegenstand("item.t_a_rest", ItemCategory.Quest, "quest.t_a");
            Gegenstand("item.faded_letter", ItemCategory.Quest, "quest.faded_letter");
            Auftrag("quest.t_a", "item.t_a_rest");
            CharacterSaveData d = Stand(new List<string> { "quest.t_a" });
            d.bag[0] = new ItemStack("item.t_a_rest", 3, true);
            d.bag[1] = new ItemStack("item.faded_letter", 1, true);
            Assert.AreEqual(0, Aufraeumen(d, null, null).Count, "alleAuftraege == null: Folgebedarf nicht belegbar");
            Assert.AreEqual(3, Anzahl(d, "item.t_a_rest"));
            Assert.AreEqual(1, Anzahl(d, "item.faded_letter"));
        }

        [Test]
        public void Codex4_Feste_Altlasten_gehen_unbekannte_Questgegenstaende_bleiben()
        {
            Gegenstand("item.bell_shard", ItemCategory.Quest, "quest.bell_shepherd");
            Gegenstand("item.faded_letter", ItemCategory.Quest, "quest.faded_letter");
            Gegenstand("item.t_fund", ItemCategory.Quest, "quest.t_fehlt");
            Auftrag("quest.t_sonst");
            CharacterSaveData d = Stand();
            d.bag[0] = new ItemStack("item.bell_shard", 5, true);
            d.bag[1] = new ItemStack("item.faded_letter", 1, true);
            d.bag[2] = new ItemStack("item.t_fund", 2, true);
            d.bag[InventoryModel.BackpackSize + 1] = new ItemStack("item.bell_shard", 2, true);   // auch in einer Tasche
            List<string> weg = Aufraeumen(d);
            Assert.AreEqual(3, weg.Count, "entfernt: " + string.Join(", ", weg));
            Assert.AreEqual(0, Anzahl(d, "item.bell_shard"), "Glockensplitter (Altlast)");
            Assert.AreEqual(0, Anzahl(d, "item.faded_letter"), "Verblichener Brief (Altlast)");
            Assert.AreEqual(2, Anzahl(d, "item.t_fund"), "anderer Questgegenstand ohne Definition bleibt");
            StringAssert.Contains("Altlast ohne Auftrag quest.bell_shepherd", weg[0]);
            // Sammelt ein offener Auftrag die Altlast, bleibt sie
            Auftrag("quest.t_glocke", "item.bell_shard");
            CharacterSaveData e = Stand();
            e.bag[0] = new ItemStack("item.bell_shard", 4, true);
            Assert.AreEqual(0, Aufraeumen(e).Count);
            Assert.AreEqual(4, Anzahl(e, "item.bell_shard"), "Altlast, die ein offener Auftrag sammelt, bleibt");
        }

        [Test]
        public void Codex5_Zollbuch_fuer_offenen_Folgeauftrag_bleibt_und_Wiederholung_aendert_nichts()
        {
            ItemDefinition buch = Gegenstand("item.t_zollbuch", ItemCategory.Quest, "quest.t_w19");
            Gegenstand("item.t_w19_rest", ItemCategory.Quest, "quest.t_w19");
            Auftrag("quest.t_w19", "item.t_w19_rest");
            Auftrag("quest.t_w20", null, buch);   // offen, Startgegenstand = Zollbuch
            CharacterSaveData d = Stand(new List<string> { "quest.t_w19" });
            d.bag[0] = new ItemStack("item.t_zollbuch", 1, true);
            d.bag[1] = new ItemStack("item.t_w19_rest", 2, true);
            Assert.AreEqual(1, Aufraeumen(d).Count, "nur der Rest von w19");
            Assert.AreEqual(1, Anzahl(d, "item.t_zollbuch"), "Zollbuch bleibt: Startgegenstand des offenen w20");
            Assert.AreEqual(0, Anzahl(d, "item.t_w19_rest"));
            string[] vorher = Abbild(d);
            Assert.AreEqual(0, Aufraeumen(d).Count, "zweite Bereinigung entfernt nichts");
            CollectionAssert.AreEqual(vorher, Abbild(d), "zweite Bereinigung ändert den Rucksack nicht");
        }

        [Test]
        public void Laufender_Auftrag_der_auch_als_erledigt_gefuehrt_wird_behaelt_seine_Gegenstaende()
        {
            Gegenstand("item.t_b_beute", ItemCategory.Quest, "quest.t_b");
            Auftrag("quest.t_b", "item.t_b_beute");
            var d = new CharacterSaveData
            {
                profileId = "lade-bereinigung-test",
                bag = new ItemStack[InventoryModel.BagSize],
                completedQuests = new List<string> { "quest.t_b" },
                quests = new List<QuestSave> { new QuestSave { id = "quest.t_b", progress = new int[1] } }
            };
            d.bag[0] = new ItemStack("item.t_b_beute", 2, true);
            Assert.AreEqual(0, Aufraeumen(d).Count);
            Assert.AreEqual(2, Anzahl(d, "item.t_b_beute"));
        }

        // ------------------------------------------------------------------ LADEN-01, echte Daten über den Ladeweg (Sanitize)

        private const string DatenbankPfad = "Assets/_Ashveil/Data/DefinitionDatabase.asset";

        [Test]
        public void Echter_Ladeweg_raeumt_alte_Fundstuecke_und_Abgegebenes_auf()
        {
            var db = AssetDatabase.LoadAssetAtPath<DefinitionDatabase>(DatenbankPfad);
            if (db == null) Assert.Inconclusive("Definitionsdatenbank fehlt: " + DatenbankPfad);
            string[] noetig =
            {
                "item.bell_shard", "item.faded_letter", "item.gr_venom_gland", "item.ww_zollbuch", "item.ww_hufeisen", "item.healing_draught"
            };
            foreach (string id in noetig)
                if (!db.TryGet(id, out ItemDefinition _)) Assert.Inconclusive("Gegenstand fehlt in den Daten: " + id);
            foreach (string id in new[] { "quest.glockenrast.gegengift", "quest.westweiden.w19_zollbuch", "quest.westweiden.w20_alter_mann_liest",
                                          "quest.westweiden.w04_bezahlt_in_fell", "quest.westweiden.w05_zaumzeug" })
                if (!db.TryGet(id, out QuestDefinition _)) Assert.Inconclusive("Auftrag fehlt in den Daten: " + id);

            FieldInfo dbFeld = typeof(GameData).GetField("_db", BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo questFeld = typeof(GameData).GetField("_quests", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(dbFeld, "GameData._db");
            object vorher = dbFeld.GetValue(null);
            try
            {
                GameData.Register(db);
                // Alter Stand (v8, wie vor dem 21.10.): Funde ohne Auftrag, Überschuss eines abgegebenen Auftrags, dazu Fälle, die bleiben
                var d = new CharacterSaveData
                {
                    version = 8,
                    profileId = "lade-bereinigung-test",
                    name = "Maxim",
                    bag = new ItemStack[20],
                    completedQuests = new List<string> { "quest.glockenrast.gegengift", "quest.westweiden.w19_zollbuch", "quest.westweiden.w04_bezahlt_in_fell" },
                    quests = null,
                    professions = null,
                    knownRecipes = null,
                    storyFlags = null,
                    discoveredAreas = null
                };
                d.bag[0] = new ItemStack("item.bell_shard", 5, true);
                d.bag[1] = new ItemStack("item.faded_letter", 1, true);
                d.bag[2] = new ItemStack("item.gr_venom_gland", 3, true);   // Gegengift abgegeben: Überschuss
                d.bag[3] = new ItemStack("item.ww_zollbuch", 1, true);      // w19 erledigt, aber Startgegenstand von w20 (offen)
                d.bag[4] = new ItemStack("item.ww_hufeisen", 1, true);      // Belohnung von w04, gehört zu w05 (offen)
                d.bag[5] = new ItemStack("item.healing_draught", 2);

                CharacterStore.MigrateForTests(d);

                Assert.AreEqual(InventoryModel.BagSize, d.bag.Length, "alter Rucksack wurde vergrößert (Laden wie bisher)");
                Assert.AreEqual(0, Anzahl(d, "item.bell_shard"), "Glockensplitter ohne Auftrag");
                Assert.AreEqual(0, Anzahl(d, "item.faded_letter"), "Verblichener Brief ohne Auftrag");
                Assert.AreEqual(0, Anzahl(d, "item.gr_venom_gland"), "Überschuss des abgegebenen Gegengifts");
                Assert.AreEqual(1, Anzahl(d, "item.ww_zollbuch"), "Zollbuch bleibt: Startgegenstand eines offenen Auftrags");
                Assert.AreEqual(1, Anzahl(d, "item.ww_hufeisen"), "Hufeisen bleibt: sein Auftrag (w05) ist offen");
                Assert.AreEqual(2, Anzahl(d, "item.healing_draught"), "Verbrauchsgut bleibt");
                Assert.AreEqual(CharacterSaveData.CurrentVersion, d.version);
                // 22.10.2026 (Runde 6, Codex-Fall 5): wiederholte Migration ändert nichts mehr
                string[] ersterStand = Abbild(d);
                CharacterStore.MigrateForTests(d);
                CollectionAssert.AreEqual(ersterStand, Abbild(d), "zweites Laden ändert den Rucksack nicht");
            }
            finally
            {
                dbFeld.SetValue(null, vorher);
                questFeld?.SetValue(null, null);
            }
        }

        // ------------------------------------------------------------------ RELEASE-02: Sicherung des Profilzeigers

        private string Ordner()
        {
            _temp = Path.Combine(Path.GetTempPath(), "ashveil_zeiger_test_" + Guid.NewGuid().ToString("N").Substring(0, 10));
            Directory.CreateDirectory(Path.Combine(_temp, "Saves", "Characters"));
            return _temp;
        }

        [Test]
        public void Verwaiste_Sicherung_wird_zurueckgelegt_wenn_der_Zeiger_unbenutzt_ist()
        {
            string o = Ordner();
            File.WriteAllText(Path.Combine(o, "ashveil_profile.txt.vor_smoke"), "spieler-max");
            File.WriteAllText(Path.Combine(o, "ashveil_profile.txt"), "spieler-zufall");   // Lauf 1: ohne Spielstand
            string text = PlaytestSmoke.VerwaisteSicherungZurueck(o, out bool blockiert, false);
            Assert.IsFalse(blockiert, text);
            Assert.AreEqual("spieler-max", File.ReadAllText(Path.Combine(o, "ashveil_profile.txt")).Trim());
            Assert.IsFalse(File.Exists(Path.Combine(o, "ashveil_profile.txt.vor_smoke")));
        }

        [Test]
        public void Verwaiste_Sicherung_ohne_Zeiger_wird_zurueckgelegt()
        {
            string o = Ordner();
            File.WriteAllText(Path.Combine(o, "ashveil_profile.txt.vor_smoke"), "spieler-max");
            PlaytestSmoke.VerwaisteSicherungZurueck(o, out bool blockiert, false);
            Assert.IsFalse(blockiert);
            Assert.AreEqual("spieler-max", File.ReadAllText(Path.Combine(o, "ashveil_profile.txt")).Trim());
        }

        [Test]
        public void Benutzter_neuer_Zeiger_und_Sicherung_bleiben_beide_erhalten()
        {
            string o = Ordner();
            File.WriteAllText(Path.Combine(o, "ashveil_profile.txt.vor_smoke"), "spieler-max");
            File.WriteAllText(Path.Combine(o, "ashveil_profile.txt"), "spieler-neu");
            File.WriteAllText(Path.Combine(o, "Saves", "Characters", "spieler-neu.json"), "{}");   // inzwischen eine Figur angelegt
            string text = PlaytestSmoke.VerwaisteSicherungZurueck(o, out bool blockiert, false);
            Assert.IsFalse(blockiert, text);
            Assert.AreEqual("spieler-neu", File.ReadAllText(Path.Combine(o, "ashveil_profile.txt")).Trim(), "aktueller Zeiger unverändert");
            string[] umbenannt = Directory.GetFiles(o, "ashveil_profile.txt.vor_smoke_*");
            Assert.AreEqual(1, umbenannt.Length, "Sicherung mit Zeitstempel behalten");
            Assert.AreEqual("spieler-max", File.ReadAllText(umbenannt[0]).Trim());
            Assert.IsFalse(File.Exists(Path.Combine(o, "ashveil_profile.txt.vor_smoke")), "der nächste Lauf kann seine eigene Sicherung anlegen, ohne zu überschreiben");
            StringAssert.Contains("BEIDE behalten", text);
        }

        [Test]
        public void Ohne_Sicherung_geschieht_nichts()
        {
            string o = Ordner();
            File.WriteAllText(Path.Combine(o, "ashveil_profile.txt"), "spieler-max");
            Assert.IsNull(PlaytestSmoke.VerwaisteSicherungZurueck(o, out bool blockiert, false));
            Assert.IsFalse(blockiert);
            Assert.AreEqual("spieler-max", File.ReadAllText(Path.Combine(o, "ashveil_profile.txt")).Trim());
        }

        // ------------------------------------------------------------------ 22.10.2026 (Runde 6, Codex-Paket A): Prozessprüfung

        private static string Exe(string ordner) => Path.Combine(ordner, "Ashveil.exe");

        private static void Laufend(params PlaytestSmoke.SpielProzess[] prozesse) => PlaytestSmoke.ProzesseFuerTests = () => prozesse;

        private static string Lies(string pfad) => File.Exists(pfad) ? File.ReadAllText(pfad).Trim() : null;

        [Test]
        public void Codex_A1_Start_ohne_Sicherung_mit_laufendem_Spiel_aus_dem_Ordner_wird_abgelehnt()
        {
            string o = Ordner();
            File.WriteAllText(Path.Combine(o, "ashveil_profile.txt"), "spieler-max");
            Laufend(new PlaytestSmoke.SpielProzess(4242, Exe(o)));
            string grund = PlaytestSmoke.ZeigerVorbereiten(o, out string wiederher, false);
            Assert.IsNotNull(grund, "Start muss abgelehnt werden");
            StringAssert.Contains("PID 4242", grund);
            Assert.IsNull(wiederher);
            Assert.AreEqual("spieler-max", Lies(Path.Combine(o, "ashveil_profile.txt")), "Zeiger unverändert");
            Assert.IsFalse(File.Exists(Path.Combine(o, "ashveil_profile.txt.vor_smoke")), "keine Sicherung angelegt");
        }

        [Test]
        public void Codex_A2_Sicherung_und_laufendes_Spiel_lassen_beide_Dateien_unveraendert()
        {
            string o = Ordner();
            File.WriteAllText(Path.Combine(o, "ashveil_profile.txt.vor_smoke"), "spieler-max");
            File.WriteAllText(Path.Combine(o, "ashveil_profile.txt"), "spieler-zufall");
            Laufend(new PlaytestSmoke.SpielProzess(4243, Exe(o)));
            Assert.IsNotNull(PlaytestSmoke.ZeigerVorbereiten(o, out _, false));
            string text = PlaytestSmoke.VerwaisteSicherungZurueck(o, out bool blockiert, false);
            Assert.IsTrue(blockiert, text);
            Assert.AreEqual("spieler-zufall", Lies(Path.Combine(o, "ashveil_profile.txt")), "Zeiger unverändert");
            Assert.AreEqual("spieler-max", Lies(Path.Combine(o, "ashveil_profile.txt.vor_smoke")), "Sicherung unverändert");
            // Wiederholung später: Spiel beendet → jetzt zurücklegen
            Laufend(new PlaytestSmoke.SpielProzess(4243, Exe(o), beendet: true));
            PlaytestSmoke.VerwaisteSicherungZurueck(o, out bool spaeter, false);
            Assert.IsFalse(spaeter);
            Assert.AreEqual("spieler-max", Lies(Path.Combine(o, "ashveil_profile.txt")), "nach dem Ende zurückgelegt");
        }

        [Test]
        public void Codex_A3_Unlesbarer_Pfad_eines_laufenden_Spiels_blockiert()
        {
            string o = Ordner();
            File.WriteAllText(Path.Combine(o, "ashveil_profile.txt"), "spieler-max");
            Laufend(new PlaytestSmoke.SpielProzess(4244, null, false, "Win32Exception: Zugriff verweigert"));
            Assert.AreEqual(PlaytestSmoke.SpielZustand.NichtFeststellbar, PlaytestSmoke.SpielPruefen(o, null, out int pid, out string grund));
            Assert.AreEqual(4244, pid);
            StringAssert.Contains("Zugriff verweigert", grund);
            string abgelehnt = PlaytestSmoke.ZeigerVorbereiten(o, out _, false);
            Assert.IsNotNull(abgelehnt);
            StringAssert.Contains("nicht feststellbar", abgelehnt);
            Assert.AreEqual("spieler-max", Lies(Path.Combine(o, "ashveil_profile.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(o, "ashveil_profile.txt.vor_smoke")));
            // mit liegender Sicherung: ebenfalls blockiert, nichts angefasst
            File.WriteAllText(Path.Combine(o, "ashveil_profile.txt.vor_smoke"), "spieler-alt");
            PlaytestSmoke.VerwaisteSicherungZurueck(o, out bool blockiert, false);
            Assert.IsTrue(blockiert);
            Assert.AreEqual("spieler-max", Lies(Path.Combine(o, "ashveil_profile.txt")));
            Assert.AreEqual("spieler-alt", Lies(Path.Combine(o, "ashveil_profile.txt.vor_smoke")));
            // Prozessliste selbst nicht lesbar: ebenfalls blockiert
            PlaytestSmoke.ProzesseFuerTests = () => throw new InvalidOperationException("Prozessliste gesperrt");
            Assert.AreEqual(PlaytestSmoke.SpielZustand.NichtFeststellbar, PlaytestSmoke.SpielPruefen(o, null, out _, out _));
        }

        [Test]
        public void Codex_A3b_Beendeter_Prozess_mit_unlesbarem_Pfad_blockiert_nicht()
        {
            string o = Ordner();
            File.WriteAllText(Path.Combine(o, "ashveil_profile.txt"), "spieler-max");
            Laufend(new PlaytestSmoke.SpielProzess(4245, null, beendet: true));
            Assert.IsNull(PlaytestSmoke.ZeigerVorbereiten(o, out _, false));
            Assert.AreEqual("spieler-max", Lies(Path.Combine(o, "ashveil_profile.txt.vor_smoke")), "Zeiger gesichert");
        }

        [Test]
        public void Codex_A4_Spiel_aus_anderem_Buildordner_zaehlt_nicht()
        {
            string o = Ordner();
            _temp2 = o + "_alt";   // gleicher Namensanfang, anderer Ordner
            Directory.CreateDirectory(_temp2);
            File.WriteAllText(Path.Combine(o, "ashveil_profile.txt"), "spieler-max");
            Laufend(new PlaytestSmoke.SpielProzess(4246, Exe(_temp2)), new PlaytestSmoke.SpielProzess(4247, @"C:\Spiele\Ashveil\Ashveil.exe"));
            Assert.AreEqual(PlaytestSmoke.SpielZustand.SicherKeiner, PlaytestSmoke.SpielPruefen(o, null, out _, out _));
            Assert.IsNull(PlaytestSmoke.ZeigerVorbereiten(o, out _, false));
            Assert.AreEqual("spieler-max", Lies(Path.Combine(o, "ashveil_profile.txt.vor_smoke")), "Zeiger gesichert");
            Assert.AreEqual(PlaytestSmoke.SpielZustand.Laeuft, PlaytestSmoke.SpielPruefen(_temp2, null, out int pid, out _), "im anderen Ordner läuft es");
            Assert.AreEqual(4246, pid);
        }

        [Test]
        public void Codex_A5_Eigene_Kurztest_Laeufe_sind_ausgenommen()
        {
            string o = Ordner();
            var prozesse = new[] { new PlaytestSmoke.SpielProzess(4248, Exe(o)) };
            Assert.AreEqual(PlaytestSmoke.SpielZustand.SicherKeiner,
                PlaytestSmoke.SpielZustandBestimmen(o, prozesse, new HashSet<int> { 4248 }, out _, out _), "eigener (z. B. gerade hart beendeter) Lauf");
            Assert.AreEqual(PlaytestSmoke.SpielZustand.Laeuft, PlaytestSmoke.SpielZustandBestimmen(o, prozesse, null, out _, out _));
        }

        [Test]
        public void Codex_A6_Echte_Prozessliste_meldet_fuer_frischen_Temp_Ordner_kein_laufendes_Spiel()
        {
            string o = Ordner();
            PlaytestSmoke.ProzesseFuerTests = null;   // echte Liste: aus einem frischen Temp-Ordner kann kein Spiel laufen
            PlaytestSmoke.SpielZustand z = PlaytestSmoke.SpielPruefen(o, null, out int pid, out string grund);
            Assert.AreNotEqual(PlaytestSmoke.SpielZustand.Laeuft, z, $"PID {pid}");
            if (z == PlaytestSmoke.SpielZustand.NichtFeststellbar)
                Assert.Inconclusive($"Ein laufendes Ashveil.exe ist nicht zuzuordnen (PID {pid}: {grund}) – der Schutz blockiert hier zu Recht.");
        }
    }
}
