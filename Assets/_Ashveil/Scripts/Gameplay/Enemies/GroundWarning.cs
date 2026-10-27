using UnityEngine;

namespace Ashveil.Gameplay.Enemies
{
    /// <summary>
    /// Warnring am Boden fuer Flaechenangriffe (Stampfer): waechst waehrend der Warnzeit bis zum
    /// Trefferradius, blinkt kurz vor dem Einschlag und verschwindet danach. Rein darstellend.
    /// </summary>
    public sealed class GroundWarning : MonoBehaviour
    {
        private Transform _fill, _rim;
        private float _duration, _time, _radius;

        public static GroundWarning Spawn(Vector3 center, float radius, float duration, Material fillMaterial, Material rimMaterial)
        {
            var go = new GameObject("Bodenwarnung");
            go.transform.position = center + Vector3.up * 0.06f;
            var w = go.AddComponent<GroundWarning>();
            w._radius = Mathf.Max(0.5f, radius);
            w._duration = Mathf.Max(0.1f, duration);
            w._rim = Disc(go.transform, "Rand", rimMaterial, 0f);
            w._fill = Disc(go.transform, "Flaeche", fillMaterial, 0.01f);
            w._rim.localScale = new Vector3(w._radius * 2f, 0.01f, w._radius * 2f);
            w._fill.localScale = new Vector3(0.01f, 0.01f, 0.01f);
            return w;
        }

        private static Transform Disc(Transform parent, string name, Material mat, float lift)
        {
            GameObject d = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            d.name = name;
            Object.Destroy(d.GetComponent<Collider>());
            d.transform.SetParent(parent, false);
            d.transform.localPosition = Vector3.up * lift;
            var r = d.GetComponent<MeshRenderer>();
            if (mat != null) r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return d.transform;
        }

        private void Update()
        {
            _time += Time.deltaTime;
            float k = Mathf.Clamp01(_time / _duration);
            float r = _radius * 2f * Mathf.Lerp(0.15f, 1f, k);
            _fill.localScale = new Vector3(r, 0.01f, r);
            // kurz vor dem Einschlag blinken
            bool blink = k > 0.75f && Mathf.Repeat(_time * 10f, 1f) < 0.5f;
            _rim.gameObject.SetActive(!blink);
            if (_time >= _duration + 0.25f) Destroy(gameObject);
        }
    }
}
