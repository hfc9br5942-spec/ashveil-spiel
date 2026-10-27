using Ashveil.Data;
using Ashveil.Gameplay.Audio;
using UnityEngine;

namespace Ashveil.Gameplay.Visuals
{
    /// <summary>
    /// Stufenaufstieg, fuer alle sichtbar: drei goldene Ringe steigen an der Figur hoch, dazu ein warmes Licht
    /// und ein Glockenakkord. Nur Code, keine Assets (Platzhalter bis zu echten VFX).
    /// </summary>
    public sealed class LevelUpEffect : MonoBehaviour
    {
        private const float Duration = 1.8f;
        private static readonly Color Gold = new Color(1f, 0.78f, 0.32f);

        private Transform[] _rings;
        private Light _light;
        private float _t;

        public static void Play(Transform target)
        {
            if (target == null) return;
            var go = new GameObject("Level Up Effect");
            go.transform.SetParent(target, false);
            go.AddComponent<LevelUpEffect>().Build();
            SfxPlayer.Play(SfxId.LevelUp, target.position + Vector3.up, 0.9f, 0f);
        }

        private void Build()
        {
            var part = new RigidPart { shape = ProcShape.Torus, size = new Vector3(1f, 0.05f, 1f), segments = 32 };
            Mesh mesh = ProcMesh.Get(part);
            Material mat = SurfaceMaterials.Get(SurfaceKind.Glow, Gold);
            _rings = new Transform[3];
            for (int i = 0; i < _rings.Length; i++)
            {
                var r = new GameObject("Ring " + i);
                r.transform.SetParent(transform, false);
                r.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = r.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _rings[i] = r.transform;
            }
            var lg = new GameObject("Light");
            lg.transform.SetParent(transform, false);
            lg.transform.localPosition = Vector3.up * 1.2f;
            _light = lg.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = Gold;
            _light.range = 6f;
            _light.intensity = 0f;
            Tick(0f);
        }

        private void Update()
        {
            _t += Time.deltaTime;
            Tick(_t / Duration);
            if (_t >= Duration) Destroy(gameObject);
        }

        private void Tick(float k)
        {
            if (_rings == null) return;
            for (int i = 0; i < _rings.Length; i++)
            {
                float local = Mathf.Clamp01(k * 1.4f - i * 0.18f);
                float y = Mathf.Lerp(0.05f, 2.3f, Mathf.SmoothStep(0f, 1f, local));
                float s = Mathf.Lerp(1.3f, 0.55f, local) * (local <= 0f || local >= 1f ? 0f : 1f);
                _rings[i].localPosition = Vector3.up * y;
                _rings[i].localScale = new Vector3(s, 1f, s);
            }
            if (_light != null) _light.intensity = 4f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(k));
        }
    }
}
