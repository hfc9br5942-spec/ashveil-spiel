using System;
using Ashveil.Core;
using Ashveil.World;
using UnityEngine;

namespace Ashveil.Rendering.Sky
{
    /// <summary>
    /// Dynamic Sky V1 (§ 4): zentrale Steuerung des Himmels ueber dem bestehenden Tag-und-Nacht-Zyklus (M8.2).
    /// Einzige Zeitquelle ist die Weltuhr; daraus und aus dem Weltseed entsteht <see cref="SkyRuntimeState"/> –
    /// Sonne (wie DayNightCycle), zwei Monde mit analytischer Bahn und Phase, drehender Sternenhimmel, Wolkenbild aus dem
    /// Monatsplan, Wind, Dunst, Sonnendurchlass. Alle Rechner kommen ohne Netzverkehr zum selben Himmel (§ 67).
    ///
    /// Leistung (§ 64–66, § 83): der Zustand wird 4-mal je Sekunde berechnet (wenige Sinus/Cosinus), jedes Bild werden nur
    /// die stetigen Windversaetze fortgeschrieben und als globale Shader-Werte gesetzt. Keine GameObjects je Stern oder
    /// Wolke – Verlauf, Sonne, Monde, Sterne und Wolken zeichnet der Shader "Ashveil/DynamicSky" auf der GPU.
    /// Im Editor (ohne Play) zeigt er den Himmel zur gestalteten Stunde 15:30 an Tag 0.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-40)]
    public sealed class SkyDirector : MonoBehaviour
    {
        [SerializeField] private SkyClimateProfile climate;
        [SerializeField] private Light sun;
        [Tooltip("Zustand so oft je Sekunde neu berechnen")]
        [SerializeField] private float updatesPerSecond = 4f;

        public static SkyDirector Active { get; private set; }

        // --- Fuer DayNightCycle (Licht) und andere Systeme; ohne Himmel neutral
        public static float SunTransmittance { get; private set; } = 1f;
        public static bool HasMoonLight { get; private set; }
        public static Vector3 MoonLightDirection { get; private set; } = Vector3.up;
        public static float MoonLightFactor { get; private set; } = 1f;
        public static float Coverage { get; private set; }

        // --- Pass 11: lokale Luft (Gebiete) – nur fuer die eigene Kamera, der globale Zustand bleibt unberuehrt
        /// <summary>Tests: nur den globalen Himmel zeigen (keine Gebietsluft).</summary>
        public static bool IgnoreLocal;
        /// <summary>Tests: Standort fuer die Gebietsluft statt der Kamera.</summary>
        public static Vector3? ViewerOverride;
        /// <summary>Aktuelle (zeitlich geglaettete) Gebietsluft am Betrachter.</summary>
        public static LocalAtmosphere Local { get; private set; } = LocalAtmosphere.Neutral;
        private bool _localInit;

        // --- Pass 12: Sternschnuppen
        /// <summary>Zeitraster fuer Sternschnuppen (echte Sekunden): je Schlitz hoechstens eine.</summary>
        public const float MeteorSlot = 0.25f;
        private const float MaxMeteorLife = 1.4f;
        public struct Meteor
        {
            public Vector3 From, To;
            public double Start;
            public float Life, Brightness;
            public bool Shower;
        }
        private readonly Meteor[] _meteorBuf = new Meteor[8];
        /// <summary>Wie viele Sternschnuppen gerade am Himmel sind (Tests).</summary>
        public int VisibleMeteors { get; private set; }

        public SkyClimateProfile Climate => climate;
        public SkyRuntimeState State { get; private set; }
        /// <summary>Wie oft der Zustand berechnet wurde (Tests/Leistung).</summary>
        public int Evaluations { get; private set; }

        private static SkyQuality _quality = SkyQuality.Medium;
        /// <summary>Darstellungsstufe (setzt GraphicsApplier aus den Grafikeinstellungen). Der Zustand bleibt gleich.</summary>
        public static SkyQuality Quality
        {
            get => _quality;
            set
            {
                _quality = value;
                ApplyKeywords();
            }
        }

        private float _designYaw, _elevScale = 45f;
        private bool _hasDesign;
        private float _nextEval;
        private double _lastTotal = double.NaN;
        private Vector4[] _constellation;
        private int _constellationCount;
        private float _azimuthOffset;

        private static readonly int ZenithId = Shader.PropertyToID("_AshSkyZenith"), HorizonId = Shader.PropertyToID("_AshSkyHorizon"),
            GroundId = Shader.PropertyToID("_AshSkyGround"), GlowId = Shader.PropertyToID("_AshSkyGlow"), SunId = Shader.PropertyToID("_AshSkySun"),
            SunColorId = Shader.PropertyToID("_AshSkySunColor"), CelXId = Shader.PropertyToID("_AshCelX"), CelYId = Shader.PropertyToID("_AshCelY"),
            CelZId = Shader.PropertyToID("_AshCelZ"), MoonAId = Shader.PropertyToID("_AshMoonA"), MoonBId = Shader.PropertyToID("_AshMoonB"),
            MoonAColorId = Shader.PropertyToID("_AshMoonAColor"), MoonBColorId = Shader.PropertyToID("_AshMoonBColor"),
            MoonSeedId = Shader.PropertyToID("_AshMoonSeed"), StarsId = Shader.PropertyToID("_AshStars"),
            LowId = Shader.PropertyToID("_AshCloudLow"), LowOfsId = Shader.PropertyToID("_AshCloudLowOfs"),
            HighId = Shader.PropertyToID("_AshCloudHigh"), HighOfsId = Shader.PropertyToID("_AshCloudHighOfs"),
            LitId = Shader.PropertyToID("_AshCloudLit"), ShadowId = Shader.PropertyToID("_AshCloudShadow"),
            HazeId = Shader.PropertyToID("_AshSkyHaze"), ConstId = Shader.PropertyToID("_AshConstStars"), ConstCountId = Shader.PropertyToID("_AshConstCount");
        private static readonly int[] MeteorIds =
        {
            Shader.PropertyToID("_AshMeteorA0"), Shader.PropertyToID("_AshMeteorA1"),
            Shader.PropertyToID("_AshMeteorB0"), Shader.PropertyToID("_AshMeteorB1")
        };

        public const int MaxConstellationStars = 64;

        private void OnEnable()
        {
            Active = this;
            if (sun == null) sun = RenderSettings.sun;
            _hasDesign = false;
            _nextEval = 0f;
            _lastTotal = double.NaN;
            BuildConstellations();
            ApplyKeywords();
            if (climate != null) Refresh(CurrentTotalHours);
        }

        private void OnDisable()
        {
            if (Active == this) Active = null;
            SunTransmittance = 1f;
            HasMoonLight = false;
            MoonLightFactor = 1f;
            Coverage = 0f;
            Local = LocalAtmosphere.Neutral;
            _localInit = false;
            Ashveil.Rendering.AtmosphereController.LocalFogFactor = 1f;
            Ashveil.Rendering.AtmosphereController.LocalFogTint = Color.white;
            for (int i = 0; i < MeteorIds.Length; i++) Shader.SetGlobalVector(MeteorIds[i], Vector4.zero);
        }

        private void OnValidate()
        {
            SkyMonthSchedule.ClearCache();
            BuildConstellations();
            _nextEval = 0f;
        }

        public static void ApplyKeywords()
        {
            Shader.DisableKeyword("ASHSKY_LOW");
            Shader.DisableKeyword("ASHSKY_HIGH");
            if (_quality == SkyQuality.Low) Shader.EnableKeyword("ASHSKY_LOW");
            else if (_quality == SkyQuality.High) Shader.EnableKeyword("ASHSKY_HIGH");
        }

        // ================================================================== Ablauf

        /// <summary>Nur Editor ohne Play: Weltstunde fuer die Tageszeit-Vorschau (Review-Bilder, Welle 0); null = gestaltete Stunde.</summary>
        public static double? EditorPreviewHours;

        /// <summary>Fortlaufende Weltstunde, die der Himmel gerade zeigt (im Editor: gestaltete Stunde an Tag 0 oder die Vorschau).</summary>
        public static double CurrentTotalHours => Application.isPlaying ? WorldClock.TotalHours : (EditorPreviewHours ?? DayNightCycle.DesignHour);

        private void Update()
        {
            if (climate == null) return;
            EnsureDesign();
            double total = CurrentTotalHours;
            bool jumped = double.IsNaN(_lastTotal) || Math.Abs(total - _lastTotal) > 0.25;
            float now = Application.isPlaying ? Time.unscaledTime : 0f;
            if (jumped || !Application.isPlaying || now >= _nextEval)
            {
                _nextEval = now + 1f / Mathf.Max(0.5f, updatesPerSecond);
                Refresh(total);
            }
            _lastTotal = total;
            UpdateLocal(Application.isPlaying ? Time.unscaledDeltaTime : 0f, !Application.isPlaying);
            ApplyMotion(total);
        }

        /// <summary>Zustand sofort fuer eine Zeit berechnen und anwenden (Tests, Zeitsprung).</summary>
        public void Refresh(double totalHours)
        {
            if (climate == null) return;
            EnsureDesign();
            SkyRuntimeState s = Evaluate(totalHours);
            State = s;
            Evaluations++;
            if (!_localInit) UpdateLocal(0f, true);
            SkyRuntimeState v = WithLocal(s, Local);
            EffectiveState = v;
            SunTransmittance = v.SunTransmittance;
            HasMoonLight = v.MoonLightVisible;
            MoonLightDirection = v.MoonLightDirection;
            MoonLightFactor = v.MoonLightFactor;
            Coverage = v.CloudCoverage;
            ApplyState(v);
            ApplyMotion(totalHours);
        }

        /// <summary>Was die eigene Kamera sieht: globaler Zustand plus Gebietsluft.</summary>
        public SkyRuntimeState EffectiveState { get; private set; }

        // ================================================================== Pass 11: Gebietsluft

        /// <summary>Gemischte Gebietsluft an einem Weltpunkt (ohne zeitliche Glaettung).</summary>
        public static LocalAtmosphere Sample(Vector3 p)
        {
            LocalAtmosphere r = LocalAtmosphere.Neutral;
            float best = 0f;
            string name = r.Name;
            foreach (BiomeAtmosphereVolume v in BiomeAtmosphereVolume.All)
            {
                if (v == null || v.Profile == null) continue;
                float w = v.Weight(p);
                if (w <= 0f) continue;
                r = LocalAtmosphere.Lerp(r, LocalAtmosphere.From(v.Profile), w);
                if (w > best) { best = w; name = v.Profile.displayName; }
            }
            r.Name = best >= 0.5f ? name : "Offenes Land";
            return r;
        }

        /// <summary>Gebietsluft sofort auf den Zielwert setzen (Zeitsprung/Tests).</summary>
        public void SnapLocal()
        {
            UpdateLocal(0f, true);
            if (climate != null) Refresh(CurrentTotalHours);
        }

        private void UpdateLocal(float dt, bool snap)
        {
            LocalAtmosphere target = LocalAtmosphere.Neutral;
            if (!IgnoreLocal)
            {
                Vector3? viewer = ViewerOverride;
                if (viewer == null)
                {
                    Camera cam = Camera.main;
                    if (cam != null) viewer = cam.transform.position;
                }
                if (viewer != null) target = Sample(viewer.Value);
            }
            if (snap || !_localInit)
            {
                Local = target;
                _localInit = true;
            }
            else
            {
                // Zeitliche Glaettung: ~95 % nach der Blendzeit des Gebiets (nie ein harter Wechsel, § 53)
                float k = 1f - Mathf.Exp(-dt * 3f / Mathf.Max(0.5f, target.BlendSeconds));
                LocalAtmosphere l = LocalAtmosphere.Lerp(Local, target, k);
                l.Name = target.Name;
                Local = l;
            }
            Ashveil.Rendering.AtmosphereController.LocalFogFactor = Local.FogFactor;
            Ashveil.Rendering.AtmosphereController.LocalFogTint = Local.FogTint;
        }

        /// <summary>Globalen Zustand um die Gebietsluft verschieben (Wolken, Dunst, Sonnendurchlass, Sterne).</summary>
        public static SkyRuntimeState WithLocal(SkyRuntimeState s, LocalAtmosphere l)
        {
            s.CloudCoverage = Mathf.Clamp01(s.CloudCoverage + l.CloudBias);
            s.HorizonHaze = Mathf.Clamp01(s.HorizonHaze + l.HazeBias);
            s.AtmosphericDensity = s.HorizonHaze;
            s.SunTransmittance = Mathf.Clamp01(s.SunTransmittance * l.SunTransmission);
            s.StarVisibility *= Mathf.Clamp01(1f - 0.5f * l.HazeBias - Mathf.Max(0f, l.CloudBias));
            s.MoonLightFactor *= Mathf.Lerp(1f, 0.85f, Mathf.Clamp01(l.HazeBias * 2f));
            return s;
        }

        private void EnsureDesign()
        {
            if (_hasDesign) return;
            DayNightCycle dnc = DayNightCycle.Active;
            if (dnc != null && dnc.HasDesign)
            {
                _designYaw = dnc.DesignYaw;
                _elevScale = dnc.ElevationScale;
            }
            else if (sun != null) DayNightCycle.DesignFromLight(sun.transform.rotation, out _designYaw, out _elevScale);
            _hasDesign = true;
            // Himmelsachse so drehen, dass die Mittagssonne im Sueden des Himmelsgewoelbes steht
            Vector3 noon = DayNightCycle.SunDirection(12.5f, _designYaw, _elevScale);
            _azimuthOffset = Mathf.Atan2(noon.x, noon.z) * Mathf.Rad2Deg - 180f;
        }

        // ================================================================== Zustand (rein aus der Zeit)

        public SkyRuntimeState Evaluate(double totalHours)
        {
            EnsureDesign();
            var s = new SkyRuntimeState { TotalHours = totalHours };
            double day = Math.Floor(totalHours / 24.0);
            float h = (float)(totalHours - day * 24.0);
            s.WorldDay = (int)day;
            s.TimeOfDay = h;

            // --- Sonne wie DayNightCycle (stetig ueber 24 h)
            s.SunDirection = DayNightCycle.SunDirection(h, _designYaw, _elevScale);
            s.SunElevation = Mathf.Asin(Mathf.Clamp(s.SunDirection.y, -1f, 1f)) * Mathf.Rad2Deg;

            // --- Himmelsgewoelbe: Pol im Norden, Hoehe = geografische Breite; dreht einmal je Tag
            CelestialBasis(totalHours, out s.CelestialX, out s.CelestialY, out s.CelestialZ);
            s.StarRotation = (h - 12.5f) / 24f * 360f;

            // --- Monde
            double days = totalHours / 24.0;
            Moon(climate.moonA, days, s, out s.MoonADirection, out s.MoonAIllumination, out s.MoonAPhase, out s.MoonAElevation);
            Moon(climate.moonB, days, s, out s.MoonBDirection, out s.MoonBIllumination, out s.MoonBPhase, out s.MoonBElevation);

            // --- Wolken aus dem Monatsplan
            SkyMonthSchedule.Evaluate(climate, totalHours, out SkyState from, out SkyState to, out float blend, out int month);
            s.MonthIndex = month;
            s.CloudFrom = from;
            s.CloudType = to;
            s.CloudBlend = blend;
            CloudProfile a = climate.Profile(from), b = climate.Profile(to);
            if (a != null && b != null)
            {
                s.CloudCoverage = Mathf.Lerp(a.lowCoverage, b.lowCoverage, blend);
                s.CloudDensity = Mathf.Lerp(a.lowDensity, b.lowDensity, blend);
                s.CloudScale = Mathf.Lerp(a.lowScale, b.lowScale, blend);
                s.CloudErosion = Mathf.Lerp(a.lowErosion, b.lowErosion, blend);
                s.CloudDarkness = Mathf.Lerp(a.darkness, b.darkness, blend);
                s.HighCoverage = Mathf.Lerp(a.highCoverage, b.highCoverage, blend);
                s.HighDensity = Mathf.Lerp(a.highDensity, b.highDensity, blend);
                s.HorizonHaze = Mathf.Lerp(a.horizonHaze, b.horizonHaze, blend);
                Vector2 tr = Vector2.Lerp(a.sunTransmittance, b.sunTransmittance, blend);
                // Sonnenfenster: langsames Rauschen ueber echte Sekunden (Periode ~1,5 min), kein Flackern
                double seconds = totalHours * WorldClock.SecondsPerHour;
                // Periode 150 s: ein voller Wechsel von trueb zu sonnig dauert etwa 1,5–2,5 Minuten (§ 9)
                float breaks = SmoothNoise(seconds / 150.0, climate.worldSeed);
                s.SunTransmittance = Mathf.Lerp(tr.x, tr.y, breaks);
            }
            else s.SunTransmittance = 1f;
            s.AtmosphericDensity = s.HorizonHaze;

            // --- Wind (nur fuer Anzeige/Tests; die Versaetze rechnet ApplyMotion stetig)
            double sec = totalHours * WorldClock.SecondsPerHour;
            s.CloudWindLow = Wind(climate.lowWind, sec, 1.3);
            s.CloudWindHigh = Wind(climate.highWind, sec, 4.1);

            // --- Sterne: nur nachts, von Wolken, Dunst und hellem Mond gedaempft
            float night = Mathf.Clamp01((-s.SunElevation - 1f) / 11f);
            float moonGlare = Mathf.Max(s.MoonAIllumination * (s.MoonAElevation > 0f ? 1f : 0f), 0.5f * s.MoonBIllumination * (s.MoonBElevation > 0f ? 1f : 0f));
            s.StarVisibility = night * (1f - 0.85f * Mathf.Clamp01(s.CloudCoverage * s.CloudDensity * 1.2f)) * (1f - 0.35f * moonGlare) * (1f - 0.6f * s.HorizonHaze);

            // --- Mondlicht: ein Licht, Richtung aus beiden Monden nach Helligkeit gemischt (§ 20)
            float wa = MoonWeight(climate.moonA, s.MoonAIllumination, s.MoonAElevation);
            float wb = MoonWeight(climate.moonB, s.MoonBIllumination, s.MoonBElevation);
            float sum = wa + wb;
            float bright = Mathf.Clamp01(sum);
            s.MoonLightFactor = Mathf.Lerp(climate.moonLight.x, climate.moonLight.y, bright) * Mathf.Lerp(1f, 0.72f, s.CloudCoverage * s.CloudDensity);
            if (sum > 0.04f)
            {
                Vector3 dir = (s.MoonADirection * wa + s.MoonBDirection * wb).normalized;
                float elev = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
                if (elev < 18f)
                {
                    // flache Mondschatten waeren endlos lang: fuer das Licht auf 18° anheben, Richtung bleibt
                    Vector3 flat = new Vector3(dir.x, 0f, dir.z).normalized;
                    dir = (flat * Mathf.Cos(18f * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(18f * Mathf.Deg2Rad)).normalized;
                }
                s.MoonLightDirection = dir;
                s.MoonLightVisible = true;
            }
            s.SkyExposure = 1f;
            s.ActiveSkyEvent = ShowerAt(totalHours, out SkyEventDefinition ev, out _) ? ev.id : "";
            return s;
        }

        /// <summary>Achsen des Himmelsgewoelbes (Welt) zu einer Weltstunde.</summary>
        private void CelestialBasis(double totalHours, out Vector3 x, out Vector3 y, out Vector3 z)
        {
            float h = (float)(totalHours - Math.Floor(totalHours / 24.0) * 24.0);
            float lat = climate.latitude * Mathf.Deg2Rad;
            Quaternion az = Quaternion.Euler(0f, _azimuthOffset, 0f);
            Vector3 pole = az * new Vector3(0f, Mathf.Sin(lat), Mathf.Cos(lat));
            Vector3 south = az * new Vector3(0f, Mathf.Cos(lat), -Mathf.Sin(lat));
            Vector3 west = az * Vector3.left;
            float hourAngle = (h - 12.5f) / 24f * Mathf.PI * 2f;
            float ch = Mathf.Cos(hourAngle), sh = Mathf.Sin(hourAngle);
            x = ch * south + sh * west;
            y = sh * south - ch * west;
            z = pole;
        }

        private static float MoonWeight(CelestialBodyDefinition m, float illum, float elevDeg)
        {
            if (m == null) return 0f;
            return illum * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-1f, 10f, elevDeg)) * m.lightShare;
        }

        private static void Moon(CelestialBodyDefinition m, double days, SkyRuntimeState s, out Vector3 dir, out float illum, out float phase, out float elev)
        {
            if (m == null) { dir = Vector3.down; illum = 0f; phase = 0f; elev = -90f; return; }
            double cycles = days / Math.Max(0.1, m.orbitalPeriodDays) + m.phaseOffset;
            phase = (float)(cycles - Math.Floor(cycles));
            float ra = phase * Mathf.PI * 2f; // Rektaszension relativ zur Sonne (Sonne bei 0)
            float dec = m.inclination * Mathf.Deg2Rad * Mathf.Sin(ra - m.longitude * Mathf.Deg2Rad);
            Vector3 c = new Vector3(Mathf.Cos(dec) * Mathf.Cos(ra), Mathf.Cos(dec) * Mathf.Sin(ra), Mathf.Sin(dec));
            dir = (c.x * s.CelestialX + c.y * s.CelestialY + c.z * s.CelestialZ).normalized;
            // Beleuchteter Anteil aus dem Winkel Sonne – Mond – Beobachter
            illum = Mathf.Clamp01((1f - Vector3.Dot(dir, s.SunDirection)) * 0.5f);
            elev = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        // ================================================================== Wind (stetig integriert)

        private Vector2 Wind(Vector2 baseWind, double seconds, double phase)
        {
            double omega = Math.PI * 2.0 / Math.Max(60.0, climate.windSwingDays * 24.0 * WorldClock.SecondsPerHour);
            double a = climate.windSwing * baseWind.magnitude;
            double ang = omega * seconds + phase;
            return baseWind + new Vector2((float)(a * Math.Cos(ang)), (float)(a * Math.Sin(ang)));
        }

        /// <summary>Weg der Wolken seit Weltbeginn = Integral des Windes (analytisch, daher auf allen Rechnern stetig gleich).</summary>
        private Vector2 WindOffset(Vector2 baseWind, double seconds, double phase)
        {
            double omega = Math.PI * 2.0 / Math.Max(60.0, climate.windSwingDays * 24.0 * WorldClock.SecondsPerHour);
            double a = climate.windSwing * baseWind.magnitude;
            double x = baseWind.x * seconds + a / omega * (Math.Sin(omega * seconds + phase) - Math.Sin(phase));
            double y = baseWind.y * seconds - a / omega * (Math.Cos(omega * seconds + phase) - Math.Cos(phase));
            return new Vector2((float)Wrap(x, 512.0), (float)Wrap(y, 512.0));
        }

        private static double Wrap(double v, double period) => v - Math.Floor(v / period) * period;

        // ================================================================== Shader-Werte

        private void ApplyMotion(double totalHours)
        {
            if (climate == null) return;
            double sec = totalHours * WorldClock.SecondsPerHour;
            Vector2 lo = WindOffset(climate.lowWind, sec, 1.3), hi = WindOffset(climate.highWind, sec, 4.1);
            float evolve = (float)Wrap(sec * climate.evolution, 512.0);
            SkyRuntimeState s = State;
            Shader.SetGlobalVector(LowOfsId, new Vector4(lo.x, lo.y, evolve, s.CloudDarkness));
            Vector2 wd = s.CloudWindHigh.sqrMagnitude > 1e-10f ? s.CloudWindHigh.normalized : Vector2.right;
            Shader.SetGlobalVector(HighOfsId, new Vector4(hi.x, hi.y, wd.x, wd.y));
            ApplyMeteors(sec);
            ApplyVeil(totalHours);
        }

        // ================================================================== Schleier (Kanon 30.09.)

        private static readonly int VeilNormalId = Shader.PropertyToID("_AshVeilNormal"), VeilTangentId = Shader.PropertyToID("_AshVeilTangent"),
            VeilParamsId = Shader.PropertyToID("_AshVeilParams");

        /// <summary>Hoehe des Schleiers ueber dem Horizont in Grad am letzten Betrachterort (Test/Anzeige).</summary>
        public static float VeilElevation { get; private set; }
        /// <summary>Staerke eines Schleierstosses gerade jetzt (0..1).</summary>
        public static float VeilPulse { get; private set; }

        /// <summary>
        /// Der Schleier ist die Milchstrasse mit ihrer Magie (Max 30.09.). Er dreht nicht mit den Sternen, sondern steht
        /// als Band ueber der Grossen Kerbe: ein Grosskreis entlang der Kerbe, dessen hoechster Punkt ueber der Kerbe liegt.
        /// Aus der Ferne steht er tief ueber dem Horizont in Richtung Kerbe, am Rand senkrecht ueber dem Betrachter.
        /// Lage aus dem Terrain-Generator (VeilScarPath) – dieselben Punkte wie die Kerbe im Gelaende.
        /// </summary>
        private void ApplyVeil(double totalHours)
        {
            VeilScarPath path = VeilScarPath.Load();
            Vector3? viewer = ViewerOverride;
            if (viewer == null && Camera.main != null) viewer = Camera.main.transform.position;
            if (path == null || path.points.Count < 2 || viewer == null)
            {
                Shader.SetGlobalVector(VeilNormalId, Vector4.zero);   // w = 0: bisherige Milchstrasse
                return;
            }
            Vector3 p = viewer.Value;
            float d = path.Nearest(new Vector2(p.x, p.z), out Vector2 closest, out Vector2 tangent, out float width, out float veil);
            Vector3 t1 = new Vector3(tangent.x, 0f, tangent.y).normalized;
            Vector2 toScar = closest - new Vector2(p.x, p.z);
            Vector3 u = toScar.sqrMagnitude > 1f ? new Vector3(toScar.x, 0f, toScar.y).normalized : Vector3.Cross(Vector3.up, t1);
            float horizontal = Mathf.Max(0f, d - width * 0.5f);
            float alpha = Mathf.Atan2(Mathf.Max(1f, path.veilHeight), horizontal);
            VeilElevation = alpha * Mathf.Rad2Deg;
            Vector3 top = (u * Mathf.Cos(alpha) + Vector3.up * Mathf.Sin(alpha)).normalized;
            Vector3 n = Vector3.Cross(t1, top).normalized;
            float bandWidth = 0.11f + 0.1f * Mathf.Sin(alpha);
            // gerastert: der Pfadverfolger backt seine Himmelskarte nur neu, wenn sich der Schleier sichtbar verschiebt
            Vector4 Q(Vector3 v, float w) => new Vector4(Mathf.Round(v.x * 256f) / 256f, Mathf.Round(v.y * 256f) / 256f, Mathf.Round(v.z * 256f) / 256f, Mathf.Round(w * 256f) / 256f);
            Shader.SetGlobalVector(VeilNormalId, Q(n, 1f));
            Shader.SetGlobalVector(VeilTangentId, Q(t1, bandWidth));

            // Schleierstoss: an manchen Tagen (Seed) glimmt das Band nachts fuer Minuten auf
            VeilPulse = VeilPulseAt(totalHours);
            float near = Mathf.Clamp01(1f - horizontal / 12000f);
            Shader.SetGlobalVector(VeilParamsId, new Vector4((float)Wrap(totalHours * 0.5, 4096.0), VeilPulse, Mathf.Max(near, veil * 0.5f), 0.35f));
        }

        /// <summary>Staerke eines Schleierstosses (0..1) zu einer Weltzeit – deterministisch aus dem Welt-Seed.</summary>
        public float VeilPulseAt(double totalHours)
        {
            if (climate == null) return 0f;
            int seed = climate.worldSeed ^ 0x5C1E1;
            long today = (long)Math.Floor(totalHours / 24.0);
            float best = 0f;
            for (long day = today - 1; day <= today; day++)
            {
                if (Hash01(day, seed) > 0.22f) continue;                   // etwa jeder vierte bis fuenfte Tag
                double start = day * 24.0 + 21.0 + Hash01(day, seed + 1) * 6.0;   // zwischen 21 und 3 Uhr
                double len = 0.25 + Hash01(day, seed + 2) * 0.35;          // 15–36 Weltminuten
                if (totalHours < start || totalHours > start + len) continue;
                best = Mathf.Max(best, Mathf.Sin(Mathf.PI * (float)((totalHours - start) / len)));
            }
            return best;
        }

        // ================================================================== Pass 12: Sternschnuppen

        /// <summary>
        /// Schauer-Nacht? Je Welt-Tag entscheidet der Seed, ob ein Ereignis stattfindet, wann es beginnt und wie lange es
        /// dauert. <paramref name="envelope"/> = Staerke 0..1 (steigt an, Hoehepunkt in der Mitte, klingt ab).
        /// </summary>
        public bool ShowerAt(double totalHours, out SkyEventDefinition ev, out float envelope)
        {
            ev = null;
            envelope = 0f;
            if (climate == null || climate.events == null) return false;
            long today = (long)Math.Floor(totalHours / 24.0);
            for (int e = 0; e < climate.events.Length; e++)
            {
                SkyEventDefinition def = climate.events[e];
                if (def == null) continue;
                for (long d = today - 1; d <= today; d++)
                {
                    if (!ShowerWindow(def, e, d, out double start, out double end)) continue;
                    if (totalHours < start || totalHours > end) continue;
                    float u = (float)((totalHours - start) / Math.Max(1e-3, end - start));
                    ev = def;
                    envelope = Mathf.Sin(Mathf.PI * u);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Zeitfenster (Weltstunden) des Ereignisses in der Nacht nach Tag <paramref name="day"/>, falls es stattfindet.</summary>
        public bool ShowerWindow(SkyEventDefinition def, int index, long day, out double start, out double end)
        {
            start = end = 0;
            int seed = climate.worldSeed * 31 + index * 977 + 0x5E7;
            if (Hash01(day, seed) >= def.chancePerDay) return false;
            float sh = def.startHour.x, eh = def.startHour.y;
            if (eh < sh) eh += 24f;
            start = day * 24.0 + Mathf.Lerp(sh, eh, Hash01(day, seed + 1));
            end = start + Mathf.Lerp(def.durationHours.x, def.durationHours.y, Hash01(day, seed + 2));
            return true;
        }

        /// <summary>Naechster Schauer ab einer Weltstunde (sucht hoechstens <paramref name="maxDays"/> Tage).</summary>
        public bool NextShower(double fromHours, int maxDays, out double start, out double end)
        {
            start = end = 0;
            if (climate == null || climate.events == null) return false;
            long d0 = (long)Math.Floor(fromHours / 24.0);
            for (long d = d0 - 1; d <= d0 + maxDays; d++)
                for (int e = 0; e < climate.events.Length; e++)
                    if (climate.events[e] != null && ShowerWindow(climate.events[e], e, d, out start, out end) && end > fromHours) return true;
            return false;
        }

        /// <summary>Sternschnuppen je echter Sekunde zu einer Weltzeit (nur nachts; Wolken verdecken sie spaeter im Shader).</summary>
        public float MeteorRate(double totalHours, out float showerShare, out SkyEventDefinition ev)
        {
            showerShare = 0f;
            ev = null;
            if (climate == null) return 0f;
            EnsureDesign();
            float h = (float)(totalHours - Math.Floor(totalHours / 24.0) * 24.0);
            float elev = Mathf.Asin(Mathf.Clamp(DayNightCycle.SunDirection(h, _designYaw, _elevScale).y, -1f, 1f)) * Mathf.Rad2Deg;
            float dark = Mathf.Clamp01((-elev - 6f) / 8f);
            if (dark <= 0f) return 0f;
            double sph = Math.Max(1.0, WorldClock.SecondsPerHour);
            float rate = (float)(climate.sporadicMeteorsPerHour / sph);
            if (ShowerAt(totalHours, out ev, out float env))
            {
                float sr = (float)(ev.frequency * 60.0 / sph) * env;
                showerShare = sr / Mathf.Max(1e-9f, rate + sr);
                rate += sr;
            }
            return rate * dark;
        }

        /// <summary>Sternschnuppe im Zeitschlitz (falls es eine gibt) – rein aus Seed und Schlitznummer.</summary>
        public bool MeteorInSlot(long slot, out Meteor m)
        {
            m = default;
            if (climate == null) return false;
            double sec = slot * (double)MeteorSlot;
            double totalHours = sec / Math.Max(1.0, WorldClock.SecondsPerHour);
            float rate = MeteorRate(totalHours, out float showerShare, out SkyEventDefinition ev);
            if (rate <= 0f) return false;
            int seed = climate.worldSeed ^ 0x3E7E0;
            if (Hash01(slot, seed) >= rate * MeteorSlot) return false;
            // Bei Schauer: Anteil der Schauer-Meteore = Schaueranteil der Rate
            bool fromShower = ev != null && Hash01(slot, seed + 1) < showerShare;
            float h1 = Hash01(slot, seed + 2), h2 = Hash01(slot, seed + 3), h3 = Hash01(slot, seed + 4), h4 = Hash01(slot, seed + 5), h5 = Hash01(slot, seed + 6);
            m.Start = sec + Hash01(slot, seed + 7) * MeteorSlot;
            m.Brightness = 0.45f + 0.95f * h5 * h5;
            m.Shower = fromShower;
            Vector3 from, dir;
            if (fromShower)
            {
                CelestialBasis(totalHours, out Vector3 cx, out Vector3 cy, out Vector3 cz);
                float ra = ev.radiant.x * Mathf.Deg2Rad, dec = ev.radiant.y * Mathf.Deg2Rad;
                Vector3 rc = new Vector3(Mathf.Cos(dec) * Mathf.Cos(ra), Mathf.Cos(dec) * Mathf.Sin(ra), Mathf.Sin(dec));
                Vector3 radiant = (rc.x * cx + rc.y * cy + rc.z * cz).normalized;
                Tangents(radiant, out Vector3 b1, out Vector3 b2);
                float off = Mathf.Lerp(6f, 38f, h1) * Mathf.Deg2Rad, phi = h2 * Mathf.PI * 2f;
                from = (radiant * Mathf.Cos(off) + (Mathf.Cos(phi) * b1 + Mathf.Sin(phi) * b2) * Mathf.Sin(off)).normalized;
                dir = (from * Vector3.Dot(from, radiant) - radiant).normalized; // vom Radianten weg
                m.Life = ev.trailLifetime * Mathf.Lerp(0.7f, 1.3f, h4);
                m.Brightness *= Mathf.Lerp(0.8f, 1.3f, ev.intensity);
            }
            else
            {
                float az = h1 * Mathf.PI * 2f, el = Mathf.Lerp(25f, 72f, h2) * Mathf.Deg2Rad;
                from = new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));
                Vector3 side = Vector3.Cross(Vector3.up, from).normalized;
                Vector3 down = (from * from.y - Vector3.up).normalized; // Tangente nach unten
                float th = Mathf.Lerp(15f, 165f, h3) * Mathf.Deg2Rad;
                dir = (Mathf.Cos(th) * side + Mathf.Sin(th) * down).normalized;
                m.Life = Mathf.Lerp(0.45f, 1.05f, h4);
            }
            if (from.y < 0.08f) return false; // unter dem Horizont: nicht zu sehen
            float len = Mathf.Lerp(7f, 22f, Hash01(slot, seed + 8)) * Mathf.Deg2Rad;
            m.From = from;
            m.To = (from * Mathf.Cos(len) + dir * Mathf.Sin(len)).normalized;
            return true;
        }

        /// <summary>Anzahl Sternschnuppen in einem Zeitraum (echte Sekunden seit Weltbeginn) – fuer Tests und Statistik.</summary>
        public int CountMeteors(double secFrom, double secTo, out int shower)
        {
            shower = 0;
            int n = 0;
            for (long i = (long)Math.Floor(secFrom / MeteorSlot); i * (double)MeteorSlot < secTo; i++)
            {
                if (!MeteorInSlot(i, out Meteor m)) continue;
                n++;
                if (m.Shower) shower++;
            }
            return n;
        }

        /// <summary>Gerade leuchtende Sternschnuppen (hoechstens <paramref name="buffer"/>.Length).</summary>
        public int ActiveMeteors(double sec, Meteor[] buffer)
        {
            int n = 0;
            long last = (long)Math.Floor(sec / MeteorSlot);
            long first = last - (long)Math.Ceiling(MaxMeteorLife / MeteorSlot) - 1;
            for (long i = last; i >= first && n < buffer.Length; i--)
            {
                if (!MeteorInSlot(i, out Meteor m)) continue;
                if (sec < m.Start || sec > m.Start + m.Life) continue;
                buffer[n++] = m;
            }
            return n;
        }

        private void ApplyMeteors(double sec)
        {
            int n = Application.isPlaying ? ActiveMeteors(sec, _meteorBuf) : 0;
            VisibleMeteors = n;
            for (int k = 0; k < 2; k++)
            {
                if (k >= n)
                {
                    Shader.SetGlobalVector(MeteorIds[k * 2], Vector4.zero);
                    Shader.SetGlobalVector(MeteorIds[k * 2 + 1], Vector4.zero);
                    continue;
                }
                Meteor m = _meteorBuf[k];
                float p = Mathf.Clamp01((float)((sec - m.Start) / m.Life));
                float b = m.Brightness * Mathf.Sin(Mathf.PI * Mathf.Min(1f, p * 1.15f));
                Shader.SetGlobalVector(MeteorIds[k * 2], new Vector4(m.From.x, m.From.y, m.From.z, p));
                Shader.SetGlobalVector(MeteorIds[k * 2 + 1], new Vector4(m.To.x, m.To.y, m.To.z, b));
            }
        }

        private static void Tangents(Vector3 n, out Vector3 a, out Vector3 b)
        {
            Vector3 up = Mathf.Abs(n.y) < 0.99f ? Vector3.up : Vector3.right;
            a = Vector3.Cross(up, n).normalized;
            b = Vector3.Cross(n, a);
        }

        private void ApplyState(SkyRuntimeState s)
        {
            SkyColorKey ck = climate.SampleColors(s.TimeOfDay);
            Color zen = ck.zenith, hor = ck.horizon, gnd = ck.ground, glow = ck.glow;
            // Bedeckter Himmel wird grauer und etwas dunkler
            float cov = Mathf.Clamp01(s.CloudCoverage * s.CloudDensity);
            zen = Color.Lerp(zen, Grey(zen) * 1.05f, cov * 0.7f) * Mathf.Lerp(1f, 0.82f, cov);
            hor = Color.Lerp(hor, Grey(hor), cov * 0.5f);
            Color hazeCol = Color.Lerp(hor, Grey(hor) * 1.1f, 0.4f);

            Shader.SetGlobalColor(ZenithId, zen.linear);
            Shader.SetGlobalColor(HorizonId, hor.linear);
            Shader.SetGlobalColor(GroundId, gnd.linear);
            Color gl = glow.linear;
            Shader.SetGlobalVector(GlowId, new Vector4(gl.r, gl.g, gl.b, glow.a * Mathf.Lerp(1f, 0.45f, cov)));
            bool sunUp = s.SunElevation > -1.5f;
            Shader.SetGlobalVector(SunId, new Vector4(s.SunDirection.x, s.SunDirection.y, s.SunDirection.z, sunUp ? 1f : 0f));
            Color sunCol = Color.Lerp(new Color(1f, 0.55f, 0.32f), new Color(1f, 0.93f, 0.82f), Mathf.InverseLerp(0f, 25f, s.SunElevation)).linear;
            Shader.SetGlobalVector(SunColorId, new Vector4(sunCol.r, sunCol.g, sunCol.b, s.SunTransmittance));

            Shader.SetGlobalVector(CelXId, s.CelestialX);
            Shader.SetGlobalVector(CelYId, s.CelestialY);
            Shader.SetGlobalVector(CelZId, s.CelestialZ);

            float dayLight = Mathf.Clamp01((s.SunElevation + 4f) / 16f);
            SetMoon(MoonAId, MoonAColorId, climate.moonA, s.MoonADirection, dayLight);
            SetMoon(MoonBId, MoonBColorId, climate.moonB, s.MoonBDirection, dayLight);
            Shader.SetGlobalVector(MoonSeedId, new Vector4(climate.moonA != null ? climate.moonA.surfaceSeed : 1, climate.moonB != null ? climate.moonB.surfaceSeed : 2, 0f, 0f));
            Shader.SetGlobalVector(StarsId, new Vector4(s.StarVisibility, climate.milkyWay, climate.starDensity, 1f));

            Shader.SetGlobalVector(LowId, new Vector4(s.CloudCoverage, s.CloudDensity, s.CloudScale, s.CloudErosion));
            Shader.SetGlobalVector(HighId, new Vector4(s.HighCoverage, s.HighDensity, 1f, 0f));

            // Wolkenfarben: beleuchtet (Horizont + Sonnenleuchten, nachts etwas Mondlicht) und Schattenseite
            Color moonTint = (climate.moonA != null ? climate.moonA.color : Color.white) * (0.05f * s.MoonLightFactor * (1f - dayLight));
            // Beleuchtete Wolke: hellgrau (Helligkeit des Horizonts), Sonnenleuchten; Schattenseite kuehl und dunkler
            float bright = Mathf.Max(hor.grayscale, 0.02f);
            // Dichte Decke: weniger Abendglut in den Wolken, grauer und dunkler (schwere Wolkenbank)
            Color lit = Color.Lerp(hor, new Color(1f, 0.98f, 0.95f) * bright * Mathf.Lerp(1.35f, 0.95f, cov), 0.55f)
                        + glow * (glow.a * 0.5f * (1f - 0.75f * cov)) + moonTint;
            Color shade = Color.Lerp(lit, zen, 0.45f) * Mathf.Lerp(0.62f, 0.3f, s.CloudDarkness);
            Shader.SetGlobalColor(LitId, lit.linear);
            Shader.SetGlobalColor(ShadowId, shade.linear);
            Color hz = hazeCol.linear;
            Shader.SetGlobalVector(HazeId, new Vector4(hz.r, hz.g, hz.b, s.HorizonHaze));

            if (_constellation != null)
            {
                Shader.SetGlobalVectorArray(ConstId, _constellation);
                Shader.SetGlobalFloat(ConstCountId, _constellationCount);
            }
        }

        private static void SetMoon(int dirId, int colorId, CelestialBodyDefinition m, Vector3 dir, float dayLight)
        {
            if (m == null)
            {
                Shader.SetGlobalVector(dirId, new Vector4(0f, -1f, 0f, 0.001f));
                Shader.SetGlobalVector(colorId, Vector4.zero);
                return;
            }
            Color c = m.color.linear;
            // Tagsueber blasser (Mond am Taghimmel), nachts voll
            float vis = Mathf.Lerp(1f, 0.35f, dayLight);
            Shader.SetGlobalVector(dirId, new Vector4(dir.x, dir.y, dir.z, m.apparentRadius * Mathf.Deg2Rad));
            Shader.SetGlobalVector(colorId, new Vector4(c.r, c.g, c.b, vis));
        }

        private static Color Grey(Color c)
        {
            float g = c.grayscale;
            return new Color(g, g, g, c.a);
        }

        private void BuildConstellations()
        {
            _constellation = new Vector4[MaxConstellationStars];
            _constellationCount = 0;
            if (climate == null || climate.constellations == null) return;
            foreach (Constellation con in climate.constellations)
            {
                if (con?.stars == null) continue;
                foreach (ConstellationStar st in con.stars)
                {
                    if (_constellationCount >= MaxConstellationStars) return;
                    float ra = st.ra * Mathf.Deg2Rad, dec = st.dec * Mathf.Deg2Rad;
                    _constellation[_constellationCount++] = new Vector4(Mathf.Cos(dec) * Mathf.Cos(ra), Mathf.Cos(dec) * Mathf.Sin(ra), Mathf.Sin(dec), st.brightness);
                }
            }
        }

        /// <summary>Stetiges 1D-Rauschen 0..1 (Wertrauschen mit weicher Interpolation).</summary>
        public static float SmoothNoise(double x, int seed)
        {
            double i = Math.Floor(x);
            float f = (float)(x - i);
            float a = Hash01((long)i, seed), b = Hash01((long)i + 1, seed);
            return Mathf.Lerp(a, b, f * f * (3f - 2f * f));
        }

        private static float Hash01(long i, int seed)
        {
            unchecked
            {
                uint h = (uint)i * 0x9E3779B1u ^ (uint)(i >> 32) * 0x85EBCA77u ^ (uint)seed * 0xC2B2AE3Du;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }
    }
}
