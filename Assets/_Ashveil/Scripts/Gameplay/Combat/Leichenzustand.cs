using System;

namespace Ashveil.Gameplay.Combat
{
    /// <summary>
    /// 23.10.2026 (Paket C, Codex-Nachprüfung 1 „später Beobachter“): dauerhafter Leichenzustand eines Netzobjekts für die Darstellung.
    /// Umgesetzt von NetworkHealth (Netz-Baugruppe) – die Gegnerdarstellung (Gameplay) kennt die Netz-Baugruppe nicht und holt den
    /// Zustand über dieses Interface: <c>GetComponentInParent&lt;ILeichenzustand&gt;()</c>.
    ///
    /// <see cref="Trennpunkt"/>: vom Server beim tödlichen Treffer gesetzt (Zerstueckelung.TrennpunktFuer, derselbe Wert wie
    /// DamageInfo.Trennpunkt im Treffer-RPC), 0 nach Wiederbeleben/Zurücksetzen/neuem Spawn. Wer die Figur erst NACH dem Tod zu
    /// sehen bekommt (Beobachterbeginn, Wiederverbinden), liest hier schon beim Spawn den Wert – der Treffer-RPC ist nicht gepuffert.
    /// Reihenfolge Treffer-RPC / Zustand ist im Netz nicht garantiert: die Darstellung trennt einmal, egal welches zuerst kommt.
    /// </summary>
    public interface ILeichenzustand
    {
        /// <summary>Trennpunkt der Leiche (0 = nicht zerstückelt / lebt). Auf Host und Clients lesbar.</summary>
        byte Trennpunkt { get; }

        /// <summary>
        /// Darstellungsseite (Host und Clients, nicht auf einem reinen Server): (alt, neu). Feuert genau einmal je Wertwechsel,
        /// auch beim Beobachterbeginn mit schon gesetztem Wert (0 → Wert, direkt nach dem Spawn der Client-Kopie).
        /// </summary>
        event Action<byte, byte> TrennpunktGeaendert;
    }
}
