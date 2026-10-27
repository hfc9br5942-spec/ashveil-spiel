using Ashveil.Core.Diagnostics;
using NUnit.Framework;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// Ruckler-Messung v2 (CORE-005, 09.10.2026): Einordnung langer Bilder, Abgleich der Messpunkte mit der eigenen Stoppuhr und
    /// die Zähler je Abschnitt. Ohne Szene, ohne Play.
    /// </summary>
    public sealed class SpitzenAuswertungTests
    {
        [Test]
        public void Einordnen_nach_der_eigenen_Stoppuhr()
        {
            Assert.AreEqual(SpitzenAuswertung.Art.Spielschleife, SpitzenAuswertung.Einordnen(250, 240, 5, 5));
            Assert.AreEqual(SpitzenAuswertung.Art.Anzeige, SpitzenAuswertung.Einordnen(172, 7, 165, 0), "Grafikkarte hat gebremst");
            Assert.AreEqual(SpitzenAuswertung.Art.Ausserhalb, SpitzenAuswertung.Einordnen(400, 12, 0, 388), "Windows-Nachrichten");
            Assert.AreEqual(SpitzenAuswertung.Art.Gemischt, SpitzenAuswertung.Einordnen(200, 100, 0, 100));
            Assert.AreEqual(SpitzenAuswertung.Art.Gemischt, SpitzenAuswertung.Einordnen(0, 0, 0, 0));
        }

        [Test]
        public void Rauschen_wird_ausgefiltert()
        {
            Assert.IsTrue(SpitzenAuswertung.Rauschen("EndJob", 5, 60), "Auftragsverwaltung");
            Assert.IsTrue(SpitzenAuswertung.Rauschen("Idle", 30, 60), "Leerlauf der Hilfsfäden");
            Assert.IsTrue(SpitzenAuswertung.Rauschen("Object.Unregister", 2308974594, 57), "kein Zeitwert");
            Assert.IsFalse(SpitzenAuswertung.Rauschen("DXGI.WaitOnSwapChain", 54, 60));
            Assert.IsFalse(SpitzenAuswertung.Rauschen("Update.ScriptRunBehaviourUpdate", 49, 57));
        }

        [Test]
        public void Abgleich_waehlt_den_passenden_Schnappschuss()
        {
            Assert.AreEqual(0, SpitzenAuswertung.Abgleich(240, 245, 13), "A passt");
            Assert.AreEqual(1, SpitzenAuswertung.Abgleich(240, 13, 238), "B passt");
            Assert.AreEqual(-1, SpitzenAuswertung.Abgleich(240, 13, 20), "keiner passt");
            Assert.AreEqual(0, SpitzenAuswertung.Abgleich(12, 15, 0), "kleine Zeiten: 5 ms Spielraum");
            Assert.AreEqual(-1, SpitzenAuswertung.Abgleich(240, 0, 0), "ohne Messpunkt kein Abgleich");
        }

        [Test]
        public void Zaehler_je_Abschnitt_und_Sitzung()
        {
            var s = new SpitzenStatistik("Start");
            s.Bild(16);
            s.Bild(40);
            s.Bild(120, neueZustaende: 37, neueVarianten: 5);
            SpitzenStatistik.Teil start = s.Aktuell;
            s.Abschnitt("Blickpunkt 1");
            s.Bild(60);
            s.Bild(16);
            s.Haenger(start, SpitzenAuswertung.Art.Spielschleife, true, false);

            Assert.AreEqual(5, s.Gesamt.Bilder);
            Assert.AreEqual(3, s.Gesamt.Ab33);
            Assert.AreEqual(2, s.Gesamt.Ab50);
            Assert.AreEqual(1, s.Gesamt.Ab100);
            Assert.AreEqual(120, s.Gesamt.LaengstesMs, 1e-9);
            Assert.AreEqual(37, s.Gesamt.NeueZustaende);
            Assert.AreEqual(1, start.Ab100);
            Assert.AreEqual(1, start.Schleife, "der Hänger zählt im Abschnitt, in dem er begann");
            Assert.AreEqual(1, start.MitZustaenden);
            Assert.AreEqual(0, s.Aktuell.Ab100);
            Assert.AreEqual(1, s.Aktuell.Ab50);
            Assert.AreEqual(1, s.Gesamt.Schleife);
            Assert.AreEqual(2, s.Teile.Count);
        }

        [Test]
        public void Ursachen_nach_Summe_und_Zusammenfassung_lesbar()
        {
            var s = new SpitzenStatistik();
            s.Ursache("Update.ScriptRunBehaviourUpdate", 140);
            s.Ursache("Shader.CreateGPUProgram", 80);
            s.Ursache("Update.ScriptRunBehaviourUpdate", 60);
            s.Ursache("", 500);
            var u = s.Ursachen(5);
            Assert.AreEqual(2, u.Count);
            Assert.AreEqual("Update.ScriptRunBehaviourUpdate", u[0].Key);
            Assert.AreEqual(200, u[0].Value, 1e-9);
            Assert.AreEqual(2, s.UrsacheAnzahl("Update.ScriptRunBehaviourUpdate"));

            s.Bild(120);
            string text = s.Text(true);
            StringAssert.Contains("  Hänger: ≥ 33 ms 1 · ≥ 50 ms 1 · ≥ 100 ms 1 · längster 120 ms", text, "der Auftrag liest diese Zeile");
            StringAssert.Contains("  Davon ≥ 50 ms: ", text);
            StringAssert.Contains("  Neue Grafik-Zustände in der Sitzung: ", text);
        }
    }
}
