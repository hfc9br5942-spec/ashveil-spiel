using System;
using System.Collections.Generic;
using Ashveil.World;
using UnityEngine;

namespace Ashveil.EditorTools.TerrainGen
{
    // Datenmodell des Terrain-Generators V1 (Terrain-Vorgabe §17–40, World-Foundation §26). Alles Lage-/Namensbezogene
    // ist PROVISIONAL bzw. DEV_ONLY; die Standardwerte stehen in MacroWorldDefaults (Weltprogressions-Plan 30.09.).

    /// <summary>Gebirgszug als Linienzug: Distanzfeld + Ridged fBM + Domain Warp (§17–20).</summary>
    [Serializable]
    public sealed class MountainBeltDefinition
    {
        public string id = "";
        public List<Vector2> controlPoints = new List<Vector2>();
        [Tooltip("Gesamtbreite inkl. Vorberge (m)")] public float width = 6000f;
        [Tooltip("Hoehe der Vorberge ueber dem Umland (m)")] public float baseElevation = 150f;
        [Tooltip("Gipfelhoehe ueber dem Umland (m)")] public float peakElevation = 900f;
        [Range(0f, 1f)] public float ridgeStrength = 0.75f;
        [Tooltip("Wellenlaenge der Hauptgrate (m)")] public float ridgeScale = 2600f;
        [Tooltip("−1…1: eine Flanke breiter/sanfter als die andere")] [Range(-0.8f, 0.8f)] public float asymmetry;
        [Range(0f, 1f)] public float valleyStrength = 0.35f;
        [Range(0f, 1f)] public float roughness = 0.5f;
        [Tooltip("Gipfel entlang des Kamms variieren (0 = gleich hoch)")] [Range(0f, 1f)] public float peakVariation = 0.35f;
        public int seed = 1;
    }

    /// <summary>Tal / Schlucht entlang eines Linienzugs; z-Komponente der Punkte = Talsohle (Welthoehe).</summary>
    [Serializable]
    public sealed class ValleyDefinition
    {
        public string id = "";
        [Tooltip("x, z = Lage; y = Hoehe der Talsohle (m)")] public List<Vector3> centerline = new List<Vector3>();
        public float width = 1600f;
        public float floorWidth = 300f;
        [Tooltip("Exponent der Flanken: 1 = V/sanft, >2 = steil/U")] public float sideSlope = 1.6f;
        public float noise = 8f;
        [Tooltip("Maeandern (m Versatz)")] public float meander = 120f;
        public bool canyon;
    }

    /// <summary>Laenglicher Sattel ueber einen Kamm (§24) – kein runder Krater.</summary>
    [Serializable]
    public sealed class PassDefinition
    {
        public string id = "";
        public Vector2 start, end;
        public float width = 400f;
        public float targetElevation = 600f;
        public float shoulderFalloff = 700f;
        public float roughness = 10f;
        [Tooltip("Wie stark die Sattellinie zu den Enden ansteigt (m)")] public float traversalBias = 60f;
    }

    /// <summary>Hochplateau mit erkennbarer Oberflaeche und gebrochenen Raendern (§26).</summary>
    [Serializable]
    public sealed class PlateauDefinition
    {
        public string id = "";
        public Vector2 center;
        public Vector2 radii = new Vector2(800f, 600f);
        public float yaw;
        public float targetHeight = 400f;
        public float interiorNoise = 12f;
        public float edgeRoughness = 0.18f;
        [Range(0f, 1f)] public float cliffStrength = 0.6f;
        public float falloff = 350f;
    }

    /// <summary>See in einer natuerlichen Senke (§40) – z. B. der abflusslose Salzsee zwischen Kareth und Grenzgebirge.</summary>
    [Serializable]
    public sealed class LakeDefinition
    {
        public string id = "";
        public Vector2 center;
        public Vector2 radii = new Vector2(800f, 600f);
        public float yaw;
        [Tooltip("Tiefster Punkt (Welthoehe, m)")] public float floorElevation = -40f;
        [Tooltip("Wasserspiegel (Welthoehe, m)")] public float waterLevel = -24f;
        public float shoreWidth = 400f;
        [Tooltip("Abflusslos (Salzsee): kein Abfluss, Salzkrusten am Ufer")] public bool endorheic;
    }

    /// <summary>Kontrollpunkt der Grossen Kerbe (§29).</summary>
    [Serializable]
    public struct VeilScarPoint
    {
        public Vector2 position;
        public float width;
        [Tooltip("Nominelle Tiefe unter dem Rand (m, fuer Himmel/Doku)")] public float depth;
        [Tooltip("Hoehe der Kerbensohle (Welthoehe, m) – fallend nach Westen, der Fluss darauf muendet ins Meer")] public float floorElevation;
        public float rimUplift;
        [Range(0f, 1f)] public float fractureStrength;
        [Range(0f, 1f)] public float terraceStrength;
        [Range(-1f, 1f)] public float asymmetry;
        [Range(0f, 1f)] public float glassFieldProbability;
        [Range(0f, 1f)] public float secondaryFractureStrength;
        [Range(0f, 1f)] public float veilIntensity;
    }

    /// <summary>Nebenbruch der Kerbe (§33): wenige grosse Aeste, keilt zur Spitze hin aus.</summary>
    [Serializable]
    public sealed class SecondaryFractureDefinition
    {
        public string id = "";
        [Tooltip("Erster Punkt liegt an der Kerbe, letzter an der Spitze")] public List<Vector2> path = new List<Vector2>();
        public float width = 3000f;
        public float floorElevation = -40f;
        public float tipDepth = 60f;
    }

    [Serializable]
    public sealed class VeilScarDefinition
    {
        public string id = "great_veil_scar";
        public List<VeilScarPoint> points = new List<VeilScarPoint>();
        public List<SecondaryFractureDefinition> branches = new List<SecondaryFractureDefinition>();
        [Tooltip("Scheinbare Schleierhoehe fuer den Himmel (m)")] public float veilHeight = 3500f;
    }

    public enum AncientScarType
    {
        ProtectorFootprint,
        GlassField,
        StraightFault,
        MineralStructure,
        WideDepression
    }

    /// <summary>Handgesetzte uralte Spur (§27, §113–114). Nie zufaellig verteilt.</summary>
    [Serializable]
    public sealed class AncientScarDefinition
    {
        public string id = "";
        public AncientScarType type;
        public Vector2 center;
        public float yaw;
        public float length = 600f;
        public float width = 400f;
        public float depth = 20f;
        [Tooltip("Liegt in handgebautem Gelaende (z. B. Fussabdruck im Grenzgebirge): nur Daten, formt nichts")] public bool representsExisting;
        [Tooltip("Identitaet UNBEKANNT – nicht definieren")] public string note = "";
    }

    [Serializable]
    public sealed class RiverDefinition
    {
        public string id = "";
        [Tooltip("Von der Quelle zur Muendung")] public List<Vector2> path = new List<Vector2>();
        public float width = 40f;
        public float depth = 6f;
        [Tooltip("Aus Kandidaten befoerdert (PROMOTE TO RIVER) – sonst von Hand")] public bool promoted;
        [Tooltip("Nur mit ausdruecklicher magischer Begruendung (§103). Nie automatisch setzen.")] public bool magicalException;
    }

    [Serializable]
    public sealed class RoadCorridorDefinition
    {
        public string id = "";
        public RouteClass routeClass = RouteClass.Main;
        public List<Vector2> path = new List<Vector2>();
        public float width = 24f;
        [Tooltip("Grad")] public float maxSlope = 12f;
        [Range(0f, 1f)] public float flattenStrength = 0.6f;
        [Range(0f, 1f)] public float cutStrength = 0.6f;
        [Range(0f, 1f)] public float fillStrength = 0.4f;
        public string fromPoi = "", toPoi = "";
        [Tooltip("Spaeter Karrentrasse (World-Foundation §28)")] public bool cartRoute;
    }

    /// <summary>Makro-Region fuer Progressions-/Fraktionskarte. Flaeche als Vieleck (x, z).</summary>
    [Serializable]
    public sealed class MacroRegionDefinition
    {
        public string regionId = "";
        public string displayName = "";
        public List<Vector2> polygon = new List<Vector2>();
        public Vector2Int levels = new Vector2Int(1, 10);
        public FactionAffinity faction = FactionAffinity.Unknown;
        public ProgressionTier tier = ProgressionTier.RaceStart;
        public string starterRaceId = "";
        public bool contested;
        public TravelTier travelTier = TravelTier.Regional;
        [Tooltip("Handgebaut (Kareth, Westweiden, Grenzgebirge-Kern)")] public bool isProtectedFromGeneration;
        public List<string> neighbors = new List<string>();
        [Tooltip("Leerer Platz fuer ein noch offenes Volk – keine Namen, keine Lore")] public bool isSlot;
    }

    [Serializable]
    public sealed class MacroPoiDefinition
    {
        public string id = "";
        public string label = "";
        public string regionId = "";
        public Vector2 position;
        public string poiType = "landmark";
        [Range(0, 3)] public int importance = 1;
        public float visibleDistance = 3000f;
        public HubTier hubTier = HubTier.None;
        public int recommendedLevel;
        public FactionAffinity faction = FactionAffinity.Unknown;
        public List<string> connectedPoiIds = new List<string>();
        [Tooltip("Kamerapunkt fuer Spielerhoehen-Test (§90)")] public bool viewpoint;
    }

    /// <summary>Handgebautes Gelaende, das nie ueberschrieben wird (§75). Hoehen kommen aus den vorhandenen TerrainData.</summary>
    [Serializable]
    public sealed class ProtectedAreaDefinition
    {
        public string id = "";
        public Rect rect;
        [Tooltip("TerrainData-Assets, deren Hoehen den Bereich beschreiben")] public List<string> terrainDataPaths = new List<string>();
    }
}
