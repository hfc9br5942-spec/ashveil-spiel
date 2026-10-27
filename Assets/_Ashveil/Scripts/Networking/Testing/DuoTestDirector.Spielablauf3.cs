using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Ashveil.Core.Events;
using Ashveil.Core.Settings;
using Ashveil.Data;
using Ashveil.Gameplay.Abilities;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Npcs;
using Ashveil.Gameplay.Quests;
using Ashveil.Gameplay.Targeting;
using Ashveil.Gameplay.Weapons;
using Ashveil.UI;
using Ashveil.World;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using KC = Ashveil.Data.KarethCatalog;

namespace Ashveil.Networking
{
    /// <summary>
    /// 21.10.2026 (Spielablauf Runde 3, Befunde EINSTIEG/FORTSCHRITT/AUFTRAEGE/KAMPF/MEHRSPIELER): die Fixes im Host-Spiel nachprüfen –
    /// ein Spieler, Szene Kareth v4, je Befund gezielte Prüfungen. Was ein Spieler allein im Editor nicht prüfen kann, steht am Ende
    /// ausdrücklich unter „nicht geprüft“ (keine Schein-Prüfungen).
    ///   Daten:  Bündelpreis (FORTSCHRITT-02), keine Geldquelle, Leinen/Rezept nicht im Angebot (FORTSCHRITT-03/05), Splitter/Brief nicht
    ///           in Beutetabellen + 500 Würfe (AUFTRAEGE-03/FORTSCHRITT-04), Munition nur für Klassen mit Bogen/Armbrust (AUFTRAEGE-02),
    ///           Logik von PartyService.Leave mit dem Online-Prädikat NetworkSocial.IsOnline (MEHRSPIELER-04), Q05 belohnt Grobe statt
    ///           Scharfe Pfeile (AUFTRAEGE-Q05), alle Fernkämpfer mit Reichweite ≥ Wunschabstand (KAMPF-02, Westweiden-Schützen).
    ///   Eingabe (echte Ereignisse des Input Systems, ohne Fensterfokus über eine vorübergehende Einstellung „Eingabe auch ohne Fokus“):
    ///           Namensfeld über den Tastaturpuffer (EINSTIEG-01), „/“ einer belegten Spieltaste öffnet keinen Chat – Num / kurz und
    ///           gehalten (Tastenwiederholung), Umschalt+7 (DE) = Leistenplatz 7; „/“ allein und Enter öffnen ihn (EINSTIEG-02), F2 ohne
    ///           Wirkung an der eigenen Figur ohne Eingriff der Suite (EINSTIEG-03, seit der Nachbesserung 21.10. ohne #if), Rechtsklick im
    ///           Rucksack (FORTSCHRITT-01, echter Mausklick auf den Platz).
    ///   Kampf:  GCD nach Sofortzauber mit Abklingzeit (KAMPF-04), Nahkampf am Reichweitenrand + Heldenhafter Schlag (KAMPF-03),
    ///           Betäubung beim Heimkehrer + Dominieren abgelehnt (KAMPF-01), Schützin an der Leinengrenze, Rückzug von innen bis zur
    ///           Grenze, Westweiden-Schütze schießt auf 9 m (KAMPF-02).
    ///   Welt:   Leiche glimmt nach Wiedereintritt in die Sicht (MEHRSPIELER-01), Grete nachts sitzend mit „!“ (AUFTRAEGE-01).
    ///   Tod:    toter Angreifer abgelehnt (MEHRSPIELER-03), Esc im Tod öffnet das Menü, Autolauf aus (EINSTIEG-05), tot gespeichert →
    ///           Bindepunkt, ohne Bindepunkt (Sandbox) keine Stelle (EINSTIEG-04); MEHRSPIELER-02 (echtes Trennen) nicht geprüft.
    ///   Weber:  Belohnung ohne Pfeile im Spiel (AUFTRAEGE-02), GCD Aschenschild → Glutbolzen (KAMPF-04).
    ///   Runde 4 (22.10.2026, DuoTestDirector.Spielablauf3.Runde4.cs): F10 wie im gebauten Spiel (REL-02), „Speichern und beenden“
    ///           mit Antwort binnen 2 s, Menü zu während „Speichere …“, Tod mit Grund (SPEICHERN-02).
    ///   Runde 6 (22.10.2026, DuoTestDirector.Spielablauf3.Runde6.cs): LADEN-01, WESTWEIDEN-02/-03, FORTSCHRITT-06/-07/-08, INSTANZEN-04
    ///           (Anzeige-Regel), STORY-01/-02/-04/-05, WELTZELLEN-01/-02/-03 (mit der Westweiden-Zelle), zuletzt CODEX-A2 (NetworkBootstrap:
    ///           baut den Editor-Host ab und wieder auf).
    /// Keine Spiel-Logik geändert. Bilder: Logs/Ashveil/Spielablauf3/. Start: Auftrag „spieltest spielablauf3“.
    /// Bericht: Logs/Ashveil/Ashveil_Spielablauf3Test.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string Spielablauf3Flag = "spielablauf3";
        private static bool _spielablauf3Mode;
        private const string Spielablauf3Profile = "duo-spielablauf3";

        private const string Sp3Warden = "class.warden", Sp3Weaver = "class.emberweaver", Sp3Stalker = "class.ashstalker";
        private const string Sp3HS = "spell.warden_heroic_strike", Sp3Thunder = "spell.warden_thunder_clap", Sp3Bash = "spell.warden_shield_bash";
        private const string Sp3AshShield = "spell.weaver_ash_shield", Sp3EmberBolt = "spell.weaver_ember_bolt";
        private const string Sp3Junk = "item.broken_pocketwatch";

        private sealed class Sp3Befund
        {
            public string Id, Titel;
            public int Ok, Fehler;
            public readonly List<string> NichtGeprueft = new List<string>();
            public readonly List<string> Punkte = new List<string>();
        }

        private readonly Dictionary<string, Sp3Befund> _sp3 = new Dictionary<string, Sp3Befund>();
        private readonly List<string> _sp3Reihenfolge = new List<string>();
        private readonly List<string> _sp3Bilder = new List<string>();
        private readonly List<string> _sp3Fehlertexte = new List<string>();
        private readonly List<string> _sp3Log = new List<string>();
        private string _sp3Id;
        private int _sp3Ok0, _sp3Fehler0;
        private bool _sp3EingabeUmgestellt;
        private InputSettings.BackgroundBehavior _sp3AltHintergrund;
        private InputSettings.EditorInputBehaviorInPlayMode _sp3AltEditor;

        private static readonly (string id, string titel)[] Sp3Titel =
        {
            ("EINSTIEG-01", "Namensfeld im Charakter-Editor bekommt getippte Buchstaben"),
            ("EINSTIEG-02", "„/“ einer belegten Spieltaste (Num /, Umschalt+7) öffnet den Chat nicht"),
            ("EINSTIEG-03", "F2 (Körper wechseln) ohne Wirkung an der vernetzten Spielerfigur"),
            ("EINSTIEG-04", "Tot abgemeldet → Start am Bindepunkt"),
            ("EINSTIEG-05", "Esc im Tod öffnet das Menü, Autolauf endet"),
            ("FORTSCHRITT-01", "Rechtsklick verkauft nur bei offenem Händlerfenster"),
            ("FORTSCHRITT-02", "Bündelpreis = Stückpreis × Menge (keine Geldquelle)"),
            ("FORTSCHRITT-03", "Leinenfetzen nicht bei Maelis/Idun im Angebot"),
            ("FORTSCHRITT-04", "Glockensplitter/Brief nicht mehr in der Beute"),
            ("FORTSCHRITT-05", "Rezept: Heiltrank nicht bei Maelis"),
            ("AUFTRAEGE-01", "Grete nachts sichtbar (sitzt) mit Markierung"),
            ("AUFTRAEGE-02", "Aschenweber bekommt keine Pfeile als Belohnung"),
            ("AUFTRAEGE-03", "Questgegenstände ohne Auftrag fallen nicht mehr"),
            ("AUFTRAEGE-Q05", "Q05 (ab Stufe 2) belohnt Grobe statt Scharfe Pfeile (Stufe 8)"),
            ("KAMPF-01", "Heimkehrer: Betäubung wirkt nicht, Dominieren abgelehnt, er kommt heim"),
            ("KAMPF-02", "Schützen schießen (Leinengrenze, Westweiden auf Distanz)"),
            ("KAMPF-03", "Nahkampf am Reichweitenrand / Heldenhafter Schlag bleibt bei Fehlschlag"),
            ("KAMPF-04", "GCD nach Sofortzauber mit Abklingzeit auf der Leiste"),
            ("MEHRSPIELER-01", "Leiche glimmt nach Wiedereintritt in die Sicht wieder"),
            ("MEHRSPIELER-02", "Tot getrennt → Start am Bindepunkt"),
            ("MEHRSPIELER-03", "Toter Angreifer wird vom Server abgelehnt"),
            ("MEHRSPIELER-04", "Gruppenleitung geht an ein Online-Mitglied"),
            // 22.10.2026 (Runde 4, DuoTestDirector.Spielablauf3.Runde4.cs)
            ("REL-02", "F10 startet im gebauten Spiel keinen Selbsttest an der eigenen Figur"),
            ("SPEICHERN-02", "„Speichern und beenden“: Antwort binnen 2 s, Menü zu setzt die Bitte zurück, Tod mit Grund"),
            ("REL-01", "Speichern beim Beenden: Stelle aus OnApplicationQuit bleibt beim Abbau stehen"),
            // 22.10.2026 (Runde 6, DuoTestDirector.Spielablauf3.Runde6.cs)
            ("LADEN-01", "Lade-Bereinigung: fehlende Definition erhält, nur feste Altlasten und erledigter Überschuss gehen"),
            ("WESTWEIDEN-02", "Gerlach in Langer Rain handelt (Heiltränke, Brot, Tee)"),
            ("WESTWEIDEN-03", "Rotkehl wirft Ausrüstung ab Stufe 9"),
            ("FORTSCHRITT-08", "Westweiden-Elite und Endgegner werfen Ausrüstung ab Stufe 9"),
            ("INSTANZEN-04", "Ring-Anzeige bleibt beim „reset“ einer Begegnung im selben Prozess"),
            ("FORTSCHRITT-06", "Rollender Donner verstärkt Donnerschlag wie im Tooltip"),
            ("FORTSCHRITT-07", "Mana-Anteil bleibt beim Umrüsten (kein Auffüllen)"),
            ("STORY-05", "Eigener Tod bricht Dominieren ab, die Betäubung des Ziels endet mit (23.10.)"),
            ("STORY-01", "Aufgegebene Pferde-Aufträge sind wieder anbietbar"),
            ("STORY-02", "Unterbrochene Schluss-Szene q2_final wird beim Login fortgesetzt"),
            ("STORY-04", "Einzigartiges Fundstück nach Aufgeben: bleibt genommen, keine „Kein Platz“-Meldung"),
            ("WELTZELLEN-03", "Sichere Orte in den Westweiden: sofort speichern und beenden"),
            ("WELTZELLEN-01", "Meer vor den Westweiden hält die Zelle geladen"),
            ("WELTZELLEN-02", "Gerufene der Alarmglocke gehen mit ihrer Zelle"),
            ("CODEX-A2", "NetworkBootstrap: Host+Trennen / Host+Verbinden im selben Bild; Host nach erfolglosem Verbinden verbindet lokal (23.10.)"),
        };

        private IEnumerator Spielablauf3Suite()
        {
            _sp3.Clear();
            _sp3Reihenfolge.Clear();
            _sp3Bilder.Clear();
            _sp3Fehlertexte.Clear();
            _sp3Id = null;
            _uiFehlendeHaken.Clear();
            foreach ((string id, string titel) in Sp3Titel) Sp3Anlegen(id, titel);

            Line("");
            Line("[Vorbereitung]");
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(Spielablauf3Profile)) != null, 40f);
            Check(a != null, "Spieler angemeldet (Testprofil, Kareth v4)");
            if (a == null) yield break;
            var pa = a.GetComponent<NetworkProgression>();
            Health hp = a.GetComponent<Health>();
            if (pa == null || hp == null) { Check(false, "Spieler hat Fortschritt und Leben"); yield break; }
            hp.SetInvulnerableFor(7200f);
            yield return new WaitForSecondsRealtime(3f);
            yield return WaitUntil(() => !Ashveil.Gameplay.Story.StoryDirector.Active, 25f);
            QuestUi ui = FindAnyObjectByType<QuestUi>();
            IProgressionService svc = ProgressionClient.Current;
            Check(ui != null && svc != null && svc.Ready, "Auftragsoberfläche und Fortschritt des Editor-Spielers bereit");
            if (ui == null || svc == null) yield break;
            Info($"Spielansicht {Screen.width} × {Screen.height} px; Testfigur {a.ServerModel.Class?.DisplayName}, Stufe {a.ServerModel.Level}; Fenster {(Application.isFocused ? "mit" : "ohne")} Fokus");

            string ordner = Ashveil.Core.ProjectPaths.Logs("Spielablauf3");
            Directory.CreateDirectory(ordner);
            foreach (string alt in Directory.GetFiles(ordner, "*.png"))
                try { File.Delete(alt); } catch (IOException) { }

            EventBus<CombatTextEvent>.Subscribe(Sp3Kampftext);
            Application.logMessageReceived += Sp3LogMit;
            try
            {
                Sp3Daten(a);
                yield return Sp3Eingabe(a);
                yield return Sp3Handel(a, pa, ui, svc);
                yield return Sp3Kampf(a, pa, hp);
                yield return Sp3LeicheUndGrete(a, pa, svc);
                yield return Sp3Tod(a, pa, hp);
                yield return Sp3Weber(a, pa, hp, svc);
                yield return Sp3Runde4(a, hp);   // 22.10.2026: REL-02 (F10), SPEICHERN-02
                yield return Sp3Runde6(a, pa, hp);   // 22.10.2026 (Runde 6): zuletzt CODEX-A2 – baut den Host ab und wieder auf
            }
            finally
            {
                EventBus<CombatTextEvent>.Unsubscribe(Sp3Kampftext);
                Application.logMessageReceived -= Sp3LogMit;
                Sp3EingabeAus();
                NetworkWorldTime.ServerJumpTo(15.5f);
            }
            Sp3Punkt(null, null);
            Sp3Zusammenfassung();
        }

        // ================================================================== 1. Daten (Server-Daten, wie sie im Spiel geladen sind)

        private void Sp3Daten(NetworkInventory a)
        {
            Line("");
            Line("[1 Daten im laufenden Spiel (DefinitionDatabase)]");
            List<NpcDefinition> npcs = GameData.Database.All.OfType<NpcDefinition>().ToList();
            var szene = new HashSet<string>(NpcIdentity.All.Where(n => n != null).Select(n => n.NpcId));

            // ---------------------------------------------------------- FORTSCHRITT-02
            Sp3Punkt("FORTSCHRITT-02", "Bündelpreis: Standardpreis je Stück × Menge; kein Angebot ist billiger als sein Verkaufswert");
            ItemDefinition pulver = GameData.Item("item.black_powder");
            if (pulver != null)
            {
                int p5 = Progression.BuyPrice(pulver, 0, 5), p1 = Progression.BuyPrice(pulver, 0, 1);
                Check(p5 == pulver.sellPrice * 4 * 5 && p5 == 5 * p1 && Progression.BuyPrice(pulver, 0) == p1,
                    $"Schwarzpulver ×5 ohne festen Preis: {p5}k (= 5 × {p1}k; Verkauf des Stapels {pulver.sellPrice * 5}k)");
            }
            else Check(false, "item.black_powder vorhanden");
            ItemDefinition pfeil = GameData.Item("item.rough_arrow");
            if (pfeil != null) Check(Progression.BuyPrice(pfeil, 20, 200) == 20, $"Fester Preis gilt weiter für das ganze Angebot (Grobe Pfeile ×200 für 20k: {Progression.BuyPrice(pfeil, 20, 200)}k)");
            var quellen = new List<string>();
            int angebote = 0;
            foreach (NpcDefinition n in npcs.Where(n => n.vendor && n.stock != null))
                foreach (VendorEntry e in n.stock)
                {
                    if (e == null || e.item == null || !Progression.Sellable(e.item)) continue;
                    angebote++;
                    int kauf = Progression.BuyPrice(e.item, e.price, e.count), verkauf = e.item.sellPrice * e.count;
                    if (kauf < verkauf) quellen.Add($"{n.Id}: {e.item.Id} ×{e.count} kauf {kauf}k < verkauf {verkauf}k");
                }
            Check(angebote > 10 && quellen.Count == 0, $"Keine Geldquelle: {angebote} verkäufliche Angebote aller Händler, Kauf ≥ Verkauf" +
                                                    (quellen.Count > 0 ? " – VERLETZT: " + string.Join("; ", quellen) : ""));
            var buendel = npcs.Where(n => n.vendor && n.stock != null).SelectMany(n => n.stock.Where(e => e != null && e.item != null && e.count > 1 && e.price <= 0)
                .Select(e => $"{n.Name} ({n.Id}{(szene.Contains(n.Id) ? ", Kareth v4" : ", nicht in Kareth v4")}): {e.item.DisplayName} ×{e.count} = {Progression.BuyPrice(e.item, e.price, e.count)}k")).ToList();
            Info("Bündel ohne festen Preis: " + (buendel.Count > 0 ? string.Join(" · ", buendel) : "keine"));

            // ---------------------------------------------------------- FORTSCHRITT-03 / -05
            NpcDefinition maelis = GameData.Npc("npc.maelis"), idun = GameData.Npc(KC.Idun);
            Sp3Punkt("FORTSCHRITT-03", "Leinenfetzen nicht im Angebot von Maelis und Idun; kein Händler verkauft, was seine eigenen Aufträge sammeln lassen");
            Check(maelis != null && idun != null, $"Maelis ({maelis?.stock.Count} Waren) und Idun ({idun?.stock.Count} Waren) geladen");
            if (maelis != null && idun != null)
                Check(!Sp3Fuehrt(maelis, "item.linen_scraps") && !Sp3Fuehrt(idun, "item.linen_scraps"),
                    $"Leinenfetzen: Maelis {(Sp3Fuehrt(maelis, "item.linen_scraps") ? "führt sie" : "nein")}, Idun {(Sp3Fuehrt(idun, "item.linen_scraps") ? "führt sie" : "nein")}");
            var selbst = new List<string>();
            foreach (QuestDefinition q in GameData.Quests.Where(q => q != null))
                foreach (QuestObjective o in q.objectives.Where(o => o != null && o.type == QuestObjectiveType.Collect))
                    foreach (string npcId in new[] { q.giverNpc, q.TurnInNpc }.Distinct())
                        if (GameData.Npc(npcId) is NpcDefinition n && n.vendor && Sp3Fuehrt(n, o.targetId)) selbst.Add($"{q.Id}: {n.Id} verkauft {o.targetId}");
            Check(selbst.Count == 0, "Kein Händler verkauft einen Gegenstand, den ein Sammelziel seiner eigenen Aufträge verlangt" + (selbst.Count > 0 ? " – " + string.Join("; ", selbst) : ""));

            Sp3Punkt("FORTSCHRITT-05", "Rezept: Heiltrank nicht bei Maelis; kein Händler in Kareth v4 verkauft Rezepte");
            if (maelis != null)
                Check(!Sp3Fuehrt(maelis, "item.recipe_healing_draught") && maelis.stock.Count > 0 && maelis.stock[0]?.item?.Id == "item.healing_draught",
                    $"Maelis ohne Rezept, Heiltrank bleibt erstes Angebot ({string.Join(", ", maelis.stock.Where(e => e?.item != null).Select(e => e.item.DisplayName))})");
            var rezepte = npcs.Where(n => n.vendor && szene.Contains(n.Id)).SelectMany(n => n.stock.Where(e => e?.item != null && e.item.teachesRecipe != null).Select(e => $"{n.Id}: {e.item.Id}")).ToList();
            Check(rezepte.Count == 0, $"Händler in Kareth v4 ({npcs.Count(n => n.vendor && szene.Contains(n.Id))}) ohne Rezepte" + (rezepte.Count > 0 ? " – " + string.Join("; ", rezepte) : ""));

            // ---------------------------------------------------------- AUFTRAEGE-03 / FORTSCHRITT-04
            Sp3Punkt("AUFTRAEGE-03", "Keine Beutetabelle führt Questgegenstände ohne Auftrag; Verblichener, Schützin, Glockenhirte je 500 Würfe");
            var tabellen = GameData.Database.All.OfType<LootTableDefinition>().ToList();
            var ohneAuftrag = new List<string>();
            foreach (LootTableDefinition t in tabellen)
            {
                IEnumerable<ItemDefinition> items = t.entries.Select(e => e.item).Concat(t.groups.Where(g => g != null).SelectMany(g => g.items.Select(w => w.item)));
                foreach (ItemDefinition d in items.Where(d => d != null).Distinct())
                    if (d.category == ItemCategory.Quest && (string.IsNullOrEmpty(d.questId) || GameData.Quest(d.questId) == null)) ohneAuftrag.Add($"{t.Id}: {d.Id}");
            }
            Check(tabellen.Count > 5 && ohneAuftrag.Count == 0, $"{tabellen.Count} Beutetabellen ohne Questgegenstand ohne Auftrag" + (ohneAuftrag.Count > 0 ? " – " + string.Join("; ", ohneAuftrag) : ""));
            CharacterClassDefinition warden = GameData.Class(Sp3Warden);
            var rng = new System.Random(2110);
            foreach (string tid in new[] { "loot.faded_one", "loot.faded_gunner", "loot.bell_shepherd" })
            {
                LootTableDefinition t = GameData.Get<LootTableDefinition>(tid);
                if (t == null) { Check(false, $"Beutetabelle {tid} geladen"); continue; }
                int splitter = 0, brief = 0, stapel = 0;
                for (int i = 0; i < 500; i++)
                    foreach (ItemStack s in LootGenerator.Roll(t, warden, rng))
                    {
                        stapel++;
                        if (s.id == "item.bell_shard") splitter += s.count;
                        if (s.id == "item.faded_letter") brief += s.count;
                    }
                Check(splitter == 0 && brief == 0 && stapel > 0, $"{tid}: 500 Würfe, {stapel} Stapel Beute, {splitter} Glockensplitter, {brief} Verblichene Briefe");
            }
            ItemDefinition shard = GameData.Item("item.bell_shard"), letter = GameData.Item("item.faded_letter");
            Check(shard != null && letter != null && shard.category == ItemCategory.Quest && letter.category == ItemCategory.Quest,
                "Gegenstandsdefinitionen bleiben (Questgegenstand; ui_auftraege J und Progress legen sie per Testweg an)");
            Sp3Punkt("FORTSCHRITT-04", "dieselben Prüfungen wie AUFTRAEGE-03 (gleiche Ursache, gleicher Fix)");
            Check(ohneAuftrag.Count == 0, "Siehe AUFTRAEGE-03: kein Glockensplitter und kein Verblichener Brief mehr in einer Beutetabelle");

            // ---------------------------------------------------------- AUFTRAEGE-02 (Regel; im Spiel unten bei der Weber-Probe)
            Sp3Punkt("AUFTRAEGE-02", "Belohnungsregel QuestRules.ForClass: Munition nur für Klassen, die sie verschießen können");
            CharacterClassDefinition weaver = GameData.Class(Sp3Weaver), stalker = GameData.Class(Sp3Stalker);
            if (warden != null && weaver != null && stalker != null)
            {
                int weberMuni = 0, jaegerMuni = 0, waechterMuni = 0, falsch = 0;
                var falschListe = new List<string>();
                foreach (QuestDefinition q in GameData.Quests.Where(q => q != null))
                    foreach (CharacterClassDefinition c in new[] { warden, weaver, stalker })
                        foreach (QuestItemReward r in QuestRules.ForClass(q.rewardItems, c).Concat(QuestRules.ForClass(q.choiceItems, c)))
                        {
                            if (r?.item == null || !r.item.IsAmmo) continue;
                            if (c == weaver) weberMuni++;
                            if (c == stalker) jaegerMuni++;
                            if (c == warden) waechterMuni++;
                            if (!c.CanUseWeapon(r.item.ammoType == AmmoType.Arrow ? WeaponClass.Bow : WeaponClass.Crossbow)) { falsch++; falschListe.Add($"{q.Id}→{c.Id}"); }
                        }
                Check(weberMuni == 0 && falsch == 0, $"Aschenweber: {weberMuni} Munitionsbelohnungen (Soll 0); unbrauchbare Munition insgesamt {falsch}" + (falsch > 0 ? " – " + string.Join(", ", falschListe) : ""));
                Check(jaegerMuni > 0 && waechterMuni > 0, $"Gegenprobe: Aschenjäger ({jaegerMuni}) und Eidwächter ({waechterMuni}) bekommen Pfeile weiter");
            }
            else Check(false, "Klassen Eidwächter, Aschenweber, Aschenjäger geladen");

            // ---------------------------------------------------------- AUFTRAEGE-Q05 (Nachbesserung 21.10.)
            Sp3Punkt("AUFTRAEGE-Q05", "„Federn für die Wacht“: Pfeil-Belohnung passt zur Stufe (Grobe Pfeile ×200 statt Scharfe Pfeile mit Mindeststufe 8)");
            QuestDefinition q5 = GameData.Quest("quest.powder_for_the_watch");
            if (q5 == null) Check(false, "quest.powder_for_the_watch geladen");
            else
            {
                List<QuestItemReward> q5Pfeile = q5.rewardItems.Where(r => r?.item != null && r.item.IsAmmo).ToList();
                int stufe5 = Math.Max(1, q5.minLevel);
                bool zuHoch = q5.rewardItems.Concat(q5.choiceItems).Any(r => r?.item != null && r.item.requiredLevel > stufe5);
                Check(q5Pfeile.Count == 1 && q5Pfeile[0].item.Id == "item.rough_arrow" && q5Pfeile[0].count == 200 && !zuHoch,
                    $"Q05 (ab Stufe {q5.minLevel}): {string.Join(", ", q5.rewardItems.Where(r => r?.item != null).Select(r => $"{r.item.DisplayName} ×{r.count} (ab Stufe {r.item.requiredLevel})"))}");
                // nur Übersicht, keine Prüfung: andere Aufträge, deren feste Belohnung deutlich über der Annahmestufe liegt
                var spaeter = GameData.Quests.Where(q => q != null && q != q5)
                    .SelectMany(q => q.rewardItems.Where(r => r?.item != null && r.item.requiredLevel > Math.Max(1, q.minLevel) + 2)
                        .Select(r => $"{q.Id} (ab {q.minLevel}): {r.item.Id} ab {r.item.requiredLevel}")).ToList();
                Info($"Übersicht feste Belohnungen > Annahmestufe + 2: {(spaeter.Count == 0 ? "keine" : string.Join(" · ", spaeter.Take(10)) + (spaeter.Count > 10 ? $" … (+{spaeter.Count - 10})" : ""))}");
            }

            // ---------------------------------------------------------- KAMPF-02 (Daten, Nachbesserung 21.10.: Westweiden-Schützen)
            Sp3Punkt("KAMPF-02", "Daten: jeder Fernkämpfer erreicht aus seinem Wunschabstand (+2 m, ab da läuft er heran) sein Ziel");
            List<EnemyDefinition> fern = GameData.Database.All.OfType<EnemyDefinition>().Where(d => d != null && d.style == EnemyStyle.Ranged).ToList();
            List<string> zuKurz = fern.Where(d => d.attackRange < d.preferredDistance + 2f).Select(d => $"{d.Id} {d.attackRange:0.#} m").ToList();
            Check(fern.Count >= 6 && zuKurz.Count == 0 && fern.Any(d => d.Id == "enemy.ww_heckenschuetze") && fern.Any(d => d.Id == "enemy.ww_kellerschuetze"),
                $"{fern.Count} Fernkämpfer, Reichweite/Wunschabstand: {string.Join(", ", fern.OrderBy(d => d.Id, StringComparer.Ordinal).Select(d => $"{d.Id} {d.attackRange:0.#}/{d.preferredDistance:0.#}"))}" +
                (zuKurz.Count > 0 ? " – ZU KURZ: " + string.Join("; ", zuKurz) : ""));

            // ---------------------------------------------------------- MEHRSPIELER-04
            Sp3Punkt("MEHRSPIELER-04", "Logik von PartyService.Leave mit dem Online-Prädikat (NetworkSocial.IsOnline): Leitung an das älteste Online-Mitglied");
            MethodInfo online = typeof(NetworkSocial).GetMethod("IsOnline", BindingFlags.NonPublic | BindingFlags.Static);
            if (online == null) { _uiFehlendeHaken.Add("NetworkSocial.IsOnline"); Check(false, "NetworkSocial.IsOnline vorhanden"); }
            else
            {
                var istOnline = (Predicate<string>)Delegate.CreateDelegate(typeof(Predicate<string>), online);
                string ich = a.ProfileId, chef = "sp3-leitung", weg = "sp3-offline";
                Check(istOnline(ich) && !istOnline(weg) && PartyService.Of(ich) == null, $"Server: {ich} online, {weg} offline, {ich} in keiner Gruppe");
                string e1 = PartyService.Invite(chef, weg);
                PartyService.Accept(weg, out string f1);
                string e2 = PartyService.Invite(chef, ich);
                PartyService.Party p = PartyService.Accept(ich, out string f2);
                bool aufbau = e1 == null && e2 == null && f1 == null && f2 == null && p != null && p.Leader == chef && p.Members.SequenceEqual(new[] { chef, weg, ich });
                Check(aufbau, $"Gruppe: Leitung {p?.Leader}, Mitglieder {string.Join(", ", p?.Members ?? new List<string>())} (älteres Mitglied {weg} ist offline)");
                PartyService.Party rest = PartyService.Leave(chef, istOnline);
                Check(aufbau && rest != null && rest.Leader == ich, $"Leitung verlässt die Gruppe → neue Leitung „{rest?.Leader}“ (Soll {ich}, online; nicht {weg})");
                PartyService.Leave(ich);
                PartyService.Leave(weg);
                // Gegenprobe ohne Online-Angabe: altes Verhalten (ältestes Mitglied)
                PartyService.Invite(chef, weg); PartyService.Accept(weg, out _);
                PartyService.Invite(chef, ich); PartyService.Accept(ich, out _);
                PartyService.Party alt = PartyService.Leave(chef);
                Check(alt != null && alt.Leader == weg, $"Gegenprobe ohne Online-Angabe: Leitung „{alt?.Leader}“ (ältestes Mitglied, wie bisher)");
                PartyService.Leave(ich);
                PartyService.Leave(weg);
                Check(PartyService.Of(ich) == null && PartyService.Of(weg) == null && PartyService.Of(chef) == null, "Testgruppen wieder aufgelöst");
                Sp3NichtGeprueft("MEHRSPIELER-04", "Verdrahtung NetworkSocial.ServerLeave/ServerKick → PartyService.Leave(…, IsOnline) nicht durchlaufen (bräuchte ein zweites Online-Mitglied; nur nachgelesen) und echte Clients (B getrennt, A verlässt, C lädt ein) – Allein-Suite; geprüft ist die Logik von PartyService mit dem IsOnline der echten NetworkSocial-Tabelle");
            }

            // ---------------------------------------------------------- EINSTIEG-03 (Zustand der eigenen Figur)
            Sp3Punkt("EINSTIEG-03", "Körpertaste F2 an der eigenen, vernetzten Figur aus – BecomeLocal ohne #if (Editor = Development-/Spieltest-Build = Release)");
            CharacterBodySwitcher koerper = a.GetComponentInChildren<CharacterBodySwitcher>(true);
            Check(koerper != null && !koerper.DebugSwitchAllowed,
                $"Eigene Figur nach BecomeLocal: F2 erlaubt {koerper?.DebugSwitchAllowed} (Soll False; die Suite setzt dafür nichts)");
            FieldInfo reserviert = typeof(KeybindUi).GetField("Reserved", BindingFlags.NonPublic | BindingFlags.Static);
            if (reserviert == null) _uiFehlendeHaken.Add("KeybindUi.Reserved");
            else Check(reserviert.GetValue(null) is IDictionary<string, string> res && !res.ContainsKey("Key:F2") && res.ContainsKey("Key:F1"),
                "Tastenbelegung warnt nicht mehr vor F2 („Körper“) – die Taste ist frei belegbar; F1 bleibt reserviert");
            Sp3NichtGeprueft("EINSTIEG-03", "gebauter Spieltest-Build r2 (Development) nicht gestartet – BecomeLocal hat keinen #if-Zweig mehr, der Editor durchläuft denselben Code");

            // ---------------------------------------------------------- EINSTIEG-01 (Reihenfolge)
            Sp3Punkt("EINSTIEG-01", "Ausführungsreihenfolge: Charakter-Editor liest den Tastaturpuffer vor dem Chat");
            int eo(Type typ) => typ.GetCustomAttribute<DefaultExecutionOrder>()?.order ?? 0;
            int ed = eo(typeof(CharacterCreatorUi)), so = eo(typeof(SocialUi)), qo = eo(typeof(QuestUi)), io = eo(typeof(InventoryUi));
            Check(ed < so && ed > qo && ed > io, $"Charakter-Editor {ed} < Chat {so}; nach Aufträgen {qo} und Inventar {io}");
        }

        private static bool Sp3Fuehrt(NpcDefinition n, string itemId) => n != null && n.stock != null && n.stock.Any(e => e != null && e.item != null && e.item.Id == itemId);

        // ================================================================== 2. Eingabe (echte Ereignisse)

        private IEnumerator Sp3Eingabe(NetworkInventory a)
        {
            Line("");
            Line("[2 Eingabe über echte Tastatur-/Text-Ereignisse]");
            string zustand = Sp3EingabeAn();
            Info("Eingabe: " + zustand);
            Keyboard kb = Keyboard.current;
            bool tastatur = kb != null && kb.enabled;

            // ---------------------------------------------------------- EINSTIEG-01
            Sp3Punkt("EINSTIEG-01", "Charakter-Editor offen: „Maxim“ über den Tastaturpuffer (TextEvent) → steht im Namensfeld, Chat bleibt zu");
            CharacterCreatorUi editor = CharacterCreatorUi.Instance;
            SocialUi chat = SocialUi.Instance;
            if (editor == null || chat == null) { Check(false, $"Charakter-Editor ({editor != null}) und Chat ({chat != null}) vorhanden"); }
            else
            {
                var dienst = new Sp3KeinErstellen();
                CharacterCreationClient.RaiseNeeded(dienst, "");
                yield return UiTakt();
                editor.SelectCategory(CharacterCreatorUi.Category.Name);
                yield return UiTakt();
                Check(editor.IsOpen && editor.NameText.Length == 0 && UiPointer.KeyboardCaptured, $"Editor offen, Namensfeld leer, Spieltasten ruhen (KeyboardCaptured {UiPointer.KeyboardCaptured})");
                string weg = RawInput.TestTextEingeben("Maxim");
                for (int i = 0; i < 4; i++) yield return null;
                bool chatOffen = UiFeld(chat, "_typing") is bool t && t;
                Check(weg.Length > 0 && editor.NameText == "Maxim" && !chatOffen,
                    $"Getippt über „{weg}“: Namensfeld „{editor.NameText}“ (Soll „Maxim“), Chat {(chatOffen ? "OFFEN" : "zu")}");
                yield return Sp3Foto("einstieg01_namensfeld");
                CharacterCreationClient.RaiseResult(true, "Maxim");   // schließt den Editor wie eine Server-Bestätigung (Testdienst hat nichts erstellt)
                yield return UiTakt();
                Check(!editor.IsOpen && dienst.Aufrufe == 0 && !UiPointer.KeyboardCaptured && !UiPointer.GameBlocked && !CharacterCreationClient.Pending,
                    "Editor wieder zu, nichts an den Server geschickt, Spieltasten frei");
            }

            // ---------------------------------------------------------- EINSTIEG-02
            Sp3Punkt("EINSTIEG-02", "„/“ einer belegten Spieltaste: Num / kurz und gehalten (Gangart wechselt), Umschalt+7 (Leistenplatz 7) – Chat bleibt zu; „/“ allein und Enter öffnen ihn");
            PlayerCharacter pc = a.GetComponent<PlayerCharacter>();
            PlayerInputReader input = pc != null ? pc.Input : a.GetComponent<PlayerInputReader>();
            string code = InputBindingsStore.Current.walkKey;
            bool hatTaste = Sp3Taste(code, out Key gehen);
            if (!tastatur || input == null || chat == null || !hatTaste)
            {
                Check(input != null && chat != null && hatTaste, $"Eingabeleser, Chat und Gehen-Taste ({code}) vorhanden");
                if (!tastatur) Sp3NichtGeprueft("EINSTIEG-02", "Tastatur des Input Systems ließ sich ohne Fensterfokus nicht einschalten");
            }
            else
            {
                Info($"Belegung Gehen: {code}");
                bool gehen0 = input.WalkMode;
                RawInput.TestTextEingeben("/");
                InputSystem.QueueStateEvent(kb, new KeyboardState(gehen));
                yield return null;
                bool kam = RawInput.WasPressed(GameKey.WalkToggle);
                bool chatAuf = UiFeld(chat, "_typing") is bool t1 && t1;
                bool gehen1 = input.WalkMode;
                InputSystem.QueueStateEvent(kb, new KeyboardState());
                yield return null;
                yield return null;
                if (!kam)
                {
                    Info("Tastendruck kam in diesem Bild nicht im Spiel an (Input System ohne Fokus)");
                    Sp3NichtGeprueft("EINSTIEG-02", "der eingespeiste Tastendruck kam nicht im Spiel an");
                }
                else
                {
                    Check(!chatAuf && !UiPointer.KeyboardCaptured, $"Taste {gehen} + Text „/“ im selben Bild: Chat {(chatAuf ? "OFFEN" : "zu")}, KeyboardCaptured {UiPointer.KeyboardCaptured}");
                    Check(gehen1 != gehen0, $"Gangart umgeschaltet: Gehen {gehen0} → {gehen1}");
                }
                UiSetze(input, "_walkMode", gehen0);
                MethodInfo stop = typeof(SocialUi).GetMethod("StopTyping", BindingFlags.NonPublic | BindingFlags.Instance);
                if (stop == null) _uiFehlendeHaken.Add("SocialUi.StopTyping");

                // Nachbesserung 21.10.: Num / GEHALTEN – Windows wiederholt das Zeichen „/“ ohne neue Druckflanke (Tastenwiederholung)
                RawInput.TestTextEingeben("/");
                InputSystem.QueueStateEvent(kb, new KeyboardState(gehen));
                yield return null;
                bool kamH = RawInput.WasPressed(code);
                bool offenH = UiFeld(chat, "_typing") is bool th0 && th0;
                for (int i = 0; i < 3; i++)
                {
                    RawInput.TestTextEingeben("/");   // nur Text, Taste weiter gehalten
                    yield return null;
                    offenH |= UiFeld(chat, "_typing") is bool th && th;
                }
                bool gehalten = RawInput.IsHeld(code);
                InputSystem.QueueStateEvent(kb, new KeyboardState());
                yield return null;
                yield return null;
                offenH |= UiFeld(chat, "_typing") is bool th1 && th1;
                if (!kamH) Sp3NichtGeprueft("EINSTIEG-02", "gehaltene Taste: der eingespeiste Tastendruck kam nicht im Spiel an");
                else Check(!offenH && gehalten && !UiPointer.KeyboardCaptured,
                    $"Num / gehalten, dabei 3 Bilder lang weitere „/“ ohne neue Druckflanke (Tastenwiederholung): Chat {(offenH ? "OFFEN" : "zu")}, Taste gehalten {gehalten}");
                if (offenH) stop?.Invoke(chat, null);
                UiSetze(input, "_walkMode", gehen0);
                yield return UiTakt();

                // Nachbesserung 21.10.: Umschalt+7 auf der deutschen Tastatur = „/“; beim Sprinten (Umschalt gehalten) soll Leistenplatz 7
                // auslösen, nicht der Chat aufgehen
                string[] plaetze = InputBindingsStore.Current.slots;
                string platz7 = plaetze != null && plaetze.Length > 6 ? plaetze[6] : "";
                if (!Sp3Taste(platz7, out Key sieben)) Sp3NichtGeprueft("EINSTIEG-02", $"Umschalt+7: Leistenplatz 7 liegt auf keiner Tastatur-Taste ({platz7})");
                else
                {
                    int ausgeloest0 = ActionBarInput.TriggerCount;
                    ActionBarEntry eintrag7 = ActionBarClient.Get(ActionBarInput.ActivePage, 6);
                    RawInput.TestTextEingeben("/");
                    InputSystem.QueueStateEvent(kb, new KeyboardState(Key.LeftShift, sieben));
                    yield return null;
                    bool kam7 = RawInput.WasPressed(platz7);
                    bool chat7 = UiFeld(chat, "_typing") is bool t7 && t7;
                    bool frei7 = !UiPointer.KeyboardCaptured && UiPointer.HotkeysAllowed;
                    InputSystem.QueueStateEvent(kb, new KeyboardState());
                    yield return null;
                    yield return null;
                    int ausgeloest = ActionBarInput.TriggerCount - ausgeloest0;
                    if (!kam7) Sp3NichtGeprueft("EINSTIEG-02", "Umschalt+7: der eingespeiste Tastendruck kam nicht im Spiel an");
                    else Check(!chat7 && frei7,
                        $"Umschalt + {platz7} (Leistenplatz 7) + Text „/“ im selben Bild: Chat {(chat7 ? "OFFEN" : "zu")}, Spieltasten frei {frei7}");
                    Info($"Leistenplatz 7: {(eintrag7.IsEmpty ? "leer" : eintrag7.referenceId)}, Leiste löste {ausgeloest}× aus (ohne Fensterfokus nimmt die Leiste keine Tasten an: Fokus {Application.isFocused})");
                    if (chat7) stop?.Invoke(chat, null);
                    yield return UiTakt();
                }

                // Gegenprobe: nur der Text „/“ (ohne Spieltaste, z. B. US-Tastatur) öffnet den Chat weiter
                RawInput.TestTextEingeben("/");
                yield return null;
                yield return null;
                bool chatJetzt = UiFeld(chat, "_typing") is bool t2 && t2;
                string zeile = UiFeld(chat, "_input")?.ToString() ?? "";
                Check(chatJetzt && zeile == "/" && UiPointer.KeyboardCaptured, $"Gegenprobe nur „/“ (ohne Spieltaste): Chat offen mit „{zeile}“");
                stop?.Invoke(chat, null);
                yield return UiTakt();
                Check(!(UiFeld(chat, "_typing") is bool t3 && t3) && !UiPointer.KeyboardCaptured, "Chat wieder zu");

                // Gegenprobe Enter: öffnet den Chat wie bisher
                InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Enter));
                yield return null;
                bool enterKam = RawInput.EnterPressed;
                bool chatEnter = UiFeld(chat, "_typing") is bool t4 && t4;
                string zeileEnter = UiFeld(chat, "_input")?.ToString() ?? "";
                InputSystem.QueueStateEvent(kb, new KeyboardState());
                yield return null;
                yield return null;
                if (!enterKam) Sp3NichtGeprueft("EINSTIEG-02", "Enter: der eingespeiste Tastendruck kam nicht im Spiel an");
                else Check(chatEnter && zeileEnter.Length == 0, $"Gegenprobe Enter: Chat {(chatEnter ? "offen" : "ZU")}, Zeile „{zeileEnter}“");
                stop?.Invoke(chat, null);
                yield return UiTakt();
                Check(!(UiFeld(chat, "_typing") is bool t5 && t5) && !UiPointer.KeyboardCaptured, "Chat wieder zu");
            }

            // ---------------------------------------------------------- EINSTIEG-03
            Sp3Punkt("EINSTIEG-03", "Echter F2-Druck an der eigenen Figur im Zustand nach BecomeLocal (die Suite setzt nichts) → kein Körperwechsel");
            CharacterBodySwitcher koerper = a.GetComponentInChildren<CharacterBodySwitcher>(true);
            if (!tastatur || koerper == null) Sp3NichtGeprueft("EINSTIEG-03", tastatur ? "kein CharacterBodySwitcher an der Figur" : "Tastatur ohne Fokus nicht verfügbar");
            else
            {
                bool erlaubt = koerper.DebugSwitchAllowed;
                int index0 = koerper.ActiveIndex, wuensche = 0;
                Action<int> zaehlen = _ => wuensche++;
                koerper.BodyChangeRequested += zaehlen;
                try
                {
                    InputSystem.QueueStateEvent(kb, new KeyboardState(Key.F2));
                    yield return null;
                    bool kam = RawInput.WasPressed(GameKey.SwitchBody);
                    InputSystem.QueueStateEvent(kb, new KeyboardState());
                    yield return new WaitForSecondsRealtime(0.5f);
                    if (!kam) Sp3NichtGeprueft("EINSTIEG-03", "der eingespeiste F2-Druck kam nicht im Spiel an");
                    else Check(!erlaubt && wuensche == 0 && koerper.ActiveIndex == index0,
                        $"F2 kam an (RawInput.WasPressed), F2 erlaubt {erlaubt}, kein Körperwechsel: Wünsche {wuensche}, Körper {koerper.ActiveIndex} (vorher {index0}, {koerper.ActiveName})");
                }
                finally
                {
                    koerper.BodyChangeRequested -= zaehlen;
                }
            }
        }

        private sealed class Sp3KeinErstellen : ICharacterCreationService
        {
            public int Aufrufe;
            public void Submit(string name, CharacterAppearanceData look, string classId) => Aufrufe++;
        }

        /// <summary>
        /// Eingabe-Ereignisse auch ohne Fensterfokus zulassen: Einstellung des Input Systems vorübergehend auf „Fokus ignorieren /
        /// alle Eingaben gehen an die Spielansicht“ und Tastatur/Maus einschalten (ohne Fokus schaltet das Input System sie ab und
        /// verwirft eingereihte Ereignisse). Wird am Ende zurückgestellt – nur die Werte, kein Austausch des Einstellungsobjekts.
        /// </summary>
        private string Sp3EingabeAn()
        {
            InputSettings s = InputSystem.settings;
            if (s == null) return "keine Input-System-Einstellungen";
            bool fokus = Application.isFocused;
            if (!_sp3EingabeUmgestellt)
            {
                _sp3AltHintergrund = s.backgroundBehavior;
                _sp3AltEditor = s.editorInputBehaviorInPlayMode;
                s.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                s.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                _sp3EingabeUmgestellt = true;
            }
            Keyboard kb = Keyboard.current;
            Mouse m = Mouse.current;
            bool kbAus = kb != null && !kb.enabled, mAus = m != null && !m.enabled;
            if (kbAus) InputSystem.EnableDevice(kb);
            if (mAus) InputSystem.EnableDevice(m);
            return $"Fenster {(fokus ? "mit" : "ohne")} Fokus; vorübergehend „Fokus ignorieren“ (vorher {_sp3AltHintergrund}/{_sp3AltEditor}); " +
                   $"Tastatur {(kb == null ? "fehlt" : kb.enabled ? (kbAus ? "eingeschaltet" : "an") : "AUS")}, Maus {(m == null ? "fehlt" : m.enabled ? (mAus ? "eingeschaltet" : "an") : "AUS")}";
        }

        private void Sp3EingabeAus()
        {
            if (!_sp3EingabeUmgestellt) return;
            _sp3EingabeUmgestellt = false;
            InputSettings s = InputSystem.settings;
            if (s == null) return;
            if (Keyboard.current != null && Keyboard.current.enabled) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            s.backgroundBehavior = _sp3AltHintergrund;
            s.editorInputBehaviorInPlayMode = _sp3AltEditor;
        }

        /// <summary>„Key:NumpadDivide“ → Key.NumpadDivide.</summary>
        private static bool Sp3Taste(string code, out Key key)
        {
            key = Key.None;
            return !string.IsNullOrEmpty(code) && code.StartsWith("Key:", StringComparison.Ordinal) && Enum.TryParse(code.Substring(4), out key) && key != Key.None;
        }

        private static bool Sp3Maus(Vector2 position, bool rechts)
        {
            Mouse m = Mouse.current;
            if (m == null || !m.enabled) return false;
            var st = new MouseState { position = position };
            if (rechts) st = st.WithButton(MouseButton.Right);
            InputSystem.QueueStateEvent(m, st);
            return true;
        }

        /// <summary>Echter Rechtsklick (Drücken, Loslassen) an <paramref name="p"/>; gibt zurück, ob Drücken und Loslassen im Spiel ankamen.</summary>
        private IEnumerator Sp3Rechtsklick(Vector2 p, bool[] angekommen)
        {
            Sp3Maus(p, false);
            yield return null;
            Sp3Maus(p, true);
            yield return null;
            bool druck = Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
            Sp3Maus(p, false);
            yield return null;
            bool los = Mouse.current != null && Mouse.current.rightButton.wasReleasedThisFrame;
            angekommen[0] = druck && los;
        }

        // ================================================================== 3. Handel (FORTSCHRITT-01/-02)

        private IEnumerator Sp3Handel(NetworkInventory a, NetworkProgression pa, QuestUi ui, IProgressionService svc)
        {
            Line("");
            Line("[3 Handel: Rechtsklick im Rucksack, Kauf]");
            Sp3Punkt("FORTSCHRITT-01", "Im Gespräch mit einem Händler, Händlerfenster zu: echter Rechtsklick auf Plunder verkauft NICHT; mit offenem Fenster verkauft er");
            NpcDefinition haendler = UiHaendler();
            InventoryUi invUi = FindAnyObjectByType<InventoryUi>();
            if (haendler == null || invUi == null) { Check(false, "Händler in der Szene und Inventar-Oberfläche vorhanden"); yield break; }
            yield return UiZumNpc(a, haendler.Id);
            yield return Ask(a, "npc-talk", haendler.Id);
            yield return UiTakt();
            yield return new WaitForSecondsRealtime(0.3f);
            Check(_last.ok && svc.DialogNpc != null && svc.DialogNpc.Id == haendler.Id && UiSichtbar(UiFeld(invUi, "_bagWin")),
                $"Gespräch mit {haendler.Name} offen, Rucksack mit geöffnet");
            Check(!QuestUi.HandelOffen && !UiSichtbar(UiFeld(ui, "_vendWin")), $"Händlerfenster zu: QuestUi.HandelOffen {QuestUi.HandelOffen}");

            a.ServerGive(new ItemStack(Sp3Junk, 1));
            yield return new WaitForSecondsRealtime(0.6f);
            int idx = IndexOf(a, Sp3Junk);
            ItemDefinition junk = GameData.Item(Sp3Junk);
            RectTransform platz = Sp3Rucksackplatz(invUi, idx);
            bool maus = Mouse.current != null && Mouse.current.enabled;
            if (idx < 0 || platz == null || !maus || junk == null)
            {
                Check(idx >= 0 && platz != null && junk != null, $"Plunder im Rucksack (Platz {idx}) und sichtbar");
                if (!maus) Sp3NichtGeprueft("FORTSCHRITT-01", "Maus des Input Systems ohne Fokus nicht verfügbar");
            }
            else
            {
                int n0 = a.ServerModel.Count(Sp3Junk), bb0 = svc.Buyback.Count, f0 = _sp3Fehlertexte.Count;
                long geld0 = pa.ServerMoney;
                var kam = new bool[1];
                yield return Sp3Rechtsklick(UiMitte(platz), kam);
                yield return new WaitForSecondsRealtime(0.8f);
                if (!kam[0]) Sp3NichtGeprueft("FORTSCHRITT-01", "der eingespeiste Rechtsklick kam nicht im Spiel an");
                else
                {
                    Check(a.ServerModel.Count(Sp3Junk) == n0 && pa.ServerMoney == geld0 && svc.Buyback.Count == bb0,
                        $"Rechtsklick ohne Händlerfenster auf „{junk.DisplayName}“: Stück {n0} → {a.ServerModel.Count(Sp3Junk)}, Geld {geld0} → {pa.ServerMoney}, Rückkauf {bb0} → {svc.Buyback.Count} (nicht verkauft)");
                    Info($"Meldungen dabei: {(_sp3Fehlertexte.Count > f0 ? string.Join(" / ", _sp3Fehlertexte.Skip(f0)) : "keine")} (Zweig Benutzen/Anlegen)");
                    yield return UiKlickHandeln(ui);
                    yield return new WaitForSecondsRealtime(0.3f);
                    Check(QuestUi.HandelOffen && UiSichtbar(UiFeld(ui, "_vendWin")), "„Handeln“: Händlerfenster offen, QuestUi.HandelOffen true");
                    idx = IndexOf(a, Sp3Junk);
                    platz = Sp3Rucksackplatz(invUi, idx);
                    if (platz != null)
                    {
                        yield return Sp3Rechtsklick(UiMitte(platz), kam);
                        yield return WaitUntil(() => a.ServerModel.Count(Sp3Junk) < n0, 3f);
                        yield return new WaitForSecondsRealtime(0.3f);
                        Check(kam[0] && a.ServerModel.Count(Sp3Junk) == n0 - 1 && pa.ServerMoney == geld0 + junk.sellPrice && svc.Buyback.Count == Mathf.Min(bb0 + 1, 6),
                            $"Rechtsklick mit offenem Händlerfenster: verkauft (Stück {a.ServerModel.Count(Sp3Junk)}, Geld +{pa.ServerMoney - geld0}k, Rückkauf {svc.Buyback.Count})");
                    }
                    else Check(false, "Plunder nach „Handeln“ wieder im Rucksack sichtbar");
                }
            }

            // ---------------------------------------------------------- FORTSCHRITT-02 im Spiel (Serverweg Kauf)
            Sp3Punkt("FORTSCHRITT-02", "Kauf über den Server: Preis = BuyPrice(Gegenstand, Angebotspreis, Angebotsmenge)");
            if (!QuestUi.HandelOffen) yield return UiKlickHandeln(ui);
            int k = haendler.stock.FindIndex(e => e != null && e.item != null && e.count > 1 && e.price <= 0);
            if (k < 0) k = haendler.stock.FindIndex(e => e != null && e.item != null);
            if (k >= 0)
            {
                VendorEntry e = haendler.stock[k];
                pa.ServerSetMoney(Math.Max(pa.ServerMoney, 100000));
                yield return new WaitForSecondsRealtime(0.3f);
                long g0 = pa.ServerMoney;
                int c0 = a.ServerModel.Count(e.item.Id);
                yield return Ask(a, "buy", k.ToString());
                yield return new WaitForSecondsRealtime(0.3f);
                int soll = Progression.BuyPrice(e.item, e.price, e.count);
                string kauf = $"{haendler.Name}: „{e.item.DisplayName} ×{e.count}“ gekauft für {g0 - pa.ServerMoney}k (Soll {soll}k), +{a.ServerModel.Count(e.item.Id) - c0} Stück";
                if (e.count > 1 && e.price <= 0)
                    Check(_last.ok && g0 - pa.ServerMoney == soll && a.ServerModel.Count(e.item.Id) - c0 == e.count, kauf);
                else
                {
                    // Nachbesserung 21.10.: Einzelstück oder fester Preis – alte und neue Formel liefern denselben Preis, also keine Prüfung
                    // des Fixes (vorher als OK gezählt)
                    Info($"{kauf} – {(_last.ok && g0 - pa.ServerMoney == soll ? "stimmt" : "WEICHT AB")}; Einzelstück/fester Preis: alte und neue Formel gleich, zählt nicht als Prüfung");
                    Sp3NichtGeprueft("FORTSCHRITT-02", "Kauf eines Bündels OHNE festen Preis im Spiel – in Kareth v4 gibt es keins mehr (Schwarzpulver ×5 nur bei Tobbe/Oda in der Sandbox); Formel und alle Angebote oben über die Daten geprüft");
                }
            }
            else Check(false, "Angebot zum Kaufen gefunden");

            for (int i = 0; i < 5 && UiFensterstapel.Oberstes != ""; i++)
            {
                UiFensterstapel.EscAusloesen();
                yield return UiTakt();
            }
            Check(UiFensterstapel.Oberstes == "" && svc.DialogNpc == null && !QuestUi.HandelOffen, "Esc schließt Händler, Rucksack und Gespräch; HandelOffen wieder false");
        }

        /// <summary>Rucksackplatz (RectTransform) für Rucksack-Index <paramref name="code"/> – nur wenn sichtbar.</summary>
        private RectTransform Sp3Rucksackplatz(InventoryUi invUi, int code)
        {
            if (code < 0 || !(UiFeld(invUi, "_bagSlots") is IEnumerable plaetze)) return null;
            foreach (object s in plaetze)
                if (UiFeld(s, "Code") is int c && c == code && UiFeld(s, "Rt") is RectTransform rt && rt.gameObject.activeInHierarchy) return rt;
            return null;
        }

        // ================================================================== 4. Kampf (KAMPF-01 … -04)

        private IEnumerator Sp3Kampf(NetworkInventory a, NetworkProgression pa, Health ah)
        {
            Line("");
            Line("[4 Kampf]");
            if (EnemyDirector.Instance == null) { Check(false, "Gegner-Steuerung läuft"); yield break; }
            CharacterClassDefinition warden = GameData.Class(Sp3Warden);
            if (a.ServerModel.Class != warden && warden != null) { pa.ServerDebugSwitchClass(warden, true); Info("Klasse auf Eidwächter gestellt"); }
            var nm = a.GetComponent<NetworkMotion>();
            var nc = a.GetComponent<NetworkCombat>();
            var waffen = a.GetComponent<WeaponUser>();
            Vector3 stand = Sp3RuhigerOrt(a);
            yield return Ask(a, "goto", V(stand + Vector3.up * 0.3f, 0f));
            yield return new WaitForSecondsRealtime(2f);

            // ---------------------------------------------------------- KAMPF-04 (Eidwächter)
            Sp3Punkt("KAMPF-04", "Eidwächter: Donnerschlag (Sofortzauber mit eigener Abklingzeit) → Leiste zeigt die GCD auf Schildhieb");
            foreach (string id in new[] { Sp3HS, Sp3Thunder, Sp3Bash }) pa.ServerDebugLearn(id, 1);
            pa.ServerDebugResetCooldowns();
            pa.ServerSetResource(100f);
            yield return new WaitForSecondsRealtime(1.7f);
            SpellDefinition donner = GameData.Get<SpellDefinition>(Sp3Thunder);
            Check(donner != null && donner.cooldown > 0f && donner.castTime <= 0f && donner.triggersGcd, $"Donnerschlag: Sofortzauber, eigene Abklingzeit {donner?.cooldown:0.#} s, löst die GCD aus");
            float vorher = SpellClient.CooldownRemaining(Sp3Bash, out _);
            string why = pa.ServerCast(Sp3Thunder, null);
            yield return new WaitForSecondsRealtime(0.25f);
            float rest = SpellClient.CooldownRemaining(Sp3Bash, out float gesamt);
            float eigen = SpellClient.CooldownRemaining(Sp3Thunder, out float eigenGesamt);
            Check(why == "" && vorher <= 0.01f && rest > 0.9f && rest <= SpellClient.GcdTotal + 0.01f && Mathf.Approximately(gesamt, SpellClient.GcdTotal),
                $"Nach Donnerschlag ({(why == "" ? "gewirkt" : why)}): Schildhieb auf der Leiste gesperrt {rest:0.00} s von {gesamt:0.00} s (GCD {SpellClient.GcdTotal:0.0} s; vorher {vorher:0.00})");
            Check(eigen > SpellClient.GcdTotal && eigenGesamt > SpellClient.GcdTotal,
                $"Donnerschlag zeigt seine eigene Abklingzeit ({eigen:0.0} s von {eigenGesamt:0.0} s; Daten {donner?.cooldown:0.#} s)");
            string sofort = pa.ServerCast(Sp3Bash, ah);
            Check(sofort.Contains("Noch nicht bereit"), $"Server sperrt Schildhieb in der GCD ebenso („{sofort}“) – Leiste und Server stimmen überein");
            yield return new WaitForSecondsRealtime(1.6f);
            Check(SpellClient.CooldownRemaining(Sp3Bash, out _) <= 0.01f, "Nach der GCD ist Schildhieb auf der Leiste wieder frei");

            // ---------------------------------------------------------- KAMPF-03
            Sp3Punkt("KAMPF-03", "Nahkampf: Client-Reichweite (Autoangriff) passt zum Server-Treffer; Heldenhafter Schlag verfällt nicht bei Fehlschlag");
            EnemyBrain ziel = Sp3Gegner(KC.EnemyFaded, K28Near(a.transform.position, Flach(a.transform.forward).normalized, 6f), a.ServerModel.Level,
                br => br.SetAggroProfile(AggroProfile.Defensive), 30f);
            if (ziel == null || waffen == null || waffen.MeleeWeapon == null || nm == null || nc == null)
            {
                Check(false, $"Verblichener erzeugt ({ziel != null}), Nahkampfwaffe ({waffen?.MeleeWeapon?.Id})");
            }
            else
            {
                yield return new WaitForSecondsRealtime(0.6f);
                ziel.ServerStun(60f);
                Health zh = ziel.GetComponent<Health>();
                CombatTarget ct = ziel.GetComponentInChildren<CombatTarget>();
                WeaponDefinition w = waffen.MeleeWeapon;
                MethodInfo inRange = typeof(WeaponUser).GetMethod("InRange", BindingFlags.NonPublic | BindingFlags.Instance);
                if (inRange == null) _uiFehlendeHaken.Add("WeaponUser.InRange");
                Collider koerper = ziel.GetComponents<Collider>().FirstOrDefault(c => c != null && c.enabled && !c.isTrigger);
                Check(zh != null && ct != null && inRange != null && koerper != null && ziel.IsStunned,
                    $"Ziel: {ziel.Definition?.DisplayName} betäubt, Körper {koerper?.GetType().Name}, Waffe {w.DisplayName} (Reichweite {w.range:0.0} m)");
                if (zh != null && ct != null && inRange != null && koerper != null)
                {
                    Vector3 zuZiel = Flach(a.transform.forward).normalized;
                    int luft = 0, verweigert = 0, randOk = 0;
                    var zeilen = new List<string>();
                    for (float d = 2.0f; d <= 3.61f; d += 0.2f)
                    {
                        UiNeben(nm, a, ziel.transform.position, zuZiel, d);
                        yield return new WaitForSecondsRealtime(0.35f);
                        Physics.SyncTransforms();
                        bool client = (bool)inRange.Invoke(waffen, new object[] { w, ct });
                        Vector3 origin = a.transform.position + Vector3.up * 1.0f;
                        Vector3 fwd = Flach(ziel.transform.position - a.transform.position).normalized;
                        int treffer = Sp3Vorhersage(a, w, origin, fwd);
                        float kante = Vector3.Distance(origin, koerper.ClosestPoint(origin));
                        float mitte = Vector3.Distance(Flach(a.transform.position), Flach(ziel.transform.position));
                        if (client && treffer == 0) { if (kante - w.range <= 0.15f + 0.01f) randOk++; else luft++; }
                        if (!client && treffer > 0) verweigert++;
                        zeilen.Add($"{mitte:0.00}m: Client {(client ? "in" : "aus")}, Server {(treffer > 0 ? "Treffer" : "kein")} (Kante {kante:0.00})");
                    }
                    Info("Abstand Mitte–Mitte → Client-Reichweite / Server-Treffer: " + string.Join(" · ", zeilen));
                    Check(luft == 0 && verweigert == 0, $"Keine Luftschläge (Client „in Reichweite“, Server trifft nicht: {luft}) und keine verweigerten Treffer ({verweigert}); im Zuschlag 0,15 m am Rand: {randOk}");

                    // echter Autoangriff außerhalb der Reichweite
                    UiNeben(nm, a, ziel.transform.position, zuZiel, 3.3f);
                    yield return new WaitForSecondsRealtime(0.4f);
                    float t0 = nc.ServerLastAttackTime, hp0 = zh.Current;
                    int f0 = _sp3Fehlertexte.Count;
                    yield return Ask(a, "attack", ziel.GetComponent<FishNet.Object.NetworkObject>().ObjectId.ToString());
                    yield return new WaitForSecondsRealtime(2f);
                    bool meldung = _sp3Fehlertexte.Skip(f0).Contains("Ziel ausser Reichweite");
                    Check(meldung && Mathf.Approximately(nc.ServerLastAttackTime, t0) && Mathf.Approximately(zh.Current, hp0),
                        $"Echter Autoangriff auf 3,3 m: Meldung „Ziel ausser Reichweite“ {(meldung ? "ja" : "NEIN")}, kein Schlag beim Server ({(Mathf.Approximately(nc.ServerLastAttackTime, t0) ? "keiner" : "doch")}), Leben {zh.Current:0}/{zh.Max:0}");
                    // derselbe Autoangriff näher dran trifft
                    UiNeben(nm, a, ziel.transform.position, zuZiel, 2.2f);
                    yield return WaitUntil(() => zh.Current < hp0, 4f);
                    Check(zh.Current < hp0 && nc.ServerLastAttackTime > t0, $"Auf 2,2 m trifft der laufende Autoangriff (Leben {hp0:0} → {zh.Current:0})");
                    yield return Ask(a, "stop");
                    yield return new WaitForSecondsRealtime(1.2f);
                    ziel.ServerStun(60f);

                    // Heldenhafter Schlag: Fehlschlag am Rand → bleibt vorgemerkt, Wut bleibt; Treffer → verbraucht
                    pa.ServerSetResource(50f);
                    why = pa.ServerCast(Sp3HS, zh);
                    Check(why == "" && pa.ServerQueuedNextSwing == Sp3HS, $"Heldenhafter Schlag vorgemerkt ({(why == "" ? "ok" : why)}), Wut {pa.ServerResource:0}");
                    UiNeben(nm, a, ziel.transform.position, zuZiel, 3.3f);
                    yield return new WaitForSecondsRealtime(1.0f);
                    float hpA = zh.Current, wutA = pa.ServerResource;
                    string spellA = pa.ServerLastSpellResult;
                    nc.SubmitMelee(AttackIntentKind.Combo, 0, a.transform.position + Vector3.up * 1.0f, Flach(ziel.transform.position - a.transform.position).normalized);
                    yield return new WaitForSecondsRealtime(0.5f);
                    Check(pa.ServerQueuedNextSwing == Sp3HS && Mathf.Abs(pa.ServerResource - wutA) < 0.01f && Mathf.Approximately(zh.Current, hpA) && pa.ServerLastSpellResult == spellA,
                        $"Schlag ins Leere (3,3 m): Heldenhafter Schlag bleibt vorgemerkt („{pa.ServerQueuedNextSwing}“), Wut {wutA:0} → {pa.ServerResource:0}, kein Schaden");
                    UiNeben(nm, a, ziel.transform.position, zuZiel, 2.0f);
                    yield return new WaitForSecondsRealtime(1.0f);
                    float hpB = zh.Current;
                    nc.SubmitMelee(AttackIntentKind.Combo, 0, a.transform.position + Vector3.up * 1.0f, Flach(ziel.transform.position - a.transform.position).normalized);
                    yield return new WaitForSecondsRealtime(0.5f);
                    Check(pa.ServerQueuedNextSwing == "" && pa.ServerLastSpellResult == Sp3HS + ":swing" && zh.Current < hpB,
                        $"Treffer (2,0 m): verbraucht ({pa.ServerLastSpellResult}), Schaden {hpB - zh.Current:0} (Waffe {w.minDamage:0}–{w.maxDamage:0}), Wut jetzt {pa.ServerResource:0}");
                }
                EnemyDirector.Instance.Remove(ziel);
            }

            // ---------------------------------------------------------- KAMPF-01
            Sp3Punkt("KAMPF-01", "Heimkehrender Aschwolf: ServerStun (wie Schildhieb/Ansturm) und ServerStun(0) wirken nicht, Dominieren wird abgelehnt, er läuft heim und setzt sich zurück");
            yield return Sp3Heimkehrer(a, pa, ah);

            // ---------------------------------------------------------- KAMPF-02
            Sp3Punkt("KAMPF-02", "Schützin an der Rückzugsgrenze (80 % der Leine), Nahkämpfer dicht dran: schießt statt zurückzuweichen, kein Heimweg");
            GameObject schuetzin = Sp3GegnerVorlage(KC.EnemyDeserterArcher, out EnemyDefinition schuetzinDef);
            if (schuetzin == null) schuetzin = Sp3GegnerVorlage("enemy.faded_gunner", out schuetzinDef);
            yield return Sp3Schuetzin(a, ah, nm, schuetzin, schuetzinDef, false);
            Sp3Punkt("KAMPF-02", "Start innerhalb von 80 % der Leine, Nahkämpfer drängt nach: weicht zurück bis zur Grenze, bleibt dort stehen und schießt");
            yield return Sp3Schuetzin(a, ah, nm, schuetzin, schuetzinDef, true);
            Sp3NichtGeprueft("KAMPF-02", "Gegenprobe Nadelspeier (taucht unter Dauerdruck weiter ab) sowie Gegner mit Route und in der Arena – Regression kareth28/kolosseum");

            // ---------------------------------------------------------- KAMPF-02 Westweiden (Nachbesserung 21.10.)
            Sp3Punkt("KAMPF-02", "Westweiden: Habbos Schütze (vorher Reichweite 2,3 m) schießt aus 9 m – zwischen Zurückweichen (5 m) und Heranlaufen (13 m)");
            yield return Sp3Westweidenschuetze(a, ah, nm);
            ah.Heal(ah.Max);
        }

        /// <summary>Server-Vorhersage wie NetworkCombat.ServerMelee (PredictOnly: kein Schaden, gleiche Zielwahl).</summary>
        private static int Sp3Vorhersage(NetworkInventory a, WeaponDefinition w, Vector3 origin, Vector3 fwd)
        {
            var req = new AttackRequest
            {
                Attacker = a.gameObject,
                Origin = origin,
                Forward = fwd,
                Weapon = w,
                Step = WeaponUser.StepFor(w, AttackKind.Combo, 0),
                LocalPlayer = false,
                DamageScale = 1f,
                SingleTarget = true,
                PredictOnly = true
            };
            return CombatResolver.ResolveMelee(req, null);
        }

        private IEnumerator Sp3Heimkehrer(NetworkInventory a, NetworkProgression pa, Health ah)
        {
            EnemySpawnArea vorlage = K28Template(KC.EnemyWolf);
            if (vorlage == null) { Check(false, "Vorlage Aschwolf in der Szene"); yield break; }
            // KAMPF-01 Folgepunkt (21.10.): Dominieren braucht einen Aschenjäger mit gelerntem Dominieren – für diese Probe umstellen,
            // am Ende zurück auf Eidwächter (Testprofil, kein Spielstand)
            CharacterClassDefinition jaeger = GameData.Class(Sp3Stalker), waechter = GameData.Class(Sp3Warden);
            bool umgestellt = jaeger != null && waechter != null && pa != null, dominieren = false;
            if (umgestellt)
            {
                pa.ServerRemoveDominate();
                pa.ServerDebugSwitchClass(jaeger, true);
                dominieren = pa.ServerUnlockDominate() == "" && pa.ServerCanDominate && !pa.ServerHasCompanion;
            }
            EnemyBrain wolf = null;
            try
            {
                Vector3 blick = Flach(a.transform.forward).normalized;
                Vector3 start = K28Near(a.transform.position, blick, 6f);
                float leine = vorlage.Definition.leashDistance;
                if (!Sp3Heimat(start, blick, leine + 15f, leine * 1.8f, out Vector3 heim, out float weg)) { Check(false, $"Heimat jenseits der Leine ({leine:0} m) mit Weg"); yield break; }
                int stufe = Mathf.Max(1, a.ServerModel.Level);
                wolf = EnemyDirector.Instance.SpawnAt(vorlage.Prefab, start, 0f, stufe, "spielablauf3",
                    br => { br.Configure(stufe, heim, "spielablauf3"); br.SetAggroProfile(AggroProfile.Defensive); }, 5f);
                if (wolf == null) { Check(false, "Aschwolf erzeugt"); yield break; }
                EnemyBrain w = wolf;
                yield return new WaitForSecondsRealtime(0.5f);
                Health wh = w.GetComponent<Health>();
                NavMeshAgent agent = w.GetComponent<NavMeshAgent>();
                wh.ApplyDamage(new DamageInfo { Amount = 5f, Source = a.gameObject, WeaponId = "test" });
                w.ServerEngage(ah);
                yield return WaitUntil(() => w.State == EnemyState.Return, 8f);
                Check(w.State == EnemyState.Return && wh.IsTemporarilyInvulnerable && wh.Current < wh.Max,
                    $"Leine überschritten: Wolf kehrt heim (Weg {weg:0} m, Leine {leine:0} m), unverwundbar, Leben {wh.Current:0}/{wh.Max:0}");
                yield return new WaitForSecondsRealtime(0.5f);
                w.ServerStun(2f);
                bool betaeubt = w.IsStunned, steht = agent != null && agent.isOnNavMesh && agent.isStopped;
                w.ServerStun(0f);   // bis 22.10. der Weg von CancelDominate (seit 23.10. EnemyBrain.ServerStunEnde, das nie Stop() ruft)
                bool steht0 = agent != null && agent.isOnNavMesh && agent.isStopped;
                Check(!betaeubt && !steht && !steht0, $"ServerStun(2 s) und ServerStun(0) auf den Heimkehrer: betäubt {betaeubt}, Agent angehalten {steht}/{steht0} (Soll nein)");

                // KAMPF-01 Folgepunkt (21.10.): Dominieren am Heimkehrer wird gleich abgelehnt (vorher Wirkbalken, Tier lief weiter heim)
                FishNet.Object.NetworkObject wolfNob = w.GetComponent<FishNet.Object.NetworkObject>();
                string sollNein = $"{w.Definition.DisplayName} lässt sich nicht dominieren.";
                if (!dominieren) Sp3NichtGeprueft("KAMPF-01", "Dominieren am Heimkehrer: Testfigur ließ sich nicht auf Aschenjäger mit Dominieren stellen");
                else
                {
                    bool nochHeim = w.State == EnemyState.Return;
                    string nein = pa.ServerStartDominate(wolfNob);
                    Check(nochHeim && nein == sollNein && !pa.ServerDominating,
                        $"Dominieren am Heimkehrer (Aschenjäger, Dominieren gelernt): abgelehnt „{nein}“ (Soll „{sollNein}“), Wirken {(pa.ServerDominating ? "LÄUFT" : "keins")}");
                }

                float t = 0f;
                Vector3 p0 = w.transform.position;
                while (t < 45f && w != null && w.State == EnemyState.Return) { t += 0.5f; yield return new WaitForSecondsRealtime(0.5f); }
                yield return new WaitForSecondsRealtime(0.8f);
                float daheim = w != null ? Vector3.Distance(Flach(w.transform.position), Flach(heim)) : -1f;
                Check(w != null && w.State != EnemyState.Return && daheim < 4f && Mathf.Approximately(wh.Current, wh.Max) && !wh.IsTemporarilyInvulnerable,
                    $"Nach {t:0.0} s daheim ({daheim:0.0} m von der Heimat, {Vector3.Distance(p0, heim):0} m gelaufen): Zustand {w?.State}, Leben {wh.Current:0}/{wh.Max:0}, unverwundbar {wh.IsTemporarilyInvulnerable}");
                if (dominieren && w != null && w.State != EnemyState.Return)
                {
                    // Gegenprobe: daheim (volles Leben) lehnt jetzt die Lebensgrenze ab – die Ablehnung oben kam von der Heimkehr
                    string danach = pa.ServerStartDominate(wolfNob);
                    Check(danach.StartsWith("Sein Wille ist ungebrochen", StringComparison.Ordinal) && !pa.ServerDominating,
                        $"Gegenprobe daheim (Zustand {w.State}, volles Leben): Ablehnung „{danach}“ – nicht mehr wegen der Heimkehr");
                }
            }
            finally
            {
                if (wolf != null) EnemyDirector.Instance.Remove(wolf);
                if (umgestellt)
                {
                    pa.ServerRemoveDominate();
                    pa.ServerDebugSwitchClass(waechter, true);
                }
            }
        }

        /// <summary>
        /// KAMPF-02: Schützin, ein Nahkämpfer (die Figur) drängt dicht davor. <paramref name="innen"/> = false: Start knapp jenseits von
        /// 80 % der Leine – sie schießt statt weiter zu weichen. true (Nachbesserung 21.10.): Start deutlich innerhalb – sie weicht zurück,
        /// bis der nächste Schritt über die 80 % ginge, bleibt dort stehen und schießt.
        /// </summary>
        private IEnumerator Sp3Schuetzin(NetworkInventory a, Health ah, NetworkMotion nm, GameObject prefab, EnemyDefinition def, bool innen)
        {
            if (prefab == null || def == null || nm == null) { Check(false, "Vorlage Schützin (Deserteurin oder Verblichene) gefunden"); yield break; }
            if (!innen)
                Check(def.style == EnemyStyle.Ranged && def.tooCloseDistance > 2.5f, $"{def.DisplayName}: Fernkampf, weicht unter {def.tooCloseDistance:0.#} m, Leine {def.leashDistance:0} m, Reichweite {def.attackRange:0} m");
            Vector3 blick = Flach(a.transform.forward).normalized;
            Vector3 heim = K28Near(a.transform.position, blick, 4f);
            float grenze = def.leashDistance * 0.8f;
            float abstand = innen ? Mathf.Max(4f, grenze - 12f) : grenze + 1.5f;
            // innen: eine Richtung mit Weg bis über die Grenze hinaus, damit der Rückzug nicht vorher an einer Kante endet
            float suche = innen ? grenze + 3f : abstand;
            if (!Sp3Heimat(heim, blick, suche, suche * 1.4f, out Vector3 fern, out _)) { Check(false, $"Stelle {suche:0} m von der Heimat mit Weg"); yield break; }
            Vector3 richtung = Flach(fern - heim).normalized;
            Vector3 stelle = innen ? K28Near(heim, richtung, abstand) : fern;
            int stufe = Mathf.Max(1, a.ServerModel.Level);
            Vector3 zurHeimat = Flach(heim - stelle).normalized;
            EnemyBrain s = EnemyDirector.Instance.SpawnAt(prefab, stelle, Quaternion.LookRotation(zurHeimat).eulerAngles.y, stufe, "spielablauf3",
                br => br.Configure(stufe, heim, "spielablauf3"), 5f);
            if (s == null) { Check(false, "Schützin erzeugt"); yield break; }
            float startHeim = Vector3.Distance(Flach(s.transform.position), Flach(heim));
            int schuesse = 0;
            bool heimweg = false;
            float maxHeim = 0f, ersterSchuss = -1f;
            void Schuss(Vector3 _, float __, bool ___)
            {
                schuesse++;
                if (ersterSchuss < 0f && s != null) ersterSchuss = Vector3.Distance(Flach(s.transform.position), Flach(heim));
            }
            void Zustand(EnemyState z) { if (z == EnemyState.Return) heimweg = true; }
            s.AttackBegan += Schuss;
            s.StateChanged += Zustand;
            try
            {
                yield return new WaitForSecondsRealtime(0.5f);
                UiNeben(nm, a, s.transform.position, -zurHeimat, 2f);   // Figur zwischen Schützin und Heimat, 2 m vor ihr
                yield return new WaitForSecondsRealtime(0.2f);
                s.ServerEngage(ah);
                float dauer = innen ? 20f : 12f, takt = innen ? 0.25f : 0.5f;
                for (float t = 0f; t < dauer; t += takt)
                {
                    if (Vector3.Distance(Flach(a.transform.position), Flach(s.transform.position)) > 2.6f)
                        UiNeben(nm, a, s.transform.position, Flach(s.transform.position - heim).normalized, 2f);
                    maxHeim = Mathf.Max(maxHeim, Vector3.Distance(Flach(s.transform.position), Flach(heim)));
                    yield return new WaitForSecondsRealtime(takt);
                }
                if (!innen)
                {
                    Check(schuesse >= 1, $"Schützin schießt aus kurzer Distanz: {schuesse} Angriffe in 12 s (Figur ständig ~2 m vor ihr)");
                    Check(!heimweg && s.State != EnemyState.Return && maxHeim <= abstand + 1.5f,
                        $"Kein Heimweg („Entkommen“) und kein Zurückweichen über die Grenze: größter Abstand zur Heimat {maxHeim:0.0} m (Start {abstand:0.0} m, Leine {def.leashDistance:0} m), Zustand {s.State}");
                }
                else
                {
                    Check(!heimweg && s.State != EnemyState.Return && maxHeim >= startHeim + 4f && maxHeim <= grenze + 3f,
                        $"Rückzug von {startHeim:0.0} m bis höchstens {maxHeim:0.0} m von der Heimat (Grenze {grenze:0.0} m = 80 % der Leine {def.leashDistance:0} m), kein Heimweg, Zustand {s.State}");
                    Check(schuesse >= 1 && ersterSchuss >= grenze - 6f,
                        $"An der Grenze stehen geblieben und geschossen: {schuesse} Angriffe in 20 s, erster Schuss {ersterSchuss:0.0} m von der Heimat (Rückzug davor ohne Schuss)");
                }
            }
            finally
            {
                s.AttackBegan -= Schuss;
                s.StateChanged -= Zustand;
                EnemyDirector.Instance.Remove(s);
            }
        }

        /// <summary>
        /// KAMPF-02 Nachbesserung (21.10.): Habbos Schütze (Westweiden) hatte die Nahkampfwerte aus SetupHusk (Reichweite 2,3 m) und schoss
        /// zwischen 5 und 13 m nie. Figur 9 m vor ihm, still: Er bleibt stehen (Wunschabstand) und schießt.
        /// </summary>
        private IEnumerator Sp3Westweidenschuetze(NetworkInventory a, Health ah, NetworkMotion nm)
        {
            GameObject prefab = Sp3GegnerVorlage("enemy.ww_heckenschuetze", out EnemyDefinition def);
            if (prefab == null || def == null || nm == null) { Check(false, "Vorlage Habbos Schütze (enemy.ww_heckenschuetze) unter den Netz-Prefabs"); yield break; }
            Check(def.style == EnemyStyle.Ranged && def.attackRange >= def.preferredDistance + 2f,
                $"{def.DisplayName}: Fernkampf, Reichweite {def.attackRange:0.#} m, Wunschabstand {def.preferredDistance:0.#} m, weicht unter {def.tooCloseDistance:0.#} m, Leine {def.leashDistance:0} m");
            Vector3 blick = Flach(a.transform.forward).normalized;
            Vector3 heim = K28Near(a.transform.position, blick, 4f);
            if (!Sp3Heimat(heim, blick, 9f, 14f, out Vector3 platz, out _)) { Check(false, "Stelle 9 m vom Schützen mit Weg"); yield break; }
            int stufe = Mathf.Max(1, a.ServerModel.Level);
            Vector3 zuFigur = Flach(platz - heim).normalized;
            EnemyBrain s = EnemyDirector.Instance.SpawnAt(prefab, heim, Quaternion.LookRotation(zuFigur).eulerAngles.y, stufe, "spielablauf3",
                br => br.Configure(stufe, heim, "spielablauf3"), 5f);
            if (s == null) { Check(false, "Habbos Schütze erzeugt"); yield break; }
            int schuesse = 0;
            bool heimweg = false;
            float minAbstand = 999f, maxAbstand = 0f;
            void Schuss(Vector3 _, float __, bool ___) => schuesse++;
            void Zustand(EnemyState z) { if (z == EnemyState.Return) heimweg = true; }
            s.AttackBegan += Schuss;
            s.StateChanged += Zustand;
            try
            {
                yield return new WaitForSecondsRealtime(0.5f);
                UiNeben(nm, a, s.transform.position, -zuFigur, 9f);   // Figur 9 m vor ihm, sie bewegt sich nicht
                yield return new WaitForSecondsRealtime(0.3f);
                s.ServerEngage(ah);
                for (float t = 0f; t < 10f; t += 0.5f)
                {
                    float d = Vector3.Distance(Flach(a.transform.position), Flach(s.transform.position));
                    minAbstand = Mathf.Min(minAbstand, d);
                    maxAbstand = Mathf.Max(maxAbstand, d);
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                Check(schuesse >= 1 && !heimweg && s.State != EnemyState.Return && minAbstand >= def.tooCloseDistance,
                    $"Schießt auf Distanz: {schuesse} Angriffe in 10 s, Abstand {minAbstand:0.0}–{maxAbstand:0.0} m (vorher mit Reichweite 2,3 m: keiner), Zustand {s.State}");
            }
            finally
            {
                s.AttackBegan -= Schuss;
                s.StateChanged -= Zustand;
                EnemyDirector.Instance.Remove(s);
            }
        }

        /// <summary>Gegner-Vorlage: zuerst ein Gebiet der Szene, sonst das Netz-Prefab mit dieser Definition (Westweiden liegt in einer Zelle).</summary>
        private static GameObject Sp3GegnerVorlage(string enemyId, out EnemyDefinition def)
        {
            EnemySpawnArea area = K28Template(enemyId);
            if (area != null) { def = area.Definition; return area.Prefab; }
            def = null;
            FishNet.Managing.NetworkManager netz = FishNet.InstanceFinder.NetworkManager;
            FishNet.Managing.Object.PrefabObjects liste = netz != null ? netz.SpawnablePrefabs : null;
            if (liste == null) return null;
            for (int i = 0; i < liste.GetObjectCount(); i++)
            {
                FishNet.Object.NetworkObject n = liste.GetObject(true, i);
                EnemyBrain b = n != null ? n.GetComponent<EnemyBrain>() : null;
                if (b == null || b.Definition == null || b.Definition.Id != enemyId) continue;
                def = b.Definition;
                return n.gameObject;
            }
            return null;
        }

        /// <summary>Punkt im Abstand <paramref name="abstand"/> von <paramref name="von"/> mit vollständigem NavMesh-Weg (höchstens <paramref name="maxWeg"/>).</summary>
        private static bool Sp3Heimat(Vector3 von, Vector3 blick, float abstand, float maxWeg, out Vector3 ziel, out float weg)
        {
            ziel = von;
            weg = 0f;
            var pfad = new NavMeshPath();
            foreach (float w in new[] { 0f, 20f, -20f, 40f, -40f, 60f, -60f, 90f, -90f, 135f, -135f, 180f })
            {
                Vector3 r = Quaternion.Euler(0f, w, 0f) * blick;
                if (!NavMesh.SamplePosition(von + r * abstand, out NavMeshHit h, 6f, NavMesh.AllAreas)) continue;
                if (Vector3.Distance(Flach(h.position), Flach(von)) < abstand - 3f) continue;
                if (!NavMesh.CalculatePath(von, h.position, NavMesh.AllAreas, pfad) || pfad.status != NavMeshPathStatus.PathComplete) continue;
                float l = 0f;
                for (int k = 1; k < pfad.corners.Length; k++) l += Vector3.Distance(pfad.corners[k - 1], pfad.corners[k]);
                if (l > maxWeg) continue;
                ziel = h.position;
                weg = l;
                return true;
            }
            return false;
        }

        private static Vector3 Sp3RuhigerOrt(NetworkInventory a)
        {
            WorldQuestSite wiese = WorldQuestSite.Find("site.kareth.vorratswiesen");
            Vector3 p = wiese != null ? wiese.transform.position : a.transform.position;
            if (NavMesh.SamplePosition(p + new Vector3(0f, 0f, 30f), out NavMeshHit h, 8f, NavMesh.AllAreas)) p = h.position;
            return p;
        }

        private EnemyBrain Sp3Gegner(string enemyId, Vector3 pos, int level, Action<EnemyBrain> prep, float leiche)
        {
            EnemySpawnArea v = K28Template(enemyId);
            if (v == null || EnemyDirector.Instance == null) return null;
            return EnemyDirector.Instance.SpawnAt(v.Prefab, pos, 0f, level, "spielablauf3", prep, leiche);
        }

        // ================================================================== 5. Leiche außer Sicht und zurück (MEHRSPIELER-01) + Grete nachts (AUFTRAEGE-01)

        private IEnumerator Sp3LeicheUndGrete(NetworkInventory a, NetworkProgression pa, IProgressionService svc)
        {
            Line("");
            Line("[5 Leiche außer Sicht und zurück · Grete nachts]");
            Sp3Punkt("MEHRSPIELER-01", "Eigene Beuteleiche: Figur > 550 m weg (Sicht verloren), zurück → Server meldet die Beute neu, Leiche glimmt, Beute öffnet");
            EnemySpawnArea vorlage = K28Template(KC.EnemyWolf);
            EnemyBrain wolf = null;
            FishNet.Object.NetworkObject nob = null;
            LootIndicator glimmen = null;
            CorpseLoot leiche = null;
            Vector3 leichenOrt = Vector3.zero;
            if (vorlage != null)
            {
                Vector3 stelle = K28Near(a.transform.position, Flach(a.transform.right).normalized, 3f);
                wolf = EnemyDirector.Instance.SpawnAt(vorlage.Prefab, stelle, 0f, 1, "spielablauf3-leiche", br => br.SetAggroProfile(AggroProfile.Defensive), 900f);
            }
            if (wolf == null) Check(false, "Aschwolf für die Leiche erzeugt");
            else
            {
                yield return new WaitForSecondsRealtime(0.5f);
                Health wh = wolf.GetComponent<Health>();
                wh.ApplyDamage(new DamageInfo { Amount = 1f, Source = a.gameObject });
                yield return new WaitForSecondsRealtime(0.1f);
                wh.ApplyDamage(new DamageInfo { Amount = wh.Max * 20f, Source = a.gameObject, Unblockable = true });
                yield return new WaitForSecondsRealtime(0.8f);
                leiche = wolf.GetComponent<CorpseLoot>();
                glimmen = wolf.GetComponent<LootIndicator>();
                nob = wolf.GetComponent<FishNet.Object.NetworkObject>();
                leichenOrt = wolf.transform.position;
                if (leiche != null && !wh.IsAlive && !leiche.HasLootFor(a.ProfileId))
                {
                    leiche.AddBonus(new[] { a.ProfileId }, Sp3Junk, 1);
                    a.NotifyLootAvailable(nob);
                    Info("Testhilfe: Beutewurf leer – eine Zerbrochene Taschenuhr ergänzt (Glimmen über NotifyLootAvailable wie beim Tod)");
                }
                yield return WaitUntil(() => glimmen != null && glimmen.Available, 3f);
                Check(!wh.IsAlive && leiche != null && leiche.HasLootFor(a.ProfileId) && glimmen != null && glimmen.Available && nob.Observers.Contains(a.Owner),
                    $"Leiche mit Beute für die Figur glimmt ({string.Join(", ", leiche?.Get(a.ProfileId).Select(s => GameData.Item(s.id)?.DisplayName + " ×" + s.count) ?? new string[0])})");
            }

            // ---------------------------------------------------------- Grete (weit weg von der Leiche)
            yield return Sp3Grete(a, svc, nob);

            if (wolf != null && nob != null && glimmen != null)
            {
                Sp3Punkt("MEHRSPIELER-01", "zurück an der Leiche");
                bool wegGewesen = _sp3LeicheAusserSicht;
                Check(wegGewesen, $"Während der Grete-Prüfung (≈{_sp3LeicheAbstand:0} m entfernt) war die Leiche aus der Sicht des Spielers (Observers ohne seine Verbindung)");
                Info("Host: FishNet zerstört die Kopie des Hosts nicht – „frische Kopie ohne Glimmen“ nachgestellt: Glimmen beim Client aus, bevor die Figur zurückkehrt");
                glimmen.SetAvailable(false);
                yield return Ask(a, "goto", V(leichenOrt + Flach(a.transform.right).normalized * 2f + Vector3.up * 0.3f, 0f));
                yield return WaitUntil(() => nob != null && nob.Observers.Contains(a.Owner), 10f);
                yield return WaitUntil(() => glimmen.Available, 4f);
                Check(nob != null && nob.Observers.Contains(a.Owner) && glimmen.Available && leiche.HasLootFor(a.ProfileId),
                    $"Wieder in Sicht: Server meldet die Beute neu (NetworkEnemy.OnSpawnServer) – Leiche glimmt {(glimmen.Available ? "ja" : "NEIN")}");
                yield return Ask(a, "loot-open", nob.ObjectId.ToString());
                yield return UiTakt();
                Check(_last.ok && a.LootOpen && a.Loot.Count > 0, $"Beutefenster öffnet ({a.Loot.Count} Zeilen; {_last.data})");
                a.CloseLoot();
                yield return UiTakt();
                if (!wegGewesen) Sp3NichtGeprueft("MEHRSPIELER-01", "Leiche blieb in Sicht – Wiedereintritt nicht ausgelöst");
                Sp3NichtGeprueft("MEHRSPIELER-01", "echter Test-Client (neue Kopie nach Despawn, Wiederverbinden, Nachlese-Glimmen) – Allein-Suite");
                EnemyDirector.Instance.Remove(wolf);
            }
        }

        private bool _sp3LeicheAusserSicht;
        private float _sp3LeicheAbstand;
        private bool _sp3TotErfasstOk;

        private IEnumerator Sp3Grete(NetworkInventory a, IProgressionService svc, FishNet.Object.NetworkObject leiche)
        {
            Sp3Punkt("AUFTRAEGE-01", "Müllerin Grete um 22:00 und 02:00: sitzt dösend vor der Mühle, sichtbar, Namensschild mit Markierung, ansprechbar");
            _sp3LeicheAusserSicht = false;
            NpcIdentity grete = NpcIdentity.Find(KC.Grete);
            NpcRoutine r = grete != null ? grete.GetComponent<NpcRoutine>() : null;
            if (grete == null || r == null) { Check(false, $"Grete ({KC.Grete}) mit Tagesablauf in der Szene"); yield break; }
            foreach (float stunde in new[] { 22f, 2f })
            {
                NetworkWorldTime.ServerJumpTo(stunde);
                for (float t = 0f; t < 60f && (r.Walking || r.StepIndex < 0); t += 0.5f) yield return new WaitForSecondsRealtime(0.5f);
                yield return new WaitForSecondsRealtime(1f);
                string uhr = stunde >= 10f ? $"{stunde:00}:00" : $"0{stunde:0}:00";
                string note = r.CurrentStep.note ?? "";
                Check(!r.Sleeping && r.Activity == "Sit" && note.Contains("doest"), $"{uhr}: Grete schläft nicht ({r.Activity}, „{note}“)");
                Renderer[] koerper = grete.GetComponentsInChildren<Renderer>(false);
                Collider col = grete.GetComponentsInChildren<Collider>(false).FirstOrDefault(c => c.enabled && !c.isTrigger) ?? grete.GetComponentInChildren<Collider>(false);
                WorldLabel schild = grete.GetComponentInChildren<WorldLabel>(false);
                Check(koerper.Any(x => x.enabled) && col != null && col.enabled && schild != null && schild.isActiveAndEnabled,
                    $"{uhr}: sichtbar ({koerper.Count(x => x.enabled)} Renderer an), anklickbar (Collider {col?.GetType().Name}), Namensschild „{schild?.Title}“ aktiv");
                yield return GotoNpc(a, KC.Grete);
                yield return new WaitForSecondsRealtime(1.2f);
                if (leiche != null)
                {
                    _sp3LeicheAbstand = Vector3.Distance(Flach(a.transform.position), Flach(leiche.transform.position));
                    if (!leiche.Observers.Contains(a.Owner)) _sp3LeicheAusserSicht = true;
                }
                QuestMarkerState soll = QuestRules.MarkerFor(KC.Grete, svc, a.ServerModel.Class);
                Check(soll == QuestMarkerState.Available && schild != null && schild.Marker == WorldLabelMarker.Available,
                    $"{uhr}: Markierung über Grete: {schild?.Marker} (Regel: {soll}; „!“ = Available – N01 ab Stufe 2)");
                if (stunde == 22f) yield return Sp3Foto("auftraege01_grete_2200");
                yield return Ask(a, "npc-talk", KC.Grete);
                yield return UiTakt();
                Check(_last.ok && svc.DialogNpc != null && svc.DialogNpc.Id == KC.Grete, $"{uhr}: Gespräch mit Grete öffnet ({svc.DialogNpc?.Name})");
                if (stunde == 22f) yield return Sp3Foto("auftraege01_grete_gespraech");
                for (int i = 0; i < 4 && UiFensterstapel.Oberstes != ""; i++)
                {
                    UiFensterstapel.EscAusloesen();
                    yield return UiTakt();
                }
            }
            NetworkWorldTime.ServerJumpTo(15.5f);
            yield return new WaitForSecondsRealtime(0.5f);
        }

        // ================================================================== 6. Tod (MEHRSPIELER-03, EINSTIEG-05, EINSTIEG-04/MEHRSPIELER-02)

        private IEnumerator Sp3Tod(NetworkInventory a, NetworkProgression pa, Health hp)
        {
            Line("");
            Line("[6 Gefallen]");
            NetworkPlayer np = a.GetComponent<NetworkPlayer>();
            var nm = a.GetComponent<NetworkMotion>();
            var nc = a.GetComponent<NetworkCombat>();
            PlayerCharacter pc = a.GetComponent<PlayerCharacter>();
            PlayerInputReader input = pc != null ? pc.Input : null;
            TargetingSystem zielwahl = a.GetComponent<TargetingSystem>();
            MethodInfo capture = typeof(NetworkInventory).GetMethod("CapturePosition", BindingFlags.NonPublic | BindingFlags.Instance);
            if (capture == null) _uiFehlendeHaken.Add("NetworkInventory.CapturePosition");
            if (np == null || nm == null || nc == null || input == null || zielwahl == null || capture == null)
            {
                Check(false, "Spieler, Bewegung, Kampf, Eingabe, Zielwahl und CapturePosition vorhanden");
                yield break;
            }
            Vector3 stand = Sp3RuhigerOrt(a);
            yield return Ask(a, "goto", V(stand + Vector3.up * 0.3f, 0f));
            yield return new WaitForSecondsRealtime(1.5f);
            EnemyBrain ziel = Sp3Gegner(KC.EnemyFaded, K28Near(a.transform.position, Flach(a.transform.forward).normalized, 4f), a.ServerModel.Level,
                br => br.SetAggroProfile(AggroProfile.Defensive), 10f);
            if (ziel == null) { Check(false, "Verblichener als Ziel erzeugt"); yield break; }
            yield return new WaitForSecondsRealtime(0.6f);
            ziel.ServerStun(60f);
            Health zh = ziel.GetComponent<Health>();
            CombatTarget ct = ziel.GetComponentInChildren<CombatTarget>();
            UiNeben(nm, a, ziel.transform.position, Flach(ziel.transform.position - a.transform.position).normalized, 1.8f);
            yield return new WaitForSecondsRealtime(0.5f);
            zielwahl.SetTarget(ct);
            yield return UiTakt();
            // Ausgangslage für Esc: kein Fenster offen (in Lauf 1 war vom Plündern in Teil 5 noch der Rucksack offen – Esc schloss
            // korrekt erst ihn; die Prüfung braucht ein leeres Fensterstapel)
            if (a.LootOpen) a.CloseLoot();
            for (int i = 0; i < 5 && UiFensterstapel.Oberstes != ""; i++)
            {
                Info($"Vorher offen: „{UiFensterstapel.Oberstes}“ – Esc schließt");
                UiFensterstapel.EscAusloesen();
                yield return UiTakt();
            }
            zielwahl.SetTarget(ct);
            yield return UiTakt();

            // EINSTIEG-04 / MEHRSPIELER-02: lebend erfasst
            Sp3Punkt("EINSTIEG-04", "Speicherstand der Position (CapturePosition): lebend = Stelle, tot = leer (Bindepunkt), Dungeon behält die Weltposition");
            capture.Invoke(a, null);
            WorldPosition lebend = a.SavedPosition;
            bool zellen = WorldStreamer.Instance != null && WorldStreamer.Instance.Manifest != null;
            BindPoint bindepunkt = BindPoint.Current;
            Info($"Positionszweig: {(zellen ? "Weltzellen (WorldStreamer mit Manifest – Kareth v4 seit dem Weidepass 12.10.)" : "Szene ohne Weltzellen (ScenePositionKey)")}; " +
                 $"Bindepunkt {(bindepunkt != null ? $"{bindepunkt.Position.x:0}/{bindepunkt.Position.z:0}" : "keiner")}; Sterbeort {a.transform.position.x:0}/{a.transform.position.z:0}");
            Check(lebend.IsSet, $"Lebend erfasst: Stelle gesetzt ({lebend.cellId} {lebend.localPosition.x:0}/{lebend.localPosition.z:0})");

            Func<NetworkPlayer, string> haltVorher = NetworkPlayer.HoldRespawn;
            NetworkPlayer.HoldRespawn = p => ReferenceEquals(p, np) ? "Gefallen – die Suite hält die Figur liegen." : haltVorher?.Invoke(p);
            try
            {
                UiSetze(input, "_autorun", true);   // Autolauf an (Num-Feststell)
                yield return null;
                Check(input.Autorun && zielwahl.Current == ct, "Vor dem Tod: Autolauf an, Gegner als Ziel gewählt");
                UiSetze(hp, "_invulnerableUntil", -1f);
                hp.ApplyDamage(new DamageInfo { Amount = hp.Max * 20f, Direction = -a.transform.forward, Unblockable = true });
                yield return UiTakt();
                yield return WaitUntil(() => !hp.IsAlive && np.ServerHeldDown, 3f);
                Check(!hp.IsAlive && np.ServerHeldDown, $"Figur gefallen ({hp.Current:0}/{hp.Max:0}), bleibt liegen");

                // ---------------------------------------------------------- EINSTIEG-05
                Sp3Punkt("EINSTIEG-05", "Im Tod: Autolauf endet; Esc öffnet das Spielmenü, obwohl ein Ziel gewählt ist");
                Check(!input.Autorun, $"Autolauf im Tod beendet (Autorun {input.Autorun})");
                UiSetze(input, "_autorun", true);   // Num-Feststell im Liegen
                yield return null;
                yield return null;
                Check(!input.Autorun, "Autolauf im Liegen erneut eingeschaltet → sofort wieder aus");
                Check(zielwahl.Current == ct && !UiFensterstapel.HatZiel, $"Ziel bleibt gewählt ({zielwahl.Current != null}), zählt im Tod aber nicht für Esc (HatZiel {UiFensterstapel.HatZiel})");
                SpielmenueUi menue = SpielmenueUi.Instance;
                Check(UiFensterstapel.Oberstes == "", $"Im Tod kein Fenster offen (oberstes: „{UiFensterstapel.Oberstes}“)");
                UiFensterstapel.EscAusloesen();
                yield return UiTakt();
                Check(UiFensterstapel.LetzteWirkung == EscWirkung.MenueOeffnen && menue != null && menue.IsOpen,
                    $"Esc im Tod: {UiFensterstapel.LetzteWirkung}{(UiFensterstapel.LetztesFenster != "" ? $" („{UiFensterstapel.LetztesFenster}“)" : "")}, Spielmenü {(menue != null && menue.IsOpen ? "offen" : "ZU")}");
                yield return Sp3Foto("einstieg05_esc_im_tod");
                if (menue != null && menue.IsOpen) menue.Schliessen();
                yield return UiTakt();

                // ---------------------------------------------------------- MEHRSPIELER-03
                Sp3Punkt("MEHRSPIELER-03", "Toter Angreifer: Nahkampf-Anfrage des eigenen Clients (SubmitMelee) neben dem Gegner → Server lehnt ab („tot“), kein Schaden");
                int rej0 = nc.ServerRejected, tot0 = Sp3LogZaehle("verworfen (tot)");
                float last0 = nc.ServerLastAttackTime, zhp0 = zh.Current;
                nc.SubmitMelee(AttackIntentKind.Combo, 0, a.transform.position + Vector3.up, Flach(ziel.transform.position - a.transform.position).normalized);
                yield return new WaitForSecondsRealtime(0.6f);
                Check(nc.ServerRejected == rej0 + 1 && Mathf.Approximately(nc.ServerLastAttackTime, last0) && Mathf.Approximately(zh.Current, zhp0),
                    $"Abgelehnt: ServerRejected {rej0} → {nc.ServerRejected}, kein Angriff gezählt, Gegner {zh.Current:0}/{zh.Max:0}");
                Check(Sp3LogZaehle("verworfen (tot)") > tot0, "Grund im Serverlog: „verworfen (tot)“");
                Sp3NichtGeprueft("MEHRSPIELER-03", "Schuss/Geschoss (SubmitShot/SubmitProjectile) – Testfigur ohne Fernkampfwaffe; dieselbe Prüfung ValidateCommon gilt für alle drei");

                // ---------------------------------------------------------- EINSTIEG-04 / MEHRSPIELER-02 tot
                Sp3Punkt("EINSTIEG-04", "tot erfasst");
                capture.Invoke(a, null);
                WorldPosition tot = a.SavedPosition;
                float zumBind = tot.IsSet && bindepunkt != null ? Vector3.Distance(Flach(tot.localPosition), Flach(bindepunkt.Position)) : -1f;
                float zumOrt = tot.IsSet ? Vector3.Distance(Flach(tot.localPosition), Flach(a.transform.position)) : -1f;
                // Szenenzweig: keine Stelle → RestoreScenePosition setzt an den Bindepunkt. Weltzellen-Zweig: RestorePosition stellt jede
                // gesetzte Stelle wieder her – richtig ist dort nur eine Stelle am Bindepunkt (oder keine, dann Spawn).
                bool totOk = zellen ? (!tot.IsSet || (bindepunkt != null && zumBind >= 0f && zumBind < 2f)) : !tot.IsSet;
                Check(totOk, zellen
                    ? $"Tot erfasst (Weltzellen-Zweig): {(tot.IsSet ? $"Stelle {tot.localPosition.x:0}/{tot.localPosition.z:0} – {zumBind:0} m vom Bindepunkt, {zumOrt:0} m vom Sterbeort" : "keine Stelle (Start am Spawn)")} → Anmeldung {(totOk ? "am Bindepunkt/Spawn" : "AM STERBEORT")}"
                    : $"Tot erfasst (Szenenzweig): {(tot.IsSet ? "Stelle gesetzt – Anmeldung AM STERBEORT" : "keine Stelle → Anmeldung am Bindepunkt")}");
                _sp3TotErfasstOk = totOk;
                // Dungeon-Gegenprobe: mit Wiederbelebungspunkt bleibt die Weltposition vor dem Dungeon
                FieldInfo rp = typeof(NetworkMotion).GetField("_hasRespawnPoint", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo daten = typeof(NetworkInventory).GetField("_data", BindingFlags.NonPublic | BindingFlags.Instance);
                if (rp != null && daten != null && daten.GetValue(a) is CharacterSaveData d && lebend.IsSet)
                {
                    d.position = lebend;
                    bool rp0 = (bool)rp.GetValue(nm);
                    rp.SetValue(nm, true);
                    capture.Invoke(a, null);
                    rp.SetValue(nm, rp0);
                    Check(a.SavedPosition.IsSet && a.SavedPosition.Equals(lebend), "Gegenprobe Dungeon (Wiederbelebungspunkt aktiv): die Weltposition vor dem Tod bleibt erhalten");
                }
                else { _uiFehlendeHaken.Add("NetworkMotion._hasRespawnPoint/NetworkInventory._data"); Sp3NichtGeprueft("EINSTIEG-04", "Dungeon-Gegenprobe (Felder nicht gefunden)"); }
                // Nachbesserung 21.10.: Weltzellen ohne Bindepunkt (Sandbox-Welt) – ServerRespawn belebt dort am Spawn wieder, also darf
                // tot keine Stelle stehen bleiben (vorher blieb die Lebend-Stelle = Anmeldung am Sterbeort). Bindepunkt kurz abgeschaltet.
                if (zellen && bindepunkt != null && daten != null && daten.GetValue(a) is CharacterSaveData d2 && lebend.IsSet)
                {
                    d2.position = lebend;
                    bindepunkt.enabled = false;
                    bool ohneBind = BindPoint.Current == null;
                    try { capture.Invoke(a, null); }
                    finally { bindepunkt.enabled = true; }
                    Check(ohneBind && !a.SavedPosition.IsSet && BindPoint.Current == bindepunkt,
                        $"Gegenprobe ohne Bindepunkt (Sandbox-Welt): tot erfasst → {(a.SavedPosition.IsSet ? "Stelle BLEIBT (Anmeldung am Sterbeort)" : "keine Stelle (Start am Spawn wie ServerRespawn)")}; Bindepunkt danach wieder aktiv {BindPoint.Current == bindepunkt}");
                }
                else Sp3NichtGeprueft("EINSTIEG-04", "Gegenprobe ohne Bindepunkt (Sandbox-Welt): Szene ohne Weltzellen oder ohne Bindepunkt");
                // Nachbesserung 21.10.: MEHRSPIELER-02 nicht mehr als OK zählen – der echte Trennweg (OnStopServer → SaveNow im Tod →
                // Neuanmeldung über RestorePosition) ist nicht gelaufen; geprüft ist nur dieselbe Erfassung wie bei EINSTIEG-04
                Sp3Punkt("MEHRSPIELER-02", "gleiche Stelle wie EINSTIEG-04 (CapturePosition beim Trennen/Speichern im Tod) – nur Hinweis, keine eigene Prüfung");
                Info($"Erfassung im Tod (EINSTIEG-04): {(_sp3TotErfasstOk ? "Anmeldung danach nicht am Sterbeort" : "ANMELDUNG AM STERBEORT")}");
                Sp3NichtGeprueft("MEHRSPIELER-02", "echtes Trennen im Tod (OnStopServer → SaveNow → Neuanmeldung über RestorePosition in der Zelle kareth_v4) mit Nicht-Testprofil – Testprofile stellen keine Position wieder her; Spielstände nicht angefasst");
                Sp3NichtGeprueft("EINSTIEG-04", "echtes Ab-/Anmelden mit Nicht-Testprofil (Spielstände nicht angefasst) – geprüft ist die Erfassung beim Speichern");
            }
            finally
            {
                NetworkPlayer.HoldRespawn = haltVorher;
                if (!hp.IsAlive) np.ServerReviveHere(1f);
                hp.SetInvulnerableFor(7200f);
                if (SpielmenueUi.Instance != null && SpielmenueUi.Instance.IsOpen) SpielmenueUi.Instance.Schliessen();
            }
            yield return new WaitForSecondsRealtime(0.5f);
            capture.Invoke(a, null);
            Sp3Punkt("EINSTIEG-04", "wiederbelebt");
            Check(hp.IsAlive && a.SavedPosition.IsSet, "Wiederbelebt erfasst: Stelle wieder gesetzt");
            Sp3Punkt("EINSTIEG-05", "wiederbelebt");
            Check(hp.IsAlive && !input.Autorun, $"Nach dem Erwachen läuft die Figur nicht von selbst (Autorun {input.Autorun})");
            if (ziel != null) EnemyDirector.Instance.Remove(ziel);
        }

        // ================================================================== 7. Aschenweber (AUFTRAEGE-02 im Spiel, KAMPF-04)

        private IEnumerator Sp3Weber(NetworkInventory a, NetworkProgression pa, Health ah, IProgressionService svc)
        {
            Line("");
            Line("[7 Aschenweber]");
            CharacterClassDefinition weaver = GameData.Class(Sp3Weaver), warden = GameData.Class(Sp3Warden);
            if (weaver == null || warden == null) { Check(false, "Klassen Aschenweber und Eidwächter"); yield break; }
            pa.ServerDebugSwitchClass(weaver, false);
            yield return new WaitForSecondsRealtime(0.8f);
            Info($"Testfigur auf Aschenweber gestellt (Klasse {a.ServerModel.Class?.DisplayName}, Ressource {pa.ServerResourceType})");
            try
            {
                // ---------------------------------------------------------- KAMPF-04 (Aschenweber)
                Sp3Punkt("KAMPF-04", "Aschenweber: Aschenschild auf sich → Glutbolzen zeigt die GCD (vorher „bereit“, Server sagte „Noch nicht bereit.“)");
                pa.ServerDebugLearn(Sp3AshShield, 1);
                pa.ServerDebugLearn(Sp3EmberBolt, 1);
                pa.ServerDebugResetCooldowns();
                pa.ServerSetResource(100000f);
                yield return new WaitForSecondsRealtime(1.7f);
                SpellDefinition schild = GameData.Get<SpellDefinition>(Sp3AshShield);
                string why = pa.ServerCast(Sp3AshShield, ah);
                yield return new WaitForSecondsRealtime(0.25f);
                float rest = SpellClient.CooldownRemaining(Sp3EmberBolt, out float gesamt);
                Check(why == "" && schild != null && schild.cooldown > 0f && rest > 0.9f && Mathf.Approximately(gesamt, SpellClient.GcdTotal),
                    $"Aschenschild ({(why == "" ? "gewirkt" : why)}, eigene Abklingzeit {schild?.cooldown:0.#} s): Glutbolzen auf der Leiste gesperrt {rest:0.00} s von {gesamt:0.00} s");
                Check(SpellClient.CooldownRemaining(Sp3AshShield, out _) > SpellClient.GcdTotal, "Aschenschild zeigt seine eigene Abklingzeit");

                // ---------------------------------------------------------- AUFTRAEGE-02 im Spiel
                Sp3Punkt("AUFTRAEGE-02", "Im Spiel: Aschenweber gibt einen Auftrag mit Pfeil-Belohnung ab → keine Pfeile, übrige Belohnung kommt");
                QuestDefinition q = GameData.Quests.Where(x => x != null && (string.IsNullOrEmpty(x.requiredSceneTag) || x.requiredSceneTag == KC.SceneTag) &&
                                                                 x.rewardItems.Concat(x.choiceItems).Any(r => r?.item != null && r.item.IsAmmo) &&
                                                                 NpcIdentity.Find(x.TurnInNpc) != null && x.objectives.Count > 0 &&
                                                                 x.objectives.All(o => o.type == QuestObjectiveType.Collect || (o.type == QuestObjectiveType.TalkTo && o.targetId == x.TurnInNpc)))
                    .OrderBy(x => x.Id == KC.N02 ? 0 : 1).ThenBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
                if (q == null) Sp3NichtGeprueft("AUFTRAEGE-02", "kein Auftrag mit Pfeil-Belohnung, der sich über Sammelziele abgeben lässt");
                else
                {
                    var muni = q.rewardItems.Concat(q.choiceItems).Where(r => r?.item != null && r.item.IsAmmo).Select(r => r.item.Id).Distinct().ToList();
                    var andere = QuestRules.ForClass(q.rewardItems, weaver).Where(r => r?.item != null && !r.item.IsAmmo).ToList();
                    pa.ServerDebugStartQuest(q.Id);
                    yield return WaitUntil(() => svc.IsActive(q.Id), 4f);
                    foreach (QuestObjective o in q.objectives.Where(o => o.type == QuestObjectiveType.Collect)) a.ServerGive(new ItemStack(o.targetId, o.count));
                    yield return new WaitForSecondsRealtime(0.6f);
                    var vorher = muni.ToDictionary(id => id, id => a.ServerModel.Count(id));
                    yield return UiZumNpc(a, q.TurnInNpc);
                    yield return Ask(a, "npc-talk", q.TurnInNpc);
                    int wahl = QuestRules.ForClass(q.choiceItems, weaver).Count > 0 ? 0 : -1;
                    long geldV = pa.ServerMoney;
                    yield return Ask(a, "quest-turnin", wahl >= 0 ? $"{q.Id} {wahl}" : q.Id);
                    yield return WaitUntil(() => pa.ServerIsCompleted(q.Id), 4f);
                    yield return new WaitForSecondsRealtime(0.3f);
                    Info($"Abgabe angenommen: Geld +{pa.ServerMoney - geldV}k (Auftrag {q.money}k), Auftrag beim Server {(pa.ServerIsCompleted(q.Id) ? "abgeschlossen" : "NICHT abgeschlossen")}");
                    bool keine = muni.All(id => a.ServerModel.Count(id) == vorher[id]);
                    bool rest2 = andere.All(r => a.ServerModel.Count(r.item.Id) >= r.count);
                    Check(pa.ServerIsCompleted(q.Id) && keine && rest2,
                        $"„{q.Title}“ ({q.Id}) als Aschenweber abgegeben: Pfeile {string.Join(", ", muni.Select(id => $"{GameData.Item(id)?.DisplayName} {vorher[id]} → {a.ServerModel.Count(id)}"))}; " +
                        $"übrige Belohnung {(andere.Count == 0 ? "keine" : string.Join(", ", andere.Select(r => $"{r.item.DisplayName} ×{r.count}")))} {(rest2 ? "erhalten" : "FEHLT")}");
                    for (int i = 0; i < 4 && UiFensterstapel.Oberstes != ""; i++)
                    {
                        UiFensterstapel.EscAusloesen();
                        yield return UiTakt();
                    }
                }
            }
            finally
            {
                pa.ServerDebugSwitchClass(warden, true);
            }
            yield return new WaitForSecondsRealtime(0.5f);
            Info($"Testfigur zurück auf {a.ServerModel.Class?.DisplayName}");
        }

        // ================================================================== Hilfen

        private void Sp3Anlegen(string id, string titel)
        {
            if (_sp3.ContainsKey(id)) return;
            _sp3[id] = new Sp3Befund { Id = id, Titel = titel };
            _sp3Reihenfolge.Add(id);
        }

        /// <summary>Neuen Prüfpunkt zu Befund <paramref name="id"/> beginnen; Ergebnis des vorigen Punkts seinem Befund zurechnen.</summary>
        private void Sp3Punkt(string id, string punkt)
        {
            if (!string.IsNullOrEmpty(_sp3Id) && _sp3.TryGetValue(_sp3Id, out Sp3Befund b))
            {
                b.Ok += _passed - _sp3Ok0;
                b.Fehler += _failed - _sp3Fehler0;
            }
            _sp3Id = id;
            _sp3Ok0 = _passed;
            _sp3Fehler0 = _failed;
            if (string.IsNullOrEmpty(id)) return;
            if (_sp3.TryGetValue(id, out Sp3Befund neu) && !string.IsNullOrEmpty(punkt)) neu.Punkte.Add(punkt);
            Line($"  — {id}: {punkt}");
        }

        private void Sp3NichtGeprueft(string id, string was)
        {
            if (_sp3.TryGetValue(id, out Sp3Befund b)) b.NichtGeprueft.Add(was);
            Line($"  NICHT GEPRÜFT  {id}: {was}");
        }

        private void Sp3Zusammenfassung()
        {
            Line("");
            Line("[Testhaken]");
            Check(_uiFehlendeHaken.Count == 0, "Alle gelesenen Felder/Hilfen vorhanden" + (_uiFehlendeHaken.Count > 0 ? " – fehlt: " + string.Join(", ", _uiFehlendeHaken) : ""));
            Line("");
            Line("[PRÜFUNG je Befund]");
            foreach (string id in _sp3Reihenfolge)
            {
                Sp3Befund b = _sp3[id];
                string status = b.Fehler > 0 ? "FEHLGESCHLAGEN" : b.Ok == 0 ? "NICHT GEPRÜFT " : b.NichtGeprueft.Count > 0 ? "teilweise     " : "bestanden     ";
                Line($"  {status}  {id,-15} {b.Titel} ({b.Ok} ok, {b.Fehler} Fehler{(b.NichtGeprueft.Count > 0 ? $", {b.NichtGeprueft.Count}× nicht geprüft" : "")})");
            }
            Line("");
            Line("[Nicht geprüft (ehrlich)]");
            int n = 0;
            foreach (string id in _sp3Reihenfolge)
                foreach (string w in _sp3[id].NichtGeprueft) { Line($"  · {id}: {w}"); n++; }
            if (n == 0) Line("  · –");
            Line("");
            Line("[Bilder]");
            foreach (string b in _sp3Bilder) Info(b);
        }

        private void Sp3Kampftext(CombatTextEvent e)
        {
            if (e.Kind == CombatTextKind.Error) _sp3Fehlertexte.Add(e.Text);
        }

        private void Sp3LogMit(string message, string stackTrace, LogType type)
        {
            if (message != null && message.Contains("[Ashveil] Server: Angriff")) _sp3Log.Add(message);
        }

        private int Sp3LogZaehle(string teil) => _sp3Log.Count(m => m.Contains(teil));

        /// <summary>Bildschirmfoto der Spielansicht nach Logs/Ashveil/Spielablauf3/&lt;nr&gt;_&lt;name&gt;.png.</summary>
        private IEnumerator Sp3Foto(string name, float warten = 0.3f)
        {
            if (warten > 0f) yield return new WaitForSecondsRealtime(warten);
            yield return new WaitForEndOfFrame();
            Texture2D bild = null;
            try
            {
                bild = ScreenCapture.CaptureScreenshotAsTexture(Screen.height < 900 ? 2 : 1);
                string pfad = Ashveil.Core.ProjectPaths.Logs("Spielablauf3", $"{_sp3Bilder.Count + 1:00}_{name}.png");
                Directory.CreateDirectory(Path.GetDirectoryName(pfad));
                File.WriteAllBytes(pfad, bild.EncodeToPNG());
                _sp3Bilder.Add($"{pfad} ({bild.width} × {bild.height})");
                Info($"Bild: {Path.GetFileName(pfad)} ({bild.width} × {bild.height})");
            }
            catch (Exception e) { Info("Bildschirmfoto nicht möglich: " + e.Message); }
            finally { if (bild != null) Destroy(bild); }
        }
    }
}
