using System.Collections.Generic;
using Ashveil.Gameplay.Npcs;
using Ashveil.World;
using UnityEngine;
using L = Ashveil.EditorTools.StartCampLayout;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// M8.5: Lagerszenen nach der Art-Direction-Bibel (§ 43, § 48) – „nicht jede Szene muss eine Quest sein“:
    ///   Lazarett (Ost)        – Heilerin Nessa versorgt den verwundeten Hollin, ein zweiter schlaeft unter einer Decke
    ///   Schreibpult (Nordtor) – Schreiber Veit traegt Neuankoemmlinge ins Register ein
    ///   Palisade (Nordost)    – Zimmermann Ulf spitzt an der Werkbank Pfaehle und traegt sie quer durchs Lager zur Luecke
    ///   Handkarren            – Haendler Pell schiebt seinen Karren mit Getraenken und Heiltraenken durchs Lager
    /// Alle folgen der Weltuhr wie die uebrigen Bewohner (NpcRoutine); der Karren haengt am Tagesablauf (NpcCart).
    /// </summary>
    public static partial class SandboxSceneBuilder
    {
        // Lazarett: Mitte, Blick zum Feuer (lokal +z zeigt zum Feuer)
        private static readonly Vector2 Infirmary = new Vector2(21f, -1f);
        private static readonly Vector2 ScribeDesk = new Vector2(-6f, 19.8f);
        private const float RepairAngle = 40f;
        private static readonly Vector2 PellPark = new Vector2(12.8f, -27.2f);

        private static NpcCart _pellCart;

        private static float InfirmaryYaw => YawTowardFire(Infirmary);

        /// <summary>Weltpunkt zu einem lokalen Punkt einer um yaw gedrehten Gruppe bei c (Hoehe = Gelaende).</summary>
        private static Vector3 LocalToWorld(Vector2 c, float yaw, Vector3 local)
        {
            Vector3 p = new Vector3(c.x, 0f, c.y) + Quaternion.Euler(0f, yaw, 0f) * local;
            p.y = Ground(p.x, p.z);
            return p;
        }

        private static (Vector3, float) LocalSpot(Vector2 c, float yaw, Vector3 local, float localYaw) =>
            (LocalToWorld(c, yaw, local), yaw + localYaw);

        // ------------------------------------------------------------------ Aufbauten (vor dem NavMesh)

        private static void BuildCampScenes(Transform camp)
        {
            Transform root = K.Group("Lagerszenen", camp);
            BuildInfirmary(K.Group("Lazarett", root));
            BuildScribeDesk(K.Group("Schreibpult", root));
            BuildRepairSite(K.Group("Palisade in Arbeit", root));
            _pellCart = BuildHandcart(root);
        }

        private static void BuildInfirmary(Transform parent)
        {
            float yaw = InfirmaryYaw;
            Transform inf = K.Group("Unterstand", parent, Lage("req:lazarett", new Vector3(Infirmary.x, Ground(Infirmary.x, Infirmary.y), Infirmary.y)), new Vector3(0f, LageYaw("req:lazarett", yaw), 0f));
            Material linen = K.Mat("Linen", new Color(0.72f, 0.69f, 0.62f));
            Material blood = K.Mat("Linen_Blood", new Color(0.42f, 0.13f, 0.11f));
            Material blanket = K.Mat("Blanket_Wine", new Color(0.33f, 0.12f, 0.12f)); // Weinrot als warmer Akzent (Bibel § 47)
            Material skin = K.Mat("Skin_Pale", new Color(0.62f, 0.5f, 0.42f));

            // Unterstand: vier Pfosten, Plane schraeg, Rueckwand
            for (int ix = -1; ix <= 1; ix += 2)
            for (int iz = 0; iz <= 1; iz++)
                K.Block("Pfosten", inf, new Vector3(ix * 3f, 0f, iz == 0 ? -1.9f : 1.3f), new Vector3(0.18f, iz == 0 ? 2.5f : 2.9f, 0.18f), _woodDark);
            K.Box("Plane", inf, new Vector3(0f, 2.75f, -0.3f), new Vector3(6.6f, 0.06f, 3.8f), _canvas, new Vector3(8f, 0f, 0f), false);
            K.Box("Rueckwand", inf, new Vector3(0f, 1.2f, -1.95f), new Vector3(6.2f, 2.4f, 0.06f), _canvas);

            // Zwei Pritschen (quer), links Hollin, rechts ein Schlafender unter der Decke
            for (int s = -1; s <= 1; s += 2)
            {
                Transform cot = K.Group(s < 0 ? "Pritsche Hollin" : "Pritsche", inf, new Vector3(s * 1.5f, 0f, -0.75f), Vector3.zero);
                K.Box("Rahmen", cot, new Vector3(0f, 0.38f, 0f), new Vector3(2.0f, 0.1f, 0.8f), _wood);
                for (int lx = -1; lx <= 1; lx += 2)
                for (int lz = -1; lz <= 1; lz += 2)
                    K.Block("Bein", cot, new Vector3(lx * 0.9f, 0f, lz * 0.33f), new Vector3(0.08f, 0.36f, 0.08f), _woodDark);
                K.Box("Laken", cot, new Vector3(0f, 0.45f, 0f), new Vector3(1.9f, 0.05f, 0.74f), linen, default, false);
            }
            // Der Schlafende (Requisit): Decke mit Koerperform, Kopf mit Verband
            K.Box("Decke", inf, new Vector3(1.35f, 0.58f, -0.75f), new Vector3(1.5f, 0.22f, 0.62f), blanket, default, false);
            K.Prim(PrimitiveType.Sphere, "Kopf", inf, new Vector3(2.3f, 0.6f, -0.75f), new Vector3(0.24f, 0.22f, 0.26f), Vector3.zero, skin, false);
            K.Prim(PrimitiveType.Sphere, "Verband", inf, new Vector3(2.32f, 0.66f, -0.75f), new Vector3(0.26f, 0.14f, 0.28f), Vector3.zero, linen, false);
            // Verbandstisch, Waschschuessel, Eimer, blutige Tuecher, Kraeuterbuendel
            K.Block("Tisch", inf, new Vector3(-2.6f, 0f, 0.4f), new Vector3(0.9f, 0.8f, 0.6f), _wood, 90f);
            K.Prim(PrimitiveType.Cylinder, "Schuessel", inf, new Vector3(-2.6f, 0.86f, 0.25f), new Vector3(0.42f, 0.05f, 0.42f), Vector3.zero, _bronze, false);
            for (int i = 0; i < 3; i++)
                K.Box("Binde", inf, new Vector3(-2.7f + i * 0.12f, 0.84f, 0.62f), new Vector3(0.1f, 0.07f, 0.18f), linen, new Vector3(0f, i * 25f, 0f), false);
            K.Box("Tuch", inf, new Vector3(-2.2f, 0.03f, 0.9f), new Vector3(0.4f, 0.02f, 0.3f), blood, new Vector3(0f, 30f, 0f), false);
            K.Prim(PrimitiveType.Cylinder, "Eimer", inf, new Vector3(-2.2f, 0.22f, -0.3f), new Vector3(0.36f, 0.22f, 0.36f), Vector3.zero, _woodDark, false);
            K.Box("Kraeuter", inf, new Vector3(-2.9f, 2.1f, -1.85f), new Vector3(0.5f, 0.35f, 0.08f), K.Mat("Herb_Green", new Color(0.3f, 0.45f, 0.24f)), default, false);
            K.Block("Hocker", inf, new Vector3(0.1f, 0f, 0.3f), new Vector3(0.4f, 0.45f, 0.4f), _woodDark);
            K.PointLight("Laterne", inf, new Vector3(0f, 2.3f, 0.6f), new Color(1f, 0.62f, 0.3f), 6f, 0.9f, true);
            K.Box("Laternenglas", inf, new Vector3(0f, 2.35f, 0.6f), new Vector3(0.22f, 0.3f, 0.22f), _warmWindow, default, false);
            K.Label(inf, new Vector3(0f, 3.6f, 0f), "Lazarett", "", WorldLabelStyle.Place, 35f, 0.85f);
        }

        private static void BuildScribeDesk(Transform parent)
        {
            Transform d = K.Group("Pult", parent, Lage("req:pult", new Vector3(ScribeDesk.x, Ground(ScribeDesk.x, ScribeDesk.y), ScribeDesk.y)), new Vector3(0f, LageYaw("req:pult", 90f), 0f));
            Material paper = K.Mat("Paper", new Color(0.78f, 0.74f, 0.63f));
            K.Block("Hocker", d, Vector3.zero, new Vector3(0.42f, 0.45f, 0.42f), _woodDark);
            K.Block("Pult", d, new Vector3(0f, 0f, 0.65f), new Vector3(1.3f, 0.78f, 0.6f), _wood);
            K.Box("Register", d, new Vector3(-0.1f, 0.81f, 0.6f), new Vector3(0.55f, 0.04f, 0.38f), paper, new Vector3(0f, -8f, 0f), false);
            K.Box("Einband", d, new Vector3(-0.1f, 0.795f, 0.6f), new Vector3(0.6f, 0.02f, 0.42f), K.Mat("Leather_Dark", new Color(0.25f, 0.16f, 0.1f)), new Vector3(0f, -8f, 0f), false);
            K.Prim(PrimitiveType.Cylinder, "Tintenfass", d, new Vector3(0.4f, 0.83f, 0.72f), new Vector3(0.08f, 0.05f, 0.08f), Vector3.zero, _charred, false);
            K.Box("Feder", d, new Vector3(0.38f, 0.92f, 0.72f), new Vector3(0.015f, 0.2f, 0.015f), paper, new Vector3(0f, 0f, 18f), false);
            K.Block("Schriftrollen", d, new Vector3(0.75f, 0f, 0.9f), new Vector3(0.5f, 0.55f, 0.4f), _woodDark);
            // Schild fuer Neuankoemmlinge
            K.Block("Pfahl", d, new Vector3(-0.9f, 0f, 1.1f), new Vector3(0.1f, 1.9f, 0.1f), _woodDark);
            K.Box("Tafel", d, new Vector3(-0.9f, 1.65f, 1.14f), new Vector3(0.7f, 0.4f, 0.05f), _wood, default, false);
            K.Label(d, new Vector3(-0.9f, 2.4f, 1.1f), "Register", "Neuankömmlinge", WorldLabelStyle.Place, 25f, 0.7f);
        }

        /// <summary>Kniestelle des Zimmermanns an der Palisade (innen, Blick zur Wand).</summary>
        private static (Vector3, float) RepairSpot()
        {
            Vector2 p = L.OnArc(RepairAngle, -2.1f);
            return Spot(p.x, p.y, RepairAngle);
        }

        private static void BuildRepairSite(Transform parent)
        {
            Material fresh = K.Mat("Wood_Fresh", new Color(0.62f, 0.48f, 0.3f));
            Vector2 c = L.OnArc(RepairAngle + 4.5f, -3.4f);
            Transform site = K.Group("Pfahlstapel", parent, new Vector3(c.x, Ground(c.x, c.y), c.y), new Vector3(0f, RepairAngle + 90f, 0f));
            for (int i = 0; i < 5; i++)
            {
                int row = i < 3 ? 0 : 1;
                float x = -0.45f + (row == 0 ? i : i - 3 + 0.5f) * 0.3f;
                K.Prim(PrimitiveType.Cylinder, "Neuer Pfahl", site, new Vector3(x, 0.14f + row * 0.26f, 0f), new Vector3(0.26f, 1.2f, 0.26f),
                    new Vector3(90f, 0f, 0f), fresh, row == 0);
            }
            K.Block("Holzhammer", site, new Vector3(0.9f, 0f, 0.5f), new Vector3(0.14f, 0.14f, 0.35f), _woodDark, 30f);
            // Ein frischer Pfahl lehnt schon an der Wand
            Vector2 lean = L.OnArc(RepairAngle - 3f, -1.3f);
            Transform l = K.Group("Angelehnter Pfahl", parent, new Vector3(lean.x, Ground(lean.x, lean.y), lean.y), new Vector3(0f, RepairAngle, 0f));
            K.Prim(PrimitiveType.Cylinder, "Pfahl", l, new Vector3(0f, 1.3f, -0.35f), new Vector3(0.26f, 1.35f, 0.26f), new Vector3(15f, 0f, 0f), fresh, false);
        }

        /// <summary>Handkarren des Haendlers (ohne Collider; Position setzt der Tagesablauf).</summary>
        private static NpcCart BuildHandcart(Transform parent)
        {
            Vector3 start = Lage("req:handkarren", new Vector3(PellPark.x, Ground(PellPark.x, PellPark.y), PellPark.y));
            Transform cart = K.Group("Handkarren (Haendler Pell)", parent, start, new Vector3(0f, LageYaw("req:handkarren", 30f), 0f));
            Material bottle = K.Mat("Glass_Bottle", new Color(0.22f, 0.35f, 0.3f), 0.6f);
            Material potion = K.Mat("Potion_Red", new Color(0.55f, 0.1f, 0.08f), 0.5f);
            K.Box("Ladeflaeche", cart, new Vector3(0f, 0.62f, 0.05f), new Vector3(1.0f, 0.08f, 1.3f), _wood, default, false);
            K.Box("Bord L", cart, new Vector3(-0.5f, 0.8f, 0.05f), new Vector3(0.05f, 0.32f, 1.3f), _woodDark, default, false);
            K.Box("Bord R", cart, new Vector3(0.5f, 0.8f, 0.05f), new Vector3(0.05f, 0.32f, 1.3f), _woodDark, default, false);
            K.Box("Bord vorn", cart, new Vector3(0f, 0.8f, 0.7f), new Vector3(1.0f, 0.32f, 0.05f), _woodDark, default, false);
            // Griffe zeigen nach hinten zum Haendler
            for (int s = -1; s <= 1; s += 2)
                K.Box("Griff", cart, new Vector3(s * 0.42f, 0.78f, -0.85f), new Vector3(0.05f, 0.05f, 0.7f), _wood, new Vector3(-12f, 0f, 0f), false);
            var wheels = new List<Transform>();
            for (int s = -1; s <= 1; s += 2)
            {
                GameObject w = K.Prim(PrimitiveType.Cylinder, "Rad", cart, new Vector3(s * 0.6f, 0.35f, 0.15f), new Vector3(0.7f, 0.04f, 0.7f), new Vector3(0f, 0f, 90f), _woodDark, false);
                K.Box("Speiche", w.transform, Vector3.zero, new Vector3(0.9f, 2.6f, 0.12f), _wood, default, false);
                wheels.Add(w.transform);
            }
            K.Box("Stuetze", cart, new Vector3(0f, 0.29f, 0.65f), new Vector3(0.06f, 0.58f, 0.06f), _woodDark, default, false);
            // Ladung: zwei Faesschen (Bier, Wasser), Kiste mit Flaschen und Heiltraenken, Plane
            for (int i = 0; i < 2; i++)
                K.Prim(PrimitiveType.Cylinder, "Faesschen", cart, new Vector3(-0.22f + i * 0.44f, 0.86f, 0.35f), new Vector3(0.36f, 0.2f, 0.36f), new Vector3(90f, 0f, 90f), _woodDark, false);
            K.Box("Kiste", cart, new Vector3(0f, 0.78f, -0.3f), new Vector3(0.8f, 0.24f, 0.5f), _wood, default, false);
            for (int i = 0; i < 6; i++)
                K.Prim(PrimitiveType.Cylinder, "Flasche", cart, new Vector3(-0.3f + (i % 3) * 0.3f, 0.99f, -0.42f + (i / 3) * 0.22f), new Vector3(0.08f, 0.1f, 0.08f),
                    Vector3.zero, i % 2 == 0 ? potion : bottle, false);
            K.Box("Plane", cart, new Vector3(0f, 1.02f, 0.35f), new Vector3(0.95f, 0.04f, 0.5f), _canvas, new Vector3(-6f, 0f, 0f), false);
            var nc = cart.gameObject.AddComponent<NpcCart>();
            nc.Configure(wheels.ToArray(), 0.35f);
            K.MakeDynamic(cart.gameObject);
            return nc;
        }

        // ------------------------------------------------------------------ Bewohner der Szenen

        private static List<NpcSpec> SceneNpcs()
        {
            float iy = InfirmaryYaw;
            var hollinSeat = LocalSpot(Infirmary, iy, new Vector3(-1.5f, 0f, -0.42f), 0f);        // Pritschenkante, Blick zum Feuer
            var tendHollin = LocalSpot(Infirmary, iy, new Vector3(-1.5f, 0f, 0.28f), 180f);        // kniet vor ihm
            var tendSleeper = LocalSpot(Infirmary, iy, new Vector3(1.4f, 0f, -0.1f), 180f);        // kniet an der zweiten Pritsche
            var salveTable = LocalSpot(Infirmary, iy, new Vector3(-2.0f, 0f, 0.4f), -90f);         // am Verbandstisch
            var watchStool = LocalSpot(Infirmary, iy, new Vector3(0.1f, 0f, 0.3f), 0f);            // Hocker zwischen den Pritschen
            var nessaBed = LocalSpot(Infirmary, iy, new Vector3(0f, 0f, -1.4f), 0f);
            var hotWater = Spot(-8.0f, -19.9f, 200f);

            var desk = Spot(ScribeDesk.x, ScribeDesk.y, 90f);
            var toHall = Spot(1.6f, -22.5f, 180f);

            var bench = Spot(-19.95f, 10.05f, 120f);   // vor der Werkbank
            var ulfBed = Spot(-21.4f, 12.2f, 120f);
            var repair = RepairSpot();

            var pellNorth = Spot(4.0f, 17.5f, 200f);
            var pellTables = Spot(-9.8f, -27.8f, 270f);
            var pellFire = Facing(7.2f, -3.6f, Vector2.zero);
            var pellNight = Spot(PellPark.x, PellPark.y, 30f);

            RoutineStep C(float from, (Vector3, float) s, string act, string note) { RoutineStep r = At(from, s, act, note: note); r.cart = true; return r; }

            return new List<NpcSpec>
            {
                new NpcSpec
                {
                    Name = "Heilerin Nessa", Title = "Lazarett", Female = true, NpcId = Ashveil.Data.RegisterCatalog.Nessa, Quest = true,   // Akt I „Das Register“ (R4, R5)
                    Look = "1|b1|s1|h5|c1|a0|e1|m0", Gear = new[] { "weaver_robe", "rope_belt", "weaver_sandals" },
                    Steps = new[]
                    {
                        At(0f, nessaBed, "Sleep", note: "schlaeft im Lazarett"),
                        At(6f, tendHollin, "Kneel", note: "wechselt Hollins Verbaende"),
                        At(8f, hotWater, "Stand", note: "holt heisses Wasser an der Kochstelle"),
                        At(8.3f, tendHollin, "Kneel|Kneel|Talk", carry: true, note: "versorgt Hollin im Lazarett"),
                        At(10.75f, salveTable, "Interact|Stand", note: "mischt Salben am Verbandstisch"),
                        At(11.25f, tendSleeper, "Kneel", note: "sieht nach dem Schlafenden"),
                        At(12.5f, TableSeat(0, false, 3), "SitTalk", BenchSeat, note: "Mittag an den Langtischen"),
                        At(13.25f, tendHollin, "Kneel|Talk", carry: true, note: "bringt Hollin Essen"),
                        At(15f, tendSleeper, "Kneel", note: "sieht nach dem Schlafenden"),
                        At(16.5f, tendHollin, "Talk|Kneel", note: "versorgt Hollin im Lazarett"),
                        At(19f, watchStool, "Sit|SitTalk", 0.45f, note: "wacht bei den Verwundeten"),
                        At(23f, nessaBed, "Sleep", note: "schlaeft im Lazarett")
                    }
                },
                new NpcSpec
                {
                    Name = "Hollin", Title = "Verwundeter",
                    Look = "1|b0|s4|h6|c2|d2|a1|e0|m1", Gear = new[] { "frayed_gambeson", "travel_trousers", "foot_wraps" },
                    Steps = new[]
                    {
                        At(0f, hollinSeat, "Sit", 0.45f, note: "sitzt wach auf der Pritsche"),
                        At(7f, hollinSeat, "SitTalk|Sit", 0.45f, note: "wird im Lazarett versorgt"),
                        At(21f, hollinSeat, "Sit", 0.45f, note: "sitzt wach auf der Pritsche")
                    }
                },
                new NpcSpec
                {
                    // P3 Glockenrast: Veit nimmt die Abriebe der Schaedeldecke ab (G7)
                    Name = "Schreiber Veit", Title = "Register", NpcId = Ashveil.Data.GlockenrastCatalog.Veit, Quest = true,
                    Look = "1|b0|s0|h2|c4|d0|a0|e3|m0", Gear = new[] { "travel_mantle", "ashen_leather_jerkin", "travel_trousers", "foot_wraps" },
                    Steps = new[]
                    {
                        At(0f, TentBed(3), "Sleep", note: "schlaeft (Tildas Bett, sie hat Nachtwache)"),
                        At(7f, desk, "SitTalk|Sit", 0.45f, note: "traegt Neuankoemmlinge ins Register ein"),
                        At(12.5f, TableSeat(0, true, 3), "SitTalk", BenchSeat, note: "Mittag an den Langtischen"),
                        At(13.25f, desk, "SitTalk|Sit", 0.45f, note: "traegt Neuankoemmlinge ins Register ein"),
                        At(17.5f, toHall, "Talk|Stand", carry: true, note: "bringt das Register zur Halle"),
                        At(18f, TableSeat(1, true, 3), "SitTalk", BenchSeat, note: "Abendessen"),
                        At(19.5f, FireSeat(11), "SitTalk", LogSeat, note: "abends am Wachtfeuer"),
                        At(22.5f, TentBed(3), "Sleep", note: "schlaeft (Tildas Bett, sie hat Nachtwache)")
                    }
                },
                new NpcSpec
                {
                    Name = "Zimmermann Ulf", Title = "Palisade", NpcId = Ashveil.Data.KarethCatalog.Ulf, // #28: K-D in Kareth v4
                    Look = "1|b0|s3|h6|c1|d1|a2|e2|m0", Gear = new[] { "ashen_leather_jerkin", "travel_trousers", "stalker_boots", "stalker_gloves", "rope_belt" },
                    Steps = new[]
                    {
                        At(0f, ulfBed, "Sleep", note: "schlaeft bei der Werkbank"),
                        At(6.5f, bench, "Chop|Interact", note: "spitzt Pfaehle an der Werkbank"),
                        At(8f, repair, "Kneel", carry: true, note: "repariert die Palisade"),
                        At(10.5f, bench, "Chop|Interact", note: "spitzt Pfaehle an der Werkbank"),
                        At(11f, repair, "Kneel|Stand", carry: true, note: "repariert die Palisade"),
                        At(12.5f, TableSeat(1, true, 3), "SitTalk", BenchSeat, note: "Mittag an den Langtischen"),
                        At(13.25f, repair, "Kneel", note: "repariert die Palisade"),
                        At(15.5f, bench, "Chop|Interact", note: "spitzt Pfaehle an der Werkbank"),
                        At(16f, repair, "Kneel", carry: true, note: "repariert die Palisade"),
                        At(18f, TableSeat(1, false, 3), "SitTalk", BenchSeat, note: "Abendessen"),
                        At(19f, FireSeat(7), "SitTalk|Sit", LogSeat, note: "abends am Wachtfeuer"),
                        At(22.5f, ulfBed, "Sleep", note: "schlaeft bei der Werkbank")
                    }
                },
                new NpcSpec
                {
                    Name = "Händler Pell", Title = "Getränke & Heiltränke",
                    Look = "1|b0|s2|h2|c3|d4|a3|e1|m0", Gear = new[] { "travel_hood", "travel_mantle", "hunter_breeches", "stalker_boots" },
                    Cart = _pellCart,
                    Steps = new[]
                    {
                        C(0f, pellNight, "Sleep", "schlaeft neben seinem Karren"),
                        C(7f, pellNorth, "Call|Talk|Stand", "bietet Neuankoemmlingen am Nordtor Getraenke an"),
                        C(11.75f, pellTables, "Call|Talk", "verkauft an den Langtischen"),
                        C(14f, pellNorth, "Call|Talk|Stand", "bietet Neuankoemmlingen am Nordtor Getraenke an"),
                        C(18.5f, pellFire, "Talk|Call", "schenkt am Wachtfeuer aus"),
                        C(22.5f, pellNight, "Sleep", "schlaeft neben seinem Karren")
                    }
                }
            };
        }
    }
}
