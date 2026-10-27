using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>
    /// Regeln fuer Erfahrung, Stufen und Geld. Rein rechnerisch – Server und Client nutzen dieselben Zahlen.
    ///
    /// Stufen (kanonisch, Update 26.09.2026): das ganze Spiel geht bis <see cref="GlobalMaxLevel"/> 60.
    /// Der aktuelle Inhalt (Kareth und Umgebung) zielt auf <see cref="CurrentContentTargetLevel"/> 20 –
    /// das ist KEINE Obergrenze, nur die Zielmarke fuer Balance und Inhalte. Stufe 20 im Startinhalt soll ein
    /// Prestige-Ziel sein, das man nur mit vielen optionalen Inhalten erreicht.
    ///
    /// Kurve (Max 03.10.2026 spaetabends, „ganze Kurve 1–60 an Classic angleichen“): Tabelle <see cref="XpTable"/> im
    /// Classic-Mass – glatt (PCHIP) durch Stuetzpunkte der Classic-Groessenordnung (1: 400, 10: 7 600, 20: 23 200, 30: 47 400,
    /// 40: 90 700, 50: 147 500, 59: 209 800), auf 100 gerundet; 1–60 gesamt 4 082 400 EP. Jede Stufe braucht mehr als die vorige.
    /// Die fruehere Phasenkurve (Prestige 19→20 ×5, ab 20 Platzhalter) ist ersetzt; <see cref="PhaseFactor"/> bleibt nur zur Auskunft.
    /// Normale Gegner geben wenig (45 + 5·Stufe wie Classic), Auftraege, Entdeckungen, Elite, Bosse und Dungeons deutlich mehr.
    /// </summary>
    public static class Progression
    {
        /// <summary>Hoechste Stufe im fertigen Spiel.</summary>
        public const int GlobalMaxLevel = 60;
        /// <summary>Zielstufe des aktuellen Inhalts (Kareth, Salzgruben ...). Keine Obergrenze.</summary>
        public const int CurrentContentTargetLevel = 20;
        /// <summary>Veralteter Name – entspricht <see cref="GlobalMaxLevel"/>.</summary>
        public const int MaxLevel = GlobalMaxLevel;
        public const int StartMoney = 50; // Kupfer

        /// <summary>Phasenfaktor der frueheren Stufenkurve (bis 03.10.2026). Wird fuer <see cref="XpToNext"/> nicht mehr verwendet.</summary>
        public static float PhaseFactor(int level)
        {
            if (level <= 4) return 1f;
            if (level <= 9) return 1f + 0.15f * (level - 4);
            if (level <= 14) return 1.9f + 0.15f * (level - 10);
            if (level <= 18) return 2.8f + 0.25f * (level - 15);
            if (level == 19) return 5f;
            return 4.6f + 0.05f * (level - 20);
        }

        /// <summary>Erfahrung von Stufe <paramref name="level"/> zur naechsten.</summary>
        public static int XpToNext(int level)
        {
            if (level >= GlobalMaxLevel) return 0;
            return XpTable[Mathf.Clamp(level, 1, GlobalMaxLevel - 1) - 1];
        }

        /// <summary>Erfahrung von Stufe L zu L+1 (Index L-1), Stufen 1–59. Classic-Mass, siehe Klassenbeschreibung.</summary>
        private static readonly int[] XpTable =
        {
            400, 900, 1400, 2100, 2800, 3600, 4500, 5500, 6500, 7600,                       // 1–10
            8800, 10100, 11500, 12900, 14500, 16100, 17800, 19500, 21300, 23200,             // 11–20
            25100, 27200, 29300, 31500, 33800, 36200, 38800, 41500, 44400, 47400,            // 21–30
            50700, 54300, 58200, 62400, 66800, 71400, 76100, 80900, 85800, 90700,            // 31–40
            95700, 100900, 106300, 111800, 117400, 123200, 129100, 135200, 141300, 147500,   // 41–50
            153800, 160300, 167000, 173800, 180700, 187800, 195000, 202300, 209800           // 51–59
        };

        /// <summary>Gesamte Erfahrung von Stufe 1 bis zum Erreichen von <paramref name="level"/>.</summary>
        public static long TotalXpTo(int level)
        {
            long sum = 0;
            for (int l = 1; l < Mathf.Min(level, GlobalMaxLevel); l++) sum += XpToNext(l);
            return sum;
        }

        /// <summary>
        /// Erfahrung fuer einen besiegten Gegner (fuer einen Spieler allein; in der Gruppe <see cref="GroupShare"/>).
        /// Grundwert wie WoW Classic: 45 + 5 × Stufe (P2, 30.09. – vorher 40 + 10 × Stufe, damit ging Stufe 10 in gut einer Stunde).
        /// Hoehere Gegner geben bis zu 40 % mehr. Niedrigere wie Classic (Max 10.10.2026: „Graue Gegner: Ja, 0 EP“): Abzug nach der
        /// Nulldifferenz-Tabelle <see cref="ZeroDifference"/>, ab der Grau-Stufe <see cref="GrayLevel"/> gar nichts. Elite/Dungeon: <see cref="Balance.XpFactor"/>.
        /// </summary>
        public static int KillXp(int enemyLevel, int playerLevel)
        {
            enemyLevel = Mathf.Max(1, enemyLevel);
            playerLevel = Mathf.Max(1, playerLevel);
            int baseXp = 45 + 5 * enemyLevel;
            int diff = enemyLevel - playerLevel;
            if (diff >= 0) return Mathf.RoundToInt(baseXp * (1f + 0.1f * Mathf.Min(diff, 4)));
            if (enemyLevel <= GrayLevel(playerLevel)) return 0;
            float mult = 1f - (float)(playerLevel - enemyLevel) / ZeroDifference(playerLevel);
            return Mathf.Max(0, Mathf.RoundToInt(baseXp * mult));
        }

        /// <summary>Hoechste Gegnerstufe, die fuer diese Spielerstufe grau ist (0 EP) – Classic-Formel; bis Stufe 5 ist nichts grau.</summary>
        public static int GrayLevel(int playerLevel)
        {
            if (playerLevel <= 5) return 0;
            if (playerLevel <= 39) return playerLevel - 5 - playerLevel / 10;
            return playerLevel - 1 - playerLevel / 5;
        }

        /// <summary>Nulldifferenz (Classic): ueber so viele Stufen faellt die Erfahrung niedrigerer Gegner auf null.</summary>
        public static int ZeroDifference(int playerLevel)
        {
            if (playerLevel <= 7) return 5;
            if (playerLevel <= 9) return 6;
            if (playerLevel <= 11) return 7;
            if (playerLevel <= 15) return 8;
            if (playerLevel <= 19) return 9;
            if (playerLevel <= 29) return 11;
            if (playerLevel <= 39) return 12;
            if (playerLevel <= 44) return 13;
            if (playerLevel <= 49) return 14;
            if (playerLevel <= 54) return 15;
            if (playerLevel <= 59) return 16;
            return 17;
        }

        /// <summary>
        /// Gruppenbonus wie Classic (Max 10.10.2026: „Gruppen-EP: Ja, wie Classic“): 1–2 Spieler ×1, 3 ×1,166, 4 ×1,3, ab 5 ×1,4.
        /// Gruppe = alle beuteberechtigten Beteiligten am Sieg (ein Gruppensystem gibt es noch nicht).
        /// </summary>
        public static float GroupBonus(int members)
        {
            if (members <= 2) return 1f;
            if (members == 3) return 1.166f;
            if (members == 4) return 1.3f;
            return 1.4f;
        }

        /// <summary>Anteil je Beteiligtem: Bonus geteilt durch die Zahl der Beteiligten (allein = 1).</summary>
        public static float GroupShare(int members) => members <= 1 ? 1f : GroupBonus(members) / members;

        // ------------------------------------------------------------------ Ausgeruht (Max 10.10.2026: „Ja, wie Classic“)

        /// <summary>Hoechstens anderthalb Stufen Ruhe (Classic); auf der Hoechststufe keine.</summary>
        public static int RestedCap(int level) => level >= GlobalMaxLevel ? 0 : Mathf.RoundToInt(1.5f * XpToNext(level));

        /// <summary>
        /// Ruhe je Stunde: 5 % der Stufe je 8 Stunden in einem Gasthaus (auch angemeldet, wenn man dort steht);
        /// abgemeldet ausserhalb ein Viertel davon (Classic).
        /// </summary>
        public static float RestedPerHour(int level, bool inRestArea) =>
            level >= GlobalMaxLevel ? 0f : XpToNext(level) * 0.05f / 8f * (inRestArea ? 1f : 0.25f);

        /// <summary>Neuer Ruhestand nach <paramref name="hours"/> Stunden (gedeckelt).</summary>
        public static int AccrueRested(int rested, int level, double hours, bool inRestArea)
        {
            if (hours <= 0) return Mathf.Clamp(rested, 0, RestedCap(level));
            double add = RestedPerHour(level, inRestArea) * hours;
            return (int)System.Math.Min(RestedCap(level), System.Math.Max(0, rested) + add);
        }

        /// <summary>Zusatz-EP aus Ruhe fuer einen Sieg: verdoppelt die Sieg-EP, solange Ruhe da ist (nicht fuer Auftraege).</summary>
        public static int RestedBonus(int killXp, int rested) => Mathf.Clamp(Mathf.Min(killXp, rested), 0, int.MaxValue);

        /// <summary>
        /// Entdeckung (P3 Glockenrast, Max 30.09.: „wie Classic“): kleine Erfahrung beim ersten Betreten eines benannten Ortes,
        /// abhaengig von der Stufe des Ortes (Untergrenze seines Stufenbands): 20 + 5 × Ortsstufe.
        /// </summary>
        public static int DiscoveryXp(int areaLevel) => 20 + 5 * Mathf.Max(1, areaLevel);

        /// <summary>Grau = gibt keine Erfahrung mehr (Classic-Grau-Stufe, siehe <see cref="GrayLevel"/>).</summary>
        public static bool IsTrivial(int enemyLevel, int playerLevel) => KillXp(enemyLevel, playerLevel) <= 0;

        /// <summary>Zusaetzliches Grundleben je Stufe ueber 1.</summary>
        public const float HealthPerLevel = 20f; // P2: 15 → 20, dazu ~0,5 Ausdauer je Stufe aus Ausruestung = Balance.RefHealth
        /// <summary>Zusaetzlicher Schaden je Stufe ueber 1 (Anteil).</summary>
        public const float DamagePerLevel = 0.04f;

        // ------------------------------------------------------------------ Geld (1 Gold = 100 Silber = 10 000 Kupfer)

        /// <summary>"1g 20s 5k" in Muenzfarben (TextMeshPro-Richtext).</summary>
        public static string FormatMoney(long copper, bool rich = true)
        {
            if (copper < 0) copper = 0;
            long g = copper / 10000, s = copper / 100 % 100, k = copper % 100;
            var sb = new System.Text.StringBuilder(64);
            void Part(long v, string unit, string color)
            {
                if (sb.Length > 0) sb.Append(' ');
                if (rich) sb.Append("<color=").Append(color).Append('>');
                sb.Append(v).Append(unit);
                if (rich) sb.Append("</color>");
            }
            if (g > 0) Part(g, "g", "#e8c35a");
            if (s > 0 || g > 0) Part(s, "s", "#c9ccd4");
            if (k > 0 || sb.Length == 0) Part(k, "k", "#c8834a");
            return sb.ToString();
        }

        /// <summary>Kaufpreis beim Haendler (0 im Angebot = vierfacher Verkaufswert, mindestens 1 Kupfer).
        /// 21.10. (FORTSCHRITT-02): <paramref name="count"/> = Stueckzahl des Angebots. Der Standardpreis gilt je Stueck,
        /// sonst kostete „Leinenfetzen ×5“ 8k und brachte beim Verkauf 10k (endlose Geldquelle). Feste Preise gelten
        /// weiter fuer das ganze Angebot (Pfeile ×200).</summary>
        public static int BuyPrice(ItemDefinition def, int overridePrice, int count = 1)
        {
            if (overridePrice > 0) return overridePrice;
            return def == null ? 0 : Mathf.Max(1, def.sellPrice * 4 * Mathf.Max(1, count));
        }

        public static bool Sellable(ItemDefinition def) =>
            def != null && def.sellPrice > 0 && def.category != ItemCategory.Quest;
    }
}
