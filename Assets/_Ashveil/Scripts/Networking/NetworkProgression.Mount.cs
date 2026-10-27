using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Mounts;
using Ashveil.World;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Klassenreittier des Eidwaechters (Class Identity Masterplan §7–§24, Max 28.09.). Alles serverautoritaer (§161/§162):
    ///
    ///   Aufsitzen – Taste/Leiste → Server prueft (eigenes Reittier, passende Klasse, lebt, nicht im Kampf, nicht in einer
    ///               Instanz oder im Kolosseum, am Boden) → 1,5 s Aufsitzen; Bewegen, Schaden oder Angreifen bricht ab.
    ///   Im Sattel – +40 % Tempo (Bindung bis +10 Prozentpunkte), kein Sprint, keine Hechtrolle, kein Reiserhythmus.
    ///               Das steht im MotorState und laeuft so in der Vorhersage mit.
    ///   Absitzen  – Taste/Leiste, eigener Angriff, Treffer eines Gegners (wie „Benommen“ in Classic), Tod, Teleport,
    ///               Instanz/Kolosseum.
    ///   Bindung   – geritten Strecke (gleiche Messung wie Laufkunst, 1×/s, Kreis/Teleport zaehlen nicht) → Lernpunkte;
    ///               kleine Wirkungen: schneller aufsitzen, etwas mehr Tempo (§22).
    ///   Anzeige   – SyncVar fuer alle (Pferd sichtbar), Besitzer bekommt Name/Zustand fuer Leiste und Fenster.
    /// </summary>
    public sealed partial class NetworkProgression
    {
        /// <summary>0 = zu Fuss, sonst Index+1 im <see cref="MountCatalog"/> (fuer alle sichtbar).</summary>
        private readonly SyncVar<byte> _mountShown = new SyncVar<byte>(0);

        private float _mountCastUntil = -1f;
        private Vector3 _mountCastFrom;
        private float _mountCastStartedAt;
        private float _mountedAt = -1f;
        private int _mountTeleports;
        private MountVisual _mountVisual;
        private bool _mountHooked;

        /// <summary>Server: sitzt die Figur gerade auf?</summary>
        public bool ServerMounted => _mountShown.Value != 0;
        /// <summary>Server: laeuft gerade das Aufsitzen?</summary>
        public bool ServerMounting => _mountCastUntil > 0f;
        /// <summary>Server: letzter Grund, warum Aufsitzen nicht ging (Tests).</summary>
        public string ServerMountReject { get; private set; } = "";
        public bool ServerHasMount => Loaded && D.mount != null;
        public double ServerMetersRidden => Loaded && D.lifetime != null ? D.lifetime.metersRidden : 0;
        public ClassMountDefinition ServerMountDefinition => Loaded && D.mount != null ? MountCatalog.Get(D.mount.id) : null;

        private int BondRank()
        {
            ClassMountDefinition m = ServerMountDefinition;
            return m != null ? m.bond.RankFor(ServerMasteryPoints(m.bond.id)) : 0;
        }

        // ================================================================== Start

        private void MountAwake()
        {
            _mountShown.OnChange += OnMountShownChanged;
        }

        private void MountStartClient()
        {
            _mountVisual = MountVisual.Ensure(gameObject);
            _mountVisual.SetVisible(_mountShown.Value != 0);
            if (!IsServerInitialized && !IsOwner)
            {
                var motor = GetComponent<CharacterMotor>();
                if (motor != null) motor.SetMounted(_mountShown.Value != 0, 0f); // fremde Figur: nur fuer Haltung/Posen
            }
            if (IsOwner) MountClient.ToggleRequest = RequestMountToggle;
        }

        private void MountStopClient()
        {
            if (IsOwner && MountClient.ToggleRequest == (System.Action)RequestMountToggle) MountClient.ToggleRequest = null;
        }

        private void OnMountShownChanged(byte prev, byte next, bool asServer)
        {
            if (_mountVisual == null) _mountVisual = MountVisual.Ensure(gameObject);
            _mountVisual.SetVisible(next != 0);
            if (!asServer && !IsServerInitialized && !IsOwner)
            {
                var motor = GetComponent<CharacterMotor>();
                if (motor != null) motor.SetMounted(next != 0, 0f);
            }
        }

        private void MountLogin()
        {
            if (_mMotor == null) _mMotor = GetComponent<CharacterMotor>();
            if (!_mountHooked && _health != null)
            {
                _health.Damaged += OnMountDamaged;
                _health.Died += OnMountDied;
                _mountHooked = true;
            }
            // Wer im Sattel abgemeldet hat, steht beim Anmelden zu Fuss (kein Zustand ueber Sitzungen hinweg)
            SetMountedServer(false, false);
            SendMountState();
        }

        private void MountStopServer()
        {
            if (!_mountHooked || _health == null) return;
            _health.Damaged -= OnMountDamaged;
            _health.Died -= OnMountDied;
            _mountHooked = false;
        }

        // ================================================================== Anfragen

        /// <summary>Besitzer: auf- oder absitzen (Leiste, Taste).</summary>
        public void RequestMountToggle()
        {
            if (!IsOwner) return;
            ServerMountToggle();
        }

        [ServerRpc]
        private void ServerMountToggle() => ServerToggleMount();

        /// <summary>Server: auf- oder absitzen; gibt den Grund zurueck, falls es nicht geht (leer = in Ordnung).</summary>
        public string ServerToggleMount()
        {
            if (!Loaded) return "Nicht bereit.";
            if (ServerMounted) { Dismount("Leiste/Taste"); return ""; }
            if (ServerMounting) { CancelMountCast(""); return ""; }
            string why = MountCheck();
            ServerMountReject = why;
            if (why.Length > 0)
            {
                if (Owner != null && Owner.IsActive) TargetMessage(Owner, why);
                return why;
            }
            float secs = MountCatalog.MountSeconds(ServerMountDefinition, BondRank());
            _mountCastUntil = Time.time + secs;
            _mountCastStartedAt = Time.time;
            _mountCastFrom = transform.position;
            if (Owner != null && Owner.IsActive) TargetMountCast(Owner, secs, NameOfMount());
            return "";
        }

        private string MountCheck()
        {
            ClassMountDefinition m = ServerMountDefinition;
            if (m == null) return "Du hast kein Reittier.";
            CharacterClassDefinition cls = _inv.ServerModel != null ? _inv.ServerModel.Class : null;
            if (cls == null || cls.Id != m.classId) return "Nur ein Eidwächter kann dieses Tier reiten.";
            if (_health != null && !_health.IsAlive) return "Nicht jetzt.";
            if (_mInCombat || ComputeInCombat(Time.time)) return "Nicht mitten im Kampf.";
            if (DungeonSlot.At(transform.position) != null) return "Hier kannst du nicht reiten.";
            if (_mMotor != null && _mMotor.IsSwimming) return "Nicht im Wasser.";
            if (_mMotor != null && (!_mMotor.State.Grounded || _mMotor.IsDodging)) return "Nicht in der Luft.";
            return "";
        }

        private string NameOfMount()
        {
            if (!Loaded || D.mount == null) return "";
            if (!string.IsNullOrEmpty(D.mount.name)) return D.mount.name;
            ClassMountDefinition m = MountCatalog.Get(D.mount.id);
            return m != null ? m.displayName : "";
        }

        // ================================================================== Takt

        private void MountUpdate()
        {
            if (!Loaded || _mMotor == null) return;
            float now = Time.time;
            if (ServerMounting)
            {
                Vector3 d = transform.position - _mountCastFrom;
                d.y = 0f;
                if (d.sqrMagnitude > 0.35f * 0.35f) CancelMountCast("Aufsitzen abgebrochen – du hast dich bewegt.");
                else if (_mCombat != null && _mCombat.ServerLastAttackTime > _mountCastStartedAt) CancelMountCast("Aufsitzen abgebrochen.");
                else if (now >= _mountCastUntil)
                {
                    _mountCastUntil = -1f;
                    string why = MountCheck();
                    if (why.Length > 0) CancelMountCast(why);
                    else SetMountedServer(true, true);
                }
                return;
            }
            if (!ServerMounted) return;
            string off = null;
            if (_health != null && !_health.IsAlive) off = "tot";
            else if (_mCombat != null && _mCombat.ServerLastAttackTime > _mountedAt) off = "Angriff";
            else if (_mMotor.TeleportCount != _mountTeleports) off = "Teleport";
            else if (DungeonSlot.At(transform.position) != null) off = "Instanz";
            else if (!_mMotor.IsMounted) off = "Motor nicht mehr im Sattel";
            if (off != null) Dismount(off);
        }

        private void CancelMountCast(string message)
        {
            if (!ServerMounting) return;
            _mountCastUntil = -1f;
            if (Owner != null && Owner.IsActive)
            {
                TargetMountCastEnd(Owner, true);
                if (!string.IsNullOrEmpty(message)) TargetMessage(Owner, message);
            }
        }

        private void SetMountedServer(bool on, bool announce)
        {
            _mountCastUntil = -1f;
            ClassMountDefinition m = ServerMountDefinition;
            if (on && m == null) on = false;
            float bonus = on ? MountCatalog.SpeedBonus(m, BondRank()) : 0f;
            if (_mMotor == null) _mMotor = GetComponent<CharacterMotor>();
            if (_mMotor != null) _mMotor.SetMounted(on, bonus);
            byte shown = on ? (byte)(System.Array.IndexOf(MountCatalog.All, m) + 1) : (byte)0;
            bool changed = _mountShown.Value != shown;
            if (changed) _mountShown.Value = shown;
            if (on)
            {
                _mountedAt = Time.time;
                _mountTeleports = _mMotor != null ? _mMotor.TeleportCount : 0;
                var emote = GetComponent<NetworkEmote>();
                if (emote != null) emote.ServerClear();
            }
            if (Owner != null && Owner.IsActive)
            {
                if (announce) TargetMountCastEnd(Owner, false);
                TargetMountState(Owner, D.mount != null ? D.mount.id : "", NameOfMount(), on, bonus);
            }
        }

        private void SendMountState()
        {
            if (Owner == null || !Owner.IsActive || !Loaded) return;
            TargetMountState(Owner, D.mount != null ? D.mount.id : "", NameOfMount(), ServerMounted,
                ServerMounted ? MountCatalog.SpeedBonus(ServerMountDefinition, BondRank()) : 0f);
        }

        /// <summary>Bindung gestiegen: Tempo im Sattel sofort nachziehen.</summary>
        private void MountBondChanged()
        {
            if (!ServerMounted) return;
            float bonus = MountCatalog.SpeedBonus(ServerMountDefinition, BondRank());
            if (_mMotor != null) _mMotor.SetMounted(true, bonus);
            SendMountState();
        }

        private void OnMountDamaged(Ashveil.Gameplay.Combat.DamageInfo info)
        {
            if (!IsServerInitialized) return;
            bool byEnemy = info.Source != null && info.Source.GetComponentInParent<Ashveil.Gameplay.Enemies.EnemyBrain>() != null;
            if (ServerMounting) CancelMountCast("Aufsitzen abgebrochen – du wurdest getroffen!");
            else if (ServerMounted && (byEnemy || info.Source != null && info.Source != gameObject))
            {
                Dismount("Treffer von " + info.Source.name);
                if (Owner != null && Owner.IsActive) TargetMessage(Owner, "Der Treffer reißt dich aus dem Sattel!");
            }
        }

        private void OnMountDied()
        {
            if (!IsServerInitialized) return;
            if (ServerMounting) CancelMountCast("");
            if (ServerMounted) Dismount("gestorben");
        }

        /// <summary>Server: absitzen und den Grund protokollieren (Fehlersuche, Tests).</summary>
        private void Dismount(string reason)
        {
            ServerLastDismount = reason;
            Debug.Log($"[Ashveil] Server: {(_inv != null ? _inv.CharacterName : name)} sitzt ab ({reason}).");
            SetMountedServer(false, true);
        }

        public string ServerLastDismount { get; private set; } = "";

        // ================================================================== Freischalten

        /// <summary>
        /// Server: Reittier freischalten (spaeter Abschluss der Klassenquest „Der letzte Reiter“, §3/§13–§18). Legt es auf den
        /// ersten freien Platz der Leiste. Leer = in Ordnung, sonst der Grund.
        /// </summary>
        public string ServerUnlockMount(string mountId, string name = null)
        {
            if (!Loaded) return "Nicht bereit.";
            ClassMountDefinition m = MountCatalog.Get(mountId);
            if (m == null) return "Unbekanntes Reittier.";
            CharacterClassDefinition cls = _inv.ServerModel != null ? _inv.ServerModel.Class : null;
            if (cls == null || cls.Id != m.classId) return "Dieses Tier folgt nur einem Eidwächter.";
            bool fresh = D.mount == null || D.mount.id != m.id;
            D.mount = new MountSave { id = m.id, name = string.IsNullOrEmpty(name) ? null : name.Trim(), cosmetics = D.mount != null ? D.mount.cosmetics : null };
            if (fresh)
            {
                var bar = GetComponent<NetworkActionBar>();
                if (bar != null) bar.ServerPlaceIfMissing(ActionEntryType.Mount, m.id);
                if (Owner != null && Owner.IsActive)
                    TargetMessage(Owner, $"{NameOfMount()} folgt dir jetzt. Je mehr ihr gemeinsam reist, desto vertrauter wird es mit dir.");
            }
            _inv.ServerMarkDirty();
            SendMastery();
            SendMountState();
            return "";
        }

        /// <summary>Server (Tests/Werkzeug): Reittier wieder entfernen.</summary>
        public void ServerRemoveMount()
        {
            if (!Loaded) return;
            SetMountedServer(false, false);
            D.mount = null;
            _inv.ServerMarkDirty();
            SendMountState();
        }

        // ================================================================== Besitzer

        [TargetRpc]
        private void TargetMountCast(NetworkConnection conn, float seconds, string name) =>
            CastBarState.Begin($"{name} rufen", seconds);

        [TargetRpc]
        private void TargetMountCastEnd(NetworkConnection conn, bool interrupted) => CastBarState.End(interrupted);

        [TargetRpc]
        private void TargetMountState(NetworkConnection conn, string id, string name, bool mounted, float bonus)
        {
            MountClient.Apply(id, name, mounted);
            // Eigene Vorhersage sofort mitziehen (der Server-Abgleich kommt erst beim naechsten Schritt)
            if (IsServerInitialized) return;
            var motor = GetComponent<CharacterMotor>();
            if (motor != null) motor.SetMounted(mounted, bonus);
        }
    }
}
