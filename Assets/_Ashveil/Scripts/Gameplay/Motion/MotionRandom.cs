namespace Ashveil.Gameplay.Motion
{
    /// <summary>
    /// Kleiner, deterministischer Zufall (xorshift32) fuer Bewegungsvarianten (§142). Gleicher Seed → gleiche Folge
    /// auf jedem Rechner. Nie UnityEngine.Random fuer Varianten verwenden.
    /// </summary>
    public struct MotionRandom
    {
        private uint _state;

        public MotionRandom(uint seed) { _state = seed == 0 ? 0x9E3779B9u : seed; Next(); }

        public uint Next()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>Gleichverteilt in [0, 1).</summary>
        public float Value => (Next() & 0xFFFFFF) / 16777216f;

        /// <summary>Seed aus Ereignis-ID und Figur (z. B. Angriffszaehler + Objekt-ID).</summary>
        public static uint Mix(uint a, uint b)
        {
            uint h = a * 0x85EBCA6Bu ^ (b + 0x9E3779B9u + (a << 6) + (a >> 2));
            h ^= h >> 13; h *= 0xC2B2AE35u; h ^= h >> 16;
            return h == 0 ? 1u : h;
        }
    }
}
