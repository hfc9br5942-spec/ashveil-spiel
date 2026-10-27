using System.Collections;
using System.IO;
using Ashveil.Core;
using Ashveil.Gameplay.Visuals;
using Ashveil.Rendering;
using Ashveil.World;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Abnahmetest M8.2 „Tageszeit“: Maxmod (Editor-Host), Krisbot und Maltebot (kommt nachts dazu).
    /// Geprueft: alle Uhren gleich ohne laufende Uebertragung, /zeit zeigt und stellt die Zeit, Sonne/Mond/Umgebungslicht
    /// folgen der Uhr, Feuer leuchten nachts kraeftiger, unter Tage ruht der Zyklus. Bilder: Logs/Ashveil/Tageszeit.
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string DayNightFlag = "tageszeit";
        private static bool _dayNightMode;

        /// <summary>
        /// Zeitraffer nur für Suiten, deren Prüfungen in Spielminuten je echter Sekunde gedacht sind (Lagerleben, Himmel):
        /// 1 Spielstunde = 5 Minuten wie bis 06.10.2026. Das Spiel läuft in Echtzeit (NET-020); diese Suite prüft die Echtzeit.
        /// </summary>
        public const double TestTempoSecondsPerHour = 300.0;

        private void Zeitraffer(bool an)
        {
            NetworkWorldTime.ServerSetTempo(an ? TestTempoSecondsPerHour : WorldClock.DefaultSecondsPerHour);
            Info(an ? "Zeitraffer nur für diesen Test: 1 Spielstunde = 5 Minuten (das Spiel läuft in Echtzeit)" : "Zeitraffer aus: Echtzeit");
        }

        private IEnumerator DayNightSuite()
        {
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(EmoteA)) != null, 30f);
            Check(a != null, "Maxmod (Editor-Host) angemeldet");
            if (a == null) yield break;
            yield return new WaitForSecondsRealtime(1f);
            DayNightCycle cycle = DayNightCycle.Active;
            Check(cycle != null && RenderSettings.sun != null, "Tag-und-Nacht-Steuerung in der Szene");
            if (cycle == null) yield break;
            float h0 = WorldClock.Hours;
            Check(h0 >= 15.4f && h0 < 17f && !cycle.MoonActive, $"Server startet am Nachmittag ({WorldClock.Text}) – gestaltetes Kareth-Licht");
            Info($"Tempo: 1 Spielstunde = {WorldClock.SecondsPerHour / 60.0:0.#} Minuten, ein Tag = {WorldClock.SecondsPerHour * 24 / 3600.0:0.#} Stunden");

            string exe = ProjectPaths.TestClientExe;
            if (!File.Exists(exe)) { Check(false, "Test-Client vorhanden"); yield break; }
            StartClient(exe, EmoteB);
            NetworkInventory b = null;
            yield return WaitUntil(() => (b = OnlineInv(EmoteB)) != null && b.IsReady, 120f);
            Check(b != null, "Krisbot (Test-Client) angemeldet");
            if (b == null) yield break;
            yield return new WaitForSecondsRealtime(2f);

            // ---------------------------------------------------------- Gleiche Uhr
            Line("");
            Line("[Eine Uhr für alle]");
            yield return Ask(b, "clock");
            float hb = ParseAfter(_last.data, "h=");
            float diffMin = Mathf.Abs(Mathf.DeltaAngle(hb * 15f, WorldClock.Hours * 15f) / 15f) * 60f;
            Check(_last.ok && diffMin < 1.5f && ParseAfter(_last.data, "syncs=") >= 1f,
                $"Krisbot hat dieselbe Uhrzeit wie der Server (Abweichung {diffMin:0.0} Spielminuten)");
            Check(_last.data.Contains("hud=") && _last.data.Contains(" Uhr") && _last.data.Contains("cycle=1"), $"Uhrzeit im HUD unter dem Spielerrahmen ({Field(_last.data, "hud=")})");
            float tStart = WorldClock.Hours;
            yield return new WaitForSecondsRealtime(3f);
            float advanced = (WorldClock.Hours - tStart) * 60f;
            float expect = (float)(3.0 / WorldClock.SecondsPerHour * 60.0);
            Check(advanced > expect * 0.6f && advanced < expect * 1.6f, $"Die Zeit läuft (+{advanced:0.00} Spielminuten in 3 s, erwartet {expect:0.00})");

            yield return Ask(b, "chat-line", "/zeit");
            yield return Ask(b, "clock");
            Check(Field(_last.data, "chat=").StartsWith("Es ist"), $"/zeit nennt die Uhrzeit („{Field(_last.data, "chat=")}“)");
            yield return Ask(b, "chat-line", "/zeit abc");
            yield return Ask(b, "clock");
            Check(Field(_last.data, "chat=").StartsWith("Uhrzeit?"), "Unsinnige Uhrzeit wird abgewiesen");

            // ---------------------------------------------------------- Stellen: Nacht
            Line("");
            Line("[Nacht]");
            int day0 = WorldClock.Day;
            float skyDay = RenderSettings.ambientSkyColor.grayscale;
            yield return Ask(b, "chat-line", "/zeit 23");
            yield return new WaitForSecondsRealtime(1f);
            Check(Mathf.Abs(WorldClock.Hours - 23f) < 0.1f, $"/zeit 23 (Test-Profil): Server springt auf {WorldClock.Text}");
            yield return Ask(b, "clock");
            string nightB = _last.data;
            Check(Mathf.Abs(ParseAfter(nightB, "h=") - 23f) < 0.1f && nightB.Contains("moon=1"), $"Krisbot sieht dieselbe Nacht ({Head(nightB)})");
            Check(cycle.MoonActive && RenderSettings.sun.intensity < 0.6f && RenderSettings.ambientSkyColor.grayscale < skyDay * 0.5f,
                $"Mondlicht: schwach und kühl ({RenderSettings.sun.intensity:0.00}), Umgebung dunkler ({RenderSettings.ambientSkyColor.grayscale:0.00} statt {skyDay:0.00})");
            Check(ParseAfter(nightB, "int=") < 0.6f && ParseAfter(nightB, "night=") > 0.8f, "… auch bei Krisbot");
            Check(DayNightCycle.Night > 0.8f && LightFlicker.NightBoost > 0.8f, "Feuer und Laternen leuchten nachts kräftiger");
            Check(WorldClock.Day == day0, "Vorwärts gestellt am selben Tag");

            // ---------------------------------------------------------- Stellen: Mittag
            Line("");
            Line("[Mittag]");
            yield return Ask(b, "chat-line", "/zeit 12:30");
            yield return new WaitForSecondsRealtime(1f);
            yield return Ask(b, "clock");
            Check(WorldClock.Day == day0 + 1 && Mathf.Abs(WorldClock.Hours - 12.5f) < 0.1f, $"Die Uhr läuft nie rückwärts: 12:30 ist am nächsten Tag (Tag {WorldClock.Day + 1})");
            // Dynamic Sky: Wolken daempfen die Sonne (Sonnendurchlass) – ohne Wolken waere sie ueber 2
            float trans = Ashveil.Rendering.Sky.SkyDirector.SunTransmittance;
            Check(!cycle.MoonActive && RenderSettings.sun.intensity > 2f * trans && ParseAfter(_last.data, "elev=") > 35f,
                $"Mittagssonne hoch und hell (Höhe {ParseAfter(_last.data, "elev="):0}°, Stärke {RenderSettings.sun.intensity:0.00}, Wolkendurchlass {trans:P0})");
            Check(Mathf.Abs(ParseAfter(_last.data, "h=") - 12.5f) < 0.1f && _last.data.Contains("moon=0"), "Krisbot: Mittag");
            Check(DayNightCycle.Night < 0.1f && LightFlicker.NightBoost < 0.1f, "Tagsüber normale Feuer");

            // ---------------------------------------------------------- Unter Tage
            BeginIndoorTest();
            try
            {
                float before = RenderSettings.sun.intensity;
                NetworkWorldTime.ServerJumpTo(22f);
                yield return new WaitForSecondsRealtime(0.4f);
                Check(Mathf.Approximately(before, RenderSettings.sun.intensity), "Unter Tage ruht der Tageszyklus (Grubenlicht bleibt)");
            }
            finally { RestoreIndoorTest(); }
            yield return null;
            yield return null;
            Check(cycle.MoonActive, "… und setzt draußen sofort wieder ein");

            // ---------------------------------------------------------- Spaeter Hinzukommende
            Line("");
            Line("[Später dazukommen]");
            StartClient(exe, EmoteC);
            NetworkInventory c = null;
            yield return WaitUntil(() => (c = OnlineInv(EmoteC)) != null && c.IsReady, 120f);
            Check(c != null, "Maltebot kommt in der Nacht dazu");
            if (c != null)
            {
                yield return new WaitForSecondsRealtime(2f);
                yield return Ask(c, "clock");
                float hc = ParseAfter(_last.data, "h=");
                float d = Mathf.Abs(Mathf.DeltaAngle(hc * 15f, WorldClock.Hours * 15f) / 15f) * 60f;
                Check(d < 1.5f && _last.data.Contains("moon=1"), $"Maltebot hat sofort dieselbe Nacht ({hc:0.00} / Server {WorldClock.Hours:0.00})");
            }

            // ---------------------------------------------------------- Bilder
            yield return CaptureDayNight(a, cycle);
            NetworkWorldTime.ServerJumpTo((float)WorldClock.DefaultStartHour);
        }

        private static string Field(string data, string key)
        {
            int i = data.IndexOf(key, System.StringComparison.Ordinal);
            if (i < 0) return "";
            int j = data.IndexOf(';', i);
            return j < 0 ? data.Substring(i + key.Length) : data.Substring(i + key.Length, j - i - key.Length);
        }

        private IEnumerator CaptureDayNight(NetworkInventory a, DayNightCycle cycle)
        {
            Camera cam = Camera.main;
            if (cam == null) { Check(false, "Kamera für Bilder"); yield break; }
            string dir = ProjectPaths.Logs("Tageszeit");
            Directory.CreateDirectory(dir);
            Vector3 p = a.transform.position;
            Vector3 toSun = Quaternion.Euler(0f, cycle.DesignYaw + 180f, 0f) * Vector3.forward;
            float[] hours = { 6.1f, 9f, 12.5f, 15.5f, 18.4f, 19.7f, 21.5f, 1f };
            int shots = 0;
            foreach (float h in hours)
            {
                NetworkWorldTime.ServerJumpTo(h);
                yield return new WaitForSecondsRealtime(0.5f);
                string name = $"{(int)h:00}{(int)((h % 1f) * 60f + 0.5f):00}";
                for (int v = 0; v < 2; v++)
                {
                    Vector3 dir2 = v == 0 ? toSun : -toSun;
                    Vector3 pos0 = cam.transform.position;
                    Quaternion rot0 = cam.transform.rotation;
                    cam.transform.position = p + Vector3.up * 4.5f - dir2 * 7f;
                    cam.transform.LookAt(p + dir2 * 40f + Vector3.up * 3f);
                    if (CaptureUtil.Capture(cam, Path.Combine(dir, $"{name}_{(v == 0 ? "zur_Sonne" : "Gegenlicht_ab")}.png"), 1280, 720)) shots++;
                    cam.transform.SetPositionAndRotation(pos0, rot0);
                }
            }
            Check(shots == hours.Length * 2, $"{shots} Bilder zu {hours.Length} Tageszeiten gespeichert (Logs/Ashveil/Tageszeit)");
        }
    }
}
