using System.Collections.Generic;
using Ashveil.Gameplay.Combat;
using UnityEngine;
using UnityEngine.AI;

namespace Ashveil.Gameplay.Enemies
{
    /// <summary>
    /// M6.5 – Der Ertrunkene Kapitaen (Wrackhoehle, optionaler Boss der Salzgruben). Sein Schiff lief vor langer Zeit
    /// in den Salzsee, der damals noch bis ans Meer reichte; das Salz hielt es fest, und ihn mit.
    ///
    ///   Breitseite  – alle <see cref="broadsideInterval"/> s zielt er auf einen Spieler: eine Reihe von Einschlaegen
    ///                 laeuft vom Kapitaen aus in dessen Richtung (jeder spaeter als der davor). Seitlich ausweichen.
    ///   „Alle Mann an Deck!“ – bei 60 % und 30 % kommen je zwei ertrunkene Matrosen aus dem Wrack.
    ///   Unter 30 %  – die Breitseite feuert doppelt (zweite Reihe auf einen anderen Spieler).
    /// </summary>
    public sealed class CaptainEncounter : EnemyEncounter
    {
        [Header("Breitseite")]
        [SerializeField] private float broadsideFirst = 6f;
        [SerializeField] private float broadsideInterval = 12f;
        [SerializeField] private int shotsPerLine = 5;
        [SerializeField] private float shotSpacing = 3f;
        [SerializeField] private float shotRadius = 2.2f;
        [SerializeField] private float firstFuse = 1.5f;
        [SerializeField] private float fuseStep = 0.3f;
        [SerializeField] private float shotDamage = 50f;

        [Header("Matrosen")]
        [SerializeField] private EnemySpawnArea[] crewAreas = new EnemySpawnArea[0];
        [SerializeField] private float[] crewAt = { 0.6f, 0.3f };

        /// <summary>Tests: erste Breitseite frueher (kleiner 0 = Einstellung).</summary>
        public static float FirstBroadsideOverride = -1f;

        private float _timer;
        private int _nextCrew;
        private bool _enraged;

        public int Broadsides { get; private set; }
        public int CrewWaves => _nextCrew;
        public bool Enraged => _enraged;
        /// <summary>Letzte Schusslinie (Welt, fuer Tests): Einschlagpunkte.</summary>
        public readonly List<Vector3> LastLine = new List<Vector3>();

        protected override void OnFightStarted()
        {
            _timer = FirstBroadsideOverride >= 0f ? FirstBroadsideOverride : broadsideFirst;
            _nextCrew = 0;
            _enraged = false;
            Broadsides = 0;
            Announce($"{bossName}: „Wer betritt mein Deck ohne Erlaubnis? Kanoniere – ach. Ihr seid ja alle tot. Dann eben ich.“");
        }

        protected override void OnFightTick(float dt)
        {
            float hp = Boss.HealthFraction;
            if (_nextCrew < crewAt.Length && hp <= crewAt[_nextCrew])
            {
                _nextCrew++;
                int n = 0;
                foreach (EnemySpawnArea a in crewAreas)
                    if (Summon(a) != null) n++;
                Announce($"{bossName} brüllt: „Alle Mann an Deck!“ – {n} ertrunkene Matrosen klettern aus dem Wrack.");
            }
            if (!_enraged && crewAt.Length > 0 && hp <= crewAt[crewAt.Length - 1])
            {
                _enraged = true;
                Announce($"{bossName} zieht die zweite Pistole.");
            }

            _timer -= dt;
            if (_timer > 0f) return;
            _timer = broadsideInterval;
            List<Health> players = PlayersNear(Boss.transform.position, 35f);
            if (players.Count == 0) return;
            Broadsides++;
            LastLine.Clear();
            Boss.ServerDelayAttacks(1.5f);
            int first = Random.Range(0, players.Count);
            FireLine(players[first]);
            if (_enraged && players.Count > 1) FireLine(players[(first + 1) % players.Count]);
        }

        private void FireLine(Health target)
        {
            Vector3 from = Boss.transform.position;
            Vector3 dir = target.transform.position - from;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = Boss.transform.forward;
            dir.Normalize();
            for (int i = 0; i < shotsPerLine; i++)
            {
                Vector3 p = from + dir * (shotSpacing * (i + 1));
                // Nur die Hoehe an den Boden anpassen; trifft die Linie Wrack oder Wand, endet sie dort
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 2.5f, NavMesh.AllAreas)) break;
                Vector3 flat = hit.position - p;
                flat.y = 0f;
                if (flat.magnitude > 1.2f) break;
                p.y = hit.position.y;
                LastLine.Add(p);
                Boss.ServerAreaStrike(p, shotRadius, firstFuse + i * fuseStep, shotDamage, "broadside");
            }
        }

        protected override void OnVictory() =>
            Announce($"{bossName} sinkt auf die Knie. „Endlich … Landgang.“ Das Salz um das Wrack knirscht, als ließe es los.");

        protected override void OnWipe() => Announce($"{bossName}: „Über Bord mit euch!“ – er kehrt ans Ruder zurück.");
    }
}
