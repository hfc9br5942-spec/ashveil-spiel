using UnityEngine;

namespace Ashveil.World
{
    /// <summary>
    /// Editor-Hilfe: zeichnet einen gemessenen Weg (z. B. den Suchweg der Pferde-Geschichte) als Linie mit Etappenpunkten
    /// in die Szene. Laenge je Etappe steht im Bericht des Messwerkzeugs; hier nur die Anzeige.
    /// </summary>
    public sealed class TrailGizmo : MonoBehaviour
    {
        [SerializeField] private Vector3[] points = new Vector3[0];
        [SerializeField] private Vector3[] stops = new Vector3[0];
        [SerializeField] private Color color = new Color(1f, 0.6f, 0.2f, 0.9f);
        [SerializeField] private float totalMeters;

        public float TotalMeters => totalMeters;

        public void Configure(Vector3[] path, Vector3[] legStops, Color c, float meters)
        {
            points = path ?? new Vector3[0];
            stops = legStops ?? new Vector3[0];
            color = c;
            totalMeters = meters;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = color;
            for (int i = 1; i < points.Length; i++) Gizmos.DrawLine(points[i - 1] + Vector3.up * 0.6f, points[i] + Vector3.up * 0.6f);
            foreach (Vector3 s in stops) Gizmos.DrawWireSphere(s + Vector3.up * 0.6f, 3f);
        }
    }
}
