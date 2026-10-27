using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Wiesen-Pass (10.10.2026, Optik-Termin nach CORE-009 „Immersion vor allem“; Max' Klick „Gras, Boden, Luft“): Das Terrain-Gras
    /// bestand aus sternförmigen Einzelbüscheln (Blender-Platzhalter SM_GrassClump) – vereinzelt, kantig, ohne Verlauf. Dieses
    /// Werkzeug erzeugt im Editor eine <b>Grasfläche als Instanz</b>: ein Fleck aus neun gebogenen Halmkarten (je drei Halme auf
    /// einer gemalten Alpha-Textur), mit Farbverlauf von dunkler Wurzel zu heller Spitze (Vertexfarbe r) und Bodenverschattung
    /// (Vertexfarbe g). Dazu eine prozedural gemalte Halmtextur (keine fremden Inhalte, ART-099) und ein Material mit dem
    /// Foliage-Shader (Alpha-Ausschnitt, Wind, Durchscheinen, Farbvariation je Weltposition).
    ///
    /// Ergebnis: <c>Terrain/KarethV4_GrasPatch.prefab</c> (Mesh, Textur, Material daneben). Nur Kareth v4 nutzt es
    /// (<see cref="KarethV4Builder"/>); Sandbox und Kareth bleiben beim alten Büschel. Alles wird beim Bau neu erzeugt, wenn es fehlt;
    /// Menü *Ashveil › Welt › Kareth v4: Grasfläche neu erzeugen* erzwingt den Neubau.
    /// Kosten: 9 Karten × 3 Segmente = 54 Dreiecke je Fleck (vorher 6 Halme ≈ 12 Dreiecke je Büschel), aber ein Fleck ersetzt
    /// zwei bis drei Büschel; Instancing wie bisher.
    /// </summary>
    public static class GrasPatchGenerator
    {
        private const string Folder = ProjectSetup.Root + "/Terrain";
        public const string PrefabPath = Folder + "/KarethV4_GrasPatch.prefab";
        private const string MeshPath = Folder + "/KarethV4_GrasPatch_Mesh.asset";
        private const string TexPath = Folder + "/KarethV4_GrasHalme.asset";
        private const string MatPath = Folder + "/KarethV4_GrasPatch.mat";
        /// <summary>Kennung (Name des Netzes): ändert sich die Form, wird neu erzeugt.</summary>
        private const string Fassung = "GrasPatch v5 (10.10.2026)";   // v5 (Durchgang 2): breitere, geschwungene Halme, kühleres, weniger leuchtendes Grün

        private const int Karten = 9, Segmente = 4;

        [MenuItem("Ashveil/Welt/Kareth v4: Grasfläche neu erzeugen", priority = 568)]
        private static void Menue()
        {
            GameObject g = Erzeugen(true);
            Debug.Log("[Ashveil] Grasfläche: " + (g != null ? AssetDatabase.GetAssetPath(g) : "fehlgeschlagen"));
        }

        /// <summary>Vorlage laden oder erzeugen (Prefab mit MeshFilter/MeshRenderer, Drehpunkt unten mittig, 1 m breit).</summary>
        public static GameObject Vorlage() => Erzeugen(false);

        private static GameObject Erzeugen(bool neu)
        {
            var vorhanden = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (vorhanden != null && !neu)
            {
                var mf = vorhanden.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name == Fassung) return vorhanden;   // Fassung steckt im Netznamen
            }
            ProjectSetup.EnsureFolder(Folder);
            Texture2D tex = Textur();
            Mesh mesh = Netz();
            Material mat = MaterialAnlegen(tex);

            var go = new GameObject("KarethV4_GrasPatch");
            try
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // Gras wirft keinen Schatten (wie bisher; Kosten)
                mr.receiveShadows = true;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath, out bool ok);
                if (!ok) Debug.LogWarning("[Ashveil] Grasfläche: Prefab nicht gespeichert.");
                return ok ? prefab : null;
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ================================================================== Netz: neun gebogene Halmkarten
        private static Mesh Netz()
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var c = new List<Color>(); var tri = new List<int>();
            var rng = new System.Random(20261010);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            for (int k = 0; k < Karten; k++)
            {
                // Lage im Fleck: innen eine Karte, außen ein Ring; jede Karte eine andere Richtung
                float ang = k == 0 ? R(0f, 360f) : (k - 1) * (360f / (Karten - 1)) + R(-18f, 18f);
                float rad = k == 0 ? 0f : R(0.18f, 0.42f);
                Vector3 fuss = new Vector3(Mathf.Cos(ang * Mathf.Deg2Rad) * rad, 0f, Mathf.Sin(ang * Mathf.Deg2Rad) * rad);
                float yaw = R(0f, 360f) * Mathf.Deg2Rad;
                Vector3 quer = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));        // Kartenbreite
                Vector3 vorn = new Vector3(-Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));       // Biegungsrichtung
                float breite = R(0.34f, 0.5f), hoehe = R(0.3f, 0.58f);
                float biegung = R(0.22f, 0.42f) * hoehe;                              // Spitze neigt sich deutlich nach vorn (Bogen)
                float lehne = R(-0.06f, 0.06f);                                      // leichtes seitliches Kippen
                float uv0 = R(0f, 0.05f);                                            // kleine Verschiebung je Karte (Abwechslung)
                int basis = v.Count;
                for (int r = 0; r <= Segmente; r++)
                {
                    float t = r / (float)Segmente;
                    Vector3 mitte = fuss + Vector3.up * (hoehe * Mathf.Sin(t * 1.35f) / Mathf.Sin(1.35f)) + vorn * (biegung * t * t) + quer * (lehne * t);   // Bogen: oben flacher
                    float halb = breite * 0.5f * (1f - 0.08f * t);
                    for (int s = 0; s < 2; s++)
                    {
                        v.Add(mitte + quer * (s == 0 ? -halb : halb));
                        // Normale: etwas nach vorn (Biegung) und nach oben – der Shader zieht sie ohnehin Richtung Himmel
                        n.Add((Vector3.up * 0.8f + vorn * 0.35f).normalized);
                        uv.Add(new Vector2(s == 0 ? uv0 : 1f - uv0, t));
                        // r = Höhe (Wurzel dunkel → Spitze hell), g = Bodenverschattung, b = Zufall je Karte (Variation)
                        c.Add(new Color(t, Mathf.Lerp(0.72f, 1f, t), k / (float)(Karten - 1), 1f));
                    }
                }
                for (int r = 0; r < Segmente; r++)
                {
                    int a = basis + r * 2;
                    tri.Add(a); tri.Add(a + 2); tri.Add(a + 1);
                    tri.Add(a + 1); tri.Add(a + 2); tri.Add(a + 3);
                }
            }
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            bool neu = mesh == null;
            if (neu) mesh = new Mesh();
            mesh.Clear();
            mesh.name = Fassung;
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetColors(c); mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            if (neu) AssetDatabase.CreateAsset(mesh, MeshPath); else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // ================================================================== Textur: drei gemalte Halme je Karte (Alpha-Ausschnitt)
        private static Texture2D Textur()
        {
            const int W = 256, H = 512;
            var px = new Color[W * H];
            var rng = new System.Random(77);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            // Halme: Fußpunkt (u), Biegung, Breite, Höhe, Farbton
            var halme = new List<(float u, float bend, float w, float h, float ton)>();
            float[] fuesse = { 0.28f, 0.5f, 0.72f };
            for (int i = 0; i < 3; i++) halme.Add((fuesse[i] + R(-0.05f, 0.05f), R(-0.24f, 0.24f), R(0.1f, 0.15f), R(0.78f, 1f), R(-0.08f, 0.08f)));
            halme.Add((R(0.35f, 0.65f), R(-0.2f, 0.2f), 0.05f, R(0.45f, 0.6f), R(-0.1f, 0.05f)));   // ein kurzer Halm dahinter
            Color wurzel = new Color(0.30f, 0.41f, 0.17f), spitze = new Color(0.64f, 0.74f, 0.37f);   // kühler, näher am Kiefern-Grün
            for (int y = 0; y < H; y++)
            {
                float vv = y / (float)(H - 1);   // 0 unten, 1 oben
                for (int x = 0; x < W; x++)
                {
                    float uu = x / (float)(W - 1);
                    // Farbe auch dort, wo Alpha 0 ist: sonst mischen die Mipmaps Schwarz hinein und die Wiese wird in der Ferne
                    // zur dunklen Masse (gesehen im ersten Review 10.10.)
                    float alpha = 0f; Color col = Color.Lerp(wurzel, spitze, Mathf.Pow(vv, 0.8f)); float best = 0f;
                    foreach (var h in halme)
                    {
                        if (vv > h.h) continue;
                        float t = vv / h.h;
                        float mitte = h.u + h.bend * Mathf.Pow(t, 1.6f) + 0.01f * Mathf.Sin(t * 7f + h.u * 20f);   // geschwungen, oben stärker
                        float halb = h.w * Mathf.Pow(1f - t, 0.6f) * (0.55f + 0.45f * Mathf.Sqrt(1f - t));   // breiter Fuß, spitze Spitze
                        float d = Mathf.Abs(uu - mitte);
                        float a = Mathf.Clamp01((halb - d) / 0.02f);                       // weiche Kante (gemalt, nicht hart)
                        if (a <= best) continue;
                        best = a;
                        float strich = 0.9f + 0.1f * Mathf.Sin((uu - mitte) * 180f + t * 40f);  // feiner Längsstrich wie Pinsel
                        float mittelAder = 1f - 0.18f * Mathf.Exp(-d * d / (halb * halb * 0.08f + 1e-5f));
                        Color basis = Color.Lerp(wurzel, spitze, Mathf.Pow(t, 0.8f));
                        basis = basis * (1f + h.ton) * strich * mittelAder;
                        col = basis;
                    }
                    alpha = best;
                    px[y * W + x] = new Color(col.r, col.g, col.b, alpha);
                }
            }
            // Ränder: unten voll, damit kein Spalt zum Boden; außerhalb der Halme bleibt alles durchsichtig
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
            bool neu = tex == null;
            if (neu) tex = new Texture2D(W, H, TextureFormat.RGBA32, true, false);
            else if (tex.width != W || tex.height != H) tex.Reinitialize(W, H);
            tex.name = "KarethV4_GrasHalme";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            tex.SetPixels(px);
            tex.Apply(true, false);
            if (neu) AssetDatabase.CreateAsset(tex, TexPath); else EditorUtility.SetDirty(tex);
            return tex;
        }

        private static Material MaterialAnlegen(Texture2D tex)
        {
            Shader sh = Shader.Find("Ashveil/Foliage");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            bool neu = mat == null;
            if (neu) mat = new Material(sh);
            else mat.shader = sh;
            mat.name = "KarethV4_GrasPatch";
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", new Color(0.80f, 0.86f, 0.74f, 1f));   // um 15:30 leuchtete die Wiese gelbgrün gegen die Hänge (Tageszeit-Bilder 10.10.)
            mat.SetFloat("_Cutoff", 0.36f);
            mat.SetFloat("_AlphaClip", 1f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_WindStrength", 0.06f);
            mat.SetFloat("_WindSpeed", 1.2f);
            mat.SetFloat("_SwayHeight", 0.5f);
            mat.SetFloat("_Translucency", 0.32f);
            mat.SetFloat("_NormalUp", 0.8f);
            if (mat.HasProperty("_NearFade")) mat.SetFloat("_NearFade", 0.9f);
            if (mat.HasProperty("_VarColor")) mat.SetColor("_VarColor", new Color(0.86f, 0.90f, 0.68f, 1f));   // Ton B: olivgelb, trockener (gedämpft)
            if (mat.HasProperty("_VarScale")) mat.SetFloat("_VarScale", 11f);
            if (mat.HasProperty("_RootDark")) mat.SetFloat("_RootDark", 0.3f);
            mat.enableInstancing = true;
            mat.SetShaderPassEnabled("SHADOWCASTER", false);
            if (neu) AssetDatabase.CreateAsset(mat, MatPath); else EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
