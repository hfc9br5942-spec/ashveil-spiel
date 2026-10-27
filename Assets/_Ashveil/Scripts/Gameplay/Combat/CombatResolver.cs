using System.Collections.Generic;
using Ashveil.Core.Events;
using Ashveil.Data;
using UnityEngine;

namespace Ashveil.Gameplay.Combat
{
    /// <summary>Beschreibt einen einzelnen Angriff (Absicht + Geometrie).</summary>
    public struct AttackRequest
    {
        public GameObject Attacker;
        /// <summary>Startpunkt (Brust bzw. Muendung).</summary>
        public Vector3 Origin;
        /// <summary>Blick-/Schlagrichtung (horizontal fuer Nahkampf).</summary>
        public Vector3 Forward;
        public WeaponDefinition Weapon;
        public AttackStep Step;
        /// <summary>Kampftext fuer den lokalen Spieler erzeugen.</summary>
        public bool LocalPlayer;
        /// <summary>&gt; 0: Reichweite statt der Waffenreichweite (z. B. Schockwelle).</summary>
        public float RangeOverride;
        /// <summary>
        /// Nur vorausberechnen (Client mit Server-Autoritaet): Treffer fuer Effekte ermitteln,
        /// aber keinen Schaden anwenden und keine Zahlen zeigen – das macht der Server.
        /// </summary>
        public bool PredictOnly;
        /// <summary>Schadensfaktor (0 = 1). Nebenhand-Waffe: 0,5 wie in WoW.</summary>
        public float DamageScale;
        /// <summary>Zusatzschaden je Treffer vor Faktoren (Pfeile: Munitions-DPS × Angriffstempo).</summary>
        public float BonusDamage;
        /// <summary>
        /// Nur EIN Ziel treffen (Max 03.10.2026: „Standardangriffe duerfen kein AoE haben“). Gilt fuer Schlagfolge und
        /// Luftschlag; Faehigkeiten mit ausdruecklicher Flaeche (Sturzangriff) setzen es nicht.
        /// </summary>
        public bool SingleTarget;
        /// <summary>Bevorzugtes Ziel bei <see cref="SingleTarget"/> (Tab-Ziel); sonst das Ziel am naechsten vor dem Angreifer.</summary>
        public GameObject PreferredTarget;
    }

    /// <summary>Ein Treffer fuer Effekte (Funken, Blut, Tracer-Endpunkt).</summary>
    public struct HitResult
    {
        public Vector3 Point;
        public Vector3 Normal;
        public bool HitTarget;
        public bool IsCritical;
    }

    /// <summary>
    /// Trefferermittlung und Schadensberechnung.
    ///
    /// Das ist die Stelle, die spaeter AUF DEM SERVER laeuft: Clients schicken nur "Angriff X mit Waffe Y",
    /// der Server ruft diese Methoden auf und verteilt die Ergebnisse. Deshalb liest hier nichts aus
    /// Eingabe oder Kamera – alles kommt ueber <see cref="AttackRequest"/>.
    /// </summary>
    public static class CombatResolver
    {
        private static readonly Collider[] OverlapBuffer = new Collider[48];
        private static readonly HashSet<IDamageable> AlreadyHit = new HashSet<IDamageable>();
        private static readonly Dictionary<IDamageable, (float amount, bool crit, Vector3 point, GameObject go)> Aggregate =
            new Dictionary<IDamageable, (float, bool, Vector3, GameObject)>();

        /// <summary>Welt, die Schuesse und Schlaege blockiert (alles ausser Charakteren/Triggern).</summary>
        public static int BlockMask => Ashveil.Core.GameLayers.CameraCollisionMask();

        /// <summary>Was getroffen werden kann.</summary>
        public static int TargetMask => LayerMask.GetMask(Ashveil.Core.GameLayers.Enemy, Ashveil.Core.GameLayers.Interactable, Ashveil.Core.GameLayers.Player);

        // ------------------------------------------------------------------ Nahkampf

        public static int ResolveMelee(in AttackRequest req, List<HitResult> results)
        {
            results?.Clear();
            AlreadyHit.Clear();
            WeaponDefinition w = req.Weapon;
            Vector3 forward = Flatten(req.Forward);
            float halfArc = req.Step.arcAngle * 0.5f;
            int hitCount = 0;

            float range = req.RangeOverride > 0f ? req.RangeOverride : w.range;
            int n = Physics.OverlapSphereNonAlloc(req.Origin, range, OverlapBuffer, TargetMask, QueryTriggerInteraction.Ignore);
            // Einzelziel: zuerst das gueltige Ziel waehlen (Tab-Ziel, sonst das naechste vor dem Angreifer), nur dieses treffen
            int only = req.SingleTarget ? PickSingle(req, n, forward, halfArc) : -1;
            if (req.SingleTarget && only < 0) return 0;
            for (int i = 0; i < n; i++)
            {
                if (req.SingleTarget && i != only) continue;
                Collider col = OverlapBuffer[i];
                if (IsSelf(req.Attacker, col)) continue;

                var target = col.GetComponentInParent<IDamageable>();
                if (target == null || !target.IsAlive || AlreadyHit.Contains(target)) continue;
                if (!CanAttack(col) || IsFriendlyPlayer(req.Attacker, col)) continue;

                // bounds statt ClosestPoint: funktioniert auch mit nicht-konvexen MeshCollidern
                Vector3 point = col.bounds.ClosestPoint(req.Origin);
                Vector3 to = Flatten(point - req.Origin);
                float dist = to.magnitude;
                if (dist > 0.6f && Vector3.Angle(forward, to) > halfArc) continue;
                if (Mathf.Abs(point.y - req.Origin.y) > 2.2f) continue;
                if (Physics.Linecast(req.Origin, point, BlockMask & ~TargetMask, QueryTriggerInteraction.Ignore)) continue;

                AlreadyHit.Add(target);
                DamageInfo info = Roll(req, point, dist > 0.01f ? to / dist : forward);
                info.PartId = BodyPartAim.PartFor(req.Attacker, col);
                if (!req.PredictOnly && RollLevelMiss(req.Attacker, col))
                {
                    AnnounceMiss(req, info, col);
                    hitCount++;
                    continue;
                }
                if (!req.PredictOnly)
                {
                    target.ApplyDamage(info);
                    Announce(req, info, col);
                    GrantLifeOnHit(req);
                }
                results?.Add(new HitResult { Point = point, Normal = -forward, HitTarget = true, IsCritical = info.IsCritical });
                hitCount++;
            }
            return hitCount;
        }

        /// <summary>
        /// Index des einen Ziels fuer einen Einzelziel-Schlag in <see cref="OverlapBuffer"/> (gleiche Pruefungen wie im
        /// Flaechenschlag). Bevorzugt das Tab-Ziel; sonst das Ziel mit dem kleinsten Abstand, gewichtet nach Winkel. -1 = keins.
        /// </summary>
        private static int PickSingle(in AttackRequest req, int n, Vector3 forward, float halfArc)
        {
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                Collider col = OverlapBuffer[i];
                if (IsSelf(req.Attacker, col)) continue;
                var target = col.GetComponentInParent<IDamageable>();
                if (target == null || !target.IsAlive) continue;
                if (!CanAttack(col) || IsFriendlyPlayer(req.Attacker, col)) continue;
                Vector3 point = col.bounds.ClosestPoint(req.Origin);
                Vector3 to = Flatten(point - req.Origin);
                float dist = to.magnitude;
                float angle = dist > 0.6f ? Vector3.Angle(forward, to) : 0f;
                if (angle > halfArc) continue;
                if (Mathf.Abs(point.y - req.Origin.y) > 2.2f) continue;
                if (Physics.Linecast(req.Origin, point, BlockMask & ~TargetMask, QueryTriggerInteraction.Ignore)) continue;
                if (req.PreferredTarget != null && IsSameTarget(target, req.PreferredTarget)) return i;
                float score = dist * (1f + angle / 90f);
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        private static bool IsSameTarget(IDamageable target, GameObject preferred)
        {
            if (!(target is Component c) || preferred == null) return false;
            Transform t = c.transform, p = preferred.transform;
            return t == p || t.IsChildOf(p) || p.IsChildOf(t);
        }

        // ------------------------------------------------------------------ Schuss (Hitscan / Streuung)

        /// <summary>
        /// Schiesst einen oder mehrere Strahlen. Mehrere Kugeln auf dasselbe Ziel werden
        /// zu einer Schadenszahl zusammengefasst.
        /// </summary>
        public static int ResolveShot(in AttackRequest req, IReadOnlyList<Vector3> directions, List<HitResult> results)
        {
            results?.Clear();
            Aggregate.Clear();
            WeaponDefinition w = req.Weapon;
            int mask = BlockMask | TargetMask;
            int hits = 0;

            for (int d = 0; d < directions.Count; d++)
            {
                Vector3 dir = directions[d].normalized;
                if (!RaycastIgnoringSelf(req.Attacker, req.Origin, dir, w.range, mask, out RaycastHit hit))
                {
                    results?.Add(new HitResult { Point = req.Origin + dir * w.range, Normal = -dir, HitTarget = false });
                    continue;
                }

                var target = hit.collider.GetComponentInParent<IDamageable>();
                bool valid = target != null && target.IsAlive && CanAttack(hit.collider) && !IsFriendlyPlayer(req.Attacker, hit.collider);
                results?.Add(new HitResult { Point = hit.point, Normal = hit.normal, HitTarget = valid });
                if (!valid) continue;

                // Schaden faellt mit der Distanz leicht ab (voll bis 40 % der Reichweite, min. 60 %).
                float falloff = Mathf.Lerp(1f, 0.6f, Mathf.InverseLerp(w.range * 0.4f, w.range, hit.distance));
                DamageInfo roll = Roll(req, hit.point, dir);
                roll.Amount *= falloff / Mathf.Max(1, w.pellets);

                if (Aggregate.TryGetValue(target, out var agg))
                    Aggregate[target] = (agg.amount + roll.Amount, agg.crit || roll.IsCritical, agg.point, agg.go);
                else
                    Aggregate[target] = (roll.Amount, roll.IsCritical, hit.point, hit.collider.gameObject);
            }

            foreach (KeyValuePair<IDamageable, (float amount, bool crit, Vector3 point, GameObject go)> pair in Aggregate)
            {
                Collider hitCol = pair.Value.go != null ? pair.Value.go.GetComponent<Collider>() : null;
                if (!req.PredictOnly && hitCol != null && RollLevelMiss(req.Attacker, hitCol))
                {
                    AnnounceMiss(req, new DamageInfo { Source = req.Attacker, HitPoint = pair.Value.point, WeaponId = req.Weapon.Id }, hitCol);
                    hits++;
                    continue;
                }
                Vector3 dir = Flatten(pair.Value.point - req.Origin).normalized;
                var info = new DamageInfo
                {
                    Amount = Mathf.Max(1f, Mathf.Round(pair.Value.amount)),
                    IsCritical = pair.Value.crit,
                    Source = req.Attacker,
                    HitPoint = pair.Value.point,
                    Direction = dir,
                    Knockback = req.Step.knockback,
                    WeaponId = req.Weapon.Id,
                    PartId = BodyPartAim.PartFor(req.Attacker, pair.Value.go != null ? pair.Value.go.transform : null)
                };
                if (!req.PredictOnly)
                {
                    pair.Key.ApplyDamage(info);
                    Announce(req, info, pair.Value.go.GetComponent<Collider>());
                    GrantLifeOnHit(req);
                }
                hits++;
            }
            return hits;
        }

        // ------------------------------------------------------------------ Geschoss (Pfeil / Zauber)

        /// <summary>
        /// Ein angekommenes Geschoss trifft genau EIN Ziel (Pfeil, Zauberkugel). Wird auf dem Server beim Eintreffen
        /// aufgerufen – gezielte Schuesse (WoW) wie freie Flugbahnen enden hier.
        /// </summary>
        public static bool ResolveProjectileHit(in AttackRequest req, Collider col, Vector3 point, Vector3 dir)
        {
            if (col == null || IsSelf(req.Attacker, col)) return false;
            var target = col.GetComponentInParent<IDamageable>();
            if (target == null || !target.IsAlive || !CanAttack(col) || IsFriendlyPlayer(req.Attacker, col)) return false;
            DamageInfo info = Roll(req, point, dir.sqrMagnitude > 1e-6f ? dir.normalized : req.Forward);
            info.PartId = BodyPartAim.PartFor(req.Attacker, col);
            if (!req.PredictOnly && RollLevelMiss(req.Attacker, col))
            {
                AnnounceMiss(req, info, col);
                return true;
            }
            if (!req.PredictOnly)
            {
                target.ApplyDamage(info);
                Announce(req, info, col);
                GrantLifeOnHit(req);
            }
            return true;
        }

        // ------------------------------------------------------------------ Hilfen

        /// <summary>Ausruestung "Leben bei Treffer" (nur dort, wo Schaden wirklich angewendet wird).</summary>
        private static void GrantLifeOnHit(in AttackRequest req)
        {
            if (req.Attacker == null) return;
            ICombatModifiers mods = req.Attacker.GetComponent<ICombatModifiers>();
            if (mods == null || mods.LifeOnHit <= 0f) return;
            Health h = req.Attacker.GetComponent<Health>();
            if (h != null) h.Heal(mods.LifeOnHit, req.Attacker);
        }

        private static DamageInfo Roll(in AttackRequest req, Vector3 point, Vector3 dir)
        {
            WeaponDefinition w = req.Weapon;
            float amount = (Random.Range(w.minDamage, w.maxDamage) + req.BonusDamage) * req.Step.damageMultiplier;
            if (req.DamageScale > 0f) amount *= req.DamageScale;
            ICombatModifiers mods = req.Attacker != null ? req.Attacker.GetComponent<ICombatModifiers>() : null;
            if (mods != null) amount *= mods.OutgoingDamageMultiplier(w.IsMelee);
            bool crit = Random.value < w.critChance + (mods != null ? mods.CritChanceBonus : 0f);
            if (crit) amount *= w.critMultiplier;
            return new DamageInfo
            {
                Amount = Mathf.Max(1f, Mathf.Round(amount)),
                IsCritical = crit,
                Source = req.Attacker,
                HitPoint = point,
                Direction = dir,
                Knockback = req.Step.knockback,
                WeaponId = w.Id,
                Dodgeable = w.IsMelee
            };
        }

        // ------------------------------------------------------------------ Stufenunterschied

        /// <summary>
        /// Trefferchance gegen deutlich hoehere Gegner: bis 4 Stufen Unterschied immer 100 %, danach
        /// je Stufe 7 % weniger, mindestens 5 % (Glockenhirte Stufe 34 gegen Stufe 5: 5 %).
        /// </summary>
        public static float HitChance(int attackerLevel, int targetLevel)
        {
            int diff = targetLevel - attackerLevel;
            if (diff <= 4) return 1f;
            return Mathf.Max(0.05f, 1f - 0.07f * (diff - 4));
        }

        /// <summary>Test/Diagnose: Anzahl Fehlschlaege durch Stufenunterschied.</summary>
        public static int LevelMisses { get; set; }

        private static bool RollLevelMiss(GameObject attacker, Collider col)
        {
            if (attacker == null || col == null) return false;
            var ct = col.GetComponentInParent<CombatTarget>();
            ICombatModifiers mods = attacker.GetComponent<ICombatModifiers>();
            if (ct == null || mods == null) return false;
            bool miss = Random.value > HitChance(mods.AttackerLevel, ct.Level);
            if (miss) LevelMisses++;
            return miss;
        }

        private static void AnnounceMiss(in AttackRequest req, in DamageInfo info, Collider col)
        {
            Health h = col != null ? col.GetComponentInParent<Health>() : null;
            if (h != null) h.RaiseMissed(info); // Netz: der Angreifer bekommt "Verfehlt" angezeigt
            if (!req.LocalPlayer) return;
            var ct = col != null ? col.GetComponentInParent<CombatTarget>() : null;
            Vector3 pos = ct != null ? ct.AimPoint + Vector3.up * 0.6f : (col != null ? col.bounds.center : Vector3.zero);
            EventBus<CombatTextEvent>.Raise(new CombatTextEvent(pos, "Verfehlt", CombatTextKind.Miss));
        }

        private static void Announce(in AttackRequest req, in DamageInfo info, Collider col)
        {
            if (!req.LocalPlayer) return;
            Vector3 pos = info.HitPoint;
            var ct = col != null ? col.GetComponentInParent<CombatTarget>() : null;
            if (ct != null) pos = ct.AimPoint + Vector3.up * 0.6f;
            EventBus<CombatTextEvent>.Raise(new CombatTextEvent(pos, info.Amount.ToString("0"),
                info.IsCritical ? CombatTextKind.Critical : CombatTextKind.Damage));
        }

        private static bool CanAttack(Collider col)
        {
            var ct = col.GetComponentInParent<CombatTarget>();
            return ct == null || FactionRules.IsAttackableByPlayer(ct.Faction);
        }

        /// <summary>Spieler treffen keine anderen Spieler (kein PvP im Vertical Slice).</summary>
        private static bool IsFriendlyPlayer(GameObject attacker, Collider col)
        {
            if (attacker == null || col == null) return false;
            int player = LayerMask.NameToLayer(Ashveil.Core.GameLayers.Player);
            return player >= 0 && attacker.layer == player && col.gameObject.layer == player;
        }

        private static bool IsSelf(GameObject attacker, Collider col)
        {
            return attacker != null && col.transform.IsChildOf(attacker.transform);
        }

        private static readonly RaycastHit[] RayBuffer = new RaycastHit[16];

        private static bool RaycastIgnoringSelf(GameObject attacker, Vector3 origin, Vector3 dir, float range, int mask, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(origin, dir, RayBuffer, range, mask, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                if (IsSelf(attacker, RayBuffer[i].collider)) continue;
                if (RayBuffer[i].distance < bestDist)
                {
                    bestDist = RayBuffer[i].distance;
                    best = RayBuffer[i];
                    found = true;
                }
            }
            return found;
        }

        private static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
