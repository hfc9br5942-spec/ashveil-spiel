using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Gameplay.Combat
{
    /// <summary>
    /// Macht ein Objekt anvisierbar (Tab, Mausklick, Zielrahmen). Traegt Name, Stufe und Zugehoerigkeit.
    /// </summary>
    [RequireComponent(typeof(Health))]
    [DisallowMultipleComponent]
    public sealed class CombatTarget : MonoBehaviour
    {
        private static readonly List<CombatTarget> AllTargets = new List<CombatTarget>();

        [SerializeField] private string displayName = "Ziel";
        [SerializeField] private int level = 1;
        [SerializeField] private string levelText = "";
        [SerializeField] private Faction faction = Faction.Hostile;
        [SerializeField] private bool elite;
        [Tooltip("Punkt, auf den gezielt wird (relativ zum Objekt)")]
        [SerializeField] private Vector3 aimOffset = new Vector3(0f, 1.2f, 0f);
        [Tooltip("Radius der Auswahlmarkierung am Boden")]
        [SerializeField] private float selectionRadius = 0.8f;

        private Health _health;

        public static IReadOnlyList<CombatTarget> All => AllTargets;

        public string DisplayName => displayName;
        public int Level => level;
        /// <summary>Anzeige der Stufe, z. B. "1" oder "??" fuer viel zu starke Gegner.</summary>
        public string LevelLabel => string.IsNullOrEmpty(levelText) ? level.ToString() : levelText;
        public Faction Faction => faction;
        public bool IsElite => elite;
        public Health Health => _health;
        public Vector3 AimPoint => transform.position + aimOffset;
        public float SelectionRadius => selectionRadius;
        public bool IsAttackable => _health != null && _health.IsAlive && !Concealed && FactionRules.IsAttackableByPlayer(faction);

        /// <summary>Getarnt oder abgetaucht: nicht anvisierbar (Tab/Klick), Namensschild aus.</summary>
        public bool Concealed { get; set; }

        private void Awake()
        {
            _health = GetComponent<Health>();
        }

        private void OnEnable()
        {
            if (!AllTargets.Contains(this)) AllTargets.Add(this);
        }

        private void OnDisable()
        {
            AllTargets.Remove(this);
        }

        public void Configure(string newName, int newLevel, Faction newFaction, Vector3 newAimOffset,
            float newSelectionRadius, bool isElite = false, string newLevelText = "")
        {
            displayName = newName;
            level = newLevel;
            faction = newFaction;
            aimOffset = newAimOffset;
            selectionRadius = newSelectionRadius;
            elite = isElite;
            levelText = newLevelText ?? "";
        }

        /// <summary>Nur die Zugehörigkeit ändern (#28: neutrale Tiere je Gebiet gelb oder rot).</summary>
        public void SetFaction(Faction newFaction) => faction = newFaction;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry()
        {
            AllTargets.Clear();
        }
    }
}
