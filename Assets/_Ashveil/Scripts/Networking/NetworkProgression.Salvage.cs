using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Quests;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Nachlese des Heilers (Class Identity Masterplan §32–§57, Phase 3 / §184: ein Gegner, ein abtrennbares Teil, ein
    /// Bergungswurf). Alles serverautoritaer:
    ///
    ///   Tod eines Gegners mit Nachlese-Profil → fuer jeden berechtigten Heiler (Signatur erlernt, Heilerklasse, echter
    ///   Beitrag ≥ Mindestanteil) ein persoenlicher Wurf aus den Koerperteil-Zustaenden beim Tod (intakt ≠ gebrochen ≠
    ///   abgetrennt, §40). Normaler Gruppenloot bleibt unberuehrt (§34). Nur der Heiler sieht an der Leiche ein gruenes
    ///   Glimmen. Rechtsklick (nach der Beute) → Zauberleiste „Bergen“ (bewegen, angreifen, Treffer brechen ab) → in den
    ///   Rucksack. Meisterschaft Nachlese: seltene Stoffe haeufiger, Bergen schneller; Stufenabschlag wie ueberall (§50).
    /// </summary>
    public sealed partial class NetworkProgression
    {
        public const string SalvageSignature = "salvage";
        private const float SalvageBaseSeconds = 2.0f;
        private const float SalvageRange = 6.5f;

        private float _salvageUntil = -1f;
        private float _salvageStartedAt;
        private Vector3 _salvageFrom;
        private CorpseLoot _salvageCorpse;
        private NetworkObject _salvageCorpseObj;
        private bool _salvageHooked;

        /// <summary>Server: hat diese Figur die Nachlese erlernt?</summary>
        public bool ServerSalvageUnlocked => Loaded && D.signatures != null && D.signatures.Contains(SalvageSignature);
        public bool ServerSalvaging => _salvageUntil > 0f;
        public int ServerSalvageCount => Loaded && D.lifetime != null ? D.lifetime.salvages : 0;
        public int ServerRareSalvageCount => Loaded && D.lifetime != null ? D.lifetime.rareSalvages : 0;
        /// <summary>Server: letzter Grund, warum Bergen nicht ging oder kein Wurf kam (Tests).</summary>
        public string ServerSalvageNote { get; private set; } = "";

        public static bool IsHealerClass(CharacterClassDefinition c) => c != null && (c.roles & ClassRole.Healer) != 0;

        private bool SalvageClassOk => _inv.ServerModel != null && IsHealerClass(_inv.ServerModel.Class);

        // ================================================================== Freischalten (spaeter ueber die Klassenquest)

        /// <summary>Server: Nachlese lernen. Rueckgabe: Grund, falls es nicht geht (leer = in Ordnung).</summary>
        public string ServerUnlockSalvage()
        {
            if (!Loaded) return "Nicht bereit.";
            if (!SalvageClassOk) return "Nur ein Heiler (Aschenweber) lernt die Nachlese.";
            if (D.signatures == null) D.signatures = new List<string>();
            if (!D.signatures.Contains(SalvageSignature)) D.signatures.Add(SalvageSignature);
            _inv.ServerMarkDirty();
            SendSalvageState();
            SendMastery();
            if (Owner != null && Owner.IsActive)
                TargetMessage(Owner, "Du hast die Nachlese erlernt: Nach einem Kampf kannst du aus Besiegten bergen, was andere retten kann.");
            return "";
        }

        /// <summary>Server (Tests/Werkzeug): Nachlese wieder entfernen.</summary>
        public void ServerRemoveSalvage()
        {
            if (!Loaded || D.signatures == null) return;
            D.signatures.Remove(SalvageSignature);
            CancelSalvage("");
            _inv.ServerMarkDirty();
            SendSalvageState();
        }

        // ================================================================== Tod eines Gegners

        /// <summary>
        /// Server (NetworkEnemy beim Tod): Nachlese-Wuerfe fuer alle berechtigten Heiler unter den Empfaengern.
        /// <paramref name="stateOf"/> liefert den Zustand eines Koerperteils beim Tod (null = gibt es nicht).
        /// </summary>
        public static void ServerSalvageOnKill(IEnumerable<string> recipients, LootEligibility eligibility, EnemyDefinition def, int level,
            System.Func<string, BodyPartState?> stateOf, float enemyMaxHealth, CorpseLoot corpse, NetworkObject corpseObj, int seed)
        {
            if (def == null || def.salvage == null || !def.salvage.Active || corpse == null) return;
            int n = 0;
            foreach (string p in recipients)
            {
                n++;
                if (!TryGet(p, out NetworkProgression np) || !np.ServerSalvageUnlocked) continue;
                if (!np.SalvageClassOk) { np.ServerSalvageNote = "keine Heilerklasse"; continue; }
                float contrib = eligibility != null ? eligibility.ContributionOf(p) : 0f;
                float need = def.salvage.minContribution * Mathf.Max(1f, enemyMaxHealth);
                if (contrib < need)
                {
                    np.ServerSalvageNote = $"Beitrag zu klein ({contrib:0} von {need:0})";
                    continue;
                }
                int rank = MasteryCatalog.Salvage.RankFor(np.ServerMasteryPoints(MasteryCatalog.SalvageId));
                float rareBonus = MasteryCatalog.Salvage.Effect("rareChance", rank);
                var rng = new System.Random(unchecked(seed * 92821 + n * 68917 + p.GetHashCode()));
                SalvageRoller.Result r = SalvageRoller.Roll(def.salvage, stateOf, rareBonus, rng);
                corpse.SetSalvage(p, r.Items, r.Rare, level);
                np.ServerSalvageNote = r.Items.Count > 0 ? "Fund: " + JoinItems(r.Items) : "nichts gefunden";
                if (r.Items.Count > 0 && np.Owner != null && np.Owner.IsActive) np.TargetSalvageAvailable(np.Owner, corpseObj, true);
            }
        }

        private static string JoinItems(List<ItemStack> items)
        {
            var sb = new System.Text.StringBuilder();
            foreach (ItemStack s in items) { if (sb.Length > 0) sb.Append(','); sb.Append(s.ToString()); }
            return sb.ToString();
        }

        // ================================================================== Bergen

        /// <summary>Besitzer: diese Leiche bergen (Rechtsklick).</summary>
        public void RequestSalvage(GameObject corpse)
        {
            if (!IsOwner || corpse == null) return;
            NetworkObject nob = corpse.GetComponentInParent<NetworkObject>();
            if (nob != null) ServerSalvageRequest(nob);
        }

        [ServerRpc]
        private void ServerSalvageRequest(NetworkObject corpse) => ServerStartSalvage(corpse);

        /// <summary>Server: Bergen beginnen. Rueckgabe: Grund, falls es nicht geht (leer = in Ordnung).</summary>
        public string ServerStartSalvage(NetworkObject corpseObj)
        {
            string why = SalvageCheck(corpseObj, out CorpseLoot corpse);
            ServerSalvageNote = why;
            if (why.Length > 0)
            {
                if (Owner != null && Owner.IsActive) TargetMessage(Owner, why);
                return why;
            }
            if (ServerSalvaging) return "";
            int rank = MasteryCatalog.Salvage.RankFor(ServerMasteryPoints(MasteryCatalog.SalvageId));
            float secs = SalvageBaseSeconds * (1f - MasteryCatalog.Salvage.Effect("salvageSpeed", rank));
            _salvageUntil = Time.time + secs;
            _salvageStartedAt = Time.time;
            _salvageFrom = transform.position;
            _salvageCorpse = corpse;
            _salvageCorpseObj = corpseObj;
            if (Owner != null && Owner.IsActive) TargetSalvageCast(Owner, secs);
            return "";
        }

        /// <summary>Server: Dauer des Bergens beim aktuellen Rang (Tests).</summary>
        public float ServerSalvageSeconds()
        {
            int rank = MasteryCatalog.Salvage.RankFor(ServerMasteryPoints(MasteryCatalog.SalvageId));
            return SalvageBaseSeconds * (1f - MasteryCatalog.Salvage.Effect("salvageSpeed", rank));
        }

        private string SalvageCheck(NetworkObject corpseObj, out CorpseLoot corpse)
        {
            corpse = corpseObj != null ? corpseObj.GetComponent<CorpseLoot>() : null;
            if (!Loaded) return "Nicht bereit.";
            if (!ServerSalvageUnlocked || !SalvageClassOk) return "Du beherrschst die Nachlese nicht.";
            if (_health != null && !_health.IsAlive) return "Nicht jetzt.";
            if (ServerMounted) return "Nicht im Sattel.";
            if (corpse == null || !corpse.HasSalvageFor(_profile)) return "Hier gibt es für dich nichts zu bergen.";
            if (Vector3.Distance(corpseObj.transform.position, transform.position) > SalvageRange) return "Zu weit entfernt.";
            return "";
        }

        private void SalvageLogin()
        {
            if (!_salvageHooked && _health != null)
            {
                _health.Damaged += OnSalvageDamaged;
                _health.Died += OnSalvageDied;
                _salvageHooked = true;
            }
            _salvageUntil = -1f;
            SendSalvageState();
            // Nach dem Wiederverbinden: noch liegende Funde wieder anzeigen
            if (Owner == null || !Owner.IsActive) return;
            foreach (CorpseLoot c in CorpseLoot.All)
                if (c != null && c.HasSalvageFor(_profile))
                {
                    NetworkObject nob = c.GetComponent<NetworkObject>();
                    if (nob != null && nob.IsSpawned) TargetSalvageAvailable(Owner, nob, true);
                }
        }

        /// <summary>
        /// 21.10. (MEHRSPIELER-01): Server – diese Leiche ist fuer den Besitzer (wieder) sichtbar geworden (FishNet-Sicht: Rueckweg vom
        /// Wachtfeuer, Wiederverbinden). Der Client hat sie neu erzeugt und kennt das gruene Glimmen nicht mehr – erneut schicken,
        /// solange hier noch etwas zu bergen ist. Kein neues RPC, nur das vorhandene <see cref="TargetSalvageAvailable"/>.
        /// </summary>
        public void ServerNotifySalvage(NetworkObject corpseObj)
        {
            if (!Loaded || corpseObj == null || Owner == null || !Owner.IsActive) return;
            CorpseLoot c = corpseObj.GetComponent<CorpseLoot>();
            if (c != null && c.HasSalvageFor(_profile)) TargetSalvageAvailable(Owner, corpseObj, true);
        }

        private void SalvageStopServer()
        {
            if (!_salvageHooked || _health == null) return;
            _health.Damaged -= OnSalvageDamaged;
            _health.Died -= OnSalvageDied;
            _salvageHooked = false;
        }

        private void OnSalvageDamaged(DamageInfo info)
        {
            if (IsServerInitialized && ServerSalvaging) CancelSalvage("Bergen abgebrochen – du wurdest getroffen!");
        }

        private void OnSalvageDied()
        {
            if (IsServerInitialized && ServerSalvaging) CancelSalvage("");
        }

        private void SalvageUpdate()
        {
            if (!ServerSalvaging) return;
            Vector3 d = transform.position - _salvageFrom;
            d.y = 0f;
            if (_salvageCorpse == null || _salvageCorpseObj == null || !_salvageCorpseObj.IsSpawned) CancelSalvage("Bergen abgebrochen.");
            else if (d.sqrMagnitude > 0.35f * 0.35f) CancelSalvage("Bergen abgebrochen – du hast dich bewegt.");
            else if (_mCombat != null && _mCombat.ServerLastAttackTime > _salvageStartedAt) CancelSalvage("Bergen abgebrochen.");
            else if (Time.time >= _salvageUntil) CompleteSalvage();
        }

        private void CancelSalvage(string message)
        {
            if (!ServerSalvaging) return;
            _salvageUntil = -1f;
            _salvageCorpse = null;
            _salvageCorpseObj = null;
            if (Owner != null && Owner.IsActive)
            {
                TargetSalvageCastEnd(Owner, true);
                if (!string.IsNullOrEmpty(message)) TargetMessage(Owner, message);
            }
        }

        private void CompleteSalvage()
        {
            CorpseLoot corpse = _salvageCorpse;
            NetworkObject corpseObj = _salvageCorpseObj;
            _salvageUntil = -1f;
            _salvageCorpse = null;
            _salvageCorpseObj = null;
            string why = SalvageCheck(corpseObj, out _);
            if (why.Length > 0)
            {
                if (Owner != null && Owner.IsActive) { TargetSalvageCastEnd(Owner, true); TargetMessage(Owner, why); }
                return;
            }
            CorpseLoot.Salvage found = corpse.PeekSalvage(_profile);
            var rest = new List<ItemStack>();
            var got = new List<ItemStack>();
            foreach (ItemStack s in found.Items)
            {
                int taken = Model.Add(new ItemStack(s.id, s.count, false));
                if (taken > 0) got.Add(s.WithCount(taken));
                if (taken < s.count) rest.Add(s.WithCount(s.count - taken));
            }
            if (got.Count == 0)
            {
                if (Owner != null && Owner.IsActive) { TargetSalvageCastEnd(Owner, true); TargetMessage(Owner, "Kein Platz im Rucksack."); }
                return;
            }
            corpse.KeepSalvage(_profile, rest);
            _inv.ServerCommit();

            // Statistik und Meisterschaft (§48–§50): selten zaehlt dreifach, schwache Gegner lehren wenig bis nichts
            D.lifetime.salvages++;
            if (found.Rare) D.lifetime.rareSalvages++;
            float factor = MasteryCatalog.LevelFactor(found.EnemyLevel, ServerLevel);
            if (factor > 0f) AddMasteryPoints(MasteryCatalog.Salvage, (found.Rare ? 3.0 : 1.0) * factor);
            else { _mSaveDirty = true; if (!_mSendPending) { _mSendPending = true; _mSendTimer = 5f; } }
            _inv.ServerMarkDirty();
            ServerSalvageNote = "geborgen: " + JoinItems(got);

            if (Owner != null && Owner.IsActive)
            {
                TargetSalvageCastEnd(Owner, false);
                var names = new System.Text.StringBuilder();
                foreach (ItemStack s in got)
                {
                    if (names.Length > 0) names.Append(", ");
                    ItemDefinition def = GameData.Item(s.id);
                    names.Append(def != null ? def.DisplayName : s.id);
                    if (s.count > 1) names.Append(" ×").Append(s.count);
                }
                TargetSalvaged(Owner, JoinItems(got), "Geborgen: " + names + (found.Rare ? " – ein seltener Fund!" : ""));
                if (rest.Count == 0) TargetSalvageAvailable(Owner, corpseObj, false);
                SendSalvageState();
            }
        }

        private void SendSalvageState()
        {
            if (Owner == null || !Owner.IsActive || !Loaded) return;
            TargetSalvageState(Owner, ServerSalvageUnlocked, D.lifetime != null ? D.lifetime.salvages : 0, D.lifetime != null ? D.lifetime.rareSalvages : 0);
        }

        // ================================================================== Besitzer

        private void SalvageStartClient()
        {
            if (IsOwner) SalvageClient.Request = RequestSalvage;
        }

        private void SalvageStopClient()
        {
            if (IsOwner && SalvageClient.Request == (System.Action<GameObject>)RequestSalvage) SalvageClient.Request = null;
        }

        [TargetRpc]
        private void TargetSalvageAvailable(NetworkConnection conn, NetworkObject corpse, bool available)
        {
            if (corpse == null) return;
            LootIndicator li = corpse.GetComponent<LootIndicator>();
            if (li != null) li.SetSalvage(available);
        }

        [TargetRpc]
        private void TargetSalvageCast(NetworkConnection conn, float seconds) => CastBarState.Begin("Bergen", seconds);

        [TargetRpc]
        private void TargetSalvageCastEnd(NetworkConnection conn, bool interrupted) => CastBarState.End(interrupted);

        [TargetRpc]
        private void TargetSalvageState(NetworkConnection conn, bool unlocked, int salvages, int rare) =>
            SalvageClient.Apply(unlocked, salvages, rare);

        [TargetRpc]
        private void TargetSalvaged(NetworkConnection conn, string items, string message)
        {
            SalvageClient.RaiseSalvaged(items);
            ProgressionClient.RaiseMessage(message);
        }
    }
}
