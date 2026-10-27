using System.Collections.Generic;
using Ashveil.Core;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Npcs;
using Ashveil.Gameplay.Visuals;
using Ashveil.World;
using UnityEditor;
using UnityEngine;
using L = Ashveil.EditorTools.StartCampLayout;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// M8.3: Bewohner des Startlagers mit echten Menschenkoerpern und Tagesablaeufen (NpcRoutine).
    /// Dienst-NPCs (Auftraege, Haendler, Training) bleiben rund um die Uhr im Lager und ansprechbar: tagsueber an
    /// ihrem Platz, mittags teils an den Langtischen, abends am Wachtfeuer, nachts am kleinen Feuer bei den Zelten.
    /// Dazu fuenf Bewohner ohne Dienst (Koechin, Holzhacker, zwei Wachen, eine alte Erzaehlerin), die auch schlafen.
    /// Soziale Momente: Mittagessen an den Langtischen, Abend am Feuer (Erzaehlen, Lachen, Jorns Tanz ab 21 Uhr).
    /// </summary>
    public static partial class SandboxSceneBuilder
    {
        private struct NpcSpec
        {
            public string Name, Title, NpcId, Look;
            public bool Female, Quest;
            public string[] Gear;
            public RoutineStep[] Steps;
            public NpcCart Cart; // M8.5: Handkarren (optional)
            public float Speed;  // M8.6: Gehgeschwindigkeit (0 = Standard 1,35 m/s; Fuhrwerke schneller)
        }

        // ------------------------------------------------------------------ Orte

        private static float Ground(float x, float z) => KarethTerrainBuilder.Height(x, z);

        private static RoutineStep At(float from, Vector3 pos, float yaw, string activity, float seat = 0f, bool carry = false, string note = null) =>
            new RoutineStep { from = from, pos = pos, yaw = yaw, activity = activity, seat = seat, carry = carry, note = note ?? activity };

        private static RoutineStep At(float from, (Vector3 p, float yaw) spot, string activity, float seat = 0f, bool carry = false, string note = null) =>
            At(from, spot.p, spot.yaw, activity, seat, carry, note);

        private static (Vector3, float) Spot(float x, float z, float yaw) => (new Vector3(x, Ground(x, z), z), yaw);

        private static (Vector3, float) Facing(float x, float z, Vector2 lookAt)
        {
            Vector2 d = lookAt - new Vector2(x, z);
            return Spot(x, z, Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg);
        }

        /// <summary>Sitzplatz k (0–11) auf den Staemmen am Wachtfeuer, Blick ins Feuer.</summary>
        private static (Vector3, float) FireSeat(int k)
        {
            float a = 30f + (k / 2) * 60f;
            Vector3 log = OnRing(a, 3.8f);
            Vector3 tangent = Quaternion.Euler(0f, a, 0f) * Vector3.right;
            Vector3 p = log + tangent * (k % 2 == 0 ? -0.7f : 0.7f);
            return Spot(p.x, p.z, a + 180f);
        }

        private const float LogSeat = 0.42f, BenchSeat = 0.44f;

        /// <summary>Platz an Langtisch t (0/1), Seite north (Bank noerdlich, Blick nach Sueden), i = 0–3 von West nach Ost.</summary>
        private static (Vector3, float) TableSeat(int t, bool north, int i)
        {
            float x = -15f - 1.8f + i * 1.2f, z = -25f - t * 5f + (north ? 0.95f : -0.95f);
            return Spot(x, z, north ? 180f : 0f);
        }

        /// <summary>Platz k (0–5) am kleinen Feuer der Zelte (auf dem Boden).</summary>
        private static (Vector3, float) TentFire(int k)
        {
            float a = k * 60f + 15f;
            Vector2 p = L.Tents + new Vector2(Mathf.Sin(a * Mathf.Deg2Rad), Mathf.Cos(a * Mathf.Deg2Rad)) * 1.8f;
            return Facing(p.x, p.y, L.Tents);
        }

        /// <summary>Schlafplatz im Zelt k (0–4) – derselbe Winkel wie im Zeltaufbau.</summary>
        private static (Vector3, float) TentBed(int k)
        {
            float a = new[] { 20f, 90f, 160f, 230f, 300f }[k];
            Vector2 p = L.Tents + new Vector2(Mathf.Sin(a * Mathf.Deg2Rad), Mathf.Cos(a * Mathf.Deg2Rad)) * 5.6f;
            return Spot(p.x, p.y, a);
        }

        private static (Vector3, float) Post(Vector3 p) => Facing(p.x, p.z, Vector2.zero);

        // ------------------------------------------------------------------ Bewohner

        private static List<NpcSpec> CampNpcs()
        {
            Vector3 hallTable = GiantBuilder.Active ? GiantBuilder.World(new Vector3(0f, 1.2f, -70.4f)) : new Vector3(2f, 0f, -24f);
            Vector3 hallChair = GiantBuilder.Active ? GiantBuilder.World(new Vector3(0f, 1.7f, -80.0f)) : new Vector3(2f, 0f, -26f);
            var anvil = Spot(-13.6f, -3.6f, 255f);
            var forgeDrop = Spot(-12.8f, -7.2f, 200f);
            var kitchenDrop = Spot(-11.2f, -19.8f, 250f);
            var chop = Spot(-20.3f, -10.4f, 215f);
            var cook = Spot(-9f, -22.2f, 0f);
            var stores = Spot(-17.5f, -33.8f, 200f);
            var northGateW = Spot(-3.4f, 25.2f, 0f);
            var northGateE = Spot(3.4f, 25.2f, 0f);
            var eastGate = Facing(27.2f, -8.6f, new Vector2(40f, -12f));
            var westGate = Facing(-26.6f, 8.8f, new Vector2(-40f, 12f));
            var story = Facing(-5.4f, 1.6f, Vector2.zero);
            var dance = Spot(0f, 5.9f, 180f);

            return new List<NpcSpec>
            {
                // --- Dienste (immer ansprechbar)
                new NpcSpec
                {
                    Name = "Hauptmann Oskar Bralt", Title = "Aufträge", NpcId = QuestContentBuilder.Bralt, Quest = true,
                    Look = "1|b0|s1|h2|c5|d1|a4|e2|m1", Gear = new[] { "warden_cuirass", "plated_greaves", "iron_sabatons", "grey_wool_cloak" },
                    Steps = new[]
                    {
                        At(0f, hallChair, 0f, "Sit", 0.6f, note: "sitzt auf dem Hauptmannsstuhl"),
                        At(6.5f, hallTable, 0f, "FoldArms|Talk|Stand", note: "am Kartentisch"),
                        At(21f, hallChair, 0f, "Sit", 0.6f, note: "sitzt auf dem Hauptmannsstuhl")
                    }
                },
                new NpcSpec
                {
                    Name = "Kräuterin Maelis", Title = "Heiltränke & Kräuter", NpcId = QuestContentBuilder.Maelis, Female = true,
                    Look = "1|b1|s0|h5|c2|a1|e5|m3", Gear = new[] { "frayed_gambeson", "travel_trousers", "weaver_sandals", "ember_sash" },
                    Steps = new[]
                    {
                        At(0f, TentFire(0), "Sit", 0f, note: "wacht am Zeltfeuer"),
                        At(6f, Post(OnRing(28f, 12.6f)), "Talk|FoldArms|Stand", note: "am Kraeuterstand"),
                        At(20f, FireSeat(0), "SitTalk|Sit", LogSeat, note: "abends am Wachtfeuer"),
                        At(23f, TentFire(0), "Sit", 0f, note: "wacht am Zeltfeuer")
                    }
                },
                new NpcSpec
                {
                    Name = "Pfeilmacher Gerrit", Title = "Pfeile, Köcher & Taschen", NpcId = QuestContentBuilder.Gerrit,
                    Look = "1|b0|s3|h1|c0|d3|a2|e0|m0", Gear = new[] { "ashen_leather_jerkin", "hunter_breeches", "stalker_boots", "travel_hood" },
                    Steps = new[]
                    {
                        At(0f, TentFire(1), "Sit", 0f, note: "wacht am Zeltfeuer"),
                        At(6.25f, Post(OnRing(50f, 12.6f)), "FoldArms|Talk|Stand", note: "am Munitionsstand"),
                        At(20.25f, FireSeat(2), "SitTalk|Sit", LogSeat, note: "abends am Wachtfeuer"),
                        At(23.25f, TentFire(1), "Sit", 0f, note: "wacht am Zeltfeuer")
                    }
                },
                new NpcSpec
                {
                    Name = "Schmied Dorn", Title = "Rüstung & Reparatur", NpcId = QuestContentBuilder.Dorn,
                    Look = "1|b0|s5|h0|c1|d1|a1|e1|m2", Gear = new[] { "chainmail_shirt", "travel_trousers", "iron_sabatons", "iron_gauntlets" },
                    Steps = new[]
                    {
                        At(0f, TentFire(2), "Sit", 0f, note: "wacht am Zeltfeuer"),
                        At(6f, anvil, "Chop|Chop|Stand", note: "schmiedet am Amboss"),
                        At(12.5f, TableSeat(0, true, 0), "SitTalk", BenchSeat, note: "Mittag an den Langtischen"),
                        At(13.25f, anvil, "Chop|Stand", note: "schmiedet am Amboss"),
                        At(20f, FireSeat(4), "SitTalk|Sit", LogSeat, note: "abends am Wachtfeuer"),
                        At(23f, TentFire(2), "Sit", 0f, note: "wacht am Zeltfeuer")
                    }
                },
                new NpcSpec
                {
                    Name = "Waffenmeisterin Ilse Harrow", Title = "Waffen & Training", NpcId = QuestContentBuilder.Ilse, Female = true, Quest = true,
                    Look = "1|b1|s2|h7|c0|a0|e3|m1", Gear = new[] { "ashen_leather_jerkin", "hunter_shoulderguards", "hunter_breeches", "stalker_boots", "stalker_gloves" },
                    Steps = new[]
                    {
                        At(0f, TentFire(3), "Sit", 0f, note: "wacht am Zeltfeuer"),
                        At(6.5f, Spot(11.5f, -11.5f, 160f), "FoldArms|Talk|Shake", note: "beaufsichtigt den Trainingsplatz"),
                        At(12.5f, TableSeat(0, false, 1), "SitTalk", BenchSeat, note: "Mittag an den Langtischen"),
                        At(13.25f, Spot(11.5f, -11.5f, 160f), "FoldArms|Talk", note: "beaufsichtigt den Trainingsplatz"),
                        At(20.5f, FireSeat(6), "SitTalk|Sit", LogSeat, note: "abends am Wachtfeuer"),
                        At(23.5f, TentFire(3), "Sit", 0f, note: "wacht am Zeltfeuer")
                    }
                },
                new NpcSpec
                {
                    Name = "Kartograf", Title = "Wegweiser", NpcId = QuestContentBuilder.Cartographer,
                    Look = "1|b0|s0|h2|c5|d5|a5|e4|m0", Gear = new[] { "travel_mantle", "frayed_gambeson", "travel_trousers", "foot_wraps", "travel_hood" },
                    Steps = new[]
                    {
                        At(0f, TentFire(4), "Sit", 0f, note: "wacht am Zeltfeuer"),
                        At(7f, Post(new Vector3(2.5f, 0f, 20.5f)), "Talk|FoldArms|Stand", note: "am Nordtor"),
                        At(12.5f, TableSeat(0, true, 2), "SitTalk", BenchSeat, note: "Mittag an den Langtischen"),
                        At(13.25f, Post(new Vector3(2.5f, 0f, 20.5f)), "Talk|FoldArms", note: "am Nordtor"),
                        At(19.5f, FireSeat(8), "SitTalk", LogSeat, note: "abends am Wachtfeuer"),
                        At(23f, TentFire(4), "Sit", 0f, note: "wacht am Zeltfeuer")
                    }
                },

                // --- Bewohner ohne Dienst (schlafen nachts in den Zelten)
                new NpcSpec
                {
                    // #28: Auftragsgeberin K-A/K-B in Kareth v4 (Kennung; die Aufträge gelten nur dort, SceneQuestFilter)
                    Name = "Wenna", Title = "Köchin", NpcId = Ashveil.Data.KarethCatalog.Wenna, Female = true,
                    Look = "1|b1|s4|h4|c1|a2|e0|m0", Gear = new[] { "frayed_gambeson", "travel_trousers", "weaver_sandals", "rope_belt" },
                    Steps = new[]
                    {
                        At(0f, TentBed(0), "Sleep", note: "schlaeft"),
                        At(5.5f, cook, "Kneel|Stand", note: "schuert das Kochfeuer"),
                        At(9f, stores, "Stand", note: "holt Vorraete"),
                        At(9.4f, cook, "Kneel|Stand|Talk", carry: true, note: "kocht"),
                        At(12.5f, TableSeat(0, false, 0), "SitTalk", BenchSeat, note: "Mittag an den Langtischen"),
                        At(13.25f, cook, "Kneel|Stand", note: "kocht"),
                        At(15f, TableSeat(1, true, 1), "SitTalk|Sit", BenchSeat, note: "Pause am Tisch"),
                        At(17f, cook, "Kneel|Stand", note: "kocht das Abendessen"),
                        At(20f, FireSeat(1), "SitTalk", LogSeat, note: "abends am Wachtfeuer"),
                        At(22f, TentBed(0), "Sleep", note: "schlaeft")
                    }
                },
                new NpcSpec
                {
                    Name = "Jorn", Title = "Holzhacker", NpcId = Ashveil.Data.KarethCatalog.Jorn, // #28: K-C in Kareth v4
                    Look = "1|b0|s2|h6|c4|d1|a3|e2|m4", Gear = new[] { "ashen_leather_jerkin", "travel_trousers", "stalker_boots" },
                    Steps = new[]
                    {
                        At(0f, TentBed(1), "Sleep", note: "schlaeft"),
                        At(6f, chop, "Chop", note: "hackt Holz"),
                        At(10.5f, forgeDrop, "Stand", carry: true, note: "bringt Holz zur Schmiede"),
                        At(10.8f, chop, "Chop", note: "hackt Holz"),
                        At(12.5f, TableSeat(0, true, 1), "SitTalk", BenchSeat, note: "Mittag an den Langtischen"),
                        At(13.25f, chop, "Chop", note: "hackt Holz"),
                        At(17.5f, kitchenDrop, "Stand", carry: true, note: "bringt Holz zur Kochstelle"),
                        At(17.8f, TableSeat(1, false, 2), "SitTalk|Sit", BenchSeat, note: "Feierabend am Tisch"),
                        At(20f, FireSeat(3), "SitTalk", LogSeat, note: "abends am Wachtfeuer"),
                        At(21f, dance, "Dance|Talk|Dance", note: "tanzt am Feuer"),
                        At(22f, FireSeat(3), "SitTalk", LogSeat, note: "abends am Wachtfeuer"),
                        At(23f, TentBed(1), "Sleep", note: "schlaeft")
                    }
                },
                new NpcSpec
                {
                    Name = "Brenn", Title = "Wache (Tag)",
                    Look = "1|b0|s1|h1|c1|d2|a4|e1|m1", Gear = new[] { "dented_helm", "chainmail_shirt", "plated_greaves", "iron_sabatons", "grey_wool_cloak" },
                    Steps = new[]
                    {
                        At(0f, TentBed(2), "Sleep", note: "schlaeft"),
                        At(6f, northGateW, "FoldArms|Stand", note: "Wache am Nordtor"),
                        At(12f, TableSeat(1, true, 0), "SitTalk", BenchSeat, note: "Mittag an den Langtischen"),
                        At(12.75f, eastGate, "FoldArms|Stand", note: "Wache am Osttor"),
                        At(15f, northGateW, "FoldArms|Stand", note: "Wache am Nordtor"),
                        At(18f, TableSeat(1, false, 0), "SitTalk", BenchSeat, note: "Abendessen"),
                        At(19f, FireSeat(5), "SitTalk|Sit", LogSeat, note: "abends am Wachtfeuer"),
                        At(23f, TentBed(2), "Sleep", note: "schlaeft")
                    }
                },
                new NpcSpec
                {
                    Name = "Tilda", Title = "Wache (Nacht)", Female = true,
                    Look = "1|b1|s3|h3|c0|a3|e6|m2", Gear = new[] { "dented_helm", "chainmail_shirt", "hunter_breeches", "stalker_boots", "wolfhide_cloak" },
                    Steps = new[]
                    {
                        At(0f, northGateE, "Torch|Lantern", note: "Nachtwache am Nordtor"),
                        At(3f, eastGate, "Torch", note: "Nachtwache am Osttor"),
                        At(6f, TableSeat(1, true, 2), "SitTalk", BenchSeat, note: "Fruehstueck nach der Wache"),
                        At(7f, TentBed(3), "Sleep", note: "schlaeft"),
                        At(14f, TableSeat(1, false, 3), "SitTalk|Sit", BenchSeat, note: "am Tisch"),
                        At(16f, FireSeat(7), "SitTalk", LogSeat, note: "am Wachtfeuer"),
                        At(18f, northGateE, "Lantern|FoldArms", note: "Nachtwache am Nordtor"),
                        At(21f, westGate, "Torch", note: "Nachtwache am Westtor")
                    }
                },
                new NpcSpec
                {
                    Name = "Hedda", Title = "Erzählerin", Female = true,
                    Look = "1|b1|s1|h4|c5|a5|e4|m0", Gear = new[] { "travel_mantle", "weaver_robe", "weaver_sandals" },
                    Steps = new[]
                    {
                        At(0f, TentBed(4), "Sleep", note: "schlaeft"),
                        At(7.5f, FireSeat(9), "Sit", LogSeat, note: "waermt sich am Feuer"),
                        At(9f, TableSeat(1, false, 1), "SitTalk", BenchSeat, note: "plaudert an den Tischen"),
                        At(11f, story, "Talk", note: "erzaehlt am Feuer"),
                        At(12.5f, TableSeat(0, false, 2), "SitTalk", BenchSeat, note: "Mittag an den Langtischen"),
                        At(14f, Spot(-3.0f, 5.2f, 200f), "Kneel", note: "streichelt die Katze"),
                        At(14.4f, FireSeat(10), "Sit|SitTalk", LogSeat, note: "sitzt am Feuer"),
                        At(17f, TentFire(5), "Sit", 0f, note: "am Zeltfeuer"),
                        At(19.5f, story, "Talk", note: "erzaehlt am Feuer"),
                        At(21.5f, FireSeat(10), "SitTalk", LogSeat, note: "abends am Wachtfeuer"),
                        At(22.5f, TentBed(4), "Sleep", note: "schlaeft")
                    }
                }
            };
        }

        private static void BuildCampNpcs(Transform camp)
        {
            Transform npcs = K.Group("NPCs (Tagesablaeufe)", camp);
            List<NpcSpec> list = CampNpcs();
            list.AddRange(SceneNpcs()); // M8.5: Lazarett, Schreiber, Zimmermann, Haendler mit Karren
            for (int i = 0; i < list.Count; i++) NpcFigure(npcs, list[i], i + 1);
        }

        /// <summary>NPC mit Menschenkoerper, Namensschild, Tagesablauf; Dienst-NPCs zusaetzlich mit NpcIdentity.</summary>
        private static void NpcFigure(Transform parent, NpcSpec spec, int seed)
        {
            RoutineStep first = spec.Steps[0];
            Transform npc = K.Group(spec.Name, parent, first.pos, new Vector3(0f, first.yaw, 0f));

            // Klick-/Kollisionskoerper
            var col = npc.gameObject.AddComponent<CapsuleCollider>();
            col.direction = 1; col.radius = 0.35f; col.height = 1.8f; col.center = new Vector3(0f, 0.9f, 0f);

            // Koerper (Quaternius, wie Spieler) – ohne Import bleibt die Kapsel sichtbar
            Transform visual = K.Group("Visual", npc);
            GameObject model = null;
            Animator anim = null;
            EmotePoser poser = null;
            if (QuaterniusImport.IsImported)
            {
                string path = CharacterBodies.PathFor(spec.Female); // Art-Pass 3: neue Basiskoerper, falls eingeschaltet
                var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                    spec.Female ? PlayerAnimatorBuilder.FemaleOverridePath : PlayerAnimatorBuilder.ControllerPath);
                model = PlayerRigBuilder.InstantiateModel(path, "Model", visual, controller);
            }
            if (model != null)
            {
                anim = model.GetComponent<Animator>();
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                // Keine Standhaltung (StancePoser) fuer NPCs: kostet je Figur CPU, NPCs zeigen ihre Taetigkeit per Animation
                poser = model.AddComponent<EmotePoser>();
                K.SetRef(poser, "animator", anim);
                model.AddComponent<NpcBody>().Configure(spec.Look, spec.Gear, npc);
                npc.gameObject.AddComponent<CharacterLod>();
            }
            else
            {
                K.Prim(PrimitiveType.Capsule, "Body (Platzhalter)", visual, new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.9f, 0.7f), Vector3.zero,
                    spec.Quest ? _npcQuest : _npc, false);
            }

            // Markierungen (! / ?) setzt die Oberflaeche je Spieler – deshalb freundlicher Stil ohne festes "!"
            WorldLabel label = K.Label(npc, new Vector3(0f, 2.15f, 0f), spec.Name, spec.Title, WorldLabelStyle.Friendly, 40f);
            if (!string.IsNullOrEmpty(spec.NpcId)) npc.gameObject.AddComponent<Ashveil.Gameplay.Quests.NpcIdentity>().Configure(spec.NpcId);

            var routine = npc.gameObject.AddComponent<NpcRoutine>();
            routine.Configure(spec.Steps, seed);
            routine.Bind(anim, poser, visual.gameObject, label.gameObject, col);
            if (spec.Cart != null) routine.BindCart(spec.Cart);
            if (spec.Speed > 0f) routine.SetWalkSpeed(spec.Speed);

            K.SetLayerRecursively(npc.gameObject, LayerMask.NameToLayer(GameLayers.Interactable));
            K.MakeDynamic(npc.gameObject);
            EditorUtility.SetDirty(routine);
        }
    }
}
