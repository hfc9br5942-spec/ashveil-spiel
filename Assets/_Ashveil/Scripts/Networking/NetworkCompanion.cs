using System.Collections;
using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Companions;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using Ashveil.World;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.AI;

namespace Ashveil.Networking
{
    /// <summary>
    /// Gefaehrte des Dominators (Klassen-Identitaet §58–§91, Prototyp §185: Aschwolf). Eigenes Netzwerk-Objekt, dem
    /// Besitzer gehoerend, mit vereinfachter Verbuendeten-KI (§65/§179) – der Spieler spielt seine Figur, nicht zwei:
    ///
    ///   Folgen   – laeuft hinter dem Besitzer und verteidigt ihn von selbst: greift an, was den Besitzer oder ihn angreift
    ///              oder womit der Besitzer kaempft.
    ///   Angreifen– das Ziel des Besitzers, bis es faellt.   Bleiben – haelt die Stelle, beisst nur, was in Reichweite ist.
    ///   Passiv   – kaempft nie.
    ///
    /// Basisangriff + eine Signatur (Sprungbiss: Sprung aus 4–10 m, Biss, Blutung) + ein Zug (Hetzjaeger) – §66–§68.
    /// Freundlich: Spieler koennen ihn nicht treffen; Gegner greifen ihn an, sobald er sie beisst. Beute, Erfahrung und
    /// Auftraege laufen ueber den Besitzer (<see cref="ILootRecipient"/>). Faellt er, ist er besiegt, nicht tot (§83–§85):
    /// nach dem Kampf rappelt er sich wieder auf.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public sealed class NetworkCompanion : NetworkBehaviour, ILootRecipient
    {
        private readonly SyncVar<string> _species = new SyncVar<string>("");
        private readonly SyncVar<string> _petName = new SyncVar<string>("");
        private readonly SyncVar<string> _ownerName = new SyncVar<string>("");
        private readonly SyncVar<byte> _level = new SyncVar<byte>(1);
        private readonly SyncVar<byte> _command = new SyncVar<byte>(0);
        private readonly SyncVar<bool> _downed = new SyncVar<bool>(false);

        private Health _health;
        private CombatTarget _combatTarget;
        private EnemyVisual _visual;
        private NavMeshAgent _agent;
        private WorldLabel _plate;

        // ------------------------------------------------------------------ Server
        private NetworkProgression _owner;
        private NetworkInventory _ownerInv;
        private Health _ownerHealth;
        private CharacterMotor _ownerMotor;
        private CompanionSpecies _spec;
        private Health _foe, _ordered;
        private Vector3 _stayPos, _lastOwnerPos;
        private float _attackCd, _windup = -1f, _leapCd, _think, _downSince, _quietSince, _ownerSpeed;
        private int _ownerTeleports;
        private bool _leaping;
        private readonly HashSet<Health> _engaged = new HashSet<Health>();

        /// <summary>Server: Schaden am Gegner (Ziel, Menge) – fuer die Bindung.</summary>
        public event System.Action<Health, float> DealtDamage;
        /// <summary>Server: ein Gegner, an dem der Gefaehrte beteiligt war, ist gefallen.</summary>
        public event System.Action<EnemyBrain> FoeFell;

        public CompanionSpecies Species => _spec ?? CompanionCatalog.Get(_species.Value);
        public CompanionCommand Command => (CompanionCommand)_command.Value;
        public bool Downed => _downed.Value;
        public int Level => _level.Value;
        public Health Health => _health;
        public Health ServerFoe => _foe;
        public string DisplayName => string.IsNullOrEmpty(_petName.Value) ? (Species != null ? Species.displayName : "Gefährte") : _petName.Value;
        /// <summary>Server: Besitzer (fuer Tests und Werkzeuge).</summary>
        public NetworkProgression ServerOwner => _owner;
        public static readonly List<NetworkCompanion> All = new List<NetworkCompanion>();

        // ILootRecipient: was der Gefaehrte beitraegt, zaehlt fuer seinen Besitzer (Beute, Erfahrung, Auftraege)
        public string ProfileId => _ownerInv != null ? _ownerInv.ProfileId : null;
        public CharacterClassDefinition LootClass => _ownerInv != null ? _ownerInv.LootClass : null;
        public bool IsReady => _ownerInv != null && _ownerInv.IsReady && IsServerInitialized;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _combatTarget = GetComponent<CombatTarget>();
            _visual = GetComponent<EnemyVisual>();
            _agent = GetComponent<NavMeshAgent>();
            _plate = GetComponentInChildren<WorldLabel>(true);
            _species.OnChange += (_, __, ___) => ApplyLabels();
            _petName.OnChange += (_, __, ___) => ApplyLabels();
            _ownerName.OnChange += (_, __, ___) => ApplyLabels();
            _level.OnChange += (_, __, ___) => { ApplyLabels(); NotifyOwnerClient(); };
            _command.OnChange += (_, __, ___) => NotifyOwnerClient();
            _downed.OnChange += (_, __, ___) => NotifyOwnerClient();
        }

        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() => All.Remove(this);

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyLabels();
            if (IsOwner) CompanionClient.SetPet(_health);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (IsOwner && CompanionClient.PetHealth == _health) CompanionClient.SetPet(null);
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            if (_health != null) { _health.Died -= OnServerDied; _health.Damaged -= OnServerDamaged; }
        }

        private void NotifyOwnerClient()
        {
            if (IsServerInitialized && _owner != null) _owner.CompanionChanged();
        }

        private void ApplyLabels()
        {
            CompanionSpecies s = Species;
            string name = DisplayName;
            if (_combatTarget != null)
                _combatTarget.Configure(name, _level.Value, Faction.Friendly, new Vector3(0f, 0.7f, 0.1f), 0.9f, false, "");
            if (_plate != null)
                _plate.Configure(name, string.IsNullOrEmpty(_ownerName.Value) ? "" : _ownerName.Value + "s Gefährte", WorldLabelStyle.Friendly, 28f);
        }

        // ================================================================== Server: Einrichten

        /// <summary>Server, direkt nach dem Spawnen: Besitzer, Art, Name, Lebensanteil.</summary>
        public void ServerInit(NetworkProgression owner, CompanionSpecies spec, string petName, string ownerName, float healthFraction)
        {
            _owner = owner;
            _ownerInv = owner.GetComponent<NetworkInventory>();
            _ownerHealth = owner.GetComponent<Health>();
            _ownerMotor = owner.GetComponent<CharacterMotor>();
            _ownerTeleports = _ownerMotor != null ? _ownerMotor.TeleportCount : 0;
            _spec = spec;
            _species.Value = spec.id;
            _petName.Value = petName ?? "";
            _ownerName.Value = ownerName ?? "";
            _command.Value = (byte)CompanionCommand.Follow;
            _downed.Value = false;
            _health.Died += OnServerDied;
            _health.Damaged += OnServerDamaged;
            _leapCd = 2f;
            ApplyStats(true, Mathf.Clamp01(healthFraction));
            if (_agent != null)
            {
                _agent.enabled = true;
                _agent.speed = spec.runSpeed;
                _agent.stoppingDistance = 0.2f;
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 6f, NavMesh.AllAreas)) _agent.Warp(hit.position);
            }
            _lastOwnerPos = owner.transform.position;
            ApplyLabels();
            ObserversHowl();
        }

        /// <summary>Stufe = Stufe des Besitzers, Leben nach Stufe und Bindung (§80).</summary>
        public void ApplyStats(bool first, float fraction = 1f)
        {
            if (_spec == null || _owner == null) return;
            int level = Mathf.Clamp(_owner.ServerLevel, 1, 255);
            float max = _spec.HealthAt(level, _owner.ServerCompanionBondRank);
            if (first)
            {
                _health.Configure(max, false);
                // Configure meldet nichts – zweimal setzen, damit NetworkHealth Hoechst- und Istwert sicher uebertraegt
                float cur = Mathf.Max(1f, Mathf.Round(max * (fraction > 0f ? fraction : 1f)));
                _health.SetCurrentFromNetwork(cur - 0.5f);
                _health.SetCurrentFromNetwork(cur);
            }
            else if (!Mathf.Approximately(_health.Max, max)) _health.SetMaxHealth(max);
            _level.Value = (byte)level;
        }

        public void ServerSetCommand(CompanionCommand cmd, Health ordered)
        {
            _command.Value = (byte)cmd;
            if (cmd == CompanionCommand.Stay) _stayPos = transform.position;
            if (cmd == CompanionCommand.Attack)
            {
                _ordered = ordered != null && ordered.IsAlive ? ordered : null;
                if (_ordered == null) _command.Value = (byte)CompanionCommand.Follow;
                else _foe = _ordered;
            }
            else _ordered = null;
            if (cmd == CompanionCommand.Passive) _foe = null;
        }

        public void ServerDespawn()
        {
            if (IsServerInitialized && NetworkObject != null && NetworkObject.IsSpawned) ServerManager.Despawn(gameObject);
        }

        /// <summary>Server (Tests): sofort neben den Besitzer setzen.</summary>
        public void ServerWarpToOwner() => WarpNearOwner();

        private void OnServerDamaged(DamageInfo info)
        {
            _quietSince = Time.time;
            // Wer mich beisst, wird mein Gegner (ausser passiv oder schon im Kampf)
            if (Command == CompanionCommand.Passive || _foe != null || info.Source == null) return;
            Health attacker = info.Source.GetComponentInParent<Health>();
            if (attacker != null && attacker != _health && attacker.GetComponentInParent<EnemyBrain>() != null) _foe = attacker;
        }

        private void OnServerDied()
        {
            _downed.Value = true;
            _downSince = _quietSince = Time.time;
            _foe = _ordered = null;
            _windup = -1f;
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.isStopped = true;
            if (_owner != null) _owner.CompanionMessage($"{DisplayName} ist besiegt. Nach dem Kampf rappelt er sich wieder auf.");
        }

        // ================================================================== Server: Takt

        private void Update()
        {
            if (!IsServerInitialized || _spec == null) return;
            if (_owner == null || _ownerInv == null || !_ownerInv.IsReady) { ServerDespawn(); return; }
            float dt = Time.deltaTime;
            _attackCd -= dt;
            _leapCd -= dt;
            if (_downed.Value) { DownedTick(); return; }
            if (_leaping) return;
            if (_windup > 0f)
            {
                _windup -= dt;
                Face(_foe, dt);
                if (_windup <= 0f) ResolveBite();
                return;
            }
            _think -= dt;
            if (_think > 0f) return;
            _think = 0.1f;
            Think();
        }

        private void Think()
        {
            Vector3 ownerPos = _owner.transform.position;
            Vector3 d0 = ownerPos - _lastOwnerPos; d0.y = 0f;
            _ownerSpeed = Mathf.Lerp(_ownerSpeed, d0.magnitude / 0.1f, 0.3f);
            _lastOwnerPos = ownerPos;

            // Besitzer teleportiert, weit weg oder in einer anderen Instanz: nachkommen (kein Suchen, kein Haengenbleiben)
            if ((_ownerMotor != null && _ownerMotor.TeleportCount != _ownerTeleports) || Flat(ownerPos - transform.position) > 45f ||
                DungeonSlot.At(ownerPos) != DungeonSlot.At(transform.position))
            {
                WarpNearOwner();
                return;
            }
            if (_level.Value != Mathf.Clamp(_owner.ServerLevel, 1, 255)) ApplyStats(false);

            CheckFallenFoes();
            CompanionCommand cmd = Command;
            if (_foe != null && (!_foe.IsAlive || !_foe.isActiveAndEnabled || Flat(_foe.transform.position - ownerPos) > 35f)) _foe = null;
            if (_ordered != null && !_ordered.IsAlive) { _ordered = null; if (cmd == CompanionCommand.Attack) { _command.Value = (byte)CompanionCommand.Follow; cmd = CompanionCommand.Follow; } }
            if (cmd == CompanionCommand.Passive) _foe = null;
            else if (cmd == CompanionCommand.Attack && _ordered != null) _foe = _ordered;
            else if (_foe == null) _foe = PickFoe(cmd);

            if (_foe != null)
            {
                _engaged.Add(_foe);
                Fight(cmd);
            }
            else Idle(cmd);
        }

        /// <summary>Wen verteidigen? Gegner, die den Besitzer oder mich angreifen oder mit denen der Besitzer kaempft.</summary>
        private Health PickFoe(CompanionCommand cmd)
        {
            string profile = ProfileId;
            Health best = null;
            float bestD = float.MaxValue;
            Vector3 me = transform.position, ownerPos = _owner.transform.position;
            foreach (EnemyBrain b in EnemyBrain.All)
            {
                if (b == null || !b.IsActive || b.Health == null || !b.Health.IsAlive || !b.InCombat) continue;
                if (Flat(b.transform.position - ownerPos) > 30f) continue;
                bool threat = b.Target == _ownerHealth || b.Target == _health;
                if (!threat && profile != null)
                {
                    var elig = b.GetComponent<LootEligibility>();
                    threat = elig != null && elig.Contains(profile);
                }
                if (!threat) continue;
                float d = Flat(b.transform.position - me);
                if (cmd == CompanionCommand.Stay && d > _spec.attackRange + 1.2f) continue;
                if (d < bestD) { bestD = d; best = b.Health; }
            }
            return best;
        }

        private void Fight(CompanionCommand cmd)
        {
            Vector3 fp = _foe.transform.position;
            float d = Flat(fp - transform.position);
            float reach = _spec.attackRange + 0.5f;
            if (cmd == CompanionCommand.Stay && d > reach + 0.6f) { Stop(); return; }
            if (cmd != CompanionCommand.Stay && _leapCd <= 0f && d >= _spec.leapMin && d <= _spec.leapMax)
            {
                StartCoroutine(Leap());
                return;
            }
            if (d > reach)
            {
                MoveTo(fp, _spec.runSpeed);
                return;
            }
            Stop();
            if (_attackCd <= 0f)
            {
                _windup = _spec.windup;
                _attackCd = _spec.attackInterval + _spec.windup;
                ObserversAttack(fp + Vector3.up * 0.8f, _spec.windup);
            }
        }

        private void ResolveBite()
        {
            _windup = -1f;
            if (_foe == null || !_foe.IsAlive) { ObserversBite(false, transform.position); return; }
            Vector3 fp = _foe.transform.position;
            if (Flat(fp - transform.position) > (_spec.attackRange + 0.5f) * 1.25f) { ObserversBite(false, fp); return; }
            Hit(Roll(1f), fp + Vector3.up * 0.7f, "");
            ObserversBite(true, fp + Vector3.up * 0.7f);
        }

        private float Roll(float factor)
        {
            float dmg = _spec.DamageAt(_level.Value) * Random.Range(0.85f, 1.15f) * factor *
                        (1f + _owner.ServerTalents.Get(Ashveil.Data.TalentStat.CompanionDamagePercent) / 100f); // P1 Talente „Rudelband“/„Rudelführer“
            if (_foe != null && _foe.Fraction < _spec.traitWoundedBelow) dmg *= 1f + _spec.traitBonus; // Hetzjaeger
            return Mathf.Max(1f, Mathf.Round(dmg));
        }

        private void Hit(float amount, Vector3 point, string suffix)
        {
            Health foe = _foe;
            if (foe == null || !foe.IsAlive) return;
            Vector3 dir = foe.transform.position - transform.position; dir.y = 0f;
            foe.ApplyDamage(new DamageInfo
            {
                Amount = amount, Source = gameObject, HitPoint = point, Direction = dir.sqrMagnitude > 1e-4f ? dir.normalized : transform.forward,
                WeaponId = _spec.id + suffix
            });
            DealtDamage?.Invoke(foe, amount);
            _engaged.Add(foe);
        }

        /// <summary>Signatur Sprungbiss (§66/§67): Sprung an das Ziel, kraeftiger Biss, Blutung.</summary>
        private IEnumerator Leap()
        {
            _leaping = true;
            _leapCd = _spec.LeapCooldown(_owner.ServerCompanionBondRank);
            Health foe = _foe;
            Vector3 from = transform.position;
            Vector3 to = foe.transform.position;
            Vector3 dir = to - from; dir.y = 0f;
            to -= (dir.sqrMagnitude > 1e-4f ? dir.normalized : transform.forward) * 1.2f;
            if (NavMesh.SamplePosition(to, out NavMeshHit hit, 2f, NavMesh.AllAreas)) to = hit.position;
            Stop();
            if (dir.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            ObserversLeap(from, to);
            yield return new WaitForSeconds(0.3f);
            if (_agent != null && _agent.enabled) _agent.Warp(to); else transform.position = to;
            LastLeapAt = Time.time;
            if (foe != null && foe.IsAlive && _foe == foe && Flat(foe.transform.position - to) < 3.5f)
            {
                float dmg = Roll(_spec.leapDamageFactor);
                Hit(dmg, foe.transform.position + Vector3.up * 0.7f, ".leap");
                DamageOverTime.Apply(foe, gameObject, Mathf.Max(1f, Mathf.Round(dmg * _spec.bleedPerSecondFactor)), _spec.bleedSeconds, _spec.id + ".bleed");
                ObserversBite(true, foe.transform.position + Vector3.up * 0.7f);
            }
            _attackCd = 0.8f;
            _leaping = false;
        }

        /// <summary>Server (Tests): Zeitpunkt des letzten Sprungbisses.</summary>
        public float LastLeapAt { get; private set; } = -100f;

        private void CheckFallenFoes()
        {
            if (_engaged.Count == 0) return;
            List<Health> fallen = null;
            foreach (Health h in _engaged)
                if (h == null || !h.IsAlive) (fallen ??= new List<Health>()).Add(h);
            if (fallen == null) return;
            foreach (Health h in fallen)
            {
                _engaged.Remove(h);
                if (h != null)
                {
                    var b = h.GetComponent<EnemyBrain>();
                    if (b != null) FoeFell?.Invoke(b);
                }
            }
        }

        private void Idle(CompanionCommand cmd)
        {
            if (cmd == CompanionCommand.Stay)
            {
                if (Flat(_stayPos - transform.position) > 1.5f) MoveTo(_stayPos, _spec.walkSpeed * 1.5f); else Stop();
            }
            else
            {
                Transform o = _owner.transform;
                Vector3 goal = o.position - o.forward * 2.2f + o.right * 1.1f;
                float d = Flat(goal - transform.position);
                if (d > 1.0f) MoveTo(goal, d > 3f ? _spec.runSpeed : Mathf.Max(_spec.walkSpeed, _ownerSpeed * 1.1f));
                else Stop();
            }
            // Ausserhalb des Kampfes erholt er sich (4 %/s)
            if (!_owner.ServerInCombat && _health.IsAlive && _health.Current < _health.Max)
                _health.Heal(_health.Max * 0.004f);
        }

        private void DownedTick()
        {
            // Nach dem Kampf wieder auf (§85): 5 s Ruhe, mindestens 3 s liegen
            if (_owner.ServerInCombat) _quietSince = Time.time;
            if (Time.time - _quietSince < 5f || Time.time - _downSince < 3f) return;
            _health.Revive(0.5f);
            _downed.Value = false;
            _command.Value = (byte)CompanionCommand.Follow;
            WarpNearOwner();
            ObserversHowl();
            _owner.CompanionMessage($"{DisplayName} rappelt sich wieder auf.");
        }

        private void WarpNearOwner()
        {
            _foe = _ordered = null;
            _windup = -1f;
            _ownerTeleports = _ownerMotor != null ? _ownerMotor.TeleportCount : 0;
            Transform o = _owner.transform;
            Vector3 p = o.position - o.forward * 2f + o.right * 1f;
            if (NavMesh.SamplePosition(p, out NavMeshHit hit, 6f, NavMesh.AllAreas)) p = hit.position;
            if (_agent != null && _agent.enabled) _agent.Warp(p); else transform.position = p;
            _stayPos = p;
            _lastOwnerPos = o.position;
        }

        private void MoveTo(Vector3 p, float speed)
        {
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) return;
            _agent.isStopped = false;
            _agent.speed = speed;
            _agent.SetDestination(p);
        }

        private void Stop()
        {
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) return;
            if (!_agent.isStopped) { _agent.isStopped = true; _agent.ResetPath(); }
        }

        private void Face(Health h, float dt)
        {
            if (h == null) return;
            Vector3 d = h.transform.position - transform.position; d.y = 0f;
            if (d.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d.normalized, Vector3.up), 720f * dt);
        }

        private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        // ================================================================== Darstellung (alle)

        [ObserversRpc]
        private void ObserversAttack(Vector3 aim, float windup) { if (_visual != null) _visual.PlayAttack(aim, windup, false); }

        [ObserversRpc]
        private void ObserversBite(bool hit, Vector3 point) { if (_visual != null) _visual.PlayAttackResolved(hit, point); }

        [ObserversRpc]
        private void ObserversLeap(Vector3 from, Vector3 to) { if (_visual != null) _visual.PlayPounce(from, to); }

        [ObserversRpc]
        private void ObserversHowl() { if (_visual != null) _visual.PlayCall(); }
    }
}
