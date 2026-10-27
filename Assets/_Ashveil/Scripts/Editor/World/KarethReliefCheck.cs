using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ashveil.Gameplay.Enemies;
using Ashveil.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Pruefung des P2.5-Kareth-Reliefs (<see cref="KarethRelief"/>). Menue "Ashveil/Welt/Kareth-Relief pruefen".
    /// Bericht: Logs/Ashveil/Kareth_Relief.txt. Teil 1 rechnet nur mit der Hoehenfunktion, Teil 2 prueft die gebaute
    /// Szene (Terrain, Orte, Gegnergebiete, Route, NavMesh, schwebende/vergrabene Objekte).
    /// </summary>
    public static class KarethReliefCheck
    {
        private static readonly (string name, float x, float z)[] TestPoints =
        {
            ("Wachtfeuer", 0f, 0f), ("Plateau (Kareth-Test)", 0f, 690f), ("Moor (Kareth-Test)", 40f, 330f),
            ("Uhrwerk-Wand", 585f, 100f), ("Meer", -470f, -300f), ("Halle", 0f, -64f), ("Kolosseum-Vorplatz", 490f, 285f),
            ("Tunnel-Westportal", 500f, 60f), ("Salzgruben-Eingang", -290f, -195f), ("Weidepass", -600f, 250f)
        };

        [MenuItem("Ashveil/Welt/Kareth-Relief pruefen", priority = 519)]
        public static void RunMenu()
        {
            string report = Run(out int errors);
            string path = Ashveil.Core.ProjectPaths.Logs("Kareth_Relief.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, report, new UTF8Encoding(false));
            Debug.Log($"[Ashveil] Kareth-Relief geprueft: {errors} Fehler -> {path}");
        }

        public static string Run(out int errors)
        {
            KarethRelief.Refresh();
            var sb = new StringBuilder();
            int err = 0;
            void Ok(bool ok, string msg)
            {
                sb.AppendLine((ok ? "  OK    " : "  FEHLER ") + msg);
                if (!ok) err++;
            }
            void Info(string msg) => sb.AppendLine("  ·     " + msg);

            sb.AppendLine($"ASHVEIL Kareth-Relief (P2.5) · {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} · Master v0.2");
            sb.AppendLine($"Relief: {(KarethRelief.Enabled ? "AN" : "AUS")} (KarethLayout.reliefEnabled) · {KarethRelief.Forms.Length} Formen");
            sb.AppendLine();
            sb.AppendLine("[Hoehenfunktion]");

            // Schutzpunkte: dort bleibt Kareth exakt
            foreach ((string label, float px, float pz) in TestPoints)
            {
                float o = KarethRelief.Exact(px, pz);
                Ok(Mathf.Abs(o) < 0.01f, $"{label} unveraendert (Relief {o:0.00} m)");
            }

            // Raender = Naehte zu Grenzgebirge, Westweiden und Umland
            float edgeMax = 0f;
            for (float t = 0f; t <= 1f; t += 1f / 600f)
            {
                float x = Mathf.Lerp(KarethTerrainBuilder.MinX, KarethTerrainBuilder.MaxX, t);
                float z = Mathf.Lerp(KarethTerrainBuilder.MinZ, KarethTerrainBuilder.MaxZ, t);
                edgeMax = Mathf.Max(edgeMax, Mathf.Abs(KarethRelief.Offset(x, KarethTerrainBuilder.MinZ)), Mathf.Abs(KarethRelief.Offset(x, KarethTerrainBuilder.MaxZ)),
                    Mathf.Abs(KarethRelief.Offset(KarethTerrainBuilder.MinX, z)), Mathf.Abs(KarethRelief.Offset(KarethTerrainBuilder.MaxX, z)));
            }
            Ok(edgeMax < 0.001f, $"Kartenraender unveraendert – Naehte zu Nachbarn bleiben (max {edgeMax * 100f:0.0} cm)");

            // Flaeche, Hoehen, Neigung
            int n = 0, withRelief = 0, steep = 0, steepBefore = 0;
            float min = 0f, max = 0f;
            var slopes = new List<float>();
            for (float z = KarethTerrainBuilder.MinZ + 2f; z < KarethTerrainBuilder.MaxZ - 2f; z += 4f)
                for (float x = KarethTerrainBuilder.MinX + 2f; x < KarethTerrainBuilder.MaxX - 2f; x += 4f)
                {
                    n++;
                    float o = KarethRelief.Offset(x, z);
                    if (Mathf.Abs(o) < 0.3f) continue;
                    withRelief++;
                    min = Mathf.Min(min, o);
                    max = Mathf.Max(max, o);
                    float s = Slope(KarethTerrainBuilder.Height, x, z), sb0 = Slope(KarethTerrainBuilder.BaseHeight, x, z);
                    slopes.Add(s);
                    if (s > 35f) steep++;
                    if (sb0 > 35f) steepBefore++;
                }
            slopes.Sort();
            Info($"Relief auf {100f * withRelief / Mathf.Max(1, n):0} % der Flaeche, {min:0.0} … +{max:0.0} m");
            if (slopes.Count > 0)
                Info($"Neigung dort: Median {slopes[slopes.Count / 2]:0.0}°, 95 % bis {slopes[(int)(slopes.Count * 0.95f)]:0.0}°, ueber 35°: {steep} Punkte (vorher {steepBefore}, v. a. Plateaukante)");

            // Wege: Laengsgefaelle nur durch das Relief und gesamt
            sb.AppendLine();
            sb.AppendLine("[Wege]");
            int wi = 0;
            foreach (Vector2[] line in KarethRelief.Paths)
            {
                wi++;
                float gRel = 0f, gAll = 0f, gBase = 0f;
                Vector2 at = line[0];
                foreach ((Vector2 a, Vector2 b) in Segments(line, 4f))
                {
                    float len = Vector2.Distance(a, b);
                    float r = Mathf.Abs(KarethRelief.Offset(b.x, b.y) - KarethRelief.Offset(a.x, a.y)) / len;
                    float all = Mathf.Abs(KarethTerrainBuilder.Height(b.x, b.y) - KarethTerrainBuilder.Height(a.x, a.y)) / len;
                    float bs = Mathf.Abs(KarethTerrainBuilder.BaseHeight(b.x, b.y) - KarethTerrainBuilder.BaseHeight(a.x, a.y)) / len;
                    gRel = Mathf.Max(gRel, r);
                    gBase = Mathf.Max(gBase, bs);
                    if (all > gAll)
                    {
                        gAll = all;
                        at = a;
                    }
                }
                // Grenze: das Relief selbst traegt hoechstens 10 % bei und macht keinen Weg steiler als 25 % (≈ 14°)
                bool ok = gRel <= 0.10f && (gAll <= Mathf.Max(0.25f, gBase + 0.02f));
                Ok(ok, $"Weg {wi} ({line[0].x:0}/{line[0].y:0} → {line[line.Length - 1].x:0}/{line[line.Length - 1].y:0}): Relief max {gRel * 100f:0.0} %, gesamt {gAll * 100f:0.0} % (vorher {gBase * 100f:0.0} %) bei {at.x:0}/{at.y:0}");
            }

            // Orte: ebene Flaeche
            sb.AppendLine();
            sb.AppendLine("[Quest-Orte]");
            int flat = 0, sites = 0;
            foreach (KarethQuestSites.Site s in KarethQuestSites.All)
            {
                if (s.Id == "site.kareth.krater") continue;
                sites++;
                float r = Mathf.Max(s.Radius, 20f) * 0.8f, lo = float.MaxValue, hi = float.MinValue;
                for (int k = 0; k < 16; k++)
                {
                    float a = k * Mathf.PI / 8f;
                    float o = KarethRelief.Offset(s.X + Mathf.Cos(a) * r, s.Z + Mathf.Sin(a) * r);
                    lo = Mathf.Min(lo, o);
                    hi = Mathf.Max(hi, o);
                }
                if (hi - lo <= 0.6f) flat++;
                else Info($"{s.Id}: Relief-Unterschied {hi - lo:0.0} m auf {r:0} m Radius");
            }
            Ok(flat == sites, $"Quest-Orte stehen eben (Relief ≤ 0,6 m im Ortsradius): {flat}/{sites}");

            // Teil 2: Szene
            sb.AppendLine();
            sb.AppendLine("[Szene]");
            Terrain terrain = Terrain.activeTerrains.FirstOrDefault(t => t != null && t.name == "Terrain Kareth");
            if (terrain == null) Info("Keine Sandbox-Szene mit 'Terrain Kareth' geoeffnet – Szenenpruefung uebersprungen.");
            else SceneChecks(terrain, Ok, Info);

            errors = err;
            sb.AppendLine();
            sb.AppendLine(err == 0 ? "ERGEBNIS: BESTANDEN" : $"ERGEBNIS: {err} FEHLER");
            return sb.ToString();
        }

        private static void SceneChecks(Terrain terrain, System.Action<bool, string> ok, System.Action<string> info)
        {
            float Ground(float x, float z) => terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;

            // Gebautes Terrain = Funktion (Relief wirklich eingebaut)
            float dev = 0f;
            Vector2 devAt = Vector2.zero;
            for (float z = KarethTerrainBuilder.MinZ + 10f; z < KarethTerrainBuilder.MaxZ - 10f; z += 20f)
                for (float x = KarethTerrainBuilder.MinX + 10f; x < KarethTerrainBuilder.MaxX - 10f; x += 20f)
                {
                    if (Mathf.Abs(KarethRelief.Offset(x, z)) < 0.3f) continue;
                    float d = Mathf.Abs(Ground(x, z) - KarethTerrainBuilder.Height(x, z));
                    if (d > dev)
                    {
                        dev = d;
                        devAt = new Vector2(x, z);
                    }
                }
            ok(dev < 0.5f, $"Gebautes Terrain enthaelt das Relief (Abweichung max {dev:0.00} m bei {devAt.x:0}/{devAt.y:0})");

            // Orte, POIs, Gegnergebiete: auf dem Boden, nicht darunter, nicht schwebend
            int placed = 0, off = 0;
            var offList = new List<string>();
            void CheckPivot(string what, Vector3 p, bool onGiant)
            {
                if (onGiant || !InKareth(p)) return;
                placed++;
                float g = Ground(p.x, p.z), d = p.y - g;
                if (Mathf.Abs(d) > 1.2f)
                {
                    off++;
                    if (offList.Count < 20) offList.Add($"{what} bei {p.x:0}/{p.z:0}: {d:+0.0;-0.0} m zum Boden");
                }
            }
            foreach (WorldQuestSite q in Object.FindObjectsByType<WorldQuestSite>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                bool giant = KarethQuestSites.All.Any(s => s.Id == q.Id && s.OnGiant);
                CheckPivot(q.Id, q.transform.position, giant);
            }
            foreach (EnemySpawnArea a in Object.FindObjectsByType<EnemySpawnArea>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                CheckPivot("Gegnergebiet " + a.name, a.transform.position, GiantBuilder.Blocks(a.transform.position.x, a.transform.position.z, 0f, false));
            foreach (string s in offList) info(s);
            ok(off == 0, $"Orte und Gegnergebiete auf dem Boden (keiner unter dem Gelaende oder schwebend): {placed - off}/{placed}");

            // Route des Glockenhirten = Relief-Konstante
            EnemyRoute route = Object.FindObjectsByType<EnemyRoute>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(r => r.name.Contains("Glockenhirt"));
            if (route != null)
            {
                float worst = 0f;
                for (int i = 0; i < route.Count && i < KarethRelief.ShepherdRoute.Length; i++)
                    worst = Mathf.Max(worst, Vector2.Distance(new Vector2(route.Point(i).x, route.Point(i).z), KarethRelief.ShepherdRoute[i]));
                ok(worst < 1f, $"Route des Glockenhirten stimmt mit der Relief-Konstante ueberein (max {worst:0.0} m)");
                float routeOff = 0f;
                for (int i = 0; i < route.Count; i++) routeOff = Mathf.Max(routeOff, Mathf.Abs(route.Point(i).y - Ground(route.Point(i).x, route.Point(i).z)));
                ok(routeOff < 1.2f, $"Routenpunkte des Glockenhirten auf dem Boden (max {routeOff:0.0} m)");
            }
            else info("Route des Glockenhirten nicht gefunden");

            // NavMesh entlang aller Wege (alle 10 m)
            int samples = 0, onMesh = 0;
            var holes = new List<string>();
            foreach (Vector2[] line in KarethRelief.Paths)
                foreach ((Vector2 a, Vector2 b2) in Segments(line, 10f))
                {
                    if (!InKareth(new Vector3(a.x, 0f, a.y))) continue;
                    samples++;
                    if (NavMesh.SamplePosition(new Vector3(a.x, Ground(a.x, a.y), a.y), out _, 3f, NavMesh.AllAreas)) onMesh++;
                    else if (holes.Count < 12) holes.Add($"{a.x:0}/{a.y:0}");
                }
            if (holes.Count > 0) info("ohne NavMesh: " + string.Join(", ", holes));
            ok(onMesh >= samples - 2, $"NavMesh auf allen Wegen ({onMesh}/{samples} Messpunkte)");

            // NavMesh auf den Reliefformen (Kaemme, Kuppen, Krater) begehbar
            int formPts = 0, formOk = 0;
            foreach (KarethRelief.Form f in KarethRelief.Forms)
            {
                Vector2 c = f.Kind == KarethRelief.Kind.Ridge || f.Kind == KarethRelief.Kind.Spur || f.Kind == KarethRelief.Kind.Gully ? (f.A + f.B) * 0.5f : f.A;
                if (Mathf.Abs(KarethRelief.Offset(c.x, c.y)) < 0.3f) continue;
                formPts++;
                if (NavMesh.SamplePosition(new Vector3(c.x, Ground(c.x, c.y), c.y), out _, 3f, NavMesh.AllAreas)) formOk++;
                else info($"Form '{f.Name}' ohne NavMesh in der Mitte ({c.x:0}/{c.y:0})");
            }
            info($"Reliefformen mit NavMesh in der Mitte: {formOk}/{formPts} (Rinnen/Felsnasen an der Plateaukante duerfen steil sein)");

            // Objekte, die dem Relief NICHT folgen (feste Hoehe statt Bodenhoehe): sie passen besser zum alten Boden
            // als zum neuen -> schweben ueber einer Senke oder stecken in einem Huegel. Absichtlich erhoehte Teile
            // (Tischplatten, Daecher, Lampen) fallen nicht auf, weil ihr Abstand zum alten Boden genauso gross ist.
            int checkedObj = 0, floating = 0, buried = 0;
            var odd = new List<string>();
            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (r.GetComponentInParent<Terrain>() != null || r.GetComponentInParent<WorldLabel>() != null) continue;
                Bounds b = r.bounds;
                Vector3 c = b.center;
                if (!InKareth(c)) continue;
                float rel = KarethRelief.Offset(c.x, c.z);
                if (Mathf.Abs(rel) < 1f) continue;
                if (b.size.x > 40f || b.size.z > 40f) continue;
                checkedObj++;
                float gNow = Ground(c.x, c.z), gOld = gNow - rel;
                float dNow = b.min.y - gNow, dOld = b.min.y - gOld;
                if (Mathf.Abs(dOld) + 0.3f < Mathf.Abs(dNow) && Mathf.Abs(dOld) < 1.5f)
                {
                    if (dNow > 0f) floating++;
                    else buried++;
                    if (odd.Count < 25) odd.Add($"{(dNow > 0f ? "schwebt" : "steckt im Boden")}: {ObjPath(r.transform)} bei {c.x:0}/{c.z:0} ({dNow:+0.0;-0.0} m, Relief {rel:+0.0;-0.0} m)");
                }
            }
            foreach (string s in odd) info(s);
            ok(floating == 0 && buried == 0, $"Alle Objekte in Reliefzonen folgen dem Boden ({checkedObj} geprueft, schwebend {floating}, im Boden {buried})");
        }

        private static string ObjPath(Transform t)
        {
            string p = t.name;
            for (int i = 0; i < 3 && t.parent != null; i++)
            {
                t = t.parent;
                p = t.name + "/" + p;
            }
            return p;
        }

        private static bool InKareth(Vector3 p) =>
            p.x > KarethTerrainBuilder.MinX + 1f && p.x < KarethTerrainBuilder.MaxX - 1f && p.z > KarethTerrainBuilder.MinZ + 1f && p.z < KarethTerrainBuilder.MaxZ - 1f;

        private static float Slope(System.Func<float, float, float> h, float x, float z)
        {
            float gx = (h(x + 2f, z) - h(x - 2f, z)) / 4f, gz = (h(x, z + 2f) - h(x, z - 2f)) / 4f;
            return Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
        }

        private static IEnumerable<(Vector2 a, Vector2 b)> Segments(Vector2[] line, float step)
        {
            for (int i = 0; i + 1 < line.Length; i++)
            {
                Vector2 a = line[i], b = line[i + 1];
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / step));
                for (int k = 0; k < n; k++) yield return (Vector2.Lerp(a, b, k / (float)n), Vector2.Lerp(a, b, (k + 1) / (float)n));
            }
        }
    }
}
