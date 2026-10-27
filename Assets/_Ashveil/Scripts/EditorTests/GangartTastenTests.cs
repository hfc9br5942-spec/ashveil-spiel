using Ashveil.Core.Settings;
using NUnit.Framework;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// Gangart-Tasten (Max 29.09.2026: Gehen, Joggen und Epischer Gang je eine eigene, umbelegbare Umschalttaste;
    /// nachgezogen 15.10.2026 – vorher fest auf dem Ziffernblock). Ohne Szene, ohne Play.
    /// </summary>
    public sealed class GangartTastenTests
    {
        [Test]
        public void Standard_liegt_auf_dem_Ziffernblock()
        {
            var b = new InputBindings();
            b.Clamp();
            Assert.AreEqual("Key:NumpadDivide", b.walkKey);
            Assert.AreEqual("Key:NumpadMultiply", b.jogKey);
            Assert.AreEqual("Key:NumpadMinus", b.epicWalkKey);
        }

        [Test]
        public void Alte_Datei_ohne_Gangarten_bekommt_den_Standard()
        {
            var b = UnityEngine.JsonUtility.FromJson<InputBindings>("{\"version\":1,\"swapKey\":\"Key:LeftCtrl\"}");
            b.Clamp();
            Assert.AreEqual(InputBindings.DefaultJog, b.jogKey);
        }

        [Test]
        public void Umbelegen_entfernt_die_Taste_vom_alten_Platz()
        {
            var b = new InputBindings();
            b.Clamp();
            int vorher = b.BindGait(103, "Key:Digit1");
            Assert.AreEqual(-1, vorher, "Taste lag vorher auf Platz 1");
            Assert.AreEqual("Key:Digit1", b.jogKey);
            Assert.AreEqual("", b.slots[0]);
            vorher = b.BindGait(102, "Key:Digit1");
            Assert.AreEqual(-103, vorher);
            Assert.AreEqual("Key:Digit1", b.walkKey);
            Assert.AreEqual("", b.jogKey);
        }
    }
}
