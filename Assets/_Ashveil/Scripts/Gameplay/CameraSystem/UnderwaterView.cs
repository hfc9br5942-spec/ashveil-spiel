using Ashveil.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashveil.Gameplay.CameraSystem
{
    /// <summary>
    /// Unterwassersicht (MOV-169, Welle 0, Greybox): Liegt die Kamera unter einem Wasserspiegel (<see cref="WaterVolume"/>),
    /// wird der Nebel dicht und wassergrün; taucht sie auf, kommen die Nebelwerte der Szene (Stil A) unverändert zurück.
    /// Nur Darstellung auf diesem Rechner, kein Gameplay. Aufgerufen von <see cref="MMOCameraController"/> nach dem Setzen
    /// der Kameraposition und vom Fotomodus, solange er die Kamera selbst führt.
    /// </summary>
    public static class UnderwaterView
    {
        public static readonly Color FogColor = new Color(0.16f, 0.27f, 0.27f);
        public const float FogDensity = 0.09f;

        private static bool _under;
        private static bool _savedFog;
        private static Color _savedColor;
        private static float _savedDensity;
        private static FogMode _savedMode;

        public static bool IsUnderwater => _under;

        public static void Apply(Vector3 cameraPosition)
        {
            bool under = WaterVolume.TrySurface(cameraPosition, out float surface, 0f) && cameraPosition.y < surface - 0.05f;
            if (under == _under) return;
            _under = under;
            if (under)
            {
                _savedFog = RenderSettings.fog;
                _savedColor = RenderSettings.fogColor;
                _savedDensity = RenderSettings.fogDensity;
                _savedMode = RenderSettings.fogMode;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Exponential;
                RenderSettings.fogColor = FogColor;
                RenderSettings.fogDensity = FogDensity;
            }
            else
            {
                RenderSettings.fog = _savedFog;
                RenderSettings.fogColor = _savedColor;
                RenderSettings.fogDensity = _savedDensity;
                RenderSettings.fogMode = _savedMode;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _under = false;
            SceneManager.activeSceneChanged -= OnSceneChanged;
            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        // Neue Szene = eigene Nebelwerte: nichts zurückschreiben, nur den Merker lösen (sonst kämen die Werte der alten Szene).
        private static void OnSceneChanged(Scene from, Scene to) => _under = false;
    }
}
