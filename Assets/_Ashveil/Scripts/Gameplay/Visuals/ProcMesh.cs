using System.Collections.Generic;
using System.Globalization;
using Ashveil.Data;
using UnityEngine;

namespace Ashveil.Gameplay.Visuals
{
    /// <summary>
    /// Grundformen fuer Prototyp-Ausruestung (Kasten, Zylinder/Kegel/Bogenplatte, Kuppel, Kugel, Ring).
    /// Einheitsgroesse (Durchmesser/Hoehe 1), die Groesse kommt ueber die Skalierung. Jede Form wird einmal erzeugt
    /// und von allen Figuren geteilt.
    /// </summary>
    public static class ProcMesh
    {
        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        public static Mesh Get(RigidPart p)
        {
            // Wandstaerke ist absolut (m) – relativ zur Groesse umrechnen
            float th = p.thickness > 0f ? Mathf.Clamp01(p.thickness / Mathf.Max(0.001f, Mathf.Min(p.size.x, p.size.z) * 0.5f)) : 0f;
            float tube = p.shape == ProcShape.Torus ? p.size.y / Mathf.Max(0.001f, p.size.x) : 0f;
            string key = string.Format(CultureInfo.InvariantCulture, "{0}|{1:0.###}|{2:0.###}|{3:0}|{4:0}|{5:0.###}|{6:0.###}|{7}|{8}|{9:0.###}",
                p.shape, p.taperX, p.taperZ, p.arc, p.facing, th, p.cut, p.segments, p.capped, tube);
            if (Cache.TryGetValue(key, out Mesh m) && m != null) return m;
            var v = new List<Vector3>();
            var t = new List<int>();
            switch (p.shape)
            {
                case ProcShape.Box: Box(v, t, p.taperX, p.taperZ); break;
                case ProcShape.Cylinder: Cylinder(v, t, p.taperX, p.arc, p.facing, th, p.segments, p.capped); break;
                case ProcShape.Dome: Dome(v, t, p.cut, p.arc, p.facing, p.segments); break;
                case ProcShape.Sphere: Dome(v, t, 1f, 360f, 0f, p.segments); break;
                default: Torus(v, t, tube, p.segments); break;
            }
            m = new Mesh { name = "Proc " + key, hideFlags = HideFlags.DontSave };
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            Cache[key] = m;
            return m;
        }

        private static void Quad(List<int> t, int a, int b, int c, int d)
        {
            t.Add(a); t.Add(b); t.Add(c);
            t.Add(a); t.Add(c); t.Add(d);
        }

        private static void Box(List<Vector3> v, List<int> t, float tx, float tz)
        {
            // Eigene Ecken je Flaeche (harte Kanten)
            Vector3[] c =
            {
                new Vector3(-.5f, -.5f, -.5f), new Vector3(.5f, -.5f, -.5f), new Vector3(.5f, -.5f, .5f), new Vector3(-.5f, -.5f, .5f),
                new Vector3(-.5f * tx, .5f, -.5f * tz), new Vector3(.5f * tx, .5f, -.5f * tz), new Vector3(.5f * tx, .5f, .5f * tz), new Vector3(-.5f * tx, .5f, .5f * tz)
            };
            int[][] faces = { new[] { 0, 1, 2, 3 }, new[] { 7, 6, 5, 4 }, new[] { 4, 5, 1, 0 }, new[] { 5, 6, 2, 1 }, new[] { 6, 7, 3, 2 }, new[] { 7, 4, 0, 3 } };
            foreach (int[] f in faces)
            {
                int b = v.Count;
                foreach (int i in f) v.Add(c[i]);
                Quad(t, b, b + 1, b + 2, b + 3);
            }
        }

        /// <summary>Zylinder/Kegelstumpf; mit arc &lt; 360 und Wandstaerke eine gebogene Platte.</summary>
        private static void Cylinder(List<Vector3> v, List<int> t, float taper, float arcDeg, float facing, float th, int seg, bool capped)
        {
            bool full = arcDeg >= 359.9f;
            int n = Mathf.Max(3, seg);
            int m = full ? n : n + 1;
            float arc = arcDeg * Mathf.Deg2Rad;
            float a0 = (90f + facing - arcDeg * 0.5f) * Mathf.Deg2Rad;
            float[] radii = th > 0f ? new[] { 1f, 1f - th } : new[] { 1f };
            var ring = new int[radii.Length, 2];
            for (int li = 0; li < radii.Length; li++)
            for (int ri = 0; ri < 2; ri++)
            {
                ring[li, ri] = v.Count;
                float y = ri == 0 ? -0.5f : 0.5f;
                float sc = (ri == 0 ? 1f : taper) * radii[li];
                for (int i = 0; i < m; i++)
                {
                    float a = a0 + arc * i / n;
                    v.Add(new Vector3(Mathf.Cos(a) * 0.5f * sc, y, Mathf.Sin(a) * 0.5f * sc));
                }
            }
            for (int li = 0; li < radii.Length; li++)
            {
                int b0 = ring[li, 0], b1 = ring[li, 1];
                for (int i = 0; i < n; i++)
                {
                    int j = full ? (i + 1) % m : i + 1;
                    if (li == 0) Quad(t, b0 + j, b0 + i, b1 + i, b1 + j);
                    else Quad(t, b0 + i, b0 + j, b1 + j, b1 + i);
                }
            }
            if (th > 0f)
            {
                for (int ri = 0; ri < 2; ri++)
                {
                    int o = ring[0, ri], inn = ring[1, ri];
                    for (int i = 0; i < n; i++)
                    {
                        int j = full ? (i + 1) % m : i + 1;
                        if (ri == 0) Quad(t, o + i, o + j, inn + j, inn + i);
                        else Quad(t, o + j, o + i, inn + i, inn + j);
                    }
                }
                if (!full)
                    foreach (int e in new[] { 0, m - 1 })
                        Quad(t, ring[0, 0] + e, ring[1, 0] + e, ring[1, 1] + e, ring[0, 1] + e);
            }
            else if (capped && full)
            {
                for (int ri = 0; ri < 2; ri++)
                {
                    int c = v.Count;
                    v.Add(new Vector3(0f, ri == 0 ? -0.5f : 0.5f, 0f));
                    int o = ring[0, ri];
                    for (int i = 0; i < n; i++)
                    {
                        int j = (i + 1) % m;
                        if (ri == 0) { t.Add(c); t.Add(o + i); t.Add(o + j); }
                        else { t.Add(c); t.Add(o + j); t.Add(o + i); }
                    }
                }
            }
        }

        /// <summary>Kuppel (cut = Anteil der Kugelhoehe von oben, 1 = ganze Kugel).</summary>
        private static void Dome(List<Vector3> v, List<int> t, float cut, float arcDeg, float facing, int seg)
        {
            bool full = arcDeg >= 359.9f;
            int n = Mathf.Max(6, seg);
            int rows = Mathf.Max(4, n / 2);
            int m = full ? n : n + 1;
            float arc = arcDeg * Mathf.Deg2Rad;
            float a0 = (90f + facing - arcDeg * 0.5f) * Mathf.Deg2Rad;
            float tmax = Mathf.PI * Mathf.Clamp01(cut);
            int b = v.Count;
            for (int r = 0; r <= rows; r++)
            {
                float th = tmax * r / rows;
                for (int i = 0; i < m; i++)
                {
                    float a = a0 + arc * i / n;
                    v.Add(new Vector3(Mathf.Sin(th) * Mathf.Cos(a) * 0.5f, Mathf.Cos(th) * 0.5f, Mathf.Sin(th) * Mathf.Sin(a) * 0.5f));
                }
            }
            for (int r = 0; r < rows; r++)
            for (int i = 0; i < n; i++)
            {
                int j = full ? (i + 1) % m : i + 1;
                Quad(t, b + r * m + i, b + r * m + j, b + (r + 1) * m + j, b + (r + 1) * m + i);
            }
        }

        private static void Torus(List<Vector3> v, List<int> t, float tube, int seg)
        {
            int n = Mathf.Max(8, seg), k = 8;
            float r = Mathf.Clamp(tube, 0.01f, 0.9f) * 0.5f;
            float R = 0.5f - r;
            for (int i = 0; i < n; i++)
            {
                float a = 2f * Mathf.PI * i / n;
                for (int j = 0; j < k; j++)
                {
                    float c = 2f * Mathf.PI * j / k;
                    // Querschnitt in normierten Einheiten: Hoehe des Rings = 1 (wird mit size.y skaliert)
                    v.Add(new Vector3((R + r * Mathf.Cos(c)) * Mathf.Cos(a), 0.5f * Mathf.Sin(c), (R + r * Mathf.Cos(c)) * Mathf.Sin(a)));
                }
            }
            for (int i = 0; i < n; i++)
            for (int j = 0; j < k; j++)
                Quad(t, i * k + j, i * k + (j + 1) % k, ((i + 1) % n) * k + (j + 1) % k, ((i + 1) % n) * k + j);
        }
    }
}
