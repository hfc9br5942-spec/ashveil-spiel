using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ashveil.Core;
using Ashveil.Gameplay.Colosseum;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.World;
using UnityEngine;
using UnityEngine.AI;

namespace Ashveil.Networking
{
    /// <summary>
    /// 22.10.2026 (Spielablauf Runde 6, Prüfschritt) – Teil 3b der Suite mehrspieler4: Kolosseum mit dem Editor-Host A und dem ECHTEN
    /// Test-Client C (Aschenweberin, Stufe 15), Zeitraffer ×12, Glocke (Sandbox_M1 – das Kolosseum gibt es nur hier):
    ///   INSTANZEN-01  A geht allein in den Ring („solo:A“), lädt danach C ein; C folgt durch die Pforte → dieselbe Arena („party:…“).
    ///   INSTANZEN-04  Während des Laufs wird auf dem Host ein Salzgruben-Platz zurückgesetzt (EnemyEncounter.ServerReset wie beim Belegen)
    ///                 → die Ring-Anzeige von A (Host-Prozess) bleibt.
    ///   INSTANZEN-03  A fällt im Ring; C wirkt „Ruhe der Asche“ auf A → abgelehnt; C belebt A über den Ring (Vorrat −1); Gegenprobe
    ///                 außerhalb des Rings: „Ruhe der Asche“ belebt.
    ///   INSTANZEN-02  Sieg, C mit vollem Rucksack öffnet die Truhe: Geld/EP einmal, Gegenstand bleibt in der Truhe; beide gehen → Arena
    ///                 bleibt belegt; C macht Platz, kommt über den Eingang zurück und holt den Gegenstand ohne zweites Geld.
    ///   STORY-03      Zweiter Lauf, Sieg; C öffnet NICHT und trennt die Verbindung → nach der Neuanmeldung Geld und Gegenstand im Stand.
    /// Jede Prüfung nennt mit „| alt: …“ das Verhalten vor Runde 6. Kein Spielcode geändert, keine neuen RPCs/Agent-Befehle.
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        private const string Mp6Rez = "spell.weaver_rest_of_ash";
        private const string Mp6Fueller = "item.broken_pocketwatch";
        private const string Mp6RingSatz = "Im Ring holt nur ein Mitstreiter Gefallene zurück.";

        private sealed class Mp6Schutz
        {
            public bool Aktiv = true, A = true, C = true;
        }

        private readonly List<(LogType typ, string text)> _mp6Logs = new List<(LogType, string)>();

        private void Mp6Log(string message, string stackTrace, LogType type)
        {
            if (message == null) return;
            if (message.Contains("[Ashveil] Server:") || type == LogType.Warning || type == LogType.Error || type == LogType.Exception)
                _mp6Logs.Add((type, message));
        }

        private IEnumerator Mp6Kolosseum(NetworkInventory a)
        {
            Line("");
            Line("[3b Runde 6: Kolosseum mit Test-Client C – Gruppe folgt (INSTANZEN-01), Salzgruben-Reset (INSTANZEN-04), Ruhe der Asche im Ring (INSTANZEN-03), Truhe bei vollem Rucksack (INSTANZEN-02), Truhe beim Trennen (STORY-03)]");
            NetworkInventory c = _mp4C;
            DungeonDirector dir = DungeonDirector.For(ArenaId), mine = DungeonDirector.For(MineId);
            DungeonPortal portal = DungeonPortal.Find(ArenaId);
            ColosseumInstanceHandler handler = dir != null ? dir.GetComponent<ColosseumInstanceHandler>() : null;
            Sp3Punkt("INSTANZEN-01", "Ausgangslage");
            if (!Mp4Voraus(a != null && c != null && OnlineInv(Mp4C) == c && dir != null && portal != null && handler != null && handler.Definition != null && mine != null,
                    "A und C angemeldet, Kolosseum (Direktor, Pforte, Ablauf) und Salzgruben in der Szene")) yield break;
            if (!Mp4Voraus(PartyService.Of(Mp4A) == null && PartyService.Of(Mp4C) == null && dir.Instances.All(i => !i.Active), "A und C in keiner Gruppe, alle Arenen frei")) yield break;
            var schutz = new Mp6Schutz();
            Health ha = a.GetComponent<Health>();
            StartCoroutine(KeepSafe(() => schutz.Aktiv, () => schutz.A ? ha : null, () => schutz.C && _mp4C != null ? _mp4C.GetComponent<Health>() : null));
            _mp6Logs.Clear();
            Application.logMessageReceived += Mp6Log;
            DungeonDirector.ResetOverride = 90f;   // genug Zeit, um über den Eingang an die Truhe zurückzukommen
            ColosseumInstanceHandler.StartDelayOverride = -1f;
            ColosseumInstanceHandler.BellCountdownOverride = 3f;
            ColosseumInstanceHandler.TimeScaleOverride = 12f;
            ColosseumInstanceHandler.SeedOverride = ArenaTestSeed;
            ColosseumInstanceHandler.WipeDelayOverride = 2f;
            try
            {
                yield return Mp6ErsterLauf(a, dir, mine, portal, handler, schutz);
                yield return Mp6ZweiterLauf(a, dir, portal, handler);
                yield return Mp6RuheDraussen(a, portal, schutz);
            }
            finally
            {
                schutz.Aktiv = false;
                Application.logMessageReceived -= Mp6Log;
                DungeonDirector.ResetOverride = -1f;
                ColosseumInstanceHandler.BellCountdownOverride = -1f;
                ColosseumInstanceHandler.TimeScaleOverride = 1f;
                ColosseumInstanceHandler.SeedOverride = -1;
                ColosseumInstanceHandler.WipeDelayOverride = -1f;
            }
            // Aufräumen: Gruppe auflösen (Teil 4 beginnt ohne Gruppe), Schutz wie zu Beginn der Suite
            if (PartyService.Of(Mp4A) != null) yield return Ask(a, "party-leave");
            if (_mp4C != null && PartyService.Of(Mp4C) != null) yield return Ask(_mp4C, "party-leave");
            yield return new WaitForSecondsRealtime(0.6f);
            ha?.SetInvulnerableFor(7200f);
            _mp4C?.GetComponent<Health>()?.SetInvulnerableFor(7200f);
            Info($"Nach Teil 3b: Gruppen A {(PartyService.Of(Mp4A) == null ? "keine" : "NOCH")}, C {(PartyService.Of(Mp4C) == null ? "keine" : "NOCH")}; aktive Arenen {dir.Instances.Count(i => i.Active)}");
        }

        // ================================================================== erster Lauf: INSTANZEN-01, -04, -03, -02

        private IEnumerator Mp6ErsterLauf(NetworkInventory a, DungeonDirector dir, DungeonDirector mine, DungeonPortal portal, ColosseumInstanceHandler handler, Mp6Schutz schutz)
        {
            NetworkInventory c = _mp4C;
            NetworkProgression ap = a.GetComponent<NetworkProgression>(), cp = c.GetComponent<NetworkProgression>();
            Health ha = a.GetComponent<Health>();
            Vector3 tor = Gnd(portal.transform.position.x, portal.transform.position.z);
            Vector3 vorTor = Gnd(portal.ReturnWorld.x, portal.ReturnWorld.z);

            // ---------------------------------------------------------- INSTANZEN-01
            Sp3Punkt("INSTANZEN-01", "A geht allein in den Ring (Instanz „solo:A“) und lädt danach C ein; C folgt durch die Pforte → dieselbe Arena, auf die Gruppe umgeschlüsselt");
            yield return Ask(c, "goto", V(vorTor + Vector3.right * 2f, portal.ReturnYaw));
            yield return Ask(a, "goto", V(vorTor, portal.ReturnYaw));
            yield return new WaitForSecondsRealtime(3.6f);   // Pforten-Sperre früherer Teile
            yield return Ask(a, "goto", V(tor, portal.ReturnYaw + 180f));
            yield return WaitUntil(() => dir.InstanceOf(Mp4A) != null, 8f);
            DungeonDirector.Instance inst = dir.InstanceOf(Mp4A);
            if (!Mp4Voraus(inst != null && inst.Key == "solo:" + Mp4A, $"A allein in Arena {inst?.Slot.Index} (Schlüssel „{inst?.Key}“)")) yield break;
            int folgt0 = _mp6Logs.Count(l => l.text.Contains("(Gruppe folgt)"));
            yield return Ask(a, "party-invite", c.CharacterName);
            yield return Ask(c, "party-respond", "1");
            yield return new WaitForSecondsRealtime(0.6f);
            if (!Mp4Voraus(PartyService.Of(Mp4A) != null && PartyService.Of(Mp4A) == PartyService.Of(Mp4C), "A (im Ring) und C bilden danach eine Gruppe")) yield break;
            yield return Ask(c, "goto", V(tor, portal.ReturnYaw + 180f));
            yield return WaitUntil(() => dir.InstanceOf(Mp4C) != null, 8f);
            int aktiv = dir.Instances.Count(i => i.Active);
            bool folgt = _mp6Logs.Count(l => l.text.Contains("(Gruppe folgt)")) > folgt0;
            Check(dir.InstanceOf(Mp4C) == inst && inst.Key != null && inst.Key.StartsWith("party:", StringComparison.Ordinal) && aktiv == 1 && folgt,
                $"C folgt in Arena {dir.InstanceOf(Mp4C)?.Slot.Index} (A in {inst.Slot.Index}), Schlüssel „{inst.Key}“, aktive Arenen {aktiv}, Log „Gruppe folgt“ {(folgt ? "ja" : "NEIN")} " +
                "| alt: C bekam eine frische Arena (zwei aktive Plätze, A und C getrennt)");
            if (dir.InstanceOf(Mp4C) != inst) yield break;

            // ---------------------------------------------------------- Glocke
            DungeonSlot slot = inst.Slot;
            ArenaBell bell = ArenaBell.All.FirstOrDefault(x => x != null && x.SlotIndex == slot.Index);
            ArenaChest chest = ArenaChest.All.FirstOrDefault(x => x != null && x.SlotIndex == slot.Index);
            ColosseumInstanceHandler.RunState s = handler.StateOf(inst);
            if (!Mp4Voraus(bell != null && chest != null && s != null && !s.Started, "Glocke, Truhe und wartender Lauf in dieser Arena")) yield break;
            yield return Ask(a, "goto", V(bell.transform.position + bell.transform.forward * 1.6f, bell.transform.eulerAngles.y + 180f));
            yield return Ask(a, "arena-bell");
            yield return WaitUntil(() => s.Started, 10f);
            if (!Mp4Voraus(s.Started && s.Participants.Count == 2, $"Lauf beginnt mit {s.Participants.Count} Teilnehmern (Seed {s.Seed}, Zeitraffer ×12)")) yield break;
            yield return new WaitForSecondsRealtime(1.5f);

            // ---------------------------------------------------------- INSTANZEN-04
            Sp3Punkt("INSTANZEN-04", "Lauf läuft; auf dem Host wird ein freier Salzgruben-Platz zurückgesetzt (EnemyEncounter.ServerReset wie beim Belegen) → A's Ring-Anzeige (Host-Prozess) bleibt");
            yield return Ask(a, "arena");
            string vor = _last.data ?? "";
            DungeonDirector.Instance freiMine = mine.Instances.FirstOrDefault(i => !i.Active && i.Slot != null);
            EnemyEncounter[] begegnungen = freiMine != null ? freiMine.Slot.GetComponentsInChildren<EnemyEncounter>(true) : new EnemyEncounter[0];
            foreach (EnemyEncounter e in begegnungen) e.ServerReset();
            if (begegnungen.Length == 0 && freiMine != null) EncounterCues.Raise(freiMine.Slot.transform.position, EncounterCues.Reset, 0f);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Ask(a, "arena");
            string nach = _last.data ?? "";
            Check(vor.Contains("visible=1") && vor.Contains("run=1") && nach.Contains("visible=1") && nach.Contains("run=1") &&
                  Mathf.Approximately(ParseAfter(vor, "revives="), ParseAfter(nach, "revives=")),
                $"A's Anzeige vorher „{vor}“ – nach dem Reset von Salzgruben-Platz {freiMine?.Slot.Index} ({begegnungen.Length} Begegnungen{(begegnungen.Length == 0 ? ", Signal direkt" : "")}) „{nach}“ " +
                "| alt: visible=0;run=0 – Uhr, Vorrat und Block mitten im Lauf gelöscht");
            yield return Ask(c, "arena");
            Info($"C (eigener Prozess, war nie betroffen): {_last.data}");

            // ---------------------------------------------------------- INSTANZEN-03
            Sp3Punkt("INSTANZEN-03", "A fällt im Ring und liegt; C (Aschenweberin) wirkt „Ruhe der Asche“ auf A → abgelehnt, der Vorrat bleibt; C belebt über den Ring");
            cp.ServerDebugLearn(Mp6Rez, 1);
            NetworkPlayer npA = a.GetComponent<NetworkPlayer>();
            // „Ruhe der Asche“ ist „nicht im Kampf“: die Wellen-Gegner (ohne Champions) verlassen dafür laufend den Ring – wie in einer
            // Ruhepause zwischen zwei Trupps; sonst lehnt schon diese Regel ab (Lauf 22.10. 19:4x: „Nicht mitten im Kampf.“) und auch
            // das Wiederbeleben über den Ring würde von Treffern auf C unterbrochen
            bool ringwache = true;
            StartCoroutine(Mp6Ringwache(() => ringwache, inst, s));
            try
            {
                Vector3 platz = Gnd(slot.transform.position.x + 6f, slot.transform.position.z - 12f);
                yield return Ask(a, "goto", V(platz, 0f));
                yield return Ask(c, "goto", V(platz + new Vector3(1.5f, 0f, 0f), 270f));
                schutz.A = false;
                yield return new WaitForSecondsRealtime(0.8f);
                UiSetze(ha, "_invulnerableUntil", -1f);
                ha.ApplyDamage(new DamageInfo { Amount = 999999f, Source = c.gameObject, WeaponId = "test" });
                yield return WaitUntil(() => !ha.IsAlive && s.Down.Contains(Mp4A), 4f);
                if (!Mp4Voraus(!ha.IsAlive && s.Down.Contains(Mp4A) && npA != null && npA.ServerHeldDown, $"A liegt im Ring (Vorrat {s.Revives})"))
                {
                    schutz.A = true;
                    yield break;
                }
                float tRuhe = Time.realtimeSinceStartup;
                yield return WaitUntil(() => !cp.ServerInCombat && cp.ServerCombatReason == "-", 15f);
                Info($"C außer Kampf nach {Time.realtimeSinceStartup - tRuhe:0.0} s ({cp.ServerCombatReason}); Abstand C–A {Flat(c.transform.position, a.transform.position):0.0} m, Uhr {ColosseumInstanceHandler.Clock(s.Elapsed)}");
                int vorrat = s.Revives;
                cp.ServerDebugResetCooldowns();
                cp.ServerSetResource(100000f);
                string why = cp.ServerCast(Mp6Rez, ha);
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Ask(a, "arena");
                Check(why == Mp6RingSatz && !ha.IsAlive && s.Down.Contains(Mp4A) && s.Revives == vorrat && (_last.data ?? "").Contains("down=1"),
                    $"„Ruhe der Asche“ auf A: „{why}“, A liegt {(!ha.IsAlive ? "weiter" : "NICHT MEHR")}, Vorrat {vorrat} → {s.Revives}, A's Anzeige „{_last.data}“ " +
                    "| alt: Wirken begann (\"\"), A stand nach 5 s an der Reserve vorbei auf, „Du liegst am Boden“ blieb stehen");
                if (why == "") cp.ServerDebugResetCooldowns();   // altes Verhalten: laufendes Wirken abbrechen
                yield return StandBy(c, a);
                yield return Ask(c, "arena-revive", a.ObjectId.ToString());
                yield return WaitUntil(() => ha.IsAlive, 9f);
                Check(ha.IsAlive && s.Revives == vorrat - 1 && !s.Down.Contains(Mp4A), $"Gegenprobe im Ring: C belebt A über den Ring (5 s) – Vorrat {vorrat} → {s.Revives}");
            }
            finally
            {
                ringwache = false;
                schutz.A = true;
            }
            yield return new WaitForSecondsRealtime(0.5f);
            if (!ha.IsAlive && npA != null) npA.ServerReviveHere(1f);   // Rot-Fall: der Lauf soll weitergehen
            ha.Heal(ha.Max);

            // ---------------------------------------------------------- Champions, Sieg mit vollem Rucksack bei C
            Sp3Punkt("INSTANZEN-02", "Sieg, C mit vollem Rucksack: Truhe zahlt Geld und EP einmal, der Gegenstand bleibt in der Truhe; Rückkehr über den Eingang holt ihn ohne zweites Geld");
            yield return WaitUntil(() => s.Bosses.Count >= 1, 50f);
            EnemyBrain boss1 = s.Bosses.FirstOrDefault();
            if (boss1 != null) boss1.GetComponent<Health>()?.ApplyDamage(new DamageInfo { Amount = 9999999f, Source = a.gameObject });
            yield return WaitUntil(() => s.Bosses.Count >= 2, 70f);
            EnemyBrain boss2 = s.Bosses.Count >= 2 ? s.Bosses[1] : null;
            if (!Mp4Voraus(boss2 != null, $"Zweiter Champion bei {ColosseumInstanceHandler.Clock(s.Elapsed)}")) yield break;
            int gefuellt = c.ServerFillFreeSlots(Mp6Fueller);
            yield return new WaitForSecondsRealtime(0.5f);
            boss2.GetComponent<Health>()?.ApplyDamage(new DamageInfo { Amount = 9999999f, Source = a.gameObject });
            yield return WaitUntil(() => s.Won, 5f);
            yield return new WaitForSecondsRealtime(1f);
            if (!Mp4Voraus(s.Won && s.ChestPending.Contains(Mp4C) && s.ChestPending.Contains(Mp4A),
                    $"Prüfung bestanden, Truhe wartet auf A und C; C's Rucksack mit {gefuellt} × Füller voll")) yield break;

            Vector3 kiste = chest.transform.position + chest.transform.forward * 1.6f;
            float kisteYaw = chest.transform.eulerAngles.y + 180f;
            yield return Ask(c, "goto", V(kiste, kisteYaw));
            yield return new WaitForSecondsRealtime(0.4f);
            long m0 = cp.ServerMoney;
            int xp0 = cp.ServerXp, belegt0 = c.ServerModel.Bag.Count(x => !x.IsEmpty);
            yield return Ask(c, "arena-chest");
            string antwort = _last.data ?? "";
            s.ChestLog.TryGetValue(Mp4C, out string logC);
            string gehalten = s.ChestHeld.TryGetValue(Mp4C, out Ashveil.Data.ItemDefinition h) && h != null ? h.Id : null;
            int geld = handler.Definition.checkpointMoney;
            Check(cp.ServerMoney - m0 == geld && logC != null && s.ChestPending.Contains(Mp4C) && gehalten != null &&
                  c.ServerModel.Bag.Count(x => !x.IsEmpty) == belegt0 && antwort.Contains("chest=1"),
                $"C öffnet mit vollem Rucksack: Geld +{cp.ServerMoney - m0} (Soll {geld}), EP {cp.ServerXp - xp0:+0;-0;0}, „{logC}“, Gegenstand in der Truhe „{gehalten ?? "KEINER"}“, " +
                $"Truhe offen für C {s.ChestPending.Contains(Mp4C)}, Anzeige „{antwort}“ | alt: Truhe leer, Gegenstand verloren (chest=0)");
            long m1 = cp.ServerMoney;
            yield return Ask(c, "arena-chest");
            Check(cp.ServerMoney == m1 && s.ChestHeld.ContainsKey(Mp4C), $"Erneut geöffnet, Rucksack noch voll: kein zweites Geld ({cp.ServerMoney - m1:+0;-0;0}), Gegenstand bleibt in der Truhe");

            long mA = ap.ServerMoney;
            yield return Ask(a, "goto", V(MineGround(slot.ExitWorld), DoorYaw(slot)));
            yield return WaitUntil(() => DungeonSlot.At(a.transform.position) == null, 8f);
            s.ChestLog.TryGetValue(Mp4A, out string logA);
            Info($"A geht ohne zu öffnen: Geld +{ap.ServerMoney - mA} ({logA})");
            yield return Ask(c, "goto", V(MineGround(slot.ExitWorld), DoorYaw(slot)));
            yield return WaitUntil(() => DungeonSlot.At(c.transform.position) == null, 8f);
            yield return new WaitForSecondsRealtime(2.5f);
            Check(inst.Active && handler.StateOf(inst) == s && s.ChestPending.Contains(Mp4C) && cp.ServerMoney == m1,
                $"Beide draußen, C's Gegenstand liegt noch in der Truhe: Arena bleibt belegt ({(inst.Active ? "ja" : "NEIN")}, Räumen nach {dir.ResetAfterEmpty:0} s), " +
                $"beim Gehen kein zweites Geld ({cp.ServerMoney - m1:+0;-0;0}) | alt: Arena sofort frei, der Gegenstand war schon verloren");
            c.ServerRemoveAll(Mp6Fueller);
            yield return new WaitForSecondsRealtime(3.6f);   // Pforten-Sperre
            yield return Ask(c, "goto", V(tor, portal.ReturnYaw + 180f));
            yield return WaitUntil(() => dir.InstanceOf(Mp4C) != null, 8f);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Ask(c, "arena");
            string zurueck = _last.data ?? "";
            Check(dir.InstanceOf(Mp4C) == inst && zurueck.Contains("chest=1"),
                $"C macht Platz und kommt über den Eingang zurück: Arena {dir.InstanceOf(Mp4C)?.Slot.Index} (dieselbe: {dir.InstanceOf(Mp4C) == inst}), Anzeige „{zurueck}“");
            int n0 = gehalten != null ? c.ServerModel.Count(gehalten) : 0;
            long m2 = cp.ServerMoney;
            yield return Ask(c, "goto", V(kiste, kisteYaw));
            yield return Ask(c, "arena-chest");
            string zuletzt = _last.data ?? "";
            Check(gehalten != null && c.ServerModel.Count(gehalten) == n0 + 1 && cp.ServerMoney == m2 && !s.ChestPending.Contains(Mp4C) && !s.ChestHeld.ContainsKey(Mp4C) && zuletzt.Contains("chest=0"),
                $"Truhe erneut geöffnet: {gehalten} {n0} → {(gehalten != null ? c.ServerModel.Count(gehalten) : 0)}, Geld {cp.ServerMoney - m2:+0;-0;0} (keine Doppelzahlung), Anzeige „{zuletzt}“");
            yield return Ask(c, "goto", V(MineGround(slot.ExitWorld), DoorYaw(slot)));
            yield return WaitUntil(() => DungeonSlot.At(c.transform.position) == null, 8f);
            yield return WaitUntil(() => !inst.Active, 5f);
            Check(!inst.Active && handler.StateOf(inst) == null, "Truhe leer, C geht → die Arena ist sofort wieder frei (wie bisher)");
            Sp3NichtGeprueft("INSTANZEN-02", "Rucksack bleibt voll und der Spieler kommt nicht zurück (Verfall nach dem Räumen, Log „Truhe nicht abgeholt“) – Restfall, offen für Max");
        }

        // ================================================================== zweiter Lauf: STORY-03

        private IEnumerator Mp6ZweiterLauf(NetworkInventory a, DungeonDirector dir, DungeonPortal portal, ColosseumInstanceHandler handler)
        {
            Sp3Punkt("STORY-03", "Zweiter Lauf, Sieg; C öffnet die Truhe NICHT und trennt die Verbindung (reconnect) → nach der Neuanmeldung Geld, EP und Gegenstand im Spielstand");
            NetworkInventory c = _mp4C;
            yield return new WaitForSecondsRealtime(3.6f);   // Pforten-Sperre
            yield return EnterRing(dir, portal, a, c);
            DungeonDirector.Instance inst = dir.InstanceOf(Mp4A);
            ColosseumInstanceHandler.RunState s = handler.StateOf(inst);
            if (!Mp4Voraus(inst != null && dir.InstanceOf(Mp4C) == inst && s != null && !s.Started, $"A und C gemeinsam in Arena {inst?.Slot.Index}, Lauf wartet")) yield break;
            ArenaBell bell = ArenaBell.All.FirstOrDefault(x => x != null && x.SlotIndex == inst.Slot.Index);
            if (bell == null) { Mp4Voraus(false, "Glocke der Arena"); yield break; }
            yield return Ask(a, "goto", V(bell.transform.position + bell.transform.forward * 1.6f, bell.transform.eulerAngles.y + 180f));
            yield return Ask(a, "arena-bell");
            yield return WaitUntil(() => s.Started, 10f);
            yield return WaitUntil(() => s.Bosses.Count >= 1, 50f);
            s.Bosses.FirstOrDefault()?.GetComponent<Health>()?.ApplyDamage(new DamageInfo { Amount = 9999999f, Source = a.gameObject });
            yield return WaitUntil(() => s.Bosses.Count >= 2, 70f);
            if (s.Bosses.Count >= 2) s.Bosses[1].GetComponent<Health>()?.ApplyDamage(new DamageInfo { Amount = 9999999f, Source = a.gameObject });
            yield return WaitUntil(() => s.Won, 5f);
            yield return new WaitForSecondsRealtime(1f);
            if (!Mp4Voraus(s.Won && s.ChestPending.Contains(Mp4C), "Zweiter Lauf gewonnen, C's Truhe wartet (ungeöffnet)")) yield break;
            NetworkProgression cp = c.GetComponent<NetworkProgression>();
            long m0 = cp.ServerMoney;
            int xp0 = cp.ServerXp, stufe0 = c.ServerModel.Level, belegt0 = c.ServerModel.Bag.Count(x => !x.IsEmpty);
            int warn0 = _mp6Logs.Count(l => l.typ != LogType.Log);
            NetworkInventory alt = c;
            yield return Ask(c, "reconnect");
            NetworkInventory neu = null;
            yield return WaitUntil(() => (neu = OnlineInv(Mp4C)) != null && neu != alt, 60f);
            if (!Mp4Voraus(neu != null && neu != alt, "C nach dem Trennen neu angemeldet")) yield break;
            _mp4C = neu;
            yield return new WaitForSecondsRealtime(1.5f);
            NetworkProgression np = neu.GetComponent<NetworkProgression>();
            s.ChestLog.TryGetValue(Mp4C, out string logC);
            int geld = handler.Definition.checkpointMoney;
            int belegt1 = neu.ServerModel.Bag.Count(x => !x.IsEmpty);
            List<string> warnungen = _mp6Logs.Where(l => l.typ != LogType.Log).Skip(warn0).Select(l => l.text).ToList();
            Check(np != null && np.ServerMoney - m0 == geld && belegt1 == belegt0 + 1 && logC != null && !s.ChestPending.Contains(Mp4C),
                $"Nach der Neuanmeldung: Geld +{(np != null ? np.ServerMoney - m0 : 0)} (Soll {geld}), Rucksack {belegt0} → {belegt1} Plätze, „{logC}“, Truhe offen für C {s.ChestPending.Contains(Mp4C)} " +
                "| alt: kein Geld, ChestLog ohne C – die Belohnung verfiel beim Trennen");
            Info($"C: Stufe {stufe0} → {neu.ServerModel.Level}, EP {xp0} → {(np != null ? np.ServerXp : 0)}; Warnungen/Fehler im Log beim Trennen: {warnungen.Count}" +
                 (warnungen.Count > 0 ? " – " + string.Join(" / ", warnungen.Take(4).Select(w => w.Length > 160 ? w.Substring(0, 160) + "…" : w)) : ""));
            neu.GetComponent<Health>()?.SetInvulnerableFor(7200f);
            yield return Ask(a, "goto", V(MineGround(inst.Slot.ExitWorld), DoorYaw(inst.Slot)));
            yield return WaitUntil(() => DungeonSlot.At(a.transform.position) == null, 8f);
            yield return WaitUntil(() => !inst.Active, 5f);
            Check(!inst.Active, "A geht – die Arena ist sofort frei (C's Anteil ist ausgezahlt, nichts wartet mehr)");
            Sp3NichtGeprueft("STORY-03", "Trennen mit vollem Rucksack (Gegenstand bleibt in der Truhe) und Beenden des Hosts (OnApplicationQuit) – nicht gefahren");
        }

        /// <summary>Wellen-Gegner (keine Champions) laufend aus dem Ring nehmen, solange <paramref name="aktiv"/> gilt.</summary>
        private static IEnumerator Mp6Ringwache(Func<bool> aktiv, DungeonDirector.Instance inst, ColosseumInstanceHandler.RunState s)
        {
            while (aktiv())
            {
                foreach (EnemyBrain e in inst.Enemies.Where(e => e != null && !s.Bosses.Contains(e) && e.GetComponent<Health>() is Health eh && eh != null && eh.IsAlive).ToList())
                    EnemyDirector.Instance?.Remove(e);
                yield return new WaitForSecondsRealtime(0.1f);
            }
        }

        // ================================================================== INSTANZEN-03 Gegenprobe außerhalb des Rings

        private IEnumerator Mp6RuheDraussen(NetworkInventory a, DungeonPortal portal, Mp6Schutz schutz)
        {
            Sp3Punkt("INSTANZEN-03", "Gegenprobe außerhalb des Rings: „Ruhe der Asche“ belebt A wieder (die Ablehnung gilt nur im Ring)");
            NetworkInventory c = _mp4C;
            NetworkProgression cp = c != null ? c.GetComponent<NetworkProgression>() : null;
            Health ha = a.GetComponent<Health>();
            NetworkPlayer npA = a.GetComponent<NetworkPlayer>();
            if (!Mp4Voraus(cp != null && ha != null && npA != null, "C mit Fortschritt, A mit Leben")) yield break;
            // Lauf 22.10. 19:4x: neben der Pforte stand eine Figur im Eingang („Außer Reichweite.“) – jetzt 25 m neben dem Wachtfeuer
            // (fern der Pforte, nicht am Wiederbelebungspunkt selbst: so ist ein Aufstehen am Fleck vom Erwachen am Spawn unterscheidbar)
            GameObject spawn = GameObject.Find("PlayerSpawn");
            Vector3 lager = spawn != null ? spawn.transform.position : Vector3.zero;
            Vector3 stand = Gnd(lager.x + 18f, lager.z + 18f);
            if (NavMesh.SamplePosition(stand, out NavMeshHit nh, 6f, NavMesh.AllAreas)) stand = nh.position + Vector3.up * 0.3f;
            yield return Ask(a, "goto", V(stand, 0f));
            yield return Ask(c, "goto", V(stand + new Vector3(2f, 0f, 0f), 270f));
            yield return new WaitForSecondsRealtime(1f);
            Info($"Draußen: A bei {a.transform.position.x:0}/{a.transform.position.z:0} ({Flat(a.transform.position, lager):0} m vom Wachtfeuer), C {Flat(c.transform.position, a.transform.position):0.0} m daneben, " +
                 $"Arena-Platz A {(DungeonSlot.At(a.transform.position) != null ? "JA" : "nein")}, C {(DungeonSlot.At(c.transform.position) != null ? "JA" : "nein")}");
            cp.ServerDebugLearn(Mp6Rez, 1);
            // Kein Festhalten über HoldRespawn: ServerHeldDown stünde dann auch draußen – das Liegenbleiben übernimmt wie im Spiel
            // das Zeitfenster der nahen Aschenweberin (NetworkPlayer.ResurrectWindow)
            schutz.A = false;
            string why = "?";
            bool gefallen = false, steht = false;
            Vector3 todOrt = a.transform.position;
            float amFleck = -1f;
            try
            {
                yield return new WaitForSecondsRealtime(0.8f);
                UiSetze(ha, "_invulnerableUntil", -1f);
                ha.ApplyDamage(new DamageInfo { Amount = 999999f, Source = c.gameObject, WeaponId = "test" });
                yield return WaitUntil(() => !ha.IsAlive, 3f);
                gefallen = !ha.IsAlive;
                todOrt = a.transform.position;
                yield return WaitUntil(() => !cp.ServerInCombat && cp.ServerCombatReason == "-", 8f);
                cp.ServerDebugResetCooldowns();
                cp.ServerSetResource(100000f);
                why = cp.ServerCast(Mp6Rez, ha);
                yield return WaitUntil(() => ha.IsAlive, 8f);
                steht = ha.IsAlive;
                amFleck = Flat(a.transform.position, todOrt);
            }
            finally
            {
                if (!ha.IsAlive) npA.ServerReviveHere(1f);
                schutz.A = true;
                ha.SetInvulnerableFor(7200f);
            }
            Check(gefallen && why == "" && steht && amFleck >= 0f && amFleck < 3f && DungeonSlot.At(a.transform.position) == null,
                $"Draußen: A gefallen {gefallen}, „Ruhe der Asche“ ({(why == "" ? "gewirkt" : why)}) → A steht wieder ({(steht ? "ja" : "NEIN")}) {amFleck:0.0} m vom Sterbeort (nicht am Wachtfeuer erwacht)");
        }
    }
}
