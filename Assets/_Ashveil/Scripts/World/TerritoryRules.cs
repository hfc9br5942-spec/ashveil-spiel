using System.Collections.Generic;

namespace Ashveil.World
{
    /// <summary>
    /// Territoriale Zugehoerigkeit und lokale Konfliktknoten (Fraktionskrieg-Direktive 05.10.): kleine, reine Regeln ohne
    /// Laufzeitzustand. Territorium beantwortet „wer beansprucht den Raum?“, nie „wie viel Kampf findet dort statt?“.
    /// Es gibt bewusst keinen regionsweiten Kriegszustand (Heimat → Grenzland → Front ist verworfen).
    /// </summary>
    public static class TerritoryRules
    {
        /// <summary>Seitenkennungen der Voelker (Data/RaceCatalog: RaceDefinition.faction). Bleiben aus Kompatibilitaet so benannt.</summary>
        public const string FactionIdSideA = "faction.human";
        public const string FactionIdSideB = "faction.nharim";

        /// <summary>Erlaubte Werte des getrennten PvP-Regelwerks einer Region (WorldRegionDefinition.pvpRules).</summary>
        public static readonly string[] PvpRuleIds = { "none", "safe", "contested", "pvp" };

        /// <summary>Seite eines Volkes aus seiner Seitenkennung; unbekannt → Unknown.</summary>
        public static FactionAffinity SideOf(string factionId)
        {
            switch (factionId)
            {
                case FactionIdSideA: return FactionAffinity.HumanAlliance;
                case FactionIdSideB: return FactionAffinity.NharimAlliance;
                default: return FactionAffinity.Unknown;
            }
        }

        /// <summary>Ist das eine der beiden Seiten (und nicht neutral, geteilt oder unbekannt)?</summary>
        public static bool IsSide(FactionAffinity a) => a == FactionAffinity.HumanAlliance || a == FactionAffinity.NharimAlliance;

        /// <summary>Gehoert der Raum grundsaetzlich der Seite des Spielers? Sagt nichts darueber, ob dort gekaempft wird.</summary>
        public static bool IsHomeTerritory(FactionAffinity territory, FactionAffinity side) => IsSide(side) && territory == side;

        /// <summary>Beanspruchen beide Seiten den Raum („geteilt“, WORLD-218)?</summary>
        public static bool IsShared(FactionAffinity territory) => territory == FactionAffinity.Contested;

        /// <summary>Anspruchsbit einer Seite.</summary>
        public static FactionClaim ClaimOf(FactionAffinity side) =>
            side == FactionAffinity.HumanAlliance ? FactionClaim.SideA :
            side == FactionAffinity.NharimAlliance ? FactionClaim.SideB : FactionClaim.None;

        /// <summary>Braucht die Seite diesen Knoten (steht sie in claimedBy)?</summary>
        public static bool Claims(ConflictNodeDefinition node, FactionAffinity side) =>
            node != null && (node.claimedBy & ClaimOf(side)) != 0;

        /// <summary>
        /// Autorenpruefung einer Region (keine Laufzeitregel): fehlende Zugehoerigkeit, veraltetes Feld isContested, unbekannte
        /// PvP-Regel, Knoten ohne Kennung, Anspruch oder Grund, Zustandsliste ohne Ausgangszustand, Knoten ausserhalb der Region.
        /// </summary>
        public static List<string> Validate(WorldRegionDefinition region)
        {
            var hints = new List<string>();
            if (region == null) return hints;
            string r = string.IsNullOrEmpty(region.regionId) ? "(ohne Kennung)" : region.regionId;
            if (region.factionAffinity == FactionAffinity.Unknown)
                hints.Add($"{r}: territoriale Zugehoerigkeit fehlt (factionAffinity)");
            if (region.isContested != region.IsContestedTerritory)
                hints.Add($"{r}: veraltetes Feld isContested ({region.isContested}) widerspricht factionAffinity ({region.factionAffinity})");
            if (System.Array.IndexOf(PvpRuleIds, region.pvpRules) < 0)
                hints.Add($"{r}: unbekannte PvP-Regel '{region.pvpRules}' (erlaubt: {string.Join(", ", PvpRuleIds)})");
            if (region.conflictNodes == null) return hints;
            var ids = new HashSet<string>();
            foreach (ConflictNodeDefinition n in region.conflictNodes)
            {
                if (n == null) { hints.Add($"{r}: leerer Konfliktknoten"); continue; }
                string id = string.IsNullOrEmpty(n.nodeId) ? "(ohne Kennung)" : n.nodeId;
                if (string.IsNullOrEmpty(n.nodeId)) hints.Add($"{r}: Konfliktknoten ohne Kennung");
                else if (!ids.Add(n.nodeId)) hints.Add($"{r}: doppelter Konfliktknoten {n.nodeId}");
                if (n.claimedBy == FactionClaim.None) hints.Add($"{r}/{id}: niemand beansprucht den Knoten");
                if (n.ClaimedBySideA && string.IsNullOrWhiteSpace(n.reasonSideA)) hints.Add($"{r}/{id}: Grund fuer Seite A fehlt");
                if (n.ClaimedBySideB && string.IsNullOrWhiteSpace(n.reasonSideB)) hints.Add($"{r}/{id}: Grund fuer Seite B fehlt");
                if (n.HasLocalStates)
                {
                    var states = new HashSet<string>();
                    foreach (string s in n.localStates)
                        if (string.IsNullOrEmpty(s) || !states.Add(s)) hints.Add($"{r}/{id}: leerer oder doppelter lokaler Zustand");
                    if (!states.Contains(n.defaultState)) hints.Add($"{r}/{id}: Ausgangszustand '{n.defaultState}' fehlt in den lokalen Zustaenden");
                }
                else if (!string.IsNullOrEmpty(n.defaultState))
                    hints.Add($"{r}/{id}: Ausgangszustand ohne Zustandsliste");
                if (!string.IsNullOrEmpty(n.pvpRuleset) && System.Array.IndexOf(PvpRuleIds, n.pvpRuleset) < 0)
                    hints.Add($"{r}/{id}: unbekannte PvP-Regel '{n.pvpRuleset}'");
                if (region.bounds.width > 0f && region.bounds.height > 0f &&
                    (n.position.x < region.bounds.xMin || n.position.x > region.bounds.xMax ||
                     n.position.z < region.bounds.yMin || n.position.z > region.bounds.yMax))
                    hints.Add($"{r}/{id}: liegt ausserhalb der Region");
            }
            return hints;
        }
    }
}
