using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Audio;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Motion.Procedural;
using Ashveil.Gameplay.Weapons;
using UnityEngine;

namespace Ashveil.Gameplay.Abilities
{
    /// <summary>
    /// Bild und Klang der Klassenfaehigkeiten (P1) auf allen Clients – schlicht und gut lesbar (Kampf-Lesbarkeit vor
    /// Effekt-Gewitter): Glut in der Hand beim Wirken, Geschosse, Ringe fuer Flaechen, Licht-Saeule fuer Heilung,
    /// Schildblase, Sterne ueber betaeubten Zielen. Nur Darstellung, keine Spiellogik.
    /// </summary>
    public sealed class SpellFx : MonoBehaviour
    {
        private sealed class Fx
        {
            public Transform T;
            public Renderer R;
            public float Start, Life;
            public Color Color;
            public Vector3 FromScale, ToScale;
            public Transform Follow;
            public Vector3 Offset;
            public bool Spin;
        }

        private static SpellFx _instance;
        private readonly List<Fx> _fx = new List<Fx>();
        private static Material _mat;
        private static Mesh _sphere, _ring, _column;
        private static WeaponDefinition _boltCarrier;
        private readonly Dictionary<GameObject, Fx> _castGlow = new Dictionary<GameObject, Fx>();

        public static Color SchoolColor(SpellSchool s)
        {
            switch (s)
            {
                case SpellSchool.Fire: return new Color(1f, 0.52f, 0.16f);
                case SpellSchool.Ash: return new Color(1f, 0.86f, 0.52f);
                case SpellSchool.Nature: return new Color(0.45f, 0.85f, 0.35f);
                default: return new Color(0.9f, 0.88f, 0.8f);
            }
        }

        private static SpellFx Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("[Zauber-Effekte]");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<SpellFx>();
                return _instance;
            }
        }

        // ================================================================== Wirken

        public static void CastBegin(GameObject caster, SpellDefinition s, float seconds)
        {
            if (caster == null || s == null) return;
            SpellFx fx = Instance;
            fx.EndGlow(caster);
            Transform hand = Hand(caster);
            Color c = SchoolColor(s.school);
            Fx g = fx.Spawn(Sphere(), hand != null ? hand.position : caster.transform.position + Vector3.up * 1.3f, c, seconds + 0.4f,
                Vector3.one * 0.05f, Vector3.one * 0.26f);
            g.Follow = hand != null ? hand : caster.transform;
            g.Offset = hand != null ? Vector3.zero : Vector3.up * 1.3f;
            fx._castGlow[caster] = g;
            SfxPlayer.Play(SfxId.CastCharge, caster.transform.position + Vector3.up, 0.55f, 0.05f);
            Choreo(caster)?.PlayAbility(false, 0, Mathf.Max(0.2f, seconds), 0.12f, 0.35f);
        }

        public static void CastEnd(GameObject caster, bool interrupted)
        {
            if (caster == null || _instance == null) return;
            _instance.EndGlow(caster);
        }

        private void EndGlow(GameObject caster)
        {
            if (!_castGlow.TryGetValue(caster, out Fx g)) return;
            _castGlow.Remove(caster);
            if (g.T != null) g.Life = Mathf.Min(g.Life, Time.time - g.Start + 0.05f);
        }

        // ================================================================== Wirkung

        public static void Cast(GameObject caster, SpellDefinition s, GameObject target, Vector3 point, float value)
        {
            if (caster == null || s == null) return;
            SpellFx fx = Instance;
            fx.EndGlow(caster);
            Color c = SchoolColor(s.school);
            Vector3 casterPos = caster.transform.position;
            CombatTarget ct = target != null ? target.GetComponentInParent<CombatTarget>() : null;
            Vector3 tpos = ct != null ? ct.AimPoint : (target != null ? target.transform.position + Vector3.up * 1.2f : point + Vector3.up * 1.2f);
            CombatChoreographer choreo = Choreo(caster);

            switch (s.effect)
            {
                case SpellEffectKind.WeaponStrike:
                    choreo?.PlayAbility(true, 0, 0.12f, 0.1f, 0.3f);
                    SfxPlayer.Play(SfxId.ShieldBlock, tpos, 0.5f, 0.1f);
                    break;
                case SpellEffectKind.NextSwing:
                    fx.Spawn(Sphere(), (Hand(caster) != null ? Hand(caster).position : casterPos + Vector3.up * 1.3f), new Color(1f, 0.35f, 0.2f), 0.35f,
                        Vector3.one * 0.2f, Vector3.one * 0.6f);
                    break;
                case SpellEffectKind.RangedShot:
                {
                    var wu = caster.GetComponent<WeaponUser>();
                    WeaponDefinition w = wu != null ? wu.RangedWeapon : null;
                    if (wu != null && wu.ActiveWeapon == w) choreo?.PlayAbility(true, 0, 0.2f, 0.06f, 0.3f);
                    Vector3 from = casterPos + Vector3.up * 1.45f + caster.transform.forward * 0.5f;
                    ProjectileSystem.Launch(new ProjectileLaunch
                    {
                        Request = new AttackRequest { Attacker = caster, Weapon = w, PredictOnly = true },
                        Origin = from,
                        Direction = (tpos - from).normalized,
                        Target = ct,
                        Resolve = false,
                        Visual = true,
                        Look = ProjectileLook.Arrow,
                        Color = c
                    });
                    SfxPlayer.Play(SfxId.BowRelease, from, 0.7f, 0.05f);
                    if (s.school == SpellSchool.Fire && ct != null)
                        fx.Spawn(Sphere(), tpos, c, 0.9f, Vector3.one * 0.3f, Vector3.one * 0.9f, ct.transform, tpos - ct.transform.position);
                    break;
                }
                case SpellEffectKind.SpellDamage:
                    if (s.target == SpellTarget.AreaAroundSelf)
                    {
                        choreo?.PlayAbility(true, 3, 0.1f, 0.12f, 0.4f);
                        fx.Ring(casterPos + Vector3.up * 0.15f, s.radius, c, 0.7f);
                        SfxPlayer.Play(SfxId.SpellHit, casterPos + Vector3.up, 0.9f, 0.05f);
                    }
                    else
                    {
                        Transform hand = Hand(caster);
                        Vector3 from = hand != null ? hand.position : casterPos + Vector3.up * 1.4f;
                        ProjectileSystem.Launch(new ProjectileLaunch
                        {
                            Request = new AttackRequest { Attacker = caster, Weapon = BoltCarrier(), PredictOnly = true },
                            Origin = from,
                            Direction = (tpos - from).normalized,
                            Target = ct,
                            Resolve = false,
                            Visual = true,
                            Look = ProjectileLook.Arcane,
                            Color = c
                        });
                        SfxPlayer.Play(SfxId.CastRelease, from, 0.8f, 0.05f);
                    }
                    break;
                case SpellEffectKind.Heal:
                    fx.Column(target != null ? target.transform : null, target != null ? target.transform.position : point, new Color(1f, 0.9f, 0.55f), 1.1f);
                    SfxPlayer.Play(SfxId.CastRelease, point + Vector3.up, 0.6f, 0.1f);
                    break;
                case SpellEffectKind.Absorb:
                {
                    Transform tt = target != null ? target.transform : null;
                    fx.Spawn(Sphere(), (tt != null ? tt.position : point) + Vector3.up * 1f, new Color(1f, 0.78f, 0.4f, 0.45f), 2.5f,
                        Vector3.one * 1.6f, Vector3.one * 2.1f, tt, Vector3.up * 1f);
                    SfxPlayer.Play(SfxId.ShieldBlock, point + Vector3.up, 0.5f, 0.05f);
                    break;
                }
                case SpellEffectKind.DamageReduction:
                    fx.Spawn(Sphere(), casterPos + Vector3.up, new Color(0.75f, 0.8f, 0.9f, 0.5f), 0.8f, Vector3.one * 1.5f, Vector3.one * 2.3f,
                        caster.transform, Vector3.up);
                    SfxPlayer.Play(SfxId.ShieldBlock, casterPos + Vector3.up, 0.7f, 0f);
                    break;
                case SpellEffectKind.Charge:
                    SfxPlayer.Play(SfxId.Dodge, casterPos + Vector3.up, 0.9f, 0f);
                    break;
                case SpellEffectKind.LeapBack:
                    fx.Ring(casterPos + Vector3.up * 0.1f, 1.6f, new Color(0.7f, 0.65f, 0.55f), 0.5f);
                    SfxPlayer.Play(SfxId.Dodge, casterPos + Vector3.up, 0.8f, 0.05f);
                    break;
                case SpellEffectKind.Resurrect:
                    fx.Column(target != null ? target.transform : null, point, new Color(1f, 0.95f, 0.7f), 2.2f);
                    break;
                default:
                    if (s.target == SpellTarget.AreaAroundSelf)
                    {
                        choreo?.PlayAbility(true, 1, 0.15f, 0.1f, 0.4f);
                        fx.Ring(casterPos + Vector3.up * 0.1f, s.radius, new Color(0.85f, 0.75f, 0.55f), 0.6f);
                        SfxPlayer.Play(SfxId.ShieldBlock, casterPos, 1f, 0f);
                    }
                    else if (s.tauntSeconds > 0f)
                    {
                        fx.Spawn(Sphere(), tpos + Vector3.up * 0.9f, new Color(1f, 0.2f, 0.15f, 0.8f), 0.8f, Vector3.one * 0.25f, Vector3.one * 0.55f,
                            ct != null ? ct.transform : null, ct != null ? tpos + Vector3.up * 0.9f - ct.transform.position : Vector3.zero);
                        SfxPlayer.Play(SfxId.CastRelease, casterPos + Vector3.up, 0.6f, 0.2f);
                    }
                    else choreo?.PlayAbility(true, 2, 0.12f, 0.1f, 0.3f);
                    break;
            }

            // Betaeubung: kleiner Kreis ueber dem Kopf
            if (s.stunSeconds > 0f && ct != null && s.effect != SpellEffectKind.Charge)
                fx.Stars(ct.transform, ct.AimPoint - ct.transform.position + Vector3.up * 1.0f, s.stunSeconds);
            if (s.effect == SpellEffectKind.Charge && ct != null)
                fx.Stars(ct.transform, ct.AimPoint - ct.transform.position + Vector3.up * 1.0f, s.stunSeconds + Mathf.Max(0f, value));
            if (s.slowFactor > 0f && s.target != SpellTarget.AreaAroundSelf && ct != null)
                fx.Ring(ct.transform.position + Vector3.up * 0.1f, 1.2f, new Color(0.55f, 0.7f, 0.95f), 0.6f);
        }

        // ================================================================== Bausteine

        private static CombatChoreographer Choreo(GameObject go) => go != null ? go.GetComponentInChildren<CombatChoreographer>() : null;

        private static Transform Hand(GameObject go)
        {
            var anim = go != null ? go.GetComponentInChildren<Animator>() : null;
            return anim != null && anim.isHuman && anim.avatar != null ? anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
        }

        private static WeaponDefinition BoltCarrier()
        {
            if (_boltCarrier != null) return _boltCarrier;
            _boltCarrier = ScriptableObject.CreateInstance<WeaponDefinition>();
            _boltCarrier.hideFlags = HideFlags.HideAndDontSave;
            _boltCarrier.projectileSpeed = 22f;
            _boltCarrier.projectileGravity = 0f;
            return _boltCarrier;
        }

        private Fx Spawn(Mesh mesh, Vector3 pos, Color color, float life, Vector3 from, Vector3 to, Transform follow = null, Vector3 offset = default)
        {
            var go = new GameObject("Effekt");
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            go.transform.localScale = from;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Mat();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var f = new Fx { T = go.transform, R = mr, Start = Time.time, Life = Mathf.Max(0.05f, life), Color = color, FromScale = from, ToScale = to, Follow = follow, Offset = offset };
            SetColor(f, 1f);
            _fx.Add(f);
            return f;
        }

        private void Ring(Vector3 center, float radius, Color color, float life)
        {
            float d = Mathf.Max(0.5f, radius) * 2f;
            Spawn(RingMesh(), center, color, life, new Vector3(0.3f, 1f, 0.3f), new Vector3(d, 1f, d));
        }

        private void Column(Transform follow, Vector3 pos, Color color, float life)
        {
            Fx f = Spawn(ColumnMesh(), pos, new Color(color.r, color.g, color.b, 0.55f), life, new Vector3(0.9f, 0.2f, 0.9f), new Vector3(1.1f, 2.4f, 1.1f),
                follow, Vector3.zero);
            f.Spin = true;
        }

        private void Stars(Transform follow, Vector3 offset, float seconds)
        {
            if (follow == null || seconds <= 0f) return;
            Fx f = Spawn(RingMesh(), follow.position + offset, new Color(1f, 0.92f, 0.45f, 0.9f), seconds, new Vector3(0.7f, 1f, 0.7f), new Vector3(0.7f, 1f, 0.7f),
                follow, offset);
            f.Spin = true;
        }

        private static void SetColor(Fx f, float alpha)
        {
            if (f.R == null) return;
            var mpb = new MaterialPropertyBlock();
            f.R.GetPropertyBlock(mpb);
            Color c = f.Color;
            c.a *= alpha;
            mpb.SetColor("_Color", c);
            mpb.SetColor("_RendererColor", c);
            f.R.SetPropertyBlock(mpb);
        }

        private void Update()
        {
            float now = Time.time;
            for (int i = _fx.Count - 1; i >= 0; i--)
            {
                Fx f = _fx[i];
                float p = (now - f.Start) / f.Life;
                if (f.T == null || p >= 1f)
                {
                    if (f.T != null) Destroy(f.T.gameObject);
                    _fx.RemoveAt(i);
                    continue;
                }
                if (f.Follow != null) f.T.position = f.Follow.position + (f.Follow == f.T.parent ? Vector3.zero : f.Offset);
                f.T.localScale = Vector3.Lerp(f.FromScale, f.ToScale, Mathf.Sqrt(p));
                if (f.Spin) f.T.rotation = Quaternion.Euler(0f, now * 240f, 0f);
                SetColor(f, p < 0.15f ? p / 0.15f : 1f - Mathf.Clamp01((p - 0.6f) / 0.4f));
            }
        }

        private static Material Mat()
        {
            if (_mat != null) return _mat;
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            _mat = new Material(sh) { name = "Zauber-Effekt", enableInstancing = true };
            return _mat;
        }

        private static Mesh Sphere()
        {
            if (_sphere != null) return _sphere;
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _sphere = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            return _sphere;
        }

        private static Mesh ColumnMesh()
        {
            if (_column != null) return _column;
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _column = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            return _column;
        }

        /// <summary>Flacher Ring (Durchmesser 1, Breite 12 %), nach oben offen.</summary>
        private static Mesh RingMesh()
        {
            if (_ring != null) return _ring;
            const int n = 48;
            var v = new Vector3[n * 2];
            var tri = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v[i * 2] = dir * 0.5f;
                v[i * 2 + 1] = dir * 0.44f;
                int j = (i + 1) % n;
                tri[i * 6] = i * 2; tri[i * 6 + 1] = j * 2; tri[i * 6 + 2] = i * 2 + 1;
                tri[i * 6 + 3] = j * 2; tri[i * 6 + 4] = j * 2 + 1; tri[i * 6 + 5] = i * 2 + 1;
            }
            _ring = new Mesh { name = "Zauber-Ring", vertices = v, triangles = tri };
            _ring.RecalculateNormals();
            _ring.RecalculateBounds();
            return _ring;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
        }
    }
}
