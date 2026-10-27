using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Holzbau-Baukasten der Westweiden (Claude 14.10.2026, Lückenliste Punkt 6 „Zäune, Brücken“, VORSCHLAG):
    /// gespaltene Stangen, Pfosten und Bohlen als Prismen, alle Teile eines Bauwerks in EIN Mesh (ein Zaun = ein Objekt statt
    /// 30–90 Würfel). Textur: das Eichenband des Trim-Sheets der Häuser (WW_Trim_Probe, v 0,60–0,76, Maserung längs u),
    /// also dasselbe verwitterte Holz wie Schwellen und Fachwerk – kein neues Material, keine neue Textur.
    /// </summary>
    public static class HolzBau
    {
        public const string TrimPfad = ProjectSetup.Root + "/Art/Westweiden/Proben/WW_Trim_Probe.mat";
        // Eichenband im Trim-Sheet (uv.y = 1 − v): v 0,60–0,76 → y 0,24–0,40; etwas Rand weglassen (keine Nachbarbänder)
        private const float Y0 = 0.255f, Y1 = 0.385f;

        public static Material Holz => AssetDatabase.LoadAssetAtPath<Material>(TrimPfad);

        public sealed class Netz
        {
            private readonly List<Vector3> v = new List<Vector3>();
            private readonly List<Vector3> n = new List<Vector3>();
            private readonly List<Vector2> uv = new List<Vector2>();
            private readonly List<int> tri = new List<int>();
            private readonly Transform _bezug;
            private readonly System.Random _rng;
            public int Teile { get; private set; }

            /// <summary>bezug = Objekt, in dessen lokalen Raum gebaut wird (Weltpunkte werden umgerechnet).</summary>
            public Netz(Transform bezug, int saat) { _bezug = bezug; _rng = new System.Random(saat); }

            public float Zufall(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

            /// <summary>
            /// Prisma von a nach b (Weltpunkte). seiten 4 = Bohle/Balken (breite × hoehe), 5–6 = gespaltene Stange / Pfosten
            /// (Durchmesser = breite, hoehe = Stauchung). hoch = Richtung, die „oben“ am Querschnitt ist.
            /// </summary>
            public void Prisma(Vector3 a, Vector3 b, float breite, float hoehe, int seiten, Vector3 hoch, bool kappen = true)
            {
                a = _bezug.InverseTransformPoint(a); b = _bezug.InverseTransformPoint(b);
                hoch = _bezug.InverseTransformDirection(hoch);
                Vector3 l = b - a; float len = l.magnitude;
                if (len < 1e-3f) return;
                Vector3 f = l / len;
                Vector3 up = Vector3.ProjectOnPlane(hoch, f).normalized;
                if (up.sqrMagnitude < 0.5f) up = Vector3.ProjectOnPlane(Mathf.Abs(f.y) > 0.9f ? Vector3.forward : Vector3.up, f).normalized;
                Vector3 side = Vector3.Cross(up, f);
                float u0 = Zufall(0f, 8f);                       // jede Stange zeigt ein anderes Stück Maserung
                float drehung = seiten == 4 ? Mathf.PI * 0.25f : Zufall(0f, Mathf.PI);
                var ring = new Vector3[seiten];
                for (int s = 0; s < seiten; s++)
                {
                    float w = drehung + s * Mathf.PI * 2f / seiten;
                    float rau = seiten == 4 ? 1f : Zufall(0.88f, 1.08f);   // gespaltenes Holz ist nicht rund
                    ring[s] = side * (Mathf.Cos(w) * breite * 0.5f * rau) + up * (Mathf.Sin(w) * hoehe * 0.5f * rau);
                }
                if (seiten == 4)   // exakter Kasten
                {
                    ring[0] = side * (breite * 0.5f) + up * (hoehe * 0.5f); ring[1] = -side * (breite * 0.5f) + up * (hoehe * 0.5f);
                    ring[2] = -side * (breite * 0.5f) - up * (hoehe * 0.5f); ring[3] = side * (breite * 0.5f) - up * (hoehe * 0.5f);
                }
                float uLen = len / 2f;   // u = Meter / 2 wie Häuser und Mühle
                for (int s = 0; s < seiten; s++)
                {
                    Vector3 p0 = ring[s], p1 = ring[(s + 1) % seiten];
                    Vector3 nn = Vector3.Cross(p1 - p0, f).normalized;
                    if (Vector3.Dot(nn, (p0 + p1) * 0.5f) < 0f) nn = -nn;
                    // jede Fläche nimmt einen anderen Streifen des Bandes (Maserung wechselt um die Stange)
                    float yy0 = Mathf.Lerp(Y0, Y1, (s % 3) / 3f), yy1 = Mathf.Lerp(Y0, Y1, (s % 3 + 1) / 3f);
                    int i = v.Count;
                    v.Add(a + p0); v.Add(a + p1); v.Add(b + p1); v.Add(b + p0);
                    n.Add(nn); n.Add(nn); n.Add(nn); n.Add(nn);
                    uv.Add(new Vector2(u0, yy0)); uv.Add(new Vector2(u0, yy1)); uv.Add(new Vector2(u0 + uLen, yy1)); uv.Add(new Vector2(u0 + uLen, yy0));
                    Quad(i, nn, a + p0, a + p1, b + p1);
                }
                if (kappen)
                {
                    Kappe(a, ring, -f);
                    Kappe(b, ring, f);
                }
                Teile++;
            }

            private void Kappe(Vector3 m, Vector3[] ring, Vector3 nn)
            {
                int i0 = v.Count;
                float um = Zufall(0f, 6f);
                v.Add(m); n.Add(nn); uv.Add(new Vector2(um + 0.06f, (Y0 + Y1) * 0.5f));
                for (int s = 0; s < ring.Length; s++)
                {
                    v.Add(m + ring[s]); n.Add(nn);
                    uv.Add(new Vector2(um + 0.06f + ring[s].x * 0.05f, Mathf.Lerp(Y0, Y1, 0.5f + 0.4f * Mathf.Sin(s))));
                }
                for (int s = 0; s < ring.Length; s++)
                {
                    int a = i0 + 1 + s, b = i0 + 1 + (s + 1) % ring.Length;
                    if (Vector3.Dot(Vector3.Cross(v[a] - v[i0], v[b] - v[i0]), nn) > 0f) tri.AddRange(new[] { i0, b, a });
                    else tri.AddRange(new[] { i0, a, b });
                }
            }

            // Unity: Dreiecke im Uhrzeigersinn von der sichtbaren Seite
            private void Quad(int i, Vector3 nn, Vector3 p0, Vector3 p1, Vector3 p2)
            {
                if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), nn) > 0f) tri.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
                else tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }

            /// <summary>Mesh an ein neues Kind von <c>bezug</c> hängen (ohne Kollider). Null, wenn nichts gebaut wurde.</summary>
            public GameObject Fertig(string name, Material m)
            {
                if (Teile == 0) return null;
                var mesh = new Mesh { name = name };
                if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
                mesh.RecalculateTangents(); mesh.RecalculateBounds();
                var g = new GameObject(name);
                g.transform.SetParent(_bezug, false);
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                g.AddComponent<MeshRenderer>().sharedMaterial = m;
                return g;
            }
        }
    }
}
