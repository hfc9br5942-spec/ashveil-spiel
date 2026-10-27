using Ashveil.World;
using UnityEngine;

namespace Ashveil.Rendering
{
    /// <summary>
    /// Stimmung unter Tage: Sobald die Kamera in einem Dungeon-Instanzplatz (<see cref="DungeonSlot"/>) unter Tage ist, geht die
    /// Sonne aus, das Umgebungslicht wird dunkel, der Nebel wird zu gleichmaessigem Grubenstaub und der Himmel schwarz.
    /// Licht kommt dann nur noch von Grubenlampen und leuchtendem Salz. Beim Verlassen wird alles wiederhergestellt.
    /// Reine Darstellung auf dem eigenen Rechner – entsteht beim Spielstart von selbst.
    /// </summary>
    public sealed class IndoorAtmosphere : MonoBehaviour
    {
        public static IndoorAtmosphere Instance { get; private set; }

        /// <summary>Ist die eigene Kamera gerade unter Tage? (fuer Tests und Oberflaeche)</summary>
        public static bool IsIndoors { get; private set; }
        public static DungeonSlot CurrentSlot { get; private set; }

        private bool _saved;
        private Light _sun;
        private bool _sunEnabled;
        private Color _sky, _equator, _ground;
        private UnityEngine.Rendering.AmbientMode _ambientMode;
        private Camera _cam;
        private CameraClearFlags _clear;
        private Color _background;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("Unter Tage (Stimmung)");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<IndoorAtmosphere>();
        }

        private void OnDestroy()
        {
            if (IsIndoors) Leave();
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            DungeonSlot slot = cam != null && DungeonSlot.All.Count > 0 ? DungeonSlot.At(cam.transform.position) : null;
            if (slot != null && !slot.IsUnderground) slot = null; // Freiluft-Instanz (Kolosseum): normaler Himmel
            if (slot != null && !IsIndoors) Enter(cam, slot);
            else if (slot == null && IsIndoors) Leave();
            else if (slot != null && slot != CurrentSlot) Enter(cam, slot);
        }

        private void Enter(Camera cam, DungeonSlot slot)
        {
            if (!IsIndoors)
            {
                _sun = RenderSettings.sun;
                _sunEnabled = _sun != null && _sun.enabled;
                _ambientMode = RenderSettings.ambientMode;
                _sky = RenderSettings.ambientSkyColor;
                _equator = RenderSettings.ambientEquatorColor;
                _ground = RenderSettings.ambientGroundColor;
                _cam = cam;
                _clear = cam.clearFlags;
                _background = cam.backgroundColor;
                _saved = true;
            }
            IsIndoors = true;
            CurrentSlot = slot;
            if (_sun != null) _sun.enabled = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = slot.AmbientSky;
            RenderSettings.ambientEquatorColor = slot.AmbientEquator;
            RenderSettings.ambientGroundColor = slot.AmbientGround;
            AtmosphereController.IndoorOverride = true;
            AtmosphereController.IndoorFogColor = slot.FogColor;
            AtmosphereController.IndoorFogDensity = slot.FogDensity;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
            }
        }

        private void Leave()
        {
            IsIndoors = false;
            CurrentSlot = null;
            AtmosphereController.IndoorOverride = false;
            if (!_saved) return;
            if (_sun != null) _sun.enabled = _sunEnabled;
            RenderSettings.ambientMode = _ambientMode;
            RenderSettings.ambientSkyColor = _sky;
            RenderSettings.ambientEquatorColor = _equator;
            RenderSettings.ambientGroundColor = _ground;
            if (_cam != null)
            {
                _cam.clearFlags = _clear;
                _cam.backgroundColor = _background;
            }
            _saved = false;
        }
    }
}
