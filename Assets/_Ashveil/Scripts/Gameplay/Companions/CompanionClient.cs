using System;
using Ashveil.Gameplay.Combat;
using UnityEngine;

namespace Ashveil.Gameplay.Companions
{
    /// <summary>Befehle an den Gefaehrten (§64): Folgen, Bleiben, Angreifen, Passiv. Folgen ist zugleich „verteidigen“.</summary>
    public enum CompanionCommand : byte { Follow = 0, Stay = 1, Attack = 2, Passive = 3 }

    /// <summary>
    /// Der eigene Gefaehrte, wie der Client ihn sieht (nur fuer den Besitzer): Leiste, Gefaehrten-Rahmen, Fenster „Erfahrung“.
    /// Der Netzwerk-Layer setzt Zustand und Anfragewege (Gameplay kennt das Netz nicht).
    /// </summary>
    public static class CompanionClient
    {
        /// <summary>Dominieren erlernt (Klassen-Signatur)?</summary>
        public static bool CanDominate { get; private set; }
        /// <summary>Besitzt einen Gefaehrten (gerufen oder nicht).</summary>
        public static bool HasCompanion => !string.IsNullOrEmpty(Species);
        public static string Species { get; private set; } = "";
        public static string Name { get; private set; } = "";
        public static bool Out { get; private set; }
        public static bool Downed { get; private set; }
        public static CompanionCommand Command { get; private set; }
        public static int Level { get; private set; }
        /// <summary>Leben des gerufenen Gefaehrten (Spiegel des Servers), sonst null.</summary>
        public static Health PetHealth { get; private set; }
        public static int Revision { get; private set; }
        public static event Action Changed;

        /// <summary>Leisten-Eintrag ausloesen (companion.call / companion.dominate).</summary>
        public static Action<string> TriggerRequest;
        /// <summary>Befehl senden (mit dem aktuellen Ziel des Spielers fuer „Angreifen“).</summary>
        public static Action<CompanionCommand> CommandRequest;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            CanDominate = false;
            Species = Name = "";
            Out = Downed = false;
            Command = CompanionCommand.Follow;
            Level = 0;
            PetHealth = null;
            Revision = 0;
            Changed = null;
            TriggerRequest = null;
            CommandRequest = null;
        }

        public static void Trigger(string id) => TriggerRequest?.Invoke(id);
        public static void SendCommand(CompanionCommand c) => CommandRequest?.Invoke(c);

        public static void Apply(bool canDominate, string species, string name, bool isOut, bool downed, CompanionCommand command, int level)
        {
            CanDominate = canDominate;
            Species = species ?? "";
            Name = name ?? "";
            Out = isOut;
            Downed = downed;
            Command = command;
            Level = level;
            Revision++;
            Changed?.Invoke();
        }

        public static void SetPet(Health pet)
        {
            PetHealth = pet;
            Revision++;
            Changed?.Invoke();
        }
    }
}
