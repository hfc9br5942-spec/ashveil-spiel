using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ashveil.Core;
using Ashveil.Data;
using Ashveil.Gameplay.Quests;
using Ashveil.Gameplay.Story;
using UnityEngine;
using UnityEngine.AI;
using RC = Ashveil.Data.RegisterCatalog;

namespace Ashveil.Networking
{
    /// <summary>
    /// Hauptquest der Menschen, Akt I „Das Register“ (13.10.2026) – Teil der Prüfung „spieltest weidepass“ (braucht Kareth v4 und die
    /// Westweiden: R6 endet bei Fenna am Kornwagen):
    ///   1. Daten: sechs Aufträge, EP-Summe, Kette, Szenenkennzeichen, nur Menschen; Figuren Veit, Bralt, Nessa, Ansa in der Szene und
    ///      erreichbar; Fundstellen.
    ///   2. Halle: Höhlenvolumen, Wegnetz Hof → Torgang → Halle → Treppe → Salzkeller → Truhe, Stehen in den Räumen, Bilder.
    ///   3. Durchspielen über den echten Client-Weg: R1 (Reihenfolge Brett vor Buch), R2 (Register mitgegeben und abgegeben, Regal),
    ///      R3, R4 (Truhe), R5 (Marke „gestanden“), R6 über die Zonengrenze zu Fenna (Seite bleibt bei ihr, Marke).
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        private IEnumerator RegisterSuite(NetworkInventory a)
        {
            var pa = a.GetComponent<NetworkProgression>();
            if (pa == null) { Check(false, "Spieler hat Fortschritt"); yield break; }
            _grQuestXp = _grTopUp = _grKillXp = _grDiscoveryXp = 0;

            // ---------------------------------------------------------- Daten
            Line("");
            Line("[Akt I „Das Register“: Daten]");
            var defs = RC.Quests.Select(GameData.Quest).ToList();
            Check(defs.All(d => d != null), $"Alle {RC.Quests.Length} Aufträge vorhanden ({defs.Count(d => d != null)})");
            if (defs.Any(d => d == null)) yield break;
            Check(defs.Sum(d => d.xp) == RC.QuestXpTotal, $"EP-Summe {defs.Sum(d => d.xp)} (Soll {RC.QuestXpTotal})");
            Check(GrPrereqs(RC.R2, RC.R1) && GrPrereqs(RC.R3, RC.R2) && GrPrereqs(RC.R4, RC.R3) && GrPrereqs(RC.R5, RC.R4) && GrPrereqs(RC.R6, RC.R5) && GrPrereqs(RC.R1),
                "Kette R1 → R2 → R3 → R4 → R5 → R6");
            Check(defs.Take(5).All(d => d.requiredSceneTag == KarethCatalog.SceneTag) && defs[5].requiredSceneTag == WestweidenCatalog.SceneTag,
                "R1–R5 in Kareth v4, R6 nur mit den Westweiden");
            Check(defs.All(d => d.requiredRace == RaceCatalog.HumanId), "Nur für Menschen (Hauptquest je Volk)");
            Check(pa.ServerGate()?.Race == RaceCatalog.HumanId, $"Testfigur ist ein Mensch ({pa.ServerGate()?.Race})");
            string[] figuren = { RC.Veit, RC.Bralt, RC.Nessa, RC.Ansa };
            string fehlt = string.Join(", ", figuren.Where(n => GameData.Npc(n) == null || NpcIdentity.Find(n) == null));
            Check(fehlt.Length == 0, "Veit, Bralt, Nessa und Mutter Ansa in Daten und Szene" + (fehlt.Length > 0 ? " – fehlt: " + fehlt : ""));
            string abseits = string.Join(", ", figuren.Select(NpcIdentity.Find).Where(n => n != null &&
                !NavMesh.SamplePosition(n.transform.position, out NavMeshHit _, 2.5f, NavMesh.AllAreas)).Select(n => n.name));
            Check(abseits.Length == 0, "Die vier stehen erreichbar (NavMesh ≤ 2,5 m)" + (abseits.Length > 0 ? " – abseits: " + abseits : ""));
            int anker = RC.Nodes.Count(n => StoryAnchor.Find(n.Action) != null);
            Check(anker == RC.Nodes.Count, $"Fundstellen in der Szene ({anker}/{RC.Nodes.Count}: Zählbrett, Register, Regal, Truhe)");

            // ---------------------------------------------------------- Halle: Bau und Wegnetz
            Line("");
            Line("[Halle des Hauptmanns: Bau und Wegnetz]");
            var punkte = new List<StoryAnchor>();
            for (int i = 0; i < 20; i++)
            {
                StoryAnchor p = StoryAnchor.Find("halle.p." + i);
                if (p == null) break;
                punkte.Add(p);
            }
            Check(punkte.Count >= 8, $"Prüfpunkte ({punkte.Count})");
            string Kurz(StoryAnchor s) => s.name.Replace("Prüfpunkt Halle: ", "");
            var path = new NavMeshPath();
            for (int i = 0; i + 1 < punkte.Count; i++)
            {
                bool a0 = NavMesh.SamplePosition(punkte[i].Position, out NavMeshHit h0, 1.6f, NavMesh.AllAreas);
                bool a1 = NavMesh.SamplePosition(punkte[i + 1].Position, out NavMeshHit h1, 1.6f, NavMesh.AllAreas);
                bool ok = a0 && a1 && NavMesh.CalculatePath(h0.position, h1.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                float l = 0f;
                if (ok) for (int k = 1; k < path.corners.Length; k++) l += Vector3.Distance(path.corners[k - 1], path.corners[k]);
                Check(ok, $"Weg {Kurz(punkte[i])} → {Kurz(punkte[i + 1])}" +
                          (ok ? $" ({l:0} m)" : $" – NavMesh {(a0 ? "" : "fehlt am Start ")}{(a1 ? "" : "fehlt am Ziel ")}{path.status}"));
            }
            foreach (int i in new[] { 2, 5, 6 })
            {
                if (i >= punkte.Count) break;
                Vector3 p = punkte[i].Position + Vector3.up * 0.3f;
                yield return Ask(a, "goto", V(p, 0f));
                yield return new WaitForSecondsRealtime(2.5f);
                Vector3 q = a.transform.position;
                Check(Vector3.Distance(q, p) < 4f && Mathf.Abs(q.y - punkte[i].Position.y) < 1.5f && HoehlenVolumen.Enthaelt(q),
                    $"Steht im Fels: {Kurz(punkte[i])} (y {q.y:0.0}, Soll {punkte[i].Position.y:0.0}, nicht an die Oberfläche gehoben)");
            }
            string ordner = ProjectPaths.Logs("Kareth", "Halle_" + System.DateTime.Now.ToString("HHmm"));
            System.IO.Directory.CreateDirectory(ordner);
            foreach ((int i, float yaw, string name) in new[]
                     {
                         // Lauf 501: die Kamera blickt dem Gierwinkel der Figur entgegen – darum 0° = Blick nach Süden (in den Fels)
                         (0, 0f, "1_Hof_Blick_auf_das_Tor"), (1, 0f, "2_Torgang"), (2, 20f, "3_Halle_Herd_Regal"), (2, 300f, "4_Halle_Treppe"),
                         (4, 0f, "5_Treppe_hinab"), (6, 0f, "6_Salzkeller_Truhe"), (5, 120f, "7_Salzkeller_Blick_West")
                     })
            {
                if (i >= punkte.Count) continue;
                yield return Ask(a, "goto", V(punkte[i].Position + Vector3.up * 0.3f, yaw));
                yield return new WaitForSecondsRealtime(2.5f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(ordner, "HA_" + name + ".png"));
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Info("Bilder: " + ordner);

            // ---------------------------------------------------------- R1–R3: Veit
            Line("");
            Line("[R1–R3: Schreiber Veit]");
            yield return GrAccept(a, pa, RC.R1);
            yield return GrTake(a, pa, RC.NodeBook, false);
            Check(pa.ServerStoryNote.StartsWith(RC.NodeBook + ": erst") && pa.ServerProgress(RC.R1, 1) == 0,
                $"Erst das Brett, dann das Buch ({pa.ServerStoryNote})");
            yield return GrTake(a, pa, RC.NodeBoard, false);
            yield return GrTake(a, pa, RC.NodeBook, false);
            Check(pa.ServerProgress(RC.R1, 0) == 1 && pa.ServerProgress(RC.R1, 1) == 1, $"Kerben und Namen gezählt ({pa.ServerStoryNote})");
            yield return GrTurnIn(a, pa, RC.R1);
            yield return GrAccept(a, pa, RC.R2);
            Check(a.ServerModel.Count(RC.ItemRegister) == 1, "Veit gibt das Register mit");
            yield return GrTake(a, pa, RC.NodeShelf, false);
            Check(pa.ServerProgress(RC.R2, 0) == 1, $"Die alten Bände in der Halle angesehen ({pa.ServerStoryNote})");
            yield return GrTurnIn(a, pa, RC.R2);
            Check(a.ServerModel.Count(RC.ItemRegister) == 0, "Das Register bleibt bei Bralt");
            yield return GrAccept(a, pa, RC.R3);
            yield return GrTurnIn(a, pa, RC.R3);

            // ---------------------------------------------------------- R4–R5: Nessa
            Line("");
            Line("[R4–R5: Heilerin Nessa]");
            yield return GrAccept(a, pa, RC.R4);
            yield return GrTake(a, pa, RC.NodeChest, false);
            Check(pa.ServerProgress(RC.R4, 0) == 1, $"Truhe im Salzkeller gefunden ({pa.ServerStoryNote})");
            yield return GrTurnIn(a, pa, RC.R4);
            yield return GrAccept(a, pa, RC.R5);
            yield return GrTurnIn(a, pa, RC.R5);
            Check(pa.ServerGate().HasFlag(RC.FlagConfession), "Marke: Bralt hat gestanden");

            // ---------------------------------------------------------- R6: über die Zonengrenze zu Fenna
            Line("");
            Line("[R6: Wem die Seite gehört]");
            yield return GrAccept(a, pa, RC.R6);
            Check(a.ServerModel.Count(RC.ItemSealedPage) == 1, "Bralt gibt die versiegelte Seite mit");
            WorldStreamer ws = WorldStreamer.Instance;
            if (ws != null) yield return Preload(ws, new Vector3(2350f - 6450f, 0f, 470f - 3140f), 60f);
            yield return GrTurnIn(a, pa, RC.R6);
            Check(a.ServerModel.Count(RC.ItemSealedPage) == 0 && pa.ServerGate().HasFlag(RC.FlagFennasPage), "Die Seite bleibt ungelesen bei Fenna (Marke)");

            Line("");
            Info($"EP aus Akt I im Test: {_grQuestXp} (Soll {RC.QuestXpTotal}), Nachhilfe {_grTopUp}, Entdeckung {_grDiscoveryXp}");
        }
    }
}
