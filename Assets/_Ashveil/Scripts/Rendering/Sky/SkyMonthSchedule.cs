using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Rendering.Sky
{
    /// <summary>
    /// Himmelsplan eines 30-Tage-Monats (§ 43–48): aus Weltseed und Monatsnummer entsteht eine Folge von Zustaenden mit
    /// Verweildauer und mehrstuendigen Uebergaengen – nur Stuetzstellen, keine Sekunden-Simulation. Jeder Monat ist
    /// anders, aber reproduzierbar; der Monatswechsel blendet vom letzten Zustand des Vormonats weich ueber.
    /// Eigener Zufallsgenerator (nicht System.Random), damit Editor, Server und Clients garantiert gleich rechnen.
    /// </summary>
    public sealed class SkyMonthSchedule
    {
        public struct Key
        {
            public double Start;        // fortlaufende Spielstunde
            public SkyState State;
            public float Transition;    // Stunden, in denen vom vorigen Zustand uebergeblendet wird
        }

        public int Month { get; private set; }
        public Key[] Keys { get; private set; } = Array.Empty<Key>();
        public SkyState LastState => Keys.Length > 0 ? Keys[Keys.Length - 1].State : SkyState.Scattered;

        private static readonly Dictionary<long, SkyMonthSchedule> Cache = new Dictionary<long, SkyMonthSchedule>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Cache.Clear();

        /// <summary>Nach Aenderungen am Klima (Editor) neu erzeugen lassen.</summary>
        public static void ClearCache() => Cache.Clear();

        /// <summary>Plan fuer Monat 'month' (zwischengespeichert).</summary>
        public static SkyMonthSchedule For(SkyClimateProfile climate, int month)
        {
            long key = ((long)climate.worldSeed << 32) ^ (uint)month;
            if (Cache.TryGetValue(key, out SkyMonthSchedule s)) return s;
            if (Cache.Count > 16) Cache.Clear();
            s = Generate(climate, month);
            Cache[key] = s;
            return s;
        }

        public static SkyMonthSchedule Generate(SkyClimateProfile c, int month)
        {
            var rng = new Rng(Mix(c.worldSeed, month, 0x5A17));
            double hoursPerMonth = Math.Max(1, c.monthDays) * 24.0;
            double start = month * hoursPerMonth, end = start + hoursPerMonth;
            var keys = new List<Key>();
            SkyState s = month == 0 ? c.firstState : Weighted(c, null, rng);
            keys.Add(new Key { Start = start, State = s, Transition = month == 0 ? 0f : Range(rng, c.transitionHours) });
            double t = start + Dwell(c, s, rng);
            while (t < end)
            {
                SkyState next = Weighted(c, s, rng);
                float tr = Range(rng, c.transitionHours);
                keys.Add(new Key { Start = t, State = next, Transition = tr });
                s = next;
                t += tr + Dwell(c, s, rng);
            }
            return new SkyMonthSchedule { Month = month, Keys = keys.ToArray() };
        }

        /// <summary>Zustand zur Spielstunde t: (von, nach, Anteil 0..1) – Anteil laeuft weich (SmoothStep).</summary>
        public static void Evaluate(SkyClimateProfile c, double totalHours, out SkyState from, out SkyState to, out float blend, out int month)
        {
            double hoursPerMonth = Math.Max(1, c.monthDays) * 24.0;
            month = (int)Math.Floor(totalHours / hoursPerMonth);
            SkyMonthSchedule m = For(c, month);
            int i = 0;
            for (int k = 0; k < m.Keys.Length; k++) if (m.Keys[k].Start <= totalHours) i = k;
            Key key = m.Keys[i];
            to = key.State;
            from = i > 0 ? m.Keys[i - 1].State : month > 0 ? For(c, month - 1).LastState : key.State;
            if (key.Transition <= 0.001f) { blend = 1f; from = to; return; }
            float x = (float)((totalHours - key.Start) / key.Transition);
            blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x));
        }

        private static float Dwell(SkyClimateProfile c, SkyState s, Rng rng)
        {
            CloudProfile p = c.Profile(s);
            return p != null ? Range(rng, p.dwellHours) : 12f;
        }

        private static SkyState Weighted(SkyClimateProfile c, SkyState? prev, Rng rng)
        {
            CloudProfile pp = prev.HasValue ? c.Profile(prev.Value) : null;
            float total = 0f;
            var w = new float[c.clouds.Length];
            for (int i = 0; i < c.clouds.Length; i++)
            {
                CloudProfile p = c.clouds[i];
                if (p == null || (prev.HasValue && p.state == prev.Value)) continue;
                float affinity = pp != null ? Mathf.Exp(-Mathf.Abs(p.order - pp.order) * 0.7f) : 1f;
                w[i] = Mathf.Max(0f, p.weight) * affinity;
                total += w[i];
            }
            if (total <= 0f) return prev ?? SkyState.Scattered;
            float r = rng.Next01() * total;
            for (int i = 0; i < w.Length; i++)
            {
                r -= w[i];
                if (r <= 0f && w[i] > 0f) return c.clouds[i].state;
            }
            for (int i = w.Length - 1; i >= 0; i--) if (w[i] > 0f) return c.clouds[i].state;
            return SkyState.Scattered;
        }

        private static float Range(Rng rng, Vector2 r) => Mathf.Lerp(r.x, r.y, rng.Next01());

        public static int Mix(int a, int b, int c)
        {
            unchecked
            {
                uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u ^ (uint)c * 0xC2B2AE3Du;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
                return (int)h;
            }
        }

        /// <summary>Kleiner, plattformunabhaengiger Zufallsgenerator (xorshift32).</summary>
        public sealed class Rng
        {
            private uint _s;
            public Rng(int seed) { _s = (uint)seed == 0 ? 0x1234567u : (uint)seed; }
            public uint Next()
            {
                _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5;
                return _s;
            }
            public float Next01() => (Next() & 0xFFFFFF) / 16777216f;
        }
    }
}
