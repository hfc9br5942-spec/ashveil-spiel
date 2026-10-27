using System;
using Ashveil.Data;

namespace Ashveil.Gameplay.Mounts
{
    /// <summary>
    /// Klassenreittier, wie der eigene Client es sieht (Besitzer): besitzt die Figur eines, sitzt sie gerade auf, wie heisst es.
    /// Leiste und Fenster lesen hier; <see cref="Toggle"/> schickt die Absicht an den Server (setzt der Netzwerk-Teil).
    /// </summary>
    public static class MountClient
    {
        public static string MountId { get; private set; } = "";
        public static string Name { get; private set; } = "";
        public static bool Mounted { get; private set; }
        public static bool HasMount => !string.IsNullOrEmpty(MountId);
        public static ClassMountDefinition Definition => HasMount ? MountCatalog.Get(MountId) : null;
        public static int Revision { get; private set; }
        public static event Action Changed;

        /// <summary>Auf-/Absitzen anfragen (vom Netzwerk-Teil gesetzt).</summary>
        public static Action ToggleRequest;

        public static void Toggle() => ToggleRequest?.Invoke();

        public static void Apply(string id, string name, bool mounted)
        {
            MountId = id ?? "";
            Name = string.IsNullOrEmpty(name) ? (Definition != null ? Definition.displayName : "") : name;
            Mounted = mounted;
            Revision++;
            Changed?.Invoke();
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            MountId = "";
            Name = "";
            Mounted = false;
            ToggleRequest = null;
            Changed = null;
        }
    }
}
