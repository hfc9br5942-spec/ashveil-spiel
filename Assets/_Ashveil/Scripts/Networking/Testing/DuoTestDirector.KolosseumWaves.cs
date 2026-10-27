using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ashveil.Data;
using Ashveil.Gameplay.Colosseum;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.World;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Kolosseum Pass 3: Wellenplan (Seed, Budget, Eingaenge) und ein Lauf im Zeitraffer mit Maxmod und Krisbot –
    /// Trupps kommen aus den Toren, die Obergrenze haelt, Gegner kaempfen ohne Leine, Maltebot in Kareth sieht nichts davon.
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        private const int ArenaTestSeed = 4242;

        private IEnumerator WavePlanChecks(DungeonDirector dir, DungeonSlot slot)
        {
            Line("");
            Line("[Wellenplan (ohne Netz)]");
            var handler = dir.GetComponent<ColosseumInstanceHandler>();
            ColosseumDefinition def = handler != null ? handler.Definition : null;
            Check(handler != null && def != null && def.phases.Count == 12 && def.pool.Count > 0,
                $"Wellen-Direktor am Kolosseum mit Daten ({(def != null ? def.phases.Count : 0)} Bloecke, {(def != null ? def.pool.Count : 0)} Gegnerarten im Pool)");
            if (def == null) yield break;
            Check(def.pool.All(e => e.prefab != null && e.prefab.GetComponent<EnemyBrain>() != null), "Alle Pool-Eintraege haben ein Gegner-Prefab");
            List<ArenaGate> gates = ColosseumInstanceHandler.GatesOf(slot);
            List<ArenaEntryMask> masks = ColosseumInstanceHandler.MasksOf(gates);
            Check(gates.Count == 7, $"Eingaenge der Arena in fester Reihenfolge: {string.Join(", ", gates.Select(g => g.GateId))}");

            WavePlan p1 = WavePlan.Build(def, ArenaTestSeed, 5, masks, 3600f);
            WavePlan p2 = WavePlan.Build(def, ArenaTestSeed, 5, masks, 3600f);
            WavePlan p3 = WavePlan.Build(def, ArenaTestSeed + 1, 5, masks, 3600f);
            Check(p1.Spawns.Count > 0 && p1.Hash() == p2.Hash(), $"Gleicher Seed, gleicher Plan ({p1.Hash():X8}, {p1.Spawns.Count} Trupps in 60 min)");
            Check(p1.Hash() != p3.Hash(), $"Anderer Seed, anderer Plan ({p3.Hash():X8})");

            bool budgetOk = true, gatesOk = true, levelOk = true, capOk = true;
            for (int players = 1; players <= 5; players++)
            {
                WavePlan pl = WavePlan.Build(def, ArenaTestSeed + players * 17, players, masks, 3600f);
                for (int i = 0; i < pl.SpentPerPhase.Count; i++)
                    if (pl.SpentPerPhase[i] > pl.BudgetPerPhase[i] + 0.001f) budgetOk = false;
                foreach (WaveSpawn w in pl.Spawns)
                {
                    ArenaPoolEntry e = def.pool[w.Pool];
                    if ((masks[w.Gate] & e.entries & def.phases[w.Phase].entries) == 0) gatesOk = false;
                    if (w.Level < def.baseLevel || w.Level > def.maxLevel) levelOk = false;
                    if (w.Count > def.AliveCap(w.Phase, players)) capOk = false;
                }
            }
            Check(budgetOk, "Budget je Block wird nie ueberschritten (1 bis 5 Spieler)");
            Check(gatesOk, "Jeder Trupp kommt aus einem Eingang, den Gegnerart und Block erlauben");
            Check(levelOk, $"Gegnerstufen im Band {def.baseLevel}–{def.maxLevel}");
            Check(capOk, "Kein Trupp ist groesser als die Obergrenze gleichzeitiger Gegner");
            Check(p1.Spawns.Where(w => w.Phase == 0).All(w => !w.Elite) && p1.Spawns.Any(w => w.Elite), "Prüfung I ohne Elite, spaeter Elite-Trupps");
            WavePlan solo = WavePlan.Build(def, ArenaTestSeed, 1, masks, 3600f);
            Check(solo.Spawns.Sum(w => w.Cost) < p1.Spawns.Sum(w => w.Cost), $"Allein weniger Gegnerpunkte als zu fuenft ({solo.Spawns.Sum(w => w.Cost)} gegen {p1.Spawns.Sum(w => w.Cost)})");
            for (int i = 0; i < def.phases.Count; i++)
            {
                var ws = p1.Spawns.Where(w => w.Phase == i).ToList();
                Info($"{def.phases[i].displayName,-17} 5 Spieler: {p1.SpentPerPhase[i],3}/{p1.BudgetPerPhase[i],5:0} Punkte, {ws.Count,2} Trupps, " +
                     $"{ws.Sum(w => w.Count),3} Gegner, {ws.Count(w => w.Elite)} Elite, gleichzeitig hoechstens {def.AliveCap(i, 5)} (allein {def.AliveCap(i, 1)})");
            }
            yield break;
        }

        private IEnumerator WaveRunChecks(DungeonDirector dir, DungeonPortal portal, NetworkInventory a, NetworkInventory b, NetworkInventory c)
        {
            Line("");
            Line("[Wellen im Ring (Zeitraffer ×4, Maxmod und Krisbot unverwundbar)]");
            var handler = dir.GetComponent<ColosseumInstanceHandler>();
            if (handler == null) yield break;
            ColosseumInstanceHandler.StartDelayOverride = 2f;
            ColosseumInstanceHandler.TimeScaleOverride = 4f;
            ColosseumInstanceHandler.SeedOverride = ArenaTestSeed;
            Vector3 gate = Gnd(portal.transform.position.x, portal.transform.position.z);
            Vector3 front = Gnd(portal.ReturnWorld.x, portal.ReturnWorld.z);
            yield return Ask(c, "goto", V(front - Vector3.forward * 4f, portal.ReturnYaw));
            yield return new WaitForSecondsRealtime(3.5f); // Pforten-Sperre nach dem Verlassen
            yield return Ask(a, "goto", V(gate, portal.ReturnYaw + 180f));
            yield return WaitUntil(() => dir.InstanceOf(a.ProfileId) != null, 6f);
            yield return Ask(b, "goto", V(gate, portal.ReturnYaw + 180f));
            yield return WaitUntil(() => dir.InstanceOf(b.ProfileId) != null, 6f);
            DungeonDirector.Instance inst = dir.InstanceOf(a.ProfileId);
            Check(inst != null && dir.InstanceOf(b.ProfileId) == inst, "Maxmod und Krisbot stehen gemeinsam im Ring");
            if (inst == null) yield break;
            DungeonSlot slot = inst.Slot;
            var duo = new[] { a.GetComponent<Health>(), b.GetComponent<Health>() };
            // In die Mitte stellen
            Vector3 mid = slot.transform.position;
            yield return Ask(a, "goto", V(Gnd(mid.x - 2f, mid.z - 6f), 0f));
            yield return Ask(b, "goto", V(Gnd(mid.x + 2f, mid.z - 6f), 0f));

            ColosseumInstanceHandler.RunState state = null;
            yield return WaitUntil(() => (state = handler.StateOf(inst)) != null && state.Started, 8f);
            Check(state != null && state.Started, $"Nach der Wartezeit beginnt der Lauf (Seed {(state != null ? state.Seed : 0)}, " +
                                                  $"{(state != null && state.Run != null ? state.Run.Plan.Spawns.Count : 0)} Trupps geplant)");
            if (state == null || !state.Started) yield break;
            Check(state.Run.Plan.Players == 2, $"Plan fuer 2 Spieler (Gruppenfaktor {state.Run.Definition.GroupFactor(2):0.00})");

            float t = 0f, nextKill = 6f;
            int maxAlive = 0, returning = 0, killed = 0, spawnNearPlayer = 0, checkedLog = 0;
            float closest = float.MaxValue;
            bool clientSaw = false, kaarethClean = true;
            while (t < 48f)
            {
                foreach (Health h in duo)
                {
                    if (h == null) continue;
                    h.SetInvulnerableFor(1.5f);
                    if (h.Fraction < 0.9f) h.Heal(h.Max);
                }
                var alive = EnemyBrain.All.Where(e => e != null && slot.Contains(e.transform.position) && e.GetComponent<Health>() is Health eh && eh != null && eh.IsAlive).ToList();
                maxAlive = Mathf.Max(maxAlive, alive.Count);
                returning += alive.Count(e => e.State == EnemyState.Return);
                foreach (EnemyBrain e in alive)
                    closest = Mathf.Min(closest, Mathf.Min(Vector3.Distance(e.transform.position, a.transform.position), Vector3.Distance(e.transform.position, b.transform.position)));
                // neue Erscheinungen: wie weit vom naechsten Spieler?
                for (; checkedLog < state.SpawnLog.Count; checkedLog++)
                {
                    Vector3 sp = state.SpawnLog[checkedLog].pos;
                    if (Mathf.Min(Vector3.Distance(sp, a.transform.position), Vector3.Distance(sp, b.transform.position)) < 12f) spawnNearPlayer++;
                }
                if (t >= nextKill && alive.Count > 0)
                {
                    // Ein paar Gegner fallen (Testschaden) – die Wellen muessen nachruecken
                    foreach (EnemyBrain e in alive.Take(3)) { e.GetComponent<Health>().ApplyDamage(new DamageInfo { Amount = 99999f }); killed++; }
                    nextKill += 6f;
                }
                if (Mathf.Abs(t - 30f) < 0.3f)
                {
                    yield return Ask(b, "enemies");
                    clientSaw = IntField(_last.data, "same") > 0;
                    Info($"Krisbot (im Ring): {_last.data}");
                    yield return Ask(c, "enemies");
                    kaarethClean = IntField(_last.data, "other") == 0;
                    Info($"Maltebot (auf dem Vorplatz): {_last.data}");
                }
                t += 0.5f;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            ColosseumRun run = state.Run;
            int cap = run.CurrentCap;
            Info($"Nach {t:0} s (Spielzeit {ColosseumInstanceHandler.Clock(state.Elapsed)}, {state.Run.Definition.phases[Mathf.Max(0, run.Phase)].displayName}): " +
                 $"{run.Released} Trupps / {run.ReleasedEnemies} Gegner erschienen, {run.Waiting} warten, hoechstens {maxAlive} gleichzeitig (Grenze {cap}), {killed} per Testschaden gefallen");
            Check(run.Released >= 3 && state.SpawnLog.Count >= 6, $"Trupps kommen nacheinander ({run.Released} Trupps, {state.SpawnLog.Count} Gegner)");
            var gates = state.Gates;
            bool fromGates = state.SpawnLog.All(e => Vector3.Distance(e.pos, gates[e.gate].SpawnWorld) < 6f);
            var usedGates = state.SpawnLog.Select(e => gates[e.gate].GateId).Distinct().OrderBy(x => x).ToList();
            Check(fromGates, $"Jeder Gegner erscheint an seinem Eingang (genutzt: {string.Join(", ", usedGates)})");
            Check(spawnNearPlayer == 0, $"Niemand erscheint direkt vor einem Spieler (unter 12 m: {spawnNearPlayer})");
            Check(maxAlive <= cap && run.CapViolations == 0, $"Obergrenze gehalten: hoechstens {maxAlive} gleichzeitig (Grenze {cap} fuer 2 Spieler)");
            Check(run.Waiting > 0 || maxAlive >= cap - 3, $"Die Arena fuellt sich bis an die Grenze ({maxAlive}/{cap}, {run.Waiting} Trupps warten)");
            Check(closest < 4f, $"Gegner stuermen ohne Sichtlinie aus dem Torgang zu den Spielern (naechster {closest:0.0} m)");
            Check(returning == 0, "Kein Gegner kehrt um oder heilt sich an einer Leine (Arena-Modus)");
            Check(clientSaw, "Krisbots Client bekommt die Gegner ihrer Arena");
            Check(kaarethClean, "Maltebot auf dem Vorplatz bekommt keinen einzigen Arena-Gegner");

            // Verlassen: Arena wird zurueckgesetzt, alles verschwindet
            foreach (NetworkInventory p in new[] { a, b })
            {
                yield return Ask(p, "goto", V(MineGround(slot.ExitWorld), DoorYaw(slot)));
                NetworkInventory who = p;
                yield return WaitUntil(() => DungeonSlot.At(who.transform.position) == null, 8f);
            }
            yield return WaitUntil(() => !inst.Active, 12f);
            yield return new WaitForSecondsRealtime(1f);
            int left = EnemyBrain.All.Count(e => e != null && slot.Contains(e.transform.position));
            Check(!inst.Active && handler.StateOf(inst) == null && left == 0, $"Nach dem Verlassen: Lauf verworfen, Arena leer ({left} Gegner/Leichen uebrig)");

            ColosseumInstanceHandler.StartDelayOverride = -1f;
            ColosseumInstanceHandler.TimeScaleOverride = 1f;
            ColosseumInstanceHandler.SeedOverride = -1;
        }
    }
}
