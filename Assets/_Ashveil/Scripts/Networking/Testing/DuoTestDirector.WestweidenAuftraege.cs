using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ashveil.Core;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using FishNet.Object;
using Ashveil.Gameplay.Quests;
using Ashveil.Gameplay.Story;
using UnityEngine;
using UnityEngine.AI;
using WC = Ashveil.Data.WestweidenCatalog;

namespace Ashveil.Networking
{
    /// <summary>
    /// Westweiden-Aufträge (Lückenliste Punkt 2, 12.10.2026) – Teil der Prüfung „spieltest weidepass“, nachdem die Zelle in Langer Rain
    /// geladen ist:
    ///   1. Daten: 26 Aufträge (Kennzeichen „westweiden“, EP-Summe), 18 Figuren in Daten und Zelle (auf dem NavMesh), alle Fundstellen,
    ///      Auftragsbeute in den Beutetabellen, Spawngebiete der Ziel-Gegner.
    ///   2. Durchspielen über den echten Client-Weg: H1/H2 (W01–W05: Bote, Zaunpfähle, Otterfelle, Hufeisen + Zaumzeug),
    ///      K1/K2 (W06–W11: Botengänge, Kornsäcke), N1 W14 (Zulauf = Geschichtsschritt), W12 (Schlammgrundler), K3 (W19–W21: Zollbuch).
    ///   3. Kreidekeller (Punkt 5): Höhlenvolumen, Wegnetz vom Felsband bis zum Kaminsteig, Stehen in der Höhle (keine Sturzgrenze),
    ///      Alarmglocke, Kellermeister-Begegnung (ruft bei 50 % die Schützen), W23 (Hofmarken + Liste aus seiner Beute), W24 (Habbo).
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        private IEnumerator WwAuftraegeSuite(NetworkInventory a)
        {
            var pa = a.GetComponent<NetworkProgression>();
            if (pa == null) { Check(false, "Spieler hat Fortschritt"); yield break; }
            _grQuestXp = _grTopUp = _grKillXp = _grDiscoveryXp = 0;

            // ---------------------------------------------------------- Daten
            Line("");
            Line("[Westweiden-Aufträge: Daten]");
            var defs = WC.Quests.Select(GameData.Quest).ToList();
            Check(defs.All(d => d != null), $"Alle {WC.Quests.Length} Aufträge vorhanden ({defs.Count(d => d != null)})");
            if (defs.Any(d => d == null)) yield break;
            Check(defs.Sum(d => d.xp) == WC.QuestXpTotal, $"EP-Summe {defs.Sum(d => d.xp)} (Soll {WC.QuestXpTotal})");
            Check(defs.All(d => d.requiredSceneTag == WC.SceneTag) && SceneQuestFilter.HasTag(WC.SceneTag),
                "Alle gelten nur mit dem Kennzeichen „westweiden“ – und die Welt trägt es");
            Check(GrPrereqs(WC.W02, WC.W01) && GrPrereqs(WC.W05, WC.W04) && GrPrereqs(WC.W06, WC.W05) && GrPrereqs(WC.W09, WC.W08) &&
                  GrPrereqs(WC.W14, WC.W11) && GrPrereqs(WC.W19, WC.W11) && GrPrereqs(WC.W21, WC.W20) && GrPrereqs(WC.W28, WC.W27) && GrPrereqs(WC.W12),
                "Ketten: H1 → H2 → K1 → K2 → N1/K3, P (W26 → W28), W12 einzeln");
            string fehltNpc = string.Join(", ", WC.Npcs.Where(n => GameData.Npc(n) == null || NpcIdentity.Find(n) == null));
            Check(fehltNpc.Length == 0, $"{WC.Npcs.Length} Figuren in Daten und Zelle" + (fehltNpc.Length > 0 ? " – fehlt: " + fehltNpc : ""));
            string abseits = string.Join(", ", WC.Npcs.Select(NpcIdentity.Find).Where(n => n != null &&
                !NavMesh.SamplePosition(n.transform.position, out NavMeshHit _, 2.5f, NavMesh.AllAreas)).Select(n => n.name));
            Check(abseits.Length == 0, "Alle Figuren stehen erreichbar (NavMesh ≤ 2,5 m)" + (abseits.Length > 0 ? " – abseits: " + abseits : ""));
            int anker = WC.Nodes.Count(n => StoryAnchor.Find(n.Action) != null);
            Check(anker == WC.Nodes.Count, $"Fundstellen in der Zelle ({anker}/{WC.Nodes.Count})");
            foreach ((string item, string quest, string loot) in new[]
                     {
                         (WC.ItemOtterPelt, WC.W04, "loot.ww_bruchotter"), (WC.ItemRotkehlHead, WC.W18, "loot.ww_rotkehl"),
                         (WC.ItemSnakeSkin, WC.W27, "loot.ww_kammnatter"), (WC.ItemHedgeKingSkin, WC.W28, "loot.ww_heckenkoenig")
                     })
            {
                ItemDefinition d = GameData.Item(item);
                LootTableDefinition t = GameData.Get<LootTableDefinition>(loot);
                Check(d != null && d.category == ItemCategory.Quest && d.questId == quest && t != null && t.entries.Any(e => e.item == d),
                    $"{d?.DisplayName ?? item}: Auftragsgegenstand von {quest} und Beute ({loot})");
            }
            string[] foes = { WC.EnemyOtter, WC.EnemyWeasel, WC.EnemySnake, WC.EnemyArcher, WC.EnemyRotkehl, WC.EnemyHedgeKing };
            var spawns = FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None).Where(s => s.Definition != null).ToList();
            string fehltFoe = string.Join(", ", foes.Where(f => !spawns.Any(s => s.Definition.Id == f && s.Prefab != null)));
            Check(fehltFoe.Length == 0, "Spawngebiete der Ziel-Gegner in der Zelle" + (fehltFoe.Length > 0 ? " – fehlt: " + fehltFoe : ""));

            // ---------------------------------------------------------- H1/H2 Hallgrund
            Line("");
            Line("[H1/H2 Hallgrund: W01–W05]");
            yield return GrEnsureLevel(a, pa, 10);
            yield return GrAccept(a, pa, WC.W01);
            yield return GrTurnIn(a, pa, WC.W01);
            yield return GrAccept(a, pa, WC.W02);
            for (int i = 0; i < 8; i++) yield return GrTake(a, pa, WC.NodePost(i), true);
            Check(pa.ServerProgress(WC.W02, 0) == 8, $"Acht Zaunpfähle ({pa.ServerProgress(WC.W02, 0)}/8)");
            yield return GrTake(a, pa, WC.NodePost(0), false);
            Check(pa.ServerStoryNote.EndsWith("schon genommen"), "Ein genommener Pfahl lässt sich nicht zweimal nehmen");
            yield return GrTurnIn(a, pa, WC.W02);
            yield return GrAccept(a, pa, WC.W03);
            yield return GrTurnIn(a, pa, WC.W03);
            yield return GrAccept(a, pa, WC.W04);
            int otter = 0;
            while (a.ServerModel.Count(WC.ItemOtterPelt) < 8 && otter < 40)
            {
                otter++;
                yield return GrKill(a, pa, WC.EnemyOtter, 9, 1, null, WC.ItemOtterPelt);
            }
            Check(a.ServerModel.Count(WC.ItemOtterPelt) == 8, $"Acht Bruchotterfelle aus der Beute ({otter} Otter)");
            yield return GrTurnIn(a, pa, WC.W04);
            Check(a.ServerModel.Count(WC.ItemHorseshoes) == 1 && a.ServerModel.Count(WC.ItemOtterPelt) == 0, "Harm gibt die Hufeisen, die Felle sind abgegeben");
            yield return GrAccept(a, pa, WC.W05);
            Check(a.ServerModel.Count(WC.ItemBridle) == 1, "Lute gibt das Zaumzeug mit");
            yield return GrTurnIn(a, pa, WC.W05);
            Check(a.ServerModel.Count(WC.ItemHorseshoes) == 0 && a.ServerModel.Count(WC.ItemBridle) == 0, "Hufeisen und Zaumzeug in Hallgrund abgegeben");

            // ---------------------------------------------------------- K1/K2 Der Kornwagen
            Line("");
            Line("[K1/K2 Der Kornwagen: W06–W11]");
            yield return GrEnsureLevel(a, pa, 11);
            foreach (string q in new[] { WC.W06, WC.W07, WC.W08 })
            {
                yield return GrAccept(a, pa, q);
                yield return GrTurnIn(a, pa, q);
            }
            yield return GrAccept(a, pa, WC.W09);
            for (int i = 0; i < 8; i++) yield return GrTake(a, pa, WC.NodeGrain(i), true);
            yield return GrTurnIn(a, pa, WC.W09);
            foreach (string q in new[] { WC.W10, WC.W11 })
            {
                yield return GrAccept(a, pa, q);
                yield return GrTurnIn(a, pa, q);
            }

            // ---------------------------------------------------------- N1 Tränke, W12 Grundler
            Line("");
            Line("[W14 Die gemiedene Tränke, W12 Schlammgrundler]");
            yield return GrAccept(a, pa, WC.W14);
            yield return GrTake(a, pa, WC.NodeDrain, false);
            Check(pa.ServerProgress(WC.W14, 0) == 1, $"Zulauf frei geräumt ({pa.ServerStoryNote})");
            yield return GrTurnIn(a, pa, WC.W14);
            yield return GrAccept(a, pa, WC.W12);
            for (int i = 0; i < 8; i++) yield return GrTake(a, pa, WC.NodeFish(i), true);
            yield return GrTurnIn(a, pa, WC.W12);

            // ---------------------------------------------------------- K3 Das Zollbuch
            Line("");
            Line("[K3 Das Zollbuch: W19–W21]");
            yield return GrEnsureLevel(a, pa, 14);
            yield return GrAccept(a, pa, WC.W19);
            yield return GrTake(a, pa, WC.NodeTollBook, true);
            yield return GrTurnIn(a, pa, WC.W19);
            yield return GrAccept(a, pa, WC.W20);
            Check(a.ServerModel.Count(WC.ItemTollBook) == 1, "Die Hauptfrau gibt das Zollbuch für Wilke mit");
            yield return GrTurnIn(a, pa, WC.W20);
            yield return GrAccept(a, pa, WC.W21);
            yield return GrTurnIn(a, pa, WC.W21);
            Check(a.ServerModel.Count(WC.ItemTollBook) == 0, "Das Zollbuch bleibt bei Wilke");

            yield return KreidekellerSuite(a, pa);

            Line("");
            Info($"EP aus Westweiden-Aufträgen im Test: {_grQuestXp}, Nachhilfe {_grTopUp}, Gegner {_grKillXp}, Entdeckung {_grDiscoveryXp}");
        }
    
        // ================================================================== Kreidekeller

        private IEnumerator KreidekellerSuite(NetworkInventory a, NetworkProgression pa)
        {
            Line("");
            Line("[Kreidekeller: Bau und Wegnetz]");
            Check(HoehlenVolumen.Liste.Count >= 4, $"Höhlenvolumen in der Zelle ({HoehlenVolumen.Liste.Count})");
            Check(AlarmGlocke.Liste.Count == 1, "Alarmglocke am Spannseil");
            KellermeisterEncounter enc = EnemyEncounter.All.OfType<KellermeisterEncounter>().FirstOrDefault();
            Check(enc != null, "Begegnung Kellermeister Kord");
            var spawns = FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None).Where(s => s.Definition != null).ToList();
            int kk = spawns.Count(s => s.Definition.Id == WC.EnemyCellarGuard || s.Definition.Id == WC.EnemyCellarArcher || s.Definition.Id == WC.EnemyCellarMaster);
            Check(kk >= 15, $"Gegnergebiete im Keller ({kk}, davon {spawns.Count(s => s.SummonOnly)} nur gerufen)");
            var punkte = new List<StoryAnchor>();
            for (int i = 0; i < 20; i++)
            {
                StoryAnchor p = StoryAnchor.Find("kk.p." + i);
                if (p == null) break;
                punkte.Add(p);
            }
            Check(punkte.Count >= 9, $"Prüfpunkte ({punkte.Count})");
            var path = new NavMeshPath();
            float weg = 0f;
            for (int i = 0; i + 1 < punkte.Count; i++)
            {
                bool a0 = NavMesh.SamplePosition(punkte[i].Position, out NavMeshHit h0, 1.6f, NavMesh.AllAreas);
                bool a1 = NavMesh.SamplePosition(punkte[i + 1].Position, out NavMeshHit h1, 1.6f, NavMesh.AllAreas);
                bool ok = a0 && a1 && NavMesh.CalculatePath(h0.position, h1.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                float l = 0f;
                if (ok) for (int k = 1; k < path.corners.Length; k++) l += Vector3.Distance(path.corners[k - 1], path.corners[k]);
                weg += l;
                Check(ok, $"Weg {punkte[i].name.Replace("Prüfpunkt Kreidekeller: ", "")} → {punkte[i + 1].name.Replace("Prüfpunkt Kreidekeller: ", "")}" +
                          (ok ? $" ({l:0} m)" : $" – NavMesh {(a0 ? "" : "fehlt am Start ")}{(a1 ? "" : "fehlt am Ziel ")}{path.status}"));
            }
            Info($"Laufweg Felsband → Kaminsteig: {weg:0} m");
            foreach (int i in new[] { 3, 5, 6 })
            {
                if (i >= punkte.Count) break;
                Vector3 p = punkte[i].Position + Vector3.up * 0.3f;
                yield return Ask(a, "goto", V(p, 0f));
                yield return new WaitForSecondsRealtime(2.5f);
                Vector3 q = a.transform.position;
                Check(Vector3.Distance(q, p) < 4f && Mathf.Abs(q.y - punkte[i].Position.y) < 1.5f && HoehlenVolumen.Enthaelt(q),
                    $"Steht in der Höhle: {punkte[i].name.Replace("Prüfpunkt Kreidekeller: ", "")} (y {q.y:0.0}, Soll {punkte[i].Position.y:0.0})");
            }

            // ---------------------------------------------------------- Bilder (Spielkamera, für Max und die eigene Prüfung)
            string ordner = ProjectPaths.Logs("Westweiden", "Kreidekeller_" + System.DateTime.Now.ToString("HHmm"));
            System.IO.Directory.CreateDirectory(ordner);
            foreach ((int i, float yaw, string name) in new[]
                     {
                         (0, -1f, "1_Felsband_oben"), (1, -1f, "2_Felsband_Mitte"), (2, 90f, "3_Spalt"), (3, -1f, "4_Kornkammer_Galerie"),
                         (5, 180f, "5_Brandungsgang"), (6, 270f, "6_Zollkammer_Ladeluke"), (7, 270f, "7_Ladebuehne_Kiesbucht"), (8, 270f, "8_Kaminsteig_oben")
                     })
            {
                if (i >= punkte.Count) continue;
                float y = yaw;
                if (y < 0f && i + 1 < punkte.Count)
                {
                    Vector3 d = punkte[i + 1].Position - punkte[i].Position;
                    y = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                }
                yield return Ask(a, "goto", V(punkte[i].Position + Vector3.up * 0.3f, y));
                yield return new WaitForSecondsRealtime(2.5f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ordner, "KK_" + name + ".png"));
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Info("Bilder: " + ordner);

            // ---------------------------------------------------------- Alarmglocke
            Line("");
            Line("[Kreidekeller: Alarmglocke]");
            AlarmGlocke glocke = AlarmGlocke.Liste.FirstOrDefault();
            if (glocke != null)
            {
                yield return Ask(a, "goto", V(glocke.SeilMitte + new Vector3(0f, 0.1f, 2.5f), 180f));
                yield return new WaitForSecondsRealtime(1f);
                yield return Ask(a, "goto", V(glocke.SeilMitte + new Vector3(0f, 0.1f, 0f), 180f));
                float t0 = Time.realtimeSinceStartup;
                while (glocke.Ausloesungen == 0 && Time.realtimeSinceStartup - t0 < 4f) yield return null;
                yield return new WaitForSecondsRealtime(1f);
                Check(glocke.Ausloesungen == 1 && glocke.Gerufen.Count == 2, $"Seil berührt: Glocke läutet, {glocke.Gerufen.Count} Verstärkung kommt");
                foreach (EnemyBrain b in glocke.Gerufen.ToList())
                {
                    Health hp = b.Health;
                    hp?.ApplyDamage(new DamageInfo { Amount = 1f, Source = a.gameObject });
                    hp?.ApplyDamage(new DamageInfo { Amount = hp.Max * 20f, Source = a.gameObject, Unblockable = true });
                }
                yield return new WaitForSecondsRealtime(0.8f);
            }

            // ---------------------------------------------------------- W23 mit dem echten Kellermeister
            Line("");
            Line("[W23 Die Siegel im Kreidekeller]");
            yield return GrEnsureLevel(a, pa, 15);
            yield return GrAccept(a, pa, WC.W23);
            foreach (string n in new[] { WC.NodeSealRed, WC.NodeSealBlue, WC.NodeSealMill }) yield return GrTake(a, pa, n, true);
            EnemyBrain kord = EnemyBrain.All.FirstOrDefault(b => b != null && b.IsActive && b.Definition != null && b.Definition.Id == WC.EnemyCellarMaster && b.State != EnemyState.Dead);
            Check(kord != null, "Kellermeister Kord steht in der Zollkammer");
            if (kord != null && enc != null)
            {
                yield return Ask(a, "goto", V(kord.transform.position + kord.transform.forward * 3f + Vector3.up * 0.3f, 0f));
                yield return new WaitForSecondsRealtime(0.6f);
                Health hp = kord.Health;
                hp.ApplyDamage(new DamageInfo { Amount = 1f, Source = a.gameObject });
                yield return new WaitForSecondsRealtime(0.6f);
                Check(enc.Engaged, "Begegnung beginnt, sobald Kord angreift");
                hp.ApplyDamage(new DamageInfo { Amount = hp.Max * 0.55f, Source = a.gameObject, Unblockable = true });
                yield return new WaitForSecondsRealtime(0.8f);
                Check(enc.HatGerufen && enc.LivingAdds == 2, $"Bei der Hälfte ruft er die Schützen ({enc.LivingAdds})");
                hp.ApplyDamage(new DamageInfo { Amount = hp.Max * 2f, Source = a.gameObject, Unblockable = true });
                yield return new WaitForSecondsRealtime(1f);
                Check(enc.Defeated && enc.LivingAdds == 0, "Kord besiegt, die Gerufenen ziehen ab");
                var corpse = kord.GetComponent<CorpseLoot>();
                int idx = corpse != null ? corpse.Get(a.ProfileId).FindIndex(s => s.id == WC.ItemBoatList) : -1;
                if (idx >= 0)
                {
                    string nid = kord.GetComponent<NetworkObject>().ObjectId.ToString();
                    yield return Ask(a, "goto", V(kord.transform.position + new Vector3(1.2f, 0.3f, -1f), 0f));
                    yield return Ask(a, "loot-open", nid);
                    if (_last.ok) yield return Ask(a, "loot-take", idx.ToString());
                }
                Check(a.ServerModel.Count(WC.ItemBoatList) == 1, "Liste des Kellermeisters aus seiner Beute");
            }
            yield return GrTurnIn(a, pa, WC.W23);

            Line("");
            Line("[W24 Kopf des Zöllners]");
            yield return GrAccept(a, pa, WC.W24);
            int habbo = 0;
            while (a.ServerModel.Count(WC.ItemHabboHead) < 1 && habbo < 3)
            {
                habbo++;
                yield return GrKill(a, pa, WC.EnemyHabbo, 15, 1, null, WC.ItemHabboHead);
            }
            Check(a.ServerModel.Count(WC.ItemHabboHead) == 1, "Habbo besiegt, Beweis für Alwine");
            yield return GrTurnIn(a, pa, WC.W24);
        }
    }
}
