using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Ashveil.Core;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Westweiden – Bodenbewuchs (Lückenliste Punkt 7, 13.10.2026): Gras auf den Weiden und Korn auf den Äckern als Gelände-Details,
    /// mit denselben Vorlagen wie Kareth (<see cref="GrasPatchGenerator"/>, <see cref="FeldPatchGenerator"/>), damit der Übergang
    /// über den Weidepass bruchlos ist (CORE-009: aus dem dichten Kareth-Gras auf nackten Boden wäre ein Bruch).
    /// Grundlage ist die Bodenmischung der Zelle (WW_Splat_2048_gz.bytes, 7 Familien: Weidegras A–D, Kreidefels, Weg, Acker) –
    /// Detail- und Bodenraster sind gleich (2048²), eine Zelle ≈ 2,05 × 1,66 m.
    ///   Acker (Anteil > 0,5): Korn, drei Flecken je Zelle, selten eine Lücke (wie die Kareth-Felder).
    ///   Weide: Gras nach Grasanteil, ohne Fels; auf Wegen keins, am Wegrand ausgedünnt (Wege bleiben lesbar, Gameplay vor Optik);
    ///          großflächig leicht wechselnd (Rauschen 0,7–1,0), damit keine Rasenfläche entsteht.
    ///   Röhricht (Rohrkolben und Blätter) am Ufer des Mühlbachs (2 Zellen neben dem Wasserband), Heide auf dem Kreidefels-Rand
    ///   (Felsanteil 0,1–0,8: Steilrand, Kehrental), Mohn am Ackerrand (Anteil 0,15–0,5) und vereinzelt im Korn.
    ///   Kein Bewuchs unter Bauten (Kollider auf dem Boden, gedrehte Kästen genau), unter Wasser (Wasserflächen der Zelle) und im
    ///   Mühlbach (sein Wasserband ohne Wasserfläche – watbar).
    /// Aufruf in <see cref="WestweidenZelle"/> und im Entwurf nach Orten, Wasser, Hecken und Füllplan – vor dem Verschieben.
    /// Pflanzen-Vorlagen aus <see cref="FeldPatchGenerator"/> (Röhricht, Heide, Mohn: eigene Arten, 13.10.2026).
    /// </summary>
    public static class WestweidenBewuchs
    {
        private const string SplatPfad = ProjectSetup.Root + "/Data/Westweiden/Entwurf/WW_Splat_2048_gz.bytes";
        private const int Res = 2048, L = 7, Fels = 4, Weg = 5, Acker = 6;

        public static string Bauen(Terrain t)
        {
            GameObject gras = GrasPatchGenerator.Vorlage(), korn = FeldPatchGenerator.WestweidenKorn() ?? FeldPatchGenerator.Korn();
            GameObject roehricht = FeldPatchGenerator.Roehricht(), heide = FeldPatchGenerator.Heide(), mohn = FeldPatchGenerator.Mohn();
            if (gras == null) return "Bewuchs: Gras-Vorlage fehlt · ";
            string abs = Path.Combine(Path.GetDirectoryName(Application.dataPath), SplatPfad);
            if (!File.Exists(abs)) return "Bewuchs: Bodenmischung fehlt · ";
            byte[] sb;
            using (var fs = File.OpenRead(abs))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var ms = new MemoryStream()) { gz.CopyTo(ms); sb = ms.ToArray(); }
            if (sb.Length != Res * Res * L) return $"Bewuchs: Bodenmischung hat falsche Größe ({sb.Length}) · ";

            TerrainData d = t.terrainData;
            Vector3 tp = t.transform.position, ts = d.size;
            float cx = ts.x / Res, cz = ts.z / Res;
            bool[] frei = Freiraum(t, cx, cz, out int bebaut, out int nass, out bool[] ufer);

            d.SetDetailResolution(Res, 32);
            d.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            var protos = new List<DetailPrototype>
            {
                new DetailPrototype { prototype = gras, usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit, useInstancing = true,
                    minWidth = 1.1f, maxWidth = 1.7f, minHeight = 0.8f, maxHeight = 1.3f, noiseSpread = 0.45f, alignToGround = 0.6f }
            };
            int kornL = -1;
            if (korn != null)
            {
                kornL = protos.Count;
                protos.Add(new DetailPrototype { prototype = korn, usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit, useInstancing = true,
                    minWidth = 1.35f, maxWidth = 1.7f, minHeight = 1.3f, maxHeight = 1.55f, noiseSpread = 0.3f, alignToGround = 0.15f });
            }
            int roehrichtL = -1, heideL = -1, mohnL = -1;
            if (roehricht != null)
            {
                roehrichtL = protos.Count;
                protos.Add(new DetailPrototype { prototype = roehricht, usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit, useInstancing = true,
                    minWidth = 1.1f, maxWidth = 1.5f, minHeight = 1.5f, maxHeight = 2.0f, noiseSpread = 0.4f, alignToGround = 0.1f });
            }
            if (heide != null)
            {
                heideL = protos.Count;
                protos.Add(new DetailPrototype { prototype = heide, usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit, useInstancing = true,
                    minWidth = 1.0f, maxWidth = 1.5f, minHeight = 0.55f, maxHeight = 0.8f, noiseSpread = 0.5f, alignToGround = 0.7f });
            }
            if (mohn != null)
            {
                mohnL = protos.Count;
                protos.Add(new DetailPrototype { prototype = mohn, usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit, useInstancing = true,
                    minWidth = 0.9f, maxWidth = 1.3f, minHeight = 0.9f, maxHeight = 1.2f, noiseSpread = 0.5f, alignToGround = 0.3f });
            }
            d.detailPrototypes = protos.ToArray();

            var g = new int[Res, Res];
            var k = new int[Res, Res];
            var ro = new int[Res, Res];
            var he = new int[Res, Res];
            var mo = new int[Res, Res];
            var rng = new System.Random(5113);
            long grasN = 0, kornN = 0, roN = 0, heN = 0, moN = 0;
            for (int z = 0; z < Res; z++)
                for (int x = 0; x < Res; x++)
                {
                    if (!frei[z * Res + x]) continue;
                    int i = (z * Res + x) * L;
                    float s = 0f;
                    for (int q = 0; q < L; q++) s += sb[i + q];
                    if (s <= 0f) continue;
                    float wiese = (sb[i] + sb[i + 1] + sb[i + 2] + sb[i + 3]) / s, weg = sb[i + Weg] / s, acker = sb[i + Acker] / s, fels = sb[i + Fels] / s;
                    if (acker > 0.5f && kornL >= 0)
                    {
                        int c0 = rng.NextDouble() < 0.06 ? 2 : 3;
                        k[z, x] = c0; kornN += c0;
                        if (mohnL >= 0 && rng.NextDouble() < 0.015) { mo[z, x] = 1; moN++; }   // vereinzelt Mohn im Korn
                        continue;
                    }
                    if (weg > 0.35f) continue;
                    if (ufer[z * Res + x] && roehrichtL >= 0)
                    {
                        // Ufer des Mühlbachs: Röhricht statt Gras (dicht, mit kleinen Lücken)
                        if (rng.NextDouble() < 0.8) { int c1 = rng.NextDouble() < 0.5 ? 2 : 1; ro[z, x] = c1; roN += c1; }
                        continue;
                    }
                    if (mohnL >= 0 && acker > 0.15f && rng.NextDouble() < 0.35) { mo[z, x] = 1; moN++; }   // Ackerrand
                    if (heideL >= 0 && fels > 0.1f && fels < 0.8f && rng.NextDouble() < 0.25 + 0.6 * (1f - Mathf.Abs(fels - 0.4f) / 0.4f) &&
                        d.GetSteepness((x + 0.5f) / Res, (z + 0.5f) / Res) < 38f)   // nicht an der Felswand
                    {
                        int c2 = rng.NextDouble() < 0.4 ? 2 : 1;
                        he[z, x] = c2; heN += c2;
                    }
                    float wx = tp.x + (x + 0.5f) * cx, wz = tp.z + (z + 0.5f) * cz;
                    // 14.10.: dichter (vorher 0,7–1,0 × 0,9 – zwischen den Büscheln blieb viel nackter Boden; eine Weide ist eine geschlossene Narbe)
                    float dens = Mathf.Clamp01(wiese * (1f - fels * 1.5f)) * (0.85f + 0.15f * Mathf.PerlinNoise(wx * 0.013f + 31f, wz * 0.013f + 7f));
                    dens *= 1f - Mathf.Clamp01(weg / 0.35f) * 0.8f;
                    if (dens < 0.05f) continue;
                    int c = 0;
                    for (int r = 0; r < 3; r++) if (rng.NextDouble() < dens) c++;
                    g[z, x] = c; grasN += c;
                }
            d.SetDetailLayer(0, 0, 0, g);
            if (kornL >= 0) d.SetDetailLayer(0, 0, kornL, k);
            if (roehrichtL >= 0) d.SetDetailLayer(0, 0, roehrichtL, ro);
            if (heideL >= 0) d.SetDetailLayer(0, 0, heideL, he);
            if (mohnL >= 0) d.SetDetailLayer(0, 0, mohnL, mo);
            t.detailObjectDistance = 90f;      // wie Kareth: gleiche Sichtweite, gleiche Kosten je Bild
            t.detailObjectDensity = 1f;
            d.wavingGrassStrength = 0.25f;     // Westwind: einen Hauch mehr Bewegung als in der Mulde
            d.RefreshPrototypes();
            t.Flush();
            EditorUtility.SetDirty(d);
            return $"Bewuchs: {grasN} Grasflecken, {kornN} Kornflecken, {roN} Röhricht, {heN} Heide, {moN} Mohn ({bebaut} Zellen unter Bauten, {nass} unter Wasser frei) · ";
        }

        /// <summary>Zellen, auf denen etwas wachsen darf: nicht unter Bauten am Boden (Kollider, außer Hecken), nicht unter Wasser.</summary>
        private static bool[] Freiraum(Terrain t, float cx, float cz, out int bebaut, out int nass, out bool[] ufer)
        {
            var frei = new bool[Res * Res];
            for (int n = 0; n < frei.Length; n++) frei[n] = true;
            Vector3 tp = t.transform.position;
            bebaut = 0;
            nass = 0;
            Physics.SyncTransforms();
            foreach (Collider c in Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude))
            {
                if (c is TerrainCollider || c.isTrigger || !c.enabled) continue;
                if (Hecke(c.transform)) continue;   // Hecken: Gras darf bis an den Fuß
                Bounds b = c.bounds;
                float boden = t.SampleHeight(b.center) + tp.y;
                if (b.min.y > boden + 1.5f || b.max.y < boden - 0.5f) continue;   // hängt in der Luft oder liegt ganz im Boden
                int x0 = Mathf.Max(0, Mathf.FloorToInt((b.min.x - tp.x) / cx)), x1 = Mathf.Min(Res - 1, Mathf.FloorToInt((b.max.x - tp.x) / cx));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((b.min.z - tp.z) / cz)), z1 = Mathf.Min(Res - 1, Mathf.FloorToInt((b.max.z - tp.z) / cz));
                var box = c as BoxCollider;
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int n = z * Res + x;
                        if (!frei[n]) continue;
                        if (box != null)
                        {
                            // gedrehter Kasten: Zellmitte in Kastenkoordinaten (Höhe aus der Kastenmitte, nur die Grundfläche zählt)
                            var w = new Vector3(tp.x + (x + 0.5f) * cx, b.center.y, tp.z + (z + 0.5f) * cz);
                            Vector3 l = box.transform.InverseTransformPoint(w) - box.center;
                            Vector3 h = box.size * 0.5f;
                            if (Mathf.Abs(l.x) > h.x + 0.3f || Mathf.Abs(l.z) > h.z + 0.3f) continue;
                        }
                        frei[n] = false;
                        bebaut++;
                    }
            }
            foreach (WaterVolume wv in Object.FindObjectsByType<WaterVolume>(FindObjectsInactive.Include))
            {
                Rect r = wv.AreaXZ;
                float sy = wv.SurfaceY;
                int x0 = Mathf.Max(0, Mathf.FloorToInt((r.xMin - tp.x) / cx)), x1 = Mathf.Min(Res - 1, Mathf.FloorToInt((r.xMax - tp.x) / cx));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((r.yMin - tp.z) / cz)), z1 = Mathf.Min(Res - 1, Mathf.FloorToInt((r.yMax - tp.z) / cz));
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int n = z * Res + x;
                        if (!frei[n]) continue;
                        float hy = t.SampleHeight(new Vector3(tp.x + (x + 0.5f) * cx, 0f, tp.z + (z + 0.5f) * cz)) + tp.y;
                        if (hy > sy - 0.05f) continue;
                        frei[n] = false;
                        nass++;
                    }
            }
            // Mühlbach: watbares Wasserband ohne Wasserfläche – seine Dreiecke frei halten, zwei Zellen daneben ist Ufer
            ufer = new bool[Res * Res];
            var bach = new bool[Res * Res];
            foreach (MeshFilter mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
            {
                if (mf.name != "Mühlbach" || mf.sharedMesh == null) continue;
                Vector3[] v = mf.sharedMesh.vertices;
                int[] tri = mf.sharedMesh.triangles;
                for (int q = 0; q < v.Length; q++) v[q] = mf.transform.TransformPoint(v[q]);
                for (int q = 0; q + 2 < tri.Length; q += 3)
                {
                    Vector2 a = new Vector2(v[tri[q]].x, v[tri[q]].z), b = new Vector2(v[tri[q + 1]].x, v[tri[q + 1]].z), c = new Vector2(v[tri[q + 2]].x, v[tri[q + 2]].z);
                    int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - tp.x) / cx)), x1 = Mathf.Min(Res - 1, Mathf.FloorToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - tp.x) / cx));
                    int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, Mathf.Min(b.y, c.y)) - tp.z) / cz)), z1 = Mathf.Min(Res - 1, Mathf.FloorToInt((Mathf.Max(a.y, Mathf.Max(b.y, c.y)) - tp.z) / cz));
                    for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                        {
                            var pz = new Vector2(tp.x + (x + 0.5f) * cx, tp.z + (z + 0.5f) * cz);
                            if (ImDreieck(pz, a, b, c)) bach[z * Res + x] = true;
                        }
                }
            }
            for (int z = 0; z < Res; z++)
                for (int x = 0; x < Res; x++)
                {
                    int n = z * Res + x;
                    if (bach[n]) { if (frei[n]) nass++; frei[n] = false; continue; }
                    for (int dz = -2; dz <= 2 && !ufer[n]; dz++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int zz = z + dz, xx = x + dx;
                            if (zz < 0 || zz >= Res || xx < 0 || xx >= Res || !bach[zz * Res + xx]) continue;
                            ufer[n] = true;
                            break;
                        }
                }
            return frei;
        }

        private static bool ImDreieck(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f, pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos);
        }

        private static bool Hecke(Transform tr)
        {
            for (Transform p = tr; p != null; p = p.parent)
                if (p.name.StartsWith("Hecke", System.StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
