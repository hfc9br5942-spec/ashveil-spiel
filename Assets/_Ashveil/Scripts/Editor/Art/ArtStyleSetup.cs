using Ashveil.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Stimmung nach Art-Bible "Asche & Glut": URP-Qualitaet, Nachbearbeitung, Himmel, Sonne, Umgebungslicht,
    /// eigener Hoehennebel, Kamera und Ascheflocken. Wird beim Szenenbau aufgerufen.
    /// </summary>
    public static class ArtStyleSetup
    {
        private const string Folder = ProjectSetup.Root + "/Art/Rendering";
        private const string PipelineAssetPath = "Assets/Settings/PC_RPAsset.asset";

        // ================================================================== URP

        public static void ApplyPipelineSettings()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (asset == null)
            {
                Debug.LogWarning("[Ashveil] URP-Asset nicht gefunden: " + PipelineAssetPath);
                return;
            }
            var so = new SerializedObject(asset);
            Set(so, "m_ShadowDistance", 150f);
            SetInt(so, "m_MainLightShadowmapResolution", 4096);
            SetInt(so, "m_ShadowCascadeCount", 4);
            SetInt(so, "m_AdditionalLightsShadowmapResolution", 4096); // Lagerfeuer und Fackeln werfen Schatten
            SetInt(so, "m_MSAA", 4);
            SetBool(so, "m_SupportsHDR", true);
            SetBool(so, "m_RequireDepthTexture", true);
            SetBool(so, "m_SoftShadowsSupported", true);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void Set(SerializedObject so, string name, float v)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.floatValue = v;
        }

        private static void SetInt(SerializedObject so, string name, int v)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.intValue = v;
        }

        private static void SetBool(SerializedObject so, string name, bool v)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.boolValue = v;
        }

        // ================================================================== Licht und Himmel

        public static void SetupLighting(Light sun)
        {
            ProjectSetup.EnsureFolder(Folder);
            sun.color = new Color(1f, 0.78f, 0.55f);
            sun.intensity = 2.1f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.92f;
            // Mit dem Riesen kommt die Abendsonne aus Westsuedwest: sein Schatten faellt nach Osten, das Lager bleibt hell,
            // der Kratersee und die Bruchkante der linken Schulter stehen im Licht.
            sun.transform.rotation = GiantBuilder.Active ? Quaternion.Euler(32f, 72f, 0f) : Quaternion.Euler(33f, -38f, 0f);

            RenderSettings.skybox = SkyMaterial();
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.5f, 0.55f, 0.66f);
            RenderSettings.ambientEquatorColor = new Color(0.36f, 0.35f, 0.34f);
            RenderSettings.ambientGroundColor = new Color(0.13f, 0.12f, 0.1f);
            // Nebel macht der eigene Render-Pass (Hoehennebel) – Unitys Nebel aus, sonst doppelt
            RenderSettings.fog = false;
            // Himmel spiegelt sich sonst grell im Moorwasser
            RenderSettings.reflectionIntensity = 0.5f;
        }

        private static Material SkyMaterial()
        {
            string path = Folder + "/Sky_Ashveil.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader sh = Shader.Find("Skybox/Procedural");
            if (mat == null)
            {
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = sh;
            mat.SetFloat("_SunDisk", 2f);
            mat.SetFloat("_SunSize", 0.03f);
            mat.SetFloat("_SunSizeConvergence", 6f);
            mat.SetFloat("_AtmosphereThickness", 1.35f);
            mat.SetColor("_SkyTint", new Color(0.42f, 0.46f, 0.55f));
            mat.SetColor("_GroundColor", new Color(0.19f, 0.18f, 0.18f));
            mat.SetFloat("_Exposure", 0.95f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ================================================================== Nebel und Nachbearbeitung

        public static void SetupAtmosphere(Light sun)
        {
            ProjectSetup.EnsureFolder(Folder);
            var go = new GameObject("Atmosphaere (Nebel, Nachbearbeitung)");
            var atmo = go.AddComponent<AtmosphereController>();
            K(atmo, "fogMaterial", FogMaterial());
            K(atmo, "sun", sun);
            // M8: Tag und Nacht nach der gemeinsamen Weltuhr (nur im Spiel aktiv)
            var cycle = go.AddComponent<DayNightCycle>();
            K(cycle, "sun", sun);
            // Dynamic Sky V1: prozeduraler Himmel mit Wolken, zwei Monden und Sternen ueber dem Tageszyklus
            SkyContentBuilder.Install(go, sun);

            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            volume.sharedProfile = VolumeProfileAsset();
        }

        private static void K(Object target, string field, Object value) => GreyboxKit.SetRef(target, field, value);

        private static Material FogMaterial()
        {
            string path = Folder + "/M_AtmosphereFog.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader sh = Shader.Find("Hidden/Ashveil/AtmosphereFog");
            if (sh == null)
            {
                Debug.LogWarning("[Ashveil] Nebel-Shader fehlt (Hidden/Ashveil/AtmosphereFog).");
                return null;
            }
            if (mat == null)
            {
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = sh;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static VolumeProfile VolumeProfileAsset()
        {
            string path = Folder + "/Ashveil_Kareth_Profile.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tone = Add<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.ACES);

            var bloom = Add<Bloom>(profile);
            bloom.threshold.Override(0.9f);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.65f);
            bloom.tint.Override(new Color(1f, 0.86f, 0.7f));

            var color = Add<ColorAdjustments>(profile);
            color.postExposure.Override(0.35f);
            color.contrast.Override(18f);
            color.saturation.Override(-12f);
            color.colorFilter.Override(new Color(1f, 0.98f, 0.95f));

            var split = Add<SplitToning>(profile);
            split.shadows.Override(new Color(0.36f, 0.43f, 0.56f));
            split.highlights.Override(new Color(0.64f, 0.53f, 0.4f));
            split.balance.Override(-15f);

            var wb = Add<WhiteBalance>(profile);
            wb.temperature.Override(-6f);

            var vignette = Add<Vignette>(profile);
            vignette.intensity.Override(0.28f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(Color.black);

            var grain = Add<FilmGrain>(profile);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.12f);
            grain.response.Override(0.8f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            T c = profile.Add<T>(true);
            c.name = typeof(T).Name;
            c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }

        // ================================================================== Kamera und Luft

        public static void SetupCamera(Camera cam)
        {
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.renderShadows = true;
            cam.allowHDR = true;
            cam.farClipPlane = 2500f;
            AddAshFlakes(cam.transform);
        }

        /// <summary>Feine Ascheflocken rund um die Kamera (Welt-Simulation, faellt langsam).</summary>
        private static void AddAshFlakes(Transform cam)
        {
            var go = new GameObject("Ascheflocken");
            go.transform.SetParent(cam, false);
            go.transform.localPosition = new Vector3(0f, 2f, 10f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 10f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 14f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.07f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.72f, 0.7f, 0.68f, 0.55f), new Color(0.45f, 0.43f, 0.42f, 0.45f));
            main.gravityModifier = 0.015f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1500;
            main.prewarm = true;
            var emission = ps.emission;
            emission.rateOverTime = 70f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(45f, 16f, 45f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.1f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = GreyboxKit.ParticleMaterial();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
        }
    }
}
