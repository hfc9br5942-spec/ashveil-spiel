#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Ashveil.UI.Diagnostics
{
    /// <summary>
    /// Bild-Spitzen-Messung v2.5 (CORE-005, 09.10.2026 abends): Rechenzeit des Hauptfadens und Stapelproben bei Hängern am
    /// Bildanfang. Nur im fertigen Entwicklungs-Build unter Windows (64 Bit); im Editor und in einem Release-Build gibt es das nicht.
    ///
    /// Anlass: In Max' Sitzungen in r2.1 (Unity geschlossen, mit und ohne Browser) stand der Hauptfaden etwa einmal je Sekunde
    /// 50–460 ms zwischen dem Ende des Zeit-Abschnitts (TimeUpdate) und dem Profiler-Bildstart (ProfilerStartFrame). Dort hat
    /// Unity keine Messpunkte; der Grafikfaden wartete in der Zeit nur auf Befehle.
    ///
    /// Was gemessen wird:
    /// - Rechenzeit des Hauptfadens (QueryThreadCycleTime, an einer kurzen Rechenschleife geeicht): Rechnet er am Bildanfang,
    ///   oder wartet er auf etwas?
    /// - Stapelproben: Ein eigener Faden schaut alle 2 ms nach, ob der Bildanfang schon länger als 25 ms dauert. Dann hält er den
    ///   Hauptfaden für einige Mikrosekunden an, liest den Befehlszeiger, kopiert den oberen Teil des Stapels und lässt ihn sofort
    ///   weiterlaufen (höchstens drei Proben je Bild: ab 25, 100 und 250 ms). Danach ordnet er die Adressen den geladenen
    ///   Programmteilen (DLLs) zu und, wo es geht, deren benannten Einsprungpunkten. Der Stapel wird nur durchgesehen, nicht
    ///   entwirrt: Unter dem obersten Glied können einzelne Einträge alte Rücksprungadressen sein.
    ///
    /// Sicherheit: Solange der Hauptfaden steht, fordert die Wache keinen Speicher an und ruft nur vorher aufgewärmte Funktionen
    /// (der angehaltene Faden könnte eine Sperre halten, die sonst gebraucht würde). Abschalten mit dem Startargument
    /// -ashveil-ruckler-ohne-stapel (die Rechenzeit-Messung bleibt).
    ///
    /// v2.6 (nach den ersten Stapelproben: der Hauptfaden wartet in Windows beim Verarbeiten der Maus-Nachrichten): zu jeder Probe
    /// ein Systemblick (NtQuerySystemInformation) – worauf der Hauptfaden wartet (Wartegrund), welche anderen Fäden des Spiels
    /// gerade laufen (mit Namen) und welche anderen Programme gerade Rechenzeit haben; dazu die geladenen Programmteile mit
    /// Herkunft (Windows, Spiel, andere), um eingeschleuste Programmteile fremder Programme zu sehen. Außerdem zählt sie die
    /// Fenster-Nachrichten an Unitys Fenster nach Art.
    ///
    /// v2.7 (09.10.2026 abends, Max: Mausfilter fest einbauen): Das Zählen der Fenster-Nachrichten und der Mausfilter sind aus der
    /// Wache in den Laufzeit-Baustein Ashveil.Core.Platform.Mausfilter gewandert (in jedem Build an; gezählt wird nur im
    /// Entwicklungs-Build). Die Wache leitet Unitys Fenster nicht mehr selbst um.
    /// </summary>
    internal static class BildanfangWache
    {
        /// <summary>Startargument: ohne Stapelproben (die Rechenzeit-Messung bleibt).</summary>
        internal const string AusArgument = "-ashveil-ruckler-ohne-stapel";

        /// <summary>Eine Stapelprobe am Bildanfang.</summary>
        internal sealed class Probe
        {
            public long Bild;        // laufende Nummer des Bildes, wie die Messung sie zählt
            public double NachMs;    // so lange dauerte der Bildanfang zur Zeit der Probe
            public int Phase;        // 1 Zeit-Abschnitt · 2 nach dem Zeit-Abschnitt · 3 Profiler-Bildstart
            public double RechenMs;  // Rechenzeit des Hauptfadens seit dem Bildanfang (< 0: unbekannt)
            public string Stapel;    // oberstes Glied zuerst: „ntdll!NtWaitForSingleObject+0x14 ← …“
            public string Stelle;    // nur das oberste Glied
            public string Kette;     // die obersten vier Glieder
            public string SystemText; // v2.6: Wartegrund des Hauptfadens, aktive Fäden des Spiels, aktive andere Programme
        }

        internal static string PhasenText(int phase)
        {
            switch (phase)
            {
                case 1: return "im Zeit-Abschnitt";
                case 2: return "nach dem Zeit-Abschnitt";
                case 3: return "im Profiler-Bildstart";
                default: return "Abschnitt unbekannt";
            }
        }

#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
        private const uint FadenRechte = 0x0002 | 0x0008 | 0x0040 | 0x0800;   // SUSPEND_RESUME | GET_CONTEXT | QUERY_INFORMATION | QUERY_LIMITED_INFORMATION
        private const int KontextGroesse = 1232;                                 // CONTEXT (x64)
        private const int KontextFlags = 0x30, KontextRsp = 0x98, KontextRip = 0xF8;
        private const int KontextSteuerungUndGanzzahl = 0x00100003;              // CONTEXT_AMD64 | CONTEXT_CONTROL | CONTEXT_INTEGER
        private const uint Fehler = 0xFFFFFFFF;
        private const int StapelBytes = 64 * 1024;
        private const int MaxGlieder = 14;
        private const double ProbeAb1 = 25.0, ProbeAb2 = 100.0, ProbeAb3 = 250.0;
        private const int MaxProben = 400;

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenThread(uint access, bool inherit, uint threadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SuspendThread(IntPtr thread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr thread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetThreadContext(IntPtr thread, IntPtr context);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryThreadCycleTime(IntPtr thread, out ulong cycles);

        [DllImport("kernel32.dll")]
        private static extern void GetCurrentThreadStackLimits(out IntPtr low, out IntPtr high);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, [Out] byte[] buffer, IntPtr size, out IntPtr read);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool K32EnumProcessModulesEx(IntPtr process, [Out] IntPtr[] modules, uint cb, out uint needed, uint filter);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool K32GetModuleInformation(IntPtr process, IntPtr module, out ModulInfo info, uint cb);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint K32GetModuleBaseNameW(IntPtr process, IntPtr module, StringBuilder name, uint size);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint K32GetModuleFileNameExW(IntPtr process, IntPtr module, StringBuilder name, uint size);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(int infoClass, IntPtr buffer, int length, out int returnLength);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationThread(IntPtr thread, int infoClass, out IntPtr info, int length, out int returnLength);

        [DllImport("kernel32.dll")]
        private static extern int GetThreadDescription(IntPtr thread, out IntPtr description);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        [StructLayout(LayoutKind.Sequential)]
        private struct ModulInfo
        {
            public IntPtr Basis;
            public uint Groesse;
            public IntPtr Einstieg;
        }

        /// <summary>Ein geladener Programmteil (DLL oder EXE) mit seinen Code-Abschnitten und benannten Einsprungpunkten.</summary>
        private sealed class Modul
        {
            public string Name, Pfad;
            public long Basis, Ende;
            public int ExportVerz, ExportGroesse;
            public long[] CodeAb = new long[0], CodeBis = new long[0];
            public int[] ExportRva;
            public string[] ExportName;

            public bool IstCode(long a)
            {
                for (int k = 0; k < CodeAb.Length; k++)
                    if (a >= CodeAb[k] && a < CodeBis[k]) return true;
                return false;
            }
        }

        private static IntPtr _prozess;
        private static IntPtr _haupt = IntPtr.Zero;
        private static long _stapelTief, _stapelHoch;
        private static IntPtr _kontextRoh = IntPtr.Zero, _kontext = IntPtr.Zero;
        private static readonly byte[] _stapel = new byte[StapelBytes];
        private static readonly byte[] _kopf = new byte[4096];
        private static readonly byte[] _textPuffer = new byte[32];
        private static Thread _wache;
        private static volatile bool _laeuft;
        private static double _zyklenJeMs;
        private static string _status = "aus";

        // vom Hauptfaden gesetzt (Bildanfang)
        private static long _fensterAb;       // Stoppuhr-Zeit des Bildanfangs; 0 = außerhalb
        private static long _fensterBild;
        private static long _fensterZyklen;
        private static volatile int _phase;

        // nur von der Wache benutzt
        private static long _probeBild = -1;
        private static int _probeStufe;
        private static Modul[] _module = new Modul[0];
        private static long _modulLesung;
        private static string _spielOrdner;

        // v2.6: Systemblick
        private static uint _hauptId, _wachId, _eigenePid;
        private static IntPtr _sysPuffer = IntPtr.Zero;
        private static int _sysLaenge;
        private static readonly Dictionary<long, string> _fadenNamen = new Dictionary<long, string>();
        private static readonly string[] Zustaende =
        {
            "initialisiert", "bereit", "läuft", "gleich dran", "beendet", "wartet", "im Übergang", "bereit (verzögert)",
            "wartet (Tor)", "wartet auf Einlagerung"
        };
        private static readonly string[] Gruende =
        {
            "Executive", "FreePage", "PageIn", "PoolAllocation", "DelayExecution", "Suspended", "UserRequest", "WrExecutive",
            "WrFreePage", "WrPageIn", "WrPoolAllocation", "WrDelayExecution", "WrSuspended", "WrUserRequest", "WrEventPair",
            "WrQueue", "WrLpcReceive", "WrLpcReply", "WrVirtualMemory", "WrPageOut", "WrRendezvous", "WrKeyedEvent",
            "WrTerminated", "WrProcessInSwap", "WrCpuRateControl", "WrCalloutStack", "WrKernel", "WrResource", "WrPushLock",
            "WrMutex", "WrQuantumEnd", "WrDispatchInt", "WrPreempted", "WrYieldExecution", "WrFastMutex", "WrGuardedMutex",
            "WrRundown", "WrAlertByThreadId", "WrDeferredPreempt", "WrPhysicalFault", "WrIoRing", "WrMdlCache"
        };

        // geteilt (unter _sperre)
        private static readonly object _sperre = new object();
        private static readonly List<Probe> _proben = new List<Probe>();
        private static readonly Dictionary<string, int> _stellen = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _ketten = new Dictionary<string, int>();
        private static int _probenZahl, _verworfen;

        internal static bool Aktiv => _laeuft;
        internal static string Status => _status;
        internal static double ZyklenJeMs => _zyklenJeMs;

        /// <summary>Vom Hauptfaden (Start der Messung): Rechenzeit-Messung einrichten und, wenn erlaubt, die Wache starten.</summary>
        internal static void Starten(bool ohneStapel)
        {
            if (_haupt != IntPtr.Zero) return;
            if (IntPtr.Size != 8) { _status = "aus (nur 64 Bit)"; return; }
            try
            {
                _prozess = GetCurrentProcess();
                _hauptId = GetCurrentThreadId();
                _eigenePid = GetCurrentProcessId();
                _haupt = OpenThread(FadenRechte, false, _hauptId);
                if (_haupt == IntPtr.Zero) { _status = "aus (Hauptfaden nicht geöffnet)"; return; }
                _zyklenJeMs = Eichen();
                if (ohneStapel) { _status = "aus (Startargument " + AusArgument + ")"; return; }
                GetCurrentThreadStackLimits(out IntPtr tief, out IntPtr hoch);
                _stapelTief = tief.ToInt64();
                _stapelHoch = hoch.ToInt64();
                _kontextRoh = Marshal.AllocHGlobal(KontextGroesse + 16);
                _kontext = new IntPtr((_kontextRoh.ToInt64() + 15) & ~15L);
                ModuleLesen();
                string warm = Aufwaermen();
                if (warm != null) { _status = "aus (Aufwärmen: " + warm + ")"; KontextFreigeben(); return; }
                _laeuft = true;
                _wache = new Thread(Wachen) { IsBackground = true, Name = "Ashveil Bildanfang-Wache" };
                try { _wache.Priority = ThreadPriority.AboveNormal; }
                catch (Exception) { }
                _wache.Start();
                _status = "an (" + _module.Length.ToString(CultureInfo.InvariantCulture) + " Programmteile)";
            }
            catch (Exception e)
            {
                _laeuft = false;
                _wache = null;
                _status = "aus (" + e.GetType().Name + ")";
                KontextFreigeben();
            }
        }

        /// <summary>Vom Hauptfaden (Ende der Messung): Wache anhalten und aufräumen.</summary>
        internal static void Beenden()
        {
            Thread w = _wache;
            _laeuft = false;
            Interlocked.Exchange(ref _fensterAb, 0);
            if (w != null)
            {
                if (!w.Join(500)) return;   // läuft noch: Speicher und Handle nicht freigeben
                _wache = null;
            }
            KontextFreigeben();
            if (_sysPuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_sysPuffer);
                _sysPuffer = IntPtr.Zero;
            }
            if (_haupt != IntPtr.Zero)
            {
                CloseHandle(_haupt);
                _haupt = IntPtr.Zero;
            }
        }

        /// <summary>v2.6: geladene Programmteile nach Herkunft (für den Kopf der Sitzungsdatei); null, wenn unbekannt.</summary>
        internal static string ProgrammteileText()
        {
            Modul[] ms = _module;
            if (ms.Length == 0) return null;
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            int ausWindows = 0;
            var spiel = new List<string>();
            var andere = new List<string>();
            foreach (Modul m in ms)
            {
                string pfad = m.Pfad ?? "";
                if (win.Length > 0 && pfad.StartsWith(win, StringComparison.OrdinalIgnoreCase)) ausWindows++;
                else if (_spielOrdner != null && pfad.StartsWith(_spielOrdner, StringComparison.OrdinalIgnoreCase)) spiel.Add(m.Name);
                else andere.Add(pfad.Length > 0 ? pfad : m.Name);
            }
            spiel.Sort(StringComparer.OrdinalIgnoreCase);
            andere.Sort(StringComparer.OrdinalIgnoreCase);
            return ms.Length.ToString(CultureInfo.InvariantCulture) + " · aus Windows " + ausWindows.ToString(CultureInfo.InvariantCulture) +
                   " · aus dem Spiel " + spiel.Count.ToString(CultureInfo.InvariantCulture) + " (" + string.Join(", ", spiel.ToArray()) + ")" +
                   " · andere " + andere.Count.ToString(CultureInfo.InvariantCulture) + (andere.Count > 0 ? ": " + string.Join(" · ", andere.ToArray()) : "");
        }

        /// <summary>Verbrauchte Rechenzeit des Hauptfadens in Takten (0 = unbekannt).</summary>
        internal static long Zyklen()
        {
            IntPtr h = _haupt;
            if (h == IntPtr.Zero) return 0;
            return QueryThreadCycleTime(h, out ulong z) ? (long)z : 0;
        }

        /// <summary>Vom Hauptfaden, ganz am Anfang des Bildes: ab jetzt wacht die Wache.</summary>
        internal static void FensterAuf(long bild, long tick, long zyklen)
        {
            if (!_laeuft) return;
            Interlocked.Exchange(ref _fensterBild, bild);
            Interlocked.Exchange(ref _fensterZyklen, zyklen);
            _phase = 1;
            Interlocked.Exchange(ref _fensterAb, tick);
        }

        /// <summary>Vom Hauptfaden: der Bildanfang ist im nächsten Abschnitt (2 nach dem Zeit-Abschnitt, 3 Profiler-Bildstart).</summary>
        internal static void Phase(int phase)
        {
            if (_laeuft) _phase = phase;
        }

        /// <summary>Vom Hauptfaden, gleich nach dem Profiler-Bildstart: der Bildanfang ist vorbei.</summary>
        internal static void FensterZu()
        {
            if (!_laeuft) return;
            Interlocked.Exchange(ref _fensterAb, 0);
            _phase = 0;
        }

        /// <summary>Vom Hauptfaden: die Proben zu einem Bild herausnehmen (älteste zuerst); null, wenn es keine gibt.</summary>
        internal static List<Probe> Abholen(long bild)
        {
            if (_probenZahl == 0) return null;
            List<Probe> l = null;
            lock (_sperre)
            {
                for (int k = 0; k < _proben.Count; k++)
                {
                    if (_proben[k].Bild != bild) continue;
                    if (l == null) l = new List<Probe>();
                    l.Add(_proben[k]);
                }
                if (l != null) _proben.RemoveAll(p => p.Bild == bild);
                // Proben zu Bildern ohne Bericht (Bildanfang 25–50 ms) zählen nur in der Zusammenfassung
                _proben.RemoveAll(p => p.Bild < bild - 128);
            }
            return l;
        }

        /// <summary>Zeilen für die Zusammenfassung der Sitzung.</summary>
        internal static string Zusammenfassung()
        {
            var sb = new StringBuilder();
            sb.Append("  Stapelproben am Bildanfang: ").Append(_status);
            lock (_sperre)
            {
                sb.Append(" · ").Append(_probenZahl).Append(" Proben");
                if (_verworfen > 0) sb.Append(" (").Append(_verworfen).Append(" verworfen)");
                sb.AppendLine();
                Haeufigste(sb, "    häufigste oberste Stellen:", _stellen, 8);
                Haeufigste(sb, "    häufigste Ketten (oberste vier Glieder):", _ketten, 6);
            }
            return sb.ToString();
        }

        private static void Haeufigste(StringBuilder sb, string titel, Dictionary<string, int> d, int max)
        {
            if (d.Count == 0) return;
            var l = new List<KeyValuePair<string, int>>(d);
            l.Sort((x, y) => y.Value.CompareTo(x.Value));
            sb.AppendLine(titel);
            for (int k = 0; k < l.Count && k < max; k++)
                sb.Append("      ").Append(l[k].Value).Append("× ").AppendLine(l[k].Key);
        }

        // ------------------------------------------------------------------ Wache

        private static void Wachen()
        {
            try
            {
                _wachId = GetCurrentThreadId();
                while (_laeuft)
                {
                    Thread.Sleep(2);
                    long ab = Interlocked.Read(ref _fensterAb);
                    if (ab == 0) continue;
                    long bild = Interlocked.Read(ref _fensterBild);
                    double ms = (Stopwatch.GetTimestamp() - ab) * 1000.0 / Stopwatch.Frequency;
                    int stufe = ms >= ProbeAb3 ? 3 : ms >= ProbeAb2 ? 2 : ms >= ProbeAb1 ? 1 : 0;
                    if (stufe == 0) continue;
                    if (bild != _probeBild)
                    {
                        _probeBild = bild;
                        _probeStufe = 0;
                    }
                    if (stufe <= _probeStufe) continue;
                    _probeStufe = stufe;
                    try { Proben(bild, ab); }
                    catch (Exception) { Interlocked.Increment(ref _verworfen); }
                }
            }
            catch (Exception)
            {
                _laeuft = false;
                _status = "ausgefallen";
            }
        }

        private static void Proben(long bild, long ab)
        {
            int n = Anhalten(_haupt, _stapelTief, _stapelHoch, ab, out long rip, out int phase);
            if (n < 0)
            {
                Interlocked.Increment(ref _verworfen);
                return;
            }
            double nachMs = (Stopwatch.GetTimestamp() - ab) * 1000.0 / Stopwatch.Frequency;
            double rechen = -1.0;
            if (_zyklenJeMs > 0.0 && Interlocked.Read(ref _fensterBild) == bild)
            {
                long z = Zyklen(), z0 = Interlocked.Read(ref _fensterZyklen);
                if (z > 0 && z0 > 0 && z >= z0) rechen = (z - z0) / _zyklenJeMs;
            }
            Probe p = Auswerten(rip, n);
            p.Bild = bild;
            p.NachMs = nachMs;
            p.Phase = phase;
            p.RechenMs = rechen;
            try { p.SystemText = Systemblick(); }
            catch (Exception e) { p.SystemText = "Systemblick ausgefallen (" + e.GetType().Name + ")"; }
            lock (_sperre)
            {
                _probenZahl++;
                _proben.Add(p);
                if (_proben.Count > MaxProben) _proben.RemoveRange(0, _proben.Count - MaxProben);
                Zaehlen(_stellen, p.Stelle);
                Zaehlen(_ketten, p.Kette);
            }
        }

        /// <summary>
        /// Faden anhalten, Befehlszeiger und Stapel kopieren, weiterlaufen lassen. Solange er steht: keine Speicheranforderung, nur
        /// aufgewärmte Aufrufe. Rückgabe: kopierte Bytes; -1, wenn nichts zu holen war oder der Bildanfang schon vorbei war
        /// (ab = 0: ohne diese Prüfung, für das Aufwärmen).
        /// </summary>
        private static int Anhalten(IntPtr faden, long tief, long hoch, long ab, out long rip, out int phase)
        {
            rip = 0;
            phase = 0;
            if (SuspendThread(faden) == Fehler) return -1;
            int n = -1;
            try
            {
                Marshal.WriteInt32(_kontext, KontextFlags, KontextSteuerungUndGanzzahl);
                if (GetThreadContext(faden, _kontext) && (ab == 0 || Interlocked.Read(ref _fensterAb) == ab))
                {
                    phase = _phase;
                    rip = Marshal.ReadInt64(_kontext, KontextRip);
                    long rsp = Marshal.ReadInt64(_kontext, KontextRsp);
                    n = 0;
                    if (rsp >= tief && rsp < hoch)
                    {
                        long laenge = Math.Min(hoch - rsp, (long)StapelBytes);
                        if (ReadProcessMemory(_prozess, new IntPtr(rsp), _stapel, new IntPtr(laenge), out IntPtr gelesen))
                            n = (int)gelesen.ToInt64();
                    }
                }
            }
            finally
            {
                ResumeThread(faden);
            }
            return n;
        }

        /// <summary>Befehlszeiger und Stapelkopie in lesbare Glieder übersetzen (nach dem Weiterlaufen; hier darf Speicher angefordert werden).</summary>
        private static Probe Auswerten(long rip, int n)
        {
            var glieder = new List<string>(MaxGlieder) { Symbol(rip) };
            long vorige = rip;
            for (int o = 0; o + 8 <= n && glieder.Count < MaxGlieder; o += 8)
            {
                long w = BitConverter.ToInt64(_stapel, o);
                if (w == 0 || w == vorige) continue;
                Modul m = Suchen(w);
                if (m == null || !m.IstCode(w)) continue;
                glieder.Add(Symbol(m, w));
                vorige = w;
            }
            return new Probe
            {
                Stapel = string.Join(" ← ", glieder.ToArray()),
                Stelle = glieder[0],
                Kette = string.Join(" ← ", glieder.GetRange(0, Math.Min(4, glieder.Count)).ToArray()),
            };
        }

        /// <summary>
        /// Alle Aufrufe, die die Wache bei angehaltenem Hauptfaden braucht, einmal an einem Hilfsfaden ausführen (Übersetzung der
        /// Methoden, Auflösen der Windows-Funktionen). Sonst könnte die erste echte Probe auf eine Sperre warten, die der angehaltene
        /// Hauptfaden hält. Rückgabe: null = gut, sonst der Grund.
        /// </summary>
        private static string Aufwaermen()
        {
            uint id = 0;
            long tief = 0, hoch = 0;
            var bereit = new ManualResetEvent(false);
            var ende = new ManualResetEvent(false);
            var helfer = new Thread(() =>
            {
                try
                {
                    id = GetCurrentThreadId();
                    GetCurrentThreadStackLimits(out IntPtr l, out IntPtr h);
                    tief = l.ToInt64();
                    hoch = h.ToInt64();
                    bereit.Set();
                    ende.WaitOne(5000);
                }
                catch (Exception) { }
            }) { IsBackground = true, Name = "Ashveil Bildanfang-Wache (Aufwärmen)" };
            helfer.Start();
            try
            {
                if (!bereit.WaitOne(2000)) return "Hilfsfaden startet nicht";
                IntPtr faden = OpenThread(FadenRechte, false, id);
                if (faden == IntPtr.Zero) return "Hilfsfaden nicht geöffnet";
                try
                {
                    int n = Anhalten(faden, tief, hoch, 0, out long rip, out int _);
                    if (n <= 0 || rip == 0) return "kein Stapel";
                    Auswerten(rip, n);
                    QueryThreadCycleTime(faden, out ulong _);
                }
                finally
                {
                    CloseHandle(faden);
                }
                return null;
            }
            finally
            {
                ende.Set();
                if (helfer.Join(1000))
                {
                    bereit.Close();
                    ende.Close();
                }
            }
        }

        /// <summary>Takte je Millisekunde: dreimal 2 ms rechnen lassen; der höchste Wert gilt (ein Faden kann kurz verdrängt werden).</summary>
        private static double Eichen()
        {
            double best = 0.0;
            for (int k = 0; k < 3; k++)
            {
                if (!QueryThreadCycleTime(_haupt, out ulong z0)) return 0.0;
                long t0 = Stopwatch.GetTimestamp(), ziel = t0 + Stopwatch.Frequency / 500, t1;
                do { t1 = Stopwatch.GetTimestamp(); } while (t1 < ziel);
                if (!QueryThreadCycleTime(_haupt, out ulong z1)) return 0.0;
                double ms = (t1 - t0) * 1000.0 / Stopwatch.Frequency;
                if (ms > 0.0 && z1 > z0) best = Math.Max(best, (z1 - z0) / ms);
            }
            return best;
        }

        private static void KontextFreigeben()
        {
            if (_kontextRoh == IntPtr.Zero) return;
            Marshal.FreeHGlobal(_kontextRoh);
            _kontextRoh = IntPtr.Zero;
            _kontext = IntPtr.Zero;
        }

        private static void Zaehlen(Dictionary<string, int> d, string schluessel)
        {
            d.TryGetValue(schluessel, out int n);
            d[schluessel] = n + 1;
        }

        // ------------------------------------------------------------------ Programmteile und Namen

        private static string Symbol(long adresse)
        {
            Modul m = Suchen(adresse);
            if (m == null && Stopwatch.GetTimestamp() - _modulLesung > Stopwatch.Frequency * 5)
            {
                ModuleLesen();
                m = Suchen(adresse);
            }
            return m != null ? Symbol(m, adresse) : "0x" + adresse.ToString("x", CultureInfo.InvariantCulture) + " (ohne DLL)";
        }

        private static string Symbol(Modul m, long adresse)
        {
            ExportsLaden(m);
            long rva = adresse - m.Basis;
            int k = Array.BinarySearch(m.ExportRva, (int)rva);
            if (k < 0) k = ~k - 1;
            if (k >= 0 && rva - m.ExportRva[k] <= 0x4000)
                return m.Name + "!" + m.ExportName[k] + "+0x" + (rva - m.ExportRva[k]).ToString("x", CultureInfo.InvariantCulture);
            return m.Name + "+0x" + rva.ToString("x", CultureInfo.InvariantCulture);
        }

        private static Modul Suchen(long a)
        {
            Modul[] ms = _module;
            int lo = 0, hi = ms.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (a < ms[mid].Basis) hi = mid - 1;
                else if (a >= ms[mid].Ende) lo = mid + 1;
                else return ms[mid];
            }
            return null;
        }

        private static void ModuleLesen()
        {
            _modulLesung = Stopwatch.GetTimestamp();
            var handles = new IntPtr[2048];
            if (!K32EnumProcessModulesEx(_prozess, handles, (uint)(handles.Length * IntPtr.Size), out uint noetig, 3)) return;
            int anzahl = (int)Math.Min(noetig / (uint)IntPtr.Size, (uint)handles.Length);
            var alt = new Dictionary<long, Modul>();
            foreach (Modul m in _module) alt[m.Basis] = m;
            var liste = new List<Modul>(anzahl);
            var name = new StringBuilder(260);
            var voll = new StringBuilder(1024);
            uint groesse = (uint)Marshal.SizeOf(typeof(ModulInfo));
            for (int k = 0; k < anzahl; k++)
            {
                if (!K32GetModuleInformation(_prozess, handles[k], out ModulInfo info, groesse)) continue;
                long basis = info.Basis.ToInt64();
                name.Length = 0;
                uint laenge = K32GetModuleBaseNameW(_prozess, handles[k], name, (uint)name.Capacity);
                string kurz = KurzName(laenge > 0 ? name.ToString() : "?");
                voll.Length = 0;
                string pfad = K32GetModuleFileNameExW(_prozess, handles[k], voll, (uint)voll.Capacity) > 0 ? voll.ToString() : null;
                if (k == 0 && pfad != null && _spielOrdner == null) _spielOrdner = Path.GetDirectoryName(pfad);
                if (alt.TryGetValue(basis, out Modul bekannt) && bekannt.Name == kurz && bekannt.Ende == basis + info.Groesse)
                {
                    liste.Add(bekannt);
                    continue;
                }
                var neu = new Modul { Name = kurz, Pfad = pfad, Basis = basis, Ende = basis + info.Groesse };
                AbschnitteLesen(neu);
                liste.Add(neu);
            }
            liste.Sort((x, y) => x.Basis.CompareTo(y.Basis));
            _module = liste.ToArray();
        }

        private static string KurzName(string datei)
        {
            if (datei.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || datei.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return datei.Substring(0, datei.Length - 4);
            return datei;
        }

        /// <summary>Kopf eines geladenen Programmteils lesen: Code-Abschnitte und Lage des Verzeichnisses der Einsprungpunkte.</summary>
        private static void AbschnitteLesen(Modul m)
        {
            if (!Lesen(m.Basis, _kopf, _kopf.Length)) return;
            int pe = BitConverter.ToInt32(_kopf, 0x3C);
            if (pe <= 0 || pe + 24 + 120 > _kopf.Length) return;
            if (BitConverter.ToUInt32(_kopf, pe) != 0x00004550u) return;          // "PE\0\0"
            int abschnitte = BitConverter.ToUInt16(_kopf, pe + 6);
            int optGroesse = BitConverter.ToUInt16(_kopf, pe + 20);
            int opt = pe + 24;
            if (BitConverter.ToUInt16(_kopf, opt) != 0x20B) return;                // nur PE32+ (64 Bit)
            m.ExportVerz = BitConverter.ToInt32(_kopf, opt + 112);
            m.ExportGroesse = BitConverter.ToInt32(_kopf, opt + 116);
            var ab = new List<long>();
            var bis = new List<long>();
            int tabelle = opt + optGroesse;
            for (int s = 0; s < abschnitte; s++)
            {
                int o = tabelle + 40 * s;
                if (o + 40 > _kopf.Length) break;
                uint groesse = BitConverter.ToUInt32(_kopf, o + 8);
                uint adresse = BitConverter.ToUInt32(_kopf, o + 12);
                uint art = BitConverter.ToUInt32(_kopf, o + 36);
                if ((art & 0x20000000u) == 0) continue;                             // IMAGE_SCN_MEM_EXECUTE
                ab.Add(m.Basis + adresse);
                bis.Add(m.Basis + adresse + groesse);
            }
            m.CodeAb = ab.ToArray();
            m.CodeBis = bis.ToArray();
        }

        /// <summary>Benannte Einsprungpunkte eines Programmteils lesen (einmal, bei der ersten Adresse darin).</summary>
        private static void ExportsLaden(Modul m)
        {
            if (m.ExportRva != null) return;
            m.ExportName = new string[0];
            m.ExportRva = new int[0];
            if (m.ExportVerz <= 0 || m.ExportGroesse < 40) return;
            var verz = new byte[40];
            if (!Lesen(m.Basis + m.ExportVerz, verz, verz.Length)) return;
            int anzFunk = BitConverter.ToInt32(verz, 0x14), anzNamen = BitConverter.ToInt32(verz, 0x18);
            int funk = BitConverter.ToInt32(verz, 0x1C), namen = BitConverter.ToInt32(verz, 0x20), ordn = BitConverter.ToInt32(verz, 0x24);
            if (anzFunk <= 0 || anzFunk > 200000 || anzNamen <= 0 || anzNamen > 200000) return;
            var funkRva = new byte[anzFunk * 4];
            var namenRva = new byte[anzNamen * 4];
            var ordnungen = new byte[anzNamen * 2];
            if (!Lesen(m.Basis + funk, funkRva, funkRva.Length) || !Lesen(m.Basis + namen, namenRva, namenRva.Length) ||
                !Lesen(m.Basis + ordn, ordnungen, ordnungen.Length)) return;
            var paare = new List<KeyValuePair<int, string>>(anzNamen);
            for (int k = 0; k < anzNamen; k++)
            {
                int ord = BitConverter.ToUInt16(ordnungen, 2 * k);
                if (ord >= anzFunk) continue;
                int rva = BitConverter.ToInt32(funkRva, 4 * ord);
                if (rva <= 0 || (rva >= m.ExportVerz && rva < m.ExportVerz + m.ExportGroesse)) continue;   // Weiterleitung
                string n = TextLesen(m.Basis + BitConverter.ToUInt32(namenRva, 4 * k));
                if (!string.IsNullOrEmpty(n)) paare.Add(new KeyValuePair<int, string>(rva, n));
            }
            paare.Sort((x, y) => x.Key.CompareTo(y.Key));
            var rvas = new int[paare.Count];
            var namenListe = new string[paare.Count];
            for (int k = 0; k < paare.Count; k++)
            {
                rvas[k] = paare[k].Key;
                namenListe[k] = paare[k].Value;
            }
            m.ExportName = namenListe;
            m.ExportRva = rvas;
        }

        // ------------------------------------------------------------------ Systemblick (v2.6)

        private static string ZustandText(int zustand, int grund)
        {
            string z = zustand >= 0 && zustand < Zustaende.Length ? Zustaende[zustand] : "Zustand " + zustand.ToString(CultureInfo.InvariantCulture);
            if (zustand == 5) z += " (" + (grund >= 0 && grund < Gruende.Length ? Gruende[grund] : "Grund " + grund.ToString(CultureInfo.InvariantCulture)) + ")";
            return z;
        }

        /// <summary>
        /// Momentaufnahme aller Fäden im System (NtQuerySystemInformation, SystemProcessInformation): Zustand und Wartegrund des
        /// Hauptfadens, die anderen gerade aktiven Fäden des Spiels (mit Namen) und die anderen Programme mit aktiven Fäden.
        /// Nur lesen, nichts anhalten.
        /// </summary>
        private static string Systemblick()
        {
            if (_sysPuffer == IntPtr.Zero)
            {
                _sysLaenge = 8 << 20;
                _sysPuffer = Marshal.AllocHGlobal(_sysLaenge);
            }
            int st = NtQuerySystemInformation(5, _sysPuffer, _sysLaenge, out int noetig);
            if (st == unchecked((int)0xC0000004) && noetig > _sysLaenge)
            {
                Marshal.FreeHGlobal(_sysPuffer);
                _sysPuffer = IntPtr.Zero;
                _sysLaenge = noetig + (2 << 20);
                _sysPuffer = Marshal.AllocHGlobal(_sysLaenge);
                st = NtQuerySystemInformation(5, _sysPuffer, _sysLaenge, out noetig);
            }
            if (st != 0) return "Systemblick nicht möglich (0x" + st.ToString("x8", CultureInfo.InvariantCulture) + ")";
            string haupt = "nicht gefunden";
            var fremd = new Dictionary<string, int>();
            var eigene = new List<string>();
            long basis = _sysPuffer.ToInt64(), ende = basis + _sysLaenge, off = 0;
            for (int n = 0; n < 8192 && off + 0x100 <= _sysLaenge; n++)
            {
                IntPtr p = new IntPtr(basis + off);
                int naechster = Marshal.ReadInt32(p, 0x00);
                int faeden = Marshal.ReadInt32(p, 0x04);
                long pid = Marshal.ReadInt64(p, 0x50);
                string name = null;
                for (int t = 0; t < faeden; t++)
                {
                    long th = basis + off + 0x100 + 0x50L * t;
                    if (th + 0x50 > ende) break;
                    IntPtr tp = new IntPtr(th);
                    long tid = Marshal.ReadInt64(tp, 0x30);
                    int prio = Marshal.ReadInt32(tp, 0x38);
                    int zustand = Marshal.ReadInt32(tp, 0x44);
                    int grund = Marshal.ReadInt32(tp, 0x48);
                    if (pid == _eigenePid && tid == _hauptId)
                        haupt = ZustandText(zustand, grund) + ", Priorität " + prio.ToString(CultureInfo.InvariantCulture) + ", Kontextwechsel bisher " +
                                ((uint)Marshal.ReadInt32(tp, 0x40)).ToString(CultureInfo.InvariantCulture);
                    bool aktiv = zustand == 1 || zustand == 2 || zustand == 3 || zustand == 7;
                    if (!aktiv || pid == 0) continue;   // Prozess 0 = Leerlauf
                    if (pid == _eigenePid)
                    {
                        if (tid != _wachId && tid != _hauptId) eigene.Add(FadenName(tid) + " " + ZustandText(zustand, grund));
                    }
                    else
                    {
                        if (name == null) name = ProzessName(p);
                        Zaehlen(fremd, name);
                    }
                }
                if (naechster <= 0) break;
                off += naechster;
            }
            var sb = new StringBuilder();
            sb.Append("Hauptfaden ").Append(haupt).Append(" · andere Fäden des Spiels aktiv: ");
            sb.Append(eigene.Count == 0 ? "keine" : string.Join(", ", eigene.ToArray()));
            sb.Append(" · andere Programme aktiv: ");
            if (fremd.Count == 0) sb.Append("keine");
            else
            {
                var l = new List<KeyValuePair<string, int>>(fremd);
                l.Sort((x, y) => y.Value.CompareTo(x.Value));
                for (int k = 0; k < l.Count && k < 12; k++)
                {
                    if (k > 0) sb.Append(", ");
                    sb.Append(l[k].Key);
                    if (l[k].Value > 1) sb.Append(" ×").Append(l[k].Value);
                }
            }
            return sb.ToString();
        }

        private static string ProzessName(IntPtr p)
        {
            int laenge = Marshal.ReadInt16(p, 0x38) & 0xFFFF;
            IntPtr text = Marshal.ReadIntPtr(p, 0x40);
            if (laenge <= 0 || text == IntPtr.Zero) return "System";
            long a = text.ToInt64(), basis = _sysPuffer.ToInt64();
            if (a < basis || a + laenge > basis + _sysLaenge) return "?";
            return Marshal.PtrToStringUni(text, laenge / 2);
        }

        /// <summary>Name eines Fadens des Spiels (Thread-Beschreibung, sonst Startadresse); einmal je Faden gelesen.</summary>
        private static string FadenName(long tid)
        {
            if (_fadenNamen.TryGetValue(tid, out string n)) return n;
            n = "Faden " + tid.ToString(CultureInfo.InvariantCulture);
            IntPtr h = OpenThread(0x0040 | 0x0800, false, (uint)tid);
            if (h != IntPtr.Zero)
            {
                try
                {
                    try
                    {
                        if (GetThreadDescription(h, out IntPtr text) >= 0 && text != IntPtr.Zero)
                        {
                            string d = Marshal.PtrToStringUni(text);
                            LocalFree(text);
                            if (!string.IsNullOrEmpty(d)) n = d;
                        }
                    }
                    catch (EntryPointNotFoundException) { }
                    if (n.StartsWith("Faden ", StringComparison.Ordinal) && NtQueryInformationThread(h, 9, out IntPtr start, IntPtr.Size, out int _) == 0 &&
                        start != IntPtr.Zero)
                        n += " (" + Symbol(start.ToInt64()) + ")";
                }
                finally
                {
                    CloseHandle(h);
                }
            }
            if (_fadenNamen.Count < 4096) _fadenNamen[tid] = n;
            return n;
        }

        private static string TextLesen(long adresse)
        {
            var sb = new StringBuilder(32);
            for (int stueck = 0; stueck < 8; stueck++)
            {
                if (!Lesen(adresse + 32 * stueck, _textPuffer, _textPuffer.Length)) return sb.Length > 0 ? sb.ToString() : null;
                for (int k = 0; k < _textPuffer.Length; k++)
                {
                    byte b = _textPuffer[k];
                    if (b == 0) return sb.ToString();
                    sb.Append((char)b);
                }
            }
            return sb.ToString();
        }

        private static bool Lesen(long adresse, byte[] ziel, int laenge)
        {
            return ReadProcessMemory(_prozess, new IntPtr(adresse), ziel, new IntPtr(laenge), out IntPtr gelesen) && gelesen.ToInt64() == laenge;
        }
#else
        internal static bool Aktiv => false;
        internal static string Status => "aus (nur im fertigen Spiel unter Windows)";
        internal static double ZyklenJeMs => 0.0;
        internal static void Starten(bool ohneStapel) { }
        internal static void Beenden() { }
        internal static long Zyklen() => 0;
        internal static void FensterAuf(long bild, long tick, long zyklen) { }
        internal static void Phase(int phase) { }
        internal static void FensterZu() { }
        internal static List<Probe> Abholen(long bild) => null;
        internal static string Zusammenfassung() => null;
        internal static string ProgrammteileText() => null;
#endif
    }
}
#endif
