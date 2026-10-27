using UnityEngine;

namespace Ashveil.UI
{
    /// <summary>
    /// Belegter Platz im HUD, damit Elemente aus verschiedenen Baugruppen sich nicht überdecken (08.10.2026, Karten r2):
    /// <see cref="ObenRechts"/> = Höhe in Referenz-Pixeln (1920 × 1080), die oben rechts belegt ist – heute von der Minimap
    /// (Ashveil.Maps). Die Auftragsliste (QuestUi) beginnt darunter.
    /// </summary>
    public static class HudPlatz
    {
        public static float ObenRechts;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Zuruecksetzen() => ObenRechts = 0f;   // Play ohne Neuladen der Skripte
    }
}
