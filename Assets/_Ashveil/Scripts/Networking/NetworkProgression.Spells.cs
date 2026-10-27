using System.Collections.Generic;
using System.Text;
using Ashveil.Core;
using Ashveil.Core.Events;
using Ashveil.Data;
using Ashveil.Gameplay.Abilities;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Weapons;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// P1 Klassenfaehigkeiten nach WoW Classic (Max 30.09.): Ressource (Wut/Energie/Mana), gelernte Raenge, globale
    /// Abklingzeit, Wirken im Stand, Server-Pruefung von Ziel, Reichweite, Sicht, Kosten und Abklingzeit, danach die Wirkung.
    ///
    ///   Client : druckt die Taste (Leiste) → <see cref="ServerCastSpell"/> mit Id und Ziel.
    ///   Server : prueft, zieht die Kosten ab (bei Wirkzeit am Ende), wendet Schaden/Heilung/Zustaende an, meldet Abklingzeit
    ///            und Wirken an den Besitzer und das Bild an alle.
    /// Ressource steht fuer alle sichtbar in SyncVars (Gruppenrahmen spaeter), der Rest geht nur an den Besitzer.
    /// </summary>
    public sealed partial class NetworkProgression
    {
        private readonly SyncVar<byte> _resType = new SyncVar<byte>(0);
        private readonly SyncVar<float> _resCur = new SyncVar<float>(0f);
        private readonly SyncVar<float> _resMax = new SyncVar<float>(0f);

        // ---- Server
        private float _res;
        private float _lastManaSpend = -99f;
        private float _gcdUntil = -1f;
        private readonly Dictionary<string, float> _spellReady = new Dictionary<string, float>();
        private SpellDefinition _castSpell;
        private int _castRank;
        private float _castUntil = -1f, _castStarted;
        private Vector3 _castFrom;
        private Health _castTarget;
        private SpellDefinition _nextSwing;
        private int _nextSwingRank;
        private readonly TalentBonuses _talents = new TalentBonuses();
        private CharacterEquipment _sEq;
        private WeaponUser _sWu;
        private bool _spellHooked;
        private float _resSync;
        private string _lastSpellState;

        private struct PendingHit
        {
            public float Due;
            public SpellDefinition Spell;
            public int Rank;
            public Health Target;
            public float Amount;
            public bool Crit;
            public bool Heal;
            public bool Stun;
        }
        private readonly List<PendingHit> _pending = new List<PendingHit>();

        /// <summary>Server: Klassen-Ressource (Tests/Anzeige).</summary>
        public float ServerResource => _res;
        public float ServerResourceMax => _resMax.Value;
        public ResourceType ServerResourceType => (ResourceType)_resType.Value;
        public bool ServerIsCasting => _castSpell != null;
        public string ServerCastingId => _castSpell != null ? _castSpell.Id : "";
        public string ServerQueuedNextSwing => _nextSwing != null ? _nextSwing.Id : "";
        public TalentBonuses ServerTalents => _talents;
        /// <summary>Server: letzter Grund, warum ein Zauber abgelehnt wurde (Tests).</summary>
        public string ServerLastSpellError { get; private set; } = "";
        /// <summary>Server: zuletzt erfolgreich gewirkter Zauber und seine Wirkung (Tests).</summary>
        public string ServerLastSpellResult { get; private set; } = "";
        public int ServerSpellsCast { get; private set; }

        /// <summary>Alle Clients: Ressource der Figur (auch fremder Spieler).</summary>
        public ResourceType ResourceKind => (ResourceType)_resType.Value;
        public float ResourceCurrent => _resCur.Value;
        public float ResourceMaximum => _resMax.Value;

        public int ServerSpellRank(string spellId)
        {
            if (!Loaded || D.spells == null) return 0;
            foreach (SpellSave s in D.spells) if (s.id == spellId) return s.rank;
            return 0;
        }

        public int ServerTalentRank(string talentId)
        {
            if (!Loaded || D.talents == null) return 0;
            foreach (TalentSave t in D.talents) if (t.id == talentId) return t.rank;
            return 0;
        }

        private CharacterClassDefinition ServerClass => Loaded ? Model.Class : null;

        // ================================================================== Start

        private void SpellsLogin()
        {
            _sEq = GetComponent<CharacterEquipment>();
            _sWu = GetComponent<WeaponUser>();
            NetworkPlayer.ResurrectWindow = ResurrectWindowFor;
            if (!_spellHooked)
            {
                Health.AnyDamaged += OnAnyDamagedForSpells;
                if (_health != null) _health.Died += OnSpellOwnerDied;
                _spellHooked = true;
            }
            _castSpell = null;
            _castUntil = -1f;
            _nextSwing = null;
            _pending.Clear();
            _spellReady.Clear();
            if (D.spells == null) D.spells = new List<SpellSave>();
            if (D.talents == null) D.talents = new List<TalentSave>();
            GrantStartSpells();
            ValidateTalentPoints();
            RecomputeTalents();
            CharacterClassDefinition cls = ServerClass;
            _resType.Value = (byte)(cls != null ? cls.resource : ResourceType.None);
            float max = ComputeMaxResource();
            _res = ServerResourceType == ResourceType.Rage ? 0f : max;
            _resMax.Value = max;
            _resCur.Value = _res;
            PlaceNewSpells();
            // Die Hechtrolle hat nur, wer rollen kann (Kampfumbau 30.09.): alte Leisten anderer Klassen bereinigen
            if (cls != null && !cls.canRoll && D.actionBar != null)
            {
                var bar = GetComponent<NetworkActionBar>();
                foreach (ActionBarEntry e in D.actionBar.ToArray())
                    if (e.referenceId == ActionBarLayout.Dodge && bar != null) bar.ClearServer(e.page, e.slot);
            }
            SendSpellState(true);
        }

        public const string ResurrectSpellId = "spell.weaver_rest_of_ash";

        /// <summary>
        /// Gefallen: kann ein Aschenweber in der Naehe (60 m, lebend) ihn zurueckholen, wartet das Erwachen am Wachtfeuer 45 s.
        /// </summary>
        private static float ResurrectWindowFor(NetworkPlayer dead)
        {
            if (dead == null) return 0f;
            foreach (NetworkProgression p in ByProfile.Values)
            {
                if (p == null || p.gameObject == dead.gameObject || p._health == null || !p._health.IsAlive) continue;
                if (p.ServerSpellRank(ResurrectSpellId) <= 0) continue;
                if (HorizontalDistance(p.transform.position, dead.transform.position) > 60f) continue;
                return 45f;
            }
            return 0f;
        }

        private void SpellsStopServer()
        {
            if (!_spellHooked) return;
            Health.AnyDamaged -= OnAnyDamagedForSpells;
            if (_health != null) _health.Died -= OnSpellOwnerDied;
            _spellHooked = false;
        }

        private void SpellsStartClient()
        {
            _resCur.OnChange += OnResourceChanged;
            _resMax.OnChange += OnResourceChanged;
            if (!IsOwner) return;
            SpellClient.Reset();
            SpellClient.CastRequest = RequestCast;
            SpellClient.TrainRequest = RequestTrain;
            SpellClient.TalentRequest = RequestTalent;
            SpellClient.TalentResetRequest = RequestTalentReset;
        }

        private void SpellsStopClient()
        {
            _resCur.OnChange -= OnResourceChanged;
            _resMax.OnChange -= OnResourceChanged;
            if (!IsOwner) return;
            if (SpellClient.CastRequest == (System.Action<string>)RequestCast) SpellClient.CastRequest = null;
            SpellClient.TrainRequest = null;
            SpellClient.TalentRequest = null;
            SpellClient.TalentResetRequest = null;
        }

        private void OnResourceChanged(float prev, float next, bool asServer)
        {
            if (!IsOwner) return;
            SpellClient.Resource = (ResourceType)_resType.Value;
            SpellClient.ResourceCurrent = _resCur.Value;
            SpellClient.ResourceMax = _resMax.Value;
        }

        /// <summary>Rang 1 der Startfaehigkeiten der Klasse (wie in WoW bekommt man die ersten Faehigkeiten geschenkt).</summary>
        private void GrantStartSpells()
        {
            CharacterClassDefinition cls = ServerClass;
            if (cls == null || GameData.Database == null) return;
            foreach (GameDefinition g in GameData.Database.All)
            {
                if (!(g is SpellDefinition s) || s.classId != cls.Id || !s.startKnown || s.RankCount == 0) continue;
                if (ServerSpellRank(s.Id) > 0) continue;
                if (s.ranks[0].level > Model.Level) continue;
                D.spells.Add(new SpellSave { id = s.Id, rank = 1 });
            }
        }

        /// <summary>Neu gelernte Faehigkeiten einmal auf die Leiste legen (freier Platz, nie ueberschreiben).</summary>
        private void PlaceNewSpells()
        {
            var bar = GetComponent<NetworkActionBar>();
            bool changed = false;
            foreach (SpellSave s in D.spells)
            {
                if (s.placed) continue;
                SpellDefinition def = GameData.Get<SpellDefinition>(s.id);
                if (def == null) continue;
                if (bar != null) bar.ServerPlaceIfMissing(ActionEntryType.Spell, s.id);
                s.placed = true;
                changed = true;
            }
            if (changed) _inv.ServerCommit();
        }

        private float ComputeMaxResource()
        {
            CharacterClassDefinition cls = ServerClass;
            if (cls == null) return 0f;
            EquipmentStats st = _sEq != null ? _sEq.Stats : default;
            return SpellRules.MaxResource(cls.resource, Model.Level, st, _talents);
        }

        // ================================================================== Takt

        private int _spellLevel;

        private void SpellsUpdate()
        {
            float now = Time.time, dt = Time.deltaTime;
            if (Model.Level != _spellLevel)
            {
                _spellLevel = Model.Level;
                SendSpellState(true); // Talentpunkte (auch bei Stufe per Test/Verwaltung)
            }
            ResourceType type = ServerResourceType;
            if (type != ResourceType.None)
            {
                float max = ComputeMaxResource();
                if (!Mathf.Approximately(max, _resMax.Value))
                {
                    // 22.10.2026 (FORTSCHRITT-07): Aendert sich das Maximum (Ausruestung, Stufe, Talent), bleibt der Mana-ANTEIL erhalten
                    // wie beim Leben (Health.SetMaxHealth). Vorher wurde jede Erhoehung gutgeschrieben – Ab- und Anlegen eines
                    // Intelligenz-Teils fuellte so mitten im Kampf beliebig auf. Volles Mana bleibt exakt voll; Wut/Energie unberuehrt,
                    // das Kappen nach unten erledigt weiter der Clamp unten.
                    if (type == ResourceType.Mana && _resMax.Value > 0f) _res = _res >= _resMax.Value - 0.01f ? max : _res * max / _resMax.Value;
                    _resMax.Value = max;
                }
                bool alive = _health == null || _health.IsAlive;
                switch (type)
                {
                    case ResourceType.Rage:
                        if (!_mInCombat && !ComputeInCombat(now)) _res = Mathf.Max(0f, _res - SpellRules.RageDecayPerSecond * dt);
                        break;
                    case ResourceType.Energy:
                        if (alive) _res = Mathf.Min(max, _res + SpellRules.EnergyRegen(_talents) * dt);
                        break;
                    case ResourceType.Mana:
                        if (alive && now - _lastManaSpend >= SpellRules.FiveSecondRule && _castSpell == null)
                        {
                            float regen = SpellRules.ManaRegen(Model.Level, _sEq != null ? _sEq.Stats : default, _talents);
                            // P2: ausser Kampf erholt sich Mana wie das Leben zuegig (kein Trink-Leerlauf zwischen den Kaempfen)
                            if (!_mInCombat) regen = Mathf.Max(regen, max * SpellRules.OutOfCombatManaPerSecond);
                            _res = Mathf.Min(max, _res + regen * dt);
                        }
                        break;
                }
                _res = Mathf.Clamp(_res, 0f, max);
                _resSync -= dt;
                if (_resSync <= 0f || Mathf.Abs(_res - _resCur.Value) >= 5f)
                {
                    _resSync = 0.2f;
                    if (!Mathf.Approximately(Mathf.Round(_res), _resCur.Value)) _resCur.Value = Mathf.Round(_res);
                }
            }

            if (_castSpell != null) UpdateCast(now);

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (now < _pending[i].Due) continue;
                PendingHit p = _pending[i];
                _pending.RemoveAt(i);
                Land(p);
            }
        }

        private void UpdateCast(float now)
        {
            Vector3 d = transform.position - _castFrom;
            d.y = 0f;
            if (_health != null && !_health.IsAlive) { CancelCast("", true); return; }
            if (d.sqrMagnitude > 0.35f * 0.35f) { CancelCast("Unterbrochen – du hast dich bewegt.", true); return; }
            if (_castSpell.target == SpellTarget.Enemy && (_castTarget == null || !_castTarget.IsAlive)) { CancelCast("Ziel verloren.", true); return; }
            if (now < _castUntil) return;
            SpellDefinition s = _castSpell;
            int rank = _castRank;
            Health target = _castTarget;
            // Am Ende noch einmal pruefen (Reichweite, Kosten) – dann wirkt er.
            // 23.10.2026 (Wirken sichtbar): erst NACH der Prüfung freigeben – vorher stand _castSpell hier schon auf null, CancelCast kehrte
            // sofort zurück, und weder Besitzer (Meldung) noch Beobachter (Wirkende) erfuhren vom Abbruch am Ende der Wirkzeit.
            string why = CheckTarget(s, target, out CombatTarget ct);
            if (why.Length == 0) why = CheckCost(s, rank);
            if (why.Length > 0) { CancelCast(why, true); return; }
            _castSpell = null;
            _castUntil = -1f;
            // 23.10.2026 (Wirken sichtbar): auch das erfolgreiche Ende an alle – Beobachter beenden die Wirkhaltung ausgelöst
            // (vorher nur beim Abbruch; das Ende las man fremden Spielern bisher nur am Effekt ab)
            ObserversCastEnd(false);
            if (Owner != null && Owner.IsActive) TargetCastEnd(Owner, false, "");
            Execute(s, rank, target, ct);
        }

        private void CancelCast(string message, bool interrupted)
        {
            if (_castSpell == null) return;
            _castSpell = null;
            _castUntil = -1f;
            ObserversCastEnd(true);
            if (Owner != null && Owner.IsActive) TargetCastEnd(Owner, interrupted, message ?? "");
        }

        // ================================================================== Ressource

        private void OnAnyDamagedForSpells(Health victim, DamageInfo info)
        {
            if (!Loaded || ServerResourceType != ResourceType.Rage) return;
            float amount = info.Amount + info.Absorbed;
            if (amount <= 0f) return;
            float gain = 1f + _talents.Get(TalentStat.RageGainPercent) / 100f;
            if (victim == _health) AddResource(SpellRules.RageFromTaken(amount, _health.Max) * gain);
            else if (info.Source == gameObject && info.WeaponId != null && !info.WeaponId.EndsWith(".dot"))
                AddResource(SpellRules.RageFromDealt(amount, Model.Level) * gain);
        }

        private void OnSpellOwnerDied()
        {
            CancelCast("", true);
            _nextSwing = null;
            if (ServerResourceType == ResourceType.Rage) _res = 0f;
            if (Owner != null && Owner.IsActive) TargetNextSwing(Owner, "");
        }

        /// <summary>Server: Ressource gutschreiben (Wut durch Ansturm, Tests).</summary>
        public void AddResource(float amount)
        {
            if (ServerResourceType == ResourceType.None) return;
            _res = Mathf.Clamp(_res + amount, 0f, Mathf.Max(0f, _resMax.Value));
        }

        /// <summary>Test: Ressource setzen.</summary>
        public void ServerSetResource(float value)
        {
            _res = Mathf.Clamp(value, 0f, Mathf.Max(0f, _resMax.Value));
            _resCur.Value = Mathf.Round(_res);
        }

        private void Spend(int cost)
        {
            if (cost <= 0) return;
            _res = Mathf.Max(0f, _res - cost);
            _resCur.Value = Mathf.Round(_res);
            if (ServerResourceType == ResourceType.Mana) _lastManaSpend = Time.time;
        }

        // ================================================================== Anfragen

        /// <summary>Besitzer: Zauber wirken (Leiste). Ziel = aktuelles Ziel der Zielwahl.</summary>
        public void RequestCast(string spellId)
        {
            if (!IsOwner || string.IsNullOrEmpty(spellId)) return;
            var pc = GetComponent<PlayerCharacter>();
            CombatTarget ct = pc != null && pc.Targeting != null ? pc.Targeting.Current : null;
            NetworkObject nob = ct != null ? ct.GetComponentInParent<NetworkObject>() : null;
            // Vorab auf dem Client: Bewegung waehrend Wirkzeit (klare Meldung ohne Rundreise)
            SpellDefinition s = GameData.Get<SpellDefinition>(spellId);
            if (s != null && !s.Instant && s.effect != SpellEffectKind.NextSwing)
            {
                var motor = GetComponent<CharacterMotor>();
                if (motor != null && motor.HorizontalSpeed > 0.6f) { CombatTextEvent.Error("Nicht während der Bewegung."); return; }
            }
            ServerCastSpell(spellId, nob);
        }

        [ServerRpc]
        private void ServerCastSpell(string spellId, NetworkObject targetNob)
        {
            Health target = null;
            if (targetNob != null)
            {
                target = targetNob.GetComponentInChildren<Health>();
                if (target == null) target = targetNob.GetComponentInParent<Health>();
            }
            string why = ServerCast(spellId, target);
            if (why.Length > 0 && Owner != null && Owner.IsActive) TargetSpellError(Owner, why);
        }

        /// <summary>Server: Zauber wirken (auch fuer Tests). Leer = angenommen, sonst der Grund.</summary>
        public string ServerCast(string spellId, Health target)
        {
            string why = TryCast(spellId, target);
            ServerLastSpellError = why;
            return why;
        }

        private string TryCast(string spellId, Health target)
        {
            if (!Loaded) return "Nicht bereit.";
            SpellDefinition s = GameData.Get<SpellDefinition>(spellId);
            if (s == null) return "Unbekannte Fähigkeit.";
            CharacterClassDefinition cls = ServerClass;
            if (cls == null || s.classId != cls.Id) return "Das kann deine Klasse nicht.";
            int rank = Mathf.Min(ServerSpellRank(s.Id), s.RankCount);
            if (rank <= 0) return "Das hast du noch nicht gelernt.";
            if (_health != null && !_health.IsAlive) return "Du bist tot.";
            float now = Time.time;

            // Heldenhafter Schlag: umschalten, kein GCD, Kosten erst beim Schlag
            if (s.effect == SpellEffectKind.NextSwing)
            {
                if (_nextSwing == s) { _nextSwing = null; if (Owner != null && Owner.IsActive) TargetNextSwing(Owner, ""); return ""; }
                string w = CheckRequirements(s);
                if (w.Length > 0) return w;
                if (_res < SpellRules.CostOf(s, s.Rank(rank), _talents)) return "Nicht genug " + SpellDefinition.ResourceName(ServerResourceType) + ".";
                _nextSwing = s;
                _nextSwingRank = rank;
                if (Owner != null && Owner.IsActive) TargetNextSwing(Owner, s.Id);
                ServerLastSpellResult = s.Id + ":queued";
                return "";
            }

            if (_castSpell != null) return "Du wirkst bereits.";
            if (s.triggersGcd && now < _gcdUntil - 0.12f) return "Noch nicht bereit.";
            if (_spellReady.TryGetValue(s.Id, out float ready) && now < ready - 0.12f) return "Noch nicht bereit.";
            string why = CheckRequirements(s);
            if (why.Length > 0) return why;
            if (s.outOfCombatOnly && (_mInCombat || ComputeInCombat(now)) && s.effect != SpellEffectKind.Charge) return "Nicht mitten im Kampf.";
            if (s.effect == SpellEffectKind.Charge && _health != null && now - _health.LastDamageTime < 5f) return "Nicht mitten im Kampf.";
            why = CheckCost(s, rank);
            if (why.Length > 0) return why;
            if (s.target == SpellTarget.Friendly && (target == null || !IsPlayer(target) || !target.IsAlive)) target = _health;
            if (s.target == SpellTarget.Self) target = _health;
            why = CheckTarget(s, target, out CombatTarget ct);
            if (why.Length > 0) return why;
            if (s.target == SpellTarget.Friendly && s.effect == SpellEffectKind.Absorb && target != null)
            {
                StatusEffects se = target.GetComponent<StatusEffects>();
                if (se != null && se.IsLocked(s.Id)) return "Das Ziel kann noch keinen neuen Schild tragen.";
            }
            if (ServerMounted) Dismount("Zauber");

            float cast = SpellRules.CastTimeOf(s, _talents);
            float gcd = s.triggersGcd ? SpellRules.GcdFor(ServerResourceType) : 0f;
            if (gcd > 0f) _gcdUntil = now + gcd;
            if (cast > 0.01f)
            {
                if (_mMotor != null && _mMotor.HorizontalSpeed > 0.8f) return "Nicht während der Bewegung.";
                _castSpell = s;
                _castRank = rank;
                _castTarget = target;
                _castStarted = now;
                _castUntil = now + cast;
                _castFrom = transform.position;
                if (Owner != null && Owner.IsActive)
                {
                    TargetSpellCooldown(Owner, s.Id, 0f, gcd);
                    TargetCastStart(Owner, s.Id, cast);
                }
                ObserversCastBegin(s.Id, cast);
                return "";
            }
            // KAMPF-04 (21.10.): die globale Abklingzeit auch bei Sofortzaubern MIT eigener Abklingzeit melden (vorher nur ohne) –
            // der Server setzt sie immer (oben), die Leiste zeigte sie aber nicht, und der naechste Knopf meldete „Noch nicht bereit.“.
            // Reihenfolge: zuerst (0, gcd), danach aus Execute (cd, 0); SpellClient.SetCooldown uebernimmt beides.
            if (Owner != null && Owner.IsActive && gcd > 0f) TargetSpellCooldown(Owner, s.Id, 0f, gcd);
            Execute(s, rank, target, ct);
            return "";
        }

        private string CheckRequirements(SpellDefinition s)
        {
            if ((s.requires & SpellRequirement.Shield) != 0 && (_sEq == null || !_sEq.HasShield)) return "Dafür brauchst du einen Schild.";
            if ((s.requires & SpellRequirement.RangedWeapon) != 0)
            {
                WeaponDefinition r = _sWu != null ? _sWu.RangedWeapon : null;
                if (r == null) return "Dafür brauchst du eine Fernkampfwaffe.";
                if (r.ammoType != AmmoType.None && _inv.AmmoCount(r.ammoType) <= 0) return "Keine Munition.";
            }
            if ((s.requires & SpellRequirement.MeleeWeapon) != 0)
            {
                WeaponDefinition m = _sWu != null ? _sWu.MeleeWeapon : null;
                if (m == null || !m.IsMelee || m == GameDataBootstrap.Unarmed) return "Dafür brauchst du eine Nahkampfwaffe.";
            }
            return "";
        }

        private string CheckCost(SpellDefinition s, int rank)
        {
            int cost = SpellRules.CostOf(s, s.Rank(rank), _talents);
            if (cost > 0 && _res + 0.01f < cost) return "Nicht genug " + SpellDefinition.ResourceName(ServerResourceType) + ".";
            return "";
        }

        private static bool IsPlayer(Health h) => h != null && h.GetComponentInParent<NetworkPlayer>() != null;

        private string CheckTarget(SpellDefinition s, Health target, out CombatTarget ct)
        {
            ct = null;
            switch (s.target)
            {
                case SpellTarget.Self:
                case SpellTarget.AreaAroundSelf:
                    return "";
                case SpellTarget.Enemy:
                {
                    ct = target != null ? target.GetComponentInParent<CombatTarget>() : null;
                    if (target == null || ct == null || !ct.IsAttackable || IsPlayer(target)) return "Kein gültiges Ziel.";
                    float dist = HorizontalDistance(transform.position, ct.transform.position) - ct.SelectionRadius;
                    float slack = s.range > 6f ? 2f : 1.5f;
                    if (dist > s.Range + slack) return "Außer Reichweite.";
                    if (s.minRange > 0f && dist < s.minRange - 1f) return "Zu nah.";
                    Vector3 to = ct.transform.position - transform.position;
                    to.y = 0f;
                    if (to.sqrMagnitude > 1f && Vector3.Dot(transform.forward, to.normalized) < -0.1f) return "Du musst dein Ziel ansehen.";
                    if (s.range > 6f && !HasSight(ct.AimPoint)) return "Das Ziel ist nicht in Sicht.";
                    return "";
                }
                case SpellTarget.Friendly:
                {
                    if (target == null || !target.IsAlive) return "Kein gültiges Ziel.";
                    if (target != _health && HorizontalDistance(transform.position, target.transform.position) > s.Range + 2f) return "Außer Reichweite.";
                    return "";
                }
                case SpellTarget.DeadFriendly:
                {
                    if (target == null || !IsPlayer(target) || target == _health) return "Wähle einen gefallenen Mitspieler.";
                    if (target.IsAlive) return "Das Ziel lebt.";
                    // 22.10. (INSTANZEN-03): Hält der Ring die Figur am Boden (Kolosseum, ServerHeldDown), belebt nur ein Mitstreiter aus
                    // der gemeinsamen Reserve wieder. Bisher holte „Ruhe der Asche“ sie an der Reserve vorbei zurück (Wipe-Regel umgangen),
                    // und die Anzeige „Du liegst am Boden“ blieb stehen. Systeme bleiben getrennt – keine Verrechnung mit der Reserve.
                    if (target.GetComponentInParent<NetworkPlayer>() is NetworkPlayer held && held.ServerHeldDown)
                        return "Im Ring holt nur ein Mitstreiter Gefallene zurück.";
                    if (HorizontalDistance(transform.position, target.transform.position) > s.Range + 2f) return "Außer Reichweite.";
                    return "";
                }
            }
            return "";
        }

        private bool HasSight(Vector3 aim)
        {
            Vector3 eye = transform.position + Vector3.up * 1.6f;
            int mask = GameLayers.CameraCollisionMask();
            return !Physics.Linecast(eye, aim, mask, QueryTriggerInteraction.Ignore);
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }

        // ================================================================== Wirkung

        private void Execute(SpellDefinition s, int rank, Health target, CombatTarget ct)
        {
            SpellRank r = s.Rank(rank);
            if (r == null) return;
            float now = Time.time;
            Spend(SpellRules.CostOf(s, r, _talents));
            float cd = SpellRules.CooldownOf(s, _talents);
            if (cd > 0f) _spellReady[s.Id] = now + cd;
            if (Owner != null && Owner.IsActive && cd > 0f) TargetSpellCooldown(Owner, s.Id, cd, 0f);
            ServerSpellsCast++;
            float power = SpellRules.PowerOf(s, _talents);
            EquipmentStats st = _sEq != null ? _sEq.Stats : default;
            NetworkObject targetNob = target != null ? target.GetComponentInParent<NetworkObject>() : null;
            var sb = new StringBuilder(s.Id).Append(':');

            switch (s.effect)
            {
                case SpellEffectKind.WeaponStrike:
                {
                    WeaponDefinition w = _sWu != null ? _sWu.MeleeWeapon : null;
                    float dmg = WeaponRoll(w, true) + Random.Range(r.min, r.max) * power;
                    bool crit = Random.value < st.CritChance + (w != null ? w.critChance : 0f);
                    if (crit) dmg *= w != null ? w.critMultiplier : 2f;
                    float dealt = Strike(s, target, dmg, crit, true);
                    sb.Append("dmg=").Append(Mathf.RoundToInt(dealt));
                    Sides(s, r, target, dealt);
                    ObserversSpellCast(s.Id, targetNob, target != null ? target.transform.position : transform.position, crit, dealt);
                    break;
                }
                case SpellEffectKind.RangedShot:
                {
                    WeaponDefinition w = _sWu != null ? _sWu.RangedWeapon : null;
                    if (w != null && w.ammoType != AmmoType.None) _inv.ServerConsumeAmmo(w.ammoType);
                    float bonus = WeaponUser.AmmoBonus(w, _inv);
                    var targets = new List<Health> { target };
                    if (s.radius > 0f && s.maxTargets > 1 && ct != null) CollectEnemies(ct.transform.position, s.radius, s.maxTargets, targets);
                    float flight = w != null ? HorizontalDistance(transform.position, target.transform.position) / Mathf.Max(10f, w.projectileSpeed) : 0.3f;
                    int n = 0;
                    foreach (Health h in targets)
                    {
                        // P2: halber Waffenschuss + Zusatzschaden (sonst zaehlte jeder Faehigkeitsschuss wie ein voller Bogenschuss)
                        float dmg = (WeaponRoll(w, false) + bonus) * RangedShotWeaponShare + Random.Range(r.min, r.max) * power;
                        bool crit = Random.value < st.CritChance + (w != null ? w.critChance : 0f);
                        if (crit) dmg *= w != null ? w.critMultiplier : 2f;
                        _pending.Add(new PendingHit { Due = now + flight, Spell = s, Rank = rank, Target = h, Amount = dmg, Crit = crit });
                        ObserversSpellCast(s.Id, h.GetComponentInParent<NetworkObject>(), h.transform.position, crit, 0f);
                        n++;
                    }
                    sb.Append("targets=").Append(n);
                    break;
                }
                case SpellEffectKind.SpellDamage:
                {
                    float mult = SpellRules.SpellPowerMultiplier(st, _talents, false) * power;
                    if (s.target == SpellTarget.AreaAroundSelf)
                    {
                        var list = new List<Health>();
                        CollectEnemies(transform.position, s.radius, s.maxTargets, list);
                        foreach (Health h in list)
                        {
                            // 22.10.2026 (FORTSCHRITT-06): auch der koerperliche Flaechenzweig (Donnerschlag) rechnet die Talentstaerke
                            // ein (Rollender Donner, SpellPowerPercent) – wie Schildhieb und Tooltip (SpellRules.Describe); ohne Talent power = 1
                            float dmg = Random.Range(r.min, r.max) * (s.school == SpellSchool.Physical ? st.DamageMultiplier(true) * power : mult);
                            bool crit = Random.value < (s.school == SpellSchool.Physical ? st.CritChance : SpellRules.SpellCrit(st));
                            if (crit) dmg *= s.school == SpellSchool.Physical ? 2f : 1.5f;
                            float dealt = Strike(s, h, dmg, crit, false);
                            Sides(s, r, h, dealt);
                        }
                        sb.Append("targets=").Append(list.Count);
                        ObserversSpellCast(s.Id, null, transform.position, false, list.Count);
                    }
                    else if (s.school == SpellSchool.Physical)
                    {
                        // Koerperlicher Schlag ohne Waffenschaden (Schildhieb): sofort, Staerke zaehlt
                        float dmg = Random.Range(r.min, r.max) * st.DamageMultiplier(true) * power;
                        bool crit = Random.value < st.CritChance;
                        if (crit) dmg *= 2f;
                        float dealt = Strike(s, target, dmg, crit, false);
                        Sides(s, r, target, dealt);
                        sb.Append("dmg=").Append(Mathf.RoundToInt(dealt));
                        ObserversSpellCast(s.Id, targetNob, target.transform.position, crit, dealt);
                    }
                    else
                    {
                        float dmg = Random.Range(r.min, r.max) * mult;
                        bool crit = Random.value < SpellRules.SpellCrit(st);
                        if (crit) dmg *= 1.5f;
                        float flight = HorizontalDistance(transform.position, target.transform.position) / 22f;
                        _pending.Add(new PendingHit { Due = now + flight, Spell = s, Rank = rank, Target = target, Amount = dmg, Crit = crit });
                        ObserversSpellCast(s.Id, targetNob, target.transform.position, crit, 0f);
                        sb.Append("flight=").Append(flight.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    break;
                }
                case SpellEffectKind.Heal:
                {
                    float amount = Random.Range(r.min, r.max) * SpellRules.SpellPowerMultiplier(st, _talents, true) * power;
                    bool crit = Random.value < SpellRules.SpellCrit(st);
                    if (crit) amount *= 1.5f;
                    float before = target.Current;
                    target.Heal(Mathf.Round(amount), gameObject);
                    float healed = target.Current - before;
                    HealThreat(target, healed);
                    sb.Append("heal=").Append(Mathf.RoundToInt(healed));
                    ObserversSpellCast(s.Id, targetNob, target.transform.position, crit, healed);
                    break;
                }
                case SpellEffectKind.Absorb:
                {
                    float amount = r.extra * SpellRules.SpellPowerMultiplier(st, _talents, true) * power;
                    StatusEffects se = StatusEffects.Ensure(target.gameObject);
                    se.AddAbsorb(Mathf.Round(amount), s.EffectDuration(r, 30f), s.DisplayName);
                    if (s.reapplyLockSeconds > 0f) se.Lock(s.Id, s.reapplyLockSeconds);
                    HealThreat(target, amount * 0.5f);
                    sb.Append("absorb=").Append(Mathf.RoundToInt(amount));
                    ObserversSpellCast(s.Id, targetNob, target.transform.position, false, amount);
                    break;
                }
                case SpellEffectKind.DamageReduction:
                {
                    StatusEffects.Ensure(gameObject).AddDamageReduction(r.extra / 100f, s.EffectDuration(r, 10f), s.DisplayName);
                    sb.Append("reduce=").Append(r.extra);
                    ObserversSpellCast(s.Id, null, transform.position, false, r.extra);
                    break;
                }
                case SpellEffectKind.Charge:
                {
                    Vector3 to = target.transform.position - transform.position;
                    to.y = 0f;
                    float dist = Mathf.Max(0f, to.magnitude - (ct != null ? ct.SelectionRadius : 0.5f) - 1.2f);
                    const float speed = 24f;
                    float t = dist / speed;
                    if (_mMotor != null && to.sqrMagnitude > 0.01f) _mMotor.StartDash(to.normalized * speed, t, 0f);
                    AddResource(r.extra);
                    _pending.Add(new PendingHit { Due = now + t, Spell = s, Rank = rank, Target = target, Amount = 0f, Stun = true });
                    sb.Append("dash=").Append(t.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                    ObserversSpellCast(s.Id, targetNob, target.transform.position, false, t);
                    break;
                }
                case SpellEffectKind.LeapBack:
                {
                    Vector3 back = -transform.forward;
                    back.y = 0f;
                    if (_mMotor != null) _mMotor.StartDash(back.normalized * 16f, 0.55f, 6.5f);
                    sb.Append("leap");
                    ObserversSpellCast(s.Id, null, transform.position, false, 0f);
                    break;
                }
                case SpellEffectKind.Resurrect:
                {
                    var np = target.GetComponentInParent<NetworkPlayer>();
                    if (np != null) np.ServerReviveHere(Mathf.Clamp01(r.extra / 100f));
                    sb.Append("revived");
                    ObserversSpellCast(s.Id, targetNob, target.transform.position, false, 0f);
                    break;
                }
                case SpellEffectKind.None:
                default:
                {
                    if (s.target == SpellTarget.AreaAroundSelf)
                    {
                        var list = new List<Health>();
                        CollectEnemies(transform.position, s.radius, s.maxTargets, list);
                        foreach (Health h in list) Sides(s, r, h, 0f);
                        sb.Append("targets=").Append(list.Count);
                        ObserversSpellCast(s.Id, null, transform.position, false, list.Count);
                    }
                    else
                    {
                        Sides(s, r, target, 0f);
                        sb.Append("effect");
                        ObserversSpellCast(s.Id, targetNob, target != null ? target.transform.position : transform.position, false, 0f);
                    }
                    break;
                }
            }
            if (s.effect != SpellEffectKind.Heal && s.effect != SpellEffectKind.Absorb && s.effect != SpellEffectKind.DamageReduction &&
                s.effect != SpellEffectKind.Resurrect && s.effect != SpellEffectKind.LeapBack && _mCombat != null)
                _mCombat.ServerMarkAttack();
            ServerLastSpellResult = sb.ToString();
        }

        /// <summary>Verzoegerter Treffer (Geschoss kommt an, Ansturm erreicht das Ziel).</summary>
        private void Land(PendingHit p)
        {
            if (p.Target == null || !p.Target.IsAlive) return;
            SpellRank r = p.Spell.Rank(p.Rank);
            if (p.Stun)
            {
                Sides(p.Spell, r, p.Target, 0f);
                return;
            }
            float dealt = Strike(p.Spell, p.Target, p.Amount, p.Crit, false);
            Sides(p.Spell, r, p.Target, dealt);
        }

        /// <summary>Anteil des Waffenschadens bei Fernkampf-Faehigkeiten (Brandpfeil, Mehrfachschuss).</summary>
        private const float RangedShotWeaponShare = 0.5f;

        private float WeaponRoll(WeaponDefinition w, bool melee)
        {
            if (w == null) return 0f;
            float mult = _sEq != null ? _sEq.OutgoingDamageMultiplier(melee) : 1f;
            return Random.Range(w.minDamage, w.maxDamage) * mult;
        }

        /// <summary>Schaden anwenden (Trefferwurf nach Stufe wie beim Waffenangriff). Gibt den angekommenen Schaden zurueck.</summary>
        private float Strike(SpellDefinition s, Health h, float amount, bool crit, bool dodgeable)
        {
            if (h == null || !h.IsAlive || amount <= 0f) return 0f;
            var ct = h.GetComponentInParent<CombatTarget>();
            Vector3 dir = h.transform.position - transform.position;
            dir.y = 0f;
            var info = new DamageInfo
            {
                Amount = Mathf.Max(1f, Mathf.Round(amount)),
                IsCritical = crit,
                Source = gameObject,
                HitPoint = ct != null ? ct.AimPoint : h.transform.position + Vector3.up,
                Direction = dir.sqrMagnitude > 1e-4f ? dir.normalized : transform.forward,
                WeaponId = s.Id,
                Dodgeable = dodgeable,
                Unblockable = s.school != SpellSchool.Physical
            };
            if (ct != null && Random.value > CombatResolver.HitChance(Model.Level, ct.Level))
            {
                h.RaiseMissed(info);
                return 0f;
            }
            float before = h.Current;
            h.ApplyDamage(info);
            return Mathf.Max(0f, before - h.Current);
        }

        /// <summary>Nebenwirkungen: Betaeubung, Verlangsamung, Spott, Brennen, Zusatzbedrohung.</summary>
        private void Sides(SpellDefinition s, SpellRank r, Health h, float dealt)
        {
            if (h == null || !h.IsAlive) return;
            var brain = h.GetComponentInParent<EnemyBrain>();
            float bonus = SpellRules.EffectBonusSeconds(s, _talents);
            if (s.stunSeconds > 0f && brain != null) brain.ServerStun(s.stunSeconds + bonus);
            if (s.slowFactor > 0f)
            {
                StatusEffects se = StatusEffects.Ensure(h.gameObject);
                if (brain != null && se.SpeedFactorSink == null) se.SpeedFactorSink = brain.ServerSetSlowFactor;
                se.AddSlow(s.slowFactor, s.EffectDuration(r, s.slowSeconds) + bonus, s.DisplayName);
            }
            if (s.tauntSeconds > 0f && brain != null) brain.ServerTaunt(_health, s.tauntSeconds);
            if (s.dotSeconds > 0f && r != null && r.extra > 0f)
            {
                float dur = s.EffectDuration(r, s.dotSeconds);
                float total = r.extra * SpellRules.PowerOf(s, _talents) *
                              (s.school == SpellSchool.Physical ? 1f : SpellRules.SpellPowerMultiplier(_sEq != null ? _sEq.Stats : default, _talents, false));
                DamageOverTime.Apply(h, gameObject, total / Mathf.Max(1f, dur), dur, s.Id + ".dot");
            }
            if (brain != null)
            {
                float extra = s.bonusThreat + dealt * Mathf.Max(0f, s.threatMultiplier - 1f);
                if (extra > 0f) brain.ServerAddThreat(_health, extra * (_sEq != null ? _sEq.ThreatMultiplier : 1f));
                else if (dealt <= 0f) brain.ServerAddThreat(_health, 1f); // auch reine Nebenwirkungen machen auf einen aufmerksam
            }
        }

        /// <summary>Heilung erzeugt Bedrohung bei allen Gegnern, die gerade gegen den Geheilten kaempfen (halbe Menge, geteilt).</summary>
        private void HealThreat(Health healed, float amount)
        {
            if (amount <= 0f || healed == null) return;
            var foes = new List<EnemyBrain>();
            foreach (EnemyBrain b in EnemyBrain.All)
                if (b != null && b.InCombat && (b.CurrentTarget == healed || b.ThreatOf(healed) > 0f)) foes.Add(b);
            if (foes.Count == 0) return;
            float each = amount * 0.5f / foes.Count;
            foreach (EnemyBrain b in foes) b.ServerAddThreat(_health, each);
        }

        private static readonly Collider[] AreaBuffer = new Collider[48];

        private void CollectEnemies(Vector3 center, float radius, int max, List<Health> into)
        {
            int mask = LayerMask.GetMask(GameLayers.Enemy);
            int n = Physics.OverlapSphereNonAlloc(center, radius + 1f, AreaBuffer, mask, QueryTriggerInteraction.Ignore);
            var candidates = new List<(Health h, float d)>();
            for (int i = 0; i < n; i++)
            {
                var ct = AreaBuffer[i].GetComponentInParent<CombatTarget>();
                if (ct == null || !ct.IsAttackable) continue;
                Health h = ct.Health;
                if (h == null || into.Contains(h)) continue;
                bool dup = false;
                foreach (var c in candidates) if (c.h == h) { dup = true; break; }
                if (dup) continue;
                float d = HorizontalDistance(center, ct.transform.position) - ct.SelectionRadius;
                if (d > radius) continue;
                if (Mathf.Abs(ct.transform.position.y - center.y) > 4f) continue;
                candidates.Add((h, d));
            }
            candidates.Sort((a, b) => a.d.CompareTo(b.d));
            foreach (var c in candidates)
            {
                if (max > 0 && into.Count >= max) break;
                into.Add(c.h);
            }
        }

        // ================================================================== Heldenhafter Schlag (naechster Schlag)

        /// <summary>
        /// Server (NetworkCombat, Hauptschlag des automatischen Angriffs): liegt ein „naechster Schlag“ bereit und reicht
        /// die Wut, wird er jetzt verbraucht. Gibt den Bonusschaden zurueck (0 = nichts).
        /// </summary>
        public float ServerConsumeNextSwing(out string spellId)
        {
            spellId = "";
            if (_nextSwing == null) return 0f;
            SpellDefinition s = _nextSwing;
            SpellRank r = s.Rank(_nextSwingRank);
            _nextSwing = null;
            if (Owner != null && Owner.IsActive) TargetNextSwing(Owner, "");
            int cost = SpellRules.CostOf(s, r, _talents);
            if (r == null || _res + 0.01f < cost) return 0f;
            Spend(cost);
            spellId = s.Id;
            ServerSpellsCast++;
            ServerLastSpellResult = s.Id + ":swing";
            ObserversSpellCast(s.Id, null, transform.position, false, 0f);
            return Random.Range(r.min, r.max) * SpellRules.PowerOf(s, _talents);
        }

        // ================================================================== Stand an den Besitzer

        private void SendSpellState(bool force)
        {
            if (!Loaded || Owner == null || !Owner.IsActive) return;
            var k = new StringBuilder();
            foreach (SpellSave s in D.spells) { if (k.Length > 0) k.Append(','); k.Append(s.id).Append(':').Append(s.rank); }
            var t = new StringBuilder();
            int spent = 0;
            foreach (TalentSave ts in D.talents) { if (t.Length > 0) t.Append(','); t.Append(ts.id).Append(':').Append(ts.rank); spent += ts.rank; }
            int free = Mathf.Max(0, TalentDefinition.PointsForLevel(Model.Level) - spent);
            string key = k + "|" + t + "|" + free;
            if (!force && key == _lastSpellState) return;
            _lastSpellState = key;
            TargetSpellState(Owner, k.ToString(), t.ToString(), free, spent);
        }

        [TargetRpc]
        private void TargetSpellState(NetworkConnection conn, string known, string talents, int free, int spent)
        {
            SpellClient.ApplyState(known, talents, free, spent);
            OnResourceChanged(0f, 0f, false);
            // Talente wirken auch auf der eigenen Anzeige (Charakterfenster)
            var eq = GetComponent<CharacterEquipment>();
            if (eq != null && !IsServerInitialized)
            {
                var tb = new TalentBonuses();
                foreach (var kv in SpellClient.Talents) tb.Add(GameData.Get<TalentDefinition>(kv.Key), kv.Value);
                eq.SetTalents(tb);
            }
        }

        [TargetRpc]
        private void TargetSpellCooldown(NetworkConnection conn, string spellId, float seconds, float gcd) => SpellClient.SetCooldown(spellId, seconds, gcd);

        [TargetRpc]
        private void TargetCastStart(NetworkConnection conn, string spellId, float seconds)
        {
            SpellDefinition s = GameData.Get<SpellDefinition>(spellId);
            SpellClient.BeginCast(spellId, seconds, s != null ? s.DisplayName : spellId);
        }

        [TargetRpc]
        private void TargetCastEnd(NetworkConnection conn, bool interrupted, string message)
        {
            SpellClient.EndCast(interrupted);
            if (!string.IsNullOrEmpty(message)) CombatTextEvent.Error(message);
        }

        [TargetRpc]
        private void TargetNextSwing(NetworkConnection conn, string spellId) => SpellClient.SetNextSwing(spellId);

        [TargetRpc]
        private void TargetSpellError(NetworkConnection conn, string message)
        {
            if (!string.IsNullOrEmpty(message)) CombatTextEvent.Error(message);
            SpellClient.LastError = message;
        }

        // ================================================================== Bild fuer alle

        [ObserversRpc]
        private void ObserversCastBegin(string spellId, float seconds)
        {
            SpellFx.CastBegin(gameObject, GameData.Get<SpellDefinition>(spellId), seconds);
            WirkenBeobachtet(true, false, seconds);
        }

        [ObserversRpc]
        private void ObserversCastEnd(bool interrupted)
        {
            SpellFx.CastEnd(gameObject, interrupted);
            WirkenBeobachtet(false, !interrupted, 0f);
        }

        // ------------------------------------------------------------------ Wirken für andere sichtbar (23.10.2026)
        // Max/Codex-Detailplan Kampfreaktionen: andere Spieler sollen das Zaubern sehen. Der Wirkzustand war nur dem Besitzer bekannt
        // (SpellClient über TargetCastStart/-End → CharacterAnimatorBridge.WirkenAktualisieren); die Beobachter-RPCs zeigten nur die Glut.
        // Jetzt stellen alle anderen Rechner die Wirkhaltung über dieselben, schon vorhandenen RPCs dar (kein neuer RPC):
        // Beginn → SetCasting(true), erfolgreiches Ende → SetCasting(false, ausgelöst), Abbruch/Unterbrechung/Tod → SetCasting(false).
        // Der Besitzer bleibt beim SpellClient-Weg (kein Doppel auf dem Host). Nur Darstellung, keine Spiellogik.

        private CharacterAnimatorBridge _wirkBridge;
        private int _wirkSerie;

        /// <summary>Darstellung: zeigt diese Figur auf diesem Rechner gerade die Wirkhaltung aus dem Netz?</summary>
        public bool WirkenSichtbar { get; private set; }
        /// <summary>Darstellung: Ende des zuletzt beobachteten Wirkens ausgelöst (true) oder abgebrochen (false).</summary>
        public bool WirkenZuletztAusgeloest { get; private set; }
        /// <summary>Prüfungen: auf diesem Rechner empfangene Wirk-Beginne/-Enden dieser Figur (Host zählt den RPC einmal).</summary>
        public int WirkenBeginneEmpfangen { get; private set; }
        public int WirkenEndenEmpfangen { get; private set; }

        private void WirkenBeobachtet(bool beginn, bool ausgeloest, float sekunden)
        {
            if (beginn) WirkenBeginneEmpfangen++;
            else WirkenEndenEmpfangen++;
            if (!beginn && !WirkenSichtbar) return;   // Ende ohne gesehenen Beginn (später Beobachter): nichts zu beenden
            WirkenSichtbar = beginn;
            if (!beginn) WirkenZuletztAusgeloest = ausgeloest;
            // Eigene Figur: Wirkzustand kommt aus SpellClient (TargetCastStart/-End) – hier nicht ein zweites Mal setzen
            if (!IsOwner)
            {
                if (_wirkBridge == null) _wirkBridge = GetComponentInChildren<CharacterAnimatorBridge>(true);
                if (_wirkBridge != null) _wirkBridge.SetCasting(beginn, ausgeloest);
            }
            if (beginn)
            {
                int serie = ++_wirkSerie;
                if (isActiveAndEnabled) StartCoroutine(WirkenSicherung(serie, sekunden));
            }
        }

        /// <summary>
        /// Netz-Sicherung: bleibt das Ende aus (darf nicht vorkommen – jeder Abbruch- und Erfolgsweg meldet es), endet die Haltung
        /// 2 s nach der geplanten Wirkzeit als abgebrochen, statt dauerhaft zu wirken.
        /// </summary>
        private System.Collections.IEnumerator WirkenSicherung(int serie, float sekunden)
        {
            yield return new WaitForSeconds(Mathf.Max(0.2f, sekunden) + 2f);
            if (serie != _wirkSerie || !WirkenSichtbar) yield break;
            WirkenSichtbar = false;
            WirkenZuletztAusgeloest = false;
            if (!IsOwner && _wirkBridge != null) _wirkBridge.SetCasting(false, false);
        }

        [ObserversRpc]
        private void ObserversSpellCast(string spellId, NetworkObject target, Vector3 point, bool crit, float value)
        {
            SpellDefinition s = GameData.Get<SpellDefinition>(spellId);
            if (s == null) return;
            GameObject t = target != null ? target.gameObject : null;
            SpellFx.Cast(gameObject, s, t, point, value);
            // Heilung: Zahlen fuer Wirker und Geheilten (Schaden zeigt NetworkHealth)
            if (s.effect == SpellEffectKind.Heal && value > 0f && (IsOwner || (target != null && target.IsOwner)))
            {
                Vector3 pos = (t != null ? t.transform.position : point) + Vector3.up * 2.1f;
                EventBus<CombatTextEvent>.Raise(new CombatTextEvent(pos, "+" + value.ToString("0") + (crit ? "!" : ""), CombatTextKind.Heal));
            }
            if (s.effect == SpellEffectKind.Absorb && value > 0f && (IsOwner || (target != null && target.IsOwner)))
            {
                Vector3 pos = (t != null ? t.transform.position : point) + Vector3.up * 2.1f;
                EventBus<CombatTextEvent>.Raise(new CombatTextEvent(pos, s.DisplayName + " " + value.ToString("0"), CombatTextKind.Heal));
            }
        }
    }
}
