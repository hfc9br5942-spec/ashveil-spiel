using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ashveil.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ashveil.EditorTools.TerrainGen
{
    /// <summary>
    /// Ablauf "erste Generierung" (Vorgabe §110, §133): Selbsttest → Makrowelt → Vorschau → Progression → Kareth-Umland
    /// → Grenzgebirge-Auslaeufer und Uebergang zur Bruchmark → Fernansicht (Bruchmark, Nharim-Seite, Kerbe) →
    /// Pruefungen. Kareth wird vorher und nachher per Pruefsumme verglichen. Bericht: Logs/Ashveil/TerrainGenerator.txt.
    /// </summary>
    public static class TerrainGeneratorRunner
    {
        public const string ReportPath = "Logs/Ashveil/TerrainGenerator.txt";

        [MenuItem("Ashveil/Welt/Terrain-Generator: Alles erzeugen (V1)", priority = 541)]
        public static void RunAllMenu() => RunAll(true);

        [MenuItem("Ashveil/Welt/Terrain-Generator: Nur Makro-Vorschau", priority = 542)]
        public static void MacroOnlyMenu()
        {
            MacroWorldProfile p = WorldTerrainGenerator.LoadOrCreateProfile();
            TerrainGenerationContext ctx = WorldTerrainGenerator.Run(p);
            WorldTerrainGenerator.Publish(ctx);
            SavePreviews(ctx);
            WriteReport(ctx, new ValidationReport(), null, "Makro-Vorschau");
        }

        [MenuItem("Ashveil/Welt/Terrain-Generator: Selbsttest (Testregion)", priority = 543)]
        public static void SelfTestMenu()
        {
            ValidationReport r = TerrainGeneratorSelfTest.Run();
            Write("Terrain-Generator Selbsttest", r.Text(), r);
        }

        public static ValidationReport RunAll(bool placeIntoScene)
        {
            var report = new ValidationReport();
            KarethGuard guard = KarethGuard.Capture();

            // 1) Testregion zuerst (§80)
            ValidationReport self = TerrainGeneratorSelfTest.Run();
            report.Merge(self);
            if (!self.Ok)
            {
                Write("Terrain-Generator: Selbsttest fehlgeschlagen – nichts erzeugt", report.Text(), report);
                return report;
            }

            // 2) Makrowelt
            MacroWorldProfile profile = WorldTerrainGenerator.LoadOrCreateProfile();
            if (profile.defaultsVersion < MacroWorldDefaults.Version)
            {
                MacroWorldDefaults.Apply(profile);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }
            TerrainGenerationContext ctx = WorldTerrainGenerator.Run(profile);
            WorldTerrainGenerator.Publish(ctx);
            SavePreviews(ctx);

            // 3) Pruefungen der Makrowelt
            report.Merge(WorldValidators.Macro(ctx));
            report.Merge(WorldValidators.Progression(profile));
            report.Merge(WorldValidators.Rivers(ctx));
            report.Merge(WorldValidators.Routes(ctx));

            // 4) Kacheln (Kareth-Umland, Grenzgebirge-Auslaeufer, Uebergang zur Bruchmark)
            List<GeneratedTile> tiles = TerrainTileGenerator.PlanRing(profile, ctx);
            TerrainTileGenerator.Generate(ctx, tiles);
            float worstSeam = tiles.Count > 0 ? tiles.Max(t => t.SeamError) : 0f;
            if (worstSeam > 0.25f) report.Errors.Add($"Naht zur Handarbeit: bis {worstSeam:0.00} m Abweichung");
            else report.Info.Add($"Naehte zur Handarbeit: max {worstSeam * 100f:0.0} cm ({tiles.Count} Kacheln)");

            // 5) Fernansicht (Bruchmark, Nharim-Seite, Kerbe als Makro-Proxy)
            var real = new List<Rect>();
            foreach (ProtectedAreaDefinition a in profile.protectedAreas) real.Add(a.rect);
            foreach (GeneratedTile t in tiles) real.Add(t.Rect);
            ProxyMeshGenerator.Generate(ctx, real);

            // 6) In die Welt-Ebene setzen (nur wenn Sandbox_M1 offen ist)
            if (placeIntoScene)
            {
                var scene = EditorSceneManager.GetActiveScene();
                if (scene.path == SandboxSceneBuilder.ScenePath)
                {
                    GeneratedWorldPlacement.PlaceIntoOpenScene(false);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    report.Info.Add("Erzeugte Welt in Sandbox_M1 gesetzt");
                }
                else report.Warnings.Add($"Sandbox_M1 nicht offen ({scene.path}) – erzeugte Welt nicht gesetzt");
            }

            // 7) Kareth unveraendert? (§98)
            report.Merge(guard.Verify());
            WriteReport(ctx, report, tiles, "Alles erzeugen (V1)");
            return report;
        }

        public static void SavePreviews(TerrainGenerationContext ctx)
        {
            var all = PreviewOverlay.Routes | PreviewOverlay.Rivers | PreviewOverlay.Pois | PreviewOverlay.Protected | PreviewOverlay.Scale;
            foreach (PreviewMode m in Enum.GetValues(typeof(PreviewMode)))
            {
                PreviewOverlay o = m == PreviewMode.Factions || m == PreviewMode.Progression ? all | PreviewOverlay.Regions
                                 : m == PreviewMode.Hydrology ? PreviewOverlay.Candidates | PreviewOverlay.Rivers | PreviewOverlay.Scale
                                 : m == PreviewMode.Relief ? all : PreviewOverlay.Scale | PreviewOverlay.Protected;
                Texture2D tex = MacroPreviewRenderer.Render(ctx, m, o);
                MacroPreviewRenderer.SavePng(tex, "GEN_MacroWorldPreview_" + m);
                UnityEngine.Object.DestroyImmediate(tex);
            }
            // Silhouettentest (§89): nur Relief, komplett grau
            Texture2D grey = MacroPreviewRenderer.Render(ctx, PreviewMode.Relief, PreviewOverlay.None);
            Color32[] px = grey.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                byte g = (byte)((px[i].r + px[i].g + px[i].b) / 3);
                px[i] = new Color32(g, g, g, 255);
            }
            grey.SetPixels32(px);
            grey.Apply();
            MacroPreviewRenderer.SavePng(grey, "GEN_MacroWorldPreview_Silhouette");
            UnityEngine.Object.DestroyImmediate(grey);
            AssetDatabase.Refresh();
        }

        private static void WriteReport(TerrainGenerationContext ctx, ValidationReport report, List<GeneratedTile> tiles, string title)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"== ASHVEIL Terrain-Generator V1 – {title} – {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Welt-Seed {ctx.WorldSeed} (DEV_ONLY) · Region {ctx.RegionSeed} · Terrain {ctx.TerrainSeed} · Biom {ctx.BiomeSeed} · Detail {ctx.DetailSeed}");
            foreach (string l in ctx.Log) sb.AppendLine("  " + l);
            if (tiles != null)
                foreach (GeneratedTile t in tiles) sb.AppendLine($"  Kachel {t.Name} Region {t.RegionId} Naht {t.SeamError * 100f:0.0} cm");
            if (ctx.Hydrology != null)
                foreach (RiverCandidate c in ctx.Hydrology.Candidates.Take(12)) sb.AppendLine($"  Flusskandidat {c.Id}: {c.AreaKm2:0} km², {c.Path.Count} Punkte, Muendung {c.Path[c.Path.Count - 1]}");
            // Biom-Anteile
            if (ctx.Biomes != null)
            {
                var counts = new int[16];
                foreach (byte b in ctx.Biomes) counts[b]++;
                for (int b = 0; b < counts.Length; b++)
                    if (counts[b] > 0) sb.AppendLine($"  Biom {(MacroBiome)b}: {100f * counts[b] / ctx.Biomes.Length:0.0} %");
            }
            // Sichtbarkeit der Landmarken (§83, grob)
            foreach (string line in LandmarkVisibility(ctx)) sb.AppendLine("  " + line);
            sb.AppendLine(report.Text());
            sb.AppendLine(report.Ok ? "ERGEBNIS: BESTANDEN" : $"ERGEBNIS: {report.Errors.Count} FEHLER");
            Write(title, sb.ToString(), report);
        }

        /// <summary>Grobe Sichtpruefung zwischen wichtigen Orten auf dem Makrofeld (§83) – wenige Strahlen, niedrige Aufloesung.</summary>
        public static List<string> LandmarkVisibility(TerrainGenerationContext ctx)
        {
            var lines = new List<string>();
            List<MacroPoiDefinition> pois = ctx.Profile.pois.Where(p => p.importance >= 2).ToList();
            MacroHeightField f = ctx.Field;
            foreach (MacroPoiDefinition target in pois.Where(p => p.importance >= 3))
            {
                var seenFrom = new List<string>();
                foreach (MacroPoiDefinition from in pois)
                {
                    if (from == target) continue;
                    float dist = Vector2.Distance(from.position, target.position);
                    if (dist > target.visibleDistance * 2.5f) continue;
                    Vector3 a = new Vector3(from.position.x, f.SampleBilinear(from.position.x, from.position.y) + 2f, from.position.y);
                    Vector3 b = new Vector3(target.position.x, f.SampleBilinear(target.position.x, target.position.y) + 20f, target.position.y);
                    bool clear = true;
                    int steps = Mathf.CeilToInt(dist / f.CellSize);
                    for (int s = 1; s < steps; s++)
                    {
                        Vector3 p = Vector3.Lerp(a, b, (float)s / steps);
                        if (f.SampleBilinear(p.x, p.z) > p.y + 1f)
                        {
                            clear = false;
                            break;
                        }
                    }
                    if (clear) seenFrom.Add(from.id);
                }
                lines.Add($"Sicht auf {target.id}: von {seenFrom.Count} Orten ({string.Join(", ", seenFrom.Take(6))})");
            }
            return lines;
        }

        private static void Write(string title, string text, ValidationReport r)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, text);
            if (r.Ok) Debug.Log($"[Ashveil] {title}: bestanden. Bericht: {ReportPath}");
            else Debug.LogWarning($"[Ashveil] {title}: {r.Errors.Count} Fehler. Bericht: {ReportPath}\n{string.Join("\n", r.Errors)}");
        }
    }
}
