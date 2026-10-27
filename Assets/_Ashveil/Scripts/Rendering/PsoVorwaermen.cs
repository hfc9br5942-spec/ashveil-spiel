using System.Diagnostics;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Ashveil.Rendering
{
    /// <summary>
    /// Shader-Varianten und Grafik-Zustände („PSO“) beim Spielstart vorwärmen (13.10.2026, Leistung Westweiden, Changelog (113):
    /// der letzte Hänger beim ersten Blick auf die Westweiden waren 186 ms <c>Shader.CreateGPUProgram</c> – 19 Varianten, die erst
    /// beim Hinsehen gebaut wurden). Grundlage ist eine Aufzeichnung aus dem fertigen Spiel (<c>FrameSpikeProfiler</c> zeichnet bei
    /// jeder Messung auf: Kareth, Reise, Westweiden, Kreidekeller) unter <c>Resources/Ashveil_Grafikzustaende_DX12</c>.
    /// Beim Start wird sie auf Hintergrund-Fäden aufgewärmt (<see cref="GraphicsStateCollection.WarmUp"/>, parallel, wenn die
    /// Grafikkarte das kann); ohne parallele Erzeugung in kleinen Portionen je Bild, damit kein eigener Hänger entsteht.
    /// Nur im fertigen Spiel mit Grafik – nicht im Editor (der baut Shader ohnehin nebenbei), nicht am Server, nicht bei Bots.
    /// Fehlt die Aufzeichnung oder passt sie nicht zur Grafik-Schnittstelle, passiert nichts (die Shader entstehen wie bisher beim
    /// ersten Gebrauch). Neue Aufzeichnung: Auftrag „ruckler_westweiden“, dann die neueste `Grafikzustaende_*.graphicsstate` aus
    /// `Builds/Spieltest_r2/Logs/Ashveil/Ruckler/` hierher kopieren.
    /// </summary>
    public static class PsoVorwaermen
    {
        public const string ResourcePfad = "Ashveil_Grafikzustaende_DX12";
        /// <summary>Portion je Bild, wenn die Grafikkarte nicht parallel erzeugen kann.</summary>
        private const int ProBild = 8;

        public static string Status { get; private set; } = "nicht gestartet";
        public static bool Fertig { get; private set; }
        public static float Sekunden { get; private set; }

        private static GraphicsStateCollection _gsc;
        private static JobHandle _job;
        private static Stopwatch _uhr;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Start()
        {
#if UNITY_EDITOR
            Status = "aus (Editor)";
#else
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Status = "aus (ohne Grafik)"; return; }
            if (System.Environment.CommandLine.Contains("-ashveil-ohne-vorwaermen")) { Status = "aus (Startargument)"; return; }
            _gsc = Resources.Load<GraphicsStateCollection>(ResourcePfad);
            if (_gsc == null) { Status = "keine Aufzeichnung"; return; }
            if (_gsc.graphicsDeviceType != SystemInfo.graphicsDeviceType)
            {
                Status = $"Aufzeichnung für {_gsc.graphicsDeviceType}, Spiel läuft mit {SystemInfo.graphicsDeviceType} – übersprungen";
                return;
            }
            _uhr = Stopwatch.StartNew();
            if (SystemInfo.supportsParallelPSOCreation)
            {
                _job = _gsc.WarmUp(default);
                Status = $"läuft parallel ({_gsc.totalGraphicsStateCount} Zustände)";
            }
            else Status = $"läuft in Portionen zu {ProBild} je Bild";
            var go = new GameObject("PSO vorwärmen") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Waechter>();
#endif
        }

        private sealed class Waechter : MonoBehaviour
        {
            private void Update()
            {
                if (_gsc == null) { Destroy(gameObject); return; }
                if (!SystemInfo.supportsParallelPSOCreation && !_gsc.isWarmedUp) _job = _gsc.WarmUpProgressively(ProBild, _job);   // hängt an der vorigen Portion
                if (!_gsc.isWarmedUp) return;
                _job.Complete();
                Fertig = true;
                Sekunden = (float)_uhr.Elapsed.TotalSeconds;
                Status = $"fertig: {_gsc.completedWarmupCount} Zustände in {Sekunden:0.0} s";
                Debug.Log("[Ashveil] PSO vorwärmen " + Status);
                Destroy(gameObject);
            }
        }
    }
}
