using Ashveil.Gameplay.Combat;
using Ashveil.Data;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using Ashveil.World;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Netzwerk-Adapter eines Gegners: Die KI (<see cref="EnemyBrain"/>) laeuft nur auf dem Server.
    /// Clients bekommen Position (NetworkTransform), Leben (<see cref="NetworkHealth"/>), Stufe und
    /// Angriffs-Ankuendigungen – damit sie ausholende Gegner sehen und rechtzeitig ausweichen koennen.
    /// </summary>
    [RequireComponent(typeof(EnemyBrain))]
    [DisallowMultipleComponent]
    public sealed class NetworkEnemy : NetworkBehaviour
    {
        [SerializeField] private WorldLabel nameplate;

        private readonly SyncVar<byte> _level = new SyncVar<byte>(1);
        private readonly SyncVar<byte> _concealment = new SyncVar<byte>(0);
        // #28: Zugehoerigkeit je Gebiet (neutrale Tiere gelb, sonst rot) – das Aggro-Profil kennt nur der Server
        private readonly SyncVar<byte> _faction = new SyncVar<byte>((byte)Faction.Hostile);
        // Boss-Anatomie: Zustaende aller Koerperteile (je 2 Bit) und Gesamtzustand – Spaetkommer sehen sie sofort
        private readonly SyncVar<uint> _partStates = new SyncVar<uint>(0u);
        private readonly SyncVar<byte> _damageState = new SyncVar<byte>(0);
        private EnemyAnatomy _anatomy;

        /// <summary>Server: abweichende Beutetabelle (Selbsttest).</summary>
        public LootTableDefinition LootTableOverride { get; set; }

        private EnemyBrain _brain;
        private EnemyVisual _visual;
        private CombatTarget _target;
        private bool _hooked;

        private void Awake()
        {
            _brain = GetComponent<EnemyBrain>();
            _visual = GetComponent<EnemyVisual>();
            _target = GetComponent<CombatTarget>();
            _level.OnChange += OnLevelChanged;
            _faction.OnChange += OnFactionChanged;
            _concealment.OnChange += OnConcealmentChanged;
            _partStates.OnChange += OnAnatomySync;
            _damageState.OnChange += OnDamageStateSync;
            // 11.10.2026 (Ruckler-Messung r2.12: 80 ms je Bild in den Animatoren aller Gegner): nicht sichtbare Gegner nicht animieren.
            // Gegner nutzen weder Wurzelbewegung noch Animations-Ereignisse – Bewegung und Treffer laufen ueber KI und Netz.
            CullAnimators();
        }

        /// <summary>Animatoren nur rechnen, solange ein Renderer sichtbar ist (auch spaeter angehaengte Modelle).</summary>
        private void CullAnimators()
        {
            foreach (Animator an in GetComponentsInChildren<Animator>(true))
                an.cullingMode = AnimatorCullingMode.CullCompletely;
        }

        private void OnDestroy()
        {
            _level.OnChange -= OnLevelChanged;
            _faction.OnChange -= OnFactionChanged;
            _concealment.OnChange -= OnConcealmentChanged;
            _partStates.OnChange -= OnAnatomySync;
            _damageState.OnChange -= OnDamageStateSync;
            Unhook();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            CullAnimators();
            ServerVisualStripper.StripRenderers(gameObject); // Server ohne Grafik: keine Begrenzungsvolumen je Bild
            _level.Value = (byte)Mathf.Clamp(_brain.Level, 1, 255);
            _faction.Value = (byte)_brain.Faction;
            if (!_hooked)
            {
                _hooked = true;
                _brain.AttackBegan += OnAttackBegan;
                _brain.AreaAttackBegan += OnAreaAttackBegan;
                _brain.AttackResolved += OnAttackResolved;
                _brain.Pounced += OnPounced;
                _brain.Howled += OnHowled;
                _brain.ConcealmentChanged += OnServerConcealment;
                _brain.Killed += OnKilled;
                _brain.StrikeExploded += OnStrikeExploded;
                _brain.MoveBegan += OnMoveBegan;
                _brain.FactionChanged += OnServerFactionChanged;
                _anatomy = _brain.Anatomy != null ? _brain.Anatomy : EnemyAnatomy.Ensure(gameObject, _brain.Definition, true);
                if (_anatomy != null)
                {
                    _anatomy.PartStateChanged += OnServerPartState;
                    _anatomy.DamageStateChanged += OnServerDamageState;
                    _anatomy.KnockedDown += OnServerKnockdown;
                    _anatomy.Staggered += OnServerStagger;
                }
            }
            var eligibility = GetComponent<LootEligibility>();
            if (eligibility != null) eligibility.ActivateServer();
            _brain.Activate();
            ApplyLevel(_brain.Level);
            OnServerConcealment(_brain.Concealment);
            if (_anatomy != null)
            {
                _partStates.Value = _anatomy.PackStates();
                _damageState.Value = (byte)_anatomy.DamageState;
            }
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            Unhook();
        }

        private void Unhook()
        {
            if (!_hooked || _brain == null) return;
            _hooked = false;
            _brain.AttackBegan -= OnAttackBegan;
            _brain.AreaAttackBegan -= OnAreaAttackBegan;
            _brain.AttackResolved -= OnAttackResolved;
            _brain.Pounced -= OnPounced;
            _brain.Howled -= OnHowled;
            _brain.ConcealmentChanged -= OnServerConcealment;
            _brain.Killed -= OnKilled;
            _brain.StrikeExploded -= OnStrikeExploded;
            _brain.MoveBegan -= OnMoveBegan;
            _brain.FactionChanged -= OnServerFactionChanged;
            if (_anatomy != null)
            {
                _anatomy.PartStateChanged -= OnServerPartState;
                _anatomy.DamageStateChanged -= OnServerDamageState;
                _anatomy.KnockedDown -= OnServerKnockdown;
                _anatomy.Staggered -= OnServerStagger;
            }
        }

        // ------------------------------------------------------------------ Beute (Server)

        private void OnKilled(EnemyBrain brain)
        {
            var corpse = GetComponent<CorpseLoot>();
            var eligibility = GetComponent<LootEligibility>();
            if (corpse == null || eligibility == null) return;
            LootTableDefinition table = LootTableOverride != null ? LootTableOverride : (_brain.Definition != null ? _brain.Definition.lootTable : null);
            var recipients = eligibility.FinalRecipients();
            corpse.Generate(table, recipients, p =>
            {
                if (NetworkInventory.Online.TryGetValue(p, out NetworkInventory inv) && inv != null) return inv.LootClass;
                return eligibility.ClassOf(p);
            }, unchecked((int)ObjectId * 7919 + (int)(Time.time * 1000f)), NetworkProgression.ServerWantsLoot); // P3: Auftragsbeute nur mit laufendem Auftrag
            // Boss-Anatomie: gebrochene Teile geben Zusatzbeute (Anatomie §8: Zerstoerung lohnt sich)
            if (_anatomy != null)
                foreach (EnemyAnatomy.Part part in _anatomy.Parts)
                    if (part.State >= BodyPartState.Broken && !string.IsNullOrEmpty(part.Def.bonusLootItem))
                        corpse.AddBonus(recipients, part.Def.bonusLootItem, part.Def.bonusLootCount);
            // 23.10.2026 (Max, COMBAT-026b): abgetrennte Teile enthalten mit geringer Chance Zusatzbeute (in die persönliche Leichenbeute)
            EnemyVisual vis = GetComponentInChildren<EnemyVisual>();
            if (vis != null)
                Zerstueckelung.ZusatzBeute(vis.TodesTrennpunkt, corpse, table, recipients, p =>
                {
                    if (NetworkInventory.Online.TryGetValue(p, out NetworkInventory inv) && inv != null) return inv.LootClass;
                    return eligibility.ClassOf(p);
                }, unchecked((int)ObjectId * 31337 + (int)(Time.time * 1000f)), NetworkProgression.ServerWantsLoot);
            foreach (string p in recipients)
                if (NetworkInventory.Online.TryGetValue(p, out NetworkInventory inv) && inv != null) inv.NotifyLootAvailable(NetworkObject);
            // Nachlese des Heilers: persoenlicher Zusatzwurf aus dem Zustand der Koerperteile beim Tod (Klassen-Identitaet §35–§42)
            if (_brain.Definition != null && _brain.Definition.salvage != null && _brain.Definition.salvage.Active)
            {
                EnemyAnatomy anatomy = _anatomy;
                NetworkProgression.ServerSalvageOnKill(recipients, eligibility, _brain.Definition, _brain.Level, key =>
                {
                    if (anatomy == null) return null;
                    foreach (EnemyAnatomy.Part part in anatomy.Parts)
                        if (part.Def != null && part.Def.key == key) return part.State;
                    return null;
                }, _brain.Health != null ? _brain.Health.Max : 1f, corpse, NetworkObject, unchecked((int)ObjectId * 104729 + (int)(Time.time * 1000f)));
            }
            NetworkProgression.ServerEnemyKilled(recipients, _brain.Definition, _brain.Level, transform.position + Vector3.up * 1.6f);
            Debug.Log($"[Ashveil] Server: {(_brain.Definition != null ? _brain.Definition.DisplayName : name)} besiegt – persoenliche Beute fuer {recipients.Count} Spieler.");
        }

        /// <summary>
        /// 21.10. (MEHRSPIELER-01): Server – ein Spieler sieht diesen Gegner (wieder). FishNet zerstoert die Client-Kopie, sobald er
        /// aus der Sicht faellt (Sicht_Gegner: ab 550 m, z. B. Tod → Wachtfeuer, Wiederverbinden an anderer Stelle), und erzeugt beim
        /// Wiedereintritt eine neue ohne Glimmen. Das Glimmen ist reiner Client-Zustand (LootIndicator), und der LootInteractor
        /// oeffnet nur bei Glimmen – ohne Neusenden waere die eigene Beute (auch Questbeute) nicht mehr aufhebbar.
        /// FishNet ruft das erst nach dem Spawn-Paket auf (ServerObjects.Observers), die TargetRpc trifft also die neue Kopie.
        /// Nur vorhandene RPCs (NotifyLootAvailable, ServerNotifySalvage).
        /// </summary>
        public override void OnSpawnServer(NetworkConnection connection)
        {
            base.OnSpawnServer(connection);
            if (connection == null || !connection.IsActive) return;
            var corpse = GetComponent<CorpseLoot>();
            if (corpse == null || !corpse.HasAnyLoot) return;   // lebender Gegner oder leere Leiche: nichts zu melden
            NetworkInventory inv = InventoryOf(connection);
            if (inv == null || string.IsNullOrEmpty(inv.ProfileId)) return;   // noch nicht angemeldet: NotifyAllCorpses beim Login
            if (corpse.HasLootFor(inv.ProfileId)) inv.NotifyLootAvailable(NetworkObject);
            if (corpse.HasSalvageFor(inv.ProfileId))
            {
                var np = inv.GetComponent<NetworkProgression>();
                if (np != null) np.ServerNotifySalvage(NetworkObject);
            }
        }

        /// <summary>Server: Inventar der Spielerfigur einer Verbindung (erstes Objekt, sonst Suche unter den Angemeldeten).</summary>
        private static NetworkInventory InventoryOf(NetworkConnection conn)
        {
            NetworkObject first = conn.FirstObject;
            NetworkInventory inv = first != null ? first.GetComponent<NetworkInventory>() : null;
            if (inv != null) return inv;
            foreach (NetworkInventory i in NetworkInventory.Online.Values)
                if (i != null && i.Owner == conn) return i;
            return null;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            CullAnimators();
            if (!IsServerInitialized)
            {
                _brain.DisableForClient();
                ApplyLevel(_level.Value);
                ApplyConcealment((EnemyConcealment)_concealment.Value);
                _anatomy = EnemyAnatomy.Ensure(gameObject, _brain.Definition, false);
                if (_anatomy != null) _anatomy.ApplyReplicated(_partStates.Value, (EnemyDamageState)_damageState.Value);
            }
        }

        // ------------------------------------------------------------------ Boss-Anatomie

        private void OnServerPartState(int index, BodyPartState state)
        {
            _partStates.Value = _anatomy.PackStates();
            if (state >= BodyPartState.Broken) ObserversPartBroken((byte)index, (byte)state);
        }

        private void OnServerDamageState(EnemyDamageState s) => _damageState.Value = (byte)s;
        private void OnServerKnockdown(float seconds) => ObserversStun(seconds, true);
        private void OnServerStagger(float seconds) => ObserversStun(seconds, false);

        private void OnAnatomySync(uint prev, uint next, bool asServer)
        {
            if (asServer || IsServerInitialized || _anatomy == null) return;
            _anatomy.ApplyReplicated(next, (EnemyDamageState)_damageState.Value);
        }

        private void OnDamageStateSync(byte prev, byte next, bool asServer)
        {
            if (asServer || IsServerInitialized || _anatomy == null) return;
            _anatomy.ApplyReplicated(_partStates.Value, (EnemyDamageState)next);
        }

        [ObserversRpc]
        private void ObserversPartBroken(byte index, byte state)
        {
            if (_visual != null) _visual.PlayPartBroken(index, (BodyPartState)state);
        }

        [ObserversRpc]
        private void ObserversStun(float seconds, bool knockdown)
        {
            if (!IsServerInitialized && _anatomy != null) _anatomy.ApplyReplicatedStun(seconds, knockdown);
        }

        private void OnMoveBegan(byte id, float windup) => ObserversMove(id, windup);

        [ObserversRpc]
        private void ObserversMove(byte id, float windup)
        {
            if (!IsServerInitialized && _anatomy != null && _brain.Definition != null)
            {
                EnemyAttackDefinition m = _brain.Definition.Move(id);
                if (m != null && m.lowersHeadFor > 0f) _anatomy.SetLowered(windup + m.lowersHeadFor);
            }
            if (_visual != null) _visual.PlayMove(id, windup);
        }

        private void OnLevelChanged(byte prev, byte next, bool asServer)
        {
            if (asServer || IsServerInitialized) return;
            ApplyLevel(next);
        }

        /// <summary>Server: Aggro-Profil nach dem Erscheinen geändert (Gebiet, Arena) – Zugehörigkeit an alle.</summary>
        private void OnServerFactionChanged()
        {
            _faction.Value = (byte)_brain.Faction;
            ApplyLevel(_level.Value);
        }

        private void OnFactionChanged(byte prev, byte next, bool asServer)
        {
            if (asServer || IsServerInitialized) return;
            ApplyLevel(_level.Value);
        }

        /// <summary>Zugehoerigkeit: auf dem Server aus der KI (Aggro-Profil des Gebiets), auf Clients aus dem Netz.</summary>
        private Faction CurrentFaction => IsServerInitialized ? _brain.Faction : (Faction)_faction.Value;

        private void ApplyLevel(int level)
        {
            EnemyDefinition d = _brain.Definition;
            if (d == null) return;
            Faction f = CurrentFaction;
            if (_target != null)
                _target.Configure(d.DisplayName, level, f,
                    _target.AimPoint - transform.position, _target.SelectionRadius, d.elite, d.levelLabel);
            if (nameplate != null)
                nameplate.Configure(d.DisplayName, d.elite ? "Elite" : "", f == Faction.Neutral ? WorldLabelStyle.Neutral : WorldLabelStyle.Hostile,
                    nameplate.MaxDistance, nameplate.SizeMultiplier);
        }

        // ------------------------------------------------------------------ Tarnung / Abtauchen

        private void OnServerConcealment(EnemyConcealment c)
        {
            _concealment.Value = (byte)c;
            ApplyConcealment(c);
        }

        private void OnConcealmentChanged(byte prev, byte next, bool asServer)
        {
            if (asServer || IsServerInitialized) return;
            ApplyConcealment((EnemyConcealment)next);
        }

        private void ApplyConcealment(EnemyConcealment c)
        {
            if (_target != null) _target.Concealed = c != EnemyConcealment.None;
            if (nameplate != null) nameplate.gameObject.SetActive(c == EnemyConcealment.None);
            if (_visual != null) _visual.SetConcealment(c);
        }

        // ------------------------------------------------------------------ Server -> alle

        private void OnAttackBegan(Vector3 aim, float windup, bool ranged) => ObserversAttackBegan(aim, windup, ranged);
        private void OnAttackResolved(bool hit, Vector3 point) => ObserversAttackResolved(hit, point);
        private void OnHowled() => ObserversCall();
        private void OnAreaAttackBegan(Vector3 center, float windup, float radius) => ObserversAreaAttack(center, windup, radius);
        private void OnPounced(Vector3 from, Vector3 to) => ObserversPounce(from, to);
        private void OnStrikeExploded(Vector3 center, float radius, bool any) => ObserversExplosion(center, radius);

        [ObserversRpc]
        private void ObserversExplosion(Vector3 center, float radius)
        {
            if (_visual != null) _visual.PlayExplosion(center, radius);
        }

        [ObserversRpc]
        private void ObserversAreaAttack(Vector3 center, float windup, float radius)
        {
            if (_visual != null) _visual.PlayAreaAttack(center, windup, radius);
        }

        [ObserversRpc]
        private void ObserversPounce(Vector3 from, Vector3 to)
        {
            if (_visual != null) _visual.PlayPounce(from, to);
        }

        [ObserversRpc]
        private void ObserversAttackBegan(Vector3 aim, float windup, bool ranged)
        {
            if (_visual != null) _visual.PlayAttack(aim, windup, ranged);
        }

        [ObserversRpc]
        private void ObserversAttackResolved(bool hit, Vector3 point)
        {
            if (_visual != null) _visual.PlayAttackResolved(hit, point);
        }

        [ObserversRpc]
        private void ObserversCall()
        {
            if (_visual != null) _visual.PlayCall();
        }
    }
}
