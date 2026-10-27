using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Ashveil.World;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools.TerrainGen
{
    /// <summary>Ergebnis einer Pruefung: Fehler brechen die Abnahme, Warnungen sind Designer-Hinweise.</summary>
    public sealed class ValidationReport
    {
        public readonly List<string> Errors = new List<string>(), Warnings = new List<string>(), Info = new List<string>();
        public bool Ok => Errors.Count == 0;

        public void Merge(ValidationReport o)
        {
            Errors.AddRange(o.Errors);
            Warnings.AddRange(o.Warnings);
            Info.AddRange(o.Info);
        }

        public string Text()
        {
            var sb = new System.Text.StringBuilder();
            foreach (string e in Errors) sb.AppendLine("FEHLER  " + e);
            foreach (string w in Warnings) sb.AppendLine("WARNUNG " + w);
            foreach (string i in Info) sb.AppendLine("OK      " + i);
            return sb.ToString();
        }
    }

    /// <summary>
    /// Kareth-Waechter (Vorgabe §98): vor der Generierung Pruefsummen von Kareths TerrainData, NavMesh, Layout und
    /// Handarbeit festhalten, danach vergleichen. Jede Abweichung = FEHLER.
    /// </summary>
    public sealed class KarethGuard
    {
        public static readonly string[] GuardedAssets =
        {
            ProjectSetup.Root + "/Terrain/Kareth_TerrainData.asset",
            ProjectSetup.Root + "/Scenes/Sandbox_M1_NavMesh.asset",
            ProjectSetup.Root + "/Layout/KarethLayout.asset",
            ProjectSetup.Root + "/Layout/Handarbeit.prefab",
            ProjectSetup.Root + "/Terrain/WW_Hallgrund_TerrainData.asset",
            ProjectSetup.Root + "/Terrain/GG_West_Sued_TerrainData.asset",
            ProjectSetup.Root + "/Terrain/GG_West_Nord_TerrainData.asset",
            ProjectSetup.Root + "/Terrain/GG_Ost_Sued_TerrainData.asset",
            ProjectSetup.Root + "/Terrain/GG_Ost_Nord_TerrainData.asset",
        };

        private readonly Dictionary<string, string> _hashes = new Dictionary<string, string>();

        public static KarethGuard Capture()
        {
            var g = new KarethGuard();
            foreach (string p in GuardedAssets) g._hashes[p] = FileHash(p);
            return g;
        }

        public ValidationReport Verify()
        {
            var r = new ValidationReport();
            foreach (KeyValuePair<string, string> kv in _hashes)
            {
                string now = FileHash(kv.Key);
                if (kv.Value == null && now == null) continue;
                if (now != kv.Value) r.Errors.Add($"Geschuetztes Asset veraendert: {kv.Key}");
            }
            if (r.Ok) r.Info.Add($"Kareth-Schutz: {_hashes.Count} geschuetzte Dateien unveraendert");
            return r;
        }

        private static string FileHash(string assetPath)
        {
            if (!File.Exists(assetPath)) return null;
            using (MD5 md5 = MD5.Create())
            using (FileStream fs = File.OpenRead(assetPath))
                return System.BitConverter.ToString(md5.ComputeHash(fs));
        }
    }

    public static class WorldValidators
    {
        /// <summary>Fluesse grob bergab (§39, §103) – Ausnahme nur mit magicalException.</summary>
        public static ValidationReport Rivers(TerrainGenerationContext ctx)
        {
            var r = new ValidationReport();
            MacroHeightField f = ctx.Field;
            foreach (RiverDefinition river in ctx.Profile.rivers)
            {
                List<Vector2> pts = PathProfile.Resample(river.path, f.CellSize * 2f);
                int uphill = 0;
                float prev = float.MaxValue, worst = 0f;
                Vector2 at = Vector2.zero;
                foreach (Vector2 p in pts)
                {
                    float h = f.SampleBilinear(p.x, p.y);
                    if (prev < float.MaxValue && h > prev + 2f)
                    {
                        uphill++;
                        if (h - prev > worst)
                        {
                            worst = h - prev;
                            at = p;
                        }
                    }
                    prev = Mathf.Min(prev, h);
                }
                if (uphill > 2 && !river.magicalException) r.Warnings.Add($"Fluss '{river.id}': {uphill} Abschnitte bergauf (max +{worst:0} m bei {at.x:0}/{at.y:0})");
                else r.Info.Add($"Fluss '{river.id}' faellt bergab");
            }
            r.Info.Add($"{ctx.Hydrology?.Candidates.Count ?? 0} Flusskandidaten (≥ {ctx.Profile.riverCandidateAreaKm2:0} km²) – nur zur Auswahl");
            return r;
        }

        /// <summary>Hauptrouten nicht ueber Klippen, unpassierbare Kerbe oder Meer (§100).</summary>
        public static ValidationReport Routes(TerrainGenerationContext ctx)
        {
            var r = new ValidationReport();
            MacroHeightField f = ctx.Field;
            float[] inner = ctx.Masks.Get(MaskSet.ScarInner);
            float sea = ctx.Profile.seaLevel;
            foreach (RoadCorridorDefinition road in ctx.Profile.roads)
            {
                if (road.routeClass != RouteClass.Main && road.routeClass != RouteClass.Secondary) continue;
                List<Vector2> pts = PathProfile.Resample(road.path, f.CellSize);
                float maxSlope = 0f;
                Vector2 at = Vector2.zero;
                float atH0 = 0f, atH1 = 0f;
                int badScar = 0, wet = 0;
                for (int k = 1; k < pts.Count; k++)
                {
                    float h0 = f.SampleBilinear(pts[k - 1].x, pts[k - 1].y), h1 = f.SampleBilinear(pts[k].x, pts[k].y);
                    float s = Mathf.Atan(Mathf.Abs(h1 - h0) / Vector2.Distance(pts[k - 1], pts[k])) * Mathf.Rad2Deg;
                    if (s > maxSlope)
                    {
                        maxSlope = s;
                        at = pts[k];
                        atH0 = h0;
                        atH1 = h1;
                    }
                    if (f.SampleBilinear(inner, pts[k].x, pts[k].y) > 0.6f) badScar++;
                    if (h1 < sea - 1f) wet++;
                }
                string name = $"{road.routeClass} '{road.id}'";
                if (maxSlope > 80f) r.Errors.Add($"{name}: Steigung {maxSlope:0}° – Klippe");
                else if (maxSlope > 35f && road.routeClass == RouteClass.Main) r.Warnings.Add($"{name}: Steigung bis {maxSlope:0}° bei {at.x:0}/{at.y:0} ({atH0:0} → {atH1:0} m)");
                if (badScar > 0) r.Errors.Add($"{name}: quert die Kerbe an {badScar} Stellen");
                if (wet > 2) r.Warnings.Add($"{name}: {wet} Stuecke unter Meeresniveau");
                if (maxSlope <= 35f && badScar == 0 && wet <= 2) r.Info.Add($"{name}: begehbar (max {maxSlope:0}°)");
            }
            return r;
        }

        /// <summary>Progression und Fraktionsnaehe (§101–102).</summary>
        public static ValidationReport Progression(MacroWorldProfile profile)
        {
            var r = new ValidationReport();
            WorldProgressionGraph g = WorldProgressionGraph.Build(profile);
            g.Validate();
            r.Errors.AddRange(g.Errors);
            r.Warnings.AddRange(g.Warnings);
            r.Info.AddRange(g.Info);
            // Bruchmark: mehrere Zugaenge (Update §H)
            int access = 0;
            foreach (string n in g.Neighbors("Contested_Bruchmark_West")) if (!n.StartsWith("Contested")) access++;
            foreach (string n in g.Neighbors("Contested_Bruchmark_East")) if (!n.StartsWith("Contested")) access++;
            foreach (string n in g.Neighbors("Contested_Bruchmark")) if (!n.StartsWith("Contested")) access++;
            if (access < 3) r.Warnings.Add($"Bruchmark hat nur {access} Zugaenge von aussen");
            else r.Info.Add($"Bruchmark: {access} Zugaenge von aussen (beide Fraktionen)");
            return r;
        }

        /// <summary>Ist die Makrokarte wie geplant lesbar? Grobe Plausibilitaet (§129).</summary>
        public static ValidationReport Macro(TerrainGenerationContext ctx)
        {
            var r = new ValidationReport();
            MacroHeightField f = ctx.Field;
            f.MinMax(out float mn, out float mx);
            if (mx < 800f) r.Warnings.Add($"Hoechster Punkt nur {mx:0} m – Gebirge zu flach?");
            else r.Info.Add($"Hoehe {mn:0} … {mx:0} m");
            float[] inner = ctx.Masks.Get(MaskSet.ScarInner);
            int scar = 0;
            foreach (float v in inner) if (v > 0.5f) scar++;
            float km2 = scar * f.CellSize * f.CellSize / 1e6f;
            if (km2 < 5f) r.Warnings.Add($"Kerbe sehr klein im Ausschnitt ({km2:0} km²)");
            else r.Info.Add($"Grosse Kerbe im Ausschnitt: {km2:0} km²");
            foreach (string note in ctx.Protected?.Notes ?? new List<string>()) r.Warnings.Add(note);
            return r;
        }
    }
}
