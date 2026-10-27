using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Targeting;
using Ashveil.Gameplay.Weapons;
using FishNet.Object;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Serverautoritaerer Kampf fuer Spieler.
    ///
    ///   Client (Besitzer): WeaponUser rechnet Treffer nur voraus (Gefuehl) und meldet hier die ABSICHT:
    ///                      "Angriff Art/Kombo, von hier, in diese Richtung".
    ///   Server:            prueft Tempo, Abklingzeit, Position und Waffe, ermittelt die Treffer selbst
    ///                      (<see cref="CombatResolver"/>) und zieht Leben ab. Die Lebenspunkte verteilt
    ///                      <see cref="NetworkHealth"/>.
    ///   Andere Clients:    bekommen Funken, Leuchtspuren und Schockwellen zu sehen.
    /// </summary>
    [RequireComponent(typeof(WeaponUser))]
    [DisallowMultipleComponent]
    public sealed class NetworkCombat : NetworkBehaviour, IAttackAuthority
    {
        [Tooltip("Wie weit der gemeldete Angriffsursprung von der Figur entfernt sein darf (m)")]
        [SerializeField] private float maxOriginOffset = 3f;
        [Tooltip("Toleranz fuer Netzwerk-Schwankungen beim Angriffstempo (s)")]
        [SerializeField] private float timingTolerance = 0.12f;

        private WeaponUser _weapons;
        /// <summary>MEHRSPIELER-03 (21.10.): Leben des Angreifers – Gefallene greifen nicht an (Server).</summary>
        private Health _health;
        private readonly List<HitResult> _results = new List<HitResult>();
        private readonly List<Vector3> _directions = new List<Vector3>();

        // Server: Pruefzustand
        private float _nextMeleeTime, _nextShotTime;
        private readonly Dictionary<WeaponDefinition, float> _lastSpecial = new Dictionary<WeaponDefinition, float>();
        private int _rejected;
        // Server: Magazin je Fernkampfwaffe (die Zahl auf dem Client ist nur Anzeige)
        private readonly Dictionary<WeaponDefinition, int> _magazine = new Dictionary<WeaponDefinition, int>();
        private WeaponDefinition _reloadWeapon;
        private float _reloadDoneAt;

        /// <summary>Server: Zeitpunkt des letzten angenommenen Angriffs (M8: kein Emote im Kampf).</summary>
        public float ServerLastAttackTime { get; private set; } = -999f;

        /// <summary>Server: Klassenfaehigkeit gilt als Angriff (Kampfzustand, Absitzen, Emotes).</summary>
        public void ServerMarkAttack() => ServerLastAttackTime = Time.time;

        /// <summary>Server: abgelehnte Angriffe (Tests).</summary>
        public int ServerRejected => _rejected;

        public bool IsRemoteAuthority => IsClientInitialized && IsOwner;

        private TargetingSystem _targeting;

        private void Awake()
        {
            _weapons = GetComponent<WeaponUser>();
            _health = GetComponent<Health>();
        }

        // ================================================================== Boss-Anatomie: welches Koerperteil

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsOwner) return;
            var pc = GetComponent<PlayerCharacter>();
            _targeting = pc != null ? pc.Targeting : GetComponent<TargetingSystem>();
            if (_targeting != null) _targeting.AimChanged += OnAimChanged;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (_targeting != null) _targeting.AimChanged -= OnAimChanged;
            _targeting = null;
        }

        private void OnAimChanged(CombatTarget target, byte part)
        {
            NetworkObject nob = target != null ? target.GetComponent<NetworkObject>() : null;
            ServerAim(nob, nob != null ? part : (byte)0);
        }

        [ServerRpc]
        private void ServerAim(NetworkObject target, byte part)
        {
            var aim = GetComponent<BodyPartAim>();
            if (aim == null) aim = gameObject.AddComponent<BodyPartAim>();
            aim.Set(target != null ? target.gameObject : null, part);
        }

        /// <summary>Server (Tests): welches Teil dieser Spieler gerade anvisiert.</summary>
        public byte ServerAimedPart
        {
            get
            {
                var a = GetComponent<BodyPartAim>();
                return a != null ? a.Part : (byte)0;
            }
        }

        // ================================================================== Client -> Server

        public void SubmitMelee(AttackIntentKind kind, int comboIndex, Vector3 origin, Vector3 forward)
        {
            ServerMelee((byte)kind, (byte)Mathf.Clamp(comboIndex, 0, 255), origin, forward);
        }

        public void SubmitShot(AttackIntentKind kind, int comboIndex, Vector3 muzzle, IReadOnlyList<Vector3> directions)
        {
            int n = Mathf.Min(directions.Count, 32);
            var dirs = new Vector3[n];
            for (int i = 0; i < n; i++) dirs[i] = directions[i];
            ServerShot((byte)kind, (byte)Mathf.Clamp(comboIndex, 0, 255), muzzle, dirs);
        }

        public void SubmitReload() => ServerReload();

        public void SubmitProjectile(AttackIntentKind kind, int comboIndex, Vector3 origin, Vector3 direction, CombatTarget target)
        {
            NetworkObject nob = target != null ? target.GetComponentInParent<NetworkObject>() : null;
            ServerProjectile((byte)kind, (byte)Mathf.Clamp(comboIndex, 0, 255), origin, direction, nob);
        }

        // ================================================================== Server: Munition

        /// <summary>Server: Schuss im Magazin dieser Waffe (volle Magazine beim ersten Anlegen, wie auf dem Client).</summary>
        public int ServerAmmo(WeaponDefinition w)
        {
            if (w == null || !w.UsesAmmo) return 0;
            FinishReloadIfDue();
            return _magazine.TryGetValue(w, out int n) ? n : w.magazineSize;
        }

        /// <summary>Tests: Magazin auf dem Server setzen (z. B. leer), ohne den Client zu informieren.</summary>
        public void ServerSetAmmoForTest(WeaponDefinition w, int ammo)
        {
            if (w != null && w.UsesAmmo) _magazine[w] = Mathf.Clamp(ammo, 0, w.magazineSize);
        }

        private void FinishReloadIfDue()
        {
            if (_reloadWeapon == null || Time.time < _reloadDoneAt) return;
            _magazine[_reloadWeapon] = _reloadWeapon.magazineSize;
            _reloadWeapon = null;
        }

        [ServerRpc]
        private void ServerReload()
        {
            WeaponDefinition w = _weapons.RangedWeapon;
            if (w == null || !w.UsesAmmo) return;
            FinishReloadIfDue();
            if (_reloadWeapon != null) return;
            if (ServerAmmo(w) >= w.magazineSize) return;
            _reloadWeapon = w;
            // etwas Spielraum fuer Laufzeitschwankungen; ein Schuss vor Ablauf wird abgelehnt
            _reloadDoneAt = Time.time + w.reloadTime * 0.8f;
        }

        /// <summary>Server: einen Schuss aus dem Magazin nehmen. false = leer oder mitten im Nachladen.</summary>
        private bool TakeRound(WeaponDefinition w)
        {
            if (!w.UsesAmmo) return true;
            FinishReloadIfDue();
            if (_reloadWeapon == w) { Reject("laedt nach"); TargetAmmo(Owner, ServerAmmo(w)); return false; }
            int n = ServerAmmo(w);
            if (n <= 0) { Reject("Magazin leer"); TargetAmmo(Owner, 0); return false; }
            _magazine[w] = n - 1;
            return true;
        }

        [TargetRpc]
        private void TargetAmmo(FishNet.Connection.NetworkConnection conn, int ammo)
        {
            _weapons.CorrectAmmo(_weapons.RangedWeapon, ammo);
        }

        // ================================================================== Server

        [ServerRpc]
        private void ServerMelee(byte kindByte, byte comboIndex, Vector3 origin, Vector3 forward)
        {
            WeaponDefinition w = _weapons.MeleeWeapon;
            var kind = (AttackKind)kindByte;
            if (w == null || !w.IsMelee || kindByte > (byte)AttackKind.Plunge) { Reject("keine Nahkampfwaffe"); return; }
            AttackStep step = WeaponUser.StepFor(w, kind, comboIndex);
            if (!ValidateCommon(w, kind, step, origin, ref _nextMeleeTime)) return;
            // Beidhaendig: jeder zweite Kombo-Schlag kommt aus der Nebenhand (halber Schaden, deren Schadenswerte)
            WeaponDefinition striker = WeaponUser.StrikeWeapon(w, _weapons.OffhandWeapon, kind, comboIndex, out float scale);
            var req = new AttackRequest
            {
                Attacker = gameObject,
                Origin = origin,
                Forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : transform.forward,
                Weapon = striker,
                Step = step,
                LocalPlayer = false,
                RangeOverride = kind == AttackKind.Plunge ? w.plungeRadius : 0f,
                DamageScale = scale,
                // Max 03.10.2026: Standardangriffe (Schlagfolge, Luftschlag) treffen nur ein Ziel – bevorzugt das gemeldete Tab-Ziel
                SingleTarget = kind == AttackKind.Combo || kind == AttackKind.Air,
                PreferredTarget = GetComponent<BodyPartAim>() != null ? GetComponent<BodyPartAim>().Target : null
            };

            // P1 Heldenhafter Schlag: der naechste Hauptschlag der Schlagfolge bekommt den Bonus (Wut wird erst jetzt faellig)
            // KAMPF-03 (21.10.): nur verbrauchen, wenn der Schlag ein Ziel erreicht – erst vorausberechnen (PredictOnly: kein
            // Schaden, kein Stufen-Fehlwurf, gleiche Zielwahl), dann verbrauchen und echt aufloesen. Vorher kostete ein Schlag
            // ins Leere am Reichweitenrand die Wut, und der Bonus kam nie an.
            float swingBonus = 0f;
            if (kind == AttackKind.Combo && scale >= 0.99f)
            {
                var prog = GetComponent<NetworkProgression>();
                if (prog != null && prog.ServerQueuedNextSwing.Length > 0)
                {
                    req.PredictOnly = true;
                    if (CombatResolver.ResolveMelee(req, null) > 0) swingBonus = prog.ServerConsumeNextSwing(out _);
                    req.PredictOnly = false;
                }
            }
            req.BonusDamage = swingBonus;
            CombatResolver.ResolveMelee(req, _results);

            if (kind == AttackKind.Plunge) ObserversSlammed(transform.position, w.plungeRadius);
            else if (_results.Count > 0) ObserversMeleeStruck(PointsOf(_results), CritsOf(_results));
        }

        [ServerRpc]
        private void ServerShot(byte kindByte, byte comboIndex, Vector3 muzzle, Vector3[] directions)
        {
            WeaponDefinition w = _weapons.RangedWeapon;
            var kind = (AttackKind)kindByte;
            if (w == null || directions == null || directions.Length == 0) { Reject("keine Fernkampfwaffe"); return; }
            // 22.10.2026 (Abnahme Runde 4): Der Sofortschuss gehört nur zu Feuerwaffen (Hitscan, Streuung). Der echte Client schickt
            // ihn nur aus WeaponUser.PerformShot (weder Nahkampf noch Geschoss, WeaponUser.AdvancePhase); Bogen, Armbrust und Stab
            // (IsProjectile) gehen über SubmitProjectile → ServerProjectile mit Flugbahn und Pfeil-/Bolzenverbrauch. Vorher nahm der
            // Server einen Bogen hier als Sofortschuss an – Schaden ohne Pfeil und ohne Flugzeit (nur mit verändertem Client nutzbar).
            // Vor ValidateCommon: ein verworfener Schuss zählt nicht als Angriff und setzt keine Schusssperre.
            // 22.10.2026 (Runde 5): dieselbe Regel wie der Client (WeaponUser.AdvancePhase: weder IsMelee noch IsProjectile → PerformShot)
            // statt einer Positivliste (Hitscan/Spread) – ein künftiger Archetyp, den der Client als Schuss schickt, wird so nicht still verworfen.
            if (w.IsMelee || w.IsProjectile) { Reject("Waffentyp passt nicht zum Schuss"); return; }
            if (directions.Length > Mathf.Max(1, w.pellets)) { Reject("zu viele Kugeln"); return; }
            AttackStep step = WeaponUser.StepFor(w, kind, comboIndex);
            if (!ValidateCommon(w, kind, step, muzzle, ref _nextShotTime)) return;
            if (!TakeRound(w)) return;

            _directions.Clear();
            foreach (Vector3 d in directions) _directions.Add(d.sqrMagnitude > 1e-4f ? d.normalized : transform.forward);

            var req = new AttackRequest
            {
                Attacker = gameObject,
                Origin = muzzle,
                Forward = _directions[0],
                Weapon = w,
                Step = step,
                LocalPlayer = false
            };
            CombatResolver.ResolveShot(req, _directions, _results);

            var points = new Vector3[_results.Count];
            var hitFlags = new bool[_results.Count];
            for (int i = 0; i < _results.Count; i++)
            {
                points[i] = _results[i].Point;
                hitFlags[i] = _results[i].HitTarget;
            }
            ObserversFired(muzzle, points, hitFlags);
        }

        /// <summary>Welche Geschosswaffe fuehrt der Spieler gerade (Bogen im Fernkampfplatz oder Stab in der Haupthand)?</summary>
        private WeaponDefinition ProjectileWeapon()
        {
            WeaponDefinition a = _weapons.ActiveWeapon;
            if (a != null && a.IsProjectile) return a;
            if (_weapons.RangedWeapon != null && _weapons.RangedWeapon.IsProjectile) return _weapons.RangedWeapon;
            if (_weapons.MeleeWeapon != null && _weapons.MeleeWeapon.IsProjectile) return _weapons.MeleeWeapon;
            return null;
        }

        [ServerRpc]
        private void ServerProjectile(byte kindByte, byte comboIndex, Vector3 origin, Vector3 direction, NetworkObject targetNob)
        {
            WeaponDefinition w = ProjectileWeapon();
            var kind = (AttackKind)kindByte;
            if (w == null || kindByte > (byte)AttackKind.Special) { Reject("keine Geschosswaffe"); return; }
            var inv = GetComponent<NetworkInventory>();
            IRangedSupply supply = inv;
            AttackStep step = WeaponUser.EffectiveStep(w, kind, comboIndex, supply != null ? supply.RangedHaste : 0f);
            if (!ValidateCommon(w, kind, step, origin, ref _nextShotTime)) return;

            CombatTarget target = targetNob != null ? targetNob.GetComponentInChildren<CombatTarget>() : null;
            if (target != null && !target.IsAttackable) target = null;
            if (target != null)
            {
                Vector3 a = transform.position, b = target.transform.position;
                a.y = 0f; b.y = 0f;
                if (Vector3.Distance(a, b) - target.SelectionRadius > w.range + 3f) { Reject("Ziel ausser Reichweite"); return; }
            }
            // WoW: jeder Schuss verbraucht einen Pfeil der gewaehlten Sorte
            if (w.ammoType != AmmoType.None && inv != null && !inv.ServerConsumeAmmo(w.ammoType)) { Reject("keine Munition"); return; }

            Vector3 dir = direction.sqrMagnitude > 1e-4f ? direction.normalized : transform.forward;
            var req = new AttackRequest
            {
                Attacker = gameObject,
                Origin = origin,
                Forward = dir,
                Weapon = w,
                Step = step,
                LocalPlayer = false,
                BonusDamage = WeaponUser.AmmoBonus(w, supply)
            };
            ProjectileSystem.Launch(new ProjectileLaunch
            {
                Request = req,
                Origin = origin,
                Direction = dir,
                Target = target,
                Resolve = true,
                Visual = false,
                Look = WeaponUser.LookOf(w),
                Color = w.effectColor
            });
            ObserversProjectile(origin, dir, target != null ? targetNob : null);
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void ObserversProjectile(Vector3 origin, Vector3 dir, NetworkObject targetNob)
        {
            if (IsServerOnlyStarted) return;
            CombatTarget target = targetNob != null ? targetNob.GetComponentInChildren<CombatTarget>() : null;
            _weapons.ProxyProjectile(origin, dir, target);
        }

        /// <summary>Tempo, Abklingzeit und Position pruefen. Ungueltige Angriffe verfallen still.</summary>
        private bool ValidateCommon(WeaponDefinition w, AttackKind kind, AttackStep step, Vector3 origin, ref float nextAllowed)
        {
            // MEHRSPIELER-03 (21.10.): Gefallene (bis zum Erwachen, im Kolosseum am Boden festgehalten) greifen nicht an.
            // Vorher sperrte nur der Client (PlayerCharacter); Zauber, Beute, Emotes usw. pruefen das serverseitig laengst.
            // Steht vor dem Setzen von ServerLastAttackTime/nextAllowed – ein verworfener Angriff zaehlt nicht als Angriff.
            if (_health != null && !_health.IsAlive) { Reject("tot"); return false; }
            float now = Time.time;
            if (now + timingTolerance < nextAllowed) { Reject("zu schnell"); return false; }

            // Abklingzeit je Waffe (wie auf dem Client)
            if (kind == AttackKind.Special && _lastSpecial.TryGetValue(w, out float last) && now - last < w.specialCooldown * 0.85f)
            {
                Reject("Abklingzeit");
                return false;
            }

            if ((origin - transform.position).sqrMagnitude > maxOriginOffset * maxOriginOffset) { Reject("Position"); return false; }

            if (kind == AttackKind.Special) _lastSpecial[w] = now;
            ServerLastAttackTime = now;
            // Naechster Angriff fruehestens nach ~70 % der Schrittdauer (Kombos duerfen fluessig ineinander)
            nextAllowed = now + Mathf.Max(0.12f, step.Duration * 0.7f);
            return true;
        }

        private void Reject(string reason)
        {
            _rejected++;
            if (_rejected <= 5 || _rejected % 50 == 0)
                Debug.Log($"[Ashveil] Server: Angriff von Spieler {Owner.ClientId + 1} verworfen ({reason}).");
        }

        private static Vector3[] PointsOf(List<HitResult> results)
        {
            var p = new Vector3[results.Count];
            for (int i = 0; i < p.Length; i++) p[i] = results[i].Point;
            return p;
        }

        private static bool[] CritsOf(List<HitResult> results)
        {
            var c = new bool[results.Count];
            for (int i = 0; i < c.Length; i++) c[i] = results[i].IsCritical;
            return c;
        }

        // ================================================================== Effekte fuer andere Spieler

        [ObserversRpc(ExcludeOwner = true)]
        private void ObserversMeleeStruck(Vector3[] points, bool[] crits)
        {
            _results.Clear();
            for (int i = 0; i < points.Length; i++)
                _results.Add(new HitResult { Point = points[i], Normal = -transform.forward, HitTarget = true, IsCritical = i < crits.Length && crits[i] });
            _weapons.ProxyMeleeStruck(_results);
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void ObserversFired(Vector3 muzzle, Vector3[] points, bool[] hitTarget)
        {
            _results.Clear();
            for (int i = 0; i < points.Length; i++)
                _results.Add(new HitResult { Point = points[i], Normal = (muzzle - points[i]).normalized, HitTarget = i < hitTarget.Length && hitTarget[i] });
            _weapons.ProxyFired(muzzle, _results);
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void ObserversSlammed(Vector3 position, float radius)
        {
            _weapons.ProxySlammed(position, radius);
        }
    }
}
