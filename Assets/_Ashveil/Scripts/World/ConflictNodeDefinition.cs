using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.World
{
    /// <summary>
    /// Art eines lokalen Konfliktknotens (Fraktionskrieg-Direktive 05.10., §3B). Ein Knoten existiert nur, wenn Geografie,
    /// Geschichte, Ressourcen oder heutige Interessen ihn begruenden – nie, weil „noch PvP fehlt“.
    /// Werte werden als Zahl gespeichert: nur hinten anhaengen, nie umnummerieren.
    /// </summary>
    public enum ConflictNodeKind
    {
        Bridge = 0,
        Pass = 1,
        Crossroads = 2,
        Harbor = 3,
        Mine = 4,
        Village = 5,
        Ruin = 6,
        WaterSource = 7,
        Grave = 8,
        Shrine = 9,
        Resource = 10,
        DungeonEntrance = 11,
        WorldBossArena = 12,
        SupplyRoute = 13,
        Outpost = 14,
        Ford = 15
    }

    /// <summary>Wer den Ort braucht oder beansprucht (Bits). Ein Knoten im Heimatgebiet einer Seite darf beide nennen.</summary>
    [Flags]
    public enum FactionClaim
    {
        None = 0,
        SideA = 1,
        SideB = 2,
        Both = SideA | SideB
    }

    /// <summary>
    /// Lokaler Konfliktknoten (Fraktionskrieg-Direktive 05.10., §3B und §50): ein konkreter Ort, an dem sich die Interessen
    /// beider Seiten glaubwuerdig ueberschneiden – mit dem Grund je Seite, den Routen, die dort zusammenlaufen, und optional
    /// kuratierten lokalen Zustaenden.
    ///
    /// Reine Daten: keine Laufzeitlogik, keine Kriegssimulation, kein Einfluss auf Spawns oder Netz (CORE-005). Wechselt ein
    /// Zustand spaeter wirklich im Spiel, dann serverautoritaer ueber die vorhandene Geschichts-/Weltzustands-Infrastruktur und
    /// nur fuer dafuer entworfene Orte. Feste Wachposten bleiben fest (Max 02.10.). Das Spieler-PvP-Regelwerk ist getrennt
    /// (<see cref="pvpRuleset"/>, sonst die Regel der Region).
    /// </summary>
    [Serializable]
    public sealed class ConflictNodeDefinition
    {
        [Tooltip("Eindeutig in der ganzen Welt, z. B. bruchmark.furt_schwemmkegel")] public string nodeId = "";
        [Tooltip("Arbeitsname – Eigennamen ohne Max' Entscheidung bleiben Vorschlag")] public string displayName = "";
        public ConflictNodeKind kind = ConflictNodeKind.Crossroads;
        [Tooltip("Weltposition der Knotenmitte (x, Hoehe, z)")] public Vector3 position;
        [Min(0f)] public float radius = 60f;
        public FactionClaim claimedBy = FactionClaim.Both;
        [Tooltip("Warum Seite A diesen Ort braucht (Geografie, Geschichte, Ressource, Interesse)")]
        [TextArea(1, 3)] public string reasonSideA = "";
        [Tooltip("Warum Seite B diesen Ort braucht")]
        [TextArea(1, 3)] public string reasonSideB = "";
        [Tooltip("Kuratierte lokale Zustaende, nur fuer dafuer entworfene Orte (z. B. a_kontrolle, umkaempft, b_kontrolle, verwuestet). Leer = fester Ort.")]
        public List<string> localStates = new List<string>();
        [Tooltip("Ausgangszustand; muss in localStates stehen, wenn es welche gibt")] public string defaultState = "";
        public List<string> questIds = new List<string>();
        public List<string> npcIds = new List<string>();
        [Tooltip("Wege/Routen der Seite A, die hier zusammenlaufen")] public List<string> routeIdsSideA = new List<string>();
        [Tooltip("Wege/Routen der Seite B, die hier zusammenlaufen")] public List<string> routeIdsSideB = new List<string>();
        [Tooltip("Getrenntes PvP-Regelwerk fuer diesen Ort; leer = Regel der Region (WorldRegionDefinition.pvpRules)")]
        public string pvpRuleset = "";
        [Tooltip("true, solange Max den Knoten nicht bestaetigt hat ([VORSCHLAG])")] public bool proposalOnly = true;

        public bool ClaimedBySideA => (claimedBy & FactionClaim.SideA) != 0;
        public bool ClaimedBySideB => (claimedBy & FactionClaim.SideB) != 0;
        public bool HasLocalStates => localStates != null && localStates.Count > 0;
    }
}
