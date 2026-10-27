using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Kopfweiden der Westweiden (Lückenliste Punkt 6, 13.10.2026; Unikat WORLD-224: „Gräben mit Kopfweiden im Talboden“):
    /// eigenes Modell per Code statt Greybox (Zylinder + Kugel). Keine fremden Inhalte (ART-099), Texturen gemalt per Code.
    /// <list type="bullet">
    /// <item><b>Stamm</b> 2,0–2,8 m, dick (r ≈ 0,4 m), leicht schief, knorrig (Wülste), oben der verdickte <b>Kopf</b>, aus dem
    /// gekappt wird – das Erkennungszeichen der Kopfweide.</item>
    /// <item><b>Ruten</b> 1,6–3,0 m, aufrecht bis leicht ausladend, als gekreuzte Blattkarten mit schmalen Weidenblättern
    /// (oben grün, unten silbrig) und dem Foliage-Shader (Wind, beidseitig).</item>
    /// </list>
    /// Drei Varianten, je zwei Detailstufen (LODGroup; die zweite mit halb so vielen Ruten), Kapsel-Kollider am Stamm.
    /// Ergebnis: <c>Art/Westweiden/Pflanzen/Westweiden_Kopfweide_{0..2}.prefab</c> (Netze, Texturen, Materialien daneben).
    /// Kosten je Baum LOD0 ≈ 1 100 Dreiecke; 89 Bäume in der Zelle.
    /// </summary>
    public static class KopfweideGenerator
    {
        private const string Folder = ProjectSetup.Root + "/Art/Westweiden/Pflanzen";
        private const string Fassung = "Kopfweide v2 (13.10.2026)";
        public const int Varianten = 3;

        /// <summary>Vorlage der Variante laden oder erzeugen (null, wenn etwas fehlt – dann baut der Aufrufer die Greybox).</summary>
        public static GameObject Vorlage(int variante) => Erzeugen(Mathf.Abs(variante) % Varianten, false);

        [MenuItem("Ashveil/Welt/Westweiden: Kopfweiden neu erzeugen", priority = 570)]
        private static void Menue()
        {
            for (int i = 0; i < Varianten; i++) Erzeugen(i, true);
            Debug.Log("[Ashveil] Kopfweiden neu erzeugt (" + Varianten + " Varianten).");
        }

        private static string Pfad(string name) => $"{Folder}/{name}";

        private static GameObject Erzeugen(int v, bool neu)
        {
            string prefabPfad = Pfad($"Westweiden_Kopfweide_{v}.prefab");
            var vorhanden = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPfad);
            if (vorhanden != null && !neu)
            {
                var mf = vorhanden.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name.StartsWith(Fassung)) return vorhanden;
            }
            Shader foliage = Shader.Find("Ashveil/Foliage"), lit = Shader.Find("Universal Render Pipeline/Lit");
            if (foliage == null || lit == null) return null;
            ProjectSetup.EnsureFolder(Folder);
            Material rinde = Rinde(lit), laub = Laub(foliage);
            var rng = new System.Random(9001 + v * 131);
            float hs = 2.1f + 0.3f * v + (float)rng.NextDouble() * 0.2f;          // Stammhöhe
            float r0 = 0.36f + 0.04f * v;
            var lean = new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f).normalized * (0.15f + 0.15f * (float)rng.NextDouble());
            Mesh m0 = Netz(v, 0, hs, r0, lean, 44, 12, 9);
            Mesh m1 = Netz(v, 1, hs, r0, lean, 22, 7, 5);
            var root = new GameObject($"Westweiden_Kopfweide_{v}");
            try
            {
                var l0 = Teil(root.transform, "LOD0", m0, rinde, laub);
                var l1 = Teil(root.transform, "LOD1", m1, rinde, laub);
                var lod = root.AddComponent<LODGroup>();
                lod.SetLODs(new[]
                {
                    new LOD(0.12f, new Renderer[] { l0 }),
                    new LOD(0.004f, new Renderer[] { l1 })   // bis ≈ 1 km sichtbar (offene Talböden, Blick vom Kammsattel)
                });
                lod.RecalculateBounds();
                var cap = root.AddComponent<CapsuleCollider>();
                cap.direction = 1;
                cap.radius = r0 * 1.1f;
                cap.height = hs + 0.4f;
                cap.center = new Vector3(lean.x * 0.5f, (hs + 0.4f) * 0.5f, lean.y * 0.5f);
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
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return mr;
        }

        // ------------------------------------------------------------------ Netz: Stamm mit Kopf (Untermenge 0), Ruten (Untermenge 1)

        private static Mesh Netz(int v, int stufe, float hs, float r0, Vector2 lean, int ruten, int segmente, int ringe)
        {
            var vert = new List<Vector3>(); var norm = new List<Vector3>(); var uv = new List<Vector2>();
            var triStamm = new List<int>(); var triLaub = new List<int>();
            var rng = new System.Random(500 + v * 37);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            // Stamm: Ringe vom Boden (−0,3 m, steckt im Boden) bis zum Kopf; Wülste aus zwei festen Sinusmustern je Variante
            float p1 = R(0f, 6.28f), p2 = R(0f, 6.28f);
            float kopfR = r0 * 1.45f, kopfH = 0.55f;
            int nRing = ringe + 3;
            for (int i = 0; i <= nRing; i++)
            {
                float t = i / (float)nRing;
                float y = Mathf.Lerp(-0.3f, hs + kopfH, t);
                float k = Mathf.Clamp01((y - (hs - 0.35f)) / (kopfH + 0.35f));                 // 0 Stamm … 1 Kopfoberseite
                float r = Mathf.Lerp(r0 * 1.12f, r0 * 0.92f, Mathf.Clamp01(y / hs));            // Fuß breiter
                r = Mathf.Lerp(r, kopfR, Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, k * 2.2f)));     // Kopf verdickt
                if (k > 0.6f) r *= Mathf.Sqrt(Mathf.Max(0.05f, 1f - (k - 0.6f) / 0.4f * 0.85f)); // Kopf oben gewölbt
                Vector2 off = lean * Mathf.Pow(Mathf.Clamp01(y / hs), 1.5f);
                for (int s = 0; s <= segmente; s++)
                {
                    float a = s / (float)segmente * Mathf.PI * 2f;
                    float wulst = 1f + 0.14f * Mathf.Sin(a * 3f + p1 + y * 1.7f) + 0.08f * Mathf.Sin(a * 5f + p2 - y * 2.9f) + 0.05f * Mathf.Sin(a * 9f + y * 6.1f);
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    vert.Add(new Vector3(off.x, y, off.y) + dir * r * wulst);
                    norm.Add((dir + Vector3.up * (k > 0.6f ? 1.2f : 0.05f)).normalized);
                    uv.Add(new Vector2(s / (float)segmente * 2f, y * 0.8f));
                }
            }
            for (int i = 0; i < nRing; i++)
                for (int s = 0; s < segmente; s++)
                {
                    int a = i * (segmente + 1) + s, b = a + segmente + 1;
                    triStamm.Add(a); triStamm.Add(b); triStamm.Add(a + 1);
                    triStamm.Add(a + 1); triStamm.Add(b); triStamm.Add(b + 1);
                }
            // Deckel (Schnittfläche oben)
            int mitte = vert.Count;
            Vector2 offTop = lean;
            vert.Add(new Vector3(offTop.x, hs + kopfH + 0.02f, offTop.y)); norm.Add(Vector3.up); uv.Add(new Vector2(0.5f, (hs + kopfH) * 0.8f));
            int letzter = nRing * (segmente + 1);
            for (int s = 0; s < segmente; s++) { triStamm.Add(mitte); triStamm.Add(letzter + s + 1); triStamm.Add(letzter + s); }

            // Ruten: aus der Kopfoberseite, aufrecht bis ausladend, je Rute zwei gekreuzte Karten (4 Abschnitte)
            var rr = new System.Random(77 + v * 11);   // gleiche Ruten in beiden Stufen, LOD1 nimmt jede zweite
            float Q(float a, float b) => a + (float)rr.NextDouble() * (b - a);
            int alle = 44;
            for (int j = 0; j < alle; j++)
            {
                float ang = Q(0f, Mathf.PI * 2f), rad = Mathf.Sqrt(Q(0.05f, 1f)) * kopfR * 0.85f;
                float laenge = Q(1.5f, 2.9f), aus = Q(0.12f, 0.65f), breite = Q(0.75f, 1.15f);
                float yawK = Q(0f, 180f);
                if (stufe == 1 && (j & 1) == 1) continue;
                if (stufe == 0 && j >= ruten) continue;
                var fuss = new Vector3(lean.x + Mathf.Cos(ang) * rad, hs + kopfH * 0.35f, lean.y + Mathf.Sin(ang) * rad);
                var raus = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                for (int kk = 0; kk < (stufe == 0 ? 2 : 1); kk++)
                {
                    float yaw = (yawK + kk * 90f) * Mathf.Deg2Rad;
                    var quer = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
                    int basis = vert.Count;
                    const int Abs = 4;
                    for (int r = 0; r <= Abs; r++)
                    {
                        float t = r / (float)Abs;
                        // Bogen: steigt auf, neigt sich oben nach außen (Weidenruten überhängen leicht)
                        Vector3 p = fuss + Vector3.up * (laenge * t) + raus * (laenge * aus * t * t);
                        float halb = breite * 0.5f * Mathf.Lerp(0.55f, 1f, Mathf.Sin(t * Mathf.PI * 0.85f + 0.2f));
                        for (int sd = 0; sd < 2; sd++)
                        {
                            vert.Add(p + quer * (sd == 0 ? -halb : halb));
                            norm.Add((Vector3.up * 0.7f + raus * 0.5f).normalized);
                            uv.Add(new Vector2(sd, t));
                        }
                    }
                    for (int r = 0; r < Abs; r++)
                    {
                        int b = basis + r * 2;
                        triLaub.Add(b); triLaub.Add(b + 2); triLaub.Add(b + 1);
                        triLaub.Add(b + 1); triLaub.Add(b + 2); triLaub.Add(b + 3);
                    }
                }
            }

            string pfad = Pfad($"Westweiden_Kopfweide_{v}_LOD{stufe}.asset");
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(pfad);
            bool neu = mesh == null;
            if (neu) mesh = new Mesh();
            mesh.Clear();
            mesh.name = $"{Fassung} {v} LOD{stufe}";
            mesh.SetVertices(vert); mesh.SetNormals(norm); mesh.SetUVs(0, uv);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(triStamm, 0);
            mesh.SetTriangles(triLaub, 1);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            if (neu) AssetDatabase.CreateAsset(mesh, pfad); else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // ------------------------------------------------------------------ Materialien und gemalte Texturen

        private static Material Rinde(Shader lit)
        {
            string pfad = Pfad("Westweiden_Kopfweide_Rinde.mat");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(pfad);
            bool neu = mat == null;
            if (neu) mat = new Material(lit);
            mat.SetTexture("_BaseMap", RindenTextur());
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.12f);
            mat.enableInstancing = true;
            if (neu) AssetDatabase.CreateAsset(mat, pfad); else EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material Laub(Shader foliage)
        {
            string pfad = Pfad("Westweiden_Kopfweide_Laub.mat");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(pfad);
            bool neu = mat == null;
            if (neu) mat = new Material(foliage); else mat.shader = foliage;
            mat.SetTexture("_BaseMap", LaubTextur());
            mat.SetColor("_BaseColor", new Color(0.92f, 0.95f, 0.9f, 1f));
            mat.SetFloat("_Cutoff", 0.45f);
            mat.SetFloat("_AlphaClip", 1f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_WindStrength", 0.16f);
            mat.SetFloat("_WindSpeed", 1.1f);
            mat.SetFloat("_SwayHeight", 5.5f);
            mat.SetFloat("_Translucency", 0.45f);
            mat.SetFloat("_NormalUp", 0.55f);
            if (mat.HasProperty("_NearFade")) mat.SetFloat("_NearFade", 0.6f);
            if (mat.HasProperty("_VarColor")) mat.SetColor("_VarColor", new Color(0.86f, 0.92f, 0.82f, 1f));
            if (mat.HasProperty("_VarScale")) mat.SetFloat("_VarScale", 30f);
            if (mat.HasProperty("_RootDark")) mat.SetFloat("_RootDark", 0.25f);
            mat.enableInstancing = true;
            if (neu) AssetDatabase.CreateAsset(mat, pfad); else EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Rinde: graubraun, tiefe senkrechte Furchen, ein Hauch Grün (Algen) unten.</summary>
        private static Texture2D RindenTextur()
        {
            const int W = 256, H = 256;
            var px = new Color[W * H];
            var hell = new Color(0.56f, 0.51f, 0.44f);
            var dunkel = new Color(0.26f, 0.23f, 0.20f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)W, v = y / (float)H;
                    float furche = Mathf.PerlinNoise(u * 18f, v * 2.2f) * 0.7f + Mathf.PerlinNoise(u * 41f + 3f, v * 5f) * 0.3f;
                    furche = Mathf.SmoothStep(0.3f, 0.7f, furche);
                    float korn = Mathf.PerlinNoise(u * 90f, v * 60f) * 0.12f;
                    Color c = Color.Lerp(dunkel, hell, furche + korn);
                    float alge = Mathf.Clamp01(Mathf.PerlinNoise(u * 6f + 9f, v * 4f) - 0.55f) * 1.4f;
                    c = Color.Lerp(c, new Color(0.30f, 0.36f, 0.22f), alge * 0.5f);
                    px[y * W + x] = c;
                }
            return Speichern("Westweiden_Kopfweide_Rinde_Tex.asset", px, W, H, TextureWrapMode.Repeat, false);
        }

        /// <summary>Rute mit schmalen Weidenblättern: dünner Stängel, Blätter wechselständig schräg nach oben, Oberseite grün, Unterseite silbrig.</summary>
        private static Texture2D LaubTextur()
        {
            const int W = 256, H = 512;
            var px = new Color[W * H];
            var rng = new System.Random(4242);
            var blaetter = new List<(Vector2 a, Vector2 d, float l, float b, bool silber)>();
            // drei Ruten je Karte nebeneinander, dicht beblättert ab 15 % Höhe
            float[] ruten = { 0.12f, 0.31f, 0.5f, 0.69f, 0.88f };
            foreach (float ru in ruten)
                for (float t = 0.04f + ru * 0.05f; t < 0.985f; t += 0.026f)
                {
                    float seite = blaetter.Count % 2 == 0 ? -1f : 1f;
                    float x = ru + Mathf.Sin(t * 3f + ru * 7f) * 0.02f;
                    float winkel = (35f + (float)rng.NextDouble() * 25f) * Mathf.Deg2Rad;
                    var d = new Vector2(Mathf.Sin(winkel) * seite, Mathf.Cos(winkel));
                    blaetter.Add((new Vector2(x, t), d, 0.15f + (float)rng.NextDouble() * 0.08f, 0.028f + (float)rng.NextDouble() * 0.01f, rng.NextDouble() < 0.3));
                }
            var gruen = new Color(0.34f, 0.46f, 0.22f);
            var gruenHell = new Color(0.52f, 0.62f, 0.30f);
            var silber = new Color(0.62f, 0.68f, 0.56f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var p = new Vector2(x / (float)(W - 1), y / (float)(H - 1));
                    float best = 0f; Color col = gruen;
                    foreach (float ru in ruten)
                    {
                        float sx = ru + Mathf.Sin(p.y * 3f + ru * 7f) * 0.02f;
                        float a = Mathf.Clamp01((0.006f - Mathf.Abs(p.x - sx)) / 0.004f);
                        if (a > best) { best = a; col = new Color(0.36f, 0.30f, 0.20f); }
                    }
                    foreach (var b in blaetter)
                    {
                        // Blatt als schmale Lanzette entlang d (Seitenmaß 1:2 der Textur ausgeglichen)
                        Vector2 q = p - b.a; q.y *= 3.8f;   // Karte ≈ 0,6 × 2,3 m: v-Einheiten in u-Einheiten der Welt
                        float s = Vector2.Dot(q, b.d);
                        if (s < 0f || s > b.l) continue;
                        float quer = Mathf.Abs(q.x * b.d.y - q.y * b.d.x);
                        float w = b.b * Mathf.Sin(Mathf.Clamp01(s / b.l) * Mathf.PI);
                        float a = Mathf.Clamp01((w - quer) / 0.004f);
                        if (a > best)
                        {
                            best = a;
                            float hell = s / b.l;
                            col = b.silber ? silber : Color.Lerp(gruen, gruenHell, hell * 0.6f + 0.2f * Mathf.PerlinNoise(b.a.x * 30f, b.a.y * 30f));
                        }
                    }
                    px[y * W + x] = new Color(col.r, col.g, col.b, best);
                }
            return Speichern("Westweiden_Kopfweide_Laub_Tex.asset", px, W, H, TextureWrapMode.Clamp, true, 0.45f);
        }

        /// <summary>
        /// Mip-Stufen selbst rechnen und je Stufe das Alpha so skalieren, dass der Anteil über der Schwelle wie in Stufe 0 bleibt
        /// (Coverage-Erhalt) – sonst dünnen schmale Blätter mit der Entfernung aus und die Krone verschwindet.
        /// </summary>
        private static void Abdeckung(Texture2D tex, Color[] stufe0, int w, int h, float cutoff)
        {
            float Anteil(Color[] p, float sk) { int n = 0; foreach (Color c in p) if (c.a * sk > cutoff) n++; return n / (float)p.Length; }
            float ziel = Anteil(stufe0, 1f);
            Color[] vorher = stufe0;
            int pw = w, ph = h;
            for (int m = 1; m < tex.mipmapCount; m++)
            {
                int nw = Mathf.Max(1, pw / 2), nh = Mathf.Max(1, ph / 2);
                var neu = new Color[nw * nh];
                for (int y = 0; y < nh; y++)
                    for (int x = 0; x < nw; x++)
                    {
                        Color s = Color.clear;
                        float a = 0f;
                        for (int dy = 0; dy < 2; dy++)
                            for (int dx = 0; dx < 2; dx++)
                            {
                                Color c = vorher[Mathf.Min(ph - 1, y * 2 + dy) * pw + Mathf.Min(pw - 1, x * 2 + dx)];
                                s += new Color(c.r * c.a, c.g * c.a, c.b * c.a, 0f);
                                a += c.a;
                            }
                        neu[y * nw + x] = a > 0f ? new Color(s.r / a, s.g / a, s.b / a, a * 0.25f) : new Color(0.3f, 0.4f, 0.2f, 0f);
                    }
                // Skalierung per Halbierung suchen
                float lo = 1f, hi = 8f;
                for (int it = 0; it < 14; it++) { float mid = (lo + hi) * 0.5f; if (Anteil(neu, mid) < ziel) lo = mid; else hi = mid; }
                var gesetzt = new Color[neu.Length];
                for (int i = 0; i < neu.Length; i++) gesetzt[i] = new Color(neu[i].r, neu[i].g, neu[i].b, Mathf.Clamp01(neu[i].a * hi));
                tex.SetPixels(gesetzt, m);
                vorher = neu;
                pw = nw; ph = nh;
            }
            tex.Apply(false, false);
        }

        private static Texture2D Speichern(string name, Color[] px, int w, int h, TextureWrapMode wrap, bool alpha, float cutoff = 0f)
        {
            string pfad = Pfad(name);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(pfad);
            bool neu = tex == null;
            if (neu) tex = new Texture2D(w, h, TextureFormat.RGBA32, true, false);
            else if (tex.width != w || tex.height != h) tex.Reinitialize(w, h);
            tex.wrapMode = wrap;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            tex.SetPixels(px);
            if (alpha && cutoff > 0f) Abdeckung(tex, px, w, h, cutoff);   // Laub verschwindet sonst in der Ferne (Mip-Alpha)
            else tex.Apply(true, false);
            if (neu) AssetDatabase.CreateAsset(tex, pfad); else EditorUtility.SetDirty(tex);
            return tex;
        }
    }
}
