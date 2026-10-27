using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Ashveil.Core;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Westweiden – Wasser im Entwurf (Claude 12.10.2026, Wasser-Familie; VORSCHLAG): Meer im Westen, Mühlbach als Band im
    /// Bachbett, Brücken an allen Kreuzungen von Bach und Weg. Shader <c>Ashveil/Wasser</c>, Kachel WW_Wasser_Wellen (mat_wasser.py).
    /// Meer: Fläche bis 30 km in alle Richtungen westlich der Küste, Netz zur Küste hin dicht (Uferschaum, Tiefe) und draußen
    /// grob; WaterVolume für Schwimmen (MOV-169). Bach: Wasserspiegel aus dem Bachbett im Gelände (Sohle + 0,75 m, fällt nie
    /// bachabwärts an), Breite bis zur Uferlinie; watbar (kein WaterVolume, 0,75 m).
    /// Nur für die Szene Westweiden_Entwurf; Daten aus Data/Westweiden/Entwurf (WW_Bach.csv, WW_Wege.csv).
    /// </summary>
    public static class WestweidenWasser
    {
        private const string Src = ProjectSetup.Root + "/Art/Westweiden/Proben";
        private const string DataDir = ProjectSetup.Root + "/Data/Westweiden/Entwurf";

        public static string Bauen(Terrain t, float sx, float sz)
        {
            var log = new StringBuilder();
            var root = new GameObject("Wasser (Entwurf)").transform;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Src + "/WW_Wasser_Wellen.png");
            Material meer = Mat("WW_Wasser_Meer", tex, m =>
            {
                m.SetColor("_FlachFarbe", new Color(0.46f, 0.72f, 0.66f));   // Kreideküste: helles, klares Türkisgrün
                m.SetColor("_TiefFarbe", new Color(0.05f, 0.19f, 0.25f));
                m.SetFloat("_TiefeVoll", 9f); m.SetFloat("_Klarheit", 3.0f);
                m.SetFloat("_WellenMassstab", 16f); m.SetFloat("_WellenStaerke", 0.8f);
                m.SetVector("_Wind", new Vector4(1f, 0.25f, 0.7f, 0f));
                m.SetFloat("_UVWelt", 1f); m.SetFloat("_Fliess", 0f);
                m.SetFloat("_SchaumBreite", 2.5f); m.SetFloat("_SchaumGrund", 0f);
            });
            Material bach = Mat("WW_Wasser_Bach", tex, m =>
            {
                m.SetColor("_FlachFarbe", new Color(0.55f, 0.66f, 0.56f));   // Kalkbach: klar, leicht grünlich
                m.SetColor("_TiefFarbe", new Color(0.16f, 0.27f, 0.24f));
                m.SetFloat("_TiefeVoll", 1.6f); m.SetFloat("_Klarheit", 0.9f);
                m.SetFloat("_WellenMassstab", 3.5f); m.SetFloat("_WellenStaerke", 0.55f);
                m.SetVector("_Wind", new Vector4(1f, 0f, 0.05f, 0f));
                m.SetFloat("_UVWelt", 0f); m.SetFloat("_Fliess", 0.8f);
                m.SetFloat("_SchaumBreite", 0.18f); m.SetFloat("_SchaumGrund", 0.05f);   // Entwurf 14: Uferschaum als durchgehende weiße Linie → schmaler
            });
            if (meer == null) return "Shader Ashveil/Wasser fehlt";

            // ---------------------------------------------------------------- Meer
            float kuesteX = 0f;
            for (float z = 0f; z <= sz; z += 25f)
                for (float x = 0f; x < sx * 0.3f; x += 5f)
                    if (Boden(t, x, z) > 2f) { kuesteX = Mathf.Max(kuesteX, x); break; }
            float ostRand = kuesteX + 80f;                              // Meer reicht unter die Küste (dort verdeckt das Gelände)
            var go = new GameObject("Meer");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = MeerNetz(ostRand, sz);
            go.AddComponent<MeshRenderer>().sharedMaterial = meer;
            var wv = go.AddComponent<WaterVolume>();
            wv.Configure(Rect.MinMaxRect(-30000f, -30000f, ostRand, sz + 30000f), 0f);
            log.Append("Meer bis x ").Append(ostRand.ToString("0")).Append(" m · ");

            // ---------------------------------------------------------------- Mühlbach
            var linie = Linie(DataDir + "/WW_Bach.csv", "Mühlbach");
            if (linie.Count < 2) { log.Append("WW_Bach.csv fehlt – kein Bach · "); return log.ToString(); }
            var punkte = Abtasten(linie, 2f);
            int n = punkte.Count;
            var spiegel = new float[n]; var links = new float[n]; var rechts = new float[n]; var quer = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 a = punkte[Mathf.Max(0, i - 1)], b = punkte[Mathf.Min(n - 1, i + 1)];
                Vector2 d = (b - a).normalized; quer[i] = new Vector2(-d.y, d.x);
                float sohle = float.MaxValue;
                for (float o = -3f; o <= 3f; o += 0.5f) sohle = Mathf.Min(sohle, Boden(t, punkte[i].x + quer[i].x * o, punkte[i].y + quer[i].y * o));
                spiegel[i] = sohle + 0.75f;
                if (i > 0) spiegel[i] = Mathf.Min(spiegel[i], spiegel[i - 1]);       // nie bergauf
            }
            for (int i = 0; i < n; i++)
            {
                links[i] = Ufer(t, punkte[i], quer[i], spiegel[i], 1f);
                rechts[i] = Ufer(t, punkte[i], quer[i], spiegel[i], -1f);
            }
            Glaetten(links, 3); Glaetten(rechts, 3);
            // Quelle: das Band beginnt nicht als Rechteck, sondern läuft auf den ersten 30 m aus einem Rinnsal auf (Entwurf 15)
            float lauf = 0f;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) lauf += Vector2.Distance(punkte[i - 1], punkte[i]);
                float f = Mathf.SmoothStep(0.15f, 1f, Mathf.Clamp01(lauf / 30f));
                links[i] *= f; rechts[i] *= f;
            }
            // Klang (14.10.2026): Bachlauf mit Wasserspiegel für die Klangquellen (WestweidenKlang), Zellen-Koordinaten
            LetzterBach.Clear();
            for (int i = 0; i < n; i++) LetzterBach.Add(new Vector3(punkte[i].x, spiegel[i], punkte[i].y));
            var bgo = new GameObject("Mühlbach");
            bgo.transform.SetParent(root, false);
            bgo.AddComponent<MeshFilter>().sharedMesh = BachNetz(punkte, quer, spiegel, links, rechts);
            bgo.AddComponent<MeshRenderer>().sharedMaterial = bach;
            float laenge = 0f; for (int i = 1; i < n; i++) laenge += Vector2.Distance(punkte[i - 1], punkte[i]);
            log.Append("Mühlbach ").Append((laenge / 1000f).ToString("0.00", CultureInfo.InvariantCulture)).Append(" km · ");

            // ---------------------------------------------------------------- Brücken an Kreuzungen Bach × Weg
            Material holz = AssetDatabase.LoadAssetAtPath<Material>(ProjectSetup.Root + "/Art/Westweiden/Proben/WW_Greybox_Holz.mat");
            var wege = Wege(DataDir + "/WW_Wege.csv");
            int bruecken = 0;
            foreach (var kv in wege)
            {
                bool haupt = kv.Key.StartsWith("Hofstraße") || kv.Key.StartsWith("Vorspannrampe");
                for (int s = 1; s < kv.Value.Count; s++)
                    for (int k = 1; k < linie.Count; k++)
                    {
                        if (!Schnitt(kv.Value[s - 1], kv.Value[s], linie[k - 1], linie[k], out Vector2 p)) continue;
                        int ni = Naechster(punkte, p);
                        float breite = links[ni] + rechts[ni];
                        Vector2 r = (kv.Value[s] - kv.Value[s - 1]).normalized;
                        Bruecke(t, root, kv.Key, p, r, breite + 7f, haupt ? 4.2f : 2.6f, spiegel[ni], holz);
                        bruecken++;
                    }
            }
            log.Append(bruecken).Append(" Brücken · ");
            return log.ToString();
        }

        private static float Boden(Terrain t, float x, float z) => t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y;

        private static Material Mat(string name, Texture2D tex, System.Action<Material> setze)
        {
            var shader = Shader.Find("Ashveil/Wasser");
            if (shader == null) return null;
            string mp = Src + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, mp); }
            m.shader = shader;
            m.SetTexture("_WellenTex", tex);
            setze(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Meer: Gitter mit geometrisch wachsenden Abständen von der Küste weg (x) und vom Kartenrand weg (z).</summary>
        private static Mesh MeerNetz(float ostRand, float sz)
        {
            var xs = new List<float>();
            for (float x = ostRand, s = 6f; x > -30000f; x -= s, s *= 1.12f) xs.Add(x);
            xs.Add(-30000f); xs.Reverse();
            var zs = new List<float>();
            for (float z = 0f; z <= sz; z += 40f) zs.Add(z);
            for (float z = -40f, s = 40f; z > -30000f; z -= s, s *= 1.25f) zs.Insert(0, z);
            zs.Insert(0, -30000f);
            for (float z = sz + 40f, s = 40f; z < sz + 30000f; z += s, s *= 1.25f) zs.Add(z);
            zs.Add(sz + 30000f);
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            foreach (float z in zs) foreach (float x in xs) { v.Add(new Vector3(x, 0f, z)); uv.Add(new Vector2(x, z)); }
            int w = xs.Count;
            for (int j = 0; j < zs.Count - 1; j++)
                for (int i = 0; i < w - 1; i++)
                {
                    int a = j * w + i;
                    tri.AddRange(new[] { a, a + w, a + 1, a + 1, a + w, a + w + 1 });
                }
            var m = new Mesh { name = "WW Meer", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Uferlinie: vom Bachmittelpunkt nach außen bis das Gelände den Spiegel überragt (+0,6 m Überlappung ins Ufer).</summary>
        private static float Ufer(Terrain t, Vector2 p, Vector2 q, float spiegel, float seite)
        {
            for (float o = 0.5f; o <= 12f; o += 0.25f)
                if (Boden(t, p.x + q.x * o * seite, p.y + q.y * o * seite) >= spiegel) return Mathf.Clamp(o + 0.6f, 1.2f, 12f);
            return 12f;
        }

        private static void Glaetten(float[] a, int r)
        {
            var b = (float[])a.Clone();
            for (int i = 0; i < a.Length; i++)
            {
                float s = 0f; int c = 0;
                for (int k = -r; k <= r; k++) { int j = Mathf.Clamp(i + k, 0, a.Length - 1); s += b[j]; c++; }
                a[i] = Mathf.Max(b[i], s / c);                         // nie schmaler als die gemessene Uferlinie
            }
        }

        private static Mesh BachNetz(List<Vector2> p, Vector2[] q, float[] y, float[] l, float[] r)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            float s = 0f;
            for (int i = 0; i < p.Count; i++)
            {
                if (i > 0) s += Vector2.Distance(p[i - 1], p[i]);
                Vector2 a = p[i] + q[i] * l[i], b = p[i] - q[i] * r[i];
                v.Add(new Vector3(a.x, y[i], a.y)); v.Add(new Vector3(b.x, y[i], b.y));
                uv.Add(new Vector2(-l[i], s)); uv.Add(new Vector2(r[i], s));
                if (i > 0)
                {
                    int k = v.Count - 4;
                    tri.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
                }
            }
            var m = new Mesh { name = "WW Mühlbach", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateNormals();
            var nn = m.normals; for (int i = 0; i < nn.Length; i++) if (nn[i].y < 0f) nn[i] = -nn[i];
            m.normals = nn;
            m.RecalculateBounds();
            // Dreiecke nach oben ausrichten (Cull Back): Wicklung prüfen
            if (Vector3.Cross(v[2] - v[0], v[1] - v[0]).y < 0f)
            {
                for (int i = 0; i < tri.Count; i += 3) { int x = tri[i + 1]; tri[i + 1] = tri[i + 2]; tri[i + 2] = x; }
                m.SetTriangles(tri, 0);
            }
            return m;
        }

        /// <summary>Bachlauf des letzten Baus (Punkte alle 2 m, y = Wasserspiegel) – für die Klangquellen am Bach.</summary>
        internal static readonly List<Vector3> LetzterBach = new List<Vector3>();

        private static void Bruecke(Terrain t, Transform root, string weg, Vector2 p, Vector2 r, float laenge, float breite, float spiegel, Material holz)
        {
            float ya = Boden(t, p.x - r.x * laenge * 0.5f, p.y - r.y * laenge * 0.5f);
            float yb = Boden(t, p.x + r.x * laenge * 0.5f, p.y + r.y * laenge * 0.5f);
            float deck = Mathf.Max(Mathf.Max(ya, yb), spiegel + 1.2f);
            var g = new GameObject("Brücke " + weg).transform;
            g.SetParent(root, false);
            g.SetPositionAndRotation(new Vector3(p.x, deck, p.y), Quaternion.LookRotation(new Vector3(r.x, 0f, r.y)));
            Material trim = HolzBau.Holz;
            if (trim != null) { BrueckeModell(g, weg, laenge, breite, deck - spiegel, trim); return; }
            Teil(g, "Deck", new Vector3(0f, -0.25f, 0f), new Vector3(breite, 0.5f, laenge), holz, true);
            foreach (float sxx in new[] { -1f, 1f })
            {
                Teil(g, "Geländer", new Vector3(sxx * (breite * 0.5f - 0.1f), 0.9f, 0f), new Vector3(0.15f, 0.12f, laenge - 1f), holz, false);
                for (float z = -laenge * 0.5f + 0.6f; z <= laenge * 0.5f - 0.5f; z += 2.2f)
                    Teil(g, "Pfosten", new Vector3(sxx * (breite * 0.5f - 0.1f), 0.45f, z), new Vector3(0.15f, 0.9f, 0.15f), holz, false);
            }
            // Joche im Bach (zwei Stützen)
            foreach (float z in new[] { -laenge * 0.18f, laenge * 0.18f })
                Teil(g, "Joch", new Vector3(0f, -(deck - spiegel + 0.8f) * 0.5f - 0.5f, z), new Vector3(breite * 0.9f, deck - spiegel + 0.8f, 0.4f), holz, false);
        }

        /// <summary>
        /// 14.10.2026 (Lückenliste Punkt 6): Holzbrücke als Modell (<see cref="HolzBau"/>, Eichenholz des Trim-Sheets, ein Mesh):
        /// quer gelegte Bohlen mit Fugen auf drei/vier Längsbalken, Geländer mit Pfosten, Handlauf und Knieholm, zwei Joche aus
        /// Rundpfählen mit Holm im Bach. Begehbar ist weiter der unsichtbare Kasten „Deck“ (wie vorher, gleiche Maße).
        /// hoch = Deck über dem Wasserspiegel.
        /// </summary>
        private static void BrueckeModell(Transform g, string weg, float laenge, float breite, float hoch, Material holz)
        {
            var deck = new GameObject("Deck").transform;
            deck.SetParent(g, false);
            var bc = deck.gameObject.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, -0.25f, 0f); bc.size = new Vector3(breite, 0.5f, laenge);
            int saat = 7; foreach (char c in weg) saat = saat * 31 + c;
            var netz = new HolzBau.Netz(g, saat);
            Vector3 W(float x, float y, float z) => g.TransformPoint(new Vector3(x, y, z));
            Vector3 quer = g.right, oben = Vector3.up;
            float l2 = laenge * 0.5f, b2 = breite * 0.5f;
            // Bohlen quer, 0,30 m breit mit Fugen, leicht ungleich
            for (float z = -l2 + 0.16f; z < l2 - 0.1f; z += 0.32f)
            {
                float yo = -0.035f + netz.Zufall(-0.012f, 0.012f), ue = netz.Zufall(0.05f, 0.2f);
                netz.Prisma(W(-b2 - ue, yo, z + netz.Zufall(-0.01f, 0.01f)), W(b2 + netz.Zufall(0.05f, 0.2f), yo, z), 0.29f, 0.07f, 4, oben);
            }
            // Längsbalken unter den Bohlen
            int balken = breite > 3.5f ? 4 : 3;
            for (int i = 0; i < balken; i++)
            {
                float x = Mathf.Lerp(-b2 + 0.35f, b2 - 0.35f, i / (float)(balken - 1));
                netz.Prisma(W(x, -0.22f, -l2 - 0.3f), W(x, -0.22f, l2 + 0.3f), 0.24f, 0.30f, 4, oben);
            }
            // Geländer: Pfosten, Handlauf, Knieholm
            foreach (float sx in new[] { -1f, 1f })
            {
                float x = sx * (b2 - 0.1f);
                for (float z = -l2 + 0.6f; z <= l2 - 0.5f; z += 2.2f)
                    netz.Prisma(W(x, -0.3f, z), W(x + netz.Zufall(-0.02f, 0.02f), 1.0f, z), 0.15f, 0.15f, 4, quer);
                netz.Prisma(W(x, 1.0f, -l2 + 0.4f), W(x, 1.0f, l2 - 0.3f), 0.13f, 0.10f, 5, oben);
                netz.Prisma(W(x, 0.5f, -l2 + 0.5f), W(x, 0.5f, l2 - 0.4f), 0.09f, 0.08f, 5, oben);
            }
            // Joche: Rundpfähle bis unter den Bachgrund, Holm quer darauf
            foreach (float z in new[] { -laenge * 0.18f, laenge * 0.18f })
            {
                int pf = breite > 3.5f ? 4 : 3;
                for (int i = 0; i < pf; i++)
                {
                    float x = Mathf.Lerp(-b2 + 0.3f, b2 - 0.3f, i / (float)(pf - 1));
                    netz.Prisma(W(x, -0.37f, z), W(x + netz.Zufall(-0.04f, 0.04f), -hoch - 1.4f, z + netz.Zufall(-0.04f, 0.04f)), 0.30f, 0.30f, 6, quer);
                }
                netz.Prisma(W(-b2 - 0.15f, -0.5f, z), W(b2 + 0.15f, -0.5f, z), 0.28f, 0.24f, 4, oben);
            }
            var vis = netz.Fertig("Brücke " + weg + " (Holz)", holz);
            if (vis != null) vis.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        private static void Teil(Transform g, string name, Vector3 lp, Vector3 s, Material m, bool kollision)
        {
            var o = GameObject.CreatePrimitive(PrimitiveType.Cube);
            o.name = name; o.transform.SetParent(g, false);
            o.transform.localPosition = lp; o.transform.localScale = s;
            if (m != null) o.GetComponent<Renderer>().sharedMaterial = m;
            if (!kollision) Object.DestroyImmediate(o.GetComponent<Collider>());   // Geländer sichtbar, keine Wand (WORLD-114, wie Zäune)
        }

        // ---------------------------------------------------------------- Daten
        private static string Abs(string p) => Path.Combine(Path.GetDirectoryName(Application.dataPath), p);

        private static List<Vector2> Linie(string pfad, string name)
        {
            var l = new List<Vector2>();
            if (!File.Exists(Abs(pfad))) return l;
            foreach (string line in File.ReadAllLines(Abs(pfad), Encoding.UTF8))
            {
                string[] f = line.Split(';');
                if (f.Length < 3 || f[0] != name) continue;
                l.Add(new Vector2(float.Parse(f[1], CultureInfo.InvariantCulture), float.Parse(f[2], CultureInfo.InvariantCulture)));
            }
            return l;
        }

        private static Dictionary<string, List<Vector2>> Wege(string pfad)
        {
            var d = new Dictionary<string, List<Vector2>>();
            if (!File.Exists(Abs(pfad))) return d;
            foreach (string line in File.ReadAllLines(Abs(pfad), Encoding.UTF8))
            {
                string[] f = line.Split(';');
                if (f.Length < 3) continue;
                if (!d.TryGetValue(f[0], out var l)) d[f[0]] = l = new List<Vector2>();
                l.Add(new Vector2(float.Parse(f[1], CultureInfo.InvariantCulture), float.Parse(f[2], CultureInfo.InvariantCulture)));
            }
            return d;
        }

        private static List<Vector2> Abtasten(List<Vector2> l, float schritt)
        {
            var o = new List<Vector2>();
            for (int i = 1; i < l.Count; i++)
            {
                float len = Vector2.Distance(l[i - 1], l[i]);
                int n = Mathf.Max(1, Mathf.RoundToInt(len / schritt));
                for (int k = 0; k < n; k++) o.Add(Vector2.Lerp(l[i - 1], l[i], k / (float)n));
            }
            o.Add(l[l.Count - 1]);
            // Ecken der Polylinie runden (Chaikin, 3 Durchgänge), damit der Bach nicht geknickt läuft
            for (int pass = 0; pass < 3; pass++)
            {
                var g = new List<Vector2> { o[0] };
                for (int i = 1; i < o.Count - 1; i++) g.Add(o[i - 1] * 0.25f + o[i] * 0.5f + o[i + 1] * 0.25f);
                g.Add(o[o.Count - 1]); o = g;
            }
            return o;
        }

        private static int Naechster(List<Vector2> p, Vector2 q)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < p.Count; i++) { float d = (p[i] - q).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
            return best;
        }

        private static bool Schnitt(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 p)
        {
            p = default;
            Vector2 r = b - a, s = d - c;
            float den = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(den) < 1e-6f) return false;
            float tt = ((c - a).x * s.y - (c - a).y * s.x) / den, u = ((c - a).x * r.y - (c - a).y * r.x) / den;
            if (tt < 0f || tt > 1f || u < 0f || u > 1f) return false;
            p = a + r * tt;
            return true;
        }
    }
}
