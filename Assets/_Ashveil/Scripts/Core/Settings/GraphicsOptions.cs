using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Core.Settings
{
    /// <summary>Grafikstufe. Die drei Stufen sind Voreinstellungen – jeder Einzelwert bleibt frei einstellbar.</summary>
    public enum GraphicsTier
    {
        /// <summary>Ab GeForce RTX 3050/3060 (RX 6600): 1080p, 60 Bilder/s auch mit 50 Spielern in der Naehe.</summary>
        Niedrig = 0,
        /// <summary>Durchschnitts-Gaming-PC (RTX 3060 Ti–3080, 4060/4060 Ti, RX 6700 XT): 1080p–1440p, 60 Bilder/s.</summary>
        Mittel = 1,
        /// <summary>RTX 4070 und hoeher, RTX 50xx: 1440p–4K mit allen Effekten.</summary>
        Hoch = 2,
        /// <summary>RTX 4090, RTX 5080/5090: 4K bis 8K, maximale Sichtweite, Schatten und Dichte (27.09., Wunsch Max).</summary>
        Episch = 3,
        /// <summary>Mindestens ein Einzelwert weicht von der gewaehlten Stufe ab. (Bis Version 1 der Datei = 3; die Stufe
        /// wird beim Laden ohnehin aus den Einzelwerten neu bestimmt.)</summary>
        Benutzerdefiniert = 4
    }

    /// <summary>
    /// Alle Grafikeinstellungen eines Rechners (nicht pro Charakter). Reine Daten – angewendet werden sie von
    /// <c>Ashveil.Rendering.GraphicsApplier</c> (Render-Pipeline, Gelaende, Kamera, Nachbearbeitung) und
    /// <c>Ashveil.Gameplay.Visuals.CharacterLod</c> (Detail der Figuren bei vielen Spielern).
    /// Jeder Wert ist ein Index in die Auswahl von <see cref="GraphicsCatalog"/>.
    /// </summary>
    [Serializable]
    public sealed class GraphicsOptions
    {
        public int version = 4;
        public GraphicsTier tier = GraphicsTier.Mittel;

        // Anzeige
        /// <summary>Veraltet seit Version 4 – folgt <see cref="displayMode"/> (true = kein Fenster). Bleibt fuer alte Dateien.</summary>
        public bool fullscreen = true;
        /// <summary>Version 4 (Visual Clarity Gate 02.10.): 0 Fenster, 1 Randloses Vollbild, 2 Exklusives Vollbild.</summary>
        public int displayMode = 1;
        /// <summary>Version 4: Ausgabeaufloesung; 0 × 0 = native Desktopaufloesung (Standard). Nie automatisch kleiner.</summary>
        public int resolutionWidth;
        public int resolutionHeight;
        // VSync ab Werk aus, dafuer Grenze 144: gleichmaessig, ohne die Eingabeverzoegerung von VSync und ohne dass
        // Fenster im Hintergrund (mehrere Clients, Alt+Tab) vom Bildabgleich ausgebremst werden.
        public bool vSync = false;
        public int frameLimit = 4;      // 30, 60, 90, 120, 144, unbegrenzt
        public int renderScale = 4;     // 50, 67, 77, 85, 100 %

        // Bild
        public int antiAliasing = 2;    // Aus, FXAA, SMAA, SMAA + MSAA 4x, SMAA hoch + MSAA nach Aufloesung
        public int shadows = 2;         // Aus, Niedrig, Mittel, Hoch, Ultra, Episch
        public int viewDistance = 2;    // Nah, Mittel, Weit, Sehr weit, Episch
        public int vegetation = 1;      // Niedrig, Mittel, Hoch, Ultra, Episch
        public int terrainDetail = 1;   // Niedrig, Mittel, Hoch, Episch
        public int textures = 1;        // Halb, Voll
        public int lights = 1;          // Niedrig, Mittel, Hoch, Episch
        public int postProcessing = 1;  // Einfach, Voll
        public int particles = 1;       // Niedrig, Mittel, Hoch, Episch
        public int globalIllumination;  // Klassisch, Dynamisch, Dynamisch realistisch (Raytracing: Licht prallt von Boden und Waenden ab)

        // Viele Spieler
        public int figureDetail = 1;    // Niedrig, Mittel, Hoch, Episch

        // Kampf-Darstellung (Combat-Masterplan §19/§89/§112) – gehoert zu keiner Grafikstufe, bleibt beim Stufenwechsel
        public int blood;               // Voll, Weniger, Aus (Blut, Wunden, abgetrennte Teile)
        public int damageNumbers;       // Voll, Weniger, Aus
        public int enemyBars;           // Voll, Minimal, Aus (Lebensbalken im Zielrahmen)

        public GraphicsOptions Clone() => (GraphicsOptions)MemberwiseClone();

        /// <summary>Anzeige-Werte (Modus, Aufloesung, VSync, Bildrate, Skalierung) gehoeren nicht zur Grafikstufe.</summary>
        public void CopyDisplay(GraphicsOptions o)
        {
            fullscreen = o.fullscreen; displayMode = o.displayMode; resolutionWidth = o.resolutionWidth; resolutionHeight = o.resolutionHeight;
            vSync = o.vSync; frameLimit = o.frameLimit; renderScale = o.renderScale;
        }

        /// <summary>Gleiche Einzelwerte (die Stufe selbst zaehlt nicht).</summary>
        public bool SameValues(GraphicsOptions o) =>
            o != null && displayMode == o.displayMode && resolutionWidth == o.resolutionWidth && resolutionHeight == o.resolutionHeight && vSync == o.vSync && frameLimit == o.frameLimit && renderScale == o.renderScale &&
            antiAliasing == o.antiAliasing && shadows == o.shadows && viewDistance == o.viewDistance && vegetation == o.vegetation &&
            terrainDetail == o.terrainDetail && textures == o.textures && lights == o.lights && postProcessing == o.postProcessing &&
            particles == o.particles && figureDetail == o.figureDetail && globalIllumination == o.globalIllumination;

        /// <summary>Werte in den gueltigen Bereich bringen (z. B. nach dem Laden einer alten Datei).</summary>
        public void Clamp()
        {
            frameLimit = GraphicsCatalog.Clamp(frameLimit, GraphicsCatalog.FrameLimits.Length);
            renderScale = GraphicsCatalog.Clamp(renderScale, GraphicsCatalog.RenderScales.Length);
            antiAliasing = GraphicsCatalog.Clamp(antiAliasing, GraphicsCatalog.AntiAliasingCount);
            shadows = GraphicsCatalog.Clamp(shadows, GraphicsCatalog.ShadowDistance.Length);
            viewDistance = GraphicsCatalog.Clamp(viewDistance, GraphicsCatalog.FarClip.Length);
            vegetation = GraphicsCatalog.Clamp(vegetation, GraphicsCatalog.DetailDistance.Length);
            terrainDetail = GraphicsCatalog.Clamp(terrainDetail, GraphicsCatalog.PixelError.Length);
            textures = GraphicsCatalog.Clamp(textures, 2);
            lights = GraphicsCatalog.Clamp(lights, GraphicsCatalog.AdditionalLights.Length);
            postProcessing = GraphicsCatalog.Clamp(postProcessing, 2);
            particles = GraphicsCatalog.Clamp(particles, GraphicsCatalog.ParticleShare.Length);
            figureDetail = GraphicsCatalog.Clamp(figureDetail, GraphicsCatalog.FullDetailFigures.Length);
            globalIllumination = GraphicsCatalog.Clamp(globalIllumination, 3);
            blood = GraphicsCatalog.Clamp(blood, 3);
            damageNumbers = GraphicsCatalog.Clamp(damageNumbers, 3);
            enemyBars = GraphicsCatalog.Clamp(enemyBars, 3);
            displayMode = GraphicsCatalog.Clamp(displayMode, 3);
            if (resolutionWidth < 0 || resolutionHeight < 0 || resolutionWidth == 0 != (resolutionHeight == 0)) resolutionWidth = resolutionHeight = 0;
            fullscreen = displayMode != 0;
            if (!Enum.IsDefined(typeof(GraphicsTier), tier)) tier = GraphicsTier.Mittel;
        }
    }

    /// <summary>
    /// Was jeder Index bedeutet (eine Tabelle fuer Anwendung, Fenster und Doku) und die drei Voreinstellungen.
    /// Zielhardware und Begruendung: Docs/Grafikstufen.md.
    /// </summary>
    public static class GraphicsCatalog
    {
        public static readonly int[] FrameLimits = { 30, 60, 90, 120, 144, 0 };
        public static readonly float[] RenderScales = { 0.5f, 0.67f, 0.77f, 0.85f, 1f };

        /// <summary>Anzahl waehlbarer Stufen (ohne Benutzerdefiniert).</summary>
        public const int TierCount = 4;

        /// <summary>Kantenglaettung: 0 Aus, 1 FXAA, 2 SMAA, 3 SMAA + MSAA 4x, 4 SMAA hoch + MSAA nach Aufloesung.</summary>
        public const int AntiAliasingCount = 5;
        public const int AntiAliasingAuto = 4;

        /// <summary>
        /// MSAA-Stufe fuer eine Ausgabegroesse. "Auto" (Episch): bis 4K 4x, bis ~6K 2x, darueber (8K) aus – bei 8K sind die
        /// Pixel so klein, dass SMAA reicht, waehrend 4x MSAA dort ueber 1,5 GB Grafikspeicher und viel Bandbreite kostet.
        /// </summary>
        public static int MsaaFor(GraphicsOptions o, int width, int height)
        {
            if (o.antiAliasing == 3) return 4;
            if (o.antiAliasing != AntiAliasingAuto) return 1;
            long px = (long)width * height;
            if (px <= 3840L * 2160L * 11 / 10) return 4;
            if (px <= 5120L * 2880L * 11 / 10) return 2;
            return 1;
        }

        public static readonly float[] ShadowDistance = { 0f, 60f, 90f, 120f, 150f, 250f };
        /// <summary>Episch: 8192, falls die Render-Pipeline es anbietet (sonst 4096, siehe GraphicsApplier).</summary>
        public static readonly int[] ShadowResolution = { 256, 1024, 2048, 2048, 4096, 8192 };
        public static readonly int[] ShadowCascades = { 1, 2, 2, 3, 4, 4 };
        public static readonly bool[] SoftShadows = { false, false, true, true, true, true };
        public static readonly bool[] LightShadows = { false, false, false, true, true, true };

        public static readonly float[] FarClip = { 900f, 1400f, 2000f, 2500f, 4000f };
        public static readonly float[] LodBias = { 0.8f, 1f, 1.4f, 2f, 3f };
        public static readonly float[] BasemapDistance = { 300f, 450f, 600f, 800f, 1500f };

        public static readonly float[] DetailDistance = { 35f, 55f, 85f, 120f, 200f };
        public static readonly float[] DetailDensity = { 0.45f, 0.7f, 1f, 1f, 1f };

        public static readonly float[] PixelError = { 10f, 7f, 5f, 2f };

        public static readonly int[] AdditionalLights = { 2, 4, 8, 8 };
        public static readonly int[] AdditionalShadowResolution = { 1024, 2048, 4096, 8192 };

        public static readonly float[] ParticleShare = { 0.3f, 0.6f, 1f, 1.5f };

        /// <summary>So viele naechste Figuren (Spieler/NPCs) bekommen volle Qualitaet (Schatten, jede Animation).</summary>
        public static readonly int[] FullDetailFigures = { 12, 25, 50, 100 };
        /// <summary>Weiter entfernte Figuren werden gar nicht gezeichnet (Namen bleiben im eigenen Radius).</summary>
        public static readonly float[] FigureViewDistance = { 90f, 140f, 220f, 400f };

        public static readonly string[] TierNames = { "Niedrig", "Mittel", "Hoch", "Episch", "Benutzerdefiniert" };

        public static int Clamp(int v, int count) => v < 0 ? 0 : v >= count ? count - 1 : v;

        public static GraphicsOptions Preset(GraphicsTier tier)
        {
            var o = new GraphicsOptions { tier = tier };
            switch (tier)
            {
                case GraphicsTier.Niedrig:
                    o.antiAliasing = 1; o.shadows = 1; o.viewDistance = 1; o.vegetation = 0; o.terrainDetail = 0;
                    o.textures = 1; o.lights = 0; o.postProcessing = 0; o.particles = 0; o.figureDetail = 0;
                    break;
                case GraphicsTier.Hoch:
                    o.antiAliasing = 3; o.shadows = 4; o.viewDistance = 3; o.vegetation = 3; o.terrainDetail = 2;
                    o.textures = 1; o.lights = 2; o.postProcessing = 1; o.particles = 2; o.figureDetail = 2;
                    o.globalIllumination = 1;
                    break;
                case GraphicsTier.Episch:
                    o.antiAliasing = AntiAliasingAuto; o.shadows = 5; o.viewDistance = 4; o.vegetation = 4; o.terrainDetail = 3;
                    o.textures = 1; o.lights = 3; o.postProcessing = 1; o.particles = 3; o.figureDetail = 3;
                    o.globalIllumination = 1;
                    break;
                default:
                    o.tier = GraphicsTier.Mittel;
                    o.antiAliasing = 2; o.shadows = 2; o.viewDistance = 2; o.vegetation = 1; o.terrainDetail = 1;
                    o.textures = 1; o.lights = 1; o.postProcessing = 1; o.particles = 1; o.figureDetail = 1;
                    break;
            }
            return o;
        }

        /// <summary>Welche Stufe passt zu den Einzelwerten? Sonst <see cref="GraphicsTier.Benutzerdefiniert"/>.</summary>
        public static GraphicsTier Match(GraphicsOptions o)
        {
            for (int t = 0; t < TierCount; t++)
            {
                GraphicsOptions p = Preset((GraphicsTier)t);
                // Anzeige-Werte (Vollbild, VSync, Bildrate, Aufloesung) gehoeren nicht zur Stufe
                p.CopyDisplay(o);
                if (p.SameValues(o)) return (GraphicsTier)t;
            }
            return GraphicsTier.Benutzerdefiniert;
        }

        // ================================================================== Aufloesungen (Visual Clarity Gate 02.10.)

        /// <summary>Waehlbare Aufloesungen: [0] = nativ, danach die Monitoraufloesungen ab 1280 × 720 absteigend (ohne Doppelte).</summary>
        public static List<Vector2Int> Resolutions()
        {
            var list = new List<Vector2Int> { Vector2Int.zero };
            int nw = Display.main.systemWidth, nh = Display.main.systemHeight;
            var seen = new HashSet<Vector2Int>();
            Resolution[] all = Screen.resolutions;
            for (int i = all.Length - 1; i >= 0; i--)
            {
                var r = new Vector2Int(all[i].width, all[i].height);
                if (r.x < 1280 || r.y < 720 || r.x > nw || r.y > nh || (r.x == nw && r.y == nh) || !seen.Add(r)) continue;
                list.Add(r);
            }
            return list;
        }

        private static string[] ResolutionLabels()
        {
            List<Vector2Int> list = Resolutions();
            var s = new string[list.Count];
            s[0] = $"Nativ ({Display.main.systemWidth} × {Display.main.systemHeight})";
            for (int i = 1; i < list.Count; i++) s[i] = $"{list[i].x} × {list[i].y}";
            return s;
        }

        private static int ResolutionIndex(GraphicsOptions o)
        {
            if (o.resolutionWidth <= 0) return 0;
            int i = Resolutions().IndexOf(new Vector2Int(o.resolutionWidth, o.resolutionHeight));
            return i < 0 ? 0 : i;
        }

        private static void SetResolutionIndex(GraphicsOptions o, int v)
        {
            List<Vector2Int> list = Resolutions();
            Vector2Int r = v > 0 && v < list.Count ? list[v] : Vector2Int.zero;
            o.resolutionWidth = r.x;
            o.resolutionHeight = r.y;
        }

        // ================================================================== Fenster-Zeilen

        /// <summary>Eine Zeile im Einstellungsfenster: Name, Auswahl, Lesen/Schreiben.</summary>
        public sealed class Row
        {
            public string Label;
            public string Hint;
            public string[] Choices;
            /// <summary>Auswahl, die erst zur Laufzeit feststeht (z. B. Monitoraufloesungen); sonst <see cref="Choices"/>.</summary>
            public Func<string[]> ChoicesFunc;
            public string[] CurrentChoices => ChoicesFunc != null ? ChoicesFunc() : Choices;
            public Func<GraphicsOptions, int> Get;
            public Action<GraphicsOptions, int> Set;
        }

        public static readonly Row[] Rows =
        {
            new Row { Label = "Anzeigemodus", Hint = "Randloses Vollbild empfohlen", Choices = new[] { "Fenster", "Randloses Vollbild", "Exklusives Vollbild" }, Get = o => o.displayMode, Set = (o, v) => { o.displayMode = v; o.fullscreen = v != 0; } },
            new Row { Label = "Aufloesung", Hint = "Nativ = Bildschirmaufloesung (scharf). Kleinere Werte werden hochskaliert.", ChoicesFunc = ResolutionLabels, Get = ResolutionIndex, Set = SetResolutionIndex },
            new Row { Label = "VSync", Hint = "Verhindert Bildrisse, begrenzt auf die Bildschirmfrequenz", Choices = new[] { "Aus", "An" }, Get = o => o.vSync ? 1 : 0, Set = (o, v) => o.vSync = v == 1 },
            new Row { Label = "Bildrate begrenzen", Choices = new[] { "30", "60", "90", "120", "144", "Unbegrenzt" }, Get = o => o.frameLimit, Set = (o, v) => o.frameLimit = v },
            new Row { Label = "Aufloesungsskalierung", Hint = "Unter 100 % rechnet weniger Pixel und skaliert hoch (FSR)", Choices = new[] { "50 %", "67 %", "77 %", "85 %", "100 %" }, Get = o => o.renderScale, Set = (o, v) => o.renderScale = v },
            new Row { Label = "Kantenglaettung", Choices = new[] { "Aus", "FXAA", "SMAA", "SMAA + MSAA 4x", "SMAA hoch + MSAA auto" }, Get = o => o.antiAliasing, Set = (o, v) => o.antiAliasing = v },
            new Row { Label = "Schatten", Choices = new[] { "Aus", "Niedrig", "Mittel", "Hoch", "Ultra", "Episch" }, Get = o => o.shadows, Set = (o, v) => o.shadows = v },
            new Row { Label = "Sichtweite", Choices = new[] { "Nah", "Mittel", "Weit", "Sehr weit", "Episch (4 km)" }, Get = o => o.viewDistance, Set = (o, v) => o.viewDistance = v },
            new Row { Label = "Gras und Pflanzen", Choices = new[] { "Niedrig", "Mittel", "Hoch", "Ultra", "Episch" }, Get = o => o.vegetation, Set = (o, v) => o.vegetation = v },
            new Row { Label = "Gelaendedetail", Choices = new[] { "Niedrig", "Mittel", "Hoch", "Episch" }, Get = o => o.terrainDetail, Set = (o, v) => o.terrainDetail = v },
            new Row { Label = "Texturen", Choices = new[] { "Halb", "Voll" }, Get = o => o.textures, Set = (o, v) => o.textures = v },
            new Row { Label = "Lichter", Hint = "Fackeln, Lagerfeuer; ab Hoch mit Schatten", Choices = new[] { "Niedrig", "Mittel", "Hoch", "Episch" }, Get = o => o.lights, Set = (o, v) => o.lights = v },
            new Row { Label = "Globale Beleuchtung", Hint = "Dynamisch: Licht prallt in Echtzeit von Boden, Waenden und Feuern ab, unter Daechern und in Hoehlen wird es dunkler (Raytracing, DirectX 12). Realistisch: echtes Himmelslicht ohne Angleich – deutlich dunklere Naechte. Ohne Raytracing-Karte bleibt es klassisch.", Choices = new[] { "Klassisch", "Dynamisch", "Dynamisch realistisch" }, Get = o => o.globalIllumination, Set = (o, v) => o.globalIllumination = v },
            new Row { Label = "Nachbearbeitung", Choices = new[] { "Einfach", "Voll" }, Get = o => o.postProcessing, Set = (o, v) => o.postProcessing = v },
            new Row { Label = "Partikel", Choices = new[] { "Niedrig", "Mittel", "Hoch", "Episch" }, Get = o => o.particles, Set = (o, v) => o.particles = v },
            new Row { Label = "Blut und Zerteilung", Hint = "Wunden, Blut, abgetrennte Koerperteile", Choices = new[] { "Voll", "Weniger", "Aus" }, Get = o => o.blood, Set = (o, v) => o.blood = v },
            new Row { Label = "Schadenszahlen", Hint = "Weniger: nur kritische Treffer und erlittener Schaden", Choices = new[] { "Voll", "Weniger", "Aus" }, Get = o => o.damageNumbers, Set = (o, v) => o.damageNumbers = v },
            new Row { Label = "Gegner-Lebensbalken", Hint = "Minimal: Balken ohne Prozent", Choices = new[] { "Voll", "Minimal", "Aus" }, Get = o => o.enemyBars, Set = (o, v) => o.enemyBars = v },
            new Row { Label = "Figurendetail", Hint = "Wichtig bei vielen Spielern: nur die naechsten Figuren in voller Qualitaet", Choices = new[] { "Niedrig (12)", "Mittel (25)", "Hoch (50)", "Episch (100)" }, Get = o => o.figureDetail, Set = (o, v) => o.figureDetail = v },
        };
    }
}
