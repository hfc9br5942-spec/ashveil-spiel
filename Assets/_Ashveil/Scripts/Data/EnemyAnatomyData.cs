using System;
using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>Wann ein Koerperteil ueberhaupt getroffen werden kann (Boss-Anatomie §3/§4).</summary>
    public enum BodyPartReach : byte
    {
        /// <summary>Immer (Arme, Beine).</summary>
        Always,
        /// <summary>Nur wenn der Gegner kniet oder sich zum Schlag herabbeugt (Kopf des Grubenschrats) – fuer Nah- und Fernkampf gleich.</summary>
        WhenLowered
    }

    /// <summary>Zustand eines Koerperteils (Boss-Anatomie §6).</summary>
    public enum BodyPartState : byte
    {
        Intact,
        Damaged,
        Broken,
        Destroyed
    }

    /// <summary>Gesamtzustand eines Gegners fuer die Darstellung (Masterplan: Healthy / Wounded / Critical).</summary>
    public enum EnemyDamageState : byte
    {
        Healthy,
        Wounded,
        Critical
    }

    /// <summary>
    /// Ein angreifbarer Koerperteil (Boss-Anatomie §1–§9, Masterplan §74). Schaden auf das Teil zaehlt immer auch
    /// fuers Hauptleben; das Teil hat zusaetzlich eigenes Leben, das ueber Zustand und Bruch entscheidet.
    /// </summary>
    [Serializable]
    public sealed class BodyPartDefinition
    {
        [Tooltip("1..255, 0 = Koerper allgemein")]
        public byte id = 1;
        [Tooltip("Technischer Name (head, arm_r, leg_l)")]
        public string key = "";
        [Tooltip("Anzeige im Zielrahmen, z. B. \"Rechter Arm\"")]
        public string displayName = "";
        [Tooltip("Name des Gelenk-Objekts im Modell (z. B. \"Head\", \"Arm R\", \"Leg L\")")]
        public string pivot = "";
        [Tooltip("Trefferzone relativ zum Gelenk (Mitte, Groesse; Modellmassstab)")]
        public Vector3 hitboxCenter;
        public Vector3 hitboxSize = Vector3.one;

        [Header("Werte")]
        public BodyPartReach reach = BodyPartReach.Always;
        [Tooltip("Leben des Teils als Anteil am Hauptleben")]
        [Range(0.02f, 1f)] public float healthShare = 0.2f;
        [Tooltip("Schadensfaktor fuer Treffer auf dieses Teil (Kopf 1,5)")]
        [Min(0f)] public float damageMultiplier = 1f;
        [Tooltip("Beschaedigt ab diesem Anteil verbleibenden Teil-Lebens")]
        [Range(0f, 1f)] public float damagedAt = 0.5f;
        [Tooltip("> 0: nach dem Bruch zerstoert, wenn noch dieser Anteil weiterer Schaden kommt (Unterarm faellt ab)")]
        [Min(0f)] public float destroyOverkill;

        [Header("Folgen des Bruchs")]
        [Tooltip("> 0: einmaliger Niederwurf fuer so viele Sekunden (Kopf dann erreichbar)")]
        [Min(0f)] public float knockdownSeconds;
        [Tooltip("> 0: kurzes Taumeln (Sekunden)")]
        [Min(0f)] public float staggerSeconds;
        [Tooltip("Tempo danach (1 = unveraendert)")]
        [Range(0.2f, 1f)] public float speedAfterBreak = 1f;
        [Tooltip("Zusaetzliche Beute fuer alle Berechtigten, wenn das Teil gebrochen ist (Item-Id, leer = keine)")]
        public string bonusLootItem = "";
        [Min(1)] public int bonusLootCount = 1;
    }

    /// <summary>Wie die Darstellung einen Angriff zeigt (Platzhalter-Koerper: welche Glieder sich bewegen).</summary>
    public enum EnemyMovePose : byte
    {
        Default,
        RightArm,
        LeftArm,
        BothArms,
        Shoulder,
        Stomp,
        Charge
    }

    public enum EnemyMoveKind : byte
    {
        /// <summary>Schlag auf das Ziel (Reichweite, Winkel).</summary>
        Strike,
        /// <summary>Flaechenangriff um den Gegner (Warnring).</summary>
        Area,
        /// <summary>Ansturm aus mittlerer Entfernung bis vor das Ziel.</summary>
        Charge
    }

    /// <summary>
    /// Ein Angriff im Moveset (Masterplan §102–§104, §75/§76): gewaehlt nach Entfernung, Abklingzeit und Gewicht,
    /// mit Abschlag fuer kuerzlich benutzte Angriffe. Ein gebrochenes Teil kann Angriffe sperren oder Ersatz freischalten.
    /// </summary>
    [Serializable]
    public sealed class EnemyAttackDefinition
    {
        [Tooltip("1..255 (geht als Byte an die Clients fuer die passende Bewegung)")]
        public byte id = 1;
        public string displayName = "";
        public EnemyMoveKind kind = EnemyMoveKind.Strike;
        public EnemyMovePose pose = EnemyMovePose.Default;
        [Min(0f)] public float minRange;
        [Min(0.5f)] public float maxRange = 3f;
        [Min(0.05f)] public float windup = 1f;
        [Min(0f)] public float damageMultiplier = 1f;
        [Min(0f)] public float cooldown = 4f;
        [Tooltip("0 = nur als Folgeangriff")]
        [Min(0f)] public float weight = 1f;
        [Range(10f, 360f)] public float arc = 130f;
        [Min(0f)] public float knockback = 0.3f;
        [Tooltip("Flaechenangriff: Radius (m)")]
        [Min(0f)] public float areaRadius;
        [Tooltip("Folgeangriff direkt danach (Id, 0 = keiner) und seine Ausholzeit")]
        public byte followUp;
        [Min(0.05f)] public float followUpWindup = 0.5f;
        [Tooltip("Nur solange dieses Teil nicht gebrochen ist (Id, 0 = egal)")]
        public byte requiresIntactPart;
        [Tooltip("Nur wenn dieses Teil gebrochen ist (Ersatzangriff, Id, 0 = egal)")]
        public byte onlyWhenPartBroken;
        [Tooltip("Beugt sich dabei herab: Kopf danach kurz erreichbar (s, 0 = nein)")]
        [Min(0f)] public float lowersHeadFor;
        [Tooltip("Tritt danach so viele Meter zurueck (Abstand, Masterplan §10)")]
        [Min(0f)] public float retreatAfter;
    }
}
