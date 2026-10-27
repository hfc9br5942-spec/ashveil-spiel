using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Ashveil.Core;
using Ashveil.Data;
using Ashveil.Gameplay.Abilities;
using Ashveil.Gameplay.Colosseum;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Quests;
using Ashveil.Gameplay.Story;
using Ashveil.UI;
using Ashveil.World;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.AI;
using KC = Ashveil.Data.KarethCatalog;
using WC = Ashveil.Data.WestweidenCatalog;

namespace Ashveil.Networking
{
    /// <summary>
    /// 22.10.2026 (Spielablauf Runde 6, Prüfschritt) – Teil 9/10 der Suite spielablauf3 im Host-Spiel (Kareth v4 mit der Westweiden-Zelle).
    /// Jede Prüfung nennt mit „| alt: …“, woran sie mit dem Verhalten vor Runde 6 gescheitert wäre.
    ///   9a Daten     LADEN-01 (Lade-Bereinigung mit echtem GameData: fehlende Definition, laufender Auftrag ohne Definition, ohne
    ///                Auftragsliste → nichts entfernt; Normalfall), WESTWEIDEN-02 (Gerlach handelt), WESTWEIDEN-03/FORTSCHRITT-08
    ///                (Westweiden-Beute ab Stufe 9, 500 Würfe je Klasse), INSTANZEN-04 (Ring-Anzeige überlebt „reset“ im selben Prozess).
    ///   9b-9f Spiel  FORTSCHRITT-06 (Rollender Donner verstärkt Donnerschlag), FORTSCHRITT-07 (Mana-Anteil beim Umrüsten), STORY-05
    ///                (Tod bricht Dominieren ab), STORY-01/-02 (Pferdegeschichte wieder anbietbar, unterbrochene Schluss-Szene),
    ///                STORY-04 (einzigartiges Fundstück nach Aufgeben).
    ///   9g Westweiden WELTZELLEN-03 (sichere Orte, echter Menüweg), WESTWEIDEN-02 (Kauf bei Gerlach), WELTZELLEN-01 (Meer hält die
    ///                Zelle), WELTZELLEN-02 (Gerufene der Alarmglocke gehen mit der Zelle).
    ///   10 Netz      CODEX-A2 (NetworkBootstrap: Host+Trennen bzw. Host+Verbinden im selben Bild) – baut den Editor-Host ab und wieder auf,
    ///                deshalb ganz am Ende.
    /// Kein Spielcode geändert, keine neuen RPCs; nur Testprofil „duo-spielablauf3“ und synthetische Spielstände im Speicher.
    /// 23.10.2026 (Runde-6-Reste): LADEN-01 (a) trennscharf (w20 ebenfalls erledigt, Gegenprobe a'); WELTZELLEN-02 trifft jetzt
    /// EnemyDirector.OnCellUnloading (Glocke angehalten, Gerufene per Referenz + Id gemerkt, kein „glocke != null“ nach dem Entladen);
    /// STORY-05 prüft, dass der Abbruch die Betäubung beendet (EnemyBrain.ServerStunEnde); CODEX-A2 (3) erfolgloses Verbinden → Host
    /// → eigener Client lokal (NetworkBootstrap.LokaleAdresse) – das Wiederherstellen setzt die Adresse nicht mehr von Hand.
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        private const string Sp6Zorn = "talent.warden_righteous_wrath", Sp6Donner = "talent.warden_rolling_thunder";
        private const string Sp6Hose = "item.k28_loden_trousers";
        private const string Sp6Halten = "Gefallen – die Suite hält die Figur liegen.";

        private IEnumerator Sp3Runde6(NetworkInventory a, NetworkProgression pa, Health hp)
        {
            Line("");
            Line("[9 Runde 6 (22.10.2026): Lade-Bereinigung, Westweiden, Talente, Mana, Dominieren, Pferdegeschichte, Fundstück, Weltzellen]");
            Sp6Daten();
            yield return Sp6Talentschaden(a, pa);
            yield return Sp6Mana(a, pa);
            yield return Sp6DominierenImTod(a, pa, hp);
            yield return Sp6Pferdegeschichte(a, pa);
            yield return Sp6Fundstueck(a, pa);
            yield return Sp6Westweiden(a, pa);
            yield return Sp6NetzStart();   // zuletzt: baut den Host ab und wieder auf
        }

        // ================================================================== 9a Daten

        private void Sp6Daten()
        {
            Line("");
            Line("[9a Daten: LADEN-01, Westweiden-Händler und -Beute, Ring-Anzeige]");

            // ---------------------------------------------------------- LADEN-01
            Sp3Punkt("LADEN-01", "Lade-Bereinigung (CharacterStore.QuestgegenstaendeAufraeumen) mit echtem GameData, synthetische Stände (keine Spielstände)");
            const string w19 = "quest.westweiden.w19_zollbuch";
            // 23.10.2026 (Runde-6-Reste a): Fall (a) trennscharf gemacht. Vorher (Zollbuch, nur w19 erledigt und verborgen) hielt auch
            // der Runde-5-Code das Buch – der offene w20 nennt es als Startgegenstand (Schritt 4 „noch gebraucht“), die neue Regel
            // „fehlende Definition → bleibt“ wurde gar nicht gebraucht. Jetzt sind w19 UND w20 erledigt, w19 fehlt in Lookup und
            // Gesamtliste: Nur noch diese Regel hält das Buch. Die Gegenprobe (a') zeigt im selben Lauf, dass sonst nichts es hält.
            Func<string, QuestDefinition> ohneW19 = id => id == w19 ? null : GameData.Quest(id);
            List<QuestDefinition> listeOhneW19 = GameData.Quests.Where(q => q != null && q.Id != w19).ToList();
            CharacterSaveData da = Sp6Stand(new[] { ("item.ww_zollbuch", 1) }, new[] { w19, WC.W20 }, null);
            List<string> wa = CharacterStore.QuestgegenstaendeAufraeumen(da, GameData.Item, ohneW19, listeOhneW19);
            Check(wa.Count == 0 && Sp6Anzahl(da, "item.ww_zollbuch") == 1,
                $"(a) Zollbuch (questId w19), w19 und w20 erledigt, Definition von w19 fehlt (Lookup und Gesamtliste ohne w19): {wa.Count} Stapel entfernt{Sp6Liste(wa)} " +
                "| alt: als „ohne Auftrag quest.westweiden.w19_zollbuch“ entfernt");
            CharacterSaveData da2 = Sp6Stand(new[] { ("item.ww_zollbuch", 1) }, new[] { w19, WC.W20 }, null);
            List<string> wa2 = CharacterStore.QuestgegenstaendeAufraeumen(da2, GameData.Item, GameData.Quest, GameData.Quests);
            Check(wa2.Count == 1 && Sp6Anzahl(da2, "item.ww_zollbuch") == 0,
                $"(a') Gegenprobe zu (a) mit vorhandener Definition: {wa2.Count} Stapel entfernt{Sp6Liste(wa2)} (Soll 1 – kein offener Auftrag braucht das Buch; " +
                "also hält in (a) allein die fehlende Definition es fest, der alte Code hätte es entfernt)");
            CharacterSaveData db = Sp6Stand(new[] { ("item.bell_shard", 5) }, null, new[] { "quest.bell_shepherd" });
            List<string> wb = CharacterStore.QuestgegenstaendeAufraeumen(db, GameData.Item, GameData.Quest, GameData.Quests);
            Check(wb.Count == 0 && Sp6Anzahl(db, "item.bell_shard") == 5,
                $"(b) Laufender Auftrag ohne Definition (quest.bell_shepherd) mit Glockensplittern: {wb.Count} Stapel entfernt{Sp6Liste(wb)} | alt: Splitter als Altlast entfernt");
            CharacterSaveData dc = Sp6Stand(new[] { ("item.gr_venom_gland", 3) }, new[] { "quest.glockenrast.gegengift" }, null);
            List<string> wc = CharacterStore.QuestgegenstaendeAufraeumen(dc, GameData.Item, GameData.Quest, null);
            Check(wc.Count == 0 && Sp6Anzahl(dc, "item.gr_venom_gland") == 3,
                $"(c) Ohne Gesamtliste der Aufträge (null), Gegengift erledigt: {wc.Count} Stapel entfernt{Sp6Liste(wc)} | alt: Giftdrüsen entfernt, obwohl der Bedarf nicht belegbar war");
            // (d) Normalfall über den echten Ladeweg (wie LadeBereinigungTests.Echter_Ladeweg, hier mit dem GameData des laufenden Spiels)
            var dd = new CharacterSaveData
            {
                version = 8,
                profileId = "sp6-lade-bereinigung",
                name = "Maxim",
                bag = new ItemStack[20],
                completedQuests = new List<string> { "quest.glockenrast.gegengift", w19, "quest.westweiden.w04_bezahlt_in_fell" },
                quests = null,
                professions = null,
                knownRecipes = null,
                storyFlags = null,
                discoveredAreas = null
            };
            dd.bag[0] = new ItemStack("item.bell_shard", 5, true);
            dd.bag[1] = new ItemStack("item.faded_letter", 1, true);
            dd.bag[2] = new ItemStack("item.gr_venom_gland", 3, true);
            dd.bag[3] = new ItemStack("item.ww_zollbuch", 1, true);
            dd.bag[4] = new ItemStack("item.ww_hufeisen", 1, true);
            dd.bag[5] = new ItemStack("item.healing_draught", 2);
            string fehlerD = "";
            try { CharacterStore.MigrateForTests(dd); }
            catch (Exception e) { fehlerD = e.GetType().Name + ": " + e.Message; }
            string ersterStand = Sp6Abbild(dd);
            try { CharacterStore.MigrateForTests(dd); }
            catch (Exception e) { fehlerD += " / " + e.GetType().Name + ": " + e.Message; }
            Check(fehlerD == "" && Sp6Anzahl(dd, "item.bell_shard") == 0 && Sp6Anzahl(dd, "item.faded_letter") == 0 && Sp6Anzahl(dd, "item.gr_venom_gland") == 0 &&
                  Sp6Anzahl(dd, "item.ww_zollbuch") == 1 && Sp6Anzahl(dd, "item.ww_hufeisen") == 1 && Sp6Anzahl(dd, "item.healing_draught") == 2,
                $"(d) Normalfall (Ladeweg MigrateForTests): Splitter, Brief, Giftdrüsen weg; Zollbuch (Start von w20), Hufeisen (w05 offen), Heiltränke bleiben – Rucksack {ersterStand}{(fehlerD != "" ? " – " + fehlerD : "")}");
            Check(Sp6Abbild(dd) == ersterStand, "(d) Zweites Laden ändert den Rucksack nicht mehr (idempotent)");

            // ---------------------------------------------------------- WESTWEIDEN-02 (Daten)
            Sp3Punkt("WESTWEIDEN-02", "Daten: Gerlach (Langer Rain, „Händler“) handelt – Heiltrank, Starker Heiltrank, Trockenbrot, Aschentee; ohne Schwarzpulver; Regelpreis");
            NpcDefinition g = GameData.Npc(WC.Gerlach);
            string angebot = g?.stock == null ? "–" : string.Join(", ", g.stock.Where(e => e?.item != null).Select(e => $"{e.item.DisplayName} ×{e.count} {(e.price > 0 ? e.price + "k" : "Regel " + Progression.BuyPrice(e.item, e.price, e.count) + "k")}"));
            Check(g != null && g.vendor && Sp3Fuehrt(g, "item.healing_draught") && Sp3Fuehrt(g, "item.strong_healing_draught") && Sp3Fuehrt(g, "item.dry_bread") &&
                  Sp3Fuehrt(g, "item.ash_tea") && !Sp3Fuehrt(g, "item.black_powder"),
                $"Gerlach: Händler {g?.vendor}, Angebot: {angebot} | alt: kein Händler (vendor false, leeres Angebot) – kein „Handeln“ im Gespräch");
            Check(g != null && g.stock != null && g.stock.Count > 0 &&
                  g.stock.All(e => e?.item != null && Progression.BuyPrice(e.item, e.price, e.count) >= e.item.sellPrice * e.count),
                "Keine Geldquelle bei Gerlach: jeder Kaufpreis ≥ Verkaufswert");

            // ---------------------------------------------------------- WESTWEIDEN-03 / FORTSCHRITT-08 (Daten + 500 Würfe je Klasse)
            CharacterClassDefinition[] klassen = { GameData.Class(Sp3Warden), GameData.Class(Sp3Stalker), GameData.Class(Sp3Weaver) };
            if (klassen.Any(c => c == null)) { Check(false, "Drei Klassen geladen"); return; }
            Sp3Punkt("WESTWEIDEN-03", "Rotkehl (selten, Stufe 12): Ausrüstung nur ab Stufe 9 (Eichenschild ausgenommen), keine Wahlbelohnungen; 500 Würfe je Klasse");
            Sp6Beute("loot.ww_rotkehl", klassen, 500, out _);
            Sp3Punkt("FORTSCHRITT-08", "Heckenkönig, Kellerleute, Kellermeister Kord, Habbo: Ausrüstung nur ab Stufe 9 (Eichenschild ausgenommen); 500 Würfe je Klasse");
            Sp6Beute("loot.ww_heckenkoenig", klassen, 500, out List<List<ItemDefinition>> heck);
            Sp6Beute("loot.ww_kellerleute", klassen, 500, out _);
            Sp6Beute("loot.ww_kellermeister", klassen, 500, out List<List<ItemDefinition>> kord);
            Sp6Beute("loot.ww_habbo", klassen, 500, out _);
            if (kord.Count > 0)
            {
                int genau = kord.Count(w => w.Count(d => (int)d.quality >= (int)ItemQuality.Rare && d.requiredLevel >= 11 && d.requiredLevel <= 12) == 1 && w.Count == 1);
                Check(genau == kord.Count, $"Kellermeister Kord: {genau}/{kord.Count} Würfe mit genau einem Teil Qualität ≥ selten, Stufe 11–12 | alt: Kareth-Teile Stufe 1–5");
            }
            if (heck.Count > 0)
            {
                int ruestung = heck.Count(w => w.Count == 1 && !w[0].IsWeapon && !w[0].IsShield);
                int selten = heck.Count(w => w.Count == 1 && (int)w[0].quality >= (int)ItemQuality.Rare);
                Check(ruestung == heck.Count, $"Heckenkönig (Tier, Elite 15): {ruestung}/{heck.Count} Würfe mit genau einem Rüstungsteil, keine Waffe; selten {selten * 100f / heck.Count:0} % (Erwartung ≈ 43 %)");
            }

            // ---------------------------------------------------------- INSTANZEN-04 (Anzeige-Regel im selben Prozess wie der Host)
            Sp3Punkt("INSTANZEN-04", "Ring-Anzeige (ColosseumClient): „reset“ einer Begegnung im selben Prozess (Host: Salzgruben-Platz belegt/zurückgesetzt) löscht die laufende Anzeige nicht; CueEnd 0 leert sie");
            Vector3 fern = new Vector3(0f, -900f, 0f);
            EncounterCues.Raise(fern, ColosseumClient.CueRevives, 3f);
            EncounterCues.Raise(fern, ColosseumClient.CueClock, 12f);
            EncounterCues.Raise(fern, ColosseumClient.CuePhase, 1f);
            string vor = ColosseumClient.Describe();
            bool lief = ColosseumClient.Visible && ColosseumClient.InRun;
            EncounterCues.Raise(fern, EncounterCues.Reset, 0f);   // genau das, was EnemyEncounter.ServerReset auf dem Host lokal auslöst
            string nach = ColosseumClient.Describe();
            Check(lief && ColosseumClient.Visible && ColosseumClient.InRun && ColosseumClient.Phase == 1 && ColosseumClient.Revives == 3,
                $"Laufende Anzeige ({vor}) nach „reset“: {nach} | alt: visible=0;run=0 (Anzeige mitten im Lauf gelöscht)");
            EncounterCues.Raise(fern, ColosseumClient.CueEnd, 0f);
            Check(!ColosseumClient.Visible && !ColosseumClient.InRun, $"CueEnd 0 (Betreten, Verlassen, Wipe) leert die Anzeige weiter: {ColosseumClient.Describe()}");
            Sp3NichtGeprueft("INSTANZEN-04", "echter Kolosseum-Lauf mit Salzgruben-Reset auf dem Host – das Kolosseum gibt es nur in Sandbox_M1 (mehrspieler4)");
        }

        /// <summary>Beutetabelle prüfen: Ausrüstung der Gruppen ab Stufe 9 (oder Eichenschild), keine Wahlbelohnungen; Würfe je Klasse.</summary>
        private void Sp6Beute(string tabelle, CharacterClassDefinition[] klassen, int wuerfe, out List<List<ItemDefinition>> ausruestungJeWurf)
        {
            ausruestungJeWurf = new List<List<ItemDefinition>>();
            LootTableDefinition t = GameData.Get<LootTableDefinition>(tabelle);
            if (t == null) { Check(false, $"Beutetabelle {tabelle} geladen"); return; }
            List<ItemDefinition> teile = t.groups.Where(gr => gr != null).SelectMany(gr => gr.items.Select(w => w.item)).Where(d => d != null && d.IsEquippable).Distinct().ToList();
            List<string> zuNiedrig = teile.Where(d => d.requiredLevel < 9 && d.Id != "item.oak_shield").Select(d => $"{d.Id} ({d.requiredLevel})").ToList();
            List<string> wahl = teile.Where(d => d.Id.StartsWith("item.gr_moor_", StringComparison.Ordinal) || d.Id.StartsWith("item.k28_", StringComparison.Ordinal)).Select(d => d.Id).ToList();
            List<string> gegner = GameData.Database.All.OfType<EnemyDefinition>().Where(e => e != null && e.lootTable == t).Select(e => e.Id).ToList();
            Check(teile.Count > 0 && zuNiedrig.Count == 0 && wahl.Count == 0,
                $"{tabelle} ({string.Join(", ", gegner)}): {teile.Count} Ausrüstungsteile, Stufe {(teile.Count > 0 ? teile.Min(d => d.requiredLevel) + "–" + teile.Max(d => d.requiredLevel) : "–")}" +
                (zuNiedrig.Count > 0 ? " – ZU NIEDRIG: " + string.Join(", ", zuNiedrig) : "") + (wahl.Count > 0 ? " – WAHLBELOHNUNG: " + string.Join(", ", wahl) : "") +
                " | alt: Kareth-Teile Stufe 1–5");
            int niedrig = 0, mitTeil = 0, weberPassend = 0, weberTeile = 0;
            for (int k = 0; k < klassen.Length; k++)
            {
                var rng = new System.Random(2210 + k);
                for (int i = 0; i < wuerfe; i++)
                {
                    var wurf = new List<ItemDefinition>();
                    foreach (ItemStack s in LootGenerator.Roll(t, klassen[k], rng))
                    {
                        ItemDefinition d = GameData.Item(s.id);
                        if (d == null || !d.IsEquippable) continue;
                        wurf.Add(d);
                        if (d.requiredLevel < 9 && d.Id != "item.oak_shield") niedrig++;
                        if (klassen[k].Id == Sp3Weaver)
                        {
                            weberTeile++;
                            bool stoffOderStab = d.armorType == ArmorType.Cloth || (d.IsWeapon && klassen[k].CanUseWeapon(d.weapon.weaponClass));
                            if (stoffOderStab) weberPassend++;
                        }
                    }
                    if (wurf.Count > 0) mitTeil++;
                    ausruestungJeWurf.Add(wurf);
                }
            }
            int gesamt = klassen.Length * wuerfe;
            Check(niedrig == 0 && mitTeil > 0,
                $"{tabelle}: {gesamt} Würfe (3 Klassen × {wuerfe}), Ausrüstung in {mitTeil * 100f / gesamt:0} % der Würfe, Teile unter Stufe 9: {niedrig}");
            if (weberTeile > 0) Info($"{tabelle}: Aschenweber – {weberPassend * 100f / weberTeile:0} % Stoff bzw. eigene Waffe (Klassenquote der Gruppe 85 %)");
        }

        private static CharacterSaveData Sp6Stand(IEnumerable<(string id, int n)> bag, IEnumerable<string> erledigt, IEnumerable<string> laufend)
        {
            var d = new CharacterSaveData
            {
                profileId = "sp6-lade-bereinigung",
                bag = new ItemStack[InventoryModel.BagSize],
                completedQuests = new List<string>(erledigt ?? new string[0]),
                quests = new List<QuestSave>()
            };
            int i = 0;
            foreach ((string id, int n) in bag) d.bag[i++] = new ItemStack(id, n, true);
            if (laufend != null)
                foreach (string q in laufend) d.quests.Add(new QuestSave { id = q, progress = new int[0] });
            return d;
        }

        private static int Sp6Anzahl(CharacterSaveData d, string id) => d.bag.Where(s => !s.IsEmpty && s.id == id).Sum(s => s.count);

        private static string Sp6Abbild(CharacterSaveData d) => string.Join(" ", d.bag.Where(s => !s.IsEmpty).Select(s => s.id.Replace("item.", "") + "×" + s.count));

        private static string Sp6Liste(List<string> l) => l.Count > 0 ? " – " + string.Join("; ", l) : "";

        // ================================================================== 9b FORTSCHRITT-06: Rollender Donner

        private IEnumerator Sp6Talentschaden(NetworkInventory a, NetworkProgression pa)
        {
            Line("");
            Line("[9b Talente: Rollender Donner auf Donnerschlag (FORTSCHRITT-06)]");
            Sp3Punkt("FORTSCHRITT-06", "Eidwächter Stufe 17: Donnerschlag am selben betäubten Ziel ohne und mit „Rollender Donner“ 3/3 (+30 % laut Tooltip)");
            CharacterClassDefinition warden = GameData.Class(Sp3Warden);
            SpellDefinition donner = GameData.Get<SpellDefinition>(Sp3Thunder);
            var nm = a.GetComponent<NetworkMotion>();
            if (warden == null || donner == null || GameData.Get<TalentDefinition>(Sp6Donner) == null || GameData.Get<TalentDefinition>(Sp6Zorn) == null || nm == null)
            {
                Check(false, "Eidwächter, Donnerschlag, Talente „Gerechter Zorn“/„Rollender Donner“ und Bewegung vorhanden");
                yield break;
            }
            int stufe0 = a.ServerModel.Level;
            EnemyBrain ziel = null;
            try
            {
                pa.ServerDebugSwitchClass(warden, true);   // Talente leer, Zauber bleiben
                a.ServerSetLevel(17);
                yield return new WaitForSecondsRealtime(0.6f);
                pa.ServerDebugLearn(Sp3Thunder, 1);
                var gruende = new List<string>();
                for (int i = 0; i < 5; i++) gruende.Add(pa.ServerLearnTalent(Sp6Zorn));
                Check(gruende.All(x => x == "") && pa.ServerTalentRank(Sp6Zorn) == 5,
                    $"Stufe {a.ServerModel.Level}: 5 × „Gerechter Zorn“ (Baum 1, Reihe 0) gelernt ({string.Join("/", gruende.Select(x => x == "" ? "ok" : x))}), frei {pa.ServerTalentPointsFree}");
                Vector3 stand = Sp3RuhigerOrt(a);
                yield return Ask(a, "goto", V(stand + Vector3.up * 0.3f, 0f));
                yield return new WaitForSecondsRealtime(1.5f);
                ziel = Sp3Gegner(KC.EnemyFaded, K28Near(a.transform.position, Flach(a.transform.forward).normalized, 4f), 12,
                    br => br.SetAggroProfile(AggroProfile.Defensive), 10f);
                if (ziel == null) { Check(false, "Verblichener als Ziel erzeugt"); yield break; }
                yield return new WaitForSecondsRealtime(0.6f);
                ziel.ServerStun(60f);
                Health zh = ziel.GetComponent<Health>();
                UiNeben(nm, a, ziel.transform.position, Flach(ziel.transform.position - a.transform.position).normalized, 2f);
                yield return new WaitForSecondsRealtime(0.5f);
                Info($"Ziel {ziel.Definition?.DisplayName} Stufe {ziel.Level}, {Flat(a.transform.position, ziel.transform.position):0.0} m entfernt (Radius Donnerschlag {donner.radius:0.#} m); " +
                     $"Tooltip ohne Talent: „{Sp3OhneTags(SpellRules.Describe(donner, pa.ServerSpellRank(Sp3Thunder)))}“");

                var ohne = new List<float>();
                var gruende0 = new List<string>();
                yield return Sp6Donnerreihe(pa, ziel, zh, 20, ohne, gruende0);
                var lernen = new List<string>();
                for (int i = 0; i < 3; i++) lernen.Add(pa.ServerLearnTalent(Sp6Donner));
                Check(lernen.All(x => x == "") && pa.ServerTalentRank(Sp6Donner) == 3,
                    $"„Rollender Donner“ 3/3 gelernt ({string.Join("/", lernen.Select(x => x == "" ? "ok" : x))})");
                Info($"Tooltip mit 3/3: „{Sp3OhneTags(SpellRules.Describe(donner, pa.ServerSpellRank(Sp3Thunder), pa.ServerTalents))}“");
                var mit = new List<float>();
                var gruende3 = new List<string>();
                yield return Sp6Donnerreihe(pa, ziel, zh, 20, mit, gruende3);
                float m0 = Sp6Median(ohne), m3 = Sp6Median(mit);
                float verh = m0 > 0f ? m3 / m0 : 0f;
                Info($"Schaden ohne Talent: {string.Join(" ", ohne.Select(x => x.ToString("0")))}");
                Info($"Schaden mit 3/3:     {string.Join(" ", mit.Select(x => x.ToString("0")))}");
                Check(ohne.Count >= 15 && mit.Count >= 15 && verh >= 1.15f,
                    $"Median {m0:0.0} → {m3:0.0} = ×{verh:0.00} (Soll ×1,30; Mittel ×{(ohne.Count > 0 && mit.Count > 0 ? mit.Average() / ohne.Average() : 0f):0.00}; " +
                    $"gewirkt {ohne.Count}/{mit.Count} von 20{(gruende0.Concat(gruende3).Any() ? ", abgelehnt: " + string.Join("/", gruende0.Concat(gruende3).Distinct()) : "")}) " +
                    "| alt: ×1,00 – der körperliche Flächenzweig ignorierte die Talentstärke, der Tooltip versprach +30 %");
            }
            finally
            {
                if (ziel != null) EnemyDirector.Instance.Remove(ziel);
                pa.ServerDebugSwitchClass(warden, true);   // Talente wieder leer
                a.ServerSetLevel(stufe0);
            }
            yield return new WaitForSecondsRealtime(0.5f);
        }

        private IEnumerator Sp6Donnerreihe(NetworkProgression pa, EnemyBrain ziel, Health zh, int n, List<float> werte, List<string> gruende)
        {
            for (int i = 0; i < n; i++)
            {
                ziel.ServerStun(30f);
                zh.Heal(zh.Max);
                pa.ServerDebugResetCooldowns();
                pa.ServerSetResource(100f);
                float hp0 = zh.Current;
                string why = pa.ServerCast(Sp3Thunder, null);
                yield return new WaitForSecondsRealtime(0.3f);
                if (why != "") { gruende.Add(why); continue; }
                werte.Add(hp0 - zh.Current);
            }
        }

        private static float Sp6Median(List<float> l)
        {
            if (l == null || l.Count == 0) return 0f;
            List<float> s = l.OrderBy(x => x).ToList();
            return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) * 0.5f;
        }

        // ================================================================== 9c FORTSCHRITT-07: Mana beim Umrüsten

        private IEnumerator Sp6Mana(NetworkInventory a, NetworkProgression pa)
        {
            Line("");
            Line("[9c Mana nach Umrüsten (FORTSCHRITT-07)]");
            Sp3Punkt("FORTSCHRITT-07", "Aschenweber, Feldhose aus Loden (+Int): Ab- und Anlegen ändert das Maximum, der Mana-ANTEIL bleibt – kein Auffüllen per Umrüsten");
            CharacterClassDefinition weaver = GameData.Class(Sp3Weaver), warden = GameData.Class(Sp3Warden);
            ItemDefinition hose = GameData.Item(Sp6Hose);
            FieldInfo letzte = typeof(NetworkProgression).GetField("_lastManaSpend", BindingFlags.NonPublic | BindingFlags.Instance);
            if (letzte == null) _uiFehlendeHaken.Add("NetworkProgression._lastManaSpend");
            if (weaver == null || warden == null || hose == null || letzte == null)
            {
                Check(false, $"Aschenweber, Eidwächter, {Sp6Hose} und das Feld _lastManaSpend vorhanden");
                yield break;
            }
            int stufe0 = a.ServerModel.Level;
            string beine0 = Equipped(a, EquipmentSlot.Legs);
            string legs = ((int)EquipmentSlot.Legs).ToString();
            try
            {
                pa.ServerDebugSwitchClass(weaver, false);
                a.ServerSetLevel(Mathf.Max(10, hose.requiredLevel));
                a.ServerGive(new ItemStack(Sp6Hose, 1));
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Ask(a, "autoequip", IndexOf(a, Sp6Hose).ToString());
                yield return new WaitForSecondsRealtime(0.8f);
                float m1 = pa.ServerResourceMax;
                bool an = Equipped(a, EquipmentSlot.Legs) == Sp6Hose;
                Check(an && pa.ServerResourceType == ResourceType.Mana && m1 > 0f,
                    $"Aschenweber Stufe {a.ServerModel.Level} mit {hose.DisplayName} an den Beinen (vorher „{beine0 ?? "nichts"}“): Mana-Maximum {m1:0}");
                if (!an) yield break;

                // Regeneration sperren (Fünf-Sekunden-Regel), dann 50 Mana
                letzte.SetValue(pa, Time.time);
                pa.ServerSetResource(50f);
                float hoechst = pa.ServerResource;
                var zeilen = new List<string>();
                bool anteilOk = true, zurueckOk = true;
                float differenz = 0f;
                for (int k = 0; k < 3; k++)
                {
                    float r0 = pa.ServerResource;
                    letzte.SetValue(pa, Time.time);
                    yield return Ask(a, "unequip", legs);
                    yield return new WaitForSecondsRealtime(0.6f);
                    float mAb = pa.ServerResourceMax, rAb = pa.ServerResource;
                    hoechst = Mathf.Max(hoechst, rAb);
                    float soll = r0 * mAb / m1;
                    if (k == 0) differenz = m1 - mAb;
                    if (!(mAb < m1 - 1f) || Mathf.Abs(rAb - soll) > 1.01f) anteilOk = false;
                    letzte.SetValue(pa, Time.time);
                    yield return Ask(a, "autoequip", IndexOf(a, Sp6Hose).ToString());
                    yield return new WaitForSecondsRealtime(0.6f);
                    float mAn = pa.ServerResourceMax, rAn = pa.ServerResource;
                    hoechst = Mathf.Max(hoechst, rAn);
                    if (Mathf.Abs(mAn - m1) > 0.5f || Equipped(a, EquipmentSlot.Legs) != Sp6Hose) zurueckOk = false;
                    zeilen.Add($"{r0:0.0}/{m1:0} → ab {rAb:0.0}/{mAb:0} (Soll {soll:0.0}) → an {rAn:0.0}/{mAn:0}");
                }
                Info("Zyklen: " + string.Join(" · ", zeilen));
                Check(anteilOk && zurueckOk, "Ablegen senkt das Maximum, der Anteil bleibt (±1); Anlegen stellt das Maximum wieder her");
                Check(hoechst <= 51f, $"Drei Ab-/Anlege-Zyklen ab 50 Mana: höchstens {hoechst:0.0} Mana (Soll ≤ 51) | alt: jedes Anlegen schrieb die Differenz gut (+{differenz:0}) – nach drei Zyklen ≈ {50f + 3f * differenz:0} Mana, Umrüsten füllte mitten im Kampf auf");
                // Gegenprobe: volles Mana bleibt voll
                letzte.SetValue(pa, Time.time);
                pa.ServerSetResource(100000f);
                bool voll0 = Mathf.Abs(pa.ServerResource - pa.ServerResourceMax) < 0.5f;
                yield return Ask(a, "unequip", legs);
                yield return new WaitForSecondsRealtime(0.6f);
                bool voll1 = Mathf.Abs(pa.ServerResource - pa.ServerResourceMax) < 0.5f;
                float mVoll = pa.ServerResourceMax;
                yield return Ask(a, "autoequip", IndexOf(a, Sp6Hose).ToString());
                yield return new WaitForSecondsRealtime(0.6f);
                bool voll2 = Mathf.Abs(pa.ServerResource - pa.ServerResourceMax) < 0.5f;
                Check(voll0 && voll1 && voll2, $"Gegenprobe volles Mana: voll {voll0} → abgelegt voll {voll1} ({mVoll:0}) → angelegt voll {voll2} ({pa.ServerResource:0}/{pa.ServerResourceMax:0})");
            }
            finally
            {
                pa.ServerDebugSwitchClass(warden, true);
                a.ServerSetLevel(stufe0);
            }
            // Aufräumen außerhalb von finally (dort kein yield): Hose ab, weg, alte Beine wieder an
            if (Equipped(a, EquipmentSlot.Legs) == Sp6Hose)
            {
                yield return Ask(a, "unequip", legs);
                yield return new WaitForSecondsRealtime(0.5f);
            }
            a.ServerRemoveAll(Sp6Hose);
            if (!string.IsNullOrEmpty(beine0) && Equipped(a, EquipmentSlot.Legs) == null && IndexOf(a, beine0) >= 0)
            {
                yield return Ask(a, "autoequip", IndexOf(a, beine0).ToString());
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Info($"Aufgeräumt: Klasse {a.ServerModel.Class?.DisplayName}, Stufe {a.ServerModel.Level}, Beine „{Equipped(a, EquipmentSlot.Legs) ?? "nichts"}“");
        }

        // ================================================================== 9d STORY-05: Dominieren im Tod

        private IEnumerator Sp6DominierenImTod(NetworkInventory a, NetworkProgression pa, Health hp)
        {
            Line("");
            Line("[9d Dominieren im Tod (STORY-05)]");
            Sp3Punkt("STORY-05", "Aschenjäger dominiert einen geschwächten Aschwolf und fällt während des Wirkens → Wirken bricht ab, kein Gefährte neben der Leiche");
            EnemySpawnArea vorlage = K28Template(KC.EnemyWolf);
            CharacterClassDefinition jaeger = GameData.Class(Sp3Stalker), waechter = GameData.Class(Sp3Warden);
            NetworkPlayer np = a.GetComponent<NetworkPlayer>();
            CompanionSpecies art = vorlage != null && vorlage.Definition != null ? CompanionCatalog.ForEnemy(vorlage.Definition.Id) : null;
            if (vorlage == null || jaeger == null || waechter == null || np == null || art == null || EnemyDirector.Instance == null)
            {
                Check(false, $"Aschwolf-Vorlage ({vorlage != null}), Klassen, NetworkPlayer und Gefährten-Art ({art != null}) vorhanden");
                yield break;
            }
            Vector3 stand = Sp3RuhigerOrt(a);
            yield return Ask(a, "goto", V(stand + Vector3.up * 0.3f, 0f));
            yield return new WaitForSecondsRealtime(1.2f);
            EnemyBrain wolf = null;
            Func<NetworkPlayer, string> haltVorher = NetworkPlayer.HoldRespawn;
            try
            {
                pa.ServerRemoveDominate();
                pa.ServerDebugSwitchClass(jaeger, true);
                string frei = pa.ServerUnlockDominate();
                int stufe = Mathf.Max(1, a.ServerModel.Level);
                wolf = EnemyDirector.Instance.SpawnAt(vorlage.Prefab, K28Near(a.transform.position, Flach(a.transform.forward).normalized, 4f), 0f, stufe, "spielablauf3-dom",
                    br => br.SetAggroProfile(AggroProfile.Defensive), 30f);
                if (wolf == null) { Check(false, "Aschwolf erzeugt"); yield break; }
                yield return new WaitForSecondsRealtime(0.6f);
                Health wh = wolf.GetComponent<Health>();
                wolf.ServerStun(1.5f);
                wh.ApplyDamage(new DamageInfo { Amount = 1f, Source = a.gameObject });
                for (int i = 0; i < 4 && wh.IsAlive && wh.Fraction > art.dominateBelow - 0.05f; i++)
                    wh.ApplyDamage(new DamageInfo { Amount = wh.Max * 0.25f, Source = a.gameObject, Unblockable = true });
                yield return new WaitForSecondsRealtime(0.3f);
                string why = pa.ServerStartDominate(wolf.GetComponent<FishNet.Object.NetworkObject>());
                bool laeuft = why == "" && pa.ServerDominating;
                Check(frei == "" && laeuft, $"Dominieren begonnen („{(why == "" ? "ok" : why)}“): Wolf {wh.Fraction * 100f:0} % Leben, Wirken {(pa.ServerDominating ? $"läuft ({art.dominateSeconds:0.#} s)" : "KEINS")}");
                if (!laeuft) yield break;
                NetworkPlayer.HoldRespawn = p => ReferenceEquals(p, np) ? Sp6Halten : haltVorher?.Invoke(p);
                UiSetze(hp, "_invulnerableUntil", -1f);
                hp.ApplyDamage(new DamageInfo { Amount = hp.Max * 20f, Source = wolf.gameObject, Unblockable = true });
                yield return WaitUntil(() => !hp.IsAlive, 2f);
                yield return new WaitForSecondsRealtime(0.5f);
                bool abgebrochen = !pa.ServerDominating;
                Check(!hp.IsAlive && abgebrochen,
                    $"Figur gefallen ({hp.Current:0}/{hp.Max:0}) → 0,5 s später Wirken {(abgebrochen ? "abgebrochen" : "LÄUFT WEITER")} | alt: der Balken lief weiter");
                // 23.10.2026 (Runde-6-Reste c): Der Abbruch beendet die vom Dominieren gesetzte Betäubung (EnemyBrain.ServerStunEnde).
                // Vorher nur eine Info: ServerStun(0) in CancelDominate verpuffte an der Max-Regel, der Wolf stand bis zum geplanten Ende.
                Check(!wolf.IsStunned,
                    $"Wolf 0,5 s nach dem Abbruch {(wolf.IsStunned ? "NOCH BETÄUBT" : "frei")} (das Dominieren betäubt {art.dominateSeconds + 0.4f:0.0} s) " +
                    "| alt: bis zum geplanten Ende betäubt – ServerStun(0) in CancelDominate wirkte wegen Mathf.Max nicht");
                yield return new WaitForSecondsRealtime(art.dominateSeconds + 1f);
                string notiz = pa.ServerCompanionNote ?? "";
                Check(!hp.IsAlive && !pa.ServerHasCompanion && !notiz.StartsWith("dominiert:", StringComparison.Ordinal),
                    $"{art.dominateSeconds + 1.5f:0.0} s nach dem Tod: Gefährte {(pa.ServerHasCompanion ? "JA" : "keiner")}, Notiz „{notiz}“ | alt: „dominiert: …“ – der Wolf wurde Gefährte der Leiche");
            }
            finally
            {
                NetworkPlayer.HoldRespawn = haltVorher;
                if (!hp.IsAlive) np.ServerReviveHere(1f);
                hp.SetInvulnerableFor(7200f);
                if (wolf != null) EnemyDirector.Instance.Remove(wolf);
                pa.ServerRemoveDominate();
                pa.ServerDebugSwitchClass(waechter, true);
            }
            yield return new WaitForSecondsRealtime(0.5f);
        }

        // ================================================================== 9e STORY-01 / STORY-02: Pferdegeschichte

        private IEnumerator Sp6Pferdegeschichte(NetworkInventory a, NetworkProgression pa)
        {
            Line("");
            Line("[9e Pferdegeschichte: aufgegebene Aufträge wieder anbietbar (STORY-01), unterbrochene Schluss-Szene (STORY-02)]");
            string hq1 = HorseStoryCatalog.FoalRescueQuest, hq2 = HorseStoryCatalog.LastPastureQuest;
            MethodInfo login = typeof(NetworkProgression).GetMethod("HorseStoryLogin", BindingFlags.NonPublic | BindingFlags.Instance);
            if (login == null) _uiFehlendeHaken.Add("NetworkProgression.HorseStoryLogin");
            bool Marke(string f) => pa.ServerGate()?.HasFlag?.Invoke(f) ?? false;
            Sp3Punkt("STORY-01", "(a) Q1 „Fohlen“ mitten in der Suche aufgegeben (Fohlen gefunden, Schritt 10) → Stand zurück, sofort wieder im Angebot");
            if (GameData.Quest(hq1) == null || GameData.Quest(hq2) == null || pa.ServerIsCompleted(hq1) || pa.ServerIsCompleted(hq2))
            {
                Check(false, $"Pferde-Aufträge vorhanden und noch nicht abgeschlossen ({hq1}, {hq2})");
                yield break;
            }
            Sp3NichtGeprueft("STORY-01", "echter Ablauf am Hof (Szene, „!“ am Züchter) – Hof und horse.*-Anker gibt es nur in Regions/Westweiden.unity; geprüft serverseitig über Debug-Setter und echten Aufgeben-Weg");
            try
            {
                pa.ServerDebugFlag("story.horse.q1_home_played", true);
                pa.ServerDebugSetHorseStory(HorseStoryState.FoalFound, 10);
                pa.ServerDebugStartQuest(hq1);
                yield return new WaitForSecondsRealtime(0.5f);
                bool lief1 = pa.ServerIsActive(hq1);
                yield return Ask(a, "quest-abandon", hq1);
                yield return new WaitForSecondsRealtime(0.4f);
                string d1 = pa.DescribeHorseStory();
                Check(lief1 && !pa.ServerIsActive(hq1) && pa.ServerHorseState == HorseStoryState.Unknown && pa.ServerHorseStep == 0 &&
                      Marke("story.horse.q1_offered") && !Marke("story.horse.q1_home_played") && d1.Contains("offer=" + hq1 + ";"),
                    $"Aufgegeben über den echten Weg: {d1} | alt: state=FoalFound;step=10, kein Angebot – Tank-Klassenkette dauerhaft gesperrt");

                Sp3Punkt("STORY-01", "(b) Q2 „Letzte Weide“ nach dem Weidetor aufgegeben (Schritt 7 „Hand hinhalten“) → zurück auf das Angebot");
                pa.ServerDebugSetHorseStory(HorseStoryState.AdultAtFarm, 7);
                pa.ServerDebugStartQuest(hq2);
                yield return new WaitForSecondsRealtime(0.5f);
                bool lief2 = pa.ServerIsActive(hq2);
                int xp0 = pa.ServerXp;
                bool reittier0 = pa.ServerHasMount;
                yield return Ask(a, "quest-abandon", hq2);
                yield return new WaitForSecondsRealtime(0.4f);
                string hand = pa.ServerStoryAction("hand", "");
                string d2 = pa.DescribeHorseStory();
                Check(lief2 && !pa.ServerIsActive(hq2) && pa.ServerHorseState == HorseStoryState.AdultAtFarm && pa.ServerHorseStep == 0 &&
                      Marke("story.horse.q2_offered") && d2.Contains("offer=" + hq2 + ";"),
                    $"Aufgegeben: {d2} | alt: Schritt 7 blieb – „Hand hinhalten“ hätte das Pferd ohne EP/Geld gegeben");
                Info($"„Hand hinhalten“ danach: „{hand}“; Reittier {pa.ServerHasMount} (vorher {reittier0}), EP {pa.ServerXp - xp0:+0;-0;0} (in Kareth v4 ohne Hof-Anker – keine eigene Prüfung)");

                Sp3Punkt("STORY-01", "(c) Altstand ohne laufenden Auftrag (Fohlen gerettet, Heimweg 300 m) → beim Login repariert");
                if (login == null) Check(false, "HorseStoryLogin vorhanden");
                else
                {
                    pa.ServerDebugFlag("story.horse.q1_offered", false);
                    pa.ServerDebugSetHorseStory(HorseStoryState.FoalRescued, 20, 300f);
                    login.Invoke(pa, null);
                    string d3 = pa.DescribeHorseStory();
                    Check(pa.ServerHorseState == HorseStoryState.Unknown && pa.ServerHorseStep == 0 && pa.ServerHerdProgress < 0.01f && Marke("story.horse.q1_offered") &&
                          d3.Contains("offer=" + hq1 + ";"),
                        $"Login (HorseStoryLogin wie beim Anmelden, per Reflexion): {d3} | alt: Stand bleibt FoalRescued/20 ohne Auftrag");
                    Sp3NichtGeprueft("STORY-01", "echtes Ab-/Anmelden – der Login-Weg ist direkt aufgerufen (dieselbe Methode)");

                    // ---------------------------------------------------------- STORY-02
                    Sp3Punkt("STORY-02", "Schluss-Szene q2_final unterbrochen (gebunden, Schritt 90) → beim Login fortgesetzt (Schritt 99, „After“), Epilog möglich");
                    pa.ServerDebugSetHorseStory(HorseStoryState.BondedToPlayer, 90);
                    int xpS = pa.ServerXp;
                    login.Invoke(pa, null);
                    string d4 = pa.DescribeHorseStory();
                    Check(pa.ServerHorseStep == 99 && d4.Contains("phase=After") && pa.ServerXp == xpS,
                        $"Login: {d4}, EP unverändert ({pa.ServerXp - xpS:+0;-0;0}) | alt: Schritt 90 blieb – kein Epilog, Züchter nie im Haus");
                    pa.ServerDebugEpilogueCheck(3.0);
                    string d5 = pa.DescribeHorseStory();
                    Check(d5.Contains("epi=1") && d5.Contains("phase=Epilogue"), $"Epilog-Prüfung danach (gebunden vor 3 h): {d5}");
                    pa.ServerDebugSetHorseStory(HorseStoryState.BondedToPlayer, 99);
                    login.Invoke(pa, null);
                    Check(pa.ServerHorseStep == 99 && pa.ServerHorseState == HorseStoryState.BondedToPlayer, $"Gegenprobe: gebunden/99 bleibt beim Login unverändert ({pa.DescribeHorseStory()})");
                    Sp3NichtGeprueft("STORY-02", "echtes Trennen während q2_final und Reittier-Hinweis beim Login (TargetMessage) – Hof nur in Regions/Westweiden.unity");
                }
            }
            finally
            {
                pa.ServerDebugSetHorseStory(HorseStoryState.Unknown, 0);
                foreach (string f in new[] { "story.horse.q1_offered", "story.horse.q2_offered", "story.horse.q1_home_played" }) pa.ServerDebugFlag(f, false);
            }
            yield return new WaitForSecondsRealtime(0.3f);
        }

        // ================================================================== 9f STORY-04: einzigartiges Fundstück

        private IEnumerator Sp6Fundstueck(NetworkInventory a, NetworkProgression pa)
        {
            Line("");
            Line("[9f Einzigartiges Fundstück nach Aufgeben (STORY-04)]");
            string g4 = GlockenrastCatalog.G4, rad = GlockenrastCatalog.ItemSpareWheel, knoten = GlockenrastCatalog.NodeWheel;
            string marke = GlockenrastCatalog.TakenFlag(knoten);
            StoryAnchor anker = StoryAnchor.Find(knoten);
            ItemDefinition def = GameData.Item(rad);
            QuestDefinition q = GameData.Quest(g4);
            MethodInfo angenommen = typeof(NetworkProgression).GetMethod("GlockenrastQuestAccepted", BindingFlags.NonPublic | BindingFlags.Instance);
            if (angenommen == null) _uiFehlendeHaken.Add("NetworkProgression.GlockenrastQuestAccepted");
            Sp3Punkt("STORY-04", "Ersatzrad (einzigartig, G4) aufgehoben, G4 aufgegeben (Rad bleibt), neu angenommen → bleibt „genommen“; Altstand ohne Marke → neutrale Meldung statt „Kein Platz im Inventar.“");
            if (anker == null || def == null || q == null || angenommen == null)
            {
                Check(false, $"Fundstelle {knoten} in der Szene ({anker != null}), Ersatzrad ({def != null}), G4 ({q != null}), Annahme-Haken ({angenommen != null})");
                yield break;
            }
            bool Hat() => pa.ServerGate()?.HasFlag?.Invoke(marke) ?? false;
            try
            {
                a.ServerRemoveAll(rad);
                pa.ServerDebugFlag(marke, false);
                pa.ServerDebugStartQuest(g4);
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Ask(a, "goto", V(anker.Position + new Vector3(1.1f, 0.3f, 0f), 0f));
                yield return Ask(a, "story-act", knoten);
                Check(def.unique && pa.ServerStoryNote == knoten + ": ok" && a.ServerModel.Count(rad) == 1 && Hat(),
                    $"{def.DisplayName} (einzigartig {def.unique}) aufgehoben ({pa.ServerStoryNote}), Marke gesetzt {Hat()}");
                yield return Ask(a, "quest-abandon", g4);
                yield return new WaitForSecondsRealtime(0.3f);
                Check(!pa.ServerIsActive(g4) && a.ServerModel.Count(rad) == 1, "G4 aufgegeben: das Ersatzrad bleibt im Rucksack (Max-Regel REP-062)");
                pa.ServerDebugStartQuest(g4);
                angenommen.Invoke(pa, new object[] { q });   // wie die Annahme beim Geber (ServerDebugStartQuest ruft den Haken nicht)
                yield return new WaitForSecondsRealtime(0.4f);
                Check(pa.ServerIsActive(g4) && Hat(), $"G4 neu angenommen: das Fundstück bleibt „genommen“ (Marke {Hat()}) | alt: Marke entfernt – das Rad lag wieder sichtbar am Fundort");
                int f0 = _sp3Fehlertexte.Count;
                pa.ServerDebugFlag(marke, false);   // Altstand vor dem Fix nachbilden
                yield return Ask(a, "story-act", knoten);
                yield return new WaitForSecondsRealtime(0.3f);
                List<string> meldungen = _sp3Fehlertexte.Skip(f0).ToList();
                Check(pa.ServerStoryNote == knoten + ": schon genommen" && Hat() && a.ServerModel.Count(rad) == 1 && !meldungen.Any(m => m.Contains("Kein Platz")),
                    $"Altstand (Marke fehlt, Rad im Rucksack) angeklickt: „{pa.ServerStoryNote}“, Marke wieder {Hat()}, rote Meldungen {(meldungen.Count > 0 ? string.Join(" / ", meldungen) : "keine")} " +
                    $"| alt: „{knoten}: kein Platz“ und rot „Kein Platz im Inventar.“ bei jedem Klick");
            }
            finally
            {
                a.ServerRemoveAll(rad);
                pa.ServerDebugFlag(marke, false);
            }
            if (pa.ServerIsActive(g4)) yield return Ask(a, "quest-abandon", g4);
            yield return new WaitForSecondsRealtime(0.3f);
        }

        // ================================================================== 9g Westweiden

        private IEnumerator Sp6Westweiden(NetworkInventory a, NetworkProgression pa)
        {
            Line("");
            Line("[9g Westweiden: sichere Orte (WELTZELLEN-03), Gerlach (WESTWEIDEN-02), Meer (WELTZELLEN-01), Gerufene (WELTZELLEN-02)]");
            WorldStreamer ws = WorldStreamer.Instance;
            WorldCellDefinition zelle = ws != null && ws.Manifest != null ? ws.Manifest.FindCell(WwZelle) : null;
            if (zelle == null) { Check(false, "Streamer mit Zelle westweiden_v2 (Kareth v4)"); yield break; }
            Vector3 mitte = new Vector3(2300f - 6450f, 0f, 1500f - 3140f);   // Langer Rain, Ortsmitte (Weidepass)
            Vector3 ziel = mitte + new Vector3(14f, 0f, -18f);
            float t0 = Time.realtimeSinceStartup;
            yield return Preload(ws, ziel, 90f);
            ziel = Gnd(ziel.x, ziel.z, 0.3f);
            yield return Ask(a, "goto", V(ziel, 0f));
            yield return new WaitForSecondsRealtime(3f);
            bool geladen = ws.StateOf(WwZelle) == WorldStreamer.CellState.Ready;
            Info($"Westweiden {(geladen ? "geladen" : "NICHT geladen")} nach {Time.realtimeSinceStartup - t0:0} s, Figur bei {a.transform.position.x:0}/{a.transform.position.z:0}");
            if (!geladen) { Check(false, "Westweiden-Zelle in Langer Rain geladen"); yield break; }

            // ---------------------------------------------------------- WELTZELLEN-03
            Sp3Punkt("WELTZELLEN-03", "Sichere Orte in den Westweiden: Langer Rain, Hallgrund, Dreispannhof (Anker + Umkreis) sicher; Kammsattel, Kreidekeller, 150 m neben dem Ort nicht");
            SafeSpawnAnchor Anker(string id) => SafeSpawnAnchor.All.FirstOrDefault(x => x != null && x.AnchorId == id);
            SafeSpawnAnchor lr = Anker("ww_langer_rain"), hg = Anker("ww_hallgrund"), dh = Anker("ww_dreispannhof"), ks = Anker("ww_kammsattel"), kk = Anker("ww_kreidekeller");
            NpcIdentity gerlach = NpcIdentity.Find(WC.Gerlach);
            Vector3 ortsmitte = Gnd(mitte.x, mitte.z, 0f);
            if (lr == null || hg == null || dh == null) Check(false, $"Anker Langer Rain ({lr != null}), Hallgrund ({hg != null}), Dreispannhof ({dh != null}) in der Zelle");
            else
            {
                bool S(Vector3 p) => NetworkInventory.ServerSichererOrt(p);
                Check(S(lr.transform.position) && S(hg.transform.position) && S(dh.transform.position) && S(ortsmitte) && (gerlach == null || S(gerlach.transform.position)),
                    $"Sicher: Anker Langer Rain {S(lr.transform.position)}, Ortsmitte {S(ortsmitte)}, Gerlach {(gerlach != null ? S(gerlach.transform.position).ToString() : "–")}, Hallgrund {S(hg.transform.position)}, Dreispannhof {S(dh.transform.position)} " +
                    "| alt: alles unsicher (keine Stützpunkte in der Zelle) – immer der 20-s-Countdown");
                Vector3 neben = lr.transform.position + new Vector3(150f, 0f, 0f);
                Check((ks == null || !S(ks.transform.position)) && (kk == null || !S(kk.transform.position)) && !S(neben),
                    $"Unsicher bleiben: Kammsattel {(ks != null ? S(ks.transform.position).ToString() : "–")}, Kreidekeller {(kk != null ? S(kk.transform.position).ToString() : "–")}, 150 m östlich des Ankers {S(neben)}");
                yield return Sp6MenueSicher(a, lr.transform.position);
            }

            // ---------------------------------------------------------- WESTWEIDEN-02 im Spiel
            Sp3Punkt("WESTWEIDEN-02", "Im Spiel: Gespräch mit Gerlach zeigt „Handeln“, Kauf über den Server zum Regelpreis");
            NpcDefinition g = GameData.Npc(WC.Gerlach);
            QuestUi ui = FindAnyObjectByType<QuestUi>();
            int k = g?.stock != null ? g.stock.FindIndex(e => e?.item != null && e.item.Id == "item.healing_draught") : -1;
            if (gerlach == null || g == null || ui == null || k < 0) Check(false, $"Gerlach in der Zelle ({gerlach != null}) mit Heiltrank im Angebot ({k}), Auftragsoberfläche ({ui != null})");
            else
            {
                yield return GotoNpc(a, WC.Gerlach);
                yield return new WaitForSecondsRealtime(1f);
                yield return Ask(a, "npc-talk", WC.Gerlach);
                yield return UiTakt();
                yield return new WaitForSecondsRealtime(0.3f);
                bool knopf = UiSichtbar(UiFeld(ui, "_dlgTrade"));
                Check(_last.ok && knopf, $"Gespräch mit {g.Name}: Knopf „Handeln“ {(knopf ? "sichtbar" : "FEHLT")} | alt: kein „Handeln“");
                pa.ServerSetMoney(Math.Max(pa.ServerMoney, 10000));
                yield return new WaitForSecondsRealtime(0.3f);
                long geld0 = pa.ServerMoney;
                int n0 = a.ServerModel.Count("item.healing_draught");
                yield return Ask(a, "buy", k.ToString());
                yield return new WaitForSecondsRealtime(0.4f);
                VendorEntry e = g.stock[k];
                int soll = Progression.BuyPrice(e.item, e.price, e.count);
                Check(_last.ok && geld0 - pa.ServerMoney == soll && a.ServerModel.Count("item.healing_draught") == n0 + e.count,
                    $"Kauf bei Gerlach: {e.item.DisplayName} ×{e.count} für {geld0 - pa.ServerMoney}k (Soll {soll}k), Rucksack {n0} → {a.ServerModel.Count("item.healing_draught")} | alt: „Nicht im Angebot.“");
                for (int i = 0; i < 5 && UiFensterstapel.Oberstes != ""; i++)
                {
                    UiFensterstapel.EscAusloesen();
                    yield return UiTakt();
                }
            }
            Sp3NichtGeprueft("WESTWEIDEN-02", "Lehrer, Gasthaus-Ruhe und Handel bei Harm/Tade – neue Inhalte (Max)");

            // ---------------------------------------------------------- WELTZELLEN-01: Meer
            Sp3Punkt("WELTZELLEN-01", "Weit draußen im Meer vor der Westweiden-Küste (außerhalb aller Zellen, jenseits unloadMargin): die Wasserfläche hält die Zelle geladen");
            Rect r = zelle.bounds;
            float x = r.xMin - (zelle.unloadMargin + 350f);
            WaterVolume meer = null;
            Vector3 punkt = Vector3.zero;
            foreach (float z in new[] { -1440f, r.center.y, r.yMin + 300f, r.yMax - 300f })
            {
                foreach (WaterVolume w in WaterVolume.All)
                    if (w != null && w.gameObject.scene.name == zelle.SceneName && w.AreaXZ.Contains(new Vector2(x, z))) { meer = w; punkt = new Vector3(x, w.SurfaceY, z); break; }
                if (meer != null) break;
            }
            if (meer == null) Check(false, $"Meeresfläche der Zelle {r.xMin - x:0} m westlich des Zellrands (x {x:0})");
            else
            {
                bool ausserhalb = ws.Manifest.CellAt(punkt, out _) == null && DungeonSlot.At(punkt) == null;
                int entladen0 = WorldStreamer.Log.Count(l => l.Contains("entladen " + WwZelle));
                yield return Ask(a, "goto", V(punkt + Vector3.up * 0.2f, 270f));
                float warten = zelle.holdSeconds + 12f, t = 0f, weg = 0f;
                bool immer = true;
                while (t < warten)
                {
                    yield return new WaitForSecondsRealtime(1f);
                    t += 1f;
                    if (ws.StateOf(WwZelle) != WorldStreamer.CellState.Ready) immer = false;
                    weg = Mathf.Max(weg, Flat(a.transform.position, punkt));
                }
                bool still = WorldStreamer.Log.Count(l => l.Contains("entladen " + WwZelle)) == entladen0;
                bool wasser = WaterVolume.TrySurface(a.transform.position, out float spiegel);
                Check(ausserhalb && r.xMin - punkt.x > zelle.unloadMargin && immer && still && weg < 50f,
                    $"Meerpunkt {punkt.x:0}/{punkt.y:0}/{punkt.z:0} ({r.xMin - punkt.x:0} m vor dem Zellrand, unloadMargin {zelle.unloadMargin:0} m, außerhalb aller Zellen {ausserhalb}): " +
                    $"{warten:0} s lang Zelle {(immer ? "geladen" : "ENTLADEN")}, Figur höchstens {weg:0} m vom Punkt, im Wasser {wasser} (Spiegel {spiegel:0.0}, Figur y {a.transform.position.y:0.0}) " +
                    $"| alt: nach {zelle.holdSeconds:0} s entladen – Sturz unter die Welt, Figur am Bindepunkt");
            }

            // ---------------------------------------------------------- WELTZELLEN-02: Gerufene der Alarmglocke
            // 23.10.2026 (Runde-6-Reste a): trennscharf umgebaut. Der Lauf vom 22.10. prüfte nichts: (1) Die Auswertung hing an
            // „glocke != null“ – die Glocke liegt in der Zellszene und ist nach dem Entladen zerstört (Unity-null), der Block entfiel
            // also immer. (2) Die Glocke schickte ihre Gerufenen nach 30 s ohne Spieler selbst fort, die Zelle entlud erst nach ~37 s –
            // EnemyDirector.OnCellUnloading fand nie einen Gerufenen. Jetzt: Gerufene als Referenz + NetworkObject-Id und das Läuten
            // als bool VOR dem Weggehen merken; die Glocke gleich nach dem Läuten anhalten (enabled = false → ihr Aufräumen nach 30 s
            // läuft nicht; nachgestellt wird der Fall „Zelle entlädt vor dem Aufräumen“, z. B. Tod gegen die Verstärkung). Bild für
            // Bild bis zum Entladen verfolgen: Lebten im letzten Bild davor noch Gerufene, müssen sie danach fort sein. Mit dem alten
            // EnemyDirector (entfernte nur Gebietsgegner aus _owner) blieben sie ohne Zelle stehen → rot. Lebt beim Entladen keiner
            // mehr, ist die Vorbedingung verletzt → ebenfalls rot (kein stilles „nicht geprüft“ mehr).
            Sp3Punkt("WELTZELLEN-02", "Alarmglocke im Kreidekeller läuten, Verstärkung NICHT töten, Glocken-Aufräumen anhalten, zurück ans Wachtfeuer → beim Entladen der Zelle gehen die Gerufenen mit (EnemyDirector.OnCellUnloading)");
            AlarmGlocke glocke = AlarmGlocke.Liste.FirstOrDefault(gl => gl != null && ws.CellAt(gl.SeilMitte) == WwZelle);
            GameObject spawn = GameObject.Find("PlayerSpawn");
            Vector3 lager = spawn != null ? spawn.transform.position : Gnd(0f, 7f);
            var gerufen = new List<(EnemyBrain b, FishNet.Object.NetworkObject nob, int id)>();
            bool gelaeutet = false;   // nicht später „glocke != null“ fragen: die Glocke vergeht mit der Zelle
            // Gerufener noch derselbe gespawnte Gegner (Referenz lebt, NetworkObject gespawnt, Id unverändert)?
            bool GerufenDa((EnemyBrain b, FishNet.Object.NetworkObject nob, int id) ruf) => ruf.b != null && ruf.nob != null && ruf.nob.IsSpawned && ruf.nob.ObjectId == ruf.id;
            try
            {
                if (glocke == null) Check(false, "Alarmglocke im Kreidekeller (Zelle geladen)");
                else
                {
                    int aus0 = glocke.Ausloesungen;
                    yield return Ask(a, "goto", V(glocke.SeilMitte + new Vector3(0f, 0.1f, 2.5f), 180f));
                    yield return new WaitForSecondsRealtime(1f);
                    yield return Ask(a, "goto", V(glocke.SeilMitte + new Vector3(0f, 0.1f, 0f), 180f));
                    yield return WaitUntil(() => glocke.Ausloesungen > aus0, 4f);
                    if (glocke.Ausloesungen > aus0) glocke.enabled = false;   // Aufräumen nach 30 s ohne Spieler anhalten (s. o.)
                    yield return new WaitForSecondsRealtime(1f);
                    foreach (EnemyBrain b in glocke.Gerufen)
                    {
                        if (b == null) continue;
                        FishNet.Object.NetworkObject nob = b.GetComponent<FishNet.Object.NetworkObject>();
                        gerufen.Add((b, nob, nob != null ? nob.ObjectId : -1));
                    }
                    gelaeutet = glocke.Ausloesungen == aus0 + 1 && gerufen.Count >= 1;
                    Check(gelaeutet, $"Seil berührt: Glocke läutet ({glocke.Ausloesungen}×), {gerufen.Count} Gerufene (SpawnOnce, ohne Gebiet; Ids {string.Join(", ", gerufen.Select(ruf => ruf.id))}), " +
                                     $"Glocke angehalten {!glocke.enabled}");
                }
                yield return Ask(a, "goto", V(lager, 20f));
                int lebend = -1;
                float tE = 0f;
                _sp6EnemyLog.Clear();
                Application.LogCallback mitlesen = (m, s, ty) => { if (m != null && m.Contains("Zelle " + WwZelle + " entladen")) _sp6EnemyLog.Add(m); };
                Application.logMessageReceived += mitlesen;
                try
                {
                    while (tE < 120f)
                    {
                        WorldStreamer.CellState st = ws.StateOf(WwZelle);
                        // letztes Bild vor dem Entladen (OnCellUnloading läuft im Bild des Wechsels Ready → Unloading)
                        if (st == WorldStreamer.CellState.Ready || st == WorldStreamer.CellState.Loaded)
                            lebend = gerufen.Count(ruf => GerufenDa(ruf) && ruf.b.isActiveAndEnabled && ruf.b.Health != null && ruf.b.Health.IsAlive);
                        else if (st == WorldStreamer.CellState.Unloaded) break;
                        yield return null;
                        tE += Time.unscaledDeltaTime;
                    }
                }
                finally
                {
                    Application.logMessageReceived -= mitlesen;
                }
                yield return new WaitForSecondsRealtime(1f);
                int rest = gerufen.Count(GerufenDa);
                int westlich = EnemyBrain.All.Count(e => e != null && e.transform.position.x < WwKante);
                string protokoll = WorldStreamer.Log.LastOrDefault(l => l.Contains("entladen " + WwZelle)) ?? "–";
                string regie = _sp6EnemyLog.LastOrDefault() ?? "";
                if (gelaeutet)
                {
                    if (ws.StateOf(WwZelle) != WorldStreamer.CellState.Unloaded) Check(false, $"Zelle {tE:0} s nach der Rückkehr ans Wachtfeuer entladen (Zustand {ws.StateOf(WwZelle)})");
                    else if (lebend <= 0) Check(false, $"Vorbedingung verletzt: im letzten Bild vor dem Entladen ({tE:0} s) lebte kein Gerufener mehr ({lebend}) trotz angehaltener Glocke – Fix nicht ausgelöst");
                    else Check(rest == 0 && westlich == 0,
                        $"Im letzten Bild vor dem Entladen ({tE:0} s) lebten {lebend}/{gerufen.Count} Gerufene; 1 s danach noch gespawnt: {rest}, Gegner westlich der Kante: {westlich}" +
                        $"{(regie.Length > 0 ? " (" + regie + ")" : " (keine Zeile „Zelle … entladen – N Gegner entfernt“)")} | alt: die Gerufenen blieben ohne Zelle in der Welt (nur Gebietsgegner entfernt)");
                }
                Info("Streamer: " + protokoll);
                if (regie.Length > 0) Info("Gegner-Regie: " + regie);
                Sp3Punkt("WELTZELLEN-01", "Gegenprobe: zurück am Wachtfeuer wird die Zelle wieder entladen");
                Check(ws.StateOf(WwZelle) == WorldStreamer.CellState.Unloaded, $"Westweiden {tE:0} s nach der Rückkehr {ws.StateOf(WwZelle)} (Soll Unloaded, ≤ 120 s)");
            }
            finally
            {
                if (glocke != null && !glocke.enabled) glocke.enabled = true;   // Zelle nicht entladen: Glocke räumt wieder selbst auf
            }
            Sp3NichtGeprueft("WELTZELLEN-01", "Client-Streamer (Test-Client im Meer) und Instanzplatz als Gegenprobe – in Kareth v4 keine Instanzen; Allein-Suite");
        }

        private readonly List<string> _sp6EnemyLog = new List<string>();

        /// <summary>WELTZELLEN-03: „Speichern und beenden“ über das echte Menü am sicheren Ort – sofort, ohne Countdown (Beenden abgefangen).</summary>
        private IEnumerator Sp6MenueSicher(NetworkInventory a, Vector3 anker)
        {
            SpielmenueUi menue = SpielmenueUi.Instance;
            if (menue == null) { Check(false, "Spielmenü vorhanden"); yield break; }
            Vector3 p = anker;
            if (NavMesh.SamplePosition(anker, out NavMeshHit h, 6f, NavMesh.AllAreas)) p = h.position;
            yield return Ask(a, "goto", V(p + Vector3.up * 0.3f, 0f));
            yield return new WaitForSecondsRealtime(1.5f);
            if (a.LootOpen) a.CloseLoot();
            for (int i = 0; i < 5 && (UiFensterstapel.Oberstes != "" || menue.IsOpen); i++)
            {
                if (menue.IsOpen) menue.Schliessen();
                else UiFensterstapel.EscAusloesen();
                yield return UiTakt();
            }
            bool sicher = NetworkInventory.ServerSichererOrt(a.transform.position);
            yield return new WaitForSecondsRealtime(2.3f);   // 2-s-Sperre früherer Bitten
            int beendet = 0;
            bool countdown = false;
            Action vorher = SpielmenueUi.BeendenFuerTests;
            SpielmenueUi.BeendenFuerTests = () => beendet++;
            float dauer = 0f;
            try
            {
                menue.Oeffnen();
                yield return UiTakt();
                float t1 = Time.unscaledTime;
                menue.SpeichernUndBeenden();
                while (beendet == 0 && Time.unscaledTime - t1 < 6f)
                {
                    if (a.ServerAbmeldenLaeuft || menue.Countdown) countdown = true;
                    yield return null;
                }
                dauer = Time.unscaledTime - t1;
            }
            finally
            {
                SpielmenueUi.BeendenFuerTests = vorher;
                if (menue.IsOpen) menue.Schliessen();
            }
            Check(sicher && beendet == 1 && !countdown,
                $"Echter Menüweg „Speichern und beenden“ am Anker Langer Rain (sicher {sicher}): beendet {beendet}× nach {dauer:0.0} s, Countdown {(countdown ? "JA" : "nein")} | alt: 20-s-Countdown");
        }

        // ================================================================== 10 CODEX-A2: NetworkBootstrap

        private IEnumerator Sp6NetzStart()
        {
            Line("");
            Line("[10 NetworkBootstrap: Host + Trennen / Host + Verbinden im selben Bild (Codex-Paket A), Host nach erfolglosem Verbinden – baut den Editor-Host ab und wieder auf]");
            Sp3Punkt("CODEX-A2", "(1) Host() und Disconnect() im selben Bild: kein eigener Client, Server bleibt aus, keine Port-Meldung");
            NetworkBootstrap boot = FindAnyObjectByType<NetworkBootstrap>();
            NetworkManager netz = FishNet.InstanceFinder.NetworkManager;
            FieldInfo wartet = typeof(NetworkBootstrap).GetField("_hostWartet", BindingFlags.NonPublic | BindingFlags.Instance);
            if (wartet == null) _uiFehlendeHaken.Add("NetworkBootstrap._hostWartet");
            if (boot == null || netz == null || wartet == null || netz.TransportManager == null || netz.TransportManager.Transport == null)
            {
                Check(false, "NetworkBootstrap, NetworkManager mit Transport und Feld _hostWartet vorhanden");
                yield break;
            }
            Transport tr = netz.TransportManager.Transport;
            bool Aus() => !netz.ServerManager.Started && !netz.ClientManager.Started &&
                          tr.GetConnectionState(true) == LocalConnectionState.Stopped && tr.GetConnectionState(false) == LocalConnectionState.Stopped;
            int starting = 0, started = 0;
            Action<ClientConnectionStateArgs> zaehle = args =>
            {
                if (args.ConnectionState == LocalConnectionState.Starting) starting++;
                else if (args.ConnectionState == LocalConnectionState.Started) started++;
            };
            NetzTransport.Stoppen(netz);
            yield return WaitUntil(Aus, 10f);
            for (int i = 0; i < 10; i++) yield return null;
            Info($"Editor-Host abgebaut: {(Aus() ? "Server, Client, Transport gestoppt" : $"NICHT ganz aus (Server {tr.GetConnectionState(true)}, Client {tr.GetConnectionState(false)})")}");
            netz.ClientManager.OnClientConnectionState += zaehle;
            try
            {
                boot.Host();
                boot.Disconnect();
                for (int i = 0; i < 5; i++) yield return null;
                yield return new WaitForSecondsRealtime(0.5f);
                bool w1 = (bool)wartet.GetValue(boot);
                Check(starting == 0 && started == 0 && !netz.ServerManager.Started && tr.GetConnectionState(true) == LocalConnectionState.Stopped && !w1 && boot.Meldung == "",
                    $"Client-Starts {starting}/{started}, Server {(netz.ServerManager.Started ? "LÄUFT" : "aus")} (Transport {tr.GetConnectionState(true)}), wartende Host-Absicht {w1}, Meldung „{boot.Meldung}“ " +
                    "| alt: Disconnect stoppte nur „Started“ – der startende Server lief weiter, sein verspätetes „Started“ startete den eigenen Client");

                Sp3Punkt("CODEX-A2", "(2) Host() und Join(127.0.0.2) im selben Bild: genau ein Client-Start, kein zweiter durch das verspätete Server-„Started“");
                NetzTransport.Stoppen(netz);
                yield return WaitUntil(Aus, 10f);
                starting = started = 0;
                boot.Host();
                boot.Join("127.0.0.2");
                for (int i = 0; i < 5; i++) yield return null;
                yield return new WaitForSecondsRealtime(1f);
                string adresse = tr.GetClientAddress();
                bool w2 = (bool)wartet.GetValue(boot);
                Check(starting == 1 && adresse == "127.0.0.2" && !w2 && boot.Meldung == "",
                    $"Client-Starts {starting} (verbunden {started}), Adresse {adresse}, wartende Host-Absicht {w2}, Meldung „{boot.Meldung}“ " +
                    "| alt: das „Started“ des eigenen Servers rief StartConnection ein zweites Mal (Tugboat bricht den Aufbau ab und beginnt neu)");
            }
            finally
            {
                netz.ClientManager.OnClientConnectionState -= zaehle;
            }
            // 23.10.2026 (Runde-6-Reste b): Das Wiederherstellen ist zugleich Prüfung (3). Bis zum 22.10. setzte die Suite hier die
            // Client-Adresse von Hand zurück – Lauf 22.10. 19:2x: ohne das verband sich der eigene Client des neuen Hosts mit
            // „127.0.0.2“ aus (2) und kam nie an (Server lief, Client nicht), weil Host() die zuletzt per Join gesetzte Adresse übernahm.
            // Jetzt bindet NetworkBootstrap.Host() den eigenen Client selbst an die lokale Adresse; die Suite hilft nur noch nach, wenn
            // er nach 60 s nicht verbunden ist (damit der Editor-Host für Folgeläufe benutzbar bleibt) – die Prüfung ist dann rot.
            Sp3Punkt("CODEX-A2", "(3) F9-Verbinden zu einer unerreichbaren Adresse (127.0.0.2, kein Server läuft) schlägt fehl → „Host starten“: " +
                                 "der eigene Client verbindet sich lokal, die Testfigur meldet sich an");
            NetzTransport.Stoppen(netz);
            yield return WaitUntil(Aus, 10f);
            for (int i = 0; i < 10; i++) yield return null;
            const string unerreichbar = "127.0.0.2";
            boot.Join(unerreichbar);
            float tJoin = Time.realtimeSinceStartup;
            yield return WaitUntil(() => Time.realtimeSinceStartup - tJoin > 1f && !netz.ClientManager.Started &&
                                         tr.GetConnectionState(false) == LocalConnectionState.Stopped, 20f);
            bool joinAufgegeben = !netz.ClientManager.Started && tr.GetConnectionState(false) == LocalConnectionState.Stopped;
            Info($"Verbinden zu {unerreichbar} ohne laufenden Server: nach {Time.realtimeSinceStartup - tJoin:0.0} s " +
                 (joinAufgegeben ? "aufgegeben (Client gestoppt)" : $"noch nicht aufgegeben (Client {tr.GetConnectionState(false)}) – „Host starten“ trotzdem, wie im F9-Fenster möglich") +
                 $", Client-Adresse {tr.GetClientAddress()}");
            boot.Host();
            NetworkInventory neu = null;
            yield return WaitUntil(() => (neu = OnlineInv(Spielablauf3Profile)) != null, 60f);
            string adresseHost = tr.GetClientAddress();
            bool eigenerClient = netz.ServerManager.Started && netz.ClientManager.Started;
            Check(eigenerClient && neu != null && adresseHost != unerreichbar,
                $"Nach „Host starten“: Server {(netz.ServerManager.Started ? "läuft" : "AUS")}, eigener Client {(netz.ClientManager.Started ? "verbunden" : "NICHT verbunden")} " +
                $"(Adresse {adresseHost}), Testfigur {(neu != null ? "angemeldet" : "NICHT angemeldet")}, Meldung „{boot.Meldung}“ " +
                $"| alt: Client-Adresse blieb {unerreichbar} – der eigene Client kam nie an");
            if (neu == null && netz.ServerManager.Started && !netz.ClientManager.Started)
            {
                Info("Notweg: eigener Client von Hand mit 127.0.0.1 verbunden, damit der Editor-Host benutzbar bleibt");
                netz.ClientManager.StartConnection("127.0.0.1");
                yield return WaitUntil(() => (neu = OnlineInv(Spielablauf3Profile)) != null, 60f);
            }
            Info($"Host wieder aufgebaut: Server {netz.ServerManager.Started}, Client {netz.ClientManager.Started}, Testfigur {(neu != null ? "angemeldet" : "NICHT angemeldet")}");
            if (neu == null) Check(false, "Nach der Probe läuft der Editor-Host wieder mit der Testfigur");
            if (neu != null) neu.GetComponent<Health>()?.SetInvulnerableFor(7200f);
            Sp3NichtGeprueft("CODEX-A2", "Join während eines echten, langsamen Host-Starts mit fremdem Server; Port-Meldung bei belegtem Port (prüft mehrspieler4 Teil 5, MEHRSPIELER-05)");
        }
    }
}
