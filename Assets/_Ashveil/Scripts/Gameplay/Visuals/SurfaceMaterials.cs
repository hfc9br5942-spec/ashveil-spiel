using System.Collections.Generic;
using Ashveil.Data;
using UnityEngine;

namespace Ashveil.Gameplay.Visuals
{
    /// <summary>
    /// Geteilte Materialien je (Oberflaeche, Farbe) – nie pro Frame neu, nie pro Figur doppelt. Gefaerbte
    /// Flaechen behalten ihre Eigenschaften (Metall bleibt metallisch, Stoff matt).
    /// </summary>
    public static class SurfaceMaterials
    {
        private static readonly Dictionary<long, Material> Cache = new Dictionary<long, Material>();
        private static readonly Dictionary<(Material, Texture, Color32), Material> SkinCache = new Dictionary<(Material, Texture, Color32), Material>();
        private static readonly Dictionary<(Material, Texture, int), Material> OverlayCache = new Dictionary<(Material, Texture, int), Material>();
        private static readonly Dictionary<(Material, Color32), Material> TintCache = new Dictionary<(Material, Color32), Material>();
        private static Material _template;

        private static Material Template
        {
            get
            {
                if (_template != null) return _template;
                AppearanceCatalog cat = GameDataBootstrap.Catalog;
                if (cat != null && cat.litTemplate != null) _template = cat.litTemplate;
                else
                {
                    Shader s = Shader.Find("Universal Render Pipeline/Lit");
                    _template = new Material(s != null ? s : Shader.Find("Standard")) { hideFlags = HideFlags.DontSave };
                }
                return _template;
            }
        }

        private static readonly Dictionary<(Material, Color32), Material> ModellCache = new Dictionary<(Material, Color32), Material>();

        /// <summary>
        /// 22.10.2026: modelliertes Anbauteil mit eigener Textur (Tripo/Artlist: Nieten, Riemen, Schnallen) – dieselbe Schattierung wie
        /// die übrige Rüstung (Vorlage, Metall/Glanz der Oberfläche), aber die Textur des Modells, leicht in der Gegenstandsfarbe getönt.
        /// Das FBX-Material selbst kam mit Metallic 1 an und war ohne Umgebungsreflexion fast schwarz.
        /// </summary>
        public static Material Modell(Material original, SurfaceKind kind, Color color)
        {
            Color32 c = color;
            if (ModellCache.TryGetValue((original, c), out Material m) && m != null) return m;
            Material basis = Get(kind, color);
            m = new Material(basis) { name = $"Modell {original.name} #{ColorUtility.ToHtmlStringRGB(color)}", hideFlags = HideFlags.DontSave };
            Texture alb = original.mainTexture;
            if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", alb); m.SetTextureScale("_BaseMap", Vector2.one); }
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", alb);
            Texture nor = original.HasProperty("_BumpMap") ? original.GetTexture("_BumpMap") : null;
            if (m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", nor); if (nor != null) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP"); }
            // die Modelltexturen sind dunkel gebacken (Stahl, Leder): etwas aufhellen, Gegenstandsfarbe nur als leichter Ton
            Color ton = Color.Lerp(Color.white, color, 0.35f) * 1.35f; ton.a = 1f;
            Set(m, "_BaseColor", ton);
            Set(m, "_Color", ton);
            if (kind == SurfaceKind.Metal) { SetF(m, "_Metallic", 0.6f); SetF(m, "_Smoothness", 0.45f); SetF(m, "_Glossiness", 0.45f); }
            ModellCache[(original, c)] = m;
            return m;
        }

        public static Material Get(SurfaceKind kind, Color color)
        {
            Color32 c = color;
            long key = ((long)kind << 32) | ((long)c.r << 24) | ((long)c.g << 16) | ((long)c.b << 8) | c.a;
            if (Cache.TryGetValue(key, out Material m) && m != null) return m;

            m = new Material(Template) { name = $"Equip {kind} #{ColorUtility.ToHtmlStringRGB(color)}", hideFlags = HideFlags.DontSave };
            float metallic = 0f, smooth = 0.2f;
            switch (kind)
            {
                case SurfaceKind.Cloth: smooth = 0.12f; break;
                case SurfaceKind.Leather: smooth = 0.36f; break;
                case SurfaceKind.Metal: metallic = 0.85f; smooth = 0.55f; break;
                case SurfaceKind.Mail: metallic = 0.75f; smooth = 0.32f; break;
                case SurfaceKind.Wood: smooth = 0.22f; break;
                case SurfaceKind.Fur: smooth = 0.04f; break;
                case SurfaceKind.Bone: smooth = 0.3f; break;
                case SurfaceKind.Glow: smooth = 0.6f; break;
            }
            Set(m, "_BaseColor", color);
            Set(m, "_Color", color);
            SetF(m, "_Metallic", metallic);
            SetF(m, "_Smoothness", smooth);
            SetF(m, "_Glossiness", smooth);
            SetF(m, "_Cull", 0f); // beidseitig: offene Kanten von Schalen/Platten sehen innen nicht leer aus
            // 22.10.2026 (Max: Rüstung wie in WoW anliegend, aber Soulslike): Graustufen-Textur der Oberfläche (Schienen/Nieten,
            // Kettenringe, Leder, Gewebe …) mal Gegenstandsfarbe, dazu Normalmap – Erzeugung: Auftrag „ruestungs_texturen“.
            Texture2D alb = Resources.Load<Texture2D>("Ruestung/T_Ruestung_" + kind), nor = Resources.Load<Texture2D>("Ruestung/T_Ruestung_" + kind + "_N");
            if (alb != null) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", alb); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", alb); }
            if (nor != null && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", nor); m.EnableKeyword("_NORMALMAP"); SetF(m, "_BumpScale", 1f); }
            if (kind == SurfaceKind.Glow)
            {
                m.EnableKeyword("_EMISSION");
                Set(m, "_EmissionColor", color * 2.6f);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            Cache[key] = m;
            return m;
        }

        private static readonly Dictionary<long, Material> BandCache = new Dictionary<long, Material>();

        /// <summary>
        /// 22.10.2026: Band (Saum, Manschette, Steppnaht, Leiste) – Eigenschaften der Oberfläche, aber mit der Bandmaske
        /// T_Ruestung_Band (Alpha-Schnitt): saubere Kante unabhängig von der Dreiecksgröße des Körpers.
        /// </summary>
        public static Material Band(SurfaceKind kind, Color color)
        {
            Color32 c = color;
            long key = ((long)kind << 32) | ((long)c.r << 24) | ((long)c.g << 16) | ((long)c.b << 8) | c.a;
            if (BandCache.TryGetValue(key, out Material m) && m != null) return m;
            m = new Material(Get(kind, color)) { name = $"Equip Band {kind} #{ColorUtility.ToHtmlStringRGB(color)}", hideFlags = HideFlags.DontSave };
            Texture2D maske = Resources.Load<Texture2D>("Ruestung/T_Ruestung_Band");
            if (maske != null)
            {
                if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", maske); m.SetTextureScale("_BaseMap", Vector2.one); }
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", maske);
                SetF(m, "_AlphaClip", 1f);
                SetF(m, "_Cutoff", 0.5f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }
            m.DisableKeyword("_NORMALMAP");
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            BandCache[key] = m;
            return m;
        }

        /// <summary>Hautton/Augenfarbe: gleiches Material mit anderer Farbtextur (optional eingefaerbt).</summary>
        public static Material Skin(Material original, Texture tex) => Skin(original, tex, Color.white);

        public static Material Skin(Material original, Texture tex, Color tint)
        {
            if (original == null || tex == null) return original;
            Color32 c = tint;
            if (SkinCache.TryGetValue((original, tex, c), out Material m) && m != null) return m;
            m = new Material(original) { name = original.name + " " + tex.name, hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (tint != Color.white)
            {
                Color baseCol = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
                Set(m, "_BaseColor", baseCol * tint);
                Set(m, "_Color", baseCol * tint);
            }
            SkinCache[(original, tex, c)] = m;
            return m;
        }

        /// <summary>Gesichtsebene (durchsichtig) – Reihenfolge ueber die Warteschlange (0 = Gesicht, 1 = Bart, 2 = Merkmal).</summary>
        public static Material Overlay(Material template, Texture tex, int order)
        {
            if (template == null || tex == null) return null;
            if (OverlayCache.TryGetValue((template, tex, order), out Material m) && m != null) return m;
            m = new Material(template) { name = "Face Overlay " + tex.name, hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            m.renderQueue = template.renderQueue + order;
            OverlayCache[(template, tex, order)] = m;
            return m;
        }

        /// <summary>Haarfarbe: Grundmaterial eingefaerbt (die Haartexturen sind neutral grau).</summary>
        public static Material Tint(Material original, Color color)
        {
            if (original == null) return null;
            Color32 c = color;
            if (TintCache.TryGetValue((original, c), out Material m) && m != null) return m;
            m = new Material(original) { name = original.name + " #" + ColorUtility.ToHtmlStringRGB(color), hideFlags = HideFlags.DontSave };
            Set(m, "_BaseColor", color);
            Set(m, "_Color", color);
            TintCache[(original, c)] = m;
            return m;
        }

        private static void Set(Material m, string p, Color c) { if (m.HasProperty(p)) m.SetColor(p, c); }
        private static void SetF(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }
    }
}
