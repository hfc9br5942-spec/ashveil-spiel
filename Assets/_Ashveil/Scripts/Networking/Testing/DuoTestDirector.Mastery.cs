using System.Collections;
using System.IO;
using System.Linq;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Praxis-Meisterschaften, erster Prototyp (Laufkunst, Ausweichen): Editor-Host Maxmod + Test-Client Krisbot.
    /// Prueft Kurven, echte Laufstrecke (Server), Teleports/Kreis/Wand ohne Lernwert, Reiserhythmus mit Meisterschaft
    /// (schneller, aber nie im Kampf), Ausweichen nur bei echter Gefahr, Werte beim Client, Speichern.
    /// Start: "Ashveil &gt; Test &gt; Meisterschaften (2 Spieler)". Bericht: Logs/Ashveil_MeisterschaftenTest.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string MasteryFlag = "meister";
        private static bool _masteryMode;

        private IEnumerator MasterySuite()
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

            NetworkProgression bp = b.GetComponent<NetworkProgression>();
            CharacterMotor bm = b.GetComponent<CharacterMotor>();
            NetworkMotion bn = b.GetComponent<NetworkMotion>();
            Health bh = b.GetComponent<Health>();
            const string Run = MasteryCatalog.RunningId, Dodge = MasteryCatalog.DodgingId;
            MasteryDefinition run = MasteryCatalog.Running, dodge = MasteryCatalog.Dodging;
            bp.ServerSetMasteryPoints(Run, 0);
            bp.ServerSetMasteryPoints(Dodge, 0);

            // ---------------------------------------------------------- Kurven
            Line("");
            Line("[Kurven]");
            Check(run.RankFor(9000) == 9 && run.RankFor(9400) == 10 && run.RankFor(run.TotalFor(25)) == 25 && run.RankFor(run.TotalFor(25) - 1) == 24,
                $"Laufkunst: 9,0 km = Rang {run.RankFor(9000)}, 9,4 km = Rang {run.RankFor(9400)}, Rang 25 bei {run.TotalFor(25) / 1000:0} km");
            Check(run.TotalFor(50) / 1000 > 150 && run.TotalFor(50) / 1000 < 210 && run.TotalFor(100) / 1000 > 2500 && run.RankFor(1e12) == 100,
                $"Langzeit: Rang 50 bei {run.TotalFor(50) / 1000:0} km, Rang 100 bei {run.TotalFor(100) / 1000:0} km, darueber keine Raenge");
            Check(Mathf.Abs(run.Effect("travel", 100) - 0.15f) < 0.001f && run.Effect("travel", 25) > 0.05f && run.Effect("travel", 25) < 0.065f,
                $"Wirkung frueh spuerbar, spaet flach: Reisetempo +{run.Effect("travel", 25) * 100:0.0} % bei 25, +{run.Effect("travel", 100) * 100:0} % bei 100");
            Check(MasteryCatalog.LevelFactor(13, 15) == 1f && MasteryCatalog.LevelFactor(12, 15) == 0.75f && MasteryCatalog.LevelFactor(11, 15) == 0.5f && MasteryCatalog.LevelFactor(9, 15) == 0f,
                "Schwache Gegner: bis 2 Stufen darunter voll, 3 darunter 75 %, 4 darunter 50 %, ab 6 darunter nichts");

            // ---------------------------------------------------------- Laufstrecke
            Line("");
            Line("[Laufkunst]");
            Vector3 start = Gnd(470f, 110f);
            // Laufstrecke abseits lebender Gegner: Am 07.10. lief B nach Norden genau in ein Versteck der Grauen Mutter (470/160),
            // wurde angesprungen und landete danach am Spawn – gemessen wurde dann nicht das Laufen. Gelaufen wird jetzt in die
            // Richtung mit dem meisten Abstand (Norden bleibt, solange dort 45 m frei sind).
            float walkYaw = QuietWalkYaw(start, 65f, out float clearance);
            Line($"·     Laufrichtung {walkYaw:0}°, naechster lebender Gegner {clearance:0} m von der Strecke");
            bn.ServerTeleport(start, walkYaw);
            yield return new WaitForSecondsRealtime(2.5f);
            double p0 = bp.ServerMasteryPoints(Run), m0 = bp.ServerMetersOnFoot;
            Vector3 s0 = b.transform.position;
            float maxMomentum = 0f;
            bool done = false;
            StartCoroutine(Flag(Ask(b, "walk", "10 1"), () => done = true));
            while (!done) { maxMomentum = Mathf.Max(maxMomentum, bm.State.TravelMomentum); yield return null; }
            yield return new WaitForSecondsRealtime(1.5f);
            float dist0 = Flat(b.transform.position - s0);
            double gained = bp.ServerMasteryPoints(Run) - p0, walked = bp.ServerMetersOnFoot - m0;
            Check(dist0 > 40f && gained > dist0 * 0.85f && gained < dist0 * 1.1f,
                $"Echte Strecke zaehlt: {dist0:0} m gelaufen → {gained:0} Lernpunkte (Statistik +{walked:0} m)");
            Check(maxMomentum > 0.95f, $"Reiserhythmus baut sich beim Laufen auf (hoechstens {maxMomentum:0.00}; Kampf-Grund: {bp.ServerCombatReason})");

            p0 = bp.ServerMasteryPoints(Run);
            bn.ServerTeleport(start + new Vector3(0f, 0f, -300f), 0f);
            yield return new WaitForSecondsRealtime(2.5f);
            bn.ServerTeleport(start, walkYaw);
            yield return new WaitForSecondsRealtime(2.5f);
            Check(bp.ServerMasteryPoints(Run) - p0 < 1.0, $"Teleport hin und zurueck (600 m) zaehlt nicht ({bp.ServerMasteryPoints(Run) - p0:0.0})");

            // Im Kreis laufen und gegen die Wand: dieselbe Rechnung, eingespeist (1 Probe = 1 s)
            p0 = bp.ServerMasteryPoints(Run);
            Vector3 c = b.transform.position;
            bp.ServerResetWalkWindow(c + new Vector3(4f, 0f, 0f));
            float circlePath = 0f;
            Vector3 prev = c + new Vector3(4f, 0f, 0f);
            for (int i = 1; i <= 40; i++)
            {
                float ang = i * 1.6f; // ~6,4 m je Sekunde auf einem Kreis mit 4 m Radius
                Vector3 q = c + new Vector3(Mathf.Cos(ang) * 4f, 0f, Mathf.Sin(ang) * 4f);
                circlePath += Flat(q - prev);
                prev = q;
                bp.ServerFeedWalkSample(q);
            }
            double circleGain = bp.ServerMasteryPoints(Run) - p0;
            Check(circleGain < circlePath * 0.35f, $"Im Kreis laufen: {circlePath:0} m Weg, nur {circleGain:0} Lernpunkte (Wirkungsgrad zuletzt {bp.ServerLastWalkEfficiency:0.00})");
            p0 = bp.ServerMasteryPoints(Run);
            bp.ServerResetWalkWindow(c);
            for (int i = 0; i < 15; i++) bp.ServerFeedWalkSample(c + new Vector3(0.02f, 0f, 0f) * (i % 2));
            Check(bp.ServerMasteryPoints(Run) - p0 < 0.5, "Gegen die Wand laufen (kein Versatz): nichts");

            // Meilenstein: Rang 10 durch echtes Laufen erreicht -> Kopfzeile und Chatzeile beim Client
            bp.ServerSetMasteryPoints(Run, run.TotalFor(10) - 3.0);
            bp.ServerResetWalkWindow(c);
            bp.ServerFeedWalkSample(c + new Vector3(6f, 0f, 0f));
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Ask(b, "mastery");
            Check(bp.ServerMasteryRank(Run) == 10 && _last.ok && _last.data.Contains("milestone=Laufkunst 10"),
                $"Meilenstein Rang 10: Client bekommt die Meldung ({_last.data})");
            bp.ServerResetWalkWindow(b.transform.position);

            // ---------------------------------------------------------- Meisterschaft macht Reisen schneller – nicht den Kampf
            Line("");
            Line("[Reiserhythmus]");
            bp.ServerSetMasteryPoints(Run, run.TotalFor(50) + 1);
            yield return new WaitForSecondsRealtime(1.5f);
            float want = run.Effect("travel", 50);
            yield return Ask(b, "mastery");
            Check(_last.ok && _last.data.Contains("rankRun=50") && Mathf.Abs(bm.State.TravelBonus - want) < 0.001f && _last.data.Contains("travelBonus=" + want.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)),
                $"Rang 50: Server und Client rechnen mit +{want * 100:0.0} % ({_last.data})");
            bn.ServerTeleport(start, walkYaw);
            yield return new WaitForSecondsRealtime(2.5f);
            s0 = b.transform.position;
            yield return Ask(b, "walk", "10 1");
            yield return new WaitForSecondsRealtime(1.5f);
            float dist50 = Flat(b.transform.position - s0);
            Check(dist50 > dist0 * 1.03f && dist50 < dist0 * (1f + want + 0.03f),
                $"Gleiche Strecke, gleiche Zeit: {dist0:0.0} m → {dist50:0.0} m (+{(dist50 / Mathf.Max(1f, dist0) - 1f) * 100:0.0} %; Kampf-Grund: {bp.ServerCombatReason})");
            Check(Mathf.Abs(bm.State.SprintSaving - run.Effect("sprintCost", 50)) < 0.001f, $"Sprinten kostet {run.Effect("sprintCost", 50) * 100:0} % weniger Ausdauer");

            // Im Kampf: kein Rhythmus
            bn.ServerTeleport(start, walkYaw);
            yield return new WaitForSecondsRealtime(2f);
            bh.ApplyDamage(new DamageInfo { Amount = 1f, Source = a.gameObject, WeaponId = "test" });
            yield return new WaitForSecondsRealtime(0.5f);
            bool combatFlag = bp.ServerInCombat && bm.State.InCombat;
            maxMomentum = 0f;
            done = false;
            StartCoroutine(Flag(Ask(b, "walk", "3 1"), () => done = true));
            while (!done) { maxMomentum = Mathf.Max(maxMomentum, bm.State.TravelMomentum); yield return null; }
            Check(combatFlag && maxMomentum < 0.01f, $"Im Kampf baut sich kein Reiserhythmus auf (Kampf {combatFlag}, hoechstens {maxMomentum:0.00})");
            yield return new WaitForSecondsRealtime(5.5f);
            Check(!bp.ServerInCombat, $"5 s nach dem letzten Treffer gilt man wieder als ausser Kampf (Kampf-Grund: {bp.ServerCombatReason})");

            // ---------------------------------------------------------- Ausweichen
            Line("");
            Line("[Ausweichen]");
            EnemySpawnArea area = Object.FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None)
                .FirstOrDefault(x => x.Definition != null && x.Definition.Id == "enemy.salt_brute" && x.Prefab != null);
            EnemyBrain brute = area != null ? EnemyDirector.Instance.SpawnAt(area.Prefab, start + new Vector3(0f, 0f, 6f), 180f, 13, "", br => br.SetAggroProfile(AggroProfile.Passive), 5f) : null;
            Check(brute != null, "Uebungsgegner (Grubenschrat, Stufe 13) erzeugt");
            if (brute != null)
            {
                yield return new WaitForSecondsRealtime(2f);
                // Kampfumbau (Max 30.09.): die Ausweichrolle hat nur der Aschenjaeger; die Meisterschaft „Ausweichen“ lernt
                // jede Klasse ueber das passive Ausweichen (WoW Classic)
                var beq = b.GetComponent<CharacterEquipment>();
                bool bRolls = b.ServerModel.Class != null && b.ServerModel.Class.canRoll;
                brute.ServerStun(6f);
                int empty0 = bp.ServerEmptyDodges;
                double d0 = bp.ServerMasteryPoints(Dodge);
                yield return Ask(b, "dodge");
                yield return new WaitForSecondsRealtime(1.5f);
                if (bRolls)
                    Check(bp.ServerEmptyDodges == empty0 + 1 && bp.ServerMasteryPoints(Dodge) - d0 < 0.001, "Rolle ohne Gefahr: kein Lernwert (als leere Rolle gezaehlt)");
                else
                    Check(bp.ServerEmptyDodges == empty0 && bp.ServerMasteryPoints(Dodge) - d0 < 0.001,
                        $"{b.ServerModel.Class?.DisplayName} rollt nicht (Ausweichrolle nur fuer den Aschenjaeger) – kein Lernwert");
                yield return new WaitForSecondsRealtime(5f);

                // Passives Ausweichen: der Grubenschrat schlaegt zu, B weicht aus (Wurf erzwungen) und lernt daran
                int dodges0 = bp.ServerMeaningfulDodges;
                d0 = bp.ServerMasteryPoints(Dodge);
                CharacterEquipment.DodgeEnabled = true;
                beq.DodgeRollOverride = 0f;
                bn.ServerTeleport(brute.transform.position + brute.transform.forward * 2.2f, brute.transform.eulerAngles.y + 180f);
                brute.ServerEngage(bh);
                float tp = Time.time;
                while (Time.time - tp < 25f && bp.ServerMeaningfulDodges - dodges0 < 2)
                {
                    if (bh.Current < bh.Max * 0.6f) bh.Heal(bh.Max);
                    yield return null;
                }
                beq.DodgeRollOverride = -1f;
                CharacterEquipment.DodgeEnabled = false;
                yield return new WaitForSecondsRealtime(0.3f);
                int credited = bp.ServerMeaningfulDodges - dodges0;
                double dGain = bp.ServerMasteryPoints(Dodge) - d0;
                Check(credited >= 2 && Mathf.Abs((float)dGain - credited) < 0.01f,
                    $"Passives Ausweichen im Kampf zaehlt: {credited}× ausgewichen → {dGain:0.0} Lernpunkte (Stufe 13 gegen 15 = voll)");
                brute.ServerStun(8f);
                bh.Heal(bh.Max);
                yield return BlockSection(a, b, brute);
                EnemyDirector.Instance.Remove(brute);
            }

            // ---------------------------------------------------------- Client und Speicherstand
            Line("");
            Line("[Anzeige und Speichern]");
            yield return new WaitForSecondsRealtime(6f);
            yield return Ask(b, "mastery");
            double srvRun = bp.ServerMasteryPoints(Run);
            Check(_last.ok && _last.data.Contains("rankRun=50") && _last.data.Contains("dodges=" + bp.ServerMeaningfulDodges),
                $"Client B kennt seine Werte fuer das Fenster „Erfahrung“ ({_last.data})");
            b.SaveNow();
            yield return new WaitForSecondsRealtime(0.5f);
            bool loaded = CharacterStore.Service.TryLoad(ArenaProfiles[1], out CharacterSaveData saved);
            MasterySave sRun = loaded ? saved.mastery.FirstOrDefault(m => m.id == Run) : null;
            Check(loaded && saved.version == CharacterSaveData.CurrentVersion && sRun != null && System.Math.Abs(sRun.points - srvRun) < 1.0 && saved.lifetime.metersOnFoot > 50,
                $"Speicherstand v{(loaded ? saved.version : 0)}: Laufkunst {(sRun != null ? sRun.points : -1):0} Punkte, {(loaded ? saved.lifetime.metersOnFoot : 0):0} m zu Fuss");

            // Ausgangslage fuer andere Tests wiederherstellen
            bp.ServerSetMasteryPoints(Run, 0);
            bp.ServerSetMasteryPoints(Dodge, 0);
            b.SaveNow();
        }

        // ---------------------------------------------------------- Schildblock (passiv, nur mit Schild)
        private IEnumerator BlockSection(NetworkInventory a, NetworkInventory b, EnemyBrain brute)
        {
            Line("");
            Line("[Schildblock]");
            const string Shield = "item.oak_shield", Block = MasteryCatalog.BlockingId;
            MasteryDefinition block = MasteryCatalog.Blocking;
            NetworkProgression bp = b.GetComponent<NetworkProgression>();
            var eq = b.GetComponent<Ashveil.Gameplay.Items.CharacterEquipment>();
            Health bh = b.GetComponent<Health>();
            NetworkMotion bn = b.GetComponent<NetworkMotion>();
            bp.ServerSetMasteryPoints(Block, 0);

            if (eq.HasShield)
            {
                // Der Eidwaechter beginnt seit dem Kampfumbau mit Schild – fuer diesen Abschnitt erst ablegen
                yield return Ask(b, "unequip", ((int)EquipmentSlot.OffHand).ToString());
                yield return new WaitForSecondsRealtime(0.5f);
                b.ServerRemoveAll(Shield);
                yield return new WaitForSecondsRealtime(0.3f);
            }
            Check(!eq.HasShield && eq.BlockChance == 0f, "Ohne Schild wird nicht geblockt (Blockchance 0)");
            ItemDefinition shieldDef = GameData.Item(Shield);
            Check(shieldDef != null && shieldDef.IsShield && shieldDef.BlockValue > 0 && !GameData.Item("item.cracked_focus").IsShield,
                $"Eichenschild ist ein Schild (Blockwert {(shieldDef != null ? shieldDef.BlockValue : 0)}), ein Fokus nicht");
            b.ServerGive(new ItemStack(Shield, 1));
            yield return Ask(b, "autoequip", IndexOf(b, Shield).ToString());
            yield return new WaitForSecondsRealtime(0.5f);
            Check(Equipped(b, EquipmentSlot.OffHand) == Shield && eq.HasShield,
                $"Krisbot ({(b.LootClass != null ? b.LootClass.DisplayName : "ohne Klasse")}) legt den Eichenschild an ({_last.data})");
            if (!eq.HasShield) yield break;
            float expectValue = shieldDef.BlockValue + eq.Stats.Strength * 0.5f;
            Check(Mathf.Approximately(eq.BlockChance, 0.10f) && Mathf.Approximately(eq.BlockValue, Mathf.Round(expectValue)),
                $"Blockchance 10 %, Blockwert {eq.BlockValue:0} (Schild {shieldDef.BlockValue} + halbe Staerke {eq.Stats.Strength})");

            // Vor dem Schrat stehen, ihn ansehen; er ist betaeubt, damit nur unsere Treffer zaehlen
            brute.ServerStun(60f);
            Vector3 bpos = brute.transform.position;
            Vector3 front = bpos + brute.transform.forward * 2.5f;
            float faceYaw = Quaternion.LookRotation(bpos - front).eulerAngles.y;
            bn.ServerTeleport(front, faceYaw);
            yield return new WaitForSecondsRealtime(1.5f);
            bh.ResetToFull();
            Vector3 dir = (b.transform.position - bpos); dir.y = 0f; dir.Normalize();
            DamageInfo Hit(float amount, GameObject src, bool area = false) => new DamageInfo
                { Amount = amount, Source = src, Direction = dir, HitPoint = b.transform.position + Vector3.up, WeaponId = "test", Unblockable = area };

            // Sicherer Block (fester Wurf 0)
            eq.BlockRollOverride = 0f;
            float hp0 = bh.Current, raw = 40f;
            float mitigated = Mathf.Max(1f, Mathf.Round(eq.Mitigate(raw)));
            double pts0 = bp.ServerMasteryPoints(Block), dmg0 = bp.ServerDamageBlocked;
            bh.ApplyDamage(Hit(raw, brute.gameObject));
            float lost = hp0 - bh.Current;
            float blocked = (float)(bp.ServerDamageBlocked - dmg0);
            Check(Mathf.Approximately(blocked, Mathf.Min(mitigated, eq.BlockValue)) && Mathf.Approximately(lost, Mathf.Max(0f, mitigated - blocked)),
                $"Treffer von vorn geblockt: {raw:0} roh → {mitigated:0} nach Ruestung → {blocked:0} geblockt, {lost:0} Schaden");
            Check(Mathf.Abs((float)(bp.ServerMasteryPoints(Block) - pts0) - blocked) < 0.01f,
                $"Schildblock lernt am abgefangenen Schaden (+{bp.ServerMasteryPoints(Block) - pts0:0} Punkte, Stufe 13 gegen 15 = voll)");
            yield return new WaitForSecondsRealtime(1f);
            yield return Ask(b, "mastery");
            Check(_last.ok && _last.data.Contains("lastBlock=" + blocked.ToString("0", System.Globalization.CultureInfo.InvariantCulture)),
                $"Krisbot sieht „({blocked:0} geblockt)“ ({_last.data})");

            // Kleiner Treffer: voll geblockt, kein Schaden
            hp0 = bh.Current;
            bh.ApplyDamage(Hit(6f, brute.gameObject));
            Check(bh.Current == hp0, $"Kleiner Treffer ganz abgefangen: kein Schaden ({hp0 - bh.Current:0})");

            // Nicht blockbar: Flaeche, von hinten, Wurf ueber der Chance, Spieler als Quelle lernt nicht
            dmg0 = bp.ServerDamageBlocked;
            bh.ApplyDamage(Hit(raw, brute.gameObject, area: true));
            Check(bp.ServerDamageBlocked == dmg0, "Flaechenangriff (Stampfen) wird nie geblockt – ausweichen statt blocken");
            bn.ServerTeleport(front, faceYaw + 180f);
            yield return new WaitForSecondsRealtime(1.5f);
            bh.ApplyDamage(Hit(raw, brute.gameObject));
            Check(bp.ServerDamageBlocked == dmg0, "Treffer von hinten wird nicht geblockt");
            bn.ServerTeleport(front, faceYaw);
            yield return new WaitForSecondsRealtime(1.5f);
            eq.BlockRollOverride = 0.5f;
            bh.ApplyDamage(Hit(raw, brute.gameObject));
            Check(bp.ServerDamageBlocked == dmg0, "Wurf 0,5 bei 10 % Blockchance: kein Block");
            eq.BlockRollOverride = 0f;
            a.GetComponent<NetworkMotion>().ServerTeleport(b.transform.position + b.transform.forward * 2f + b.transform.right * 1.5f, faceYaw + 180f);
            yield return new WaitForSecondsRealtime(1.5f);
            pts0 = bp.ServerMasteryPoints(Block);
            int blocks0 = bp.ServerBlocks;
            bh.ResetToFull();
            hp0 = bh.Current;
            bh.ApplyDamage(Hit(raw, a.gameObject));
            Check(Mathf.Approximately(hp0 - bh.Current, Mathf.Max(0f, mitigated - eq.BlockValue)) && bp.ServerBlocks == blocks0 && bp.ServerMasteryPoints(Block) == pts0,
                $"Treffer eines Spielers: geblockt ({hp0 - bh.Current:0} Schaden), aber kein Lernwert (kein Farmen unter Freunden)");

            // Zufall: ueber viele Wuerfe etwa 10 %
            eq.BlockRollOverride = -1f;
            int n = 0;
            for (int i = 0; i < 2000; i++)
            {
                var info = Hit(raw, brute.gameObject);
                eq.Block(ref info, 30f);
                if (info.Blocked > 0f) n++;
            }
            Check(n > 150 && n < 260, $"Ohne festen Wurf: {n} von 2000 Treffern geblockt (≈ 10 %)");

            // Meisterschaft Rang 50
            bp.ServerSetMasteryPoints(Block, block.TotalFor(50) + 1);
            yield return new WaitForSecondsRealtime(1f);
            float c50 = 0.10f + block.Effect("blockChance", 50), v50 = Mathf.Round(expectValue * (1f + block.Effect("blockValue", 50)));
            Check(Mathf.Abs(eq.BlockChance - c50) < 0.0001f && Mathf.Approximately(eq.BlockValue, v50),
                $"Schildblock Rang 50: Blockchance {eq.BlockChance * 100f:0.#} %, Blockwert {eq.BlockValue:0}");

            // Aufraeumen
            eq.BlockRollOverride = -1f;
            bp.ServerSetMasteryPoints(Block, 0);
            yield return Ask(b, "unequip", ((int)EquipmentSlot.OffHand).ToString());
            b.ServerRemoveAll(Shield);
            bh.ResetToFull();
            yield return new WaitForSecondsRealtime(0.5f);
            Check(!eq.HasShield, "Schild wieder abgelegt");
        }

        private static float Flat(Vector3 v)
        {
            v.y = 0f;
            return v.magnitude;
        }

        private static IEnumerator Flag(IEnumerator inner, System.Action done)
        {
            yield return inner;
            done();
        }

        /// <summary>Laufrichtung (0/90/180/270°) für Messstrecken: Norden, solange dort mindestens 45 m Abstand zu lebenden Gegnern
        /// sind, sonst die Richtung mit dem größten Abstand der Strecke zum nächsten lebenden Gegner.</summary>
        private static float QuietWalkYaw(Vector3 start, float length, out float clearance)
        {
            var best = (yaw: 0f, gap: -1f);
            float north = -1f;
            for (int k = 0; k < 4; k++)
            {
                float yaw = k * 90f;
                Vector3 end = start + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * length;
                float gap = float.MaxValue;
                foreach (EnemyBrain e in EnemyBrain.All)
                    if (e != null && e.State != EnemyState.Dead) gap = Mathf.Min(gap, SegmentGap(e.transform.position, start, end));
                if (k == 0) north = gap;
                if (gap > best.gap) best = (yaw, gap);
            }
            if (north >= 45f) best = (0f, north);
            clearance = best.gap;
            return best.yaw;
        }

        private static float SegmentGap(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector2 p2 = new Vector2(p.x, p.z), a2 = new Vector2(a.x, a.z), ab = new Vector2(b.x - a.x, b.z - a.z);
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p2 - a2, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(p2, a2 + ab * t);
        }
    }
}
