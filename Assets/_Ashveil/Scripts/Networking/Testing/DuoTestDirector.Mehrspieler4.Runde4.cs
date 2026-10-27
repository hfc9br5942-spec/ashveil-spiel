using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Ashveil.World;
using FishNet;
using FishNet.Connection;
using FishNet.Managing.Server;
using FishNet.Transporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Ashveil.Networking
{
    /// <summary>
    /// 22.10.2026 (Runde 4: Mehrspieler, Speichern/Laden, Releaseablauf) – Teil 5 der Suite mehrspieler4, mit ECHTEN Test-Client-Prozessen
    /// (Test-Client = Build ohne Development wie das Release, Mausfilter hängt):
    ///   MEHRSPIELER-05 (a) Test-Client P startet mit -ashveil-host, während der Editor Port 7770 hält → P's Server kommt nicht hoch, der
    ///                      eigene Spieler verbindet sich NICHT (keine neue Verbindung am Editor-Server), Grund im Log/F9-Fenster.
    ///                  (c) Test-Client Q meldet sich mit dem Profil des Host-Spielers A an → Q wird abgewiesen, A bleibt im Spiel.
    ///   REL-01 / SPEICHERN-01  P über WM_CLOSE (wie X; per PostMessage), Q über WM_SYSCOMMAND/SC_CLOSE (wie Alt+F4, Taskleiste) und C
    ///                  (angemeldet, in der Welt) über WM_CLOSE schließen → jeder endet über Application.Quit (Logzeile „Fenster schliessen“)
    ///                  mit Exit-Code 0. Vorher (Probe 22.10. mit dem alten Test-Client): echtes WM_CLOSE und SC_CLOSE → Unity beendet INNERHALB der
    ///                  Fensterprozedur des Mausfilters (OnApplicationQuit lief), danach Absturz 0xC0000005 (alter Spieltest-Build ebenso). Die „Exit -2 ohne Abbau“ der Kurztests kamen von Process.CloseMainWindow im Editor-Mono (= TerminateProcess -2).
    /// Logs der Prozesse: Logs/Ashveil/Mehrspieler4/&lt;Name&gt;.log (-logFile). Jede Prüfung nennt mit „| alt: …“ das alte Verhalten.
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        private const string Mp4P = "duo-mp4-port";
        private const string Mp4MarkeQuit = "[Ashveil] Fenster schliessen";
        private const string Mp4MarkeHost = "[Ashveil] Netzwerk: Host nicht gestartet";
        private const uint Mp4WmSysCommand = 0x0112, Mp4WmClose = 0x0010;
        private static readonly IntPtr Mp4ScClose = new IntPtr(0xF060);

        private Process _mp4ProzessC;
        private int _mp4Abgewiesen;

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hwnd, uint nachricht, IntPtr wParam, IntPtr lParam);

        private IEnumerator Mp4Release(NetworkInventory a, string exe)
        {
            Line("");
            Line("[5 Releaseablauf: Host-Start bei belegtem Port, zweite Anmeldung des Host-Profils, Fenster schließen (MEHRSPIELER-05, REL-01)]");
            ServerManager sm = InstanceFinder.ServerManager;
            if (!Mp4Voraus(sm != null && sm.Started && a != null && OnlineInv(Mp4A) == a && a.Owner != null && a.Owner.IsLocalClient,
                    "Editor ist Host (Server läuft, A ist der eigene Spieler des Hosts)")) yield break;
            string ordner = Ashveil.Core.ProjectPaths.Logs("Mehrspieler4");
            Directory.CreateDirectory(ordner);
            int verbindungen = 0;
            Action<NetworkConnection, RemoteConnectionStateArgs> zaehle = (c, args) => { if (args.ConnectionState == RemoteConnectionState.Started) verbindungen++; };
            sm.OnRemoteConnectionState += zaehle;
            Application.logMessageReceived += Mp4ReleaseLog;
            try
            {
                // ---------------------------------------------------------- (a) MEHRSPIELER-05: Host-Start bei belegtem Port
                Sp3Punkt("MEHRSPIELER-05", "(a) Test-Client P startet als Host (-ashveil-host), während der Editor Port 7770 hält (wie zweiter Start von Spielen.bat)");
                string logP = Path.Combine(ordner, "P_Host_belegter_Port.log");
                Process p = Mp4Prozess(exe, $"-ashveil-host -ashveil-profile {Mp4P} -screen-fullscreen 0 -screen-width 960 -screen-height 540", logP);
                int v0 = verbindungen;
                yield return WaitUntil(() => verbindungen > v0 || p.HasExited || Mp4LogHat(logP, Mp4MarkeHost), 120f);
                yield return new WaitForSecondsRealtime(10f);   // Zeit, in der sich der eigene Client früher verbunden hätte
                string textP = Mp4LogLesen(logP);
                bool zeile = textP.Contains(Mp4MarkeHost);
                bool bindefehler = textP.Contains("already in use") || textP.Contains("Bind exception");
                bool clientStart = textP.Contains("Local client is starting");
                bool amServer = NetworkInventory.Pending(Mp4P) != null || OnlineInv(Mp4P) != null;
                Info($"P: läuft {(!p.HasExited ? "ja" : "NEIN")}, Bindefehler im Log {(bindefehler ? "ja" : "nein")}, eigener Client gestartet {(clientStart ? "JA" : "nein")}, Log {textP.Length / 1024} KB");
                Check(zeile && !clientStart && verbindungen == v0 && !amServer && !p.HasExited,
                    $"P: Server kam nicht hoch → eigener Spieler bleibt aus, Grund im F9-Fenster (Log „Host nicht gestartet“ {(zeile ? "ja" : "NEIN")}); " +
                    $"neue Verbindungen am Editor-Server {verbindungen - v0}, Profil {Mp4P} am Editor-Server {(amServer ? "ANGEMELDET" : "nicht da")} " +
                    $"| alt: P verbindet sich still als Client mit dem fremden Server (hier: Charakter-Editor des Editor-Servers)");

                // ---------------------------------------------------------- (b) REL-01: P über das Fenster schließen (WM_CLOSE)
                Sp3Punkt("REL-01", "(b) P über das Fenster schließen: WM_CLOSE (wie X; echte Nachricht per PostMessage)");
                yield return Mp4Schliessen(p, logP, false, "P (ohne Netz)");

                // ---------------------------------------------------------- (c) MEHRSPIELER-05: zweite Anmeldung mit dem Profil des Hosts
                Sp3Punkt("MEHRSPIELER-05", "(c) Test-Client Q meldet sich mit dem Profil des Host-Spielers A an (kopierter Build-Ordner samt ashveil_profile.txt)");
                NetworkInventory a0 = OnlineInv(Mp4A);
                int abgewiesen0 = _mp4Abgewiesen, v1 = verbindungen;
                string logQ = Path.Combine(ordner, "Q_Host_Profil.log");
                Process q = Mp4Prozess(exe, $"-ashveil-join 127.0.0.1 -ashveil-profile {Mp4A} -screen-fullscreen 0 -screen-width 960 -screen-height 540", logQ);
                yield return WaitUntil(() => _mp4Abgewiesen > abgewiesen0 || OnlineInv(Mp4A) != a0 || q.HasExited, 120f);
                yield return new WaitForSecondsRealtime(3f);
                NetworkInventory a1 = OnlineInv(Mp4A);
                bool aBleibt = a0 != null && a1 == a0 && a0.Owner != null && a0.Owner.IsActive && a0.Owner.IsLocalClient;
                Check(aBleibt && _mp4Abgewiesen > abgewiesen0 && verbindungen > v1,
                    $"Q (Profil {Mp4A}) verbunden ({verbindungen - v1} Verbindung), abgewiesen {(_mp4Abgewiesen > abgewiesen0 ? "ja" : "NEIN")}; A bleibt der eigene Spieler des Hosts {(aBleibt ? "ja" : "NEIN")} " +
                    "| alt: A (der Host selbst) wird gespeichert und getrennt, Q übernimmt die Figur");

                // ---------------------------------------------------------- (d) REL-01: Q über Alt+F4-Weg schließen (SC_CLOSE)
                Sp3Punkt("REL-01", "(d) Q schließen wie Alt+F4 / Taskleiste → Schließen: WM_SYSCOMMAND mit SC_CLOSE");
                yield return Mp4Schliessen(q, logQ, true, "Q (abgewiesen)");

                // ---------------------------------------------------------- (e) REL-01: angemeldeter Spieler C in der Welt, WM_CLOSE
                Sp3Punkt("REL-01", "(e) C (angemeldet, in der Welt) über das Fenster schließen (WM_CLOSE)");
                NetworkInventory c = OnlineInv(Mp4C);
                string logC = Mp4ClientLog(Mp4C);
                if (!Mp4Voraus(c != null && _mp4ProzessC != null && !_mp4ProzessC.HasExited && File.Exists(logC), $"C ({Mp4C}) angemeldet, eigener Prozess und Log {Path.GetFileName(logC)}"))
                    yield break;
                // 22.10.2026 (Runde 5, Abnahme: „(e) prüft nur die Abmeldung“): Jetzt wird geprüft, dass der Server beim Trennen WIRKLICH
                // speichert – savedAtUtc liegt zwischen dem Schließen und der Abmeldung, und der Speicherstand trägt die Stelle, an der C
                // im Augenblick des Schließens stand. Damit der Stand überhaupt eine Stelle trägt, geht C vorher in eine Weltzelle
                // (Lagerplatz Grenzgebirge wie Teil 3; außerhalb jeder Zelle speichert die Sandbox keine Stelle).
                WorldStreamer wsC = WorldStreamer.Instance;
                Vector3 zielC = GG(826f, 190f);
                WorldCellDefinition zelleC = wsC != null && wsC.Manifest != null ? wsC.Manifest.CellAt(zielC, out _) : null;
                if (zelleC != null)
                {
                    yield return Preload(wsC, zielC, 30f);
                    yield return Ask(c, "goto", V(zielC, 45f));
                    yield return WaitClientCell(c, zelleC.cellId, "Ready", 25f);
                    yield return new WaitForSecondsRealtime(2f);
                }
                CharacterStore.FlushSaves();
                string zeitVorher = CharacterStore.Service.TryLoad(Mp4C, out CharacterSaveData vorherC) ? vorherC.savedAtUtc : null;
                Vector3 stelleC = c.transform.position;
                bool inZelle = zelleC != null && wsC.Manifest.CellAt(stelleC, out _) == zelleC;
                DateTime schliessenUtc = DateTime.UtcNow;
                yield return Mp4Schliessen(_mp4ProzessC, logC, false, "C (angemeldet)");
                yield return WaitUntil(() => OnlineInv(Mp4C) == null, 10f);
                DateTime abgemeldetUtc = DateTime.UtcNow;
                bool abgemeldet = OnlineInv(Mp4C) == null;
                CharacterStore.FlushSaves();
                CharacterSaveData nachC = CharacterStore.Service.TryLoad(Mp4C, out CharacterSaveData geladenC) ? geladenC : null;
                DateTime? gespeichert = Mp4Utc(nachC?.savedAtUtc);
                bool zeitOk = gespeichert.HasValue && nachC.savedAtUtc != zeitVorher &&
                              gespeichert.Value >= schliessenUtc.AddSeconds(-0.25) && gespeichert.Value <= abgemeldetUtc.AddSeconds(1.0);
                WorldPosition wpC = nachC != null ? nachC.position : default;
                bool stelleOk = inZelle && wpC.IsSet && wpC.cellId == zelleC.cellId && Flat(wpC.localPosition, stelleC) < 1.5f;
                string zeitText = nachC == null ? "Speicherstand fehlt"
                    : !gespeichert.HasValue ? $"savedAtUtc „{nachC.savedAtUtc}“ unlesbar"
                    : $"gespeichert {(gespeichert.Value - schliessenUtc).TotalSeconds:+0.00;-0.00} s nach dem Schließen, " +
                      $"{(abgemeldetUtc - gespeichert.Value).TotalSeconds:0.00} s vor der festgestellten Abmeldung (vorher {zeitVorher ?? "kein Stand"})";
                Check(abgemeldet && zeitOk && (stelleOk || !inZelle),
                    $"Server: C nach dem Schließen abgemeldet {(abgemeldet ? "ja" : "NEIN")} und beim Trennen gespeichert: {zeitText}; " +
                    $"Stelle {(wpC.IsSet ? $"{wpC.cellId} {wpC.localPosition.x:0.0}/{wpC.localPosition.z:0.0}" : "keine")} ↔ beim Schließen {zelleC?.cellId ?? "?"} " +
                    $"{stelleC.x:0.0}/{stelleC.z:0.0} ({(inZelle ? (stelleOk ? "passt" : "PASST NICHT") : "nicht geprüft – C stand in keiner Weltzelle")}) " +
                    "| alt (bis 22.10.): geprüft wurde nur die Abmeldung");
                if (!inZelle) Sp3NichtGeprueft("REL-01", "(e) Stelle im Speicherstand beim Trennen – C stand in keiner Weltzelle");
                Sp3NichtGeprueft("REL-01", "Speichern beim Beenden des HOSTS (OnApplicationQuit, savedAtUtc) – nur im gebauten Spieltest-Build: Kurztest Lauf 4 „Schliessen über das Fenster speichert beim Beenden“");
            }
            finally
            {
                sm.OnRemoteConnectionState -= zaehle;
                Application.logMessageReceived -= Mp4ReleaseLog;
            }
        }

        /// <summary>
        /// Prozess über das Fenster schließen (WM_CLOSE bzw. SC_CLOSE) und prüfen: endet über Application.Quit (Logzeile) mit Exit-Code 0.
        /// Hängt er länger als 25 s, wird er hart beendet (Fehler).
        /// </summary>
        private IEnumerator Mp4Schliessen(Process p, string log, bool altF4, string wer)
        {
            bool gesendet = false;
            try
            {
                // Echte Fensternachricht posten – NICHT Process.CloseMainWindow: das ist im Mono des Unity-Editors ein hartes
                // TerminateProcess mit Exit-Code -2 (Lauf 1 am 22.10.: P und C „beendet nach 0,0 s, Exit -2“, Q mit SC_CLOSE sauber)
                p.Refresh();
                IntPtr h = p.HasExited ? IntPtr.Zero : p.MainWindowHandle;
                if (h != IntPtr.Zero)
                    gesendet = altF4 ? PostMessage(h, Mp4WmSysCommand, Mp4ScClose, IntPtr.Zero) : PostMessage(h, Mp4WmClose, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception e) { Info($"{wer}: Schließen nicht gesendet – {e.Message}"); }
            float t0 = Time.realtimeSinceStartup;
            while (gesendet && !p.HasExited && Time.realtimeSinceStartup - t0 < 25f) yield return new WaitForSecondsRealtime(0.25f);
            bool zu = p.HasExited;
            float dauer = Time.realtimeSinceStartup - t0;
            if (!zu) { try { p.Kill(); } catch (Exception) { } }
            int code = zu ? p.ExitCode : int.MinValue;
            yield return new WaitForSecondsRealtime(0.5f);
            string text = Mp4LogLesen(log);
            bool quit = text.Contains(Mp4MarkeQuit);
            bool abbau = text.Contains("Input System module state changed to: Shutdown");
            Check(gesendet && zu && code == 0 && quit,
                $"{wer}: {(altF4 ? "SC_CLOSE (Alt+F4)" : "WM_CLOSE (X)")} {(gesendet ? "gesendet" : "NICHT gesendet")} → {(zu ? $"beendet nach {dauer:0.0} s, Exit-Code {code} (0x{code:X8})" : "HÄNGT – hart beendet")}, " +
                $"Logzeile „Fenster schliessen → Application.Quit“ {(quit ? "ja" : "NEIN")}, Abbau bis „Input System … Shutdown“ {(abbau ? "ja" : "nein")} " +
                "| alt: Beenden innerhalb der Fensterprozedur, danach Absturz 0xC0000005 (Exit ≠ 0, keine Logzeile)");
        }

        private Process Mp4Prozess(string exe, string argumente, string log)
        {
            try { if (File.Exists(log)) File.Delete(log); } catch (IOException) { }
            Process p = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = argumente + $" -logFile \"{log}\"",
                WorkingDirectory = Path.GetDirectoryName(exe),
                UseShellExecute = false
            });
            if (p == null) throw new InvalidOperationException("Test-Client konnte nicht gestartet werden.");
            Clients.Add(p);
            return p;
        }

        /// <summary>Eigenes Log je Test-Client-Profil (Mp4StarteClient), damit Teil 5 die Logzeilen von C lesen kann.</summary>
        private static string Mp4ClientLog(string profil) => Ashveil.Core.ProjectPaths.Logs("Mehrspieler4", profil + ".log");

        private static bool Mp4LogHat(string pfad, string teil) => Mp4LogLesen(pfad).Contains(teil);

        /// <summary>22.10.2026 (Runde 5): savedAtUtc („o“-Format) als UTC-Zeit, sonst null.</summary>
        private static DateTime? Mp4Utc(string zeit) =>
            !string.IsNullOrEmpty(zeit) && DateTime.TryParse(zeit, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out DateTime t) ? t.ToUniversalTime() : (DateTime?)null;

        private static string Mp4LogLesen(string pfad)
        {
            try
            {
                if (!File.Exists(pfad)) return "";
                using (var fs = new FileStream(pfad, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs))
                    return r.ReadToEnd();
            }
            catch (Exception) { return ""; }
        }

        private void Mp4ReleaseLog(string message, string stackTrace, LogType type)
        {
            if (message != null && message.Contains($"Profil '{Mp4A}' gehört dem Host-Spieler") && message.Contains("abgewiesen")) _mp4Abgewiesen++;
        }
    }
}
