using System.Collections.Generic;
using System.Linq;
using Ashveil.World;
using UnityEngine;

namespace Ashveil.EditorTools.TerrainGen
{
    /// <summary>
    /// Welt-Progressionsgraph (Update 30.09. §K): welche Regionen sinnvoll aufeinander folgen. Kanten = Nachbarschaft,
    /// begehbar in Stufenrichtung, wenn sich die Stufenbereiche beruehren/ueberlappen. Prueft: von jedem Startgebiet ein
    /// lueckenloser Weg bis 60 (§101), ab Stufe 10 mindestens zwei Wahlmoeglichkeiten (§E), verbuendete Starts naeher
    /// als gegnerische (§102, nur Designer-Warnung).
    /// </summary>
    public sealed class WorldProgressionGraph
    {
        public readonly Dictionary<string, MacroRegionDefinition> Regions = new Dictionary<string, MacroRegionDefinition>();
        public readonly Dictionary<string, HashSet<string>> Edges = new Dictionary<string, HashSet<string>>();
        public readonly List<string> Errors = new List<string>(), Warnings = new List<string>(), Info = new List<string>();

        public static WorldProgressionGraph Build(MacroWorldProfile profile)
        {
            var g = new WorldProgressionGraph();
            foreach (MacroRegionDefinition r in profile.regions) g.Regions[r.regionId] = r;
            foreach (MacroRegionDefinition r in profile.regions)
                foreach (string n in r.neighbors)
                {
                    if (!g.Regions.ContainsKey(n))
                    {
                        g.Warnings.Add($"{r.regionId}: Nachbar '{n}' existiert nicht");
                        continue;
                    }
                    g.Link(r.regionId, n);
                }
            return g;
        }

        private void Link(string a, string b)
        {
            if (!Edges.TryGetValue(a, out HashSet<string> sa)) Edges[a] = sa = new HashSet<string>();
            if (!Edges.TryGetValue(b, out HashSet<string> sb)) Edges[b] = sb = new HashSet<string>();
            sa.Add(b);
            sb.Add(a);
        }

        public IEnumerable<string> Neighbors(string id) => Edges.TryGetValue(id, out HashSet<string> s) ? s : Enumerable.Empty<string>();

        public static Vector2 Centroid(MacroRegionDefinition r)
        {
            if (r.polygon.Count == 0) return Vector2.zero;
            Vector2 c = Vector2.zero;
            foreach (Vector2 p in r.polygon) c += p;
            return c / r.polygon.Count;
        }

        /// <summary>Weiter in Stufenrichtung: Folgegebiet beginnt spaetestens 2 Stufen nach dem Ende des jetzigen.</summary>
        private bool CanProgress(MacroRegionDefinition from, MacroRegionDefinition to) =>
            !to.isSlot && to.levels.x <= from.levels.y + 2 && to.levels.y > from.levels.x + 4;

        public void Validate()
        {
            var starters = Regions.Values.Where(r => !string.IsNullOrEmpty(r.starterRaceId)).ToList();
            if (starters.Count == 0) Errors.Add("Kein Startgebiet definiert");
            foreach (MacroRegionDefinition s in starters)
            {
                // Breitensuche in Stufenrichtung
                var reach = new HashSet<string> { s.regionId };
                var queue = new Queue<string>();
                queue.Enqueue(s.regionId);
                int maxLevel = s.levels.y;
                while (queue.Count > 0)
                {
                    MacroRegionDefinition cur = Regions[queue.Dequeue()];
                    maxLevel = Mathf.Max(maxLevel, cur.levels.y);
                    foreach (string n in Neighbors(cur.regionId))
                    {
                        MacroRegionDefinition nr = Regions[n];
                        if (reach.Contains(n) || !CanProgress(cur, nr)) continue;
                        reach.Add(n);
                        queue.Enqueue(n);
                    }
                }
                if (maxLevel < 60) Errors.Add($"{s.starterRaceId}: kein lueckenloser Weg bis 60 (endet bei {maxLevel})");
                else Info.Add($"{s.starterRaceId} ({s.regionId}): Weg bis 60 ueber {reach.Count} Regionen");

                // Sackgassen: erreichbare Region unter 60 ohne Anschluss nach oben
                foreach (string id in reach)
                {
                    MacroRegionDefinition r = Regions[id];
                    if (r.levels.y >= 60) continue;
                    bool onward = Neighbors(id).Any(n => Regions[n].levels.y > r.levels.y && !Regions[n].isSlot);
                    if (!onward) Errors.Add($"{s.starterRaceId}: Sackgasse in {id} bei Stufe {r.levels.y}");
                }
                // Ab Stufe 10 mindestens zwei Gebiete je Stufe (ohne Startgebiete und Slots)
                for (int lvl = 10; lvl <= 20; lvl += 2)
                {
                    int options = reach.Count(id =>
                    {
                        MacroRegionDefinition r = Regions[id];
                        return !r.isSlot && r.tier != ProgressionTier.RaceStart && r.levels.x <= lvl && r.levels.y >= lvl;
                    });
                    if (options < 2) Warnings.Add($"{s.starterRaceId}: Stufe {lvl} nur {options} Gebiet(e) – Ziel ≥ 2 (§E)");
                }
            }

            // Verbuendete naeher als Gegner (Reiseaufwand ueber den Graphen)
            foreach (MacroRegionDefinition a in starters)
            {
                float allied = float.MaxValue, enemy = float.MaxValue;
                foreach (MacroRegionDefinition b in starters)
                {
                    if (a == b) continue;
                    float cost = TravelCost(a.regionId, b.regionId);
                    if (a.faction == b.faction) allied = Mathf.Min(allied, cost);
                    else enemy = Mathf.Min(enemy, cost);
                }
                if (allied < float.MaxValue && enemy < float.MaxValue)
                {
                    string line = $"{a.starterRaceId}: naechster Verbuendeter {allied / 1000f:0.0} km-Aequivalent, naechster Gegner {enemy / 1000f:0.0}";
                    if (enemy < allied * 1.5f) Warnings.Add(line + " – Gegner zu nah (§102)");
                    else Info.Add(line);
                }
            }
        }

        /// <summary>Reiseaufwand: kuerzester Weg ueber Regionsmitten, Grenzland/umkaempft teurer.</summary>
        public float TravelCost(string from, string to)
        {
            var dist = new Dictionary<string, float> { [from] = 0f };
            var open = new List<string> { from };
            while (open.Count > 0)
            {
                open.Sort((x, y) => dist[x].CompareTo(dist[y]));
                string cur = open[0];
                open.RemoveAt(0);
                if (cur == to) return dist[cur];
                foreach (string n in Neighbors(cur))
                {
                    MacroRegionDefinition nr = Regions[n];
                    if (nr.isSlot) continue;
                    float factor = nr.travelTier == TravelTier.Frontier ? 1.4f : nr.travelTier == TravelTier.Remote ? 1.8f : 1f;
                    float c = dist[cur] + Vector2.Distance(Centroid(Regions[cur]), Centroid(nr)) * factor;
                    if (!dist.TryGetValue(n, out float old) || c < old)
                    {
                        dist[n] = c;
                        if (!open.Contains(n)) open.Add(n);
                    }
                }
            }
            return float.MaxValue;
        }

        /// <summary>Stufenband einer Region fuer die Debug-Karte (0 = 1–10 … 5 = 50–60).</summary>
        public static int Band(MacroRegionDefinition r) => Mathf.Clamp((r.levels.x + r.levels.y) / 2 / 10, 0, 5);
    }
}
