using System.Collections.Generic;
using Ashveil.Core;
using Ashveil.Gameplay.Combat;
using UnityEngine;

namespace Ashveil.Gameplay.Enemies
{
    /// <summary>
    /// M6.3 – Die Salzmutter (Salzsee-Grotte, Boss 2 der Salzgruben).
    ///
    ///   Salzspucke   – alle <see cref="spitInterval"/> s auf einen zufaelligen Spieler (Warnring, dann Treffer).
    ///   Flut         – alle <see cref="floodInterval"/> s steigt das Salzwasser: <see cref="floodWarn"/> s Warnung,
    ///                  dann <see cref="floodDuration"/> s Schaden fuer alle, die unten in der Grotte stehen.
    ///                  Auf den Plattformen bleibt man trocken.
    ///   Krebsbrut    – bei 75 %, 50 % und 25 % schluepfen kleine Salzkrebse aus dem See.
    ///   Unter 30 %   – die Flut kommt haeufiger.
    ///   Sieg         – das Gitter zur Lorenbahn oeffnet sich (Abkuerzung zur Foerderhalle), fuer alle in der Instanz.
    /// </summary>
    public sealed class SaltMotherEncounter : EnemyEncounter
    {
        [Header("Salzspucke")]
        [SerializeField] private float spitInterval = 9f;
        [SerializeField] private float spitRadius = 3f;
        [SerializeField] private float spitFuse = 1.8f;
        [SerializeField] private float spitDamage = 45f;

        [Header("Flut")]
        [SerializeField] private float floodFirst = 14f;
        [SerializeField] private float floodInterval = 30f;
        [SerializeField] private float floodIntervalEnraged = 22f;
        [SerializeField] private float floodWarn = 4f;
        [SerializeField] private float floodDuration = 7f;
        [SerializeField] private float floodDamagePerSecond = 16f;
        [Tooltip("Halbe Kantenlaenge der Grotte (m) um dieses Objekt – nur dort wirkt die Flut")]
        [SerializeField] private float floodHalfSize = 21f;
        [Tooltip("Wer mit den Fuessen hoeher steht (m ueber dem Grottenboden), bleibt trocken")]
        [SerializeField] private float floodSafeHeight = 0.9f;

        [Header("Krebsbrut")]
        [SerializeField] private EnemySpawnArea[] broodAreas = new EnemySpawnArea[0];
        [SerializeField] private float[] broodAt = { 0.75f, 0.5f, 0.25f };
        [SerializeField] private float enrageAt = 0.3f;

        /// <summary>Tests: Zeitpunkte ueberschreiben (kleiner 0 = Einstellung).</summary>
        public static float FirstFloodOverride = -1f;
        public static float FirstSpitOverride = -1f;

        private float _spitTimer, _floodTimer, _floodPhase, _damageTick;
        private enum FloodState { Calm, Warning, Flooding }
        private FloodState _flood;
        private int _nextBrood;
        private bool _enraged;

        public int Floods { get; private set; }
        public int Spits { get; private set; }
        public int BroodWaves => _nextBrood;
        public bool Enraged => _enraged;
        public bool FloodWarning => _flood == FloodState.Warning;
        public bool FloodActive => _flood == FloodState.Flooding;
        /// <summary>Hoehe, ab der man trocken steht (Welt, Tests).</summary>
        public float SafeHeightWorld => transform.position.y + floodSafeHeight;

        protected override void OnFightStarted()
        {
            _spitTimer = FirstSpitOverride >= 0f ? FirstSpitOverride : spitInterval * 0.5f;
            _floodTimer = FirstFloodOverride >= 0f ? FirstFloodOverride : floodFirst;
            _flood = FloodState.Calm;
            _nextBrood = 0;
            _enraged = false;
            Floods = 0;
            Spits = 0;
            Announce($"{bossName} hebt sich aus dem Salzsee. Die Grotte hallt von Scheren wider.");
        }

        protected override void OnFightTick(float dt)
        {
            float hp = Boss.HealthFraction;

            if (_nextBrood < broodAt.Length && hp <= broodAt[_nextBrood])
            {
                _nextBrood++;
                int n = 0;
                foreach (EnemySpawnArea a in broodAreas)
                    if (Summon(a) != null) n++;
                Announce($"Der See brodelt – {n} junge Salzkrebse schlüpfen.");
            }
            if (!_enraged && hp <= enrageAt)
            {
                _enraged = true;
                Announce($"{bossName} schlägt mit den Scheren auf den Grund. Das Wasser kommt jetzt schneller.");
            }

            // Salzspucke
            _spitTimer -= dt;
            if (_spitTimer <= 0f && _flood != FloodState.Flooding)
            {
                _spitTimer = spitInterval;
                List<Health> players = PlayersNear(Boss.transform.position, 35f);
                if (players.Count > 0)
                {
                    Health h = players[Random.Range(0, players.Count)];
                    Spits++;
                    Boss.ServerDelayAttacks(0.8f);
                    Boss.ServerAreaStrike(h.transform.position, spitRadius, spitFuse, spitDamage, "salt_spit");
                }
            }

            // Flut
            switch (_flood)
            {
                case FloodState.Calm:
                    _floodTimer -= dt;
                    if (_floodTimer <= 0f)
                    {
                        _flood = FloodState.Warning;
                        _floodPhase = floodWarn;
                        Cue(EncounterCues.FloodWarn, floodWarn);
                        Announce("Das Salzwasser beginnt zu steigen – auf die Plattformen!");
                    }
                    break;
                case FloodState.Warning:
                    _floodPhase -= dt;
                    if (_floodPhase <= 0f)
                    {
                        _flood = FloodState.Flooding;
                        _floodPhase = floodDuration;
                        _damageTick = 0f;
                        Floods++;
                        Cue(EncounterCues.Flood, floodDuration);
                    }
                    break;
                case FloodState.Flooding:
                    _floodPhase -= dt;
                    _damageTick -= dt;
                    if (_damageTick <= 0f)
                    {
                        _damageTick = 0.5f;
                        DamageFlooded(floodDamagePerSecond * 0.5f);
                    }
                    if (_floodPhase <= 0f)
                    {
                        _flood = FloodState.Calm;
                        _floodTimer = _enraged ? floodIntervalEnraged : floodInterval;
                        Cue(EncounterCues.FloodEnd, 0f);
                    }
                    break;
            }
        }

        /// <summary>Steht dieser Punkt in der Flut (in der Grotte und zu tief)?</summary>
        public bool IsFlooded(Vector3 feet)
        {
            Vector3 d = feet - transform.position;
            return Mathf.Abs(d.x) <= floodHalfSize && Mathf.Abs(d.z) <= floodHalfSize && feet.y < SafeHeightWorld && d.y > -3f;
        }

        private void DamageFlooded(float amount)
        {
            foreach (Health h in PlayersNear(transform.position, floodHalfSize * 1.5f))
            {
                if (!IsFlooded(h.transform.position)) continue;
                h.ApplyDamage(new DamageInfo
                {
                    Amount = Mathf.Round(amount),
                    Source = Boss != null ? Boss.gameObject : gameObject,
                    HitPoint = h.transform.position + Vector3.up * 0.5f,
                    Direction = Vector3.up,
                    Knockback = 0f,
                    WeaponId = (Boss != null && Boss.Definition != null ? Boss.Definition.Id : "enemy.salt_mother") + ".flood",
                    Unblockable = true
                });
            }
        }

        private void StopFlood()
        {
            if (_flood != FloodState.Calm) Cue(EncounterCues.FloodEnd, 0f);
            _flood = FloodState.Calm;
        }

        protected override void OnVictory()
        {
            StopFlood();
            Cue(EncounterCues.LoreGate, 1f, true);
            Announce($"{bossName} sinkt in den Salzsee. Im Süden kracht ein rostiger Riegel – die Lorenbahn ist frei.");
        }

        protected override void OnWipe()
        {
            StopFlood();
            Announce($"{bossName} gleitet zurück in den See. Das Wasser beruhigt sich.");
        }

        protected override void OnReset() => _flood = FloodState.Calm;
    }
}
