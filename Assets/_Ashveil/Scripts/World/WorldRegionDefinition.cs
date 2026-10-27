using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.World
{
    /// <summary>Eine Region (Masterplan §10): eigene Identitaet, Nachbarn, Stufen, Regeln – besteht aus Zellen.</summary>
    [CreateAssetMenu(menuName = "Ashveil/Welt/Region", fileName = "Region")]
    public sealed class WorldRegionDefinition : ScriptableObject
    {
        public string regionId = "";
        public string displayName = "";
        [Tooltip("Grundflaeche x/z (min) und Groesse")] public Rect bounds;
        public List<string> neighbors = new List<string>();
        public Vector2Int recommendedLevel = new Vector2Int(1, 10);
        [Tooltip("Spieler-PvP-Regelwerk, GETRENNT von der Weltkrieg-Fiktion (Fraktionskrieg 05.10. §49): none / safe / contested / pvp. " +
                 "Wird nie aus factionAffinity abgeleitet. Alpha: kein Welt-PvP (ALPHA-002).")]
        public string pvpRules = "none";
        [Tooltip("Kennung eines Gebietsluft-/Audio-Profils (vorbereitet)")] public string atmosphereProfile = "";
        public string audioProfile = "";
        public List<WorldCellDefinition> cells = new List<WorldCellDefinition>();

        [Header("Weltprogression (30.09., PROVISIONAL)")]
        [Tooltip("Territoriale Zugehoerigkeit: wer den Raum grundsaetzlich beansprucht – nicht, wie viel Kampf dort stattfindet")]
        public FactionAffinity factionAffinity = FactionAffinity.Unknown;
        [Tooltip("Abschnitt im Stufen-/Reisebogen – kein Kriegszustand")]
        public ProgressionTier progressionTier = ProgressionTier.RaceStart;
        [Tooltip("Volk, das hier startet (human, dwarf, nharim, kharai) – leer, wenn kein Startgebiet")] public string starterRaceId = "";
        [Tooltip("VERALTET seit 05.10.: gleichbedeutend mit factionAffinity = Contested. Bleibt gespeichert, „Welt pruefen“ meldet Abweichungen.")]
        public bool isContested;
        public TravelTier travelTier = TravelTier.Local;
        [Tooltip("Handgebaut: kein Generator darf Gelaende oder Orte dieser Region ueberschreiben (Kareth!)")]
        public bool isProtectedFromGeneration;

        [Header("Lokale Konfliktknoten (Fraktionskrieg 05.10. §3B) – nur wo Geografie oder Interessen sie begruenden")]
        public List<ConflictNodeDefinition> conflictNodes = new List<ConflictNodeDefinition>();

        public int MinRecommendedLevel => recommendedLevel.x;
        public int MaxRecommendedLevel => recommendedLevel.y;

        /// <summary>Beanspruchen beide Seiten diesen Raum? (Ersetzt das veraltete Feld <see cref="isContested"/>.)</summary>
        public bool IsContestedTerritory => factionAffinity == FactionAffinity.Contested;

        public ConflictNodeDefinition FindConflictNode(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId) || conflictNodes == null) return null;
            foreach (ConflictNodeDefinition n in conflictNodes)
                if (n != null && n.nodeId == nodeId) return n;
            return null;
        }
    }
}
