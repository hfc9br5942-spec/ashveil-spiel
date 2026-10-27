using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Feldpflanzen für die Idylle um Untersteg (WORLD-244; Max 10.10.2026: „Korn, Flachs und Heu“): zwei Terrain-Details nach dem
    /// Vorbild der Grasfläche (<see cref="GrasPatchGenerator"/>) – ein Fleck aus aufrechten Halmkarten mit gemalter Alpha-Textur
    /// (keine fremden Inhalte, ART-099) und dem Foliage-Shader:
    /// <list type="bullet">
    /// <item><b>Korn</b>: 0,9–1,2 m, goldgelb, oben die Ähre (dicker, körnig), leicht nickend.</item>
    /// <item><b>Flachs</b>: 0,6–0,85 m, dünne blaugrüne Stängel mit kleinen blauen Blüten oben.</item>
    /// </list>
    /// Ergebnis unter <c>Terrain/</c>: <c>KarethV4_KornPatch.prefab</c>, <c>KarethV4_FlachsPatch.prefab</c> (Netz, Textur, Material daneben).
    /// Kosten je Fleck: 7 Karten × 4 Segmente = 56 Dreiecke; Instancing; kein Schattenwurf (wie das Gras).
    /// </summary>
    public static class FeldPatchGenerator
    {
        private const string Folder = ProjectSetup.Root + "/Terrain";
        private const string FassungV1 = "FeldPatch v1 (10.10.2026)";

        private sealed class Art
        {
            public string Name;
            public float HoeheMin, HoeheMax, BiegungMin, BiegungMax, BreiteMin, BreiteMax;
            public Color Wurzel, Spitze, Blute, BaseColor, VarColor;
            public int Seed;
            public bool Aehre;          // Korn: dicke Ähre oben; sonst kleine Blüten
            // Westweiden (13.10.2026): Dateiname, Blütenform und Fassung je Art; Vorgaben = Korn/Flachs wie bisher
            public string Praefix = "KarethV4_";
            public string Fassung = FassungV1;
            public int Halme;            // 0 = 5 (Ähre) bzw. 6
            public int Blueten = 1;      // Blüten je Stängel (von BluetenAb bis zur Spitze verteilt)
            public float BluetenAb = 0.82f, BluetenRadius = 0.032f, BluetenAnteil = 1f;
            public float AehreBreite = 2.6f;
            public float KopfAnteil = 1f; // Ähre/Kolben nur an diesem Anteil der Halme (Rest: schlanke Blätter)
            public float Wind = 0f;      // 0 = wie bisher (Ähre 0,09, sonst 0,07)
        }

        private static readonly Art KornArt = new Art
        {
            Name = "Korn", HoeheMin = 0.62f, HoeheMax = 0.8f, BiegungMin = 0.04f, BiegungMax = 0.12f, BreiteMin = 0.3f, BreiteMax = 0.42f,
            Wurzel = new Color(0.47f, 0.43f, 0.2f), Spitze = new Color(0.9f, 0.76f, 0.4f), Blute = new Color(0.83f, 0.66f, 0.33f),
            BaseColor = new Color(0.92f, 0.9f, 0.82f, 1f), VarColor = new Color(0.95f, 0.85f, 0.62f, 1f), Seed = 31, Aehre = true
        };

        private static readonly Art FlachsArt = new Art
        {
            Name = "Flachs", HoeheMin = 0.5f, HoeheMax = 0.66f, BiegungMin = 0.03f, BiegungMax = 0.1f, BreiteMin = 0.28f, BreiteMax = 0.4f,
            Wurzel = new Color(0.28f, 0.4f, 0.26f), Spitze = new Color(0.47f, 0.6f, 0.44f), Blute = new Color(0.42f, 0.55f, 0.92f),
            BaseColor = new Color(0.86f, 0.9f, 0.86f, 1f), VarColor = new Color(0.82f, 0.9f, 0.8f, 1f), Seed = 47, Aehre = false
        };

        // ------------------------------------------------------------------ Westweiden (Lückenliste Punkt 7, 13.10.2026)
        /// <summary>Röhricht am Mühlbach: hohe Halme mit braunem Kolben (Rohrkolben), 1,5–2 m mit der Prototyp-Größe.</summary>
        private static readonly Art RoehrichtArt = new Art
        {
            Name = "Roehricht", Praefix = "Westweiden_", Fassung = "WW-Pflanzen v1 (13.10.2026)",
            HoeheMin = 0.85f, HoeheMax = 1f, BiegungMin = 0.03f, BiegungMax = 0.1f, BreiteMin = 0.26f, BreiteMax = 0.36f,
            Wurzel = new Color(0.24f, 0.33f, 0.16f), Spitze = new Color(0.58f, 0.62f, 0.34f), Blute = new Color(0.36f, 0.23f, 0.13f),
            BaseColor = new Color(0.9f, 0.92f, 0.86f, 1f), VarColor = new Color(0.85f, 0.88f, 0.7f, 1f), Seed = 61, Aehre = true,
            Halme = 7, AehreBreite = 1.9f, KopfAnteil = 0.45f, Wind = 0.06f
        };

        /// <summary>Heide am Steilrand: niedrig, dicht, viele kleine violette Blüten an der oberen Hälfte.</summary>
        private static readonly Art HeideArt = new Art
        {
            Name = "Heide", Praefix = "Westweiden_", Fassung = "WW-Pflanzen v1 (13.10.2026)",
            HoeheMin = 0.5f, HoeheMax = 0.7f, BiegungMin = 0.02f, BiegungMax = 0.08f, BreiteMin = 0.32f, BreiteMax = 0.45f,
            Wurzel = new Color(0.22f, 0.24f, 0.14f), Spitze = new Color(0.36f, 0.4f, 0.24f), Blute = new Color(0.62f, 0.36f, 0.62f),
            BaseColor = new Color(0.9f, 0.88f, 0.9f, 1f), VarColor = new Color(0.86f, 0.8f, 0.86f, 1f), Seed = 73, Aehre = false,
            Halme = 9, Blueten = 10, BluetenAb = 0.45f, BluetenRadius = 0.017f, Wind = 0.04f
        };

        /// <summary>Mohn am Ackerrand: grüne Stängel, einzelne große rote Blüten.</summary>
        private static readonly Art MohnArt = new Art
        {
            Name = "Mohn", Praefix = "Westweiden_", Fassung = "WW-Pflanzen v1 (13.10.2026)",
            HoeheMin = 0.55f, HoeheMax = 0.72f, BiegungMin = 0.04f, BiegungMax = 0.12f, BreiteMin = 0.3f, BreiteMax = 0.42f,
            Wurzel = new Color(0.25f, 0.36f, 0.18f), Spitze = new Color(0.42f, 0.55f, 0.28f), Blute = new Color(0.78f, 0.13f, 0.08f),
            BaseColor = new Color(0.92f, 0.92f, 0.9f, 1f), VarColor = new Color(0.9f, 0.88f, 0.84f, 1f), Seed = 89, Aehre = false,
            Halme = 5, BluetenAb = 0.92f, BluetenRadius = 0.07f, BluetenAnteil = 0.6f, Wind = 0.08f
        };

        /// <summary>
        /// Korn der Westweiden (14.10.2026, VORSCHLAG): heller, strohgelber als das Kareth-Korn – im warmen Westwind-Nachmittagslicht
        /// der Westweiden (F6) wirkte das Kareth-Korn orange. Form wie Korn, nur die Farben.
        /// </summary>
        private static readonly Art WwKornArt = new Art
        {
            Name = "Korn", Praefix = "Westweiden_", Fassung = "WW-Pflanzen v1 (14.10.2026)",
            HoeheMin = 0.62f, HoeheMax = 0.8f, BiegungMin = 0.04f, BiegungMax = 0.12f, BreiteMin = 0.3f, BreiteMax = 0.42f,
            Wurzel = new Color(0.52f, 0.52f, 0.30f), Spitze = new Color(0.88f, 0.82f, 0.56f), Blute = new Color(0.82f, 0.74f, 0.50f),
            BaseColor = new Color(0.93f, 0.92f, 0.86f, 1f), VarColor = new Color(0.92f, 0.90f, 0.78f, 1f), Seed = 31, Aehre = true
        };

        public static GameObject Korn() => Erzeugen(KornArt, false);
        public static GameObject WestweidenKorn() => Erzeugen(WwKornArt, false);
        public static GameObject Flachs() => Erzeugen(FlachsArt, false);
        public static GameObject Roehricht() => Erzeugen(RoehrichtArt, false);
        public static GameObject Heide() => Erzeugen(HeideArt, false);
        public static GameObject Mohn() => Erzeugen(MohnArt, false);

        [MenuItem("Ashveil/Welt/Kareth v4: Feldpflanzen neu erzeugen", priority = 569)]
        private static void Menue()
        {
            Erzeugen(KornArt, true); Erzeugen(FlachsArt, true);
            Erzeugen(RoehrichtArt, true); Erzeugen(HeideArt, true); Erzeugen(MohnArt, true); Erzeugen(WwKornArt, true);
            Debug.Log("[Ashveil] Feldpflanzen (Korn, Flachs; Westweiden: Röhricht, Heide, Mohn) neu erzeugt.");
        }

        private static string Pfad(Art a, string endung) => $"{Folder}/{a.Praefix}{a.Name}Patch{endung}";

        private static GameObject Erzeugen(Art a, bool neu)
        {
            var vorhanden = AssetDatabase.LoadAssetAtPath<GameObject>(Pfad(a, ".prefab"));
            if (vorhanden != null && !neu)
            {
                var mf = vorhanden.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name == a.Fassung + " " + a.Name) return vorhanden;
            }
            ProjectSetup.EnsureFolder(Folder);
            Texture2D tex = Textur(a);
            Mesh mesh = Netz(a);
            Material mat = MaterialAnlegen(a, tex);
            var go = new GameObject(a.Praefix + a.Name + "Patch");
            try
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = true;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, Pfad(a, ".prefab"), out bool ok);
                if (!ok) Debug.LogWarning("[Ashveil] Feldpflanzen: Prefab nicht gespeichert – " + a.Name);
                return ok ? prefab : null;
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static Mesh Netz(Art art)
        {
            const int Karten = 7, Segmente = 4;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var c = new List<Color>(); var tri = new List<int>();
            var rng = new System.Random(art.Seed * 1009);
            float R(float x, float y) => x + (float)rng.NextDouble() * (y - x);
            for (int k = 0; k < Karten; k++)
            {
                float ang = k * (360f / Karten) + R(-20f, 20f);
                float rad = k == 0 ? 0f : R(0.12f, 0.36f);
                Vector3 fuss = new Vector3(Mathf.Cos(ang * Mathf.Deg2Rad) * rad, 0f, Mathf.Sin(ang * Mathf.Deg2Rad) * rad);
                float yaw = R(0f, 360f) * Mathf.Deg2Rad;
                Vector3 quer = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)), vorn = new Vector3(-Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                float breite = R(art.BreiteMin, art.BreiteMax), hoehe = R(art.HoeheMin, art.HoeheMax);
                float biegung = R(art.BiegungMin, art.BiegungMax) * hoehe;
                int basis = v.Count;
                for (int r = 0; r <= Segmente; r++)
                {
                    float t = r / (float)Segmente;
                    Vector3 mitte = fuss + Vector3.up * (hoehe * t) + vorn * (biegung * t * t * t);   // aufrecht, oben nickend
                    float halb = breite * 0.5f;
                    for (int s = 0; s < 2; s++)
                    {
                        v.Add(mitte + quer * (s == 0 ? -halb : halb));
                        n.Add((Vector3.up * 0.75f + vorn * 0.3f).normalized);
                        uv.Add(new Vector2(s, t));
                        c.Add(new Color(t, Mathf.Lerp(0.7f, 1f, t), k / (float)(Karten - 1), 1f));
                    }
                }
                for (int r = 0; r < Segmente; r++)
                {
                    int b = basis + r * 2;
                    tri.Add(b); tri.Add(b + 2); tri.Add(b + 1);
                    tri.Add(b + 1); tri.Add(b + 2); tri.Add(b + 3);
                }
            }
            string pfad = Pfad(art, "_Mesh.asset");
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(pfad);
            bool neu = mesh == null;
            if (neu) mesh = new Mesh();
            mesh.Clear();
            mesh.name = art.Fassung + " " + art.Name;
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetColors(c); mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            if (neu) AssetDatabase.CreateAsset(mesh, pfad); else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static Texture2D Textur(Art art)
        {
            const int W = 256, H = 512;
            var px = new Color[W * H];
            var rng = new System.Random(art.Seed);
            float R(float x, float y) => x + (float)rng.NextDouble() * (y - x);
            // fünf bis sechs Halme je Karte, dicht nebeneinander (Feld statt Wiese)
            var halme = new List<(float u, float bend, float w, float h, float ton)>();
            int zahl = art.Halme > 0 ? art.Halme : art.Aehre ? 5 : 6;
            float seitenmass = (art.HoeheMin + art.HoeheMax) / Mathf.Max(0.01f, art.BreiteMin + art.BreiteMax);   // v-Einheit in u-Einheiten (runde Blüten)
            for (int i = 0; i < zahl; i++) halme.Add(((i + 0.5f) / zahl + R(-0.03f, 0.03f), R(-0.06f, 0.06f), art.Aehre ? R(0.022f, 0.03f) : R(0.012f, 0.018f), R(0.86f, 1f), R(-0.07f, 0.07f)));
            for (int y = 0; y < H; y++)
            {
                float vv = y / (float)(H - 1);
                for (int x = 0; x < W; x++)
                {
                    float uu = x / (float)(W - 1);
                    Color col = Color.Lerp(art.Wurzel, art.Spitze, vv);
                    float best = 0f;
                    foreach (var h in halme)
                    {
                        bool bluetenkopf = !art.Aehre && (art.Blueten > 1 || art.BluetenAnteil < 1f);
                        if (vv > h.h + (bluetenkopf ? art.BluetenRadius * 1.5f / seitenmass : 0f)) continue;
                        float t = vv / h.h;
                        float mitte = h.u + h.bend * t * t;
                        float halb = h.w;
                        Color farbe = Color.Lerp(art.Wurzel, art.Spitze, Mathf.Pow(t, 0.7f)) * (1f + h.ton);
                        bool kopf = art.KopfAnteil >= 1f || Mathf.Repeat(halme.IndexOf(h) * 0.618f + art.Seed * 0.1f, 1f) <= art.KopfAnteil;
                        if (art.Aehre && !kopf) halb = h.w * Mathf.Lerp(1.4f, 0.15f, t * t);   // Blatt: unten breit, spitz auslaufend
                        if (art.Aehre && kopf && t > 0.78f)
                        {
                            // Ähre: dicker, körnig (Querstreifen), etwas dunkler gold
                            float k = (t - 0.78f) / 0.22f;
                            halb = h.w * (art.AehreBreite - 1.2f * k * k);
                            float korn = 0.82f + 0.18f * Mathf.Abs(Mathf.Sin(t * 260f + (uu - mitte) * 90f));
                            farbe = art.Blute * korn * (1f + h.ton);
                        }
                        float d = Mathf.Abs(uu - mitte);
                        float a = Mathf.Clamp01((halb - d) / 0.008f);
                        if (t > 1f) a = 0f;   // über der Spitze nur die Blüte
                        if (!art.Aehre && art.Blueten <= 1 && art.BluetenAnteil >= 1f && t > 0.82f)
                        {
                            // Flachs: kleine blaue Blüte am Ende jedes Stängels
                            float bd = Vector2.Distance(new Vector2(uu, vv), new Vector2(mitte, h.h - 0.035f));
                            float ba = Mathf.Clamp01((0.032f - bd) / 0.01f);
                            if (ba > a) { a = ba; farbe = art.Blute * (0.9f + 0.2f * (float)rng.NextDouble()); }
                        }
                        else if (bluetenkopf && t > art.BluetenAb - art.BluetenRadius * 2f / seitenmass)
                        {
                            // Westweiden: mehrere Blüten am Stängel (Heide) oder eine große, nicht an jedem Stängel (Mohn) – fest je Halm
                            int hi = halme.IndexOf(h);
                            if (art.BluetenAnteil < 1f && Mathf.Repeat(hi * 0.618f + art.Seed * 0.1f, 1f) > art.BluetenAnteil) { }
                            else
                                for (int b = 0; b < art.Blueten; b++)
                                {
                                    float tb = art.Blueten == 1 ? art.BluetenAb : Mathf.Lerp(art.BluetenAb, 0.97f, b / (float)(art.Blueten - 1));
                                    float seite = art.Blueten == 1 ? 0f : ((b & 1) == 0 ? -1f : 1f) * h.w * 1.6f;
                                    Vector2 bp = new Vector2(h.u + h.bend * tb * tb + seite, tb * h.h);
                                    float bd = Vector2.Distance(new Vector2(uu, vv * seitenmass), new Vector2(bp.x, bp.y * seitenmass));   // rund auf der Karte
                                    float ba = Mathf.Clamp01((art.BluetenRadius - bd) / 0.008f);
                                    if (ba > a) { a = ba; farbe = art.Blute * (0.92f + 0.16f * Mathf.Repeat(hi * 0.37f + b * 0.21f, 1f)); }
                                }
                        }
                        if (a <= best) continue;
                        best = a; col = farbe;
                    }
                    px[y * W + x] = new Color(col.r, col.g, col.b, best);
                }
            }
            string pfad = Pfad(art, "_Halme.asset");
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(pfad);
            bool neu = tex == null;
            if (neu) tex = new Texture2D(W, H, TextureFormat.RGBA32, true, false);
            else if (tex.width != W || tex.height != H) tex.Reinitialize(W, H);
            tex.name = art.Praefix + art.Name + "Halme";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            tex.SetPixels(px);
            tex.Apply(true, false);
            if (neu) AssetDatabase.CreateAsset(tex, pfad); else EditorUtility.SetDirty(tex);
            return tex;
        }

        private static Material MaterialAnlegen(Art art, Texture2D tex)
        {
            Shader sh = Shader.Find("Ashveil/Foliage");
            string pfad = Pfad(art, ".mat");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(pfad);
            bool neu = mat == null;
            if (neu) mat = new Material(sh); else mat.shader = sh;
            mat.name = art.Praefix + art.Name + "Patch";
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", art.BaseColor);
            mat.SetFloat("_Cutoff", 0.4f);
            mat.SetFloat("_AlphaClip", 1f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_WindStrength", art.Wind > 0f ? art.Wind : art.Aehre ? 0.09f : 0.07f);    // Korn wogt etwas stärker
            mat.SetFloat("_WindSpeed", 1.0f);
            mat.SetFloat("_SwayHeight", 0.7f);
            mat.SetFloat("_Translucency", 0.38f);
            mat.SetFloat("_NormalUp", 0.8f);
            if (mat.HasProperty("_NearFade")) mat.SetFloat("_NearFade", 0.9f);
            if (mat.HasProperty("_VarColor")) mat.SetColor("_VarColor", art.VarColor);
            if (mat.HasProperty("_VarScale")) mat.SetFloat("_VarScale", 16f);
            if (mat.HasProperty("_RootDark")) mat.SetFloat("_RootDark", 0.35f);
            mat.enableInstancing = true;
            mat.SetShaderPassEnabled("SHADOWCASTER", false);
            if (neu) AssetDatabase.CreateAsset(mat, pfad); else EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
