using System;
using System.Collections.Generic;

namespace Ashveil.Data
{
    /// <summary>Was auf einem Actionbar-Platz liegen kann (Feature-Update 26.09., Abschnitt 5). Generisch – eine Leiste fuer alles.</summary>
    public enum ActionEntryType : byte
    {
        None = 0,
        /// <summary>Waffen- und Bewegungsfaehigkeiten (<see cref="AbilityDefinition"/>)</summary>
        Ability = 1,
        /// <summary>Zauber (spaeter, Klassen)</summary>
        Spell = 2,
        /// <summary>Kampfgegenstaende (spaeter: Bomben, Fallen …)</summary>
        CombatItem = 3,
        /// <summary>Verbrauchsgegenstaende (Heiltraenke, Essen)</summary>
        Consumable = 4,
        /// <summary>Hilfsfaehigkeiten (spaeter)</summary>
        Utility = 5,
        Emote = 6,
        ProfessionAbility = 7,
        Mount = 8,
        /// <summary>Dominator: Gefaehrten rufen/entlassen (companion.call) und Dominieren (companion.dominate)</summary>
        Companion = 9
    }

    /// <summary>
    /// Ein belegter Actionbar-Platz. Speichert nur die Verknuepfung (Typ + stabile ID + Platz) – Symbol, Anzahl,
    /// Ladungen und Abklingzeit kommen beim Zeichnen aus der jeweiligen Quelle (Item, Waffe, Motor), nie doppelt.
    /// </summary>
    [Serializable]
    public struct ActionBarEntry : IEquatable<ActionBarEntry>
    {
        public ActionEntryType type;
        /// <summary>"ability.auto_attack", "item.healing_draught", spaeter "spell.x", "emote.wave"</summary>
        public string referenceId;
        /// <summary>0 = Bank A, 1 = Bank B</summary>
        public byte page;
        /// <summary>0 … 11</summary>
        public byte slot;

        public ActionBarEntry(ActionEntryType type, string referenceId, int page, int slot)
        {
            this.type = type;
            this.referenceId = referenceId ?? "";
            this.page = (byte)page;
            this.slot = (byte)slot;
        }

        public bool IsEmpty => type == ActionEntryType.None || string.IsNullOrEmpty(referenceId);
        public int Index => page * ActionBarLayout.SlotsPerPage + slot;

        public bool Equals(ActionBarEntry o) => type == o.type && referenceId == o.referenceId && page == o.page && slot == o.slot;
        public override bool Equals(object obj) => obj is ActionBarEntry o && Equals(o);
        public override int GetHashCode() => ((int)type * 397) ^ (referenceId ?? "").GetHashCode() ^ (page << 8) ^ slot;
        public override string ToString() => IsEmpty ? "-" : $"{type}:{referenceId}";
    }

    /// <summary>Aufbau der Leiste und Regeln, die Server und Client gleich anwenden.</summary>
    public static class ActionBarLayout
    {
        public const int SlotsPerPage = 12;
        public const int Pages = 2;
        public const int Total = SlotsPerPage * Pages;
        public const int MaxIdLength = 64;

        public const string AutoAttack = "ability.auto_attack";
        public const string WeaponSpecial = "ability.weapon_special";
        public const string Dodge = "ability.dodge";
        public const string SwapWeapon = "ability.swap_weapon";
        public const string Reload = "ability.reload";

        /// <summary>Welche Typen man heute belegen darf (Rest folgt mit Zaubern und Reittieren). M8: Emotes.</summary>
        public static bool Supported(ActionEntryType t) =>
            t == ActionEntryType.Ability || t == ActionEntryType.Consumable || t == ActionEntryType.CombatItem || t == ActionEntryType.Emote ||
            t == ActionEntryType.Mount || t == ActionEntryType.Companion || t == ActionEntryType.Spell;

        /// <summary>Standardbelegung eines neuen Charakters – entspricht den bisherigen festen Tasten (1, 2, 4) plus Heiltrank auf 3.</summary>
        public static List<ActionBarEntry> Defaults() => new List<ActionBarEntry>
        {
            new ActionBarEntry(ActionEntryType.Ability, AutoAttack, 0, 0),
            new ActionBarEntry(ActionEntryType.Ability, WeaponSpecial, 0, 1),
            new ActionBarEntry(ActionEntryType.Consumable, "item.healing_draught", 0, 2),
            new ActionBarEntry(ActionEntryType.Ability, Dodge, 0, 3),
            new ActionBarEntry(ActionEntryType.Ability, SwapWeapon, 1, 0),
            new ActionBarEntry(ActionEntryType.Ability, Reload, 1, 1)
        };

        /// <summary>Formal gueltig (Platz, Typ, ID-Laenge)? Ob der Inhalt existiert/erlaubt ist, prueft der Server extra.</summary>
        public static bool IsWellFormed(ActionBarEntry e) =>
            e.page < Pages && e.slot < SlotsPerPage && Supported(e.type) &&
            !string.IsNullOrEmpty(e.referenceId) && e.referenceId.Length <= MaxIdLength;

        /// <summary>Doppelte Plaetze und kaputte Eintraege entfernen (Laden alter/manipulierter Staende).</summary>
        public static List<ActionBarEntry> Sanitize(IEnumerable<ActionBarEntry> entries)
        {
            var seen = new HashSet<int>();
            var result = new List<ActionBarEntry>();
            if (entries == null) return result;
            foreach (ActionBarEntry e in entries)
            {
                if (!IsWellFormed(e)) continue;
                // Klassenfaehigkeiten: nur bekannte (entfernte oder erfundene Zauber fallen weg)
                if (e.type == ActionEntryType.Spell && GameData.Ready && GameData.Get<SpellDefinition>(e.referenceId) == null) continue;
                if (!seen.Add(e.Index)) continue;
                result.Add(e);
            }
            result.Sort((a, b) => a.Index.CompareTo(b.Index));
            return result;
        }

        /// <summary>Liste in ein festes Feld mit 24 Plaetzen (leer = <see cref="ActionEntryType.None"/>).</summary>
        public static ActionBarEntry[] ToSlots(IEnumerable<ActionBarEntry> entries)
        {
            var slots = new ActionBarEntry[Total];
            for (int i = 0; i < Total; i++) slots[i] = new ActionBarEntry(ActionEntryType.None, "", i / SlotsPerPage, i % SlotsPerPage);
            if (entries != null)
                foreach (ActionBarEntry e in entries)
                    if (e.page < Pages && e.slot < SlotsPerPage) slots[e.Index] = e;
            return slots;
        }
    }
}
