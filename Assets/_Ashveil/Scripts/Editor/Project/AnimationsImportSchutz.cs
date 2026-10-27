using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Schutz gegen den Editor-Absturz beim Neu-Importieren von Modellen (15.10.2026, drei gleiche Abstürze):
    /// Unity 6000.6.3f1 stürzt in AnimationClip::IsMecanimDataValid ab (AnimationClipRefresher → Animator::Rebind),
    /// wenn ein FBX mit Bewegungen neu importiert wird, während ein Animator in einer offenen Szene, einer Prefab-Ansicht
    /// oder der Inspector-Vorschau die alten Clips noch hält. Das ist ein Unity-Fehler; wir umgehen ihn, indem wir vor
    /// dem Import eine leere Szene öffnen, die Auswahl leeren und die Animations-Vorschau beenden – danach wird der
    /// vorherige Szenenstand wiederhergestellt. Ungespeicherte Szenen werden NIE verworfen: dann läuft der Import ohne
    /// Szenenwechsel (mit Warnung).
    /// </summary>
    public static class AnimationsImportSchutz
    {
        private const string Ordner = "Assets/_Ashveil";
        private static string Schluessel => "Ashveil.ImportSchutz.Stempel." + Application.dataPath.GetHashCode();

        /// <summary>Neuester Schreibzeitpunkt aller FBX unter Assets/_Ashveil (Ticks, 0 = keine).</summary>
        private static long NeuesteModellZeit()
        {
            string voll = Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, Ordner);
            if (!Directory.Exists(voll)) return 0;
            try
            {
                return Directory.EnumerateFiles(voll, "*.fbx", SearchOption.AllDirectories)
                    .Select(f => File.GetLastWriteTimeUtc(f).Ticks).DefaultIfEmpty(0).Max();
            }
            catch (Exception) { return long.MaxValue; }   // im Zweifel schützen
        }

        /// <summary>true, wenn seit dem letzten geschützten Import ein FBX geschrieben wurde.</summary>
        public static bool ModelleGeaendert()
        {
            long gemerkt = long.TryParse(EditorPrefs.GetString(Schluessel, "0"), out long g) ? g : 0;
            return NeuesteModellZeit() > gemerkt;
        }

        /// <summary>
        /// Führt <paramref name="aktion"/> aus (Refresh/Import). Bei geänderten Modellen oder <paramref name="immer"/>
        /// vorher in eine leere Szene wechseln und danach den alten Szenenstand wiederherstellen.
        /// </summary>
        public static void Ausfuehren(Action aktion, bool immer = false, bool szeneZurueck = true)
        {
            if (!immer && !ModelleGeaendert()) { aktion(); return; }

            Selection.activeObject = null;                       // Inspector-Vorschau eines FBX hält sonst einen Animator
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            if (StageUtility.GetCurrentStage() is PrefabStage) StageUtility.GoToMainStage();

            // Vom Werkzeug erzeugte Szenen (Motion-Lab) duerfen gespeichert werden – sie sind nach jeder Aufnahme „ungespeichert“
            // und blockierten sonst den Schutz. Szenen von Max werden nie angefasst.
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var s = EditorSceneManager.GetSceneAt(i);
                if (s.isDirty && !string.IsNullOrEmpty(s.path) && s.path.Replace('\\', '/') == MotionLabBuilder.ScenePath) EditorSceneManager.SaveScene(s);
            }
            SceneSetup[] stand = EditorSceneManager.GetSceneManagerSetup();
            bool wechselbar = !EditorApplication.isPlayingOrWillChangePlaymode;
            for (int i = 0; i < EditorSceneManager.sceneCount && wechselbar; i++)
            {
                var s = EditorSceneManager.GetSceneAt(i);
                if (s.isDirty || string.IsNullOrEmpty(s.path)) wechselbar = false;
            }

            if (!wechselbar)
            {
                Debug.LogWarning("[Ashveil] Importschutz: offene Szene ist ungespeichert – Import ohne Szenenwechsel " +
                                 "(Absturzgefahr bei Bewegungs-FBX). Szene speichern und Auftrag wiederholen.");
                aktion();
                Merken();
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try { aktion(); }
            finally
            {
                // 17.10.2026 abends (Absturz 7, Stack: RestoreSceneManagerSetup → Animator::AwakeFromLoad →
                // AnimationBlendTreePlayable::ConfigureFromConstant): Beim Zurueckholen der Szene laedt ein Animator, dessen
                // Controller noch auf die gerade ersetzten Clips zeigt – Unity stuerzt ab. Wer danach den Animator neu baut
                // (mocap_import → spieler_animator → motionlab), bleibt deshalb in der leeren Szene (szeneZurueck = false).
                if (szeneZurueck && stand.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(stand);
                Merken();
            }
        }

        private static void Merken() => EditorPrefs.SetString(Schluessel, NeuesteModellZeit().ToString());
    }
}
