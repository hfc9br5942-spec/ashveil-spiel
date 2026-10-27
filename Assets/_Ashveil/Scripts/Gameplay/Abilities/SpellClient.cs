using System;
using System.Collections.Generic;
using Ashveil.Data;
using UnityEngine;

namespace Ashveil.Gameplay.Abilities
{
    /// <summary>
    /// Client-Sicht der eigenen Klassenfaehigkeiten (P1): gelernte Raenge, Talente, Abklingzeiten, globale Abklingzeit,
    /// laufendes Wirken und die Klassen-Ressource. Der Netz-Layer fuellt das und stellt die Anfragen bereit; Oberflaeche und
    /// Eingabe lesen nur. Entscheidend ist immer der Server.
    /// </summary>
    public static class SpellClient
    {
        private static readonly Dictionary<string, int> _known = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _talents = new Dictionary<string, int>();
        private static readonly Dictionary<string, (float end, float total)> _cooldowns = new Dictionary<string, (float, float)>();

        public static readonly TalentBonuses Bonuses = new TalentBonuses();

        public static bool Ready { get; private set; }
        /// <summary>Letzte Ablehnung des Servers (Tests/Anzeige).</summary>
        public static string LastError { get; set; } = "";
        public static int TalentPointsFree { get; private set; }
        public static int TalentPointsSpent { get; private set; }
        public static int Revision { get; private set; }

        public static ResourceType Resource { get; set; }
        public static float ResourceCurrent { get; set; }
        public static float ResourceMax { get; set; }

        public static float GcdEnd { get; private set; } = -1f;
        public static float GcdTotal { get; private set; } = 1.5f;

        public static string CastingSpell { get; private set; } = "";
        public static float CastEnd { get; private set; } = -1f;
        public static float CastTotal { get; private set; }

        /// <summary>Heldenhafter Schlag wartet auf den naechsten Schlag (leuchtet auf der Leiste).</summary>
        public static string QueuedNextSwing { get; private set; } = "";

        // ---- Anfragen an den Server (setzt der Netz-Layer)
        public static Action<string> CastRequest;
        public static Action<string, string, int> TrainRequest;
        public static Action<string> TalentRequest;
        public static Action<string> TalentResetRequest;

        public static event Action Changed;

        public static IReadOnlyDictionary<string, int> Known => _known;
        public static IReadOnlyDictionary<string, int> Talents => _talents;

        public static int RankOf(string spellId) => spellId != null && _known.TryGetValue(spellId, out int r) ? r : 0;
        public static bool Knows(string spellId) => RankOf(spellId) > 0;
        public static int TalentRank(string talentId) => talentId != null && _talents.TryGetValue(talentId, out int r) ? r : 0;
        public static bool IsCasting => CastEnd > 0f && Time.time < CastEnd + 0.3f && CastingSpell.Length > 0;

        public static float CooldownRemaining(string spellId, out float total)
        {
            total = 0f;
            float left = 0f;
            if (spellId != null && _cooldowns.TryGetValue(spellId, out var cd))
            {
                left = Mathf.Max(0f, cd.end - Time.unscaledTime);
                total = cd.total;
            }
            SpellDefinition s = spellId != null ? GameData.Get<SpellDefinition>(spellId) : null;
            float gcdLeft = s != null && s.triggersGcd ? Mathf.Max(0f, GcdEnd - Time.unscaledTime) : 0f;
            if (gcdLeft > left) { left = gcdLeft; total = GcdTotal; }
            return left;
        }

        // ---------------------------------------------------------------- vom Netz

        /// <summary>"spell.a:2,spell.b:1" / "talent.x:3,…"</summary>
        public static void ApplyState(string known, string talents, int free, int spent)
        {
            Parse(known, _known);
            Parse(talents, _talents);
            Bonuses.Clear();
            foreach (var kv in _talents) Bonuses.Add(GameData.Get<TalentDefinition>(kv.Key), kv.Value);
            TalentPointsFree = free;
            TalentPointsSpent = spent;
            Ready = true;
            Raise();
        }

        private static void Parse(string enc, Dictionary<string, int> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(enc)) return;
            foreach (string part in enc.Split(','))
            {
                int c = part.LastIndexOf(':');
                if (c <= 0) continue;
                if (int.TryParse(part.Substring(c + 1), out int r) && r > 0) into[part.Substring(0, c)] = r;
            }
        }

        public static void SetCooldown(string spellId, float seconds, float gcd)
        {
            float now = Time.unscaledTime;
            if (seconds > 0f && !string.IsNullOrEmpty(spellId)) _cooldowns[spellId] = (now + seconds, seconds);
            if (gcd > 0f)
            {
                GcdEnd = now + gcd;
                GcdTotal = gcd;
            }
            Raise();
        }

        public static void BeginCast(string spellId, float seconds, string label)
        {
            CastingSpell = spellId ?? "";
            CastTotal = seconds;
            CastEnd = Time.time + seconds;
            Ashveil.Gameplay.Characters.CastBarState.Begin(label, seconds);
            Raise();
        }

        public static void EndCast(bool interrupted)
        {
            if (CastingSpell.Length == 0 && CastEnd < 0f) return;
            CastingSpell = "";
            CastEnd = -1f;
            Ashveil.Gameplay.Characters.CastBarState.End(interrupted);
            Raise();
        }

        public static void SetNextSwing(string spellId)
        {
            QueuedNextSwing = spellId ?? "";
            Raise();
        }

        public static void Reset()
        {
            _known.Clear();
            _talents.Clear();
            _cooldowns.Clear();
            Bonuses.Clear();
            Ready = false;
            TalentPointsFree = TalentPointsSpent = 0;
            GcdEnd = CastEnd = -1f;
            CastingSpell = QueuedNextSwing = "";
            Resource = ResourceType.None;
            ResourceCurrent = ResourceMax = 0f;
            Raise();
        }

        private static void Raise()
        {
            Revision++;
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _known.Clear();
            _talents.Clear();
            _cooldowns.Clear();
            Bonuses.Clear();
            Ready = false;
            CastRequest = null;
            TrainRequest = null;
            TalentRequest = null;
            TalentResetRequest = null;
            Changed = null;
            GcdEnd = CastEnd = -1f;
            CastingSpell = QueuedNextSwing = "";
        }
    }
}
