using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Colosseum;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.World;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Kolosseum – Ablauf einer Arena-Instanz (Server). Haengt am Kolosseum-<see cref="DungeonDirector"/>.
    ///
    ///   Betreten   – die Gruppe belegt eine Arena und wartet (Pass 4, Max 28.09.: **Bereit-Glocke** – einer laeutet,
    ///                <see cref="ColosseumDefinition.bellCountdown"/> s Countdown). Wer beim Glockenschlag drin ist, nimmt teil.
    ///   Plan       – beim Start wird aus Seed, Spielerzahl und den Eingaengen der ganze Wellenplan berechnet
    ///                (<see cref="WavePlan"/>); gleicher Seed = gleicher Plan.
    ///   Takt       – faellige Trupps kommen aus ihrem Tor, die Obergrenze gleichzeitiger Gegner wird gehalten.
    ///   Champions  – zu festen Zeiten (5:00, 10:00) ein verstaerkter Gegner aus dem Haupttor; jeder Sieg +1 Wiederbelebung.
    ///   Tod        – im laufenden Kampf bleibt man liegen. Ein Mitstreiter belebt in 5 s wieder (Schaden bricht ab),
    ///                gemeinsamer Vorrat 3 je Lauf. Liegen alle Teilnehmer, ist der Lauf verloren (Wipe).
    ///   Pruefpunkt – der Champion der letzten Minute faellt → „Prüfung bestanden“, Truhe mit persoenlicher Belohnung
    ///                (Geld, Erfahrung, 1 Gegenstand gruen/blau) fuer jeden Teilnehmer.
    ///   Zuruecksetzen – der Direktor raeumt Gegner und Platz ab, der Lauf wird verworfen.
    /// </summary>
    public sealed class ColosseumInstanceHandler : DungeonInstanceHandler
    {
        [SerializeField] private ColosseumDefinition definition;
        [SerializeField] private float arenaRadius = 35f;

        /// <summary>Tests: fester Seed (kleiner 0 = zufaellig).</summary>
        public static int SeedOverride = -1;
        /// <summary>Tests: Zeitraffer der Laufzeit (1 = Echtzeit).</summary>
        public static float TimeScaleOverride = 1f;
        /// <summary>Tests (Pass 3): Start ohne Glocke nach so vielen Sekunden (kleiner 0 = Glocke noetig).</summary>
        public static float StartDelayOverride = -1f;
        /// <summary>Tests: Countdown nach der Glocke (kleiner 0 = Einstellung).</summary>
        public static float BellCountdownOverride = -1f;
        /// <summary>Tests: Wartezeit nach einem Wipe, bevor alle vor dem Tor stehen.</summary>
        public static float WipeDelayOverride = -1f;

        /// <summary>Der Kolosseum-Ablauf dieser Szene (fuer die Spieler-RPCs).</summary>
        public static ColosseumInstanceHandler Current { get; private set; }

        public sealed class RunState
        {
            public DungeonDirector.Instance Instance;
            public float StartAt = -1f;
            public float StartedAt = -1f;
            public int Seed;
            public ColosseumRun Run;
            public readonly List<ArenaGate> Gates = new List<ArenaGate>();
            public readonly List<(Vector3 pos, int gate, float time)> SpawnLog = new List<(Vector3, int, float)>();
            public bool EndAnnounced;
            public float Elapsed;
            public bool Started => Run != null;
            // Pass 4
            public string BellRungBy;
            public readonly HashSet<string> Participants = new HashSet<string>();
            public int Revives;
            public int NextBoss;
            public readonly List<EnemyBrain> Bosses = new List<EnemyBrain>();
            public int BossesKilled;
            public bool Won, Lost;
            public float WipeAt = -1f;
            public readonly HashSet<string> Down = new HashSet<string>();
            public readonly Dictionary<string, Channel> Channels = new Dictionary<string, Channel>();
            public readonly HashSet<string> ChestPending = new HashSet<string>();
            public readonly Dictionary<string, string> ChestLog = new Dictionary<string, string>();
            /// <summary>22.10. (INSTANZEN-02): Geld und Erfahrung sind ausgezahlt, der Gegenstand liegt noch in der Truhe (Rucksack voll).</summary>
            public readonly Dictionary<string, ItemDefinition> ChestHeld = new Dictionary<string, ItemDefinition>();
            public bool Over => Won || Lost;
        }

        /// <summary>Laufende Wiederbelebung: wer, wen, seit wann, von wo.</summary>
        public sealed class Channel
        {
            public string Reviver, Target;
            public float Start;
            public Vector3 From;
        }

        private readonly Dictionary<DungeonDirector.Instance, RunState> _runs = new Dictionary<DungeonDirector.Instance, RunState>();
        private readonly List<WaveSpawn> _due = new List<WaveSpawn>();
        private readonly List<string> _tmp = new List<string>();
        private DungeonDirector _director;

        public ColosseumDefinition Definition => definition;
        public float ArenaRadius => arenaRadius;

        public void Configure(ColosseumDefinition def, float radius)
        {
            definition = def;
            arenaRadius = radius;
        }

        private void Awake()
        {
            Current = this;
            _director = GetComponent<DungeonDirector>();
            if (definition != null) ColosseumClient.Definition = definition;
            NetworkPlayer.HoldRespawn = HoldRespawn;
        }

        private void Start()
        {
            // Glocke und Truhe je Arena – auf Server und Clients gleich (reine Szene, Klicks gehen per RPC)
            string id = _director != null ? _director.DungeonId : "kolosseum";
            foreach (DungeonSlot slot in DungeonSlot.All)
            {
                if (slot == null || slot.DungeonId != id) continue;
                Vector3 c = slot.transform.position;
                Vector3 arrival = slot.ArrivalWorld;
                Vector3 fwd = c - arrival;
                fwd.y = 0f;
                fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                Vector3 bell = Ground(arrival + fwd * 6f + right * 3.5f, c.y);
                Vector3 chest = Ground(arrival + fwd * 6f - right * 3.5f, c.y);
                Renderer anyStone = slot.GetComponentInChildren<MeshRenderer>();
                ArenaProps.Build(slot.transform, slot.Index, bell, chest, c, anyStone != null ? anyStone.sharedMaterial : null);
            }
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (NetworkPlayer.HoldRespawn == (System.Func<NetworkPlayer, string>)HoldRespawn) NetworkPlayer.HoldRespawn = null;
        }

        private static Vector3 Ground(Vector3 p, float fallbackY)
        {
            if (Physics.Raycast(new Vector3(p.x, fallbackY + 6f, p.z), Vector3.down, out RaycastHit hit, 12f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return hit.point;
            return new Vector3(p.x, fallbackY, p.z);
        }

        public RunState StateOf(DungeonDirector.Instance instance) =>
            instance != null && _runs.TryGetValue(instance, out RunState s) ? s : null;

        private RunState StateOfProfile(string profile, out DungeonDirector.Instance instance)
        {
            instance = _director != null ? _director.InstanceOf(profile) : null;
            return StateOf(instance);
        }

        /// <summary>Eingaenge einer Arena in fester Reihenfolge (nach Kennung) – Index = WaveSpawn.Gate.</summary>
        public static List<ArenaGate> GatesOf(DungeonSlot slot)
        {
            var list = new List<ArenaGate>();
            foreach (ArenaGate g in ArenaGate.All)
                if (g != null && slot.Contains(g.SpawnWorld)) list.Add(g);
            list.Sort((a, b) => string.CompareOrdinal(a.GateId, b.GateId));
            return list;
        }

        public static List<ArenaEntryMask> MasksOf(List<ArenaGate> gates)
        {
            var masks = new List<ArenaEntryMask>(gates.Count);
            foreach (ArenaGate g in gates)
                masks.Add(g.Kind == ArenaGateKind.Gate ? ArenaEntryMask.Gate : g.Kind == ArenaGateKind.FloorOpening ? ArenaEntryMask.FloorOpening : ArenaEntryMask.Gallery);
            return masks;
        }

        /// <summary>Champions des Laufs: aus den Daten oder (leer) die ersten beiden Elite-Eintraege bei 5:00 und 10:00.</summary>
        public List<ArenaBossEntry> Bosses()
        {
            if (definition == null) return new List<ArenaBossEntry>();
            if (definition.bosses != null && definition.bosses.Count > 0) return definition.bosses;
            var list = new List<ArenaBossEntry>();
            float[] times = { 300f, 600f };
            foreach (ArenaPoolEntry e in definition.pool)
            {
                if (!e.elite || e.prefab == null) continue;
                list.Add(new ArenaBossEntry { title = list.Count == 0 ? "Champion des Rings" : "Champion der letzten Minute", atSeconds = times[list.Count], prefab = e.prefab });
                if (list.Count == times.Length) break;
            }
            return list;
        }

        // ================================================================== Direktor-Haken

        public override void OnActivate(DungeonDirector director, DungeonDirector.Instance instance)
        {
            var state = new RunState { Instance = instance };
            if (StartDelayOverride >= 0f) state.StartAt = Time.time + StartDelayOverride; // Pass-3-Tests: ohne Glocke
            state.Gates.AddRange(GatesOf(instance.Slot));
            _runs[instance] = state;
        }

        public override void OnEnter(DungeonDirector director, DungeonDirector.Instance instance, NetworkInventory player, bool fresh)
        {
            RunState s = StateOf(instance);
            if (s == null) return;
            string p = player.ProfileId;
            Vector3 c = instance.Slot.transform.position;
            // 22.10. (INSTANZEN-04): Die Anzeige beginnt leer – bisher erledigte das der „reset“ aus SendInstanceState, den
            // ColosseumClient jetzt nicht mehr auswertet (auf dem Host loeschte jeder Salzgruben-Reset die laufende Ring-Anzeige).
            Cue(p, c, ColosseumClient.CueEnd, 0f);
            Cue(p, c, ColosseumClient.CueScale, Mathf.Max(0.01f, TimeScaleOverride));
            Cue(p, c, ColosseumClient.CueRevives, s.Started ? s.Revives : definition != null ? definition.revivePool : 3);
            if (!s.Started)
            {
                Cue(p, c, ColosseumClient.CuePhase, 0f);
                if (s.StartAt >= 0f)
                {
                    Cue(p, c, ColosseumClient.CueCountdown, Mathf.Max(0.1f, s.StartAt - Time.time));
                    NetworkSocial.ServerSystemMessage(p, $"Ringmeister Brannoc ruft: „In {Mathf.Max(1, Mathf.CeilToInt(s.StartAt - Time.time))} Sekunden öffnen sich die Tore!“");
                }
                else NetworkSocial.ServerSystemMessage(p, "Ringmeister Brannoc ruft: „Läutet die Glocke, wenn ihr bereit seid!“");
            }
            else
            {
                Cue(p, c, ColosseumClient.CuePhase, s.Run.Phase);
                Cue(p, c, ColosseumClient.CueClock, s.Elapsed);
                Cue(p, c, ColosseumClient.CueBoss, AliveBoss(s) ? 1f : 0f);
                if (s.Over) Cue(p, c, ColosseumClient.CueEnd, s.Won ? 1f : 2f);
                if (s.ChestPending.Contains(p)) Cue(p, c, ColosseumClient.CueChest, 1f);
                NetworkSocial.ServerSystemMessage(p, s.Over ? "Der Kampf ist vorbei." : $"Der Kampf läuft bereits – {Clock(s.Elapsed)}, {PhaseName(s.Run.Phase)}.");
            }
        }

        public override void OnLeave(DungeonDirector director, DungeonDirector.Instance instance, NetworkInventory player)
        {
            RunState s = StateOf(instance);
            if (s == null) return;
            // Nicht abgeholte Truhe: der Ringmeister haendigt den Anteil beim Gehen aus (niemand verliert seine Belohnung)
            // 22.10. (INSTANZEN-02): bei vollem Rucksack bleibt der Gegenstand in der Truhe (Rueckkehr ueber den Eingang)
            if (s.ChestPending.Contains(player.ProfileId)) GiveChest(s, player, true);
            CancelChannelsOf(s, player.ProfileId, null);
            Cue(player.ProfileId, instance.Slot.transform.position, ColosseumClient.CueEnd, 0f);
        }

        public override void OnReset(DungeonDirector director, DungeonDirector.Instance instance)
        {
            RunState s = StateOf(instance);
            if (s != null && s.Started)
                Debug.Log($"[Ashveil] Server: Kolosseum Platz {instance.Slot.Index} beendet nach {Clock(s.Elapsed)} – {s.Run.ReleasedEnemies} Gegner in {s.Run.Released} Trupps, " +
                          $"hoechstens {s.Run.PeakAlive} gleichzeitig, {s.BossesKilled} Champions besiegt, {(s.Won ? "bestanden" : s.Lost ? "verloren" : "abgebrochen")} " +
                          $"(Seed {s.Seed}, Plan {s.Run.Plan.Hash():X8}).");
            // 22.10. (INSTANZEN-02): nicht abgeholte Truhen-Anteile verfallen erst hier – im Log sichtbar machen
            if (s != null && s.ChestPending.Count > 0)
                Debug.Log($"[Ashveil] Server: Kolosseum Platz {instance.Slot.Index} – Truhe nicht abgeholt, verfaellt: {string.Join(", ", s.ChestPending)}.");
            if (s != null)
                foreach (string p in s.Participants)
                    if (NetworkInventory.Online.TryGetValue(p, out NetworkInventory inv) && inv != null)
                    {
                        var np = inv.GetComponent<NetworkPlayer>();
                        if (np != null && np.ServerHeldDown && s.Down.Contains(p) && inv.GetComponent<Health>() is Health h && h != null && !h.IsAlive)
                            ReturnToGate(inv);
                    }
            _runs.Remove(instance);
        }

        public override void OnTick(DungeonDirector director, DungeonDirector.Instance instance, float now)
        {
            RunState s = StateOf(instance);
            if (s == null || definition == null || EnemyDirector.Instance == null) return;
            Vector3 center = instance.Slot.transform.position;
            if (!s.Started)
            {
                if (s.StartAt < 0f || now < s.StartAt || instance.Inside.Count == 0) return;
                StartRun(s, instance, now);
            }

            float scale = Mathf.Max(0.01f, TimeScaleOverride);
            if (s.Over)
            {
                // Vorbei und leer: Platz sofort frei, damit die Gruppe gleich noch einmal antreten kann
                // 22.10. (INSTANZEN-02 / STORY-03): Liegt noch ein Anteil in der Truhe (Rucksack war voll), nicht sofort – dann greift
                // das normale Leer-Zuruecksetzen des Direktors, und der Spieler kann ueber den Eingang zurueck an die Truhe.
                if (s.Won && instance.Inside.Count == 0 && s.ChestPending.Count == 0 && _director != null) { _director.ResetInstance(instance); return; }
                if (s.Lost && s.WipeAt >= 0f && now >= s.WipeAt) FinishWipe(s);
                TickChannels(s, now);
                return;
            }
            s.Elapsed = (now - s.StartedAt) * scale;
            int alive = 0;
            foreach (EnemyBrain b in instance.Enemies)
                if (b != null && b.GetComponent<Health>() is Health h && h != null && h.IsAlive) alive++;
            bool phaseChanged = s.Run.Tick(s.Elapsed, alive, _due);
            if (phaseChanged && !s.Run.Finished)
            {
                Announce(instance, $"{PhaseName(s.Run.Phase)} – {Clock(s.Elapsed)}");
                CueAll(s, ColosseumClient.CuePhase, s.Run.Phase);
                CueAll(s, ColosseumClient.CueClock, s.Elapsed);
            }
            foreach (WaveSpawn w in _due) SpawnTroop(s, w);

            // Champions zu festen Zeiten
            List<ArenaBossEntry> bosses = Bosses();
            while (s.NextBoss < bosses.Count && s.Elapsed >= bosses[s.NextBoss].atSeconds)
            {
                SpawnBoss(s, bosses[s.NextBoss], s.NextBoss);
                s.NextBoss++;
            }

            if (s.Run.Finished && !s.EndAnnounced)
            {
                s.EndAnnounced = true;
                Announce(instance, $"Die Tore schließen sich – {Clock(s.Elapsed)}. Besiegt den letzten Champion!");
            }

            TickChannels(s, now);
            CheckWipe(s, now);
        }

        private void StartRun(RunState s, DungeonDirector.Instance instance, float now)
        {
            s.Seed = SeedOverride >= 0 ? SeedOverride : Random.Range(1, int.MaxValue);
            s.Participants.Clear();
            foreach (string p in instance.Inside) s.Participants.Add(p);
            WavePlan plan = WavePlan.Build(definition, s.Seed, Mathf.Max(1, s.Participants.Count), MasksOf(s.Gates), definition.runSeconds);
            s.Run = new ColosseumRun(definition, plan);
            s.StartedAt = now;
            s.Revives = definition.revivePool;
            CueAll(s, ColosseumClient.CueScale, Mathf.Max(0.01f, TimeScaleOverride));
            CueAll(s, ColosseumClient.CueRevives, s.Revives);
            CueAll(s, ColosseumClient.CuePhase, 0f);
            CueAll(s, ColosseumClient.CueClock, 0f);
            Debug.Log($"[Ashveil] Server: Kolosseum Platz {instance.Slot.Index} – Lauf beginnt ({s.Participants.Count} Spieler, Seed {s.Seed}, " +
                      $"{plan.Spawns.Count} Trupps geplant, Plan {plan.Hash():X8}).");
        }

        // ================================================================== Glocke

        /// <summary>Server: ein Spieler laeutet die Glocke seiner Arena.</summary>
        public void ServerRingBell(NetworkInventory who)
        {
            if (who == null || definition == null) return;
            RunState s = StateOfProfile(who.ProfileId, out DungeonDirector.Instance inst);
            if (s == null) return;
            ArenaBell bell = BellOf(inst.Slot.Index);
            if (bell == null || (bell.transform.position - who.transform.position).sqrMagnitude > 7f * 7f)
            {
                NetworkSocial.ServerSystemMessage(who.ProfileId, "Die Glocke ist zu weit weg.");
                return;
            }
            if (s.Started || s.StartAt >= 0f)
            {
                NetworkSocial.ServerSystemMessage(who.ProfileId, s.Started ? "Der Kampf läuft bereits." : "Die Glocke hat schon geschlagen.");
                return;
            }
            var hp = who.GetComponent<Health>();
            if (hp != null && !hp.IsAlive) return;
            float countdown = BellCountdownOverride >= 0f ? BellCountdownOverride : definition.bellCountdown;
            s.StartAt = Time.time + countdown;
            s.BellRungBy = who.ProfileId;
            Vector3 c = inst.Slot.transform.position;
            foreach (string p in inst.Inside)
            {
                Cue(p, c, ColosseumClient.CueBell, 1f);
                Cue(p, c, ColosseumClient.CueCountdown, countdown);
                NetworkSocial.ServerSystemMessage(p, $"{who.CharacterName} läutet die Glocke – in {Mathf.CeilToInt(countdown)} Sekunden öffnen sich die Tore!");
            }
        }

        private static ArenaBell BellOf(int slotIndex)
        {
            foreach (ArenaBell b in ArenaBell.All)
                if (b != null && b.SlotIndex == slotIndex) return b;
            return null;
        }

        private static ArenaChest ChestOf(int slotIndex)
        {
            foreach (ArenaChest c in ArenaChest.All)
                if (c != null && c.SlotIndex == slotIndex) return c;
            return null;
        }

        // ================================================================== Trupps und Champions

        private void SpawnTroop(RunState s, WaveSpawn w)
        {
            if (w.Gate < 0 || w.Gate >= s.Gates.Count || w.Pool < 0 || w.Pool >= definition.pool.Count) return;
            ArenaGate gate = s.Gates[w.Gate];
            ArenaPoolEntry entry = definition.pool[w.Pool];
            Vector3 center = s.Instance.Slot.transform.position;
            Vector3 right = gate.transform.right, back = -gate.transform.forward;
            float yaw = gate.transform.eulerAngles.y;
            for (int k = 0; k < w.Count; k++)
            {
                // Aufstellung im Torgang: Reihen zu dritt, nach hinten gestaffelt
                Vector3 pos = gate.SpawnWorld + right * ((k % 3) - 1) * 1.8f + back * (k / 3) * 1.8f;
                EnemyBrain brain = EnemyDirector.Instance.SpawnAt(entry.prefab, pos, yaw, w.Level, "arena",
                    b => b.ServerSetArenaMode(center, arenaRadius));
                if (brain == null) continue;
                s.Instance.Enemies.Add(brain);
                s.SpawnLog.Add((brain.transform.position, w.Gate, s.Elapsed));
            }
        }

        private void SpawnBoss(RunState s, ArenaBossEntry entry, int index)
        {
            if (entry == null || entry.prefab == null) return;
            ArenaGate gate = null;
            foreach (ArenaGate g in s.Gates)
                if (g.Kind == ArenaGateKind.Gate) { gate = g; break; }
            if (gate == null) return;
            Vector3 center = s.Instance.Slot.transform.position;
            int level = Mathf.Min(definition.maxLevel + 1, definition.baseLevel + entry.levelBonus);
            float healthMul = entry.healthBase + entry.healthPerGroup * definition.GroupFactor(s.Participants.Count);
            EnemyBrain boss = EnemyDirector.Instance.SpawnAt(entry.prefab, gate.SpawnWorld, gate.transform.eulerAngles.y, level, "arena-boss",
                b =>
                {
                    b.ServerSetArenaMode(center, arenaRadius);
                    b.ServerSetDamageScale(entry.damageMultiplier);
                    var h = b.GetComponent<Health>();
                    if (h != null) h.Configure(h.Max * healthMul, false);
                }, 60f);
            if (boss == null) return;
            s.Instance.Enemies.Add(boss);
            s.Bosses.Add(boss);
            boss.Killed += b => OnBossKilled(s, b, index);
            string name = boss.Definition != null ? boss.Definition.DisplayName : "Champion";
            Announce(s.Instance, $"{entry.title}: {name} (Stufe {level}) betritt den Ring!");
            CueAll(s, ColosseumClient.CueBoss, 1f);
            var bh = boss.GetComponent<Health>();
            Debug.Log($"[Ashveil] Server: Kolosseum Platz {s.Instance.Slot.Index} – Champion {index + 1} {name}, Stufe {level}, Leben ×{healthMul:0.0} ({(bh != null ? bh.Max : 0f):0}).");
        }

        private void OnBossKilled(RunState s, EnemyBrain boss, int index)
        {
            if (!_runs.ContainsValue(s) || s.Over) return;
            s.BossesKilled++;
            s.Revives += definition.revivePerBoss;
            CueAll(s, ColosseumClient.CueRevives, s.Revives);
            CueAll(s, ColosseumClient.CueBoss, AliveBoss(s) ? 1f : 0f);
            string name = boss != null && boss.Definition != null ? boss.Definition.DisplayName : "Der Champion";
            bool last = index >= Bosses().Count - 1;
            Announce(s.Instance, last ? $"{name} fällt! Die Prüfung ist bestanden – {Clock(s.Elapsed)}."
                                      : $"{name} fällt! +{definition.revivePerBoss} Wiederbelebung (jetzt {s.Revives}).");
            if (last) Win(s);
        }

        private static bool AliveBoss(RunState s)
        {
            foreach (EnemyBrain b in s.Bosses)
                if (b != null && b.GetComponent<Health>() is Health h && h != null && h.IsAlive) return true;
            return false;
        }

        // ================================================================== Pruefpunkt

        private void Win(RunState s)
        {
            s.Won = true;
            // Restliche Gegner verlassen den Ring (keine Beute), Gefallene stehen wieder auf
            var rest = new List<EnemyBrain>();
            foreach (EnemyBrain b in s.Instance.Enemies)
                if (b != null && b.GetComponent<Health>() is Health h && h != null && h.IsAlive) rest.Add(b);
            foreach (EnemyBrain b in rest) EnemyDirector.Instance.Remove(b);
            foreach (string p in s.Participants)
            {
                if (!NetworkInventory.Online.TryGetValue(p, out NetworkInventory inv) || inv == null) continue;
                var np = inv.GetComponent<NetworkPlayer>();
                if (np != null && inv.GetComponent<Health>() is Health h && h != null && !h.IsAlive) np.ServerReviveHere(1f);
                s.Down.Remove(p);
                Cue(p, s.Instance.Slot.transform.position, ColosseumClient.CueDown, 0f);
                if (s.Instance.Inside.Contains(p)) s.ChestPending.Add(p);
            }
            s.Channels.Clear();
            CueAll(s, ColosseumClient.CueEnd, 1f);
            foreach (string p in s.ChestPending) Cue(p, s.Instance.Slot.transform.position, ColosseumClient.CueChest, 1f);
            Announce(s.Instance, "Ringmeister Brannoc: „Gut gekämpft! Holt euch euren Anteil aus der Truhe.“");
        }

        /// <summary>Server: persoenliche Truhe oeffnen.</summary>
        public void ServerOpenChest(NetworkInventory who)
        {
            if (who == null) return;
            RunState s = StateOfProfile(who.ProfileId, out DungeonDirector.Instance inst);
            if (s == null || !s.ChestPending.Contains(who.ProfileId))
            {
                NetworkSocial.ServerSystemMessage(who.ProfileId, "Die Truhe ist für dich leer.");
                return;
            }
            ArenaChest chest = ChestOf(inst.Slot.Index);
            if (chest == null || (chest.transform.position - who.transform.position).sqrMagnitude > 7f * 7f)
            {
                NetworkSocial.ServerSystemMessage(who.ProfileId, "Die Truhe ist zu weit weg.");
                return;
            }
            GiveChest(s, who, false);
        }

        /// <summary>
        /// Server: Abmelden/Trennen (NetworkInventory.OnStopServer, erneute Anmeldung, Beenden) – eine offene Truhe dieses Profils
        /// auszahlen. 22.10. (STORY-03): Bisher haendigte nur OnLeave (Ausgang) den Anteil aus; wer nach dem Sieg die Verbindung verlor
        /// oder das Spiel schloss, fiel still aus Inside, die leere Arena wurde zurueckgesetzt und die Belohnung war weg. Gesucht wird
        /// ueber ChestPending (nicht ueber Inside, das der Direktor jeden Takt neu aufbaut). Still: keine Nachricht/Signale an eine
        /// Verbindung, die gerade abgebaut wird. Passt der Gegenstand nicht mehr, bleibt er in der Truhe (INSTANZEN-02).
        /// </summary>
        public void ServerOnLogout(NetworkInventory who)
        {
            if (who == null || string.IsNullOrEmpty(who.ProfileId)) return;
            foreach (RunState s in _runs.Values)
                if (s.ChestPending.Contains(who.ProfileId)) { GiveChest(s, who, true, true); return; }
        }

        /// <summary>Belohnung je Spieler: Geld, Erfahrung, ein Gegenstand gruen/blau passend zu Klasse und Stufe.</summary>
        /// <remarks>
        /// 22.10. (INSTANZEN-02): Bei vollem Rucksack ging der Gegenstand bisher endgueltig verloren (ChestPending wurde vor der
        /// Uebergabe entfernt, die Truhe war danach leer). Jetzt: Geld und Erfahrung genau einmal sofort, der Gegenstand bleibt mit
        /// Meldung in der Truhe (<see cref="RunState.ChestHeld"/>), bis Platz ist – erneut oeffnen oder ueber den Eingang zurueckkommen.
        /// </remarks>
        private void GiveChest(RunState s, NetworkInventory who, bool onLeave, bool silent = false)
        {
            string p = who.ProfileId;
            if (!s.ChestPending.Contains(p)) return;
            string head = onLeave ? "Der Ringmeister händigt dir deinen Anteil aus" : "Truhe des Ringmeisters";
            string paid = null;
            if (!s.ChestHeld.TryGetValue(p, out ItemDefinition item))
            {
                var prog = who.GetComponent<NetworkProgression>();
                int level = who.ServerModel != null ? who.ServerModel.Level : definition.baseLevel;
                int money = definition.checkpointMoney;
                int xp = Mathf.Max(1, Progression.KillXp(definition.baseLevel + 1, level)) * definition.checkpointXpKills;
                item = PickReward(who, s.Seed ^ p.GetHashCode());
                if (prog != null)
                {
                    prog.ServerSetMoney(prog.ServerMoney + money);
                    prog.AddXp(xp);
                }
                s.ChestLog[p] = $"money={money};xp={xp};item={(item != null ? item.Id : "-")}";
                paid = $"{money} Kupfer, {xp} Erfahrung";
            }
            bool given = item == null || who.ServerGive(new ItemStack(item.Id, 1)) > 0;
            who.ServerCommit();
            string text;
            if (given)
            {
                s.ChestPending.Remove(p);
                s.ChestHeld.Remove(p);
                text = paid != null ? $"{head}: {paid}{(item != null ? ", " + item.DisplayName : "")}." : $"Truhe des Ringmeisters: {item.DisplayName}.";
            }
            else
            {
                s.ChestHeld[p] = item;
                int minutes = _director != null ? Mathf.Max(1, Mathf.CeilToInt(_director.ResetAfterEmpty / 60f)) : 5;
                string held = onLeave
                    ? $"Dein Rucksack ist voll – {item.DisplayName} bleibt in der Truhe. Mach Platz und komm zurück; eine leere Arena wird nach {minutes} Minuten geräumt."
                    : $"Dein Rucksack ist voll – {item.DisplayName} bleibt in der Truhe. Mach Platz und öffne sie noch einmal.";
                text = paid != null ? $"{head}: {paid}. {held}" : held;
                Debug.Log($"[Ashveil] Server: Kolosseum – {who.CharacterName}: Rucksack voll, {item.Id} bleibt in der Truhe.");
            }
            if (silent) return;
            NetworkSocial.ServerSystemMessage(p, text);
            if (given) Cue(p, s.Instance.Slot.transform.position, ColosseumClient.CueChest, 0f);
        }

        /// <summary>Gegenstand gruen oder blau, anlegbar, fuer die Klasse erlaubt, Mindeststufe im Band der Arena.</summary>
        private ItemDefinition PickReward(NetworkInventory who, int seed)
        {
            if (GameData.Database == null) return null;
            CharacterClassDefinition cls = who.LootClass;
            int level = who.ServerModel != null ? who.ServerModel.Level : definition.baseLevel;
            var pool = new List<ItemDefinition>();
            foreach (GameDefinition g in GameData.Database.All)
            {
                if (!(g is ItemDefinition d) || !d.IsEquippable) continue;
                if (d.quality != ItemQuality.Uncommon && d.quality != ItemQuality.Rare) continue;
                if (d.bind == BindType.OnPickup || d.unique) continue; // Boss-/Questbeute bleibt, wo sie hingehoert
                if (d.requiredLevel > level || d.requiredLevel < Mathf.Min(level, definition.baseLevel) - 6) continue;
                if (cls != null && !Ashveil.Gameplay.Items.InventoryModel.UsableByClass(d, cls)) continue;
                pool.Add(d);
            }
            if (pool.Count == 0) return null;
            pool.Sort((x, y) => string.CompareOrdinal(x.Id, y.Id));
            var rng = new System.Random(seed);
            // Blau seltener (1 : 3)
            var weighted = new List<ItemDefinition>();
            foreach (ItemDefinition d in pool) for (int i = 0; i < (d.quality == ItemQuality.Rare ? 1 : 3); i++) weighted.Add(d);
            return weighted[rng.Next(weighted.Count)];
        }

        // ================================================================== Tod und Wiederbelebung

        /// <summary>Regel fuer <see cref="NetworkPlayer"/>: im laufenden Kampf liegen bleiben.</summary>
        private string HoldRespawn(NetworkPlayer player)
        {
            var inv = player.GetComponent<NetworkInventory>();
            if (inv == null || _director == null) return null;
            RunState s = StateOfProfile(inv.ProfileId, out DungeonDirector.Instance inst);
            if (s == null || !s.Started || s.Over || !s.Participants.Contains(inv.ProfileId)) return null;
            s.Down.Add(inv.ProfileId);
            CancelChannelsOf(s, inv.ProfileId, "Wiederbeleben abgebrochen – du bist selbst gefallen.");
            Cue(inv.ProfileId, inst.Slot.transform.position, ColosseumClient.CueDown, 1f);
            foreach (string p in inst.Inside)
                if (p != inv.ProfileId) NetworkSocial.ServerSystemMessage(p, $"{inv.CharacterName} ist gefallen! Stellt euch daneben und belebt sie wieder (Rechtsklick, {s.Revives} übrig).");
            return s.Revives > 0 ? "Du bist gefallen – ein Mitstreiter kann dich wiederbeleben." : "Du bist gefallen – es ist keine Wiederbelebung mehr übrig.";
        }

        /// <summary>Server: <paramref name="reviver"/> beginnt, <paramref name="target"/> wiederzubeleben (5 s, Schaden bricht ab).</summary>
        public void ServerStartRevive(NetworkInventory reviver, NetworkInventory target)
        {
            if (reviver == null || target == null || reviver == target) return;
            RunState s = StateOfProfile(reviver.ProfileId, out DungeonDirector.Instance inst);
            string why = null;
            var rh = reviver.GetComponent<Health>();
            var th = target.GetComponent<Health>();
            if (s == null || !s.Started || s.Over) why = "Hier kann niemand wiederbelebt werden.";
            else if (!inst.Inside.Contains(target.ProfileId) || !s.Down.Contains(target.ProfileId) || th == null || th.IsAlive) why = "Diese Figur liegt nicht am Boden.";
            else if (rh == null || !rh.IsAlive) why = "Du liegst selbst am Boden.";
            else if (s.Revives <= 0) why = "Es ist keine Wiederbelebung mehr übrig.";
            else if ((reviver.transform.position - target.transform.position).sqrMagnitude > definition.reviveRange * definition.reviveRange) why = "Zu weit entfernt.";
            else if (s.Channels.ContainsKey(reviver.ProfileId)) why = "Du belebst schon jemanden wieder.";
            else
                foreach (Channel ch in s.Channels.Values)
                    if (ch.Target == target.ProfileId) { why = "Jemand belebt sie schon wieder."; break; }
            if (why != null)
            {
                NetworkSocial.ServerSystemMessage(reviver.ProfileId, why);
                Debug.Log($"[Ashveil] Server: Kolosseum – {reviver.CharacterName} kann {target.CharacterName} nicht wiederbeleben: {why}");
                return;
            }
            s.Channels[reviver.ProfileId] = new Channel { Reviver = reviver.ProfileId, Target = target.ProfileId, Start = Time.time, From = reviver.transform.position };
            Cue(reviver.ProfileId, inst.Slot.transform.position, ColosseumClient.CueChannel, definition.reviveSeconds);
            NetworkSocial.ServerSystemMessage(target.ProfileId, $"{reviver.CharacterName} belebt dich wieder …");
        }

        private void TickChannels(RunState s, float now)
        {
            if (s.Channels.Count == 0) return;
            _tmp.Clear();
            _tmp.AddRange(s.Channels.Keys);
            foreach (string key in _tmp)
            {
                Channel ch = s.Channels[key];
                NetworkInventory.Online.TryGetValue(ch.Reviver, out NetworkInventory rv);
                NetworkInventory.Online.TryGetValue(ch.Target, out NetworkInventory tg);
                Health rh = rv != null ? rv.GetComponent<Health>() : null;
                Health th = tg != null ? tg.GetComponent<Health>() : null;
                string why = null;
                if (s.Over) why = "";
                else if (rv == null || tg == null || rh == null || th == null) why = "Wiederbeleben abgebrochen.";
                else if (!rh.IsAlive) why = "Wiederbeleben abgebrochen – du bist selbst gefallen.";
                else if (th.IsAlive) why = "";
                else if (rh.LastDamageTime > ch.Start) why = "Wiederbeleben abgebrochen – du wurdest getroffen!";
                else if ((rv.transform.position - ch.From).sqrMagnitude > 1.5f * 1.5f ||
                         (rv.transform.position - tg.transform.position).sqrMagnitude > (definition.reviveRange + 0.5f) * (definition.reviveRange + 0.5f))
                    why = "Wiederbeleben abgebrochen – du hast dich bewegt.";
                if (why != null)
                {
                    s.Channels.Remove(key);
                    Cue(ch.Reviver, s.Instance.Slot.transform.position, ColosseumClient.CueChannel, 0f);
                    if (why.Length > 0) NetworkSocial.ServerSystemMessage(ch.Reviver, why);
                    continue;
                }
                if (now - ch.Start < definition.reviveSeconds) continue;
                s.Channels.Remove(key);
                Cue(ch.Reviver, s.Instance.Slot.transform.position, ColosseumClient.CueChannel, 0f);
                if (s.Revives <= 0) { NetworkSocial.ServerSystemMessage(ch.Reviver, "Es ist keine Wiederbelebung mehr übrig."); continue; }
                s.Revives--;
                s.Down.Remove(ch.Target);
                var np = tg.GetComponent<NetworkPlayer>();
                if (np != null) np.ServerReviveHere(definition.reviveHealth);
                Cue(ch.Target, s.Instance.Slot.transform.position, ColosseumClient.CueDown, 0f);
                CueAll(s, ColosseumClient.CueRevives, s.Revives);
                Announce(s.Instance, $"{rv.CharacterName} hat {tg.CharacterName} wiederbelebt ({s.Revives} übrig).");
            }
        }

        private void CancelChannelsOf(RunState s, string profile, string message)
        {
            _tmp.Clear();
            foreach (KeyValuePair<string, Channel> kv in s.Channels)
                if (kv.Value.Reviver == profile || kv.Value.Target == profile) _tmp.Add(kv.Key);
            foreach (string key in _tmp)
            {
                Channel ch = s.Channels[key];
                s.Channels.Remove(key);
                Cue(ch.Reviver, s.Instance.Slot.transform.position, ColosseumClient.CueChannel, 0f);
                if (message != null && ch.Reviver == profile) NetworkSocial.ServerSystemMessage(ch.Reviver, message);
                else if (ch.Target == profile && ch.Reviver != profile) NetworkSocial.ServerSystemMessage(ch.Reviver, "Wiederbeleben abgebrochen.");
            }
        }

        // ================================================================== Wipe

        private void CheckWipe(RunState s, float now)
        {
            int standing = 0, present = 0;
            foreach (string p in s.Participants)
            {
                if (!s.Instance.Inside.Contains(p)) continue;
                if (!NetworkInventory.Online.TryGetValue(p, out NetworkInventory inv) || inv == null) continue;
                present++;
                if (inv.GetComponent<Health>() is Health h && h != null && h.IsAlive) standing++;
            }
            if (present == 0 || standing > 0) return;
            s.Lost = true;
            s.Channels.Clear();
            float delay = WipeDelayOverride >= 0f ? WipeDelayOverride : 6f;
            s.WipeAt = now + delay;
            CueAll(s, ColosseumClient.CueEnd, 2f);
            CueAll(s, ColosseumClient.CueBoss, 0f);
            Announce(s.Instance, $"Der Ring hat euch besiegt – {Clock(s.Elapsed)}. Die Wärter tragen euch hinaus.");
            Debug.Log($"[Ashveil] Server: Kolosseum Platz {s.Instance.Slot.Index} – Wipe nach {Clock(s.Elapsed)} ({s.BossesKilled} Champions).");
        }

        private void FinishWipe(RunState s)
        {
            s.WipeAt = -1f;
            var inside = new List<string>(s.Instance.Inside);
            foreach (string p in inside)
                if (NetworkInventory.Online.TryGetValue(p, out NetworkInventory inv) && inv != null) ReturnToGate(inv);
            if (_director != null) _director.ResetInstance(s.Instance);
        }

        /// <summary>Vor das Tor bringen und voll wiederbeleben.</summary>
        private void ReturnToGate(NetworkInventory inv)
        {
            var np = inv.GetComponent<NetworkPlayer>();
            if (_director != null && _director.TryGetReturn(out Vector3 pos, out float yaw))
            {
                if (np != null) np.ServerRespawnAt(pos, yaw);
                _director.ServerLeave(inv);
            }
            else if (np != null) np.ServerRespawnAt(inv.transform.position, 0f);
        }

        // ================================================================== Signale und Texte

        /// <summary>Signal an einen Spieler; der Host-Spieler bekommt es direkt (kein TargetRpc an sich selbst).</summary>
        private static void Cue(string profile, Vector3 where, string cue, float value)
        {
            if (profile == null) return;
            if (NetworkInventory.Online.TryGetValue(profile, out NetworkInventory inv) && inv != null && inv.IsOwner)
                Ashveil.Core.EncounterCues.Raise(where, cue, value);
            else NetworkSocial.ServerCue(profile, where, cue, value);
        }

        private static void CueAll(RunState s, string cue, float value)
        {
            Vector3 c = s.Instance.Slot.transform.position;
            foreach (string p in s.Instance.Inside) Cue(p, c, cue, value);
        }

        private void Announce(DungeonDirector.Instance instance, string text)
        {
            foreach (string profile in instance.Inside) NetworkSocial.ServerSystemMessage(profile, text);
        }

        private string PhaseName(int phase) =>
            definition != null && phase >= 0 && phase < definition.phases.Count ? definition.phases[phase].displayName : "Prüfung";

        public static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{s / 60:00}:{s % 60:00}";
        }
    }
}
