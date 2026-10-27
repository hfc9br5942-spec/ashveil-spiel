using Ashveil.Data;
using UnityEngine;

namespace Ashveil.Gameplay.Enemies
{
    /// <summary>
    /// Markiert, wo Gegner leben (Rudel, Lager). Der Server erzeugt hier <see cref="count"/> Gegner
    /// und laesst Gefallene nach der Wiederkehrzeit neu erscheinen. Die Gruppe dient nur der Zuordnung
    /// (Auftraege, Anzeige) – Gegner alarmieren einander NICHT (keine Social-Aggro, Kanon 26.09.).
    /// </summary>
    public sealed class EnemySpawnArea : MonoBehaviour
    {
        [SerializeField] private EnemyDefinition definition;
        [Tooltip("Netzwerk-Prefab des Gegners")]
        [SerializeField] private GameObject prefab;
        [Min(1)] [SerializeField] private int count = 3;
        [Min(0f)] [SerializeField] private float radius = 4f;
        [Tooltip("Gruppenname nur zur Zuordnung (Auftraege, Anzeige, Tests) – Gegner alarmieren einander NICHT (COMBAT-062). Leer = Name@Position.")]
        [SerializeField] private string groupName = "";
        [Tooltip("Stufen fuer dieses Gebiet (0 = aus der Definition)")]
        [Min(0)] [SerializeField] private int minLevel;
        [Min(0)] [SerializeField] private int maxLevel;
        [Tooltip("Mehrere Verstecke: jedes Erscheinen waehlt zufaellig eines (seltene Gegner)")]
        [SerializeField] private Vector3[] alternatePoints = new Vector3[0];
        [Tooltip("Feste Wanderroute (z. B. Glockenhirte)")]
        [SerializeField] private EnemyRoute route;
        [Tooltip("Aggro-Profil nur fuer dieses Gebiet ueberschreiben (z. B. friedliche Startgegner am Lager)")]
        [SerializeField] private bool overrideAggro;
        [SerializeField] private AggroProfile aggroProfile = AggroProfile.Defensive;
        [Tooltip("Wiederkehrzeit dieses Gebiets = Zeit des Gegnertyps × Faktor (Weltfülle 10.10.2026: verstreute Wildtiere und Streuner ≈ 3, Auftragslager 1)")]
        [Min(0.1f)] [SerializeField] private float respawnMultiplier = 1f;
        [Tooltip("Nur fuer Boss-Begegnungen: erscheint nicht von selbst, sondern wird vom Boss gerufen (EnemyEncounter)")]
        [SerializeField] private bool summonOnly;

        public EnemyDefinition Definition => definition;
        public GameObject Prefab => prefab;
        public int Count => count;
        public float Radius => radius;
        public EnemyRoute Route => route;
        public bool SummonOnly => summonOnly;
        public float RespawnMultiplier => respawnMultiplier > 0.1f ? respawnMultiplier : 1f;
        /// <summary>Aggro-Profil der Gegner dieses Gebiets (Gebietswert oder der des Gegnertyps).</summary>
        public AggroProfile Aggro => overrideAggro || definition == null ? aggroProfile : definition.aggroProfile;
        public int MinLevel => minLevel > 0 ? minLevel : (definition != null ? definition.minLevel : 1);
        public int MaxLevel => Mathf.Max(MinLevel, maxLevel > 0 ? maxLevel : (definition != null ? definition.maxLevel : 1));

        /// <summary>Mittelpunkt fuer das naechste Erscheinen (bei mehreren Verstecken zufaellig).</summary>
        public Vector3 PickCenter()
        {
            if (alternatePoints == null || alternatePoints.Length == 0) return transform.position;
            int i = Random.Range(0, alternatePoints.Length + 1);
            return i == 0 ? transform.position : alternatePoints[i - 1];
        }

        public int HideoutCount => 1 + (alternatePoints != null ? alternatePoints.Length : 0);
        /// <summary>Gruppe fuer Rudelruf.</summary>
        public string Group => string.IsNullOrEmpty(groupName) ? name + "@" + transform.position.ToString("F0") : groupName;

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, radius);
            if (definition != null) Gizmos.DrawWireSphere(transform.position, definition.aggroRadius);
            if (alternatePoints != null)
                foreach (Vector3 p in alternatePoints) Gizmos.DrawWireSphere(p, radius + 1f);
        }
    }
}
