using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.EditorTools.TerrainGen
{
    /// <summary>Grossraeumige Anhebung des Umlands (Ebenen, Heide, Hochlande) – weicher Kreis.</summary>
    [Serializable]
    public struct RegionalElevation
    {
        public string id;
        public Vector2 center;
        public float radius;
        public float height;
    }

    /// <summary>Wald/Biom-Hinweis aus der Weltkarte (Silberwald, Kronenwald, Schattenwald …) – weicher Kreis.</summary>
    [Serializable]
    public struct BiomeHint
    {
        public string id;
        public Vector2 center;
        public float radius;
        public Ashveil.World.MacroBiome biome;
    }

    /// <summary>
    /// Profil der Makrowelt (Terrain-Vorgabe §2, §5, §111): Seed, Ausschnitt, Kueste, Gebirge, Taeler, Paesse, Plateaus,
    /// Kerbe, Fluesse, Wege, Regionen, POIs und geschuetzte Bereiche. Alles Designerdaten – der Generator liest nur.
    /// Standardwerte: <see cref="MacroWorldDefaults"/> (DEV_ONLY / PROVISIONAL, Weltprogressions-Plan 30.09.).
    /// </summary>
    public sealed class MacroWorldProfile : ScriptableObject
    {
        public const string AssetPath = TerrainGenPaths.DataFolder + "/MacroWorldProfile.asset";

        [Header("Welt")]
        [Tooltip("DEV_ONLY, keine Lore")] public int worldSeed = 137377;
        [Tooltip("Stand der Standardwerte – steigt, wenn MacroWorldDefaults sich aendert")] public int defaultsVersion;
        [Tooltip("Ausschnitt x/z (min) + Groesse. An das 1200-m-Kachelraster von Kareth gebunden.")]
        public Rect bounds = new Rect(-14200f, -14350f, 38000f, 26000f);
        public float cellSize = 40f;
        public float seaLevel = -24f;

        [Header("Kontinent")]
        [Tooltip("Kueste aus Max' Weltkarte (vorzeichenbehafteter Abstand, ContinentSdf.bytes). Hat Vorrang vor dem Vieleck.")]
        public TextAsset continentSdf;
        [Tooltip("Ersatz ohne Karte: Landflaeche (Vieleck) – ausserhalb liegt Meer")] public List<Vector2> coastline = new List<Vector2>();
        public float coastNoise = 500f;
        public float lowlandHeight = 30f;
        public float regionalNoiseAmp = 55f;
        public float regionalNoiseScale = 9000f;
        public List<RegionalElevation> regionalElevation = new List<RegionalElevation>();
        [Tooltip("Waelder und andere Biome, wie sie auf der Weltkarte stehen")] public List<BiomeHint> biomeHints = new List<BiomeHint>();

        [Header("Geologie")]
        public List<MountainBeltDefinition> mountainBelts = new List<MountainBeltDefinition>();
        public List<ValleyDefinition> valleys = new List<ValleyDefinition>();
        public List<PassDefinition> passes = new List<PassDefinition>();
        public List<PlateauDefinition> plateaus = new List<PlateauDefinition>();
        public List<LakeDefinition> lakes = new List<LakeDefinition>();
        public VeilScarDefinition veilScar = new VeilScarDefinition();
        public List<AncientScarDefinition> ancientScars = new List<AncientScarDefinition>();

        [Header("Wasser und Wege")]
        public List<RiverDefinition> rivers = new List<RiverDefinition>();
        [Tooltip("Ab welcher Einzugsflaeche (km²) eine Abflussrinne ein Flusskandidat ist")] public float riverCandidateAreaKm2 = 30f;
        public List<RoadCorridorDefinition> roads = new List<RoadCorridorDefinition>();

        [Header("Erosion und Detail")]
        public int erosionIterations = 10;
        [Tooltip("Grad")] public float talusAngle = 38f;
        [Range(0f, 1f)] public float erosionStrength = 0.35f;
        public float localDetailAmp = 5f;

        [Header("Regionen, Orte, Schutz")]
        public List<MacroRegionDefinition> regions = new List<MacroRegionDefinition>();
        public List<MacroPoiDefinition> pois = new List<MacroPoiDefinition>();
        public List<ProtectedAreaDefinition> protectedAreas = new List<ProtectedAreaDefinition>();
        [Tooltip("Uebergangszone um handgebautes Gelaende (Vorgabe §10: 300–800 m)")] public float transitionDistance = 600f;

        public int GridWidth => Mathf.RoundToInt(bounds.width / cellSize) + 1;
        public int GridHeight => Mathf.RoundToInt(bounds.height / cellSize) + 1;
    }

    public static class TerrainGenPaths
    {
        public const string DataFolder = ProjectSetup.Root + "/Data/World/Generation";
        /// <summary>Alles hier ist jederzeit loeschbar, ohne Handarbeit zu beschaedigen (Vorgabe §1, §105).</summary>
        public const string GeneratedRoot = ProjectSetup.Root + "/Generated/World";
        public const string TerrainFolder = GeneratedRoot + "/Terrain";
        public const string ProxyFolder = GeneratedRoot + "/Proxies";
        public const string PreviewFolder = GeneratedRoot + "/Preview";
        public const string ResourcesFolder = ProjectSetup.Root + "/Resources/World";
        public const string MacroDataPath = ResourcesFolder + "/MacroWorldData.asset";
        public const string VeilPathAsset = ResourcesFolder + "/VeilScarPath.asset";
        public const string ManifestPath = GeneratedRoot + "/GEN_Manifest.json";
    }
}
