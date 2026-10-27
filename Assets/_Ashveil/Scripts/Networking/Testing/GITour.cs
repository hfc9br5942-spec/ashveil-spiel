using System.Collections;
using System.IO;
using System.Text;
using Ashveil.Core;
using Ashveil.Gameplay.CameraSystem;
using Ashveil.Gameplay.Characters;
using Ashveil.Rendering.GI;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// GI-Rundgang (Editor-Host, Play-Modus): die festen Blickpunkte der Rendering-Grundlinie (Startlager, Wald, Glockenmoor,
    /// Aschenfeld, Kolosseum-Vorplatz, Klippenschenke, Salzhalle, Arena) am Nachmittag und in der Nacht abfahren und je Ort
    /// ein Bildpaar klassisch ↔ dynamisch speichern. Bilder: Logs/Ashveil/GI/Rundgang/, Bericht: Logs/Ashveil/GI/Rundgang.txt.
    /// Start: "Ashveil › Rendering › Dynamisches GI: Rundgang (8 Orte, Tag + Nacht)".
    /// </summary>
    public sealed class GITour : MonoBehaviour
    {
        public static bool Running { get; private set; }

        private static readonly (string name, float hour)[] Times = { ("Tag", 15.5f), ("Nacht", 22f) };

        public static bool Begin()
        {
            if (Running || AshveilDynamicGI.Instance == null || !AshveilDynamicGI.IsRunning) return false;
            var go = new GameObject("GI-Rundgang");
            go.AddComponent<GITour>();
            return true;
        }

        private IEnumerator Start()
        {
            Running = true;
            var report = new StringBuilder();
            report.AppendLine($"ASHVEIL GI-Rundgang · {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            string dir = ProjectPaths.Logs("GI/Rundgang");
            Directory.CreateDirectory(dir);

            PlayerCharacter player = null;
            foreach (PlayerCharacter pc in FindObjectsByType<PlayerCharacter>(FindObjectsInactive.Exclude))
                if (pc.isActiveAndEnabled && pc.GetComponent<NetworkMotion>() != null && pc.GetComponent<NetworkMotion>().IsOwner) { player = pc; break; }
            if (player == null)
                foreach (PlayerCharacter pc in FindObjectsByType<PlayerCharacter>(FindObjectsInactive.Exclude))
                    if (pc.isActiveAndEnabled) { player = pc; break; }
            Camera cam = Camera.main;
            if (player == null || cam == null)
            {
                Debug.LogWarning("[Ashveil] GI-Rundgang: keine eigene Spielfigur/Kamera.");
                Running = false;
                Destroy(gameObject);
                yield break;
            }
            var motion = player.GetComponent<NetworkMotion>();
            var controller = cam.GetComponent<MMOCameraController>();
            if (controller != null) controller.enabled = false;
            var pose = cam.gameObject.AddComponent<GITourPose>();
            AshveilDynamicGI gi = AshveilDynamicGI.Instance;

            foreach ((string timeName, float hour) in Times)
            {
                NetworkWorldTime.ServerJumpTo(hour);
                report.AppendLine();
                report.AppendLine($"[{timeName}, {hour:0.0} Uhr]");
                int n = 0;
                foreach (RenderBaseline.Station st in RenderBaseline.Stations)
                {
                    n++;
                    if (motion != null)
                    {
                        Vector3 stand = st.Stand;
                        stand.y = WorldGround.SampleHeight(stand, stand.y) + 0.2f;
                        motion.ServerTeleport(stand, 0f);
                        WorldStreamer.Instance?.RequestPreload(stand, 30f);
                    }
                    float settle = 0f;
                    while (settle < 12f && WorldStreamer.Instance != null && !WorldStreamer.Instance.IsReadyAt(st.Stand))
                    {
                        settle += 0.25f;
                        yield return new WaitForSecondsRealtime(0.25f);
                    }
                    RenderBaseline.PlaceCamera(cam, st);
                    pose.Hold(cam.transform.position, cam.transform.rotation);
                    // Proben am neuen Ort einschwingen lassen (neue Proben starten ohne Traegheit, Rest ~1–2 s)
                    yield return new WaitForSecondsRealtime(4f);
                    if (gi == null || !AshveilDynamicGI.IsRunning) break;
                    string prefix = Path.Combine(dir, $"{timeName}_{n}_{Safe(st.Name)}");
                    string result = null;
                    yield return gi.CaptureComparison(prefix, r => result = r, false);
                    report.AppendLine($"  {st.Name}");
                    foreach (string line in (result ?? "").Split('\n'))
                        if (line.Length > 0) report.AppendLine("    " + Path.GetFileName(line));
                }
            }

            Destroy(pose);
            if (controller != null) controller.enabled = true;
            if (motion != null) motion.ServerTeleportToSpawn();
            NetworkWorldTime.ServerJumpTo(15.5f);
            File.WriteAllText(ProjectPaths.Logs("GI/Rundgang.txt"), report.ToString());
            Debug.Log("[Ashveil] GI-Rundgang fertig → " + ProjectPaths.Logs("GI/Rundgang.txt"));
            Running = false;
            Destroy(gameObject);
        }

        private static string Safe(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString().Trim('_');
        }
    }

    /// <summary>Haelt die Kamera fuer den Rundgang fest (nach allen anderen Kamera-Skripten).</summary>
    [DefaultExecutionOrder(32000)]
    internal sealed class GITourPose : MonoBehaviour
    {
        private Vector3 _p;
        private Quaternion _r = Quaternion.identity;
        public void Hold(Vector3 p, Quaternion r) { _p = p; _r = r; transform.SetPositionAndRotation(p, r); }
        private void LateUpdate() => transform.SetPositionAndRotation(_p, _r);
    }
}
