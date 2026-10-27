using System;
using UnityEngine;

namespace Ashveil.Gameplay.Characters
{
    /// <summary>Was das Spielmenü beim Server anfragen darf (nur Bitten – der Server speichert und antwortet).</summary>
    public interface ISitzungService
    {
        /// <summary>Den eigenen Charakter jetzt speichern; der Server antwortet mit dem Ergebnis – oder mit einem Abmelde-Countdown.</summary>
        void SpeichernAnfragen();
        /// <summary>Einen laufenden Abmelde-Countdown abbrechen (Menü geschlossen).</summary>
        void AbmeldenAbbrechen();
    }

    /// <summary>
    /// „Speichern und beenden“ (UI-204; Spieltest r2, Max 09.10.2026: „Ich konnte das Spiel nicht speichern und beenden“).
    /// Brücke zwischen Netz und Spielmenü wie <see cref="CharacterCreationClient"/>: Das Menü bittet um eine Speicherung,
    /// der Server speichert sofort und meldet, ob die Datei geschrieben ist (<see cref="Gespeichert"/>). Erst danach endet
    /// das Spiel. Der Server speichert ohnehin laufend und beim Abmelden; das Menü macht das Speichern sichtbar und wartet darauf.
    /// </summary>
    public static class SitzungClient
    {
        /// <summary>22.10.2026 (SPEICHERN-02): Abbruchgrund des Servers für eine Bitte innerhalb der 2-s-Sperre.</summary>
        public const string ZuSchnell = "zu schnell";

        public static ISitzungService Service { get; private set; }
        /// <summary>Eine Bitte ist unterwegs, die Antwort steht noch aus.</summary>
        public static bool Wartet { get; private set; }
        public static bool LetztesOk { get; private set; }
        /// <summary>Zeitpunkt der letzten bestätigten Speicherung (UTC, ISO 8601), leer = noch keine in dieser Sitzung.</summary>
        public static string ZuletztGespeichertUtc { get; private set; } = "";
        public static int AntwortZahl { get; private set; }

        /// <summary>Antwort des Servers: (geschrieben?, Zeitpunkt UTC).</summary>
        public static event Action<bool, string> Gespeichert;
        /// <summary>Außerhalb sicherer Orte (COMBAT-094): der Server wartet so viele Sekunden, bevor er speichert.</summary>
        public static event Action<float> AbmeldenWartet;
        /// <summary>Der Countdown wurde abgebrochen (Schaden, Bewegung, gestorben, Menü zu) – oder seit 22.10.2026 die Bitte kam zu schnell (<see cref="ZuSchnell"/>).</summary>
        public static event Action<string> AbmeldenAbgebrochen;
        /// <summary>Tests: Sekunden des letzten Countdowns und Grund des letzten Abbruchs.</summary>
        public static float LetzterCountdown { get; private set; }
        public static string LetzterAbbruch { get; private set; } = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Service = null;
            Wartet = LetztesOk = false;
            ZuletztGespeichertUtc = "";
            AntwortZahl = 0;
            Gespeichert = null;
            AbmeldenWartet = null;
            AbmeldenAbgebrochen = null;
            LetzterCountdown = 0f;
            LetzterAbbruch = "";
        }

        public static void Anmelden(ISitzungService service)
        {
            Service = service;
            Wartet = false;
        }

        public static void Abmelden(ISitzungService service)
        {
            if (Service != service) return;
            Service = null;
            Wartet = false;
        }

        /// <summary>Um eine Speicherung bitten. false = keine Verbindung zum eigenen Charakter oder schon unterwegs.</summary>
        public static bool SpeichernAnfragen()
        {
            if (Service == null || Wartet) return false;
            Wartet = true;
            Service.SpeichernAnfragen();
            return true;
        }

        /// <summary>Einen laufenden Countdown abbrechen (Menü geschlossen); die Bitte gilt danach als beantwortet.</summary>
        public static void AbmeldenAbbrechen()
        {
            if (Service == null || !Wartet) return;
            Wartet = false;
            Service.AbmeldenAbbrechen();
        }

        /// <summary>Vom Netz: außerhalb sicherer Orte wartet der Server erst (Countdown).</summary>
        public static void MeldeAbmeldenWartet(float sekunden)
        {
            LetzterCountdown = sekunden;
            AbmeldenWartet?.Invoke(sekunden);
        }

        /// <summary>Vom Netz: der Countdown ist abgebrochen; eine neue Bitte ist möglich.</summary>
        public static void MeldeAbmeldenAbgebrochen(string grund)
        {
            Wartet = false;
            LetzterAbbruch = grund ?? "";
            AbmeldenAbgebrochen?.Invoke(grund ?? "");
        }

        /// <summary>Vom Netz: der Server hat geantwortet.</summary>
        public static void MeldeGespeichert(bool ok, string zeitUtc)
        {
            Wartet = false;
            LetztesOk = ok;
            if (ok) ZuletztGespeichertUtc = zeitUtc ?? "";
            AntwortZahl++;
            Gespeichert?.Invoke(ok, zeitUtc ?? "");
        }

        /// <summary>„12:41“ in Ortszeit aus einem ISO-Zeitpunkt (leer, wenn er nicht lesbar ist).</summary>
        public static string Uhrzeit(string zeitUtc)
        {
            if (string.IsNullOrEmpty(zeitUtc)) return "";
            return DateTime.TryParse(zeitUtc, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out DateTime t)
                ? t.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)
                : "";
        }
    }
}
