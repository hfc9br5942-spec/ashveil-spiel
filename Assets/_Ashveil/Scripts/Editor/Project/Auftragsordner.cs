using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Ashveil.Core;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Auftragsordner (06.10.2026, für Claude): Ein Auftrag ist eine kleine JSON-Datei in Tools/Auftraege/neu, z. B.
    ///   { "id": "2026-10-06_1200_bauen", "aktion": "kareth_v4_bauen", "parameter": "" }
    /// Der Editor führt NUR die hier freigegebenen Aktionen aus und schreibt das Ergebnis nach Tools/Auftraege/fertig
    /// (&lt;id&gt;.ergebnis.json: Status, Meldung, Dauer, mitgeschriebene [Ashveil]-Meldungen, Warnungen und Fehler).
    /// So laufen Kompilieren, Bauen, Prüfen, Bilder, Tests und Blender-Schritte ohne Klicks im Editor.
    ///
    /// Grenzen: keine freien Befehle oder Menüpfade, Play nur über feste Spiel-Suiten (<see cref="AuftragSpiel"/>, Max 06.10.
    /// abends), nichts bei ungespeicherten Szenen (szenenändernde
    /// Aktionen werden dann abgelehnt), Blender nur mit Skripten Tools/Blender/ashveil_*.py und Pfaden im Projekt.
    /// Pause/Weiter: Ashveil › Projekt › Auftragsordner aktiv. Aktionen siehe Tools/Auftraege/LIESMICH.md.
    /// </summary>
    [InitializeOnLoad]
    public static class Auftragsordner
    {
        [Serializable]
        public class Auftrag
        {
            public string id;
            public string aktion;
            public string parameter;
        }

        [Serializable]
        public class Ergebnis
        {
            public string id, aktion, parameter, status, meldung, beginn, ende, szene, unity, protokoll;
            public float sekunden;
        }

        /// <summary>Aktion: führt den Auftrag aus und ruft genau einmal fertig(ok, meldung) auf (sofort oder später).</summary>
        public delegate void Handler(Auftrag auftrag, Action<bool, string> fertig);

        private sealed class Eintrag
        {
            public Handler Handler;
            public bool SzeneSicher;
            public int Zeitlimit;
        }

        public const string PrefAktiv = "Ashveil.Auftragsordner.Aktiv";
        private const string MenuAktiv = "Ashveil/Projekt/Auftragsordner aktiv";
        private const string KeyKompId = "Ashveil.Auftrag.Komp.Id";
        private const string KeyKompDatei = "Ashveil.Auftrag.Komp.Datei";
        private const string KeyKompStart = "Ashveil.Auftrag.Komp.Start";
        private const string KeyKompGestartet = "Ashveil.Auftrag.Komp.Gestartet";
        private const string KeyKompFertig = "Ashveil.Auftrag.Komp.FertigOhneFehler";
        private const string KeyKompNeugeladen = "Ashveil.Auftrag.Komp.Neugeladen";
        private const int MaxProtokoll = 200_000;

        private static readonly Dictionary<string, Eintrag> Aktionen = new Dictionary<string, Eintrag>(StringComparer.OrdinalIgnoreCase);
        private static readonly Regex Unerlaubt = new Regex(@"[^A-Za-z0-9_.\-]");
        private static readonly Regex SkriptMuster = new Regex(@"^ashveil_[a-z0-9_]+\.py$");
        private static readonly Regex ArgMuster = new Regex(@"^[A-Za-z0-9_\-\.:/\\ ="",]*$");
        private static readonly List<string> KompFehler = new List<string>();

        private static double _naechsterBlick;
        private static Auftrag _laufend;
        private static Ergebnis _ergebnis;
        private static string _laufendDatei;
        private static DateTime _start;
        private static StringBuilder _protokoll;
        private static int _zeitlimit;

        public static string Wurzel => ProjectPaths.Tools("Auftraege");
        private static string Neu => Path.Combine(Wurzel, "neu");
        private static string Laufend => Path.Combine(Wurzel, "laufend");
        private static string Fertig => Path.Combine(Wurzel, "fertig");

        public static bool Aktiv => EditorPrefs.GetBool(PrefAktiv, true);

        /// <summary>ID des gerade laufenden Auftrags (z. B. für Protokolldateien), sonst null.</summary>
        public static string LaufendeId => _laufend?.id;

        static Auftragsordner()
        {
            Registrieren("status", Status, false, 120);
            Registrieren("kareth_v4_bauen", (a, fertig) => fertig(true, KarethV4Builder.Build()), true, 3600);
            // 07.10.2026: Gegner-Prefabs an Max' Entscheidungen anpassen (Wolf ohne Glut, Seitschritt, Bogen statt Pistole), ohne Szene
            Registrieren("gegner_anpassen", (a, fertig) => fertig(true, EnemyBuilder.ApplyDecisions0710()), true, 600);
            // #28 (07.10.2026): Daten der elf Ergänzungsaufträge – Tiere, Beute, Aufträge, Figuren, Datenbank (ohne Szene)
            Registrieren("inhalte_28", (a, fertig) => { (bool ok, string m) = Kareth28Inhalte.Run(); fertig(ok, m); }, true, 1800);
            Registrieren("weltpruefung", Weltpruefung, true, 3600);
            Registrieren("review_bilder", ReviewBilder, true, 3600);
            Registrieren("szene_oeffnen", SzeneOeffnen, true, 600);
            // 22.10.: Szene aus der Liste ohne Speichern neu laden (verwirft ungespeicherte Änderungen) – z. B. nach einer versehentlichen
            // Änderungsmarke durch ein Werkzeug; bewusst ohne Szenen-Schutz, sonst wäre der Ausweg selbst gesperrt.
            Registrieren("szene_verwerfen", SzeneOeffnen, false, 600);
            Registrieren("blender_erweiterungen_laden", (a, fertig) => fertig(BlenderErweiterungen.Herunterladen(false, out string t), t), false, 900);
            Registrieren("blender_erweiterungen_installieren", (a, fertig) => fertig(BlenderErweiterungen.Installieren(false, out string t), t), false, 3600);
            Registrieren("blender_erweiterungen_bericht", (a, fertig) => fertig(BlenderErweiterungen.Bericht(false, out string t), t), false, 1800);
            Registrieren("blender_skript", BlenderSkript, false, 3600);
            Registrieren("suchen", Suchen, false, 300);
            // „kompilieren“ läuft über das Neuladen der Skripte hinweg und wird gesondert behandelt (siehe unten).

            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            CompilationPipeline.compilationStarted -= KompilierungBeginnt;
            CompilationPipeline.compilationStarted += KompilierungBeginnt;
            CompilationPipeline.assemblyCompilationFinished -= AssemblyFertig;
            CompilationPipeline.assemblyCompilationFinished += AssemblyFertig;
            CompilationPipeline.compilationFinished -= KompilierungEndet;
            CompilationPipeline.compilationFinished += KompilierungEndet;
            EditorApplication.delayCall += NachDemLaden;
            // Läuft ein Kompilier-Auftrag, sind wir gerade nach dem Neuladen der Skripte hier (für die Meldung)
            if (KompilierAuftragOffen) SessionState.SetBool(KeyKompNeugeladen, true);
        }

        /// <summary>Weitere Aktion freigeben (z. B. „tests“ aus Ashveil.EditorAuftraege).</summary>
        public static void Registrieren(string aktion, Handler handler, bool szeneSicher, int zeitlimitSekunden)
        {
            Aktionen[aktion] = new Eintrag { Handler = handler, SzeneSicher = szeneSicher, Zeitlimit = Math.Max(30, zeitlimitSekunden) };
        }

        // ================================================================================================== Menüs

        [MenuItem(MenuAktiv, priority = 905)]
        private static void Umschalten()
        {
            EditorPrefs.SetBool(PrefAktiv, !Aktiv);
            Debug.Log("[Ashveil] Auftragsordner " + (Aktiv ? "aktiv" : "pausiert") + " – " + Neu);
        }

        [MenuItem(MenuAktiv, true)]
        private static bool UmschaltenPruefen()
        {
            Menu.SetChecked(MenuAktiv, Aktiv);
            return true;
        }

        [MenuItem("Ashveil/Projekt/Auftragsordner öffnen", priority = 906)]
        private static void Oeffnen()
        {
            Directory.CreateDirectory(Neu);
            EditorUtility.RevealInFinder(Neu);
        }

        // ================================================================================================== Ablauf

        private static void Tick()
        {
            double jetzt = EditorApplication.timeSinceStartup;
            if (jetzt < _naechsterBlick) return;
            _naechsterBlick = jetzt + 2.0;
            try
            {
                if (KompilierAuftragOffen)
                {
                    PruefeKompilierAuftrag();
                    return;
                }
                if (_laufend != null)
                {
                    if ((DateTime.Now - _start).TotalSeconds > _zeitlimit) Abschliessen(false, $"Zeitlimit {_zeitlimit} s überschritten", "abgebrochen");
                    return;
                }
                if (!Aktiv || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
                // 17.10.: Ein Motion-Lab-Auftrag startet Play erst 0,5 s spaeter – bis dahin keinen weiteren Auftrag annehmen
                // (sonst ueberschrieb der naechste das Kennzeichen, und Play nahm die falsche Aufnahme auf).
                if (AuftragMotionLab.StartLaeuft) return;
                if (!Directory.Exists(Neu)) return;
                string datei = Directory.GetFiles(Neu, "*.json")
                    .Where(f => (DateTime.Now - File.GetLastWriteTime(f)).TotalSeconds > 1.0)   // fertig geschrieben
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (datei != null) Starten(datei);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Ashveil] Auftragsordner: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static void Starten(string datei)
        {
            string text;
            try { text = File.ReadAllText(datei, Encoding.UTF8); }
            catch (IOException) { return; }   // wird noch geschrieben – beim nächsten Blick
            Auftrag a = null;
            try { a = JsonUtility.FromJson<Auftrag>(text); }
            catch (Exception) { a = null; }
            string id = Sauber(a != null && !string.IsNullOrWhiteSpace(a.id) ? a.id : Path.GetFileNameWithoutExtension(datei));
            Directory.CreateDirectory(Laufend);
            Directory.CreateDirectory(Fertig);
            string ziel = Eindeutig(Laufend, id, ".json");
            File.Move(datei, ziel);

            _laufend = a ?? new Auftrag();
            _laufend.id = id;
            _laufendDatei = ziel;
            _start = DateTime.Now;
            _protokoll = new StringBuilder();
            _zeitlimit = 600;
            _ergebnis = new Ergebnis
            {
                id = id, aktion = _laufend.aktion ?? "", parameter = _laufend.parameter ?? "", beginn = _start.ToString("yyyy-MM-dd HH:mm:ss"),
                unity = Application.unityVersion, szene = EditorSceneManager.GetActiveScene().path
            };
            if (a == null || string.IsNullOrWhiteSpace(a.aktion))
            {
                Abschliessen(false, "ungültiger Auftrag – erwartet JSON mit \"aktion\"", "abgelehnt");
                return;
            }
            Debug.Log($"[Ashveil] Auftrag {id}: {a.aktion} {a.parameter}");
            if (string.Equals(a.aktion, "kompilieren", StringComparison.OrdinalIgnoreCase))
            {
                KompilierenStarten();
                return;
            }
            if (!Aktionen.TryGetValue(a.aktion, out Eintrag eintrag))
            {
                Abschliessen(false, "unbekannte Aktion – erlaubt: kompilieren, " + string.Join(", ", Aktionen.Keys.OrderBy(k => k)), "abgelehnt");
                return;
            }
            if (eintrag.SzeneSicher && UngespeicherteSzene(out string welche))
            {
                Abschliessen(false, "ungespeicherte Änderungen in " + welche + " – nichts verändert", "abgelehnt");
                return;
            }
            _zeitlimit = eintrag.Zeitlimit;
            Application.logMessageReceived -= Mitschreiben;
            Application.logMessageReceived += Mitschreiben;
            try
            {
                Auftrag dieser = _laufend;   // spätes „fertig“ eines abgebrochenen Auftrags darf keinen anderen abschließen
                eintrag.Handler(dieser, (ok, meldung) => { if (ReferenceEquals(_laufend, dieser)) Abschliessen(ok, meldung); });
            }
            catch (Exception e)
            {
                Abschliessen(false, "Ausnahme: " + e.GetType().Name + ": " + e.Message + "\n" + Kurz(e.StackTrace, 8));
            }
        }

        private static void Abschliessen(bool ok, string meldung, string status = null)
        {
            if (_laufend == null || _ergebnis == null) return;   // schon abgeschlossen
            Application.logMessageReceived -= Mitschreiben;
            EditorUtility.ClearProgressBar();
            _ergebnis.status = status ?? (ok ? "ok" : "fehler");
            _ergebnis.meldung = meldung ?? "";
            _ergebnis.ende = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _ergebnis.sekunden = (float)(DateTime.Now - _start).TotalSeconds;
            _ergebnis.protokoll = _protokoll != null ? _protokoll.ToString() : "";
            Ergebnis e = _ergebnis;
            string datei = _laufendDatei;
            _laufend = null;
            _ergebnis = null;
            _protokoll = null;
            _laufendDatei = null;
            Schreiben(e, datei);
        }

        private static void Schreiben(Ergebnis e, string auftragsDatei)
        {
            try
            {
                Directory.CreateDirectory(Fertig);
                File.WriteAllText(Eindeutig(Fertig, e.id, ".ergebnis.json"), JsonUtility.ToJson(e, true), new UTF8Encoding(false));
                if (!string.IsNullOrEmpty(auftragsDatei) && File.Exists(auftragsDatei)) File.Move(auftragsDatei, Eindeutig(Fertig, e.id, ".json"));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Ashveil] Auftragsordner: Ergebnis nicht geschrieben – " + ex.Message);
            }
            string zeile = $"[Ashveil] Auftrag {e.id} ({e.aktion}): {e.status} nach {e.sekunden:0} s – {Kurz(e.meldung, 3)}";
            if (e.status == "ok") Debug.Log(zeile);
            else Debug.LogWarning(zeile);
        }

        private static void Mitschreiben(string message, string stack, LogType type)
        {
            if (_protokoll == null || _protokoll.Length > MaxProtokoll) return;
            bool behalten = type != LogType.Log || (message != null && message.StartsWith("[Ashveil]"));
            if (!behalten) return;
            _protokoll.Append('[').Append(DateTime.Now.ToString("HH:mm:ss")).Append("] ").Append(type).Append(": ").Append(message).Append('\n');
            if (type == LogType.Exception || type == LogType.Error) _protokoll.Append(Kurz(stack, 6)).Append('\n');
        }

        /// <summary>Nach dem Laden der Skripte: offenen Kompilier-Auftrag abschließen, unterbrochene Aufträge melden.</summary>
        private static void NachDemLaden()
        {
            try
            {
                if (KompilierAuftragOffen && !EditorApplication.isCompiling)
                    KompilierAbschluss(true, "kompiliert, Skripte neu geladen (0 Fehler)");
                if (!Directory.Exists(Laufend)) return;
                string komp = SessionState.GetString(KeyKompDatei, "");
                foreach (string f in Directory.GetFiles(Laufend, "*.json"))
                {
                    if (string.Equals(Path.GetFullPath(f), komp, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrEmpty(_laufendDatei) &&
                        string.Equals(Path.GetFullPath(f), Path.GetFullPath(_laufendDatei), StringComparison.OrdinalIgnoreCase)) continue;
                    var e = new Ergebnis
                    {
                        id = Path.GetFileNameWithoutExtension(f), status = "abgebrochen", unity = Application.unityVersion,
                        meldung = "unterbrochen (Skripte neu geladen oder Editor neu gestartet) – bitte neu einreichen",
                        ende = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    };
                    try
                    {
                        Auftrag a = JsonUtility.FromJson<Auftrag>(File.ReadAllText(f, Encoding.UTF8));
                        if (a != null) { e.aktion = a.aktion; e.parameter = a.parameter; }
                    }
                    catch (Exception) { }
                    Schreiben(e, f);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Ashveil] Auftragsordner (nach dem Laden): " + ex.Message);
            }
        }

        // ================================================================================================== Kompilieren

        private static bool KompilierAuftragOffen => !string.IsNullOrEmpty(SessionState.GetString(KeyKompId, ""));

        private static void KompilierenStarten()
        {
            // Der Auftrag überlebt das Neuladen nur im SessionState; Ergebnis schreibt KompilierungEndet (bei Fehlern, kein
            // Neuladen) oder NachDemLaden (Erfolg) oder PruefeKompilierAuftrag (nichts zu kompilieren).
            SessionState.SetString(KeyKompId, _laufend.id);
            SessionState.SetString(KeyKompDatei, Path.GetFullPath(_laufendDatei));
            SessionState.SetString(KeyKompStart, DateTime.Now.ToString("o"));
            SessionState.SetBool(KeyKompGestartet, false);
            SessionState.EraseString(KeyKompFertig);
            SessionState.EraseBool(KeyKompNeugeladen);
            KompFehler.Clear();
            _laufend = null;
            _ergebnis = null;
            _protokoll = null;
            _laufendDatei = null;
            AssetDatabase.Refresh();
        }

        private static void KompilierungBeginnt(object _)
        {
            if (KompilierAuftragOffen) SessionState.SetBool(KeyKompGestartet, true);
            KompFehler.Clear();
        }

        private static void AssemblyFertig(string assembly, CompilerMessage[] meldungen)
        {
            foreach (CompilerMessage m in meldungen)
                if (m.type == CompilerMessageType.Error)
                    KompFehler.Add($"{Path.GetFileName(m.file)}({m.line}): {m.message}");
        }

        private static void KompilierungEndet(object _)
        {
            // Mit Fehlern lädt Unity die Skripte nicht neu – das Ergebnis jetzt schreiben
            if (!KompilierAuftragOffen) return;
            if (KompFehler.Count > 0)
                KompilierAbschluss(false, $"{KompFehler.Count} Kompilierfehler:\n" + string.Join("\n", KompFehler.Distinct().Take(40)));
            else
                SessionState.SetString(KeyKompFertig, DateTime.Now.ToString("o"));   // Neuladen folgt (sonst PruefeKompilierAuftrag)
        }

        private static void PruefeKompilierAuftrag()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            DateTime.TryParse(SessionState.GetString(KeyKompStart, ""), null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime start);
            double sek = (DateTime.Now - start).TotalSeconds;
            string fertigOhneFehler = SessionState.GetString(KeyKompFertig, "");
            if (!SessionState.GetBool(KeyKompGestartet, false) && sek > 15)
            {
                // Unity kann die Änderungen schon vorher selbst kompiliert haben – dann zählt, ob Fehler anstehen
                if (EditorUtility.scriptCompilationFailed)
                    KompilierAbschluss(false, "Kompilierfehler stehen an (Skripte waren schon kompiliert):\n" + LetzteKompilierFehler());
                else
                    KompilierAbschluss(true, "keine neuen Skriptänderungen – kompiliert, 0 Fehler");
            }
            else if (fertigOhneFehler.Length > 0 && DateTime.TryParse(fertigOhneFehler, null, System.Globalization.DateTimeStyles.RoundtripKind,
                         out DateTime t1) && (DateTime.Now - t1).TotalSeconds > 30)
                KompilierAbschluss(true, SessionState.GetBool(KeyKompNeugeladen, false)
                    ? "kompiliert (0 Fehler), Skripte neu geladen" : "kompiliert (0 Fehler), ohne Neuladen der Skripte");
            else if (sek > 1200)
                KompilierAbschluss(false, "Kompilieren ohne Abschluss nach 20 min");
        }

        private static void KompilierAbschluss(bool ok, string meldung)
        {
            string id = SessionState.GetString(KeyKompId, "");
            if (string.IsNullOrEmpty(id)) return;
            string datei = SessionState.GetString(KeyKompDatei, "");
            string start = SessionState.GetString(KeyKompStart, "");
            SessionState.EraseString(KeyKompId);
            SessionState.EraseString(KeyKompDatei);
            SessionState.EraseString(KeyKompStart);
            SessionState.EraseBool(KeyKompGestartet);
            SessionState.EraseString(KeyKompFertig);
            SessionState.EraseBool(KeyKompNeugeladen);
            DateTime.TryParse(start, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime t0);
            var e = new Ergebnis
            {
                id = id, aktion = "kompilieren", parameter = "", status = ok ? "ok" : "fehler", meldung = meldung,
                beginn = t0 == default ? "" : t0.ToString("yyyy-MM-dd HH:mm:ss"), ende = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                sekunden = t0 == default ? 0f : (float)(DateTime.Now - t0).TotalSeconds, unity = Application.unityVersion,
                szene = EditorSceneManager.GetActiveScene().path, protokoll = ""
            };
            Schreiben(e, datei);
        }

        /// <summary>Letzte Fehlerzeilen aus Logs/Ashveil/Kompilieren.txt (schreibt ConsoleFileLog bei jedem Kompilieren).</summary>
        private static string LetzteKompilierFehler()
        {
            try
            {
                string pfad = ProjectPaths.Logs("Kompilieren.txt");
                if (!File.Exists(pfad)) return "(keine Kompilier-Protokolldatei)";
                string[] zeilen = File.ReadAllLines(pfad, Encoding.UTF8).Where(z => z.Contains("FEHLER")).ToArray();
                return zeilen.Length == 0 ? "(keine Fehlerzeilen im Protokoll)" : string.Join("\n", zeilen.Skip(Math.Max(0, zeilen.Length - 40)));
            }
            catch (Exception e)
            {
                return "(Protokoll nicht lesbar: " + e.Message + ")";
            }
        }

        // ================================================================================================== Aktionen

        private static void Status(Auftrag a, Action<bool, string> fertig)
        {
            var s = EditorSceneManager.GetActiveScene();
            string blender = ArtPipeline.FindBlender();
            fertig(true, $"Unity {Application.unityVersion} · Szene {s.path} (ungespeichert: {s.isDirty}) · Play: {EditorApplication.isPlaying} · Pause: {EditorApplication.isPaused} · " +
                         "Blender: " + (blender == null ? "nicht gefunden" : blender + " · " + ArtPipeline.BlenderVersion(blender)) + " · " +
                         "Aktionen: kompilieren, " + string.Join(", ", Aktionen.Keys.OrderBy(k => k)));
        }

        private static void Weltpruefung(Auftrag a, Action<bool, string> fertig)
        {
            if (string.Equals((a.parameter ?? "").Trim(), "kareth_v4", StringComparison.OrdinalIgnoreCase) &&
                EditorSceneManager.GetActiveScene().path != KarethV4Builder.ScenePath)
                EditorSceneManager.OpenScene(KarethV4Builder.ScenePath, OpenSceneMode.Single);
            Quality.WqReport r = Quality.WorldQualityValidator.Run();
            string pfad = Quality.WorldQualityValidator.Write(r);
            fertig(true, $"{r.scene}: {r.critical} kritisch · {r.warning} Warnungen · {r.info} Hinweise → {pfad}");
        }

        private static void ReviewBilder(Auftrag a, Action<bool, string> fertig)
        {
            string unter = string.IsNullOrWhiteSpace(a.parameter) ? "Auftrag_" + DateTime.Now.ToString("yyyy-MM-dd_HHmm") : Sauber(a.parameter.Trim());
            string dir = KarethV4Shots.Render(unter);
            fertig(dir != null, dir != null ? "Review-Bilder in " + dir : "keine Bilder (Kareth v4 fehlt oder Play läuft)");
        }

        private static readonly Dictionary<string, string> Szenen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "kareth_v4", KarethV4Builder.ScenePath },
            { "sandbox_m1", "Assets/_Ashveil/Scenes/Sandbox_M1.unity" },   // 22.10.: neu laden verwirft eine versehentliche Änderungsmarke
        };

        private static void SzeneOeffnen(Auftrag a, Action<bool, string> fertig)
        {
            if (!Szenen.TryGetValue((a.parameter ?? "").Trim(), out string pfad))
            {
                fertig(false, "erlaubt: " + string.Join(", ", Szenen.Keys));
                return;
            }
            if (!File.Exists(pfad))
            {
                fertig(false, "Szene fehlt: " + pfad);
                return;
            }
            EditorSceneManager.OpenScene(pfad, OpenSceneMode.Single);
            fertig(true, "geöffnet: " + pfad);
        }

        /// <summary>„ashveil_xyz.py --arg wert …“: eigenes Skript aus Tools/Blender, Blender im Hintergrund wie die Grafik-Pipeline.</summary>
        private static void BlenderSkript(Auftrag a, Action<bool, string> fertig)
        {
            string p = (a.parameter ?? "").Trim();
            int leer = p.IndexOf(' ');
            string skript = leer < 0 ? p : p.Substring(0, leer);
            string args = leer < 0 ? "" : p.Substring(leer + 1).Trim();
            if (!SkriptMuster.IsMatch(skript))
            {
                fertig(false, "Skript nicht erlaubt – nur Tools/Blender/ashveil_*.py");
                return;
            }
            string pfad = ProjectPaths.Tools("Blender", skript);
            if (!File.Exists(pfad))
            {
                fertig(false, "Skript fehlt: " + pfad);
                return;
            }
            if (args.Length > 600 || !ArgMuster.IsMatch(args) || args.Contains("..") || !PfadeImProjekt(args))
            {
                fertig(false, "Argumente nicht erlaubt (nur einfache Werte, Pfade nur innerhalb des Projekts)");
                return;
            }
            string blender = ArtPipeline.FindBlender();
            if (blender == null)
            {
                fertig(false, "Blender nicht gefunden");
                return;
            }
            string log = ProjectPaths.Logs("Blender", "Auftrag_" + (LaufendeId ?? "ohne_id") + ".txt");
            // 16.10. abends (Max: „du stuerzt staendig ab“): Hat Blender Bewegungs-FBX unter Assets geschrieben, importiert Unity sie
            // beim naechsten Refresh – mit offener Szene samt Animator stuerzt 6000.6.3f1 dabei ab (Animator::Rebind →
            // IsMecanimDataValid; heute dreimal). Darum sofort hier geschuetzt importieren (leere Szene, dann zurueck).
            // 17.10.: trotzdem ein Absturz (10:45, direkt nach dem Blender-Lauf, Unity im Hintergrund) – der Import kann schon
            // VOR unserem Refresh anlaufen (Dateiwaechter). Darum bei Skripten, die Bewegungs-FBX schreiben (retarget), schon
            // waehrend des Blender-Laufs in der leeren Szene bleiben: Szene weg → Blender → Refresh → Szene zurueck.
            bool ok; string zusammenfassung;
            bool bewegung = skript.IndexOf("retarget", StringComparison.OrdinalIgnoreCase) >= 0 || skript.IndexOf("bvh", StringComparison.OrdinalIgnoreCase) >= 0;
            if (bewegung)
            {
                bool okLauf = false; string zs = "";
                AnimationsImportSchutz.Ausfuehren(() =>
                {
                    okLauf = ArtPipeline.RunBlenderProcess(blender, pfad, args, log, skript, out string _, out zs);
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }, true);
                ok = okLauf; zusammenfassung = zs + " · FBX in leerer Szene importiert";
            }
            else
            {
                ok = ArtPipeline.RunBlenderProcess(blender, pfad, args, log, skript, out string _, out zusammenfassung);
                if (AnimationsImportSchutz.ModelleGeaendert())
                {
                    AnimationsImportSchutz.Ausfuehren(() => AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport));
                    zusammenfassung += " · FBX geschuetzt importiert";
                }
            }
            fertig(ok, zusammenfassung + " · " + ArtPipeline.BlenderVersion(blender) + " · Protokoll " + log);
        }

        /// <summary>
        /// „suchen“ (nur lesen): Text in Skripten und Daten finden. Parameter „Text“ oder „Text|Unterordner“; der Unterordner
        /// liegt unter Assets/_Ashveil (Standard „Scripts“) oder ist „Tools“. Höchstens 400 Treffer, Dateien bis 8 MB.
        /// </summary>
        private static void Suchen(Auftrag a, Action<bool, string> fertig)
        {
            string p = a.parameter ?? "";
            string text = p, unter = "Scripts";
            int bar = p.LastIndexOf('|');
            if (bar >= 0) { text = p.Substring(0, bar); unter = p.Substring(bar + 1).Trim(); }
            if (text.Length < 2 || text.Length > 200)
            {
                fertig(false, "Suchtext mit 2 bis 200 Zeichen erwartet");
                return;
            }
            string projekt = Path.GetFullPath(ProjectPaths.ProjectRoot);
            string erlaubt = unter.StartsWith("Tools", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFullPath(Path.Combine(projekt, "Tools"))
                : Path.GetFullPath(Path.Combine(projekt, "Assets", "_Ashveil"));
            string basis = unter.StartsWith("Tools", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFullPath(Path.Combine(projekt, unter))
                : Path.GetFullPath(Path.Combine(erlaubt, unter));
            if (!basis.StartsWith(erlaubt, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(basis))
            {
                fertig(false, "Ordner nicht erlaubt oder nicht vorhanden: " + unter);
                return;
            }
            var endungen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".cs", ".asmdef", ".json", ".asset", ".prefab", ".unity", ".shader", ".hlsl", ".py", ".md", ".txt", ".uss", ".uxml" };
            var sb = new StringBuilder();
            int treffer = 0, dateien = 0;
            foreach (string f in Directory.EnumerateFiles(basis, "*", SearchOption.AllDirectories))
            {
                if (!endungen.Contains(Path.GetExtension(f)) || new FileInfo(f).Length > 8_000_000) continue;
                dateien++;
                int zeile = 0;
                foreach (string l in File.ReadLines(f))
                {
                    zeile++;
                    if (l.IndexOf(text, StringComparison.Ordinal) < 0) continue;
                    if (treffer < 400)
                    {
                        string t = l.Trim();
                        sb.Append(f.Substring(projekt.Length).TrimStart('\\', '/')).Append(':').Append(zeile).Append(": ")
                          .Append(t.Length > 220 ? t.Substring(0, 220) + " …" : t).Append('\n');
                    }
                    treffer++;
                }
            }
            fertig(true, $"{treffer} Treffer in {dateien} Dateien unter {unter}" + (treffer > 400 ? " (die ersten 400)" : "") + "\n" + sb);
        }

        // ================================================================================================== Hilfen

        private static bool PfadeImProjekt(string args)
        {
            string root = Path.GetFullPath(ProjectPaths.ProjectRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            foreach (string roh in args.Split(new[] { ' ', '"', '=', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                bool absolut = roh.Length > 2 && roh[1] == ':' || roh.StartsWith("/") || roh.StartsWith("\\");
                if (!absolut) continue;
                string voll;
                try { voll = Path.GetFullPath(roh); }
                catch (Exception) { return false; }
                if (!voll.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        private static bool UngespeicherteSzene(out string welche)
        {
            var namen = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.isDirty) namen.Add(string.IsNullOrEmpty(s.path) ? "(unbenannte Szene)" : s.path);
            }
            welche = string.Join(", ", namen);
            return namen.Count > 0;
        }

        private static string Sauber(string id)
        {
            string s = Unerlaubt.Replace(id ?? "", "_");
            if (s.Length == 0) s = "auftrag_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return s.Length > 80 ? s.Substring(0, 80) : s;
        }

        private static string Eindeutig(string ordner, string name, string endung)
        {
            string pfad = Path.Combine(ordner, name + endung);
            for (int n = 2; File.Exists(pfad); n++) pfad = Path.Combine(ordner, $"{name}_{n}{endung}");
            return pfad;
        }

        private static string Kurz(string text, int zeilen)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string[] z = text.Replace("\r", "").Split('\n');
            return z.Length <= zeilen ? text.TrimEnd() : string.Join("\n", z.Take(zeilen)) + " …";
        }
    }
}
