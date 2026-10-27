using System.Collections.Generic;
using System.IO;
using Ashveil.Core;
using Ashveil.Gameplay.Enemies;
using Ashveil.World;
using UnityEditor;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Westweiden, Außenstation „Kreidekeller“ (L13, Lückenliste Punkt 5, 12.10.2026 nachts): Höhle in der Steilwand für 2–3 Spieler
    /// der Stufe 13–15, offene Welt, keine Instanz (Gebietsblatt v2 §7, Aufbau VORSCHLAG). Python plant und prüft
    /// (`Tools/claude/topo/kreidekeller_plan.py` gegen die Höhenkarte), Unity baut aus `Data/Westweiden/Entwurf/WW_Kreidekeller.json`:
    ///   1 Felsband und Spalt · 2 Kornkammer mit Galerie (Hofmarken, W23) · 3 Brandungsgang mit drei Lichtfenstern und Alarmglocke ·
    ///   4 Alte Zollkammer mit Kellermeister Kord, Ladeluke und Kranbalken über der Kiesbucht (Anleger, Boot) · 5 Kaminsteig hinauf.
    /// Greybox-Quader (Kreidefels, Bruchstein, Holz), Gelände-Löcher nur am Eingang, an der Ladeluke, den Fenstern und am
    /// Kaminausgang (Fels bis zur Oberfläche aufgefüllt), Höhlenvolumen (Sturzgrenze, Wiedereinstieg), Fackellicht, Gegner
    /// (Elite, Wiederkehr 6–8 min, Kellermeister 8–12 min), Begegnung, Glocke, Fundstellen, sicherer Anker am Spalt.
    /// Aufruf aus <see cref="WestweidenZelle"/> nach den Gegnern (Weltkoordinaten, lokale Plan-Meter + Versatz), vor dem NavMesh.
    /// Keine Netzobjekte (Zellen-Regel).
    /// </summary>
    public static class WestweidenKreidekeller
    {
        public const string RootName = "Kreidekeller (Außenstation L13)";
        public const string DatenPfad = ProjectSetup.Root + "/Data/Westweiden/Entwurf/WW_Kreidekeller.json";

        [System.Serializable] private sealed class Q { public string n, m; public float[] c, s, r; public bool col; }
        [System.Serializable] private sealed class Gebiet { public string name, enemy; public float x, y, z, radius; public int count, min, max; public bool summon; }
        [System.Serializable] private sealed class Knoten { public string aktion; public float x, y, z; }
        [System.Serializable] private sealed class Glocke { public float[] a, b; public float y; public string[] verstaerkung; }
        [System.Serializable] private sealed class Boss { public string gebiet; public string[] gerufen; }
        [System.Serializable] private sealed class Requisit { public string art; public float x, y, z; public int n; }
        [System.Serializable] private sealed class Volumen { public string name; public float[] min, max; }
        [System.Serializable] private sealed class Punkt { public string name; public float[] p; }
        [System.Serializable] private sealed class Anker { public string id; public float[] p; }
        [System.Serializable]
        private sealed class Datei
        {
            public int version; public string stand;
            public Q[] quader; public Gebiet[] gebiete; public Knoten[] knoten; public Glocke glocke; public Boss boss;
            public Requisit[] requisiten; public Volumen[] volumina; public Punkt[] pruefpunkte; public Anker anker;
        }
        // JsonUtility kann keine verschachtelten Arrays (loecher, lichter): eigene kleine Leser (unten)

        public static string Build(Transform parent, Terrain t, Vector3 v, string cellId)
        {
            if (!File.Exists(DatenPfad)) return "Kreidekeller: WW_Kreidekeller.json fehlt";
            string json = File.ReadAllText(DatenPfad);
            Datei d = JsonUtility.FromJson<Datei>(json);
            if (d?.quader == null) return "Kreidekeller: Plan leer";
            List<int[]> loecher = ZahlenPaare(json, "loecher");
            List<float[]> lichter = FloatListen(json, "lichter");

            Transform root = K.Group(RootName, parent);
            root.position = Vector3.zero;   // Kinder in Weltkoordinaten
            Vector3 W(float x, float y, float z) => new Vector3(x + v.x, y + v.y, z + v.z);
            Vector3 Wa(float[] p) => W(p[0], p[1], p[2]);

            // ---- Gelände-Löcher (vor allem anderen; Terrain-lokale Indizes)
            TerrainData td = t.terrainData;
            int hr = td.holesResolution;
            bool[,] holes = td.GetHoles(0, 0, hr, hr);
            int n = 0;
            foreach (int[] ij in loecher)
            {
                if (ij[0] < 0 || ij[1] < 0 || ij[0] >= hr || ij[1] >= hr) continue;
                if (holes[ij[1], ij[0]]) { holes[ij[1], ij[0]] = false; n++; }
            }
            td.SetHoles(0, 0, holes);
            EditorUtility.SetDirty(td);

            // ---- Quader
            var mats = new Dictionary<string, Material>
            {
                { "kreide", K.Mat("WW_KK_Kreide", new Color(0.80f, 0.79f, 0.74f), 0.05f) },
                { "kreide_boden", K.Mat("WW_KK_KreideBoden", new Color(0.62f, 0.60f, 0.55f), 0.05f) },
                { "kreide_aussen", K.Mat("WW_KK_KreideAussen", new Color(0.86f, 0.85f, 0.80f), 0.05f) },
                { "mauer", K.Mat("WW_KK_Bruchstein", new Color(0.52f, 0.48f, 0.42f), 0.05f) },
                { "mauer_boden", K.Mat("WW_KK_Steinboden", new Color(0.42f, 0.40f, 0.36f), 0.05f) },
                { "holz", K.Mat("WW_KK_Holz", new Color(0.42f, 0.31f, 0.21f), 0.1f) },
                { "seil", K.Mat("WW_KK_Seil", new Color(0.62f, 0.54f, 0.38f), 0.05f) },
                { "korn", K.Mat("WW_KK_Kornsack", new Color(0.78f, 0.70f, 0.52f), 0.05f) },
                { "rot", K.Mat("WW_KK_Hofzeichen_Rot", new Color(0.62f, 0.18f, 0.12f)) },
                { "blau", K.Mat("WW_KK_Hofzeichen_Blau", new Color(0.22f, 0.32f, 0.62f)) },
                { "messing", K.Mat("WW_KK_Messing", new Color(0.72f, 0.58f, 0.30f), 0.6f) },
                { "fackel", K.Mat("WW_KK_Fackel", new Color(1f, 0.62f, 0.25f), 0.2f, new Color(1f, 0.55f, 0.2f) * 1.5f) },
            };
            Transform bau = K.Group("Bau (Greybox)", root);
            int quader = 0;
            foreach (Q q in d.quader)
            {
                bool unsichtbar = q.m == "unsichtbar_seil";
                Material m = mats.TryGetValue(unsichtbar ? "seil" : q.m, out Material mm) ? mm : mats["kreide"];
                GameObject g = K.Box(q.n, bau, W(q.c[0], q.c[1], q.c[2]), new Vector3(q.s[0], q.s[1], q.s[2]), m, new Vector3(q.r[0], q.r[1], q.r[2]), q.col);
                // Halteseil: Kollision genau am sichtbaren Seilgeländer (Pflöcke + Seil), kein eigener Körper (WORLD-114: sichtbar)
                if (unsichtbar) Object.DestroyImmediate(g.GetComponent<MeshRenderer>());
                quader++;
            }

            // ---- Licht: Fackeln (ohne Schatten, kleine Reichweite – leuchtet nicht durch den Fels nach oben)
            Transform licht = K.Group("Fackeln", root);
            foreach (float[] p in lichter)
            {
                Transform f = K.Group("Fackel", licht, W(p[0], p[1], p[2]), Vector3.zero);
                var l = f.gameObject.AddComponent<Light>();
                l.type = LightType.Point; l.range = 14f; l.intensity = 14f;   // Lauf 487: 3 war zu dunkel (Gegner als Schattenriss, Regel 19)
                l.color = new Color(1f, 0.74f, 0.48f); l.shadows = LightShadows.None;
                K.Prim(PrimitiveType.Cylinder, "Fackelschaft", f, new Vector3(0f, -0.35f, 0f), new Vector3(0.08f, 0.35f, 0.08f), Vector3.zero, mats["holz"], false);
                K.Prim(PrimitiveType.Sphere, "Flamme", f, Vector3.zero, new Vector3(0.18f, 0.25f, 0.18f), Vector3.zero, mats["fackel"], false);
            }

            // ---- Requisiten
            Transform req = K.Group("Requisiten", root);
            var rng = new System.Random(1301);
            foreach (Requisit r in d.requisiten)
            {
                Vector3 p = W(r.x, r.y, r.z);
                switch (r.art)
                {
                    case "kornsaecke":
                        Transform h = K.Group("Kornsäcke", req, p, Vector3.zero);
                        for (int i = 0; i < r.n; i++)
                        {
                            float a = (float)rng.NextDouble() * 6.28f, rr = 0.4f + (float)rng.NextDouble() * 1.3f;
                            K.Prim(PrimitiveType.Capsule, "Kornsack", h, new Vector3(Mathf.Cos(a) * rr, 0.3f + (i % 3 == 2 ? 0.5f : 0f), Mathf.Sin(a) * rr),
                                new Vector3(0.6f, 0.38f, 0.45f), new Vector3(90f, (float)rng.NextDouble() * 180f, 0f), mats["korn"], i < 3);
                        }
                        break;
                    case "zollkiste":
                        K.Box("Zollkiste", req, p + new Vector3(0f, 0.45f, 0f), new Vector3(1.4f, 0.9f, 0.8f), mats["holz"]);
                        K.Box("Beschlag", req, p + new Vector3(0f, 0.92f, 0f), new Vector3(1.45f, 0.06f, 0.1f), mats["messing"], default, false);
                        break;
                    case "wappen":
                        // Wappenrest des alten Königreichs: abgeschlagen, nur der Rand ist noch zu sehen (Lore offen – Max)
                        K.Box("Wappenrest", req, p + new Vector3(0f, 3.2f, 0.25f), new Vector3(1.6f, 2.0f, 0.15f), mats["mauer"], default, false);
                        break;
                    case "seilwinde":
                        K.Prim(PrimitiveType.Cylinder, "Seilwinde", req, p + new Vector3(0f, 0.9f, 0f), new Vector3(0.6f, 0.5f, 0.6f), new Vector3(0f, 0f, 90f), mats["holz"], true);
                        K.Box("Windengestell", req, p + new Vector3(0f, 0.6f, 0f), new Vector3(0.2f, 1.2f, 1.4f), mats["holz"]);
                        break;
                    case "glocke":
                        K.Prim(PrimitiveType.Cylinder, "Alarmglocke", req, p + new Vector3(0f, 2.2f, 0f), new Vector3(0.45f, 0.3f, 0.45f), Vector3.zero, mats["messing"], false);
                        break;
                }
            }

            // ---- Gegner, Begegnung, Glocke
            Dictionary<string, EnemyBuilder.BorderEnemy> arten = WestweidenGegner.LetzteArten ?? EnemyBuilder.BuildWestweidenCreatures();
            Transform geg = K.Group("Gegner (Kreidekeller)", root);
            var gebiete = new Dictionary<string, EnemySpawnArea>();
            int gegner = 0, gerufen = 0;
            var fehlt = new List<string>();
            foreach (Gebiet g in d.gebiete)
            {
                if (!arten.TryGetValue(g.enemy, out EnemyBuilder.BorderEnemy art) || art?.Prefab == null) { fehlt.Add(g.enemy); continue; }
                EnemySpawnArea a = EnemyBuilder.PlaceArea(geg, g.name, W(g.x, g.y + 0.2f, g.z), art, Mathf.Max(1, g.count), Mathf.Max(0.5f, g.radius), "", g.min, g.max);
                if (g.summon) { K.SetBool(a, "summonOnly", true); gerufen++; }
                else gegner += g.count;
                gebiete[g.name] = a;
            }
            string begegnung = "–";
            if (d.boss != null && gebiete.TryGetValue(d.boss.gebiet, out EnemySpawnArea bossArea))
            {
                var go = new GameObject("Begegnung: Kellermeister Kord");
                go.transform.SetParent(root, false);
                go.transform.position = bossArea.transform.position;
                var enc = go.AddComponent<KellermeisterEncounter>();
                var so = new SerializedObject(enc);
                so.FindProperty("bossArea").objectReferenceValue = bossArea;
                so.FindProperty("bossName").stringValue = "Kellermeister Kord";
                so.FindProperty("offeneWelt").boolValue = true;
                SerializedProperty arr = so.FindProperty("gerufen");
                var liste = new List<EnemySpawnArea>();
                foreach (string s in d.boss.gerufen) if (gebiete.TryGetValue(s, out EnemySpawnArea ga)) liste.Add(ga);
                arr.arraySize = liste.Count;
                for (int i = 0; i < liste.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = liste[i];
                so.ApplyModifiedPropertiesWithoutUndo();
                begegnung = $"Kellermeister mit {liste.Count} Gerufenen";
            }
            string glocke = "–";
            if (d.glocke != null)
            {
                var go = new GameObject("Alarmglocke (Spannseil)");
                go.transform.SetParent(root, false);
                Vector3 a = Wa(d.glocke.a), b = Wa(d.glocke.b);
                go.transform.position = (a + b) * 0.5f;
                var liste = new List<EnemySpawnArea>();
                foreach (string s in d.glocke.verstaerkung) if (gebiete.TryGetValue(s, out EnemySpawnArea ga)) liste.Add(ga);
                go.AddComponent<AlarmGlocke>().Configure(a, b, liste.ToArray());
                // Spannseil sichtbar (knöchelhoch, gelbliches Seil), ohne Kollision – man stolpert hinein, nicht darüber
                Vector3 dd = b - a;
                K.Box("Spannseil", go.transform, Vector3.zero, new Vector3(0.04f, 0.04f, dd.magnitude), mats["seil"],
                    new Vector3(0f, Mathf.Atan2(dd.x, dd.z) * Mathf.Rad2Deg, 0f), false);
                glocke = $"{liste.Count} Verstärkung";
            }

            // ---- Fundstellen (Hofmarken, W23)
            int knoten = 0;
            Transform fund = K.Group("Fundstellen (Hofmarken)", root);
            foreach (Knoten kn in d.knoten)
            {
                string farbe = kn.aktion.EndsWith("rot") ? "rot" : kn.aktion.EndsWith("blau") ? "blau" : "korn";
                if (WestweidenAuftraege.KnotenAn(fund, kn.aktion, W(kn.x, kn.y, kn.z), vis =>
                    {
                        K.Prim(PrimitiveType.Capsule, "Kornsack mit Hofmarke", vis, new Vector3(0f, 0.32f, 0f), new Vector3(0.65f, 0.4f, 0.5f), new Vector3(90f, 0f, 0f), mats["korn"], false);
                        K.Box("Hofmarke", vis, new Vector3(0f, 0.55f, 0.22f), new Vector3(0.3f, 0.3f, 0.03f), mats[farbe], default, false);
                    })) knoten++;
            }

            // ---- Höhlenvolumen, sicherer Anker
            int vol = 0;
            foreach (Volumen vv in d.volumina)
            {
                Vector3 mn = Wa(vv.min), mx = Wa(vv.max);
                var go = new GameObject("Höhlenvolumen: " + vv.name);
                go.transform.SetParent(root, false);
                go.transform.position = (mn + mx) * 0.5f;
                var b = new Bounds();
                b.SetMinMax(mn, mx);
                go.AddComponent<HoehlenVolumen>().Configure(b);
                vol++;
            }
            // Prüfpunkte als Anker „kk.p.<i>“ (Tests: Wegnetz und Stehen in der Höhle)
            for (int i = 0; i < d.pruefpunkte.Length; i++)
            {
                var g = new GameObject("Prüfpunkt Kreidekeller: " + d.pruefpunkte[i].name);
                g.transform.SetParent(root, false);
                g.transform.position = Wa(d.pruefpunkte[i].p);
                g.AddComponent<Ashveil.Gameplay.Story.StoryAnchor>().Configure(PruefAnker(i), 1f);
            }
            if (d.anker?.p != null)
            {
                var g = new GameObject("Anker Kreidekeller (Spalt)");
                g.transform.SetParent(root, false);
                g.transform.position = Wa(d.anker.p);
                g.AddComponent<SafeSpawnAnchor>().Configure(d.anker.id, cellId);
            }
            AssetDatabase.SaveAssets();
            Physics.SyncTransforms();
            string r0 = $"Kreidekeller: {quader} Bauteile, {n} Geländelöcher, {lichter.Count} Fackeln, {gegner} Gegner + {gerufen} gerufen, " +
                        $"{begegnung}, Glocke ({glocke}), {knoten}/{d.knoten.Length} Hofmarken, {vol} Höhlenvolumen";
            if (fehlt.Count > 0) r0 += " – unbekannte Gegner: " + string.Join(", ", fehlt);
            return r0;
        }

        public static string PruefAnker(int i) => "kk.p." + i;

        /// <summary>Prüfpunkte des Plans (lokale Meter) – für Tests und Bilder.</summary>
        public static List<(string name, Vector3 lokal)> Pruefpunkte()
        {
            var l = new List<(string, Vector3)>();
            if (!File.Exists(DatenPfad)) return l;
            Datei d = JsonUtility.FromJson<Datei>(File.ReadAllText(DatenPfad));
            foreach (Punkt p in d.pruefpunkte) l.Add((p.name, new Vector3(p.p[0], p.p[1], p.p[2])));
            return l;
        }

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
                string s = teil.Replace("[", "").Trim(',', ' ');
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
