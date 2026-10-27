using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Npcs;
using Ashveil.Gameplay.Quests;
using Ashveil.Maps;
using Ashveil.UI;
using UnityEngine;
using UnityEngine.AI;

namespace Ashveil.Networking
{
    /// <summary>
    /// #100 Karten in Kareth v4 (Spieltest r2, 08.10.2026; Max: Minimap und Gebietskarte „Aufnahme von oben“, UI-203 Weltkarte als
    /// Pergament, PROG-013 „Zusätzlich EP je Unterzone“, UI-201). Ein Spieler, Szene Kareth v4:
    ///   1. Daten: Kartenbezug, Szenenraster, Unterzonen an bekannten Orten.
    ///   2. Anmelden: Startunterzone bekannt ohne EP, Client bekommt die Liste vom Server, Banner und Minimap folgen der Figur,
    ///      die Auftragsliste rückt unter die Minimap.
    ///   3. Entdeckung: nach Untersteg versetzt → genau 20 + 5 × 4 EP vom Server, Client und Banner melden „Entdeckt“.
    ///   4. Kein zweites Mal; Wiederverbinden (ganze Liste neu); Speicherstand (discoveredAreas).
    ///   5. Karte: Gebietskarte mit Schleier über den unentdeckten Unterzonen, Weltkarte mit genau einem bekannten Gebiet.
    ///   6. Minimap: Zoom, Orte in der Nähe; Rechenzeit der Rasterabfrage.
    /// Bildschirmfotos (HUD, Gebietskarte, Weltkarte): Logs/Ashveil/Karten/. Start: Auftrag „spieltest karten“.
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string KartenFlag = "karten";
        private static bool _kartenMode;
        private const string KartenProfile = "duo-karten";

        private IEnumerator KartenSuite()
        {
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(KartenProfile)) != null, 40f);
            Check(a != null, "Spieler angemeldet (neue Figur)");
            if (a == null) yield break;
            var pa = a.GetComponent<NetworkProgression>();
            if (pa == null) { Check(false, "Spieler hat Fortschritt"); yield break; }
            a.GetComponent<Health>()?.SetInvulnerableFor(3600f);

            // ---------------------------------------------------------- 1. Daten
            Line("");
            Line("[Kartendaten]");
            KartenBezug kb = KartenBezug.Instanz;
            Check(kb != null && kb.Dienst != null, "Kartenbezug in der Szene, Daten geladen" + (kb != null && !string.IsNullOrEmpty(kb.Fehler) ? " – " + kb.Fehler : ""));
            if (kb == null || kb.Dienst == null) yield break;
            ZonenDienst d = kb.Dienst;
            Check(d.Szene != null && d.SzenenZone != null && d.SzenenZone.key == "kareth", $"Szenenraster {d.Szene?.name} (Gebiet {d.SzenenZone?.name})");
            Info($"{d.Daten.zonen.Length} Gebiete, {d.AnzahlUnterzonen} Unterzonen, Stand {d.Daten.stand}");
            (string ort, float x, float z, int id)[] proben =
            {
                ("Wachtfeuer", 0f, 0f, 1), ("Untersteg", 715f, 275f, 2), ("Holzfällerlager", -785f, -646f, 3), ("Fischerhütten", -1155f, 275f, 4),
                ("Glockenrast", 330f, 1375f, 5), ("Aschenwacht", 1536f, 386f, 6), ("Wüstungshöfe", -1430f, -1540f, 7), ("Nördlicher Aufstieg", 770f, 1595f, 8)
            };
            int treffer = 0;
            foreach (var pr in proben)
            {
                UnterzoneDaten u = kb.UnterzoneBei(new Vector3(pr.x, 0f, pr.z));
                if (u != null && u.id == pr.id) treffer++;
                else Info($"{pr.ort} ({pr.x:0}/{pr.z:0}): {u?.name ?? "keine Unterzone"} statt Nummer {pr.id}");
            }
            Check(treffer == proben.Length, $"Unterzonen an den Orten wie gebaut ({treffer}/{proben.Length})");
            Check(kb.ZoneBei(new Vector3(-2200f, 0f, -1950f))?.key == "kareth" && kb.ZoneBei(new Vector3(1800f, 0f, 2050f))?.key == "kareth",
                "Ganzes Gelände gehört zu Kareth (Ecken des Szenenrasters)");

            // ---------------------------------------------------------- 2. Anmelden
            Line("");
            Line("[Anmelden]");
            yield return new WaitForSecondsRealtime(3.5f);   // Anmeldeverzug 2 s + Prüftakt
            Check(pa.ServerMapDiscovered(1), "Startunterzone Wachtfeuer-Mulde ist bekannt");
            Check(pa.ServerLastMapDiscovery == "", "Beim Anmelden keine Entdeckungs-EP" + (pa.ServerLastMapDiscovery != "" ? ": " + pa.ServerLastMapDiscovery : ""));
            yield return WaitUntil(() => KartenEntdeckungen.Enthaelt(1), 5f);
            Check(KartenEntdeckungen.Enthaelt(1) && KartenEntdeckungen.Anzahl == pa.ServerMapDiscoveredCount,
                $"Client kennt, was der Server meldet ({KartenEntdeckungen.Anzahl} Unterzone/n, Server {pa.ServerMapDiscoveredCount})");
            KartenSystem ks = KartenSystem.Instanz;
            Check(ks != null && ks.HatSpieler && ks.Banner != null && ks.Minimap != null && ks.Fenster != null, "Kartensystem mit Banner, Minimap und Karte folgt der eigenen Figur");
            if (ks == null || ks.Minimap == null || ks.Fenster == null || ks.Banner == null) yield break;
            Check(ks.AktuelleZone?.key == "kareth" && ks.AktuelleUnterzone?.id == 1, $"Anzeige: {ks.AktuelleZone?.name} · {ks.AktuelleUnterzone?.name}");
            Check(ks.Minimap.SichtbareKacheln > 0 && ks.Minimap.Ortsname == "Wachtfeuer-Mulde", $"Minimap: {ks.Minimap.SichtbareKacheln} Kachelplätze, Ort „{ks.Minimap.Ortsname}“");
            Check(Mathf.Approximately(HudPlatz.ObenRechts, 264f), $"Auftragsliste rückt unter die Minimap (oben rechts belegt: {HudPlatz.ObenRechts:0} px)");
            yield return KartenFoto("Karten_1_HUD_Start");

            // ---------------------------------------------------------- 3. Entdeckung
            Line("");
            Line("[Entdeckung (PROG-013)]");
            long gesamt0 = GrTotal(a, pa);
            Vector3 untersteg = KartenBoden(715f, 275f);
            yield return Ask(a, "goto", V(untersteg, 0f));
            yield return new WaitForSecondsRealtime(1.5f);
            int soll = Progression.DiscoveryXp(4);
            Check(pa.ServerMapDiscovered(2), "Untersteg und Feldmark entdeckt (Server)");
            Check(pa.ServerLastMapDiscovery == "uz.kareth.2+" + soll, $"Entdeckungs-EP vom Server: {pa.ServerLastMapDiscovery} (Soll uz.kareth.2+{soll}, Stufe 4 der Unterzone)");
            long dazu = GrTotal(a, pa) - gesamt0;
            Check(dazu >= soll, $"Erfahrung gestiegen: +{dazu} (darin {soll} für die Unterzone; dazu ggf. Quest-Orte)");
            yield return WaitUntil(() => KartenEntdeckungen.Enthaelt(2), 5f);
            Check(KartenEntdeckungen.Enthaelt(2) && KartenEntdeckungen.LetzteMeldung == "Untersteg und Feldmark+" + soll,
                $"Client: Unterzone 2 bekannt, Meldung „{KartenEntdeckungen.LetzteMeldung}“");
            yield return new WaitForSecondsRealtime(0.3f);
            Check(ks.AktuelleUnterzone?.id == 2 && ks.Banner.Unterzone == "Untersteg und Feldmark" && ks.Banner.EntdecktZeile.Contains("Untersteg und Feldmark"),
                $"Banner: „{ks.Banner.Unterzone}“ / „{StripTags(ks.Banner.EntdecktZeile)}“");
            yield return KartenFoto("Karten_2_HUD_Entdeckt");

            // ---------------------------------------------------------- 4. Kein zweites Mal, Wiederverbinden, Speicherstand
            Line("");
            Line("[Einmal je Figur]");
            string vorher = pa.ServerLastMapDiscovery;
            int anzahl = pa.ServerMapDiscoveredCount;
            long xpVorher = GrTotal(a, pa);
            GameObject spawn = GameObject.Find("PlayerSpawn");
            Vector3 start = spawn != null ? spawn.transform.position : KartenBoden(0f, -60f);
            yield return Ask(a, "goto", V(start + Vector3.up * 0.3f, 0f));
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Ask(a, "goto", V(untersteg, 0f));
            yield return new WaitForSecondsRealtime(1.5f);
            Check(pa.ServerLastMapDiscovery == vorher && pa.ServerMapDiscoveredCount == anzahl && GrTotal(a, pa) == xpVorher,
                $"Zweites Betreten: keine EP, nichts Neues ({pa.ServerMapDiscoveredCount} Unterzonen, +{GrTotal(a, pa) - xpVorher} EP)");
            KartenEntdeckungen.Leeren();
            pa.ServerKartenNeuSenden();
            yield return WaitUntil(() => KartenEntdeckungen.Anzahl >= anzahl, 5f);
            Check(KartenEntdeckungen.Enthaelt(1) && KartenEntdeckungen.Enthaelt(2) && KartenEntdeckungen.Anzahl == anzahl,
                $"Wiederverbinden: ganze Liste vom Server ({KartenEntdeckungen.Anzahl}/{anzahl})");
            Check(a.ServerData != null && a.ServerData.discoveredAreas != null &&
                  a.ServerData.discoveredAreas.Contains("uz.kareth.1") && a.ServerData.discoveredAreas.Contains("uz.kareth.2"),
                "Im Speicherstand (discoveredAreas): uz.kareth.1 und uz.kareth.2");

            // ---------------------------------------------------------- 5. Karte (M)
            Line("");
            Line("[Karte]");
            ks.Fenster.Umschalten();
            yield return new WaitForSecondsRealtime(0.6f);
            Check(ks.Fenster.Offen && ks.Fenster.ZeigtGebiet != null && ks.Fenster.ZeigtGebiet.key == "kareth", "Karte öffnet mit der Gebietskarte Kareth");
            Check(ks.Fenster.VerhuellteUnterzonen == 6, $"Gebietskarte: {ks.Fenster.VerhuellteUnterzonen} Unterzonen verhüllt (Soll 6), Namen und Orte nur entdeckt ({ks.Fenster.Beschriftungen} Beschriftungen)");
            Check(File.Exists(Path.Combine(d.Ordner, "gebietskarten", "kareth.jpg")), "Gebietskarte Kareth (Aufnahme von oben) vorhanden");
            yield return KartenFoto("Karten_3_Gebietskarte");
            ks.Fenster.ZeigeWelt();
            yield return new WaitForSecondsRealtime(0.6f);
            Check(ks.Fenster.ZeigtWelt && ks.Fenster.BekannteGebiete == 1, $"Weltkarte: {ks.Fenster.BekannteGebiete} Gebiet bekannt (Kareth), alle anderen verhüllt");
            yield return KartenFoto("Karten_4_Weltkarte");
            ks.Fenster.Schliessen();
            yield return null;
            Check(!ks.Fenster.Offen, "Karte schließt");

            // ---------------------------------------------------------- 6. Minimap und Rechenzeit
            Line("");
            Line("[Minimap]");
            float s0 = ks.Minimap.Sichtweite;
            ks.Minimap.Zoomen(1);
            float naeher = ks.Minimap.Sichtweite;
            ks.Minimap.Zoomen(-1);
            ks.Minimap.Zoomen(-1);
            float weiter = ks.Minimap.Sichtweite;
            ks.Minimap.Zoomen(1);
            Check(naeher < s0 && weiter > s0 && Mathf.Approximately(ks.Minimap.Sichtweite, s0), $"Zoom: {naeher:0} / {s0:0} / {weiter:0} m");
            yield return new WaitForSecondsRealtime(0.2f);
            Check(ks.Minimap.Punkte > 0, $"Orte in der Nähe von Untersteg auf der Minimap ({ks.Minimap.Punkte} Punkte, keine Gegner)");
            var uhr = Stopwatch.StartNew();
            int n = 0;
            for (int i = 0; i < 100000; i++) if (kb.UnterzoneBei(new Vector3(-2000f + (i % 400) * 10f, 0f, -1900f + (i / 400) * 15f)) != null) n++;
            uhr.Stop();
            Info($"Rasterabfrage: 100 000 Abfragen in {uhr.Elapsed.TotalMilliseconds:0.0} ms ({uhr.Elapsed.TotalMilliseconds * 10.0:0} ns je Abfrage; Server prüft je Spieler alle 0,5 s einmal)");

            // ---------------------------------------------------------- 7. Auftragsführung A/B/C (Max 10.10.2026)
            Line("");
            Line("[Auftragsführung (A/B/C, UI-202/205)]");
            // 14.10.2026: Weltzellen bringen eigene Führung mit (Westweiden 29, Changelog (117)) – Soll = Kareth 60 + weitere Szenenraster
            int weitere = 0;
            if (ks.Dienst != null)
                foreach (Ashveil.Maps.SzenenRasterDatei w in ks.Dienst.WeitereSzenen)
                {
                    Ashveil.Maps.FuehrungDatei fw = Ashveil.Maps.QuestFuehrung.Laden(ks.Dienst.Ordner, w.name, out _);
                    if (fw?.auftraege != null) weitere += fw.auftraege.Count(fa => fa != null && !string.IsNullOrEmpty(fa.quest));
                }
            Check(ks.Fuehrung != null && ks.Fuehrung.Auftraege == 60 + weitere,
                $"Auftragsführung geladen: {ks.Fuehrung?.Auftraege ?? 0} Aufträge (Soll {60 + weitere} = Kareth 60 [29 + Nebenaufträge N01–N15 + Classic-Paket 11 + Akt I R1–R5] + {weitere} aus weiteren Szenenrastern; Stand {ks.Fuehrung?.Stand})");
            Check(ks.AnzahlQuestgebiete == 0 && ks.AnzahlQuestUnterzonen == 0, "Ohne Auftrag: keine Kreise, keine Unterzonen");
            // A: Q1 annehmen → ein Kreis (80 m) über dem Lager; auf der Minimap erst ein Randmarker (Untersteg), im Lager der Kreis selbst
            QuestDefinition q1 = GameData.Quest("quest.blade_for_the_watch");
            yield return GotoNpc(a, q1.giverNpc);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Ask(a, "npc-talk", q1.giverNpc);
            yield return Ask(a, "quest-accept", q1.Id);
            yield return WaitUntil(() => ks.AnzahlQuestgebiete == 1, 5f);
            KartenSystem.Questgebiet kreis = default;
            foreach (KartenSystem.Questgebiet g in ks.Questgebiete) kreis = g;
            Vector2 tor = kb.ZuWelt(new Vector3(4f, 0f, 44f));          // Q1: Ilse vor dem Nordtor (WORLD-240) – Kreis über Tor und Lager
            Check(ks.AnzahlQuestgebiete == 1 && Mathf.Approximately(kreis.Radius, 80f) && Vector2.Distance(new Vector2(kreis.X, kreis.Z), tor) < 30f,
                $"Q1 angenommen: ein weicher Kreis über Nordtor und Lager ({kreis.Radius:0} m, {Vector2.Distance(new Vector2(kreis.X, kreis.Z), tor):0} m neben dem Tor)");
            yield return null;
            Check(ks.Minimap.Kreise == 1 && ks.Minimap.Randmarker == 0, $"Minimap im Lager: Kreis im Bild ({ks.Minimap.Kreise}), kein Randmarker ({ks.Minimap.Randmarker})");
            ks.Fenster.Umschalten();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return KartenFoto("Karten_5_Auftragskreis");
            ks.Fenster.Schliessen();
            yield return Ask(a, "goto", V(untersteg, 0f));
            yield return new WaitForSecondsRealtime(0.5f);
            Check(ks.Minimap.Kreise == 0 && ks.Minimap.Randmarker == 1, $"Minimap in Untersteg (770 m): Randmarker zum Lager ({ks.Minimap.Randmarker}), kein Kreis ({ks.Minimap.Kreise})");
            yield return KartenFoto("Karten_6_Randmarker");
            yield return Ask(a, "quest-abandon", q1.Id);
            yield return WaitUntil(() => ks.AnzahlQuestgebiete == 0, 5f);
            Check(ks.AnzahlQuestgebiete == 0 && ks.Minimap.Randmarker == 0, "Q1 abgebrochen: Kreis und Randmarker weg");
            // B: nur die Unterzone – Name und Ring auf der Gebietskarte (auch unentdeckt), nichts auf der Minimap
            ks.Fenster.Umschalten();
            yield return new WaitForSecondsRealtime(0.5f);
            int marken0 = ks.Fenster.Beschriftungen;
            ks.ZeigeQuestUnterzone("probe.b", 4, QuestFuehrung.Farbe);
            yield return new WaitForSecondsRealtime(0.3f);
            Check(ks.AnzahlQuestUnterzonen == 1 && ks.Fenster.Beschriftungen == marken0 + 3 && ks.Minimap.Kreise == 0 && ks.Minimap.Randmarker == 0,
                $"B (Salzsee-Ufer): Name, „Auftrag“ und Ring auf der Gebietskarte (+{ks.Fenster.Beschriftungen - marken0}), nichts auf der Minimap");
            yield return KartenFoto("Karten_7_Unterzone_B");
            ks.EntferneQuestUnterzone("probe.b");
            yield return new WaitForSecondsRealtime(0.3f);
            Check(ks.AnzahlQuestUnterzonen == 0 && ks.Fenster.Beschriftungen == marken0, "B entfernt: Gebietskarte wie vorher");
            ks.Fenster.Schliessen();
            // C: unmarkiert – G7 und U-7 ohne Kreis und ohne Unterzone in der Führung, Assets unmarkiert
            FuehrungAuftrag g7 = ks.Fuehrung.Eintrag(Ashveil.Data.GlockenrastCatalog.G7), u7 = ks.Fuehrung.Eintrag(Ashveil.Data.KarethCatalog.U7);
            Check(g7 != null && g7.stufe == "C" && u7 != null && u7.stufe == "C" && GameData.Quest(Ashveil.Data.GlockenrastCatalog.G7).unmarked && GameData.Quest(Ashveil.Data.KarethCatalog.U7).unmarked,
                "C: G7 „Der älteste Herd“ und U-7 unmarkiert, ohne Kreis (Max 10.10.)");

            // ---------------------------------------------------------- 8. Lager: Einladung beim Ankommen, Lockrufe, Ilse vor dem Nordtor (WORLD-238/239/240)
            Line("");
            Line("[Lager (Max 10.10.: Einladung, Lockrufe, erster Auftrag nach draußen)]");
            yield return WaitUntil(() => Ashveil.Gameplay.Story.StoryDirector.LastFinished == NetworkProgression.SzeneAnkunft || !Ashveil.Gameplay.Story.StoryDirector.Active, 25f);
            Check(a.ServerData != null && a.ServerData.storyFlags != null && a.ServerData.storyFlags.Contains(NetworkProgression.FlagAnkunft) && pa.ServerLastScene == NetworkProgression.SzeneAnkunft,
                $"Erstes Betreten des Lagers (beim Weg zu Bralt): Marke {NetworkProgression.FlagAnkunft} gesetzt, Szene „{pa.ServerLastScene}“ gespielt");
            Check(Ashveil.Gameplay.Story.StoryDirector.LastFinished == NetworkProgression.SzeneAnkunft, $"Client hat die Einladung (Wenna) zu Ende gespielt (zuletzt: „{Ashveil.Gameplay.Story.StoryDirector.LastFinished}“)");
            Check(DialogueLibrary.Scene(NetworkProgression.SzeneAnkunft) != null && DialogueLibrary.Scene(NetworkProgression.SzeneAnkunft).Lines.Count == 3 && Loc.Has("dlg.kareth.ankunft.03"),
                "Szene kareth_ankunft: drei Zeilen, Texte in der Texttabelle");
            string szeneVorher = pa.ServerLastScene;
            yield return Ask(a, "goto", V(KartenBoden(0f, -20f), 0f));
            yield return new WaitForSecondsRealtime(1.5f);
            Check(pa.ServerLastScene == szeneVorher && a.ServerData.storyFlags.Count(f => f == NetworkProgression.FlagAnkunft) == 1, "Zweites Betreten: keine zweite Einladung");
            var gespraeche = FindAnyObjectByType<Ashveil.UI.NpcGespraeche>();
            KarethGespraeche.Gespraech[] alle = KarethGespraeche.Alle();
            Check(gespraeche != null && alle.Length >= 15 && alle.All(g => g.Zeilen.All(z => Loc.Has(z.Key))),
                $"Lockrufe: Gesprächsschnipsel in der Szene ({alle.Length} Gespräche in Lager und Untersteg, alle Texte vorhanden)");
            string ohne = string.Join(", ", alle.SelectMany(g => g.Figuren).Distinct().Where(n => NpcRoutineRegistry.Find(n) == null));
            Check(ohne.Length == 0, "Lockrufe: alle Figuren der Gespräche stehen in der Szene (Tagesabläufe)" + (ohne.Length > 0 ? " – fehlt: " + ohne : ""));
            Check(new[] { "Grete Bachmann", "Hennek Doll", "Idun Vesper", "Mutter Ansa" }.All(n => NpcRoutineRegistry.Find(n) != null) && GameData.Npc(Ashveil.Data.KarethCatalog.Idun)?.vendor == true,
                "Untersteg (Sheets v1): Grete, Hennek, Idun (Händlerin), Mutter Ansa in der Szene");
            NpcIdentity ilse = NpcIdentity.Find("npc.ilse_harrow");
            NpcRoutine ilseAblauf = ilse != null ? ilse.GetComponentInParent<NpcRoutine>() : null;
            bool ilseDraussen = ilseAblauf != null && Enumerable.Range(0, ilseAblauf.StepCount).Any(i => ilseAblauf.Step(i).note.Contains("Nordtor"));
            Check(ilseDraussen, "Ilse Harrow steht tagsüber vor dem Nordtor (Tagesablauf, WORLD-240)");
            Check(GameData.Quest(q1.Id).summary.Contains("Nordtor") && GameData.Quest(q1.Id).completeText.Contains("Steinmann"), "Q1: Bralt schickt vor das Nordtor, Ilse zeigt den Aussichtsfels");

            // ---------------------------------------------------------- 9. Abmelden: sofort im Lager, Countdown draußen (COMBAT-094)
            Line("");
            Line("[Abmelden (Max 10.10.: Countdown außerhalb, sofort im Lager)]");
            Check(NetworkInventory.ServerSichererOrt(a.transform.position), "Im Lager: sicherer Ort");
            Vector3 draussen = KartenBoden(-100f, 300f);
            yield return Ask(a, "goto", V(draussen, 0f));
            yield return new WaitForSecondsRealtime(0.8f);
            Check(!NetworkInventory.ServerSichererOrt(a.transform.position), "Draußen (−100/300): kein sicherer Ort");
            SitzungClient.SpeichernAnfragen();
            yield return WaitUntil(() => SitzungClient.LetzterCountdown > 0f, 5f);
            Check(a.ServerAbmeldenLaeuft && Mathf.Approximately(SitzungClient.LetzterCountdown, NetworkInventory.AbmeldenSekunden),
                $"Draußen: Server wartet {SitzungClient.LetzterCountdown:0} s (Soll {NetworkInventory.AbmeldenSekunden:0})");
            yield return Ask(a, "goto", V(draussen + new Vector3(3f, 0f, 0f), 0f));
            yield return WaitUntil(() => !a.ServerAbmeldenLaeuft, 5f);
            Check(!a.ServerAbmeldenLaeuft && a.ServerAbmeldenAbbruch == "Bewegung" && SitzungClient.LetzterAbbruch == "Bewegung", $"Bewegung bricht ab (Server „{a.ServerAbmeldenAbbruch}“, Client „{SitzungClient.LetzterAbbruch}“)");
            yield return new WaitForSecondsRealtime(2.2f);
            int antworten = SitzungClient.AntwortZahl;
            yield return Ask(a, "goto", V(KartenBoden(0f, -20f), 0f));
            yield return new WaitForSecondsRealtime(0.8f);
            SitzungClient.SpeichernAnfragen();
            yield return WaitUntil(() => SitzungClient.AntwortZahl > antworten, 8f);
            Check(SitzungClient.AntwortZahl > antworten && SitzungClient.LetztesOk && !a.ServerAbmeldenLaeuft, "Im Lager: sofort gespeichert und bestätigt, kein Countdown");
        }

        /// <summary>Begehbarer Boden nahe (x, z) in Szenenkoordinaten, leicht angehoben.</summary>
        private static Vector3 KartenBoden(float x, float z)
        {
            Vector3 p = Gnd(x, z, 0f);
            if (NavMesh.SamplePosition(p, out NavMeshHit hit, 15f, NavMesh.AllAreas)) p = hit.position;
            return p + Vector3.up * 0.3f;
        }

        /// <summary>Bildschirmfoto mit HUD (Bildschirm-Oberfläche) nach Logs/Ashveil/Karten/&lt;name&gt;.png.</summary>
        private IEnumerator KartenFoto(string name)
        {
            yield return new WaitForSecondsRealtime(0.4f);
            yield return new WaitForEndOfFrame();
            Texture2D shot = null;
            try
            {
                shot = ScreenCapture.CaptureScreenshotAsTexture();
                string pfad = Ashveil.Core.ProjectPaths.Logs("Karten", name + ".png");
                Directory.CreateDirectory(Path.GetDirectoryName(pfad));
                File.WriteAllBytes(pfad, shot.EncodeToPNG());
                Info($"Bild: {pfad} ({shot.width} × {shot.height})");
            }
            catch (Exception e) { Info("Bildschirmfoto nicht möglich: " + e.Message); }
            finally { if (shot != null) Destroy(shot); }
        }

        private static string StripTags(string s) => string.IsNullOrEmpty(s) ? "" : System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", "");
    }
}
