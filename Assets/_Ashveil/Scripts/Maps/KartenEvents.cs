using System.Collections.Generic;
using Ashveil.Core.Events;
using UnityEngine;

namespace Ashveil.Maps
{
    // Querverbindungen über Ashveil.Core.Events.EventBus<T> (Structs).

    /// <summary>Die Spielfigur hat ein anderes Gebiet betreten (Zone null = offene See). Nur Anzeige (Client).</summary>
    public struct ZoneBetretenEvent
    {
        public ZoneDaten Zone;
        public ZoneDaten Vorher;
        public PvpArt Pvp;

        public ZoneBetretenEvent(ZoneDaten zone, ZoneDaten vorher, PvpArt pvp) { Zone = zone; Vorher = vorher; Pvp = pvp; }
    }

    /// <summary>Die Spielfigur hat eine benannte Unterzone betreten. Nur Anzeige (Client).</summary>
    public struct UnterzoneBetretenEvent
    {
        public UnterzoneDaten Unterzone;
        public ZoneDaten Zone;

        public UnterzoneBetretenEvent(UnterzoneDaten unterzone, ZoneDaten zone) { Unterzone = unterzone; Zone = zone; }
    }

    /// <summary>
    /// Der Server hat Entdeckungen der eigenen Figur bestätigt (PROG-013). Voll = vollständige Liste (nach dem Anmelden),
    /// sonst neu dazugekommene Unterzonen. Xp &gt; 0 mit Name: eine neue Unterzone mit Entdeckungs-EP (Banner „Entdeckt“).
    /// </summary>
    public struct KartenEntdeckungEvent
    {
        public int[] Ids;
        public bool Voll;
        public int Xp;
        public string Name;

        public KartenEntdeckungEvent(int[] ids, bool voll, int xp, string name) { Ids = ids; Voll = voll; Xp = xp; Name = name; }
    }

    /// <summary>
    /// Client: die vom Server bestätigten Unterzonen der eigenen Figur (Nummern wie in den Kartendaten). Nur Anzeige –
    /// was entdeckt ist und welche EP es gibt, entscheidet der Server (NetworkProgression.Karten). Gefüllt über
    /// <see cref="Melden"/>; beim Abmelden geleert.
    /// </summary>
    public static class KartenEntdeckungen
    {
        private static readonly HashSet<int> Menge = new HashSet<int>();

        /// <summary>Steigt mit jeder Änderung (Karten zeichnen dann neu).</summary>
        public static int Revision { get; private set; }
        public static int Anzahl => Menge.Count;
        public static bool Enthaelt(int id) => Menge.Contains(id);
        public static IEnumerable<int> Alle => Menge;
        /// <summary>Letzte Meldung mit EP (Tests): Name und EP.</summary>
        public static string LetzteMeldung { get; private set; } = "";

        /// <summary>Vom Netz (Besitzer-Client): Entdeckungen übernehmen und melden.</summary>
        public static void Melden(int[] ids, bool voll, int xp, string name)
        {
            if (voll) Menge.Clear();
            if (ids != null)
                foreach (int id in ids)
                    if (id > 0) Menge.Add(id);
            Revision++;
            if (xp > 0 && !string.IsNullOrEmpty(name)) LetzteMeldung = name + "+" + xp;
            EventBus<KartenEntdeckungEvent>.Raise(new KartenEntdeckungEvent(ids, voll, xp, name));
        }

        /// <summary>Abmelden: nichts mehr bekannt.</summary>
        public static void Leeren()
        {
            if (Menge.Count == 0) return;
            Menge.Clear();
            Revision++;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Zuruecksetzen()   // Play ohne Neuladen der Skripte
        {
            Menge.Clear();
            Revision = 0;
            LetzteMeldung = "";
        }
    }
}
