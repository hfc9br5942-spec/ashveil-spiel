using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Quests;
using Ashveil.Gameplay.Weapons;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// P1 Klassen spielbar (Max 30.09.): Ressourcen (Wut, Energie, Mana), Klassenfaehigkeiten mit Raengen, globale
    /// Abklingzeit, Wirken im Stand, Bedrohung/Spott, Klassenlehrer gegen Geld, Talente ab Stufe 10, Speicherstand v10.
    /// Editor-Host Maxmod (Eidwaechter) + Test-Client Krisbot (nacheinander Aschenjaegerin und Aschenweberin).
    /// Start: "Ashveil › Test › Klassen und Fähigkeiten (2 Spieler)". Bericht: Logs/Ashveil/Ashveil_KlassenTest.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string ClassesFlag = "klassen";
        private static bool _classesMode;

        private const string HS = "spell.warden_heroic_strike", Charge = "spell.warden_charge", Thunder = "spell.warden_thunder_clap",
            Bash = "spell.warden_shield_bash", Taunt = "spell.warden_taunt", Fortify = "spell.warden_fortify";
        private const string Feint = "spell.stalker_feint", Clip = "spell.stalker_wing_clip", FireArrow = "spell.stalker_fire_arrow",
            Disengage = "spell.stalker_disengage", Multi = "spell.stalker_multi_shot";
        private const string Bolt = "spell.weaver_ember_bolt", Heal = "spell.weaver_heal", AshShield = "spell.weaver_ash_shield",
            Flash = "spell.weaver_flash_heal", Wave = "spell.weaver_flame_wave", Rez = "spell.weaver_rest_of_ash";

        private readonly List<EnemyBrain> _classFoes = new List<EnemyBrain>();

        private IEnumerator ClassesSuite()
        {
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(ArenaProfiles[0])) != null, 30f);
            Check(a != null, "Spieler A (Editor-Host) angemeldet");
            if (a == null) yield break;

            // ---------------------------------------------------------- Daten
            Line("");
            Line("[Daten]");
            var spells = GameData.Database.All.OfType<SpellDefinition>().ToList();
            var talents = GameData.Database.All.OfType<TalentDefinition>().ToList();
            CharacterClassDefinition warden = GameData.Class("class.warden"), stalker = GameData.Class("class.ashstalker"), weaver = GameData.Class("class.emberweaver");
            int ws = spells.Count(s => s.classId == warden.Id), ss = spells.Count(s => s.classId == stalker.Id), es = spells.Count(s => s.classId == weaver.Id);
            Check(ws == 6 && ss == 5 && es == 6, $"Klassenfähigkeiten: Eidwächter {ws}, Aschenjäger {ss}, Aschenweber {es}");
            Check(spells.All(s => s.RankCount > 0 && s.ranks.All(r => r.level >= 1 && r.level <= 20)), "Jede Fähigkeit hat Ränge bis höchstens Stufe 20");
            Check(talents.Count == 45 && new[] { warden, stalker, weaver }.All(c => talents.Count(t => t.classId == c.Id) == 15 && c.talentTrees != null && c.talentTrees.Length == 3),
                $"Talente: {talents.Count} (je Klasse 3 Bäume à 5)");
            Check(warden.resource == ResourceType.Rage && stalker.resource == ResourceType.Energy && weaver.resource == ResourceType.Mana,
                "Ressourcen wie WoW: Wut, Energie, Mana");
            NpcDefinition ilse = GameData.Npc(QuestIds.Ilse), gerrit = GameData.Npc(QuestIds.Gerrit), maelis = GameData.Npc(QuestIds.Maelis);
            Check(ilse != null && ilse.trainerClass == warden.Id && gerrit != null && gerrit.trainerClass == stalker.Id && maelis != null && maelis.trainerClass == weaver.Id,
                "Klassenlehrer im Startlager: Ilse (Eidwächter), Gerrit (Aschenjäger), Maelis (Aschenweber)");
            Check(TalentDefinition.PointsForLevel(9) == 0 && TalentDefinition.PointsForLevel(10) == 1 && TalentDefinition.PointsForLevel(20) == 11,
                "Talentpunkte: ab Stufe 10 einer je Stufe (Stufe 20 = 11)");

            string exe = Ashveil.Core.ProjectPaths.TestClientExe;
            if (!File.Exists(exe)) { Check(false, "Test-Client vorhanden"); yield break; }
            StartClient(exe, ArenaProfiles[1]);
            NetworkInventory b = null;
            yield return WaitUntil(() => (b = OnlineInv(ArenaProfiles[1])) != null, 150f);
            Check(b != null, "Spieler B (Test-Client) angemeldet");
            if (b == null) yield break;
            yield return new WaitForSecondsRealtime(2f);

            var ap = a.GetComponent<NetworkProgression>();
            var bp = b.GetComponent<NetworkProgression>();
            Health ah = a.GetComponent<Health>(), bh = b.GetComponent<Health>();
            NetworkMotion an = a.GetComponent<NetworkMotion>(), bn = b.GetComponent<NetworkMotion>();
            Vector3 start = Gnd(470f, 110f);

            // ---------------------------------------------------------- Eidwaechter
            Line("");
            Line("[Eidwächter: Wut]");
            ap.ServerDebugSwitchClass(warden, true);
            a.ServerSetLevel(20);
            ap.ServerSetMoney(100000);
            yield return new WaitForSecondsRealtime(0.5f);
            Check(ap.ServerSpellRank(HS) == 1, "Heldenhafter Schlag (Rang 1) ist von Anfang an bekannt");
            Check(ap.ServerResourceType == ResourceType.Rage && Mathf.Approximately(ap.ServerResourceMax, 100f), $"Wut 0–100 (jetzt {ap.ServerResource:0})");
            foreach (string id in new[] { HS, Charge, Thunder, Bash, Taunt, Fortify }) ap.ServerDebugLearn(id, 9);
            ap.ServerSetResource(0f);
            TrainingDummy dummy = FindObjectsByType<TrainingDummy>(FindObjectsSortMode.None).FirstOrDefault();
            Health dh = dummy != null ? dummy.GetComponent<Health>() : null;
            if (dh != null) dh.ApplyDamage(new DamageInfo { Amount = 40f, Source = a.gameObject, Direction = Vector3.forward, WeaponId = "test" });
            float rDealt = ap.ServerResource;
            ah.ApplyDamage(new DamageInfo { Amount = Mathf.Round(ah.Max * 0.2f), Direction = a.transform.forward * -1f, Dodgeable = false });
            float rTaken = ap.ServerResource - rDealt;
            Check(rDealt > 0.5f && rDealt < 20f, $"Wut durch ausgeteilten Schaden (40 Schaden auf Stufe 20: +{rDealt:0.0})");
            Check(rTaken > 7f && rTaken < 13f, $"Wut durch erlittenen Schaden (ein Fünftel des Lebens: +{rTaken:0.0})");
            ah.Heal(ah.Max);

            // Gegner zum Üben
            an.ServerTeleport(start, 0f);
            bn.ServerTeleport(start + new Vector3(4f, 0f, 0f), 0f);
            yield return new WaitForSecondsRealtime(2f);
            EnemyBrain brute = SpawnFoe(start + new Vector3(0f, 0f, 16f), 180f);
            Check(brute != null, "Übungsgegner (Grubenschrat) erzeugt");
            if (brute == null) { Cleanup(); yield break; }
            Health fh = brute.GetComponent<Health>();
            yield return new WaitForSecondsRealtime(1.5f);

            Line("");
            Line("[Eidwächter: Ansturm, Donnerschlag, Schildhieb, Spott, Rüstung stärken, Heldenhafter Schlag]");
            yield return new WaitForSecondsRealtime(2.5f); // Ansturm: 5 s ohne Treffer
            ap.ServerSetResource(0f);
            float d0 = Flat(a.transform.position, brute.transform.position);
            string why = ap.ServerCast(Charge, fh);
            yield return new WaitForSecondsRealtime(1.2f);
            float d1 = Flat(a.transform.position, brute.transform.position);
            Check(why == "" && d1 < d0 - 6f && d1 < 5f, $"Ansturm: {d0:0.0} m → {d1:0.0} m ({(why == "" ? "angenommen" : why)})");
            Check(ap.ServerResource >= 14f, $"Ansturm erzeugt Wut ({ap.ServerResource:0})");
            ap.ServerSetResource(100f);
            ap.ServerDebugResetCooldowns();
            float fh0 = fh.Current;
            why = ap.ServerCast(Thunder, null);
            Check(why == "" && fh.Current < fh0 && brute.SlowFactor < 0.9f, $"Donnerschlag: {fh0 - fh.Current:0} Schaden, verlangsamt (×{brute.SlowFactor:0.00}) – {ap.ServerLastSpellResult}");
            Check(brute.ThreatOf(ah) > 0f, $"Bedrohung beim Gegner ({brute.ThreatOf(ah):0})");
            why = ap.ServerCast(Bash, fh);
            Check(why.Contains("Noch nicht bereit"), $"Globale Abklingzeit 1,5 s: sofort danach abgelehnt („{why}“)");
            yield return new WaitForSecondsRealtime(1.6f);
            why = ap.ServerCast(Bash, fh);
            Check(why == "" && ap.ServerLastSpellResult.Contains("dmg=") && brute.IsStunned, $"Schildhieb mit Schild: Treffer und Betäubung ({(why == "" ? ap.ServerLastSpellResult : why)})");
            ap.ServerSetResource(3f);
            yield return new WaitForSecondsRealtime(1.6f);
            ap.ServerDebugResetCooldowns();
            why = ap.ServerCast(Thunder, null);
            Check(why.Contains("Nicht genug Wut"), $"Zu wenig Wut: abgelehnt („{why}“)");

            // Spott: der Gegner greift B an, A spottet
            fh.Heal(fh.Max);
            brute.ServerAddThreat(bh, 10000f);
            brute.ServerEngage(bh);
            yield return new WaitForSecondsRealtime(0.6f);
            why = ap.ServerCast(Taunt, fh);
            yield return new WaitForSecondsRealtime(0.3f);
            Check(why == "" && brute.IsTaunted && brute.CurrentTarget == ah && brute.ThreatOf(ah) >= brute.ThreatOf(bh),
                $"Spott: der Grubenschrat greift jetzt Maxmod an (Bedrohung A {brute.ThreatOf(ah):0} ≥ B {brute.ThreatOf(bh):0})");

            ap.ServerSetResource(100f);
            yield return new WaitForSecondsRealtime(1.6f);
            float before = ah.Current;
            ah.ApplyDamage(new DamageInfo { Amount = 100f, Direction = -a.transform.forward, Unblockable = true });
            float plain = before - ah.Current;
            ah.Heal(ah.Max);
            why = ap.ServerCast(Fortify, null);
            before = ah.Current;
            ah.ApplyDamage(new DamageInfo { Amount = 100f, Direction = -a.transform.forward, Unblockable = true });
            float reduced = before - ah.Current;
            Check(why == "" && reduced < plain * 0.8f, $"Rüstung stärken: {plain:0} → {reduced:0} Schaden");
            ah.Heal(ah.Max);

            ap.ServerSetResource(50f);
            why = ap.ServerCast(HS, fh);
            Check(why == "" && ap.ServerQueuedNextSwing == HS, "Heldenhafter Schlag wartet auf den nächsten Schlag (kein GCD)");
            float bonus = ap.ServerConsumeNextSwing(out string used);
            Check(used == HS && Mathf.Approximately(bonus, 32f) && Mathf.Approximately(ap.ServerResource, 35f), $"Nächster Schlag: +{bonus:0} Schaden (Rang 3), 15 Wut verbraucht ({ap.ServerResource:0} übrig)");

            // ---------------------------------------------------------- Aschenjaegerin (B)
            Line("");
            Line("[Aschenjägerin: Energie, Leiste, Finte, Zurechtstutzen, Brandpfeil, Mehrfachschuss, Rückzug]");
            CharacterSaveData kit = CharacterStore.CreateNew("klassen-jaeger", stalker);
            bp.ServerDebugSwitchClass(stalker);
            b.ServerModel.Load(kit.bag, kit.equipment, kit.containers, kit.ammoId);
            b.ServerSetLevel(20);
            b.ServerCommit();
            yield return new WaitForSecondsRealtime(1.5f);
            foreach (string id in new[] { Feint, Clip, FireArrow, Disengage, Multi }) bp.ServerDebugLearn(id, 9);
            yield return new WaitForSecondsRealtime(1f);
            yield return Ask(b, "spell-state");
            Check(_last.ok && _last.data.Contains("res=Energy") && _last.data.Contains("max=100") && _last.data.Contains(Feint + ":3") && _last.data.Contains(Multi + ":2"),
                $"Client kennt Energie und die gelernten Ränge ({Cut(_last.data)})");
            bool onBar = b.ServerData.actionBar.Any(e => e.type == ActionEntryType.Spell && e.referenceId == Feint) &&
                         b.ServerData.actionBar.Any(e => e.type == ActionEntryType.Spell && e.referenceId == Multi);
            Check(onBar, "Neu gelernte Fähigkeiten liegen auf der Leiste");
            Check(!b.ServerData.actionBar.Any(e => e.referenceId == ActionBarLayout.Dodge) || stalker.canRoll, "Leiste passt zur Klasse");

            fh.Heal(fh.Max);
            bn.ServerTeleport(brute.transform.position - brute.transform.forward * -2.2f, 0f);
            yield return new WaitForSecondsRealtime(0.8f);
            FaceTo(bn, b, brute.transform.position);
            yield return new WaitForSecondsRealtime(0.8f);
            var bnob = brute.GetComponent<FishNet.Object.NetworkObject>();
            yield return Ask(b, "spell-target", bnob.ObjectId.ToString());
            bp.ServerSetResource(100f);
            fh0 = fh.Current;
            int slot = BarSlotOf(b, Feint);
            yield return Ask(b, "slot-press", slot.ToString());
            yield return new WaitForSecondsRealtime(0.6f);
            Check(fh.Current < fh0 && bp.ServerResource < 80f, $"Finte über die Leiste (Taste {slot + 1}): {fh0 - fh.Current:0} Schaden, Energie {bp.ServerResource:0}");
            float e0 = bp.ServerResource;
            yield return new WaitForSecondsRealtime(2f);
            Check(bp.ServerResource - e0 > 15f, $"Energie füllt sich (10/s): {e0:0} → {bp.ServerResource:0}");
            bp.ServerSetResource(100f);
            bp.ServerDebugResetCooldowns();
            why = bp.ServerCast(Clip, fh);
            Check(why == "" && brute.SlowFactor <= 0.55f, $"Zurechtstutzen verlangsamt (×{brute.SlowFactor:0.00}) {why}");

            // Fernkampf: Abstand nehmen
            bn.ServerTeleport(brute.transform.position + Vector3.back * 14f, 0f);
            brute.ServerStun(20f);
            yield return new WaitForSecondsRealtime(0.8f);
            FaceTo(bn, b, brute.transform.position);
            yield return new WaitForSecondsRealtime(0.8f);
            EnemyBrain f2 = SpawnFoe(brute.transform.position + Vector3.right * 3f, 180f), f3 = SpawnFoe(brute.transform.position + Vector3.left * 3f, 180f);
            yield return new WaitForSecondsRealtime(1.2f);
            if (f2 != null) f2.ServerStun(20f);
            if (f3 != null) f3.ServerStun(20f);
            bp.ServerSetResource(100f);
            bp.ServerDebugResetCooldowns();
            int arrows0 = b.ServerModel.AmmoCount;
            fh0 = fh.Current;
            why = bp.ServerCast(FireArrow, fh);
            yield return new WaitForSecondsRealtime(1.2f);
            Check(why == "" && fh.Current < fh0 && b.ServerModel.AmmoCount == arrows0 - 1 && DamageOverTime.IsPoisoned(fh),
                $"Brandpfeil: Treffer {fh0 - fh.Current:0}, ein Pfeil verbraucht, das Ziel brennt ({(why == "" ? bp.ServerLastSpellResult : why)})");
            bp.ServerDebugResetCooldowns();
            bp.ServerSetResource(100f);
            float h2 = f2 != null ? f2.GetComponent<Health>().Current : 0f, h3 = f3 != null ? f3.GetComponent<Health>().Current : 0f;
            fh0 = fh.Current;
            why = bp.ServerCast(Multi, fh);
            yield return new WaitForSecondsRealtime(1.2f);
            int hits = (fh.Current < fh0 ? 1 : 0) + (f2 != null && f2.GetComponent<Health>().Current < h2 ? 1 : 0) + (f3 != null && f3.GetComponent<Health>().Current < h3 ? 1 : 0);
            Check(why == "" && hits == 3, $"Mehrfachschuss trifft drei Gegner ({hits}; {bp.ServerLastSpellResult})");
            why = bp.ServerCast(Multi, fh);
            Check(why.Contains("Noch nicht bereit"), $"Eigene Abklingzeit (10 s): „{why}“");
            Vector3 p0 = b.transform.position;
            FaceTo(bn, b, brute.transform.position);
            yield return new WaitForSecondsRealtime(0.8f);
            p0 = b.transform.position;
            why = bp.ServerCast(Disengage, null);
            yield return new WaitForSecondsRealtime(1.2f);
            float leap = Flat(p0, b.transform.position);
            Check(why == "" && leap > 5f && Flat(b.transform.position, brute.transform.position) > Flat(p0, brute.transform.position), $"Rückzug: {leap:0.0} m nach hinten");
            yield return Ask(b, "pos");
            ParsePos(_last.data, out Vector3 cpos, out _, out _);
            Check(Flat(cpos, b.transform.position) < 1.5f, $"Client und Server stehen nach dem Sprung am selben Ort ({Flat(cpos, b.transform.position):0.00} m)");

            // ---------------------------------------------------------- Aschenweberin (B)
            Line("");
            Line("[Aschenweberin: Mana, Wirken im Stand, Glutbolzen, Heilung, Aschenschild, Flammenwelle, Ruhe der Asche]");
            CharacterSaveData wkit = CharacterStore.CreateNew("klassen-weber", weaver);
            bp.ServerDebugSwitchClass(weaver);
            b.ServerModel.Load(wkit.bag, wkit.equipment, wkit.containers, wkit.ammoId);
            b.ServerSetLevel(20);
            b.ServerCommit();
            yield return new WaitForSecondsRealtime(1.5f);
            foreach (string id in new[] { Bolt, Heal, AshShield, Flash, Wave, Rez }) bp.ServerDebugLearn(id, 9);
            yield return new WaitForSecondsRealtime(0.8f);
            float manaMax = bp.ServerResourceMax;
            Check(bp.ServerResourceType == ResourceType.Mana && manaMax >= 110f + 20f * 19f && Mathf.Approximately(bp.ServerResource, manaMax), $"Mana {bp.ServerResource:0}/{manaMax:0}");
            bn.ServerTeleport(brute.transform.position + Vector3.back * 16f, 0f);
            yield return new WaitForSecondsRealtime(0.8f);
            FaceTo(bn, b, brute.transform.position);
            yield return new WaitForSecondsRealtime(0.8f);
            int casts0 = bp.ServerSpellsCast;
            why = bp.ServerCast(Bolt, fh);
            yield return new WaitForSecondsRealtime(0.5f);
            Check(why == "" && bp.ServerIsCasting, "Glutbolzen: Wirkzeit läuft");
            bn.ServerTeleport(b.transform.position + Vector3.right * 1.5f, b.transform.eulerAngles.y);
            yield return new WaitForSecondsRealtime(2.2f);
            Check(!bp.ServerIsCasting && bp.ServerSpellsCast == casts0, "Bewegung bricht das Wirken ab (kein Glutbolzen)");
            FaceTo(bn, b, brute.transform.position);
            yield return new WaitForSecondsRealtime(0.6f);
            fh0 = fh.Current;
            yield return new WaitForSecondsRealtime(1.4f);
            float m0 = bp.ServerResource;
            why = bp.ServerCast(Bolt, fh);
            yield return new WaitForSecondsRealtime(3.4f);
            Check(why == "" && fh.Current < fh0 && bp.ServerResource < m0, $"Glutbolzen im Stand: {fh0 - fh.Current:0} Feuerschaden, {m0 - bp.ServerResource:0} Mana");
            float m1 = bp.ServerResource;
            yield return new WaitForSecondsRealtime(2.5f);
            float m2 = bp.ServerResource;
            yield return new WaitForSecondsRealtime(3.5f);
            float m3 = bp.ServerResource;
            Check(Mathf.Abs(m2 - m1) < 0.5f && m3 > m2 + 3f, $"Fünf-Sekunden-Regel: erst keine Erholung ({m1:0} → {m2:0}), danach wieder ({m3:0})");

            // Heilung und Schild auf den Eidwaechter
            an.ServerTeleport(b.transform.position + Vector3.right * 6f, 0f);
            yield return new WaitForSecondsRealtime(1f);
            ah.ApplyDamage(new DamageInfo { Amount = Mathf.Round(ah.Max * 0.5f), Direction = -a.transform.forward, Unblockable = true });
            float ha = ah.Current;
            why = bp.ServerCast(Heal, ah);
            yield return new WaitForSecondsRealtime(2.9f);
            Check(why == "" && ah.Current > ha + 100f, $"Heilung auf Maxmod: {ha:0} → {ah.Current:0}");
            why = bp.ServerCast(Heal, fh);
            yield return new WaitForSecondsRealtime(2.9f);
            Check(bh.Current >= bh.Max - 1f, "Heilung auf einen Gegner: wirkt stattdessen auf sich selbst (WoW)");
            ah.Heal(ah.Max);
            yield return new WaitForSecondsRealtime(1.6f);
            why = bp.ServerCast(AshShield, ah);
            before = ah.Current;
            ah.ApplyDamage(new DamageInfo { Amount = 60f, Direction = -a.transform.forward, Unblockable = true });
            Check(why == "" && Mathf.Approximately(ah.Current, before), $"Aschenschild fängt den Treffer auf ({before - ah.Current:0} Schaden kommt durch)");
            bp.ServerDebugResetCooldowns();
            bp.ServerSetResource(1000f);
            why = bp.ServerCast(AshShield, ah);
            Check(why.Contains("Schild"), $"Geschwächte Seele: kein zweiter Schild sofort („{why}“)");

            bn.ServerTeleport(brute.transform.position + Vector3.back * 4f, 0f);
            yield return new WaitForSecondsRealtime(1f);
            bp.ServerDebugResetCooldowns();
            bp.ServerSetResource(1000f);
            h2 = f2 != null ? f2.GetComponent<Health>().Current : 0f;
            fh0 = fh.Current;
            why = bp.ServerCast(Wave, null);
            Check(why == "" && fh.Current < fh0 && (f2 == null || f2.GetComponent<Health>().Current < h2), $"Flammenwelle trifft alle im Umkreis ({bp.ServerLastSpellResult})");

            // Ruhe der Asche
            Cleanup();
            yield return new WaitForSecondsRealtime(6f);
            an.ServerTeleport(b.transform.position + Vector3.right * 5f, 0f);
            yield return new WaitForSecondsRealtime(1f);
            ah.ApplyDamage(new DamageInfo { Amount = ah.Max * 5f, Direction = -a.transform.forward, Unblockable = true });
            yield return new WaitForSecondsRealtime(7.5f);
            Check(!ah.IsAlive, "Maxmod bleibt liegen, solange eine Aschenweberin in der Nähe ist (sonst nach 6 s am Wachtfeuer)");
            why = bp.ServerCast(Rez, ah);
            yield return new WaitForSecondsRealtime(5.6f);
            Check(why == "" && ah.IsAlive && ah.Current <= ah.Max * 0.4f, $"Ruhe der Asche: Maxmod steht wieder auf ({ah.Current:0}/{ah.Max:0}) {why}");
            ah.Heal(ah.Max);

            // ---------------------------------------------------------- Lehrer und Talente (Client B)
            Line("");
            Line("[Klassenlehrer und Talente]");
            bp.ServerDebugForgetSpells();
            b.ServerSetLevel(20);
            bp.ServerSetMoney(5000);
            yield return new WaitForSecondsRealtime(0.6f);
            NpcIdentity mid = NpcIdentity.Find(QuestIds.Maelis), iid = NpcIdentity.Find(QuestIds.Ilse);
            Check(mid != null && iid != null, "Maelis und Ilse stehen im Lager");
            if (mid != null)
            {
                bn.ServerTeleport(mid.transform.position + mid.transform.forward * 2f, mid.transform.eulerAngles.y + 180f);
                yield return new WaitForSecondsRealtime(1.2f);
                yield return Ask(b, "npc-talk", QuestIds.Maelis);
                yield return Ask(b, "ui-open", "trainer " + QuestIds.Maelis);
                Check(_last.data.Contains("trainer=1"), "Knopf „Ausbildung“ öffnet das Lehrer-Fenster");
                yield return Ask(b, "train", QuestIds.Maelis + " " + AshShield + " 1");
                Check(_last.ok && bp.ServerSpellRank(AshShield) == 1 && bp.ServerMoney == 5000 - 50, $"Aschenschild (Rang 1) für 50 Kupfer gelernt ({Cut(_last.data)})");
                yield return Ask(b, "train", QuestIds.Maelis + " " + AshShield + " 3");
                Check(!_last.ok && _last.data.Contains("Rang davor"), $"Ränge der Reihe nach ({Tail(_last.data)})");
                yield return Ask(b, "train", QuestIds.Maelis + " " + Bolt + " 2");
                Check(_last.ok && bp.ServerSpellRank(Bolt) == 2 && b.ServerData.actionBar.Any(e => e.referenceId == AshShield), "Glutbolzen Rang 2 gelernt, Aschenschild liegt auf der Leiste");
                bp.ServerSetMoney(10);
                yield return Ask(b, "train", QuestIds.Maelis + " " + Flash + " 1");
                Check(!_last.ok && _last.data.Contains("Geld"), $"Zu wenig Geld ({Tail(_last.data)})");
                bp.ServerSetMoney(5000);
                b.ServerSetLevel(5);
                yield return new WaitForSecondsRealtime(0.3f);
                yield return Ask(b, "train", QuestIds.Maelis + " " + Wave + " 1");
                Check(!_last.ok && _last.data.Contains("Stufe 12"), $"Zu niedrige Stufe ({Tail(_last.data)})");
                b.ServerSetLevel(20);
                yield return Ask(b, "ui-open", "trainer 0");
            }
            if (iid != null)
            {
                bn.ServerTeleport(iid.transform.position + iid.transform.forward * 2f, iid.transform.eulerAngles.y + 180f);
                yield return new WaitForSecondsRealtime(1.2f);
                yield return Ask(b, "npc-talk", QuestIds.Ilse);
                yield return Ask(b, "train", QuestIds.Ilse + " " + Bolt + " 3");
                Check(!_last.ok && _last.data.Contains("bildet nur"), $"Ilse bildet nur Eidwächter aus ({Tail(_last.data)})");
            }

            yield return new WaitForSecondsRealtime(0.5f);
            yield return Ask(b, "spell-state");
            Check(_last.data.Contains("free=11") && _last.data.Contains("spent=0"), $"Stufe 20: 11 Talentpunkte frei ({Cut(_last.data)})");
            float maxBefore = bp.ServerResourceMax;
            for (int i = 0; i < 5; i++) yield return Ask(b, "talent", "talent.weaver_wellspring");
            Check(bp.ServerTalentRank("talent.weaver_wellspring") == 5 && bp.ServerResourceMax > maxBefore * 1.08f,
                $"Tiefe Quelle 5/5: Mana {maxBefore:0} → {bp.ServerResourceMax:0}");
            yield return Ask(b, "talent", "talent.weaver_wellspring");
            Check(!_last.ok && _last.data.Contains("voll"), $"Mehr als der Höchstrang geht nicht ({Tail(_last.data)})");
            yield return Ask(b, "talent", "talent.weaver_serenity");
            Check(!_last.ok && _last.data.Contains("10 Punkte"), $"Reihe 3 braucht 10 Punkte im Baum ({Tail(_last.data)})");
            yield return Ask(b, "talent", "talent.weaver_cinder_skin");
            Check(_last.ok && bp.ServerTalentRank("talent.weaver_cinder_skin") == 1, "Reihe 2 nach 5 Punkten im Baum");
            yield return Ask(b, "ui-open", "talents");
            Check(_last.data.Contains("talents=1") && _last.data.Contains("cells=15"), $"Talentfenster (N) mit 15 Talenten ({_last.data})");
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Ask(b, "ui-open", "talents 0");

            // Speicherstand
            b.SaveNow();
            yield return new WaitForSecondsRealtime(0.5f);
            bool loaded = CharacterStore.Service.TryLoad(ArenaProfiles[1], out CharacterSaveData saved);
            Check(loaded && saved.version == CharacterSaveData.CurrentVersion && saved.version >= 10 && saved.spells.Any(s => s.id == Bolt && s.rank == 2) &&
                  saved.talents.Any(t => t.id == "talent.weaver_wellspring" && t.rank == 5),
                $"Speicherstand v{(loaded ? saved.version : 0)}: Ränge und Talente gespeichert");

            if (mid != null)
            {
                bn.ServerTeleport(mid.transform.position + mid.transform.forward * 2f, mid.transform.eulerAngles.y + 180f);
                yield return new WaitForSecondsRealtime(1.2f);
                yield return Ask(b, "npc-talk", QuestIds.Maelis);
                long money0 = bp.ServerMoney;
                yield return Ask(b, "talent-reset", QuestIds.Maelis);
                Check(_last.ok && bp.ServerTalentPointsSpent() == 0 && bp.ServerMoney == money0 - 100, $"Talente beim Lehrer zurückgesetzt (1 Silber; {Tail(_last.data)})");
            }

            // Ausgangslage fuer andere Tests
            bp.ServerDebugSwitchClass(warden);
            CharacterSaveData back = CharacterStore.CreateNew("klassen-zurueck", warden);
            b.ServerModel.Load(back.bag, back.equipment, back.containers, back.ammoId);
            b.ServerCommit();
            b.SaveNow();
            Cleanup();
        }

        // ---------------------------------------------------------- Hilfen

        private static class QuestIds
        {
            public const string Ilse = "npc.ilse_harrow", Gerrit = "npc.gerrit", Maelis = "npc.maelis";
        }

        private EnemyBrain SpawnFoe(Vector3 pos, float yaw)
        {
            EnemySpawnArea area = FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None)
                .FirstOrDefault(x => x.Definition != null && x.Definition.Id == "enemy.salt_brute" && x.Prefab != null);
            if (area == null || EnemyDirector.Instance == null) return null;
            EnemyBrain e = EnemyDirector.Instance.SpawnAt(area.Prefab, pos, yaw, 13, "", br => br.SetAggroProfile(AggroProfile.Defensive), 5f);
            if (e != null) { _classFoes.Add(e); e.ServerStun(600f); } // steht still: Werte pruefbar, niemand wird verletzt
            return e;
        }

        private void Cleanup()
        {
            foreach (EnemyBrain e in _classFoes) if (e != null) EnemyDirector.Instance.Remove(e);
            _classFoes.Clear();
        }

        private static void FaceTo(NetworkMotion n, NetworkInventory who, Vector3 target)
        {
            Vector3 d = target - who.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 0.01f) return;
            n.ServerTeleport(who.transform.position, Quaternion.LookRotation(d).eulerAngles.y);
        }

        private static int BarSlotOf(NetworkInventory who, string id)
        {
            foreach (ActionBarEntry e in who.ServerData.actionBar)
                if (e.page == 0 && e.referenceId == id) return e.slot;
            return -1;
        }

        private static string Cut(string s) => s != null && s.Length > 160 ? s.Substring(0, 160) + "…" : s;
    }
}
