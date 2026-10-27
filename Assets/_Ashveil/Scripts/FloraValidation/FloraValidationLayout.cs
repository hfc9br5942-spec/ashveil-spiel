using UnityEngine;

namespace Ashveil.FloraValidation
{
    /// <summary>
    /// Feste Aufteilung der isolierten Flora-Testszene (Szenenbau und Messlauf nutzen dieselben Werte).
    /// Jeder Bereich hat einen eigenen Boden und ist waehrend der Messung allein aktiv. Kein Bezug zur Spielwelt.
    /// </summary>
    public static class FloraValidationLayout
    {
        public const string GroundName = "FV_Boden";

        public static readonly Vector3 Hero = new Vector3(0, 0, 0);
        public static readonly Vector3 Group = new Vector3(0, 0, 3000);
        public static readonly Vector3 Habitat = new Vector3(3000, 0, 3000);
        public static readonly Vector3 Stress = new Vector3(6000, 0, 6000);
        public static readonly Vector3 InstSame = new Vector3(0, 0, -3000);
        public static readonly Vector3 InstSeeds = new Vector3(3000, 0, -3000);
        public static readonly Vector3 InstMixed = new Vector3(6000, 0, -3000);
        public static readonly Vector3 LineupF01 = new Vector3(-3000, 0, 0);
        public static readonly Vector3 LineupF04 = new Vector3(-3000, 0, 3000);
        public static readonly Vector3 F10Full = new Vector3(-3000, 0, -3000);
        public static readonly Vector3 F10Lod = new Vector3(-6000, 0, -3000);
        public static readonly Vector3 F19Full = new Vector3(-6000, 0, 0);
        public static readonly Vector3 F19Lod = new Vector3(-6000, 0, 3000);
        /// <summary>v3-Kalibrierbereich: Einzel-LOD-Kandidaten ohne LODGroup (Baeume 300 m auseinander, F10 bei z +600).</summary>
        public static readonly Vector3 Calib = new Vector3(9000, 0, 0);
        public static readonly string[] CalTrees =
        {
            "f01_v1_bestand_s1", "f01_v2_solitaer_s1", "f01_v3_altbaum_s1",
            "f04a_v1_bestand_s1", "f04a_v2_freistand_s1", "f04a_v3_windform_s1",
        };
        public static readonly string[] CalTreeVariants = { "near", "mid", "far", "far3", "far4", "vfar", "vfar3", "vfar3k", "vfar4", "vfar4k" };
        public static readonly string[] CalF10Variants = { "full", "lod0", "lod0a3", "lod0b3", "lod1", "lod2" };

        /// <summary>Hero-Objekte nebeneinander auf der x-Achse (80 m Abstand – aus der Nahkamera nicht sichtbar).</summary>
        public static readonly string[] HeroItems =
        {
            "f01_v1_bestand_s1", "f01_v2_solitaer_s1", "f01_v3_altbaum_s1",
            "f04a_v1_bestand_s1", "f04a_v2_freistand_s1", "f04a_v3_windform_s1",
            "f19_trunk", "f19_trunk_full", "f19_rock", "f19_wall",
            "f10_group_sommer", "f10_group_sommer_hero",
            "dead_snag_A", "dead_log_B", "dead_stump_C",
        };
        public const float HeroSpacing = 80f;
        public static Vector3 HeroPos(int i) => Hero + new Vector3(i * HeroSpacing, 0, 0);

        /// <summary>Kleine Gruppe B: Spieler steht bei (0,0,0) und schaut nach +z.</summary>
        public static readonly (string prefab, Vector3 pos, float rot)[] GroupItems =
        {
            ("f01_v2_solitaer_s2", new Vector3(0, 0, 18), 0), ("f01_v1_bestand_s3", new Vector3(-14, 0, 30), 40), ("f01_v1_bestand_s5", new Vector3(-6, 0, 40), 100),
            ("f01_v3_altbaum_s2", new Vector3(16, 0, 32), 200), ("f01_v1_bestand_s1", new Vector3(-24, 0, 22), 300),
            ("f04a_v1_bestand_s2", new Vector3(28, 0, 45), 10), ("f04a_v1_bestand_s4", new Vector3(36, 0, 38), 70), ("f04a_v2_freistand_s3", new Vector3(-34, 0, 40), 150),
            ("f04a_v3_windform_s2", new Vector3(40, 0, 20), 220), ("f04a_v1_bestand_s6", new Vector3(24, 0, 58), 280),
            ("f10_group_sommer", new Vector3(6, 0, 8), 0), ("f10_group_trocken", new Vector3(-10, 0, 10), 90),
            ("f10_mittel_sommer", new Vector3(3, 0, 3), 0), ("f10_alt_sommer", new Vector3(-4, 0, 5), 50), ("f10_jung_sommer", new Vector3(9, 0, 4), 130),
            ("f19_wall", new Vector3(-18, 0, 6), 180), ("f19_trunk", new Vector3(12, 0, 12), 0), ("f19_rock", new Vector3(4, 0, 26), 30),
            ("dead_log_B", new Vector3(-4, 0, 14), 30), ("dead_stump_C", new Vector3(20, 0, 10), 0), ("dead_snag_B", new Vector3(-28, 0, 50), 0),
            ("shrub_ph_1", new Vector3(-20, 0, 18), 0), ("shrub_ph_2", new Vector3(10, 0, 24), 60),
        };

        public static readonly string[] LineupF01Items = { "f01_v1_bestand_s1", "f01_v2_solitaer_s1", "f01_v3_altbaum_s1" };
        public static readonly string[] LineupF04Items = { "f04a_v1_bestand_s1", "f04a_v2_freistand_s1", "f04a_v3_windform_s1" };
        public const float LineupSpacing = 30f;

        public const int InstGrid = 20;
        public const float InstSpacing = 9f;
        public const int F10Grid = 10;
        public const float F10Spacing = 8.5f;
        public const int F19GridX = 6, F19GridZ = 5;
        public const float F19Spacing = 14f;
        public const float StressTile = 180f;
    }
}
