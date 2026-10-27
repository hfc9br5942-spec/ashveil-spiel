using Ashveil.Data;
using NUnit.Framework;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// #30 (08.10.2026): wählbare Völker. Im Editor (ohne das Kennzeichen ASHVEIL_NUR_MENSCH des Spieltest-Builds r2) sind alle
    /// Völker wählbar; ungültige Indizes nie. Den Build „nur Mensch“ prüft der Kurztest (Auftrag spieltest_kurztest).
    /// </summary>
    public sealed class RaceCatalogTests
    {
        [Test]
        public void Playable_InEditor_AllRaces()
        {
            Assert.AreEqual(RaceCatalog.All.Length, RaceCatalog.Playable.Length, "im Editor alle Völker wählbar");
            for (int i = 0; i < RaceCatalog.All.Length; i++) Assert.IsTrue(RaceCatalog.IsPlayable(i), RaceCatalog.All[i].id);
            Assert.IsFalse(RaceCatalog.IsPlayable(-1));
            Assert.IsFalse(RaceCatalog.IsPlayable(RaceCatalog.All.Length));
            StringAssert.Contains(RaceCatalog.HumanId, RaceCatalog.PlayableIds());
            Assert.AreEqual(0, RaceCatalog.IndexOf(RaceCatalog.HumanId), "Mensch ist Index 0 (Rückfall im Server)");
        }
    }
}
