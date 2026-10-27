using UnityEngine;

namespace Ashveil.Data.Motion
{
    /// <summary>
    /// Beschreibung eines Clips fuer die Auswahl (§80) – getrennt vom Clip selbst, damit Auswahl, Pruefwerkzeuge und
    /// Netz nie Clipnamen brauchen. Wird vom Editor (Ashveil › Motion › Datenbank aktualisieren) angelegt und gepflegt.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Motion/Clip-Metadaten", fileName = "Clip_")]
    public sealed class MotionClipMetadata : ScriptableObject
    {
        [Tooltip("Stabile Kennung (z. B. ss.attack.diag_r). Daraus entsteht die Netz-ID.")]
        public string motionId;
        public AnimationClip clip;
        [Tooltip("Herkunft, z. B. „Quaternius UAL2: Sword_Regular_A“ oder „Mixamo: sword and shield slash“.")]
        public string source;
        public MotionAssetStatus status = MotionAssetStatus.Placeholder;
        [TextArea(1, 3)] public string notes;

        [Header("Einordnung")]
        public MotionCategory category = MotionCategory.Locomotion;
        public MotionTag tags;
        public WeaponFamily weaponFamily = WeaponFamily.None;
        public MotionStance stance = MotionStance.Exploration;
        public MotionDirection direction = MotionDirection.Any;
        public MotionPriority priority = MotionPriority.Idle;

        [Header("Tempo und Wiedergabe")]
        [Tooltip("Tempo (m/s), fuer das der Clip gedacht ist (Fortbewegung).")]
        public Vector2 speedRange;
        public bool loop;
        [Tooltip("Spiegeln erlaubt? Schwert+Schild nie (Waffenhaende, §105).")]
        public bool mirrorAllowed;
        [Tooltip("Grenzen fuer Zeitstreckung (§125) – ausserhalb braucht es einen anderen Clip.")]
        public float minPlaybackSpeed = 0.8f;
        public float maxPlaybackSpeed = 1.25f;
        public RootMotionPolicy rootMotion = RootMotionPolicy.InPlace;

        [Header("Kontakt und Uebergaenge")]
        [Range(0f, 1f)] public float contactNormalizedTime = -1f;
        public PoseTag startPose = PoseTag.Any;
        public PoseTag endPose = PoseTag.Any;
        [Tooltip("Drehwinkel (Turn-in-Place), 0 wenn keiner.")]
        public float turnAngle;
        [Tooltip("Benoetigter seitlicher Freiraum (m).")]
        public float requiredClearance;
        [Tooltip("Ab diesem normierten Zeitpunkt darf die Animation abgebrochen werden (§120 – Gameplay entscheidet).")]
        [Range(0f, 1f)] public float cancelableFrom = 1f;
        [Tooltip("Schlagworte, die als Folgebewegung bevorzugt werden (§121).")]
        public MotionTag preferredNextTags;

        [Header("Fusskontakt (Pruefwerkzeuge)")]
        [Tooltip("Normierte Zeitfenster, in denen der linke/rechte Fuss steht (x = von, y = bis).")]
        public Vector2[] leftFootPlanted;
        public Vector2[] rightFootPlanted;

        /// <summary>Stabile Netz-ID (32 Bit – 16 Bit kollidierten schon bei 60 Clips) aus der Kennung (gleich auf allen Rechnern, FNV-1a).</summary>
        public uint NetId => Hash32(motionId);

        public float Length => clip != null ? clip.length : 0f;

        public static uint Hash32(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            uint h = 2166136261;
            for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619; }
            return h == 0 ? 1u : h;
        }
    }
}
