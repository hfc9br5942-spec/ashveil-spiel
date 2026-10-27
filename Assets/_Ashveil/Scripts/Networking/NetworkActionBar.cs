using Ashveil.Data;
using Ashveil.Gameplay.Controls;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Netzwerk-Adapter der Actionbar (M6.6). Die Belegung gehoert zum Charakter und liegt im Speicherstand (Server).
    /// Der Besitzer bittet um Aenderungen (belegen, leeren, tauschen); der Server prueft sie und schickt den neuen Stand.
    ///
    /// Geprueft wird beim Belegen: Platz gueltig, Typ erlaubt, Faehigkeit existiert, Gegenstand existiert, ist
    /// benutzbar und liegt im eigenen Rucksack. Das Ausloesen laeuft ueber die bestehenden, serverseitig geprueften
    /// Wege (Kampf ueber NetworkCombat/Vorhersage, Gegenstaende ueber NetworkInventory.UseItem).
    /// </summary>
    [RequireComponent(typeof(NetworkInventory))]
    [DisallowMultipleComponent]
    public sealed class NetworkActionBar : NetworkBehaviour, IActionBarService
    {
        private NetworkInventory _inv;

        // Client (Besitzer)
        private readonly ActionBarEntry[] _cSlots = ActionBarLayout.ToSlots(null);
        private bool _cReady;
        private int _cRev;

        // Server
        private int _rev;

        /// <summary>Server: letzte Ablehnung (Tests).</summary>
        public string ServerLastError { get; private set; } = "";

        private void Awake() => _inv = GetComponent<NetworkInventory>();

        public override void OnStartServer()
        {
            base.OnStartServer();
            _inv.ServerLoggedIn += OnServerLogin;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            _inv.ServerLoggedIn -= OnServerLogin;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (IsOwner) ActionBarClient.Register(this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (IsOwner) ActionBarClient.Unregister(this);
        }

        private void OnDestroy() => ActionBarClient.Unregister(this);

        // ================================================================== Server

        private CharacterSaveData D => _inv != null ? _inv.ServerData : null;
        private bool Loaded => _inv != null && _inv.IsReady && D != null;

        /// <summary>Server: aktuelle Belegung (Tests).</summary>
        public ActionBarEntry[] ServerSlots => Loaded ? ActionBarLayout.ToSlots(D.actionBar) : ActionBarLayout.ToSlots(null);

        private void OnServerLogin()
        {
            if (!Loaded) return;
            D.actionBar = ActionBarLayout.Sanitize(D.actionBar);
            Send("");
        }

        private void Send(string error)
        {
            ServerLastError = error ?? "";
            if (!Loaded || Owner == null || !Owner.IsActive) return;
            _rev++;
            TargetBar(Owner, _rev, D.actionBar.ToArray(), error ?? "");
        }

        private void Commit()
        {
            D.actionBar = ActionBarLayout.Sanitize(D.actionBar);
            _inv.ServerMarkDirty();
            Send("");
        }

        private static bool ValidPlace(int page, int slot) =>
            page >= 0 && page < ActionBarLayout.Pages && slot >= 0 && slot < ActionBarLayout.SlotsPerPage;

        /// <summary>Server: darf dieser Inhalt auf die Leiste? Leer = ja, sonst der Grund.</summary>
        public string ServerCheck(ActionEntryType type, string id)
        {
            if (!ActionBarLayout.Supported(type)) return "Das kann man (noch) nicht auf die Leiste legen.";
            if (string.IsNullOrEmpty(id) || id.Length > ActionBarLayout.MaxIdLength) return "Unbekannte Aktion.";
            if (type == ActionEntryType.Ability)
                return GameData.Get<AbilityDefinition>(id) != null ? "" : "Unbekannte Fähigkeit.";
            if (type == ActionEntryType.Emote)
                return EmoteCatalog.TryGetByKey(id, out _) ? "" : "Unbekanntes Emote.";
            if (type == ActionEntryType.Spell)
            {
                if (GameData.Get<SpellDefinition>(id) == null) return "Unbekannte Fähigkeit.";
                if (D.spells != null) foreach (SpellSave s in D.spells) if (s.id == id && s.rank > 0) return "";
                return "Diese Fähigkeit hast du noch nicht gelernt.";
            }
            if (type == ActionEntryType.Companion)
            {
                if (id == CompanionCatalog.DominateId)
                    return D.signatures != null && D.signatures.Contains(NetworkProgression.DominateSignature) ? "" : "Du beherrschst das Dominieren nicht.";
                if (id == CompanionCatalog.CallId) return D.companion != null ? "" : "Du hast keinen Gefährten.";
                return "Unbekannte Aktion.";
            }
            if (type == ActionEntryType.Mount)
            {
                if (MountCatalog.Get(id) == null) return "Unbekanntes Reittier.";
                return D.mount != null && D.mount.id == id ? "" : "Dieses Reittier hast du nicht.";
            }
            ItemDefinition def = GameData.Item(id);
            if (def == null) return "Unbekannter Gegenstand.";
            if (def.category != ItemCategory.Consumable || def.IsEquippable) return "Diesen Gegenstand kann man nicht benutzen.";
            if (_inv.ServerModel == null) return "Nicht bereit.";
            foreach (ItemStack s in _inv.ServerModel.Bag) if (!s.IsEmpty && s.id == id) return "";
            return "Den Gegenstand hast du nicht.";
        }

        [ServerRpc]
        private void ServerAssign(byte page, byte slot, byte type, string id) => AssignServer(page, slot, (ActionEntryType)type, id);

        /// <summary>Server: Platz belegen (ueberschreibt, was dort lag). Auch fuer Tests.</summary>
        public bool AssignServer(int page, int slot, ActionEntryType type, string id)
        {
            if (!Loaded) return false;
            if (!ValidPlace(page, slot)) { Send("Ungültiger Platz."); return false; }
            string why = ServerCheck(type, id);
            if (why.Length > 0) { Send(why); return false; }
            D.actionBar.RemoveAll(e => e.page == page && e.slot == slot);
            D.actionBar.Add(new ActionBarEntry(type, id, page, slot));
            Commit();
            return true;
        }

        [ServerRpc]
        private void ServerClear(byte page, byte slot) => ClearServer(page, slot);

        /// <summary>Server: auf den ersten freien Platz legen, falls es noch nirgends liegt (neues Reittier, spaeter Zauber).</summary>
        public bool ServerPlaceIfMissing(ActionEntryType type, string id)
        {
            if (!Loaded) return false;
            foreach (ActionBarEntry e in D.actionBar) if (e.type == type && e.referenceId == id) return true;
            ActionBarEntry[] slots = ActionBarLayout.ToSlots(D.actionBar);
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].IsEmpty) return AssignServer(i / ActionBarLayout.SlotsPerPage, i % ActionBarLayout.SlotsPerPage, type, id);
            return false;
        }

        public bool ClearServer(int page, int slot)
        {
            if (!Loaded) return false;
            if (!ValidPlace(page, slot)) { Send("Ungültiger Platz."); return false; }
            D.actionBar.RemoveAll(e => e.page == page && e.slot == slot);
            Commit();
            return true;
        }

        [ServerRpc]
        private void ServerSwap(byte pageA, byte slotA, byte pageB, byte slotB) => SwapServer(pageA, slotA, pageB, slotB);

        public bool SwapServer(int pageA, int slotA, int pageB, int slotB)
        {
            if (!Loaded) return false;
            if (!ValidPlace(pageA, slotA) || !ValidPlace(pageB, slotB)) { Send("Ungültiger Platz."); return false; }
            if (pageA == pageB && slotA == slotB) { Send(""); return true; }
            ActionBarEntry[] s = ActionBarLayout.ToSlots(D.actionBar);
            ActionBarEntry a = s[pageA * ActionBarLayout.SlotsPerPage + slotA];
            ActionBarEntry b = s[pageB * ActionBarLayout.SlotsPerPage + slotB];
            D.actionBar.RemoveAll(e => (e.page == pageA && e.slot == slotA) || (e.page == pageB && e.slot == slotB));
            if (!a.IsEmpty) D.actionBar.Add(new ActionBarEntry(a.type, a.referenceId, pageB, slotB));
            if (!b.IsEmpty) D.actionBar.Add(new ActionBarEntry(b.type, b.referenceId, pageA, slotA));
            Commit();
            return true;
        }

        // ================================================================== Client (Besitzer)

        public bool Ready => _cReady;
        public ActionBarEntry[] Slots => _cSlots;
        public int Revision => _cRev;
        public string LastError { get; private set; } = "";

        public void Assign(int page, int slot, ActionEntryType type, string referenceId)
        {
            if (!_cReady || referenceId == null || referenceId.Length > ActionBarLayout.MaxIdLength) return;
            ServerAssign((byte)Mathf.Clamp(page, 0, 255), (byte)Mathf.Clamp(slot, 0, 255), (byte)type, referenceId);
        }

        public void Clear(int page, int slot)
        {
            if (_cReady) ServerClear((byte)Mathf.Clamp(page, 0, 255), (byte)Mathf.Clamp(slot, 0, 255));
        }

        public void Swap(int pageA, int slotA, int pageB, int slotB)
        {
            if (_cReady) ServerSwap((byte)Mathf.Clamp(pageA, 0, 255), (byte)Mathf.Clamp(slotA, 0, 255), (byte)Mathf.Clamp(pageB, 0, 255), (byte)Mathf.Clamp(slotB, 0, 255));
        }

        [TargetRpc]
        private void TargetBar(NetworkConnection conn, int rev, ActionBarEntry[] entries, string error)
        {
            ActionBarEntry[] slots = ActionBarLayout.ToSlots(entries);
            System.Array.Copy(slots, _cSlots, _cSlots.Length);
            _cRev = rev;
            _cReady = true;
            LastError = error ?? "";
            if (!string.IsNullOrEmpty(error)) Ashveil.Core.Events.CombatTextEvent.Error(error);
            ActionBarClient.RaiseChanged();
        }
    }
}
