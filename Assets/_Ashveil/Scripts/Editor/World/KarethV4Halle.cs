using System.Collections.Generic;
using System.IO;
using Ashveil.Core;
using UnityEditor;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Kareth v4: Halle des Hauptmanns begehbar (13.10.2026, Hauptquest der Menschen Akt I „Das Register“, freigegeben von Max am
    /// 12.10.2026; Bauplan §5: „Salzkeller unter der Halle als kleiner Innenraum (Greybox)“). Hinter dem Tor der Holzfront: Torgang,
    /// Halle (Herd unter dem Rauchloch, Tisch, Regal mit den alten Bänden, Ablage für das Register), Treppe, Salzkeller mit der
    /// versiegelten Truhe. Python plant und prüft gegen die Höhenkarte (`Tools/claude/topo/halle_plan.py`), Unity baut aus
    /// `Data/KarethV4/KarethV4_Halle.json`: behauener Fels (ein Mesh, eine Kollision), Dielen und Steinboden, zwei Gelände-Löcher
    /// am Torgang (die 2-m-Rampe hinter der Felsstirn), Licht, Requisiten, Fundstellen der Aufträge R1, R2 und R4, Höhlenvolumen
    /// (sonst hielte WorldGround die Figur unter der Felsoberfläche für „unter der Welt“), Prüfpunkte für die Tests.
    /// Aufruf aus <see cref="KarethV4Builder"/> nach den Inhalten, vor Spielsystemen und NavMesh. Keine Netzobjekte.
    /// </summary>
    public static class KarethV4Halle
    {
        public const string RootName = "Halle des Hauptmanns (begehbar) und Salzkeller";
        public const string DatenPfad = ProjectSetup.Root + "/Data/KarethV4/KarethV4_Halle.json";

        [System.Serializable] private sealed class Q { public string n, m; public float[] c, s, r; public bool col; }
        [System.Serializable] private sealed class Knoten { public string aktion; public float x, y, z; }
        [System.Serializable] private sealed class Requisit { public string art; public float x, y, z, yaw; public int n; }
        [System.Serializable] private sealed class Volumen { public string name; public float[] min, max; }
        [System.Serializable] private sealed class Punkt { public string name; public float[] p; }
        [System.Serializable]
        private sealed class Datei
        {
            public int version; public string stand;
            public Q[] quader; public Knoten[] knoten; public Requisit[] requisiten; public Volumen[] volumina; public Punkt[] pruefpunkte;
        }

        public static string PruefAnker(int i) => "halle.p." + i;

        public static string Build(Transform parent, Terrain t)
        {
            if (!File.Exists(DatenPfad)) return "Halle: KarethV4_Halle.json fehlt";
            if (t == null) return "Halle: kein Gelände";
            string json = File.ReadAllText(DatenPfad);
            Datei d = JsonUtility.FromJson<Datei>(json);
            if (d?.quader == null) return "Halle: Plan leer";
            List<int[]> loecher = ZahlenPaare(json, "loecher");
            List<float[]> lichter = FloatListen(json, "lichter");

            Transform root = K.Group(RootName, parent);
            root.position = Vector3.zero;   // Kinder in Weltkoordinaten (Kareth v4: Wurzel im Ursprung)

            // ---- Gelände-Löcher (Terrain-Indizes, 2-m-Raster)
            TerrainData td = t.terrainData;
            int hr = td.holesResolution, n = 0;
            bool[,] holes = td.GetHoles(0, 0, hr, hr);
            foreach (int[] ij in loecher)
            {
                if (ij[0] < 0 || ij[1] < 0 || ij[0] >= hr || ij[1] >= hr) continue;
                if (holes[ij[1], ij[0]]) { holes[ij[1], ij[0]] = false; n++; }
            }
            td.SetHoles(0, 0, holes);
            EditorUtility.SetDirty(td);

            // ---- Bau: behauener Fels als ein Mesh (innen ohne Moos, außen mit), Böden und Stufen als Greybox
            Material dielen = K.Mat("KV4 Halle Dielen", new Color(0.36f, 0.25f, 0.16f), 0.12f);
            Material steinboden = K.Mat("KV4 Halle Steinboden", new Color(0.40f, 0.38f, 0.35f), 0.06f);
            Material stufe = K.Mat("KV4 Halle Steinstufe", new Color(0.47f, 0.45f, 0.41f), 0.06f);
            var innen = new FelsKit.Behauen();
            var aussen = new FelsKit.Behauen();
            Transform bau = K.Group("Bau (Greybox)", root);
            int teile = 0;
            foreach (Q q in d.quader)
            {
                var c = new Vector3(q.c[0], q.c[1], q.c[2]);
                var s = new Vector3(q.s[0], q.s[1], q.s[2]);
                var r = new Vector3(q.r[0], q.r[1], q.r[2]);
                switch (q.m)
                {
                    case "fels": innen.Add(c, s, Quaternion.Euler(r), false); break;
                    case "fels_aussen": aussen.Add(c, s, Quaternion.Euler(r), true); break;
                    default:
                        Material m = q.m == "dielen" ? dielen : q.m == "steinstufe" ? stufe : steinboden;
                        K.Box(q.n, bau, c, s, m, r, q.col);
                        break;
                }
                teile++;
            }
            Material granit = FelsKit.GranitBehauen();
            innen.Bauen("Fels der Halle (innen)", bau, granit);
            aussen.Bauen("Fels über dem Torgang (außen)", bau, granit);

            // ---- Licht (ohne Schatten, kleine Reichweite: leuchtet nicht durch den Fels)
            Transform licht = K.Group("Licht", root);
            Material flamme = K.Mat("KV4 Halle Flamme", new Color(1f, 0.62f, 0.25f), 0.2f, new Color(1f, 0.55f, 0.2f) * 1.5f);
            Material holz = K.Mat("KV4 Halle Holz", new Color(0.42f, 0.31f, 0.21f), 0.1f);
            foreach (float[] p in lichter)
            {
                Transform f = K.Group("Licht", licht, new Vector3(p[0], p[1], p[2]), Vector3.zero);
                var l = f.gameObject.AddComponent<Light>();
                l.type = LightType.Point; l.range = 12f; l.intensity = 10f;
                l.color = new Color(1f, 0.74f, 0.48f); l.shadows = LightShadows.None;
                K.Prim(PrimitiveType.Cylinder, "Halter", f, new Vector3(0f, -0.35f, 0f), new Vector3(0.08f, 0.35f, 0.08f), Vector3.zero, holz, false);
                K.Prim(PrimitiveType.Sphere, "Flamme", f, Vector3.zero, new Vector3(0.18f, 0.25f, 0.18f), Vector3.zero, flamme, false);
            }

            // ---- Requisiten
            Transform req = K.Group("Requisiten", root);
            int reqs = 0;
            foreach (Requisit r in d.requisiten) if (Requisite(req, r, holz, flamme)) reqs++;

            // ---- Fundstellen (R1 Zählbrett und Register, R2 Regal, R4 Truhe)
            Transform fund = K.Group("Fundstellen (Akt I „Das Register“)", root);
            int knoten = 0;
            foreach (Knoten kn in d.knoten)
                if (WestweidenAuftraege.KnotenAn(fund, kn.aktion, new Vector3(kn.x, kn.y, kn.z), v => { })) knoten++;

            // ---- Höhlenvolumen, Prüfpunkte
            foreach (Volumen vv in d.volumina)
            {
                var go = new GameObject("Höhlenvolumen: " + vv.name);
                go.transform.SetParent(root, false);
                var b = new Bounds();
                b.SetMinMax(new Vector3(vv.min[0], vv.min[1], vv.min[2]), new Vector3(vv.max[0], vv.max[1], vv.max[2]));
                go.transform.position = b.center;
                go.AddComponent<HoehlenVolumen>().Configure(b);
            }
            for (int i = 0; i < d.pruefpunkte.Length; i++)
            {
                var g = new GameObject("Prüfpunkt Halle: " + d.pruefpunkte[i].name);
                g.transform.SetParent(root, false);
                g.transform.position = new Vector3(d.pruefpunkte[i].p[0], d.pruefpunkte[i].p[1], d.pruefpunkte[i].p[2]);
                g.AddComponent<Ashveil.Gameplay.Story.StoryAnchor>().Configure(PruefAnker(i), 1f);
            }
            Physics.SyncTransforms();
            return $"Halle: {teile} Bauteile, {n} Geländelöcher, {lichter.Count} Lichter, {reqs} Requisiten, {knoten}/{d.knoten.Length} Fundstellen, " +
                   $"{d.volumina.Length} Höhlenvolumen, {d.pruefpunkte.Length} Prüfpunkte";
        }

        /// <summary>Prüfpunkte des Plans (Weltkoordinaten) – für Tests und Bilder.</summary>
        public static List<(string name, Vector3 p)> Pruefpunkte()
        {
            var l = new List<(string, Vector3)>();
            if (!File.Exists(DatenPfad)) return l;
            Datei d = JsonUtility.FromJson<Datei>(File.ReadAllText(DatenPfad));
            foreach (Punkt p in d.pruefpunkte) l.Add((p.name, new Vector3(p.p[0], p.p[1], p.p[2])));
            return l;
        }

        // ================================================================== Requisiten (Greybox)

        private static bool Requisite(Transform parent, Requisit r, Material holz, Material flamme)
        {
            var p = new Vector3(r.x, r.y, r.z);
            Material stein = K.Mat("KV4 Halle Herdstein", new Color(0.36f, 0.34f, 0.31f), 0.05f);
            Material dunkel = K.Mat("KV4 Halle Holz dunkel", new Color(0.26f, 0.18f, 0.12f), 0.1f);
            Material salz = K.Mat("KV4 Halle Salz", new Color(0.86f, 0.85f, 0.82f), 0.15f);
            Material sack = K.Mat("KV4 Halle Salzsack", new Color(0.74f, 0.70f, 0.60f), 0.05f);
            Material eisen = K.Mat("KV4 Halle Eisen", new Color(0.24f, 0.24f, 0.25f), 0.4f);
            Material tuch = K.Mat("KV4 Halle Banner", new Color(0.42f, 0.14f, 0.10f), 0.05f);
            Material papier = K.Mat("Paper_Old", new Color(0.82f, 0.77f, 0.64f));
            var rng = new System.Random(Mathf.RoundToInt(r.x * 31f + r.z * 17f));
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            Transform t = K.Group(r.art, parent, p, new Vector3(0f, r.yaw, 0f));
            switch (r.art)
            {
                case "herd":
                    // offener Herd an der Westwand, Rauchfang aus Stein bis an die Decke (darüber das Rauchloch im Fels)
                    K.Box("Herdplatte", t, new Vector3(0f, 0.2f, 0f), new Vector3(2.2f, 0.4f, 1.6f), stein);
                    K.Box("Glut", t, new Vector3(0f, 0.43f, 0f), new Vector3(1.2f, 0.06f, 0.8f), flamme, default, false);
                    for (int i = 0; i < 4; i++)
                        K.Prim(PrimitiveType.Cylinder, "Scheit", t, new Vector3(R(-0.4f, 0.4f), 0.5f, R(-0.25f, 0.25f)), new Vector3(0.1f, 0.4f, 0.1f),
                            new Vector3(90f, R(0f, 180f), 0f), dunkel, false);
                    K.Box("Rauchfang", t, new Vector3(0f, 3.4f, 0f), new Vector3(2.0f, 1.6f, 1.4f), stein, default, false);
                    K.Box("Rauchfang oben", t, new Vector3(0f, 4.6f, 0f), new Vector3(1.0f, 1.0f, 0.8f), stein, default, false);
                    K.Prim(PrimitiveType.Cylinder, "Kessel", t, new Vector3(0.5f, 0.62f, 0f), new Vector3(0.45f, 0.18f, 0.45f), Vector3.zero, eisen, false);
                    return true;
                case "tisch":
                    K.Box("Tischplatte", t, new Vector3(0f, 0.86f, 0f), new Vector3(3.6f, 0.08f, 1.0f), holz);
                    for (int ix = -1; ix <= 1; ix += 2)
                    for (int iz = -1; iz <= 1; iz += 2)
                        K.Block("Bein", t, new Vector3(ix * 1.6f, 0f, iz * 0.38f), new Vector3(0.12f, 0.82f, 0.12f), dunkel);
                    for (int iz = -1; iz <= 1; iz += 2)
                        K.Box("Bank", t, new Vector3(0f, 0.45f, iz * 0.85f), new Vector3(3.4f, 0.08f, 0.32f), holz);
                    K.Prim(PrimitiveType.Cylinder, "Becher", t, new Vector3(-0.9f, 0.96f, 0.1f), new Vector3(0.08f, 0.06f, 0.08f), Vector3.zero, eisen, false);
                    K.Box("Karte", t, new Vector3(0.6f, 0.905f, -0.05f), new Vector3(0.9f, 0.01f, 0.6f), papier, new Vector3(0f, 8f, 0f), false);
                    return true;
                case "regal":
                    // R2: Regal mit den alten Bänden – die mit dem grünen Rücken; dazwischen andere Bände und Lücken
                    Material gruen = K.Mat("KV4 Halle Bandrücken grün", new Color(0.16f, 0.30f, 0.18f), 0.1f);
                    Material braun = K.Mat("KV4 Halle Bandrücken braun", new Color(0.36f, 0.22f, 0.12f), 0.1f);
                    K.Box("Rückwand", t, new Vector3(0f, 1.15f, -0.2f), new Vector3(3.2f, 2.3f, 0.06f), dunkel);
                    for (int s = -1; s <= 1; s += 2) K.Box("Wange", t, new Vector3(s * 1.6f, 1.15f, 0f), new Vector3(0.08f, 2.3f, 0.46f), dunkel);
                    for (int b = 0; b < 4; b++)
                    {
                        float y = 0.12f + b * 0.62f;
                        K.Box("Brett", t, new Vector3(0f, y, 0f), new Vector3(3.2f, 0.05f, 0.44f), holz);
                        if (b == 3) break;
                        float x = -1.48f;
                        while (x < 1.45f)
                        {
                            float w = R(0.06f, 0.12f), h = R(0.36f, 0.5f);
                            if (rng.NextDouble() < 0.08) { x += 0.2f; continue; }   // Lücke
                            bool alt = b < 2 && rng.NextDouble() < 0.75;
                            K.Box("Band", t, new Vector3(x + w * 0.5f, y + 0.03f + h * 0.5f, 0.02f), new Vector3(w, h, 0.32f), alt ? gruen : braun, default, false);
                            x += w + 0.01f;
                        }
                    }
                    K.Box("Regal (Kollision)", t, new Vector3(0f, 1.15f, -0.02f), new Vector3(3.2f, 2.3f, 0.44f), dunkel).GetComponent<MeshRenderer>().enabled = false;
                    return true;
                case "ablage":
                    // Ablage, auf der das Register nachts liegt
                    K.Block("Pfosten", t, Vector3.zero, new Vector3(0.18f, 1.0f, 0.18f), dunkel);
                    K.Box("Pultplatte", t, new Vector3(0f, 1.05f, 0f), new Vector3(0.7f, 0.06f, 0.5f), holz, new Vector3(-18f, 0f, 0f));
                    return true;
                case "buchpult":
                    // R1: Buchstütze neben Veits Pult, darauf liegt das Register offen
                    K.Block("Pfosten", t, Vector3.zero, new Vector3(0.16f, 0.95f, 0.16f), dunkel);
                    K.Box("Pultplatte", t, new Vector3(0f, 1.0f, 0f), new Vector3(0.62f, 0.05f, 0.46f), holz, new Vector3(-16f, 0f, 0f));
                    K.Box("Register (offen)", t, new Vector3(0f, 1.05f, -0.01f), new Vector3(0.5f, 0.04f, 0.34f), papier, new Vector3(-16f, 0f, 0f), false);
                    K.Box("Einband", t, new Vector3(0f, 1.03f, -0.01f), new Vector3(0.54f, 0.02f, 0.37f), GruenBand(), new Vector3(-16f, 0f, 0f), false);
                    return true;
                case "banner":
                    K.Box("Banner", t, new Vector3(0f, 2.8f, 0.04f), new Vector3(1.1f, 2.6f, 0.04f), tuch, default, false);
                    K.Prim(PrimitiveType.Cylinder, "Stange", t, new Vector3(0f, 4.15f, 0.08f), new Vector3(0.05f, 0.7f, 0.05f), new Vector3(0f, 0f, 90f), dunkel, false);
                    return true;
                case "waffen":
                    K.Box("Gestell", t, new Vector3(0f, 0.9f, 0f), new Vector3(1.8f, 0.08f, 0.3f), dunkel);
                    for (int i = 0; i < 4; i++)
                        K.Box("Speer", t, new Vector3(-0.7f + i * 0.45f, 1.1f, 0.05f), new Vector3(0.04f, 2.2f, 0.04f), holz, new Vector3(R(-4f, 4f), 0f, 0f), false);
                    return true;
                case "salzsaecke":
                    for (int i = 0; i < Mathf.Max(1, r.n); i++)
                    {
                        float a = R(0f, 6.28f), rr = R(0.2f, 1.2f);
                        K.Prim(PrimitiveType.Capsule, "Salzsack", t, new Vector3(Mathf.Cos(a) * rr, 0.3f + (i % 3 == 2 ? 0.48f : 0f), Mathf.Sin(a) * rr),
                            new Vector3(0.6f, 0.38f, 0.45f), new Vector3(90f, R(0f, 180f), 0f), sack, i < 3);
                    }
                    return true;
                case "salzbloecke":
                    for (int i = 0; i < Mathf.Max(1, r.n); i++)
                        K.Box("Salzblock", t, new Vector3((i % 3) * 0.62f - 0.6f, 0.2f + (i / 3) * 0.4f, R(-0.05f, 0.05f)), new Vector3(0.58f, 0.38f, 0.4f), salz,
                            new Vector3(0f, R(-6f, 6f), 0f), i < 3);
                    return true;
                case "faesser":
                    for (int i = 0; i < Mathf.Max(1, r.n); i++)
                        K.Prim(PrimitiveType.Cylinder, "Fass", t, new Vector3(i * 0.8f, 0.5f, R(-0.1f, 0.1f)), new Vector3(0.66f, 0.5f, 0.66f), Vector3.zero, holz, true);
                    return true;
                case "truhe":
                    // R4: die versiegelte Truhe – Siegel der Halle (rotes Wachs) über dem Schloss, frisch
                    Material wachs = K.Mat("KV4 Halle Siegelwachs", new Color(0.62f, 0.08f, 0.06f), 0.35f);
                    K.Box("Truhe", t, new Vector3(0f, 0.38f, 0f), new Vector3(1.3f, 0.76f, 0.72f), dunkel);
                    K.Box("Deckel", t, new Vector3(0f, 0.8f, 0f), new Vector3(1.34f, 0.1f, 0.76f), holz, default, false);
                    for (int s = -1; s <= 1; s += 2) K.Box("Band", t, new Vector3(s * 0.45f, 0.42f, 0f), new Vector3(0.06f, 0.86f, 0.78f), eisen, default, false);
                    K.Box("Schloss", t, new Vector3(0f, 0.62f, 0.37f), new Vector3(0.18f, 0.2f, 0.04f), eisen, default, false);
                    K.Prim(PrimitiveType.Cylinder, "Siegel", t, new Vector3(0f, 0.74f, 0.39f), new Vector3(0.14f, 0.012f, 0.14f), new Vector3(90f, 0f, 0f), wachs, false);
                    K.Box("Siegelschnur", t, new Vector3(0f, 0.7f, 0.385f), new Vector3(0.02f, 0.22f, 0.01f), wachs, default, false);
                    return true;
            }
            Debug.LogWarning("[Ashveil] Halle: unbekannte Requisite " + r.art);
            return false;
        }

        private static Material GruenBand() => K.Mat("KV4 Halle Bandrücken grün", new Color(0.16f, 0.30f, 0.18f), 0.1f);

        // ------------------------------------------------------------------ kleine JSON-Leser für [[a,b],…]

        private static string ArrayText(string json, string key)
        {
            int i = json.IndexOf("\"" + key + "\"", System.StringComparison.Ordinal);
            if (i < 0) return "";
            i = json.IndexOf('[', i);
            int tiefe = 0, j = i;
            for (; j < json.Length; j++)
            {
                if (json[j] == '[') tiefe++;
                else if (json[j] == ']' && --tiefe == 0) break;
            }
            return json.Substring(i + 1, j - i - 1);
        }

        private static List<float[]> FloatListen(string json, string key)
        {
            var l = new List<float[]>();
            foreach (string teil in ArrayText(json, key).Split(']'))
            {
                string s = teil.Replace("[", "").Trim().Trim(',').Trim();
                if (s.Length == 0) continue;
                string[] f = s.Split(',');
                var a = new float[f.Length];
                for (int i = 0; i < f.Length; i++) a[i] = float.Parse(f[i].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                l.Add(a);
            }
            return l;
        }

        private static List<int[]> ZahlenPaare(string json, string key)
        {
            var l = new List<int[]>();
            foreach (float[] f in FloatListen(json, key)) if (f.Length >= 2) l.Add(new[] { (int)f[0], (int)f[1] });
            return l;
        }
    }
}
