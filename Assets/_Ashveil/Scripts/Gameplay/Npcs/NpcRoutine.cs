using System;
using Ashveil.Core;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using UnityEngine;
using UnityEngine.AI;

namespace Ashveil.Gameplay.Npcs
{
    /// <summary>Ein Abschnitt im Tagesablauf: ab Stunde 'from' geht der NPC zum Ort und tut dort 'activity'.</summary>
    [Serializable]
    public struct RoutineStep
    {
        [Tooltip("Beginn (Stunde 0–24)")] public float from;
        public Vector3 pos;
        public float yaw;
        [Tooltip("Stand, Sit, SitTalk, Sleep, Dance, Laugh oder ein Zustand im Layer 'Emote' (Chop, Talk, FoldArms …). " +
                 "Mehrere mit '|' – dann wechselt es (fuer alle gleich) etwa jede Spielviertelstunde.")]
        public string activity;
        [Tooltip("Sitzhoehe (m) fuer Sit/SitTalk, 0 = Boden")] public float seat;
        [Tooltip("Traegt beim Hingehen etwas (Walk_Carry)")] public bool carry;
        [Tooltip("M8.5: schiebt beim Hingehen seinen Handkarren und stellt ihn vor sich ab")] public bool cart;
        [Tooltip("M8.6: faehrt auf dem Kutschbock seines Fuhrwerks (sitzt, Wagen und Zugtier folgen)")] public bool ride;
        [Tooltip("M8.6: Wegpunkte (z. B. die Strasse) statt NavMesh – der Weg folgt ihnen, Hoehe vom Gelaende")] public Vector3[] via;
        [Tooltip("Kurze Beschreibung fuer Tests/Anzeige")] public string note;
    }

    /// <summary>
    /// M8.3: Tagesablauf eines NPCs – rein aus der Weltuhr berechnet (<see cref="WorldClock"/>). Server und alle Clients
    /// kommen so ohne Netzverkehr auf dieselbe Stelle und Taetigkeit; Wege laufen ueber das gebackene NavMesh.
    /// Dienst-NPCs (Haendler, Auftraggeber) bleiben im Plan jederzeit im Lager und ansprechbar.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    [DisallowMultipleComponent]
    public sealed class NpcRoutine : MonoBehaviour
    {
        [SerializeField] private RoutineStep[] steps = Array.Empty<RoutineStep>();
        [SerializeField] private Animator animator;
        [SerializeField] private EmotePoser poser;
        [Tooltip("Wird beim Schlafen ausgeblendet (Modell)")] [SerializeField] private GameObject visual;
        [Tooltip("Wird beim Schlafen ausgeblendet (Namensschild)")] [SerializeField] private GameObject label;
        [SerializeField] private Collider body;
        [SerializeField] private int seed;
        [SerializeField] private float walkSpeed = 1.35f;
        [Tooltip("M8.5: Handkarren, den der NPC in Abschnitten mit 'cart' schiebt (sonst bleibt er am letzten Platz stehen)")]
        [SerializeField] private NpcCart cartProp;

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int MoveZId = Animator.StringToHash("MoveZ");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int WalkingId = Animator.StringToHash("Walking");

        private int _pathStep = -1;
        private long _pathDay = long.MinValue;
        private Vector3[] _corners = Array.Empty<Vector3>();
        private float[] _cum = Array.Empty<float>();
        private float _pathLength;
        private string _shownActivity = "\u0000";
        private float _shownSeat;
        private bool _hidden;

        public int StepIndex { get; private set; } = -1;
        public bool Walking { get; private set; }
        public bool Sleeping => _hidden;
        public string Activity { get; private set; } = "";
        public RoutineStep CurrentStep => StepIndex >= 0 && StepIndex < steps.Length ? steps[StepIndex] : default;
        public int StepCount => steps.Length;
        public RoutineStep Step(int i) => steps[i];
        public NpcCart Cart => cartProp;
        public void BindCart(NpcCart cart) => cartProp = cart;
        /// <summary>Gehgeschwindigkeit (m/s) – Fuhrwerke fahren etwas schneller als Fussgaenger gehen.</summary>
        public void SetWalkSpeed(float metersPerSecond) => walkSpeed = Mathf.Max(0.3f, metersPerSecond);
        public float WalkSpeed => walkSpeed;

        public void Configure(RoutineStep[] s, int npcSeed)
        {
            steps = s ?? Array.Empty<RoutineStep>();
            Array.Sort(steps, (a, b) => a.from.CompareTo(b.from));
            seed = npcSeed;
        }

        public void Bind(Animator anim, EmotePoser emote, GameObject model, GameObject nameLabel, Collider col)
        {
            animator = anim;
            poser = emote;
            visual = model;
            label = nameLabel;
            body = col;
        }

        /// <summary>Welcher Abschnitt gilt zur fortlaufenden Stunde totalHours? (Index, Beginn als fortlaufende Stunde)</summary>
        public int StepAt(double totalHours, out double startTotal)
        {
            startTotal = 0;
            if (steps.Length == 0) return -1;
            double day = Math.Floor(totalHours / 24.0);
            double h = totalHours - day * 24.0;
            int idx = -1;
            for (int i = 0; i < steps.Length; i++) if (steps[i].from <= h) idx = i;
            if (idx < 0) { idx = steps.Length - 1; day -= 1; }
            startTotal = day * 24.0 + steps[idx].from;
            return idx;
        }

        private void OnEnable()
        {
            NpcRoutineRegistry.Add(this);
            Ashveil.Core.WorldCells.CellReady += OnCellReady;
        }

        private void OnDisable()
        {
            NpcRoutineRegistry.Remove(this);
            Ashveil.Core.WorldCells.CellReady -= OnCellReady;
        }

        /// <summary>Weg ohne NavMesh als gerade Linie gebaut? Sobald eine Zelle bereit ist, neu berechnen (Streaming).</summary>
        private bool _straightFallback;

        private void OnCellReady(string cellId)
        {
            if (_straightFallback) _pathStep = -1;
        }

        private void LateUpdate() => Tick();

        /// <summary>Stand und Taetigkeit fuer die aktuelle Weltzeit setzen (auch vom Server ohne Darstellung genutzt).</summary>
        public void Tick()
        {
            if (steps.Length == 0) return;
            double total = WorldClock.TotalHours;
            int idx = StepAt(total, out double start);
            long day = (long)Math.Floor(start / 24.0);
            int prev = (idx - 1 + steps.Length) % steps.Length;
            if (idx != _pathStep || day != _pathDay) BuildPath(steps[prev].pos, steps[idx].pos, steps[idx].via, idx, day);
            StepIndex = idx;
            RoutineStep st = steps[idx];

            double elapsed = (total - start) * WorldClock.SecondsPerHour; // echte Sekunden seit Beginn
            float walked = (float)elapsed * walkSpeed;
            if (walked < _pathLength - 0.05f)
            {
                Walking = true;
                Vector3 p = PointAt(walked, out Vector3 dir);
                Place(p, dir.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(dir) : transform.rotation);
                SetHidden(false);
                Animate(walkSpeed, true);
                bool riding = st.ride && cartProp != null;
                bool pushing = !riding && st.cart && cartProp != null;
                Activity = riding ? "Ride" : pushing ? "Push" : st.carry ? "Carry" : "Walk";
                if (riding) ShowActivity("Sit", cartProp.Seat, false);
                else ShowActivity(pushing ? "Push" : st.carry ? "Carry" : null, 0f, false);
                if (riding || pushing) cartProp.Place(transform.position + transform.forward * cartProp.Reach, transform.rotation);
                else PlaceCartParked(idx);
                return;
            }

            Walking = false;
            Place(st.pos, Quaternion.Euler(0f, st.yaw, 0f));
            PlaceCartParked(idx);
            Animate(0f, false);
            string act = Pick(st.activity, day, idx, elapsed - _pathLength / walkSpeed);
            Activity = act;
            if (act == "Sleep")
            {
                SetHidden(true);
                return;
            }
            SetHidden(false);
            ShowActivity(act, st.seat, true);
        }

        /// <summary>Karren am Platz des letzten Abschnitts mit 'cart' (vor dem NPC, in seiner Blickrichtung).</summary>
        private void PlaceCartParked(int idx)
        {
            if (cartProp == null) return;
            for (int k = 0; k < steps.Length; k++)
            {
                RoutineStep s = steps[(idx - k + steps.Length) % steps.Length];
                if (!s.cart && !s.ride) continue;
                Quaternion r = Quaternion.Euler(0f, s.yaw, 0f);
                cartProp.Place(s.pos + r * Vector3.forward * cartProp.Reach, r);
                return;
            }
        }

        /// <summary>Bei mehreren Taetigkeiten ("Talk|FoldArms") alle ~75 s echte Zeit wechseln – fuer alle Rechner gleich.</summary>
        private string[][] _options;

        private string Pick(string activity, long day, int idx, double secondsThere)
        {
            if (string.IsNullOrEmpty(activity)) return "Stand";
            if (activity.IndexOf('|') < 0) return activity;
            if (_options == null || _options.Length != steps.Length) _options = new string[steps.Length][];
            string[] opts = _options[idx] ??= activity.Split('|');
            long slot = (long)Math.Floor(Math.Max(0.0, secondsThere) / 75.0);
            int h = Hash(day, idx, slot);
            return opts[(int)((uint)h % (uint)opts.Length)];
        }

        private int Hash(long day, int idx, long slot)
        {
            unchecked
            {
                int h = seed * 73856093 ^ (int)day * 19349663 ^ idx * 83492791 ^ (int)slot * 2654435;
                h ^= h >> 13;
                h *= 1274126177;
                return h ^ (h >> 16);
            }
        }

        private void ShowActivity(string act, float seat, bool arrived)
        {
            if (poser == null) return;
            if (act == _shownActivity && Mathf.Abs(seat - _shownSeat) < 0.001f) return;
            _shownActivity = act;
            _shownSeat = seat;
            switch (act)
            {
                case null: case "": case "Stand": case "Walk":
                    poser.PlayActivity(null);
                    break;
                case "Sit":
                    poser.Play(EmoteId.Sit, 1f, seat);
                    break;
                case "SitTalk":
                    poser.Play(EmoteId.Sit, 1f, seat, "SitTalk");
                    break;
                case "Dance":
                    poser.Play(EmoteId.Dance, 0f);
                    break;
                case "Laugh":
                    poser.Play(EmoteId.Laugh, 0f);
                    break;
                case "Wave":
                    poser.Play(EmoteId.Wave, 0f);
                    break;
                case "Cheer":
                    poser.Play(EmoteId.Cheer, 0f);
                    break;
                default:
                    poser.PlayActivity(act);
                    break;
            }
        }

        private void Animate(float speed, bool walking)
        {
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return;
            animator.SetFloat(SpeedId, speed);
            animator.SetFloat(MoveZId, walking ? 1f : 0f);
            animator.SetBool(GroundedId, true);
            animator.SetBool(WalkingId, walking);
        }

        private void Place(Vector3 p, Quaternion r)
        {
            transform.SetPositionAndRotation(p, Quaternion.Slerp(transform.rotation, r, Walking ? 0.25f : 1f));
        }

        private void SetHidden(bool hidden)
        {
            if (hidden == _hidden) return;
            _hidden = hidden;
            if (visual != null) visual.SetActive(!hidden);
            if (label != null) label.SetActive(!hidden);
            if (body != null) body.enabled = !hidden;
            _shownActivity = "\u0000";
        }

        // ------------------------------------------------------------------ Wege

        private void BuildPath(Vector3 from, Vector3 to, Vector3[] via, int idx, long day)
        {
            _pathStep = idx;
            _pathDay = day;
            if (via != null && via.Length > 0)
            {
                BuildViaPath(from, via, to);
                return;
            }
            if ((from - to).sqrMagnitude < 0.04f)
            {
                _corners = new[] { to };
                _cum = new[] { 0f };
                _pathLength = 0f;
                return;
            }
            var path = new NavMeshPath();
            bool ok = NavMesh.SamplePosition(from, out NavMeshHit a, 2f, NavMesh.AllAreas) &&
                      NavMesh.SamplePosition(to, out NavMeshHit b, 2f, NavMesh.AllAreas) &&
                      NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) &&
                      path.status == NavMeshPathStatus.PathComplete && path.corners.Length >= 2;
            if (ok)
            {
                _corners = new Vector3[path.corners.Length + 2];
                _corners[0] = from;
                Array.Copy(path.corners, 0, _corners, 1, path.corners.Length);
                _corners[_corners.Length - 1] = to;
            }
            else _corners = new[] { from, to };
            _straightFallback = !ok;
            _cum = new float[_corners.Length];
            for (int i = 1; i < _corners.Length; i++) _cum[i] = _cum[i - 1] + Vector3.Distance(_corners[i - 1], _corners[i]);
            _pathLength = _cum[_cum.Length - 1];
        }

        /// <summary>Weg ueber feste Wegpunkte (Strasse): in Stuecke von hoechstens 4 m geteilt, Hoehe vom Gelaende.</summary>
        private void BuildViaPath(Vector3 from, Vector3[] via, Vector3 to)
        {
            var pts = new System.Collections.Generic.List<Vector3> { from };
            pts.AddRange(via);
            pts.Add(to);
            var dense = new System.Collections.Generic.List<Vector3> { from };
            for (int i = 1; i < pts.Count; i++)
            {
                Vector3 a = pts[i - 1], b = pts[i];
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 4f));
                for (int k = 1; k <= n; k++)
                {
                    Vector3 p = Vector3.Lerp(a, b, k / (float)n);
                    bool end = i == pts.Count - 1 && k == n;
                    // Hoehe vom Gelaende unter dem Punkt (mehrere Kacheln moeglich: Kareth + Grenzgebirge)
                    if (!end && Ashveil.Core.WorldGround.TrySample(p, out float gh)) p.y = gh;
                    dense.Add(p);
                }
            }
            _corners = dense.ToArray();
            _cum = new float[_corners.Length];
            for (int i = 1; i < _corners.Length; i++) _cum[i] = _cum[i - 1] + Vector3.Distance(_corners[i - 1], _corners[i]);
            _pathLength = _cum[_cum.Length - 1];
        }

        private Vector3 PointAt(float d, out Vector3 dir)
        {
            dir = Vector3.zero;
            for (int i = 1; i < _corners.Length; i++)
            {
                if (d > _cum[i] && i < _corners.Length - 1) continue;
                float seg = _cum[i] - _cum[i - 1];
                float t = seg > 1e-4f ? Mathf.Clamp01((d - _cum[i - 1]) / seg) : 1f;
                dir = _corners[i] - _corners[i - 1];
                dir.y = 0f;
                return Vector3.Lerp(_corners[i - 1], _corners[i], t);
            }
            return _corners[_corners.Length - 1];
        }
    }

    /// <summary>Alle NPCs mit Tagesablauf (Suche nach Name fuer Tests und spaetere Systeme).</summary>
    public static class NpcRoutineRegistry
    {
        private static readonly System.Collections.Generic.List<NpcRoutine> List = new System.Collections.Generic.List<NpcRoutine>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => List.Clear();

        public static System.Collections.Generic.IReadOnlyList<NpcRoutine> All => List;
        internal static void Add(NpcRoutine r) { if (!List.Contains(r)) List.Add(r); }
        internal static void Remove(NpcRoutine r) => List.Remove(r);

        public static NpcRoutine Find(string name)
        {
            foreach (NpcRoutine r in List)
                if (r != null && (r.name == name || r.name.EndsWith(" " + name))) return r;
            return null;
        }
    }
}
