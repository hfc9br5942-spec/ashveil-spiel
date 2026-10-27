using System;
using Ashveil.Core;
using UnityEngine;
using UnityEngine.AI;

namespace Ashveil.Gameplay.Npcs
{
    /// <summary>Abschnitt im Tierplan: ab Stunde 'from' in einem Bereich umherstreifen oder an einer Stelle verharren.</summary>
    [Serializable]
    public struct AnimalStep
    {
        public float from;
        public Vector3 center;
        [Tooltip("0 = an der Stelle bleiben; sonst Umherstreifen in diesem Radius")] public float radius;
        public float yaw;
        [Tooltip("Haltung beim Verharren (Sit, Lie, Sleep, Graze, Groom, Look, Stand, Sniff)")] public AnimalPose pose;
        public string note;
    }

    /// <summary>
    /// M8.4: Tagesablauf eines Tieres – wie bei den NPCs rein aus der Weltuhr berechnet (alle sehen dasselbe, kein
    /// Netzverkehr). Beim Umherstreifen wird alle ~14 s echte Zeit ein neues Ziel im Bereich gewaehlt (fester Zufall aus
    /// Tier, Tag und Abschnitt), dazwischen eine arttypische Haltung (grasen, sitzen, putzen …).
    /// </summary>
    [DefaultExecutionOrder(-19)]
    [DisallowMultipleComponent]
    public sealed class AnimalRoutine : MonoBehaviour
    {
        [SerializeField] private AnimalStep[] steps = Array.Empty<AnimalStep>();
        [SerializeField] private AnimalVisual visual;
        [SerializeField] private AnimalPose[] idlePoses = { AnimalPose.Stand, AnimalPose.Look };
        [SerializeField] private float speed = 1f;
        [SerializeField] private float slotSeconds = 14f;
        [SerializeField] private int seed;

        private int _pathStep = -1;
        private long _pathDay = long.MinValue;
        private Vector3[] _corners = Array.Empty<Vector3>();
        private float[] _cum = Array.Empty<float>();
        private float _pathLength;

        public int StepIndex { get; private set; } = -1;
        public bool Walking { get; private set; }
        public AnimalPose Pose { get; private set; }
        public AnimalStep CurrentStep => StepIndex >= 0 && StepIndex < steps.Length ? steps[StepIndex] : default;

        public void Configure(AnimalStep[] s, AnimalVisual v, AnimalPose[] idles, float walkSpeed, int animalSeed)
        {
            steps = s ?? Array.Empty<AnimalStep>();
            Array.Sort(steps, (a, b) => a.from.CompareTo(b.from));
            visual = v;
            if (idles != null && idles.Length > 0) idlePoses = idles;
            speed = walkSpeed;
            seed = animalSeed;
        }

        private void OnEnable() => AnimalRegistry.Add(this);
        private void OnDisable() => AnimalRegistry.Remove(this);

        private int StepAt(double total, out double start)
        {
            double day = Math.Floor(total / 24.0), h = total - day * 24.0;
            int idx = -1;
            for (int i = 0; i < steps.Length; i++) if (steps[i].from <= h) idx = i;
            if (idx < 0) { idx = steps.Length - 1; day -= 1; }
            start = day * 24.0 + steps[idx].from;
            return idx;
        }

        private float Hash01(long a, long b, int c)
        {
            unchecked
            {
                uint h = (uint)(seed * 73856093) ^ (uint)(a * 19349663) ^ (uint)(b * 83492791) ^ (uint)(c * 2654435761);
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        /// <summary>Ziel k im Bereich des Abschnitts (fester Zufall).</summary>
        private Vector3 Target(AnimalStep st, long day, int idx, long k)
        {
            if (st.radius <= 0.01f) return st.center;
            float a = Hash01(day * 31 + idx, k, 1) * Mathf.PI * 2f, r = Mathf.Sqrt(Hash01(day * 31 + idx, k, 2)) * st.radius;
            Vector3 p = st.center + new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
            p.y = st.center.y;
            return p;
        }

        private int Prev(int idx) => (idx - 1 + steps.Length) % steps.Length;

        /// <summary>Laenge des Abschnitts in Stunden.</summary>
        private double Duration(int idx)
        {
            if (steps.Length == 1) return 24.0;
            double d = steps[(idx + 1) % steps.Length].from - steps[idx].from;
            return d <= 0 ? d + 24.0 : d;
        }

        /// <summary>Erster Slot eines Abschnitts: lang genug fuer den Weg vom vorigen Bereich (nur aus den Plandaten).</summary>
        private double FirstSlot(int idx)
        {
            float d = Vector3.Distance(steps[Prev(idx)].center, steps[idx].center);
            return Math.Max(slotSeconds, d * 1.5 / Mathf.Max(0.05f, speed) + 2.0);
        }

        /// <summary>Slot zur Zeit 'elapsed' (Sekunden seit Abschnittsbeginn) und Zeit im Slot.</summary>
        private long Slot(int idx, double elapsed, out double inSlot)
        {
            double first = FirstSlot(idx);
            if (elapsed < first) { inSlot = elapsed; return 0; }
            long k = 1 + (long)Math.Floor((elapsed - first) / slotSeconds);
            inSlot = elapsed - first - (k - 1) * slotSeconds;
            return k;
        }

        /// <summary>Wo das Tier am Ende eines Abschnitts steht.</summary>
        private Vector3 EndPos(int idx, long day)
        {
            AnimalStep st = steps[idx];
            if (st.radius <= 0.01f) return st.center;
            long k = Slot(idx, Duration(idx) * WorldClock.SecondsPerHour - 0.01, out _);
            return Target(st, day, idx, k);
        }

        private void LateUpdate() => Tick();

        public void Tick()
        {
            if (steps.Length == 0) return;
            double total = WorldClock.TotalHours;
            int idx = StepAt(total, out double start);
            long day = (long)Math.Floor(start / 24.0);
            AnimalStep st = steps[idx];
            StepIndex = idx;
            double elapsed = (total - start) * WorldClock.SecondsPerHour;

            if (idx != _pathStep || day != _pathDay)
            {
                int prev = Prev(idx);
                long prevDay = (long)Math.Floor((start - Duration(prev)) / 24.0);
                BuildPath(EndPos(prev, prevDay), Target(st, day, idx, 0));
                _pathStep = idx;
                _pathDay = day;
            }

            long k = st.radius <= 0.01f ? 0 : Slot(idx, elapsed, out _);
            if (k == 0)
            {
                float walked = (float)elapsed * speed;
                if (walked < _pathLength - 0.03f)
                {
                    Vector3 p = PointAt(walked, out Vector3 dir);
                    Apply(p, dir, true, AnimalPose.Walk);
                    return;
                }
                if (st.radius <= 0.01f)
                {
                    Apply(st.center, Quaternion.Euler(0f, st.yaw, 0f) * Vector3.forward, false, st.pose);
                    return;
                }
                Apply(Target(st, day, idx, 0), transform.forward, false, IdlePose(day, idx, 0));
                return;
            }
            // Umherstreifen: Slot k laeuft von Ziel k-1 zu Ziel k, danach eine Haltung
            Slot(idx, elapsed, out double ts);
            Vector3 from = Target(st, day, idx, k - 1), to = Target(st, day, idx, k);
            float dist = Vector3.Distance(from, to);
            float walk = dist / Mathf.Max(0.05f, speed);
            if (ts < walk && dist > 0.05f)
            {
                Apply(Vector3.Lerp(from, to, (float)(ts / walk)), to - from, true, AnimalPose.Walk);
                return;
            }
            Apply(to, dist > 0.05f ? to - from : transform.forward, false, IdlePose(day, idx, k));
        }

        private AnimalPose IdlePose(long day, int idx, long k)
        {
            AnimalStep st = steps[idx];
            if (st.pose != AnimalPose.Stand && st.pose != AnimalPose.Walk && Hash01(day * 31 + idx, k, 5) < 0.35f) return st.pose; // Vorzugshaltung des Abschnitts
            return idlePoses[(int)(Hash01(day * 31 + idx, k, 3) * idlePoses.Length) % idlePoses.Length];
        }

        private void Apply(Vector3 p, Vector3 dir, bool walking, AnimalPose pose)
        {
            Walking = walking;
            Pose = pose;
            dir.y = 0f;
            Quaternion rot = dir.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(dir) : transform.rotation;
            transform.SetPositionAndRotation(p, walking ? Quaternion.Slerp(transform.rotation, rot, 0.2f) : Quaternion.Slerp(transform.rotation, rot, 0.08f));
            if (visual != null) visual.Set(pose, walking ? speed : 0f);
        }

        private void BuildPath(Vector3 from, Vector3 to)
        {
            if ((from - to).sqrMagnitude < 0.04f)
            {
                _corners = new[] { to };
                _cum = new[] { 0f };
                _pathLength = 0f;
                return;
            }
            var path = new NavMeshPath();
            bool ok = (from - to).sqrMagnitude > 36f &&
                      NavMesh.SamplePosition(from, out NavMeshHit a, 2f, NavMesh.AllAreas) &&
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
                return Vector3.Lerp(_corners[i - 1], _corners[i], t);
            }
            return _corners[_corners.Length - 1];
        }
    }

    /// <summary>Alle Tiere mit Tagesablauf (Tests, spaetere Systeme).</summary>
    public static class AnimalRegistry
    {
        private static readonly System.Collections.Generic.List<AnimalRoutine> List = new System.Collections.Generic.List<AnimalRoutine>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => List.Clear();

        public static System.Collections.Generic.IReadOnlyList<AnimalRoutine> All => List;
        internal static void Add(AnimalRoutine r) { if (!List.Contains(r)) List.Add(r); }
        internal static void Remove(AnimalRoutine r) => List.Remove(r);

        public static AnimalRoutine Find(string name)
        {
            foreach (AnimalRoutine r in List) if (r != null && (r.name == name || r.name.StartsWith(name + " "))) return r;
            return null;
        }
    }
}
