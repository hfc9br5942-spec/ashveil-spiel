using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Targeting;
using FishNet.Object;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Kampf-Prototyp (Combat-Masterplan, Boss-Anatomie): Grubenschrat mit Kopf, rechtem Arm und linkem Bein.
    /// Editor-Host Maxmod (arena-a) + Test-Client Krisbot (arena-b), spaeter Maltebot (arena-c) als Spaetkommer.
    /// Prueft Trefferzonen (Klick und Taste), Umleitung des Schadens, Erreichbarkeit des Kopfes, Bruch/Zerstoerung,
    /// Moveset vor und nach dem Armbruch, Niederwurf, Zustaende Healthy/Wounded/Critical, Spiegelung auf Clients,
    /// Tod mit Zerteilung und Zusatzbeute. Start: "Ashveil &gt; Test &gt; Kampf-Prototyp (Grubenschrat, 3 Spieler)".
    /// Bericht: Logs/Ashveil_KampfTest.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string CombatFlag = "kampf";
        private static bool _combatMode;
        private const byte PHead = 1, PArm = 2, PLeg = 3;

        private IEnumerator CombatSuite()
        {
            // ---------------------------------------------------------- Anmeldung
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(ArenaProfiles[0])) != null, 30f);
            Check(a != null, "Spieler A (Editor-Host) angemeldet");
            if (a == null) yield break;
            string exe = Ashveil.Core.ProjectPaths.TestClientExe;
            if (!File.Exists(exe)) { Check(false, "Test-Client vorhanden"); yield break; }
            StartClient(exe, ArenaProfiles[1]);
            NetworkInventory b = null;
            yield return WaitUntil(() => (b = OnlineInv(ArenaProfiles[1])) != null, 150f);
            Check(b != null, "Spieler B (Test-Client) angemeldet");
            if (b == null) yield break;
            yield return new WaitForSecondsRealtime(2f);

            // Platz: Kolosseum-Vorplatz (eben, NavMesh, offen)
            Vector3 spot = Gnd(488f, 270f);
            a.GetComponent<NetworkMotion>().ServerTeleport(spot + new Vector3(-9f, 0f, -2f), 90f);
            b.GetComponent<NetworkMotion>().ServerTeleport(spot + new Vector3(-9f, 0f, 2f), 90f);
            yield return new WaitForSecondsRealtime(2f);

            EnemySpawnArea area = Object.FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None)
                .FirstOrDefault(x => x.Definition != null && x.Definition.Id == "enemy.salt_brute" && x.Prefab != null);
            Check(area != null, "Grubenschrat-Vorlage gefunden");
            if (area == null) yield break;
            EnemyDefinition def = area.Definition;
            Check(def.HasAnatomy && def.bodyParts.Count == 3 && def.HasMoveset && def.moveset.Count == 6,
                $"Grubenschrat hat Kopf, rechten Arm, linkes Bein und ein Moveset ({def.bodyParts.Count} Teile, {def.moveset.Count} Angriffe)");

            // ---------------------------------------------------------- Planer (rein rechnerisch)
            Line("");
            Line("[Moveset-Planer]");
            {
                var plan = new EnemyMovePlanner(def);
                var picks = new Dictionary<byte, int>();
                float now = 0f;
                for (int i = 0; i < 400; i++)
                {
                    now += 1.5f;
                    EnemyAttackDefinition m = plan.Pick(2.5f, now, id => false, (i * 0.618f) % 1f);
                    if (m == null) continue;
                    plan.MarkUsed(m, now);
                    picks[m.id] = picks.TryGetValue(m.id, out int n) ? n + 1 : 1;
                }
                Check(picks.ContainsKey(1) && picks.ContainsKey(3) && !picks.ContainsKey(2) && !picks.ContainsKey(4) && !picks.ContainsKey(5) && !picks.ContainsKey(6),
                    "Arm heil, Nahkampf: Doppelschlag und Stampfen, nie Nachschlag allein, nie Ansturm oder Ersatzangriffe (" + Dict(picks) + ")");
                var plan2 = new EnemyMovePlanner(def);
                var picks2 = new Dictionary<byte, int>();
                now = 0f;
                for (int i = 0; i < 400; i++)
                {
                    now += 1.5f;
                    EnemyAttackDefinition m = plan2.Pick(2.5f, now, id => id == PArm, (i * 0.618f) % 1f);
                    if (m == null) continue;
                    plan2.MarkUsed(m, now);
                    picks2[m.id] = picks2.TryGetValue(m.id, out int n) ? n + 1 : 1;
                }
                Check(!picks2.ContainsKey(1) && !picks2.ContainsKey(2) && picks2.ContainsKey(5) && picks2.ContainsKey(6),
                    "Arm gebrochen: kein Doppelschlag mehr, dafuer Schulterramme und linker Faustschlag (" + Dict(picks2) + ")");
                var plan3 = new EnemyMovePlanner(def);
                EnemyAttackDefinition far = plan3.Pick(10f, 100f, id => false, 0.5f);
                EnemyAttackDefinition farLeg = plan3.Pick(10f, 100f, id => id == PLeg, 0.5f);
                Check(far != null && far.id == 4 && farLeg == null, "Ansturm aus 10 m – aber nicht mit gebrochenem Bein");
            }

            // ---------------------------------------------------------- Spawn und Zonen
            Line("");
            Line("[Trefferzonen]");
            // Defensiv: greift nicht von selbst an, schlaegt aber zurueck. Bis 03.10.2026 stand hier „Passiv“ – das hiess damals
            // „flieht und kehrt heim“ (mit Vollheilung und Teile-Reset), der Kampfteil war so nicht pruefbar.
            EnemyBrain brute = EnemyDirector.Instance.SpawnAt(area.Prefab, spot, 270f, 13, "", br => br.SetAggroProfile(AggroProfile.Defensive), 120f);
            Check(brute != null, "Grubenschrat erzeugt (Stufe 13, zunaechst friedlich)");
            _combatGuard = true;
            StartCoroutine(GuardPlayers(a.GetComponent<Health>(), b.GetComponent<Health>()));
            if (brute == null) { _combatGuard = false; yield break; }
            yield return new WaitForSecondsRealtime(2.5f);
            int oid = brute.GetComponent<NetworkObject>().ObjectId;
            EnemyAnatomy ana = brute.Anatomy;
            Health hp = brute.Health;
            Check(ana != null && ana.Parts.Count == 3 && ana.Parts.All(p => p.Pivot != null && p.Hitbox != null && p.Hitbox.isTrigger),
                "Server: drei Koerperteile mit Trefferzonen (Trigger, stoeren keine Kampfabfragen)");
            yield return Ask(b, "anatomy", oid.ToString());
            Check(_last.ok && _last.data.Contains("parts=3;zones=3;states=0,0,0"), "Client B sieht dieselben Teile und Zonen (" + _last.data + ")");

            // Klick auf den Arm waehlt Ziel und Teil (derselbe Strahl wie ein Mausklick)
            TargetingSystem ts = a.GetComponent<TargetingSystem>();
            Vector3 armCenter = ana.Get(PArm).Hitbox.bounds.center;
            Vector3 legCenter = ana.Get(PLeg).Hitbox.bounds.center;
            bool clicked = ts != null && ts.SelectByRay(new Ray(armCenter + brute.transform.right * 8f + Vector3.up * 0.5f,
                (armCenter - (armCenter + brute.transform.right * 8f + Vector3.up * 0.5f)).normalized));
            Check(clicked && ts.Current != null && ts.Current.gameObject == brute.gameObject && ts.CurrentPart == PArm,
                $"Klick auf den rechten Arm waehlt Grubenschrat + Teil {ts?.CurrentPart}");
            ts.CycleWeakPoint(null);
            byte afterKey = ts.CurrentPart;
            ts.CycleWeakPoint(null);
            byte afterKey2 = ts.CurrentPart;
            Check(afterKey == PLeg && afterKey2 == 0, $"Taste „Schwachpunkt“ geht reihum: Arm → Bein → Koerper ({afterKey}, {afterKey2})");
            bool bodyClick = ts.SelectByRay(new Ray(brute.transform.position + Vector3.up * 1.6f + brute.transform.forward * 8f, -brute.transform.forward));
            Check(bodyClick && ts.Current != null && ts.Current.gameObject == brute.gameObject && ts.CurrentPart == 0,
                $"Klick von vorn auf den Rumpf waehlt den Gegner ohne Teil ({ts.CurrentPart})");

            // Client B: waehlt den Arm (Weg wie ein Mensch) → Server kennt die Wahl
            yield return Ask(b, "aimpart", oid + " " + PArm);
            yield return new WaitForSecondsRealtime(0.5f);
            Check(_last.ok && _last.data.Contains("part=2") && _last.data.Contains("RECHTER ARM"), "Client B: Zielrahmen zeigt „Ziel: RECHTER ARM“ (" + _last.data + ")");
            Check(b.GetComponent<NetworkCombat>().ServerAimedPart == PArm, "Server: B zielt auf den rechten Arm");

            // Echte Schlaege von B landen auf dem Arm
            float armBefore = ana.Get(PArm).Current, hpBefore = hp.Current;
            brute.ServerStun(12f); // steht still, waehrend B zuschlaegt
            b.GetComponent<NetworkMotion>().ServerTeleport(brute.transform.position + brute.transform.forward * 2.6f, brute.transform.eulerAngles.y + 180f);
            yield return new WaitForSecondsRealtime(1f);
            yield return Ask(b, "attack", oid + " keep");
            yield return WaitUntil(() => ana.Get(PArm).Current < armBefore, 12f);
            yield return Ask(b, "stop");
            float armLoss = armBefore - ana.Get(PArm).Current, hpLoss = hpBefore - hp.Current;
            Check(armLoss > 0f && hpLoss > 0f, $"B trifft den Arm: Arm −{armLoss:0}, Hauptleben −{hpLoss:0} (Teil zaehlt auch fuers Ganze)");

            // ---------------------------------------------------------- Kopf, Bruch, Zerstoerung
            Line("");
            Line("[Kopf und Bruch]");
            float headBefore = ana.Get(PHead).Current;
            hpBefore = hp.Current;
            hp.ApplyDamage(new DamageInfo { Amount = 50f, Source = a.gameObject, PartId = PHead, WeaponId = "test" });
            Check(Mathf.Approximately(ana.Get(PHead).Current, headBefore) && Mathf.Abs(hpBefore - hp.Current - 50f) < 1.1f,
                $"Kopf im Stehen nicht erreichbar: Treffer geht auf den Koerper (−{hpBefore - hp.Current:0}, Kopf unveraendert)");

            hp.ApplyDamage(new DamageInfo { Amount = ana.Get(PArm).Max, Source = a.gameObject, PartId = PArm, WeaponId = "test" });
            Check(ana.StateOf(PArm) == BodyPartState.Broken, $"Rechter Arm gebrochen ({ana.StateOf(PArm)})");
            yield return new WaitForSecondsRealtime(1f);
            yield return Ask(b, "anatomy", oid.ToString());
            Check(_last.ok && _last.data.Contains("states=0,2,0"), "Client B sieht den gebrochenen Arm (" + _last.data + ")");
            hp.ApplyDamage(new DamageInfo { Amount = ana.Get(PArm).Max * 0.7f, Source = a.gameObject, PartId = PArm, WeaponId = "test" }); // P2: 0,5 → 0,7, damit es mit Tabellenleben sicher unter 66 % geht
            Check(ana.StateOf(PArm) == BodyPartState.Destroyed && !ana.Get(PArm).Hitbox.enabled, "Weiterer Schaden zerstoert den Arm – Zone weg");
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Ask(b, "anatomy", oid.ToString());
            Check(_last.ok && _last.data.Contains("states=0,3,0") && _last.data.Contains("forearm=1"), "Client B: Unterarm faellt ab (" + _last.data + ")");
            Check(ana.DamageState == EnemyDamageState.Wounded && _last.data.Contains("dmg=1"),
                $"Unter 66 % Leben: verwundet – Server und Client ({hp.Current:0}/{hp.Max:0}, {ana.DamageState})");

            // Spaetkommer sieht den zerstoerten Arm
            StartClient(exe, ArenaProfiles[2]);
            NetworkInventory c = null;
            yield return WaitUntil(() => (c = OnlineInv(ArenaProfiles[2])) != null, 150f);
            if (c != null)
            {
                c.GetComponent<NetworkMotion>().ServerTeleport(spot + new Vector3(-10f, 0f, 5f), 90f);
                yield return new WaitForSecondsRealtime(4f);
                yield return Ask(c, "anatomy", oid.ToString());
                Check(_last.ok && _last.data.Contains("states=0,3,0") && _last.data.Contains("forearm=1"), "Spaetkommer C sieht den zerstoerten Arm sofort (" + _last.data + ")");
            }
            else Check(false, "Spieler C (Spaetkommer) angemeldet");

            // ---------------------------------------------------------- Bein: Niederwurf, Kopf erreichbar
            Line("");
            Line("[Niederwurf]");
            hp.ApplyDamage(new DamageInfo { Amount = ana.Get(PLeg).Max + 1f, Source = a.gameObject, PartId = PLeg, WeaponId = "test" });
            yield return new WaitForSecondsRealtime(0.6f);
            Check(ana.StateOf(PLeg) == BodyPartState.Broken && ana.IsKnockedDown && brute.IsStunned, $"Bein gebrochen: Niederwurf ({ana.KnockdownRemaining:0.0} s)");
            yield return Ask(b, "anatomy", oid.ToString());
            Check(_last.ok && _last.data.Contains("knock=1"), "Client B sieht ihn knien (" + _last.data + ")");
            headBefore = ana.Get(PHead).Current;
            hpBefore = hp.Current;
            hp.ApplyDamage(new DamageInfo { Amount = 100f, Source = a.gameObject, PartId = PHead, WeaponId = "test" });
            float headLoss = headBefore - ana.Get(PHead).Current;
            float mainLoss = hpBefore - hp.Current;
            Check(Mathf.Abs(headLoss - 150f) < 1.1f && Mathf.Abs(mainLoss - 150f) < 1.1f, $"Kniend ist der Kopf erreichbar: 100 → {mainLoss:0} Schaden (x1,5)");
            hp.ApplyDamage(new DamageInfo { Amount = ana.Get(PHead).Current / 1.5f + 1f, Source = a.gameObject, PartId = PHead, WeaponId = "test" });
            Check(ana.StateOf(PHead) >= BodyPartState.Broken && hp.IsAlive, "Kopf gebrochen (er lebt noch)");
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Ask(b, "anatomy", oid.ToString());
            Check(ana.DamageState == EnemyDamageState.Critical && _last.data.Contains("dmg=2"),
                $"Unter 33 % Leben: kritisch – Server und Client ({hp.Current:0}/{hp.Max:0}, {_last.data})");
            yield return new WaitForSecondsRealtime(8.5f);
            Check(!ana.IsKnockedDown && Mathf.Approximately(brute.SpeedFactor, 0.7f), $"Nach 8 s steht er wieder auf, humpelt (Tempo x{brute.SpeedFactor:0.00})");
            hp.ApplyDamage(new DamageInfo { Amount = 50f, Source = a.gameObject, PartId = PLeg, WeaponId = "test" });
            Check(!ana.IsKnockedDown, "Niederwurf nur einmal");

            // ---------------------------------------------------------- Moveset im Kampf (Arm und Bein gebrochen)
            Line("");
            Line("[Moveset im Kampf]");
            brute.MoveHistory.Clear();
            brute.MoveCounts.Clear();
            Health bh = b.GetComponent<Health>();
            brute.ServerEngage(bh);
            float t0 = Time.time;
            while (Time.time - t0 < 22f)
            {
                bh.SetInvulnerableFor(1f);
                bh.Heal(bh.Max);
                yield return new WaitForSecondsRealtime(0.5f);
            }
            string hist = string.Join(",", brute.MoveHistory);
            Info("Angriffe: " + hist);
            bool noArm = !brute.MoveHistory.Contains(1) && !brute.MoveHistory.Contains(2);
            bool noCharge = !brute.MoveHistory.Contains(4);
            int maxRun = 0, run = 0;
            for (int i = 0; i < brute.MoveHistory.Count; i++) { run = i > 0 && brute.MoveHistory[i] == brute.MoveHistory[i - 1] ? run + 1 : 1; maxRun = Mathf.Max(maxRun, run); }
            Check(brute.MoveHistory.Count >= 4, $"Er greift an ({brute.MoveHistory.Count} Angriffe in 22 s)");
            Check(noArm && noCharge, "Ohne Arm kein Doppelschlag, mit gebrochenem Bein kein Ansturm");
            Check(brute.MoveHistory.Distinct().Count() >= 2 && maxRun <= 3, $"Abwechslung: {brute.MoveHistory.Distinct().Count()} verschiedene Angriffe, hoechstens {maxRun}× derselbe hintereinander");

            // ---------------------------------------------------------- Nachhall (legendaere Eigenschaft, Max 04.10.)
            Line("");
            Line("[Nachhall]");
            WeaponDefinition nh = GameData.Get<WeaponDefinition>("weapon.nachhall_short");
            Check(nh != null && nh.HasEcho && GameData.Get<WeaponDefinition>("weapon.nachhall_great") != null,
                "Nachhall: Kurzklinge und Großklinge in der Datenbank, Eigenschaft aktiv");
            if (nh != null && nh.HasEcho)
            {
                float nhOld = nh.echoChance;
                nh.echoChance = 1f;   // nur fuer den Test sicher ausloesen
                try
                {
                    int nhT0 = WeaponEcho.TriggeredCount, nhR0 = NetworkHealth.EchoRpcCount;
                    float nhH0 = hp.Current;
                    hp.ApplyDamage(new DamageInfo { Amount = 40f, Source = a.gameObject, Direction = Vector3.forward, WeaponId = nh.Id });
                    float nhH1 = hp.Current, nhHit = nhH0 - nhH1;
                    yield return new WaitForSecondsRealtime(nh.echoDelay * 0.4f);
                    bool nhNotYet = WeaponEcho.TriggeredCount == nhT0;
                    yield return new WaitForSecondsRealtime(nh.echoDelay + 0.5f);
                    float nhEcho = nhH1 - hp.Current;
                    Check(nhNotYet && WeaponEcho.TriggeredCount == nhT0 + 1,
                        $"Treffer hallt verzögert genau einmal nach (nach {nh.echoDelay * 0.4f:0.00} s noch nicht, danach 1×)");
                    Check(nhEcho >= 1f && nhEcho <= nhHit * nh.echoFraction + 1f,
                        $"Nachhall-Schaden {nhEcho:0} ≤ {nh.echoFraction * 100f:0} % des Treffers ({nhHit:0}), keine Kette");
                    hp.ApplyDamage(new DamageInfo { Amount = 40f, Source = a.gameObject, Direction = Vector3.forward, WeaponId = nh.Id });
                    yield return new WaitForSecondsRealtime(nh.echoDelay + 0.5f);
                    Check(WeaponEcho.TriggeredCount == nhT0 + 1, $"Interne Abklingzeit {nh.echoCooldown:0} s: zweiter Treffer hallt nicht nach");
                    Check(NetworkHealth.EchoRpcCount > nhR0, "Lichtpuls-RPC erreicht die Beobachter (Host)");
                }
                finally { nh.echoChance = nhOld; }
            }

            // ---------------------------------------------------------- Tod, Zerteilung, Beute
            Line("");
            Line("[Tod und Beute]");
            hp.ApplyDamage(new DamageInfo { Amount = hp.Max * 3f, Source = a.gameObject, WeaponId = "test" });
            yield return new WaitForSecondsRealtime(3f);
            var corpse = brute.GetComponent<CorpseLoot>();
            List<ItemStack> loot = corpse != null ? corpse.Get(a.ProfileId) : new List<ItemStack>();
            int vein = Count(loot, "item.red_vein_salt"), grave = Count(loot, "item.grave_salt"), mineSalt = Count(loot, "item.mine_salt");
            Check(vein >= 3 && grave >= 1 && mineSalt >= 4, $"Zusatzbeute fuer gebrochene Teile: Rotaderiges Salz {vein}, Grabsalz {grave}, Grubensalz {mineSalt} ({Join(loot)})");
            int hostSplit = 0;
            foreach (Transform ch in brute.transform) if (ch.GetComponent<Rigidbody>() != null && !ch.name.StartsWith("Unterarm")) hostSplit++;
            yield return Ask(b, "anatomy", oid.ToString());
            Check(hostSplit >= 2 && _last.data.Contains("split=2"), $"Tod bei „Blut: Voll“: Kopf und linker Arm fallen ab (Host {hostSplit}, B {_last.data})");
            _combatGuard = false;
            EnemyDirector.Instance.Remove(brute);

            // Legendaeres Schwert „Nachhall“: Handprobe an der echten Figur (Bilder unter Logs/Ashveil/Ausruestung)
            yield return NachhallHandProbe(a);
        }

        private bool _combatGuard;

        /// <summary>Haelt beide Testfiguren am Leben, solange der Grubenschrat zurueckschlaegt (der Test prueft ihn, nicht sie).</summary>
        private IEnumerator GuardPlayers(Health x, Health y)
        {
            while (_combatGuard)
            {
                foreach (Health h in new[] { x, y })
                    if (h != null && h.IsAlive) { h.SetInvulnerableFor(0.6f); h.Heal(h.Max); }
                yield return new WaitForSecondsRealtime(0.25f);
            }
        }

        private static string Dict(Dictionary<byte, int> d) => string.Join(" ", d.OrderBy(k => k.Key).Select(k => k.Key + ":" + k.Value));
    }
}
