using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashveil.Rendering.PathTracing
{
    /// <summary>
    /// Eigene Lichtliste des Pathtracers (§33): Hauptlicht (Sonne tagsueber, Mond nachts – der WorldSkyDirector bleibt
    /// Autoritaet fuer Richtung und Staerke) als Scheibe mit Winkelgroesse, lokale Punkt-/Spotlichter als Kugeln
    /// (bis zu 64, nach Beitrag an der Kamera sortiert) und die Tabelle der leuchtenden Dreiecke.
    /// Einheiten (§163): URP rechnet Albedo × Licht × cos, der Pathtracer Albedo/π × L × cos → Lichtwerte × π.
    /// </summary>
    public sealed class AshveilRTLightRegistry : System.IDisposable
    {
        public const int MaxLocalLights = 64;
        private const int LightStride = 64;   // 4 × float4

        private GraphicsBuffer _lights, _emissive, _emissiveAlias;
        private readonly Vector4[] _lightData = new Vector4[MaxLocalLights * 4];
        private int _lightHash;

        public GraphicsBuffer Lights => _lights;
        public GraphicsBuffer Emissive => _emissive;
        public GraphicsBuffer EmissiveAlias => _emissiveAlias;
        public int LocalCount { get; private set; }
        public int EmissiveCount { get; private set; }
        public float EmissivePower { get; private set; }
        public Vector4 EmissiveCenter { get; private set; }
        public int Version { get; private set; }

        /// <summary>Hauptlicht (Richtung zum Licht, Strahldichte der Scheibe, cos Winkelradius, trefferbar?).</summary>
        public Vector3 SunDirection { get; private set; } = Vector3.up;
        public Color SunRadiance { get; private set; }
        public float SunCosAngle { get; private set; } = 1f;
        public bool SunVisible { get; private set; }
        public bool MainIsMoon { get; private set; }
        public float SunIrradianceLuminance { get; private set; }
        public Light MainLight { get; private set; }
        /// <summary>Hauptlicht in URP-Einheit (Farbe × Staerke, linear) – so, wie der Rasterrenderer es anwendet.</summary>
        public Color MainColor { get; private set; }


        /// <summary>
        /// Lichtliste zwischenspeichern (dynamisches GI fragt oft): neu suchen nur nach Szenenwechsel oder alle ~5 s.
        /// Der Pathtracer sucht weiter jedes Mal (Momentaufnahme).
        /// </summary>
        public bool CacheLights;
        private Light[] _cached;
        private float _cachedAt = -100f;
        private bool _cacheDirty = true;

        private Light[] AllLights()
        {
            if (!CacheLights) return Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            if (_cached == null || _cacheDirty || Time.unscaledTime - _cachedAt > 5f)
            {
                _cached = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
                _cachedAt = Time.unscaledTime;
                _cacheDirty = false;
            }
            return _cached;
        }

        private void OnSceneChanged(UnityEngine.SceneManagement.Scene a, UnityEngine.SceneManagement.LoadSceneMode b) => _cacheDirty = true;
        private void OnSceneUnloaded(UnityEngine.SceneManagement.Scene a) => _cacheDirty = true;

        public AshveilRTLightRegistry()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneChanged;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += OnSceneUnloaded;
            _lights = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxLocalLights, LightStride);
            _emissive = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 64);
            _emissiveAlias = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 16);
        }

        /// <summary>Lichter aus der Szene lesen. Rueckgabe: true bei Aenderung (→ Akkumulation neu).</summary>
        public bool Collect(Vector3 cameraPos, Vector3 origin, PathTraceSettings s)
        {
            int hash = 17;
            // --- Hauptlicht
            Light main = RenderSettings.sun;
            if (main == null || !main.isActiveAndEnabled)
            {
                main = null;
                float best = 0f;
                foreach (Light l in AllLights())
                    if (l != null && l.type == LightType.Directional && l.isActiveAndEnabled && l.intensity > best) { best = l.intensity; main = l; }
            }
            MainLight = main;
            if (main != null && main.isActiveAndEnabled && main.intensity > 0f)
            {
                Vector3 toLight = -main.transform.forward;
                // Der DayNightCycle setzt nachts dasselbe Licht auf den Mond (SkyDirector bleibt Autoritaet fuer Richtung/Phase)
                MainIsMoon = Sky.SkyDirector.Active != null && Sky.SkyDirector.HasMoonLight &&
                             Vector3.Dot(Sky.SkyDirector.MoonLightDirection.normalized, toLight) > 0.999f;
                float radius = MainIsMoon ? s.moonAngularRadius : s.sunAngularRadius;
                float sin = Mathf.Sin(Mathf.Max(radius, 1e-4f));
                Color c = LightColor(main);
                MainColor = c;
                // Bestrahlungsstaerke E = π·I; Scheibe mit Radiant L: E = L·π·sin²θ → L = I / sin²θ
                SunRadiance = c / (sin * sin);
                SunDirection = toLight;
                SunCosAngle = Mathf.Cos(radius);
                SunVisible = !MainIsMoon;
                SunIrradianceLuminance = Mathf.PI * Lum(c);
                hash = Combine(hash, toLight.GetHashCode());
                hash = Combine(hash, c.GetHashCode());
                hash = Combine(hash, radius.GetHashCode());
            }
            else
            {
                SunRadiance = Color.black;
                MainColor = Color.black;
                SunIrradianceLuminance = 0f;
                SunVisible = false;
            }

            // --- lokale Lichter
            var candidates = new List<(Light l, float score)>();
            foreach (Light l in AllLights())
            {
                if (l == null || !l.isActiveAndEnabled || l.intensity <= 0f) continue;
                if (l.type != LightType.Point && l.type != LightType.Spot) continue;
                if (l.bakingOutput.lightmapBakeType == LightmapBakeType.Baked) continue;
                var tag = l.GetComponent<PathTraceLight>();
                if (tag != null && tag.exclude) continue;
                float d2 = (l.transform.position - cameraPos).sqrMagnitude;
                float score = l.intensity * Lum(l.color.linear) / (d2 + 1f);
                candidates.Add((l, score));
            }
            candidates.Sort((a, b) => b.score.CompareTo(a.score));
            LocalCount = Mathf.Min(candidates.Count, MaxLocalLights);
            for (int i = 0; i < LocalCount; i++)
            {
                Light l = candidates[i].l;
                var tag = l.GetComponent<PathTraceLight>();
                float radius = tag != null ? tag.radius : (l.GetComponent("LightFlicker") != null ? 0.2f : 0.05f);
                Vector3 p = l.transform.position - origin;
                Color c = LightColor(l) * Mathf.PI;
                Vector3 fwd = l.transform.forward;
                float cosOuter = Mathf.Cos(0.5f * l.spotAngle * Mathf.Deg2Rad);
                float cosInner = Mathf.Cos(0.5f * Mathf.Max(l.innerSpotAngle, 0f) * Mathf.Deg2Rad);
                if (cosInner <= cosOuter) cosInner = Mathf.Min(1f, cosOuter + 1e-3f);
                _lightData[i * 4 + 0] = new Vector4(p.x, p.y, p.z, radius);
                _lightData[i * 4 + 1] = new Vector4(c.r, c.g, c.b, l.type == LightType.Spot ? 1f : 0f);
                _lightData[i * 4 + 2] = new Vector4(fwd.x, fwd.y, fwd.z, cosOuter);
                _lightData[i * 4 + 3] = new Vector4(cosInner, tag != null && tag.visibleToCamera ? 1f : 0f, 0f, 0f);
                hash = Combine(hash, _lightData[i * 4].GetHashCode());
                hash = Combine(hash, _lightData[i * 4 + 1].GetHashCode());
                hash = Combine(hash, _lightData[i * 4 + 2].GetHashCode());
            }
            hash = Combine(hash, LocalCount);
            _lights.SetData(_lightData, 0, 0, Mathf.Max(LocalCount, 1) * 4);

            bool changed = hash != _lightHash;
            _lightHash = hash;
            if (changed) Version++;
            return changed;
        }

        /// <summary>Tabelle der leuchtenden Dreiecke (Alias nach Flaeche × Leuchtdichte, §40).</summary>
        public void SetEmissive(IReadOnlyList<AshveilRTSceneManager.AshPTEmissiveTriCpu> tris)
        {
            EmissiveCount = tris.Count;
            int n = Mathf.Max(1, tris.Count);
            if (_emissive.count < n)
            {
                _emissive.Release();
                _emissiveAlias.Release();
                _emissive = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(n), 64);
                _emissiveAlias = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(n), 16);
            }
            var weights = new float[tris.Count];
            Vector3 center = Vector3.zero;
            float wsum = 0f;
            for (int i = 0; i < tris.Count; i++)
            {
                weights[i] = tris[i].Weight;
                Vector3 c = (tris[i].P0 + tris[i].P1 + tris[i].P2) / 3f;
                center += c * tris[i].Weight;
                wsum += tris[i].Weight;
            }
            Vector4[] alias = AliasTable.Build(weights, out float total);
            var data = new Vector4[n * 4];
            float radius = 0f;
            center = wsum > 0 ? center / wsum : Vector3.zero;
            for (int i = 0; i < tris.Count; i++)
            {
                var t = tris[i];
                data[i * 4 + 0] = new Vector4(t.P0.x, t.P0.y, t.P0.z, t.Area);
                data[i * 4 + 1] = new Vector4(t.P1.x, t.P1.y, t.P1.z, alias[i].z);
                data[i * 4 + 2] = new Vector4(t.P2.x, t.P2.y, t.P2.z, AliasTable.AsFloat(t.InstanceId));
                data[i * 4 + 3] = new Vector4(AliasTable.AsFloat(t.Primitive), t.Luminance, 0f, 0f);
                radius = Mathf.Max(radius, (t.P0 - center).magnitude);
            }
            _emissive.SetData(data);
            _emissiveAlias.SetData(alias);
            // Gesamtleistung ≈ π · Σ Flaeche × Leuchtdichte
            EmissivePower = Mathf.PI * total;
            EmissiveCenter = new Vector4(center.x, center.y, center.z, radius);
            Version++;
        }

        private static Color LightColor(Light l)
        {
            Color c = l.color.linear * l.intensity;
            if (l.useColorTemperature) c *= Mathf.CorrelatedColorTemperatureToRGB(l.colorTemperature);
            return c;
        }

        private static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        private static int Combine(int h, int v) { unchecked { return h * 31 + v; } }

        public void Dispose()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneChanged;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= OnSceneUnloaded;
            _lights?.Release();
            _emissive?.Release();
            _emissiveAlias?.Release();
        }
    }
}
