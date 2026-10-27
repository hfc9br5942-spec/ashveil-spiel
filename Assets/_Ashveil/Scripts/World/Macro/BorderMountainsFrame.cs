using UnityEngine;

namespace Ashveil.World
{
    /// <summary>
    /// Lage des handgebauten Grenzgebirge-Kerns in der Welt (Max 30.09.: "Gebautes nach Sueden verlegen", Weltkarte).
    /// Der Kern ist im "Entwurfsraum" gebaut (x 600…3000, z −350…2050: Westkante = Seite zum Tiefland, Ostkante = Seite
    /// zur Bruchmark). In der Welt liegt er suedoestlich von Kareth, am Ostende des Grenzgebirges, um 90° gedreht:
    /// Entwurf-Ost → Welt-Sued (Bruchmark), Entwurf-Nord → Welt-Ost. Der Talpass zeigt damit wie auf der Karte nach Sueden.
    /// Alle Stellen, die Grenzgebirge-Koordinaten kennen (Builder, Tests, Blickpunkte), rechnen ueber diese Klasse.
    /// </summary>
    public static class BorderMountainsFrame
    {
        /// <summary>Entwurfsraum: Suedwest-Ecke des Kerns.</summary>
        public static readonly Vector2 LocalPivot = new Vector2(600f, -350f);
        /// <summary>Welt: dort landet die Entwurf-Suedwest-Ecke (Nordwest-Ecke des Kerns in der Welt). Kachelraster von Kareth.</summary>
        public static readonly Vector2 WorldPivot = new Vector2(3000f, -1550f);
        /// <summary>Drehung (Grad, Unity-Yaw) vom Entwurfs- in den Weltraum.</summary>
        public const float Yaw = 90f;

        /// <summary>Weltflaeche des Kerns (x/z min + Groesse).</summary>
        public static Rect WorldRect => new Rect(WorldPivot.x, WorldPivot.y - 2400f, 2400f, 2400f);

        public static Vector2 ToWorld(Vector2 l) => new Vector2(WorldPivot.x + (l.y - LocalPivot.y), WorldPivot.y - (l.x - LocalPivot.x));
        public static Vector2 ToWorld(float lx, float lz) => ToWorld(new Vector2(lx, lz));
        public static Vector3 ToWorld(Vector3 l) { Vector2 w = ToWorld(new Vector2(l.x, l.z)); return new Vector3(w.x, l.y, w.y); }

        public static Vector2 ToLocal(Vector2 w) => new Vector2(LocalPivot.x - (w.y - WorldPivot.y), LocalPivot.y + (w.x - WorldPivot.x));
        public static Vector3 ToLocal(Vector3 w) { Vector2 l = ToLocal(new Vector2(w.x, w.z)); return new Vector3(l.x, w.y, l.y); }

        /// <summary>Richtung (Yaw in Grad) aus dem Entwurfs- in den Weltraum.</summary>
        public static float YawToWorld(float localYaw) => localYaw + Yaw;

        /// <summary>Wurzel-Transform fuer Inhalte im Entwurfsraum: Weltpunkt = Position + Drehung · Entwurfspunkt.</summary>
        public static Vector3 RootPosition => new Vector3(WorldPivot.x - LocalPivot.y, 0f, WorldPivot.y + LocalPivot.x);
        public static Quaternion RootRotation => Quaternion.Euler(0f, Yaw, 0f);
    }
}
