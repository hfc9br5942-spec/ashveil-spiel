using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>Klassen-Ressource (WoW Classic, Max 30.09.): Wut = Eidwaechter, Energie = Aschenjaeger, Mana = Aschenweber.</summary>
    public enum ResourceType : byte
    {
        None = 0,
        Mana = 1,
        Rage = 2,
        Energy = 3
    }

    /// <summary>Worauf ein Zauber wirkt.</summary>
    public enum SpellTarget : byte
    {
        /// <summary>Nur auf sich selbst.</summary>
        Self = 0,
        /// <summary>Feindliches Ziel (braucht ein angewaehltes, angreifbares Ziel).</summary>
        Enemy = 1,
        /// <summary>Befreundet: angewaehlter Spieler, sonst man selbst (wie WoW-Selbstzauber).</summary>
        Friendly = 2,
        /// <summary>Flaeche um die eigene Figur (kein Ziel noetig).</summary>
        AreaAroundSelf = 3,
        /// <summary>Gefallener Mitspieler (Wiederbeleben).</summary>
        DeadFriendly = 4
    }

    /// <summary>Schadensart – fuer Farbe, Klang und Talente.</summary>
    public enum SpellSchool : byte
    {
        Physical = 0,
        Fire = 1,
        /// <summary>Asche/Glut des Aschenwebers (Heilung, Schilde).</summary>
        Ash = 2,
        Nature = 3
    }

    /// <summary>Hauptwirkung eines Zaubers. Nebenwirkungen (Betaeubung, Verlangsamung, Brennen …) stehen als eigene Felder.</summary>
    public enum SpellEffectKind : byte
    {
        /// <summary>Schlag mit der Haupthand: Waffenschaden + Rang-Bonus.</summary>
        WeaponStrike = 0,
        /// <summary>Naechster Schlag des automatischen Angriffs bekommt den Rang-Bonus (Heldenhafter Schlag, kein GCD).</summary>
        NextSwing = 1,
        /// <summary>Schuss mit der Fernkampfwaffe: Waffen- + Munitionsschaden + Rang-Bonus (verbraucht einen Pfeil).</summary>
        RangedShot = 2,
        /// <summary>Zauberschaden (Rang min..max).</summary>
        SpellDamage = 3,
        /// <summary>Heilung (Rang min..max).</summary>
        Heal = 4,
        /// <summary>Schild, der Schaden auffaengt (Rang extra = Menge).</summary>
        Absorb = 5,
        /// <summary>Kein Schaden, nur Nebenwirkungen (Spott, Verlangsamen …).</summary>
        None = 6,
        /// <summary>Stuermt zum Ziel (Ansturm).</summary>
        Charge = 7,
        /// <summary>Sprung zurueck (Rueckzug).</summary>
        LeapBack = 8,
        /// <summary>Weniger erlittener Schaden fuer Rang-Dauer (Rang extra = Prozent).</summary>
        DamageReduction = 9,
        /// <summary>Gefallenen Mitspieler zurueckholen (Rang extra = Anteil Leben in %).</summary>
        Resurrect = 10
    }

    /// <summary>Was fuer den Zauber in der Hand sein muss.</summary>
    [Flags]
    public enum SpellRequirement : byte
    {
        None = 0,
        Shield = 1,
        RangedWeapon = 2,
        MeleeWeapon = 4
    }

    /// <summary>Ein Rang: ab welcher Stufe, was er beim Lehrer kostet und wie stark er ist.</summary>
    [Serializable]
    public sealed class SpellRank
    {
        [Tooltip("Ab dieser Stufe lernbar")]
        public int level = 1;
        [Tooltip("Preis beim Klassenlehrer in Kupfer (0 = kostenlos)")]
        public int trainCost;
        [Tooltip("Kosten in der Klassen-Ressource (Wut/Energie/Mana)")]
        public int cost;
        [Tooltip("Hauptwert: Bonusschaden, Zauberschaden, Heilung (von … bis)")]
        public float min;
        public float max;
        [Tooltip("Zusatzwert: Brennen gesamt, Schild-Menge, Wut durch Ansturm, % weniger Schaden, % Leben beim Wiederbeleben")]
        public float extra;
        [Tooltip("Dauer der Nebenwirkung (Brennen, Schild, Verlangsamung …) in s; 0 = Wert des Zaubers")]
        public float duration;
    }

    /// <summary>
    /// Klassenfaehigkeit nach WoW Classic (P1, Max 30.09.): Raenge, die man beim Klassenlehrer gegen Geld lernt, Kosten in
    /// der Klassen-Ressource, globale Abklingzeit, Wirkzeit (nur im Stand), Reichweite. Der Server prueft alles und wendet
    /// die Wirkung an; auf der Leiste steht nur die Id – es gilt immer der hoechste gelernte Rang.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Klassenfaehigkeit", fileName = "Spell_")]
    public sealed class SpellDefinition : GameDefinition
    {
        protected override string IdPrefix => "spell";

        [Header("Zuordnung")]
        [Tooltip("Klasse, die den Zauber lernt (class.warden …)")]
        public string classId;
        [Tooltip("Rang 1 kennt man von Anfang an (wie in WoW die ersten Faehigkeiten)")]
        public bool startKnown;
        [Tooltip("Reihenfolge im Faehigkeitenbuch und beim Lehrer")]
        public int sortOrder;

        [Header("Darstellung")]
        public IconShape iconShape = IconShape.Sword;
        public Color iconColor = new Color(0.85f, 0.8f, 0.7f);
        public SpellSchool school = SpellSchool.Physical;

        [Header("Regeln")]
        public SpellTarget target = SpellTarget.Enemy;
        public SpellEffectKind effect = SpellEffectKind.WeaponStrike;
        [Tooltip("Wirkzeit in s (0 = sofort). Wirken geht nur im Stand – Bewegung bricht ab.")]
        public float castTime;
        [Tooltip("Eigene Abklingzeit in s")]
        public float cooldown;
        [Tooltip("Loest die globale Abklingzeit aus (1,5 s; mit Energie 1 s)")]
        public bool triggersGcd = true;
        [Tooltip("Reichweite in m (0 = Nahkampf 5 m)")]
        public float range;
        [Tooltip("Mindestabstand in m (Ansturm 8 m)")]
        public float minRange;
        [Tooltip("Nur ausserhalb des Kampfes (Ansturm, Wiederbeleben)")]
        public bool outOfCombatOnly;
        public SpellRequirement requires = SpellRequirement.None;

        [Header("Flaeche / mehrere Ziele")]
        [Tooltip("Flaeche um die eigene Figur bzw. um das Ziel (Mehrfachschuss) in m")]
        public float radius;
        [Tooltip("Hoechstens so viele Ziele (0 = eins bzw. alle in der Flaeche)")]
        public int maxTargets;

        [Header("Nebenwirkungen")]
        public float stunSeconds;
        [Tooltip("Tempo-Faktor des Ziels (0,5 = halbes Tempo); 0 = keine Verlangsamung")]
        public float slowFactor;
        public float slowSeconds;
        [Tooltip("Spott: Ziel greift den Wirker so lange an (s)")]
        public float tauntSeconds;
        [Tooltip("Brennen/Blutung ueber diese Dauer (s); Menge = Rang extra")]
        public float dotSeconds;
        [Tooltip("Bedrohung x Schaden (Tank-Faehigkeiten > 1)")]
        public float threatMultiplier = 1f;
        [Tooltip("Zusaetzliche feste Bedrohung (Schildhieb …)")]
        public float bonusThreat;
        [Tooltip("Schild haelt ueber den Rang hinaus: 'Geschwaechte Seele' in s, solange kein neuer Schild auf dasselbe Ziel")]
        public float reapplyLockSeconds;

        [Header("Raenge")]
        public List<SpellRank> ranks = new List<SpellRank>();

        [Header("Pflege")]
        [Tooltip("Stand der Standardwerte (vom Inhalts-Werkzeug); hoehere Nummer ueberschreibt beim naechsten Bauen")]
        public int defaultsVersion;

        public int RankCount => ranks.Count;
        public float Range => range > 0f ? range : 5f;
        public bool Instant => castTime <= 0.01f;
        public bool IsHostile => target == SpellTarget.Enemy || target == SpellTarget.AreaAroundSelf;

        /// <summary>Rang (1-basiert) oder null.</summary>
        public SpellRank Rank(int rank) => rank >= 1 && rank <= ranks.Count ? ranks[rank - 1] : null;

        /// <summary>Hoechster Rang, der fuer diese Stufe erlaubt ist (0 = keiner).</summary>
        public int MaxRankForLevel(int level)
        {
            int r = 0;
            for (int i = 0; i < ranks.Count; i++) if (ranks[i].level <= level) r = i + 1;
            return r;
        }

        /// <summary>Dauer der Nebenwirkung fuer einen Rang (Rang-Dauer, sonst Zauber-Wert).</summary>
        public float EffectDuration(SpellRank r, float fallback) => r != null && r.duration > 0f ? r.duration : fallback;

        public static string ResourceName(ResourceType t)
        {
            switch (t)
            {
                case ResourceType.Mana: return "Mana";
                case ResourceType.Rage: return "Wut";
                case ResourceType.Energy: return "Energie";
                default: return "";
            }
        }

        public static Color ResourceColor(ResourceType t)
        {
            switch (t)
            {
                case ResourceType.Mana: return new Color(0.2f, 0.42f, 0.9f);
                case ResourceType.Rage: return new Color(0.82f, 0.16f, 0.14f);
                case ResourceType.Energy: return new Color(0.92f, 0.82f, 0.22f);
                default: return new Color(0.4f, 0.4f, 0.4f);
            }
        }
    }
}
