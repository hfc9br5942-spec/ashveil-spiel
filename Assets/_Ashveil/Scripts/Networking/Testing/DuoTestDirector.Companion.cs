using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Companions;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using FishNet.Object;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Klassen-Identitaet Phase 4 – Dominator-Gefaehrte (Prototyp §185: Aschwolf – Dominieren, Folgen, Angreifen,
    /// Signatur, Bindung). Editor-Host Maxmod (Eidwaechter) + Test-Client Krisbot (fuer den Test Aschenjaegerin).
    /// Start: "Ashveil › Test › Gefährte (2 Spieler)". Bericht: Logs/Ashveil/Ashveil_GefaehrteTest.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string CompanionFlag = "gefaehrte";
        private static bool _companionMode;

        private IEnumerator CompanionSuite()
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
            Health bh = b.GetComponent<Health>();
            CharacterClassDefinition oldClass = b.ServerModel.Class;
            var spawned = new List<EnemyBrain>();

            // ---------------------------------------------------------- Daten
            Line("");
            Line("[Daten]");
            EnemyDefinition wolfDef = GameData.Enemy("enemy.ash_wolf"), bruteDef = GameData.Enemy("enemy.salt_brute");
            GameObject petPrefab = Resources.Load<GameObject>("Companions/Companion_Aschwolf");
            Check(wolfDef != null && wolfDef.dominatable && bruteDef != null && !bruteDef.dominatable && CompanionCatalog.ForEnemy("enemy.ash_wolf") != null && petPrefab != null &&
                  petPrefab.GetComponent<NetworkCompanion>() != null && petPrefab.GetComponent<EnemyBrain>() == null,
                "Aschwolf dominierbar, Grubenschrat nicht; Gefährten-Vorlage ohne Gegner-KI vorhanden");
            if (petPrefab == null || wolfDef == null || !wolfDef.dominatable)
            {
                Line("  → Menü „Ashveil › Kampf › Begleiter: Aschwolf-Gefährte bauen“ ausführen.");
                yield break;
            }
            EnemySpawnArea wolfArea = Object.FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None).FirstOrDefault(x => x.Definition != null && x.Definition.Id == "enemy.ash_wolf" && x.Prefab != null);
            EnemySpawnArea bruteArea = Object.FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None).FirstOrDefault(x => x.Definition != null && x.Definition.Id == "enemy.salt_brute" && x.Prefab != null);
            Check(wolfArea != null && bruteArea != null, "Vorlagen für Aschwolf und Grubenschrat gefunden");
            if (wolfArea == null || bruteArea == null) yield break;

            // ---------------------------------------------------------- Freischalten
            Line("");
            Line("[Freischalten]");
            string whyA = ap.ServerUnlockDominate();
            Check(whyA.Contains("Aschenjäger") && !ap.ServerCanDominate, $"Maxmod (Eidwächter) kann es nicht lernen: „{whyA}“");
            b.ServerModel.Class = GameData.Class(CompanionCatalog.DominatorClassId);
            b.ServerSetLevel(15);
            bp.ServerRemoveDominate();
            yield return new WaitForSecondsRealtime(1f);
            string whyB = bp.ServerUnlockDominate();
            yield return new WaitForSecondsRealtime(1f);
            var bar = b.GetComponent<NetworkActionBar>();
            bool domOnBar = bar != null && bar.ServerSlots.Any(e => e.type == ActionEntryType.Companion && e.referenceId == CompanionCatalog.DominateId);
            yield return Ask(b, "companion");
            Check(whyB == "" && bp.ServerCanDominate && domOnBar && _last.data.Contains("can=1;species=;out=0"),
                $"Krisbot (Aschenjägerin) lernt Dominieren, „Dominieren“ liegt auf der Leiste ({_last.data})");

            Vector3 spot = Gnd(488f, 270f);
            Vector3 stand = spot + new Vector3(8f, 0f, 0f);
            stand.y = Ashveil.Core.WorldGround.SampleHeight(stand, stand.y) + 0.1f;
            bn.ServerTeleport(stand, 270f);
            yield return new WaitForSecondsRealtime(2.5f);

            // ---------------------------------------------------------- Dominieren
            Line("");
            Line("[Dominieren]");
            EnemyBrain wild = EnemyDirector.Instance.SpawnAt(wolfArea.Prefab, stand + new Vector3(-4f, 0f, 2f), 90f, 3, "", br => br.SetAggroProfile(AggroProfile.Defensive), 30f);
            EnemyBrain brute = EnemyDirector.Instance.SpawnAt(bruteArea.Prefab, stand + new Vector3(-4f, 0f, -6f), 90f, 13, "", br => br.SetAggroProfile(AggroProfile.Passive), 30f);
            spawned.Add(brute);
            yield return new WaitForSecondsRealtime(2.5f);
            int wildId = wild.GetComponent<NetworkObject>().ObjectId;
            yield return Ask(b, "dominate", wildId.ToString());
            Check(!bp.ServerDominating && bp.ServerCompanionNote.Contains("ungebrochen"), $"Bei vollem Leben: „{bp.ServerCompanionNote}“");
            brute.Health.ApplyDamage(new DamageInfo { Amount = brute.Health.Max * 0.8f, Source = a.gameObject, WeaponId = "test" });
            yield return Ask(b, "dominate", brute.GetComponent<NetworkObject>().ObjectId.ToString());
            Check(!bp.ServerDominating && bp.ServerCompanionNote.Contains("nicht dominieren"), $"Elite (Grubenschrat) nie: „{bp.ServerCompanionNote}“");

            wild.Health.ApplyDamage(new DamageInfo { Amount = wild.Health.Max * 0.72f, Source = a.gameObject, WeaponId = "test" });
            wild.ServerStun(40f);   // Test: der Wolf soll nicht Maxmod hinterherlaufen
            yield return new WaitForSecondsRealtime(0.3f);
            float wildFraction = wild.HealthFraction;
            yield return Ask(b, "dominate", wildId.ToString());
            bool began = bp.ServerDominating && _last.data.Contains("castLabel=Dominieren");
            yield return Ask(b, "walk", "0.8 1");
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Ask(b, "companion");
            Check(began && !bp.ServerDominating && _last.data.Contains("interrupted=1") && wild != null && wild.Health.IsAlive,
                $"Bei {wildFraction * 100f:0} % Leben beginnt „Dominieren“; Loslaufen bricht ab ({_last.data})");

            bn.ServerTeleport(stand, 270f);
            yield return new WaitForSecondsRealtime(2f);
            int dom0 = b.ServerData.lifetime.dominations;
            wildFraction = wild.HealthFraction;
            Vector3 wildPos = wild.transform.position;
            float t0 = Time.time;
            yield return Ask(b, "dominate", wildId.ToString());
            yield return WaitUntil(() => !bp.ServerDominating, 5f);
            float took = Time.time - t0;
            yield return new WaitForSecondsRealtime(1.5f);
            NetworkCompanion pet = bp.ServerPet;
            bool wildGone = wild == null || !wild.GetComponent<NetworkObject>().IsSpawned;
            bool callOnBar = bar.ServerSlots.Any(e => e.type == ActionEntryType.Companion && e.referenceId == CompanionCatalog.CallId);
            Check(pet != null && wildGone && bp.ServerHasCompanion && took > 2.6f && took < 3.8f && b.ServerData.lifetime.dominations == dom0 + 1 && callOnBar,
                $"Nach {took:0.0} s gebunden: das wilde Tier ist fort, der Gefährte steht da, „Aschwolf rufen“ liegt auf der Leiste (Hinweis: {bp.ServerCompanionNote})");
            if (pet == null) { Cleanup(b, bp, oldClass, spawned); yield break; }
            int petId = pet.GetComponent<NetworkObject>().ObjectId;
            Check(pet.Level == 15 && Mathf.Abs(pet.Health.Fraction - wildFraction) < 0.08f && Flat(pet.transform.position - wildPos) < 12f,
                $"Gefährte auf Krisbots Stufe ({pet.Level}), mit dem Leben, das das Tier noch hatte ({pet.Health.Fraction * 100f:0} %)");

            // ---------------------------------------------------------- Freundlich
            Line("");
            Line("[Freundlich und sichtbar]");
            // Maxmod stellt sich dazu (Sichtbarkeit: nur Spieler in der Naehe bekommen den Gefaehrten)
            Vector3 aSpot = stand + new Vector3(3f, 0f, 4f);
            aSpot.y = Ashveil.Core.WorldGround.SampleHeight(aSpot, aSpot.y) + 0.1f;
            a.GetComponent<NetworkMotion>().ServerTeleport(aSpot, 270f);
            yield return new WaitForSecondsRealtime(3f);
            yield return Ask(b, "companion");
            string mine = _last.data;
            yield return Ask(a, "petseen", petId.ToString());
            Check(mine.Contains("species=companion.ash_wolf;out=1") && _last.ok && _last.data.Contains("faction=Friendly;attackable=0"),
                $"Krisbot sieht ihren Gefährten ({mine}); Maxmod sieht ihn freundlich, nicht angreifbar ({_last.data})");

            // ---------------------------------------------------------- Folgen
            Line("");
            Line("[Folgen]");
            yield return Ask(b, "walk", "6 1");
            yield return new WaitForSecondsRealtime(2f);
            float followD = Flat(pet.transform.position - b.transform.position);
            Check(followD < 5f, $"Nach 6 s Laufen ist er {followD:0.0} m hinter Krisbot");
            Vector3 far = b.transform.position + new Vector3(0f, 0f, 120f);
            far.y = Ashveil.Core.WorldGround.SampleHeight(far, far.y) + 0.1f;
            bn.ServerTeleport(far, 0f);
            yield return new WaitForSecondsRealtime(2.5f);
            followD = Flat(pet.transform.position - b.transform.position);
            Check(followD < 6f, $"Teleport 120 m: er kommt nach ({followD:0.0} m)");
            bn.ServerTeleport(stand, 270f);
            yield return new WaitForSecondsRealtime(3f);

            // ---------------------------------------------------------- Angreifen, Signatur, Anrechnung
            Line("");
            Line("[Angreifen]");
            EnemyBrain foe = EnemyDirector.Instance.SpawnAt(wolfArea.Prefab, stand + new Vector3(-8f, 0f, 3f), 90f, 14, "", br => br.SetAggroProfile(AggroProfile.Defensive), 60f);
            spawned.Add(foe);
            yield return new WaitForSecondsRealtime(2.5f);
            pet.ServerWarpToOwner();
            yield return new WaitForSecondsRealtime(0.5f);
            int foeId = foe.GetComponent<NetworkObject>().ObjectId;
            float foeHp0 = foe.Health.Current;
            int xp0 = bp.ServerXp;
            double bond0 = bp.ServerMasteryPoints(CompanionCatalog.AshWolf.bond.id);
            float leap0 = pet.LastLeapAt;
            t0 = Time.time;
            yield return Ask(b, "petcmd", "attack " + foeId);
            yield return new WaitForSecondsRealtime(3f);
            bool leapt = pet.LastLeapAt > leap0;
            bool bleeding = foe.GetComponent<DamageOverTime>() != null;
            Check(pet.ServerFoe == foe.Health && foe.Health.Current < foeHp0 && foe.Target == pet.Health,
                $"„Angriff“ auf Krisbots Ziel: er beißt, der Wolf wehrt sich gegen ihn ({foeHp0:0} → {foe.Health.Current:0})");
            Check(leapt && bleeding, $"Signatur Sprungbiss aus {Flat(foe.transform.position - pet.transform.position):0.0} m, das Ziel blutet ({(leapt ? "Sprung" : "kein Sprung")}, {(bleeding ? "Blutung" : "keine Blutung")})");
            // P2 Balance: Tabellenleben (Stufe 14 ≈ 1100) – allein braucht der Gefaehrte dafuer anderthalb Minuten; der Test
            // prueft das Allein-Besiegen, nicht die Dauer: im Kampf auf 60 Leben (Schaden ohne Urheber, Krisbot trifft nie; Woelfe fliehen bei 15 % – sonst entkommt er dem Gefaehrten)
            if (foe != null && foe.Health.IsAlive && foe.Health.Current > 60f) foe.Health.ApplyDamage(new DamageInfo { Amount = foe.Health.Current - 60f, WeaponId = "test" });
            yield return WaitUntil(() => foe == null || !foe.Health.IsAlive, 40f);
            float killTime = Time.time - t0;
            yield return new WaitForSecondsRealtime(1f);
            var corpse = foe != null ? foe.GetComponent<CorpseLoot>() : null;
            Check(foe != null && !foe.Health.IsAlive && corpse != null && corpse.IsRecipient(b.ProfileId) && bp.ServerXp > xp0,
                $"Allein vom Gefährten besiegt ({killTime:0} s): Beute und Erfahrung für Krisbot, obwohl sie nie getroffen hat (+{bp.ServerXp - xp0} EP)");
            double bondGain = bp.ServerMasteryPoints(CompanionCatalog.AshWolf.bond.id) - bond0;
            Check(bondGain > 0.5 && b.ServerData.lifetime.companionKills >= 1, $"Bindung wächst durch den gemeinsamen Kampf (+{bondGain:0.0}, Siege {b.ServerData.lifetime.companionKills})");

            // ---------------------------------------------------------- Verteidigen ohne Befehl
            Line("");
            Line("[Verteidigen]");
            yield return Ask(b, "petcmd", "follow");
            EnemyBrain raider = EnemyDirector.Instance.SpawnAt(wolfArea.Prefab, stand + new Vector3(-3f, 0f, -3f), 90f, 5, "", br => br.SetAggroProfile(AggroProfile.Defensive), 30f);
            spawned.Add(raider);
            yield return new WaitForSecondsRealtime(2f);
            raider.ServerEngage(bh);
            yield return WaitUntil(() => pet.ServerFoe == raider.Health, 3f);
            Check(pet.ServerFoe == raider.Health, "Ein Wolf greift Krisbot an – der Gefährte geht ohne Befehl dazwischen");

            // Passiv: kaempft nie
            yield return Ask(b, "petcmd", "passive");
            yield return new WaitForSecondsRealtime(1.5f);
            Check(pet.Command == CompanionCommand.Passive && pet.ServerFoe == null, "„Passiv“: er lässt vom Gegner ab");
            EnemyDirector.Instance.Remove(raider);
            yield return new WaitForSecondsRealtime(1f);

            // Bleiben
            yield return Ask(b, "petcmd", "stay");
            Vector3 stayAt = pet.transform.position;
            yield return Ask(b, "walk", "4 1");
            yield return new WaitForSecondsRealtime(1.5f);
            Check(pet.Command == CompanionCommand.Stay && Flat(pet.transform.position - stayAt) < 1.6f && Flat(pet.transform.position - b.transform.position) > 5f,
                $"„Bleiben“: er hält die Stelle ({Flat(pet.transform.position - stayAt):0.0} m bewegt, Krisbot {Flat(pet.transform.position - b.transform.position):0} m weg)");
            yield return Ask(b, "petcmd", "follow");
            yield return new WaitForSecondsRealtime(6f);
            Check(Flat(pet.transform.position - b.transform.position) < 5f, $"„Folgen“: er kommt wieder mit ({Flat(pet.transform.position - b.transform.position):0.0} m)");

            // ---------------------------------------------------------- Anteil am Klassenschaden (§69/§70)
            Line("");
            Line("[Anteil am Klassenschaden]");
            bn.ServerTeleport(stand, 270f);
            yield return new WaitForSecondsRealtime(2f);
            EnemyBrain dummy = EnemyDirector.Instance.SpawnAt(bruteArea.Prefab, stand + new Vector3(-2.2f, 0f, 0f), 90f, 13, "", br => br.SetAggroProfile(AggroProfile.Defensive), 30f);
            spawned.Add(dummy);
            yield return new WaitForSecondsRealtime(2.5f);
            dummy.ServerStun(60f);
            dummy.Health.SetInvulnerableFor(0f);
            int dummyId = dummy.GetComponent<NetworkObject>().ObjectId;
            yield return Ask(b, "petcmd", "passive");
            float h0 = dummy.Health.Current;
            yield return Ask(b, "attack", dummyId.ToString());
            yield return new WaitForSecondsRealtime(8f);
            yield return Ask(b, "stop");
            float ownDps = (h0 - dummy.Health.Current) / 8f;
            yield return new WaitForSecondsRealtime(1f);
            h0 = dummy.Health.Current;
            yield return Ask(b, "petcmd", "attack " + dummyId);
            yield return new WaitForSecondsRealtime(10f);
            float petDps = (h0 - dummy.Health.Current) / 10f;
            yield return Ask(b, "petcmd", "follow");
            float share = petDps / Mathf.Max(0.01f, ownDps + petDps);
            Info($"Krisbot allein {ownDps:0.0} Schaden/s, Gefährte allein {petDps:0.0} Schaden/s (mit Sprungbiss und Blutung) → Anteil {share * 100f:0} %");
            Check(ownDps > 1f && share > 0.15f && share < 0.4f, $"Der Gefährte ist Teil des Klassenschadens, kein Bonus obendrauf: {share * 100f:0} % (Ziel etwa 25 %)");
            EnemyDirector.Instance.Remove(dummy);
            yield return new WaitForSecondsRealtime(1f);

            // ---------------------------------------------------------- Besiegt, nicht tot
            Line("");
            Line("[Besiegt, nicht tot]");
            yield return new WaitForSecondsRealtime(1f);
            pet.Health.ApplyDamage(new DamageInfo { Amount = pet.Health.Max * 3f, Source = brute.gameObject, WeaponId = "test" });
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Ask(b, "companion");
            Check(pet.Downed && !pet.Health.IsAlive && _last.data.Contains("downed=1"), $"Er fällt – besiegt, nicht tot ({_last.data})");
            float down0 = Time.time;
            yield return WaitUntil(() => !pet.Downed, 20f);
            float downFor = Time.time - down0;
            yield return new WaitForSecondsRealtime(0.8f);
            Check(!pet.Downed && pet.Health.IsAlive && pet.Health.Fraction > 0.4f && pet.Health.Fraction < 0.62f && Flat(pet.transform.position - b.transform.position) < 5f,
                $"Nach dem Kampf ({downFor:0} s) rappelt er sich mit {pet.Health.Fraction * 100f:0} % Leben auf und steht wieder bei Krisbot");

            // ---------------------------------------------------------- Entlassen und Rufen
            Line("");
            Line("[Entlassen und Rufen]");
            yield return Ask(b, "petcall");
            Check(bp.ServerPet == null && _last.data.Contains("out=0") && !b.ServerData.companion.active, $"Leiste: entlassen ({_last.data})");
            yield return Ask(b, "petcall");
            yield return new WaitForSecondsRealtime(1f);
            yield return Ask(b, "companion");
            Check(bp.ServerPet != null && _last.data.Contains("out=1") && b.ServerData.companion.active, $"Leiste: wieder gerufen ({_last.data})");
            pet = bp.ServerPet;

            // ---------------------------------------------------------- Speichern
            Line("");
            Line("[Speichern]");
            var d = b.ServerData;
            Check(d.companion != null && d.companion.species == CompanionCatalog.AshWolfId && d.signatures.Contains(NetworkProgression.DominateSignature) &&
                  d.mastery.Any(m => m.id == CompanionCatalog.AshWolf.bond.id),
                $"Speicherstand: Gefährte „{d.companion?.species}“, Signatur „dominate“, Bindung, {d.lifetime.dominations} Dominierungen, {d.lifetime.companionKills} gemeinsame Siege");

            Cleanup(b, bp, oldClass, spawned);
        }

        private void Cleanup(NetworkInventory b, NetworkProgression bp, CharacterClassDefinition oldClass, List<EnemyBrain> spawned)
        {
            foreach (EnemyBrain e in spawned) if (e != null) EnemyDirector.Instance.Remove(e);
            foreach (NetworkMotion m in Object.FindObjectsByType<NetworkMotion>(FindObjectsSortMode.None))
                if (m != null && m.IsServerInitialized && m.GetComponent<NetworkInventory>()?.ProfileId == ArenaProfiles[0]) m.ServerTeleportToSpawn();
            bp.ServerRemoveDominate();
            if (oldClass != null) { b.ServerModel.Class = oldClass; b.ServerSetLevel(b.ServerModel.Level); }
            b.GetComponent<NetworkMotion>().ServerTeleportToSpawn();
        }
    }
}
