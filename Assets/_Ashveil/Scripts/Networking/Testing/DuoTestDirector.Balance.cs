using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Weapons;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// P2 Balance (30.09.): misst echte Kaempfe im Spiel. Eine Figur (Editor-Host) kaempft als Eidwaechter, Aschenjaeger
    /// (Bogen und Nahkampf) und Aschenweber auf den Stufen 1, 3, 5, 8, 10, 12 – mit der besten verfuegbaren Ausruestung bis
    /// zu dieser Stufe, allen Klassenfaehigkeiten und einer einfachen Rotation – gegen einen normalen Gegner gleicher Stufe
    /// (ab Stufe 3 je zwei Kaempfe, die Spreizung der Klassen je Stufe vergleicht die Mittel).
    /// Gemessen: Zeit bis zum Sieg, Schaden je Sekunde (Waffe/Zauber), verlorenes Leben. Ziel ist die Balance-Tabelle
    /// (<see cref="Balance"/>). Start: "Ashveil › Test › Balance (1 Spieler)". Bericht: Logs/Ashveil/Ashveil_BalanceTest.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string BalanceFlag = "balance";
        private static bool _balanceMode;

        private static readonly int[] BalanceLevels = { 1, 3, 5, 8, 10, 12 };

        private sealed class FightResult
        {
            public string Config;
            public int Level;
            public float Time, WeaponDamage, SpellDamage, Taken, HealthMax, EnemyMax;
            public bool Won, Died;
            public string Gear;
            public float Dps => Time > 0f ? (WeaponDamage + SpellDamage) / Time : 0f;
        }

        private readonly List<FightResult> _fights = new List<FightResult>();
        private Health _balEnemy, _balPlayer;
        private float _balWeapon, _balSpell, _balTaken;

        private IEnumerator BalanceSuite()
        {
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(ArenaProfiles[0])) != null, 30f);
            Check(a != null, "Spieler angemeldet");
            if (a == null) yield break;
            yield return new WaitForSecondsRealtime(2f);

            // Echte Kaempfe: passives Ausweichen und Gegner-Ausweichen wie im Spiel
            CharacterEquipment.DodgeEnabled = true;
            EnemyBrain.DodgeChance = 0.05f;

            Line("");
            Line("[Tabelle]");
            foreach (int l in BalanceLevels)
                Line($"  Stufe {l,2}: Referenz {Balance.RefDps(l):0} Schaden/s, {Balance.RefHealth(l):0} Leben · normaler Gegner {Balance.MobHealth(l):0} Leben, " +
                     $"{Balance.MobDps(l):0.0} Schaden/s, Ziel {Balance.TargetTtk(l):0.0} s · Erfahrung je Sieg {Progression.KillXp(l, l)}");

            foreach (int l in new[] { 5, 10, 14 })
            {
                Vector2 el = Balance.RoleFactors(BalanceRole.Elite), bo = Balance.RoleFactors(BalanceRole.DungeonBoss);
                Line($"  Gruppe Stufe {l,2}: Elite {Balance.MobHealth(l) * el.x:0} Leben → mit 3 Spielern {Balance.MobHealth(l) * el.x / (3f * Balance.RefDps(l)):0} s · " +
                     $"Dungeon-Boss {Balance.MobHealth(l) * bo.x:0} Leben → mit 4 Spielern {Balance.MobHealth(l) * bo.x / (4f * Balance.RefDps(l)):0} s, " +
                     $"trifft den Tank mit {Balance.MobDps(l) * bo.y:0} Schaden/s (vor Rüstung)");
            }

            CharacterClassDefinition warden = GameData.Class("class.warden"), stalker = GameData.Class("class.ashstalker"), weaver = GameData.Class("class.emberweaver");
            Health.AnyDamaged += OnBalanceDamage;
            var configs = new (string name, CharacterClassDefinition cls, bool bow)[]
            {
                ("Eidwächter", warden, false), ("Aschenjäger Bogen", stalker, true), ("Aschenjäger Nahkampf", stalker, false), ("Aschenweber", weaver, false)
            };
            foreach (var c in configs)
            {
                Line("");
                Line($"[{c.name}]");
                foreach (int l in BalanceLevels)
                {
                    yield return Fight(a, c.name, c.cls, c.bow, l);
                    // Ab Stufe 3 ein zweiter Kampf: Die Spreizung je Stufe vergleicht Mittelwerte. Ein einzelner Kampf von 6–18 s
                    // streut mit Krits, Ausweichen und Laufwegen zu stark (Läufe 06./07.10.: ×1,69 auf Stufe 3, dann ×1,67 auf 8 und 12).
                    if (l > 1) yield return Fight(a, c.name, c.cls, c.bow, l);
                }
            }
            Health.AnyDamaged -= OnBalanceDamage;

            // ---------------------------------------------------------- Auswertung
            Line("");
            Line("[Auswertung]");
            foreach (var c in configs)
            {
                var fs = _fights.Where(f => f.Config == c.name).ToList();
                if (fs.Count == 0) continue;
                float ratio = fs.Average(f => f.Dps / Balance.RefDps(f.Level));
                Check(ratio > 0.8f && ratio < 1.25f, $"{c.name}: Schaden im Schnitt {ratio * 100f:0} % der Referenz (Ziel 80–125 %)");
                Check(fs.All(f => f.Won && !f.Died), $"{c.name}: gewinnt jeden Kampf gleicher Stufe allein ({fs.Count(f => f.Won)}/{fs.Count})");
                float lost = fs.Average(f => f.Taken / Mathf.Max(1f, f.HealthMax));
                Check(lost > 0.12f && lost < 0.7f, $"{c.name}: ein Kampf kostet im Schnitt {lost * 100f:0} % Leben (Ziel 12–70 %)");
            }
            var all = _fights.Where(f => f.Won).ToList();
            if (all.Count > 0)
            {
                float ttkRatio = all.Average(f => f.Time / (Balance.TargetTtk(f.Level) * f.EnemyMax / Mathf.Max(1f, Balance.MobHealth(f.Level))));
                Check(ttkRatio > 0.75f && ttkRatio < 1.35f, $"Zeit bis zum Sieg im Schnitt {ttkRatio * 100f:0} % der Zielzeit (Ziel 75–135 %)");
            }
            float spread = 0f;
            // Stufe 1 (Einstieg, noch keine Klassenfaehigkeiten ausser der ersten) zaehlt nicht mit
            foreach (int l in BalanceLevels.Where(x => x > 1))
            {
                // Mittel je Klasse (zwei Kämpfe), dann größter gegen kleinsten Wert dieser Stufe
                var fs = _fights.Where(f => f.Level == l && f.Won).GroupBy(f => f.Config).Select(g => g.Average(f => f.Dps)).ToList();
                if (fs.Count > 1) spread = Mathf.Max(spread, fs.Max() / Mathf.Max(1f, fs.Min()));
            }
            Check(spread > 0f && spread < 1.6f, $"Klassen liegen ab Stufe 3 je Stufe nah beieinander (größter Abstand ×{spread:0.00}, Ziel unter ×1,6)");
            Cleanup();
        }

        // ================================================================== Gruppe gegen Elite (P2: 45–90 s in der Gruppe)

        public const string BalanceGroupFlag = "balancegroup";
        private static bool _balanceGroupMode;

        /// <summary>
        /// Drei echte Spieler auf Stufe 10 (Eidwaechter Tank, Aschenjaegerin Bogen, Aschenweberin Heilung) gegen einen
        /// Elite-Gegner der Stufe 10 (Grubenschrat, Rolle Elite). Gemessen: Dauer, niedrigstes Leben des Tanks, Tode,
        /// wer die Bedrohung hat, Mana der Heilerin.
        /// </summary>
        private IEnumerator BalanceGroupSuite()
        {
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(ArenaProfiles[0])) != null, 30f);
            Check(a != null, "Tank (Editor-Host) angemeldet");
            if (a == null) yield break;
            string exe = Ashveil.Core.ProjectPaths.TestClientExe;
            if (!System.IO.File.Exists(exe)) { Check(false, "Test-Client vorhanden"); yield break; }
            StartClient(exe, ArenaProfiles[1]);
            StartClient(exe, ArenaProfiles[2]);
            NetworkInventory b = null, c = null;
            yield return WaitUntil(() => (b = OnlineInv(ArenaProfiles[1])) != null && (c = OnlineInv(ArenaProfiles[2])) != null, 180f);
            Check(b != null && c != null, "Jägerin und Heilerin (Test-Clients) angemeldet");
            if (b == null || c == null) yield break;
            yield return new WaitForSecondsRealtime(2f);
            CharacterEquipment.DodgeEnabled = true;
            EnemyBrain.DodgeChance = 0.05f;

            const int L = 10;
            CharacterClassDefinition warden = GameData.Class("class.warden"), stalker = GameData.Class("class.ashstalker"), weaver = GameData.Class("class.emberweaver");
            var ap = a.GetComponent<NetworkProgression>(); var bp = b.GetComponent<NetworkProgression>(); var cp = c.GetComponent<NetworkProgression>();
            Line("");
            Line("[Aufstellung]");
            Line($"  Tank: {EquipReference(a, ap, warden, L, false)} · Jägerin: {EquipReference(b, bp, stalker, L, true)} · Heilerin: {EquipReference(c, cp, weaver, L, false)}");
            yield return new WaitForSecondsRealtime(1.5f);
            foreach ((NetworkProgression p, CharacterClassDefinition cls) in new[] { (ap, warden), (bp, stalker), (cp, weaver) })
                foreach (SpellDefinition s in GameData.Database.All.OfType<SpellDefinition>().Where(s => s.classId == cls.Id))
                {
                    int rank = s.ranks.Count(r => r.level <= L);
                    if (rank > 0) p.ServerDebugLearn(s.Id, rank);
                }
            Health ah = a.GetComponent<Health>(), bh = b.GetComponent<Health>(), ch = c.GetComponent<Health>();
            foreach (Health h in new[] { ah, bh, ch }) h.Heal(h.Max);
            ap.ServerSetResource(0f); bp.ServerSetResource(100f); cp.ServerSetResource(100000f);
            yield return new WaitForSecondsRealtime(1f);

            Vector3 start = Gnd(470f, 110f);
            a.GetComponent<NetworkMotion>().ServerTeleport(start, 0f);
            b.GetComponent<NetworkMotion>().ServerTeleport(start + new Vector3(-4f, 0f, -14f), 0f);
            c.GetComponent<NetworkMotion>().ServerTeleport(start + new Vector3(4f, 0f, -12f), 0f);
            yield return new WaitForSecondsRealtime(2f);
            EnemySpawnArea area = FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None)
                .FirstOrDefault(x => x.Definition != null && x.Definition.Id == "enemy.salt_brute" && x.Prefab != null);
            EnemyBrain elite = area != null ? EnemyDirector.Instance.SpawnAt(area.Prefab, start + new Vector3(0f, 0f, 3.5f), 180f, L, "", br => br.SetAggroProfile(AggroProfile.Aggressive), 10f) : null;
            Check(elite != null, $"Elite-Gegner Stufe {L}: {(elite != null ? elite.Definition.DisplayName : "-")} ({(elite != null ? elite.Health.Max : 0):0} Leben, Rolle {(elite != null ? elite.Definition.balanceRole.ToString() : "-")})");
            if (elite == null) yield break;
            _classFoes.Add(elite);
            Health eh = elite.Health;
            int id = elite.GetComponent<FishNet.Object.NetworkObject>().ObjectId;
            FaceTo(a.GetComponent<NetworkMotion>(), a, elite.transform.position);
            FaceTo(b.GetComponent<NetworkMotion>(), b, elite.transform.position);
            FaceTo(c.GetComponent<NetworkMotion>(), c, elite.transform.position);
            elite.ServerEngage(ah);
            yield return Ask(a, "attack", id.ToString());
            yield return Ask(b, "attack-ranged", id.ToString());
            yield return Ask(c, "attack", id.ToString());
            ap.ServerCast("spell.warden_taunt", eh);

            float t = 0f, tankMin = 1f, onTank = 0f, samples = 0f, manaMin = 1f;
            int deaths = 0;
            bool aDead = false, bDead = false, cDead = false;
            while (t < 150f && eh.IsAlive)
            {
                // Tank: Spott, wenn er das Ziel verliert; sonst normale Rotation
                if (elite.Target != ah && ap.ServerCast("spell.warden_taunt", eh) == "") { }
                else if (!ap.ServerIsCasting) WardenRotation(ap, ah, eh);
                if (!bp.ServerIsCasting) StalkerRotation(bp, eh, true);
                // Heilerin: wer am tiefsten steht, bekommt Heilung; sonst Glutbolzen
                if (!cp.ServerIsCasting)
                {
                    Health low = new[] { ah, bh, ch }.Where(h => h.IsAlive).OrderBy(h => h.Fraction).First();
                    if (low.Fraction < 0.5f && cp.ServerCast("spell.weaver_flash_heal", low) == "") { }
                    else if (low.Fraction < 0.8f && cp.ServerCast("spell.weaver_heal", low) == "") { }
                    else if (ah.Fraction < 0.9f && cp.ServerCast("spell.weaver_ash_shield", ah) == "") { }
                    else if (cp.ServerResource > cp.ServerResourceMax * 0.4f) cp.ServerCast("spell.weaver_ember_bolt", eh);
                }
                tankMin = Mathf.Min(tankMin, ah.IsAlive ? ah.Fraction : 0f);
                manaMin = Mathf.Min(manaMin, cp.ServerResource / Mathf.Max(1f, cp.ServerResourceMax));
                samples++;
                if (elite.Target == ah) onTank++;
                if (!ah.IsAlive && !aDead) { aDead = true; deaths++; }
                if (!bh.IsAlive && !bDead) { bDead = true; deaths++; }
                if (!ch.IsAlive && !cDead) { cDead = true; deaths++; }
                if (Flat(a.transform.position, elite.transform.position) > 3f && ah.IsAlive)
                {
                    Vector3 to = elite.transform.position - a.transform.position; to.y = 0f;
                    a.GetComponent<NetworkMotion>().ServerTeleport(elite.transform.position - to.normalized * 2f, Quaternion.LookRotation(to).eulerAngles.y);
                }
                t += 0.25f;
                yield return new WaitForSecondsRealtime(0.25f);
            }
            foreach (NetworkInventory p in new[] { a, b, c }) yield return Ask(p, "stop");
            Line("");
            Line("[Ergebnis]");
            Info($"Dauer {t:0.0} s · Tank tiefstens {tankMin * 100f:0} % · Bedrohung beim Tank {onTank / Mathf.Max(1f, samples) * 100f:0} % der Zeit · Mana der Heilerin tiefstens {manaMin * 100f:0} % · Tode {deaths}");
            Check(!eh.IsAlive && t >= 30f && t <= 100f, $"Elite fällt in der Gruppe nach {t:0} s (Ziel 45–90 s, Toleranz 30–100 s)");
            Check(deaths == 0, $"Niemand stirbt ({deaths} Tode)");
            Check(onTank / Mathf.Max(1f, samples) > 0.8f, $"Der Tank hält die Bedrohung ({onTank / Mathf.Max(1f, samples) * 100f:0} % der Zeit)");
            Check(tankMin < 0.85f, $"Der Kampf fordert den Tank (tiefstens {tankMin * 100f:0} % Leben) – ohne Heilerin wäre es eng");
            Cleanup();
        }

        private void OnBalanceDamage(Health target, DamageInfo info)
        {
            if (target == null) return;
            if (target == _balEnemy)
            {
                if (info.WeaponId != null && info.WeaponId.StartsWith("spell.")) _balSpell += info.Amount;
                else _balWeapon += info.Amount;
            }
            else if (target == _balPlayer) _balTaken += info.Amount;
        }

        private IEnumerator Fight(NetworkInventory a, string config, CharacterClassDefinition cls, bool bow, int level)
        {
            var ap = a.GetComponent<NetworkProgression>();
            var an = a.GetComponent<NetworkMotion>();
            var ah = a.GetComponent<Health>();
            var weapons = a.GetComponent<WeaponUser>();
            string gear = EquipReference(a, ap, cls, level, bow);
            yield return new WaitForSecondsRealtime(1f);
            foreach (SpellDefinition s in GameData.Database.All.OfType<SpellDefinition>().Where(s => s.classId == cls.Id))
            {
                int rank = s.ranks.Count(r => r.level <= level);
                if (rank > 0) ap.ServerDebugLearn(s.Id, rank);
            }
            ap.ServerDebugResetCooldowns();
            ah.Heal(ah.Max);
            ap.ServerSetResource(cls.resource == ResourceType.Rage ? 0f : 10000f);
            Vector3 start = Gnd(470f, 110f);
            an.ServerTeleport(start, 0f);
            yield return new WaitForSecondsRealtime(1.5f);

            // Normaler Gegner gleicher Stufe: Bogen gegen die Schuetzin (haelt Abstand), sonst Nahkampf-Verblichener
            string enemyId = bow ? "enemy.faded_gunner" : "enemy.faded_one";
            EnemySpawnArea area = FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None)
                .FirstOrDefault(x => x.Definition != null && x.Definition.Id == enemyId && x.Prefab != null);
            float dist = bow ? 18f : 3.2f;
            EnemyBrain foe = area != null && EnemyDirector.Instance != null
                ? EnemyDirector.Instance.SpawnAt(area.Prefab, start + new Vector3(0f, 0f, dist), 180f, level, "", br => br.SetAggroProfile(AggroProfile.Aggressive), 5f)
                : null;
            if (foe == null) { Check(false, $"{config} Stufe {level}: Gegner {enemyId} erzeugt"); yield break; }
            _classFoes.Add(foe);
            var fh = foe.GetComponent<Health>();
            yield return new WaitForSecondsRealtime(0.4f);
            FaceTo(an, a, foe.transform.position);
            _balEnemy = fh;
            _balPlayer = ah;
            _balWeapon = _balSpell = _balTaken = 0f;
            var r = new FightResult { Config = config, Level = level, HealthMax = ah.Max, EnemyMax = fh.Max, Gear = gear };
            int id = foe.GetComponent<FishNet.Object.NetworkObject>().ObjectId;
            foe.ServerEngage(ah);
            yield return Ask(a, bow ? "attack-ranged" : "attack", id.ToString());
            float t = 0f, nextSpecial = 0f;
            bool shielded = false;
            while (t < 75f && fh.IsAlive && ah.IsAlive)
            {
                // Rotation (serverseitig, wie ein ordentlicher Spieler)
                if (!ap.ServerIsCasting)
                {
                    if (cls.resource == ResourceType.Rage) WardenRotation(ap, ah, fh);
                    else if (cls.resource == ResourceType.Energy) StalkerRotation(ap, fh, bow);
                    else
                    {
                        if (!shielded && ap.ServerSpellRank("spell.weaver_ash_shield") > 0) shielded = ap.ServerCast("spell.weaver_ash_shield", ah) == "";
                        WeaverRotation(ap, ah, fh);
                    }
                }
                if (weapons != null && t >= nextSpecial && weapons.SpecialCooldownRemaining <= 0f && !ap.ServerIsCasting)
                {
                    yield return Ask(a, "special");
                    nextSpecial = t + 1f;
                }
                // bleibt dem Gegner zugewandt (der Gegner kann sich bewegen)
                if (!bow && Flat(a.transform.position, foe.transform.position) > 2.6f && !ap.ServerIsCasting && cls.resource != ResourceType.Mana)
                {
                    Vector3 to = foe.transform.position - a.transform.position;
                    to.y = 0f;
                    an.ServerTeleport(foe.transform.position - to.normalized * 1.8f, Quaternion.LookRotation(to).eulerAngles.y);
                }
                else if (Random.value < 0.1f) FaceTo(an, a, foe.transform.position);
                t += 0.2f;
                yield return new WaitForSecondsRealtime(0.2f);
            }
            yield return Ask(a, "stop");
            r.Time = t;
            r.Won = !fh.IsAlive;
            r.Died = !ah.IsAlive;
            r.WeaponDamage = _balWeapon;
            r.SpellDamage = _balSpell;
            r.Taken = _balTaken;
            _fights.Add(r);
            _balEnemy = _balPlayer = null;
            float refDps = Balance.RefDps(level);
            Line($"  Stufe {level,2}: {(r.Won ? "Sieg" : r.Died ? "TOT" : "kein Sieg")} nach {r.Time,5:0.0} s · {r.Dps,5:0.0} Schaden/s ({r.Dps / refDps * 100f:0} % Ref; " +
                 $"Waffe {(r.Time > 0 ? r.WeaponDamage / r.Time : 0):0.0}, Zauber {(r.Time > 0 ? r.SpellDamage / r.Time : 0):0.0}) · Leben −{r.Taken / Mathf.Max(1f, r.HealthMax) * 100f:0} % " +
                 $"({r.Taken:0}/{r.HealthMax:0}, Gegner {r.EnemyMax:0}) · {gear}");
            if (foe != null) EnemyDirector.Instance.Remove(foe);
            _classFoes.Remove(foe);
            if (!ah.IsAlive)
            {
                // Tod: sofort wieder aufstehen lassen (Testfigur)
                { var np = a.GetComponent<NetworkPlayer>(); if (np != null) np.ServerReviveHere(1f); }
                yield return new WaitForSecondsRealtime(1f);
            }
            ah.Heal(ah.Max);
            yield return new WaitForSecondsRealtime(0.6f);
        }

        private static void WardenRotation(NetworkProgression ap, Health ah, Health fh)
        {
            float rage = ap.ServerResource;
            if (ah.Fraction < 0.5f && ap.ServerCast("spell.warden_fortify", null) == "") return;
            if (rage >= 10f && ap.ServerCast("spell.warden_shield_bash", fh) == "") return;
            if (rage >= 20f && ap.ServerCast("spell.warden_thunder_clap", null) == "") return;
            bool others = ap.ServerSpellRank("spell.warden_thunder_clap") > 0;
            if (ap.ServerQueuedNextSwing == "" && rage >= (others ? 40f : 15f)) ap.ServerCast("spell.warden_heroic_strike", null);
        }

        private static void StalkerRotation(NetworkProgression ap, Health fh, bool bow)
        {
            float e = ap.ServerResource;
            if (bow)
            {
                if (e >= 30f && ap.ServerCast("spell.stalker_fire_arrow", fh) == "") return;
                if (e >= 40f) ap.ServerCast("spell.stalker_multi_shot", fh);
                return;
            }
            if (e >= 40f && ap.ServerCast("spell.stalker_feint", fh) == "") return;
            if (e >= 70f) ap.ServerCast("spell.stalker_wing_clip", fh);
        }

        private static void WeaverRotation(NetworkProgression ap, Health ah, Health fh)
        {
            if (ah.Fraction < 0.45f)
            {
                if (ap.ServerCast("spell.weaver_flash_heal", ah) == "") return;
                if (ap.ServerCast("spell.weaver_heal", ah) == "") return;
            }
            if (Flat(ah.transform.position, fh.transform.position) < 7.5f && ap.ServerCast("spell.weaver_flame_wave", null) == "") return;
            ap.ServerCast("spell.weaver_ember_bolt", fh);
        }

        /// <summary>
        /// Beste verfuegbare Ausruestung bis zu dieser Stufe (Startausruestung + alles Passende aus der Datenbank):
        /// je Platz das hoechste Item, Waffen nach Schaden je Sekunde. Eidwaechter mit Einhand + Schild.
        /// </summary>
        private static string EquipReference(NetworkInventory a, NetworkProgression ap, CharacterClassDefinition cls, int level, bool bow)
        {
            CharacterSaveData kit = CharacterStore.CreateNew("balance-kit", cls);
            ap.ServerDebugSwitchClass(cls);
            a.ServerSetLevel(level);
            InventoryModel m = a.ServerModel;
            m.Load(kit.bag, kit.equipment, kit.containers, kit.ammoId);
            var usable = GameData.Database.All.OfType<ItemDefinition>()
                .Where(d => d.IsEquippable && d.equipType != EquipType.Bag && !d.IsContainer && d.requiredLevel <= level && InventoryModel.UsableByClass(d, cls))
                .ToList();
            // Ruestung/Schmuck: je Platz das beste
            foreach (var group in usable.Where(d => !d.IsWeapon && !d.IsShield && d.equipType != EquipType.Weapon).GroupBy(d => d.equipType))
            {
                ItemDefinition best = group.OrderByDescending(d => d.itemLevel * 10 + d.armor * 0.1f + d.stats.Sum(s => s.value)).First();
                Put(m, best, null);
            }
            // Waffen
            var melee1h = usable.Where(d => d.IsWeapon && d.weapon.IsMelee && !d.IsTwoHanded).OrderByDescending(d => WeaponDpsProxy(d.weapon)).ToList();
            var bows = usable.Where(d => d.IsRangedWeapon).OrderByDescending(d => WeaponDpsProxy(d.weapon)).ToList();
            var staves = usable.Where(d => d.IsWeapon && d.weapon.IsSpell).OrderByDescending(d => WeaponDpsProxy(d.weapon)).ToList();
            var shields = usable.Where(d => d.IsShield).OrderByDescending(d => d.armor).ToList();
            if (cls.resource == ResourceType.Mana)
            {
                if (staves.Count > 0) Put(m, staves[0], EquipmentSlot.MainHand);
            }
            else if (melee1h.Count > 0)
            {
                Put(m, melee1h[0], EquipmentSlot.MainHand);
                if (cls.dualWield) Put(m, melee1h.Count > 1 ? melee1h[1] : melee1h[0], EquipmentSlot.OffHand);
                else if (shields.Count > 0) Put(m, shields[0], EquipmentSlot.OffHand);
            }
            if (cls.dualWield && bows.Count > 0) Put(m, bows[0], EquipmentSlot.Ranged);
            a.ServerSetLevel(level);
            a.ServerCommit();
            string W(EquipmentSlot s)
            {
                ItemStack st = m.Equipment[(int)s];
                return st.IsEmpty ? "–" : GameData.Item(st.id)?.DisplayName ?? st.id;
            }
            return bow ? $"{W(EquipmentSlot.Ranged)}" : cls.dualWield ? $"{W(EquipmentSlot.MainHand)} + {W(EquipmentSlot.OffHand)}" :
                $"{W(EquipmentSlot.MainHand)}{(m.Equipment[(int)EquipmentSlot.OffHand].IsEmpty ? "" : " + " + W(EquipmentSlot.OffHand))}";
        }

        private static void Put(InventoryModel m, ItemDefinition d, EquipmentSlot? slot)
        {
            int before = m.FirstFreeBagSlot();
            if (before < 0 || m.Add(new ItemStack(d.Id, 1)) <= 0) return;
            InvResult r = slot.HasValue ? m.Equip(before, slot.Value) : m.AutoEquip(before);
            if (!r.Ok) Debug.Log($"[Ashveil] Balance-Test: {d.Id} nicht angelegt ({r.Error})");
        }

        /// <summary>Waffenschaden je Sekunde im Auto-Angriff (Mittelwert × Schrittfaktoren / Zeit).</summary>
        private static float WeaponDpsProxy(WeaponDefinition w)
        {
            if (w == null) return 0f;
            float avg = (w.minDamage + w.maxDamage) * 0.5f;
            if (w.archetype == WeaponArchetype.Projectile) return avg / Mathf.Max(0.5f, w.attackSpeed);
            if (w.combo == null || w.combo.Length == 0) return avg;
            float time = 0f, mult = 0f;
            foreach (AttackStep s in w.combo) { time += s.windup + s.active + s.recovery; mult += s.damageMultiplier; }
            return avg * mult / Mathf.Max(0.2f, time);
        }
    }
}
