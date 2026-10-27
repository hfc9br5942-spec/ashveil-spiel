using UnityEngine;

namespace Ashveil.Gameplay.Combat
{
    /// <summary>
    /// Sichtbare Trefferreaktion ohne Animation: kurzes helles Aufblitzen, Wegzucken in Schlagrichtung,
    /// leichtes Stauchen. Wird bei Animationsmodellen (Milestone 2b) durch Hit-Animationen ergaenzt.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public sealed class HitReaction : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [Tooltip("Dieses Kind wird bewegt (nie das Objekt selbst, sonst stoert es Physik/KI)")]
        [SerializeField] private Transform visual;
        [SerializeField] private Color flashColor = new Color(1f, 0.95f, 0.85f);
        [SerializeField] private float flashDuration = 0.09f;
        [SerializeField] private float punchReturnSpeed = 10f;
        [SerializeField] private float maxPunch = 0.35f;

        private Health _health;
        private Renderer[] _renderers;
        private Color[][] _baseColors;
        private MaterialPropertyBlock _block;
        private MaterialPropertyBlock _emptyBlock;
        private bool _flashing;
        private float _flashTimer;
        private Vector3 _punch;
        private float _squash;
        private Vector3 _visualBasePos;
        private Vector3 _visualBaseScale;

        private void Awake()
        {
            _health = GetComponent<Health>();
            if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
            if (visual != null)
            {
                _visualBasePos = visual.localPosition;
                _visualBaseScale = visual.localScale;
            }

            Transform root = visual != null ? visual : transform;
            _renderers = root.GetComponentsInChildren<Renderer>();
            _baseColors = new Color[_renderers.Length][];
            for (int i = 0; i < _renderers.Length; i++)
            {
                Material[] mats = _renderers[i].sharedMaterials;
                _baseColors[i] = new Color[mats.Length];
                for (int m = 0; m < mats.Length; m++)
                    _baseColors[i][m] = mats[m] != null && mats[m].HasProperty(BaseColorId) ? mats[m].GetColor(BaseColorId)
                        : mats[m] != null && mats[m].HasProperty(ColorId) ? mats[m].GetColor(ColorId) : Color.white;
            }
            _block = new MaterialPropertyBlock();
            _emptyBlock = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            _health.Damaged += OnDamaged;
        }

        private void OnDisable()
        {
            _health.Damaged -= OnDamaged;
        }

        private void OnDamaged(DamageInfo info)
        {
            if (info.Dodged) return; // ausgewichen: kein Treffer-Aufblitzen
            _flashTimer = flashDuration;
            _flashing = true;
            Vector3 dir = info.Direction.sqrMagnitude > 0.001f ? info.Direction.normalized : -transform.forward;
            _punch = Vector3.ClampMagnitude(_punch + dir * (0.12f + info.Knockback * 0.5f), maxPunch);
            _squash = info.IsCritical ? 0.18f : 0.1f;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;

            // Aufblitzen (per MaterialPropertyBlock, damit Materialien nicht dupliziert werden)
            if (_flashing)
            {
                _flashTimer -= dt;
                float t = flashDuration > 0f ? Mathf.Clamp01(_flashTimer / flashDuration) : 0f;
                for (int i = 0; i < _renderers.Length; i++)
                {
                    Renderer r = _renderers[i];
                    if (r == null) continue;
                    for (int m = 0; m < _baseColors[i].Length; m++)
                    {
                        if (t <= 0f)
                        {
                            r.SetPropertyBlock(_emptyBlock, m); // Ueberschreibung entfernen
                            continue;
                        }
                        Color c = Color.Lerp(_baseColors[i][m], flashColor, t);
                        _block.Clear();
                        _block.SetColor(BaseColorId, c);
                        _block.SetColor(ColorId, c);
                        r.SetPropertyBlock(_block, m);
                    }
                }
                if (t <= 0f) _flashing = false;
            }

            // Wegzucken & Stauchen
            if (visual != null)
            {
                _punch = Vector3.Lerp(_punch, Vector3.zero, 1f - Mathf.Exp(-punchReturnSpeed * dt));
                _squash = Mathf.Lerp(_squash, 0f, 1f - Mathf.Exp(-punchReturnSpeed * dt));
                visual.localPosition = _visualBasePos + transform.InverseTransformDirection(_punch);
                visual.localScale = new Vector3(
                    _visualBaseScale.x * (1f + _squash * 0.5f),
                    _visualBaseScale.y * (1f - _squash),
                    _visualBaseScale.z * (1f + _squash * 0.5f));
            }
        }
    }
}
