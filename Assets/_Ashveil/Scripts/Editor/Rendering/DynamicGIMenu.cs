using System.IO;
using Ashveil.Rendering.GI;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools.Rendering
{
    /// <summary>Dynamisches GI im Play-Modus: an/aus, Anzeige, Diagnose, Vergleichsbilder (klassisch ↔ dynamisch).</summary>
    public static class DynamicGIMenu
    {
        [MenuItem("Ashveil/Rendering/Dynamisches GI an – aus (Play-Modus)", priority = 420)]
        public static void Toggle()
        {
            if (!EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Ashveil", "Nur im Play-Modus.", "OK"); return; }
            bool on = !AshveilDynamicGI.IsRunning;
            if (!AshveilDynamicGI.SetEnabled(on))
                EditorUtility.DisplayDialog("Ashveil", AshveilDynamicGI.LastError, "OK");
            Debug.Log("[Ashveil] Dynamisches GI: " + (AshveilDynamicGI.IsRunning ? "an" : "aus"));
        }

        [MenuItem("Ashveil/Rendering/Dynamisches GI: Anzeige (Kosten, Proben)", priority = 421)]
        public static void Hud()
        {
            var gi = AshveilDynamicGI.Instance;
            if (gi != null) gi.showHud = !gi.showHud;
        }

        [MenuItem("Ashveil/Rendering/Dynamisches GI: Diagnose Gueltigkeit", priority = 422)]
        public static void Validity()
        {
            var gi = AshveilDynamicGI.Instance;
            if (gi != null) gi.debugView = gi.debugView == 2 ? 0 : 2;
        }

        [MenuItem("Ashveil/Rendering/Dynamisches GI: Vergleichsbilder speichern", priority = 423)]
        public static void Compare()
        {
            var gi = AshveilDynamicGI.Instance;
            if (gi == null || !AshveilDynamicGI.IsRunning) { Debug.LogWarning("[Ashveil] Dynamisches GI laeuft nicht."); return; }
            string prefix = Path.Combine("Logs/Ashveil/GI", "Vergleich_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            gi.StartCoroutine(gi.CaptureComparison(prefix, r => Debug.Log("[Ashveil] GI-Vergleich gespeichert:\n" + r), true));
        }
    
        [MenuItem("Ashveil/Rendering/Dynamisches GI: Rundgang (8 Orte, Tag + Nacht)", priority = 423)]
        public static void Tour()
        {
            if (!EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Ashveil", "Nur im Play-Modus (Host).", "OK"); return; }
            if (!Ashveil.Networking.GITour.Begin())
                Debug.LogWarning("[Ashveil] GI-Rundgang: GI laeuft nicht oder Rundgang laeuft schon.");
            else Debug.Log("[Ashveil] GI-Rundgang gestartet (ca. 2 Minuten) …");
        }

        [MenuItem("Ashveil/Rendering/Dynamisches GI: Uhrzeit 22 Uhr (Host)", priority = 424)]
        public static void Night() => Jump(22f);

        [MenuItem("Ashveil/Rendering/Dynamisches GI: Uhrzeit 15:30 (Host)", priority = 425)]
        public static void Day() => Jump(15.5f);

        [MenuItem("Ashveil/Rendering/Dynamisches GI: Kostendiagnose weiterschalten", priority = 427)]
        public static void CostMode()
        {
            var gi = AshveilDynamicGI.Instance;
            if (gi == null) return;
            gi.costMode = (gi.costMode + 1) % 8;
            gi.showHud = true;
            string[] names = { "alles", "ohne Bildpass (Vorpass bleibt)", "ohne Probenstrahlen", "ohne Vorpass und Bildpass", "nur 64 Proben", "ohne Bewegungsnachfuehrung", "64 Proben ohne Bewegung", "ein Material fuer alle" };
            Debug.Log("[Ashveil] GI-Kostendiagnose: " + names[gi.costMode]);
        }

        private static void Jump(float hour)
        {
            if (!EditorApplication.isPlaying) return;
            Ashveil.Networking.NetworkWorldTime.ServerJumpTo(hour);
        }
    }
}
