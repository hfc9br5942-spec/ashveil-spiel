using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Ashveil.World;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Ashveil.EditorTools.TerrainGen
{
    /// <summary>
    /// Terrain-Generator V1 (Terrain-Vorgabe 30.09.): fuehrt die Pipeline in fester Reihenfolge aus und veroeffentlicht
    /// die Ergebnisse. Erst die grobe Makrowelt (Vorschau, Sekunden), danach einzelne Regionen hoch aufloesen –
    /// niemals umgekehrt (§131). Kareth und alle Handarbeit werden nur gelesen.
    /// </summary>
    public static class WorldTerrainGenerator
    {
        public const int GenerationVersion = 1;

        /// <summary>Pipeline in der Reihenfolge der Vorgabe §3 (sortiert nach Prioritaet).</summary>
        public static List<TerrainModifier> Pipeline() => new List<TerrainModifier>
        {
            new ContinentModifier(),
            new MountainBeltModifier(),
            new ValleyModifier(),
            new PassModifier(),
            new PlateauModifier(),
            new LakeModifier(),
            new VeilScarModifier(),
            new HydrologyPass(350),
            new RiverModifier(),
            new RoadCorridorModifier(),
            new AncientScarModifier(),
            new LocalDetailModifier(),
            new MacroErosionPass(),
            new SmoothingModifier(),
            new ProtectedAreaModifier(),
            new HydrologyPass(1100),
            new BiomePass(),
        }.OrderBy(m => m.Priority).ToList();

        public static MacroWorldProfile LoadOrCreateProfile()
        {
            ProjectSetup.EnsureFolder(TerrainGenPaths.DataFolder);
            var p = AssetDatabase.LoadAssetAtPath<MacroWorldProfile>(MacroWorldProfile.AssetPath);
            if (p == null)
            {
                p = ScriptableObject.CreateInstance<MacroWorldProfile>();
                MacroWorldDefaults.Apply(p);
                AssetDatabase.CreateAsset(p, MacroWorldProfile.AssetPath);
                AssetDatabase.SaveAssets();
            }
            else if (p.defaultsVersion < MacroWorldDefaults.Version)
            {
                // neue Standardwerte (z. B. nach Max' Weltkarte) – Profil folgt, solange es nicht von Hand gepflegt wird
                MacroWorldDefaults.Apply(p);
                EditorUtility.SetDirty(p);
                AssetDatabase.SaveAssets();
            }
            return p;
        }

        /// <summary>Makrowelt rechnen (ohne etwas zu speichern). showProgress = Fortschrittsbalken mit Abbrechen.</summary>
        public static TerrainGenerationContext Run(MacroWorldProfile profile, bool showProgress = true)
        {
            var ctx = new TerrainGenerationContext(profile);
            var sw = Stopwatch.StartNew();
            bool cancelled = false;
            ctx.Progress = (label, t) =>
            {
                if (!showProgress) return false;
                return EditorUtility.DisplayCancelableProgressBar("ASHVEIL Terrain-Generator", label, t);
            };
            try
            {
                ctx.Protected = ProtectedHeightSampler.Load(profile);
                foreach (string note in ctx.Protected.Notes) ctx.Log.Add(note);
                List<TerrainModifier> pipe = Pipeline();
                for (int k = 0; k < pipe.Count; k++)
                {
                    TerrainModifier m = pipe[k];
                    if (ctx.Progress($"{m.Priority} {m.Name}", (float)k / pipe.Count))
                    {
                        cancelled = true;
                        ctx.Log.Add("Abgebrochen.");
                        break;
                    }
                    long t0 = sw.ElapsedMilliseconds;
                    m.Apply(ctx);
                    ctx.Log.Add($"{m.Priority,4} {m.Name}: {sw.ElapsedMilliseconds - t0} ms");
                }
            }
            finally
            {
                if (showProgress) EditorUtility.ClearProgressBar();   // §94: Balken garantiert schliessen
            }
            ctx.Field.MinMax(out float mn, out float mx);
            ctx.Log.Add($"Makrowelt {ctx.Field.Width}×{ctx.Field.Height} Zellen à {ctx.Field.CellSize} m, Hoehe {mn:0}…{mx:0} m, {sw.ElapsedMilliseconds} ms, Hash {ctx.Field.Hash()}" + (cancelled ? " (abgebrochen)" : ""));
            return ctx;
        }

        /// <summary>Makrodaten fuer die Laufzeit, Kerbenpfad fuer den Himmel und Vorschau-Bilder speichern.</summary>
        public static void Publish(TerrainGenerationContext ctx)
        {
            ProjectSetup.EnsureFolder(TerrainGenPaths.ResourcesFolder);
            ProjectSetup.EnsureFolder(TerrainGenPaths.GeneratedRoot);
            MacroHeightField f = ctx.Field;
            f.MinMax(out float mn, out float mx);
            mn = Mathf.Floor(mn) - 10f;
            mx = Mathf.Ceil(mx) + 10f;

            var hBytes = new byte[f.Count * 2];
            for (int i = 0; i < f.Count; i++)
            {
                int q = Mathf.Clamp(Mathf.RoundToInt((f.H[i] - mn) / (mx - mn) * 65535f), 0, 65535);
                hBytes[i * 2] = (byte)(q & 0xFF);
                hBytes[i * 2 + 1] = (byte)(q >> 8);
            }
            float[] veil = ctx.Masks.Get(MaskSet.VeilInfluence);
            var vBytes = new byte[f.Count];
            for (int i = 0; i < f.Count; i++) vBytes[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(veil[i] * 255f), 0, 255);
            string dir = TerrainGenPaths.ResourcesFolder;
            File.WriteAllBytes(dir + "/MacroWorld_Heights.bytes", hBytes);
            File.WriteAllBytes(dir + "/MacroWorld_Biomes.bytes", ctx.Biomes ?? new byte[f.Count]);
            File.WriteAllBytes(dir + "/MacroWorld_Veil.bytes", vBytes);
            AssetDatabase.ImportAsset(dir + "/MacroWorld_Heights.bytes");
            AssetDatabase.ImportAsset(dir + "/MacroWorld_Biomes.bytes");
            AssetDatabase.ImportAsset(dir + "/MacroWorld_Veil.bytes");

            var data = AssetDatabase.LoadAssetAtPath<MacroWorldData>(TerrainGenPaths.MacroDataPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<MacroWorldData>();
                AssetDatabase.CreateAsset(data, TerrainGenPaths.MacroDataPath);
            }
            data.origin = f.Origin;
            data.cellSize = f.CellSize;
            data.width = f.Width;
            data.height = f.Height;
            data.minHeight = mn;
            data.maxHeight = mx;
            data.seaLevel = ctx.Profile.seaLevel;
            data.worldSeed = ctx.WorldSeed;
            data.generationVersion = GenerationVersion;
            data.generatedHash = f.Hash();
            data.heights = AssetDatabase.LoadAssetAtPath<TextAsset>(dir + "/MacroWorld_Heights.bytes");
            data.biomes = AssetDatabase.LoadAssetAtPath<TextAsset>(dir + "/MacroWorld_Biomes.bytes");
            data.veil = AssetDatabase.LoadAssetAtPath<TextAsset>(dir + "/MacroWorld_Veil.bytes");
            EditorUtility.SetDirty(data);

            // Kerbe veroeffentlichen (§86): der Himmel liest dieselben Punkte
            var path = AssetDatabase.LoadAssetAtPath<VeilScarPath>(TerrainGenPaths.VeilPathAsset);
            if (path == null)
            {
                path = ScriptableObject.CreateInstance<VeilScarPath>();
                AssetDatabase.CreateAsset(path, TerrainGenPaths.VeilPathAsset);
            }
            path.points.Clear();
            foreach (VeilScarPoint p in ctx.Profile.veilScar.points)
                path.points.Add(new VeilScarPath.Point { position = p.position, width = p.width, depth = p.depth, veilIntensity = p.veilIntensity });
            path.veilHeight = ctx.Profile.veilScar.veilHeight;
            path.generationVersion = GenerationVersion;
            EditorUtility.SetDirty(path);
            AssetDatabase.SaveAssets();
            MacroWorld.Override(data);

            WriteManifest(ctx);
        }

        /// <summary>Liste aller erzeugten Dateien – "Generated" ist jederzeit loeschbar (§1, §74).</summary>
        private static void WriteManifest(TerrainGenerationContext ctx)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"generationVersion\": {GenerationVersion},");
            sb.AppendLine($"  \"worldSeed\": {ctx.WorldSeed},");
            sb.AppendLine($"  \"regionSeed\": {ctx.RegionSeed}, \"terrainSeed\": {ctx.TerrainSeed}, \"biomeSeed\": {ctx.BiomeSeed}, \"detailSeed\": {ctx.DetailSeed},");
            sb.AppendLine($"  \"macroHash\": \"{ctx.Field.Hash()}\",");
            sb.AppendLine($"  \"generatedAt\": \"{DateTime.Now:yyyy-MM-dd HH:mm}\"");
            sb.AppendLine("}");
            File.WriteAllText(TerrainGenPaths.ManifestPath, sb.ToString());
        }

        /// <summary>Nur erzeugte Inhalte loeschen (§74) – nie Kareth, nie Handarbeit, nie "RESET WORLD".</summary>
        public static int DeleteGenerated()
        {
            int n = 0;
            foreach (string root in new[] { TerrainGenPaths.GeneratedRoot })
            {
                if (!AssetDatabase.IsValidFolder(root)) continue;
                foreach (string guid in AssetDatabase.FindAssets("", new[] { root }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetDatabase.IsValidFolder(p)) continue;
                    if (!Path.GetFileName(p).StartsWith("GEN_") && !p.Contains("/Preview/")) continue;
                    if (AssetDatabase.DeleteAsset(p)) n++;
                }
            }
            GeneratedWorldPlacement.RemoveFromOpenScene();
            AssetDatabase.SaveAssets();
            return n;
        }
    }
}
