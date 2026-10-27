using System;
using System.Collections.Generic;
using System.Text;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Visuals;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ashveil.UI
{
    /// <summary>
    /// M7 Charakter-Editor: Vollbild, oeffnet sich, wenn der Server meldet "dieses Profil hat noch keinen Charakter".
    ///   Links: Kategorien (Geschlecht, Gesicht, Hautfarbe, Frisur, Haarfarbe, Augen, Bart, Merkmal, Name)
    ///   Mitte: grosse 3D-Vorschau (Ziehen = drehen, Mausrad/Knopf = Gesicht), dezente Ruhe-Animation
    ///   Rechts: Varianten der Kategorie (Knoepfe, Farbfelder)
    ///   Unten: Zurueck · Zufaellig · Name · Charakter erstellen
    /// Alles ist nur eine Bitte: der Server prueft Name (Regeln, schon vergeben?) und Aussehen (nur vorhandene
    /// Optionen) und legt den Charakter erst dann an. Neue Optionen kommen allein ueber den Aussehen-Katalog.
    ///
    /// Namensfeld seit dem Spieltest r2 (UI-204, Max 09.10.2026: „meinen Namen am Charakter speichern“ ging nicht):
    /// Das Feld beginnt leer. Tippen geht sofort, ohne Klick – der Editor hat keine anderen Tasten. Der Würfel schlägt einen
    /// Namen vor und markiert ihn; der erste Tastendruck ersetzt ihn. Rücktaste lässt sich halten, Strg+A markiert alles,
    /// Strg+V fügt ein (nur Buchstaben). Die Namensregeln stehen sofort unter dem Feld; ob der Name frei ist, sagt der Server.
    /// </summary>
    // EINSTIEG-01 (21.10.): vor SocialUi (−480), sonst leert deren HandleTyping den gemeinsamen Zeichenpuffer
    // (RawInput.ConsumeTypedText), und das Namensfeld bekam von der echten Tastatur nie einen Buchstaben.
    // Weiter nach InventoryUi/SpielmenueUi/QuestUi (−500/−499/−490), damit UiPointer.OverUi stehen bleibt.
    [DefaultExecutionOrder(-485)]
    public sealed class CharacterCreatorUi : MonoBehaviour
    {
        /// <summary>Werte bleiben stabil (Tests adressieren sie als Zahl); das Volk kam spaeter hinzu und steht in der Anzeige oben.</summary>
        public enum Category { Body, Face, Skin, Hair, HairColor, Eyes, Beard, Feature, Name, Race, Class }

        private static readonly string[] CategoryNames =
            { "Geschlecht", "Gesicht", "Hautfarbe", "Frisur", "Haarfarbe", "Augen", "Bart", "Gesichtsmerkmal", "Name", "Volk", "Klasse" };

        /// <summary>Reihenfolge links (Volk zuerst).</summary>
        private static readonly Category[] Order =
            { Category.Race, Category.Class, Category.Body, Category.Face, Category.Skin, Category.Hair, Category.HairColor, Category.Eyes, Category.Beard, Category.Feature, Category.Name };

        private sealed class Btn
        {
            public RectTransform Rt;
            public Image Back;
            public Image Swatch;
            public TextMeshProUGUI Label;
            public TextMeshProUGUI Sub;
            public int Value;
        }

        private static readonly Color Backdrop = new Color(0.045f, 0.04f, 0.045f, 1f);
        private static readonly Color ButtonColor = new Color(0.2f, 0.155f, 0.1f, 0.95f);
        private static readonly Color ButtonHover = new Color(0.34f, 0.25f, 0.15f, 0.98f);
        private static readonly Color ButtonActive = new Color(0.58f, 0.39f, 0.15f, 1f);
        private static readonly Color Accent = new Color(0.72f, 0.36f, 0.12f, 1f);
        private static readonly Color AccentHover = new Color(0.86f, 0.45f, 0.16f, 1f);
        private const string Gold = "#e8c98a";

        public static CharacterCreatorUi Instance { get; private set; }
        public bool IsOpen => _open;
        public Category Current => _cat;
        public string NameText => _name.Text;
        /// <summary>Der Name ist ein markierter Vorschlag des Würfels (Tests).</summary>
        public bool NameIsSuggestion => _name.Markiert;
        public string Message => _messageText;
        public CharacterAppearanceData Look => _look.Clone();

        private Canvas _canvas;
        private RectTransform _root, _previewRt, _optionsRt, _catRt, _nameFieldRt;
        private RawImage _preview;
        private TextMeshProUGUI _optionsTitle, _optionsHint, _nameField, _message, _nameRules, _zoomLabel;
        private Image _nameFrame;
        private readonly List<Btn> _catButtons = new List<Btn>();
        private readonly List<Btn> _options = new List<Btn>();
        private Btn _back, _random, _create, _zoomBtn, _rotL, _rotR, _dice;

        private bool _open;
        private Category _cat = Category.Body;
        private CharacterAppearanceData _look = new CharacterAppearanceData();
        /// <summary>Gewaehlte Klasse: Index in <see cref="CharacterClassDefinition.SelectableIds"/>.</summary>
        private int _classIndex;

        /// <summary>Id der gewaehlten Klasse (leer, falls die Klassen nicht geladen sind).</summary>
        public string ClassId => SelectedClass != null ? SelectedClass.Id : "";

        private CharacterClassDefinition SelectedClass =>
            _classIndex >= 0 && _classIndex < CharacterClassDefinition.SelectableIds.Length ? GameData.Class(CharacterClassDefinition.SelectableIds[_classIndex]) : null;
        /// <summary>Namensfeld (UI-204): nur Buchstaben, höchstens <see cref="CharacterNameRules.MaxLength"/>.</summary>
        private readonly TextEingabe _name = new TextEingabe(CharacterNameRules.MaxLength, char.IsLetter);
        /// <summary>Das Namensfeld hat den Fokus (Schreibmarke, Enter erstellt). Getippt wird immer ins Feld.</summary>
        private bool _typing;
        private TastenWiederholung _backspace = new TastenWiederholung(0.45f, 0.05f);
        private string _shownName;
        private bool _dirty = true, _optionsDirty = true;
        private bool _drag;
        private float _dragX;
        private string _messageText = "";
        private ItemStack[] _starterGear = new ItemStack[InventoryModel.EquipCount];
        private readonly System.Random _rng = new System.Random();

        // ================================================================== Aufbau

        public void Build(Transform parent)
        {
            Instance = this;
            Debug.Log("[Ashveil] Charakter-Editor: wählbare Völker " + RaceCatalog.PlayableIds());
            _canvas = UiKit.CreateCanvas("Charakter-Editor", parent, 90);
            _root = (RectTransform)_canvas.transform;
            UiKit.AddImage(UiKit.Stretch("Hintergrund", _root), Backdrop);
            // Glut vom Boden: zwei breite, weiche Baender ueber die ganze Breite
            for (int i = 0; i < 3; i++)
            {
                RectTransform band = UiKit.CreateRect("Glut " + i, _root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(4000f, 90f + i * 110f));
                UiKit.AddImage(band, new Color(0.4f, 0.13f, 0.04f, 0.07f));
            }

            TextMeshProUGUI title = UiKit.CreateText("Titel", _root, "Erschaffe deinen Charakter", 38f, UiKit.TextColor, TextAlignmentOptions.Center);
            Anchored(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(900f, 48f));
            TextMeshProUGUI sub = UiKit.CreateText("Untertitel", _root, "Asche liegt über Kareth. Wer wirst du sein, wenn du aus dem Rauch trittst?", 18f, UiKit.MutedText, TextAlignmentOptions.Center);
            Anchored(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(1100f, 26f));

            // Kategorien links
            const float step = 58f;
            RectTransform catInner = UiKit.Panel("Kategorien", _root, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(40f, 20f), new Vector2(300f, Order.Length * step + 16f));
            _catRt = (RectTransform)catInner.parent;
            for (int i = 0; i < Order.Length; i++)
            {
                Btn b = MakeButton(_catRt, CategoryNames[(int)Order[i]], new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -10f - i * step), new Vector2(280f, step - 5f), 19f, true);
                b.Value = (int)Order[i];
                b.Label.alignment = TextAlignmentOptions.TopLeft;
                b.Label.rectTransform.offsetMin = new Vector2(14f, 0f);
                b.Label.rectTransform.offsetMax = new Vector2(-8f, -6f);
                _catButtons.Add(b);
            }

            // Vorschau Mitte
            _previewRt = UiKit.CreateRect("Vorschau", _root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-70f, 30f), new Vector2(900f, 655f));
            UiKit.AddImage(_previewRt, new Color(0f, 0f, 0f, 0.28f));
            _preview = UiKit.Stretch("Bild", _previewRt).gameObject.AddComponent<RawImage>();
            _preview.raycastTarget = false;
            TextMeshProUGUI hint = UiKit.CreateText("Hinweis", _previewRt, "Ziehen: drehen · Mausrad: Gesicht", 14f, UiKit.MutedText, TextAlignmentOptions.Center);
            Anchored(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(500f, 22f));
            _rotL = MakeButton(_previewRt, "<", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(12f, 8f), new Vector2(44f, 36f), 20f);
            _rotR = MakeButton(_previewRt, ">", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-12f, 8f), new Vector2(44f, 36f), 20f);
            _zoomBtn = MakeButton(_previewRt, "Gesicht", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-12f, -12f), new Vector2(150f, 36f), 16f);
            _zoomLabel = _zoomBtn.Label;

            // Varianten rechts
            RectTransform optInner = UiKit.Panel("Varianten", _root, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-40f, 20f), new Vector2(430f, 640f));
            _optionsRt = (RectTransform)optInner.parent;
            _optionsTitle = UiKit.CreateText("Titel", _optionsRt, "", 24f, UiKit.TextColor);
            Place(_optionsTitle.rectTransform, new Vector2(18f, -12f), new Vector2(390f, 32f));
            _optionsHint = UiKit.CreateText("Hinweis", _optionsRt, "", 14f, UiKit.MutedText);
            _optionsHint.textWrappingMode = TextWrappingModes.Normal;
            _optionsHint.richText = true;
            Place(_optionsHint.rectTransform, new Vector2(18f, -548f), new Vector2(394f, 84f));

            // Namensfeld (in der Kategorie "Name" gross, unten immer sichtbar)
            _nameFieldRt = UiKit.CreateRect("Namensfeld", _root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-20f, 36f), new Vector2(380f, 52f));
            _nameFrame = UiKit.AddImage(_nameFieldRt, UiKit.BorderColor);
            UiKit.AddImage(UiKit.Stretch("Inner", _nameFieldRt, 1.5f), new Color(0.03f, 0.03f, 0.035f, 0.98f));
            _nameField = UiKit.CreateText("Text", _nameFieldRt, "", 24f, UiKit.TextColor, TextAlignmentOptions.Center);
            _nameField.richText = true;
            _nameField.rectTransform.anchorMin = Vector2.zero;
            _nameField.rectTransform.anchorMax = Vector2.one;
            _nameField.rectTransform.offsetMin = _nameField.rectTransform.offsetMax = Vector2.zero;
            _dice = MakeButton(_nameFieldRt, "Würfeln", new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(104f, 52f), 17f);
            TextMeshProUGUI nameLabel = UiKit.CreateText("Name", _nameFieldRt, "Name", 14f, UiKit.MutedText, TextAlignmentOptions.BottomLeft);
            nameLabel.rectTransform.anchorMin = nameLabel.rectTransform.anchorMax = new Vector2(0f, 1f);
            nameLabel.rectTransform.pivot = new Vector2(0f, 0f);
            nameLabel.rectTransform.anchoredPosition = new Vector2(2f, 3f);
            nameLabel.rectTransform.sizeDelta = new Vector2(200f, 20f);

            _back = MakeButton(_root, "Zurück", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-600f, 36f), new Vector2(170f, 52f), 21f);
            _random = MakeButton(_root, "Zufällig", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-400f, 36f), new Vector2(170f, 52f), 21f);
            _create = MakeButton(_root, "Charakter erstellen", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(480f, 36f), new Vector2(300f, 52f), 22f);

            _message = UiKit.CreateText("Meldung", _root, "", 17f, UiKit.MutedText, TextAlignmentOptions.Center);
            _message.richText = true;
            Anchored(_message.rectTransform, new Vector2(0.5f, 0f), new Vector2(-20f, 100f), new Vector2(1100f, 26f));
            _nameRules = UiKit.CreateText("Regeln", _root, "", 14f, UiKit.MutedText, TextAlignmentOptions.Center);
            _nameRules.richText = true;
            Anchored(_nameRules.rectTransform, new Vector2(0.5f, 0f), new Vector2(-20f, 10f), new Vector2(900f, 22f));
            _nameRules.text = RulesLine;

            _canvas.gameObject.SetActive(false);
            CharacterCreationClient.Needed += OnNeeded;
            CharacterCreationClient.Result += OnResult;
            if (CharacterCreationClient.Pending) OnNeeded(); // Meldung kam vor dem Aufbau
        }

        private void OnDestroy()
        {
            CharacterCreationClient.Needed -= OnNeeded;
            CharacterCreationClient.Result -= OnResult;
            if (_open) Close();
            if (Instance == this) Instance = null;
        }

        // ================================================================== Oeffnen / Schliessen

        private void OnNeeded()
        {
            AppearanceCatalog cat = GameDataBootstrap.Catalog;
            _look = CharacterStoreDefaults(cat);
            _classIndex = 0;
            _starterGear = StarterGear(SelectedClass);
            _name.Leeren();   // UI-204: leer beginnen – kein fremder Name, den man erst löschen muss (der Würfel schlägt vor)
            _typing = false;
            _cat = Category.Race;
            _messageText = "";
            _open = true;
            _canvas.gameObject.SetActive(true);
            UiPointer.GameBlocked = true;
            UiPointer.KeyboardCaptured = true; // Spieltasten (Chat, Fenster) ruhen, solange der Editor offen ist
            CharacterPreviewStage stage = CharacterPreviewStage.Ensure();
            stage.SetYaw(0f);
            stage.SetZoom(0f, true);
            _dirty = _optionsDirty = true;
        }

        private void Close()
        {
            _open = false;
            _typing = false;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
            UiPointer.GameBlocked = false;
            UiPointer.KeyboardCaptured = false;
            CharacterPreviewStage stage = CharacterPreviewStage.Existing;
            if (stage != null)
            {
                stage.SetZoom(0f, true);
                stage.Show(false);
            }
        }

        private void OnResult(bool ok, string message)
        {
            if (!_open) return;
            if (ok)
            {
                _messageText = $"Willkommen, <color={Gold}>{message}</color>.";
                Close();
                return;
            }
            SetMessage("<color=#e06a50>" + message + "</color>");
            if (message.Contains("Name") || message.Contains("vergeben") || message.Contains("Buchstabe") || message.Contains("reserviert"))
                SelectCategory(Category.Name);
        }

        private static CharacterAppearanceData CharacterStoreDefaults(AppearanceCatalog cat)
        {
            var d = new CharacterAppearanceData { body = 0, hairColor = 1 };
            if (cat != null) d.hair = AppearanceDefaults.FirstStyle(cat.hairstyles, cat, 0);
            d.EnsureArrays();
            return d;
        }

        /// <summary>Startausruestung der Klasse (so sieht der Charakter beim Betreten der Welt aus).</summary>
        private static ItemStack[] StarterGear(CharacterClassDefinition cls)
        {
            var model = new InventoryModel { Class = cls != null ? cls : GameDataBootstrap.DefaultClass, Level = 1 };
            if (model.Class != null)
                foreach (ItemDefinition def in model.Class.startEquipment)
                {
                    if (def == null) continue;
                    int slot = model.FirstFreeBagSlot();
                    if (model.Add(new ItemStack(def.Id, 1)) > 0 && slot >= 0) model.AutoEquip(slot);
                }
            return (ItemStack[])model.Equipment.Clone();
        }

        // ================================================================== Auswahl (auch fuer Tests)

        public void SelectCategory(Category c)
        {
            _cat = c;
            _optionsDirty = true;
            _typing = c == Category.Name;
            CharacterPreviewStage stage = CharacterPreviewStage.Existing;
            if (stage != null)
            {
                bool face = c == Category.Face || c == Category.Eyes || c == Category.Beard || c == Category.Feature || c == Category.Skin;
                if (c != Category.Name) stage.SetZoom(face ? 1f : 0f);
            }
        }

        /// <summary>Katalog-Index setzen (nur erlaubte Werte). false = gibt es fuer diesen Koerper nicht.</summary>
        public bool SetValue(Category c, int value)
        {
            AppearanceCatalog cat = GameDataBootstrap.Catalog;
            if (cat == null) return false;
            bool ok = false;
            foreach (var o in Options(c, cat)) if (o.value == value && value >= 0) { ok = true; break; }
            if (!ok) return false;
            switch (c)
            {
                case Category.Body:
                    if (_look.body == value) break;
                    _look.body = value;
                    _look.hair = AppearanceDefaults.FirstStyle(cat.hairstyles, cat, value);
                    if (!cat.StyleAllowed(cat.beards, _look.beard, value)) _look.beard = 0;
                    if (!cat.FeatureAllowed(_look.feature, value)) _look.feature = 0;
                    _look.skin = Mathf.Min(_look.skin, cat.SkinCount(value) - 1);
                    break;
                case Category.Face: _look.face = value; break;
                case Category.Skin: _look.skin = value; break;
                case Category.Hair: _look.hair = value; break;
                case Category.HairColor: _look.hairColor = value; break;
                case Category.Eyes: _look.eyeColor = value; break;
                case Category.Beard: _look.beard = value; break;
                case Category.Feature: _look.feature = value; break;
                case Category.Race: _look.race = value; break;
                case Category.Class:
                    if (value < 0) return false; // "Magier – kommt später"
                    if (_classIndex != value)
                    {
                        _classIndex = value;
                        _starterGear = StarterGear(SelectedClass);
                    }
                    break;
                default: return false;
            }
            _dirty = _optionsDirty = true;
            return true;
        }

        /// <summary>Varianten einer Kategorie fuer den aktuellen Koerper: (Name, Katalog-Index, Farbfeld).</summary>
        public List<(string name, int value, Color? swatch)> Options(Category c, AppearanceCatalog cat)
        {
            var list = new List<(string, int, Color?)>();
            if (cat == null) return list;
            int body = _look.body;
            switch (c)
            {
                case Category.Race:
                    // nur die hier waehlbaren Voelker (Spieltest r2: nur Mensch); der Wert bleibt der Index in RaceCatalog.All
                    foreach (RaceDefinition r in RaceCatalog.Playable) list.Add((r.DisplayName, RaceCatalog.IndexOf(r.id), null));
                    break;
                case Category.Class:
                    for (int i = 0; i < CharacterClassDefinition.SelectableIds.Length; i++)
                    {
                        CharacterClassDefinition cd = GameData.Class(CharacterClassDefinition.SelectableIds[i]);
                        if (cd != null) list.Add((cd.DisplayName, i, cd.classColor));
                    }
                    list.Add((CharacterClassDefinition.ComingSoonName, -1, null)); // ausgegraut, nicht waehlbar
                    break;
                case Category.Body:
                    for (int i = 0; i < cat.bodies.Count; i++) list.Add((cat.bodies[i].variant == BodyVariantMask.Female ? "Frau" : "Mann", i, null));
                    break;
                case Category.Face:
                    for (int i = 0; i < cat.faces.Count; i++) list.Add((cat.faces[i].name, i, null));
                    break;
                case Category.Skin:
                    for (int i = 0; i < cat.SkinCount(body); i++)
                        list.Add((cat.SkinName(body, i), i, cat.skinTones.Count > i ? cat.skinTones[i].swatch : (Color?)null));
                    break;
                case Category.Hair:
                    for (int i = 0; i < cat.hairstyles.Count; i++) if (cat.StyleAllowed(cat.hairstyles, i, body)) list.Add((cat.hairstyles[i].name, i, null));
                    break;
                case Category.HairColor:
                    for (int i = 0; i < cat.hairColors.Count; i++) list.Add((cat.hairColors[i].name, i, cat.hairColors[i].color * 0.62f));
                    break;
                case Category.Eyes:
                    for (int i = 0; i < cat.eyeColors.Count; i++) list.Add((cat.eyeColors[i].name, i, cat.eyeColors[i].swatch));
                    break;
                case Category.Beard:
                    for (int i = 0; i < cat.beards.Count; i++) if (cat.StyleAllowed(cat.beards, i, body)) list.Add((cat.beards[i].name, i, null));
                    break;
                case Category.Feature:
                    for (int i = 0; i < cat.features.Count; i++) if (cat.FeatureAllowed(i, body)) list.Add((cat.features[i].name, i, null));
                    break;
            }
            return list;
        }

        public int OptionCount(Category c) => Options(c, GameDataBootstrap.Catalog).Count;

        /// <summary>Wuerfeln: jede Kategorie zufaellig (passend zum Koerper), dazu ein passender Name.</summary>
        public void Randomize(int seed = -1)
        {
            AppearanceCatalog cat = GameDataBootstrap.Catalog;
            if (cat == null) return;
            var rng = seed >= 0 ? new System.Random(seed) : _rng;
            SetValue(Category.Body, Pick(Category.Body));
            foreach (Category c in new[] { Category.Face, Category.Skin, Category.Hair, Category.HairColor, Category.Eyes, Category.Beard })
                SetValue(c, Pick(c));
            // Merkmal eher selten – ein Gesicht ohne Besonderheit ist der Normalfall
            SetValue(Category.Feature, rng.NextDouble() < 0.45 ? Pick(Category.Feature) : 0);
            // Einen selbst getippten Namen lässt der Zufall stehen; sonst schlägt er einen vor (markiert, Tippen ersetzt ihn)
            if (_name.Laenge == 0 || _name.Markiert)
                _name.Setzen(CharacterNameRules.Random(cat.VariantOf(_look.body) == BodyVariantMask.Female, rng), true);
            SetMessage("Zufällig gewürfelt – alles lässt sich einzeln ändern.");
            _dirty = _optionsDirty = true;

            int Pick(Category c)
            {
                List<(string, int, Color?)> o = Options(c, cat);
                return o.Count == 0 ? 0 : o[rng.Next(o.Count)].Item2;
            }
        }

        /// <summary>Namen setzen wie getippt (Tests); nur Buchstaben bleiben.</summary>
        public void SetName(string n) => _name.Setzen(n, false);

        /// <summary>Tests: Zeichen so verarbeiten, als wären sie getippt worden (gleicher Weg wie die Tastatur).</summary>
        public void TypeText(string text)
        {
            if (!_open) return;
            _typing = true;
            _name.Tippen(text);
        }

        /// <summary>Würfel neben dem Namensfeld: Vorschlag passend zum Körper, markiert – Tippen ersetzt ihn.</summary>
        public void RollName(int seed = -1)
        {
            AppearanceCatalog c = GameDataBootstrap.Catalog;
            if (c == null) return;
            var rng = seed >= 0 ? new System.Random(seed) : _rng;
            _name.Setzen(CharacterNameRules.Random(c.VariantOf(_look.body) == BodyVariantMask.Female, rng), true);
            _typing = true;
        }

        /// <summary>"Charakter erstellen": Name vorpruefen, dann den Server fragen.</summary>
        public bool TryCreate()
        {
            string clean = CharacterNameRules.Normalize(_name.Text);
            string err = CharacterNameRules.Validate(clean);
            if (err != null)
            {
                SetMessage("<color=#e06a50>" + err + "</color>");
                SelectCategory(Category.Name);
                return false;
            }
            if (!CharacterCreationClient.Submit(clean, _look.Clone(), ClassId)) return false;
            SetMessage("Der Charakter wird erschaffen …");
            return true;
        }

        public void Back()
        {
            int i = System.Array.IndexOf(Order, _cat);
            if (i > 0) SelectCategory(Order[i - 1]);
        }

        private void SetMessage(string m)
        {
            _messageText = m;
            if (_message != null) _message.text = m;
        }

        /// <summary>Test/Bericht: aktuelle Vorschau als Bild (zoom 0 = Figur, 1 = Gesicht).</summary>
        public bool CapturePreview(string path, float zoom, float yaw)
        {
            CharacterPreviewStage stage = CharacterPreviewStage.Ensure();
            stage.SetState(_starterGear, _look);
            float oldYaw = stage.Yaw, oldZoom = stage.Zoom;
            stage.SetYaw(yaw);
            stage.SetZoom(zoom, true);
            bool ok = stage.Capture(path);
            stage.SetYaw(oldYaw);
            stage.SetZoom(oldZoom, true);
            return ok;
        }

        public string Summary()
        {
            AppearanceCatalog cat = GameDataBootstrap.Catalog;
            var sb = new StringBuilder();
            sb.Append("open=").Append(_open ? 1 : 0).Append(";cat=").Append(_cat).Append(";name=").Append(_name.Text).Append(";class=").Append(ClassId);
            sb.Append(";look=").Append(_look.Encode().Split("|y")[0]);
            if (cat != null)
                foreach (Category c in Enum.GetValues(typeof(Category)))
                    if (c != Category.Name) sb.Append(';').Append(c).Append('=').Append(OptionCount(c));
            AppearanceRig rig = CharacterPreviewStage.Existing != null ? CharacterPreviewStage.Existing.Rig : null;
            if (rig != null)
                sb.Append(";overlay=").Append(rig.OverlaySummary()).Append(";tris=").Append(rig.OverlayTriangles)
                  .Append(";eyes=").Append(rig.EyeTextureName).Append(";head=").Append(rig.HeadScale.x.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                  .Append(";hairParts=").Append(rig.HairPartCount).Append(";beardParts=").Append(rig.BeardPartCount).Append(";hair=").Append(rig.HasHair ? 1 : 0);
            return sb.ToString();
        }

        // ================================================================== Takt

        private void Update()
        {
            if (!_open) return;
            UiPointer.GameBlocked = true;
            UiPointer.KeyboardCaptured = true;
            UiPointer.OverUi = true;
            CharacterPreviewStage stage = CharacterPreviewStage.Ensure();
            stage.Show(true);
            if (_preview.texture != stage.Texture) _preview.texture = stage.Texture;
            if (_dirty)
            {
                _dirty = false;
                stage.SetState(_starterGear, _look);
            }
            if (_optionsDirty) RebuildOptions();

            Vector2 mouse = RawInput.MousePosition;
            HandleTyping();

            // Drehen / Zoom
            if (_drag)
            {
                if (RawInput.LeftMouseHeld) { stage.Rotate(-(mouse.x - _dragX) * 0.5f); _dragX = mouse.x; }
                else _drag = false;
            }
            if (Over(_previewRt, mouse))
            {
                float scroll = RawInput.ScrollSteps;
                if (scroll != 0f) stage.SetZoom(stage.Zoom + scroll * 0.25f);
            }
            if (RawInput.LeftMouseHeld && Over(_rotL.Rt, mouse)) stage.Rotate(-90f * Time.unscaledDeltaTime);
            if (RawInput.LeftMouseHeld && Over(_rotR.Rt, mouse)) stage.Rotate(90f * Time.unscaledDeltaTime);
            _zoomLabel.text = stage.Zoom > 0.5f ? "Ganze Figur" : "Gesicht";

            UpdateTexts();
            HoverAll(mouse);
            if (!RawInput.LeftMousePressed) return;

            bool waiting = CharacterCreationClient.Waiting;
            foreach (Btn b in _catButtons) if (Over(b.Rt, mouse)) { SelectCategory((Category)b.Value); return; }
            foreach (Btn b in _options)
                if (b.Rt.gameObject.activeSelf && Over(b.Rt, mouse))
                {
                    if (_cat == Category.Class && b.Value < 0) SetMessage("Der Magier kommt später – seine Magie wird noch an die Lore von Kareth angepasst.");
                    else SetValue(_cat, b.Value);
                    return;
                }
            if (Over(_zoomBtn.Rt, mouse)) { stage.SetZoom(stage.Zoom > 0.5f ? 0f : 1f); return; }
            if (Over(_rotL.Rt, mouse) || Over(_rotR.Rt, mouse)) return;
            if (Over(_nameFieldRt, mouse)) { _typing = true; return; }
            if (Over(_dice.Rt, mouse)) { RollName(); return; }
            if (Over(_back.Rt, mouse)) { Back(); return; }
            if (Over(_random.Rt, mouse)) { Randomize(); return; }
            if (Over(_create.Rt, mouse)) { if (!waiting) TryCreate(); return; }
            if (Over(_previewRt, mouse)) { _drag = true; _dragX = mouse.x; return; }
            _typing = _cat == Category.Name;
        }

        /// <summary>
        /// Tastatur im Editor (UI-204): Getippt wird immer ins Namensfeld – der Editor hat keine anderen Tasten. Jede Eingabe
        /// gibt dem Feld den Fokus. Enter erstellt den Charakter, wenn das Feld den Fokus hat.
        /// </summary>
        private void HandleTyping()
        {
            string typed = RawInput.ConsumeTypedText();
            if (CharacterCreationClient.Waiting) return;   // Antwort des Servers steht aus: Name nicht mehr ändern
            if (RawInput.SelectAllPressed) { _name.AllesMarkieren(); _typing = true; }
            if (RawInput.PastePressed) { _name.Einfuegen(GUIUtility.systemCopyBuffer); _typing = true; }
            int back = _backspace.Schritte(RawInput.BackspacePressed, RawInput.BackspaceHeld, Time.unscaledTime);
            for (int i = 0; i < back; i++) _name.Loeschen();
            if (back > 0) _typing = true;
            if (_name.Tippen(typed)) _typing = true;
            if (RawInput.EnterPressed && _typing) TryCreate();
        }

        private static string RulesLine =>
            $"Name: {CharacterNameRules.MinLength}–{CharacterNameRules.MaxLength} Buchstaben, keine Ziffern oder Leerzeichen";

        /// <summary>Regelzeile unter dem Feld: sofort sagen, was fehlt – ob der Name frei ist, prüft erst der Server.</summary>
        private string LiveRules()
        {
            if (_name.Laenge == 0) return RulesLine;
            string err = CharacterNameRules.Validate(CharacterNameRules.Normalize(_name.Text));
            if (err != null) return "<color=#e0a050>" + err + "</color>";
            return _name.Markiert
                ? "<color=#8fcf7a>Vorschlag des Würfels</color> – tippe einfach los, um ihn zu ersetzen."
                : "<color=#8fcf7a>Der Name passt zu den Regeln.</color> Ob er noch frei ist, prüft der Server beim Erstellen.";
        }

        private void UpdateTexts()
        {
            AppearanceCatalog cat = GameDataBootstrap.Catalog;
            foreach (Btn b in _catButtons)
                if (b.Sub != null) b.Sub.text = CurrentValueName((Category)b.Value, cat);
            string n = CharacterNameRules.Normalize(_name.Text);
            bool caret = _typing && !_name.Markiert && (int)(Time.unscaledTime * 2f) % 2 == 0;
            string shown = n.Length == 0
                ? (_typing ? "" : "<color=#8a8478>Name eingeben …</color>")
                : _name.Markiert ? "<mark=#b8873a70>" + n + "</mark>" : n;
            shown += caret ? "<color=#e8c98a>|</color>" : (_typing && !_name.Markiert ? "<color=#00000000>|</color>" : "");
            if (shown != _shownName)
            {
                _shownName = shown;
                _nameField.text = shown;
                _nameRules.text = LiveRules();
            }
            _nameFrame.color = _typing ? new Color(0.91f, 0.79f, 0.54f, 1f) : UiKit.BorderColor;
            if (_message.text != _messageText) _message.text = _messageText;
            _create.Label.text = CharacterCreationClient.Waiting ? "Einen Moment …" : "Charakter erstellen";
            _back.Rt.gameObject.SetActive(System.Array.IndexOf(Order, _cat) > 0);
        }

        private string CurrentValueName(Category c, AppearanceCatalog cat)
        {
            if (cat == null) return "";
            switch (c)
            {
                case Category.Race: return RaceCatalog.At(_look.race).DisplayName;
                case Category.Class: return SelectedClass != null ? SelectedClass.DisplayName : "";
                case Category.Body: return _look.body < cat.bodies.Count ? (cat.bodies[_look.body].variant == BodyVariantMask.Female ? "Frau" : "Mann") : "";
                case Category.Face: return _look.face < cat.faces.Count ? cat.faces[_look.face].name : "";
                case Category.Skin: return cat.SkinName(_look.body, _look.skin);
                case Category.Hair: return _look.hair < cat.hairstyles.Count ? cat.hairstyles[_look.hair].name : "";
                case Category.HairColor: return _look.hairColor < cat.hairColors.Count ? cat.hairColors[_look.hairColor].name : "";
                case Category.Eyes: return _look.eyeColor < cat.eyeColors.Count ? cat.eyeColors[_look.eyeColor].name : "";
                case Category.Beard: return _look.beard < cat.beards.Count ? cat.beards[_look.beard].name : "";
                case Category.Feature: return _look.feature < cat.features.Count ? cat.features[_look.feature].name : "";
                default: return CharacterNameRules.Normalize(_name.Text);
            }
        }

        private void RebuildOptions()
        {
            _optionsDirty = false;
            AppearanceCatalog cat = GameDataBootstrap.Catalog;
            _optionsTitle.text = CategoryNames[(int)_cat];
            List<(string name, int value, Color? swatch)> opts = _cat == Category.Name ? new List<(string, int, Color?)>() : Options(_cat, cat);
            bool swatches = _cat == Category.Skin || _cat == Category.HairColor || _cat == Category.Eyes || _cat == Category.Class;
            while (_options.Count < opts.Count) _options.Add(MakeButton(_optionsRt, "", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(10f, 10f), 17f));
            int current = CurrentIndex(_cat);
            for (int i = 0; i < _options.Count; i++)
            {
                Btn b = _options[i];
                bool on = i < opts.Count;
                b.Rt.gameObject.SetActive(on);
                if (!on) continue;
                b.Value = opts[i].value;
                b.Label.text = opts[i].name;
                int col = i % 2, row = i / 2;
                b.Rt.anchoredPosition = new Vector2(16f + col * 200f, -60f - row * (swatches ? 58f : 52f));
                b.Rt.sizeDelta = new Vector2(192f, swatches ? 52f : 46f);
                if (b.Swatch == null)
                {
                    RectTransform sw = UiKit.CreateRect("Farbe", b.Rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(32f, 32f));
                    b.Swatch = UiKit.AddImage(sw, Color.white);
                }
                b.Swatch.gameObject.SetActive(swatches && opts[i].swatch.HasValue);
                if (opts[i].swatch.HasValue) b.Swatch.color = opts[i].swatch.Value;
                b.Label.rectTransform.offsetMin = new Vector2(swatches ? 46f : 6f, 0f);
                b.Label.alignment = swatches ? TextAlignmentOptions.Left : TextAlignmentOptions.Center;
                b.Label.fontSize = opts[i].name.Length > 16 ? 14f : 17f;
                bool greyed = _cat == Category.Class && opts[i].value < 0;
                b.Label.color = greyed ? new Color(0.55f, 0.53f, 0.5f) : UiKit.TextColor;
                if (greyed) { b.Swatch.gameObject.SetActive(true); b.Swatch.color = new Color(0.3f, 0.29f, 0.28f); }
            }
            string hint;
            switch (_cat)
            {
                case Category.Race:
                    hint = RaceCatalog.At(_look.race).Description;
                    break;
                case Category.Class:
                    hint = ClassHint(SelectedClass);
                    break;
                case Category.Body: hint = "Mann oder Frau – Körperbau, Frisuren und Bärte passen sich an. Ausrüstung sitzt bei beiden gleich."; break;
                case Category.Face: hint = "Gesichtszüge und Kopfform. Die Vorschau zeigt das Gesicht aus der Nähe (Mausrad: zurück zur ganzen Figur)."; break;
                case Category.Beard: hint = opts.Count <= 1 ? "Für diesen Körper gibt es keine Bärte." : "Bartstoppeln sind aufgemalt, Kinn- und Schnauzbart eigene Formen."; break;
                case Category.Feature: hint = "Eine Besonderheit, die man sich merkt – oder keine."; break;
                case Category.Name:
                    hint = $"Tippe einfach los – der Name erscheint im Feld unten. <color={Gold}>Enter</color> erstellt den Charakter. " +
                           $"{CharacterNameRules.MinLength}–{CharacterNameRules.MaxLength} Buchstaben, keine Ziffern; jeden Namen gibt es nur einmal. " +
                           "Der Würfel neben dem Feld schlägt einen Namen vor – tippst du, ersetzt du ihn.";
                    break;
                default: hint = "Wähle eine Variante – die Vorschau ändert sich sofort."; break;
            }
            _optionsHint.text = hint;
        }

        private static string ClassHint(CharacterClassDefinition c)
        {
            if (c == null) return "Wähle deine Klasse.";
            var sb = new StringBuilder();
            sb.Append($"<color={Gold}>").Append(c.DisplayName).Append("</color> – ").Append(RoleText(c.roles)).Append('\n').Append(c.Description);
            sb.Append("\nRüstung: ").Append(ArmorText(c.maxArmor));
            if (c.canRoll) sb.Append(" · Ausweichrolle");
            if (c.dualWield) sb.Append(" · zwei Waffen");
            return sb.ToString();
        }

        private static string RoleText(ClassRole r)
        {
            var parts = new List<string>();
            if ((r & ClassRole.Tank) != 0) parts.Add("Tank");
            if ((r & ClassRole.Healer) != 0) parts.Add("Heiler");
            if ((r & ClassRole.Damage) != 0) parts.Add("Schaden");
            return string.Join(" / ", parts);
        }

        private static string ArmorText(ArmorType a)
        {
            switch (a)
            {
                case ArmorType.Plate: return "Platte";
                case ArmorType.Mail: return "Kette";
                case ArmorType.Leather: return "Leder";
                default: return "Stoff";
            }
        }

        private int CurrentIndex(Category c)
        {
            switch (c)
            {
                case Category.Class: return _classIndex;
                case Category.Race: return _look.race;
                case Category.Body: return _look.body;
                case Category.Face: return _look.face;
                case Category.Skin: return _look.skin;
                case Category.Hair: return _look.hair;
                case Category.HairColor: return _look.hairColor;
                case Category.Eyes: return _look.eyeColor;
                case Category.Beard: return _look.beard;
                case Category.Feature: return _look.feature;
                default: return -1;
            }
        }

        private void HoverAll(Vector2 mouse)
        {
            foreach (Btn b in _catButtons) Hover(b, mouse, b.Value == (int)_cat);
            int cur = CurrentIndex(_cat);
            foreach (Btn b in _options) if (b.Rt.gameObject.activeSelf) Hover(b, mouse, b.Value == cur);
            Hover(_back, mouse, false);
            Hover(_random, mouse, false);
            Hover(_zoomBtn, mouse, false);
            Hover(_rotL, mouse, false);
            Hover(_rotR, mouse, false);
            Hover(_dice, mouse, false);
            _create.Back.color = Over(_create.Rt, mouse) ? AccentHover : Accent;
        }

        // ================================================================== Hilfen

        private Btn MakeButton(RectTransform parent, string text, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, float fontSize, bool withSub = false)
        {
            RectTransform rt = UiKit.CreateRect("Knopf " + text, parent, anchor, pivot, pos, size);
            UiKit.AddImage(rt, UiKit.BorderColor);
            Image back = UiKit.AddImage(UiKit.Stretch("Inner", rt, 1.5f), ButtonColor);
            TextMeshProUGUI label = UiKit.CreateText("Label", rt, text, fontSize, UiKit.TextColor, TextAlignmentOptions.Center);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            var b = new Btn { Rt = rt, Back = back, Label = label };
            if (withSub)
            {
                b.Sub = UiKit.CreateText("Wert", rt, "", 14f, new Color(0.91f, 0.79f, 0.54f), TextAlignmentOptions.BottomLeft);
                b.Sub.rectTransform.anchorMin = Vector2.zero;
                b.Sub.rectTransform.anchorMax = Vector2.one;
                b.Sub.rectTransform.offsetMin = new Vector2(14f, 5f);
                b.Sub.rectTransform.offsetMax = new Vector2(-8f, 0f);
            }
            return b;
        }

        private static void Hover(Btn b, Vector2 mouse, bool active)
        {
            if (b == null || !b.Rt.gameObject.activeInHierarchy) return;
            b.Back.color = active ? ButtonActive : Over(b.Rt, mouse) ? ButtonHover : ButtonColor;
        }

        private static void Place(RectTransform rt, Vector2 topLeft, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeft;
            rt.sizeDelta = size;
        }

        private static void Anchored(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, anchor.y);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static bool Over(RectTransform rt, Vector2 screen) =>
            rt != null && rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);
    }
}
