using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Core
{
    /// <summary>
    /// Wasserfläche zum Schwimmen und Tauchen (MOV-169, Welle 0). Rechteck in x/z mit einem Wasserspiegel; der Grund ist
    /// das Gelände darunter – flaches Wasser zum Waten ergibt sich von selbst. Der Bewegungsmotor fragt
    /// <see cref="TrySurface"/> in jedem Schritt ab: auf Server und Client mit denselben Szenendaten, also deterministisch
    /// (Vorhersage und Abgleich). Keine Suche pro Frame (MOV-015): eine kleine Liste registrierter Flächen.
    /// Liegt in Ashveil.Core, damit Gameplay (Motor, Kamera) und Welt (Szenenbau) sie ohne gegenseitige Abhängigkeit kennen.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaterVolume : MonoBehaviour
    {
        /// <summary>Grunddaten einer Wasserfläche (Welt-Koordinaten), auch ohne Szene prüfbar.</summary>
        public struct Area
        {
            public float MinX, MaxX, MinZ, MaxZ, Surface;

            public Area(Rect xz, float surface)
            {
                MinX = xz.xMin; MaxX = xz.xMax; MinZ = xz.yMin; MaxZ = xz.yMax; Surface = surface;
            }

            public bool ContainsXZ(Vector3 p) => p.x >= MinX && p.x <= MaxX && p.z >= MinZ && p.z <= MaxZ;
        }

        [Tooltip("Ausdehnung in x/z (Welt). Leer (Breite 0) = aus den Bounds des Renderers auf diesem Objekt.")]
        [SerializeField] private Rect area;
        [Tooltip("Wasserspiegel relativ zur Objekthöhe (m)")]
        [SerializeField] private float surfaceOffset;

        private static readonly List<WaterVolume> Registered = new List<WaterVolume>();
        private static readonly List<Area> Areas = new List<Area>();

        public static IReadOnlyList<WaterVolume> All => Registered;
        public float SurfaceY => transform.position.y + surfaceOffset;
        public Rect AreaXZ => area;

        /// <summary>Szenenbau: Fläche festlegen (Welt-Rechteck x/z, Spiegel = Objekthöhe + offset).</summary>
        public void Configure(Rect xz, float offset = 0f)
        {
            area = xz;
            surfaceOffset = offset;
            if (isActiveAndEnabled) Rebuild();
        }

        private void OnEnable()
        {
            if (area.width <= 0f || area.height <= 0f)
            {
                var r = GetComponent<Renderer>();
                if (r != null) area = Rect.MinMaxRect(r.bounds.min.x, r.bounds.min.z, r.bounds.max.x, r.bounds.max.z);
            }
            if (!Registered.Contains(this)) Registered.Add(this);
            Rebuild();
        }

        private void OnDisable()
        {
            Registered.Remove(this);
            Rebuild();
        }

        // Werte im Inspektor geändert: Liste neu aufbauen. Fläche und Spiegel werden beim Einschalten übernommen –
        // wer das Objekt zur Laufzeit verschiebt, ruft danach Configure() (Wasser steht in Welle 0 still).
        private void OnValidate()
        {
            if (isActiveAndEnabled && Registered.Contains(this)) Rebuild();
        }

        private static void Rebuild()
        {
            Areas.Clear();
            foreach (WaterVolume w in Registered)
                if (w != null) Areas.Add(new Area(w.area, w.SurfaceY));
        }

        /// <summary>
        /// Wasserspiegel über diesem Punkt? Liegt der Punkt in mehreren Flächen, gilt der höchste Spiegel, der nicht mehr
        /// als <paramref name="above"/> m unter dem Punkt liegt (sonst wäre man über einer tieferen Fläche „im Wasser“).
        /// </summary>
        public static bool TrySurface(Vector3 position, out float surfaceY, float above = 50f) => TrySurface(Areas, position, out surfaceY, above);

        /// <summary>Reine Regel (Tests, Werkzeuge): Wasserspiegel über einem Punkt in einer Liste von Flächen.</summary>
        public static bool TrySurface(IReadOnlyList<Area> areas, Vector3 position, out float surfaceY, float above = 50f)
        {
            surfaceY = float.MinValue;
            bool found = false;
            for (int i = 0; i < areas.Count; i++)
            {
                Area a = areas[i];
                if (!a.ContainsXZ(position) || a.Surface < position.y - above) continue;
                if (!found || a.Surface > surfaceY)
                {
                    surfaceY = a.Surface;
                    found = true;
                }
            }
            return found;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Registered.Clear();
            Areas.Clear();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.5f, 1f, 0.5f);
            Gizmos.DrawWireCube(new Vector3(area.center.x, SurfaceY, area.center.y), new Vector3(area.width, 0.05f, area.height));
        }
    }
}
