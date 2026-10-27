using System.Collections.Generic;
using Ashveil.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Westweiden – die drei Windmühlen auf dem Mühlenrain als Modell (Lückenliste Punkt 6 „Mühlen-Hero“, 14.10.2026, VORSCHLAG)
    /// statt Greybox (Zylinder, Kasten, zwei starre Balken). Maße wie in der Prüfung 12.10. (F1) festgelegt: Turm 24 m, Flügel 30 m
    /// (stilisiert über echtem Maß, damit sie vom Kammsattel aus 1,6 km als Silhouette lesbar sind).
    /// <list type="bullet">
    /// <item><b>Turm:</b> gemauerter Kegelstumpf (Ø 8,8 → 6,4 m), Bänder des Trim-Sheets wie die Häuser (Sockel, Bruchstein,
    /// Eichenschwelle, Kalkputz), aber nachgedunkelt (Silhouette im Dunst), Tür und drei kleine Fenster.</item>
    /// <item><b>Kappe:</b> gewölbt, mit Eichenschindeln wie Turm und Wache in Langer Rain; Wellenkopf nach Westen (Westwind).</item>
    /// <item><b>Flügel:</b> vier Ruten mit Gitterwerk und gesetztem Leinensegel, drehen langsam (vorhandener <see cref="SlowRotator"/>,
    /// 4 Umdrehungen je Minute, jede Mühle mit eigener Stellung) – reine Darstellung, kein Netzverkehr.</item>
    /// <item><b>Steert:</b> der lange Balken von der Kappe schräg zum Boden, mit dem der Müller die Kappe in den Wind dreht.</item>
    /// </list>
    /// Kollision nur am Turm (Kapsel). Gitter-Material beidseitig mit Ausschnitt.
    /// </summary>
    public static class WindmuehleModell
    {
        private const string Folder = ProjectSetup.Root + "/Art/Westweiden/Muehle";
        /// <summary>Flügel drehen (aus = stehende Flügel). Messung 14.10.: kostet nichts Messbares.</summary>
        public static readonly bool Drehen = true;
        /// <summary>Segeltuch-Karten auf den Ruten (aus = nur Ruten). Messung 14.10.: kostet nichts Messbares.</summary>
        public static readonly bool Segel = true;
        private const float TurmH = 22f, R0 = 4.4f, R1 = 3.2f, NabeH = 23.2f, Rute = 15f;

        /// <summary>Mühle bauen; fuss = Bodenpunkt der Turmmitte, yaw = Richtung, in die die Flügel zeigen (Wind von dort).</summary>
        public static GameObject Bauen(Transform parent, string name, Vector3 fuss, float yaw, int k, Material holz)
        {
            Material wand = TurmMat(AssetDatabase.LoadAssetAtPath<Material>(ProjectSetup.Root + "/Art/Westweiden/Proben/WW_Trim_Probe.mat"));
            Material dach = AssetDatabase.LoadAssetAtPath<Material>(ProjectSetup.Root + "/Art/Westweiden/Proben/WW_Dach_Schindel.mat");
            if (wand == null || dach == null || holz == null) return null;
            Material gitter = GitterMat();
            Material dunkel = DunkelMat();
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(fuss, Quaternion.Euler(0f, yaw, 0f));

            // Turm
            var turm = new GameObject("Turm");
            turm.transform.SetParent(root, false);
            turm.AddComponent<MeshFilter>().sharedMesh = Turm();
            turm.AddComponent<MeshRenderer>().sharedMaterial = wand;
            var cap = turm.AddComponent<CapsuleCollider>();
            cap.direction = 1; cap.radius = R0 * 0.95f; cap.height = TurmH + 2f; cap.center = new Vector3(0f, TurmH * 0.5f, 0f);
            // Tür und Fenster (dunkle Öffnungen, leicht vor der Wand)
            Kiste(root, "Tür", new Vector3(0f, 1.25f, -(R0 - 0.08f)), new Vector3(1.5f, 2.5f, 0.2f), 0f, dunkel);
            float[] fh = { 7f, 12.5f, 17.5f };
            for (int i = 0; i < fh.Length; i++)
            {
                float rr = Mathf.Lerp(R0, R1, fh[i] / TurmH) - 0.06f;
                float a = (i * 120f + 40f) * Mathf.Deg2Rad;
                Kiste(root, "Fenster", new Vector3(Mathf.Sin(a) * rr, fh[i], Mathf.Cos(a) * rr), new Vector3(0.7f, 1.0f, 0.2f), i * 120f + 40f, dunkel);
            }

            // Kappe mit Welle, Flügeln und Steert
            var kappe = new GameObject("Kappe").transform;
            kappe.SetParent(root, false);
            kappe.localPosition = new Vector3(0f, TurmH, 0f);
            var kg = new GameObject("Haube");
            kg.transform.SetParent(kappe, false);
            kg.AddComponent<MeshFilter>().sharedMesh = Haube();
            kg.AddComponent<MeshRenderer>().sharedMaterial = dach;
            Kiste(kappe, "Welle", new Vector3(0f, NabeH - TurmH, R1 + 0.9f), new Vector3(0.6f, 0.6f, 3.0f), 0f, holz);
            Kiste(kappe, "Steert", new Vector3(0f, (NabeH - TurmH) * 0.5f - 10f, -(R1 + 4.2f)), new Vector3(0.35f, 0.35f, 23.5f), 0f, holz)
                .transform.localRotation = Quaternion.Euler(-62f, 0f, 0f);
            Kiste(kappe, "Steert-Fuß", new Vector3(0f, -TurmH + 0.6f, -(R1 + 9.2f)), new Vector3(1.6f, 1.2f, 0.4f), 0f, holz);

            var nabe = new GameObject("Flügel").transform;
            nabe.SetParent(kappe, false);
            nabe.localPosition = new Vector3(0f, NabeH - TurmH, R1 + 2.3f);
            nabe.localRotation = Quaternion.Euler(0f, 0f, k * 27f + 10f);   // jede Mühle in eigener Stellung
            Kiste(nabe, "Nabe", Vector3.zero, new Vector3(1.0f, 1.0f, 0.8f), 0f, holz);
            for (int a = 0; a < 4; a++)
            {
                var arm = new GameObject("Rute " + (a + 1)).transform;
                arm.SetParent(nabe, false);
                arm.localRotation = Quaternion.Euler(0f, 0f, a * 90f);
                Kiste(arm, "Rutenbalken", new Vector3(0f, Rute * 0.5f, 0.1f), new Vector3(0.38f, Rute, 0.3f), 0f, holz);
                if (!Segel) continue;
                var g = new GameObject("Gitter");
                g.transform.SetParent(arm, false);
                g.transform.localPosition = new Vector3(1.35f, 2.2f + (Rute - 2.6f) * 0.5f, 0.25f);
                g.AddComponent<MeshFilter>().sharedMesh = Gitter();
                var mr = g.AddComponent<MeshRenderer>();
                mr.sharedMaterial = gitter;
            }
            // Drehende Teile: keine Schatten der Segeltücher (große Ausschnitt-Flächen, jedes Bild neu) und nicht in die Strahlenverfolgung
            // des dynamischen GI (bewegte Objekte erneuern sonst jedes Bild die Beschleunigungsstruktur)
            foreach (Renderer r in nabe.GetComponentsInChildren<Renderer>(true))
            {
                r.rayTracingMode = UnityEngine.Experimental.Rendering.RayTracingMode.Off;
                if (r.name == "Gitter") r.shadowCastingMode = ShadowCastingMode.Off;
            }
            if (!Drehen) return root.gameObject;
            var rot = nabe.gameObject.AddComponent<SlowRotator>();
            var so = new SerializedObject(rot);
            so.FindProperty("localAxis").vector3Value = Vector3.forward;
            so.FindProperty("degreesPerSecond").floatValue = -24f;   // 4 Umdrehungen je Minute, gegen den Uhrzeigersinn von vorn
            so.ApplyModifiedPropertiesWithoutUndo();
            return root.gameObject;
        }

        private static GameObject Kiste(Transform parent, string name, Vector3 pos, Vector3 groesse, float yaw, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            g.transform.localScale = groesse;
            g.GetComponent<Renderer>().sharedMaterial = m;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            return g;
        }

        /// <summary>Kegelstumpf mit den Bändern des Trim-Sheets (wie <see cref="WestweidenOrte"/>: Sockel −1,5–0,6 m, Bruchstein bis 2,2 m,
        /// Eichenschwelle 0,25 m, darüber Kalkputz; u = Meter / 2).</summary>
        private static Mesh Turm()
        {
            const int Seg = 20;
            var baender = new List<(float y0, float y1, float v0, float v1)>
            {
                (-1.5f, 0.6f, 0.76f, 0.92f), (0.6f, 2.2f, 0.0f, 0.40f), (2.2f, 2.45f, 0.60f, 0.76f), (2.45f, TurmH, 0.40f, 0.60f),
            };
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            float umfang = 2f * Mathf.PI * R0;
            foreach (var (y0, y1, v0, v1) in baender)
            {
                // Putzband in Abschnitte teilen, damit die Rundung der Neigung folgt
                int teile = y1 - y0 > 4f ? Mathf.CeilToInt((y1 - y0) / 4f) : 1;
                for (int p = 0; p < teile; p++)
                {
                    float ya = Mathf.Lerp(y0, y1, p / (float)teile), yb = Mathf.Lerp(y0, y1, (p + 1) / (float)teile);
                    float va = Mathf.Lerp(v0, v1, p / (float)teile), vb = Mathf.Lerp(v0, v1, (p + 1) / (float)teile);
                    int basis = v.Count;
                    for (int s = 0; s <= Seg; s++)
                    {
                        float a = s / (float)Seg * Mathf.PI * 2f;
                        var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                        float ra = Mathf.Lerp(R0, R1, Mathf.Clamp01(ya / TurmH)), rb = Mathf.Lerp(R0, R1, Mathf.Clamp01(yb / TurmH));
                        Vector3 nn = (d + Vector3.up * ((R0 - R1) / TurmH)).normalized;
                        v.Add(d * ra + Vector3.up * ya); n.Add(nn); uv.Add(new Vector2(s / (float)Seg * umfang / 2f, 1f - va));
                        v.Add(d * rb + Vector3.up * yb); n.Add(nn); uv.Add(new Vector2(s / (float)Seg * umfang / 2f, 1f - vb));
                    }
                    for (int s = 0; s < Seg; s++)
                    {
                        int i = basis + s * 2;
                        tri.AddRange(new[] { i, i + 2, i + 1, i + 2, i + 3, i + 1 });   // im Uhrzeigersinn von außen
                    }
                }
            }
            var m = new Mesh { name = "WW Mühlenturm" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Gewölbte Haube (Halbkugel gestaucht, etwas über den Turm überstehend); UV 3 m je Kachel wie die Dächer.</summary>
        private static Mesh Haube()
        {
            const int Seg = 20, Ringe = 7;
            float r = R1 + 0.5f, h = 4.4f;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int i = 0; i <= Ringe; i++)
            {
                float t = i / (float)Ringe, ph = t * Mathf.PI * 0.5f;
                float rr = Mathf.Cos(ph) * r, y = Mathf.Sin(ph) * h - 0.3f;
                for (int s = 0; s <= Seg; s++)
                {
                    float a = s / (float)Seg * Mathf.PI * 2f;
                    var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    v.Add(d * rr + Vector3.up * y);
                    n.Add((d * Mathf.Cos(ph) / r + Vector3.up * Mathf.Sin(ph) / h).normalized);
                    uv.Add(new Vector2(s / (float)Seg * 2f * Mathf.PI * r / 3f, t * (r + h) * 0.8f / 3f));
                }
            }
            for (int i = 0; i < Ringe; i++)
                for (int s = 0; s < Seg; s++)
                {
                    int a = i * (Seg + 1) + s, b = a + Seg + 1;
                    tri.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });   // im Uhrzeigersinn von außen
                }
            var m = new Mesh { name = "WW Mühlenhaube" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Gitterfläche einer Rute: 2,4 × 12,4 m, eine Karte (Material beidseitig).</summary>
        private static Mesh Gitter()
        {
            float b = 2.4f, h = Rute - 2.6f;
            var m = new Mesh { name = "WW Mühlengitter" };
            m.SetVertices(new List<Vector3> { new Vector3(-b * 0.5f, -h * 0.5f, 0f), new Vector3(b * 0.5f, -h * 0.5f, 0f), new Vector3(-b * 0.5f, h * 0.5f, 0f), new Vector3(b * 0.5f, h * 0.5f, 0f) });
            m.SetNormals(new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            m.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }

        private static Material GitterMat()
        {
            ProjectSetup.EnsureFolder(Folder);
            string pfad = Folder + "/WW_Muehle_Gitter.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(pfad);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, pfad); }
            mat.SetTexture("_BaseMap", GitterTextur());
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.1f);
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_Cull", 0f);
            mat.doubleSidedGI = true;
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Turm: dasselbe Trim-Sheet wie die Häuser, aber mit dunklerem Ton (verwitterter, nachgedunkelter Kalk). Vergleichsbild 14.10.:
        /// ein hell verputzter Turm verschwindet im Dunst der Westweiden, F1 verlangt die Silhouette vom Kammsattel; ein Bruchsteinturm
        /// zeigte aus der Ferne Ringe (das Band wiederholt sich alle 1,6 m).
        /// </summary>
        private static Material TurmMat(Material trim)
        {
            if (trim == null) return null;
            ProjectSetup.EnsureFolder(Folder);
            string pfad = Folder + "/WW_Muehle_Turm.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(pfad);
            if (mat == null) { mat = new Material(trim); AssetDatabase.CreateAsset(mat, pfad); }
            else mat.CopyPropertiesFromMaterial(trim);
            mat.SetColor("_BaseColor", new Color(0.56f, 0.51f, 0.45f, 1f));
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material DunkelMat()
        {
            ProjectSetup.EnsureFolder(Folder);
            string pfad = Folder + "/WW_Muehle_Oeffnung.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(pfad);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, pfad); }
            mat.SetColor("_BaseColor", new Color(0.09f, 0.075f, 0.06f));
            mat.SetFloat("_Smoothness", 0.05f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Gitterwerk (Rahmen, 16 Querlatten, eine Längslatte) mit gesetztem Leinensegel; außerhalb des Rahmens durchsichtig.</summary>
        private static Texture2D GitterTextur()
        {
            const int W = 64, H = 512;
            var px = new Color[W * H];
            var holz = new Color(0.40f, 0.31f, 0.22f);
            var segel = new Color(0.66f, 0.58f, 0.46f);   // geöltes Leinen, dunkler als der Himmel (Silhouette)
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)(W - 1), vv = y / (float)(H - 1);
                    bool rahmen = u < 0.07f || u > 0.93f || vv < 0.012f || vv > 0.988f;
                    bool quer = Mathf.Repeat(vv * 16f, 1f) < 0.09f;
                    bool laengs = Mathf.Abs(u - 0.5f) < 0.035f;
                    bool an = rahmen || quer || laengs;
                    float mas = 0.9f + 0.1f * Mathf.PerlinNoise(u * 4f, vv * 60f);
                    // Segeltuch über dem Gitter (vom Rand und von der Spitze etwas zurück): macht die Flügel auch aus der Ferne
                    // zu einer Fläche statt eines Striches; leichte Falten quer und ein dunkleres Gitter darauf
                    bool tuch = u > 0.1f && u < 0.9f && vv > 0.05f && vv < 0.95f;
                    float falte = 0.88f + 0.12f * Mathf.Sin(vv * 160f) * Mathf.Sin(u * 3.1f);
                    if (an) px[y * W + x] = new Color(holz.r * mas, holz.g * mas, holz.b * mas, 1f);
                    else if (tuch) px[y * W + x] = new Color(segel.r * falte, segel.g * falte, segel.b * falte, 1f);
                    else px[y * W + x] = new Color(holz.r, holz.g, holz.b, 0f);
                }
            string pfad = Folder + "/WW_Muehle_Gitter_Tex.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(pfad);
            bool neu = tex == null;
            if (neu) tex = new Texture2D(W, H, TextureFormat.RGBA32, true, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.SetPixels(px);
            tex.Apply(true, false);
            if (neu) AssetDatabase.CreateAsset(tex, pfad); else EditorUtility.SetDirty(tex);
            return tex;
        }
    }
}
