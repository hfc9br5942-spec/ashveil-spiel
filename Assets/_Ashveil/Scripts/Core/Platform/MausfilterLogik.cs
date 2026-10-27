using System;

namespace Ashveil.Core.Platform
{
    /// <summary>
    /// Entscheidungen des Mausfilters (CORE-005, 09.10.2026), ohne Windows-Aufrufe und darum im Editor prüfbar
    /// (EditorTests/MausfilterLogikTests). Die Windows-Seite (Fenster-Umleitung, Bildwechsel in der Spielschleife) steht in
    /// <see cref="Mausfilter"/>.
    ///
    /// Grundregel: Von jeder Bewegungsart (Pointer-Update, Mausbewegung) bekommt Unity je Bild die erste Nachricht sofort. Spätere
    /// im selben Bild hält der Filter zurück; die jeweils letzte reicht er beim Bildwechsel nach (am Anfang von Initialization, vor
    /// Unitys Eingabe-Verarbeitung – Unity sieht so immer die neueste Lage). Weitere Mauszeiger-Anfragen im Bild beantwortet er
    /// selbst (der Zeiger bleibt, wie Unity ihn zuletzt gesetzt hat), den Treffertest im Vollbild ebenfalls (alles ist Spielfläche).
    /// Tasten, Klicks, Mausrad und Roheingabe (Kamera-Drehung) gehen unverändert durch.
    ///
    /// Schutzregeln:
    /// - Ändern sich die Maustasten oder der Zeiger (anderes Gerät, neuer Zeiger, abgebrochen), geht die Nachricht sofort durch;
    ///   eine zurückgehaltene ältere Bewegung derselben Art verfällt (die neue trägt den neueren Stand).
    /// - Folgt eine andere Pointer-Nachricht (gedrückt, losgelassen, verlassen, Mausrad …), verfällt ein zurückgehaltenes
    ///   Pointer-Update ebenso: Windows liefert zur neuen Nachricht die aktuellen Zeigerdaten.
    /// - Vor einem Klick, dem Mausrad oder „Maus verlässt das Fenster“ wird eine zurückgehaltene Mausbewegung zuerst nachgereicht,
    ///   damit die Reihenfolge stimmt.
    /// - Mauszeiger-Anfragen, die ein Klick auslöst, gehen immer durch.
    /// - Notlieferung: Bleibt der Bildwechsel aus, geht spätestens nach <see cref="NotTicks"/> (50 ms) wieder eine Nachricht jeder
    ///   Art durch – die Maus friert nie ein.
    /// </summary>
    public sealed class MausfilterLogik
    {
        public const uint WmSetCursor = 0x0020, WmNcHitTest = 0x0084, WmMouseMove = 0x0200, WmPointerUpdate = 0x0245;
        public const uint WmMouseLeave = 0x02A3;
        public const int HtClient = 1;

        // Pointer-Flags im oberen Wort von wParam (POINTER_MESSAGE_FLAG_*)
        private const int PointerNeu = 0x0001, PointerTasten = 0x01F0, PointerAbgebrochen = 0x8000;

        public enum Urteil
        {
            /// <summary>An Unity weiterreichen.</summary>
            Durchlassen,
            /// <summary>Nicht weiterreichen; als neueste merken, beim Bildwechsel nachreichen.</summary>
            Zurueckhalten,
            /// <summary>Nicht weiterreichen; der Filter antwortet selbst mit <see cref="Entscheidung.Antwort"/>.</summary>
            SelbstBeantworten,
        }

        /// <summary>Ergebnis für eine Nachricht.</summary>
        public struct Entscheidung
        {
            public Urteil Urteil;
            public IntPtr Antwort;
            /// <summary>Vorher die zurückgehaltene Mausbewegung (MausW/MausL) an Unity nachreichen.</summary>
            public bool MausVorher;
            public IntPtr MausW, MausL;
        }

        /// <summary>Was beim Bildwechsel nachzureichen ist.</summary>
        public struct Nachreichung
        {
            public bool Pointer;
            public IntPtr PointerW, PointerL;
            public bool Maus;
            public IntPtr MausW, MausL;
        }

        /// <summary>Notlieferung: so viele Zeiteinheiten (Stoppuhr-Ticks) höchstens ohne Weiterreichen je Art.</summary>
        public readonly long NotTicks;

        /// <summary>Vom Hauptfaden je Bild: Vollbild (randlos oder exklusiv) – dann beantwortet der Filter den Treffertest.</summary>
        public bool Vollbild;

        // Zähler (für die Messung im Entwicklungs-Build; im fertigen Spiel liest sie niemand)
        /// <summary>Zurückgehalten: [0] Pointer-Update, [1] Mausbewegung.</summary>
        public readonly int[] Zurueckgehalten = new int[2];
        /// <summary>Beim Bildwechsel oder vor einem Klick nachgereicht: [0] Pointer-Update, [1] Mausbewegung.</summary>
        public readonly int[] Nachgereicht = new int[2];
        /// <summary>Selbst beantwortet: [0] Mauszeiger, [1] Treffertest.</summary>
        public readonly int[] SelbstBeantwortet = new int[2];
        /// <summary>Zurückgehaltene Bewegungen, die eine neuere Nachricht überholt hat.</summary>
        public int Verfallen;
        /// <summary>Im selben Bild sofort durchgelassen, weil sich Tasten oder Zeiger geändert haben.</summary>
        public int Tastenwechsel;
        /// <summary>Im selben Bild durchgelassen, weil der Bildwechsel länger als <see cref="NotTicks"/> ausblieb.</summary>
        public int Notlieferungen;

        private int _bild = 1;
        private int _pointerBild, _mausBild, _zeigerBild;   // Bild der letzten Weiterreichung (0 = noch nie)
        private long _pointerTick, _mausTick, _zeigerTick;
        private long _pointerSchluessel;
        private IntPtr _mausWZuletzt;
        private bool _pointerOffen, _mausOffen;
        private IntPtr _pointerW, _pointerL, _mausW, _mausL;

        public MausfilterLogik(long notTicks)
        {
            NotTicks = Math.Max(1L, notTicks);
        }

        /// <summary>Entscheidung für eine Nachricht an Unitys Fenster. jetzt: Stoppuhr-Ticks (Einheit wie <see cref="NotTicks"/>).</summary>
        public Entscheidung Entscheiden(uint nachricht, IntPtr wParam, IntPtr lParam, long jetzt)
        {
            var e = new Entscheidung { Urteil = Urteil.Durchlassen };
            switch (nachricht)
            {
                case WmPointerUpdate:
                {
                    long w = wParam.ToInt64();
                    int flags = (int)((w >> 16) & 0xFFFF);
                    long schluessel = ((w & 0xFFFF) << 16) | (long)(flags & PointerTasten);
                    bool immer = (flags & (PointerNeu | PointerAbgebrochen)) != 0;
                    if (_pointerBild == _bild && !immer)
                    {
                        if (schluessel == _pointerSchluessel && jetzt - _pointerTick < NotTicks)
                        {
                            _pointerW = wParam;
                            _pointerL = lParam;
                            _pointerOffen = true;
                            Zurueckgehalten[0]++;
                            e.Urteil = Urteil.Zurueckhalten;
                            return e;
                        }
                        if (schluessel != _pointerSchluessel) Tastenwechsel++;
                        else Notlieferungen++;
                    }
                    if (_pointerOffen)
                    {
                        _pointerOffen = false;
                        Verfallen++;
                    }
                    _pointerBild = _bild;
                    _pointerTick = jetzt;
                    _pointerSchluessel = schluessel;
                    return e;
                }
                case WmMouseMove:
                {
                    if (_mausBild == _bild)
                    {
                        if (wParam == _mausWZuletzt && jetzt - _mausTick < NotTicks)
                        {
                            _mausW = wParam;
                            _mausL = lParam;
                            _mausOffen = true;
                            Zurueckgehalten[1]++;
                            e.Urteil = Urteil.Zurueckhalten;
                            return e;
                        }
                        if (wParam != _mausWZuletzt) Tastenwechsel++;
                        else Notlieferungen++;
                    }
                    if (_mausOffen)
                    {
                        _mausOffen = false;
                        Verfallen++;
                    }
                    _mausBild = _bild;
                    _mausTick = jetzt;
                    _mausWZuletzt = wParam;
                    return e;
                }
                case WmSetCursor:
                {
                    long l = lParam.ToInt64();
                    int treffer = (int)(l & 0xFFFF);
                    int ausloeser = (int)((l >> 16) & 0xFFFF);
                    bool klick = (ausloeser >= 0x0201 && ausloeser <= 0x020E) || ausloeser == 0x0246 || ausloeser == 0x0247;
                    if (treffer != HtClient || klick) return e;
                    if (_zeigerBild == _bild)
                    {
                        if (jetzt - _zeigerTick < NotTicks)
                        {
                            SelbstBeantwortet[0]++;
                            e.Urteil = Urteil.SelbstBeantworten;
                            e.Antwort = new IntPtr(1);   // „erledigt“: Windows setzt keinen anderen Zeiger
                            return e;
                        }
                        Notlieferungen++;
                    }
                    _zeigerBild = _bild;
                    _zeigerTick = jetzt;
                    return e;
                }
                case WmNcHitTest:
                    if (Vollbild)
                    {
                        SelbstBeantwortet[1]++;
                        e.Urteil = Urteil.SelbstBeantworten;
                        e.Antwort = new IntPtr(HtClient);
                    }
                    return e;
            }

            if (IstPointerNachricht(nachricht))
            {
                if (_pointerOffen)
                {
                    _pointerOffen = false;
                    Verfallen++;
                }
            }
            else if (IstMausTasteOderVerlassen(nachricht) && _mausOffen)
            {
                _mausOffen = false;
                Nachgereicht[1]++;
                e.MausVorher = true;
                e.MausW = _mausW;
                e.MausL = _mausL;
            }
            return e;
        }

        /// <summary>
        /// Vom Hauptfaden einmal je Bild (Anfang von Initialization): gibt zurück, was an Unity nachzureichen ist, und beginnt das
        /// neue Bild.
        /// </summary>
        public Nachreichung Bildwechsel()
        {
            var n = new Nachreichung();
            if (_pointerOffen)
            {
                _pointerOffen = false;
                n.Pointer = true;
                n.PointerW = _pointerW;
                n.PointerL = _pointerL;
                Nachgereicht[0]++;
            }
            if (_mausOffen)
            {
                _mausOffen = false;
                n.Maus = true;
                n.MausW = _mausW;
                n.MausL = _mausL;
                Nachgereicht[1]++;
            }
            _bild = _bild == int.MaxValue ? 1 : _bild + 1;
            return n;
        }

        /// <summary>Pointer-Nachrichten außer dem Pointer-Update (WM_NCPOINTERUPDATE … WM_POINTERROUTEDRELEASED).</summary>
        private static bool IstPointerNachricht(uint n) => n >= 0x0241 && n <= 0x0253 && n != WmPointerUpdate;

        /// <summary>Maustasten und Mausrad (WM_LBUTTONDOWN … WM_MOUSEHWHEEL) und „Maus verlässt das Fenster“.</summary>
        private static bool IstMausTasteOderVerlassen(uint n) => (n >= 0x0201 && n <= 0x020E) || n == WmMouseLeave;
    }
}
