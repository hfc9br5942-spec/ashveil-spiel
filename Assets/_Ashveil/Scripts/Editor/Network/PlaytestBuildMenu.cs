using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// 12h-Block 01.10.2026: P4-Playtest-Build fuer Max (Development-Build, eigener Ordner, Startdateien).
    ///   Spielen.bat       – startet als Host (Server + eigener Spieler), vorhandenes Profil; Weltuhr ab 09:00, danach Echtzeit
    ///                       (Max 06.10.2026, Klick „Versatz auf Vormittag“, NET-020)
    ///   Neues Profil.bat  – loescht nur die Profilzuordnung (ashveil_profile.txt), der Charakter-Editor oeffnet sich
    /// Spielstaende liegen im Build-Ordner unter Saves/ und beruehren die Projekt-Spielstaende nicht.
    /// </summary>
    public static class PlaytestBuildMenu
    {
        public const string Folder = "Builds/Playtest_P4_2026-10-02_r2";

        [MenuItem("Ashveil/P3/P4-Playtest-Build erstellen", priority = 4)]
        public static void BuildMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst den Play-Modus beenden.", "OK");
                return;
            }
            if (!NetworkSetupBuilder.BuildPlaytest(Folder)) return;
            WriteStartFiles(Folder, "P4-Playtest (r2, Schaerfe-Korrektur 02.10.)");
        }

        /// <summary>
        /// Spielen.bat und Neues Profil.bat in den Build-Ordner (P4-Build und seit #30 auch der Spieltest-Build r2, SpieltestBuild).
        /// titel steht als Kommentar in den Dateien (nur ASCII).
        /// </summary>
        public static void WriteStartFiles(string folder, string titel)
        {
            var bat = new StringBuilder();
            bat.AppendLine("@echo off");
            bat.AppendLine("rem ASHVEIL " + titel + ": startet als Host (eigener Server + Spieler)");
            bat.AppendLine("start \"\" \"%~dp0Ashveil.exe\" -ashveil-host " + NetworkSetupBuilder.ZeitVormittag);
            File.WriteAllText(Path.Combine(folder, "Spielen.bat"), bat.ToString(), Encoding.ASCII);

            var fresh = new StringBuilder();
            fresh.AppendLine("@echo off");
            fresh.AppendLine("rem ASHVEIL " + titel + ": neues Profil (alter Spielstand bleibt in Saves\\Characters erhalten)");
            fresh.AppendLine("if exist \"%~dp0ashveil_profile.txt\" del \"%~dp0ashveil_profile.txt\"");
            fresh.AppendLine("start \"\" \"%~dp0Ashveil.exe\" -ashveil-host " + NetworkSetupBuilder.ZeitVormittag);
            File.WriteAllText(Path.Combine(folder, "Neues Profil.bat"), fresh.ToString(), Encoding.ASCII);

            Debug.Log("[Ashveil] Startdateien geschrieben (" + titel + "): Spielen.bat, Neues Profil.bat");
        }
    }
}
