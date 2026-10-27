using UnityEngine;

namespace Ashveil.Data.Motion
{
    /// <summary>Gemeinsame Basis: jede Schicht traegt Stellschrauben und eine lesbare Beschreibung.</summary>
    public abstract class MotionLayerProfile : ScriptableObject
    {
        [TextArea(2, 4)] public string intent;
        public MotionModifiers modifiers = MotionModifiers.Neutral;
    }
}
