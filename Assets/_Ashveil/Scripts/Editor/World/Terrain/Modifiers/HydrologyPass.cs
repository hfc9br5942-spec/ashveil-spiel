using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.EditorTools.TerrainGen
{
    /// <summary>Ergebnis der Abflussanalyse (Vorgabe §35–40). Nur Editor, nie zur Laufzeit.</summary>
    public sealed class HydrologyResult
    {
        public int[] Downstream;      // Zellindex, in den das Wasser fliesst; −1 = Meer/Rand
        public float[] Accumulation;  // Einzugsflaeche in Zellen
        public float[] LakeDepth;     // Senken, die sich fuellen wuerden (m)
        public readonly List<RiverCandidate> Candidates = new List<RiverCandidate>();
    }

    public sealed class RiverCandidate
    {
        public string Id;
        public List<Vector2> Path = new List<Vector2>();
        public float AreaKm2;
    }

    /// <summary>
    /// HYDROLOGY (Vorgabe §35–37): Priority-Flood (Senken fuellen) liefert fuer jede Zelle den Abflussweg zum Meer,
    /// daraus Abflussakkumulation, Seen in natuerlichen Senken und Flusskandidaten ab einer Einzugsflaeche.
    /// Kandidaten werden NICHT automatisch Fluesse – der Designer befoerdert sie (PROMOTE TO RIVER).
    /// </summary>
    public sealed class HydrologyPass : TerrainModifier
    {
        private readonly int _priority;
        public HydrologyPass(int priority = 350) => _priority = priority;

        public override string Name => _priority < 1000 ? "Abfluss (Analyse)" : "Abfluss (Endstand)";
        public override int Priority => _priority;

        public override void Apply(TerrainGenerationContext ctx) => ctx.Hydrology = Analyze(ctx);

        public static HydrologyResult Analyze(TerrainGenerationContext ctx)
        {
            MacroHeightField f = ctx.Field;
            int w = f.Width, hgt = f.Height, n = f.Count;
            float sea = ctx.Profile.seaLevel;
            float[] land = ctx.Masks.Get(MaskSet.Land);
            var filled = new float[n];
            var down = new int[n];
            var visited = new bool[n];
            var order = new int[n];
            int orderCount = 0;
            var heap = new MinHeap(n);
            for (int i = 0; i < n; i++)
            {
                down[i] = -1;
                int x = i % w, z = i / w;
                bool edge = x == 0 || z == 0 || x == w - 1 || z == hgt - 1;
                bool ocean = f.H[i] <= sea && land[i] < 0.5f;
                if (edge || ocean)
                {
                    filled[i] = f.H[i];
                    visited[i] = true;
                    heap.Push(filled[i], i);
                }
            }
            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] dz = { 0, 0, 1, -1, 1, -1, 1, -1 };
            while (heap.Count > 0)
            {
                heap.Pop(out float ch, out int c);
                order[orderCount++] = c;
                int cx = c % w, cz = c / w;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + dx[k], nz = cz + dz[k];
                    if (nx < 0 || nz < 0 || nx >= w || nz >= hgt) continue;
                    int ni = nz * w + nx;
                    if (visited[ni]) continue;
                    visited[ni] = true;
                    filled[ni] = Mathf.Max(f.H[ni], ch + 0.01f);
                    down[ni] = c;
                    heap.Push(filled[ni], ni);
                }
            }
            var acc = new float[n];
            for (int k = orderCount - 1; k >= 0; k--)
            {
                int c = order[k];
                acc[c] += 1f;
                if (down[c] >= 0) acc[down[c]] += acc[c];
            }
            var lake = new float[n];
            for (int i = 0; i < n; i++)
            {
                float d = filled[i] - f.H[i];
                lake[i] = d > 1.5f && land[i] > 0.5f ? d : 0f;
            }
            float[] lakeMask = ctx.Masks.Get(MaskSet.Lake);
            for (int i = 0; i < n; i++) lakeMask[i] = Mathf.Clamp01(lake[i] / 6f);

            var res = new HydrologyResult { Downstream = down, Accumulation = acc, LakeDepth = lake };
            TraceCandidates(ctx, res);
            return res;
        }

        private static void TraceCandidates(TerrainGenerationContext ctx, HydrologyResult r)
        {
            MacroHeightField f = ctx.Field;
            float cellKm2 = f.CellSize * f.CellSize / 1e6f;
            float minCells = ctx.Profile.riverCandidateAreaKm2 / cellKm2;
            float[] land = ctx.Masks.Get(MaskSet.Land);
            int n = f.Count;
            var isCand = new bool[n];
            var hasUpstream = new bool[n];
            for (int i = 0; i < n; i++) isCand[i] = r.Accumulation[i] >= minCells && land[i] > 0.5f;
            for (int i = 0; i < n; i++)
                if (isCand[i] && r.Downstream[i] >= 0 && isCand[r.Downstream[i]]) hasUpstream[r.Downstream[i]] = true;
            var used = new bool[n];
            int id = 0;
            for (int i = 0; i < n; i++)
            {
                if (!isCand[i] || hasUpstream[i]) continue;   // nur Quellen starten
                var cand = new RiverCandidate { Id = $"kandidat_{id++:000}" };
                int c = i, steps = 0;
                while (c >= 0 && steps < 5000)
                {
                    if (steps % 3 == 0 || used[c]) cand.Path.Add(f.World(c));
                    if (used[c]) break;
                    used[c] = true;
                    cand.AreaKm2 = r.Accumulation[c] * cellKm2;
                    if (land[c] < 0.5f) break;
                    c = r.Downstream[c];
                    steps++;
                }
                if (cand.Path.Count >= 3) r.Candidates.Add(cand);
            }
            r.Candidates.Sort((a, b) => b.AreaKm2.CompareTo(a.AreaKm2));
        }

        /// <summary>Binaerer Min-Heap (Hoehe, Zelle) – bei Gleichstand kleinerer Index zuerst (deterministisch).</summary>
        private sealed class MinHeap
        {
            private readonly float[] _k;
            private readonly int[] _v;
            public int Count;

            public MinHeap(int cap)
            {
                _k = new float[cap];
                _v = new int[cap];
            }

            private bool Less(int a, int b) => _k[a] < _k[b] || (_k[a] == _k[b] && _v[a] < _v[b]);

            public void Push(float key, int val)
            {
                int i = Count++;
                _k[i] = key;
                _v[i] = val;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (!Less(i, p)) break;
                    Swap(i, p);
                    i = p;
                }
            }

            public void Pop(out float key, out int val)
            {
                key = _k[0];
                val = _v[0];
                Count--;
                _k[0] = _k[Count];
                _v[0] = _v[Count];
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, rr = l + 1, m = i;
                    if (l < Count && Less(l, m)) m = l;
                    if (rr < Count && Less(rr, m)) m = rr;
                    if (m == i) break;
                    Swap(i, m);
                    i = m;
                }
            }

            private void Swap(int a, int b)
            {
                (_k[a], _k[b]) = (_k[b], _k[a]);
                (_v[a], _v[b]) = (_v[b], _v[a]);
            }
        }
    }
}
