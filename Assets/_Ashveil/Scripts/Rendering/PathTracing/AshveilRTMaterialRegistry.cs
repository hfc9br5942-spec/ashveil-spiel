using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Rendering.PathTracing
{
    /// <summary>
    /// Uebersetzt vorhandene Materialien (URP Lit/Simple Lit/Unlit, Ashveil/Foliage, alte Standard-Materialien) in
    /// Pathtracing-Stellvertreter mit dem Shader Hidden/Ashveil/PTSurface. Kein doppeltes Authoring: Texturen und
    /// Werte werden uebernommen; Unity legt je Material einen eigenen Trefferdatensatz an, dadurch braucht es weder
    /// Bindless-Texturen noch Shader-Varianten (Master-Plan §17–20).
    /// </summary>
    public sealed class AshveilRTMaterialRegistry
    {
        public struct Entry
        {
            public Material Proxy;
            public bool AlphaTest;
            public bool Transparent;
            public bool DoubleSided;
            public bool Dielectric;
            public bool Vegetation;
            public bool Unlit;
            public Color EmissionLinear;   // Materialfarbe × Staerke (ohne Textur)
            public bool HasEmissionMap;
            public bool Emissive => EmissionLuminance > EmissionThreshold;
            public float EmissionLuminance => 0.2126f * EmissionLinear.r + 0.7152f * EmissionLinear.g + 0.0722f * EmissionLinear.b;
        }

        /// <summary>Nur Flaechen darueber kommen in die Tabelle der leuchtenden Dreiecke (§41).</summary>
        public const float EmissionThreshold = 0.05f;

        private readonly Shader _shader;
        private readonly Dictionary<(Material, int), Entry> _cache = new Dictionary<(Material, int), Entry>();
        private readonly Dictionary<(Material, int), int> _hashes = new Dictionary<(Material, int), int>();

        public int Version { get; private set; }
        public int Count => _cache.Count;
        public int Skipped { get; private set; }

        public AshveilRTMaterialRegistry(Shader surfaceShader) => _shader = surfaceShader;

        public static bool IsTraceable(Material m)
        {
            if (m == null || m.shader == null) return false;
            string n = m.shader.name;
            if (n.StartsWith("Hidden/") || n.StartsWith("Sprites/") || n.StartsWith("UI/") || n.Contains("Particles") ||
                n.Contains("TextMeshPro") || n.Contains("Skybox") || n == "Ashveil/DynamicSky" || n.Contains("Terrain")) return false;
            return true;
        }

        public bool TryGet(Material src, PathTraceSurface authoring, out Entry e)
        {
            e = default;
            if (!IsTraceable(src)) { Skipped++; return false; }
            int ah = authoring != null ? authoring.Hash() : 0;
            var key = (src, ah);
            int h = SourceHash(src) ^ ah;
            if (_cache.TryGetValue(key, out e) && _hashes[key] == h) return true;
            if (e.Proxy == null) e.Proxy = new Material(_shader) { name = src.name + " (PT)", hideFlags = HideFlags.DontSave };
            Fill(src, authoring, ref e);
            _cache[key] = e;
            _hashes[key] = h;
            Version++;
            return true;
        }

        /// <summary>Alle Stellvertreter gegen ihre Quellen pruefen (Materialaenderung → Version++).</summary>
        public void Refresh()
        {
            var keys = new List<(Material, int)>(_cache.Keys);
            foreach (var key in keys)
            {
                if (key.Item1 == null) { Object.Destroy(_cache[key].Proxy); _cache.Remove(key); _hashes.Remove(key); Version++; continue; }
                int h = SourceHash(key.Item1) ^ key.Item2;
                if (_hashes[key] != h)
                {
                    var e = _cache[key];
                    Fill(key.Item1, null, ref e);
                    _cache[key] = e;
                    _hashes[key] = h;
                    Version++;
                }
            }
        }

        /// <summary>
        /// Nur Stellvertreter behalten, die noch benutzt werden (Zellen-Streaming): die Quellmaterialien entladener Zellen
        /// und ihre Texturen bleiben sonst ueber die Verweise im Speicher.
        /// </summary>
        public int RetainOnlyProxies(HashSet<Material> used)
        {
            var drop = new List<(Material, int)>();
            foreach (var kv in _cache)
                if (kv.Key.Item1 == null || kv.Value.Proxy == null || !used.Contains(kv.Value.Proxy)) drop.Add(kv.Key);
            foreach (var key in drop)
            {
                if (_cache[key].Proxy != null) Object.Destroy(_cache[key].Proxy);
                _cache.Remove(key);
                _hashes.Remove(key);
            }
            if (drop.Count > 0) Version++;
            return drop.Count;
        }

        public void Dispose()
        {
            foreach (var e in _cache.Values) if (e.Proxy != null) Object.Destroy(e.Proxy);
            _cache.Clear();
            _hashes.Clear();
        }

        private static int SourceHash(Material m)
        {
            unchecked
            {
                int h = m.shader.GetHashCode();
                h = h * 31 + m.renderQueue;
                foreach (string p in HashProps)
                    if (m.HasProperty(p))
                    {
                        int id = Shader.PropertyToID(p);
                        var t = m.shader.FindPropertyIndex(p) >= 0 ? m.shader.GetPropertyType(m.shader.FindPropertyIndex(p)) : UnityEngine.Rendering.ShaderPropertyType.Float;
                        if (t == UnityEngine.Rendering.ShaderPropertyType.Texture) { var tex = m.GetTexture(id); h = h * 31 + (tex != null ? tex.GetHashCode() : 0); }
                        else if (t == UnityEngine.Rendering.ShaderPropertyType.Color || t == UnityEngine.Rendering.ShaderPropertyType.Vector) h = h * 31 + m.GetVector(id).GetHashCode();
                        else h = h * 31 + m.GetFloat(id).GetHashCode();
                    }
                h = h * 31 + (m.IsKeywordEnabled("_EMISSION") ? 1 : 0);
                h = h * 31 + (m.IsKeywordEnabled("_ALPHATEST_ON") ? 1 : 0);
                return h;
            }
        }

        private static readonly string[] HashProps =
        {
            "_BaseMap", "_MainTex", "_BaseColor", "_Color", "_BumpMap", "_BumpScale", "_MetallicGlossMap", "_Metallic", "_Smoothness", "_Glossiness",
            "_EmissionMap", "_EmissionColor", "_Cutoff", "_AlphaClip", "_Surface", "_Cull", "_SmoothnessTextureChannel", "_Translucency"
        };

        private void Fill(Material src, PathTraceSurface authoring, ref Entry e)
        {
            Material p = e.Proxy;
            string sn = src.shader.name;
            bool unlit = sn.Contains("Unlit");
            bool foliage = sn == "Ashveil/Foliage";
            bool standard = sn == "Standard" || sn == "Standard (Specular setup)";

            Texture baseMap = Tex(src, "_BaseMap") ?? Tex(src, "_MainTex");
            Color baseColor = Col(src, "_BaseColor", Col(src, "_Color", Color.white));
            Vector2 scale = src.HasProperty("_BaseMap") ? src.GetTextureScale("_BaseMap") : src.HasProperty("_MainTex") ? src.GetTextureScale("_MainTex") : Vector2.one;
            Vector2 offset = src.HasProperty("_BaseMap") ? src.GetTextureOffset("_BaseMap") : src.HasProperty("_MainTex") ? src.GetTextureOffset("_MainTex") : Vector2.zero;
            p.SetTexture("_BaseMap", baseMap != null ? baseMap : Texture2D.whiteTexture);
            p.SetTextureScale("_BaseMap", scale);
            p.SetTextureOffset("_BaseMap", offset);
            p.SetColor("_BaseColor", baseColor);
            p.SetVector("_AshTexSize", baseMap != null ? new Vector4(baseMap.width, baseMap.height, 0, 0) : new Vector4(1, 1, 0, 0));

            Texture normal = Tex(src, "_BumpMap");
            p.SetTexture("_BumpMap", normal != null ? normal : Texture2D.normalTexture);
            p.SetFloat("_AshHasNormalMap", normal != null ? 1 : 0);
            p.SetFloat("_BumpScale", Flt(src, "_BumpScale", 1f));

            Texture metal = Tex(src, "_MetallicGlossMap");
            p.SetTexture("_MetallicGlossMap", metal != null ? metal : Texture2D.whiteTexture);
            p.SetFloat("_AshHasMetalMap", metal != null ? 1 : 0);
            p.SetFloat("_Metallic", Flt(src, "_Metallic", 0f));
            float smooth = standard ? Flt(src, "_Glossiness", 0.5f) : Flt(src, "_Smoothness", foliage ? 0.15f : 0.5f);
            if (metal != null && standard) smooth = Flt(src, "_GlossMapScale", 1f);
            p.SetFloat("_Smoothness", smooth);
            p.SetFloat("_AshSmoothSource", Flt(src, "_SmoothnessTextureChannel", 0f) > 0.5f ? 1 : 0);

            bool emissionOn = src.IsKeywordEnabled("_EMISSION") || (src.HasProperty("_EmissionColor") && !src.shader.name.StartsWith("Universal Render Pipeline/"));
            Color emission = emissionOn ? Col(src, "_EmissionColor", Color.black) : Color.black;
            Texture emissionMap = emissionOn ? Tex(src, "_EmissionMap") : null;
            p.SetColor("_EmissionColor", emission);
            p.SetTexture("_EmissionMap", emissionMap != null ? emissionMap : Texture2D.whiteTexture);
            p.SetFloat("_AshHasEmissionMap", emissionMap != null ? 1 : 0);

            bool transparent = Flt(src, "_Surface", 0f) > 0.5f || src.renderQueue >= 2900 && !foliage;
            bool alphaTest = Flt(src, "_AlphaClip", 0f) > 0.5f || src.IsKeywordEnabled("_ALPHATEST_ON") || src.renderQueue == 2450;
            bool doubleSided = foliage || (src.HasProperty("_Cull") && Flt(src, "_Cull", 2f) < 0.5f);
            p.SetFloat("_Cutoff", Flt(src, "_Cutoff", 0.5f));
            p.SetFloat("_AshAlphaClip", alphaTest ? 1 : 0);
            p.SetFloat("_AshTransparent", transparent ? 1 : 0);

            uint flags = 0;
            float transmission = 0f, ior = 1.5f;
            Vector3 absorption = Vector3.zero;
            bool dielectric = false;
            if (doubleSided) flags |= 0x04;
            if (unlit) flags |= 0x10;
            if (foliage)
            {
                flags |= 0x20;
                transmission = Mathf.Clamp01(Flt(src, "_Translucency", 0.35f));
            }
            if (authoring != null)
            {
                switch (authoring.kind)
                {
                    case PathTraceSurface.Kind.Glas:
                        dielectric = true; flags |= 0x08; ior = authoring.ior; absorption = authoring.Absorption; transparent = false; doubleSided = true; flags |= 0x04; break;
                    case PathTraceSurface.Kind.DuennDurchlaessig:
                        flags |= 0x20 | 0x04; transmission = authoring.transmission; doubleSided = true; break;
                }
            }
            p.SetFloat("_AshSurfFlags", flags);
            p.SetFloat("_AshIor", ior);
            p.SetFloat("_AshTransmission", transmission);
            p.SetVector("_AshAbsorption", absorption);

            e.AlphaTest = alphaTest;
            e.Transparent = transparent;
            e.DoubleSided = doubleSided;
            e.Dielectric = dielectric;
            e.Vegetation = foliage;
            e.Unlit = unlit;
            e.EmissionLinear = unlit ? Color.black : emission.linear;
            e.HasEmissionMap = emissionMap != null;
        }

        private static Texture Tex(Material m, string p) => m.HasProperty(p) ? m.GetTexture(p) : null;
        private static Color Col(Material m, string p, Color d) => m.HasProperty(p) ? m.GetColor(p) : d;
        private static float Flt(Material m, string p, float d) => m.HasProperty(p) ? m.GetFloat(p) : d;
    }
}
