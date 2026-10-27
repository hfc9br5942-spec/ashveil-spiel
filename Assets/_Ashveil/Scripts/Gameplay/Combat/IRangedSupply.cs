using Ashveil.Data;

namespace Ashveil.Gameplay.Combat
{
    /// <summary>
    /// Munition und Koecher eines Schuetzen (WoW-Prinzip): Pfeile liegen in Koecher/Taschen, der gewaehlte Pfeiltyp
    /// wird verbraucht, der Koecher beschleunigt den Fernkampf. Ohne diese Komponente (Labor, Gegner) gilt: unbegrenzt.
    /// </summary>
    public interface IRangedSupply
    {
        /// <summary>Vorrat des gewaehlten Geschosses fuer diese Munitionsart (0 = leer).</summary>
        int AmmoCount(AmmoType type);
        /// <summary>Schaden pro Sekunde, den die gewaehlte Munition zusaetzlich bringt (wird mit dem Waffentempo multipliziert).</summary>
        float AmmoDps(AmmoType type);
        /// <summary>Fernkampftempo-Bonus des Koechers (0,15 = 15 % schneller).</summary>
        float RangedHaste { get; }
    }
}
