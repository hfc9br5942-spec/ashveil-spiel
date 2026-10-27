using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Quests;
using Ashveil.Gameplay.Story;
using FishNet.Object;
using UnityEngine;
using UnityEngine.AI;

namespace Ashveil.Networking
{
    /// <summary>
    /// Tank-Klassenquests Mensch – „Wo die Herde floh“ (HQ1) und „Der letzte Weidegang“ (HQ2), Abnahme nach Vorgabe §42.
    /// Besitzer der Geschichte ist der Test-Client Hallbot (menschlicher Eidwaechter, damit Phasing, Wiederverbinden und
    /// Client-Szenen echt laufen); der Editor-Host Maxmod ist erst Fremder (andere Klasse, anderer Stand), dann Gruppenhelfer.
    /// Start: „Ashveil › Test › Pferdehof (2 Spieler)“. Bericht: Logs/Ashveil/Ashveil_PferdehofTest.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string HorseFlag = "pferdehof";
        private static bool _horseMode;
        private static readonly string[] HorseProfiles = { "horse-a", "horse-b" };

        private const string HQ1 = HorseStoryCatalog.FoalRescueQuest, HQ2 = HorseStoryCatalog.LastPastureQuest, HBreeder = HorseStoryCatalog.BreederNpcId;

        private IEnumerator HorseStorySuite()
        {
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(HorseProfiles[0])) != null, 30f);
            Check(a != null, "Spieler A (Editor-Host, Maxmod) angemeldet");
            if (a == null) yield break;
            string exe = Ashveil.Core.ProjectPaths.TestClientExe;
            if (!File.Exists(exe)) { Check(false, "Test-Client vorhanden"); yield break; }
            StartClient(exe, HorseProfiles[1]);
            NetworkInventory b = null;
            yield return WaitUntil(() => (b = OnlineInv(HorseProfiles[1])) != null, 150f);
            Check(b != null, "Spieler B (Test-Client, Hallbot) angemeldet");
            if (b == null) yield break;
            yield return new WaitForSecondsRealtime(2f);
            NetworkProgression ap = a.GetComponent<NetworkProgression>(), bp = b.GetComponent<NetworkProgression>();
            CharacterClassDefinition warden = GameData.Class("class.warden"), stalker = GameData.Class("class.ashstalker"), weaver = GameData.Class("class.emberweaver");

            // ============================================================ Daten
            Line("");
            Line("[Daten]");
            QuestDefinition q1 = GameData.Quest(HQ1), q2 = GameData.Quest(HQ2);
            Check(q1 != null && q2 != null && GameData.Npc(HBreeder) != null, "Beide Auftraege und der Pferdezuechter sind in der Datenbank");
            if (q1 == null || q2 == null) { Line("  → Menü „Ashveil › Welt › Westweiden bauen“ ausführen."); yield break; }
            Check(q1.requiredRace == RaceCatalog.HumanId && q1.requiredRole == ClassRole.Tank && q1.requiredFlags.Contains(HorseStoryCatalog.MilestoneIntro) && q1.offeredByStory &&
                  q2.prerequisites.Contains(q1) && q2.requiredFlags.Contains(HorseStoryCatalog.MilestoneMainStory) && q2.minLevel == 20,
                "Zugang: Q1 Mensch + Tank + Einführung (Stufe ≥ 3); Q2 nach Q1 + Hauptgeschichte + Stufe 20");
            Check(q1.Title == "Wo die Herde floh" && q2.Title == "Der letzte Weidegang" && GameData.Npc(HBreeder).Name == "Brun Haller",
                $"Namen aus der Texttabelle: „{q1.Title}“, „{q2.Title}“, „{GameData.Npc(HBreeder).Name}“");
            int missing = 0, scenes = 0;
            foreach (DialogueScene sc in DialogueLibrary.AllScenes)
            {
                scenes++;
                foreach (DialogueLine l in sc.Lines)
                    if (!string.IsNullOrEmpty(l.TextKey) && !Loc.Has(l.TextKey)) { missing++; Info("fehlt: " + l.TextKey); }
            }
            Check(scenes >= 13 && missing == 0 && DialogueLibrary.Speaker("breeder_panic") != null && DialogueLibrary.Speaker("breeder_tired") != null,
                $"Dialog-Szenen datengetrieben: {scenes} Szenen, alle Texte vorhanden, Sprecherprofile Panik/ruhig/müde");
            HorseStoryContent content = HorseStoryContent.Load();
            Check(content != null && content.herdPrefab != null && content.predatorPrefab != null && content.wolfPrefab != null && content.spitterPrefab != null && content.lonePrefab != null,
                "Inhalte: Herden-Objekt, Mähnenreißer, Aschwolf, Nadelspeier, Einzelgänger");
            StoryHorseIdentity id = HorseStoryCatalog.Warhorse;
            HorseLook foal = HorseLook.FromIdentity(id, true, false), adult = HorseLook.FromIdentity(id, false, true);
            Check(foal.Coat == adult.Coat && foal.Mane == adult.Mane && foal.Blaze == adult.Blaze && foal.Legs == adult.Legs &&
                  foal.Scar == HorseScarPattern.RightShoulderFresh && adult.Scar == HorseScarPattern.RightShoulderHealed && adult.Withers >= 1.70f && adult.Withers <= 1.75f &&
                  id.blaze == HorseBlazePattern.NarrowBroken && id.coat == HorseCoatPreset.BrownBlack,
                $"Story-Pferd: feste Identität (Braunschwarz, gebrochene Blesse, Schulter rechts: Fohlen frisch → erwachsen Narbe), Widerrist {adult.Withers:0.00} m");
            string gameplayDir = Path.Combine(Application.dataPath, "_Ashveil/Scripts/Gameplay");
            string asmdef = Directory.Exists(gameplayDir) ? Directory.GetFiles(gameplayDir, "*.asmdef").Select(File.ReadAllText).FirstOrDefault() : null;
            Check(asmdef != null && !asmdef.Contains("FishNet"), "Gameplay-Assembly ohne FishNet-Abhängigkeit");

            // ============================================================ Zugang
            Line("");
            Line("[Zugang Q1]");
            b.ServerModel.Class = warden;
            b.ServerSetLevel(4);
            bp.ServerDebugRace(RaceCatalog.HumanId);
            bp.ServerDebugFlag(HorseStoryCatalog.MilestoneIntro, true);
            bp.ServerDebugSetHorseStory(HorseStoryState.Unknown, 0);
            string Gate(QuestDefinition q) => QuestRules.CheckGate(q, bp.ServerGate());
            Check(Gate(q1) == null, "Menschlicher Eidwächter (Tank), Stufe 4, nach der Einführung: Q1 zugänglich");
            b.ServerModel.Class = stalker;
            string whyDps = Gate(q1);
            b.ServerModel.Class = weaver;
            string whyHeal = Gate(q1);
            b.ServerModel.Class = warden;
            bp.ServerDebugRace(RaceCatalog.NharimId);
            string whyRace = Gate(q1);
            bp.ServerDebugRace(RaceCatalog.HumanId);
            bp.ServerDebugFlag(HorseStoryCatalog.MilestoneIntro, false);
            string whyFlag = Gate(q1);
            bp.ServerDebugFlag(HorseStoryCatalog.MilestoneIntro, true);
            Check(whyDps != null && whyHeal != null && whyRace != null && whyFlag != null,
                $"Nicht für: Schaden („{whyDps}“), Heiler („{whyHeal}“), anderes Volk („{whyRace}“), ohne Einführung („{whyFlag}“)");
            b.ServerSetLevel(45);
            Check(Gate(q1) == null, "Keine Höchststufe: auf Stufe 45 bleibt Q1 zugänglich");
            b.ServerSetLevel(20);
            Check(QuestRules.CanAccept(q2, 20, bp.ServerIsCompleted, bp.ServerIsActive, 0, bp.ServerGate()) != null, "Q2 erscheint nicht ohne Q1");
            b.ServerSetLevel(4);

            // Maxmod: anderer Stand (Schaden), damit der Hof fuer ihn Alltag ist
            a.ServerModel.Class = stalker;
            ap.ServerDebugSetHorseStory(HorseStoryState.Unknown, 0);

            // ============================================================ Welt
            Line("");
            Line("[Welt]");
            WorldStreamer ws = WorldStreamer.Instance;
            Vector3 farmGuess = new Vector3(-798f, 13f, 256f);
            if (ws != null) ws.RequestPreload(farmGuess, 30f);
            yield return WaitUntil(() => ws == null || ws.IsReadyAt(farmGuess), 30f);
            yield return WaitUntil(() => StoryAnchor.Find(NetworkProgression.AnchorYard) != null, 10f);
            StoryAnchor yard = StoryAnchor.Find(NetworkProgression.AnchorYard), basin = StoryAnchor.Find(NetworkProgression.AnchorBasin), foalAnchor = StoryAnchor.Find(NetworkProgression.AnchorFoal),
                gate = StoryAnchor.Find(NetworkProgression.AnchorGate);
            HerdRoute route = HerdRoute.Find(NetworkProgression.RouteReturn);
            Check(yard != null && basin != null && foalAnchor != null && gate != null && route != null, "Weltzelle Westweiden geladen, Geschichtsanker und Heimweg gefunden");
            if (yard == null || basin == null || route == null) yield break;
            float trail = 0f;
            bool trailOk = true;
            var stops = new List<Vector3> { yard.Position };
            for (int i = 0; i < 6; i++) { StoryAnchor c = StoryAnchor.Find(NetworkProgression.ClueAnchor(i)); if (c != null) stops.Add(c.Position); else trailOk = false; }
            stops.Add(basin.Position);
            for (int i = 1; i < stops.Count; i++) { float d = PathLen(stops[i - 1], stops[i]); if (d < 0) trailOk = false; else trail += d; }
            Check(trailOk && trail >= 1800f && trail <= 2200f, $"Suchweg Hof → sechs Hinweise → Felskessel: {trail:0} m auf dem NavMesh (Ziel 1800–2200 m)");
            Check(route.Length >= 700f && route.Length <= 1000f, $"Heimweg der Herde (kuratierte Route): {route.Length:0} m (Ziel 700–1000 m)");
            float pass = PathLen(new Vector3(-250f, 0f, 240f) + Vector3.up * KarethTerrainBuilderHeight(-250f, 240f), yard.Position);
            Check(pass > 0f, $"Weidepass: zu Fuß vom Holzfällerlager (Kareth) zum Hof {pass:0} m");

            // Phasing: zwei Spieler, zwei Stände – jeder sieht seinen Hof
            yield return GoTo(b, yard.Position + new Vector3(-28f, 0f, 0f));   // ~20 m vom Westzaun, wo er in der Krise steht
            yield return GoTo(a, yard.Position + new Vector3(-10f, 0f, -12f));
            yield return new WaitForSecondsRealtime(2f);
            yield return Ask(b, "story");
            string bs = _last.data;
            yield return Ask(a, "story");
            string asState = _last.data;
            Check(bs.Contains("farm=Crisis") && bs.Contains("fenceBroken=1") && bs.Contains("localHerd=0"), $"Hallbot (Tank, betroffen): Koppel leer, Westzaun eingedrückt ({Short(bs)})");
            Check(asState.Contains("farm=Normal") && asState.Contains("fenceBroken=0") && asState.Contains("localHerd=6"), $"Maxmod (nicht betroffen): Alltag, Herde auf der Koppel ({Short(asState)})");

            // ============================================================ HQ1 Annahme
            Line("");
            Line("[Q1: Der Züchter kommt herüber]");
            yield return WaitUntil(() => bp.ServerLastScene == "q1_approach", 20f);
            Check(bp.ServerLastScene == "q1_approach", $"Er bemerkt Rüstung und Schild und kommt herüber (Szene „q1_approach“, {bp.ServerStoryNote})");
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Ask(b, "story");
            Check(_last.data.Contains("farmerApproach=1") || _last.data.Contains("scene=q1_approach"), $"Beim Client: Figur läuft auf den Spieler zu, Szene läuft ({Short(_last.data)})");
            float t0 = Time.realtimeSinceStartup;
            string lastScene = "";
            while (Time.realtimeSinceStartup - t0 < 60f)
            {
                yield return Ask(b, "story");
                lastScene = _last.data;
                if (lastScene.Contains("last=q1_approach")) break;
                yield return new WaitForSecondsRealtime(2f);
            }
            Check(lastScene.Contains("offer=" + HQ1), $"Nach der Szene bietet er „Wo die Herde floh“ an ({Short(lastScene)})");
            yield return Ask(a, "npc-talk", HBreeder);
            yield return Ask(a, "quest-accept", HQ1);
            Check(!ap.ServerIsActive(HQ1), "Maxmod (Schaden) kann Q1 nicht annehmen");
            yield return Ask(b, "npc-talk", HBreeder);
            yield return Ask(b, "quest-accept", HQ1);
            Check(bp.ServerIsActive(HQ1) && bp.ServerHorseState == HorseStoryState.FoalMissing, $"Hallbot nimmt an → Stand {bp.ServerHorseState}");
            Check(!bp.ServerHasMount, "Kein Reittier in Q1");

            // ============================================================ HQ1 Suche
            Line("");
            Line("[Q1: Spuren]");
            yield return GoTo(b, StoryAnchor.Find(NetworkProgression.ClueAnchor(2)).Position);
            yield return new WaitForSecondsRealtime(1.2f);
            Check(bp.ServerHorseStep == 0, "Außer der Reihe zählt ein Hinweis nicht (Bach vor Zaun)");
            bool order = true;
            for (int i = 0; i < 6; i++)
            {
                yield return GoTo(b, StoryAnchor.Find(NetworkProgression.ClueAnchor(i)).Position);
                int want = i + 1;
                yield return WaitUntil(() => bp.ServerHorseStep >= want, 6f);
                bool ok = bp.ServerHorseStep == want && bp.ServerLastScene == "q1_clue" + want;
                order &= ok;
                Info($"Hinweis {want}: Schritt {bp.ServerHorseStep}, Szene {bp.ServerLastScene}");
            }
            Check(order, "Sechs Hinweise in der richtigen Reihenfolge, je eine Beobachtung");
            yield return Ask(b, "story");
            Check(_last.data.Contains("step=6"), $"Client kennt den Suchstand ({Short(_last.data)})");

            yield return GoTo(b, basin.Position + basin.Forward * -14f);
            yield return WaitUntil(() => bp.ServerHorseState == HorseStoryState.FoalFound && bp.ServerHerd != null, 12f);
            HerdEncounter herd = bp.ServerHerd;
            Check(bp.ServerHorseState == HorseStoryState.FoalFound && herd != null && herd.FoalStuck && herd.Mode == HerdMode.Huddle,
                $"Felskessel: Herde zusammengedrängt, Fohlen hängt fest ({bp.DescribeHorseStory()})");
            if (herd == null) yield break;
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Ask(b, "story");
            Check(_last.data.Contains("herds=1/1"), $"Hallbot sieht die Herde ({Short(_last.data)})");
            Check(!herd.NetworkObject.Observers.Contains(a.Owner), "Maxmod (nicht in der Gruppe) sieht sie nicht");

            // ============================================================ Fohlen befreien
            Line("");
            Line("[Q1: Fohlen befreien]");
            string far = bp.ServerStoryAction("free_foal", "");
            Check(far != "", $"Aus der Entfernung geht es nicht („{far}“)");
            yield return GoTo(b, foalAnchor.Position + basin.Forward * -1.6f);
            yield return Ask(b, "story-act", "free_foal");
            yield return WaitUntil(() => bp.ServerHorseState == HorseStoryState.FoalRescued, 6f);
            Check(bp.ServerHorseState == HorseStoryState.FoalRescued && bp.ServerHorseStep == 12 && bp.ServerPredator != null && !herd.FoalStuck,
                $"„Fohlen befreien“ (3,5 s) → frei, der Mähnenreißer kommt ({bp.DescribeHorseStory()})");
            EnemyBrain pred = bp.ServerPredator;
            Check(pred != null && pred.Definition != null && pred.Definition.Id == "enemy.human_tank.maneripper" && pred.Definition.moveset.Count == 3,
                $"Quest-Raubtier: Variante des Aschwolfs mit Biss, schwerem Biss, Ansturm (Stufe {(pred != null ? pred.Level : 0)})");
            Check(pred != null && !pred.GetComponent<NetworkObject>().Observers.Contains(a.Owner), "Fremde sehen das Raubtier nicht (können es nicht stehlen)");
            string steal = ap.ServerStoryAction("free_foal", "");
            Check(steal != "", $"Maxmod kann nichts auslösen („{steal}“)");

            // Sprung zur Herde: dazwischenstehen faengt ihn ab
            yield return GoTo(b, basin.Position + basin.Forward * -12f);
            yield return new WaitForSecondsRealtime(0.5f);
            float stress0 = bp.ServerHerdStress;
            bool leap = false;
            for (int i = 0; i < 20 && !leap; i++)
            {
                leap = bp.ServerDebugLeap(1);
                if (!leap) yield return new WaitForSecondsRealtime(0.25f);
            }
            yield return new WaitForSecondsRealtime(2f);
            Check(leap && bp.ServerHerdStress > stress0 + 20f, $"Sprung zur Herde, niemand dazwischen → Panik ({stress0:0} → {bp.ServerHerdStress:0})");
            Vector3 hp = bp.ServerDebugHerdPoint(2);
            yield return GoTo(b, hp + (pred != null ? (pred.transform.position - hp).normalized * 1.2f : Vector3.zero));
            yield return new WaitForSecondsRealtime(0.3f);
            float stress1 = bp.ServerHerdStress;
            bool leap2 = false;
            for (int i = 0; i < 20 && !leap2; i++)
            {
                leap2 = bp.ServerDebugLeap(2);
                if (!leap2) yield return new WaitForSecondsRealtime(0.25f);
            }
            stress1 = bp.ServerHerdStress;
            yield return new WaitForSecondsRealtime(2f);
            Check(leap2 && bp.ServerHerdStress <= stress1 + 1f && pred != null && (pred.IsStunned || bp.ServerHerdStress <= stress1),
                $"Tank steht dazwischen → Sprung abgefangen, Raubtier taumelt (Stress {stress1:0} → {bp.ServerHerdStress:0})");

            // Zuruecksetzen: zu viel Panik
            bp.ServerDebugStress(100f);
            yield return new WaitForSecondsRealtime(0.6f);
            Check(bp.ServerHorseState == HorseStoryState.FoalFound && bp.ServerPredator == null && herd.FoalStuck,
                $"Herde bricht aus → Anfang der Begegnung (kein Fehlschlag, Fohlen stirbt nie) ({bp.DescribeHorseStory()})");

            // Wipe
            yield return FreeFoal(b, bp, foalAnchor, basin);
            Health bh = b.GetComponent<Health>();
            bh.ApplyDamage(new DamageInfo { Amount = 1e6f, Source = bp.ServerPredator != null ? bp.ServerPredator.gameObject : null, Direction = Vector3.forward });
            yield return WaitUntil(() => bp.ServerHorseState == HorseStoryState.FoalFound, 12f);
            Check(bp.ServerHorseState == HorseStoryState.FoalFound && bp.ServerPredator == null, "Wipe → Begegnung setzt sich sicher zurück");
            yield return new WaitForSecondsRealtime(1f);
            var bnp = b.GetComponent<NetworkPlayer>();
            if (bh != null && !bh.IsAlive && bnp != null) bnp.ServerReviveHere(1f);
            yield return new WaitForSecondsRealtime(1f);

            // Gruppe: Helfer darf mitkaempfen, schaltet aber nichts weiter
            Line("");
            Line("[Q1: Gruppe]");
            yield return Ask(a, "party-invite", "Hallbot");
            yield return new WaitForSecondsRealtime(1f);
            yield return Ask(b, "party-respond", "1");
            yield return new WaitForSecondsRealtime(1f);
            PartyService.Party party = PartyService.Of(HorseProfiles[1]);
            Check(party != null && party.Members.Contains(HorseProfiles[0]), $"Gruppe gebildet ({(party != null ? string.Join(",", party.Members) : "keine")})");
            yield return FreeFoal(b, bp, foalAnchor, basin);
            pred = bp.ServerPredator;
            herd = bp.ServerHerd; // nach dem Zuruecksetzen neu erzeugt
            yield return GoTo(a, basin.Position + basin.Forward * -10f);
            yield return WaitUntil(() => pred != null && herd != null && pred.GetComponent<NetworkObject>().Observers.Contains(a.Owner) && herd.NetworkObject.Observers.Contains(a.Owner), 12f);
            bool seesBoth = pred != null && herd != null && pred.GetComponent<NetworkObject>().Observers.Contains(a.Owner) && herd.NetworkObject.Observers.Contains(a.Owner);
            if (!seesBoth && herd != null)
            {
                StoryPhaseTag tag = herd.GetComponent<StoryPhaseTag>();
                Info($"Diagnose: A-Verbindung {a.Owner?.ClientId}, Beobachter Herde [{string.Join(",", herd.NetworkObject.Observers.Select(o => o.ClientId))}], " +
                     $"Abstand A–Herde {Vector3.Distance(a.transform.position, herd.transform.position):0} m, Besitzer {tag?.OwnerProfile}, " +
                     $"Gruppe {string.Join(",", PartyService.Of(HorseProfiles[1])?.Members ?? new List<string>())}, A-Objekte {a.Owner?.Objects.Count}");
                FishNet.InstanceFinder.ServerManager.Objects.RebuildObservers(herd.NetworkObject);
                yield return new WaitForSecondsRealtime(0.5f);
                Info($"nach Neuaufbau: Beobachter Herde [{string.Join(",", herd.NetworkObject.Observers.Select(o => o.ClientId))}]");
            }
            Check(seesBoth, "Gruppenmitglied in der Nähe sieht Herde und Raubtier");
            float hpBefore = pred != null ? pred.Health.Current : 0f;
            if (pred != null) pred.Health.ApplyDamage(new DamageInfo { Amount = 50f, Source = a.gameObject, Direction = Vector3.forward });
            yield return new WaitForSecondsRealtime(0.3f);
            Check(pred != null && pred.Health.Current < hpBefore && ap.ServerHorseState == HorseStoryState.Unknown, "Helfer kämpft mit, sein eigener Stand bleibt unberührt");

            // Wiederverbinden mitten im Kampf
            Line("");
            Line("[Q1: Wiederverbinden]");
            yield return Relog(exe, 1);
            b = OnlineInv(HorseProfiles[1]);
            Check(b != null, "Hallbot wieder angemeldet");
            if (b == null) yield break;
            bp = b.GetComponent<NetworkProgression>();
            Check(bp.ServerHorseState == HorseStoryState.FoalFound && bp.ServerHorseStep == 10 && bp.ServerIsActive(HQ1),
                $"Nach dem Kampf-Abbruch: Anfang der Begegnung, Auftrag bleibt ({bp.DescribeHorseStory()})");
            yield return GoTo(b, basin.Position + basin.Forward * -12f);
            yield return WaitUntil(() => bp.ServerHerd != null, 12f);
            herd = bp.ServerHerd;
            Check(herd != null && herd.FoalStuck && HerdCount(HorseProfiles[1]) == 1, "Genau eine Herde (keine Doppelte), Fohlen hängt wieder fest");
            if (herd == null) yield break;

            // Raubtier besiegen
            Line("");
            Line("[Q1: Mähnenreißer]");
            yield return FreeFoal(b, bp, foalAnchor, basin);
            pred = bp.ServerPredator;
            for (int i = 0; i < 40 && pred != null && pred.State != EnemyState.Dead; i++)
            {
                pred.Health.ApplyDamage(new DamageInfo { Amount = 400f, Source = b.gameObject, Direction = Vector3.forward });
                yield return new WaitForSecondsRealtime(0.1f);
            }
            yield return WaitUntil(() => bp.ServerHorseStep == 20, 5f);
            Check(bp.ServerHorseStep == 20 && herd.Mode == HerdMode.FleeHome && bp.ServerProgress(HQ1, 2) == 1,
                $"Raubtier besiegt → „Kehre zum Hof zurück“, Herde auf dem Heimweg ({bp.DescribeHorseStory()})");

            // Heimweg: Herde wartet ohne den Tank, folgt mit ihm
            Line("");
            Line("[Q1: Heimweg]");
            bp.ServerDebugHerdSpeed(10f);
            yield return GoTo(b, yard.Position);
            float p0 = bp.ServerHerdProgress;
            yield return new WaitForSecondsRealtime(3f);
            Check(bp.ServerHerdProgress - p0 < 1f, $"Ohne den Tank in der Nähe wartet die Herde ({p0:0} → {bp.ServerHerdProgress:0} m)");
            float walkT = Time.realtimeSinceStartup;
            bool relogged = false;
            while (bp.ServerHorseState == HorseStoryState.FoalRescued && Time.realtimeSinceStartup - walkT < 120f)
            {
                Vector3 lead = route.PointAt(bp.ServerHerdProgress + 12f, out _);
                yield return GoTo(b, lead, 0.3f);
                if (!relogged && bp.ServerHerdProgress > 350f)
                {
                    relogged = true;
                    float before = bp.ServerHerdProgress;
                    yield return Relog(exe, 1);
                    b = OnlineInv(HorseProfiles[1]);
                    if (b == null) { Check(false, "Hallbot nach dem Heimweg-Wiederverbinden zurück"); yield break; }
                    bp = b.GetComponent<NetworkProgression>();
                    bp.ServerDebugHerdSpeed(10f);
                    Check(bp.ServerHorseStep == 20 && Mathf.Abs(bp.ServerHerdProgress - before) < 25f,
                        $"Wiederverbinden auf dem Heimweg: weiter ab {bp.ServerHerdProgress:0} m (vorher {before:0} m)");
                }
            }
            Check(bp.ServerHorseState == HorseStoryState.ReturnedToFarm && bp.ServerProgress(HQ1, 3) == 1, $"Herde am Hof ({bp.DescribeHorseStory()})");

            // Abschluss-Szene: das Fohlen schnuppert
            Line("");
            Line("[Q1: Am Hof]");
            yield return GoTo(b, yard.Position + new Vector3(3f, 0f, -3f));
            yield return WaitUntil(() => bp.ServerLastScene == "q1_home", 10f);
            Check(bp.ServerLastScene == "q1_home", "Abschluss-Szene startet am Hof");
            bool sniffed = false;
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 45f)
            {
                herd = bp.ServerHerd;
                if (herd != null && herd.Sniffing) sniffed = true;
                yield return Ask(b, "story");
                if (_last.data.Contains("last=q1_home")) break;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Check(sniffed, "Das Fohlen kommt und schnuppert (vor „Seltsam.“)");
            yield return Ask(b, "npc-talk", HBreeder);
            yield return Ask(b, "quest-turnin", HQ1);
            yield return new WaitForSecondsRealtime(1f);
            Check(bp.ServerIsCompleted(HQ1) && bp.ServerHorseState == HorseStoryState.Growing && b.ServerData.HasFlag(HorseStoryCatalog.FlagFoalRescueCompleted) &&
                  b.ServerData.HasFlag(HorseStoryCatalog.FlagStoryHorseMetPlayer) && !bp.ServerHasMount && bp.ServerHerd == null,
                $"Q1 abgeschlossen: FoalRescueCompleted + StoryHorseMetPlayer, kein Reittier ({bp.DescribeHorseStory()})");
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Ask(b, "story");
            Check(_last.data.Contains("farm=Growing") && _last.data.Contains("localHerd=7") && _last.data.Contains("fenceBroken=0"),
                $"Hof danach: Fohlen bei der Stute, Zaun geflickt ({Short(_last.data)})");

            // ============================================================ HQ2
            Line("");
            Line("[Q2: Voraussetzungen]");
            b.ServerSetLevel(20);
            yield return new WaitForSecondsRealtime(1f);
            Check(bp.ServerHorseState == HorseStoryState.Growing && QuestRules.CheckGate(q2, bp.ServerGate()) != null,
                "Stufe 20 ohne Hauptgeschichte: Q2 erscheint nicht");
            // Das Salzgruben-Finale erlebt man weit weg vom Hof – zurueck geht es ueber das Wachtfeuer
            NpcIdentity bralt = NpcIdentity.Find("npc.oskar_bralt");
            if (bralt != null) yield return GoTo(b, bralt.transform.position + Vector3.forward * 60f);
            bp.ServerDebugFlag(HorseStoryCatalog.MilestoneMainStory, true);
            yield return WaitUntil(() => bp.ServerHorseState == HorseStoryState.AdultAtFarm, 3f);
            Check(bp.ServerHorseState == HorseStoryState.AdultAtFarm, "Mit dem Salzgruben-Finale: das Pferd ist erwachsen (Stand AdultAtFarm)");
            if (bralt != null)
            {
                yield return GoTo(b, bralt.transform.position + Vector3.forward * 2f);
                yield return WaitUntil(() => bp.ServerLastScene == "q2_messenger", 6f);
                Check(bp.ServerLastScene == "q2_messenger" && b.ServerData.HasFlag(HorseStoryCatalog.FlagMessengerHeard), "Bote am Wachtfeuer: „Vom Pferdehof draußen war jemand hier.“");
            }

            Line("");
            Line("[Q2: Wiedererkennen]");
            yield return GoTo(b, yard.Position + new Vector3(22f, 0f, -6f));
            yield return WaitUntil(() => bp.ServerLastScene == "q2_recognize", 25f);
            Check(bp.ServerLastScene == "q2_recognize" && bp.ServerHerd != null && bp.ServerHerd.Kind == HerdEncounter.KindAdultHerd, $"Wiedererkennen: erwachsene Herde, Szene startet ({bp.DescribeHorseStory()})");
            bool away = false;
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 50f)
            {
                if (bp.ServerHerd != null && bp.ServerHerd.StoryHorseAway) away = true;
                yield return Ask(b, "story");
                if (_last.data.Contains("last=q2_recognize")) break;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Check(away, "Das Pferd bemerkt ihn, hebt den Kopf und kommt langsam heran – erst dann spricht der Züchter");
            // Das Story-Pferd auf Maxmods Rechner (Gruppe, in der Nähe): genau diese Identitaet
            yield return GoTo(a, yard.Position + new Vector3(20f, 0f, -10f));
            yield return new WaitForSecondsRealtime(2f);
            herd = bp.ServerHerd;
            HorseRig rig = herd != null && herd.Presenter != null ? herd.Presenter.Rig(herd.Presenter.FoalIndex) : null;
            Check(rig != null && rig.Look.Coat == adult.Coat && rig.Look.Blaze == HorseBlazePattern.NarrowBroken && rig.Look.Scar == HorseScarPattern.RightShoulderHealed && !rig.Look.Foal,
                $"Erwachsenes Pferd = storyhorse.human_tank_01 (Fell, Blesse, Narbe){(rig != null ? $", Widerrist {rig.WithersHeight:0.00} m" : "")}");
            yield return Ask(b, "npc-talk", HBreeder);
            yield return Ask(b, "quest-accept", HQ2);
            Check(bp.ServerIsActive(HQ2) && bp.ServerHorseStep == 1, $"„Der letzte Weidegang“ angenommen ({bp.DescribeHorseStory()})");

            Line("");
            Line("[Q2: Bedrohungen]");
            yield return WaitUntil(() => bp.ServerThreats.Count >= 6, 8f);
            Check(bp.ServerThreats.Count == 6, $"Drei Bedrohungsanker: Rudel (3), Speier (2), Einzelgänger (1) – {bp.ServerThreats.Count} Gegner");
            foreach ((EnemyBrain brain, string _) in bp.ServerThreats.ToList())
                for (int i = 0; i < 60 && brain != null && brain.State != EnemyState.Dead; i++)
                {
                    brain.Health.ApplyDamage(new DamageInfo { Amount = 900f, Source = b.gameObject, Direction = Vector3.forward });
                    yield return null;
                }
            yield return WaitUntil(() => bp.ServerHorseStep == 5, 6f);
            Check(bp.ServerHorseStep == 5 && bp.ServerProgress(HQ2, 0) == 1 && bp.ServerProgress(HQ2, 1) == 1 && bp.ServerProgress(HQ2, 2) == 1, "Weide sicher → Weidetor");

            Line("");
            Line("[Q2: Weidetor]");
            yield return GoTo(b, gate.Position + gate.Forward * -2.2f);
            yield return Ask(b, "story-act", "open_gate");
            yield return WaitUntil(() => bp.ServerHorseStep == 6, 5f);
            Check(bp.ServerHorseStep == 6 && bp.ServerLastScene == "q2_release", "„Weidetor öffnen“ → Szene");
            bool ranWithHerd = false, stoppedAway = false, released = false;
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 60f && bp.ServerHorseStep == 6)
            {
                herd = bp.ServerHerd;
                if (herd != null && herd.Mode == HerdMode.Released)
                {
                    released = true;
                    if (!herd.StoryHorseAway && !stoppedAway) ranWithHerd = true;
                    if (herd.StoryHorseAway && ranWithHerd) stoppedAway = true;
                }
                yield return new WaitForSecondsRealtime(0.3f);
            }
            Check(released && ranWithHerd && stoppedAway, "Herde läuft hinaus; das Pferd läuft erst mit, hält, dreht um und kommt zurück");
            Check(bp.ServerHorseStep == 7 && bp.ServerHerd != null && bp.ServerHerd.HandReady, "Nach Sattel und „Den da nicht.“: Hand hinhalten");

            Line("");
            Line("[Q2: Hand hinhalten]");
            var bar = b.GetComponent<NetworkActionBar>();
            var barBefore = bar.ServerSlots.Select(e => e.type + ":" + e.referenceId).ToList();
            yield return Ask(b, "story-act", "hand");
            yield return WaitUntil(() => bp.ServerHorseState == HorseStoryState.BondedToPlayer, 30f);
            Check(bp.ServerHasMount && b.ServerData.mount.id == MountCatalog.WarhorseId && b.ServerData.mount.cosmetics == HorseStoryCatalog.StoryHorseId &&
                  bp.ServerMasteryPoints(MountCatalog.Warhorse.bond.id) > 0 && bp.ServerIsCompleted(HQ2),
                $"Reittier freigeschaltet (Story-Pferd, Bindung mit Vorsprung {bp.ServerMasteryPoints(MountCatalog.Warhorse.bond.id):0}), Q2 abgeschlossen");
            var after = bar.ServerSlots.Select(e => e.type + ":" + e.referenceId).ToList();
            bool untouched = true;
            for (int i = 0; i < barBefore.Count && i < after.Count; i++)
                if (!barBefore[i].StartsWith("None") && barBefore[i] != after[i]) untouched = false;
            int mountsOnBar = bar.ServerSlots.Count(e => e.type == ActionEntryType.Mount);
            Check(untouched && mountsOnBar == 1, $"Leiste nicht überschrieben, Reittier genau einmal darauf ({mountsOnBar})");
            string again = bp.ServerStoryAction("hand", "");
            bp.ServerUnlockMount(MountCatalog.WarhorseId);
            Check(again != "" && bar.ServerSlots.Count(e => e.type == ActionEntryType.Mount) == 1, "Freischaltung genau einmal (zweites Mal ohne Wirkung)");
            yield return WaitUntil(() => bp.ServerHorseStep == 99, 30f);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Ask(b, "story");
            Check(bp.ServerHorseStep == 99 && _last.data.Contains("farm=After") && _last.data.Contains("farmerInside=1"),
                $"„Mehr hab ich heute nicht vor.“ – er geht ins Haus, kein Sterben ({Short(_last.data)})");
            yield return Ask(a, "story");
            Check(_last.data.Contains("farm=Normal") && _last.data.Contains("farmerInside=0"), $"Maxmod (anderer Stand) sieht den Züchter weiter bei der Arbeit ({Short(_last.data)})");

            Line("");
            Line("[Speichern und Epilog]");
            yield return Relog(exe, 1);
            b = OnlineInv(HorseProfiles[1]);
            if (b == null) { Check(false, "Hallbot nach dem Bindungs-Wiederverbinden zurück"); yield break; }
            bp = b.GetComponent<NetworkProgression>();
            bar = b.GetComponent<NetworkActionBar>();
            Check(bp.ServerHasMount && b.ServerData.mount.cosmetics == HorseStoryCatalog.StoryHorseId && bp.ServerHorseState == HorseStoryState.BondedToPlayer &&
                  bar.ServerSlots.Count(e => e.type == ActionEntryType.Mount) == 1 && b.ServerData.version == CharacterSaveData.CurrentVersion,
                "Nach Speichern/Neu anmelden: Pferd da, genau einmal, Stand erhalten");
            Check(!b.ServerData.horseStory.epilogue, "Epilog nicht sofort nach der Quest");
            bp.ServerDebugEpilogueCheck(0.5);
            Check(!b.ServerData.horseStory.epilogue, "… auch nicht nach einer halben Stunde");
            bp.ServerDebugEpilogueCheck(3.0);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Ask(b, "story");
            Check(b.ServerData.horseStory.epilogue && _last.data.Contains("farm=Epilogue"), $"Beim nächsten Login nach Stunden: stiller Hof ({Short(_last.data)})");

            // Aufraeumen
            yield return Ask(b, "party-leave");
            a.ServerModel.Class = warden;
        }

        // ================================================================== Hilfen

        private IEnumerator FreeFoal(NetworkInventory b, NetworkProgression bp, StoryAnchor foalAnchor, StoryAnchor basin)
        {
            if (bp.ServerHorseState != HorseStoryState.FoalFound) yield break;
            yield return GoTo(b, foalAnchor.Position + basin.Forward * -1.6f);
            yield return Ask(b, "story-act", "free_foal");
            yield return WaitUntil(() => bp.ServerHorseState == HorseStoryState.FoalRescued && bp.ServerPredator != null, 7f);
        }

        /// <summary>Test-Client beenden und neu starten (echtes Trennen/Wiederverbinden).</summary>
        private IEnumerator Relog(string exe, int index)
        {
            string profile = HorseProfiles[index];
            NetworkInventory old = OnlineInv(profile);
            StopOwnedClients();
            // Der Server merkt das Trennen sonst erst nach dem Zeitlimit – sofort trennen (wie ein echter Verbindungsabbruch)
            if (old != null && old.Owner != null && old.Owner.IsActive) old.Owner.Disconnect(true);
            yield return WaitUntil(() => OnlineInv(profile) == null, 30f);
            Info($"getrennt: {(OnlineInv(profile) == null ? "ja" : "nein")}");
            yield return new WaitForSecondsRealtime(2f);
            StartClient(exe, profile);
            NetworkInventory fresh = null;
            yield return WaitUntil(() => (fresh = OnlineInv(profile)) != null && fresh != old, 150f);
            yield return new WaitForSecondsRealtime(3f);
            if (fresh != null) yield return Ask(fresh, "state");
        }

        private IEnumerator GoTo(NetworkInventory who, Vector3 p, float settle = 1f)
        {
            WorldStreamer ws = WorldStreamer.Instance;
            if (ws != null) ws.RequestPreload(p, 20f);
            yield return WaitUntil(() => ws == null || ws.IsReadyAt(p), 20f);
            if (NavMesh.SamplePosition(p, out NavMeshHit hit, 6f, NavMesh.AllAreas)) p = hit.position;
            yield return Ask(who, "goto", V(p + Vector3.up * 0.1f, 0f));
            if (settle > 0f) yield return new WaitForSecondsRealtime(settle);
        }

        private static float PathLen(Vector3 from, Vector3 to)
        {
            if (!NavMesh.SamplePosition(from, out NavMeshHit a, 8f, NavMesh.AllAreas) || !NavMesh.SamplePosition(to, out NavMeshHit b, 8f, NavMesh.AllAreas)) return -1f;
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return -1f;
            float d = 0f;
            for (int i = 1; i < path.corners.Length; i++) d += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            return d;
        }

        private static float KarethTerrainBuilderHeight(float x, float z) =>
            Ashveil.Core.WorldGround.TrySample(new Vector3(x, 200f, z), out float h) ? h : 0f;

        private static int HerdCount(string profile) =>
            Object.FindObjectsByType<HerdEncounter>(FindObjectsSortMode.None).Count(h => h.IsSpawned && h.OwnerProfile == profile);

        private static string Short(string s) => s != null && s.Length > 150 ? s.Substring(0, 150) + "…" : s;
    }
}
