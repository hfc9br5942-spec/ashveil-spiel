using UnityEngine;

namespace Ashveil.Gameplay.Combat
{
    /// <summary>
    /// Welches Koerperteil ein Spieler anvisiert (Boss-Anatomie §4: Klick auf die Zone oder Taste
    /// „Naechster Schwachpunkt“). Liegt auf der Spielfigur; der Server setzt den Wert, wenn der Client seine Wahl
    /// meldet, und <see cref="CombatResolver"/> schreibt das Teil in den Treffer – nur fuer genau dieses Ziel.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BodyPartAim : MonoBehaviour
    {
        /// <summary>Ziel, auf das sich die Wahl bezieht (Server).</summary>
        public GameObject Target { get; private set; }
        /// <summary>Gewaehltes Teil (0 = Koerper).</summary>
        public byte Part { get; private set; }

        public void Set(GameObject target, byte part)
        {
            Target = target;
            Part = target != null ? part : (byte)0;
        }

        /// <summary>Teil fuer einen Treffer dieses Angreifers auf dieses Ziel (0, wenn nichts oder etwas anderes gewaehlt ist).</summary>
        public static byte PartFor(GameObject attacker, Component hit)
        {
            if (attacker == null || hit == null) return 0;
            var aim = attacker.GetComponent<BodyPartAim>();
            if (aim == null || aim.Target == null || aim.Part == 0) return 0;
            Health h = hit.GetComponentInParent<Health>();
            return h != null && h.gameObject == aim.Target ? aim.Part : (byte)0;
        }
    }
}
