using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>Koerperbau – bestimmt Modell und Animation.</summary>
    public enum EnemyBody
    {
        /// <summary>Vierbeiner (Aschwolf) – prozedural animierter Platzhalter.</summary>
        Beast,
        /// <summary>Menschliche Gestalt (Verblichene) – Humanoid mit Animationsbibliothek.</summary>
        Humanoid,
        /// <summary>Schwerer Zweibeiner (Torfschrat, Glockenhirte) – prozedural animierter Koloss.</summary>
        Colossus
    }

    /// <summary>
    /// Aggro-Profil (Kanon 26.09.): jeder Gegner entscheidet einzeln – es gibt KEINE Social-Aggro
    /// (kein Rufen, kein Assist-Radius, keine Ketten-Aggro). Gruppen-Aggro nur als ausdrueckliche Encounter-Mechanik.
    /// </summary>
    public enum AggroProfile
    {
        /// <summary>Greift nie selbst an. Wird er angegriffen, wehrt er sich wie in Classic (neutrales Tier); nur Beutetiere
        /// (<see cref="EnemyDefinition.fleesWhenAttacked"/>) weichen stattdessen aus (Max 03.10.2026).</summary>
        Passive,
        /// <summary>Greift nur an, wenn genau dieser Gegner zuerst angegriffen wird.</summary>
        Defensive,
        /// <summary>Greift an, wenn jemand sein kleines Territorium betritt (territoryRadius).</summary>
        Territorial,
        /// <summary>Greift Spieler in seiner Wahrnehmung an (aggroRadius).</summary>
        Aggressive,
        /// <summary>Boss-/Ereignisregeln; bis eigene Begegnungsregeln existieren wie Aggressive.</summary>
        Encounter
    }

    /// <summary>Kampfweise.</summary>
    public enum EnemyStyle
    {
        Melee,
        /// <summary>Haelt Abstand und schiesst (Radschlosspistole).</summary>
        Ranged
    }

    /// <summary>
    /// Daten eines Gegnertyps. Verhalten im Kampf kommt aus diesen Werten plus der schlanken
    /// Server-Zustandsmaschine (Ruhe/Streifen -> Wahrnehmung -> Kampf -> Leine/Rueckkehr -> Tod).
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Gegner", fileName = "Enemy_")]
    public sealed class EnemyDefinition : GameDefinition
    {
        protected override string IdPrefix => "enemy";

        [Header("Art")]
        public EnemyBody body = EnemyBody.Beast;
        public EnemyStyle style = EnemyStyle.Melee;
        [Tooltip("Humanoid: weibliches statt maennliches Modell")]
        public bool femaleModel;

        [Header("Stufe & Werte")]
        [Min(1)] public int minLevel = 1;
        [Min(1)] public int maxLevel = 2;
        [Min(1f)] public float baseHealth = 60f;
        [Min(0f)] public float healthPerLevel = 10f;
        [Min(0f)] public float minDamage = 4f;
        [Min(0f)] public float maxDamage = 7f;
        [Min(0f)] public float damagePerLevel = 1f;
        [Tooltip("Balance-Tabelle (P2): Leben und Schaden kommen aus Stufe + Rolle. 'Custom' = die festen Werte oben.")]
        public BalanceRole balanceRole = BalanceRole.Custom;
        [Tooltip("Feinabstimmung der Art gegenueber ihrer Rolle (1 = genau die Tabelle)")]
        [Min(0.05f)] public float healthFactor = 1f;
        [Min(0.05f)] public float damageFactor = 1f;

        [Header("Angriff")]
        [Tooltip("Nahkampf: Reichweite. Fernkampf: maximale Schussweite")]
        [Min(0.5f)] public float attackRange = 2.2f;
        [Min(0.2f)] public float attackInterval = 1.6f;
        [Tooltip("Ausholen/Zielen vor dem Treffer (s) – sichtbar, damit man ausweichen kann")]
        [Min(0f)] public float windup = 0.4f;
        [Range(10f, 360f)] public float attackArc = 100f;
        [Tooltip("Fernkampf: Wunschabstand zum Ziel (m)")]
        [Min(0f)] public float preferredDistance = 11f;
        [Tooltip("Fernkampf: naeher als das -> zurueckweichen (m)")]
        [Min(0f)] public float tooCloseDistance = 5f;
        [Range(0f, 1f)] public float rangedAccuracy = 0.8f;

        [Header("Flaechenangriff (Stampfer)")]
        [Tooltip("Jeder n-te Angriff ist ein Stampfer mit Flaechenschaden (0 = nie)")]
        [Min(0)] public int areaAttackEvery;
        [Min(0.5f)] public float areaRadius = 4f;
        [Tooltip("Warnzeit des Stampfers (s) – ein Ring am Boden zeigt die Flaeche")]
        [Min(0.1f)] public float areaWindup = 1.2f;
        [Min(0f)] public float areaDamageMultiplier = 1.6f;

        [Header("Gift")]
        [Tooltip("Schaden je Sekunde nach einem Treffer (0 = kein Gift)")]
        [Min(0f)] public float poisonPerSecond;
        [Min(0f)] public float poisonDuration = 6f;

        [Header("Sonderverhalten")]
        [Tooltip("Taucht ab und an anderer Stelle wieder auf, wenn ein Spieler so lange zu nah ist (s, 0 = nie)")]
        [Min(0f)] public float diveWhenPressedFor;
        [Tooltip("Getarnt (unsichtbar, nicht anvisierbar), bis ein Spieler naeher als revealRadius kommt oder er angreift")]
        public bool camouflaged;
        [Min(1f)] public float revealRadius = 8f;
        [Tooltip("Springt Ziele aus dieser Entfernung an (0 = nie)")]
        [Min(0f)] public float pounceRange;
        [Tooltip("Bevorzugt Spieler ohne Begleitung in der Naehe")]
        public bool huntsLoners;

        [Header("Wurf (Nahkämpfer mit gelegentlichem Fernangriff, #28 Schlickwerfer)")]
        [Tooltip("Wirft ab und zu auf Abstand (0 = nie): Reichweite in m. Sichtbares Ausholen, nur Schaden (Baustein 29: Schlickwurf)")]
        [Min(0f)] public float throwRange;
        [Tooltip("Näher als das wird nicht geworfen (m) – dann kämpft er im Nahkampf")]
        [Min(0f)] public float throwMinRange = 6f;
        [Tooltip("Abklingzeit zwischen zwei Würfen (s)")]
        [Min(1f)] public float throwCooldown = 9f;
        [Tooltip("Ausholen vor dem Wurf (s)")]
        [Min(0.2f)] public float throwWindup = 0.9f;
        [Tooltip("Schaden gegenüber einem normalen Treffer")]
        [Min(0f)] public float throwDamageFactor = 0.8f;
        [Tooltip("Trefferchance des Wurfs (wer ausweicht, wird nie getroffen)")]
        [Range(0f, 1f)] public float throwAccuracy = 0.75f;

        public bool Throws => throwRange > 0f;
        [Tooltip("Stufenanzeige statt Zahl (z. B. \"??\" fuer den Totenkopf)")]
        public string levelLabel = "";
        public bool elite;
        [Tooltip("Schadensfaktor gegen Feuer (1 = normal) – wirkt, sobald es Feuerschaden gibt")]
        [Min(0f)] public float fireDamageTaken = 1f;

        [Header("Bewegung")]
        [Min(0.1f)] public float walkSpeed = 1.8f;
        [Min(0.1f)] public float runSpeed = 6.2f;
        [Tooltip("Streifen um den Heimatpunkt (m)")]
        [Min(0f)] public float wanderRadius = 5f;

        [Header("Verhalten")]
        [Tooltip("Wann greift er an? Kein Gegner alarmiert andere (keine Social-Aggro).")]
        public AggroProfile aggroProfile = AggroProfile.Aggressive;
        [Tooltip("Aggressive: Wahrnehmungsdistanz (m)")]
        [Min(1f)] public float aggroRadius = 12f;
        [Tooltip("Territorial: persoenliches Territorium um seine Position (m)")]
        [Min(1f)] public float territoryRadius = 6f;
        [Tooltip("Weiter vom Heimatpunkt weg -> Verfolgung abbrechen, heimkehren, heilen")]
        [Min(5f)] public float leashDistance = 38f;
        [Tooltip("Heult/ruft beim Kampfbeginn – nur Ton und Darstellung, alarmiert NIEMANDEN")]
        public bool howlsOnEngage;
        [Tooltip("Flieht unter diesem Lebensanteil (0 = nie)")]
        [Range(0f, 0.5f)] public float fleeHealthFraction;
        [Tooltip("Beutetier: flieht bei einem Angriff, statt sich zu wehren (nur mit Aggro-Profil Passiv)")]
        public bool fleesWhenAttacked;
        [Tooltip("Neutrales Tier (#28, wie in Classic): gelbes Namensschild, solange es nicht von selbst angreift – Aggro-Profil Passiv " +
                 "oder Defensiv, auch wenn ein Gebiet es so setzt. Angreifbar bleibt es; wie es kämpft, ändert das nicht.")]
        public bool neutral;
        [Tooltip("Tritt zwischen zwei Angriffen kurz zur Seite und greift dann wieder an – kein Kreisen (Aschwolf, Max 07.10.2026). " +
                 "0 = nie, 1 = nach jedem Angriff")]
        [Range(0f, 1f)] public float sidestepChance;

        [Header("Beute")]
        public LootTableDefinition lootTable;

        [Header("Tod & Wiederkehr")]
        [Min(1f)] public float corpseTime = 25f;
        [Min(1f)] public float respawnTime = 30f;
        [Tooltip("Groesser als respawnTime: zufaellige Wiederkehr zwischen beiden Werten (seltene Gegner)")]
        [Min(0f)] public float respawnTimeMax;

        public float RollRespawnTime() => respawnTimeMax > respawnTime ? Random.Range(respawnTime, respawnTimeMax) : respawnTime;

        /// <summary>Gelbes Namensschild bei diesem wirksamen Aggro-Profil? Nur neutrale Tiere, die nicht von selbst angreifen.</summary>
        public bool ShowsNeutral(AggroProfile effective) => neutral && (effective == AggroProfile.Passive || effective == AggroProfile.Defensive);

        [Header("Koerperteile und Moveset (Boss-Anatomie, Combat-Masterplan)")]
        [Tooltip("Leer = normaler Gegner ohne Trefferzonen")]
        public List<BodyPartDefinition> bodyParts = new List<BodyPartDefinition>();
        [Tooltip("Leer = bisherige Angriffslogik (Einzelschlag, jeder n-te Stampfer, Sprung)")]
        public List<EnemyAttackDefinition> moveset = new List<EnemyAttackDefinition>();
        [Tooltip("Darstellung: verwundet unter diesem Lebensanteil, kritisch unter dem zweiten")]
        [Range(0f, 1f)] public float woundedBelow = 0.66f;
        [Range(0f, 1f)] public float criticalBelow = 0.33f;

        [Header("Dominator (Klassen-Identitaet §59–§62)")]
        [Tooltip("Kann ein Dominator dieses Wesen binden? Nie fuer Bosse, einzigartige oder Story-Wesen (§61)")]
        public bool dominatable;

        [Header("Nachlese des Heilers (Klassen-Identitaet §32–§50)")]
        [Tooltip("Was ein Heiler zusaetzlich bergen kann – je Koerperteil und Zustand. Wuerfe 0 = keine Nachlese")]
        public SalvageProfile salvage = new SalvageProfile();

        public bool HasAnatomy => bodyParts != null && bodyParts.Count > 0;
        public bool HasMoveset => moveset != null && moveset.Count > 0;

        public BodyPartDefinition Part(byte id)
        {
            if (bodyParts == null || id == 0) return null;
            foreach (BodyPartDefinition p in bodyParts) if (p != null && p.id == id) return p;
            return null;
        }

        public EnemyAttackDefinition Move(byte id)
        {
            if (moveset == null || id == 0) return null;
            foreach (EnemyAttackDefinition m in moveset) if (m != null && m.id == id) return m;
            return null;
        }

        [Header("Aussehen (Platzhalter)")]
        public Color bodyColor = new Color(0.22f, 0.21f, 0.2f);
        public Color glowColor = new Color(1f, 0.45f, 0.12f);
        [Min(0.3f)] public float scale = 1f;

        public bool UsesBalance => balanceRole != BalanceRole.Custom;

        public float HealthAt(int level) => UsesBalance
            ? Mathf.Round(Balance.MobHealth(level) * Balance.RoleFactors(balanceRole).x * healthFactor)
            : baseHealth + healthPerLevel * (level - 1);

        /// <summary>Durchschnittlicher Schaden eines normalen Treffers auf dieser Stufe.</summary>
        public float AverageHit(int level)
        {
            if (!UsesBalance) return (minDamage + maxDamage) * 0.5f + damagePerLevel * (level - 1);
            // Flaechenangriffe (jeder n-te Schlag, staerker) und Gift sind im Tempo enthalten – der Schnitt bleibt beim Tabellenwert
            float area = areaAttackEvery > 0 ? ((areaAttackEvery - 1) + areaDamageMultiplier) / areaAttackEvery : 1f;
            float perHit = Balance.MobDps(level) * Balance.RoleFactors(balanceRole).y * damageFactor * AttackCycle / area;
            return Mathf.Max(1f, perHit);
        }

        /// <summary>Zeit zwischen zwei Angriffen (Pause + Ausholen, wie im Kampfgehirn).</summary>
        public float AttackCycle => attackInterval + windup;

        public float RollDamage(int level)
        {
            if (UsesBalance) return Mathf.Max(1f, Mathf.Round(AverageHit(level) * Random.Range(0.85f, 1.15f)));
            float bonus = damagePerLevel * (level - 1);
            return Mathf.Round(Random.Range(minDamage, maxDamage) + bonus);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            if (maxLevel < minLevel) maxLevel = minLevel;
            if (maxDamage < minDamage) maxDamage = minDamage;
        }
#endif
    }
}
