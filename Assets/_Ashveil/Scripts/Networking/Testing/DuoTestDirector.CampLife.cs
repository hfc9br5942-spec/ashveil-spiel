using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using Ashveil.Core;
using Ashveil.Gameplay.Npcs;
using Ashveil.Gameplay.Quests;
using Ashveil.Gameplay.Visuals;
using UnityEngine;
using UnityEngine.AI;

namespace Ashveil.Networking
{
    /// <summary>
    /// Abnahmetest M8.3 „Lagerleben“: vergroessertes Startlager und NPC-Tagesablaeufe. Maxmod (Editor-Host) und
    /// Krisbot (Test-Client). Geprueft: Lagerbereiche und Wege, echte Koerper, Dienst-NPCs rund um die Uhr erreichbar,
    /// Arbeit am Vormittag, Mittag an den Langtischen, Abend am Feuer (Tanz), Nachtruhe, dieselben Stellen auf beiden
    /// Rechnern, ein Weg wird tatsaechlich gegangen. Bilder: Logs/Ashveil/Lagerleben.
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string CampLifeFlag = "lagerleben";
        private static bool _campLifeMode;

        private static readonly string[] ServiceNpcs = { "npc.oskar_bralt", "npc.maelis", "npc.gerrit", "npc.dorn", "npc.ilse_harrow", "npc.cartographer" };

        private IEnumerator CampLifeSuite()
        {
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(EmoteA)) != null, 30f);
            Check(a != null, "Maxmod (Editor-Host) angemeldet");
            if (a == null) yield break;
            yield return new WaitForSecondsRealtime(1.5f);
            Zeitraffer(true);   // Wege und Fahrten sind hier in Spielminuten je echter Sekunde gedacht
            Vector3 spawn = GameObject.Find("PlayerSpawn") != null ? GameObject.Find("PlayerSpawn").transform.position : Vector3.zero;

            // ---------------------------------------------------------- Lager
            Line("");
            Line("[Das Startlager]");
            Transform camp = GameObject.Find("Wachtfeuer (Spawn)")?.transform;
            _campForTest = camp;
            Check(camp != null, "Lager „Wachtfeuer“ in der Szene");
            if (camp == null) yield break;
            string[] areas = { "Markt", "Schmiede und Holzplatz", "Zelte", "Kochstelle", "Trainingsplatz", "Pferch und Wagen", "Tore und Tuerme", "Laternen", "Aussichtsfels" };
            int found = areas.Count(n => camp.Find(n) != null);
            Check(found == areas.Length, $"Alle Bereiche vorhanden ({found}/{areas.Length})");
            int towers = camp.Find("Tore und Tuerme")?.GetComponentsInChildren<Transform>().Count(t => t.name.StartsWith("Wachturm")) ?? 0;
            Check(towers == 4, $"Vier Wachtuerme ({towers})");
            float farthest = 0f;
            foreach (Transform t in camp.Find("Palisade"))
                if (!t.name.StartsWith("Geroell")) farthest = Mathf.Max(farthest, Vector2.Distance(new Vector2(t.position.x, t.position.z), new Vector2(0f, -4f)));
            Check(farthest > 30f && farthest < 52f, $"Palisade weiter gezogen (bis {farthest:0} m vom Mittelpunkt bis an den Fels, vorher r = 22 m)");
            // Tore: vom Feuer durch jedes Tor nach draussen
            (string name, Vector3 p)[] outside =
            {
                ("Nordtor", new Vector3(0f, 0f, 36f)), ("Osttor", new Vector3(40f, 0f, -14f)),
                ("Westtor", new Vector3(-38f, 0f, 10f)), ("Suedwesttor", new Vector3(-36f, 0f, -34f))
            };
            foreach ((string n, Vector3 p) in outside)
            {
                float len = PathLen(spawn, p);
                Check(len > 0f && len < 70f, $"{n}: vom Spawn nach draussen ({len:0} m)");
            }
            float hall = PathLen(spawn, GiantHallPoint());
            Check(hall > 0f && hall < 90f, $"Halle des Hauptmanns liegt im Lager, direkt erreichbar ({hall:0} m)");

            // ---------------------------------------------------------- Bewohner
            Line("");
            Line("[Bewohner]");
            var routines = NpcRoutineRegistry.All.Where(r => r != null && r.transform.IsChildOf(camp)).ToList();
            int services = routines.Count(r => r.GetComponent<NpcIdentity>() != null);
            // 06.10.: mindestens die sechs festen Dienste (inzwischen trägt ein weiterer Lager-NPC eine Kennung für Aufträge)
            Check(routines.Count >= 16 && services >= ServiceNpcs.Length, $"{routines.Count} NPCs mit Tagesablauf ({services} Dienste, {routines.Count - services} Bewohner)");
            yield return new WaitForSecondsRealtime(0.5f);
            int bodies = routines.Count(r => r.GetComponentInChildren<NpcBody>() != null && r.GetComponentInChildren<NpcBody>().Built);
            Check(bodies == routines.Count, $"Echte Menschenkoerper mit Kleidung statt Kapseln ({bodies}/{routines.Count})");
            string exe = ProjectPaths.TestClientExe;
            if (!File.Exists(exe)) { Check(false, "Test-Client vorhanden"); yield break; }
            StartClient(exe, EmoteB);
            NetworkInventory b = null;
            yield return WaitUntil(() => (b = OnlineInv(EmoteB)) != null && b.IsReady, 120f);
            Check(b != null, "Krisbot (Test-Client) angemeldet");
            if (b == null) yield break;
            yield return new WaitForSecondsRealtime(2f);

            // ---------------------------------------------------------- Vormittag
            yield return JumpAndSettle(10f, "Vormittag (10:00)");
            Check(Act("Jorn") == "Chop", $"Jorn hackt Holz ({Act("Jorn")})");
            Check(Act("Schmied Dorn") == "Chop" || Act("Schmied Dorn") == "Stand", $"Dorn schmiedet am Amboss ({Note("Schmied Dorn")})");
            Check(Act("Wenna") == "Kneel" || Act("Wenna") == "Stand" || Act("Wenna") == "Talk" || Act("Wenna") == "Carry", $"Wenna an der Kochstelle ({Note("Wenna")})");
            yield return ServicesReachable(spawn, "am Vormittag");
            yield return SameOnClient(b, new[] { "Jorn", "Kräuterin Maelis", "Brenn" });
            yield return CaptureCamp("1000_Vormittag");
            yield return CapturePortraits(routines);

            // ---------------------------------------------------------- Tiere (M8.4)
            Line("");
            Line("[Tiere]");
            var animals = AnimalRegistry.All.Where(r => r != null && r.transform.IsChildOf(camp)).ToList();
            Check(animals.Count >= 5, $"{animals.Count} Tiere im Lager (Katze, Hund, zwei Ziegen, Maultier)");
            AnimalRoutine catR = AnimalRegistry.Find("Katze"), dogR = AnimalRegistry.Find("Hund");
            yield return WaitUntil(() => catR != null && !catR.Walking, 30f);
            Check(catR != null && catR.CurrentStep.note.Contains("Vorraeten") && catR.Pose == AnimalPose.Lie, $"Die Katze liegt auf den Vorraeten ({(catR != null ? catR.CurrentStep.note : "-")})");
            NpcRoutine brenn = NpcRoutineRegistry.Find("Brenn");
            Check(dogR != null && brenn != null && Vector3.Distance(dogR.transform.position, brenn.transform.position) < 6f,
                $"Bolt ist bei Brenn am Nordtor ({(dogR != null && brenn != null ? Vector3.Distance(dogR.transform.position, brenn.transform.position) : -1f):0.0} m)");
            Vector2 pen = new Vector2(19f, -34f);
            int inPen = animals.Count(r => (r.name.StartsWith("Ziege") || r.name.StartsWith("Maultier")) &&
                                          Vector2.Distance(new Vector2(r.transform.position.x, r.transform.position.z), pen) < 4.2f);
            Check(inPen == 3, $"Ziegen und Maultier bleiben im Pferch ({inPen}/3)");
            var before = animals.Select(r => r.transform.position).ToList();
            yield return new WaitForSecondsRealtime(20f);
            int moved = animals.Where((r, i) => Vector3.Distance(before[i], r.transform.position) > 0.4f).Count();
            Check(moved >= 2, $"Tiere streifen umher ({moved} haben sich in 20 s bewegt)");
            Check(animals.Select(r => r.Pose).Distinct().Count() >= 2, $"Verschiedene Haltungen ({string.Join(", ", animals.Select(r => r.Pose).Distinct())})");
            yield return Ask(b, "animal-state", "Katze");
            float dx = ParseAfter(_last.data, "x=") - catR.transform.position.x, dz = ParseAfter(_last.data, "z=") - catR.transform.position.z;
            Check(_last.ok && Mathf.Sqrt(dx * dx + dz * dz) < 0.3f && _last.data.Contains("pose=" + catR.Pose), $"Krisbot sieht die Katze an derselben Stelle ({Mathf.Sqrt(dx * dx + dz * dz):0.00} m)");
            yield return CaptureAnimals(animals, "1000");

            // ---------------------------------------------------------- Lagerszenen (M8.5, Bibel § 43/§ 48)
            Line("");
            Line("[Lagerszenen]");
            NpcRoutine nessa = NpcRoutineRegistry.Find("Heilerin Nessa"), hollin = NpcRoutineRegistry.Find("Hollin"),
                veit = NpcRoutineRegistry.Find("Schreiber Veit"), ulf = NpcRoutineRegistry.Find("Zimmermann Ulf"), pell = NpcRoutineRegistry.Find("Händler Pell");
            Check(nessa != null && hollin != null && veit != null && ulf != null && pell != null, "Heilerin, Verwundeter, Schreiber, Zimmermann und Haendler sind da");
            if (nessa == null || hollin == null || veit == null || ulf == null || pell == null) yield break;
            yield return WaitUntil(() => !nessa.Walking && !ulf.Walking && !pell.Walking, 60f);
            float tend = Vector3.Distance(nessa.transform.position, hollin.transform.position);
            Check((nessa.Activity == "Kneel" || nessa.Activity == "Talk") && tend < 1.2f && (hollin.Activity == "Sit" || hollin.Activity == "SitTalk"),
                $"Nessa versorgt den verwundeten Hollin im Lazarett ({nessa.Activity}, {hollin.Activity}, {tend:0.0} m)");
            Check(veit.Activity.StartsWith("Sit") && veit.CurrentStep.note.Contains("Register"), $"Schreiber Veit sitzt am Pult ({veit.Activity}: {veit.CurrentStep.note})");
            float wall = Vector2.Distance(new Vector2(ulf.transform.position.x, ulf.transform.position.z), new Vector2(0f, -4f));
            Check(ulf.Activity == "Kneel" && wall > 28.5f, $"Zimmermann Ulf repariert die Palisade ({ulf.Activity}, {32f - wall:0.0} m vor der Wand)");
            NpcCart cart = pell.Cart;
            float cartGap = cart != null ? Vector3.Distance(cart.transform.position, pell.transform.position) : 99f;
            Check(cart != null && cartGap < 2f, $"Pells Handkarren steht vor ihm am Nordtor ({cartGap:0.0} m)");
            Check(cart != null && cart.GetComponentsInChildren<Collider>().Length == 0, "Der Karren blockiert niemanden (keine Collider)");
            yield return CaptureScenes(hollin, veit, ulf, pell, "1000");
            // Weg zu den Langtischen: Pell schiebt den Karren
            Vector3 cart0 = cart != null ? cart.transform.position : Vector3.zero;
            NetworkWorldTime.ServerJumpTo(11.76f);
            yield return new WaitForSecondsRealtime(1.5f);
            float pushGap = cart != null ? Vector3.Distance(cart.transform.position, pell.transform.position) : 99f;
            Check(pell.Walking && pell.Activity == "Push" && pushGap < 1.6f, $"11:45 – Pell schiebt seinen Karren zu den Tischen ({pell.Activity}, Karren {pushGap:0.0} m vor ihm)");
            yield return WaitUntil(() => !pell.Walking, 60f);
            float cartMoved = cart != null ? Vector3.Distance(cart0, cart.transform.position) : 0f;
            Check(!pell.Walking && cartMoved > 25f && pell.CurrentStep.note.Contains("Langtischen"), $"… und kommt mit dem Karren an ({cartMoved:0} m gefahren)");
            yield return Ask(b, "npc-state", "Händler Pell");
            float pdx = ParseAfter(_last.data, "x=") - pell.transform.position.x, pdz = ParseAfter(_last.data, "z=") - pell.transform.position.z;
            Check(_last.ok && Mathf.Sqrt(pdx * pdx + pdz * pdz) < 0.6f, $"Krisbot sieht Pell an derselben Stelle ({Mathf.Sqrt(pdx * pdx + pdz * pdz):0.00} m)");

            // ---------------------------------------------------------- Weg zum Mittag
            Line("");
            Line("[Mittag an den Langtischen]");
            NetworkWorldTime.ServerJumpTo(12.5f);
            yield return new WaitForSecondsRealtime(0.4f);
            NpcRoutine dorn = NpcRoutineRegistry.Find("Schmied Dorn");
            Vector3 d0 = dorn.transform.position;
            Check(dorn.Walking, "12:30 – Dorn macht sich auf den Weg zu den Tischen");
            yield return WaitUntil(() => !dorn.Walking, 30f);
            Check(!dorn.Walking && Vector3.Distance(d0, dorn.transform.position) > 8f, $"… und kommt an ({Vector3.Distance(d0, dorn.transform.position):0} m gegangen)");
            yield return new WaitForSecondsRealtime(8f);
            int seated = routines.Count(r => r.Activity == "SitTalk" && r.CurrentStep.note.Contains("Langtisch"));
            Check(seated >= 6, $"{seated} sitzen zum Mittagessen an den Langtischen");
            Check(Act("Kräuterin Maelis") != "SitTalk" && Act("Pulverhändler Gerrit") != "SitTalk", "Maelis und Gerrit bleiben an ihren Staenden");
            yield return CaptureCamp("1240_Mittag");

            // ---------------------------------------------------------- Hedda und die Katze
            yield return JumpAndSettle(14.08f, "Nachmittag (14:05)");
            yield return WaitUntil(() => catR != null && !catR.Walking, 45f);
            NpcRoutine hedda = NpcRoutineRegistry.Find("Hedda");
            float petDist = hedda != null && catR != null ? Vector3.Distance(hedda.transform.position, catR.transform.position) : 99f;
            Check(hedda != null && hedda.Activity == "Kneel" && catR.Pose == AnimalPose.Sit && petDist < 1.4f,
                $"Hedda kniet und streichelt die Katze ({petDist:0.0} m, {(hedda != null ? hedda.Activity : "-")}, {catR.Pose})");
            yield return CaptureCamp("1405_Katze");
            if (catR != null) yield return CaptureAnimals(new System.Collections.Generic.List<AnimalRoutine> { catR }, "1405_Hedda");

            // ---------------------------------------------------------- Abend
            yield return JumpAndSettle(21.2f, "Abend (21:12)");
            int atFire = routines.Count(r => r.CurrentStep.note.Contains("Wachtfeuer") || r.CurrentStep.note.Contains("erzaehlt") || r.CurrentStep.note.Contains("tanzt"));
            Check(atFire >= 8, $"{atFire} Bewohner am Wachtfeuer");
            Check(Note("Jorn").Contains("tanzt"), $"Jorn tanzt am Feuer ({Act("Jorn")})");
            yield return ServicesReachable(spawn, "am Abend");
            yield return CaptureCamp("2112_Abend");

            // ---------------------------------------------------------- Nacht
            yield return JumpAndSettle(2f, "Nacht (02:00)");
            int sleeping = routines.Count(r => r.Sleeping);
            Check(sleeping >= 4, $"{sleeping} Bewohner schlafen in den Zelten");
            Check(NpcRoutineRegistry.Find("Tilda") != null && !NpcRoutineRegistry.Find("Tilda").Sleeping, $"Tilda haelt Nachtwache ({Note("Tilda")})");
            bool servicesAwake = ServiceNpcs.All(id => NpcIdentity.Find(id) != null && NpcIdentity.Find(id).GetComponent<NpcRoutine>() is NpcRoutine r && !r.Sleeping);
            Check(servicesAwake, "Kein Dienst-NPC verschwindet nachts");
            yield return ServicesReachable(spawn, "in der Nacht");
            yield return GotoNpc(b, "npc.maelis");
            yield return Ask(b, "npc-talk", "npc.maelis");
            Check(_last.ok, "Krisbot spricht nachts mit Maelis am Zeltfeuer");
            yield return SameOnClient(b, new[] { "Kräuterin Maelis", "Tilda", "Hauptmann Oskar Bralt" });
            NpcRoutine pellN = NpcRoutineRegistry.Find("Händler Pell"), hollinN = NpcRoutineRegistry.Find("Hollin");
            float parked = pellN != null && pellN.Cart != null ? Vector3.Distance(pellN.Cart.transform.position, pellN.transform.position) : 99f;
            Check(pellN != null && pellN.Sleeping && parked < 2f && pellN.Cart.gameObject.activeInHierarchy, $"Pell schlaeft neben seinem Karren ({parked:0.0} m)");
            Check(hollinN != null && !hollinN.Sleeping, "Hollin bleibt im Lazarett sichtbar (sitzt wach auf der Pritsche)");
            NpcRoutine oswN = NpcRoutineRegistry.Find("Fuhrmann Oswald");
            float wagonMine = oswN != null && oswN.Cart != null ? Vector2.Distance(new Vector2(oswN.Cart.transform.position.x, oswN.Cart.transform.position.z), new Vector2(-278.7f, -189.3f)) : 99f;
            Check(oswN != null && oswN.Sleeping && wagonMine < 1.5f, $"Nachts: Oswald schläft bei den Grubenleuten, sein Fuhrwerk steht am Stollenmund ({wagonMine:0.0} m)");
            int pilgrimsAsleep = new[] { "Pilgerin Ada", "Pilger Rune", "Wagner Tamo" }.Count(n => NpcRoutineRegistry.Find(n) != null && NpcRoutineRegistry.Find(n).Sleeping);
            Check(pilgrimsAsleep == 3, $"Pilger und Wagner schlafen draußen an ihren Plätzen ({pilgrimsAsleep}/3)");
            int resting = AnimalRegistry.All.Count(r => r != null && (r.Pose == AnimalPose.Sleep || r.Pose == AnimalPose.Lie));
            Check(resting >= 4, $"{resting} Tiere schlafen");
            yield return CaptureCamp("0200_Nacht");

            // ---------------------------------------------------------- Unterwegs (M8.6)
            yield return RoadLifeSection(b);

            NetworkWorldTime.ServerJumpTo((float)WorldClock.DefaultStartHour);
            Zeitraffer(false);
        }

        private static Transform _campForTest;

        /// <summary>Bewohner des Lagers (ohne Reisende auf den Wegen, deren Fahrten dauern Minuten).</summary>
        private static bool InCamp(NpcRoutine r) => r != null && (_campForTest == null || r.transform.IsChildOf(_campForTest));

        private IEnumerator JumpAndSettle(float hour, string title)
        {
            Line("");
            Line($"[{title}]");
            NetworkWorldTime.ServerJumpTo(hour);
            // Wege abwarten: laengster Weg im Lager ~70 m / 1,35 m/s
            float t = 0f;
            while (t < 60f && NpcRoutineRegistry.All.Any(r => InCamp(r) && r.Walking))
            {
                t += 0.5f;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            yield return new WaitForSecondsRealtime(1f);
            Check(!NpcRoutineRegistry.All.Any(r => InCamp(r) && r.Walking), $"Alle Lagerbewohner sind angekommen ({t:0} s Wege)");
        }

        private IEnumerator ServicesReachable(Vector3 spawn, string when)
        {
            int ok = 0;
            var bad = new System.Collections.Generic.List<string>();
            foreach (string id in ServiceNpcs)
            {
                NpcIdentity n = NpcIdentity.Find(id);
                if (n == null) { bad.Add(id); continue; }
                float len = PathLen(spawn, n.transform.position, 3f);
                if (len > 0f && len < 110f) ok++;
                else bad.Add(n.name);
            }
            Check(ok == ServiceNpcs.Length, $"Alle Dienste {when} erreichbar ({ok}/{ServiceNpcs.Length}{(bad.Count > 0 ? ": fehlt " + string.Join(", ", bad) : "")})");
            yield break;
        }

        private IEnumerator SameOnClient(NetworkInventory b, string[] names)
        {
            int same = 0;
            var info = new System.Collections.Generic.List<string>();
            foreach (string n in names)
            {
                NpcRoutine r = NpcRoutineRegistry.Find(n);
                yield return Ask(b, "npc-state", n);
                if (r == null || !_last.ok) continue;
                var c = new Vector3(ParseAfter(_last.data, "x="), ParseAfter(_last.data, "y="), ParseAfter(_last.data, "z="));
                float d = Vector3.Distance(c, r.transform.position);
                bool act = _last.data.Contains("act=" + r.Activity + ";");
                if (d < 0.6f && act) same++;
                info.Add($"{n.Split(' ').Last()} {d:0.00} m");
            }
            Check(same == names.Length, $"Krisbot sieht dieselben Stellen und Taetigkeiten ({same}/{names.Length}: {string.Join(", ", info)})");
        }

        private static string Act(string name) => NpcRoutineRegistry.Find(name)?.Activity ?? "?";
        private static string Note(string name) => NpcRoutineRegistry.Find(name)?.CurrentStep.note ?? "?";

        private static Vector3 GiantHallPoint()
        {
            var poi = Ashveil.World.WorldPoi.Find("poi.kareth.halle_des_hauptmanns");
            return poi != null ? poi.transform.position : new Vector3(0f, 0f, -26f);
        }

        private static float PathLen(Vector3 from, Vector3 to, float snap = 4f)
        {
            if (!NavMesh.SamplePosition(from, out NavMeshHit a, 3f, NavMesh.AllAreas) || !NavMesh.SamplePosition(to, out NavMeshHit b, snap, NavMesh.AllAreas)) return -1f;
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return -1f;
            float len = 0f;
            for (int i = 1; i < path.corners.Length; i++) len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            return len;
        }

        /// <summary>Nahaufnahme jedes Bewohners (Aussehen, Kleidung, Haltung) – zur Sichtpruefung.</summary>
        private IEnumerator CapturePortraits(System.Collections.Generic.List<NpcRoutine> list)
        {
            Camera cam = Camera.main;
            if (cam == null) yield break;
            string dir = ProjectPaths.Logs("Lagerleben/Bewohner");
            Directory.CreateDirectory(dir);
            Vector3 pos0 = cam.transform.position;
            Quaternion rot0 = cam.transform.rotation;
            float fov0 = cam.fieldOfView;
            int shots = 0;
            foreach (NpcRoutine r in list)
            {
                if (r.Sleeping) continue;
                Transform t = r.transform;
                Vector3 chest = t.position + Vector3.up * 1.15f;
                cam.transform.position = chest + t.forward * 2.6f + t.right * 0.6f + Vector3.up * 0.25f;
                // Steht eine Wand vor der Figur (z. B. Heilerin kniet zur Rueckwand), von der anderen Seite aufnehmen
                if (Physics.Linecast(chest, cam.transform.position, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) && hit.transform.GetComponentInParent<NpcRoutine>() == null)
                    cam.transform.position = chest - t.forward * 2.6f + t.right * 0.6f + Vector3.up * 0.25f;
                cam.transform.LookAt(chest);
                cam.fieldOfView = 40f;
                if (CaptureUtil.Capture(cam, Path.Combine(dir, r.name.Replace(" ", "_") + ".png"), 720, 900)) shots++;
            }
            cam.transform.SetPositionAndRotation(pos0, rot0);
            cam.fieldOfView = fov0;
            Info($"{shots} Nahaufnahmen der Bewohner (Logs/Ashveil/Lagerleben/Bewohner)");
            yield break;
        }

        /// <summary>Nahaufnahme jedes Tieres (Form, Haltung) – zur Sichtpruefung.</summary>
        private IEnumerator CaptureAnimals(System.Collections.Generic.List<AnimalRoutine> list, string prefix)
        {
            Camera cam = Camera.main;
            if (cam == null) yield break;
            string dir = ProjectPaths.Logs("Lagerleben/Tiere");
            Directory.CreateDirectory(dir);
            Vector3 pos0 = cam.transform.position;
            Quaternion rot0 = cam.transform.rotation;
            float fov0 = cam.fieldOfView;
            int shots = 0;
            foreach (AnimalRoutine r in list)
            {
                Transform t = r.transform;
                bool big = r.name.StartsWith("Maultier") || r.name.StartsWith("Ziege");
                Vector3 body = t.position + Vector3.up * (big ? 0.8f : 0.35f);
                float dist = big ? 3.2f : 1.9f;
                cam.transform.position = body + (t.forward * 0.8f + t.right).normalized * dist + Vector3.up * (big ? 0.9f : 0.7f);
                cam.transform.LookAt(body);
                cam.fieldOfView = 45f;
                string file = $"{prefix}_{r.name.Split(' ')[0]}_{r.name.Split(' ').Last()}.png";
                if (CaptureUtil.Capture(cam, Path.Combine(dir, file), 960, 720)) shots++;
            }
            cam.transform.SetPositionAndRotation(pos0, rot0);
            cam.fieldOfView = fov0;
            Info($"{shots} Nahaufnahmen der Tiere (Logs/Ashveil/Lagerleben/Tiere)");
            yield break;
        }

        /// <summary>M8.6: Reisende auf den Wegen – Salzfuhrwerk auf der Strasse, Pilger, Wagner am liegengebliebenen Wagen.</summary>
        private IEnumerator RoadLifeSection(NetworkInventory b)
        {
            Line("");
            Line("[Unterwegs auf den Wegen]");
            string[] pois = { "poi.kareth.wegschrein", "poi.kareth.pilgerrast", "poi.kareth.liegengebliebener_wagen" };
            int found = pois.Count(id => Ashveil.World.WorldPoi.Find(id) != null);
            Check(found == pois.Length, $"Neue Orte am Weg: Wegschrein, Pilgerrast, liegengebliebener Wagen ({found}/{pois.Length})");
            NpcRoutine osw = NpcRoutineRegistry.Find("Fuhrmann Oswald"), ada = NpcRoutineRegistry.Find("Pilgerin Ada"), rune = NpcRoutineRegistry.Find("Pilger Rune"),
                tamo = NpcRoutineRegistry.Find("Wagner Tamo");
            Check(osw != null && ada != null && rune != null && tamo != null && osw.Cart != null && osw.Cart.Draft != null, "Fuhrmann mit Wagen und Maultier, zwei Pilger und der Wagner sind da");
            if (osw == null || ada == null || rune == null || tamo == null || osw.Cart == null) yield break;

            // 06:45 – das Fuhrwerk ist unterwegs auf dem Salzweg
            NetworkWorldTime.ServerJumpTo(6.75f);
            yield return new WaitForSecondsRealtime(1.5f);
            Vector2 p = new Vector2(osw.transform.position.x, osw.transform.position.z);
            float offRoad = DistanceToRoad(p);
            float gap = Vector3.Distance(osw.Cart.transform.position, osw.transform.position);
            Check(osw.Walking && osw.Activity == "Ride" && gap < 0.5f, $"06:45 – Oswald fährt auf dem Kutschbock (Wagen {gap:0.0} m, {osw.Activity})");
            Check(offRoad < 4f, $"Das Fuhrwerk folgt der Straße, nicht dem kürzesten Weg ({offRoad:0.0} m neben dem Salzweg)");
            Check(osw.Cart.Speed > 1f && osw.Cart.Draft.Pose == AnimalPose.Walk, $"Maultier läuft, Räder drehen ({osw.Cart.Speed:0.0} m/s)");
            yield return Ask(b, "npc-state", "Fuhrmann Oswald");
            float dx = ParseAfter(_last.data, "x=") - osw.transform.position.x, dz = ParseAfter(_last.data, "z=") - osw.transform.position.z;
            Check(_last.ok && Mathf.Sqrt(dx * dx + dz * dz) < 1.5f, $"Krisbot sieht das Fuhrwerk an derselben Stelle ({Mathf.Sqrt(dx * dx + dz * dz):0.00} m)");
            yield return CaptureRoad(osw.transform, "0645_Salzfuhrwerk", 7f, 2.5f, 3.5f);

            // 07:40 – Salz wird an der Vorratskiste abgeladen, der Wagen steht vor dem Suedwesttor
            NetworkWorldTime.ServerJumpTo(7.6f);
            yield return WaitUntil(() => !osw.Walking, 30f);
            float atGate = Vector2.Distance(new Vector2(osw.Cart.transform.position.x, osw.Cart.transform.position.z), new Vector2(-42f, -38f));
            Check(osw.CurrentStep.note.Contains("Salzsaecke") || osw.CurrentStep.note.Contains("Sack"), $"07:40 – Oswald lädt Salz ab ({osw.CurrentStep.note})");
            Check(atGate < 1f, $"… sein Wagen wartet vor dem Südwesttor ({atGate:0.0} m)");

            // 09:15 – Pilger beten vor dem Beschuetzer, Tamo repariert das Rad
            NetworkWorldTime.ServerJumpTo(9.25f);
            yield return WaitUntil(() => !ada.Walking && !rune.Walking && !tamo.Walking, 60f);
            float hall = Vector2.Distance(new Vector2(ada.transform.position.x, ada.transform.position.z), new Vector2(0f, -20.5f));
            Check(ada.Activity == "Kneel" && rune.Activity == "Kneel" && hall < 2.5f, $"09:15 – die Pilger knien vor dem Beschützer ({hall:0.0} m vor der Halle)");
            Check(tamo.Activity == "Kneel" || tamo.Activity == "Chop", $"Wagner Tamo repariert das gebrochene Rad ({tamo.Activity})");
            yield return CaptureRoad(ada.transform, "0915_Pilger", 5f, 1.5f, 2f);
            yield return CaptureRoad(tamo.transform, "0915_Wagner", 5f, 2f, 2f);

            // 18:10 – Pilger zurueck an ihrem Feuer
            NetworkWorldTime.ServerJumpTo(18.15f);
            yield return WaitUntil(() => !ada.Walking && !rune.Walking, 60f);
            float rest = Vector2.Distance(new Vector2(ada.transform.position.x, ada.transform.position.z), new Vector2(14f, 178f));
            Check(ada.Activity.StartsWith("Sit") && rest < 3f, $"18:10 – die Pilger sitzen wieder an ihrem Feuer an der Pilgerrast ({rest:0.0} m)");
            yield return CaptureRoad(ada.transform, "1810_Pilgerrast", 6f, 2f, 3f);
        }

        /// <summary>Abstand eines Punkts zum Salzweg (Strassenlinie im Kareth-Bauplan).</summary>
        private static float DistanceToRoad(Vector2 p)
        {
            Vector2[] road = { new Vector2(0f, 0f), new Vector2(-45f, -45f), new Vector2(-150f, -120f), new Vector2(-290f, -200f) };
            float best = float.MaxValue;
            for (int i = 1; i < road.Length; i++)
            {
                Vector2 a = road[i - 1], ab = road[i] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }

        private IEnumerator CaptureRoad(Transform t, string name, float dist, float side, float up)
        {
            Camera cam = Camera.main;
            if (cam == null) yield break;
            string dir = ProjectPaths.Logs("Lagerleben/Unterwegs");
            Directory.CreateDirectory(dir);
            Vector3 pos0 = cam.transform.position;
            Quaternion rot0 = cam.transform.rotation;
            float fov0 = cam.fieldOfView;
            Vector3 focus = t.position + Vector3.up * 1.2f;
            cam.transform.position = focus + t.forward * dist + t.right * side + Vector3.up * up;
            cam.transform.LookAt(focus);
            cam.fieldOfView = 55f;
            CaptureUtil.Capture(cam, Path.Combine(dir, name + ".png"), 1280, 720);
            cam.transform.SetPositionAndRotation(pos0, rot0);
            cam.fieldOfView = fov0;
        }

        /// <summary>Nahaufnahmen der Lagerszenen (M8.5) – zur Sichtpruefung.</summary>
        private IEnumerator CaptureScenes(NpcRoutine hollin, NpcRoutine veit, NpcRoutine ulf, NpcRoutine pell, string prefix)
        {
            Camera cam = Camera.main;
            if (cam == null) yield break;
            string dir = ProjectPaths.Logs("Lagerleben/Szenen");
            Directory.CreateDirectory(dir);
            Vector3 pos0 = cam.transform.position;
            Quaternion rot0 = cam.transform.rotation;
            float fov0 = cam.fieldOfView;
            (string name, Transform t, float dist, float side)[] shots =
            {
                ("Lazarett", hollin.transform, 3.4f, 1.4f), ("Schreibpult", veit.transform, 3.2f, -1.0f),
                ("Palisade", ulf.transform, 4.0f, 1.6f), ("Handkarren", pell.transform, 4.2f, 1.8f)
            };
            foreach ((string n, Transform t, float dist, float side) in shots)
            {
                Vector3 focus = t.position + Vector3.up * 0.9f;
                cam.transform.position = focus - t.forward * dist + t.right * side + Vector3.up * 1.1f;
                if (n == "Lazarett" || n == "Handkarren") cam.transform.position = focus + t.forward * dist + t.right * side + Vector3.up * 1.1f;
                cam.transform.LookAt(focus);
                cam.fieldOfView = 50f;
                CaptureUtil.Capture(cam, Path.Combine(dir, $"{prefix}_{n}.png"), 1280, 720);
            }
            cam.transform.SetPositionAndRotation(pos0, rot0);
            cam.fieldOfView = fov0;
            Info("4 Nahaufnahmen der Lagerszenen (Logs/Ashveil/Lagerleben/Szenen)");
            yield break;
        }

        private IEnumerator CaptureCamp(string name)
        {
            Camera cam = Camera.main;
            if (cam == null) yield break;
            yield return new WaitForSecondsRealtime(0.3f);
            string dir = ProjectPaths.Logs("Lagerleben");
            Directory.CreateDirectory(dir);
            (string suffix, Vector3 eye, Vector3 look)[] views =
            {
                ("Uebersicht", new Vector3(24f, 14f, 22f), new Vector3(-6f, 0f, -12f)),
                ("Wachtfeuer", new Vector3(1.5f, 2.6f, 9.5f), new Vector3(0f, 0.6f, 0f)),
                ("Langtische", new Vector3(-7.5f, 2.4f, -19f), new Vector3(-15f, 0.7f, -27.5f)),
                ("Markt", new Vector3(1f, 2f, 3f), new Vector3(9f, 1f, 11f)),
                ("Schmiede_Holzplatz", new Vector3(-7f, 2.2f, -2f), new Vector3(-17f, 1f, -7f)),
                ("Zelte", new Vector3(-6f, 2.4f, 10f), new Vector3(-14f, 0.6f, 16f))
            };
            Vector3 pos0 = cam.transform.position;
            Quaternion rot0 = cam.transform.rotation;
            float fov0 = cam.fieldOfView;
            foreach ((string suffix, Vector3 eye, Vector3 look) in views)
            {
                cam.transform.position = eye;
                cam.transform.LookAt(look);
                cam.fieldOfView = 62f;
                CaptureUtil.Capture(cam, Path.Combine(dir, $"{name}_{suffix}.png"), 1280, 720);
            }
            cam.transform.SetPositionAndRotation(pos0, rot0);
            cam.fieldOfView = fov0;
        }
    }
}
