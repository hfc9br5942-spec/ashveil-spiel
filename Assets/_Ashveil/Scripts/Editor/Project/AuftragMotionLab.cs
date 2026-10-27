using System;
using System.IO;
using Ashveil.Gameplay.Motion.Lab;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Auftrag „motionlab|&lt;Aufnahme&gt;“ (15.10.2026): startet die vorhandene Motion-Lab-Aufnahme (§110) ohne Menüklick –
    /// die echte Spielfigur mit allen prozeduralen Schichten fährt den Parcours ab, misst Fußgleiten/Anatomie und schreibt
    /// Logs/Ashveil/Motion/Motion-Aufnahme_&lt;Aufnahme&gt;.txt plus Bildbögen. Das Lab wird vorher neu gebaut (aktueller Spieleraufbau).
    /// Aufnahmen: mann, frau, mann-roh, waffen-mann, waffen-frau, anatomie-mann, anatomie-frau.
    /// Der Auftrag meldet „gestartet“; das Ergebnis steht in der Berichtsdatei, sobald Play von selbst endet.
    /// Kein Speichern-Dialog: ungespeicherte Änderungen der offenen Szene werden nicht angefasst (Abbruch, falls vorhanden).
    /// </summary>
    [InitializeOnLoad]
    public static class AuftragMotionLab
    {
        private static readonly string[] Erlaubt = { "mann", "frau", "mann-roh", "waffen-mann", "waffen-frau", "anatomie-mann", "anatomie-frau", "haltung", "rucke", "clips", "film", "stufen", "kopf", "stufen-frau", "kopf-frau", "film-frau", "lod", "lod-frau", "gleiten", "gleiten-frau", "reaktion", "reaktion-frau", "ziehfilm", "ziehfilm-frau", "angriffsfilm", "angriffsfilm-frau", "angriffsfilm-einhand", "angriffsfilm-wechsel",
            "kleidung-frau", "kleidung-mann", "treffer", "treffer-frau", "angriffsfilm-fps30", "angriffsfilm-fps60", "angriffsfilm-fps144" };   // 18.10.: Frauen-Varianten (MPFB-Frau); 21.10.: Tripo-Kleidung in Bewegung (Mann = Schalen-Vergleich)

        static AuftragMotionLab()
        {
            Auftragsordner.Registrieren("motionlab", Starten, false, 120);
        }

        private static void Starten(Auftragsordner.Auftrag a, Action<bool, string> fertig)
        {
            string was = (a.parameter ?? "").Trim();
            if (Array.IndexOf(Erlaubt, was) < 0) { fertig(false, "unbekannte Aufnahme – erlaubt: " + string.Join(", ", Erlaubt)); return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode) { fertig(false, "Play läuft schon"); return; }
            if (EditorSceneManager.GetActiveScene().isDirty) { fertig(false, "offene Szene hat ungespeicherte Änderungen – nicht angefasst"); return; }
            MotionLabBuilder.Build();
            EditorSceneManager.OpenScene(MotionLabBuilder.ScenePath, OpenSceneMode.Single);
            Directory.CreateDirectory("Temp");
            File.WriteAllText(MotionLabDirector.FlagFile, was);
            fertig(true, "Aufnahme „" + was + "“ gestartet – Bericht: Logs/Ashveil/Motion/Motion-Aufnahme_" + was + ".txt");
            PlayStarten(0);
        }

        // 16.10.: Direkt nach „kompilieren“ (Skripte neu geladen) ging der Play-Start gelegentlich verloren – der Auftrag meldete
        // „gestartet“, Unity blieb im Bearbeiten-Modus. Darum: starten, nach 2 s pruefen, bis zu dreimal nachsetzen.
        private static double _pruefZeit; private static int _versuch; private static bool _wacht;
        /// <summary>true, solange ein Play-Start angestossen, aber noch nicht bestaetigt ist (Auftragsordner wartet).</summary>
        public static bool StartLaeuft => _wacht;

        private static readonly string PlayLog = "Logs/Ashveil/Motion/play_start.txt";
        private static void PlayNotiz(string t)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PlayLog));
                File.AppendAllText(PlayLog, DateTime.Now.ToString("HH:mm:ss.fff") + " " + t
                    + " | compiling=" + EditorApplication.isCompiling + " updating=" + EditorApplication.isUpdating
                    + " focused=" + UnityEditorInternal.InternalEditorUtility.isApplicationActive
                    + " playing=" + EditorApplication.isPlaying + " will=" + EditorApplication.isPlayingOrWillChangePlaymode + "\n");
            }
            catch (Exception) { }
        }

        private static void PlayStarten(int versuch)
        {
            _versuch = versuch;
            // 16.10.: EnterPlaymode wurde nach einem Skript-Neuladen wiederholt stillschweigend verweigert, der Play-Knopf von Hand
            // ging – darum im Wechsel drei Wege: EnterPlaymode, isPlaying, Menue „Edit/Play“.
            // 17.10.: Mitschrift nach Logs/Ashveil/Motion/play_start.txt (Konsole ist von aussen nicht lesbar) und der Start
            // laeuft ueber EditorApplication.update statt delayCall – delayCall fiel direkt nach dem Szenenwechsel/Asset-Import
            // offenbar in einen Moment, in dem Unity den Wechsel verwirft.
            int weg = versuch % 3;
            double start = EditorApplication.timeSinceStartup + 0.5;
            EditorApplication.CallbackFunction einmal = null;
            einmal = () =>
            {
                if (EditorApplication.timeSinceStartup < start) return;
                EditorApplication.update -= einmal;
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) { PlayNotiz("Versuch " + (versuch + 1) + " verschoben (Unity beschaeftigt)"); start = EditorApplication.timeSinceStartup + 0.5; EditorApplication.update += einmal; return; }
                PlayNotiz("Versuch " + (versuch + 1) + " Weg " + weg);
                if (weg == 0) EditorApplication.EnterPlaymode();
                else if (weg == 1) EditorApplication.isPlaying = true;
                else PlayNotiz("Menue Edit/Play: " + EditorApplication.ExecuteMenuItem("Edit/Play"));
                EditorApplication.QueuePlayerLoopUpdate();
            };
            EditorApplication.update += einmal;
            _pruefZeit = EditorApplication.timeSinceStartup + 2.5;
            if (!_wacht) { _wacht = true; EditorApplication.update += Pruefen; }
        }

        // 18.10.: Wächter – eine Aufnahme hing mit defektem Avatar (Kopf fehlte) unbegrenzt im Play-Modus, alle folgenden Aufträge
        // warteten. Nach MaxAufnahmeSekunden ohne Ende wird Play beendet (Notiz in play_start.txt).
        private const double MaxAufnahmeSekunden = 1500;
        private static double _frist;
        private static void Waechter()
        {
            if (!EditorApplication.isPlaying) { EditorApplication.update -= Waechter; return; }
            if (EditorApplication.timeSinceStartup < _frist) return;
            EditorApplication.update -= Waechter;
            PlayNotiz("WÄCHTER: Aufnahme lief über " + MaxAufnahmeSekunden + " s – Play beendet");
            UnityEngine.Debug.LogWarning("[Ashveil] Motion-Lab: Aufnahme hing – Play nach " + MaxAufnahmeSekunden + " s beendet.");
            EditorApplication.ExitPlaymode();
        }

        private static void Pruefen()
        {
            if (EditorApplication.timeSinceStartup < _pruefZeit) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(MotionLabDirector.FlagFile) || _versuch >= 5)
            {
                EditorApplication.update -= Pruefen; _wacht = false;
                PlayNotiz(EditorApplication.isPlayingOrWillChangePlaymode ? "Play laeuft" : File.Exists(MotionLabDirector.FlagFile) ? "AUFGEGEBEN nach " + (_versuch + 1) + " Versuchen" : "Kennzeichen weg (Play lief?)");
                if (!EditorApplication.isPlayingOrWillChangePlaymode && File.Exists(MotionLabDirector.FlagFile))
                    UnityEngine.Debug.LogWarning("[Ashveil] Motion-Lab: Play liess sich sechsmal nicht starten – Unity neu starten.");
                if (EditorApplication.isPlayingOrWillChangePlaymode) { _frist = EditorApplication.timeSinceStartup + MaxAufnahmeSekunden; EditorApplication.update -= Waechter; EditorApplication.update += Waechter; }
                return;
            }
            UnityEngine.Debug.Log("[Ashveil] Motion-Lab: Play nicht gestartet – Versuch " + (_versuch + 2));
            PlayStarten(_versuch + 1);
        }
    }
}
