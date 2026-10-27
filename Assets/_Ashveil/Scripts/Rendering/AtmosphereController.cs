using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ashveil.Rendering
{
    /// <summary>
    /// Stimmung der Welt (Art-Bible "Asche & Glut"): Hoehennebel mit Sonnenstreuung als eigener Render-Pass,
    /// Werte als globale Shader-Parameter. Laeuft auch im Editor (Szenenansicht, Stilbilder).
    /// Die Vorschau-Kamera des Charakterfensters bekommt keinen Nebel.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class AtmosphereController : MonoBehaviour
    {
        [SerializeField] private Material fogMaterial;
        [SerializeField] private Light sun;

        [Header("Nebel")]
        [SerializeField] private Color fogColor = new Color(0.42f, 0.46f, 0.52f);
        [Tooltip("Dichte am Boden (je Meter)")]
        [SerializeField] private float density = 0.0032f;
        [Tooltip("Abnahme mit der Hoehe (je Meter) – groesser = Nebel liegt tiefer")]
        [SerializeField] private float heightFalloff = 0.045f;
        [Tooltip("Hoehe, auf die sich die Dichte bezieht")]
        [SerializeField] private float baseHeight = 0f;
        [Range(0f, 1f)] [SerializeField] private float maxOpacity = 0.95f;
        [Tooltip("Klarer Bereich direkt vor der Kamera (m)")]
        [SerializeField] private float startDistance = 3f;
        [Tooltip("Zusaetzlicher gleichmaessiger Dunst (je Meter)")]
        [SerializeField] private float haze = 0.00025f;

        [Header("Himmel")]
        [Tooltip("Wie schnell der Nebel am Himmel nach oben ausduennt")]
        [SerializeField] private float horizonSharpness = 4f;
        [SerializeField] private float skyDistance = 1500f;

        [Header("Sonnenstreuung")]
        [SerializeField] private Color sunScatter = new Color(1.1f, 0.78f, 0.5f);
        [SerializeField] private float sunPower = 6f;

        private static readonly int FogColorId = Shader.PropertyToID("_AshFogColor");
        private static readonly int FogParamsId = Shader.PropertyToID("_AshFogParams");
        private static readonly int FogParams2Id = Shader.PropertyToID("_AshFogParams2");
        private static readonly int SunColorId = Shader.PropertyToID("_AshSunColor");
        private static readonly int SunDirId = Shader.PropertyToID("_AshFogDir");

        private AtmosphereFogPass _pass;
        private int _previewLayer = -2;

        public static AtmosphereController Active { get; private set; }

        /// <summary>M8: Tageszeit (DayNightCycle) faerbt Nebel und Sonnenstreuung um.</summary>
        public Color FogColor { get => fogColor; set => fogColor = value; }
        public Color SunScatter { get => sunScatter; set => sunScatter = value; }

        /// <summary>Unter Tage (IndoorAtmosphere): Nebel gleichmaessig dicht statt Hoehennebel mit Sonnenschein.</summary>
        public static bool IndoorOverride;
        public static Color IndoorFogColor = new Color(0.16f, 0.15f, 0.14f);
        public static float IndoorFogDensity = 0.014f;

        /// <summary>Dynamic Sky Pass 11: Gebietsluft (Glockenmoor dichter …) – setzt der SkyDirector, geglaettet.</summary>
        public static float LocalFogFactor = 1f;
        public static Color LocalFogTint = Color.white;

        private void OnEnable()
        {
            Active = this;
            _pass ??= new AtmosphereFogPass();
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            Apply();
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            if (Active == this) Active = null;
        }

        private void OnValidate() => Apply();

        private void Update() => Apply();

        /// <summary>Globale Shader-Parameter setzen (auch fuer Wasser/Partikel nutzbar).</summary>
        public void Apply()
        {
            if (IndoorOverride)
            {
                // Staub im Stollen: ueberall gleich dicht (kaum Hoehenabfall), kein Himmel, kein Sonnenschimmer
                Shader.SetGlobalColor(FogColorId, new Color(IndoorFogColor.r, IndoorFogColor.g, IndoorFogColor.b, 0.92f));
                Shader.SetGlobalVector(FogParamsId, new Vector4(IndoorFogDensity, 0.0001f, -200f, horizonSharpness));
                Shader.SetGlobalVector(FogParams2Id, new Vector4(2f, 100000f, 0f, 0f));
                Shader.SetGlobalColor(SunColorId, new Color(0f, 0f, 0f, sunPower));
            }
            else
            {
                float f = Mathf.Clamp(LocalFogFactor, 0.3f, 4f);
                Color fc = fogColor * LocalFogTint;
                Shader.SetGlobalColor(FogColorId, new Color(fc.r, fc.g, fc.b, maxOpacity));
                Shader.SetGlobalVector(FogParamsId, new Vector4(density * f, Mathf.Max(0.0001f, heightFalloff), baseHeight, horizonSharpness));
                Shader.SetGlobalVector(FogParams2Id, new Vector4(startDistance, skyDistance, haze * f, 0f));
                Shader.SetGlobalColor(SunColorId, new Color(sunScatter.r, sunScatter.g, sunScatter.b, sunPower));
            }
            Vector3 toSun = sun != null ? -sun.transform.forward : new Vector3(0.3f, 0.4f, 0.3f).normalized;
            Shader.SetGlobalVector(SunDirId, new Vector4(toSun.x, toSun.y, toSun.z, 0f));
            // Umgebungslicht fuer Vegetation (Terrain-Details bekommen beim Instancing keine SH-Werte)
            Shader.SetGlobalColor("_AshAmbientSky", RenderSettings.ambientSkyColor.linear);
            Shader.SetGlobalColor("_AshAmbientEquator", RenderSettings.ambientEquatorColor.linear);
            Shader.SetGlobalColor("_AshAmbientGround", RenderSettings.ambientGroundColor.linear);
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (fogMaterial == null || cam == null) return;
            if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
            if (cam.orthographic) return; // Draufsichten/Karten: Hoehennebel setzt eine Perspektivkamera voraus
            if (_previewLayer == -2) _previewLayer = LayerMask.NameToLayer("Preview");
            // Charaktervorschau (rendert nur die Vorschau-Ebene) ohne Nebel
            if (_previewLayer >= 0 && cam.cullingMask == 1 << _previewLayer) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null) return;
            _pass.Setup(fogMaterial);
            data.scriptableRenderer.EnqueuePass(_pass);
        }
    }
}
