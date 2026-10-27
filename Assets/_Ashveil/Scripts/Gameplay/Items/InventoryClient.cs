using System;
using System.Collections.Generic;
using Ashveil.Data;
using UnityEngine;

namespace Ashveil.Gameplay.Items
{
    /// <summary>
    /// Was die Oberflaeche ueber das eigene Inventar wissen und ausloesen darf. Jede Aktion ist nur eine
    /// Bitte an den Server; die Anzeige aendert sich erst, wenn er antwortet.
    /// </summary>
    public interface IInventoryService
    {
        bool Ready { get; }
        ItemStack[] Bag { get; }
        ItemStack[] Equipment { get; }
        /// <summary>Die 4 Taschenplaetze (Taschen, Koecher).</summary>
        ItemStack[] Containers { get; }
        /// <summary>Gewaehlte Munitionssorte.</summary>
        string AmmoId { get; }
        CharacterClassDefinition Class { get; }
        int Level { get; }
        /// <summary>Fehlermeldung der letzten Antwort (leer = alles gut).</summary>
        string LastError { get; }

        void Move(int from, int to);
        void Split(int from, int amount, int to);
        void Use(int bagIndex);
        /// <summary>Gegenstand nach ID benutzen (Actionbar) – der Server sucht den Rucksackplatz und prueft alles.</summary>
        void UseItem(string itemId);
        void AutoEquip(int bagIndex);
        void Unequip(EquipmentSlot slot);

        bool LootOpen { get; }
        GameObject LootSource { get; }
        IReadOnlyList<ItemStack> Loot { get; }
        bool TryOpenLoot(GameObject corpse);
        void TakeLoot(int index);
        void TakeAllLoot();
        void CloseLoot();
    }

    /// <summary>Zentrale Anlaufstelle fuer die Oberflaeche (eigener Spieler).</summary>
    public static class InventoryClient
    {
        public static IInventoryService Current { get; private set; }

        /// <summary>Rucksack/Ausruestung haben sich geaendert.</summary>
        public static event Action Changed;
        /// <summary>Beutefenster auf/zu oder Inhalt geaendert.</summary>
        public static event Action LootChanged;

        public static void Register(IInventoryService service)
        {
            Current = service;
            Changed?.Invoke();
            LootChanged?.Invoke();
        }

        public static void Unregister(IInventoryService service)
        {
            if (Current != service) return;
            Current = null;
            Changed?.Invoke();
            LootChanged?.Invoke();
        }

        public static void RaiseChanged() => Changed?.Invoke();
        public static void RaiseLootChanged() => LootChanged?.Invoke();

        // ---------------------------------------------------------------- Abklingzeiten (vom Server gemeldet)

        private static readonly Dictionary<string, (float end, float total)> Cooldowns = new Dictionary<string, (float, float)>();

        /// <summary>Der Server meldet: dieser Gegenstand ist fuer <paramref name="seconds"/> s gesperrt.</summary>
        public static void SetCooldown(string itemId, float seconds)
        {
            if (string.IsNullOrEmpty(itemId)) return;
            Cooldowns[itemId] = (Time.unscaledTime + Mathf.Max(0f, seconds), Mathf.Max(0.01f, seconds));
        }

        public static float CooldownRemaining(string itemId) =>
            itemId != null && Cooldowns.TryGetValue(itemId, out var c) ? Mathf.Max(0f, c.end - Time.unscaledTime) : 0f;

        public static float CooldownTotal(string itemId) =>
            itemId != null && Cooldowns.TryGetValue(itemId, out var c) ? c.total : 0f;

        /// <summary>Wie viele davon liegen im eigenen Rucksack?</summary>
        public static int CountInBag(string itemId)
        {
            if (Current == null || !Current.Ready || string.IsNullOrEmpty(itemId)) return 0;
            int n = 0;
            foreach (ItemStack s in Current.Bag) if (!s.IsEmpty && s.id == itemId) n += s.count;
            return n;
        }

        /// <summary>Nachbildung des Inventars aus dem Client-Stand (Taschengroessen, Koecher-Regeln fuer die Anzeige).</summary>
        public static InventoryModel Mirror(IInventoryService s)
        {
            var m = new InventoryModel();
            if (s == null || !s.Ready) return m;
            m.Class = s.Class;
            m.Level = s.Level;
            m.Load(s.Bag, s.Equipment, s.Containers, s.AmmoId);
            return m;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Cooldowns.Clear();
    }
}
