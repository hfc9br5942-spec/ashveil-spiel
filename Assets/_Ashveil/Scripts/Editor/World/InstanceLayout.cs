using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Kolosseum Pass 2 / Large World: alle Instanz-Plaetze an EINER Stelle. Jede Instanz-Art hat eine Reihe
    /// (Ursprung + Abstand entlang x). Die Plaetze liegen in der Welt-Ebene (Sandbox_M1), weit ausserhalb jeder Weltzelle,
    /// und bleiben auf dem Server dauerhaft geladen – wie Kareth.
    ///
    /// Belegte Welt (Stand 28.09.): Kareth x −600…600, Grenzgebirge x 600…3000 (z −350…2050). Die Reihen beginnen bei
    /// x = 4000. Bevor die Bruchmark (oestlich x 3000) gebaut wird, verschiebt M9 beide Reihen mit <see cref="Origin"/> an
    /// einen reservierten Ort – sonst nichts anfassen: Builder, Gegner und Tests lesen nur von hier.
    /// </summary>
    public static class InstanceLayout
    {
        /// <summary>Gemeinsamer Ursprung aller Instanz-Reihen.</summary>
        /// 30.09.: nach Max' Weltkarte liegt bei x = 4000 das Eschenfeld – Instanzen an einen reservierten Ort weit
        /// westlich ausserhalb des Kontinents (hinter dem Azurmeer, ausserhalb jeder Weltzelle und der Fernansicht).
        public static readonly Vector3 Origin = new Vector3(-26000f, 0f, 0f);

        /// <summary>Abstand der Plaetze einer Reihe. Sichtbarkeit trennt Plaetze per Instanz-Regel, nicht per Abstand.</summary>
        public const float Spacing = 300f;

        public const int SaltMineRow = 0;
        public const int ColosseumRow = 1;

        /// <summary>z-Versatz je Reihe (Salzgruben-Plaetze reichen bis z ≈ 210, eine Arena ist ~130 m breit).</summary>
        private static readonly float[] RowZ = { 0f, 1200f };

        public static Vector3 Slot(int row, int index) => Origin + new Vector3(index * Spacing, 0f, RowZ[row]);
    }
}
