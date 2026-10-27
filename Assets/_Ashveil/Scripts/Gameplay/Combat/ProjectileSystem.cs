using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Audio;
using UnityEngine;

namespace Ashveil.Gameplay.Combat
{
    /// <summary>Aussehen eines Geschosses.</summary>
    public enum ProjectileLook : byte
    {
        Arrow = 0,
        Bolt = 1,
        /// <summary>Magische Kugel (Stab)</summary>
        Arcane = 2
    }

    /// <summary>Ein abgeschossenes Geschoss.</summary>
    public struct ProjectileLaunch
    {
        /// <summary>Angreifer, Waffe, Schritt, Zusatzschaden – wie bei Nahkampf/Schuss.</summary>
        public AttackRequest Request;
        public Vector3 Origin;
        public Vector3 Direction;
        /// <summary>Gezielter Schuss wie in WoW: fliegt sicher zum Ziel, der Treffer wird beim Eintreffen gewuerfelt.</summary>
        public CombatTarget Target;
        /// <summary>Schaden anwenden (Server bzw. Einzelspiel).</summary>
        public bool Resolve;
        /// <summary>Sichtbar darstellen (Clients).</summary>
        public bool Visual;
        public ProjectileLook Look;
        public Color Color;
    }

    /// <summary>
    /// Fliegende Geschosse: Pfeile, Bolzen, Zauberkugeln.
    ///
    ///  - MIT Ziel (WoW-Prinzip): Das Geschoss folgt dem Ziel und kommt sicher an; ob es trifft (Stufenunterschied,
    ///    Kritisch), entscheidet der Wurf beim Eintreffen. Pfeile fliegen dabei einen leichten Bogen.
    ///  - OHNE Ziel: echte Flugbahn mit Schwerkraft. Was zuerst im Weg ist, wird getroffen.
    ///  - Pfeile bleiben kurz stecken (im Ziel oder in der Welt).
    /// Der Server fliegt unsichtbar mit und rechnet den Schaden, Clients zeigen nur das Bild.
    /// </summary>
    public sealed class ProjectileSystem : MonoBehaviour
    {
        private const float MaxLife = 6f;
        private const float StuckLife = 7f;
        private const int MaxStuck = 60;

        private sealed class Flight
        {
            public ProjectileLaunch L;
            public Vector3 Start, Pos, Vel;
            public float Speed, Gravity, Traveled, Life, Arc;
            public Collider TargetCollider;
            public Transform Visual;
            public TrailRenderer Trail;
        }

        private static ProjectileSystem _instance;
        private readonly List<Flight> _flights = new List<Flight>();
        private readonly Queue<(Transform t, float until)> _stuck = new Queue<(Transform, float)>();
        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        private static Mesh _arrowMesh, _orbMesh;
        private static Material[] _arrowMats;
        private static readonly Dictionary<Color, Material> GlowMats = new Dictionary<Color, Material>();

        /// <summary>Tests/Diagnose: gerade fliegende Geschosse.</summary>
        public static int InFlight => _instance != null ? _instance._flights.Count : 0;
        /// <summary>Tests: angekommene Geschosse mit Treffer seit Start.</summary>
        public static int Landed { get; private set; }

        /// <summary>Geschoss starten.</summary>
        public static void Launch(in ProjectileLaunch launch)
        {
            if (!launch.Resolve && !launch.Visual) return;
            if (_instance == null)
            {
                var go = new GameObject("[Geschosse]");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<ProjectileSystem>();
            }
            _instance.Add(launch);
        }

        /// <summary>Flugzeit (s) bis zum Ziel – fuer Anzeige und Tests.</summary>
        public static float FlightTime(WeaponDefinition w, float distance) =>
            w == null ? 0f : distance / Mathf.Max(1f, w.projectileSpeed);

        private void Add(in ProjectileLaunch launch)
        {
            WeaponDefinition w = launch.Request.Weapon;
            var f = new Flight
            {
                L = launch,
                Start = launch.Origin,
                Pos = launch.Origin,
                Speed = w != null ? Mathf.Max(1f, w.projectileSpeed) : 40f,
                Gravity = w != null && launch.Look != ProjectileLook.Arcane ? w.projectileGravity : 0f
            };
            Vector3 dir = launch.Direction.sqrMagnitude > 1e-6f ? launch.Direction.normalized : Vector3.forward;
            f.Vel = dir * f.Speed;
            if (launch.Target != null)
            {
                f.TargetCollider = launch.Target.GetComponentInChildren<Collider>();
                float dist = Vector3.Distance(launch.Origin, launch.Target.AimPoint);
                // Pfeile fliegen einen sichtbaren Bogen (hoeher bei weiten Schuessen), Zauber gerade
                f.Arc = launch.Look == ProjectileLook.Arcane ? 0f : Mathf.Clamp(dist * 0.045f, 0f, 2.2f);
            }
            if (launch.Visual) f.Visual = CreateVisual(launch.Look, launch.Color, out f.Trail);
            Place(f, f.Pos, dir);
            _flights.Add(f);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = _flights.Count - 1; i >= 0; i--)
            {
                Flight f = _flights[i];
                f.Life += dt;
                bool done = f.L.Target != null ? StepTargeted(f, dt) : StepFree(f, dt);
                if (done || f.Life > MaxLife)
                {
                    if (!done) Discard(f);
                    _flights.RemoveAt(i);
                }
            }
            float now = Time.time;
            while (_stuck.Count > 0 && (_stuck.Peek().t == null || _stuck.Peek().until <= now || _stuck.Count > MaxStuck))
            {
                var s = _stuck.Dequeue();
                if (s.t != null) Destroy(s.t.gameObject);
            }
        }

        // ---------------------------------------------------------------- gezielt (WoW)

        private bool StepTargeted(Flight f, float dt)
        {
            CombatTarget t = f.L.Target;
            if (t == null || !t.isActiveAndEnabled)
            {
                // Ziel verschwunden: frei weiterfliegen
                f.L.Target = null;
                return StepFree(f, dt);
            }
            Vector3 goal = t.AimPoint;
            // Fortschritt entlang der (mitwandernden) Geraden, Bogen darueber
            Vector3 chord = goal - f.Start;
            float total = Mathf.Max(0.01f, chord.magnitude);
            f.Traveled += f.Speed * dt;
            float u = Mathf.Clamp01(f.Traveled / total);
            Vector3 p = f.Start + chord * u + Vector3.up * (4f * f.Arc * u * (1f - u));
            Vector3 dir = p - f.Pos;
            if (dir.sqrMagnitude < 1e-8f) dir = chord;
            f.Pos = p;
            Place(f, p, dir);
            if (u < 1f) return false;

            Vector3 flat = chord; flat.y = 0f;
            if (f.L.Resolve && f.TargetCollider != null)
            {
                CombatResolver.ResolveProjectileHit(f.L.Request, f.TargetCollider, goal, flat.sqrMagnitude > 1e-4f ? flat.normalized : chord.normalized);
                Landed++;
            }
            Impact(f, goal, dir.normalized, t.transform);
            return true;
        }

        // ---------------------------------------------------------------- freie Flugbahn

        private bool StepFree(Flight f, float dt)
        {
            Vector3 from = f.Pos;
            f.Vel += Vector3.down * f.Gravity * dt;
            Vector3 to = from + f.Vel * dt;
            Vector3 seg = to - from;
            float len = seg.magnitude;
            f.Traveled += len;
            float maxRange = f.L.Request.Weapon != null ? f.L.Request.Weapon.range * 1.6f : 80f;
            if (len > 1e-5f && Cast(f, from, seg / len, len, out RaycastHit hit))
            {
                f.Pos = hit.point;
                Place(f, hit.point, seg);
                if (f.L.Resolve)
                {
                    Vector3 flat = seg; flat.y = 0f;
                    if (CombatResolver.ResolveProjectileHit(f.L.Request, hit.collider, hit.point, flat.sqrMagnitude > 1e-4f ? flat : seg)) Landed++;
                }
                Impact(f, hit.point, seg / len, hit.collider.transform);
                return true;
            }
            f.Pos = to;
            Place(f, to, f.Vel);
            if (f.Traveled > maxRange) { Discard(f); return true; }
            return false;
        }

        private static bool Cast(Flight f, Vector3 origin, Vector3 dir, float dist, out RaycastHit best)
        {
            best = default;
            int mask = CombatResolver.BlockMask | CombatResolver.TargetMask;
            int n = Physics.RaycastNonAlloc(origin, dir, Hits, dist, mask, QueryTriggerInteraction.Ignore);
            float bestD = float.MaxValue;
            bool found = false;
            GameObject attacker = f.L.Request.Attacker;
            for (int i = 0; i < n; i++)
            {
                if (attacker != null && Hits[i].collider.transform.IsChildOf(attacker.transform)) continue;
                if (Hits[i].distance < bestD) { bestD = Hits[i].distance; best = Hits[i]; found = true; }
            }
            return found;
        }

        // ---------------------------------------------------------------- Bild

        private void Impact(Flight f, Vector3 point, Vector3 dir, Transform into)
        {
            if (f.Visual == null) return;
            bool arcane = f.L.Look == ProjectileLook.Arcane;
            SfxPlayer.Play(arcane ? SfxId.SpellHit : SfxId.ArrowHit, point, arcane ? 0.8f : 0.6f);
            if (arcane || into == null)
            {
                if (f.Trail != null) f.Trail.emitting = false;
                Destroy(f.Visual.gameObject, arcane ? 0.35f : 0.05f);
                if (arcane) f.Visual.localScale *= 2.2f; // kurzes Aufbluehen
                return;
            }
            // Pfeil bleibt stecken: etwas eindringen, dem Getroffenen folgen
            if (f.Trail != null) f.Trail.emitting = false;
            f.Visual.position = point + dir * 0.12f;
            f.Visual.rotation = Quaternion.LookRotation(dir);
            f.Visual.SetParent(into, true);
            _stuck.Enqueue((f.Visual, Time.time + StuckLife));
        }

        private static void Discard(Flight f)
        {
            if (f.Visual != null) Destroy(f.Visual.gameObject);
        }

        private static void Place(Flight f, Vector3 pos, Vector3 dir)
        {
            if (f.Visual == null) return;
            f.Visual.position = pos;
            if (dir.sqrMagnitude > 1e-8f) f.Visual.rotation = Quaternion.LookRotation(dir);
        }

        private Transform CreateVisual(ProjectileLook look, Color color, out TrailRenderer trail)
        {
            var go = new GameObject(look == ProjectileLook.Arcane ? "Zauber" : "Pfeil");
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail = go.AddComponent<TrailRenderer>();
            trail.minVertexDistance = 0.1f;
            trail.numCapVertices = 2;
            Shader spr = Shader.Find("Sprites/Default");
            if (spr != null) trail.sharedMaterial = new Material(spr);
            if (look == ProjectileLook.Arcane)
            {
                mf.sharedMesh = OrbMesh();
                mr.sharedMaterial = Glow(color);
                go.transform.localScale = Vector3.one * 0.28f;
                trail.time = 0.35f;
                trail.widthMultiplier = 0.22f;
                trail.startColor = color;
                trail.endColor = new Color(color.r, color.g, color.b, 0f);
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = color;
                light.range = 4f;
                light.intensity = 2.2f;
                light.shadows = LightShadows.None;
            }
            else
            {
                mf.sharedMesh = ArrowMesh(look == ProjectileLook.Bolt ? 0.45f : 0.78f);
                mr.sharedMaterials = ArrowMaterials();
                trail.time = 0.12f;
                trail.widthMultiplier = 0.025f;
                trail.startColor = new Color(1f, 1f, 1f, 0.35f);
                trail.endColor = new Color(1f, 1f, 1f, 0f);
            }
            return go.transform;
        }

        public static Material[] ArrowMaterials()
        {
            if (_arrowMats != null && _arrowMats[0] != null) return _arrowMats;
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            Material Make(string n, Color col, float smooth, float metal)
            {
                var m = new Material(sh) { name = n, enableInstancing = true };
                m.color = col;
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
                return m;
            }
            _arrowMats = new[]
            {
                Make("Pfeil_Schaft", new Color(0.46f, 0.33f, 0.2f), 0.2f, 0f),
                Make("Pfeil_Spitze", new Color(0.34f, 0.35f, 0.37f), 0.55f, 0.85f),
                Make("Pfeil_Feder", new Color(0.8f, 0.76f, 0.68f), 0.1f, 0f)
            };
            return _arrowMats;
        }

        private static Material Glow(Color c)
        {
            if (GlowMats.TryGetValue(c, out Material m) && m != null) return m;
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            m = new Material(sh) { name = "Zauberglut" };
            m.color = c * 2.5f;
            GlowMats[c] = m;
            return m;
        }

        private static Mesh OrbMesh()
        {
            if (_orbMesh != null) return _orbMesh;
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _orbMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            return _orbMesh;
        }

        /// <summary>Pfeil als ein Netz: Schaft, Spitze, drei Federn (je ein Teilnetz). +Z = Flugrichtung, Spitze vorn.</summary>
        public static Mesh ArrowMesh(float length)
        {
            if (_arrowMesh != null && Mathf.Abs(_arrowMesh.bounds.size.z - length) < 0.01f) return _arrowMesh;
            var v = new List<Vector3>();
            var tw = new List<int>();
            var ti = new List<int>();
            var tf = new List<int>();
            float tail = -length * 0.5f, head = length * 0.5f, r = 0.006f;
            Box(v, tw, new Vector3(-r, -r, tail), new Vector3(r, r, head - 0.06f));
            int b = v.Count;
            float hr = 0.016f, hb = head - 0.07f;
            v.Add(new Vector3(-hr, 0, hb)); v.Add(new Vector3(0, hr, hb)); v.Add(new Vector3(hr, 0, hb)); v.Add(new Vector3(0, -hr, hb)); v.Add(new Vector3(0, 0, head));
            ti.AddRange(new[] { b, b + 1, b + 4, b + 1, b + 2, b + 4, b + 2, b + 3, b + 4, b + 3, b, b + 4, b, b + 2, b + 1, b, b + 3, b + 2 });
            for (int k = 0; k < 3; k++)
            {
                Quaternion q = Quaternion.AngleAxis(k * 120f, Vector3.forward);
                int s0 = v.Count;
                v.Add(q * new Vector3(0, r, tail + 0.02f));
                v.Add(q * new Vector3(0, r, tail + 0.14f));
                v.Add(q * new Vector3(0, 0.028f, tail + 0.04f));
                v.Add(q * new Vector3(0, 0.024f, tail + 0.01f));
                tf.AddRange(new[] { s0, s0 + 1, s0 + 2, s0, s0 + 2, s0 + 3, s0, s0 + 2, s0 + 1, s0, s0 + 3, s0 + 2 });
            }
            _arrowMesh = new Mesh { name = "Pfeil", subMeshCount = 3 };
            _arrowMesh.SetVertices(v);
            _arrowMesh.SetTriangles(tw, 0);
            _arrowMesh.SetTriangles(ti, 1);
            _arrowMesh.SetTriangles(tf, 2);
            _arrowMesh.RecalculateNormals();
            _arrowMesh.RecalculateBounds();
            return _arrowMesh;
        }

        private static void Box(List<Vector3> v, List<int> t, Vector3 min, Vector3 max)
        {
            int b = v.Count;
            for (int i = 0; i < 8; i++)
                v.Add(new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z));
            int[] q = { 0, 2, 3, 1, 4, 5, 7, 6, 0, 1, 5, 4, 2, 6, 7, 3, 0, 4, 6, 2, 1, 3, 7, 5 };
            for (int f = 0; f < 6; f++)
            {
                int a0 = b + q[f * 4], a1 = b + q[f * 4 + 1], a2 = b + q[f * 4 + 2], a3 = b + q[f * 4 + 3];
                t.AddRange(new[] { a0, a1, a2, a0, a2, a3 });
            }
        }
    }
}
