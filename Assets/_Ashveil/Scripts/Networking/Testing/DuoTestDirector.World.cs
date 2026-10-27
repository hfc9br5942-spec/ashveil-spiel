using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using Ashveil.World;
using UnityEngine;
using UnityEngine.AI;

namespace Ashveil.Networking
{
    /// <summary>
    /// Large-World Phase G: Streaming-Test mit Editor-Host A und zwei Test-Clients B, C – Kareth + Grenzgebirge als
    /// eine Welt (Masterplan §220/221). Tunnel, Laden/Entladen mit Hysterese, Server-Vereinigung, Sturz unter die Welt,
    /// Speicherstand v5 mit Wiederverbinden in der Region, 5x hin und zurueck mit Speicher- und Frame-Messung.
    /// Start: "Ashveil &gt; Test &gt; Weltzellen (Streaming, 3 Spieler)". Bericht: Logs/Ashveil_WeltTest.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string WorldFlag = "welt";
        private static bool _worldMode;
        private const string WorldA = "duo-welt-a", WorldB = "duo-welt-b", WorldC = "duo-welt-c";
        private const string West = "gg_west", East = "gg_ost";

        private static bool TryPos(string reply, out Vector3 p)
        {
            p = Vector3.zero;
            int i = reply?.IndexOf("pos=", System.StringComparison.Ordinal) ?? -1;
            if (i < 0) return false;
            string[] parts = reply.Substring(i + 4).Split(';')[0].Split(' ');
            return parts.Length >= 3 &&
                   float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out p.x) &&
                   float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out p.y) &&
                   float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out p.z);
        }

        private static float ParseMem(string reply)
        {
            int i = reply?.IndexOf("mem=", System.StringComparison.Ordinal) ?? -1;
            return i >= 0 && float.TryParse(reply.Substring(i + 4).Split(';')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : -1f;
        }

        /// <summary>Punkt im Grenzgebirge-Kern (Entwurfskoordinaten) auf dem Boden der Welt.</summary>
        private static Vector3 GG(float lx, float lz, float lift = 0.3f)
        {
            Vector2 w = BorderMountainsFrame.ToWorld(lx, lz);
            return Gnd(w.x, w.y, lift);
        }

        /// <summary>Liegt ein Weltpunkt im Kern zwischen Entwurfs-x lx0 und lx1?</summary>
        private static bool InCore(Vector3 world, float lx0, float lx1)
        {
            Vector3 l = BorderMountainsFrame.ToLocal(world);
            return l.x >= lx0 && l.x < lx1 && l.z > -400f && l.z < 2100f;
        }

        /// <summary>Wartet, bis der Client die Zelle im gewuenschten Zustand meldet.</summary>
        private IEnumerator WaitClientCell(NetworkInventory who, string cell, string state, float timeout)
        {
            float t = 0f;
            while (t < timeout)
            {
                yield return Ask(who, "world");
                if (_last.ok && _last.data.Contains(cell + "=" + state)) yield break;
                t += 1f;
                yield return new WaitForSecondsRealtime(1f);
            }
        }

        /// <summary>Server laedt die Zelle am Ziel vor (angekuendigte Reise) und liefert den Boden dort.</summary>
        private IEnumerator Preload(WorldStreamer ws, Vector3 target, float timeout)
        {
            ws.RequestPreload(target, 30f);
            float t = 0f;
            while (!ws.IsReadyAt(target) && t < timeout)
            {
                t += 0.25f;
                yield return new WaitForSecondsRealtime(0.25f);
            }
        }

        private IEnumerator WorldSuite()
        {
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(WorldA)) != null, 30f);
            Check(a != null, "Spieler A (Editor-Host) angemeldet");
            if (a == null) yield break;
            string exe = Ashveil.Core.ProjectPaths.TestClientExe;
            if (!File.Exists(exe)) { Check(false, "Test-Client vorhanden"); yield break; }
            StartClient(exe, WorldB);
            StartClient(exe, WorldC);
            NetworkInventory b = null, c = null;
            yield return WaitUntil(() => (b = OnlineInv(WorldB)) != null && (c = OnlineInv(WorldC)) != null, 120f);
            Check(b != null && c != null, "Spieler B und C (Test-Clients) angemeldet");
            if (b == null || c == null) yield break;
            yield return new WaitForSecondsRealtime(2f);
            Vector3 camp = GameObject.Find("PlayerSpawn") != null ? GameObject.Find("PlayerSpawn").transform.position : Vector3.zero;

            // ---------------------------------------------------------- Weltverzeichnis
            Line("");
            Line("[Weltverzeichnis]");
            WorldStreamer ws = WorldStreamer.Instance;
            Check(ws != null && ws.Manifest != null, "Streamer mit Weltverzeichnis in der Welt-Ebene");
            if (ws == null || ws.Manifest == null) yield break;
            Check(ws.Manifest.FindCell(West) != null && ws.Manifest.FindCell(East) != null, "Zellen gg_west und gg_ost bekannt");
            Check(ws.StateOf(West) == WorldStreamer.CellState.Unloaded && ws.StateOf(East) == WorldStreamer.CellState.Unloaded,
                "Am Wachtfeuer ist das Grenzgebirge nicht geladen");
            var proxies = FindObjectsByType<WorldCellProxy>(FindObjectsSortMode.None);
            Check(proxies.Length == 2 && proxies.All(p => p.GetComponent<Renderer>().enabled), $"Fernansicht beider Zellen sichtbar ({proxies.Length})");

            // ---------------------------------------------------------- Osttunnel (Kareth) und Talpass-Tor (30.09.: Kern suedoestlich)
            Line("");
            Line("[Osttunnel und Talpass-Tor]");
            // im Rohr messen (von oben traefe der Strahl die Decke)
            float floor650 = Physics.Raycast(new Vector3(650f, 9f, 60f), Vector3.down, out RaycastHit fh, 8f) ? fh.point.y : float.NaN;
            Check(!float.IsNaN(floor650), $"Tunnelboden bei x = 650 ({floor650:0.0} m)");
            float lenTunnel = PathLength(Gnd(492f, 60f), new Vector3(650f, floor650, 60f), out _, 8f);
            Check(lenTunnel > 140f, $"NavMesh-Weg von Kareth in den Osttunnel ({lenTunnel:0} m)");
            Check(ws.StateOf(West) == WorldStreamer.CellState.Unloaded, "Der Osttunnel fuehrt ins Eschenfeld – das Grenzgebirge bleibt dabei ungeladen");

            // Vor dem Talpass-Tor (erzeugtes Umland, 150 m noerdlich der Nordkante des Kerns)
            Vector2 gate = BorderMountainsFrame.ToWorld(BorderMountainsFrame.LocalPivot.x + 55f, 60f);
            Vector3 portal = Gnd(gate.x, gate.y + 150f);
            float t0 = Time.realtimeSinceStartup;
            yield return Ask(b, "goto", V(portal, 180f));
            yield return WaitUntil(() => ws.StateOf(West) == WorldStreamer.CellState.Ready, 20f);
            Check(ws.StateOf(West) == WorldStreamer.CellState.Ready, $"Server laedt GG-West, sobald B vor dem Talpass-Tor steht ({Time.realtimeSinceStartup - t0:0.0} s)");
            yield return WaitClientCell(b, West, "Ready", 20f);
            Check(_last.data.Contains(West + "=Ready"), "B's Client hat GG-West geladen");
            Check(proxies.All(p => p.CellId != West || !p.GetComponent<Renderer>().enabled), "Fernansicht von GG-West ist aus, die echte Zelle da");
            yield return Ask(b, "goto", V(Gnd(gate.x, gate.y + 25f), 180f));
            yield return Ask(b, "walk", "9 1");
            bool walked = TryPos(_last.data, out Vector3 bp);
            float ground = Ashveil.Core.WorldGround.SampleHeight(bp, float.NaN);
            Check(walked && bp.z < BorderMountainsFrame.WorldPivot.y - 5f && Mathf.Abs(bp.y - ground) < 3f,
                $"B laeuft ueber die Naht ins Grenzgebirge (z {bp.z:0}, y {bp.y:0.0}, Boden {ground:0.0})");

            // NavMesh im Kern: Talpass-Eingang -> Lagerplatz
            Vector3 t2 = GG(832f, 196f);
            float lenT2 = PathLength(GG(680f, 62f), t2, out _, 8f);
            Check(lenT2 > 100f, $"NavMesh-Weg vom Talpass-Eingang zum Lagerplatz ({lenT2:0} m)");

            yield return Ask(b, "goto", V(t2, 45f));
            yield return new WaitForSecondsRealtime(2f);
            Check(Mathf.Abs(b.transform.position.y - Gnd(b.transform.position.x, b.transform.position.z, 0f).y) < 1.5f,
                $"B steht am Lagerplatz auf dem Gelaende (y {b.transform.position.y:0.0})");

            // Gegner je Zelle: erscheinen erst, wenn die Zelle bereit ist
            yield return new WaitForSecondsRealtime(1f);
            int westEnemies = Ashveil.Gameplay.Enemies.EnemyBrain.All.Count(e => e != null && InCore(e.transform.position, 600f, 1800f));
            // Seit dem Aktivierungsraster (11.10.2026) gibt es Gegner nur in Gebieten nahe bei Spielern: erwartet werden die
            // Gegner der Gebiete im Weckabstand um B (14.10.: die alte feste Erwartung „≥ 8“ zählte auch schlafende Gebiete mit)
            EnemyDirector dir = EnemyDirector.Instance;
            int erwartet = 0, wach = 0, gebiete = 0;
            foreach (var area in Object.FindObjectsByType<Ashveil.Gameplay.Enemies.EnemySpawnArea>(FindObjectsSortMode.None))
            {
                if (area == null || !InCore(area.transform.position, 600f, 1800f)) continue;
                gebiete++;
                Vector3 d = area.transform.position - b.transform.position; d.y = 0f;
                if (EnemyDirector.ActivationEnabled && dir != null && d.magnitude > dir.ActivateRange + area.Radius) continue;
                wach++;
                erwartet += dir != null ? dir.TargetCount(area) : area.Count;
            }
            Check(erwartet > 0 && westEnemies >= erwartet,
                $"Gegner in GG-West erschienen, als die Zelle bereit war ({westEnemies}, erwartet {erwartet} aus {wach} wachen von {gebiete} Gebieten – die übrigen schlafen, bis ein Spieler naht)");

            // ---------------------------------------------------------- Zwei Zellen, drei Spieler
            Line("");
            Line("[Zwei Zellen, drei Spieler]");
            Vector3 fort = BorderMountainsFrame.ToWorld(new Vector3(2250f, 0f, 385f));
            t0 = Time.realtimeSinceStartup;
            yield return Preload(ws, fort, 20f);
            Check(ws.StateOf(East) == WorldStreamer.CellState.Ready, $"Angekuendigte Reise laedt GG-Ost vor ({Time.realtimeSinceStartup - t0:0.0} s)");
            fort = Gnd(fort.x, fort.z);
            yield return Ask(c, "goto", V(fort, 90f));
            yield return WaitClientCell(c, East, "Ready", 20f);
            Check(_last.data.Contains(East + "=Ready"), "C's Client laedt GG-Ost nach dem Teleport");
            yield return new WaitForSecondsRealtime(3f);
            Check(Mathf.Abs(c.transform.position.y - fort.y) < 2f, $"C steht an der Grenzfeste (nicht gefallen, y {c.transform.position.y:0.0})");
            Check(ws.StateOf(West) == WorldStreamer.CellState.Ready && ws.StateOf(East) == WorldStreamer.CellState.Ready,
                "Server haelt beide Zellen (B im Westen, C im Osten, A am Wachtfeuer)");
            yield return WaitClientCell(b, East, "Unloaded", 45f);
            Check(_last.data.Contains(East + "=Unloaded"), "B's Client laedt GG-Ost nicht (nur die eigene Umgebung)");
            yield return WaitClientCell(c, West, "Unloaded", 45f);
            Check(_last.data.Contains(West + "=Unloaded"), "C's Client entlaedt GG-West nach Haltezeit (Hysterese 250/400 m, 20 s)");

            // ---------------------------------------------------------- Interessenverwaltung (N3)
            Line("");
            Line("[Sichtbarkeit (Interessenverwaltung)]");
            yield return new WaitForSecondsRealtime(3f);
            yield return Ask(b, "players");
            Check(_last.data == "players=1", $"B (Talpass) bekommt nur die eigene Figur – A und C sind ueber 500 m entfernt ({_last.data})");
            yield return Ask(b, "party-invite", c.CharacterName);
            yield return Ask(c, "party-respond", "1");
            yield return new WaitForSecondsRealtime(3f);
            yield return Ask(b, "players");
            Check(_last.data == "players=2", $"In einer Gruppe sehen sich B und C ueber 1,4 km hinweg ({_last.data})");
            yield return Ask(c, "party-leave");
            yield return new WaitForSecondsRealtime(3f);
            yield return Ask(b, "players");
            Check(_last.data == "players=1", $"Nach dem Verlassen der Gruppe wieder nur die eigene Figur ({_last.data})");

            // ---------------------------------------------------------- Sturz
            Line("");
            Line("[Sturz unter die Welt]");
            yield return Ask(b, "goto", V(BorderMountainsFrame.ToWorld(new Vector3(900f, -120f, 300f)), 0f));
            yield return new WaitForSecondsRealtime(2f);
            SafeSpawnAnchor nearest = SafeSpawnAnchor.All.Where(x => x != null && x.CellId == West)
                .OrderBy(x => (x.transform.position - b.transform.position).sqrMagnitude).FirstOrDefault();
            Check(nearest != null && Vector3.Distance(nearest.transform.position, b.transform.position) < 3f,
                $"B landet am naechsten sicheren Anker der Zelle ({(nearest != null ? nearest.AnchorId : "-")}), nicht am Wachtfeuer");

            // ---------------------------------------------------------- Speicherstand v5
            Line("");
            Line("[Speicherstand und Wiederverbinden in der Region]");
            yield return Ask(b, "goto", V(t2, 45f));
            yield return new WaitForSecondsRealtime(2f);
            Vector3 saved = b.transform.position;
            b.SaveNow();
            WorldPosition wp = b.SavedPosition;
            Check(wp.IsSet && wp.cellId == West && wp.regionId == "Human_BorderMountains" && wp.worldVersion == ws.Manifest.version,
                $"Speicherstand v5 enthaelt Region/Zelle/Weltversion ({wp.regionId}/{wp.cellId}/v{wp.worldVersion})");
            NetworkInventory bOld = b;
            yield return Ask(b, "reconnect");
            NetworkInventory b2 = null;
            yield return WaitUntil(() => (b2 = OnlineInv(WorldB)) != null && b2 != bOld, 60f);
            Check(b2 != null && b2 != bOld, "B ist wieder da");
            if (b2 != null && b2 != bOld)
            {
                b = b2;
                yield return new WaitForSecondsRealtime(6f);
                float d = Vector3.Distance(b.transform.position, saved);
                Check(d < 3f, $"B beginnt am gespeicherten Ort im Grenzgebirge ({d:0.0} m daneben)");
                yield return WaitClientCell(b, West, "Ready", 20f);
                Check(_last.data.Contains(West + "=Ready"), "B's Client laedt die Zelle beim Login");
            }

            // ---------------------------------------------------------- Entladen
            Line("");
            Line("[Zurueck nach Kareth]");
            yield return Ask(b, "goto", V(camp + Vector3.right * 3f, 0f));
            yield return Ask(c, "goto", V(camp + Vector3.left * 3f, 0f));
            t0 = Time.realtimeSinceStartup;
            yield return WaitUntil(() => ws.StateOf(West) == WorldStreamer.CellState.Unloaded && ws.StateOf(East) == WorldStreamer.CellState.Unloaded, 70f);
            Check(ws.StateOf(West) == WorldStreamer.CellState.Unloaded && ws.StateOf(East) == WorldStreamer.CellState.Unloaded,
                $"Server entlaedt beide Zellen, wenn alle zurueck sind ({Time.realtimeSinceStartup - t0:0} s inkl. Haltezeit)");
            Check(proxies.All(p => p != null && p.GetComponent<Renderer>().enabled), "Fernansicht wieder an");
            yield return new WaitForSecondsRealtime(1f);
            int left = Ashveil.Gameplay.Enemies.EnemyBrain.All.Count(e => e != null && InCore(e.transform.position, 600f, 3000f));
            Check(left == 0, $"Mit den Zellen sind auch ihre Gegner weg ({left} uebrig)");

            // ---------------------------------------------------------- 5x hin und zurueck
            Line("");
            Line("[5x hin und zurueck – Speicher und Frame-Spitzen]");
            System.GC.Collect();
            long mem0 = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            int rounds = 0;
            long memAfter1 = mem0;
            float clientAfter1 = -1f;
            // Nur die Ladevorgaenge dieser 5 Runden zaehlen: WorldStreamer.Log ist statisch und ueberlebt Play-Sitzungen
            // (ohne Domain-Reload) – das allererste Laden nach dem Kompilieren (Shader, Assets) gehoert nicht dazu.
            int logStart = WorldStreamer.Log.Count;
            yield return Ask(b, "world-worst-reset");
            for (int i = 0; i < 5; i++)
            {
                Vector3 target = BorderMountainsFrame.ToWorld(new Vector3(832f, 0f, 196f));
                yield return Preload(ws, target, 20f);
                if (ws.StateOf(West) != WorldStreamer.CellState.Ready) break;
                yield return Ask(b, "goto", V(Gnd(target.x, target.z), 45f));
                yield return WaitClientCell(b, West, "Ready", 20f);
                if (!_last.data.Contains(West + "=Ready")) break;
                yield return Ask(b, "goto", V(camp + Vector3.right * 3f, 0f));
                yield return WaitUntil(() => ws.StateOf(West) == WorldStreamer.CellState.Unloaded, 60f);
                if (ws.StateOf(West) != WorldStreamer.CellState.Unloaded) break;
                rounds++;
                if (rounds == 1)
                {
                    yield return WaitClientCell(b, West, "Unloaded", 30f);
                    yield return Ask(b, "world-mem");
                    clientAfter1 = ParseMem(_last.data);
                    // Grundlinie nach der ersten Runde: einmalig geladene Gelaendeschichten/Materialien zaehlen nicht als Leck
                    yield return Resources.UnloadUnusedAssets();
                    System.GC.Collect();
                    memAfter1 = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
                }
            }
            Check(rounds == 5, $"5 Runden Laden/Entladen ({rounds} geschafft)");
            yield return Resources.UnloadUnusedAssets();
            System.GC.Collect();
            yield return new WaitForSecondsRealtime(1f);
            long mem1 = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            float grow = (mem1 - mem0) / 1048576f, leak = (mem1 - memAfter1) / 1048576f;
            Info($"Editor-Host (nur Anhalt, der Editor haelt Assets fest): nach Runde 1 {(memAfter1 - mem0) / 1048576f:+0;-0} MB, Runden 2–5 {leak:+0;-0} MB");
            yield return WaitClientCell(b, West, "Unloaded", 30f);
            yield return Ask(b, "world-mem");
            float clientAfter5 = ParseMem(_last.data);
            float clientLeak = clientAfter5 - clientAfter1;
            Check(clientAfter1 > 0f && clientAfter5 > 0f && clientLeak < 40f,
                $"Kein Speicherleck im fertigen Spiel (Client B): {clientAfter1:0} MB nach Runde 1, {clientAfter5:0} MB nach Runde 5 ({clientLeak:+0;-0} MB, Grenze 40)");
            float worst = 0f;
            foreach (string l in WorldStreamer.Log.Skip(logStart))
            {
                int k = l.IndexOf("laengster Frame ", System.StringComparison.Ordinal);
                if (k >= 0 && float.TryParse(l.Substring(k + 16).Split(' ')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float ms)) worst = Mathf.Max(worst, ms);
            }
            float coldest = 0f;
            foreach (string l in WorldStreamer.Log.Take(logStart))
            {
                int k = l.IndexOf("laengster Frame ", System.StringComparison.Ordinal);
                if (k >= 0 && float.TryParse(l.Substring(k + 16).Split(' ')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float ms)) coldest = Mathf.Max(coldest, ms);
            }
            if (coldest > 0f) Info($"Vor den Runden (auch fruehere Play-Sitzungen, erstes Laden nach dem Kompilieren): laengster Frame {coldest:0} ms");
            Info($"Laengster Frame beim Laden (Editor-Host, 5 Runden – der Editor liest Szenen als Text, nur Anhalt): {worst:0} ms");
            // Massgeblich ist das fertige Spiel (Client B laedt dieselbe Zelle selbst): so erleben es die Spieler.
            yield return Ask(b, "world-worst");
            float clientWorst = -1f;
            if (_last.data != null)
            {
                int w = _last.data.IndexOf("worst=", System.StringComparison.Ordinal);
                if (w >= 0) float.TryParse(_last.data.Substring(w + 6), NumberStyles.Float, CultureInfo.InvariantCulture, out clientWorst);
            }
            Check(clientWorst >= 0f && clientWorst < 500f, $"Kein Haenger beim Laden im fertigen Spiel (Client B, laengster Frame {clientWorst:0} ms, Grenze 500)");
            foreach (string l in WorldStreamer.Log.Skip(Mathf.Max(0, WorldStreamer.Log.Count - 6))) Info(l);
        }
    }
}
