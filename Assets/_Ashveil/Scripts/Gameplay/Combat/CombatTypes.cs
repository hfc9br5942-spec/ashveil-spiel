using UnityEngine;

namespace Ashveil.Gameplay.Combat
{
    /// <summary>Zugehoerigkeit – bestimmt Namensfarbe, ob man angreifen kann und wen Tab auswaehlt.</summary>
    public enum Faction
    {
        Player,
        Friendly,
        Neutral,
        Hostile
    }

    /// <summary>Ergebnis eines Treffers. Wird spaeter vom Server berechnet und an Clients verteilt.</summary>
    public struct DamageInfo
    {
        public float Amount;
        public bool IsCritical;
        public GameObject Source;
        public Vector3 HitPoint;
        /// <summary>Richtung vom Angreifer zum Ziel (normiert, horizontal).</summary>
        public Vector3 Direction;
        public float Knockback;
        public string WeaponId;
        /// <summary>Getroffenes Koerperteil (Boss-Anatomie); 0 = Koerper allgemein.</summary>
        public byte PartId;
        /// <summary>Vom Schild abgefangener Schaden (Schildblock); 0 = nicht geblockt.</summary>
        public float Blocked;
        /// <summary>Kann nicht geblockt werden (Flaechenangriffe, Gift, Brennen).</summary>
        public bool Unblockable;
        /// <summary>Nahkampfschlag: das Ziel kann ausweichen (WoW Classic – passiver Ausweichwert, nur von vorn).</summary>
        public bool Dodgeable;
        /// <summary>Ergebnis: ausgewichen, kein Schaden.</summary>
        public bool Dodged;
        /// <summary>Von einem Schild (Aschenschild) aufgefangener Schaden.</summary>
        public float Absorbed;
        /// <summary>Umweltschaden (Ertrinken): Rüstung mindert ihn nicht – wie in Classic (Max 06.10.2026).</summary>
        public bool Environmental;
        /// <summary>
        /// 22.10.2026 (Zerstückelung, COMBAT-026a): vom Server am tatsächlichen Treffer entschieden – tödlich, kritisch, Hiebwaffe.
        /// 0 = nichts abtrennen; sonst Trennpunkt der jeweiligen Gegnerdarstellung (Zerstueckelung.TrennpunktFuer).
        /// </summary>
        public byte Trennpunkt;
        /// <summary>
        /// 23.10.2026 (Kampfreaktionen, Paket C): periodischer Treffer (Gift, Brennen, Blutung – DamageOverTime). Vorher nur an der
        /// Waffen-Id erkennbar (".dot", aber nicht ".poison"/".bleed") und auf reinen Clients gar nicht (die Id geht nicht übers Netz).
        /// </summary>
        public bool Periodisch;
        /// <summary>
        /// 23.10.2026 (Kampfreaktionen, Paket C): Ergebnis für die Darstellung – Server/Host aus <see cref="TrefferReaktionen.Ableiten"/>
        /// in Health.ApplyDamage, reine Clients aus dem Treffer-RPC. Eine Entscheidung je Treffer, auf allen Rechnern gleich.
        /// </summary>
        public TrefferReaktion Reaktion;
    }

    /// <summary>
    /// Leitet einen Treffer auf ein Koerperteil um, bevor Leben abgezogen wird (Server). Gibt den Schaden fuers
    /// Hauptleben zurueck (z. B. Kopf x1,5) und setzt <see cref="DamageInfo.PartId"/> auf 0, wenn das Teil nicht
    /// erreichbar ist.
    /// </summary>
    public interface IDamageRouter
    {
        float RouteDamage(ref DamageInfo info);
    }

    /// <summary>Alles, was Schaden nehmen kann.</summary>
    public interface IDamageable
    {
        bool IsAlive { get; }
        void ApplyDamage(in DamageInfo info);
    }

    public static class FactionRules
    {
        /// <summary>Darf der Spieler dieses Ziel angreifen?</summary>
        public static bool IsAttackableByPlayer(Faction f) => f == Faction.Hostile || f == Faction.Neutral;

        public static Color NameColor(Faction f)
        {
            switch (f)
            {
                case Faction.Hostile: return new Color(1f, 0.3f, 0.22f);
                case Faction.Neutral: return new Color(1f, 0.85f, 0.2f);
                default: return new Color(0.42f, 0.95f, 0.42f);
            }
        }
    }
}
