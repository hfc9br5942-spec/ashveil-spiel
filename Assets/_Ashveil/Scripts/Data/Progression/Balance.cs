using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>Rolle eines Gegners in der Balance-Tabelle (P2, 30.09.). "Eigene Werte" = feste Werte aus der Definition.</summary>
    public enum BalanceRole
    {
        /// <summary>Eigene, fest eingetragene Werte (Weltbosse, Sonderfaelle).</summary>
        Custom,
        /// <summary>Schwach (Welpen, Tiere am Wegrand): halbes Leben, wenig Schaden.</summary>
        Weak,
        /// <summary>Normaler Gegner – der Massstab: gleiche Stufe = etwa 7–15 s Kampf, ein gutes Drittel des Lebens.</summary>
        Normal,
        /// <summary>Stark (Schrate, Brocken): mehr Leben, harte Schlaege – solo machbar, aber ein echter Kampf.</summary>
        Strong,
        /// <summary>Elite in der offenen Welt: fuer 3 Spieler gedacht, 45–90 s.</summary>
        Elite,
        /// <summary>Dungeon-Gegner (Salzgruben-Trupps): fuer eine Gruppe von 3–5, einzeln kurz, zusammen gefaehrlich.</summary>
        DungeonTrash,
        /// <summary>Dungeon-Boss: 3–5 Spieler, etwa 1,5–2,5 Minuten.</summary>
        DungeonBoss
    }

    /// <summary>
    /// Balance-Tabelle Stufe 1–20 (P2, Max 30.09.: "Werte in einer Tabelle abstimmen"). Ein Referenzspieler mit
    /// stufengerechter Ausruestung und seiner Rotation macht <see cref="RefDps"/> Schaden je Sekunde und hat
    /// <see cref="RefHealth"/> Leben. Daraus folgen Leben und Schaden aller Gegner mit einer <see cref="BalanceRole"/>:
    ///   · Zeit bis zum Sieg (gleiche Stufe, allein): Stufe 1 = 7 s, steigt bis Stufe 10 auf 15 s
    ///   · ein Kampf kostet Stufe 1–3 etwa 30 %, danach etwa 42 % des Lebens (vor Ruestung, Block, Ausweichen)
    /// Classic-Gefuehl, aber ohne Leerlauf: ausserhalb des Kampfes heilt man nach 8 s schnell (5 %/s).
    /// Der Balance-Test (Ashveil › Test › Balance) misst die echten Werte je Klasse im Spiel nach.
    /// </summary>
    public static class Balance
    {
        /// <summary>Schaden je Sekunde des Referenzspielers (Auto-Angriff, Waffenfaehigkeit, Klassenrotation).</summary>
        public static float RefDps(int level) => 24f + 5f * (Lv(level) - 1);

        /// <summary>
        /// Leben des Referenzspielers: Grundleben je Stufe + Ausdauer aus stufengerechter Ausruestung (Messung 30.09.: voll
        /// ausgeruestet Stufe 10 etwa 470–720, nur Startausruestung deutlich weniger – die Referenz liegt dazwischen).
        /// </summary>
        public static float RefHealth(int level) => 100f + 45f * (Lv(level) - 1);

        /// <summary>Zielzeit bis zum Sieg gegen einen normalen Gegner gleicher Stufe.</summary>
        public static float TargetTtk(int level)
        {
            int l = Lv(level);
            if (l >= 10) return 15f;
            return 7f + 8f * (l - 1) / 9f;
        }

        /// <summary>Anteil des Lebens, den ein normaler Kampf gleicher Stufe kostet (vor Ruestung).</summary>
        public static float HealthCostPerFight(int level) => Lv(level) <= 3 ? 0.3f : 0.42f;

        /// <summary>Leben eines normalen Gegners dieser Stufe.</summary>
        public static float MobHealth(int level) => Mathf.Round(RefDps(level) * TargetTtk(level));

        /// <summary>Schaden je Sekunde eines normalen Gegners dieser Stufe.</summary>
        public static float MobDps(int level) => RefHealth(level) * HealthCostPerFight(level) / TargetTtk(level);

        /// <summary>Faktoren (Leben, Schaden) je Rolle.</summary>
        public static Vector2 RoleFactors(BalanceRole role)
        {
            switch (role)
            {
                case BalanceRole.Weak: return new Vector2(0.5f, 0.6f);
                case BalanceRole.Strong: return new Vector2(1.7f, 1.35f);
                case BalanceRole.Elite: return new Vector2(9f, 2.4f);
                case BalanceRole.DungeonTrash: return new Vector2(2.6f, 1.5f);
                case BalanceRole.DungeonBoss: return new Vector2(32f, 2.6f);
                default: return Vector2.one;
            }
        }

        /// <summary>Erfahrung: Elite und Bosse zaehlen mehr (Classic: Elite doppelt); Dungeon-Gegner nur leicht (WoW: Forever).</summary>
        public static float XpFactor(BalanceRole role, bool elite)
        {
            switch (role)
            {
                case BalanceRole.Weak: return 0.5f;
                case BalanceRole.Strong: return 1.5f;
                case BalanceRole.Elite: return 3f;
                case BalanceRole.DungeonTrash: return 1.1f;   // Max 10.10.2026: „Dungeon-EP wie Forever“ (vorher 1,5) – dafuer mehr EP aus Dungeon-Auftraegen
                case BalanceRole.DungeonBoss: return 6f;
                default: return elite ? 2f : 1f;
            }
        }

        // ------------------------------------------------------------------ Ausruestung

        /// <summary>
        /// Durchschnittlicher Waffenschaden (Mittel aus min/max) einer Einhandwaffe mit Tempo 1 und dieser Gegenstandsstufe –
        /// der Auto-Angriff mit Schlagfolge schafft etwa 2 × diesen Wert je Sekunde. Kalibriert an den vorhandenen Waffen (30.09.);
        /// der Rest des Wachstums kommt aus Stufe (+4 %/Stufe) und Werten. Qualitaet: gruen +8 %, blau +16 %, lila +26 %.
        /// </summary>
        public static float WeaponAverage(int itemLevel, ItemQuality quality)
        {
            float q = quality >= ItemQuality.Legendary ? 1.36f : quality == ItemQuality.Epic ? 1.26f : quality == ItemQuality.Rare ? 1.16f :
                      quality == ItemQuality.Uncommon ? 1.08f : 1f;
            return (10f + 0.7f * (Mathf.Max(1, itemLevel) - 1)) * q;
        }

        private static int Lv(int level) => Mathf.Clamp(level, 1, Progression.GlobalMaxLevel);
    }
}
