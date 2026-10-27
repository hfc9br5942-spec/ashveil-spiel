using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Eichen als Überhälter in den Hecken der Westweiden (Lückenliste Punkt 6, 13.10.2026, VORSCHLAG): eigenes Modell per Code
    /// (ART-099), lesbar als Silhouette über der Heckenflur – senkrechte Marken in einer weiten, flachen Landschaft.
    /// <list type="bullet">
    /// <item><b>Stamm</b> 3,5–4,5 m bis zur Krone, kräftig (r ≈ 0,5 m), dann drei bis vier <b>Hauptäste</b> schräg nach außen.</item>
    /// <item><b>Krone</b> breit und gewölbt (Eichenform, 12–16 m Ø): Laubbüschel als je drei gekreuzte Karten mit gemalter
    /// Laubtextur, Normalen von der Kronenmitte nach außen (weiche, runde Schattierung wie eine Wolke) und Foliage-Shader (Wind).</item>
    /// </list>
    /// Drei Varianten, zwei Detailstufen (LODGroup), Kapsel-Kollider am Stamm. Ergebnis: <c>Art/Westweiden/Pflanzen/Westweiden_Eiche_{0..2}.prefab</c>.
    /// </summary>
    public static class EicheGenerator
    {
        private const string Folder = ProjectSetup.Root + "/Art/Westweiden/Pflanzen";
        private const string Fassung = "Eiche v1 (13.10.2026)";
        public const int Varianten = 3;

        public static GameObject Vorlage(int variante) => Erzeugen(Mathf.Abs(variante) % Varianten, false);

        [MenuItem("Ashveil/Welt/Westweiden: Eichen neu erzeugen", priority = 571)]
        private static void Menue()
        {
            for (int i = 0; i < Varianten; i++) Erzeugen(i, true);
            Debug.Log("[Ashveil] Eichen neu erzeugt (" + Varianten + " Varianten).");
        }

        private static string Pfad(string name) => $"{Folder}/{name}";

        private struct Ast { public Vector3 a, b; public float ra, rb; }

        private static GameObject Erzeugen(int v, bool neu)
        {
            string prefabPfad = Pfad($"Westweiden_Eiche_{v}.prefab");
            var vorhanden = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPfad);
            if (vorhanden != null && !neu)
            {
                var mf = vorhanden.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name.StartsWith(Fassung)) return vorhanden;
            }
            Shader foliage = Shader.Find("Ashveil/Foliage"), lit = Shader.Find("Universal Render Pipeline/Lit");
            if (foliage == null || lit == null) return null;
            ProjectSetup.EnsureFolder(Folder);
            Material rinde = AssetDatabase.LoadAssetAtPath<Material>(Pfad("Westweiden_Kopfweide_Rinde.mat"));
            if (rinde == null) { KopfweideGenerator.Vorlage(0); rinde = AssetDatabase.LoadAssetAtPath<Material>(Pfad("Westweiden_Kopfweide_Rinde.mat")); }
            if (rinde == null) return null;
            Material laub = Laub(foliage);

            // Gerüst: Stamm + Hauptäste (gleich in beiden Stufen)
            var rng = new System.Random(3301 + v * 97);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float stamm = R(3.5f, 4.5f), r0 = R(0.45f, 0.55f);
            var aeste = new List<Ast> { new Ast { a = new Vector3(0f, -0.4f, 0f), b = new Vector3(R(-0.3f, 0.3f), stamm, R(-0.3f, 0.3f)), ra = r0 * 1.15f, rb = r0 * 0.85f } };
            Vector3 gabel = aeste[0].b;
            int n = 3 + (v % 2);
            float dreh = R(0f, 360f);
            var kronenPunkte = new List<Vector3>();
            for (int i = 0; i < n; i++)
            {
                float ang = (dreh + i * 360f / n + R(-20f, 20f)) * Mathf.Deg2Rad;
                float weit = R(3.2f, 4.6f), hoch = R(3.0f, 4.5f);
                Vector3 ende = gabel + new Vector3(Mathf.Cos(ang) * weit, hoch, Mathf.Sin(ang) * weit);
                aeste.Add(new Ast { a = gabel, b = ende, ra = r0 * 0.6f, rb = r0 * 0.22f });
                kronenPunkte.Add(ende);
            }
            float kronenR = R(6.0f, 7.5f);
            Vector3 kronenMitte = gabel + new Vector3(0f, R(4.5f, 5.5f), 0f);

            Mesh m0 = Netz(v, 0, aeste, kronenMitte, kronenR, 42, 10);
            Mesh m1 = Netz(v, 1, aeste, kronenMitte, kronenR, 16, 6);
            var root = new GameObject($"Westweiden_Eiche_{v}");
            try
            {
                var l0 = Teil(root.transform, "LOD0", m0, rinde, laub);
                var l1 = Teil(root.transform, "LOD1", m1, rinde, laub);
                var lod = root.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(0.18f, new Renderer[] { l0 }), new LOD(0.006f, new Renderer[] { l1 }) });   // groß: bis ≈ 2 km sichtbar
                lod.RecalculateBounds();
                var cap = root.AddComponent<CapsuleCollider>();
                cap.direction = 1; cap.radius = r0 * 1.1f; cap.height = stamm + 1f; cap.center = new Vector3(0f, (stamm + 1f) * 0.5f, 0f);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPfad, out bool ok);
                return ok ? prefab : null;
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static MeshRenderer Teil(Transform parent, string name, Mesh mesh, Material rinde, Material laub)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { rinde, laub };
            return mr;
        }

        private static Mesh Netz(int v, int stufe, List<Ast> aeste, Vector3 kronenMitte, float kronenR, int bueschel, int segmente)
        {
            var vert = new List<Vector3>(); var norm = new List<Vector3>(); var uv = new List<Vector2>();
            var triHolz = new List<int>(); var triLaub = new List<int>();

            // Holz: je Ast ein Kegelstumpf mit leichtem Knick in der Mitte
            foreach (Ast ast in aeste)
            {
                const int Ringe = 4;
                Vector3 achse = ast.b - ast.a;
                Vector3 quer1 = Vector3.Cross(achse.normalized, Mathf.Abs(achse.normalized.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                Vector3 quer2 = Vector3.Cross(achse.normalized, quer1);
                int basis = vert.Count;
                for (int r = 0; r <= Ringe; r++)
                {
                    float t = r / (float)Ringe;
                    Vector3 c = ast.a + achse * t + quer1 * (Mathf.Sin(t * Mathf.PI) * achse.magnitude * 0.04f);
                    float rad = Mathf.Lerp(ast.ra, ast.rb, t);
                    for (int s = 0; s <= segmente; s++)
                    {
                        float a = s / (float)segmente * Mathf.PI * 2f;
                        Vector3 d = quer1 * Mathf.Cos(a) + quer2 * Mathf.Sin(a);
                        vert.Add(c + d * rad * (1f + 0.08f * Mathf.Sin(a * 3f + t * 5f)));
                        norm.Add(d);
                        uv.Add(new Vector2(s / (float)segmente * 2f, (ast.a.y + achse.y * t) * 0.6f));
                    }
                }
                for (int r = 0; r < Ringe; r++)
                    for (int s = 0; s < segmente; s++)
                    {
                        int a = basis + r * (segmente + 1) + s, b = a + segmente + 1;
                        triHolz.Add(a); triHolz.Add(b); triHolz.Add(a + 1);
                        triHolz.Add(a + 1); triHolz.Add(b); triHolz.Add(b + 1);
                    }
            }

            // Krone: Büschel auf einer abgeflachten Halbkugel (Eiche: breit, oben gewölbt, unten offen) und an den Astenden
            var rr = new System.Random(808 + v * 13);
            float Q(float a, float b) => a + (float)rr.NextDouble() * (b - a);
            int alle = 42;
            for (int j = 0; j < alle; j++)
            {
                // fester Ablauf aller Zufallszahlen, damit LOD1 dieselben (größeren) Büschel an denselben Stellen trägt
                float th = Q(0f, Mathf.PI * 2f), ph = Mathf.Acos(Q(-0.35f, 1f)), rad = Q(0.7f, 1.0f) * kronenR;
                float groesse = Q(3.0f, 4.4f), yaw = Q(0f, 180f);
                if (stufe == 1 && j % 3 != 0) continue;
                if (stufe == 0 && j >= bueschel) continue;
                Vector3 dir = new Vector3(Mathf.Sin(ph) * Mathf.Cos(th), Mathf.Cos(ph) * 0.62f, Mathf.Sin(ph) * Mathf.Sin(th));
                Vector3 p = kronenMitte + dir * rad;
                float g = groesse * (stufe == 1 ? 1.6f : 1f);
                for (int kk = 0; kk < 3; kk++)
                {
                    // drei gekreuzte Karten: zwei senkrecht, eine liegend (sonst wirkt die Krone von oben leer)
                    Vector3 rechts, oben;
                    if (kk < 2)
                    {
                        float y = (yaw + kk * 90f) * Mathf.Deg2Rad;
                        rechts = new Vector3(Mathf.Cos(y), 0f, Mathf.Sin(y)); oben = Vector3.up;
                    }
                    else { rechts = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)); oben = new Vector3(-Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)); }
                    int basis = vert.Count;
                    for (int q = 0; q < 4; q++)
                    {
                        float sx = (q & 1) == 0 ? -0.5f : 0.5f, sy = q < 2 ? -0.5f : 0.5f;
                        Vector3 pos = p + rechts * sx * g + oben * sy * g;
                        vert.Add(pos);
                        // Normale von der Kronenmitte nach außen (weiche, wolkige Schattierung), unten etwas nach oben gekippt
                        Vector3 nn = (pos - kronenMitte).normalized;
                        norm.Add((nn + Vector3.up * 0.25f).normalized);
                        uv.Add(new Vector2(sx + 0.5f, sy + 0.5f));
                    }
                    triLaub.Add(basis); triLaub.Add(basis + 2); triLaub.Add(basis + 1);
                    triLaub.Add(basis + 1); triLaub.Add(basis + 2); triLaub.Add(basis + 3);
                }
            }

            string pfad = Pfad($"Westweiden_Eiche_{v}_LOD{stufe}.asset");
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(pfad);
            bool neu = mesh == null;
            if (neu) mesh = new Mesh();
            mesh.Clear();
            mesh.name = $"{Fassung} {v} LOD{stufe}";
            mesh.SetVertices(vert); mesh.SetNormals(norm); mesh.SetUVs(0, uv);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(triHolz, 0);
            mesh.SetTriangles(triLaub, 1);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            if (neu) AssetDatabase.CreateAsset(mesh, pfad); else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static Material Laub(Shader foliage)
        {
            string pfad = Pfad("Westweiden_Eiche_Laub.mat");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(pfad);
            bool neu = mat == null;
            if (neu) mat = new Material(foliage); else mat.shader = foliage;
            mat.SetTexture("_BaseMap", LaubTextur());
            mat.SetColor("_BaseColor", new Color(0.9f, 0.94f, 0.88f, 1f));
            mat.SetFloat("_Cutoff", 0.5f);
            mat.SetFloat("_AlphaClip", 1f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_WindStrength", 0.12f);
            mat.SetFloat("_WindSpeed", 0.8f);
            mat.SetFloat("_SwayHeight", 16f);
            mat.SetFloat("_Translucency", 0.35f);
            mat.SetFloat("_NormalUp", 0.15f);   // Normalen kommen aus der Kronenform
            if (mat.HasProperty("_NearFade")) mat.SetFloat("_NearFade", 0.6f);
            if (mat.HasProperty("_VarColor")) mat.SetColor("_VarColor", new Color(0.86f, 0.9f, 0.8f, 1f));
            if (mat.HasProperty("_VarScale")) mat.SetFloat("_VarScale", 40f);
            if (mat.HasProperty("_RootDark")) mat.SetFloat("_RootDark", 0.3f);
            mat.enableInstancing = true;
            if (neu) AssetDatabase.CreateAsset(mat, pfad); else EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Laubbüschel: runder, ausgefranster Fleck aus vielen gelappten Eichenblättern, innen dunkler, Rand heller.</summary>
        private static Texture2D LaubTextur()
        {
            const int W = 256, H = 256;
            var px = new Color[W * H];
            var rng = new System.Random(1212);
            var blaetter = new List<(Vector2 p, float ang, float l, float b, float ton)>();
            for (int i = 0; i < 260; i++)
            {
                // Blätter dicht in einer Scheibe (r ≤ 0,44), am Rand lockerer
                float r = Mathf.Sqrt((float)rng.NextDouble()) * 0.44f, a = (float)rng.NextDouble() * Mathf.PI * 2f;
                blaetter.Add((new Vector2(0.5f + Mathf.Cos(a) * r, 0.5f + Mathf.Sin(a) * r), (float)rng.NextDouble() * Mathf.PI * 2f,
                    0.075f + (float)rng.NextDouble() * 0.035f, 0.032f + (float)rng.NextDouble() * 0.012f, (float)rng.NextDouble()));
            }
            var dunkel = new Color(0.20f, 0.30f, 0.12f);
            var mittel = new Color(0.32f, 0.44f, 0.18f);
            var hell = new Color(0.48f, 0.58f, 0.26f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var p = new Vector2(x / (float)(W - 1), y / (float)(H - 1));
                    float best = 0f; Color col = dunkel;
                    foreach (var b in blaetter)
                    {
                        Vector2 q = p - b.p;
                        float c = Mathf.Cos(b.ang), s = Mathf.Sin(b.ang);
                        float lx = q.x * c + q.y * s, ly = -q.x * s + q.y * c;
                        if (lx < -b.l * 0.5f || lx > b.l * 0.5f) continue;
                        float t = lx / b.l + 0.5f;
                        // gelappter Rand (Eichenblatt): Breite schwankt mit vier Buchten
                        float w = b.b * Mathf.Sin(t * Mathf.PI) * (0.75f + 0.25f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 4f)));
                        float a = Mathf.Clamp01((w - Mathf.Abs(ly)) / 0.004f);
                        if (a > best)
                        {
                            best = a;
                            float rand = Vector2.Distance(b.p, new Vector2(0.5f, 0.6f)) / 0.44f;   // oben/außen heller
                            col = Color.Lerp(Color.Lerp(dunkel, mittel, b.ton), hell, Mathf.Clamp01(rand * 0.8f + (b.p.y - 0.5f) * 0.6f) * b.ton);
                        }
                    }
                    px[y * W + x] = new Color(col.r, col.g, col.b, best);
                }
            string pfad = Pfad("Westweiden_Eiche_Laub_Tex.asset");
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(pfad);
            bool neu = tex == null;
            if (neu) tex = new Texture2D(W, H, TextureFormat.RGBA32, true, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            tex.SetPixels(px);
            tex.Apply(true, false);
            if (neu) AssetDatabase.CreateAsset(tex, pfad); else EditorUtility.SetDirty(tex);
            return tex;
        }
    }
}
