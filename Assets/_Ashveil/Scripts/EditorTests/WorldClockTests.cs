using System;
using Ashveil.Core;
using Ashveil.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// Weltuhr in Echtzeit (NET-020, Welle 0 06.10.2026): Stunde aus der Serverzeit, Versatz und Startstunde, Tageswechsel,
    /// Sprung nur vorwärts, Neustart ohne Rücksprung, Tempo 1:1. Dazu: Die Sonnenbahn trifft um 15:30 das gestaltete Licht.
    /// </summary>
    public class WorldClockTests
    {
        private static readonly DateTime Abend = new DateTime(2026, 10, 6, 20, 15, 0);   // 6. Oktober = Tag 278 seit 1. Januar

        private double _hourAtRef, _refTime, _sph;
        private Func<double> _source;

        [SetUp]
        public void Merken()
        {
            _hourAtRef = WorldClock.HourAtRef;
            _refTime = WorldClock.RefTime;
            _sph = WorldClock.SecondsPerHour;
            _source = WorldClock.TimeSource;
        }

        [TearDown]
        public void Zuruecksetzen()
        {
            WorldClock.TimeSource = _source;
            WorldClock.Set(_hourAtRef, _refTime, _sph);
        }

        [Test]
        public void Echtzeit_folgt_der_Serverzeit()
        {
            double total = WorldTimeStart.Echtzeit.TotalHoursAt(Abend);
            Assert.AreEqual(20.25f, WorldClock.HourOfDay(total), 1e-4f);
            Assert.AreEqual(278, (int)Math.Floor(total / 24.0), "Tag zählt ab 1. Januar 2026");
            Assert.AreEqual(3600.0, WorldClock.DefaultSecondsPerHour, "1 Spielstunde = 1 echte Stunde");
        }

        [Test]
        public void Versatz_verschiebt_die_Uhr()
        {
            Assert.AreEqual(23.25f, WorldClock.HourOfDay(WorldTimeStart.Versatz(3.0).TotalHoursAt(Abend)), 1e-4f);
            Assert.AreEqual(17.75f, WorldClock.HourOfDay(WorldTimeStart.Versatz(-2.5).TotalHoursAt(Abend)), 1e-4f);
            // über Mitternacht: der Tag wechselt mit
            double spaet = WorldTimeStart.Versatz(5.0).TotalHoursAt(Abend);
            Assert.AreEqual(1.25f, WorldClock.HourOfDay(spaet), 1e-4f);
            Assert.AreEqual(279, (int)Math.Floor(spaet / 24.0));
        }

        [Test]
        public void Startstunde_heute_dann_Echtzeit()
        {
            double total = WorldTimeStart.Start(9.0).TotalHoursAt(Abend);
            Assert.AreEqual(9f, WorldClock.HourOfDay(total), 1e-4f);
            Assert.AreEqual(278, (int)Math.Floor(total / 24.0), "Startstunde am heutigen Tag (Mondphasen bleiben beim Datum)");
            double gestaltet = WorldTimeStart.Start(WorldClock.DefaultStartHour).TotalHoursAt(Abend);
            Assert.AreEqual(15.5f, WorldClock.HourOfDay(gestaltet), 1e-4f);
        }

        [Test]
        public void Startwerte_lesen()
        {
            Assert.IsTrue(WorldTimeStart.TryParse("09:00", out WorldTimeStart s) && s.Equals(WorldTimeStart.Start(9.0)));
            Assert.IsTrue(WorldTimeStart.TryParse("9", out s) && s.Equals(WorldTimeStart.Start(9.0)));
            Assert.IsTrue(WorldTimeStart.TryParse("vormittag", out s) && s.Equals(WorldTimeStart.Start(9.0)));
            Assert.IsTrue(WorldTimeStart.TryParse("start=9:30", out s) && s.Equals(WorldTimeStart.Start(9.5)));
            Assert.IsTrue(WorldTimeStart.TryParse("gestaltet", out s) && s.Equals(WorldTimeStart.Start(15.5)));
            Assert.IsTrue(WorldTimeStart.TryParse("15:30", out s) && s.Equals(WorldTimeStart.Start(15.5)));
            Assert.IsTrue(WorldTimeStart.TryParse("", out s) && s.Equals(WorldTimeStart.Echtzeit));
            Assert.IsTrue(WorldTimeStart.TryParse("Echtzeit", out s) && s.Equals(WorldTimeStart.Echtzeit));
            Assert.IsTrue(WorldTimeStart.TryParse("+3", out s) && s.Equals(WorldTimeStart.Versatz(3.0)));
            Assert.IsTrue(WorldTimeStart.TryParse("versatz=-2.5", out s) && s.Equals(WorldTimeStart.Versatz(-2.5)));
            Assert.IsTrue(WorldTimeStart.TryParse("versatz=-2:30", out s) && s.Equals(WorldTimeStart.Versatz(-2.5)));
            Assert.IsTrue(WorldTimeStart.TryParse("2,5h", out s) && s.Equals(WorldTimeStart.Versatz(2.5)));
            foreach (string falsch in new[] { "25", "abc", "versatz=", "9:75", "+", "start=", "h" })
                Assert.IsFalse(WorldTimeStart.TryParse(falsch, out _), "abgewiesen: " + falsch);
        }

        [Test]
        public void Neustart_springt_nicht_zurueck()
        {
            double vorher = WorldTimeStart.Echtzeit.TotalHoursAt(Abend);
            double gleich = WorldTimeStart.Echtzeit.TotalHoursAt(Abend);
            double spaeter = WorldTimeStart.Echtzeit.TotalHoursAt(Abend.AddMinutes(10));
            Assert.AreEqual(vorher, gleich, 1e-9, "gleiche Serverzeit = gleiche Weltzeit (nichts gespeichert)");
            Assert.AreEqual(vorher + 10.0 / 60.0, spaeter, 1e-9, "Neustart 10 Minuten später: Uhr 10 Minuten weiter");
        }

        [Test]
        public void Uhr_laeuft_eins_zu_eins_und_wechselt_den_Tag()
        {
            double netz = 1000.0;
            WorldClock.TimeSource = () => netz;
            WorldClock.Set(WorldTimeStart.Echtzeit.TotalHoursAt(new DateTime(2026, 10, 6, 23, 59, 0)), netz, WorldClock.DefaultSecondsPerHour);
            Assert.AreEqual("23:59", WorldClock.Text);
            int tag = WorldClock.Day;
            netz += 120.0;   // zwei echte Minuten
            Assert.AreEqual("00:01", WorldClock.Text);
            Assert.AreEqual(tag + 1, WorldClock.Day, "Tageswechsel um Mitternacht");
            netz += 3600.0;
            Assert.AreEqual("01:01", WorldClock.Text, "eine echte Stunde = eine Spielstunde");
        }

        [Test]
        public void Sprung_nur_vorwaerts()
        {
            double jetzt = WorldTimeStart.Echtzeit.TotalHoursAt(Abend);   // 20:15, Tag 278
            Assert.AreEqual(Math.Floor(jetzt / 24.0) * 24.0 + 23.0, WorldClock.JumpTarget(jetzt, 23f), 1e-6, "später am selben Tag");
            Assert.AreEqual(Math.Floor(jetzt / 24.0) * 24.0 + 24.0 + 12.5, WorldClock.JumpTarget(jetzt, 12.5f), 1e-6, "früher: nächster Tag");
            Assert.GreaterOrEqual(WorldClock.JumpTarget(jetzt, 20.25f), jetzt);
        }

        [Test]
        public void Sonnenbahn_trifft_um_1530_das_gestaltete_Licht()
        {
            Assert.AreEqual(DayNightCycle.DesignHour, (float)WorldClock.DefaultStartHour, 1e-6f);
            foreach (Vector2 licht in new[] { new Vector2(28f, 35f), new Vector2(34f, 35f), new Vector2(40f, 140f) })   // Stil A, Bau, Sandbox-artig
            {
                Quaternion design = Quaternion.Euler(licht.x, licht.y, 0f);
                DayNightCycle.DesignFromLight(design, out float yaw, out float scale);
                Vector3 zurSonne = DayNightCycle.SunDirection(DayNightCycle.DesignHour, yaw, scale);
                Assert.Less(Vector3.Angle(zurSonne, -(design * Vector3.forward)), 0.01f, $"Licht {licht}");
            }
        }
    }
}
