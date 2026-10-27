using UnityEngine;

namespace Ashveil.World
{
    /// <summary>
    /// Fester Modell-Platz in der Welt. Merkt sich, welches Modell (Slot-Name, z. B. "SM_Pine_A") hier steht und welche
    /// Kollision es bekommt. Damit lassen sich Modelle spaeter gezielt austauschen, ohne die Szene neu zu bauen
    /// (Ashveil &gt; Grafik &gt; Modelle austauschen). Reine Markierung – keine Laufzeitlogik.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArtSlot : MonoBehaviour
    {
        [SerializeField] private string slotName = "";
        [SerializeField] private int colliderKind;
        [SerializeField] private string sourcePath = "";

        public string SlotName => slotName;
        public int ColliderKind => colliderKind;
        public string SourcePath => sourcePath;

        public void Configure(string slot, int collider, string source)
        {
            slotName = slot;
            colliderKind = collider;
            sourcePath = source;
        }
    }
}
