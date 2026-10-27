using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Westweiden – Füllung im Entwurf (Claude 12.10.2026, Füllplan v1, VORSCHLAG): Fundorte und Erzählspuren, Grenzsteine,
    /// Wanderfindlinge in der Tonpfanne (Unikat-Wunder WORLD-224), Kopfweiden an den Gräben, Herden als Maßstab (Prüfung F7),
    /// Erz-Fundstellen. Alles Greybox (Formen und Lage zählen, Modelle folgen). Daten: Data/Westweiden/Entwurf/WW_Fuelle.csv.
    /// Herden ohne Kollision (Kulissentiere; echte Tiere kommen mit den Figuren), Findlinge, Steine und Bäume mit Kollision.
    /// </summary>
    public static class WestweidenFuelle
    {
        private const string DataDir = ProjectSetup.Root + "/Data/Westweiden/Entwurf";

        /// <summary>14.10.2026: Herden als bewegte Tiere (Schaf, Rind, Pferd) statt Greybox-Quadern. Aus = alte Quader.</summary>
        public static readonly bool Lebendig = true;

        public static string Bauen(Terrain t, System.Func<string, Color, Material> farbe)
        {
            string pfad = Path.Combine(Path.GetDirectoryName(Application.dataPath), DataDir + "/WW_Fuelle.csv");
            if (!File.Exists(pfad)) return "WW_Fuelle.csv fehlt · ";
            var root = new GameObject("Füllung (Füllplan v1)").transform;
            Material fund = farbe("Fundort", new Color(0.78f, 0.55f, 0.25f));
            Material stein = farbe("Stein", new Color(0.58f, 0.57f, 0.53f));
            Material kreide = farbe("Findling", new Color(0.66f, 0.65f, 0.61f));
            Material rinde = farbe("Rinde", new Color(0.30f, 0.26f, 0.21f));
            Material weide = farbe("Weidenlaub", new Color(0.42f, 0.52f, 0.36f));
            Material fell = farbe("Fell", new Color(0.45f, 0.30f, 0.20f));
            Material rind = farbe("Rinderfell", new Color(0.55f, 0.26f, 0.15f));
            Material wolle = farbe("Wolle", new Color(0.86f, 0.84f, 0.78f));
            Material kupfer = farbe("Kupfer", new Color(0.55f, 0.42f, 0.30f));
            var rnd = new System.Random(4711);
            var z = new Dictionary<string, int>();
            Transform herden = null;
            int tiere = 0;
            foreach (string line in File.ReadAllLines(pfad, Encoding.UTF8))
            {
                string[] f = line.Split(';');
                if (f.Length < 7) continue;
                float x = F(f[2]), y = F(f[3]);
                string typ = f[0], name = f[1];
                z[typ] = z.TryGetValue(typ, out int c) ? c + 1 : 1;
                switch (typ)
                {
                    case "fund":
                        var g = Teil(t, root, "Fundort: " + name, PrimitiveType.Cube, x, y, new Vector3(1.4f, 1.1f, 1.4f), (float)rnd.NextDouble() * 90f, 0.5f, fund, true);
                        Teil(t, g.transform.parent, "Fundort-Stange: " + name, PrimitiveType.Cylinder, x + 1.2f, y, new Vector3(0.12f, 1.4f, 0.12f), 0f, 1.4f, fund, false);
                        break;
                    case "grenzstein":
                        Teil(t, root, "Grenzstein", PrimitiveType.Cube, x, y, new Vector3(0.5f, 1.3f, 0.3f), (float)rnd.NextDouble() * 30f, 0.55f, stein, true);
                        break;
                    case "findling":
                    {
                        float rich = F(f[4]), gr = F(f[6]);
                        // Findling: Kreideblock, halb im Ton, Längsachse in Zugrichtung; dahinter liegt die Schleifspur (Bodenschicht)
                        var s = Teil(t, root, "Wanderfindling", PrimitiveType.Sphere, x, y, new Vector3(gr * 1.6f, gr * 1.1f, gr * 2.0f), 90f - rich, gr * 0.25f, kreide, true);
                        s.transform.rotation = Quaternion.Euler((float)rnd.NextDouble() * 10f, 90f - rich, (float)rnd.NextDouble() * 10f);
                        break;
                    }
                    case "kopfweide":
                    {
                        float hs = 2.2f + (float)rnd.NextDouble() * 0.6f;
                        // 13.10.2026 (Punkt 6): eigenes Modell (KopfweideGenerator, drei Varianten); Greybox nur, wenn es fehlt
                        int variante = z[typ] % KopfweideGenerator.Varianten;
                        GameObject vorlage = KopfweideGenerator.Vorlage(variante);
                        if (vorlage != null)
                        {
                            var rw = new System.Random(911 + z[typ] * 17);   // eigener Zufall: Herden und Findlinge bleiben, wo sie waren
                            var baum = (GameObject)PrefabUtility.InstantiatePrefab(vorlage, root);
                            float boden = t.SampleHeight(new Vector3(x, 0f, y)) + t.transform.position.y;
                            baum.transform.SetPositionAndRotation(new Vector3(x, boden - 0.1f, y), Quaternion.Euler(0f, (float)rw.NextDouble() * 360f, 0f));
                            float skal = 0.9f + (float)rw.NextDouble() * 0.25f;
                            baum.transform.localScale = new Vector3(skal, skal * (0.95f + (float)rw.NextDouble() * 0.1f), skal);
                            baum.name = "Kopfweide";
                            break;
                        }
                        Teil(t, root, "Kopfweide Stamm", PrimitiveType.Cylinder, x, y, new Vector3(0.9f, hs * 0.5f, 0.9f), 0f, hs * 0.5f - 0.1f, rinde, true);
                        Teil(t, root, "Kopfweide Krone", PrimitiveType.Sphere, x, y, new Vector3(3.4f, 2.8f, 3.4f), 0f, hs + 1.0f, weide, false);
                        break;
                    }
                    case "herde":
                    {
                        string art = f[4]; int n = int.Parse(f[5], CultureInfo.InvariantCulture);
                        Vector3 groesse = art == "pferd" ? new Vector3(0.6f, 1.6f, 2.2f) : art == "rind" ? new Vector3(0.8f, 1.3f, 2.0f) : new Vector3(0.6f, 0.75f, 1.1f);
                        Material m = art == "pferd" ? fell : art == "rind" ? rind : wolle;
                        float yaw0 = (float)rnd.NextDouble() * 360f;
                        for (int i = 0; i < n; i++)
                        {
                            float r = i == 0 ? 0f : 4f + (float)rnd.NextDouble() * 18f, a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                            float k = i == 0 ? 1.1f : 0.9f + (float)rnd.NextDouble() * 0.2f;   // Leittier etwas größer
                            float yawT = yaw0 + (float)rnd.NextDouble() * 50f - 25f;
                            if (Lebendig)
                            {
                                // 14.10.2026 (Punkt 6): lebende Herde – grast im Weidekreis um die Herdenmitte, nachts eng beieinander
                                float rn = r * 0.22f;
                                Vector3 Boden(float px, float pz) => new Vector3(px, t.SampleHeight(new Vector3(px, 0f, pz)) + t.transform.position.y, pz);
                                if (herden == null) { herden = new GameObject("Herden (Westweiden)").transform; herden.SetParent(root, false); }
                                SandboxSceneBuilder.WestweidenHerdentier(herden, art,
                                    (i == 0 ? "Leittier " : "Herdentier ") + name + " " + (i + 1), Boden(x, y), Boden(x + Mathf.Cos(a) * rn, y + Mathf.Sin(a) * rn),
                                    art == "pferd" ? 9f : 14f, yawT, k, 6100 + z[typ] * 37 + i);
                                tiere++;
                                continue;
                            }
                            Teil(t, root, (i == 0 ? "Leittier " : "Herdentier ") + name, PrimitiveType.Cube, x + Mathf.Cos(a) * r, y + Mathf.Sin(a) * r,
                                 groesse * k, yawT, groesse.y * k * 0.5f + 0.1f, m, false);
                        }
                        break;
                    }
                    case "erz":
                        Teil(t, root, "Erz: " + name, PrimitiveType.Sphere, x, y, new Vector3(1.6f, 1.0f, 1.3f), 0f, 0.25f, kupfer, true);
                        break;
                }
            }
            var sb = new StringBuilder("Füllung: ");
            foreach (var kv in z) sb.Append(kv.Value).Append(' ').Append(kv.Key).Append(", ");
            string text = sb.ToString().TrimEnd(',', ' ');
            if (tiere > 0) text += $", davon {tiere} Herdentiere mit Tagesablauf";
            return text + " · ";
        }

        /// <summary>
        /// Zelle: Nach dem Verschieben an die Lage in Kareth tragen die Tagespläne der Herdentiere noch die alten Lagen (Weltkoordinaten
        /// im Plan) – hier um denselben Versatz nachziehen. Gibt eine Meldung zurück.
        /// </summary>
        public static string TiereVerschieben(Transform root, Vector3 versatz)
        {
            int n = 0;
            foreach (Ashveil.Gameplay.Npcs.AnimalRoutine r in root.GetComponentsInChildren<Ashveil.Gameplay.Npcs.AnimalRoutine>(true))
            {
                var so = new UnityEditor.SerializedObject(r);
                SerializedProperty st = so.FindProperty("steps");
                for (int i = 0; i < st.arraySize; i++)
                {
                    SerializedProperty c = st.GetArrayElementAtIndex(i).FindPropertyRelative("center");
                    c.vector3Value += versatz;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                n++;
            }
            return n > 0 ? $"{n} Herdentiere an die Lage in Kareth nachgezogen · " : "";
        }

        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        private static GameObject Teil(Terrain t, Transform parent, string name, PrimitiveType p, float x, float y, Vector3 s, float yaw, float hoch, Material m, bool kollision)
        {
            var g = GameObject.CreatePrimitive(p);
            g.name = name; g.transform.SetParent(parent, false);
            float b = t.SampleHeight(new Vector3(x, 0f, y)) + t.transform.position.y;
            g.transform.SetPositionAndRotation(new Vector3(x, b + hoch, y), Quaternion.Euler(0f, yaw, 0f));
            g.transform.localScale = s;
            g.GetComponent<Renderer>().sharedMaterial = m;
            if (!kollision) Object.DestroyImmediate(g.GetComponent<Collider>());
            return g;
        }
    }
}
