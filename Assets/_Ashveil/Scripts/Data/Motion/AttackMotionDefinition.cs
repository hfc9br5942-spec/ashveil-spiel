using UnityEngine;

namespace Ashveil.Data.Motion
{
    /// <summary>
    /// Kontaktdaten eines Angriffs, getrennt vom Clip (§31/§32): wohin er zielt, wie weit er reicht, wie viel
    /// Korrektur er darf und wie er an andere Angriffe anschliesst. Treffer und Zeitfenster bestimmt weiterhin die
    /// Waffe (Gameplay) – dieses Asset waehlt nur die passende sichtbare Bewegung.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Motion/Angriff", fileName = "Attack_")]
    public sealed class AttackMotionDefinition : ScriptableObject
    {
        public MotionClipMetadata motion;
        [Tooltip("Lesbarer Name fuer Debug-Anzeige und Berichte (z. B. „Diagonal rechts“).")]
        public string label;

        [Header("Ziel (§29/§30)")]
        public BodyZone zone = BodyZone.Mid;
        [Tooltip("Treffhoehe relativ zum Boden unter dem Angreifer, als Anteil seiner Koerperhoehe (x = min, y = max).")]
        public Vector2 heightRange = new Vector2(0.35f, 0.85f);
        [Tooltip("Zielgroessen, fuer die die Bewegung glaubwuerdig ist.")]
        public TargetSizeClass minTargetSize = TargetSizeClass.Small;
        public TargetSizeClass maxTargetSize = TargetSizeClass.Huge;
        [Tooltip("Bevorzugte Distanz (m) Kontaktpunkt ↔ Angreifer.")]
        public float preferredRange = 1.8f;
        public float minRange = 0.8f;
        public float maxRange = 2.6f;
        [Tooltip("Bogen des Schlags (Grad) – groesser braucht mehr Freiraum.")]
        public float attackArc = 90f;
        [Tooltip("Seite, auf der der Schlag Platz braucht (§69): -1 links, +1 rechts, 0 mittig (Stoss/vertikal).")]
        [Range(-1f, 1f)] public float swingSide;

        [Header("Korrektur (§31, §129 – kein Magnet)")]
        public float maxTranslationWarp = 0.35f;
        public float maxRotationWarp = 25f;
        [Tooltip("Vorwaertsschritt des Motors waehrend des Angriffs (m) – deterministisch wie die Hechtrolle.")]
        public float forwardStep = 0.15f;

        [Header("Fluss (§121/§122)")]
        public PoseTag startPose = PoseTag.GuardCenter;
        public PoseTag endPose = PoseTag.GuardCenter;
        [Tooltip("Grundgewicht in der Auswahl.")]
        public float baseWeight = 1f;
        [Tooltip("Kurz und kontrolliert (gut gegen schnelle Ziele, §71).")]
        public bool quick;
        [Tooltip("Ausladend/festgelegt (gut gegen grosse, ruhige Ziele).")]
        public bool committed;
        [Tooltip("Nur fuer Faehigkeiten/Spezialangriffe, nicht fuer den Automatikangriff.")]
        public bool abilityOnly;

        public uint NetId => motion != null ? motion.NetId : MotionClipMetadata.Hash32(name);
    }
}
