using System.Collections.Generic;
using Ashveil.World;
using UnityEngine;

namespace Ashveil.EditorTools.TerrainGen
{
    /// <summary>
    /// Testregion 4 × 4 km (Vorgabe §80) und Modultests (§124–125): kleines Gebirge, Tal, Pass, Fluss, Plateau, ein Stueck
    /// Kerbe – ohne Kueste, ohne Handarbeit. Wird von den EditMode-Tests und vom Menue benutzt.
    /// </summary>
    public static class TerrainGeneratorSelfTest
    {
        public static MacroWorldProfile TestProfile(int seed = 4242)
        {
            MacroWorldProfile p = ScriptableObject.CreateInstance<MacroWorldProfile>();
            p.hideFlags = HideFlags.HideAndDontSave;
            p.worldSeed = seed;
            p.bounds = new Rect(0f, 0f, 4000f, 4000f);
            p.cellSize = 20f;
            p.seaLevel = -24f;
            p.coastline = new List<Vector2> { new Vector2(-5000f, -5000f), new Vector2(-5000f, 9000f), new Vector2(9000f, 9000f), new Vector2(9000f, -5000f) };
            p.coastNoise = 0f;
            p.lowlandHeight = 40f;
            p.regionalNoiseAmp = 10f;
            p.mountainBelts = new List<MountainBeltDefinition>
            {
                new MountainBeltDefinition { id = "test_belt", width = 1800f, baseElevation = 80f, peakElevation = 600f, ridgeScale = 700f, seed = 3,
                    controlPoints = new List<Vector2> { new Vector2(2600f, 200f), new Vector2(2700f, 2000f), new Vector2(2500f, 3800f) } }
            };
            p.valleys = new List<ValleyDefinition>
            {
                new ValleyDefinition { id = "test_valley", width = 700f, floorWidth = 120f, sideSlope = 2f, meander = 40f,
                    centerline = new List<Vector3> { new Vector3(3900f, 70f, 2100f), new Vector3(2700f, 60f, 2000f), new Vector3(1400f, 30f, 1700f), new Vector3(100f, 10f, 1500f) } }
            };
            p.passes = new List<PassDefinition>
            {
                new PassDefinition { id = "test_pass", start = new Vector2(2000f, 3000f), end = new Vector2(3300f, 3100f), width = 160f, targetElevation = 260f, shoulderFalloff = 300f }
            };
            p.plateaus = new List<PlateauDefinition>
            {
                new PlateauDefinition { id = "test_plateau", center = new Vector2(900f, 3000f), radii = new Vector2(450f, 320f), targetHeight = 180f, cliffStrength = 0.7f, falloff = 160f }
            };
            p.veilScar = new VeilScarDefinition
            {
                points = new List<VeilScarPoint>
                {
                    new VeilScarPoint { position = new Vector2(-200f, 400f), width = 500f, floorElevation = -60f, rimUplift = 40f, fractureStrength = 0.5f, terraceStrength = 0.5f, veilIntensity = 1f },
                    new VeilScarPoint { position = new Vector2(1800f, 350f), width = 700f, floorElevation = -40f, rimUplift = 60f, fractureStrength = 0.5f, terraceStrength = 0.5f, veilIntensity = 1f },
                }
            };
            p.rivers = new List<RiverDefinition>
            {
                new RiverDefinition { id = "test_river", width = 20f, depth = 4f, path = new List<Vector2> { new Vector2(3900f, 2100f), new Vector2(2700f, 2000f), new Vector2(1400f, 1700f), new Vector2(100f, 1500f) } }
            };
            p.regions = new List<MacroRegionDefinition>();
            p.protectedAreas = new List<ProtectedAreaDefinition>();
            p.erosionIterations = 4;
            p.riverCandidateAreaKm2 = 0.5f;
            return p;
        }

        public static TerrainGenerationContext RunTest(int seed = 4242)
        {
            MacroWorldProfile p = TestProfile(seed);
            var ctx = new TerrainGenerationContext(p) { Protected = new ProtectedHeightSampler() };
            foreach (TerrainModifier m in WorldTerrainGenerator.Pipeline()) m.Apply(ctx);
            return ctx;
        }

        public static ValidationReport Run()
        {
            var r = new ValidationReport();
            // Rauschen deterministisch (§125)
            double a = AshNoise.Fbm(1234.5, -987.25, 800, 5, 77), b = AshNoise.Fbm(1234.5, -987.25, 800, 5, 77), c = AshNoise.Fbm(1234.5, -987.25, 800, 5, 78);
            Check(r, a == b, "Rauschen: gleicher Seed = gleicher Wert");
            Check(r, a != c, "Rauschen: anderer Seed = anderer Wert");
            double ridge = AshNoise.Ridged(500, 500, 700, 5, 9);
            Check(r, ridge >= 0 && ridge <= 1.0001, $"Ridged-Rauschen im Bereich 0…1 ({ridge:0.000})");

            TerrainGenerationContext t1 = RunTest(4242), t2 = RunTest(4242), t3 = RunTest(4343);
            Check(r, t1.Field.Hash() == t2.Field.Hash(), "Testregion: gleicher Seed = gleiche Welt");
            Check(r, t1.Field.Hash() != t3.Field.Hash(), "Testregion: anderer Seed = andere Details");
            MacroHeightField f = t1.Field;
            float crest = f.SampleBilinear(2700f, 1000f), valley = f.SampleBilinear(2700f, 2000f), pass = f.SampleBilinear(2650f, 3050f);
            float ridgeNearPass = Mathf.Max(f.SampleBilinear(2650f, 3450f), f.SampleBilinear(2650f, 2700f));
            Check(r, crest > 250f, $"Gebirge: Kamm hoch ({crest:0} m)");
            Check(r, valley < crest - 150f, $"Tal schneidet ins Gebirge ({valley:0} m < Kamm {crest:0} m)");
            Check(r, pass < ridgeNearPass, $"Pass liegt tiefer als der Kamm daneben ({pass:0} < {ridgeNearPass:0} m)");
            float plateauMid = f.SampleBilinear(900f, 3000f), plateauEdge = f.SampleBilinear(900f + 280f, 3000f);
            Check(r, Mathf.Abs(plateauMid - 180f) < 25f && Mathf.Abs(plateauEdge - plateauMid) < 30f, $"Plateau: flache Oberflaeche ({plateauMid:0} / {plateauEdge:0} m)");
            float scarFloor = f.SampleBilinear(800f, 380f), scarOutside = f.SampleBilinear(800f, 1100f);
            Check(r, scarFloor < scarOutside - 60f, $"Kerbe: Sohle tief unter dem Umland ({scarFloor:0} < {scarOutside:0} m)");
            ValidationReport rivers = WorldValidators.Rivers(t1);
            Check(r, rivers.Warnings.Count == 0, "Fluss faellt bergab" + (rivers.Warnings.Count > 0 ? ": " + rivers.Warnings[0] : ""));
            Check(r, t1.Hydrology != null && t1.Hydrology.Candidates.Count > 0, $"Abfluss: {t1.Hydrology?.Candidates.Count ?? 0} Flusskandidaten");

            // Naht-Pruefung ueber den Schutz-Mechanismus (§99, §75): ein kuenstlicher Schutzbereich
            var ps = new ProtectedHeightSampler();
            var fake = new ProtectedHeightSampler.Area { Id = "Test", Rect = new Rect(1000f, 1000f, 400f, 400f) };
            ps.Areas.Add(fake);
            float blended = ProtectedAreaModifier.BlendAt(ps, 300f, 1000f, 1200f, 999f, out float wgt);
            Check(r, wgt > 0.99f, "Schutz: am Rand gilt die Randhoehe der Handarbeit");
            ProtectedAreaModifier.BlendAt(ps, 300f, 600f, 1200f, 999f, out float far);
            Check(r, far <= 0f, "Schutz: ausserhalb der Uebergangszone bleibt die Makrohoehe");
            Object.DestroyImmediate(t1.Profile);
            Object.DestroyImmediate(t2.Profile);
            Object.DestroyImmediate(t3.Profile);
            return r;
        }

        private static void Check(ValidationReport r, bool ok, string text)
        {
            if (ok) r.Info.Add(text);
            else r.Errors.Add(text);
        }
    }
}
