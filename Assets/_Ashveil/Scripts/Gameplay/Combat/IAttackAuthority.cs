using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Gameplay.Combat
{
    /// <summary>Art eines Angriffs, wie sie ans Netz geht (entspricht Weapons.AttackKind).</summary>
    public enum AttackIntentKind : byte
    {
        Combo = 0,
        Special = 1,
        Air = 2,
        Plunge = 3
    }

    /// <summary>
    /// Wer ueber Schaden entscheidet. Ist eine solche Komponente am Spieler vorhanden und aktiv, rechnet der
    /// Client Treffer nur noch VORAUS (Funken, Hitstop, Klang – sofortiges Gefuehl) und meldet die Absicht;
    /// den echten Schaden berechnet der Server. Ohne diese Komponente (reines Einzelspieler-Testen)
    /// bleibt alles wie bisher lokal.
    /// </summary>
    public interface IAttackAuthority
    {
        /// <summary>true = Schaden entscheidet jemand anderes (Server).</summary>
        bool IsRemoteAuthority { get; }

        void SubmitMelee(AttackIntentKind kind, int comboIndex, Vector3 origin, Vector3 forward);
        void SubmitShot(AttackIntentKind kind, int comboIndex, Vector3 muzzle, IReadOnlyList<Vector3> directions);
        /// <summary>Nachladen begonnen – der Server zaehlt das Magazin selbst.</summary>
        void SubmitReload();
        /// <summary>Pfeil/Bolzen/Zauber losgelassen. Mit Ziel fliegt es sicher dorthin (WoW), sonst frei mit Schwerkraft.</summary>
        void SubmitProjectile(AttackIntentKind kind, int comboIndex, Vector3 origin, Vector3 direction, CombatTarget target);
    }
}
