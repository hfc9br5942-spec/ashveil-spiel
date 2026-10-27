using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Targeting;
using Ashveil.Gameplay.Weapons;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// CORE-005 / NET-012 (02.10.2026): einfacher Lasttest-Bot. Nur mit "-ashveil-bot" auf der eigenen Figur aktiv.
    /// Spielt wie ein Mensch ueber dieselben Eingaben (keine Sonderwege am Server): laeuft, dreht sich, greift Gegner
    /// in der Naehe an und bleibt grob im Umkreis seines Startpunkts. Keine Grafik noetig (-batchmode -nographics).
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public sealed class LoadTestBot : MonoBehaviour
    {
        public static bool Requested => TestBuild.Enabled && ProfileSelection.HasArg("-ashveil-bot");

        // REQ-20261019-003: Der Netzcode kennt den Bot nicht mehr; der Bot meldet sich beim Start der eigenen Figur an.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Anmelden()
        {
            NetworkPlayer.LocalPlayerStarted -= OnLocalPlayer;
            if (TestBuild.Enabled) NetworkPlayer.LocalPlayerStarted += OnLocalPlayer;
        }

        private static void OnLocalPlayer(NetworkPlayer player)
        {
            if (Requested && player.GetComponent<LoadTestBot>() == null) player.gameObject.AddComponent<LoadTestBot>();
        }

        private PlayerInputReader _input;
        private TargetingSystem _targeting;
        private WeaponUser _weapons;
        private Health _health;
        private Vector3 _home;
        private float _until;
        private float _forward, _turn;
        private bool _fighting;
        private CombatTarget _goal; // eigenes Ziel: das Zielsystem laesst Ziele ueber 60 m fallen, der Bot laeuft erst hin
        private System.Random _rng;
        // Nachweis im Bot-Log: sucht er Gegner, findet er welche, kaempft er wirklich?
        private int _seeks, _found, _fights;
        private float _nextReport = 30f;

        private const float LeashRadius = 140f; // reicht bis zu den ersten Wolfs- und Verblichenen-Gebieten (80–90 m)

        private void Start()
        {
            _input = GetComponent<PlayerInputReader>();
            _targeting = GetComponent<TargetingSystem>();
            _weapons = GetComponent<WeaponUser>();
            _health = GetComponent<Health>();
            _home = transform.position;
            _rng = new System.Random(System.Environment.TickCount ^ System.Diagnostics.Process.GetCurrentProcess().Id);
            Debug.Log("[Ashveil][Bot] Lasttest-Bot aktiv.");
        }

        private float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        // Bots teilen sich einen Rechner mit dem Server: hoechstens 20 Bilder/s, damit der Server die Rechenzeit bekommt
        private float _frameStart;

        private void LateUpdate()
        {
            float spent = Time.realtimeSinceStartup - _frameStart;
            int sleep = Mathf.FloorToInt((0.05f - spent) * 1000f);
            if (sleep > 0) System.Threading.Thread.Sleep(sleep);
            _frameStart = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            if (_input == null) return;
            if (_health != null && !_health.IsAlive)
            {
                _input.InjectMovement(0f, false);
                _input.InjectTurn(0f);
                _fighting = false;
                _goal = null;
                return;
            }
            if (Time.time >= _until && !_fighting) Decide();
            else if (Time.time >= _until) { _fighting = false; _goal = null; Decide(); }
            if (Time.time >= _nextReport) Report();
            if (!_fighting)
            {
                _input.InjectMovement(_forward, false);
                _input.InjectTurn(_turn);
            }
            else
            {
                CombatTarget t = _goal;
                if (t == null || !t.IsAttackable || t.Health == null || !t.Health.IsAlive)
                { _fighting = false; _goal = null; _until = 0f; _input.InjectMovement(0f, false); _input.InjectTurn(0f); return; }
                // Erst in Reichweite anvisieren (wie ein Spieler mit Tab), dann angreifen
                bool close = (t.transform.position - transform.position).sqrMagnitude < 35f * 35f;
                if (close && _targeting != null && _targeting.Current != t) _targeting.SetTarget(t);
                // Wie ein Mensch: zum Ziel drehen und bis in Nahkampfreichweite laufen
                Vector3 to = t.transform.position - transform.position;
                to.y = 0f;
                float angle = Vector3.SignedAngle(transform.forward, to, Vector3.up);
                _input.InjectTurn(Mathf.Abs(angle) > 8f ? Mathf.Clamp(angle / 30f, -1f, 1f) : 0f);
                _input.InjectMovement(to.magnitude > 2.2f && Mathf.Abs(angle) < 60f ? 1f : 0f, false);
                if (close && _weapons != null && !_weapons.IsAutoAttacking && Time.frameCount % 10 == 0) _input.InjectCombat(new CombatInput { Ability1 = true });
            }
        }

        private void Decide()
        {
            Vector3 toHome = _home - transform.position;
            toHome.y = 0f;
            // Zu weit weg: zurueck zum Startbereich drehen und laufen
            if (toHome.magnitude > LeashRadius)
            {
                float angle = Vector3.SignedAngle(transform.forward, toHome, Vector3.up);
                _forward = Mathf.Abs(angle) < 45f ? 1f : 0.2f;
                _turn = Mathf.Clamp(angle / 45f, -1f, 1f);
                _fighting = false;
                _goal = null;
                _until = Time.time + R(0.5f, 1.2f);
                return;
            }
            double roll = _rng.NextDouble();
            if (roll < 0.30)
            {
                _seeks++;
                CombatTarget enemy = NearestEnemy(120f); // wie ein Spieler: Gegner suchen und hinlaufen
                if (enemy != null) _found++;
                if (enemy != null)
                {
                    _fights++;
                    _goal = enemy;
                    _fighting = true;
                    _until = Time.time + R(15f, 30f);
                    return;
                }
            }
            if (roll < 0.85)
            {
                _forward = 1f;
                _turn = R(-0.6f, 0.6f);
                _until = Time.time + R(1.5f, 5f);
            }
            else
            {
                _forward = 0f;
                _turn = 0f;
                _until = Time.time + R(1f, 3f); // kurz stehen bleiben
            }
            _fighting = false;
            _goal = null;
        }

        private void Report()
        {
            _nextReport = Time.time + 30f;
            int hostile = 0;
            float nearest = float.MaxValue;
            var all = CombatTarget.All;
            for (int i = 0; i < all.Count; i++)
            {
                CombatTarget t = all[i];
                if (t == null || t.Faction != Faction.Hostile) continue;
                hostile++;
                nearest = Mathf.Min(nearest, Vector3.Distance(t.transform.position, transform.position));
            }
            CombatTarget cur = _targeting != null ? _targeting.Current : null;
            Debug.Log($"[Ashveil][Bot] Stand: {Time.time:0} s, Abstand Start {Vector3.Distance(transform.position, _home):0} m, " +
                      $"Ziele bekannt {all.Count} (feindlich {hostile}, naechstes {(hostile > 0 ? nearest.ToString("0") + " m" : "-")}), " +
                      $"gesucht {_seeks}, gefunden {_found}, Kaempfe {_fights}, kaempft {_fighting}, Ziel {(cur != null ? cur.name : "-")}, Weg {(_goal != null ? Vector3.Distance(_goal.transform.position, transform.position).ToString("0") + " m" : "-")}, " +
                      $"Autoangriff {(_weapons != null && _weapons.IsAutoAttacking)}, lebt {(_health == null || _health.IsAlive)}");
        }

        private CombatTarget NearestEnemy(float range)
        {
            CombatTarget best = null;
            float bestD = range * range;
            var all = CombatTarget.All;
            for (int i = 0; i < all.Count; i++)
            {
                CombatTarget t = all[i];
                if (t == null || !t.IsAttackable || t.Faction != Faction.Hostile) continue;
                float d = (t.transform.position - transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = t; }
            }
            return best;
        }
    }
}
