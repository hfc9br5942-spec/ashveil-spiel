using System;

namespace Ashveil.World
{
    /// <summary>
    /// Deterministisches Rauschen fuer die Weltgenerierung (Terrain-Vorgabe §19–20): Gradient-Noise mit ganzzahligem Hash
    /// (kein Mathf.PerlinNoise, kein Zufallsgenerator mit Zustand) – gleicher Seed = gleiche Werte auf jedem Rechner.
    /// fBM, Ridged fBM (ridge = 1 − |2n − 1|) und Domain Warp. Rechnet in double, damit auch 40 km vom Ursprung
    /// keine Genauigkeit verloren geht.
    /// </summary>
    public static class AshNoise
    {
        /// <summary>Seed aus Welt-Seed und Kanal ableiten (Region, Terrain, Biom, Detail …).</summary>
        public static int Derive(int worldSeed, string channel)
        {
            unchecked
            {
                uint h = 2166136261u ^ (uint)worldSeed;
                foreach (char c in channel)
                {
                    h ^= c;
                    h *= 16777619u;
                }
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                return (int)(h & 0x7FFFFFFF);
            }
        }

        private static uint Hash(int x, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 0x8DA6B343u ^ (uint)z * 0xD8163841u ^ (uint)seed * 0xCB1AB31Fu;
                h ^= h >> 13;
                h *= 0x5BD1E995u;
                h ^= h >> 15;
                return h;
            }
        }

        /// <summary>Wert 0…1 je Gitterpunkt (fuer Platzierung, Jitter).</summary>
        public static double Hash01(int x, int z, int seed) => (Hash(x, z, seed) & 0xFFFFFF) / 16777215.0;

        private static double Grad(int ix, int iz, int seed, double dx, double dz)
        {
            uint h = Hash(ix, iz, seed);
            double a = (h & 0xFFFF) * (Math.PI * 2.0 / 65536.0);
            return Math.Cos(a) * dx + Math.Sin(a) * dz;
        }

        private static double Fade(double t) => t * t * t * (t * (t * 6.0 - 15.0) + 10.0);

        /// <summary>Gradient-Noise, Bereich etwa −0,7…0,7 (Mittel 0).</summary>
        public static double Gradient(double x, double z, int seed)
        {
            int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z);
            double fx = x - ix, fz = z - iz;
            double u = Fade(fx), v = Fade(fz);
            double n00 = Grad(ix, iz, seed, fx, fz);
            double n10 = Grad(ix + 1, iz, seed, fx - 1, fz);
            double n01 = Grad(ix, iz + 1, seed, fx, fz - 1);
            double n11 = Grad(ix + 1, iz + 1, seed, fx - 1, fz - 1);
            double a = n00 + (n10 - n00) * u;
            double b = n01 + (n11 - n01) * u;
            return a + (b - a) * v;
        }

        /// <summary>fBM, normiert auf etwa −1…1. scale = Wellenlaenge der ersten Oktave in Metern.</summary>
        public static double Fbm(double x, double z, double scale, int octaves, int seed, double lacunarity = 2.0, double gain = 0.5)
        {
            double f = 1.0 / scale, amp = 1.0, sum = 0.0, norm = 0.0;
            for (int o = 0; o < octaves; o++)
            {
                sum += Gradient(x * f, z * f, seed + o * 1013) * amp;
                norm += amp;
                f *= lacunarity;
                amp *= gain;
            }
            return sum / norm * 1.45;
        }

        /// <summary>
        /// Ridged fBM 0…1: Grate statt Blasen. Jede Oktave wird mit dem vorigen Grat gewichtet (Musgrave), dadurch
        /// verzweigen sich Nebenkaemme vom Hauptkamm statt gleichmaessig verteilt zu sein.
        /// </summary>
        public static double Ridged(double x, double z, double scale, int octaves, int seed, double lacunarity = 2.1, double gain = 0.5)
        {
            double f = 1.0 / scale, amp = 1.0, sum = 0.0, norm = 0.0, weight = 1.0;
            for (int o = 0; o < octaves; o++)
            {
                double n = Gradient(x * f, z * f, seed + o * 7919) * 1.4 * 0.5 + 0.5;   // 0…1
                double r = 1.0 - Math.Abs(2.0 * n - 1.0);
                r *= r;
                r *= weight;
                weight = Math.Max(0.0, Math.Min(1.0, r * 2.0));
                sum += r * amp;
                norm += amp;
                f *= lacunarity;
                amp *= gain;
            }
            return sum / norm;
        }

        /// <summary>Domain Warp: verschiebt die Koordinaten mit tieffrequentem Rauschen (klein halten, Makroform bleibt).</summary>
        public static void Warp(ref double x, ref double z, double scale, double amount, int seed)
        {
            double wx = Fbm(x, z, scale, 3, seed + 31);
            double wz = Fbm(x + 5217.0, z - 3181.0, scale, 3, seed + 57);
            x += wx * amount;
            z += wz * amount;
        }

        public static double SmoothStep(double a, double b, double x)
        {
            double t = Math.Max(0.0, Math.Min(1.0, (x - a) / (b - a)));
            return t * t * (3.0 - 2.0 * t);
        }

        public static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }
}
