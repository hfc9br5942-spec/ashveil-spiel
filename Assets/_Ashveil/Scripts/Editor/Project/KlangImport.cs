using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Import-Regeln für Klänge unter <c>Assets/_Ashveil/Audio/</c> und <c>Art/ThirdParty/Audio/</c> (Claude 14.10.2026, Klangteppich; Projektregel 12):
    /// Vorbis; Schleifen und Betten komprimiert im Speicher, kurze Einzelklänge beim Laden entpackt (kein Dekodieren je Abspielen);
    /// alles außer Betten („_Bett“) mono (3D-Quellen brauchen kein Stereo); im Hintergrund laden.
    /// </summary>
    public sealed class KlangImport : AssetPostprocessor
    {
        public override uint GetVersion() => 2;   // 2: Schleifen gestreamt (erzwingt Neuimport)

        private void OnPreprocessAudio()
        {
            string p = assetPath.Replace('\\', '/');
            if (!p.StartsWith(ProjectSetup.Root + "/Audio/") && !p.StartsWith(ProjectSetup.Root + "/Art/ThirdParty/Audio/")) return;
            var imp = (AudioImporter)assetImporter;
            bool bett = p.Contains("_Bett");
            imp.forceToMono = !bett;
            imp.loadInBackground = true;
            var s = imp.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = bett ? 0.5f : 0.6f;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            // Kurze Einzelklänge beim Laden entpackt; lange Schleifen und Betten (Bach, Brandung, Mühle, Wind, Grillen) gestreamt:
            // Messung 14.10. nachmittags – das erste Abspielen einer im Speicher komprimierten Schleife kostete im Hauptfaden
            // 138 ms (SoundManager.LoadFMODSound); Streamen öffnet nur und dekodiert im Audio-Faden.
            s.loadType = p.Contains("Einzel") || p.Contains("glocke") || p.Contains("Lerche") || p.Contains("Schritt")
                ? AudioClipLoadType.DecompressOnLoad
                : AudioClipLoadType.Streaming;
            s.preloadAudioData = true;
            imp.defaultSampleSettings = s;
        }
    }
}
