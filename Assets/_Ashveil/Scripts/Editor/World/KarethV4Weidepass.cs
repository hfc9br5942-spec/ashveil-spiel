using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Westweiden spielbar (Lückenliste Punkt 1, Max 12.10.2026 „ok“): Kareth v4 und die Westweiden-Zelle als eine Welt
    /// (WORLD-120: keine Ladebildschirme zwischen Nachbarregionen – additive Weltzelle, Large-World-Architektur §2).
    ///
    /// Lage: Ausgang an Ausgang. Der Weidensteig verlässt Kareth am Weidegatter (−2078 / −1601) nach Westen; der Weidepass der
    /// Westweiden liegt dort lokal bei (4200 / 1540). Westweiden-Ursprung damit bei Kareth (−6450 / −3140), Höhe −175 m
    /// (die Passhöhe der Westweiden, lokal 155 m, liegt so auf −20 m – knapp über dem Salzseeufer am Gatter, −25 m).
    ///
    /// In Kareth (nur bei eingeschaltetem Anschluss, Menü-Schalter, Standard an – Welle-0-Stand bleibt per Schalter erreichbar):
    ///   • Klamm durch den Randkamm: 16 m breite Sohle von −25 m am Gatter auf −20 m an der Kante, Wandfuß auf 30 m gerundet, dann 1 : 1,4 (≈ 54°),
    ///     Sohle als Weg, Wände als Fels; Bäume und Felsen in der Klamm entfallen.
    ///   • Randspalte x = −2250 zwischen den Knoten des Westweiden-Rasters linear (Naht ohne Sprung trotz 2 m / 3,3 m Raster).
    ///   • Weidegatter offen (Torflügel zur Seite geschwungen), kein Hangrutsch.
    /// Die Westweiden-Zelle (<see cref="WestweidenZelle"/>) rechnet ihre Ostkante aus derselben Kante (<see cref="KnotenKante"/>).
    /// </summary>
    public static class KarethV4Weidepass
    {
        public const string Key = "Ashveil.KarethV4.Westweiden";
        private const string MenuPfad = "Ashveil/Welt/Kareth v4: Westweiden anschließen (Weidepass)";

        // ---- Lage der Westweiden-Zelle im Kareth-Raum
        public const float WwX0 = -6450f, WwZ0 = -3140f, WwDy = -175f;
        public const float WwSX = 4200f, WwSZ = 3400f, WwSY = 600f, WwY0 = -50f;
        public const int WwRes = 1025;
        public static float WwDz => WwSZ / (WwRes - 1);
        public static Vector3 WwUrsprung => new Vector3(WwX0, WwDy, WwZ0);

        // ---- Klamm
        public const float KanteX = -2250f;
        private static readonly Vector2 A = new Vector2(-2078f, -1601f);   // Weidegatter (Schwelle im Plan)
        private static readonly Vector2 B = new Vector2(-2262f, -1600f);   // 12 m jenseits der Kante
        private const float SohleGatter = -25f, SohleKante = -20f, Halb = 8f, Wand = 1.4f, Rundung = 30f;

        /// <summary>Schalter (Standard an). Aus = Welle-0-Stand: Weidegatter zu, Hangrutsch, keine Klamm, kein Streamer.</summary>
        public static bool Schalter
        {
            get => EditorPrefs.GetBool(Key, true);
            set => EditorPrefs.SetBool(Key, value);
        }

        /// <summary>Anschluss wirksam: Schalter an und die Zellen-Szene gebaut (sonst führte die Klamm ins Leere).</summary>
        public static bool Aktiv => Schalter && File.Exists(WestweidenZelle.ScenePath);

        [MenuItem(MenuPfad, priority = 568)]
        private static void Umschalten()
        {
            Schalter = !Schalter;
            Debug.Log("[Ashveil] Kareth v4: Westweiden anschließen " + (Schalter ? "an" : "aus") + " (gilt beim nächsten „Szene bauen“).");
        }

        [MenuItem(MenuPfad, true)]
        private static bool UmschaltenPruefen()
        {
            Menu.SetChecked(MenuPfad, Schalter);
            return true;
        }

        // ================================================================== Klamm

        /// <summary>Höhe der Klamm-Fläche (Welt-y) an (x, z), +∞ außerhalb (vor dem Gatter keine Kerbe).</summary>
        public static float Kerbhoehe(float x, float z, out float d)
        {
            Vector2 ab = B - A, ap = new Vector2(x, z) - A;
            float t = Vector2.Dot(ap, ab) / ab.sqrMagnitude;
            if (t <= 0f) { d = float.MaxValue; return float.PositiveInfinity; }
            t = Mathf.Min(t, 1f);
            Vector2 q = A + ab * t;
            d = Vector2.Distance(q, new Vector2(x, z));
            float u = Mathf.Clamp01((A.x - q.x) / (A.x - KanteX));
            float sohle = Mathf.Lerp(SohleGatter, SohleKante, u);
            // Wandfuß als Rundung (Radius 30 m, Neigung wächst von 0 auf 1 : 1,4): keine Knickkante, die zwei verschiedene Raster an
            // der Naht unterschiedlich abbilden würden (Lauf op_461), und eine Sohle, die wie ein ausgewaschener Hohlweg wirkt.
            float e = Mathf.Max(0f, d - Halb);
            float w = e < Rundung ? e * e / (2f * Rundung) : e - Rundung * 0.5f;
            return sohle + w * Wand;
        }

        // Kerbtiefe je Höhenpunkt im Arbeitsfenster (für Bodenschicht, Bäume, Felsen)
        private static float[,] _tiefe;
        private static int _ti0, _tj0;
        private static float _tCell, _tX0, _tZ0;

        /// <summary>Klamm in die Höhen (normiert, [z, x]) schneiden und die Randspalte auf das Westweiden-Raster legen.</summary>
        public static string Kerbe(KarethV4Plan p, float[,] heights)
        {
            int n = p.res;
            float cell = p.size / (n - 1);
            if (Mathf.Abs(p.x0 - KanteX) > 0.01f) return "Kareth-Westkante liegt nicht bei x = −2250 – keine Klamm";
            int i1 = Mathf.Clamp(Mathf.CeilToInt((A.x + 2f - p.x0) / cell), 0, n - 1);
            // Fenster: die Wände (1 : 1,4) reichen bei 640 m Höhenumfang bis ≈ 470 m von der Achse – Fenster ± 480 m, sonst bricht
            // die Kerbe am Fensterrand als Stufe ab (Lauf op_450: 46 m Stufe bei z −1380)
            float reichweite = Halb + Rundung * 0.5f + (p.baseY + p.rangeY - SohleKante) / Wand + 4f;
            int j0 = Mathf.Clamp(Mathf.FloorToInt((A.y - reichweite - p.z0) / cell), 0, n - 1);
            int j1 = Mathf.Clamp(Mathf.CeilToInt((A.y + reichweite - p.z0) / cell), 0, n - 1);
            _tiefe = new float[j1 - j0 + 1, i1 + 1];
            _ti0 = 0; _tj0 = j0; _tCell = cell; _tX0 = p.x0; _tZ0 = p.z0;
            int geschnitten = 0;
            float maxTiefe = 0f;
            for (int j = j0; j <= j1; j++)
                for (int i = 0; i <= i1; i++)
                {
                    float x = p.x0 + i * cell, z = p.z0 + j * cell;
                    float k = Kerbhoehe(x, z, out _);
                    float h = heights[j, i] * p.rangeY + p.baseY;
                    if (k >= h) continue;
                    heights[j, i] = Mathf.Clamp01((k - p.baseY) / p.rangeY);
                    _tiefe[j - j0, i] = h - k;
                    maxTiefe = Mathf.Max(maxTiefe, h - k);
                    geschnitten++;
                }
            // Randspalte: zwischen den Knoten des Westweiden-Rasters linear (beide Kacheln beschreiben dieselbe Kante)
            float[] knoten = KnotenKante(p, heights);
            // Kante glätten: Die Raster (2 m Kareth, 3,32 m Westweiden) treffen sich nur an Knoten; eine
            // scharfe Spitze zwischen zwei Kareth-Zeilen ließ sich nicht abbilden (Lauf op_457: 1,25 m Sprung auf dem Grat bei z −1925).
            // 64 Durchgänge (σ ≈ 19 m) auf dem Grat; in der Klamm (± 140 m um die Achse, Übergang 80 m) bleibt das gerundete
            // Profil selbst – sonst höbe die Glättung die Sohle an der Naht an. Rest-Sprung nach Rechnung ≤ 8 cm.
            float[] roh = (float[])knoten.Clone();
            for (int pass = 0; pass < 64; pass++)
            {
                float vorher = knoten[0];
                for (int k = 1; k < knoten.Length - 1; k++)
                {
                    float jetzt = knoten[k];
                    knoten[k] = 0.25f * vorher + 0.5f * jetzt + 0.25f * knoten[k + 1];
                    vorher = jetzt;
                }
            }
            for (int k = 0; k < knoten.Length; k++)
            {
                float zk = WwZ0 + k * WwDz;
                float wGlatt = Mathf.Clamp01((Mathf.Abs(zk - A.y) - 140f) / 80f);
                knoten[k] = Mathf.Lerp(roh[k], knoten[k], wGlatt);
            }
            LetzteKnoten = knoten;   // die Westweiden-Naht nimmt genau diese Werte (nicht neu aus der linearisierten Spalte abgetastet)
            int spalte = 0;
            for (int j = 0; j < n; j++)
            {
                float z = p.z0 + j * cell;
                if (z < WwZ0 || z > WwZ0 + WwSZ) continue;
                float f = (z - WwZ0) / WwDz;
                int k0 = Mathf.Clamp(Mathf.FloorToInt(f), 0, WwRes - 2);
                float v = Mathf.Lerp(knoten[k0], knoten[k0 + 1], f - k0);
                heights[j, 0] = Mathf.Clamp01((v - p.baseY) / p.rangeY);
                spalte++;
            }
            return $"Klamm am Weidepass: {geschnitten} Höhenpunkte, bis {maxTiefe:0} m tief; Randspalte {spalte} Punkte auf das Westweiden-Raster";
        }

        /// <summary>Knotenwerte der Kante aus dem letzten <see cref="Kerbe"/>-Lauf (vor dem Linearisieren). Lauf op_454: neu abgetastet
        /// wichen Spitzen zwischen zwei Knoten um bis zu 1,2 m ab.</summary>
        public static float[] LetzteKnoten { get; private set; }

        /// <summary>Welt-Höhe der Kareth-Westkante (x = −2250) an den Knoten des Westweiden-Rasters (iz = 0 … 1024; außerhalb von
        /// Kareth der nächste Randwert).</summary>
        public static float[] KnotenKante(KarethV4Plan p, float[,] heights)
        {
            int n = p.res;
            float cell = p.size / (n - 1);
            var r = new float[WwRes];
            for (int iz = 0; iz < WwRes; iz++)
            {
                float z = WwZ0 + iz * WwDz;
                float f = Mathf.Clamp((z - p.z0) / cell, 0f, n - 1);
                int j = Mathf.Min(Mathf.FloorToInt(f), n - 2);
                r[iz] = Mathf.Lerp(heights[j, 0], heights[j + 1, 0], f - j) * p.rangeY + p.baseY;
            }
            return r;
        }

        /// <summary>Kareth-Höhen (normiert) aus der Rohdatei – mit Klamm, wie sie der Kareth-Bau schneidet (für die Westweiden-Naht).</summary>
        public static float[,] KarethMitKlamm(KarethV4Plan p, out string meldung)
        {
            byte[] raw = File.ReadAllBytes(KarethV4Builder.DataFolder + "/KarethV4_Hoehe.raw");
            int n = p.res;
            var h = new float[n, n];
            for (int z = 0, i = 0; z < n; z++)
                for (int x = 0; x < n; x++, i += 2)
                    h[z, x] = (raw[i] | (raw[i + 1] << 8)) / 65535f;
            meldung = Kerbe(p, h);
            return h;
        }

        private static float TiefeAn(float x, float z)
        {
            if (_tiefe == null) return 0f;
            int i = Mathf.RoundToInt((x - _tX0) / _tCell) - _ti0, j = Mathf.RoundToInt((z - _tZ0) / _tCell) - _tj0;
            if (i < 0 || j < 0 || j >= _tiefe.GetLength(0) || i >= _tiefe.GetLength(1)) return 0f;
            return _tiefe[j, i];
        }

        /// <summary>Bodenschicht (Rohdaten, je Zelle 7 Bytes: Gras, Waldboden, Moor, Asche, Fels, Weg, Salz): Sohle Weg, Wände Fels.</summary>
        public static int Splat(KarethV4Plan p, byte[] sb)
        {
            if (_tiefe == null) return 0;
            int n = p.splatRes, geaendert = 0;
            float zm = p.size / n;
            for (int z = 0; z < n; z++)
            {
                float wz = p.z0 + (z + 0.5f) * zm;
                if (wz < A.y - 490f || wz > A.y + 490f) continue;
                for (int x = 0; x < n; x++)
                {
                    float wx = p.x0 + (x + 0.5f) * zm;
                    if (wx > A.x + 4f) break;
                    float k = Kerbhoehe(wx, wz, out float d);
                    if (float.IsInfinity(k)) continue;
                    bool sohle = d <= Halb + 1.5f;
                    bool wand = !sohle && TiefeAn(wx, wz) > 0.4f;
                    if (!sohle && !wand) continue;
                    int o = (z * n + x) * 7;
                    for (int c = 0; c < 7; c++) sb[o + c] = 0;
                    sb[o + (sohle ? 5 : 4)] = 255;
                    geaendert++;
                }
            }
            return geaendert;
        }

        /// <summary>Bäume und Felsen, die in der Klamm stünden (Sohle oder geschnittene Wand), aus dem Plan nehmen.</summary>
        public static string PlanRaeumen(KarethV4Plan p)
        {
            bool Weg(float x, float z) { Kerbhoehe(x, z, out float d); return d <= Halb + 3f || TiefeAn(x, z) > 0.3f; }
            int b = 0, f = 0;
            if (p.baeume != null)
            {
                var l = new List<float>(p.baeume.Length);
                for (int i = 0; i + 4 < p.baeume.Length; i += 5)
                {
                    if (Weg(p.baeume[i], p.baeume[i + 1])) { b++; continue; }
                    for (int k = 0; k < 5; k++) l.Add(p.baeume[i + k]);
                }
                p.baeume = l.ToArray();
            }
            if (p.felsenV2 != null)
            {
                var l = new List<float>(p.felsenV2.Length);
                for (int i = 0; i + 11 < p.felsenV2.Length; i += 12)
                {
                    if (Weg(p.felsenV2[i], p.felsenV2[i + 1])) { f++; continue; }
                    for (int k = 0; k < 12; k++) l.Add(p.felsenV2[i + k]);
                }
                p.felsenV2 = l.ToArray();
            }
            return $"aus der Klamm genommen: {b} Bäume, {f} Felsen";
        }

        // ================================================================== Offenes Weidegatter

        /// <summary>Weidegatter offen: Pfosten, der Torflügel liegt zur Talseite geschwungen am Hang; ein Wegweiser nach Westen.</summary>
        public static void OffenesGatter(Transform g, float w, Material holz, Material holzAlt, System.Func<Transform, float, float, float> boden)
        {
            float half = w * 0.5f + 0.8f;
            foreach (float side in new[] { -1f, 1f })
            {
                float y0 = boden(g, side * half, 0f);
                K.Prim(PrimitiveType.Cylinder, "Gatterpfosten", g, new Vector3(side * half, y0 + 1.0f, 0f), new Vector3(0.24f, 1.3f, 0.24f), Vector3.zero, holz);
            }
            // Flügel um den linken Pfosten 105° aufgeschwungen (liegt an der Wegseite, nicht im Weg)
            Transform fluegel = K.Group("Torflügel (offen)", g, new Vector3(-half, boden(g, -half, 0f), 0f), new Vector3(0f, -105f, 0f));
            for (int i = 0; i < 5; i++)
                K.Prim(PrimitiveType.Cube, "Gatterlatte", fluegel, new Vector3(half, 0.35f + i * 0.4f, 0f), new Vector3(half * 2f, 0.1f, 0.06f), Vector3.zero, holz);
            K.Prim(PrimitiveType.Cube, "Strebe", fluegel, new Vector3(half, 1.15f, 0f), new Vector3(half * 2.1f, 0.1f, 0.06f), new Vector3(0f, 0f, 24f), holzAlt);
            // Wegweiser (Tafel ohne Schrift – Namen gibt Max)
            float sx = half + 1.4f, sz = -2.5f, sy = boden(g, sx, sz);
            Transform s = K.Group("Wegweiser Weidepass", g, new Vector3(sx, sy, sz), new Vector3(0f, 0f, 0f));
            K.Prim(PrimitiveType.Cube, "Pfahl", s, new Vector3(0f, 1.1f, 0f), new Vector3(0.14f, 2.2f, 0.14f), Vector3.zero, holzAlt);
            K.Prim(PrimitiveType.Cube, "Arm nach Westen", s, new Vector3(0f, 1.85f, 0.45f), new Vector3(0.08f, 0.3f, 0.9f), Vector3.zero, holz, false);
        }
    }
}
