using System.Collections;
using System.IO;
using System.Linq;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Weapons;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Kampfumbau (29./30.09., Max): Pfeil und Bogen exakt wie WoW (Fernkampfplatz, Munitionsplatz, Pfeile verbrauchen,
    /// Koecher mit Tempo, toter Winkel), Taschenplaetze, Feuerwaffen ruhen, zwei Waffen beim Aschenjaeger, Stab = Zauber,
    /// Ausweichrolle nur fuer den Aschenjaeger, passives Ausweichen (WoW Classic).
    /// Editor-Host Maxmod (Eidwaechter) + Test-Client Krisbot (fuer den Test Aschenjaegerin).
    /// Start: "Ashveil › Test › Fernkampf und Taschen (2 Spieler)". Bericht: Logs/Ashveil/Ashveil_FernkampfTest.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string RangedFlag = "fernkampf";
        private static bool _rangedMode;

        private IEnumerator RangedSuite()
        {
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(ArenaProfiles[0])) != null, 30f);
            Check(a != null, "Spieler A (Editor-Host) angemeldet");
            if (a == null) yield break;

            // ---------------------------------------------------------- Daten
            Line("");
            Line("[Daten: Feuerwaffen ruhen, Bogen/Pfeile/Koecher/Taschen wie WoW]");
            ItemDefinition bow = GameData.Item("item.hunters_shortbow"), arrow = GameData.Item("item.rough_arrow"),
                quiver = GameData.Item("item.light_quiver"), bag = GameData.Item("item.linen_bag"), staff = GameData.Item("item.ember_staff");
            Check(bow != null && bow.IsRangedWeapon && bow.weapon.archetype == WeaponArchetype.Projectile && bow.weapon.ammoType == AmmoType.Arrow &&
                  Mathf.Approximately(bow.weapon.minRange, 7.3f) && bow.FitsSlot(EquipmentSlot.Ranged) && !bow.FitsSlot(EquipmentSlot.MainHand),
                "Kurzbogen: Geschoss, braucht Pfeile, toter Winkel 7,3 m, nur im Fernkampfplatz");
            Check(arrow != null && arrow.IsAmmo && arrow.maxStack == 200 && arrow.ammoDps > 0f, "Grobe Pfeile: Munition, Stapel 200, Zusatzschaden");
            Check(quiver != null && quiver.IsContainer && quiver.containerKind == ContainerKind.Quiver && quiver.containerSlots == 6 && quiver.rangedHaste > 0.09f,
                "Leichter Köcher: 6 Plätze, nur Pfeile, +10 % Fernkampftempo");
            Check(bag != null && bag.IsContainer && bag.containerKind == ContainerKind.General && bag.containerSlots == 6, "Leinensack: Tasche mit 6 Plätzen");
            Check(staff != null && staff.weapon != null && staff.weapon.IsSpell && !staff.weapon.IsMelee && staff.weapon.HeldLikeMelee, "Glutstab ist eine Zauberwaffe (kein Nahkampf)");
            string[] guns = { "item.wheellock_pistol", "item.thunder_blunderbuss", "item.ash_hunter_rifle", "item.salt_lake_rifle" };
            bool gunInLoot = GameData.Database.All.OfType<LootTableDefinition>().Any(t => t.entries.Any(e => e.item != null && guns.Contains(e.item.Id)) ||
                                                               t.groups.Any(g => g.items.Any(e => e.item != null && guns.Contains(e.item.Id))));
            bool gunAtVendor = GameData.Database.All.OfType<NpcDefinition>().Any(n => n.stock != null && n.stock.Any(v => v.item != null && guns.Contains(v.item.Id)));
            bool gunInKit = GameData.Database.All.OfType<CharacterClassDefinition>().Any(c => c.startEquipment.Any(i => i != null && guns.Contains(i.Id)));
            Check(!gunInLoot && !gunAtVendor && !gunInKit, $"Feuerwaffen ruhen: nicht in Beute ({gunInLoot}), nicht beim Händler ({gunAtVendor}), nicht in Startausrüstung ({gunInKit})");

            CharacterClassDefinition stalker = GameData.Class("class.ashstalker"), warden = GameData.Class("class.warden");
            CharacterSaveData kit = CharacterStore.CreateNew("fernkampf-probe", stalker);
            var km = new InventoryModel { Class = stalker, Level = 1 };
            km.Load(kit.bag, kit.equipment, kit.containers, kit.ammoId);
            Check(km.GetEquipped(EquipmentSlot.Ranged).id == "item.hunters_shortbow" && km.GetEquipped(EquipmentSlot.MainHand).id == "item.cinder_dagger" &&
                  km.GetEquipped(EquipmentSlot.OffHand).id == "item.cinder_dagger" && km.Containers[0].id == "item.light_quiver" &&
                  km.AmmoId == "item.rough_arrow" && km.AmmoCount == 200 && km.KindOf(0) == ContainerKind.Quiver,
                $"Aschenjäger startet mit zwei Dolchen, Kurzbogen, Köcher und 200 Pfeilen im Köcher (Munition gewählt: {km.AmmoId}, {km.AmmoCount})");
            Check(stalker.canRoll && !warden.canRoll, "Ausweichrolle nur für den Aschenjäger");

            // ---------------------------------------------------------- Taschen (reine Logik)
            Line("");
            Line("[Taschenplätze]");
            int gen0 = km.GeneralCapacity;
            km.Add(new ItemStack("item.linen_bag", 1));
            int bagCell = Enumerable.Range(0, InventoryModel.BagSize).FirstOrDefault(i => km.Bag[i].id == "item.linen_bag");
            InvResult r = km.Move(bagCell, InventoryModel.ContainerCode(1));
            Check(r.Ok && km.Capacity(1) == 6 && km.GeneralCapacity == gen0 + 6, $"Leinensack in Taschenplatz 2: +6 Plätze ({gen0} → {km.GeneralCapacity})");
            int free = km.FirstFreeBagSlot();
            r = km.Move(InventoryModel.ContainerCode(0), free);
            Check(!r.Ok && km.Containers[0].id == "item.light_quiver", $"Voller Köcher lässt sich nicht ablegen („{r.Error}“)");
            int bread = Enumerable.Range(0, InventoryModel.BagSize).FirstOrDefault(i => km.Bag[i].id == "item.dry_bread");
            r = km.Move(bread, InventoryModel.CellCode(0, 5));
            Check(!r.Ok, $"In den Köcher passen nur Pfeile („{r.Error}“)");
            int before = km.AmmoCount;
            bool used = km.ConsumeAmmo();
            Check(used && km.AmmoCount == before - 1, "Ein Schuss verbraucht genau einen Pfeil");

            // ---------------------------------------------------------- Zweiter Spieler: Aschenjaegerin
            string exe = Ashveil.Core.ProjectPaths.TestClientExe;
            if (!File.Exists(exe)) { Check(false, "Test-Client vorhanden"); yield break; }
            StartClient(exe, ArenaProfiles[1]);
            NetworkInventory b = null;
            yield return WaitUntil(() => (b = OnlineInv(ArenaProfiles[1])) != null, 150f);
            Check(b != null, "Spieler B (Test-Client) angemeldet");
            if (b == null) yield break;
            yield return new WaitForSecondsRealtime(2f);

            b.ServerModel.Class = stalker;
            b.ServerModel.Load(kit.bag, kit.equipment, kit.containers, kit.ammoId);
            b.ServerSetLevel(10);
            yield return new WaitForSecondsRealtime(1.5f);
            var bw = b.GetComponent<WeaponUser>();
            var bm = b.GetComponent<CharacterMotor>();
            var am = a.GetComponent<CharacterMotor>();
            Check(bw.RangedWeapon != null && bw.RangedWeapon.Id == "weapon.hunters_shortbow" && bw.OffhandWeapon != null,
                $"Server: Krisbot führt Kurzbogen (Fernkampfplatz) und zweiten Dolch ({(bw.OffhandWeapon != null ? bw.OffhandWeapon.Id : "-")})");
            Check(bm != null && bm.CanRoll && am != null && !am.CanRoll, "Rolle: Krisbot (Aschenjägerin) darf, Maxmod (Eidwächter) nicht");
            yield return Ask(b, "ammo");
            Check(_last.ok && _last.data.Contains("arrows=200") && _last.data.Contains("dual=1"), $"Client: 200 Pfeile im Köcher, zwei Waffen gezogen bereit ({_last.data})");
            WeaponDefinition dagger = bw.MeleeWeapon;
            WeaponDefinition hit1 = WeaponUser.StrikeWeapon(dagger, bw.OffhandWeapon, AttackKind.Combo, 1, out float scale1);
            WeaponUser.StrikeWeapon(dagger, bw.OffhandWeapon, AttackKind.Combo, 0, out float scale0);
            Check(hit1 == bw.OffhandWeapon && Mathf.Approximately(scale1, 0.5f) && Mathf.Approximately(scale0, 1f), "Beidhändig: jeder zweite Schlag mit der Nebenhand, 50 % Schaden (WoW)");

            // ---------------------------------------------------------- Bogen im Kampf
            Line("");
            Line("[Bogen: automatischer Schuss, Pfeile, toter Winkel]");
            TrainingDummy dummy = FindObjectsByType<TrainingDummy>(FindObjectsSortMode.None).FirstOrDefault();
            Check(dummy != null, "Trainingspuppe gefunden");
            if (dummy == null) yield break;
            var dh = dummy.GetComponent<Health>();
            var ct = dummy.GetComponent<CombatTarget>();
            var dnob = dummy.GetComponent<FishNet.Object.NetworkObject>();
            Vector3 away = Vector3.ProjectOnPlane(Vector3.forward, Vector3.up);
            yield return Ask(b, "goto", V(dummy.transform.position + away * 15f, Quaternion.LookRotation(-away).eulerAngles.y));
            yield return new WaitForSecondsRealtime(1f);
            // B1 = Waffe wechseln (Standardleiste)
            yield return Ask(b, "bank-hold", "1");
            yield return Ask(b, "slot-press", "0");
            yield return Ask(b, "bank-hold", "0");
            yield return new WaitForSecondsRealtime(1.4f);
            yield return Ask(b, "ammo");
            Check(_last.data.Contains("ranged=1"), $"Krisbot nimmt den Bogen ({_last.data})");
            var bc = b.GetComponent<NetworkCombat>();
            int rej0 = bc.ServerRejected;
            int arrows0 = b.ServerModel.AmmoCount;
            float hp0 = dh.Current;
            yield return Ask(b, "attack", dnob.ObjectId + " keep");
            yield return new WaitForSecondsRealtime(7.5f);
            yield return Ask(b, "stop");
            yield return new WaitForSecondsRealtime(1.2f);
            int shots = arrows0 - b.ServerModel.AmmoCount;
            Check(shots >= 2 && shots <= 4, $"Automatischer Schuss im Waffentempo: {shots} Pfeile in 7,5 s (Tempo 2,6 s, Köcher +10 %)");
            Check(dh.Current < hp0 || dh.Current >= dh.Max, $"Pfeile treffen die Puppe (Leben {hp0:0} → {dh.Current:0})");
            yield return Ask(b, "ammo");
            Check(_last.data.Contains("arrows=" + b.ServerModel.AmmoCount), $"Client zählt dieselben Pfeile wie der Server ({b.ServerModel.AmmoCount}; {_last.data})");
            Check(bc.ServerRejected == rej0, $"Keine Schüsse vom Server verworfen ({bc.ServerRejected - rej0})");

            yield return Ask(b, "goto", V(dummy.transform.position + away * 4f, Quaternion.LookRotation(-away).eulerAngles.y));
            yield return new WaitForSecondsRealtime(1f);
            int arrows1 = b.ServerModel.AmmoCount;
            yield return Ask(b, "attack", dnob.ObjectId + " keep");
            yield return new WaitForSecondsRealtime(4f);
            yield return Ask(b, "stop");
            Check(b.ServerModel.AmmoCount == arrows1, $"Toter Winkel: auf 4 m kein Schuss ({arrows1 - b.ServerModel.AmmoCount} Pfeile)");

            // ---------------------------------------------------------- Passives Ausweichen (WoW Classic)
            Line("");
            Line("[Ausweichen im Kampf]");
            var ae = a.GetComponent<CharacterEquipment>();
            var ah = a.GetComponent<Health>();
            CharacterEquipment.DodgeEnabled = true;
            ae.DodgeRollOverride = 0f;
            float h0 = ah.Current;
            Vector3 fwd = a.transform.forward;
            ah.ApplyDamage(new DamageInfo { Amount = 10f, Direction = -fwd, HitPoint = a.transform.position + Vector3.up, Dodgeable = true });
            float h1 = ah.Current;
            ah.ApplyDamage(new DamageInfo { Amount = 10f, Direction = fwd, HitPoint = a.transform.position + Vector3.up, Dodgeable = true });
            float h2 = ah.Current;
            ah.ApplyDamage(new DamageInfo { Amount = 10f, Direction = -fwd, HitPoint = a.transform.position + Vector3.up, Dodgeable = false });
            float h3 = ah.Current;
            ae.DodgeRollOverride = -1f;
            CharacterEquipment.DodgeEnabled = false;
            Check(Mathf.Approximately(h0, h1) && h2 < h1 && h3 < h2, $"Nahkampf von vorn ausgewichen ({h0:0} → {h1:0}), von hinten getroffen (→ {h2:0}), Fernkampf nie ausweichbar (→ {h3:0})");
            Check(ae.DodgeChance >= CharacterEquipment.BaseDodgeChance, $"Ausweichchance {ae.DodgeChance * 100f:0.#} % (5 % + Geschick)");
            ah.Heal(ah.Max);

            b.ServerSetLevel(10);
            Line("");
        }
    }
}
