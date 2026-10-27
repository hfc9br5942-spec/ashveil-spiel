using System;
using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>Waffenklasse – bestimmt Animationsset und welche Faehigkeiten passen.</summary>
    public enum WeaponClass
    {
        Sword, TwoHandedSword, Axe, TwoHandedAxe, Spear, Dagger, Hammer, Mace,
        Bow, Crossbow,
        Pistol, Revolver, Rifle, Shotgun, HeavyGun,
        Staff, Focus, Artifact, AetherGun
    }

    /// <summary>Wie eine Waffe trifft. Neue Waffen kombinieren nur vorhandene Archetypen – kein neuer Code.</summary>
    public enum WeaponArchetype
    {
        /// <summary>Nahkampfbogen vor dem Charakter.</summary>
        MeleeArc,
        /// <summary>Sofortiger Strahl (Pistole, Gewehr).</summary>
        Hitscan,
        /// <summary>Mehrere Strahlen mit Streuung (Donnerbuechse).</summary>
        Spread,
        /// <summary>Fliegendes Geschoss (Pfeil, Bolzen): Zielen, Loslassen, Flugzeit; mit Ziel wie WoW (trifft beim Eintreffen).</summary>
        Projectile,
        /// <summary>Zauber (Stab): Wirkzeit, dann ein magisches Geschoss – keine Munition, kein Nahkampf.</summary>
        Spell
    }

    /// <summary>Was ein Angriff verbraucht.</summary>
    public enum WeaponResource
    {
        None,
        /// <summary>Magazin + Nachladen.</summary>
        Ammo,
        /// <summary>Aether (magische Waffen, spaeter).</summary>
        Aether,
        Stamina
    }

    /// <summary>
    /// Ein einzelner Schlag/Schuss mit Timing. Nahkampf-Kombos sind eine Liste davon.
    ///   Windup   : Ausholen, bevor der Treffer zaehlt
    ///   Active   : Zeitfenster, in dem der Treffer passiert (Nahkampf)
    ///   Recovery : Nachlauf, bevor der naechste Angriff beginnen darf
    /// </summary>
    [Serializable]
    public struct AttackStep
    {
        public string name;
        [Min(0f)] public float damageMultiplier;
        [Min(0f)] public float windup;
        [Min(0f)] public float active;
        [Min(0f)] public float recovery;
        [Tooltip("Nur Nahkampf: Breite des Treffbogens in Grad")]
        [Range(10f, 360f)] public float arcAngle;
        [Tooltip("Kurzes Einfrieren beim Treffer (s) – macht schwere Schlaege spuerbar")]
        [Range(0f, 0.15f)] public float hitStop;
        [Tooltip("Kamera-Erschuetterung bei Treffer, 0 = keine")]
        [Range(0f, 1f)] public float impact;
        [Tooltip("Rueckstoss auf das Ziel in m")]
        [Range(0f, 2f)] public float knockback;

        public float Duration => windup + active + recovery;

        public static AttackStep Create(string name, float mult, float windup, float active, float recovery,
            float arc = 110f, float hitStop = 0.04f, float impact = 0f, float knockback = 0.1f)
        {
            return new AttackStep
            {
                name = name, damageMultiplier = mult, windup = windup, active = active, recovery = recovery,
                arcAngle = arc, hitStop = hitStop, impact = impact, knockback = knockback
            };
        }
    }
}
