using System.IO;
using System.Linq;
using System.Text;
using Ashveil.EditorTools.TerrainGen;
using Ashveil.World;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// EditMode-Tests des Terrain-Generators (Vorgabe §124–125): Rauschen, Gebirge, Tal, Pass, Kerbe, Abfluss, Naht/Schutz,
    /// Determinismus. Rechnen auf der Testregion (4 × 4 km) – schnell, ohne Szene, ohne Handarbeit.
    /// </summary>
    public sealed class TerrainGeneratorTests
    {
        private static TerrainGenerationContext _a;

        private static TerrainGenerationContext A => _a ?? (_a = TerrainGeneratorSelfTest.RunTest(4242));

        [Test]
        public void DeterministicNoise()
        {
            Assert.AreEqual(AshNoise.Fbm(1234.5, -987.25, 800, 5, 77), AshNoise.Fbm(1234.5, -987.25, 800, 5, 77));
            Assert.AreNotEqual(AshNoise.Fbm(1234.5, -987.25, 800, 5, 77), AshNoise.Fbm(1234.5, -987.25, 800, 5, 78));
            double r = AshNoise.Ridged(500, 500, 700, 5, 9);
            Assert.That(r, Is.InRange(0.0, 1.0001));
            Assert.AreEqual(AshNoise.Derive(137377, "terrain"), AshNoise.Derive(137377, "terrain"));
        }

        [Test]
        public void SameSeedSameWorld_OtherSeedOtherDetails()
        {
            TerrainGenerationContext b = TerrainGeneratorSelfTest.RunTest(4242), c = TerrainGeneratorSelfTest.RunTest(4343);
            Assert.AreEqual(A.Field.Hash(), b.Field.Hash());
            Assert.AreNotEqual(A.Field.Hash(), c.Field.Hash());
        }

        [Test]
        public void MountainModifier_RaisesACrest() => Assert.Greater(A.Field.SampleBilinear(2700f, 1000f), 250f);

        [Test]
        public void ValleyModifier_CutsIntoTheRange() =>
            Assert.Less(A.Field.SampleBilinear(2700f, 2000f), A.Field.SampleBilinear(2700f, 1000f) - 150f);

        [Test]
        public void PassModifier_SaddleLowerThanRidge()
        {
            MacroHeightField f = A.Field;
            Assert.Less(f.SampleBilinear(2650f, 3050f), Mathf.Max(f.SampleBilinear(2650f, 3450f), f.SampleBilinear(2650f, 2700f)));
        }

        [Test]
        public void VeilScarModifier_DeepFloor() => Assert.Less(A.Field.SampleBilinear(800f, 380f), A.Field.SampleBilinear(800f, 1100f) - 60f);

        [Test]
        public void Hydrology_RiversRunDownhill()
        {
            ValidationReport r = WorldValidators.Rivers(A);
            Assert.IsEmpty(r.Warnings, string.Join("\n", r.Warnings));
            Assert.IsNotNull(A.Hydrology);
            Assert.Greater(A.Hydrology.Candidates.Count, 0);
        }

        [Test]
        public void TerrainSeam_EdgeTakesHandworkHeight()
        {
            var ps = new ProtectedHeightSampler();
            ps.Areas.Add(new ProtectedHeightSampler.Area { Id = "Test", Rect = new Rect(1000f, 1000f, 400f, 400f) });
            ProtectedAreaModifier.BlendAt(ps, 300f, 1000f, 1200f, 999f, out float atEdge);
            ProtectedAreaModifier.BlendAt(ps, 300f, 600f, 1200f, 999f, out float farAway);
            Assert.Greater(atEdge, 0.99f);
            Assert.LessOrEqual(farAway, 0f);
        }

        [Test]
        public void ProtectedRegion_KarethIsGuarded()
        {
            KarethGuard g = KarethGuard.Capture();
            Assert.IsTrue(g.Verify().Ok);
            Assert.That(KarethGuard.GuardedAssets.Any(p => p.Contains("Kareth_TerrainData")));
        }

        [Test]
        public void BorderMountainsFrame_RoundTrip()
        {
            var l = new Vector2(1234f, 567f);
            Vector2 back = BorderMountainsFrame.ToLocal(BorderMountainsFrame.ToWorld(l));
            Assert.Less(Vector2.Distance(l, back), 0.001f);
            Vector3 viaRoot = BorderMountainsFrame.RootPosition + BorderMountainsFrame.RootRotation * new Vector3(l.x, 0f, l.y);
            Vector2 direct = BorderMountainsFrame.ToWorld(l);
            Assert.Less(Vector2.Distance(new Vector2(viaRoot.x, viaRoot.z), direct), 0.01f);
        }
    }

    /// <summary>Menue: EditMode-Tests starten und Ergebnis nach Logs/Ashveil/TerrainEditModeTests.txt schreiben.</summary>
    public static class TerrainTestRunnerMenu
    {
        public const string ReportPath = "Logs/Ashveil/TerrainEditModeTests.txt";

        [MenuItem("Ashveil/Welt/Terrain-Generator: EditMode-Tests", priority = 544)]
        public static void Run()
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks());
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { "Ashveil.EditorTests" } }));
        }

        private sealed class Callbacks : ICallbacks
        {
            private readonly StringBuilder _sb = new StringBuilder();
            private int _pass, _fail;

            public void RunStarted(ITestAdaptor testsToRun) => _sb.AppendLine("== ASHVEIL Terrain-Generator EditMode-Tests " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            public void RunFinished(ITestResultAdaptor result)
            {
                _sb.AppendLine($"ERGEBNIS: {_pass} bestanden, {_fail} fehlgeschlagen");
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath, _sb.ToString());
                Debug.Log("[Ashveil] Terrain EditMode-Tests: " + _pass + "/" + (_pass + _fail));
            }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.HasChildren) return;
                bool ok = result.TestStatus == TestStatus.Passed;
                if (ok) _pass++;
                else _fail++;
                _sb.AppendLine($"{(ok ? "OK    " : "FEHLER")} {result.Test.Name} {(ok ? "" : result.Message)}");
            }
        }
    }
}
