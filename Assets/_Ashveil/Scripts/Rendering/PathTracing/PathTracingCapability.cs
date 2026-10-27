using UnityEngine;
using UnityEngine.Rendering;

namespace Ashveil.Rendering.PathTracing
{
    /// <summary>
    /// Pathtracing-Faehigkeit dieses Geraets (Master-Plan §4). Ohne Raytracing-Shader (z. B. DirectX 11 oder aeltere
    /// Karten) bleibt der Pathtracer aus und der normale Rasterrenderer laeuft unveraendert weiter.
    /// Nicht an einen Hersteller gebunden: gefragt wird nur, was Unity fuer die laufende API meldet.
    /// </summary>
    public static class PathTracingCapability
    {
        public static bool IsSupported =>
            SystemInfo.supportsRayTracingShaders && SystemInfo.supportsComputeShaders &&
            SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        public static string Describe()
        {
            if (IsSupported)
                return $"verfuegbar ({SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsDeviceName})";
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11)
                return "nicht verfuegbar: DirectX 11 kennt kein Raytracing (Spiel mit DirectX 12 starten)";
            if (!SystemInfo.supportsRayTracingShaders)
                return $"nicht verfuegbar: {SystemInfo.graphicsDeviceName} meldet unter {SystemInfo.graphicsDeviceType} keine Raytracing-Shader";
            return "nicht verfuegbar: keine Compute-Shader";
        }
    }
}
