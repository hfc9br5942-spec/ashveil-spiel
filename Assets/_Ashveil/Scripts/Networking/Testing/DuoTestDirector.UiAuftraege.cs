using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Ashveil.Core.Events;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Quests;
using Ashveil.UI;
using UnityEngine;
using UnityEngine.AI;
using KC = Ashveil.Data.KarethCatalog;

namespace Ashveil.Networking
{
    /// <summary>
    /// REP-20261021-059 (Commit 089ee93): die neuen UI-Zustände sichtbar im Spiel prüfen – ein Spieler, Szene Kareth v4.
    ///   A. Auftragsbuch (F3): 12 laufende Aufträge → Seite 1 mit 11 Zeilen, „Seite 1 / 2“, Pfeil &gt;; Seite 2 mit Auftrag 12 (wählbar);
    ///      Auftrag 12 aufgeben → eine Seite ohne Pfeile; 20 Aufträge → Seite 2 zeigt 9; Zusatz: die Seite folgt dem gewählten Auftrag.
    ///   B. Tracker (F2): bei vielen Aufträgen endet er über „Einstellungen (O)“ mit „… und X weitere (L)“; Rucksack offen →
    ///      Tracker-Ebene unter dem Taschenfenster.
    ///   C. Abgabe bei vollem Rucksack (B2): Abgabe-Ansicht bleibt, rote Zeile „Kein Platz im Inventar.“; nach Platz schaffen → Liste.
    ///   D. „Entkommen“ (F1): heimkehrender Gegner (Leine überschritten), ECHTE Schläge der Figur (Ziel + automatischer Angriff,
    ///      WeaponUser → NetworkCombat.ServerMelee → CombatResolver) → Kampftext, kein Schaden; geprüft auch, was der
    ///      Kampftext-Renderer wirklich zeigt. Drossel 1,5 s (ein Zusatztreffer über den Serverweg, damit er sicher innerhalb liegt,
    ///      plus Regelprüfung über alle Treffer). Testhilfe: der Wolf wird auf dem Heimweg angehalten (Zustand bleibt Return).
    ///      Zusatz (Nr. 3): Beuteberechtigung nach der Heimkehr leer.
    ///   21.10. (Nr. 7/8/9-Runde) – Reihenfolge F, G, E (E zuletzt, weil die Figur dort stirbt):
    ///   F. Rückkauf (Nr. 9): 6 Teile beim Händler verkauft → 6 Rückkaufzeilen (vorher 3) über Hinweis und Geld; Zeile 6 anklicken kauft zurück.
    ///   G. Annehmen bei 20 laufenden Aufträgen: Server lehnt ab „Zu viele Aufträge.“, Angebots-Ansicht bleibt mit „Annehmen“, rote Zeile;
    ///      ein Auftrag weniger → Annehmen klappt, Ansicht wechselt zur Liste.
    ///   E. Gefallene (Nr. 8): Tod bei offenem Gespräch/Händler/Beute → alles schließt sich (Client und Server); tot Plündern, Gespräch,
    ///      Annehmen, Fundstelle → „Du bist tot.“; nach der Wiederbelebung gehen Gespräch und Beute wieder.
    ///   21.10. (Nachbesserung, Fund aus Bild 15) – H im Aufbau von E (H1 vor dem Tod, H2 nach der Wiederbelebung):
    ///   H. Beute neben Gespräch und Händler statt darunter: keine Überdeckung (auch nicht mit Lehrer und Taschen), ganz im Bild,
    ///      „Alles nehmen“ frei; kein Klickpunkt auf Zeilen und Fenstern der Gesprächsoberfläche trifft eine Beutezeile (InventoryUi.LootAt).
    ///   21.10. (Max: „Nach der Abgabe automatisch alle Reste dieses Questgegenstands entfernen; beim Aufgeben behalten.“) – I vor E:
    ///   I. Questgegenstand-Reste: Abgabe mit Überschuss des eigenen Questgegenstands → 0 Stück im Rucksack (I1); Gegenprobe Material
    ///      (verkäuflich) und fremder Questgegenstand → Überschuss bleibt (I2); Aufgeben über das Auftragsbuch behält Questgegenstände (I3).
    ///   21.10. (Max: „ok lehne ab“ – Questgegenstand nur plündern, solange der Auftrag läuft und noch welche fehlen) – J vor I und E
    ///   (J braucht G3 „Gegengift“ noch nicht abgegeben, I gibt ihn ab; in E stirbt die Figur):
    ///   J. Questbeute nur bei Bedarf, nicht Benötigtes wird aus der persönlichen Beute verworfen (nach der Review: eine Leiche mit nur
    ///      gesperrter Questbeute glimmte weiter und versperrte die Nachlese). Je Punkt eine Leiche mit fester persönlicher Beute (Testweg
    ///      CorpseLoot.SetFixed). Auftrag läuft nicht, nur Questgegenstand → nichts aufgenommen, verworfen, „Brauchst du nicht mehr.“,
    ///      Beutefenster zu, Leiche glimmt nicht mehr, öffnet sich nicht wieder (J1); 5/6 → von 3 nur 1, 2 verworfen, übrige Beute bleibt
    ///      (J2); „Alles nehmen“ bei 6/6 nimmt Material, Ausrüstung und Glockensplitter (Questgegenstand ohne Auftrag), Questrest
    ///      verworfen, Leiche leer, kein „Kein Platz“ (J3); 0/6 → beide, keine Meldung (J4); Rucksack voll, 0/6 → nichts verworfen, beide
    ///      bleiben, „Kein Platz …“ (J5).
    /// Steuerung per Code (simulierte Tasten erreichen das Spiel nicht zuverlässig): Fenster und Klicks der QuestUi werden über
    /// Reflexion nachgestellt – Trefferprüfung mit den eigenen Hilfen der QuestUi (Clicked/RowAt an der Bildschirmmitte des
    /// Elements), danach genau die Zuweisungen bzw. Dienst-Aufrufe des Klick-Zweigs. Esc über <see cref="UiFensterstapel.EscAusloesen"/>.
    /// Keine Spiel-Logik geändert. Bilder: Logs/Ashveil/UiAuftraege/&lt;nr&gt;_&lt;name&gt;.png (volle Auflösung der Spielansicht).
    /// Start: Auftrag „spieltest ui_auftraege“. Bericht: Logs/Ashveil/Ashveil_UiAuftraegeTest.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string UiAuftraegeFlag = "ui_auftraege";
        private static bool _uiAuftraegeMode;
        private const string UiAuftraegeProfile = "duo-uiauftraege";

        private const BindingFlags UiFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string UiKeinPlatz = "Kein Platz im Inventar.";
        private const string UiZuViele = "Zu viele Aufträge.";   // 21.10.: QuestRules.CanAccept bei 20 laufenden
        private const string UiTot = "Du bist tot.";             // 21.10.: Server-Ablehnung für Gefallene (Nr. 8)
        private const string UiNichtMehr = "Brauchst du nicht mehr.";   // 21.10. (J): nicht benötigter Questgegenstand wird aus der persönlichen Beute verworfen
        private const string UiKeinPlatzBeute = "Kein Platz im Inventar – der Rest bleibt liegen.";   // 21.10. (J5): Platzmangel – nichts verworfen

        private readonly List<(string punkt, bool ok)> _uiPunkte = new List<(string, bool)>();
        private readonly List<string> _uiBilder = new List<string>();
        private readonly List<string> _uiFehlertexte = new List<string>();
        private readonly HashSet<string> _uiFehlendeHaken = new HashSet<string>();
        private string _uiPunkt;
        private int _uiPunktFehler;
        private int _uiEntkommen;
        private CombatTextEvent _uiLetztesEntkommen;
        private readonly List<(float t, int bild)> _uiEntkommenZeiten = new List<(float, int)>();

        private IEnumerator UiAuftraegeSuite()
        {
            // Zustand frisch (der Editor kann private Felder beim Anlegen mit "" statt null belegen – Lauf 2 hatte deshalb
            // einen namenlosen ersten Punkt in der Zusammenfassung)
            _uiPunkt = null;
            _uiPunkte.Clear();
            _uiBilder.Clear();
            _uiFehlertexte.Clear();
            _uiFehlendeHaken.Clear();
            _uiEntkommen = 0;
            _uiEntkommenZeiten.Clear();
            UiPunkt("Vorbereitung: Spieler angemeldet, Auftragsoberfläche, Auftragspool");
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(UiAuftraegeProfile)) != null, 40f);
            Check(a != null, "Spieler angemeldet (neue Figur)");
            if (a == null) yield break;
            var pa = a.GetComponent<NetworkProgression>();
            if (pa == null) { Check(false, "Spieler hat Fortschritt"); yield break; }
            a.GetComponent<Health>()?.SetInvulnerableFor(7200f);
            yield return new WaitForSecondsRealtime(3f);
            yield return WaitUntil(() => !Ashveil.Gameplay.Story.StoryDirector.Active, 25f);

            QuestUi ui = FindAnyObjectByType<QuestUi>();
            IProgressionService svc = ProgressionClient.Current;
            Check(ui != null && svc != null && svc.Ready && ReferenceEquals(UiFeld(ui, "_svc"), svc),
                "Auftragsoberfläche (QuestUi) läuft mit dem eigenen Fortschritt des Editor-Spielers");
            if (ui == null || svc == null) yield break;
            Info($"Spielansicht {Screen.width} × {Screen.height} px; Testfigur {a.ServerModel.Class?.DisplayName}, Stufe {a.ServerModel.Level}");

            string ordner = Ashveil.Core.ProjectPaths.Logs("UiAuftraege");
            Directory.CreateDirectory(ordner);
            foreach (string alt in Directory.GetFiles(ordner, "*.png"))
                try { File.Delete(alt); } catch (IOException) { }

            QuestDefinition abgabe = UiAbgabeAuftrag(a.ServerModel.Class);
            EventBus<CombatTextEvent>.Subscribe(UiKampftext);
            try
            {
                yield return UiBuch(a, pa, ui, svc, UiAuftragsPool(abgabe != null ? abgabe.Id : null));
                yield return UiTracker(a, pa, ui, svc);
                yield return UiAbgabe(a, pa, ui, svc, abgabe);
                yield return UiEntkommen(a);
                // 21.10.: Nr. 9, Annehmen-Ansicht, Nr. 8 – Gefallene zuletzt (die Figur stirbt dort)
                yield return UiRueckkauf(a, pa, ui, svc);
                yield return UiAnnehmen(a, pa, ui, svc);
                yield return UiQuestBeute(a, pa, svc);       // 21.10.: Questbeute nur bei Bedarf (vor I – I gibt G3 ab; vor E – dort stirbt die Figur)
                yield return UiQuestReste(a, pa, ui, svc);   // 21.10.: Questgegenstand-Reste nach Abgabe (vor E – dort stirbt die Figur)
                yield return UiGefallen(a, pa, ui, svc);
            }
            finally
            {
                EventBus<CombatTextEvent>.Unsubscribe(UiKampftext);
            }
            UiPunkt(null);

            Line("");
            Line("[Testhaken]");
            Check(_uiFehlendeHaken.Count == 0, "Alle gelesenen Felder/Hilfen der Oberfläche vorhanden" +
                                              (_uiFehlendeHaken.Count > 0 ? " – fehlt: " + string.Join(", ", _uiFehlendeHaken) : ""));
            Line("");
            Line("[PRÜFUNG je Punkt]");
            foreach ((string punkt, bool ok) in _uiPunkte) Line($"  {(ok ? "bestanden     " : "fehlgeschlagen")}  {punkt}");
            Line("");
            Line("[Bilder]");
            foreach (string b in _uiBilder) Info(b);
        }

        // ================================================================== A. Auftragsbuch

        private IEnumerator UiBuch(NetworkInventory a, NetworkProgression pa, QuestUi ui, IProgressionService svc, List<string> pool)
        {
            Line("");
            Line("[A Auftragsbuch: Seiten (F3)]");
            Check(pool.Count >= 26, $"Aufträge für den Test: {pool.Count} aus Kareth v4 (über den Testweg ServerDebugStartQuest, ohne Vorbedingungen)");
            if (pool.Count < 21) yield break;
            int benutzt = 0;

            // ---------------------------------------------------------- A1
            UiPunkt("A1 12 Aufträge → Buch Seite 1: 11 Zeilen, „Seite 1 / 2“, Pfeil > sichtbar");
            for (; benutzt < 12; benutzt++) pa.ServerDebugStartQuest(pool[benutzt]);
            yield return WaitUntil(() => svc.Quests.Count == 12, 5f);
            Check(svc.Quests.Count == 12 && pool.Take(12).All(pa.ServerIsActive), $"12 laufende Aufträge (Server und Client: {svc.Quests.Count})");
            string id12 = svc.Quests.Count >= 12 ? svc.Quests[11].Def?.Id : null;
            string titel12 = GameData.Quest(id12)?.Title ?? "?";
            Check(id12 == pool[11], $"Auftrag 12 = zuletzt gestarteter: „{titel12}“");
            UiSetze(ui, "_logOpen", true);   // Taste L
            UiSetze(ui, "_dirty", true);
            yield return UiTakt();
            object rueck = UiFeld(ui, "_logPrevious"), vor = UiFeld(ui, "_logNext"), seite = UiFeld(ui, "_logPageLabel");
            Check(UiSichtbar(UiFeld(ui, "_logWin")) && UiFensterstapel.Oberstes == "Auftragsbuch", "Auftragsbuch offen (oberstes Fenster im Fensterstapel)");
            List<object> zeilen = UiAktiveZeilen(ui, "_logRows");
            Check(zeilen.Count == 11 && zeilen.Select(UiIndex).SequenceEqual(Enumerable.Range(0, 11)), $"Seite 1: {zeilen.Count} Zeilen (Soll 11, Aufträge 1–11)");
            Check(UiSichtbar(seite) && UiText(seite) == "Seite 1 / 2", $"Seitenanzeige „{UiText(seite)}“ (Soll „Seite 1 / 2“)");
            Check(UiSichtbar(vor) && UiTrifft(vor), "Pfeil > sichtbar und anklickbar");
            Check(!UiSichtbar(rueck), "Pfeil < auf Seite 1 ausgeblendet");
            Check(UiFeld(ui, "_logSelected") as string == svc.Quests[0].Def.Id && zeilen.Count > 0 && UiZeileGewaehlt(zeilen[0]),
                "Erster Auftrag gewählt und markiert");
            yield return UiFoto("buch_12_seite1");

            // ---------------------------------------------------------- A2
            UiPunkt("A2 Seite 2: Auftrag 12 sichtbar und wählbar");
            yield return UiKlickSeite(ui, vor, +1);
            zeilen = UiAktiveZeilen(ui, "_logRows");
            Check(zeilen.Count == 1 && UiIndex(zeilen[0]) == 11 && UiZeilenText(zeilen[0]).Contains(titel12),
                $"Seite 2: {zeilen.Count} Zeile – „{(zeilen.Count > 0 ? StripTags(UiZeilenText(zeilen[0])) : "")}“ (Soll Auftrag 12 „{titel12}“)");
            Check(UiSichtbar(seite) && UiText(seite) == "Seite 2 / 2", $"Seitenanzeige „{UiText(seite)}“ (Soll „Seite 2 / 2“)");
            Check(UiSichtbar(rueck) && UiTrifft(rueck) && !UiSichtbar(vor), "Pfeil < sichtbar und anklickbar, Pfeil > ausgeblendet");
            UiSetze(ui, "_logSelected", null);
            if (zeilen.Count > 0)
            {
                object hit = UiZeileBei(ui, "_logRows", UiMitte(UiRt(zeilen[0])));
                Check(hit != null && ReferenceEquals(hit, zeilen[0]), "Klick auf die Zeile trifft Auftrag 12 (RowAt an der Zeilenmitte)");
                if (hit != null && UiIndex(hit) < svc.Quests.Count) UiSetze(ui, "_logSelected", svc.Quests[UiIndex(hit)].Def.Id);   // Klick-Zweig
                UiSetze(ui, "_dirty", true);
                yield return UiTakt();
            }
            Check(UiFeld(ui, "_logSelected") as string == id12 && zeilen.Count > 0 && UiZeileGewaehlt(zeilen[0]), "Auftrag 12 gewählt und markiert");
            Check(UiText(UiFeld(ui, "_logDetail")).Contains(titel12), "Rechts stehen Text und Ziele von Auftrag 12");
            object aufgeben = UiFeld(ui, "_logAbandon");
            Check(UiSichtbar(aufgeben) && UiTrifft(aufgeben), "Knopf „Aufgeben“ sichtbar und anklickbar");
            yield return UiFoto("buch_12_seite2");

            // ---------------------------------------------------------- A3
            UiPunkt("A3 Auftrag 12 aufgeben → 11 Aufträge, eine Seite, kein leerer Seitenpfeil");
            if (UiTrifft(aufgeben) && UiFeld(ui, "_logSelected") is string gewaehlt)
            {
                svc.Abandon(gewaehlt);              // Klick-Zweig „Aufgeben“
                UiSetze(ui, "_logSelected", null);
                UiSetze(ui, "_dirty", true);
            }
            yield return WaitUntil(() => svc.Quests.Count == 11, 5f);
            yield return UiTakt();
            Check(svc.Quests.Count == 11 && !pa.ServerIsActive(id12), $"Auftrag 12 aufgegeben (Server: {(pa.ServerIsActive(id12) ? "läuft noch" : "weg")}, Client {svc.Quests.Count})");
            zeilen = UiAktiveZeilen(ui, "_logRows");
            Check(zeilen.Count == 11 && (int)(UiFeld(ui, "_logPage") ?? -1) == 0, $"Eine Seite mit {zeilen.Count} Zeilen (Seite {(int)(UiFeld(ui, "_logPage") ?? -1) + 1})");
            Check(!UiSichtbar(vor) && !UiSichtbar(rueck) && !UiSichtbar(seite), "Keine Seitenpfeile und keine Seitenanzeige");
            Check(UiFeld(ui, "_logSelected") is string s3 && svc.IsActive(s3) && UiText(UiFeld(ui, "_logDetail")).Length > 0, "Wieder ein laufender Auftrag gewählt, rechts sein Text");
            yield return UiFoto("buch_11_eine_seite");

            // ---------------------------------------------------------- A4
            UiPunkt("A4 20 Aufträge → Seite 1: 11, Seite 2: 9 Zeilen");
            for (int n = 0; n < 9; n++, benutzt++) pa.ServerDebugStartQuest(pool[benutzt]);
            yield return WaitUntil(() => svc.Quests.Count == 20, 5f);
            yield return UiTakt();
            Check(svc.Quests.Count == 20, $"20 laufende Aufträge (Höchstzahl {QuestRules.MaxActive}; Client {svc.Quests.Count})");
            zeilen = UiAktiveZeilen(ui, "_logRows");
            Check(zeilen.Count == 11 && UiText(seite) == "Seite 1 / 2" && UiSichtbar(vor), $"Seite 1: {zeilen.Count} Zeilen, „{UiText(seite)}“, Pfeil >");
            yield return UiKlickSeite(ui, vor, +1);
            zeilen = UiAktiveZeilen(ui, "_logRows");
            bool reihenfolge = zeilen.Count == 9 && zeilen.Select(UiIndex).SequenceEqual(Enumerable.Range(11, 9)) &&
                               zeilen.All(z => UiIndex(z) < svc.Quests.Count && UiZeilenText(z).Contains(svc.Quests[UiIndex(z)].Def.Title));
            Check(reihenfolge, $"Seite 2: {zeilen.Count} Zeilen (Soll 9, Aufträge 12–20 in Reihenfolge)");
            Check(UiText(seite) == "Seite 2 / 2" && UiSichtbar(rueck) && !UiSichtbar(vor), $"„{UiText(seite)}“, Pfeil <, kein Pfeil >");
            yield return UiFoto("buch_20_seite2");

            // ---------------------------------------------------------- A5 (Zusatz)
            UiPunkt("A5 (Zusatz) Seite folgt dem gewählten Auftrag, wenn er durch Aufgeben davor auf Seite 1 rückt");
            object z16 = zeilen.FirstOrDefault(z => UiIndex(z) == 15);
            string id16 = svc.Quests.Count > 15 ? svc.Quests[15].Def.Id : null;
            if (z16 != null && ReferenceEquals(UiZeileBei(ui, "_logRows", UiMitte(UiRt(z16))), z16))
            {
                UiSetze(ui, "_logSelected", id16);   // Klick-Zweig der Zeile
                UiSetze(ui, "_dirty", true);
            }
            yield return UiTakt();
            Check(UiFeld(ui, "_logSelected") as string == id16 && z16 != null && UiZeileGewaehlt(z16), "Auftrag 16 auf Seite 2 gewählt");
            foreach (string vorn in svc.Quests.Take(5).Select(v => v.Def.Id).ToList()) yield return Ask(a, "quest-abandon", vorn);   // z. B. Abgabe beim NPC
            yield return WaitUntil(() => svc.Quests.Count == 15, 5f);
            yield return UiTakt();
            zeilen = UiAktiveZeilen(ui, "_logRows");
            object jetzt = zeilen.FirstOrDefault(z => UiIndex(z) == 10);
            Check(svc.Quests.Count == 15 && (int)(UiFeld(ui, "_logPage") ?? -1) == 0 && UiFeld(ui, "_logSelected") as string == id16 &&
                  jetzt != null && UiZeileGewaehlt(jetzt) && UiZeilenText(jetzt).Contains(GameData.Quest(id16)?.Title ?? "?"),
                $"Fünf Aufträge davor aufgegeben: Buch springt auf Seite {(int)(UiFeld(ui, "_logPage") ?? -1) + 1}, Auftrag (jetzt Nr. 11) bleibt markiert");
            yield return UiFoto("buch_seite_folgt_auswahl");
            for (int fehlt = 20 - svc.Quests.Count; fehlt > 0 && benutzt < pool.Count; fehlt--) pa.ServerDebugStartQuest(pool[benutzt++]);   // Client-Stand kommt erst später
            yield return WaitUntil(() => svc.Quests.Count == 20, 5f);

            UiFensterstapel.EscAusloesen();
            yield return UiTakt();
            Check(!(bool)(UiFeld(ui, "_logOpen") ?? true) && !UiSichtbar(UiFeld(ui, "_logWin")), "Esc schließt das Auftragsbuch");
        }

        // ================================================================== B. Tracker

        private IEnumerator UiTracker(NetworkInventory a, NetworkProgression pa, QuestUi ui, IProgressionService svc)
        {
            Line("");
            Line("[B Tracker (F2)]");
            UiPunkt("B1 Tracker bei vielen Aufträgen: endet über „Einstellungen (O)“, Zeile „… und X weitere (L)“");
            UiSetze(ui, "_dirty", true);
            yield return UiTakt();
            object tracker = UiFeld(ui, "_tracker");
            RectTransform trackerRt = UiRt(tracker), back = UiFeld(ui, "_trackerBack") as RectTransform;
            string text = UiText(tracker);
            int gesamt = svc.Quests.Count(v => v.Def != null);
            Match m = Regex.Match(StripTags(text), @"und (\d+) weitere \(L\)");
            int weitere = m.Success ? int.Parse(m.Groups[1].Value) : 0, gezeigt = gesamt - weitere;
            Check(gesamt >= 15 && m.Success && weitere > 0 && StripTags(text).TrimEnd().EndsWith("weitere (L)"),
                $"Letzte Zeile „… und {weitere} weitere (L)“ bei {gesamt} Aufträgen");
            bool ersteDa = svc.Quests.Take(gezeigt).All(v => text.Contains(">" + v.Def.Title + "<"));
            bool restWeg = svc.Quests.Skip(gezeigt).All(v => !text.Contains(">" + v.Def.Title + "<"));
            Check(gezeigt >= 1 && ersteDa && restWeg, $"Gezeigt: die ersten {gezeigt} Aufträge in Reihenfolge, die übrigen {weitere} nicht");

            RectTransform knopf = UiEinstellungenKnopf();
            Check(knopf != null && knopf.gameObject.activeInHierarchy, "Knopf „Einstellungen (O)“ gefunden");
            if (knopf != null && back != null && trackerRt != null)
            {
                float skala = trackerRt.GetComponentInParent<Canvas>()?.rootCanvas.scaleFactor ?? 1f;
                float knopfOben = UiRect(knopf).yMax, backUnten = UiRect(back).yMin, textUnten = UiTextUnten(tracker);
                Info($"Unterkante Tracker-Hintergrund {backUnten / skala:0} · Text {textUnten / skala:0} · Oberkante „Einstellungen (O)“ {knopfOben / skala:0} " +
                     $"(Einheiten der 1080er-Oberfläche über dem unteren Rand; oberer Start {(UiFeld(ui, "_trackerTop") is float top ? top : -1f):0})");
                Check(backUnten >= knopfOben - 0.5f, $"Hintergrund endet über dem Knopf ({(backUnten - knopfOben) / skala:0.0} Abstand)");
                Check(textUnten >= knopfOben, $"Text endet über dem Knopf ({(textUnten - knopfOben) / skala:0.0} Abstand)");
                Check(textUnten >= backUnten - 1f, "Text bleibt im Hintergrund");
            }
            yield return UiFoto("tracker_viele_auftraege");

            // ---------------------------------------------------------- B2
            UiPunkt("B2 Rucksack offen: Tracker liegt unter dem Taschenfenster (Canvas-Reihenfolge + Bild)");
            InventoryUi.RequestBag = true;
            yield return UiTakt();
            yield return new WaitForSecondsRealtime(0.2f);
            InventoryUi inv = FindAnyObjectByType<InventoryUi>();
            RectTransform tasche = UiFeld(inv, "_bagWin") as RectTransform;
            Check(tasche != null && tasche.gameObject.activeInHierarchy, "Taschenfenster offen");
            Canvas tc = trackerRt != null ? trackerRt.GetComponentInParent<Canvas>()?.rootCanvas : null;
            Canvas bc = tasche != null ? tasche.GetComponentInParent<Canvas>()?.rootCanvas : null;
            Canvas qc = (UiFeld(ui, "_dlgWin") as RectTransform)?.GetComponentInParent<Canvas>()?.rootCanvas;
            Check(tc != null && bc != null && tc != bc && tc.renderMode == RenderMode.ScreenSpaceOverlay && bc.renderMode == RenderMode.ScreenSpaceOverlay &&
                  tc.sortingOrder < bc.sortingOrder,
                $"Ebenen: Tracker „{tc?.name}“ {tc?.sortingOrder} < Taschen „{bc?.name}“ {bc?.sortingOrder} (Gespräch/Buch „{qc?.name}“ {qc?.sortingOrder})");
            if (tasche != null && back != null)
            {
                Rect ru = UiRect(back), rt = UiRect(tasche);
                bool ueber = ru.Overlaps(rt);
                Info($"Tracker und Taschenfenster überlappen sich {(ueber ? $"(Fläche {Mathf.Max(0f, Mathf.Min(ru.xMax, rt.xMax) - Mathf.Max(ru.xMin, rt.xMin)):0} × {Mathf.Max(0f, Mathf.Min(ru.yMax, rt.yMax) - Mathf.Max(ru.yMin, rt.yMin)):0} px) – im Bild verdeckt die Tasche den Tracker" : "nicht – bei dieser Ansicht nur die Ebenenprüfung")}");
            }
            yield return UiFoto("tracker_unter_rucksack");
            UiFensterstapel.EscAusloesen();
            yield return UiTakt();
            Check(tasche != null && !tasche.gameObject.activeInHierarchy, "Esc schließt den Rucksack");

            // Aufräumen: alle Test-Aufträge aufgeben (Client-Weg)
            foreach (string id in svc.Quests.Select(v => v.Def.Id).ToList()) yield return Ask(a, "quest-abandon", id);
            yield return WaitUntil(() => svc.Quests.Count == 0, 5f);
            Check(svc.Quests.Count == 0 && a.ServerData != null && a.ServerData.quests.Count == 0, "Test-Aufträge wieder aufgegeben (Server und Client leer)");
        }

        // ================================================================== C. Abgabe bei vollem Rucksack

        private IEnumerator UiAbgabe(NetworkInventory a, NetworkProgression pa, QuestUi ui, IProgressionService svc, QuestDefinition q)
        {
            Line("");
            Line("[C Abgabe bei vollem Rucksack (B2)]");
            UiPunkt("C1 Rucksack voll → Abgeben: Abgabe-Ansicht bleibt, rote Zeile „Kein Platz im Inventar.“");
            Check(q != null, $"Abgabebereiter Auftrag mit Gegenstandsbelohnung: „{q?.Title}“ ({q?.Id}, {GameData.Npc(q?.giverNpc ?? "")?.Name} → {GameData.Npc(q?.TurnInNpc ?? "")?.Name})");
            if (q == null) yield break;
            List<QuestItemReward> fest = QuestRules.ForClass(q.rewardItems, a.ServerModel.Class);
            List<QuestItemReward> wahl = QuestRules.ForClass(q.choiceItems, a.ServerModel.Class);
            Info("Belohnung: " + string.Join(", ", fest.Concat(wahl).Select(r => $"{r.item.DisplayName} ×{r.count}")) + (wahl.Count > 1 ? " (zur Wahl)" : ""));

            yield return GotoNpc(a, q.giverNpc);
            yield return new WaitForSecondsRealtime(1f);
            yield return WaitUntil(() => !Ashveil.Gameplay.Story.StoryDirector.Active, 25f);
            yield return Ask(a, "npc-talk", q.giverNpc);
            yield return Ask(a, "quest-accept", q.Id);
            if (!pa.ServerIsActive(q.Id))
            {
                Info($"Annehmen über den Client abgelehnt ({_last.data}) – Start über den Testweg");
                pa.ServerDebugStartQuest(q.Id);
                yield return WaitUntil(() => svc.IsActive(q.Id), 5f);
            }
            Check(pa.ServerIsActive(q.Id), $"„{q.Title}“ läuft");

            // Rucksack füllen: Belohnungen dürfen auf keinen vorhandenen Stapel passen
            foreach (QuestItemReward r in fest.Concat(wahl)) if (r.item.IsStackable) a.ServerRemoveAll(r.item.Id);
            int gefuellt = a.ServerFillFreeSlots(Filler);
            bool passtNicht = fest.Concat(wahl).Any(r => a.ServerModel.SpaceFor(new ItemStack(r.item.Id, r.count)) < r.count);
            Check(passtNicht, $"Rucksack voll ({gefuellt} freie Plätze mit Füllmaterial belegt) – Belohnung passt nicht mehr");

            if (q.TurnInNpc != q.giverNpc)
            {
                yield return GotoNpc(a, q.TurnInNpc);
                yield return new WaitForSecondsRealtime(1f);
                yield return WaitUntil(() => !Ashveil.Gameplay.Story.StoryDirector.Active, 25f);
            }
            yield return Ask(a, "npc-talk", q.TurnInNpc);
            yield return UiTakt();
            Check(svc.DialogNpc != null && svc.DialogNpc.Id == q.TurnInNpc && UiSichtbar(UiFeld(ui, "_dlgWin")), $"Gespräch mit {GameData.Npc(q.TurnInNpc)?.Name} offen");

            // Zeile des Auftrags anklicken → Abgabe-Ansicht
            var gelistet = UiFeld(ui, "_listed") as List<QuestDefinition> ?? new List<QuestDefinition>();
            object zeile = UiAktiveZeilen(ui, "_dlgRows").FirstOrDefault(z => UiIndex(z) >= 0 && UiIndex(z) < gelistet.Count && gelistet[UiIndex(z)].Id == q.Id);
            Check(zeile != null && ReferenceEquals(UiZeileBei(ui, "_dlgRows", UiMitte(UiRt(zeile))), zeile) && UiZeilenText(zeile).Contains("?"),
                $"Auftrag steht mit „?“ in der Liste, Klick trifft die Zeile ({StripTags(zeile != null ? UiZeilenText(zeile) : "–")})");
            QuestView view = svc.Quests.FirstOrDefault(v => v.Def != null && v.Def.Id == q.Id);
            bool abgabeModus = view != null && (view.Complete || QuestRules.TalkToHere(view, q.TurnInNpc)) && q.TurnInNpc == svc.DialogNpc?.Id;
            UiSetze(ui, "_modeQuest", q);   // Klick-Zweig der Zeile
            UiSetze(ui, "_choice", -1);
            UiModus(ui, abgabeModus ? "TurnIn" : "Progress");
            UiSetze(ui, "_dirty", true);
            yield return UiTakt();
            object abgeben = UiFeld(ui, "_dlgTurnIn");
            int belohnZeilen = UiAktiveZeilen(ui, "_rewardRows").Count;
            Check(abgabeModus && UiModusName(ui) == "TurnIn" && UiSichtbar(abgeben) && UiTrifft(abgeben),
                $"Abgabe-Ansicht mit Knopf „Abgeben“ ({belohnZeilen} Belohnungszeilen)");
            if (wahl.Count > 1)
            {
                int start = (int)(UiFeld(ui, "_choiceStart") ?? 0);
                object w0 = UiAktiveZeilen(ui, "_rewardRows").FirstOrDefault(z => UiIndex(z) == start);
                if (w0 != null && ReferenceEquals(UiZeileBei(ui, "_rewardRows", UiMitte(UiRt(w0))), w0)) UiSetze(ui, "_choice", 0);
                UiSetze(ui, "_dirty", true);
                yield return UiTakt();
                Check((int)(UiFeld(ui, "_choice") ?? -1) == 0, "Belohnung gewählt (erstes Teil)");
            }
            InventoryUi.RequestBag = true;   // fürs Bild: voller Rucksack daneben
            yield return UiTakt();

            int f0 = _uiFehlertexte.Count;
            yield return UiKlickAbgeben(ui, svc, abgeben);
            yield return WaitUntil(() => _uiFehlertexte.Count > f0, 5f);
            yield return new WaitForSecondsRealtime(0.2f);
            string fehler = _uiFehlertexte.Count > f0 ? _uiFehlertexte[_uiFehlertexte.Count - 1] : "(keine Meldung)";
            Check(fehler == UiKeinPlatz && pa.ServerIsActive(q.Id), $"Server lehnt ab: „{fehler}“, Auftrag läuft weiter");
            Check(UiModusName(ui) == "TurnIn" && ReferenceEquals(UiFeld(ui, "_modeQuest"), q) && UiSichtbar(UiFeld(ui, "_dlgWin")) && UiSichtbar(abgeben),
                $"Dialog bleibt in der Abgabe-Ansicht (Modus {UiModusName(ui)}, Knopf „Abgeben“ {(UiSichtbar(abgeben) ? "da" : "weg")})");
            if (wahl.Count > 1) Check((int)(UiFeld(ui, "_choice") ?? -1) == 0, "Gewählte Belohnung bleibt gewählt");
            HudRoot hud = FindAnyObjectByType<HudRoot>();
            object fehlerZeile = UiFeld(hud, "_error");
            Color rot = UiFarbe(fehlerZeile);
            Check(UiSichtbar(fehlerZeile) && UiText(fehlerZeile) == UiKeinPlatz && UiAlpha(fehlerZeile) > 0.5f && rot.r > 0.8f && rot.g < 0.5f,
                $"Rote Fehlerzeile oben: „{UiText(fehlerZeile)}“ (Deckkraft {UiAlpha(fehlerZeile):0.00}, Farbe {rot.r:0.00}/{rot.g:0.00}/{rot.b:0.00})");
            yield return UiFoto("abgabe_rucksack_voll", 0.05f);

            // ---------------------------------------------------------- C2
            UiPunkt("C2 Platz geschaffen → Abgeben: Ansicht wechselt zur Liste");
            int weg = a.ServerRemoveAll(Filler);
            yield return new WaitForSecondsRealtime(0.7f);   // Inventarstand beim Client, 0,5-s-Sperre abgelaufen
            yield return UiKlickAbgeben(ui, svc, abgeben);
            yield return WaitUntil(() => pa.ServerIsCompleted(q.Id), 5f);
            yield return WaitUntil(() => UiModusName(ui) == "Main", 3f);
            yield return UiTakt();
            Check(pa.ServerIsCompleted(q.Id) && !svc.IsActive(q.Id), $"Nach {weg} entfernten Füllstücken: Auftrag abgegeben (Server)");
            Check(UiModusName(ui) == "Main" && UiFeld(ui, "_modeQuest") == null && !UiSichtbar(abgeben) && !UiSichtbar(UiFeld(ui, "_dlgBack")) &&
                  UiSichtbar(UiFeld(ui, "_dlgWin")),
                $"Dialog zeigt wieder die Liste (Modus {UiModusName(ui)}, {UiAktiveZeilen(ui, "_dlgRows").Count} Auftragszeilen)");
            Check(fest.All(r => a.ServerModel.Count(r.item.Id) >= r.count) && (wahl.Count == 0 || a.ServerModel.Count(wahl[0].item.Id) >= wahl[0].count),
                "Belohnung im Rucksack");
            yield return UiFoto("abgabe_liste");
            for (int i = 0; i < 4 && UiFensterstapel.Oberstes != ""; i++)
            {
                UiFensterstapel.EscAusloesen();
                yield return UiTakt();
            }
            Check(UiFensterstapel.Oberstes == "" && svc.DialogNpc == null, "Esc schließt Rucksack und Gespräch");
        }

        /// <summary>Klick-Zweig „Abgeben“ der QuestUi nachgestellt (Wahlpflicht, 0,5-s-Sperre, Bitte an den Server).</summary>
        private IEnumerator UiKlickAbgeben(QuestUi ui, IProgressionService svc, object knopf)
        {
            if (!UiTrifft(knopf) || !(UiFeld(ui, "_modeQuest") is QuestDefinition q)) { Check(false, "Knopf „Abgeben“ anklickbar"); yield break; }
            int start = (int)(UiFeld(ui, "_choiceStart") ?? 0), wahl = (int)(UiFeld(ui, "_choice") ?? -1);
            int choices = ((UiFeld(ui, "_shownRewards") as IList)?.Count ?? 0) - start;
            if (choices > 1 && wahl < 0) { Check(false, "Belohnung gewählt"); yield break; }
            float sperre = (float)(UiFeld(ui, "_turnInWait") ?? 0f);
            while (Time.unscaledTime < sperre) yield return null;
            UiSetze(ui, "_turnInWait", Time.unscaledTime + 0.5f);
            svc.TurnIn(q.Id, choices > 0 ? Mathf.Max(0, wahl) : -1);
            UiSetze(ui, "_dirty", true);
        }

        // ================================================================== D. Entkommen

        private IEnumerator UiEntkommen(NetworkInventory a)
        {
            Line("");
            Line("[D „Entkommen“ (F1)]");
            UiPunkt("D1 Heimkehrender Gegner, Spieler trifft: Kampftext „Entkommen“, kein Schaden");
            if (EnemyDirector.Instance == null) { Check(false, "Gegner-Steuerung läuft"); yield break; }
            EnemySpawnArea vorlage = K28Template(KC.EnemyWolf) ??
                                     FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None).FirstOrDefault(x => x.Definition != null && x.Prefab != null && !x.Definition.neutral);
            if (vorlage == null) { Check(false, "Gegner-Vorlage (Aschwolf)"); yield break; }
            Ashveil.World.WorldQuestSite wiese = Ashveil.World.WorldQuestSite.Find("site.kareth.vorratswiesen");
            Vector3 stand = wiese != null ? wiese.transform.position : a.transform.position;
            if (NavMesh.SamplePosition(stand + new Vector3(0f, 0f, 30f), out NavMeshHit sh, 8f, NavMesh.AllAreas)) stand = sh.position;
            yield return Ask(a, "goto", V(stand + Vector3.up * 0.3f, 0f));
            yield return new WaitForSecondsRealtime(2f);

            Camera cam = Camera.main;
            Vector3 blick = cam != null ? cam.transform.forward : a.transform.forward;
            blick.y = 0f;
            blick = blick.sqrMagnitude < 1e-3f ? Vector3.forward : blick.normalized;
            Vector3 start = K28Near(a.transform.position, blick, 5f);
            float leine = vorlage.Definition.leashDistance, abstand = leine + 30f, weg = 0f;
            Vector3 heim = Vector3.zero;
            bool gefunden = false;
            var pfad = new NavMeshPath();
            foreach (float w in new[] { 0f, 20f, -20f, 40f, -40f, 60f, -60f, 90f, -90f, 135f, -135f, 180f })
            {
                Vector3 r = Quaternion.Euler(0f, w, 0f) * blick;
                if (!NavMesh.SamplePosition(start + r * abstand, out NavMeshHit h, 8f, NavMesh.AllAreas)) continue;
                if (!NavMesh.CalculatePath(start, h.position, NavMesh.AllAreas, pfad) || pfad.status != NavMeshPathStatus.PathComplete) continue;
                float l = 0f;
                for (int k = 1; k < pfad.corners.Length; k++) l += Vector3.Distance(pfad.corners[k - 1], pfad.corners[k]);
                if (l > abstand * 1.6f) continue;
                heim = h.position;
                weg = l;
                gefunden = true;
                if (w != 0f) Info($"Heimat {w:0}° neben der Blickrichtung (geradeaus kein Weg)");
                break;
            }
            Check(gefunden, $"Heimat jenseits der Leine mit Weg gefunden ({abstand:0} m, Leine {leine:0} m)");
            if (!gefunden) yield break;

            int stufe = Mathf.Max(1, a.ServerModel.Level);
            EnemyBrain wolf = EnemyDirector.Instance.SpawnAt(vorlage.Prefab, start, Quaternion.LookRotation(-blick).eulerAngles.y, stufe, "uiauftraege-test",
                br =>
                {
                    br.Configure(stufe, heim, "uiauftraege-test");   // Heimat weit weg: schon der erste Kampfschritt liegt jenseits der Leine
                    br.SetAggroProfile(AggroProfile.Defensive);      // greift erst an, wenn der Spieler zuschlägt
                }, 5f);
            if (wolf == null) { Check(false, "Aschwolf erzeugt"); yield break; }
            Health wh = wolf.GetComponent<Health>();
            LootEligibility elig = wolf.GetComponent<LootEligibility>();
            NavMeshAgent agent = wolf.GetComponent<NavMeshAgent>();
            var nm = a.GetComponent<NetworkMotion>();
            var waffen = a.GetComponent<Ashveil.Gameplay.Weapons.WeaponUser>();
            string waffe = waffen != null && waffen.MeleeWeapon != null ? waffen.MeleeWeapon.Id : "";
            int oid = wolf.GetComponent<FishNet.Object.NetworkObject>().ObjectId;
            Vector3 zuWolf = Quaternion.Euler(0f, 20f, 0f) * blick;   // Wolf leicht rechts vor der Figur – im Bild neben ihr, nicht dahinter
            int nSpawn = _uiEntkommen, kSpawn = _uiEntkommenZeiten.Count;

            // Server-Ereignisse am Wolf mitschreiben: echte Treffer (Damaged) und Treffer während der Unverwundbarkeit (Evaded)
            var treffer = new List<(float t, int bild, DamageInfo d)>();
            var abgeprallt = new List<(float t, int bild, DamageInfo d)>();
            void Getroffen(DamageInfo d) => treffer.Add((Time.time, Time.frameCount, d));
            void Abgeprallt(DamageInfo d) => abgeprallt.Add((Time.time, Time.frameCount, d));
            wh.Damaged += Getroffen;
            wh.Evaded += Abgeprallt;
            bool beteiligt = false;
            try
            {
                // Erster ECHTER Schlag: Ziel wählen, automatischer Angriff (Taste 1) – Weg über WeaponUser → NetworkCombat.ServerMelee
                // → CombatResolver.ResolveMelee (Treffererkennung, Stufen-Fehlwurf) → Health.ApplyDamage
                UiNeben(nm, a, wolf.transform.position, zuWolf, 1.9f);
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Ask(a, "attack", oid.ToString());
                Check(_last.ok && waffe.Length > 0, $"Echter Angriff gestartet: Ziel {wolf.Definition?.DisplayName}, automatischer Angriff mit {waffen?.MeleeWeapon?.DisplayName} ({waffe})");
                for (float t = 0f; t < 10f && treffer.Count == 0; t += 0.25f)
                {
                    // läuft der Wolf vor dem ersten Schlag los (Umherstreifen), bleibt die Figur dran
                    if (Vector3.Distance(Flach(a.transform.position), Flach(wolf.transform.position)) > 2.4f) UiNeben(nm, a, wolf.transform.position, zuWolf, 1.9f);
                    yield return new WaitForSecondsRealtime(0.25f);
                }
                yield return WaitUntil(() => wolf.State == EnemyState.Return && wh.IsTemporarilyInvulnerable, 6f);
                beteiligt = elig != null && elig.Profiles.Contains(a.ProfileId);
                DamageInfo erster = treffer.Count > 0 ? treffer[0].d : default;
                Check(treffer.Count >= 1 && erster.Source == a.gameObject && erster.WeaponId == waffe && wh.Current < wh.Max,
                    $"Erster echter Schlag trifft: −{erster.Amount:0} Leben ({wh.Current:0}/{wh.Max:0}), Quelle Figur, Waffe {erster.WeaponId}");
                Check(wolf.State == EnemyState.Return && wh.IsTemporarilyInvulnerable,
                    $"Leine überschritten: {wolf.Definition?.DisplayName} kehrt heim und ist dabei unverwundbar (Heimat {Vector3.Distance(wolf.transform.position, wolf.Home):0} m entfernt, Weg {weg:0} m, Leine {leine:0} m)");

                // Wolf auf dem Heimweg anhalten: Zustand bleibt Return (Unverwundbarkeit erneuert er selbst), er läuft den Schlägen nur nicht davon
                if (agent != null && agent.isOnNavMesh) { agent.isStopped = true; agent.velocity = Vector3.zero; }
                Info("Testhilfe: Wolf auf dem Heimweg angehalten (NavMeshAgent.isStopped, Zustand bleibt Return) – sonst läuft er den echten Schlägen davon");
                yield return Ask(a, "stop");
                // Schadenszahl des ersten Schlags ausblenden lassen (Lebensdauer 1,0 s) und die Drossel (1,5 s) sicher ablaufen lassen
                yield return new WaitForSecondsRealtime(1.8f);
                List<(string text, float alpha, Vector2 pos)> vorher = UiKampftexte();
                Check(wolf.State == EnemyState.Return && wh.IsTemporarilyInvulnerable && vorher.Count == 0,
                    $"Vor dem nächsten Schlag: Gegner weiter auf dem Heimweg, kein Kampftext mehr im Bild ({vorher.Count})");

                // ---------------------------------------------------------- echter Schlag auf den heimkehrenden Gegner
                UiNeben(nm, a, wolf.transform.position, zuWolf, 1.9f);
                yield return new WaitForSecondsRealtime(0.5f);
                float hp0 = wh.Current;
                int n0 = _uiEntkommen, t0 = treffer.Count, e0 = abgeprallt.Count, k0 = _uiEntkommenZeiten.Count;
                yield return Ask(a, "attack", oid.ToString());
                yield return WaitUntil(() => _uiEntkommen > n0, 8f);
                yield return new WaitForSecondsRealtime(0.2f);
                List<(string text, float alpha, Vector2 pos)> texte = UiKampftexte();
                DamageInfo prall = abgeprallt.Count > e0 ? abgeprallt[e0].d : default;
                Check(abgeprallt.Count > e0 && prall.Source == a.gameObject && prall.WeaponId == waffe,
                    $"Echter Schlag ({prall.WeaponId}) trifft den heimkehrenden Gegner: Health.Evaded auf dem Server ({abgeprallt.Count - e0}×)");
                float abstandText = Vector3.Distance(_uiLetztesEntkommen.WorldPosition, wolf.transform.position);
                Check(_uiEntkommen - n0 == 1 && _uiLetztesEntkommen.Kind == CombatTextKind.Miss && abstandText < 6f,
                    $"Kampftext „Entkommen“ beim Spieler angekommen ({_uiEntkommen - n0}× über EventBus<CombatTextEvent>, Art {_uiLetztesEntkommen.Kind}, {abstandText:0.0} m vom Gegner)");
                if (abgeprallt.Count > e0 && _uiEntkommenZeiten.Count > k0)
                    Info($"„Entkommen“ kam {_uiEntkommenZeiten[k0].bild - abgeprallt[e0].bild} Bilder nach dem Server-Treffer an (TargetRpc über den Transport des Hosts, kein direkter Aufruf)");
                var sichtbar = texte.Where(x => x.text == "Entkommen" && x.alpha > 0.9f && x.pos.x > 0f && x.pos.x < Screen.width && x.pos.y > 0f && x.pos.y < Screen.height).ToList();
                Check(sichtbar.Count == 1 && texte.Count == 1,
                    $"Im Bild: genau ein Kampftext „Entkommen“ und sonst keiner ({string.Join(", ", texte.Select(x => $"„{x.text}“ Deckkraft {x.alpha:0.00} bei {x.pos.x:0}/{x.pos.y:0} px"))})");
                Check(Mathf.Approximately(wh.Current, hp0) && treffer.Count == t0, $"Kein Schaden ({wh.Current:0}/{wh.Max:0} Leben, {treffer.Count - t0} Treffer mit Schaden)");
                UiNamensschildNeben(wolf, sichtbar.Count > 0 ? sichtbar[0].pos : new Vector2(-1f, -1f));
                yield return UiFoto("entkommen", 0f);
                UiNamensschilderUebereinander();

                // ---------------------------------------------------------- D2
                UiPunkt("D2 Drossel: weiterer Treffer < 1,5 s ohne zweiten Text, der nächste echte Schlag danach wieder mit Text");
                float tText = abgeprallt.Count > e0 ? abgeprallt[e0].t : Time.time;
                int e1 = abgeprallt.Count;
                wh.ApplyDamage(new DamageInfo { Amount = 30f, Source = a.gameObject, WeaponId = "test" });   // Serverweg, Figur als Quelle – sicher innerhalb der 1,5 s
                float tZweit = Time.time;
                yield return new WaitForSecondsRealtime(0.15f);   // der Text kam in Lauf 1 drei Bilder nach dem Treffer – ein zweiter wäre jetzt da
                List<(string text, float alpha, Vector2 pos)> texte2 = UiKampftexte();
                Check(abgeprallt.Count > e1 && tZweit - tText < 1.5f && _uiEntkommen - n0 == 1,
                    $"Weiterer Treffer {tZweit - tText:0.00} s nach dem ersten: kein zweiter Text ({_uiEntkommen - n0} insgesamt)");
                Check(texte2.Count(x => x.text == "Entkommen") == 1 && texte2.All(x => x.text == "Entkommen"),
                    $"Im Bild weiter genau ein „Entkommen“ ({string.Join(", ", texte2.Select(x => $"„{x.text}“ {x.alpha:0.00}"))})");
                yield return UiFoto("entkommen_drossel", 0f);

                // echte Schläge laufen weiter: der nächste Text kommt frühestens 1,5 s nach dem ersten
                yield return WaitUntil(() => _uiEntkommen - n0 >= 2, 8f);
                bool nochHeim = wolf.State == EnemyState.Return && wh.IsTemporarilyInvulnerable;
                float abstand2 = _uiEntkommenZeiten.Count > k0 + 1 ? _uiEntkommenZeiten[k0 + 1].t - _uiEntkommenZeiten[k0].t : -1f;
                Check(nochHeim && _uiEntkommen - n0 == 2 && abstand2 >= 1.5f,
                    $"Zweiter Text {abstand2:0.00} s nach dem ersten (Soll ≥ 1,5 s), Gegner {(nochHeim ? "noch auf dem Heimweg" : "nicht mehr auf dem Heimweg")}");
                yield return Ask(a, "stop");
                yield return new WaitForSecondsRealtime(0.8f);

                // Regel über alle Treffer seit dem Erscheinen: Text genau dann, wenn der letzte Text ≥ 1,5 s her ist
                int erwartet = 0;
                float zuletzt = float.NegativeInfinity;
                foreach (var x in abgeprallt)
                    if (x.t - zuletzt >= 1.5f) { erwartet++; zuletzt = x.t; }
                int echte = abgeprallt.Count(x => x.d.WeaponId == waffe);
                Check(erwartet == _uiEntkommen - nSpawn && _uiEntkommenZeiten.Count - kSpawn == erwartet,
                    $"Drossel über alle {abgeprallt.Count} Treffer auf den Heimkehrer ({echte} echte Schläge, {abgeprallt.Count - echte} über den Serverweg): {_uiEntkommen - nSpawn} Texte, nach der Regel {erwartet}");
                Check(Mathf.Approximately(wh.Current, hp0) && treffer.Count == t0, $"Alle Treffer auf den Heimkehrer ohne Schaden ({wh.Current:0}/{wh.Max:0})");
            }
            finally
            {
                wh.Damaged -= Getroffen;
                wh.Evaded -= Abgeprallt;
                if (agent != null && agent.isOnNavMesh) agent.isStopped = false;   // Heimweg fortsetzen
            }

            // ---------------------------------------------------------- D3 (Zusatz)
            UiPunkt("D3 (Zusatz, Nr. 3) Nach der Heimkehr ist die Beuteberechtigung zurückgesetzt");
            yield return WaitUntil(() => wolf == null || wolf.State != EnemyState.Return, 40f);
            yield return new WaitForSecondsRealtime(0.3f);
            Check(beteiligt, "Vor der Heimkehr war der Spieler beteiligt (erster echter Schlag)");
            Check(wolf != null && wolf.State != EnemyState.Return && elig != null && elig.Profiles.Count == 0 && Mathf.Approximately(wh.Current, wh.Max),
                $"Daheim: volles Leben ({wh.Current:0}/{wh.Max:0}), Beteiligte {(elig != null ? elig.Profiles.Count : -1)} (Soll 0), Zustand {wolf?.State}");
            if (wolf != null) EnemyDirector.Instance.Remove(wolf);
        }

        private static Vector3 Flach(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>Figur <paramref name="abstand"/> m vor den Wolf stellen (Richtung <paramref name="zuWolf"/>), Blick auf ihn.</summary>
        private static void UiNeben(NetworkMotion nm, NetworkInventory a, Vector3 wolf, Vector3 zuWolf, float abstand)
        {
            if (nm == null) return;
            Vector3 p = wolf - zuWolf * abstand;
            if (NavMesh.SamplePosition(p, out NavMeshHit h, 3f, NavMesh.AllAreas)) p = h.position;
            Vector3 d = Flach(wolf - p);
            nm.ServerTeleport(p + Vector3.up * 0.1f, d.sqrMagnitude > 0.01f ? Quaternion.LookRotation(d).eulerAngles.y : a.transform.eulerAngles.y);
        }

        /// <summary>Aktive Einträge des Kampftext-Renderers (was gerade wirklich im Bild steht): Text, Deckkraft, Bildschirmposition.</summary>
        private List<(string text, float alpha, Vector2 pos)> UiKampftexte()
        {
            var liste = new List<(string, float, Vector2)>();
            CombatTextRenderer r = FindAnyObjectByType<CombatTextRenderer>();
            if (r == null) { _uiFehlendeHaken.Add("CombatTextRenderer"); return liste; }
            if (!(UiFeld(r, "_entries") is IEnumerable eintraege)) return liste;
            foreach (object e in eintraege)
            {
                if (!(UiFeld(e, "Active") is bool an) || !an) continue;
                object tmp = UiFeld(e, "Text");
                RectTransform rt = UiRt(tmp);
                if (rt == null || !rt.gameObject.activeInHierarchy) continue;
                liste.Add((UiText(tmp), UiAlpha(tmp), new Vector2(rt.position.x, rt.position.y)));
            }
            return liste;
        }

        /// <summary>Nur Bericht: wo das Namensschild des Gegners relativ zum Kampftext steht (beide starten auf Höhe Zielpunkt + 0,6 m).</summary>
        private void UiNamensschildNeben(EnemyBrain wolf, Vector2 text)
        {
            Camera cam = Camera.main;
            Ashveil.World.WorldLabel schild = wolf != null ? wolf.GetComponentInChildren<Ashveil.World.WorldLabel>() : null;
            if (cam == null || schild == null || text.x < 0f) return;
            Vector3 s = cam.WorldToScreenPoint(schild.WorldPosition);
            float skala = Screen.height / 1080f;
            Info($"Namensschild „{schild.Title}“ bei {s.x:0}/{s.y:0} px, Kampftext {text.x:0}/{text.y:0} px – Abstand {(text.x - s.x) / skala:0}/{(text.y - s.y) / skala:0} (1080er-Einheiten; " +
                 "Schadenszahlen, „Verfehlt“ und „Entkommen“ starten alle bei Zielpunkt + 0,6 m und steigen 1 s lang auf)");
        }

        /// <summary>Nur Bericht (bestehende Namensschilder, nicht Teil von 089ee93): gleich lautende Schilder, die im Bild übereinanderliegen.</summary>
        private void UiNamensschilderUebereinander()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            float skala = Screen.height / 1080f;
            var im = new List<(Ashveil.World.WorldLabel l, Vector3 s)>();
            foreach (Ashveil.World.WorldLabel l in Ashveil.World.WorldLabel.Active)
            {
                if (l == null || !l.isActiveAndEnabled || string.IsNullOrEmpty(l.Title)) continue;
                Vector3 s = cam.WorldToScreenPoint(l.WorldPosition);
                if (s.z <= 0.1f || s.x < 0f || s.x > Screen.width || s.y < 0f || s.y > Screen.height) continue;
                if (Vector3.Distance(cam.transform.position, l.WorldPosition) > l.MaxDistance) continue;
                im.Add((l, s));
            }
            int n = 0;
            for (int i = 0; i < im.Count; i++)
                for (int j = i + 1; j < im.Count; j++)
                {
                    // Schildtext ca. 150 × 20 Einheiten (1080er-Oberfläche): näher beieinander = Schrift überlagert sich
                    if (im[i].l.Title != im[j].l.Title || Mathf.Abs(im[i].s.x - im[j].s.x) > 150f * skala || Mathf.Abs(im[i].s.y - im[j].s.y) > 16f * skala) continue;
                    EnemyBrain e1 = im[i].l.GetComponentInParent<EnemyBrain>(), e2 = im[j].l.GetComponentInParent<EnemyBrain>();
                    string wer = e1 != null && ReferenceEquals(e1, e2) ? "dasselbe Objekt (doppeltes Schild)"
                        : $"zwei Objekte, {Vector3.Distance(im[i].l.WorldPosition, im[j].l.WorldPosition):0.0} m auseinander, {Vector3.Distance(cam.transform.position, im[i].l.WorldPosition):0} m von der Kamera";
                    Info($"Namensschilder übereinander: „{im[i].l.Title}“ ({(im[j].s.x - im[i].s.x) / skala:0}/{(im[j].s.y - im[i].s.y) / skala:0} Einheiten versetzt) – {wer}");
                    n++;
                }
            if (n == 0) Info($"Keine gleich lautenden Namensschilder übereinander im Bild ({im.Count} Schilder sichtbar)");
        }

        // ================================================================== F. Rückkauf (Nr. 9, 21.10.)

        private IEnumerator UiRueckkauf(NetworkInventory a, NetworkProgression pa, QuestUi ui, IProgressionService svc)
        {
            Line("");
            Line("[F Rückkauf: alle 6 Plätze des Servers (Nr. 9)]");
            UiPunkt("F1 6 Teile verkauft → Händlerfenster zeigt 6 Rückkaufzeilen (vorher 3), über Hinweis und Geld");
            NpcDefinition haendler = UiHaendler();
            Check(haendler != null, $"Händler in der Szene: {haendler?.Name} ({haendler?.Id}, {haendler?.stock.Count} Waren)");
            if (haendler == null) yield break;
            List<string> ware = UiVerkaufsware();
            Check(ware.Count >= 6, $"Verkäufliche Testware: {string.Join(", ", ware.Select(id => GameData.Item(id).DisplayName))}");
            if (ware.Count < 6) yield break;
            ware = ware.Take(6).ToList();

            yield return UiZumNpc(a, haendler.Id);
            yield return Ask(a, "npc-talk", haendler.Id);
            yield return UiTakt();
            Check(_last.ok && svc.DialogNpc != null && svc.DialogNpc.Id == haendler.Id && UiSichtbar(UiFeld(ui, "_dlgWin")), $"Gespräch mit {haendler.Name} offen");
            yield return UiKlickHandeln(ui);
            RectTransform win = UiFeld(ui, "_vendWin") as RectTransform;
            Check(UiSichtbar(win), "Knopf „Handeln“ öffnet das Händlerfenster");

            // Verkaufen: derselbe Weg wie Rechtsklick im Rucksack bei offenem Händler (IProgressionService.Sell)
            int menge = 1;
            foreach (string id in ware) a.ServerGive(new ItemStack(id, GameData.Item(id).IsStackable ? ++menge : 1));
            yield return new WaitForSecondsRealtime(0.3f);
            long geld0 = pa.ServerMoney;
            int verkauft = 0;
            foreach (string id in ware)
            {
                int vorher = a.ServerModel.Count(id);
                yield return Ask(a, "sell", IndexOf(a, id).ToString());
                if (_last.ok && a.ServerModel.Count(id) < vorher) verkauft++;
                else Info($"Verkauf von {id} abgelehnt: {_last.data}");
            }
            yield return WaitUntil(() => svc.Buyback.Count == 6, 3f);
            Check(verkauft == 6 && svc.Buyback.Count == 6,
                $"6 Teile verkauft (+{pa.ServerMoney - geld0} Kupfer), Rückkaufliste beim Client: {svc.Buyback.Count} Einträge (Server hält 6)");
            UiSetze(ui, "_dirty", true);
            yield return UiTakt();
            List<object> zeilen = UiAktiveZeilen(ui, "_buybackRows");
            bool reihenfolge = zeilen.Count == 6 && zeilen.Select(UiIndex).SequenceEqual(Enumerable.Range(0, 6)) &&
                               zeilen.All(z => UiZeilenText(z).Contains(GameData.Item(ware[5 - UiIndex(z)]).DisplayName));
            Check(reihenfolge, $"{zeilen.Count} Rückkaufzeilen (Soll 6), neuester Verkauf oben: {string.Join(" · ", zeilen.Select(z => StripTags(UiZeilenText(z))))}");
            Check(zeilen.Count == 6 && zeilen.All(z => ReferenceEquals(UiZeileBei(ui, "_buybackRows", UiMitte(UiRt(z))), z)),
                "Jede Rückkaufzeile trifft der Klick an ihrer Mitte (RowAt)");

            // Lage: unter der 9. Warenzeile, über Hinweis und Geld, ohne Überdeckung, Schrift in der flachen Zeile
            RectTransform hinweis = UiRt(UiFeld(ui, "_vendHint")), geld = UiRt(UiFeld(ui, "_vendMoney"));
            IList lager = UiFeld(ui, "_stockRows") as IList;
            RectTransform ware9 = lager != null && lager.Count >= 9 ? UiRt(lager[8]) : null;
            RectTransform titel = win != null ? win.Find("Buyback Title") as RectTransform : null;
            Info($"Waren im Fenster: {UiAktiveZeilen(ui, "_stockRows").Count} (Händler hat {haendler.stock.Count}); die Lage wird gegen die 9. Warenzeile geprüft (auch wenn leer)");
            if (zeilen.Count == 6 && win != null && hinweis != null && geld != null && ware9 != null)
            {
                float skala = win.GetComponentInParent<Canvas>()?.rootCanvas.scaleFactor ?? 1f;
                Rect w = UiRect(win), h = UiRect(hinweis), g = UiRect(geld), w9 = UiRect(ware9);
                Rect erste = UiRect(UiRt(zeilen[0])), letzte = UiRect(UiRt(zeilen[5]));
                Rect t = titel != null ? UiRect(titel) : erste;
                Info($"Lage von der Fensteroberkante (Einheiten der 1080er-Oberfläche): 9. Ware endet {(w.yMax - w9.yMin) / skala:0}, Titel „Zurückkaufen“ " +
                     $"{(titel != null ? $"{(w.yMax - t.yMax) / skala:0}–{(w.yMax - t.yMin) / skala:0}" : "nicht gefunden")}, Zeilen {(w.yMax - erste.yMax) / skala:0}–{(w.yMax - letzte.yMin) / skala:0}, " +
                     $"Hinweis ab {(w.yMax - h.yMax) / skala:0}, Geld ab {(w.yMax - g.yMax) / skala:0} (Fenster {w.height / skala:0} hoch)");
                bool ueberdeckt = false;
                for (int i = 0; i < 5; i++)
                    if (UiRect(UiRt(zeilen[i])).yMin < UiRect(UiRt(zeilen[i + 1])).yMax - 0.5f) ueberdeckt = true;
                Check(!ueberdeckt, "Rückkaufzeilen überdecken sich nicht");
                Check(t.yMax <= w9.yMin + 0.5f && erste.yMax <= t.yMin + 0.5f, "Titel „Zurückkaufen“ und erste Zeile liegen unter der 9. Warenzeile");
                Check(letzte.yMin >= h.yMax - 0.5f && letzte.yMin >= g.yMax - 0.5f,
                    $"Unterste Rückkaufzeile endet über Hinweis und Geld ({(letzte.yMin - Mathf.Max(h.yMax, g.yMax)) / skala:0.0} Abstand)");
                Check(erste.yMax <= w.yMax && letzte.yMin >= w.yMin && erste.xMin >= w.xMin && erste.xMax <= w.xMax, "Alle Rückkaufzeilen liegen im Fenster");
                object text6 = UiFeld(zeilen[5], "Text");
                float unten = UiTextUnten(text6), oben = UiTextOben(text6);
                Check(unten >= letzte.yMin - 1f && oben <= letzte.yMax + 1f,
                    $"Schrift passt in die flache Zeile (Text {(oben - unten) / skala:0.0} hoch, Zeile {letzte.height / skala:0.0})");
            }
            else Check(false, "Lage prüfbar (6 Zeilen, Fenster, Hinweis, Geld, 9. Warenzeile)");
            yield return UiFoto("rueckkauf_6_zeilen");

            // ---------------------------------------------------------- F2
            UiPunkt("F2 Zeile 6 (vorher versteckt) anklicken → Rückkauf klappt, 5 Zeilen bleiben");
            object z6 = zeilen.Count == 6 ? zeilen[5] : null;
            string erstes = ware[0], erstesName = GameData.Item(erstes).DisplayName;
            int preis = svc.Buyback.Count == 6 ? svc.Buyback[5].Price : -1;
            long geld1 = pa.ServerMoney;
            object treffer = z6 != null ? UiZeileBei(ui, "_buybackRows", UiMitte(UiRt(z6))) : null;
            Check(treffer != null && ReferenceEquals(treffer, z6), $"Klick trifft Zeile 6 („{(z6 != null ? StripTags(UiZeilenText(z6)) : "–")}“)");
            if (treffer != null) svc.BuyBack(UiIndex(treffer));   // Klick-Zweig der Rückkaufzeile
            yield return WaitUntil(() => a.ServerModel.Count(erstes) > 0 && svc.Buyback.Count == 5, 5f);
            UiSetze(ui, "_dirty", true);
            yield return UiTakt();
            Check(a.ServerModel.Count(erstes) > 0 && pa.ServerMoney == geld1 - preis, $"„{erstesName}“ zurückgekauft ({preis} Kupfer)");
            List<object> rest = UiAktiveZeilen(ui, "_buybackRows");
            Check(rest.Count == 5 && svc.Buyback.Count == 5 && rest.All(z => !UiZeilenText(z).Contains(erstesName)),
                $"Noch {rest.Count} Rückkaufzeilen, „{erstesName}“ nicht mehr dabei");

            for (int i = 0; i < 4 && UiFensterstapel.Oberstes != ""; i++)
            {
                UiFensterstapel.EscAusloesen();
                yield return UiTakt();
            }
            Check(UiFensterstapel.Oberstes == "" && svc.DialogNpc == null, "Esc schließt Rucksack und Gespräch");
        }

        // ================================================================== G. Annehmen bei 20 laufenden Aufträgen (21.10.)

        private IEnumerator UiAnnehmen(NetworkInventory a, NetworkProgression pa, QuestUi ui, IProgressionService svc)
        {
            Line("");
            Line("[G Annehmen bei 20 laufenden Aufträgen]");
            UiPunkt("G1 20 Aufträge → Annehmen: Server lehnt ab „Zu viele Aufträge.“, Angebot bleibt mit „Annehmen“, rote Zeile");
            foreach (string id in svc.Quests.Where(v => v.Def != null).Select(v => v.Def.Id).ToList()) yield return Ask(a, "quest-abandon", id);
            yield return WaitUntil(() => svc.Quests.Count == 0, 5f);
            QuestDefinition q = UiAngebot(svc, a.ServerModel.Class);
            NpcDefinition geber = q != null ? GameData.Npc(q.giverNpc) : null;
            Check(q != null && geber != null, $"Angebot für die Figur: „{q?.Title}“ ({q?.Id}) bei {geber?.Name}");
            if (q == null || geber == null) yield break;
            List<string> fueller = UiFueller(svc, q);
            foreach (string id in fueller) pa.ServerDebugStartQuest(id);
            yield return WaitUntil(() => svc.Quests.Count == QuestRules.MaxActive, 5f);
            Check(svc.Quests.Count == QuestRules.MaxActive && a.ServerData.quests.Count == QuestRules.MaxActive && !svc.IsActive(q.Id),
                $"{svc.Quests.Count} laufende Aufträge (Höchstzahl {QuestRules.MaxActive}, Testweg), keiner von oder bei {geber.Name}");

            yield return UiZumNpc(a, geber.Id);
            yield return Ask(a, "npc-talk", geber.Id);
            yield return UiTakt();
            Check(svc.DialogNpc != null && svc.DialogNpc.Id == geber.Id && UiSichtbar(UiFeld(ui, "_dlgWin")), $"Gespräch mit {geber.Name} offen");
            object zeile = UiAuftragsZeile(ui, q);
            Check(zeile != null && ReferenceEquals(UiZeileBei(ui, "_dlgRows", UiMitte(UiRt(zeile))), zeile) && UiZeilenText(zeile).Contains("!"),
                $"Angebot steht mit „!“ in der Liste, Klick trifft die Zeile ({StripTags(zeile != null ? UiZeilenText(zeile) : "–")})");
            if (zeile != null)
            {
                UiSetze(ui, "_modeQuest", q);   // Klick-Zweig der Zeile: Auftrag läuft nicht → Angebot
                UiSetze(ui, "_choice", -1);
                UiModus(ui, "Offer");
                UiSetze(ui, "_dirty", true);
            }
            yield return UiTakt();
            object annehmen = UiFeld(ui, "_dlgAccept");
            Check(UiModusName(ui) == "Offer" && UiSichtbar(annehmen) && UiTrifft(annehmen), "Angebots-Ansicht mit Knopf „Annehmen“");

            int f0 = _uiFehlertexte.Count;
            yield return UiKlickAnnehmen(ui, svc, annehmen);
            yield return WaitUntil(() => _uiFehlertexte.Count > f0, 5f);
            yield return new WaitForSecondsRealtime(0.2f);
            string fehler = _uiFehlertexte.Count > f0 ? _uiFehlertexte[_uiFehlertexte.Count - 1] : "(keine Meldung)";
            Check(fehler == UiZuViele && !pa.ServerIsActive(q.Id) && a.ServerData.quests.Count == QuestRules.MaxActive,
                $"Server lehnt ab: „{fehler}“, Auftrag nicht angenommen ({a.ServerData.quests.Count} laufen)");
            Check(UiModusName(ui) == "Offer" && ReferenceEquals(UiFeld(ui, "_modeQuest"), q) && UiSichtbar(UiFeld(ui, "_dlgWin")) && UiSichtbar(annehmen) && UiTrifft(annehmen),
                $"Dialog bleibt im Angebot (Modus {UiModusName(ui)}, Knopf „Annehmen“ {(UiSichtbar(annehmen) ? "da" : "weg")})");
            UiRoteZeile(UiZuViele);
            yield return UiFoto("annehmen_zu_viele", 0.05f);

            // ---------------------------------------------------------- G2
            UiPunkt("G2 Ein Auftrag weniger → Annehmen klappt, Ansicht wechselt zur Liste");
            string weg = fueller.LastOrDefault(svc.IsActive);
            if (weg != null) yield return Ask(a, "quest-abandon", weg);
            yield return WaitUntil(() => svc.Quests.Count == QuestRules.MaxActive - 1, 5f);
            f0 = _uiFehlertexte.Count;
            yield return UiKlickAnnehmen(ui, svc, annehmen);
            yield return WaitUntil(() => pa.ServerIsActive(q.Id), 5f);
            yield return WaitUntil(() => UiModusName(ui) == "Main", 3f);
            yield return UiTakt();
            Check(pa.ServerIsActive(q.Id) && svc.IsActive(q.Id) && svc.Quests.Count == QuestRules.MaxActive,
                $"„{q.Title}“ angenommen (Server und Client, {svc.Quests.Count} laufen)");
            Check(_uiFehlertexte.Count == f0, $"Keine rote Meldung nach dem Annehmen ({(_uiFehlertexte.Count > f0 ? string.Join(" / ", _uiFehlertexte.Skip(f0)) : "keine")})");
            zeile = UiAuftragsZeile(ui, q);
            Check(UiModusName(ui) == "Main" && UiFeld(ui, "_modeQuest") == null && !UiSichtbar(annehmen) && !UiSichtbar(UiFeld(ui, "_dlgBack")) &&
                  UiSichtbar(UiFeld(ui, "_dlgWin")) && zeile != null && UiZeilenText(zeile).Contains("?"),
                $"Dialog zeigt wieder die Liste, der Auftrag steht mit „?“ darin (Modus {UiModusName(ui)}, „{(zeile != null ? StripTags(UiZeilenText(zeile)) : "–")}“)");
            yield return UiFoto("annehmen_liste");

            foreach (string id in svc.Quests.Where(v => v.Def != null).Select(v => v.Def.Id).ToList()) yield return Ask(a, "quest-abandon", id);
            yield return WaitUntil(() => svc.Quests.Count == 0, 5f);
            Check(svc.Quests.Count == 0 && a.ServerData.quests.Count == 0, "Test-Aufträge wieder aufgegeben (Server und Client leer)");
            for (int i = 0; i < 4 && UiFensterstapel.Oberstes != ""; i++)
            {
                UiFensterstapel.EscAusloesen();
                yield return UiTakt();
            }
            Check(UiFensterstapel.Oberstes == "" && svc.DialogNpc == null, "Esc schließt das Gespräch");
        }

        /// <summary>21.10.: Klick-Zweig „Annehmen“ der QuestUi nachgestellt (0,5-s-Sperre, Bitte an den Server; die Ansicht wechselt erst mit der Antwort).</summary>
        private IEnumerator UiKlickAnnehmen(QuestUi ui, IProgressionService svc, object knopf)
        {
            if (!UiTrifft(knopf) || !(UiFeld(ui, "_modeQuest") is QuestDefinition q)) { Check(false, "Knopf „Annehmen“ anklickbar"); yield break; }
            float sperre = UiFeld(ui, "_acceptWait") is float f ? f : 0f;
            while (Time.unscaledTime < sperre) yield return null;
            UiSetze(ui, "_acceptWait", Time.unscaledTime + 0.5f);
            svc.Accept(q.Id);
            UiSetze(ui, "_dirty", true);
        }

        // ================================================================== I. Questgegenstand-Reste nach Abgabe (21.10.)

        private IEnumerator UiQuestReste(NetworkInventory a, NetworkProgression pa, QuestUi ui, IProgressionService svc)
        {
            Line("");
            Line("[I Questgegenstand-Reste: Abgabe räumt auf, Aufgeben behält]");
            UiPunkt("I1 Abgabe mit Überschuss des eigenen Questgegenstands → danach 0 Stück im Rucksack (Server, Client, Taschenfenster)");
            foreach (string id in svc.Quests.Where(v => v.Def != null).Select(v => v.Def.Id).ToList()) yield return Ask(a, "quest-abandon", id);
            yield return WaitUntil(() => svc.Quests.Count == 0, 5f);
            // Kareth-Questgegenstände (Hauer, Ohr, Krug, Logbuchseite …) sind einzigartig – mehr als ein Stück passt nie in den Rucksack.
            // Überschuss gibt es nur bei stapelbaren Questgegenständen (Giftdrüsen, Abriebe, Glockenscherben; Fundstellen nach erneutem Annehmen).
            QuestDefinition q1 = UiQuestgegenstandAuftrag(svc);
            QuestObjective z1 = UiEigenesSammelziel(q1);
            ItemDefinition g1 = z1 != null ? GameData.Item(z1.targetId) : null;
            Check(q1 != null && g1 != null,
                $"Auftrag mit Questgegenstand-Sammelziel: „{q1?.Title}“ ({q1?.Id}) – {g1?.DisplayName} ×{z1?.count} (Kategorie {g1?.category}, questId {g1?.questId}, " +
                $"Stapel {g1?.maxStack}, {(g1 != null && g1.unique ? "einzigartig" : "nicht einzigartig")}), Abgabe bei {GameData.Npc(q1?.TurnInNpc ?? "")?.Name}");
            if (q1 == null || g1 == null) yield break;
            Info("Kareth-Questgegenstände mit questId sind einzigartig (höchstens 1 Stück, Überschuss unmöglich) – darum ein stapelbarer Questgegenstand aus Kareth v4");

            pa.ServerDebugStartQuest(q1.Id);
            yield return WaitUntil(() => svc.IsActive(q1.Id), 5f);
            a.ServerRemoveAll(g1.Id);
            int menge1 = z1.count + Mathf.Max(1, Mathf.Max(1, g1.maxStack) / 2);   // z. B. 6 verlangt → 9 (Stapel 6 + 3)
            int gegeben1 = a.ServerGive(new ItemStack(g1.Id, menge1, g1.bind == BindType.OnPickup));
            yield return WaitUntil(() => UiAnzahl(a.Bag, g1.Id) == menge1 && UiAuftragFertig(svc, q1.Id), 5f);
            int stapel1 = a.ServerModel.Bag.Count(s => !s.IsEmpty && s.id == g1.Id);
            Check(pa.ServerIsActive(q1.Id) && gegeben1 == menge1 && a.ServerModel.Count(g1.Id) == menge1 && UiAnzahl(a.Bag, g1.Id) == menge1 && menge1 > z1.count,
                $"„{q1.Title}“ läuft (Testweg), {g1.DisplayName} ×{a.ServerModel.Count(g1.Id)} im Rucksack in {stapel1} Stapeln (Testweg) – mehr als verlangt ({z1.count}); Client {UiAnzahl(a.Bag, g1.Id)}");
            Check(UiAuftragFertig(svc, q1.Id), "Sammelziel beim Client erfüllt (abgabebereit)");

            yield return UiAbgebenImGespraech(a, pa, ui, svc, q1);
            yield return WaitUntil(() => UiAnzahl(a.Bag, g1.Id) == 0, 3f);
            InventoryUi invUi = FindAnyObjectByType<InventoryUi>();
            InventoryUi.RequestBag = true;   // fürs Bild und die Anzeige: Rucksack nach der Abgabe
            yield return UiTakt();
            yield return new WaitForSecondsRealtime(0.2f);
            IInventoryService isvc = UiFeld(invUi, "_svc") as IInventoryService;
            Check(a.ServerModel.Count(g1.Id) == 0 && UiAnzahl(a.Bag, g1.Id) == 0,
                $"Nach der Abgabe: {g1.DisplayName} ×{a.ServerModel.Count(g1.Id)} beim Server, ×{UiAnzahl(a.Bag, g1.Id)} beim Client (Soll 0; vorher {menge1}, verlangt {z1.count}, Überschuss {menge1 - z1.count} mit entfernt)");
            Check(UiSichtbar(UiFeld(invUi, "_bagWin")) && isvc != null && UiAnzahl(isvc.Bag, g1.Id) == 0,
                $"Taschenfenster offen, seine Quelle (InventoryUi._svc.Bag) zeigt {(isvc != null ? UiAnzahl(isvc.Bag, g1.Id) : -1)} {g1.DisplayName}");
            yield return UiFoto("rucksack_nach_abgabe_questgegenstand");
            yield return UiAllesZu();

            // ---------------------------------------------------------- I2
            UiPunkt("I2 Gegenprobe: Sammelziel aus verkäuflichem Material mit Überschuss abgeben → Überschuss bleibt; fremder Questgegenstand bleibt");
            QuestDefinition q2 = UiMaterialAuftrag(svc);
            QuestObjective z2 = q2?.objectives.FirstOrDefault(o => o.type == QuestObjectiveType.Collect && UiVerkaeuflichesMaterial(o.targetId));
            ItemDefinition g2 = z2 != null ? GameData.Item(z2.targetId) : null;
            // fremder Questgegenstand: gehört zu einem anderen (nicht laufenden) Auftrag und ist kein Ziel dieses Auftrags
            ItemDefinition fremd = new[] { GlockenrastCatalog.ItemRubbing, GlockenrastCatalog.ItemBellFragment }.Select(GameData.Item)
                .FirstOrDefault(d => d != null && d.category == ItemCategory.Quest && !string.IsNullOrEmpty(d.questId) && q2 != null && d.questId != q2.Id &&
                                     !d.unique && !q2.objectives.Any(o => o.targetId == d.Id));
            Check(q2 != null && g2 != null,
                $"Auftrag mit Material-Sammelziel: „{q2?.Title}“ ({q2?.Id}) – {g2?.DisplayName} ×{z2?.count} (Kategorie {g2?.category}, verkäuflich für {g2?.sellPrice} Kupfer), Abgabe bei {GameData.Npc(q2?.TurnInNpc ?? "")?.Name}");
            if (q2 == null || g2 == null) yield break;
            pa.ServerDebugStartQuest(q2.Id);
            yield return WaitUntil(() => svc.IsActive(q2.Id), 5f);
            a.ServerRemoveAll(g2.Id);
            int menge2 = z2.count + 3;
            int gegeben2 = a.ServerGive(new ItemStack(g2.Id, menge2, g2.bind == BindType.OnPickup));
            int mengeFremd = 0;
            if (fremd != null)
            {
                a.ServerRemoveAll(fremd.Id);
                mengeFremd = a.ServerGive(new ItemStack(fremd.Id, Mathf.Min(2, Mathf.Max(1, fremd.maxStack)), fremd.bind == BindType.OnPickup));
            }
            yield return WaitUntil(() => UiAnzahl(a.Bag, g2.Id) == menge2 && UiAuftragFertig(svc, q2.Id), 5f);
            Check(pa.ServerIsActive(q2.Id) && gegeben2 == menge2 && a.ServerModel.Count(g2.Id) == menge2 && UiAuftragFertig(svc, q2.Id),
                $"„{q2.Title}“ läuft (Testweg), {g2.DisplayName} ×{a.ServerModel.Count(g2.Id)} im Rucksack (verlangt {z2.count}, Überschuss {menge2 - z2.count})" +
                (fremd != null ? $", dazu fremder Questgegenstand {fremd.DisplayName} ×{a.ServerModel.Count(fremd.Id)} (questId {fremd.questId})" : ""));

            yield return UiAbgebenImGespraech(a, pa, ui, svc, q2);
            yield return WaitUntil(() => UiAnzahl(a.Bag, g2.Id) == menge2 - z2.count, 3f);
            Check(a.ServerModel.Count(g2.Id) == menge2 - z2.count && UiAnzahl(a.Bag, g2.Id) == menge2 - z2.count,
                $"Nach der Abgabe: {g2.DisplayName} ×{a.ServerModel.Count(g2.Id)} beim Server, ×{UiAnzahl(a.Bag, g2.Id)} beim Client (Soll {menge2 - z2.count} – nur die verlangte Menge entfernt)");
            if (fremd != null)
                Check(mengeFremd > 0 && a.ServerModel.Count(fremd.Id) == mengeFremd && UiAnzahl(a.Bag, fremd.Id) == mengeFremd,
                    $"Fremder Questgegenstand bleibt: {fremd.DisplayName} ×{a.ServerModel.Count(fremd.Id)} (Soll {mengeFremd})");
            else Check(false, "Fremder Questgegenstand für die Gegenprobe gefunden");
            yield return UiAllesZu();
            a.ServerRemoveAll(g2.Id);
            if (fremd != null) a.ServerRemoveAll(fremd.Id);

            // ---------------------------------------------------------- I3
            UiPunkt("I3 Aufgeben über das Auftragsbuch behält die Questgegenstände (einzigartig und gestapelt mit Überschuss)");
            var aufgabe = new List<(QuestDefinition q, ItemDefinition g, int menge)>();
            foreach (string qid in new[] { KC.H2, GlockenrastCatalog.G8 })
            {
                QuestDefinition q = GameData.Quest(qid);
                QuestObjective z = UiEigenesSammelziel(q);
                ItemDefinition g = z != null ? GameData.Item(z.targetId) : null;
                if (q == null || g == null || svc.IsCompleted(qid) || svc.IsActive(qid)) { Info($"{qid}: für I3 nicht nutzbar"); continue; }
                pa.ServerDebugStartQuest(q.Id);
                a.ServerRemoveAll(g.Id);
                int menge = g.unique ? 1 : z.count + 2;
                int n = a.ServerGive(new ItemStack(g.Id, menge, g.bind == BindType.OnPickup));
                aufgabe.Add((q, g, n));
            }
            yield return WaitUntil(() => aufgabe.All(x => svc.IsActive(x.q.Id) && UiAnzahl(a.Bag, x.g.Id) == x.menge), 5f);
            Check(aufgabe.Count == 2 && aufgabe.All(x => pa.ServerIsActive(x.q.Id) && x.menge > 0 && a.ServerModel.Count(x.g.Id) == x.menge),
                "Laufend (Testweg), Gegenstände im Rucksack (Testweg): " +
                string.Join("; ", aufgabe.Select(x => $"„{x.q.Title}“ – {x.g.DisplayName} ×{a.ServerModel.Count(x.g.Id)} ({(x.g.unique ? "einzigartig" : "verlangt " + UiEigenesSammelziel(x.q).count)})")));
            UiSetze(ui, "_logOpen", true);   // Taste L
            UiSetze(ui, "_dirty", true);
            yield return UiTakt();
            Check(UiSichtbar(UiFeld(ui, "_logWin")), "Auftragsbuch offen");
            foreach ((QuestDefinition q, ItemDefinition g, int menge) in aufgabe)
            {
                object zeile = UiAktiveZeilen(ui, "_logRows").FirstOrDefault(z => UiIndex(z) >= 0 && UiIndex(z) < svc.Quests.Count && svc.Quests[UiIndex(z)].Def?.Id == q.Id);
                object hit = zeile != null ? UiZeileBei(ui, "_logRows", UiMitte(UiRt(zeile))) : null;
                if (hit != null && ReferenceEquals(hit, zeile)) UiSetze(ui, "_logSelected", q.Id);   // Klick-Zweig der Zeile
                UiSetze(ui, "_dirty", true);
                yield return UiTakt();
                object aufgeben = UiFeld(ui, "_logAbandon");
                Check(hit != null && ReferenceEquals(hit, zeile) && UiFeld(ui, "_logSelected") as string == q.Id && UiSichtbar(aufgeben) && UiTrifft(aufgeben),
                    $"„{q.Title}“ im Buch gewählt, Knopf „Aufgeben“ anklickbar");
                if (UiTrifft(aufgeben) && UiFeld(ui, "_logSelected") as string == q.Id)
                {
                    svc.Abandon(q.Id);   // Klick-Zweig „Aufgeben“
                    UiSetze(ui, "_logSelected", null);
                    UiSetze(ui, "_dirty", true);
                }
                yield return WaitUntil(() => !svc.IsActive(q.Id), 5f);
                yield return new WaitForSecondsRealtime(0.3f);   // Inventarstand beim Client
                Check(!pa.ServerIsActive(q.Id) && !pa.ServerIsCompleted(q.Id) && !svc.IsActive(q.Id),
                    $"„{q.Title}“ aufgegeben (Server: {(pa.ServerIsActive(q.Id) ? "läuft noch" : "weg")}, nicht abgeschlossen)");
                Check(a.ServerModel.Count(g.Id) == menge && UiAnzahl(a.Bag, g.Id) == menge,
                    $"{g.DisplayName} bleibt: ×{a.ServerModel.Count(g.Id)} beim Server, ×{UiAnzahl(a.Bag, g.Id)} beim Client (Soll {menge})");
            }
            yield return UiAllesZu();
            Check(svc.Quests.Count == 0 && a.ServerData.quests.Count == 0 && !(bool)(UiFeld(ui, "_logOpen") ?? true), "Aufgeräumt: keine laufenden Aufträge, Buch zu");
            foreach (var x in aufgabe) a.ServerRemoveAll(x.g.Id);
        }

        /// <summary>
        /// 21.10. (I): Abgabe über die Gesprächsoberfläche wie in C – hinlaufen, Gespräch, Zeile anklicken (Abgabe-Ansicht), bei mehreren
        /// Wahlbelohnungen die erste, „Abgeben“; prüft Abschluss (Server und Client), keine rote Meldung und die Belohnung im Rucksack.
        /// </summary>
        private IEnumerator UiAbgebenImGespraech(NetworkInventory a, NetworkProgression pa, QuestUi ui, IProgressionService svc, QuestDefinition q)
        {
            yield return UiZumNpc(a, q.TurnInNpc);
            yield return Ask(a, "npc-talk", q.TurnInNpc);
            yield return UiTakt();
            Check(svc.DialogNpc != null && svc.DialogNpc.Id == q.TurnInNpc && UiSichtbar(UiFeld(ui, "_dlgWin")), $"Gespräch mit {GameData.Npc(q.TurnInNpc)?.Name} offen");
            object zeile = UiAuftragsZeile(ui, q);
            Check(zeile != null && ReferenceEquals(UiZeileBei(ui, "_dlgRows", UiMitte(UiRt(zeile))), zeile) && UiZeilenText(zeile).Contains("?"),
                $"„{q.Title}“ steht mit „?“ in der Liste, Klick trifft die Zeile ({StripTags(zeile != null ? UiZeilenText(zeile) : "–")})");
            if (zeile == null) yield break;
            QuestView view = svc.Quests.FirstOrDefault(v => v.Def != null && v.Def.Id == q.Id);
            bool bereit = view != null && view.Complete && q.TurnInNpc == svc.DialogNpc?.Id;
            UiSetze(ui, "_modeQuest", q);   // Klick-Zweig der Zeile
            UiSetze(ui, "_choice", -1);
            UiModus(ui, bereit ? "TurnIn" : "Progress");
            UiSetze(ui, "_dirty", true);
            yield return UiTakt();
            List<QuestItemReward> fest = QuestRules.ForClass(q.rewardItems, a.ServerModel.Class);
            List<QuestItemReward> wahl = QuestRules.ForClass(q.choiceItems, a.ServerModel.Class);
            if (wahl.Count > 1)
            {
                int start = (int)(UiFeld(ui, "_choiceStart") ?? 0);
                object w0 = UiAktiveZeilen(ui, "_rewardRows").FirstOrDefault(z => UiIndex(z) == start);
                if (w0 != null && ReferenceEquals(UiZeileBei(ui, "_rewardRows", UiMitte(UiRt(w0))), w0)) UiSetze(ui, "_choice", 0);
                UiSetze(ui, "_dirty", true);
                yield return UiTakt();
            }
            object abgeben = UiFeld(ui, "_dlgTurnIn");
            Check(bereit && UiModusName(ui) == "TurnIn" && UiSichtbar(abgeben) && UiTrifft(abgeben) && (wahl.Count <= 1 || (int)(UiFeld(ui, "_choice") ?? -1) == 0),
                $"Abgabe-Ansicht mit Knopf „Abgeben“{(wahl.Count > 1 ? ", erste Wahlbelohnung gewählt" : "")}");
            var belohnung = fest.Concat(wahl.Take(1)).Where(r => r.item != null).GroupBy(r => r.item.Id).ToDictionary(x => x.Key, x => x.Sum(r => r.count));
            var vorher = belohnung.Keys.ToDictionary(id => id, id => a.ServerModel.Count(id));
            int f0 = _uiFehlertexte.Count;
            yield return UiKlickAbgeben(ui, svc, abgeben);
            yield return WaitUntil(() => pa.ServerIsCompleted(q.Id), 5f);
            yield return WaitUntil(() => !svc.IsActive(q.Id) && UiModusName(ui) == "Main", 3f);
            yield return new WaitForSecondsRealtime(0.3f);   // Inventarstand beim Client
            Check(pa.ServerIsCompleted(q.Id) && !pa.ServerIsActive(q.Id) && !svc.IsActive(q.Id) && _uiFehlertexte.Count == f0,
                $"„{q.Title}“ abgegeben (Server und Client){(_uiFehlertexte.Count > f0 ? " – Meldung: " + string.Join(" / ", _uiFehlertexte.Skip(f0)) : ", keine rote Meldung")}");
            Check(belohnung.All(kv => a.ServerModel.Count(kv.Key) >= vorher[kv.Key] + kv.Value),
                "Belohnung im Rucksack: " + (belohnung.Count > 0 ? string.Join(", ", belohnung.Select(kv => $"{GameData.Item(kv.Key)?.DisplayName} +{a.ServerModel.Count(kv.Key) - vorher[kv.Key]} (Soll +{kv.Value})")) : "keine Gegenstände"));
        }

        /// <summary>21.10. (I): Esc, bis kein Fenster mehr offen ist (Rucksack, Gespräch, Buch).</summary>
        private IEnumerator UiAllesZu()
        {
            for (int i = 0; i < 5 && UiFensterstapel.Oberstes != ""; i++)
            {
                UiFensterstapel.EscAusloesen();
                yield return UiTakt();
            }
        }

        /// <summary>21.10. (I): Stückzahl eines Gegenstands in einem Rucksack-Stand (Server-Modell oder Client-Spiegel).</summary>
        private static int UiAnzahl(ItemStack[] bag, string id) => bag == null ? 0 : bag.Where(s => !s.IsEmpty && s.id == id).Sum(s => s.count);

        private static bool UiAuftragFertig(IProgressionService svc, string questId) =>
            svc.Quests.FirstOrDefault(v => v.Def != null && v.Def.Id == questId) is QuestView qv && qv.Complete;

        /// <summary>21.10. (I): Sammelziel, dessen Gegenstand der eigene Questgegenstand des Auftrags ist (Kategorie Quest, questId = Auftrag).</summary>
        private static QuestObjective UiEigenesSammelziel(QuestDefinition q) =>
            q?.objectives.FirstOrDefault(o => o.type == QuestObjectiveType.Collect && GameData.Item(o.targetId) is ItemDefinition d &&
                                              d.category == ItemCategory.Quest && d.questId == q.Id);

        private static bool UiVerkaeuflichesMaterial(string itemId) =>
            GameData.Item(itemId) is ItemDefinition d && d.category == ItemCategory.Material && Progression.Sellable(d);

        /// <summary>21.10. (I): Auftrag nur mit Sammelzielen (erfüllt sich durch den Rucksack), Abgabe-NPC in der Szene, weder laufend noch abgeschlossen.</summary>
        private static bool UiNurSammeln(QuestDefinition q, IProgressionService svc) =>
            q != null && q.objectives.Count > 0 && q.objectives.All(o => o.type == QuestObjectiveType.Collect) && NpcIdentity.Find(q.TurnInNpc) != null &&
            (string.IsNullOrEmpty(q.requiredSceneTag) || q.requiredSceneTag == KC.SceneTag) && !svc.IsCompleted(q.Id) && !svc.IsActive(q.Id);

        /// <summary>21.10. (I1): stapelbarer eigener Questgegenstand (bevorzugt G3 „Gegengift“: 6 Giftdrüsen, Abgabe bei Maelis).</summary>
        private static QuestDefinition UiQuestgegenstandAuftrag(IProgressionService svc) =>
            new[] { GameData.Quest(GlockenrastCatalog.G3) }.Concat(GameData.Quests.Where(x => x != null).OrderBy(x => x.Id, StringComparer.Ordinal))
                .FirstOrDefault(q => UiNurSammeln(q, svc) && UiEigenesSammelziel(q) is QuestObjective o && GameData.Item(o.targetId) is ItemDefinition d &&
                                     !d.unique && d.maxStack > 1);

        /// <summary>21.10. (I2): Sammelziel aus verkäuflichem Material (bevorzugt Q2 „Zähe Felle“: 4 Aschwolfpelze, Abgabe bei Maelis).</summary>
        private static QuestDefinition UiMaterialAuftrag(IProgressionService svc) =>
            new[] { GameData.Quest("quest.tough_hides") }.Concat(GameData.Quests.Where(x => x != null).OrderBy(x => x.Id, StringComparer.Ordinal))
                .FirstOrDefault(q => UiNurSammeln(q, svc) && q.objectives.Any(o => UiVerkaeuflichesMaterial(o.targetId)));

        // ================================================================== J. Questbeute nur bei Bedarf (21.10.)

        /// <summary>
        /// 21.10. (Max: „ok lehne ab“; nach der Review: verwerfen statt liegen lassen): Plündern eines Questgegenstands nimmt nur, was der
        /// laufende Auftrag noch braucht (NetworkInventory.QuestLootRoom). Der nicht benötigte Rest wird aus der PERSÖNLICHEN Beute des
        /// Spielers verworfen, Meldung „Brauchst du nicht mehr.“ – liegt sonst nichts mehr darin, schließt das Beutefenster und die Leiche
        /// glimmt nicht mehr (TargetLootAvailable false: sie öffnet sich nicht immer wieder, Rechtsklick führt zur Nachlese). Bei
        /// Platzmangel wird nichts verworfen („Kein Platz …“, alles bleibt liegen).
        /// Testweg: je Punkt ein Aschwolf, ohne Beteiligung der Figur getötet (kein Beutewurf für sie), mit fester persönlicher Beute über
        /// CorpseLoot.SetFixed und Glimmen wie in NetworkEnemy.OnKilled (NetworkInventory.NotifyLootAvailable) – Questgegenstand von G3
        /// (Giftdrüsen, 6 verlangt), Material, ein Ausrüstungsteil und 2 Glockensplitter (Questgegenstand, dessen questId keinen Auftrag
        /// hat – altes Fundstück, bleibt Beute wie beim Beutewurf, ServerWantsLoot).
        /// Im Spiel entsteht so eine Leiche z. B., wenn der Auftrag nach dem Sieg aufgegeben oder anderswo fertig gesammelt wird.
        /// Klicks über die Trefferprüfung der InventoryUi (LootAt an der Zeilenmitte, Clicked für „Alles nehmen“), dann deren Dienst-Aufruf.
        /// </summary>
        private IEnumerator UiQuestBeute(NetworkInventory a, NetworkProgression pa, IProgressionService svc)
        {
            Line("");
            Line("[J Questbeute nur bei Bedarf]");
            UiPunkt("J1 Auftrag läuft nicht, Leiche nur mit Questgegenstand → Nehmen: nichts aufgenommen, aus der persönlichen Beute verworfen, " +
                    "„Brauchst du nicht mehr.“, Beutefenster zu, Leiche glimmt nicht mehr");
            foreach (string id in svc.Quests.Where(v => v.Def != null).Select(v => v.Def.Id).ToList()) yield return Ask(a, "quest-abandon", id);
            yield return WaitUntil(() => svc.Quests.Count == 0, 5f);
            yield return UiAllesZu();
            QuestDefinition q = UiQuestbeuteAuftrag(svc);
            QuestObjective z = UiEigenesSammelziel(q);
            ItemDefinition g = z != null ? GameData.Item(z.targetId) : null;
            int zi = q != null && z != null ? q.objectives.IndexOf(z) : -1;
            ItemDefinition mat = new[] { "item.ash_wolf_pelt", "item.wolf_fang" }.Select(GameData.Item)
                .FirstOrDefault(d => d != null && d.category == ItemCategory.Material && d.IsStackable && d.maxStack >= 2);
            ItemDefinition ruest = new[] { "item.grey_wool_cloak", "item.oak_shield" }.Select(GameData.Item)
                .FirstOrDefault(d => d != null && d.IsEquippable && !d.unique && string.IsNullOrEmpty(d.questId));
            ItemDefinition splitter = GameData.Item("item.bell_shard");
            bool splitterOhneAuftrag = splitter != null && splitter.category == ItemCategory.Quest && !string.IsNullOrEmpty(splitter.questId) &&
                                       GameData.Quest(splitter.questId) == null && !splitter.unique && splitter.maxStack >= 2;
            Check(q != null && g != null && zi >= 0,
                $"Auftrag mit stapelbarem eigenem Questgegenstand: „{q?.Title}“ ({q?.Id}) – {g?.DisplayName} ×{z?.count} (Kategorie {g?.category}, questId {g?.questId}, Stapel {g?.maxStack})");
            Check(mat != null && ruest != null, $"Gegenprobe-Beute: Material {mat?.DisplayName} (Kategorie {mat?.category}), Ausrüstung {ruest?.DisplayName} ({ruest?.category})");
            Check(splitterOhneAuftrag, $"Altes Fundstück: {splitter?.DisplayName} – Kategorie {splitter?.category}, questId „{splitter?.questId}“ ohne Auftragsdefinition " +
                                       $"({(splitter != null && GameData.Quest(splitter.questId) == null ? "kein Auftrag" : "Auftrag vorhanden")})");
            if (q == null || g == null || zi < 0 || mat == null || ruest == null) yield break;

            Health hp = a.GetComponent<Health>();
            InventoryUi invUi = FindAnyObjectByType<InventoryUi>();
            EnemySpawnArea vorlage = K28Template(KC.EnemyWolf) ??
                                     FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None).FirstOrDefault(x => x.Definition != null && x.Prefab != null && !x.Definition.neutral);
            if (invUi == null || vorlage == null || EnemyDirector.Instance == null)
            {
                Check(false, "Inventar-Oberfläche, Gegner-Vorlage und Gegner-Steuerung vorhanden");
                yield break;
            }
            object lootWin = UiFeld(invUi, "_lootWin");
            string pid = a.ProfileId;
            a.ServerRemoveAll(g.Id);
            var vorher = new[] { mat, ruest, splitter }.Where(d => d != null).ToDictionary(d => d.Id, d => a.ServerModel.Count(d.Id));
            int fueller0 = a.ServerModel.Count(Filler);
            int frei = a.ServerModel.FreeBagSlots;   // wie die Anzeige „x/y frei“ (Bag-Feld ist größer als die nutzbaren Zellen)
            yield return WaitUntil(() => UiAnzahl(a.Bag, g.Id) == 0, 3f);
            Check(!pa.ServerIsActive(q.Id) && !svc.IsActive(q.Id) && !pa.ServerIsCompleted(q.Id) && a.ServerModel.Count(g.Id) == 0 && UiAnzahl(a.Bag, g.Id) == 0 && frei >= 4,
                $"„{q.Title}“ läuft nicht (nicht abgeschlossen), {g.DisplayName} ×{a.ServerModel.Count(g.Id)} im Rucksack, {frei}/{a.ServerModel.GeneralCapacity} Plätze frei");

            var leichen = new List<UiLeiche>();
            try
            {
                // ---------------------------------------------------------- J1: nur der Questgegenstand, Auftrag läuft nicht
                var l1 = new UiLeiche();
                leichen.Add(l1);
                yield return UiQuestbeuteLeiche(a, vorlage, new List<ItemStack> { new ItemStack(g.Id, 3, false) }, l1);
                if (l1.Beute == null) yield break;
                yield return UiBeuteOeffnen(a, l1, lootWin, 1);
                Check(ReferenceEquals(UiFeld(invUi, "_svc"), a), "Beutefenster arbeitet mit dem Inventar der Figur (InventoryUi._svc)");
                if (!a.LootOpen) yield break;

                int f0 = _uiFehlertexte.Count;
                yield return UiBeuteKlick(a, invUi, UiBeuteIndex(a.Loot, g.Id));
                yield return WaitUntil(() => !UiGlimmt(l1), 3f);
                Check(a.ServerModel.Count(g.Id) == 0 && UiAnzahl(a.Bag, g.Id) == 0,
                    $"Nichts aufgenommen: {g.DisplayName} ×{a.ServerModel.Count(g.Id)} beim Server, ×{UiAnzahl(a.Bag, g.Id)} beim Client (Soll 0)");
                Check(UiBeuteAnzahl(l1.Beute.Get(pid), g.Id) == 0 && !l1.Beute.HasLootFor(pid) && UiBeuteAnzahl(a.Loot, g.Id) == 0,
                    $"Aus der persönlichen Beute verworfen: Leiche beim Server {UiBeuteText(l1.Beute.Get(pid))}, Beutefenster ×{UiBeuteAnzahl(a.Loot, g.Id)} (Soll 0)");
                Check(a.LastError == UiNichtMehr && _uiFehlertexte.Skip(f0).Contains(UiNichtMehr),
                    $"Meldung vom Server „{a.LastError}“ (Kampftext: {string.Join(" / ", _uiFehlertexte.Skip(f0))})");
                UiRoteZeile(UiNichtMehr);
                Check(!a.LootOpen && !UiSichtbar(lootWin) && a.Loot.Count == 0, $"Sonst nichts darin → Beutefenster zu (LootOpen {a.LootOpen}, {a.Loot.Count} Zeilen)");
                Check(l1.Glimmen != null && !l1.Glimmen.Available && !UiFunken(l1.Glimmen) && !l1.Beute.HasAnyLoot,
                    $"Leiche glimmt nicht mehr ({UiGlimmText(l1.Glimmen)}; Server: HasAnyLoot {l1.Beute.HasAnyLoot}) – ein Rechtsklick ginge zur Nachlese statt ins Beutefenster (LootInteractor)");
                yield return UiFoto("questbeute_auftrag_laeuft_nicht", 0.05f);
                yield return Ask(a, "loot-open", l1.Oid);
                yield return UiTakt();
                Check(!_last.ok && !a.LootOpen && !UiSichtbar(lootWin) && !UiGlimmt(l1), $"Erneut öffnen: kein Beutefenster, kein Glimmen ({_last.data})");
                UiLeicheWeg(a, l1);

                // ---------------------------------------------------------- J2
                UiPunkt($"J2 Auftrag läuft, {z.count - 1}/{z.count} vorhanden, Leiche mit 3 → 1 aufgenommen ({z.count}/{z.count}), 2 verworfen, Meldung; " +
                        "übrige Beute bleibt, Fenster offen, Leiche glimmt weiter");
                pa.ServerDebugStartQuest(q.Id);
                yield return WaitUntil(() => svc.IsActive(q.Id), 5f);
                int gegeben = a.ServerGive(new ItemStack(g.Id, z.count - 1, g.bind == BindType.OnPickup));
                yield return WaitUntil(() => UiAnzahl(a.Bag, g.Id) == z.count - 1 && UiZielStand(svc, q.Id, zi) == z.count - 1, 5f);
                Check(pa.ServerIsActive(q.Id) && gegeben == z.count - 1 && a.ServerModel.Count(g.Id) == z.count - 1 && UiZielStand(svc, q.Id, zi) == z.count - 1,
                    $"„{q.Title}“ läuft (Testweg), {g.DisplayName} ×{a.ServerModel.Count(g.Id)} im Rucksack (Testweg), Ziel beim Client {UiZielStand(svc, q.Id, zi)}/{z.count}");
                var feste2 = new List<ItemStack> { new ItemStack(g.Id, 3, false), new ItemStack(mat.Id, 2, false), new ItemStack(ruest.Id, 1, false) };
                if (splitterOhneAuftrag) feste2.Add(new ItemStack(splitter.Id, 2, false));
                var l2 = new UiLeiche();
                leichen.Add(l2);
                yield return UiQuestbeuteLeiche(a, vorlage, feste2, l2);
                if (l2.Beute == null) yield break;
                yield return UiBeuteOeffnen(a, l2, lootWin, feste2.Count);
                if (!a.LootOpen) yield break;
                f0 = _uiFehlertexte.Count;
                yield return UiBeuteKlick(a, invUi, UiBeuteIndex(a.Loot, g.Id));
                yield return WaitUntil(() => UiZielStand(svc, q.Id, zi) == z.count, 3f);
                Check(a.ServerModel.Count(g.Id) == z.count && UiAnzahl(a.Bag, g.Id) == z.count,
                    $"1 aufgenommen: {g.DisplayName} ×{a.ServerModel.Count(g.Id)} beim Server, ×{UiAnzahl(a.Bag, g.Id)} beim Client (Soll {z.count} = verlangt)");
                Check(UiBeuteAnzahl(l2.Beute.Get(pid), g.Id) == 0 && UiBeuteAnzahl(a.Loot, g.Id) == 0,
                    $"2 verworfen: {g.DisplayName} in der Leiche ×{UiBeuteAnzahl(l2.Beute.Get(pid), g.Id)} beim Server, ×{UiBeuteAnzahl(a.Loot, g.Id)} im Beutefenster (Soll 0)");
                Check(l2.Beute.Get(pid).Count == feste2.Count - 1 && a.Loot.Count == feste2.Count - 1 && a.LootOpen && UiSichtbar(lootWin),
                    $"Übrige Beute bleibt, Fenster offen ({a.Loot.Count} Zeilen; Server: {UiBeuteText(l2.Beute.Get(pid))})");
                Check(UiGlimmt(l2), $"Leiche glimmt weiter ({UiGlimmText(l2.Glimmen)})");
                Check(UiAuftragFertig(svc, q.Id) && UiZielStand(svc, q.Id, zi) == z.count, $"Sammelziel {UiZielStand(svc, q.Id, zi)}/{z.count} (abgabebereit)");
                Check(a.LastError == UiNichtMehr && _uiFehlertexte.Skip(f0).Contains(UiNichtMehr),
                    $"Meldung vom Server „{a.LastError}“ (Kampftext: {string.Join(" / ", _uiFehlertexte.Skip(f0))})");
                yield return UiFoto("questbeute_nur_was_fehlt", 0.05f);
                UiLeicheWeg(a, l2);

                // ---------------------------------------------------------- J3
                UiPunkt($"J3 „Alles nehmen“ bei {z.count}/{z.count}: Material, Ausrüstung und Glockensplitter aufgenommen, Questrest verworfen, Leiche leer, " +
                        "„Brauchst du nicht mehr.“ (kein Platzproblem)");
                // Questgegenstand mitten in der Liste: nach dem Verwerfen rückt der Rest nach (ServerTakeLoot geht mit demselben Index weiter)
                var feste3 = new List<ItemStack> { new ItemStack(mat.Id, 2, false), new ItemStack(g.Id, 2, false), new ItemStack(ruest.Id, 1, false) };
                if (splitterOhneAuftrag) feste3.Add(new ItemStack(splitter.Id, 2, false));
                var l3 = new UiLeiche();
                leichen.Add(l3);
                yield return UiQuestbeuteLeiche(a, vorlage, feste3, l3);
                if (l3.Beute == null) yield break;
                yield return UiBeuteOeffnen(a, l3, lootWin, feste3.Count);
                if (!a.LootOpen) yield break;
                f0 = _uiFehlertexte.Count;
                yield return UiBeuteKlick(a, invUi, -1, true);
                yield return WaitUntil(() => !UiGlimmt(l3), 3f);
                foreach ((ItemDefinition d, int n) in new[] { (mat, 2), (ruest, 1) }.Concat(splitterOhneAuftrag ? new[] { (splitter, 2) } : new (ItemDefinition, int)[0]))
                    Check(a.ServerModel.Count(d.Id) == vorher[d.Id] + n && UiAnzahl(a.Bag, d.Id) == vorher[d.Id] + n,
                        $"{d.DisplayName} +{a.ServerModel.Count(d.Id) - vorher[d.Id]} (Soll +{n}; Server ×{a.ServerModel.Count(d.Id)}, Client ×{UiAnzahl(a.Bag, d.Id)})" +
                        (d == splitter ? $" – Questgegenstand ohne Auftrag („{d.questId}“) bleibt Beute" : ""));
                Check(a.ServerModel.Count(g.Id) == z.count && UiAnzahl(a.Bag, g.Id) == z.count,
                    $"{g.DisplayName} nicht weiter genommen (Server ×{a.ServerModel.Count(g.Id)}, Client ×{UiAnzahl(a.Bag, g.Id)}, Soll {z.count})");
                Check(!l3.Beute.HasLootFor(pid) && a.Loot.Count == 0 && !a.LootOpen && !UiSichtbar(lootWin),
                    $"Questrest verworfen, Leiche leer (Server: {UiBeuteText(l3.Beute.Get(pid))}), Beutefenster zu");
                Check(l3.Glimmen != null && !l3.Glimmen.Available && !UiFunken(l3.Glimmen), $"Leiche glimmt nicht mehr ({UiGlimmText(l3.Glimmen)})");
                Check(a.LastError == UiNichtMehr && _uiFehlertexte.Skip(f0).Contains(UiNichtMehr) && !_uiFehlertexte.Skip(f0).Any(t => t.StartsWith("Kein Platz", StringComparison.Ordinal)),
                    $"Meldung „{a.LastError}“ – kein Platzproblem (Kampftext: {string.Join(" / ", _uiFehlertexte.Skip(f0))})");
                yield return UiFoto("questbeute_alles_nehmen", 0.05f);
                UiLeicheWeg(a, l3);

                // ---------------------------------------------------------- J4
                UiPunkt($"J4 Auftrag läuft, 0/{z.count}, Leiche mit 2 → beide aufgenommen, keine Meldung, Leiche leer");
                a.ServerRemoveAll(g.Id);
                yield return WaitUntil(() => UiAnzahl(a.Bag, g.Id) == 0 && UiZielStand(svc, q.Id, zi) == 0, 5f);
                Check(pa.ServerIsActive(q.Id) && a.ServerModel.Count(g.Id) == 0 && UiZielStand(svc, q.Id, zi) == 0,
                    $"„{q.Title}“ läuft, {g.DisplayName} aus dem Rucksack entfernt (Testweg), Ziel beim Client {UiZielStand(svc, q.Id, zi)}/{z.count}");
                var l4 = new UiLeiche();
                leichen.Add(l4);
                yield return UiQuestbeuteLeiche(a, vorlage, new List<ItemStack> { new ItemStack(g.Id, 2, false) }, l4);
                if (l4.Beute == null) yield break;
                yield return UiBeuteOeffnen(a, l4, lootWin, 1);
                if (!a.LootOpen) yield break;
                f0 = _uiFehlertexte.Count;
                yield return UiBeuteKlick(a, invUi, UiBeuteIndex(a.Loot, g.Id));
                yield return WaitUntil(() => UiZielStand(svc, q.Id, zi) == 2 && !UiGlimmt(l4), 3f);
                Check(a.ServerModel.Count(g.Id) == 2 && UiAnzahl(a.Bag, g.Id) == 2 && UiZielStand(svc, q.Id, zi) == 2,
                    $"Beide aufgenommen: {g.DisplayName} ×{a.ServerModel.Count(g.Id)} beim Server, ×{UiAnzahl(a.Bag, g.Id)} beim Client, Ziel {UiZielStand(svc, q.Id, zi)}/{z.count}");
                Check(!l4.Beute.HasLootFor(pid) && !a.LootOpen && !UiSichtbar(lootWin), $"Leiche leer ({UiBeuteText(l4.Beute.Get(pid))}), Beutefenster zu");
                Check(l4.Glimmen != null && !l4.Glimmen.Available, $"Leiche glimmt nicht mehr ({UiGlimmText(l4.Glimmen)})");
                Check(string.IsNullOrEmpty(a.LastError) && _uiFehlertexte.Count == f0,
                    $"Keine Meldung ({(_uiFehlertexte.Count > f0 ? string.Join(" / ", _uiFehlertexte.Skip(f0)) : "keine")})");
                yield return UiFoto("questbeute_beide_genommen");
                UiLeicheWeg(a, l4);

                // ---------------------------------------------------------- J5
                UiPunkt($"J5 Platzmangel: Rucksack voll, Auftrag läuft 0/{z.count}, Leiche mit 2 → nichts verworfen, beide bleiben, „Kein Platz …“");
                a.ServerRemoveAll(g.Id);
                yield return WaitUntil(() => UiAnzahl(a.Bag, g.Id) == 0 && UiZielStand(svc, q.Id, zi) == 0, 5f);
                int gefuellt = a.ServerFillFreeSlots(Filler);
                yield return WaitUntil(() => UiAnzahl(a.Bag, Filler) == fueller0 + gefuellt, 3f);
                int platz = a.ServerModel.SpaceFor(new ItemStack(g.Id, 1));
                Check(pa.ServerIsActive(q.Id) && a.ServerModel.Count(g.Id) == 0 && UiZielStand(svc, q.Id, zi) == 0 && gefuellt > 0 && platz == 0,
                    $"„{q.Title}“ läuft, {g.DisplayName} ×{a.ServerModel.Count(g.Id)}, Ziel {UiZielStand(svc, q.Id, zi)}/{z.count}; Rucksack voll ({gefuellt} freie Plätze " +
                    $"mit Füllmaterial belegt, Platz für {g.DisplayName}: {platz})");
                var l5 = new UiLeiche();
                leichen.Add(l5);
                yield return UiQuestbeuteLeiche(a, vorlage, new List<ItemStack> { new ItemStack(g.Id, 2, false) }, l5);
                if (l5.Beute == null) yield break;
                yield return UiBeuteOeffnen(a, l5, lootWin, 1);
                if (!a.LootOpen) yield break;
                f0 = _uiFehlertexte.Count;
                yield return UiBeuteKlick(a, invUi, UiBeuteIndex(a.Loot, g.Id));
                int i5 = UiBeuteIndex(a.Loot, g.Id);
                object zeile5 = i5 >= 0 && UiFeld(invUi, "_lootRows") is IList zeilen5 && i5 < zeilen5.Count ? zeilen5[i5] : null;
                Check(a.ServerModel.Count(g.Id) == 0 && UiAnzahl(a.Bag, g.Id) == 0 && UiZielStand(svc, q.Id, zi) == 0,
                    $"Nichts aufgenommen: {g.DisplayName} ×{a.ServerModel.Count(g.Id)} beim Server, ×{UiAnzahl(a.Bag, g.Id)} beim Client, Ziel {UiZielStand(svc, q.Id, zi)}/{z.count}");
                Check(UiBeuteAnzahl(l5.Beute.Get(pid), g.Id) == 2 && UiBeuteAnzahl(a.Loot, g.Id) == 2 && a.LootOpen && UiSichtbar(lootWin) && UiZeilenName(zeile5).Contains("×2"),
                    $"Nichts verworfen, beide bleiben: Server ×{UiBeuteAnzahl(l5.Beute.Get(pid), g.Id)}, Beutefenster ×{UiBeuteAnzahl(a.Loot, g.Id)} " +
                    $"(„{StripTags(UiZeilenName(zeile5))}“), Fenster offen");
                Check(UiGlimmt(l5), $"Leiche glimmt weiter ({UiGlimmText(l5.Glimmen)})");
                Check(a.LastError == UiKeinPlatzBeute && _uiFehlertexte.Skip(f0).Contains(UiKeinPlatzBeute) && !_uiFehlertexte.Skip(f0).Contains(UiNichtMehr),
                    $"Meldung vom Server „{a.LastError}“ (Kampftext: {string.Join(" / ", _uiFehlertexte.Skip(f0))})");
                UiRoteZeile(UiKeinPlatzBeute);
                InventoryUi.RequestBag = true;   // fürs Bild: voller Rucksack neben der Beute
                yield return UiTakt();
                yield return UiFoto("questbeute_kein_platz", 0.05f);
                UiLeicheWeg(a, l5);
            }
            finally
            {
                if (a.LootOpen) a.CloseLoot();
                foreach (UiLeiche l in leichen)
                    if (l.Wolf != null) EnemyDirector.Instance.Remove(l.Wolf);
                if (a.ServerModel.Count(Filler) != fueller0)   // Füllmaterial aus J5 wieder heraus
                {
                    a.ServerRemoveAll(Filler);
                    if (fueller0 > 0) a.ServerGive(new ItemStack(Filler, fueller0));
                }
                if (hp != null && !hp.IsAlive) Info("Figur unerwartet gefallen");
            }
            yield return Ask(a, "quest-abandon", q.Id);
            yield return WaitUntil(() => !svc.IsActive(q.Id), 5f);
            a.ServerRemoveAll(g.Id);
            foreach (ItemDefinition d in new[] { mat, ruest, splitter }.Where(x => x != null))
            {
                if (a.ServerModel.Count(d.Id) <= vorher[d.Id]) continue;   // Beute aus J3 wieder heraus, alter Stand zurück
                a.ServerRemoveAll(d.Id);
                if (vorher[d.Id] > 0) a.ServerGive(new ItemStack(d.Id, vorher[d.Id], d.bind == BindType.OnPickup));
            }
            yield return UiAllesZu();
            Check(!pa.ServerIsActive(q.Id) && !pa.ServerIsCompleted(q.Id) && a.ServerModel.Count(g.Id) == 0 && a.ServerModel.Count(Filler) == fueller0 && !a.LootOpen,
                $"Aufgeräumt: „{q.Title}“ aufgegeben (nicht abgeschlossen), {g.DisplayName} entfernt, Füllmaterial heraus, Beute zu");
        }

        /// <summary>21.10. (J): Leiche des Testwegs – Gegner, persönliche Beute (Server), Glimmen (Client), Objekt-Id für „loot-open“.</summary>
        private sealed class UiLeiche
        {
            public EnemyBrain Wolf;
            public CorpseLoot Beute;
            public LootIndicator Glimmen;
            public string Oid = "";
        }

        /// <summary>
        /// 21.10. (J): Aschwolf neben der Figur, ohne ihre Beteiligung getötet (kein Beutewurf für sie) → feste persönliche Beute über den
        /// Testweg CorpseLoot.SetFixed; Glimmen wie in NetworkEnemy.OnKilled für Beuteberechtigte (NetworkInventory.NotifyLootAvailable).
        /// </summary>
        private IEnumerator UiQuestbeuteLeiche(NetworkInventory a, EnemySpawnArea vorlage, List<ItemStack> feste, UiLeiche l)
        {
            Vector3 seite = Flach(a.transform.right);
            Vector3 stelle = K28Near(a.transform.position, seite.sqrMagnitude > 0.01f ? seite.normalized : Vector3.right, 2.5f);
            l.Wolf = EnemyDirector.Instance.SpawnAt(vorlage.Prefab, stelle, a.transform.eulerAngles.y, 1, "uiauftraege-questbeute",
                br => br.SetAggroProfile(AggroProfile.Defensive), 120f);
            if (l.Wolf == null) { Check(false, "Aschwolf für die Leiche erzeugt"); yield break; }
            yield return new WaitForSecondsRealtime(0.5f);
            Health wh = l.Wolf.GetComponent<Health>();
            wh.ApplyDamage(new DamageInfo { Amount = wh.Max * 20f, Unblockable = true });   // ohne Quelle: die Figur ist nicht beuteberechtigt
            yield return new WaitForSecondsRealtime(0.6f);
            string pid = a.ProfileId;
            var nob = l.Wolf.GetComponent<FishNet.Object.NetworkObject>();
            CorpseLoot leiche = l.Wolf.GetComponent<CorpseLoot>();
            l.Glimmen = l.Wolf.GetComponent<LootIndicator>();
            l.Oid = nob != null ? nob.ObjectId.ToString() : "";
            bool schonBerechtigt = leiche != null && leiche.IsRecipient(pid);
            if (leiche != null && nob != null && !wh.IsAlive)
            {
                if (!schonBerechtigt) leiche.SetFixed(pid, feste);
                else foreach (ItemStack s in feste) leiche.AddBonus(new[] { pid }, s.id, s.count);
                a.NotifyLootAvailable(nob);   // wie NetworkEnemy.OnKilled: Glimmen beim Client an
                l.Beute = leiche;
            }
            yield return WaitUntil(() => UiGlimmt(l), 3f);
            Info($"Testweg: feste persönliche Beute in die Leiche gelegt ({(schonBerechtigt ? "AddBonus – Figur war schon beuteberechtigt" : "CorpseLoot.SetFixed")}), " +
                 "Glimmen über NetworkInventory.NotifyLootAvailable");
            Check(l.Beute != null && l.Beute.HasLootFor(pid) && feste.All(s => UiBeuteAnzahl(l.Beute.Get(pid), s.id) >= s.count),
                $"Leiche mit persönlicher Beute: {(l.Beute != null ? UiBeuteText(l.Beute.Get(pid)) : "–")}");
            Check(UiGlimmt(l) && UiFunken(l.Glimmen), $"Leiche glimmt beim Client ({UiGlimmText(l.Glimmen)})");
        }

        /// <summary>21.10. (J): Beute der Leiche über den Client öffnen (SelfTestAgent „loot-open“ → TryOpenLoot).</summary>
        private IEnumerator UiBeuteOeffnen(NetworkInventory a, UiLeiche l, object lootWin, int zeilen)
        {
            yield return Ask(a, "loot-open", l.Oid);
            yield return UiTakt();
            Check(_last.ok && a.LootOpen && UiSichtbar(lootWin) && a.Loot.Count == zeilen, $"Beutefenster offen, {a.Loot.Count} Zeilen (Soll {zeilen}; {_last.data})");
        }

        /// <summary>21.10. (J): Beutefenster zu, Leiche weg (die nächste entsteht an derselben Stelle).</summary>
        private static void UiLeicheWeg(NetworkInventory a, UiLeiche l)
        {
            if (a.LootOpen) a.CloseLoot();
            if (l.Wolf != null && EnemyDirector.Instance != null) EnemyDirector.Instance.Remove(l.Wolf);
        }

        /// <summary>21.10. (J): Leiche glimmt beim Client (LootIndicator.Available – Rechtsklick öffnet die Beute statt der Nachlese).</summary>
        private static bool UiGlimmt(UiLeiche l) => l != null && l.Glimmen != null && l.Glimmen.Available;

        /// <summary>21.10. (J): Funken des LootIndicator sichtbar (Kind „Loot Glimmer“ aktiv).</summary>
        private static bool UiFunken(LootIndicator li) => li != null && UiFeldStill(li, "_root") is Transform r && r != null && r.gameObject.activeSelf;

        private static string UiGlimmText(LootIndicator li) => li == null ? "kein LootIndicator" :
            $"LootIndicator.Available {li.Available}, Nachlese {li.SalvageAvailable}, Funken {(UiFunken(li) ? "an" : "aus")}";

        /// <summary>
        /// 21.10. (J): Klick-Zweig der Beute nachgestellt – Zeile <paramref name="index"/> (InventoryUi.LootAt an der Zeilenmitte) bzw.
        /// „Alles nehmen“ bei <paramref name="alles"/> (InventoryUi.Clicked an der Knopfmitte), dann genau der Dienst-Aufruf des Klick-Zweigs (TakeLoot/TakeAllLoot);
        /// wartet auf Beute- und Inventarstand vom Server.
        /// </summary>
        private IEnumerator UiBeuteKlick(NetworkInventory a, InventoryUi invUi, int index, bool alles = false)
        {
            IInventoryService isvc = UiFeld(invUi, "_svc") as IInventoryService;
            int lr = a.LootRevision, cr = a.ClientRevision;
            bool trifft;
            if (alles)
            {
                object knopf = UiFeld(invUi, "_takeAll");
                MethodInfo clicked = typeof(InventoryUi).GetMethod("Clicked", BindingFlags.NonPublic | BindingFlags.Static);
                if (clicked == null) _uiFehlendeHaken.Add("InventoryUi.Clicked");
                trifft = clicked != null && UiSichtbar(knopf) && clicked.Invoke(null, new[] { knopf, (object)UiMitte(UiRt(knopf)) }) is bool b && b;
                Check(trifft, "Knopf „Alles nehmen“ sichtbar, Klick trifft ihn (InventoryUi.Clicked)");
                if (trifft) isvc?.TakeAllLoot();
            }
            else
            {
                MethodInfo lootAt = typeof(InventoryUi).GetMethod("LootAt", BindingFlags.NonPublic | BindingFlags.Instance);
                if (lootAt == null) _uiFehlendeHaken.Add("InventoryUi.LootAt");
                object zeile = UiFeld(invUi, "_lootRows") is IList zeilen && index >= 0 && index < zeilen.Count ? zeilen[index] : null;
                trifft = lootAt != null && UiSichtbar(zeile) && lootAt.Invoke(invUi, new object[] { UiMitte(UiRt(zeile)) }) is int li && li == index;
                Check(trifft, $"Beutezeile {index + 1} („{StripTags(UiZeilenName(zeile))}“) sichtbar, Klick trifft sie (InventoryUi.LootAt)");
                if (trifft) isvc?.TakeLoot(index);
            }
            if (trifft && isvc != null) yield return WaitUntil(() => a.LootRevision != lr && a.ClientRevision != cr, 5f);
            yield return UiTakt();
        }

        /// <summary>21.10. (J): Stand eines Ziels beim Client (QuestView.Progress), -1 ohne laufenden Auftrag.</summary>
        private static int UiZielStand(IProgressionService svc, string questId, int ziel) =>
            svc.Quests.FirstOrDefault(v => v.Def != null && v.Def.Id == questId) is QuestView qv && ziel >= 0 && ziel < qv.Progress.Length ? qv.Progress[ziel] : -1;

        private static int UiBeuteAnzahl(IEnumerable<ItemStack> beute, string id) => beute == null ? 0 : beute.Where(s => s.id == id).Sum(s => s.count);

        private static int UiBeuteIndex(IReadOnlyList<ItemStack> beute, string id)
        {
            for (int i = 0; beute != null && i < beute.Count; i++) if (beute[i].id == id) return i;
            return -1;
        }

        private static string UiBeuteText(IEnumerable<ItemStack> beute) =>
            beute == null || !beute.Any() ? "leer" : string.Join(", ", beute.Select(s => $"{GameData.Item(s.id)?.DisplayName ?? s.id} ×{s.count}"));

        private static string UiZeilenName(object beuteZeile) => UiText(UiFeldStill(beuteZeile, "Name"));

        /// <summary>21.10. (J): stapelbarer eigener Questgegenstand mit mindestens 2 verlangten (bevorzugt G3 „Gegengift“: 6 Giftdrüsen) – weder laufend noch abgeschlossen.</summary>
        private static QuestDefinition UiQuestbeuteAuftrag(IProgressionService svc) =>
            new[] { GameData.Quest(GlockenrastCatalog.G3) }.Concat(GameData.Quests.Where(x => x != null).OrderBy(x => x.Id, StringComparer.Ordinal))
                .FirstOrDefault(q => q != null && !svc.IsCompleted(q.Id) && !svc.IsActive(q.Id) && UiEigenesSammelziel(q) is QuestObjective o && o.count >= 2 &&
                                     GameData.Item(o.targetId) is ItemDefinition d && !d.unique && d.maxStack >= o.count);

        // ================================================================== E. Gefallene (Nr. 8, 21.10.)

        private IEnumerator UiGefallen(NetworkInventory a, NetworkProgression pa, QuestUi ui, IProgressionService svc)
        {
            // 21.10.: H (Beute neben den Gesprächsfenstern) nutzt denselben Aufbau wie E – Gespräch, Händler und Beute offen – und läuft davor
            Line("");
            Line("[H Beute neben Gespräch und Händler statt darunter (Fund aus Bild 15)]");
            UiPunkt("H1 Gespräch, Händler- und Beutefenster offen: Beute daneben, ganz im Bild, „Alles nehmen“ frei, ein Klick auf eine Ware nimmt keine Beute");
            Health hp = a.GetComponent<Health>();
            NetworkPlayer np = a.GetComponent<NetworkPlayer>();
            NpcDefinition haendler = UiHaendler();
            InventoryUi invUi = FindAnyObjectByType<InventoryUi>();
            EnemySpawnArea vorlage = K28Template(KC.EnemyWolf) ??
                                     FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None).FirstOrDefault(x => x.Definition != null && x.Prefab != null && !x.Definition.neutral);
            if (hp == null || np == null || haendler == null || invUi == null || vorlage == null || EnemyDirector.Instance == null)
            {
                Check(false, "Figur, Händler, Inventar-Oberfläche, Gegner-Vorlage und Gegner-Steuerung vorhanden");
                yield break;
            }
            object dlgWin = UiFeld(ui, "_dlgWin"), vendWin = UiFeld(ui, "_vendWin"), lootWin = UiFeld(invUi, "_lootWin");
            yield return UiZumNpc(a, haendler.Id);

            // Leiche mit Beute für die Figur neben ihr (Aschwolf Stufe 1, greift nicht an; Erst-Treffer der Figur = beteiligt)
            Vector3 seite = Flach(a.transform.right);
            Vector3 stelle = K28Near(a.transform.position, seite.sqrMagnitude > 0.01f ? seite.normalized : Vector3.right, 2.5f);
            EnemyBrain wolf = EnemyDirector.Instance.SpawnAt(vorlage.Prefab, stelle, a.transform.eulerAngles.y, 1, "uiauftraege-gefallen",
                br => br.SetAggroProfile(AggroProfile.Defensive), 120f);
            if (wolf == null) { Check(false, "Aschwolf für die Leiche erzeugt"); yield break; }
            yield return new WaitForSecondsRealtime(0.5f);
            Health wh = wolf.GetComponent<Health>();
            wh.ApplyDamage(new DamageInfo { Amount = 1f, Source = a.gameObject });
            yield return new WaitForSecondsRealtime(0.1f);
            wh.ApplyDamage(new DamageInfo { Amount = wh.Max * 20f, Source = a.gameObject, Unblockable = true });
            yield return new WaitForSecondsRealtime(0.6f);
            CorpseLoot leiche = wolf.GetComponent<CorpseLoot>();
            string oid = wolf.GetComponent<FishNet.Object.NetworkObject>().ObjectId.ToString();
            if (leiche != null && !wh.IsAlive && !leiche.HasLootFor(a.ProfileId))
            {
                leiche.AddBonus(new[] { a.ProfileId }, "item.broken_pocketwatch", 1);   // Testhilfe: Beutewurf war leer – feste Beute, damit Plündern prüfbar bleibt
                Info("Testhilfe: Beutewurf leer – eine Zerbrochene Taschenuhr als Beute ergänzt");
            }
            string beute = leiche != null ? string.Join(", ", leiche.Get(a.ProfileId).Select(s => $"{GameData.Item(s.id)?.DisplayName} ×{s.count}")) : "";
            Check(!wh.IsAlive && leiche != null && leiche.HasLootFor(a.ProfileId), $"Leiche mit Beute für die Figur ({beute})");

            yield return Ask(a, "npc-talk", haendler.Id);
            yield return UiTakt();
            yield return UiKlickHandeln(ui);
            yield return Ask(a, "loot-open", oid);
            yield return UiTakt();
            Check(svc.DialogNpc != null && svc.DialogNpc.Id == haendler.Id && UiSichtbar(dlgWin) && UiSichtbar(vendWin) && Equals(UiFeld(pa, "_dialogNpc"), haendler.Id),
                $"Vorher: Gespräch und Händlerfenster bei {haendler.Name} offen (auch beim Server)");
            Check(_last.ok && a.LootOpen && UiSichtbar(lootWin), $"Vorher: Beutefenster offen ({_last.data})");
            UiBeuteFrei(invUi, ui, "Gespräch und Händlerfenster");
            yield return UiFoto("beute_neben_haendler");

            Line("");
            Line("[E Gefallene: kein Gespräch, Handel, Plündern (Nr. 8)]");
            UiPunkt("E1 Tod bei offenem Gespräch, Händler- und Beutefenster: alles schließt sich, auch das Gespräch beim Server");
            Func<NetworkPlayer, string> haltVorher = NetworkPlayer.HoldRespawn;
            // Testhilfe: Figur bleibt liegen (sonst erwacht sie nach 6 s am Wachtfeuer) – Kolosseum-Haken, nur für diese Figur
            NetworkPlayer.HoldRespawn = p => ReferenceEquals(p, np) ? "Gefallen – der Test hält die Figur liegen." : haltVorher?.Invoke(p);
            try
            {
                UiSetze(hp, "_invulnerableUntil", -1f);   // Schutz vom Anfang der Suite aufheben
                hp.ApplyDamage(new DamageInfo { Amount = hp.Max * 20f, Direction = -a.transform.forward, Unblockable = true });
                yield return UiTakt();
                yield return WaitUntil(() => svc.DialogNpc == null && !a.LootOpen && UiFeld(pa, "_dialogNpc") == null, 3f);
                yield return UiTakt();
                Check(!hp.IsAlive && np.ServerHeldDown, $"Figur gefallen ({hp.Current:0}/{hp.Max:0} Leben), bleibt liegen");
                Check(svc.DialogNpc == null && !UiSichtbar(dlgWin) && !UiSichtbar(vendWin), "Client: Gespräch und Händlerfenster geschlossen (NpcInteractor)");
                Check(UiFeld(pa, "_dialogNpc") == null, "Server: Gespräch beendet (ServerCloseDialog)");
                Check(!a.LootOpen && !UiSichtbar(lootWin), "Client: Beutefenster geschlossen (LootInteractor)");
                yield return UiFoto("gefallen_fenster_zu");

                // ---------------------------------------------------------- E2
                UiPunkt("E2 Tot: Plündern, Gespräch, Annehmen, Fundstelle → Server lehnt ab „Du bist tot.“, nichts öffnet sich");
                int f0 = _uiFehlertexte.Count;
                yield return Ask(a, "loot-open", oid);
                yield return UiTakt();
                Check(!_last.ok && !a.LootOpen && !UiSichtbar(lootWin) && leiche.HasLootFor(a.ProfileId), $"Plündern: kein Beutefenster, Beute bleibt in der Leiche ({_last.data})");
                Check(_uiFehlertexte.Skip(f0).Contains(UiTot), $"Plündern: Meldung vom Server „{string.Join(" / ", _uiFehlertexte.Skip(f0))}“");

                yield return Ask(a, "npc-talk", haendler.Id);
                yield return UiTakt();
                Check(!_last.ok && pa.LastError == UiTot && svc.DialogNpc == null && !UiSichtbar(dlgWin) && UiFeld(pa, "_dialogNpc") == null,
                    $"Gespräch mit {haendler.Name}: abgelehnt „{pa.LastError}“, kein Fenster");

                QuestDefinition angebot = UiAngebot(svc, a.ServerModel.Class);
                if (angebot != null)
                {
                    UiSetze(pa, "_dialogNpc", angebot.giverNpc);   // Testhilfe: Gespräch beim Server noch offen – wie bei einem Client, der es beim Tod nicht schließt
                    yield return Ask(a, "quest-accept", angebot.Id);
                    Check(!_last.ok && pa.LastError == UiTot && !pa.ServerIsActive(angebot.Id),
                        $"Annehmen „{angebot.Title}“ trotz offenem Server-Gespräch: abgelehnt „{pa.LastError}“, Auftrag läuft nicht");
                    UiSetze(pa, "_dialogNpc", null);
                }
                else Check(false, "Angebot für die Annehmen-Probe gefunden");

                int f1 = _uiFehlertexte.Count;
                string fund = pa.ServerStoryAction(GlockenrastCatalog.NodeWheel, "");
                yield return WaitUntil(() => _uiFehlertexte.Count > f1, 3f);
                Check(fund == "tot" && _uiFehlertexte.Skip(f1).Contains(UiTot), $"Fundstelle {GlockenrastCatalog.NodeWheel}: „{fund}“ (Soll „tot“), Meldung „{string.Join(" / ", _uiFehlertexte.Skip(f1))}“");
                UiRoteZeile(UiTot);
                yield return UiFoto("gefallen_abgelehnt", 0.05f);

                // ---------------------------------------------------------- E3
                UiPunkt("E3 Wiederbelebt: Gespräch und Beute gehen wieder");
                np.ServerReviveHere(1f);
                NetworkPlayer.HoldRespawn = haltVorher;
                yield return new WaitForSecondsRealtime(0.5f);
                Check(hp.IsAlive && !np.ServerHeldDown, $"Wiederbelebt ({hp.Current:0}/{hp.Max:0} Leben)");
                string fund2 = pa.ServerStoryAction(GlockenrastCatalog.NodeWheel, "");
                Check(fund2 != "tot", $"Fundstelle lebend: „{fund2}“ (kein „tot“ mehr – ohne laufenden Auftrag erwartet „Auftrag läuft nicht“)");
                yield return Ask(a, "npc-talk", haendler.Id);
                yield return UiTakt();
                Check(_last.ok && svc.DialogNpc != null && svc.DialogNpc.Id == haendler.Id && UiSichtbar(dlgWin) && Equals(UiFeld(pa, "_dialogNpc"), haendler.Id),
                    $"Gespräch mit {haendler.Name} öffnet wieder (Client und Server)");
                yield return Ask(a, "loot-open", oid);
                yield return UiTakt();
                Check(_last.ok && a.LootOpen && UiSichtbar(lootWin), $"Beutefenster öffnet wieder ({_last.data})");

                // 21.10.: H2 – frisches Gespräch beim Händler (Händlerfenster zu): Beute bleibt rechts vom Händlerplatz, springt beim Handeln nicht
                UiPunkt("H2 Gespräch beim Händler ohne Händlerfenster, Beute offen: Beute daneben, ganz im Bild, kein Klick trifft beide");
                Check(!UiSichtbar(vendWin), "Händlerfenster zu (neues Gespräch)");
                UiBeuteFrei(invUi, ui, "Gespräch ohne Händlerfenster");
                yield return UiFoto("wiederbelebt");
            }
            finally
            {
                NetworkPlayer.HoldRespawn = haltVorher;
                if (!hp.IsAlive) np.ServerReviveHere(1f);
                hp.SetInvulnerableFor(7200f);
            }
            a.CloseLoot();
            for (int i = 0; i < 4 && UiFensterstapel.Oberstes != ""; i++)
            {
                UiFensterstapel.EscAusloesen();
                yield return UiTakt();
            }
            Check(UiFensterstapel.Oberstes == "" && svc.DialogNpc == null && !a.LootOpen, "Aufgeräumt: Beute, Rucksack und Gespräch zu");
            if (wolf != null) EnemyDirector.Instance.Remove(wolf);
        }

        // ================================================================== Hilfen (21.10.: F, G, E, H)

        /// <summary>
        /// 21.10. (H): Beutefenster frei neben den Gesprächsfenstern – keine Überdeckung mit sichtbarem Gespräch, Händler, Lehrer
        /// oder Taschenfenster, ganz im Bild, „Alles nehmen“ frei, und ein Klick auf eine sichtbare Gesprächs-, Waren- oder
        /// Rückkaufzeile träfe keine Beutezeile (InventoryUi.LootAt an fünf Punkten je Zeile – beide Oberflächen werten denselben
        /// Klick aus). Gegenprobe: die erste Beutezeile selbst wird getroffen.
        /// </summary>
        private void UiBeuteFrei(InventoryUi invUi, QuestUi ui, string lage)
        {
            RectTransform loot = UiRt(UiFeld(invUi, "_lootWin"));
            if (!UiSichtbar(loot)) { Check(false, $"{lage}: Beutefenster sichtbar"); return; }
            Canvas leinwand = loot.GetComponentInParent<Canvas>();
            float skala = leinwand != null && leinwand.scaleFactor > 0f ? leinwand.scaleFactor : 1f;   // Bildpunkte je Oberflächen-Einheit
            string Spanne(Rect r) => $"{r.xMin / skala:0}–{r.xMax / skala:0}";
            Rect rl = UiRect(loot);

            var fenster = new List<(string name, Rect r)>();
            foreach ((object o, string name) in new[] { (UiFeld(ui, "_dlgWin"), "Gespräch"), (UiFeld(ui, "_vendWin"), "Händler"),
                                                        (UiFeldStill(TrainerUi.Instance, "_win"), "Lehrer"), (UiFeld(invUi, "_bagWin"), "Taschen") })
                if (UiSichtbar(o)) fenster.Add((name, UiRect(UiRt(o))));
            List<string> ueber = fenster.Where(f => f.r.Overlaps(rl)).Select(f => f.name).ToList();
            bool imBild = rl.xMin >= 0f && rl.xMax <= Screen.width && rl.yMin >= 0f && rl.yMax <= Screen.height;
            Check(fenster.Any(f => f.name == "Gespräch") && ueber.Count == 0 && imBild,
                $"{lage}: Beute x {Spanne(rl)} überdeckt nichts und liegt unter nichts ({string.Join(", ", fenster.Select(f => $"{f.name} x {Spanne(f.r)}"))}" +
                $"{(ueber.Count > 0 ? "; ÜBERDECKT: " + string.Join(", ", ueber) : "")}), {(imBild ? "ganz im Bild" : "NICHT ganz im Bild")} (Breite {Screen.width / skala:0})");

            // ganzer Knopf (nicht nur die Mitte – im ersten Lauf ragte er zur Hälfte links aus dem Fenster unter den Händler)
            RectTransform alles = UiRt(UiFeld(invUi, "_takeAll"));
            Rect ra = alles != null ? UiRect(alles) : default;
            bool imFenster = ra.xMin >= rl.xMin && ra.xMax <= rl.xMax && ra.yMin >= rl.yMin && ra.yMax <= rl.yMax;
            List<string> unter = fenster.Where(f => f.r.Overlaps(ra)).Select(f => f.name).ToList();
            Check(UiSichtbar(alles) && imFenster && unter.Count == 0,
                $"{lage}: „Alles nehmen“ x {Spanne(ra)} ganz im Beutefenster ({(imFenster ? "ja" : "NEIN")}) und frei{(unter.Count > 0 ? " – ÜBERDECKT von " + string.Join(", ", unter) : "")}");

            MethodInfo lootAt = typeof(InventoryUi).GetMethod("LootAt", BindingFlags.NonPublic | BindingFlags.Instance);
            if (lootAt == null) { _uiFehlendeHaken.Add("InventoryUi.LootAt"); Check(false, "InventoryUi.LootAt vorhanden"); return; }
            int LootBei(Vector2 p) => lootAt.Invoke(invUi, new object[] { p }) is int i ? i : -1;
            object ersteBeute = (UiFeld(invUi, "_lootRows") as IEnumerable)?.Cast<object>().FirstOrDefault(UiSichtbar);
            Check(ersteBeute != null && LootBei(UiMitte(UiRt(ersteBeute))) == 0, $"{lage}: Gegenprobe – Klick auf die erste Beutezeile trifft sie (LootAt 0)");
            // Zeilen (Gespräch, Ware, Rückkauf) an fünf Punkten, dazu ein Raster 11 × 11 über jedes sichtbare Gesprächsfenster (auch Knöpfe)
            int zeilen = 0, punkte = 0, treffer = 0;
            foreach (string liste in new[] { "_dlgRows", "_stockRows", "_buybackRows" })
                foreach (object z in UiAktiveZeilen(ui, liste))
                {
                    Rect r = UiRect(UiRt(z));
                    zeilen++;
                    for (int k = 0; k < 5; k++, punkte++)
                        if (LootBei(new Vector2(Mathf.Lerp(r.xMin + 1f, r.xMax - 1f, k / 4f), r.center.y)) >= 0) treffer++;
                }
            foreach ((string name, Rect r) in fenster.Where(f => f.name != "Taschen"))
                for (int ix = 0; ix <= 10; ix++)
                    for (int iy = 0; iy <= 10; iy++, punkte++)
                        if (LootBei(new Vector2(Mathf.Lerp(r.xMin + 1f, r.xMax - 1f, ix / 10f), Mathf.Lerp(r.yMin + 1f, r.yMax - 1f, iy / 10f))) >= 0) treffer++;
            bool haendler = fenster.Any(f => f.name == "Händler");
            Check((zeilen > 0 || !haendler) && treffer == 0,
                $"{lage}: {punkte} Klickpunkte ({zeilen} Gesprächs-/Waren-/Rückkaufzeilen, Raster über {string.Join(", ", fenster.Where(f => f.name != "Taschen").Select(f => f.name))}) treffen keine Beutezeile ({treffer} Treffer)");
        }

        /// <summary>Hinlaufen (Testweg) und warten, bis keine Geschichtsszene läuft – wie in C.</summary>
        private IEnumerator UiZumNpc(NetworkInventory a, string npcId)
        {
            yield return GotoNpc(a, npcId);
            yield return new WaitForSecondsRealtime(1f);
            yield return WaitUntil(() => !Ashveil.Gameplay.Story.StoryDirector.Active, 25f);
        }

        /// <summary>Klick-Zweig „Handeln“ nachgestellt: Händlerfenster auf, Rucksack dazu.</summary>
        private IEnumerator UiKlickHandeln(QuestUi ui)
        {
            object knopf = UiFeld(ui, "_dlgTrade");
            if (!UiTrifft(knopf)) { Check(false, "Knopf „Handeln“ anklickbar"); yield break; }
            if (!(UiFeld(ui, "_vendorOpen") is bool offen) || !offen) UiSetze(ui, "_vendorOpen", true);   // Klick schaltet um
            InventoryUi.RequestBag = true;
            UiSetze(ui, "_dirty", true);
            yield return UiTakt();
        }

        /// <summary>Händler in der Szene mit dem größten Angebot (Kareth v4: Dorn 8, Idun 7, Gerrit 6, Maelis/Ilse 4 – seit 21.10. FORTSCHRITT-03/05).</summary>
        private static NpcDefinition UiHaendler() =>
            NpcIdentity.All.Where(n => n != null).Select(n => GameData.Npc(n.NpcId)).Where(d => d != null && d.vendor && d.stock != null && d.stock.Count > 0)
                .OrderByDescending(d => d.stock.Count).ThenBy(d => d.Id, StringComparer.Ordinal).FirstOrDefault();

        /// <summary>Sechs (bis sieben) verschiedene verkäufliche Gegenstände für den Rückkauf.</summary>
        private static List<string> UiVerkaufsware() =>
            new[] { "item.broken_pocketwatch", "item.healing_draught", "item.dry_bread", "item.grey_wool_cloak", "item.ash_wolf_pelt", "item.wolf_fang", "item.oak_shield" }
                .Where(id => GameData.Item(id) is ItemDefinition d && Progression.Sellable(d) && !d.unique).ToList();

        /// <summary>
        /// Erstes Angebot eines NPC in der Szene, das die Figur jetzt annehmen dürfte (Regel wie die Liste der QuestUi, steht dort oben) –
        /// bevorzugt bei jemandem, der nicht handelt (sonst geht der Rucksack mit auf).
        /// </summary>
        private static QuestDefinition UiAngebot(IProgressionService svc, CharacterClassDefinition cls)
        {
            QuestGate gate = QuestGate.Local(cls);
            QuestDefinition beiHaendler = null;
            foreach (QuestDefinition q in GameData.Quests)
            {
                if (q == null || q.offeredByStory || NpcIdentity.Find(q.giverNpc) == null) continue;
                if (!QuestRules.Offered(q, q.giverNpc, svc.Level, svc.IsCompleted, svc.IsActive, gate)) continue;
                if (GameData.Npc(q.giverNpc)?.vendor == true) { if (beiHaendler == null) beiHaendler = q; continue; }
                return q;
            }
            return beiHaendler;
        }

        /// <summary>20 Aufträge für den Testweg, die mit dem Geber von <paramref name="ziel"/> nichts zu tun haben (seine Liste bleibt kurz).</summary>
        private static List<string> UiFueller(IProgressionService svc, QuestDefinition ziel)
        {
            string npc = ziel.giverNpc;
            return UiAuftragsPool(ziel.Id)
                .Concat(GameData.Quests.Where(x => x != null).OrderBy(x => x.Id, StringComparer.Ordinal).Select(x => x.Id))
                .Distinct()
                .Where(id => GameData.Quest(id) is QuestDefinition d && d.Id != ziel.Id && d.giverNpc != npc && d.TurnInNpc != npc &&
                             !d.objectives.Any(o => o.targetId == npc) && !svc.IsCompleted(id) && !svc.IsActive(id))
                .Take(QuestRules.MaxActive).ToList();
        }

        /// <summary>Gesprächszeile des Auftrags <paramref name="q"/> (oder null).</summary>
        private object UiAuftragsZeile(QuestUi ui, QuestDefinition q)
        {
            var gelistet = UiFeld(ui, "_listed") as List<QuestDefinition> ?? new List<QuestDefinition>();
            return UiAktiveZeilen(ui, "_dlgRows").FirstOrDefault(z => UiIndex(z) >= 0 && UiIndex(z) < gelistet.Count && gelistet[UiIndex(z)].Id == q.Id);
        }

        /// <summary>Rote Fehlerzeile oben (HudRoot) zeigt <paramref name="soll"/>.</summary>
        private void UiRoteZeile(string soll)
        {
            HudRoot hud = FindAnyObjectByType<HudRoot>();
            object z = UiFeld(hud, "_error");
            Color rot = UiFarbe(z);
            Check(UiSichtbar(z) && UiText(z) == soll && UiAlpha(z) > 0.5f && rot.r > 0.8f && rot.g < 0.5f,
                $"Rote Fehlerzeile oben: „{UiText(z)}“ (Deckkraft {UiAlpha(z):0.00}, Farbe {rot.r:0.00}/{rot.g:0.00}/{rot.b:0.00})");
        }

        // ================================================================== Hilfen

        private void UiKampftext(CombatTextEvent e)
        {
            if (e.Kind == CombatTextKind.Error) { _uiFehlertexte.Add(e.Text); return; }
            if (e.Text != "Entkommen") return;
            _uiEntkommen++;
            _uiLetztesEntkommen = e;
            _uiEntkommenZeiten.Add((Time.time, Time.frameCount));
        }

        /// <summary>Neuen Prüfpunkt beginnen (der vorige wird mit „bestanden“, wenn keine seiner Prüfungen fehlschlug, abgelegt).</summary>
        private void UiPunkt(string punkt)
        {
            if (!string.IsNullOrEmpty(_uiPunkt)) _uiPunkte.Add((_uiPunkt, _failed == _uiPunktFehler));
            _uiPunkt = punkt;
            _uiPunktFehler = _failed;
            if (!string.IsNullOrEmpty(punkt)) Line("  — " + punkt);
        }

        /// <summary>Bildschirmfoto der Spielansicht (mit Oberfläche) nach Logs/Ashveil/UiAuftraege/&lt;nr&gt;_&lt;name&gt;.png.</summary>
        private IEnumerator UiFoto(string name, float warten = 0.3f)
        {
            if (warten > 0f) yield return new WaitForSecondsRealtime(warten);
            yield return new WaitForEndOfFrame();
            Texture2D bild = null;
            try
            {
                // kleine Spielansicht im Editor (z. B. 1101 × 485): doppelt so groß aufnehmen, damit die Oberfläche lesbar bleibt
                bild = ScreenCapture.CaptureScreenshotAsTexture(Screen.height < 900 ? 2 : 1);
                string pfad = Ashveil.Core.ProjectPaths.Logs("UiAuftraege", $"{_uiBilder.Count + 1:00}_{name}.png");
                Directory.CreateDirectory(Path.GetDirectoryName(pfad));
                File.WriteAllBytes(pfad, bild.EncodeToPNG());
                _uiBilder.Add($"{pfad} ({bild.width} × {bild.height})");
                Info($"Bild: {Path.GetFileName(pfad)} ({bild.width} × {bild.height})");
            }
            catch (Exception e) { Info("Bildschirmfoto nicht möglich: " + e.Message); }
            finally { if (bild != null) Destroy(bild); }
        }

        /// <summary>Zwei Bilder warten: QuestUi.Update übernimmt „_dirty“ und baut die Anzeige neu.</summary>
        private static IEnumerator UiTakt()
        {
            yield return null;
            yield return null;
            yield return new WaitForSecondsRealtime(0.05f);
        }

        /// <summary>Klick-Zweig der Seitenpfeile (&lt; &gt;) nachgestellt.</summary>
        private IEnumerator UiKlickSeite(QuestUi ui, object pfeil, int richtung)
        {
            if (!UiTrifft(pfeil)) { Check(false, $"Pfeil {(richtung > 0 ? ">" : "<")} anklickbar"); yield break; }
            UiSetze(ui, "_logPage", (int)(UiFeld(ui, "_logPage") ?? 0) + richtung);
            UiSetze(ui, "_logSelected", null);
            UiSetze(ui, "_dirty", true);
            yield return UiTakt();
        }

        /// <summary>Kareth-Aufträge für das Buch (Ergänzungs- und Nebenaufträge), notfalls weitere ohne Szenenbindung.</summary>
        private static List<string> UiAuftragsPool(string ohne)
        {
            var ids = KC.Quests.Concat(KC.NebenQuests).Where(id => id != ohne && GameData.Quest(id) != null).Distinct().ToList();
            foreach (QuestDefinition q in GameData.Quests.Where(x => x != null).OrderBy(x => x.Id, StringComparer.Ordinal))
                if (ids.Count < 30 && q.Id != ohne && !ids.Contains(q.Id) && (string.IsNullOrEmpty(q.requiredSceneTag) || q.requiredSceneTag == KC.SceneTag)) ids.Add(q.Id);
            return ids;
        }

        /// <summary>Auftrag, der nach einem Gespräch abgabebereit ist und Gegenstände für diese Klasse gibt (bevorzugt Q1 „Eine Klinge für die Wache“).</summary>
        private static QuestDefinition UiAbgabeAuftrag(CharacterClassDefinition cls)
        {
            bool Passt(QuestDefinition q) =>
                q != null && q.objectives.Count > 0 && q.objectives.All(o => o.type == QuestObjectiveType.TalkTo && o.targetId == q.TurnInNpc) &&
                NpcIdentity.Find(q.giverNpc) != null && NpcIdentity.Find(q.TurnInNpc) != null &&
                (string.IsNullOrEmpty(q.requiredSceneTag) || q.requiredSceneTag == KC.SceneTag) &&
                QuestRules.ForClass(q.rewardItems, cls).Count + QuestRules.ForClass(q.choiceItems, cls).Count > 0;
            return new[] { "quest.blade_for_the_watch", KC.B8, KC.B6 }.Select(GameData.Quest)
                .Concat(GameData.Quests.Where(x => x != null).OrderBy(x => x.Id, StringComparer.Ordinal)).FirstOrDefault(Passt);
        }

        private static RectTransform UiEinstellungenKnopf()
        {
            SettingsUi s = FindAnyObjectByType<SettingsUi>();
            RectTransform rt = s != null ? UiRt(UiFeldStill(s, "_openButton")) : null;
            if (rt != null) return rt;
            GameObject go = GameObject.Find("Button Einstellungen (O)");
            return go != null ? go.transform as RectTransform : null;
        }

        // ---------------------------------------------------------- Reflexion (Felder der Oberfläche, ohne Spiel-Logik zu ändern)

        private object UiFeld(object o, string name)
        {
            if (o == null) return null;
            FieldInfo f = o.GetType().GetField(name, UiFlags);
            if (f == null) { _uiFehlendeHaken.Add(o.GetType().Name + "." + name); return null; }
            return f.GetValue(f.IsStatic ? null : o);
        }

        private static object UiFeldStill(object o, string name) => o?.GetType().GetField(name, UiFlags)?.GetValue(o);

        private void UiSetze(object o, string name, object wert)
        {
            FieldInfo f = o?.GetType().GetField(name, UiFlags);
            if (f == null) { _uiFehlendeHaken.Add((o?.GetType().Name ?? "?") + "." + name); return; }
            f.SetValue(o, wert);
        }

        private void UiModus(QuestUi ui, string modus)
        {
            FieldInfo f = typeof(QuestUi).GetField("_mode", UiFlags);
            if (f == null || !f.FieldType.IsEnum) { _uiFehlendeHaken.Add("QuestUi._mode"); return; }
            f.SetValue(ui, Enum.Parse(f.FieldType, modus));
        }

        private string UiModusName(QuestUi ui) => UiFeld(ui, "_mode")?.ToString() ?? "?";

        /// <summary>Trefferprüfung wie im Klick-Zweig: QuestUi.Clicked(Btn, Maus) an der Mitte des Knopfes.</summary>
        private bool UiTrifft(object knopf)
        {
            RectTransform rt = UiRt(knopf);
            MethodInfo m = typeof(QuestUi).GetMethod("Clicked", BindingFlags.NonPublic | BindingFlags.Static);
            if (m == null) { _uiFehlendeHaken.Add("QuestUi.Clicked"); return false; }
            return rt != null && (bool)m.Invoke(null, new[] { knopf, (object)UiMitte(rt) });
        }

        /// <summary>QuestUi.RowAt(Liste, Maus): welche Zeile ein Klick an dieser Stelle träfe.</summary>
        private object UiZeileBei(QuestUi ui, string liste, Vector2 p)
        {
            MethodInfo m = typeof(QuestUi).GetMethod("RowAt", BindingFlags.NonPublic | BindingFlags.Static);
            object rows = UiFeld(ui, liste);
            if (m == null) { _uiFehlendeHaken.Add("QuestUi.RowAt"); return null; }
            return rows != null ? m.Invoke(null, new[] { rows, (object)p }) : null;
        }

        private List<object> UiAktiveZeilen(QuestUi ui, string liste) =>
            (UiFeld(ui, liste) as IEnumerable)?.Cast<object>().Where(UiSichtbar).ToList() ?? new List<object>();

        private int UiIndex(object zeile) => UiFeld(zeile, "Index") is int i ? i : -1;
        private string UiZeilenText(object zeile) => UiText(UiFeld(zeile, "Text"));

        private bool UiZeileGewaehlt(object zeile)
        {
            object soll = typeof(QuestUi).GetField("RowSelected", UiFlags)?.GetValue(null);
            return soll is Color c && UiFarbe(UiFeld(zeile, "Back")) == c;
        }

        private static RectTransform UiRt(object o)
        {
            if (o == null) return null;
            if (o is Component c) return c.transform as RectTransform;
            return UiFeldStill(o, "Rt") as RectTransform;
        }

        private static bool UiSichtbar(object o)
        {
            RectTransform rt = UiRt(o);
            return rt != null && rt.gameObject.activeInHierarchy;
        }

        // Bildschirm-Oberfläche (Screen Space Overlay): Weltkoordinaten = Bildschirmpixel
        private static Vector2 UiMitte(RectTransform rt)
        {
            if (rt == null) return new Vector2(-1f, -1f);
            Vector3 c = rt.TransformPoint(rt.rect.center);
            return new Vector2(c.x, c.y);
        }

        private static Rect UiRect(RectTransform rt)
        {
            var w = new Vector3[4];
            rt.GetWorldCorners(w);
            return Rect.MinMaxRect(w[0].x, w[0].y, w[2].x, w[2].y);
        }

        // TextMeshPro ohne Assembly-Verweis: Eigenschaften über Reflexion
        private static string UiText(object tmp) => tmp?.GetType().GetProperty("text")?.GetValue(tmp) as string ?? "";
        private static float UiAlpha(object tmp) => tmp?.GetType().GetProperty("alpha")?.GetValue(tmp) is float f ? f : 0f;
        private static Color UiFarbe(object graphic) => graphic?.GetType().GetProperty("color")?.GetValue(graphic) is Color c ? c : Color.clear;

        /// <summary>Unterkante des gezeichneten Textes (Bildschirmpixel) – textBounds nach ForceMeshUpdate.</summary>
        private static float UiTextUnten(object tmp)
        {
            RectTransform rt = UiRt(tmp);
            if (rt == null) return 0f;
            Type t = tmp.GetType();
            MethodInfo force = t.GetMethod("ForceMeshUpdate", new[] { typeof(bool), typeof(bool) });
            force?.Invoke(tmp, new object[] { false, false });
            if (!(t.GetProperty("textBounds")?.GetValue(tmp) is Bounds b)) return UiRect(rt).yMin;
            return rt.TransformPoint(new Vector3(b.center.x, b.min.y, 0f)).y;
        }

        /// <summary>21.10.: Oberkante des gezeichneten Textes (Bildschirmpixel) – Gegenstück zu <see cref="UiTextUnten"/>.</summary>
        private static float UiTextOben(object tmp)
        {
            RectTransform rt = UiRt(tmp);
            if (rt == null) return 0f;
            Type t = tmp.GetType();
            MethodInfo force = t.GetMethod("ForceMeshUpdate", new[] { typeof(bool), typeof(bool) });
            force?.Invoke(tmp, new object[] { false, false });
            if (!(t.GetProperty("textBounds")?.GetValue(tmp) is Bounds b)) return UiRect(rt).yMax;
            return rt.TransformPoint(new Vector3(b.center.x, b.max.y, 0f)).y;
        }
    }
}
