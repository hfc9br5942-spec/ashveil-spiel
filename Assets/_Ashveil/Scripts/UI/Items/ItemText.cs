using System.Collections.Generic;
using System.Text;
using Ashveil.Data;
using Ashveil.Gameplay.Items;
using UnityEngine;

namespace Ashveil.UI
{
    /// <summary>Texte fuer Gegenstaende: Tooltip, Vergleich, Namen von Plaetzen und Arten.</summary>
    public static class ItemText
    {
        private const string Green = "#3fdc4c";
        private const string Red = "#ff4a3a";
        private const string Muted = "#a8a296";
        private const string Flavor = "#d8b86a";

        public static string SlotName(EquipmentSlot s)
        {
            switch (s)
            {
                case EquipmentSlot.Head: return "Kopf";
                case EquipmentSlot.Shoulders: return "Schultern";
                case EquipmentSlot.Chest: return "Brust";
                case EquipmentSlot.Hands: return "Hände";
                case EquipmentSlot.Legs: return "Beine";
                case EquipmentSlot.Feet: return "Füße";
                case EquipmentSlot.Belt: return "Gürtel";
                case EquipmentSlot.Cloak: return "Umhang";
                case EquipmentSlot.Neck: return "Hals";
                case EquipmentSlot.Ring1: return "Ring";
                case EquipmentSlot.Ring2: return "Ring";
                case EquipmentSlot.Trinket1: return "Schmuckstück";
                case EquipmentSlot.Trinket2: return "Schmuckstück";
                case EquipmentSlot.MainHand: return "Haupthand";
                case EquipmentSlot.Ranged: return "Fernkampf";
                default: return "Nebenhand";
            }
        }

        public static string EquipTypeName(ItemDefinition d)
        {
            switch (d.equipType)
            {
                case EquipType.Head: return "Kopf";
                case EquipType.Shoulders: return "Schultern";
                case EquipType.Chest: return "Brust";
                case EquipType.Hands: return "Hände";
                case EquipType.Legs: return "Beine";
                case EquipType.Feet: return "Füße";
                case EquipType.Belt: return "Gürtel";
                case EquipType.Cloak: return "Umhang";
                case EquipType.Neck: return "Hals";
                case EquipType.Ring: return "Finger";
                case EquipType.Trinket: return "Schmuckstück";
                case EquipType.Bag: return "Tasche";
                case EquipType.Weapon:
                    if (d.IsRangedWeapon) return "Distanz";
                    switch (d.Hand)
                    {
                        case WeaponHand.TwoHand: return "Zweihand";
                        case WeaponHand.MainHandOnly: return "Haupthand";
                        case WeaponHand.OffHandOnly: return "Nebenhand";
                        default: return "Einhand";
                    }
                default: return "";
            }
        }

        public static string WeaponClassName(WeaponClass c)
        {
            switch (c)
            {
                case WeaponClass.Sword: return "Schwert";
                case WeaponClass.TwoHandedSword: return "Zweihandschwert";
                case WeaponClass.Axe: return "Axt";
                case WeaponClass.TwoHandedAxe: return "Zweihandaxt";
                case WeaponClass.Spear: return "Speer";
                case WeaponClass.Dagger: return "Dolch";
                case WeaponClass.Hammer: return "Hammer";
                case WeaponClass.Mace: return "Streitkolben";
                case WeaponClass.Bow: return "Bogen";
                case WeaponClass.Crossbow: return "Armbrust";
                case WeaponClass.Pistol: return "Pistole";
                case WeaponClass.Revolver: return "Revolver";
                case WeaponClass.Rifle: return "Büchse";
                case WeaponClass.Shotgun: return "Donnerbüchse";
                case WeaponClass.HeavyGun: return "Schwere Feuerwaffe";
                case WeaponClass.Staff: return "Stab";
                case WeaponClass.Focus: return "Fokus";
                case WeaponClass.Artifact: return "Artefakt";
                default: return "Ätherbüchse";
            }
        }

        public static string ArmorName(ArmorType a)
        {
            switch (a)
            {
                case ArmorType.Cloth: return "Stoff";
                case ArmorType.Leather: return "Leder";
                case ArmorType.Mail: return "Kette";
                case ArmorType.Plate: return "Platte";
                default: return "";
            }
        }

        public static string StatName(StatType s)
        {
            switch (s)
            {
                case StatType.Strength: return "Stärke";
                case StatType.Agility: return "Geschick";
                case StatType.Intellect: return "Intellekt";
                case StatType.Stamina: return "Ausdauer";
                case StatType.Willpower: return "Willenskraft";
                default: return "Rüstung";
            }
        }

        public static string CategoryName(ItemDefinition d)
        {
            switch (d.category)
            {
                case ItemCategory.Consumable: return "Verbrauchsgegenstand";
                case ItemCategory.Material: return "Handwerksmaterial";
                case ItemCategory.Recipe: return "Rezept";
                case ItemCategory.Quest: return "Questgegenstand";
                case ItemCategory.Junk: return "Verkäufliche Beute";
                case ItemCategory.Container:
                    return d.containerKind == ContainerKind.Quiver ? "Köcher" : d.containerKind == ContainerKind.AmmoPouch ? "Munitionsbeutel" : "Tasche";
                case ItemCategory.Ammo: return d.ammoType == AmmoType.Bolt ? "Projektil · Bolzen" : "Projektil · Pfeil";
                default: return "";
            }
        }

        public static string Money(int copper)
        {
            int g = copper / 10000, s = copper / 100 % 100, k = copper % 100;
            var sb = new StringBuilder();
            if (g > 0) sb.Append($"<color=#f0c850>{g} G</color> ");
            if (s > 0 || g > 0) sb.Append($"<color=#c8c8d0>{s} S</color> ");
            sb.Append($"<color=#c8864a>{k} K</color>");
            return sb.ToString();
        }

        public static float Dps(WeaponDefinition w)
        {
            if (w == null) return 0f;
            // Bogen/Armbrust: automatischer Schuss im Waffentempo (wie WoW)
            if (w.archetype == WeaponArchetype.Projectile) return (w.minDamage + w.maxDamage) * 0.5f / Mathf.Max(0.3f, w.attackSpeed);
            if (w.combo == null || w.combo.Length == 0) return 0f;
            float mult = 0f, time = 0f;
            foreach (AttackStep s in w.combo)
            {
                mult += s.damageMultiplier;
                time += Mathf.Max(0.1f, s.Duration);
            }
            return (w.minDamage + w.maxDamage) * 0.5f * mult / time;
        }

        private static string Fmt(float v) => v.ToString(v < 10f ? "0.0" : "0");

        // ================================================================== Tooltip

        /// <param name="compareWith">Angelegte Teile, die dieses ersetzen wuerde (fuer den Vergleich).</param>
        public static string Tooltip(ItemDefinition d, ItemStack stack, IInventoryService who, bool equipped, IList<ItemDefinition> compareWith)
        {
            var sb = new StringBuilder(512);
            string q = ItemQualityColors.Hex(d.quality);
            sb.Append($"<size=120%><b><color={q}>{d.DisplayName}</color></b></size>\n");
            sb.Append($"<color={q}>{ItemQualityColors.Name(d.quality)}</color>");
            if (d.IsEquippable || d.itemLevel > 1) sb.Append($"<color={Muted}>   ·   Gegenstandsstufe {d.itemLevel}</color>");
            sb.Append('\n');

            if (stack.bound) sb.Append("Gebunden\n");
            else if (d.bind == BindType.OnPickup) sb.Append("Wird beim Aufheben gebunden\n");
            else if (d.bind == BindType.OnEquip) sb.Append("Wird beim Anlegen gebunden\n");
            if (d.unique) sb.Append("Einzigartig\n");
            if (d.category == ItemCategory.Quest) sb.Append($"<color={Flavor}>Questgegenstand</color>\n");

            if (d.IsEquippable)
            {
                string kind = d.IsWeapon ? WeaponClassName(d.weapon.weaponClass)
                    : d.equipType == EquipType.Weapon ? (d.armor > 0 ? "Schild" : "Fokus") : ArmorName(d.armorType);
                sb.Append(EquipTypeName(d));
                if (!string.IsNullOrEmpty(kind)) sb.Append($"<color={Muted}>   ·   </color>{kind}");
                sb.Append('\n');
            }
            else
            {
                string cat = CategoryName(d);
                if (!string.IsNullOrEmpty(cat) && d.category != ItemCategory.Quest)
                {
                    sb.Append(cat);
                    if (d.gatheredBy != null) sb.Append($"<color={Muted}> ({d.gatheredBy.DisplayName})</color>");
                    sb.Append('\n');
                }
            }

            if (d.IsWeapon)
            {
                WeaponDefinition w = d.weapon;
                sb.Append($"{w.minDamage:0} – {w.maxDamage:0} Schaden");
                if (w.archetype == WeaponArchetype.Projectile) sb.Append($"<color={Muted}>   ·   Tempo {w.attackSpeed:0.00}</color>");
                if (!w.IsMelee) sb.Append($"<color={Muted}>   ·   Reichweite {(w.minRange > 0f ? $"{w.minRange:0}–" : "")}{w.range:0} m</color>");
                sb.Append($"\n<color={Muted}>({Fmt(Dps(w))} Schaden pro Sekunde)</color>\n");
                if (w.IsSpell) sb.Append($"<color={Muted}>Zauberwaffe – wirkt Magie statt zu schlagen</color>\n");
                if (w.ammoType != AmmoType.None) sb.Append($"<color={Muted}>Benötigt {(w.ammoType == AmmoType.Bolt ? "Bolzen" : "Pfeile")}</color>\n");
                if (w.UsesAmmo) sb.Append($"<color={Muted}>Magazin {w.magazineSize}</color>\n");
                if (!string.IsNullOrEmpty(w.specialName)) sb.Append($"<color={Muted}>Spezial: {w.specialName}</color>\n");
            }
            if (d.IsContainer)
            {
                string what = d.containerKind == ContainerKind.Quiver ? "Köcher" : d.containerKind == ContainerKind.AmmoPouch ? "Munitionsbeutel" : "Tasche";
                sb.Append($"{what} mit {d.containerSlots} Plätzen\n");
                if (d.containerKind == ContainerKind.Quiver) sb.Append($"<color={Muted}>Fasst nur Pfeile</color>\n");
                if (d.containerKind == ContainerKind.AmmoPouch) sb.Append($"<color={Muted}>Fasst nur Bolzen</color>\n");
                if (d.rangedHaste > 0f) sb.Append($"<color={Green}>Ausrüstung: Erhöht das Fernkampftempo um {d.rangedHaste * 100f:0} %.</color>\n");
            }
            if (d.IsAmmo && d.ammoDps > 0f) sb.Append($"Fügt {d.ammoDps:0.#} Schaden pro Sekunde hinzu\n");
            if (d.armor > 0) sb.Append($"{d.armor} Rüstung\n");
            if (d.IsShield) sb.Append($"{d.BlockValue} Blockwert\n");

            foreach (StatModifier m in d.stats)
                if (m.value != 0 && m.stat != StatType.Armor) sb.Append($"{(m.value > 0 ? "+" : "")}{m.value} {StatName(m.stat)}\n");

            foreach (ItemEffect e in d.effects)
            {
                string t = EffectText(d, e);
                if (!string.IsNullOrEmpty(t)) sb.Append($"<color={Green}>{t}</color>\n");
            }

            if (d.teachesRecipe != null)
            {
                RecipeDefinition r = d.teachesRecipe;
                sb.Append($"<color={Green}>Benutzen: Lehrt {(r.output != null ? r.output.DisplayName : r.DisplayName)}.</color>\n");
                if (r.profession != null) sb.Append($"Benötigt {r.profession.DisplayName} ({r.requiredSkill})\n");
                if (r.reagents.Count > 0)
                {
                    sb.Append($"<color={Muted}>Zutaten: ");
                    for (int i = 0; i < r.reagents.Count; i++)
                    {
                        if (r.reagents[i].item == null) continue;
                        if (i > 0) sb.Append(", ");
                        sb.Append($"{r.reagents[i].count}× {r.reagents[i].item.DisplayName}");
                    }
                    sb.Append("</color>\n");
                }
            }

            // Anforderungen
            int level = who != null ? who.Level : 1;
            if (d.requiredLevel > 1)
                sb.Append(d.requiredLevel > level ? $"<color={Red}>Benötigt Stufe {d.requiredLevel}</color>\n" : $"Benötigt Stufe {d.requiredLevel}\n");
            if (d.allowedClasses != null && d.allowedClasses.Count > 0)
            {
                var names = new List<string>();
                foreach (CharacterClassDefinition c in d.allowedClasses) if (c != null) names.Add(c.DisplayName);
                bool ok = who != null && who.Class != null && d.allowedClasses.Contains(who.Class);
                sb.Append($"<color={(ok ? "#ffffff" : Red)}>Klassen: {string.Join(", ", names)}</color>\n");
            }
            else if (d.IsEquippable && who != null && who.Class != null)
            {
                string why = InventoryModel.ClassRestriction(d, who.Class);
                if (why != null) sb.Append($"<color={Red}>{why}</color>\n");
            }

            if (d.IsEquippable && d.appearance != null && d.appearance.placeholder && EquipmentVisualsSlot(d))
                sb.Append($"<color={Muted}><size=85%>Sichtbar am Charakter (Prototyp-Modell)</size></color>\n");
            if (!string.IsNullOrEmpty(d.Description)) sb.Append($"<color={Flavor}><i>\"{d.Description}\"</i></color>\n");
            if (d.IsStackable && stack.count > 0) sb.Append($"<color={Muted}>Stapel: {stack.count}/{d.maxStack}</color>\n");
            if (d.sellPrice > 0 && d.category != ItemCategory.Quest)
                sb.Append($"<color={Muted}>Verkaufswert:</color> {Money(d.sellPrice * Mathf.Max(1, stack.count))}\n");

            // Vergleich mit dem, was ersetzt wuerde
            if (!equipped && d.IsEquippable && compareWith != null)
            {
                string diff = Compare(d, compareWith);
                if (diff.Length > 0) sb.Append($"\n<color={Muted}>Wenn du es anlegst:</color>\n{diff}");
            }
            if (!equipped && d.IsEquippable) sb.Append($"\n<color={Muted}>Rechtsklick: anlegen</color>");
            if (!equipped && d.IsAmmo) sb.Append($"\n<color={Muted}>Rechtsklick: als Munition wählen</color>");
            else if (equipped) sb.Append($"\n<color={Muted}>Rechtsklick: ablegen</color>");
            else if (d.IsUsable) sb.Append($"\n<color={Muted}>Rechtsklick: benutzen</color>");
            if (!equipped && d.IsStackable && stack.count > 1) sb.Append($"\n<color={Muted}>Umschalt + Klick: aufteilen</color>");
            return sb.ToString().TrimEnd('\n');
        }

        private static bool EquipmentVisualsSlot(ItemDefinition d) =>
            d.equipType != EquipType.Neck && d.equipType != EquipType.Ring && d.equipType != EquipType.Trinket;

        public static string EffectText(ItemDefinition d, ItemEffect e)
        {
            if (!string.IsNullOrEmpty(e.text)) return (d.IsUsable ? "Benutzen: " : "Anlegen: ") + e.text;
            switch (e.type)
            {
                case ItemEffectType.DamagePercent: return $"Anlegen: +{e.value:0.#} % Schaden.";
                case ItemEffectType.CritChance: return $"Anlegen: +{e.value:0.#} % kritische Trefferchance.";
                case ItemEffectType.LifeOnHit: return $"Anlegen: Jeder Treffer heilt dich um {e.value:0} Leben.";
                case ItemEffectType.ArmorPercent: return $"Anlegen: +{e.value:0.#} % Rüstung.";
                case ItemEffectType.HealthPercent: return $"Anlegen: +{e.value:0.#} % maximales Leben.";
                case ItemEffectType.Heal: return $"Benutzen: Heilt sofort {e.value:0} Leben.";
                case ItemEffectType.HealOverTime: return $"Benutzen: Heilt {e.value:0} Leben über 10 Sek.";
                default: return "";
            }
        }

        /// <summary>Unterschiede (neu minus angelegt) in Werten, Ruestung und Schaden pro Sekunde.</summary>
        public static string Compare(ItemDefinition d, IList<ItemDefinition> current)
        {
            var sb = new StringBuilder();
            var cur = new EquipmentStats();
            float curDps = 0f;
            foreach (ItemDefinition c in current)
            {
                cur.Add(c);
                if (c != null && c.IsWeapon) curDps = Mathf.Max(curDps, Dps(c.weapon));
            }
            var next = new EquipmentStats();
            next.Add(d);

            if (d.IsWeapon)
            {
                float delta = Dps(d.weapon) - curDps;
                if (Mathf.Abs(delta) >= 0.05f) Diff(sb, delta, " Schaden pro Sekunde", true);
            }
            foreach (StatType t in new[] { StatType.Armor, StatType.Strength, StatType.Agility, StatType.Intellect, StatType.Stamina, StatType.Willpower })
            {
                int delta = next.Get(t) - cur.Get(t);
                if (delta != 0) Diff(sb, delta, " " + StatName(t), false);
            }
            if (Mathf.Abs(next.DamagePercent - cur.DamagePercent) > 0.01f) Diff(sb, next.DamagePercent - cur.DamagePercent, " % Schaden", true);
            if (Mathf.Abs(next.CritPercent - cur.CritPercent) > 0.01f) Diff(sb, next.CritPercent - cur.CritPercent, " % krit. Chance", true);
            if (Mathf.Abs(next.LifeOnHit - cur.LifeOnHit) > 0.01f) Diff(sb, next.LifeOnHit - cur.LifeOnHit, " Leben pro Treffer", false);
            if (Mathf.Abs(next.HealthPercent - cur.HealthPercent) > 0.01f) Diff(sb, next.HealthPercent - cur.HealthPercent, " % max. Leben", true);
            return sb.ToString();
        }

        private static void Diff(StringBuilder sb, float delta, string what, bool decimals)
        {
            string v = decimals ? delta.ToString("+0.0;-0.0") : delta.ToString("+0;-0");
            sb.Append($"<color={(delta > 0 ? Green : Red)}>{v}{what}</color>\n");
        }
    }
}
