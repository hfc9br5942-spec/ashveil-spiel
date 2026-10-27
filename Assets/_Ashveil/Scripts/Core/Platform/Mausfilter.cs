using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using Debug = UnityEngine.Debug;

namespace Ashveil.Core.Platform
{
    /// <summary>
    /// Mausfilter (CORE-005, Max 09.10.2026 abends: „Ja, fest einbauen“). Im fertigen Spiel unter Windows immer an, in jedem Build;
    /// abschalten nur mit dem Startargument <see cref="AusArgument"/>. Im Editor, ohne Fenster (Server mit -batchmode) und auf
    /// anderen Systemen ohne Wirkung.
    ///
    /// Warum: Unity 6 verarbeitet unter Windows die Maus als Pointer-Nachrichten (EnableMouseInPointer). Die fasst Windows nicht
    /// zusammen; eine Maus mit 1000 Hz schickt je Bild Dutzende Pointer-Updates, Mausbewegungen, Mauszeiger-Anfragen und
    /// Treffertests. Unity holt sie zwischen TimeUpdate und Initialization ab und braucht für jede mehrere Windows-Aufrufe, die
    /// eine systemweite Sperre teilen. Nimmt daneben ein anderes Programm diese Sperre oft (bei Max „Adobe Desktop Service“),
    /// wartet der Hauptfaden 50–460 ms (Messung v2.5/v2.6: Wartegrund WrResource; bekannter Unity-Fehler UUM-147395).
    /// Max' Probe am 09.10. (je etwa 10 s): ohne Filter 20 Bilder über 50 ms, längstes 238 ms; mit Filter keines über 33 ms.
    ///
    /// Was er tut: Unitys Fensterprozedur wird auf <see cref="Prozedur"/> umgeleitet; die Entscheidungen trifft
    /// <see cref="MausfilterLogik"/> (je Bild eine Bewegung sofort, die neueste beim Bildwechsel nach; Tasten, Klicks, Mausrad und
    /// Roheingabe unverändert). Der Bildwechsel ist ein eigener Schritt ganz am Anfang von Initialization – nach dem Abholen der
    /// Nachrichten, vor Unitys Eingabe-Verarbeitung. Beim Beenden (Application.quitting) und beim Zerstören des Fensters wird die
    /// alte Prozedur wiederhergestellt.
    ///
    /// Sicherheit: Ein Fehler in der Prozedur oder im Bildwechsel schaltet den Filter ab (alles geht unverändert durch). Bleibt
    /// der Bildwechsel aus, hilft die Notlieferung der Logik (spätestens alle 50 ms); zusätzlich setzt eine Wache
    /// (Application.onBeforeRender) den Schritt neu, wenn er drei Bilder lang nicht lief. Keine Speicheranforderung je Nachricht.
    ///
    /// Im Entwicklungs-Build zählt die Prozedur außerdem die Nachrichten nach Art (für die Bild-Spitzen-Messung,
    /// UI/Diagnostics/FrameSpikeProfiler) – auch mit abgeschaltetem Filter.
    ///
    /// Fenster schließen (22.10.2026, REL-01/SPEICHERN-01): Bei X, Alt+F4 oder Taskleiste → Schließen beendete Unity das Spiel
    /// INNERHALB dieser Prozedur (OnApplicationQuit lief noch, aufgerufen aus Prozedur → CallWindowProc) und baute die Laufzeit ab,
    /// während unser verwalteter Rahmen noch auf dem Stapel lag – danach Absturz 0xC0000005 (Probe mit echtem WM_CLOSE: alter
    /// Test-Client und alter Spieltest-Build). Die „Exit -2 ohne Abbau“ der Kurztests waren dagegen Process.CloseMainWindow im
    /// Editor-Mono (= TerminateProcess mit -2), kein echtes Schließen. Die Prozedur fängt deshalb das ERSTE WM_CLOSE bzw. SC_CLOSE an Unitys Fenster
    /// ab – unabhängig vom Filter, auch wenn er nach einem Fehler aus ist – und der Bildwechsel ruft im Hauptfaden
    /// Application.Quit(): derselbe Weg wie „Speichern und beenden“ (Exit 0, voller Abbau, Speichern beim Beenden). Nach
    /// Application.quitting ist ohnehin wieder Unitys Prozedur oben. Wirkt nur, wo die Prozedur hängt (jeder Build außer Release
    /// mit <see cref="AusArgument"/>).
    ///
    /// Weiteres Schließen (22.10.2026, Runde 5): Vorher ging ein zweites Schließen nach Application.Quit oder nach 1 s ohne
    /// Bildwechsel per CallWindowProc an Unity – Unity beendete dann wieder INNERHALB dieser Prozedur (alter Absturzweg 0xC0000005).
    /// Jetzt: Während die Bitte läuft (<see cref="SchliessenFristSek"/> ab dem ersten Schließen), wird jedes weitere Schließen
    /// verschluckt – Application.Quit ist gerufen bzw. kommt mit dem nächsten Bildwechsel. Kommt danach noch ein Schließen (das
    /// Spiel hängt, z. B. die Spielschleife steht, während Windows Nachrichten zustellt), wird die Nachricht an Unity ÜBERGEBEN:
    /// erst Unitys eigene Prozedur wieder einsetzen (<see cref="Loesen"/>), dann dieselbe Nachricht neu einreihen (PostMessage) –
    /// Unity bearbeitet sie aus seiner eigenen Nachrichtenschleife, ohne unseren verwalteten Rahmen auf dem Stapel (wie im Spiel
    /// ohne Mausfilter). So bleibt ein hängendes Spiel schließbar, und der alte Absturzweg ist zu.
    /// </summary>
    public static class Mausfilter
    {
        /// <summary>Startargument: Mausfilter aus (zum Vergleich; im Entwicklungs-Build werden die Nachrichten weiter gezählt).</summary>
        public const string AusArgument = "-ashveil-ohne-mausfilter";

        /// <summary>Notlieferung der Logik: 50 ms.</summary>
        private const int NotMs = 50;

        /// <summary>
        /// 22.10.2026 (Runde 5): So lange ab dem ersten Schließen gilt ein weiteres Schließen als „doppelt gedrückt, Beenden läuft“
        /// und wird verschluckt; danach wird es an Unity übergeben (siehe Klassenbeschreibung). 5 s: normal ruft der nächste
        /// Bildwechsel Application.Quit nach Millisekunden, und nach Application.quitting ist unsere Prozedur ohnehin weg – wer
        /// nach 5 s noch einmal schließt, hat ein hängendes Spiel vor sich.
        /// </summary>
        public const int SchliessenFristSek = 5;

        private static string _status = "aus (nur im fertigen Spiel unter Windows)";
        private static volatile bool _filtern = false;
        private static MausfilterLogik _logik = null;
        private static int _hakenNeu = 0;

        /// <summary>Zustand für Log und Messung, z. B. „an (Fenster umgeleitet)“.</summary>
        public static string Status => _status;

        /// <summary>true, solange der Filter Nachrichten zusammenfasst.</summary>
        public static bool Aktiv => _filtern;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Arten der gezählten Fenster-Nachrichten (Reihenfolge wie in <see cref="NachrichtenAbholen"/>).</summary>
        public static readonly string[] NachrichtenArten = { "Pointer-Update", "Mausbewegung", "Mauszeiger", "Treffertest", "Roheingabe", "andere" };

        private static readonly int[] _zaehler = new int[6];
        private static bool _zaehlt = false;

        /// <summary>true, wenn die Fenster-Nachrichten gezählt werden (Entwicklungs-Build unter Windows).</summary>
        public static bool Zaehlt => _zaehlt;

        /// <summary>Vom Hauptfaden: Zähler seit dem letzten Abholen nach <paramref name="ziel"/> (6 Arten) und auf null.</summary>
        public static void NachrichtenAbholen(int[] ziel)
        {
            for (int k = 0; k < _zaehler.Length && k < ziel.Length; k++)
            {
                ziel[k] = _zaehler[k];
                _zaehler[k] = 0;
            }
        }

        /// <summary>Zeile für die Zusammenfassung der Messung: was der Filter getan hat; null, wenn er nie eingerichtet wurde.</summary>
        public static string FilterText()
        {
            MausfilterLogik l = _logik;
            if (l == null) return null;
            var sb = new StringBuilder();
            sb.Append("  Mausfilter: ").Append(_status)
              .Append(" · zurückgehalten Pointer-Update ").Append(l.Zurueckgehalten[0].ToString(CultureInfo.InvariantCulture))
              .Append(" · Mausbewegung ").Append(l.Zurueckgehalten[1].ToString(CultureInfo.InvariantCulture))
              .Append(" · nachgereicht ").Append(l.Nachgereicht[0].ToString(CultureInfo.InvariantCulture))
              .Append(" + ").Append(l.Nachgereicht[1].ToString(CultureInfo.InvariantCulture))
              .Append(" · verfallen ").Append(l.Verfallen.ToString(CultureInfo.InvariantCulture))
              .Append(" · selbst beantwortet Mauszeiger ").Append(l.SelbstBeantwortet[0].ToString(CultureInfo.InvariantCulture))
              .Append(" · Treffertest ").Append(l.SelbstBeantwortet[1].ToString(CultureInfo.InvariantCulture))
              .Append(" · sofort wegen Tastenwechsel ").Append(l.Tastenwechsel.ToString(CultureInfo.InvariantCulture))
              .Append(" · Notlieferungen ").Append(l.Notlieferungen.ToString(CultureInfo.InvariantCulture));
            if (_hakenNeu > 0) sb.Append(" · Schritt in der Spielschleife neu gesetzt ").Append(_hakenNeu.ToString(CultureInfo.InvariantCulture)).Append('×');
            return sb.ToString();
        }
#endif

#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
        private const int GwlpWndProc = -4;
        private const uint WmNcDestroy = 0x0082;
        // 22.10.2026 (REL-01): Fenster schließen – WM_CLOSE (X, CloseMainWindow) und WM_SYSCOMMAND/SC_CLOSE (Alt+F4, Taskleiste)
        private const uint WmClose = 0x0010, WmSysCommand = 0x0112;
        private const long ScClose = 0xF060;

        private delegate IntPtr FensterProzedur(IntPtr hwnd, uint nachricht, IntPtr wParam, IntPtr lParam);
        private delegate bool FensterAufzaehlung(IntPtr hwnd, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(uint threadId, FensterAufzaehlung callback, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassNameW(IntPtr hwnd, StringBuilder name, int max);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

        [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
        private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr hwnd, uint nachricht, IntPtr wParam, IntPtr lParam);

        // 22.10.2026 (Runde 5): Schließen an Unity übergeben – neu einreihen statt aus unserer Prozedur heraus weiterreichen
        [DllImport("user32.dll", EntryPoint = "PostMessageW")]
        private static extern bool PostMessage(IntPtr hwnd, uint nachricht, IntPtr wParam, IntPtr lParam);

        // Die Delegaten müssen leben, solange Windows sie ruft (statische Felder).
        private static readonly FensterProzedur _prozedur = Prozedur;
        private static readonly FensterAufzaehlung _aufzaehlung = FensterPruefen;
        private static readonly StringBuilder _klasse = new StringBuilder(64);
        private static IntPtr _eigene = IntPtr.Zero, _alt = IntPtr.Zero, _fenster = IntPtr.Zero, _gefunden = IntPtr.Zero;
        private static bool _gewuenscht, _anhaengen, _beendet;
        private static volatile bool _fehler;
        private static int _suchBilder;
        private static int _hakenLaeufe, _hakenZuletzt, _ohneHaken;
        // 22.10.2026 (REL-01): Schließen erbeten (Prozedur) → Application.Quit gerufen (Bildwechsel); Zeitpunkt der Bitte
        private static volatile bool _schliessenErbeten, _quitGerufen;
        private static long _schliessenAb;
        // 22.10.2026 (Runde 5): weiteres Schließen verschluckt (einmal melden) bzw. an Unity übergeben
        private static bool _verschlucktGemeldet, _uebergeben;

        private struct AshveilMausfilter { }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Starten()
        {
            if (_logik != null) return;
            _gewuenscht = !HatArgument(AusArgument);
#if DEVELOPMENT_BUILD
            _anhaengen = true;   // zählen auch ohne Filter
#else
            _anhaengen = _gewuenscht;
#endif
            if (Application.isBatchMode)
            {
                _status = "aus (ohne Fenster)";
                Melden();
                return;
            }
            if (IntPtr.Size != 8)
            {
                _status = "aus (nur 64 Bit)";
                Melden();
                return;
            }
            if (!_anhaengen)
            {
                _status = "aus (Startargument " + AusArgument + ")";
                Melden();
                return;
            }
            _logik = new MausfilterLogik(Stopwatch.Frequency * NotMs / 1000);
            HakenSetzen();
            Application.quitting += Beenden;
            Application.onBeforeRender += Wachen;
            _status = "wartet auf Unitys Fenster";
            Anhaengen();
        }

        /// <summary>Unitys Fenster suchen und die Fensterprozedur umleiten. Nur vom Hauptfaden (dem Besitzer des Fensters).</summary>
        private static void Anhaengen()
        {
            if (_fenster != IntPtr.Zero || _beendet || !_anhaengen) return;
            try
            {
                _gefunden = IntPtr.Zero;
                EnumThreadWindows(GetCurrentThreadId(), _aufzaehlung, IntPtr.Zero);
                if (_gefunden == IntPtr.Zero) return;
                if (_eigene == IntPtr.Zero) _eigene = Marshal.GetFunctionPointerForDelegate(_prozedur);
                IntPtr alt = SetWindowLongPtr(_gefunden, GwlpWndProc, _eigene);
                if (alt == IntPtr.Zero)
                {
                    _anhaengen = false;
                    _status = "aus (Fenster nicht umgeleitet)";
                    Melden();
                    return;
                }
                _alt = alt;
                _fenster = _gefunden;
                _filtern = _gewuenscht;
#if DEVELOPMENT_BUILD
                _zaehlt = true;
#endif
                _status = _gewuenscht ? "an (Fenster umgeleitet)" : "aus (Startargument " + AusArgument + "), Nachrichten werden nur gezählt";
                Melden();
            }
            catch (Exception e)
            {
                _filtern = false;
                _anhaengen = false;
                _status = "aus (" + e.GetType().Name + ")";
                Melden();
            }
        }

        [AOT.MonoPInvokeCallback(typeof(FensterAufzaehlung))]
        private static bool FensterPruefen(IntPtr hwnd, IntPtr lParam)
        {
            _klasse.Length = 0;
            GetClassNameW(hwnd, _klasse, _klasse.Capacity);
            if (_klasse.ToString() != "UnityWndClass") return true;
            _gefunden = hwnd;
            return false;
        }

        /// <summary>Application.quitting: alte Fensterprozedur zurück, danach nichts mehr umleiten.</summary>
        private static void Beenden()
        {
            _beendet = true;
            Loesen();
        }

        /// <summary>
        /// Die alte Fensterprozedur wiederherstellen. Nur, wenn unsere noch die oberste ist; sonst bliebe die eines anderen außen
        /// vor – unsere reicht dann ab jetzt alles unverändert weiter (_fenster ist null, _alt bleibt gültig).
        /// </summary>
        private static void Loesen()
        {
            _filtern = false;
            IntPtr f = _fenster;
            if (f == IntPtr.Zero) return;
            _fenster = IntPtr.Zero;
            try
            {
                if (GetWindowLongPtr(f, GwlpWndProc) == _eigene) SetWindowLongPtr(f, GwlpWndProc, _alt);
            }
            catch (Exception) { }
        }

        [AOT.MonoPInvokeCallback(typeof(FensterProzedur))]
        private static IntPtr Prozedur(IntPtr hwnd, uint nachricht, IntPtr wParam, IntPtr lParam)
        {
            IntPtr alt = _alt;
            try
            {
#if DEVELOPMENT_BUILD
                int k;
                switch (nachricht)
                {
                    case MausfilterLogik.WmPointerUpdate: k = 0; break;
                    case MausfilterLogik.WmMouseMove: k = 1; break;
                    case MausfilterLogik.WmSetCursor: k = 2; break;
                    case MausfilterLogik.WmNcHitTest: k = 3; break;
                    case 0x00FF: k = 4; break;   // WM_INPUT
                    default: k = 5; break;
                }
                _zaehler[k]++;
#endif
                // 22.10.2026 (REL-01): erstes Schließen an Unitys Fenster abfangen – vor und unabhängig von _filtern; Application.Quit
                // ruft der Bildwechsel im Hauptfaden.
                // 22.10.2026 (Runde 5): ein weiteres Schließen NIE mehr per CallWindowProc aus dieser Prozedur an Unity (dort beendete
                // Unity mit unserem Rahmen auf dem Stapel → 0xC0000005): innerhalb der Frist verschlucken (Beenden läuft), danach an
                // Unity übergeben – Unitys Prozedur wieder oben, Nachricht neu einreihen; Unity bearbeitet sie ohne unseren Rahmen.
                if (hwnd == _fenster && !_beendet &&
                    (nachricht == WmClose || (nachricht == WmSysCommand && (wParam.ToInt64() & 0xFFF0) == ScClose)))
                {
                    long jetzt = Stopwatch.GetTimestamp();
                    if (!_schliessenErbeten)
                    {
                        _schliessenAb = jetzt;
                        _schliessenErbeten = true;
                        return IntPtr.Zero;
                    }
                    double sek = (jetzt - _schliessenAb) / (double)Stopwatch.Frequency;
                    if (sek < SchliessenFristSek)
                    {
                        if (!_verschlucktGemeldet)
                        {
                            _verschlucktGemeldet = true;
                            Debug.Log($"[Ashveil] Fenster schliessen: weitere Bitte nach {sek:0.00} s verschluckt (Beenden läuft, Application.Quit " +
                                      $"{(_quitGerufen ? "gerufen" : "kommt mit dem nächsten Bild")})");
                        }
                        return IntPtr.Zero;
                    }
                    if (!_uebergeben)
                    {
                        _uebergeben = true;
                        _beendet = true;   // nicht wieder anhängen (Bildwechsel/Wache)
                        Loesen();          // Unitys Prozedur wieder oben – nur, wenn unsere die oberste war
                        bool eingereiht = PostMessage(hwnd, nachricht, wParam, lParam);
                        Debug.Log($"[Ashveil] Fenster schliessen: nach {sek:0.0} s noch nicht beendet (Application.Quit {(_quitGerufen ? "gerufen" : "nicht gerufen – Spielschleife steht")}) " +
                                  $"→ an Unity übergeben ({(eingereiht ? "neu eingereiht, ohne Mausfilter-Rahmen" : "Einreihen fehlgeschlagen – nächstes Schließen geht direkt an Unity")})");
                        _status = "aus (Schliessen an Unity übergeben)";
                        return IntPtr.Zero;
                    }
                }
                if (nachricht == WmNcDestroy)
                {
                    if (hwnd == _fenster)
                    {
                        // Fenster wird zerstört: lösen; falls Unity ein neues erstellt, sucht der Bildwechsel es wieder
                        Loesen();
                        _suchBilder = 0;
                        if (!_beendet) _status = "wartet auf Unitys Fenster (altes zerstört)";
                    }
                }
                else if (_filtern && hwnd == _fenster)
                {
                    MausfilterLogik.Entscheidung e = _logik.Entscheiden(nachricht, wParam, lParam, Stopwatch.GetTimestamp());
                    if (e.MausVorher) CallWindowProc(alt, hwnd, MausfilterLogik.WmMouseMove, e.MausW, e.MausL);
                    if (e.Urteil == MausfilterLogik.Urteil.Zurueckhalten) return IntPtr.Zero;
                    if (e.Urteil == MausfilterLogik.Urteil.SelbstBeantworten) return e.Antwort;
                }
            }
            catch (Exception)
            {
                _filtern = false;   // im Zweifel alles unverändert weiterreichen
                _fehler = true;
            }
            return CallWindowProc(alt, hwnd, nachricht, wParam, lParam);
        }

        /// <summary>Schritt am Anfang von Initialization: Nachgereichtes an Unity, neues Bild, Vollbild-Stand.</summary>
        private static void Bildwechsel()
        {
            _hakenLaeufe++;
            // 22.10.2026 (REL-01): Schließen über das Fenster → sauber beenden wie „Speichern und beenden“ (vor allen frühen Returns)
            if (_schliessenErbeten && !_quitGerufen)
            {
                _quitGerufen = true;
                Debug.Log("[Ashveil] Fenster schliessen → Application.Quit (sauberes Beenden, Speichern beim Beenden)");
                Application.Quit();
            }
            if (_fehler)
            {
                _fehler = false;
                _status = "aus (Fehler in der Fensterprozedur)";
                Melden();
            }
            if (_fenster == IntPtr.Zero)
            {
                // Fenster noch nicht gefunden oder neu erstellt: alle 30 Bilder suchen, höchstens 600 Bilder lang
                if (!_anhaengen || _beendet || _suchBilder > 600) return;
                if (_suchBilder % 30 == 0) Anhaengen();
                _suchBilder++;
                if (_fenster == IntPtr.Zero && _suchBilder > 600)
                {
                    _anhaengen = false;
                    _status = "aus (Unitys Fenster nicht gefunden)";
                    Melden();
                }
                return;
            }
            if (!_filtern) return;
            try
            {
                FullScreenMode modus = Screen.fullScreenMode;
                _logik.Vollbild = modus == FullScreenMode.FullScreenWindow || modus == FullScreenMode.ExclusiveFullScreen;
                MausfilterLogik.Nachreichung n = _logik.Bildwechsel();
                if (n.Pointer) CallWindowProc(_alt, _fenster, MausfilterLogik.WmPointerUpdate, n.PointerW, n.PointerL);
                if (n.Maus) CallWindowProc(_alt, _fenster, MausfilterLogik.WmMouseMove, n.MausW, n.MausL);
            }
            catch (Exception e)
            {
                _filtern = false;
                _status = "aus (Fehler beim Bildwechsel: " + e.GetType().Name + ")";
                Melden();
            }
        }

        /// <summary>Wache (einmal je Bild vor dem Zeichnen): lief der Schritt drei Bilder lang nicht, ihn neu setzen.</summary>
        private static void Wachen()
        {
            if (_hakenLaeufe != _hakenZuletzt)
            {
                _hakenZuletzt = _hakenLaeufe;
                _ohneHaken = 0;
                return;
            }
            if (++_ohneHaken < 3 || _beendet) return;
            _ohneHaken = 0;
            if (HakenSetzen()) _hakenNeu++;
        }

        /// <summary>Den Schritt ganz an den Anfang von Initialization setzen; true, wenn er neu eingefügt wurde.</summary>
        private static bool HakenSetzen()
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoopSystem[] oben = loop.subSystemList;
            if (oben == null) return false;
            for (int i = 0; i < oben.Length; i++)
            {
                if (oben[i].type != typeof(Initialization)) continue;
                PlayerLoopSystem[] subs = oben[i].subSystemList ?? new PlayerLoopSystem[0];
                for (int k = 0; k < subs.Length; k++)
                    if (subs[k].type == typeof(AshveilMausfilter)) return false;
                var neu = new List<PlayerLoopSystem>(subs.Length + 1)
                {
                    new PlayerLoopSystem { type = typeof(AshveilMausfilter), updateDelegate = Bildwechsel }
                };
                neu.AddRange(subs);
                oben[i].subSystemList = neu.ToArray();
                loop.subSystemList = oben;
                PlayerLoop.SetPlayerLoop(loop);
                return true;
            }
            return false;
        }

        /// <summary>Eine Zeile je Zustandswechsel ins Log (der Kurztest prüft „[Ashveil] Mausfilter: an“).</summary>
        private static void Melden() => Debug.Log("[Ashveil] Mausfilter: " + _status);

        private static bool HatArgument(string name)
        {
            try
            {
                foreach (string a in Environment.GetCommandLineArgs())
                    if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception) { }
            return false;
        }
#endif
    }
}
