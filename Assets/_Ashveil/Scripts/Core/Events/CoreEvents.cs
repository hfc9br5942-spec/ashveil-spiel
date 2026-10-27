using UnityEngine;

namespace Ashveil.Core.Events
{
    /// <summary>
    /// Bitte um eine Kamera-Erschuetterung an einem Weltpunkt (Schritte eines Riesen,
    /// Explosion, schwerer Treffer). Die Kamera entscheidet selbst anhand der Distanz,
    /// wie stark sie wackelt. Sparsam einsetzen.
    /// </summary>
    public struct CameraImpulseEvent
    {
        public Vector3 Origin;
        /// <summary>0..1, 1 = sehr stark direkt am Ursprung.</summary>
        public float Strength;
        /// <summary>Ab dieser Distanz (m) spuert man nichts mehr.</summary>
        public float Radius;

        public CameraImpulseEvent(Vector3 origin, float strength, float radius)
        {
            Origin = origin;
            Strength = strength;
            Radius = radius;
        }
    }

    public enum CombatTextKind
    {
        /// <summary>Schaden, den der lokale Spieler verursacht.</summary>
        Damage,
        /// <summary>Kritischer Treffer des lokalen Spielers.</summary>
        Critical,
        /// <summary>Schaden, den der lokale Spieler erleidet.</summary>
        DamageTaken,
        Heal,
        Miss,
        /// <summary>Fehlermeldung oben am Bildschirm ("Ausser Reichweite").</summary>
        Error
    }

    /// <summary>
    /// Anzeige fuer schwebenden Kampftext bzw. Fehlermeldungen. Gameplay sendet, UI zeigt an –
    /// dadurch kennt das Kampfsystem keine UI-Klassen.
    /// </summary>
    public struct CombatTextEvent
    {
        public Vector3 WorldPosition;
        public string Text;
        public CombatTextKind Kind;

        public CombatTextEvent(Vector3 worldPosition, string text, CombatTextKind kind)
        {
            WorldPosition = worldPosition;
            Text = text;
            Kind = kind;
        }

        public static void Error(string text)
        {
            EventBus<CombatTextEvent>.Raise(new CombatTextEvent(Vector3.zero, text, CombatTextKind.Error));
        }
    }

    /// <summary>Der lokal gesteuerte Spieler ist in der Welt erschienen.</summary>
    public struct LocalPlayerSpawnedEvent
    {
        public GameObject Player;

        public LocalPlayerSpawnedEvent(GameObject player)
        {
            Player = player;
        }
    }
}
