using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Quests;
using UnityEngine;
using UnityEngine.AI;
using KC = Ashveil.Data.KarethCatalog;

namespace Ashveil.Networking
{
    /// <summary>
    /// #28 Kareth v4, Welle 0 (1 Spieler, in der Szene Kareth v4): die vier Greybox-Tiere und die elf Ergänzungsaufträge.
    /// Geprüft: Inhalte (Aufträge nur mit Szenenkennzeichen, EP-Summe, Stufen, Ketten, unmarkiertes U-7, Figuren, Erkundungsziele,
    /// Spawngebiete, Auftragsbeute), Tiere (neutral bleibt ruhig und gelb, wehrt sich, Gebiet macht feindlich; Schlickwurf auf Abstand;
    /// Schriller Ausstoß als Flächenangriff; Werte der Angriffe), danach alle elf Aufträge mit Gesprächen, Siegen, Auftragsbeute nur
    /// mit Auftrag, Erkunden, Belohnungen je Klasse und dem Gruß nach Marke (Ebba, Oswald).
    /// Test-Hilfen wie in der Glockenrast-Suite: Spielfigur unverwundbar, Gegner neben der Figur erzeugt und mit einem Treffer
    /// besiegt (der Kampf selbst steht in den Tier-Prüfungen), fehlende Stufen als „Nachhilfe“ ausgewiesen.
    /// Start: Auftrag „spieltest kareth28“. Bericht: Logs/…/Ashveil_Kareth28Test.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string K28Flag = "kareth28";
        private static bool _k28Mode;
        private const string K28Profile = "duo-k28";

        private IEnumerator Kareth28Suite()
        {
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(K28Profile)) != null, 40f);
            Check(a != null, "Spieler angemeldet (neue Figur)");
            if (a == null) yield break;
            var pa = a.GetComponent<NetworkProgression>();
            if (pa == null) { Check(false, "Spieler hat Fortschritt"); yield break; }
            yield return new WaitForSecondsRealtime(3f);
            Health hpA = a.GetComponent<Health>();
            hpA?.SetInvulnerableFor(7200f);
            _grQuestXp = _grTopUp = _grKillXp = _grDiscoveryXp = 0;

            // ---------------------------------------------------------- Inhalte
            Line("");
            Line("[Inhalte]");
            List<QuestDefinition> defs = KC.Quests.Select(GameData.Quest).ToList();
            Check(defs.All(d => d != null), $"Alle elf Aufträge vorhanden ({defs.Count(d => d != null)}/11)");
            if (defs.Any(d => d == null)) yield break;
            Check(defs.Sum(d => d.xp) == KC.QuestXpTotal, $"EP-Summe = {defs.Sum(d => d.xp)} (Auftragsliste: {KC.QuestXpTotal})");
            int[] minLv = { 1, 2, 2, 4, 5, 5, 6, 7, 8, 9, 7 };
            Check(defs.Select(d => d.minLevel).SequenceEqual(minLv), "Stufen wie in der Auftragsliste (K-A 1 … U-6 9, U-7 7)");
            Check(defs.All(d => d.requiredSceneTag == KC.SceneTag) && SceneQuestFilter.HasTag(KC.SceneTag),
                "Alle elf gelten nur in Kareth v4 – und diese Szene trägt das Kennzeichen");
            Check(GrPrereqs(KC.KA, "quest.blade_for_the_watch") && GrPrereqs(KC.KB, KC.KA) && GrPrereqs(KC.KC, "quest.blade_for_the_watch") &&
                  GrPrereqs(KC.KD, "quest.blade_for_the_watch") && GrPrereqs(KC.U1, KC.KD) && GrPrereqs(KC.U2) && GrPrereqs(KC.U3, "quest.view_over_the_hollow") &&
                  GrPrereqs(KC.U4) && GrPrereqs(KC.U5, KC.U4) && GrPrereqs(KC.U6) && GrPrereqs(KC.U7),
                "Ketten: K-A → K-B, K-D → U-1, Q3 → U-3, U-4 → U-5; Q1 vor K-A, K-C, K-D");
            Check(GameData.Quest(KC.U7).unmarked && defs.Count(d => d.unmarked) == 1, "U-7 ist unmarkiert (kein „!“), die anderen zehn nicht");
            string[] npcs = { KC.Wenna, KC.Jorn, KC.Ulf, KC.Oswald, KC.Ebba, KC.Tamo, KC.Cartographer, KC.Veit, KC.Bralt, KC.Pilgrim };
            string missingNpc = string.Join(", ", npcs.Where(n => GameData.Npc(n) == null || NpcIdentity.Find(n) == null));
            Check(missingNpc.Length == 0, "Figuren in Daten und Szene (Wenna, Jorn, Ulf, Oswald, Ebba, Tamo, Kartograf, Veit, Bralt, Edo)" +
                                         (missingNpc.Length > 0 ? " – fehlt: " + missingNpc : ""));
            string[] areas = KC.Areas();
            Check(areas.All(x => QuestArea.Find(x) != null), $"Erkundungsziele in der Szene ({areas.Count(x => QuestArea.Find(x) != null)}/{areas.Length})");
            string[] foes = { KC.EnemyRooter, KC.EnemyClacker, KC.EnemySlinger, KC.EnemyBellows, KC.EnemyWreckSailor, KC.EnemyReedDeserter, KC.EnemyDeserter, KC.EnemyDeserterArcher };
            List<EnemySpawnArea> spawns = FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None).Where(s => s.Definition != null).ToList();
            string missingFoe = string.Join(", ", foes.Where(f => !spawns.Any(s => s.Definition.Id == f && s.Prefab != null)));
            Check(missingFoe.Length == 0, "Spawngebiete der Aufträge in der Szene" + (missingFoe.Length > 0 ? " – fehlt: " + missingFoe : ""));
            foreach ((string item, string quest, string loot) in new[]
                     {
                         (KC.ItemJug, KC.U2, "loot.silt_slinger"), (KC.ItemLogPage, KC.U3, "loot.wreck_sailor"), (KC.ItemGifts, KC.U7, "loot.reed_deserter")
                     })
            {
                ItemDefinition d = GameData.Item(item);
                LootTableDefinition t = GameData.Get<LootTableDefinition>(loot);
                Check(d != null && d.category == ItemCategory.Quest && d.questId == quest && t != null && t.entries.Any(e => e.item == d),
                    $"{d?.DisplayName ?? item}: Auftragsgegenstand von {quest} und Beute ({loot})");
            }

            // ---------------------------------------------------------- Tiere
            Line("");
            Line("[Tiere]");
            yield return K28Animals(a);

            // ---------------------------------------------------------- Q1 und Q3 (Voraussetzungen)
            Line("");
            Line("[Voraussetzungen Q1 und Q3]");
            yield return GrAccept(a, pa, "quest.blade_for_the_watch");
            yield return GrTurnIn(a, pa, "quest.blade_for_the_watch");
            yield return GrAccept(a, pa, "quest.view_over_the_hollow");
            yield return GrExplore(a, pa, "quest.view_over_the_hollow", 0, "area.lookout");
            yield return GrTurnIn(a, pa, "quest.view_over_the_hollow");

            // ---------------------------------------------------------- K-A Die Vorratsgruben
            Line("");
            Line("[K-A Die Vorratsgruben]");
            Check(Marker(KC.Wenna) == QuestMarkerState.Available, "Wenna zeigt gelbes „!“");
            yield return GrAccept(a, pa, KC.KA);
            yield return Ask(a, "quest-accept", KC.KB);
            Check(!_last.ok && !pa.ServerIsActive(KC.KB), "K-B vor Abschluss von K-A abgelehnt (Kette)");
            yield return GrGotoSite(a, pa, "site.kareth.vorratswiesen");
            yield return GrKill(a, pa, KC.EnemyRooter, 2, 6, null);
            Check(pa.ServerProgress(KC.KA, 0) == 6, $"Sechs Furchenwühler erlegt ({pa.ServerProgress(KC.KA, 0)}/6)");
            yield return GrTurnIn(a, pa, KC.KA);
            K28ClassReward(a, "Gürtel", "item.k28_studded_girdle", "item.k28_knife_loop_belt", "item.k28_braided_cord");

            // ---------------------------------------------------------- K-B Die Wasserstelle
            Line("");
            Line("[K-B Die Wasserstelle]");
            yield return GrAccept(a, pa, KC.KB);
            yield return GrGotoSite(a, pa, "site.kareth.kueste");
            yield return GrKill(a, pa, KC.EnemyClacker, 3, 8, null);
            Check(pa.ServerProgress(KC.KB, 0) == 8, $"Acht Uferklapper erlegt ({pa.ServerProgress(KC.KB, 0)}/8)");
            int potions = a.ServerModel.Count("item.healing_draught");
            yield return GrTurnIn(a, pa, KC.KB);
            Check(a.ServerModel.Count("item.healing_draught") == potions + 2, "Belohnung: zwei Heiltränke");

            // ---------------------------------------------------------- K-C Der Waldrand
            Line("");
            Line("[K-C Der Waldrand]");
            yield return GrAccept(a, pa, KC.KC);
            yield return GrGotoSite(a, pa, "site.kareth.waldrand.westtor");
            yield return GrKill(a, pa, KC.EnemyBellows, 3, 6, null);
            Check(pa.ServerProgress(KC.KC, 0) == 6, $"Sechs Flüsterbälge erlegt ({pa.ServerProgress(KC.KC, 0)}/6)");
            yield return GrTurnIn(a, pa, KC.KC);

            // ---------------------------------------------------------- K-D Die ausgebliebene Holzlieferung
            Line("");
            Line("[K-D Die ausgebliebene Holzlieferung]");
            yield return GrAccept(a, pa, KC.KD);
            yield return GrExplore(a, pa, KC.KD, 0, KC.AreaWoodYard);
            yield return GrKill(a, pa, KC.EnemyWolf, 4, 6, null);
            Check(pa.ServerProgress(KC.KD, 1) == 6, $"Sechs Aschwölfe erlegt ({pa.ServerProgress(KC.KD, 1)}/6)");
            yield return GrTurnIn(a, pa, KC.KD);

            // ---------------------------------------------------------- U-1 Tamos Wagen
            Line("");
            Line("[U-1 Tamos Wagen]");
            yield return GrEnsureLevel(a, pa, 5);
            yield return GrAccept(a, pa, KC.U1);
            yield return GrGotoSite(a, pa, "site.kareth.balgnest");
            yield return GrKill(a, pa, KC.EnemyBellows, 5, 6, null);
            yield return GrExplore(a, pa, KC.U1, 1, KC.AreaWagon);
            yield return GrTurnIn(a, pa, KC.U1);
            K28ClassReward(a, "Brustteil", "item.k28_wagon_plate_cuirass", "item.k28_tarp_leather_jerkin", "item.k28_wagon_canvas_smock");

            // ---------------------------------------------------------- U-2 Ein Krug für die Schale
            Line("");
            Line("[U-2 Ein Krug für die Schale]");
            yield return GrGotoSite(a, pa, "site.kareth.salzufer.damm");
            int leaks = 0;
            yield return GrKill(a, pa, KC.EnemySlinger, 5, 2, corpse => { if (corpse != null && corpse.Get(a.ProfileId).Any(s => s.id == KC.ItemJug)) leaks++; });
            Check(leaks == 0, "Ohne den Auftrag fällt kein Krug (Auftragsbeute)");
            yield return GrAccept(a, pa, KC.U2);
            // Seit 10.10. vergibt Oswald den Nebenauftrag N09 „Schlick am Damm“ – sein „!“ ist richtig. Für U-2 bleibt er Gesprächsziel (Prüfung unten).
            yield return GotoNpc(a, KC.Oswald);
            yield return Ask(a, "npc-talk", KC.Oswald);
            Check(pa.ServerProgress(KC.U2, 0) == 1, "Mit Oswald am Dammbruch gesprochen");
            yield return GrGotoSite(a, pa, "site.kareth.salzufer.damm");
            int slingers = 0;
            while ((slingers < 6 || a.ServerModel.Count(KC.ItemJug) < 1) && slingers < 30)
            {
                slingers++;
                yield return GrKill(a, pa, KC.EnemySlinger, 5, 1, null, KC.ItemJug);
            }
            Check(pa.ServerProgress(KC.U2, 1) >= 6 && a.ServerModel.Count(KC.ItemJug) == 1, $"Sechs Schlickwerfer erlegt, Ebbas Krug geborgen ({slingers} Schlickwerfer)");
            int extra = 0;
            yield return GrKill(a, pa, KC.EnemySlinger, 5, 1, corpse => { if (corpse != null && corpse.Get(a.ProfileId).Any(s => s.id == KC.ItemJug)) extra++; });
            Check(extra == 0, "Mit dem Krug im Gepäck fällt kein zweiter");
            int tea = a.ServerModel.Count("item.ash_tea");
            yield return GrTurnIn(a, pa, KC.U2);
            QuestGate gate = pa.ServerGate();
            Check(a.ServerModel.Count("item.ash_tea") == tea + 2 && a.ServerModel.Count(KC.ItemJug) == 0, "Getränk aufs Haus (zwei Aschentee), der Krug ist abgegeben");
            Check(gate != null && gate.HasFlag(KC.FlagJugReturned), "Marke „Krug zurück“ gesetzt");
            string greet = GameData.Npc(KC.Oswald)?.GreetingFor(f => gate != null && gate.HasFlag(f)) ?? "";
            string greetE = GameData.Npc(KC.Ebba)?.GreetingFor(f => gate != null && gate.HasFlag(f)) ?? "";
            Check(greet.Contains("Krug wieder") && greetE.Contains("wieder da"), "Gruß nach Marke: Oswald und Ebba sprechen anders");

            // ---------------------------------------------------------- U-3 Das Wrack im Salz
            Line("");
            Line("[U-3 Das Wrack im Salz]");
            yield return GrEnsureLevel(a, pa, 6);
            yield return GrAccept(a, pa, KC.U3);
            yield return GrExplore(a, pa, KC.U3, 0, KC.AreaWreck);
            int sailors = 0;
            while ((sailors < 6 || a.ServerModel.Count(KC.ItemLogPage) < 1) && sailors < 30)
            {
                sailors++;
                yield return GrKill(a, pa, KC.EnemyWreckSailor, 6, 1, null, KC.ItemLogPage);
            }
            Check(pa.ServerProgress(KC.U3, 1) >= 6 && a.ServerModel.Count(KC.ItemLogPage) == 1, $"Sechs Ertrunkene Matrosen, eine Logbuchseite ({sailors} Matrosen)");
            yield return GrTurnIn(a, pa, KC.U3);

            // ---------------------------------------------------------- U-4 Arbeitszeichen
            Line("");
            Line("[U-4 Arbeitszeichen]");
            yield return GrEnsureLevel(a, pa, 7);
            yield return GrAccept(a, pa, KC.U4);
            yield return Ask(a, "quest-accept", KC.U5);
            Check(!_last.ok && !pa.ServerIsActive(KC.U5), "U-5 vor Abschluss von U-4 abgelehnt (Kette)");
            yield return GrGotoSite(a, pa, "site.kareth.werkstaetten");
            yield return GrKill(a, pa, KC.EnemyFaded, 7, 8, null);
            Check(pa.ServerProgress(KC.U4, 0) == 8, $"Acht Verblichene besiegt ({pa.ServerProgress(KC.U4, 0)}/8)");
            yield return GrTurnIn(a, pa, KC.U4);

            // ---------------------------------------------------------- U-5 Die Zeichensteine
            Line("");
            Line("[U-5 Die Zeichensteine]");
            yield return GrEnsureLevel(a, pa, 8);
            yield return GrAccept(a, pa, KC.U5);
            for (int i = 0; i < KC.SignStoneCount; i++) yield return GrExplore(a, pa, KC.U5, i, KC.AreaSignStone(i));
            yield return GrTurnIn(a, pa, KC.U5);
            Check(a.ServerModel.Count("item.k28_sign_stone_pendant") == 1, "Halsschmuck für alle: Anhänger aus Zeichenstein");

            // ---------------------------------------------------------- U-6 Die leeren Höfe
            Line("");
            Line("[U-6 Die leeren Höfe]");
            yield return GrEnsureLevel(a, pa, 9);
            yield return GrAccept(a, pa, KC.U6);
            yield return GrGotoSite(a, pa, "site.kareth.wuestung");
            yield return GrKill(a, pa, KC.EnemyDeserter, 9, 5, null);
            yield return GrKill(a, pa, KC.EnemyDeserterArcher, 9, 3, null);
            Check(pa.ServerProgress(KC.U6, 0) == 5 && pa.ServerProgress(KC.U6, 1) == 3, "Fünf Deserteure und drei Deserteurinnen besiegt");
            yield return GrExplore(a, pa, KC.U6, 2, KC.AreaTrail);
            yield return GrTurnIn(a, pa, KC.U6);
            K28ClassReward(a, "Beinteil", "item.k28_watch_greaves", "item.k28_scout_breeches", "item.k28_loden_trousers");

            // ---------------------------------------------------------- U-7 Die verschwundenen Gaben (unmarkiert)
            Line("");
            Line("[U-7 Die verschwundenen Gaben]");
            QuestDefinition u7 = GameData.Quest(KC.U7);
            bool offered = QuestRules.Offered(u7, KC.Pilgrim, a.ServerModel.Level, pa.ServerIsCompleted, pa.ServerIsActive, pa.ServerGate());
            Check(offered && Marker(KC.Pilgrim) != QuestMarkerState.Available, "Edo bietet U-7 an, zeigt aber kein „!“ (unmarkiert)");
            yield return GrAccept(a, pa, KC.U7);
            yield return GrExplore(a, pa, KC.U7, 0, KC.AreaRestingPlace);
            yield return GrExplore(a, pa, KC.U7, 1, KC.AreaHideout);
            int reed = 0;
            while (a.ServerModel.Count(KC.ItemGifts) < 1 && reed < 5)
            {
                reed++;
                yield return GrKill(a, pa, KC.EnemyReedDeserter, 7, 1, null, KC.ItemGifts);
            }
            Check(a.ServerModel.Count(KC.ItemGifts) == 1, $"Pilgergaben beim hungrigen Deserteur zurückgeholt ({reed} Sieg)");
            yield return GrTurnIn(a, pa, KC.U7);
            Check(a.ServerModel.Count("item.k28_resting_pebble") == 1, "Kleiner Gegenstand von Edo: Kiesel vom Rastplatz");

            // ---------------------------------------------------------- Classic-Paket (Max 11.10.2026 abends)
            Line("");
            Line("[Classic-Paket: Botengänge, seltene Tiere, „Kopf von …“]");
            EnemySpawnArea[] rareAreas = FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None)
                .Where(x => x.Definition != null && KC.Rares.Contains(x.Definition.Id)).ToArray();
            Check(rareAreas.Length == 6 && rareAreas.All(x => x.Count == 1 && x.HideoutCount == 3 && x.Prefab != null),
                $"Sechs seltene Tiere in der Szene, je eins mit drei Verstecken ({rareAreas.Length}/6)");
            Check(rareAreas.All(x => x.Definition.respawnTime >= 1200f), "Seltene Tiere kehren erst nach 20–45 min wieder");
            yield return GrAccept(a, pa, KC.B5);
            yield return GrTurnIn(a, pa, KC.B5);
            yield return GrAccept(a, pa, KC.B1);
            yield return GrTurnIn(a, pa, KC.B1);
            yield return GrAccept(a, pa, KC.B2);
            yield return GrTurnIn(a, pa, KC.B2);
            yield return GrAccept(a, pa, KC.H1);
            yield return GrKill(a, pa, KC.RareAltborste, 4, 1, null, KC.ItemAltborsteTusk);
            Check(a.ServerModel.Count(KC.ItemAltborsteTusk) == 1, "Altborste erlegt, Hauer geborgen");
            yield return GrTurnIn(a, pa, KC.H1);
            Check(new[] { KC.B5, KC.B1, KC.B2, KC.H1 }.All(pa.ServerIsCompleted), "Botengänge B5, B1, B2 und „Die Altborste“ abgeschlossen");

            // ---------------------------------------------------------- Bilanz
            Line("");
            Line("[Bilanz]");
            Check(KC.Quests.All(pa.ServerIsCompleted), $"Alle elf Ergänzungsaufträge abgeschlossen ({KC.Quests.Count(pa.ServerIsCompleted)}/11)");
            Info($"Auftrags-EP gesamt (mit Q1/Q3): {_grQuestXp}, Siege: {_grKillXp}, Entdeckungen: {_grDiscoveryXp}, Nachhilfe: {_grTopUp} – Stufe {a.ServerModel.Level}");
        }

        /// <summary>Feste Belohnung nach Klasse: genau das Teil der eigenen Klasse, keins der anderen.</summary>
        private void K28ClassReward(NetworkInventory a, string what, string warden, string stalker, string weaver)
        {
            string id = a.ServerModel.Class?.Id;
            string mine = id == "class.ashstalker" ? stalker : id == "class.emberweaver" ? weaver : warden;
            int others = new[] { warden, stalker, weaver }.Where(x => x != mine).Sum(x => a.ServerModel.Count(x));
            Check(a.ServerModel.Count(mine) == 1 && others == 0, $"{what} nach Klasse: {GameData.Item(mine)?.DisplayName} ({a.ServerModel.Class?.DisplayName}), keins der anderen Klassen");
        }

        /// <summary>Gegner-Vorlage aus den Spawngebieten der Szene (Kareth v4).</summary>
        private static EnemySpawnArea K28Template(string enemyId) =>
            FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None).FirstOrDefault(x => x.Definition != null && x.Definition.Id == enemyId && x.Prefab != null);

        private static Vector3 K28Near(Vector3 from, Vector3 dir, float dist)
        {
            Vector3 p = from + dir * dist;
            return NavMesh.SamplePosition(p, out NavMeshHit hit, 4f, NavMesh.AllAreas) ? hit.position : p;
        }

        /// <summary>
        /// Tiere im echten Kampf (Figur unverwundbar): neutral ruhig und gelb, wehrt sich; Gebiet macht feindlich; Schlickwurf auf Abstand
        /// (die Figur hält 10 m Abstand); Schriller Ausstoß des Flüsterbalgs (Fläche, Warnring); Angriffswerte aus den Bausteinen.
        /// </summary>
        private IEnumerator K28Animals(NetworkInventory a)
        {
            if (EnemyDirector.Instance == null) { Check(false, "Gegner-Steuerung läuft"); yield break; }
            // Ruhige Stelle: Vorratswiesen (offen, eben)
            Ashveil.World.WorldQuestSite meadow = Ashveil.World.WorldQuestSite.Find("site.kareth.vorratswiesen");
            Vector3 stand = meadow != null ? meadow.transform.position : a.transform.position;
            if (NavMesh.SamplePosition(stand + new Vector3(0f, 0f, 30f), out NavMeshHit sh, 8f, NavMesh.AllAreas)) stand = sh.position;
            yield return Ask(a, "goto", V(stand + Vector3.up * 0.3f, 0f));
            yield return new WaitForSecondsRealtime(1f);
            Health player = a.GetComponent<Health>();

            // Werte (Bausteine 28/29/32): nur mehr Schaden, keine Kontrolle
            EnemyDefinition rooterDef = GameData.Get<EnemyDefinition>(KC.EnemyRooter), clackerDef = GameData.Get<EnemyDefinition>(KC.EnemyClacker),
                slingerDef = GameData.Get<EnemyDefinition>(KC.EnemySlinger), bellowsDef = GameData.Get<EnemyDefinition>(KC.EnemyBellows);
            Check(rooterDef != null && rooterDef.neutral && rooterDef.Move(2) != null && rooterDef.Move(2).kind == EnemyMoveKind.Charge &&
                  rooterDef.Move(2).damageMultiplier > 1f && rooterDef.Move(2).knockback == 0f, "Furchenwühler: neutral, Schulterrammer = Ansturm mit mehr Schaden, ohne Rückstoß");
            Check(clackerDef != null && clackerDef.neutral && clackerDef.Move(2) != null && clackerDef.Move(2).pose == EnemyMovePose.BothArms &&
                  clackerDef.Move(2).windup >= 1f && clackerDef.Move(2).knockback == 0f, "Uferklapper: neutral, Klappenschlag mit langer Vorbereitung, nur Schaden");
            Check(slingerDef != null && !slingerDef.neutral && slingerDef.Throws && slingerDef.throwRange <= 16f && slingerDef.aggroProfile == AggroProfile.Territorial,
                "Schlickwerfer: territorial, Schlickwurf mit begrenzter Reichweite");
            Check(bellowsDef != null && bellowsDef.neutral && bellowsDef.Move(3) != null && bellowsDef.Move(3).kind == EnemyMoveKind.Area && bellowsDef.Move(3).windup >= 1f,
                "Flüsterbalg: neutral, Schriller Ausstoß als angekündigter Flächenangriff");

            // 1. Neutral: bleibt ruhig, gelbes Namensschild, wehrt sich
            EnemySpawnArea rooterArea = K28Template(KC.EnemyRooter);
            EnemyBrain rooter = rooterArea != null ? EnemyDirector.Instance.SpawnAt(rooterArea.Prefab, K28Near(stand, Vector3.forward, 2.5f), 180f, 2, "k28-test", null, 5f) : null;
            if (rooter == null) { Check(false, "Furchenwühler erzeugt"); yield break; }
            yield return new WaitForSecondsRealtime(4f);
            var ct = rooter.GetComponent<CombatTarget>();
            Check(rooter.Faction == Faction.Neutral && ct != null && ct.Faction == Faction.Neutral, "Furchenwühler ist neutral (gelbes Namensschild)");
            Check(!rooter.InCombat, $"Steht 2,5 m daneben – greift nicht an ({rooter.State})");
            rooter.GetComponent<Health>()?.ApplyDamage(new DamageInfo { Amount = 1f, Source = a.gameObject });
            yield return WaitUntil(() => rooter.InCombat, 3f);
            Check(rooter.InCombat && rooter.Target == player, "Nach einem Treffer wehrt er sich (Kampf gegen die Figur)");
            EnemyDirector.Instance.Remove(rooter);

            // 2. Gebiet macht feindlich: Flüsterbalg am Nest (territorial) rot, am Waldrand (passiv) gelb
            EnemySpawnArea bellowsArea = K28Template(KC.EnemyBellows);
            EnemyBrain calm = bellowsArea != null ? EnemyDirector.Instance.SpawnAt(bellowsArea.Prefab, K28Near(stand, Vector3.right, 12f), 0f, 3, "k28-test", null, 5f) : null;
            EnemyBrain nest = bellowsArea != null ? EnemyDirector.Instance.SpawnAt(bellowsArea.Prefab, K28Near(stand, Vector3.left, 12f), 0f, 5, "k28-test",
                br => br.SetAggroProfile(AggroProfile.Territorial), 5f) : null;
            yield return new WaitForSecondsRealtime(0.5f);
            Check(calm != null && nest != null && calm.Faction == Faction.Neutral && nest.Faction == Faction.Hostile,
                "Gleiches Tier, anderes Gebiet: passiv = neutral (gelb), territorial am Nest = feindlich (rot)");
            if (calm != null) EnemyDirector.Instance.Remove(calm);
            if (nest != null) EnemyDirector.Instance.Remove(nest);

            // 3. Schlickwurf: Figur hält 10 m Abstand, der Schlickwerfer wirft
            EnemySpawnArea slingerArea = K28Template(KC.EnemySlinger);
            EnemyBrain slinger = slingerArea != null ? EnemyDirector.Instance.SpawnAt(slingerArea.Prefab, K28Near(stand, Vector3.forward, 10f), 180f, 5, "k28-test", null, 5f) : null;
            if (slinger == null) { Check(false, "Schlickwerfer erzeugt"); yield break; }
            int rangedBegan = 0, resolved = 0;
            void OnBegan(Vector3 p, float w, bool ranged) { if (ranged) rangedBegan++; }
            void OnResolved(bool hit, Vector3 p) { if (rangedBegan > 0) resolved++; }
            slinger.AttackBegan += OnBegan;
            slinger.AttackResolved += OnResolved;
            yield return new WaitForSecondsRealtime(0.5f);
            slinger.GetComponent<Health>()?.ApplyDamage(new DamageInfo { Amount = 1f, Source = a.gameObject });
            float t = 0f;
            while (t < 16f && (slinger.ThrowsStarted == 0 || resolved == 0))
            {
                Vector3 away = a.transform.position - slinger.transform.position;
                away.y = 0f;
                if (away.sqrMagnitude < 1e-3f) away = Vector3.forward;
                if (away.magnitude < 8f) yield return Ask(a, "goto", V(K28Near(slinger.transform.position, away.normalized, 10f) + Vector3.up * 0.3f, 0f));
                yield return new WaitForSecondsRealtime(0.5f);
                t += 0.6f;
            }
            slinger.AttackBegan -= OnBegan;
            slinger.AttackResolved -= OnResolved;
            Check(slinger.ThrowsStarted > 0 && rangedBegan > 0 && resolved > 0, $"Schlickwurf auf Abstand: {slinger.ThrowsStarted} Würfe angekündigt, {resolved} aufgelöst");
            EnemyDirector.Instance.Remove(slinger);

            // 4. Schriller Ausstoß: Flüsterbalg greift aus nächster Nähe an, irgendwann mit dem Flächenangriff
            EnemyBrain shrill = bellowsArea != null ? EnemyDirector.Instance.SpawnAt(bellowsArea.Prefab, K28Near(a.transform.position, Vector3.forward, 2f), 180f, 5, "k28-test",
                br => br.SetAggroProfile(AggroProfile.Aggressive), 5f) : null;
            if (shrill == null) { Check(false, "Flüsterbalg erzeugt"); yield break; }
            int area = 0;
            void OnArea(Vector3 c, float w, float r) => area++;
            shrill.AreaAttackBegan += OnArea;
            yield return WaitUntil(() => area > 0, 45f);
            shrill.AreaAttackBegan -= OnArea;
            Check(area > 0 && shrill.MoveCounts.TryGetValue(3, out int n3) && n3 > 0, $"Schriller Ausstoß (Fläche mit Warnring) im Kampf ({string.Join(",", shrill.MoveHistory)})");
            EnemyDirector.Instance.Remove(shrill);
            yield return new WaitForSecondsRealtime(0.5f);
        }
    }
}
