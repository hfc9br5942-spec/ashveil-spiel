using System;
using System.Collections.Generic;
using Ashveil.Data;

namespace Ashveil.Gameplay.Items
{
    /// <summary>Ergebnis einer Inventaraenderung.</summary>
    public readonly struct InvResult
    {
        public readonly bool Ok;
        public readonly string Error;

        private InvResult(bool ok, string error)
        {
            Ok = ok;
            Error = error;
        }

        public static readonly InvResult Success = new InvResult(true, null);
        public static InvResult Fail(string error) => new InvResult(false, error);
    }

    /// <summary>
    /// Rucksack + 4 Taschenplaetze (wie WoW) + Ausruestung + Munitionsplatz eines Charakters – reine Logik ohne
    /// Unity-Objekte. Laeuft auf dem Server als einzige Wahrheit; Clients bekommen Kopien zur Anzeige.
    ///
    /// Platz-Codes:
    ///   0..19                 Rucksack (fest)
    ///   20 + Tasche*20 + Platz Inhalt der Taschen 0..3 (Codes 20..99) (nur so viele Plaetze, wie die Tasche hat)
    ///   100 + EquipmentSlot   Ausruestung
    ///   150                   Munitionsplatz (waehlt die Sorte – die Pfeile bleiben in den Taschen, wie WoW)
    ///   200..203              Taschenplaetze (Tasche, Koecher, Munitionsbeutel)
    /// Regel: Jede Operation laesst die Gesamtmenge jedes Gegenstands unveraendert oder scheitert ganz –
    /// nichts wird verdoppelt, nichts verschwindet.
    /// </summary>
    public sealed class InventoryModel
    {
        public const int BackpackSize = 20;
        public const int BagSlotCount = 4;
        public const int MaxBagSlots = 20;
        /// <summary>Alle Taschenzellen (Rucksack + 4 Taschen je hoechstens 20) – Laenge von <see cref="Bag"/>.</summary>
        public const int BagSize = BackpackSize + BagSlotCount * MaxBagSlots;
        public const int EquipCount = 16;
        public const int EquipBase = 100;
        public const int AmmoCode = 150;
        public const int ContainerBase = 200;

        public readonly ItemStack[] Bag = new ItemStack[BagSize];
        public readonly ItemStack[] Equipment = new ItemStack[EquipCount];
        /// <summary>Die 4 Taschenplaetze (Taschen-Items).</summary>
        public readonly ItemStack[] Containers = new ItemStack[BagSlotCount];
        /// <summary>Gewaehlte Munitionssorte (Item-ID) oder leer.</summary>
        public string AmmoId = string.Empty;

        public CharacterClassDefinition Class;
        public int Level = 1;

        /// <summary>Wird nach jeder erfolgreichen Aenderung ausgeloest.</summary>
        public event Action Changed;
        /// <summary>Nur wenn sich die Ausruestung aenderte (Werte/Waffen neu berechnen).</summary>
        public event Action EquipmentChanged;

        private readonly Func<string, ItemDefinition> _resolve;

        public InventoryModel(Func<string, ItemDefinition> resolve = null)
        {
            _resolve = resolve ?? GameData.Item;
        }

        public ItemDefinition Def(in ItemStack s) => s.IsEmpty ? null : _resolve(s.id);

        // ================================================================== Codes

        /// <summary>Gueltiger Zellen-Code (Rucksack oder Taschenzelle – ob die Zelle gerade existiert: <see cref="Usable"/>).</summary>
        public static bool IsBag(int code) => code >= 0 && code < BagSize;
        public static bool IsEquip(int code) => code >= EquipBase && code < EquipBase + EquipCount;
        public static bool IsContainerSlot(int code) => code >= ContainerBase && code < ContainerBase + BagSlotCount;
        public static int EquipCode(EquipmentSlot s) => EquipBase + (int)s;
        public static EquipmentSlot SlotOf(int code) => (EquipmentSlot)(code - EquipBase);
        public static int ContainerCode(int bag) => ContainerBase + bag;
        /// <summary>Zelle: Tasche -1 = Rucksack.</summary>
        public static int CellCode(int bag, int slot) => bag < 0 ? slot : BackpackSize + bag * MaxBagSlots + slot;
        /// <summary>Zu welcher Tasche gehoert die Zelle (-1 = Rucksack)?</summary>
        public static int BagOf(int code) => code < BackpackSize ? -1 : (code - BackpackSize) / MaxBagSlots;
        public static int SlotInBag(int code) => code < BackpackSize ? code : (code - BackpackSize) % MaxBagSlots;

        /// <summary>Plaetze einer Tasche (-1 = Rucksack).</summary>
        public int Capacity(int bag)
        {
            if (bag < 0) return BackpackSize;
            if (bag >= BagSlotCount) return 0;
            ItemDefinition d = Def(Containers[bag]);
            return d != null && d.IsContainer ? Math.Min(MaxBagSlots, d.containerSlots) : 0;
        }

        public ContainerKind KindOf(int bag)
        {
            if (bag < 0) return ContainerKind.General;
            ItemDefinition d = Def(Containers[bag]);
            return d != null ? d.containerKind : ContainerKind.General;
        }

        /// <summary>Existiert die Zelle (Rucksack, oder Platz einer angelegten Tasche)?</summary>
        public bool Usable(int code) => IsBag(code) && SlotInBag(code) < Capacity(BagOf(code));

        /// <summary>Darf der Gegenstand in diese Zelle? (Koecher: nur Pfeile; Munitionsbeutel: nur Bolzen/Kugeln)</summary>
        public bool Accepts(int code, ItemDefinition def)
        {
            if (!Usable(code) || def == null) return false;
            switch (KindOf(BagOf(code)))
            {
                case ContainerKind.Quiver: return def.IsAmmo && def.ammoType == AmmoType.Arrow;
                case ContainerKind.AmmoPouch: return def.IsAmmo && def.ammoType == AmmoType.Bolt;
                default:
                    // Eine Tasche mit Inhalt kann nicht in eine andere gelegt werden – leere schon (sie liegt ja nicht an)
                    return true;
            }
        }

        public ItemStack Get(int code)
        {
            if (IsBag(code)) return Bag[code];
            if (IsEquip(code)) return Equipment[code - EquipBase];
            if (IsContainerSlot(code)) return Containers[code - ContainerBase];
            return ItemStack.Empty;
        }

        public ItemStack GetEquipped(EquipmentSlot slot) => Equipment[(int)slot];

        /// <summary>Zellen in der Reihenfolge, in der neue Gegenstaende einsortiert werden: passende Spezialtaschen zuerst
        /// (Pfeile in den Koecher), dann Rucksack, dann die uebrigen Taschen.</summary>
        private IEnumerable<int> FillOrder(ItemDefinition def)
        {
            if (def != null && def.IsAmmo)
                for (int b = 0; b < BagSlotCount; b++)
                    if (KindOf(b) != ContainerKind.General)
                        for (int s = 0; s < Capacity(b); s++) yield return CellCode(b, s);
            for (int s = 0; s < BackpackSize; s++) yield return s;
            for (int b = 0; b < BagSlotCount; b++)
                if (KindOf(b) == ContainerKind.General)
                    for (int s = 0; s < Capacity(b); s++) yield return CellCode(b, s);
        }

        /// <summary>Alle existierenden Zellen (Anzeige, Summen).</summary>
        public IEnumerable<int> Cells()
        {
            for (int s = 0; s < BackpackSize; s++) yield return s;
            for (int b = 0; b < BagSlotCount; b++)
                for (int s = 0; s < Capacity(b); s++) yield return CellCode(b, s);
        }

        // ================================================================== Laden

        public void Load(ItemStack[] bag, ItemStack[] equipment) => Load(bag, equipment, null, null);

        public void Load(ItemStack[] bag, ItemStack[] equipment, ItemStack[] containers, string ammoId)
        {
            for (int i = 0; i < BagSlotCount; i++)
                Containers[i] = containers != null && i < containers.Length && !containers[i].IsEmpty ? containers[i] : ItemStack.Empty;
            for (int i = 0; i < BagSize; i++) Bag[i] = bag != null && i < bag.Length && !bag[i].IsEmpty ? bag[i] : ItemStack.Empty;
            for (int i = 0; i < EquipCount; i++) Equipment[i] = equipment != null && i < equipment.Length && !equipment[i].IsEmpty ? equipment[i] : ItemStack.Empty;
            AmmoId = ammoId ?? string.Empty;
            Changed?.Invoke();
            EquipmentChanged?.Invoke();
        }

        // ================================================================== Abfragen

        /// <summary>Freie Zellen fuer normale Gegenstaende (Koecher zaehlen nicht).</summary>
        public int FreeBagSlots
        {
            get
            {
                int n = 0;
                foreach (int c in Cells()) if (Bag[c].IsEmpty && KindOf(BagOf(c)) == ContainerKind.General) n++;
                return n;
            }
        }

        /// <summary>Alle Zellen normaler Taschen (Anzeige "x/y frei").</summary>
        public int GeneralCapacity
        {
            get
            {
                int n = BackpackSize;
                for (int b = 0; b < BagSlotCount; b++) if (KindOf(b) == ContainerKind.General) n += Capacity(b);
                return n;
            }
        }

        public int FirstFreeBagSlot() => FirstFreeFor(null);

        /// <summary>Erste freie Zelle, die den Gegenstand aufnimmt (null = normale Zelle).</summary>
        public int FirstFreeFor(ItemDefinition def)
        {
            foreach (int c in FillOrder(def))
            {
                if (!Bag[c].IsEmpty) continue;
                if (def == null ? KindOf(BagOf(c)) == ContainerKind.General : Accepts(c, def)) return c;
            }
            return -1;
        }

        /// <summary>Gesamtmenge in Taschen, angelegt und in den Taschenplaetzen.</summary>
        public int Count(string id)
        {
            int n = 0;
            foreach (ItemStack s in Bag) if (!s.IsEmpty && s.id == id) n += s.count;
            foreach (ItemStack s in Equipment) if (!s.IsEmpty && s.id == id) n += s.count;
            foreach (ItemStack s in Containers) if (!s.IsEmpty && s.id == id) n += s.count;
            return n;
        }

        /// <summary>Wie viele Stueck passen noch dazu?</summary>
        public int SpaceFor(in ItemStack stack)
        {
            ItemDefinition def = Def(stack);
            if (def == null) return 0;
            if (def.unique && Count(stack.id) > 0) return 0;
            int max = Math.Max(1, def.maxStack);
            int space = 0;
            foreach (int c in Cells())
            {
                if (!Accepts(c, def)) continue;
                ItemStack s = Bag[c];
                if (s.IsEmpty) space += max;
                else if (s.SameKind(stack)) space += Math.Max(0, max - s.count);
            }
            if (def.unique) space = Math.Min(space, 1);
            return space;
        }

        /// <summary>Ist die Tasche im Taschenplatz leer?</summary>
        public bool BagEmpty(int bag)
        {
            for (int s = 0; s < MaxBagSlots; s++) if (!Bag[CellCode(bag, s)].IsEmpty) return false;
            return true;
        }

        // ================================================================== Munition (wie WoW)

        /// <summary>Munition der gewaehlten Sorte in allen Taschen.</summary>
        public int AmmoCount => string.IsNullOrEmpty(AmmoId) ? 0 : CountInBags(AmmoId);

        private int CountInBags(string id)
        {
            int n = 0;
            foreach (int c in Cells()) if (!Bag[c].IsEmpty && Bag[c].id == id) n += Bag[c].count;
            return n;
        }

        public ItemDefinition AmmoDef => string.IsNullOrEmpty(AmmoId) ? null : _resolve(AmmoId);

        /// <summary>Koecher-Tempo (%) des angelegten Koechers bzw. Munitionsbeutels – nur einer zaehlt (WoW).</summary>
        public float RangedHaste
        {
            get
            {
                float best = 0f;
                foreach (ItemStack s in Containers)
                {
                    ItemDefinition d = Def(s);
                    if (d != null && d.containerKind != ContainerKind.General) best = Math.Max(best, d.rangedHaste);
                }
                return best;
            }
        }

        /// <summary>Einen Schuss Munition verbrauchen (Koecher zuerst, jeweils vom kleinsten Stapel). false = keine mehr.</summary>
        public bool ConsumeAmmo() => ConsumeAmmoCell() >= 0;

        /// <summary>Wie <see cref="ConsumeAmmo"/>, liefert aber die Zelle (fuer eine kleine Netzmeldung statt des ganzen Inventars); -1 = leer.</summary>
        public int ConsumeAmmoCell()
        {
            if (string.IsNullOrEmpty(AmmoId)) return -1;
            int best = -1;
            bool bestSpecial = false;
            foreach (int c in Cells())
            {
                ItemStack s = Bag[c];
                if (s.IsEmpty || s.id != AmmoId) continue;
                bool special = KindOf(BagOf(c)) != ContainerKind.General;
                if (best < 0 || (special && !bestSpecial) || (special == bestSpecial && s.count < Bag[best].count)) { best = c; bestSpecial = special; }
            }
            if (best < 0) return -1;
            Bag[best] = Bag[best].WithCount(Bag[best].count - 1);
            Changed?.Invoke();
            return best;
        }

        // ================================================================== Hinzufuegen / Entfernen

        /// <summary>
        /// Legt so viel wie moeglich ab (erst auf passende Stapel, dann in freie Zellen; Pfeile zuerst in den Koecher).
        /// Gibt die aufgenommene Menge zurueck – der Rest bleibt beim Aufrufer (z. B. in der Leiche).
        /// </summary>
        public int Add(ItemStack stack)
        {
            if (stack.IsEmpty) return 0;
            ItemDefinition def = Def(stack);
            if (def == null) return 0;
            int want = Math.Min(stack.count, SpaceFor(stack));
            if (want <= 0) return 0;
            int max = Math.Max(1, def.maxStack);
            int left = want;
            foreach (int i in FillOrder(def))
            {
                if (left <= 0) break;
                if (!Accepts(i, def) || !Bag[i].SameKind(stack) || Bag[i].count >= max) continue;
                int put = Math.Min(left, max - Bag[i].count);
                Bag[i] = Bag[i].WithCount(Bag[i].count + put);
                left -= put;
            }
            foreach (int i in FillOrder(def))
            {
                if (left <= 0) break;
                if (!Bag[i].IsEmpty || !Accepts(i, def)) continue;
                int put = Math.Min(left, max);
                Bag[i] = new ItemStack(stack.id, put, stack.bound);
                left -= put;
            }
            Changed?.Invoke();
            return want - left;
        }

        /// <summary>Entfernt Menge aus einer Zelle (Benutzen, Herstellen).</summary>
        public InvResult RemoveAt(int bagIndex, int count)
        {
            if (!IsBag(bagIndex) || Bag[bagIndex].IsEmpty) return InvResult.Fail("Dort liegt nichts.");
            if (count <= 0 || count > Bag[bagIndex].count) return InvResult.Fail("Ungueltige Menge.");
            Bag[bagIndex] = Bag[bagIndex].WithCount(Bag[bagIndex].count - count);
            Changed?.Invoke();
            return InvResult.Success;
        }

        // ================================================================== Pruefungen

        /// <summary>Darf dieser Charakter das Item in diesen Platz legen? null = ja, sonst Grund.</summary>
        public string CanEquip(ItemDefinition def, EquipmentSlot slot)
        {
            if (def == null) return "Unbekannter Gegenstand.";
            if (!def.IsEquippable || def.equipType == EquipType.Bag) return "Das kann man nicht anlegen.";
            if (!def.FitsSlot(slot)) return "Passt nicht in diesen Platz.";
            if (def.requiredLevel > Level) return $"Benötigt Stufe {def.requiredLevel}.";
            string cls = ClassRestriction(def, Class);
            if (cls != null) return cls;
            if (slot == EquipmentSlot.OffHand && def.IsWeapon && def.Hand == WeaponHand.OneHand && def.weapon.IsMelee &&
                (Class == null || !Class.dualWield))
                return "Deine Klasse kann keine zwei Nahkampfwaffen führen.";
            return null;
        }

        /// <summary>Klassenpruefung ohne Platz/Stufe (auch fuer Tooltip und Beute-Vorlieben).</summary>
        public static string ClassRestriction(ItemDefinition def, CharacterClassDefinition cls)
        {
            if (def == null || !def.IsEquippable) return null;
            if (def.allowedClasses != null && def.allowedClasses.Count > 0)
            {
                if (cls == null || !def.allowedClasses.Contains(cls)) return "Deine Klasse kann das nicht tragen.";
                return null;
            }
            if (cls == null) return null;
            if (def.IsWeapon && !cls.CanUseWeapon(def.weapon.weaponClass)) return "Deine Klasse kann diese Waffenart nicht führen.";
            if (def.armorType != ArmorType.None && !cls.CanWear(def.armorType)) return "Deine Klasse kann diese Rüstungsart nicht tragen.";
            return null;
        }

        public static bool UsableByClass(ItemDefinition def, CharacterClassDefinition cls) => ClassRestriction(def, cls) == null;

        // ================================================================== Verschieben

        /// <summary>Zieht den Inhalt von <paramref name="from"/> auf <paramref name="to"/> (Drag &amp; Drop).</summary>
        public InvResult Move(int from, int to)
        {
            if (from == to) return InvResult.Success;
            // Munitionsplatz: Sorte waehlen (die Pfeile bleiben, wo sie sind) bzw. abwaehlen
            if (to == AmmoCode) return IsBag(from) ? SetAmmo(from) : InvResult.Fail("Ungueltiger Platz.");
            if (from == AmmoCode) { AmmoId = string.Empty; Changed?.Invoke(); return InvResult.Success; }
            if (IsContainerSlot(from) || IsContainerSlot(to)) return MoveContainer(from, to);

            bool fromBag = IsBag(from), toBag = IsBag(to);
            if ((!fromBag && !IsEquip(from)) || (!toBag && !IsEquip(to))) return InvResult.Fail("Ungueltiger Platz.");
            if (fromBag && !Usable(from)) return InvResult.Fail("Ungueltiger Platz.");
            if (toBag && !Usable(to)) return InvResult.Fail("Dort ist kein Platz.");
            ItemStack a = Get(from);
            if (a.IsEmpty) return InvResult.Fail("Dort liegt nichts.");

            if (fromBag && toBag) return BagToBag(from, to);
            if (fromBag) return Equip(from, SlotOf(to));
            if (toBag) return UnequipTo(SlotOf(from), to);
            return EquipToEquip(SlotOf(from), SlotOf(to));
        }

        private InvResult BagToBag(int from, int to)
        {
            ItemStack a = Bag[from], b = Bag[to];
            ItemDefinition da = Def(a), db = Def(b);
            if (!Accepts(to, da)) return InvResult.Fail(KindOf(BagOf(to)) == ContainerKind.Quiver ? "In den Köcher passen nur Pfeile." : "Das passt dort nicht hinein.");
            if (!b.IsEmpty && !a.SameKind(b) && !Accepts(from, db)) return InvResult.Fail("Das passt dort nicht hinein.");
            if (b.IsEmpty)
            {
                Bag[to] = a;
                Bag[from] = ItemStack.Empty;
            }
            else if (a.SameKind(b))
            {
                int max = da != null ? Math.Max(1, da.maxStack) : 1;
                int put = Math.Min(a.count, max - b.count);
                if (put <= 0)
                {
                    Bag[from] = b;
                    Bag[to] = a;
                }
                else
                {
                    Bag[to] = b.WithCount(b.count + put);
                    Bag[from] = a.WithCount(a.count - put);
                }
            }
            else
            {
                Bag[from] = b;
                Bag[to] = a;
            }
            Changed?.Invoke();
            return InvResult.Success;
        }

        /// <summary>Teilt einen Stapel: <paramref name="amount"/> Stueck nach <paramref name="to"/> (leer oder gleicher Stapel).</summary>
        public InvResult Split(int from, int amount, int to)
        {
            if (!IsBag(from) || !IsBag(to) || from == to || !Usable(from) || !Usable(to)) return InvResult.Fail("Ungueltiger Platz.");
            ItemStack a = Bag[from];
            if (a.IsEmpty || amount <= 0 || amount >= a.count) return InvResult.Fail("Ungueltige Menge.");
            ItemDefinition def = Def(a);
            if (!Accepts(to, def)) return InvResult.Fail("Das passt dort nicht hinein.");
            ItemStack b = Bag[to];
            if (b.IsEmpty)
            {
                Bag[to] = a.WithCount(amount);
            }
            else if (a.SameKind(b))
            {
                int max = def != null ? Math.Max(1, def.maxStack) : 1;
                if (b.count + amount > max) return InvResult.Fail("Der Stapel ist zu voll.");
                Bag[to] = b.WithCount(b.count + amount);
            }
            else return InvResult.Fail("Der Platz ist belegt.");
            Bag[from] = a.WithCount(a.count - amount);
            Changed?.Invoke();
            return InvResult.Success;
        }

        // ================================================================== Taschen

        /// <summary>Tasche anlegen, ablegen oder zwei Taschenplaetze tauschen.</summary>
        private InvResult MoveContainer(int from, int to)
        {
            if (IsContainerSlot(from) && IsContainerSlot(to))
            {
                int a = from - ContainerBase, b = to - ContainerBase;
                if (!BagEmpty(a) || !BagEmpty(b)) return InvResult.Fail("Leere die Taschen zuerst.");
                (Containers[a], Containers[b]) = (Containers[b], Containers[a]);
                Changed?.Invoke();
                return InvResult.Success;
            }
            if (IsBag(from) && IsContainerSlot(to)) return EquipBag(from, to - ContainerBase);
            if (IsContainerSlot(from) && IsBag(to)) return UnequipBag(from - ContainerBase, to);
            return InvResult.Fail("Ungueltiger Platz.");
        }

        /// <summary>Tasche aus einer Zelle in einen Taschenplatz. Eine dort liegende (leere) Tasche wandert in die Zelle.</summary>
        public InvResult EquipBag(int cell, int bag)
        {
            if (!Usable(cell) || bag < 0 || bag >= BagSlotCount) return InvResult.Fail("Ungueltiger Platz.");
            ItemStack item = Bag[cell];
            ItemDefinition def = Def(item);
            if (def == null || !def.IsContainer) return InvResult.Fail("Das ist keine Tasche.");
            if (BagOf(cell) == bag) return InvResult.Fail("Eine Tasche kann nicht in sich selbst liegen.");
            if (!Containers[bag].IsEmpty && !BagEmpty(bag)) return InvResult.Fail("Leere die Tasche zuerst.");
            if (def.containerKind != ContainerKind.General)
                for (int b = 0; b < BagSlotCount; b++)
                {
                    ItemDefinition o = b == bag ? null : Def(Containers[b]);
                    if (o != null && o.containerKind != ContainerKind.General) return InvResult.Fail("Du kannst nur einen Köcher oder Munitionsbeutel tragen.");
                }
            ItemStack old = Containers[bag];
            if (def.bind == BindType.OnEquip || def.bind == BindType.OnPickup) item.bound = true;
            Containers[bag] = item;
            Bag[cell] = old;
            Changed?.Invoke();
            return InvResult.Success;
        }

        /// <summary>Leere Tasche aus dem Taschenplatz in eine (andere) Zelle.</summary>
        public InvResult UnequipBag(int bag, int cell)
        {
            if (bag < 0 || bag >= BagSlotCount || Containers[bag].IsEmpty) return InvResult.Fail("Dort liegt nichts.");
            if (!BagEmpty(bag)) return InvResult.Fail("Leere die Tasche zuerst.");
            if (!Usable(cell) || BagOf(cell) == bag) return InvResult.Fail("Ungueltiger Platz.");
            ItemStack there = Bag[cell];
            ItemDefinition td = Def(there);
            if (!there.IsEmpty)
            {
                if (td == null || !td.IsContainer) return InvResult.Fail("Der Platz ist belegt.");
                return EquipBag(cell, bag); // tauscht
            }
            if (!Accepts(cell, Def(Containers[bag]))) return InvResult.Fail("Das passt dort nicht hinein.");
            Bag[cell] = Containers[bag];
            Containers[bag] = ItemStack.Empty;
            Changed?.Invoke();
            return InvResult.Success;
        }

        private InvResult SetAmmo(int cell)
        {
            ItemDefinition def = Def(Bag[cell]);
            if (def == null || !def.IsAmmo) return InvResult.Fail("Das ist keine Munition.");
            ItemDefinition ranged = Def(Equipment[(int)EquipmentSlot.Ranged]);
            if (ranged != null && ranged.IsWeapon && ranged.weapon.ammoType != AmmoType.None && ranged.weapon.ammoType != def.ammoType)
                return InvResult.Fail(ranged.weapon.ammoType == AmmoType.Arrow ? "Dein Bogen verschießt nur Pfeile." : "Deine Armbrust verschießt nur Bolzen.");
            if (def.requiredLevel > Level) return InvResult.Fail($"Benötigt Stufe {def.requiredLevel}.");
            AmmoId = Bag[cell].id;
            Changed?.Invoke();
            EquipmentChanged?.Invoke();
            return InvResult.Success;
        }

        // ================================================================== Ausruesten

        /// <summary>Rechtsklick: passenden Platz selbst waehlen (Taschen in einen freien Taschenplatz, Munition als Sorte).</summary>
        public InvResult AutoEquip(int bagIndex)
        {
            if (!Usable(bagIndex) || Bag[bagIndex].IsEmpty) return InvResult.Fail("Dort liegt nichts.");
            ItemDefinition def = Def(Bag[bagIndex]);
            if (def != null && def.IsAmmo) return SetAmmo(bagIndex);
            if (def != null && def.IsContainer)
            {
                for (int b = 0; b < BagSlotCount; b++) if (Containers[b].IsEmpty) return EquipBag(bagIndex, b);
                return InvResult.Fail("Alle Taschenplätze sind belegt.");
            }
            if (def == null || !def.IsEquippable) return InvResult.Fail("Das kann man nicht anlegen.");
            return Equip(bagIndex, BestSlot(def));
        }

        public EquipmentSlot BestSlot(ItemDefinition def)
        {
            switch (def.equipType)
            {
                case EquipType.Ring:
                    return Equipment[(int)EquipmentSlot.Ring1].IsEmpty || !Equipment[(int)EquipmentSlot.Ring2].IsEmpty
                        ? EquipmentSlot.Ring1 : EquipmentSlot.Ring2;
                case EquipType.Trinket:
                    return Equipment[(int)EquipmentSlot.Trinket1].IsEmpty || !Equipment[(int)EquipmentSlot.Trinket2].IsEmpty
                        ? EquipmentSlot.Trinket1 : EquipmentSlot.Trinket2;
                case EquipType.Weapon:
                    if (def.IsRangedWeapon) return EquipmentSlot.Ranged;
                    switch (def.Hand)
                    {
                        case WeaponHand.OffHandOnly: return EquipmentSlot.OffHand;
                        case WeaponHand.TwoHand:
                        case WeaponHand.MainHandOnly: return EquipmentSlot.MainHand;
                        default:
                            ItemDefinition main = Def(Equipment[(int)EquipmentSlot.MainHand]);
                            if (Class != null && Class.dualWield && main != null && !main.IsTwoHanded &&
                                Equipment[(int)EquipmentSlot.OffHand].IsEmpty)
                                return EquipmentSlot.OffHand;
                            return EquipmentSlot.MainHand;
                    }
                case EquipType.Head: return EquipmentSlot.Head;
                case EquipType.Shoulders: return EquipmentSlot.Shoulders;
                case EquipType.Chest: return EquipmentSlot.Chest;
                case EquipType.Hands: return EquipmentSlot.Hands;
                case EquipType.Legs: return EquipmentSlot.Legs;
                case EquipType.Feet: return EquipmentSlot.Feet;
                case EquipType.Belt: return EquipmentSlot.Belt;
                case EquipType.Cloak: return EquipmentSlot.Cloak;
                default: return EquipmentSlot.Neck;
            }
        }

        /// <summary>
        /// Legt Zelle <paramref name="bagIndex"/> in <paramref name="slot"/> an. Verdraengte Teile wandern in die
        /// frei werdende Zelle bzw. freie Zellen. Zweihand: Nebenhand wird mit abgelegt; Nebenhand zu Zweihand:
        /// die Zweihandwaffe wird abgelegt. Reicht der Platz nicht, passiert nichts.
        /// </summary>
        public InvResult Equip(int bagIndex, EquipmentSlot slot)
        {
            if (!Usable(bagIndex)) return InvResult.Fail("Ungueltiger Platz.");
            ItemStack item = Bag[bagIndex];
            if (item.IsEmpty) return InvResult.Fail("Dort liegt nichts.");
            ItemDefinition def = Def(item);
            string why = CanEquip(def, slot);
            if (why != null) return InvResult.Fail(why);

            var displaced = new List<EquipmentSlot>(2);
            if (def.IsTwoHanded && slot == EquipmentSlot.MainHand)
            {
                if (!Equipment[(int)EquipmentSlot.MainHand].IsEmpty) displaced.Add(EquipmentSlot.MainHand);
                if (!Equipment[(int)EquipmentSlot.OffHand].IsEmpty) displaced.Add(EquipmentSlot.OffHand);
            }
            else
            {
                if (!Equipment[(int)slot].IsEmpty) displaced.Add(slot);
                if (slot == EquipmentSlot.OffHand)
                {
                    ItemDefinition main = Def(Equipment[(int)EquipmentSlot.MainHand]);
                    if (main != null && main.IsTwoHanded) displaced.Add(EquipmentSlot.MainHand);
                }
            }

            // Platz: die Zelle des angelegten Teils wird frei (wenn sie das Verdraengte aufnimmt), dazu alle freien normalen
            bool cellTakesFirst = displaced.Count == 0 || Accepts(bagIndex, Def(Equipment[(int)displaced[0]]));
            if (displaced.Count > FreeBagSlots + (cellTakesFirst ? 1 : 0)) return InvResult.Fail("Nicht genug Platz im Inventar.");

            Bag[bagIndex] = ItemStack.Empty;
            var moving = new List<ItemStack>(displaced.Count);
            foreach (EquipmentSlot d in displaced)
            {
                moving.Add(Equipment[(int)d]);
                Equipment[(int)d] = ItemStack.Empty;
            }
            if (def.bind == BindType.OnEquip || def.bind == BindType.OnPickup) item.bound = true;
            Equipment[(int)slot] = item;

            for (int i = 0; i < moving.Count; i++)
            {
                int target = i == 0 && cellTakesFirst ? bagIndex : FirstFreeBagSlot();
                Bag[target] = moving[i];
            }
            Changed?.Invoke();
            EquipmentChanged?.Invoke();
            return InvResult.Success;
        }

        /// <summary>Legt ab – in <paramref name="bagIndex"/> (leer) oder tauscht mit einem dort passenden Teil.</summary>
        public InvResult UnequipTo(EquipmentSlot slot, int bagIndex)
        {
            ItemStack worn = Equipment[(int)slot];
            if (worn.IsEmpty) return InvResult.Fail("Dort liegt nichts.");
            if (!Usable(bagIndex)) return InvResult.Fail("Ungueltiger Platz.");
            if (!Accepts(bagIndex, Def(worn))) return InvResult.Fail("Das passt dort nicht hinein.");
            ItemStack there = Bag[bagIndex];
            if (!there.IsEmpty)
            {
                ItemDefinition d = Def(there);
                if (d == null || !d.FitsSlot(slot)) return InvResult.Fail("Der Platz ist belegt.");
                return Equip(bagIndex, slot); // tauscht: angelegtes Teil landet in bagIndex
            }
            Equipment[(int)slot] = ItemStack.Empty;
            Bag[bagIndex] = worn;
            Changed?.Invoke();
            EquipmentChanged?.Invoke();
            return InvResult.Success;
        }

        /// <summary>Rechtsklick auf ein angelegtes Teil: in die erste freie Zelle.</summary>
        public InvResult Unequip(EquipmentSlot slot)
        {
            int free = FirstFreeBagSlot();
            if (free < 0) return InvResult.Fail("Kein Platz im Inventar.");
            return UnequipTo(slot, free);
        }

        private InvResult EquipToEquip(EquipmentSlot a, EquipmentSlot b)
        {
            ItemStack sa = Equipment[(int)a], sb = Equipment[(int)b];
            ItemDefinition da = Def(sa), db = Def(sb);
            string why = CanEquip(da, b);
            if (why != null) return InvResult.Fail(why);
            if (db != null)
            {
                why = CanEquip(db, a);
                if (why != null) return InvResult.Fail(why);
            }
            if (da != null && da.IsTwoHanded) return InvResult.Fail("Passt nicht in diesen Platz.");
            Equipment[(int)a] = sb;
            Equipment[(int)b] = sa;
            Changed?.Invoke();
            EquipmentChanged?.Invoke();
            return InvResult.Success;
        }

        // ================================================================== Hilfen

        /// <summary>Summe aller Mengen je ID (Taschen + Ausruestung + Taschenplaetze) – fuer Pruefungen "nichts verloren, nichts doppelt".</summary>
        public Dictionary<string, int> Totals()
        {
            var d = new Dictionary<string, int>();
            void Add(ItemStack s) { if (!s.IsEmpty) d[s.id] = (d.TryGetValue(s.id, out int n) ? n : 0) + s.count; }
            foreach (ItemStack s in Bag) Add(s);
            foreach (ItemStack s in Equipment) Add(s);
            foreach (ItemStack s in Containers) Add(s);
            return d;
        }

        public void RaiseChanged() => Changed?.Invoke();
    }
}
