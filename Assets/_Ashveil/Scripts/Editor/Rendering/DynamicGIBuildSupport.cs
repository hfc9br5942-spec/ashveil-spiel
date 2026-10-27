using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ashveil.EditorTools.Rendering
{
    /// <summary>
    /// Dynamisches GI im fertigen Spiel. URP entfernt beim Bauen alle "_SCREEN_SPACE_IRRADIANCE"-Varianten von Lit, TerrainLit
    /// und Gras, ausser das eigene Surface-Cache-GI von Unity ist im Renderer aktiv (ShaderBuildPreprocessor/
    /// ShaderScriptableStripper, URP 17.6). Ohne diese Varianten waere das GI im Build wirkungslos.
    ///
    /// Loesung ohne fremde Pakete und ohne Kopien der URP-Shader:
    /// 1. Das Skript-Symbol SURFACE_CACHE aktiviert Unitys (offiziellen, experimentellen) Surface-Cache-Code.
    /// 2. Der Renderer bekommt die Surface-Cache-Funktion, im Projekt aber AUS (sie laeuft nie, belegt nichts).
    /// 3. Nur waehrend eines Builds wird sie eingeschaltet, damit URP die Varianten behaelt; danach wieder aus.
    /// 4. Im fertigen Spiel schaltet <c>AshveilDynamicGI</c> sie beim Start sofort ab, bevor gerendert wird.
    /// Das Licht selbst kommt immer aus dem ASHVEIL-GI (Unitys Surface Cache vertraegt kein statisches Batching).
    /// </summary>
    [InitializeOnLoad]
    public static class DynamicGIBuildSupport
    {
        public const string Define = "SURFACE_CACHE";
        private const string FeatureTypeName = "UnityEngine.Rendering.Universal.SurfaceCacheGIRendererFeature, Unity.RenderPipelines.Universal.Runtime";
        private const string FeatureName = "Surface Cache GI (nur Build-Varianten fuer Ashveil-GI, bleibt aus)";

        static DynamicGIBuildSupport()
        {
            EditorApplication.delayCall += () =>
            {
                if (BuildPipeline.isBuildingPlayer) return;
                EnsureFeature();
                SetActiveForBuild(false);   // falls ein abgebrochener Build sie angelassen hat
            };
        }

        public static Type FeatureType => Type.GetType(FeatureTypeName);

        public static bool HasDefine()
        {
            string d = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
            return d.Split(';').Any(s => s.Trim() == Define);
        }

        [MenuItem("Ashveil/Rendering/Dynamisches GI: fuer Builds einrichten", priority = 426)]
        public static void Setup()
        {
            if (!HasDefine())
            {
                string d = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
                PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, string.IsNullOrEmpty(d) ? Define : d + ";" + Define);
                Debug.Log("[Ashveil] Dynamisches GI: Skript-Symbol " + Define + " gesetzt – nach dem Neukompilieren wird der Renderer ergaenzt.");
                return;
            }
            int n = EnsureFeature();
            Debug.Log($"[Ashveil] Dynamisches GI fuer Builds eingerichtet: {Describe()} ({n} Renderer ergaenzt).");
        }

        public static string Describe()
        {
            int total = 0, active = 0;
            foreach (ScriptableRendererFeature f in Features()) { total++; if (f.isActive) active++; }
            return $"Symbol {(HasDefine() ? "gesetzt" : "fehlt")}, Surface-Cache-Funktion in {total} Renderer(n), aktiv {active}";
        }

        /// <summary>Alle Renderer der verwendeten URP-Assets (Standard + Qualitaetsstufen).</summary>
        public static IEnumerable<ScriptableRendererData> RendererData()
        {
            var assets = new HashSet<UniversalRenderPipelineAsset>();
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset a) assets.Add(a);
            for (int i = 0; i < QualitySettings.count; i++)
                if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset q) assets.Add(q);
            FieldInfo list = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var seen = new HashSet<ScriptableRendererData>();
            foreach (UniversalRenderPipelineAsset asset in assets)
            {
                if (list?.GetValue(asset) is ScriptableRendererData[] data)
                    foreach (ScriptableRendererData d in data)
                        if (d != null && seen.Add(d)) yield return d;
            }
        }

        private static IEnumerable<ScriptableRendererFeature> Features()
        {
            Type t = FeatureType;
            if (t == null) yield break;
            foreach (ScriptableRendererData d in RendererData())
                foreach (ScriptableRendererFeature f in d.rendererFeatures)
                    if (f != null && t.IsInstanceOfType(f)) yield return f;
        }

        /// <summary>Surface-Cache-Funktion (aus) in jeden Renderer eintragen, falls sie fehlt. Rueckgabe: Anzahl ergaenzt.</summary>
        public static int EnsureFeature()
        {
            Type t = FeatureType;
            if (t == null || !HasDefine()) return 0;
            int added = 0;
            FieldInfo mapField = typeof(ScriptableRendererData).GetField("m_RendererFeatureMap", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            foreach (ScriptableRendererData data in RendererData().ToList())
            {
                if (data.rendererFeatures.Any(f => f != null && t.IsInstanceOfType(f))) continue;
                var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(t);
                feature.name = FeatureName;
                feature.SetActive(false);
                AssetDatabase.AddObjectToAsset(feature, data);
                data.rendererFeatures.Add(feature);
                if (mapField?.GetValue(data) is List<long> map &&
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId))
                    map.Add(localId);
                data.SetDirty();
                EditorUtility.SetDirty(data);
                added++;
            }
            if (added > 0) AssetDatabase.SaveAssets();
            return added;
        }

        public static void SetActiveForBuild(bool active)
        {
            bool changed = false;
            foreach (ScriptableRendererFeature f in Features().ToList())
            {
                if (f.isActive == active) continue;
                f.SetActive(active);
                EditorUtility.SetDirty(f);
                changed = true;
            }
            if (changed) AssetDatabase.SaveAssets();
        }
    }

    /// <summary>Schaltet die Surface-Cache-Funktion nur fuer die Dauer eines Builds ein (Varianten behalten).</summary>
    public sealed class DynamicGIBuildHook : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => -1000;   // vor URPs ShaderBuildPreprocessor (0) und dessen Shader-Vorfilter (-100)

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!DynamicGIBuildSupport.HasDefine() || DynamicGIBuildSupport.FeatureType == null)
            {
                Debug.LogWarning("[Ashveil] Dynamisches GI: Build ohne GI-Varianten – 'Ashveil/Rendering/Dynamisches GI: fuer Builds einrichten' ausfuehren.");
                return;
            }
            DynamicGIBuildSupport.EnsureFeature();
            DynamicGIBuildSupport.SetActiveForBuild(true);
            Debug.Log("[Ashveil] Dynamisches GI: Build behaelt die Screen-Space-Irradiance-Varianten (" + DynamicGIBuildSupport.Describe() + ").");
        }

        public void OnPostprocessBuild(BuildReport report) => DynamicGIBuildSupport.SetActiveForBuild(false);
    }
}
