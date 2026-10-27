using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Quests;
using Ashveil.World;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Ashveil.Networking
{
    /// <summary>
    /// #29 Begehung Kareth v4 (Welle 0, 07.10.2026; Kareth-Pass §Y „Steile Wegstellen prüfen … danach eine Begehungsprüfung mit
    /// dem Charaktermotor“). Ein Prüfkörper mit denselben Maßen und Werten wie die Spielfigur (CharacterController + CharacterMotor,
    /// kopiert von der angemeldeten Figur) läuft im 50-Hz-Takt des Servers, aber schneller als Echtzeit:
    ///   1. Baumstämme: Vorlagen mit Stamm-Kollider, drei Bäume am Start werden angelaufen.
    ///   2. Alle Wege der Entwurfsdaten in beide Richtungen (bis 25 m vor eine Schwelle am Ausgang). Gemeldet werden Hänger (mit
    ///      Hindernis), Rutschen am Hang (steiler als der Motor erlaubt), Schwimmen, Stürze über 2 m und Abdrängen neben den Weg;
    ///      dazu Reisezeit und die steilste Stelle, die die Figur wirklich erlebt.
    ///   3. Sperren an den Ausgängen (WORLD-114, Variante b): ob die Figur auf dem Weg vom Rand des Sperrbereichs an der Schwelle
    ///      vorbeikommt (nur Hinweis).
    ///   4. Auftragsorte, Erkundungsziele, Figuren und Jagdgebiete: vom nächsten Wegpunkt den NavMesh-Weg mit dem Motor ablaufen.
    ///      Erreicht heißt wie im Spiel: im Radius des Orts bzw. Erkundungsziels, in Gesprächsweite der Figur, im Jagdgebiet.
    /// Funde im Sperrbereich (120 m um eine Schwelle), an Figuren und am Bauwerk am Ende eines Wegs (z. B. versiegeltes Tor des
    /// Uhrwerks) gelten als Hinweis, nicht als Fehler.
    /// Start: Auftrag „spieltest begehung“ (Szene Kareth v4, ein Spieler). Bericht: Logs/…/Ashveil_BegehungTest.txt,
    /// alle Funde zusätzlich als CSV unter Logs/…/Begehung/.
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string BegehungFlag = "begehung";
        private static bool _begehungMode;
        private const string BegehungProfile = "duo-begehung";

        private const float BgDt = 0.02f;              // Netz-Takt 50 Hz wie der Server (NetworkSetupBuilder)
        private const int BgStepsPerFrame = 1200;
        private const float BgLookahead = 4f;
        private const float BgSperrZone = 120f;
        private const float BgStuckSeconds = 3f;
        private const int BgMaxSkips = 25;
        /// <summary>Bleibt die Figur auf den letzten Metern eines Wegs an einem Bauwerk stehen, ist sie am Ziel (Tor, Turm).</summary>
        private const float BgEndeBauwerk = 12f;
        private static readonly Vector2 BgHort = new Vector2(-1705f, -1870f);   // Hort der Grauen Mutter: bewusst auf dem Klettergrat (Max 06.10.)
        private const float BgHortRadius = 90f;

        [Serializable] private sealed class BgPlan { public float x0, z0, size; public BgWeg[] wege; public BgSchwelle[] schwellen; }
        [Serializable] private sealed class BgWeg { public string name, typ; public float breite, steigung; public float[] xyz; }
        [Serializable] private sealed class BgSchwelle { public string typ, weg; public float x, z, yaw, breite; }

        private sealed class BgRoute
        {
            public string Name, Typ;
            public float Breite, PlanProzent;
            public Wegstrecke Line;
            public float[] PlanY;
            public BgSchwelle Schwelle;
            public float SchwelleS = -1f;
            public int OutDir;
            public float InnerA, InnerB;
        }

        private sealed class BgEvent
        {
            public string Art, Detail;
            public Vector3 Pos;
            public float S;
            public bool Sperre, Figur, Ziel;
        }

        private sealed class BgLauf
        {
            public string Name;
            public float Laenge, Zeit, Fortschritt, Steilste;
            public Vector3 SteilPos;
            public bool Erreicht, Abbruch, Geschwommen;
            public int Spruenge;
            public readonly List<BgEvent> Events = new List<BgEvent>();
            public bool Fehlerfrei => Erreicht && !Abbruch && Events.All(e => e.Sperre || e.Figur || e.Ziel);
        }

        private readonly List<string> _bgCsv = new List<string>();
        private int _bgMask = Physics.DefaultRaycastLayers;
        private float _bgSlopeLimit = 50f;

        private IEnumerator BegehungSuite()
        {
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(BegehungProfile)) != null, 40f);
            Check(a != null, "Spieler angemeldet");
            if (a == null) yield break;
            yield return new WaitForSecondsRealtime(2f);
            a.GetComponent<Health>()?.SetInvulnerableFor(7200f);
            var cc0 = a.GetComponent<CharacterController>();
            var m0 = a.GetComponent<CharacterMotor>();
            if (cc0 == null || m0 == null)
            {
                Check(false, "Spielfigur hat CharacterController und Charaktermotor");
                yield break;
            }

            BgPlan plan = BgLoadPlan(out string planFehler);
            Check(plan != null && plan.wege != null && plan.wege.Length > 0,
                plan != null && plan.wege != null ? $"Entwurfsdaten gelesen: {plan.wege.Length} Wege, {(plan.schwellen != null ? plan.schwellen.Length : 0)} Schwellen" : "Entwurfsdaten lesbar – " + planFehler);
            if (plan == null || plan.wege == null) yield break;
            List<BgRoute> routes = BgRoutes(plan);

            // ---------------------------------------------------------- Prüfkörper mit den Werten der Spielfigur
            var go = new GameObject("Begehung (Prüfkörper, nur Test)");
            go.SetActive(false);
            go.layer = a.gameObject.layer;
            var cc = go.AddComponent<CharacterController>();
            cc.height = cc0.height;
            cc.radius = cc0.radius;
            cc.center = cc0.center;
            cc.slopeLimit = cc0.slopeLimit;
            cc.stepOffset = cc0.stepOffset;
            cc.skinWidth = cc0.skinWidth;
            cc.minMoveDistance = cc0.minMoveDistance;
            var motor = go.AddComponent<CharacterMotor>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(m0.Settings), motor.Settings);
            var probe = go.AddComponent<BegehungsKoerper>();
            go.transform.position = a.transform.position + new Vector3(3f, 0.2f, 3f);
            go.SetActive(true);
            _bgMask = Physics.DefaultRaycastLayers & ~(1 << a.gameObject.layer);
            _bgSlopeLimit = cc.slopeLimit;
            Info($"Prüfkörper wie die Figur: Höhe {cc.height:0.00} m, Radius {cc.radius:0.00} m, Hang bis {cc.slopeLimit:0}°, Stufe {cc.stepOffset:0.00} m, " +
                 $"Laufen {motor.Settings.runSpeed:0.0} m/s, Takt {1f / BgDt:0} Hz");
            _bgCsv.Clear();
            _bgCsv.Add("Lauf;Art;x;y;z;Bogen_m;Sperrbereich;Figur;Hinweis");

            // ---------------------------------------------------------- 1. Baumstämme
            Line("");
            Line("[Baumstämme]");
            yield return BgBaeume(motor, probe, a.transform.position);

            // ---------------------------------------------------------- 2. Wege in beide Richtungen
            Line("");
            Line("[Wege mit dem Charaktermotor]");
            var reise = new List<string>();
            float km = 0f;
            foreach (BgRoute r in routes)
            {
                float laenge = r.InnerB - r.InnerA;
                if (laenge < 5f)
                {
                    Info($"{r.Name}: innerhalb der Schwelle kürzer als 5 m – ausgelassen");
                    continue;
                }
                km += laenge / 1000f;
                foreach (bool hin in new[] { true, false })
                {
                    float s0 = hin ? r.InnerA : r.InnerB, s1 = hin ? r.InnerB : r.InnerA;
                    Vector2 pa = r.Line.PointAt(s0), pb = r.Line.PointAt(s1);
                    var lauf = new BgLauf { Name = $"{r.Name} {(hin ? "hin" : "zurück")} ({pa.x:0}/{pa.y:0} → {pb.x:0}/{pb.y:0})" };
                    yield return BgWalk(motor, probe, r, s0, s1, lauf, true, laenge / 2.5f + 30f);
                    BgReport(lauf, r);
                    if (hin) reise.Add($"{r.Name} {laenge:0} m: {BgMinSek(lauf.Zeit)} min");
                }
            }

            // ---------------------------------------------------------- 3. Sperren an den Ausgängen
            Line("");
            Line("[Sperren an den Ausgängen (WORLD-114; Variante b: Umgehung im Gelände ist bekannt)]");
            foreach (BgRoute r in routes)
            {
                if (r.Schwelle == null) continue;
                yield return BgSperre(motor, probe, r);
            }

            // ---------------------------------------------------------- 4. Auftragsorte, Ziele, Figuren, Jagdgebiete
            Line("");
            Line("[Ziele zu Fuß (NavMesh-Weg vom nächsten Wegpunkt, mit dem Charaktermotor abgelaufen)]");
            yield return BgZiele(motor, probe, routes);

            // ---------------------------------------------------------- Zusammenfassung
            Line("");
            Line("[Reisezeiten zu Fuß (Laufen, ohne Reittier)]");
            foreach (string s in reise) Info(s);
            Info($"Wege zusammen {km:0.0} km (je Richtung, bis vor die Schwellen)");
            try
            {
                string dir = Ashveil.Core.ProjectPaths.Logs("Begehung");
                Directory.CreateDirectory(dir);
                string csv = Path.Combine(dir, $"Begehung_{DateTime.Now:yyyy-MM-dd_HHmm}.csv");
                File.WriteAllLines(csv, _bgCsv, new UTF8Encoding(true));
                Info("Alle Funde: " + csv);
            }
            catch (Exception e) { Info("CSV nicht schreibbar: " + e.Message); }
            Destroy(go);
        }

        // ================================================================== Daten

        private static BgPlan BgLoadPlan(out string fehler)
        {
            fehler = null;
            string path = Path.Combine(Application.dataPath, "_Ashveil", "Data", "KarethV4", "KarethV4_Plan.json");
            if (!File.Exists(path))
            {
                fehler = "fehlt: " + path;
                return null;
            }
            try { return JsonUtility.FromJson<BgPlan>(File.ReadAllText(path)); }
            catch (Exception e)
            {
                fehler = e.Message;
                return null;
            }
        }

        private static List<BgRoute> BgRoutes(BgPlan plan)
        {
            var list = new List<BgRoute>();
            foreach (BgWeg w in plan.wege)
            {
                if (w?.xyz == null || w.xyz.Length < 6) continue;
                var pts = new List<Vector2>();
                var ys = new List<float>();
                for (int i = 0; i + 2 < w.xyz.Length; i += 3)
                {
                    var q = new Vector2(w.xyz[i], w.xyz[i + 2]);
                    if (pts.Count > 0 && (q - pts[pts.Count - 1]).sqrMagnitude <= 1e-6f) continue;   // wie Wegstrecke
                    pts.Add(q);
                    ys.Add(w.xyz[i + 1]);
                }
                if (pts.Count < 2) continue;
                var r = new BgRoute
                {
                    Name = w.name, Typ = w.typ, Breite = w.breite > 0f ? w.breite : 3f,
                    PlanProzent = w.steigung > 0f ? (w.steigung <= 1f ? w.steigung * 100f : w.steigung) : 0f,
                    Line = new Wegstrecke(pts), PlanY = ys.ToArray()
                };
                r.InnerA = 0f;
                r.InnerB = r.Line.Length;
                BgSchwelle sw = plan.schwellen?.FirstOrDefault(x => x != null && x.weg == w.name);
                if (sw != null)
                {
                    r.Schwelle = sw;
                    r.SchwelleS = r.Line.Project(new Vector2(sw.x, sw.z), out _);
                    r.OutDir = r.SchwelleS > r.Line.Length * 0.5f ? 1 : -1;
                    if (r.OutDir > 0) r.InnerB = Mathf.Max(0f, r.SchwelleS - 25f);
                    else r.InnerA = Mathf.Min(r.Line.Length, r.SchwelleS + 25f);
                }
                list.Add(r);
            }
            return list;
        }

        private static float BgPlanY(BgRoute r, float s)
        {
            int i = r.Line.SegmentAt(s);
            float a = r.Line.ArcAt(i), b = r.Line.ArcAt(i + 1);
            float t = b - a > 1e-6f ? Mathf.Clamp01((s - a) / (b - a)) : 0f;
            return Mathf.Lerp(r.PlanY[i], r.PlanY[i + 1], t);
        }

        /// <summary>Boden unter (x, z): Strahl von knapp über Gelände bzw. Wegplan (Brückendeck) nach unten, Spielerfiguren ausgenommen.</summary>
        private bool BgGround(float x, float z, float hintY, out Vector3 p)
        {
            Terrain t = Terrain.activeTerrain;
            float ty = t != null ? t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y : hintY;
            float top = Mathf.Max(ty, hintY) + 2.2f;
            if (Physics.Raycast(new Vector3(x, top, z), Vector3.down, out RaycastHit hit, 80f, _bgMask, QueryTriggerInteraction.Ignore))
            {
                p = hit.point + Vector3.up * 0.08f;
                return true;
            }
            p = new Vector3(x, ty + 0.08f, z);
            return false;
        }

        private void BgTeleport(CharacterMotor motor, BgRoute r, float s, int dir)
        {
            Vector2 q = r.Line.PointAt(s), d = r.Line.DirectionAt(s) * dir;
            BgGround(q.x, q.y, BgPlanY(r, s), out Vector3 p);
            motor.Teleport(p, Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg);
        }

        // ================================================================== Ein Lauf

        private IEnumerator BgWalk(CharacterMotor motor, BegehungsKoerper probe, BgRoute r, float sFrom, float sTo, BgLauf lauf, bool allowSkip, float timeLimit,
            Func<Vector3, bool> zielErreicht = null)
        {
            // Ziele (NavMesh-Wege): kürzer vorausschauen (Ecken liegen dicht an Hindernissen), unterwegs ankommen, kein „Ende am
            // Bauwerk“ – das gilt nur für Wege, die an einem Bauwerk enden (Uhrwerk-Tor). Seit 07.10. (2. Lauf #29).
            bool istZiel = zielErreicht != null;
            Wegstrecke w = r.Line;
            int dir = sTo >= sFrom ? 1 : -1;
            float length = Mathf.Abs(sTo - sFrom);
            float sLo = Mathf.Min(sFrom, sTo), sHi = Mathf.Max(sFrom, sTo);
            lauf.Laenge = length;
            BgTeleport(motor, r, sFrom, dir);
            probe.ResetContacts();
            float sCur = sFrom, best = 0f, markProg = 0f, markT = 0f, simT = 0f, nextSample = 0f;
            float recoverStart = -1f;
            BgEvent stuck = null;
            float steepT = 0f, airT = 0f, takeoffY = 0f, offT = 0f;
            bool steepSeen = false, swimming = false, airborne = false, offSeen = false;
            BgEvent swimEvt = null;
            Vector3 swimStart = Vector3.zero;
            var gs = new List<float>();
            var gy = new List<float>();
            var gp = new List<Vector3>();
            float halfWidth = r.Breite * 0.5f;
            int steps = 0;

            void Skip(float sSkip, string warum)
            {
                lauf.Spruenge++;
                BgTeleport(motor, r, sSkip, dir);
                probe.ResetContacts();
                sCur = sSkip;
                best = Mathf.Max(best, Mathf.Abs(sSkip - sFrom));
                markProg = best;
                markT = simT;
                recoverStart = -1f;
                steepT = airT = offT = 0f;
                steepSeen = swimming = airborne = offSeen = false;
                if (stuck != null && warum != null) stuck.Detail += warum;
            }

            while (true)
            {
                Vector3 pos = motor.transform.position;
                var p2 = new Vector2(pos.x, pos.z);
                float lo = dir > 0 ? sCur - 5f : sCur - 30f, hi = dir > 0 ? sCur + 30f : sCur + 5f;
                sCur = w.Project(p2, Mathf.Max(lo, sLo), Mathf.Min(hi, sHi), out float lateral);
                float prog = dir > 0 ? sCur - sFrom : sFrom - sCur;
                if (prog > best) best = prog;
                if (best >= nextSample)
                {
                    gs.Add(best);
                    gy.Add(pos.y);
                    gp.Add(pos);
                    nextSample = best + 1f;
                }
                if (best >= length - 1.5f || (istZiel && steps % 5 == 0 && zielErreicht(pos)))
                {
                    lauf.Erreicht = true;
                    break;
                }
                if (simT >= timeLimit)
                {
                    BgAdd(lauf, r, "Zeit", pos, sCur, $"Zeitlimit {timeLimit:0} s nach {best:0} von {length:0} m");
                    break;
                }

                // Vorausschauen wie ein Spieler, der dem Weg folgt: in Kehren (Richtung dreht über 45° bis zum Zielpunkt) und auf
                // NavMesh-Wegen nur 1,5 m, sonst 4 m – sonst schneidet der Prüfkörper die Kehre über die steile Innenböschung.
                float look = istZiel ? 1.5f : BgLookahead;
                float sT = Mathf.Clamp(sCur + dir * look, sLo, sHi);
                if (!istZiel && Vector2.Angle(w.DirectionAt(sCur), w.DirectionAt(sT)) > 45f) sT = Mathf.Clamp(sCur + dir * 1.5f, sLo, sHi);
                Vector2 to = w.PointAt(sT) - p2;
                float yaw = to.sqrMagnitude > 1e-4f ? Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg : motor.State.Yaw;
                var cmd = new MoveCommand { Forward = 1f, SteerWithCamera = true, DesiredYaw = yaw };

                // Fortschritt, Hänger, Ausweichen, Überspringen
                if (best - markProg >= 1f)
                {
                    markProg = best;
                    markT = simT;
                    if (recoverStart >= 0f && stuck != null) stuck.Detail += " – nach Ausweichen frei";
                    recoverStart = -1f;
                }
                else if (recoverStart < 0f && simT - markT > BgStuckSeconds)
                {
                    string hindernis = BgObstacle(probe, simT, out bool figur);
                    if (!istZiel && length - best < BgEndeBauwerk && !figur && probe.LastObstacle != null && !(probe.LastObstacle is TerrainCollider) &&
                        simT - probe.LastTime < 1.5f)
                    {
                        BgEvent ende = BgAdd(lauf, r, "Ende am Bauwerk", pos, sCur, hindernis);
                        ende.Ziel = true;
                        lauf.Erreicht = true;
                        break;
                    }
                    stuck = BgAdd(lauf, r, "hängt", pos, sCur, hindernis);
                    stuck.Figur = figur;
                    recoverStart = simT;
                }
                if (recoverStart >= 0f)
                {
                    float rt = simT - recoverStart;
                    if (rt < 1.2f) cmd.Jump = steps % 25 == 0;
                    else if (rt < 2.6f) { cmd.Strafe = -1f; cmd.Forward = 0.6f; }
                    else if (rt < 4f) { cmd.Strafe = 1f; cmd.Forward = 0.6f; }
                    else
                    {
                        if (!allowSkip || lauf.Spruenge >= BgMaxSkips)
                        {
                            if (stuck != null) stuck.Detail += " – Abbruch";
                            lauf.Abbruch = true;
                            break;
                        }
                        Skip(Mathf.Clamp(sFrom + dir * (best + 6f), sLo, sHi), " – 6 m übersprungen");
                        continue;
                    }
                }

                probe.SimTime = simT;
                motor.Simulate(cmd, BgDt);
                simT += BgDt;
                steps++;

                // Zustände nach dem Schritt
                pos = motor.transform.position;
                bool swimNow = motor.IsSwimming;
                if (motor.IsOnSteepSlope)
                {
                    steepT += BgDt;
                    if (steepT >= 0.4f && !steepSeen)
                    {
                        BgAdd(lauf, r, "rutscht", pos, sCur, $"Hang steiler als {_bgSlopeLimit:0}°");
                        steepSeen = true;
                    }
                }
                else
                {
                    steepT = 0f;
                    steepSeen = false;
                }
                if (swimNow && !swimming)
                {
                    swimming = true;
                    swimStart = pos;
                    swimEvt = BgAdd(lauf, r, "schwimmt", pos, sCur, "ins Wasser");
                    lauf.Geschwommen = true;
                }
                else if (!swimNow && swimming)
                {
                    swimming = false;
                    if (swimEvt != null) swimEvt.Detail = $"{Vector3.Distance(swimStart, pos):0} m im Wasser";
                }
                bool grounded = motor.State.Grounded || swimNow;
                if (!grounded && !airborne)
                {
                    airborne = true;
                    takeoffY = pos.y;
                    airT = 0f;
                }
                else if (airborne)
                {
                    airT += BgDt;
                    if (grounded)
                    {
                        airborne = false;
                        float drop = takeoffY - pos.y;
                        if (drop > 2f) BgAdd(lauf, r, "stürzt", pos, sCur, $"{drop:0.0} m tief");
                    }
                    else if (airT > 8f)
                    {
                        stuck = BgAdd(lauf, r, "fällt", pos, sCur, "8 s im Fall (Loch im Boden?)");
                        if (!allowSkip || lauf.Spruenge >= BgMaxSkips)
                        {
                            lauf.Abbruch = true;
                            break;
                        }
                        Skip(Mathf.Clamp(sFrom + dir * (best + 6f), sLo, sHi), " – zurückgesetzt");
                        continue;
                    }
                }
                if (lateral > halfWidth + 3f && !swimNow)
                {
                    offT += BgDt;
                    if (offT > 2f && !offSeen)
                    {
                        BgAdd(lauf, r, "abgedrängt", pos, sCur, $"{lateral:0} m neben der Wegmitte");
                        offSeen = true;
                    }
                }
                else
                {
                    offT = 0f;
                    offSeen = false;
                }

                if (steps % BgStepsPerFrame == 0) yield return null;
            }
            if (swimming && swimEvt != null) swimEvt.Detail = $"{Vector3.Distance(swimStart, motor.transform.position):0} m im Wasser (bis zum Ende)";
            lauf.Zeit = simT;
            lauf.Fortschritt = best;
            lauf.Steilste = Wegstrecke.WorstGrade(gs, gy, 10f, out int at);
            if (at >= 0) lauf.SteilPos = gp[at];
        }

        private BgEvent BgAdd(BgLauf lauf, BgRoute r, string art, Vector3 pos, float s, string detail)
        {
            var e = new BgEvent { Art = art, Pos = pos, S = s, Detail = detail ?? "" };
            if (r != null && r.SchwelleS >= 0f && Mathf.Abs(s - r.SchwelleS) < BgSperrZone) e.Sperre = true;
            if (new Vector2(pos.x - BgHort.x, pos.z - BgHort.y).magnitude < BgHortRadius) e.Sperre = true;   // Klettergrat, bewusst
            lauf.Events.Add(e);
            return e;
        }

        private string BgObstacle(BegehungsKoerper probe, float simT, out bool figur)
        {
            figur = false;
            if (probe.LastObstacle == null || simT - probe.LastTime > 1.5f) return "kein seitliches Hindernis berührt (Hang oder Kante?)";
            Collider c = probe.LastObstacle;
            if (c is TerrainCollider)
            {
                Terrain t = c.GetComponent<Terrain>();
                if (BgNearestTree(t, probe.LastPoint, 1.6f, out Vector3 tp)) return $"Baumstamm bei {tp.x:0}/{tp.z:0}";
                return $"Gelände (Neigung {Vector3.Angle(probe.LastNormal, Vector3.up):0}°)";
            }
            figur = c is CharacterController || c.GetComponentInParent<NpcIdentity>() != null || c.GetComponentInParent<NetworkEnemy>() != null ||
                    c.GetComponentInParent<NetworkPlayer>() != null;
            return (figur ? "Figur: " : "") + BgPath(c.transform);
        }

        private static string BgPath(Transform t)
        {
            string s = t.name;
            Transform p = t.parent;
            for (int i = 0; i < 2 && p != null; i++, p = p.parent) s = p.name + "/" + s;
            return s;
        }

        private static bool BgNearestTree(Terrain t, Vector3 point, float maxDist, out Vector3 trunk)
        {
            trunk = Vector3.zero;
            if (t == null || t.terrainData == null) return false;
            Vector3 size = t.terrainData.size, o = t.transform.position;
            float best = maxDist * maxDist;
            bool found = false;
            foreach (TreeInstance ti in t.terrainData.treeInstances)
            {
                Vector3 p = o + Vector3.Scale(ti.position, size);
                float d = (p.x - point.x) * (p.x - point.x) + (p.z - point.z) * (p.z - point.z);
                if (d < best)
                {
                    best = d;
                    trunk = p;
                    found = true;
                }
            }
            return found;
        }

        private void BgReport(BgLauf lauf, BgRoute r)
        {
            string plan = r != null && r.PlanProzent > 0f ? $" (geplant {r.PlanProzent:0} %)" : "";
            string kopf = $"{lauf.Name}: {lauf.Fortschritt:0} von {lauf.Laenge:0} m in {BgMinSek(lauf.Zeit)} min " +
                          $"({(lauf.Zeit > 0f ? lauf.Fortschritt / lauf.Zeit : 0f):0.0} m/s), steilste Stelle {lauf.Steilste * 100f:0} %{plan}";
            if (lauf.Spruenge > 0) kopf += $", {lauf.Spruenge}× übersprungen";
            Check(lauf.Fehlerfrei, kopf);
            int n = 0;
            foreach (BgEvent e in lauf.Events)
            {
                string art = e.Ziel ? " [am Ziel]" : e.Sperre ? " [Sperrbereich]" : e.Figur ? " [Figur]" : "";
                if (n++ < 10) Info($"  {e.Art}{art} bei {e.Pos.x:0}/{e.Pos.y:0}/{e.Pos.z:0}: {e.Detail}");
                _bgCsv.Add(string.Join(";", lauf.Name.Replace(';', ','), e.Art, e.Pos.x.ToString("0.0"), e.Pos.y.ToString("0.0"), e.Pos.z.ToString("0.0"),
                    e.S.ToString("0"), e.Sperre ? "ja" : "", e.Figur ? "ja" : "", (e.Detail ?? "").Replace(';', ',')));
            }
            if (n > 10) Info($"  … {n - 10} weitere (CSV)");
            if (lauf.Steilste > 0f && lauf.Events.Count == 0 && r != null && r.PlanProzent > 0f && lauf.Steilste * 100f > r.PlanProzent * 1.6f)
                Info($"  steilste Stelle bei {lauf.SteilPos.x:0}/{lauf.SteilPos.z:0} – begehbar, aber über dem Plan");
        }

        private static string BgMinSek(float sec)
        {
            int s = Mathf.RoundToInt(sec);
            return $"{s / 60}:{s % 60:00}";
        }

        // ================================================================== Baumstämme

        private IEnumerator BgBaeume(CharacterMotor motor, BegehungsKoerper probe, Vector3 near)
        {
            Terrain t = Terrain.activeTerrain;
            if (t == null || t.terrainData == null)
            {
                Check(false, "Terrain vorhanden");
                yield break;
            }
            TreePrototype[] protos = t.terrainData.treePrototypes;
            int withCol = protos.Count(p => p.prefab != null && p.prefab.GetComponent<Collider>() != null);
            Check(protos.Length > 0 && withCol == protos.Length && t.GetComponent<TerrainCollider>() != null,
                $"Baumvorlagen mit Stamm-Kollider: {withCol} von {protos.Length} (dazu unten die Probe am Stamm)");

            Vector3 size = t.terrainData.size, o = t.transform.position;
            List<Vector3> cands = t.terrainData.treeInstances
                .Select(ti => o + Vector3.Scale(ti.position, size))
                .OrderBy(p => (p.x - near.x) * (p.x - near.x) + (p.z - near.z) * (p.z - near.z))
                .Take(20).ToList();
            int tested = 0, blocked = 0;
            foreach (Vector3 trunk in cands)
            {
                if (tested >= 3) break;
                var away = new Vector2(near.x - trunk.x, near.z - trunk.z);
                if (away.sqrMagnitude < 0.01f) away = Vector2.up;
                away.Normalize();
                if (!BgGround(trunk.x + away.x * 3.5f, trunk.z + away.y * 3.5f, trunk.y, out Vector3 sp)) continue;
                if (Mathf.Abs(sp.y - trunk.y) > 1.5f) continue;   // am Hang: nächster Baum
                float yaw = Mathf.Atan2(-away.x, -away.y) * Mathf.Rad2Deg;
                motor.Teleport(sp, yaw);
                probe.ResetContacts();
                float minD = float.MaxValue;
                bool stamm = false;
                for (int i = 0; i < 100; i++)
                {
                    probe.SimTime = i * BgDt;
                    motor.Simulate(new MoveCommand { Forward = 1f, SteerWithCamera = true, DesiredYaw = yaw }, BgDt);
                    Vector3 p = motor.transform.position;
                    minD = Mathf.Min(minD, new Vector2(p.x - trunk.x, p.z - trunk.z).magnitude);
                    if (probe.LastObstacle is TerrainCollider && Mathf.Abs(probe.LastNormal.y) < 0.5f &&
                        new Vector2(probe.LastPoint.x - trunk.x, probe.LastPoint.z - trunk.z).magnitude < 1.4f) stamm = true;
                }
                tested++;
                if (stamm && minD > 0.3f) blocked++;
                Info($"Baum bei {trunk.x:0}/{trunk.z:0}: kleinster Abstand Figur–Stamm {minD:0.00} m{(stamm ? ", Stamm berührt" : ", kein Stamm berührt (läuft hindurch)")}");
                yield return null;
            }
            Check(tested > 0 && blocked == tested, $"Baumstämme halten die Figur auf ({blocked} von {tested} angelaufenen Bäumen)");
        }

        // ================================================================== Sperren

        private IEnumerator BgSperre(CharacterMotor motor, BegehungsKoerper probe, BgRoute r)
        {
            float L = r.Line.Length;
            // vom inneren Rand des Sperrbereichs nach außen: Am Nordpass schließen die Granitstufen schon vor der Schwelle
            float sIn = Mathf.Clamp(r.SchwelleS - r.OutDir * BgSperrZone, 0f, L), sOut = Mathf.Clamp(r.SchwelleS + r.OutDir * 25f, 0f, L);
            if (Mathf.Abs(sOut - sIn) < 10f)
            {
                Info($"{r.Name} / {r.Schwelle.typ}: zu kurz für die Probe");
                yield break;
            }
            var lauf = new BgLauf { Name = $"Sperre {r.Name}" };
            yield return BgWalk(motor, probe, r, sIn, sOut, lauf, false, Mathf.Abs(sOut - sIn) / 2f + 30f);
            float bisSchwelle = Mathf.Abs(r.SchwelleS - sIn);
            string hindernis = lauf.Events.Where(e => e.Art == "hängt").Select(e => e.Detail).FirstOrDefault();
            string wie = lauf.Erreicht ? (lauf.Geschwommen ? "auf dem Weg schwimmend passierbar" : "auf dem Weg zu Fuß passierbar")
                : lauf.Fortschritt < bisSchwelle ? $"hält – die Figur bleibt {bisSchwelle - lauf.Fortschritt:0} m vor der Schwelle" +
                                                     (hindernis != null ? $" ({hindernis.Replace(" – Abbruch", "")})" : "")
                : $"hält – die Figur kommt {lauf.Fortschritt - bisSchwelle:0} m über die Schwelle, nicht weiter";
            Info($"{r.Name} / {r.Schwelle.typ}: {wie}");
            foreach (BgEvent e in lauf.Events)
                _bgCsv.Add(string.Join(";", lauf.Name, e.Art, e.Pos.x.ToString("0.0"), e.Pos.y.ToString("0.0"), e.Pos.z.ToString("0.0"), e.S.ToString("0"), "ja",
                    e.Figur ? "ja" : "", (e.Detail ?? "").Replace(';', ',')));
        }

        // ================================================================== Ziele

        private sealed class BgZiel
        {
            public string Name, Regel;
            public Vector3 Pos;
            public Func<Vector3, bool> Erreicht;
        }

        private IEnumerator BgZiele(CharacterMotor motor, BegehungsKoerper probe, List<BgRoute> routes)
        {
            Scene scene = SceneManager.GetActiveScene();
            var ziele = new List<BgZiel>();
            float Flach(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
            foreach (WorldQuestSite s in WorldQuestSite.All)
                if (s != null && s.gameObject.scene == scene)
                {
                    Vector3 c = s.transform.position;
                    float rad = Mathf.Max(3f, s.Radius);
                    ziele.Add(new BgZiel { Name = "Auftragsort " + s.Id, Pos = c, Regel = $"im Ortsradius {rad:0} m", Erreicht = p => Flach(p, c) <= rad });
                }
            foreach (QuestArea q in FindObjectsByType<QuestArea>(FindObjectsSortMode.None))
            {
                QuestArea area = q;
                ziele.Add(new BgZiel { Name = "Erkundungsziel " + q.AreaId, Pos = q.transform.position, Regel = "im Erkundungsbereich", Erreicht = p => area.Contains(p) });
            }
            foreach (NpcIdentity n in NpcIdentity.All.ToList())
                if (n != null)
                {
                    Vector3 c = n.transform.position;
                    float range = GameData.Npc(n.NpcId) is NpcDefinition def ? def.interactRange : 5f;
                    ziele.Add(new BgZiel { Name = "Figur " + n.NpcId, Pos = c, Regel = $"in Gesprächsweite {range:0.#} m", Erreicht = p => Vector3.Distance(p, c) <= range });
                }
            foreach (EnemySpawnArea e in FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None))
                if (e != null && !e.SummonOnly)
                {
                    Vector3 c = e.transform.position;
                    float rad = e.Radius + 6f;
                    ziele.Add(new BgZiel { Name = "Jagdgebiet " + e.Group, Pos = c, Regel = $"im Jagdgebiet ({rad:0} m)", Erreicht = p => Flach(p, c) <= rad });
                }
            // gleiche Stelle und gleiche Art nur einmal
            var unique = new List<BgZiel>();
            foreach (BgZiel z in ziele)
                if (!unique.Any(u => (u.Pos - z.Pos).sqrMagnitude < 9f && u.Name.Split(' ')[0] == z.Name.Split(' ')[0])) unique.Add(z);

            int erreicht = 0, amWeg = 0, nurGerade = 0, bewusst = 0, sprung = 0;
            var fehler = new List<string>();
            var schwellen = routes.Where(r => r.Schwelle != null).Select(r => new Vector2(r.Schwelle.x, r.Schwelle.z)).ToList();
            foreach (BgZiel z in unique)
            {
                var z2 = new Vector2(z.Pos.x, z.Pos.z);
                // bewusst nicht zu Fuß erreichbar: Hort der Grauen Mutter (Klettergrat, Max 06.10.), geschlossene Ausgänge (WORLD-114)
                if ((z2 - BgHort).magnitude < BgHortRadius || schwellen.Any(sv => (sv - z2).magnitude < BgSperrZone * 0.5f))
                {
                    bewusst++;
                    continue;
                }
                BgRoute bestR = null;
                float bestS = 0f, bestLat = float.MaxValue;
                foreach (BgRoute r in routes)
                {
                    float s = r.Line.Project(z2, r.InnerA, r.InnerB, out float lat);
                    if (lat < bestLat)
                    {
                        bestLat = lat;
                        bestR = r;
                        bestS = s;
                    }
                }
                if (bestR == null) continue;
                Vector2 rp = bestR.Line.PointAt(bestS);
                BgGround(rp.x, rp.y, BgPlanY(bestR, bestS), out Vector3 start);
                if (z.Erreicht(start))
                {
                    amWeg++;
                    erreicht++;
                    continue;
                }
                var path = new NavMeshPath();
                bool nav = NavMesh.SamplePosition(start, out NavMeshHit hs, 5f, NavMesh.AllAreas) &&
                           NavMesh.SamplePosition(z.Pos, out NavMeshHit ht, 5f, NavMesh.AllAreas) &&
                           NavMesh.CalculatePath(hs.position, ht.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                // Sprungziel (Erkundungsziel oben auf einem Fels mit Stufen – Q3 übt das Springen, Max 07.10.): kein NavMesh-Weg
                // hinauf; wie ein Spieler von der untersten Stufe aus hinaufspringen (BgSprung)
                Terrain tz = Terrain.activeTerrain;
                if (!nav && z.Name.StartsWith("Erkundungsziel") && tz != null && z.Pos.y > tz.SampleHeight(z.Pos) + tz.transform.position.y + 1.5f)
                {
                    var sl = new BgLauf { Name = z.Name };
                    string wie = null;
                    yield return BgSprung(motor, probe, z, sl, w => wie = w);
                    if (sl.Erreicht)
                    {
                        erreicht++;
                        sprung++;
                        continue;
                    }
                    fehler.Add($"{z.Name} bei {z.Pos.x:0}/{z.Pos.y:0}/{z.Pos.z:0} ({z.Regel}) – Sprungweg über die Stufen: {wie}");
                    continue;
                }
                var pts = new List<Vector2> { new Vector2(start.x, start.z) };
                var ys = new List<float> { start.y };
                if (nav)
                    foreach (Vector3 c in path.corners)
                    {
                        var c2 = new Vector2(c.x, c.z);
                        if ((c2 - pts[pts.Count - 1]).sqrMagnitude > 0.04f)
                        {
                            pts.Add(c2);
                            ys.Add(c.y);
                        }
                    }
                if ((z2 - pts[pts.Count - 1]).sqrMagnitude > 0.04f)
                {
                    pts.Add(z2);
                    ys.Add(z.Pos.y);
                }
                if (pts.Count < 2)
                {
                    erreicht++;
                    continue;
                }
                var route = new BgRoute { Name = z.Name, Typ = "ziel", Breite = 2f, Line = new Wegstrecke(pts), PlanY = ys.ToArray(), InnerA = 0f };
                route.InnerB = route.Line.Length;
                var lauf = new BgLauf { Name = z.Name };
                // zu Fuß hin; das Ziel zählt, sobald die Figur unterwegs nah genug ist (Gesprächsweite, Radius)
                yield return BgWalk(motor, probe, route, 0f, route.Line.Length, lauf, false, route.Line.Length / 2f + 25f, z.Erreicht);
                Vector3 endPos = motor.transform.position;
                if (z.Erreicht(endPos))
                {
                    erreicht++;
                    if (!nav) nurGerade++;
                    continue;
                }
                string grund = lauf.Events.Count > 0 ? $"{lauf.Events[0].Art}: {lauf.Events[0].Detail}" : "kommt nicht an";
                fehler.Add($"{z.Name} bei {z.Pos.x:0}/{z.Pos.y:0}/{z.Pos.z:0} ({z.Regel}) – {(nav ? "NavMesh-Weg" : "kein NavMesh-Weg, gerade Linie")} " +
                           $"{route.Line.Length:0} m, Figur bleibt {Vector3.Distance(endPos, z.Pos):0.0} m entfernt ({grund})");
                foreach (BgEvent e in lauf.Events)
                    _bgCsv.Add(string.Join(";", ("Ziel " + z.Name).Replace(';', ','), e.Art, e.Pos.x.ToString("0.0"), e.Pos.y.ToString("0.0"), e.Pos.z.ToString("0.0"),
                        e.S.ToString("0"), "", e.Figur ? "ja" : "", (e.Detail ?? "").Replace(';', ',')));
            }
            int geprueft = unique.Count - bewusst;
            Check(fehler.Count == 0, $"Ziele zu Fuß erreicht: {erreicht} von {geprueft} ({amWeg} schon vom Weg aus, {nurGerade} nur in gerader Linie ohne NavMesh-Weg, " +
                                     $"{sprung} mit Sprüngen über Stufen; {bewusst} bewusst abseits: Hort der Grauen Mutter, geschlossene Ausgänge)");
            foreach (string f in fehler.Take(25)) Info("  nicht erreicht: " + f);
            if (fehler.Count > 25) Info($"  … {fehler.Count - 25} weitere");
        }
            /// <summary>
        /// Sprungziel: Stufen = Objekte „Steinplatte“, „Stufe …“ und „Fels“ mit Kollider bis 8 m um das Ziel, nach Oberkante sortiert.
        /// Start 2,5 m vor der untersten Stufe (auf der Seite weg von der nächsten); dann je Stufe auf deren Mitte zulaufen und springen,
        /// sobald sie höher liegt und nah genug ist (wie ein Spieler, Sprungtaste als Flanke). Erreicht = im Erkundungsbereich.
        /// </summary>
        private IEnumerator BgSprung(CharacterMotor motor, BegehungsKoerper probe, BgZiel z, BgLauf lauf, Action<string> wie)
        {
            var stufen = new List<Collider>();
            foreach (Collider c in FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (c == null || c.isTrigger) continue;
                string n = c.gameObject.name;
                if ((n == "Steinplatte" || n.StartsWith("Stufe ") || n == "Fels") && (c.bounds.center - z.Pos).sqrMagnitude < 64f) stufen.Add(c);
            }
            stufen.Sort((p, q) => p.bounds.max.y.CompareTo(q.bounds.max.y));
            if (stufen.Count == 0)
            {
                wie("keine Stufen gefunden");
                yield break;
            }
            Bounds b0 = stufen[0].bounds;
            Vector3 naechste = stufen.Count > 1 ? stufen[1].bounds.center : z.Pos;
            Vector3 weg = b0.center - naechste;
            weg.y = 0f;
            if (weg.sqrMagnitude < 0.01f) weg = Vector3.back;
            Vector3 st = b0.center + weg.normalized * (Mathf.Max(b0.extents.x, b0.extents.z) + 2.5f);
            BgGround(st.x, st.z, b0.min.y, out Vector3 sp);
            motor.Teleport(sp, Mathf.Atan2(-weg.x, -weg.z) * Mathf.Rad2Deg);
            probe.ResetContacts();
            float simT = 0f;
            int steps = 0, spruenge = 0;
            string stand = "";
            foreach (Collider c in stufen)
            {
                Bounds b = c.bounds;
                var oben = new Vector3(b.center.x, b.max.y, b.center.z);
                float t0 = simT;
                bool drauf = false;
                while (simT - t0 < 6f)
                {
                    Vector3 pos = motor.transform.position;
                    var flach = new Vector2(pos.x - b.center.x, pos.z - b.center.z);
                    if (pos.y > b.max.y - 0.25f && flach.magnitude < Mathf.Min(b.extents.x, b.extents.z) + 0.2f)
                    {
                        drauf = true;
                        break;
                    }
                    var to = new Vector2(oben.x - pos.x, oben.z - pos.z);
                    bool hoeher = oben.y > pos.y + 0.35f;
                    var cmd = new MoveCommand
                    {
                        Forward = 1f, SteerWithCamera = true, DesiredYaw = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg,
                        Jump = hoeher && motor.State.Grounded && to.magnitude < Mathf.Max(b.extents.x, b.extents.z) + 1.6f && steps % 15 == 0
                    };
                    if (cmd.Jump) spruenge++;
                    probe.SimTime = simT;
                    motor.Simulate(cmd, BgDt);
                    simT += BgDt;
                    steps++;
                    if (steps % BgStepsPerFrame == 0) yield return null;
                }
                stand += (stand.Length > 0 ? ", " : "") + c.gameObject.name + (drauf ? " oben" : " nicht erreicht");
                if (!drauf) break;
            }
            // oben: zur Mitte des Ziels
            for (int i = 0; i < 100 && !z.Erreicht(motor.transform.position); i++)
            {
                Vector3 pos = motor.transform.position;
                var to = new Vector2(z.Pos.x - pos.x, z.Pos.z - pos.z);
                motor.Simulate(new MoveCommand { Forward = to.magnitude > 0.3f ? 0.5f : 0f, SteerWithCamera = true, DesiredYaw = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg }, BgDt);
            }
            lauf.Erreicht = z.Erreicht(motor.transform.position);
            lauf.Zeit = simT;
            wie($"{stand}; {spruenge} Sprünge, Figur bleibt {Vector3.Distance(motor.transform.position, z.Pos):0.0} m entfernt");
        }
    }
}
