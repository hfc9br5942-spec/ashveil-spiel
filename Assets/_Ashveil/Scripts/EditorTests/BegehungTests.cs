using System.Collections.Generic;
using Ashveil.World;
using NUnit.Framework;
using UnityEngine;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// #29 Begehung (07.10.2026): Rechnung der Wegstrecke, mit der die Begehungs-Suite den Charaktermotor die Wege entlangführt –
    /// Bogenlänge, nächster Punkt, Suchfenster in Kehren, steilste Stelle über 10 m.
    /// </summary>
    public class BegehungTests
    {
        [Test]
        public void Gerade_BogenlaengeUndSeitenabstand()
        {
            var w = new Wegstrecke(new[] { new Vector2(0f, 0f), new Vector2(0f, 50f), new Vector2(0f, 100f) });
            Assert.AreEqual(100f, w.Length, 1e-3f);
            float s = w.Project(new Vector2(3f, 40f), out float lateral);
            Assert.AreEqual(40f, s, 1e-3f);
            Assert.AreEqual(3f, lateral, 1e-3f);
            Vector2 p = w.PointAt(75f);
            Assert.AreEqual(0f, p.x, 1e-4f);
            Assert.AreEqual(75f, p.y, 1e-4f);
            Assert.AreEqual(1f, w.DirectionAt(10f).y, 1e-4f);
        }

        [Test]
        public void DoppeltePunkteWerdenUebergangen()
        {
            var w = new Wegstrecke(new[] { new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(10f, 0f) });
            Assert.AreEqual(2, w.Count);
            Assert.AreEqual(10f, w.Length, 1e-4f);
            Assert.Throws<System.ArgumentException>(() => new Wegstrecke(new[] { new Vector2(1f, 1f), new Vector2(1f, 1f) }));
        }

        [Test]
        public void Kehre_SuchfensterSpringtNichtAufDenAnderenSchenkel()
        {
            // Haarnadel: 50 m nach Norden, 6 m nach Osten, 50 m zurück nach Süden – die Schenkel liegen 6 m auseinander
            var w = new Wegstrecke(new[] { new Vector2(0f, 0f), new Vector2(0f, 50f), new Vector2(6f, 50f), new Vector2(6f, 0f) });
            var p = new Vector2(3.5f, 20f);   // näher am zweiten Schenkel
            float sAll = w.Project(p, out _);
            Assert.Greater(sAll, 56f, "ohne Fenster: zweiter Schenkel");
            float sWin = w.Project(p, 10f, 40f, out float lat);
            Assert.AreEqual(20f, sWin, 1e-3f, "im Fenster bleibt der Läufer auf dem ersten Schenkel");
            Assert.AreEqual(3.5f, lat, 1e-3f);
        }

        [Test]
        public void Fenster_RandWirdEingehalten()
        {
            var w = new Wegstrecke(new[] { new Vector2(0f, 0f), new Vector2(100f, 0f) });
            float s = w.Project(new Vector2(80f, 2f), 0f, 30f, out float lat);
            Assert.AreEqual(30f, s, 1e-3f);
            Assert.AreEqual(Mathf.Sqrt(50f * 50f + 4f), lat, 1e-2f);
            Assert.AreEqual(0, w.SegmentAt(-5f));
            Assert.AreEqual(0, w.SegmentAt(500f));
        }

        [Test]
        public void SteilsteStelle_UeberZehnMeter()
        {
            var s = new List<float>();
            var y = new List<float>();
            for (int i = 0; i <= 60; i++)
            {
                s.Add(i);
                // 0–20 m eben, 20–30 m Rampe mit 3 m Anstieg (30 %), danach eben
                y.Add(i < 20 ? 0f : i < 30 ? (i - 20) * 0.3f : 3f);
            }
            float g = Wegstrecke.WorstGrade(s, y, 10f, out int at);
            Assert.AreEqual(0.3f, g, 1e-3f);
            Assert.AreEqual(20, at);
            Assert.AreEqual(0f, Wegstrecke.WorstGrade(new List<float> { 0f, 5f }, new List<float> { 0f, 1f }, 10f, out int none), 1e-6f);
            Assert.AreEqual(-1, none);
        }
    }
}
