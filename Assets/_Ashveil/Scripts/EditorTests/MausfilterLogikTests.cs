using System;
using Ashveil.Core.Platform;
using NUnit.Framework;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// Mausfilter (CORE-005, 09.10.2026 abends): Entscheidungen der Logik ohne Windows. Zeit in den Tests in Millisekunden
    /// (Notlieferung nach 50).
    /// </summary>
    public sealed class MausfilterLogikTests
    {
        private const uint PointerUpdate = MausfilterLogik.WmPointerUpdate, Maus = MausfilterLogik.WmMouseMove;
        private const uint Zeiger = MausfilterLogik.WmSetCursor, Treffer = MausfilterLogik.WmNcHitTest;
        private const uint PointerDown = 0x0246, PointerUp = 0x0247, LinksDown = 0x0201, Rad = 0x020A, Roh = 0x00FF, Taste = 0x0100;

        // Pointer-wParam: unteres Wort Zeiger-Nummer, oberes Wort Flags (0x0002 im Bereich, 0x2000 Haupt-Zeiger, 0x0010 erste Taste)
        private static IntPtr PW(int flags, int id = 1) => new IntPtr(((long)flags << 16) | (long)id);
        private static readonly IntPtr Frei = PW(0x2002), MitTaste = PW(0x2012);
        private static IntPtr L(int x) => new IntPtr(x);
        private static IntPtr ZeigerL(int treffer, uint ausloeser) => new IntPtr(((long)ausloeser << 16) | (long)(treffer & 0xFFFF));

        private static MausfilterLogik Neu() => new MausfilterLogik(50);

        [Test]
        public void Erste_Bewegung_je_Bild_geht_durch_die_neueste_wird_nachgereicht()
        {
            var f = Neu();
            Assert.AreEqual(MausfilterLogik.Urteil.Durchlassen, f.Entscheiden(PointerUpdate, Frei, L(10), 0).Urteil);
            Assert.AreEqual(MausfilterLogik.Urteil.Zurueckhalten, f.Entscheiden(PointerUpdate, Frei, L(11), 1).Urteil);
            Assert.AreEqual(MausfilterLogik.Urteil.Zurueckhalten, f.Entscheiden(PointerUpdate, Frei, L(12), 2).Urteil);
            MausfilterLogik.Nachreichung n = f.Bildwechsel();
            Assert.IsTrue(n.Pointer, "die neueste Bewegung kommt beim Bildwechsel");
            Assert.AreEqual(L(12), n.PointerL);
            Assert.AreEqual(Frei, n.PointerW);
            Assert.IsFalse(n.Maus);
            Assert.IsFalse(f.Bildwechsel().Pointer, "nur einmal nachgereicht");
            Assert.AreEqual(MausfilterLogik.Urteil.Durchlassen, f.Entscheiden(PointerUpdate, Frei, L(13), 3).Urteil, "neues Bild");
            Assert.AreEqual(2, f.Zurueckgehalten[0]);
            Assert.AreEqual(1, f.Nachgereicht[0]);
        }

        [Test]
        public void Tastenwechsel_geht_sofort_durch_und_die_aeltere_Bewegung_verfaellt()
        {
            var f = Neu();
            f.Entscheiden(PointerUpdate, Frei, L(10), 0);
            Assert.AreEqual(MausfilterLogik.Urteil.Zurueckhalten, f.Entscheiden(PointerUpdate, Frei, L(11), 1).Urteil);
            Assert.AreEqual(MausfilterLogik.Urteil.Durchlassen, f.Entscheiden(PointerUpdate, MitTaste, L(12), 2).Urteil, "zweite Taste gedrückt");
            Assert.AreEqual(MausfilterLogik.Urteil.Zurueckhalten, f.Entscheiden(PointerUpdate, MitTaste, L(13), 3).Urteil);
            Assert.AreEqual(MausfilterLogik.Urteil.Durchlassen, f.Entscheiden(PointerUpdate, PW(0x2002, 7), L(14), 4).Urteil, "anderer Zeiger");
            Assert.AreEqual(MausfilterLogik.Urteil.Durchlassen, f.Entscheiden(PointerUpdate, PW(0x2003, 7), L(15), 5).Urteil, "neuer Zeiger");
            Assert.IsFalse(f.Bildwechsel().Pointer, "nichts Älteres nachreichen");
            Assert.AreEqual(2, f.Tastenwechsel);
            Assert.AreEqual(2, f.Verfallen);
        }

        [Test]
        public void Andere_Pointer_Nachricht_laesst_die_zurueckgehaltene_Bewegung_verfallen()
        {
            var f = Neu();
            f.Entscheiden(PointerUpdate, Frei, L(10), 0);
            f.Entscheiden(PointerUpdate, Frei, L(11), 1);
            MausfilterLogik.Entscheidung e = f.Entscheiden(PointerDown, MitTaste, L(11), 2);
            Assert.AreEqual(MausfilterLogik.Urteil.Durchlassen, e.Urteil);
            Assert.IsFalse(e.MausVorher);
            Assert.AreEqual(MausfilterLogik.Urteil.Durchlassen, f.Entscheiden(PointerUp, Frei, L(11), 3).Urteil);
            Assert.IsFalse(f.Bildwechsel().Pointer, "kein veralteter Stand nach Drücken/Loslassen");
            Assert.AreEqual(1, f.Verfallen);
        }

        [Test]
        public void Mausbewegung_wird_vor_Klick_und_Rad_nachgereicht()
        {
            var f = Neu();
            IntPtr ohne = IntPtr.Zero;
            Assert.AreEqual(MausfilterLogik.Urteil.Durchlassen, f.Entscheiden(Maus, ohne, L(5), 0).Urteil);
            Assert.AreEqual(MausfilterLogik.Urteil.Zurueckhalten, f.Entscheiden(Maus, ohne, L(6), 1).Urteil);
            MausfilterLogik.Entscheidung klick = f.Entscheiden(LinksDown, new IntPtr(1), L(6), 2);
            Assert.AreEqual(MausfilterLogik.Urteil.Durchlassen, klick.Urteil);
            Assert.IsTrue(klick.MausVorher, "erst die Bewegung, dann der Klick");
            Assert.AreEqual(L(6), klick.MausL);
            Assert.IsFalse(f.Bildwechsel().Maus, "schon nachgereicht");
            f.Entscheiden(Maus, ohne, L(7), 3);
            f.Entscheiden(Maus, ohne, L(8), 4);
            Assert.IsTrue(f.Entscheiden(Rad, ohne, L(8), 5).MausVorher);
            Assert.AreEqual(MausfilterLogik.Urteil.Durchlassen, f.Entscheiden(Maus, new IntPtr(1), L(9), 6).Urteil, "Taste gehalten: wParam neu");
        }

        [Test]
        public void Mauszeiger_einmal_je_Bild_Klicks_und_Rand_immer()
        {
            var f = Neu();
            IntPtr bewegung = ZeigerL(MausfilterLogik.HtClient, 0x0200);
            Assert.AreEqual(MausfilterLogik.Urteil.Durchlassen, f.Entscheiden(Zeiger, IntPtr.Zero, bewegung, 0).Urteil);
            MausfilterLogik.Entscheidung e = f.Entscheiden(Zeiger, IntPtr.Zero, bewegung, 1);
            Assert.AreEqual(MausfilterLogik.Urteil.SelbstBeantworten, e.Urteil);
            Assert.AreEqual(new IntPtr(1), e.Antwort);
            Assert.AreEqual(MausfilterLogik.Urteil.Durchlassen, f.Entscheiden(Zeiger, IntPtr.Zero, ZeigerL(MausfilterLogik.HtClient, 0x0201), 2).Urteil, "Klick");
            Assert.AreEqual(MausfilterLogik.Urteil.Durchlassen, f.Entscheiden(Zeiger, IntPtr.Zero, ZeigerL(MausfilterLogik.HtClient, 0x0246), 3).Urteil, "Pointer gedrückt");
            Assert.AreEqual(MausfilterLogik.Urteil.Durchlassen, f.Entscheiden(Zeiger, IntPtr.Zero, ZeigerL(2, 0x0200), 4).Urteil, "Titelleiste im Fenster");
            f.Bildwechsel();
            Assert.AreEqual(MausfilterLogik.Urteil.Durchlassen, f.Entscheiden(Zeiger, IntPtr.Zero, bewegung, 5).Urteil, "neues Bild");
            Assert.AreEqual(1, f.SelbstBeantwortet[0]);
        }

        [Test]
        public void Treffertest_nur_im_Vollbild()
        {
            var f = Neu();
            Assert.AreEqual(MausfilterLogik.Urteil.Durchlassen, f.Entscheiden(Treffer, IntPtr.Zero, L(0), 0).Urteil, "Fenster: Rand und Titelleiste");
            f.Vollbild = true;
            MausfilterLogik.Entscheidung e = f.Entscheiden(Treffer, IntPtr.Zero, L(0), 1);
            Assert.AreEqual(MausfilterLogik.Urteil.SelbstBeantworten, e.Urteil);
            Assert.AreEqual(new IntPtr(MausfilterLogik.HtClient), e.Antwort);
            Assert.AreEqual(1, f.SelbstBeantwortet[1]);
        }

        [Test]
        public void Notlieferung_wenn_der_Bildwechsel_ausbleibt()
        {
            var f = Neu();
            IntPtr bewegung = ZeigerL(MausfilterLogik.HtClient, 0x0200);
            f.Entscheiden(PointerUpdate, Frei, L(1), 0);
            f.Entscheiden(Zeiger, IntPtr.Zero, bewegung, 0);
            Assert.AreEqual(MausfilterLogik.Urteil.Zurueckhalten, f.Entscheiden(PointerUpdate, Frei, L(2), 49).Urteil);
            Assert.AreEqual(MausfilterLogik.Urteil.SelbstBeantworten, f.Entscheiden(Zeiger, IntPtr.Zero, bewegung, 49).Urteil);
            Assert.AreEqual(MausfilterLogik.Urteil.Durchlassen, f.Entscheiden(PointerUpdate, Frei, L(3), 50).Urteil, "50 ms ohne Bildwechsel");
            Assert.AreEqual(MausfilterLogik.Urteil.Durchlassen, f.Entscheiden(Zeiger, IntPtr.Zero, bewegung, 50).Urteil);
            Assert.AreEqual(MausfilterLogik.Urteil.Zurueckhalten, f.Entscheiden(PointerUpdate, Frei, L(4), 60).Urteil, "danach wieder zusammenfassen");
            Assert.AreEqual(2, f.Notlieferungen);
            Assert.AreEqual(1, f.Verfallen, "die zurückgehaltene Bewegung von 49 ist älter als die von 50");
        }

        [Test]
        public void Tasten_Roheingabe_und_Rad_bleiben_unberuehrt()
        {
            var f = Neu();
            f.Entscheiden(PointerUpdate, Frei, L(1), 0);
            f.Entscheiden(PointerUpdate, Frei, L(2), 1);
            MausfilterLogik.Entscheidung roh = f.Entscheiden(Roh, IntPtr.Zero, L(0), 2);
            Assert.AreEqual(MausfilterLogik.Urteil.Durchlassen, roh.Urteil);
            Assert.IsFalse(roh.MausVorher);
            Assert.AreEqual(MausfilterLogik.Urteil.Durchlassen, f.Entscheiden(Taste, new IntPtr(0x57), L(0), 3).Urteil);
            Assert.IsTrue(f.Bildwechsel().Pointer, "Roheingabe und Tasten lassen die Bewegung nicht verfallen");
            Assert.AreEqual(MausfilterLogik.Urteil.Durchlassen, f.Entscheiden(Rad, IntPtr.Zero, L(0), 4).Urteil);
        }
    }
}
