using System.Collections.Generic;
using System.Text;
using Ashveil.Core.Settings;
using Ashveil.Data;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Social;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ashveil.UI
{
    /// <summary>
    /// Chat (unten links) und Gruppe (links unter dem Spielerrahmen).
    ///   Enter            : tippen / senden, Esc bricht ab
    ///   /s Text          : Umgebung (Rufweite)      /g Text : Gruppe
    ///   /w Name Text     : fluestern                /r Text : dem letzten Fluesternden antworten
    ///   /einladen Name, /verlassen, /entfernen Name
    /// Rechtsklick auf einen Spieler: einladen oder fluestern.
    /// </summary>
    [DefaultExecutionOrder(-480)] // nach InventoryUi/QuestUi: ergaenzt UiPointer.OverUi
    public sealed class SocialUi : MonoBehaviour
    {
        private sealed class Btn
        {
            public RectTransform Rt;
            public Image Back;
            public TextMeshProUGUI Label;
        }

        private sealed class MemberFrame
        {
            public RectTransform Rt;
            public TextMeshProUGUI Name;
            public UiKit.Bar Hp;
        }

        private static readonly Color ButtonColor = new Color(0.22f, 0.17f, 0.11f, 0.95f);
        private static readonly Color ButtonHover = new Color(0.36f, 0.27f, 0.16f, 0.98f);
        private const float FadeAfter = 25f;

        private ISocialService _svc;
        private RectTransform _canvasRt;
        private bool _built, _dirty = true;

        // Chat
        private RectTransform _chatBack;
        private TextMeshProUGUI _chatText, _inputText;
        private RectTransform _inputRt;
        private bool _typing;
        private string _spieltaste;   // EINSTIEG-02 (21.10.): zuletzt gedrückte belegte Spieltaste, solange gehalten (kein Chat-Start mit „/“)
        private readonly StringBuilder _input = new StringBuilder();
        private ChatChannel _channel = ChatChannel.Say;
        private string _whisperTarget = "";
        private string _lastWhisperFrom = "";
        private int _seenLines;
        private float _lastLineTime;

        // Gruppe
        private readonly List<MemberFrame> _frames = new List<MemberFrame>();
        private Btn _leave;

        // Einladung
        private RectTransform _inviteWin;
        private TextMeshProUGUI _inviteText;
        private Btn _inviteYes, _inviteNo;

        // Spielermenue
        private RectTransform _menu;
        private TextMeshProUGUI _menuTitle;
        private Btn _menuInvite, _menuWhisper;
        private string _menuName;

        public static SocialUi Instance { get; private set; }

        /// <summary>Tests/Werkzeuge: eine Chatzeile so ausfuehren, als haette der Spieler sie getippt ("/tanzen").</summary>
        public static bool RunLine(string line)
        {
            if (Instance == null || Instance._svc == null) return false;
            Instance.Submit(line);
            return true;
        }

        private void OnEnable()
        {
            Instance = this;
            SocialClient.Changed += MarkDirty;
            SocialClient.PlayerClicked += OnPlayerClicked;
            Ashveil.Gameplay.Characters.EmoteClient.Message += LocalSystem;
            Ashveil.Gameplay.Mastery.MasteryClient.Milestone += OnMilestone;
        }

        private void OnMilestone(string name, int rank, string effects) =>
            LocalSystem($"Meisterschaft: {name} hat Rang {rank} erreicht – {effects}.");

        private void OnDisable()
        {
            SocialClient.Changed -= MarkDirty;
            SocialClient.PlayerClicked -= OnPlayerClicked;
            Ashveil.Gameplay.Characters.EmoteClient.Message -= LocalSystem;
            Ashveil.Gameplay.Mastery.MasteryClient.Milestone -= OnMilestone;
            if (_typing) UiPointer.KeyboardCaptured = false;
        }

        private void MarkDirty() => _dirty = true;

        private void Start()
        {
            if (UiKit.Font == null)
            {
                enabled = false;
                return;
            }
            Build();
        }

        // ================================================================== Aufbau

        private void Build()
        {
            Canvas canvas = UiKit.CreateCanvas("Social Canvas", transform, 24);
            _canvasRt = (RectTransform)canvas.transform;

            _chatBack = UiKit.CreateRect("Chat", _canvasRt, Vector2.zero, Vector2.zero, new Vector2(20f, 96f), new Vector2(560f, 230f));
            UiKit.AddImage(_chatBack, new Color(0f, 0f, 0f, 0.3f));
            _chatText = UiKit.CreateText("Lines", _chatBack, "", 16f, UiKit.TextColor, TextAlignmentOptions.BottomLeft);
            _chatText.richText = true;
            _chatText.textWrappingMode = TextWrappingModes.Normal;
            _chatText.overflowMode = TextOverflowModes.Truncate;
            RectTransform ct = _chatText.rectTransform;
            ct.anchorMin = Vector2.zero;
            ct.anchorMax = Vector2.one;
            ct.offsetMin = new Vector2(8f, 6f);
            ct.offsetMax = new Vector2(-8f, -6f);

            _inputRt = UiKit.CreateRect("Input", _canvasRt, Vector2.zero, Vector2.zero, new Vector2(20f, 62f), new Vector2(560f, 30f));
            UiKit.AddImage(_inputRt, new Color(0.05f, 0.05f, 0.07f, 0.9f));
            _inputText = UiKit.CreateText("Text", _inputRt, "", 16f, UiKit.TextColor, TextAlignmentOptions.Left);
            _inputText.richText = true;
            RectTransform it = _inputText.rectTransform;
            it.anchorMin = Vector2.zero;
            it.anchorMax = Vector2.one;
            it.offsetMin = new Vector2(8f, 0f);
            it.offsetMax = new Vector2(-8f, 0f);
            _inputRt.gameObject.SetActive(false);

            for (int i = 0; i < 5; i++)
            {
                RectTransform inner = UiKit.Panel("Member " + i, _canvasRt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -118f - i * 50f), new Vector2(240f, 44f));
                var border = (RectTransform)inner.parent;
                TextMeshProUGUI n = UiKit.CreateText("Name", border, "", 15f, UiKit.TextColor);
                n.richText = true;
                RectTransform nr = n.rectTransform;
                nr.anchorMin = nr.anchorMax = new Vector2(0f, 1f);
                nr.pivot = new Vector2(0f, 1f);
                nr.anchoredPosition = new Vector2(8f, -3f);
                nr.sizeDelta = new Vector2(224f, 20f);
                UiKit.Bar hp = UiKit.CreateBar("Hp", border, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, -24f), new Vector2(224f, 13f), UiKit.HealthColor, 10f);
                border.gameObject.SetActive(false);
                _frames.Add(new MemberFrame { Rt = border, Name = n, Hp = hp });
            }
            _leave = MakeButton(_canvasRt, "Gruppe verlassen", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -118f), new Vector2(150f, 26f));
            _leave.Rt.gameObject.SetActive(false);

            RectTransform invInner = UiKit.Panel("Invite", _canvasRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(420f, 100f));
            _inviteWin = (RectTransform)invInner.parent;
            _inviteText = UiKit.CreateText("Text", _inviteWin, "", 18f, UiKit.TextColor, TextAlignmentOptions.Center);
            RectTransform ir = _inviteText.rectTransform;
            ir.anchorMin = ir.anchorMax = new Vector2(0.5f, 1f);
            ir.pivot = new Vector2(0.5f, 1f);
            ir.anchoredPosition = new Vector2(0f, -12f);
            ir.sizeDelta = new Vector2(400f, 30f);
            _inviteYes = MakeButton(_inviteWin, "Annehmen", new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(-6f, 12f), new Vector2(140f, 32f));
            _inviteNo = MakeButton(_inviteWin, "Ablehnen", new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(6f, 12f), new Vector2(140f, 32f));
            _inviteWin.gameObject.SetActive(false);

            RectTransform menuInner = UiKit.Panel("Player Menu", _canvasRt, new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), Vector2.zero, new Vector2(190f, 104f));
            _menu = (RectTransform)menuInner.parent;
            _menuTitle = UiKit.CreateText("Title", _menu, "", 16f, new Color(0.62f, 0.8f, 1f), TextAlignmentOptions.Center);
            RectTransform mt = _menuTitle.rectTransform;
            mt.anchorMin = mt.anchorMax = new Vector2(0.5f, 1f);
            mt.pivot = new Vector2(0.5f, 1f);
            mt.anchoredPosition = new Vector2(0f, -6f);
            mt.sizeDelta = new Vector2(180f, 22f);
            _menuInvite = MakeButton(_menu, "In Gruppe einladen", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -32f), new Vector2(170f, 28f));
            _menuWhisper = MakeButton(_menu, "Flüstern", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -66f), new Vector2(170f, 28f));
            _menu.gameObject.SetActive(false);
            UiFensterstapel.Anmelden(this, "Spielermenü", () => _menu != null && _menu.gameObject.activeSelf, () => SetActive(_menu, false));

            _built = true;
        }

        private Btn MakeButton(RectTransform parent, string text, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            RectTransform rt = UiKit.CreateRect("Button " + text, parent, anchor, pivot, pos, size);
            UiKit.AddImage(rt, UiKit.BorderColor);
            Image back = UiKit.AddImage(UiKit.Stretch("Inner", rt, 1.5f), ButtonColor);
            TextMeshProUGUI label = UiKit.CreateText("Label", rt, text, 15f, UiKit.TextColor, TextAlignmentOptions.Center);
            RectTransform lr = label.rectTransform;
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = lr.offsetMax = Vector2.zero;
            return new Btn { Rt = rt, Back = back, Label = label };
        }

        // ================================================================== Takt

        private void Update()
        {
            if (!_built) return;
            if (!ReferenceEquals(_svc, SocialClient.Current))
            {
                _svc = SocialClient.Current;
                _seenLines = 0;
                _dirty = true;
            }
            bool ready = _svc != null && _svc.Ready;
            if (!ready)
            {
                SetActive(_chatBack, false);
                SetActive(_inviteWin, false);
                SetActive(_menu, false);
                foreach (MemberFrame f in _frames) SetActive(f.Rt, false);
                SetActive(_leave.Rt, false);
                StopTyping();
                return;
            }

            HandleTyping();
            if (_dirty) Refresh();
            _dirty = false;

            // Alte Zeilen verblassen, solange niemand tippt
            float idle = Time.unscaledTime - _lastLineTime;
            float alpha = _typing ? 1f : Mathf.Clamp01(1f - (idle - FadeAfter) / 4f);
            _chatText.alpha = alpha;
            SetActive(_chatBack, true);
            _chatBack.GetComponent<Image>().color = new Color(0f, 0f, 0f, _typing ? 0.45f : 0.3f * alpha);

            Vector2 mouse = RawInput.MousePosition;
            bool over = (_typing && Over(_chatBack, mouse)) || Over(_inviteWin, mouse) || Over(_menu, mouse) || Over(_leave.Rt, mouse);
            foreach (MemberFrame f in _frames) over |= Over(f.Rt, mouse);
            if (over) UiPointer.OverUi = true;
            if (!UiPointer.ModalOpen) HandleMouse(mouse);   // Spielmenü offen (UI-204): keine Klicks unter dem Menü
        }

        private static void SetActive(RectTransform rt, bool on)
        {
            if (rt != null && rt.gameObject.activeSelf != on) rt.gameObject.SetActive(on);
        }

        private static bool Over(RectTransform rt, Vector2 screen) =>
            rt != null && rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);

        // ================================================================== Tippen

        private void HandleTyping()
        {
            if (!_typing)
            {
                // Tastendruecke im Spiel nicht spaeter in den Chat schreiben; "/" je nach Tastatur (DE: Umschalt+7) ueber den Text erkennen
                string gameTyped = RawInput.ConsumeTypedText();
                SpieltasteMerken();
                if (!UiPointer.HotkeysAllowed) return; // ein anderes Feld tippt gerade, oder das Spielmenü ist offen
                if (RawInput.EnterPressed) StartTyping("");
                else if (gameTyped.IndexOf('/') >= 0 && _spieltaste == null) StartTyping("/");
                return;
            }
            if (RawInput.EscapePressed) { StopTyping(); return; }
            if (RawInput.BackspacePressed && _input.Length > 0) _input.Length--;
            string typed = RawInput.ConsumeTypedText();
            if (RawInput.PastePressed)
            {
                typed += SocialClient.Clean(GUIUtility.systemCopyBuffer);
            }
            if (typed.Length > 0 && _input.Length < 200) _input.Append(typed);
            if (_input.Length > 200) _input.Length = 200;
            if (RawInput.EnterPressed)
            {
                Submit(_input.ToString());
                StopTyping();
                return;
            }
            UpdateInputText();
        }

        /// <summary>
        /// EINSTIEG-02 (21.10., Nachbesserung): Ein „/“ aus dem Textpuffer startet den Chat NICHT, wenn es von einer belegten
        /// Spieltaste kommt – die Spielaktion hat Vorrang. Beispiele: Num / (Gehen) liefert auch das Zeichen „/“; auf der deutschen
        /// Tastatur ist Umschalt+7 = „/“, beim Sprinten (Umschalt gehalten) soll Leistenplatz 7 auslösen statt den Chat zu öffnen.
        /// Belegte Spieltasten = Tastatur-Codes aus <see cref="InputBindingsStore.Current"/> (zwölf Leistenplätze, Wechseltaste,
        /// Schwachpunkt, drei Gangarten). Gemerkt wird die zuletzt gedrückte, solange sie gehalten wird: Windows wiederholt das
        /// Zeichen bei gehaltener Taste ohne neue Druckflanke, und das Zeichen darf auch ein Bild nach dem Tastendruck kommen.
        /// Enter öffnet den Chat wie bisher; ein „/“ ohne Spieltaste (z. B. US-Tastatur) ebenfalls.
        /// </summary>
        private void SpieltasteMerken()
        {
            string jetzt = GedrueckteSpieltaste(InputBindingsStore.Current);
            if (jetzt != null) _spieltaste = jetzt;
            else if (_spieltaste != null && !RawInput.IsHeld(_spieltaste)) _spieltaste = null;
        }

        private static string GedrueckteSpieltaste(InputBindings b)
        {
            if (b == null) return null;
            if (b.slots != null)
                foreach (string s in b.slots)
                    if (TasteGedrueckt(s)) return s;
            if (TasteGedrueckt(b.swapKey)) return b.swapKey;
            if (TasteGedrueckt(b.weakPointKey)) return b.weakPointKey;
            if (TasteGedrueckt(b.walkKey)) return b.walkKey;
            if (TasteGedrueckt(b.jogKey)) return b.jogKey;
            return TasteGedrueckt(b.epicWalkKey) ? b.epicWalkKey : null;
        }

        /// <summary>Nur Tastatur-Codes: Maustasten liefern keinen Text.</summary>
        private static bool TasteGedrueckt(string code) =>
            !string.IsNullOrEmpty(code) && code.StartsWith("Key:", System.StringComparison.Ordinal) && RawInput.WasPressed(code);

        private void StartTyping(string prefill)
        {
            _typing = true;
            _input.Clear();
            _input.Append(prefill);
            UiPointer.KeyboardCaptured = true;
            SetActive(_inputRt, true);
            RawInput.ConsumeTypedText(); // das "/" oder Enter selbst nicht doppelt
            UpdateInputText();
        }

        private void StopTyping()
        {
            if (!_typing) return;
            _typing = false;
            _input.Clear();
            UiPointer.KeyboardCaptured = false;
            SetActive(_inputRt, false);
        }

        private void UpdateInputText()
        {
            string label;
            switch (_channel)
            {
                case ChatChannel.Party: label = "<color=#8fc8ff>Gruppe:</color> "; break;
                case ChatChannel.Whisper: label = $"<color=#e89be8>An {_whisperTarget}:</color> "; break;
                default: label = "<color=#d8d2c4>Sagen:</color> "; break;
            }
            if (_input.Length > 0 && _input[0] == '/') label = "";
            _inputText.text = label + "<noparse>" + _input.ToString().Replace("</noparse>", "") + "</noparse>_";
        }

        private void Submit(string line)
        {
            line = line.Trim();
            if (line.Length == 0) return;
            if (line[0] != '/')
            {
                _svc.Say(_channel, _whisperTarget, line);
                return;
            }
            int sp = line.IndexOf(' ');
            string cmd = (sp < 0 ? line : line.Substring(0, sp)).ToLowerInvariant();
            string rest = sp < 0 ? "" : line.Substring(sp + 1).Trim();
            switch (cmd)
            {
                case "/s": case "/sagen":
                    _channel = ChatChannel.Say;
                    if (rest.Length > 0) _svc.Say(ChatChannel.Say, "", rest);
                    break;
                case "/g": case "/p": case "/gruppe":
                    _channel = ChatChannel.Party;
                    if (rest.Length > 0) _svc.Say(ChatChannel.Party, "", rest);
                    break;
                case "/w": case "/f": case "/flüstern": case "/fluestern":
                {
                    int s2 = rest.IndexOf(' ');
                    string who = s2 < 0 ? rest : rest.Substring(0, s2);
                    string text = s2 < 0 ? "" : rest.Substring(s2 + 1);
                    if (who.Length == 0) { LocalSystem("Wem? /w Name Text"); break; }
                    _channel = ChatChannel.Whisper;
                    _whisperTarget = who;
                    if (text.Length > 0) _svc.Say(ChatChannel.Whisper, who, text);
                    break;
                }
                case "/r": case "/antworten":
                    if (_lastWhisperFrom.Length == 0) { LocalSystem("Niemand hat dir gefluestert."); break; }
                    _channel = ChatChannel.Whisper;
                    _whisperTarget = _lastWhisperFrom;
                    if (rest.Length > 0) _svc.Say(ChatChannel.Whisper, _lastWhisperFrom, rest);
                    break;
                case "/einladen": case "/invite":
                    _svc.Invite(rest);
                    break;
                case "/verlassen": case "/leave":
                    _svc.LeaveParty();
                    break;
                case "/entfernen": case "/kick":
                    _svc.Kick(rest);
                    break;
                case "/emotes":
                    LocalSystem("Emotes: " + EmoteCatalog.CommandList() + " · Menü: J · Bewegung beendet");
                    break;
                case "/zeit": case "/uhr":
                {
                    float now = Ashveil.Core.WorldClock.Hours;
                    if (rest.Length == 0)
                    {
                        // Echtzeit (NET-020): kein Tageszähler mehr – der zählt ab 1. Januar 2026 und sagt Spielern nichts
                        LocalSystem($"Es ist {Ashveil.Core.WorldClock.Text} Uhr ({Ashveil.Core.WorldClock.PeriodName(now)}).");
                        break;
                    }
                    if (!Ashveil.Core.WorldClock.TryParseHour(rest, out float h)) { LocalSystem("Uhrzeit? z. B. /zeit 22 oder /zeit 6:30"); break; }
                    if (Ashveil.Core.WorldClock.SetRequest == null) { LocalSystem("Gerade nicht möglich."); break; }
                    Ashveil.Core.WorldClock.SetRequest(h);
                    break;
                }
                case "/stopp": case "/aufstehen":
                    Ashveil.Gameplay.Characters.EmoteClient.Stop();
                    break;
                default:
                    if (EmoteCatalog.TryGetByCommand(cmd, out EmoteInfo emote))
                    {
                        if (!Ashveil.Gameplay.Characters.EmoteClient.Play(emote.Id)) LocalSystem("Gerade nicht möglich.");
                        break;
                    }
                    LocalSystem("Befehle: /s, /g, /w Name, /r, /einladen Name, /verlassen, /entfernen Name, /emotes, /zeit");
                    break;
            }
        }

        private readonly List<string> _local = new List<string>();

        /// <summary>Letzte lokale Systemzeile (Tests).</summary>
        public static string LastLocal { get; private set; } = "";

        private void LocalSystem(string text)
        {
            LastLocal = text;
            _local.Add(text);
            _lastLineTime = Time.unscaledTime;
            _dirty = true;
        }

        // ================================================================== Anzeige

        private void Refresh()
        {
            // Chat: letzte Zeilen
            IReadOnlyList<ChatLine> lines = _svc.Chat;
            if (lines.Count != _seenLines)
            {
                for (int i = Mathf.Min(_seenLines, lines.Count); i < lines.Count; i++)
                    if (lines[i].Channel == ChatChannel.Whisper) _lastWhisperFrom = lines[i].From;
                _seenLines = lines.Count;
                _lastLineTime = Time.unscaledTime;
            }
            var sb = new StringBuilder();
            int start = Mathf.Max(0, lines.Count - 10);
            for (int i = start; i < lines.Count; i++)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(Format(lines[i]));
            }
            foreach (string l in _local)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("<color=#f0d060>").Append(Esc(l)).Append("</color>");
            }
            if (_local.Count > 3) _local.RemoveAt(0);
            _chatText.text = sb.ToString();

            // Gruppe
            IReadOnlyList<PartyMemberView> party = _svc.Party;
            int shown = 0;
            foreach (PartyMemberView m in party)
            {
                if (m.Self || shown >= _frames.Count) continue;
                MemberFrame f = _frames[shown];
                f.Rt.anchoredPosition = new Vector2(24f, -118f - shown * 50f);
                string col = !m.Online ? "#7a766e" : !m.Alive ? "#c05050" : "#e8e2d4";
                f.Name.text = $"<color={col}>{(m.Leader ? "* " : "")}{Esc(m.Name)}</color> <size=80%><color=#a8a296>{m.Level} {Esc(m.ClassName)}{(m.Online ? "" : " (offline)")}</color></size>";
                f.Hp.Set(m.Online ? m.Health : 0f);
                f.Hp.Label.text = m.Online ? (m.Alive ? $"{Mathf.RoundToInt(m.Health * 100f)} %" : "tot") : "";
                SetActive(f.Rt, true);
                shown++;
            }
            for (int i = shown; i < _frames.Count; i++) SetActive(_frames[i].Rt, false);
            SetActive(_leave.Rt, party.Count > 0);
            _leave.Rt.anchoredPosition = new Vector2(24f, -118f - shown * 50f);

            // Einladung
            SetActive(_inviteWin, !string.IsNullOrEmpty(_svc.PendingInvite));
            if (!string.IsNullOrEmpty(_svc.PendingInvite))
                _inviteText.text = $"<b>{Esc(_svc.PendingInvite)}</b> lädt dich in eine Gruppe ein.";
        }

        private static string Esc(string s) => string.IsNullOrEmpty(s) ? "" : "<noparse>" + s.Replace("</noparse>", "") + "</noparse>";

        private static string Format(ChatLine l)
        {
            switch (l.Channel)
            {
                case ChatChannel.Party: return $"<color=#8fc8ff>[Gruppe] {Esc(l.From)}: {Esc(l.Text)}</color>";
                case ChatChannel.Whisper: return $"<color=#e89be8>{Esc(l.From)} flüstert: {Esc(l.Text)}</color>";
                case ChatChannel.WhisperSent: return $"<color=#e89be8>An {Esc(l.From)}: {Esc(l.Text)}</color>";
                case ChatChannel.System: return $"<color=#f0d060>{Esc(l.Text)}</color>";
                default: return $"<color=#e8e2d4>{Esc(l.From)} sagt: {Esc(l.Text)}</color>";
            }
        }

        // ================================================================== Maus

        private void OnPlayerClicked(string name, Vector2 screen)
        {
            if (!_built || _svc == null) return;
            _menuName = name;
            _menuTitle.text = Esc(name);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, screen, null, out Vector2 local);
            _menu.anchoredPosition = local + new Vector2(8f, -8f);
            SetActive(_menu, true);
        }

        private void HandleMouse(Vector2 mouse)
        {
            foreach (Btn b in new[] { _leave, _inviteYes, _inviteNo, _menuInvite, _menuWhisper })
                if (b.Rt.gameObject.activeInHierarchy) b.Back.color = Over(b.Rt, mouse) ? ButtonHover : ButtonColor;
            if (!RawInput.LeftMousePressed) return;

            if (_menu.gameObject.activeSelf)
            {
                if (Over(_menuInvite.Rt, mouse)) _svc.Invite(_menuName);
                else if (Over(_menuWhisper.Rt, mouse))
                {
                    _channel = ChatChannel.Whisper;
                    _whisperTarget = _menuName;
                    StartTyping("");
                }
                SetActive(_menu, false); // jeder Klick schliesst das Menue
                return;
            }
            if (Over(_inviteYes.Rt, mouse)) { _svc.RespondInvite(true); return; }
            if (Over(_inviteNo.Rt, mouse)) { _svc.RespondInvite(false); return; }
            if (Over(_leave.Rt, mouse)) { _svc.LeaveParty(); return; }
        }
    }
}
