using UnityEngine;

namespace Ashveil.Data.Motion
{
    /// <summary>
    /// Buendelt die Standard-Schichten einer spielbaren Kombination (z. B. „Mensch · Tank · schwer · Schwert+Schild“).
    /// Zur Laufzeit waehlt <see cref="MotionDatabase.Resolve"/> die Schichten aus dem Kontext; dieses Asset ist der
    /// dokumentierte Referenzfall und Rueckfall.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Motion/Identitaet", fileName = "MotionIdentity_")]
    public sealed class MotionIdentityProfile : ScriptableObject
    {
        [TextArea(2, 4)] public string intent;
        public BodyMotionProfile body;
        public RaceMotionProfile race;
        public ClassMotionProfile classMotion;
        public ArmorMotionProfile armor;
        public WeaponMotionProfile weapon;
    }
}
