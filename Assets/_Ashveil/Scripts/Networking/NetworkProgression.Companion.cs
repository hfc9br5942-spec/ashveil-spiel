using System.Collections;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Companions;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Quests;
using Ashveil.Gameplay.Targeting;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Dominator (Aschenjaeger) – Gefaehrte, Klassen-Identitaet Phase 4 (§58–§91, Prototyp §185). Serverautoritaer:
    ///
    ///   Dominieren – eigenes Ziel ist ein dominierbares Wesen (§60/§61: nie Bosse/Elite/Story), hoechstens so stark wie
    ///                man selbst, unter 35 % Leben, in 10 m → 3 s „Dominieren“ (das Wesen erstarrt; Laufen oder eigener
    ///                Angriff bricht ab). Danach verlaesst das wilde Wesen die Welt und steht als Gefaehrte neben dir.
    ///   Rufen      – Leiste: Gefaehrten rufen oder entlassen. Genau einer aktiv (§63).
    ///   Befehle    – Folgen, Bleiben, Angreifen (dein Ziel), Passiv (§64) – Gefaehrten-Rahmen.
    ///   Bindung    – Schaden an Gegnern, gemeinsame Siege (Elite mehr), gemeinsame Wege; mit Stufenabschlag (§79–§82).
    ///
    /// Freischalten spaeter ueber die Klassenquest (§72), bis dahin ueber das Test-Menue.
    /// </summary>
    public sealed partial class NetworkProgression
    {
        public const string DominateSignature = "dominate";
        private const string CompanionResource = "Companions/Companion_Aschwolf";

        private NetworkCompanion _pet;
        private float _petHealthFraction = 1f;
        private bool _petDownedWhenDismissed;
        private float _domUntil = -1f, _domStartedAt;
        private float _domStunBis = -1f;   // 23.10.2026 (STORY-05 Nachtrag): Ende der vom Dominieren gesetzten Betäubung des Ziels
        private Vector3 _domFrom;
        private EnemyBrain _domBrain;
        private Vector3 _travelFrom;
        private float _travelTimer;
        private bool _companionHooked;

        public bool ServerCanDominate => Loaded && D.signatures != null && D.signatures.Contains(DominateSignature);
        public bool ServerDominating => _domUntil > 0f;
        public bool ServerHasCompanion => Loaded && D.companion != null && CompanionCatalog.Get(D.companion.species) != null;
        public NetworkCompanion ServerPet => _pet;
        public CompanionSpecies ServerCompanionSpecies => Loaded && D.companion != null ? CompanionCatalog.Get(D.companion.species) : null;
        /// <summary>Server: letzter Grund/Hinweis (Tests).</summary>
        public string ServerCompanionNote { get; private set; } = "";

        public int ServerCompanionBondRank
        {
            get
            {
                CompanionSpecies s = ServerCompanionSpecies;
                return s != null && s.bond != null ? s.bond.RankFor(ServerMasteryPoints(s.bond.id)) : 0;
            }
        }

        private bool DominatorClassOk => _inv.ServerModel != null && _inv.ServerModel.Class != null && _inv.ServerModel.Class.Id == CompanionCatalog.DominatorClassId;

        // ================================================================== Freischalten

        public string ServerUnlockDominate()
        {
            if (!Loaded) return "Nicht bereit.";
            if (!DominatorClassOk) return "Nur ein Aschenjäger kann Wesen dominieren.";
            if (D.signatures == null) D.signatures = new System.Collections.Generic.List<string>();
            if (!D.signatures.Contains(DominateSignature)) D.signatures.Add(DominateSignature);
            GetComponent<NetworkActionBar>()?.ServerPlaceIfMissing(ActionEntryType.Companion, CompanionCatalog.DominateId);
            _inv.ServerMarkDirty();
            SendCompanionState();
            CompanionMessage("Du hast gelernt, Wesen zu dominieren: Schwäche ein Tier unter 35 % Leben und brich seinen Willen (Leiste).");
            return "";
        }

        /// <summary>Server (Tests/Werkzeug): Dominieren und Gefaehrten wieder entfernen.</summary>
        public void ServerRemoveDominate()
        {
            if (!Loaded) return;
            DespawnPet();
            CancelDominate("");
            D.signatures?.Remove(DominateSignature);
            D.companion = null;
            _petHealthFraction = 1f;
            _petDownedWhenDismissed = false;
            _inv.ServerMarkDirty();
            SendCompanionState();
        }

        // ================================================================== Dominieren

        [ServerRpc]
        private void ServerDominateRequest(NetworkObject target) => ServerStartDominate(target);

        public string ServerStartDominate(NetworkObject target)
        {
            string why = DominateCheck(target, out EnemyBrain brain);
            ServerCompanionNote = why;
            if (why.Length > 0) { CompanionMessage(why); return why; }
            if (ServerDominating) return "";
            CompanionSpecies spec = CompanionCatalog.ForEnemy(brain.Definition.Id);
            _domBrain = brain;
            _domUntil = Time.time + spec.dominateSeconds;
            _domStartedAt = Time.time;
            _domFrom = transform.position;
            brain.ServerStun(spec.dominateSeconds + 0.4f);   // sein Wille ist gebunden, solange du ihn haeltst
            _domStunBis = Time.time + (spec.dominateSeconds + 0.4f);   // 23.10.2026: genau dieses Ende beendet CancelDominate wieder
            if (Owner != null && Owner.IsActive) TargetDominateCast(Owner, spec.dominateSeconds);
            return "";
        }

        private string DominateCheck(NetworkObject target, out EnemyBrain brain)
        {
            brain = target != null ? target.GetComponent<EnemyBrain>() : null;
            if (!Loaded) return "Nicht bereit.";
            if (!ServerCanDominate || !DominatorClassOk) return "Du beherrschst das Dominieren nicht.";
            if (_health != null && !_health.IsAlive) return "Nicht jetzt.";
            if (ServerMounted) return "Nicht im Sattel.";
            if (ServerHasCompanion) return "Du führst bereits einen Gefährten.";
            if (brain == null || brain.Definition == null || brain.Health == null || !brain.Health.IsAlive) return "Du brauchst ein lebendes Wesen als Ziel.";
            EnemyDefinition def = brain.Definition;
            CompanionSpecies spec = CompanionCatalog.ForEnemy(def.Id);
            // KAMPF-01 Folgepunkt (21.10.): Ein heimkehrendes Tier ist unverwundbar und lässt sich nicht betäuben (ServerStun wirkt
            // bei Return nicht) – das Dominieren lief sonst mit Wirkbalken an, das Tier lief weiter heim und es brach nach ~2 s mit
            // „zu weit entfernt“ ab (bei naher Heimat gelang es sogar). Jetzt gleich abgelehnt, mit der vorhandenen Meldung.
            if (!def.dominatable || def.elite || spec == null || brain.InArena || brain.State == EnemyState.Return) return $"{def.DisplayName} lässt sich nicht dominieren.";
            if (brain.Level > ServerLevel) return "Dieses Wesen ist stärker als du.";
            if (brain.HealthFraction > spec.dominateBelow) return $"Sein Wille ist ungebrochen – schwäche es unter {spec.dominateBelow * 100f:0} % Leben.";
            if (Vector3.Distance(brain.transform.position, transform.position) > spec.dominateRange) return "Zu weit entfernt.";
            return "";
        }

        private void DominateTick()
        {
            if (!ServerDominating) return;
            // 22.10. (STORY-05): eigener Tod bricht ab (wie das Geschichts-Wirken) – sonst lief der Balken weiter und der tote
            // Aschenjaeger bekam den Gefaehrten neben seiner Leiche.
            // 23.10.2026 (Kommentar korrigiert): Bis zum 22.10. hob CancelDominate die Betaeubung NICHT auf – ServerStun(0f) ging an
            // der Max-Regel von EnemyBrain.ServerStun vorbei, das Ziel blieb bis zum geplanten Ende (Dominieren + 0,4 s) betaeubt.
            // Jetzt beendet CancelDominate genau die vom Dominieren gesetzte Betaeubung (EnemyBrain.ServerStunEnde).
            if (_health != null && !_health.IsAlive) { CancelDominate("Dominieren abgebrochen."); return; }
            Vector3 d = transform.position - _domFrom;
            d.y = 0f;
            if (_domBrain == null || _domBrain.Health == null || !_domBrain.Health.IsAlive) CancelDominate("Dominieren abgebrochen.");
            else if (d.sqrMagnitude > 0.35f * 0.35f) CancelDominate("Dominieren abgebrochen – du hast dich bewegt.");
            else if (_mCombat != null && _mCombat.ServerLastAttackTime > _domStartedAt) CancelDominate("Dominieren abgebrochen.");
            else if (Vector3.Distance(_domBrain.transform.position, transform.position) > 12f) CancelDominate("Dominieren abgebrochen – zu weit entfernt.");
            else if (Time.time >= _domUntil) CompleteDominate();
        }

        private void CancelDominate(string message)
        {
            if (!ServerDominating) return;
            _domUntil = -1f;
            // 23.10.2026 (STORY-05 Nachtrag): Abbruch gibt den Willen des Wesens frei – die vom Dominieren gesetzte Betaeubung endet
            // sofort. Vorher ServerStun(0f): wirkungslos (Mathf.Max), das Ziel stand bis zum geplanten Ende still. Eine laengere fremde
            // Betaeubung (Schildhieb, Anatomie, Tests) bleibt bestehen.
            if (_domBrain != null) _domBrain.ServerStunEnde(_domStunBis);
            _domStunBis = -1f;
            _domBrain = null;
            if (Owner != null && Owner.IsActive)
            {
                TargetDominateCastEnd(Owner, true);
                if (!string.IsNullOrEmpty(message)) TargetMessage(Owner, message);
            }
        }

        private void CompleteDominate()
        {
            EnemyBrain brain = _domBrain;
            _domUntil = -1f;
            _domBrain = null;
            CompanionSpecies spec = brain != null && brain.Definition != null ? CompanionCatalog.ForEnemy(brain.Definition.Id) : null;
            if (spec == null) return;
            Vector3 at = brain.transform.position;
            float yaw = brain.transform.eulerAngles.y;
            float fraction = Mathf.Max(0.2f, brain.HealthFraction);
            if (EnemyDirector.Instance != null) EnemyDirector.Instance.ServerRemoveTamed(brain);
            D.companion = new CompanionSave { species = spec.id, name = "", active = true };
            D.lifetime.dominations++;
            GetComponent<NetworkActionBar>()?.ServerPlaceIfMissing(ActionEntryType.Companion, CompanionCatalog.CallId);
            _inv.ServerMarkDirty();
            ServerCompanionNote = "dominiert: " + spec.id;
            if (Owner != null && Owner.IsActive) TargetDominateCastEnd(Owner, false);
            SpawnPet(at, yaw, fraction);
            CompanionMessage($"Der {spec.displayName} beugt sich deinem Willen. Er folgt dir jetzt.");
        }

        // ================================================================== Rufen und Entlassen

        [ServerRpc]
        private void ServerCompanionToggleRpc() => ServerToggleCompanion();

        public string ServerToggleCompanion()
        {
            if (!ServerHasCompanion) { CompanionMessage("Du hast keinen Gefährten."); return "kein Gefährte"; }
            if (_pet != null)
            {
                _petHealthFraction = _pet.Health != null ? _pet.Health.Fraction : 1f;
                _petDownedWhenDismissed = _pet.Downed;
                DespawnPet();
                D.companion.active = false;
                _inv.ServerMarkDirty();
                SendCompanionState();
                return "";
            }
            if (_petDownedWhenDismissed && (_mInCombat || ComputeInCombat(Time.time)))
            {
                CompanionMessage("Dein Gefährte ist noch erschöpft. Nach dem Kampf kommt er wieder.");
                return "erschöpft";
            }
            float fraction = _petDownedWhenDismissed ? 0.5f : Mathf.Max(0.2f, _petHealthFraction);
            _petDownedWhenDismissed = false;
            D.companion.active = true;
            _inv.ServerMarkDirty();
            Transform t = transform;
            SpawnPet(t.position - t.forward * 2f + t.right * 1f, t.eulerAngles.y, fraction);
            return "";
        }

        private void SpawnPet(Vector3 at, float yaw, float fraction)
        {
            CompanionSpecies spec = ServerCompanionSpecies;
            if (spec == null || _pet != null) return;
            GameObject prefab = Resources.Load<GameObject>(CompanionResource);
            if (prefab == null) { Debug.LogWarning("[Ashveil] Gefährten-Vorlage fehlt – Menü „Ashveil › Kampf › Begleiter: Aschwolf-Gefährte bauen“."); return; }
            GameObject go = Instantiate(prefab, at, Quaternion.Euler(0f, yaw, 0f));
            go.name = spec.displayName + " (" + (Loaded ? D.name : "") + ")";
            EnemyInterestCondition.Ensure(go);
            ServerManager.Spawn(go, Owner);
            _pet = go.GetComponent<NetworkCompanion>();
            if (_pet == null) { ServerManager.Despawn(go); return; }
            _pet.ServerInit(this, spec, D.companion != null ? D.companion.name : "", Loaded ? D.name : "", fraction);
            _pet.DealtDamage += OnPetDealtDamage;
            _pet.FoeFell += OnPetFoeFell;
            _travelFrom = transform.position;
            SendCompanionState();
        }

        private void DespawnPet()
        {
            if (_pet == null) return;
            _pet.DealtDamage -= OnPetDealtDamage;
            _pet.FoeFell -= OnPetFoeFell;
            _pet.ServerDespawn();
            _pet = null;
        }

        // ================================================================== Befehle

        [ServerRpc]
        private void ServerCompanionCommandRpc(byte cmd, NetworkObject target) => ServerCompanionCommand((CompanionCommand)cmd, target);

        public string ServerCompanionCommand(CompanionCommand cmd, NetworkObject target)
        {
            if (_pet == null) return "kein Gefährte draußen";
            if (_pet.Downed) return "besiegt";
            Health h = null;
            if (cmd == CompanionCommand.Attack)
            {
                h = target != null ? target.GetComponent<Health>() : null;
                var ct = target != null ? target.GetComponent<CombatTarget>() : null;
                if (h == null || !h.IsAlive || ct == null || !FactionRules.IsAttackableByPlayer(ct.Faction))
                {
                    CompanionMessage("Dafür brauchst du ein feindliches Ziel.");
                    return "kein feindliches Ziel";
                }
            }
            _pet.ServerSetCommand(cmd, h);
            SendCompanionState();
            return "";
        }

        // ================================================================== Bindung (§79–§82)

        private void OnPetDealtDamage(Health foe, float amount)
        {
            CompanionSpecies s = ServerCompanionSpecies;
            var b = foe != null ? foe.GetComponent<EnemyBrain>() : null;
            if (s == null || s.bond == null || b == null) return;
            float factor = MasteryCatalog.LevelFactor(b.Level, ServerLevel);
            if (factor > 0f) AddMasteryPoints(s.bond, amount / 25.0 * factor);
        }

        private void OnPetFoeFell(EnemyBrain b)
        {
            CompanionSpecies s = ServerCompanionSpecies;
            if (s == null || s.bond == null || b == null) return;
            D.lifetime.companionKills++;
            float factor = MasteryCatalog.LevelFactor(b.Level, ServerLevel);
            if (factor > 0f) AddMasteryPoints(s.bond, (b.Definition != null && b.Definition.elite ? 6.0 : 1.0) * factor);
            else { _mSaveDirty = true; if (!_mSendPending) { _mSendPending = true; _mSendTimer = 5f; } }
        }

        private void CompanionTravelTick()
        {
            if (_pet == null || _pet.Downed || _pet.Command == CompanionCommand.Stay) { _travelFrom = transform.position; return; }
            _travelTimer -= Time.deltaTime;
            if (_travelTimer > 0f) return;
            _travelTimer = 1f;
            Vector3 d = transform.position - _travelFrom; d.y = 0f;
            _travelFrom = transform.position;
            float m = d.magnitude;
            if (m > 20f) return; // Teleport
            CompanionSpecies s = ServerCompanionSpecies;
            if (s != null && s.bond != null && m > 0.5f) AddMasteryPoints(s.bond, m * 0.004);
        }

        // ================================================================== Ablauf

        private void CompanionLogin()
        {
            _companionHooked = true;
            SendCompanionState();
            if (ServerHasCompanion && D.companion.active) StartCoroutine(SpawnPetAfterLogin());
        }

        private IEnumerator SpawnPetAfterLogin()
        {
            yield return new WaitForSeconds(1.5f);
            if (!Loaded || _pet != null || !ServerHasCompanion || !D.companion.active) yield break;
            Transform t = transform;
            SpawnPet(t.position - t.forward * 2f + t.right * 1f, t.eulerAngles.y, 1f);
        }

        private void CompanionStopServer()
        {
            if (!_companionHooked) return;
            _companionHooked = false;
            DespawnPet();
        }

        private void CompanionUpdate()
        {
            if (!Loaded) return;
            DominateTick();
            CompanionTravelTick();
        }

        /// <summary>Vom Gefaehrten: sein Zustand hat sich geaendert (Befehl, besiegt, Stufe).</summary>
        internal void CompanionChanged() => SendCompanionState();

        internal void CompanionMessage(string text)
        {
            if (Owner != null && Owner.IsActive && !string.IsNullOrEmpty(text)) TargetMessage(Owner, text);
        }

        private void SendCompanionState()
        {
            if (Owner == null || !Owner.IsActive || !Loaded) return;
            CompanionSpecies s = ServerCompanionSpecies;
            TargetCompanionState(Owner, ServerCanDominate, s != null ? s.id : "", D.companion != null ? D.companion.name ?? "" : "",
                _pet != null, _pet != null && _pet.Downed, (byte)(_pet != null ? _pet.Command : CompanionCommand.Follow), ServerLevel);
        }

        // ================================================================== Besitzer

        private void CompanionStartClient()
        {
            if (!IsOwner) return;
            CompanionClient.TriggerRequest = OnCompanionTrigger;
            CompanionClient.CommandRequest = OnCompanionCommand;
        }

        private void CompanionStopClient()
        {
            if (!IsOwner) return;
            if (CompanionClient.TriggerRequest == (System.Action<string>)OnCompanionTrigger) CompanionClient.TriggerRequest = null;
            if (CompanionClient.CommandRequest == (System.Action<CompanionCommand>)OnCompanionCommand) CompanionClient.CommandRequest = null;
        }

        private NetworkObject CurrentTargetObject()
        {
            var pc = GetComponent<PlayerCharacter>();
            TargetingSystem ts = pc != null ? pc.Targeting : GetComponent<TargetingSystem>();
            CombatTarget ct = ts != null ? ts.Current : null;
            return ct != null ? ct.GetComponentInParent<NetworkObject>() : null;
        }

        private void OnCompanionTrigger(string id)
        {
            if (!IsOwner) return;
            if (id == CompanionCatalog.DominateId) ServerDominateRequest(CurrentTargetObject());
            else if (id == CompanionCatalog.CallId) ServerCompanionToggleRpc();
        }

        private void OnCompanionCommand(CompanionCommand cmd)
        {
            if (!IsOwner) return;
            ServerCompanionCommandRpc((byte)cmd, cmd == CompanionCommand.Attack ? CurrentTargetObject() : null);
        }

        [TargetRpc]
        private void TargetDominateCast(NetworkConnection conn, float seconds) => CastBarState.Begin("Dominieren", seconds);

        [TargetRpc]
        private void TargetDominateCastEnd(NetworkConnection conn, bool interrupted) => CastBarState.End(interrupted);

        [TargetRpc]
        private void TargetCompanionState(NetworkConnection conn, bool canDominate, string species, string name, bool isOut, bool downed, byte command, int level) =>
            CompanionClient.Apply(canDominate, species, name, isOut, downed, (CompanionCommand)command, level);
    }
}
