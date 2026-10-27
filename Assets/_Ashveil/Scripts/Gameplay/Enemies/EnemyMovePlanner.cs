using System.Collections.Generic;
using Ashveil.Data;

namespace Ashveil.Gameplay.Enemies
{
    /// <summary>
    /// Waehlt den naechsten Angriff aus dem Moveset (Combat-Masterplan §102–§104): nur Angriffe, deren Entfernung passt,
    /// deren Abklingzeit abgelaufen ist und deren Koerperteil-Bedingung erfuellt ist; gewichteter Zufall mit Abschlag
    /// fuer die zuletzt benutzten Angriffe (keine Endlosschleife desselben Schlags). Reines C#, deterministisch testbar.
    /// </summary>
    public sealed class EnemyMovePlanner
    {
        public const float RecentPenaltyLast = 0.35f;
        public const float RecentPenaltySecond = 0.7f;

        private readonly EnemyDefinition _def;
        private readonly Dictionary<byte, float> _readyAt = new Dictionary<byte, float>();
        private byte _last, _secondLast;
        private readonly List<EnemyAttackDefinition> _candidates = new List<EnemyAttackDefinition>();

        public EnemyMovePlanner(EnemyDefinition def) { _def = def; }

        public byte LastMove => _last;

        /// <summary>Darf dieser Angriff mit diesen Teilzustaenden ueberhaupt benutzt werden?</summary>
        public static bool Allowed(EnemyAttackDefinition m, System.Func<byte, bool> isBroken)
        {
            if (m == null) return false;
            if (m.requiresIntactPart != 0 && isBroken != null && isBroken(m.requiresIntactPart)) return false;
            if (m.onlyWhenPartBroken != 0 && (isBroken == null || !isBroken(m.onlyWhenPartBroken))) return false;
            return true;
        }

        /// <summary>
        /// Angriff fuer diese Entfernung waehlen. null = nichts passt gerade (naeher heran oder warten).
        /// <paramref name="roll"/> in [0,1).
        /// </summary>
        public EnemyAttackDefinition Pick(float distance, float now, System.Func<byte, bool> isBroken, float roll)
        {
            _candidates.Clear();
            float total = 0f;
            foreach (EnemyAttackDefinition m in _def.moveset)
            {
                if (m == null || m.weight <= 0f || !Allowed(m, isBroken)) continue;
                if (distance < m.minRange || distance > m.maxRange) continue;
                if (_readyAt.TryGetValue(m.id, out float t) && now < t) continue;
                _candidates.Add(m);
                total += Weight(m);
            }
            if (_candidates.Count == 0 || total <= 0f) return null;
            float r = roll * total;
            foreach (EnemyAttackDefinition m in _candidates)
            {
                r -= Weight(m);
                if (r <= 0f) return m;
            }
            return _candidates[_candidates.Count - 1];
        }

        /// <summary>Die kleinste Entfernung, auf die er fuer irgendeinen erlaubten Angriff herankommen muss.</summary>
        public float ApproachDistance(System.Func<byte, bool> isBroken)
        {
            float best = float.MaxValue;
            foreach (EnemyAttackDefinition m in _def.moveset)
                if (m != null && m.weight > 0f && m.kind != EnemyMoveKind.Charge && Allowed(m, isBroken) && m.maxRange < best) best = m.maxRange;
            return best == float.MaxValue ? 2f : best * 0.9f;
        }

        public void MarkUsed(EnemyAttackDefinition m, float now)
        {
            if (m == null) return;
            _readyAt[m.id] = now + m.cooldown;
            _secondLast = _last;
            _last = m.id;
        }

        public void Reset()
        {
            _readyAt.Clear();
            _last = _secondLast = 0;
        }

        private float Weight(EnemyAttackDefinition m)
        {
            float w = m.weight;
            if (m.id == _last && m.id == _secondLast) w *= 0.05f; // dreimal hintereinander nur, wenn nichts anderes geht
            else if (m.id == _last) w *= RecentPenaltyLast;
            else if (m.id == _secondLast) w *= RecentPenaltySecond;
            return w;
        }
    }
}
