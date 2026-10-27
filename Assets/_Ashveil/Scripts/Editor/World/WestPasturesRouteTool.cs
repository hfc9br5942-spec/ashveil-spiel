using System.Collections.Generic;
using System.IO;
using System.Text;
using Ashveil.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using L = Ashveil.EditorTools.WestPasturesLayout;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Misst die Wege der Pferde-Geschichte auf dem NavMesh (echte Laufstrecke, nicht Luftlinie – Vorgabe):
    ///   Suchweg  Hof → Hinweise 1–6 → Hohlweg → Felskessel   Ziel 1,8–2,2 km
    ///   Heimweg  Felskessel → Hof (kuratierte Herdenroute)    Ziel 0,7–1,0 km
    /// Zeichnet beide als Linie in die Zellen-Szene (TrailGizmo) und schreibt Logs/Ashveil/Pferdehof-Route.txt.
    /// </summary>
    public static class WestPasturesRouteTool
    {
        [MenuItem("Ashveil/Welt/Westweiden: Suchweg und Heimweg messen", priority = 516)]
        public static void MeasureMenu() => Debug.Log(Measure(true));

        public static string Measure(bool keepOpen)
        {
            Scene cell = SceneManager.GetSceneByPath(WestPasturesBuilder.ScenePath);
            bool opened = false;
            if (!cell.IsValid() || !cell.isLoaded)
            {
                if (!File.Exists(WestPasturesBuilder.ScenePath)) return "[Ashveil] Westweiden: Szene fehlt – erst bauen.";
                cell = EditorSceneManager.OpenScene(WestPasturesBuilder.ScenePath, OpenSceneMode.Additive);
                opened = true;
            }
            var sb = new StringBuilder("[Ashveil] Pferde-Geschichte – Wege auf dem NavMesh (" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ")\n");

            // Suchweg: Etappen zwischen Hof, Hinweisen, Hohlweg-Eingang und Senke
            var stops = new List<(string name, Vector3 p)> { ("Hof (Westzaun)", L.OnGround(L.FarmCenter.x - 30f, L.FarmCenter.y + 8f)) };
            for (int i = 0; i < L.Clues.Length; i++) stops.Add(("Hinweis " + (i + 1) + " " + L.Clues[i].key, L.OnGround(L.Clues[i].pos.x, L.Clues[i].pos.y)));
            Vector3 rv0 = L.Ravine.PointAt(0);
            stops.Add(("Hohlweg-Eingang", L.OnGround(rv0.x, rv0.z)));
            stops.Add(("Felskessel", L.OnGround(L.BasinCenter.x, L.BasinCenter.y)));
            float total = 0f, air = 0f;
            bool ok = true;
            var path = new List<Vector3>();
            for (int i = 1; i < stops.Count; i++)
            {
                float d = Leg(stops[i - 1].p, stops[i].p, path, out bool complete);
                float a = Vector3.Distance(Flat(stops[i - 1].p), Flat(stops[i].p));
                total += d;
                air += a;
                ok &= complete;
                sb.AppendLine($"  {stops[i - 1].name} → {stops[i].name}: {d:0} m (Luftlinie {a:0} m){(complete ? "" : "  ! KEIN VOLLSTAENDIGER WEG")}");
            }
            bool inRange = total >= 1800f && total <= 2200f;
            sb.AppendLine($"  SUCHWEG gesamt: {total:0} m (Luftlinie {air:0} m) – Ziel 1800–2200 m: {(inRange && ok ? "OK" : "PRUEFEN")}");

            // Heimweg der Herde (kuratierte Route) und die direkte Laufstrecke zurueck
            float route = (float)L.HerdReturn.Length;
            var back = new List<Vector3>();
            float walkBack = Leg(L.OnGround(L.BasinCenter.x, L.BasinCenter.y), L.OnGround(-836f, 248f), back, out bool backOk);
            bool routeOk = route >= 700f && route <= 1000f;
            sb.AppendLine($"  HEIMWEG Herde (Route): {route:0} m – Ziel 700–1000 m: {(routeOk ? "OK" : "PRUEFEN")}; zu Fuss auf dem NavMesh: {walkBack:0} m{(backOk ? "" : " ! unvollstaendig")}");

            // Furten: Bach nur dort querbar? (Stichprobe: direkte Querung neben der Furt muss laenger sein)
            foreach (Vector2 f in L.Fords)
            {
                float across = Leg(L.OnGround(f.x - 8f, f.y + 30f), L.OnGround(f.x + 8f, f.y + 30f), new List<Vector3>(), out bool c1);
                sb.AppendLine($"  Bach 30 m neben der Furt ({f.x:0},{f.y:0}) queren: {across:0} m {(c1 ? "" : "(kein Weg)")} – lang = Ufer halten");
            }

            // Diagnose: Hoehen und NavMesh-Abdeckung
            var tri = NavMesh.CalculateTriangulation();
            sb.AppendLine($"  NavMesh gesamt (alle geladenen Szenen): {tri.vertices.Length} Ecken, {tri.indices.Length / 3} Dreiecke");
            foreach (Vector2 q in new[] { L.FarmCenter, L.Clues[2].pos, L.Clues[3].pos, L.BasinCenter, new Vector2(-650f, L.PassZ), new Vector2(-1200f, 300f) })
            {
                float gh = L.Height(q.x, q.y);
                bool hit = NavMesh.SamplePosition(new Vector3(q.x, gh, q.y), out NavMeshHit nh, 5f, NavMesh.AllAreas);
                bool ray = Physics.Raycast(new Vector3(q.x, gh + 50f, q.y), Vector3.down, out RaycastHit rh, 200f);
                sb.AppendLine($"  ({q.x:0},{q.y:0}) Hoehe {gh:0.0} m · Boden-Strahl {(ray ? rh.point.y.ToString("0.0") + " " + rh.collider.name : "kein Treffer")} · NavMesh {(hit ? nh.position.y.ToString("0.0") : "nein")}");
            }

            // Anzeige in der Szene
            GameObject holder = null;
            foreach (GameObject g in cell.GetRootGameObjects())
                if (g.name.StartsWith("Zelle ")) holder = g;
            if (holder != null)
            {
                Transform old = holder.transform.Find("Wege (Messung)");
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var go = new GameObject("Wege (Messung)");
                go.transform.SetParent(holder.transform, false);
                var st = new Vector3[stops.Count];
                for (int i = 0; i < stops.Count; i++) st[i] = stops[i].p;
                go.AddComponent<TrailGizmo>().Configure(path.ToArray(), st, new Color(1f, 0.55f, 0.15f, 0.95f), total);
                var go2 = new GameObject("Heimweg (Messung)");
                go2.transform.SetParent(go.transform, false);
                go2.AddComponent<TrailGizmo>().Configure(back.ToArray(), new Vector3[0], new Color(0.3f, 0.8f, 1f, 0.9f), walkBack);
                EditorSceneManager.MarkSceneDirty(cell);
                EditorSceneManager.SaveScene(cell);
            }
            string report = sb.ToString();
            File.WriteAllText(Ashveil.Core.ProjectPaths.Logs("Pferdehof-Route.txt"), report, new UTF8Encoding(false));
            if (opened && !keepOpen) EditorSceneManager.CloseScene(cell, true);
            return report;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static float Leg(Vector3 from, Vector3 to, List<Vector3> collect, out bool complete)
        {
            complete = false;
            if (!NavMesh.SamplePosition(from, out NavMeshHit a, 8f, NavMesh.AllAreas) || !NavMesh.SamplePosition(to, out NavMeshHit b, 8f, NavMesh.AllAreas))
                return Vector3.Distance(from, to);
            var p = new NavMeshPath();
            if (!NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, p) || p.corners.Length < 2) return Vector3.Distance(from, to);
            complete = p.status == NavMeshPathStatus.PathComplete;
            float d = 0f;
            for (int i = 1; i < p.corners.Length; i++) d += Vector3.Distance(p.corners[i - 1], p.corners[i]);
            collect.AddRange(p.corners);
            return d;
        }
    }
}
