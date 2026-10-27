using System;

namespace Ashveil.Data
{
    /// <summary>
    /// Klassenreittier (Class Identity Masterplan §7–§24, Max 28.09.: nur der Eidwaechter, +40 % Tempo, nicht in Instanzen,
    /// Kolosseum oder im Kampf). Eine Figur mit Namen, kein „Vehicle 004“ (§11). Die Bindung waechst durch gemeinsames
    /// Reisen (§20/§21) und nutzt dieselbe Technik wie die Praxis-Meisterschaften.
    /// </summary>
    [Serializable]
    public sealed class ClassMountDefinition
    {
        public string id;
        /// <summary>Art des Tiers (Anzeige, wenn der Spieler keinen eigenen Namen vergeben hat).</summary>
        public string displayName;
        /// <summary>Welche Klasse es reiten darf (Klassen-Kennung).</summary>
        public string classId;
        /// <summary>Tempo-Aufschlag gegenueber dem Laufen (0,4 = +40 %).</summary>
        public float speedBonus = 0.4f;
        /// <summary>Aufsitzen dauert so lange (s); Bewegen oder Schaden bricht ab.</summary>
        public float mountSeconds = 1.5f;
        /// <summary>Bindung: waechst mit der gerittenen Strecke (Meter).</summary>
        public MasteryDefinition bond;
    }

    /// <summary>
    /// Alle Klassenreittiere an einer Stelle (Prototyp: Code-Katalog; spaeter ScriptableObjects, sobald Designer ohne Code
    /// tunen sollen – wie bei den Meisterschaften). Nharim-Seelenross folgt mit dem Nharim-Koerper.
    /// </summary>
    public static class MountCatalog
    {
        public const string WarhorseId = "mount.warhorse";
        public const string WardenClassId = "class.warden";

        /// <summary>
        /// Kriegspferd des Eidwaechters (Arbeitstitel, Quest „Der letzte Reiter“ folgt nach Freigabe). Bindung: Rang 25 ≈ 64 km,
        /// Rang 50 ≈ 300 km geritten. Wirkung klein (§22): schneller aufsitzen, etwas mehr Tempo.
        /// </summary>
        public static readonly ClassMountDefinition Warhorse = new ClassMountDefinition
        {
            id = WarhorseId,
            displayName = "Kriegspferd",
            classId = WardenClassId,
            speedBonus = 0.4f,
            mountSeconds = 1.5f,
            bond = new MasteryDefinition
            {
                id = "bond.warhorse",
                displayName = "Bindung",
                description = "Je mehr ihr gemeinsam reist, desto vertrauter wird es mit dir.",
                firstRankCost = 1250.0,
                growth = 1.055,
                effects = new[]
                {
                    new MasteryEffect { key = "mountTime", displayName = "Zeit zum Aufsitzen", atMax = 0.5f, exponent = 0.7f, sign = "−" },
                    new MasteryEffect { key = "mountSpeed", displayName = "Reittempo", atMax = 0.1f, exponent = 0.7f, sign = "+" }
                }
            }
        };

        public static readonly ClassMountDefinition[] All = { Warhorse };

        public static ClassMountDefinition Get(string id)
        {
            foreach (ClassMountDefinition m in All) if (m.id == id) return m;
            return null;
        }

        /// <summary>Reittier der Klasse (oder null – nur der Tank hat eines, §7/§111).</summary>
        public static ClassMountDefinition ForClass(string classId)
        {
            foreach (ClassMountDefinition m in All) if (m.classId == classId) return m;
            return null;
        }

        /// <summary>Tempo-Aufschlag inkl. Bindung (Rang 0..100).</summary>
        public static float SpeedBonus(ClassMountDefinition m, int bondRank) =>
            m == null ? 0f : m.speedBonus + m.bond.Effect("mountSpeed", bondRank);

        public static float MountSeconds(ClassMountDefinition m, int bondRank) =>
            m == null ? 1.5f : m.mountSeconds * (1f - m.bond.Effect("mountTime", bondRank));
    }
}
