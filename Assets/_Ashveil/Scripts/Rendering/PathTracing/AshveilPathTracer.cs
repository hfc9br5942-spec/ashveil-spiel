using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Ashveil.Rendering.PathTracing
{
    /// <summary>
    /// ASHVEIL Pathtracer – optionaler High-End-Renderpfad (Master-Plan). Fotomodus/Kino/Referenz, nie Voraussetzung
    /// fuers Spielen: ohne Raytracing-Faehigkeit bleibt er aus und der Rasterrenderer laeuft unveraendert.
    /// Nur Darstellung auf diesem Rechner – kein Netzwerkzustand, keine Sicht-/Treffer-/Gegnerlogik (§132–135).
    ///
    /// Ablauf je Bild: Szene/Lichter/Himmel als Momentaufnahme (bei "Welt einfrieren" nur einmal), Zustands-
    /// schluessel aus Versionen + Kamera → bei Aenderung Akkumulation neu; im Render-Pass RTAS bauen, Himmel backen,
    /// Strahlen verschiessen, adaptiv weiter nur wo noetig, Entrauschen, Aufloesung in die Kamerafarbe.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    public sealed class AshveilPathTracer : MonoBehaviour
    {
        public static AshveilPathTracer Instance { get; private set; }
        public static bool IsRunning => Instance != null && Instance.isActiveAndEnabled && Instance._ready;

        public PathTraceSettings settings = new PathTraceSettings();
        [Tooltip("Leer = Camera.main")]
        public Camera targetCamera;
        public bool showHud = true;
        [Tooltip("Bild in der Hoehe spiegeln (falls eine Plattform die Zeilen andersherum liefert)")]
        public bool flipY;

        /// <summary>Fertig: Probenziel oder Konvergenzziel erreicht (einmal je Akkumulation).</summary>
        public event Action Converged;

        public PathTraceStats Stats => _stats;

        private RayTracingShader _rts;
        private ComputeShader _denoise;
        private Material _resolve;
        private AshveilRTMaterialRegistry _materials;
        private AshveilRTSceneManager _scene;
        private AshveilRTLightRegistry _lights;
        private AshveilRTEnvironment _env;
        private readonly AshveilRTAccumulation _accum = new AshveilRTAccumulation();
        private GraphicsBuffer _counters;
        private GraphicsBuffer _dummyBuffer;
        private AshveilPathTracingPass _pass;
        private ProfilingSampler _sampler;
        private bool _ready;
        private bool _hasSnapshot;
        private long _stateKey;
        private bool _convergedRaised;
        private readonly PathTraceStats _stats = new PathTraceStats();
        private readonly uint[] _counterZero = new uint[8];
        private int _kPrepare, _kAtrous, _kFinish, _kAdaptive;
        private int _generation;

        // ------------------------------------------------------------------ Ein/Aus

        /// <summary>Pathtracer holen/erzeugen (einmal je Sitzung, ueberlebt Szenenwechsel).</summary>
        public static AshveilPathTracer Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("Ashveil Pathtracer");
            DontDestroyOnLoad(go);
            go.SetActive(false);
            var t = go.AddComponent<AshveilPathTracer>();
            Instance = t;
            return t;
        }

        /// <summary>Ein-/Ausschalten. Rueckgabe false, wenn das Geraet kein Raytracing kann (Grund im Log).</summary>
        public static bool SetEnabled(bool on)
        {
            if (on && !PathTracingCapability.IsSupported)
            {
                Debug.LogWarning("[Ashveil] Pathtracing " + PathTracingCapability.Describe());
                return false;
            }
            AshveilPathTracer t = on ? Ensure() : Instance;
            if (t != null) t.gameObject.SetActive(on);
            return true;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnEnable()
        {
            if (!PathTracingCapability.IsSupported)
            {
                Debug.LogWarning("[Ashveil] Pathtracing " + PathTracingCapability.Describe());
                enabled = false;
                return;
            }
            _rts = Resources.Load<RayTracingShader>("AshveilPathTracer");
            _denoise = Resources.Load<ComputeShader>("AshveilPTDenoise");
            Shader surface = Resources.Load<Shader>("AshveilPTSurface");
            Shader terrain = Resources.Load<Shader>("AshveilPTTerrain");
            Shader resolve = Resources.Load<Shader>("AshveilPTResolve");
            Shader bake = Resources.Load<Shader>("AshveilPTSkyBake");
            if (_rts == null || surface == null || resolve == null || _denoise == null)
            {
                Debug.LogError("[Ashveil] Pathtracing: Shader fehlen (Resources/AshveilPathTracer.raytrace, PTSurface, PTResolve, PTDenoise).");
                enabled = false;
                return;
            }
            _resolve = new Material(resolve) { hideFlags = HideFlags.DontSave };
            _materials = new AshveilRTMaterialRegistry(surface);
            _scene = new AshveilRTSceneManager(_materials, terrain);
            _lights = new AshveilRTLightRegistry();
            _env = new AshveilRTEnvironment(bake);
            _counters = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, 4);
            _dummyBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 16);
            _sampler = new ProfilingSampler("Ashveil Pathtracing") { enableRecording = true };
            _pass = new AshveilPathTracingPass(this, _sampler);
            _kPrepare = _denoise.FindKernel("AshPrepare");
            _kAtrous = _denoise.FindKernel("AshAtrous");
            _kFinish = _denoise.FindKernel("AshFinish");
            _kAdaptive = _denoise.FindKernel("AshAdaptive");
            _hasSnapshot = false;
            _stateKey = 0;
            _accum.Reset();
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            _ready = true;
            Debug.Log("[Ashveil] Pathtracing an: " + PathTracingCapability.Describe());
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            _ready = false;
            _scene?.Dispose(); _scene = null;
            _lights?.Dispose(); _lights = null;
            _env?.Dispose(); _env = null;
            _materials?.Dispose(); _materials = null;
            _accum.Release();
            _counters?.Release(); _counters = null;
            _dummyBuffer?.Release(); _dummyBuffer = null;
            _expBuf?.Release(); _expBuf = null;
            if (_expSmall != null) { _expSmall.Release(); Destroy(_expSmall); _expSmall = null; }
            if (_resolve != null) Destroy(_resolve);
            _hasSnapshot = false;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public Camera Camera => targetCamera != null ? targetCamera : Camera.main;

        /// <summary>Alles neu aufnehmen (Szene, Lichter, Himmel) und neu akkumulieren.</summary>
        public void Recapture()
        {
            _hasSnapshot = false;
            _fogCaptured = false;
            _scene?.MarkDirty();
            StartOver();
        }

        public void ResetAccumulation() => StartOver();

        private void StartOver()
        {
            _accum.Reset();
            _convergedRaised = false;
            _generation++;
            _stats.ActiveTiles = 0;
            _stats.ActiveTilesValid = false;
            _stats.ActiveShare = 1f;
            _stats.NaNSamples = 0;
            _stats.DenoisedFinal = false;
            _stats.Done = false;
            _stats.Samples = 0;
        }

        // ------------------------------------------------------------------ Momentaufnahme + Zustand

        private void LateUpdate()
        {
            if (!_ready) return;
            Camera cam = Camera;
            if (cam == null) return;
            Vector3 camPos = cam.transform.position;

            bool first = !_hasSnapshot;
            if (first || _scene.SceneDirty)
            {
                float t0 = Time.realtimeSinceStartup;
                _scene.Collect(camPos, settings.captureRadius);
                _lights.SetEmissive(_scene.EmissiveTriangles);
                _stats.CollectMs = (Time.realtimeSinceStartup - t0) * 1000f;
            }
            else if (!settings.freezeWorld)
            {
                _materials.Refresh();
                _scene.UpdateDynamic();
            }
            if (first || !settings.freezeWorld) _lights.Collect(camPos, _scene.Origin, settings);
            if (first || !settings.freezeWorld) _env.Check(settings);
            _hasSnapshot = true;

            long key = StateKey(cam);
            if (key != _stateKey)
            {
                _stateKey = key;
                StartOver();
            }
        }

        private long StateKey(Camera cam)
        {
            unchecked
            {
                long h = 1469598103934665603L;
                void Mix(int v) { h = (h ^ v) * 1099511628211L; }
                Mix(_scene.GeometryVersion);
                Mix(_materials.Version);
                Mix(_lights.Version);
                Mix(_env.Version);
                Transform t = cam.transform;
                Mix(t.position.GetHashCode());
                Mix(t.rotation.GetHashCode());
                Mix(cam.projectionMatrix.GetHashCode());
                Mix(cam.pixelWidth); Mix(cam.pixelHeight);
                Mix(settings.apertureRadius.GetHashCode());
                Mix(settings.focusDistance.GetHashCode());
                Mix(settings.maxBounces);
                Mix(settings.russianRouletteStart);
                Mix((int)settings.globalSeed);
                Mix(settings.fireflyClamp.GetHashCode());
                Mix(settings.environmentIntensity.GetHashCode());
                Mix((int)settings.environment);
                Mix(settings.uniformEnvironment.GetHashCode());
                int v = (int)settings.view;
                Mix(v >= 4 && v <= 8 ? v : 0);   // Diagnose-Ansichten aus den Hilfsbildern brauchen keinen Neustart
                Mix(flipY ? 1 : 0);
                Mix(settings.misMode);
                Mix(settings.filterGlossy.GetHashCode());
                Mix(settings.fog ? 1 : 0);
                Mix(settings.fogAnisotropy.GetHashCode());
                Mix(settings.fogScatterAlbedo.GetHashCode());
                if (settings.fog && !settings.freezeWorld) Mix(Shader.GetGlobalVector(Ids.WorldFogParams).GetHashCode());
                return h;
            }
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (!_ready || cam == null || cam != Camera) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null) return;
            data.scriptableRenderer.EnqueuePass(_pass);
        }

        // ------------------------------------------------------------------ Render-Pass

        private static class Ids
        {
            public static readonly int Scene = Shader.PropertyToID("_AshPTScene");
            public static readonly int Accum = Shader.PropertyToID("_AshPTAccum");
            public static readonly int Moments = Shader.PropertyToID("_AshPTMoments");
            public static readonly int Albedo = Shader.PropertyToID("_AshPTAlbedo");
            public static readonly int NormalDepth = Shader.PropertyToID("_AshPTNormalDepth");
            public static readonly int Counters = Shader.PropertyToID("_AshPTCounters");
            public static readonly int Active = Shader.PropertyToID("_AshPTActive");
            public static readonly int Lights = Shader.PropertyToID("_AshPTLights");
            public static readonly int Emissive = Shader.PropertyToID("_AshPTEmissive");
            public static readonly int EmissiveAlias = Shader.PropertyToID("_AshPTEmissiveAlias");
            public static readonly int Instances = Shader.PropertyToID("_AshPTInstances");
            public static readonly int EnvAlias = Shader.PropertyToID("_AshPTEnvAlias");
            public static readonly int EnvMap = Shader.PropertyToID("_AshPTEnvMap");
            public static readonly int SunDir = Shader.PropertyToID("_AshPTSunDir");
            public static readonly int SunRadiance = Shader.PropertyToID("_AshPTSunRadiance");
            public static readonly int EnvParams = Shader.PropertyToID("_AshPTEnvParams");
            public static readonly int EnvUniform = Shader.PropertyToID("_AshPTEnvUniform");
            public static readonly int LightCounts = Shader.PropertyToID("_AshPTLightCounts");
            public static readonly int EmissiveCenter = Shader.PropertyToID("_AshPTEmissiveCenter");
            public static readonly int CamToWorld = Shader.PropertyToID("_AshPTCamToWorld");
            public static readonly int InvProj = Shader.PropertyToID("_AshPTInvProj");
            public static readonly int CamPos = Shader.PropertyToID("_AshPTCamPos");
            public static readonly int CamForward = Shader.PropertyToID("_AshPTCamForward");
            public static readonly int Lens = Shader.PropertyToID("_AshPTLens");
            public static readonly int Size = Shader.PropertyToID("_AshPTSize");
            public static readonly int ClampParams = Shader.PropertyToID("_AshPTClampParams");
            public static readonly int MaxBounces = Shader.PropertyToID("_AshPTMaxBounces");
            public static readonly int RRStart = Shader.PropertyToID("_AshPTRRStart");
            public static readonly int Spp = Shader.PropertyToID("_AshPTSpp");
            public static readonly int ViewMode = Shader.PropertyToID("_AshPTViewMode");
            public static readonly int Seed = Shader.PropertyToID("_AshPTSeed");
            public static readonly int Reset = Shader.PropertyToID("_AshPTReset");
            public static readonly int Adaptive = Shader.PropertyToID("_AshPTAdaptive");
            public static readonly int Flip = Shader.PropertyToID("_AshPTFlip");
            public static readonly int MisMode = Shader.PropertyToID("_AshPTMisMode");
            public static readonly int FilterGlossy = Shader.PropertyToID("_AshPTFilterGlossy");
            public static readonly int Fog = Shader.PropertyToID("_AshPTFog");
            public static readonly int Fog2 = Shader.PropertyToID("_AshPTFog2");
            public static readonly int FogTint = Shader.PropertyToID("_AshPTFogTint");
            public static readonly int WorldFogParams = Shader.PropertyToID("_AshFogParams");
            public static readonly int WorldFogParams2 = Shader.PropertyToID("_AshFogParams2");
            public static readonly int WorldFogColor = Shader.PropertyToID("_AshFogColor");
            // Aufloesung/Entrauschen
            public static readonly int AccumTex = Shader.PropertyToID("_AshPTAccumTex");
            public static readonly int MomentsTex = Shader.PropertyToID("_AshPTMomentsTex");
            public static readonly int AlbedoTex = Shader.PropertyToID("_AshPTAlbedoTex");
            public static readonly int NormalDepthTex = Shader.PropertyToID("_AshPTNormalDepthTex");
            public static readonly int DenoisedTex = Shader.PropertyToID("_AshPTDenoisedTex");
            public static readonly int ResolveParams = Shader.PropertyToID("_AshPTResolveParams");
            public static readonly int SizeTex = Shader.PropertyToID("_AshPTSizeTex");
            public static readonly int ResolveParams2 = Shader.PropertyToID("_AshPTResolveParams2");
            public static readonly int ExpSrc = Shader.PropertyToID("_AshExpSrc");
            public static readonly int ExpOut = Shader.PropertyToID("_AshExpOut");
            public static readonly int ExpParams = Shader.PropertyToID("_AshExpParams");
            public static readonly int IrrA = Shader.PropertyToID("_AshIrrA");
            public static readonly int IrrB = Shader.PropertyToID("_AshIrrB");
            public static readonly int IrrIn = Shader.PropertyToID("_AshIrrIn");
            public static readonly int Guide = Shader.PropertyToID("_AshGuide");
            public static readonly int GuideIn = Shader.PropertyToID("_AshGuideIn");
            public static readonly int Denoised = Shader.PropertyToID("_AshDenoised");
            public static readonly int ActiveRW = Shader.PropertyToID("_AshActive");
            public static readonly int CountersCS = Shader.PropertyToID("_AshCounters");
            public static readonly int CsSize = Shader.PropertyToID("_AshSize");
            public static readonly int AtrousParams = Shader.PropertyToID("_AshAtrousParams");
            public static readonly int AdaptiveParams = Shader.PropertyToID("_AshAdaptiveParams");
            public static readonly int HeroRect = Shader.PropertyToID("_AshHeroRect");
        }

        /// <summary>Optionaler Hero-Bereich in Pixeln (Gesicht/Boss): strengeres Konvergenzziel, gleiche Beleuchtung (§71–72).</summary>
        public RectInt? HeroRect { get; set; }

        internal void Execute(CommandBuffer cmd, int w, int h, TextureHandle destination, TextureHandle raster)
        {
            Camera cam = Camera;
            if (cam == null || _scene == null || _scene.Structure == null) return;
            if (_accum.Ensure(w, h, settings.adaptiveTile)) StartOver();

            _env.Bake(cmd);
            bool built = false;
            if (_scene.NeedsBuild) { _scene.Build(cmd); built = true; }

            bool done = IsDone();
            if (!done)
            {
                bool reset = _accum.PendingReset;
                bool adaptive = settings.adaptive && !reset && _accum.SamplesTaken >= settings.adaptiveMinSamples;
                cmd.SetBufferData(_counters, _counterZero);
                if (adaptive) DispatchAdaptive(cmd, w, h);
                DispatchRays(cmd, cam, w, h, reset, adaptive);
                _accum.PendingReset = false;
                _accum.SamplesTaken += settings.samplesPerFrame;
                _accum.Frames++;
                int expectedTiles = ((w + _accum.Tile - 1) / _accum.Tile) * ((h + _accum.Tile - 1) / _accum.Tile);
                bool adaptiveNow = adaptive;
                int gen = _generation;
                cmd.RequestAsyncReadback(_counters, r => { if (gen == _generation) OnCounters(r, adaptiveNow, expectedTiles); });
            }

            bool denoise = settings.denoise != PathTraceDenoise.Aus && _accum.SamplesTaken > 0;
            if (denoise && (!done || !_stats.DenoisedFinal)) Denoise(cmd, w, h);
            _stats.DenoisedFinal = done && denoise;

            if (settings.matchGameExposure && _accum.SamplesTaken > 0) MatchExposure(cmd, w, h, raster);

            // Aufloesung in die Kamerafarbe
            RTHandle dst = destination;
            _resolve.SetTexture(Ids.AccumTex, _accum.Accum);
            _resolve.SetTexture(Ids.MomentsTex, _accum.Moments);
            _resolve.SetTexture(Ids.AlbedoTex, _accum.Albedo);
            _resolve.SetTexture(Ids.NormalDepthTex, _accum.NormalDepth);
            _resolve.SetTexture(Ids.DenoisedTex, denoise ? _accum.Denoised : _accum.Accum);
            _resolve.SetVector(Ids.ResolveParams, new Vector4((int)settings.view, Mathf.Max(settings.targetSamples, 64), 0.02f, denoise ? 1 : 0));
            _resolve.SetVector(Ids.SizeTex, new Vector4(w, h, 0, 0));
            float ev = settings.exposureEV + (settings.matchGameExposure ? _autoEv : 0f);
            _stats.ExposureEV = ev;
            _resolve.SetVector(Ids.ResolveParams2, new Vector4(Mathf.Pow(2f, ev), 0, 0, 0));
            cmd.SetRenderTarget(dst);
            Blitter.BlitTexture(cmd, _accum.Accum, new Vector4(1, 1, 0, 0), _resolve, 0);

            if (built) _scene.UpdateMemoryStat();
            _stats.GpuMs = _sampler.gpuElapsedTime;
            _stats.Samples = _accum.SamplesTaken;
            _stats.Frames = _accum.Frames;
            _stats.AgeSeconds = Time.realtimeSinceStartup - _accum.StartedAt;
            _stats.Instances = _scene.InstanceCount;
            _stats.Triangles = _scene.TriangleCount;
            _stats.RtasBytes = _scene.MemoryBytes;
            _stats.AccumBytes = _accum.Bytes;
            _stats.LocalLights = _lights.LocalCount;
            _stats.EmissiveTriangles = _lights.EmissiveCount;
            _stats.Materials = _materials.Count;
            _stats.Skinned = _scene.SkinnedCount;
            _stats.Terrains = _scene.TerrainCount;
            _stats.Details = _scene.DetailCount;
            _stats.Width = w; _stats.Height = h;
            _stats.Done = done;
            _stats.MainIsMoon = _lights.MainIsMoon;
            _stats.SunIrradiance = _lights.SunIrradianceLuminance;
            _stats.MainLight = _lights.MainLight != null ? _lights.MainLight.name : "-";
            _stats.EnvImportance = _env.AliasReady;
            if (done && !_convergedRaised)
            {
                _convergedRaised = true;
                Converged?.Invoke();
            }
        }

        private bool IsDone()
        {
            if (settings.targetSamples > 0 && _accum.SamplesTaken >= settings.targetSamples) return true;
            // Konvergenz: adaptive Maske meldet keine aktive Kachel mehr
            return settings.adaptive && settings.convergenceTarget > 0 && _accum.SamplesTaken >= settings.adaptiveMinSamples * 2 &&
                   _stats.ActiveTiles == 0 && _stats.ActiveTilesValid;
        }

        private void DispatchRays(CommandBuffer cmd, Camera cam, int w, int h, bool reset, bool adaptive)
        {
            Vector3 origin = _scene.Origin;
            Transform t = cam.transform;
            cmd.SetRayTracingShaderPass(_rts, "AshveilPT");
            cmd.SetRayTracingAccelerationStructure(_rts, Ids.Scene, _scene.Structure);
            cmd.SetRayTracingTextureParam(_rts, Ids.Accum, _accum.Accum);
            cmd.SetRayTracingTextureParam(_rts, Ids.Moments, _accum.Moments);
            cmd.SetRayTracingTextureParam(_rts, Ids.Albedo, _accum.Albedo);
            cmd.SetRayTracingTextureParam(_rts, Ids.NormalDepth, _accum.NormalDepth);
            cmd.SetRayTracingTextureParam(_rts, Ids.Active, _accum.Active);
            cmd.SetRayTracingTextureParam(_rts, Ids.EnvMap, _env.Map);
            cmd.SetRayTracingBufferParam(_rts, Ids.Counters, _counters);
            cmd.SetRayTracingBufferParam(_rts, Ids.Lights, _lights.Lights);
            cmd.SetRayTracingBufferParam(_rts, Ids.Emissive, _lights.Emissive);
            cmd.SetRayTracingBufferParam(_rts, Ids.EmissiveAlias, _lights.EmissiveAlias);
            cmd.SetRayTracingBufferParam(_rts, Ids.Instances, _scene.InstanceInfo != null ? _scene.InstanceInfo : _dummyBuffer);
            cmd.SetRayTracingBufferParam(_rts, Ids.EnvAlias, _env.Alias);

            Vector3 sd = _lights.SunDirection;
            Color sr = _lights.SunRadiance;
            cmd.SetRayTracingVectorParam(_rts, Ids.SunDir, new Vector4(sd.x, sd.y, sd.z, _lights.SunCosAngle));
            cmd.SetRayTracingVectorParam(_rts, Ids.SunRadiance, new Vector4(sr.r, sr.g, sr.b, _lights.SunVisible ? 1f : 0f));
            cmd.SetRayTracingVectorParam(_rts, Ids.EnvParams, _env.Params(settings));
            Color u = settings.uniformEnvironment.linear * settings.environmentIntensity;
            cmd.SetRayTracingVectorParam(_rts, Ids.EnvUniform, new Vector4(u.r, u.g, u.b, _env.Power * settings.environmentIntensity));
            cmd.SetRayTracingVectorParam(_rts, Ids.LightCounts, new Vector4(_lights.LocalCount, _lights.EmissiveCount, _lights.EmissivePower, _lights.SunIrradianceLuminance));
            cmd.SetRayTracingVectorParam(_rts, Ids.EmissiveCenter, _lights.EmissiveCenter);

            Matrix4x4 camToWorld = Matrix4x4.TRS(t.position - origin, t.rotation, Vector3.one) * Matrix4x4.Scale(new Vector3(1, 1, -1));
            cmd.SetRayTracingMatrixParam(_rts, Ids.CamToWorld, camToWorld);
            cmd.SetRayTracingMatrixParam(_rts, Ids.InvProj, cam.projectionMatrix.inverse);
            float spread = Mathf.Atan(2f * Mathf.Tan(0.5f * cam.fieldOfView * Mathf.Deg2Rad) / h);
            Vector3 p = t.position - origin, f = t.forward;
            cmd.SetRayTracingVectorParam(_rts, Ids.CamPos, new Vector4(p.x, p.y, p.z, spread));
            cmd.SetRayTracingVectorParam(_rts, Ids.CamForward, new Vector4(f.x, f.y, f.z, 0));
            cmd.SetRayTracingVectorParam(_rts, Ids.Lens, new Vector4(Mathf.Max(0, settings.apertureRadius), Mathf.Max(0.05f, settings.focusDistance), 0, 0));
            cmd.SetRayTracingVectorParam(_rts, Ids.Size, new Vector4(w, h, 0, 0));
            cmd.SetRayTracingVectorParam(_rts, Ids.ClampParams, new Vector4(settings.fireflyClamp, _accum.Tile, 0, 0));
            cmd.SetRayTracingIntParam(_rts, Ids.MaxBounces, settings.maxBounces);
            cmd.SetRayTracingIntParam(_rts, Ids.RRStart, settings.russianRouletteStart);
            cmd.SetRayTracingIntParam(_rts, Ids.Spp, Mathf.Max(1, settings.samplesPerFrame));
            cmd.SetRayTracingIntParam(_rts, Ids.ViewMode, (int)settings.view);
            cmd.SetRayTracingIntParam(_rts, Ids.Seed, (int)settings.globalSeed);
            cmd.SetRayTracingIntParam(_rts, Ids.Reset, reset ? 1 : 0);
            cmd.SetRayTracingIntParam(_rts, Ids.Adaptive, adaptive ? 1 : 0);
            cmd.SetRayTracingIntParam(_rts, Ids.Flip, flipY ? 1 : 0);
            cmd.SetRayTracingIntParam(_rts, Ids.MisMode, settings.misMode);
            cmd.SetRayTracingFloatParam(_rts, Ids.FilterGlossy, settings.filterGlossy);
            SetFog(cmd, origin);
            cmd.DispatchRays(_rts, "AshPTRayGen", (uint)w, (uint)h, 1, null);
        }

        /// <summary>Nebel der Welt (AtmosphereController-Werte, gleiche Formel wie der Raster-Nebel) als Medium.</summary>
        private void SetFog(CommandBuffer cmd, Vector3 origin)
        {
            bool on = settings.fog && AtmosphereController.Active != null && AtmosphereController.Active.isActiveAndEnabled;
            Vector4 p = _fogParams, p2 = _fogParams2;
            Color fc = _fogColor;
            if (on && (!settings.freezeWorld || !_fogCaptured))
            {
                p = _fogParams = Shader.GetGlobalVector(Ids.WorldFogParams);
                p2 = _fogParams2 = Shader.GetGlobalVector(Ids.WorldFogParams2);
                fc = _fogColor = Shader.GetGlobalColor(Ids.WorldFogColor);
                _fogCaptured = true;
            }
            float maxC = Mathf.Max(fc.r, Mathf.Max(fc.g, fc.b), 1e-3f);
            Color tint = Color.Lerp(Color.white, new Color(fc.r / maxC, fc.g / maxC, fc.b / maxC), 0.5f);
            cmd.SetRayTracingVectorParam(_rts, Ids.Fog, new Vector4(p.x, p.y, p.z - origin.y, p2.z));
            cmd.SetRayTracingVectorParam(_rts, Ids.Fog2, new Vector4(settings.fogScatterAlbedo, settings.fogAnisotropy, Mathf.Max(p2.y, 100f), on ? 1f : 0f));
            cmd.SetRayTracingVectorParam(_rts, Ids.FogTint, new Vector4(tint.r, tint.g, tint.b, 0));
        }

        private Vector4 _fogParams, _fogParams2;
        private Color _fogColor;
        private bool _fogCaptured;

        private RenderTexture _expSmall;
        private GraphicsBuffer _expBuf;
        private float _autoEv, _autoEvTarget;
        private int _kExposure = -1;
        private int _expFrame;

        /// <summary>Kamera-Belichtung so, dass das Pathtracing-Bild im Mittel so hell ist wie das Rasterbild darunter.</summary>
        private void MatchExposure(CommandBuffer cmd, int w, int h, TextureHandle raster)
        {
            _autoEv = Mathf.Lerp(_autoEv, _autoEvTarget, 0.15f);
            if (++_expFrame % 8 != 0) return;
            if (_kExposure < 0) _kExposure = _denoise.FindKernel("AshExposure");
            if (_expSmall == null)
            {
                _expSmall = new RenderTexture(128, 72, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { name = "PT_Belichtung", hideFlags = HideFlags.DontSave };
                _expSmall.Create();
                _expBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, 4);
            }
            RTHandle src = raster;
            cmd.Blit(src, _expSmall);
            cmd.SetComputeBufferParam(_denoise, _kExposure, Ids.ExpOut, _expBuf);
            cmd.SetComputeTextureParam(_denoise, _kExposure, Ids.ExpSrc, _expSmall);
            cmd.SetComputeVectorParam(_denoise, Ids.ExpParams, new Vector4(128, 72, 0, 1));
            cmd.DispatchCompute(_denoise, _kExposure, 1, 1, 1);
            cmd.SetComputeTextureParam(_denoise, _kExposure, Ids.ExpSrc, _accum.Accum);
            cmd.SetComputeVectorParam(_denoise, Ids.ExpParams, new Vector4(w, h, 1, 0));
            cmd.DispatchCompute(_denoise, _kExposure, 1, 1, 1);
            cmd.RequestAsyncReadback(_expBuf, r =>
            {
                if (r.hasError) return;
                var d = r.GetData<float>();
                float pt = d[0], rs = d[1];
                if (float.IsNaN(pt) || float.IsNaN(rs)) return;
                _autoEvTarget = Mathf.Clamp((rs - pt) / Mathf.Log(2f), -3f, 3f);
            });
        }

        private void DispatchAdaptive(CommandBuffer cmd, int w, int h)
        {
            int tiles = _accum.Tile;
            cmd.SetComputeTextureParam(_denoise, _kAdaptive, Ids.AccumTex, _accum.Accum);
            cmd.SetComputeTextureParam(_denoise, _kAdaptive, Ids.MomentsTex, _accum.Moments);
            cmd.SetComputeTextureParam(_denoise, _kAdaptive, Ids.ActiveRW, _accum.Active);
            cmd.SetComputeBufferParam(_denoise, _kAdaptive, Ids.CountersCS, _counters);
            cmd.SetComputeVectorParam(_denoise, Ids.CsSize, new Vector4(w, h, 0, 0));
            cmd.SetComputeVectorParam(_denoise, Ids.AdaptiveParams, new Vector4(tiles, settings.adaptiveMinSamples, Mathf.Max(settings.convergenceTarget, 1e-4f), settings.targetSamples));
            RectInt hero = HeroRect ?? new RectInt(0, 0, -1, -1);
            cmd.SetComputeVectorParam(_denoise, Ids.HeroRect, new Vector4(hero.xMin, hero.yMin, hero.xMax, hero.yMax));
            cmd.DispatchCompute(_denoise, _kAdaptive, (w + tiles - 1) / tiles, (h + tiles - 1) / tiles, 1);
        }

        private void Denoise(CommandBuffer cmd, int w, int h)
        {
            int gx = (w + 7) / 8, gy = (h + 7) / 8;
            cmd.SetComputeVectorParam(_denoise, Ids.CsSize, new Vector4(w, h, 0, 0));
            cmd.SetComputeTextureParam(_denoise, _kPrepare, Ids.AccumTex, _accum.Accum);
            cmd.SetComputeTextureParam(_denoise, _kPrepare, Ids.MomentsTex, _accum.Moments);
            cmd.SetComputeTextureParam(_denoise, _kPrepare, Ids.AlbedoTex, _accum.Albedo);
            cmd.SetComputeTextureParam(_denoise, _kPrepare, Ids.NormalDepthTex, _accum.NormalDepth);
            cmd.SetComputeTextureParam(_denoise, _kPrepare, Ids.IrrA, _accum.IrrA);
            cmd.SetComputeTextureParam(_denoise, _kPrepare, Ids.Guide, _accum.Guide);
            cmd.DispatchCompute(_denoise, _kPrepare, gx, gy, 1);

            bool strong = settings.denoise == PathTraceDenoise.Stark;
            int iterations = strong ? 5 : 3;
            float sigmaL = strong ? 8f : 3f;
            RenderTexture src = _accum.IrrA, dst = _accum.IrrB;
            for (int i = 0; i < iterations; i++)
            {
                cmd.SetComputeVectorParam(_denoise, Ids.AtrousParams, new Vector4(1 << i, sigmaL, 64f, 1f));
                cmd.SetComputeTextureParam(_denoise, _kAtrous, Ids.IrrIn, src);
                cmd.SetComputeTextureParam(_denoise, _kAtrous, Ids.GuideIn, _accum.Guide);
                cmd.SetComputeTextureParam(_denoise, _kAtrous, Ids.IrrB, dst);
                cmd.DispatchCompute(_denoise, _kAtrous, gx, gy, 1);
                (src, dst) = (dst, src);
            }
            cmd.SetComputeTextureParam(_denoise, _kFinish, Ids.AccumTex, _accum.Accum);
            cmd.SetComputeTextureParam(_denoise, _kFinish, Ids.AlbedoTex, _accum.Albedo);
            cmd.SetComputeTextureParam(_denoise, _kFinish, Ids.GuideIn, _accum.Guide);
            cmd.SetComputeTextureParam(_denoise, _kFinish, Ids.IrrIn, src);
            cmd.SetComputeTextureParam(_denoise, _kFinish, Ids.Denoised, _accum.Denoised);
            cmd.DispatchCompute(_denoise, _kFinish, gx, gy, 1);
        }

        private void OnCounters(AsyncGPUReadbackRequest r, bool adaptive, int tiles)
        {
            if (r.hasError) return;
            var d = r.GetData<uint>();
            _stats.NaNSamples += d[0];
            _stats.RaysLastFrame = d[1];
            _stats.PixelsTracedLastFrame = d[2];
            if (adaptive)
            {
                _stats.ActiveTiles = (int)d[3];
                _stats.ActiveTilesValid = true;
                _stats.ActiveShare = tiles > 0 ? d[3] / (float)tiles : 1f;
            }
            else
            {
                _stats.ActiveTilesValid = false;
                _stats.ActiveShare = 1f;
            }
        }

        // ------------------------------------------------------------------ Export (§122–125)

        /// <summary>
        /// Speichert das aktuelle Bild: lineares HDR als EXR (Akkumulation bzw. entrauscht, ohne Tonemapping) und
        /// das fertige Bild mit ASHVEIL-Farbpipeline als PNG (Bildschirmaufnahme am Bildende).
        /// </summary>
        public void SaveCapture(string pathWithoutExtension, bool exr = true, bool png = true, Action<string> done = null)
        {
            StartCoroutine(SaveRoutine(pathWithoutExtension, exr, png, done));
        }

        private IEnumerator SaveRoutine(string path, bool exr, bool png, Action<string> done)
        {
            bool hud = showHud;
            showHud = false;   // Anzeige nicht mit aufs Bild
            yield return null;
            yield return new WaitForEndOfFrame();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string written = "";
            if (png)
            {
                Camera cam = Camera;
                Texture2D shot;
                if (cam != null && cam.targetTexture != null)
                {
                    // Kamera rendert in eine feste Zieltextur (Labor, Aufnahmen in fester Aufloesung)
                    RenderTexture tt = cam.targetTexture, prev = RenderTexture.active;
                    RenderTexture.active = tt;
                    shot = new Texture2D(tt.width, tt.height, TextureFormat.RGBA32, false);
                    shot.ReadPixels(new Rect(0, 0, tt.width, tt.height), 0, 0);
                    shot.Apply();
                    RenderTexture.active = prev;
                }
                else shot = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(path + ".png", shot.EncodeToPNG());
                Destroy(shot);
                written += path + ".png ";
            }
            showHud = hud;
            if (exr && _accum.Accum != null)
            {
                RenderTexture src = settings.denoise != PathTraceDenoise.Aus ? _accum.Denoised : null;
                var req = src != null ? AsyncGPUReadback.Request(src, 0, TextureFormat.RGBAFloat)
                                      : AsyncGPUReadback.Request(_accum.Accum, 0, TextureFormat.RGBAFloat);
                yield return new WaitUntil(() => req.done);
                if (!req.hasError)
                {
                    var data = req.GetData<Color>();
                    var tex = new Texture2D(_accum.Width, _accum.Height, TextureFormat.RGBAFloat, false, true);
                    var px = new Color[data.Length];
                    for (int i = 0; i < data.Length; i++)
                    {
                        Color c = data[i];
                        float n = src != null ? 1f : Mathf.Max(c.a, 1f);
                        px[i] = new Color(c.r / n, c.g / n, c.b / n, 1f);
                    }
                    tex.SetPixels(px);
                    File.WriteAllBytes(path + ".exr", tex.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat | Texture2D.EXRFlags.CompressZIP));
                    Destroy(tex);
                    written += path + ".exr";
                }
            }
            done?.Invoke(written.Trim());
        }

        /// <summary>Ganzes lineares Bild (Summe/Anzahl bzw. entrauscht) – fuer Tests (Fehler gegen Referenz).</summary>
        public void ReadImage(bool denoised, Action<Vector3[], int, int> done)
        {
            RenderTexture src = denoised ? _accum.Denoised : _accum.Accum;
            if (src == null) { done(null, 0, 0); return; }
            int w = _accum.Width, h = _accum.Height;
            AsyncGPUReadback.Request(src, 0, r =>
            {
                if (r.hasError) { done(null, w, h); return; }
                if (denoised)
                {
                    // entrauschtes Bild ist RGBA16F
                    var raw = r.GetData<ushort>();
                    var img = new Vector3[w * h];
                    for (int i = 0; i < img.Length; i++)
                        img[i] = new Vector3(Mathf.HalfToFloat(raw[i * 4]), Mathf.HalfToFloat(raw[i * 4 + 1]), Mathf.HalfToFloat(raw[i * 4 + 2]));
                    done(img, w, h);
                    return;
                }
                var data = r.GetData<Color>();
                var o = new Vector3[w * h];
                for (int i = 0; i < o.Length; i++)
                {
                    Color c = data[i];
                    float k = Mathf.Max(c.a, 1f);
                    o[i] = new Vector3(c.r / k, c.g / k, c.b / k);
                }
                done(o, w, h);
            });
        }

        public struct RegionStats
        {
            public Vector3 Mean;          // mittlere Strahldichte (linear)
            public float MaxLuminance;
            public float RelError;        // mittlerer relativer Standardfehler je Pixel
            public float MinSamples, MaxSamples;
            public int Pixels, Invalid;
            public float Luminance => 0.2126f * Mean.x + 0.7152f * Mean.y + 0.0722f * Mean.z;
        }

        /// <summary>Kennzahlen eines Bildbereichs (Pixel, Zeile 0 = unten) aus Summe und Momenten – fuer Tests, asynchron.</summary>
        public void ReadRegion(RectInt region, Action<RegionStats> done)
        {
            if (_accum.Accum == null) { done(default); return; }
            int w = _accum.Width, h = _accum.Height;
            region.xMin = Mathf.Clamp(region.xMin, 0, w); region.xMax = Mathf.Clamp(region.xMax, 0, w);
            region.yMin = Mathf.Clamp(region.yMin, 0, h); region.yMax = Mathf.Clamp(region.yMax, 0, h);
            // Daten eines Rueckleseauftrags gelten nur in seinem Rueckruf → Summe zuerst kopieren
            AsyncGPUReadback.Request(_accum.Accum, 0, ra =>
            {
                Color[] acc = ra.hasError ? null : ra.GetData<Color>().ToArray();
                AsyncGPUReadback.Request(_accum.Moments, 0, rm =>
            {
                var s = new RegionStats { MinSamples = float.MaxValue };
                if (acc == null || rm.hasError)
                {
                    Debug.Log($"[Ashveil] Pathtracing: Rueckleseauftrag fehlgeschlagen ({w}×{h})");
                    done(s);
                    return;
                }
                var mom = rm.GetData<Color>();
                double sr = 0, sg = 0, sb = 0, se = 0;
                for (int y = region.yMin; y < region.yMax; y++)
                for (int x = region.xMin; x < region.xMax; x++)
                {
                    Color c = acc[y * w + x];
                    Color m = mom[y * w + x];
                    float k = Mathf.Max(c.a, 1f);
                    if (float.IsNaN(c.r + c.g + c.b) || float.IsInfinity(c.r + c.g + c.b) || m.b > 0.5f) { s.Invalid++; continue; }
                    float r = c.r / k, g = c.g / k, b = c.b / k;
                    sr += r; sg += g; sb += b;
                    float lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                    s.MaxLuminance = Mathf.Max(s.MaxLuminance, lum);
                    float mean = m.r / k;
                    float var = Mathf.Max(0f, m.g / k - mean * mean);
                    se += Mathf.Sqrt(var / k) / Mathf.Max(mean, 1e-3f);
                    s.MinSamples = Mathf.Min(s.MinSamples, c.a);
                    s.MaxSamples = Mathf.Max(s.MaxSamples, c.a);
                    s.Pixels++;
                }
                if (s.Pixels == 0) Debug.Log($"[Ashveil] Pathtracing: leerer Bereich {region} bei {w}×{h}");
                if (s.Pixels > 0)
                {
                    s.Mean = new Vector3((float)(sr / s.Pixels), (float)(sg / s.Pixels), (float)(sb / s.Pixels));
                    s.RelError = (float)(se / s.Pixels);
                }
                done(s);
            });
            });
        }

        // ------------------------------------------------------------------ Diagnose-Anzeige (§115)

        private void OnGUI()
        {
            if (!showHud || !_ready) return;
            PathTraceStats s = _stats;
            string status = s.Done ? "fertig" : settings.freezeWorld ? "sammelt (Welt eingefroren)" : "sammelt (lebendig)";
            string text =
                $"<b>Pathtracing</b> · {settings.preset} · {status}\n" +
                $"Proben {s.Samples}/{(settings.targetSamples > 0 ? settings.targetSamples.ToString() : "∞")} · {settings.samplesPerFrame}/Bild · {settings.maxBounces} Abpraller · Alter {s.AgeSeconds:0.0} s\n" +
                $"GPU {s.GpuMs:0.0} ms · Strahlen/Pixel {(s.PixelsTracedLastFrame > 0 ? s.RaysLastFrame / (float)s.PixelsTracedLastFrame : 0):0.0} · aktiv {s.ActiveShare * 100f:0} %\n" +
                $"RTAS {s.Instances} Instanzen · {s.Triangles / 1000}k Dreiecke · {s.RtasBytes / (1024 * 1024)} MB · Figuren {s.Skinned} · Gelaende {s.Terrains} · Gras {s.Details}\n" +
                $"Lichter {s.LocalLights} · Leuchtdreiecke {s.EmissiveTriangles} · Hauptlicht {(s.MainIsMoon ? "Mond" : "Sonne")} · Himmel {(s.EnvImportance ? "Importance" : "gleichm.")}\n" +
                $"Ansicht {settings.view} · Entrauschen {settings.denoise} · NaN {s.NaNSamples} · Aufnahme {s.CollectMs:0} ms";
            var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, richText = true, fontSize = 12 };
            style.normal.textColor = s.NaNSamples > 0 ? new Color(1f, 0.5f, 0.5f) : Color.white;
            GUI.Box(new Rect(Screen.width - 470, 10, 460, 104), text, style);
        }
    }

    public sealed class PathTraceStats
    {
        public float GpuMs, AgeSeconds, CollectMs, ActiveShare = 1f, SunIrradiance, ExposureEV;
        public string MainLight = "-";
        public int Samples, Frames, Instances, LocalLights, EmissiveTriangles, Materials, Skinned, Terrains, Width, Height, ActiveTiles, Details;
        public long Triangles;
        public ulong RtasBytes, AccumBytes;
        public uint NaNSamples, RaysLastFrame, PixelsTracedLastFrame;
        public bool Done, MainIsMoon, EnvImportance, ActiveTilesValid, DenoisedFinal;
    }
}
