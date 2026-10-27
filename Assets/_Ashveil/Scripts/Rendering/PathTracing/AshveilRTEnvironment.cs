using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashveil.Rendering.PathTracing
{
    /// <summary>
    /// Himmel fuer den Pathtracer (§36–38): derselbe Himmel wie im Spiel (DynamicSkyCore mit den globalen Werten des
    /// SkyDirectors) als Lat-Long-Karte, ohne Sonnenscheibe (die kommt analytisch). Danach wird die Karte einmal
    /// gelesen und eine Alias-Tabelle nach Luminanz × Raumwinkel gebaut → Importance Sampling statt gleichmaessig.
    /// Bis die Tabelle da ist, wird gleichmaessig gesampelt (ebenfalls erwartungstreu).
    /// </summary>
    public sealed class AshveilRTEnvironment : System.IDisposable
    {
        public const int Width = 1024, Height = 512;
        public const int TableWidth = 256, TableHeight = 128;

        private readonly Material _bake;
        private RenderTexture _map;
        private GraphicsBuffer _alias;
        private GraphicsBuffer _uniformAlias;
        private bool _aliasReady;
        private int _bakeHash;
        private int _pendingHash;

        public RenderTexture Map => _map;
        public GraphicsBuffer Alias => _aliasReady ? _alias : _uniformAlias;
        public bool AliasReady => _aliasReady;
        public float Power { get; private set; }   // π × mittlere Luminanz (Raumwinkel-gewichtet)
        public int Version { get; private set; }
        public bool NeedsBake { get; private set; } = true;
        public PathTraceEnvironment Mode { get; private set; }

        private static readonly string[] SkyGlobals =
        {
            "_AshSkyZenith", "_AshSkyHorizon", "_AshSkyGround", "_AshSkyGlow", "_AshSkySun", "_AshSkySunColor",
            "_AshMoonA", "_AshMoonB", "_AshMoonAColor", "_AshMoonBColor", "_AshStars", "_AshCloudLow", "_AshCloudLowOfs",
            "_AshCloudHigh", "_AshCloudHighOfs", "_AshCloudLit", "_AshCloudShadow", "_AshSkyHaze", "_AshCelX", "_AshCelY", "_AshCelZ",
            "_AshVeilNormal", "_AshVeilTangent"
        };
        private static int[] _skyIds;

        private readonly int _w = Width, _h = Height;
        private readonly bool _importance = true;

        /// <summary>Kleinere Karte ohne Wichtigkeitstabelle (dynamisches GI: nur Himmelslicht fuer Probenstrahlen, kein Rueckleseweg).</summary>
        public AshveilRTEnvironment(Shader bakeShader, int width, int height, bool importance) : this(bakeShader, width, height)
        {
            _importance = importance;
        }

        public AshveilRTEnvironment(Shader bakeShader) : this(bakeShader, Width, Height) { }

        private AshveilRTEnvironment(Shader bakeShader, int width, int height)
        {
            _w = width; _h = height;
            _bake = bakeShader != null ? new Material(bakeShader) { hideFlags = HideFlags.DontSave } : null;
            _map = new RenderTexture(_w, _h, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = "AshveilPT_Himmel", wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear, useMipMap = false, hideFlags = HideFlags.DontSave
            };
            _map.Create();
            _alias = new GraphicsBuffer(GraphicsBuffer.Target.Structured, TableWidth * TableHeight, 16);
            _uniformAlias = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 16);
            _uniformAlias.SetData(new[] { new Vector4(1f, 0f, 1f, 0f) });
            if (_skyIds == null)
            {
                _skyIds = new int[SkyGlobals.Length];
                for (int i = 0; i < SkyGlobals.Length; i++) _skyIds[i] = Shader.PropertyToID(SkyGlobals[i]);
            }
        }

        /// <summary>Prueft, ob sich der Himmel geaendert hat (nur wenn nicht eingefroren aufgerufen).</summary>
        public bool Check(PathTraceSettings s)
        {
            int h = (int)s.environment * 7919 + s.uniformEnvironment.GetHashCode() * 31 + s.environmentIntensity.GetHashCode();
            if (s.environment == PathTraceEnvironment.Spielhimmel)
                unchecked { foreach (int id in _skyIds) h = h * 31 + Shader.GetGlobalVector(id).GetHashCode(); }
            else if (s.environment == PathTraceEnvironment.Verlauf)
                h = h * 31 + RenderSettings.ambientSkyColor.GetHashCode() + RenderSettings.ambientEquatorColor.GetHashCode() * 7 + RenderSettings.ambientGroundColor.GetHashCode() * 13;
            Mode = s.environment;
            if (h == _bakeHash) return false;
            _bakeHash = h;
            NeedsBake = s.environment == PathTraceEnvironment.Spielhimmel || s.environment == PathTraceEnvironment.Verlauf;
            if (s.environment == PathTraceEnvironment.Gleichmaessig)
            {
                Color c = s.uniformEnvironment.linear * s.environmentIntensity;
                Power = Mathf.PI * (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b);
            }
            else if (s.environment == PathTraceEnvironment.Aus) Power = 0f;
            else if (!_aliasReady) Power = Mathf.PI * 0.5f;   // Schaetzung bis zur ersten Auswertung
            Version++;
            return true;
        }

        /// <summary>Himmel in die Karte backen und Auswertung anstossen (im Render-Pass).</summary>
        public void Bake(CommandBuffer cmd)
        {
            if (!NeedsBake || _bake == null) return;
            NeedsBake = false;
            _bake.SetVector("_AshPTBakeSize", new Vector4(_w, _h, 0, 0));
            bool gradient = Mode == PathTraceEnvironment.Verlauf;
            _bake.SetVector("_AshPTBakeGround", new Vector4(0, 0, 0, gradient ? 1 : 0));
            _bake.SetColor("_AshPTGradSky", RenderSettings.ambientSkyColor.linear);
            _bake.SetColor("_AshPTGradEquator", RenderSettings.ambientEquatorColor.linear);
            _bake.SetColor("_AshPTGradGround", RenderSettings.ambientGroundColor.linear);
            cmd.SetRenderTarget(_map);
            cmd.DrawProcedural(Matrix4x4.identity, _bake, 0, MeshTopology.Triangles, 3);
            _aliasReady = false;
            if (!_importance)
            {
                if (ComputeUpIrradiance) cmd.RequestAsyncReadback(_map, 0, TextureFormat.RGBAHalf, OnUpReadback);
                return;
            }
            _pendingHash = _bakeHash;
            int expected = _pendingHash;
            cmd.RequestAsyncReadback(_map, 0, TextureFormat.RGBAHalf, r => OnReadback(r, expected));
        }

        /// <summary>
        /// Kosinusgewichtetes Himmelslicht fuer eine nach oben zeigende Flaeche (URP-Einheit wie bakedGI), aus der kleinen
        /// Karte gelesen. Dynamisches GI gleicht damit auf Wunsch die Helligkeit an das klassische Umgebungslicht an.
        /// </summary>
        public bool ComputeUpIrradiance;
        public Color UpIrradiance { get; private set; }
        public bool UpIrradianceValid { get; private set; }

        private void OnUpReadback(AsyncGPUReadbackRequest r)
        {
            if (r.hasError || _map == null) return;
            NativeArray<ushort> px = r.GetData<ushort>();
            double sr = 0, sg = 0, sb = 0, sw = 0;
            int half = _h / 2;
            for (int j = 0; j < half; j++)
            {
                float theta = (j + 0.5f) / _h * Mathf.PI;          // Zeile 0 = Zenit (wie AshDirToLatLong)
                double w = Mathf.Cos(theta) * Mathf.Sin(theta);
                for (int i = 0; i < _w; i++)
                {
                    int idx = (j * _w + i) * 4;
                    float cr = Mathf.HalfToFloat(px[idx]), cg = Mathf.HalfToFloat(px[idx + 1]), cb = Mathf.HalfToFloat(px[idx + 2]);
                    if (float.IsNaN(cr + cg + cb) || float.IsInfinity(cr + cg + cb)) continue;
                    sr += cr * w; sg += cg * w; sb += cb * w; sw += w;
                }
            }
            if (sw <= 0) return;
            UpIrradiance = new Color((float)(sr / sw), (float)(sg / sw), (float)(sb / sw), 1f);
            UpIrradianceValid = true;
        }

        private void OnReadback(AsyncGPUReadbackRequest r, int hash)
        {
            if (r.hasError || hash != _bakeHash || _alias == null) return;
            NativeArray<ushort> px = r.GetData<ushort>();
            var weights = new float[TableWidth * TableHeight];
            int bx = Width / TableWidth, by = Height / TableHeight;
            double lumSum = 0, solidSum = 0;
            for (int j = 0; j < TableHeight; j++)
            {
                float theta = (j + 0.5f) / TableHeight * Mathf.PI;
                float sinT = Mathf.Sin(theta);
                for (int i = 0; i < TableWidth; i++)
                {
                    float lum = 0f;
                    for (int y = 0; y < by; y++)
                    for (int x = 0; x < bx; x++)
                    {
                        int idx = ((j * by + y) * Width + (i * bx + x)) * 4;
                        float rr = Mathf.HalfToFloat(px[idx]), gg = Mathf.HalfToFloat(px[idx + 1]), bb = Mathf.HalfToFloat(px[idx + 2]);
                        lum += 0.2126f * rr + 0.7152f * gg + 0.0722f * bb;
                    }
                    lum /= bx * by;
                    if (float.IsNaN(lum) || float.IsInfinity(lum) || lum < 0f) lum = 0f;
                    // kleiner Sockel: auch dunkle Richtungen bleiben erreichbar (Dichte nie 0, wo Licht sein koennte)
                    weights[j * TableWidth + i] = (lum + 1e-4f) * sinT;
                    lumSum += lum * sinT;
                    solidSum += sinT;
                }
            }
            Vector4[] alias = AliasTable.Build(weights, out _);
            _alias.SetData(alias);
            Power = Mathf.PI * (float)(lumSum / System.Math.Max(solidSum, 1e-9));
            _aliasReady = true;
        }

        public Vector4 Params(PathTraceSettings s)
        {
            float mode = s.environment switch
            {
                PathTraceEnvironment.Gleichmaessig => 1f,
                PathTraceEnvironment.Aus => 2f,
                _ => 0f
            };
            // Tabellenaufloesung, nicht Kartenaufloesung: die Dichte ist stueckweise konstant je Tabellenzelle
            return _aliasReady ? new Vector4(TableWidth, TableHeight, s.environmentIntensity, mode)
                               : new Vector4(1, 1, s.environmentIntensity, mode);
        }

        public void Dispose()
        {
            if (_map != null) { _map.Release(); Object.Destroy(_map); }
            _alias?.Release();
            _uniformAlias?.Release();
            _alias = null;
            if (_bake != null) Object.Destroy(_bake);
        }
    }
}
