using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Felsbaukasten (05.10., Arbeitsregel „Sinn und Gelände“): behauene, kantige Felsen ohne Fremdmodelle.
    ///
    /// Jede Form ist ein konvexer Körper: ein Quader, von zufälligen Ebenen beschnitten (Bruchflächen). Die Formen sind:
    /// 0 Block · 1 Platte (Schichtplatte) · 2 Säule (Kluftwand) · 3 Kiesel (gerundet: Bachstein, Findling) · 4 Wand (Wandplatte,
    /// Rückseite glatt im Hang). Je Form gibt es 12 Varianten. Die Maße kommen je Fels aus den Daten.
    /// Platziert wird nach Geologie (claude/kareth_v4/integration/geologie.py): Wände an Steilstellen, Schutt am Wandfuß,
    /// Schichtköpfe auf Rücken, Bachsteine im Bett, wenige Findlinge.
    /// Zusammengefasst wird je 64-m-Kachel zu einem Mesh mit zwei Materialien (Fels, Moos) und Kollision (CORE-005:
    /// wenige Renderer). UVs sind Weltkoordinaten in Metern (Box-Projektion); das Granit-Material T11 kachelt selbst (2 m).
    /// Moos wächst oben und auf der Schattenseite (NO, Hauptwind/Sonne aus SW), in großer Höhe kaum.
    ///
    /// Seit 05.10. (Felssatz, Max: Regelstopp-Ausnahme): Liegt der Felssatz aus Blender vor (FelssatzPipeline, 5 Formen ×
    /// 6 Varianten × 3 Detailstufen), nimmt der Bau dessen Netze statt der Polyeder. Je 128-m-Kachel entstehen dann drei
    /// Detailstufen (LODGroup) und ein eigenes Kollisionsnetz; je Fels wird pro Kachelstufe die Netzstufe gewählt, die bei
    /// seiner größten möglichen Bildhöhe reicht (mit dem LOD-Bias der Qualitätsstufe). Die Netze liegen binär in KarethV4_Felsen.asset (FelsKachelSatz).
    /// Fehlt der Satz oder ist er abgeschaltet (Menü Ashveil › Grafik › Felssatz in Kareth benutzen), gilt der alte Weg.
    /// </summary>
    public static class FelsKit
    {
        public const int Formen = 5, Varianten = 12;
        private static readonly string[] FormNamen = { "Block", "Platte", "Säule", "Kiesel", "Wand" };
        public static string FormName(int form) => form >= 0 && form < FormNamen.Length ? FormNamen[form] : "Form " + form;
        private const float Kachel = 128f;        // 05.10.: 128 m statt 64 m (1118 → ≈ 300 Renderer; CORE-005)
        private const string AssetPfad = ProjectSetup.Root + "/Terrain/KarethV4_Felsen.asset";
        private const string GranitPfad = ProjectSetup.Root + "/Art/Welt/Materialien/Materialien/W_T11_Granit.mat";
        private const string MoosPfad = ProjectSetup.Root + "/Art/Welt/Materialien/Materialien/W_KV4_Fels_Moos.mat";
        private const string BehauenPfad = ProjectSetup.Root + "/Art/Welt/Materialien/Materialien/W_KV4_Fels_behauen.mat";

        private struct Koerper { public List<Vector3> V; public List<Vector3> N; public List<int[]> F; }

        // ------------------------------------------------------------------ Formen
        private static List<List<Vector3>> Quader()
        {
            Vector3[] c = { new Vector3(-.5f, -.5f, -.5f), new Vector3(.5f, -.5f, -.5f), new Vector3(.5f, .5f, -.5f), new Vector3(-.5f, .5f, -.5f),
                            new Vector3(-.5f, -.5f, .5f), new Vector3(.5f, -.5f, .5f), new Vector3(.5f, .5f, .5f), new Vector3(-.5f, .5f, .5f) };
            int[][] f = { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 }, new[] { 1, 2, 6, 5 }, new[] { 3, 0, 4, 7 } };
            var faces = new List<List<Vector3>>();
            foreach (int[] fi in f) { var p = new List<Vector3>(); foreach (int i in fi) p.Add(c[i]); faces.Add(p); }
            return faces;
        }

        /// <summary>Konvexen Körper an der Ebene n·p ≤ d beschneiden (Flächen klippen, Deckel schließen).</summary>
        private static List<List<Vector3>> Schneiden(List<List<Vector3>> faces, Vector3 n, float d)
        {
            var outFaces = new List<List<Vector3>>();
            var cap = new List<Vector3>();
            foreach (var poly in faces)
            {
                var res = new List<Vector3>();
                for (int i = 0; i < poly.Count; i++)
                {
                    Vector3 a = poly[i], b = poly[(i + 1) % poly.Count];
                    float da = Vector3.Dot(n, a) - d, db = Vector3.Dot(n, b) - d;
                    if (da <= 0f) res.Add(a);
                    if ((da < 0f && db > 0f) || (da > 0f && db < 0f))
                    {
                        Vector3 q = a + (b - a) * (da / (da - db));
                        res.Add(q); cap.Add(q);
                    }
                    else if (Mathf.Abs(da) < 1e-6f) cap.Add(a);
                }
                if (res.Count >= 3) outFaces.Add(res);
            }
            if (cap.Count >= 3)
            {
                // Deckel: Punkte um die Normale sortieren (gegen den Uhrzeigersinn von außen)
                Vector3 mid = Vector3.zero; foreach (var p in cap) mid += p; mid /= cap.Count;
                Vector3 u = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized, w = Vector3.Cross(n, u);
                cap.Sort((p, q) => Mathf.Atan2(Vector3.Dot(p - mid, w), Vector3.Dot(p - mid, u)).CompareTo(Mathf.Atan2(Vector3.Dot(q - mid, w), Vector3.Dot(q - mid, u))));
                var dedup = new List<Vector3>();
                foreach (var p in cap) if (dedup.Count == 0 || (p - dedup[dedup.Count - 1]).sqrMagnitude > 1e-8f) dedup.Add(p);
                if (dedup.Count >= 3 && (dedup[0] - dedup[dedup.Count - 1]).sqrMagnitude < 1e-8f) dedup.RemoveAt(dedup.Count - 1);
                if (dedup.Count >= 3)
                {
                    // Umlaufsinn so, dass die Flächennormale nach außen (= n) zeigt
                    Vector3 fn = Vector3.Cross(dedup[1] - dedup[0], dedup[2] - dedup[0]);
                    if (Vector3.Dot(fn, n) < 0f) dedup.Reverse();
                    outFaces.Add(dedup);
                }
            }
            return outFaces;
        }

        private static Koerper Erzeugen(int form, int variante)
        {
            var rng = new System.Random(1009 * form + 31 * variante + 7);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            Vector3 Richtung(float minY, float maxY)
            {
                float az = R(0f, Mathf.PI * 2f), y = R(minY, maxY), r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                return new Vector3(Mathf.Cos(az) * r, y, Mathf.Sin(az) * r).normalized;
            }
            var faces = Quader();
            int cuts;
            switch (form)
            {
                case 0: cuts = 7 + rng.Next(4); break;     // Block
                case 1: cuts = 5 + rng.Next(3); break;     // Platte
                case 2: cuts = 6 + rng.Next(3); break;     // Säule
                case 3: cuts = 16 + rng.Next(6); break;    // Kiesel
                default: cuts = 8 + rng.Next(4); break;    // Wand
            }
            for (int k = 0; k < cuts; k++)
            {
                Vector3 n; float tiefe;
                switch (form)
                {
                    case 1:   // Platte: Kanten brechen, Ober- und Unterseite bleiben (Schichtflächen)
                        n = Richtung(-0.35f, 0.35f); tiefe = R(0.08f, 0.2f); break;
                    case 2:   // Säule: senkrechte Klüfte, oben ein schräger Abbruch
                        n = k < cuts - 2 ? Richtung(-0.12f, 0.12f) : Richtung(0.55f, 0.95f); tiefe = R(0.06f, 0.18f); break;
                    case 3:   // Kiesel: viele flache Schnitte → rund
                        n = Richtung(-0.9f, 0.95f); tiefe = R(0.04f, 0.1f); break;
                    case 4:   // Wand: Vorderseite (+z) und Oberkante unregelmäßig, Rückseite glatt
                        n = Richtung(-0.5f, 0.85f); if (n.z < 0f) n.z = -n.z * 0.4f; n.Normalize(); tiefe = R(0.06f, 0.2f); break;
                    default:  // Block: Ecken abschlagen
                        n = Richtung(-0.7f, 0.9f); tiefe = R(0.08f, 0.24f); break;
                }
                // Abstand der Schnittebene: Stützwert des Quaders in Richtung n (weiteste Ecke) minus Bruchtiefe
                float support = 0.5f * (Mathf.Abs(n.x) + Mathf.Abs(n.y) + Mathf.Abs(n.z));
                faces = Schneiden(faces, n, support - tiefe);
            }
            // leichte Verformung (kein perfekter Kristall): Ecken etwas verschieben, Flächen bleiben eben genug
            var k_ = new Koerper { V = new List<Vector3>(), N = new List<Vector3>(), F = new List<int[]>() };
            foreach (var poly in faces)
            {
                Vector3 fn = Vector3.zero;
                for (int i = 1; i + 1 < poly.Count; i++) fn += Vector3.Cross(poly[i] - poly[0], poly[i + 1] - poly[0]);
                fn.Normalize();
                int b = k_.V.Count;
                foreach (var p in poly) { k_.V.Add(p); k_.N.Add(fn); }
                for (int i = 1; i + 1 < poly.Count; i++) k_.F.Add(new[] { b, b + i, b + i + 1 });
            }
            return k_;
        }

        private static Koerper[,] _cache;
        private static Koerper Form(int f, int v)
        {
            if (_cache == null) _cache = new Koerper[Formen, Varianten];
            f = Mathf.Clamp(f, 0, Formen - 1); v = Mathf.Clamp(v, 0, Varianten - 1);
            if (_cache[f, v].V == null) _cache[f, v] = Erzeugen(f, v);
            return _cache[f, v];
        }

        // ------------------------------------------------------------------ Materialien
        private static Material Granit()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(GranitPfad);
            return m != null ? m : GreyboxKit.Mat("KV4 Fels", new Color(0.46f, 0.45f, 0.43f));
        }

        /// <summary>
        /// Frisch behauener Granit (Felsstirn, Laibungen): heller und etwas wärmer als der verwitterte Naturfels, weil
        /// Flechten, Ruß und Patina fehlen. 05.10. abends: Die Felsstirn lag als Nordwand bei Südwest-Sonne ganz im
        /// Schatten und war mit dem Granit des Naturfelsens fast schwarz; die Halle war kaum lesbar (11 §X).
        /// </summary>
        public static Material GranitBehauen()
        {
            Material granit = Granit();
            var m = AssetDatabase.LoadAssetAtPath<Material>(BehauenPfad);
            if (m == null && AssetDatabase.Contains(granit))
            {
                m = new Material(granit) { name = "W_KV4_Fels_behauen" };
                AssetDatabase.CreateAsset(m, BehauenPfad);
            }
            if (m == null) return granit;
            m.SetColor("_BaseColor", new Color(1.3f, 1.26f, 1.2f, 1f));   // über 1: hellt die Granitkarte auf (Shader multipliziert)
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", Mathf.Max(0f, granit.GetFloat("_Smoothness") - 0.05f));
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Moos(Material granit)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MoosPfad);
            if (m == null && AssetDatabase.Contains(granit))
            {
                m = new Material(granit) { name = "W_KV4_Fels_Moos" };
                AssetDatabase.CreateAsset(m, MoosPfad);
            }
            if (m == null) return GreyboxKit.Mat("KV4 Moos", new Color(0.32f, 0.38f, 0.24f));
            // Granit grünlich getönt: Moos- und Flechtenbelag (Struktur bleibt sichtbar). 05.10. erstes Unity-Bild: 0,58/0,68/0,42
            // mit Glanz 0,08 wirkte im Schatten fast schwarz (Granit spiegelt dort den Himmel) → heller, etwas Glanz.
            m.SetColor("_BaseColor", new Color(0.78f, 0.88f, 0.6f, 1f));
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.45f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ Bauen
        /// <summary>daten: je Fels 12 Werte (x, z, dy, Form, Variante, sx, sy, sz, yaw, pitch, roll, moos).</summary>
        public static int Build(float[] daten, Transform parent, System.Func<float, float, float> boden)
        {
            if (daten == null || daten.Length < 12) return 0;
            if (FelssatzPipeline.Benutzen)
            {
                FelssatzPipeline.Satz satz = FelssatzPipeline.Laden(out string info);
                if (satz != null) return BuildFelssatz(daten, parent, boden, satz, AssetPfad, "KarethV4_Felsen", Granit(), null, "Felsen (Geologie, Felssatz)", "KV4");
                Debug.LogWarning("[Ashveil] Felssatz nicht benutzt: " + info + " – Felsen aus dem alten Baukasten.");
            }
            Transform root = GreyboxKit.Group("Felsen (Geologie, Felsbaukasten)", parent);
            Material granit = Granit(), moos = Moos(granit);
            AssetDatabase.DeleteAsset(AssetPfad);
            Mesh index = null;
            var kacheln = new Dictionary<Vector2Int, (List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t0, List<int> t1)>();
            int count = 0;
            for (int i = 0; i + 11 < daten.Length; i += 12)
            {
                float x = daten[i], z = daten[i + 1], dy = daten[i + 2];
                int form = Mathf.RoundToInt(daten[i + 3]), variante = Mathf.RoundToInt(daten[i + 4]);
                var s = new Vector3(daten[i + 5], daten[i + 6], daten[i + 7]);
                Quaternion q = Quaternion.Euler(0f, daten[i + 8], 0f) * Quaternion.Euler(daten[i + 9], 0f, daten[i + 10]);
                float moosNeigung = daten[i + 11];
                Vector3 pos = new Vector3(x, boden(x, z) + dy, z);
                var key = new Vector2Int(Mathf.FloorToInt(x / Kachel), Mathf.FloorToInt(z / Kachel));
                if (!kacheln.TryGetValue(key, out var k))
                    kacheln[key] = k = (new List<Vector3>(), new List<Vector3>(), new List<Vector2>(), new List<int>(), new List<int>());
                Koerper kr = Form(form, variante);
                Matrix4x4 M = Matrix4x4.TRS(pos, q, s);
                Matrix4x4 Nm = M.inverse.transpose;
                Vector3 kc = new Vector3(key.x * Kachel, 0f, key.y * Kachel);
                int b = k.v.Count;
                for (int vi = 0; vi < kr.V.Count; vi++)
                {
                    Vector3 w = M.MultiplyPoint3x4(kr.V[vi]);
                    Vector3 wn = Nm.MultiplyVector(kr.N[vi]).normalized;
                    k.v.Add(w - kc); k.n.Add(wn);
                    // Box-Projektion in Metern (Kachel 2 m), Achse nach der stärksten Normalenrichtung
                    Vector3 a = new Vector3(Mathf.Abs(wn.x), Mathf.Abs(wn.y), Mathf.Abs(wn.z));
                    Vector2 uv = a.y >= a.x && a.y >= a.z ? new Vector2(w.x, w.z) : a.x >= a.z ? new Vector2(w.z, w.y) : new Vector2(w.x, w.y);
                    k.uv.Add(uv);                                     // Meter; die Kachelung (2 m) steckt im Material
                }
                foreach (int[] tri in kr.F)
                {
                    Vector3 fn = k.n[b + tri[0]];
                    // Moos: oben, und auf der Schattenseite (NO); hoch am Berg kaum
                    float schatten = Mathf.Max(0f, fn.x * 0.62f + fn.z * 0.78f);
                    float hoehe = pos.y > 250f ? 0.25f : 1f;
                    // (05.10.: Schattenseite erst ab 0,35 Neigung nach oben – senkrechte Nordflächen bleiben Fels, sonst wirken
                    // sie als dunkle Löcher in der Wand)
                    bool istMoos = fn.y > 1f - 0.42f * moosNeigung * hoehe || (fn.y > 0.35f && schatten > 0.55f && moosNeigung * hoehe > 0.5f);
                    var list = istMoos ? k.t1 : k.t0;
                    list.Add(b + tri[0]); list.Add(b + tri[1]); list.Add(b + tri[2]);
                }
                count++;
            }
            int renderer = 0; long tris = 0;
            foreach (var kv in kacheln)
            {
                var k = kv.Value;
                var mesh = new Mesh { name = $"KV4 Felsen {kv.Key.x}_{kv.Key.y}", indexFormat = k.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(k.v); mesh.SetNormals(k.n); mesh.SetUVs(0, k.uv);
                mesh.subMeshCount = 2;
                mesh.SetTriangles(k.t0, 0); mesh.SetTriangles(k.t1, 1);
                mesh.RecalculateBounds(); mesh.RecalculateTangents();
                if (index == null) { index = mesh; AssetDatabase.CreateAsset(mesh, AssetPfad); }
                else AssetDatabase.AddObjectToAsset(mesh, index);
                var go = new GameObject(mesh.name);
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3(kv.Key.x * Kachel, 0f, kv.Key.y * Kachel);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = new[] { granit, moos };
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
                // Ferne Kacheln mit nur kleinen Steinen verschwinden früh (LOD: eine Stufe, dann ausblenden)
                var lod = go.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(0.012f, new Renderer[] { r }) });
                lod.RecalculateBounds();
                renderer++; tris += (k.t0.Count + k.t1.Count) / 3;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Ashveil] Felsbaukasten: {count} Felsen in {renderer} Kacheln, {tris} Dreiecke.");
            return count;
        }

        // ------------------------------------------------------------------ Felssatz (Blender) mit Detailstufen

        /// <summary>Bildhöhenanteil der Kachel (Unity-LODGroup), ab dem LOD1, LOD2 und „aus“ gelten. 05.10. abends nach der
        /// Messung (0,6/0,15/0,012: nahe Stufe bis ≈ 375 m bei LOD-Bias 2, +0,44 ms GPU) angezogen: nahe Stufe bis ≈ 225 m.</summary>
        public static readonly float[] KachelLod = { 1.0f, 0.25f, 0.015f };
        private const float LodFov = 60f;           // Öffnungswinkel für die Stufenwahl (Spielkamera ≈ 60°)
        private const float BildHoehe = 1080f;      // Pixel, auf die sich die Schwellen beziehen
        private const float PxLod0 = 150f, PxLod1 = 45f, PxMin = 5f;   // ab dieser Bildhöhe: Netzstufe 0 / 1 / 2, darunter weg
        private const float KollisionGross = 6f;    // Felsen ab dieser Größe (m) bekommen die feinere Kollision (Stufe 1)

        private struct Fels
        {
            public Vector3 Pos, S;
            public Quaternion Q;
            public int Form, Variante;
            public float Moos, Groesse;
            public FelssatzPipeline.Netz Alt;        // Rückfall: Polyeder, wenn die Form im Satz fehlt
        }

        /// <summary>Sammelt Felsnetze einer Kachelstufe: Weltpunkte, Box-UV je Dreieck (Punkte je Achse getrennt), Moos.</summary>
        private sealed class KachelNetz
        {
            public readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<int> T0 = new List<int>(), T1 = new List<int>();
            private int[] _map = new int[0];
            private Vector3[] _wv = new Vector3[0], _wn = new Vector3[0];

            public void Add(FelssatzPipeline.Netz n, Matrix4x4 m, Vector3 kachel, float moosNeigung, float hoehe)
            {
                int nv = n.V.Length;
                if (_wv.Length < nv) { _wv = new Vector3[nv]; _wn = new Vector3[nv]; }
                if (_map.Length < nv * 3) _map = new int[nv * 3];
                for (int i = 0; i < nv * 3; i++) _map[i] = -1;
                Matrix4x4 nm = m.inverse.transpose;
                for (int i = 0; i < nv; i++)
                {
                    _wv[i] = m.MultiplyPoint3x4(n.V[i]);
                    _wn[i] = nm.MultiplyVector(n.N[i]).normalized;
                }
                for (int t = 0; t < n.T.Length; t += 3)
                {
                    int a = n.T[t], b = n.T[t + 1], c = n.T[t + 2];
                    Vector3 fn = (_wn[a] + _wn[b] + _wn[c]).normalized;   // geglättete Dreiecksnormale (weniger Flickenteppich)
                    float ax = Mathf.Abs(fn.x), ay = Mathf.Abs(fn.y), az = Mathf.Abs(fn.z);
                    int achse = ay >= ax && ay >= az ? 1 : ax >= az ? 0 : 2;
                    // Moos wie beim Baukasten: oben, und auf der Schattenseite (NO) ab 0,35 Neigung nach oben; hoch am Berg kaum
                    float schatten = Mathf.Max(0f, fn.x * 0.62f + fn.z * 0.78f);
                    bool istMoos = fn.y > 1f - 0.42f * moosNeigung * hoehe || (fn.y > 0.35f && schatten > 0.55f && moosNeigung * hoehe > 0.5f);
                    List<int> liste = istMoos ? T1 : T0;
                    liste.Add(Punkt(a, achse, kachel));
                    liste.Add(Punkt(b, achse, kachel));
                    liste.Add(Punkt(c, achse, kachel));
                }
            }

            private int Punkt(int i, int achse, Vector3 kachel)
            {
                int key = i * 3 + achse;
                if (_map[key] >= 0) return _map[key];
                Vector3 w = _wv[i];
                V.Add(w - kachel);
                N.Add(_wn[i]);
                UV.Add(achse == 1 ? new Vector2(w.x, w.z) : achse == 0 ? new Vector2(w.z, w.y) : new Vector2(w.x, w.y));   // Meter
                return _map[key] = V.Count - 1;
            }

            public int Dreiecke => (T0.Count + T1.Count) / 3;

            public Mesh Bauen(string name)
            {
                if (V.Count == 0) return null;
                var mesh = new Mesh { name = name, indexFormat = V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(V); mesh.SetNormals(N); mesh.SetUVs(0, UV);
                mesh.subMeshCount = 2;
                mesh.SetTriangles(T0, 0); mesh.SetTriangles(T1, 1);
                mesh.RecalculateBounds(); mesh.RecalculateTangents();
                return mesh;
            }
        }

        /// <summary>Nur Punkte und Dreiecke – für die Kollision.</summary>
        private sealed class KollisionsNetz
        {
            private readonly List<Vector3> _v = new List<Vector3>();
            private readonly List<int> _t = new List<int>();
            public int Dreiecke => _t.Count / 3;

            public void Add(FelssatzPipeline.Netz n, Matrix4x4 m, Vector3 kachel)
            {
                int b = _v.Count;
                foreach (Vector3 p in n.V) _v.Add(m.MultiplyPoint3x4(p) - kachel);
                foreach (int i in n.T) _t.Add(b + i);
            }

            public Mesh Bauen(string name)
            {
                if (_v.Count == 0) return null;
                var mesh = new Mesh { name = name, indexFormat = _v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(_v);
                mesh.SetTriangles(_t, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        /// <summary>Darstellungsnetz im fertigen Spiel nicht lesbar: Unity gibt die Kopie im Hauptspeicher nach dem Hochladen
        /// frei (die Kollision hat eigene, lesbare Netze). Im Editor bleiben die Daten erhalten.</summary>
        private static void NichtLesbar(Mesh m)
        {
            var so = new SerializedObject(m);
            SerializedProperty p = so.FindProperty("m_IsReadable");
            if (p == null) return;
            p.boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static FelssatzPipeline.Netz AlsNetz(Koerper k)
        {
            var t = new List<int>();
            foreach (int[] f in k.F) t.AddRange(f);
            return new FelssatzPipeline.Netz { V = k.V.ToArray(), N = k.N.ToArray(), T = t.ToArray() };
        }

        /// <summary>
        /// Felssatz mit eigenem Ziel (14.10.2026, Westweiden-Kreidefelsen): dieselben Formen aus Blender, aber eigene Datei, eigenes
        /// Material (fels, bewuchs = Oberseiten) und eigener Name. Kareth benutzt weiter <see cref="Build"/> (unverändert).
        /// −1, wenn der Felssatz fehlt oder abgeschaltet ist (dann baut der Aufrufer nichts – kein Rückfall auf Polyeder).
        /// </summary>
        public static int BuildSatz(float[] daten, Transform parent, System.Func<float, float, float> boden, string assetPfad, string name,
                                    Material fels, Material bewuchs, string gruppe, string praefix)
        {
            if (daten == null || daten.Length < 12 || !FelssatzPipeline.Benutzen) return -1;
            FelssatzPipeline.Satz satz = FelssatzPipeline.Laden(out _);
            if (satz == null) return -1;
            return BuildFelssatz(daten, parent, boden, satz, assetPfad, name, fels, bewuchs, gruppe, praefix);
        }

        private static int BuildFelssatz(float[] daten, Transform parent, System.Func<float, float, float> boden, FelssatzPipeline.Satz satz,
                                         string assetPfad, string satzName, Material granit, Material moos, string gruppe, string praefix)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Transform root = GreyboxKit.Group(gruppe, parent);
            if (moos == null) moos = Moos(granit);
            AssetDatabase.DeleteAsset(assetPfad);
            var behaelter = ScriptableObject.CreateInstance<Ashveil.World.FelsKachelSatz>();
            behaelter.name = satzName;
            AssetDatabase.CreateAsset(behaelter, assetPfad);

            // 1) Felsen lesen, Variante im Satz wählen, Kacheln zuordnen
            var kacheln = new Dictionary<Vector2Int, List<Fels>>();
            int count = 0, rueckfall = 0;
            for (int i = 0; i + 11 < daten.Length; i += 12)
            {
                float x = daten[i], z = daten[i + 1], dy = daten[i + 2];
                int form = Mathf.Clamp(Mathf.RoundToInt(daten[i + 3]), 0, Formen - 1), wunsch = Mathf.RoundToInt(daten[i + 4]);
                var fels = new Fels
                {
                    Pos = new Vector3(x, boden(x, z) + dy, z),
                    S = new Vector3(daten[i + 5], daten[i + 6], daten[i + 7]),
                    Q = Quaternion.Euler(0f, daten[i + 8], 0f) * Quaternion.Euler(daten[i + 9], 0f, daten[i + 10]),
                    Form = form,
                    Variante = satz.Variante(form, wunsch),
                    Moos = daten[i + 11]
                };
                fels.Groesse = Mathf.Max(fels.S.x, Mathf.Max(fels.S.y, fels.S.z));
                if (fels.Variante < 0) { fels.Alt = AlsNetz(Form(form, wunsch)); rueckfall++; }
                var key = new Vector2Int(Mathf.FloorToInt(x / Kachel), Mathf.FloorToInt(z / Kachel));
                if (!kacheln.TryGetValue(key, out List<Fels> liste)) kacheln[key] = liste = new List<Fels>();
                liste.Add(fels);
                count++;
            }

            // 2) je Kachel: LOD0 (alle Felsen fein), daraus Größe und Mitte der Kachel, dann LOD1/LOD2 nach Bildhöhe
            float tanHalb = Mathf.Tan(LodFov * 0.5f * Mathf.Deg2Rad);
            // LOD-Bias der aktuellen Qualitätsstufe (Editor: „PC“ = 2): Die Kachelstufe wechselt im Spiel erst beim Bias-fachen
            // Abstand, die Felsen erscheinen dort entsprechend kleiner. Schwächere Stufen wechseln früher und zeigen gröbere Felsen.
            float bias = Mathf.Max(1f, QualitySettings.lodBias);
            long[] summe = new long[4];
            int kachelnGebaut = 0;
            var wahl = new int[3, 4];                // [Kachelstufe, Netzstufe 0..2 / weg]
            foreach (var kv in kacheln)
            {
                Vector3 kc = new Vector3(kv.Key.x * Kachel, 0f, kv.Key.y * Kachel);
                var stufen = new[] { new KachelNetz(), new KachelNetz(), new KachelNetz() };
                var kollision = new KollisionsNetz();
                foreach (Fels f in kv.Value)
                {
                    Matrix4x4 m = Matrix4x4.TRS(f.Pos, f.Q, f.S);
                    float hoehe = f.Pos.y > 250f ? 0.25f : 1f;
                    stufen[0].Add(f.Alt ?? satz.Get(f.Form, f.Variante, 0), m, kc, f.Moos, hoehe);
                    wahl[0, 0]++;
                    kollision.Add(f.Alt ?? satz.Get(f.Form, f.Variante, f.Groesse >= KollisionGross ? 1 : 2), m, kc);
                }
                Mesh lod0 = stufen[0].Bauen($"{praefix} Felsen {kv.Key.x}_{kv.Key.y} LOD0");
                if (lod0 == null) continue;
                Bounds b = lod0.bounds;
                Vector3 mitte = b.center + kc;
                float groesse = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                for (int k = 1; k < 3; k++)
                {
                    // Abstand (zur Kachelmitte), ab dem diese Kachelstufe gilt; der Fels kann der Kamera um seinen Abstand
                    // zur Mitte näher sein.
                    float ab = groesse * bias / (2f * tanHalb * KachelLod[k - 1]);
                    foreach (Fels f in kv.Value)
                    {
                        float dmin = Mathf.Max(1f, ab - Vector3.Distance(f.Pos, mitte));
                        float px = BildHoehe * f.Groesse / (2f * tanHalb * dmin);
                        int stufe = px >= PxLod0 ? 0 : px >= PxLod1 ? 1 : px >= PxMin ? 2 : -1;
                        wahl[k, stufe < 0 ? 3 : stufe]++;
                        if (stufe < 0) continue;
                        stufen[k].Add(f.Alt ?? satz.Get(f.Form, f.Variante, stufe), Matrix4x4.TRS(f.Pos, f.Q, f.S), kc, f.Moos, f.Pos.y > 250f ? 0.25f : 1f);
                    }
                }

                var go = new GameObject($"{praefix} Felsen {kv.Key.x}_{kv.Key.y}");
                go.transform.SetParent(root, false);
                go.transform.position = kc;
                var lods = new LOD[3];
                for (int k = 0; k < 3; k++)
                {
                    Mesh mesh = k == 0 ? lod0 : stufen[k].Bauen($"{praefix} Felsen {kv.Key.x}_{kv.Key.y} LOD{k}");
                    if (mesh == null) { lods[k] = new LOD(KachelLod[k], new Renderer[0]); continue; }
                    NichtLesbar(mesh);
                    AssetDatabase.AddObjectToAsset(mesh, behaelter);
                    var child = new GameObject("LOD" + k);
                    child.transform.SetParent(go.transform, false);
                    child.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = child.AddComponent<MeshRenderer>();
                    r.sharedMaterials = new[] { granit, moos };
                    // Kein BatchingStatic: die Kacheln sind schon zusammengefasst, statisches Batching würde den Speicher verdoppeln
                    GameObjectUtility.SetStaticEditorFlags(child, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
                    lods[k] = new LOD(KachelLod[k], new Renderer[] { r });
                    summe[k] += stufen[k].Dreiecke;
                }
                Mesh kol = kollision.Bauen($"{praefix} Felsen {kv.Key.x}_{kv.Key.y} Kollision");
                if (kol != null)
                {
                    AssetDatabase.AddObjectToAsset(kol, behaelter);
                    var c = new GameObject("Kollision");
                    c.transform.SetParent(go.transform, false);
                    c.AddComponent<MeshCollider>().sharedMesh = kol;
                    summe[3] += kollision.Dreiecke;
                }
                var lg = go.AddComponent<LODGroup>();
                lg.fadeMode = LODFadeMode.None;
                lg.SetLODs(lods);
                lg.RecalculateBounds();
                kachelnGebaut++;
            }
            behaelter.quelle = satz.Quelle + $" · gebaut {System.DateTime.Now:yyyy-MM-dd HH:mm}";
            behaelter.felsen = count;
            behaelter.kacheln = kachelnGebaut;
            behaelter.dreiecke = summe;
            behaelter.lodHoehen = (float[])KachelLod.Clone();
            EditorUtility.SetDirty(behaelter);
            AssetDatabase.SaveAssets();
            long bytes = 0;
            try { bytes = new System.IO.FileInfo(System.IO.Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, assetPfad)).Length; }
            catch (System.Exception) { }
            Debug.Log($"[Ashveil] Felssatz {satzName}: {count} Felsen in {kachelnGebaut} Kacheln ({sw.Elapsed.TotalSeconds:0.0} s). Dreiecke gesamt LOD0 {summe[0]:N0} · " +
                      $"LOD1 {summe[1]:N0} · LOD2 {summe[2]:N0} · Kollision {summe[3]:N0}; Datei {bytes / 1048576.0:0.0} MB (binär). " +
                      $"Netzstufen in Kachel-LOD1: {wahl[1, 0]}/{wahl[1, 1]}/{wahl[1, 2]}/weg {wahl[1, 3]}, " +
                      $"in LOD2: {wahl[2, 0]}/{wahl[2, 1]}/{wahl[2, 2]}/weg {wahl[2, 3]} (LOD-Bias {bias:0.#})" +
                      (rueckfall > 0 ? $"; {rueckfall} Felsen mit altem Polyeder (Form fehlt im Satz)" : "") + ". " + satz.Quelle);
            return count;
        }

        // ------------------------------------------------------------------ Behauener Fels (Felsstirn, Laibungen)
        /// <summary>
        /// Sammelt Quader aus behauenem Fels zu einem Mesh: Granit (wie die Felsen), Oberseiten bemoost, UVs in Weltmetern
        /// (Box-Projektion, darum laufen Nachbarquader ohne Naht weiter), eine Kollision. Für die Felsstirn am Lager: eine
        /// Wand aus Streifen mit Öffnungen für die Halle und die Nischen (KarethV4Camp.FelsStirn).
        /// </summary>
        public sealed class Behauen
        {
            private readonly List<Vector3> _v = new List<Vector3>(), _n = new List<Vector3>();
            private readonly List<Vector2> _uv = new List<Vector2>();
            private readonly List<int> _t0 = new List<int>(), _t1 = new List<int>();
            public int Anzahl { get; private set; }

            /// <summary>Quader in Weltkoordinaten: Mitte, Größe (x entlang, y hoch, z tief), Drehung.</summary>
            public void Add(Vector3 mitte, Vector3 groesse, Quaternion rot, bool moosOben = true)
            {
                Vector3 h = groesse * 0.5f;
                Vector3[] normalen = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left, Vector3.up, Vector3.down };
                foreach (Vector3 nl in normalen)
                {
                    Vector3 a1 = Mathf.Abs(nl.y) > 0.5f ? Vector3.right : Vector3.up;
                    Vector3 a2 = Vector3.Cross(nl, a1);
                    Vector3 c = Vector3.Scale(nl, h), e1 = Vector3.Scale(a1, h), e2 = Vector3.Scale(a2, h);
                    Vector3 wn = rot * nl;
                    int b = _v.Count;
                    foreach (Vector3 lp in new[] { c - e1 - e2, c + e1 - e2, c + e1 + e2, c - e1 + e2 })
                    {
                        Vector3 w = mitte + rot * lp;
                        _v.Add(w); _n.Add(wn);
                        Vector3 an = new Vector3(Mathf.Abs(wn.x), Mathf.Abs(wn.y), Mathf.Abs(wn.z));
                        _uv.Add(an.y >= an.x && an.y >= an.z ? new Vector2(w.x, w.z) : an.x >= an.z ? new Vector2(w.z, w.y) : new Vector2(w.x, w.y));
                    }
                    var liste = moosOben && wn.y > 0.7f ? _t1 : _t0;
                    // Umlaufsinn: Kreuzprodukt der ersten Kanten zeigt nach außen (Unity: dann Vorderseite)
                    if (Vector3.Dot(Vector3.Cross(_v[b + 1] - _v[b], _v[b + 2] - _v[b]), wn) >= 0f)
                        liste.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                    else
                        liste.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                }
                Anzahl++;
            }

            /// <summary>Ein Objekt mit Mesh (Granit, Moos), MeshCollider und statischen Flags. Gibt null zurück, wenn leer.</summary>
            public GameObject Bauen(string name, Transform parent, Material stein = null)
            {
                if (_v.Count == 0) return null;
                Vector3 pivot = Vector3.zero;
                foreach (Vector3 p in _v) pivot += p;
                pivot /= _v.Count;
                var verts = new List<Vector3>(_v.Count);
                foreach (Vector3 p in _v) verts.Add(p - pivot);
                var mesh = new Mesh { name = name, indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(verts); mesh.SetNormals(_n); mesh.SetUVs(0, _uv);
                mesh.subMeshCount = 2;
                mesh.SetTriangles(_t0, 0); mesh.SetTriangles(_t1, 1);
                mesh.RecalculateBounds(); mesh.RecalculateTangents();
                var go = new GameObject(name);
                go.transform.position = pivot;
                if (parent != null) go.transform.SetParent(parent, true);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                Material granit = Granit();
                go.AddComponent<MeshRenderer>().sharedMaterials = new[] { stein != null ? stein : granit, Moos(granit) };
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
                return go;
            }
        }
    }
}
