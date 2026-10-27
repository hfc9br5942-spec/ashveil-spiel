using Ashveil.Core;
using Ashveil.Rendering.Sky;
using Ashveil.World;
using UnityEngine;

namespace Ashveil.Rendering
{
    /// <summary>
    /// M8: Tag und Nacht aus der gemeinsamen Weltuhr (<see cref="WorldClock"/>). Sonne wandert (Aufgang 5:30,
    /// Untergang 19:30), nachts wird dasselbe Licht zum kuehlen Mond. Umgebungslicht, Nebelfarbe, Himmel und Spiegelungen
    /// folgen Stuetzstellen; 15:30 entspricht genau dem gestalteten Licht aus der Szene (Richtung, Farbe, Staerke,
    /// Schattenstaerke, Spiegelungen, Nebel und Sonnenstreuung – Sandbox wie Kareth v4 mit Stil A).
    /// Unter Tage (IndoorAtmosphere) ruht alles. Laeuft nur im Spiel – die Szene im Editor bleibt beim gestalteten Licht;
    /// fuer Review-Bilder ohne Play gibt es die Vorschau (<see cref="PreviewBegin"/>, Welle 0 06.10.2026).
    /// Zwei Saetze Stuetzstellen (<see cref="LichtProfil"/>): „Standard“ (Sandbox, Spieltest r2 – unveraendert) und
    /// „Stil A“ (Kareth v4, #79 07.10.2026): Tag relativ zum gestalteten Stil-A-Licht, Morgen und Abend hell und farbig mit
    /// tiefem Goldlicht, Nacht blaugrau statt tiefblau. Den Stil-A-Satz setzt <c>StilAKareth</c> beim Anwenden.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DayNightCycle : MonoBehaviour
    {
        public const float Sunrise = 5.5f, Sunset = 19.5f, DesignHour = 15.5f;

        /// <summary>Welche Stuetzstellen der Zyklus nutzt (15:30 ist in beiden das gestaltete Licht der Szene).</summary>
        public enum LichtProfil { Standard = 0, StilA = 1 }

        private struct Key
        {
            public float H, Intensity, Exposure, Scatter;
            public Color Light, Sky, Equator, Ground, Fog, SkyTint;
        }

        [SerializeField] private Light sun;
        [SerializeField, Tooltip("Standard = bisherige Stuetzstellen (Sandbox, Spieltest r2); Stil A = auf Stil A abgestimmt (Kareth v4, #79).")]
        private LichtProfil profil = LichtProfil.Standard;

        /// <summary>Aktiver Satz Stuetzstellen.</summary>
        public LichtProfil Profil => profil;

        public static DayNightCycle Active { get; private set; }
        /// <summary>0 = heller Tag … 1 = tiefe Nacht (fuer Laternen, NPCs, Tiere).</summary>
        public static float Night { get; private set; }
        public bool MoonActive { get; private set; }
        public float CurrentHour { get; private set; }
        /// <summary>Aus dieser Richtung (Himmelsrichtung, Grad) scheint das gestaltete Nachmittagslicht.</summary>
        public float DesignYaw => _designYaw;
        /// <summary>Hoehenfaktor der Sonnenbahn (aus dem gestalteten Licht).</summary>
        public float ElevationScale => _elevScale;
        public bool HasDesign { get; private set; }

        private Key[] _keys;
        private float _envHour = -100f;
        private bool _envDirty = true;

        /// <summary>Wie oft Umgebungslicht/Himmel neu gesetzt wurden (Tests/Leistung).</summary>
        public int EnvironmentUpdates { get; private set; }
        private float _designYaw, _elevScale;
        private Material _sky;
        private float _baseSunSize;
        // Gestalteter Stand, den 15:30 genau zeigt (06.10.: Stil A in Kareth v4 hat andere Werte als die Sandbox)
        private float _designShadow = 0.92f, _designReflection = 0.5f;
        private Color _designScatter = new Color(1.1f, 0.78f, 0.5f);

        private void Awake()
        {
            if (sun == null) sun = RenderSettings.sun;
            if (sun == null) { enabled = false; return; }
            CaptureDesign();
            if (RenderSettings.skybox != null)
            {
                _sky = new Material(RenderSettings.skybox) { name = RenderSettings.skybox.name + " (Tageszeit)" };
                RenderSettings.skybox = _sky;
                _baseSunSize = _sky.HasProperty("_SunSize") ? _sky.GetFloat("_SunSize") : 0.03f;
            }
        }

        /// <summary>Bahn und Werte des gestalteten Lichts merken (vor jeder Aenderung durch den Zyklus).</summary>
        private void CaptureDesign()
        {
            DesignFromLight(sun.transform.rotation, out _designYaw, out _elevScale);
            _designShadow = sun.shadowStrength;
            _designReflection = RenderSettings.reflectionIntensity;
            HasDesign = true;
        }

        private void OnEnable() => Active = this;

        // Profil im Inspector gewechselt: Stuetzstellen beim naechsten Bild neu aufbauen
        private void OnValidate()
        {
            if (_preview != null) return;
            _keys = null;
            _envDirty = true;
        }

        private void OnDisable()
        {
            if (Active == this) Active = null;
            LightFlicker.NightBoost = 0f;
            Night = 0f;
        }

        private Key BuildDesignKey()
        {
            var atmo = AtmosphereController.Active;
            if (atmo != null) _designScatter = atmo.SunScatter;
            return new Key
            {
                H = DesignHour,
                Light = sun.color,
                Intensity = sun.intensity,
                Sky = RenderSettings.ambientSkyColor,
                Equator = RenderSettings.ambientEquatorColor,
                Ground = RenderSettings.ambientGroundColor,
                Fog = atmo != null ? atmo.FogColor : new Color(0.42f, 0.46f, 0.52f),
                Scatter = 1f,
                Exposure = _sky != null && _sky.HasProperty("_Exposure") ? _sky.GetFloat("_Exposure") : 1f,
                SkyTint = _sky != null && _sky.HasProperty("_SkyTint") ? _sky.GetColor("_SkyTint") : new Color(0.42f, 0.46f, 0.55f)
            };
        }

        private static Key K(float h, Color light, float intensity, Color sky, Color eq, Color ground, Color fog, float scatter, float exposure, Color tint) =>
            new Key { H = h, Light = light, Intensity = intensity, Sky = sky, Equator = eq, Ground = ground, Fog = fog, Scatter = scatter, Exposure = exposure, SkyTint = tint };

        private Key[] BuildKeys() => profil == LichtProfil.StilA ? BuildKeysStilA() : BuildKeysStandard();

        private static Color Mul(Color c, float r, float g, float b) => new Color(c.r * r, c.g * g, c.b * b, 1f);
        private static Color Mul(Color c, float f) => Mul(c, f, f, f);
        private static Color Mix(Color a, Color b, float t) { Color c = Color.Lerp(a, b, t); c.a = 1f; return c; }

        /// <summary>
        /// Stil A (ART-108, ART-111; #79 07.10.2026): Der Tag (Vormittag bis spaeter Nachmittag) folgt dem gestalteten
        /// Stil-A-Licht (relativ zu 15:30, damit eine spaetere Aenderung von Stil A den ganzen Tag mitnimmt). Morgen und Abend
        /// bleiben hell und farbig – rosig-goldener Morgen, tiefes Goldlicht am Abend, warme Umgebung statt Dunkelheit;
        /// die Nacht ist blaugrau statt tiefblau (die kuehlen Schatten macht schon das Stil-A-Profil), Feuer und Fenster bleiben
        /// die warmen Inseln. Wechsel Nachtlicht ↔ Sonne bei 5:30 und 19:30 mit fast ausgeblendetem Licht wie im Standard.
        /// Morgen und Abend: Steht die Sonne tief, liegt das Tal im Schatten der Berge und das zurueckgeworfene Licht besonnter
        /// Flaechen fehlt – deshalb ist das Himmelslicht dann heller als am Tag (gemessen 07.10.: sonst Vordergrund um 6 und
        /// 19 Uhr ein Zehntel von 15:30). Es bleibt kuehl: warmes oder sehr helles Himmelslicht macht das Gras blassgelb.
        /// </summary>
        private Key[] BuildKeysStilA()
        {
            Key d = BuildDesignKey();
            Color grau = new Color(0.5f, 0.5f, 0.5f);
            // Nacht: Mondlicht blaugrau, Umgebung fast neutral – das Stil-A-Profil (Split Toning) kuehlt dunkle Bereiche ohnehin
            Key night = K(0f, new Color(0.84f, 0.88f, 1f), 0.45f, new Color(0.22f, 0.23f, 0.27f), new Color(0.14f, 0.145f, 0.165f),
                new Color(0.055f, 0.053f, 0.055f), new Color(0.095f, 0.1f, 0.12f), 0f, 0.13f, new Color(0.36f, 0.37f, 0.44f));
            // Blaue Stunde vor Sonnenaufgang und nach Sonnenuntergang: noch Nachtlicht, Umgebung schon heller
            Key blau = K(0f, new Color(0.8f, 0.84f, 1f), 0.25f, new Color(0.4f, 0.41f, 0.52f), new Color(0.29f, 0.28f, 0.33f),
                new Color(0.11f, 0.1f, 0.1f), new Color(0.24f, 0.24f, 0.29f), 0.3f, 0.32f, new Color(0.38f, 0.37f, 0.47f));
            Key[] k =
            {
                night,
                At(night, 4.4f),
                At(blau, 5.05f),
                // Sonnenaufgang: Licht fast weg (Wechsel Nachtlicht → Sonne), Umgebung lavendel
                K(Sunrise, new Color(1f, 0.58f, 0.44f), 0.04f, new Color(0.5f, 0.5f, 0.67f), new Color(0.46f, 0.41f, 0.46f), new Color(0.17f, 0.15f, 0.14f),
                    new Color(0.46f, 0.4f, 0.46f), 0.9f, 0.55f, new Color(0.48f, 0.4f, 0.46f)),
                // Morgenrot: rosig-goldene Sonne, kuehles lavendelblaues Himmelslicht (Schatten kuehl, Gras bleibt gruen), rosiger Dunst
                K(6.1f, new Color(1f, 0.66f, 0.48f), d.Intensity * 0.65f, new Color(0.73f, 0.73f, 0.92f), new Color(0.68f, 0.62f, 0.68f), new Color(0.26f, 0.22f, 0.21f),
                    new Color(0.62f, 0.56f, 0.62f), 1.5f, 0.9f, new Color(0.52f, 0.44f, 0.48f)),
                // Morgen: die Sonne kommt ins Tal, das Himmelslicht geht auf Stil A zurueck.
                // 10.10.2026 (Wiesen-Pass, CORE-009): Dunst am Morgen und Vormittag kuehl statt gelblich – die Ferne wirkte um 09:00
                // ausgebleicht gelbweiss; warm bleibt nur das Sonnenlicht (farbige Luftperspektive, ART-111). Vorher Fog ×1,07/0,97/0,93
                // und ×1,03/1,03/1,02.
                K(7.3f, new Color(1f, 0.76f, 0.58f), d.Intensity * 0.88f, new Color(0.68f, 0.7f, 0.84f), new Color(0.58f, 0.56f, 0.58f), new Color(0.26f, 0.23f, 0.2f),
                    Mul(d.Fog, 1.0f, 0.99f, 1.0f), 1.2f, d.Exposure, Mix(d.SkyTint, new Color(0.52f, 0.47f, 0.5f), 0.5f)),
                // Vormittag: wie Stil A, Licht etwas weisser, Himmel milchig statt satt blau
                K(10f, new Color(1f, 0.89f, 0.75f), d.Intensity * 1.05f, d.Sky, d.Equator, Mul(d.Ground, 0.95f), Mul(d.Fog, 0.98f, 1.0f, 1.04f),
                    0.8f, d.Exposure * 1.1f, Mix(d.SkyTint, grau, 0.7f)),
                // Mittag: hellster, neutralster Stand
                K(12.5f, new Color(1f, 0.92f, 0.8f), d.Intensity * 1.1f, d.Sky, Mul(d.Equator, 1.02f), Mul(d.Ground, 0.95f), Mul(d.Fog, 1.04f),
                    0.7f, d.Exposure * 1.1f, Mix(d.SkyTint, grau, 0.6f)),
                d,
                // Spaeter Nachmittag: Licht wird waermer
                K(17.4f, new Color(1f, 0.76f, 0.54f), d.Intensity * 0.95f, Mul(d.Sky, 0.98f, 0.96f, 0.95f), Mul(d.Equator, 1.04f, 0.96f, 0.9f), Mul(d.Ground, 1.04f, 0.96f, 0.9f),
                    Mul(d.Fog, 1.09f, 0.97f, 0.85f), 1.25f, d.Exposure * 0.96f, Mix(d.SkyTint, new Color(0.52f, 0.47f, 0.48f), 0.5f)),
                // Goldene Stunde: tiefes Goldlicht der Sonne (ART-111), Himmelslicht hell und kuehl-lavendel (warme Lichter, kuehle
                // Schatten – Stil A), goldener Dunst
                K(18.75f, new Color(1f, 0.6f, 0.33f), d.Intensity * 0.85f, new Color(0.76f, 0.72f, 0.86f), new Color(0.8f, 0.64f, 0.56f), new Color(0.32f, 0.24f, 0.18f),
                    new Color(0.78f, 0.62f, 0.5f), 1.75f, 0.95f, new Color(0.55f, 0.46f, 0.44f)),
                // Abendrot
                K(19.25f, new Color(1f, 0.5f, 0.3f), d.Intensity * 0.45f, new Color(0.66f, 0.6f, 0.76f), new Color(0.68f, 0.52f, 0.48f), new Color(0.26f, 0.19f, 0.15f),
                    new Color(0.62f, 0.47f, 0.44f), 1.5f, 0.8f, new Color(0.55f, 0.42f, 0.43f)),
                // Sonnenuntergang: Licht fast weg (Wechsel Sonne → Nachtlicht)
                K(Sunset, new Color(1f, 0.45f, 0.3f), 0.04f, new Color(0.52f, 0.47f, 0.63f), new Color(0.48f, 0.38f, 0.39f), new Color(0.19f, 0.14f, 0.13f),
                    new Color(0.44f, 0.34f, 0.37f), 1f, 0.6f, new Color(0.47f, 0.36f, 0.41f)),
                At(blau, 19.95f),
                At(night, 21f),
                At(night, 24f)
            };
            return k;
        }

        private Key[] BuildKeysStandard()
        {
            // Nacht: kuehles Mondlicht, dunkelblaue Umgebung – spielbar, aber deutlich Nacht.
            Color moon = new Color(0.62f, 0.72f, 1f);
            Key night = K(0f, moon, 0.42f, new Color(0.15f, 0.18f, 0.29f), new Color(0.1f, 0.11f, 0.16f), new Color(0.045f, 0.045f, 0.055f),
                new Color(0.09f, 0.11f, 0.16f), 0f, 0.13f, new Color(0.25f, 0.3f, 0.5f));
            Key design = BuildDesignKey();
            Key[] k =
            {
                night,
                At(night, 4.6f),
                // Morgengrauen: Licht fast weg (Wechsel Mond → Sonne), Umgebung blaeulich-violett
                K(Sunrise, new Color(1f, 0.55f, 0.42f), 0.04f, new Color(0.36f, 0.35f, 0.48f), new Color(0.29f, 0.24f, 0.27f), new Color(0.1f, 0.085f, 0.08f),
                    new Color(0.3f, 0.26f, 0.3f), 0.8f, 0.35f, new Color(0.5f, 0.38f, 0.42f)),
                // Sonnenaufgang: rosig-golden
                K(6.6f, new Color(1f, 0.62f, 0.42f), 1.2f, new Color(0.42f, 0.42f, 0.52f), new Color(0.34f, 0.29f, 0.28f), new Color(0.11f, 0.1f, 0.09f),
                    new Color(0.52f, 0.43f, 0.4f), 1.3f, 0.7f, new Color(0.5f, 0.42f, 0.45f)),
                // Vormittag: klar
                K(9f, new Color(1f, 0.88f, 0.74f), 2.0f, new Color(0.52f, 0.58f, 0.7f), new Color(0.38f, 0.38f, 0.38f), new Color(0.13f, 0.12f, 0.11f),
                    new Color(0.46f, 0.5f, 0.56f), 0.9f, 1.0f, new Color(0.42f, 0.47f, 0.57f)),
                // Mittag: neutral-hell
                K(12.5f, new Color(1f, 0.93f, 0.84f), 2.3f, new Color(0.56f, 0.61f, 0.72f), new Color(0.4f, 0.4f, 0.4f), new Color(0.14f, 0.13f, 0.12f),
                    new Color(0.47f, 0.51f, 0.57f), 0.8f, 1.05f, new Color(0.42f, 0.47f, 0.57f)),
                design,
                // Goldene Stunde
                K(18.2f, new Color(1f, 0.6f, 0.36f), 1.6f, new Color(0.44f, 0.42f, 0.5f), new Color(0.38f, 0.3f, 0.26f), new Color(0.12f, 0.1f, 0.08f),
                    new Color(0.56f, 0.44f, 0.36f), 1.5f, 0.8f, new Color(0.52f, 0.42f, 0.4f)),
                // Abenddaemmerung: Licht fast weg (Wechsel Sonne → Mond)
                K(Sunset, new Color(1f, 0.45f, 0.3f), 0.04f, new Color(0.35f, 0.32f, 0.45f), new Color(0.29f, 0.22f, 0.23f), new Color(0.1f, 0.08f, 0.07f),
                    new Color(0.3f, 0.22f, 0.24f), 1f, 0.32f, new Color(0.46f, 0.33f, 0.38f)),
                At(night, 20.8f),
                At(night, 24f)
            };
            return k;
        }

        private static Key At(Key k, float h) { k.H = h; return k; }

        private Key Sample(float h)
        {
            for (int i = 0; i < _keys.Length - 1; i++)
            {
                Key a = _keys[i], b = _keys[i + 1];
                if (h < a.H || h > b.H) continue;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a.H, b.H, h));
                return new Key
                {
                    H = h,
                    Light = Color.Lerp(a.Light, b.Light, t),
                    Intensity = Mathf.Lerp(a.Intensity, b.Intensity, t),
                    Sky = Color.Lerp(a.Sky, b.Sky, t),
                    Equator = Color.Lerp(a.Equator, b.Equator, t),
                    Ground = Color.Lerp(a.Ground, b.Ground, t),
                    Fog = Color.Lerp(a.Fog, b.Fog, t),
                    Scatter = Mathf.Lerp(a.Scatter, b.Scatter, t),
                    Exposure = Mathf.Lerp(a.Exposure, b.Exposure, t),
                    SkyTint = Color.Lerp(a.SkyTint, b.SkyTint, t)
                };
            }
            return _keys[0];
        }

        /// <summary>Bahn der Sonne aus dem gestalteten Licht (Richtung um 15:30).</summary>
        public static void DesignFromLight(Quaternion designRotation, out float designYaw, out float elevScale)
        {
            Vector3 e = designRotation.eulerAngles;
            designYaw = e.y;
            float designElev = Mathf.DeltaAngle(0f, e.x);
            elevScale = designElev / Mathf.Max(0.2f, Mathf.Sin(Mathf.PI * (DesignHour - Sunrise) / (Sunset - Sunrise)));
        }

        /// <summary>
        /// Richtung ZUR Sonne zur Stunde h – tagsueber wie das Sonnenlicht, nachts stetig unter dem Horizont weiter
        /// (fuer Himmel, Daemmerungsleuchten und Mondphasen). Ueber 24 h ohne Sprung.
        /// </summary>
        public static Vector3 SunDirection(float h, float designYaw, float elevScale)
        {
            float span = Sunset - Sunrise;
            float elev, yaw;
            if (h >= Sunrise && h <= Sunset)
            {
                elev = elevScale * Mathf.Sin(Mathf.PI * (h - Sunrise) / span);
                yaw = designYaw + (h - DesignHour) * (180f / span);
            }
            else
            {
                float t = Mathf.Repeat(h - Sunset, 24f) / (24f - span);
                elev = -elevScale * 0.8f * Mathf.Sin(Mathf.PI * t);
                yaw = designYaw + (Sunset - DesignHour) * (180f / span) + 180f * t;
            }
            return -(Quaternion.Euler(elev, yaw, 0f) * Vector3.forward);
        }

        public Vector3 SunDirection(float h) => SunDirection(h, _designYaw, _elevScale);

        /// <summary>Sonnenstand zur Stunde h (Tag): (Hoehe, Richtung) in Grad.</summary>
        public Vector2 SunAngles(float h)
        {
            float elev = _elevScale * Mathf.Sin(Mathf.PI * (h - Sunrise) / (Sunset - Sunrise));
            float yaw = _designYaw + (h - DesignHour) * (180f / (Sunset - Sunrise));
            return new Vector2(Mathf.Max(3f, elev), yaw);
        }

        private void LateUpdate()
        {
            if (sun == null || _preview != null) return;
            _keys ??= BuildKeys(); // erst hier: Nebel-Steuerung ist sicher aktiv
            ApplyHour(WorldClock.Hours);
        }

        /// <summary>Licht, Umgebung, Nebel und Himmel auf die Stunde h stellen.</summary>
        private void ApplyHour(float h)
        {
            CurrentHour = h;
            if (AtmosphereController.IndoorOverride) { _envDirty = true; return; }

            Key k = Sample(h);
            bool day = WorldClock.InWindow(h, Sunrise, Sunset);
            MoonActive = !day;
            if (day)
            {
                Vector2 a = SunAngles(h);
                sun.transform.rotation = Quaternion.Euler(a.x, a.y, 0f);
            }
            else if (SkyDirector.HasMoonLight)
            {
                // Dynamic Sky: ein Mondlicht aus der Richtung der hellen Monde (§ 20)
                sun.transform.rotation = Quaternion.LookRotation(-SkyDirector.MoonLightDirection);
            }
            else
            {
                // Kein Mond am Himmel: diffuses Nachtlicht aus einer ruhigen Richtung
                float nightT = Mathf.Repeat(h - Sunset, 24f) / (24f - (Sunset - Sunrise));
                float elev = 22f + 26f * Mathf.Sin(Mathf.PI * nightT);
                sun.transform.rotation = Quaternion.Euler(elev, _designYaw + 150f + 70f * nightT, 0f);
            }
            sun.color = k.Light;
            // Wolken daempfen die Sonne (Sonnendurchlass), nachts bestimmen die Monde das Licht
            sun.intensity = k.Intensity * (day ? SkyDirector.SunTransmittance : SkyDirector.MoonLightFactor);
            sun.shadowStrength = day ? _designShadow : 0.6f;

            // Umgebung (Umgebungslicht, Himmel, Spiegelungen) nur bei spuerbarer Aenderung neu setzen: jede Aenderung
            // laesst Unity das Umgebungslicht neu berechnen – jedes Bild waere teuer. 0,01 h = 36 s echte Zeit (Echtzeit-Uhr).
            float dh = Mathf.Abs(Mathf.DeltaAngle(h * 15f, _envHour * 15f) / 15f);
            if (dh < 0.01f && !_envDirty) return;
            _envHour = h;
            _envDirty = false;
            EnvironmentUpdates++;

            RenderSettings.ambientSkyColor = k.Sky * Mathf.Lerp(1f, 0.86f, SkyDirector.Coverage);
            RenderSettings.ambientEquatorColor = k.Equator;
            RenderSettings.ambientGroundColor = k.Ground;

            float dayLight = Mathf.InverseLerp(0.13f, 1f, k.Exposure);
            Night = 1f - Mathf.Clamp01(dayLight * 1.4f);
            RenderSettings.reflectionIntensity = Mathf.Lerp(0.12f, _designReflection, dayLight);
            LightFlicker.NightBoost = Night;

            var atmo = AtmosphereController.Active;
            if (atmo != null)
            {
                atmo.FogColor = k.Fog;
                atmo.SunScatter = _designScatter * k.Scatter;
            }
            if (_sky != null)
            {
                if (_sky.HasProperty("_Exposure")) _sky.SetFloat("_Exposure", k.Exposure);
                if (_sky.HasProperty("_SkyTint")) _sky.SetColor("_SkyTint", k.SkyTint);
                if (_sky.HasProperty("_SunSize")) _sky.SetFloat("_SunSize", day ? _baseSunSize : _baseSunSize * 0.7f);
            }
        }

        // ================================================================== Vorschau ohne Play (Review-Bilder)

        private sealed class Vorher
        {
            public Quaternion SunRotation;
            public Color SunColor, AmbSky, AmbEquator, AmbGround, Fog, Scatter, SkyTint;
            public float SunIntensity, SunShadow, Reflection, Exposure, SunSize, NightBoost, Night;
            public bool HasExposure, HasTint, HasSunSize;
        }

        private Vorher _preview;

        /// <summary>Laeuft gerade eine Vorschau (nur Editor ohne Play)?</summary>
        public bool InPreview => _preview != null;

        /// <summary>
        /// Nur im Editor ohne Play: Vorschau beginnen. Merkt den gestalteten Stand (Sonne, Umgebung, Nebel, Himmelswerte)
        /// und stellt ihn mit <see cref="PreviewEnd"/> genau wieder her. Das Himmelsmaterial der Szene wird waehrend der
        /// Vorschau direkt veraendert und danach zurueckgesetzt.
        /// </summary>
        public bool PreviewBegin()
        {
            if (Application.isPlaying || _preview != null) return false;
            if (sun == null) sun = RenderSettings.sun;
            if (sun == null) return false;
            var atmo = AtmosphereController.Active;
            Material sky = RenderSettings.skybox;
            _preview = new Vorher
            {
                SunRotation = sun.transform.rotation, SunColor = sun.color, SunIntensity = sun.intensity, SunShadow = sun.shadowStrength,
                AmbSky = RenderSettings.ambientSkyColor, AmbEquator = RenderSettings.ambientEquatorColor, AmbGround = RenderSettings.ambientGroundColor,
                Reflection = RenderSettings.reflectionIntensity,
                Fog = atmo != null ? atmo.FogColor : default, Scatter = atmo != null ? atmo.SunScatter : default,
                HasExposure = sky != null && sky.HasProperty("_Exposure"), HasTint = sky != null && sky.HasProperty("_SkyTint"),
                HasSunSize = sky != null && sky.HasProperty("_SunSize"),
                NightBoost = LightFlicker.NightBoost, Night = Night
            };
            if (_preview.HasExposure) _preview.Exposure = sky.GetFloat("_Exposure");
            if (_preview.HasTint) _preview.SkyTint = sky.GetColor("_SkyTint");
            if (_preview.HasSunSize) _preview.SunSize = sky.GetFloat("_SunSize");
            _sky = sky;
            _baseSunSize = _preview.HasSunSize ? _preview.SunSize : 0.03f;
            CaptureDesign();
            _keys = BuildKeys();
            _envDirty = true;
            return true;
        }

        /// <summary>Vorschau: Szene auf Stunde h stellen (nach <see cref="PreviewBegin"/>).</summary>
        public void PreviewAt(float h)
        {
            if (_preview == null) return;
            _envDirty = true;
            ApplyHour(Mathf.Repeat(h, 24f));
            // Ohne Play laufen Update-Schleifen nicht von selbst: Nebelwerte und Umgebungslicht sofort uebernehmen
            AtmosphereController a = AtmosphereController.Active;
            if (a != null) a.Apply();
            DynamicGI.UpdateEnvironment();
        }

        /// <summary>Vorschau beenden: gestalteten Stand genau wiederherstellen.</summary>
        public void PreviewEnd()
        {
            Vorher v = _preview;
            if (v == null) return;
            _preview = null;
            sun.transform.rotation = v.SunRotation;
            sun.color = v.SunColor;
            sun.intensity = v.SunIntensity;
            sun.shadowStrength = v.SunShadow;
            RenderSettings.ambientSkyColor = v.AmbSky;
            RenderSettings.ambientEquatorColor = v.AmbEquator;
            RenderSettings.ambientGroundColor = v.AmbGround;
            RenderSettings.reflectionIntensity = v.Reflection;
            var atmo = AtmosphereController.Active;
            if (atmo != null)
            {
                atmo.FogColor = v.Fog;
                atmo.SunScatter = v.Scatter;
                atmo.Apply();
            }
            if (_sky != null)
            {
                if (v.HasExposure) _sky.SetFloat("_Exposure", v.Exposure);
                if (v.HasTint) _sky.SetColor("_SkyTint", v.SkyTint);
                if (v.HasSunSize) _sky.SetFloat("_SunSize", v.SunSize);
            }
            LightFlicker.NightBoost = v.NightBoost;
            Night = v.Night;
            DynamicGI.UpdateEnvironment();
            _sky = null;
            _keys = null;
            _envHour = -100f;
            _envDirty = true;
            MoonActive = false;
        }
    }
}
