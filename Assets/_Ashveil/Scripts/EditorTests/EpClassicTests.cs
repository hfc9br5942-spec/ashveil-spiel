using Ashveil.Data;
using NUnit.Framework;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// EP-Regeln wie WoW Classic (Max 10.10.2026: graue Gegner 0 EP, Gruppen-EP mit Bonus, Ausgeruht, Dungeon-EP wie Forever).
    /// Reine Rechnung (<see cref="Progression"/>, <see cref="Balance"/>).
    /// </summary>
    public class EpClassicTests
    {
        [Test]
        public void Grau_nach_Classic_Formel()
        {
            Assert.AreEqual(0, Progression.GrayLevel(5), "bis Stufe 5 ist nichts grau");
            Assert.AreEqual(1, Progression.GrayLevel(6));
            Assert.AreEqual(4, Progression.GrayLevel(10));
            Assert.AreEqual(13, Progression.GrayLevel(20));
            Assert.AreEqual(31, Progression.GrayLevel(39));
            Assert.AreEqual(31, Progression.GrayLevel(40));
            Assert.AreEqual(47, Progression.GrayLevel(60));
            Assert.AreEqual(0, Progression.KillXp(4, 10), "Stufe 4 ist für Stufe 10 grau");
            Assert.Greater(Progression.KillXp(5, 10), 0, "Stufe 5 ist für Stufe 10 noch grün");
            Assert.AreEqual(0, Progression.KillXp(13, 20));
            Assert.Greater(Progression.KillXp(14, 20), 0);
            Assert.IsTrue(Progression.IsTrivial(1, 6));
            Assert.IsFalse(Progression.IsTrivial(1, 5));
        }

        [Test]
        public void Niedrigere_Gegner_nach_Nulldifferenz_hoehere_unveraendert()
        {
            Assert.AreEqual(50, Progression.KillXp(1, 1));
            Assert.AreEqual(95, Progression.KillXp(10, 10));
            // Stufe 10 gegen Stufe 8: 85 × (1 − 2/7)
            Assert.AreEqual(UnityEngine.Mathf.RoundToInt(85f * (1f - 2f / 7f)), Progression.KillXp(8, 10));
            // höhere Gegner wie bisher: +10 % je Stufe, höchstens +40 %
            Assert.AreEqual(UnityEngine.Mathf.RoundToInt(105f * 1.2f), Progression.KillXp(12, 10));
            Assert.AreEqual(UnityEngine.Mathf.RoundToInt(125f * 1.4f), Progression.KillXp(16, 10));
            // niemals steigend nach unten
            for (int p = 1; p <= 60; p++)
                for (int e = 1; e < p; e++)
                    Assert.LessOrEqual(Progression.KillXp(e, p), Progression.KillXp(e + 1, p), $"Spieler {p}: Gegner {e} ≤ {e + 1}");
        }

        [Test]
        public void Gruppenbonus_wie_Classic()
        {
            Assert.AreEqual(1f, Progression.GroupShare(1));
            Assert.AreEqual(0.5f, Progression.GroupShare(2), 1e-4);
            Assert.AreEqual(1.166f / 3f, Progression.GroupShare(3), 1e-4);
            Assert.AreEqual(1.3f / 4f, Progression.GroupShare(4), 1e-4);
            Assert.AreEqual(1.4f / 5f, Progression.GroupShare(5), 1e-4);
            Assert.AreEqual(1.4f / 6f, Progression.GroupShare(6), 1e-4);
        }

        [Test]
        public void Ausgeruht_Rate_Deckel_Verbrauch()
        {
            int need = Progression.XpToNext(10);
            // 8 Stunden im Gasthaus = 5 % der Stufe; draussen ein Viertel
            Assert.AreEqual((int)(need * 0.05), Progression.AccrueRested(0, 10, 8.0, true));
            Assert.AreEqual((int)(need * 0.05 / 4), Progression.AccrueRested(0, 10, 8.0, false));
            // Deckel anderthalb Stufen
            Assert.AreEqual(Progression.RestedCap(10), Progression.AccrueRested(0, 10, 10000.0, true));
            Assert.AreEqual(UnityEngine.Mathf.RoundToInt(1.5f * need), Progression.RestedCap(10));
            Assert.AreEqual(0, Progression.RestedCap(Progression.GlobalMaxLevel));
            // Verbrauch: höchstens die Sieg-EP, höchstens die vorhandene Ruhe
            Assert.AreEqual(95, Progression.RestedBonus(95, 1000));
            Assert.AreEqual(40, Progression.RestedBonus(95, 40));
            Assert.AreEqual(0, Progression.RestedBonus(95, 0));
        }

        [Test]
        public void Dungeon_EP_wie_Forever()
        {
            Assert.AreEqual(1.1f, Balance.XpFactor(BalanceRole.DungeonTrash, false), 1e-4);
            Assert.AreEqual(6f, Balance.XpFactor(BalanceRole.DungeonBoss, false), 1e-4);
            Assert.AreEqual(2f, Balance.XpFactor(BalanceRole.Normal, true), 1e-4);
        }
    }
}
