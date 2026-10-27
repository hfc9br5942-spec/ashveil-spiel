using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using UnityEngine;

namespace Ashveil.Gameplay.Items
{
    /// <summary>Wer Beute bekommen kann: stabile Profil-ID + Klasse (vom Netzwerk-Layer bereitgestellt).</summary>
    public interface ILootRecipient
    {
        string ProfileId { get; }
        CharacterClassDefinition LootClass { get; }
        bool IsReady { get; }
    }

    /// <summary>
    /// Gruppen (Milestone 5). Bis dahin leer – die Schnittstelle steht, damit Gruppenmitglieder
    /// ohne eigenen Treffer beuteberechtigt werden.
    /// </summary>
    public static class PartyRegistry
    {
        public static System.Func<string, IEnumerable<string>> MembersOf = _ => System.Array.Empty<string>();
        /// <summary>Ist dieses Mitglied nah genug am Geschehen (Weltpunkt), um mitzuzaehlen?</summary>
        public static System.Func<string, Vector3, bool> IsNear = (_, __) => true;
    }

    /// <summary>
    /// Server: merkt sich, wer an einem Kampf beteiligt war – Schaden, Tanken (vom Gegner angegriffen),
    /// Heilen/Unterstuetzen eines Beteiligten. Kein "letzter Treffer" noetig.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LootEligibility : MonoBehaviour
    {
        private static readonly List<LootEligibility> Active = new List<LootEligibility>();

        private readonly HashSet<string> _profiles = new HashSet<string>();
        private readonly Dictionary<string, CharacterClassDefinition> _classes = new Dictionary<string, CharacterClassDefinition>();
        private Health _health;
        private EnemyBrain _brain;
        private float _pollTimer;
        private bool _serverActive;
        private EnemyState _lastState = EnemyState.Idle;

        public IReadOnlyCollection<string> Profiles => _profiles;
        public bool IsEngaged => _profiles.Count > 0 && _health != null && _health.IsAlive;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _brain = GetComponent<EnemyBrain>();
        }

        /// <summary>Nur der Server zaehlt Beteiligung.</summary>
        public void ActivateServer()
        {
            if (_serverActive) return;
            _serverActive = true;
            if (_health != null) _health.Damaged += OnDamaged;
            if (_brain != null) { _lastState = _brain.State; _brain.StateChanged += OnStateChanged; }
            if (!Active.Contains(this)) Active.Add(this);
        }

        private void OnDestroy()
        {
            if (_health != null) _health.Damaged -= OnDamaged;
            if (_brain != null) _brain.StateChanged -= OnStateChanged;
            Active.Remove(this);
        }

        // 21.10.: Heimkehr setzt den Gegner zurueck (volles Leben, Bedrohung weg) – dann gilt auch die alte Beteiligung nicht mehr.
        // Sonst bekamen fruehere Angreifer/Verfolgte beim spaeteren Tod noch EP, Kill-Ziel und persoenliche Beute, der Toeter nur
        // den geteilten EP-Anteil. Return -> Idle gibt es nur nach diesem Zuruecksetzen (EnemyBrain, Fall Return). Die Arena kennt
        // kein Return (BeginReturn geht dort ohne Heilung direkt auf Idle) – dort bleibt die Beteiligung stehen.
        private void OnStateChanged(EnemyState s)
        {
            if (_lastState == EnemyState.Return && s == EnemyState.Idle) ServerReset();
            _lastState = s;
        }

        /// <summary>Server: Beteiligung vergessen (Gegner nach der Heimkehr zurueckgesetzt).</summary>
        public void ServerReset()
        {
            _profiles.Clear();
            _classes.Clear();
            _contribution.Clear();
        }

        private void OnDamaged(DamageInfo info)
        {
            Credit(info.Source);
            AddContribution(info.Source, info.Amount);
        }

        // Beitrag in Lebenspunkten (Schaden + 1,5 × Heilung eines Beteiligten) – fuer die Nachlese (§43: echter Beitrag,
        // nicht danebenstehen). Die Beute-Berechtigung selbst bleibt ein Ja/Nein wie bisher.
        private readonly Dictionary<string, float> _contribution = new Dictionary<string, float>();
        public const float HealWeight = 1.5f;

        public float ContributionOf(string profile) => profile != null && _contribution.TryGetValue(profile, out float v) ? v : 0f;

        public void AddContribution(GameObject who, float amount)
        {
            if (!_serverActive || who == null || amount <= 0f || _health == null || !_health.IsAlive) return;
            ILootRecipient r = who.GetComponentInParent<ILootRecipient>();
            if (r == null || string.IsNullOrEmpty(r.ProfileId)) return;
            _contribution[r.ProfileId] = ContributionOf(r.ProfileId) + amount;
        }

        private void Update()
        {
            if (!_serverActive || _brain == null || _health == null || !_health.IsAlive) return;
            _pollTimer -= Time.deltaTime;
            if (_pollTimer > 0f) return;
            _pollTimer = 0.5f;
            // Tanken: wen der Gegner verfolgt oder angreift, der ist beteiligt
            if (_brain.Target != null && (_brain.State == EnemyState.Chase || _brain.State == EnemyState.Attack))
                Credit(_brain.Target.gameObject);
        }

        public void Credit(GameObject who)
        {
            if (!_serverActive || who == null || _health == null || !_health.IsAlive) return;
            ILootRecipient r = who.GetComponentInParent<ILootRecipient>();
            if (r == null || !r.IsReady || string.IsNullOrEmpty(r.ProfileId)) return;
            _profiles.Add(r.ProfileId);
            _classes[r.ProfileId] = r.LootClass;
        }

        public bool Contains(string profile) => _profiles.Contains(profile);

        public CharacterClassDefinition ClassOf(string profile) =>
            _classes.TryGetValue(profile, out CharacterClassDefinition c) ? c : null;

        /// <summary>
        /// Heilung/Unterstuetzung: wer einem Beteiligten eines laufenden Kampfes hilft, wird selbst beteiligt.
        /// </summary>
        public static void CreditSupport(GameObject supporter, GameObject supported) => CreditSupport(supporter, supported, 0f);

        /// <summary>Wie oben, mit der tatsaechlich geheilten Menge als Beitrag (zaehlt 1,5-fach).</summary>
        public static void CreditSupport(GameObject supporter, GameObject supported, float healed)
        {
            if (supporter == null || supported == null || supporter == supported) return;
            ILootRecipient target = supported.GetComponentInParent<ILootRecipient>();
            if (target == null || string.IsNullOrEmpty(target.ProfileId)) return;
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                LootEligibility e = Active[i];
                if (e != null && e.IsEngaged && e.Contains(target.ProfileId))
                {
                    e.Credit(supporter);
                    e.AddContribution(supporter, healed * HealWeight);
                }
            }
        }

        /// <summary>Beim Tod: Beteiligte + ihre Gruppenmitglieder in der Naehe.</summary>
        public HashSet<string> FinalRecipients()
        {
            var all = new HashSet<string>(_profiles);
            foreach (string p in _profiles)
                foreach (string m in PartyRegistry.MembersOf(p))
                    if (!string.IsNullOrEmpty(m) && PartyRegistry.IsNear(m, transform.position)) all.Add(m);
            return all;
        }
    }
}
