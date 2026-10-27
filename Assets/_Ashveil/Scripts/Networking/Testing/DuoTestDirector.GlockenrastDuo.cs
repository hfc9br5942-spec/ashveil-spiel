using System.Collections;
using System.IO;
using System.Linq;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Story;
using FishNet;
using UnityEngine;
using UnityEngine.AI;
using C = Ashveil.Data.GlockenrastCatalog;

namespace Ashveil.Networking
{
    /// <summary>
    /// P3 Glockenrast (2 Spieler, 12h-Block 01.10.2026): Editor-Host A und Test-Client B spielen denselben Abschnitt nebeneinander.
    /// Geprueft wird, was nur mit zwei Figuren sichtbar wird:
    ///   · Entdeckungs-EP je Figur (B entdeckt die Glockenrast selbst, A's Entdeckung zaehlt nicht fuer B)
    ///   · Fundstuecke je Figur (beide heben ihr eigenes Ersatzrad auf, die Genommen-Marke gilt nur fuer die eigene Figur)
    ///   · gemeinsame Siege zaehlen fuer beide Auftraege (G2)
    ///   · Auftragsbeute je Empfaenger: nur wer „Gegengift“ hat, sieht Giftdruesen in derselben Leiche
    ///   · Speichern beider Figuren (v11)
    /// Start: "Ashveil › Test › Glockenrast (2 Spieler)". Bericht: Logs/…/Ashveil_GlockenrastDuoTest.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string GlockeDuoFlag = "glockenrast-duo";
        private static bool _glockeDuoMode;
        private const string GlockeDuoA = "duo-glocke-a", GlockeDuoB = "duo-glocke-b";

        private IEnumerator GlockenrastDuoSuite()
        {
            NetworkInventory a = null, b = null;
            yield return WaitUntil(() => (a = OnlineInv(GlockeDuoA)) != null, 30f);
            Check(a != null, "Spieler A (Editor-Host) angemeldet");
            if (a == null) yield break;
            string exe = Ashveil.Core.ProjectPaths.TestClientExe;
            if (!File.Exists(exe)) { Check(false, "Test-Client vorhanden"); yield break; }
            StartClient(exe, GlockeDuoB);
            yield return WaitUntil(() => (b = OnlineInv(GlockeDuoB)) != null, 120f);
            Check(b != null, "Spieler B (Test-Client) angemeldet");
            if (b == null) yield break;
            var pa = a.GetComponent<NetworkProgression>();
            var pb = b.GetComponent<NetworkProgression>();
            Check(pa != null && pb != null, "Beide haben Fortschritt (NetworkProgression)");
            if (pa == null || pb == null) yield break;
            yield return new WaitForSecondsRealtime(3f);
            a.GetComponent<Health>()?.SetInvulnerableFor(3600f);
            b.GetComponent<Health>()?.SetInvulnerableFor(3600f);
            Check(a.ServerModel.Level == 5 && b.ServerModel.Level == 5, $"Beide auf Stufe 5 (A {a.ServerModel.Level}, B {b.ServerModel.Level})");

            // ---------------------------------------------------------- G1 und Entdeckung je Figur
            Line("");
            Line("[G1 und Entdeckung je Figur]");
            yield return GrAccept(a, pa, C.G1);
            yield return GotoNpc(a, C.Anselm);
            yield return new WaitForSecondsRealtime(2.5f);
            Check(pa.ServerDiscovered(KarethQuestSitesHub), "A hat die Glockenrast entdeckt");
            Check(!pb.ServerDiscovered(KarethQuestSitesHub), "B noch nicht – die Entdeckung von A gilt nicht für B");
            yield return GrAccept(b, pb, C.G1);
            long b0 = GrTotal(b, pb);
            yield return GotoNpc(b, C.Anselm);
            yield return new WaitForSecondsRealtime(2.5f);
            long db = GrTotal(b, pb) - b0;
            Check(pb.ServerDiscovered(KarethQuestSitesHub) && db == Progression.DiscoveryXp(5),
                $"B entdeckt die Glockenrast selbst (+{db} EP, erwartet {Progression.DiscoveryXp(5)})");
            yield return GrTurnIn(a, pa, C.G1);
            yield return GrTurnIn(b, pb, C.G1);

            // ---------------------------------------------------------- G4 Fundstueck je Figur
            Line("");
            Line("[G4 Fundstück je Figur]");
            yield return GrAccept(a, pa, C.G4);
            yield return GrAccept(b, pb, C.G4);
            // Auftragsziel 1 ist das Erkunden des Holzfaellerlagers (Testfehler im ersten Lauf: fehlte, Abgabe abgelehnt)
            yield return GrExplore(a, pa, C.G4, 0, C.AreaLumberCamp);
            yield return GrExplore(b, pb, C.G4, 0, C.AreaLumberCamp);
            yield return GrTake(a, pa, C.NodeWheel, true);
            yield return GrTake(b, pb, C.NodeWheel, true);
            Check(a.ServerModel.Count(C.ItemSpareWheel) == 1 && b.ServerModel.Count(C.ItemSpareWheel) == 1,
                $"Jede Figur hat ihr eigenes Ersatzrad (A {a.ServerModel.Count(C.ItemSpareWheel)}, B {b.ServerModel.Count(C.ItemSpareWheel)})");
            yield return Ask(b, "story-act", C.NodeWheel);
            Check(b.ServerModel.Count(C.ItemSpareWheel) == 1, $"B kann es nicht zweimal aufheben ({pb.ServerStoryNote})");
            string taken = C.TakenFlag(C.NodeWheel);
            Check(pa.ServerGate() != null && pa.ServerGate().HasFlag(taken) && pb.ServerGate() != null && pb.ServerGate().HasFlag(taken),
                "Genommen-Marke je Figur gespeichert (A und B getrennt)");
            yield return GrTurnIn(a, pa, C.G4);
            yield return GrTurnIn(b, pb, C.G4);

            // ---------------------------------------------------------- G2 gemeinsame Siege
            Line("");
            Line("[G2 gemeinsame Siege]");
            yield return GrAccept(a, pa, C.G2);
            yield return GrAccept(b, pb, C.G2);
            yield return GrGotoSite(a, pa, "site.kareth.moorrand.west");
            yield return Ask(b, "goto", V(a.transform.position + new Vector3(2f, 0.3f, 0f), 0f));
            for (int i = 0; i < 8; i++) yield return GrSharedKill(a, b, C.EnemySpitter, 5, null);
            Check(pa.ServerProgress(C.G2, 0) == 8 && pb.ServerProgress(C.G2, 0) == 8,
                $"Gemeinsame Siege zählen für beide (A {pa.ServerProgress(C.G2, 0)}/8, B {pb.ServerProgress(C.G2, 0)}/8)");
            yield return GrTurnIn(a, pa, C.G2);
            yield return GrTurnIn(b, pb, C.G2);

            // ---------------------------------------------------------- G3 Auftragsbeute je Empfaenger
            Line("");
            Line("[G3 Auftragsbeute je Empfänger]");
            yield return GrAccept(a, pa, C.G3);
            Check(!pb.ServerIsActive(C.G3), "Nur A hat „Gegengift“ angenommen");
            yield return GrGotoSite(a, pa, "site.kareth.moorrand.ost");
            yield return Ask(b, "goto", V(a.transform.position + new Vector3(2f, 0.3f, 0f), 0f));
            int seenA = 0, leaksB = 0, both = 0;
            for (int i = 0; i < 12 && seenA < 2; i++)
                yield return GrSharedKill(a, b, C.EnemySpitter, 5, corpse =>
                {
                    if (corpse == null) return;
                    if (corpse.Get(a.ProfileId).Any(s => s.id == C.ItemVenomGland)) seenA++;
                    if (corpse.Get(b.ProfileId).Any(s => s.id == C.ItemVenomGland)) leaksB++;
                    both++;
                });
            Check(seenA > 0, $"A (mit Auftrag) findet Giftdrüsen ({seenA} von {both} Leichen)");
            Check(leaksB == 0, $"B (ohne Auftrag) findet in denselben Leichen keine ({leaksB})");

            // ---------------------------------------------------------- Speichern
            Line("");
            Line("[Speichern]");
            a.SaveNow();
            b.SaveNow();
            yield return new WaitForSecondsRealtime(1f);
            bool la = CharacterStore.Service.TryLoad(GlockeDuoA, out CharacterSaveData sa);
            bool lb = CharacterStore.Service.TryLoad(GlockeDuoB, out CharacterSaveData sb);
            Check(la && lb && sa.version == CharacterSaveData.CurrentVersion && sb.version == CharacterSaveData.CurrentVersion &&
                  sa.completedQuests.Contains(C.G4) && sb.completedQuests.Contains(C.G4) &&
                  sa.discoveredAreas != null && sb.discoveredAreas != null &&
                  sa.discoveredAreas.Contains(KarethQuestSitesHub) && sb.discoveredAreas.Contains(KarethQuestSitesHub) &&
                  sa.quests.Any(q => q.id == C.G3) && !sb.quests.Any(q => q.id == C.G3),
                $"Beide Speicherstände v{(la ? sa.version : 0)}/v{(lb ? sb.version : 0)}: eigene Aufträge und Entdeckungen");
        }

        /// <summary>Test-Gegner bei A: B trifft zuerst (beteiligt), A besiegt ihn – beide sind Empfaenger der Beute.</summary>
        private IEnumerator GrSharedKill(NetworkInventory a, NetworkInventory b, string enemyId, int level, System.Action<CorpseLoot> inspect)
        {
            EnemySpawnArea template = FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None)
                .FirstOrDefault(x => x.Definition != null && x.Definition.Id == enemyId && x.Prefab != null);
            if (template == null) { Check(false, "Gegner-Vorlage gefunden: " + enemyId); yield break; }
            Vector3 pos = a.transform.position + a.transform.forward * 3.5f;
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 6f, NavMesh.AllAreas)) pos = hit.position;
            GameObject go = Instantiate(template.Prefab, pos, Quaternion.identity);
            go.GetComponent<EnemyBrain>().Configure(level, pos, "glockenrast-duo-test");
            InstanceFinder.ServerManager.Spawn(go);
            yield return new WaitForSecondsRealtime(0.5f);
            var hp = go.GetComponent<Health>();
            if (hp == null) { Check(false, "Test-Gegner hat Leben"); yield break; }
            hp.ApplyDamage(new DamageInfo { Amount = 1f, Source = b.gameObject });
            yield return new WaitForSecondsRealtime(0.1f);
            hp.ApplyDamage(new DamageInfo { Amount = 1f, Source = a.gameObject });
            yield return new WaitForSecondsRealtime(0.1f);
            hp.ApplyDamage(new DamageInfo { Amount = hp.Max * 20f, Source = a.gameObject, Unblockable = true });
            yield return new WaitForSecondsRealtime(0.5f);
            if (hp.IsAlive) { Check(false, $"Test-Gegner {enemyId} besiegt"); yield break; }
            inspect?.Invoke(go.GetComponent<CorpseLoot>());
        }
    }
}
