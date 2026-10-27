using System;
using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Visuals;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Aussehen eines Spielers im Netz: Koerper, Hautton, Frisur, Haarfarbe, Bart, Helm/Umhang ausblenden,
    /// Faerbungen und (spaeter) kosmetische Uebertragung – als kurzer Text in einer SyncVar. Spaeter Beitretende
    /// bekommen den Stand sofort. Der Server prueft jede Aenderung (nur vorhandene Optionen, Paletten, Freischaltung,
    /// Kompatibilitaet) und speichert sie mit dem Charakter.
    /// </summary>
    [RequireComponent(typeof(EquipmentVisuals))]
    [DisallowMultipleComponent]
    public sealed class NetworkAppearance : NetworkBehaviour, IAppearanceService
    {
        private readonly SyncVar<string> _look = new SyncVar<string>(string.Empty);

        private EquipmentVisuals _visuals;
        private CharacterBodySwitcher _bodies;
        private NetworkInventory _inventory;
        private string _applied;
        private CharacterAppearanceData _current = new CharacterAppearanceData();

        /// <summary>Aktuelles Aussehen (wie vom Server bestaetigt).</summary>
        public CharacterAppearanceData Current => _current;
        public event Action Changed;

        /// <summary>Eigener Spieler (fuer die Oberflaeche).</summary>
        public static NetworkAppearance Local { get; private set; }

        private void Awake()
        {
            _visuals = GetComponent<EquipmentVisuals>();
            _bodies = GetComponent<CharacterBodySwitcher>();
            _inventory = GetComponent<NetworkInventory>();
            _look.OnChange += OnLookChanged;
        }

        private void OnDestroy()
        {
            _look.OnChange -= OnLookChanged;
            if (Local == this) Local = null;
            AppearanceClient.Unregister(this);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!string.IsNullOrEmpty(_look.Value)) ApplyLocal(_look.Value);
            else SetHidden(true); // M7: noch kein Charakter (Editor offen) – Figur unsichtbar
            if (IsOwner)
            {
                Local = this;
                AppearanceClient.Register(this);
                if (_bodies != null) _bodies.BodyChangeRequested += OnBodyKey;
            }
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (_bodies != null) _bodies.BodyChangeRequested -= OnBodyKey;
            if (Local == this) Local = null;
            AppearanceClient.Unregister(this);
        }

        private void OnLookChanged(string prev, string next, bool asServer) => ApplyLocal(next);

        private void ApplyLocal(string encoded)
        {
            if (string.IsNullOrEmpty(encoded) || encoded == _applied) return;
            _applied = encoded;
            if (_hidden) SetHidden(false);
            _current = CharacterAppearanceData.Decode(encoded);
            if (_visuals != null) _visuals.SetAppearance(_current);
            Changed?.Invoke();
            if (Local == this) AppearanceClient.RaiseChanged();
        }

        private bool _hidden;

        /// <summary>Unsichtbar, solange der Spieler noch keinen Charakter hat (kein Aussehen vom Server).</summary>
        public bool Hidden => _hidden;

        private void SetHidden(bool hidden)
        {
            _hidden = hidden;
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.GetComponentInParent<Ashveil.World.WorldLabel>() != null) continue;
                r.forceRenderingOff = hidden;
            }
        }

        // ================================================================== Client -> Server

        /// <summary>Bitte um neues Aussehen. Der Server korrigiert Ungueltiges und verteilt das Ergebnis.</summary>
        public void RequestChange(CharacterAppearanceData wanted)
        {
            if (wanted == null || !IsClientInitialized) return;
            ServerSetLook(wanted.Encode());
        }

        /// <summary>F2: anderer Koerper – mit einer passenden Frisur.</summary>
        private void OnBodyKey(int nextBody)
        {
            CharacterAppearanceData d = _current.Clone();
            d.body = nextBody;
            AppearanceCatalog cat = GameDataBootstrap.Catalog;
            if (cat != null)
            {
                d.hair = FirstStyle(cat.hairstyles, cat, nextBody);
                if (!cat.StyleAllowed(cat.beards, d.beard, nextBody)) d.beard = 0;
                d.skin = Mathf.Min(d.skin, cat.SkinCount(nextBody) - 1);
                if (!cat.FeatureAllowed(d.feature, nextBody)) d.feature = 0;
            }
            RequestChange(d);
        }

        public static int FirstStyle(List<AppearanceCatalog.StyleEntry> list, AppearanceCatalog cat, int body) =>
            AppearanceDefaults.FirstStyle(list, cat, body);

        [ServerRpc]
        private void ServerSetLook(string encoded)
        {
            if (_inventory == null || !_inventory.IsReady) return;
            if (encoded == null || encoded.Length > 1024) return;
            CharacterAppearanceData d = CharacterAppearanceData.Decode(encoded);
            ServerApply(d, true);
        }

        // ================================================================== Server

        /// <summary>Server: pruefen, uebernehmen, verteilen, speichern.</summary>
        public void ServerApply(CharacterAppearanceData d, bool save)
        {
            if (d == null) d = new CharacterAppearanceData();
            d.EnsureArrays();
            AppearanceCatalog cat = GameDataBootstrap.Catalog;
            if (cat != null && cat.Sanitize(d)) Debug.Log("[Ashveil] Aussehen: ungueltige Angaben korrigiert.");
            if (_inventory != null && _inventory.ServerData != null) d.race = RaceCatalog.IndexOf(_inventory.ServerData.race); // Volk steht fest
            ValidateTransmog(d);
            string enc = d.Encode();
            _look.Value = enc;
            ApplyLocal(enc); // Host/Server sofort
            if (save && _inventory != null) _inventory.ServerStoreAppearance(d);
        }

        private void ValidateTransmog(CharacterAppearanceData d)
        {
            if (_inventory == null || _inventory.ServerModel == null) return;
            InventoryModel m = _inventory.ServerModel;
            for (int i = 0; i < d.transmog.Length; i++)
            {
                if (string.IsNullOrEmpty(d.transmog[i])) continue;
                ItemStack worn = m.Equipment[i];
                ItemDefinition wornDef = worn.IsEmpty ? null : GameData.Item(worn.id);
                ItemDefinition src = GameData.Item(d.transmog[i]);
                if (!Transmog.Allowed(wornDef, src, m.Class, _inventory.UnlockedAppearances)) d.transmog[i] = null;
            }
        }
    }
}
