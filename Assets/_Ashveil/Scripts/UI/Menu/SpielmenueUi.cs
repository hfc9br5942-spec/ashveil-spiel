using System.Collections;
using System.Text;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Items;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ashveil.UI
{
    /// <summary>
    /// Spielmenü (Esc) – UI-204; Spieltest r2 (Max 09.10.2026: „Ich konnte das Spiel nicht speichern und beenden“).
    ///
    /// Öffnet sich über die Esc-Regel (<see cref="UiFensterstapel"/>): Erst schließen die offenen Fenster, dann wird das Ziel
    /// aufgehoben, erst dann kommt dieses Menü. Einträge: Zurück zum Spiel · Einstellungen · Tastenbelegung · Speichern und beenden.
    /// Die Welt läuft weiter (MMO). Solange das Menü offen ist, ruht die eigene Figur (keine Bewegung, kein Kampf, keine Kamera),
    /// und die Fenster-Tasten schweigen (<see cref="UiPointer.ModalOpen"/>). Einstellungen und Tastenbelegung liegen über dem Menü;
    /// Esc schließt sie und führt zurück ins Menü.
    /// „Speichern und beenden“: Der Server speichert sofort und bestätigt (<see cref="SitzungClient"/>); erst dann endet das Spiel.
    /// Außerhalb sicherer Orte (COMBAT-094, Max 10.10.2026) wartet der Server erst 20 s (Countdown im Menü; Schaden oder Bewegung
    /// brechen ab); in Lagern und Siedlungen geht es sofort. Esc während des Countdowns bricht ihn ab.
    /// Antwortet er nicht, bietet das Menü „Trotzdem beenden“ an – der Server speichert beim Abmelden ohnehin.
    /// Bedienung: Maus, oder W/S bzw. Pfeiltasten und Enter.
    /// Aussehen: vorerst der Oberflächen-Baukasten von heute; das AAA-Aussehen kommt mit UI-204.
    /// </summary>
    [DefaultExecutionOrder(-499)]   // früh im Bild: Esc-Regel vor allen Fenstern; nach InventoryUi (−500), die UiPointer.OverUi zurücksetzt
    public sealed class SpielmenueUi : MonoBehaviour
    {
        private enum Phase { Normal, Speichert, Wartet, Gespeichert, KeineAntwort }

        private sealed class Knopf
        {
            public RectTransform Rt;
            public Image Back;
            public TextMeshProUGUI Label;
        }

        private static readonly Color ButtonColor = new Color(0.2f, 0.155f, 0.1f, 0.95f);
        private static readonly Color ButtonHover = new Color(0.34f, 0.25f, 0.15f, 0.98f);
        private static readonly Color ButtonActive = new Color(0.58f, 0.39f, 0.15f, 1f);
        private static readonly Color Gold = new Color(0.91f, 0.79f, 0.54f);
        private const float AntwortWartezeit = 6f;
        private const float AnzeigeVorEnde = 0.9f;

        private static readonly string[] Eintraege = { "Zurück zum Spiel", "Einstellungen", "Tastenbelegung", "Speichern und beenden" };
        private const int Speichern = 3;

        public static SpielmenueUi Instance { get; private set; }

        /// <summary>Tests: wird statt des echten Beendens aufgerufen.</summary>
        public static System.Action BeendenFuerTests;

        public bool IsOpen => _open;
        public string Status => _status != null ? _status.text : "";
        /// <summary>Tests: läuft der Abmelde-Countdown im Menü?</summary>
        public bool Countdown => _open && _phase == Phase.Wartet;

        private Canvas _canvas;
        private RectTransform _panel;
        private TextMeshProUGUI _status;
        private readonly Knopf[] _knoepfe = new Knopf[Eintraege.Length];
        private bool _open, _built;
        private int _auswahl;
        private Phase _phase;
        private float _bis;

        // ================================================================== Aufbau

        public void Build(Transform parent)
        {
            Instance = this;
            _canvas = UiKit.CreateCanvas("Spielmenü", parent, 35);   // über HUD (10) und Karte (30), unter Einstellungen (40) und Editor (90)
            var root = (RectTransform)_canvas.transform;
            UiKit.AddImage(UiKit.Stretch("Abdunkeln", root), new Color(0f, 0f, 0f, 0.5f));

            const float w = 440f, h = 470f;
            RectTransform inner = UiKit.Panel("Menü", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h));
            _panel = (RectTransform)inner.parent;
            TextMeshProUGUI titel = UiKit.CreateText("Titel", _panel, "ASHVEIL", 36f, Gold, TextAlignmentOptions.Center);
            Oben(titel.rectTransform, -18f, new Vector2(w - 40f, 44f));
            TextMeshProUGUI unter = UiKit.CreateText("Untertitel", _panel, "Spielmenü", 16f, UiKit.MutedText, TextAlignmentOptions.Center);
            Oben(unter.rectTransform, -62f, new Vector2(w - 40f, 22f));

            for (int i = 0; i < Eintraege.Length; i++)
            {
                RectTransform rt = UiKit.CreateRect("Knopf " + Eintraege[i], _panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -104f - i * 66f), new Vector2(w - 80f, 54f));
                UiKit.AddImage(rt, UiKit.BorderColor);
                Image back = UiKit.AddImage(UiKit.Stretch("Inner", rt, 1.5f), ButtonColor);
                TextMeshProUGUI label = UiKit.CreateText("Label", rt, Eintraege[i], 21f, UiKit.TextColor, TextAlignmentOptions.Center);
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
                _knoepfe[i] = new Knopf { Rt = rt, Back = back, Label = label };
            }

            _status = UiKit.CreateText("Status", _panel, "", 16f, UiKit.MutedText, TextAlignmentOptions.Center);
            _status.richText = true;
            _status.textWrappingMode = TextWrappingModes.Normal;
            Oben(_status.rectTransform, -372f, new Vector2(w - 40f, 50f));
            TextMeshProUGUI hinweis = UiKit.CreateText("Hinweis", _panel, "Esc zurück · W/S oder Pfeiltasten wählen · Enter bestätigen", 13f, UiKit.MutedText, TextAlignmentOptions.Center);
            Oben(hinweis.rectTransform, -432f, new Vector2(w - 40f, 20f));

            _canvas.gameObject.SetActive(false);
            UiFensterstapel.Anmelden(this, "Spielmenü", () => _open, Schliessen);
            UiFensterstapel.MenueOeffnen = Oeffnen;
            SitzungClient.Gespeichert += OnGespeichert;
            SitzungClient.AbmeldenWartet += OnAbmeldenWartet;
            SitzungClient.AbmeldenAbgebrochen += OnAbmeldenAbgebrochen;
            _built = true;

            if (HatArgument(ProbeArgument)) StartCoroutine(Probe());
        }

        private void OnDestroy()
        {
            SitzungClient.Gespeichert -= OnGespeichert;
            SitzungClient.AbmeldenWartet -= OnAbmeldenWartet;
            SitzungClient.AbmeldenAbgebrochen -= OnAbmeldenAbgebrochen;
            UiFensterstapel.Abmelden(this);
            if (UiFensterstapel.MenueOeffnen == (System.Action)Oeffnen) UiFensterstapel.MenueOeffnen = null;
            if (_open) Freigeben();
            if (Instance == this) Instance = null;
        }

        // ================================================================== Öffnen / Schließen

        public void Oeffnen()
        {
            if (!_built || _open) return;
            if (UiPointer.KeyboardCaptured || UiPointer.GameBlocked) return;   // Charakter-Editor, Fotomodus, Tippen
            _open = true;
            _auswahl = 0;
            _phase = Phase.Normal;
            SetzeStatus("");
            _knoepfe[Speichern].Label.text = Eintraege[Speichern];
            _canvas.gameObject.SetActive(true);
            UiPointer.GameBlocked = true;
            UiPointer.ModalOpen = true;
        }

        /// <summary>Menü schließen. Ein angestoßenes „Speichern und beenden“ wird damit abgebrochen (gespeichert ist trotzdem).</summary>
        public void Schliessen()
        {
            if (!_open) return;
            // 22.10.2026 (SPEICHERN-02): auch während „Speichere …“ und nach „Keine Antwort“ – sonst blieb SitzungClient.Wartet stehen
            // und der nächste Klick auf „Speichern und beenden“ beendete sofort ohne Bitte an den Server (Countdown umgangen).
            // Ein leerer Abbruch ist am Server harmlos, eine spätere Antwort wird bei geschlossenem Menü ignoriert.
            if (_phase == Phase.Wartet || _phase == Phase.Speichert || _phase == Phase.KeineAntwort) SitzungClient.AbmeldenAbbrechen();
            _open = false;
            _phase = Phase.Normal;
            if (_knoepfe[Speichern] != null) _knoepfe[Speichern].Label.text = Eintraege[Speichern];
            if (_canvas != null) _canvas.gameObject.SetActive(false);
            Freigeben();
        }

        private static void Freigeben()
        {
            UiPointer.GameBlocked = false;
            UiPointer.ModalOpen = false;
        }

        // ================================================================== Takt

        private void Update()
        {
            UiFensterstapel.Takt();   // Esc-Regel – vor den Fenstern, damit jeder Druck genau einmal wirkt
            if (!_built || !_open) return;
            UiPointer.GameBlocked = true;
            UiPointer.ModalOpen = true;
            UiPointer.OverUi = true;   // das ganze Bild gehört dem Menü – kein Zielen, kein Kameradrehen

            // Einstellungen oder Tastenbelegung liegen darüber: Menütafel ausblenden, bis sie wieder zu sind
            bool darueber = (SettingsUi.Instance != null && SettingsUi.Instance.IsOpen) || (KeybindUi.Instance != null && KeybindUi.Instance.IsOpen);
            if (_panel.gameObject.activeSelf == darueber) _panel.gameObject.SetActive(!darueber);

            float jetzt = Time.unscaledTime;
            if (_phase == Phase.Speichert && jetzt > _bis)
            {
                _phase = Phase.KeineAntwort;
                SetzeStatus("<color=#e0a050>Keine Antwort vom Server.</color> Beim Beenden speichert der Server deinen Stand beim Abmelden.");
                _knoepfe[Speichern].Label.text = "Trotzdem beenden";
            }
            if (_phase == Phase.Gespeichert && jetzt > _bis) { Beenden(); return; }
            if (_phase == Phase.Wartet)
            {
                int rest = Mathf.Max(0, Mathf.CeilToInt(_bis - jetzt));
                SetzeStatus(rest > 0 ? $"Abmelden in <b>{rest} s</b> – du bist außerhalb eines sicheren Orts.\nNicht bewegen, nicht kämpfen. Esc bricht ab."
                                     : "Speichere …");
                if (jetzt > _bis + AntwortWartezeit)
                {
                    _phase = Phase.KeineAntwort;
                    SetzeStatus("<color=#e0a050>Keine Antwort vom Server.</color> Beim Beenden speichert der Server deinen Stand beim Abmelden.");
                    _knoepfe[Speichern].Label.text = "Trotzdem beenden";
                }
            }
            if (darueber) return;

            int n = _knoepfe.Length;
            if (RawInput.WasPressed(GameKey.Forward)) _auswahl = (_auswahl + n - 1) % n;
            if (RawInput.WasPressed(GameKey.Backward)) _auswahl = (_auswahl + 1) % n;
            Vector2 maus = RawInput.MousePosition;
            for (int i = 0; i < n; i++) if (Ueber(_knoepfe[i].Rt, maus) && RawInput.MouseDelta.sqrMagnitude > 0f) _auswahl = i;
            for (int i = 0; i < n; i++)
            {
                bool aktiv = i == _auswahl;
                _knoepfe[i].Back.color = aktiv ? (Ueber(_knoepfe[i].Rt, maus) ? ButtonActive : ButtonHover) : ButtonColor;
            }

            if (RawInput.EnterPressed) { Ausfuehren(_auswahl); return; }
            if (!RawInput.LeftMousePressed) return;
            for (int i = 0; i < n; i++) if (Ueber(_knoepfe[i].Rt, maus)) { _auswahl = i; Ausfuehren(i); return; }
        }

        private void Ausfuehren(int i)
        {
            switch (i)
            {
                case 0: Schliessen(); break;
                case 1: if (SettingsUi.Instance != null) SettingsUi.Instance.SetOpen(true); break;
                case 2: if (KeybindUi.Instance != null) KeybindUi.Instance.SetOpen(true); break;
                case Speichern: SpeichernUndBeenden(); break;
            }
        }

        /// <summary>„Speichern und beenden“ (auch für die Probe im fertigen Spiel).</summary>
        public void SpeichernUndBeenden()
        {
            if (!_open) Oeffnen();
            switch (_phase)
            {
                case Phase.Speichert:
                case Phase.Wartet:
                case Phase.Gespeichert:
                    return;
                case Phase.KeineAntwort:
                    Beenden();
                    return;
            }
            if (!SitzungClient.SpeichernAnfragen())
            {
                // Kein eigener Charakter am Server (noch nicht angemeldet): es gibt nichts zu speichern
                Debug.Log("[Ashveil] Speichern und beenden: kein Charakter verbunden – das Spiel endet ohne Speichern.");
                Beenden();
                return;
            }
            _phase = Phase.Speichert;
            _bis = Time.unscaledTime + AntwortWartezeit;
            SetzeStatus("Speichere …");
            Debug.Log("[Ashveil] Speichern und beenden: Bitte an den Server.");
        }

        private void OnAbmeldenWartet(float sekunden)
        {
            if (!_open || _phase != Phase.Speichert) return;
            _phase = Phase.Wartet;
            _bis = Time.unscaledTime + sekunden;
            _knoepfe[Speichern].Label.text = "Abmelden …";
            Debug.Log($"[Ashveil] Abmelden: außerhalb eines sicheren Orts – {sekunden:0} s Countdown.");
        }

        private void OnAbmeldenAbgebrochen(string grund)
        {
            // 22.10.2026 (SPEICHERN-02): „zu schnell“ beantwortet eine neue Bitte – das Menü steht dann noch auf „Speichere …“
            // (vorher kam keine Antwort, nach 6 s hieß es „Keine Antwort vom Server“)
            bool zuSchnell = grund == SitzungClient.ZuSchnell;
            if (!_open || !(_phase == Phase.Wartet || (zuSchnell && _phase == Phase.Speichert))) return;
            _phase = Phase.Normal;
            _knoepfe[Speichern].Label.text = Eintraege[Speichern];
            SetzeStatus(grund == "Schaden" ? "<color=#e0a050>Abgebrochen:</color> Du wurdest angegriffen."
                      : grund == "Bewegung" ? "<color=#e0a050>Abgebrochen:</color> Du hast dich bewegt."
                      // 22.10.2026 (SPEICHERN-02): mit Grund statt nur „Abgebrochen.“
                      : grund == "gestorben" ? "<color=#e0a050>Abgebrochen:</color> Du bist gefallen. Abmelden geht erst nach dem Erwachen."
                      : zuSchnell ? "<color=#e0a050>Bitte einen Moment warten</color> und dann noch einmal „Speichern und beenden“."
                      : "Abmelden abgebrochen.");
        }

        private void OnGespeichert(bool ok, string zeitUtc)
        {
            if (!_open || (_phase != Phase.Speichert && _phase != Phase.Wartet)) return;   // abgebrochen (Menü geschlossen)
            if (!ok)
            {
                _phase = Phase.KeineAntwort;
                SetzeStatus("<color=#e0a050>Speichern nicht bestätigt.</color> Beim Beenden speichert der Server erneut.");
                _knoepfe[Speichern].Label.text = "Trotzdem beenden";
                return;
            }
            _phase = Phase.Gespeichert;
            _bis = Time.unscaledTime + AnzeigeVorEnde;
            string uhr = SitzungClient.Uhrzeit(zeitUtc);
            SetzeStatus((uhr.Length > 0 ? $"Gespeichert um {uhr} Uhr." : "Gespeichert.") + " ASHVEIL wird beendet …");
        }

        private void Beenden()
        {
            _phase = Phase.Normal;
            Debug.Log("[Ashveil] Speichern und beenden: Das Spiel wird beendet.");
            if (BeendenFuerTests != null)
            {
                BeendenFuerTests();
                Schliessen();
                return;
            }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#else
            Application.Quit();
#endif
        }

        private void SetzeStatus(string text)
        {
            if (_status != null) _status.text = text;
        }

        // ================================================================== Probe im fertigen Spiel

        /// <summary>
        /// Kurztest des Spieltest-Builds (spieltest_kurztest, Lauf 3): Nach dem Laden prüft das Spiel selbst die Esc-Regel mit
        /// echten Fenstern (Fähigkeiten, dann Rucksack: Esc schließt erst den Rucksack, dann die Fähigkeiten, dann kommt das
        /// Menü) und wählt danach „Speichern und beenden“. Ergebnis im Log: „[Ashveil] Menü-Probe: …“.
        /// </summary>
        public const string ProbeArgument = "-ashveil-menue-probe";

        private IEnumerator Probe()
        {
            float bis = Time.unscaledTime + 60f;
            while ((InventoryClient.Current == null || !InventoryClient.Current.Ready) && Time.unscaledTime < bis) yield return null;
            yield return new WaitForSecondsRealtime(5f);
            var log = new StringBuilder();
            int fehler = 0;
            void Pruefe(bool ok, string text)
            {
                if (!ok) fehler++;
                log.Append(ok ? " ok: " : " FEHLER: ").Append(text).Append(';');
                Debug.Log($"[Ashveil] Menü-Probe: {(ok ? "ok" : "FEHLER")} – {text}");
            }

            Pruefe(InventoryClient.Current != null && InventoryClient.Current.Ready, "Charakter geladen");
            Pruefe(SpellbookUi.Instance != null, "Fähigkeitenbuch vorhanden");
            if (SpellbookUi.Instance != null) SpellbookUi.Instance.SetOpen(true);
            yield return null;
            yield return null;
            InventoryUi.RequestBag = true;
            for (int i = 0; i < 3; i++) yield return null;
            Pruefe(UiFensterstapel.Oberstes == "Rucksack", $"oben liegt der Rucksack ({UiFensterstapel.Oberstes}, offen {UiFensterstapel.OffeneFenster})");

            UiFensterstapel.EscAusloesen();
            yield return null;
            yield return null;
            Pruefe(UiFensterstapel.LetzteWirkung == EscWirkung.FensterSchliessen && UiFensterstapel.LetztesFenster == "Rucksack" &&
                   UiFensterstapel.Oberstes == "Fähigkeiten", $"Esc 1 schließt nur den Rucksack (jetzt oben: {UiFensterstapel.Oberstes})");

            UiFensterstapel.EscAusloesen();
            yield return null;
            yield return null;
            Pruefe(UiFensterstapel.LetztesFenster == "Fähigkeiten" && UiFensterstapel.OffeneFenster == 0 && !_open,
                $"Esc 2 schließt die Fähigkeiten, kein Fenster mehr offen ({UiFensterstapel.OffeneFenster})");

            Pruefe(!UiFensterstapel.HatZiel, "kein Ziel gewählt");
            UiFensterstapel.EscAusloesen();
            yield return null;
            yield return null;
            Pruefe(_open && UiFensterstapel.LetzteWirkung == EscWirkung.MenueOeffnen && UiPointer.GameBlocked && UiPointer.ModalOpen,
                "Esc 3 öffnet das Spielmenü, die Figur ruht");

            UiFensterstapel.EscAusloesen();
            yield return null;
            yield return null;
            Pruefe(!_open && !UiPointer.GameBlocked && !UiPointer.ModalOpen, "Esc 4 schließt das Spielmenü, die Figur ist wieder frei");

            Debug.Log(fehler == 0 ? "[Ashveil] Menü-Probe: bestanden" : $"[Ashveil] Menü-Probe: {fehler} Fehler –{log}");
            Oeffnen();
            yield return null;
            SpeichernUndBeenden();
        }

        // ================================================================== Hilfen

        private static void Oben(RectTransform rt, float y, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = size;
        }

        private static bool HatArgument(string name)
        {
            foreach (string a in System.Environment.GetCommandLineArgs())
                if (string.Equals(a, name, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool Ueber(RectTransform rt, Vector2 screen) =>
            rt != null && rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);
    }
}
