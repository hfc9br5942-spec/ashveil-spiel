using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>
    /// Eine dominierbare Art als Gefaehrte des Dominators (Klassen-Identitaet §58–§91, §175 DominatableCreatureDefinition).
    /// Basisangriff + genau eine Signatur + ein passiver Zug (§66–§68). Werte als Startwerte (§70: spaeter mit Tests).
    /// </summary>
    public sealed class CompanionSpecies
    {
        public string id;
        /// <summary>Wilde Form (EnemyDefinition), die man dominieren kann.</summary>
        public string enemyId;
        public string displayName;
        public string classId;

        // Kampf – skaliert mit der Stufe des Besitzers (§80: kein Gefaehrten-Level-Mikromanagement)
        public float baseHealth = 70f, healthPerLevel = 17f;
        public float baseDamage = 2.9f, damagePerLevel = 0.8f;   // gemessen: ≈ 25 % des Klassenschadens (§70)
        public float attackInterval = 1.6f, windup = 0.35f, attackRange = 1.9f;
        public float walkSpeed = 2.4f, runSpeed = 7.4f;

        // Signatur und Zug
        public string signatureName = "", signatureDescription = "";
        public float leapMin = 4f, leapMax = 10f, leapCooldown = 12f, leapDamageFactor = 1.5f;
        public float bleedPerSecondFactor = 0.35f, bleedSeconds = 6f;
        public string traitName = "", traitDescription = "";
        public float traitWoundedBelow = 0.35f, traitBonus = 0.15f;

        // Dominieren (§59/§75)
        public float dominateBelow = 0.35f, dominateSeconds = 3f, dominateRange = 10f;

        /// <summary>Bindung (§79–§82): kleine Boni, eigene Meisterschaft.</summary>
        public MasteryDefinition bond;

        public float HealthAt(int level, int bondRank) =>
            (baseHealth + healthPerLevel * Mathf.Max(0, level - 1)) * (1f + (bond != null ? bond.Effect("petHealth", bondRank) : 0f));

        public float DamageAt(int level) => baseDamage + damagePerLevel * Mathf.Max(0, level - 1);

        public float LeapCooldown(int bondRank) => leapCooldown * (1f - (bond != null ? bond.Effect("petCooldown", bondRank) : 0f));
    }

    /// <summary>Alle Gefaehrten-Arten an einer Stelle (wie <see cref="MountCatalog"/>). Prototyp §185: Aschwolf.</summary>
    public static class CompanionCatalog
    {
        public const string DominatorClassId = "class.ashstalker";
        public const string AshWolfId = "companion.ash_wolf";
        /// <summary>Leisten-Eintraege (<see cref="ActionEntryType.Companion"/>).</summary>
        public const string CallId = "companion.call";
        public const string DominateId = "companion.dominate";

        public static readonly CompanionSpecies AshWolf = new CompanionSpecies
        {
            id = AshWolfId,
            enemyId = "enemy.ash_wolf",
            displayName = "Aschwolf",
            classId = DominatorClassId,
            signatureName = "Sprungbiss",
            signatureDescription = "Springt aus 4–10 m an sein Ziel, beißt kräftig zu und lässt es bluten.",
            traitName = "Hetzjäger",
            traitDescription = "Mehr Schaden gegen verwundete Ziele (unter 35 % Leben).",
            bond = new MasteryDefinition
            {
                id = "bond.ash_wolf",
                displayName = "Bindung",
                description = "Gemeinsame Kämpfe, Siege über Elitegegner und Wege schweißen euch zusammen.",
                firstRankCost = 3.0,
                growth = 1.055,
                effects = new[]
                {
                    new MasteryEffect { key = "petHealth", displayName = "Leben des Gefährten", atMax = 0.25f, exponent = 0.7f, sign = "+" },
                    new MasteryEffect { key = "petCooldown", displayName = "Abklingzeit Sprungbiss", atMax = 0.30f, exponent = 0.7f, sign = "−" }
                }
            }
        };

        public static readonly CompanionSpecies[] All = { AshWolf };

        public static CompanionSpecies Get(string id)
        {
            foreach (CompanionSpecies s in All) if (s.id == id) return s;
            return null;
        }

        public static CompanionSpecies ForEnemy(string enemyId)
        {
            foreach (CompanionSpecies s in All) if (s.enemyId == enemyId) return s;
            return null;
        }
    }
}
