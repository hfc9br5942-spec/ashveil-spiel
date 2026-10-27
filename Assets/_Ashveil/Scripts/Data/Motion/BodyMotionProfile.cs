using UnityEngine;

namespace Ashveil.Data.Motion
{
    /// <summary>Koerper-Bewegungscharakter (§19) – Unterschiede vor allem in Schritt, Rhythmus, Armhaltung, Silhouette.</summary>
    [CreateAssetMenu(menuName = "Ashveil/Motion/Koerper", fileName = "MotionBody_")]
    public sealed class BodyMotionProfile : MotionLayerProfile
    {
        public BodyMotionVariant variant = BodyMotionVariant.Neutral;
    }
}
