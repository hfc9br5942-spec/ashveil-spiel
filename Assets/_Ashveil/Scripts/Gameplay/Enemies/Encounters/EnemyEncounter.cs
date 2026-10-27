using System;
using System.Collections.Generic;
using Ashveil.Core;
using Ashveil.Gameplay.Combat;
using UnityEngine;

namespace Ashveil.Gameplay.Enemies
{
    /// <summary>
    /// Grundlage einer Boss-Begegnung – liegt als Szenenobjekt neben dem Boss (z. B. in einem Dungeon-Platz) und
    /// laeuft NUR auf dem Server (auf Clients gibt es keine aktive KI, dann bleibt sie still).
    ///
    ///   Bindung  – findet den Boss (aktive KI mit der Definition des Boss-Gebiets in der Naehe).
    ///   Kampf    – beginnt, sobald der Boss angreift; Unterklassen steuern Phasen und Faehigkeiten.
    ///   Abbruch  – kehrt der Boss heim (Leine) oder sterben alle, wird alles zurueckgesetzt, Helfer verschwinden.
    ///   Sieg     – Boss tot; Helfer ziehen sich zurueck.
    ///
    /// Gegner erzeugen/entfernen kann nur das Netzwerk; es traegt dafuer <see cref="Summoner"/>/<see cref="Remover"/> ein.
    /// Ansagen (<see cref="Announced"/>) verteilt der Dungeon an alle Spieler der Instanz.
    /// </summary>
    public abstract class EnemyEncounter : MonoBehaviour
    {
        /// <summary>Server: Gegner eines Gebiets einmalig erzeugen (setzt der EnemyDirector).</summary>
        public static Func<EnemySpawnArea, EnemyBrain> Summoner;
        /// <summary>Server: Gegner sofort entfernen (setzt der EnemyDirector).</summary>
        public static Action<EnemyBrain> Remover;
        /// <summary>Ansage einer Begegnung: Ort, Text (Server).</summary>
        public static event Action<Vector3, string> Announced;
        /// <summary>
        /// Ansage einer Begegnung in der offenen Welt (Kreidekeller, 12.10.2026): Ort, Text – der EnemyDirector schickt sie an die
        /// Spieler in der Nähe (keine Instanz, die sie verteilt).
        /// </summary>
        public static event Action<Vector3, string> OffeneWeltAnsage;

        /// <summary>Ansage in der offenen Welt auslösen (auch für Fallen wie die Alarmglocke).</summary>
        public static void AnsageOffen(Vector3 where, string text) => OffeneWeltAnsage?.Invoke(where, text);

        /// <summary>Signal an die Umgebung (<see cref="EncounterCues"/>): Ort, Signal, Wert (Server -> Spieler der Instanz).</summary>
        public static event Action<Vector3, string, float> CueSent;

        private readonly Dictionary<string, float> _persistent = new Dictionary<string, float>();
        /// <summary>Zustaende, die spaeter Hinzukommende auch sehen muessen (z. B. offenes Tor).</summary>
        public IReadOnlyDictionary<string, float> PersistentCues => _persistent;

        private static readonly List<EnemyEncounter> AllEncounters = new List<EnemyEncounter>();
        public static IReadOnlyList<EnemyEncounter> All => AllEncounters;

        [Tooltip("Gebiet, in dem der Boss erscheint")]
        [SerializeField] protected EnemySpawnArea bossArea;
        [Tooltip("Name in Ansagen")]
        [SerializeField] protected string bossName = "Boss";
        [Tooltip("Begegnung in der offenen Welt (keine Instanz): Ansagen gehen an die Spieler in der Nähe")]
        [SerializeField] protected bool offeneWelt;

        private const float TickInterval = 0.1f;
        private float _tick;
        private readonly List<EnemyBrain> _adds = new List<EnemyBrain>();

        public EnemyBrain Boss { get; private set; }
        public bool Engaged { get; private set; }
        public bool Defeated { get; private set; }
        /// <summary>Wie oft der Kampf schon begonnen hat (Tests).</summary>
        public int Attempts { get; private set; }
        public IReadOnlyList<EnemyBrain> Adds => _adds;
        public int LivingAdds { get { int n = 0; foreach (EnemyBrain b in _adds) if (b != null && b.Health != null && b.Health.IsAlive) n++; return n; } }
        public string BossName => bossName;

        protected virtual void OnEnable() { if (!AllEncounters.Contains(this)) AllEncounters.Add(this); }
        protected virtual void OnDisable() => AllEncounters.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            AllEncounters.Clear();
            Summoner = null;
            Remover = null;
            Announced = null;
            OffeneWeltAnsage = null;
            CueSent = null;
        }

        /// <summary>Server: Begegnung vergessen (Instanz neu belegt oder zurueckgesetzt).</summary>
        public void ServerReset()
        {
            if (Engaged) EndFight(false);
            Boss = null;
            Defeated = false;
            _adds.Clear();
            _persistent.Clear();
            EncounterCues.Raise(transform.position, EncounterCues.Reset, 0f);
            OnReset();
        }

        private void Update()
        {
            _tick -= Time.deltaTime;
            if (_tick > 0f) return;
            _tick = TickInterval;

            if (Boss == null || !Boss.isActiveAndEnabled)
            {
                if (Engaged) EndFight(false);
                Boss = FindBoss();
                if (Boss == null) return;
                Defeated = false;
            }
            if (Defeated) return;

            if (Boss.State == EnemyState.Dead)
            {
                if (Engaged) EndFight(true);
                Defeated = true;
                return;
            }
            if (!Engaged)
            {
                if (Boss.InCombat) BeginFight();
                return;
            }
            if (Boss.State == EnemyState.Return || Boss.State == EnemyState.Idle || Boss.State == EnemyState.Wander)
            {
                EndFight(false);
                return;
            }
            OnFightTick(TickInterval);
        }

        private EnemyBrain FindBoss()
        {
            if (bossArea == null || bossArea.Definition == null) return null;
            Vector3 c = bossArea.transform.position;
            float max = bossArea.Radius + bossArea.Definition.leashDistance + 5f;
            foreach (EnemyBrain b in EnemyBrain.All)
                if (b != null && b.IsActive && b.Definition == bossArea.Definition && b.State != EnemyState.Dead &&
                    (b.transform.position - c).sqrMagnitude <= max * max)
                    return b;
            return null;
        }

        private void BeginFight()
        {
            Engaged = true;
            Attempts++;
            OnFightStarted();
        }

        private void EndFight(bool victory)
        {
            Engaged = false;
            if (Boss != null) Boss.ServerClearStrikes();
            // Helfer verschwinden (Sieg: sie fliehen, Abbruch: sie gehen zurueck an die Arbeit)
            foreach (EnemyBrain b in _adds)
                if (b != null && b.Health != null && b.Health.IsAlive) Remover?.Invoke(b);
            _adds.Clear();
            if (victory) OnVictory(); else OnWipe();
        }

        // ------------------------------------------------------------------ fuer Unterklassen

        protected abstract void OnFightStarted();
        protected abstract void OnFightTick(float dt);
        protected virtual void OnVictory() { }
        protected virtual void OnWipe() { }
        protected virtual void OnReset() { }

        protected void Announce(string text)
        {
            if (offeneWelt) OffeneWeltAnsage?.Invoke(transform.position, text);
            else Announced?.Invoke(transform.position, text);
        }

        /// <summary>Signal an die Umgebung: hier auf dem Server sofort, bei den Spielern der Instanz per Netz.</summary>
        protected void Cue(string cue, float value, bool persistent = false)
        {
            if (persistent) _persistent[cue] = value;
            EncounterCues.Raise(transform.position, cue, value);
            CueSent?.Invoke(transform.position, cue, value);
        }

        /// <summary>Einen Helfer aus einem Gebiet rufen; er greift sofort den naechsten Spieler an.</summary>
        protected EnemyBrain Summon(EnemySpawnArea area, bool engage = true)
        {
            if (area == null || Summoner == null) return null;
            EnemyBrain b = Summoner(area);
            if (b == null) return null;
            _adds.Add(b);
            if (!engage) return b;
            Health target = NearestPlayer(b.transform.position, 60f);
            if (target != null) b.ServerEngage(target);
            return b;
        }

        /// <summary>Einen Helfer sofort entfernen (z. B. wenn er in einer Lampe aufgeht).</summary>
        protected void Dismiss(EnemyBrain add)
        {
            if (add == null) return;
            _adds.Remove(add);
            Remover?.Invoke(add);
        }

        private static readonly Collider[] Buffer = new Collider[32];

        /// <summary>Lebende Spieler im Umkreis des Bosses (fuer gezielte Faehigkeiten).</summary>
        protected List<Health> PlayersNear(Vector3 center, float radius)
        {
            var list = new List<Health>();
            int n = Physics.OverlapSphereNonAlloc(center, radius, Buffer, LayerMask.GetMask(GameLayers.Player), QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Health h = Buffer[i].GetComponentInParent<Health>();
                if (h != null && h.IsAlive && h.isActiveAndEnabled && !list.Contains(h)) list.Add(h);
            }
            return list;
        }

        protected Health NearestPlayer(Vector3 from, float radius)
        {
            Health best = null;
            float bestD = float.MaxValue;
            foreach (Health h in PlayersNear(from, radius))
            {
                float d = (h.transform.position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = h; }
            }
            return best;
        }
    }
}
