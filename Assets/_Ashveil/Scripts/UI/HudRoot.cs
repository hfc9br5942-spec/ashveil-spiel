using Ashveil.Core.Events;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Weapons;
using TMPro;
using UnityEngine;

namespace Ashveil.UI
{
    /// <summary>
    /// Spieloberflaeche (Milestone 2): Spielerrahmen, Zielrahmen, Aktionsleiste, Nachlade-/Zeitleiste,
    /// Munitionsanzeige und Fehlermeldungen. Alles wird zur Laufzeit per Code aufgebaut, damit keine
    /// Prefab-Verdrahtung noetig ist. Minimalistisch, dunkel, gut lesbar.
    /// </summary>
    public sealed class HudRoot : MonoBehaviour
    {
        private PlayerCharacter _player;
        private Health _playerHealth;
        private CombatTarget _boundTarget;

        // Spielerrahmen
        private TextMeshProUGUI _playerName;
        private TextMeshProUGUI _clock;
        private int _clockMinute = -1;
        private UiKit.Bar _playerHp;
        private UiKit.Bar _playerStamina;
        private UiKit.Bar _playerResource;
        private ResourceType _shownResource = (ResourceType)255;

        // Zielrahmen
        private RectTransform _targetFrame;
        private TextMeshProUGUI _targetName;
        private TextMeshProUGUI _targetLevel;
        private UiKit.Bar _targetHp;
        private TextMeshProUGUI _targetPart;
        private string _partShown;

        // Waffenanzeige (die Aktionsleiste selbst ist seit M6.6 die ActionBarUi)
        private TextMeshProUGUI _weaponName;
        private TextMeshProUGUI _ammo;
        private UiKit.Bar _castBar;

        // Fehlermeldung
        private TextMeshProUGUI _error;
        private float _errorTimer;

        [System.NonSerialized] private bool _built; // nach Skript-Neuladen im Play Mode neu aufbauen

        private void OnEnable()
        {
            EventBus<LocalPlayerSpawnedEvent>.Subscribe(OnPlayerSpawned);
            EventBus<CombatTextEvent>.Subscribe(OnCombatText);
            Ashveil.Gameplay.Items.InventoryClient.Changed += OnInventoryChanged;
        }

        private void OnDisable()
        {
            EventBus<LocalPlayerSpawnedEvent>.Unsubscribe(OnPlayerSpawned);
            EventBus<CombatTextEvent>.Unsubscribe(OnCombatText);
            Ashveil.Gameplay.Items.InventoryClient.Changed -= OnInventoryChanged;
        }

        /// <summary>Klasse und Stufe im Spielerrahmen (kommen mit dem Charakter vom Server).</summary>
        private void OnInventoryChanged()
        {
            var svc = Ashveil.Gameplay.Items.InventoryClient.Current;
            if (!_built || _playerName == null || svc == null || !svc.Ready) return;
            string cls = svc.Class != null ? svc.Class.DisplayName : "Wanderer";
            string me = Ashveil.Gameplay.Characters.LocalPlayerInfo.Name;
            _playerName.text = string.IsNullOrEmpty(me)
                ? $"{cls}  <color=#b8b09c><size=80%>Stufe {svc.Level}</size></color>"
                : $"{me}  <color=#b8b09c><size=80%>{cls} · Stufe {svc.Level}</size></color>";
        }

        private void Start()
        {
            if (UiKit.Font == null)
            {
                Debug.LogWarning("[HudRoot] Keine TextMeshPro-Schrift – HUD deaktiviert.");
                enabled = false;
                return;
            }
            Build();
            Bind(_player != null ? _player : FindAnyObjectByType<PlayerCharacter>());
        }

        private void OnPlayerSpawned(LocalPlayerSpawnedEvent evt)
        {
            if (evt.Player != null) Bind(evt.Player.GetComponent<PlayerCharacter>());
        }

        private void Bind(PlayerCharacter player)
        {
            _player = player;
            _playerHealth = player != null ? player.GetComponent<Health>() : null;
            if (_built && _playerName != null) _playerName.text = "Wanderer  <color=#b8b09c><size=80%>Stufe 1</size></color>";
        }

        private void OnCombatText(CombatTextEvent evt)
        {
            if (evt.Kind != CombatTextKind.Error || _error == null) return;
            _error.text = evt.Text;
            _errorTimer = 2.2f;
        }

        // ---------------------------------------------------------------- Aufbau

        private void Build()
        {
            Canvas canvas = UiKit.CreateCanvas("HUD Canvas", transform, 10);
            Transform root = canvas.transform;
            ColosseumHud.Ensure(transform); // Kolosseum Pass 4: Uhr, Block, Wiederbelebungen
            CastBarHud.Ensure(transform);   // Zauberleiste (Aufsitzen)
            BreathBarHud.Ensure(transform); // Atemleiste (Schwimmen und Tauchen, MOV-169)
            CompanionUi.Ensure(transform);  // Gefaehrten-Rahmen (Dominator)
            StoryUi.Ensure(transform);      // Geschichts-Szenen: Untertitel, Handlungs-Hinweis

            // Spielerrahmen oben links
            RectTransform pf = UiKit.Panel("Player Frame", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(300f, 80f));
            _playerName = UiKit.CreateText("Name", pf, "Wanderer", 19f, UiKit.TextColor);
            Place(_playerName.rectTransform, new Vector2(10f, -6f), new Vector2(280f, 24f));
            _playerHp = UiKit.CreateBar("Health", pf, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -32f), new Vector2(278f, 20f), UiKit.HealthColor);
            // P1: Klassen-Ressource (Wut rot, Energie gelb, Mana blau) unter dem Leben, Ausdauer als duenne Linie darunter
            _playerResource = UiKit.CreateBar("Resource", pf, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -54f), new Vector2(278f, 14f),
                SpellDefinition.ResourceColor(ResourceType.Mana), 11f);
            _playerStamina = UiKit.CreateBar("Stamina", pf, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -70f), new Vector2(278f, 5f), UiKit.StaminaColor, 1f);

            // M8: Tageszeit unter dem Spielerrahmen
            RectTransform clockPanel = UiKit.Panel("Uhrzeit", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -108f), new Vector2(190f, 26f));
            _clock = UiKit.CreateText("Text", clockPanel, "", 15f, UiKit.TextColor);
            Place(_clock.rectTransform, new Vector2(10f, -3f), new Vector2(176f, 20f));

            // Zielrahmen rechts daneben
            _targetFrame = UiKit.Panel("Target Frame", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(340f, -24f), new Vector2(300f, 64f));
            _targetName = UiKit.CreateText("Name", _targetFrame, "", 19f, UiKit.TextColor);
            Place(_targetName.rectTransform, new Vector2(10f, -6f), new Vector2(220f, 24f));
            _targetLevel = UiKit.CreateText("Level", _targetFrame, "", 16f, UiKit.MutedText, TextAlignmentOptions.Right);
            Place(_targetLevel.rectTransform, new Vector2(228f, -8f), new Vector2(60f, 22f));
            _targetHp = UiKit.CreateBar("Health", _targetFrame, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -34f), new Vector2(278f, 20f), UiKit.HealthColor);
            // Boss-Anatomie §5: dezenter Zusatz unter dem Rahmen „Ziel: LINKES KNIE“ – keine Balken je Teil
            _targetPart = UiKit.CreateText("Teil", _targetFrame, "", 15f, UiKit.TextColor);
            _targetPart.richText = true;
            Place(_targetPart.rectTransform, new Vector2(10f, -62f), new Vector2(300f, 22f));
            _targetFrame.parent.gameObject.SetActive(false);

            // M6.6: 24-Slot-Actionbar unten Mitte (frei belegbar, zwei Baenke) und Faehigkeitenbuch (K)
            gameObject.AddComponent<ActionBarUi>().Build((RectTransform)root);
            gameObject.AddComponent<SpellbookUi>().Build((RectTransform)root);
            gameObject.AddComponent<EmoteUi>().Build((RectTransform)root); // M8: Emote-Menue (J)
            gameObject.AddComponent<TrainerUi>().Build(transform);   // P1: Klassenlehrer
            gameObject.AddComponent<TalentUi>().Build(transform);    // P1: Talente (N)
            // M7: Charakter-Editor (eigene Vollbild-Ebene, oeffnet sich nur fuer Profile ohne Charakter)
            gameObject.AddComponent<CharacterCreatorUi>().Build(transform);
            // UI-204 (Spieltest r2): Spielmenue mit „Speichern und beenden“ und die Esc-Regel (Fenster, dann Ziel, dann Menue)
            gameObject.AddComponent<SpielmenueUi>().Build(transform);

            _weaponName = UiKit.CreateText("Weapon", root, "", 18f, UiKit.TextColor, TextAlignmentOptions.Center);
            PlaceAnchored(_weaponName.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 100f), new Vector2(400f, 24f));
            _ammo = UiKit.CreateText("Ammo", root, "", 22f, UiKit.TextColor, TextAlignmentOptions.Left);
            PlaceAnchored(_ammo.rectTransform, new Vector2(0.5f, 0f), new Vector2(260f, 100f), new Vector2(120f, 30f));

            _castBar = UiKit.CreateBar("Cast Bar", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(260f, 16f),
                new Color(0.85f, 0.7f, 0.3f), 12f);
            _castBar.Root.gameObject.SetActive(false);

            _error = UiKit.CreateText("Error", root, "", 24f, new Color(1f, 0.28f, 0.2f), TextAlignmentOptions.Center);
            PlaceAnchored(_error.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(900f, 34f));

            _built = true;
        }

        private static void Place(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static void PlaceAnchored(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        // ---------------------------------------------------------------- Aktualisierung

        private string _shownName = "";

        private void LateUpdate()
        {
            if (Ashveil.Gameplay.Characters.LocalPlayerInfo.Name != _shownName)
            {
                _shownName = Ashveil.Gameplay.Characters.LocalPlayerInfo.Name;
                OnInventoryChanged();
            }
            if (!_built) return;

            if (_errorTimer > 0f)
            {
                _errorTimer -= Time.unscaledDeltaTime;
                _error.alpha = Mathf.Clamp01(_errorTimer / 0.5f);
            }

            if (_player == null) return;

            UpdatePlayerFrame();
            UpdateClock();
            UpdateTargetFrame();
            UpdateActionBar();
        }

        private void UpdateClock()
        {
            if (_clock == null) return;
            float h = Ashveil.Core.WorldClock.Hours;
            int minute = (int)(h * 60f) / 5; // alle 5 Spielminuten neu
            if (minute == _clockMinute) return;
            _clockMinute = minute;
            _clock.text = $"{Ashveil.Core.WorldClock.Text} Uhr  <color=#b8b09c><size=88%>· {Ashveil.Core.WorldClock.PeriodName(h)}</size></color>";
        }

        /// <summary>Test: angezeigte Uhrzeit.</summary>
        public string ClockText => _clock != null ? _clock.text : "";

        private void UpdatePlayerFrame()
        {
            if (_playerHealth != null)
            {
                _playerHp.Set(_playerHealth.Fraction);
                _playerHp.Label.text = $"{_playerHealth.Current:0} / {_playerHealth.Max:0}";
            }
            CharacterMotor m = _player.Motor;
            if (m != null)
            {
                float max = Mathf.Max(1f, m.Settings.maxStamina);
                _playerStamina.Set(m.State.Stamina / max);
                _playerStamina.Label.text = "";
                _playerStamina.FillImage.color = m.State.Exhausted ? new Color(0.85f, 0.3f, 0.2f) : UiKit.StaminaColor;
            }
            ResourceType rt = Ashveil.Gameplay.Abilities.SpellClient.Resource;
            if (rt != _shownResource)
            {
                _shownResource = rt;
                _playerResource.Root.gameObject.SetActive(rt != ResourceType.None);
                _playerResource.FillImage.color = SpellDefinition.ResourceColor(rt);
            }
            if (rt != ResourceType.None)
            {
                float cur = Ashveil.Gameplay.Abilities.SpellClient.ResourceCurrent, rmax = Mathf.Max(1f, Ashveil.Gameplay.Abilities.SpellClient.ResourceMax);
                _playerResource.Set(cur / rmax);
                _playerResource.Label.text = $"{cur:0} / {rmax:0}";
            }
        }

        private void UpdateTargetFrame()
        {
            CombatTarget t = _player.Targeting != null ? _player.Targeting.Current : null;
            GameObject frame = _targetFrame.parent.gameObject;
            if (t == null)
            {
                if (frame.activeSelf) frame.SetActive(false);
                _boundTarget = null;
                return;
            }
            if (!frame.activeSelf) frame.SetActive(true);

            if (t != _boundTarget)
            {
                _boundTarget = t;
                _targetName.text = t.DisplayName;
                _targetName.color = FactionRules.NameColor(t.Faction);
                _targetLevel.text = (t.IsElite ? "Elite  " : "") + t.LevelLabel;
            }
            // Einstellung „Gegner-Lebensbalken“: Voll / Minimal (ohne Prozent) / Aus
            int bars = FactionRules.IsAttackableByPlayer(t.Faction) && Ashveil.Core.Settings.GraphicsSettingsStore.Current != null
                ? Ashveil.Core.Settings.GraphicsSettingsStore.Current.enemyBars : 0;
            if (_targetHp.Root.gameObject.activeSelf != (bars < 2)) _targetHp.Root.gameObject.SetActive(bars < 2);
            _targetHp.Set(t.Health.Fraction);
            _targetHp.Label.text = bars == 0 ? $"{t.Health.Fraction * 100f:0} %" : "";

            string part = PartLine(t, _player.Targeting.CurrentPart);
            if (part != _partShown)
            {
                _partShown = part;
                _targetPart.text = part;
            }
        }

        /// <summary>„Ziel: RECHTER ARM (gebrochen)“; grau, wenn gerade nicht erreichbar (Kopf, solange er steht).</summary>
        public static string PartLine(CombatTarget t, byte partId)
        {
            EnemyAnatomy a = t != null ? t.GetComponent<EnemyAnatomy>() : null;
            if (a == null || a.Parts.Count == 0) return "";
            EnemyAnatomy.Part p = partId != 0 ? a.Get(partId) : null;
            if (p == null) return "<color=#9a9282>Ziel: KÖRPER</color>";
            string name = (p.Def.displayName ?? "").ToUpperInvariant();
            string state = p.State == BodyPartState.Destroyed ? " (zerstört)" : p.State == BodyPartState.Broken ? " (gebrochen)" : p.State == BodyPartState.Damaged ? " (beschädigt)" : "";
            bool reach = a.IsReachable(partId);
            return reach ? $"Ziel: <color=#e8c98a>{name}</color>{state}" : $"<color=#8a8478>Ziel: {name}{state} – gerade nicht erreichbar</color>";
        }

        private void UpdateActionBar()
        {
            WeaponUser wu = _player.Weapons;
            if (wu == null) return;
            WeaponDefinition w = wu.ActiveWeapon;

            if (w != null)
            {
                _weaponName.text = w.DisplayName;
                _weaponName.color = ItemQualityColors.Get(w.quality);
            }
            bool usesAmmo = w != null && w.UsesAmmo;
            // Bogen (WoW): Vorrat der gewaehlten Pfeile in allen Taschen
            Ashveil.Gameplay.Combat.IRangedSupply supply = w != null && w.ammoType != AmmoType.None ? wu.GetComponent<Ashveil.Gameplay.Combat.IRangedSupply>() : null;
            if (supply != null)
            {
                int n = supply.AmmoCount(w.ammoType);
                _ammo.text = $"{n}<size=70%><color=#b8b09c> {(w.ammoType == AmmoType.Bolt ? "Bolzen" : "Pfeile")}</color></size>";
                _ammo.color = n == 0 ? new Color(1f, 0.35f, 0.25f) : UiKit.TextColor;
            }
            else
            {
                _ammo.text = usesAmmo ? $"{wu.Ammo}<size=70%><color=#b8b09c> / {wu.MagazineSize}</color></size>" : "";
                _ammo.color = usesAmmo && wu.Ammo == 0 ? new Color(1f, 0.35f, 0.25f) : UiKit.TextColor;
            }

            // Zeitleiste fuer Nachladen / Waffenwechsel / Wirken (Zauber und gezielte Schuesse wie in WoW; der automatische Schuss hat keine)
            bool casting = wu.IsCasting && w != null && (w.IsSpell || wu.CurrentStepIsSpecial);
            bool showCast = wu.Phase == WeaponPhase.Reloading || wu.Phase == WeaponPhase.Swapping || casting;
            if (_castBar.Root.gameObject.activeSelf != showCast) _castBar.Root.gameObject.SetActive(showCast);
            if (showCast)
            {
                _castBar.Set(wu.PhaseProgress);
                _castBar.Label.text = casting ? wu.CurrentStep.name : wu.Phase == WeaponPhase.Reloading ? "Nachladen" : "Waffe wechseln";
            }
        }
    }
}
