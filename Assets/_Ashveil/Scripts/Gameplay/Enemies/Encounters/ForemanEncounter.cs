using System.Collections.Generic;
using Ashveil.Gameplay.Combat;
using UnityEngine;
using UnityEngine.AI;

namespace Ashveil.Gameplay.Enemies
{
    /// <summary>
    /// M6.2 – Vorarbeiter Hask (Salzhalle, Boss 1 der Salzgruben).
    ///
    ///   Phase 1 (100–66 %) – Hackenschlaege, alle <see cref="chargeInterval"/> s wirft er Sprengladungen auf die
    ///                        Standorte von bis zu drei Spielern (Warnring, dann Explosion).
    ///   66 % und 33 %      – „Schicht!“: ruft je einen Arbeiter aus jedem Seitenstollen.
    ///   Phase 3 (unter 33 %) – wirft schneller und eine Ladung mehr.
    /// Wer im Ring steht, wenn er explodiert, nimmt schweren Schaden – die Hechtrolle hilft wie beim Stampfer.
    /// </summary>
    public sealed class ForemanEncounter : EnemyEncounter
    {
        [Header("Sprengladungen")]
        [SerializeField] private float chargeInterval = 12f;
        [SerializeField] private float chargeIntervalEnraged = 8f;
        [SerializeField] private float firstCharge = 6f;
        [SerializeField] private float chargeRadius = 3.5f;
        [SerializeField] private float chargeFuse = 2.6f;
        [SerializeField] private float chargeDamage = 70f;
        [SerializeField] private int chargesPerThrow = 3;

        [Header("Arbeiter rufen")]
        [SerializeField] private EnemySpawnArea[] workerAreas = new EnemySpawnArea[0];
        [SerializeField] private float[] summonAt = { 0.66f, 0.33f };

        /// <summary>Tests: Zeit bis zum ersten Wurf ueberschreiben (kleiner 0 = Einstellung).</summary>
        public static float FirstChargeOverride = -1f;

        private float _chargeTimer;
        private int _nextSummon;
        private bool _enraged;

        public int Throws { get; private set; }
        public int Waves => _nextSummon;
        public bool Enraged => _enraged;

        protected override void OnFightStarted()
        {
            _chargeTimer = FirstChargeOverride >= 0f ? FirstChargeOverride : firstCharge;
            _nextSummon = 0;
            _enraged = false;
            Throws = 0;
            Announce($"{bossName}: „Wer hat euch runtergelassen? Das ist MEINE Schicht!“");
        }

        protected override void OnFightTick(float dt)
        {
            float hp = Boss.HealthFraction;

            if (_nextSummon < summonAt.Length && hp <= summonAt[_nextSummon])
            {
                _nextSummon++;
                int n = 0;
                foreach (EnemySpawnArea a in workerAreas)
                    if (Summon(a) != null) n++;
                Announce($"{bossName} brüllt: „Schicht! Alle Mann an die Hacken!“ ({n} Arbeiter kommen aus den Stollen)");
            }

            if (!_enraged && summonAt.Length > 0 && hp <= summonAt[summonAt.Length - 1])
            {
                _enraged = true;
                Announce($"{bossName} reißt eine ganze Kiste Sprengladungen auf.");
            }

            _chargeTimer -= dt;
            if (_chargeTimer <= 0f)
            {
                _chargeTimer = _enraged ? chargeIntervalEnraged : chargeInterval;
                ThrowCharges(_enraged ? chargesPerThrow + 1 : chargesPerThrow);
            }
        }

        private void ThrowCharges(int count)
        {
            List<Health> players = PlayersNear(Boss.transform.position, 35f);
            if (players.Count == 0) return;
            Throws++;
            Boss.ServerDelayAttacks(1.2f);
            // Zuerst auf jeden Spieler eine, uebrige Ladungen zufaellig in die Naehe
            for (int i = 0; i < count; i++)
            {
                Health h = players[i % players.Count];
                Vector3 p = h.transform.position;
                if (i >= players.Count)
                {
                    Vector2 r = Random.insideUnitCircle.normalized * Random.Range(3f, 6f);
                    p += new Vector3(r.x, 0f, r.y);
                }
                if (NavMesh.SamplePosition(p, out NavMeshHit hit, 3f, NavMesh.AllAreas)) p = hit.position;
                Boss.ServerAreaStrike(p, chargeRadius, chargeFuse + i * 0.25f, chargeDamage, "dynamite");
            }
        }

        protected override void OnVictory() => Announce($"{bossName} fällt. Die Arbeiter lassen die Hacken fallen und verschwinden in den Stollen.");
        protected override void OnWipe() => Announce($"{bossName}: „Zurück an die Arbeit!“ – er kehrt an seinen Platz zurück.");
    }
}
