using Ashveil.Core;
using Ashveil.Networking;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Weltzeit im Editor (NET-020, Welle 0 06.10.2026): bei welcher Uhrzeit die Weltuhr startet, wenn im Editor Play gedrückt
    /// wird. Danach läuft sie in Echtzeit. Standard 15:30 (gestaltetes Licht), damit Bilder und Spiel-Tests gleich bleiben.
    /// Builds nehmen den Startparameter „-ashveil-zeitversatz &lt;Wert&gt;“ (ohne Parameter: Echtzeit = Uhrzeit des Servers).
    /// Menü: Ashveil › Welt › Weltzeit im Editor.
    /// </summary>
    public static class WeltzeitEditor
    {
        private const string Root = "Ashveil/Welt/Weltzeit im Editor/";
        private const string MGestaltet = Root + "15:30 – gestaltetes Licht (Standard)";
        private const string MVormittag = Root + "09:00 – Vormittag (wie Spieltest r2)";
        private const string MNacht = Root + "22:00 – Nacht";
        private const string MEcht = Root + "Echtzeit (Uhrzeit des PCs)";

        private static string Wert => EditorPrefs.GetString(WorldTimeSettings.EditorPrefKey, WorldTimeSettings.EditorDefault);

        private static void Setzen(string wert)
        {
            EditorPrefs.SetString(WorldTimeSettings.EditorPrefKey, wert);
            WorldTimeStart.TryParse(wert, out WorldTimeStart s);
            Debug.Log($"[Ashveil] Weltzeit im Editor: {s} – gilt beim nächsten Play.");
        }

        [MenuItem(MGestaltet, priority = 580)] private static void Gestaltet() => Setzen("15:30");
        [MenuItem(MVormittag, priority = 581)] private static void Vormittag() => Setzen("09:00");
        [MenuItem(MNacht, priority = 582)] private static void Nacht() => Setzen("22:00");
        [MenuItem(MEcht, priority = 583)] private static void Echt() => Setzen("echtzeit");

        [MenuItem(MGestaltet, true)] private static bool GestaltetPruefen() => Haken(MGestaltet, "15:30");
        [MenuItem(MVormittag, true)] private static bool VormittagPruefen() => Haken(MVormittag, "09:00");
        [MenuItem(MNacht, true)] private static bool NachtPruefen() => Haken(MNacht, "22:00");
        [MenuItem(MEcht, true)] private static bool EchtPruefen() => Haken(MEcht, "echtzeit");

        private static bool Haken(string menu, string wert)
        {
            Menu.SetChecked(menu, string.Equals(Wert, wert, System.StringComparison.OrdinalIgnoreCase));
            return !EditorApplication.isPlaying;
        }
    }
}
