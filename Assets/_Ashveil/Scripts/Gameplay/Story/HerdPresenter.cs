using Ashveil.Core;
using Ashveil.Data;
using UnityEngine;

namespace Ashveil.Gameplay.Story
{
    /// <summary>Was die Herde gerade tut (Vorgabe: Idle/Panicked/Huddle/FleeHome/Graze – dazu Freilassen im Weidegang).</summary>
    public enum HerdMode : byte
    {
        /// <summary>Auf der Weide verteilt, grasen und stehen.</summary>
        Graze = 0,
        /// <summary>In der Senke eng zusammengedraengt an der Rueckwand.</summary>
        Huddle = 1,
        /// <summary>Panik: unruhig auf der Stelle, Koepfe hoch (Raubtier nah).</summary>
        Panicked = 2,
        /// <summary>Heimweg entlang der kuratierten Route.</summary>
        FleeHome = 3,
        /// <summary>Weidegang: Tor offen – die Herde laeuft hinaus, das Story-Pferd haelt, dreht um und kommt zurueck.</summary>
        Released = 4
    }

    /// <summary>
    /// Darstellung einer Herde (ohne Netz, ohne Physik): Pferde aus <see cref="HorseRig"/>, Positionen rein aus Zustand, Route
    /// und festen Plaetzen je Tier berechnet – deshalb sehen alle Beteiligten dasselbe, ohne acht Netz-Transforms.
    ///   Seitlicher Versatz, Abstand in der Reihe, Tempo-Streuung und Startverzoegerung je Tier (Vorgabe Herd-AI).
    ///   Das Fohlen bleibt immer bei der Stute (Tier 0). Weit entfernt ruht alles (keine Rechenzeit, keine Darstellung).
    /// Genutzt vom vernetzten Begegnungs-Objekt (Senke/Heimweg) und von der lokalen Hofherde.
    /// </summary>
    public sealed class HerdPresenter : MonoBehaviour
    {
        public const int MareIndex = 0;

        private struct Slot
        {
            public float Along, Lateral, SpeedJitter, Phase;
            public Vector2 Ring;
        }

        private HorseRig[] _rigs = new HorseRig[0];
        private Slot[] _slots = new Slot[0];
        private Vector3[] _pos = new Vector3[0];
        private float[] _yaw = new float[0];
        private int _foalIndex = -1;
        private bool _built;
        private float _shownProgress;
        private float _releaseTime;

        // ---- Eingaben (vom Besitzer der Darstellung gesetzt)
        public HerdMode Mode { get; set; }
        public float RouteProgress { get; set; }
        /// <summary>Wo die Herde steht, wenn sie nicht laeuft (Senke, Weide) – und ihr Radius.</summary>
        public Vector3 Center { get; set; }
        public float Radius { get; set; } = 18f;
        /// <summary>Blickrichtung „nach hinten“ in der Senke (Rueckwand), Tor-Richtung beim Freilassen.</summary>
        public Vector3 Facing { get; set; } = Vector3.forward;
        public HerdRoute Route { get; set; }
        /// <summary>Fohlen haengt im Halfter fest (Senke) – an diesem Punkt.</summary>
        public bool FoalStuck { get; set; }
        public Vector3 FoalStuckPoint { get; set; }
        /// <summary>Fohlen (oder erwachsenes Story-Pferd) geht zu diesem Ziel und schnuppert (Abschlussszene).</summary>
        public Transform SniffTarget { get; set; }
        /// <summary>Story-Pferd folgt nicht der Herde, sondern steht bei diesem Punkt (Weidegang: kommt zurueck).</summary>
        public bool StoryHorseAway { get; set; }
        public Vector3 StoryHorsePoint { get; set; }
        public Transform LookAt { get; set; }
        /// <summary>Story-Pferd entspannt (nach dem Hand-Hinhalten).</summary>
        public bool StoryHorseRelaxed { get; set; }
        public bool Visible { get; private set; }
        public int Count => _rigs.Length;
        public int FoalIndex => _foalIndex;

        /// <summary>Herde anlegen: <paramref name="adults"/> Tiere (Tier 0 = Stute) plus Story-Pferd (Fohlen oder erwachsen).</summary>
        public void Build(int adults, bool withStoryHorse, bool storyHorseIsFoal, int seed)
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
            int n = adults + (withStoryHorse ? 1 : 0);
            _rigs = new HorseRig[n];
            _slots = new Slot[n];
            _pos = new Vector3[n];
            _yaw = new float[n];
            var rng = new System.Random(seed);
            for (int i = 0; i < adults; i++)
            {
                HorseLook look = HorseLook.Herd(seed + i * 3);
                if (i == MareIndex) look.Coat = HorseStoryCatalog.CoatColor(HorseCoatPreset.DarkBay); // Stute: dunkelbraun (Mutter)
                _rigs[i] = HorseRig.Create(transform, i == MareIndex ? "Stute" : "Pferd " + i, look);
                _rigs[i].AutoAnimate = false;
            }
            _foalIndex = -1;
            if (withStoryHorse)
            {
                _foalIndex = adults;
                _rigs[_foalIndex] = HorseRig.Create(transform, storyHorseIsFoal ? "Fohlen" : "Das Pferd", HorseLook.FromIdentity(HorseStoryCatalog.Warhorse, storyHorseIsFoal, false));
                _rigs[_foalIndex].AutoAnimate = false;
            }
            for (int i = 0; i < n; i++)
            {
                float a = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                _slots[i] = new Slot
                {
                    Along = i == 0 ? 2f : 2f + i * 3.2f + (float)rng.NextDouble() * 2.5f,
                    Lateral = ((float)rng.NextDouble() - 0.5f) * 4.6f,
                    SpeedJitter = 0.85f + (float)rng.NextDouble() * 0.3f,
                    Phase = (float)rng.NextDouble() * 10f,
                    Ring = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (0.35f + 0.65f * (float)rng.NextDouble())
                };
                _pos[i] = transform.position;
            }
            _built = true;
            SnapNow();
        }

        public HorseRig Rig(int i) => i >= 0 && i < _rigs.Length ? _rigs[i] : null;

        /// <summary>Position eines Tiers (ohne weiches Nachziehen) – der Server nutzt dieselbe Rechnung fuer Sprungziele.</summary>
        public Vector3 TargetPosition(int i, out float yaw, out float speed, out HorsePose pose)
        {
            Vector3 p0 = TargetPositionRaw(i, out yaw, out speed, out pose);
            if (i == _foalIndex && StoryHorseRelaxed && SniffTarget == null && speed < 0.3f) pose = HorsePose.Relaxed;
            return p0;
        }

        private Vector3 TargetPositionRaw(int i, out float yaw, out float speed, out HorsePose pose)
        {
            yaw = 0f;
            speed = 0f;
            pose = HorsePose.Stand;
            if (i < 0 || i >= _slots.Length) return Center;
            Slot sl = _slots[i];
            float t = Time.time + sl.Phase;

            if (i == _foalIndex)
            {
                if (SniffTarget != null)
                {
                    Vector3 to = SniffTarget.position - _pos[i];
                    to.y = 0f;
                    Vector3 stand = SniffTarget.position - (to.sqrMagnitude > 1e-3f ? to.normalized : Vector3.forward) * 1.35f;
                    yaw = Yaw(to);
                    float d = Vector3.Distance(Flat(_pos[i]), Flat(stand));
                    speed = d > 0.2f ? Mathf.Min(1.2f, d) : 0f;
                    pose = d > 0.6f ? HorsePose.Stand : HorsePose.Sniff;
                    return stand;
                }
                if (FoalStuck)
                {
                    pose = HorsePose.Stuck;
                    yaw = Yaw(Facing);
                    return FoalStuckPoint;
                }
                if (StoryHorseAway)
                {
                    pose = HorsePose.Alert;
                    Vector3 look = LookAt != null ? LookAt.position - StoryHorsePoint : -Facing;
                    yaw = Yaw(look);
                    return StoryHorsePoint;
                }
                // bei der Stute (Fohlen) bzw. in der Herde (erwachsen)
                Vector3 mare = _pos[MareIndex];
                float myaw = _yaw[MareIndex];
                Quaternion r = Quaternion.Euler(0f, myaw, 0f);
                Vector3 p = mare + r * new Vector3(1.5f, 0f, -1.1f);
                yaw = myaw;
                speed = _rigs[MareIndex] != null ? _rigs[MareIndex].Speed : 0f;
                pose = Mode == HerdMode.Graze && speed < 0.3f ? HorsePose.Stand : Mode == HerdMode.Panicked ? HorsePose.Panic : HorsePose.Stand;
                return p;
            }

            switch (Mode)
            {
                case HerdMode.FleeHome:
                    if (Route != null)
                    {
                        float s = _shownProgress - sl.Along * sl.SpeedJitter + Mathf.Sin(_shownProgress * 0.05f + sl.Phase) * 1.2f;
                        Vector3 p = Route.PointAt(Mathf.Max(0f, s), out Vector3 dir);
                        Vector3 right = Vector3.Cross(Vector3.up, dir);
                        p += right * sl.Lateral;
                        yaw = Yaw(dir);
                        speed = RouteSpeed;
                        pose = HorsePose.Stand;
                        if (s <= 0f && _shownProgress < 1f) speed = 0f;
                        if (_shownProgress >= Route.Length - 0.5f) { speed = 0f; pose = HorsePose.Graze; }
                        return p;
                    }
                    goto default;
                case HerdMode.Huddle:
                case HerdMode.Panicked:
                {
                    // eng an der Rueckwand, Koepfe zur Mitte/zum Eingang
                    Vector3 back = Facing.sqrMagnitude > 1e-3f ? Facing.normalized : Vector3.forward;
                    Vector3 side = Vector3.Cross(Vector3.up, back);
                    Vector3 c = Center + back * (Radius * 0.45f);
                    Vector3 p = c + side * (sl.Ring.x * Radius * 0.55f) + back * (sl.Ring.y * Radius * 0.2f);
                    if (Mode == HerdMode.Panicked) p += new Vector3(Mathf.Sin(t * 1.7f), 0f, Mathf.Cos(t * 1.3f)) * 0.7f;
                    yaw = Yaw(-back) + Mathf.Sin(t * 0.7f) * 25f;
                    speed = Mode == HerdMode.Panicked ? 0.6f + Mathf.Abs(Mathf.Sin(t * 2.1f)) * 0.8f : 0f;
                    pose = Mode == HerdMode.Panicked ? HorsePose.Panic : HorsePose.Alert;
                    return p;
                }
                case HerdMode.Released:
                {
                    // hinaus durchs Tor, 30–50 m, dann verteilt grasen
                    float since = Time.time - _releaseTime;
                    Vector3 dir = Facing.sqrMagnitude > 1e-3f ? Facing.normalized : Vector3.forward;
                    Vector3 side = Vector3.Cross(Vector3.up, dir);
                    float run = Mathf.Min(since * 7.5f * sl.SpeedJitter, 34f + sl.Along * 0.8f);
                    Vector3 p = Center + dir * run + side * (sl.Lateral * (1f + run * 0.12f));
                    yaw = Yaw(dir + side * sl.Ring.x * 0.3f);
                    bool running = run < 34f + sl.Along * 0.8f - 0.1f;
                    speed = running ? 7.5f * sl.SpeedJitter : 0f;
                    pose = running ? HorsePose.Stand : HorsePose.Graze;
                    return p;
                }
                default:
                {
                    // Weide: jedes Tier hat einen Platz, wechselt ihn alle ~40 s ein paar Meter weit
                    long slot = (long)((Time.time + sl.Phase * 7f) / 40f);
                    float h = Hash(i, slot);
                    Vector2 ring = sl.Ring + new Vector2(Mathf.Cos(h * 6.28f), Mathf.Sin(h * 6.28f)) * 0.15f;
                    Vector3 p = Center + new Vector3(ring.x, 0f, ring.y) * Radius;
                    Vector3 to = p - _pos[i];
                    to.y = 0f;
                    float d = to.magnitude;
                    speed = d > 0.3f ? 1.1f : 0f;
                    yaw = d > 0.3f ? Yaw(to) : _yaw[i];
                    pose = d > 0.3f ? HorsePose.Stand : (Hash(i, slot * 3 + 1) > 0.3f ? HorsePose.Graze : HorsePose.Stand);
                    if (LookAt != null && pose == HorsePose.Stand && Vector3.Distance(LookAt.position, p) < 12f) pose = HorsePose.Alert;
                    return p;
                }
            }
        }

        /// <summary>Tempo auf dem Heimweg (m/s) – wie der Server es fortschreibt.</summary>
        public float RouteSpeed { get; set; } = 3.1f;

        public void StartRelease()
        {
            _releaseTime = Time.time;
        }

        private static float Hash(int i, long slot)
        {
            unchecked
            {
                int h = i * 73856093 ^ (int)slot * 19349663;
                h ^= h >> 13;
                h *= 1274126177;
                h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            }
        }

        private static float Yaw(Vector3 d) => d.sqrMagnitude > 1e-5f ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg : 0f;
        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        public void SnapNow()
        {
            _shownProgress = RouteProgress;
            for (int i = 0; i < _rigs.Length; i++)
            {
                _pos[i] = Ground(TargetPosition(i, out float yaw, out _, out _));
                _yaw[i] = yaw;
            }
            // Fohlen haengt an der Stute: zweiter Durchlauf, damit es neben ihr steht
            if (_foalIndex >= 0) _pos[_foalIndex] = Ground(TargetPosition(_foalIndex, out _yaw[_foalIndex], out _, out _));
            Apply(0f);
        }

        private static Vector3 Ground(Vector3 p)
        {
            if (WorldGround.TrySample(p, out float h)) p.y = h;
            return p;
        }

        private void LateUpdate()
        {
            if (!_built) return;
            // Weit weg: ruhen (keine Rechenzeit, nicht sichtbar)
            Camera cam = Camera.main;
            Vector3 focus = Mode == HerdMode.FleeHome && Route != null ? Route.PointAt(_shownProgress, out _) : Center;
            bool near = cam == null || (cam.transform.position - focus).sqrMagnitude < 250f * 250f;
            if (near != Visible)
            {
                Visible = near;
                foreach (HorseRig r in _rigs) if (r != null) r.SetVisible(near);
                if (near) SnapNow();
            }
            if (!near) return;
            float dt = Time.deltaTime;
            // Fortschritt weich nachziehen (Server schickt ihn ein paar Mal pro Sekunde)
            float gap = RouteProgress - _shownProgress;
            if (Mathf.Abs(gap) > 25f) _shownProgress = RouteProgress;
            else _shownProgress += Mathf.Clamp(gap, -RouteSpeed * 2f * dt, RouteSpeed * 1.6f * dt + Mathf.Max(0f, gap) * dt);
            Apply(dt);
        }

        private void Apply(float dt)
        {
            float k = dt <= 0f ? 1f : 1f - Mathf.Exp(-6f * dt);
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < _rigs.Length; i++)
                {
                    bool foal = i == _foalIndex;
                    if (foal != (pass == 1)) continue; // Fohlen nach der Stute
                    Vector3 target = Ground(TargetPosition(i, out float yaw, out float speed, out HorsePose pose));
                    // einfaches Ausweichen: nicht in ein anderes Tier laufen
                    for (int j = 0; j < i; j++)
                    {
                        Vector3 d = target - _pos[j];
                        d.y = 0f;
                        float min = (_rigs[i].WithersHeight + _rigs[j].WithersHeight) * 0.48f;
                        if (d.sqrMagnitude < min * min && d.sqrMagnitude > 1e-4f) target += d.normalized * (min - d.magnitude) * 0.5f;
                    }
                    Vector3 move = target - _pos[i];
                    float maxStep = Mathf.Max(speed, 1.5f) * 1.8f * Mathf.Max(dt, 0.016f);
                    if (dt <= 0f || move.magnitude > 30f) _pos[i] = target;
                    else _pos[i] = Vector3.MoveTowards(_pos[i], Vector3.Lerp(_pos[i], target, k * 3f), maxStep);
                    _yaw[i] = dt <= 0f ? yaw : Mathf.LerpAngle(_yaw[i], yaw, k);
                    HorseRig rig = _rigs[i];
                    rig.transform.SetPositionAndRotation(_pos[i], Quaternion.Euler(0f, _yaw[i], 0f));
                    rig.Speed = speed;
                    rig.Pose = pose;
                    if (LookAt != null && (pose == HorsePose.Alert || pose == HorsePose.Stand))
                    {
                        Vector3 l = LookAt.position - _pos[i];
                        rig.HeadYaw = l.sqrMagnitude < 30f * 30f ? Mathf.DeltaAngle(_yaw[i], Yaw(l)) : 0f;
                    }
                    else rig.HeadYaw = 0f;
                    if (dt > 0f) rig.Animate(dt);
                }
        }

        /// <summary>Position eines Tiers wie gerade dargestellt.</summary>
        public Vector3 Shown(int i) => i >= 0 && i < _pos.Length ? _pos[i] : Center;
    }
}
