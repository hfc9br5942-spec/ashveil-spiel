using System;
using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>Eine Wirkung einer Meisterschaft (Praxis-Meisterschaften §12/§17/§24/§53).</summary>
    [Serializable]
    public sealed class MasteryEffect
    {
        /// <summary>Technischer Schluessel (travel, sprintCost, dodgeCost, dodgeRecovery).</summary>
        public string key;
        /// <summary>Anzeige, z. B. „Reisetempo im Reiserhythmus“.</summary>
        public string displayName;
        /// <summary>Wert bei Rang 100 (0,15 = 15 %).</summary>
        public float atMax;
        /// <summary>Kurve: Wert = atMax · (Rang/100)^exponent – unter 1 heisst: frueh spuerbar, spaet flach (§6/§7).</summary>
        public float exponent = 0.7f;
        /// <summary>Anzeige mit Vorzeichen: „+“ (mehr ist besser) oder „−“ (weniger Kosten).</summary>
        public string sign = "+";
    }

    /// <summary>
    /// Eine Praxis-Meisterschaft (Vorgabe 28.09., §59 MasteryDefinition): Rang 0–100, Kosten je Rang wachsen
    /// geometrisch (0–25 schnell, 75–100 sehr langfristig), Meilensteine alle 10 Raenge.
    /// </summary>
    [Serializable]
    public sealed class MasteryDefinition
    {
        public string id;
        public string displayName;
        /// <summary>Kurzer Satz fuer das Fenster.</summary>
        public string description;
        /// <summary>Kosten des ersten Rangs in Lernpunkten (Laufkunst: Meter).</summary>
        public double firstRankCost;
        /// <summary>Kosten je Rang wachsen um diesen Faktor.</summary>
        public double growth = 1.055;
        public int maxRank = 100;
        public int milestoneEvery = 10;
        public MasteryEffect[] effects = new MasteryEffect[0];

        public double CostOfRank(int rank) => firstRankCost * Math.Pow(growth, rank);

        /// <summary>Lernpunkte insgesamt bis zu diesem Rang.</summary>
        public double TotalFor(int rank) => rank <= 0 ? 0 : firstRankCost * (Math.Pow(growth, rank) - 1.0) / (growth - 1.0);

        /// <summary>Rang, Punkte im Rang und Kosten des naechsten Rangs aus den Gesamtpunkten.</summary>
        public int RankFor(double points, out double into, out double next)
        {
            int rank = 0;
            if (points > 0)
            {
                // geschlossen: rank = floor(log(1 + p(g-1)/c) / log g)
                rank = (int)Math.Floor(Math.Log(1.0 + points * (growth - 1.0) / firstRankCost) / Math.Log(growth));
                rank = Mathf.Clamp(rank, 0, maxRank);
                while (rank < maxRank && TotalFor(rank + 1) <= points) rank++;
                while (rank > 0 && TotalFor(rank) > points) rank--;
            }
            into = points - TotalFor(rank);
            next = rank >= maxRank ? 0 : CostOfRank(rank);
            return rank;
        }

        public int RankFor(double points) => RankFor(points, out _, out _);

        /// <summary>„+6 % Reisetempo im Reiserhythmus, −10 % Ausdauer beim Sprinten“</summary>
        public string EffectsText(int rank)
        {
            var sb = new System.Text.StringBuilder();
            foreach (MasteryEffect e in effects)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(e.sign).Append(Mathf.RoundToInt(Effect(e.key, rank) * 100f)).Append(" % ").Append(e.displayName);
            }
            return sb.ToString();
        }

        public float Effect(string key, int rank)
        {
            foreach (MasteryEffect e in effects)
                if (e != null && e.key == key) return e.atMax * Mathf.Pow(Mathf.Clamp(rank, 0, maxRank) / (float)maxRank, e.exponent);
            return 0f;
        }
    }

    /// <summary>
    /// Alle Meisterschaften an einer Stelle (keine Werte verstreut, §59). Erster Prototyp: Laufkunst und Ausweichen
    /// (§97). Startwerte zum Testen – balanciert wird mit echten Daten (§103). Kurven: Docs/Design/ashveil-mastery-pass1.md.
    /// </summary>
    public static class MasteryCatalog
    {
        public const string RunningId = "mastery.running";
        public const string DodgingId = "mastery.dodging";

        /// <summary>Laufkunst: Lernpunkte = zu Fuss zurueckgelegte Meter. Rang 25 ≈ 37 km, 50 ≈ 178 km, 100 ≈ 2 770 km.</summary>
        public static readonly MasteryDefinition Running = new MasteryDefinition
        {
            id = RunningId,
            displayName = "Laufkunst",
            description = "Wer viel zu Fuß reist, reist leichter.",
            firstRankCost = 724.0,
            growth = 1.055,
            effects = new[]
            {
                new MasteryEffect { key = "travel", displayName = "Reisetempo im Reiserhythmus", atMax = 0.15f, exponent = 0.7f, sign = "+" },
                new MasteryEffect { key = "sprintCost", displayName = "Ausdauer beim Sprinten", atMax = 0.25f, exponent = 0.7f, sign = "−" }
            }
        };

        /// <summary>Ausweichen: Lernpunkte = Ausweichrollen, die einem echten Angriff entkommen. Rang 25 ≈ 200, 100 ≈ 15 000.</summary>
        public static readonly MasteryDefinition Dodging = new MasteryDefinition
        {
            id = DodgingId,
            displayName = "Ausweichen",
            description = "Nur wer echten Angriffen entkommt, lernt dazu.",
            firstRankCost = 4.0,
            growth = 1.055,
            effects = new[]
            {
                new MasteryEffect { key = "dodgeCost", displayName = "Ausdauer je Ausweichrolle", atMax = 0.20f, exponent = 0.7f, sign = "−" },
                new MasteryEffect { key = "dodgeRecovery", displayName = "Erholung nach der Rolle", atMax = 0.25f, exponent = 0.7f, sign = "+" }
            }
        };

        public const string BlockingId = "mastery.blocking";

        /// <summary>
        /// Schildblock (passiv, nur mit Schild – Max 28.09.): Lernpunkte = abgefangener Schaden gegen Gegner, mit
        /// Stufenabschlag. Rang 25 ≈ 2 000 (≈ 150 Blocks), 100 ≈ 155 000.
        /// </summary>
        public static readonly MasteryDefinition Blocking = new MasteryDefinition
        {
            id = BlockingId,
            displayName = "Schildblock",
            description = "Wer Treffer mit dem Schild abfängt, fängt sie besser ab.",
            firstRankCost = 40.0,
            growth = 1.055,
            effects = new[]
            {
                new MasteryEffect { key = "blockChance", displayName = "Blockchance", atMax = 0.05f, exponent = 0.7f, sign = "+" },
                new MasteryEffect { key = "blockValue", displayName = "Blockwert", atMax = 0.30f, exponent = 0.7f, sign = "+" }
            }
        };

        public static readonly MasteryDefinition[] All = { Running, Dodging, Blocking };

        public const string SalvageId = "mastery.salvage";

        /// <summary>
        /// Nachlese (Heiler-Signatur, Klassen-Identitaet §48–§50): Lernpunkte je Bergung (selten = 3), mit Stufenabschlag –
        /// Level-1-Woelfe lehren einen hohen Heiler nichts. Nicht in <see cref="All"/>: erscheint nur, wer sie erlernt hat.
        /// Rang 25 ≈ 100 Bergungen, 50 ≈ 500.
        /// </summary>
        public static readonly MasteryDefinition Salvage = new MasteryDefinition
        {
            id = SalvageId,
            displayName = "Nachlese",
            description = "Wer nach dem Kampf sorgfältig birgt, findet Kostbareres – und schneller.",
            firstRankCost = 2.0,
            growth = 1.055,
            effects = new[]
            {
                new MasteryEffect { key = "rareChance", displayName = "Chance auf seltene Stoffe", atMax = 0.60f, exponent = 0.7f, sign = "+" },
                new MasteryEffect { key = "salvageSpeed", displayName = "Zeit zum Bergen", atMax = 0.40f, exponent = 0.7f, sign = "−" }
            }
        };

        public static MasteryDefinition Get(string id)
        {
            foreach (MasteryDefinition d in All) if (d.id == id) return d;
            if (id == SalvageId) return Salvage;
            foreach (CompanionSpecies c in CompanionCatalog.All) if (c.bond != null && c.bond.id == id) return c.bond; // Gefaehrten-Bindung
            foreach (ClassMountDefinition m in MountCatalog.All) if (m.bond != null && m.bond.id == id) return m.bond; // Reittier-Bindung
            return null;
        }

        /// <summary>
        /// Lernwert gegen schwache Gegner (§21/§64): bis 2 Stufen unter dem Spieler voll, dann je Stufe 25 % weniger,
        /// ab 6 Stufen darunter nichts.
        /// </summary>
        public static float LevelFactor(int enemyLevel, int playerLevel)
        {
            int diff = enemyLevel - playerLevel;
            if (diff >= -2) return 1f;
            return Mathf.Max(0f, 1f + (diff + 2) * 0.25f);
        }
    }
}
