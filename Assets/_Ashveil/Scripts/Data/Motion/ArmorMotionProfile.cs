using UnityEngine;

namespace Ashveil.Data.Motion
{
    /// <summary>Wirkung der Ruestungsklasse (§21) – getrennt von Spielwerten.</summary>
    [CreateAssetMenu(menuName = "Ashveil/Motion/Ruestung", fileName = "MotionArmor_")]
    public sealed class ArmorMotionProfile : MotionLayerProfile
    {
        public ArmorCategory category = ArmorCategory.Light;
    }
}
