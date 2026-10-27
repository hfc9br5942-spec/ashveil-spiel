using System.Collections.Generic;
using Ashveil.Core.Events;
using Ashveil.Gameplay.Combat;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Lebenspunkte im Netz: Nur der Server zieht Leben ab (<see cref="Health.ApplyDamage"/>).
    /// Clients halten ein Spiegelbild – Wert per SyncVar (auch fuer spaeter Beitretende),
    /// jeder Treffer zusaetzlich per RPC, damit Trefferreaktionen, Zielrahmen und Schadenszahlen
    /// genau im richtigen Moment erscheinen.
    ///
    /// Schadenszahlen sieht nur der Spieler, der getroffen hat (wie in klassischen MMOs);
    /// wer selbst getroffen wird, sieht den erlittenen Schaden.
    /// </summary>
    [RequireComponent(typeof(Health))]
    [DisallowMultipleComponent]
    public sealed class NetworkHealth : NetworkBehaviour, ILeichenzustand
    {
        private readonly SyncVar<float> _current = new SyncVar<float>();
        private readonly SyncVar<float> _max = new SyncVar<float>(100f);
        // 23.10.2026 (Paket C, Codex-Nachprüfung 1 „später Beobachter“): Trennpunkt als dauerhafter LEICHENZUSTAND. Der Treffer-RPC
        // (ObserversDamaged) ist nicht gepuffert – wer die Leiche erst nach dem Tod zu sehen bekommt, liest den Trennpunkt hier
        // schon beim Spawn der Client-Kopie. Bewusst als LETZTE SyncVar der Klasse (die vorhandenen behalten ihre Nummern).
        private readonly SyncVar<byte> _trennpunkt = new SyncVar<byte>(0);
        private Health _health;
        private bool _serverHooked;
        private byte _trennpunktGemeldet;   // Darstellungsseite: zuletzt gemeldeter Wert (Host meldet nur einmal, siehe OnTrennpunktChanged)

        // „Entkommen“ (21.10.): je angreifendem Spieler hoechstens alle 1,5 s – Brenn-/Gift-Ticks (1-s-Takt) und
        // Mehrfachtreffer auf einen heimkehrenden Gegner wuerden den Kampftext sonst fluten
        private const float EvadedNoticeInterval = 1.5f;
        private readonly Dictionary<int, float> _evadedNoticeAt = new Dictionary<int, float>();

        private void Awake()
        {
            _health = GetComponent<Health>();
            _current.OnChange += OnCurrentChanged;
            _max.OnChange += OnMaxChanged;
            _trennpunkt.OnChange += OnTrennpunktChanged;
        }

        private void OnDestroy()
        {
            _current.OnChange -= OnCurrentChanged;
            _max.OnChange -= OnMaxChanged;
            _trennpunkt.OnChange -= OnTrennpunktChanged;
            UnhookServer();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _max.Value = _health.Max;
            _current.Value = _health.Current;
            // 23.10.2026 (Paket C): neu erzeugt/wiederverwendet und lebend → keine alte Leiche (Gegner-Wiederkehr erzeugt neue Objekte,
            // das hier sichert auch eine spätere Wiederverwendung ab)
            if (_health.IsAlive) _trennpunkt.Value = 0;
            if (!_serverHooked)
            {
                _serverHooked = true;
                _health.Changed += OnServerChanged;
                _health.Damaged += OnServerDamaged;
                _health.Missed += OnServerMissed;
                _health.Evaded += OnServerEvaded;
            }
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            UnhookServer();
        }

        private void UnhookServer()
        {
            if (!_serverHooked || _health == null) return;
            _serverHooked = false;
            _health.Changed -= OnServerChanged;
            _health.Damaged -= OnServerDamaged;
            _health.Missed -= OnServerMissed;
            _health.Evaded -= OnServerEvaded;
            _evadedNoticeAt.Clear();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Reiner Client: Leben kommt nur noch vom Server
            if (!IsServerInitialized)
            {
                _health.SetReplica(true);
                _health.SetMaxFromNetwork(_max.Value);
                _health.SetCurrentFromNetwork(_current.Value);
            }
            // 23.10.2026 (Paket C): Beobachterbeginn an einer schon zerstückelten Leiche – der Wert kam mit dem Spawn. Meldet einmal;
            // der von FishNet nachgeholte Client-Aufruf desselben Werts wird verworfen (auch Host-Client, für den keiner nachkommt).
            MeldeTrennpunkt(_trennpunkt.Value);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (!IsServerInitialized && _health != null) _health.SetReplica(false);
            _trennpunktGemeldet = 0;
        }

        // ------------------------------------------------------------------ Leichenzustand (Paket C, 23.10.2026)

        /// <summary>
        /// Trennpunkt der Leiche (0 = nicht zerstückelt / lebt) – Host und Clients, auch für Beobachter, die erst nach dem Tod dazukommen.
        /// Der Server setzt ihn beim tödlichen Treffer (vor dem Treffer-RPC) und auf 0 bei jedem Wiederbeleben/Zurücksetzen.
        /// </summary>
        public byte Trennpunkt => _trennpunkt.Value;

        /// <summary>Darstellungsseite: (alt, neu), genau einmal je Wechsel – siehe <see cref="ILeichenzustand.TrennpunktGeaendert"/>.</summary>
        public event System.Action<byte, byte> TrennpunktGeaendert;

        /// <summary>Wie oft <see cref="TrennpunktGeaendert"/> auf diesem Rechner gefeuert hat (Prüfungen).</summary>
        public int TrennpunktEreignisse { get; private set; }

        private void OnTrennpunktChanged(byte prev, byte next, bool asServer)
        {
            // Darstellung nur, wo es eine gibt: reiner Server meldet nichts. Host: der Server-Aufruf meldet sofort; der spätere
            // Client-Aufruf desselben Werts (FishNet ruft auf dem Host beide Seiten, „prev“ ist dort schon der neue Wert) wird hier
            // verworfen – darum der eigene zuletzt gemeldete Wert statt „prev“.
            if (asServer && !IsClientInitialized) return;
            MeldeTrennpunkt(next);
        }

        private void MeldeTrennpunkt(byte wert)
        {
            if (wert == _trennpunktGemeldet) return;
            byte alt = _trennpunktGemeldet;
            _trennpunktGemeldet = wert;
            TrennpunktEreignisse++;
            TrennpunktGeaendert?.Invoke(alt, wert);
        }

        // ------------------------------------------------------------------ Server

        private void OnServerChanged()
        {
            _max.Value = _health.Max;
            _current.Value = _health.Current;
            // 23.10.2026 (Paket C): lebt wieder (Health.Revive – Kolosseum, Gefährte, Ruhe der Asche; ResetToFull – Wachtfeuer,
            // Heimkehr, Trainingspuppe) → keine Leiche mehr, alte Teile dürfen nicht wiederkommen
            if (_trennpunkt.Value != 0 && _health.IsAlive) _trennpunkt.Value = 0;
        }

        private void OnServerDamaged(DamageInfo info)
        {
            NetworkObject attacker = info.Source != null ? info.Source.GetComponentInParent<NetworkObject>() : null;
            // 22.10.2026 (Zerstückelung, Codex-Review): der Server entscheidet am Treffer selbst, ob und wo getrennt wird
            byte trennpunkt = Ashveil.Gameplay.Enemies.Zerstueckelung.TrennpunktFuer(info, _health.Current, gameObject);
            // 23.10.2026 (Paket C): tödlich → als Leichenzustand festhalten (vor dem RPC; Damaged kommt in Health vor Changed)
            if (!_health.IsAlive) _trennpunkt.Value = trennpunkt;
            // 23.10.2026 (Kampfreaktionen): Ergebnis und absorbierte Menge mitschicken – Health.ApplyDamage hat es schon abgeleitet
            TrefferReaktion reaktion = info.Reaktion != TrefferReaktion.Keine ? info.Reaktion : TrefferReaktionen.Ableiten(info);
            ObserversDamaged(info.Amount, info.IsCritical, info.HitPoint, info.Direction, info.Knockback, attacker, _health.Current, info.Blocked, info.Dodged, trennpunkt,
                (byte)reaktion, info.Absorbed);
            // Nachhall (legendaere Eigenschaft): alle sehen den Lichtpuls in der Klinge des Angreifers
            if (attacker != null && info.WeaponId == WeaponEcho.EchoId) ObserversEcho(attacker);
        }

        private void OnServerMissed(DamageInfo info)
        {
            NetworkObject attacker = info.Source != null ? info.Source.GetComponentInParent<NetworkObject>() : null;
            // 23.10.2026 (Kampfreaktionen): auch ohne Angreifer-Objekt verteilen – „Verfehlt“ ist ein Ergebnis für alle Beobachter
            // (der Text bleibt beim Angreifer, ObserversMissed prüft das selbst)
            ObserversMissed(attacker);
        }

        /// <summary>
        /// 21.10.: Treffer auf ein kurz unverwundbares Ziel (heimkehrender Gegner) blieben ganz ohne Anzeige –
        /// der Spieler dahinter bekommt jetzt „Entkommen“. Gegner als Angreifer (kein Besitzer) bekommen nichts.
        /// </summary>
        private void OnServerEvaded(DamageInfo info)
        {
            NetworkObject attacker = info.Source != null ? info.Source.GetComponentInParent<NetworkObject>() : null;
            NetworkConnection owner = attacker != null ? attacker.Owner : null;
            if (owner == null || !owner.IsActive || !Observers.Contains(owner)) return;
            float now = Time.time;
            if (_evadedNoticeAt.TryGetValue(owner.ClientId, out float last) && now - last < EvadedNoticeInterval) return;
            // lange lebende Gegner: Liste nicht wachsen lassen – nur beim Hinzufuegen eines neuen Angreifers leeren
            if (!_evadedNoticeAt.ContainsKey(owner.ClientId) && _evadedNoticeAt.Count >= 16) _evadedNoticeAt.Clear();
            _evadedNoticeAt[owner.ClientId] = now;
            TargetEvaded(owner);
        }

        // ------------------------------------------------------------------ Clients

        [ObserversRpc]
        private void ObserversEcho(NetworkObject attacker)
        {
            EchoRpcCount++;
            if (attacker != null) WeaponEchoVisual.PulseOn(attacker.gameObject);
        }

        /// <summary>Empfangene Nachhall-Pulse auf diesem Rechner (Selbsttest).</summary>
        public static int EchoRpcCount { get; private set; }

        [ObserversRpc]
        private void ObserversMissed(NetworkObject attacker)
        {
            // 23.10.2026 (Kampfreaktionen): „Verfehlt“ ist ein eigenes Ergebnis – jeder Beobachter ordnet es genau einmal ein
            // (keine Körperreaktion: das Ziel behält seine Tätigkeit; kein erfundenes Ausweichen/Parieren)
            ZaehleReaktion(TrefferReaktion.Verfehlt, 0f);
            TrefferReaktionen.MeldeVerfehlt(gameObject);
            if (attacker == null || !attacker.IsOwner) return;
            var ct = GetComponent<CombatTarget>();
            Vector3 pos = ct != null ? ct.AimPoint + Vector3.up * 0.6f : transform.position + Vector3.up * 2f;
            EventBus<CombatTextEvent>.Raise(new CombatTextEvent(pos, "Verfehlt", CombatTextKind.Miss));
        }

        // 23.10.2026 (Kampfreaktionen, Paket C): „reaktion“ und „absorbed“ HINTEN angehängt – die RPC-Nummer bleibt (Deklarationsreihenfolge
        // unverändert). Ohne „absorbed“ konnten reine Clients „vom Aschenschild gefangen“ nicht von einem Nullschaden unterscheiden.
        [ObserversRpc]
        private void ObserversDamaged(float amount, bool critical, Vector3 point, Vector3 direction, float knockback,
            NetworkObject attacker, float newCurrent, float blocked, bool dodged, byte trennpunkt, byte reaktion, float absorbed)
        {
            var info = new DamageInfo
            {
                Amount = amount,
                IsCritical = critical,
                Source = attacker != null ? attacker.gameObject : null,
                HitPoint = point,
                Direction = direction,
                Knockback = knockback,
                Blocked = blocked,
                Dodged = dodged,
                Trennpunkt = trennpunkt   // Host: dieselbe Entscheidung rechnet die Darstellung lokal (EnemyVisual.MerkeTreffer)
            };
            // Ergebnis vom Server übernehmen; dieselbe reine Ableitung muss hier dasselbe ergeben (sonst Abweichung zählen – Prüfungen)
            TrefferReaktionen.UebernimmVomServer(ref info, reaktion, absorbed);
            if (TrefferReaktionen.Ableiten(info) != info.Reaktion) ReaktionAbweichungen++;
            ZaehleReaktion(info.Reaktion, info.Absorbed);

            // Auf dem Host ist der Treffer schon angewendet (Server und Client teilen das Objekt) – dort trägt der Treffer sein
            // Ergebnis schon aus Health.ApplyDamage (gleiche Funktion), kein zweites Damaged-Ereignis
            if (!IsServerInitialized) _health.ReplicateDamage(info, newCurrent);

            // Zahlen: eigener Treffer -> Schaden; selbst getroffen -> erlittener Schaden
            if (dodged)
            {
                // Ausgewichen (WoW Classic): Angreifer und Getroffener sehen es
                if (attacker != null && attacker.IsOwner)
                {
                    var ctd = GetComponent<CombatTarget>();
                    EventBus<CombatTextEvent>.Raise(new CombatTextEvent(ctd != null ? ctd.AimPoint + Vector3.up * 0.6f : point, "Ausgewichen", CombatTextKind.Miss));
                }
                else if (IsOwner) EventBus<CombatTextEvent>.Raise(new CombatTextEvent(transform.position + Vector3.up * 2f, "Ausgewichen", CombatTextKind.Miss));
                return;
            }
            if (attacker != null && attacker.IsOwner)
            {
                var ct = GetComponent<CombatTarget>();
                Vector3 pos = ct != null ? ct.AimPoint + Vector3.up * 0.6f : point;
                if (amount <= 0f && blocked > 0f) EventBus<CombatTextEvent>.Raise(new CombatTextEvent(pos, "Geblockt", CombatTextKind.Miss));
                else EventBus<CombatTextEvent>.Raise(new CombatTextEvent(pos, amount.ToString("0") + BlockSuffix(blocked),
                    critical ? CombatTextKind.Critical : CombatTextKind.Damage));
            }
            else if (IsOwner)
            {
                Vector3 pos = transform.position + Vector3.up * 2f;
                if (amount <= 0f && blocked > 0f) EventBus<CombatTextEvent>.Raise(new CombatTextEvent(pos, "Geblockt", CombatTextKind.Miss));
                else EventBus<CombatTextEvent>.Raise(new CombatTextEvent(pos, amount.ToString("0") + BlockSuffix(blocked), CombatTextKind.DamageTaken));
            }
            if (blocked > 0f)
            {
                if (IsOwner) LastOwnBlocked = blocked;
                Blocked?.Invoke(this, blocked);
            }
        }

        /// <summary>Clients: ein Treffer auf diese Figur wurde (teilweise) mit dem Schild geblockt (Klang/Anzeige/Tests).</summary>
        public static event System.Action<NetworkHealth, float> Blocked;
        /// <summary>Zuletzt an der eigenen Figur geblockter Schaden (Selbsttest).</summary>
        public static float LastOwnBlocked { get; private set; }

        private static string BlockSuffix(float blocked) => blocked > 0f ? $" ({blocked:0} geblockt)" : "";

        // ------------------------------------------------------------------ Reaktions-Zähler (23.10.2026, Prüfungen)

        private readonly int[] _reaktionen = new int[TrefferReaktionen.Anzahl];

        /// <summary>Auf diesem Rechner empfangene Treffer-Ergebnisse für diese Figur (je Ergebnis; Host zählt den RPC einmal).</summary>
        public int ReaktionenEmpfangen(TrefferReaktion r) => (int)r < _reaktionen.Length ? _reaktionen[(int)r] : 0;

        /// <summary>Absorbierte Menge des zuletzt empfangenen Treffers (0 = keiner/nicht absorbiert).</summary>
        public float LetzteAbsorbiert { get; private set; }

        /// <summary>Clients: wie oft die eigene Ableitung vom gesendeten Ergebnis abwich (muss 0 bleiben).</summary>
        public static int ReaktionAbweichungen { get; private set; }

        private void ZaehleReaktion(TrefferReaktion r, float absorbiert)
        {
            if ((int)r < _reaktionen.Length) _reaktionen[(int)r]++;
            LetzteAbsorbiert = absorbiert;
        }

        private void OnMaxChanged(float prev, float next, bool asServer)
        {
            if (asServer || IsServerInitialized || _health == null) return;
            _health.SetMaxFromNetwork(next);
        }

        private void OnCurrentChanged(float prev, float next, bool asServer)
        {
            if (asServer || IsServerInitialized || _health == null) return;
            _health.SetCurrentFromNetwork(next);
        }

        // 21.10.: eigener Text statt „Verfehlt“ – der Schlag sass, das Ziel ist nur gerade nicht verwundbar.
        // Bewusst als LETZTER RPC der Klasse: FishNet nummeriert RPCs nach Deklarationsreihenfolge, so behalten die
        // vorhandenen RPCs (ObserversDamaged …) ihre Nummern.
        [TargetRpc]
        private void TargetEvaded(NetworkConnection conn)
        {
            var ct = GetComponent<CombatTarget>();
            Vector3 pos = ct != null ? ct.AimPoint + Vector3.up * 0.6f : transform.position + Vector3.up * 2f;
            EventBus<CombatTextEvent>.Raise(new CombatTextEvent(pos, "Entkommen", CombatTextKind.Miss));
        }
    }
}
