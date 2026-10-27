using System;
using System.Collections.Generic;
using UnityEngine;
using P = Ashveil.EditorTools.BorderMountainsLayout.Polyline;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Westweiden (Arbeitstitel) – neue Weltzelle westlich von Kareth (Max 29.09.: „Neue Weltzelle westlich“) fuer die
    /// Tank-Klassenquests „Wo die Herde floh“ / „Der letzte Weidegang“. Eine Kachel 1200 x 1200 m im Kareth-Raster
    /// (x −1800…−600, z −350…850). Kareth bleibt Kareth: nur eine Passkerbe im Randgebirge (Weidepass) und der Weideweg.
    ///
    ///   Osten   : Rueckseite des Kareth-Randgebirges, faellt nach Westen ins Tal ab; Weidepass bei z = 250.
    ///   Mitte   : Hallgrund – sicheres, bebautes Weideland um den Pferdehof (flach).
    ///   Westen  : Kiefernwald auf welligen Huegeln, nach Westen hoeher. Bach von Norden nach Sueden (nur an Furten querbar).
    ///   Norden  : Bergflanke mit Hohlweg (Schlucht) zur Fohlensenke – natuerliche Sackgasse, Felskessel.
    ///   Sueden  : Kueste.
    ///
    /// Eine Hoehenfunktion fuer alles (Gelaende, Platzierung, Pruefungen) – wie Kareth und Grenzgebirge.
    /// </summary>
    public static class WestPasturesLayout
    {
        public const float MinX = -1800f, MaxX = -600f, MinZ = -350f, MaxZ = 850f;
        public const float TileSize = 1200f;
        public const float BaseY = -40f;
        public const float HeightRange = 360f;
        public const float SeaLevel = KarethTerrainBuilder.SeaLevel;

        // ================================================================== Pass (Kareth-Seite und Zelle)

        public const float PassZ = 250f;
        /// <summary>Beginn der Kerbe auf Kareth-Seite (vor dem Randgebirge ist das Gelaende noch natuerlich).</summary>
        public const float PassX0 = -512f;
        public const float PassHalfWidth = 7f;
        public const float PassWallSlope = 1.15f;

        private static float _hp = float.NaN;
        /// <summary>Boden des Weidepasses (= Kareth-Hoehe am Fuss des Randgebirges).</summary>
        public static float PassFloor
        {
            get
            {
                if (float.IsNaN(_hp)) _hp = KarethTerrainBuilder.Height(PassX0, PassZ);
                return _hp;
            }
        }

        /// <summary>Weideweg in Kareth (vom Holzfaellerlager zum Pass) – auch als Kareth-Weg gemalt, baumfrei.</summary>
        public static readonly Vector2[] KarethRoad =
        {
            new Vector2(-250f, 240f), new Vector2(-330f, 236f), new Vector2(-420f, 247f), new Vector2(-520f, 250f), new Vector2(-600f, 250f)
        };

        /// <summary>Passkerbe (gilt fuer Kareth und die Zelle gleichermassen, damit die Naht passt).</summary>
        public static double Notch(double x, double z, double h)
        {
            if (x > PassX0 + 2 || x < -700) return h;
            double lat = Math.Max(0, Math.Abs(z - PassZ) - PassHalfWidth);
            double floor = PassFloorAt(x);
            double lim = floor + lat * PassWallSlope;
            return Math.Min(h, lim);
        }

        /// <summary>Wegboden im Pass: in Kareth eben, in der Zelle sanft hinab zum Hof.</summary>
        public static double PassFloorAt(double x)
        {
            if (x >= -600) return PassFloor;
            return PassFloor + (FarmFloor - PassFloor) * S(-600, -760, x);
        }

        /// <summary>Kareth-Hoehe an der Naht (x = −600) inklusive Passkerbe – so, wie sie im Kareth-Terrain steht.</summary>
        public static float KarethEdge(float z) => Mathf.Clamp((float)Notch(MaxX, z, KarethTerrainBuilder.Height(MaxX, z)), -40f, 120f);

        // ================================================================== Orte

        public static readonly Vector2 FarmCenter = new Vector2(-800f, 250f);
        /// <summary>Hofboden: 4 m unter dem Pass.</summary>
        public static float FarmFloor => PassFloor - 4f;

        /// <summary>Fohlensenke: Felskessel am Ende des Hohlwegs (Sackgasse).</summary>
        public static readonly Vector2 BasinCenter = new Vector2(-1470f, 725f);
        public const float BasinRadius = 22f;
        public const float BasinWallRadius = 52f;

        /// <summary>Suchgebiete (Hinweise 1–6) auf dem Weg der Herde – Reihenfolge = Suche.</summary>
        public static readonly (string key, Vector2 pos, float radius)[] Clues =
        {
            ("clue1_fence", new Vector2(-852f, 262f), 16f),     // Westzaun von aussen eingedrueckt
            ("clue2_field", new Vector2(-925f, 128f), 20f),     // Acker zertrampelt, Herde getrennt, Halfterstueck
            ("clue3_brook", new Vector2(-988f, -8f), 18f),      // Tränke am Bach, Spuren kehren um (Furt Sued)
            ("clue4_carcass", new Vector2(-1300f, -20f), 18f),  // alter Wallach, gezielt ins Dickicht gedrueckt
            ("clue5_marks", new Vector2(-1560f, 160f), 18f),    // Kratz- und Bissspuren an beiden Flanken
            ("clue6_hollow", new Vector2(-1450f, 440f), 20f),   // Senke: Herde zusammengedraengt, einzelne grosse Faehrte
        };

        /// <summary>Furten (flache Stellen im Bach) – nur hier ist er querbar.</summary>
        public static readonly Vector2[] Fords = { new Vector2(-988f, -8f), new Vector2(-1022f, 432f) };

        // ================================================================== Linien

        public static readonly P PassRoad, Brook, Ravine, SearchTrail, HerdReturn;
        /// <summary>Bachbett-Hoehe je Knoten (steigt nie bachabwaerts).</summary>
        private static readonly double[] BrookBed;

        static WestPasturesLayout()
        {
            // Weg vom Pass zum Hoftor
            PassRoad = Line(new[] { (-598.0, 250.0), (-640.0, 251.0), (-690.0, 249.0), (-735.0, 250.0), (-772.0, 250.0) }, x => PassFloorAt(x));

            // Bach von der Bergflanke im Norden zur Kueste – rechts am Hof vorbei (westlich)
            var brook = new[]
            {
                (-1125.0, 835.0), (-1098.0, 740.0), (-1068.0, 650.0), (-1045.0, 540.0), (-1022.0, 432.0), (-1000.0, 330.0),
                (-982.0, 230.0), (-972.0, 130.0), (-985.0, 55.0), (-988.0, -8.0), (-970.0, -90.0), (-958.0, -170.0), (-950.0, -260.0)
            };
            var bx = new double[brook.Length];
            var bz = new double[brook.Length];
            BrookBed = new double[brook.Length];
            for (int i = 0; i < brook.Length; i++)
            {
                bx[i] = brook[i].Item1;
                bz[i] = brook[i].Item2;
                double h = RawNoCarve(bx[i], bz[i]) - 1.6;
                if (i > 0) h = Math.Min(h, BrookBed[i - 1] - 0.25);
                BrookBed[i] = Math.Max(h, SeaLevel - 1.5);
            }
            Brook = P.FromDoubles(bx, bz, BrookBed);

            // Hohlweg: vom Waldrand hinauf in die Bergflanke, gewunden bis in die Fohlensenke
            var rv = new[] { (-1300.0, 560.0), (-1262.0, 612.0), (-1268.0, 668.0), (-1310.0, 712.0), (-1372.0, 742.0), (-1432.0, 736.0), (BasinCenter.x + 18.0, BasinCenter.y + 2.0) };
            double r0 = RawNoCarve(rv[0].Item1, rv[0].Item2);
            _basinFloor = r0 + 11.0;
            Ravine = Line(rv, null, i => r0 + 11.0 * i / (rv.Length - 1));

            // Suchweg (nur Messung/Gizmos – kein gebahnter Weg): Hof → Hinweise → Hohlweg → Senke
            var trail = new List<(double, double)> { (FarmCenter.x - 30.0, FarmCenter.y + 8.0) };
            foreach (var c in Clues) trail.Add((c.pos.x, c.pos.y));
            foreach (var p in rv) trail.Add(p);
            trail.Add((BasinCenter.x, BasinCenter.y));
            SearchTrail = Line(trail.ToArray(), null);

            // Heimweg der Herde (kuratiert, 700–1000 m): Senke → Hohlweg → Waldrand → Furt Nord → Weide
            var home = new List<(double, double)> { (BasinCenter.x, BasinCenter.y) };
            for (int i = rv.Length - 1; i >= 0; i--) home.Add(rv[i]);
            home.AddRange(new[] { (-1235.0, 520.0), (-1140.0, 470.0), (-1060.0, 440.0), ((double)Fords[1].x, (double)Fords[1].y), (-975.0, 410.0), (-915.0, 340.0),
                (-872.0, 286.0), (-856.0, 258.0), (-836.0, 248.0) });
            HerdReturn = Line(home.ToArray(), null);
        }

        private static double _basinFloor;
        public static float BasinFloor => (float)_basinFloor;

        private static P Line((double, double)[] pts, Func<double, double> heightOfX, Func<int, double> heightOfIndex = null)
        {
            var x = new double[pts.Length];
            var z = new double[pts.Length];
            var h = new double[pts.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                x[i] = pts[i].Item1;
                z[i] = pts[i].Item2;
                h[i] = heightOfIndex != null ? heightOfIndex(i) : heightOfX != null ? heightOfX(x[i]) : 0.0;
            }
            return P.FromDoubles(x, z, h);
        }

        // ================================================================== Hoehe

        private static double S(double a, double b, double v)
        {
            double t = (v - a) / (b - a);
            if (t < 0) t = 0;
            else if (t > 1) t = 1;
            return t * t * (3 - 2 * t);
        }

        private static double F(double x, double z, double scale, int oct, long seed) => BorderMountainsLayout.Fbm(x, z, scale, oct, seed);

        /// <summary>Gelaendehoehe (m) – dieselbe Funktion fuer Kachel, Platzierung und Pruefung.</summary>
        public static float Height(float x, float z) => (float)Final(x, z, KarethEdge(z));

        public static Vector3 OnGround(float x, float z, float lift = 0f) => new Vector3(x, Height(x, z) + lift, z);

        /// <summary>Grundgelaende ohne Wege, Bach, Schlucht und Senke.</summary>
        private static double RawNoCarve(double x, double z) => Raw(x, z, KarethEdge((float)z));

        private static double Raw(double x, double z, double kh)
        {
            double n1 = F(x, z, 220, 3, 301), n2 = F(x, z, 70, 2, 302), n3 = F(x, z, 22, 2, 303);
            double floor = FarmFloor + 3.0 * n1;
            // Waldhuegel nach Westen, dahinter hoeher
            double west = S(-860, -1150, x);
            double h = floor + west * (9 + 16 * n1 + 5 * n2) + S(-1150, -1750, x) * (28 + 24 * n1);
            // Bergflanke im Norden
            h += S(500, 820, z) * (75 + 55 * F(x, z, 300, 3, 304)) * (0.55 + 0.45 * S(-750, -1100, x));
            h += 1.2 * n3;
            // Rueckseite des Randgebirges: faellt von der Kareth-Kante nach Westen ab
            double d = MaxX - x;
            double ridge = kh + 5 * n2 * S(0, 25, d);
            h = ridge + (h - ridge) * S(12, 150, d);
            // Hallgrund: bebautes Weideland um den Hof, fast eben
            double df = Math.Sqrt((x - FarmCenter.x) * (x - FarmCenter.x) + (z - FarmCenter.y) * (z - FarmCenter.y));
            h += ((FarmFloor + 0.5 * n2) - h) * (1 - S(95, 175, df));
            // Kueste im Sueden
            double coastZ = -90 + (x - MaxX) * 0.2;
            double sea = S(coastZ + 15, coastZ - 40, z);
            h += ((SeaLevel - 6) - h) * sea;
            return h;
        }

        /// <summary>Bodenhoehe inklusive Weg, Bach, Hohlweg, Senke und Passkerbe; an der Naht exakt die Kareth-Kante.</summary>
        internal static double Final(double x, double z, double kh)
        {
            double h = Raw(x, z, kh);
            h = Basin(x, z, h);
            h = CarvePath(h, x, z, Ravine, 3.2, 8.0, 2.6);
            h = CarvePath(h, x, z, PassRoad, 3.2, 10.0, 1.1);
            h = Notch(x, z, h);
            h = BrookCarve(x, z, h);
            double ws = S(MaxX, MaxX - 16, x);
            return kh + (h - kh) * ws;
        }

        private static double CarvePath(double h, double x, double z, P p, double w, double band, double k)
        {
            double d = p.Dist(x, z, out double arc);
            if (d > w + band + 60) return h;
            double ph = p.HeightAt(arc);
            double kk = k * (1 + 0.25 * F(x, z, 40, 2, 97));
            double lim = kk * Math.Max(0, d - w) + 0.4 * S(w, w + band, d) * band;
            double diff = h - ph;
            if (diff > lim) diff = lim;
            else if (diff < -lim) diff = -lim;
            return ph + diff;
        }

        /// <summary>Felskessel: ebener Boden, ringsum steile Waende – nur der Hohlweg fuehrt hinein.</summary>
        private static double Basin(double x, double z, double h)
        {
            double dx = x - BasinCenter.x, dz = z - BasinCenter.y;
            double r = Math.Sqrt(dx * dx + dz * dz);
            if (r > BasinWallRadius + 20) return h;
            double wob = 3.5 * F(x, z, 18, 2, 311);
            double rr = r + wob;
            if (rr < BasinRadius) return _basinFloor + 0.35 * F(x, z, 6, 2, 312);
            if (Ravine.Dist(x, z, out _) < 7.5) return h; // Eingang
            double wall = _basinFloor + (rr - BasinRadius) * 2.6 + 3 * F(x, z, 12, 2, 313);
            if (rr < BasinWallRadius) return Math.Max(h, wall);
            return Math.Max(h, wall * (1 - S(BasinWallRadius, BasinWallRadius + 20, rr)) + h * S(BasinWallRadius, BasinWallRadius + 20, rr));
        }

        /// <summary>Bachbett: steile, 2 m hohe Ufer (nicht begehbar) – nur an den Furten flach.</summary>
        private static double BrookCarve(double x, double z, double h)
        {
            double dr = Brook.Dist(x, z, out double arc);
            if (dr > 40) return h;
            double bed = Brook.HeightAt(arc);
            double ford = 0;
            foreach (Vector2 f in Fords)
            {
                double fd = Math.Sqrt((x - f.x) * (x - f.x) + (z - f.y) * (z - f.y));
                ford = Math.Max(ford, 1 - S(7, 13, fd));
            }
            double steep = bed + Math.Min(Math.Max(0, dr - 1.4) * 1.7, 2.4) + Math.Max(0, dr - 2.8) * 0.45;
            double flat = bed + 0.55 + Math.Max(0, dr - 1.4) * 0.22;
            double bank = steep + (flat - steep) * ford;
            return Math.Min(h, bank);
        }

        public static float BrookBedAt(double arc) => (float)Brook.HeightAt(arc);

        /// <summary>Waldanteil (0…1) fuer Baeume und Bodenfarbe.</summary>
        public static float ForestDensity(float x, float z)
        {
            double df = Math.Sqrt((x - FarmCenter.x) * (x - FarmCenter.x) + (z - FarmCenter.y) * (z - FarmCenter.y));
            double open = 1 - S(150, 230, df);           // Weideland frei
            double west = S(-830, -960, x);              // Wald beginnt westlich des Ackers
            double northSlope = S(640, 780, z) * 0.7;     // Bergflanke licht
            double patch = 0.55 + 0.45 * F(x, z, 90, 2, 321);
            double d = west * (1 - open) * (1 - northSlope) * patch;
            // lichter Waldstreifen am Randgebirge
            d = Math.Max(d, S(MaxX - 40, MaxX - 120, x) * (1 - S(MaxX - 120, MaxX - 170, x)) * 0.35 * (1 - open));
            return (float)Math.Max(0, Math.Min(1, d));
        }

        /// <summary>Punkt auf Weg/Bach/Schlucht (Baeume und Felsen halten Abstand).</summary>
        public static bool Blocked(float x, float z, float margin)
        {
            if (PassRoad.Dist(x, z, out _) < 4 + margin) return true;
            if (Brook.Dist(x, z, out _) < 4 + margin) return true;
            if (Ravine.Dist(x, z, out _) < 6 + margin) return true;
            double bx = x - BasinCenter.x, bz = z - BasinCenter.y;
            if (bx * bx + bz * bz < (BasinRadius + 4 + margin) * (BasinRadius + 4 + margin)) return true;
            double fx = x - FarmCenter.x, fz = z - FarmCenter.y;
            if (fx * fx + fz * fz < 120 * 120) return true;
            foreach (var c in Clues)
                if ((x - c.pos.x) * (x - c.pos.x) + (z - c.pos.y) * (z - c.pos.y) < (c.radius * 0.6f) * (c.radius * 0.6f)) return true;
            foreach (Vector2 f in Fords)
                if ((x - f.x) * (x - f.x) + (z - f.y) * (z - f.y) < 14 * 14) return true;
            return false;
        }
    }
}
