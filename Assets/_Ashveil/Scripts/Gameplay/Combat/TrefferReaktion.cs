using System;
using UnityEngine;

namespace Ashveil.Gameplay.Combat
{
    /// <summary>
    /// 23.10.2026 (Kampfreaktionen, Codex-Detailplan §5.1, Netzanteil Spielablauf): das tatsächliche Ergebnis eines Treffers, wie es
    /// die Darstellung braucht – EINE Entscheidung je Ereignis, auf Host und reinen Clients gleich. Der Server leitet sie in
    /// <see cref="Health.ApplyDamage"/> aus dem endgültigen Treffer ab (<see cref="TrefferReaktionen.Ableiten"/>) und schickt sie als Byte
    /// mit (NetworkHealth.ObserversDamaged); „Verfehlt“ kommt ohne Schaden über NetworkHealth.ObserversMissed.
    ///
    /// Kein Parier-Ergebnis: Parade als Kampfergebnis ist verworfen (Kanon 03.10., 10_CANON_DECISIONS) – die Darstellung darf
    /// keinen Treffer scheinbar schadlos parieren. Tod und echte Kontrollzustände haben in der Darstellung Vorrang
    /// (<see cref="TrefferReaktionen.Darstellbar"/>); tödlich ist ein Treffer, wenn das Leben im Damaged-Ereignis schon 0 ist
    /// (Host und Clients setzen das Leben vor dem Ereignis).
    ///
    /// Die Werte gehen als Byte übers Netz: Reihenfolge nie umstellen, neue Werte nur hinten anhängen.
    /// </summary>
    public enum TrefferReaktion : byte
    {
        /// <summary>Nichts darzustellen (kein Schaden, nichts geblockt/absorbiert) oder unbekannter Netzwert.</summary>
        Keine = 0,
        /// <summary>Schaden kam an (ohne Block/Absorption).</summary>
        Schaden = 1,
        /// <summary>Angriff verfehlt – kein Schaden, das Ziel behält seine Tätigkeit (kein erfundenes Ausweichen/Parieren).</summary>
        Verfehlt = 2,
        /// <summary>Passiv ausgewichen (Trefferwurf des Ziels), kein Schaden.</summary>
        Ausgewichen = 3,
        /// <summary>Schild hat den ganzen Rest nach der Rüstung gefangen – kein Schaden kam an.</summary>
        BlockVoll = 4,
        /// <summary>Schild hat einen Teil gefangen, Restschaden kam an (eine kombinierte Reaktion, nicht Block + Treffer).</summary>
        BlockTeil = 5,
        /// <summary>Magischer Schild (Aschenschild) hat alles aufgefangen – kein Blut, kein Schmerz.</summary>
        AbsorbVoll = 6,
        /// <summary>Magischer Schild hat einen Teil aufgefangen, Restschaden kam an.</summary>
        AbsorbTeil = 7,
        /// <summary>Periodischer Schaden (Gift, Brennen, Blutung) oder Umwelt (Ertrinken): gedrosselter Impuls, keine Schlagreaktion.</summary>
        Tick = 8
    }

    /// <summary>23.10.2026: reine Ableitung und Netz-Hilfen zu <see cref="TrefferReaktion"/> – ohne Seiteneffekte, Host und Client gleich.</summary>
    public static class TrefferReaktionen
    {
        /// <summary>Höchster gültiger Netzwert.</summary>
        public const byte MaxWert = (byte)TrefferReaktion.Tick;

        /// <summary>Anzahl der Werte (Zähler-Felder in Prüfungen).</summary>
        public const int Anzahl = MaxWert + 1;

        /// <summary>
        /// Ergebnis aus dem ENDGÜLTIGEN Treffer (nach Ausweichen, Rüstung, Block, Absorption). Vorrang: Ausgewichen → Tick
        /// (periodisch/Umwelt, auch wenn absorbiert – Ticks sind nie blockbar und sollen keine Schlag- oder Schildreaktion
        /// neu starten; die absorbierte Menge steht in <see cref="DamageInfo.Absorbed"/>) → Block (voll, wenn kein Schaden
        /// ankam – auch wenn ein Aschenschild den Rest nahm) → Absorption → Schaden → Keine.
        /// </summary>
        public static TrefferReaktion Ableiten(in DamageInfo info)
        {
            if (info.Dodged) return TrefferReaktion.Ausgewichen;
            if (info.Periodisch || info.Environmental) return TrefferReaktion.Tick;
            bool kamAn = info.Amount > 0f;
            if (info.Blocked > 0f) return kamAn ? TrefferReaktion.BlockTeil : TrefferReaktion.BlockVoll;
            if (info.Absorbed > 0f) return kamAn ? TrefferReaktion.AbsorbTeil : TrefferReaktion.AbsorbVoll;
            return kamAn ? TrefferReaktion.Schaden : TrefferReaktion.Keine;
        }

        /// <summary>Netzwert lesen: unbekannte (neuere) Werte werden zu <see cref="TrefferReaktion.Keine"/>, nie zu einem falschen Ergebnis.</summary>
        public static TrefferReaktion AusNetz(byte wert) => wert <= MaxWert ? (TrefferReaktion)wert : TrefferReaktion.Keine;

        /// <summary>
        /// Reiner Client: übertragene Felder in den nachgebauten Treffer übernehmen (NetworkHealth.ObserversDamaged). Danach
        /// liefert <see cref="Ableiten"/> auf dem Client dasselbe Ergebnis wie auf dem Server: Ausweichen, Block, Absorption und
        /// Menge kommen einzeln mit, die Periodisch/Umwelt-Kennung steckt im Ergebnis selbst (Tick).
        /// </summary>
        public static void UebernimmVomServer(ref DamageInfo info, byte reaktion, float absorbiert)
        {
            info.Absorbed = Mathf.Max(0f, absorbiert);
            info.Reaktion = AusNetz(reaktion);
            info.Periodisch = info.Reaktion == TrefferReaktion.Tick;
        }

        /// <summary>
        /// Darf die Darstellung für dieses Ergebnis eine Körperreaktion zeigen? Nein bei Tod oder echtem Kontrollzustand
        /// (Vorrang, bestehende Todes-/Kontrollregeln), bei Keine und bei Verfehlt (Ziel behält seine Tätigkeit).
        /// </summary>
        public static bool Darstellbar(TrefferReaktion r, bool tot, bool unterKontrolle)
        {
            if (tot || unterKontrolle) return false;
            return r != TrefferReaktion.Keine && r != TrefferReaktion.Verfehlt;
        }

        /// <summary>
        /// Host und Clients: ein Angriff auf dieses Ziel hat verfehlt (es gibt dazu kein Damaged-Ereignis). Nur Darstellung und
        /// Prüfungen; ausgelöst von NetworkHealth.ObserversMissed auf jedem Beobachter genau einmal.
        /// </summary>
        public static event Action<GameObject> Verfehlt;

        public static void MeldeVerfehlt(GameObject ziel)
        {
            if (ziel != null) Verfehlt?.Invoke(ziel);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Verfehlt = null;
    }
}
