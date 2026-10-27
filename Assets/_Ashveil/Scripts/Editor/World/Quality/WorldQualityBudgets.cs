namespace Ashveil.EditorTools.Quality
{
    /// <summary>
    /// Grenzwerte des World Quality Validators (Werkzeug-Direktive §17: „Grenzen nicht erfinden. Bestehende ASHVEIL-Benchmarks
    /// verwenden und empirisch kalibrieren.“). Eine Grenze von 0 heißt: noch nicht gemessen → nur INFO, keine Warnung.
    /// Jede Grenze nennt ihre Messung. Nach der ersten Messung auf Mindesthardware (TECH-080) werden sie nachgezogen.
    /// </summary>
    public static class WorldQualityBudgets
    {
        // ------------------------------------------------------------------ Szene bzw. Weltzelle (CPU)

        /// <summary>
        /// Mesh-Renderer je geladener Szene bzw. Zelle. Messung 05.10. (Felssatz-Bericht §7, fertiges Spiel, RTX 5080,
        /// Core Ultra 9 285K, 1080p): Kareth v4 mit 9 089 Renderern ist CPU-gebunden (Hauptthread 7,07 ms, langsamster
        /// Blickpunkt 11 ms). 04.10.: 10 437 Renderer → 8,2 ms. Ab dieser Größe ist die Szene nachweislich CPU-gebunden.
        /// </summary>
        public const int RenderersCritical = 9000;
        /// <summary>Zwei Drittel der gemessenen CPU-Grenze: Rückstellung für Figuren, Gegner und Effekte im Spiel.</summary>
        public const int RenderersWarning = 6000;
        public const string RenderersSource = "Kareth v4: 9 089 Renderer → 7,07 ms Hauptthread, CPU-gebunden (Messung 05.10., RTX 5080 / Core Ultra 9 285K)";

        // ------------------------------------------------------------------ noch ohne Grenze (nur INFO)

        /// <summary>Dreiecke je Bild: 7,82 Mio. (mit Schatten) bei 2,61 ms GPU auf der RTX 5080 – Mindesthardware nicht gemessen.</summary>
        public const long TrianglesWarning = 0;
        public const string TrianglesSource = "Kareth v4: 7,82 Mio. Dreiecke je Bild (mit Schatten), 2,61 ms GPU auf RTX 5080; GTX 1060 (TECH-080) nicht gemessen";

        /// <summary>Echtzeitlichter im Blick: Salzgruben 75 (CPU 2,4 ms) und Startlager 33 ohne Befund (28.09.).</summary>
        public const int RealtimeLightsWarning = 0;
        public const string LightsSource = "geprüft bis 75 Lichter im Blick (Salzgruben, Rendering-Grundlinie 28.09.)";

        /// <summary>Schattenwerfer im Blick: Salzgruben 3 193 mit einem 18-ms-Ausreißer (REND-034, Ursache offen).</summary>
        public const int ShadowCastersWarning = 0;
        public const string ShadowSource = "Salzgruben: 3 193 Schattenwerfer im Blick, Ausreißer 18 ms (REND-034, Ursache offen)";

        // ------------------------------------------------------------------ Assets (Projektregeln, keine Leistungsmessung)

        /// <summary>Texturgröße im Spiel: Weltmaterialien 2048² Quelle, 1024² im Spiel (PBR-Kern 04.10.); gemaltes PBR braucht wenig (ART-107).</summary>
        public const int TextureSizeWarning = 2048;      // größer als 2048 → Warnung
        public const int TextureSizeCritical = 4096;     // größer als 4096 → kritisch
        /// <summary>Materialien je Renderer (Rendering-Bibel §167 „Materialgrenze je Prop“; Nachhall: drei Materialien).</summary>
        public const int MaterialsPerRendererWarning = 4;
        /// <summary>Dreiecke eines Netzes ohne LOD-Gruppe (Rendering-Bibel §31–35: LOD-Kette Pflicht für große Netze).</summary>
        public const int MeshTrianglesWithoutLodWarning = 20000;
        public const int MeshTrianglesWithoutLodCritical = 100000;
        /// <summary>Kollisionsnetz: teuer ab dieser Dreieckszahl (Felssatz: 351 018 Kollisionsdreiecke auf 398 Kacheln).</summary>
        public const int MeshColliderTrianglesWarning = 50000;
        /// <summary>„Sehr kleine Objekte mit teuren Schatten“ (§16): größte Ausdehnung unter dieser Länge.</summary>
        public const float SmallShadowCasterSize = 0.5f;

        // ------------------------------------------------------------------ Gelände und Wege (Kareth-Kette, layout.py)

        /// <summary>Planungsgrenzen der Wegeklassen (claude/kareth_v4/integration/layout.py MAX_GRADE): haupt 9 %, neben 12 %, pfad 18 %.</summary>
        public static float GradeLimit(string wegTyp)
        {
            switch (wegTyp)
            {
                case "haupt": return 9f;
                case "neben": return 12f;
                case "pfad": return 18f;
                default: return 18f;
            }
        }
        /// <summary>Spielbar bleibt ein Weg bis etwa 35° (≈ 70 %); darüber ist er für normale Wege unbrauchbar.</summary>
        public const float GradeCriticalPercent = 70f;
        /// <summary>Wege dürfen an benannten Stellen steiler sein (wege2.STEIL, neue W0-Pfade ≤ 30 %). Warnung erst über Grenze × Faktor.</summary>
        public const float GradeWarningFactor = 1.6f;

        /// <summary>Toleranz für schwebende Objekte (Regel „Sinn und Gelände“ Nr. 5: nichts schwebt).</summary>
        public const float FloatingTolerance = 0.3f;
        /// <summary>Weltzellen-Raster für Brennpunkte, solange eine Szene keine Zellen aus dem Weltverzeichnis hat.</summary>
        public const float HotspotTileSize = 256f;
    }
}
