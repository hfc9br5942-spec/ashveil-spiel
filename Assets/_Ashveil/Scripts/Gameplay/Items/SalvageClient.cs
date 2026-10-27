using System;
using UnityEngine;

namespace Ashveil.Gameplay.Items
{
    /// <summary>
    /// Nachlese des Heilers, wie der eigene Client sie sieht: freigeschaltet?, Statistik fuers Fenster „Erfahrung“ und
    /// der Weg zur Bergungs-Anfrage (vom Netzwerk-Layer gesetzt – Gameplay kennt das Netz nicht).
    /// </summary>
    public static class SalvageClient
    {
        public static bool Unlocked { get; private set; }
        public static int Salvages { get; private set; }
        public static int RareSalvages { get; private set; }
        public static int Revision { get; private set; }
        /// <summary>Letzte Bergung (Selbsttest/Anzeige), z. B. „item.salt_marrow x1“.</summary>
        public static string LastSalvage { get; private set; } = "";

        /// <summary>Anfrage „diese Leiche bergen“ (setzt die eigene Spielfigur).</summary>
        public static Action<GameObject> Request;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Unlocked = false;
            Salvages = RareSalvages = 0;
            Revision = 0;
            LastSalvage = "";
            Request = null;
        }

        public static void Apply(bool unlocked, int salvages, int rare)
        {
            Unlocked = unlocked;
            Salvages = salvages;
            RareSalvages = rare;
            Revision++;
        }

        public static void RaiseSalvaged(string what)
        {
            LastSalvage = what ?? "";
            Revision++;
        }
    }
}
