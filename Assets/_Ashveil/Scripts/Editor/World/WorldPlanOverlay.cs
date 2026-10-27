using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Planungsansicht der Weltkarte v3.4 in der Szenenansicht (nur Editor, nichts in der Szene, nichts im Build –
    /// CORE-005: null Laufzeitkosten). Liest die Regionsdaten aus dem Ordner claude/weltkarte_v3/regionen:
    ///   ASHVEIL_Regionsdaten_v3.json (Orte, POIs, Unterbereiche, Strassen, Uebergaenge; Koordinaten *_km)
    ///   regionsgeometrie_v34.json      (Regionsumrisse umriss_km, Seite, Stufen; Rückfall auf _v33)
    /// Koordinaten: km ab Suedwest-Ecke der Planetenkarte, x nach Osten, y nach Norden → Unity (x, –, z) * MeterJeKm + Ursprung.
    /// Menue: Ashveil/Welt/Planungsansicht v3.4 …
    /// </summary>
    public sealed class WorldPlanOverlay : EditorWindow
    {
        private const string Pref = "Ashveil.WorldPlan.";
        private const string DataFolder = "claude/weltkarte_v3/regionen";

        // ------------------------------------------------------------------ Einstellungen (EditorPrefs)
        private static bool Enabled { get => EditorPrefs.GetBool(Pref + "on", false); set => EditorPrefs.SetBool(Pref + "on", value); }
        private static float MetersPerKm { get => EditorPrefs.GetFloat(Pref + "scale", 1000f); set => EditorPrefs.SetFloat(Pref + "scale", value); }
        private static Vector3 Origin
        {
            get => new Vector3(EditorPrefs.GetFloat(Pref + "ox", 0f), EditorPrefs.GetFloat(Pref + "oy", 0f), EditorPrefs.GetFloat(Pref + "oz", 0f));
            set { EditorPrefs.SetFloat(Pref + "ox", value.x); EditorPrefs.SetFloat(Pref + "oy", value.y); EditorPrefs.SetFloat(Pref + "oz", value.z); }
        }
        private static string RegionFilter { get => EditorPrefs.GetString(Pref + "region", ""); set => EditorPrefs.SetString(Pref + "region", value); }
        private static bool ShowOutlines { get => EditorPrefs.GetBool(Pref + "outl", true); set => EditorPrefs.SetBool(Pref + "outl", value); }
        private static bool ShowPlaces { get => EditorPrefs.GetBool(Pref + "places", true); set => EditorPrefs.SetBool(Pref + "places", value); }
        private static bool ShowPois { get => EditorPrefs.GetBool(Pref + "pois", false); set => EditorPrefs.SetBool(Pref + "pois", value); }
        private static bool ShowRoads { get => EditorPrefs.GetBool(Pref + "roads", true); set => EditorPrefs.SetBool(Pref + "roads", value); }
        private static bool ShowSubAreas { get => EditorPrefs.GetBool(Pref + "subs", false); set => EditorPrefs.SetBool(Pref + "subs", value); }
        private static bool ShowTransitions { get => EditorPrefs.GetBool(Pref + "trans", true); set => EditorPrefs.SetBool(Pref + "trans", value); }
        private static bool OnTerrain { get => EditorPrefs.GetBool(Pref + "terrain", true); set => EditorPrefs.SetBool(Pref + "terrain", value); }
        private static float LabelDistance { get => EditorPrefs.GetFloat(Pref + "labeldist", 1500f); set => EditorPrefs.SetFloat(Pref + "labeldist", value); }

        // ------------------------------------------------------------------ Daten
        private sealed class Point { public string Name, Type, Detail; public Vector2 Km; }
        private sealed class Circle { public string Name, Levels; public Vector2 Km; public float RadiusKm; }
        private sealed class Line { public string Name, Type; public List<Vector2> Km = new List<Vector2>(); }
        private sealed class Region
        {
            public string Key, Name, Side, Levels;
            public Vector2 CenterKm;
            public List<List<Vector2>> Outline = new List<List<Vector2>>();
            public List<Point> Places = new List<Point>(), Pois = new List<Point>(), Transitions = new List<Point>();
            public List<Circle> SubAreas = new List<Circle>();
            public List<Line> Roads = new List<Line>();
        }

        private static List<Region> _regions;
        private static string _loadError;
        private static DateTime _loadedAt;
        private Vector2 _scroll;
        private static GUIStyle _label, _regionLabel;

        [MenuItem("Ashveil/Welt/Planungsansicht v3.4 – Fenster", priority = 600)]
        private static void Open() => GetWindow<WorldPlanOverlay>("Weltplan v3.4");

        [MenuItem("Ashveil/Welt/Planungsansicht v3.4 – an oder aus", priority = 601)]
        private static void Toggle()
        {
            Enabled = !Enabled;
            Hook();
            SceneView.RepaintAll();
        }

        [MenuItem("Ashveil/Welt/Planungsansicht v3.4 – an oder aus", true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked("Ashveil/Welt/Planungsansicht v3.4 – an oder aus", Enabled);
            return true;
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            if (Enabled) SceneView.duringSceneGui += OnSceneGui;
        }

        // ------------------------------------------------------------------ Fenster
        private void OnGUI()
        {
            EnsureLoaded();
            EditorGUILayout.HelpBox("Planungsansicht der Weltkarte v3.4 (Arbeitsgrundlage, kein Kanon). Zeichnet nur in der Szenenansicht; " +
                                    "legt keine Objekte an und kommt nicht in den Build.", MessageType.Info);
            bool on = EditorGUILayout.Toggle("Anzeigen", Enabled);
            if (on != Enabled) { Enabled = on; Hook(); }

            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            float scale = Mathf.Max(1f, EditorGUILayout.FloatField(new GUIContent("Meter je km", "1000 = Massstab 1:1. Kleiner fuer verkleinerte Testszenen."), MetersPerKm));
            Vector3 origin = EditorGUILayout.Vector3Field(new GUIContent("Ursprung (Suedwest-Ecke)", "Unity-Position der Kartenecke km (0, 0)."), Origin);
            bool onTerrain = EditorGUILayout.Toggle(new GUIContent("Auf Terrain setzen", "Hoehe aus Terrain.activeTerrain, sonst Ursprung.y"), OnTerrain);
            float labelDist = EditorGUILayout.Slider(new GUIContent("Beschriftung bis (m)", "Kamera-Abstand, bis zu dem Orts- und POI-Namen gezeichnet werden"), LabelDistance, 100f, 20000f);
            EditorGUILayout.Space();
            bool outl = EditorGUILayout.Toggle("Regionsumrisse", ShowOutlines);
            bool places = EditorGUILayout.Toggle("Orte (Staedte, Lager, Hoefe)", ShowPlaces);
            bool roads = EditorGUILayout.Toggle("Strassen und Pfade", ShowRoads);
            bool trans = EditorGUILayout.Toggle("Uebergaenge", ShowTransitions);
            bool subs = EditorGUILayout.Toggle("Unterbereiche (Stufen)", ShowSubAreas);
            bool pois = EditorGUILayout.Toggle("Minimap-Punkte (POIs)", ShowPois);
            if (EditorGUI.EndChangeCheck())
            {
                // nur bei Aenderung speichern (EditorPrefs schreibt unter Windows in die Registry)
                MetersPerKm = scale; Origin = origin; OnTerrain = onTerrain; LabelDistance = labelDist;
                ShowOutlines = outl; ShowPlaces = places; ShowRoads = roads; ShowTransitions = trans; ShowSubAreas = subs; ShowPois = pois;
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space();
            if (_loadError != null)
            {
                EditorGUILayout.HelpBox(_loadError, MessageType.Warning);
            }
            else if (_regions != null)
            {
                var keys = new List<string> { "(alle Regionen)" };
                foreach (var r in _regions) keys.Add(r.Key);
                int idx = Mathf.Max(0, keys.IndexOf(string.IsNullOrEmpty(RegionFilter) ? "(alle Regionen)" : RegionFilter));
                int sel = EditorGUILayout.Popup("Nur Region", idx, keys.ToArray());
                string want = sel == 0 ? "" : keys[sel];
                if (want != RegionFilter) { RegionFilter = want; SceneView.RepaintAll(); }
                EditorGUILayout.LabelField($"{_regions.Count} Regionen geladen ({_loadedAt:HH:mm:ss})", EditorStyles.miniLabel);
            }
            if (GUILayout.Button("Daten neu laden")) { _regions = null; EnsureLoaded(); }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Zu Region springen", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_regions != null)
            {
                foreach (var r in _regions)
                {
                    if (GUILayout.Button($"{r.Name}  ·  {SideText(r.Side)} {r.Levels}", EditorStyles.miniButton)) Focus(r);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private static void Focus(Region r)
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return;
            bool any = false;
            var b = new Bounds();
            foreach (var ring in r.Outline)
            foreach (var p in ring)
            {
                var w = World(p);
                if (!any) { b = new Bounds(w, Vector3.zero); any = true; }
                else b.Encapsulate(w);
            }
            if (!any) b = new Bounds(World(r.CenterKm), Vector3.one * 500f);
            b.Expand(new Vector3(0f, 200f, 0f));
            sv.Frame(b, false);
            if (!Enabled) { Enabled = true; Hook(); }
        }

        // ------------------------------------------------------------------ Zeichnen
        private static void OnSceneGui(SceneView sv)
        {
            if (Event.current.type != EventType.Repaint) return;
            EnsureLoaded();
            if (_regions == null) return;
            if (_label == null)
            {
                _label = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = Color.white } };
                _regionLabel = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, normal = { textColor = Color.white } };
            }
            Vector3 cam = sv.camera != null ? sv.camera.transform.position : Vector3.zero;
            float labelDist = LabelDistance;
            string filter = RegionFilter;

            foreach (var r in _regions)
            {
                if (!string.IsNullOrEmpty(filter) && r.Key != filter) continue;
                Color sideCol = SideColor(r.Side);

                if (ShowOutlines)
                {
                    Handles.color = sideCol;
                    foreach (var ring in r.Outline)
                    {
                        var pts = new Vector3[ring.Count + 1];
                        for (int i = 0; i < ring.Count; i++) pts[i] = World(ring[i]) + Vector3.up * 2f;
                        pts[ring.Count] = pts[0];
                        Handles.DrawAAPolyLine(3f, pts);
                    }
                    Handles.Label(World(r.CenterKm) + Vector3.up * 30f, $"{r.Name}\n{SideText(r.Side)} {r.Levels}", _regionLabel);
                }

                if (ShowRoads)
                {
                    foreach (var road in r.Roads)
                    {
                        Handles.color = road.Type == "haupt" ? new Color(1f, 0.85f, 0.4f) : new Color(0.85f, 0.75f, 0.55f, 0.8f);
                        var pts = new Vector3[road.Km.Count];
                        for (int i = 0; i < road.Km.Count; i++) pts[i] = World(road.Km[i]) + Vector3.up * 1f;
                        if (pts.Length > 1) Handles.DrawAAPolyLine(road.Type == "haupt" ? 4f : 2f, pts);
                    }
                }

                if (ShowSubAreas)
                {
                    Handles.color = new Color(1f, 1f, 1f, 0.6f);
                    foreach (var c in r.SubAreas)
                    {
                        var w = World(c.Km);
                        Handles.DrawWireDisc(w + Vector3.up * 1.5f, Vector3.up, c.RadiusKm * MetersPerKm);
                        if (Vector3.Distance(cam, w) < labelDist * 2f) Handles.Label(w + Vector3.up * 10f, $"{c.Name} ({c.Levels})", _label);
                    }
                }

                if (ShowPlaces) DrawPoints(r.Places, cam, labelDist, true);
                if (ShowPois) DrawPoints(r.Pois, cam, labelDist * 0.5f, false);
                if (ShowTransitions)
                {
                    foreach (var t in r.Transitions)
                    {
                        var w = World(t.Km);
                        Handles.color = new Color(0.6f, 0.9f, 1f);
                        float s = HandleUtility.GetHandleSize(w) * 0.12f;
                        Handles.DrawWireCube(w + Vector3.up * 3f, Vector3.one * Mathf.Max(4f, s * 8f));
                        if (Vector3.Distance(cam, w) < labelDist) Handles.Label(w + Vector3.up * 12f, $"→ {t.Name} ({t.Type})", _label);
                    }
                }
            }
        }

        private static void DrawPoints(List<Point> list, Vector3 cam, float labelDist, bool big)
        {
            foreach (var p in list)
            {
                var w = World(p.Km);
                Handles.color = TypeColor(p.Type);
                float size = Mathf.Max(big ? 6f : 3f, HandleUtility.GetHandleSize(w) * (big ? 0.08f : 0.05f));
                Handles.DrawSolidDisc(w + Vector3.up * 2f, Vector3.up, size);
                if (Vector3.Distance(cam, w) < labelDist) Handles.Label(w + Vector3.up * 8f, p.Name, _label);
            }
        }

        private static Vector3 World(Vector2 km)
        {
            Vector3 o = Origin;
            var w = new Vector3(o.x + km.x * MetersPerKm, o.y, o.z + km.y * MetersPerKm);
            if (OnTerrain)
            {
                var t = Terrain.activeTerrain;
                if (t != null) w.y = t.SampleHeight(w) + t.transform.position.y;
            }
            return w;
        }

        private static string SideText(string s) => s == "A" ? "Seite A" : s == "B" ? "Seite B" : s == "N" ? "neutral" : "umkaempft";

        private static Color SideColor(string s) =>
            s == "A" ? new Color(0.95f, 0.45f, 0.35f) : s == "B" ? new Color(0.4f, 0.65f, 1f) : s == "N" ? new Color(0.75f, 0.95f, 0.6f) : new Color(0.85f, 0.6f, 0.95f);

        private static Color TypeColor(string t)
        {
            switch (t)
            {
                case "CAP": return new Color(1f, 0.85f, 0.2f);
                case "TOWN": case "PORT": return new Color(1f, 0.95f, 0.7f);
                case "FORT": case "OUT": return new Color(0.9f, 0.3f, 0.25f);
                case "NEUT": return new Color(0.6f, 1f, 0.6f);
                case "RUINE": return new Color(0.5f, 0.1f, 0.1f);
                case "VIL": case "HOF": case "CAMP": return new Color(0.95f, 0.8f, 0.55f);
                default: return new Color(0.85f, 0.85f, 0.85f);
            }
        }

        // ------------------------------------------------------------------ Laden
        private static void EnsureLoaded()
        {
            if (_regions != null || _loadError != null) return;
            try
            {
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", DataFolder));
                string dataPath = Path.Combine(root, "ASHVEIL_Regionsdaten_v3.json");
                // v3.4 bevorzugt, sonst v3.3 (ältere Datenstände)
                string geoPath = Path.Combine(root, "regionsgeometrie_v34.json");
                if (!File.Exists(geoPath)) geoPath = Path.Combine(root, "regionsgeometrie_v33.json");
                if (!File.Exists(dataPath)) { _loadError = "Nicht gefunden: " + dataPath; return; }
                var data = MiniJson.Parse(File.ReadAllText(dataPath, Encoding.UTF8)) as Dictionary<string, object>;
                Dictionary<string, object> geo = null;
                if (File.Exists(geoPath))
                {
                    var g = MiniJson.Parse(File.ReadAllText(geoPath, Encoding.UTF8)) as Dictionary<string, object>;
                    geo = g != null && g.TryGetValue("regionen", out var gr) ? gr as Dictionary<string, object> : null;
                }
                if (data == null) { _loadError = "Regionsdaten unlesbar: " + dataPath; return; }

                var list = new List<Region>();
                foreach (var kv in data)
                {
                    var d = kv.Value as Dictionary<string, object>;
                    if (d == null) continue;
                    var r = new Region { Key = kv.Key, Name = Str(d, "name"), Side = Str(d, "seite"), Levels = Str(d, "stufen") };
                    foreach (var o in Arr(d, "orte")) { var m = o as Dictionary<string, object>; var p = Pt(m, "pos_km"); if (p.HasValue) r.Places.Add(new Point { Name = Str(m, "name"), Type = Str(m, "typ"), Km = p.Value }); }
                    foreach (var o in Arr(d, "pois")) { var m = o as Dictionary<string, object>; var p = Pt(m, "pos_km"); if (p.HasValue) r.Pois.Add(new Point { Name = Str(m, "name"), Type = Str(m, "typ"), Km = p.Value }); }
                    foreach (var o in Arr(d, "uebergaenge")) { var m = o as Dictionary<string, object>; var p = Pt(m, "pos_km"); if (p.HasValue) r.Transitions.Add(new Point { Name = Str(m, "nach"), Type = Str(m, "typ"), Km = p.Value }); }
                    foreach (var o in Arr(d, "unterbereiche"))
                    {
                        var m = o as Dictionary<string, object>; var p = Pt(m, "zentrum_km");
                        if (p.HasValue) r.SubAreas.Add(new Circle { Name = Str(m, "name"), Levels = Str(m, "stufen"), Km = p.Value, RadiusKm = Num(m, "radius_km") });
                    }
                    foreach (var o in Arr(d, "strassen"))
                    {
                        var m = o as Dictionary<string, object>; var line = new Line { Name = Str(m, "name"), Type = Str(m, "typ") };
                        foreach (var q in Arr(m, "punkte_km")) { var v = Vec(q); if (v.HasValue) line.Km.Add(v.Value); }
                        r.Roads.Add(line);
                    }
                    if (geo != null && geo.TryGetValue(kv.Key, out var gv) && gv is Dictionary<string, object> gd)
                    {
                        foreach (var ring in Arr(gd, "umriss_km"))
                        {
                            var pts = new List<Vector2>();
                            if (ring is List<object> rl) foreach (var q in rl) { var v = Vec(q); if (v.HasValue) pts.Add(v.Value); }
                            if (pts.Count > 2) r.Outline.Add(pts);
                        }
                        var c = Vec(gd.TryGetValue("zentrum_design", out var cz) ? cz : null);
                        if (c.HasValue) r.CenterKm = c.Value * 0.44f; // Entwurfseinheit → km (S = 0,44)
                    }
                    if (r.CenterKm == Vector2.zero && r.Places.Count > 0) r.CenterKm = r.Places[0].Km;
                    list.Add(r);
                }
                list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
                _regions = list;
                _loadedAt = DateTime.Now;
            }
            catch (Exception e)
            {
                _loadError = "Fehler beim Laden der Weltplan-Daten: " + e.Message;
                Debug.LogWarning("[Ashveil] " + _loadError);
            }
        }

        private static string Str(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) && v != null ? v.ToString() : "";
        private static float Num(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) && v is double x ? (float)x : 0f;
        private static List<object> Arr(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) && v is List<object> l ? l : new List<object>();
        private static Vector2? Pt(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) ? Vec(v) : null;

        private static Vector2? Vec(object v)
        {
            if (v is List<object> l && l.Count >= 2 && l[0] is double x && l[1] is double y) return new Vector2((float)x, (float)y);
            return null;
        }

        /// <summary>Kleiner JSON-Leser (Objekte, Listen, Zahlen als double, Texte, true/false/null) – ohne Fremdpaket.</summary>
        private static class MiniJson
        {
            public static object Parse(string s)
            {
                int i = 0;
                var v = Value(s, ref i);
                return v;
            }

            private static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

            private static object Value(string s, ref int i)
            {
                Ws(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON endet unerwartet");
                char c = s[i];
                if (c == '{') return Obj(s, ref i);
                if (c == '[') return ParseList(s, ref i);
                if (c == '"') return Text(s, ref i);
                if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
                if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
                if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
                if (s.Length - i >= 3 && string.CompareOrdinal(s, i, "NaN", 0, 3) == 0) { i += 3; return double.NaN; }
                return Number(s, ref i);
            }

            private static Dictionary<string, object> Obj(string s, ref int i)
            {
                var d = new Dictionary<string, object>();
                i++; Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (i < s.Length)
                {
                    Ws(s, ref i);
                    string key = Text(s, ref i);
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new FormatException("':' erwartet bei " + i);
                    i++;
                    d[key] = Value(s, ref i);
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return d; }
                    throw new FormatException("',' oder '}' erwartet bei " + i);
                }
                throw new FormatException("Objekt nicht geschlossen");
            }

            private static List<object> ParseList(string s, ref int i)
            {
                var l = new List<object>();
                i++; Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (i < s.Length)
                {
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return l; }
                    throw new FormatException("',' oder ']' erwartet bei " + i);
                }
                throw new FormatException("Liste nicht geschlossen");
            }

            private static string Text(string s, ref int i)
            {
                if (s[i] != '"') throw new FormatException("'\"' erwartet bei " + i);
                i++;
                var sb = new StringBuilder();
                while (i < s.Length)
                {
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                        default: sb.Append(e); break;
                    }
                }
                throw new FormatException("Text nicht geschlossen");
            }

            private static double Number(string s, ref int i)
            {
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                if (i == start) throw new FormatException("Unerwartetes Zeichen bei " + i);
                return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }
}
