using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.EditorTools.Quality
{
    /// <summary>Schweregrad einer Weltprüfung (Werkzeug-Direktive Max 05.10., §17: INFO, WARNING, CRITICAL).</summary>
    public enum WqSeverity
    {
        Info = 0,
        Warning = 1,
        Critical = 2
    }

    /// <summary>
    /// Reine Rechenregeln des World Quality Validators – ohne Szene, ohne Assets, damit sie im Test Runner geprüft werden
    /// können. Die Szene-Prüfungen in <see cref="WorldQualityValidator"/> rufen nur diese Funktionen auf.
    /// </summary>
    public static class WorldQualityRules
    {
        /// <summary>Steigung in Prozent zwischen zwei Höhen auf horizontalem Abstand (Betrag).</summary>
        public static float GradePercent(float h0, float h1, float distance)
        {
            if (distance <= 0.0001f) return 0f;
            return Mathf.Abs(h1 - h0) / distance * 100f;
        }

        /// <summary>
        /// Größte mittlere Steigung (Prozent) über ein gleitendes Fenster entlang eines Profils mit festen Abständen.
        /// Ein Fenster von 8–10 m glättet einzelne Stufen im Höhenfeld, die man beim Laufen nicht als Steigung spürt.
        /// </summary>
        public static float WorstGrade(IReadOnlyList<float> heights, float step, float window, out int atIndex)
        {
            atIndex = -1;
            if (heights == null || heights.Count < 2 || step <= 0f) return 0f;
            int span = Mathf.Max(1, Mathf.RoundToInt(window / step));
            float worst = 0f;
            for (int i = 0; i + span < heights.Count; i++)
            {
                float g = GradePercent(heights[i], heights[i + span], span * step);
                if (g > worst)
                {
                    worst = g;
                    atIndex = i;
                }
            }
            if (heights.Count <= span)
            {
                worst = GradePercent(heights[0], heights[heights.Count - 1], (heights.Count - 1) * step);
                atIndex = 0;
            }
            return worst;
        }

        /// <summary>Schweregrad aus Grenzwerten. Grenze 0 oder kleiner = nicht festgelegt (nur Info, „Grenzen nicht erfinden“).</summary>
        public static WqSeverity Classify(double value, double warnAt, double criticalAt)
        {
            if (criticalAt > 0 && value >= criticalAt) return WqSeverity.Critical;
            if (warnAt > 0 && value >= warnAt) return WqSeverity.Warning;
            return WqSeverity.Info;
        }

        /// <summary>Schwebt ein Objekt? Unterkante über dem höchsten Bodenpunkt seiner Grundfläche plus Toleranz, und nichts trägt es.</summary>
        public static bool IsFloating(float boundsMinY, float groundMaxUnderFootprint, bool supported, float tolerance = 0.3f) =>
            !supported && boundsMinY > groundMaxUnderFootprint + tolerance;

        /// <summary>Liegt ein Objekt vollständig unter dem Gelände (Oberkante unter dem tiefsten Bodenpunkt seiner Grundfläche)?</summary>
        public static bool IsBuried(float boundsMaxY, float groundMinUnderFootprint, float tolerance = 0.05f) =>
            boundsMaxY < groundMinUnderFootprint - tolerance;

        /// <summary>Wörter eines Objektnamens in Kleinbuchstaben (Buchstabenfolgen; Ziffern, Leer- und Satzzeichen trennen).</summary>
        public static List<string> NameWords(string name)
        {
            var words = new List<string>();
            if (string.IsNullOrEmpty(name)) return words;
            int start = -1;
            string lower = name.ToLowerInvariant();
            for (int i = 0; i <= lower.Length; i++)
            {
                bool letter = i < lower.Length && char.IsLetter(lower[i]);
                if (letter && start < 0) start = i;
                if (!letter && start >= 0)
                {
                    words.Add(lower.Substring(start, i - start));
                    start = -1;
                }
            }
            return words;
        }

        /// <summary>
        /// Enthält der Name eines der Stichworte – als ganzes Wort oder als deutsche Zusammensetzung mit dem Stichwort am Ende
        /// („Holzsteg“ → steg, „Vordach“ → dach; aber „Lichtung“ ist kein Licht)? Wörter aus <paramref name="ignore"/>
        /// (Ortsnamen wie „Untersteg“) zählen nicht. Endungen zählen erst ab vier Buchstaben.
        /// </summary>
        public static bool NameHasKeyword(string name, IReadOnlyList<string> keys, IReadOnlyList<string> ignore = null)
        {
            if (keys == null) return false;
            foreach (string w in NameWords(name))
            {
                bool skip = false;
                if (ignore != null)
                    for (int i = 0; i < ignore.Count && !skip; i++) skip = ignore[i] == w;
                if (skip) continue;
                for (int i = 0; i < keys.Count; i++)
                {
                    string k = keys[i];
                    if (w == k || (k.Length >= 4 && w.EndsWith(k, StringComparison.Ordinal))) return true;
                }
            }
            return false;
        }

        /// <summary>Kachel eines Punktes in einem Raster (x/z); negative Koordinaten werden abgerundet.</summary>
        public static Vector2Int TileKey(Vector3 p, float size) =>
            new Vector2Int(Mathf.FloorToInt(p.x / size), Mathf.FloorToInt(p.z / size));

        /// <summary>Abstand eines Punktes zu einer Strecke von a nach b (x/z).</summary>
        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + t * ab);
        }

        /// <summary>Horizontaler Abstand eines Punktes zu einer Linie (x/z), die als Punktfolge gegeben ist.</summary>
        public static float DistanceToPolyline(Vector2 p, IReadOnlyList<Vector2> line)
        {
            if (line == null || line.Count == 0) return float.MaxValue;
            if (line.Count == 1) return Vector2.Distance(p, line[0]);
            float best = float.MaxValue;
            for (int i = 0; i + 1 < line.Count; i++) best = Mathf.Min(best, DistanceToSegment(p, line[i], line[i + 1]));
            return best;
        }

        /// <summary>
        /// Punkte entlang einer Linie in genau gleichem Abstand (Bogenlänge), beginnend beim ersten Punkt. Der Rest am Ende,
        /// der kürzer als ein Schritt ist, entfällt – so stimmt der feste Abstand für <see cref="WorstGrade"/>.
        /// </summary>
        public static List<Vector2> Resample(IReadOnlyList<Vector2> line, float step)
        {
            var result = new List<Vector2>();
            if (line == null || line.Count == 0 || step <= 0f) return result;
            result.Add(line[0]);
            float carry = 0f;   // Weg seit dem letzten gesetzten Punkt
            for (int i = 0; i + 1 < line.Count; i++)
            {
                Vector2 a = line[i], b = line[i + 1];
                float len = Vector2.Distance(a, b);
                if (len < 1e-6f) continue;
                float pos = step - carry;
                while (pos <= len + 1e-5f)
                {
                    result.Add(Vector2.Lerp(a, b, Mathf.Min(pos, len) / len));
                    pos += step;
                }
                carry = len - (pos - step);
            }
            return result;
        }

        /// <summary>Eine zusammenhängende Fläche des NavMesh.</summary>
        public struct NavIsland
        {
            public int Triangles;
            public float Area;
            public Vector3 Sample;
        }

        /// <summary>
        /// Zusammenhängende Flächen eines NavMesh aus seiner Dreieckszerlegung (NavMesh.CalculateTriangulation).
        /// Eckpunkte gleicher Lage (auf <paramref name="weld"/> m gerundet) gelten als verbunden. Ergebnis nach Fläche absteigend;
        /// <paramref name="triangleIsland"/> nennt je Dreieck den Index der Fläche im Ergebnis.
        /// </summary>
        public static List<NavIsland> NavIslands(Vector3[] vertices, int[] indices, float weld, out int[] triangleIsland)
        {
            int triCount = indices == null ? 0 : indices.Length / 3;
            triangleIsland = new int[triCount];
            var result = new List<NavIsland>();
            if (triCount == 0 || vertices == null) return result;

            // Eckpunkte verschweißen
            var key = new Dictionary<Vector3Int, int>();
            var weldedOf = new int[vertices.Length];
            float inv = 1f / Mathf.Max(weld, 1e-4f);
            for (int i = 0; i < vertices.Length; i++)
            {
                var k = new Vector3Int(Mathf.RoundToInt(vertices[i].x * inv), Mathf.RoundToInt(vertices[i].y * inv), Mathf.RoundToInt(vertices[i].z * inv));
                if (!key.TryGetValue(k, out int id))
                {
                    id = key.Count;
                    key[k] = id;
                }
                weldedOf[i] = id;
            }

            // Union-Find über verschweißte Eckpunkte
            var parent = new int[key.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }
            void Union(int a, int b)
            {
                a = Find(a);
                b = Find(b);
                if (a != b) parent[b] = a;
            }
            for (int t = 0; t < triCount; t++)
            {
                int a = weldedOf[indices[t * 3]], b = weldedOf[indices[t * 3 + 1]], c = weldedOf[indices[t * 3 + 2]];
                Union(a, b);
                Union(a, c);
            }

            var rootIsland = new Dictionary<int, int>();
            var areas = new List<float>();
            var counts = new List<int>();
            var samples = new List<Vector3>();
            for (int t = 0; t < triCount; t++)
            {
                int root = Find(weldedOf[indices[t * 3]]);
                if (!rootIsland.TryGetValue(root, out int island))
                {
                    island = areas.Count;
                    rootIsland[root] = island;
                    areas.Add(0f);
                    counts.Add(0);
                    samples.Add(vertices[indices[t * 3]]);
                }
                Vector3 p0 = vertices[indices[t * 3]], p1 = vertices[indices[t * 3 + 1]], p2 = vertices[indices[t * 3 + 2]];
                areas[island] += Vector3.Cross(p1 - p0, p2 - p0).magnitude * 0.5f;
                counts[island]++;
                triangleIsland[t] = island;
            }

            // nach Fläche sortieren und Zuordnung umschreiben
            var order = new List<int>();
            for (int i = 0; i < areas.Count; i++) order.Add(i);
            order.Sort((x, y) => areas[y].CompareTo(areas[x]));
            var remap = new int[order.Count];
            for (int i = 0; i < order.Count; i++)
            {
                remap[order[i]] = i;
                result.Add(new NavIsland { Area = areas[order[i]], Triangles = counts[order[i]], Sample = samples[order[i]] });
            }
            for (int t = 0; t < triCount; t++) triangleIsland[t] = remap[triangleIsland[t]];
            return result;
        }

        /// <summary>Abschnitt eines Wasserrandes, an dem das Gelände tiefer als der Wasserspiegel liegt.</summary>
        public struct OpenEdge
        {
            public Vector3 From, To;
            public float Length, Deepest;
        }

        /// <summary>
        /// Prüft die vier Ränder einer rechteckigen Wasserfläche (x/z, Spiegel auf <paramref name="waterY"/>): Wo das Gelände
        /// am Rand unter dem Spiegel liegt, endet das Wasser sichtbar in der Luft oder läuft aus der Fläche (Schwimmen,
        /// Lecks). Abschnitte kürzer als <paramref name="minLength"/> zählen nicht. Punkte ohne Gelände werden übersprungen.
        /// </summary>
        public static List<OpenEdge> OpenWaterEdges(Rect area, float waterY, Func<float, float, float?> ground, float step, float minLength, float tolerance = 0.05f)
        {
            var result = new List<OpenEdge>();
            Vector2[] corners =
            {
                new Vector2(area.xMin, area.yMin), new Vector2(area.xMax, area.yMin),
                new Vector2(area.xMax, area.yMax), new Vector2(area.xMin, area.yMax)
            };
            for (int e = 0; e < 4; e++)
            {
                Vector2 a = corners[e], b = corners[(e + 1) % 4];
                float len = Vector2.Distance(a, b);
                int n = Mathf.Max(1, Mathf.CeilToInt(len / step));
                bool open = false;
                Vector2 start = a;
                float deepest = 0f;
                for (int i = 0; i <= n; i++)
                {
                    Vector2 p = Vector2.Lerp(a, b, i / (float)n);
                    float? g = ground(p.x, p.y);
                    bool below = g.HasValue && g.Value < waterY - tolerance;
                    if (below && !open)
                    {
                        open = true;
                        start = p;
                        deepest = 0f;
                    }
                    if (below) deepest = Mathf.Max(deepest, waterY - g.Value);
                    bool last = i == n;
                    if (open && (!below || last))
                    {
                        Vector2 end = below ? p : Vector2.Lerp(a, b, (i - 1) / (float)n);
                        float l = Vector2.Distance(start, end);
                        if (l >= minLength)
                            result.Add(new OpenEdge { From = new Vector3(start.x, waterY, start.y), To = new Vector3(end.x, waterY, end.y), Length = l, Deepest = deepest });
                        open = false;
                    }
                }
            }
            return result;
        }

        /// <summary>Speicher eines Netzes auf der Grafikkarte, grob: Eckpunkte × Schrittweite + Indizes.</summary>
        public static long MeshBytesEstimate(int vertexCount, int vertexStride, long indexCount, bool index32) =>
            (long)vertexCount * Mathf.Max(vertexStride, 12) + indexCount * (index32 ? 4L : 2L);
    }
}
