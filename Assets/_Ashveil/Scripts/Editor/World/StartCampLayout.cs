using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Bauplan des Startlagers „Wachtfeuer“ (unter dem Kopf des versteinerten Beschuetzers) – eine Quelle fuer
    /// Gelaende (Plaetze, Wege, Gras) und Aufbau (Palisade, Bereiche). Koordinaten: Welt, Wachtfeuer = (0, 0, 0),
    /// +z = Norden. Ab 27.09.2026 (M8): Palisade r = 32 um (0, −4) statt r = 22; im Sueden schliesst der Fels der
    /// Brust das Lager ab – die Halle des Hauptmanns liegt damit im Lager. Flaeche ≈ 2,5 × vorher.
    /// Geplant, aber noch nicht gebaut: ein Hauptlager fuers mittlere Spiel, etwa 2–3 × so gross.
    /// </summary>
    public static class StartCampLayout
    {
        public static readonly Vector2 Center = new Vector2(0f, -4f);
        public const float Radius = 32f;
        /// <summary>Der Palisadenbogen reicht von −ArcHalf bis +ArcHalf (Grad, 0 = Norden); suedlich davon gerade Waende zum Fels.</summary>
        public const float ArcHalf = 120f;
        /// <summary>Suedende der geraden Waende (x wird gespiegelt), liegt schon am Fels der Brust.</summary>
        public static readonly Vector2 SouthEndEast = new Vector2(29.5f, -45f);

        // Tore im Bogen (Grad) und im Westwall (z)
        public const float GateNorth = 0f, GateEast = 103f, GateWest = 290f;
        public const float GateSouthWestZ = -28f;

        // Bereiche
        public static readonly Vector2 Market = new Vector2(9f, 9f);            // Nordost: Staende (Kraeuter, Munition, Proviant)
        public static readonly Vector2 Forge = new Vector2(-15f, -4f);          // West: Schmiede, Holzplatz, Werkbank
        public static readonly Vector2 Tents = new Vector2(-14f, 16f);          // Nordwest: Zelte um eine kleine Feuerstelle
        public static readonly Vector2 Kitchen = new Vector2(-13f, -25f);       // Suedwest: Kochstelle, Langtische, Vorraete
        public static readonly Vector2 Training = new Vector2(16f, -17f);       // Suedost: Trainingsplatz, Schiessscheiben
        public static readonly Vector2 Pen = new Vector2(19f, -34f);            // Suedost am Fels: Pferch und Wagen
        public static readonly Vector2 Forecourt = new Vector2(0f, -26f);       // Sueden: Vorplatz der Halle
        public static readonly Vector2 Lookout = new Vector2(30f, 12f);         // Aussichtsfels (Auftrag 3) – Teil der Ostwand

        /// <summary>Plaetze, die als Weg in den Boden gemalt werden (Mitte, Radius).</summary>
        public static readonly (Vector2 c, float r)[] Plazas =
        {
            (Vector2.zero, 9.5f), (Market, 6.5f), (Forecourt, 6f), (Kitchen, 5.5f), (Training, 6.5f), (Forge + new Vector2(1.5f, 0f), 4.5f)
        };

        /// <summary>Wege im Lager (vom Feuer zu den Toren und zur Halle) – werden zu den Kareth-Wegen hinzugefuegt.</summary>
        public static Vector2[][] Paths => new[]
        {
            new[] { Vector2.zero, OnArc(GateWest, 0f), OnArc(GateWest, 6f) + new Vector2(-6f, 12f) }, // Westtor -> Nordwestweg
            new[] { Vector2.zero, new Vector2(0f, -40f) }                                               // Hallenachse
            // Suedwesttor: der bestehende Salzweg (0,0) -> (-45,-45) laeuft genau hindurch
        };

        public static Vector2 OnArc(float angleDeg, float extra = 0f)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            return Center + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * (Radius + extra);
        }

        /// <summary>Punkt auf dem Westwall bei z (x negativ).</summary>
        public static Vector2 WestWallAt(float z)
        {
            Vector2 top = OnArc(360f - ArcHalf), bottom = new Vector2(-SouthEndEast.x, SouthEndEast.y);
            float t = Mathf.InverseLerp(top.y, bottom.y, z);
            return Vector2.Lerp(top, bottom, t);
        }

        /// <summary>Liegt (x, z) innerhalb der Palisade?</summary>
        public static bool Inside(float x, float z, float margin = 0f)
        {
            var p = new Vector2(x, z);
            Vector2 top = OnArc(ArcHalf);
            if (z >= top.y) return Vector2.Distance(p, Center) < Radius - margin;
            if (z < SouthEndEast.y) return false;
            float t = Mathf.InverseLerp(top.y, SouthEndEast.y, z);
            float half = Mathf.Lerp(top.x, SouthEndEast.x, t);
            return Mathf.Abs(x) < half - margin;
        }
    }
}
