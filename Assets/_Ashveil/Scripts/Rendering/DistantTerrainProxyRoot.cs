using UnityEngine;
using UnityEngine.Rendering;

namespace Ashveil.Rendering
{
    /// <summary>
    /// Fernansicht der Makrowelt (Terrain-Vorgabe §64–66): haelt die groben Gelaende-Proxies fuer JEDE Kamera sichtbar,
    /// auch weit hinter der Sichtweite. Der Knoten wird um die Kamera herum verkleinert (Punkt p erscheint bei
    /// cam + s·(p − cam) – gleiche Richtung, gleicher Winkel), der Shader "Ashveil/DistantTerrain" legt die Tiefe
    /// hinter alle echte Geometrie. So bleibt die Welt hinter dem Horizont sichtbar, ohne 40 km Terrain zu laden.
    /// Ausserhalb der Makrowelt (z. B. Instanz-Plaetze) blendet sich die Fernansicht aus.
    /// </summary>
    [ExecuteAlways]
    public sealed class DistantTerrainProxyRoot : MonoBehaviour
    {
        [Tooltip("Makrowelt x/z (min) + Groesse; nur innerhalb (plus Rand) sichtbar")] public Rect worldBounds = new Rect(-14200f, -14350f, 38000f, 26000f);
        [Tooltip("Weiteste Proxy-Entfernung (m) – bestimmt die Verkleinerung")] public float maxDistance = 60000f;
        [Tooltip("Dunst-Entfernung (m) der Luftperspektive")] public float hazeDistance = 26000f;
        [Tooltip("Rand um die Makrowelt, in dem die Fernansicht noch gilt (m)")] public float boundsMargin = 3000f;

        private static readonly int ParamsId = Shader.PropertyToID("_AshProxyParams");

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += OnBeginCamera;

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            transform.localScale = Vector3.one;
            transform.position = Vector3.zero;
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam == null || cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
            Vector3 c = cam.transform.position;
            Rect r = worldBounds;
            bool inside = c.x > r.xMin - boundsMargin && c.x < r.xMax + boundsMargin && c.z > r.yMin - boundsMargin && c.z < r.yMax + boundsMargin;
            float far = Mathf.Max(50f, cam.farClipPlane);
            float s = inside && !cam.orthographic ?Mathf.Min(1f, far * 0.8f / Mathf.Max(1f, maxDistance)) : 0f;
            transform.localScale = Vector3.one * Mathf.Max(s, 1e-7f);
            transform.position = c - c * s;
            float eps = 0.5f * Mathf.Max(0.01f, cam.nearClipPlane) / far;
            Shader.SetGlobalVector(ParamsId, new Vector4(Mathf.Max(s, 1e-7f), eps, maxDistance, hazeDistance));
        }
    }
}
