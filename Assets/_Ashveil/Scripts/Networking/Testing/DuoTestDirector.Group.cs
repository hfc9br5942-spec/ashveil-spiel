using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using FishNet.Object;
using UnityEngine;
using UnityEngine.AI;

namespace Ashveil.Networking
{
    /// <summary>
    /// Abnahmetest Milestone 5.5: vier Spieler spielen gemeinsam (Editor-Host A + drei Test-Clients B, C, D).
    /// Gruppe bilden, Gruppenchat, gemeinsame Reise ueber das Kareth-Terrain (mit Server-Vorhersage), gemeinsamer
    /// Kampf im Holzfaellerlager mit geteiltem Auftrag, persoenliche Beute fuer alle, Begegnung mit dem Glockenhirten,
    /// Wiederverbinden und ein Dauerbetrieb (4 min, oder bis 30 min Gesamtzeit im Modus "group30").
    /// Start: "Ashveil &gt; Test &gt; Gruppenabenteuer (4 Spieler)". Bericht: Logs/Ashveil_GroupTest.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        private static readonly string[] GroupProfiles = { "group-a", "group-b", "group-c", "group-d" };
        private const string GroupQuest = "quest.the_faded";

        private readonly Dictionary<string, string> _replies = new Dictionary<string, string>();

        /// <summary>Wie Ask, aber das Ergebnis landet je Spieler (fuer gleichzeitige Befehle an mehrere Spieler).</summary>
        private IEnumerator AskFor(NetworkInventory who, string cmd, string arg = "", float timeout = 15f)
        {
            if (who == null) yield break;
            var agent = who.GetComponent<SelfTestAgent>();
            if (agent == null) yield break;
            string key = who.ProfileId;
            _replies.Remove(key);
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
                _replies[key] = r.data;
                Replies.Remove(token);
            }
            else _replies[key] = "timeout";
        }

        /// <summary>Allen denselben Befehl gleichzeitig geben und auf alle Antworten warten.</summary>
        private IEnumerator AskAll(IList<NetworkInventory> who, System.Func<NetworkInventory, int, string> cmd, System.Func<NetworkInventory, int, string> arg)
        {
            var running = new List<Coroutine>();
            for (int i = 0; i < who.Count; i++)
                if (who[i] != null) running.Add(StartCoroutine(AskFor(who[i], cmd(who[i], i), arg(who[i], i))));
            foreach (Coroutine c in running) yield return c;
        }

        private static float ParseMaxCorr(string data)
        {
            ParsePos(data, out _, out _, out float m);
            return m;
        }

        private IEnumerator GroupSuite(bool longRun)
        {
            float suiteStart = Time.realtimeSinceStartup;
            // ---------------------------------------------------------- Anmeldung
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(GroupProfiles[0])) != null, 30f);
            Check(a != null, "Spieler A (Editor-Host) angemeldet");
            if (a == null) yield break;
            string exe = Ashveil.Core.ProjectPaths.TestClientExe;
            if (!File.Exists(exe)) { Check(false, "Test-Client vorhanden"); yield break; }
            for (int i = 1; i < 4; i++) StartClient(exe, GroupProfiles[i]);
            yield return WaitUntil(() => GroupProfiles.All(p => OnlineInv(p) != null), 150f);
            var p4 = GroupProfiles.Select(OnlineInv).ToList();
            Check(p4.All(x => x != null), "Spieler B, C und D (drei Test-Clients) angemeldet – vier Spieler online");
            if (p4.Any(x => x == null)) yield break;
            yield return new WaitForSecondsRealtime(2f);
            string[] names = p4.Select(x => x.CharacterName).ToArray();
            Check(names.Distinct().Count() == 4, $"Vier verschiedene Charaktere: {string.Join(", ", names)}");
            Vector3 camp = GameObject.Find("PlayerSpawn") != null ? GameObject.Find("PlayerSpawn").transform.position : Vector3.zero;
            int errorsAtStart = _logErrors;

            // Server-Bildrate im Hintergrund messen
            float frameSum = 0f, worstFrame = 0f;
            int frames = 0;
            bool measuring = true;
            StartCoroutine(MeasureFrames());
            IEnumerator MeasureFrames()
            {
                while (measuring)
                {
                    float dt = Time.unscaledDeltaTime;
                    frameSum += dt;
                    frames++;
                    if (dt > worstFrame && frames > 30) worstFrame = dt;
                    yield return null;
                }
            }

            // ---------------------------------------------------------- Gruppe
            Line("");
            Line("[Gruppe und Chat]");
            yield return AskAll(p4, (p, i) => "goto", (p, i) => V(camp + new Vector3(-4.5f + i * 3f, 0f, -2f), 0f));
            for (int i = 1; i < 4; i++)
            {
                yield return Ask(a, "party-invite", names[i]);
                yield return Ask(p4[i], "party-respond", "1");
            }
            yield return new WaitForSecondsRealtime(1f);
            var party = PartyService.Of(GroupProfiles[0]);
            Check(party != null && party.Members.Count == 4, $"Gruppe mit vier Mitgliedern ({(party != null ? party.Members.Count : 0)})");
            bool allSee = true;
            foreach (NetworkInventory p in p4)
            {
                yield return Ask(p, "social");
                allSee &= names.All(n => _last.data.Contains(n));
            }
            Check(allSee, "Jeder sieht alle vier im Gruppenfenster");
            yield return Ask(p4[3], "chat", "1 - Auf zum Holzfaellerlager!");
            yield return new WaitForSecondsRealtime(0.6f);
            bool chatOk = true;
            for (int i = 0; i < 3; i++)
            {
                yield return Ask(p4[i], "social");
                chatOk &= _last.data.Contains($"1:{names[3]}:Auf zum Holzfaellerlager!");
            }
            Check(chatOk, "Gruppenchat von D kommt bei A, B und C an");

            // ---------------------------------------------------------- Reise
            Line("");
            Line("[Gemeinsame Reise zum Holzfaellerlager]");
            Vector3 lumber = Gnd(-250f, 240f);
            var path = new NavMeshPath();
            NavMesh.SamplePosition(camp, out NavMeshHit from, 5f, NavMesh.AllAreas);
            NavMesh.SamplePosition(lumber + new Vector3(30f, 0f, -25f), out NavMeshHit to, 10f, NavMesh.AllAreas);
            bool hasPath = NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            Check(hasPath, $"Weg zum Holzfaellerlager gefunden ({path.corners.Length} Wegpunkte)");
            // Wegpunkte alle ~40 m, an jedem laufen alle vier 3 s gemeinsam weiter (echtes Laufen mit Vorhersage)
            var legs = new List<Vector3>();
            if (hasPath)
                for (int i = 0; i + 1 < path.corners.Length; i++)
                {
                    Vector3 s = path.corners[i], e = path.corners[i + 1];
                    int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(s, e) / 40f));
                    for (int k = 0; k < n; k++) legs.Add(Vector3.Lerp(s, e, k / (float)n));
                }
            float worstCorr = 0f, worstGap = 0f;
            int walked = 0;
            foreach (Vector3 leg in legs.Take(12))
            {
                int idx = legs.IndexOf(leg);
                Vector3 next = idx + 1 < legs.Count ? legs[idx + 1] : to.position;
                float yaw = YawTo(leg, next);
                Vector3 side = Quaternion.Euler(0f, yaw + 90f, 0f) * Vector3.forward;
                yield return AskAll(p4, (p, i) => "goto", (p, i) => V(Gnd(leg.x + side.x * (i - 1.5f) * 2f, leg.z + side.z * (i - 1.5f) * 2f), yaw));
                yield return AskAll(p4, (p, i) => "walk", (p, i) => "3 1");
                foreach (NetworkInventory p in p4)
                {
                    string d = _replies.TryGetValue(p.ProfileId, out string v) ? v : "";
                    ParsePos(d, out Vector3 cp, out _, out float mc);
                    worstCorr = Mathf.Max(worstCorr, mc);
                    if (d.StartsWith("pos=")) worstGap = Mathf.Max(worstGap, Vector3.Distance(cp, p.transform.position));
                }
                walked++;
            }
            Check(walked >= 6, $"Vier Spieler laufen gemeinsam {walked} Etappen durch Wald und Huegel");
            Check(worstGap < 0.6f, $"Alle Clients stimmen mit dem Server ueberein (groesster Abstand {worstGap:0.00} m)");
            Check(worstCorr < 1.5f, $"Vorhersage haelt auch zu viert (groesste Korrektur {worstCorr:0.00} m)");

            // ---------------------------------------------------------- Kampf + Auftrag
            Line("");
            Line("[Gemeinsamer Kampf und geteilter Auftrag]");
            var progs = p4.Select(p => p.GetComponent<NetworkProgression>()).ToList();
            foreach (NetworkProgression pr in progs) pr.ServerDebugStartQuest(GroupQuest);
            yield return new WaitForSecondsRealtime(0.5f);
            int[] xp0 = progs.Select(pr => pr.ServerXp).ToArray();
            int[] lvl0 = p4.Select(p => p.ServerModel.Level).ToArray();
            int[] items0 = p4.Select(p => p.ServerModel.Bag.Where(s => !string.IsNullOrEmpty(s.id)).Sum(s => s.count)).ToArray();
            var corpses = new List<GameObject>();
            float fightT = 0f;
            int kills = 0;
            while (kills < 4 && fightT < 240f)
            {
                EnemyBrain husk = EnemyBrain.All
                    .Where(eb => eb != null && eb.Definition != null && eb.Definition.Id == "enemy.faded_one" && eb.GetComponent<Health>().IsAlive)
                    .OrderBy(eb => Flat(eb.transform.position, lumber)).FirstOrDefault();
                if (husk == null) { fightT += 1f; yield return new WaitForSecondsRealtime(1f); continue; }
                var hh = husk.GetComponent<Health>();
                string id = husk.GetComponent<NetworkObject>().ObjectId.ToString();
                float oneFight = 0f;
                while (hh.IsAlive && oneFight < 45f)
                {
                    Vector3 w = husk.transform.position;
                    yield return AskAll(p4, (p, i) => "goto", (p, i) =>
                    {
                        float ang = i * 90f + 45f;
                        Vector3 o = Quaternion.Euler(0f, ang, 0f) * Vector3.forward * 1.8f;
                        Vector3 g = Gnd(w.x + o.x, w.z + o.z);
                        return V(g, YawTo(g, w));
                    });
                    yield return AskAll(p4, (p, i) => "attack", (p, i) => id);
                    float w8 = 0f;
                    while (hh.IsAlive && w8 < 4f) { w8 += 0.25f; yield return new WaitForSecondsRealtime(0.25f); }
                    oneFight += 4f + 2f;
                    // Testhilfe wie in den anderen Tests: niemand soll mitten im Test fallen
                    foreach (NetworkInventory p in p4)
                    {
                        var ph = p.GetComponent<Health>();
                        if (ph != null && ph.IsAlive && ph.Fraction < 0.45f) ph.Heal(ph.Max, null);
                    }
                }
                fightT += oneFight;
                if (!hh.IsAlive)
                {
                    kills++;
                    corpses.Add(husk.gameObject);
                }
            }
            yield return AskAll(p4, (p, i) => "stop", (p, i) => "");
            Check(kills >= 4, $"Die Gruppe besiegt {kills} Verblichene ({fightT:0} s)");
            bool shared = progs.All(pr => pr.ServerProgress(GroupQuest, 0) >= Mathf.Min(4, kills));
            Check(shared, "Toetungsziel zaehlt fuer alle vier: " + string.Join(", ", progs.Select(pr => pr.ServerProgress(GroupQuest, 0))));
            bool allXp = true;
            for (int i = 0; i < 4; i++) allXp &= progs[i].ServerXp > xp0[i] || p4[i].ServerModel.Level > lvl0[i];
            Check(allXp, "Alle vier bekommen Erfahrung");
            bool allEligible = corpses.All(c => c != null && c.GetComponent<CorpseLoot>() != null &&
                                               GroupProfiles.All(pid => c.GetComponent<CorpseLoot>().IsRecipient(pid)));
            Check(allEligible, "Jede Leiche hat persoenliche Beute fuer alle vier");
            foreach (GameObject c in corpses)
                foreach (NetworkInventory p in p4)
                    yield return LootAll(p, c);
            int[] items1 = p4.Select(p => p.ServerModel.Bag.Where(s => !string.IsNullOrEmpty(s.id)).Sum(s => s.count)).ToArray();
            int gained = Enumerable.Range(0, 4).Count(i => items1[i] > items0[i]);
            Check(gained >= 3, $"Beute landet in den eigenen Rucksaecken ({gained}/4 haben etwas bekommen: " +
                               string.Join(", ", Enumerable.Range(0, 4).Select(i => $"+{items1[i] - items0[i]}")) + ")");
            // Einzigartiges, das schon im Rucksack liegt, bleibt in der Leiche („Davon kannst du nur einen besitzen.“, wie in Classic).
            // Am 07.10. bekam ein Spieler zwei Verblichene Briefe (8 % je Leiche) – das ist kein Verlust.
            int uniqueLeft = 0;
            bool leftovers = corpses.All(c => c == null || Enumerable.Range(0, 4).All(i =>
            {
                var rest = c.GetComponent<CorpseLoot>().Get(GroupProfiles[i]);
                int owned = rest.Count(s => IsOwnedUnique(p4[i], s));
                uniqueLeft += owned;
                return rest.Count == owned;
            }));
            Check(leftovers, "Jeder hat nur seine eigene Beute genommen – nichts doppelt, nichts verloren" +
                             (uniqueLeft > 0 ? $" ({uniqueLeft}× Einzigartiges blieb liegen, schon im Rucksack)" : ""));
            if (!leftovers)
                foreach (GameObject c in corpses.Where(c => c != null))
                    for (int i = 0; i < 4; i++)
                    {
                        int left = c.GetComponent<CorpseLoot>().Get(GroupProfiles[i]).Count;
                        if (left > 0)
                            Line($"        Rest: Leiche bei {c.transform.position.x:0.0}/{c.transform.position.y:0.0}/{c.transform.position.z:0.0}, " +
                                 $"Spieler {i} ({left} Teile) steht {Vector3.Distance(p4[i].transform.position, c.transform.position):0.0} m entfernt");
                    }

            // ---------------------------------------------------------- Glockenhirte
            Line("");
            Line("[Begegnung mit dem Glockenhirten]");
            EnemyBrain boss = Brains("enemy.bell_shepherd").FirstOrDefault();
            if (boss != null)
            {
                Vector3 bp = boss.transform.position;
                Vector3 front = Beside(bp, camp, 10f);
                yield return AskAll(p4, (p, i) => "goto", (p, i) => V(Gnd(front.x + (i - 1.5f) * 2.5f, front.z), YawTo(front, bp)));
                float t = 0f;
                while (t < 15f && boss.State != EnemyState.Chase && boss.State != EnemyState.Attack) { t += 0.25f; yield return new WaitForSecondsRealtime(0.25f); }
                Check(boss.State == EnemyState.Chase || boss.State == EnemyState.Attack, $"Der Glockenhirte stellt sich der Gruppe ({t:0.0} s)");
                yield return new WaitForSecondsRealtime(4f);
                // Flucht ins Lager
                yield return AskAll(p4, (p, i) => "goto", (p, i) => V(camp + new Vector3(-4.5f + i * 3f, 0f, 2f), 0f));
                t = 0f;
                // Der Koloss ist langsam: je nach Verfolgungsweg dauert der Rueckweg zur Route gut eine Minute
                while (t < 120f && boss.State != EnemyState.Idle && boss.State != EnemyState.Wander) { t += 0.5f; yield return new WaitForSecondsRealtime(0.5f); }
                Check(boss.State == EnemyState.Idle || boss.State == EnemyState.Wander, $"Gruppe flieht, er kehrt zu seiner Route zurueck ({t:0} s)");
                if (boss.State != EnemyState.Idle && boss.State != EnemyState.Wander)
                {
                    var agent = boss.GetComponent<UnityEngine.AI.NavMeshAgent>();
                    Line($"        Glockenhirte: Ziel {boss.Home.x:0}/{boss.Home.z:0} ({Flat(boss.transform.position, boss.Home):0} m), " +
                         (agent != null ? $"Pfad {agent.pathStatus}, Rest {agent.remainingDistance:0.0} m, Tempo {agent.velocity.magnitude:0.0} m/s" : "kein Agent"));
                    Line($"        Glockenhirte: Zustand {boss.State}, Position {boss.transform.position.x:0}/{boss.transform.position.z:0}, " +
                         $"naechster Spieler {p4.Min(p => Vector3.Distance(p.transform.position, boss.transform.position)):0} m");
                }
                yield return new WaitForSecondsRealtime(7f); // Gefallene wiederbeleben lassen
                Check(p4.All(p => p.GetComponent<Health>().IsAlive), "Alle vier leben (Gefallene am Wachtfeuer wiederbelebt)");
            }
            else Check(false, "Glockenhirte gefunden");

            // ---------------------------------------------------------- Wiederverbinden
            Line("");
            Line("[Wiederverbinden]");
            NetworkInventory dOld = p4[3];
            yield return Ask(dOld, "reconnect");
            yield return WaitUntil(() => OnlineInv(GroupProfiles[3]) != null && OnlineInv(GroupProfiles[3]) != dOld, 45f);
            NetworkInventory d2 = OnlineInv(GroupProfiles[3]);
            Check(d2 != null && d2 != dOld, "D verbindet sich neu");
            if (d2 != null)
            {
                p4[3] = d2;
                yield return new WaitForSecondsRealtime(1.5f);
                yield return Ask(d2, "social");
                Check(names.All(n => _last.data.Contains(n)), "D ist wieder in der Gruppe");
                Check(d2.GetComponent<NetworkProgression>().ServerProgress(GroupQuest, 0) >= Mathf.Min(4, kills), "Auftragsfortschritt von D ist erhalten");
            }

            // ---------------------------------------------------------- Dauerbetrieb
            Line("");
            float soak = longRun ? Mathf.Max(60f, 30f * 60f - (Time.realtimeSinceStartup - suiteStart)) : 240f;
            Line($"[Dauerbetrieb {soak / 60f:0.0} min]");
            var spots = new[] { camp, Gnd(-42f, 70f), Gnd(20f, 140f), Gnd(110f, -40f), Gnd(-150f, -120f), Gnd(60f, 250f) };
            var rng = new System.Random(55);
            int cycles = 0, disconnects = 0, chats = 0, soakKills = 0;
            float soakCorr = 0f;
            float soakStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - soakStart < soak)
            {
                cycles++;
                Vector3 spot = spots[rng.Next(spots.Length)];
                float yaw = rng.Next(360);
                yield return AskAll(p4, (p, i) => "goto", (p, i) => V(Gnd(spot.x + (i - 1.5f) * 3f, spot.z + 2f), yaw));
                yield return AskAll(p4, (p, i) => "walk", (p, i) => $"{2 + (i % 3)} 1");
                foreach (NetworkInventory p in p4)
                    if (_replies.TryGetValue(p.ProfileId, out string d)) soakCorr = Mathf.Max(soakCorr, ParseMaxCorr(d));
                if (cycles % 2 == 0)
                {
                    int who = rng.Next(4);
                    yield return Ask(p4[who], "chat", $"1 - Runde {cycles}, alles ruhig?");
                    chats++;
                }
                // Jeder dritte Durchgang: ein Wolf am Kiefernhang
                if (cycles % 3 == 0)
                {
                    EnemyBrain wolf = NearestBrain("enemy.ash_wolf", Gnd(-42f, 62f));
                    if (wolf != null && Flat(wolf.transform.position, camp) < 150f)
                    {
                        var wh = wolf.GetComponent<Health>();
                        string wid = wolf.GetComponent<NetworkObject>().ObjectId.ToString();
                        Vector3 w = wolf.transform.position;
                        yield return AskAll(p4, (p, i) => "goto", (p, i) => V(Gnd(w.x + (i - 1.5f) * 1.5f, w.z - 1.5f), 0f));
                        yield return AskAll(p4, (p, i) => "attack", (p, i) => wid);
                        float wt = 0f;
                        while (wh.IsAlive && wt < 10f) { wt += 0.25f; yield return new WaitForSecondsRealtime(0.25f); }
                        if (!wh.IsAlive) soakKills++;
                        yield return AskAll(p4, (p, i) => "stop", (p, i) => "");
                    }
                }
                foreach (NetworkInventory p in p4)
                {
                    var ph = p.GetComponent<Health>();
                    if (ph != null && ph.IsAlive && ph.Fraction < 0.45f) ph.Heal(ph.Max, null);
                }
                for (int i = 0; i < 4; i++)
                {
                    NetworkInventory now = OnlineInv(GroupProfiles[i]);
                    if (now == null || now != p4[i]) { disconnects++; if (now != null) p4[i] = now; }
                }
                yield return new WaitForSecondsRealtime(3f);
            }
            measuring = false;
            float avgFps = frames > 0 ? frames / Mathf.Max(0.001f, frameSum) : 0f;
            Check(disconnects == 0, $"{cycles} Runden: keine ungewollte Trennung ({disconnects})");
            Check(soakCorr < 1.5f, $"Bewegung bleibt stabil (groesste Korrektur {soakCorr:0.00} m)");
            Check(_logErrors == errorsAtStart, $"Keine Fehler im Server-Log waehrend des Spielens ({_logErrors - errorsAtStart})");
            Check(avgFps > 20f, $"Server (Editor-Host) laeuft fluessig: {avgFps:0} Bilder/s im Schnitt, laengstes Bild {worstFrame * 1000f:0} ms");
            Info($"{chats} Gruppennachrichten, {soakKills} Woelfe gemeinsam besiegt, Gesamtdauer {(Time.realtimeSinceStartup - suiteStart) / 60f:0.0} min");

            // ---------------------------------------------------------- Speichern
            Line("");
            Line("[Speichern]");
            yield return AskAll(p4, (p, i) => "goto", (p, i) => V(camp + new Vector3(-4.5f + i * 3f, 0f, -2f), 0f));
            yield return new WaitForSecondsRealtime(1f);
            CharacterStore.FlushSaves();
            bool saved = GroupProfiles.All(pid => File.Exists(Path.Combine(CharacterStore.SavesRoot, pid + ".json")));
            Check(saved, "Alle vier Charaktere sind gespeichert");
            yield return Ask(a, "party-leave");
        }

        /// <summary>Einzigartiger Gegenstand, den der Spieler schon besitzt (kann nicht ein zweites Mal aufgehoben werden).</summary>
        private static bool IsOwnedUnique(NetworkInventory p, Ashveil.Data.ItemStack s)
        {
            if (p == null || string.IsNullOrEmpty(s.id)) return false;
            var d = p.ServerModel.Def(s);
            return d != null && d.unique && p.ServerModel.Count(s.id) > 0;
        }
    }
}
