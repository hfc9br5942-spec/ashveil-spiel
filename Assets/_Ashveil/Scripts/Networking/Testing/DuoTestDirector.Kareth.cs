using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using UnityEngine;
using UnityEngine.AI;

namespace Ashveil.Networking
{
    /// <summary>
    /// Abnahmetest Milestone 5.4 (Kareth) mit Editor-Host A und einem Test-Client B:
    /// Gelaende und Hoehen, NavMesh und Wege zu allen Gebieten, alle Gegnertypen mit Stufen, Stampfer mit
    /// Warnring, Gift der Nadelspeier, Abtauchen, Tarnung + Anspringen der Grauen Mutter (nur Einzelgaenger),
    /// seltene Wiederkehr, Glockenhirte (Werte, Route, Schlag ~70 %, Leine, Trefferchance).
    /// Start: "Ashveil &gt; Test &gt; Kareth (Gelaende, Gegner, Boss)". Bericht: Logs/Ashveil_KarethTest.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        private const string KarethA = "kareth-a", KarethB = "kareth-b";

        private static Vector3 Gnd(float x, float z, float lift = 0.3f)
        {
            float y = Ashveil.Core.WorldGround.SampleHeight(new Vector3(x, 0f, z), 0f);
            return new Vector3(x, y + lift, z);
        }

        private static List<EnemyBrain> Brains(string id) =>
            EnemyBrain.All.Where(b => b != null && b.Definition != null && b.Definition.Id == id && b.GetComponent<Health>().IsAlive).ToList();

        private static EnemyBrain NearestBrain(string id, Vector3 from) =>
            Brains(id).OrderBy(b => (b.transform.position - from).sqrMagnitude).FirstOrDefault();

        private static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>Punkt d Meter vor dem Gegner Richtung "from" (auf dem Boden).</summary>
        private static Vector3 Beside(Vector3 enemy, Vector3 from, float d)
        {
            Vector3 dir = from - enemy;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.back;
            Vector3 p = enemy + dir.normalized * d;
            return Gnd(p.x, p.z);
        }

        private static float YawTo(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            d.y = 0f;
            return d.sqrMagnitude < 0.01f ? 0f : Quaternion.LookRotation(d).eulerAngles.y;
        }

        private static float PathLength(Vector3 from, Vector3 to, out NavMeshPath path, float sample = 6f)
        {
            path = new NavMeshPath();
            if (!NavMesh.SamplePosition(from, out NavMeshHit fh, sample, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(to, out NavMeshHit th, sample, NavMesh.AllAreas) ||
                !NavMesh.CalculatePath(fh.position, th.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                return -1f;
            float len = 0f;
            for (int i = 1; i < path.corners.Length; i++) len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            return len;
        }

        private static bool SegmentsCross(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
        {
            float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
            Vector2 r = p2 - p1, sv = q2 - q1;
            float den = Cross(r, sv);
            if (Mathf.Abs(den) < 1e-6f) return false;
            float t = Cross(q1 - p1, sv) / den, u = Cross(q1 - p1, r) / den;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }

        /// <summary>Slot-Name des Modells (bleibt beim Austauschen gleich), sonst der Objektname.</summary>
        private static string SlotOf(Collider c)
        {
            if (c == null) return "";
            var slot = c.GetComponentInParent<Ashveil.World.ArtSlot>();
            return slot != null ? slot.SlotName : c.name;
        }

        private static bool HitsGiant(RaycastHit h) => SlotOf(h.collider).StartsWith("SM_Giant_");

        /// <summary>
        /// Landschaftstest fuer den Riesen: Module da, Lager frei und ueberdacht, Halle des Hauptmanns begehbar und
        /// gross genug, Wege unter Ellenbogen und Unterarm frei, Schaedeldecke erreichbar, Kratersee, Sichtachsen
        /// zur Fernsilhouette. Ohne Riesen (Layout aus) wird der Abschnitt uebersprungen.
        /// </summary>
        private IEnumerator GiantChecks(NetworkInventory b, Vector3 camp)
        {
            GameObject giant = GameObject.Find("Der versteinerte Beschuetzer");
            Line("");
            Line("[Der versteinerte Beschuetzer]");
            if (giant == null)
            {
                Line("  ·     Riese im Layout ausgeschaltet – Abschnitt uebersprungen");
                yield break;
            }
            string[] modules = { "SM_Giant_Back", "SM_Giant_Chest", "SM_Giant_Head", "SM_Giant_ArmR", "SM_Giant_SkullCap",
                                 "SM_Giant_ArmForearm", "SM_Giant_ArmElbow", "SM_Giant_ArmPillar" };
            var colliders = giant.GetComponentsInChildren<MeshCollider>();
            int found = modules.Count(m => colliders.Any(c => SlotOf(c).StartsWith(m)));
            Check(found == modules.Length, $"Koerpermodule, Schaedeldecke und Armreste mit Kollision ({found}/{modules.Length})");

            // Lager: am Boden frei, darueber der Kopf als Schutzdach (mind. 20 m Luft)
            int roofed = 0, groundFree = 0;
            float lowest = float.MaxValue;
            for (int i = 0; i < 9; i++)
            {
                Vector3 p = i == 0 ? new Vector3(0f, 0.5f, 0f) : new Vector3(Mathf.Sin(i * 0.785f) * 17f, 0.5f, Mathf.Cos(i * 0.785f) * 17f);
                if (!Physics.OverlapSphere(p + Vector3.up * 1.5f, 1.2f).Any(c => SlotOf(c).StartsWith("SM_Giant_"))) groundFree++;
                if (Physics.Raycast(p, Vector3.up, out RaycastHit up, 400f) && HitsGiant(up))
                {
                    roofed++;
                    lowest = Mathf.Min(lowest, up.distance);
                }
            }
            Check(groundFree == 9, $"Lagerflaeche frei von Riesenfels ({groundFree}/9 Messpunkte)");
            Check(roofed >= 3 && lowest >= 20f, $"Beschuetzer beugt sich ueber das Lager ({roofed}/9 Punkte ueberdacht, niedrigster Fels {(roofed > 0 ? lowest : 0f):0} m ueber dem Boden)");

            // Halle des Hauptmanns
            Ashveil.World.WorldPoi hall = Ashveil.World.WorldPoi.Find("poi.kareth.halle_des_hauptmanns");
            Check(hall != null, "Halle des Hauptmanns ist als Ort markiert");
            if (hall != null)
            {
                float len = PathLength(camp, hall.transform.position, out _);
                Check(len > 0f && len < 120f, $"Halle vom zentralen Platz erreichbar (Weg {len:0} m)");
                int onMesh = 0;
                for (int ix = -2; ix <= 2; ix++)
                for (int iz = -2; iz <= 2; iz++)
                {
                    Vector3 p = hall.transform.position + new Vector3(ix * 4f, 0f, iz * 4.5f);
                    if (NavMesh.SamplePosition(p, out NavMeshHit h, 0.8f, NavMesh.AllAreas) && h.position.y > 0.8f) onMesh++;
                }
                Check(onMesh >= 18, $"Halle bietet Platz fuer mehrere Spieler ({onMesh}/25 Stellpunkte begehbar, leicht erhoeht)");
                Check(Physics.Raycast(hall.transform.position + Vector3.up, Vector3.up, out RaycastHit ceil, 60f) && HitsGiant(ceil) && ceil.distance > 6f,
                    $"Halle liegt im Fels (Decke {ceil.distance + 1f:0.0} m ueber dem Boden)");
                bool warm = FindObjectsByType<Light>(FindObjectsSortMode.None)
                    .Count(l => l.type == LightType.Point && Vector3.Distance(l.transform.position, hall.transform.position) < 16f && l.color.r > l.color.b) >= 2;
                Check(warm, "Halle ist warm beleuchtet");
                var bralt = Ashveil.Gameplay.Quests.NpcIdentity.Find("npc.oskar_bralt");
                Check(bralt == null || Vector3.Distance(bralt.transform.position, hall.transform.position) < 16f,
                    "Hauptmann Bralt sitzt in der Halle (keine zweite Rolle)");

                // B laeuft vom Suedtor die Rampe hinauf in die Halle
                yield return Ask(b, "goto", V(new Vector3(0f, 0.3f, -26f), 180f));
                yield return new WaitForSecondsRealtime(1f);
                yield return Ask(b, "walk", "6 1");
                Vector3 bp = b.transform.position;
                Check(bp.z < -52f && bp.y > 0.9f, $"B laeuft vom Suedtor in die Halle (steht bei z {bp.z:0}, Hoehe {bp.y:0.0} m)");
                yield return Ask(b, "goto", V(camp, 0f));
            }

            // Wege unter dem rechten Unterarm (Ostweg) und unter dem Ellenbogenbogen (Salzweg)
            bool UnderGiant(NavMeshPath path, Vector3 near, float radius)
            {
                foreach (Vector3 c in path.corners)
                    if (Flat(c, near) < radius && Physics.Raycast(c + Vector3.up, Vector3.up, out RaycastHit h, 120f) && HitsGiant(h)) return true;
                for (int i = 1; i < path.corners.Length; i++)
                    for (float t = 0f; t <= 1f; t += 0.05f)
                    {
                        Vector3 p = Vector3.Lerp(path.corners[i - 1], path.corners[i], t);
                        if (Flat(p, near) < radius && Physics.Raycast(p + Vector3.up, Vector3.up, out RaycastHit h, 120f) && HitsGiant(h)) return true;
                    }
                return false;
            }
            float eastLen = PathLength(camp, Gnd(300f, -40f), out NavMeshPath eastPath);
            Check(eastLen > 0f && UnderGiant(eastPath, new Vector3(125f, 0f, -40f), 30f), $"Ostweg fuehrt unter dem rechten Arm hindurch ({eastLen:0} m)");
            // Salzweg: vom Weg 30 m vor dem Bogen bis 30 m dahinter – der kuerzeste Weg muss zwischen den Pfeilern hindurch
            Vector3 dirSalt = new Vector3(-0.814f, 0f, -0.581f);
            Vector3 arch = new Vector3(-130f, 0f, -106f);
            float saltLen = PathLength(Gnd(arch.x - dirSalt.x * 30f, arch.z - dirSalt.z * 30f), Gnd(arch.x + dirSalt.x * 30f, arch.z + dirSalt.z * 30f), out NavMeshPath saltPath);
            bool through = false;
            Vector2 legA = new Vector2(-119.6f, -120.6f), legB = new Vector2(-140.4f, -91.4f);
            for (int i = 1; i < saltPath.corners.Length && !through; i++)
                through = SegmentsCross(new Vector2(saltPath.corners[i - 1].x, saltPath.corners[i - 1].z), new Vector2(saltPath.corners[i].x, saltPath.corners[i].z), legA, legB);
            bool overhead = Physics.Raycast(Gnd(arch.x, arch.z, 1f), Vector3.up, out RaycastHit archHit, 60f) && HitsGiant(archHit);
            Check(saltLen > 0f && saltLen < 70f && through && overhead,
                $"Salzweg fuehrt unter dem Ellenbogenbogen hindurch (Abschnitt {saltLen:0} m, Bogen {(overhead ? archHit.distance + 1f : 0f):0} m hoch)");

            // Schaedeldecke: eigener Ort, erreichbar und innen begehbar
            Ashveil.World.WorldPoi skull = Ashveil.World.WorldPoi.Find("poi.kareth.schaedeldecke");
            Check(skull != null, "Schaedeldecke als eigener Ort (poi.kareth.schaedeldecke)");
            if (skull != null)
            {
                float len = PathLength(camp, skull.transform.position, out _, 4f);
                bool roof = Physics.Raycast(skull.transform.position + Vector3.up, Vector3.up, out RaycastHit sh, 60f) && HitsGiant(sh);
                Check(len > 0f && roof, $"In die Schaedeldecke hinein (Weg {len:0} m, gewoelbte Decke {(roof ? sh.distance + 1f : 0f):0} m hoch)");
            }

            // Kratersee auf dem Ruecken
            Ashveil.World.WorldPoi lake = Ashveil.World.WorldPoi.Find("poi.kareth.kratersee");
            Check(lake != null && lake.transform.position.y > 60f, $"Kratersee im Einschlagtrichter ({(lake != null ? lake.transform.position.y : 0f):0} m hoch, Radius {(lake != null ? lake.Radius : 0f):0} m)");

            // Fernsilhouette: vom Aussichtspunkt auf dem Plateau sind Kopf, Ruecken, rechte Schulter und Arm frei sichtbar
            Vector3 eye = Gnd(60f, 598f, 1.7f);
            var marks = new (string name, Vector3 p)[]
            {
                ("Kopf", new Vector3(0f, 70f, 10f)), ("Ruecken", new Vector3(0f, 150f, -150f)),
                ("rechte Schulter", new Vector3(82f, 118f, -72f)), ("rechter Arm", new Vector3(122f, 40f, -30f))
            };
            int visible = 0;
            var seen = new List<string>();
            foreach ((string name, Vector3 p) in marks)
            {
                Vector3 d = p - eye;
                if (Physics.Raycast(eye, d.normalized, out RaycastHit h, d.magnitude + 40f) && HitsGiant(h) && h.distance > d.magnitude - 60f)
                {
                    visible++;
                    seen.Add(name);
                }
            }
            Check(visible >= 3, $"Fernsilhouette vom Plateau-Aussichtspunkt ({visible}/4 frei sichtbar: {string.Join(", ", seen)})");
        }

        private IEnumerator KarethSuite()
        {
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(KarethA)) != null, 30f);
            Check(a != null, "Spieler A (Editor-Host) angemeldet");
            if (a == null) yield break;
            string exe = Ashveil.Core.ProjectPaths.TestClientExe;
            if (!File.Exists(exe)) { Check(false, "Test-Client vorhanden"); yield break; }
            StartClient(exe, KarethB);
            NetworkInventory b = null;
            yield return WaitUntil(() => (b = OnlineInv(KarethB)) != null, 120f);
            Check(b != null, "Spieler B (Test-Client) angemeldet");
            if (b == null) yield break;
            yield return new WaitForSecondsRealtime(2f);
            Health ha = a.GetComponent<Health>(), hb = b.GetComponent<Health>();
            Vector3 camp = GameObject.Find("PlayerSpawn") != null ? GameObject.Find("PlayerSpawn").transform.position : Vector3.zero;

            // ---------------------------------------------------------- Gelaende
            Line("");
            Line("[Gelaende]");
            Terrain terrain = Ashveil.Core.WorldGround.TerrainAt(Vector3.zero);
            Check(terrain != null && terrain.terrainData.size.x >= 1190f && terrain.terrainData.size.z >= 1190f,
                $"Terrain der Senke ({(terrain != null ? terrain.terrainData.size.x : 0):0} x {(terrain != null ? terrain.terrainData.size.z : 0):0} m)");
            if (terrain == null) yield break;
            float hCamp = Gnd(0f, 0f, 0f).y, hPlateau = Gnd(0f, 690f, 0f).y, hMoor = Gnd(40f, 330f, 0f).y,
                  hCliff = Gnd(585f, 100f, 0f).y, hSea = Gnd(-470f, -300f, 0f).y, hWest = Gnd(-480f, 200f, 0f).y;
            Check(Mathf.Abs(hCamp) < 0.3f, $"Wachtfeuer liegt flach (Hoehe {hCamp:0.00} m)");
            Check(hPlateau > 27f && hPlateau < 38f, $"Granitplateau im Norden ({hPlateau:0} m)");
            Check(hMoor < -0.5f, $"Glockenmoor ist eine nasse Senke ({hMoor:0.0} m)");
            Check(hCliff > 50f, $"Felswand des Uhrwerks im Osten ({hCliff:0} m)");
            Check(hSea < -15f, $"Steilkueste faellt ins Meer ({hSea:0} m)");
            Check(hWest > 8f, $"Kiefernhang steigt nach Westen an ({hWest:0} m)");

            // NavMesh an allen Gegnergebieten und auf der Route
            var areas = FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None);
            int areasOk = areas.Count(ar => NavMesh.SamplePosition(ar.transform.position, out _, 6f, NavMesh.AllAreas));
            Check(areasOk == areas.Length, $"NavMesh an allen {areas.Length} Gegnergebieten ({areasOk} ok)");
            var route = FindAnyObjectByType<EnemyRoute>();
            int routeOk = 0;
            if (route != null)
                for (int i = 0; i < route.Count; i++)
                    if (NavMesh.SamplePosition(route.Point(i), out _, 6f, NavMesh.AllAreas)) routeOk++;
            Check(route != null && routeOk == route.Count, $"NavMesh auf der ganzen Route des Glockenhirten ({routeOk}/{(route != null ? route.Count : 0)})");

            // Wege vom Lager zu allen Gebieten (Gegner koennen dorthin, Spieler laufen dort)
            var goals = new (string name, Vector3 p)[]
            {
                ("Kloster (Plateau)", Gnd(0f, 690f)), ("Glockenmoor", Gnd(60f, 330f)), ("Aschenfeld", Gnd(300f, -40f)),
                ("Holzfaellerlager", Gnd(-250f, 240f)), ("Salzgruben", Gnd(-285f, -190f)), ("Uhrwerk-Tor", Gnd(505f, 210f))
            };
            NavMesh.SamplePosition(camp, out NavMeshHit campHit, 5f, NavMesh.AllAreas);
            foreach ((string name, Vector3 p) in goals)
            {
                var path = new NavMeshPath();
                bool found = NavMesh.SamplePosition(p, out NavMeshHit gh, 8f, NavMesh.AllAreas) &&
                             NavMesh.CalculatePath(campHit.position, gh.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                float len = 0f;
                if (found) for (int i = 1; i < path.corners.Length; i++) len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                Check(found, $"Weg vom Lager: {name} ({len:0} m)");
            }

            // ---------------------------------------------------------- Quest-Orte (27.09.): Stuetzpunkte je Stufenband, alle erreichbar
            var sites = Ashveil.World.WorldQuestSite.All.Where(s => s != null).ToList();
            var siteIds = new HashSet<string>(sites.Select(s => s.Id));
            var hubs = sites.Where(s => s.Kind == Ashveil.World.QuestSiteKind.Hub).ToList();
            Check(sites.Count >= 40 && siteIds.Count == sites.Count, $"Quest-Orte mit eindeutigen Kennungen ({sites.Count} Orte, {siteIds.Count} Kennungen)");
            Check(hubs.Count == 5, $"Fuenf Stuetzpunkte (Wachtfeuer, Glockenrast, Grubenlager, Ostwacht, Plateauwacht): {hubs.Count}");
            var orphans = sites.Where(s => s.Kind != Ashveil.World.QuestSiteKind.Hub && s.Kind != Ashveil.World.QuestSiteKind.Hidden &&
                                           !hubs.Any(h => h.Id == s.HubId)).Select(s => s.Id).ToList();
            Check(orphans.Count == 0, "Jeder Quest-Ort gehoert zu einem Stuetzpunkt " + string.Join(" ", orphans));
            int reach = 0, reachWanted = 0;
            var unreachable = new List<string>();
            float longest = 0f;
            foreach (var s in sites)
            {
                if (!s.Reachable || s.Status == Ashveil.World.QuestSiteStatus.Planned) continue;
                reachWanted++;
                // Erreichbar = der Weg endet am Ort (innerhalb Radius + 12 m). In der Mitte stehen oft Gebaeude oder Felsgipfel,
                // deren Dach ein eigenes NavMesh-Stueck ist – dort muss man nicht hinauf.
                var sp = new NavMeshPath();
                bool ok = false;
                if (NavMesh.SamplePosition(s.transform.position, out NavMeshHit sh, Mathf.Max(8f, s.Radius), NavMesh.AllAreas) &&
                    NavMesh.CalculatePath(campHit.position, sh.position, NavMesh.AllAreas, sp) && sp.corners.Length > 0)
                    ok = sp.status == NavMeshPathStatus.PathComplete || Flat(sp.corners[sp.corners.Length - 1], s.transform.position) < s.Radius + 12f;
                if (ok)
                {
                    reach++;
                    float l = 0f;
                    for (int i = 1; i < sp.corners.Length; i++) l += Vector3.Distance(sp.corners[i - 1], sp.corners[i]);
                    longest = Mathf.Max(longest, l);
                }
                else unreachable.Add(s.Id);
            }
            Check(reach == reachWanted, $"Alle gebauten Quest-Orte zu Fuss erreichbar ({reach}/{reachWanted}, laengster Weg {longest:0} m) " + string.Join(" ", unreachable));
            Ashveil.World.WorldQuestSite ash = Ashveil.World.WorldQuestSite.Find("site.kareth.gehoeft");
            int husksAtFarm = ash != null ? Brains("enemy.faded_one").Count(e => Flat(e.transform.position, ash.transform.position) < 25f) : 0;
            Check(ash != null && Flat(ash.transform.position, camp) > 70f && husksAtFarm >= 4,
                $"Gehoeft der Verblichenen vom Tor abgerueckt ({(ash != null ? Flat(ash.transform.position, camp) : 0f):0} m vom Feuer) mit {husksAtFarm} Nahkaempfern fuer Q4");

            // ---------------------------------------------------------- Der versteinerte Beschuetzer (Nachtrag 26.09.)
            yield return GiantChecks(b, campHit.position);
            Line("");
            Line("[Gelaende, Fortsetzung]");

            // Laufen mit Server-Vorhersage die Plateau-Rampe hinauf
            Vector3 rampStart = Gnd(18f, 470f);
            yield return Ask(b, "goto", V(rampStart, 0f));
            yield return new WaitForSecondsRealtime(1f);
            float y0 = b.transform.position.y;
            yield return Ask(b, "walk", "4 1");
            ParsePos(_last.data, out Vector3 bc, out _, out float bMax);
            float climbed = b.transform.position.y - y0;
            Check(climbed > 2f, $"B laeuft die Rampe hinauf ({climbed:0.0} m hoeher)");
            Check(Vector3.Distance(bc, b.transform.position) < 0.5f && bMax < 1f,
                $"Client und Server einig am Hang (Abstand {Vector3.Distance(bc, b.transform.position):0.00} m, groesste Korrektur {bMax:0.00} m)");

            // ---------------------------------------------------------- Gegner
            Line("");
            Line("[Gegner]");
            // 09.10. spaetabends: A starb in mehreren Laeufen vor der Grauen Mutter, auch unverwundbar – woran, zeigte nichts.
            // Darum hier jeder Treffer auf A mit Quelle, dazu der Tod, bis zum Glockenhirten.
            var aTreffer = new List<string>();
            System.Action<DamageInfo> aHit = d => aTreffer.Add($"{Time.time:0.0}s {d.Amount:0} von {(d.Source != null ? d.Source.name : "?")} ({d.WeaponId}{(d.Environmental ? ", Umwelt" : "")}) -> {ha.Current:0}");
            System.Action aTod = () => Line($"·     A stirbt bei {Time.time:0.0}s, Position {a.transform.position}, letzte Treffer: {string.Join(" | ", aTreffer.Skip(Mathf.Max(0, aTreffer.Count - 6)))}");
            ha.Damaged += aHit;
            ha.Died += aTod;
            string[] ids = { "enemy.ash_wolf", "enemy.faded_one", "enemy.faded_gunner", "enemy.peat_brute", "enemy.needle_spitter", "enemy.soot_lynx", "enemy.bell_shepherd" };
            string counts = string.Join(", ", ids.Select(id => $"{id.Replace("enemy.", "")} {Brains(id).Count}"));
            Check(ids.All(id => Brains(id).Count > 0), $"Alle Gegnertypen sind da ({counts})");
            // Kanon 26.09.: Aggro-Profile, keine Social-Aggro
            var nearCampWolves = Brains("enemy.ash_wolf").Where(w => Flat(w.Home, camp) < 130f).ToList();
            var deepWolves = Brains("enemy.ash_wolf").Where(w => Flat(w.Home, camp) >= 180f).ToList();
            Check(nearCampWolves.Count > 0 && nearCampWolves.All(w => w.Aggro == AggroProfile.Defensive),
                $"Woelfe am Lager sind defensiv ({nearCampWolves.Count(w => w.Aggro == AggroProfile.Defensive)}/{nearCampWolves.Count})");
            Check(deepWolves.All(w => w.Aggro == AggroProfile.Territorial) && Brains("enemy.needle_spitter").All(sp => sp.Aggro == AggroProfile.Aggressive),
                "Weiter draussen territorial bzw. aggressiv (Tiefer Wald, Nadelspeier)");
            Check(Brains("enemy.bell_shepherd").All(bs => bs.Aggro == AggroProfile.Encounter), "Glockenhirte folgt Begegnungsregeln");
            // 07.10.: Prüfbilder der Greybox-Gegner in der Spielkamera (Aschwolf ohne Glut, Schützin mit Bogen; Max 07.10.)
            Info($"{EnemyPortraits(a.transform.position, ("enemy.ash_wolf", "Aschwolf"), ("enemy.faded_gunner", "Verblichene_Schuetzin"), ("enemy.faded_one", "Verblichener"))} " +
                 "Gegner-Prüfbilder nach Logs/Ashveil/Gegnerbilder");
            // Zuordnung Gegner -> Gebiet ueber die Gruppe, die EnemyDirector beim Spawnen setzt (area.Group).
            // 02.10.: Seit dem additiven Glockenrast-Einschub liegen gleichartige Gebiete mit eigenen
            // Stufenbaendern nah beieinander; die alte Naeherung "Radius + 30 m" pruefte Tiere gegen das Nachbargebiet.
            bool levelsOk = true;
            int levelChecked = 0;
            foreach (EnemyBrain eb in EnemyBrain.All)
            {
                if (eb == null) continue;
                EnemySpawnArea own = null;
                foreach (EnemySpawnArea ar in areas)
                    if (ar != null && ar.Definition == eb.Definition && ar.Group == eb.Group) { own = ar; break; }
                if (own == null || own.Route != null) continue;
                levelChecked++;
                if (eb.Level < own.MinLevel || eb.Level > own.MaxLevel) { levelsOk = false; Info($"Stufe {eb.Level} bei {own.name} ({own.MinLevel}-{own.MaxLevel})"); }
            }
            levelsOk &= levelChecked > 0;
            Check(levelsOk, $"Stufen je Gebiet ({levelChecked} Gegner ihrem Gebiet zugeordnet und geprueft)");

            // Torfschrat: Stampfer mit Warnring
            EnemyBrain brute = NearestBrain("enemy.peat_brute", Gnd(20f, 300f));
            Check(brute != null, "Torfschrat im Glockenmoor gefunden");
            if (brute != null)
            {
                bool areaBegan = false, areaResolved = false;
                float areaRadius = brute.Definition.areaRadius;
                System.Action<Vector3, float, float> onArea = (c, w, r) => areaBegan = true;
                System.Action<bool, Vector3> onResolved = (hit, p) => { if (areaBegan) areaResolved = true; };
                var stompHits = new List<string>();
                System.Action<DamageInfo> onA = d => { if (d.WeaponId != null && d.WeaponId.EndsWith(".stomp")) stompHits.Add("A"); };
                System.Action<DamageInfo> onB = d => { if (d.WeaponId != null && d.WeaponId.EndsWith(".stomp")) stompHits.Add("B"); };
                brute.AreaAttackBegan += onArea;
                brute.AttackResolved += onResolved;
                ha.Damaged += onA;
                hb.Damaged += onB;
                Vector3 bp = brute.transform.position;
                yield return Ask(a, "goto", V(Beside(bp, camp, 2.2f), YawTo(Beside(bp, camp, 2.2f), bp)));
                yield return Ask(b, "goto", V(Beside(bp, camp, areaRadius + 6f), 0f));
                bool warnSeen = false;
                float t = 0f;
                while (t < 20f && !areaResolved)
                {
                    if (areaBegan && FindObjectsByType<GroundWarning>(FindObjectsSortMode.None).Length > 0) warnSeen = true;
                    t += 0.1f;
                    yield return new WaitForSecondsRealtime(0.1f);
                }
                brute.AreaAttackBegan -= onArea;
                brute.AttackResolved -= onResolved;
                ha.Damaged -= onA;
                hb.Damaged -= onB;
                Check(areaBegan && areaResolved, $"Torfschrat stampft (Flaechenangriff nach {t:0.0} s)");
                Check(warnSeen, "Warnring am Boden waehrend des Ausholens");
                Check(stompHits.Contains("A") && !stompHits.Contains("B"),
                    $"Stampfer trifft nur im Radius {areaRadius:0} m (A nah getroffen: {stompHits.Contains("A")}, B fern: {stompHits.Contains("B")})");
                yield return Ask(a, "goto", V(camp, 0f));
                yield return Ask(b, "goto", V(camp + Vector3.right * 3f, 0f));
                Line($"·     Nach dem Stampfer: Leben A {ha.Current:0}, B {hb.Current:0}, A lebt {ha.IsAlive}");
            }

            // Nadelspeier: Gift aus der Distanz
            EnemyBrain spitter = NearestBrain("enemy.needle_spitter", Gnd(165f, 270f));
            Check(spitter != null, "Nadelspeier am Moorrand gefunden");
            if (spitter != null)
            {
                Vector3 sp = spitter.transform.position;
                Vector3 bPos = Beside(sp, camp, 14f);
                int poisonTicks = 0;
                System.Action<DamageInfo> onPoison = d => { if (d.WeaponId != null && d.WeaponId.EndsWith(".poison")) poisonTicks++; };
                hb.Damaged += onPoison;
                yield return Ask(b, "goto", V(bPos, YawTo(bPos, sp)));
                float t = 0f;
                bool poisoned = false;
                while (t < 25f && poisonTicks < 2)
                {
                    poisoned |= DamageOverTime.IsPoisoned(hb);
                    t += 0.25f;
                    yield return new WaitForSecondsRealtime(0.25f);
                }
                hb.Damaged -= onPoison;
                Check(poisoned, $"Nadelspeier vergiftet B aus {Flat(bPos, sp):0} m ({t:0.0} s)");
                Check(poisonTicks >= 2, $"Gift wirkt ueber Zeit ({poisonTicks} Giftschaeden)");
                yield return Ask(b, "goto", V(camp + Vector3.right * 3f, 0f));
                yield return new WaitForSecondsRealtime(1f);
                DamageOverTime dot = hb.GetComponent<DamageOverTime>();
                if (dot != null) dot.Clear();

                // Bedraengt -> abtauchen und woanders auftauchen
                spitter = NearestBrain("enemy.needle_spitter", sp);
                if (spitter != null)
                {
                    sp = spitter.transform.position;
                    bool dove = false, surfaced = false, untargetable = false;
                    // 09.10. spaetabends: Seit der Gegner-Regel (05 COMBAT-063) gibt der Nadelspeier das weggesprungene B auf und
                    // nimmt A ins Visier, sobald A heranrueckt (vorher hing er am unerreichbaren B und liess A in Ruhe). A starb
                    // daran in drei von fuenf Laeufen vor der Grauen Mutter – auch aus vollem Leben binnen 7 s. Hier zaehlt nur das
                    // Abtauchen (es haengt an der Naehe, nicht am Schaden): A ist solange unverwundbar und danach wieder voll.
                    if (ha.IsAlive) { ha.SetInvulnerableFor(20f); ha.ResetToFull(); }
                    yield return Ask(a, "goto", V(Beside(sp, camp, 2.5f), YawTo(Beside(sp, camp, 2.5f), sp)));
                    t = 0f;
                    while (t < 20f && !surfaced)
                    {
                        if (spitter == null) break;
                        if (spitter.Concealment == EnemyConcealment.Submerged)
                        {
                            dove = true;
                            var ct = spitter.GetComponent<CombatTarget>();
                            untargetable |= ct != null && !ct.IsAttackable;
                        }
                        else if (dove) surfaced = true;
                        else
                        {
                            // A bleibt dicht dran (Nadelspeier weicht zurueck)
                            Vector3 np = Beside(spitter.transform.position, camp, 2.5f);
                            if (Flat(a.transform.position, spitter.transform.position) > 6f) yield return Ask(a, "goto", V(np, YawTo(np, spitter.transform.position)));
                        }
                        t += 0.2f;
                        yield return new WaitForSecondsRealtime(0.2f);
                    }
                    float away = spitter != null ? Flat(spitter.transform.position, a.transform.position) : 0f;
                    Check(dove, $"Bedraengter Nadelspeier taucht ab ({t:0.0} s)");
                    Check(untargetable, "Abgetaucht: nicht anvisierbar");
                    Check(surfaced && away >= 9f, $"Taucht woanders wieder auf ({away:0} m von A)");
                    yield return Ask(a, "goto", V(camp, 0f));
                    DamageOverTime dotA = ha.GetComponent<DamageOverTime>();
                    if (dotA != null) dotA.Clear();
                    if (ha.IsAlive) ha.ResetToFull();
                }
            }

            // Die Graue Mutter: getarnt, jagt Einzelgaenger, springt an
            EnemyBrain lynx = Brains("enemy.soot_lynx").FirstOrDefault();
            Check(lynx != null, "Die Graue Mutter (Russluchs) im Aschenfeld");
            if (lynx != null)
            {
                var lct = lynx.GetComponent<CombatTarget>();
                EnemySpawnArea lynxArea = areas.FirstOrDefault(x => x.Definition == lynx.Definition);
                Check(lynxArea != null && lynxArea.HideoutCount == 3, $"Drei Verstecke ({(lynxArea != null ? lynxArea.HideoutCount : 0)})");
                bool rollsOk = true;
                for (int i = 0; i < 200; i++)
                {
                    float r = lynx.Definition.RollRespawnTime();
                    if (r < 2700f || r > 5400f) rollsOk = false;
                }
                Check(rollsOk, "Seltene Wiederkehr nach 45-90 Minuten");
                Check(lct != null && lct.IsElite, "Als Elite markiert");
                Check(lynx.Concealment == EnemyConcealment.Camouflaged && lct != null && !lct.IsAttackable, "Getarnt und nicht anvisierbar");

                // Zu zweit (A und B nebeneinander, 16 m entfernt): sie wartet
                // 07.10.: In zwei von vier Laeufen schlug dieser Teil fehl, ohne dass das Protokoll den Grund zeigte. Jetzt erst warten,
                // bis sie ruhig im Versteck liegt, ihr Versteck als Bezug nehmen und Zustand und Abstaende ins Protokoll schreiben.
                float settle = 0f;
                while (settle < 30f && lynx != null && !(lynx.Concealment == EnemyConcealment.Camouflaged
                       && (lynx.State == EnemyState.Idle || lynx.State == EnemyState.Wander) && Flat(lynx.transform.position, lynx.Home) <= 4f))
                {
                    settle += 0.5f;
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                // A kommt vom Nadelspeier meist angeschlagen und vergiftet hierher (07.10.: 108–121 von 220 Leben, beim Heranruecken
                // weiter fallend). Stirbt A unterwegs, ist B allein – dann springt sie zu Recht B an, und der Test misst etwas anderes.
                // Darum beide voll und ohne Gift (ohne Heilungsereignis, also ohne Bedrohung fuer Gegner).
                foreach (Health hx in new[] { ha, hb })
                {
                    if (hx == null) continue;
                    if (!hx.IsAlive) Line($"·     {(hx == ha ? "A" : "B")} war vor der Grauen Mutter schon tot");
                    DamageOverTime dx = hx.GetComponent<DamageOverTime>();
                    if (dx != null) dx.Clear();
                    if (hx.IsAlive) hx.ResetToFull();
                }
                System.Func<string> lynxInfo = () =>
                    $"Zustand {lynx.State}, Tarnung {lynx.Concealment}, Abstand A {Flat(a.transform.position, lynx.transform.position):0} m, " +
                    $"B {Flat(b.transform.position, lynx.transform.position):0} m, A–B {Flat(a.transform.position, b.transform.position):0} m, " +
                    $"vom Versteck {Flat(lynx.transform.position, lynx.Home):0} m, Leben A {ha.Current:0}, B {hb.Current:0}";
                Line($"·     Vorher ({settle:0.0} s gewartet): {lynxInfo()}");
                Vector3 lp = lynx.Home;
                // gemeinsam in kleinen Schritten heranruecken (40 -> 16 m): selbst wenn eine Teleportation einen
                // Schritt spaeter ankommt, bleiben beide unter 15 m beieinander – sonst ist einer kurz "allein"
                foreach (float dist in new[] { 40f, 32f, 24f, 16f })
                {
                    Vector3 sa = Beside(lp, camp, dist);
                    Vector3 sb = Gnd(sa.x + 2f, sa.z);
                    StartCoroutine(Ask(b, "goto", V(sb, YawTo(sb, lp))));
                    yield return Ask(a, "goto", V(sa, YawTo(sa, lp)));
                    float w = 0f;
                    while (w < 3f && Flat(b.transform.position, sb) > 2f) { w += 0.1f; yield return new WaitForSecondsRealtime(0.1f); }
                    if (Flat(b.transform.position, sb) > 2f || lynx.Concealment != EnemyConcealment.Camouflaged)
                        Line($"·     Schritt {dist:0} m: B {Flat(b.transform.position, sb):0.0} m neben dem Ziel; {lynxInfo()}");
                }
                yield return new WaitForSecondsRealtime(4f);
                Line($"·     Zu zweit: {lynxInfo()}");
                Check(lynx.Concealment == EnemyConcealment.Camouflaged && lynx.State != EnemyState.Chase && lynx.State != EnemyState.Attack,
                    "Zwei Spieler zusammen: sie bleibt verborgen");
                // B geht -> A ist allein: Sprung
                bool pounced = false;
                System.Action<Vector3, Vector3> onPounce = (f, to) => pounced = true;
                lynx.Pounced += onPounce;
                yield return Ask(b, "goto", V(camp + Vector3.right * 3f, 0f));
                float t = 0f;
                while (t < 15f && !pounced)
                {
                    t += 0.2f;
                    yield return new WaitForSecondsRealtime(0.2f);
                }
                lynx.Pounced -= onPounce;
                if (!pounced) Line($"·     Allein, kein Sprung: {lynxInfo()}");
                Check(lynx.Concealment == EnemyConcealment.None, "A allein: sie zeigt sich");
                Check(pounced, $"Sie springt A an ({t:0.0} s)");
                yield return Ask(a, "goto", V(camp, 0f));
                t = 0f;
                while (t < 40f && lynx != null && lynx.Concealment != EnemyConcealment.Camouflaged)
                {
                    t += 0.5f;
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                Check(lynx != null && lynx.Concealment == EnemyConcealment.Camouflaged, $"Zurueck im Versteck wieder getarnt ({t:0} s)");
            }

            // ---------------------------------------------------------- Glockenhirte
            Line("");
            ha.Damaged -= aHit;
            ha.Died -= aTod;
            Line("[Der Glockenhirte]");
            EnemyBrain boss = Brains("enemy.bell_shepherd").FirstOrDefault();
            Check(boss != null, "Glockenhirte ist unterwegs");
            if (boss != null)
            {
                var bh = boss.GetComponent<Health>();
                var bct = boss.GetComponent<CombatTarget>();
                Check(bct.Level == 34 && bct.LevelLabel == "??" && bct.IsElite, $"Stufe {bct.Level}, Anzeige \"{bct.LevelLabel}\" (Totenkopf), Elite");
                Check(Mathf.Approximately(bh.Max, 2100000f), $"Leben {bh.Max:N0}");
                Check(boss.Route != null && boss.Route.Length > 520f && boss.Route.Length < 700f, $"Feste Route ({(boss.Route != null ? boss.Route.Length : 0):0} m)");
                Check(boss.GetComponent<EnemyColossusVisual>() != null, "Schwere Schritte mit Beben und Glocken (Darstellung)");
                Vector3 p0 = boss.transform.position;
                float t = 0f;
                while (t < 25f && Flat(boss.transform.position, p0) < 4f)
                {
                    t += 0.5f;
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                Check(Flat(boss.transform.position, p0) >= 4f && boss.Route.DistanceTo(boss.transform.position) < 12f,
                    $"Wandert seine Route ab ({Flat(boss.transform.position, p0):0} m in {t:0} s, Abstand zur Route {boss.Route.DistanceTo(boss.transform.position):0} m)");
                Check(Mathf.Approximately(CombatResolver.HitChance(5, 34), 0.05f) && Mathf.Approximately(CombatResolver.HitChance(5, 9), 1f),
                    $"Trefferchance Stufe 5 gegen 34: {CombatResolver.HitChance(5, 34):P0} (gegen Stufe 9: {CombatResolver.HitChance(5, 9):P0})");

                // Schlag (06.10. spaetabends): Block fuer diese Messung aus (Zufall 10 %), damit der Wert nicht streut. Der Schaden wird gegen
                // die Werte des Glockenhirten und die Ruestung von A geprueft. Das alte Ziel "~70 %" stammte aus der Zeit vor P2
                // (Stufe 5 hatte 170 Leben); seit P2 und dem Kampfumbau hat der Eidwaechter mit Schild 220 Leben und 11 % Ruestung.
                float hpMax = ha.Max;
                float firstHit = -1f;
                var eqA = a.GetComponent<Ashveil.Gameplay.Items.CharacterEquipment>();
                if (eqA != null) eqA.BlockRollOverride = 1f;
                System.Action<DamageInfo> onHit = d => { if (firstHit < 0f && d.Source == boss.gameObject) firstHit = d.Amount; };
                ha.Damaged += onHit;
                Vector3 bpos = boss.transform.position;
                Vector3 pa = Beside(bpos, boss.transform.position + boss.transform.forward * 10f, 4.5f);
                yield return Ask(a, "goto", V(pa, YawTo(pa, bpos)));
                t = 0f;
                while (t < 20f && firstHit < 0f)
                {
                    // A bleibt in Reichweite, falls er weiterlaeuft
                    if (Flat(a.transform.position, boss.transform.position) > 7f && ha.IsAlive)
                    {
                        Vector3 np = Beside(boss.transform.position, a.transform.position, 4.5f);
                        yield return Ask(a, "goto", V(np, YawTo(np, boss.transform.position)));
                    }
                    t += 0.2f;
                    yield return new WaitForSecondsRealtime(0.2f);
                }
                ha.Damaged -= onHit;
                if (eqA != null) eqA.BlockRollOverride = -1f;
                float frac = firstHit / Mathf.Max(1f, hpMax);
                Check(firstHit > 0f, $"Glockenhirte greift an ({t:0.0} s)");
                EnemyDefinition bdef = boss.Definition;
                float bonus = bdef.damagePerLevel * (boss.Level - 1);
                float scale = boss.DamageScale;
                // wie im Spiel: Wurf gerundet, mal Skalierung gerundet, Ruestung gerundet (der erste Schlag nach dem Angriff ist nie die Flaeche)
                float hitMin = Mathf.Max(1f, Mathf.Round(eqA != null ? eqA.Mitigate(Mathf.Round(Mathf.Round(bdef.minDamage + bonus) * scale)) : bdef.minDamage + bonus));
                float hitMax = Mathf.Max(1f, Mathf.Round(eqA != null ? eqA.Mitigate(Mathf.Round(Mathf.Round(bdef.maxDamage + bonus) * scale)) : bdef.maxDamage + bonus));
                Check(firstHit >= hitMin - 1f && firstHit <= hitMax + 1f,
                    $"Schlag folgt seinen Werten ({bdef.minDamage:0}–{bdef.maxDamage:0}, nach Rüstung {hitMin:0}–{hitMax:0}): {firstHit:0}");
                Check(frac >= 0.4f && frac < 0.9f, $"Ein Schlag nimmt Stufe 5 (Eidwächter mit Schild) mindestens 40 % und tötet nicht aus vollem Leben (hier {firstHit:0} von {hpMax:0} = {frac:P0})");
                Check(boss.State == EnemyState.Chase || boss.State == EnemyState.Attack || !ha.IsAlive, "Er verfolgt A");

                // Leine: 80 m von der Route -> Umkehr
                yield return Ask(a, "goto", V(camp, 0f));
                t = 0f;
                // 07.10.: Verfolgen und Heimweg dauern meist 57–58 s; einmal waren es mehr als 60 s (3 m vor der Route) – Grenze jetzt 90 s
                while (t < 90f && (boss.State == EnemyState.Chase || boss.State == EnemyState.Attack || boss.State == EnemyState.Return))
                {
                    t += 0.5f;
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                float routeDist = boss.Route.DistanceTo(boss.transform.position);
                if (!(boss.State == EnemyState.Idle || boss.State == EnemyState.Wander))
                {
                    // 07.10.: einmal nach 60 s noch nicht zurueck (3 m von der Route) – Zustand fuer die Ursachensuche festhalten
                    NavMeshAgent ag = boss.GetComponent<NavMeshAgent>();
                    Line($"·     Leine: Zustand {boss.State}, {Flat(boss.transform.position, boss.Home):0.0} m vom Heimpunkt" +
                         (ag != null ? $", Agent aktiv {ag.enabled}, auf NavMesh {ag.enabled && ag.isOnNavMesh}, Pfad {(ag.enabled && ag.isOnNavMesh ? ag.pathStatus.ToString() : "-")}, " +
                                       $"Rest {(ag.enabled && ag.isOnNavMesh ? ag.remainingDistance : -1f):0.0} m, gestoppt {(ag.enabled && ag.isOnNavMesh && ag.isStopped)}, " +
                                       $"Tempo {(ag.enabled && ag.isOnNavMesh ? ag.velocity.magnitude : 0f):0.0} m/s" : ""));
                }
                Check(boss.State == EnemyState.Idle || boss.State == EnemyState.Wander, $"Leine: kehrt zur Route zurueck ({t:0} s, {routeDist:0} m von der Route)");
                Check(Flat(boss.transform.position, camp) > 150f, $"Keine Verfolgung bis ins Lager ({Flat(boss.transform.position, camp):0} m entfernt)");
                Check(bh.Current >= bh.Max * 0.999f, "Wieder volle Lebenspunkte");
            }

            yield return new WaitForSecondsRealtime(1f);
            Info($"A: Leben {ha.Current:0}/{ha.Max:0}, B: Leben {hb.Current:0}/{hb.Max:0}");
        }

        /// <summary>
        /// Prüfbilder von Greybox-Gegnern in der Spielkamera (Dreiviertelansicht, Licht wie gerade), je Art das nächste sichtbare
        /// Exemplar. Für Änderungen am Aussehen, die sonst niemand ansieht (07.10.: Aschwolf ohne Glut, Schützin mit Bogen).
        /// Bilder: Logs/Ashveil/Gegnerbilder/&lt;Name&gt;.png. Rückgabe: Zahl der Bilder.
        /// </summary>
        private int EnemyPortraits(Vector3 near, params (string id, string name)[] kinds)
        {
            Camera cam = Camera.main;
            if (cam == null) return 0;
            string dir = Ashveil.Core.ProjectPaths.Logs("Gegnerbilder");
            Directory.CreateDirectory(dir);
            Vector3 p0 = cam.transform.position;
            Quaternion r0 = cam.transform.rotation;
            float f0 = cam.fieldOfView;
            int n = 0;
            foreach ((string id, string name) in kinds)
            {
                foreach (EnemyBrain e in Brains(id).Where(x => x != null && x.State != EnemyState.Dead).OrderBy(x => Flat(x.transform.position, near)))
                {
                    Transform vis = e.transform.Find("Visual");
                    Renderer[] rs = (vis != null ? vis : e.transform).GetComponentsInChildren<Renderer>(false)
                        .Where(r => r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer)).ToArray();
                    if (rs.Length == 0) continue;
                    Bounds bb = rs[0].bounds;
                    foreach (Renderer r in rs) bb.Encapsulate(r.bounds);
                    float size = Mathf.Clamp(bb.size.magnitude, 0.8f, 6f);
                    Vector3 side = Quaternion.Euler(0f, 35f, 0f) * e.transform.forward;
                    side.y = 0f;
                    side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.forward;
                    Vector3 pos = bb.center + side * (size * 1.6f) + Vector3.up * (size * 0.3f);
                    cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(bb.center - pos));
                    cam.fieldOfView = 40f;
                    Texture2D tex = Grab(cam, 960, 960);
                    if (tex != null)
                    {
                        File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                        Destroy(tex);
                        n++;
                    }
                    break;
                }
            }
            cam.transform.SetPositionAndRotation(p0, r0);
            cam.fieldOfView = f0;
            return n;
        }
    }
}
