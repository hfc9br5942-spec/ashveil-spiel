using System.Collections;
using System.Globalization;
using System.Text;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Targeting;
using Ashveil.Gameplay.Visuals;
using Ashveil.Gameplay.Weapons;
using FishNet;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Mehrspieler-Selbsttest: Der Server (<see cref="DuoTestDirector"/>) gibt jedem Spieler Befehle, der Spieler
    /// fuehrt sie auf seinem eigenen Client ueber dieselben Wege aus wie ein Mensch (Eingaben, Inventar-Bitten)
    /// und meldet das Ergebnis zurueck. Nur aktiv im Editor-Host oder mit "-ashveil-agent".
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SelfTestAgent : NetworkBehaviour
    {
        private static bool AgentAllowed => TestBuild.Enabled && (Application.isEditor || ProfileSelection.HasArg("-ashveil-agent"));

        /// <summary>Server -&gt; Besitzer: Befehl ausfuehren.</summary>
        public void Send(int token, string cmd, string arg) => TargetCommand(Owner, token, cmd, arg ?? "");

        [TargetRpc]
        private void TargetCommand(NetworkConnection conn, int token, string cmd, string arg)
        {
            if (!AgentAllowed) return;
            StartCoroutine(Execute(token, cmd, arg));
        }

        [ServerRpc]
        private void ServerReply(int token, bool ok, string data)
        {
            DuoTestDirector.Reply(token, ok, data);
        }

        private IEnumerator Execute(int token, string cmd, string arg)
        {
            var inv = GetComponent<NetworkInventory>();
            var motor = GetComponent<CharacterMotor>();
            var input = GetComponent<PlayerInputReader>();
            var targeting = GetComponent<TargetingSystem>();
            var weapons = GetComponent<WeaponUser>();
            string[] a = arg.Split(' ');
            int rev0 = inv.ClientRevision;

            switch (cmd)
            {
                case "state":
                    ServerReply(token, true, Describe(inv, weapons));
                    yield break;

                case "goto":
                {
                    Vector3 p = new Vector3(F(a, 0), F(a, 1), F(a, 2));
                    var motion = GetComponent<NetworkMotion>();
                    if (motion != null && motion.IsSpawned) motion.RequestTeleport(p, F(a, 3)); // Server versetzt (Vorhersage)
                    else motor.Teleport(p, F(a, 3));
                    yield return new WaitForSecondsRealtime(1f);
                    ServerReply(token, true, "");
                    yield break;
                }

                case "attack":
                {
                    NetworkObject nob = Find(I(a, 0));
                    CombatTarget ct = nob != null ? nob.GetComponent<CombatTarget>() : null;
                    if (ct == null) { ServerReply(token, false, "Ziel nicht gefunden"); yield break; }
                    if (weapons != null && !weapons.UsingMelee && weapons.MeleeWeapon != null && (a.Length < 2 || a[1] != "keep"))
                    {
                        input.InjectCombat(new CombatInput { SwapWeapon = true });
                        yield return new WaitForSecondsRealtime(0.8f);
                    }
                    targeting.SetTarget(ct);
                    yield return null;
                    if (weapons != null && !weapons.IsAutoAttacking) input.InjectCombat(new CombatInput { Ability1 = true });
                    ServerReply(token, true, "");
                    yield break;
                }

                case "attack-ranged":
                {
                    // P2 Balance: Bogen/Fernkampf aktiv lassen bzw. dorthin wechseln, dann automatischer Schuss
                    NetworkObject nob = Find(I(a, 0));
                    CombatTarget ct = nob != null ? nob.GetComponent<CombatTarget>() : null;
                    if (ct == null) { ServerReply(token, false, "Ziel nicht gefunden"); yield break; }
                    if (weapons != null && weapons.UsingMelee && weapons.RangedWeapon != null)
                    {
                        input.InjectCombat(new CombatInput { SwapWeapon = true });
                        yield return new WaitForSecondsRealtime(0.8f);
                    }
                    targeting.SetTarget(ct);
                    yield return null;
                    if (weapons != null && !weapons.IsAutoAttacking) input.InjectCombat(new CombatInput { Ability1 = true });
                    ServerReply(token, true, weapons != null && !weapons.UsingMelee ? "ranged" : "melee");
                    yield break;
                }

                case "special":
                    input.InjectCombat(new CombatInput { Ability2 = true });
                    yield return null;
                    ServerReply(token, true, "");
                    yield break;

                case "stop":
                    if (weapons != null && weapons.IsAutoAttacking) input.InjectCombat(new CombatInput { Ability1 = true });
                    yield return null;
                    targeting.SetTarget(null);
                    ServerReply(token, true, "");
                    yield break;

                case "loot-open":
                {
                    NetworkObject nob = Find(I(a, 0));
                    int lr = inv.LootRevision;
                    if (nob == null || !inv.TryOpenLoot(nob.gameObject)) { ServerReply(token, false, "Leiche nicht gefunden"); yield break; }
                    yield return WaitFor(() => inv.LootRevision != lr, 5f);
                    ServerReply(token, inv.LootOpen, DescribeLoot(inv));
                    yield break;
                }

                case "loot-take":
                case "loot-takeall":
                {
                    int lr = inv.LootRevision;
                    if (cmd == "loot-takeall") inv.TakeAllLoot(); else inv.TakeLoot(I(a, 0));
                    yield return WaitFor(() => inv.LootRevision != lr && inv.ClientRevision != rev0, 5f);
                    ServerReply(token, string.IsNullOrEmpty(inv.LastError), DescribeLoot(inv) + " || " + inv.LastError);
                    yield break;
                }

                case "move": inv.Move(I(a, 0), I(a, 1)); break;
                case "split": inv.Split(I(a, 0), I(a, 1), I(a, 2)); break;
                case "autoequip": inv.AutoEquip(I(a, 0)); break;
                case "unequip": inv.Unequip((EquipmentSlot)I(a, 0)); break;
                case "use": inv.Use(I(a, 0)); break;

                case "weapons-of":
                {
                    NetworkObject nob = Find(I(a, 0));
                    var w = nob != null ? nob.GetComponent<WeaponUser>() : null;
                    ServerReply(token, w != null, w == null ? "" :
                        $"melee={(w.MeleeWeapon != null ? w.MeleeWeapon.Id : "-")};ranged={(w.RangedWeapon != null ? w.RangedWeapon.Id : "-")}");
                    yield break;
                }

                case "look":
                {
                    var look = GetComponent<NetworkAppearance>();
                    if (look == null) { ServerReply(token, false, "kein Aussehen"); yield break; }
                    CharacterAppearanceData d = look.Current.Clone();
                    foreach (string kv in a)
                    {
                        int eq = kv.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = kv.Substring(0, eq), v = kv.Substring(eq + 1);
                        int n = int.TryParse(v, out int iv) ? iv : 0;
                        switch (k)
                        {
                            case "body": d.body = n; break;
                            case "skin": d.skin = n; break;
                            case "hair": d.hair = n; break;
                            case "hairColor": d.hairColor = n; break;
                            case "beard": d.beard = n; break;
                            case "hideHelm": d.hideHelm = n != 0; break;
                            case "hideCloak": d.hideCloak = n != 0; break;
                            case "dye":
                                string[] p = v.Split(':', '.');
                                if (p.Length == 4 && int.TryParse(p[0], out int slot) && slot >= 0 && slot < d.dyes.Length)
                                    d.dyes[slot] = new DyeSet { primary = B(p[1]), secondary = B(p[2]), detail = B(p[3]) };
                                break;
                            case "transmog":
                                string[] t = v.Split(':');
                                if (t.Length == 2 && int.TryParse(t[0], out int ts) && ts >= 0 && ts < d.transmog.Length) d.transmog[ts] = t[1];
                                break;
                        }
                    }
                    string before = look.Current.Encode();
                    look.RequestChange(d);
                    yield return WaitFor(() => look.Current.Encode() != before, 3f);
                    ServerReply(token, true, look.Current.Encode());
                    yield break;
                }

                case "visuals-of":
                {
                    NetworkObject nob = Find(I(a, 0));
                    var vis = nob != null ? nob.GetComponent<EquipmentVisuals>() : null;
                    if (vis == null || vis.Rig == null) { ServerReply(token, false, "keine Darstellung"); yield break; }
                    yield return null;
                    ServerReply(token, true, $"{vis.Rig.Summary()}|hair={vis.Rig.HairVisible}|masked={vis.Rig.BodyMasked}|look={vis.Look.Encode()}");
                    yield break;
                }

                case "preview-tryon":
                {
                    int idx = I(a, 0);
                    CharacterPreviewStage stage = CharacterPreviewStage.Ensure();
                    var eqs = (ItemStack[])inv.Equipment.Clone();
                    if (idx >= 0 && idx < inv.Bag.Length && !inv.Bag[idx].IsEmpty)
                    {
                        var probe = new InventoryModel { Class = inv.Class, Level = inv.Level };
                        probe.Load(new ItemStack[InventoryModel.BagSize], eqs);
                        probe.Bag[0] = inv.Bag[idx];
                        if (probe.AutoEquip(0).Ok) eqs = (ItemStack[])probe.Equipment.Clone();
                    }
                    var look = GetComponent<NetworkAppearance>();
                    stage.SetState(eqs, look != null ? look.Current : new CharacterAppearanceData());
                    yield return null;
                    var vis = GetComponent<EquipmentVisuals>();
                    ServerReply(token, stage.Rig != null, $"preview={(stage.Rig != null ? stage.Rig.Summary() : "")}|world={(vis != null && vis.Rig != null ? vis.Rig.Summary() : "")}");
                    yield break;
                }

                // ---------------------------------------------------------- Milestone 4: Gespraech, Auftraege, Handel
                case "npc-talk":
                case "quest-accept":
                case "quest-turnin":
                case "quest-abandon":
                case "buy":
                case "sell":
                case "buyback":
                {
                    var prog = GetComponent<NetworkProgression>();
                    if (prog == null) { ServerReply(token, false, "kein Fortschritt"); yield break; }
                    int pr = prog.ClientRevision;
                    switch (cmd)
                    {
                        case "npc-talk": prog.Talk(a[0]); break;
                        case "quest-accept": prog.Accept(a[0]); break;
                        case "quest-turnin": prog.TurnIn(a[0], a.Length > 1 ? I(a, 1) : -1); break;
                        case "quest-abandon": prog.Abandon(a[0]); break;
                        case "buy": prog.Buy(I(a, 0), a.Length > 1 ? I(a, 1) : 1); break;
                        case "sell": prog.Sell(I(a, 0)); break;
                        case "buyback": prog.BuyBack(I(a, 0)); break;
                    }
                    yield return WaitFor(() => prog.ClientRevision != pr, 5f);
                    yield return new WaitForSecondsRealtime(0.15f); // Inventar-Stand kommt im selben Schwung
                    bool ok = string.IsNullOrEmpty(prog.LastError) && (cmd != "npc-talk" || (prog.DialogNpc != null && prog.DialogNpc.Id == a[0]));
                    ServerReply(token, ok, DescribeProgress(prog) + " || " + prog.LastError);
                    yield break;
                }

                // ---------------------------------------------------------- Milestone 5: Bewegung (Server-Vorhersage)
                case "walk":
                {
                    // walk <sekunden> <vorwaerts -1..1>
                    float secs = Mathf.Clamp(F(a, 0), 0.1f, 10f);
                    float fwd = a.Length > 1 ? F(a, 1) : 1f;
                    GetComponent<NetworkMotion>()?.ResetCorrectionStats(); // nur dieses Laufen messen
                    for (float t = 0f; t < secs; t += Time.deltaTime)
                    {
                        input.InjectMovement(fwd, false);
                        yield return null;
                    }
                    input.InjectMovement(0f, false);
                    yield return new WaitForSecondsRealtime(0.6f); // auslaufen und Korrekturen abwarten
                    ServerReply(token, true, DescribePos());
                    yield break;
                }

                case "world":
                {
                    // Large World: Zellzustaende auf diesem Rechner, z. B. "gg_west=Ready;gg_ost=Unloaded"
                    yield return null;
                    var sbw = new System.Text.StringBuilder();
                    WorldStreamer ws = WorldStreamer.Instance;
                    if (ws != null && ws.Manifest != null)
                        foreach ((Ashveil.World.WorldRegionDefinition _, Ashveil.World.WorldCellDefinition c) in ws.Manifest.AllCells())
                            sbw.Append(c.cellId).Append('=').Append(ws.StateOf(c.cellId)).Append(';');
                    ServerReply(token, ws != null, sbw.ToString() + DescribePos());
                    yield break;
                }

                case "players":
                {
                    // Large-World N3: welche Spielerfiguren dieser Client ueberhaupt bekommt
                    yield return null;
                    int n = Object.FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None).Length;
                    ServerReply(token, true, "players=" + n);
                    yield break;
                }

                case "enemies":
                {
                    // Kolosseum Pass 2: welche Gegner dieser Client bekommt – im eigenen Instanz-Platz / in anderen / Oberwelt
                    yield return null;
                    Ashveil.World.DungeonSlot mySlot = Ashveil.World.DungeonSlot.At(transform.position);
                    int same = 0, other = 0, open = 0;
                    foreach (NetworkEnemy e in Object.FindObjectsByType<NetworkEnemy>(FindObjectsSortMode.None))
                    {
                        Ashveil.World.DungeonSlot es = Ashveil.World.DungeonSlot.At(e.transform.position);
                        if (es == null) open++;
                        else if (es == mySlot) same++;
                        else other++;
                    }
                    ServerReply(token, true, $"same={same};other={other};open={open};slot={(mySlot != null ? mySlot.DungeonId + mySlot.Index : "-")}");
                    yield break;
                }

                case "dodge":
                {
                    input.InjectDodge();
                    yield return new WaitForSecondsRealtime(0.6f);
                    ServerReply(token, true, DescribePos());
                    yield break;
                }

                case "mount":
                {
                    // Reittier: wie ein Klick auf die Leiste (auf- oder absitzen)
                    Ashveil.Gameplay.Mounts.MountClient.Toggle();
                    yield return new WaitForSecondsRealtime(0.4f);
                    ServerReply(token, true, MountState());
                    yield break;
                }

                case "mountstate":
                {
                    yield return null;
                    ServerReply(token, Ashveil.Gameplay.Mounts.MountClient.HasMount, MountState());
                    yield break;
                }

                case "story":
                {
                    // Pferde-Geschichte: was der eigene Client weiss und sieht (Phasing)
                    yield return null;
                    ServerReply(token, true, StoryState());
                    yield break;
                }

                case "story-act":
                {
                    // story-act <aktion>[;arg]: Bitte ueber den echten Client-Weg (ServerRpc)
                    Ashveil.Gameplay.Quests.StoryClient.Send(a.Length > 0 ? a[0] : "", a.Length > 1 ? a[1] : "");
                    yield return new WaitForSecondsRealtime(0.6f);
                    ServerReply(token, true, StoryState());
                    yield break;
                }

                case "companion":
                {
                    // Dominator: was der eigene Client ueber den Gefaehrten weiss
                    yield return null;
                    ServerReply(token, true, CompanionState());
                    yield break;
                }

                case "dominate":
                {
                    // dominate <ObjectId>: Ziel waehlen und „Dominieren“ auf der Leiste ausloesen
                    NetworkObject nob = Find(I(a, 0));
                    CombatTarget ct = nob != null ? nob.GetComponent<CombatTarget>() : null;
                    if (ct == null) { ServerReply(token, false, "Ziel nicht gefunden"); yield break; }
                    targeting.SetTarget(ct);
                    yield return null;
                    Ashveil.Gameplay.Companions.CompanionClient.Trigger(CompanionCatalog.DominateId);
                    yield return new WaitForSecondsRealtime(0.4f);
                    ServerReply(token, true, CompanionState());
                    yield break;
                }

                case "petcall":
                {
                    Ashveil.Gameplay.Companions.CompanionClient.Trigger(CompanionCatalog.CallId);
                    yield return new WaitForSecondsRealtime(0.8f);
                    ServerReply(token, true, CompanionState());
                    yield break;
                }

                case "petcmd":
                {
                    // petcmd follow|stay|attack|passive [ObjectId]
                    string c = a.Length > 0 ? a[0] : "follow";
                    var petCmd = c == "stay" ? Ashveil.Gameplay.Companions.CompanionCommand.Stay
                        : c == "attack" ? Ashveil.Gameplay.Companions.CompanionCommand.Attack
                        : c == "passive" ? Ashveil.Gameplay.Companions.CompanionCommand.Passive
                        : Ashveil.Gameplay.Companions.CompanionCommand.Follow;
                    if (a.Length > 1)
                    {
                        NetworkObject nob = Find(I(a, 1));
                        CombatTarget ct = nob != null ? nob.GetComponent<CombatTarget>() : null;
                        if (ct != null) targeting.SetTarget(ct);
                        yield return null;
                    }
                    Ashveil.Gameplay.Companions.CompanionClient.SendCommand(petCmd);
                    yield return new WaitForSecondsRealtime(0.5f);
                    ServerReply(token, true, CompanionState());
                    yield break;
                }

                case "petseen":
                {
                    // petseen <ObjectId>: sieht dieser Rechner den Gefaehrten – freundlich, mit Namen?
                    yield return null;
                    NetworkObject nob = Find(I(a, 0));
                    CombatTarget ct = nob != null ? nob.GetComponent<CombatTarget>() : null;
                    ServerReply(token, ct != null, ct != null
                        ? $"faction={ct.Faction};attackable={(ct.IsAttackable ? 1 : 0)};name={ct.DisplayName};hp={ct.Health.Fraction.ToString("0.00", CultureInfo.InvariantCulture)}"
                        : "nicht gefunden");
                    yield break;
                }

                case "salvage":
                {
                    // Nachlese: freigeschaltet, Statistik, letzte Bergung, Zauberleiste
                    yield return null;
                    ServerReply(token, true, SalvageState());
                    yield break;
                }

                case "salvagemark":
                {
                    // salvagemark <ObjectId>: glimmt die Leiche fuer mich (Beute golden / Bergen gruen)?
                    yield return null;
                    NetworkObject nob = Find(I(a, 0));
                    var li = nob != null ? nob.GetComponent<LootIndicator>() : null;
                    ServerReply(token, li != null, li != null ? $"loot={(li.Available ? 1 : 0)};salvage={(li.SalvageAvailable ? 1 : 0)}" : "nicht gefunden");
                    yield break;
                }

                case "leiche":
                {
                    // 22.10.2026 (Suite mehrspieler4, MEHRSPIELER-01): leiche <ObjectId> – hat dieser Rechner die Leiche überhaupt
                    // (nach Despawn durch die Sichtweite nicht), welche Kopie (Instanz-Nr. ändert sich beim Neu-Erzeugen) und glimmt sie
                    // für mich (Beute golden / Bergen grün)? Der LootInteractor öffnet nur bei diesem Glimmen.
                    yield return null;
                    NetworkObject nob = Find(I(a, 0));
                    if (nob == null) { ServerReply(token, true, "found=0"); yield break; }
                    var li = nob.GetComponent<LootIndicator>();
                    ServerReply(token, li != null, $"found=1;inst={nob.gameObject.GetEntityId()};loot={(li != null && li.Available ? 1 : 0)};salvage={(li != null && li.SalvageAvailable ? 1 : 0)}");
                    yield break;
                }

                case "angriff-direkt":
                {
                    // 22.10.2026 (Suite mehrspieler4, MEHRSPIELER-03): angriff-direkt melee|shot|projectile <ObjectId> – die Angriffsbitte
                    // geht direkt an den Server (NetworkCombat.Submit…), an der Sperre des eigenen Clients vorbei: so, wie ein veränderter
                    // oder ein verspäteter Client sie im Tod noch schicken kann. Nur Testbefehl, kein neues RPC.
                    var nc = GetComponent<NetworkCombat>();
                    NetworkObject nob = Find(I(a, 1));
                    CombatTarget ct = nob != null ? nob.GetComponentInChildren<CombatTarget>() : null;
                    if (nc == null || ct == null) { ServerReply(token, false, "Kampf oder Ziel fehlt"); yield break; }
                    Vector3 brust = transform.position + Vector3.up * 1.2f;
                    Vector3 zielPunkt = ct.transform.position + Vector3.up * 0.8f;
                    Vector3 richtung = (zielPunkt - brust).sqrMagnitude > 1e-4f ? (zielPunkt - brust).normalized : transform.forward;
                    Vector3 flach = new Vector3(richtung.x, 0f, richtung.z);
                    if (flach.sqrMagnitude < 1e-4f) flach = transform.forward;
                    switch (a[0])
                    {
                        case "melee": nc.SubmitMelee(AttackIntentKind.Combo, 0, transform.position + Vector3.up, flach.normalized); break;
                        case "shot": nc.SubmitShot(AttackIntentKind.Combo, 0, brust, new[] { richtung }); break;
                        case "projectile": nc.SubmitProjectile(AttackIntentKind.Combo, 0, brust, richtung, ct); break;
                        default: ServerReply(token, false, "Art unbekannt: " + a[0]); yield break;
                    }
                    yield return null;
                    var h = GetComponent<Health>();
                    ServerReply(token, true, $"gesendet={a[0]};lebt={(h != null && h.IsAlive ? 1 : 0)}");
                    yield break;
                }

                case "salvage-do":
                {
                    // salvage-do <ObjectId>: wie ein Rechtsklick auf die Leiche (ohne Beute) – Bergen beginnen
                    NetworkObject nob = Find(I(a, 0));
                    if (nob == null || SalvageClient.Request == null) { ServerReply(token, false, "Leiche/Anfrage fehlt"); yield break; }
                    SalvageClient.Request(nob.gameObject);
                    yield return new WaitForSecondsRealtime(0.4f);
                    ServerReply(token, true, SalvageState());
                    yield break;
                }

                case "mountseen":
                {
                    // mountseen <ObjectId>: sieht dieser Rechner die andere Figur im Sattel?
                    yield return null;
                    NetworkObject nob = Find(I(a, 0));
                    var mv = nob != null ? nob.GetComponent<Ashveil.Gameplay.Mounts.MountVisual>() : null;
                    ServerReply(token, mv != null, mv != null ? $"visible={(mv.Visible ? 1 : 0)};gait={mv.Gait}" : "nicht gefunden");
                    yield break;
                }

                case "arena":
                {
                    // Kolosseum Pass 4: was das eigene HUD weiss
                    yield return null;
                    ServerReply(token, true, Ashveil.Gameplay.Colosseum.ColosseumClient.Describe());
                    yield break;
                }

                case "arena-bell":
                {
                    var npb = GetComponent<NetworkProgression>();
                    if (npb != null) npb.RequestRingBell();
                    yield return new WaitForSecondsRealtime(0.5f);
                    ServerReply(token, true, Ashveil.Gameplay.Colosseum.ColosseumClient.Describe());
                    yield break;
                }

                case "arena-chest":
                {
                    var npc = GetComponent<NetworkProgression>();
                    if (npc != null) npc.RequestArenaChest();
                    yield return new WaitForSecondsRealtime(0.8f);
                    ServerReply(token, true, Ashveil.Gameplay.Colosseum.ColosseumClient.Describe());
                    yield break;
                }

                case "arena-revive":
                {
                    // arena-revive <ObjectId des Gefallenen>
                    NetworkObject nob = Find(I(a, 0));
                    var hp = nob != null ? nob.GetComponent<Ashveil.Gameplay.Combat.Health>() : null;
                    var ai = GetComponent<ArenaInteractor>();
                    bool ok = hp != null && ai != null && ai.TryRevive(hp);
                    string why = nob == null ? "nicht gefunden" : hp == null ? "ohne Leben" : ai == null ? "ohne ArenaInteractor" : ai.LastReviveResult;
                    yield return new WaitForSecondsRealtime(0.4f);
                    ServerReply(token, ok, "try=" + why + ";" + Ashveil.Gameplay.Colosseum.ColosseumClient.Describe());
                    yield break;
                }

                case "mastery":
                {
                    // Praxis-Meisterschaften: was der eigene Client weiss (nur der Besitzer bekommt die Werte)
                    yield return null;
                    var m = GetComponent<Ashveil.Gameplay.Characters.CharacterMotor>();
                    ServerReply(token, Ashveil.Gameplay.Mastery.MasteryClient.Ready, string.Format(CultureInfo.InvariantCulture,
                        "run={0:0.0};dodge={1:0.0};rankRun={2};rankDodge={3};km={4:0.000};dodges={5};travelBonus={6:0.000};momentum={7:0.00};combat={8};milestone={9};lastBlock={10:0}",
                        Ashveil.Gameplay.Mastery.MasteryClient.Points(MasteryCatalog.RunningId), Ashveil.Gameplay.Mastery.MasteryClient.Points(MasteryCatalog.DodgingId),
                        Ashveil.Gameplay.Mastery.MasteryClient.Rank(MasteryCatalog.RunningId), Ashveil.Gameplay.Mastery.MasteryClient.Rank(MasteryCatalog.DodgingId),
                        Ashveil.Gameplay.Mastery.MasteryClient.MetersOnFoot / 1000.0, Ashveil.Gameplay.Mastery.MasteryClient.MeaningfulDodges,
                        m != null ? m.State.TravelBonus : -1f, m != null ? m.State.TravelMomentum : -1f, m != null && m.State.InCombat ? 1 : 0, Ashveil.Gameplay.Mastery.MasteryClient.LastMilestone, NetworkHealth.LastOwnBlocked));
                    yield break;
                }

                case "anatomy":
                {
                    // Kampf-Prototyp: was dieser Client vom Koerper des Gegners sieht (gespiegelt vom Server)
                    yield return null;
                    NetworkObject nob = Find(I(a, 0));
                    var ana = nob != null ? nob.GetComponent<Ashveil.Gameplay.Enemies.EnemyAnatomy>() : null;
                    if (ana == null) { ServerReply(token, false, nob == null ? "Gegner nicht sichtbar" : "keine Anatomie"); yield break; }
                    var sb = new StringBuilder();
                    int zones = 0;
                    foreach (var p in ana.Parts) { if (sb.Length > 0) sb.Append(','); sb.Append((int)p.State); if (p.Hitbox != null && p.Hitbox.enabled) zones++; }
                    int split = 0, forearm = 0;
                    foreach (Transform c in nob.transform)
                    {
                        if (c.GetComponent<Rigidbody>() == null) continue;
                        if (c.name.StartsWith("Unterarm")) forearm++; else split++;
                    }
                    var h = nob.GetComponent<Health>();
                    ServerReply(token, true, $"parts={ana.Parts.Count};zones={zones};states={sb};dmg={(int)ana.DamageState};knock={(ana.IsKnockedDown ? 1 : 0)};" +
                                             $"split={split};forearm={forearm};hp={(h != null ? h.Current : -1f):0}");
                    yield break;
                }

                case "aimpart":
                {
                    NetworkObject nob = Find(I(a, 0));
                    CombatTarget ct = nob != null ? nob.GetComponent<CombatTarget>() : null;
                    if (ct == null || targeting == null) { ServerReply(token, false, "Ziel nicht gefunden"); yield break; }
                    targeting.SetTarget(ct, (byte)I(a, 1));
                    yield return new WaitForSecondsRealtime(0.3f);
                    ServerReply(token, true, "part=" + targeting.CurrentPart + ";line=" + Ashveil.UI.HudRoot.PartLine(ct, targeting.CurrentPart));
                    yield break;
                }

                case "world-worst-reset":
                {
                    WorldStreamer.WorstLoadFrameMs = 0f;
                    ServerReply(token, true, "ok");
                    yield break;
                }

                case "world-worst":
                {
                    // laengstes Bild beim Laden auf diesem Rechner (fertiges Spiel)
                    ServerReply(token, true, string.Format(CultureInfo.InvariantCulture, "worst={0:0.0}", WorldStreamer.WorstLoadFrameMs));
                    yield break;
                }

                case "world-mem":
                {
                    // Speicher dieses Prozesses nach Aufraeumen (fertiges Spiel = echte Zahlen, anders als im Editor)
                    yield return Resources.UnloadUnusedAssets();
                    System.GC.Collect();
                    yield return null;
                    long native = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
                    ServerReply(token, true, string.Format(CultureInfo.InvariantCulture, "mem={0:0.0}", native / 1048576.0));
                    yield break;
                }

                case "indoor":
                {
                    // M6: Stimmung unter Tage auf diesem Rechner (Sonne aus, dunkles Umgebungslicht)
                    yield return new WaitForSecondsRealtime(0.3f);
                    Light sun = RenderSettings.sun;
                    string slot = Ashveil.Rendering.IndoorAtmosphere.CurrentSlot != null ? Ashveil.Rendering.IndoorAtmosphere.CurrentSlot.Index.ToString() : "-";
                    ServerReply(token, true, $"indoor={(Ashveil.Rendering.IndoorAtmosphere.IsIndoors ? 1 : 0)};sun={(sun != null && sun.enabled ? 1 : 0)};slot={slot}");
                    yield break;
                }

                case "grotto":
                {
                    // M6.3: Flutwasser und Lorenbahn-Gitter der Instanz, in der diese Figur steht (auf diesem Rechner)
                    yield return null;
                    Ashveil.World.DungeonSlot here = Ashveil.World.DungeonSlot.At(transform.position);
                    Ashveil.World.FloodWater fw = Ashveil.World.FloodWater.In(here);
                    Ashveil.World.LoreGate lg = Ashveil.World.LoreGate.In(here);
                    ServerReply(token, true, string.Format(CultureInfo.InvariantCulture, "flood={0:0.00};gate={1};slot={2}",
                        fw != null ? fw.Level01 : -1f, lg == null ? "-" : (lg.IsOpen ? "1" : "0"), here != null ? here.Index.ToString() : "-"));
                    yield break;
                }

                case "heart":
                {
                    // M6.4: Grubenlampen der Instanz auf diesem Rechner (1 = brennt, 0 = aus)
                    yield return null;
                    Ashveil.World.DungeonSlot here = Ashveil.World.DungeonSlot.At(transform.position);
                    var sb = new StringBuilder();
                    foreach (Ashveil.World.MineLamp l in Ashveil.World.MineLamp.In(here)) sb.Append(l.IsLit ? '1' : '0');
                    ServerReply(token, true, "lamps=" + (sb.Length > 0 ? sb.ToString() : "-") + ";slot=" + (here != null ? here.Index.ToString() : "-"));
                    yield break;
                }

                case "pos":
                    yield return null;
                    ServerReply(token, true, DescribePos());
                    yield break;

                case "cheat-offset":
                {
                    // Betrugsversuch fuer den Test: Position lokal verschieben, als koennte ein manipulierter Client das
                    Vector3 off = new Vector3(F(a, 0), F(a, 1), F(a, 2));
                    var cc = GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;
                    transform.position += off;
                    if (cc != null) cc.enabled = true;
                    yield return null;
                    ServerReply(token, true, DescribePos());
                    yield break;
                }

                // ---------------------------------------------------------- Milestone 5: Gruppe und Chat
                case "chat":
                case "party-invite":
                case "party-respond":
                case "party-leave":
                case "party-kick":
                {
                    var soc = GetComponent<NetworkSocial>();
                    if (soc == null) { ServerReply(token, false, "kein Social"); yield break; }
                    int sr = soc.ClientRevision;
                    switch (cmd)
                    {
                        case "chat":
                        {
                            // chat <kanal 0/1/2> <ziel oder -> <text ...>
                            string target = a.Length > 1 && a[1] != "-" ? a[1] : "";
                            string text = a.Length > 2 ? string.Join(" ", a, 2, a.Length - 2) : "";
                            soc.Say((Ashveil.Gameplay.Social.ChatChannel)I(a, 0), target, text);
                            break;
                        }
                        case "party-invite": soc.Invite(a[0]); break;
                        case "party-respond": soc.RespondInvite(a[0] == "1"); break;
                        case "party-leave": soc.LeaveParty(); break;
                        case "party-kick": soc.Kick(a[0]); break;
                    }
                    yield return WaitFor(() => soc.ClientRevision != sr, 3f);
                    yield return new WaitForSecondsRealtime(0.4f); // Gruppenstand kommt im naechsten Takt
                    ServerReply(token, soc.ClientRevision != sr, DescribeSocial(soc));
                    yield break;
                }

                case "social":
                {
                    var soc = GetComponent<NetworkSocial>();
                    yield return null;
                    ServerReply(token, soc != null, soc != null ? DescribeSocial(soc) : "");
                    yield break;
                }

                case "progress":
                {
                    var prog = GetComponent<NetworkProgression>();
                    ServerReply(token, prog != null && prog.Ready, prog != null ? DescribeProgress(prog) : "");
                    yield break;
                }

                // ---------------------------------------------------------- M6.6: Actionbar
                case "bar":
                {
                    yield return null;
                    ServerReply(token, ActionBarClient.Current != null && ActionBarClient.Current.Ready, DescribeBar());
                    yield break;
                }

                case "bar-assign":
                case "bar-clear":
                case "bar-swap":
                {
                    var bar = GetComponent<NetworkActionBar>();
                    if (bar == null || !bar.Ready) { ServerReply(token, false, "keine Leiste"); yield break; }
                    int r0 = bar.Revision;
                    if (cmd == "bar-assign")
                    {
                        System.Enum.TryParse(a.Length > 2 ? a[2] : "", out ActionEntryType t);
                        bar.Assign(I(a, 0), I(a, 1), t, a.Length > 3 ? a[3] : "");
                    }
                    else if (cmd == "bar-clear") bar.Clear(I(a, 0), I(a, 1));
                    else bar.Swap(I(a, 0), I(a, 1), I(a, 2), I(a, 3));
                    yield return WaitFor(() => bar.Revision != r0, 5f);
                    ServerReply(token, bar.Revision != r0 && string.IsNullOrEmpty(bar.LastError), DescribeBar() + " || " + bar.LastError);
                    yield break;
                }

                case "slot-press":
                {
                    int n0 = ActionBarInput.TriggerCount;
                    ActionBarInput.InjectSlotKey(I(a, 0));
                    yield return WaitFor(() => ActionBarInput.TriggerCount != n0, 2f);
                    yield return new WaitForSecondsRealtime(0.6f); // Antwort des Servers (Inventar/Abklingzeit) abwarten
                    ServerReply(token, ActionBarInput.TriggerCount != n0,
                        $"id={ActionBarInput.LastTriggeredId};page={ActionBarInput.ActivePage};dodgecd={(motor != null ? motor.State.DodgeCooldown : 0f).ToString("0.00", CultureInfo.InvariantCulture)}");
                    yield break;
                }

                case "bank-hold":
                    ActionBarInput.SimulateSwapHeld = I(a, 0) == 1;
                    yield return null;
                    yield return null;
                    ServerReply(token, true, "page=" + ActionBarInput.ActivePage);
                    yield break;

                case "bank-toggle":
                    ActionBarInput.InjectSwapPress();
                    yield return null;
                    yield return null;
                    ServerReply(token, true, "page=" + ActionBarInput.ActivePage);
                    yield break;

                case "bindmode":
                case "bind":
                case "bind-swap":
                case "bind-defaults":
                {
                    // Tests aendern nie die gespeicherte Belegung des Spielers
                    Ashveil.Core.Settings.InputBindingsStore.PersistenceEnabled = false;
                    var b = Ashveil.Core.Settings.InputBindingsStore.Current.Clone();
                    int before = 0;
                    if (cmd == "bindmode") b.toggleMode = a[0] == "toggle";
                    else if (cmd == "bind") before = b.BindSlot(I(a, 0), a.Length > 1 ? a[1] : "");
                    else if (cmd == "bind-swap") before = b.BindSwap(a.Length > 0 ? a[0] : "");
                    else b = new Ashveil.Core.Settings.InputBindings();
                    Ashveil.Core.Settings.InputBindingsStore.Set(b);
                    yield return null;
                    ServerReply(token, true, DescribeBindings() + ";before=" + before);
                    yield break;
                }

                case "bind-roundtrip":
                {
                    // Speichern/Laden der Tastenbelegung in eine eigene Testdatei (nicht die des Spielers)
                    var cur = Ashveil.Core.Settings.InputBindingsStore.Current.Clone();
                    string path = System.IO.Path.Combine(Application.temporaryCachePath, "ashveil_eingabe_test.json");
                    Ashveil.Core.Settings.InputBindingsStore.FilePathOverride = path;
                    Ashveil.Core.Settings.InputBindingsStore.PersistenceEnabled = true;
                    var t = new Ashveil.Core.Settings.InputBindings();
                    t.BindSlot(0, "Key:Q");
                    t.BindSwap("Key:LeftAlt");
                    t.toggleMode = true;
                    Ashveil.Core.Settings.InputBindingsStore.Set(t);
                    Ashveil.Core.Settings.InputBindingsStore.Load();
                    var back = Ashveil.Core.Settings.InputBindingsStore.Current;
                    bool ok = System.IO.File.Exists(path) && back.slots[0] == "Key:Q" && back.swapKey == "Key:LeftAlt" && back.toggleMode && back.slots[1] == "Key:Digit2";
                    try { System.IO.File.Delete(path); } catch (System.Exception) { }
                    Ashveil.Core.Settings.InputBindingsStore.FilePathOverride = null;
                    Ashveil.Core.Settings.InputBindingsStore.PersistenceEnabled = false;
                    Ashveil.Core.Settings.InputBindingsStore.Set(cur);
                    yield return null;
                    ServerReply(token, ok, "roundtrip=" + (ok ? "1" : "0"));
                    yield break;
                }

                case "bar-ui":
                {
                    yield return null;
                    var ui = Ashveil.UI.ActionBarUi.Instance;
                    var book = Ashveil.UI.SpellbookUi.Instance;
                    ServerReply(token, ui != null,
                        ui == null ? "" : $"slots={ui.SlotCount};k0={ui.KeyLabel(0)};k9={ui.KeyLabel(9)};k10={ui.KeyLabel(10)};k11={ui.KeyLabel(11)};shown={ui.ShownPage};book={(book != null ? book.Count : -1)}");
                    yield break;
                }

                // ------------------------------------------------ M8 Tageszeit
                case "clock":
                    yield return null;
                    ServerReply(token, true, ClockState());
                    yield break;

                // ------------------------------------------------ Dynamic Sky
                case "sky-state":
                    yield return null;
                    ServerReply(token, Ashveil.Rendering.Sky.SkyDirector.Active != null, SkyState());
                    yield break;

                case "sky-meteors":
                {
                    // Pass 12: Sternschnuppen in einem Zeitraum (echte Weltsekunden "von;bis") – rein aus dem Seed berechnet
                    yield return null;
                    var sd = Ashveil.Rendering.Sky.SkyDirector.Active;
                    string[] parts = arg.Split(';');
                    if (sd == null || parts.Length < 2) { ServerReply(token, false, ""); yield break; }
                    var ci = CultureInfo.InvariantCulture;
                    int n = sd.CountMeteors(double.Parse(parts[0], ci), double.Parse(parts[1], ci), out int sh);
                    ServerReply(token, true, $"n={n};shower={sh}");
                    yield break;
                }

                // ------------------------------------------------ M8.3 Lagerleben
                case "npc-state":
                {
                    yield return null;
                    var r = Ashveil.Gameplay.Npcs.NpcRoutineRegistry.Find(arg);
                    ServerReply(token, r != null, r == null ? "" : NpcState(r));
                    yield break;
                }

                case "animal-state":
                {
                    yield return null;
                    var r = Ashveil.Gameplay.Npcs.AnimalRegistry.Find(arg);
                    var c = CultureInfo.InvariantCulture;
                    ServerReply(token, r != null, r == null ? "" :
                        $"x={r.transform.position.x.ToString("0.00", c)};z={r.transform.position.z.ToString("0.00", c)};pose={r.Pose};walk={(r.Walking ? 1 : 0)}");
                    yield break;
                }

                // ------------------------------------------------ M8 Emotes
                case "chat-line":
                {
                    bool ok = Ashveil.UI.SocialUi.RunLine(arg);
                    yield return new WaitForSecondsRealtime(0.5f);
                    ServerReply(token, ok, EmoteState());
                    yield break;
                }

                case "emote-state":
                    yield return null;
                    ServerReply(token, true, EmoteState());
                    yield break;

                case "emote-menu":
                {
                    var ui = Ashveil.UI.EmoteUi.Instance;
                    if (ui != null) ui.SetOpen(true);
                    yield return null;
                    bool ok = ui != null && ui.IsOpen && ui.Count == EmoteCatalog.All.Length &&
                              EmoteCatalog.TryGetByKey(a[0], out EmoteInfo em) && EmoteClient.Play(em.Id);
                    yield return new WaitForSecondsRealtime(0.5f);
                    if (ui != null) ui.SetOpen(false);
                    ServerReply(token, ok, EmoteState() + ";rows=" + (ui != null ? ui.Count : 0));
                    yield break;
                }

                case "emote-of":
                {
                    yield return null;
                    NetworkObject nob = Find(I(a, 0));
                    var ne = nob != null ? nob.GetComponent<NetworkEmote>() : null;
                    EmotePoser p = ne != null ? ne.Poser : null;
                    ServerReply(token, p != null, p == null ? "" :
                        $"sync={ne.Current};poser={p.Current};active={(p.Active ? 1 : 0)};elapsed={p.Elapsed.ToString("0.0", CultureInfo.InvariantCulture)}");
                    yield break;
                }

                // ------------------------------------------------ M7 Charakter-Editor
                case "creator":
                {
                    yield return null;
                    var ui = Ashveil.UI.CharacterCreatorUi.Instance;
                    ServerReply(token, ui != null, (ui != null ? ui.Summary() : "") + $";pending={(CharacterCreationClient.Pending ? 1 : 0)};blocked={(UiPointer.GameBlocked ? 1 : 0)};msg={CharacterCreationClient.LastMessage}");
                    yield break;
                }

                case "creator-cat":
                {
                    var ui = Ashveil.UI.CharacterCreatorUi.Instance;
                    if (ui != null) ui.SelectCategory((Ashveil.UI.CharacterCreatorUi.Category)I(a, 0));
                    yield return null;
                    ServerReply(token, ui != null, ui != null ? ui.Current.ToString() : "");
                    yield break;
                }

                case "creator-set":
                {
                    var ui = Ashveil.UI.CharacterCreatorUi.Instance;
                    bool ok = ui != null && ui.SetValue((Ashveil.UI.CharacterCreatorUi.Category)I(a, 0), I(a, 1));
                    yield return null;
                    yield return null;
                    yield return null;
                    ServerReply(token, ok, ui != null ? ui.Summary() : "");
                    yield break;
                }

                case "creator-random":
                {
                    var ui = Ashveil.UI.CharacterCreatorUi.Instance;
                    if (ui != null) ui.Randomize(I(a, 0));
                    yield return null;
                    yield return null;
                    ServerReply(token, ui != null, ui != null ? ui.Summary() : "");
                    yield break;
                }

                case "creator-name":
                {
                    var ui = Ashveil.UI.CharacterCreatorUi.Instance;
                    if (ui != null) ui.SetName(arg);
                    yield return null;
                    ServerReply(token, ui != null, ui != null ? ui.NameText : "");
                    yield break;
                }

                // UI-204 (Spieltest r2): Namensfeld – Würfel-Vorschlag und Tippen wie über die Tastatur
                case "creator-dice":
                case "creator-type":
                {
                    var ui = Ashveil.UI.CharacterCreatorUi.Instance;
                    if (ui != null)
                    {
                        if (cmd == "creator-dice") ui.RollName(I(a, 0));
                        else ui.TypeText(arg);
                    }
                    yield return null;
                    ServerReply(token, ui != null, ui != null ? $"{ui.NameText};marked={(ui.NameIsSuggestion ? 1 : 0)}" : "");
                    yield break;
                }

                // EINSTIEG-01 (21.10.): Tippen über den echten Tastaturpuffer (Keyboard.onTextInput → RawInput), nicht an
                // ui.TypeText vorbei – so sieht die Suite, wenn ein anderes Fenster (Chat) die Zeichen vorher abholt.
                case "creator-keys":
                {
                    var ui = Ashveil.UI.CharacterCreatorUi.Instance;
                    string weg = ui != null && ui.IsOpen ? RawInput.TestTextEingeben(arg) : "";
                    for (int i = 0; i < 4; i++) yield return null;   // Warteschlange wird im nächsten Bild verarbeitet
                    ServerReply(token, ui != null && weg.Length > 0,
                        ui != null ? $"{ui.NameText};marked={(ui.NameIsSuggestion ? 1 : 0)};weg={weg}" : "kein Editor");
                    yield break;
                }

                case "creator-create":
                case "creator-raw":
                {
                    int n0 = CharacterCreationClient.ResultCount;
                    bool sent;
                    if (cmd == "creator-raw")
                    {
                        // Manipulierter Client: an der Oberflaeche vorbei direkt senden – "creator-raw <name> <kodiertes Aussehen>"
                        sent = CharacterCreationClient.Submit(a[0], CharacterAppearanceData.Decode(a.Length > 1 ? a[1] : ""), a.Length > 2 ? a[2] : null);
                    }
                    else
                    {
                        var ui = Ashveil.UI.CharacterCreatorUi.Instance;
                        sent = ui != null && ui.TryCreate();
                        if (!sent) { ServerReply(token, false, "abgelehnt (Client): " + (ui != null ? ui.Message : "")); yield break; }
                    }
                    if (!sent) { ServerReply(token, false, "nicht gesendet"); yield break; }
                    yield return WaitFor(() => CharacterCreationClient.ResultCount != n0, 8f);
                    yield return new WaitForSecondsRealtime(0.5f);
                    var ui2 = Ashveil.UI.CharacterCreatorUi.Instance;
                    ServerReply(token, CharacterCreationClient.LastOk,
                        CharacterCreationClient.LastMessage + $";open={(ui2 != null && ui2.IsOpen ? 1 : 0)};blocked={(UiPointer.GameBlocked ? 1 : 0)};captured={(UiPointer.KeyboardCaptured ? 1 : 0)}");
                    yield break;
                }

                case "self-look":
                {
                    yield return null;
                    var na = GetComponent<NetworkAppearance>();
                    var rig = GetComponent<EquipmentVisuals>()?.Rig;
                    ServerReply(token, na != null && rig != null,
                        (na != null ? "look=" + na.Current.Encode().Split("|y")[0] + $";hidden={(na.Hidden ? 1 : 0)}" : "") +
                        (rig != null ? $";overlay={rig.OverlaySummary()};eyes={rig.EyeTextureName}" : "") + ";name=" + (GetComponent<NetworkPlayer>()?.DisplayName ?? ""));
                    yield break;
                }

                case "ammo":
                    yield return null;
                    ServerReply(token, weapons != null, weapons == null ? "" :
                        $"weapon={(weapons.ActiveWeapon != null ? weapons.ActiveWeapon.Id : "")};ranged={(weapons.ActiveWeapon != null && weapons.ActiveWeapon == weapons.RangedWeapon ? 1 : 0)};ammo={weapons.Ammo};mag={weapons.MagazineSize};reloading={(weapons.IsReloading ? 1 : 0)}" +
                        $";arrows={(weapons.GetComponent<Ashveil.Gameplay.Combat.IRangedSupply>() is Ashveil.Gameplay.Combat.IRangedSupply sup ? sup.AmmoCount(Ashveil.Data.AmmoType.Arrow) : -1)};dual={(weapons.IsDualWielding ? 1 : 0)}");
                    yield break;

                // ------------------------------------------------ P1 Klassenfaehigkeiten
                case "spell-target":
                {
                    NetworkObject nob = a.Length > 0 && a[0].Length > 0 ? Find(I(a, 0)) : null;
                    CombatTarget ct = nob != null ? nob.GetComponentInChildren<CombatTarget>() : null;
                    targeting.SetTarget(ct);
                    yield return null;
                    ServerReply(token, ct != null || nob == null, ct != null ? ct.DisplayName : "");
                    yield break;
                }

                case "spell-cast":
                {
                    int rev = Ashveil.Gameplay.Abilities.SpellClient.Revision;
                    Ashveil.Gameplay.Abilities.SpellClient.LastError = "";
                    Ashveil.Gameplay.Abilities.SpellClient.CastRequest?.Invoke(a[0]);
                    yield return new WaitForSecondsRealtime(0.5f);
                    ServerReply(token, string.IsNullOrEmpty(Ashveil.Gameplay.Abilities.SpellClient.LastError), SpellState() + ";err=" + Ashveil.Gameplay.Abilities.SpellClient.LastError);
                    yield break;
                }

                case "spell-state":
                    yield return null;
                    ServerReply(token, Ashveil.Gameplay.Abilities.SpellClient.Ready, SpellState());
                    yield break;

                case "train":
                case "talent":
                case "talent-reset":
                {
                    var prog = GetComponent<NetworkProgression>();
                    int pr = prog.ClientRevision, sr = Ashveil.Gameplay.Abilities.SpellClient.Revision;
                    if (cmd == "train") Ashveil.Gameplay.Abilities.SpellClient.TrainRequest?.Invoke(a[0], a[1], I(a, 2));
                    else if (cmd == "talent") Ashveil.Gameplay.Abilities.SpellClient.TalentRequest?.Invoke(a[0]);
                    else Ashveil.Gameplay.Abilities.SpellClient.TalentResetRequest?.Invoke(a[0]);
                    yield return WaitFor(() => prog.ClientRevision != pr || Ashveil.Gameplay.Abilities.SpellClient.Revision != sr, 5f);
                    yield return new WaitForSecondsRealtime(0.3f);
                    ServerReply(token, string.IsNullOrEmpty(prog.LastError), SpellState() + ";money=" + prog.Money + ";err=" + prog.LastError);
                    yield break;
                }

                case "ui-open":
                {
                    // Fenster fuer Bilder/Pruefung: talents | book | trainer <npc>
                    if (a[0] == "talents" && Ashveil.UI.TalentUi.Instance != null) Ashveil.UI.TalentUi.Instance.SetOpen(a.Length < 2 || a[1] != "0");
                    if (a[0] == "book" && Ashveil.UI.SpellbookUi.Instance != null) Ashveil.UI.SpellbookUi.Instance.SetOpen(a.Length < 2 || a[1] != "0");
                    if (a[0] == "trainer" && Ashveil.UI.TrainerUi.Instance != null) { if (a.Length > 1 && a[1] == "0") Ashveil.UI.TrainerUi.Instance.Close(); else Ashveil.UI.TrainerUi.Instance.Toggle(a[1]); }
                    yield return new WaitForSecondsRealtime(0.4f);
                    ServerReply(token, true, $"talents={(Ashveil.UI.TalentUi.Instance != null && Ashveil.UI.TalentUi.Instance.IsOpen ? 1 : 0)};cells={(Ashveil.UI.TalentUi.Instance != null ? Ashveil.UI.TalentUi.Instance.CellCount : -1)}" +
                                              $";book={(Ashveil.UI.SpellbookUi.Instance != null ? Ashveil.UI.SpellbookUi.Instance.SpellCount : -1)};trainer={(Ashveil.UI.TrainerUi.Instance != null && Ashveil.UI.TrainerUi.Instance.IsOpen ? 1 : 0)}");
                    yield break;
                }

                case "bar-cd":
                    yield return null;
                    ServerReply(token, true, "cd=" + InventoryClient.CooldownRemaining(a[0]).ToString("0.0", CultureInfo.InvariantCulture) +
                                              ";count=" + InventoryClient.CountInBag(a[0]));
                    yield break;

                case "paketc":
                {
                    // 23.10.2026 (Suite mehrspieler4, Paket C / Kampfreaktionen): paketc reaktion|wirken|leiche <ObjectId> – was DIESER
                    // Rechner von einer fremden Figur gesehen hat (Treffer-Ergebnisse, Wirkhaltung, Leichenzustand). Nur lesen, kein RPC.
                    yield return null;
                    string antwort = DuoTestDirector.PaketCAbfrage(a.Length > 0 ? a[0] : "", a.Length > 1 ? Find(I(a, 1)) : null);
                    ServerReply(token, antwort.Length > 0 && !antwort.StartsWith("fehler"), antwort);
                    yield break;
                }

                case "reconnect":
                    ServerReply(token, true, "");
                    yield return new WaitForSecondsRealtime(0.3f);
                    ReconnectHelper.Run();
                    yield break;

                case "quit":
                    ServerReply(token, true, "");
                    yield return new WaitForSecondsRealtime(0.5f);
                    if (!Application.isEditor) Application.Quit();
                    yield break;

                default:
                    ServerReply(token, false, "Unbekannter Befehl " + cmd);
                    yield break;
            }

            // Inventar-Bitten: auf die Antwort des Servers warten
            yield return WaitFor(() => inv.ClientRevision != rev0, 5f);
            ServerReply(token, string.IsNullOrEmpty(inv.LastError), Describe(inv, weapons) + " || " + inv.LastError);
        }

        private static IEnumerator WaitFor(System.Func<bool> cond, float timeout)
        {
            float t = 0f;
            while (!cond() && t < timeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private static string StoryState()
        {
            var sb = new StringBuilder();
            sb.Append("ready=").Append(Ashveil.Gameplay.Quests.StoryClient.Ready ? 1 : 0)
              .Append(";race=").Append(Ashveil.Gameplay.Quests.StoryClient.Race)
              .Append(";farm=").Append(Ashveil.Gameplay.Quests.StoryClient.Farm)
              .Append(";state=").Append(Ashveil.Gameplay.Quests.StoryClient.HorseState)
              .Append(";step=").Append(Ashveil.Gameplay.Quests.StoryClient.HorseStep)
              .Append(";offer=").Append(Ashveil.Gameplay.Quests.StoryClient.StoryOffer)
              .Append(";scene=").Append(Ashveil.Gameplay.Story.StoryDirector.SceneId)
              .Append(";last=").Append(Ashveil.Gameplay.Story.StoryDirector.LastFinished);
            int herds = 0, visibleHerds = 0;
            foreach (HerdEncounter h in Object.FindObjectsByType<HerdEncounter>(FindObjectsSortMode.None))
            {
                herds++;
                if (h.Presenter != null && h.Presenter.Count > 0) visibleHerds++;
            }
            sb.Append(";herds=").Append(herds).Append('/').Append(visibleHerds);
            var local = Object.FindAnyObjectByType<Ashveil.Gameplay.Story.FarmHerdLocal>();
            sb.Append(";localHerd=").Append(local != null && local.Presenter != null ? local.Presenter.Count : 0);
            foreach (Ashveil.Gameplay.Story.StoryPhasedProp pp in Object.FindObjectsByType<Ashveil.Gameplay.Story.StoryPhasedProp>(FindObjectsSortMode.None))
                if (pp.name.StartsWith("Westzaun eingedrueckt")) sb.Append(";fenceBroken=").Append(pp.VisibleNow ? 1 : 0);
            var farmer = Object.FindAnyObjectByType<Ashveil.Gameplay.Story.FarmerPresenter>();
            sb.Append(";farmerInside=").Append(farmer != null && farmer.Inside ? 1 : 0).Append(";farmerApproach=").Append(farmer != null && farmer.Approaching ? 1 : 0);
            var si = Ashveil.Gameplay.Story.StoryInteractor.Offered;
            sb.Append(";prompt=").Append(si != null ? si.Action : "");
            return sb.ToString();
        }

        private static string CompanionState()
        {
            var h = Ashveil.Gameplay.Companions.CompanionClient.PetHealth;
            return string.Format(CultureInfo.InvariantCulture,
                "can={0};species={1};out={2};downed={3};cmd={4};level={5};hp={6:0.00};cast={7:0.00};castLabel={8};interrupted={9}",
                Ashveil.Gameplay.Companions.CompanionClient.CanDominate ? 1 : 0, Ashveil.Gameplay.Companions.CompanionClient.Species,
                Ashveil.Gameplay.Companions.CompanionClient.Out ? 1 : 0, Ashveil.Gameplay.Companions.CompanionClient.Downed ? 1 : 0,
                Ashveil.Gameplay.Companions.CompanionClient.Command, Ashveil.Gameplay.Companions.CompanionClient.Level, h != null ? h.Fraction : -1f,
                CastBarState.Active ? CastBarState.Progress : -1f, CastBarState.Label, CastBarState.Interrupted ? 1 : 0);
        }

        private static string SalvageState() => string.Format(CultureInfo.InvariantCulture,
            "unlocked={0};count={1};rare={2};last={3};cast={4:0.00};castLabel={5};interrupted={6}",
            SalvageClient.Unlocked ? 1 : 0, SalvageClient.Salvages, SalvageClient.RareSalvages, SalvageClient.LastSalvage,
            CastBarState.Active ? CastBarState.Progress : -1f, CastBarState.Label, CastBarState.Interrupted ? 1 : 0);

        private string MountState()
        {
            var m = GetComponent<Ashveil.Gameplay.Characters.CharacterMotor>();
            var mv = GetComponent<Ashveil.Gameplay.Mounts.MountVisual>();
            return string.Format(CultureInfo.InvariantCulture,
                "has={0};name={1};mounted={2};motor={3};speed={4:0.000};visible={5};cast={6:0.00};castLabel={7};interrupted={8}",
                Ashveil.Gameplay.Mounts.MountClient.HasMount ? 1 : 0, Ashveil.Gameplay.Mounts.MountClient.Name,
                Ashveil.Gameplay.Mounts.MountClient.Mounted ? 1 : 0, m != null && m.IsMounted ? 1 : 0, m != null ? m.State.MountSpeed : -1f,
                mv != null && mv.Visible ? 1 : 0, Ashveil.Gameplay.Characters.CastBarState.Active ? Ashveil.Gameplay.Characters.CastBarState.Progress : -1f,
                Ashveil.Gameplay.Characters.CastBarState.Label, Ashveil.Gameplay.Characters.CastBarState.Interrupted ? 1 : 0);
        }

        private static NetworkObject Find(int id)
        {
            if (InstanceFinder.ClientManager == null) return null;
            return InstanceFinder.ClientManager.Objects.Spawned.TryGetValue(id, out NetworkObject nob) ? nob : null;
        }

        /// <summary>Uhr und Licht dieses Rechners: "h=15.52;day=0;moon=0;int=2.10;sky=0.50;night=0.00;syncs=1;hud=…;chat=…".</summary>
        /// <summary>Himmel dieses Rechners: Weltstunde und daraus berechneter Zustand (Vergleich Server/Client).</summary>
        public static string SkyState()
        {
            var c = CultureInfo.InvariantCulture;
            var d = Ashveil.Rendering.Sky.SkyDirector.Active;
            if (d == null) return "";
            double t = Ashveil.Core.WorldClock.TotalHours;
            var s = d.Evaluate(t);
            return $"t={t.ToString("R", c)};cov={s.CloudCoverage.ToString("0.00000", c)};type={s.CloudType};from={s.CloudFrom};" +
                   $"ax={s.MoonADirection.x.ToString("0.00000", c)};ay={s.MoonADirection.y.ToString("0.00000", c)};az={s.MoonADirection.z.ToString("0.00000", c)};" +
                   $"pa={s.MoonAPhase.ToString("0.00000", c)};pb={s.MoonBPhase.ToString("0.00000", c)};tr={s.SunTransmittance.ToString("0.00000", c)};q={Ashveil.Rendering.Sky.SkyDirector.Quality}";
        }

        public static string ClockState()
        {
            var c = CultureInfo.InvariantCulture;
            var cycle = Ashveil.Rendering.DayNightCycle.Active;
            Light sun = RenderSettings.sun;
            NetworkWorldTime own = null;
            foreach (NetworkWorldTime t in Object.FindObjectsByType<NetworkWorldTime>(FindObjectsSortMode.None)) if (t.IsOwner) own = t;
            var hud = Object.FindAnyObjectByType<Ashveil.UI.HudRoot>();
            return $"h={Ashveil.Core.WorldClock.Hours.ToString("0.00", c)};day={Ashveil.Core.WorldClock.Day};moon={(cycle != null && cycle.MoonActive ? 1 : 0)};" +
                   $"int={(sun != null ? sun.intensity : -1f).ToString("0.00", c)};sky={RenderSettings.ambientSkyColor.grayscale.ToString("0.00", c)};" +
                   $"night={Ashveil.Rendering.DayNightCycle.Night.ToString("0.00", c)};elev={(sun != null ? Mathf.DeltaAngle(0f, sun.transform.eulerAngles.x) : 0f).ToString("0", c)};" +
                   $"syncs={(own != null ? own.ClientSyncs : -1)};cycle={(cycle != null ? 1 : 0)};hud={(hud != null ? hud.ClockText : "")};chat={Ashveil.UI.SocialUi.LastLocal}";
        }

        public static string NpcState(Ashveil.Gameplay.Npcs.NpcRoutine r)
        {
            var c = CultureInfo.InvariantCulture;
            Vector3 p = r.transform.position;
            return $"x={p.x.ToString("0.00", c)};y={p.y.ToString("0.00", c)};z={p.z.ToString("0.00", c)};act={r.Activity};walk={(r.Walking ? 1 : 0)};" +
                   $"sleep={(r.Sleeping ? 1 : 0)};step={r.StepIndex};note={r.CurrentStep.note}";
        }

        private string EmoteState()
        {
            var ne = GetComponent<NetworkEmote>();
            EmotePoser p = ne != null ? ne.Poser : null;
            return $"sync={(ne != null ? ne.Current : EmoteId.None)};poser={(p != null ? p.Current : EmoteId.None)};active={(p != null && p.Active ? 1 : 0)};" +
                   $"elapsed={(p != null ? p.Elapsed : 0f).ToString("0.0", CultureInfo.InvariantCulture)};msg={EmoteClient.LastMessage}";
        }

        public static string Describe(NetworkInventory inv, WeaponUser w)
        {
            var sb = new StringBuilder();
            sb.Append("bag=");
            for (int i = 0; i < inv.Bag.Length; i++) sb.Append(inv.Bag[i]).Append(',');
            sb.Append(";equip=");
            for (int i = 0; i < inv.Equipment.Length; i++) sb.Append(inv.Equipment[i]).Append(',');
            if (w != null)
                sb.Append($";melee={(w.MeleeWeapon != null ? w.MeleeWeapon.Id : "-")};ranged={(w.RangedWeapon != null ? w.RangedWeapon.Id : "-")}");
            return sb.ToString();
        }

        private static string DescribeLoot(NetworkInventory inv)
        {
            var sb = new StringBuilder("loot=");
            foreach (ItemStack s in inv.Loot) sb.Append(s).Append(',');
            return sb.ToString();
        }

        private string DescribePos()
        {
            var m = GetComponent<NetworkMotion>();
            Vector3 p = transform.position;
            return string.Format(CultureInfo.InvariantCulture, "pos={0:0.00} {1:0.00} {2:0.00};corr={3};maxcorr={4:0.00};maxinfo={5}",
                p.x, p.y, p.z, m != null ? m.Corrections : -1, m != null ? m.MaxCorrection : 0f, m != null ? m.MaxCorrectionInfo : "-");
        }

        private static string DescribeSocial(NetworkSocial s)
        {
            var sb = new StringBuilder("party=");
            foreach (Ashveil.Gameplay.Social.PartyMemberView m in s.Party)
                sb.Append(m.Leader ? "*" : "").Append(m.Name).Append(m.Online ? "" : "(off)").Append(m.Self ? "(ich)" : "").Append(',');
            sb.Append(";invite=").Append(s.PendingInvite ?? "-").Append(";chat=\n").Append(s.DescribeChat(6));
            return sb.ToString();
        }

        private static string DescribeProgress(NetworkProgression p)
        {
            var sb = new StringBuilder();
            sb.Append("lvl=").Append(p.Level).Append(";xp=").Append(p.Xp).Append(";money=").Append(p.Money).Append(";quests=");
            foreach (Ashveil.Gameplay.Quests.QuestView v in p.Quests)
            {
                if (v.Def == null) continue;
                sb.Append(v.Def.Id).Append(':').Append(string.Join(".", v.Progress)).Append(v.Complete ? "!" : "").Append(',');
            }
            sb.Append(";dialog=").Append(p.DialogNpc != null ? p.DialogNpc.Id : "-");
            return sb.ToString();
        }

        private static string DescribeBar()
        {
            var sb = new StringBuilder();
            IActionBarService s = ActionBarClient.Current;
            if (s != null && s.Slots != null)
                foreach (ActionBarEntry e in s.Slots)
                    if (!e.IsEmpty) sb.Append(e.page == 0 ? 'A' : 'B').Append(e.slot + 1).Append('=').Append(e.referenceId).Append(',');
            return "bar=" + sb + ";page=" + ActionBarInput.ActivePage;
        }

        private static string DescribeBindings()
        {
            var b = Ashveil.Core.Settings.InputBindingsStore.Current;
            return "slots=" + string.Join(",", b.slots) + ";swap=" + b.swapKey + ";toggle=" + (b.toggleMode ? 1 : 0);
        }

        private static float F(string[] a, int i) =>
            i < a.Length && float.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

        private static byte B(string s) => byte.TryParse(s, out byte v) ? v : (byte)0;

        private static string SpellState()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("known=");
            foreach (var kv in Ashveil.Gameplay.Abilities.SpellClient.Known) sb.Append(kv.Key).Append(':').Append(kv.Value).Append(',');
            sb.Append(";res=").Append(Ashveil.Gameplay.Abilities.SpellClient.Resource)
              .Append(";cur=").Append(Ashveil.Gameplay.Abilities.SpellClient.ResourceCurrent.ToString("0", CultureInfo.InvariantCulture))
              .Append(";max=").Append(Ashveil.Gameplay.Abilities.SpellClient.ResourceMax.ToString("0", CultureInfo.InvariantCulture))
              .Append(";free=").Append(Ashveil.Gameplay.Abilities.SpellClient.TalentPointsFree)
              .Append(";spent=").Append(Ashveil.Gameplay.Abilities.SpellClient.TalentPointsSpent)
              .Append(";casting=").Append(Ashveil.Gameplay.Abilities.SpellClient.CastingSpell)
              .Append(";swing=").Append(Ashveil.Gameplay.Abilities.SpellClient.QueuedNextSwing);
            return sb.ToString();
        }

        private static int I(string[] a, int i) => i < a.Length && int.TryParse(a[i], out int v) ? v : -1;
    }
}
