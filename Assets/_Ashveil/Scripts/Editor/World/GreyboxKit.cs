using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using Ashveil.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Bausteine fuer Greybox-Szenen: Materialien, Primitive, Rampen, Beschriftungen, Partikel.
    /// Alle Materialien werden als Assets gespeichert (Materials/Greybox), damit sie spaeter
    /// zentral gegen finale Materialien getauscht werden koennen.
    /// </summary>
    public static class GreyboxKit
    {
        private const string MaterialFolder = ProjectSetup.GreyboxMaterials;
        private const string UrpParticleMaterialPath =
            "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat";

        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        public static void ResetCache()
        {
            Cache.Clear();
        }

        // ------------------------------------------------------------------ Materialien

        public static Shader LitShader()
        {
            Shader s = Shader.Find("Universal Render Pipeline/Lit");
            if (s == null) s = Shader.Find("Standard");
            return s;
        }

        /// <summary>Holt oder erzeugt ein Greybox-Material. emission = null bedeutet kein Leuchten.</summary>
        public static Material Mat(string name, Color color, float smoothness = 0.08f, Color? emission = null)
        {
            if (Cache.TryGetValue(name, out Material cached) && cached != null) return cached;

            ProjectSetup.EnsureFolder(MaterialFolder);
            string path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader lit = LitShader();
            if (mat == null)
            {
                mat = new Material(lit);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != lit)
            {
                mat.shader = lit;
            }

            mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);

            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emission.Value);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
            }

            EditorUtility.SetDirty(mat);
            Cache[name] = mat;
            return mat;
        }

        public static Material ParticleMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(UrpParticleMaterialPath);
            if (mat != null) return mat;

            // Fallback ueber Reflection, um Obsolete-Warnungen je nach Unity-Version zu vermeiden.
            RenderPipelineAsset rp = GraphicsSettings.currentRenderPipeline;
            if (rp != null)
            {
                PropertyInfo prop = rp.GetType().GetProperty("defaultParticleMaterial", BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.GetValue(rp) is Material m && m != null) return m;
            }
            return AssetDatabase.GetBuiltinExtraResource<Material>("Default-ParticleSystem.mat");
        }

        // ------------------------------------------------------------------ Gruppen & Primitive

        public static Transform Group(string name, Transform parent)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            return go.transform;
        }

        public static Transform Group(string name, Transform parent, Vector3 localPos, Vector3 localEuler)
        {
            Transform t = Group(name, parent);
            t.localPosition = localPos;
            t.localRotation = Quaternion.Euler(localEuler);
            return t;
        }

        /// <summary>
        /// Erzeugt ein Primitiv. Kugeln und Zylinder bekommen MeshCollider, damit auch
        /// ungleichmaessig skalierte Formen korrekt kollidieren.
        /// </summary>
        public static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos,
            Vector3 scale, Vector3 localEuler, Material mat, bool collider = true)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            Collider existing = go.GetComponent<Collider>();
            if (!collider)
            {
                Object.DestroyImmediate(existing);
            }
            else if (type == PrimitiveType.Sphere || type == PrimitiveType.Cylinder)
            {
                Object.DestroyImmediate(existing);
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            }

            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }

        public static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat,
            Vector3 euler = default, bool collider = true)
        {
            return Prim(PrimitiveType.Cube, name, parent, pos, size, euler, mat, collider);
        }

        /// <summary>Quader, dessen Unterkante auf y = baseY steht.</summary>
        public static GameObject Block(string name, Transform parent, Vector3 basePos, Vector3 size, Material mat, float yaw = 0f)
        {
            return Box(name, parent, basePos + Vector3.up * (size.y * 0.5f), size, mat, new Vector3(0f, yaw, 0f));
        }

        /// <summary>
        /// Rampe: beginnt bei lowEnd (Bodenhoehe) und steigt in Richtung yaw mit angleDeg an.
        /// Gibt die obere Kante (Mitte) zurueck.
        /// </summary>
        public static Vector3 Ramp(string name, Transform parent, Vector3 lowEnd, float yaw, float angleDeg,
            float length, float width, Material mat)
        {
            Transform pivot = Group(name, parent, lowEnd, new Vector3(0f, yaw, 0f));
            float a = angleDeg * Mathf.Deg2Rad;
            const float thickness = 0.4f;
            float h = Mathf.Sin(a) * length;
            float d = Mathf.Cos(a) * length;
            // Mittelpunkt der Oberkante minus halbe Dicke entlang der Flaechennormale (0, cos a, -sin a).
            Box("Surface", pivot, new Vector3(0f, h * 0.5f - thickness * 0.5f * Mathf.Cos(a), d * 0.5f + thickness * 0.5f * Mathf.Sin(a)),
                new Vector3(width, thickness, length), mat, new Vector3(-angleDeg, 0f, 0f));
            return pivot.TransformPoint(new Vector3(0f, h, d));
        }

        /// <summary>Flacher Streifen zwischen zwei Punkten (Wege, Bodenflaechen).</summary>
        public static GameObject Strip(string name, Transform parent, Vector3 from, Vector3 to, float width, Material mat, float y = 0.015f)
        {
            Vector3 delta = to - from;
            delta.y = 0f;
            float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            Vector3 mid = (from + to) * 0.5f;
            mid.y = y;
            return Box(name, parent, mid, new Vector3(width, 0.03f, delta.magnitude), mat, new Vector3(0f, yaw, 0f), false);
        }

        public static void MakeDynamic(GameObject root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
        }

        public static void SetLayerRecursively(GameObject root, int layer)
        {
            if (layer < 0) return;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        // ------------------------------------------------------------------ Beschriftung

        /// <summary>
        /// Namensschild / Hinweis an einem Weltpunkt. Gezeichnet wird es scharf als Bildschirmtext
        /// vom WorldLabelRenderer (siehe Ashveil.UI).
        /// </summary>
        public static WorldLabel Label(Transform parent, Vector3 localPos, string title, string subtitle,
            WorldLabelStyle style, float maxDistance, float size = 1f)
        {
            var go = new GameObject("Label " + title);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var label = go.AddComponent<WorldLabel>();
            label.Configure(title, subtitle, style, maxDistance, size);
            return label;
        }

        // ------------------------------------------------------------------ Partikel

        public static ParticleSystem Particles(string name, Transform parent, Vector3 pos, Color color, float size,
            float rate, float speed, float lifetime, float coneAngle, float radius)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // Kegel zeigt nach oben

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.startColor = color;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.CeilToInt(rate * lifetime * 1.3f) + 10;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = rate;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = radius;

            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(color.a, 0.2f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(gradient);

            ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ParticleMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------ Lichter

        public static Light PointLight(string name, Transform parent, Vector3 pos, Color color, float range,
            float intensity, bool flicker = false, bool shadows = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            if (flicker) go.AddComponent<LightFlicker>();
            return light;
        }

        // ------------------------------------------------------------------ SerializedObject-Helfer

        public static void SetRef(Object component, string field, Object value)
        {
            var so = new SerializedObject(component);
            SerializedProperty p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[Ashveil] Feld '{field}' nicht gefunden auf {component}."); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetFloat(Object component, string field, float value)
        {
            var so = new SerializedObject(component);
            SerializedProperty p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[Ashveil] Feld '{field}' nicht gefunden auf {component}."); return; }
            p.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetInt(Object component, string field, int value)
        {
            var so = new SerializedObject(component);
            SerializedProperty p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[Ashveil] Feld '{field}' nicht gefunden auf {component}."); return; }
            p.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetString(Object component, string field, string value)
        {
            var so = new SerializedObject(component);
            SerializedProperty p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[Ashveil] Feld '{field}' nicht gefunden auf {component}."); return; }
            p.stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetBool(Object component, string field, bool value)
        {
            var so = new SerializedObject(component);
            SerializedProperty p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[Ashveil] Feld '{field}' nicht gefunden auf {component}."); return; }
            p.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetVector3(Object component, string field, Vector3 value)
        {
            var so = new SerializedObject(component);
            SerializedProperty p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[Ashveil] Feld '{field}' nicht gefunden auf {component}."); return; }
            p.vector3Value = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
