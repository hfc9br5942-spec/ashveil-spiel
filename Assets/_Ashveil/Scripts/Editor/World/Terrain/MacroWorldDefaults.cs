using System.Collections.Generic;
using Ashveil.World;
using UnityEditor;
using UnityEngine;
using BF = Ashveil.World.BorderMountainsFrame;
using BM = Ashveil.EditorTools.BorderMountainsLayout;

namespace Ashveil.EditorTools.TerrainGen
{
    /// <summary>
    /// Makrogeografie nach Max' Weltkarte (Prototyp 30.09., "Ashveil – die bekannten Lande"), Massstab 1 Kartenpixel = 20 m
    /// (Max 30.09.: ~22 × 28 km, etwa 1,5-mal WoW Classic). Kareth-Lager = Karte (500, 390) = Welt 0/0; x = Ost, z = Nord.
    /// Kueste aus der Karte (ContinentSdf.bytes). Gebirge, Fluesse, Kerbe, Regionen und Stufen von der Karte abgelesen.
    /// Lagen PROVISIONAL (Karte ist Prototyp); Namen von der Karte; DEV_-Kennungen fuer Ausschnitte ohne Kartennamen.
    /// </summary>
    public static class MacroWorldDefaults
    {
        public const int Version = 19;
        public const float MapPixel = 20f;
        public static readonly Vector2 MapOriginPx = new Vector2(500f, 390f);
        public const string ContinentSdfPath = ProjectSetup.Root + "/Data/World/Generation/ContinentSdf.bytes";
        public const string MapImagePath = ProjectSetup.Root + "/Data/World/Generation/Weltkarte_Prototyp.png";

        /// <summary>Kartenpixel → Welt (x, z).</summary>
        public static Vector2 M(float px, float py) => new Vector2((px - MapOriginPx.x) * MapPixel, (MapOriginPx.y - py) * MapPixel);
        /// <summary>Welt → Kartenpixel.</summary>
        public static Vector2 ToMap(Vector2 w) => new Vector2(MapOriginPx.x + w.x / MapPixel, MapOriginPx.y - w.y / MapPixel);
        private static Vector3 MV(float px, float py, float floorY) { Vector2 w = M(px, py); return new Vector3(w.x, floorY, w.y); }
        private static List<Vector2> MP(params float[] xy)
        {
            var l = new List<Vector2>();
            for (int i = 0; i + 1 < xy.Length; i += 2) l.Add(M(xy[i], xy[i + 1]));
            return l;
        }

        public static void Apply(MacroWorldProfile p)
        {
            p.defaultsVersion = Version;
            p.worldSeed = 137377;
            // Karte 1122 × 1402 px → x −10 000…12 440, z −20 220…7 800; Raster an Kareths Kachelraster gebunden
            p.bounds = new Rect(-10200f, -20350f, 22800f, 28400f);
            p.cellSize = 40f;
            p.seaLevel = KarethTerrainBuilder.SeaLevel;
            p.continentSdf = AssetDatabase.LoadAssetAtPath<TextAsset>(ContinentSdfPath);
            p.coastline = new List<Vector2>();
            p.coastNoise = 60f;
            p.lowlandHeight = 24f;
            p.regionalNoiseAmp = 30f;
            p.regionalNoiseScale = 5000f;
            p.regionalElevation = new List<RegionalElevation>
            {
                RE("kareth_becken", M(500, 360), 2600f, 8f),
                RE("westmarken", M(270, 300), 2600f, 70f),
                RE("graue_lande", M(790, 340), 2200f, 55f),
                RE("dornenfeld", M(160, 600), 2400f, 25f),
                RE("bruchmark", M(480, 700), 4200f, 50f),
                RE("schattenwald", M(830, 630), 1800f, 90f),
                RE("blutsteppen", M(860, 730), 2400f, 110f),
                RE("nharim_kharai", M(800, 880), 3600f, 190f),
                RE("kerbenland_west", M(480, 920), 2800f, 120f),
                RE("zerrissene_lande", M(870, 1110), 2400f, 80f),
                RE("valmor", M(150, 1060), 2600f, 110f),
            };

            p.biomeHints = new List<BiomeHint>
            {
                BH("silberwald", M(370, 355), 1500f, MacroBiome.MixedForest),
                BH("kronenwald", M(620, 322), 1500f, MacroBiome.MixedForest),
                BH("westmarken_waelder", M(250, 400), 1000f, MacroBiome.MixedForest),
                BH("schattenwald", M(830, 625), 1500f, MacroBiome.AncientForest),
                BH("bruchmark_waldreste", M(420, 640), 700f, MacroBiome.MixedForest),
                BH("blutsteppen", M(860, 730), 2000f, MacroBiome.Ashland),
                BH("zerrissene_waelder", M(840, 1110), 900f, MacroBiome.MixedForest),
                BH("eschenfeld", M(690, 410), 900f, MacroBiome.TemperatePlains),
            };

            // ---------------------------------------------------------------- Gebirge
            p.mountainBelts = new List<MountainBeltDefinition>
            {
                // Eiszahn-Gebirge (Zwergenreiche) im Norden
                Belt("eiszahn", 4800f, 240f, 1450f, 1500f, 0.1f, 23, MP(250, 175, 340, 115, 430, 75, 520, 80, 610, 110, 700, 150, 790, 195)),
                // Steinfurten: Nebenkaemme im Nordosten
                Belt("steinfurten_kaemme", 2400f, 160f, 880f, 1000f, 0f, 29, MP(640, 150, 700, 230, 770, 300)),
                // Grenzgebirge: West–Ost suedlich von Kareth; der handgebaute Kern (Talpass) liegt am Ostende
                Belt("grenzgebirge", 3600f, 180f, 1080f, 1250f, 0.05f, 11, MP(205, 470, 300, 495, 400, 515, 500, 505, 600, 520, 700, 545, 790, 565, 835, 595)),
                // Huegel der Westmarken
                Belt("westmarken_huegel", 2000f, 60f, 360f, 900f, 0f, 41, MP(235, 215, 250, 320, 225, 420)),
                // Vulkanisches Bergland der Nharim/Kharai (nordoestlich der Kerbe)
                Belt("nharim_vulkane", 3000f, 180f, 920f, 900f, 0.2f, 37, MP(600, 795, 690, 825, 780, 872, 860, 915, 925, 975)),
                // Bergland suedwestlich der Kerbe
                Belt("kerbenland_west", 2200f, 140f, 720f, 850f, -0.2f, 43, MP(470, 850, 545, 895, 600, 965)),
                // Zerrissene Lande
                Belt("zerrissene_huegel", 1800f, 80f, 480f, 700f, 0f, 47, MP(760, 1075, 880, 1140)),
                // Valmor (Insel im Suedwesten)
                Belt("valmor", 1800f, 90f, 620f, 800f, 0f, 53, MP(90, 1015, 200, 990, 265, 1060)),
                // Drakeninseln (Vulkan)
                Belt("drakeninseln", 1200f, 100f, 760f, 500f, 0f, 59, MP(935, 245, 965, 300)),
            };

            // ---------------------------------------------------------------- Taeler (y = Talsohle)
            p.valleys = new List<ValleyDefinition>
            {
                Valley("silberfluss_tal", 1600f, 260f, 1.4f, MV(440, 210, 520), MV(410, 280, 220), MV(370, 350, 70), MV(378, 420, 10), MV(428, 442, -12)),
                Valley("kronenfluss_tal", 1600f, 260f, 1.4f, MV(600, 230, 480), MV(595, 300, 200), MV(612, 380, 60), MV(660, 430, 30), MV(683, 462, 24)),
                Valley("bruchmark_hauptfluss", 2200f, 420f, 1.3f, MV(672, 612, 30), MV(640, 660, 26), MV(560, 720, 18), MV(470, 740, 12), MV(360, 730, 6), MV(255, 740, -2), MV(185, 742, -26)),
                Valley("hoherpass_suedtal", 1500f, 220f, 1.8f, MV(398, 616, 770), MV(394, 640, 420), MV(386, 662, 220), MV(378, 685, 90), MV(340, 705, 30), MV(300, 720, 14)),
                // Graben hinter Kareths Osttunnel (x 658, z 60, Boden 6 m) hinab ins Eschenfeld
                Valley("osttunnel_graben", 700f, 60f, 2.6f, new Vector3(660f, 6f, 60f), new Vector3(1300f, 10f, -40f), new Vector3(2200f, 14f, -300f), new Vector3(3000f, 18f, -500f)),
            };

            // ---------------------------------------------------------------- Paesse
            p.passes = new List<PassDefinition>
            {
                // Hoher Pass (Karte): kalt, exponiert, Fernsicht – Westseite des Grenzgebirges
                new PassDefinition { id = "hoher_pass", start = M(392, 465), end = M(398, 615), width = 280f, targetElevation = 720f, shoulderFalloff = 700f, roughness = 14f, traversalBias = 60f },
                new PassDefinition { id = "eiszahn_handelspass", start = M(495, 170), end = M(478, 66), width = 320f, targetElevation = 900f, shoulderFalloff = 600f, roughness = 12f, traversalBias = 110f },
            };

            p.plateaus = new List<PlateauDefinition>
            {
                new PlateauDefinition { id = "resonanzhochland", center = M(770, 880), radii = new Vector2(1200f, 800f), yaw = 20f, targetHeight = 400f, interiorNoise = 14f, edgeRoughness = 0.2f, cliffStrength = 0.7f, falloff = 260f },
                // Zwergen-Starttal: geschuetztes Hochtal hinter dem Handelspass, tief im Eiszahn (~7 km von Kareth)
                new PlateauDefinition { id = "zwergen_hochtal", center = M(478, 50), radii = new Vector2(420f, 190f), yaw = 5f, targetHeight = 705f, interiorNoise = 8f, edgeRoughness = 0.12f, cliffStrength = 0.15f, falloff = 380f },
                new PlateauDefinition { id = "westmarken_burgfels", center = M(290, 265), radii = new Vector2(360f, 260f), yaw = 0f, targetHeight = 210f, interiorNoise = 6f, edgeRoughness = 0.15f, cliffStrength = 0.55f, falloff = 160f },
            };

            // Salzsee (abflusslos): zwischen Kareth und Grenzgebirge; Kareths Suedwest-Wasser gehoert jetzt zu ihm
            p.lakes = new List<LakeDefinition>
            {
                new LakeDefinition { id = "salzsee", center = M(445, 442), radii = new Vector2(760f, 480f), yaw = 15f, floorElevation = -46f, waterLevel = KarethTerrainBuilder.SeaLevel, shoreWidth = 420f, endorheic = true },
            };

            // ---------------------------------------------------------------- Grosse Kerbe (Karte: NW in der Bruchmark → SO ins Meer)
            p.veilScar = new VeilScarDefinition
            {
                id = "great_veil_scar",
                veilHeight = 3200f,
                points = new List<VeilScarPoint>
                {
                    // 30.09. (Max): das Westende ertrinkt im Meer – die Sohle faellt nach Westen unter den Meeresspiegel
                    SP(M(236, 757), 560f, -80f, 40f, 0.3f, 0.2f, 0f, 0f, 0f, 0.6f),
                    SP(M(292, 761), 580f, -30f, 70f, 0.4f, 0.25f, 0.05f, 0.05f, 0.2f, 0.62f),
                    SP(M(360, 767), 610f, 25f, 95f, 0.45f, 0.3f, 0.08f, 0.08f, 0.3f, 0.68f),
                    SP(M(420, 774), 630f, 70f, 110f, 0.5f, 0.3f, 0.1f, 0.1f, 0.3f, 0.72f),
                    SP(M(468, 780), 650f, 95f, 114f, 0.5f, 0.3f, 0.1f, 0.1f, 0.3f, 0.75f),
                    SP(M(530, 815), 1000f, 78f, 247f, 0.6f, 0.4f, 0.2f, 0.2f, 0.2f, 0.8f),
                    SP(M(610, 875), 1300f, 62f, 437f, 0.7f, 0.5f, 0.3f, 0.35f, 0.2f, 0.85f),
                    SP(M(690, 940), 1600f, 48f, 570f, 0.8f, 0.6f, 0.35f, 0.5f, 0.3f, 0.9f),
                    SP(M(770, 1000), 1900f, 34f, 608f, 0.8f, 0.6f, 0.3f, 0.55f, 0.6f, 0.95f),
                    SP(M(850, 1065), 2300f, 20f, 532f, 0.7f, 0.6f, 0.2f, 0.5f, 0.4f, 1f),
                    SP(M(910, 1120), 2800f, 4f, 380f, 0.6f, 0.5f, 0.1f, 0.4f, 0.2f, 1f),
                    SP(M(962, 1178), 3400f, -40f, 209f, 0.5f, 0.4f, 0f, 0.3f, 0f, 1f),
                    SP(M(1015, 1235), 3600f, -90f, 0f, 0.3f, 0.2f, 0f, 0f, 0f, 1f),   // im Meer auslaufend
                },
                branches = new List<SecondaryFractureDefinition>
                {
                    new SecondaryFractureDefinition { id = "ostarm", width = 900f, floorElevation = 10f, tipDepth = 60f, path = MP(905, 1112, 887, 1040, 862, 975) },
                }
            };

            p.ancientScars = new List<AncientScarDefinition>
            {
                new AncientScarDefinition
                {
                    id = "protector_footprint_grenzgebirge", type = AncientScarType.ProtectorFootprint, center = BF.ToWorld(BM.FootCenter), yaw = BF.YawToWorld(225f),   // Ferse zur Kerbe, Zehen weg (Max 30.09.)
                    length = 330f, width = 430f, depth = 20f, representsExisting = true, note = "Handgebaut im Grenzgebirge-Kern. Identitaet UNBEKANNT."
                },
                new AncientScarDefinition { id = "verglaste_senke_bruchmark", type = AncientScarType.GlassField, center = M(560, 760), yaw = 30f, length = 700f, width = 420f, depth = 10f, note = "Hier stimmt etwas nicht (§113)." },
                new AncientScarDefinition { id = "gerader_felsbruch_kerbenland", type = AncientScarType.StraightFault, center = M(430, 905), yaw = 62f, length = 1600f, width = 60f, depth = 18f, note = "Ungewoehnlich gerader alter Felsbruch." },
            };

            // ---------------------------------------------------------------- Fluesse (Quelle → Muendung)
            Vector2 coreNorth = BF.ToWorld(BM.TalNode(0).x, BM.TalNode(0).z);          // Talpass-Eingang (Nordkante des Kerns)
            Vector2 coreSouth = BF.ToWorld(BM.ExitSouth);                               // Talpass-Ausgang zur Bruchmark
            p.rivers = new List<RiverDefinition>
            {
                RV("silberfluss", 22f, 4f, MP(440, 210, 410, 280, 370, 350, 378, 420, 428, 442)),                 // endet im Salzsee (abflusslos)
                RV("kronenfluss", 24f, 4f, MP(600, 230, 595, 300, 612, 380, 660, 430)),
                RV("bruchmark_strom", 40f, 6f, MP(672, 612, 640, 660, 560, 720, 470, 740, 360, 730, 255, 740, 180, 744)),
                RV("hoherpass_bach", 14f, 3f, MP(398, 615, 392, 645, 378, 680, 340, 705, 300, 720, 262, 738)),
                RV("kerbenstrom", 60f, 8f, MP(470, 782, 530, 815, 610, 875, 690, 940, 770, 1000, 850, 1065, 910, 1120, 965, 1182)),
            };

            // ---------------------------------------------------------------- Wege (Bewegungsgraph zuerst)
            Vector2 capA = M(525, 205), capB = M(835, 840);   // 30.09. (Max): Hauptstadt ~4 km, Kronenwald-Nordrand am Fuss des Eiszahns
            p.roads = new List<RoadCorridorDefinition>
            {
                RD("kareth_nordstrasse", RouteClass.Main, true, "poi_kareth_nordtor", "poi_hauptknoten_a", new Vector2(-100f, 850f), M(508, 330), M(512, 285), M(522, 245), capA),
                RD("eiszahnstrasse", RouteClass.Main, true, "poi_hauptknoten_a", "poi_zwergen_start", capA, M(505, 180), M(485, 140), M(474, 100), M(476, 76), M(478, 54)),
                RD("silberweg", RouteClass.Secondary, false, "poi_westweiden_hof", "poi_westmarken_burg", new Vector2(-1800f, 300f), M(380, 360), M(330, 300), M(290, 268)),
                RD("eschenweg", RouteClass.Main, true, "poi_osttunnel", "poi_talpass_tor", new Vector2(700f, 60f), new Vector2(1600f, -120f), new Vector2(2700f, -600f), coreNorth + new Vector2(0f, 60f)),
                RD("graue_strasse", RouteClass.Secondary, false, "poi_eschenfeld", "poi_graue_lande", M(690, 410), M(740, 370), M(790, 340)),
                RD("steinfurter_steig", RouteClass.Secondary, false, "poi_hauptknoten_a", "poi_steinfurten", capA, M(560, 220), M(600, 215)),
                RD("hoherpass_weg", RouteClass.Main, false, "poi_westweiden_hof", "poi_hoher_pass", new Vector2(-1800f, 0f), new Vector2(-2350f, -700f), M(392, 470), M(394, 520), M(395, 560), M(398, 612), M(414, 624), M(390, 636), M(414, 648), M(392, 660), M(410, 672), M(398, 690)),
                RD("bruchmark_strasse", RouteClass.Main, true, "poi_talpass_ausgang", "poi_bruchmark_mitte", coreSouth + new Vector2(0f, -420f), M(640, 640), M(560, 680), M(500, 690)),
                RD("dornenweg", RouteClass.Secondary, false, "poi_bruchmark_mitte", "poi_dornenfeld", M(500, 690), M(380, 680), M(260, 640), M(180, 600)),
                RD("blutsteppenweg", RouteClass.Main, false, "poi_bruchmark_mitte", "poi_hauptknoten_b", M(560, 680), M(700, 640), M(830, 640), M(860, 730), M(835, 830), capB),
                RD("nharim_strasse", RouteClass.Main, true, "poi_hauptknoten_b", "poi_nharim_start", capB, M(795, 828), M(760, 815)),
                RD("kharai_strasse", RouteClass.Main, true, "poi_hauptknoten_b", "poi_kharai_start", capB, M(880, 860), M(925, 890), M(935, 925)),
                RD("kerbenpfad", RouteClass.Exploration, false, "poi_bruchmark_mitte", "poi_kerbenrand", M(500, 700), M(480, 750), M(505, 790)),
            };

            // ---------------------------------------------------------------- Regionen (Karte: Namen und Stufen)
            p.regions = new List<MacroRegionDefinition>
            {
                RG("Human_Kareth", "Kareth (Wachtfeuer)", 1, 12, FactionAffinity.HumanAlliance, ProgressionTier.RaceStart, "human", false, true, RectP(-600f, -350f, 600f, 850f),
                   "DEV_Kareth_Silberwald", "DEV_Kareth_Kronenwald", "DEV_Kareth_Salzsee", "DEV_Kareth_Eschenfeld", "Human_Westweiden"),
                RG("Human_Westweiden", "Westweiden (Hallgrund)", 8, 16, FactionAffinity.HumanAlliance, ProgressionTier.RaceHome, "", false, true, RectP(-1800f, -350f, -600f, 850f),
                   "Human_Kareth", "DEV_Kareth_Silberwald", "DEV_Westmarken"),
                RG("DEV_Kareth_Silberwald", "Silberwald", 6, 14, FactionAffinity.HumanAlliance, ProgressionTier.RaceHome, "", false, false, MP(330, 300, 460, 300, 470, 420, 420, 440, 330, 440),
                   "Human_Kareth", "Human_Westweiden", "DEV_Westmarken", "DEV_Kareth_Salzsee"),
                RG("DEV_Kareth_Kronenwald", "Kronenwald", 8, 16, FactionAffinity.HumanAlliance, ProgressionTier.FactionConvergence, "", false, false, MP(470, 195, 700, 200, 700, 360, 560, 370, 480, 330),
                   "Human_Kareth", "DEV_Eiszahn", "DEV_Kareth_Eschenfeld", "DEV_Steinfurten"),
                RG("DEV_Kareth_Salzsee", "Salzsee", 6, 14, FactionAffinity.HumanAlliance, ProgressionTier.RaceHome, "", false, false, MP(410, 415, 560, 415, 560, 465, 410, 470),
                   "Human_Kareth", "DEV_Kareth_Silberwald", "DEV_Grenzgebirge"),
                RG("DEV_Kareth_Eschenfeld", "Eschenfeld", 12, 20, FactionAffinity.HumanAlliance, ProgressionTier.FactionConvergence, "", false, false, MP(560, 370, 760, 360, 760, 465, 560, 450),
                   "Human_Kareth", "DEV_Kareth_Kronenwald", "DEV_Graue_Lande", "Human_BorderMountains", "DEV_Grenzgebirge"),
                RG("DEV_Dwarf_Start", "Zwergen-Starttal (Eiszahn)", 1, 6, FactionAffinity.HumanAlliance, ProgressionTier.RaceStart, "dwarf", false, false, MP(430, 35, 530, 35, 530, 95, 430, 95),
                   "DEV_Eiszahn"),
                RG("DEV_Eiszahn", "Eiszahn-Gebirge", 5, 20, FactionAffinity.HumanAlliance, ProgressionTier.RaceHome, "", false, false, MP(260, 30, 800, 30, 800, 200, 470, 195, 260, 240),
                   "DEV_Dwarf_Start", "DEV_Kareth_Kronenwald", "DEV_Westmarken", "DEV_Steinfurten"),
                RG("DEV_Westmarken", "Westmarken", 15, 35, FactionAffinity.HumanAlliance, ProgressionTier.FactionConvergence, "", false, false, MP(190, 200, 330, 200, 330, 450, 190, 450),
                   "DEV_Kareth_Silberwald", "DEV_Eiszahn", "DEV_Dornenfeld", "DEV_Scherbeninseln"),
                RG("DEV_Steinfurten", "Steinfurten", 30, 50, FactionAffinity.Neutral, ProgressionTier.DangerousOldWorld, "", true, false, MP(590, 165, 760, 165, 780, 290, 620, 290),
                   "DEV_Eiszahn", "DEV_Kareth_Kronenwald", "DEV_Graue_Lande", "DEV_Drakeninseln"),
                RG("DEV_Graue_Lande", "Die Grauen Lande", 20, 40, FactionAffinity.Neutral, ProgressionTier.Frontier, "", false, false, MP(700, 255, 880, 255, 880, 420, 760, 465, 700, 360),
                   "DEV_Kareth_Eschenfeld", "DEV_Steinfurten", "DEV_Schattenwald", "DEV_Drakeninseln"),
                RG("Human_BorderMountains", "Grenzgebirge – Talpass (Kern)", 18, 26, FactionAffinity.HumanAlliance, ProgressionTier.Frontier, "", false, true, RectW(BF.WorldRect),
                   "DEV_Kareth_Eschenfeld", "DEV_Grenzgebirge", "Contested_Bruchmark"),
                RG("DEV_Grenzgebirge", "Grenzgebirge", 18, 30, FactionAffinity.Neutral, ProgressionTier.Frontier, "", false, false, MP(205, 450, 835, 450, 845, 620, 205, 620),
                   "DEV_Kareth_Salzsee", "DEV_Kareth_Eschenfeld", "Human_BorderMountains", "Contested_Bruchmark", "DEV_Dornenfeld"),
                RG("DEV_Dornenfeld", "Dornenfeld", 20, 40, FactionAffinity.Neutral, ProgressionTier.Frontier, "", false, false, MP(55, 520, 235, 520, 260, 700, 75, 700),
                   "DEV_Westmarken", "DEV_Grenzgebirge", "Contested_Bruchmark", "DEV_Zersplitterte_Kuesten"),
                RG("Contested_Bruchmark", "Bruchmark", 25, 45, FactionAffinity.Contested, ProgressionTier.ContestedWorld, "", true, false, MP(260, 620, 760, 620, 760, 790, 560, 790, 460, 770, 260, 790),
                   "DEV_Grenzgebirge", "Human_BorderMountains", "DEV_Dornenfeld", "DEV_Schattenwald", "DEV_Blutsteppen", "DEV_Kerbenland_West", "DEV_Great_Scar"),
                RG("DEV_Schattenwald", "Schattenwald", 28, 40, FactionAffinity.Contested, ProgressionTier.Frontier, "", true, false, MP(760, 575, 905, 575, 905, 690, 760, 690),
                   "DEV_Graue_Lande", "Contested_Bruchmark", "DEV_Blutsteppen"),
                RG("DEV_Blutsteppen", "Blutsteppen", 30, 50, FactionAffinity.Contested, ProgressionTier.ContestedWorld, "", true, false, MP(760, 690, 965, 690, 965, 800, 760, 800),
                   "DEV_Schattenwald", "Contested_Bruchmark", "DEV_Nharim_Grenzland", "DEV_Great_Scar"),
                RG("DEV_Nharim_Start", "Nharim-Start", 1, 6, FactionAffinity.NharimAlliance, ProgressionTier.RaceStart, "nharim", false, false, MP(725, 805, 800, 805, 800, 860, 725, 860),
                   "DEV_Nharim_Heimat"),
                RG("DEV_Kharai_Start", "Kharai-Start", 1, 6, FactionAffinity.NharimAlliance, ProgressionTier.RaceStart, "kharai", false, false, MP(865, 900, 950, 900, 950, 965, 865, 965),
                   "DEV_Kharai_Heimat"),
                RG("DEV_Nharim_Heimat", "Nharim-Heimat", 5, 18, FactionAffinity.NharimAlliance, ProgressionTier.RaceHome, "", false, false, MP(640, 800, 830, 800, 830, 900, 700, 930, 640, 860),
                   "DEV_Nharim_Start", "DEV_Kharai_Heimat", "DEV_Nharim_Grenzland"),
                RG("DEV_Kharai_Heimat", "Kharai-Riftland", 8, 20, FactionAffinity.NharimAlliance, ProgressionTier.RaceHome, "", false, false, MP(830, 850, 985, 850, 985, 1000, 880, 1010, 830, 900),
                   "DEV_Kharai_Start", "DEV_Nharim_Heimat", "DEV_Nharim_Grenzland"),
                RG("DEV_Nharim_Grenzland", "Nharim/Kharai-Grenzland", 14, 30, FactionAffinity.NharimAlliance, ProgressionTier.FactionConvergence, "", false, false, MP(700, 780, 985, 780, 985, 850, 830, 850, 830, 800, 700, 800),
                   "DEV_Nharim_Heimat", "DEV_Kharai_Heimat", "DEV_Blutsteppen", "DEV_Great_Scar"),
                RG("DEV_Great_Scar", "Die Grosse Kerbe", 45, 60, FactionAffinity.Neutral, ProgressionTier.WorldScale, "", true, false, MP(228, 742, 455, 760, 560, 785, 700, 910, 975, 1140, 975, 1215, 900, 1200, 640, 960, 450, 820, 228, 778),
                   "Contested_Bruchmark", "DEV_Blutsteppen", "DEV_Nharim_Grenzland", "DEV_Kerbenland_West", "DEV_Zerrissene_Lande"),
                RG("DEV_Kerbenland_West", "Kerbenland (West)", 35, 50, FactionAffinity.Neutral, ProgressionTier.DangerousOldWorld, "", true, false, MP(330, 800, 460, 800, 650, 960, 700, 1040, 560, 1060, 330, 1000),
                   "Contested_Bruchmark", "DEV_Great_Scar", "DEV_Korallenkranz"),
                RG("DEV_Zerrissene_Lande", "Zerrissene Lande", 40, 60, FactionAffinity.Neutral, ProgressionTier.WorldScale, "", true, false, MP(700, 1040, 1000, 1040, 1000, 1210, 700, 1210),
                   "DEV_Great_Scar", "DEV_Aufsteigende_Lande"),
                // Inseln (Seeweg; teils "noch unerforscht")
                RG("DEV_Scherbeninseln", "Die Scherbeninseln", 40, 60, FactionAffinity.Unknown, ProgressionTier.WorldScale, "", true, false, MP(40, 230, 170, 230, 170, 400, 40, 400), "DEV_Westmarken"),
                RG("DEV_Drakeninseln", "Drakeninseln", 50, 60, FactionAffinity.Unknown, ProgressionTier.WorldScale, "", true, false, MP(900, 190, 1080, 190, 1080, 350, 900, 350), "DEV_Graue_Lande"),
                RG("DEV_Zersplitterte_Kuesten", "Die Zersplitterten Kuesten", 30, 50, FactionAffinity.Unknown, ProgressionTier.DangerousOldWorld, "", true, false, MP(30, 770, 190, 770, 190, 900, 30, 900), "DEV_Dornenfeld", "DEV_Valmor"),
                RG("DEV_Valmor", "Valmor (unerforscht)", 40, 60, FactionAffinity.Unknown, ProgressionTier.WorldScale, "", true, false, MP(25, 980, 320, 980, 320, 1150, 25, 1150), "DEV_Zersplitterte_Kuesten"),
                RG("DEV_Aufsteigende_Lande", "Die Aufsteigenden Lande", 40, 60, FactionAffinity.Unknown, ProgressionTier.WorldScale, "", true, false, MP(990, 870, 1100, 870, 1100, 1030, 990, 1030), "DEV_Zerrissene_Lande"),
                RG("DEV_Korallenkranz", "Korallenkranz (unerforscht)", 30, 50, FactionAffinity.Unknown, ProgressionTier.DangerousOldWorld, "", true, false, MP(700, 1250, 860, 1250, 860, 1360, 700, 1360), "DEV_Kerbenland_West", "DEV_Zerrissene_Lande"),
                RGSlot("SLOT_A3"),
                RGSlot("SLOT_B3"),
            };

            // ---------------------------------------------------------------- Orte und Blickpunkte
            Vector2 schenke = BF.ToWorld(BM.SchenkeCenter);
            p.pois = new List<MacroPoiDefinition>
            {
                PO("poi_kareth_lager", "KARETH", "Human_Kareth", new Vector2(0f, -4f), "hub", 3, HubTier.Camp, 1, FactionAffinity.HumanAlliance, true),
                PO("poi_kareth_nordtor", "Kareth Nord", "Human_Kareth", new Vector2(-100f, 800f), "viewpoint", 1, HubTier.None, 8, FactionAffinity.HumanAlliance, true),
                PO("poi_kareth_ost", "Kareth Ost", "Human_Kareth", new Vector2(470f, 60f), "viewpoint", 1, HubTier.None, 10, FactionAffinity.HumanAlliance, true),
                PO("poi_kareth_sued", "Kareth Sued (Salzsee)", "Human_Kareth", new Vector2(250f, -330f), "viewpoint", 1, HubTier.None, 8, FactionAffinity.HumanAlliance, true),
                PO("poi_osttunnel", "Osttunnel", "Human_Kareth", new Vector2(680f, 60f), "route", 2, HubTier.None, 12, FactionAffinity.HumanAlliance, false),
                PO("poi_salzgruben", "Salzgruben", "Human_Kareth", new Vector2(-290f, -195f), "dungeon", 2, HubTier.None, 11, FactionAffinity.HumanAlliance, false),
                PO("poi_westweiden_hof", "Hallgrund", "Human_Westweiden", WestPasturesLayout.FarmCenter, "hub", 2, HubTier.Camp, 10, FactionAffinity.HumanAlliance, false),
                PO("poi_hauptknoten_a", "Hauptstadt der Menschen (Slot)", "DEV_Kareth_Kronenwald", capA, "capital_slot", 3, HubTier.CapitalSlot, 10, FactionAffinity.HumanAlliance, false),
                PO("poi_zwergen_start", "Zwergen-Start (Slot)", "DEV_Dwarf_Start", M(478, 56), "start", 2, HubTier.Camp, 1, FactionAffinity.HumanAlliance, false),
                PO("poi_westmarken_burg", "Westmarken", "DEV_Westmarken", M(290, 265), "hub", 2, HubTier.Town, 18, FactionAffinity.HumanAlliance, false),
                PO("poi_steinfurten", "Steinfurten", "DEV_Steinfurten", M(600, 215), "hub", 2, HubTier.Outpost, 32, FactionAffinity.Neutral, false),
                PO("poi_eschenfeld", "Eschenfeld", "DEV_Kareth_Eschenfeld", M(690, 410), "hub", 2, HubTier.Camp, 14, FactionAffinity.HumanAlliance, false),
                PO("poi_graue_lande", "Die Grauen Lande", "DEV_Graue_Lande", M(790, 340), "hub", 1, HubTier.Outpost, 24, FactionAffinity.Neutral, false),
                PO("poi_talpass_tor", "VALLEY PASS", "Human_BorderMountains", coreNorth, "pass", 2, HubTier.None, 18, FactionAffinity.HumanAlliance, true),
                PO("poi_hochpass_kern", "Wachturmsteig", "Human_BorderMountains", BF.ToWorld(BM.HighPass), "pass", 1, HubTier.None, 22, FactionAffinity.HumanAlliance, false),
                PO("poi_talpass_ausgang", "Talpass-Ausgang", "Human_BorderMountains", coreSouth, "route", 1, HubTier.None, 24, FactionAffinity.HumanAlliance, false),
                PO("poi_fussabdruck", "FOOTPRINT", "Human_BorderMountains", BF.ToWorld(BM.FootCenter), "landmark", 3, HubTier.None, 20, FactionAffinity.Neutral, true),
                PO("poi_schenke", "CLIFF TAVERN", "Human_BorderMountains", schenke, "hub", 3, HubTier.Outpost, 20, FactionAffinity.HumanAlliance, true),
                PO("poi_hoher_pass", "HIGH PASS", "DEV_Grenzgebirge", M(395, 540), "pass", 3, HubTier.None, 22, FactionAffinity.Neutral, true),
                PO("poi_dornenfeld", "Dornenfeld", "DEV_Dornenfeld", M(180, 600), "hub", 1, HubTier.Outpost, 26, FactionAffinity.Neutral, false),
                PO("poi_bruchmark_eingang", "BRUCHMARK", "Contested_Bruchmark", M(660, 630), "viewpoint", 2, HubTier.None, 26, FactionAffinity.Contested, true),
                PO("poi_bruchmark_mitte", "Bruchmark-Mitte", "Contested_Bruchmark", M(500, 690), "landmark", 2, HubTier.None, 34, FactionAffinity.Contested, false),
                PO("poi_kerbenrand", "GREAT VEIL SCAR", "DEV_Great_Scar", M(505, 790), "viewpoint", 3, HubTier.None, 46, FactionAffinity.Neutral, true),
                PO("poi_hauptknoten_b", "Hauptstadt der Nharim (Slot)", "DEV_Nharim_Grenzland", capB, "capital_slot", 3, HubTier.CapitalSlot, 10, FactionAffinity.NharimAlliance, false),
                PO("poi_nharim_start", "Nharim-Start (Slot)", "DEV_Nharim_Start", M(760, 815), "start", 2, HubTier.Camp, 1, FactionAffinity.NharimAlliance, false),
                PO("poi_kharai_start", "Kharai-Start (Slot)", "DEV_Kharai_Start", M(935, 925), "start", 2, HubTier.Camp, 1, FactionAffinity.NharimAlliance, false),
            };

            // ---------------------------------------------------------------- Handarbeit (nie ueberschreiben)
            const string T = ProjectSetup.Root + "/Terrain/";
            p.protectedAreas = new List<ProtectedAreaDefinition>
            {
                new ProtectedAreaDefinition { id = "Kareth", rect = new Rect(-600f, -350f, 1200f, 1200f), terrainDataPaths = new List<string> { T + "Kareth_TerrainData.asset" } },
                new ProtectedAreaDefinition { id = "Westweiden", rect = new Rect(-1800f, -350f, 1200f, 1200f), terrainDataPaths = new List<string> { T + "WW_Hallgrund_TerrainData.asset" } },
                new ProtectedAreaDefinition
                {
                    id = "Grenzgebirge", rect = BF.WorldRect,
                    terrainDataPaths = new List<string> { T + "GG_West_Sued_TerrainData.asset", T + "GG_West_Nord_TerrainData.asset", T + "GG_Ost_Sued_TerrainData.asset", T + "GG_Ost_Nord_TerrainData.asset" }
                },
            };
            p.transitionDistance = 500f;
            p.riverCandidateAreaKm2 = 6f;
        }

        // ------------------------------------------------------------------ Kurzschreibweisen

        private static BiomeHint BH(string id, Vector2 c, float r, MacroBiome b) => new BiomeHint { id = id, center = c, radius = r, biome = b };

        private static RegionalElevation RE(string id, Vector2 c, float r, float h) => new RegionalElevation { id = id, center = c, radius = r, height = h };

        private static MountainBeltDefinition Belt(string id, float width, float baseE, float peak, float ridgeScale, float asym, int seed, List<Vector2> pts) =>
            new MountainBeltDefinition
            {
                id = id, width = width, baseElevation = baseE, peakElevation = peak, ridgeStrength = 0.8f, ridgeScale = ridgeScale, asymmetry = asym,
                valleyStrength = 0.4f, roughness = 0.55f, peakVariation = 0.4f, seed = seed, controlPoints = pts
            };

        private static ValleyDefinition Valley(string id, float width, float floorW, float side, params Vector3[] pts) =>
            new ValleyDefinition { id = id, width = width, floorWidth = floorW, sideSlope = side, noise = 4f, meander = 80f, centerline = new List<Vector3>(pts) };

        private static VeilScarPoint SP(Vector2 pos, float w, float floor, float rim, float fracture, float terrace, float asym, float glass, float secondary, float veil) =>
            new VeilScarPoint
            {
                position = pos, width = w, floorElevation = floor, depth = Mathf.Max(150f, rim + 150f - floor), rimUplift = rim, fractureStrength = fracture,
                terraceStrength = terrace, asymmetry = asym, glassFieldProbability = glass, secondaryFractureStrength = secondary, veilIntensity = veil
            };

        private static RiverDefinition RV(string id, float w, float d, List<Vector2> pts) => new RiverDefinition { id = id, width = w, depth = d, path = pts };

        private static RoadCorridorDefinition RD(string id, RouteClass cls, bool cart, string from, string to, params Vector2[] pts) =>
            new RoadCorridorDefinition
            {
                id = id, routeClass = cls, cartRoute = cart, fromPoi = from, toPoi = to, path = new List<Vector2>(pts),
                width = cls == RouteClass.Main ? 24f : cls == RouteClass.Secondary ? 16f : 10f,
                maxSlope = cls == RouteClass.Main ? 12f : cls == RouteClass.Secondary ? 18f : 28f,
                flattenStrength = cls == RouteClass.Main ? 0.7f : cls == RouteClass.Secondary ? 0.5f : 0.15f,
                cutStrength = cls == RouteClass.Main ? 0.92f : 0.45f,
                fillStrength = cls == RouteClass.Main ? 0.75f : 0.3f
            };

        private static List<Vector2> RectP(float x0, float z0, float x1, float z1) => new List<Vector2> { new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1) };
        private static List<Vector2> RectW(Rect r) => RectP(r.xMin, r.yMin, r.xMax, r.yMax);

        private static MacroRegionDefinition RG(string id, string name, int min, int max, FactionAffinity f, ProgressionTier tier, string race, bool contested, bool prot,
                                                List<Vector2> poly, params string[] neighbors) =>
            new MacroRegionDefinition
            {
                regionId = id, displayName = name, levels = new Vector2Int(min, max), faction = f, tier = tier, starterRaceId = race, contested = contested,
                isProtectedFromGeneration = prot, polygon = poly, neighbors = new List<string>(neighbors),
                travelTier = tier <= ProgressionTier.RaceHome ? TravelTier.Local : tier <= ProgressionTier.FactionConvergence ? TravelTier.Regional
                           : tier <= ProgressionTier.ContestedWorld ? TravelTier.Frontier : TravelTier.Remote
            };

        /// <summary>Drittes Volk je Fraktion: offen – keine Lage, kein Name, keine Lore (Vorgabe §107).</summary>
        private static MacroRegionDefinition RGSlot(string id) =>
            new MacroRegionDefinition { regionId = id, displayName = "", levels = new Vector2Int(1, 12), faction = FactionAffinity.Unknown, isSlot = true, polygon = new List<Vector2>() };

        private static MacroPoiDefinition PO(string id, string label, string region, Vector2 pos, string type, int importance, HubTier hub, int level, FactionAffinity f, bool viewpoint) =>
            new MacroPoiDefinition
            {
                id = id, label = label, regionId = region, position = pos, poiType = type, importance = importance, hubTier = hub, recommendedLevel = level, faction = f,
                viewpoint = viewpoint, visibleDistance = importance >= 3 ? 6000f : importance == 2 ? 3000f : 1200f
            };
    }
}
