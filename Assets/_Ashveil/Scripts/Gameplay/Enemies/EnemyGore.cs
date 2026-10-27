using System.Collections.Generic;
using Ashveil.Core.Settings;
using UnityEngine;

namespace Ashveil.Gameplay.Enemies
{
    /// <summary>
    /// Wunden, Splitter, Flecken und abgetrennte Teile – nur Darstellung (jeder Rechner fuer sich), mit festen Budgets
    /// (Masterplan: Ragdoll und Blut nur nah und zeitlich begrenzt). Beachtet die Einstellung „Blut und Zerteilung“:
    /// Voll = alles, Weniger = Splitter ohne Flecken und ohne Abtrennen beim Tod, Aus = nichts.
    /// </summary>
    public static class EnemyGore
    {
        public const int MaxShards = 90;
        public const int MaxStains = 40;
        /// <summary>Ab dieser Entfernung zur Kamera keine Splitter/Flecken (Leistung).</summary>
        public const float MaxDistance = 60f;

        private static readonly Queue<GameObject> Shards = new Queue<GameObject>();
        private static readonly Queue<GameObject> Stains = new Queue<GameObject>();
        private static readonly Dictionary<(Material, Color), Material> Tinted = new Dictionary<(Material, Color), Material>();

        /// <summary>0 = Voll, 1 = Weniger, 2 = Aus.</summary>
        public static int Level => GraphicsSettingsStore.Current != null ? GraphicsSettingsStore.Current.blood : 0;
        public static bool Full => Level == 0;
        public static bool Any => Level < 2;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Shards.Clear();
            Stains.Clear();
            Tinted.Clear();
        }

        private static bool Near(Vector3 p)
        {
            Camera cam = Camera.main;
            return cam == null || (cam.transform.position - p).sqrMagnitude < MaxDistance * MaxDistance;
        }

        /// <summary>Material in anderer Farbe (Kopie eines vorhandenen – so bleibt der Shader sicher im Build).</summary>
        public static Material Tint(Material source, Color color)
        {
            if (source == null) return null;
            if (Tinted.TryGetValue((source, color), out Material m) && m != null) return m;
            m = new Material(source) { name = source.name + " (Wunde)" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            Tinted[(source, color)] = m;
            return m;
        }

        /// <summary>Kleine Splitter, die wegspritzen und liegen bleiben (Treffer, Bruch).</summary>
        public static void Burst(Vector3 point, Vector3 direction, Material mat, int count, float size, float life = 6f)
        {
            if (!Any || mat == null || !Near(point)) return;
            if (!Full) count = Mathf.Max(1, count / 2);
            for (int i = 0; i < count; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Splitter";
                go.layer = 2; // Ignore Raycast: stoert weder Klicks noch Kamera
                go.transform.position = point + Random.insideUnitSphere * size;
                go.transform.rotation = Random.rotation;
                go.transform.localScale = Vector3.one * size * Random.Range(0.5f, 1.3f);
                go.GetComponent<Renderer>().sharedMaterial = mat;
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = 0.2f;
                Vector3 v = (direction.normalized + Random.insideUnitSphere * 0.8f + Vector3.up * 0.9f).normalized * Random.Range(2.5f, 5.5f);
                rb.linearVelocity = v;
                rb.angularVelocity = Random.insideUnitSphere * 10f;
                Object.Destroy(go, life * Random.Range(0.7f, 1.2f));
                Track(Shards, go, MaxShards);
            }
        }

        /// <summary>Fleck am Boden unter dem Punkt (nur bei „Voll“).</summary>
        public static void Stain(Vector3 point, Material mat, float size, float life = 30f)
        {
            if (!Full || mat == null || !Near(point)) return;
            if (!Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 6f,
                    Ashveil.Core.GameLayers.CameraCollisionMask(), QueryTriggerInteraction.Ignore)) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Fleck";
            Object.Destroy(go.GetComponent<Collider>());
            go.layer = 2;
            go.transform.position = hit.point + hit.normal * 0.02f;
            go.transform.rotation = Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            go.transform.localScale = new Vector3(size * Random.Range(0.8f, 1.3f), size * Random.Range(0.6f, 1.1f), 1f);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Object.Destroy(go, life);
            Track(Stains, go, MaxStains);
        }

        /// <summary>
        /// Teil abtrennen: Gruppe loest sich vom Koerper und faellt mit Physik (bleibt unter <paramref name="keepUnder"/>,
        /// damit sie mit der Leiche verschwindet).
        /// </summary>
        public static GameObject Detach(Transform part, Transform keepUnder, Vector3 impulse, bool addCollider = true)
        {
            if (part == null) return null;
            Bounds b = LocalBounds(part);
            part.SetParent(keepUnder, true);
            foreach (Transform t in part.GetComponentsInChildren<Transform>(true))
            {
                Collider c = t.GetComponent<Collider>();
                if (c != null && c.isTrigger) c.enabled = false; // Trefferzone weg
                t.gameObject.layer = 2;
            }
            if (addCollider)
            {
                var box = part.gameObject.AddComponent<BoxCollider>();
                box.center = b.center;
                box.size = Vector3.Max(b.size, Vector3.one * 0.1f);
            }
            var rb = part.gameObject.GetComponent<Rigidbody>();
            if (rb == null) rb = part.gameObject.AddComponent<Rigidbody>();
            rb.mass = 20f;
            rb.linearVelocity = impulse;
            rb.angularVelocity = Random.insideUnitSphere * 2.5f;
            return part.gameObject;
        }

        /// <summary>Grenzen aller Renderer einer Gruppe im lokalen Raum der Gruppe.</summary>
        public static Bounds LocalBounds(Transform root)
        {
            bool any = false;
            var b = new Bounds(Vector3.zero, Vector3.zero);
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                Matrix4x4 m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        private static void Track(Queue<GameObject> q, GameObject go, int max)
        {
            q.Enqueue(go);
            while (q.Count > max)
            {
                GameObject old = q.Dequeue();
                if (old != null) Object.Destroy(old);
            }
        }
    }
}
