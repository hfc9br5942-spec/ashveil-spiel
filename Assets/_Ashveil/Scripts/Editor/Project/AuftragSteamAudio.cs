using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Steam Audio (Valve, Open Source, Apache 2.0) ins Projekt holen – Max 14.10.2026: „gerne mit valve“ (Klang mit Strahlen-
    /// verfolgung: Verdeckung, Nachhall, Reflexionen, HRTF). Quelle: github.com/ValveSoftware/steam-audio, Release v4.8.1
    /// (steamaudio_unity_4.8.1.zip, SHA-256 9b7e3689…40f0). Daraus nur Windows 64-Bit verpackt (Projektregel 12: 20 MB statt
    /// 122 MB; weitere Plattformen jederzeit aus dem Original nachholbar).
    ///   „steamaudio_importieren“  spielt GENAU diese Datei ein: Tools/Pakete/SteamAudio_4.8.1_win64.unitypackage, nur wenn die
    ///                             Prüfsumme stimmt (kein freier Paketname, wie bei den Paket-Aufträgen). Die Meldung wird vor
    ///                             dem Import geschrieben, weil Unity danach die Skripte neu lädt – prüfen mit „kompilieren“.
    ///   „steamaudio_einrichten“   Projekteinstellungen › Audio: Spatializer = Steam Audio Spatializer, Ambisonic-Decoder =
    ///                             Steam Audio Ambisonic Decoder (Valve-Anleitung „Getting Started“).
    /// </summary>
    [InitializeOnLoad]
    public static class AuftragSteamAudio
    {
        public const string Paket = "Tools/Pakete/SteamAudio_4.8.1_win64.unitypackage";
        public const string Sha256 = "3f48b8577a5f1554005faef48d0f730729b14c401166210e0caad3182eaa86a6";
        public const string Spatializer = "Steam Audio Spatializer";
        public const string Decoder = "Steam Audio Ambisonic Decoder";

        static AuftragSteamAudio()
        {
            Auftragsordner.Registrieren("steamaudio_importieren", Importieren, true, 300);
            Auftragsordner.Registrieren("steamaudio_einrichten", Einrichten, true, 60);
        }

        private static string Abs(string rel) => Path.Combine(Path.GetDirectoryName(Application.dataPath), rel);

        private static void Importieren(Auftragsordner.Auftrag a, Action<bool, string> fertig)
        {
            string pfad = Abs(Paket);
            if (!File.Exists(pfad)) { fertig(false, "Paket fehlt: " + Paket); return; }
            string hash;
            using (var sha = SHA256.Create())
            using (var f = File.OpenRead(pfad))
                hash = string.Concat(sha.ComputeHash(f).Select(b => b.ToString("x2")));
            if (!string.Equals(hash, Sha256, StringComparison.OrdinalIgnoreCase))
            {
                fertig(false, $"Prüfsumme stimmt nicht ({hash}) – nichts eingespielt");
                return;
            }
            bool schon = AssetDatabase.IsValidFolder("Assets/Plugins/SteamAudio");
            fertig(true, (schon ? "Steam Audio war schon da – wird überschrieben (gleiche Version). " : "") +
                         "Prüfsumme stimmt, Import gestartet (Steam Audio 4.8.1, Windows 64-Bit) – danach „kompilieren“ und „steamaudio_einrichten“");
            EditorApplication.delayCall += () => AssetDatabase.ImportPackage(pfad, false);
        }

        private static void Einrichten(Auftragsordner.Auftrag a, Action<bool, string> fertig)
        {
            string[] spat = AudioSettings.GetSpatializerPluginNames() ?? new string[0];
            if (!spat.Contains(Spatializer))
            {
                fertig(false, "Steam Audio Spatializer nicht gefunden (vorhanden: " + string.Join(", ", spat) + ") – erst „steamaudio_importieren“ und „kompilieren“");
                return;
            }
            var am = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset").FirstOrDefault();
            if (am == null) { fertig(false, "ProjectSettings/AudioManager.asset nicht lesbar"); return; }
            var so = new SerializedObject(am);
            SerializedProperty sp = so.FindProperty("m_SpatializerPlugin"), dp = so.FindProperty("m_AmbisonicDecoderPlugin");
            if (sp == null || dp == null) { fertig(false, "Audio-Einstellungen: Felder nicht gefunden"); return; }
            string vorher = $"{sp.stringValue} / {dp.stringValue}";
            sp.stringValue = Spatializer;
            dp.stringValue = Decoder;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            fertig(true, $"Audio-Einstellungen: Spatializer „{Spatializer}“, Ambisonic-Decoder „{Decoder}“ (vorher: {vorher})");
        }
    }
}
