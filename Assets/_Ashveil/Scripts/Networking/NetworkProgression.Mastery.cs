using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Mastery;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Praxis-Meisterschaften (Vorgabe 28.09.), erster Prototyp: Laufkunst und Ausweichen. Alles serverautoritaer (§55):
    ///
    ///   Laufkunst  – der Server misst einmal pro Sekunde die echte Strecke der Figur (keine Eingaben, keine Animation, §61).
    ///                Teleports, Tod und unmoegliche Spruenge zaehlen nicht (§62). Wer im Kreis laeuft oder gegen die Wand,
    ///                lernt kaum etwas: ueber 20 s zaehlt das Verhaeltnis Luftlinie zu Weg (§60).
    ///   Ausweichen – eine Rolle zaehlt nur, wenn in dem Moment ein Gegnerangriff auf diese Figur ausgeholt wurde und
    ///                danach nicht getroffen hat (§22/§23). Schwache Gegner geben weniger bis nichts (§21), mehr als
    ///                12 Treffer-Rollen pro Minute zaehlen halb (§66). Leere Rollen geben nichts (§63).
    ///
    /// Wirkungen gehen in den Bewegungsmotor (<see cref="CharacterMotor.SetMastery"/>) und reisen mit dem Abgleich zum
    /// Besitzer. Der Besitzer bekommt seine Werte per TargetRpc (§107/§108), gespeichert wird im normalen Takt (§110).
    /// </summary>
    public sealed partial class NetworkProgression
    {
        private const int MasteryWindow = 20;
        private const float CombatLinger = 5f;
        private const float MinWindowEfficiency = 0.4f;

        private struct PendingDodge
        {
            public int DodgeId;
            public EnemyBrain Brain;
            public float Since;
            public float ResolveAt;
            public int Level;
        }

        private CharacterMotor _mMotor;
        private NetworkCombat _mCombat;
        private bool _mHooked;
        private Vector3 _mLastPos;
        private int _mLastTeleports;
        private float _mSampleTimer, _mCombatTimer, _mSendTimer, _mSaveTimer;
        private bool _mSendPending, _mSaveDirty, _mInCombat;
        private readonly Queue<Vector3> _mPositions = new Queue<Vector3>();
        private readonly Queue<float> _mSegments = new Queue<float>();
        private float _mWindowPath;
        private readonly List<PendingDodge> _mDodges = new List<PendingDodge>();
        private readonly HashSet<int> _mCredited = new HashSet<int>();
        private readonly Dictionary<EnemyBrain, float> _mHitBy = new Dictionary<EnemyBrain, float>();
        private readonly Queue<float> _mCreditTimes = new Queue<float>();
        private int _mDodgeCounter;

        // ================================================================== Server: Abfragen (auch fuer Tests)

        public double ServerMasteryPoints(string id)
        {
            MasterySave m = Loaded ? FindMastery(id, false) : null;
            return m != null ? m.points : 0;
        }

        public int ServerMasteryRank(string id)
        {
            MasteryDefinition d = MasteryCatalog.Get(id);
            return d != null ? d.RankFor(ServerMasteryPoints(id)) : 0;
        }

        public double ServerMetersOnFoot => Loaded && D.lifetime != null ? D.lifetime.metersOnFoot : 0;
        public int ServerMeaningfulDodges => Loaded && D.lifetime != null ? D.lifetime.meaningfulDodges : 0;
        public int ServerEmptyDodges => Loaded && D.lifetime != null ? D.lifetime.emptyDodges : 0;
        public int ServerBlocks => Loaded && D.lifetime != null ? D.lifetime.blocks : 0;
        public double ServerDamageBlocked => Loaded && D.lifetime != null ? D.lifetime.damageBlocked : 0;
        public bool ServerInCombat => _mInCombat;
        /// <summary>Test/Diagnose: warum der Server den Spieler zuletzt als "im Kampf" sah.</summary>
        public string ServerCombatReason { get; private set; } = "-";
        /// <summary>Test: Wirkungsgrad der letzten Laufprobe (1 = gerade Strecke, 0 = im Kreis/auf der Stelle).</summary>
        public float ServerLastWalkEfficiency { get; private set; } = 1f;

        /// <summary>Server (Tests/Werkzeug): Lernpunkte direkt setzen.</summary>
        public void ServerSetMasteryPoints(string id, double points)
        {
            if (!Loaded) return;
            MasterySave m = FindMastery(id, true);
            m.points = System.Math.Max(0, points);
            ApplyMasteryEffects();
            _mSaveDirty = true;
            SendMastery();
        }

        /// <summary>Server (Tests): eine Laufprobe von aussen einspeisen (Kreis, Wand) – dieselbe Rechnung wie die echte Messung.</summary>
        public void ServerFeedWalkSample(Vector3 position) => SampleDistance(position, _mMotor != null ? _mMotor.TeleportCount : 0, 1f);

        /// <summary>Server (Tests): Laufmessfenster leeren und an diese Stelle setzen.</summary>
        public void ServerResetWalkWindow(Vector3 position)
        {
            ClearWindow();
            _mLastPos = position;
            if (_mMotor != null) _mLastTeleports = _mMotor.TeleportCount;
        }

        // ================================================================== Server: Ablauf

        private void MasteryLogin()
        {
            if (D.mastery == null) D.mastery = new List<MasterySave>();
            if (D.lifetime == null) D.lifetime = new LifetimeStats();
            _mMotor = GetComponent<CharacterMotor>();
            _mCombat = GetComponent<NetworkCombat>();
            if (!_mHooked)
            {
                if (_mMotor != null) _mMotor.Dodged += OnMasteryDodged;
                if (_health != null) _health.Damaged += OnMasteryDamaged;
                _mHooked = true;
            }
            ServerResetWalkWindow(transform.position);
            _mDodges.Clear();
            _mCredited.Clear();
            _mHitBy.Clear();
            ApplyMasteryEffects();
            SendMastery();
        }

        private void MasteryStopServer()
        {
            if (!_mHooked) return;
            if (_mMotor != null) _mMotor.Dodged -= OnMasteryDodged;
            if (_health != null) _health.Damaged -= OnMasteryDamaged;
            _mHooked = false;
        }

        private void MasteryUpdate()
        {
            if (_mMotor == null || !_mHooked) return;
            float dt = Time.deltaTime, now = Time.time;

            _mCombatTimer -= dt;
            if (_mCombatTimer <= 0f)
            {
                _mCombatTimer = 0.25f;
                bool c = ComputeInCombat(now);
                if (c != _mInCombat)
                {
                    _mInCombat = c;
                    _mMotor.SetInCombat(c);
                }
            }

            _mSampleTimer -= dt;
            if (_mSampleTimer <= 0f)
            {
                float interval = 1f - _mSampleTimer; // echte Zeit seit der letzten Probe
                _mSampleTimer = 1f;
                SampleDistance(transform.position, _mMotor.TeleportCount, Mathf.Max(0.5f, interval));
            }

            for (int i = _mDodges.Count - 1; i >= 0; i--)
            {
                PendingDodge p = _mDodges[i];
                if (now < p.ResolveAt) continue;
                _mDodges.RemoveAt(i);
                bool hit = p.Brain != null && _mHitBy.TryGetValue(p.Brain, out float t) && t >= p.Since;
                if (!hit && p.Brain != null && !_mCredited.Contains(p.DodgeId))
                {
                    _mCredited.Add(p.DodgeId);
                    CreditDodge(p.Level, now);
                }
            }

            if (_mSendPending)
            {
                _mSendTimer -= dt;
                if (_mSendTimer <= 0f) SendMastery();
            }
            if (_mSaveDirty)
            {
                _mSaveTimer -= dt;
                if (_mSaveTimer <= 0f)
                {
                    _mSaveTimer = 30f; // Buendeln: nicht fuer jeden Meter speichern (§57/§110); Abmelden speichert ohnehin
                    _mSaveDirty = false;
                    _inv.ServerMarkDirty();
                }
            }
        }

        private bool ComputeInCombat(float now)
        {
            if (_health != null && now - _health.LastDamageTime < CombatLinger) { ServerCombatReason = $"Treffer vor {now - _health.LastDamageTime:0.0} s"; return true; }
            if (_mCombat != null && now - _mCombat.ServerLastAttackTime < CombatLinger) { ServerCombatReason = $"eigener Angriff vor {now - _mCombat.ServerLastAttackTime:0.0} s"; return true; }
            foreach (EnemyBrain b in EnemyBrain.All)
                if (b != null && b.IsActive && b.InCombat && b.Target == _health)
                {
                    ServerCombatReason = $"Gegner {b.name} ({Vector3.Distance(b.transform.position, transform.position):0} m)";
                    return true;
                }
            ServerCombatReason = "-";
            return false;
        }

        // ---------------------------------------------------------------- Laufkunst

        private void SampleDistance(Vector3 p, int teleports, float interval)
        {
            Vector3 d = p - _mLastPos;
            d.y = 0f;
            float seg = d.magnitude;
            float cap = _mMotor.Settings.sprintSpeed * 1.6f * interval;
            bool dead = _health != null && !_health.IsAlive;
            if (teleports != _mLastTeleports || dead || seg > cap)
            {
                // Teleport, Wiederbeleben, Instanz, Tod: diese Strecke ist keine eigene Bewegung
                ServerResetWalkWindow(p);
                return;
            }
            _mLastPos = p;
            _mPositions.Enqueue(p);
            _mSegments.Enqueue(seg);
            _mWindowPath += seg;
            while (_mPositions.Count > MasteryWindow)
            {
                _mPositions.Dequeue();
                _mWindowPath -= _mSegments.Dequeue();
            }
            if (seg < 0.05f) return;

            float eff = 1f;
            if (_mPositions.Count >= 6 && _mWindowPath > 8f)
            {
                Vector3 first = _mPositions.Peek();
                Vector3 net = p - first;
                net.y = 0f;
                eff = Mathf.Clamp01(net.magnitude / Mathf.Max(0.01f, _mWindowPath) / MinWindowEfficiency);
            }
            ServerLastWalkEfficiency = eff;
            // Im Sattel waechst die Bindung zum Reittier, nicht die Laufkunst (Laufkunst = nur zu Fuss)
            ClassMountDefinition mount = _mMotor != null && _mMotor.IsMounted ? ServerMountDefinition : null;
            if (mount != null)
            {
                D.lifetime.metersRidden += seg;
                AddMasteryPoints(mount.bond, seg * eff);
                return;
            }
            D.lifetime.metersOnFoot += seg;
            AddMasteryPoints(MasteryCatalog.Running, seg * eff);
        }

        private void ClearWindow()
        {
            _mPositions.Clear();
            _mSegments.Clear();
            _mWindowPath = 0f;
        }

        // ---------------------------------------------------------------- Ausweichen

        private void OnMasteryDodged(Vector3 direction)
        {
            if (!IsServerInitialized || !Loaded) return;
            float now = Time.time;
            int id = ++_mDodgeCounter;
            int found = 0;
            foreach (EnemyBrain b in EnemyBrain.All)
            {
                if (b == null || !b.IsActive) continue;
                if ((b.transform.position - transform.position).sqrMagnitude > 30f * 30f) continue;
                if (!b.Threatens(_health, out float left)) continue;
                _mDodges.Add(new PendingDodge { DodgeId = id, Brain = b, Since = now, ResolveAt = now + left + 0.35f, Level = b.Level });
                found++;
            }
            if (found == 0) D.lifetime.emptyDodges++;
        }

        private void OnMasteryDamaged(DamageInfo info)
        {
            if (!IsServerInitialized || info.Source == null) return;
            EnemyBrain b = info.Source.GetComponentInParent<EnemyBrain>();
            // Max 30.09.: passives Ausweichen (WoW Classic) zaehlt fuer die Meisterschaft „Ausweichen“ – fuer alle Klassen
            if (info.Dodged)
            {
                if (b != null && Loaded) CreditDodge(b.Level, Time.time);
                return;
            }
            if (b != null) _mHitBy[b] = Time.time;
            // Schildblock: gelernt wird nur an Gegnern (kein Blocken-Farmen unter Spielern), mit Stufenabschlag
            if (b != null && info.Blocked > 0f && Loaded)
            {
                D.lifetime.blocks++;
                D.lifetime.damageBlocked += info.Blocked;
                float factor = MasteryCatalog.LevelFactor(b.Level, ServerLevel);
                if (factor > 0f) AddMasteryPoints(MasteryCatalog.Blocking, info.Blocked * factor);
                else { _mSaveDirty = true; if (!_mSendPending) { _mSendPending = true; _mSendTimer = 5f; } }
            }
        }

        private void CreditDodge(int enemyLevel, float now)
        {
            while (_mCreditTimes.Count > 0 && now - _mCreditTimes.Peek() > 60f) _mCreditTimes.Dequeue();
            float factor = MasteryCatalog.LevelFactor(enemyLevel, ServerLevel);
            if (_mCreditTimes.Count >= 12) factor *= 0.5f;
            _mCreditTimes.Enqueue(now);
            D.lifetime.meaningfulDodges++;
            if (factor > 0f) AddMasteryPoints(MasteryCatalog.Dodging, factor);
            _mSendPending = true;
            if (_mSendTimer <= 0f || _mSendTimer > 1f) _mSendTimer = 1f;
        }

        // ---------------------------------------------------------------- Gemeinsam

        private MasterySave FindMastery(string id, bool create)
        {
            foreach (MasterySave m in D.mastery) if (m != null && m.id == id) return m;
            if (!create) return null;
            var n = new MasterySave { id = id, points = 0 };
            D.mastery.Add(n);
            return n;
        }

        private void AddMasteryPoints(MasteryDefinition def, double points)
        {
            if (points <= 0) return;
            MasterySave m = FindMastery(def.id, true);
            int before = def.RankFor(m.points);
            m.points += points;
            int after = def.RankFor(m.points);
            _mSaveDirty = true;
            if (!_mSendPending) { _mSendPending = true; _mSendTimer = 5f; } // Fortschrittsbalken hoechstens alle 5 s (§51)
            if (after == before) return;
            ApplyMasteryEffects();
            if (ServerMountDefinition != null && def.id == ServerMountDefinition.bond.id) MountBondChanged();
            SendMastery();
            _inv.ServerMarkDirty();
            // Meilensteine: kleine Meldung alle 10 Raenge (keine Dauer-Popups, §51/§52)
            if (after / def.milestoneEvery > before / def.milestoneEvery && Owner != null && Owner.IsActive)
            {
                int milestone = after / def.milestoneEvery * def.milestoneEvery;
                TargetMilestone(Owner, def.id, milestone);
            }
        }

        private void ApplyMasteryEffects()
        {
            if (_mMotor == null || !Loaded) return;
            int run = MasteryCatalog.Running.RankFor(ServerMasteryPoints(MasteryCatalog.RunningId));
            int dodge = MasteryCatalog.Dodging.RankFor(ServerMasteryPoints(MasteryCatalog.DodgingId));
            _mMotor.SetMastery(MasteryCatalog.Running.Effect("travel", run), MasteryCatalog.Running.Effect("sprintCost", run),
                MasteryCatalog.Dodging.Effect("dodgeCost", dodge), MasteryCatalog.Dodging.Effect("dodgeRecovery", dodge));
            var equip = GetComponent<Ashveil.Gameplay.Items.CharacterEquipment>();
            int block = MasteryCatalog.Blocking.RankFor(ServerMasteryPoints(MasteryCatalog.BlockingId));
            if (equip != null) equip.SetBlockMastery(MasteryCatalog.Blocking.Effect("blockChance", block), MasteryCatalog.Blocking.Effect("blockValue", block));
        }

        private void SendMastery()
        {
            _mSendPending = false;
            _mSendTimer = 0f;
            if (!Loaded || Owner == null || !Owner.IsActive) return;
            var ids = new string[D.mastery.Count];
            var pts = new double[D.mastery.Count];
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = D.mastery[i].id;
                pts[i] = D.mastery[i].points;
            }
            TargetMastery(Owner, ids, pts, D.lifetime.metersOnFoot, D.lifetime.meaningfulDodges, D.lifetime.damageBlocked);
        }

        [TargetRpc]
        private void TargetMilestone(NetworkConnection conn, string id, int rank) => MasteryClient.RaiseMilestone(id, rank);

        [TargetRpc]
        private void TargetMastery(NetworkConnection conn, string[] ids, double[] points, double meters, int dodges, double blocked)
        {
            MasteryClient.Apply(ids, points, meters, dodges, blocked);
            // Eigene Vorhersage sofort mitziehen: der Server-Abgleich kommt erst, wenn man sich bewegt – so gibt es
            // beim ersten Schritt nach einem neuen Rang kein Zurueckruckeln.
            if (IsServerInitialized) return;
            var motor = GetComponent<CharacterMotor>();
            if (motor == null) return;
            int run = MasteryClient.Rank(MasteryCatalog.RunningId), dodge = MasteryClient.Rank(MasteryCatalog.DodgingId);
            motor.SetMastery(MasteryCatalog.Running.Effect("travel", run), MasteryCatalog.Running.Effect("sprintCost", run),
                MasteryCatalog.Dodging.Effect("dodgeCost", dodge), MasteryCatalog.Dodging.Effect("dodgeRecovery", dodge));
        }
    }
}
