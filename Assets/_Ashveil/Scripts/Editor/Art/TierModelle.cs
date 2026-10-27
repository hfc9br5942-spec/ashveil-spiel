using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Tiermodelle mit Haut und Skelett einsetzen (Herden der Westweiden; später alle Tiere).
    /// Zwei Stufen:
    /// 1. <b>Creature Art Pass (ART-120)</b> – Ordner <see cref="KreaturOrdner"/>/&lt;Art&gt;/&lt;Art&gt;_&lt;Typ&gt;.fbx mit Farb- und Normalen-Textur.
    ///    Pferd (14.10.2026): aus dem CC0-Scan „Horse Outside“ (Hunt Museum) mit ASHVEIL-Stilisierung, Haar-Massen, gemaltem Fell
    ///    (Tools/Blender/ashveil_build_pferd_scan.py). Typ A = Westweiden-Arbeitspferd (Herden), Typ B = Reitpferd.
    /// 2. <b>PLACEHOLDER / BLOCKOUT</b> (ART-120, Max 14.10.2026): <see cref="Ordner"/>/Tier_&lt;art&gt;.fbx – nur solange es für eine Art
    ///    noch kein Kreaturmodell gibt (Schaf, Rind). Nicht weiter als visuelle Grundlage skalieren.
    /// Die Knochen hängen an den vorhandenen Drehpunkten (Kopf am Hals, Beine an Schulter/Hüfte, Schwanz am Ansatz);
    /// <see cref="Ashveil.Gameplay.Npcs.AnimalVisual"/> bewegt sie, bei Skelett-Tieren auch die Gelenke darunter (Beugen im Schwung, Liegen).
    /// </summary>
    public static class TierModelle
    {
        public const string Ordner = ProjectSetup.Root + "/Art/Westweiden/Tiere/Blender";
        public const string KreaturOrdner = ProjectSetup.Root + "/Art/Tiere";

        /// <summary>Kreaturmodell je Art (Typ der Herde): Pfad ohne Endung.</summary>
        private static readonly Dictionary<string, string> Kreaturen = new Dictionary<string, string>
        {
            { "pferd", KreaturOrdner + "/Pferd/Pferd_A" },
        };

        public const string ClipOrdner = ProjectSetup.Root + "/Animations/Authored";

        /// <summary>Aufgenommene/übertragene Bewegung je Art (MOV-127): FBX mit Takes, Pfad ohne Endung.</summary>
        private static readonly Dictionary<string, string> KreaturClips = new Dictionary<string, string>
        {
            { "pferd", ClipOrdner + "/Pferd/ANM_Pferd_A" },
        };

        /// <summary>Takes je Zustand (Teil des Clipnamens): Stand, Schritt, Galopp, Grasen, Liegen.</summary>
        private static readonly string[] ClipRollen = { "1_Ilde", "Walk", "Gallop", "6_Eat", "Arise" };

        public sealed class Ergebnis
        {
            public bool ok;
            public bool kreatur;            // Creature-Art-Pass-Modell (Augen gemalt, Gelenke)
            public Animator animator;       // gesetzt, wenn die Art übertragene Bewegungsclips hat (MOV-127)
            public AnimationClip[] clips;   // Stand, Schritt, Galopp, Grasen, Liegen
            public Transform[] mitte = new Transform[4], unten = new Transform[4], fessel = new Transform[4];
        }

        private static string Pfad(string art, out bool kreatur)
        {
            kreatur = false;
            if (Kreaturen.TryGetValue(art, out string k) && AssetDatabase.LoadAssetAtPath<GameObject>(k + ".fbx") != null)
            {
                kreatur = true; return k;
            }
            return Ordner + "/Tier_" + art;
        }

        /// <summary>Gibt es ein Modell (Kreatur oder Blockout) für diese Art?</summary>
        public static bool Hat(string art) =>
            (art == "schaf" || art == "rind" || art == "pferd") && AssetDatabase.LoadAssetAtPath<GameObject>(Pfad(art, out _) + ".fbx") != null;

        private static readonly string[] BeinNamen = { "VL", "VR", "HL", "HR" };

        /// <summary>
        /// Modell unter <paramref name="model"/> einsetzen (Größe g) und die Knochen an die Drehpunkte hängen. Die Drehpunkte werden
        /// genau auf die Knochenansätze gesetzt.
        /// </summary>
        public static Ergebnis Bauen(string art, float g, Transform model, Transform head, Transform[] legs, Transform tail)
        {
            var e = new Ergebnis();
            string pfad = Pfad(art, out e.kreatur);
            var quelle = AssetDatabase.LoadAssetAtPath<GameObject>(pfad + ".fbx");
            if (quelle == null) return e;
            GameObject go = Object.Instantiate(quelle);
            go.name = e.kreatur ? "Modell" : "Modell (BLOCKOUT)";
            Transform m = go.transform;
            m.SetParent(model, false);
            m.localPosition = Vector3.zero;
            m.localRotation = Quaternion.identity;
            m.localScale = Vector3.one * g;
            var anim = go.GetComponent<Animator>();
            AnimationClip[] clips = e.kreatur ? Clips(art) : null;
            if (clips != null)
            {
                // MOV-127: Skelett bleibt in seiner Hierarchie, der Animator spielt die übertragenen Clips
                if (anim == null) anim = go.AddComponent<Animator>();
                anim.runtimeAnimatorController = Steuerung(art, clips);
                anim.avatar = null;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.CullCompletely;
                anim.updateMode = AnimatorUpdateMode.Normal;
                e.animator = anim; e.clips = clips;
            }
            else if (anim != null) Object.DestroyImmediate(anim);

            Transform hals = Suche(m, "Hals"), schwanz = Suche(m, "Schweif") ?? Suche(m, "Schwanz");
            var bein = new Transform[4];
            for (int i = 0; i < 4; i++) bein[i] = Suche(m, "Bein_" + BeinNamen[i] + "_O");
            if (hals == null || schwanz == null || System.Array.IndexOf(bein, null) >= 0)
            {
                Debug.LogError("[Ashveil] Tiermodell " + art + ": Knochen fehlen (Hals, Schweif/Schwanz, Bein_*_O)");
                Object.DestroyImmediate(go);
                return e;
            }
            Vector3 h = model.InverseTransformPoint(hals.position), vl = model.InverseTransformPoint(bein[0].position);
            if (h.z < 0f) m.localRotation = Quaternion.Euler(0f, 180f, 0f);
            vl = model.InverseTransformPoint(bein[0].position);
            if (vl.x > 0f) Debug.LogWarning("[Ashveil] Tiermodell " + art + ": links/rechts vertauscht (Bein_VL bei x > 0)");

            if (e.animator != null)
            {
                // Drehpunkte nur noch als Orte (Kopf, Beine, Schwanz) – die Knochen bleiben beim Animator
                head.position = hals.position; tail.position = schwanz.position;
                for (int i = 0; i < 4; i++) legs[i].position = bein[i].position;
            }
            else
            {
                Haengen(head, hals);
                Haengen(tail, schwanz);
            }
            for (int i = 0; i < 4; i++)
            {
                if (e.animator == null) Haengen(legs[i], bein[i]);
                e.mitte[i] = Suche(m, "Bein_" + BeinNamen[i] + "_M");
                e.unten[i] = Suche(m, "Bein_" + BeinNamen[i] + "_U");
                e.fessel[i] = Suche(m, "Bein_" + BeinNamen[i] + "_F");
            }

            var mat = Material(art, pfad, e.kreatur);
            foreach (SkinnedMeshRenderer r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.On;
                r.rayTracingMode = UnityEngine.Experimental.Rendering.RayTracingMode.Off;
                r.skinnedMotionVectors = false;
                r.updateWhenOffscreen = false;
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
                Bounds b = r.localBounds; b.Expand(0.35f); r.localBounds = b;
            }
            e.ok = true;
            return e;
        }

        /// <summary>Clips einer Art in der Reihenfolge <see cref="ClipRollen"/>; null, wenn es keine (vollständigen) gibt.</summary>
        public static AnimationClip[] Clips(string art)
        {
            if (!KreaturClips.TryGetValue(art, out string p)) return null;
            var alle = AssetDatabase.LoadAllAssetsAtPath(p + ".fbx").OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
            if (alle.Length == 0) return null;
            var r = new AnimationClip[ClipRollen.Length];
            for (int i = 0; i < r.Length; i++)
                r[i] = alle.FirstOrDefault(c => c.name.EndsWith("_" + ClipRollen[i]));
            if (r.Any(c => c == null))
            {
                Debug.LogWarning("[Ashveil] Tierclips " + art + " unvollständig: " + string.Join(", ", alle.Select(c => c.name)));
                return null;
            }
            return r;
        }

        /// <summary>
        /// Animator-Steuerung einer Art (Asset neben den Clips): Stand ⇄ Schritt ⇄ Galopp nach „Tempo“ (Abspieltempo „Takt“),
        /// Grasen und Liegen nach „Haltung“ (0 Fortbewegung/Stand, 1 Grasen, 2 Liegen). Liegen hält das erste Bild von „Aufstehen“.
        /// </summary>
        private static AnimatorController Steuerung(string art, AnimationClip[] c)
        {
            string pfad = KreaturClips[art] + ".controller";
            // Vorhandene Steuerung behalten (gleiche GUID → gebaute Szenen verlieren ihren Verweis nicht); neu nur, wenn sie fehlt
            // oder nicht mehr zu den Clips passt.
            var alt = AssetDatabase.LoadAssetAtPath<AnimatorController>(pfad);
            if (alt != null && alt.layers.Length > 0 && alt.animationClips.Length >= c.Length && c.All(x => alt.animationClips.Contains(x)))
                return alt;
            AssetDatabase.DeleteAsset(pfad);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(pfad);
            ac.AddParameter("Tempo", AnimatorControllerParameterType.Float);
            ac.AddParameter("Takt", AnimatorControllerParameterType.Float);
            ac.AddParameter("Haltung", AnimatorControllerParameterType.Int);
            ac.AddParameter("Null", AnimatorControllerParameterType.Float);
            var ps = ac.parameters;
            foreach (var q in ps) if (q.name == "Takt") q.defaultFloat = 1f;
            ac.parameters = ps;
            var sm = ac.layers[0].stateMachine;
            AnimatorState Zustand(string n, AnimationClip clip, Vector3 ort)
            {
                var s = sm.AddState(n, ort); s.motion = clip; s.writeDefaultValues = false; return s;
            }
            var stand = Zustand("Stand", c[0], new Vector3(250, 0));
            var schritt = Zustand("Schritt", c[1], new Vector3(250, 120)); schritt.speedParameterActive = true; schritt.speedParameter = "Takt";
            var galopp = Zustand("Galopp", c[2], new Vector3(250, 240)); galopp.speedParameterActive = true; galopp.speedParameter = "Takt";
            var grasen = Zustand("Grasen", c[3], new Vector3(520, 0));
            var liegen = Zustand("Liegen", c[4], new Vector3(520, 120)); liegen.timeParameterActive = true; liegen.timeParameter = "Null";
            sm.defaultState = stand;
            void Weg(AnimatorState von, AnimatorState nach, float dauer, params (AnimatorConditionMode m, float w, string p)[] b)
            {
                var t = von.AddTransition(nach); t.hasExitTime = false; t.duration = dauer; t.hasFixedDuration = true;
                foreach (var x in b) t.AddCondition(x.m, x.w, x.p);
            }
            Weg(stand, schritt, 0.25f, (AnimatorConditionMode.Greater, 0.1f, "Tempo"), (AnimatorConditionMode.Equals, 0, "Haltung"));
            Weg(schritt, stand, 0.3f, (AnimatorConditionMode.Less, 0.1f, "Tempo"));
            Weg(schritt, galopp, 0.25f, (AnimatorConditionMode.Greater, AnimalVisualGaloppAb, "Tempo"));
            Weg(galopp, schritt, 0.3f, (AnimatorConditionMode.Less, AnimalVisualGaloppAb - 0.2f, "Tempo"));
            foreach (var s in new[] { stand, schritt, galopp, liegen })
                Weg(s, grasen, 0.5f, (AnimatorConditionMode.Equals, 1, "Haltung"));
            foreach (var s in new[] { stand, schritt, galopp, grasen })
                Weg(s, liegen, 0.8f, (AnimatorConditionMode.Equals, 2, "Haltung"));
            Weg(grasen, stand, 0.5f, (AnimatorConditionMode.Equals, 0, "Haltung"));
            Weg(liegen, stand, 0.9f, (AnimatorConditionMode.Equals, 0, "Haltung"));
            EditorUtility.SetDirty(ac);
            AssetDatabase.SaveAssets();
            return ac;
        }

        private const float AnimalVisualGaloppAb = Ashveil.Gameplay.Npcs.AnimalVisual.GaloppAb;

        private static void Haengen(Transform drehpunkt, Transform knochen)
        {
            drehpunkt.position = knochen.position;
            knochen.SetParent(drehpunkt, true);
        }

        private static Transform Suche(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform f = Suche(t.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }

        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        /// <summary>URP-Lit mit gemalter Farbtextur (und Normalen beim Kreaturmodell); matt, Fell mit leichtem Glanz.</summary>
        private static Material Material(string art, string pfad, bool kreatur)
        {
            if (Cache.TryGetValue(pfad, out Material c) && c != null) return c;
            string mp = pfad + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, mp);
            }
            Texture2D farbe = AssetDatabase.LoadAssetAtPath<Texture2D>(pfad + "_Farbe.png")
                              ?? AssetDatabase.LoadAssetAtPath<Texture2D>(Ordner + "/Tier_" + art + "_Farbe.png");
            mat.SetTexture("_BaseMap", farbe);
            mat.SetTexture("_MainTex", farbe);
            mat.SetColor("_BaseColor", Color.white);
            var normal = kreatur ? AssetDatabase.LoadAssetAtPath<Texture2D>(pfad + "_Normal.png") : null;
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.SetFloat("_BumpScale", 1f);
                mat.EnableKeyword("_NORMALMAP");
            }
            mat.SetFloat("_Smoothness", art == "schaf" ? 0.04f : kreatur ? 0.22f : 0.14f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_EnvironmentReflections", 0f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            Cache[pfad] = mat;
            return mat;
        }
    }

    /// <summary>Import-Regeln für Tier-Modelle (Blockout-Ordner und Kreatur-Ordner): Meter, Achsen aus Blender, Skelett ohne
    /// Avatar und ohne Animation, keine Materialien; Farbtextur 1024 (sRGB), Normalen als Normal Map.</summary>
    public sealed class TierImportRegeln : AssetPostprocessor
    {
        private bool Hier
        {
            get
            {
                string p = assetPath.Replace('\\', '/');
                return p.Contains("/_Ashveil/Art/Westweiden/Tiere/Blender/") || p.Contains("/_Ashveil/Art/Tiere/");
            }
        }

        private void OnPreprocessModel()
        {
            if (!Hier) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.importAnimation = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            mi.optimizeGameObjects = false;
            mi.skinWeights = ModelImporterSkinWeights.Standard;
            mi.meshCompression = ModelImporterMeshCompression.Low;
            mi.isReadable = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.addCollider = false;
            mi.generateSecondaryUV = false;
        }

        private void OnPreprocessTexture()
        {
            if (!Hier) return;
            var ti = (TextureImporter)assetImporter;
            bool normal = assetPath.EndsWith("_Normal.png");
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !normal;
            ti.mipmapEnabled = true;
            ti.streamingMipmaps = true;
            ti.maxTextureSize = 1024;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = 2;
            ti.alphaSource = TextureImporterAlphaSource.None;
            ti.textureCompression = TextureImporterCompression.Compressed;
        }
    }

    /// <summary>Import-Regeln für übertragene Tier-Bewegungen (MOV-127, <c>Animations/Authored/Pferd</c>): Generic ohne Avatar, Takes
    /// als Clips (Name ohne „Skelett|“), Kreislauf-Clips als Schleife, keine Materialien.</summary>
    public sealed class TierClipImportRegeln : AssetPostprocessor
    {
        private bool Hier => assetPath.Replace('\\', '/').Contains("/_Ashveil/Animations/Authored/Pferd/");
        private static readonly string[] Schleifen = { "Ilde", "Walk", "Gallop", "Eat", "Swim", "Sleep" };

        private void OnPreprocessModel()
        {
            if (!Hier) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f; mi.useFileScale = true; mi.bakeAxisConversion = false;
            mi.importAnimation = true; mi.animationType = ModelImporterAnimationType.Generic; mi.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importCameras = false; mi.importLights = false; mi.importBlendShapes = false;
            mi.animationCompression = ModelImporterAnimationCompression.Optimal;
            mi.resampleCurves = true; mi.optimizeGameObjects = false;
        }

        private void OnPreprocessAnimation()
        {
            if (!Hier) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                int k = c.takeName.LastIndexOf('|');
                c.name = k >= 0 ? c.takeName.Substring(k + 1) : c.takeName;
                c.loopTime = Schleifen.Any(x => c.name.Contains(x));
                c.loopPose = false;
            }
            mi.clipAnimations = clips;
        }
    }
}
