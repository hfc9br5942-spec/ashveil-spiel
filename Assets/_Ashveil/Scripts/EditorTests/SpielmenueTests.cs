using Ashveil.Gameplay.Controls;
using NUnit.Framework;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// UI-204 (Spieltest r2, Max 09.10.2026): Esc-Regel mit dem Fensterstapel und die Logik des Namensfelds
    /// (Vorschlag ersetzen, nur Buchstaben, Rücktaste halten, Einfügen). Ohne Szene, ohne Play.
    /// </summary>
    public sealed class SpielmenueTests
    {
        /// <summary>Ein Testfenster: schließt sofort oder erst nach einigen Takten (wie ein Gespräch, das der Server beendet).</summary>
        private sealed class Fenster
        {
            public readonly string Name;
            public bool Offen;
            public int SchliesstNach = -1;
            public int Verzoegerung;

            public Fenster(string name, int verzoegerung = 0)
            {
                Name = name;
                Verzoegerung = verzoegerung;
            }

            public void Anmelden() => UiFensterstapel.Anmelden(this, Name, () => Offen, Schliessen);

            private void Schliessen()
            {
                if (Verzoegerung <= 0) Offen = false;
                else SchliesstNach = Verzoegerung;
            }

            /// <summary>Ein Takt vergeht (verzögertes Schließen läuft ab).</summary>
            public void Weiter()
            {
                if (SchliesstNach < 0) return;
                if (--SchliesstNach <= 0)
                {
                    Offen = false;
                    SchliesstNach = -1;
                }
            }
        }

        private int _bild;
        private int _menue;

        [SetUp]
        public void Vorher()
        {
            UiFensterstapel.Zuruecksetzen();
            UiFensterstapel.MenueOeffnen = () => _menue++;
            _bild = 1000;
            _menue = 0;
        }

        [TearDown]
        public void Nachher() => UiFensterstapel.Zuruecksetzen();

        private EscWirkung Takt(bool esc, bool fokus = false) => UiFensterstapel.Verarbeiten(esc, fokus, ++_bild);

        // ---------------------------------------------------------------- Esc-Regel

        [Test]
        public void Regel_Fenster_dann_Ziel_dann_Menue()
        {
            Assert.AreEqual(EscWirkung.Nichts, UiFensterstapel.Entscheiden(true, true, true), "Eingabefeld hat den Fokus");
            Assert.AreEqual(EscWirkung.FensterSchliessen, UiFensterstapel.Entscheiden(false, true, true));
            Assert.AreEqual(EscWirkung.ZielAufheben, UiFensterstapel.Entscheiden(false, false, true));
            Assert.AreEqual(EscWirkung.MenueOeffnen, UiFensterstapel.Entscheiden(false, false, false));
        }

        [Test]
        public void Esc_schliesst_das_zuletzt_geoeffnete_Fenster_zuerst()
        {
            var rucksack = new Fenster("Rucksack");
            var buch = new Fenster("Fähigkeiten");
            rucksack.Anmelden();
            buch.Anmelden();

            buch.Offen = true;
            Takt(false);
            rucksack.Offen = true;   // später geöffnet, obwohl früher angemeldet
            Takt(false);
            Assert.AreEqual("Rucksack", UiFensterstapel.Oberstes);

            Assert.AreEqual(EscWirkung.FensterSchliessen, Takt(true));
            Assert.AreEqual("Rucksack", UiFensterstapel.LetztesFenster);
            Assert.IsFalse(rucksack.Offen);
            Assert.IsTrue(buch.Offen, "nur ein Fenster je Druck");

            Assert.AreEqual(EscWirkung.FensterSchliessen, Takt(true));
            Assert.AreEqual("Fähigkeiten", UiFensterstapel.LetztesFenster);
            Assert.IsFalse(buch.Offen);
            Assert.AreEqual(0, _menue, "das Menü bleibt zu, solange Fenster offen waren");
        }

        [Test]
        public void Ohne_Fenster_erst_Ziel_dann_Menue()
        {
            UiFensterstapel.HatZiel = true;
            Assert.AreEqual(EscWirkung.ZielAufheben, Takt(true));
            Assert.AreEqual(0, _menue);
            UiFensterstapel.HatZiel = false;
            Assert.AreEqual(EscWirkung.MenueOeffnen, Takt(true));
            Assert.AreEqual(1, _menue);
        }

        [Test]
        public void Das_Menue_selbst_ist_ein_Fenster()
        {
            var menue = new Fenster("Spielmenü");
            menue.Anmelden();
            UiFensterstapel.MenueOeffnen = () => menue.Offen = true;
            Assert.AreEqual(EscWirkung.MenueOeffnen, Takt(true));
            Takt(false);
            var einstellungen = new Fenster("Einstellungen");
            einstellungen.Anmelden();
            einstellungen.Offen = true;   // aus dem Menü geöffnet
            Takt(false);
            Assert.AreEqual(EscWirkung.FensterSchliessen, Takt(true));
            Assert.AreEqual("Einstellungen", UiFensterstapel.LetztesFenster, "zurück ins Menü");
            Assert.IsTrue(menue.Offen);
            Assert.AreEqual(EscWirkung.FensterSchliessen, Takt(true));
            Assert.IsFalse(menue.Offen);
        }

        [Test]
        public void Gleichzeitig_geoeffnet_liegt_das_spaeter_angemeldete_oben()
        {
            var rucksack = new Fenster("Rucksack");
            var beute = new Fenster("Beute");
            rucksack.Anmelden();
            beute.Anmelden();
            rucksack.Offen = beute.Offen = true;   // Beute öffnet den Rucksack mit
            Takt(false);
            Assert.AreEqual("Beute", UiFensterstapel.Oberstes);
            Takt(true);
            Assert.IsFalse(beute.Offen);
            Assert.IsTrue(rucksack.Offen);
        }

        [Test]
        public void Eingabefeld_mit_Fokus_behaelt_Esc()
        {
            var karte = new Fenster("Karte");
            karte.Anmelden();
            karte.Offen = true;
            Takt(false);
            Assert.AreEqual(EscWirkung.Nichts, Takt(true, fokus: true));
            Assert.IsTrue(karte.Offen, "Chat oder Tastenbelegung regeln Esc selbst");
            Assert.AreEqual(0, _menue);
        }

        [Test]
        public void Verzoegert_schliessendes_Fenster_wird_nicht_zweimal_geschlossen()
        {
            var buch = new Fenster("Auftragsbuch");
            var gespraech = new Fenster("Gespräch", verzoegerung: 3);   // der Server beendet das Gespräch erst später
            buch.Anmelden();
            gespraech.Anmelden();
            buch.Offen = true;
            Takt(false);
            gespraech.Offen = true;
            Takt(false);

            Assert.AreEqual(EscWirkung.FensterSchliessen, Takt(true));
            Assert.AreEqual("Gespräch", UiFensterstapel.LetztesFenster);
            Assert.IsTrue(gespraech.Offen, "schließt erst in einigen Takten");
            gespraech.Weiter();

            Assert.AreEqual(EscWirkung.FensterSchliessen, Takt(true));
            Assert.AreEqual("Auftragsbuch", UiFensterstapel.LetztesFenster, "der zweite Druck trifft das nächste Fenster");
            gespraech.Weiter();
            gespraech.Weiter();
            Takt(false);
            Assert.IsFalse(gespraech.Offen);
            Assert.AreEqual(0, UiFensterstapel.OffeneFenster);
        }

        [Test]
        public void Wieder_geoeffnetes_Fenster_kommt_nach_oben()
        {
            var a = new Fenster("A");
            var b = new Fenster("B");
            a.Anmelden();
            b.Anmelden();
            a.Offen = true;
            Takt(false);
            b.Offen = true;
            Takt(false);
            a.Offen = false;   // mit dem Knopf geschlossen
            Takt(false);
            a.Offen = true;
            Takt(false);
            Assert.AreEqual("A", UiFensterstapel.Oberstes);
        }

        [Test]
        public void Abgemeldete_Fenster_zaehlen_nicht()
        {
            var a = new Fenster("A");
            a.Anmelden();
            a.Offen = true;
            Takt(false);
            UiFensterstapel.Abmelden(a);
            Assert.AreEqual(EscWirkung.MenueOeffnen, Takt(true));
            Assert.IsTrue(a.Offen);
        }

        // ---------------------------------------------------------------- Namensfeld

        private static TextEingabe Namensfeld() => new TextEingabe(Ashveil.Data.CharacterNameRules.MaxLength, char.IsLetter);

        [Test]
        public void Vorschlag_wird_beim_ersten_Tastendruck_ersetzt()
        {
            TextEingabe n = Namensfeld();
            n.Setzen("Norbert", alsVorschlag: true);
            Assert.IsTrue(n.Markiert);
            n.Tippen("M");
            Assert.AreEqual("M", n.Text);
            Assert.IsFalse(n.Markiert);
            n.Tippen("ax");
            Assert.AreEqual("Max", n.Text);
        }

        [Test]
        public void Nur_Buchstaben_und_hoechstens_sechzehn()
        {
            TextEingabe n = Namensfeld();
            n.Tippen("Ä1 b-ß\t");
            Assert.AreEqual("Äbß", n.Text, "Umlaute und ß sind Buchstaben, Ziffern und Zeichen nicht");
            n.Tippen("abcdefghijklmnopqrstuvwxyz");
            Assert.AreEqual(16, n.Laenge);
        }

        [Test]
        public void Ruecktaste_loescht_einen_markierten_Vorschlag_ganz()
        {
            TextEingabe n = Namensfeld();
            n.Setzen("Elwen", alsVorschlag: true);
            Assert.IsTrue(n.Loeschen());
            Assert.AreEqual("", n.Text);
            n.Tippen("Kai");
            n.Loeschen();
            Assert.AreEqual("Ka", n.Text);
            n.Leeren();
            Assert.IsFalse(n.Loeschen(), "leer bleibt leer");
        }

        [Test]
        public void Einfuegen_filtert_und_ersetzt_Markiertes()
        {
            TextEingabe n = Namensfeld();
            n.Tippen("Alt");
            n.AllesMarkieren();
            n.Einfuegen(" Brigida 42\r\n");
            Assert.AreEqual("Brigida", n.Text);
            Assert.IsFalse(n.Markiert);
        }

        [Test]
        public void Ziffern_ersetzen_keinen_Vorschlag()
        {
            TextEingabe n = Namensfeld();
            n.Setzen("Elwen", alsVorschlag: true);
            Assert.IsFalse(n.Tippen("7"));
            Assert.AreEqual("Elwen", n.Text);
            Assert.IsTrue(n.Markiert);
        }

        [Test]
        public void Ruecktaste_wiederholt_sich_beim_Halten()
        {
            var w = new TastenWiederholung(0.45f, 0.05f);
            Assert.AreEqual(1, w.Schritte(true, true, 10f), "Druck wirkt sofort");
            Assert.AreEqual(0, w.Schritte(false, true, 10.2f), "erst nach der Verzögerung");
            Assert.AreEqual(1, w.Schritte(false, true, 10.46f));
            Assert.AreEqual(0, w.Schritte(false, true, 10.48f));
            Assert.AreEqual(1, w.Schritte(false, true, 10.52f));
            Assert.AreEqual(1, w.Schritte(false, true, 11.5f), "nach einem Hänger höchstens ein Schritt");
            Assert.AreEqual(0, w.Schritte(false, true, 11.52f), "danach wieder im Takt");
            Assert.AreEqual(0, w.Schritte(false, false, 11.6f), "losgelassen");
            Assert.AreEqual(0, w.Schritte(false, true, 12f), "gehalten, aber nicht neu gedrückt");
        }
    }
}
