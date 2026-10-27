using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Westweiden – Eichen als Überhälter in den Hecken (Lückenliste Punkt 6, 13.10.2026, VORSCHLAG): auf den Heckenlinien aus
    /// WW_Hecken.csv in unregelmäßigen Abständen (180–320 m, über die Heckenstücke hinweg gezählt; nur auf Stücken ab 20 m), nicht neben Bauten.
    /// Eine Heckenlandschaft mit einzelnen alten Bäumen liest sich aus der Ferne als Flur (senkrechte Marken in der Weite).
    /// Modell: <see cref="EicheGenerator"/>. Aufruf nach den Hecken (Entwurf und Zelle), Zellen-Koordinaten.
    /// </summary>
    public static class WestweidenEichen
    {
        private const string DataDir = ProjectSetup.Root + "/Data/Westweiden/Entwurf";

        public static string Bauen(Terrain t)
        {
            string pfad = Path.Combine(Path.GetDirectoryName(Application.dataPath), DataDir + "/WW_Hecken.csv");
            if (!File.Exists(pfad)) return "Eichen: WW_Hecken.csv fehlt · ";
            if (EicheGenerator.Vorlage(0) == null) return "Eichen: Modell fehlt · ";
            var linien = new Dictionary<int, (Vector2 a, Vector2 b)>();
            foreach (string line in File.ReadAllLines(pfad, Encoding.UTF8))
            {
                string[] f = line.Split(';');
                if (f.Length < 3 || !int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) continue;
                var p = new Vector2(float.Parse(f[1], CultureInfo.InvariantCulture), float.Parse(f[2], CultureInfo.InvariantCulture));
                linien[id] = linien.TryGetValue(id, out var l) ? (l.a, p) : (p, p);
            }
            var root = new GameObject("Eichen in den Hecken (Entwurf)").transform;
            var rng = new System.Random(2718);
            Physics.SyncTransforms();
            int n = 0, neben = 0;
            float rest = 90f;   // Abstand bis zur nächsten Eiche, über Linien hinweg (so liegen sie nicht alle am Linienanfang)
            var ids = new List<int>(linien.Keys);
            ids.Sort();
            var gesetzt = new List<Vector2>();
            foreach (int id in ids)
            {
                (Vector2 a, Vector2 b) = linien[id];
                float len = Vector2.Distance(a, b);
                if (len < 20f) { rest -= len; continue; }   // Heckenstücke sind 20–53 m lang: Abstand läuft über die Stücke weiter
                Vector2 dir = (b - a) / len;
                float pos = Mathf.Max(8f, rest);
                for (; pos < len - 8f; pos += 180f + (float)rng.NextDouble() * 140f)
                {
                    Vector2 p = a + dir * pos;
                    if (gesetzt.Exists(q => Vector2.Distance(q, p) < 90f)) continue;
                    float y = t.SampleHeight(new Vector3(p.x, 0f, p.y)) + t.transform.position.y;
                    Vector3 tp = t.transform.position;
                    if (t.terrainData.GetSteepness((p.x - tp.x) / t.terrainData.size.x, (p.y - tp.z) / t.terrainData.size.z) > 25f) continue;
                    // nicht neben Bauten, Brücken oder Fundorten (Kollider außer Hecken im Umkreis von 12 m)
                    bool frei = true;
                    foreach (Collider c in Physics.OverlapSphere(new Vector3(p.x, y + 1.5f, p.y), 12f))
                    {
                        if (c is TerrainCollider) continue;
                        if (Hecke(c.transform)) continue;
                        frei = false; break;
                    }
                    if (!frei) { neben++; continue; }
                    var vorlage = EicheGenerator.Vorlage(rng.Next(EicheGenerator.Varianten));
                    var baum = (GameObject)PrefabUtility.InstantiatePrefab(vorlage, root);
                    baum.name = "Eiche";
                    baum.transform.SetPositionAndRotation(new Vector3(p.x, y - 0.2f, p.y), Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
                    float g = 0.85f + (float)rng.NextDouble() * 0.35f;
                    baum.transform.localScale = new Vector3(g, g, g);
                    gesetzt.Add(p);
                    n++;
                }
                rest = pos - len;
            }
            return $"Eichen in den Hecken: {n} ({neben} neben Bauten ausgelassen) · ";
        }

        private static bool Hecke(Transform tr)
        {
            for (Transform p = tr; p != null; p = p.parent)
                if (p.name.StartsWith("Hecke", System.StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
