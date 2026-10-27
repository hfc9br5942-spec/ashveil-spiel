using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Ashveil.Core;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Blender-Erweiterungen der Ashveil-Pipeline (Max 06.10.2026: „alle empfohlenen“; nur kostenlos, nur offizielle Quelle).
    ///  1. Herunterladen: jede Datei von extensions.blender.org (offizielle Plattform der Blender Foundation), geprüft gegen die
    ///     dort veröffentlichte SHA-256-Prüfsumme. Gespeichert wird nur, was passt – unter Tools/Blender/Erweiterungen (versioniert).
    ///  2. Installieren (Blender geschlossen): über Blenders eigene Kommandozeile („--command extension install-file -e“) in die
    ///     neueste stabile Blender-Version, dazu die mitgelieferten Rigify und Node Wrangler einschalten.
    ///  3. Bericht: Logs/Ashveil/Blender/Erweiterungen_*.txt – Version, eingeschaltet, Probe wie in der Pipeline (--factory-startup).
    /// Keine Konten, keine Schlüssel, nichts Kostenpflichtiges. Neue Blender-Version (z. B. 5.3): Schritt 2 erneut ausführen.
    /// </summary>
    public static class BlenderErweiterungen
    {
        public sealed class Erweiterung
        {
            public string Name, Slug, Version, Datei, Sha256, MinBlender, Lizenz, Zweck;
            public float Kb;

            /// <summary>Download-Adresse wie auf der Plattform angegeben (die Prüfsumme ist Teil der Adresse).</summary>
            public string Url => "https://extensions.blender.org/download/sha256:" + Sha256 + "/" + Datei +
                                 "?repository=%2Fapi%2Fv1%2Fextensions%2F&blender_version_min=" + MinBlender;

            public string Seite => "https://extensions.blender.org/add-ons/" + Slug + "/";

            /// <summary>Für den Python-Helfer: Name oder Plattform-Kennung (der Helfer nimmt, was passt).</summary>
            public string Kurzname => Name + "|" + Slug;
        }

        /// <summary>Stand 06.10.2026, abgelesen auf extensions.blender.org (Version, Datei, Prüfsumme, Größe, Lizenz).</summary>
        public static readonly Erweiterung[] Liste =
        {
            new Erweiterung { Name = "Cell Fracture", Slug = "cell-fracture", Version = "0.2.1", Datei = "add-on-cell-fracture-v0.2.1.zip",
                Sha256 = "878bbd46e0c735da689101aeed9ebe99f18c0d8d0cbd2e9e17ed8cda6b264fb2", MinBlender = "4.2.0", Kb = 9.5f,
                Lizenz = "GPL-3.0-or-later", Zweck = "vorgebrochene Teile: zerfallende Verblichene, splitternde Hörner und Panzer, Steinschlag" },
            new Erweiterung { Name = "Bool Tool", Slug = "bool-tool", Version = "2.1.0", Datei = "add-on-bool-tool-v2.1.0.zip",
                Sha256 = "d3a282db25925d115dd1a7638aa28116f2f9198423b0afb6179e8127d59c06e1", MinBlender = "4.5.0", Kb = 169.0f,
                Lizenz = "GPL-3.0-or-later", Zweck = "saubere Schnitte an Trennstellen, Stumpfkappen, Waffen, Rüstung, Gebäude" },
            new Erweiterung { Name = "LoopTools", Slug = "looptools", Version = "4.7.7", Datei = "add-on-looptools-v4.7.7.zip",
                Sha256 = "ff1ca3b3fff73094379da8b1fa2c1acbc9d88d26b7dfc73bb9de5941a6b50108", MinBlender = "4.2.0", Kb = 29.8f,
                Lizenz = "GPL-2.0-or-later", Zweck = "runde, saubere Schnittringe an Gelenken, Modellierhilfen" },
            new Erweiterung { Name = "3D Print Toolbox", Slug = "print3d-toolbox", Version = "1.4.1", Datei = "add-on-print3d-toolbox-v1.4.1.zip",
                Sha256 = "cf5952c84d802a8df67368fd866e8fc01424bd1d71ab5db39d187dfbcfc6f2fd", MinBlender = "4.2.0", Kb = 145.5f,
                Lizenz = "GPL-3.0-or-later", Zweck = "Netzprüfung: Löcher, Überschneidungen, kaputte Flächen" },
            new Erweiterung { Name = "Texel Density Checker", Slug = "texel-density-checker", Version = "2026.1.1",
                Datei = "add-on-texel-density-checker-v2026.1.1.zip",
                Sha256 = "ca5554ec5c08a830c344ddc08a4c4f8f2e619178ac05ef582ea0293ab540308d", MinBlender = "4.2.0", Kb = 25.6f,
                Lizenz = "GPL-3.0-or-later", Zweck = "gleich scharfe Texturen über Gegner, Waffen und Rüstungen" },
            // 19.10.2026: Ucupaint 2.4.9 NICHT hier – es ist (wie MPFB2) schon über das Online-Repository blender_org installiert;
            // eine zweite Kopie unter user_default lässt sich nicht einschalten („Another ucupaint version is installed“).
            // Geprüfte Kopie: Erweiterungen/add-on-ucupaint-v2.4.9.zip (SHA-256 c4d87fd9…f5f8), siehe LIESMICH.md.
        };

        /// <summary>Mit Blender ausgeliefert (Kern-Add-ons) – nur einschalten.</summary>
        public static readonly string[] Mitgeliefert = { "rigify", "node_wrangler" };

        public static string Ordner => ProjectPaths.Tools("Blender", "Erweiterungen");
        public static string Pfad(Erweiterung e) => Path.Combine(Ordner, e.Datei);
        private static string Helfer => ProjectPaths.Tools("Blender", "ashveil_erweiterungen.py");
        private static string BerichtOrdner => ProjectPaths.Logs("Blender");

        // ================================================================================================== Menüs

        [MenuItem("Ashveil/Blender/Erweiterungen herunterladen (extensions.blender.org)", priority = 620)]
        public static void HerunterladenMenu()
        {
            bool ok = Herunterladen(true, out string text);
            Melden(ok, text);
        }

        [MenuItem("Ashveil/Blender/Erweiterungen installieren (Blender vorher schließen)", priority = 621)]
        public static void InstallierenMenu()
        {
            bool ok = Installieren(true, out string text);
            Melden(ok, text);
        }

        [MenuItem("Ashveil/Blender/Erweiterungen: Bericht", priority = 622)]
        public static void BerichtMenu()
        {
            bool ok = Bericht(true, out string text);
            Melden(ok, text);
        }

        private static void Melden(bool ok, string text)
        {
            if (ok) Debug.Log("[Ashveil] " + text);
            else Debug.LogWarning("[Ashveil] " + text);
            string kurz = text.Length > 1500 ? text.Substring(0, 1500) + " …" : text;
            EditorUtility.DisplayDialog("Ashveil – Blender-Erweiterungen", kurz, "OK");
        }

        // ================================================================================================== 1. Herunterladen

        /// <summary>Lädt fehlende Dateien und prüft jede gegen ihre SHA-256-Prüfsumme. Nur passende Dateien werden gespeichert.</summary>
        public static bool Herunterladen(bool interaktiv, out string bericht)
        {
            Directory.CreateDirectory(Ordner);
            var sb = new StringBuilder();
            int neu = 0, vorhanden = 0, fehler = 0;
            try
            {
                // WebClient (System.dll) statt HttpClient: in jedem Unity-Kompatibilitätsprofil vorhanden, synchron
                using (var client = new WebClient())
                {
                    client.Headers.Add(HttpRequestHeader.UserAgent, "Ashveil-Unity/1.0");
                    for (int i = 0; i < Liste.Length; i++)
                    {
                        Erweiterung e = Liste[i];
                        string ziel = Pfad(e);
                        if (Geprueft(ziel, e))
                        {
                            vorhanden++;
                            sb.AppendLine($"  vorhanden, Prüfsumme stimmt: {e.Datei}");
                            continue;
                        }
                        if (interaktiv) EditorUtility.DisplayProgressBar("Blender-Erweiterungen", $"Lade {e.Name} {e.Version} …", (float)i / Liste.Length);
                        byte[] daten;
                        try
                        {
                            daten = client.DownloadData(e.Url);
                        }
                        catch (Exception ex)
                        {
                            fehler++;
                            sb.AppendLine($"  FEHLER {e.Datei}: {Innerste(ex)}");
                            continue;
                        }
                        string hash = Sha256Hex(daten);
                        if (!string.Equals(hash, e.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            fehler++;
                            sb.AppendLine($"  FEHLER {e.Datei}: Prüfsumme {hash} statt {e.Sha256} – nicht gespeichert");
                            continue;
                        }
                        File.WriteAllBytes(ziel, daten);
                        neu++;
                        sb.AppendLine($"  geladen, Prüfsumme stimmt: {e.Datei} ({daten.Length / 1024f:0.0} KB)");
                    }
                }
            }
            finally
            {
                if (interaktiv) EditorUtility.ClearProgressBar();
            }
            SchreibeLiesmich();
            bericht = $"Blender-Erweiterungen: {neu} geladen, {vorhanden} schon da, {fehler} Fehler (Quelle extensions.blender.org, " +
                      $"SHA-256 geprüft) → {Ordner}\n{sb}";
            return fehler == 0;
        }

        // ================================================================================================== 2. Installieren

        /// <summary>Installiert alle geprüften Dateien in die neueste stabile Blender-Version und schaltet sie ein.</summary>
        public static bool Installieren(bool interaktiv, out string bericht)
        {
            string blender = ArtPipeline.FindBlender();
            if (blender == null)
            {
                bericht = "Blender nicht gefunden (erwartet unter C:/Program Files/Blender Foundation/Blender x.y).";
                return false;
            }
            if (BlenderOffen(out string offen))
            {
                bericht = $"Blender ist geöffnet ({offen}). Bitte Blender schließen und erneut starten – die Installation schreibt Blenders Einstellungen.";
                return false;
            }
            List<string> fehlend = Liste.Where(e => !Geprueft(Pfad(e), e)).Select(e => e.Datei).ToList();
            if (fehlend.Count > 0)
            {
                bericht = "Zuerst „Erweiterungen herunterladen“ – fehlt oder Prüfsumme falsch: " + string.Join(", ", fehlend);
                return false;
            }
            string version = ArtPipeline.BlenderVersion(blender);
            var sb = new StringBuilder();
            sb.AppendLine("ASHVEIL Blender-Erweiterungen – Installation " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("Blender: " + blender + " · " + version);
            sb.AppendLine();
            int installiert = 0, mitgeliefert;
            try
            {
                for (int i = 0; i < Liste.Length; i++)
                {
                    Erweiterung e = Liste[i];
                    if (interaktiv) EditorUtility.DisplayProgressBar("Blender-Erweiterungen", $"Installiere {e.Name} …", (float)i / (Liste.Length + 4));
                    int code = Starte(blender, $"--command extension install-file -r user_default -e \"{Pfad(e)}\"", 300, out string aus);
                    sb.AppendLine($"== {e.Name} {e.Version}: Exit {code}").AppendLine(Einruecken(aus));
                    if (code == 0) installiert++;
                }
                if (interaktiv) EditorUtility.DisplayProgressBar("Blender-Erweiterungen", "Rigify und Node Wrangler einschalten …", 0.75f);
                mitgeliefert = Starte(blender, $"-b --python-exit-code 3 -P \"{Helfer}\" -- aktivieren {string.Join(" ", Mitgeliefert)}", 300, out string aus2);
                sb.AppendLine($"== Mitgelieferte einschalten ({string.Join(", ", Mitgeliefert)}): Exit {mitgeliefert}").AppendLine(Einruecken(aus2));
                if (installiert < Liste.Length)
                {
                    Starte(blender, "--command extension repo-list", 120, out string repos);
                    sb.AppendLine("== Repositories (zur Fehlersuche)").AppendLine(Einruecken(repos));
                }
            }
            finally
            {
                if (interaktiv) EditorUtility.ClearProgressBar();
            }
            string pruef = Pruefen(blender, interaktiv, sb, out bool pruefOk);
            string datei = SchreibeBericht("Installation", sb);
            bool ok = installiert == Liste.Length && mitgeliefert == 0 && pruefOk;
            bericht = $"Blender-Erweiterungen in {version}: {installiert}/{Liste.Length} installiert, mitgelieferte " +
                      (mitgeliefert == 0 ? "eingeschaltet" : "FEHLER") + $" · {pruef} · Bericht {datei}";
            return ok;
        }

        // ================================================================================================== 3. Bericht

        /// <summary>Nur prüfen: Was ist in Blender installiert und eingeschaltet, und lässt es sich in der Pipeline einschalten?</summary>
        public static bool Bericht(bool interaktiv, out string bericht)
        {
            string blender = ArtPipeline.FindBlender();
            if (blender == null)
            {
                bericht = "Blender nicht gefunden.";
                return false;
            }
            var sb = new StringBuilder();
            sb.AppendLine("ASHVEIL Blender-Erweiterungen – Bericht " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("Blender: " + blender + " · " + ArtPipeline.BlenderVersion(blender));
            sb.AppendLine();
            string pruef = Pruefen(blender, interaktiv, sb, out bool ok);
            string datei = SchreibeBericht("Bericht", sb);
            bericht = $"Blender-Erweiterungen ({ArtPipeline.BlenderVersion(blender)}): {pruef} · Bericht {datei}";
            return ok;
        }

        /// <summary>Stand in Blenders Einstellungen, Probe mit Werkseinstellungen (wie die Pipeline) und Blenders eigene Liste.</summary>
        private static string Pruefen(string blender, bool interaktiv, StringBuilder sb, out bool ok)
        {
            string stempel = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            Directory.CreateDirectory(BerichtOrdner);
            string jsonStand = Path.Combine(BerichtOrdner, $"Erweiterungen_{stempel}_einstellungen.json");
            string jsonProbe = Path.Combine(BerichtOrdner, $"Erweiterungen_{stempel}_pipeline.json");
            string namen = string.Join(" ", Liste.Select(e => e.Kurzname).Concat(Mitgeliefert).Select(k => "\"" + k + "\""));
            int an = 0, probe = 0, gesamt = Liste.Length + Mitgeliefert.Length;
            try
            {
                if (interaktiv) EditorUtility.DisplayProgressBar("Blender-Erweiterungen", "Prüfe den Stand …", 0.85f);
                int c1 = Starte(blender, $"-b --python-exit-code 3 -P \"{Helfer}\" -- bericht \"{jsonStand}\" {namen}", 300, out string a1);
                an = a1.Split('\n').Count(l => l.Contains("[Ashveil] STAND") && l.Contains(": an "));
                sb.AppendLine($"== Stand in Blenders Einstellungen: Exit {c1}, {an}/{gesamt} eingeschaltet").AppendLine(Einruecken(a1));
                if (interaktiv) EditorUtility.DisplayProgressBar("Blender-Erweiterungen", "Probe wie in der Pipeline …", 0.92f);
                int c2 = Starte(blender, $"-b --factory-startup --python-exit-code 3 -P \"{Helfer}\" -- probe \"{jsonProbe}\" {namen}", 300, out string a2);
                probe = a2.Split('\n').Count(l => l.Contains("[Ashveil] an: "));
                sb.AppendLine($"== Probe mit Werkseinstellungen (Pipeline): Exit {c2}, {probe}/{gesamt} einschaltbar").AppendLine(Einruecken(a2));
                Starte(blender, "--command extension list", 120, out string a3);
                sb.AppendLine("== Blenders Liste (extension list)").AppendLine(Einruecken(a3));
                foreach (string j in new[] { jsonStand, jsonProbe })
                    if (File.Exists(j)) sb.AppendLine("== " + Path.GetFileName(j)).AppendLine(File.ReadAllText(j));
            }
            finally
            {
                if (interaktiv) EditorUtility.ClearProgressBar();
            }
            ok = an == gesamt && probe == gesamt;
            return $"{an}/{gesamt} eingeschaltet, {probe}/{gesamt} in der Pipeline nutzbar";
        }

        // ================================================================================================== Hilfen

        private static bool Geprueft(string pfad, Erweiterung e)
        {
            if (!File.Exists(pfad)) return false;
            try { return string.Equals(Sha256Hex(File.ReadAllBytes(pfad)), e.Sha256, StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }

        public static string Sha256Hex(byte[] daten)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(daten);
                var sb = new StringBuilder(h.Length * 2);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static bool BlenderOffen(out string welche)
        {
            var namen = new List<string>();
            foreach (string n in new[] { "blender", "blender-launcher" })
            {
                foreach (Process p in Process.GetProcessesByName(n))
                {
                    try { namen.Add(n + " (PID " + p.Id + ")"); }
                    catch (Exception) { }
                    finally { p.Dispose(); }
                }
            }
            welche = string.Join(", ", namen);
            return namen.Count > 0;
        }

        /// <summary>Blender mit Argumenten starten, Ausgabe sammeln. Rückgabe: Exit-Code (−1 Startfehler, −2 Zeitlimit).</summary>
        private static int Starte(string exe, string args, int sekunden, out string ausgabe)
        {
            var sb = new StringBuilder();
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = ProjectPaths.ProjectRoot,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            int code;
            try
            {
                using (Process p = Process.Start(psi))
                {
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine("ERR " + e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(sekunden * 1000))
                    {
                        try { p.Kill(); } catch (Exception) { }
                        lock (sb) sb.AppendLine($"(nach {sekunden} s abgebrochen)");
                        code = -2;
                    }
                    else
                    {
                        p.WaitForExit();
                        code = p.ExitCode;
                    }
                }
            }
            catch (Exception ex)
            {
                lock (sb) sb.AppendLine("Start fehlgeschlagen: " + ex.Message);
                code = -1;
            }
            lock (sb) ausgabe = sb.ToString();
            return code;
        }

        private static string SchreibeBericht(string art, StringBuilder sb)
        {
            Directory.CreateDirectory(BerichtOrdner);
            string pfad = Path.Combine(BerichtOrdner, $"Erweiterungen_{DateTime.Now:yyyy-MM-dd_HHmmss}_{art}.txt");
            File.WriteAllText(pfad, sb.ToString(), new UTF8Encoding(false));
            return pfad;
        }

        /// <summary>Liesmich neben den Dateien: Herkunft, Version, Lizenz, Prüfsumme, Zweck (Toolchain-Direktive: Quellen nachweisen).</summary>
        private static void SchreibeLiesmich()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Blender-Erweiterungen (Ashveil-Pipeline)");
            sb.AppendLine();
            sb.AppendLine("Freigabe Max 06.10.2026 („alle empfohlenen“). Quelle ausschließlich **extensions.blender.org** (offizielle Plattform der");
            sb.AppendLine("Blender Foundation). Alle kostenlos, Quelltext liegt in den ZIP-Dateien (GPL). Geladen und geprüft über");
            sb.AppendLine("Unity › Ashveil › Blender › Erweiterungen herunterladen; installiert über … › Erweiterungen installieren.");
            sb.AppendLine();
            sb.AppendLine("| Erweiterung | Version | Lizenz | Zweck | Datei | SHA-256 |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (Erweiterung e in Liste)
                sb.AppendLine($"| [{e.Name}]({e.Seite}) | {e.Version} | {e.Lizenz} | {e.Zweck} | `{e.Datei}` | `{e.Sha256}` |");
            sb.AppendLine();
            sb.AppendLine("Mitgeliefert mit Blender, nur eingeschaltet: " + string.Join(", ", Mitgeliefert) + ".");
            sb.AppendLine();
            sb.AppendLine("Pipeline-Skripte (Blender mit --factory-startup) schalten benötigte Erweiterungen selbst ein:");
            sb.AppendLine("`import ashveil_erweiterungen as erw; erw.bereit(\"cell_fracture\")` (siehe Tools/Blender/ashveil_erweiterungen.py).");
            File.WriteAllText(Path.Combine(Ordner, "LIESMICH.md"), sb.ToString(), new UTF8Encoding(false));
        }

        private static string Einruecken(string text)
        {
            if (string.IsNullOrEmpty(text)) return "  (keine Ausgabe)";
            string[] zeilen = text.Replace("\r", "").TrimEnd().Split('\n');
            if (zeilen.Length > 120) zeilen = zeilen.Take(60).Concat(new[] { $"… ({zeilen.Length - 100} Zeilen ausgelassen) …" }).Concat(zeilen.Skip(zeilen.Length - 40)).ToArray();
            return string.Join("\n", zeilen.Select(z => "  " + z));
        }

        private static string Innerste(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex.GetType().Name + ": " + ex.Message;
        }
    }
}
