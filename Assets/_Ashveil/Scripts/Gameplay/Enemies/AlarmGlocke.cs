using System.Collections.Generic;
using Ashveil.Core;
using Ashveil.Gameplay.Combat;
using UnityEngine;

namespace Ashveil.Gameplay.Enemies
{
    /// <summary>
    /// Westweiden, Kreidekeller (12.10.2026, Gebietsblatt v2 §7 Abschnitt 3): Alarmglocke an einem Spannseil quer über dem
    /// Brandungsgang – eine lesbare Falle (Seil und Glocke sind sichtbar; W23: „Hör auf die Glocke im Gang – wenn sie läutet,
    /// hast du sie geläutet.“). Wer das Seil berührt, läutet die Glocke: Die Verstärkung (Gebiete „nur gerufen“) kommt und greift
    /// an. Danach ist die Glocke eine Weile still. Läuft nur auf dem Server; ohne Spieler in der Nähe gehen die Gerufenen wieder.
    /// </summary>
    public sealed class AlarmGlocke : MonoBehaviour
    {
        private static readonly List<AlarmGlocke> Alle = new List<AlarmGlocke>();
        private static readonly Collider[] Puffer = new Collider[8];

        [SerializeField] private Vector3 seilA, seilB;
        [SerializeField] private float seilRadius = 0.45f;
        [SerializeField] private EnemySpawnArea[] verstaerkung = new EnemySpawnArea[0];
        [SerializeField] private float sperre = 120f;
        [SerializeField] private string ansage = "Eine Glocke läutet im Gang. Von unten kommen Schritte.";

        private readonly List<EnemyBrain> _gerufen = new List<EnemyBrain>();
        private float _naechste, _tick, _leer;

        public static IReadOnlyList<AlarmGlocke> Liste => Alle;
        public int Ausloesungen { get; private set; }
        public IReadOnlyList<EnemyBrain> Gerufen => _gerufen;
        public Vector3 SeilMitte => (seilA + seilB) * 0.5f;

        public void Configure(Vector3 a, Vector3 b, EnemySpawnArea[] areas)
        {
            seilA = a;
            seilB = b;
            verstaerkung = areas;
        }

        private void OnEnable() { if (!Alle.Contains(this)) Alle.Add(this); }
        private void OnDisable() => Alle.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Alle.Clear();

        private void Update()
        {
            if (EnemyEncounter.Summoner == null) return;   // nur auf dem Server
            _tick -= Time.deltaTime;
            if (_tick > 0f) return;
            _tick = 0.2f;
            Aufraeumen(0.2f);
            if (Time.time < _naechste) return;
            int n = Physics.OverlapCapsuleNonAlloc(seilA, seilB, seilRadius, Puffer, LayerMask.GetMask(GameLayers.Player), QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Health h = Puffer[i].GetComponentInParent<Health>();
                if (h != null && h.IsAlive && h.isActiveAndEnabled) { Ausloesen(h); return; }
            }
        }

        /// <summary>Server: Glocke läuten – Verstärkung erscheint und greift <paramref name="wer"/> an.</summary>
        public int Ausloesen(Health wer)
        {
            if (EnemyEncounter.Summoner == null || Time.time < _naechste) return 0;
            _naechste = Time.time + sperre;
            Ausloesungen++;
            int n = 0;
            foreach (EnemySpawnArea a in verstaerkung)
            {
                if (a == null) continue;
                EnemyBrain b = EnemyEncounter.Summoner(a);
                if (b == null) continue;
                _gerufen.Add(b);
                if (wer != null) b.ServerEngage(wer);
                n++;
            }
            EnemyEncounter.AnsageOffen(transform.position, ansage);
            _leer = 0f;
            return n;
        }

        /// <summary>Gerufene, die keinen Spieler mehr in der Nähe haben, gehen nach 30 s zurück an die Arbeit (verschwinden).</summary>
        private void Aufraeumen(float dt)
        {
            _gerufen.RemoveAll(b => b == null || b.Health == null || !b.Health.IsAlive);
            if (_gerufen.Count == 0) return;
            bool spieler = Physics.OverlapSphereNonAlloc(SeilMitte, 45f, Puffer, LayerMask.GetMask(GameLayers.Player), QueryTriggerInteraction.Ignore) > 0;
            bool kampf = _gerufen.Exists(b => b.InCombat);
            _leer = spieler || kampf ? 0f : _leer + dt;
            if (_leer < 30f) return;
            foreach (EnemyBrain b in _gerufen) EnemyEncounter.Remover?.Invoke(b);
            _gerufen.Clear();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(seilA, seilB);
        }
    }
}
