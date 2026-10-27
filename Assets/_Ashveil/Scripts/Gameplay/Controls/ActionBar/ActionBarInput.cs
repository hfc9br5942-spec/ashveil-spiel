using System;
using Ashveil.Core.Settings;
using Ashveil.Data;
using Ashveil.Gameplay.Items;
using UnityEngine;

namespace Ashveil.Gameplay.Controls
{
    /// <summary>
    /// Was die Oberflaeche ueber die eigene Actionbar wissen und aendern darf. Die Belegung gehoert zum Charakter
    /// (Server, Speicherstand); jede Aenderung ist eine Bitte, die der Server prueft und bestaetigt.
    /// </summary>
    public interface IActionBarService
    {
        bool Ready { get; }
        /// <summary>24 Plaetze (Bank A 0–11, Bank B 12–23), leere mit Typ None.</summary>
        ActionBarEntry[] Slots { get; }
        int Revision { get; }
        void Assign(int page, int slot, ActionEntryType type, string referenceId);
        void Clear(int page, int slot);
        /// <summary>Tauschen bzw. Verschieben (Ziel leer).</summary>
        void Swap(int pageA, int slotA, int pageB, int slotB);
    }

    /// <summary>Zentrale Anlaufstelle fuer die Oberflaeche (eigener Spieler).</summary>
    public static class ActionBarClient
    {
        public static IActionBarService Current { get; private set; }

        /// <summary>Belegung geaendert (vom Server bestaetigt).</summary>
        public static event Action Changed;

        public static void Register(IActionBarService s)
        {
            Current = s;
            Changed?.Invoke();
        }

        public static void Unregister(IActionBarService s)
        {
            if (Current != s) return;
            Current = null;
            Changed?.Invoke();
        }

        public static void RaiseChanged() => Changed?.Invoke();

        public static ActionBarEntry Get(int page, int slot)
        {
            IActionBarService s = Current;
            int i = page * ActionBarLayout.SlotsPerPage + slot;
            if (s == null || !s.Ready || s.Slots == null || i < 0 || i >= s.Slots.Length) return default;
            return s.Slots[i];
        }
    }

    /// <summary>
    /// Tasten -&gt; Actionbar-Plaetze (Feature-Update 26.09., Abschnitte 1–4, 9). Die zwoelf Tasten gelten fuer beide
    /// Baenke; die aktive Bank waehlt die Wechseltaste (Halten oder Umschalten). Ausgeloest wird ueber die bestehenden
    /// Wege: Waffenaktionen als <see cref="CombatInput"/>/Ausweichen (Server prueft wie bisher), Gegenstaende ueber
    /// <see cref="IInventoryService.UseItem"/> (Server prueft Besitz, Stufe, Abklingzeit, Leben).
    /// Keine Spiellogik haengt an einer bestimmten Taste.
    /// </summary>
    public static class ActionBarInput
    {
        private static readonly bool[] Injected = new bool[ActionBarLayout.SlotsPerPage];
        private static readonly System.Collections.Generic.List<(int page, int slot)> Queued = new System.Collections.Generic.List<(int, int)>();
        private static bool _toggled, _simTogglePress;

        /// <summary>Tests: Wechseltaste „gedrueckt halten“.</summary>
        public static bool SimulateSwapHeld;

        /// <summary>0 = Bank A, 1 = Bank B.</summary>
        public static int ActivePage { get; private set; }

        /// <summary>Platz ausgeloest (Seite, Platz) – fuer das Aufleuchten in der Leiste.</summary>
        public static event Action<int, int> Triggered;

        /// <summary>Zuletzt ausgeloeste Aktion (Tests, Anzeige).</summary>
        public static string LastTriggeredId { get; private set; } = "";
        public static int TriggerCount { get; private set; }

        /// <summary>Tests: Slot-Taste i fuer den naechsten Frame druecken (wirkt auf die dann aktive Bank).</summary>
        public static void InjectSlotKey(int slot)
        {
            if (slot >= 0 && slot < Injected.Length) Injected[slot] = true;
        }

        /// <summary>Tests: Wechseltaste einmal druecken (Umschalt-Modus).</summary>
        public static void InjectSwapPress() => _simTogglePress = true;

        /// <summary>Klick auf einen Platz in der Leiste (genau dieser Platz, unabhaengig von der aktiven Bank).</summary>
        public static void QueueSlot(int page, int slot) => Queued.Add((page, slot));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Array.Clear(Injected, 0, Injected.Length);
            Queued.Clear();
            _toggled = false;
            _simTogglePress = false;
            SimulateSwapHeld = false;
            ActivePage = 0;
            Triggered = null;
        }

        /// <summary>Einmal pro Frame aus dem Eingabeleser (eigener Spieler).</summary>
        public static void Poll(bool keysAllowed, ref CombatInput combat, ref bool dodge)
        {
            InputBindings b = InputBindingsStore.Current;
            bool swapPressed = _simTogglePress || (keysAllowed && RawInput.WasPressed(b.swapKey));
            _simTogglePress = false;
            if (b.toggleMode)
            {
                if (swapPressed) _toggled = !_toggled;
                ActivePage = _toggled ? 1 : 0;
            }
            else
            {
                _toggled = false;
                ActivePage = SimulateSwapHeld || (keysAllowed && RawInput.IsHeld(b.swapKey)) ? 1 : 0;
            }

            for (int i = 0; i < ActionBarLayout.SlotsPerPage; i++)
            {
                bool pressed = Injected[i] || (keysAllowed && RawInput.WasPressed(b.slots[i]));
                Injected[i] = false;
                if (pressed) Trigger(ActivePage, i, ref combat, ref dodge);
            }
            foreach ((int page, int slot) q in Queued) Trigger(q.page, q.slot, ref combat, ref dodge);
            Queued.Clear();
        }

        private static void Trigger(int page, int slot, ref CombatInput combat, ref bool dodge)
        {
            ActionBarEntry e = ActionBarClient.Get(page, slot);
            if (e.IsEmpty) return;
            LastTriggeredId = e.referenceId;
            TriggerCount++;
            Triggered?.Invoke(page, slot);
            switch (e.type)
            {
                case ActionEntryType.Ability:
                    switch (KindOf(e.referenceId))
                    {
                        case AbilityKind.AutoAttack: combat.Ability1 = true; break;
                        case AbilityKind.WeaponSpecial: combat.Ability2 = true; break;
                        case AbilityKind.Dodge: dodge = true; break;
                        case AbilityKind.SwapWeapon: combat.SwapWeapon = true; break;
                        case AbilityKind.Reload: combat.Reload = true; break;
                    }
                    break;
                case ActionEntryType.Consumable:
                case ActionEntryType.CombatItem:
                    InventoryClient.Current?.UseItem(e.referenceId);
                    break;
                case ActionEntryType.Emote:
                    if (EmoteCatalog.TryGetByKey(e.referenceId, out EmoteInfo emote)) Ashveil.Gameplay.Characters.EmoteClient.Play(emote.Id);
                    break;
                case ActionEntryType.Mount:
                    Ashveil.Gameplay.Mounts.MountClient.Toggle();
                    break;
                case ActionEntryType.Companion:
                    Ashveil.Gameplay.Companions.CompanionClient.Trigger(e.referenceId);
                    break;
                case ActionEntryType.Spell:
                    Ashveil.Gameplay.Abilities.SpellClient.CastRequest?.Invoke(e.referenceId);
                    break;
            }
        }

        /// <summary>Art einer Faehigkeit (aus der Definition, sonst aus der bekannten ID).</summary>
        public static AbilityKind KindOf(string abilityId)
        {
            AbilityDefinition def = GameData.Get<AbilityDefinition>(abilityId);
            if (def != null) return def.kind;
            switch (abilityId)
            {
                case ActionBarLayout.WeaponSpecial: return AbilityKind.WeaponSpecial;
                case ActionBarLayout.Dodge: return AbilityKind.Dodge;
                case ActionBarLayout.SwapWeapon: return AbilityKind.SwapWeapon;
                case ActionBarLayout.Reload: return AbilityKind.Reload;
                default: return AbilityKind.AutoAttack;
            }
        }
    }
}
