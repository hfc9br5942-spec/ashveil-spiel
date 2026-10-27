using System.Collections;
using System.Linq;
using Ashveil.Gameplay.Combat;
using Ashveil.World;
using UnityEngine;
using UnityEngine.AI;

namespace Ashveil.Networking
{
    /// <summary>
    /// Westweiden spielbar (Lückenliste Punkt 1, 12.10.2026): Kareth v4 und die Westweiden-Zelle als eine Welt (WORLD-120).
    ///   1. Weltverzeichnis und Streamer in der Welt-Ebene, am Wachtfeuer ist die Zelle nicht geladen.
    ///   2. Angekündigte Reise lädt die Zelle; Naht an x = −2250 ohne Höhensprung.
    ///   3. Zu Fuß vom Weidegatter durch die Klamm in die Westweiden (nicht gefallen, auf dem Boden, NavMesh der Zelle da).
    ///   4. Zurück nach Kareth; am Wachtfeuer wird die Zelle nach der Haltezeit entladen (Hysterese 400/650 m, 20 s).
    ///   5. Reise direkt nach Langer Rain (wie ein Login dort): Zelle lädt, die Figur steht danach auf dem Boden.
    ///   6. Westweiden-Aufträge W01–W21 (Daten und Durchspielen, DuoTestDirector.WestweidenAuftraege).
    /// Start: Auftrag „spieltest weidepass“ (Szene Kareth v4, ein Spieler). Bericht: Logs/…/Ashveil_WeidepassTest.txt.
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string WeidepassFlag = "weidepass";
        private static bool _weidepassMode;
        private const string WeidepassProfile = "duo-weidepass";
        private const string WwZelle = "westweiden_v2";
        private const float WwKante = -2250f;

        private IEnumerator WeidepassSuite()
        {
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(WeidepassProfile)) != null, 40f);
            Check(a != null, "Spieler angemeldet");
            if (a == null) yield break;
            a.GetComponent<Health>()?.SetInvulnerableFor(3600f);
            yield return new WaitForSecondsRealtime(2f);

            // ---------------------------------------------------------- 1. Weltverzeichnis
            Line("");
            Line("[Weltverzeichnis]");
            WorldStreamer ws = WorldStreamer.Instance;
            Check(ws != null && ws.Manifest != null && ws.Manifest.FindCell(WwZelle) != null, "Streamer mit Weltverzeichnis, Zelle westweiden_v2 bekannt");
            if (ws == null || ws.Manifest == null) yield break;
            Check(ws.StateOf(WwZelle) == WorldStreamer.CellState.Unloaded, "Am Wachtfeuer sind die Westweiden nicht geladen");

            // ---------------------------------------------------------- 2. Laden und Naht
            Line("");
            Line("[Laden und Naht]");
            Vector3 gatter = Gnd(-2060f, -1601f);
            float t0 = Time.realtimeSinceStartup;
            yield return Preload(ws, new Vector3(-2300f, 0f, -1600f), 60f);
            Check(ws.StateOf(WwZelle) == WorldStreamer.CellState.Ready, $"Angekündigte Reise lädt die Westweiden ({Time.realtimeSinceStartup - t0:0.0} s)");
            Terrain tk = Ashveil.Core.WorldGround.TerrainAt(new Vector3(WwKante + 5f, 0f, -1600f));
            Terrain tw = Ashveil.Core.WorldGround.TerrainAt(new Vector3(WwKante - 5f, 0f, -1600f));
            Check(tk != null && tw != null && tk != tw, $"Zwei Gelände an der Naht ({(tk != null ? tk.name : "–")} | {(tw != null ? tw.name : "–")})");
            if (tk != null && tw != null && tk != tw)
            {
                float maxSprung = 0f, zMax = 0f;
                for (float z = -1995f; z <= 255f; z += 1f)
                {
                    var p = new Vector3(WwKante, 0f, z);
                    float d = Mathf.Abs(tk.SampleHeight(p) + tk.transform.position.y - (tw.SampleHeight(p) + tw.transform.position.y));
                    if (d > maxSprung) { maxSprung = d; zMax = z; }
                }
                // Grenze 25 cm: zwei Raster (2 m / 3,32 m) mit geglätteter gemeinsamer Kante; Rest nur auf Graten, nicht auf Wegen
                Check(maxSprung < 0.25f, $"Naht x = −2250, z −1995 … 255: größter Höhensprung {maxSprung * 100f:0.0} cm (bei z {zMax:0})");
                if (maxSprung >= 0.25f)
                    foreach (float z in new[] { zMax - 4f, zMax - 2f, zMax, zMax + 2f, zMax + 4f })
                    {
                        float K(float x) => tk.SampleHeight(new Vector3(x, 0f, z)) + tk.transform.position.y;
                        float W(float x) => tw.SampleHeight(new Vector3(x, 0f, z)) + tw.transform.position.y;
                        Info($"z {z:0}: Kareth x−2250 {K(WwKante):0.00} · −2248 {K(WwKante + 2f):0.00} | Westweiden −2250 {W(WwKante):0.00} · −2252 {W(WwKante - 2f):0.00} · −2254 {W(WwKante - 4.1f):0.00}");
                    }
            }

            // ---------------------------------------------------------- 2b. Bilder (Blick der Spielkamera, für die eigene Prüfung)
            string bildOrdner = Ashveil.Core.ProjectPaths.Logs("Westweiden", "Weidepass_" + System.DateTime.Now.ToString("HHmm"));
            System.IO.Directory.CreateDirectory(bildOrdner);
            foreach ((string name, float x, float z, float yaw) in new[]
                     {
                         ("1_Gatter_Blick_West", -2040f, -1601f, 270f), ("2_Klamm_Mitte", -2200f, -1601f, 270f), ("3_Naht_Blick_West", -2300f, -1601f, 270f),
                         ("4_Kammsattel_Blick_Mühlen", -2630f, -1580f, 262f), ("5_Westweiden_Blick_zurück", -2420f, -1606f, 90f)
                     })
            {
                yield return Ask(a, "goto", V(Gnd(x, z), yaw));
                yield return new WaitForSecondsRealtime(2.5f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(bildOrdner, "WP_" + name + ".png"));
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Info("Bilder: " + bildOrdner);

            // ---------------------------------------------------------- 3. Zu Fuß durch die Klamm
            Line("");
            Line("[Zu Fuß durch die Klamm]");
            yield return Ask(a, "goto", V(gatter, 270f));
            yield return new WaitForSecondsRealtime(1.5f);
            Info($"Start am offenen Weidegatter ({gatter.x:0} / {gatter.y:0.0} / {gatter.z:0})");
            float minX = a.transform.position.x;
            bool amBoden = true, gefallen = false;
            for (int i = 0; i < 7; i++)
            {
                yield return Ask(a, "walk", "10 1", 25f);
                if (!TryPos(_last.data, out Vector3 p)) { Check(false, "Position nach dem Laufen"); break; }
                float boden = Ashveil.Core.WorldGround.SampleHeight(p, float.NaN);
                Info($"nach {(i + 1) * 10} s: x {p.x:0}, z {p.z:0}, y {p.y:0.0} (Boden {boden:0.0}), Zelle {ws.CellAt(p) ?? "–"}");
                if (float.IsNaN(boden) || Mathf.Abs(p.y - boden) > 2.5f) amBoden = false;
                if (p.x > minX + 50f) gefallen = true;    // zurück am Bindepunkt = gefallen
                minX = Mathf.Min(minX, p.x);
                if (p.x < WwKante - 300f) break;
            }
            Check(!gefallen && amBoden, "Die Figur bleibt auf dem Boden (kein Sturz, kein Zurücksetzen)");
            Check(minX < WwKante - 150f, $"Durch die Klamm in die Westweiden gelaufen (weitester Punkt x {minX:0}, Kante {WwKante:0})");
            Vector3 langerRain = new Vector3(2300f - 6450f, 0f, 1500f - 3140f);
            langerRain = Gnd(langerRain.x, langerRain.z, 0f);
            Check(NavMesh.SamplePosition(langerRain, out NavMeshHit _, 8f, NavMesh.AllAreas), $"NavMesh der Zelle in Langer Rain ({langerRain.x:0} / {langerRain.y:0} / {langerRain.z:0})");
            int wwGegner = Ashveil.Gameplay.Enemies.EnemyBrain.All.Count(e => e != null && e.transform.position.x < WwKante);
            int wwGebiete = FindObjectsByType<Ashveil.Gameplay.Enemies.EnemySpawnArea>(FindObjectsSortMode.None).Count(g => g.transform.position.x < WwKante);
            Check(wwGebiete > 500 && wwGegner > 0, $"Westweiden-Weltfülle: {wwGebiete} Spawngebiete in der Zelle, {wwGegner} Gegner nahe der Figur wach");
            int anker = SafeSpawnAnchor.All.Count(x => x != null && x.CellId == WwZelle);
            Check(anker >= 3, $"Sichere Anker in der Zelle ({anker})");

            // ---------------------------------------------------------- 4. Zurück und Entladen
            Line("");
            Line("[Zurück nach Kareth, Entladen]");
            yield return Ask(a, "goto", V(Gnd(WwKante - 120f, -1600f), 90f));
            yield return new WaitForSecondsRealtime(1f);
            yield return Ask(a, "walk", "10 1", 25f);
            yield return Ask(a, "walk", "10 1", 25f);
            bool zurueck = TryPos(_last.data, out Vector3 pz) && pz.x > WwKante;
            Check(zurueck, $"Zu Fuß zurück nach Kareth (x {pz.x:0})");
            Vector3 lager = GameObject.Find("PlayerSpawn") != null ? GameObject.Find("PlayerSpawn").transform.position : Gnd(0f, 7f);
            yield return Ask(a, "goto", V(lager, 20f));
            t0 = Time.realtimeSinceStartup;
            yield return WaitUntil(() => ws.StateOf(WwZelle) == WorldStreamer.CellState.Unloaded, 90f);
            Check(ws.StateOf(WwZelle) == WorldStreamer.CellState.Unloaded, $"Am Wachtfeuer werden die Westweiden wieder entladen ({Time.realtimeSinceStartup - t0:0} s)");
            int restGegner = Ashveil.Gameplay.Enemies.EnemyBrain.All.Count(e => e != null && e.transform.position.x < WwKante);
            Check(restGegner == 0, $"Keine Gegner der Zelle bleiben nach dem Entladen zurück ({restGegner})");

            // ---------------------------------------------------------- 5. Reise direkt nach Langer Rain
            Line("");
            Line("[Reise nach Langer Rain]");
            Vector3 ziel = new Vector3(2300f - 6450f + 14f, 0f, 1500f - 3140f - 18f);
            t0 = Time.realtimeSinceStartup;
            yield return Preload(ws, ziel, 60f);
            ziel = Gnd(ziel.x, ziel.z, 0.3f);
            yield return Ask(a, "goto", V(ziel, 0f));
            yield return new WaitForSecondsRealtime(3f);
            float by = Ashveil.Core.WorldGround.SampleHeight(a.transform.position, float.NaN);
            Check(!float.IsNaN(by) && Mathf.Abs(a.transform.position.y - by) < 2f && Vector3.Distance(a.transform.position, ziel) < 10f,
                $"Steht in Langer Rain auf dem Boden ({Time.realtimeSinceStartup - t0:0.0} s, y {a.transform.position.y:0.0}, Boden {by:0.0})");

            // Karten (Lückenliste Punkt 4, 13.10.2026): die Zelle hat ihr eigenes Szenenraster – Gebiet und Unterzone wie gebaut
            Ashveil.Maps.KartenBezug kbw = Ashveil.Maps.KartenBezug.Instanz;
            if (kbw != null && kbw.Dienst != null)
            {
                Ashveil.Maps.ZoneDaten zw = kbw.ZoneBei(a.transform.position);
                Ashveil.Maps.UnterzoneDaten uw = kbw.UnterzoneBei(a.transform.position);
                Check(zw?.key == "westweiden" && uw?.id == 20, $"Karte in Langer Rain: Gebiet {zw?.name ?? "–"}, Unterzone {uw?.name ?? "–"} (Soll Westweiden / Mühlenrain)");
                Check(kbw.ZoneBei(Vector3.zero)?.key == "kareth", "Karte in Kareth unverändert (Wachtfeuer gehört zu Kareth)");
            }
            else Check(false, "Kartenbezug in der Szene");

            // Westweiden-Aufträge (Punkt 2): Daten und Durchspielen, solange die Zelle geladen ist
            yield return WwAuftraegeSuite(a);
            // Bewohner mit Tagesablauf (Lückenliste Punkt 3, 13.10.2026), solange die Zelle geladen ist
            yield return WwBewohnerSuite();
            // Hauptquest der Menschen, Akt I „Das Register“ (13.10.2026): Halle und Salzkeller in Kareth, Ende bei Fenna in den Westweiden
            yield return RegisterSuite(a);

            Line("");
            Line("[Streamer-Protokoll]");
            foreach (string l in WorldStreamer.Log.Skip(Mathf.Max(0, WorldStreamer.Log.Count - 6))) Info(l);
            Info($"Längstes Bild beim Laden (Editor, Szenen als Text – nicht maßgeblich): {WorldStreamer.WorstLoadFrameMs:0} ms");
        }
    }
}
