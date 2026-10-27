using System;
using System.Collections;
using System.IO;
using System.Text;
using Ashveil.Core.Settings;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Ashveil.Rendering
{
    /// <summary>
    /// Visual Clarity Gate (02.10.2026) – Messwerkzeug, nur aktiv mit Kommandozeile "-ashveil-clarity &lt;Ordner&gt;".
    /// Schreibt Ausgabe- und Renderaufloesung, Desktopaufloesung, Anzeigemodus, Render-Skalierung, Kantenglaettung und
    /// Grafikstufe, wartet bis die Welt steht, speichert ein Bildschirmfoto in Ausgabegroesse und beendet das Spiel.
    /// "-ashveil-clarity-label &lt;Name&gt;" benennt Bild und Bericht. Im normalen Spiel ohne Wirkung.
    /// </summary>
    public sealed class ClarityProbe : MonoBehaviour
    {
        private const float WaitSeconds = 28f;
        private string _dir, _label;
        private readonly StringBuilder _report = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            string dir = Arg("-ashveil-clarity");
            if (string.IsNullOrEmpty(dir) || Application.isEditor) return;
            var go = new GameObject("ClarityProbe");
            DontDestroyOnLoad(go);
            var p = go.AddComponent<ClarityProbe>();
            p._dir = dir;
            p._label = Arg("-ashveil-clarity-label");
            if (string.IsNullOrEmpty(p._label)) p._label = "standard";
        }

        private IEnumerator Start()
        {
            Snapshot("Start (vor dem Anwenden der Grafikeinstellungen)");
            yield return new WaitForSecondsRealtime(WaitSeconds);
            Snapshot("Nach " + WaitSeconds + " s (Welt geladen, Einstellungen angewandt)");
            yield return new WaitForEndOfFrame();
            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                Directory.CreateDirectory(_dir);
                File.WriteAllBytes(Path.Combine(_dir, $"clarity_{_label}.png"), shot.EncodeToPNG());
                _report.AppendLine($"Bildschirmfoto: {shot.width}×{shot.height} → clarity_{_label}.png");
                File.WriteAllText(Path.Combine(_dir, $"clarity_{_label}.txt"), _report.ToString());
            }
            catch (Exception e) { Debug.LogWarning("[Ashveil] ClarityProbe: " + e.Message); }
            Destroy(shot);
            Debug.Log("[Ashveil] ClarityProbe fertig: " + _label);
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        private void Snapshot(string title)
        {
            Display d = Display.main;
            GraphicsOptions o = GraphicsSettingsStore.Current;
            Camera cam = Camera.main;
            var data = cam != null ? cam.GetUniversalAdditionalCameraData() : null;
            GraphicsApplier a = GraphicsApplier.Instance;
            _report.AppendLine($"[{title}]");
            _report.AppendLine($"  Ausgabe (Screen):          {Screen.width}×{Screen.height}");
            _report.AppendLine($"  Screen.currentResolution:  {Screen.currentResolution.width}×{Screen.currentResolution.height} @ {Screen.currentResolution.refreshRateRatio.value:0.##} Hz");
            _report.AppendLine($"  Desktop (Display.main):    {d.systemWidth}×{d.systemHeight} · Rendering {d.renderingWidth}×{d.renderingHeight}");
            _report.AppendLine($"  Anzeigemodus:              {Screen.fullScreenMode} (fullScreen={Screen.fullScreen})");
            _report.AppendLine($"  Kamera-Pixel:              {(cam != null ? cam.pixelWidth + "×" + cam.pixelHeight : "keine Kamera")}");
            _report.AppendLine($"  URP Render-Skalierung:     {(a != null ? a.PipelineRenderScale.ToString("0.00") : "?")} · MSAA {(a != null ? a.PipelineMsaa : -1)}");
            _report.AppendLine($"  Kantenglaettung (Kamera):  {(data != null ? data.antialiasing + " " + data.antialiasingQuality : "?")}");
            _report.AppendLine($"  Grafik: Stufe {o?.tier}, Anzeigemodus-Index {o?.displayMode}, Aufloesung {o?.resolutionWidth}×{o?.resolutionHeight} (0 = nativ), Skalierung-Index {o?.renderScale}, AA-Index {o?.antiAliasing}, Nachbearbeitung {o?.postProcessing}");
            _report.AppendLine($"  Texturen: Mip-Limit {QualitySettings.globalTextureMipmapLimit}, Aniso {QualitySettings.anisotropicFiltering}");
            _report.AppendLine($"  GPU: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType})");
        }

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : "";
            return null;
        }
    }
}
