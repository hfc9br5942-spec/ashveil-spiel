using UnityEngine;

namespace Ashveil.Gameplay.Enemies
{
    /// <summary>Trefferzone eines Koerperteils (Trigger). Mausklick waehlt Gegner und Teil.</summary>
    public sealed class BodyPartHitbox : MonoBehaviour
    {
        public EnemyAnatomy Anatomy { get; private set; }
        public byte PartId { get; private set; }

        public void Init(EnemyAnatomy anatomy, byte partId)
        {
            Anatomy = anatomy;
            PartId = partId;
        }
    }
}
