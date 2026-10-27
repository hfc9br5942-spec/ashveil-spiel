using Ashveil.Gameplay.Quests;
using Ashveil.Gameplay.Story;
using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Die Herde einer Geschichte als EIN Netz-Objekt (Vorgabe: keine 20 Einzelobjekte): Zustand, Routenfortschritt und ein
    /// paar Schalter als SyncVars – jedes Tier rechnet der Client selbst aus (<see cref="HerdPresenter"/>). Sichtbar nur fuer
    /// den Besitzer der Geschichte und seine Gruppe in der Naehe (<see cref="StoryPhaseCondition"/>). Das Fohlen stirbt nie:
    /// die Herde hat kein Leben – das Raubtier setzt sie nur unter Druck (Panik), die Begegnung setzt sich dann zurueck.
    /// </summary>
    public sealed class HerdEncounter : NetworkBehaviour
    {
        public const byte KindFoalHerd = 0, KindAdultHerd = 1;

        private readonly SyncVar<string> _owner = new SyncVar<string>("");
        private readonly SyncVar<NetworkObject> _ownerObject = new SyncVar<NetworkObject>();
        private readonly SyncVar<byte> _kind = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _mode = new SyncVar<byte>(0);
        private readonly SyncVar<float> _progress = new SyncVar<float>(0f);
        private readonly SyncVar<bool> _foalStuck = new SyncVar<bool>(false);
        private readonly SyncVar<bool> _sniff = new SyncVar<bool>(false);
        private readonly SyncVar<bool> _away = new SyncVar<bool>(false);
        private readonly SyncVar<Vector3> _awayPoint = new SyncVar<Vector3>(Vector3.zero);
        private readonly SyncVar<Vector3> _center = new SyncVar<Vector3>(Vector3.zero);
        private readonly SyncVar<Vector3> _facing = new SyncVar<Vector3>(Vector3.forward);
        private readonly SyncVar<float> _radius = new SyncVar<float>(16f);
        private readonly SyncVar<byte> _releaseCount = new SyncVar<byte>(0);
        private readonly SyncVar<bool> _handReady = new SyncVar<bool>(false);
        private readonly SyncVar<bool> _relaxed = new SyncVar<bool>(false);

        private HerdPresenter _presenter;
        // Werte, die der Server vor dem Spawn setzt: FishNet 4.7 erlaubt SyncVars erst nach der Netz-Initialisierung.
        // Sie werden in OnStartServer uebernommen – vor OnStartClient, damit auch der Host die Herde richtig aufbaut.
        private readonly List<Action> _beforeSpawn = new List<Action>();
        private byte _shownRelease;

        public string OwnerProfile => _owner.Value;
        public byte Kind => _kind.Value;
        public HerdMode Mode => (HerdMode)_mode.Value;
        public float Progress => _progress.Value;
        public bool FoalStuck => _foalStuck.Value;
        public bool Sniffing => _sniff.Value;
        public bool StoryHorseAway => _away.Value;
        public bool HandReady => _handReady.Value;
        public Vector3 Center => _center.Value;
        public HerdPresenter Presenter => _presenter;

        // ================================================================== Server

        /// <summary>Sofort setzen, wenn schon im Netz – sonst bis OnStartServer aufheben.</summary>
        private void Set(Action apply)
        {
            if (IsServerInitialized) apply();
            else _beforeSpawn.Add(apply);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            foreach (Action a in _beforeSpawn) a();
            _beforeSpawn.Clear();
        }

        public void ServerInit(string ownerProfile, NetworkObject ownerObject, byte kind, Vector3 center, float radius, Vector3 facing)
        {
            Vector3 face = facing.sqrMagnitude > 1e-4f ? facing.normalized : Vector3.forward;
            Set(() =>
            {
                _owner.Value = ownerProfile ?? "";
                _ownerObject.Value = ownerObject;
                _kind.Value = kind;
                _center.Value = center;
                _radius.Value = radius;
                _facing.Value = face;
            });
            transform.position = center;
        }

        public void ServerSetMode(HerdMode mode) => Set(() => _mode.Value = (byte)mode);
        public void ServerSetProgress(float meters) => Set(() => _progress.Value = meters);
        public void ServerSetFoalStuck(bool stuck) => Set(() => _foalStuck.Value = stuck);
        public void ServerSetSniff(bool on) => Set(() => _sniff.Value = on);
        public void ServerSetHandReady(bool on) => Set(() => _handReady.Value = on);
        public void ServerSetRelaxed(bool on) => Set(() => _relaxed.Value = on);

        public void ServerSetPlace(Vector3 center, float radius, Vector3 facing)
        {
            Set(() =>
            {
                _center.Value = center;
                _radius.Value = radius;
                if (facing.sqrMagnitude > 1e-4f) _facing.Value = facing.normalized;
            });
            transform.position = center;
        }

        /// <summary>Story-Pferd abseits der Herde an einen Punkt (Weidegang: haelt, dreht um, kommt zurueck).</summary>
        public void ServerSetStoryHorseAway(bool away, Vector3 point)
        {
            Set(() =>
            {
                _away.Value = away;
                _awayPoint.Value = point;
            });
        }

        /// <summary>Weidegang: Tor auf – die Herde laeuft hinaus.</summary>
        public void ServerRelease()
        {
            Set(() =>
            {
                _mode.Value = (byte)HerdMode.Released;
                _releaseCount.Value = (byte)(_releaseCount.Value + 1);
            });
        }

        /// <summary>Ungefaehre Position der Herde (Server, fuer Sprungziele des Raubtiers).</summary>
        public Vector3 ServerHerdPoint(int index)
        {
            Vector3 back = _facing.Value;
            Vector3 side = Vector3.Cross(Vector3.up, back);
            float r = _radius.Value;
            float a = index * 2.399f;
            return _center.Value + back * (r * 0.45f) + side * (Mathf.Cos(a) * r * 0.4f);
        }

        // ================================================================== Client

        public override void OnStartClient()
        {
            base.OnStartClient();
            var go = new GameObject("Herde (Darstellung)");
            go.transform.SetParent(transform, false);
            _presenter = go.AddComponent<HerdPresenter>();
            bool foal = _kind.Value == KindFoalHerd;
            _presenter.Build(foal ? 6 : 5, true, foal, foal ? 4711 : 4712);
            Sync();
            _presenter.SnapNow();
            _shownRelease = _releaseCount.Value;
            StoryDirector.RegisterFocus(foal ? "foal" : "horse", () => _presenter != null && _presenter.FoalIndex >= 0 ? _presenter.Shown(_presenter.FoalIndex) + Vector3.up * 1.2f : (Vector3?)null);
            StoryDirector.RegisterFocus("herd", () => _presenter != null ? _presenter.Shown(0) + Vector3.up * 1.5f : (Vector3?)null);
            StoryDirector.Cue += OnCue;
            HorseRig story = _presenter.Rig(_presenter.FoalIndex);
            if (story != null)
            {
                StoryInteractable.Add(story.gameObject, "free_foal", "story.horse.action.free", 4.5f, new Vector3(1.2f, 1.6f, 1.8f), new Vector3(0f, 0.8f, 0f),
                    () => IsOwner && _foalStuck.Value);
                var hand = story.gameObject.AddComponent<StoryInteractable>();
                hand.Configure("hand", "story.horse.action.hand", 4f, () => IsOwner && _handReady.Value);
            }
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            StoryDirector.Cue -= OnCue;
            StoryDirector.UnregisterFocus("foal");
            StoryDirector.UnregisterFocus("horse");
            StoryDirector.UnregisterFocus("herd");
        }

        /// <summary>Szenen-Ausloeser des Besitzers an den Server weiterreichen (alle Beteiligten sehen dasselbe).</summary>
        private void OnCue(string cue)
        {
            if (!IsOwner) return;
            switch (cue)
            {
                case "foal_sniff":
                case "foal_back":
                case "horse_notice":
                case "horse_approach":
                case "herd_release":
                case "horse_stop":
                case "horse_turn":
                case "horse_return":
                case "horse_sniff_hand":
                case "horse_relax":
                    StoryClient.Send("cue", cue);
                    break;
            }
        }

        private void Update()
        {
            if (_presenter == null) return;
            Sync();
        }

        private void Sync()
        {
            _presenter.Mode = (HerdMode)_mode.Value;
            _presenter.RouteProgress = _progress.Value;
            _presenter.Center = _center.Value;
            _presenter.Radius = _radius.Value;
            _presenter.Facing = _facing.Value;
            if (_presenter.Route == null) _presenter.Route = HerdRoute.Find(_kind.Value == KindFoalHerd ? "herd.return" : "herd.release");
            _presenter.FoalStuck = _foalStuck.Value;
            _presenter.FoalStuckPoint = StoryAnchor.TryGet("horse.foal", out Vector3 fp) ? fp : _center.Value;
            Transform owner = _ownerObject.Value != null ? _ownerObject.Value.transform : null;
            _presenter.SniffTarget = _sniff.Value ? owner : null;
            _presenter.StoryHorseAway = _away.Value;
            _presenter.StoryHorsePoint = _awayPoint.Value;
            _presenter.LookAt = owner;
            if (_releaseCount.Value != _shownRelease)
            {
                _shownRelease = _releaseCount.Value;
                _presenter.StartRelease();
            }
            _presenter.StoryHorseRelaxed = _relaxed.Value;
        }
    }
}
