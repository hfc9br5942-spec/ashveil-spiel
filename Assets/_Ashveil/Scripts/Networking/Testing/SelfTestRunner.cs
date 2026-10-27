using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using Ashveil.Data;
using Ashveil.Gameplay.Targeting;
using Ashveil.Gameplay.Weapons;
using FishNet;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Automatischer Selbsttest im laufenden Spiel – spielt wie ein Spieler (Tasten werden eingespeist)
    /// und prueft die Ergebnisse. Schreibt einen Bericht in eine Textdatei:
    ///   Editor:      &lt;Projekt&gt;/Logs/Ashveil_SelfTest_Editor.txt
    ///   Test-Client: &lt;Projekt&gt;/Builds/TestClient/Ashveil_SelfTest_Client.txt
    ///
    /// Start: F10 im Spiel (seit 22.10.2026 nur im Editor oder bei angefordertem Test) · Kommandozeile "-ashveil-selftest" ·
    /// Menue "Ashveil &gt; Test &gt; Selbsttest".
    /// Neue Pruefungen fuer spaetere Schritte kommen als weitere Abschnitte in <see cref="Suite"/>.
    /// </summary>
    public sealed class SelfTestRunner : MonoBehaviour
    {
        /// <summary>Markierungsdatei, mit der das Editor-Menue einen Test fuer den naechsten Play-Start anfordert.</summary>
        public const string EditorFlagFile = "Temp/ashveil_selftest.flag";

        private readonly StringBuilder _report = new StringBuilder();
        private int _passed, _failed, _logErrors, _rejected;
        private bool _running;
        private bool _ownsTestState;
        private bool _previousDodgeEnabled;
        private float _previousEnemyDodgeChance;

        private static string Role => Application.isEditor ? "Editor" : "Client";

        private static string ReportPath => Application.isEditor
            ? Ashveil.Core.ProjectPaths.Logs("Ashveil_SelfTest_Editor.txt")
            : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Ashveil_SelfTest_Client.txt"));

        private bool _requested;

        private void Awake()
        {
            if (!TestBuild.Enabled) { enabled = false; return; }
            _requested = HasArg("-ashveil-selftest");
            if (Application.isEditor && File.Exists(EditorFlagFile))
            {
                _requested = true;
                try { File.Delete(EditorFlagFile); } catch (IOException) { }
            }
            // Eigenes Test-Profil (startet pro Serverstart frisch) – dein normaler Charakter bleibt unberuehrt
            if (_requested && string.IsNullOrEmpty(ProfileSelection.ArgValue("-ashveil-profile")))
                ProfileSelection.Override = Application.isEditor ? "selftest-editor" : "selftest-client";
        }

        private void Start()
        {
            if (_requested) StartCoroutine(Run());
        }

        /// <summary>
        /// 22.10.2026 (REL-02/RELEASE-01): F10 startet den Selbsttest nur noch im Editor oder wenn der Test angefordert war
        /// (-ashveil-selftest bzw. Menü-Flag) – im Spieltest-Build (Development) übernahm F10 sonst ohne Rückfrage Max' echte Figur
        /// (Teleports, Kämpfe, Beute, Direktschaden, gespeichert). Startargument und Editor-Menü bleiben unverändert.
        /// </summary>
        public bool F10Aktiv => isActiveAndEnabled && ((Application.isEditor && !WieImBuildFuerTests) || _requested);

        /// <summary>Tests (Suite spielablauf3): F10 im Editor so behandeln wie im gebauten Spiel.</summary>
        public static bool WieImBuildFuerTests;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => WieImBuildFuerTests = false;

        private void Update()
        {
            if (!_running && RawInput.WasPressed(GameKey.SelfTest) && F10Aktiv) StartCoroutine(Run());
        }

        // ================================================================== Ablauf

        private IEnumerator Run()
        {
            if (_running) yield break;
            _previousDodgeEnabled = CharacterEquipment.DodgeEnabled;
            _previousEnemyDodgeChance = EnemyBrain.DodgeChance;
            _ownsTestState = _running = true;
            CharacterEquipment.DodgeEnabled = false;
            EnemyBrain.DodgeChance = 0f;
            Application.logMessageReceived += OnLog;
            try
            {
                _report.Clear();
                _passed = _failed = _logErrors = _rejected = 0;
                Line($"ASHVEIL Selbsttest · {Role} · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                yield return Suite();

                RestoreTestState();
                Info($"Kampfwerte wiederhergestellt: Ausweichen={CharacterEquipment.DodgeEnabled}, Gegnerchance={EnemyBrain.DodgeChance:0.###}");
                Line("");
                Line($"ERGEBNIS: {_passed} bestanden, {_failed} fehlgeschlagen, {_logErrors} Fehler/Ausnahmen im Log, {_rejected} vom Server verworfene Angriffe");
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                    File.WriteAllText(ReportPath, _report.ToString());
                }
                catch (Exception e) { Debug.LogWarning("[Ashveil][Test] Bericht nicht schreibbar: " + e.Message); }
                Debug.Log($"[Ashveil][Test] Fertig: {_passed} ok, {_failed} fehlgeschlagen → {ReportPath}");
                _running = false;
                // Test-Client, der nur zum Testen gestartet wurde: danach beenden
                if (!Application.isEditor && HasArg("-ashveil-selftest")) Application.Quit();
            }
            finally { RestoreTestState(); }
        }

        private void RestoreTestState()
        {
            if (!_ownsTestState) return;
            _ownsTestState = false;
            Application.logMessageReceived -= OnLog;
            CharacterEquipment.DodgeEnabled = _previousDodgeEnabled;
            EnemyBrain.DodgeChance = _previousEnemyDodgeChance;
            _running = false;
        }

        private void OnDisable()
        {
            // Disabling a MonoBehaviour does not itself stop its coroutines.
            if (Application.isPlaying) StopAllCoroutines();
            RestoreTestState();
        }

        private void OnDestroy() => RestoreTestState();

        private IEnumerator Suite()
        {
            // --- Netz & Spieler
            float t = 0f;
            while (t < 20f && !(InstanceFinder.ClientManager != null && InstanceFinder.ClientManager.Started))
            {
                t += 0.25f;
                yield return new WaitForSecondsRealtime(0.25f);
            }
            Check(InstanceFinder.ClientManager != null && InstanceFinder.ClientManager.Started, "Mit dem Server verbunden");
            Info($"Rolle: {(InstanceFinder.IsServerStarted ? ((InstanceFinder.ClientManager != null && InstanceFinder.ClientManager.Started) ? "Host" : "Server") : "Client")}");

            PlayerCharacter player = null;
            t = 0f;
            while (player == null && t < 20f)
            {
                player = FindLocalPlayer();
                if (player == null) { t += 0.25f; yield return new WaitForSecondsRealtime(0.25f); }
            }
            Check(player != null, "Eigene Spielfigur erschienen");
            if (player == null) yield break;
            yield return new WaitForSecondsRealtime(1f);

            // --- 3.4 Inventar (Grundlage fuer die Kampftests: Waffen kommen aus der Ausruestung)
            yield return InventorySection(player);

            // --- 3.2 Kampf serverautoritaer
            yield return CombatSection(player);

            // --- 3.3 Gegner
            yield return EnemySection(player);
        }

        // ================================================================== 3.4 Inventar

        private IEnumerator InventorySection(PlayerCharacter player)
        {
            Line("");
            Line("[3.4 Inventar & Ausruestung]");
            var inv = player.GetComponent<NetworkInventory>();
            float t = 0f;
            while (inv != null && !inv.Ready && t < 10f) { t += 0.25f; yield return new WaitForSecondsRealtime(0.25f); }
            Check(inv != null && inv.Ready, "Inventar vom Server erhalten");
            if (inv == null || !inv.Ready) yield break;
            Info($"Klasse {(inv.Class != null ? inv.Class.DisplayName : "-")}, Stufe {inv.Level}, {CountItems(inv.Bag)} Gegenstaende im Rucksack");
            ItemStack main = inv.Equipment[(int)EquipmentSlot.MainHand], off = inv.Equipment[(int)EquipmentSlot.OffHand];
            Check(!main.IsEmpty && !off.IsEmpty, $"Startwaffen angelegt ({main} / {off})");
            var weapons = player.GetComponent<WeaponUser>();
            ItemDefinition md = GameData.Item(main.id), od = GameData.Item(off.id);
            Check(weapons != null && md != null && od != null && weapons.MeleeWeapon == md.weapon && weapons.RangedWeapon == od.weapon,
                "Waffen im Kampf = angelegte Waffen");
            var health = player.GetComponent<Health>();
            var eq = player.GetComponent<Ashveil.Gameplay.Items.CharacterEquipment>();
            Check(health != null && eq != null && Mathf.Approximately(health.Max, Mathf.Round(eq.Stats.MaxHealth)),
                $"Leben aus Ausdauer der Ausruestung ({health.Max:0})");
        }

        private static int CountItems(ItemStack[] bag)
        {
            int n = 0;
            foreach (ItemStack s in bag) if (!s.IsEmpty) n += s.count;
            return n;
        }

        // ================================================================== 3.2 Kampf

        private IEnumerator CombatSection(PlayerCharacter player)
        {
            Line("");
            Line("[3.2 Kampf serverautoritaer]");
            var input = player.GetComponent<PlayerInputReader>();
            var weapons = player.GetComponent<WeaponUser>();
            var targeting = player.GetComponent<TargetingSystem>();
            var motor = player.GetComponent<CharacterMotor>();

            TrainingDummy dummy = Nearest<TrainingDummy>(player.transform.position);
            Check(dummy != null, "Trainingspuppe gefunden");
            if (dummy == null || input == null || weapons == null || targeting == null) yield break;
            var health = dummy.GetComponent<Health>();
            var target = dummy.GetComponent<CombatTarget>();
            Info($"Puppe: {target.DisplayName} · Leben {health.Current:0}/{health.Max:0} · Spiegelbild={health.IsReplica}");
            Check(health.IsReplica == !InstanceFinder.IsServerStarted, "Leben der Puppe kommt vom Server (Spiegelbild nur auf reinen Clients)");

            // Vor die Puppe stellen und warten, bis der Server die neue Position kennt
            Vector3 dir = player.transform.position - dummy.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = dummy.transform.forward;
            dir.Normalize();
            Vector3 pos = dummy.transform.position + dir * 1.5f;
            float yaw = Quaternion.LookRotation(-dir).eulerAngles.y;
            Teleport(motor, pos, yaw);
            targeting.SetTarget(target);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return WaitForFullHealth(health);

            if (!weapons.UsingMelee)
            {
                input.InjectCombat(new CombatInput { SwapWeapon = true });
                yield return new WaitForSecondsRealtime(1.2f);
            }

            // Spezialangriff Schwert
            float before = health.Current;
            input.InjectCombat(new CombatInput { Ability2 = true });
            yield return new WaitForSecondsRealtime(1.8f);
            Check(health.Current < before, $"Schwert-Spezial trifft (Leben {before:0} → {health.Current:0})");

            // Auto-Angriff an, zwei Sekunden, wieder aus
            before = health.Current;
            input.InjectCombat(new CombatInput { Ability1 = true });
            yield return new WaitForSecondsRealtime(2.2f);
            input.InjectCombat(new CombatInput { Ability1 = true });
            yield return new WaitForSecondsRealtime(0.8f);
            Check(health.Current < before, $"Auto-Angriff Schwert trifft (Leben {before:0} → {health.Current:0})");

            // Waffe wechseln, gezielter Schuss – nur mit Fernkampfwaffe (Feuerwaffen ruhen; Boegen testet die Kampf-Pruefung)
            if (weapons.RangedWeapon == null)
            {
                Line("  (Fernkampf übersprungen: keine Fernkampfwaffe angelegt)");
                yield break;
            }
            input.InjectCombat(new CombatInput { SwapWeapon = true });
            yield return new WaitForSecondsRealtime(1.2f);
            Check(!weapons.UsingMelee, "Waffenwechsel zur Fernkampfwaffe");
            before = health.Current;
            input.InjectCombat(new CombatInput { Ability2 = true });
            yield return new WaitForSecondsRealtime(2.2f);
            Check(health.Current < before, $"Gezielter Schuss trifft (Leben {before:0} → {health.Current:0})");

            // Zurueck zum Schwert, Puppe erholt sich (Server setzt zurueck, Clients sehen es)
            input.InjectCombat(new CombatInput { SwapWeapon = true });
            float waited = 0f;
            while (health.Current < health.Max && waited < 9f)
            {
                waited += 0.5f;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Check(Mathf.Approximately(health.Current, health.Max), $"Puppe fuellt sich wieder auf ({waited:0.0} s)");
            Check(weapons.UsingMelee, "Zurueck zum Schwert");
        }

        // ================================================================== 3.3 Gegner

        private IEnumerator EnemySection(PlayerCharacter player)
        {
            Line("");
            Line("[3.3 Gegner]");
            var input = player.GetComponent<PlayerInputReader>();
            var targeting = player.GetComponent<TargetingSystem>();
            var motor = player.GetComponent<CharacterMotor>();
            var weapons = player.GetComponent<WeaponUser>();
            var myHealth = player.GetComponent<Health>();

            // Gegner erschienen?
            float t = 0f;
            while (FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).Length < 7 && t < 10f)
            {
                t += 0.5f;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            EnemyBrain[] all = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            int wolves = 0, husks = 0;
            foreach (EnemyBrain b in all)
            {
                string id = b.Definition != null ? b.Definition.Id : "";
                if (id == "enemy.ash_wolf") wolves++;
                else if (id == "enemy.faded_one" || id == "enemy.faded_gunner") husks++;
            }
            Check(all.Length >= 7, $"Gegner erschienen ({wolves} Aschwoelfe, {husks} Verblichene)");
            if (all.Length == 0 || myHealth == null) yield break;
            if (weapons != null && !weapons.UsingMelee)
            {
                input.InjectCombat(new CombatInput { SwapWeapon = true });
                yield return new WaitForSecondsRealtime(1.2f);
            }

            // --- Aschwolf am Lager (Kanon 26.09.: Defensive, keine Social-Aggro)
            EnemyBrain wolf = NearestEnemy(new Vector3(-42f, 0f, 62f), EnemyBody.Beast);
            Check(wolf != null, "Aschwolf am Kiefernhang gefunden");
            if (wolf == null) yield break;
            var wolfHealth = wolf.GetComponent<Health>();
            Vector3 toCamp = (Vector3.zero - wolf.transform.position);
            toCamp.y = 0f;
            toCamp.Normalize();
            // 1) Vorbeigehen: direkt neben dem Wolf stehen – er greift nicht von sich aus an
            Teleport(motor, wolf.transform.position + toCamp * 3f, Quaternion.LookRotation(-toCamp).eulerAngles.y);
            float hp0 = myHealth.Current;
            yield return new WaitForSecondsRealtime(3.5f);
            bool calm = myHealth.Current >= hp0 && wolf.State != EnemyState.Chase && wolf.State != EnemyState.Attack;
            Check(calm, $"Aschwolf am Lager laesst Spieler in Ruhe, die nur vorbeigehen (Leben {myHealth.Current:0}/{hp0:0})");

            // Die anderen Woelfe des Rudels merken
            var pack = new List<EnemyBrain>();
            foreach (EnemyBrain b in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                if (b != wolf && b.Definition != null && b.Definition.body == EnemyBody.Beast && b.GetComponent<Health>().IsAlive &&
                    Vector3.Distance(b.transform.position, wolf.transform.position) < 14f) pack.Add(b);

            // Wer von einem Rundumschlag mitgetroffen wird, darf sich wehren – das ist keine Social-Aggro
            var hitByUs = new HashSet<EnemyBrain>();
            var packHooks = new List<(Health h, System.Action<DamageInfo> cb)>();
            foreach (EnemyBrain b in pack)
            {
                EnemyBrain captured = b;
                System.Action<DamageInfo> cb = packHit => hitByUs.Add(captured);
                Health packHealth = b.GetComponent<Health>();
                packHealth.Damaged += cb;
                packHooks.Add((packHealth, cb));
            }

            // 2) Angreifen: genau dieser Wolf wehrt sich
            toCamp = (player.transform.position - wolf.transform.position);
            toCamp.y = 0f;
            toCamp.Normalize();
            Teleport(motor, wolf.transform.position + toCamp * 2f, Quaternion.LookRotation(-toCamp).eulerAngles.y);
            if (wolf.TryGetComponent(out CombatTarget firstTarget)) targeting.SetTarget(firstTarget);
            input.InjectCombat(new CombatInput { Ability1 = true });
            t = 0f;
            float hpFight = myHealth.Current;
            while (t < 10f && myHealth.Current >= hpFight)
            {
                t += 0.25f;
                yield return new WaitForSecondsRealtime(0.25f);
            }
            Check(myHealth.Current < hpFight, $"Angegriffen wehrt sich der Wolf (beisst nach {t:0.0} s, Leben {hpFight:0} → {myHealth.Current:0})");
            int joined = 0;
            foreach (EnemyBrain b in pack)
                if (b != null && !hitByUs.Contains(b) && (b.State == EnemyState.Chase || b.State == EnemyState.Attack)) joined++;
            foreach ((Health h, System.Action<DamageInfo> cb) hook in packHooks) if (hook.h != null) hook.h.Damaged -= hook.cb;
            Check(joined == 0, $"Die anderen {pack.Count} Woelfe des Rudels bleiben unbeteiligt (keine Social-Aggro" +
                (hitByUs.Count > 0 ? $"; {hitByUs.Count} vom Schlag mitgetroffen, die duerfen sich wehren)" : ")"));
            input.InjectCombat(new CombatInput { Ability1 = true }); // Auto-Angriff kurz aus, unten wieder an

            // Zurueckschlagen
            if (wolf != null && wolf.TryGetComponent(out CombatTarget wolfTarget)) targeting.SetTarget(wolfTarget);
            input.InjectCombat(new CombatInput { Ability1 = true });
            t = 0f;
            while (t < 20f && wolfHealth != null && wolfHealth.IsAlive && myHealth.IsAlive)
            {
                t += 0.25f;
                yield return new WaitForSecondsRealtime(0.25f);
            }
            Check(wolfHealth != null && !wolfHealth.IsAlive, $"Aschwolf besiegt ({t:0.0} s, Spieler-Leben {myHealth.Current:0})");
            input.InjectCombat(new CombatInput { Ability1 = true }); // Auto-Angriff aus

            // --- Persoenliche Beute des Wolfs
            var inv = player.GetComponent<NetworkInventory>();
            var glimmer = wolf != null ? wolf.GetComponent<LootIndicator>() : null;
            t = 0f;
            while (glimmer != null && !glimmer.Available && t < 4f) { t += 0.25f; yield return new WaitForSecondsRealtime(0.25f); }
            Check(glimmer != null && glimmer.Available, "Leiche glimmt – Beute fuer mich");
            if (inv != null && glimmer != null && glimmer.Available)
            {
                Teleport(motor, wolf.transform.position + Vector3.right * 1.5f, 0f);
                yield return new WaitForSecondsRealtime(1f);
                int lr = inv.LootRevision;
                inv.TryOpenLoot(wolf.gameObject);
                t = 0f;
                while (inv.LootRevision == lr && t < 5f) { t += 0.1f; yield return new WaitForSecondsRealtime(0.1f); }
                int lootCount = inv.Loot.Count;
                Check(inv.LootOpen && lootCount > 0, $"Beutefenster zeigt {lootCount} Stapel");
                int before = CountItems(inv.Bag);
                int lootItems = 0;
                foreach (ItemStack st in inv.Loot) lootItems += st.count;
                int rev = inv.ClientRevision;
                inv.TakeAllLoot();
                t = 0f;
                while (inv.ClientRevision == rev && t < 5f) { t += 0.1f; yield return new WaitForSecondsRealtime(0.1f); }
                yield return new WaitForSecondsRealtime(0.3f);
                Check(CountItems(inv.Bag) == before + lootItems, $"Alles aufgenommen ({lootItems} Stueck im Rucksack)");
                Check(!glimmer.Available, "Leiche leer – Glimmen aus");
            }

            // --- Leine: ins Lager fliehen, Woelfe lassen ab
            GameObject spawn = GameObject.Find("PlayerSpawn");
            Vector3 camp = spawn != null ? spawn.transform.position : Vector3.zero;
            Teleport(motor, camp, 0f);
            targeting.SetTarget(null);
            yield return new WaitForSecondsRealtime(6f);
            float nearest = float.MaxValue;
            foreach (EnemyBrain b in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                var h = b.GetComponent<Health>();
                if (h != null && h.IsAlive) nearest = Mathf.Min(nearest, Vector3.Distance(b.transform.position, player.transform.position));
            }
            Check(nearest > 15f, $"Gegner verfolgen nicht bis ins Lager (naechster {nearest:0} m entfernt)");

            // --- Verblichene: Fernkampf + Nahkampf, bis der Spieler faellt; dann Wiederbelebung am Wachtfeuer
            // Die Schuetzin im Lager der Verblichenen (Gehoeft) – nicht die im Holzfaellerlager, keine Nadelspeier
            EnemyBrain gunner = null;
            float gunnerDist = float.MaxValue;
            var ashCamp = new Vector3(88f, 0f, -2f); // Gehoeft seit 27.09. (KarethQuestSites.AshCamp)
            foreach (EnemyBrain b in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (b.Definition == null || b.Definition.Id != "enemy.faded_gunner") continue;
                float gd = Vector3.Distance(b.transform.position, ashCamp);
                if (gd < gunnerDist) { gunnerDist = gd; gunner = b; }
            }
            Check(gunner != null, "Verblichene Schuetzin gefunden");
            if (gunner == null) yield break;
            Vector3 dir = (camp - gunner.transform.position);
            dir.y = 0f;
            dir.Normalize();
            // Seit P1 (30.09.) ist die gespeicherte Testfigur ein Eidwaechter Stufe 5 mit Schild (200 Leben, Block) –
            // der haelt 75 s gegen das Gehoeft durch. Geprueft wird Schaden, Tod und Wiederbelebung, nicht die Ausdauer:
            // deshalb vorher auf hoechstens 60 Leben bringen.
            PlayerDiag(player, myHealth);
            EnemySnapshot("vor Vorbereitung", player, myHealth, gunner);
            for (int guard = 0; guard < 40 && myHealth.IsAlive && myHealth.Current > 60f; guard++)
                myHealth.ApplyDamage(new DamageInfo { Amount = Mathf.Min(40f, myHealth.Current - 55f), Unblockable = true });
            // 21.10.2026: Die Verblichenen im Gehöft sind territorial (Kanon 26.09.: Revier Schützin 10 m, Nahkämpfer 6 m;
            // EnemyBuilder.AggroDefaults, Gebiet ohne Überschreibung). Mit festen 12 m stand der Spieler außerhalb jedes Reviers –
            // ob jemand angriff, hing davon ab, ob ein Streifender zufällig näher kam (sporadisch 22/26 bzw. 23/26, Wahrnehmung
            // selbst korrekt). Jetzt 2 m innerhalb der Wahrnehmung der Schützin, höchstens 12 m wie bisher.
            float abstand = Mathf.Clamp(gunner.PerceptionRadius - 2f, 6f, 12f);
            Info($"Gehoeft: Schuetzin {gunner.Aggro}, Wahrnehmung {gunner.PerceptionRadius:0.#} m → Spieler auf {abstand:0.#} m");
            Teleport(motor, gunner.transform.position + dir * abstand, Quaternion.LookRotation(-dir).eulerAngles.y);
            EnemySnapshot("nach Vorbereitung/Teleportauftrag", player, myHealth, gunner);
            float hpBefore = myHealth.Current;
            t = 0f;
            while (t < 75f && myHealth.IsAlive)
            {
                t += 0.25f;
                yield return new WaitForSecondsRealtime(0.25f);
                if (t == 1f || t == 10f || t == 30f)
                    EnemySnapshot($"Kampf {t:0} s", player, myHealth, gunner);
            }
            Check(myHealth.Current < hpBefore, "Verblichene verursachen Schaden (Schuesse und Hiebe)");
            bool died = !myHealth.IsAlive;
            EnemySnapshot("Ende Kampfwartezeit", player, myHealth, gunner);
            Check(died, $"Spieler faellt im Lager der Verblichenen ({t:0.0} s)");
            t = 0f;
            while (t < 12f && !myHealth.IsAlive)
            {
                t += 0.25f;
                yield return new WaitForSecondsRealtime(0.25f);
            }
            yield return new WaitForSecondsRealtime(0.8f);
            float distSpawn = Vector3.Distance(player.transform.position, camp);
            Check(died && myHealth.IsAlive && Mathf.Approximately(myHealth.Current, myHealth.Max),
                $"Wiederbelebung nach {t:0.0} s mit vollem Leben ({myHealth.Current:0}/{myHealth.Max:0})");
            Check(died && distSpawn < 8f, $"Erwacht nach beobachtetem Tod am Wachtfeuer ({distSpawn:0.0} m vom Spawn)");
        }

        // Fixed, bounded snapshots: observation only; no AI/NavMesh or combat state is changed.
        private void EnemySnapshot(string phase, PlayerCharacter player, Health health, EnemyBrain gunner)
        {
            Info($"Gehoeft [{phase}]: HP {health.Current:0.0}/{health.Max:0.0}, Replica={health.IsReplica}, TempInvul={health.IsTemporarilyInvulnerable}, LastDamage={health.LastDamageTime:0.00}, Server={InstanceFinder.IsServerStarted}, Spieler={player.transform.position:F2}");
            foreach (EnemyBrain brain in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (brain.Definition == null || Vector3.Distance(brain.transform.position, gunner.transform.position) > 30f) continue;
                var agent = brain.GetComponent<UnityEngine.AI.NavMeshAgent>();
                bool onMesh = agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh;
                string nav = onMesh ? $"{agent.pathStatus}, Path={agent.hasPath}, Stopped={agent.isStopped}" : "kein aktives NavMesh";
                Info($"Gehoeft Gegner {brain.Definition.Id}: State={brain.State}, Target={(brain.CurrentTarget != null ? brain.CurrentTarget.name : "null")}, Dist={Vector3.Distance(brain.transform.position, player.transform.position):0.0}, Pos={brain.transform.position:F2}, Nav={nav}");
                PerceptionDiag(brain, player);
            }
        }

        // Diagnose 21.10.2026 (sporadisch keine Wahrnehmung im Gehöft): Nachbildung von EnemyBrain.FindTarget/HasLineOfSight –
        // nur lesen, kein Zustandswechsel. Je Gegner: Aggro-Profil, Radius, Wachzustand, echtes PerceiveTarget und die Treffer
        // der Überlappungsprüfung (Collider, Layer, Health, gültig, Sichtlinie mit blockierendem Collider).
        private static readonly Collider[] DiagBuffer = new Collider[32];

        private void PlayerDiag(PlayerCharacter player, Health health)
        {
            var sb = new StringBuilder();
            int n = 0;
            foreach (Collider c in player.GetComponentsInChildren<Collider>(true))
            {
                if (n++ < 10)
                    sb.Append($"{c.name}[{LayerMask.LayerToName(c.gameObject.layer)},{(c.enabled && c.gameObject.activeInHierarchy ? "an" : "aus")}{(c.isTrigger ? ",Trigger" : "")},{c.GetType().Name}] ");
            }
            Info($"Gehoeft Spieler-Collider ({n}): {sb}");
            Info($"Gehoeft Masken: PlayerMask(EnemyBrain)={EnemyBrain.PlayerLayerMask}, GetMask(Player)={LayerMask.GetMask(Ashveil.Core.GameLayers.Player)}, Sicht={EnemyBrain.SightBlockMask}; " +
                 $"Spieler-Layer={LayerMask.LayerToName(player.gameObject.layer)}, Health aktiv={health.isActiveAndEnabled}, lebt={health.IsAlive}");
        }

        private void PerceptionDiag(EnemyBrain brain, PlayerCharacter player)
        {
            EnemyDefinition def = brain.Definition;
            Vector3 d = player.transform.position - brain.transform.position;
            d.y = 0f;
            Health probe = brain.ProbePerception();
            string line = $"    Wahrnehmung: Profil={brain.Aggro}, Fraktion={brain.Faction}, Aktiv={brain.IsActive}, Wach={brain.IsAwake}, Tarnung={brain.Concealment}, " +
                          $"Radius={brain.PerceptionRadius:0.#} (Aggro {def.aggroRadius:0.#}, Revier {def.territoryRadius:0.#}), horiz.={d.magnitude:0.0}, " +
                          $"PerceiveTarget={(probe != null ? probe.name : "null")}; {OverlapDiag(brain, brain.PerceptionRadius)}";
            if (brain.PerceptionRadius < def.aggroRadius) line += $" | bei Aggro-Radius {OverlapDiag(brain, def.aggroRadius)}";
            Info(line);
        }

        private static string OverlapDiag(EnemyBrain brain, float radius)
        {
            if (radius <= 0f) return "r=0";
            int n = Physics.OverlapSphereNonAlloc(brain.transform.position, radius, DiagBuffer, EnemyBrain.PlayerLayerMask, QueryTriggerInteraction.Ignore);
            var sb = new StringBuilder($"r={radius:0.#}: {n} Treffer{(n >= 16 ? " (Puffer des Spiels 16 voll!)" : "")}");
            for (int k = 0; k < Mathf.Min(n, 3); k++)
            {
                Collider c = DiagBuffer[k];
                Health h = c.GetComponentInParent<Health>();
                bool valid = h != null && h.isActiveAndEnabled && h.IsAlive && h != brain.Health;
                string los = "-";
                if (h != null)
                {
                    Vector3 eye = brain.transform.position + Vector3.up * brain.EyeHeight;
                    Vector3 aim = h.transform.position + Vector3.up * 1.2f;
                    los = Physics.Linecast(eye, aim, out RaycastHit hit, EnemyBrain.SightBlockMask, QueryTriggerInteraction.Ignore)
                        ? $"blockiert von {hit.collider.name}[{LayerMask.LayerToName(hit.collider.gameObject.layer)}] nach {hit.distance:0.0} m"
                        : "frei";
                }
                sb.Append($"; {c.name}[{LayerMask.LayerToName(c.gameObject.layer)},{(c.enabled ? "an" : "aus")}] Health={(h != null ? h.name : "keins")} gueltig={valid} Sicht={los}");
            }
            return sb.ToString();
        }

        private static EnemyBrain NearestEnemy(Vector3 from, EnemyBody body)
        {
            EnemyBrain best = null;
            float bestDist = float.MaxValue;
            foreach (EnemyBrain b in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (b.Definition == null || b.Definition.body != body) continue;
                var h = b.GetComponent<Health>();
                if (h == null || !h.IsAlive) continue;
                float dd = (b.transform.position - from).sqrMagnitude;
                if (dd < bestDist) { bestDist = dd; best = b; }
            }
            return best;
        }

        private static IEnumerator WaitForFullHealth(Health h)
        {
            float t = 0f;
            while (h.Current < h.Max && t < 8f)
            {
                t += 0.5f;
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        // ================================================================== Hilfen

        private static PlayerCharacter FindLocalPlayer()
        {
            foreach (PlayerCharacter pc in FindObjectsByType<PlayerCharacter>(FindObjectsSortMode.None))
                if (pc.isActiveAndEnabled) return pc;
            return null;
        }

        private static T Nearest<T>(Vector3 from) where T : Component
        {
            T best = null;
            float bestDist = float.MaxValue;
            foreach (T c in FindObjectsByType<T>(FindObjectsSortMode.None))
            {
                float d = (c.transform.position - from).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = c; }
            }
            return best;
        }

        private void Check(bool ok, string what)
        {
            if (ok) _passed++; else _failed++;
            Line((ok ? "  OK    " : "  FEHLER ") + what);
        }

        private void Info(string what) => Line("  ·     " + what);

        private void Line(string s)
        {
            _report.AppendLine(s);
            if (s.Length > 0) Debug.Log("[Ashveil][Test] " + s.Trim());
        }

        private void OnLog(string message, string stackTrace, LogType type)
        {
            if (message.StartsWith("[Ashveil][Test]") || TestLog.IsEditorTooling(message, stackTrace)) return;
            if (message.Contains("verworfen")) _rejected++;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                _logErrors++;
                if (_logErrors <= 10) _report.AppendLine("  LOG   " + type + ": " + message.Split('\n')[0]);
            }
        }

        private static bool HasArg(string name)
        {
            foreach (string a in Environment.GetCommandLineArgs())
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    
        /// <summary>Versetzen: mit Server-Vorhersage bittet der Spieler den Server (Test-Profil), sonst direkt.</summary>
        private static void Teleport(Ashveil.Gameplay.Characters.CharacterMotor motor, Vector3 pos, float yaw)
        {
            var motion = motor != null ? motor.GetComponent<NetworkMotion>() : null;
            if (motion != null && motion.IsSpawned) motion.RequestTeleport(pos, yaw);
            else if (motor != null) motor.Teleport(pos, yaw);
        }
    }
}
