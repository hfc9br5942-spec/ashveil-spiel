using System.Collections.Generic;
using System.Reflection;
using Ashveil.Core.Settings;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Ashveil.Rendering
{
    /// <summary>
    /// Wendet <see cref="GraphicsSettingsStore.Current"/> an: URP-Einstellungen (Schatten, Kantenglaettung, Skalierung,
    /// Lichter), Qualitaetswerte (LOD, Texturen), Gelaende (Gras, Detail), Kamera (Sichtweite, AA), Nachbearbeitung
    /// und Ascheflocken. Entsteht automatisch beim Spielstart und bleibt ueber Szenenwechsel erhalten.
    ///
    /// Im Editor aendert Play-Mode das URP-Asset und die Qualitaetseinstellungen des Projekts – deshalb merkt sich der
    /// Applier die Ausgangswerte und stellt sie beim Beenden wieder her (Projektdateien bleiben unveraendert).
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public sealed class GraphicsApplier : MonoBehaviour
    {
        public static GraphicsApplier Instance { get; private set; }

        /// <summary>Zuletzt angewendet (fuer Tests): Kurzbeschreibung der wichtigsten Werte.</summary>
        public string LastSummary { get; private set; } = "";

        public int ApplyCount { get; private set; }

        private UniversalRenderPipelineAsset _urp;
        private readonly Dictionary<string, object> _urpBackup = new Dictionary<string, object>();
        private float _lodBias;
        private int _mipLimit;
        private AnisotropicFiltering _aniso;
        private SkinWeights _skin;
        private int _vSync;
        private int _targetFps;
        private bool _backedUp;

        private Camera _camera;
        private float _cameraCheck;
        private readonly Dictionary<ParticleSystem, int> _particleBase = new Dictionary<ParticleSystem, int>();
        private readonly Dictionary<ParticleSystem, float> _emissionBase = new Dictionary<ParticleSystem, float>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("Grafikeinstellungen");
            DontDestroyOnLoad(go);
            go.AddComponent<GraphicsApplier>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            _urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Backup();
            GraphicsSettingsStore.Changed += OnChanged;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Apply(GraphicsSettingsStore.Current);
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            GraphicsSettingsStore.Changed -= OnChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Restore();
            Instance = null;
        }

        private void OnApplicationQuit() => Restore();

        private void OnChanged(GraphicsOptions o) => Apply(o);

        private void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            // Ausgangswerte NICHT verwerfen: sonst wuerden bereits skalierte Partikel bei jedem (additiven) Laden erneut
            // skaliert. Nur Eintraege zerstoerter Systeme entfernen.
            PruneParticles();
            if (m == LoadSceneMode.Additive)
            {
                // Nachgeladene Region: nur Gelaende, Partikel und Nachbearbeitung einstellen – die Render-Pipeline bleibt
                // (ein Neusetzen kostet unnoetig einen Frame-Ausreisser beim Streaming).
                GraphicsOptions o = GraphicsSettingsStore.Current;
                ApplyTerrain(o);
                ApplyParticles(o);
                ApplyPostProcessing(o);
                return;
            }
            Apply(GraphicsSettingsStore.Current);
        }

        private void PruneParticles()
        {
            var dead = new List<ParticleSystem>();
            foreach (ParticleSystem ps in _particleBase.Keys) if (ps == null) dead.Add(ps);
            foreach (ParticleSystem ps in dead)
            {
                _particleBase.Remove(ps);
                _emissionBase.Remove(ps);
            }
        }

        /// <summary>
        /// Ausgabegroesse, nach der sich "SMAA hoch + MSAA auto" richtet. Tests koennen hier z. B. 7680 x 4320 eintragen,
        /// um 8K zu messen, ohne einen 8K-Bildschirm zu haben.
        /// </summary>
        public static Vector2Int? OutputSizeOverride;

        private Vector2Int _lastScreen;

        private Vector2Int OutputSize => OutputSizeOverride ?? new Vector2Int(Screen.width, Screen.height);

        private void Update()
        {
            // Kamera kann wechseln (Spielstart, Charaktervorschau ist eigene Kamera und bleibt unberuehrt)
            _cameraCheck -= Time.unscaledDeltaTime;
            if (_cameraCheck > 0f) return;
            _cameraCheck = 1f;
            // Fenstergroesse/Aufloesung geaendert: MSAA "auto" haengt davon ab
            Vector2Int size = OutputSize;
            if (size != _lastScreen)
            {
                _lastScreen = size;
                ApplyPipeline(GraphicsSettingsStore.Current);
                ApplyCamera(GraphicsSettingsStore.Current);
            }
            if (Camera.main != _camera)
            {
                _camera = Camera.main;
                ApplyCamera(GraphicsSettingsStore.Current);
                ApplyParticles(GraphicsSettingsStore.Current);
                ApplyTerrain(GraphicsSettingsStore.Current);
                ApplyPostProcessing(GraphicsSettingsStore.Current);
            }
        }

        // ================================================================== Anwenden

        public void Apply(GraphicsOptions o)
        {
            if (o == null) return;
            ApplyCount++;

            // Anzeige
            QualitySettings.vSyncCount = o.vSync ? 1 : 0;
            int fps = GraphicsCatalog.FrameLimits[o.frameLimit];
            Application.targetFrameRate = fps > 0 ? fps : -1;
            if (!Application.isEditor) ApplyDisplay(o);

            // Qualitaet
            QualitySettings.lodBias = GraphicsCatalog.LodBias[o.viewDistance];
            QualitySettings.globalTextureMipmapLimit = o.textures == 0 ? 1 : 0;
            QualitySettings.anisotropicFiltering = o.textures == 0 ? AnisotropicFiltering.Enable : AnisotropicFiltering.ForceEnable;
            QualitySettings.skinWeights = o.figureDetail == 0 ? SkinWeights.TwoBones : SkinWeights.FourBones;
            // Episch: 16-fache anisotrope Filterung fuer alle Texturen (flache Blicke auf Boden und Wege in der Ferne)
            if (o.terrainDetail >= 3) Texture.SetGlobalAnisotropicFilteringLimits(16, 16);
            else Texture.SetGlobalAnisotropicFilteringLimits(9, 16);

            ApplyPipeline(o);
            ApplyCamera(o);
            ApplyTerrain(o);
            ApplyPostProcessing(o);
            ApplyParticles(o);
            // Dynamic Sky: Darstellungsstufe (Zustand des Himmels bleibt gleich) – Niedrig eine Wolkenschicht, Hoch mit Details
            Sky.SkyDirector.Quality = o.postProcessing == 0 ? Sky.SkyQuality.Low : o.shadows >= 4 ? Sky.SkyQuality.High : Sky.SkyQuality.Medium;
            // Globale Beleuchtung: dynamisch nur mit Raytracing-Faehigkeit (sonst bleibt still das klassische Umgebungslicht)
            GI.AshveilDynamicGI.Quality = o.shadows >= 5 ? 2 : o.shadows >= 4 ? 1 : 0;
            GI.AshveilDynamicGI.SetEnabled(o.globalIllumination >= 1);
            if (GI.AshveilDynamicGI.Instance != null) GI.AshveilDynamicGI.Instance.matchClassic = o.globalIllumination == 2 ? 0f : 1f;

            LastSummary = $"Stufe {GraphicsCatalog.TierNames[(int)o.tier]} · Schatten {GraphicsCatalog.ShadowDistance[o.shadows]:0} m/" +
                          $"{GraphicsCatalog.ShadowResolution[o.shadows]} · Gras {GraphicsCatalog.DetailDistance[o.vegetation]:0} m · " +
                          $"Sicht {GraphicsCatalog.FarClip[o.viewDistance]:0} m · Skalierung {GraphicsCatalog.RenderScales[o.renderScale]:P0} · " +
                          $"Figuren {GraphicsCatalog.FullDetailFigures[o.figureDetail]} · MSAA {GraphicsCatalog.MsaaFor(o, OutputSize.x, OutputSize.y)}x · GI {(GI.AshveilDynamicGI.IsRunning ? "dynamisch" : "klassisch")}";
        }

        private void ApplyPipeline(GraphicsOptions o)
        {
            if (_urp == null) return;
            bool shadowsOn = o.shadows > 0;
            Set("shadowDistance", "m_ShadowDistance", shadowsOn ? GraphicsCatalog.ShadowDistance[o.shadows] : 0f);
            Set("mainLightShadowmapResolution", "m_MainLightShadowmapResolution", SupportedShadowResolution(GraphicsCatalog.ShadowResolution[o.shadows]));
            Set("shadowCascadeCount", "m_ShadowCascadeCount", GraphicsCatalog.ShadowCascades[o.shadows]);
            Set(null, "m_MainLightShadowsSupported", shadowsOn);
            Set(null, "m_SoftShadowsSupported", GraphicsCatalog.SoftShadows[o.shadows]);
            Set(null, "m_AdditionalLightShadowsSupported", GraphicsCatalog.LightShadows[o.shadows] && o.lights >= 1);
            Set("additionalLightsShadowmapResolution", "m_AdditionalLightsShadowmapResolution", SupportedShadowResolution(GraphicsCatalog.AdditionalShadowResolution[o.lights]));
            Set("maxAdditionalLightsCount", "m_AdditionalLightsPerObjectLimit", GraphicsCatalog.AdditionalLights[o.lights]);
            Vector2Int outSize = OutputSize;
            _lastScreen = outSize;
            Set("msaaSampleCount", "m_MSAA", GraphicsCatalog.MsaaFor(o, outSize.x, outSize.y));
            float scale = GraphicsCatalog.RenderScales[o.renderScale];
            Set("renderScale", "m_RenderScale", scale);
            Set("upscalingFilter", "m_UpscalingFilter", scale < 0.999f ? UpscalingFilterSelection.FSR : UpscalingFilterSelection.Auto);
        }

        /// <summary>
        /// Visual Clarity Gate 02.10.2026: Anzeigemodus UND Aufloesung setzen. Vorher schaltete "Vollbild" nur den Modus um –
        /// die Fenstergroesse der letzten Sitzung blieb (z. B. 960 × 540 eines Test-Clients) und wurde auf 4K hochgezogen.
        /// Jetzt: Standard = native Desktopaufloesung. Start-Argumente (-screen-width/-height/-fullscreen) haben Vorrang,
        /// damit Test-Clients ihre Fenstergroesse behalten.
        /// </summary>
        private static void ApplyDisplay(GraphicsOptions o)
        {
            if (CommandLineControlsDisplay) return;
            Display d = Display.main;
            int nw = d.systemWidth > 0 ? d.systemWidth : Screen.currentResolution.width;
            int nh = d.systemHeight > 0 ? d.systemHeight : Screen.currentResolution.height;
            FullScreenMode mode = o.displayMode == 2 ? FullScreenMode.ExclusiveFullScreen
                : o.displayMode == 1 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            int w = o.resolutionWidth, h = o.resolutionHeight;
            bool chosen = w > 0 && h > 0 && w <= nw && h <= nh;
            if (!chosen)
            {
                if (mode == FullScreenMode.Windowed)
                {
                    // Fenster ohne feste Wahl: vorhandene Groesse behalten, solange sie sinnvoll ist (der Spieler darf ziehen)
                    bool keep = Screen.fullScreenMode == FullScreenMode.Windowed && Screen.width >= 1024 && Screen.width <= nw && Screen.height <= nh;
                    w = keep ? Screen.width : Mathf.RoundToInt(nw * 0.75f);
                    h = keep ? Screen.height : Mathf.RoundToInt(nh * 0.75f);
                }
                else { w = nw; h = nh; }
            }
            bool same = Screen.width == w && Screen.height == h && Screen.fullScreenMode == mode;
            if (!same) Screen.SetResolution(w, h, mode);
            // Immer protokollieren (Support/Smoke-Test), auch wenn nichts umgestellt werden musste
            Debug.Log($"[Ashveil] Anzeige: {mode} {w}×{h} (Desktop {nw}×{nh}{(chosen ? ", gewaehlt" : ", nativ")}){(same ? " – unveraendert" : "")}.");
        }

        private static bool? _cmdDisplay;

        private static bool CommandLineControlsDisplay
        {
            get
            {
                if (_cmdDisplay.HasValue) return _cmdDisplay.Value;
                bool found = false;
                foreach (string a in System.Environment.GetCommandLineArgs())
                    if (a.Equals("-screen-width", System.StringComparison.OrdinalIgnoreCase) || a.Equals("-screen-height", System.StringComparison.OrdinalIgnoreCase) ||
                        a.Equals("-screen-fullscreen", System.StringComparison.OrdinalIgnoreCase) || a.Equals("-popupwindow", System.StringComparison.OrdinalIgnoreCase) ||
                        a.Equals("-window-mode", System.StringComparison.OrdinalIgnoreCase))
                        found = true;
                _cmdDisplay = found;
                return found;
            }
        }

        private void ApplyCamera(GraphicsOptions o)
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            _camera = cam;
            cam.farClipPlane = GraphicsCatalog.FarClip[o.viewDistance];
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null) return;
            switch (o.antiAliasing)
            {
                case 0: data.antialiasing = AntialiasingMode.None; break;
                case 1: data.antialiasing = AntialiasingMode.FastApproximateAntialiasing; break;
                default:
                    // Laeuft schon MSAA, glaettet SMAA nur zusaetzlich nach und weicht Figuren/Texturen auf (27.09.: scharf)
                    if (GraphicsCatalog.MsaaFor(o, OutputSize.x, OutputSize.y) > 1)
                    {
                        data.antialiasing = AntialiasingMode.None;
                        break;
                    }
                    data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                    data.antialiasingQuality = o.antiAliasing >= 3 ? AntialiasingQuality.High : AntialiasingQuality.Medium;
                    break;
            }
            data.renderShadows = o.shadows > 0;
        }

        private static void ApplyTerrain(GraphicsOptions o)
        {
            foreach (Terrain t in Terrain.activeTerrains)
            {
                if (t == null) continue;
                t.detailObjectDistance = GraphicsCatalog.DetailDistance[o.vegetation];
                t.detailObjectDensity = GraphicsCatalog.DetailDensity[o.vegetation];
                t.heightmapPixelError = GraphicsCatalog.PixelError[o.terrainDetail];
                t.basemapDistance = GraphicsCatalog.BasemapDistance[o.viewDistance];
                t.treeDistance = GraphicsCatalog.FarClip[o.viewDistance];
                t.shadowCastingMode = o.shadows >= 3 ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
        }

        private static void ApplyPostProcessing(GraphicsOptions o)
        {
            bool full = o.postProcessing == 1;
            foreach (Volume v in FindObjectsByType<Volume>(FindObjectsInactive.Exclude))
            {
                if (v == null || !v.isGlobal || v.sharedProfile == null) continue;
                VolumeProfile p = v.profile; // Laufzeit-Kopie – das Profil-Asset bleibt unveraendert
                if (p.TryGet(out Bloom bloom))
                {
                    bloom.active = true;
                    bloom.highQualityFiltering.Override(full);
                    bloom.maxIterations.Override(full ? 6 : 4);
                }
                // Filmkorn aus (Wunsch Max 27.09.: Figuren gestochen scharf) – das Rauschen lag auch auf Gesichtern
                if (p.TryGet(out FilmGrain grain)) grain.active = false;
                if (p.TryGet(out SplitToning split)) split.active = full;
            }
        }

        private void ApplyParticles(GraphicsOptions o)
        {
            float share = GraphicsCatalog.ParticleShare[o.particles];
            foreach (ParticleSystem ps in FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude))
            {
                if (ps == null || !ps.name.StartsWith("Ascheflocken")) continue;
                var main = ps.main;
                var emission = ps.emission;
                if (!_particleBase.ContainsKey(ps))
                {
                    _particleBase[ps] = main.maxParticles;
                    _emissionBase[ps] = emission.rateOverTimeMultiplier;
                }
                main.maxParticles = Mathf.Max(50, Mathf.RoundToInt(_particleBase[ps] * share));
                emission.rateOverTimeMultiplier = _emissionBase[ps] * share;
            }
        }

        /// <summary>
        /// Schattenaufloesung, die die Render-Pipeline wirklich anbietet und die Grafikkarte darstellen kann (8192 nur, wenn
        /// URP den Wert kennt und Texturen dieser Groesse moeglich sind) – sonst die naechstkleinere.
        /// </summary>
        public static int SupportedShadowResolution(int wanted)
        {
            int res = wanted;
            while (res > 256)
            {
                bool gpu = SystemInfo.maxTextureSize >= res;
                FieldInfo f = Field("m_MainLightShadowmapResolution");
                bool urp = f == null || !f.FieldType.IsEnum || System.Enum.IsDefined(f.FieldType, res);
                if (gpu && urp) return res;
                res /= 2;
            }
            return res;
        }

        /// <summary>MSAA, das "Auto" fuer die aktuelle Ausgabegroesse waehlt (fuer Tests).</summary>
        public int ExpectedMsaa(GraphicsOptions o) => GraphicsCatalog.MsaaFor(o, OutputSize.x, OutputSize.y);

        public int PipelineShadowResolution => _urp != null ? _urp.mainLightShadowmapResolution : -1;

        // ================================================================== Sichern und Zuruecksetzen (Editor)

        private static readonly (string prop, string field)[] UrpMembers =
        {
            ("shadowDistance", "m_ShadowDistance"), ("mainLightShadowmapResolution", "m_MainLightShadowmapResolution"),
            ("shadowCascadeCount", "m_ShadowCascadeCount"), (null, "m_MainLightShadowsSupported"), (null, "m_SoftShadowsSupported"),
            (null, "m_AdditionalLightShadowsSupported"), ("additionalLightsShadowmapResolution", "m_AdditionalLightsShadowmapResolution"),
            ("maxAdditionalLightsCount", "m_AdditionalLightsPerObjectLimit"), ("msaaSampleCount", "m_MSAA"),
            ("renderScale", "m_RenderScale"), ("upscalingFilter", "m_UpscalingFilter")
        };

        private void Backup()
        {
            _lodBias = QualitySettings.lodBias;
            _mipLimit = QualitySettings.globalTextureMipmapLimit;
            _aniso = QualitySettings.anisotropicFiltering;
            _skin = QualitySettings.skinWeights;
            _vSync = QualitySettings.vSyncCount;
            _targetFps = Application.targetFrameRate;
            if (_urp != null)
                foreach ((string prop, string field) in UrpMembers)
                {
                    FieldInfo f = Field(field);
                    if (f != null) _urpBackup[field] = f.GetValue(_urp);
                }
            _backedUp = true;
        }

        private void Restore()
        {
            if (!_backedUp || !Application.isEditor) return;
            _backedUp = false;
            QualitySettings.lodBias = _lodBias;
            QualitySettings.globalTextureMipmapLimit = _mipLimit;
            QualitySettings.anisotropicFiltering = _aniso;
            QualitySettings.skinWeights = _skin;
            QualitySettings.vSyncCount = _vSync;
            Application.targetFrameRate = _targetFps;
            if (_urp != null)
                foreach (KeyValuePair<string, object> kv in _urpBackup)
                    Field(kv.Key)?.SetValue(_urp, kv.Value);
        }

        // ================================================================== Reflexion (URP-Felder ohne oeffentlichen Setter)

        /// <summary>Diagnose (10.10.2026, Flacker-Probe v5): Hauptlicht-Schattenwerte direkt lesen und setzen – nur im Speicher,
        /// die Grafikwahl bleibt; der nächste Apply stellt die Stufe wieder her.</summary>
        public (float distance, int cascades, int resolution) ShadowRaw =>
            _urp == null ? (0f, 0, 0) : (_urp.shadowDistance, _urp.shadowCascadeCount, _urp.mainLightShadowmapResolution);

        public void ShadowRawSet(float distance, int cascades, int resolution)
        {
            Set("shadowDistance", "m_ShadowDistance", distance);
            Set("shadowCascadeCount", "m_ShadowCascadeCount", cascades);
            Set("mainLightShadowmapResolution", "m_MainLightShadowmapResolution", resolution);
        }

        private void Set(string property, string field, object value)
        {
            if (_urp == null) return;
            if (property != null)
            {
                PropertyInfo p = typeof(UniversalRenderPipelineAsset).GetProperty(property, BindingFlags.Instance | BindingFlags.Public);
                if (p != null && p.CanWrite)
                {
                    try
                    {
                        p.SetValue(_urp, Convert(value, p.PropertyType));
                        return;
                    }
                    catch (System.Exception) { /* Feld versuchen */ }
                }
            }
            FieldInfo f = Field(field);
            if (f == null) return;
            try { f.SetValue(_urp, Convert(value, f.FieldType)); }
            catch (System.Exception e) { Debug.LogWarning($"[Ashveil] Grafik: {field} nicht setzbar ({e.Message})"); }
        }

        private static FieldInfo Field(string name) =>
            typeof(UniversalRenderPipelineAsset).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private static object Convert(object value, System.Type target)
        {
            if (value == null || target.IsInstanceOfType(value)) return value;
            if (target.IsEnum) return System.Enum.ToObject(target, System.Convert.ToInt32(value));
            return System.Convert.ChangeType(value, target);
        }

        /// <summary>Tatsaechlich gesetzte URP-Werte (fuer Tests).</summary>
        public float PipelineShadowDistance => _urp != null ? _urp.shadowDistance : -1f;
        public float PipelineRenderScale => _urp != null ? _urp.renderScale : -1f;
        public int PipelineMsaa => _urp != null ? _urp.msaaSampleCount : -1;
    }
}
