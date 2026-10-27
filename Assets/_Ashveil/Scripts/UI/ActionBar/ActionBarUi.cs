using Ashveil.Core.Events;
using Ashveil.Core.Settings;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ashveil.UI
{
    /// <summary>
    /// 24-Slot-Actionbar (M6.6, Feature-Update 26.09. Abschnitte 1–10): zwoelf sichtbare Plaetze der aktiven Bank,
    /// Bankanzeige A/B, je Platz Symbol, Taste, Abklingrad mit Restzeit, Anzahl/Ladungen, rot = kein/ungueltiges Ziel
    /// oder zu weit, grau = gerade nicht nutzbar. Ziehen: aus Rucksack und Faehigkeitenbuch belegen, in der Leiste
    /// verschieben/tauschen, ins Leere ziehen = Platz leeren (nur die Verknuepfung, nie der Gegenstand).
    /// Klick auf einen Platz loest ihn aus. Werte (Abklingzeit, Anzahl) kommen immer aus ihrer Quelle.
    /// </summary>
    [DefaultExecutionOrder(-460)] // nach Inventar/Quest/Sozial/Einstellungen: ergaenzt UiPointer.OverUi
    public sealed class ActionBarUi : MonoBehaviour
    {
        private sealed class SlotView
        {
            public int Slot;
            public RectTransform Root;
            public Image Border, Icon, Sweep, State, Flash;
            public TextMeshProUGUI Key, Timer, Count;
        }

        private const float SlotSize = 56f, Gap = 5f;
        private static readonly Color BankBColor = new Color(0.4f, 0.68f, 1f, 1f);
        private static readonly Color RedState = new Color(0.85f, 0.12f, 0.08f, 0.42f);
        private static readonly Color GreyState = new Color(0.08f, 0.08f, 0.08f, 0.62f);

        public static ActionBarUi Instance { get; private set; }

        private RectTransform _canvasRt, _bar, _tip;
        private TextMeshProUGUI _bank, _bankHint, _tipText;
        private Image _bankBorder, _dragIcon;
        private readonly SlotView[] _slots = new SlotView[ActionBarLayout.SlotsPerPage];
        private readonly float[] _flash = new float[ActionBarLayout.SlotsPerPage];
        private PlayerCharacter _player;
        private int _pressSlot = -1;
        private Vector2 _pressPos;
        private bool _dragging;
        private int _shownPage = -1;

        /// <summary>Welche Bank die Leiste gerade zeigt (Tests).</summary>
        public int ShownPage => _shownPage;
        public int SlotCount => _slots.Length;
        public string KeyLabel(int i) => i >= 0 && i < _slots.Length && _slots[i] != null ? _slots[i].Key.text : "";

        /// <summary>Von HudRoot aufgerufen – baut die Leiste auf dessen Zeichenflaeche.</summary>
        public void Build(RectTransform canvasRoot)
        {
            Instance = this;
            _canvasRt = canvasRoot;
            float w = ActionBarLayout.SlotsPerPage * (SlotSize + Gap) + Gap;
            RectTransform inner = UiKit.Panel("Actionbar", canvasRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(26f, 18f), new Vector2(w, SlotSize + 2 * Gap));
            _bar = (RectTransform)inner.parent;
            for (int i = 0; i < _slots.Length; i++) _slots[i] = CreateSlot(_bar, i);

            // Bankanzeige links neben der Leiste
            RectTransform bank = UiKit.CreateRect("Bank", canvasRoot, new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(26f - w * 0.5f - 6f, 18f), new Vector2(44f, SlotSize + 2 * Gap));
            _bankBorder = UiKit.AddImage(bank, UiKit.BorderColor);
            RectTransform bankIn = UiKit.Stretch("Inner", bank, 2f);
            UiKit.AddImage(bankIn, new Color(0.08f, 0.08f, 0.09f, 0.95f));
            _bank = UiKit.CreateText("Letter", bankIn, "A", 28f, UiKit.TextColor, TextAlignmentOptions.Center);
            Fill(_bank.rectTransform, 0f);
            _bankHint = UiKit.CreateText("Hint", canvasRoot, "", 12f, UiKit.MutedText, TextAlignmentOptions.Center);
            RectTransform hr = _bankHint.rectTransform;
            hr.anchorMin = hr.anchorMax = new Vector2(0.5f, 0f);
            hr.pivot = new Vector2(0.5f, 0f);
            hr.anchoredPosition = new Vector2(26f - w * 0.5f - 28f, 2f);
            hr.sizeDelta = new Vector2(120f, 16f);

            // Tooltip
            RectTransform tipInner = UiKit.Panel("Tooltip", canvasRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(340f, 64f));
            _tip = (RectTransform)tipInner.parent;
            _tipText = UiKit.CreateText("Text", tipInner, "", 15f, UiKit.TextColor);
            _tipText.richText = true;
            _tipText.textWrappingMode = TextWrappingModes.Normal;
            Fill(_tipText.rectTransform, 8f);
            _tip.gameObject.SetActive(false);

            RectTransform drag = UiKit.CreateRect("Drag", canvasRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(SlotSize, SlotSize));
            _dragIcon = UiKit.AddImage(drag, Color.white);
            _dragIcon.preserveAspect = true;
            _dragIcon.raycastTarget = false;
            drag.gameObject.SetActive(false);

            RefreshKeys();
        }

        private SlotView CreateSlot(RectTransform bar, int i)
        {
            var v = new SlotView { Slot = i };
            v.Root = UiKit.CreateRect("Platz " + (i + 1), bar, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(Gap + i * (SlotSize + Gap), 0f), new Vector2(SlotSize, SlotSize));
            v.Border = UiKit.AddImage(v.Root, UiKit.BorderColor);
            RectTransform inner = UiKit.Stretch("Inner", v.Root, 2f);
            UiKit.AddImage(inner, new Color(0.12f, 0.11f, 0.11f, 1f));
            RectTransform icon = UiKit.Stretch("Icon", inner, 3f);
            v.Icon = UiKit.AddImage(icon, Color.white);
            v.Icon.preserveAspect = true;
            RectTransform sweep = UiKit.Stretch("Abklingzeit", inner, 0f);
            v.Sweep = UiKit.AddImage(sweep, new Color(0f, 0f, 0f, 0.68f));
            v.Sweep.sprite = ItemIcons.White;
            v.Sweep.type = Image.Type.Filled;
            v.Sweep.fillMethod = Image.FillMethod.Radial360;
            v.Sweep.fillOrigin = (int)Image.Origin360.Top;
            v.Sweep.fillClockwise = false;
            v.Sweep.fillAmount = 0f;
            RectTransform state = UiKit.Stretch("Zustand", inner, 0f);
            v.State = UiKit.AddImage(state, Color.clear);
            RectTransform flash = UiKit.Stretch("Aufleuchten", inner, 0f);
            v.Flash = UiKit.AddImage(flash, new Color(1f, 0.9f, 0.6f, 0f));
            v.Key = UiKit.CreateText("Taste", inner, "", 13f, UiKit.TextColor, TextAlignmentOptions.TopLeft);
            Fill(v.Key.rectTransform, 3f);
            v.Key.outlineWidth = 0.25f;
            v.Count = UiKit.CreateText("Anzahl", inner, "", 14f, UiKit.TextColor, TextAlignmentOptions.BottomRight);
            Fill(v.Count.rectTransform, 3f);
            v.Count.outlineWidth = 0.25f;
            v.Timer = UiKit.CreateText("Restzeit", inner, "", 20f, UiKit.TextColor, TextAlignmentOptions.Center);
            Fill(v.Timer.rectTransform, 0f);
            v.Timer.outlineWidth = 0.3f;
            return v;
        }

        private static void Fill(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        private void OnEnable()
        {
            InputBindingsStore.Changed += OnBindings;
            ActionBarInput.Triggered += OnTriggered;
            UiDrag.Dropped += OnDropped;
            EventBus<LocalPlayerSpawnedEvent>.Subscribe(OnPlayerSpawned);
        }

        private void OnPlayerSpawned(LocalPlayerSpawnedEvent evt)
        {
            if (evt.Player != null) _player = evt.Player.GetComponent<PlayerCharacter>();
        }

        private void OnDisable()
        {
            InputBindingsStore.Changed -= OnBindings;
            ActionBarInput.Triggered -= OnTriggered;
            UiDrag.Dropped -= OnDropped;
            EventBus<LocalPlayerSpawnedEvent>.Unsubscribe(OnPlayerSpawned);
            if (Instance == this) Instance = null;
        }

        private void OnBindings(InputBindings b) => RefreshKeys();

        private void OnTriggered(int page, int slot)
        {
            if (slot >= 0 && slot < _flash.Length && page == ActionBarInput.ActivePage) _flash[slot] = 1f;
        }

        private void RefreshKeys()
        {
            InputBindings b = InputBindingsStore.Current;
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i] != null) _slots[i].Key.text = RawInput.Label(b.slots[i]);
            if (_bankHint != null)
                _bankHint.text = string.IsNullOrEmpty(b.swapKey) ? "" : (b.toggleMode ? "B: " : "B halten: ") + RawInput.Label(b.swapKey);
        }

        /// <summary>Etwas wurde aus einem anderen Fenster losgelassen – liegt es auf einem Platz, dort verknuepfen.</summary>
        private void OnDropped(UiDrag.Kind kind, string id, Vector2 screen)
        {
            int slot = SlotAt(screen);
            IActionBarService svc = ActionBarClient.Current;
            if (slot < 0 || svc == null || !svc.Ready) return;
            ActionEntryType type = kind == UiDrag.Kind.Ability ? ActionEntryType.Ability
                : kind == UiDrag.Kind.Spell ? ActionEntryType.Spell
                : kind == UiDrag.Kind.Emote ? ActionEntryType.Emote : kind == UiDrag.Kind.Mount ? ActionEntryType.Mount : ActionEntryType.Consumable;
            svc.Assign(ActionBarInput.ActivePage, slot, type, id);
        }

        private int SlotAt(Vector2 screen)
        {
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i] != null && RectTransformUtility.RectangleContainsScreenPoint(_slots[i].Root, screen, null)) return i;
            return -1;
        }

        // ================================================================== Takt

        private void Update()
        {
            if (_bar == null) return;
            IActionBarService svc = ActionBarClient.Current;
            bool ready = svc != null && svc.Ready;
            if (_bar.gameObject.activeSelf != ready) _bar.gameObject.SetActive(ready);
            if (_bankBorder.transform.parent.gameObject.activeSelf != ready) _bankBorder.transform.parent.gameObject.SetActive(ready);
            if (!ready) { _tip.gameObject.SetActive(false); return; }
            if (_player == null) _player = FindLocalPlayer();
            // Spielmenü offen (UI-204): Die Leiste liegt unter dem Menü und nimmt keine Klicks an (sonst löste sie nach dem Schließen aus)
            if (UiPointer.ModalOpen)
            {
                if (_tip.gameObject.activeSelf) _tip.gameObject.SetActive(false);
                _pressSlot = -1;
                _dragging = false;
                _dragIcon.gameObject.SetActive(false);
                return;
            }

            Vector2 mouse = RawInput.MousePosition;
            bool over = RectTransformUtility.RectangleContainsScreenPoint(_bar, mouse, null);
            if (over || _dragging) UiPointer.OverUi = true;
            HandleMouse(mouse, svc);
            UpdateTooltip(mouse);
        }

        private static PlayerCharacter FindLocalPlayer()
        {
            foreach (PlayerCharacter p in FindObjectsByType<PlayerCharacter>(FindObjectsSortMode.None))
                if (p != null && p.isActiveAndEnabled) return p;
            return null;
        }

        private void HandleMouse(Vector2 mouse, IActionBarService svc)
        {
            int page = ActionBarInput.ActivePage;
            if (RawInput.LeftMousePressed)
            {
                _pressSlot = SlotAt(mouse);
                _pressPos = mouse;
                _dragging = false;
            }
            if (_pressSlot >= 0 && RawInput.LeftMouseHeld && !_dragging && (mouse - _pressPos).sqrMagnitude > 36f)
            {
                ActionBarEntry e = ActionBarClient.Get(page, _pressSlot);
                if (!e.IsEmpty)
                {
                    _dragging = true;
                    _dragIcon.sprite = IconOf(e);
                    _dragIcon.gameObject.SetActive(true);
                }
            }
            if (_dragging)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, mouse, null, out Vector2 local);
                ((RectTransform)_dragIcon.transform).anchoredPosition = local;
            }
            if (RawInput.LeftMouseReleased && _pressSlot >= 0)
            {
                int target = SlotAt(mouse);
                if (_dragging)
                {
                    if (target >= 0) svc.Swap(page, _pressSlot, page, target);          // verschieben / tauschen
                    else if (!RectTransformUtility.RectangleContainsScreenPoint(_bar, mouse, null)) svc.Clear(page, _pressSlot); // ins Leere = leeren
                }
                else if (target == _pressSlot) ActionBarInput.QueueSlot(page, _pressSlot); // Klick = ausloesen
                _pressSlot = -1;
                _dragging = false;
                _dragIcon.gameObject.SetActive(false);
            }
            if (!RawInput.LeftMouseHeld && !RawInput.LeftMouseReleased && _pressSlot >= 0)
            {
                _pressSlot = -1;
                _dragging = false;
                _dragIcon.gameObject.SetActive(false);
            }
        }

        private void LateUpdate()
        {
            if (_bar == null || !_bar.gameObject.activeSelf) return;
            int page = ActionBarInput.ActivePage;
            if (page != _shownPage)
            {
                _shownPage = page;
                _bank.text = page == 0 ? "A" : "B";
                _bank.color = page == 0 ? UiKit.TextColor : BankBColor;
                _bankBorder.color = page == 0 ? UiKit.BorderColor : BankBColor;
            }
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _slots.Length; i++)
            {
                _flash[i] = Mathf.Max(0f, _flash[i] - dt * 4f);
                Draw(_slots[i], ActionBarClient.Get(page, i), page);
            }
        }

        // ================================================================== Zeichnen eines Platzes

        private static Sprite IconOf(ActionBarEntry e)
        {
            if (e.type == ActionEntryType.Ability)
            {
                AbilityDefinition a = GameData.Get<AbilityDefinition>(e.referenceId);
                if (a == null) return null;
                return a.icon != null ? a.icon : ItemIcons.ForShape(a.iconShape, a.iconColor);
            }
            if (e.type == ActionEntryType.Emote) return EmoteUi.IconFor(e.referenceId);
            if (e.type == ActionEntryType.Spell) return SpellIcon(GameData.Get<SpellDefinition>(e.referenceId));
            if (e.type == ActionEntryType.Mount) return ItemIcons.ForShape(IconShape.Boots, new Color(0.45f, 0.3f, 0.2f)); // Platzhalter bis zum Reittier-Symbol
            if (e.type == ActionEntryType.Companion)
                return e.referenceId == CompanionCatalog.DominateId
                    ? ItemIcons.ForShape(IconShape.Focus, new Color(0.62f, 0.5f, 0.78f))   // Platzhalter: Dominieren
                    : ItemIcons.ForShape(IconShape.Hide, new Color(0.5f, 0.47f, 0.44f));   // Platzhalter: Gefaehrte
            return ItemIcons.Get(GameData.Item(e.referenceId));
        }

        public static Sprite SpellIcon(SpellDefinition s) => s == null ? null : ItemIcons.ForShape(s.iconShape, s.iconColor);

        /// <summary>Tooltip-Text einer Klassenfaehigkeit (Rang, Kosten, Wirkzeit, Reichweite, Abklingzeit, Beschreibung).</summary>
        public static string SpellTooltip(SpellDefinition s, int rank)
        {
            if (s == null) return "";
            var t = Ashveil.Gameplay.Abilities.SpellClient.Bonuses;
            SpellRank r = s.Rank(Mathf.Max(1, rank));
            var sb = new System.Text.StringBuilder();
            CharacterClassDefinition cls = InventoryClient.Current != null ? InventoryClient.Current.Class : null;
            ResourceType res = cls != null ? cls.resource : ResourceType.None;
            int cost = Ashveil.Gameplay.Abilities.SpellRules.CostOf(s, r, t);
            if (cost > 0) sb.Append(cost).Append(' ').Append(SpellDefinition.ResourceName(res)).Append("   ");
            if (s.target == SpellTarget.Enemy || s.target == SpellTarget.Friendly || s.target == SpellTarget.DeadFriendly)
                sb.Append(s.range > 6f ? $"{s.range:0} m Reichweite" : "Nahkampf").Append("   ");
            float cast = Ashveil.Gameplay.Abilities.SpellRules.CastTimeOf(s, t);
            sb.Append(s.effect == SpellEffectKind.NextSwing ? "Nächster Schlag" : cast > 0.01f ? $"{cast:0.#} s Wirkzeit" : "Sofort");
            float cd = Ashveil.Gameplay.Abilities.SpellRules.CooldownOf(s, t);
            if (cd > 0f) sb.Append("   ").Append(cd >= 60f ? $"{cd / 60f:0.#} Min. Abklingzeit" : $"{cd:0.#} s Abklingzeit");
            if ((s.requires & SpellRequirement.Shield) != 0) sb.Append("\nBraucht einen Schild");
            if ((s.requires & SpellRequirement.RangedWeapon) != 0) sb.Append("\nBraucht Bogen oder Armbrust");
            if (s.outOfCombatOnly) sb.Append("\nNur außerhalb des Kampfes");
            sb.Append("\n<color=#e8d9b0>").Append(Ashveil.Gameplay.Abilities.SpellRules.Describe(s, rank, t)).Append("</color>");
            return sb.ToString();
        }

        private void Draw(SlotView v, ActionBarEntry e, int page)
        {
            Color border = page == 0 ? UiKit.BorderColor : new Color(BankBColor.r * 0.8f, BankBColor.g * 0.8f, BankBColor.b * 0.8f, 0.9f);
            v.Flash.color = new Color(1f, 0.9f, 0.6f, _flash[v.Slot] * 0.45f);
            if (e.IsEmpty)
            {
                v.Icon.enabled = false;
                v.Sweep.fillAmount = 0f;
                v.Timer.text = v.Count.text = "";
                v.State.color = Color.clear;
                v.Border.color = new Color(border.r, border.g, border.b, 0.45f);
                return;
            }
            Sprite icon = IconOf(e);
            v.Icon.enabled = icon != null;
            if (v.Icon.sprite != icon) v.Icon.sprite = icon;

            float left = 0f, total = 0f;
            string count = "";
            bool unusable = false, badTarget = false;
            WeaponUser wu = _player != null ? _player.Weapons : null;
            CharacterMotor motor = _player != null ? _player.Motor : null;

            if (e.type == ActionEntryType.Ability)
            {
                WeaponDefinition w = wu != null ? wu.ActiveWeapon : null;
                switch (ActionBarInput.KindOf(e.referenceId))
                {
                    case AbilityKind.AutoAttack:
                        unusable = w == null;
                        if (w != null && w.UsesAmmo) count = wu.Ammo.ToString();
                        else if (w != null && w.ammoType != AmmoType.None)
                        {
                            var sup = wu.GetComponent<Ashveil.Gameplay.Combat.IRangedSupply>();
                            if (sup != null) { int n = sup.AmmoCount(w.ammoType); count = n.ToString(); unusable = n <= 0; }
                        }
                        badTarget = TargetBad(w);
                        if (wu != null && wu.IsAutoAttacking)
                            border = new Color(1f, 0.8f, 0.25f, 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 6f));
                        break;
                    case AbilityKind.WeaponSpecial:
                        unusable = w == null;
                        if (wu != null) { left = wu.SpecialCooldownRemaining; total = wu.SpecialCooldownTotal; }
                        badTarget = TargetBad(w);
                        break;
                    case AbilityKind.Dodge:
                        if (motor != null)
                        {
                            left = motor.State.DodgeCooldown;
                            total = motor.Settings.dodgeCooldown;
                            unusable = motor.State.Stamina < motor.Settings.dodgeStaminaCost || !motor.CanRoll;
                        }
                        break;
                    case AbilityKind.SwapWeapon:
                        unusable = wu == null || wu.MeleeWeapon == null || wu.RangedWeapon == null;
                        break;
                    case AbilityKind.Reload:
                        unusable = w == null || !w.UsesAmmo || wu.Ammo >= wu.MagazineSize || wu.IsReloading;
                        if (w != null && w.UsesAmmo) count = wu.Ammo + "/" + wu.MagazineSize;
                        break;
                }
            }
            else if (e.type == ActionEntryType.Spell)
            {
                SpellDefinition s = GameData.Get<SpellDefinition>(e.referenceId);
                int rank = Ashveil.Gameplay.Abilities.SpellClient.RankOf(e.referenceId);
                left = Ashveil.Gameplay.Abilities.SpellClient.CooldownRemaining(e.referenceId, out total);
                if (s == null || rank <= 0) unusable = true;
                else
                {
                    int cost = Ashveil.Gameplay.Abilities.SpellRules.CostOf(s, s.Rank(rank), Ashveil.Gameplay.Abilities.SpellClient.Bonuses);
                    unusable = cost > 0 && Ashveil.Gameplay.Abilities.SpellClient.ResourceCurrent + 0.01f < cost;
                    if (s.target == SpellTarget.Enemy) badTarget = SpellTargetBad(s);
                    if (Ashveil.Gameplay.Abilities.SpellClient.QueuedNextSwing == s.Id || Ashveil.Gameplay.Abilities.SpellClient.CastingSpell == s.Id)
                        border = new Color(1f, 0.8f, 0.25f, 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 6f));
                }
            }
            else if (e.type == ActionEntryType.Companion)
            {
                if (e.referenceId == CompanionCatalog.CallId)
                {
                    if (Ashveil.Gameplay.Companions.CompanionClient.Out) border = new Color(1f, 0.8f, 0.25f, 0.85f);
                    unusable = !Ashveil.Gameplay.Companions.CompanionClient.HasCompanion;
                }
                else unusable = !Ashveil.Gameplay.Companions.CompanionClient.CanDominate || Ashveil.Gameplay.Companions.CompanionClient.HasCompanion;
            }
            else if (e.type == ActionEntryType.Mount)
            {
                if (Ashveil.Gameplay.Mounts.MountClient.Mounted)
                    border = new Color(1f, 0.8f, 0.25f, 0.85f);
                unusable = !Ashveil.Gameplay.Mounts.MountClient.HasMount;
            }
            else if (e.type == ActionEntryType.Emote)
            {
                var em = Ashveil.Gameplay.Characters.EmoteClient.Current;
                if (em != null && EmoteCatalog.TryGetByKey(e.referenceId, out EmoteInfo info) && em.Current == info.Id)
                    border = new Color(1f, 0.8f, 0.25f, 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 6f));
            }
            else
            {
                int n = InventoryClient.CountInBag(e.referenceId);
                count = n.ToString();
                unusable = n <= 0;
                left = InventoryClient.CooldownRemaining(e.referenceId);
                total = InventoryClient.CooldownTotal(e.referenceId);
            }

            v.Sweep.fillAmount = total > 0f && left > 0.02f ? Mathf.Clamp01(left / total) : 0f;
            v.Timer.text = left > 0.05f ? (left >= 10f ? Mathf.CeilToInt(left).ToString() : left.ToString("0.0")) : "";
            v.Count.text = count;
            v.Count.color = count == "0" ? new Color(1f, 0.4f, 0.3f) : UiKit.TextColor;
            v.State.color = badTarget ? RedState : unusable ? GreyState : Color.clear;
            v.Icon.color = unusable ? new Color(0.6f, 0.6f, 0.6f, 1f) : Color.white;
            v.Border.color = border;
        }

        /// <summary>Braucht ein Ziel: keines, freundlich, tot oder ausser Reichweite der aktiven Waffe?</summary>
        private bool TargetBad(WeaponDefinition w)
        {
            if (_player == null || _player.Targeting == null || w == null) return false;
            CombatTarget t = _player.Targeting.Current;
            if (t == null) return false; // ohne Ziel greift man ins Leere – kein Fehler
            if (!FactionRules.IsAttackableByPlayer(t.Faction) || t.Health == null || !t.Health.IsAlive) return true; // #28: neutrale Tiere sind angreifbar
            float d = Vector3.Distance(_player.transform.position, t.transform.position) - t.SelectionRadius;
            return d > w.range + 0.5f;
        }

        /// <summary>Feindliche Faehigkeit: kein Ziel, freundlich/tot oder ausser Reichweite (rot).</summary>
        private bool SpellTargetBad(SpellDefinition s)
        {
            if (_player == null || _player.Targeting == null) return true;
            CombatTarget t = _player.Targeting.Current;
            if (t == null || !FactionRules.IsAttackableByPlayer(t.Faction) || t.Health == null || !t.Health.IsAlive) return true;
            Vector3 a = _player.transform.position, b = t.transform.position;
            a.y = b.y = 0f;
            float d = Vector3.Distance(a, b) - t.SelectionRadius;
            return d > s.Range + 0.5f || (s.minRange > 0f && d < s.minRange - 0.5f);
        }

        // ================================================================== Tooltip

        private void UpdateTooltip(Vector2 mouse)
        {
            int i = _dragging ? -1 : SlotAt(mouse);
            ActionBarEntry e = i >= 0 ? ActionBarClient.Get(ActionBarInput.ActivePage, i) : default;
            bool show = i >= 0 && !e.IsEmpty;
            if (_tip.gameObject.activeSelf != show) _tip.gameObject.SetActive(show);
            if (!show) return;
            string name, desc;
            if (e.type == ActionEntryType.Ability)
            {
                AbilityDefinition a = GameData.Get<AbilityDefinition>(e.referenceId);
                name = a != null ? a.DisplayName : e.referenceId;
                desc = a != null ? a.Description : "";
                WeaponDefinition w = _player != null && _player.Weapons != null ? _player.Weapons.ActiveWeapon : null;
                if (w != null && a != null && a.kind == AbilityKind.WeaponSpecial && !string.IsNullOrEmpty(w.specialName)) name += " – " + w.specialName;
            }
            else if (e.type == ActionEntryType.Companion)
            {
                if (e.referenceId == CompanionCatalog.DominateId)
                {
                    name = "Dominieren";
                    desc = "Brich den Willen eines geschwächten Tiers (unter 35 % Leben, höchstens deine Stufe, 10 m): 3 s halten – " +
                           "Laufen oder Angreifen bricht ab. Danach folgt es dir als Gefährte. Nie Bosse, Elite oder besondere Wesen.";
                }
                else
                {
                    CompanionSpecies sp = CompanionCatalog.Get(Ashveil.Gameplay.Companions.CompanionClient.Species);
                    string pn = !string.IsNullOrEmpty(Ashveil.Gameplay.Companions.CompanionClient.Name) ? Ashveil.Gameplay.Companions.CompanionClient.Name : sp != null ? sp.displayName : "Gefährte";
                    name = Ashveil.Gameplay.Companions.CompanionClient.Out ? pn + " entlassen" : pn + " rufen";
                    desc = sp != null ? $"{sp.signatureName}: {sp.signatureDescription}\n{sp.traitName}: {sp.traitDescription}" : "";
                }
            }
            else if (e.type == ActionEntryType.Mount)
            {
                ClassMountDefinition md = MountCatalog.Get(e.referenceId);
                name = Ashveil.Gameplay.Mounts.MountClient.HasMount ? Ashveil.Gameplay.Mounts.MountClient.Name : md != null ? md.displayName : e.referenceId;
                desc = Ashveil.Gameplay.Mounts.MountClient.Mounted ? "Absitzen." :
                    $"Ruft dein Reittier ({(md != null ? md.mountSeconds : 1.5f):0.#} s, Bewegen oder Treffer bricht ab). +{(md != null ? md.speedBonus * 100f : 40f):0} % Tempo. " +
                    "Nicht im Kampf, nicht in Instanzen oder im Ring. Ein Treffer wirft dich aus dem Sattel.";
            }
            else if (e.type == ActionEntryType.Spell)
            {
                SpellDefinition sd = GameData.Get<SpellDefinition>(e.referenceId);
                int rank = Ashveil.Gameplay.Abilities.SpellClient.RankOf(e.referenceId);
                name = sd != null ? sd.DisplayName + (sd.RankCount > 1 && rank > 0 ? $" <color=#b8b09c><size=85%>(Rang {rank})</size></color>" : "") : e.referenceId;
                desc = SpellTooltip(sd, rank);
            }
            else if (e.type == ActionEntryType.Emote)
            {
                bool ok = EmoteCatalog.TryGetByKey(e.referenceId, out EmoteInfo info);
                name = ok ? info.Name : e.referenceId;
                desc = ok ? info.Description + " (/" + info.Command + ")" : "";
            }
            else
            {
                ItemDefinition d = GameData.Item(e.referenceId);
                name = d != null ? d.DisplayName : e.referenceId;
                desc = d != null ? d.Description : "";
            }
            string key = _slots[i].Key.text;
            _tipText.text = $"<b>{name}</b>  <color=#b8b09c>[{(ActionBarInput.ActivePage == 0 ? "A" : "B")}{i + 1}{(key.Length > 0 ? " · " + key : "")}]</color>\n<size=85%><color=#c8c0ac>{desc}</color></size>";
            _tip.anchoredPosition = new Vector2(26f - _bar.sizeDelta.x * 0.5f + (i + 0.5f) * (SlotSize + Gap), 96f);
            float h = _tipText.GetPreferredValues(_tipText.text, 340f - 16f, 0f).y + 18f;
            _tip.sizeDelta = new Vector2(_tip.sizeDelta.x, Mathf.Max(64f, h));
        }
    }
}
