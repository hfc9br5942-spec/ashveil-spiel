using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Kolosseum Pass 3: austauschbarer Ablauf einer Instanz-Art am selben Objekt wie der <see cref="DungeonDirector"/>.
    /// Ohne Handler verhaelt sich der Direktor wie bisher (Salzgruben: Gegner aus den Spawngebieten beim Betreten).
    /// Das Kolosseum haengt hier den Wellen-Ablauf ein. Nur Server.
    /// </summary>
    public abstract class DungeonInstanceHandler : MonoBehaviour
    {
        /// <summary>Platz wurde fuer eine Gruppe belegt (Spawngebiete sind schon erzeugt).</summary>
        public virtual void OnActivate(DungeonDirector director, DungeonDirector.Instance instance) { }

        /// <summary>Jemand hat die Instanz betreten (fresh = er hat sie gerade belegt).</summary>
        public virtual void OnEnter(DungeonDirector director, DungeonDirector.Instance instance, NetworkInventory player, bool fresh) { }

        /// <summary>Jemand verlaesst die Instanz ueber den Ausgang (vor dem Teleport).</summary>
        public virtual void OnLeave(DungeonDirector director, DungeonDirector.Instance instance, NetworkInventory player) { }

        /// <summary>Takt des Direktors (4 Hz) fuer jede belegte Instanz, nachdem die Anwesenheit ermittelt wurde.</summary>
        public virtual void OnTick(DungeonDirector director, DungeonDirector.Instance instance, float now) { }

        /// <summary>Vor dem Zuruecksetzen (Gegner und Platz werden danach abgeraeumt).</summary>
        public virtual void OnReset(DungeonDirector director, DungeonDirector.Instance instance) { }
    }
}
