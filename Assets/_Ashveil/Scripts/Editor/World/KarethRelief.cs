using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// P2.5 Kareth-Relief (Master v0.2, WORLD-072; Auftrag Max 01.10.2026).
    ///
    /// Additive, abschaltbare Hoehenschicht UEBER dem bestehenden Kareth-Gelaende
    /// (<see cref="KarethTerrainBuilder.BaseHeight"/>). Kareth wird nicht neu generiert: Wachtfeuer, Riese, Kueste,
    /// Moorwasser, Uhrwerk-Wand, Kolosseum, Tunnel, Weidepass und die Kartenraender (Naehte zu Grenzgebirge,
    /// Westweiden und Umland) bleiben exakt unveraendert (Schutzmasken, dort Versatz = 0).
    ///
    /// Aufbau (Reihenfolge = Prioritaet des Auftrags):
    ///   1. grosse Formen   : Vorhuegel des Granitplateaus (Felsnasen, Rinnen, Ruecken), Kiefernkamm, Brache
    ///   2. sekundaere Formen: Moorrand, Kuppen, Senke im Tiefen Wald, Einschlagkrater, flache Aschewellen
    ///   3. Wege            : an Wegen wird das Relief zum Sattel abgesenkt, der Wegquerschnitt bleibt eben
    ///   4. Sichtachsen     : die Sicht vom Plateau auf den Riesen bleibt frei (geprueft)
    ///   5. Lesbarkeit      : jeder Quest-Ort und jedes Gegnergebiet steht auf einer ebenen Flaeche
    ///   6. Kleinvariation  : keine – das bestehende Rauschen reicht; kein Noise ueber alles.
    ///
    /// Nur Editor-Zeit: das Ergebnis steckt im normalen Terrain (gleiches Raster 513², 2,34 m), keine Laufzeitkosten,
    /// keine Verformung zur Laufzeit (TECH-028: Perceptual Realism &gt; Brute-Force).
    /// Abschalten: KarethLayout.reliefEnabled = false, danach "Sandbox-Szene erstellen".
    /// Formen und Grenzwerte sind offline vorgeprueft (Steigung auf Wegen, ebene Orte, Sichtachse); die Pruefung im
    /// Editor: Menue "Ashveil/Welt/Kareth-Relief pruefen" (Bericht Logs/Ashveil/Kareth_Relief.txt).
    /// </summary>
    public static class KarethRelief
    {
        public enum Kind { Ridge, Knoll, Crater, Spur, Gully }

        public struct Form
        {
            public Kind Kind;
            public string Name;
            public Vector2 A, B;        // Ridge/Spur/Gully: Strecke; Knoll/Crater: A = Mitte
            public float H, W;          // Hoehe (m, negativ = Senke) und Halbbreite bzw. Radius
            public float Taper, Humps, Phase;
            public float Rb, Rr, Ro, Depth, Rim; // Krater
        }

        private static Form Ridge(string n, float ax, float az, float bx, float bz, float h, float w, float taper = 0.3f, float humps = 0f, float phase = 0f) =>
            new Form { Kind = Kind.Ridge, Name = n, A = new Vector2(ax, az), B = new Vector2(bx, bz), H = h, W = w, Taper = taper, Humps = humps, Phase = phase };
        private static Form Knoll(string n, float x, float z, float r, float h) => new Form { Kind = Kind.Knoll, Name = n, A = new Vector2(x, z), W = r, H = h };
        private static Form Spur(string n, float ax, float az, float bx, float bz, float h, float w) =>
            new Form { Kind = Kind.Spur, Name = n, A = new Vector2(ax, az), B = new Vector2(bx, bz), H = h, W = w, Taper = 0.3f };
        private static Form Gully(string n, float ax, float az, float bx, float bz, float h, float w) =>
            new Form { Kind = Kind.Gully, Name = n, A = new Vector2(ax, az), B = new Vector2(bx, bz), H = h, W = w, Taper = 0.25f };

        /// <summary>Die komponierten Formen (Fassung v5, 01.10.2026: nach Max' Hinweis deutlich kraeftiger – Kaemme 25–35 m,
        /// Taeler dazwischen, zwei Schluchten). Namen sind Arbeitsbezeichnungen im Code/Bericht, keine Ortsnamen im Spiel.</summary>
        public static readonly Form[] Forms =
        {
            // --- Nordosten: breite Brache-Schwelle, hoher Ruecken mit Kuppen (Altes Schlachtfeld auf dem Kamm), Tal zum Plateau
            Knoll("Brache-Grundschwelle", 330f, 470f, 230f, 8f),
            Ridge("Brachruecken", 190f, 420f, 470f, 505f, 30f, 105f, 0.3f, 0.4f, 0.6f),
            Knoll("Aschenweide-Kuppe", 262f, 360f, 75f, 12f),
            // --- Moorbecken: das Wasser bleibt, die Raender heben sich (Weg zur Rampe zwischen zwei Vorhuegeln)
            Ridge("Moorrand Ost", 195f, 210f, 240f, 365f, 18f, 80f, 0.35f, 0.35f, 1.2f),
            Ridge("Moorrand Nordwest", -195f, 350f, -75f, 520f, 26f, 95f, 0.3f, 0.4f, 2.0f),
            Ridge("Moor-Nordschwelle", 55f, 460f, 185f, 505f, 16f, 80f, 0.35f, 0.3f, 0.4f),
            // --- Westen: zwei hohe Kaemme, dazwischen die Klamm; Buckel im Nordwesten, Senke im Tiefen Wald
            Ridge("Kiefernkamm", -285f, -10f, -360f, 410f, 34f, 95f, 0.25f, 0.45f, 0.9f),
            Ridge("Aeusserer Kamm", -480f, 100f, -510f, 380f, 26f, 80f, 0.3f, 0.35f, 2.4f),
            Knoll("Oedis Buckel", -400f, 470f, 95f, 22f),
            Knoll("Nordwestkuppe", -320f, 520f, 80f, 18f),
            Knoll("Senke Tiefer Wald", -195f, 150f, 70f, -4f),
            // --- Mitte zwischen Lager und Moor: zwei ruhige Huegel (Lagerkern und Riese bleiben ausgespart)
            Knoll("Pilgerhuegel", 150f, 150f, 80f, 14f),
            Knoll("Harzhuegel", -150f, 235f, 75f, 12f),
            // --- Aschenfeld: bleibt Ebene (Kanon), dazu der Einschlagkrater (WORLD-078 Geometrie) und flache Aschewellen
            new Form { Kind = Kind.Crater, Name = "Einschlagkrater", A = new Vector2(420f, -150f), Rb = 34f, Rr = 52f, Ro = 95f, Depth = 8f, Rim = 9f },
            Ridge("Aschenwelle 1", 250f, 90f, 390f, -20f, 3.5f, 60f),
            Ridge("Aschenwelle 2", 215f, -95f, 330f, -215f, 4f, 55f),
            Ridge("Aschenwelle 3", 300f, 240f, 420f, 140f, 3f, 55f),
            // --- Plateau oben: Granitkuppen (Wachturm auf seiner Kuppe)
            Knoll("Plateaukuppe West", -180f, 700f, 85f, 12f),
            Knoll("Plateaukuppe Ost", 200f, 710f, 75f, 10f),
            Knoll("Turmkuppe", 320f, 690f, 65f, 12f),
            Knoll("Plateaukuppe Steinkreis-West", -440f, 670f, 75f, 14f),
            // --- Plateaukante: nicht mehr schnurgerade – Felsnasen bis fast Plateauhoehe, tiefe Rinnen
            Spur("Felsnase West", -380f, 600f, -400f, 490f, 30f, 60f),
            Spur("Felsnase Mitte", -140f, 600f, -115f, 505f, 28f, 52f),
            Spur("Felsnase Ost", 250f, 600f, 275f, 495f, 30f, 62f),
            Gully("Rinne Mitte", 128f, 618f, 120f, 552f, -24f, 30f),
            Gully("Rinne Ost", 398f, 618f, 410f, 552f, -24f, 34f),
        };

        /// <summary>Schlucht: begehbarer Boden als gleichmaessige Rampe von der Muendung (Oberflaeche dort) zum Ausgang
        /// (Oberflaeche dort), Bodenhalbbreite, Wandsteilheit (m Hoehe je m). Wird in die Oberflaeche geschnitten.</summary>
        public struct Canyon
        {
            public string Name;
            public Vector2[] Points;
            public float FloorHalfWidth, WallSlope;
        }

        public static readonly Canyon[] Canyons =
        {
            // Nebenroute vom Nordwest-Moorrand hinauf aufs Plateau (Boden ≈ 2 m → 32 m, ≈ 14 %)
            new Canyon { Name = "Plateau-Schlucht", Points = new[] { new Vector2(-212f, 505f), new Vector2(-232f, 560f), new Vector2(-258f, 620f), new Vector2(-268f, 705f) }, FloorHalfWidth = 7f, WallSlope = 1.7f },
            // Klamm zwischen Kiefernkamm und Aeusserem Kamm, Ausgang am Weideweg (≈ 9 %)
            new Canyon { Name = "Klamm", Points = new[] { new Vector2(-425f, 432f), new Vector2(-418f, 370f), new Vector2(-410f, 310f), new Vector2(-405f, 268f) }, FloorHalfWidth = 6f, WallSlope = 1.8f },
        };

        /// <summary>Grundflaeche des Riesen (wie GiantBuilder.Footprint + Schaedeldecke; Ellipsen x, z, Halbachsen) – dort bleibt alles exakt.</summary>
        private static readonly Vector4[] GiantFootprint =
        {
            new Vector4(0f, -275f, 112f, 72f), new Vector4(70f, -200f, 52f, 62f), new Vector4(-70f, -205f, 48f, 58f),
            new Vector4(0f, -320f, 115f, 45f), new Vector4(0f, -88f, 66f, 46f), new Vector4(70f, -110f, 38f, 42f),
            new Vector4(116f, 34f, 26f, 30f), new Vector4(-120f, -121f, 10f, 10f), new Vector4(-140f, -91f, 10f, 10f),
            new Vector4(-203f, -6f, 22f, 22f), new Vector4(-155f, -30f, 16f, 16f), new Vector4(80f, 92f, 36f, 31f)
        };

        /// <summary>Route des Glockenhirten (gleiche Punkte wie EnemyBuilder.BuildShepherdRoute; der Pruefbericht vergleicht sie mit der gebauten Route).</summary>
        public static readonly Vector2[] ShepherdRoute =
        {
            new Vector2(297f, -59f), new Vector2(340f, -122f), new Vector2(413f, -132f), new Vector2(433f, -46f),
            new Vector2(423f, 40f), new Vector2(373f, 93f), new Vector2(314f, 73f), new Vector2(287f, 7f), new Vector2(297f, -59f)
        };

        /// <summary>Gegnergebiete ausserhalb des Lagerkerns (EnemyBuilder: Ground(...)-Punkte) und Hueteplaetze – ebene Flaechen.</summary>
        private static readonly Vector3[] ExtraPads =
        {
            new Vector3(-200f, 150f, 12f), new Vector3(20f, 300f, 12f), new Vector3(110f, 380f, 12f), new Vector3(-30f, 420f, 12f),
            new Vector3(165f, 270f, 12f), new Vector3(-90f, 330f, 12f), new Vector3(-320f, -150f, 12f), new Vector3(-400f, -60f, 12f),
            new Vector3(250f, -150f, 12f), new Vector3(470f, 160f, 12f), new Vector3(480f, -190f, 12f), new Vector3(-250f, 240f, 12f),
            new Vector3(340f, -122f, 12f), new Vector3(423f, 40f, 12f), new Vector3(314f, 73f, 12f),
            new Vector3(60f, 598f, 25f), // Aussichtspunkt Plateau -> Riese (Kareth-Test "Fernsilhouette")
        };

        /// <summary>Quest-Orte ohne ebene Flaeche: der Krater IST die Form.</summary>
        private static readonly HashSet<string> NoPad = new HashSet<string> { "site.kareth.krater" };

        private static readonly Vector2 CoreCenter = new Vector2(-10f, 20f);
        private static readonly Vector2 MoorCenter = new Vector2(40f, 330f);

        // ================================================================== Schalter und Zwischenspeicher

        private static bool? _enabled;
        private static float[] _grid;
        private const float GridStep = 2f;
        private static int _gw, _gh;
        private static List<Vector2[]> _paths;
        private static List<Vector4> _pads; // x, z, Radius, Wert

        /// <summary>An/aus aus dem Kareth-Layout (Standard: an).</summary>
        public static bool Enabled
        {
            get
            {
                if (_enabled == null) _enabled = KarethLayout.LoadOrCreate().reliefEnabled;
                return _enabled.Value;
            }
        }

        /// <summary>Vor jedem Neuaufbau: Schalter neu lesen, Raster neu rechnen.</summary>
        public static void Refresh()
        {
            _enabled = null;
            _grid = null;
            _floors = null;
            _paths = null;
            _pads = null;
        }

        /// <summary>Hoehenversatz (m) des Reliefs an einem Weltpunkt; 0 ausserhalb von Kareth und in allen Schutzbereichen.</summary>
        public static float Offset(float x, float z)
        {
            if (!Enabled) return 0f;
            if (x < KarethTerrainBuilder.MinX || x > KarethTerrainBuilder.MaxX || z < KarethTerrainBuilder.MinZ || z > KarethTerrainBuilder.MaxZ) return 0f;
            if (_grid == null) BuildGrid();
            float fx = (x - KarethTerrainBuilder.MinX) / GridStep, fz = (z - KarethTerrainBuilder.MinZ) / GridStep;
            int ix = Mathf.Clamp((int)fx, 0, _gw - 2), iz = Mathf.Clamp((int)fz, 0, _gh - 2);
            float tx = Mathf.Clamp01(fx - ix), tz = Mathf.Clamp01(fz - iz);
            float a = _grid[iz * _gw + ix], b = _grid[iz * _gw + ix + 1], c = _grid[(iz + 1) * _gw + ix], d = _grid[(iz + 1) * _gw + ix + 1];
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }

        private static void BuildGrid()
        {
            EnsureLists();
            _gw = Mathf.RoundToInt(KarethTerrainBuilder.Size / GridStep) + 1;
            _gh = _gw;
            _grid = new float[_gw * _gh];
            for (int iz = 0; iz < _gh; iz++)
            {
                float z = KarethTerrainBuilder.MinZ + iz * GridStep;
                for (int ix = 0; ix < _gw; ix++)
                    _grid[iz * _gw + ix] = Exact(KarethTerrainBuilder.MinX + ix * GridStep, z);
            }
        }

        private static void EnsureLists()
        {
            if (_paths != null && _pads != null) return;
            _paths = new List<Vector2[]>(KarethTerrainBuilder.RoadPaths) { ShepherdRoute };
            _pads = new List<Vector4>();
            foreach (KarethQuestSites.Site s in KarethQuestSites.All)
            {
                if (NoPad.Contains(s.Id)) continue;
                _pads.Add(new Vector4(s.X, s.Z, Mathf.Max(s.Radius, 20f), 0f));
            }
            foreach (Vector3 p in ExtraPads) _pads.Add(new Vector4(p.x, p.y, p.z, 0f));
            for (int i = 0; i < _pads.Count; i++)
            {
                Vector4 p = _pads[i];
                p.w = CoreValue(p.x, p.y);
                _pads[i] = p;
            }
        }

        // ================================================================== Berechnung (exakt, ohne Raster)

        private static float S(float a, float b, float v) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, v));

        /// <summary>Exakter Wert ohne Raster (Pruefungen, Tests).</summary>
        public static float Exact(float x, float z)
        {
            float r = ExactNoCanyon(x, z);
            // Schluchten in die Oberflaeche schneiden
            float b = KarethTerrainBuilder.BaseHeight(x, z), h = b + r;
            for (int c = 0; c < Canyons.Length; c++) h = Mathf.Min(h, CanyonSurface(c, x, z));
            return h - b;
        }

        private static float[][] _floors;

        /// <summary>Bodenhoehe der Schlucht als Rampe (Oberflaeche an Muendung und Ausgang) plus steile Waende.</summary>
        private static float CanyonSurface(int c, float x, float z)
        {
            Canyon cy = Canyons[c];
            if (_floors == null) _floors = new float[Canyons.Length][];
            if (_floors[c] == null)
            {
                Vector2[] pts = cy.Points;
                float Surface(Vector2 p) => KarethTerrainBuilder.BaseHeight(p.x, p.y) + ExactNoCanyon(p.x, p.y);
                float a = Surface(pts[0]), e = Surface(pts[pts.Length - 1]);
                var len = new float[pts.Length];
                for (int i = 1; i < pts.Length; i++) len[i] = len[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
                var f = new float[pts.Length];
                for (int i = 0; i < pts.Length; i++) f[i] = Mathf.Lerp(a, e, len[i] / len[pts.Length - 1]);
                _floors[c] = f;
            }
            float best = float.MaxValue, floor = 0f;
            var q = new Vector2(x, z);
            for (int i = 0; i + 1 < cy.Points.Length; i++)
            {
                Vector2 a = cy.Points[i], ab = cy.Points[i + 1] - a;
                float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                float d = Vector2.Distance(q, a + ab * t);
                if (d < best)
                {
                    best = d;
                    floor = Mathf.Lerp(_floors[c][i], _floors[c][i + 1], t);
                }
            }
            return floor + Mathf.Max(0f, best - cy.FloorHalfWidth) * cy.WallSlope;
        }

        /// <summary>Formen, Sattel an Wegen, ebener Wegquerschnitt, ebene Orte – ohne Schluchten.</summary>
        private static float ExactNoCanyon(float x, float z)
        {
            EnsureLists();
            float r = CoreValue(x, z);
            float d = NearestOnPath(x, z, out Vector2 onPath);
            r = Mathf.Lerp(r, CoreValue(onPath.x, onPath.y), 1f - S(6f, 26f, d));
            foreach (Vector4 p in _pads)
            {
                float dist = Vector2.Distance(new Vector2(x, z), new Vector2(p.x, p.y));
                float w = 1f - S(p.z * 0.6f, p.z + 70f, dist);
                if (w > 0f) r = Mathf.Lerp(r, p.w, w);
            }
            return r;
        }

        /// <summary>Formen × Sattel an Wegen × Schutzmasken.</summary>
        private static float CoreValue(float x, float z)
        {
            float dp = NearestOnPath(x, z, out _);
            return Raw(x, z) * (0.08f + 0.92f * S(14f, 130f, dp)) * Allow(x, z);
        }

        /// <summary>Summe aller Formen.</summary>
        public static float Raw(float x, float z)
        {
            float below = 1f - S(556f, 586f, z), top = S(548f, 572f, z);
            float sum = 0f;
            foreach (Form f in Forms)
            {
                switch (f.Kind)
                {
                    case Kind.Ridge: sum += RidgeValue(f, x, z); break;
                    case Kind.Knoll: sum += f.H * (1f - S(0f, f.W, Vector2.Distance(new Vector2(x, z), f.A))); break;
                    case Kind.Crater: sum += CraterValue(f, x, z); break;
                    case Kind.Spur: sum += RidgeValue(f, x, z) * below; break;
                    case Kind.Gully: sum += RidgeValue(f, x, z) * top; break;
                }
            }
            return sum;
        }

        private static float RidgeValue(Form f, float x, float z)
        {
            Vector2 ab = f.B - f.A;
            float t = Mathf.Clamp01(Vector2.Dot(new Vector2(x, z) - f.A, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            float d = Vector2.Distance(new Vector2(x, z), f.A + ab * t);
            float end = S(0f, f.Taper, t) * S(0f, f.Taper, 1f - t);
            float crest = 1f - f.Humps * 0.5f * (1f - Mathf.Cos(t * Mathf.PI * 3f + f.Phase)); // mehrere Kuppen statt Wulst
            return f.H * crest * (1f - S(0f, f.W, d)) * (0.15f + 0.85f * end);
        }

        private static float CraterValue(Form f, float x, float z)
        {
            float d = Vector2.Distance(new Vector2(x, z), f.A);
            float bowl = -f.Depth * (1f - S(0f, f.Rb, d));
            float span = d < f.Rr ? f.Rr - f.Rb * 0.4f : f.Ro - f.Rr;
            float ring = f.Rim * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(d - f.Rr) / span), 1.6f);
            return bowl * (1f - S(f.Rb * 0.6f, f.Rr, d)) + ring;
        }

        /// <summary>
        /// 0 = hier bleibt Kareth exakt wie bisher. Lagerkern, Riese (Koerper, Arme, Halle, Schaedeldecke), Kartenraender
        /// (Naehte), Ostwand mit Uhrwerk/Kolosseum/Tunnel, Kueste mit Salzgruben und Moorwasser.
        /// </summary>
        public static float Allow(float x, float z)
        {
            float a = S(115f, 195f, Vector2.Distance(new Vector2(x, z), CoreCenter));
            float giant = (1f - S(60f, 120f, z)) * (1f - S(200f, 260f, Mathf.Abs(x)));
            a *= 1f - giant;
            foreach (Vector4 e0 in GiantFootprint)
            {
                float ex = (x - e0.x) / e0.z, ez = (z - e0.y) / e0.w;
                a *= S(1f, 1.7f, Mathf.Sqrt(ex * ex + ez * ez));
            }
            float e = Mathf.Min(Mathf.Min(x - KarethTerrainBuilder.MinX, KarethTerrainBuilder.MaxX - x), Mathf.Min(z - KarethTerrainBuilder.MinZ, KarethTerrainBuilder.MaxZ - z));
            a *= S(70f, 150f, e);
            a *= 1f - S(380f, 430f, x) * S(-80f, -50f, z);
            float coastLine = (x + z + 520f) * 0.7071f;
            a *= S(60f, 130f, coastLine);
            a *= S(170f, 215f, Vector2.Distance(new Vector2(x, z), MoorCenter));
            return a;
        }

        private static float NearestOnPath(float x, float z, out Vector2 onPath)
        {
            var p = new Vector2(x, z);
            float best = float.MaxValue;
            onPath = p;
            foreach (Vector2[] line in _paths)
                for (int i = 0; i + 1 < line.Length; i++)
                {
                    Vector2 a = line[i], ab = line[i + 1] - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                    Vector2 q = a + ab * t;
                    float d = Vector2.Distance(p, q);
                    if (d < best)
                    {
                        best = d;
                        onPath = q;
                    }
                }
            return best;
        }

        /// <summary>Alle Wege, die das Relief beruecksichtigt (fuer Pruefungen).</summary>
        public static IReadOnlyList<Vector2[]> Paths
        {
            get
            {
                EnsureLists();
                return _paths;
            }
        }
    }
}
