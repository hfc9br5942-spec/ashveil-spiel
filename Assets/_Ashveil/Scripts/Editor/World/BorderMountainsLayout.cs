using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Grenzgebirge (Design: Docs/Design/ashveil-grenzgebirge.md) – Hoehenfunktion und Bewegungsgraph.
    /// 1:1-Port des Python-Prototyps (gleiches Rauschen, gleiche Formeln, gleiche Reihenfolge), mit dem Talroute,
    /// Gebirgspass, Sichtkegel vom Plateau und der Fussabdruck ausgemessen wurden (Steigungen ≤ 22 %, Sichttests 1–4).
    ///
    /// Weltkoordinaten in Metern (Kareth-Nullpunkt = Wachtfeuer). Region x 600…3000, z −350…2050, vier Kacheln à 1200 m
    /// im Kareth-Raster. Bei x = 600 (z ≤ 850) ist die Hoehe exakt die von Kareth (<see cref="KarethTerrainBuilder.Height"/>),
    /// damit die Naht ohne Stufe schliesst. Kareth selbst wird nicht veraendert (ausser dem Tunnelportal).
    /// </summary>
    public static class BorderMountainsLayout
    {
        public const float MinX = 600f, MaxX = 3000f, MinZ = -350f, MaxZ = 2050f;
        public const float TileSize = 1200f;
        public const float BaseY = -40f;
        public const float HeightRange = 1000f;

        // ================================================================== Rauschen (identisch zum Prototyp)

        private static double S(double a, double b, double v)
        {
            double t = (v - a) / (b - a);
            if (t < 0) t = 0;
            else if (t > 1) t = 1;
            return t * t * (3 - 2 * t);
        }

        private static uint Hash2(long ix, long iy, long seed)
        {
            uint n = unchecked((uint)(ix * 374761393L + iy * 668265263L + seed * 1442695041L));
            n = unchecked((uint)((ulong)(n ^ (n >> 13)) * 1274126177UL));
            return n ^ (n >> 16);
        }

        private static double Grad(long i, long j, double dx, double dy, long seed)
        {
            uint h = Hash2(i, j, seed);
            double a = (h & 0xFFFF) / 65536.0 * 2.0 * Math.PI;
            return Math.Cos(a) * dx + Math.Sin(a) * dy;
        }

        /// <summary>Gradientenrauschen, etwa −0,7…0,7.</summary>
        private static double GNoise(double x, double y, long seed)
        {
            double x0 = Math.Floor(x), y0 = Math.Floor(y);
            double fx = x - x0, fy = y - y0;
            long ix = (long)x0, iy = (long)y0;
            double u = fx * fx * fx * (fx * (fx * 6 - 15) + 10);
            double v = fy * fy * fy * (fy * (fy * 6 - 15) + 10);
            double n00 = Grad(ix, iy, fx, fy, seed), n10 = Grad(ix + 1, iy, fx - 1, fy, seed);
            double n01 = Grad(ix, iy + 1, fx, fy - 1, seed), n11 = Grad(ix + 1, iy + 1, fx - 1, fy - 1, seed);
            double a = n00 + (n10 - n00) * u;
            return a + ((n01 + (n11 - n01) * u) - a) * v;
        }

        public static double Fbm(double x, double z, double scale, int oct, long seed)
        {
            double s = 0, amp = 1, tot = 0, f = 1.0 / scale;
            for (int o = 0; o < oct; o++)
            {
                s += amp * GNoise(x * f, z * f, seed + o * 17);
                tot += amp;
                amp *= 0.5;
                f *= 2.03;
            }
            return s / tot;
        }

        private static double Ridged(double x, double z, double scale, int oct, long seed)
        {
            double s = 0, amp = 1, tot = 0, f = 1.0 / scale;
            for (int o = 0; o < oct; o++)
            {
                double n = 1.0 - Math.Abs(GNoise(x * f, z * f, seed + o * 31) * 1.4);
                s += amp * n * n;
                tot += amp;
                amp *= 0.5;
                f *= 2.1;
            }
            return s / tot;
        }

        // ================================================================== Polylinien

        /// <summary>Weg aus Punkten (x, z, Hoehe) mit weiteren Werten je Knoten (Talroute: Sohlenbreite, Wandsteilheit).</summary>
        public sealed class Polyline
        {
            public readonly double[] X, Z, H, A, B, Cum;
            public readonly int Count;
            public double Length => Cum[Count - 1];

            public Polyline(IList<Vector3> pts, IList<Vector2> extra = null)
            {
                Count = pts.Count;
                X = new double[Count];
                Z = new double[Count];
                H = new double[Count];
                A = new double[Count];
                B = new double[Count];
                Cum = new double[Count];
                for (int i = 0; i < Count; i++)
                {
                    X[i] = pts[i].x;
                    Z[i] = pts[i].z;
                    H[i] = pts[i].y;
                    if (extra != null)
                    {
                        A[i] = extra[i].x;
                        B[i] = extra[i].y;
                    }
                    if (i > 0) Cum[i] = Cum[i - 1] + Math.Sqrt((X[i] - X[i - 1]) * (X[i] - X[i - 1]) + (Z[i] - Z[i - 1]) * (Z[i] - Z[i - 1]));
                }
            }

            private Polyline(double[] x, double[] z, double[] h)
            {
                Count = x.Length;
                X = x;
                Z = z;
                H = h;
                A = new double[Count];
                B = new double[Count];
                Cum = new double[Count];
                for (int i = 1; i < Count; i++)
                    Cum[i] = Cum[i - 1] + Math.Sqrt((X[i] - X[i - 1]) * (X[i] - X[i - 1]) + (Z[i] - Z[i - 1]) * (Z[i] - Z[i - 1]));
            }

            public static Polyline FromDoubles(double[] x, double[] z, double[] h) => new Polyline(x, z, h);

            /// <summary>Abstand zur Linie und Bogenlaenge des naechsten Punkts (erstes Segment gewinnt bei Gleichstand).</summary>
            public double Dist(double px, double pz, out double arc)
            {
                double best = 1e9;
                arc = 0;
                for (int i = 0; i < Count - 1; i++)
                {
                    double ax = X[i], az = Z[i], dx = X[i + 1] - ax, dz = Z[i + 1] - az;
                    double l2 = dx * dx + dz * dz;
                    double t = ((px - ax) * dx + (pz - az) * dz) / l2;
                    if (t < 0) t = 0;
                    else if (t > 1) t = 1;
                    double qx = ax + t * dx - px, qz = az + t * dz - pz;
                    double d = Math.Sqrt(qx * qx + qz * qz);
                    if (d < best)
                    {
                        best = d;
                        arc = Cum[i] + t * (Cum[i + 1] - Cum[i]);
                    }
                }
                return best;
            }

            private static double Interp(double[] c, double[] v, double s)
            {
                if (s <= c[0]) return v[0];
                int n = c.Length;
                if (s >= c[n - 1]) return v[n - 1];
                int lo = 0, hi = n - 1;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) >> 1;
                    if (c[mid] <= s) lo = mid;
                    else hi = mid;
                }
                double span = c[hi] - c[lo];
                return span <= 0 ? v[hi] : v[lo] + (v[hi] - v[lo]) * (s - c[lo]) / span;
            }

            public double HeightAt(double arc) => Interp(Cum, H, arc);
            public double AAt(double arc) => Interp(Cum, A, arc);
            public double BAt(double arc) => Interp(Cum, B, arc);

            /// <summary>Punkt (Welt) bei Bogenlaenge s.</summary>
            public Vector3 PointAt(double s) => new Vector3((float)Interp(Cum, X, s), (float)Interp(Cum, H, s), (float)Interp(Cum, Z, s));

            /// <summary>Richtung (x/z, normiert) bei Bogenlaenge s.</summary>
            public Vector3 DirAt(double s)
            {
                Vector3 a = PointAt(Math.Max(0, s - 2)), b = PointAt(Math.Min(Length, s + 2));
                Vector3 d = b - a;
                d.y = 0f;
                return d.sqrMagnitude < 1e-6f ? Vector3.forward : d.normalized;
            }
        }

        // ================================================================== Layout

        /// <summary>Talroute (Route B): (x, Bodenhoehe, z) + (halbe Sohlenbreite, Wandsteilheit).</summary>
        private static readonly double[,] TalNodes =
        {
            { 655, 60, 6, 14, 2.2 },    // T0 Tunnelausgang (Tunnel durch die Kareth-Ostwand ab 515/60)
            { 700, 95, 9, 34, 1.4 },    // T1 Abzweig Gebirgspass (Schluckloch des Bachs)
            { 820, 185, 18, 42, 1.3 },  // T2 Lagerplatz, Fuss der Serpentinen
            { 925, 285, 30, 13, 4.0 },  // T3 Felsspalte (eng, steil)
            { 1030, 318, 44, 13, 4.0 }, // T4 Ende der Spalte
            { 1110, 350, 58, 40, 1.6 }, // T5 Ferse (Eintritt in die Lichtung)
            { 1215, 372, 63, 45, 1.6 }, // T6
            { 1330, 392, 66, 45, 1.6 }, // T7 an der Innenseite des Ballens
            { 1450, 450, 72, 40, 1.6 }, // T8 neben den Zehen
            { 1565, 478, 92, 26, 1.8 }, // T9 Ausgang Lichtung
            { 1690, 505, 118, 24, 2.0 },// T10
            { 1820, 515, 140, 22, 2.0 },// T11 Talsattel
            { 2000, 470, 121, 30, 1.6 },// T12
            { 2250, 385, 97, 40, 1.3 }, // T13 alte Grenzbefestigung
            { 2520, 310, 78, 55, 1.0 }, // T14
            { 2760, 262, 66, 40, 1.2 }, // T15
            { 2990, 250, 60, 22, 2.2 }, // T16 Ausgang Bruchmark Sued (E1, verschuettet)
        };

        public static readonly Vector2 FootCenter = new Vector2(1255f, 520f);
        // 30.09. (Max): Der Beschuetzer stand mit dem Ruecken zur Kerbe – die Ferse liegt zur Wunde, die Zehen zeigen von ihr
        // weg. Im Entwurfsraum Suedwest = in der Welt Nordwest (Kern um 90 Grad gedreht). Gespiegelt statt gedreht, damit
        // Plateau, Wasserfall und Weg ihre Seite behalten (es ist damit der andere Fuss).
        public const double FootAx = -0.7071, FootAz = -0.7071;  // Laengsachse Ferse -> Zehen
        public const double FootBx = -0.7071, FootBz = 0.7071;   // quer (+ = Aussenseite, zum Plateau)
        private const double FootLen = 330.0;
        private const double BasinRa = 300.0, BasinRb = 215.0;
        public const float BasinFloor = 66f;
        private static readonly double[,] FootParts =
        {
            { 0, 0, 60, 47, 7.0 },     // Ferse (tief, feucht)
            { 105, 26, 78, 30, 2.5 },  // Mittelfuss aussen
            { 118, -16, 62, 15, 2.0 }, // Gewoelbe innen
            { 212, -6, 54, 74, 3.5 },  // Ballen (flache Wiese)
        };
        private static readonly double[,] FootToes = { { 292, -54, 30, 5.5 }, { 306, -3, 21, 5.0 }, { 301, 35, 18, 5.0 }, { 289, 64, 16, 4.5 }, { 272, 88, 14, 4.0 } };
        private const double HeelU0 = -FootLen / 2 + 58;

        public static readonly Vector2 PlateauCenter = new Vector2(985f, 815f);
        private const double PlatRu = 95.0, PlatRv = 70.0, PlatYaw = 35.0;
        public const float PlateauHeight = 420f;

        private static readonly double[,] Cones = { { 105.0, 165.0, 1.35, 0 }, { 194.0, 228.0, 0.80, 1 } };

        private static readonly double[,] RouteAEastNodes =
        {
            // ueber das Plateau (Westrand -> Nordrand): wer weiterzieht, laeuft an der Schenke vorbei – der Weg um das
            // Plateau herum haette als Damm die Sicht vom Plateau ins Tal verdeckt (Sichttest 4)
            { 915.27, 821.10, 420.0 }, { 962.50, 841.81, 420.0 }, { 1005.19, 890.34, 421.0 }, { 1040.0, 965.0, 432.0 },
            { 1100.0, 1050.0, 448.0 }, { 1150.0, 1105.0, 462.0 },
            { 1350, 1135, 490 }, { 1550, 1105, 515 }, { 1750, 1050, 535 }, { 1950, 1000, 528 }, { 2150, 945, 505 },
            { 2350, 905, 472 }, { 2550, 885, 432 }, { 2750, 935, 388 }, { 2985, 1000, 345 },
        };

        public static readonly Polyline Tal;
        public static readonly Polyline RouteAWest;
        public static readonly Polyline SchenkeBranch;
        public static readonly Polyline RouteAEast;
        public static readonly Vector3 SwitchbackEnd;

        static BorderMountainsLayout()
        {
            var tal = new List<Vector3>();
            var talExtra = new List<Vector2>();
            for (int i = 0; i < TalNodes.GetLength(0); i++)
            {
                tal.Add(new Vector3((float)TalNodes[i, 0], (float)TalNodes[i, 2], (float)TalNodes[i, 1]));
                talExtra.Add(new Vector2((float)TalNodes[i, 3], (float)TalNodes[i, 4]));
            }
            Tal = new Polyline(tal, talExtra);

            List<double[]> sw = ArcSwitchbacks(199.0, 223.0, 560.0, 104.0, 0.20, 0.80);
            double[] s0 = sw[sw.Count - 1];
            SwitchbackEnd = new Vector3((float)s0[0], (float)s0[2], (float)s0[1]);
            var west = new List<double[]> { new double[] { 820, 185, 18 } };
            west.AddRange(sw);
            RouteAWest = FromList(west);

            // Schenkenabzweig: S0 -> Plateaurand -> Plateau, gleichmaessig steigend
            var b = new List<double[]> { new[] { s0[0], s0[1] } };
            foreach (var (bb, r) in new[] { (240.0, 118.0), (258.0, 98.0), (275.0, 70.0) })
            {
                Pol(bb, r, out double px, out double pz);
                b.Add(new[] { px, pz });
            }
            double total = 0;
            var cumB = new double[b.Count];
            for (int i = 1; i < b.Count; i++)
            {
                total += Math.Sqrt((b[i][0] - b[i - 1][0]) * (b[i][0] - b[i - 1][0]) + (b[i][1] - b[i - 1][1]) * (b[i][1] - b[i - 1][1]));
                cumB[i] = total;
            }
            var branch = new List<double[]>();
            for (int i = 0; i < b.Count; i++) branch.Add(new[] { b[i][0], b[i][1], s0[2] + (PlateauHeight - s0[2]) * (cumB[i] / total) });
            SchenkeBranch = FromList(branch);

            RouteAEast = FromList(SmoothRoute(RouteAEastNodes, 3, 20.0));
            BuildRiver();
        }

        private static Polyline FromList(List<double[]> pts)
        {
            var x = new double[pts.Count];
            var z = new double[pts.Count];
            var h = new double[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                x[i] = pts[i][0];
                z[i] = pts[i][1];
                h[i] = pts[i][2];
            }
            return Polyline.FromDoubles(x, z, h);
        }

        private static void Pol(double bearing, double r, out double x, out double z)
        {
            x = PlateauCenter.x + r * Math.Sin(bearing * Math.PI / 180.0);
            z = PlateauCenter.y + r * Math.Cos(bearing * Math.PI / 180.0);
        }

        /// <summary>Punkt in Peilung (Grad ab Norden, im Uhrzeigersinn) und Abstand vom Plateau-Mittelpunkt.</summary>
        public static Vector2 FromPlateau(float bearing, float r)
        {
            Pol(bearing, r, out double x, out double z);
            return new Vector2((float)x, (float)z);
        }

        private static double ConeFace(double r, double k = 0.80) => PlateauHeight - 6 - k * Math.Max(0.0, r - 80);

        /// <summary>Serpentinen als Boegen um das Plateau (Hoehenlinien des Kegels), jeder Schenkel steigt mit grade.</summary>
        private static List<double[]> ArcSwitchbacks(double bA, double bB, double r0, double rEnd, double grade, double k)
        {
            var pts = new List<double[]>();
            double r = r0, b = bA, other = bB;
            while (r > rEnd + 1)
            {
                double arcLen = r * Math.Abs(other - b) * Math.PI / 180.0;
                double dr = Math.Min(grade * arcLen / k, r - rEnd);
                int n = Math.Max(4, (int)(arcLen / 25));
                for (int i = 0; i <= n; i++)
                {
                    double t = i / (double)n;
                    double rr = r - dr * t, bb = b + (other - b) * t;
                    Pol(bb, rr, out double x, out double z);
                    if (pts.Count > 0 && i == 0) continue;
                    pts.Add(new[] { x, z, ConeFace(rr, k) });
                }
                r -= dr;
                (b, other) = (other, b);
            }
            return pts;
        }

        private static List<double[]> SmoothRoute(double[,] nodes, int it, double step)
        {
            var pts = new List<double[]>();
            for (int i = 0; i < nodes.GetLength(0); i++) pts.Add(new[] { nodes[i, 0], nodes[i, 1], nodes[i, 2] });
            for (int k = 0; k < it; k++)
            {
                var o = new List<double[]> { pts[0] };
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    double[] a = pts[i], c = pts[i + 1];
                    o.Add(new[] { 0.75 * a[0] + 0.25 * c[0], 0.75 * a[1] + 0.25 * c[1], 0.75 * a[2] + 0.25 * c[2] });
                    o.Add(new[] { 0.25 * a[0] + 0.75 * c[0], 0.25 * a[1] + 0.75 * c[1], 0.25 * a[2] + 0.75 * c[2] });
                }
                o.Add(pts[pts.Count - 1]);
                pts = o;
            }
            var cum = new double[pts.Count];
            for (int i = 1; i < pts.Count; i++)
                cum[i] = cum[i - 1] + Math.Sqrt((pts[i][0] - pts[i - 1][0]) * (pts[i][0] - pts[i - 1][0]) + (pts[i][1] - pts[i - 1][1]) * (pts[i][1] - pts[i - 1][1]));
            var res = new List<double[]>();
            for (double sd = 0; sd < cum[cum.Length - 1]; sd += step)
            {
                int i = Array.BinarySearch(cum, sd);
                if (i < 0) i = ~i - 1;
                else
                {
                    // np.searchsorted(side='right') - 1
                    while (i + 1 < cum.Length && cum[i + 1] <= sd) i++;
                }
                i = Math.Min(i, pts.Count - 2);
                double t = (sd - cum[i]) / (cum[i + 1] - cum[i]);
                res.Add(new[] { pts[i][0] + t * (pts[i + 1][0] - pts[i][0]), pts[i][1] + t * (pts[i + 1][1] - pts[i][1]), pts[i][2] + t * (pts[i + 1][2] - pts[i][2]) });
            }
            res.Add(pts[pts.Count - 1]);
            return res;
        }

        // ================================================================== Hoehe

        /// <summary>Kareth-Hoehe an der Naht (x = 600), wie sie im Kareth-Terrain gespeichert ist (−40…120).</summary>
        /// <summary>
        /// Hoehe an der Entwurfs-Westkante (x = 600). Bis 30.09. lag dort Kareths Ostwand (Tunnel). Seit der Kern
        /// suedoestlich von Kareth liegt (BorderMountainsFrame), ist die Kante die Nordseite zum Eschenfeld: offene
        /// Talmuendung auf Hoehe des Talbodens T0 (6 m) beim Talpass-Tor, zu den Seiten sanft ansteigende Vorberge.
        /// Das erzeugte Umland gleicht sich an diese Kante an.
        /// </summary>
        public static float KarethEdge(float z) => (float)(6.0 + 58.0 * S(40, 420, Math.Abs(z - TunnelZ)) + 90.0 * S(900, 1500, z));

        /// <summary>Gelaendehoehe (m) an einem Weltpunkt – dieselbe Funktion, aus der die Kacheln gebaut werden.</summary>
        public static float Height(float x, float z) => (float)Final(x, z, KarethEdge(z));

        /// <summary>Punkt auf dem Boden.</summary>
        public static Vector3 OnGround(float x, float z, float lift = 0f) => new Vector3(x, Height(x, z) + lift, z);

        /// <summary>Bodenhoehe; <paramref name="kh"/> = Karethhoehe an der Naht fuer dieses z (<see cref="KarethEdge"/>).</summary>
        internal static double Final(double x, double z, double kh)
        {
            double h = Raw(x, z, kh, out _);
            h = CarvePath(h, x, z, RouteAWest, 3.5, 10.0);
            h = CarvePath(h, x, z, SchenkeBranch, 4.0, 10.0);
            h = CarvePath(h, x, z, RouteAEast, 3.0, 10.0);
            if (River != null)
            {
                // Bachbett: 1,3 m tiefe Rinne, Ufer ~30 Grad (nicht im Prototyp – kommt nach den Wegen)
                double dr = River.Dist(x, z, out double ra);
                if (dr < 60)
                {
                    double bed = River.HeightAt(ra);
                    h = Math.Min(h, bed + 1.3 * S(1.2, 4.0, dr) + 0.6 * Math.Max(0, dr - 4));
                }
            }
            // Ostportal des Tunnels: schmale Kerbe unter den Tunnelboden (statt Terrain-Loechern, siehe BorderTunnel)
            if (x > TunnelNotchX0 && x < TunnelEastX + 0.6 && Math.Abs(z - TunnelZ) < 6.4)
                h = Math.Min(h, Tal.H[0] - 1.5);
            double ws = S(600, 616, x) + S(850, 866, z) - S(600, 616, x) * S(850, 866, z);
            return kh + (h - kh) * ws;
        }

        private static double CarvePath(double h, double x, double z, Polyline p, double w, double band, double k = 1.1)
        {
            double d = p.Dist(x, z, out double arc);
            double ph = p.HeightAt(arc);
            double kk = k * (1 + 0.3 * Fbm(x, z, 40, 2, 97));
            double lim = kk * Math.Max(0, d - w) + 0.4 * S(w, w + band, d) * band;
            double diff = h - ph;
            if (diff > lim) diff = lim;
            else if (diff < -lim) diff = -lim;
            return ph + diff;
        }

        private static double BaseHeight(double x, double z, double kh)
        {
            double wx = x + 180 * Fbm(x, z, 700, 2, 91), wz = z + 180 * Fbm(x, z, 700, 2, 93);
            double r = Ridged(wx, wz, 820, 3, 11);
            double m = Fbm(x, z, 420, 3, 5);
            double n = Fbm(x, z, 140, 2, 7);
            double core = S(640, 1050, x) * (1 - 0.45 * S(2350, 2950, x));
            double north = 0.7 + 0.4 * S(200, 1300, z);
            double mountain = 110 + core * north * (200 + 480 * r + 90 * m) + 18 * n;
            double wk = 1 - S(850, 950, z);
            double edge = kh * wk + (260 + 40 * m) * (1 - wk);
            return edge + (mountain - edge) * S(600, 700, x);
        }

        private static double ViewCone(double x, double z, double h)
        {
            double dx = x - PlateauCenter.x, dz = z - PlateauCenter.y;
            double bearing = (Math.Atan2(dx, dz) * 180.0 / Math.PI + 360.0) % 360.0;
            double dist = Math.Sqrt(dx * dx + dz * dz);
            if (dist > 700) return h;
            double edge = Math.Max(0, dist - 80);
            for (int c = 0; c < Cones.GetLength(0); c++)
            {
                double b0 = Cones[c, 0], b1 = Cones[c, 1], k = Cones[c, 2];
                bool shape = Cones[c, 3] > 0.5;
                double w = S(b0 - 18, b0 + 4, bearing) * (1 - S(b1 - 4, b1 + 18, bearing)) * (1 - S(560, 700, dist));
                if (w <= 0) continue;
                double amp = shape ? 7.0 : 18.0;
                double limit = PlateauHeight - 6 - k * edge + amp * Fbm(x, z, 120, 2, 55) * S(0, 60, edge);
                limit = Math.Max(limit, (shape ? 22.0 : 62.0) + 16 * Fbm(x, z, 200, 2, 57));
                double target = shape ? limit : Math.Min(h, limit);
                h = h * (1 - w) + target * w;
            }
            return h;
        }

        private static double Saddle(double x, double z, double h)
        {
            double d = RouteAEast.Dist(x, z, out double arc);
            double ph = RouteAEast.HeightAt(arc);
            double target = ph + 14 + 0.9 * Math.Max(0, d - 22) * (1 + 0.5 * Fbm(x, z, 90, 3, 79)) + 22 * Ridged(x, z, 130, 2, 77) * S(20, 90, d);
            return Math.Min(h, target);
        }

        /// <summary>Lage im Fussabdruck: u laengs (ab Fersenmitte), v quer – leicht verzogen (Erosion).</summary>
        private static void FootLocal(double x, double z, out double u, out double v)
        {
            double dx = x - FootCenter.x, dz = z - FootCenter.y;
            u = dx * FootAx + dz * FootAz - HeelU0;
            v = dx * FootBx + dz * FootBz;
            u += 9 * Fbm(x, z, 55, 2, 81);
            v += 9 * Fbm(x, z, 55, 2, 83);
        }

        /// <summary>Negativ = im Abdruck. Tiefe der Senke dazu.</summary>
        public static double FootSdf(double x, double z, out double depth, out double u, out double v)
        {
            FootLocal(x, z, out u, out v);
            double sdf = 1e9;
            depth = 0;
            for (int i = 0; i < FootParts.GetLength(0); i++)
            {
                double cu = FootParts[i, 0], cv = FootParts[i, 1], ru = FootParts[i, 2], rv = FootParts[i, 3], dp = FootParts[i, 4];
                double r = Math.Sqrt(((u - cu) / ru) * ((u - cu) / ru) + ((v - cv) / rv) * ((v - cv) / rv));
                double d = (r - 1) * Math.Min(ru, rv);
                sdf = Math.Min(sdf, d);
                depth = Math.Max(depth, dp * S(0, 12, -d));
            }
            for (int i = 0; i < FootToes.GetLength(0); i++)
            {
                double cu = FootToes[i, 0], cv = FootToes[i, 1], rr = FootToes[i, 2], dp = FootToes[i, 3];
                double d = Math.Sqrt((u - cu) * (u - cu) + (v - cv) * (v - cv)) - rr;
                sdf = Math.Min(sdf, d);
                depth = Math.Max(depth, dp * S(0, 8, -d));
            }
            return sdf;
        }

        /// <summary>Normierter Abstand zur Lichtungs-Ellipse (&lt; 1 = in der Lichtung).</summary>
        public static double BasinR(double x, double z)
        {
            double dx = x - FootCenter.x, dz = z - FootCenter.y;
            double ua = dx * FootAx + dz * FootAz + 45 * Fbm(x, z, 160, 2, 61);
            double vb = dx * FootBx + dz * FootBz + 40 * Fbm(x, z, 160, 2, 63);
            return Math.Sqrt((ua / BasinRa) * (ua / BasinRa) + (vb / BasinRb) * (vb / BasinRb));
        }

        /// <summary>Maske des Plateaus (1 = oben) und normierter Radius.</summary>
        public static double PlateauMask(double x, double z, out double r)
        {
            double c = Math.Cos(PlatYaw * Math.PI / 180.0), s = Math.Sin(PlatYaw * Math.PI / 180.0);
            double dx = x - PlateauCenter.x, dz = z - PlateauCenter.y;
            double u = dx * c + dz * s, v = -dx * s + dz * c;
            r = Math.Sqrt((u / PlatRu) * (u / PlatRu) + (v / PlatRv) * (v / PlatRv));
            return 1 - S(0.85, 1.12, r);
        }

        private static double Raw(double x, double z, double kh, out double basinR)
        {
            double h = BaseHeight(x, z, kh);
            h = ViewCone(x, z, h);
            h = Saddle(x, z, h);
            double n = Fbm(x, z, 90, 3, 23);

            // Talroute (nur abtragen)
            double d = Tal.Dist(x, z, out double arc);
            double floor = Tal.HeightAt(arc), hw = Tal.AAt(arc), k = Tal.BAt(arc);
            double wall = k * Math.Pow(Math.Max(0, d - hw), 1.25);
            double t0x = x - Tal.X[0], t0z = z - Tal.Z[0];
            double flat0 = S(8, 40, Math.Sqrt(t0x * t0x + t0z * t0z));
            double carved = floor + 1.2 * n * flat0 + wall * (1 + 0.35 * Fbm(x, z, 70, 3, 41)) + Math.Max(0, 655 - x) * 8;
            h = Math.Min(h, carved);

            // Lichtung (Ellipse)
            double dx = x - FootCenter.x, dz = z - FootCenter.y;
            double ua = dx * FootAx + dz * FootAz + 45 * Fbm(x, z, 160, 2, 61);
            double vb = dx * FootBx + dz * FootBz + 40 * Fbm(x, z, 160, 2, 63);
            double rr = Math.Sqrt((ua / BasinRa) * (ua / BasinRa) + (vb / BasinRb) * (vb / BasinRb));
            basinR = rr;
            double outside = Math.Max(0, rr - 1) * BasinRb;
            double bfloor = BasinFloor + 2.0 * n - 0.012 * ua;   // faellt von der Ferse zu den Zehen: der Bach laeuft durch die Spur hinaus
            h = Math.Min(h, bfloor + 1.7 * Math.Pow(outside, 1.3) * (1 + 0.4 * Fbm(x, z, 60, 3, 45)));

            // Fussabdruck: Senken + Randwall + parallele Risse
            double inb = 1 - S(0.9, 1.0, rr);
            if (inb > 0)
            {
                double sdf = FootSdf(x, z, out double depth, out double u, out double v);
                double brk = S(-0.08, 0.12, Fbm(u, v, 38, 2, 71));
                double rimH = 8 + 6 * S(250, 0, u);
                double rim = rimH * Math.Exp(-((sdf - 9) / 7.5) * ((sdf - 9) / 7.5)) * (0.35 + 0.65 * brk);
                double cracks = 0;
                foreach (double cu in new[] { 168.0, 190.0, 246.0 })
                    cracks += 1.6 * Math.Exp(-((u - cu) / 2.6) * ((u - cu) / 2.6)) * S(0, 6, -sdf);
                h += inb * (rim - depth - cracks);
            }

            double pm = PlateauMask(x, z, out _);
            h = h * (1 - pm) + (PlateauHeight + 0.6 * n) * pm;
            return h;
        }

        // ================================================================== Orte (Bewegungsgraph, Design §38)

        /// <summary>Talknoten Ti als Weltpunkt (Bodenhoehe).</summary>
        public static Vector3 TalNode(int i) => new Vector3((float)Tal.X[i], (float)Tal.H[i], (float)Tal.Z[i]);
        public static int TalCount => Tal.Count;

        /// <summary>Tunnel durch die Kareth-Ostwand (Max 27.09.: "Tunnel durch die Wand").</summary>
        public const float TunnelZ = 60f;
        public const float TunnelWestX = 510f;
        public const float TunnelEastX = 658f;
        public const float TunnelWidth = 8f;
        public const float TunnelHeight = 8f;
        /// <summary>Beginn der Kerbe am Ostportal (Roehre laeuft bis TunnelEastX aus dem Fels).</summary>
        public const float TunnelNotchX0 = 640f;

        /// <summary>Ausgaenge in die Bruchmark (noch nicht gebaut – Felssturz).</summary>
        public static readonly Vector2 ExitSouth = new Vector2(2930f, 252f);
        public static readonly Vector2 ExitPass = new Vector2(2930f, 985f);

        /// <summary>Hochpunkt des Gebirgspasses (Wachturmruine).</summary>
        public static readonly Vector2 HighPass = new Vector2(1750f, 1050f);

        /// <summary>Schenke: Mitte auf dem Plateau, Terrasse zur Lichtung (Peilung 137 Grad).</summary>
        public const float SchenkeBearing = 137f;
        public static Vector2 SchenkeCenter => FromPlateau(SchenkeBearing, 60f);   // Terrasse reicht ueber die Kante (r ≈ 80)

        // ================================================================== Bach (§4)

        /// <summary>
        /// Bach: Wasserfall aus der Plateauwand in die Ferse -> Tuempel -> durch eine Luecke im Randwall ins Tal -> an der
        /// Nordseite des Talwegs abwaerts, quert ihn hinter dem Lagerplatz (Bruecke) -> verschwindet bei T1 im Schluckloch.
        /// Hoehe = Bachbett (stromabwaerts nie steigend). Null, solange der Bach gebaut wird.
        /// </summary>
        public static Polyline River { get; private set; }
        public static Vector2 RiverSource { get; private set; }
        public static Vector3 WaterfallTop { get; private set; }
        public static Vector3 WaterfallFoot { get; private set; }
        private static double _crossArc;

        public static double RiverBedAt(double s) => River.HeightAt(s);

        /// <summary>Stellen, an denen der Talweg den Bach quert (Bruecken).</summary>
        public static IEnumerable<Vector3> RiverCrossings()
        {
            Vector3 p = Tal.PointAt(_crossArc);
            yield return p;
        }

        private static double TalArcOf(int node) => Tal.Cum[node];

        private static void BuildRiver()
        {
            River = null;
            double heelA = HeelU0;                          // Fersenmitte auf der Laengsachse
            double hx = FootCenter.x + heelA * FootAx, hz = FootCenter.y + heelA * FootAz;
            RiverSource = new Vector2((float)hx, (float)hz);

            var pts = new List<double[]>();
            // Ferse -> Luecke im Wall -> Talboden bei T5 (Nordseite)
            pts.Add(new[] { hx, hz });
            pts.Add(new[] { hx - 30, hz - 38 });
            pts.Add(new[] { hx - 55, hz - 70 });
            // Tal abwaerts: von T5 bis kurz hinter T1, 9 m neben dem Weg; Seitenwechsel hinter T2 (Bruecke)
            _crossArc = TalArcOf(2) + 30;
            double start = TalArcOf(5) - 5, end = TalArcOf(1) + 18;
            for (double s = start; s >= end; s -= 10)
            {
                Vector3 c = Tal.PointAt(s), dir = Tal.DirAt(s);
                double hw = Tal.AAt(s);
                double off = Math.Min(9.0, hw - 3.5);
                double side = -1 + 2 * S(_crossArc + 14, _crossArc - 14, s);   // Nordseite (-1) oberhalb, Suedseite (+1) unterhalb
                double rx = dir.z, rz = -dir.x;                                 // rechts der Wegrichtung (T0 -> T16)
                pts.Add(new[] { c.x + rx * off * side, c.z + rz * off * side });
            }
            // Bettenhoehe: Gelaende minus 1,8 m, stromabwaerts nie steigend
            var bed = new double[pts.Count];
            double prev = double.MaxValue;
            for (int i = 0; i < pts.Count; i++)
            {
                double g = FinalNoRiver(pts[i][0], pts[i][1]) - 1.8;
                prev = Math.Min(prev, g);
                bed[i] = prev;
            }
            var x = new double[pts.Count];
            var z = new double[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                x[i] = pts[i][0];
                z[i] = pts[i][1];
            }
            // Wasserfall: von der Ferse nach aussen (Nordwest, Richtung Plateau) bis zur Wand
            double floorH = FinalNoRiver(hx, hz);
            Vector3 foot = Vector3.zero, top = Vector3.zero;
            bool inWall = false;
            for (double r = 20; r < 420; r += 4)
            {
                double px = hx + FootBx * r, pz = hz + FootBz * r;
                double h = FinalNoRiver(px, pz);
                if (!inWall && h > floorH + 6)
                {
                    inWall = true;
                    foot = new Vector3((float)px, (float)floorH + 1.5f, (float)pz);
                }
                if (inWall)
                {
                    top = new Vector3((float)px, (float)h, (float)pz);
                    double ahead = FinalNoRiver(hx + FootBx * (r + 8), hz + FootBz * (r + 8));
                    if (ahead - h < 8 || h > floorH + 260) break;   // Wand wird flach -> Kante
                }
            }
            WaterfallFoot = foot;
            WaterfallTop = top;
            River = Polyline.FromDoubles(x, z, bed);
        }

        private static double FinalNoRiver(double x, double z)
        {
            double h = Raw(x, z, 0, out _);
            h = CarvePath(h, x, z, RouteAWest, 3.5, 10.0);
            h = CarvePath(h, x, z, SchenkeBranch, 4.0, 10.0);
            return CarvePath(h, x, z, RouteAEast, 3.0, 10.0);
        }
    }
}
