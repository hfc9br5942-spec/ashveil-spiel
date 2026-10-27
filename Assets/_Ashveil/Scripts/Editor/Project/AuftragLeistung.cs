using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// #29 Begehung und Leistung (Welle 0, 07.10.2026) – zwei feste Aufträge für den Auftragsordner:
    ///   „messung_kv4“            Leistungsmessung CORE-005 im fertigen Spiel wie Menü 10 (Mess-Build bauen, starten, Blickpunkte
    ///                            messen). Wartet auf Builds/Messung_KarethV4/KV4_Messung.txt und meldet Mittelwerte, langsamsten
    ///                            Blickpunkt, Ladezeit, Speicher und Größe des Builds.
    ///   „projekt_einstellungen“  „pruefen“ (Standard) oder „setzen“: Prebake Collision Meshes (Kollisionsnetze beim Bauen statt beim
    ///                            Laden backen). Surface-Cache-GI bleibt unberührt: Die Renderer-Funktion ist absichtlich da und aus
    ///                            (DynamicGIBuildSupport – nur während eines Builds an, damit die Varianten des Ashveil-GI bleiben);
    ///                            die Bau-Meldung „incompatible with Static Batching“ ist deshalb erwartet.
    /// </summary>
    [InitializeOnLoad]
    public static class AuftragLeistung
    {
        private const int MessungMinuten = 30;

        static AuftragLeistung()
        {
            Auftragsordner.Registrieren("messung_kv4", MessungKV4, true, MessungMinuten * 60 + 600);
            Auftragsordner.Registrieren("projekt_einstellungen", ProjektEinstellungen, false, 300);
        }

        // ================================================================== Messung CORE-005

        private static void MessungKV4(Auftragsordner.Auftrag a, Action<bool, string> fertig)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                fertig(false, "Play läuft – nichts gestartet");
                return;
            }
            string root = Directory.GetParent(Application.dataPath).FullName;
            string dir = Path.Combine(root, "Builds", "Messung_KarethV4");
            string exe = Path.Combine(dir, "AshveilKV4.exe");
            string txt = Path.Combine(dir, "KV4_Messung.txt");
            DateTime start = DateTime.Now;
            try
            {
                WeltMaterialtest.KarethV4BuildMessung();
            }
            catch (Exception e)
            {
                fertig(false, "Mess-Build: " + e.GetType().Name + ": " + e.Message);
                return;
            }
            // Die .exe behält beim Bauen das Datum der Unity-Vorlage – frisch geschrieben sind die Szenendaten im _Data-Ordner
            string daten = Path.Combine(dir, "AshveilKV4_Data");
            bool neu = File.Exists(exe) && Directory.Exists(daten) &&
                       new DirectoryInfo(daten).EnumerateFiles("*", SearchOption.TopDirectoryOnly).Any(f => f.LastWriteTime > start.AddSeconds(-5));
            if (!neu)
            {
                fertig(false, "Mess-Build nicht erstellt (Bau fehlgeschlagen, siehe Protokoll)");
                return;
            }
            long bytes = Ordnergroesse(dir);
            double naechster = 0;
            void Warten()
            {
                if (EditorApplication.timeSinceStartup < naechster) return;
                naechster = EditorApplication.timeSinceStartup + 5.0;
                if ((DateTime.Now - start).TotalMinutes > MessungMinuten)
                {
                    EditorApplication.update -= Warten;
                    fertig(false, $"keine Messung nach {MessungMinuten} min (Mess-Build {bytes / 1048576} MB läuft noch oder ist abgestürzt: {dir}\\player.log)");
                    return;
                }
                if (!File.Exists(txt) || File.GetLastWriteTime(txt) < start) return;
                string text;
                try { text = File.ReadAllText(txt); }
                catch (IOException) { return; }   // wird noch geschrieben
                if (!text.Contains("Fehler/Ausnahmen im Log")) return;
                EditorApplication.update -= Warten;
                fertig(true, Zusammenfassung(text, bytes, txt));
            }
            EditorApplication.update -= Warten;
            EditorApplication.update += Warten;
        }

        private static long Ordnergroesse(string dir)
        {
            try
            {
                return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories)
                    .Where(f => !f.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && !f.Name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) &&
                                !f.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    .Sum(f => f.Length);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>Kopfzeilen, Mittelwerte und der langsamste Blickpunkt aus KV4_Messung.txt.</summary>
        private static string Zusammenfassung(string text, long bytes, string pfad)
        {
            var sb = new StringBuilder();
            string[] lines = text.Replace("\r", "").Split('\n');
            foreach (string l in lines)
            {
                string t = l.Trim();
                if (t.StartsWith("Grafikkarte:") || t.StartsWith("Bild:") || t.StartsWith("Szene:") || t.StartsWith("Ladezeit:") ||
                    t.StartsWith("Mittel über") || t.StartsWith("kachelfrei aus GPU") || t.StartsWith("Speicher am Ende") || t.StartsWith("Fehler/Ausnahmen"))
                    sb.AppendLine(t);
            }
            // langsamster Blickpunkt (erste Variante je Blickpunkt)
            string ort = null, langsam = null;
            float min = float.MaxValue;
            var fps = new Regex(@"^\s*(kachelfrei an |kachelfrei aus|\s{13})?\s*(\d+)\s+Bilder/s");
            string aktuell = null;
            bool erste = false;
            foreach (string l in lines)
            {
                if (l.StartsWith("[") && l.TrimEnd().EndsWith("]"))
                {
                    aktuell = l.Trim().Trim('[', ']');
                    erste = true;
                    continue;
                }
                Match m = fps.Match(l);
                if (!m.Success || aktuell == null || !erste) continue;
                erste = false;
                float f = float.Parse(m.Groups[2].Value);
                if (f < min)
                {
                    min = f;
                    ort = aktuell;
                    langsam = l.Trim();
                }
            }
            if (ort != null) sb.AppendLine($"Langsamster Blickpunkt: {ort} – {langsam}");
            sb.AppendLine($"Mess-Build {bytes / 1048576.0:0} MB (ohne Bilder und Protokolle) · Bericht {pfad}");
            return sb.ToString().TrimEnd();
        }

        // ================================================================== Projekt-Einstellungen

        private static void ProjektEinstellungen(Auftragsordner.Auftrag a, Action<bool, string> fertig)
        {
            string p = (a.parameter ?? "").Trim().ToLowerInvariant();
            if (p != "" && p != "pruefen" && p != "setzen")
            {
                fertig(false, "erlaubt: pruefen, setzen");
                return;
            }
            bool vorher = PlayerSettings.bakeCollisionMeshes;
            if (p == "setzen" && !vorher)
            {
                PlayerSettings.bakeCollisionMeshes = true;
                AssetDatabase.SaveAssets();
            }
            bool nachher = PlayerSettings.bakeCollisionMeshes;
            bool feature = Rendering.DynamicGIBuildSupport.FeatureType != null;
            fertig(true,
                $"Prebake Collision Meshes: {(vorher ? "an" : "aus")}{(nachher != vorher ? " → an (Kollisionsnetze werden beim Bauen gebacken, nicht beim Laden)" : "")} · " +
                $"Surface-Cache-GI: unverändert – {(feature ? "Renderer-Funktion vorhanden und aus" : "Funktion in dieser URP-Version nicht gefunden")}, " +
                "nur während eines Builds an (DynamicGIBuildSupport, Varianten des Ashveil-GI); die Bau-Meldung „incompatible with Static Batching“ ist erwartet");
        }
    }
}
