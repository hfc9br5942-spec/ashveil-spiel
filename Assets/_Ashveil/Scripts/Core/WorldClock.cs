using System;
using System.Globalization;
using UnityEngine;

namespace Ashveil.Core
{
    /// <summary>
    /// M8: Tageszeit der Welt – fuer Licht, NPC-Tagesablaeufe, Tiere. Alle Rechner kommen ohne laufende Uebertragung
    /// auf dieselbe Uhrzeit: Stunde = Bezugsstunde + (gemeinsame Netzzeit − Bezugszeit) / Sekunden je Stunde.
    /// Die Netzzeit (<see cref="TimeSource"/>) setzt die Netzwerkschicht (FishNet-Takt, auf allen Clients gleich);
    /// ohne Verbindung laeuft sie mit der Spielzeit.
    ///
    /// Echtzeit (NET-020, Max 06.10.2026 „wie classic+ forever“): 1 Spielstunde = 1 echte Stunde, ein Spieltag = 24 h.
    /// Der Server stellt die Bezugsstunde beim Start aus seiner Ortszeit (Stunden seit <see cref="Epoch"/>) und einem
    /// Versatz (<see cref="WorldTimeStart"/>). Dadurch folgt die Uhr der Serverzeit, springt nach einem Neustart nicht auf
    /// einen festen Wert zurueck, und der Tageszaehler (Mondphasen, Monatsplan des Himmels) zaehlt ab einem festen Datum.
    /// Gespeichert wird nichts.
    /// </summary>
    public static class WorldClock
    {
        /// <summary>Echtzeit-Sekunden je Spielstunde: 3 600 = Echtzeit (NET-020; bis 06.10.2026: 300, ein Tag = 2 Stunden).</summary>
        public const double DefaultSecondsPerHour = 3600.0;
        /// <summary>Gestaltete Stunde: spaeter Nachmittag – das Licht, fuer das Kareth gestaltet wurde (Editor, Bilder, Tests).</summary>
        public const double DefaultStartHour = 15.5;
        /// <summary>Tag 0 der Weltzeit: 1. Januar 2026, 0 Uhr Serverzeit (fester Bezug fuer Tageszaehler und Mondphasen).</summary>
        public static readonly DateTime Epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        private static double _hourAtRef = DefaultStartHour, _refTime, _secondsPerHour = DefaultSecondsPerHour;

        /// <summary>Gemeinsame Zeit in Sekunden (Netzwerktakt). null = Spielzeit.</summary>
        public static Func<double> TimeSource;

        public static event Action Changed;

        /// <summary>Bitte an den Server, die Uhr zu stellen (setzt die Netzwerkschicht; Entwicklerwerkzeug "/zeit 22").</summary>
        public static Action<float> SetRequest;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _hourAtRef = DefaultStartHour;
            _refTime = 0;
            _secondsPerHour = DefaultSecondsPerHour;
            TimeSource = null;
            Changed = null;
            SetRequest = null;
        }

        public static double Now => TimeSource != null ? TimeSource() : Time.timeAsDouble;
        public static double SecondsPerHour => _secondsPerHour;
        public static double HourAtRef => _hourAtRef;
        public static double RefTime => _refTime;

        /// <summary>Fortlaufende Stunden seit Tag 0 (fuer Tageszaehler und deterministische Ablaeufe).</summary>
        public static double TotalHours => _hourAtRef + (Now - _refTime) / Math.Max(1e-3, _secondsPerHour);

        /// <summary>Uhrzeit 0..24.</summary>
        public static float Hours => HourOfDay(TotalHours);

        public static int Day => (int)Math.Floor(TotalHours / 24.0);

        /// <summary>Uhrzeit 0..24 zu fortlaufenden Weltstunden.</summary>
        public static float HourOfDay(double totalHours)
        {
            double h = totalHours % 24.0;
            return (float)(h < 0 ? h + 24.0 : h);
        }

        /// <summary>Echtzeit-Uhr ohne Versatz: Stunden seit <see cref="Epoch"/> zur Ortszeit des Servers.</summary>
        public static double HoursSinceEpoch(DateTime serverZeit) => (serverZeit - Epoch).TotalHours;

        /// <summary>"16:30"</summary>
        public static string Text => Format(TotalHours);

        /// <summary>Stunde (oder fortlaufende Weltstunden) als "09:05" – in double gerechnet, damit volle Minuten nicht abrunden.</summary>
        public static string Format(double h)
        {
            h %= 24.0;
            if (h < 0) h += 24.0;
            int min = (int)Math.Floor(h * 60.0 + 1e-4) % (24 * 60);
            return $"{min / 60:00}:{min % 60:00}";
        }

        /// <summary>"Nachmittag", "Nacht" … fuer Anzeige und Chat.</summary>
        public static string PeriodName(float h)
        {
            if (h < 5f) return "Nacht";
            if (h < 6.5f) return "Morgengrauen";
            if (h < 11f) return "Morgen";
            if (h < 14f) return "Mittag";
            if (h < 18f) return "Nachmittag";
            if (h < 20.5f) return "Abend";
            return "Nacht";
        }

        /// <summary>"/zeit 22", "22:30", "7" → Stunde; false bei Unsinn.</summary>
        public static bool TryParseHour(string text, out float hour)
        {
            hour = 0f;
            text = (text ?? "").Trim().Replace("uhr", "").Replace("Uhr", "").Trim();
            if (text.Length == 0) return false;
            string[] p = text.Split(':', '.');
            if (!int.TryParse(p[0], out int hh) || hh < 0 || hh > 24) return false;
            int mm = 0;
            if (p.Length > 1 && (!int.TryParse(p[1], out mm) || mm < 0 || mm > 59)) return false;
            hour = (hh % 24) + mm / 60f;
            return true;
        }

        /// <summary>Uhr einstellen (Server verteilt dieselben drei Werte an alle).</summary>
        public static void Set(double hourAtRef, double refTime, double secondsPerHour)
        {
            _hourAtRef = hourAtRef;
            _refTime = refTime;
            _secondsPerHour = secondsPerHour > 0 ? secondsPerHour : DefaultSecondsPerHour;
            Changed?.Invoke();
        }

        /// <summary>Jetzt auf eine Uhrzeit springen (gleicher Tag oder der naechste, nie zurueck).</summary>
        public static void JumpTo(float hour)
        {
            Set(JumpTarget(TotalHours, hour), Now, _secondsPerHour);
        }

        /// <summary>Ziel eines Sprungs von <paramref name="total"/> auf die Uhrzeit <paramref name="hour"/> (nie zurueck).</summary>
        public static double JumpTarget(double total, float hour)
        {
            double day = Math.Floor(total / 24.0) * 24.0;
            double target = day + Mathf.Repeat(hour, 24f);
            if (target < total) target += 24.0;
            return target;
        }

        /// <summary>Liegt h im Zeitfenster [from, to) (auch ueber Mitternacht, z. B. 22–6)?</summary>
        public static bool InWindow(float h, float from, float to) =>
            from <= to ? h >= from && h < to : h >= from || h < to;
    }

    /// <summary>
    /// Wie die Weltuhr beim Serverstart steht (NET-020): Echtzeit, Echtzeit mit Versatz in Stunden oder eine feste
    /// Startstunde am heutigen Tag, ab der die Uhr in Echtzeit weiterlaeuft (Spieltest r2: Vormittag, Max 06.10.).
    /// Text fuer Startparameter und Editor-Einstellung: „echtzeit“ · „09:00“, „9“, „vormittag“, „gestaltet“ (= 15:30) ·
    /// „+3“, „versatz=-2.5“, „versatz=-2:30“ (ein Minus am Anfang verwechselt die Kommandozeile mit einem Schalter).
    /// </summary>
    public readonly struct WorldTimeStart : IEquatable<WorldTimeStart>
    {
        public enum Kind { Echtzeit, Versatz, Startstunde }

        public readonly Kind Mode;
        /// <summary>Versatz in Stunden bzw. Startstunde 0..24.</summary>
        public readonly double Value;

        private WorldTimeStart(Kind mode, double value)
        {
            Mode = mode;
            Value = value;
        }

        public static WorldTimeStart Echtzeit => new WorldTimeStart(Kind.Echtzeit, 0.0);
        public static WorldTimeStart Versatz(double stunden) => new WorldTimeStart(Kind.Versatz, stunden);
        public static WorldTimeStart Start(double stunde) => new WorldTimeStart(Kind.Startstunde, ((stunde % 24.0) + 24.0) % 24.0);

        /// <summary>Fortlaufende Weltstunden beim Serverstart zur Ortszeit <paramref name="serverZeit"/>.</summary>
        public double TotalHoursAt(DateTime serverZeit)
        {
            double echt = WorldClock.HoursSinceEpoch(serverZeit);
            switch (Mode)
            {
                case Kind.Versatz: return echt + Value;
                case Kind.Startstunde: return Math.Floor(echt / 24.0) * 24.0 + Value;
                default: return echt;
            }
        }

        public static bool TryParse(string text, out WorldTimeStart start)
        {
            start = Echtzeit;
            string t = (text ?? "").Trim().ToLowerInvariant().Replace(" ", "").Replace("uhr", "");
            if (t.Length == 0 || t == "echtzeit" || t == "echt" || t == "real") return true;
            if (t == "vormittag") { start = Start(9.0); return true; }
            if (t == "gestaltet" || t == "nachmittag") { start = Start(WorldClock.DefaultStartHour); return true; }
            if (t.StartsWith("start=", StringComparison.Ordinal)) t = t.Substring(6);
            bool versatz = false;
            if (t.StartsWith("versatz=", StringComparison.Ordinal)) { t = t.Substring(8); versatz = true; }
            if (t.EndsWith("h", StringComparison.Ordinal)) { t = t.Substring(0, t.Length - 1); versatz = true; }
            if (t.StartsWith("+", StringComparison.Ordinal) || t.StartsWith("-", StringComparison.Ordinal)) versatz = true;
            if (!TryParseStunden(t, out double h)) return false;
            if (versatz)
            {
                if (Math.Abs(h) > 24.0 * 366.0) return false;
                start = Versatz(h);
                return true;
            }
            if (h < 0.0 || h > 24.0) return false;
            start = Start(h);
            return true;
        }

        /// <summary>„9“, „09:30“, „-2:30“, „2.5“, „2,5“ → Stunden.</summary>
        private static bool TryParseStunden(string t, out double h)
        {
            h = 0.0;
            if (t.Length == 0) return false;
            double sign = 1.0;
            if (t[0] == '+' || t[0] == '-')
            {
                if (t[0] == '-') sign = -1.0;
                t = t.Substring(1);
            }
            int colon = t.IndexOf(':');
            if (colon >= 0)
            {
                if (!int.TryParse(t.Substring(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out int hh) ||
                    !int.TryParse(t.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int mm) || mm > 59) return false;
                h = sign * (hh + mm / 60.0);
                return true;
            }
            if (!double.TryParse(t.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double v)) return false;
            h = sign * v;
            return true;
        }

        public override string ToString()
        {
            switch (Mode)
            {
                case Kind.Versatz:
                    double a = Math.Abs(Value);
                    int hh = (int)a, mm = (int)Math.Round((a - hh) * 60.0);
                    if (mm == 60) { hh++; mm = 0; }
                    return $"Echtzeit mit Versatz {(Value < 0 ? "−" : "+")}{hh}:{mm:00} h";
                case Kind.Startstunde:
                    return $"Start {WorldClock.Format(Value)} Uhr, danach Echtzeit";
                default:
                    return "Echtzeit (Serverzeit)";
            }
        }

        public bool Equals(WorldTimeStart other) => Mode == other.Mode && Value.Equals(other.Value);
        public override bool Equals(object obj) => obj is WorldTimeStart o && Equals(o);
        public override int GetHashCode() => ((int)Mode * 397) ^ Value.GetHashCode();
    }
}
