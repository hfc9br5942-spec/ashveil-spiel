using System.Collections.Generic;
using Ashveil.Data;
using UnityEngine;

namespace Ashveil.Gameplay.Colosseum
{
    /// <summary>Ein geplanter Trupp: wann, was, wie viele, aus welchem Eingang, welche Stufe.</summary>
    public struct WaveSpawn
    {
        public float Time;     // Sekunden seit Laufbeginn
        public int Phase;
        public int Pool;       // Index in ColosseumDefinition.pool
        public int Count;
        public int Gate;       // Index in der Eingangsliste der Arena
        public int Level;
        public bool Elite;
        public int Cost;       // Punkte des ganzen Trupps
    }

    /// <summary>
    /// Kolosseum: der komplette Wellenplan eines Laufs, VORAB aus Seed + Daten berechnet (eigenes System.Random –
    /// unabhaengig vom Kampfzufall). Gleicher Seed, gleiche Spielerzahl, gleiche Eingaenge = gleicher Plan (Hash).
    /// Je Block wird das Budget (Punkte/Minute × Minuten × Gruppenfaktor) nie ueberschritten.
    /// </summary>
    public sealed class WavePlan
    {
        public readonly List<WaveSpawn> Spawns = new List<WaveSpawn>();
        public readonly List<float> BudgetPerPhase = new List<float>();
        public readonly List<int> SpentPerPhase = new List<int>();
        public int Seed { get; private set; }
        public int Players { get; private set; }
        public float Seconds { get; private set; }

        /// <param name="gates">Art jedes Eingangs der Arena in fester Reihenfolge (Index = WaveSpawn.Gate).</param>
        public static WavePlan Build(ColosseumDefinition def, int seed, int players, IReadOnlyList<ArenaEntryMask> gates, float seconds)
        {
            var plan = new WavePlan { Seed = seed, Players = Mathf.Clamp(players, 1, 5), Seconds = seconds };
            if (def == null || def.phases.Count == 0 || def.pool.Count == 0 || gates == null || gates.Count == 0) return plan;
            float factor = def.GroupFactor(plan.Players);
            var candidates = new List<int>();
            var gateChoice = new List<int>();
            for (int p = 0; p < def.phases.Count; p++)
            {
                WavePhaseDefinition ph = def.phases[p];
                float t0 = ph.startMinute * 60f, t1 = Mathf.Min(ph.endMinute * 60f, seconds);
                float budget = ph.budgetPerMinute * (ph.endMinute - ph.startMinute) * factor;
                plan.BudgetPerPhase.Add(budget);
                int spent = 0;
                if (t1 <= t0) { plan.SpentPerPhase.Add(0); continue; }
                // Budget nur anteilig, falls der Lauf mitten im Block endet
                float usable = budget * Mathf.Clamp01((t1 - t0) / Mathf.Max(1f, (ph.endMinute - ph.startMinute) * 60f));
                var rng = new System.Random(unchecked(seed * 7919 + p * 104729 + plan.Players * 31));
                float t = t0 + (p == 0 ? 0f : 2f);
                int guard = 0;
                while (t < t1 && guard++ < 1000)
                {
                    bool eliteRoll = rng.NextDouble() < ph.eliteChance;
                    candidates.Clear();
                    float total = 0f;
                    for (int i = 0; i < def.pool.Count; i++)
                    {
                        ArenaPoolEntry e = def.pool[i];
                        if (e.prefab == null || e.minPhase > p || e.elite != eliteRoll || (e.entries & ph.entries) == 0) continue;
                        if (e.cost * Mathf.Max(1, e.packMin) > usable - spent) continue;
                        candidates.Add(i);
                        total += Mathf.Max(0.01f, e.weight);
                    }
                    if (candidates.Count == 0 && eliteRoll)
                    {
                        // keine passende Elite: normaler Trupp
                        for (int i = 0; i < def.pool.Count; i++)
                        {
                            ArenaPoolEntry e = def.pool[i];
                            if (e.prefab == null || e.minPhase > p || e.elite || (e.entries & ph.entries) == 0) continue;
                            if (e.cost * Mathf.Max(1, e.packMin) > usable - spent) continue;
                            candidates.Add(i);
                            total += Mathf.Max(0.01f, e.weight);
                        }
                    }
                    if (candidates.Count == 0) break; // Budget des Blocks aufgebraucht
                    float pick = (float)rng.NextDouble() * total;
                    int chosen = candidates[candidates.Count - 1];
                    foreach (int i in candidates)
                    {
                        pick -= Mathf.Max(0.01f, def.pool[i].weight);
                        if (pick <= 0f) { chosen = i; break; }
                    }
                    ArenaPoolEntry entry = def.pool[chosen];
                    int count = rng.Next(Mathf.Max(1, entry.packMin), Mathf.Max(entry.packMin, entry.packMax) + 1);
                    count = Mathf.Min(count, Mathf.FloorToInt((usable - spent) / Mathf.Max(1, entry.cost)));
                    count = Mathf.Min(count, def.AliveCap(p, plan.Players)); // ein Trupp passt immer unter die Obergrenze
                    if (count <= 0) break;

                    gateChoice.Clear();
                    for (int g = 0; g < gates.Count; g++)
                        if ((gates[g] & entry.entries & ph.entries) != 0) gateChoice.Add(g);
                    if (gateChoice.Count == 0) { t += 1f; continue; }
                    int gate = gateChoice[rng.Next(gateChoice.Count)];
                    int level = Mathf.Clamp(def.baseLevel + ph.levelBonus + (entry.elite ? 1 : 0), 1, def.maxLevel);

                    plan.Spawns.Add(new WaveSpawn
                    {
                        Time = Mathf.Round(t * 100f) / 100f, Phase = p, Pool = chosen, Count = count, Gate = gate, Level = level,
                        Elite = entry.elite, Cost = count * entry.cost
                    });
                    spent += count * entry.cost;
                    t += Mathf.Lerp(ph.packInterval.x, ph.packInterval.y, (float)rng.NextDouble());
                }
                plan.SpentPerPhase.Add(spent);
            }
            return plan;
        }

        /// <summary>Pruefsumme des Plans (FNV-1a) – fuer Tests und Berichte.</summary>
        public uint Hash()
        {
            uint h = 2166136261;
            void Mix(int v)
            {
                unchecked
                {
                    for (int i = 0; i < 4; i++) { h ^= (uint)((v >> (i * 8)) & 0xFF); h *= 16777619; }
                }
            }
            foreach (WaveSpawn s in Spawns)
            {
                Mix(Mathf.RoundToInt(s.Time * 100f)); Mix(s.Phase); Mix(s.Pool); Mix(s.Count); Mix(s.Gate); Mix(s.Level); Mix(s.Elite ? 1 : 0);
            }
            return h;
        }
    }

    /// <summary>
    /// Ablauf eines Laufs ohne Netz: gibt faellige Trupps frei, haelt die Obergrenze gleichzeitiger Gegner ein
    /// (was nicht passt, wartet – nichts geht verloren) und merkt sich Kennzahlen fuer Tests und Telemetrie.
    /// </summary>
    public sealed class ColosseumRun
    {
        public readonly ColosseumDefinition Definition;
        public readonly WavePlan Plan;
        private int _next;
        private readonly Queue<WaveSpawn> _waiting = new Queue<WaveSpawn>();

        public int Released { get; private set; }
        public int ReleasedEnemies { get; private set; }
        public int PeakAlive { get; private set; }
        public int CapViolations { get; private set; }
        public int Phase { get; private set; } = -1;
        public bool Finished { get; private set; }
        public int Waiting => _waiting.Count;

        public ColosseumRun(ColosseumDefinition def, WavePlan plan)
        {
            Definition = def;
            Plan = plan;
        }

        /// <summary>
        /// Takt: faellige Trupps nach <paramref name="output"/>. <paramref name="alive"/> = lebende Gegner jetzt.
        /// Gibt true zurueck, wenn der Block gewechselt hat.
        /// </summary>
        public bool Tick(float elapsed, int alive, List<WaveSpawn> output)
        {
            output.Clear();
            bool phaseChanged = false;
            int phase = Definition.PhaseAt(elapsed);
            if (phase != Phase) { Phase = phase; phaseChanged = true; }
            if (elapsed >= Plan.Seconds) Finished = true;
            PeakAlive = Mathf.Max(PeakAlive, alive);
            while (_next < Plan.Spawns.Count && Plan.Spawns[_next].Time <= elapsed) _waiting.Enqueue(Plan.Spawns[_next++]);
            int cap = Definition.AliveCap(phase, Plan.Players);
            if (alive > cap) CapViolations++;
            while (_waiting.Count > 0 && alive + _waiting.Peek().Count <= cap)
            {
                WaveSpawn s = _waiting.Dequeue();
                alive += s.Count;
                output.Add(s);
                Released++;
                ReleasedEnemies += s.Count;
            }
            return phaseChanged;
        }

        public int CurrentCap => Definition.AliveCap(Mathf.Max(0, Phase), Plan.Players);
    }
}
