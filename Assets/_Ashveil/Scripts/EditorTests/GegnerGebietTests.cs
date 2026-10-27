using Ashveil.Gameplay.Enemies;
using NUnit.Framework;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// Gegner geben ein Ziel weit außerhalb ihres Gebiets auf (Max 09.10.2026: „Ja, einbauen“). Fund aus der Reittier-Suite: Ein
    /// Rußluchs hielt Krisbot nach dem Sprung in den Ring (25 km entfernt) „im Kampf“. Grenze: Leine × 1,5 + 20 m.
    /// </summary>
    public sealed class GegnerGebietTests
    {
        [Test]
        public void Ziel_weit_ausserhalb_wird_aufgegeben()
        {
            Assert.IsTrue(EnemyBrain.ZielAusserhalbGebiet(24982f, 38f), "Sprung in den Ring (25 km)");
            Assert.IsTrue(EnemyBrain.ZielAusserhalbGebiet(77.1f, 38f), "Standard-Leine 38 m: ab 77 m");
            Assert.IsTrue(EnemyBrain.ZielAusserhalbGebiet(28f, 5f), "kleinste Leine 5 m: ab 27,5 m");
        }

        [Test]
        public void Weglaufen_und_Fernkampf_bleiben_unberuehrt()
        {
            Assert.IsFalse(EnemyBrain.ZielAusserhalbGebiet(76.9f, 38f), "knapp innerhalb");
            Assert.IsFalse(EnemyBrain.ZielAusserhalbGebiet(38f + 30f, 38f), "Schütze am Rand seiner Leine, Ziel 30 m weiter");
            Assert.IsFalse(EnemyBrain.ZielAusserhalbGebiet(26f + 12f, 26f), "Grubenschrat: Ziel am Rand des Wahrnehmungskreises jenseits der Leine");
            Assert.IsFalse(EnemyBrain.ZielAusserhalbGebiet(0f, 5f), "Ziel daheim");
        }
    }
}
