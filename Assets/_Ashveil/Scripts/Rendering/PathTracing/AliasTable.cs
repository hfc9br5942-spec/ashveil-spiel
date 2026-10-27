using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Rendering.PathTracing
{
    /// <summary>
    /// Vose-Alias-Tabelle: Auswahl eines Elements proportional zu seinem Gewicht in O(1) auf der GPU.
    /// Eintrag = (Schwelle, Alias als Bitmuster, normierte Wahrscheinlichkeit, 0).
    /// </summary>
    public static class AliasTable
    {
        public static Vector4[] Build(IReadOnlyList<float> weights, out float total)
        {
            int n = weights.Count;
            var result = new Vector4[Mathf.Max(n, 1)];
            total = 0f;
            for (int i = 0; i < n; i++) total += Mathf.Max(0f, weights[i]);
            if (n == 0 || total <= 0f)
            {
                for (int i = 0; i < n; i++) result[i] = new Vector4(1f, AsFloat((uint)i), n > 0 ? 1f / n : 0f, 0f);
                return result;
            }
            var scaled = new float[n];
            var small = new Stack<int>();
            var large = new Stack<int>();
            for (int i = 0; i < n; i++)
            {
                float p = Mathf.Max(0f, weights[i]) / total;
                result[i] = new Vector4(0f, 0f, p, 0f);
                scaled[i] = p * n;
                if (scaled[i] < 1f) small.Push(i); else large.Push(i);
            }
            while (small.Count > 0 && large.Count > 0)
            {
                int s = small.Pop(), l = large.Pop();
                result[s].x = scaled[s];
                result[s].y = AsFloat((uint)l);
                scaled[l] = scaled[l] + scaled[s] - 1f;
                if (scaled[l] < 1f) small.Push(l); else large.Push(l);
            }
            while (large.Count > 0) { int l = large.Pop(); result[l].x = 1f; result[l].y = AsFloat((uint)l); }
            while (small.Count > 0) { int s = small.Pop(); result[s].x = 1f; result[s].y = AsFloat((uint)s); }
            return result;
        }

        public static float AsFloat(uint v) => System.BitConverter.Int32BitsToSingle((int)v);
        public static uint AsUInt(float f) => (uint)System.BitConverter.SingleToInt32Bits(f);
    }
}
