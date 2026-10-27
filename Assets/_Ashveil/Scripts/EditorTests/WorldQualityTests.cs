using System.Collections.Generic;
using System.Linq;
using Ashveil.EditorTools.Quality;
using NUnit.Framework;
using UnityEngine;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// World Quality Validator V1 (Werkzeug-Direktive 05.10.): die reinen Rechenregeln ohne Szene – Steigung, Schweregrad,
    /// Schweben/Vergraben, NavMesh-Inseln, offene Wasserränder, Raster, Linienabstand, Neuabtastung, Namens-Stichworte
    /// und der Bericht. Die Szene-Prüfungen selbst laufen über das Menü Ashveil › Welt › Weltqualität prüfen.
    /// </summary>
    public sealed class WorldQualityTests
    {
        [Test]
        public void GradePercent_IsAbsoluteRisePerRun()
        {
            Assert.AreEqual(10f, WorldQualityRules.GradePercent(0f, 1f, 10f), 1e-4f);
            Assert.AreEqual(10f, WorldQualityRules.GradePercent(1f, 0f, 10f), 1e-4f, "bergab zählt gleich");
            Assert.AreEqual(0f, WorldQualityRules.GradePercent(0f, 5f, 0f), "ohne Abstand keine Steigung");
        }

        [Test]
        public void WorstGrade_FindsSteepestWindow()
        {
            // 2-m-Schritte: flach, dann 4 m Anstieg auf 10 m (40 %), dann wieder flach
            var h = new List<float> { 0f, 0f, 0f, 0f, 0f, 0.8f, 1.6f, 2.4f, 3.2f, 4.0f, 4.0f, 4.0f, 4.0f };
            float worst = WorldQualityRules.WorstGrade(h, 2f, 10f, out int at);
            Assert.AreEqual(40f, worst, 1e-3f);
            Assert.AreEqual(4, at);

            // Profil kürzer als das Fenster: ganze Länge
            float shortGrade = WorldQualityRules.WorstGrade(new List<float> { 0f, 1f, 2f }, 2f, 10f, out int at2);
            Assert.AreEqual(50f, shortGrade, 1e-3f);
            Assert.AreEqual(0, at2);
            Assert.AreEqual(0f, WorldQualityRules.WorstGrade(new List<float> { 3f }, 2f, 10f, out _));
        }

        [Test]
        public void Classify_UsesOnlyMeasuredLimits()
        {
            Assert.AreEqual(WqSeverity.Info, WorldQualityRules.Classify(5999, 6000, 9000));
            Assert.AreEqual(WqSeverity.Warning, WorldQualityRules.Classify(6000, 6000, 9000));
            Assert.AreEqual(WqSeverity.Critical, WorldQualityRules.Classify(9089, 6000, 9000), "Kareth v4 am 05.10.");
            Assert.AreEqual(WqSeverity.Info, WorldQualityRules.Classify(1e9, 0, 0), "Grenze 0 = nicht gemessen → nur INFO");
            Assert.AreEqual(WqSeverity.Critical, WorldQualityRules.Classify(5, 0, 4), "nur kritische Grenze gesetzt");
        }

        [Test]
        public void Budgets_AreOrderedAndSourced()
        {
            Assert.Less(WorldQualityBudgets.RenderersWarning, WorldQualityBudgets.RenderersCritical);
            Assert.Less(WorldQualityBudgets.TextureSizeWarning, WorldQualityBudgets.TextureSizeCritical);
            Assert.Less(WorldQualityBudgets.MeshTrianglesWithoutLodWarning, WorldQualityBudgets.MeshTrianglesWithoutLodCritical);
            Assert.IsFalse(string.IsNullOrEmpty(WorldQualityBudgets.RenderersSource), "jede Grenze nennt ihre Messung");
            Assert.IsFalse(string.IsNullOrEmpty(WorldQualityBudgets.TrianglesSource));
            Assert.AreEqual(9f, WorldQualityBudgets.GradeLimit("haupt"));
            Assert.AreEqual(12f, WorldQualityBudgets.GradeLimit("neben"));
            Assert.AreEqual(18f, WorldQualityBudgets.GradeLimit("pfad"));
            Assert.AreEqual(18f, WorldQualityBudgets.GradeLimit("unbekannt"));
        }

        [Test]
        public void FloatingAndBuried()
        {
            Assert.IsTrue(WorldQualityRules.IsFloating(10.5f, 10f, false, 0.3f), "0,5 m über dem höchsten Bodenpunkt, nichts trägt");
            Assert.IsFalse(WorldQualityRules.IsFloating(10.5f, 10f, true, 0.3f), "getragen (Tisch, Mauer, Balken)");
            Assert.IsFalse(WorldQualityRules.IsFloating(10.2f, 10f, false, 0.3f), "innerhalb der Toleranz");
            Assert.IsTrue(WorldQualityRules.IsBuried(9.9f, 10f));
            Assert.IsFalse(WorldQualityRules.IsBuried(10.5f, 10f), "ragt heraus");
        }

        [Test]
        public void NavIslands_WeldsSharedEdgesAndSeparatesGaps()
        {
            // Zwei Quadrate 10 × 10 m Kante an Kante (eigene Eckpunkte an gleicher Stelle) und ein 2 × 2 m großes weit weg
            var v = new List<Vector3>();
            var idx = new List<int>();
            void Quad(float x, float z, float s)
            {
                int b = v.Count;
                v.Add(new Vector3(x, 0f, z));
                v.Add(new Vector3(x + s, 0f, z));
                v.Add(new Vector3(x + s, 0f, z + s));
                v.Add(new Vector3(x, 0f, z + s));
                idx.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            Quad(0f, 0f, 10f);
            Quad(10f, 0f, 10f);
            Quad(100f, 100f, 2f);
            List<WorldQualityRules.NavIsland> islands = WorldQualityRules.NavIslands(v.ToArray(), idx.ToArray(), 0.05f, out int[] triIsland);
            Assert.AreEqual(2, islands.Count);
            Assert.AreEqual(200f, islands[0].Area, 1e-3f, "größte Fläche zuerst");
            Assert.AreEqual(4, islands[0].Triangles);
            Assert.AreEqual(4f, islands[1].Area, 1e-3f);
            Assert.AreEqual(6, triIsland.Length);
            Assert.AreEqual(0, triIsland[0]);
            Assert.AreEqual(0, triIsland[3]);
            Assert.AreEqual(1, triIsland[5]);
            Assert.AreEqual(0, WorldQualityRules.NavIslands(new Vector3[0], new int[0], 0.05f, out _).Count);
        }

        [Test]
        public void OpenWaterEdges_FindsLowShore()
        {
            // Becken 100 × 100 m, Spiegel auf 0; Ufer überall 1 m hoch – außer an der Ostseite zwischen z = 40 und 70 (Gelände −0,5 m)
            var area = new Rect(0f, 0f, 100f, 100f);
            float? Ground(float x, float z) => x > 99f && z >= 40f && z <= 70f ? -0.5f : 1f;
            List<WorldQualityRules.OpenEdge> open = WorldQualityRules.OpenWaterEdges(area, 0f, Ground, 2f, 8f);
            Assert.AreEqual(1, open.Count);
            Assert.AreEqual(30f, open[0].Length, 2.5f);
            Assert.AreEqual(0.5f, open[0].Deepest, 1e-4f);
            Assert.AreEqual(100f, open[0].From.x, 1e-3f);

            // Kurze Lücken (unter 8 m) und Stellen ohne Gelände zählen nicht
            float? Notch(float x, float z) => x > 99f && z >= 50f && z <= 54f ? -0.5f : 1f;
            Assert.AreEqual(0, WorldQualityRules.OpenWaterEdges(area, 0f, Notch, 2f, 8f).Count);
            Assert.AreEqual(0, WorldQualityRules.OpenWaterEdges(area, 0f, (x, z) => null, 2f, 8f).Count);
        }

        [Test]
        public void TileKey_FloorsNegativeCoordinates()
        {
            Assert.AreEqual(new Vector2Int(0, 0), WorldQualityRules.TileKey(new Vector3(10f, 5f, 255.9f), 256f));
            Assert.AreEqual(new Vector2Int(-1, 1), WorldQualityRules.TileKey(new Vector3(-0.1f, 0f, 256f), 256f));
            Assert.AreEqual(new Vector2Int(-2, -1), WorldQualityRules.TileKey(new Vector3(-257f, 0f, -1f), 256f));
        }

        [Test]
        public void DistanceToPolylineAndSegment()
        {
            var line = new List<Vector2> { new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(10f, 10f) };
            Assert.AreEqual(3f, WorldQualityRules.DistanceToPolyline(new Vector2(5f, 3f), line), 1e-4f);
            Assert.AreEqual(2f, WorldQualityRules.DistanceToPolyline(new Vector2(12f, 5f), line), 1e-4f);
            Assert.AreEqual(5f, WorldQualityRules.DistanceToSegment(new Vector2(-3f, 4f), Vector2.zero, new Vector2(10f, 0f)), 1e-4f, "vor dem Anfang: Abstand zum Endpunkt");
            Assert.AreEqual(float.MaxValue, WorldQualityRules.DistanceToPolyline(Vector2.zero, new List<Vector2>()));
        }

        [Test]
        public void Resample_KeepsFixedSpacingAcrossCorners()
        {
            var line = new List<Vector2> { new Vector2(0f, 0f), new Vector2(3f, 0f), new Vector2(3f, 4f) };   // 7 m lang
            List<Vector2> pts = WorldQualityRules.Resample(line, 2f);
            Assert.AreEqual(4, pts.Count, "0, 2, 4, 6 m – der Rest von 1 m entfällt");
            Assert.AreEqual(Vector2.zero, pts[0]);
            Assert.AreEqual(2f, pts[1].x, 1e-4f);
            Assert.AreEqual(3f, pts[2].x, 1e-4f);
            Assert.AreEqual(1f, pts[2].y, 1e-4f, "4 m = 3 m bis zur Ecke und 1 m weiter");
            Assert.AreEqual(3f, pts[3].y, 1e-4f);
            Assert.AreEqual(0, WorldQualityRules.Resample(null, 2f).Count);
        }

        [Test]
        public void NameKeywords_MatchWordsAndCompoundEndings()
        {
            string[] keys = { "steg", "licht", "dach" };
            string[] places = { "untersteg" };
            Assert.IsTrue(WorldQualityRules.NameHasKeyword("Holzsteg_03", keys, places));
            Assert.IsTrue(WorldQualityRules.NameHasKeyword("Vordach (1)", keys, places));
            Assert.IsTrue(WorldQualityRules.NameHasKeyword("Steg", keys, places));
            Assert.IsFalse(WorldQualityRules.NameHasKeyword("Untersteg", keys, places), "Ortsname, kein Steg");
            Assert.IsFalse(WorldQualityRules.NameHasKeyword("Lichtung Ost", keys, places), "Stichwort nur am Wortende");
            Assert.IsFalse(WorldQualityRules.NameHasKeyword("Dachsbau", keys, places));
            Assert.IsFalse(WorldQualityRules.NameHasKeyword(null, keys, places));
            CollectionAssert.AreEqual(new[] { "haus", "dach" }, WorldQualityRules.NameWords("Haus_03/Dach"));
        }

        [Test]
        public void MeshBytesEstimate_CountsVerticesAndIndices()
        {
            Assert.AreEqual(1000L * 32 + 3000L * 2, WorldQualityRules.MeshBytesEstimate(1000, 32, 3000, false));
            Assert.AreEqual(1000L * 12 + 3000L * 4, WorldQualityRules.MeshBytesEstimate(1000, 4, 3000, true), "mindestens 12 Byte je Eckpunkt (Position)");
        }

        [Test]
        public void Report_CountsCapsAndSerializes()
        {
            var r = new WqReport { scene = "Test" };
            for (int i = 0; i < WqReport.MaxPerCode + 5; i++)
                r.Add(WqSeverity.Warning, "Platzierung", "PROP_FLOATING", "schwebt " + i, new Vector3(i, 0f, 0f));
            r.Add(WqSeverity.Critical, "Kollision", "MESHCOLLIDER_EMPTY", "leer");
            r.Add(WqSeverity.Info, "Budget", "HOTSPOT", "Kachel");
            r.Finish();

            Assert.AreEqual(1, r.critical);
            Assert.AreEqual(WqReport.MaxPerCode + 5, r.warning, "auch nicht einzeln aufgeführte Befunde zählen");
            Assert.AreEqual(1, r.info);
            Assert.AreEqual(WqReport.MaxPerCode + 5, r.CountOf("PROP_FLOATING"));
            Assert.AreEqual(WqReport.MaxPerCode + 1, r.findings.Count(f => f.code == "PROP_FLOATING"), "25 Einzelzeilen und eine Sammelzeile");
            Assert.AreEqual(WqReport.MaxPerCode + 3, r.findings.Count);

            WqReport back = JsonUtility.FromJson<WqReport>(JsonUtility.ToJson(r));
            Assert.AreEqual(r.findings.Count, back.findings.Count);
            Assert.AreEqual("CRITICAL", back.findings.First(f => f.code == "MESHCOLLIDER_EMPTY").severity);
            Assert.IsTrue(back.findings[0].hasPosition);

            string text = WorldQualityValidator.ToText(r);
            StringAssert.Contains("1 CRITICAL", text);
            StringAssert.Contains("weitere Befunde", text);
        }
    }
}
