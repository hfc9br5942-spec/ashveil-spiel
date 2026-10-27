using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Quests;
using Ashveil.Gameplay.Story;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Pferde-Geschichte des menschlichen Tanks – „Wo die Herde floh“ (Q1) und „Der letzte Weidegang“ (Q2).
    /// Serverautoritaer: Zugang, Suchschritte, Kanaele (Fohlen befreien, Weidetor, Hand hinhalten), Begegnung, Zuruecksetzen,
    /// Freischaltung, Speichern. Der logische Zustand steht im Speicherstand (<see cref="HorseStorySave"/>); alles Sichtbare
    /// (Hof, Farmer, Herde auf der Weide, Hinweise) leitet der Client aus dem Stand ab, den er hier bekommt (Phasing).
    ///
    /// Schritte (horseStory.step):
    ///   Q1  0–5  Suchgebiet k als naechstes · 6 alle Hinweise, Senke suchen · 10 Fohlen haengt fest · 12 Kampf
    ///       20 Heimweg (herdRouteProgress) · 30 am Hof (Szene, Abgabe)
    ///   Q2  0 Wiedererkennen/Angebot · 1 Bedrohungen · 5 Tor · 6 Freilassen (Szene) · 7 Hand · 8 Bindung (Szene)
    ///       90 Schluss-Szene · 99 er ist im Haus
    /// </summary>
    public sealed partial class NetworkProgression
    {
        // ---- Schritte
        private const int StepAllClues = 6, StepFoalStuck = 10, StepFight = 12, StepReturning = 20, StepHome = 30;
        private const int Q2Offer = 0, Q2Threats = 1, Q2Gate = 5, Q2Releasing = 6, Q2Hand = 7, Q2Bonding = 8, Q2Final = 90, Q2Inside = 99;
        private const int ClueCount = 6;

        // ---- Kennungen in der Szene (StoryAnchor / HerdRoute)
        public const string AnchorYard = "horse.yard", AnchorBasin = "horse.basin", AnchorFoal = "horse.foal", AnchorBasinEntry = "horse.basin_entry",
            AnchorPasture = "horse.pasture", AnchorGate = "horse.gate", AnchorBigPasture = "horse.bigpasture";
        public const string AnchorThreatWolves = "horse.threat.wolves", AnchorThreatSpitters = "horse.threat.spitters", AnchorThreatLone = "horse.threat.lone";
        public const string RouteReturn = "herd.return";
        public static string ClueAnchor(int i) => "horse.clue" + (i + 1);

        // ---- Zielkennungen (Zieltyp Story)
        public const string ObjSearch = "story.horse.search", ObjFoal = "story.horse.foal", ObjPredator = "story.horse.predator", ObjReturn = "story.horse.return";
        public const string ObjWolves = "story.horse.q2.wolves", ObjSpitters = "story.horse.q2.spitters", ObjLone = "story.horse.q2.lone",
            ObjGate = "story.horse.q2.gate", ObjHand = "story.horse.q2.hand";

        // ---- Begegnung
        private const float FreeFoalSeconds = 3.5f, GateSeconds = 2.0f, HandSeconds = 1.8f;
        private const float HerdSpeed = 3.1f, HerdWaitDistance = 42f;
        private const float StressPerLeap = 34f, StressDecay = 2.5f;

        private static readonly Dictionary<string, HerdEncounter> HerdsByProfile = new Dictionary<string, HerdEncounter>();

        private HerdEncounter _herd;
        private EnemyBrain _predator;
        private readonly List<(EnemyBrain brain, string obj)> _threats = new List<(EnemyBrain, string)>();
        private float _hsTick, _hsSaveMeters, _stress, _nextLeap, _deadSince = -1f, _lastWaitMsg = -99f, _sniffUntil = -1f;
        private float _hsCastUntil = -1f, _hsCastStart;
        private Vector3 _hsCastFrom;
        private string _hsCastAction = "";
        private bool _hsHooked;
        private string _lastStorySent;
        private HorseStorySave H => D.horseStory;

        /// <summary>Server: Stand der Pferde-Geschichte (Tests).</summary>
        public HorseStoryState ServerHorseState => Loaded && H != null ? H.State : HorseStoryState.Unknown;
        public int ServerHorseStep => Loaded && H != null ? H.step : 0;
        public float ServerHerdProgress => Loaded && H != null ? H.herdRouteProgress : 0f;
        public float ServerHerdStress => _stress;
        public HerdEncounter ServerHerd => _herd;
        public EnemyBrain ServerPredator => _predator;
        public IReadOnlyList<(EnemyBrain brain, string obj)> ServerThreats => _threats;
        public string ServerStoryNote { get; private set; } = "";
        public string ServerLastScene { get; private set; } = "";

        // ================================================================== Lebenszyklus

        private void HorseStoryLogin()
        {
            if (!_hsHooked && _health != null)
            {
                _health.Damaged += OnStoryDamaged;
                _health.Died += OnStoryDied;
                _hsHooked = true;
            }
            _hsCastUntil = -1f;
            _stress = 0f;
            if (H == null) D.horseStory = new HorseStorySave();
            // Wiederverbinden mitten in der Begegnung: zurueck an den Anfang (Fohlen haengt wieder fest) – nie ein Fehlschlag
            if (H.State == HorseStoryState.FoalRescued && H.step == StepFight) SetHorse(HorseStoryState.FoalFound, StepFoalStuck);
            if (H.State == HorseStoryState.AdultAtFarm && (H.step == Q2Releasing || H.step == Q2Bonding)) H.step = H.step == Q2Releasing ? Q2Gate : Q2Hand;
            // 22.10. (STORY-01): Altstaende – Q1/Q2 frueher aufgegeben, Stand blieb mitten in der Geschichte haengen → wieder anbietbar
            bool q1Reset = ResetAbandonedHorseQuest(HorseStoryCatalog.FoalRescueQuest);
            bool q2Reset = ResetAbandonedHorseQuest(HorseStoryCatalog.LastPastureQuest);
            if (q1Reset || q2Reset) _inv.ServerMarkDirty();
            // 22.10. (STORY-02): Schluss-Szene q2_final unterbrochen (Trennen, Absturz, andere Szene) – Pferd, EP, Geld und Abschluss
            // sind schon gebucht und gespeichert (CompleteLastPasture), nur der Schritt blieb auf 90: kein Epilog, Zuechter nie im Haus.
            // Wie scene_done "q2_final" fortsetzen, vor PromoteHorseStory (Epilog-Pruefung im selben Login). Kein ReportQuestReward:
            // „+9000 EP“ beim Einloggen waere falsch (laengst gebucht) – nur der Reittier-Hinweis, falls es nicht auf der Leiste liegt.
            if (H.State == HorseStoryState.BondedToPlayer && H.step == Q2Final)
            {
                H.step = Q2Inside;
                _inv.ServerMarkDirty();
                if (!ServerMountOnBar() && Owner != null && Owner.IsActive) TargetMessage(Owner, Loc.T("story.horse.mount_menu"));
            }
            PromoteHorseStory(true);
            DespawnStoryObjects();
            _lastStorySent = null;
            SendStory();
        }

        private void HorseStoryStopServer()
        {
            DespawnStoryObjects();
        }

        private void OnStoryDamaged(DamageInfo info)
        {
            if (IsServerInitialized && _hsCastUntil > 0f) CancelStoryCast(Loc.T("story.horse.cast_hit"));
        }

        private void OnStoryDied()
        {
            if (IsServerInitialized && _hsCastUntil > 0f) CancelStoryCast("");
        }

        /// <summary>Zeitabhaengige Uebergaenge: Q2 wird verfuegbar (erwachsenes Pferd), Epilog nach der Bindung.</summary>
        private bool PromoteHorseStory(bool login)
        {
            bool changed = false;
            if (H.State == HorseStoryState.Growing && Q2Unlocked())
            {
                SetHorse(HorseStoryState.AdultAtFarm, Q2Offer);
                changed = true;
            }
            // Epilog verzoegert (Vorgabe): erst beim naechsten Login UND mit Abstand – nie direkt nach der Quest
            if (login && H.State == HorseStoryState.BondedToPlayer && !H.epilogue && H.step >= Q2Inside && HoursSince(H.bondedAtUtc) >= HorseStoryCatalog.EpilogueDelayHours)
            {
                H.epilogue = true;
                changed = true;
            }
            if (changed) _inv.ServerMarkDirty();
            return changed;
        }

        private static double HoursSince(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return 0;
            return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime t)
                ? (DateTime.UtcNow - t.ToUniversalTime()).TotalHours
                : 0;
        }

        private static string NowUtc() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        // ================================================================== Zugang

        private bool Q1Gate(out string why)
        {
            QuestDefinition q = GameData.Quest(HorseStoryCatalog.FoalRescueQuest);
            why = q == null ? "Auftrag fehlt." : QuestRules.CheckGate(q, ServerGate());
            if (why == null && Model.Level < q.minLevel) why = $"Benötigt Stufe {q.minLevel}.";
            return why == null;
        }

        private bool Q2Unlocked()
        {
            QuestDefinition q = GameData.Quest(HorseStoryCatalog.LastPastureQuest);
            if (q == null || !D.completedQuests.Contains(HorseStoryCatalog.FoalRescueQuest)) return false;
            return QuestRules.CheckGate(q, ServerGate()) == null && Model.Level >= q.minLevel;
        }

        /// <summary>Welchen Geschichtsauftrag eine Szene gerade anbietet (leer = keinen).</summary>
        private string ServerStoryOffer()
        {
            if (!Loaded || H == null) return "";
            if (H.State == HorseStoryState.Unknown && D.HasFlag("story.horse.q1_offered") && !ServerIsActive(HorseStoryCatalog.FoalRescueQuest) &&
                !ServerIsCompleted(HorseStoryCatalog.FoalRescueQuest))
                return HorseStoryCatalog.FoalRescueQuest;
            if (H.State == HorseStoryState.AdultAtFarm && H.step == Q2Offer && D.HasFlag("story.horse.q2_offered") && !ServerIsActive(HorseStoryCatalog.LastPastureQuest) &&
                !ServerIsCompleted(HorseStoryCatalog.LastPastureQuest))
                return HorseStoryCatalog.LastPastureQuest;
            return "";
        }

        private FarmPhase ServerFarmPhase()
        {
            if (H == null) return FarmPhase.Normal;
            if (H.epilogue) return FarmPhase.Epilogue;
            switch (H.State)
            {
                case HorseStoryState.Unknown: return Q1Gate(out _) ? FarmPhase.Crisis : FarmPhase.Normal;
                case HorseStoryState.FoalMissing:
                case HorseStoryState.FoalFound:
                case HorseStoryState.FoalRescued: return FarmPhase.Crisis;
                case HorseStoryState.ReturnedToFarm: return FarmPhase.Return;
                case HorseStoryState.Growing: return FarmPhase.Growing;
                case HorseStoryState.AdultAtFarm:
                case HorseStoryState.Released: return FarmPhase.Aged;
                case HorseStoryState.BondedToPlayer: return H.step >= Q2Inside ? FarmPhase.After : FarmPhase.Aged;
            }
            return FarmPhase.Normal;
        }

        // ================================================================== Stand an den Besitzer

        private void SendStory()
        {
            if (!Loaded || Owner == null || !Owner.IsActive) return;
            string flags = string.Join(",", D.storyFlags);
            string offer = ServerStoryOffer();
            byte phase = (byte)ServerFarmPhase();
            float herd = H != null ? Mathf.Round(H.herdRouteProgress) : 0f;
            string key = D.race + "|" + flags + "|" + offer + "|" + H.state + "|" + H.step + "|" + herd + "|" + H.epilogue + "|" + phase;
            if (key == _lastStorySent) return;
            _lastStorySent = key;
            TargetStory(Owner, D.race, flags, offer, H.state, H.step, herd, H.epilogue, phase);
        }

        [TargetRpc]
        private void TargetStory(NetworkConnection conn, string race, string flags, string offer, int state, int step, float herd, bool epilogue, byte phase)
        {
            StoryClient.Apply(race, flags, offer, state, step, herd, epilogue, phase);
            ProgressionClient.RaiseChanged(); // Markierungen (!) neu
        }

        [TargetRpc]
        private void TargetStoryScene(NetworkConnection conn, string sceneId)
        {
            StoryClient.RaiseScene(sceneId);
        }

        [TargetRpc]
        private void TargetStoryCast(NetworkConnection conn, string label, float seconds)
        {
            CastBarState.Begin(label, seconds);
        }

        [TargetRpc]
        private void TargetStoryCastEnd(NetworkConnection conn, bool interrupted)
        {
            CastBarState.End(interrupted);
        }

        private void PlayScene(string id)
        {
            ServerLastScene = id;
            if (Owner != null && Owner.IsActive) TargetStoryScene(Owner, id);
        }

        private void SetHorse(HorseStoryState s, int step)
        {
            H.state = (int)s;
            H.step = step;
        }

        private void StoryCommit(bool saveNow)
        {
            if (saveNow) _inv.SaveNow();
            else _inv.ServerMarkDirty();
            SendState(false);
            SendStory();
        }

        // ================================================================== Bitten vom Client

        private void HorseStoryStartClient()
        {
            if (IsOwner) StoryClient.Request = (a, b) => { if (IsClientInitialized) ServerStoryRequest(a ?? "", b ?? ""); };
        }

        private void HorseStoryStopClient()
        {
            if (IsOwner)
            {
                StoryClient.Request = null;
                StoryClient.Reset();
                StoryDirector.Abort();
            }
        }

        [ServerRpc]
        private void ServerStoryRequest(string action, string arg)
        {
            if (!Loaded || action == null || action.Length > 32 || (arg != null && arg.Length > 48)) return;
            ServerStoryAction(action, arg ?? "");
        }

        /// <summary>Server (auch fuer Tests): eine Geschichts-Handlung ausfuehren. Rueckgabe: leer = ok, sonst Grund.</summary>
        public string ServerStoryAction(string action, string arg)
        {
            if (!Loaded) return "nicht bereit";
            string r;
            switch (action)
            {
                case "approach": r = StoryApproach(); break;
                case "free_foal": r = StartStoryCast("free_foal", FreeFoalSeconds, Loc.T("story.horse.action.free")); break;
                case "open_gate": r = StartStoryCast("open_gate", GateSeconds, Loc.T("story.horse.action.gate")); break;
                case "hand": r = StartStoryCast("hand", HandSeconds, Loc.T("story.horse.action.hand")); break;
                case "cue": r = StoryCue(arg); break;
                case "scene_done": r = StorySceneDone(arg); break;
                // P3 Glockenrast: Fundstuecke und Geschichtsschritte ("gr.*", NetworkProgression.Glockenrast)
                default: r = GlockenrastCatalog.IsNodeAction(action) ? GlockenrastAction(action) : "unbekannt"; break;
            }
            ServerStoryNote = action + ": " + (string.IsNullOrEmpty(r) ? "ok" : r);
            return r;
        }

        private float DistTo(string anchor)
        {
            StoryAnchor a = StoryAnchor.Find(anchor);
            if (a == null) return float.MaxValue;
            Vector3 d = a.Position - transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        private string StoryApproach()
        {
            if (DistTo(AnchorYard) > 45f) return "zu weit vom Hof";
            if (H.State == HorseStoryState.Unknown)
            {
                if (!Q1Gate(out string why)) return why;
                if (ServerIsActive(HorseStoryCatalog.FoalRescueQuest) || ServerIsCompleted(HorseStoryCatalog.FoalRescueQuest)) return "laeuft schon";
                StoryMilestones.Grant(D, "story.horse.q1_offered");
                StoryCommit(false);
                PlayScene("q1_approach");
                return "";
            }
            if (H.State == HorseStoryState.AdultAtFarm && H.step == Q2Offer)
            {
                if (!Q2Unlocked()) return "noch nicht";
                if (D.HasFlag("story.horse.q2_offered")) return "schon angeboten";
                EnsureHerd(HerdEncounter.KindAdultHerd);
                StoryMilestones.Grant(D, "story.horse.q2_offered");
                StoryCommit(false);
                PlayScene("q2_recognize");
                return "";
            }
            return "nichts zu tun";
        }

        private string StoryCue(string cue)
        {
            if (_herd == null) return "keine Herde";
            Transform me = transform;
            switch (cue)
            {
                case "foal_sniff":
                    if (H.State != HorseStoryState.ReturnedToFarm) return "falscher Stand";
                    _herd.ServerSetSniff(true);
                    _sniffUntil = Time.time + 9f;
                    return "";
                case "foal_back":
                    _herd.ServerSetSniff(false);
                    return "";
                case "horse_notice":
                    if (H.State != HorseStoryState.AdultAtFarm) return "falscher Stand";
                    StoryAnchor past = StoryAnchor.Find(AnchorPasture);
                    _herd.ServerSetStoryHorseAway(true, past != null ? past.Position + past.Forward * 6f : _herd.Center);
                    return "";
                case "horse_approach":
                    if (H.State != HorseStoryState.AdultAtFarm) return "falscher Stand";
                    Vector3 to = _herd.Center - me.position;
                    to.y = 0f;
                    _herd.ServerSetStoryHorseAway(true, me.position + (to.sqrMagnitude > 0.01f ? to.normalized : Vector3.forward) * 2.8f);
                    return "";
                case "herd_release":
                    if (H.step != Q2Releasing) return "falscher Schritt";
                    _herd.ServerSetStoryHorseAway(false, Vector3.zero);
                    _herd.ServerRelease();
                    return "";
                case "horse_stop":
                case "horse_turn":
                    if (H.step != Q2Releasing) return "falscher Schritt";
                    StoryAnchor gate = StoryAnchor.Find(AnchorGate);
                    Vector3 stop = gate != null ? gate.Position + gate.Forward * 36f : _herd.Center;
                    _herd.ServerSetStoryHorseAway(true, stop);
                    return "";
                case "horse_return":
                    if (H.step != Q2Releasing) return "falscher Schritt";
                    Vector3 back = _herd.Center - me.position;
                    back.y = 0f;
                    _herd.ServerSetStoryHorseAway(true, me.position + (back.sqrMagnitude > 0.01f ? back.normalized : Vector3.forward) * 3f);
                    return "";
                case "horse_sniff_hand":
                    if (H.step != Q2Bonding) return "falscher Schritt";
                    _herd.ServerSetSniff(true);
                    return "";
                case "horse_relax":
                    if (H.step != Q2Bonding) return "falscher Schritt";
                    _herd.ServerSetSniff(false);
                    _herd.ServerSetRelaxed(true);
                    return "";
            }
            return "unbekannt";
        }

        private string StorySceneDone(string scene)
        {
            switch (scene)
            {
                case "q2_release":
                    if (H.step != Q2Releasing) return "falscher Schritt";
                    H.step = Q2Hand;
                    _herd?.ServerSetHandReady(true);
                    StoryCommit(true);
                    return "";
                case "q2_bond":
                    if (H.step != Q2Bonding) return "falscher Schritt";
                    CompleteLastPasture();
                    return "";
                case "q2_final":
                    if (H.State != HorseStoryState.BondedToPlayer || H.step != Q2Final) return "falscher Schritt";
                    H.step = Q2Inside;
                    StoryCommit(true);
                    ReportQuestReward(HorseStoryCatalog.LastPastureQuest);
                    DespawnStoryObjects();
                    return "";
            }
            return "";
        }

        // ================================================================== Kanaele

        private string StartStoryCast(string action, float seconds, string label)
        {
            if (_hsCastUntil > 0f) return "laeuft schon";
            if (_health != null && !_health.IsAlive) return "tot";
            string why = CastAllowed(action);
            if (!string.IsNullOrEmpty(why))
            {
                if (Owner != null && Owner.IsActive) TargetMessage(Owner, why);
                return why;
            }
            _hsCastAction = action;
            _hsCastStart = Time.time;
            _hsCastUntil = Time.time + seconds;
            _hsCastFrom = transform.position;
            if (Owner != null && Owner.IsActive) TargetStoryCast(Owner, label, seconds);
            return "";
        }

        private string CastAllowed(string action)
        {
            switch (action)
            {
                case "free_foal":
                    if (H.State != HorseStoryState.FoalFound || H.step != StepFoalStuck) return "Das geht gerade nicht.";
                    return DistTo(AnchorFoal) > 5.5f ? "Zu weit entfernt." : "";
                case "open_gate":
                    if (H.State != HorseStoryState.AdultAtFarm || H.step != Q2Gate) return "Die Weide ist noch nicht sicher.";
                    return DistTo(AnchorGate) > 5f ? "Zu weit entfernt." : "";
                case "hand":
                    if (H.State != HorseStoryState.AdultAtFarm || H.step != Q2Hand || _herd == null) return "Das geht gerade nicht.";
                    int idx = _herd.Presenter != null ? _herd.Presenter.FoalIndex : -1;
                    Vector3 horse = _herd.Presenter != null && idx >= 0 ? _herd.Presenter.Shown(idx) : transform.position;
                    return (horse - transform.position).magnitude > 6f && _herd.Presenter != null ? "Zu weit entfernt." : "";
            }
            return "Das geht gerade nicht.";
        }

        private void StoryCastUpdate()
        {
            if (_hsCastUntil <= 0f) return;
            Vector3 d = transform.position - _hsCastFrom;
            d.y = 0f;
            if (d.sqrMagnitude > 0.35f * 0.35f) CancelStoryCast(Loc.T("story.horse.cast_moved"));
            else if (_mCombat != null && _mCombat.ServerLastAttackTime > _hsCastStart) CancelStoryCast("");
            else if (Time.time >= _hsCastUntil) CompleteStoryCast();
        }

        private void CancelStoryCast(string message)
        {
            if (_hsCastUntil <= 0f) return;
            _hsCastUntil = -1f;
            if (Owner != null && Owner.IsActive)
            {
                TargetStoryCastEnd(Owner, true);
                if (!string.IsNullOrEmpty(message)) TargetMessage(Owner, message);
            }
        }

        private void CompleteStoryCast()
        {
            string action = _hsCastAction;
            _hsCastUntil = -1f;
            if (Owner != null && Owner.IsActive) TargetStoryCastEnd(Owner, false);
            if (!string.IsNullOrEmpty(CastAllowed(action))) return;
            switch (action)
            {
                case "free_foal":
                    SetHorse(HorseStoryState.FoalRescued, StepFight);
                    H.foalRescuedAtUtc = NowUtc();
                    ServerSetStoryObjective(HorseStoryCatalog.FoalRescueQuest, ObjFoal, 1);
                    _herd?.ServerSetFoalStuck(false);
                    _herd?.ServerSetMode(HerdMode.Panicked);
                    StartFight();
                    StoryCommit(true);
                    PlayScene("q1_predator");
                    break;
                case "open_gate":
                    H.step = Q2Releasing;
                    SetHorse(HorseStoryState.AdultAtFarm, Q2Releasing);
                    ServerSetStoryObjective(HorseStoryCatalog.LastPastureQuest, ObjGate, 1);
                    H.releasedAtUtc = NowUtc();
                    StoryCommit(true);
                    PlayScene("q2_release");
                    break;
                case "hand":
                    H.step = Q2Bonding;
                    _herd?.ServerSetHandReady(false);
                    ServerSetStoryObjective(HorseStoryCatalog.LastPastureQuest, ObjHand, 1);
                    StoryCommit(true);
                    PlayScene("q2_bond");
                    break;
            }
        }

        // ================================================================== Auftraege

        private void StoryQuestAccepted(QuestDefinition q)
        {
            if (q == null) return;
            if (q.Id == HorseStoryCatalog.FoalRescueQuest)
            {
                SetHorse(HorseStoryState.FoalMissing, 0);
                H.herdRouteProgress = 0f;
                D.storyFlags.Remove("story.horse.q1_offered");
                _inv.ServerMarkDirty();
                SendStory();
            }
            else if (q.Id == HorseStoryCatalog.LastPastureQuest)
            {
                SetHorse(HorseStoryState.AdultAtFarm, Q2Threats);
                D.storyFlags.Remove("story.horse.q2_offered");
                _inv.ServerMarkDirty();
                SendStory();
            }
        }

        private void StoryQuestCompleted(QuestDefinition q)
        {
            if (q == null || q.Id != HorseStoryCatalog.FoalRescueQuest) return;
            SetHorse(HorseStoryState.Growing, 0);
            StoryMilestones.Grant(D, HorseStoryCatalog.FlagFoalRescueCompleted);
            StoryMilestones.Grant(D, HorseStoryCatalog.FlagStoryHorseMetPlayer);
            DespawnStoryObjects();
            PromoteHorseStory(false);
        }

        /// <summary>
        /// 22.10. (STORY-01): aus ServerAbandon – ein aufgegebener Geschichtsauftrag ist wieder anbietbar. Vorher blieb der Stand
        /// mitten in der Geschichte stehen (kein „!“, im Gespraech nichts, Tank-Klassenkette und Kriegspferd dauerhaft gesperrt;
        /// nach dem Weidetor gab „Hand hinhalten“ sogar das Pferd ohne EP/Geld bei falscher „+9000 EP“-Meldung).
        /// Gespeichert wird in ServerAbandon (ServerCommit).
        /// </summary>
        private void StoryQuestAbandoned(QuestDefinition q)
        {
            if (q == null || !Loaded || !ResetAbandonedHorseQuest(q.Id)) return;
            CancelStoryCast("");   // laufendes „Fohlen befreien“/„Weidetor“/„Hand“ endet sichtbar
            DespawnStoryObjects(); // Herde, Raubtier, Bedrohungen dieses Durchgangs weg (beim Neuannehmen frisch)
            _stress = 0f;
            SendStory();
        }

        /// <summary>
        /// 22.10. (STORY-01): Laeuft der Geschichtsauftrag <paramref name="questId"/> nicht (mehr) und ist er nicht abgeschlossen,
        /// steht der Stand aber noch mitten darin: zurueck auf „angeboten“. Q1 → Unknown, Q2 → AdultAtFarm/Q2Offer (auch nach dem
        /// Tor, Schritte 5–8). Die Angebotsmarke wird gleich gesetzt (die Szene q1_approach/q2_recognize kennt der Spieler schon):
        /// der Zuechter bietet den Auftrag sofort wieder an. Rueckgabe: etwas geaendert (Aufrufer speichert).
        /// </summary>
        private bool ResetAbandonedHorseQuest(string questId)
        {
            if (H == null || ServerIsActive(questId) || ServerIsCompleted(questId)) return false;
            if (questId == HorseStoryCatalog.FoalRescueQuest)
            {
                if (H.State < HorseStoryState.FoalMissing || H.State > HorseStoryState.ReturnedToFarm) return false;
                SetHorse(HorseStoryState.Unknown, 0);
                H.herdRouteProgress = 0f;
                _hsSaveMeters = 0f;
                D.storyFlags?.Remove("story.horse.q1_home_played");   // Abschluss-Szene am Hof beim naechsten Durchgang wieder
                StoryMilestones.Grant(D, "story.horse.q1_offered");
                return true;
            }
            if (questId == HorseStoryCatalog.LastPastureQuest)
            {
                if (H.State != HorseStoryState.AdultAtFarm || H.step == Q2Offer) return false;
                SetHorse(HorseStoryState.AdultAtFarm, Q2Offer);
                StoryMilestones.Grant(D, "story.horse.q2_offered");
                return true;
            }
            return false;
        }

        /// <summary>Q2 endet ohne Abgabefenster (keine Beute-Einblendung vor der Szene): Pferd binden, Auftrag abschliessen.</summary>
        private void CompleteLastPasture()
        {
            QuestDefinition q = GameData.Quest(HorseStoryCatalog.LastPastureQuest);
            QuestSave save = q != null ? FindQuest(q.Id) : null;
            string unlock = ServerUnlockMount(MountCatalog.WarhorseId);
            if (!string.IsNullOrEmpty(unlock)) Debug.LogWarning($"[Ashveil] Weidegang '{_profile}': Reittier – {unlock}");
            if (D.mount != null) D.mount.cosmetics = HorseStoryCatalog.StoryHorseId;
            // Bindung beginnt nicht bei null – er kennt Euch seit damals (Vorgabe: leicht erhoehter Startwert)
            ServerAddStartBond(MountCatalog.Warhorse.bond, 1.5);
            SetHorse(HorseStoryState.BondedToPlayer, Q2Final);
            H.bondedAtUtc = NowUtc();
            if (save != null)
            {
                D.quests.Remove(save);
                if (!D.completedQuests.Contains(q.Id)) D.completedQuests.Add(q.Id);
                if (q.grantsFlags != null) foreach (string f in q.grantsFlags) StoryMilestones.Grant(D, f);
                D.money += q.money;
            }
            _inv.ServerCommit();
            if (save != null) AddXp(q.xp);
            StoryCommit(true);
            PlayScene("q2_final");
        }

        /// <summary>Belohnung erst nach der Schluss-Szene melden (Vorgabe: kein Beute-Popup vor dem emotionalen Moment).</summary>
        private void ReportQuestReward(string questId)
        {
            QuestDefinition q = GameData.Quest(questId);
            if (q == null || Owner == null || !Owner.IsActive) return;
            TargetQuest(Owner, q.Id, true);
            if (q.xp > 0) TargetXp(Owner, q.xp, transform.position);
            if (!ServerMountOnBar() && Owner.IsActive) TargetMessage(Owner, Loc.T("story.horse.mount_menu"));
        }

        // ================================================================== Takt

        private void HorseStoryUpdate()
        {
            StoryCastUpdate();
            _hsTick -= Time.deltaTime;
            if (_hsTick > 0f) return;
            const float dt = 0.25f;
            _hsTick = dt;
            if (H == null) return;
            if (_sniffUntil > 0f && Time.time > _sniffUntil && _herd != null) { _herd.ServerSetSniff(false); _sniffUntil = -1f; }
            switch (H.State)
            {
                case HorseStoryState.FoalMissing: SearchTick(); break;
                case HorseStoryState.FoalFound: FoundTick(); break;
                case HorseStoryState.FoalRescued:
                    if (H.step == StepFight) FightTick(dt);
                    else if (H.step == StepReturning) ReturnTick(dt);
                    break;
                case HorseStoryState.ReturnedToFarm: HomeTick(); break;
                case HorseStoryState.Growing:
                    if (PromoteHorseStory(false)) SendStory();
                    break;
                case HorseStoryState.AdultAtFarm: PastureTick(); break;
            }
            PhaseObserversTick();
            SendStory();
        }

        private float _phaseObsTick;

        /// <summary>
        /// Gruppenmitglieder kommen dazu oder gehen weg: Sicht der Geschichts-Objekte etwa jede Sekunde neu pruefen
        /// (zusaetzlich zur zeitgesteuerten Regel – wenige Objekte je Spieler, billig).
        /// </summary>
        private void PhaseObserversTick()
        {
            _phaseObsTick -= 0.25f;
            if (_phaseObsTick > 0f) return;
            _phaseObsTick = 1f;
            var objs = ServerManager != null ? ServerManager.Objects : null;
            if (objs == null) return;
            if (_herd != null && _herd.IsSpawned) objs.RebuildObservers(_herd.NetworkObject);
            Rebuild(objs, _predator);
            for (int i = 0; i < _threats.Count; i++) Rebuild(objs, _threats[i].brain);
        }

        private static void Rebuild(FishNet.Managing.Server.ServerObjects objs, EnemyBrain b)
        {
            if (b == null) return;
            NetworkObject nob = b.GetComponent<NetworkObject>();
            if (nob != null && nob.IsSpawned) objs.RebuildObservers(nob);
        }

        // ---- Q1: Suche

        private void SearchTick()
        {
            if (!ServerIsActive(HorseStoryCatalog.FoalRescueQuest)) return;
            // Die Senke zaehlt immer (keine Sackgasse, falls jemand Spuren uebersieht)
            if (DistTo(AnchorBasin) < 34f)
            {
                ServerSetStoryObjective(HorseStoryCatalog.FoalRescueQuest, ObjSearch, ClueCount);
                SetHorse(HorseStoryState.FoalFound, StepFoalStuck);
                StoryCommit(true);
                PlayScene("q1_basin");
                return;
            }
            if (H.step >= ClueCount) return;
            StoryAnchor clue = StoryAnchor.Find(ClueAnchor(H.step));
            if (clue == null || !clue.Contains(transform.position)) return;
            H.step++;
            ServerSetStoryObjective(HorseStoryCatalog.FoalRescueQuest, ObjSearch, H.step);
            StoryCommit(false);
            PlayScene("q1_clue" + H.step);
        }

        private void FoundTick()
        {
            if (DistTo(AnchorBasin) > 240f) { DespawnStoryObjects(); return; }
            if (!EnsureHerd(HerdEncounter.KindFoalHerd)) return;
            _herd.ServerSetMode(HerdMode.Huddle);
            _herd.ServerSetFoalStuck(true);
        }

        // ---- Q1: Kampf

        private void StartFight()
        {
            _stress = 0f;
            _nextLeap = Time.time + 9f;
            _deadSince = -1f;
            SpawnPredator();
        }

        private void SpawnPredator()
        {
            HorseStoryContent c = HorseStoryContent.Load();
            StoryAnchor entry = StoryAnchor.Find(AnchorBasinEntry);
            if (c == null || c.predatorPrefab == null || entry == null || EnemyDirector.Instance == null) { ServerStoryNote = "Raubtier-Vorlage fehlt"; return; }
            int level = Mathf.Max(4, Model.Level);
            string owner = _profile;
            _predator = EnemyDirector.Instance.SpawnAt(c.predatorPrefab, entry.Position, entry.transform.eulerAngles.y, level, "story:" + owner, b =>
            {
                StoryPhaseCondition.Apply(b.gameObject, owner);
                b.SetAggroProfile(AggroProfile.Passive);
            }, 12f);
            if (_predator == null) { ServerStoryNote = "Raubtier nicht erzeugt (NavMesh?)"; return; }
            StoryAnchor basin = StoryAnchor.Find(AnchorBasin);
            if (basin != null) _predator.ServerSetHome(basin.Position);
            _predator.Killed += OnPredatorKilled;
            if (_health != null) _predator.ServerEngage(_health);
        }

        private void OnPredatorKilled(EnemyBrain b)
        {
            if (!Loaded || H.State != HorseStoryState.FoalRescued || H.step != StepFight) return;
            _predator = null;
            ServerSetStoryObjective(HorseStoryCatalog.FoalRescueQuest, ObjPredator, 1);
            H.step = StepReturning;
            H.herdRouteProgress = 0f;
            _stress = 0f;
            if (_herd != null)
            {
                _herd.ServerSetMode(HerdMode.FleeHome);
                _herd.ServerSetProgress(0f);
            }
            StoryCommit(true);
        }

        private void FightTick(float dt)
        {
            EnsureHerd(HerdEncounter.KindFoalHerd);
            if (_predator == null || _predator.State == EnemyState.Dead)
            {
                if (_predator == null) SpawnPredator();
                return;
            }
            StoryAnchor basin = StoryAnchor.Find(AnchorBasin);
            // Wipe / Leine: niemand Berechtigtes mehr lebend in der Naehe → Anfang der Begegnung (kein Fehlschlag)
            bool anyone = StoryDefenderNear(basin != null ? basin.Position : transform.position, 70f, out Health defender);
            if (!anyone)
            {
                if (_deadSince < 0f) _deadSince = Time.time;
                if (Time.time - _deadSince > 6f) ResetEncounter(Loc.T("story.horse.reset"));
                return;
            }
            _deadSince = -1f;
            // Ziel halten: das Raubtier kaempft nur mit Berechtigten (Fremde sehen es nicht)
            if (_predator.Target == null || !_predator.Target.IsAlive) _predator.ServerEngage(defender);

            _stress = Mathf.Max(0f, _stress - StressDecay * dt);
            if (_herd != null) _herd.ServerSetMode(_stress > 25f ? HerdMode.Panicked : HerdMode.Huddle);
            if (Time.time < _nextLeap || _herd == null || _predator.IsStunned) return;
            // Sprung zur Herde, wenn der Tank nicht dazwischen steht (Lernziel: zwischen Raubtier und Herde bleiben)
            Vector3 herdPoint = _herd.ServerHerdPoint(UnityEngine.Random.Range(0, 6));
            float tankToHerd = Flat(transform.position - herdPoint).magnitude;
            float predToHerd = Flat(_predator.transform.position - herdPoint).magnitude;
            if (tankToHerd < 7f || predToHerd < 5f) { _nextLeap = Time.time + 2f; return; }
            if (_predator.ServerLeapAt(herdPoint, 1.4f, land => OnLeapLanded(land, herdPoint)))
                _nextLeap = Time.time + UnityEngine.Random.Range(9f, 13f);
        }

        private void OnLeapLanded(Vector3 land, Vector3 herdPoint)
        {
            if (!Loaded || H.State != HorseStoryState.FoalRescued || H.step != StepFight) return;
            // Abgefangen? Wer im Landepunkt steht, faengt den Satz ab – das Raubtier taumelt
            if (StoryDefenderNear(land, 3.2f, out _))
            {
                if (_predator != null) _predator.ServerStun(1.6f);
                return;
            }
            _stress += StressPerLeap;
            if (Owner != null && Owner.IsActive) TargetMessage(Owner, Loc.T("story.horse.stress"));
            if (_stress >= 100f) ResetEncounter(Loc.T("story.horse.reset"));
        }

        private bool StoryDefenderNear(Vector3 p, float radius, out Health who)
        {
            who = null;
            float best = float.MaxValue;
            foreach (string prof in StoryParty())
            {
                if (!NetworkInventory.Online.TryGetValue(prof, out NetworkInventory inv) || inv == null) continue;
                var hp = inv.GetComponent<Health>();
                if (hp == null || !hp.IsAlive) continue;
                float d = Flat(inv.transform.position - p).magnitude;
                if (d > radius || d >= best) continue;
                best = d;
                who = hp;
            }
            return who != null;
        }

        /// <summary>Besitzer plus Gruppenmitglieder (Helfer duerfen kaempfen, nur der Besitzer schaltet weiter).</summary>
        private IEnumerable<string> StoryParty()
        {
            yield return _profile;
            foreach (string m in PartyService.MembersOf(_profile)) yield return m;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private void ResetEncounter(string message)
        {
            if (_predator != null && EnemyDirector.Instance != null) EnemyDirector.Instance.Remove(_predator);
            _predator = null;
            _stress = 0f;
            SetHorse(HorseStoryState.FoalFound, StepFoalStuck);
            ServerSetStoryObjective(HorseStoryCatalog.FoalRescueQuest, ObjFoal, 0);
            if (_herd != null)
            {
                _herd.ServerSetMode(HerdMode.Huddle);
                _herd.ServerSetFoalStuck(true);
            }
            StoryCommit(true);
            if (Owner != null && Owner.IsActive && !string.IsNullOrEmpty(message)) TargetMessage(Owner, message);
        }

        // ---- Q1: Heimweg

        private void ReturnTick(float dt)
        {
            if (!EnsureHerd(HerdEncounter.KindFoalHerd)) return;
            HerdRoute route = HerdRoute.Find(RouteReturn);
            if (route == null) return;
            _herd.ServerSetMode(HerdMode.FleeHome);
            Vector3 lead = route.PointAt(H.herdRouteProgress, out _);
            bool near = StoryDefenderNear(lead, HerdWaitDistance, out _);
            if (near) H.herdRouteProgress = Mathf.Min(route.Length, H.herdRouteProgress + HerdSpeed * _herdSpeedMul * dt);
            else if (Time.time - _lastWaitMsg > 20f && Owner != null && Owner.IsActive)
            {
                _lastWaitMsg = Time.time;
                TargetMessage(Owner, Loc.T("story.horse.herd_wait"));
            }
            _herd.ServerSetProgress(H.herdRouteProgress);
            if (H.herdRouteProgress - _hsSaveMeters > 20f)
            {
                _hsSaveMeters = H.herdRouteProgress;
                _inv.ServerMarkDirty();
            }
            if (H.herdRouteProgress >= route.Length - 0.1f)
            {
                SetHorse(HorseStoryState.ReturnedToFarm, StepHome);
                ServerSetStoryObjective(HorseStoryCatalog.FoalRescueQuest, ObjReturn, 1);
                StoryAnchor past = StoryAnchor.Find(AnchorPasture);
                if (past != null) _herd.ServerSetPlace(past.Position, 12f, past.Forward);
                _herd.ServerSetMode(HerdMode.Graze);
                StoryCommit(true);
            }
        }

        private void HomeTick()
        {
            if (DistTo(AnchorYard) > 220f) { DespawnStoryObjects(); return; }
            if (!EnsureHerd(HerdEncounter.KindFoalHerd)) return;
            if (_herd.Mode != HerdMode.Graze)
            {
                StoryAnchor past = StoryAnchor.Find(AnchorPasture);
                if (past != null) _herd.ServerSetPlace(past.Position, 12f, past.Forward);
                _herd.ServerSetMode(HerdMode.Graze);
            }
            if (!D.HasFlag("story.horse.q1_home_played") && DistTo(AnchorYard) < 30f)
            {
                StoryMilestones.Grant(D, "story.horse.q1_home_played");
                StoryCommit(false);
                PlayScene("q1_home");
            }
        }

        // ---- Q2: Weide

        private void PastureTick()
        {
            // Bote am Wachtfeuer (einmal): „Vom Pferdehof draussen war jemand hier.“
            // (wer schon am Hof war und das Pferd wiedererkannt hat, braucht keinen Boten mehr)
            if (!D.HasFlag(HorseStoryCatalog.FlagMessengerHeard) && H.step == Q2Offer && !D.HasFlag("story.horse.q2_offered"))
            {
                NpcIdentity bralt = NpcIdentity.Find("npc.oskar_bralt");
                if (bralt != null && Flat(bralt.transform.position - transform.position).magnitude < 9f)
                {
                    StoryMilestones.Grant(D, HorseStoryCatalog.FlagMessengerHeard);
                    StoryCommit(false);
                    PlayScene("q2_messenger");
                }
            }
            bool nearFarm = DistTo(AnchorYard) < 240f;
            if (!nearFarm)
            {
                if (H.step < Q2Releasing) DespawnStoryObjects();
                return;
            }
            if (!EnsureHerd(HerdEncounter.KindAdultHerd)) return;
            // Wiedererkennen: der Server entscheidet selbst, sobald der Spieler den Hof betritt (die Figur beim Client
            // kann auch vorher ausloesen – beides fuehrt genau einmal zur Szene)
            if (H.step == Q2Offer && !D.HasFlag("story.horse.q2_offered") && DistTo(AnchorYard) < 32f && _health != null && _health.IsAlive)
                ServerStoryNote = "approach (Server): " + StoryApproach();
            if (H.step == Q2Threats) ThreatTick();
        }

        private void ThreatTick()
        {
            if (!ServerIsActive(HorseStoryCatalog.LastPastureQuest)) return;
            if (_threats.Count == 0)
            {
                SpawnThreats();
                if (_threats.Count == 0) return;
            }
            bool allDone = true;
            foreach (string obj in new[] { ObjWolves, ObjSpitters, ObjLone })
            {
                bool alive = false;
                foreach ((EnemyBrain b, string o) in _threats)
                    if (o == obj && b != null && b.State != EnemyState.Dead) alive = true;
                if (!alive) ServerSetStoryObjective(HorseStoryCatalog.LastPastureQuest, obj, 1);
                else allDone = false;
                // Bedrohungen gelten nur fuer Berechtigte (Fremde sehen sie nicht)
                foreach ((EnemyBrain b, string o) in _threats)
                    if (o == obj && b != null && b.State != EnemyState.Dead && b.InCombat && (b.Target == null || !IsStoryPartyMember(b.Target)))
                        b.ServerDisengage();
            }
            if (!allDone) return;
            _threats.Clear();
            H.step = Q2Gate;
            StoryCommit(true);
        }

        private bool IsStoryPartyMember(Health h)
        {
            var inv = h != null ? h.GetComponent<NetworkInventory>() : null;
            if (inv == null) return false;
            foreach (string p in StoryParty()) if (p == inv.ProfileId) return true;
            return false;
        }

        private void SpawnThreats()
        {
            HorseStoryContent c = HorseStoryContent.Load();
            if (c == null || EnemyDirector.Instance == null) return;
            int lvl = Mathf.Max(HorseStoryCatalog.LastPastureMinLevel, Model.Level);
            SpawnGroup(c.wolfPrefab, AnchorThreatWolves, ObjWolves, 3, lvl, 4f);
            SpawnGroup(c.spitterPrefab, AnchorThreatSpitters, ObjSpitters, 2, lvl, 3f);
            SpawnGroup(c.lonePrefab, AnchorThreatLone, ObjLone, 1, lvl + 1, 0f);
        }

        private void SpawnGroup(GameObject prefab, string anchorId, string obj, int count, int level, float spread)
        {
            StoryAnchor a = StoryAnchor.Find(anchorId);
            if (prefab == null || a == null) return;
            // schon erledigt? (Wiederverbinden)
            if (ServerProgressOf(HorseStoryCatalog.LastPastureQuest, obj) >= 1) return;
            string owner = _profile;
            for (int i = 0; i < count; i++)
            {
                float ang = i * 2.4f;
                Vector3 p = a.Position + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * spread;
                EnemyBrain b = EnemyDirector.Instance.SpawnAt(prefab, p, UnityEngine.Random.Range(0f, 360f), level, "story:" + owner + ":" + obj,
                    br => StoryPhaseCondition.Apply(br.gameObject, owner), 10f);
                if (b != null) _threats.Add((b, obj));
            }
        }

        private int ServerProgressOf(string questId, string storyKey)
        {
            QuestDefinition q = GameData.Quest(questId);
            QuestSave s = q != null ? FindQuest(questId) : null;
            if (s == null) return 0;
            for (int i = 0; i < q.objectives.Count; i++)
                if (q.objectives[i].targetId == storyKey && i < s.progress.Length) return s.progress[i];
            return 0;
        }

        // ================================================================== Herde (Netz-Objekt)

        private bool EnsureHerd(byte kind)
        {
            if (_herd != null && _herd.IsSpawned && _herd.Kind == kind) return true;
            if (_herd != null) DespawnHerd();
            HorseStoryContent c = HorseStoryContent.Load();
            bool q1 = kind == HerdEncounter.KindFoalHerd;
            StoryAnchor place = StoryAnchor.Find(q1 ? (H.step >= StepHome || H.State == HorseStoryState.ReturnedToFarm ? AnchorPasture : AnchorBasin) : AnchorPasture);
            if (c == null || c.herdPrefab == null || place == null) return false;
            if (HerdsByProfile.TryGetValue(_profile, out HerdEncounter old) && old != null && old.IsSpawned) ServerManager.Despawn(old.gameObject);
            GameObject go = Instantiate(c.herdPrefab, place.Position, Quaternion.identity);
            var herd = go.GetComponent<HerdEncounter>();
            if (herd == null) { Destroy(go); return false; }
            StoryPhaseCondition.Apply(go, _profile);
            herd.ServerInit(_profile, NetworkObject, kind, place.Position, q1 ? place.Radius : 12f, place.Forward);
            if (q1 && H.State == HorseStoryState.FoalRescued && H.step == StepReturning)
            {
                herd.ServerSetMode(HerdMode.FleeHome);
                herd.ServerSetProgress(H.herdRouteProgress);
            }
            else if (q1 && H.State == HorseStoryState.ReturnedToFarm) herd.ServerSetMode(HerdMode.Graze);
            else if (q1)
            {
                herd.ServerSetMode(HerdMode.Huddle);
                herd.ServerSetFoalStuck(H.State == HorseStoryState.FoalFound);
            }
            else
            {
                herd.ServerSetMode(HerdMode.Graze);
                if (H.step == Q2Hand) herd.ServerSetHandReady(true);
                if (H.step >= Q2Hand)
                {
                    // nach dem Freilassen steht das Pferd beim Spieler
                    herd.ServerSetStoryHorseAway(true, transform.position + transform.forward * 3f);
                }
            }
            ServerManager.Spawn(go, Owner);
            _herd = herd;
            HerdsByProfile[_profile] = herd;
            return true;
        }

        private void DespawnHerd()
        {
            if (_herd != null && _herd.IsSpawned) ServerManager.Despawn(_herd.gameObject);
            if (_profile != null && HerdsByProfile.TryGetValue(_profile, out HerdEncounter h) && h == _herd) HerdsByProfile.Remove(_profile);
            _herd = null;
        }

        private void DespawnStoryObjects()
        {
            DespawnHerd();
            if (_predator != null && EnemyDirector.Instance != null) EnemyDirector.Instance.Remove(_predator);
            _predator = null;
            foreach ((EnemyBrain b, string _) in _threats)
                if (b != null && EnemyDirector.Instance != null) EnemyDirector.Instance.Remove(b);
            _threats.Clear();
        }

        /// <summary>Bindung mit einem Vorsprung beginnen (Punkte fuer <paramref name="ranks"/> Raenge ab dem ersten).</summary>
        private void ServerAddStartBond(MasteryDefinition bond, double ranks)
        {
            if (bond == null || ServerMasteryPoints(bond.id) > 0) return;
            AddMasteryPoints(bond, bond.firstRankCost * ranks);
            SendMastery();
        }

        /// <summary>Liegt das Reittier auf der Leiste? (sonst Hinweis aufs Zauberbuch)</summary>
        private bool ServerMountOnBar()
        {
            if (D.mount == null) return true;
            foreach (ActionBarEntry e in D.actionBar)
                if (e.type == ActionEntryType.Mount && e.referenceId == D.mount.id) return true;
            return false;
        }

        // ================================================================== Tests / Werkzeuge

        /// <summary>Test/Werkzeug: Pferde-Geschichte auf einen Stand setzen (nur Test-Profile – oder das Editor-Werkzeug).</summary>
        public void ServerDebugSetHorseStory(HorseStoryState state, int step, float herd = 0f)
        {
            if (!Loaded) return;
            DespawnStoryObjects();
            SetHorse(state, step);
            H.herdRouteProgress = herd;
            H.epilogue = false;
            StoryCommit(true);
        }

        /// <summary>Test: Geschichtsmarke setzen/entfernen.</summary>
        public void ServerDebugFlag(string flag, bool on)
        {
            if (!Loaded || string.IsNullOrEmpty(flag)) return;
            if (on) StoryMilestones.Grant(D, flag);
            else D.storyFlags.Remove(flag);
            StoryCommit(false);
        }

        /// <summary>Test: Volk setzen (nur Test-Profile).</summary>
        public void ServerDebugRace(string race)
        {
            if (!Loaded || !TestRechte.Erlaubt(_profile)) return;
            D.race = RaceCatalog.Normalize(race);
            D.appearance.race = RaceCatalog.IndexOf(D.race);
            StoryCommit(true);
        }

        /// <summary>Test: Epilog-Pruefung wie beim Login (mit vorgetaeuschter Zeit).</summary>
        public void ServerDebugEpilogueCheck(double hoursAgo)
        {
            if (!Loaded) return;
            H.bondedAtUtc = DateTime.UtcNow.AddHours(-hoursAgo).ToString("o", CultureInfo.InvariantCulture);
            PromoteHorseStory(true);
            StoryCommit(true);
        }

        private float _herdSpeedMul = 1f;

        /// <summary>Test: Heimweg schneller (nur Test-Profile).</summary>
        public void ServerDebugHerdSpeed(float factor)
        {
            if (Loaded && TestRechte.Erlaubt(_profile)) _herdSpeedMul = Mathf.Clamp(factor, 0.1f, 40f);
        }

        /// <summary>Test: sofort einen Sprung zur Herde ausloesen (gleiche Regel wie im Kampf). false = geht gerade nicht.</summary>
        public bool ServerDebugLeap(int horse = 0)
        {
            if (_predator == null || _herd == null || H.State != HorseStoryState.FoalRescued || H.step != StepFight) return false;
            Vector3 herdPoint = _herd.ServerHerdPoint(horse);
            _nextLeap = Time.time + 30f;
            return _predator.ServerLeapAt(herdPoint, 1.4f, land => OnLeapLanded(land, herdPoint));
        }

        /// <summary>Test: Punkt eines Herdentiers (Sprungziel).</summary>
        public Vector3 ServerDebugHerdPoint(int horse) => _herd != null ? _herd.ServerHerdPoint(horse) : transform.position;

        /// <summary>Test: Stress der Herde setzen (Zuruecksetzen pruefen).</summary>
        public void ServerDebugStress(float value)
        {
            _stress = value;
            if (_stress >= 100f) ResetEncounter(Loc.T("story.horse.reset"));
        }

        public string DescribeHorseStory()
        {
            if (!Loaded) return "nicht geladen";
            var sb = new StringBuilder();
            sb.Append("state=").Append(H.State).Append(";step=").Append(H.step).Append(";herd=").Append(H.herdRouteProgress.ToString("0.0", CultureInfo.InvariantCulture))
              .Append(";phase=").Append(ServerFarmPhase()).Append(";offer=").Append(ServerStoryOffer()).Append(";stress=").Append(_stress.ToString("0", CultureInfo.InvariantCulture))
              .Append(";herdObj=").Append(_herd != null ? (_herd.Kind + "/" + _herd.Mode) : "-").Append(";pred=").Append(_predator != null ? _predator.State.ToString() : "-")
              .Append(";threats=").Append(_threats.Count).Append(";epi=").Append(H.epilogue ? 1 : 0).Append(";note=").Append(ServerStoryNote);
            return sb.ToString();
        }
    }
}
