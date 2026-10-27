using System;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Items;
using Ashveil.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Ashveil.Networking
{
    /// <summary>
    /// Releaseablauf (22.10.2026): Probe im FERTIGEN Spieltest-Build für den Kurztest (spieltest_kurztest, Lauf 1 und 2; seit der
    /// Nachbesserung 22.10. auch Lauf 4 – dort wie Lauf 2 mit den Schritten, für die Stellenprüfung beim Beenden über das Fenster).
    /// Nur Testcode – ändert keine Spielregel, schreibt nur Logzeilen „[Ashveil] Kurztest-Probe: …“.
    ///   Lauf 1 (neues Profil, Charakter-Editor offen): „Maxim“ über den echten Tastaturpuffer (Text-Ereignisse des Input Systems)
    ///     → muss im Namensfeld stehen, der Chat bleibt zu (EINSTIEG-01). Es wird nichts an den Server geschickt.
    ///   Lauf 2 (Charakter angelegt, eigene vernetzte Figur): echter F2-Druck → kein Körperwechsel, kein Aussehenswunsch,
    ///     Aussehen unverändert (EINSTIEG-03); danach 1,5 s „W“, damit der Speicherstand beim Schließen eine andere Stelle als
    ///     den Spawn trägt (Speichern/Laden an derselben Stelle prüft der Kurztest in Lauf 3 und 4). Nennt außerdem, ob der
    ///     Selbsttest (F10) im Build aktiv ist.
    /// Ohne Fensterfokus schaltet das Input System die Tastatur ab; die Probe stellt es dafür (wie die Suite spielablauf3) auf
    /// „Fokus ignorieren“ – der Build-Prozess ist nur für diesen Lauf gestartet. Nur mit "-ashveil-kurztest-probe" und nur, wo
    /// Testeinstiege erlaubt sind (<see cref="TestBuild"/>; im Release-Build wirkungslos).
    /// </summary>
    public sealed class SpieltestKurztestProbe : MonoBehaviour
    {
        public const string Argument = "-ashveil-kurztest-probe";
        private const string Praefix = "[Ashveil] Kurztest-Probe: ";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Anlegen()
        {
            if (!TestBuild.Enabled || Application.isEditor || !ProfileSelection.HasArg(Argument)) return;
            var go = new GameObject("SpieltestKurztestProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<SpieltestKurztestProbe>();
        }

        private IEnumerator Start()
        {
            Debug.Log(Praefix + "gestartet");
            // Warten, bis entweder der Charakter-Editor offen ist (Lauf 1) oder die eigene Figur samt Inventar bereit ist (Lauf 2)
            float bis = Time.unscaledTime + 55f;
            while (Time.unscaledTime < bis)
            {
                if (CharacterCreatorUi.Instance != null && CharacterCreatorUi.Instance.IsOpen) break;
                NetworkAppearance app = NetworkAppearance.Local;
                if (app != null && !app.Hidden && app.Current != null && InventoryClient.Current != null && InventoryClient.Current.Ready) break;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(3f);
            string eingabe = EingabeAn();
            Debug.Log(Praefix + "Eingabe: " + eingabe);
            if (CharacterCreatorUi.Instance != null && CharacterCreatorUi.Instance.IsOpen) yield return Namensfeld(CharacterCreatorUi.Instance);
            else if (NetworkAppearance.Local != null) yield return F2UndGehen(NetworkAppearance.Local);
            else Debug.Log(Praefix + "nicht geprüft – weder Charakter-Editor noch eigene Figur nach 55 s");
            if (Keyboard.current != null && Keyboard.current.enabled) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            float haenger = HaengerSekunden();
            if (haenger > 0f) Haenger(haenger);
        }

        // ------------------------------------------------------------------ 22.10.2026 (Runde 5): Hänger für den Mausfilter-Rückfall

        /// <summary>
        /// „-ashveil-probe-haenger &lt;s&gt;“ (nur mit der Kurztest-Probe): Nach den Schritten steht die Spielschleife s Sekunden still
        /// (ein Hänger), Windows stellt aber weiter GESENDETE Fensternachrichten zu – wie bei einem Spiel, das noch Nachrichten annimmt,
        /// dessen Bildwechsel aber ausbleibt. Eingereihte Nachrichten bleiben liegen (PeekMessage mit einem Filter, der keine trifft).
        /// Eine Probe von außen schickt währenddessen zweimal WM_CLOSE (SendMessage) im Abstand von mehr als
        /// Mausfilter.SchliessenFristSek: das zweite muss an Unity übergeben werden (Logzeile „an Unity übergeben“) und das Spiel
        /// danach ohne Absturz enden. Nur Testcode, ändert keine Spielregel.
        /// </summary>
        public const string HaengerArgument = "-ashveil-probe-haenger";

        private static float HaengerSekunden()
        {
            string s = ProfileSelection.ArgValue(HaengerArgument);
            return !string.IsNullOrEmpty(s) && float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)
                ? Mathf.Clamp(v, 0f, 60f) : 0f;
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [StructLayout(LayoutKind.Sequential)]
        private struct Nachricht { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

        [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
        private static extern bool PeekMessage(out Nachricht m, IntPtr hwnd, uint min, uint max, uint entfernen);

        private static void Haenger(float sekunden)
        {
            Debug.Log(Praefix + $"Hänger beginnt ({sekunden:0} s, nur gesendete Nachrichten werden zugestellt)");
            var uhr = System.Diagnostics.Stopwatch.StartNew();
            const uint KeineNachricht = 0x7FFE;   // Filter, der keine eingereihte Nachricht trifft – gesendete kommen trotzdem an
            while (uhr.Elapsed.TotalSeconds < sekunden)
            {
                PeekMessage(out _, IntPtr.Zero, KeineNachricht, KeineNachricht, 0);
                System.Threading.Thread.Sleep(5);
            }
            Debug.Log(Praefix + $"Hänger vorbei nach {uhr.Elapsed.TotalSeconds:0.0} s");
        }
#else
        private static void Haenger(float sekunden) => Debug.Log(Praefix + "Hänger nur im Windows-Build");
#endif

        // ------------------------------------------------------------------ Lauf 1: Namensfeld (EINSTIEG-01)

        private static IEnumerator Namensfeld(CharacterCreatorUi editor)
        {
            editor.SelectCategory(CharacterCreatorUi.Category.Name);
            for (int i = 0; i < 3; i++) yield return null;
            string vorher = editor.NameText ?? "";
            string weg = RawInput.TestTextEingeben("Maxim");
            for (int i = 0; i < 6; i++) yield return null;
            string nachher = editor.NameText ?? "";
            bool chatOffen = SocialUi.Instance != null &&
                             typeof(SocialUi).GetField("_typing", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(SocialUi.Instance) is bool t && t;
            string text = $"Namensfeld „{vorher}“ → „{nachher}“ (Soll „{vorher}Maxim“), getippt über „{weg}“, Chat {(chatOffen ? "OFFEN" : "zu")}, Editor {(editor.IsOpen ? "offen" : "ZU")}";
            if (weg.Length == 0) Debug.Log(Praefix + "Namensfeld nicht geprüft – keine Tastatur für Text-Ereignisse (" + text + ")");
            else if (nachher == vorher + "Maxim" && !chatOffen) Debug.Log(Praefix + "Namensfeld bestanden – " + text);
            else Debug.Log(Praefix + "Namensfeld FEHLER – " + text);
        }

        // ------------------------------------------------------------------ Lauf 2: F2 (EINSTIEG-03) und ein paar Schritte

        private static IEnumerator F2UndGehen(NetworkAppearance app)
        {
            var runner = FindAnyObjectByType<SelfTestRunner>();
            // 22.10.2026 (REL-02): die Komponente bleibt im Development-Build aktiv – maßgeblich ist, ob F10 sie startet (F10Aktiv)
            Debug.Log(Praefix + $"Selbsttest-Taste F10 im Build: {(runner == null ? "kein SelfTestRunner in der Szene" : runner.F10Aktiv ? "AKTIV (F10 startet den Selbsttest an der eigenen Figur)" : runner.isActiveAndEnabled ? "aus (F10 nur im Editor oder bei angefordertem Selbsttest)" : "aus")}");

            CharacterBodySwitcher koerper = app.GetComponent<CharacterBodySwitcher>();
            Keyboard kb = Keyboard.current;
            if (koerper == null || kb == null || !kb.enabled)
            {
                Debug.Log(Praefix + $"F2 nicht geprüft – {(koerper == null ? "kein CharacterBodySwitcher an der Figur" : "keine eingeschaltete Tastatur")}");
            }
            else
            {
                bool erlaubt = koerper.DebugSwitchAllowed;
                bool gefangen = UiPointer.KeyboardCaptured;
                int index0 = koerper.ActiveIndex, wuensche = 0;
                string aussehen0 = app.Current != null ? app.Current.Encode() : "";
                Action<int> zaehlen = _ => wuensche++;
                koerper.BodyChangeRequested += zaehlen;
                bool kam;
                try
                {
                    InputSystem.QueueStateEvent(kb, new KeyboardState(Key.F2));
                    yield return null;
                    kam = RawInput.WasPressed(GameKey.SwitchBody);
                    InputSystem.QueueStateEvent(kb, new KeyboardState());
                    yield return new WaitForSecondsRealtime(1.5f);   // Zeit für einen etwaigen Server-Rundweg des Aussehens
                }
                finally
                {
                    koerper.BodyChangeRequested -= zaehlen;
                }
                string aussehen1 = app.Current != null ? app.Current.Encode() : "";
                string text = $"F2 kam an {kam}, F2 erlaubt {erlaubt}, Tastatur gefangen {gefangen}, Wünsche {wuensche}, Körper {koerper.ActiveIndex} (vorher {index0}, {koerper.ActiveName}), " +
                              $"Aussehen {(aussehen1 == aussehen0 ? "unverändert" : "GEÄNDERT")}";
                if (!kam || gefangen) Debug.Log(Praefix + "F2 nicht geprüft – " + text);
                else if (!erlaubt && wuensche == 0 && koerper.ActiveIndex == index0 && aussehen1 == aussehen0) Debug.Log(Praefix + "F2 bestanden – " + text);
                else Debug.Log(Praefix + "F2 FEHLER – " + text);
            }

            // Ein paar Schritte vorwärts: der Speicherstand beim Schließen soll nicht am Spawn liegen
            if (kb == null || !kb.enabled) yield break;
            Transform t = app.transform;
            Vector3 von = t.position;
            bool blockiert = UiPointer.GameBlocked;
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.W));
            yield return new WaitForSecondsRealtime(1.5f);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return new WaitForSecondsRealtime(1f);
            Vector3 nach = t.position;
            float weg = Vector3.Distance(new Vector3(von.x, 0f, von.z), new Vector3(nach.x, 0f, nach.z));
            Debug.Log(Praefix + $"gegangen von ({von.x:0}/{von.z:0}) nach ({nach.x:0}/{nach.z:0}), {weg:0.0} m{(blockiert ? " (Spiel war blockiert)" : "")}");
        }

        // ------------------------------------------------------------------ Eingabe ohne Fensterfokus

        private static string EingabeAn()
        {
            InputSettings s = InputSystem.settings;
            bool fokus = Application.isFocused;
            string vorher = s != null ? s.backgroundBehavior.ToString() : "keine Einstellungen";
            if (s != null) s.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Keyboard kb = Keyboard.current;
            bool aus = kb != null && !kb.enabled;
            if (aus) InputSystem.EnableDevice(kb);
            return $"Fenster {(fokus ? "mit" : "ohne")} Fokus; „Fokus ignorieren“ (vorher {vorher}); Tastatur " +
                   $"{(kb == null ? "fehlt" : kb.enabled ? (aus ? "eingeschaltet" : "an") : "AUS")}";
        }
    }
}
