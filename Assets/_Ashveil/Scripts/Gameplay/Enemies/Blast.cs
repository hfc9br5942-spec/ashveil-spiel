using UnityEngine;

namespace Ashveil.Gameplay.Enemies
{
    /// <summary>
    /// Explosion einer Sprengladung (Platzhalter): kurzer Lichtblitz und eine flache, schnell wachsende Scheibe in der
    /// Farbe des Warnrings. Rein darstellend, zerstoert sich selbst nach ca. 0,6 s.
    /// </summary>
    public sealed class Blast : MonoBehaviour
    {
        private const float Duration = 0.6f;
        private Light _light;
        private Transform _disc;
        private float _radius, _time;

        public static Blast Spawn(Vector3 center, float radius, Material material)
        {
            var go = new GameObject("Explosion");
            go.transform.position = center + Vector3.up * 0.5f;
            var b = go.AddComponent<Blast>();
            b._radius = Mathf.Max(0.5f, radius);
            b._light = go.AddComponent<Light>();
            b._light.type = LightType.Point;
            b._light.color = new Color(1f, 0.62f, 0.3f);
            b._light.range = b._radius * 3f;
            b._light.intensity = 60f;
            b._light.shadows = LightShadows.None;

            GameObject d = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            d.name = "Druckwelle";
            Object.Destroy(d.GetComponent<Collider>());
            d.transform.SetParent(go.transform, false);
            d.transform.localPosition = Vector3.down * 0.42f;
            var r = d.GetComponent<MeshRenderer>();
            if (material != null) r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            b._disc = d.transform;
            b._disc.localScale = new Vector3(0.2f, 0.02f, 0.2f);
            return b;
        }

        private void Update()
        {
            _time += Time.deltaTime;
            float k = Mathf.Clamp01(_time / Duration);
            float size = _radius * 2f * Mathf.Sqrt(k) * 1.1f;
            _disc.localScale = new Vector3(size, 0.02f, size);
            _light.intensity = 60f * (1f - k) * (1f - k);
            if (_time >= Duration) Destroy(gameObject);
        }
    }
}
