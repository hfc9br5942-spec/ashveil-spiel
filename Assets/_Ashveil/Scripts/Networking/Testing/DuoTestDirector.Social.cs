using System.Collections;
using System.IO;
using System.Linq;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Abnahmetest Milestone 5 (Gruppe und Chat) mit drei echten Spielern: Editor-Host A + zwei Test-Clients B, C.
    /// Geprueft: Namen, Umgebungschat mit Rufweite, Fluestern, Gruppenchat, Textentschaerfung, Spamschutz,
    /// Einladen/Ablehnen/Annehmen, geteilte Erfahrung/Beute/Toetungsziele nur in der Naehe, Entfernen, Verlassen,
    /// Wiederverbinden. Start: "Ashveil &gt; Test &gt; Gruppe und Chat (3 Spieler)". Bericht: Logs/Ashveil_SocialTest.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        private static void ParsePos(string data, out Vector3 pos, out int corrections, out float maxCorrection)
        {
            pos = Vector3.zero;
            corrections = -1;
            maxCorrection = 0f;
            if (string.IsNullOrEmpty(data)) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (string part in data.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string k = part.Substring(0, eq), v = part.Substring(eq + 1);
                if (k == "pos")
                {
                    string[] p = v.Split(' ');
                    if (p.Length == 3) pos = new Vector3(float.Parse(p[0], ci), float.Parse(p[1], ci), float.Parse(p[2], ci));
                }
                else if (k == "corr") int.TryParse(v, out corrections);
                else if (k == "maxcorr") float.TryParse(v, System.Globalization.NumberStyles.Float, ci, out maxCorrection);
            }
        }

        private const string SocialA = "social-a", SocialB = "social-b", SocialC = "social-c";

        private IEnumerator SocialSuite()
        {
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(SocialA)) != null, 30f);
            Check(a != null, "Spieler A (Editor-Host) angemeldet");
            if (a == null) yield break;
            string exe = Ashveil.Core.ProjectPaths.TestClientExe;
            if (!File.Exists(exe)) { Check(false, "Test-Client vorhanden"); yield break; }
            StartClient(exe, SocialB);
            StartClient(exe, SocialC);
            NetworkInventory b = null, c = null;
            yield return WaitUntil(() => (b = OnlineInv(SocialB)) != null && (c = OnlineInv(SocialC)) != null, 120f);
            Check(b != null && c != null, "Spieler B und C (zwei Test-Clients) angemeldet");
            if (b == null || c == null) yield break;
            yield return new WaitForSecondsRealtime(2f);
            string nA = a.CharacterName, nB = b.CharacterName, nC = c.CharacterName;

            // ---------------------------------------------------------- Namen
            Line("");
            Line("[Namen]");
            Check(nA == CharacterStore.NameFor(SocialA) && nB == CharacterStore.NameFor(SocialB) && nC == CharacterStore.NameFor(SocialC),
                $"Charakternamen aus dem Speicherstand: {nA}, {nB}, {nC}");
            Check(nA != nB && nB != nC && nA != nC, "Namen sind verschieden");
            Check(b.GetComponent<NetworkPlayer>().DisplayName == nB, "Namensschild zeigt den Charakternamen");

            // ---------------------------------------------------------- Bewegung (Server-Vorhersage)
            Line("");
            Line("[Bewegung mit Server-Vorhersage]");
            GameObject spawnGo = GameObject.Find("PlayerSpawn");
            Vector3 start = (spawnGo != null ? spawnGo.transform.position : Vector3.zero) + new Vector3(4f, 0f, 0f);
            yield return Ask(b, "goto", V(start, 180f));
            yield return new WaitForSecondsRealtime(1f);
            Vector3 s0 = b.transform.position;
            yield return Ask(b, "walk", "2 1");
            Vector3 s1 = b.transform.position;
            float moved = Vector3.Distance(new Vector3(s0.x, 0f, s0.z), new Vector3(s1.x, 0f, s1.z));
            ParsePos(_last.data, out Vector3 c1, out int corr1, out float maxCorr1);
            Check(moved > 4f && moved < 16f, $"B laeuft 2 s: der Server bewegt B aus dessen Eingaben ({moved:0.0} m)");
            Check(Vector3.Distance(c1, s1) < 0.35f, $"Client und Server stimmen ueberein (Abstand {Vector3.Distance(c1, s1):0.00} m)");
            Check(maxCorr1 < 0.5f, $"Vorhersage trifft: groesste Korrektur beim Laufen {maxCorr1:0.00} m ({corr1} kleine Korrekturen)");
            yield return Ask(b, "cheat-offset", "20 0 0");
            yield return new WaitForSecondsRealtime(1.5f);
            Vector3 s2 = b.transform.position;
            yield return Ask(b, "pos");
            ParsePos(_last.data, out Vector3 c2, out _, out float maxCorr2);
            Check(Vector3.Distance(s2, s1) < 1f, "Betrugsversuch (Client versetzt sich 20 m): Server laesst B stehen");
            // Die Korrektur passiert gleich im naechsten Takt (Abgleich vor der eigenen Vorhersage)
            Check(Vector3.Distance(c2, s2) < 1f && maxCorr2 > 15f,
                $"Client wird auf den Server-Stand zurueckgesetzt (Abstand {Vector3.Distance(c2, s2):0.00} m, Ruck {maxCorr2:0.0} m)");

            // Aufstellung: A und B am Feuer, C weit weg
            GameObject spawn = GameObject.Find("PlayerSpawn");
            Vector3 camp = spawn != null ? spawn.transform.position : Vector3.zero;
            yield return Ask(a, "goto", V(camp + new Vector3(-3f, 0f, 0f), 90f));
            yield return Ask(b, "goto", V(camp + new Vector3(3f, 0f, 0f), -90f));
            Vector3 far = camp + new Vector3(-8f, 0f, 90f);
            if (UnityEngine.AI.NavMesh.SamplePosition(far, out UnityEngine.AI.NavMeshHit fh, 10f, UnityEngine.AI.NavMesh.AllAreas)) far = fh.position;
            yield return Ask(c, "goto", V(far, 0f));

            // ---------------------------------------------------------- Chat
            Line("");
            Line("[Chat]");
            yield return Ask(a, "chat", "0 - Hallo Wacht");
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Ask(b, "social");
            Check(_last.data.Contains($"0:{nA}:Hallo Wacht"), "B (in Rufweite) hoert A");
            yield return Ask(c, "social");
            Check(!_last.data.Contains("Hallo Wacht"), "C (90 m entfernt) hoert es nicht");
            yield return Ask(a, "chat", $"2 {nC} Kannst du mich hoeren?");
            Check(_last.data.Contains($"3:{nC}:Kannst du mich hoeren?"), "Fluestern: A sieht die gesendete Zeile");
            yield return Ask(c, "social");
            Check(_last.data.Contains($"2:{nA}:Kannst du mich hoeren?"), "Fluestern kommt ueber jede Entfernung bei C an");
            yield return Ask(b, "social");
            Check(!_last.data.Contains("Kannst du mich"), "B liest das Fluestern nicht mit");
            yield return Ask(a, "chat", "2 Niemandda Hallo");
            Check(_last.data.Contains("ist nicht online"), "Fluestern an unbekannten Namen: Hinweis");
            yield return Ask(a, "chat", "1 - Ohne Gruppe");
            Check(_last.data.Contains("in keiner Gruppe"), "Gruppenchat ohne Gruppe: Hinweis");
            yield return Ask(b, "chat", "0 - <b>fett</b> <color=red>rot</color>");
            Check(_last.data.Contains("(b)fett(/b)") && !_last.data.Contains("<b>"), "Formatierungs-Tags werden entschaerft");
            yield return new WaitForSecondsRealtime(5.5f);
            for (int i = 0; i < 6; i++) yield return Ask(b, "chat", $"0 - Spam {i}");
            Check(_last.data.Contains("Nicht so schnell"), "Spamschutz: sechste Nachricht in 5 s abgelehnt");

            // ---------------------------------------------------------- Gruppe
            Line("");
            Line("[Gruppe]");
            yield return Ask(a, "party-invite", nB);
            Check(_last.data.Contains($"Einladung an {nB}"), "A laedt B ein");
            yield return Ask(b, "social");
            Check(_last.data.Contains($"invite={nA}"), "B sieht die Einladung");
            yield return Ask(b, "party-respond", "1");
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Ask(a, "social");
            Check(_last.data.Contains($"*{nA}(ich)") && _last.data.Contains(nB + ","), "Gruppe A+B, A fuehrt");
            yield return Ask(a, "party-invite", nC);
            yield return Ask(c, "party-respond", "0");
            yield return Ask(a, "social");
            Check(_last.data.Contains($"{nC} lehnt die Einladung ab"), "C lehnt ab – A bekommt Bescheid");
            yield return Ask(b, "party-invite", nC);
            Check(_last.data.Contains("Nur die Gruppenleitung"), "Nur die Leitung darf einladen");
            yield return Ask(a, "party-invite", nC);
            yield return Ask(c, "party-respond", "1");
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Ask(c, "social");
            Check(_last.data.Contains(nA) && _last.data.Contains(nB) && _last.data.Contains($"{nC}(ich)"), "C ist in der Gruppe (3 Mitglieder)");
            yield return Ask(b, "chat", "1 - Sammeln am Feuer");
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Ask(c, "social");
            Check(_last.data.Contains($"1:{nB}:Sammeln am Feuer"), "Gruppenchat erreicht auch das ferne Mitglied");

            // ---------------------------------------------------------- Geteilter Fortschritt
            Line("");
            Line("[Geteilte Beute, Erfahrung und Ziele]");
            var pa = a.GetComponent<NetworkProgression>();
            var pb = b.GetComponent<NetworkProgression>();
            var pc = c.GetComponent<NetworkProgression>();
            EnemySpawnArea husks = FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None)
                .FirstOrDefault(x => x.Definition != null && x.Definition.Id == "enemy.faded_one" && x.Prefab != null);
            Check(husks != null, "Verblichenen-Vorlage gefunden");
            if (husks == null) yield break;
            const string KillQuest = "quest.the_faded";
            pa.ServerDebugStartQuest(KillQuest);
            pb.ServerDebugStartQuest(KillQuest);
            pc.ServerDebugStartQuest(KillQuest);
            yield return new WaitForSecondsRealtime(0.5f);
            int xpC0 = pc.ServerXp, lvlC0 = c.ServerModel.Level, xpB0 = pb.ServerXp;
            GameObject h1 = null;
            yield return SpawnAndKill(husks, 3, a, null, go => h1 = go);
            Check(h1 != null, "A besiegt einen Verblichenen (B steht daneben, C ist weit weg)");
            var corpse1 = h1 != null ? h1.GetComponent<CorpseLoot>() : null;
            Check(corpse1 != null && corpse1.IsRecipient(SocialB) && !corpse1.IsRecipient(SocialC), "Beute: B (nah) berechtigt, C (fern) nicht");
            Check(pb.ServerXp > xpB0 || b.ServerModel.Level > 5, "B bekommt Erfahrung ohne eigenen Treffer");
            Check(pc.ServerXp == xpC0 && c.ServerModel.Level == lvlC0, "C bekommt aus der Ferne nichts");
            Check(pa.ServerProgress(KillQuest, 0) == 1 && pb.ServerProgress(KillQuest, 0) == 1 && pc.ServerProgress(KillQuest, 0) == 0,
                "Toetungsziel: A 1, B 1, C 0");
            // C kommt dazu (in Reichweite, greift aber nicht an)
            yield return Ask(c, "goto", V(a.transform.position + new Vector3(0f, 0f, -8f), 0f));
            yield return new WaitForSecondsRealtime(1.2f);
            GameObject h2 = null;
            yield return SpawnAndKill(husks, 3, a, null, go => h2 = go);
            var corpse2 = h2 != null ? h2.GetComponent<CorpseLoot>() : null;
            Check(corpse2 != null && corpse2.IsRecipient(SocialC), "Jetzt in der Naehe: C ist beuteberechtigt");
            Check(pc.ServerXp > xpC0 || c.ServerModel.Level > lvlC0, "C bekommt Erfahrung");
            Check(pc.ServerProgress(KillQuest, 0) == 1 && pa.ServerProgress(KillQuest, 0) == 2, "Toetungsziel zaehlt fuer C mit (A 2, C 1)");

            // ---------------------------------------------------------- Wiederverbinden
            Line("");
            Line("[Wiederverbinden]");
            NetworkInventory bOld = b;
            bool sawOffline = false, keptMember = false;
            yield return Ask(b, "reconnect");
            for (float t = 0f; t < 10f; t += 0.05f)
            {
                if (OnlineInv(SocialB) == null || OnlineInv(SocialB) != bOld)
                {
                    sawOffline = true;
                    keptMember = PartyService.Of(SocialB) != null && PartyService.Of(SocialA) == PartyService.Of(SocialB);
                    break;
                }
                yield return new WaitForSecondsRealtime(0.05f);
            }
            Check(sawOffline && keptMember, "B getrennt: bleibt Gruppenmitglied (offline)");
            NetworkInventory b2 = null;
            yield return WaitUntil(() => (b2 = OnlineInv(SocialB)) != null && b2 != bOld, 40f);
            Check(b2 != null && b2 != bOld, "B ist wieder da");
            if (b2 == null) yield break;
            b = b2;
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Ask(b, "social");
            Check(_last.data.Contains($"*{nA}") && _last.data.Contains($"{nB}(ich)") && _last.data.Contains(nC), "B ist nach dem Wiederverbinden wieder in der Gruppe");

            // ---------------------------------------------------------- Entfernen und Verlassen
            Line("");
            Line("[Entfernen und Verlassen]");
            yield return Ask(b, "party-kick", nC);
            Check(_last.data.Contains("Nur die Gruppenleitung"), "Nur die Leitung darf entfernen");
            yield return Ask(a, "party-kick", nC);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Ask(c, "social");
            Check(_last.data.StartsWith("party=;") && _last.data.Contains("entfernt"), "C wurde entfernt und hat keine Gruppe mehr");
            yield return Ask(a, "social");
            Check(!_last.data.Contains(nC + ",") && _last.data.Contains(nB), "A+B bleiben zusammen");
            yield return Ask(a, "party-leave");
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Ask(b, "social");
            Check(_last.data.StartsWith("party=;"), "A geht – Gruppe mit nur B ist aufgeloest");
            Info($"Letzter Chat bei B: {_last.data.Replace("\n", " | ")}");
        }
    }
}
