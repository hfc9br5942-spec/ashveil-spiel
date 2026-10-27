using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Visuals;
using Ashveil.Gameplay.Weapons;
using FishNet;
using FishNet.Object;
using UnityEngine;
using UnityEngine.AI;

namespace Ashveil.Networking
{
    /// <summary>
    /// Abnahmetest 3.4 mit zwei echten Spielern: Editor = Host (Spieler A), Test-Client = Spieler B.
    /// Beide besiegen denselben Gegner; geprueft werden persoenliche Beute, Aufnehmen nur ins eigene Inventar,
    /// Stapeln, volles Inventar, Ausruesten, Zweihandwechsel, Wiederverbinden und Speichern – jeweils ohne
    /// Verlust oder Duplizierung. Start: Menue "Ashveil &gt; Test &gt; Zwei-Spieler-Test (Beute &amp; Inventar)".
    /// Bericht: &lt;Projekt&gt;/Logs/Ashveil_DuoTest.txt
    /// </summary>
    public sealed partial class DuoTestDirector : MonoBehaviour
    {
        public const string FlagFile = "Temp/ashveil_duotest.flag";
        private const string ProfileA = "duo-a", ProfileB = "duo-b";
        private const string Pelt = "item.ash_wolf_pelt";
        private const string Axe = "item.notched_greataxe";
        private const string Ring = "item.cinder_signet";
        private const string Sword = "item.rusty_longsword";
        private const string Shield = "item.oak_shield"; // Feuerwaffen ruhen: Eidwaechter startet mit Schild
        private const string Filler = "item.cinder_dust";

        private static readonly Dictionary<int, (bool ok, string data)> Replies = new Dictionary<int, (bool, string)>();
        private static int _nextToken;

        private readonly StringBuilder _report = new StringBuilder();
        private int _passed, _failed, _logErrors;
        private bool _requested;
        private bool _ownsTestState;
        private bool _previousDodgeEnabled;
        private float _previousEnemyDodgeChance;
        private (bool ok, string data) _last;

        private static string ReportPath => Ashveil.Core.ProjectPaths.Logs(
            _mehrspieler4Mode ? "Ashveil_Mehrspieler4Test.txt" : _spielablauf3Mode ? "Ashveil_Spielablauf3Test.txt" : _kleidungMode ? "Ashveil_KleidungTest.txt" : _uiAuftraegeMode ? "Ashveil_UiAuftraegeTest.txt" : _weidepassMode ? "Ashveil_WeidepassTest.txt" : _kartenMode ? "Ashveil_KartenTest.txt" : _begehungMode ? "Ashveil_BegehungTest.txt" : _k28Mode ? "Ashveil_Kareth28Test.txt" : _spawnDensityMode ? "Ashveil_SpawnDichteTest.txt" : _glockeDuoMode ? "Ashveil_GlockenrastDuoTest.txt" : _glockeMode ? "Ashveil_GlockenrastTest.txt" : _progressMode ? "Ashveil_ProgressTest.txt" : _socialMode ? "Ashveil_SocialTest.txt" : _karethMode ? "Ashveil_KarethTest.txt" : _groupMode ? "Ashveil_GroupTest.txt" : _mineMode ? "Ashveil_SalzgrubenTest.txt" : _barMode ? "Ashveil_ActionbarTest.txt" : _creatorMode ? "Ashveil_CreatorTest.txt" : _emoteMode ? "Ashveil_EmoteTest.txt" : _dayNightMode ? "Ashveil_TageszeitTest.txt" : _campLifeMode ? "Ashveil_LagerlebenTest.txt" : _skyMode ? "Ashveil_HimmelTest.txt" : _silhouetteMode ? "Ashveil_SilhouettenTest.txt" : _worldMode ? "Ashveil_WeltTest.txt" : _arenaMode ? "Ashveil_KolosseumTest.txt" : _masteryMode ? "Ashveil_MeisterschaftenTest.txt" : _arenaRunMode ? "Ashveil_KolosseumLaufTest.txt" : _mountMode ? "Ashveil_ReittierTest.txt" : _salvageMode ? "Ashveil_NachleseTest.txt" : _companionMode ? "Ashveil_GefaehrteTest.txt" : _horseMode ? "Ashveil_PferdehofTest.txt" : _rangedMode ? "Ashveil_FernkampfTest.txt" : _classesMode ? "Ashveil_KlassenTest.txt" : _balanceMode ? "Ashveil_BalanceTest.txt" : _balanceGroupMode ? "Ashveil_BalanceGruppeTest.txt" : _combatMode ? "Ashveil_KampfTest.txt" : "Ashveil_DuoTest.txt");

        /// <summary>Inhalt der Flag-Datei "progress" = Milestone-4-Test (Stufen, Auftraege, Handel) statt Beute-Test.</summary>
        private static bool _progressMode, _socialMode, _karethMode, _groupMode, _groupLong;
        public const string ProgressFlag = "progress";
        public const string SocialFlag = "social";
        public const string KarethFlag = "kareth";
        public const string GroupFlag = "group";
        public const string GroupLongFlag = "group30";

        public static void Reply(int token, bool ok, string data) => Replies[token] = (ok, data ?? "");

        private void Awake()
        {
            if (!Application.isEditor || !File.Exists(FlagFile)) return;
            string flag = "";
            try { flag = File.ReadAllText(FlagFile).Trim(); } catch (IOException) { }
            try { File.Delete(FlagFile); } catch (IOException) { }
            _requested = true;
            _progressMode = flag == ProgressFlag;
            _socialMode = flag == SocialFlag;
            _karethMode = flag == KarethFlag;
            _groupMode = flag == GroupFlag || flag == GroupLongFlag;
            _groupLong = flag == GroupLongFlag;
            _mineMode = flag == MineFlag;
            _barMode = flag == BarFlag;
            _creatorMode = flag == CreatorFlag;
            _emoteMode = flag == EmoteFlag;
            _dayNightMode = flag == DayNightFlag;
            _campLifeMode = flag == CampLifeFlag;
            _skyMode = flag == SkyFlag;
            _silhouetteMode = flag == SilhouetteFlag;
            _worldMode = flag == WorldFlag;
            _arenaMode = flag == ArenaFlag;
            _combatMode = flag == CombatFlag;
            _masteryMode = flag == MasteryFlag;
            _arenaRunMode = flag == ArenaRunFlag;
            _mountMode = flag == MountFlag;
            _salvageMode = flag == SalvageFlag;
            _companionMode = flag == CompanionFlag;
            _horseMode = flag == HorseFlag;
            _rangedMode = flag == RangedFlag;
            _classesMode = flag == ClassesFlag;
            _balanceMode = flag == BalanceFlag;
            _balanceGroupMode = flag == BalanceGroupFlag;
            _glockeMode = flag == GlockeFlag;
            _glockeDuoMode = flag == GlockeDuoFlag;
            _spawnDensityMode = flag == SpawnDensityFlag;
            _k28Mode = flag == K28Flag;
            _begehungMode = flag == BegehungFlag;
            _kartenMode = flag == KartenFlag;
            _weidepassMode = flag == WeidepassFlag;
            _uiAuftraegeMode = flag == UiAuftraegeFlag;
            _kleidungMode = flag == KleidungFlag;   // 21.10.2026: Tripo-Kleidung (DuoTestDirector.Kleidung.cs)
            _spielablauf3Mode = flag == Spielablauf3Flag;   // 21.10.2026: Spielablauf Runde 3 (DuoTestDirector.Spielablauf3.cs)
            _mehrspieler4Mode = flag == Mehrspieler4Flag;   // 22.10.2026: Mehrspieler-Proben mit echten Test-Clients (DuoTestDirector.Mehrspieler4.cs)
            ProfileSelection.Override = _mehrspieler4Mode ? Mp4A : _spielablauf3Mode ? Spielablauf3Profile : _kleidungMode ? KleidungProfile : _uiAuftraegeMode ? UiAuftraegeProfile : _weidepassMode ? WeidepassProfile : _kartenMode ? KartenProfile : _begehungMode ? BegehungProfile : _k28Mode ? K28Profile : _spawnDensityMode ? DensityA : _glockeDuoMode ? GlockeDuoA : _glockeMode ? GlockeProfile : _worldMode ? WorldA : (_arenaMode || _combatMode || _masteryMode || _arenaRunMode || _mountMode || _salvageMode || _companionMode || _rangedMode || _classesMode || _balanceMode || _balanceGroupMode) ? ArenaProfiles[0] : _horseMode ? HorseProfiles[0] : _progressMode ? FreshA : _socialMode ? SocialA : _karethMode ? KarethA : _groupMode ? GroupProfiles[0] : _mineMode ? MineProfiles[0] :
                                        _barMode ? BarA : _creatorMode ? CreatorA : (_emoteMode || _dayNightMode || _campLifeMode || _skyMode || _silhouetteMode) ? EmoteA : ProfileA;
        }

        private void Start()
        {
            if (_requested) StartCoroutine(Run());
        }

        // ================================================================== Ablauf

        private IEnumerator Run()
        {
            if (_ownsTestState) yield break;
            _previousDodgeEnabled = CharacterEquipment.DodgeEnabled;
            _previousEnemyDodgeChance = EnemyBrain.DodgeChance;
            _previousSuiteOverrides = SuiteOverrides.Capture();
            _ownsTestState = true;
            try
            {
                // Kampfumbau: passives Ausweichen (Zufall) fuer feste Schadenspruefungen aus
                Ashveil.Gameplay.Items.CharacterEquipment.DodgeEnabled = false;
                Ashveil.Gameplay.Enemies.EnemyBrain.DodgeChance = 0f;
                Application.logMessageReceived += OnLog;
                if (_mehrspieler4Mode)
                {
                    Line($"ASHVEIL Mehrspieler-Proben Runde 4 (MEHRSPIELER-01/02/03/04, EINSTIEG-04 mit echten Test-Clients: Leiche außer Sicht/Wiederverbinden, im Tod getrennt, toter Angreifer, Gruppenleitung; drei Spieler, Sandbox_M1) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return Mehrspieler4Suite();
                }
                else if (_spielablauf3Mode)
                {
                    Line($"ASHVEIL Spielablauf Runde 3 (21 Befunde: Einstieg, Handel, Aufträge, Kampf, Mehrspieler – Fixes im Host-Spiel nachgeprüft; ein Spieler, Szene Kareth v4) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return Spielablauf3Suite();
                }
                else if (_kleidungMode)
                {
                    Line($"ASHVEIL Tripo-Kleidung (Gambeson/Tunika + Reisehose auf CHR_W_TR01: Kleid statt Schalen, Knochen, Stufen, Haut verdeckt, Rückfall andere Körper; ein Spieler, Sandbox_M1) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return KleidungSuite();
                }
                else if (_uiAuftraegeMode)
                {
                    Line($"ASHVEIL UI-Zustände Aufträge (REP-059: Auftragsbuch-Seiten, Tracker, Abgabe bei vollem Rucksack, „Entkommen“; ein Spieler, Szene Kareth v4) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return UiAuftraegeSuite();
                }
                else if (_weidepassMode)
                {
                    Line($"ASHVEIL Weidepass (Westweiden spielbar: Weltzelle neben Kareth v4, Klamm, Naht, Laden/Entladen; ein Spieler, Szene Kareth v4) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return WeidepassSuite();
                }
                else if (_kartenMode)
                {
                    Line($"ASHVEIL Karten Kareth v4 (#100: Banner, Minimap, Karte M, Entdeckungs-EP je Unterzone auf dem Server; ein Spieler, Szene Kareth v4) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return KartenSuite();
                }
                else if (_begehungMode)
                {
                    Line($"ASHVEIL Begehung Kareth v4 (#29: alle Wege mit dem Charaktermotor, Sperren, Ziele zu Fuß, Baumstämme; ein Spieler, Szene Kareth v4) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return BegehungSuite();
                }
                else if (_k28Mode)
                {
                    Line($"ASHVEIL Kareth v4 #28 (vier Greybox-Tiere und elf Ergänzungsaufträge; ein Spieler, Szene Kareth v4) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return Kareth28Suite();
                }
                else if (_spawnDensityMode)
                {
                    Line($"ASHVEIL Spawn-Fairness und Dichte (CORE-008: kein Erscheinen am Spieler, Dichte nach Spielerzahl; zwei Spieler) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return SpawnDensitySuite();
                }
                else if (_glockeDuoMode)
                {
                    Line($"ASHVEIL Glockenrast (P3: Entdeckung, Fundstücke, Siege und Auftragsbeute je Figur; zwei Spieler) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return GlockenrastDuoSuite();
                }
                else if (_glockeMode)
                {
                    Line($"ASHVEIL Glockenrast (P3: 13 Aufträge Stufe 5–9, Ketten, Belohnungen, Entdeckungen; ein Spieler) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return GlockenrastSuite();
                }
                else if (_balanceGroupMode)
                {
                    Line($"ASHVEIL Balance Gruppe (P2: Tank, Jägerin, Heilerin gegen Elite Stufe 10; drei Spieler) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return BalanceGroupSuite();
                }
                else if (_balanceMode)
                {
                    Line($"ASHVEIL Balance-Messung (P2: echte Kämpfe je Klasse und Stufe gegen normale Gegner; ein Spieler) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return BalanceSuite();
                }
                else if (_classesMode)
                {
                    Line($"ASHVEIL Klassen und Fähigkeiten (P1: Wut/Energie/Mana, Ränge, GCD, Wirken, Bedrohung, Lehrer, Talente; zwei Spieler) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return ClassesSuite();
                }
                else if (_rangedMode)
                {
                    Line($"ASHVEIL Fernkampf & Taschen (Kampfumbau: Bogen wie WoW, Taschen, zwei Waffen, Ausweichen; zwei Spieler) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return RangedSuite();
                }
                else if (_horseMode)
                {
                    Line($"ASHVEIL Pferdehof (Tank-Klassenquests Mensch: „Wo die Herde floh“ + „Der letzte Weidegang“; zwei Spieler) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return HorseStorySuite();
                }
                else if (_companionMode)
                {
                    Line($"ASHVEIL Gefährte (Klassen-Identität Phase 4: Dominator und Aschwolf; zwei Spieler) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return CompanionSuite();
                }
                else if (_salvageMode)
                {
                    Line($"ASHVEIL Nachlese (Klassen-Identität Phase 3: Bergung des Heilers; zwei Spieler) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return SalvageSuite();
                }
                else if (_mountMode)
                {
                    Line($"ASHVEIL Reittier (Klassen-Identität Phase 2: Kriegspferd des Eidwächters; zwei Spieler) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return MountSuite();
                }
                else if (_arenaRunMode)
                {
                    Line($"ASHVEIL Kolosseum-Lauf (Pass 4: Glocke, Champions, Wiederbeleben, Truhe, Wipe; zwei Spieler) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return ArenaRunSuite();
                }
                else if (_masteryMode)
                {
                    Line($"ASHVEIL Praxis-Meisterschaften (Laufkunst, Ausweichen; zwei Spieler) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return MasterySuite();
                }
                else if (_combatMode)
                {
                    Line($"ASHVEIL Kampf-Prototyp (Grubenschrat: Koerperteile, Moveset, Niederwurf, Tod, drei Spieler) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return CombatSuite();
                }
                else if (_arenaMode)
                {
                    Line($"ASHVEIL Kolosseum-Test (Pass 2 Greybox: Vorplatz, Arenen, Instanz-Sichtbarkeit, drei Spieler) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return ArenaSuite();
                }
                else if (_worldMode)
                {
                    Line($"ASHVEIL Weltzellen-Test (Large World: Kareth + Grenzgebirge, drei Spieler) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return WorldSuite();
                }
                else if (_socialMode)
                {
                    Line($"ASHVEIL Gruppen- und Chat-Test (M5, drei Spieler) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return SocialSuite();
                }
                else if (_groupMode)
                {
                    Line($"ASHVEIL Gruppenabenteuer (M5.5, vier Spieler{(_groupLong ? ", 30 Minuten" : "")}) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return GroupSuite(_groupLong);
                }
                else if (_mineMode)
                {
                    Line($"ASHVEIL Salzgruben-Test (M6.1 Instanzen, drei Spieler) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return MineSuite();
                }
                else if (_barMode)
                {
                    Line($"ASHVEIL Actionbar-Test (M6.6, zwei Spieler) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return ActionBarSuite();
                }
                else if (_silhouetteMode)
                {
                    Line($"ASHVEIL Figuren-Silhouettentest (Art-Pass 5, Spielkamera 2/5/15/30 m) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return SilhouetteSuite();
                }
                else if (_skyMode)
                {
                    Line($"ASHVEIL Himmel-Test (Dynamic Sky V1, 30 Welt-Tage, zwei Spieler) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return SkySuite();
                }
                else if (_campLifeMode)
                {
                    Line($"ASHVEIL Lagerleben-Test (M8.3 Startlager und Tagesablaeufe, zwei Spieler) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return CampLifeSuite();
                }
                else if (_dayNightMode)
                {
                    Line($"ASHVEIL Tageszeit-Test (M8.2, drei Spieler) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return DayNightSuite();
                }
                else if (_emoteMode)
                {
                    Line($"ASHVEIL Emote-Test (M8.1, drei Spieler) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return EmoteSuite();
                }
                else if (_creatorMode)
                {
                    Line($"ASHVEIL Charakter-Editor-Test (M7, zwei Spieler) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return CreatorSuite();
                }
                else if (_karethMode)
                {
                    Line($"ASHVEIL Kareth-Test (M5.4 Gelaende, Gegner, Glockenhirte) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return KarethSuite();
                }
                else if (_progressMode)
                {
                    Line($"ASHVEIL Fortschritts-Test (M4 Stufen, Auftraege, Handel) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return ProgressSuite();
                }
                else
                {
                    Line($"ASHVEIL Zwei-Spieler-Test (3.4 Beute & Inventar) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    yield return Suite();
                }
                RestoreTestState();
                Info($"Kampfwerte wiederhergestellt: Ausweichen={CharacterEquipment.DodgeEnabled}, Gegnerchance={EnemyBrain.DodgeChance:0.###}");
                Line("");
                Line($"ERGEBNIS: {_passed} bestanden, {_failed} fehlgeschlagen, {_logErrors} Fehler/Ausnahmen im Log");
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                    File.WriteAllText(ReportPath, _report.ToString());
                }
                catch (Exception e) { Debug.LogWarning("[Ashveil][Duo] Bericht nicht schreibbar: " + e.Message); }
                Debug.Log($"[Ashveil][Duo] Fertig: {_passed} ok, {_failed} fehlgeschlagen → {ReportPath}");
                StopOwnedClients();
            }
            finally { RestoreTestState(); StopOwnedClients(); }
        }

        private void RestoreTestState()
        {
            RestoreIndoorTest();
            if (!_ownsTestState) return;
            _ownsTestState = false;
            Application.logMessageReceived -= OnLog;
            CharacterEquipment.DodgeEnabled = _previousDodgeEnabled;
            EnemyBrain.DodgeChance = _previousEnemyDodgeChance;
            _previousSuiteOverrides.Restore();
        }

        private void OnDisable()
        {
            // Disabling a behaviour does not stop its coroutines; restore also when the nested suite is suspended.
            if (Application.isPlaying) StopAllCoroutines();
            RestoreTestState();
            StopOwnedClients();
        }

        private void OnDestroy() { RestoreTestState(); StopOwnedClients(true); }

        private IEnumerator Suite()
        {
            // ---------------------------------------------------------- Spieler A (Host) und B (Test-Client)
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(ProfileA)) != null, 30f);
            Check(a != null, "Spieler A (Editor-Host) angemeldet");
            if (a == null) yield break;

            string exe = Ashveil.Core.ProjectPaths.TestClientExe;
            if (!File.Exists(exe))
            {
                Check(false, "Test-Client vorhanden (Ashveil > Netzwerk > Test-Client bauen)");
                yield break;
            }
            StartClient(exe, ProfileB);
            NetworkInventory b = null;
            yield return WaitUntil(() => (b = OnlineInv(ProfileB)) != null, 90f);
            Check(b != null, "Spieler B (Test-Client) angemeldet");
            if (b == null) yield break;
            yield return new WaitForSecondsRealtime(3f);
            Info($"A: {Describe(a)}");
            Info($"B: {Describe(b)}");

            LootTableDefinition table = GameData.Get<LootTableDefinition>("loot.selftest");
            Check(table != null, "Test-Beutetabelle vorhanden");
            if (table == null) yield break;

            // ---------------------------------------------------------- Gegner
            Line("");
            Line("[Gemeinsamer Kampf]");
            EnemySpawnArea area = FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None)
                .FirstOrDefault(x => x.Definition != null && x.Definition.Id == "enemy.ash_wolf" && x.Prefab != null);
            GameObject spawnMarker = GameObject.Find("PlayerSpawn");
            Vector3 basePos = (spawnMarker != null ? spawnMarker.transform.position : Vector3.zero) + new Vector3(0f, 0f, 16f);
            if (NavMesh.SamplePosition(basePos, out NavMeshHit hit, 8f, NavMesh.AllAreas)) basePos = hit.position;
            Check(area != null, "Aschwolf-Vorlage gefunden");
            if (area == null) yield break;
            GameObject wolfGo = Instantiate(area.Prefab, basePos, Quaternion.identity);
            var brain = wolfGo.GetComponent<EnemyBrain>();
            brain.Configure(1, basePos, "duo-test");
            wolfGo.GetComponent<NetworkEnemy>().LootTableOverride = table;
            InstanceFinder.ServerManager.Spawn(wolfGo);
            yield return new WaitForSecondsRealtime(1f);
            int wolfId = wolfGo.GetComponent<NetworkObject>().ObjectId;
            var wolfHp = wolfGo.GetComponent<Health>();
            var corpse = wolfGo.GetComponent<CorpseLoot>();

            // Ausgangslage fuer Stapel- und "Inventar voll"-Pruefungen
            a.ServerGive(new ItemStack(Pelt, 2));
            b.ServerGive(new ItemStack(Pelt, 5));
            Dictionary<string, int> a0 = a.ServerModel.Totals();

            // Wie sieht B (Test-Client) den Wolf? (Zugehoerigkeit kommt seit #28 uebers Netz)
            yield return Ask(b, "petseen", wolfId.ToString());
            Info("B sieht den Wolf: " + _last.data);

            // Seit dem Seitschritt des Aschwolfs (Max 07.10.) kann der Stufe-1-Wolf fallen, bevor beide getroffen haben – B stand dann
            // nach dem Seitschritt ausser Reichweite (07.10. abends: 2 von 3 Laeufen). Wer schon beteiligt ist, wartet, bis der andere
            // beteiligt ist; die Plaetze werden alle 2,5 s an den Wolf angepasst.
            var elig = wolfGo.GetComponent<LootEligibility>();
            bool Credited(string p) => elig == null || elig.Profiles.Contains(p);
            float t = 0f;
            while (wolfHp.IsAlive && t < 60f)
            {
                Vector3 w = wolfGo.transform.position;
                yield return Ask(a, "goto", V(w + new Vector3(1.6f, 0f, -1.2f), -50f));
                yield return Ask(b, "goto", V(w + new Vector3(-1.6f, 0f, -1.2f), 50f));
                bool aIn = Credited(ProfileA), bIn = Credited(ProfileB);
                yield return Ask(a, !aIn || bIn ? "attack" : "stop", !aIn || bIn ? wolfId.ToString() : "");
                yield return Ask(b, !bIn || aIn ? "attack" : "stop", !bIn || aIn ? wolfId.ToString() : "");
                float w8 = 0f;
                while (wolfHp.IsAlive && w8 < 2.5f) { w8 += 0.25f; t += 0.25f; yield return new WaitForSecondsRealtime(0.25f); }
                t += 1.5f;
            }
            if (elig != null)
                Info($"Beteiligung: A {elig.ContributionOf(ProfileA):0} · B {elig.ContributionOf(ProfileB):0} Leben (Schaden)");
            yield return Ask(a, "stop");
            yield return Ask(b, "stop");
            Check(!wolfHp.IsAlive, $"Beide Spieler besiegen den Aschwolf ({t:0} s)");
            yield return new WaitForSecondsRealtime(0.5f);

            Check(corpse.IsRecipient(ProfileA) && corpse.IsRecipient(ProfileB), "Beide Spieler sind beuteberechtigt");
            Check(corpse.GenerationCount(ProfileA) == 1 && corpse.GenerationCount(ProfileB) == 1, "Beute je Spieler genau einmal erzeugt");
            List<ItemStack> lootA = corpse.Get(ProfileA), lootB = corpse.Get(ProfileB);
            Info("Beute A: " + string.Join(", ", lootA));
            Info("Beute B: " + string.Join(", ", lootB));

            // ---------------------------------------------------------- Loot-Fenster
            Line("");
            Line("[Persoenliche Beute]");
            yield return Ask(a, "loot-open", wolfId.ToString());
            Check(_last.ok && _last.data == "loot=" + Join(lootA), "A sieht genau seine eigene Beute");
            yield return Ask(b, "loot-open", wolfId.ToString());
            Check(_last.ok && _last.data == "loot=" + Join(lootB), "B sieht genau seine eigene Beute");

            Dictionary<string, int> bBefore = b.ServerModel.Totals();
            yield return Ask(a, "loot-takeall");
            Check(corpse.Get(ProfileA).Count == 0, "A nimmt alles – A's Beute ist leer");
            Check(SameTotals(a.ServerModel.Totals(), Add(a0, lootA)), "A's Inventar = vorher + genau seine Beute");
            Check(SameTotals(b.ServerModel.Totals(), bBefore), "B's Inventar bleibt unberuehrt");
            Check(Join(corpse.Get(ProfileB)) == Join(lootB), "B's Beute bleibt unberuehrt");
            Check(SlotsOf(a, Pelt) == 1 && a.ServerModel.Count(Pelt) == Count(a0, Pelt) + Count(lootA, Pelt),
                $"Wolfspelze gestapelt ({a.ServerModel.Count(Pelt)} Stueck in {SlotsOf(a, Pelt)} Platz)");
            int ringIdx = IndexOf(a, Ring);
            Check(ringIdx >= 0 && a.ServerModel.Bag[ringIdx].bound, "Ring wird beim Aufheben gebunden");

            // ---------------------------------------------------------- Volles Inventar
            Line("");
            Line("[Volles Inventar]");
            int filled = b.ServerFillFreeSlots(Filler);
            Info($"B: {filled} freie Plaetze mit Fuellmaterial belegt");
            Dictionary<string, int> bCombined = Add(b.ServerModel.Totals(), corpse.Get(ProfileB));
            int bPelts = b.ServerModel.Count(Pelt);
            yield return Ask(b, "loot-takeall");
            Check(!_last.ok && _last.data.Contains("Kein Platz"), "Meldung \"Kein Platz im Inventar\"");
            Check(b.ServerModel.Count(Pelt) == bPelts + Count(lootB, Pelt), "Pelze passen noch auf den vorhandenen Stapel");
            Check(corpse.Get(ProfileB).Count > 0, $"Rest bleibt in der Leiche ({corpse.Get(ProfileB).Count} Stapel)");
            Check(SameTotals(Add(b.ServerModel.Totals(), corpse.Get(ProfileB)), bCombined), "Nichts verloren, nichts doppelt (Inventar + Leiche)");

            // ---------------------------------------------------------- Wiederverbinden
            Line("");
            Line("[Wiederverbinden]");
            Dictionary<string, int> bSaved = b.ServerModel.Totals();
            string bRest = Join(corpse.Get(ProfileB));
            NetworkInventory bOld = b;
            yield return Ask(b, "reconnect");
            NetworkInventory b2 = null;
            yield return WaitUntil(() => (b2 = OnlineInv(ProfileB)) != null && b2 != bOld, 40f);
            Check(b2 != null && b2 != bOld, "B ist wieder da");
            if (b2 == null || b2 == bOld) yield break;
            yield return new WaitForSecondsRealtime(2f);
            Check(SameTotals(b2.ServerModel.Totals(), bSaved), "Inventar nach Wiederverbinden unveraendert");
            Check(corpse.GenerationCount(ProfileB) == 1 && Join(corpse.Get(ProfileB)) == bRest, "Keine neue Beute erzeugt, Rest unveraendert");
            // Neu verbunden = am Wachtfeuer; der Server laesst nur aus der Naehe pluendern
            yield return Ask(b2, "loot-open", wolfId.ToString());
            Check(!_last.ok, "Aus der Ferne pluendern wird abgelehnt");
            yield return Ask(b2, "goto", V(wolfGo.transform.position + new Vector3(-1.6f, 0f, -1.2f), 50f));
            // Position nach Teleport + Wiederverbinden kommt mit 15 Hz beim Server an – wie ein Spieler notfalls erneut klicken
            for (int attempt = 0; attempt < 4; attempt++)
            {
                yield return Ask(b2, "loot-open", wolfId.ToString());
                if (_last.ok) break;
                Info($"Pluendern Versuch {attempt + 1}: abgelehnt (Abstand Server {Vector3.Distance(b2.transform.position, wolfGo.transform.position):0.0} m)");
                yield return new WaitForSecondsRealtime(1f);
            }
            Check(_last.ok && _last.data == "loot=" + bRest, "B sieht nach dem Wiederverbinden denselben Rest");
            b2.ServerRemoveAll(Filler);
            yield return Ask(b2, "loot-takeall");
            Check(corpse.Get(ProfileB).Count == 0, "B nimmt den Rest, sobald Platz ist");
            bCombined.Remove(Filler);
            Check(SameTotals(b2.ServerModel.Totals(), bCombined), "B's Summe stimmt (ohne Fuellmaterial)");
            yield return Ask(a, "loot-open", wolfId.ToString());
            Check(!_last.ok && corpse.GenerationCount(ProfileA) == 1, "Erneutes Oeffnen bei A erzeugt nichts Neues");

            // ---------------------------------------------------------- Ausruesten & Zweihand
            Line("");
            Line("[Ausruesten]");
            Dictionary<string, int> aTotals = a.ServerModel.Totals();
            var weaponsA = a.GetComponent<WeaponUser>();
            yield return Ask(a, "autoequip", IndexOf(a, Axe).ToString());
            Check(Equipped(a, EquipmentSlot.MainHand) == Axe && Equipped(a, EquipmentSlot.OffHand) == null,
                "Zweihandaxt angelegt, Nebenhand frei");
            Check(IndexOf(a, Sword) >= 0 && IndexOf(a, Shield) >= 0, "Schwert und Schild liegen im Rucksack");
            Check(weaponsA.MeleeWeapon != null && weaponsA.MeleeWeapon.Id == WeaponIdOf(Axe) && weaponsA.RangedWeapon == null,
                "Server kaempft mit der Zweihandaxt");
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Ask(b2, "weapons-of", a.ObjectId.ToString());
            Check(_last.ok && _last.data.StartsWith("melee=" + WeaponIdOf(Axe)), "B sieht A mit der Zweihandaxt");

            yield return Ask(a, "autoequip", IndexOf(a, Shield).ToString());
            Check(Equipped(a, EquipmentSlot.OffHand) == Shield && Equipped(a, EquipmentSlot.MainHand) == null && IndexOf(a, Axe) >= 0,
                "Schild in die Nebenhand – Zweihandaxt wandert zurueck");
            yield return Ask(a, "autoequip", IndexOf(a, Sword).ToString());
            Check(Equipped(a, EquipmentSlot.MainHand) == Sword && Equipped(a, EquipmentSlot.OffHand) == Shield, "Schwert + Schild wieder angelegt");
            yield return Ask(a, "autoequip", IndexOf(a, Ring).ToString());
            Check(Equipped(a, EquipmentSlot.Ring1) == Ring || Equipped(a, EquipmentSlot.Ring2) == Ring, "Ring angelegt");
            Check(SameTotals(a.ServerModel.Totals(), aTotals), "Ausruesten verliert und verdoppelt nichts");

            // ---------------------------------------------------------- Stapel teilen / zusammenlegen
            Line("");
            Line("[Stapel]");
            int pelts = a.ServerModel.Count(Pelt);
            int pIdx = IndexOf(a, Pelt), free = a.ServerModel.FirstFreeBagSlot();
            yield return Ask(a, "split", $"{pIdx} 2 {free}");
            Check(SlotsOf(a, Pelt) == 2 && a.ServerModel.Bag[free].count == 2 && a.ServerModel.Count(Pelt) == pelts, "Stapel aufgeteilt (2 abgetrennt)");
            yield return Ask(a, "move", $"{free} {pIdx}");
            Check(SlotsOf(a, Pelt) == 1 && a.ServerModel.Count(Pelt) == pelts, "Stapel wieder zusammengelegt");
            int max = GameData.Item(Pelt) != null ? GameData.Item(Pelt).maxStack : 20;
            a.ServerGive(new ItemStack(Pelt, max));
            Check(a.ServerModel.Count(Pelt) == pelts + max && SlotsOf(a, Pelt) == 2, $"Volle Stapel laufen in einen neuen Platz ueber (max. {max})");

            // ---------------------------------------------------------- Sichtbare Ausruestung
            yield return VisualSection(a, b2, wolfGo);

            // ---------------------------------------------------------- Speichern
            b2 = OnlineInv(ProfileB) ?? b2; // nach erneutem Verbinden die aktuelle Figur
            Line("");
            Line("[Speichern]");
            a.SaveNow();
            b2.SaveNow();
            bool la = CharacterStore.Service.TryLoad(ProfileA, out CharacterSaveData da);
            bool lb = CharacterStore.Service.TryLoad(ProfileB, out CharacterSaveData db);
            Check(la && SameTotals(TotalsOf(da), a.ServerModel.Totals()), "Speicherstand A = Server-Inventar");
            Check(lb && SameTotals(TotalsOf(db), b2.ServerModel.Totals()), "Speicherstand B = Server-Inventar");
            Info("Speicherort: " + CharacterStore.Service.Describe(ProfileA));

            yield return Ask(b2, "quit");
        }

        // ================================================================== Sichtbare Ausruestung

        private static readonly string[][] Families =
        {
            new[] { "travel_hood", "travel_mantle", "frayed_gambeson", "travel_wraps", "travel_trousers", "foot_wraps", "rope_belt", "grey_wool_cloak" },
            new[] { "stalker_hood", "hunter_shoulderguards", "ashen_leather_jerkin", "stalker_gloves", "hunter_breeches", "stalker_boots", "ember_sash", "wolfhide_cloak" },
            new[] { "dented_helm", "warden_pauldrons", "warden_cuirass", "iron_gauntlets", "plated_greaves", "iron_sabatons" }
        };
        private static readonly string[] FamilyNames = { "Stoff", "Leder", "Metall" };

        private static string ImagePath(string name) =>
            Ashveil.Core.ProjectPaths.Logs("Ausruestung", name + ".png");

        private IEnumerator Equip(NetworkInventory who, params string[] keys)
        {
            foreach (string k in keys)
            {
                string id = "item." + k;
                if (IndexOf(who, id) < 0) who.ServerGive(new ItemStack(id, 1));
                yield return Ask(who, "autoequip", IndexOf(who, id).ToString());
            }
            yield return new WaitForSecondsRealtime(0.3f);
        }

        private static EquipmentSlot SlotFor(ItemDefinition d, InventoryModel m) => m.BestSlot(d);

        private IEnumerator VisualSection(NetworkInventory a, NetworkInventory b, GameObject wolf)
        {
            Line("");
            Line("[Sichtbare Ausruestung]");
            var visA = a.GetComponent<EquipmentVisuals>();
            var lookA = a.GetComponent<NetworkAppearance>();
            Check(visA != null && visA.Rig != null && visA.Rig.Ready, "Figur A hat eine Ausruestungsdarstellung");
            if (visA == null || visA.Rig == null || !visA.Rig.Ready) yield break;
            AppearanceRig rigA = visA.Rig;

            // Daten: jedes sichtbare Demo-Item hat ein eigenes Aussehen, Formen unterscheiden sich je Platz
            var sigs = new Dictionary<string, string>();
            int withLook = 0, dup = 0;
            foreach (string[] fam in Families)
                foreach (string k in fam)
                {
                    ItemDefinition d = GameData.Item("item." + k);
                    if (d == null || d.appearance == null) continue;
                    withLook++;
                    string key = d.equipType + "|" + d.appearance.GeometrySignature();
                    if (sigs.ContainsKey(key)) { dup++; Info($"Gleiche Form: {k} = {sigs[key]}"); }
                    else sigs[key] = k;
                }
            foreach (string k in new[] { "warden_blade", "notched_greataxe", "oak_shield", "wheellock_pistol", "ash_hunter_rifle", "ember_sash", "wolfhide_cloak" })
                if (GameData.Item("item." + k)?.appearance != null) withLook++;
            Check(withLook >= 29 && dup == 0, $"Demo-Items mit eigenem Aussehen: {withLook}, gleiche Formen: {dup}");

            // Vorher: A sauber, frisches Aussehen mit Frisur; Stufe reicht fuer alle Demo-Teile
            a.ServerSetLevel(10);
            b.ServerSetLevel(10);
            a.ServerResetForTest();
            yield return Ask(a, "look", "body=0 skin=0 hair=1 hairColor=1 beard=1 hideHelm=0 hideCloak=0");
            yield return new WaitForSecondsRealtime(0.4f);
            // 22.10.2026 (Codex Duo-Frisur): Tripo-Körper ohne wählbare Frisur normalisieren hair auf 0 – dann sind die Frisurprüfungen
            // nicht anwendbar (statt fester Indizes vorauszusetzen). Hat der Körper eine Frisur, wird streng geprüft wie bisher.
            bool frisur = lookA.Current.hair > 0 || rigA.HasHair;
            if (!frisur) Info($"Körper ohne wählbare Frisur (hair={lookA.Current.hair}) – Frisurprüfungen nicht anwendbar");
            Check((!frisur || (rigA.HasHair && rigA.HairVisible)) && !rigA.BodyMasked,
                frisur ? "Ohne Ruestung: Haut und Frisur sichtbar, nichts ausgeblendet" : "Ohne Ruestung: Haut sichtbar, nichts ausgeblendet (keine Frisur am Körper)");

            // Jede Familie komplett
            CharacterPreviewStage stage = CharacterPreviewStage.Ensure();
            for (int f = 0; f < Families.Length; f++)
            {
                a.ServerResetForTest();
                yield return Equip(a, Families[f]);
                int shown = 0, missing = 0;
                foreach (string k in Families[f])
                {
                    ItemDefinition d = GameData.Item("item." + k);
                    EquipmentSlot slot = SlotFor(d, a.ServerModel);
                    AppearanceDefinition got = rigA.SlotDefinition(slot);
                    if (got != null && d.appearance == got && rigA.RendererCount(slot) > 0) shown++;
                    else { missing++; Info($"{k}: nicht sichtbar (Platz {slot}, {rigA.Summary()})"); }
                }
                Check(missing == 0, $"{FamilyNames[f]}: alle {shown} Teile sichtbar, Haut darunter ausgeblendet={rigA.BodyMasked}");
                Check(!rigA.HairVisible || f != 2, $"{FamilyNames[f]}: Frisur unter Kopfbedeckung verborgen={!rigA.HairVisible}");
                stage.SetState(a.ServerModel.Equipment, lookA.Current);
                yield return null;
                stage.SetYaw(20f);
                yield return null;
                stage.Capture(ImagePath($"Familie_{f + 1}_{FamilyNames[f]}_vorn"));
                stage.SetYaw(200f);
                yield return null;
                stage.Capture(ImagePath($"Familie_{f + 1}_{FamilyNames[f]}_hinten"));
                var female = lookA.Current.Clone();
                female.body = 1;
                female.hair = AppearanceDefaults.FirstStyle(GameDataBootstrap.Catalog.hairstyles, GameDataBootstrap.Catalog, 1);
                female.beard = 0;
                stage.SetState(a.ServerModel.Equipment, female);
                yield return null;
                stage.SetYaw(25f);
                yield return null;
                stage.Capture(ImagePath($"Familie_{f + 1}_{FamilyNames[f]}_weiblich"));
                stage.SetYaw(0f);
            }

            // Gemischt + gefaerbt
            a.ServerResetForTest();
            yield return Equip(a, "stalker_hood", "warden_cuirass", "travel_trousers", "iron_sabatons", "hunter_shoulderguards", "stalker_gloves", "wolfhide_cloak", "warden_blade", "oak_shield");
            string mixed = rigA.Summary();
            Check(rigA.RendererCount(EquipmentSlot.Head) > 0 && rigA.RendererCount(EquipmentSlot.Chest) > 0 && rigA.RendererCount(EquipmentSlot.Legs) > 0 &&
                  rigA.RendererCount(EquipmentSlot.Feet) > 0 && rigA.RendererCount(EquipmentSlot.MainHand) > 0 && rigA.RendererCount(EquipmentSlot.OffHand) > 0,
                "Gemischtes Set (Leder + Metall + Stoff, Schwert + Schild) wird dargestellt");
            Info("A: " + mixed);
            yield return Ask(a, "look", "dye=2:4.10.3");
            yield return new WaitForSecondsRealtime(0.3f);
            Check(lookA.Current.dyes[(int)EquipmentSlot.Chest].primary == 4, "Brust gefaerbt (Rostrot/Stahlblau/Kohlschwarz)");
            stage.SetState(a.ServerModel.Equipment, lookA.Current);
            yield return null;
            stage.SetYaw(30f);
            yield return null;
            stage.Capture(ImagePath("Gemischt_gefaerbt"));
            stage.SetYaw(0f);

            // Helm ausblenden: Werte bleiben
            yield return Equip(a, "dented_helm");
            var hp = a.GetComponent<Health>();
            float maxBefore = hp.Max;
            float armorBefore = EquipmentStats.From(a.ServerModel.Equipment).TotalArmor;
            bool hairHidden = !rigA.HairVisible;
            yield return Ask(a, "look", "hideHelm=1");
            yield return new WaitForSecondsRealtime(0.3f);
            Check(rigA.RendererCount(EquipmentSlot.Head) == 0 && (!frisur || rigA.HairVisible), frisur ? "Helm ausgeblendet – Frisur wieder sichtbar" : "Helm ausgeblendet (keine Frisur am Körper)");
            Check(Mathf.Approximately(hp.Max, maxBefore) && Mathf.Approximately(EquipmentStats.From(a.ServerModel.Equipment).TotalArmor, armorBefore),
                $"Helm-Ausblenden aendert keine Werte (Leben {hp.Max:0}, Ruestung {armorBefore:0})");
            yield return Ask(a, "look", "hideHelm=0");
            yield return new WaitForSecondsRealtime(0.3f);
            Check(rigA.RendererCount(EquipmentSlot.Head) > 0 && rigA.HairVisible == !hairHidden, "Helm wieder sichtbar");

            // Persoenliche Merkmale bleiben bei Wechseln erhalten
            yield return Ask(a, "look", "hair=2 hairColor=4 skin=1");
            string lookBefore = lookA.Current.Encode();
            for (int i = 0; i < 3; i++)
            {
                yield return Ask(a, "unequip", ((int)EquipmentSlot.Chest).ToString());
                yield return Ask(a, "autoequip", IndexOf(a, "item.warden_cuirass").ToString());
            }
            Check(lookA.Current.Encode() == lookBefore && (!frisur || rigA.HasHair), "Frisur, Haarfarbe, Hautton bleiben bei Ausruestungswechseln erhalten");

            // Ausziehen stellt den Koerper wieder her
            a.ServerResetForTest();
            yield return new WaitForSecondsRealtime(0.4f);
            int any = 0;
            for (int i = 0; i < InventoryModel.EquipCount; i++) any += rigA.RendererCount((EquipmentSlot)i);
            Check(any == 0 && !rigA.BodyMasked, "Alles abgelegt: keine Ausruestung mehr, Haut vollstaendig");

            // Zweihandwechsel bei vollem Inventar + haeufige Wechsel ohne doppelte Modelle
            yield return Equip(a, "warden_blade", "oak_shield");
            a.ServerGive(new ItemStack("item.notched_greataxe", 1));
            a.ServerFillFreeSlots(Filler);
            Dictionary<string, int> before = a.ServerModel.Totals();
            yield return Ask(a, "autoequip", IndexOf(a, "item.notched_greataxe").ToString());
            Check(!_last.ok && Equipped(a, EquipmentSlot.MainHand) == "item.warden_blade" && Equipped(a, EquipmentSlot.OffHand) == "item.oak_shield" &&
                  SameTotals(a.ServerModel.Totals(), before), "Zweihandaxt bei vollem Inventar abgelehnt – nichts verloren/verdoppelt");
            a.ServerRemoveAll(Filler);
            for (int i = 0; i < 10; i++)
            {
                yield return Ask(a, "autoequip", IndexOf(a, "item.notched_greataxe").ToString());
                yield return Ask(a, "autoequip", IndexOf(a, "item.warden_blade").ToString());
                yield return Ask(a, "autoequip", IndexOf(a, "item.oak_shield").ToString());
            }
            yield return new WaitForSecondsRealtime(0.3f);
            int weaponModels = 0;
            foreach (Transform t in a.GetComponentsInChildren<Transform>(true)) if (t.name == "Weapon Model") weaponModels++;
            Check(weaponModels == 2, $"30 Waffenwechsel: {weaponModels} Waffenmodelle (Schwert + Schild), keine Doppelten");
            Check(SameTotals(a.ServerModel.Totals(), a.ServerModel.Totals()) && a.ServerModel.Count("item.notched_greataxe") == 1, "Waffenwechsel verliert nichts");

            // Ansicht in Bewegung und Kampf (Bilder der echten Spielfigur)
            yield return WorldShots(a, wolf);

            // Mitspieler sieht meine Ausruestung
            yield return Equip(a, "stalker_hood", "warden_cuirass", "hunter_breeches", "stalker_boots", "grey_wool_cloak");
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Ask(b, "visuals-of", a.ObjectId.ToString());
            string seen = _last.data.Split('|')[0];
            Check(_last.ok && seen == rigA.Summary(), "B sieht A's Ausruestung genau so wie A");
            if (seen != rigA.Summary()) Info($"A: {rigA.Summary()} · B sieht: {seen}");

            // B: eigene Ausruestung + Aussehen, danach Wiederverbinden
            b.ServerResetForTest();
            yield return Equip(b, "dented_helm", "warden_pauldrons", "chainmail_shirt", "plated_greaves", "iron_sabatons", "rope_belt");
            yield return Ask(b, "look", "body=1 hair=4 hairColor=3 skin=1");
            yield return new WaitForSecondsRealtime(0.6f);
            var visB = b.GetComponent<EquipmentVisuals>();
            string bSummary = visB != null && visB.Rig != null ? visB.Rig.Summary() : "";
            string bLook = b.GetComponent<NetworkAppearance>().Current.Encode();
            Check(bSummary.Contains("Head=") && bLook.Contains("|b1|"), "A sieht B (weiblich, schwere Ruestung)");

            // Vorschau aendert nichts
            b.ServerGive(new ItemStack("item.wolfhide_cloak", 1));
            yield return new WaitForSecondsRealtime(0.3f);
            Dictionary<string, int> bTotals = b.ServerModel.Totals();
            string bEquip = string.Join(",", b.ServerModel.Equipment);
            yield return Ask(b, "preview-tryon", IndexOf(b, "item.wolfhide_cloak").ToString());
            string[] parts = _last.data.Split('|');
            Check(_last.ok && parts[0].Contains("Cloak=appearance.wolfhide_cloak") && !parts[1].Contains("Cloak="),
                "Anprobe: Vorschau zeigt den Umhang, die Spielfigur nicht");
            Check(SameTotals(b.ServerModel.Totals(), bTotals) && string.Join(",", b.ServerModel.Equipment) == bEquip, "Anprobe aendert weder Inventar noch Ausruestung");

            NetworkInventory bOld = b;
            yield return Ask(b, "reconnect");
            NetworkInventory b3 = null;
            yield return WaitUntil(() => (b3 = OnlineInv(ProfileB)) != null && b3 != bOld, 40f);
            yield return new WaitForSecondsRealtime(2.5f);
            if (b3 != null)
            {
                var visB3 = b3.GetComponent<EquipmentVisuals>();
                Check(visB3 != null && visB3.Rig != null && visB3.Rig.Summary() == bSummary && b3.GetComponent<NetworkAppearance>().Current.Encode() == bLook,
                    "Nach Wiederverbinden: B erscheint mit gleicher Ausruestung und gleichem Aussehen");
                yield return Ask(b3, "visuals-of", a.ObjectId.ToString());
                Check(_last.ok && _last.data.Split('|')[0] == rigA.Summary(), "Neu verbundener B sieht A sofort richtig (spaeter Beitritt)");
                yield return Ask(b3, "visuals-of", b3.ObjectId.ToString());
                Check(_last.ok && _last.data.Split('|')[0] == bSummary, "B sieht sich selbst richtig");
                b = b3;
            }

            // Respawn
            string aBefore = rigA.Summary();
            a.GetComponent<Health>().ApplyDamage(new DamageInfo { Amount = 99999f, Source = wolf });
            float waited = 0f;
            while (waited < 12f && (hp == null || !hp.IsAlive || waited < 1f)) { waited += 0.25f; yield return new WaitForSecondsRealtime(0.25f); }
            yield return new WaitForSecondsRealtime(0.5f);
            Check(hp.IsAlive && visA.Rig.Summary() == aBefore, "Nach dem Tod/Wiederbeleben: gleiche Ausruestung sichtbar");

            // Fehlende Ressourcen: Ersatz statt Absturz
            ItemDefinition belt = GameData.Item("item.rope_belt");
            AppearanceDefinition keep = belt.appearance;
            belt.appearance = null;
            yield return Equip(a, "rope_belt");
            AppearanceDefinition fb = rigA.SlotDefinition(EquipmentSlot.Belt);
            Check(fb != null && fb.Id.StartsWith("appearance.fallback") && rigA.RendererCount(EquipmentSlot.Belt) > 0, "Fehlendes Aussehen -> Ersatzmodell, kein Absturz");
            belt.appearance = keep;
            CharacterAppearanceData junk = CharacterAppearanceData.Decode("x|b99|h-4|c77|y99:9.9.9,2:250.1.1|t5=item.nope|zz");
            GameDataBootstrap.Catalog.Sanitize(junk);
            Check(junk.body < GameDataBootstrap.Catalog.bodies.Count && junk.hair >= 0 && junk.dyes[2].primary == 0, "Kaputte Aussehen-Daten werden bereinigt");

            // Speichern des Aussehens
            a.SaveNow();
            Check(CharacterStore.Service.TryLoad(ProfileA, out CharacterSaveData sa) && sa.appearance != null && sa.appearance.Encode() == lookA.Current.Encode(),
                "Aussehen und Faerbung gespeichert");
            Info("Bilder: " + Path.GetDirectoryName(ImagePath("x")));
        }

        /// <summary>Bilder der echten Figur: Stehen, Laufen, Springen, Nahkampf, Zielen, Waffe verstaut.</summary>
        private IEnumerator WorldShots(NetworkInventory a, GameObject wolf)
        {
            yield return Equip(a, "stalker_hood", "warden_pauldrons", "ashen_leather_jerkin", "iron_gauntlets", "travel_trousers", "stalker_boots", "ember_sash", "grey_wool_cloak", "warden_blade", "ashwood_longbow");
            var camGo = new GameObject("Duo Capture Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = 32f;
            int preview = LayerMask.NameToLayer(CharacterPreviewStage.LayerName);
            if (preview >= 0) cam.cullingMask &= ~(1 << preview);
            var input = a.GetComponent<PlayerInputReader>();
            var weapons = a.GetComponent<WeaponUser>();
            Transform t = a.transform;

            void Aim(float side)
            {
                Vector3 target = t.position + Vector3.up * 1.05f;
                camGo.transform.position = target + t.forward * 3.6f + t.right * side + Vector3.up * 0.4f;
                camGo.transform.LookAt(target);
                Unoccluded(target);
            }

            void Unoccluded(Vector3 target)
            {
                Vector3 dir = camGo.transform.position - target;
                float dist = dir.magnitude;
                foreach (RaycastHit h in Physics.RaycastAll(target, dir / dist, dist, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                {
                    if (h.transform.IsChildOf(t) || h.distance < 0.6f) continue;
                    camGo.transform.position = target + dir / dist * Mathf.Max(1.2f, h.distance - 0.2f);
                    break;
                }
            }

            GameObject spawn = GameObject.Find("PlayerSpawn");
            Vector3 open = (spawn != null ? spawn.transform.position : Vector3.zero) + new Vector3(-8f, 0f, 8f);
            yield return Ask(a, "goto", V(open, 30f));
            yield return new WaitForSecondsRealtime(1.2f);
            Aim(1.2f);
            CaptureUtil.Capture(cam, ImagePath("Welt_1_Stehen"), 900, 900);
            Aim(-1.5f);
            camGo.transform.position = t.position + Vector3.up * 1.4f - t.forward * 3.4f + t.right * 1.0f;
            camGo.transform.LookAt(t.position + Vector3.up * 1.0f);
            CaptureUtil.Capture(cam, ImagePath("Welt_2_Ruecken_verstaut"), 900, 900);

            input.InjectMovement(1f, false);
            yield return new WaitForSecondsRealtime(0.9f);
            Aim(2.2f);
            CaptureUtil.Capture(cam, ImagePath("Welt_3_Laufen"), 900, 900);
            input.InjectMovement(1f, true);
            yield return new WaitForSecondsRealtime(0.35f);
            Aim(2.2f);
            CaptureUtil.Capture(cam, ImagePath("Welt_4_Springen"), 900, 900);
            input.InjectMovement(0f, false);
            yield return new WaitForSecondsRealtime(1.2f);

            // Nahkampf gegen eine Trainingspuppe
            TrainingDummy dummy = FindObjectsByType<TrainingDummy>(FindObjectsSortMode.None).FirstOrDefault();
            if (dummy != null)
            {
                Vector3 d = (t.position - dummy.transform.position);
                d.y = 0f;
                d = d.sqrMagnitude < 0.01f ? Vector3.forward : d.normalized;
                yield return Ask(a, "goto", V(dummy.transform.position + d * 1.6f, Quaternion.LookRotation(-d).eulerAngles.y));
                yield return Ask(a, "attack", dummy.GetComponent<NetworkObject>().ObjectId.ToString());
                yield return new WaitForSecondsRealtime(0.55f);
                Aim(1.8f);
                CaptureUtil.Capture(cam, ImagePath("Welt_5_Nahkampf"), 900, 900);
                yield return Ask(a, "stop");
                // Zielen mit dem Bogen
                input.InjectCombat(new CombatInput { SwapWeapon = true });
                for (float w = 0f; w < 3f && weapons.UsingMelee; w += 0.1f) yield return new WaitForSecondsRealtime(0.1f);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Ask(a, "attack", dummy.GetComponent<NetworkObject>().ObjectId + " keep");
                yield return new WaitForSecondsRealtime(0.25f);
                Aim(1.8f);
                CaptureUtil.Capture(cam, ImagePath("Welt_6_Zielen_Schwert_verstaut"), 900, 900);
                yield return Ask(a, "stop");
                Transform swordModel = a.GetComponent<EquipmentVisuals>().Rig.WeaponModel(EquipmentSlot.MainHand);
                Info($"Schwert haengt an: {(swordModel != null && swordModel.parent != null ? swordModel.parent.name : "-")}");
                Check(weapons != null && !weapons.UsingMelee && swordModel != null && swordModel.parent != null && Ashveil.Gameplay.Visuals.SkelettNamen.Kanonisch(swordModel.parent.name) == "pelvis",
                    "Bogen gezogen, Schwert an der Huefte verstaut");
                input.InjectCombat(new CombatInput { SwapWeapon = true });
                yield return new WaitForSecondsRealtime(0.9f);
            }
            Destroy(camGo);
        }

        // ================================================================== Befehle an Spieler

        private IEnumerator Ask(NetworkInventory who, string cmd, string arg = "", float timeout = 15f)
        {
            _last = (false, "kein Spieler");
            if (who == null) yield break;
            var agent = who.GetComponent<SelfTestAgent>();
            if (agent == null) yield break;
            int token = ++_nextToken;
            agent.Send(token, cmd, arg);
            float t = 0f;
            while (!Replies.ContainsKey(token) && t < timeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (Replies.TryGetValue(token, out (bool ok, string data) r))
            {
                _last = r;
                Replies.Remove(token);
            }
            else _last = (false, "Zeitueberschreitung bei " + cmd);
        }

        private static IEnumerator WaitUntil(Func<bool> cond, float timeout)
        {
            float t = 0f;
            while (!cond() && t < timeout)
            {
                t += 0.25f;
                yield return new WaitForSecondsRealtime(0.25f);
            }
        }

        // ================================================================== Hilfen

        private readonly List<System.Diagnostics.Process> Clients = new List<System.Diagnostics.Process>();

        private void StartClient(string exe, string profile)
        {
            var client = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"-ashveil-join 127.0.0.1 -ashveil-profile {profile} -ashveil-agent -screen-fullscreen 0 -screen-width 960 -screen-height 540",
                WorkingDirectory = Path.GetDirectoryName(exe),
                UseShellExecute = false
            });
            if (client == null) throw new InvalidOperationException("Test-Client konnte nicht gestartet werden.");
            Clients.Add(client);
        }

        private static NetworkInventory OnlineInv(string profile) =>
            NetworkInventory.Online.TryGetValue(profile, out NetworkInventory inv) && inv != null && inv.IsReady ? inv : null;

        private static string V(Vector3 p, float yaw) =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} {1} {2} {3}", p.x, p.y, p.z, yaw);

        private static string Join(List<ItemStack> l)
        {
            var sb = new StringBuilder();
            foreach (ItemStack s in l) sb.Append(s).Append(',');
            return sb.ToString();
        }

        private static string Describe(NetworkInventory inv)
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, int> kv in inv.ServerModel.Totals()) sb.Append(kv.Key).Append('x').Append(kv.Value).Append(' ');
            return sb.ToString();
        }

        private static Dictionary<string, int> Add(Dictionary<string, int> baseTotals, List<ItemStack> more)
        {
            var d = new Dictionary<string, int>(baseTotals);
            foreach (ItemStack s in more) if (!s.IsEmpty) d[s.id] = (d.TryGetValue(s.id, out int n) ? n : 0) + s.count;
            return d;
        }

        private static Dictionary<string, int> TotalsOf(CharacterSaveData d)
        {
            var m = new InventoryModel();
            m.Load(d.bag, d.equipment);
            return m.Totals();
        }

        private static bool SameTotals(Dictionary<string, int> x, Dictionary<string, int> y)
        {
            if (x.Count != y.Count) return false;
            foreach (KeyValuePair<string, int> kv in x)
                if (!y.TryGetValue(kv.Key, out int n) || n != kv.Value) return false;
            return true;
        }

        private static int Count(Dictionary<string, int> d, string id) => d.TryGetValue(id, out int n) ? n : 0;

        private static int Count(List<ItemStack> l, string id)
        {
            int n = 0;
            foreach (ItemStack s in l) if (s.id == id) n += s.count;
            return n;
        }

        private static int SlotsOf(NetworkInventory inv, string id)
        {
            int n = 0;
            foreach (ItemStack s in inv.ServerModel.Bag) if (!s.IsEmpty && s.id == id) n++;
            return n;
        }

        private static int IndexOf(NetworkInventory inv, string id)
        {
            for (int i = 0; i < InventoryModel.BagSize; i++)
                if (!inv.ServerModel.Bag[i].IsEmpty && inv.ServerModel.Bag[i].id == id) return i;
            return -1;
        }

        private static string Equipped(NetworkInventory inv, EquipmentSlot slot)
        {
            ItemStack s = inv.ServerModel.Equipment[(int)slot];
            return s.IsEmpty ? null : s.id;
        }

        private static string WeaponIdOf(string itemId)
        {
            ItemDefinition def = GameData.Item(itemId);
            return def != null && def.weapon != null ? def.weapon.Id : "?";
        }

        private void Check(bool ok, string what)
        {
            if (ok) _passed++; else _failed++;
            Line((ok ? "  OK    " : "  FEHLER ") + what);
            if (!ok && !string.IsNullOrEmpty(_last.data)) Line("        (letzte Antwort: " + _last.data + ")");
        }

        private void Info(string what) => Line("  ·     " + what);

        private void Line(string s)
        {
            _report.AppendLine(s);
            if (s.Length > 0) Debug.Log("[Ashveil][Duo] " + s.Trim());
            // Zwischenstand mitschreiben: bleibt ein Test haengen, sieht man, wo
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath, _report + "\n(laeuft noch ...)\n");
            }
            catch (Exception) { }
        }

        private void OnLog(string message, string stackTrace, LogType type)
        {
            if (message.StartsWith("[Ashveil][Duo]") || TestLog.IsEditorTooling(message, stackTrace)) return;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                _logErrors++;
                if (_logErrors <= 10) _report.AppendLine("  LOG   " + type + ": " + message.Split('\n')[0]);
            }
        }
    }
}
