using System;
using System.Linq;
using Ashveil.Data;
using Ashveil.EditorTools;
using Ashveil.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// Fraktionskrieg-Direktive 05.10.: territoriale Zugehoerigkeit und lokale Konfliktknoten als Daten.
    /// Prueft die Seitenzuordnung der Voelker, die Regeln, die Autorenpruefung, die Regionsdaten und dass es keinen
    /// regionsweiten Kriegszustand gibt. Laeuft im Test Runner (EditMode, gleiche Test-Assembly wie die Relief-Tests).
    /// </summary>
    public sealed class TerritoryTests
    {
        [Test]
        public void RaceFactionIds_MapToSides()
        {
            Assert.AreEqual(FactionAffinity.HumanAlliance, TerritoryRules.SideOf(RaceCatalog.Human.faction), "Menschen = Seite A");
            Assert.AreEqual(FactionAffinity.NharimAlliance, TerritoryRules.SideOf(RaceCatalog.Nharim.faction), "Nharim = Seite B");
            Assert.AreEqual(FactionAffinity.Unknown, TerritoryRules.SideOf("faction.unbekannt"));
            Assert.AreEqual(FactionAffinity.Unknown, TerritoryRules.SideOf(null));
            foreach (RaceDefinition r in RaceCatalog.All)
                Assert.IsTrue(TerritoryRules.IsSide(TerritoryRules.SideOf(r.faction)), $"Volk {r.id} ohne Seite");
        }

        [Test]
        public void Territory_IsNotWarIntensity()
        {
            // Heimat heisst „wer beansprucht den Raum“, nicht „wie viel Kampf“ – und ein Heimatgebiet darf einen Knoten haben,
            // den beide Seiten brauchen (Direktive §3A: einzelne feindliche Operationen im tiefen Heimatgebiet).
            Assert.IsTrue(TerritoryRules.IsHomeTerritory(FactionAffinity.HumanAlliance, FactionAffinity.HumanAlliance));
            Assert.IsFalse(TerritoryRules.IsHomeTerritory(FactionAffinity.HumanAlliance, FactionAffinity.NharimAlliance));
            Assert.IsFalse(TerritoryRules.IsHomeTerritory(FactionAffinity.Contested, FactionAffinity.HumanAlliance));
            Assert.IsFalse(TerritoryRules.IsHomeTerritory(FactionAffinity.Neutral, FactionAffinity.Neutral), "neutral ist keine Seite");
            Assert.IsTrue(TerritoryRules.IsShared(FactionAffinity.Contested));
            Assert.IsFalse(TerritoryRules.IsShared(FactionAffinity.Neutral));

            WorldRegionDefinition home = Region("test.heimat", FactionAffinity.HumanAlliance);
            home.conflictNodes.Add(Node("test.heimat.spaeherpfad", FactionClaim.Both, "Nachschubweg", "Spaeherweg"));
            Assert.IsEmpty(TerritoryRules.Validate(home), string.Join("\n", TerritoryRules.Validate(home)));
            Assert.IsTrue(TerritoryRules.Claims(home.conflictNodes[0], FactionAffinity.NharimAlliance));
            UnityEngine.Object.DestroyImmediate(home);
        }

        [Test]
        public void Validate_FindsAuthoringMistakes()
        {
            WorldRegionDefinition r = Region("test.fehler", FactionAffinity.Unknown);
            r.pvpRules = "immer";
            r.isContested = true;
            ConflictNodeDefinition a = Node("test.fehler.furt", FactionClaim.Both, "", "Wasser");
            a.localStates.Add("a_kontrolle");
            a.localStates.Add("umkaempft");
            a.defaultState = "b_kontrolle";
            ConflictNodeDefinition b = Node("test.fehler.furt", FactionClaim.None, "", "");
            b.position = new Vector3(9999f, 0f, 9999f);
            r.conflictNodes.Add(a);
            r.conflictNodes.Add(b);
            string all = string.Join("\n", TerritoryRules.Validate(r));
            StringAssert.Contains("territoriale Zugehoerigkeit fehlt", all);
            StringAssert.Contains("veraltetes Feld isContested", all);
            StringAssert.Contains("unbekannte PvP-Regel 'immer'", all);
            StringAssert.Contains("Grund fuer Seite A fehlt", all);
            StringAssert.Contains("Ausgangszustand 'b_kontrolle' fehlt", all);
            StringAssert.Contains("doppelter Konfliktknoten", all);
            StringAssert.Contains("niemand beansprucht", all);
            StringAssert.Contains("ausserhalb der Region", all);
            UnityEngine.Object.DestroyImmediate(r);
        }

        [Test]
        public void ConflictNode_SurvivesSerialization()
        {
            WorldRegionDefinition r = Region("test.serial", FactionAffinity.Contested);
            ConflictNodeDefinition n = Node("test.serial.bruecke", FactionClaim.Both, "Landweg", "alter Uebergang");
            n.kind = ConflictNodeKind.Bridge;
            n.localStates.Add("neutral_besetzt");
            n.defaultState = "neutral_besetzt";
            n.routeIdsSideA.Add("route.a");
            n.routeIdsSideB.Add("route.b");
            r.conflictNodes.Add(n);
            string json = EditorJsonUtility.ToJson(r);
            WorldRegionDefinition copy = ScriptableObject.CreateInstance<WorldRegionDefinition>();
            EditorJsonUtility.FromJsonOverwrite(json, copy);
            ConflictNodeDefinition c = copy.FindConflictNode("test.serial.bruecke");
            Assert.IsNotNull(c);
            Assert.AreEqual(ConflictNodeKind.Bridge, c.kind);
            Assert.AreEqual(FactionClaim.Both, c.claimedBy);
            Assert.AreEqual("neutral_besetzt", c.defaultState);
            CollectionAssert.AreEqual(new[] { "route.a" }, c.routeIdsSideA);
            Assert.AreEqual(FactionAffinity.Contested, copy.factionAffinity);
            UnityEngine.Object.DestroyImmediate(r);
            UnityEngine.Object.DestroyImmediate(copy);
        }

        [Test]
        public void WorldManifest_RegionsHaveTerritory()
        {
            var m = AssetDatabase.LoadAssetAtPath<WorldManifest>(WorldManifestBuilder.ManifestPath);
            if (m == null) Assert.Ignore("Weltverzeichnis fehlt");
            WorldRegionDefinition kareth = m.FindRegion(WorldManifestBuilder.RegionKareth);
            Assert.IsNotNull(kareth);
            Assert.AreEqual(FactionAffinity.HumanAlliance, kareth.factionAffinity, "Kareth ist menschlich kontrollierte Startregion");
            Assert.AreEqual(0, kareth.conflictNodes.Count, "Kareth braucht in Welle 0 keinen Fraktions-Konfliktknoten");
            Assert.AreEqual("none", kareth.pvpRules, "kein Welt-PvP in der Alpha (ALPHA-002)");
            foreach (WorldRegionDefinition r in m.regions.Where(x => x != null))
                Assert.IsEmpty(TerritoryRules.Validate(r), string.Join("\n", TerritoryRules.Validate(r)));
        }

        [Test]
        public void NoRegionWideWarState()
        {
            // Direktive 05.10. §3: der regionsweite WarPresenceState (Heimat → Grenzland → Front) ist verworfen.
            string[] banned = { "WarPresenceState", "FrontWorldState" };
            foreach (System.Reflection.Assembly asm in AshveilAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                foreach (Type t in types)
                    Assert.IsFalse(banned.Contains(t.Name), $"{t.FullName}: regionsweiter Kriegszustand ist verworfen");
            }
        }

        private static WorldRegionDefinition Region(string id, FactionAffinity affinity)
        {
            WorldRegionDefinition r = ScriptableObject.CreateInstance<WorldRegionDefinition>();
            r.regionId = id;
            r.factionAffinity = affinity;
            r.isContested = affinity == FactionAffinity.Contested;
            r.bounds = new Rect(-500f, -500f, 1000f, 1000f);
            return r;
        }

        private static ConflictNodeDefinition Node(string id, FactionClaim claim, string whyA, string whyB) =>
            new ConflictNodeDefinition { nodeId = id, claimedBy = claim, reasonSideA = whyA, reasonSideB = whyB, position = Vector3.zero };

        /// <summary>
        /// Alle ASHVEIL-Assemblies, ausgehend von den referenzierten Assemblies und ihren Verweisen – ohne
        /// AppDomain.GetAssemblies (Unity-Analyzer UAC0005).
        /// </summary>
        private static System.Collections.Generic.IEnumerable<System.Reflection.Assembly> AshveilAssemblies()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            var queue = new System.Collections.Generic.Queue<System.Reflection.Assembly>(new[]
            {
                typeof(WorldRegionDefinition).Assembly, typeof(WorldManifestBuilder).Assembly, typeof(RaceCatalog).Assembly
            });
            while (queue.Count > 0)
            {
                System.Reflection.Assembly a = queue.Dequeue();
                if (!seen.Add(a.GetName().Name)) continue;
                yield return a;
                foreach (System.Reflection.AssemblyName r in a.GetReferencedAssemblies())
                {
                    if (!r.Name.StartsWith("Ashveil") || seen.Contains(r.Name)) continue;
                    System.Reflection.Assembly loaded = null;
                    try { loaded = System.Reflection.Assembly.Load(r); }
                    catch (Exception) { /* nicht ladbar: auslassen */ }
                    if (loaded != null) queue.Enqueue(loaded);
                }
            }
        }
    }
}
