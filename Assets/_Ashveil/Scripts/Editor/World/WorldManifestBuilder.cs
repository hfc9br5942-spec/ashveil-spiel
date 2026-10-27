using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ashveil.World;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Legt das Weltverzeichnis an (Large-World Phase B1) und prueft es ("Welt pruefen", Masterplan §103).
    /// Handarbeit bleibt: vorhandene Regionen/Zellen werden nicht ueberschrieben, nur fehlende ergaenzt und
    /// Szenenpfade nachgetragen. Kacheln neuer Regionen im Kareth-Raster (1200 m, x = ±600 + k·1200, z = −350 + k·1200).
    /// </summary>
    public static class WorldManifestBuilder
    {
        public const string Folder = ProjectSetup.Root + "/Data/World";
        public const string ManifestPath = Folder + "/WorldManifest.asset";
        public const string RegionFolder = ProjectSetup.Root + "/Scenes/Regions";

        public const string RegionKareth = "Human_Kareth";
        public const string RegionBorder = "Human_BorderMountains";
        public const string CellKareth = "kareth";
        public const string CellBorderWest = "gg_west";
        public const string CellBorderEast = "gg_ost";

        public static string BorderWestScene => RegionFolder + "/BorderMountains_West.unity";
        public static string BorderEastScene => RegionFolder + "/BorderMountains_East.unity";

        [MenuItem("Ashveil/Welt/Weltverzeichnis aktualisieren", priority = 500)]
        public static void BuildMenu()
        {
            WorldManifest m = EnsureManifest();
            Debug.Log($"[Ashveil] Weltverzeichnis v{m.version}: {m.regions.Count} Regionen, {m.AllCells().Count()} Zellen -> {ManifestPath}");
            Selection.activeObject = m;
        }

        public static WorldManifest EnsureManifest()
        {
            ProjectSetup.EnsureFolder(Folder);
            var m = AssetDatabase.LoadAssetAtPath<WorldManifest>(ManifestPath);
            if (m == null)
            {
                m = ScriptableObject.CreateInstance<WorldManifest>();
                AssetDatabase.CreateAsset(m, ManifestPath);
            }

            WorldRegionDefinition kareth = EnsureRegion(m, RegionKareth, "Kareth", new Rect(-600f, -350f, 1200f, 1200f), new Vector2Int(1, 18));
            if (kareth.neighbors.Count == 0) kareth.neighbors.Add(RegionBorder);
            EnsureCell(kareth, CellKareth, "", new Rect(-600f, -350f, 1200f, 1200f), alwaysLoaded: true, priority: 10);
            // Sandbox_M1 ist Welt-Ebene UND Region Kareth (Max: Kareth wird nicht zerlegt)
            kareth.cells[0].scenePath = SandboxSceneBuilder.ScenePath;

            WorldRegionDefinition border = EnsureRegion(m, RegionBorder, "Grenzgebirge", new Rect(600f, -350f, 2400f, 2400f), new Vector2Int(16, 22));
            if (border.neighbors.Count == 0)
            {
                border.neighbors.Add(RegionKareth);
                border.neighbors.Add("Contested_Bruchmark"); // noch nicht gebaut (Stub)
            }
            EnsureCell(border, CellBorderWest, BorderWestScene, new Rect(600f, -350f, 1200f, 2400f), false, 5);
            EnsureCell(border, CellBorderEast, BorderEastScene, new Rect(1800f, -350f, 1200f, 2400f), false, 5);
            // 30.09. (Max, Weltkarte): Kern suedoestlich von Kareth, um 90° gedreht – Grundflaechen nachziehen, Weltversion
            // erhoehen (gespeicherte Positionen im alten Grenzgebirge sind damit ungueltig und fallen auf sichere Anker zurueck)
            Rect gg = BorderMountainsFrame.WorldRect;
            if (border.bounds != gg)
            {
                border.bounds = gg;
                foreach (WorldCellDefinition c in border.cells)
                {
                    if (c.cellId == CellBorderWest) c.bounds = new Rect(gg.x, gg.y + 1200f, 2400f, 1200f);   // Entwurf-West = Welt-Nord
                    if (c.cellId == CellBorderEast) c.bounds = new Rect(gg.x, gg.y, 2400f, 1200f);           // Entwurf-Ost = Welt-Sued
                }
                m.version++;
            }

            // 29.09.: Westweiden (Pferdehof Hallgrund) – Tank-Klassenquests Mensch, hinter dem Weidepass
            WorldRegionDefinition west = EnsureRegion(m, WestPasturesBuilder.RegionId, "Westweiden", new Rect(-1800f, -350f, 1200f, 1200f), new Vector2Int(3, 20));
            if (west.neighbors.Count == 0) west.neighbors.Add(RegionKareth);
            if (!kareth.neighbors.Contains(WestPasturesBuilder.RegionId)) kareth.neighbors.Add(WestPasturesBuilder.RegionId);
            EnsureCell(west, WestPasturesBuilder.CellId, WestPasturesBuilder.ScenePath, new Rect(-1800f, -350f, 1200f, 1200f), false, 5);

            foreach (WorldRegionDefinition r in m.regions) EditorUtility.SetDirty(r);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        private static WorldRegionDefinition EnsureRegion(WorldManifest m, string id, string name, Rect bounds, Vector2Int levels)
        {
            WorldRegionDefinition r = m.FindRegion(id);
            if (r != null) return r;
            string path = $"{Folder}/Region_{id}.asset";
            r = AssetDatabase.LoadAssetAtPath<WorldRegionDefinition>(path);
            if (r == null)
            {
                r = ScriptableObject.CreateInstance<WorldRegionDefinition>();
                r.regionId = id;
                r.displayName = name;
                r.bounds = bounds;
                r.recommendedLevel = levels;
                AssetDatabase.CreateAsset(r, path);
            }
            m.regions.Add(r);
            return r;
        }

        private static void EnsureCell(WorldRegionDefinition r, string id, string scene, Rect bounds, bool alwaysLoaded, int priority)
        {
            WorldCellDefinition c = r.cells.FirstOrDefault(x => x != null && x.cellId == id);
            if (c == null)
            {
                c = new WorldCellDefinition { cellId = id, bounds = bounds, alwaysLoaded = alwaysLoaded, streamingPriority = priority };
                r.cells.Add(c);
            }
            if (!string.IsNullOrEmpty(scene)) c.scenePath = scene;
        }

        /// <summary>Streamer (Large-World E1) in der Welt-Ebene anlegen bzw. mit dem Weltverzeichnis verbinden.</summary>
        public static void EnsureStreamer()
        {
            WorldManifest m = EnsureManifest();
            var streamer = Object.FindAnyObjectByType<Ashveil.Networking.WorldStreamer>();
            if (streamer == null) streamer = new GameObject("WorldStreamer").AddComponent<Ashveil.Networking.WorldStreamer>();
            var so = new SerializedObject(streamer);
            so.FindProperty("manifest").objectReferenceValue = m;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(streamer);
        }

        /// <summary>Zellen-Szenen in die Build-Liste (nach Sandbox_M1), damit Server und Test-Client sie laden koennen.</summary>
        public static void EnsureBuildSettings()
        {
            WorldManifest m = AssetDatabase.LoadAssetAtPath<WorldManifest>(ManifestPath);
            if (m == null) return;
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            bool changed = false;
            foreach ((WorldRegionDefinition _, WorldCellDefinition c) in m.AllCells())
            {
                if (c.alwaysLoaded || string.IsNullOrEmpty(c.scenePath) || !File.Exists(c.scenePath)) continue;
                if (scenes.Any(s => s.path == c.scenePath)) continue;
                scenes.Add(new EditorBuildSettingsScene(c.scenePath, true));
                changed = true;
            }
            if (changed) EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>Pfade aller Zellen-Szenen (fuer den Test-Client-Build).</summary>
        public static List<string> CellScenes()
        {
            var list = new List<string>();
            WorldManifest m = AssetDatabase.LoadAssetAtPath<WorldManifest>(ManifestPath);
            if (m == null) return list;
            foreach ((WorldRegionDefinition _, WorldCellDefinition c) in m.AllCells())
                if (!c.alwaysLoaded && !string.IsNullOrEmpty(c.scenePath) && File.Exists(c.scenePath)) list.Add(c.scenePath);
            return list;
        }

        // ================================================================== Pruefen

        [MenuItem("Ashveil/Welt/Welt pruefen", priority = 501)]
        public static void ValidateMenu()
        {
            List<string> problems = Validate(out string report);
            string path = Ashveil.Core.ProjectPaths.Logs("Ashveil_WeltPruefung.txt");
            File.WriteAllText(path, report, new UTF8Encoding(false));
            if (problems.Count == 0) Debug.Log("[Ashveil] Welt pruefen: alles in Ordnung.\n" + report);
            else Debug.LogWarning($"[Ashveil] Welt pruefen: {problems.Count} Hinweise.\n" + report);
        }

        /// <summary>Masterplan §103: Regionen, Nachbarn, doppelte Kennungen, fehlende Szenen/Anker, Ueberlappung, Naehte.</summary>
        public static List<string> Validate(out string report)
        {
            var problems = new List<string>();
            var sb = new StringBuilder("ASHVEIL Welt pruefen · " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            WorldManifest m = AssetDatabase.LoadAssetAtPath<WorldManifest>(ManifestPath);
            if (m == null)
            {
                problems.Add("Weltverzeichnis fehlt (Ashveil › Welt › Weltverzeichnis aktualisieren)");
                report = sb.AppendLine("  ! " + problems[0]).ToString();
                return problems;
            }
            sb.AppendLine($"  Weltversion {m.version}, {m.regions.Count} Regionen");
            var regionIds = new HashSet<string>();
            var cellIds = new HashSet<string>();
            var nodeIds = new HashSet<string>();
            var cells = m.AllCells().ToList();
            foreach (WorldRegionDefinition r in m.regions)
            {
                if (r == null) { problems.Add("leerer Regionseintrag"); continue; }
                if (!regionIds.Add(r.regionId)) problems.Add("doppelte Region " + r.regionId);
                sb.AppendLine($"  Region {r.regionId} ({r.displayName}) Stufe {r.recommendedLevel.x}–{r.recommendedLevel.y}, {r.cells.Count} Zellen, Nachbarn: {string.Join(", ", r.neighbors)}");
                foreach (string n in r.neighbors)
                    if (m.FindRegion(n) == null) sb.AppendLine($"    · Nachbar {n} noch nicht gebaut (Stub)");
                // Fraktionskrieg-Direktive 05.10.: Territorium und lokale Konfliktknoten (Daten, keine Laufzeitlogik);
                // die PvP-Regel ist ein getrenntes Regelwerk
                int nodeCount = r.conflictNodes != null ? r.conflictNodes.Count : 0;
                sb.AppendLine($"    Territorium {r.factionAffinity} · Stufenbogen {r.progressionTier} · PvP-Regel '{r.pvpRules}' (getrennt) · {nodeCount} Konfliktknoten");
                problems.AddRange(TerritoryRules.Validate(r));
                if (r.conflictNodes != null)
                {
                    var seenHere = new HashSet<string>();   // Doppelte in derselben Region meldet TerritoryRules.Validate
                    foreach (ConflictNodeDefinition node in r.conflictNodes)
                    {
                        if (node == null || string.IsNullOrEmpty(node.nodeId) || !seenHere.Add(node.nodeId)) continue;
                        if (!nodeIds.Add(node.nodeId)) problems.Add($"Konfliktknoten {node.nodeId} gibt es in mehreren Regionen");
                        sb.AppendLine($"    Knoten {node.nodeId} ({node.kind}, {node.claimedBy}{(node.proposalOnly ? ", Vorschlag" : "")})" +
                                      (node.HasLocalStates ? $" · Zustaende {string.Join("/", node.localStates)}, Start {node.defaultState}" : " · fester Ort"));
                    }
                }
                foreach (WorldCellDefinition c in r.cells)
                {
                    if (!cellIds.Add(c.cellId)) problems.Add("doppelte Zelle " + c.cellId);
                    bool sceneOk = string.IsNullOrEmpty(c.scenePath) || File.Exists(c.scenePath);
                    if (!sceneOk) problems.Add($"Szene fehlt: {c.cellId} → {c.scenePath}");
                    if (!c.alwaysLoaded && c.unloadMargin <= c.loadMargin) problems.Add($"{c.cellId}: Entladerand muss groesser als Laderand sein");
                    if (c.bounds.xMin < r.bounds.xMin - 0.1f || c.bounds.xMax > r.bounds.xMax + 0.1f ||
                        c.bounds.yMin < r.bounds.yMin - 0.1f || c.bounds.yMax > r.bounds.yMax + 0.1f)
                        problems.Add($"{c.cellId} liegt ausserhalb seiner Region");
                    sb.AppendLine($"    Zelle {c.cellId}: x {c.bounds.xMin:0}…{c.bounds.xMax:0}, z {c.bounds.yMin:0}…{c.bounds.yMax:0} · " +
                                  $"{(c.alwaysLoaded ? "immer geladen" : $"laden {c.loadMargin:0} m / entladen {c.unloadMargin:0} m / halten {c.holdSeconds:0} s")} · " +
                                  $"{(sceneOk ? "Szene ok" : "SZENE FEHLT")}");
                }
            }
            for (int i = 0; i < cells.Count; i++)
                for (int j = i + 1; j < cells.Count; j++)
                {
                    Rect a = cells[i].cell.bounds, b = cells[j].cell.bounds;
                    Rect overlap = Rect.MinMaxRect(Mathf.Max(a.xMin, b.xMin), Mathf.Max(a.yMin, b.yMin), Mathf.Min(a.xMax, b.xMax), Mathf.Min(a.yMax, b.yMax));
                    if (overlap.width > 0.5f && overlap.height > 0.5f)
                        problems.Add($"Zellen {cells[i].cell.cellId} und {cells[j].cell.cellId} ueberlappen");
                }
            CheckSeams(problems, sb);
            CheckAnchors(m, problems, sb);
            foreach (string p in problems) sb.AppendLine("  ! " + p);
            sb.AppendLine(problems.Count == 0 ? "ERGEBNIS: in Ordnung" : $"ERGEBNIS: {problems.Count} Hinweise");
            report = sb.ToString();
            return problems;
        }

        /// <summary>Naehte zwischen Gelaendekacheln (Masterplan §103): gleiche Hoehe an gemeinsamen Kanten (Toleranz 5 cm).</summary>
        private static void CheckSeams(List<string> problems, StringBuilder sb)
        {
            var tiles = new List<(string name, TerrainData data, Vector3 origin)>();
            var kareth = AssetDatabase.LoadAssetAtPath<TerrainData>(ProjectSetup.Root + "/Terrain/Kareth_TerrainData.asset");
            if (kareth != null) tiles.Add(("Kareth", kareth, new Vector3(KarethTerrainBuilder.MinX, -40f, KarethTerrainBuilder.MinZ)));
            var ww = AssetDatabase.LoadAssetAtPath<TerrainData>(WestPasturesBuilder.TileDataPath);
            if (ww != null) tiles.Add(("Westweiden", ww, new Vector3(WestPasturesLayout.MinX, WestPasturesLayout.BaseY, WestPasturesLayout.MinZ)));
            foreach (BorderMountainsBuilder.Tile t in BorderMountainsBuilder.Tiles)
            {
                var d = AssetDatabase.LoadAssetAtPath<TerrainData>(t.DataPath);
                Vector2 wo = BorderMountainsBuilder.WorldOrigin(t);   // 30.09.: Kern gedreht (BorderMountainsFrame)
                if (d != null) tiles.Add((t.Name, d, new Vector3(wo.x, BorderMountainsLayout.BaseY, wo.y)));
            }
            // 30.09.: erzeugtes Umland (Terrain-Generator V1)
            if (AssetDatabase.IsValidFolder(TerrainGen.TerrainGenPaths.TerrainFolder))
                foreach (string guid in AssetDatabase.FindAssets("t:TerrainData", new[] { TerrainGen.TerrainGenPaths.TerrainFolder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!TerrainGen.GeneratedWorldPlacement.TryParseCoord(Path.GetFileNameWithoutExtension(path), out Vector2Int c)) continue;
                    Vector2 o = TerrainGen.TerrainTileGenerator.TileOrigin(c);
                    tiles.Add(($"GEN {c.x}/{c.y}", AssetDatabase.LoadAssetAtPath<TerrainData>(path), new Vector3(o.x, TerrainGen.TerrainTileGenerator.BaseY, o.y)));
                }
            int seams = 0;
            for (int i = 0; i < tiles.Count; i++)
                for (int j = 0; j < tiles.Count; j++)
                {
                    if (i == j) continue;
                    var a = tiles[i];
                    var b = tiles[j];
                    Vector3 sa = a.data.size, sb2 = b.data.size;
                    bool eastWest = Mathf.Abs(a.origin.x + sa.x - b.origin.x) < 0.01f && a.origin.z < b.origin.z + sb2.z - 1f && b.origin.z < a.origin.z + sa.z - 1f;
                    bool northSouth = Mathf.Abs(a.origin.z + sa.z - b.origin.z) < 0.01f && a.origin.x < b.origin.x + sb2.x - 1f && b.origin.x < a.origin.x + sa.x - 1f;
                    if (!eastWest && !northSouth) continue;
                    // unterschiedliche Aufloesung (513 an Handarbeit, 257 Greybox): an den gemeinsamen Stuetzstellen vergleichen
                    int resA = a.data.heightmapResolution, resB = b.data.heightmapResolution;
                    int res = Mathf.Min(resA, resB);
                    int stepA = (resA - 1) / (res - 1), stepB = (resB - 1) / (res - 1);
                    float max = 0f;
                    float cell = sa.x / (res - 1);
                    for (int k = 0; k < res; k++)
                    {
                        float along = (eastWest ? a.origin.z : a.origin.x) + k * cell;
                        float bStart = eastWest ? b.origin.z : b.origin.x, bLen = eastWest ? sb2.z : sb2.x;
                        if (along < bStart - 0.01f || along > bStart + bLen + 0.01f) continue;
                        int kb = Mathf.RoundToInt((along - bStart) / cell);
                        float ha = eastWest ? a.data.GetHeight(resA - 1, k * stepA) : a.data.GetHeight(k * stepA, resA - 1);
                        float hb = eastWest ? b.data.GetHeight(0, kb * stepB) : b.data.GetHeight(kb * stepB, 0);
                        max = Mathf.Max(max, Mathf.Abs(a.origin.y + ha - (b.origin.y + hb)));
                    }
                    seams++;
                    sb.AppendLine($"  Naht {a.name} | {b.name}: groesster Hoehensprung {max * 100f:0.0} cm");
                    if (max > 0.05f) problems.Add($"Naht {a.name}|{b.name}: {max * 100f:0} cm Sprung");
                }
            if (seams == 0) sb.AppendLine("  (noch keine benachbarten Kacheln)");
        }

        /// <summary>Jede Zelle mit Szene braucht mindestens einen sicheren Anker (§49) – geprueft in der gespeicherten Szene.</summary>
        private static void CheckAnchors(WorldManifest m, List<string> problems, StringBuilder sb)
        {
            foreach ((WorldRegionDefinition _, WorldCellDefinition c) in m.AllCells())
            {
                if (c.alwaysLoaded || string.IsNullOrEmpty(c.scenePath) || !File.Exists(c.scenePath)) continue;
                string text = File.ReadAllText(c.scenePath);
                int anchors = System.Text.RegularExpressions.Regex.Matches(text, "anchorId: ").Count;
                sb.AppendLine($"  Zelle {c.cellId}: {anchors} sichere Anker");
                if (anchors == 0) problems.Add($"Zelle {c.cellId} ohne sicheren Anker");
            }
        }
    }
}
