using System;
using UnityEngine;

namespace Ashveil.Core
{
    /// <summary>
    /// Kleine Signale einer Boss-Begegnung an die Umgebung (Flut steigt, Tor oeffnet sich …). Der Server loest sie
    /// bei sich selbst aus und schickt sie an die Spieler der Instanz; auf jedem Rechner reagieren Szenenobjekte
    /// (z. B. Flutwasser, Lorenbahn-Tor), die im selben Instanz-Platz liegen wie der Absendeort.
    /// Liegt in Core, damit Gameplay (Begegnungen) und World (Szenenobjekte) sich nicht kennen muessen.
    /// </summary>
    public static class EncounterCues
    {
        /// <summary>Alles in diesem Instanz-Platz auf Anfang (neue Belegung, Betreten).</summary>
        public const string Reset = "reset";
        /// <summary>Flut kuendigt sich an – Wert: Warnzeit (s).</summary>
        public const string FloodWarn = "flood-warn";
        /// <summary>Flut steht – Wert: Dauer (s).</summary>
        public const string Flood = "flood";
        /// <summary>Flut vorbei (vorzeitig, z. B. Abbruch).</summary>
        public const string FloodEnd = "flood-end";
        /// <summary>Lorenbahn-Tor – Wert 1 = offen, 0 = zu.</summary>
        public const string LoreGate = "lore-gate";

        /// <summary>Grubenlampe Nummer i: Signal „lamp0“ … „lamp3“ – Wert 1 = brennt, 0 = erloschen.</summary>
        public static string Lamp(int index) => "lamp" + index;

        /// <summary>Ort (irgendwo im Instanz-Platz), Signal, Wert.</summary>
        public static event Action<Vector3, string, float> Received;

        public static void Raise(Vector3 where, string cue, float value) => Received?.Invoke(where, cue, value);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Received = null;
    }
}
