using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Gameplay.Visuals
{
    /// <summary>
    /// M7: duenne Kopie der Gesichts-Dreiecke eines Koerpers, auf der die gemalten Gesichtsebenen liegen.
    /// Gesicht = Dreiecke, deren UVs im Gesichtsbereich der Hauttextur liegen (u 0..0.375, v 0.625..1, bei beiden
    /// Quaternius-Koerpern oben links). Die UVs werden auf 0..1 gedehnt, damit eine kleine Ebene (512 px) reicht.
    /// Leicht entlang der Normalen versetzt (kein Flimmern), gleiche Knochen und Gewichte wie der Koerper.
    /// Einmal je Quell-Mesh erzeugt und von allen Figuren geteilt.
    /// </summary>
    public static class FaceOverlayMesh
    {
        public const float URange = 0.375f;
        public const float VMin = 0.625f;
        private static readonly Dictionary<Mesh, Mesh> Cache = new Dictionary<Mesh, Mesh>();

        public static Mesh For(Mesh src)
        {
            if (src == null) return null;
            if (Cache.TryGetValue(src, out Mesh m)) return m;
            m = Build(src);
            Cache[src] = m;
            return m;
        }

        private static Mesh Build(Mesh src)
        {
            if (!src.isReadable)
            {
                Debug.LogWarning("[Ashveil] Gesichtsebenen: Koerper-Mesh '" + src.name + "' ist nicht lesbar.");
                return null;
            }
            Vector2[] uv = src.uv;
            Vector3[] pos = src.vertices;
            Vector3[] nrm = src.normals;
            BoneWeight[] bw = src.boneWeights;
            if (uv == null || uv.Length != pos.Length) return null;
            int[] tris = src.GetTriangles(0);
            float push = src.bounds.size.y * 0.0004f; // ~0,7 mm bei einer 1,8-m-Figur
            var map = new Dictionary<int, int>();
            var nPos = new List<Vector3>();
            var nNrm = new List<Vector3>();
            var nUv = new List<Vector2>();
            var nBw = new List<BoneWeight>();
            var nTri = new List<int>();
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                bool inside = true;
                for (int k = 0; k < 3; k++)
                {
                    Vector2 t = uv[tris[i + k]];
                    if (t.x < 0f || t.x > URange || t.y < VMin || t.y > 1f) { inside = false; break; }
                }
                if (!inside) continue;
                for (int k = 0; k < 3; k++)
                {
                    int v = tris[i + k];
                    if (!map.TryGetValue(v, out int ni))
                    {
                        ni = nPos.Count;
                        map[v] = ni;
                        Vector3 n = nrm != null && nrm.Length == pos.Length ? nrm[v] : Vector3.zero;
                        nPos.Add(pos[v] + n * push);
                        nNrm.Add(n);
                        nUv.Add(new Vector2(uv[v].x / URange, (uv[v].y - VMin) / (1f - VMin)));
                        if (bw != null && bw.Length == pos.Length) nBw.Add(bw[v]);
                    }
                    nTri.Add(ni);
                }
            }
            if (nTri.Count == 0) return null;
            var m = new Mesh { name = src.name + " Face Overlay" };
            m.SetVertices(nPos);
            m.SetNormals(nNrm);
            m.SetUVs(0, nUv);
            m.SetTriangles(nTri, 0);
            if (nBw.Count == nPos.Count) m.boneWeights = nBw.ToArray();
            m.bindposes = src.bindposes;
            m.RecalculateBounds();
            return m;
        }
    }
}
