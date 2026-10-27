#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ashveil.Core.Diagnostics
{
    /// <summary>
    /// Auswertung langer Bilder (Ruckler, CORE-005; 09.10.2026 nach dem Spieltest r2) ohne Unity-Abhängigkeit: Wo lag die Zeit
    /// eines langen Bildes, welcher Schnappschuss der Messpunkte gehört zu ihm, und die Zähler je Abschnitt. Genutzt von der
    /// Bild-Spitzen-Messung (Ashveil.UI.Diagnostics.FrameSpikeProfiler) und den EditMode-Tests. Nur Editor und Entwicklungs-Build.
    /// </summary>
    public static class SpitzenAuswertung
    {
        /// <summary>Ab hier zählt ein Bild als Hänger (wie bisher).</summary>
        public const double HaengerMs = 100.0;

        /// <summary>Ab hier schreibt die Messung ein langes Bild mit Messpunkten auf (ab 50 ms ist ein Ruckeln sichtbar).</summary>
        public const double DetailMs = 50.0;

        /// <summary>Wo die Zeit eines langen Bildes lag.</summary>
        public enum Art
        {
            /// <summary>Die Spielschleife selbst war lang (Skripte, Laden, Rendern, Warten auf den Grafikfaden).</summary>
            Spielschleife,
            /// <summary>
            /// Am Anfang des Bildes wartet Unity, bis das vorige Bild angezeigt ist (TimeUpdate, „WaitForLastPresentation“). Ist
            /// das lang, hat die Grafikkarte oder der Treiber gebremst (etwa Beschleunigungsstrukturen für Strahlen, neue Shader).
            /// </summary>
            Anzeige,
            /// <summary>Zwischen zwei Durchläufen der Spielschleife (Windows-Nachrichten, Fensterwechsel).</summary>
            Ausserhalb,
            Gemischt
        }

        /// <summary>Einordnen nach der eigenen Stoppuhr: mindestens 60 % der Zeit an einer Stelle.</summary>
        public static Art Einordnen(double gesamtMs, double schleifeMs, double anzeigeMs, double aussenMs)
        {
            if (gesamtMs <= 0.0) return Art.Gemischt;
            if (schleifeMs >= 0.6 * gesamtMs) return Art.Spielschleife;
            if (anzeigeMs >= 0.6 * gesamtMs) return Art.Anzeige;
            if (aussenMs >= 0.6 * gesamtMs) return Art.Ausserhalb;
            return Art.Gemischt;
        }

        public static string Text(Art a) =>
            a == Art.Spielschleife ? "in der Spielschleife" :
            a == Art.Anzeige ? "Warten auf Grafikkarte/Anzeige" :
            a == Art.Ausserhalb ? "außerhalb der Spielschleife (Windows)" : "gemischt";

        /// <summary>
        /// Messpunkte, die über alle Fäden summiert werden und darum nichts über das lange Bild sagen (Leerlauf der Hilfsfäden,
        /// Auftragsverwaltung), oder deren Wert keine Zeit ist.
        /// </summary>
        public static bool Rauschen(string name, double ms, double gesamtMs)
        {
            if (ms > 1.5 * gesamtMs + 2.0) return true;
            switch (name)
            {
                case "Idle":
                case "EngineJob":
                case "Semaphore.WaitForSignal":
                case "WaitForJobGroupID":
                case "GfxTask_ReadValue":
                case "BeginJob":
                case "EndJob":
                case "ScheduleJob":
                case "ScheduleAllocJob":
                case "KickJobs":
                case "WaitForCompleted":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Welcher Schnappschuss der Messpunkte gehört zum langen Bild? A = gleich nach dem Bild gelesen, B = ein Bild später.
        /// Maßstab ist der Messpunkt „PlayerLoop“ gegen die eigene Messung der Spielschleife.
        /// 0 = A, 1 = B, -1 = keiner passt (mehr als 25 % und mehr als 5 ms daneben).
        /// </summary>
        public static int Abgleich(double schleifeMs, double playerLoopA, double playerLoopB)
        {
            double a = Abweichung(schleifeMs, playerLoopA), b = Abweichung(schleifeMs, playerLoopB);
            if (Math.Min(a, b) > 0.25) return -1;
            return a <= b ? 0 : 1;
        }

        private static double Abweichung(double soll, double ist)
        {
            if (ist <= 0.0) return double.MaxValue;
            double d = Math.Abs(soll - ist);
            if (d <= 5.0) return 0.0;
            return d / Math.Max(soll, 1.0);
        }

        /// <summary>Zahl mit deutschem Tausenderpunkt (Berichte).</summary>
        public static string Zahl(double v, string format = "0") => v.ToString("#,##" + format, CultureInfo.GetCultureInfo("de-DE"));
    }

    /// <summary>Zähler für die ganze Sitzung und je Abschnitt (die Ruckler-Probe setzt Abschnitte, sonst gibt es nur „Start“).</summary>
    public sealed class SpitzenStatistik
    {
        public sealed class Teil
        {
            public string Name;
            public long Bilder;
            public double Sekunden;
            public int Ab33, Ab50, Ab100;
            public double LaengstesMs;
            public long NeueZustaende, NeueVarianten;
            public int Schleife, Anzeige, Ausserhalb, Gemischt, MitZustaenden, MitFensterwechsel;

            public double BilderJeSekunde => Sekunden > 0.0 ? Bilder / Sekunden : 0.0;
        }

        private readonly List<Teil> _teile = new List<Teil>();
        private readonly Dictionary<string, double> _ursachen = new Dictionary<string, double>();
        private readonly Dictionary<string, int> _ursachenZahl = new Dictionary<string, int>();

        public Teil Gesamt { get; } = new Teil { Name = "Sitzung" };
        public Teil Aktuell { get; private set; }
        public IReadOnlyList<Teil> Teile => _teile;

        public SpitzenStatistik(string ersterAbschnitt = "Start") => Abschnitt(ersterAbschnitt);

        /// <summary>Neuer Abschnitt; alle folgenden Bilder zählen dort.</summary>
        public void Abschnitt(string name)
        {
            Aktuell = new Teil { Name = string.IsNullOrEmpty(name) ? "?" : name };
            _teile.Add(Aktuell);
        }

        /// <summary>Ein fertiges Bild (eigene Stoppuhr) mit den in ihm neu entstandenen Grafik-Zuständen und Shader-Varianten.</summary>
        public void Bild(double ms, long neueZustaende = 0, long neueVarianten = 0)
        {
            Zaehlen(Gesamt, ms, neueZustaende, neueVarianten);
            Zaehlen(Aktuell, ms, neueZustaende, neueVarianten);
        }

        private static void Zaehlen(Teil t, double ms, long z, long v)
        {
            t.Bilder++;
            t.Sekunden += ms / 1000.0;
            if (ms >= 33.0) t.Ab33++;
            if (ms >= 50.0) t.Ab50++;
            if (ms >= SpitzenAuswertung.HaengerMs) t.Ab100++;
            if (ms > t.LaengstesMs) t.LaengstesMs = ms;
            if (z > 0) t.NeueZustaende += z;
            if (v > 0) t.NeueVarianten += v;
        }

        /// <summary>Einordnung eines langen Bildes (ab <see cref="SpitzenAuswertung.DetailMs"/>) im Abschnitt, in dem es begann.</summary>
        public void Haenger(Teil teil, SpitzenAuswertung.Art art, bool mitZustaenden, bool mitFensterwechsel)
        {
            Einordnen(Gesamt, art, mitZustaenden, mitFensterwechsel);
            if (teil != null && teil != Gesamt) Einordnen(teil, art, mitZustaenden, mitFensterwechsel);
        }

        private static void Einordnen(Teil t, SpitzenAuswertung.Art art, bool z, bool f)
        {
            if (art == SpitzenAuswertung.Art.Spielschleife) t.Schleife++;
            else if (art == SpitzenAuswertung.Art.Anzeige) t.Anzeige++;
            else if (art == SpitzenAuswertung.Art.Ausserhalb) t.Ausserhalb++;
            else t.Gemischt++;
            if (z) t.MitZustaenden++;
            if (f) t.MitFensterwechsel++;
        }

        /// <summary>Zeit eines Messpunkts in einem langen Bild (für „häufigste Messpunkte“).</summary>
        public void Ursache(string name, double ms)
        {
            if (string.IsNullOrEmpty(name) || ms <= 0.0) return;
            _ursachen.TryGetValue(name, out double s);
            _ursachen[name] = s + ms;
            _ursachenZahl.TryGetValue(name, out int n);
            _ursachenZahl[name] = n + 1;
        }

        /// <summary>Die Messpunkte mit der größten Summe über alle Hänger.</summary>
        public List<KeyValuePair<string, double>> Ursachen(int anzahl)
        {
            var l = new List<KeyValuePair<string, double>>(_ursachen);
            l.Sort((a, b) => b.Value.CompareTo(a.Value));
            if (l.Count > anzahl) l.RemoveRange(anzahl, l.Count - anzahl);
            return l;
        }

        public int UrsacheAnzahl(string name) => _ursachenZahl.TryGetValue(name, out int n) ? n : 0;

        /// <summary>Zusammenfassung (Zeilen beginnen mit zwei Leerzeichen; der Auftrag „ruckler_messung“ liest sie).</summary>
        public string Text(bool mitZustaenden)
        {
            var sb = new StringBuilder();
            Teil g = Gesamt;
            sb.Append("  Bilder: ").Append(SpitzenAuswertung.Zahl(g.Bilder)).Append(" in ").Append(g.Sekunden.ToString("0", CultureInfo.InvariantCulture))
              .Append(" s (").Append(g.BilderJeSekunde.ToString("0", CultureInfo.InvariantCulture)).AppendLine(" Bilder/s im Mittel)");
            sb.Append("  Hänger: ≥ 33 ms ").Append(g.Ab33).Append(" · ≥ 50 ms ").Append(g.Ab50).Append(" · ≥ 100 ms ").Append(g.Ab100)
              .Append(" · längster ").Append(g.LaengstesMs.ToString("0", CultureInfo.InvariantCulture)).AppendLine(" ms");
            sb.Append("  Davon ≥ 50 ms: in der Spielschleife ").Append(g.Schleife).Append(" · Warten auf Grafikkarte/Anzeige ").Append(g.Anzeige)
              .Append(" · außerhalb (Windows) ").Append(g.Ausserhalb).Append(" · gemischt ").Append(g.Gemischt);
            if (mitZustaenden) sb.Append(" · mit neuen Grafik-Zuständen ").Append(g.MitZustaenden);
            sb.Append(" · nach Fensterwechsel ").Append(g.MitFensterwechsel).AppendLine();
            if (mitZustaenden)
                sb.Append("  Neue Grafik-Zustände in der Sitzung: ").Append(g.NeueZustaende).Append(" (Shader-Varianten ").Append(g.NeueVarianten).AppendLine(")");
            if (_teile.Count > 1 || (_teile.Count == 1 && _teile[0].Bilder > 0))
            {
                sb.AppendLine("  Je Abschnitt (Bilder/s · Hänger ≥ 50 / ≥ 100 ms · längster" + (mitZustaenden ? " · neue Grafik-Zustände" : "") + "):");
                foreach (Teil t in _teile)
                {
                    if (t.Bilder == 0) continue;
                    sb.Append("    ").Append(t.Name).Append(": ").Append(t.BilderJeSekunde.ToString("0", CultureInfo.InvariantCulture)).Append(" · ")
                      .Append(t.Ab50).Append(" / ").Append(t.Ab100).Append(" · ").Append(t.LaengstesMs.ToString("0", CultureInfo.InvariantCulture)).Append(" ms");
                    if (mitZustaenden) sb.Append(" · ").Append(t.NeueZustaende);
                    sb.AppendLine();
                }
            }
            List<KeyValuePair<string, double>> u = Ursachen(15);
            if (u.Count > 0)
            {
                sb.AppendLine("  Häufigste Messpunkte in den Bildern ≥ 50 ms (Summe ms · in wie vielen Bildern):");
                foreach (var kv in u)
                    sb.Append("    ").Append(kv.Key).Append(": ").Append(kv.Value.ToString("0", CultureInfo.InvariantCulture)).Append(" · ")
                      .Append(UrsacheAnzahl(kv.Key)).AppendLine();
            }
            return sb.ToString();
        }
    }
}
#endif
