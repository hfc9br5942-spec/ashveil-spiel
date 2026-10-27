using System.Collections.Generic;
using Ashveil.Core;
using Ashveil.Gameplay.Combat;
using UnityEngine;

namespace Ashveil.Gameplay.Enemies
{
    /// <summary>
    /// M6.4 – Das Grubenherz (Herzkaverne, Endboss der Salzgruben). Das versteinerte Herz eines der Alten; es
    /// schlaegt noch, und es hungert nach Licht.
    ///
    ///   Lichtfresser – alle <see cref="eaterInterval"/> s kriechen Schatten aus dem Herzen zu einer brennenden
    ///                  Grubenlampe. Stehen sie <see cref="channelTime"/> s ungestoert dort, erlischt die Lampe.
    ///                  Greift man sie an, lassen sie von der Lampe ab und wehren sich.
    ///   Dunkle Lampen – je erloschener Lampe heilt sich das Herz um <see cref="regenPerDarkLamp"/> seines Lebens je s.
    ///                  Wer <see cref="relightTime"/> s an einer dunklen Lampe steht, zuendet sie wieder an.
    ///   Finsternis   – sind alle vier Lampen aus, trifft alle in der Kaverne alle 3 s Schaden, bis eine wieder brennt.
    ///   Unter 35 %   – mehr und haeufiger Lichtfresser.
    /// Das Herz selbst bewegt sich nicht: Dornen im Umkreis, jeder 3. Schlag ein Puls ueber die ganze Mitte.
    /// </summary>
    public sealed class HeartEncounter : EnemyEncounter
    {
        [Header("Lampen (Reihenfolge = Nummer)")]
        [SerializeField] private Transform[] lamps = new Transform[0];
        [SerializeField] private float lampRadius = 2.6f;

        [Header("Lichtfresser")]
        [SerializeField] private EnemySpawnArea eaterArea;
        [SerializeField] private float eaterFirst = 8f;
        [SerializeField] private float eaterInterval = 18f;
        [SerializeField] private float eaterIntervalEnraged = 12f;
        [SerializeField] private int eatersPerWave = 2;
        [SerializeField] private float channelTime = 4f;
        [SerializeField] private float enrageAt = 0.35f;

        [Header("Lampen wieder anzuenden")]
        [SerializeField] private float relightTime = 3f;
        [Tooltip("Heilung des Herzens je dunkler Lampe (Anteil des Lebens je Sekunde)")]
        [SerializeField] private float regenPerDarkLamp = 0.005f;

        [Header("Finsternis")]
        [SerializeField] private float darknessDamage = 40f;
        [SerializeField] private float darknessInterval = 3f;
        [Tooltip("Ist niemand naeher als das (m), bricht das Herz den Kampf ab")]
        [SerializeField] private float leashRadius = 38f;

        /// <summary>Tests: erste Welle frueher (kleiner 0 = Einstellung).</summary>
        public static float FirstEaterOverride = -1f;

        private bool[] _lit = new bool[0];
        private float[] _relight = new float[0];
        private readonly Dictionary<EnemyBrain, int> _eaterLamp = new Dictionary<EnemyBrain, int>();
        private readonly Dictionary<EnemyBrain, float> _channel = new Dictionary<EnemyBrain, float>();
        private float _eaterTimer, _darkTimer, _nobodyFor, _regenTimer;
        private bool _enraged, _darkAnnounced;

        public int LampCount => lamps.Length;
        public bool IsLit(int i) => i >= 0 && i < _lit.Length && _lit[i];
        public int DarkLamps { get { int n = 0; foreach (bool b in _lit) if (!b) n++; return n; } }
        public int EaterWaves { get; private set; }
        public int LampsLost { get; private set; }
        public int LampsRelit { get; private set; }
        public bool Enraged => _enraged;
        public Vector3 LampPosition(int i) => lamps[i].position;
        /// <summary>Lampe, zu der dieser Lichtfresser unterwegs ist (-1 = keine).</summary>
        public int LampOf(EnemyBrain eater) => eater != null && _eaterLamp.TryGetValue(eater, out int i) ? i : -1;

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureArrays();
        }

        private void EnsureArrays()
        {
            if (_lit.Length == lamps.Length) return;
            _lit = new bool[lamps.Length];
            _relight = new float[lamps.Length];
            for (int i = 0; i < _lit.Length; i++) _lit[i] = true;
        }

        protected override void OnReset()
        {
            EnsureArrays();
            for (int i = 0; i < _lit.Length; i++) { _lit[i] = true; _relight[i] = 0f; }
            _eaterLamp.Clear();
            _channel.Clear();
        }

        protected override void OnFightStarted()
        {
            EnsureArrays();
            _eaterTimer = FirstEaterOverride >= 0f ? FirstEaterOverride : eaterFirst;
            _enraged = false;
            _darkAnnounced = false;
            _nobodyFor = 0f;
            EaterWaves = 0;
            LampsLost = 0;
            LampsRelit = 0;
            Announce($"{bossName} schlägt. Einmal. Der Boden der Kaverne bebt, und die Lampen flackern.");
        }

        protected override void OnFightTick(float dt)
        {
            Vector3 heart = Boss.transform.position;

            // Niemand mehr da -> Abbruch (das Herz kann niemandem folgen)
            _nobodyFor = PlayersNear(heart, leashRadius).Count == 0 ? _nobodyFor + dt : 0f;
            if (_nobodyFor > 3f) { Boss.ServerDisengage(); return; }

            if (!_enraged && Boss.HealthFraction <= enrageAt)
            {
                _enraged = true;
                Announce($"{bossName} rast. Aus jedem Riss kriechen Schatten.");
            }

            // Neue Lichtfresser
            _eaterTimer -= dt;
            if (_eaterTimer <= 0f)
            {
                _eaterTimer = _enraged ? eaterIntervalEnraged : eaterInterval;
                SpawnEaters(_enraged ? eatersPerWave + 1 : eatersPerWave);
            }

            UpdateEaters(dt);
            UpdateRelight(dt);

            // Dunkle Lampen naehren das Herz
            int dark = DarkLamps;
            _regenTimer -= dt;
            if (_regenTimer <= 0f)
            {
                _regenTimer = 1f; // einmal je Sekunde – weniger Netzverkehr
                if (dark > 0 && Boss.Health != null && Boss.Health.IsAlive)
                    Boss.Health.Heal(Mathf.Round(Boss.Health.Max * regenPerDarkLamp * dark), Boss.gameObject);
            }

            // Finsternis
            if (dark == _lit.Length && _lit.Length > 0)
            {
                if (!_darkAnnounced)
                {
                    _darkAnnounced = true;
                    _darkTimer = 1f;
                    Announce("Alle Lampen sind erloschen. Die Finsternis frisst an euch – zündet eine Lampe an!");
                }
                _darkTimer -= dt;
                if (_darkTimer <= 0f)
                {
                    _darkTimer = darknessInterval;
                    foreach (Health h in PlayersNear(heart, leashRadius))
                        h.ApplyDamage(new DamageInfo
                        {
                            Amount = darknessDamage,
                            Source = Boss.gameObject,
                            HitPoint = h.transform.position + Vector3.up,
                            Direction = Vector3.up,
                            WeaponId = (Boss.Definition != null ? Boss.Definition.Id : "enemy.mine_heart") + ".darkness",
                            Unblockable = true
                        });
                }
            }
            else _darkAnnounced = false;
        }

        private void SpawnEaters(int count)
        {
            var litLamps = new List<int>();
            for (int i = 0; i < _lit.Length; i++) if (_lit[i]) litLamps.Add(i);
            if (litLamps.Count == 0 || eaterArea == null) return;
            EaterWaves++;
            int n = 0;
            for (int k = 0; k < count; k++)
            {
                EnemyBrain e = Summon(eaterArea, false);
                if (e == null) continue;
                int lamp = litLamps[(k + Random.Range(0, litLamps.Count)) % litLamps.Count];
                _eaterLamp[e] = lamp;
                _channel[e] = 0f;
                e.ServerSetHome(lamps[lamp].position);
                n++;
            }
            Announce($"Aus dem Herzen kriechen {n} Lichtfresser zu den Lampen.");
        }

        private void UpdateEaters(float dt)
        {
            var done = new List<EnemyBrain>();
            var keys = new List<EnemyBrain>(_eaterLamp.Keys);
            foreach (EnemyBrain e in keys)
            {
                if (e == null || e.Health == null || !e.Health.IsAlive) { done.Add(e); continue; }
                int lamp = _eaterLamp[e];
                if (!_lit[lamp])
                {
                    // Lampe ist schon aus -> naechste brennende suchen
                    int next = NearestLitLamp(e.transform.position);
                    if (next < 0) continue;
                    _eaterLamp[e] = lamp = next;
                    _channel[e] = 0f;
                    e.ServerSetHome(lamps[next].position);
                }
                if (e.InCombat) { _channel[e] = 0f; continue; }
                Vector3 d = e.transform.position - lamps[lamp].position;
                d.y = 0f;
                if (d.magnitude > lampRadius) { _channel[e] = 0f; continue; }
                _channel[e] += dt;
                if (_channel[e] >= channelTime)
                {
                    SetLamp(lamp, false);
                    LampsLost++;
                    Announce("Eine Grubenlampe erlischt – das Grubenherz trinkt ihr Licht.");
                    done.Add(e);
                    Dismiss(e);
                }
            }
            foreach (EnemyBrain e in done) { if (e != null) { _eaterLamp.Remove(e); _channel.Remove(e); } }
            // tote/entfernte Schluessel bereinigen
            var stale = new List<EnemyBrain>();
            foreach (EnemyBrain e in _eaterLamp.Keys) if (e == null) stale.Add(e);
            foreach (EnemyBrain e in stale) { _eaterLamp.Remove(e); _channel.Remove(e); }
        }

        private int NearestLitLamp(Vector3 from)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < _lit.Length; i++)
            {
                if (!_lit[i]) continue;
                float d = (lamps[i].position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private void UpdateRelight(float dt)
        {
            for (int i = 0; i < _lit.Length; i++)
            {
                if (_lit[i]) { _relight[i] = 0f; continue; }
                bool someone = false;
                foreach (Health h in PlayersNear(lamps[i].position, lampRadius + 0.5f))
                {
                    Vector3 d = h.transform.position - lamps[i].position;
                    d.y = 0f;
                    if (d.magnitude <= lampRadius) { someone = true; break; }
                }
                _relight[i] = someone ? _relight[i] + dt : 0f;
                if (_relight[i] >= relightTime)
                {
                    _relight[i] = 0f;
                    SetLamp(i, true);
                    LampsRelit++;
                    Announce("Eine Grubenlampe brennt wieder.");
                }
            }
        }

        private void SetLamp(int i, bool lit)
        {
            if (_lit[i] == lit) return;
            _lit[i] = lit;
            Cue(EncounterCues.Lamp(i), lit ? 1f : 0f, true);
        }

        private void RelightAll()
        {
            for (int i = 0; i < _lit.Length; i++) SetLamp(i, true);
            _eaterLamp.Clear();
            _channel.Clear();
        }

        /// <summary>Server: sofort eine Welle Lichtfresser (Tests, spaetere Ereignisse).</summary>
        public void ServerSpawnEaters(int count) { if (Engaged) SpawnEaters(count); }

        /// <summary>Tests: alle Lampen sofort anzuenden.</summary>
        public void ServerRelightAllForTest()
        {
            for (int i = 0; i < _lit.Length; i++) SetLamp(i, true);
        }

        /// <summary>Tests: alle Lampen sofort loeschen (Finsternis pruefen).</summary>
        public void ServerDarkenAllForTest()
        {
            for (int i = 0; i < _lit.Length; i++) SetLamp(i, false);
        }

        protected override void OnVictory()
        {
            RelightAll();
            Announce($"{bossName} zerspringt. Salzstaub rieselt von der Decke, und zum ersten Mal ist es still in der Grube.");
        }

        protected override void OnWipe()
        {
            RelightAll();
            Announce($"{bossName} beruhigt sich. Die Lampen brennen wieder – noch.");
        }
    }
}
