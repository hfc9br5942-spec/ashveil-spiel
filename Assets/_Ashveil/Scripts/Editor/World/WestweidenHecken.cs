using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Westweiden – Weißdornhecken der Flur im Entwurf („Korn und Hecken“, C16; Claude 12.10.2026, VORSCHLAG).
    /// Linien aus Data/Westweiden/Entwurf/WW_Hecken.csv (topo/felder.py). Je Punkt drei über Kreuz gestellte Blattkarten
    /// (Weißdorn-Atlas der Texturprobe, Shader Ashveil/Foliage), Höhe 1,8–2,8 m, leicht wellig versetzt, damit keine Linealhecke entsteht.
    /// Netze in 512-m-Feldern zusammengefasst (Sichtbarkeitsprüfung je Feld). Je Heckenstück ein BoxCollider: die Hecke ist sichtbar
    /// und darf den Weg versperren (keine unsichtbare Wand, WORLD-114); Tore sind Lücken in der Linie.
    /// </summary>
    public static class WestweidenHecken
    {
        private const string DataDir = ProjectSetup.Root + "/Data/Westweiden/Entwurf";
        private const float Feld = 512f;

        public static string Bauen(Terrain t)
        {
            string pfad = Path.Combine(Path.GetDirectoryName(Application.dataPath), DataDir + "/WW_Hecken.csv");
            if (!File.Exists(pfad)) return "WW_Hecken.csv fehlt – keine Hecken · ";
            var linien = new Dictionary<int, List<Vector2>>();
            foreach (string line in File.ReadAllLines(pfad, Encoding.UTF8))
            {
                string[] f = line.Split(';');
                if (f.Length < 3) continue;
                int id = int.Parse(f[0], CultureInfo.InvariantCulture);
                if (!linien.TryGetValue(id, out var l)) linien[id] = l = new List<Vector2>();
                l.Add(new Vector2(float.Parse(f[1], CultureInfo.InvariantCulture), float.Parse(f[2], CultureInfo.InvariantCulture)));
            }
            Material mat = WestweidenTexturprobe.FoliageMat();
            var root = new GameObject("Hecken der Flur (Entwurf)").transform;
            var felder = new Dictionary<Vector2Int, Teil>();
            var rnd = new System.Random(1607);
            float gesamt = 0f; int karten = 0;
            var kol = new GameObject("Hecken-Kollision").transform; kol.SetParent(root, false);
            foreach (var kv in linien)
            {
                var l = kv.Value;
                if (l.Count < 2) continue;
                Vector2 a = l[0], b = l[l.Count - 1];
                float len = Vector2.Distance(a, b);
                if (len < 4f) continue;
                gesamt += len;
                Vector2 dir = (b - a) / len, quer = new Vector2(-dir.y, dir.x);
                float phase = (float)rnd.NextDouble() * 10f;
                for (float s = 0f; s <= len; s += 1.1f)
                {
                    // leichte Welle quer zur Linie (Hecke gewachsen, nicht gepflanzt nach Lineal)
                    float w = Mathf.Sin(s * 0.11f + phase) * 0.5f + ((float)rnd.NextDouble() - 0.5f) * 0.5f;
                    Vector2 p = a + dir * s + quer * w;
                    float y = t.SampleHeight(new Vector3(p.x, 0f, p.y)) + t.transform.position.y;
                    // Prüfung 12.10. (F5): Höhe in Gruppen groß/mittel/klein (1,5–3,9 m) statt gleichförmig – gewachsene Hecke, Rhythmus
                    float pn = Mathf.PerlinNoise(p.x * 0.018f + 7f, p.y * 0.018f);
                    float hoch = 1.5f + 2.0f * pn * pn + (float)rnd.NextDouble() * 0.4f;
                    var key = new Vector2Int(Mathf.FloorToInt(p.x / Feld), Mathf.FloorToInt(p.y / Feld));
                    if (!felder.TryGetValue(key, out var teil)) felder[key] = teil = new Teil();
                    for (int c = 0; c < 3; c++)
                    {
                        float ang = Mathf.Atan2(dir.y, dir.x) + c * 1.047f + ((float)rnd.NextDouble() - 0.5f) * 0.5f;
                        float breit = 1.5f + (float)rnd.NextDouble() * 0.4f;
                        var d3 = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (breit * 0.5f);
                        var p3 = new Vector3(p.x, y - 0.15f, p.y);
                        int i0 = teil.V.Count;
                        teil.V.Add(p3 - d3); teil.V.Add(p3 + d3); teil.V.Add(p3 + d3 + Vector3.up * hoch); teil.V.Add(p3 - d3 + Vector3.up * hoch);
                        teil.U.Add(new Vector2(0f, 0f)); teil.U.Add(new Vector2(0.5f, 0f)); teil.U.Add(new Vector2(0.5f, 1f)); teil.U.Add(new Vector2(0f, 1f));   // linke Atlashälfte: dichte Heckenkarte
                        var n = (Vector3.Cross(d3, Vector3.up).normalized + Vector3.up * 0.6f).normalized;
                        for (int k = 0; k < 4; k++) teil.N.Add(n);
                        teil.T.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });
                        karten++;
                    }
                    // Blütenkrone (Atlas rechts oben) auf jeder zweiten Stelle: weiße Doldenblüte auf der Heckenoberkante (C19 Frühsommer)
                    if (rnd.Next(2) == 0)
                    {
                        float ang = (float)rnd.NextDouble() * Mathf.PI;
                        var d3 = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 0.45f;
                        var p3 = new Vector3(p.x, y + hoch - 0.35f, p.y);
                        int i0 = teil.V.Count;
                        teil.V.Add(p3 - d3); teil.V.Add(p3 + d3); teil.V.Add(p3 + d3 + Vector3.up * 0.8f); teil.V.Add(p3 - d3 + Vector3.up * 0.8f);
                        teil.U.Add(new Vector2(0.5f, 0.5f)); teil.U.Add(new Vector2(1f, 0.5f)); teil.U.Add(new Vector2(1f, 1f)); teil.U.Add(new Vector2(0.5f, 1f));
                        var n = (Vector3.Cross(d3, Vector3.up).normalized + Vector3.up * 0.8f).normalized;
                        for (int k = 0; k < 4; k++) teil.N.Add(n);
                        teil.T.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });
                        karten++;
                    }
                }
                // Kollision: ein Kasten je Heckenstück (Linie ist gerade), 1,2 m breit, 2,2 m hoch
                var box = new GameObject("Hecke " + kv.Key);
                box.transform.SetParent(kol, false);
                Vector2 m = (a + b) * 0.5f;
                float ym = t.SampleHeight(new Vector3(m.x, 0f, m.y)) + t.transform.position.y;
                box.transform.SetPositionAndRotation(new Vector3(m.x, ym + 1.1f, m.y), Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y)));
                var bc = box.AddComponent<BoxCollider>();
                bc.size = new Vector3(1.2f, 2.2f + Mathf.Abs(t.SampleHeight(new Vector3(a.x, 0, a.y)) - t.SampleHeight(new Vector3(b.x, 0, b.y))), len);
            }
            foreach (var kv in felder)
            {
                var teil = kv.Value;
                var mesh = new Mesh { name = $"WW Hecken {kv.Key.x}_{kv.Key.y}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.SetVertices(teil.V); mesh.SetUVs(0, teil.U); mesh.SetNormals(teil.N); mesh.SetTriangles(teil.T, 0);
                mesh.RecalculateTangents(); mesh.RecalculateBounds();
                var go = new GameObject($"Hecken {kv.Key.x}_{kv.Key.y}");
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            return $"Hecken {(gesamt / 1000f).ToString("0.0", CultureInfo.InvariantCulture)} km, {karten} Karten in {felder.Count} Feldern · ";
        }

        private sealed class Teil
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector2> U = new List<Vector2>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<int> T = new List<int>();
        }
    }
}
