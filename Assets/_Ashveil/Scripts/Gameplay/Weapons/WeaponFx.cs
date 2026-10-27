using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using UnityEngine;

namespace Ashveil.Gameplay.Weapons
{
    /// <summary>
    /// Sichtbares Feedback der Waffen, solange es noch keine Animationen gibt:
    ///  - Platzhaltermodelle (Schwert / Pistole) werden je nach aktiver Waffe gezeigt
    ///  - Schwert schwingt prozedural (Ausholen, Schlag, Zurueck), Spezialangriff weiter und schneller
    ///  - Pistole: Rueckstoss, Muendungsfeuer (Licht + Blitz), Leuchtspur zum Einschlag, Einschlagfunke
    /// Ab Milestone 2b uebernehmen Animationen die Bewegung; Muendungsfeuer und Spuren bleiben.
    /// </summary>
    public sealed class WeaponFx : MonoBehaviour
    {
        [SerializeField] private WeaponUser user;
        [Header("Platzhaltermodelle")]
        [SerializeField] private Transform meleePivot;
        [SerializeField] private Transform rangedPivot;
        [SerializeField] private Transform muzzlePoint;
        [Header("Materialien")]
        [SerializeField] private Material tracerMaterial;
        [SerializeField] private Material flashMaterial;
        [Tooltip("Aus, sobald ein animiertes Modell die Waffe fuehrt (Milestone 2b)")]
        [SerializeField] private bool proceduralMotion = true;
        [Header("Schwung")]
        [SerializeField] private float windupAngle = -75f;
        [SerializeField] private float strikeAngle = 85f;
        [SerializeField] private float specialExtra = 35f;

        private const int TracerCount = 12;
        private readonly List<LineRenderer> _tracers = new List<LineRenderer>();
        private readonly List<float> _tracerLife = new List<float>();
        private readonly List<Transform> _sparks = new List<Transform>();
        private readonly List<float> _sparkLife = new List<float>();
        private int _nextTracer;
        private int _nextSpark;

        private Transform _poolRoot;
        private Light _muzzleLight;
        private Transform _muzzleFlash;
        private float _muzzleTimer;
        private float _recoil;
        private Quaternion _meleeRest;
        private Quaternion _rangedRest;

        private void Awake()
        {
            if (user == null) user = GetComponentInParent<WeaponUser>();
            if (meleePivot != null) _meleeRest = meleePivot.localRotation;
            if (rangedPivot != null) _rangedRest = rangedPivot.localRotation;
            BuildPools();
        }

        private void OnEnable()
        {
            if (user == null) return;
            user.Fired += OnFired;
            user.WeaponChanged += OnWeaponChanged;
            user.Slammed += OnSlammed;
        }

        private void OnDisable()
        {
            if (user == null) return;
            user.Fired -= OnFired;
            user.WeaponChanged -= OnWeaponChanged;
            user.Slammed -= OnSlammed;
        }

        private void Start()
        {
            OnWeaponChanged(user != null ? user.ActiveWeapon : null);
        }

        private void OnDestroy()
        {
            if (_poolRoot != null) Destroy(_poolRoot.gameObject);
        }

        private void OnWeaponChanged(WeaponDefinition w)
        {
            bool melee = w != null && w.HeldLikeMelee;
            if (meleePivot != null) meleePivot.gameObject.SetActive(melee);
            if (rangedPivot != null) rangedPivot.gameObject.SetActive(w != null && !melee);
        }

        // ---------------------------------------------------------------- Schuss

        private void OnFired(Vector3 muzzle, IReadOnlyList<HitResult> hits)
        {
            WeaponDefinition w = user.ActiveWeapon;
            Color c = w != null ? w.effectColor : new Color(1f, 0.75f, 0.4f);
            Vector3 start = muzzlePoint != null ? muzzlePoint.position : muzzle;

            for (int i = 0; i < hits.Count; i++)
            {
                SpawnTracer(start, hits[i].Point, c);
                SpawnSpark(hits[i].Point, hits[i].HitTarget ? 0.35f : 0.2f);
            }

            _muzzleTimer = 0.06f;
            if (_muzzleLight != null)
            {
                _muzzleLight.transform.position = start;
                _muzzleLight.color = c;
            }
            if (_muzzleFlash != null) _muzzleFlash.position = start;
            _recoil = 1f;
        }

        /// <summary>Sturzangriff: flache, schnell wachsende Druckwelle am Boden.</summary>
        private void OnSlammed(Vector3 position, float radius)
        {
            Transform s = _sparks[_nextSpark];
            _sparkLife[_nextSpark] = 0.22f;
            _nextSpark = (_nextSpark + 1) % _sparks.Count;
            s.position = position + Vector3.up * 0.1f;
            s.localScale = new Vector3(radius * 0.5f, 0.05f, radius * 0.5f);
            s.gameObject.SetActive(true);
        }

        private void SpawnTracer(Vector3 from, Vector3 to, Color c)
        {
            LineRenderer lr = _tracers[_nextTracer];
            _tracerLife[_nextTracer] = 0.09f;
            _nextTracer = (_nextTracer + 1) % _tracers.Count;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.startColor = new Color(c.r, c.g, c.b, 0.2f);
            lr.endColor = new Color(1f, 0.95f, 0.8f, 0.95f);
            lr.enabled = true;
        }

        private void SpawnSpark(Vector3 point, float size)
        {
            Transform s = _sparks[_nextSpark];
            _sparkLife[_nextSpark] = 0.1f;
            _nextSpark = (_nextSpark + 1) % _sparks.Count;
            s.position = point;
            s.localScale = Vector3.one * size;
            s.gameObject.SetActive(true);
        }

        // ---------------------------------------------------------------- Pro Frame

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            AnimateMelee();
            AnimateRanged(dt);

            for (int i = 0; i < _tracers.Count; i++)
            {
                if (_tracerLife[i] <= 0f) continue;
                _tracerLife[i] -= dt;
                if (_tracerLife[i] <= 0f) _tracers[i].enabled = false;
            }
            for (int i = 0; i < _sparks.Count; i++)
            {
                if (_sparkLife[i] <= 0f) continue;
                _sparkLife[i] -= dt;
                _sparks[i].localScale *= 1f + dt * 6f;
                if (_sparkLife[i] <= 0f) _sparks[i].gameObject.SetActive(false);
            }

            _muzzleTimer -= dt;
            bool flash = _muzzleTimer > 0f;
            if (_muzzleLight != null) _muzzleLight.enabled = flash;
            if (_muzzleFlash != null && _muzzleFlash.gameObject.activeSelf != flash) _muzzleFlash.gameObject.SetActive(flash);
        }

        /// <summary>Prozedurales Schwingen/Rueckstoss an- oder abschalten (bei Animationen aus).</summary>
        public void SetProceduralMotion(bool enabledMotion)
        {
            proceduralMotion = enabledMotion;
            if (!enabledMotion)
            {
                if (meleePivot != null) meleePivot.localRotation = _meleeRest;
                if (rangedPivot != null) rangedPivot.localRotation = _rangedRest;
            }
        }

        /// <summary>Ruhelage neu uebernehmen, z. B. nachdem die Waffe an eine Hand gehaengt wurde.</summary>
        public void CaptureRestPose()
        {
            if (meleePivot != null) _meleeRest = meleePivot.localRotation;
            if (rangedPivot != null) _rangedRest = rangedPivot.localRotation;
        }

        public Transform MeleePivot => meleePivot;
        public Transform RangedPivot => rangedPivot;

        /// <summary>Ausholen nach hinten, schneller Schlag nach vorn, langsam zurueck.</summary>
        private void AnimateMelee()
        {
            if (!proceduralMotion || meleePivot == null || user == null) return;
            WeaponDefinition w = user.ActiveWeapon;
            if (w == null || !w.IsMelee) return;

            float extra = user.CurrentStepIsSpecial ? specialExtra : 0f;
            float back = windupAngle - extra * 0.5f;
            float front = strikeAngle + extra;
            float p = user.PhaseProgress;
            float angle;
            switch (user.Phase)
            {
                case WeaponPhase.Windup: angle = Mathf.Lerp(0f, back, Smooth(p)); break;
                case WeaponPhase.Active: angle = Mathf.Lerp(back, front, EaseOut(p)); break;
                case WeaponPhase.Recovery: angle = Mathf.Lerp(front, 0f, Smooth(p)); break;
                default: angle = 0f; break;
            }
            // Kombo-Schlaege abwechselnd von rechts und links, Spezial von oben.
            Vector3 axis = user.CurrentStepIsSpecial ? Vector3.right : Vector3.up;
            meleePivot.localRotation = _meleeRest * Quaternion.AngleAxis(angle, axis);
        }

        private void AnimateRanged(float dt)
        {
            if (!proceduralMotion || rangedPivot == null) return;
            _recoil = Mathf.MoveTowards(_recoil, 0f, dt * 6f);
            float aimRaise = user != null && (user.Phase == WeaponPhase.Windup || user.Phase == WeaponPhase.Active) ? 1f : 0f;
            float reload = user != null && user.IsReloading ? Mathf.Sin(user.PhaseProgress * Mathf.PI) : 0f;
            rangedPivot.localRotation = _rangedRest *
                                        Quaternion.Euler(-_recoil * 22f - aimRaise * 4f + reload * 55f, 0f, reload * -30f);
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);
        private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

        // ---------------------------------------------------------------- Aufbau

        private void BuildPools()
        {
            var root = new GameObject("WeaponFx Pool").transform;
            _poolRoot = root;

            Material lineMat = tracerMaterial;
            if (lineMat == null)
            {
                Shader sh = Shader.Find("Sprites/Default");
                if (sh != null) lineMat = new Material(sh);
            }

            for (int i = 0; i < TracerCount; i++)
            {
                var go = new GameObject("Tracer");
                go.transform.SetParent(root, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.useWorldSpace = true;
                lr.startWidth = 0.035f;
                lr.endWidth = 0.02f;
                lr.numCapVertices = 2;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                if (lineMat != null) lr.sharedMaterial = lineMat;
                lr.enabled = false;
                _tracers.Add(lr);
                _tracerLife.Add(0f);
            }

            for (int i = 0; i < 8; i++)
            {
                GameObject spark = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                spark.name = "Spark";
                Destroy(spark.GetComponent<Collider>());
                spark.transform.SetParent(root, false);
                var mr = spark.GetComponent<MeshRenderer>();
                if (flashMaterial != null) mr.sharedMaterial = flashMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                spark.SetActive(false);
                _sparks.Add(spark.transform);
                _sparkLife.Add(0f);
            }

            var lightGo = new GameObject("Muzzle Light");
            lightGo.transform.SetParent(root, false);
            _muzzleLight = lightGo.AddComponent<Light>();
            _muzzleLight.type = LightType.Point;
            _muzzleLight.range = 7f;
            _muzzleLight.intensity = 5f;
            _muzzleLight.enabled = false;

            GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.name = "Muzzle Flash";
            Destroy(flash.GetComponent<Collider>());
            flash.transform.SetParent(root, false);
            flash.transform.localScale = new Vector3(0.22f, 0.22f, 0.22f);
            var fmr = flash.GetComponent<MeshRenderer>();
            if (flashMaterial != null) fmr.sharedMaterial = flashMaterial;
            fmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            flash.SetActive(false);
            _muzzleFlash = flash.transform;
        }
    }
}
