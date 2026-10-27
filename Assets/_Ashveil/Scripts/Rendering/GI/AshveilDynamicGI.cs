using System;
using System.Reflection;
using Ashveil.Rendering.PathTracing;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Ashveil.Rendering.GI
{
    /// <summary>
    /// Dynamische globale Beleuchtung in Echtzeit (Vorbild: der eigene GI-Raytracer von WoW Forever): Licht der Sonne,
    /// des Himmels, der Feuer und Fackeln prallt von Boden, Felsen und Waenden ab, faerbt die Umgebung und folgt dem
    /// Tag-Nacht-Wechsel – ohne Backen, ohne Lightmaps, auch fuer Tueren, Karren und neu geladene Weltzellen.
    ///
    /// Verfahren (DDGI, "Dynamic Diffuse Global Illumination"): Zwei Gitter aus Lichtproben wandern mit der Kamera
    /// (fein 2,5 m, grob 10 m). Jedes Bild schiessen einige tausend Proben je 64 Strahlen in dieselbe
    /// Beschleunigungsstruktur wie der Pathtracer (gleiche Material-Stellvertreter, keine doppelte Pflege). Am Treffer:
    /// Sonne/Mond mit Schattenstrahl, ein lokales Licht mit Schattenstrahl, das Probenlicht des letzten Bildes (daraus
    /// werden ueber die Zeit unendlich viele Abpraller) und Leuchten. Ergebnis: Licht und Abstand je Richtung
    /// (Oktaeder-Kacheln), Sichtbarkeitspruefung gegen Lichtlecks durch Waende.
    ///
    /// Anbindung an URP ohne eigene Material-Shader: URP 17.6 kennt "Screen Space Irradiance" (Lit, TerrainLit,
    /// ASHVEIL-Gras nehmen es statt des flachen Umgebungslichts). Ein Bildpass nach dem Tiefen-/Normalen-Vorpass
    /// fuellt diese Textur aus den Proben. SSAO, Nebel, Nachbearbeitung laufen unveraendert weiter.
    ///
    /// Nur Darstellung: kein Netzwerkzustand, keine Spiellogik. Ohne Raytracing (DirectX 11, alte Karte) bleibt es aus,
    /// das Spiel nutzt dann das klassische Umgebungslicht der Atmosphaere.
    /// </summary>
    [DefaultExecutionOrder(9990)]
    [DisallowMultipleComponent]
    public sealed class AshveilDynamicGI : MonoBehaviour
    {
        public static AshveilDynamicGI Instance { get; private set; }
        public static bool IsRunning => Instance != null && Instance.isActiveAndEnabled && Instance._ready;
        /// <summary>0 = 2048, 1 = 3072, 2 = 4096 Proben je Bild (volle Auffrischung alle 12 / 8 / 6 Bilder).</summary>
        public static int Quality = 1;
        public static string LastError { get; private set; } = "";

        public const int Tile = 8;          // muss zu ASH_GI_TILE passen
        public const int Cascades = 2;
        private const int RayCapacity = 8192 * 64;   // Strahlen je Aufruf (Puffer 8 MB)

        [Header("Gitter (wandert mit der Kamera)")]
        public Vector3Int grid = new Vector3Int(32, 12, 32);
        public float spacingFine = 2.5f;
        public float spacingCoarse = 10f;
        [Range(16, 128)] public int raysPerProbe = 64;
        [Tooltip("Proben nur jedes n-te Bild auffrischen (dafuer n-mal so viele): Unity baut je Strahlenaufruf die Shader-Tabelle, " +
                 "das kostet CPU-Zeit je Instanz. 2 halbiert diese Grundkosten bei gleicher Auffrischrate.")]
        [Range(1, 4)] public int updateInterval = 2;
        public float maxRayLength = 250f;

        [Header("Licht")]
        [Tooltip("Staerke des indirekten Lichts (1 = physikalisch)")]
        public float intensity = 1f;
        public float skyStrength = 1f;
        [Tooltip("Himmelslicht an die Helligkeit des klassischen Umgebungslichts angleichen (1) oder physikalisch lassen (0). " +
                 "Verdeckung unter Daechern, in Hoehlen und farbige Abpraller bleiben in beiden Faellen erhalten.")]
        [Range(0f, 1f)] public float matchClassic = 1f;
        [Tooltip("Mehrfach-Abpraller (1 = physikalisch, 0 = nur ein Abprall)")]
        public float bounceStrength = 1f;
        [Tooltip("Rueckfall ausserhalb des Gitters (klassisches Umgebungslicht)")]
        public float fallbackStrength = 1f;

        [Header("Proben")]
        [Range(0f, 0.99f)] public float hysteresis = 0.94f;
        [Range(0f, 0.99f)] public float distanceHysteresis = 0.96f;
        [Tooltip("Anteil Rueckseiten-Treffer, ab dem eine Probe als 'in Geometrie' gilt und abgeschaltet wird")]
        [Range(0.05f, 0.9f)] public float backfaceThreshold = 0.3f;
        [Tooltip("Versatz entlang der Normalen (Anteil des Probenabstands)")]
        public float normalBias = 0.1f;
        [Tooltip("Versatz Richtung Kamera (Anteil des Probenabstands)")]
        public float viewBias = 0.3f;
        [Tooltip("Ueberblendung zur groben Kaskade (Zellen)")]
        public float edgeBlend = 3f;

        [Header("Diagnose")]
        public bool showHud;
        [Tooltip("0 normal, 2 = gruen: Proben gueltig, rot: Rueckfall")]
        public int debugView;
        [Tooltip("Proben laufen weiter, das Bild nutzt aber das klassische Umgebungslicht (fuer Vergleiche)")]
        public bool bypassScreen;
        [Tooltip("Kostendiagnose: 0 alles, 1 ohne Bildpass (Vorpass bleibt), 2 ohne Probenstrahlen, 3 ohne Vorpass und Bildpass, 4 nur 64 Proben, 5 ohne Bewegungsnachfuehrung, 6 = 4+5")]
        public int costMode;

        public GIStats Stats { get; } = new GIStats();

        private RayTracingShader _rts;
        private ComputeShader _cs;
        private Material _screen;
        private AshveilRTMaterialRegistry _materials;
        private AshveilRTSceneManager _scene;
        private AshveilRTLightRegistry _lights;
        private AshveilRTEnvironment _env;
        private readonly PathTraceSettings _envSettings = new PathTraceSettings { environment = PathTraceEnvironment.Spielhimmel, environmentIntensity = 1f };
        private RenderTexture _irr, _dist;
        private GraphicsBuffer _state, _rays;
        private AshveilGIPass _pass;
        private ProfilingSampler _sampler;
        private int _kBlend, _kClear;
        private bool _ready, _needsClear, _hasScene, _forceCollect, _passSet, _mustBuild;
        private Vector3 _lastCamPos;
        private int _streamBurst;
        private Material _sharedTest;
        private float _skyScale = 1f;
        public float SkyScale => _skyScale;
        private static readonly Vector3[] s_up = { Vector3.up };
        private static readonly Color[] s_upResult = new Color[1];
        private int _next, _lastUpdateFrame = -1, _frame, _lastBuildFrame = -1;
        private float _dirtySince = -1f;
        private Vector3Int _gridUsed;
        private readonly System.Random _rng = new System.Random(20260928);
        private readonly Vector4[] _cascade = new Vector4[Cascades];
        private Matrix4x4 _rotation = Matrix4x4.identity;

        private static Action<UniversalResourceData, TextureHandle> s_setIrradiance;
        private static bool s_setIrradianceResolved;

        // ------------------------------------------------------------------ Ein/Aus

        /// <summary>
        /// Unitys Surface-Cache-GI steht nur im Renderer, damit URP beim Bauen die Screen-Space-Irradiance-Varianten behaelt
        /// (siehe Editor/DynamicGIBuildSupport). Es darf nie laufen: vor dem ersten Bild abschalten.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void DisableUnitySurfaceCache()
        {
            try
            {
                var assets = new System.Collections.Generic.HashSet<UniversalRenderPipelineAsset>();
                if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset a) assets.Add(a);
                for (int i = 0; i < QualitySettings.count; i++)
                    if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset q) assets.Add(q);
                FieldInfo list = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                foreach (UniversalRenderPipelineAsset asset in assets)
                {
                    if (!(list?.GetValue(asset) is ScriptableRendererData[] data)) continue;
                    foreach (ScriptableRendererData d in data)
                    {
                        if (d == null) continue;
                        foreach (ScriptableRendererFeature f in d.rendererFeatures)
                            if (f != null && f.isActive && f.GetType().Name == "SurfaceCacheGIRendererFeature") f.SetActive(false);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Ashveil] Surface-Cache-Funktion nicht abschaltbar: " + e.Message);
            }
        }

        public static AshveilDynamicGI Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("Ashveil Dynamisches GI");
            DontDestroyOnLoad(go);
            go.SetActive(false);
            Instance = go.AddComponent<AshveilDynamicGI>();
            return Instance;
        }

        /// <summary>Ein-/Ausschalten. false, wenn das Geraet es nicht kann (Grund in <see cref="LastError"/>).</summary>
        public static bool SetEnabled(bool on)
        {
            if (on && !PathTracingCapability.IsSupported)
            {
                LastError = "Dynamisches GI " + PathTracingCapability.Describe();
                if (Instance != null) Instance.gameObject.SetActive(false);
                return false;
            }
            if (!on && Instance == null) return true;
            AshveilDynamicGI gi = on ? Ensure() : Instance;
            if (gi.gameObject.activeSelf != on) gi.gameObject.SetActive(on);
            return !on || IsRunning;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnEnable()
        {
            _ready = false;
            if (!PathTracingCapability.IsSupported) { Fail("Dynamisches GI " + PathTracingCapability.Describe()); return; }
            if (!ResolveUrpHook()) { Fail("Dynamisches GI: URP bietet die Schnittstelle 'irradianceTexture' nicht (URP-Version geaendert?)."); return; }
            _rts = Resources.Load<RayTracingShader>("AshveilGIProbes");
            _cs = Resources.Load<ComputeShader>("AshveilGIUpdate");
            Shader screen = Resources.Load<Shader>("AshveilGIScreen");
            Shader surface = Resources.Load<Shader>("AshveilPTSurface");
            Shader terrain = Resources.Load<Shader>("AshveilPTTerrain");
            Shader bake = Resources.Load<Shader>("AshveilPTSkyBake");
            if (_rts == null || _cs == null || screen == null || surface == null)
            {
                Fail("Dynamisches GI: Shader fehlen (Resources/AshveilGIProbes, AshveilGIUpdate, AshveilGIScreen, AshveilPTSurface).");
                return;
            }
            _screen = new Material(screen) { hideFlags = HideFlags.DontSave };
            _materials = new AshveilRTMaterialRegistry(surface);
            _scene = new AshveilRTSceneManager(_materials, terrain)
            {
                IncludeSkinned = false,            // Figuren werfen im GI kein Licht zurueck (klein, bewegt, teuer)
                IncludeDetails = false,            // Gras: der Boden darunter reicht fuer diffuses Licht
                CollectEmissiveTriangles = false,  // Leuchten wirken ueber Strahltreffer (Emission), nicht als Lichtprobe
                PreferCoarseLod = true,
                MinExtent = 1.2f,                  // Kisten, Flaschen, Werkzeug, Zaunlatten: fuer diffuses Licht unerheblich
                MoveThreshold = 0.3f,
                ExcludeAnimatedAttachments = true, // Waffen/Werkzeug in Figurenhand bewegen sich staendig
                DropMoversAfter = 3,               // was dauernd laeuft (Tiere, Wagen), fliegt raus – Tore/Tueren bleiben
                StreamRadius = 200f,               // Kleines erst in 200 m Naehe (weiter reichen die Proben kaum)
                StreamKeepSize = 20f,
                StreamKeepRadius = 1500f,          // Grosses bis 1,5 km (Himmelsverdeckung durch Berge), danach heraus
                MaxAddsPerBuild = 64,              // je Neubau hoechstens 64 neue Objekte (Speicher fuer BLAS verteilt; CORE-005)
                IncrementalScenes = true,          // Zellen kommen schrittweise dazu – kein Ruckler beim Betreten
                TerrainMaxVertsPerSide = 257,      // Gelaende grob (ca. 4 m Raster) – schnell gebaut, fuer diffuses Licht genug
                DebugName = "GI"
            };
            _lights = new AshveilRTLightRegistry { CacheLights = true };
            _env = new AshveilRTEnvironment(bake, 256, 128, false) { ComputeUpIrradiance = true };   // klein; nur Himmelshelligkeit zurueck
            _kBlend = _cs.FindKernel("AshGIBlend");
            _kClear = _cs.FindKernel("AshGIClear");
            AllocateProbes();
            _sampler = new ProfilingSampler("Ashveil Dynamisches GI") { enableRecording = true };
            _pass = new AshveilGIPass(this);
            _hasScene = false;
            _passSet = false;
            _dirtySince = -1f;
            _next = 0;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            _ready = true;
            LastError = "";
            Debug.Log($"[Ashveil] Dynamisches GI an: {grid.x}×{grid.y}×{grid.z}×{Cascades} Proben ({spacingFine} m / {spacingCoarse} m), " +
                      $"{ProbesPerFrame} Proben × {raysPerProbe} Strahlen je Bild – {PathTracingCapability.Describe()}");
        }

        private void Fail(string why)
        {
            LastError = why;
            Debug.LogWarning("[Ashveil] " + why);
            enabled = false;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            _ready = false;
            _scene?.Dispose(); _scene = null;
            _lights?.Dispose(); _lights = null;
            _env?.Dispose(); _env = null;
            _materials?.Dispose(); _materials = null;
            ReleaseProbes();
            if (_screen != null) Destroy(_screen);
            _screen = null;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private static bool ResolveUrpHook()
        {
            if (!s_setIrradianceResolved)
            {
                s_setIrradianceResolved = true;
                try
                {
                    PropertyInfo p = typeof(UniversalResourceData).GetProperty("irradianceTexture",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    MethodInfo set = p?.GetSetMethod(true);
                    if (set != null)
                        s_setIrradiance = (Action<UniversalResourceData, TextureHandle>)Delegate.CreateDelegate(
                            typeof(Action<UniversalResourceData, TextureHandle>), set);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Ashveil] Dynamisches GI: URP-Schnittstelle nicht erreichbar: " + e.Message);
                    s_setIrradiance = null;
                }
            }
            return s_setIrradiance != null;
        }

        internal static void PublishIrradiance(UniversalResourceData res, TextureHandle tex) { s_setIrradiance?.Invoke(res, tex); LastScreenFrame = Time.frameCount; }

        /// <summary>Diagnose (10.10.2026, Flacker-Rekorder): letztes Bild, in dem der GI-Bildpass die Irradiance-Textur geliefert hat,
        /// und der Grund, wenn er zuletzt ausgelassen wurde. Fehlt der Pass in einem Bild, nimmt URP das klassische Umgebungslicht –
        /// der Boden springt hell.</summary>
        public static int LastScreenFrame { get; private set; } = -1;
        public static string LastSkipReason { get; internal set; } = "";

        public int TotalProbes => _gridUsed.x * _gridUsed.y * _gridUsed.z * Cascades;
        public int ProbesPerFrame => Mathf.Min(TotalProbes > 0 ? TotalProbes : int.MaxValue, Quality >= 2 ? 4096 : Quality == 1 ? 3072 : 2048);

        private void AllocateProbes()
        {
            ReleaseProbes();
            _gridUsed = new Vector3Int(Mathf.Clamp(grid.x, 4, 64), Mathf.Clamp(grid.y, 4, 32), Mathf.Clamp(grid.z, 4, 64));
            int w = _gridUsed.x * _gridUsed.y * Tile, h = _gridUsed.z * Tile;
            _irr = MakeAtlas("AshveilGI_Licht", w, h);
            _dist = MakeAtlas("AshveilGI_Abstand", w, h);
            _state = new GraphicsBuffer(GraphicsBuffer.Target.Structured, TotalProbes, 16) { name = "AshveilGI_Zustand" };
            _rays = new GraphicsBuffer(GraphicsBuffer.Target.Structured, RayCapacity, 16) { name = "AshveilGI_Strahlen" };
            _needsClear = true;
        }

        private static RenderTexture MakeAtlas(string name, int w, int h)
        {
            var rt = new RenderTexture(w, h, 0, GraphicsFormat.R16G16B16A16_SFloat)
            {
                name = name, dimension = TextureDimension.Tex2DArray, volumeDepth = Cascades, enableRandomWrite = true,
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, useMipMap = false, hideFlags = HideFlags.DontSave
            };
            rt.Create();
            return rt;
        }

        private void ReleaseProbes()
        {
            if (_irr != null) { _irr.Release(); Destroy(_irr); _irr = null; }
            if (_dist != null) { _dist.Release(); Destroy(_dist); _dist = null; }
            _state?.Release(); _state = null;
            _rays?.Release(); _rays = null;
        }

        /// <summary>Alles neu aufnehmen und die Proben verwerfen (z. B. nach grossen Aenderungen im Editor).</summary>
        public void Recapture()
        {
            _forceCollect = true;
            _needsClear = true;
        }

        // ------------------------------------------------------------------ CPU: Szene, Lichter, Gitterlage

        private void LateUpdate()
        {
            if (!_ready) return;
            float tCpu = Time.realtimeSinceStartup;
            try { LateUpdateInner(); }
            finally
            {
                float ms = (Time.realtimeSinceStartup - tCpu) * 1000f;
                LogSpike(ms);
                Stats.CpuMs = Mathf.Lerp(Stats.CpuMs, ms, 0.05f);
                Stats.CpuPeakMs = Mathf.Max(Stats.CpuPeakMs * 0.995f, ms);
                Stats.FrameMs = Mathf.Lerp(Stats.FrameMs, Time.unscaledDeltaTime * 1000f, 0.05f);
                if (_sampler != null && _sampler.gpuElapsedTime > 0f) Stats.GpuMs = _sampler.gpuElapsedTime;   // nur Bilder mit Strahlen
                if (_pass != null) Stats.ScreenGpuMs = _pass.ScreenGpuMs;
            }
        }

        private void LateUpdateInner()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 camPos = cam.transform.position;
            _frame++;

            // Diagnose 6: nur grosse Objekte (misst, ob die Grundkosten je Strahlenaufruf mit der Instanzzahl wachsen)
            float wantMin = costMode == 6 ? 4f : 1.2f;
            if (!Mathf.Approximately(_scene.MinExtent, wantMin)) { _scene.MinExtent = wantMin; _forceCollect = true; }

            // Diagnose 7: ein gemeinsames Material fuer alle Netze
            bool wantShared = costMode == 7;
            if (wantShared != (_scene.OverrideMaterial != null))
            {
                if (wantShared && _sharedTest == null) _sharedTest = new Material(Resources.Load<Shader>("AshveilPTSurface")) { hideFlags = HideFlags.DontSave };
                _scene.OverrideMaterial = wantShared ? _sharedTest : null;
                _forceCollect = true;
            }

            // Neu aufnehmen nur bei Zellen-/Szenenwechsel (gesammelt, damit mehrere Zellen eine Aufnahme ergeben) oder
            // weit weg vom Ursprung – nie beim normalen Laufen (kein Ruckeln).
            // Ursprung erst ab 8 km neu setzen (Gleitkomma bleibt auf mm genau). Spruenge in Instanzen (x ≈ 4 km) loesen
            // keine Voll-Aufnahme mehr aus (vorher 50–120 ms Ruckler), sondern ein paar Bilder verstaerktes Streaming.
            bool collect = !_hasScene || _forceCollect || (camPos - _scene.Origin).sqrMagnitude > 8000f * 8000f;
            if (_hasScene && (camPos - _lastCamPos).sqrMagnitude > 150f * 150f) _streamBurst = 90;
            _lastCamPos = camPos;
            if (!collect && _scene.SceneDirty)
            {
                if (_dirtySince < 0f) _dirtySince = Time.unscaledTime;
                else if (Time.unscaledTime - _dirtySince > 0.4f) collect = true;
            }
            if (collect)
            {
                float t0 = Time.realtimeSinceStartup;
                _scene.Collect(camPos, 4000f);
                Stats.CollectMs = (Time.realtimeSinceStartup - t0) * 1000f;
                Stats.Collects++;
                _hasScene = true;
                _mustBuild = true;
                _forceCollect = false;
                _dirtySince = -1f;
                _lights.Collect(camPos, _scene.Origin, _envSettings);
            }
            else
            {
                if (costMode != 5) _scene.UpdateDynamic(_frame, 8);   // je Bild ein Achtel pruefen
                _scene.Stream(camPos, _streamBurst > 0 ? 512 : 48);
                if (_streamBurst > 0) _streamBurst--;
                if (_frame % 240 == 0) _materials.Refresh();
                if (_frame % 20 == 0) _lights.Collect(camPos, _scene.Origin, _envSettings);
            }
            if (_frame % 30 == 1) _env.Check(_envSettings);
            UpdateSkyScale();

            // Gitterlage je Kaskade: um die Kamera zentriert, in ganzen Zellen (Proben bleiben an festen Weltpunkten)
            for (int c = 0; c < Cascades; c++)
            {
                float s = c == 0 ? spacingFine : spacingCoarse;
                Vector3Int b = new Vector3Int(
                    Mathf.FloorToInt(camPos.x / s) - _gridUsed.x / 2,
                    Mathf.FloorToInt(camPos.y / s) - _gridUsed.y / 2,
                    Mathf.FloorToInt(camPos.z / s) - _gridUsed.z / 2);
                _cascade[c] = new Vector4(b.x, b.y, b.z, s);
            }
            SetSamplingGlobals();
        }

        /// <summary>
        /// Helligkeitsabgleich: Das klassische Umgebungslicht (Dreifarb-Verlauf der Atmosphaere) ist heller als der echte
        /// Himmel. Mit matchClassic = 1 bekommt eine freie, nach oben zeigende Flaeche dieselbe Helligkeit wie bisher –
        /// das GI nimmt dann nur dort etwas weg, wo wirklich verdeckt ist, und fuegt farbige Abpraller hinzu.
        /// </summary>
        private void UpdateSkyScale()
        {
            float target = 1f;
            if (matchClassic > 0f && _env.UpIrradianceValid)
            {
                RenderSettings.ambientProbe.Evaluate(s_up, s_upResult);
                float classic = Lum(s_upResult[0]);
                float sky = Lum(_env.UpIrradiance) * Mathf.Max(skyStrength, 1e-3f);
                // nie dunkler als physikalisch (Tag: echter Himmel ist heller), nachts bis 4× heller (lesbare Naechte)
                float ratio = sky > 1e-5f ? Mathf.Clamp(classic / sky, 1f, 4f) : 1f;
                target = Mathf.Lerp(1f, ratio, matchClassic);
            }
            _skyScale = Mathf.Lerp(_skyScale, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 2f));
        }

        private static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        private static class Ids
        {
            public static readonly int Irradiance = Shader.PropertyToID("_AshGIIrradiance");
            public static readonly int Distance = Shader.PropertyToID("_AshGIDistance");
            public static readonly int State = Shader.PropertyToID("_AshGIState");
            public static readonly int Grid = Shader.PropertyToID("_AshGIGrid");
            public static readonly int Cascade0 = Shader.PropertyToID("_AshGICascade0");
            public static readonly int Cascade1 = Shader.PropertyToID("_AshGICascade1");
            public static readonly int Bias = Shader.PropertyToID("_AshGIBias");
            public static readonly int ScreenParams = Shader.PropertyToID("_AshGIScreenParams");
            public static readonly int Rays = Shader.PropertyToID("_AshGIRays");
            public static readonly int RaysIn = Shader.PropertyToID("_AshGIRaysIn");
            public static readonly int IrradianceRW = Shader.PropertyToID("_AshGIIrradianceRW");
            public static readonly int DistanceRW = Shader.PropertyToID("_AshGIDistanceRW");
            public static readonly int StateRW = Shader.PropertyToID("_AshGIStateRW");
            public static readonly int Rotation = Shader.PropertyToID("_AshGIRotation");
            public static readonly int Origin = Shader.PropertyToID("_AshGIOrigin");
            public static readonly int Update = Shader.PropertyToID("_AshGIUpdate");
            public static readonly int SunIrr = Shader.PropertyToID("_AshGISunIrr");
            public static readonly int SunDir = Shader.PropertyToID("_AshGISunDir");
            public static readonly int Misc = Shader.PropertyToID("_AshGIMisc");
            public static readonly int Fallback = Shader.PropertyToID("_AshGIFallback");
            public static readonly int BlendParams = Shader.PropertyToID("_AshGIBlendParams");
            public static readonly int Scene = Shader.PropertyToID("_AshPTScene");
            public static readonly int Lights = Shader.PropertyToID("_AshPTLights");
            public static readonly int LightCounts = Shader.PropertyToID("_AshPTLightCounts");
            public static readonly int EnvMap = Shader.PropertyToID("_AshPTEnvMap");
            public static readonly int EnvParams = Shader.PropertyToID("_AshPTEnvParams");
            public static readonly int EnvUniform = Shader.PropertyToID("_AshPTEnvUniform");
        }

        private Vector4 GridVector => new Vector4(_gridUsed.x, _gridUsed.y, _gridUsed.z, Cascades);
        private Vector4 BiasVector => new Vector4(normalBias, viewBias, intensity, edgeBlend);

        private void SetSamplingGlobals()
        {
            Shader.SetGlobalTexture(Ids.Irradiance, _irr);
            Shader.SetGlobalTexture(Ids.Distance, _dist);
            Shader.SetGlobalBuffer(Ids.State, _state);
            Shader.SetGlobalVector(Ids.Grid, GridVector);
            Shader.SetGlobalVector(Ids.Cascade0, _cascade[0]);
            Shader.SetGlobalVector(Ids.Cascade1, _cascade[1]);
            Shader.SetGlobalVector(Ids.Bias, BiasVector);
            Shader.SetGlobalVector(Ids.ScreenParams, new Vector4(debugView, fallbackStrength, 0, 0));
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam == null || cam.cameraType != CameraType.Game || cam != Camera.main) return;
            if (!_ready || !_hasScene) { LastSkipReason = !_ready ? "nicht bereit" : "keine Szene"; return; }
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null) { LastSkipReason = "keine Kameradaten"; return; }
            // Kostendiagnose: 3 = weder Vorpass noch Bildpass (nur Proben) – zeigt, was der Tiefen-/Normalen-Vorpass kostet
            _pass.ConfigureInput(costMode == 3 ? ScriptableRenderPassInput.None : ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            data.scriptableRenderer.EnqueuePass(_pass);
        }

        // ------------------------------------------------------------------ GPU: Proben auffrischen

        internal bool WantsUpdate => _lastUpdateFrame < 0 || Time.frameCount - _lastUpdateFrame >= Mathf.Max(1, updateInterval);
        internal ProfilingSampler Sampler => _sampler;
        internal Material ScreenMaterial => _screen;

        internal void UpdateProbes(CommandBuffer cmd)
        {
            _lastUpdateFrame = Time.frameCount;
            if (_scene == null || _scene.Structure == null) return;
            bool built = false;
            // Neubau hoechstens alle 30 Bilder (bewegte Objekte sammeln sich, das diffuse Licht hinkt ohnehin traege nach).
            // Nach einem Sprung oder wenn das Streaming Objekte zurueckstellt (MaxAddsPerBuild) alle 4 Bilder: so kommen die
            // neuen Objekte in kleinen Portionen hinein, statt in einem Bau, der den Grafikfaden 50–64 ms aufhaelt (09.10.2026).
            int bauAbstand = _streamBurst > 0 || _scene.AddsCapped ? 4 : 30;
            if (_scene.NeedsBuild && (_mustBuild || _scene.RequiresImmediateBuild || _frame - _lastBuildFrame >= bauAbstand))
            {
                _scene.Build(cmd); built = true; Stats.Builds++; _lastBuildFrame = _frame; _mustBuild = false;
                _scene.RequiresImmediateBuild = false;
                _builtFrame = Time.frameCount;
            }
            _env.Bake(cmd);

            int total = TotalProbes;
            if (_needsClear)
            {
                cmd.SetComputeVectorParam(_cs, Ids.Grid, GridVector);
                cmd.SetComputeBufferParam(_cs, _kClear, Ids.StateRW, _state);
                cmd.DispatchCompute(_cs, _kClear, (total + 63) / 64, 1, 1);
                _needsClear = false;
                _next = 0;
            }

            int rays = Mathf.Clamp(raysPerProbe, 16, 128);
            int probes = costMode == 4 ? 64 : ProbesPerFrame * Mathf.Max(1, updateInterval);   // Diagnose 4: Grundkosten je Aufruf
            probes = Mathf.Min(probes, Mathf.Min(total, RayCapacity / rays));
            var update = new Vector4(_next, probes, rays, maxRayLength);
            _next = (_next + probes) % total;
            _rotation = Matrix4x4.Rotate(RandomRotation());

            // Strahlen
            Vector3 o = _scene.Origin;
            Color sun = _lights.MainColor;
            Vector3 sd = _lights.SunDirection;
            Color amb = (RenderSettings.ambientSkyColor.linear + RenderSettings.ambientEquatorColor.linear + RenderSettings.ambientGroundColor.linear) / 3f;
            if (!_passSet) { cmd.SetRayTracingShaderPass(_rts, "AshveilPT"); _passSet = true; }
            cmd.SetRayTracingAccelerationStructure(_rts, Ids.Scene, _scene.Structure);
            cmd.SetRayTracingBufferParam(_rts, Ids.Rays, _rays);
            cmd.SetRayTracingBufferParam(_rts, Ids.Lights, _lights.Lights);
            cmd.SetRayTracingBufferParam(_rts, Ids.State, _state);
            cmd.SetRayTracingTextureParam(_rts, Ids.Irradiance, _irr);
            cmd.SetRayTracingTextureParam(_rts, Ids.Distance, _dist);
            cmd.SetRayTracingTextureParam(_rts, Ids.EnvMap, _env.Map);
            cmd.SetRayTracingVectorParam(_rts, Ids.EnvParams, _env.Params(_envSettings));
            cmd.SetRayTracingVectorParam(_rts, Ids.EnvUniform, new Vector4(1, 1, 1, _env.Power));
            cmd.SetRayTracingVectorParam(_rts, Ids.LightCounts, new Vector4(_lights.LocalCount, 0, 0, 0));
            cmd.SetRayTracingVectorParam(_rts, Ids.Grid, GridVector);
            cmd.SetRayTracingVectorParam(_rts, Ids.Cascade0, _cascade[0]);
            cmd.SetRayTracingVectorParam(_rts, Ids.Cascade1, _cascade[1]);
            cmd.SetRayTracingVectorParam(_rts, Ids.Bias, new Vector4(normalBias, viewBias, 1f, edgeBlend));   // Rueckkopplung ohne Staerke
            cmd.SetRayTracingMatrixParam(_rts, Ids.Rotation, _rotation);
            cmd.SetRayTracingVectorParam(_rts, Ids.Origin, new Vector4(o.x, o.y, o.z, 0));
            cmd.SetRayTracingVectorParam(_rts, Ids.Update, update);
            bool sunOn = _lights.MainLight != null && sun.maxColorComponent > 1e-5f;
            cmd.SetRayTracingVectorParam(_rts, Ids.SunIrr, new Vector4(sun.r, sun.g, sun.b, sunOn ? 1f : 0f));
            cmd.SetRayTracingVectorParam(_rts, Ids.SunDir, new Vector4(sd.x, sd.y, sd.z, 0));
            cmd.SetRayTracingVectorParam(_rts, Ids.Misc, new Vector4(0, skyStrength * _skyScale, bounceStrength, _frame & 0xFFFFFF));
            cmd.SetRayTracingVectorParam(_rts, Ids.Fallback, new Vector4(amb.r, amb.g, amb.b, 0) * fallbackStrength);
            cmd.DispatchRays(_rts, "AshGIRayGen", (uint)rays, (uint)probes, 1);

            // Einmischen
            cmd.SetComputeVectorParam(_cs, Ids.Grid, GridVector);
            cmd.SetComputeVectorParam(_cs, Ids.Cascade0, _cascade[0]);
            cmd.SetComputeVectorParam(_cs, Ids.Cascade1, _cascade[1]);
            cmd.SetComputeVectorParam(_cs, Ids.Update, update);
            cmd.SetComputeMatrixParam(_cs, Ids.Rotation, _rotation);
            cmd.SetComputeVectorParam(_cs, Ids.BlendParams, new Vector4(hysteresis, distanceHysteresis, backfaceThreshold, 32f));
            cmd.SetComputeBufferParam(_cs, _kBlend, Ids.RaysIn, _rays);
            cmd.SetComputeBufferParam(_cs, _kBlend, Ids.StateRW, _state);
            cmd.SetComputeTextureParam(_cs, _kBlend, Ids.IrradianceRW, _irr);
            cmd.SetComputeTextureParam(_cs, _kBlend, Ids.DistanceRW, _dist);
            cmd.DispatchCompute(_cs, _kBlend, probes, 1, 1);

            if (built) _scene.UpdateMemoryStat();

            Stats.ProbesPerFrame = probes;
            Stats.TotalProbes = total;
            Stats.Rays = rays;
            Stats.Instances = _scene.ActiveInstances;
            Stats.Triangles = _scene.TriangleCount;
            Stats.RtasBytes = _scene.MemoryBytes;
            Stats.LocalLights = _lights.LocalCount;
            Stats.Updates++;
        }

        /// <summary>Gleichverteilte Drehung (Shoemake), eigener Zufall – beruehrt UnityEngine.Random des Spiels nicht.</summary>
        private Quaternion RandomRotation()
        {
            double u1 = _rng.NextDouble(), u2 = _rng.NextDouble(), u3 = _rng.NextDouble();
            double a = Math.Sqrt(1 - u1), b = Math.Sqrt(u1);
            return new Quaternion((float)(a * Math.Sin(2 * Math.PI * u2)), (float)(a * Math.Cos(2 * Math.PI * u2)),
                                  (float)(b * Math.Sin(2 * Math.PI * u3)), (float)(b * Math.Cos(2 * Math.PI * u3)));
        }

        // ------------------------------------------------------------------ Vergleich

        /// <summary>
        /// Zwei Bilder derselben Einstellung: klassisches Umgebungslicht und dynamisches GI (PNG, volle Aufloesung).
        /// Rueckgabe ueber done: Pfade und mittlere Helligkeit beider Bilder.
        /// </summary>
        public System.Collections.IEnumerator CaptureComparison(string pathPrefix, Action<string> done, bool fixedView = false)
        {
            bool keep = bypassScreen;
            GIShotPose pose = null;
            Camera cam = Camera.main;
            if (fixedView && cam != null)
            {
                // Feste, ueberschauende Kamera: 14 m hinter/7 m ueber dem Blickpunkt, damit beide Bilder genau gleich sind
                Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(cam.transform.up, Vector3.up);
                fwd.Normalize();
                Vector3 focus = cam.transform.position;
                if (Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore))
                    focus = hit.point;
                Vector3 eye = focus - fwd * 14f + Vector3.up * 7f;
                pose = cam.gameObject.AddComponent<GIShotPose>();
                pose.Position = eye;
                pose.Rotation = Quaternion.LookRotation((focus + Vector3.up * 1.2f) - eye, Vector3.up);
                for (int i = 0; i < 30; i++) yield return null;   // Proben am neuen Ort einschwingen lassen
            }
            var lines = new System.Text.StringBuilder();
            for (int pass = 0; pass < 2; pass++)
            {
                bypassScreen = pass == 0;
                for (int i = 0; i < 4; i++) yield return null;
                yield return new WaitForEndOfFrame();
                Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                string file = pathPrefix + (pass == 0 ? "_klassisch.png" : "_dynamischesGI.png");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(file)));
                System.IO.File.WriteAllBytes(file, shot.EncodeToPNG());
                Color32[] px = shot.GetPixels32();
                double sum = 0;
                for (int i = 0; i < px.Length; i += 7) sum += 0.2126 * px[i].r + 0.7152 * px[i].g + 0.0722 * px[i].b;
                lines.Append($"{file} ({shot.width}×{shot.height}, mittlere Helligkeit {sum / (px.Length / 7.0):0.0}/255)\n");
                Destroy(shot);
            }
            bypassScreen = keep;
            if (pose != null) Destroy(pose);
            done?.Invoke(lines.ToString());
        }

        // ------------------------------------------------------------------ Anzeige

        // Diagnose: lange Bilder mit dem, was das GI in der Naehe getan hat (Logs/Ashveil/GI/Spitzen.txt)
        private int _builtFrame = -10, _lastAdds;
        private void LogSpike(float lateMs)
        {
            float dt = Time.unscaledDeltaTime * 1000f;
            int adds = _scene != null ? _scene.IncrementalAdds : 0;
            if (dt > 100f && Time.frameCount > 30)
            {
                try
                {
                    System.IO.Directory.CreateDirectory("Logs/Ashveil/GI");
                    System.IO.File.AppendAllText("Logs/Ashveil/GI/Spitzen.txt",
                        $"{System.DateTime.Now:HH:mm:ss} Bild {Time.frameCount}: {dt:0} ms · GI-LateUpdate {lateMs:0.0} ms · Neubau vor {Time.frameCount - _builtFrame} Bildern · " +
                        $"neu aufgenommen {adds - _lastAdds} · Instanzen {_scene?.ActiveInstances} · Aufnahmen {Stats.Collects}\n");
                }
                catch { }
            }
            _lastAdds = adds;
        }

        private int C(int i) => _scene != null ? _scene.CategoryCounts[i] : 0;
        private int D(int i) => _scene != null ? _scene.DistanceCounts[i] : 0;

        private void OnGUI()
        {
            if (!showHud || !_ready) return;
            GUI.depth = -50;
            var r = new Rect(12, 12, 600, 190);
            GUI.Box(r, GUIContent.none);
            GUI.Label(new Rect(r.x + 8, r.y + 4, r.width - 16, r.height - 8),
                $"<b>Dynamisches GI</b>  Proben {Stats.GpuMs:0.00} ms + Bild {Stats.ScreenGpuMs:0.00} ms GPU\n" +
                $"Proben {Stats.TotalProbes} ({_gridUsed.x}×{_gridUsed.y}×{_gridUsed.z}×{Cascades}), je Bild {Stats.ProbesPerFrame} × {Stats.Rays} Strahlen\n" +
                $"Szene {Stats.Instances} Instanzen ({_scene?.SkippedSmall ?? 0} kleine weggelassen, {_scene?.DeferredCount ?? 0} ausser Reichweite), {Stats.Triangles / 1000} k Dreiecke, {Stats.RtasBytes / (1024 * 1024)} MB · Aufnahme {Stats.CollectMs:0} ms (×{Stats.Collects})\n" +
                $"Neubau RTAS {Stats.Builds} · CPU {Stats.CpuMs:0.00} ms (Spitze {Stats.CpuPeakMs:0.0}) · Bild {Stats.FrameMs:0.00} ms = {1000f / Mathf.Max(Stats.FrameMs, 0.01f):0} Bilder/s · Kostenmodus {costMode}\n" +
                $"Bewegt {_scene?.MovedCount ?? 0}× zuletzt {_scene?.LastMoved} · herausgenommen {_scene?.DroppedMovers ?? 0}\n" +
                $"Kategorien W{C(0)} G{C(1)} V{C(2)} F{C(3)} R{C(4)} T{C(5)} E{C(6)} · Abstand <60:{D(0)} <120:{D(1)} <250:{D(2)} weiter:{D(3)}\n" +
                $"Himmel ×{_skyScale:0.00} (Angleich {matchClassic:0.0}) · Lichter {Stats.LocalLights} · Kaskaden {spacingFine} m / {spacingCoarse} m · Staerke {intensity:0.00}",
                new GUIStyle(GUI.skin.label) { richText = true, fontSize = 13 });
        }
    }

    /// <summary>Haelt die Kamera fuer Vergleichsbilder fest (laeuft nach jeder Kamerasteuerung).</summary>
    [DefaultExecutionOrder(32000)]
    internal sealed class GIShotPose : MonoBehaviour
    {
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.identity;
        private void LateUpdate() => transform.SetPositionAndRotation(Position, Rotation);
        private void OnPreCull() => transform.SetPositionAndRotation(Position, Rotation);
    }

    public sealed class GIStats
    {
        public float GpuMs, ScreenGpuMs, CollectMs, CpuMs, CpuPeakMs, FrameMs;
        public int ProbesPerFrame, TotalProbes, Rays, Instances, LocalLights, Collects, Updates, Builds;
        public long Triangles;
        public ulong RtasBytes;
    }

    /// <summary>
    /// Render-Graph: (1) Proben auffrischen (unsicherer Pass, Raytracing), (2) Bildpass Tiefe+Normale → indirektes Licht
    /// je Pixel, (3) an URP als Screen-Space-Irradiance uebergeben – die Opak-Pass-Shader nehmen es als bakedGI.
    /// </summary>
    internal sealed class AshveilGIPass : ScriptableRenderPass
    {
        private readonly AshveilDynamicGI _gi;
        private readonly ProfilingSampler _screenSampler = new ProfilingSampler("Ashveil GI Bild") { enableRecording = true };
        internal float ScreenGpuMs => _screenSampler.gpuElapsedTime;

        private sealed class UpdateData { public AshveilDynamicGI Gi; }
        private sealed class ScreenData { public Material Material; }

        public AshveilGIPass(AshveilDynamicGI gi)
        {
            _gi = gi;
            renderPassEvent = RenderPassEvent.AfterRenderingPrePasses + 1;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var res = frameData.Get<UniversalResourceData>();

            if (_gi.WantsUpdate && _gi.costMode != 2)
            {
                using (var builder = renderGraph.AddUnsafePass<UpdateData>("Ashveil GI Proben", out UpdateData data, _gi.Sampler))
                {
                    data.Gi = _gi;
                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc((UpdateData d, UnsafeGraphContext ctx) =>
                        d.Gi.UpdateProbes(CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd)));
                }
            }

            if (_gi.bypassScreen || _gi.costMode == 1 || _gi.costMode == 3) { AshveilDynamicGI.LastSkipReason = "Bildpass abgeschaltet"; return; }
            if (!res.cameraDepthTexture.IsValid() || !res.cameraNormalsTexture.IsValid())
            {
                AshveilDynamicGI.LastSkipReason = !res.cameraDepthTexture.IsValid() ? "keine Tiefentextur" : "keine Normalentextur";
                return;
            }
            TextureDesc desc = renderGraph.GetTextureDesc(res.cameraDepthTexture);
            desc.name = "_AshveilGIScreenIrradiance";
            desc.format = GraphicsFormat.R16G16B16A16_SFloat;
            desc.msaaSamples = MSAASamples.None;
            desc.bindTextureMS = false;
            desc.clearBuffer = false;
            desc.enableRandomWrite = false;
            desc.filterMode = FilterMode.Point;
            TextureHandle irr = renderGraph.CreateTexture(desc);

            using (var builder = renderGraph.AddRasterRenderPass<ScreenData>("Ashveil GI Bild", out ScreenData data, _screenSampler))
            {
                data.Material = _gi.ScreenMaterial;
                builder.SetRenderAttachment(irr, 0, AccessFlags.WriteAll);
                builder.UseTexture(res.cameraDepthTexture, AccessFlags.Read);
                builder.UseTexture(res.cameraNormalsTexture, AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((ScreenData d, RasterGraphContext ctx) =>
                    Blitter.BlitTexture(ctx.cmd, new Vector4(1, 1, 0, 0), d.Material, 0));
            }
            AshveilDynamicGI.PublishIrradiance(res, irr);
        }
    }
}
