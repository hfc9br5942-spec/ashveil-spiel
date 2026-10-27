using System.Collections.Generic;
using System.Linq;
using Ashveil.World;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools.TerrainGen
{
    /// <summary>
    /// ASHVEIL TERRAIN GENERATOR (Vorgabe §70–74): Welt · Makro · Regionen · Debug · Pruefung. Erst Vorschau (Sekunden),
    /// dann Erzeugen. Knoepfe klar getrennt: NEUES Gelaende erzeugen / ERZEUGTES neu erzeugen / erzeugtes loeschen –
    /// es gibt kein "Welt zuruecksetzen". Kareth und alle Handarbeit sind geschuetzt.
    /// </summary>
    public sealed class TerrainGeneratorWindow : EditorWindow
    {
        private MacroWorldProfile _profile;
        private TerrainGenerationContext _ctx;
        private Texture2D _preview, _map;
        private PreviewMode _mode = PreviewMode.Relief;
        private PreviewOverlay _overlay = PreviewOverlay.Routes | PreviewOverlay.Rivers | PreviewOverlay.Pois | PreviewOverlay.Protected | PreviewOverlay.Scale;
        private float _mapBlend;
        private bool _labels = true;
        private Vector2 _scroll;
        private string _status = "";
        private ValidationReport _lastReport;

        [MenuItem("Tools/Ashveil/World/Terrain Generator")]
        [MenuItem("Ashveil/Welt/Terrain-Generator (Fenster)", priority = 540)]
        public static void Open() => GetWindow<TerrainGeneratorWindow>("ASHVEIL Terrain Generator");

        private void OnEnable()
        {
            _profile = AssetDatabase.LoadAssetAtPath<MacroWorldProfile>(MacroWorldProfile.AssetPath);
            _map = AssetDatabase.LoadAssetAtPath<Texture2D>(MacroWorldDefaults.MapImagePath);
        }

        private void OnDisable()
        {
            if (_preview != null) DestroyImmediate(_preview);
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("ASHVEIL TERRAIN GENERATOR V1", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Erst Vorschau, dann Erzeugen. Kareth, Westweiden und der Grenzgebirge-Kern sind Handarbeit und werden nur gelesen. " +
                                    "Alles Erzeugte liegt unter Generated/World (GEN_…) und ist jederzeit loeschbar.", MessageType.Info);

            // ---------------------------------------------------------------- WORLD
            EditorGUILayout.LabelField("WELT", EditorStyles.boldLabel);
            _profile = (MacroWorldProfile)EditorGUILayout.ObjectField("Profil", _profile, typeof(MacroWorldProfile), false);
            if (_profile == null)
            {
                if (GUILayout.Button("Profil anlegen (Werte nach Max' Weltkarte)")) _profile = WorldTerrainGenerator.LoadOrCreateProfile();
                EditorGUILayout.EndScrollView();
                return;
            }
            EditorGUI.BeginChangeCheck();
            int seed = EditorGUILayout.IntField("Welt-Seed (DEV_ONLY)", _profile.worldSeed);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_profile, "Welt-Seed");
                _profile.worldSeed = seed;
                EditorUtility.SetDirty(_profile);
            }
            if (GUILayout.Button("Profil auf Kartenwerte zuruecksetzen (nur Profil, keine Assets)") &&
                EditorUtility.DisplayDialog("Profil", "Profilwerte auf die Standardwerte nach Max' Weltkarte setzen? Eigene Aenderungen am Profil gehen verloren.", "Ja", "Nein"))
            {
                Undo.RecordObject(_profile, "Profil zuruecksetzen");
                MacroWorldDefaults.Apply(_profile);
                EditorUtility.SetDirty(_profile);
            }

            // ---------------------------------------------------------------- MACRO
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("MAKRO", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Makro-Vorschau erzeugen")) RunMacro(false);
                if (GUILayout.Button("Hoehenfeld + Abfluss neu")) RunMacro(false);
                if (GUILayout.Button("Vorschau veroeffentlichen")) RunMacro(true);
            }

            // ---------------------------------------------------------------- REGIONS
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("REGIONEN", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("GENERATE NEW TERRAIN\n(Umland, Grenzgebirge-Auslaeufer, Bruchmark-Rand)", GUILayout.Height(38))) GenerateTiles();
                if (GUILayout.Button("Fernansicht (Proxies)\nBruchmark, Nharim-Seite, Kerbe", GUILayout.Height(38))) GenerateProxies();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Alles erzeugen (V1)")) { _lastReport = TerrainGeneratorRunner.RunAll(true); _status = _lastReport.Ok ? "Alles erzeugt – bestanden" : $"{_lastReport.Errors.Count} Fehler"; }
                if (GUILayout.Button("In offene Szene setzen")) { GeneratedWorldPlacement.PlaceIntoOpenScene(); _status = "Erzeugte Welt gesetzt"; }
                if (GUILayout.Button("Erzeugtes loeschen") &&
                    EditorUtility.DisplayDialog("Erzeugtes loeschen", "Nur GEN_-Dateien unter Generated/World und der Knoten GEN_World werden entfernt. Handarbeit bleibt.", "Loeschen", "Abbrechen"))
                    _status = $"{WorldTerrainGenerator.DeleteGenerated()} erzeugte Dateien geloescht";
            }

            // ---------------------------------------------------------------- DEBUG
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("DEBUG", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _mode = (PreviewMode)EditorGUILayout.EnumPopup("Ansicht", _mode);
            _overlay = (PreviewOverlay)EditorGUILayout.EnumFlagsField("Einblendungen", _overlay);
            _labels = EditorGUILayout.Toggle("Beschriftungen", _labels);
            if (EditorGUI.EndChangeCheck()) Redraw();
            _mapBlend = EditorGUILayout.Slider("Max' Weltkarte darueber", _mapBlend, 0f, 1f);

            if (_preview != null) DrawPreview();
            else EditorGUILayout.HelpBox("Noch keine Vorschau – 'Makro-Vorschau erzeugen'.", MessageType.None);

            // ---------------------------------------------------------------- VALIDATION
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("PRUEFUNG", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Welt pruefen")) Validate(r => { r.Merge(WorldValidators.Macro(_ctx)); r.Merge(WorldValidators.Progression(_profile)); r.Merge(WorldValidators.Routes(_ctx)); });
                if (GUILayout.Button("Kareth-Schutz")) { _lastReport = KarethGuard.Capture().Verify(); }
                if (GUILayout.Button("Naehte")) { WorldManifestBuilder.Validate(out string rep); _lastReport = new ValidationReport(); _lastReport.Info.Add(rep); }
                if (GUILayout.Button("Fluesse")) Validate(r => r.Merge(WorldValidators.Rivers(_ctx)));
                if (GUILayout.Button("Selbsttest")) _lastReport = TerrainGeneratorSelfTest.Run();
            }
            if (_lastReport != null) EditorGUILayout.HelpBox(_lastReport.Text(), _lastReport.Ok ? MessageType.Info : MessageType.Warning);

            // Flusskandidaten (PROMOTE TO RIVER, §37)
            if (_ctx?.Hydrology != null && _ctx.Hydrology.Candidates.Count > 0)
            {
                EditorGUILayout.LabelField($"Flusskandidaten ({_ctx.Hydrology.Candidates.Count})", EditorStyles.miniBoldLabel);
                foreach (RiverCandidate c in _ctx.Hydrology.Candidates.Take(10))
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField($"{c.Id}: {c.AreaKm2:0.0} km², {c.Path.Count} Punkte");
                        if (GUILayout.Button("PROMOTE TO RIVER", GUILayout.Width(150)))
                        {
                            Undo.RecordObject(_profile, "Fluss befoerdern");
                            _profile.rivers.Add(new RiverDefinition { id = "fluss_" + c.Id, path = new List<Vector2>(c.Path), width = Mathf.Clamp(c.AreaKm2 * 0.8f, 8f, 60f), depth = 4f, promoted = true });
                            EditorUtility.SetDirty(_profile);
                        }
                    }
            }
            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        private void RunMacro(bool publish)
        {
            _ctx = WorldTerrainGenerator.Run(_profile);
            if (publish)
            {
                WorldTerrainGenerator.Publish(_ctx);
                TerrainGeneratorRunner.SavePreviews(_ctx);
            }
            _status = _ctx.Log.LastOrDefault();
            Redraw();
        }

        private void GenerateTiles()
        {
            if (_ctx == null) RunMacro(true);
            KarethGuard guard = KarethGuard.Capture();
            List<GeneratedTile> tiles = TerrainTileGenerator.PlanRing(_profile, _ctx);
            TerrainTileGenerator.Generate(_ctx, tiles);
            _lastReport = guard.Verify();
            _status = $"{tiles.Count} Kacheln erzeugt (PROTECTED REGION SKIPPED: Kareth, Westweiden, Grenzgebirge-Kern)";
        }

        private void GenerateProxies()
        {
            if (_ctx == null) RunMacro(true);
            var real = _profile.protectedAreas.Select(a => a.rect).ToList();
            foreach (GeneratedTile t in TerrainTileGenerator.PlanRing(_profile, _ctx)) real.Add(t.Rect);
            ProxyMeshGenerator.Generate(_ctx, real);
            _status = "Fernansicht erzeugt";
        }

        private void Validate(System.Action<ValidationReport> body)
        {
            if (_ctx == null) RunMacro(false);
            _lastReport = new ValidationReport();
            body(_lastReport);
        }

        private void Redraw()
        {
            if (_ctx == null) return;
            _preview = MacroPreviewRenderer.Render(_ctx, _mode, _overlay, _preview);
            Repaint();
        }

        private void DrawPreview()
        {
            float w = position.width - 20f;
            float h = w * _preview.height / _preview.width;
            Rect r = GUILayoutUtility.GetRect(w, h);
            GUI.DrawTexture(r, _preview, ScaleMode.ScaleToFit);
            if (_mapBlend > 0.01f && _map != null)
            {
                // Karte liegt im selben Massstab (20 m je Pixel) – auf den Ausschnitt abbilden
                Rect b = _profile.bounds;
                Vector2 m0 = MacroWorldDefaults.ToMap(new Vector2(b.xMin, b.yMax)), m1 = MacroWorldDefaults.ToMap(new Vector2(b.xMax, b.yMin));
                var uv = new Rect(m0.x / _map.width, 1f - m1.y / _map.height, (m1.x - m0.x) / _map.width, (m1.y - m0.y) / _map.height);
                Color old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, _mapBlend);
                GUI.DrawTextureWithTexCoords(r, _map, uv);
                GUI.color = old;
            }
            if (!_labels) return;
            var style = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = Color.white } };
            Rect bounds = _profile.bounds;
            Vector2 ToGui(Vector2 wp) => new Vector2(r.x + (wp.x - bounds.xMin) / bounds.width * r.width, r.y + (1f - (wp.y - bounds.yMin) / bounds.height) * r.height);
            foreach (MacroPoiDefinition p in _profile.pois)
            {
                if (p.importance < 2) continue;
                Vector2 g = ToGui(p.position);
                GUI.Label(new Rect(g.x + 4, g.y - 8, 220, 16), p.label, style);
            }
            if ((_overlay & PreviewOverlay.Regions) != 0)
                foreach (MacroRegionDefinition reg in _profile.regions)
                {
                    if (reg.polygon.Count < 3) continue;
                    Vector2 c = WorldProgressionGraph.Centroid(reg);
                    Vector2 g = ToGui(c);
                    GUI.Label(new Rect(g.x - 60, g.y, 200, 16), $"{reg.displayName} {reg.levels.x}–{reg.levels.y}", EditorStyles.miniLabel);
                }
        }
    }
}
