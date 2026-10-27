using UnityEngine;

namespace Ashveil.World
{
    /// <summary>
    /// Behälter für die zusammengefassten Felskacheln einer Region (Netze je Kachel und Detailstufe als Unterobjekte).
    /// Wird vom Szenenbau (FelsKit, Editor) angelegt. Binär gespeichert, obwohl das Projekt sonst Text speichert:
    /// Netze als Text (Hex) sind doppelt so groß und langsam zu laden (05.10.2026: Felssatz Kareth, ≈ 3 Mio. Dreiecke).
    /// Das Hauptobjekt einer .asset-Datei bestimmt deren Format – darum ist dieser Behälter das Hauptobjekt.
    /// </summary>
    [PreferBinarySerialization]
    public sealed class FelsKachelSatz : ScriptableObject
    {
        [Tooltip("Woraus die Felsen stammen (Felssatz-Version, Blender, Datum).")]
        public string quelle = "";
        public int felsen;
        public int kacheln;
        [Tooltip("Dreiecke je Kachel-Detailstufe (LOD0, LOD1, LOD2) und der Kollision, über alle Kacheln.")]
        public long[] dreiecke = new long[4];
        [Tooltip("Bildhöhenanteile der Kachel-LODs (LOD0→1, LOD1→2, aus).")]
        public float[] lodHoehen = new float[3];
    }
}
