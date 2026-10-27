using Ashveil.Gameplay.CameraSystem;
using UnityEngine;

namespace Ashveil.Gameplay.Story
{
    /// <summary>Die eigene Figur (die, der die Kamera folgt) – fuer lokale Darstellung (Farmer, Tiere schauen her).</summary>
    public static class LocalPlayerRef
    {
        private static MMOCameraController _cc;

        public static Transform Transform
        {
            get
            {
                if (_cc == null)
                {
                    Camera cam = Camera.main;
                    if (cam != null) _cc = cam.GetComponent<MMOCameraController>();
                }
                return _cc != null ? _cc.Target : null;
            }
        }
    }
}
