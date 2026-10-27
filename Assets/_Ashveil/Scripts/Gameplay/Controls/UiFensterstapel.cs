using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Gameplay.Controls
{
    /// <summary>Was ein Druck auf Esc bewirkt (UI-204).</summary>
    public enum EscWirkung
    {
        /// <summary>Esc gehört gerade einem Eingabefeld (Chat, Tastenbelegung, Charakter-Editor, Fotomodus) oder wurde nicht gedrückt.</summary>
        Nichts,
        FensterSchliessen,
        ZielAufheben,
        MenueOeffnen
    }

    /// <summary>
    /// Fensterstapel und Esc-Regel (UI-204; Spieltest r2, Max 09.10.2026): Esc schließt zuerst das oberste offene Fenster,
    /// hebt dann das Ziel auf und öffnet erst danach das Spielmenü. Jeder Druck wirkt genau einmal – früher schlossen alle
    /// Fenster und das Ziel im selben Bild.
    ///
    /// Fenster melden sich einmal an (Name, „offen?“, „schließen“). Der Stapel merkt sich, in welcher Reihenfolge sie
    /// aufgegangen sind; das zuletzt geöffnete liegt oben (gehen zwei im selben Bild auf, liegt das später angemeldete oben).
    /// Hat ein Eingabefeld den Fokus (<see cref="UiPointer.KeyboardCaptured"/>), regelt es Esc selbst.
    /// <see cref="Takt"/> läuft einmal je Bild – das Spielmenü ruft es früh im Bild auf, die Spielsteuerung zur Sicherheit
    /// noch einmal (der zweite Aufruf im selben Bild tut nichts).
    /// </summary>
    public static class UiFensterstapel
    {
        private sealed class Eintrag
        {
            public object Besitzer;
            public string Name;
            public Func<bool> Offen;
            public Action Schliessen;
            public bool WarOffen;
            public long Seit;
            /// <summary>Esc hat das Schließen angestoßen; bis das Fenster wirklich zu ist (höchstens 30 Bilder), zählt es nicht als oben.</summary>
            public int SchliesstBis = -1;
        }

        private const int SchliessenWarteBilder = 30;

        private static readonly List<Eintrag> Eintraege = new List<Eintrag>();
        private static long _zaehler;
        private static int _letzterTakt = -1;
        private static int _zielBild = -1;
        private static int _bild;
        private static bool _escSimuliert;

        /// <summary>Hat die eigene Spielfigur ein Ziel? Setzt die Spielfigur in jedem Bild.</summary>
        public static bool HatZiel;

        /// <summary>Öffnet das Spielmenü. Setzt das Spielmenü beim Aufbau.</summary>
        public static Action MenueOeffnen;

        /// <summary>Wirkung des letzten Esc (Tests, Probe im fertigen Spiel).</summary>
        public static EscWirkung LetzteWirkung { get; private set; }

        /// <summary>Name des Fensters, das der letzte Esc geschlossen hat (leer = keines).</summary>
        public static string LetztesFenster { get; private set; } = "";

        /// <summary>In diesem Bild hat Esc das Ziel aufgehoben. Die Spielsteuerung liest das statt der rohen Esc-Taste.</summary>
        public static bool ZielAufhebenAngefordert => _zielBild >= 0 && _zielBild == Time.frameCount;

        /// <summary>Alles vergessen – beim Start jeder Play-Sitzung (ohne Neuladen der Skripte) und in Tests.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Zuruecksetzen()
        {
            Eintraege.Clear();
            _zaehler = 0;
            _letzterTakt = -1;
            _zielBild = -1;
            _bild = 0;
            _escSimuliert = false;
            HatZiel = false;
            MenueOeffnen = null;
            LetzteWirkung = EscWirkung.Nichts;
            LetztesFenster = "";
        }

        // ---------------------------------------------------------------- Anmelden

        /// <summary>Fenster anmelden. Ein zweiter Aufruf mit demselben Besitzer und Namen ersetzt den ersten.</summary>
        public static void Anmelden(object besitzer, string name, Func<bool> offen, Action schliessen)
        {
            if (besitzer == null || offen == null || schliessen == null) return;
            for (int i = Eintraege.Count - 1; i >= 0; i--)
                if (ReferenceEquals(Eintraege[i].Besitzer, besitzer) && Eintraege[i].Name == name) Eintraege.RemoveAt(i);
            Eintraege.Add(new Eintrag { Besitzer = besitzer, Name = name ?? "", Offen = offen, Schliessen = schliessen });
        }

        /// <summary>Alle Fenster eines Besitzers abmelden (OnDestroy).</summary>
        public static void Abmelden(object besitzer)
        {
            for (int i = Eintraege.Count - 1; i >= 0; i--)
                if (ReferenceEquals(Eintraege[i].Besitzer, besitzer)) Eintraege.RemoveAt(i);
        }

        /// <summary>Tests und Probe im fertigen Spiel: im nächsten Takt so tun, als sei Esc gedrückt worden.</summary>
        public static void EscAusloesen() => _escSimuliert = true;

        // ---------------------------------------------------------------- Regel

        /// <summary>Die Esc-Regel als reine Entscheidung.</summary>
        public static EscWirkung Entscheiden(bool eingabeFokus, bool fensterOffen, bool hatZiel)
        {
            if (eingabeFokus) return EscWirkung.Nichts;
            if (fensterOffen) return EscWirkung.FensterSchliessen;
            return hatZiel ? EscWirkung.ZielAufheben : EscWirkung.MenueOeffnen;
        }

        /// <summary>Name des obersten offenen Fensters (leer = keines offen).</summary>
        public static string Oberstes
        {
            get
            {
                Eintrag e = Oben();
                return e != null ? e.Name : "";
            }
        }

        /// <summary>Wie viele angemeldete Fenster gerade offen sind (Stand des letzten Takts).</summary>
        public static int OffeneFenster
        {
            get
            {
                int n = 0;
                foreach (Eintrag e in Eintraege) if (e.WarOffen) n++;
                return n;
            }
        }

        /// <summary>Einmal je Bild: offene Fenster nachführen und einen Druck auf Esc anwenden.</summary>
        public static void Takt()
        {
            int bild = Time.frameCount;
            if (_letzterTakt == bild) return;
            _letzterTakt = bild;
            bool esc = RawInput.EscapePressed || _escSimuliert;
            _escSimuliert = false;
            Verarbeiten(esc, UiPointer.KeyboardCaptured, bild);
        }

        /// <summary>Kern ohne Unity-Bildzähler (EditMode-Tests): Fenster nachführen, dann Esc anwenden.</summary>
        public static EscWirkung Verarbeiten(bool esc, bool eingabeFokus, int bild)
        {
            _bild = bild;
            Nachfuehren();
            if (!esc) return EscWirkung.Nichts;
            Eintrag oben = Oben();
            EscWirkung w = Entscheiden(eingabeFokus, oben != null, HatZiel);
            LetzteWirkung = w;
            LetztesFenster = "";
            switch (w)
            {
                case EscWirkung.FensterSchliessen:
                    LetztesFenster = oben.Name;
                    oben.SchliesstBis = bild + SchliessenWarteBilder;
                    try { oben.Schliessen(); }
                    catch (Exception e) { Debug.LogException(e); }
                    // Schließt das Fenster sofort, gilt es schon jetzt als zu – sonst, sobald es im eigenen Takt verschwindet.
                    if (!IstOffen(oben)) oben.WarOffen = false;
                    break;
                case EscWirkung.ZielAufheben:
                    _zielBild = bild;
                    break;
                case EscWirkung.MenueOeffnen:
                    try { MenueOeffnen?.Invoke(); }
                    catch (Exception e) { Debug.LogException(e); }
                    break;
            }
            return w;
        }

        private static void Nachfuehren()
        {
            for (int i = Eintraege.Count - 1; i >= 0; i--)
            {
                // zerstörte Fenster (Szenenwechsel) fallen still heraus
                if (Eintraege[i].Besitzer is UnityEngine.Object u && u == null) Eintraege.RemoveAt(i);
            }
            foreach (Eintrag e in Eintraege)
            {
                bool offen = IstOffen(e);
                if (offen && !e.WarOffen) e.Seit = ++_zaehler;   // neu geöffnet: nach oben
                if (!offen) e.SchliesstBis = -1;
                e.WarOffen = offen;
            }
        }

        private static Eintrag Oben()
        {
            Eintrag best = null;
            foreach (Eintrag e in Eintraege)
            {
                if (!e.WarOffen || (e.SchliesstBis >= 0 && _bild <= e.SchliesstBis)) continue;
                if (best == null || e.Seit > best.Seit) best = e;
            }
            return best;
        }

        private static bool IstOffen(Eintrag e)
        {
            try { return e.Offen(); }
            catch (Exception) { return false; }   // Fenster schon abgebaut
        }
    }
}
