using Ashveil.Gameplay.Controls;
using UnityEngine;

namespace Ashveil.Maps
{
    /// <summary>
    /// Tasten und Maus für die Karten, alles über <see cref="RawInput"/> wie die übrige Oberfläche. M = Karte, Esc = schließen
    /// (seit UI-204 über den Fensterstapel <see cref="UiFensterstapel"/>: erst das oberste Fenster, dann das Ziel, dann das Spielmenü),
    /// Mausrad oder die Knöpfe +/− an der Minimap = Zoom, Rechtsklick in der Gebietskarte = Weltkarte.
    /// Keine Zoomtasten: Minus/Gleich liegen auf der Actionbar (Plätze 11/12), Num − auf dem Epischen Gang.
    /// Beim späteren Rebinding wandert M in die Tastenbelegung.
    /// </summary>
    internal static class KartenEingabe
    {
        /// <summary>Code wie die frei belegbaren Tasten (Input System).</summary>
        public const string KarteTaste = "Key:M";

        public static bool KarteGedrueckt() => RawInput.WasPressed(KarteTaste);
        public static bool EscapeGedrueckt() => RawInput.EscapePressed;
        public static bool LinksklickGedrueckt() => RawInput.LeftMousePressed;
        public static bool RechtsklickGedrueckt() => RawInput.RightMousePressed;
        public static Vector2 Mausposition() => RawInput.MousePosition;
        public static float Mausrad() => RawInput.ScrollSteps;

        /// <summary>Tippt man gerade (Chat) oder ist das Spielmenü offen (UI-204), haben die Kartentasten Pause.</summary>
        public static bool TextfeldAktiv() => !UiPointer.HotkeysAllowed;

        /// <summary>
        /// Vollbild-Oberfläche (Charakter-Editor, Fotomodus): keine Karten. Unter dem Spielmenü (UI-204) bleiben Minimap und
        /// Banner stehen – der Rest der Oberfläche soll dahinter nicht umspringen.
        /// </summary>
        public static bool Gesperrt() => UiPointer.GameBlocked && !UiPointer.ModalOpen;

        /// <summary>Spielmenü offen (UI-204): keine Klicks und kein Mausrad auf der Minimap.</summary>
        public static bool ModalOffen() => UiPointer.ModalOpen;
    }
}
