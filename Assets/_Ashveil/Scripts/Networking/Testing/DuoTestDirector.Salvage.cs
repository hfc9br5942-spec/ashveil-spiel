using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using FishNet.Object;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Klassen-Identitaet Phase 3 – Nachlese des Heilers (Prototyp §184: Grubenschrat, rechter Arm, ein Wurf).
    /// Editor-Host Maxmod (Eidwaechter) + Test-Client Krisbot (fuer den Test Aschenweber). Prueft: nur Heiler lernen sie,
    /// Wuerfel nach Zustand des Arms (intakt ≠ gebrochen ≠ abgetrennt), kein Wurf ohne echten Beitrag, Heilung zaehlt,
    /// normaler Loot unveraendert, gruenes Glimmen nur fuer den Heiler, Bergen mit Zauberleiste (Abbruch durch Laufen und
    /// Treffer), Meisterschaft mit Stufenabschlag, Leiche bleibt fuer die Nachlese liegen, Speichern.
    /// Start: "Ashveil › Test › Nachlese (2 Spieler)". Bericht: Logs/Ashveil/Ashveil_NachleseTest.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string SalvageFlag = "nachlese";
        private static bool _salvageMode;

        private const string Tissue = "item.salt_tissue", Sinew = "item.salt_sinew", Marrow = "item.salt_marrow", Claw = "item.brute_claw";

        private IEnumerator SalvageSuite()
        {
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(ArenaProfiles[0])) != null, 30f);
            Check(a != null, "Spieler A (Editor-Host) angemeldet");
            if (a == null) yield break;
            string exe = Ashveil.Core.ProjectPaths.TestClientExe;
            if (!File.Exists(exe)) { Check(false, "Test-Client vorhanden"); yield break; }
            StartClient(exe, ArenaProfiles[1]);
            NetworkInventory b = null;
            yield return WaitUntil(() => (b = OnlineInv(ArenaProfiles[1])) != null, 150f);
            Check(b != null, "Spieler B (Test-Client) angemeldet");
            if (b == null) yield break;
            yield return new WaitForSecondsRealtime(2f);

            NetworkProgression ap = a.GetComponent<NetworkProgression>(), bp = b.GetComponent<NetworkProgression>();
            NetworkMotion bn = b.GetComponent<NetworkMotion>();
            Health ah = a.GetComponent<Health>(), bh = b.GetComponent<Health>();
            CharacterClassDefinition oldClass = b.ServerModel.Class;
            CharacterClassDefinition weaver = GameData.Class("class.emberweaver");

            // ---------------------------------------------------------- Daten
            Line("");
            Line("[Daten]");
            EnemyDefinition def = GameData.Enemy("enemy.salt_brute");
            bool itemsOk = new[] { Tissue, Sinew, Marrow, Claw }.All(id => GameData.Item(id) != null);
            Check(def != null && def.salvage != null && def.salvage.Active && def.salvage.rolls == 1 && def.salvage.entries.Count == 4 && itemsOk,
                $"Grubenschrat hat ein Nachlese-Profil (Elite: 1 Wurf, 4 Stoffe, Gegenstaende vorhanden: {itemsOk})");
            if (def == null || !def.salvage.Active || !itemsOk)
            {
                Line("  → Menue „Ashveil › Kampf › Nachlese: Stoffe und Grubenschrat-Profil anlegen“ ausfuehren.");
                yield break;
            }

            // Reine Rechnung: 3 × 3000 Wuerfe je Zustand des rechten Arms
            var rng = new System.Random(4242);
            Dictionary<string, int> intact = Tally(def.salvage, BodyPartState.Intact, 0f, rng, 3000);
            Dictionary<string, int> broken = Tally(def.salvage, BodyPartState.Broken, 0f, rng, 3000);
            Dictionary<string, int> gone = Tally(def.salvage, BodyPartState.Destroyed, 0f, rng, 3000);
            Dictionary<string, int> brokenMaster = Tally(def.salvage, BodyPartState.Broken, MasteryCatalog.Salvage.Effect("rareChance", 100), rng, 3000);
            Check(Only(intact, Tissue, Sinew) && Only(broken, Tissue, Marrow) && Only(gone, Tissue, Claw),
                $"Intakt ≠ gebrochen ≠ abgetrennt: intakt {Show(intact)} · gebrochen {Show(broken)} · abgetrennt {Show(gone)}");
            float marrow0 = Share(broken, Marrow), marrow100 = Share(brokenMaster, Marrow);
            Check(marrow0 > 0.25f && marrow0 < 0.5f && marrow100 > marrow0 + 0.08f,
                $"Seltenes Salzmark: {marrow0 * 100:0} % ohne, {marrow100 * 100:0} % mit Nachlese Rang 100");

            // ---------------------------------------------------------- Freischalten
            Line("");
            Line("[Freischalten]");
            string whyA = ap.ServerUnlockSalvage();
            Check(whyA.Contains("Heiler") && !ap.ServerSalvageUnlocked, $"Maxmod (Eidwächter) lernt sie nicht: „{whyA}“");
            b.ServerModel.Class = weaver;
            b.ServerSetLevel(15);
            yield return new WaitForSecondsRealtime(1f);
            bp.ServerRemoveSalvage();
            bp.ServerSetMasteryPoints(MasteryCatalog.SalvageId, 0);
            int count0 = bp.ServerSalvageCount;
            string whyB = bp.ServerUnlockSalvage();
            yield return new WaitForSecondsRealtime(1f);
            yield return Ask(b, "salvage");
            Check(whyB == "" && bp.ServerSalvageUnlocked && _last.ok && _last.data.Contains("unlocked=1"),
                $"Krisbot ({weaver?.DisplayName}) lernt die Nachlese, ihr Client weiss es ({_last.data})");

            EnemySpawnArea area = UnityEngine.Object.FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None)
                .FirstOrDefault(x => x.Definition != null && x.Definition.Id == "enemy.salt_brute" && x.Prefab != null);
            Check(area != null, "Grubenschrat-Vorlage gefunden");
            if (area == null) { Restore(b, bp, oldClass); yield break; }
            Vector3 spot = Gnd(488f, 270f);
            Vector3 watch = spot + new Vector3(8f, 0f, 0f);
            watch.y = Ashveil.Core.WorldGround.SampleHeight(watch, watch.y) + 0.1f;
            bn.ServerTeleport(watch, 270f);   // Krisbot schaut zu – ihr Client muss die Leichen sehen
            yield return new WaitForSecondsRealtime(2.5f);

            // ---------------------------------------------------------- Ohne Beitrag keine Nachlese
            Line("");
            Line("[Kein echter Beitrag]");
            EnemyBrain brute = EnemyDirector.Instance.SpawnAt(area.Prefab, spot, 270f, 13, "", br => br.SetAggroProfile(AggroProfile.Passive), 120f);
            yield return new WaitForSecondsRealtime(2f);
            brute.Health.ApplyDamage(new DamageInfo { Amount = 5f, Source = b.gameObject, WeaponId = "test" });
            brute.Health.ApplyDamage(new DamageInfo { Amount = brute.Health.Max * 3f, Source = a.gameObject, WeaponId = "test" });
            yield return new WaitForSecondsRealtime(1.5f);
            CorpseLoot corpse = brute.GetComponent<CorpseLoot>();
            Check(corpse.IsRecipient(b.ProfileId) && !corpse.SalvageRolledFor(b.ProfileId) && bp.ServerSalvageNote.Contains("Beitrag zu klein"),
                $"5 Schaden reichen fuer die normale Beute, nicht fuer die Nachlese („{bp.ServerSalvageNote}“)");
            EnemyDirector.Instance.Remove(brute);
            yield return new WaitForSecondsRealtime(1f);

            // ---------------------------------------------------------- Arm gebrochen, Beitrag durch Heilen
            Line("");
            Line("[Heilen zaehlt, Arm gebrochen]");
            brute = EnemyDirector.Instance.SpawnAt(area.Prefab, spot, 270f, 13, "", br => br.SetAggroProfile(AggroProfile.Passive), 120f);
            yield return new WaitForSecondsRealtime(2f);
            int oid = brute.GetComponent<NetworkObject>().ObjectId;
            EnemyAnatomy ana = brute.Anatomy;
            LootEligibility elig = brute.GetComponent<LootEligibility>();
            brute.Health.ApplyDamage(new DamageInfo { Amount = ana.Get(PArm).Max, Source = a.gameObject, PartId = PArm, WeaponId = "test" });
            // P2 Balance: Elite-Leben aus der Tabelle – der noetige Beitrag (3 % des Lebens) waechst mit, also mehr heilen
            float healAmt = Mathf.Round(Mathf.Max(60f, brute.Health.Max * 0.03f / LootEligibility.HealWeight + 20f));
            ah.ApplyDamage(new DamageInfo { Amount = healAmt * 2f, Source = brute.gameObject, WeaponId = "test", Unblockable = true });
            ah.Heal(healAmt, b.gameObject);
            float contrib = elig.ContributionOf(b.ProfileId);
            Check(ana.StateOf(PArm) == BodyPartState.Broken && contrib >= healAmt * LootEligibility.HealWeight - 0.5f,
                $"Arm gebrochen (nicht abgetrennt); Krisbot heilt Maxmod um {healAmt:0} → Beitrag {contrib:0} (Heilung zaehlt 1,5-fach)");
            brute.Health.ApplyDamage(new DamageInfo { Amount = brute.Health.Max * 3f, Source = a.gameObject, WeaponId = "test" });
            yield return new WaitForSecondsRealtime(1.5f);
            ah.Heal(ah.Max, null);
            corpse = brute.GetComponent<CorpseLoot>();
            CorpseLoot.Salvage found = corpse.PeekSalvage(b.ProfileId);
            List<ItemStack> foundItems = found != null ? new List<ItemStack>(found.Items) : new List<ItemStack>();
            bool foundRare = found != null && found.Rare;
            bool foundOk = found != null && found.Items.Count > 0 && found.Items.All(s => s.id == Tissue || s.id == Marrow);
            Check(foundOk, $"Krisbot hat einen persoenlichen Fund aus dem gebrochenen Arm ({(found != null ? Join(found.Items) : "-")})");
            Check(!corpse.HasSalvageFor(a.ProfileId) && !corpse.SalvageRolledFor(a.ProfileId), "Maxmod (kein Heiler) bekommt keinen Nachlese-Wurf");
            List<ItemStack> lootB = corpse.Get(b.ProfileId);
            Check(Count(lootB, "item.red_vein_salt") >= 2 && Count(lootB, Tissue) == 0 && Count(lootB, Marrow) == 0,
                $"Normale Beute unveraendert, auch fuer Krisbot (Bonus fuer den Arm dabei, kein Nachlese-Stoff darin: {Join(lootB)})");

            yield return Ask(b, "salvagemark", oid.ToString());
            string markB = _last.data;
            var hostLi = brute.GetComponent<LootIndicator>();
            Check(markB.Contains("loot=1;salvage=1") && hostLi != null && !hostLi.SalvageAvailable,
                $"Nur Krisbot sieht, dass hier etwas zu bergen ist (B: {markB}; Maxmod: {(hostLi != null && hostLi.SalvageAvailable ? "gruen" : "nichts")})");

            // Normale Beute beider Spieler leeren: die Leiche bleibt trotzdem fuer die Nachlese liegen
            // (serverseitig geleert – Krisbots Test-Rucksack ist oft voll; es geht hier nur um die Leiche)
            Info($"Krisbots Rucksack: {b.ServerModel.FreeBagSlots} Plaetze frei");
            ClearLoot(corpse, a, b, brute.GetComponent<NetworkObject>());
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Ask(b, "salvagemark", oid.ToString());
            Check(corpse.ItemCount(b.ProfileId) == 0 && corpse.HasAnyLoot && _last.data.Contains("loot=0;salvage=1"),
                $"Beute leer – die Leiche bleibt fuer die Nachlese liegen und glimmt fuer Krisbot gruen ({_last.data})");

            // ---------------------------------------------------------- Bergen
            Line("");
            Line("[Bergen]");
            Vector3 corpsePos = brute.transform.position;
            bn.ServerTeleport(corpsePos + new Vector3(15f, 0f, 0f), 270f);
            yield return new WaitForSecondsRealtime(2f);
            yield return Ask(b, "salvage-do", oid.ToString());
            Check(!bp.ServerSalvaging && bp.ServerSalvageNote.Contains("Zu weit"), $"Aus 15 m geht es nicht („{bp.ServerSalvageNote}“)");

            Vector3 near = corpsePos + new Vector3(2f, 0f, 0f);
            near.y = Ashveil.Core.WorldGround.SampleHeight(near, near.y) + 0.1f;
            bn.ServerTeleport(near, 270f);
            yield return new WaitForSecondsRealtime(2f);
            yield return Ask(b, "salvage-do", oid.ToString());
            bool began = bp.ServerSalvaging && _last.data.Contains("castLabel=Bergen");
            yield return Ask(b, "walk", "0.8 1");
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Ask(b, "salvage");
            Check(began && !bp.ServerSalvaging && _last.data.Contains("interrupted=1") && corpse.HasSalvageFor(b.ProfileId),
                $"Bergen beginnt (Zauberleiste „Bergen“), Loslaufen bricht ab, der Fund bleibt liegen ({_last.data})");

            bn.ServerTeleport(near, 270f);
            yield return new WaitForSecondsRealtime(2f);
            yield return Ask(b, "salvage-do", oid.ToString());
            yield return new WaitForSecondsRealtime(0.3f);
            bh.ApplyDamage(new DamageInfo { Amount = 1f, Source = brute.gameObject, WeaponId = "test" });
            yield return new WaitForSecondsRealtime(0.3f);
            Check(!bp.ServerSalvaging && corpse.HasSalvageFor(b.ProfileId), "Ein Treffer bricht das Bergen ab");

            yield return new WaitForSecondsRealtime(0.5f);
            var bagBefore = foundItems.ToDictionary(s => s.id, s => b.ServerModel.Count(s.id));
            double pts0 = bp.ServerMasteryPoints(MasteryCatalog.SalvageId);
            float expect = bp.ServerSalvageSeconds();
            float t0 = Time.time;
            yield return Ask(b, "salvage-do", oid.ToString());
            yield return WaitUntil(() => !bp.ServerSalvaging, 6f);
            float took = Time.time - t0;
            yield return new WaitForSecondsRealtime(0.8f);
            bool inBag = foundItems.Count > 0 && foundItems.All(s => b.ServerModel.Count(s.id) >= bagBefore[s.id] + s.count);
            double gain = bp.ServerMasteryPoints(MasteryCatalog.SalvageId) - pts0;
            yield return Ask(b, "salvage");
            string state = _last.data;
            Check(inBag && !corpse.HasSalvageFor(b.ProfileId) && took > expect - 0.5f && took < expect + 0.9f,
                $"Nach {took:0.0} s (Soll {expect:0.0} s) geborgen: {Join(foundItems)} liegt im Rucksack");
            Check(bp.ServerSalvageCount == count0 + 1 && gain >= (foundRare ? 2.9 : 0.9) && state.Contains("count=" + (count0 + 1)) &&
                  foundItems.Count > 0 && state.Contains("last=" + foundItems[0].id),
                $"Statistik und Meisterschaft: {bp.ServerSalvageCount} Bergungen, Nachlese +{gain:0.#} ({(foundRare ? "selten, zaehlt dreifach" : "gewoehnlich")}); Client: {state}");
            yield return Ask(b, "salvagemark", oid.ToString());
            yield return new WaitForSecondsRealtime(0.3f);
            Check(_last.data.Contains("salvage=0") && !corpse.HasAnyLoot, $"Danach glimmt nichts mehr, die Leiche darf verschwinden ({_last.data})");
            yield return Ask(b, "salvage-do", oid.ToString());
            Check(!bp.ServerSalvaging && bp.ServerSalvageNote.Contains("nichts zu bergen"), $"Nur ein Wurf je Gegner („{bp.ServerSalvageNote}“)");
            EnemyDirector.Instance.Remove(brute);
            yield return new WaitForSecondsRealtime(1f);

            // ---------------------------------------------------------- Abgetrennt und schwacher Gegner
            Line("");
            Line("[Abgetrennt, schwacher Gegner]");
            brute = EnemyDirector.Instance.SpawnAt(area.Prefab, spot, 270f, 8, "", br => br.SetAggroProfile(AggroProfile.Passive), 120f);
            yield return new WaitForSecondsRealtime(2f);
            oid = brute.GetComponent<NetworkObject>().ObjectId;
            ana = brute.Anatomy;
            brute.Health.ApplyDamage(new DamageInfo { Amount = ana.Get(PArm).Max * 1.6f, Source = b.gameObject, PartId = PArm, WeaponId = "test" });
            BodyPartState armState = ana.StateOf(PArm);
            brute.Health.ApplyDamage(new DamageInfo { Amount = brute.Health.Max * 3f, Source = a.gameObject, WeaponId = "test" });
            yield return new WaitForSecondsRealtime(1.5f);
            corpse = brute.GetComponent<CorpseLoot>();
            found = corpse.PeekSalvage(b.ProfileId);
            Check(armState == BodyPartState.Destroyed && found != null && found.Items.All(s => s.id == Tissue || s.id == Claw),
                $"Abgetrennter Arm (Krisbot hat selbst zugeschlagen): Fund aus Gewebe/Klaue ({(found != null ? Join(found.Items) : "-")})");
            ClearLoot(corpse, a, b, brute.GetComponent<NetworkObject>());
            corpsePos = brute.transform.position;
            near = corpsePos + new Vector3(2f, 0f, 0f);
            near.y = Ashveil.Core.WorldGround.SampleHeight(near, near.y) + 0.1f;
            bn.ServerTeleport(near, 270f);
            yield return new WaitForSecondsRealtime(2f);
            pts0 = bp.ServerMasteryPoints(MasteryCatalog.SalvageId);
            int c1 = bp.ServerSalvageCount;
            yield return Ask(b, "salvage-do", oid.ToString());
            yield return WaitUntil(() => !bp.ServerSalvaging, 6f);
            yield return new WaitForSecondsRealtime(0.5f);
            Check(bp.ServerSalvageCount == c1 + 1 && bp.ServerMasteryPoints(MasteryCatalog.SalvageId) - pts0 < 0.001,
                $"Stufe 8 gegen Stufe 15: Fund ja, aber kein Lernwert (Nachlese +{bp.ServerMasteryPoints(MasteryCatalog.SalvageId) - pts0:0.##})");
            EnemyDirector.Instance.Remove(brute);

            // ---------------------------------------------------------- Speichern
            Line("");
            Line("[Speichern]");
            CharacterSaveDataCheck(b, bp);

            Restore(b, bp, oldClass);
        }

        private static void ClearLoot(CorpseLoot corpse, NetworkInventory a, NetworkInventory b, NetworkObject nob)
        {
            foreach (NetworkInventory who in new[] { a, b })
            {
                for (int guard = 0; corpse.ItemCount(who.ProfileId) > 0 && guard < 30; guard++) corpse.TakeInto(who.ProfileId, 0, st => st.count);
                who.NotifyLootAvailable(nob);
            }
        }

        private void CharacterSaveDataCheck(NetworkInventory b, NetworkProgression bp)
        {
            var d = b.ServerData;
            Check(d != null && d.signatures != null && d.signatures.Contains(NetworkProgression.SalvageSignature) && d.lifetime.salvages == bp.ServerSalvageCount &&
                  d.mastery.Any(m => m.id == MasteryCatalog.SalvageId),
                $"Speicherstand: Signatur „salvage“, {d?.lifetime.salvages} Bergungen, davon {d?.lifetime.rareSalvages} selten, Meisterschaft Nachlese");
        }

        private void Restore(NetworkInventory b, NetworkProgression bp, CharacterClassDefinition oldClass)
        {
            bp.ServerRemoveSalvage();
            if (oldClass != null) { b.ServerModel.Class = oldClass; b.ServerSetLevel(b.ServerModel.Level); }
            b.GetComponent<NetworkMotion>().ServerTeleportToSpawn();
        }

        private static Dictionary<string, int> Tally(SalvageProfile p, BodyPartState arm, float rareBonus, System.Random rng, int n)
        {
            var d = new Dictionary<string, int>();
            for (int i = 0; i < n; i++)
            {
                SalvageRoller.Result r = SalvageRoller.Roll(p, key => key == "arm_r" ? arm : (BodyPartState?)null, rareBonus, rng);
                foreach (ItemStack s in r.Items) d[s.id] = d.TryGetValue(s.id, out int c) ? c + 1 : 1;
            }
            return d;
        }

        private static bool Only(Dictionary<string, int> d, params string[] allowed) => d.Count > 0 && d.Keys.All(allowed.Contains);
        private static float Share(Dictionary<string, int> d, string id) => d.TryGetValue(id, out int c) ? c / (float)Math.Max(1, d.Values.Sum()) : 0f;
        private static string Show(Dictionary<string, int> d) =>
            string.Join("/", d.OrderByDescending(k => k.Value).Select(k => (GameData.Item(k.Key)?.DisplayName ?? k.Key) + " " + k.Value));
    }
}
