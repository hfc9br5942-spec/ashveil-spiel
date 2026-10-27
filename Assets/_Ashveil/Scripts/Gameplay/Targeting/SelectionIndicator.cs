using Ashveil.Gameplay.Combat;
using UnityEngine;

namespace Ashveil.Gameplay.Targeting
{
    /// <summary>
    /// Leuchtender Ring am Boden unter dem aktuellen Ziel, in der Farbe der Zugehoerigkeit
    /// (rot feindlich, gelb neutral, gruen freundlich). Der Ring wird zur Laufzeit erzeugt.
    /// </summary>
    public sealed class SelectionIndicator : MonoBehaviour
    {
        [SerializeField] private TargetingSystem targeting;
        [SerializeField] private float pulseSpeed = 3f;
        [SerializeField] private float rotationSpeed = 25f;

        private SpriteRenderer _ring;
        private Transform _ringTransform;

        private void Awake()
        {
            if (targeting == null) targeting = GetComponent<TargetingSystem>();

            var go = new GameObject("Selection Ring");
            _ringTransform = go.transform;
            _ring = go.AddComponent<SpriteRenderer>();
            _ring.sprite = CreateRingSprite(128);
            _ring.sortingOrder = 10;
            go.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_ringTransform != null) Destroy(_ringTransform.gameObject);
            if (_ring != null && _ring.sprite != null)
            {
                Destroy(_ring.sprite.texture);
                Destroy(_ring.sprite);
            }
        }

        private void LateUpdate()
        {
            CombatTarget t = targeting != null ? targeting.Current : null;
            if (t == null)
            {
                if (_ringTransform.gameObject.activeSelf) _ringTransform.gameObject.SetActive(false);
                return;
            }

            if (!_ringTransform.gameObject.activeSelf) _ringTransform.gameObject.SetActive(true);

            Vector3 pos = t.transform.position + Vector3.up * 0.05f;
            float size = t.SelectionRadius * 2.4f;
            float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * 0.04f;
            _ringTransform.position = pos;
            _ringTransform.rotation = Quaternion.Euler(90f, Time.time * rotationSpeed, 0f);
            _ringTransform.localScale = Vector3.one * size * pulse;

            Color c = FactionRules.NameColor(t.Faction);
            c.a = 0.85f;
            _ring.color = c;
        }

        /// <summary>Weicher Ring mit vier Luecken – als Textur erzeugt, damit keine Grafikdatei noetig ist.</summary>
        private static Sprite CreateRingSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            float half = size * 0.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ring = 1f - Mathf.Clamp01(Mathf.Abs(r - 0.86f) / 0.07f);
                    float glow = (1f - Mathf.Clamp01(Mathf.Abs(r - 0.86f) / 0.2f)) * 0.35f;
                    float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    float gap = Mathf.Abs(Mathf.DeltaAngle(angle, Mathf.Round(angle / 90f) * 90f + 45f));
                    float gapMask = Mathf.Clamp01((gap - 6f) / 4f);
                    float a = Mathf.Clamp01(ring + glow) * Mathf.Lerp(0.25f, 1f, gapMask);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
