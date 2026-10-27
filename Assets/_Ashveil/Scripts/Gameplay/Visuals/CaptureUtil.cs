using System.IO;
using UnityEngine;

namespace Ashveil.Gameplay.Visuals
{
    /// <summary>Bilder fuer Tests/Berichte: rendert eine Kamera in eine PNG-Datei (ohne das Spielbild zu stoeren).</summary>
    public static class CaptureUtil
    {
        public static bool Capture(Camera cam, string path, int width, int height)
        {
            if (cam == null) return false;
            RenderTexture prevTarget = cam.targetTexture;
            RenderTexture prevActive = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.Destroy(tex);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Ashveil] Bild nicht gespeichert: " + e.Message);
                return false;
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
