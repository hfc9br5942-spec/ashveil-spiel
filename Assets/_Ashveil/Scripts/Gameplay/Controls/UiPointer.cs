namespace Ashveil.Gameplay.Controls
{
    /// <summary>
    /// Bruecke zwischen Oberflaeche und Spielsteuerung: Liegt der Mauszeiger ueber einem Fenster, gehoeren
    /// Klicks der Oberflaeche (kein Zielen, kein Kamera-Drehen). Tastatureingaben bleiben beim Spiel,
    /// ausser <see cref="KeyboardCaptured"/> ist gesetzt.
    /// </summary>
    public static class UiPointer
    {
        /// <summary>Mauszeiger liegt ueber einem offenen Fenster (von der UI jedes Frame gesetzt).</summary>
        public static bool OverUi;
        /// <summary>Die UI zieht gerade etwas (Drag &amp; Drop).</summary>
        public static bool Dragging;
        /// <summary>Ein Eingabefeld hat den Fokus – Spieltasten ruhen, Esc gehoert dem Feld.</summary>
        public static bool KeyboardCaptured;
        /// <summary>Vollbild-Oberflaeche (Charakter-Editor): die Spielfigur ruht ganz – keine Bewegung, kein Kampf, keine Kamera.</summary>
        public static bool GameBlocked;
        /// <summary>
        /// Ein modales Menue ist offen (Spielmenue, UI-204): Fenster-Tasten und Chat ruhen. Anders als bei
        /// <see cref="KeyboardCaptured"/> gehoert Esc weiter dem Fensterstapel (<see cref="UiFensterstapel"/>).
        /// </summary>
        public static bool ModalOpen;

        /// <summary>Duerfen Fenster-Tasten (I, C, K, L, M, N, O, J …) und Enter (Chat) gerade wirken? Nicht beim Tippen, nicht unter einem modalen Menue.</summary>
        public static bool HotkeysAllowed => !KeyboardCaptured && !ModalOpen;

        /// <summary>Play Mode ohne Neuladen der Skripte: kein Fenster und kein Menue aus der letzten Sitzung haelt die Steuerung fest.</summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OverUi = Dragging = KeyboardCaptured = GameBlocked = ModalOpen = false;
        }
    }
}
