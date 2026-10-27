using Ashveil.EditorTools;
using NUnit.Framework;
using UnityEngine;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// P2.5 Kareth-Relief: Schutzbereiche und Raender bleiben exakt, Wege bleiben flach genug, Ergebnis deterministisch.
    /// Laeuft mit "Ashveil/Welt/Terrain-Generator: EditMode-Tests" (gleiche Test-Assembly).
    /// </summary>
    public sealed class KarethReliefTests
    {
        [Test]
        public void Relief_EdgesUnchanged()
        {
            KarethRelief.Refresh();
            for (float t = 0f; t <= 1f; t += 0.01f)
            {
                float x = Mathf.Lerp(KarethTerrainBuilder.MinX, KarethTerrainBuilder.MaxX, t);
                float z = Mathf.Lerp(KarethTerrainBuilder.MinZ, KarethTerrainBuilder.MaxZ, t);
                Assert.AreEqual(0f, KarethRelief.Exact(x, KarethTerrainBuilder.MinZ), 1e-4f);
                Assert.AreEqual(0f, KarethRelief.Exact(x, KarethTerrainBuilder.MaxZ), 1e-4f);
                Assert.AreEqual(0f, KarethRelief.Exact(KarethTerrainBuilder.MinX, z), 1e-4f);
                Assert.AreEqual(0f, KarethRelief.Exact(KarethTerrainBuilder.MaxX, z), 1e-4f);
            }
        }

        [Test]
        public void Relief_ProtectedPlacesUnchanged()
        {
            // Wachtfeuer, Halle, Moorwasser, Uhrwerk-Wand, Kolosseum, Tunnel, Salzgruben, Kareth-Test-Messpunkt Plateau
            var pts = new[] { new Vector2(0f, 0f), new Vector2(0f, -64f), new Vector2(40f, 330f), new Vector2(585f, 100f), new Vector2(490f, 285f),
                              new Vector2(500f, 60f), new Vector2(-290f, -195f), new Vector2(0f, 690f), new Vector2(60f, 598f) };
            foreach (Vector2 p in pts) Assert.AreEqual(0f, KarethRelief.Exact(p.x, p.y), 0.01f, $"Relief bei {p}");
        }

        [Test]
        public void Relief_RoadsStayGentle()
        {
            foreach (Vector2[] line in KarethRelief.Paths)
                for (int i = 0; i + 1 < line.Length; i++)
                {
                    float len = Vector2.Distance(line[i], line[i + 1]);
                    int n = Mathf.Max(1, Mathf.CeilToInt(len / 4f));
                    for (int k = 0; k < n; k++)
                    {
                        Vector2 a = Vector2.Lerp(line[i], line[i + 1], k / (float)n), b = Vector2.Lerp(line[i], line[i + 1], (k + 1) / (float)n);
                        float g = Mathf.Abs(KarethRelief.Exact(b.x, b.y) - KarethRelief.Exact(a.x, a.y)) / Vector2.Distance(a, b);
                        Assert.LessOrEqual(g, 0.10f, $"Relief-Gefaelle {g:P0} bei {a}");
                    }
                }
        }

        [Test]
        public void Relief_IsDeterministicAndVisible()
        {
            float a = KarethRelief.Exact(330f, 470f);
            KarethRelief.Refresh();
            Assert.AreEqual(a, KarethRelief.Exact(330f, 470f));
            Assert.Greater(KarethRelief.Raw(335f, 472f), 10f, "Brachruecken fehlt");
        }
    }
}
