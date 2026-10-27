using UnityEngine;

namespace Ashveil.Rendering.PathTracing
{
    /// <summary>
    /// Optionale Pathtracing-Angaben an einem Objekt, die URP-Materialien nicht kennen: echtes Glas/Eis/Kristall
    /// (Brechzahl, Faerbung nach Weglaenge), duenne Durchlaessigkeit (Blaetter, Stoffbahnen) oder Ausschluss.
    /// Der Rasterrenderer ignoriert die Komponente.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PathTraceSurface : MonoBehaviour
    {
        public enum Kind { WieMaterial, Glas, DuennDurchlaessig, Ausschliessen }

        public Kind kind = Kind.WieMaterial;
        [Tooltip("Brechzahl: Wasser 1,33 · Eis 1,31 · Glas 1,5 · Kristall 1,55–2,0")]
        [Range(1f, 2.5f)] public float ior = 1.5f;
        [Tooltip("Farbe nach 'absorptionDistance' Metern Weg im Innern (Beer-Lambert)")]
        public Color absorptionColor = Color.white;
        [Min(0.001f)] public float absorptionDistance = 1f;
        [Tooltip("Anteil des Lichts, der eine duenne Flaeche diffus durchdringt")]
        [Range(0f, 1f)] public float transmission = 0.3f;

        /// <summary>Absorptionskoeffizient je Meter aus Farbe und Strecke: c = exp(-sigma·d) → sigma = -ln(c)/d.</summary>
        public Vector3 Absorption
        {
            get
            {
                Color c = absorptionColor.linear;
                float d = Mathf.Max(absorptionDistance, 0.001f);
                return new Vector3(-Mathf.Log(Mathf.Clamp(c.r, 1e-4f, 1f)) / d, -Mathf.Log(Mathf.Clamp(c.g, 1e-4f, 1f)) / d,
                                   -Mathf.Log(Mathf.Clamp(c.b, 1e-4f, 1f)) / d);
            }
        }

        public int Hash()
        {
            unchecked
            {
                int h = (int)kind * 397;
                h = h * 31 + ior.GetHashCode();
                h = h * 31 + absorptionColor.GetHashCode();
                h = h * 31 + absorptionDistance.GetHashCode();
                h = h * 31 + transmission.GetHashCode();
                return h;
            }
        }
    }

    /// <summary>
    /// Physische Groesse einer Lichtquelle fuer den Pathtracer (weiche Schatten, trefferbar fuer MIS).
    /// Ohne diese Komponente: Feuer (LightFlicker) 0,2 m, sonst 0,05 m.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    public sealed class PathTraceLight : MonoBehaviour
    {
        [Min(0.005f)] public float radius = 0.1f;
        [Tooltip("Leuchtkugel direkt im Bild zeigen (sonst nur ihr Licht; Flammen kommen aus Partikeln bzw. Proxys)")]
        public bool visibleToCamera;
        [Tooltip("Im Pathtracer nicht verwenden (z. B. reine Aufhelllichter, die es physikalisch nicht gibt)")]
        public bool exclude;
    }
}
