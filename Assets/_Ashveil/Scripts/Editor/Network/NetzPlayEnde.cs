using System.Collections.Generic;
using Ashveil.Networking;
using FishNet.Managing;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// 22.10.2026 (Port-7770-Leck): Beim Verlassen des Play-Modus Client, Server und Transport jedes noch laufenden
    /// NetworkManagers ausdruecklich beenden – bevor Unity die Szene abbaut. Bisher hing das allein an Tugboat.OnDestroy.
    /// Idempotent und ohne Fehler, wenn nichts laeuft (<see cref="NetzTransport.Stoppen"/>).
    /// Hauptursache des Lecks war die Handle-Vererbung an Kindprozesse; die behebt <see cref="NetzSocketWaechter"/>. Dieser
    /// Haken sorgt zusaetzlich dafuer, dass der Socket des Editors am Play-Ende sicher geschlossen ist (auch bei „Starting“ oder
    /// wenn ein OnDestroy-Pfad ausfaellt). Das Projekt laeuft ohne Domain-Reload, darum Abo vorher abmelden.
    /// </summary>
    [InitializeOnLoad]
    public static class NetzPlayEnde
    {
        static NetzPlayEnde()
        {
            EditorApplication.playModeStateChanged -= Wechsel;
            EditorApplication.playModeStateChanged += Wechsel;
        }

        private static void Wechsel(PlayModeStateChange zustand)
        {
            if (zustand != PlayModeStateChange.ExitingPlayMode) return;
            var gesehen = new HashSet<NetworkManager>();
            var liste = new List<NetworkManager>(NetworkManager.Instances);
            // Auch Manager ausserhalb der FishNet-Liste (z. B. noch nicht initialisiert), nur Szenenobjekte – keine Assets
            foreach (NetworkManager m in Resources.FindObjectsOfTypeAll<NetworkManager>())
                if (m != null && !EditorUtility.IsPersistent(m)) liste.Add(m);
            foreach (NetworkManager m in liste)
            {
                if (m == null || !gesehen.Add(m)) continue;
                string vorher = NetzTransport.Stoppen(m);
                if (vorher != null) Debug.Log($"[Ashveil] Play-Ende: Netz von „{m.name}“ beendet (vorher {vorher}).");
            }
        }
    }
}
