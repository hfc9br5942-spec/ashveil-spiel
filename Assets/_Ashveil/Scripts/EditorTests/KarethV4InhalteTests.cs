using System.Collections.Generic;
using System.Linq;
using Ashveil.Data;
using Ashveil.EditorTools;
using NUnit.Framework;
using UnityEditor;
using C = Ashveil.Data.GlockenrastCatalog;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// Kareth v4, Welle 0 (#27, #28): Die Beidatei der Inhalte deckt alle Aufträge von Welle 0 (Q1–Q5, G1–G13 und die elf
    /// Ergänzungsaufträge K-A bis U-7) ab – jeder Geber,
    /// jede Abgabe und jedes Gesprächsziel hat eine Figur mit Kennung, jedes Erkundungsziel eine QuestArea, jede Fundstelle
    /// eine Lage. Dazu: Jeder Tagesablauf zeigt auf vorhandene Plätze, Saat und Namen sind eindeutig, Uhrzeiten liegen in 0–24.
    /// Seit 21.10.2026 (AUFTRAEGE-01): Auftragsfiguren schlafen nicht (WORLD-096).
    /// </summary>
    public class KarethV4InhalteTests
    {
        private static readonly string[] Welle0Auftraege =
        {
            "quest.blade_for_the_watch", "quest.tough_hides", "quest.view_over_the_hollow", "quest.the_faded", "quest.powder_for_the_watch",
            C.G1, C.G2, C.G3, C.G4, C.G5, C.G6, C.G7, C.G8, C.G9, C.G10, C.G11, C.G12, C.G13,
            // #28
            KarethCatalog.KA, KarethCatalog.KB, KarethCatalog.KC, KarethCatalog.KD, KarethCatalog.U1, KarethCatalog.U2, KarethCatalog.U3,
            KarethCatalog.U4, KarethCatalog.U5, KarethCatalog.U6, KarethCatalog.U7
        };

        private static KarethV4Inhalte.Daten Daten()
        {
            KarethV4Inhalte.Daten d = KarethV4Inhalte.Load();
            Assert.IsNotNull(d, "Beidatei fehlt oder ist nicht lesbar: " + KarethV4Inhalte.DataPath);
            return d;
        }

        private static List<QuestDefinition> Auftraege()
        {
            var list = new List<QuestDefinition>();
            foreach (string guid in AssetDatabase.FindAssets("t:QuestDefinition"))
            {
                var q = AssetDatabase.LoadAssetAtPath<QuestDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (q != null && Welle0Auftraege.Contains(q.Id)) list.Add(q);
            }
            return list;
        }

        [Test]
        public void Beidatei_ist_vollstaendig()
        {
            KarethV4Inhalte.Daten d = Daten();
            Assert.Greater(d.figuren.Length, 20, "Figuren");
            Assert.AreEqual(5, d.tiere.Length, "Tiere (Katze, Hund, zwei Ziegen, Maultier)");
            Assert.AreEqual(13, d.fundstellen.Length, "Fundstellen der Glockenrast-Kette");
            Assert.AreEqual(17, d.bereiche.Length, "Erkundungsziele (Q3, fünf der Glockenrast-Kette, elf der Ergänzungsaufträge #28)");
            foreach (string area in KarethCatalog.Areas())
                Assert.IsTrue(d.bereiche.Any(b => b.id == area), "#28 Erkundungsziel " + area);
        }

        [Test]
        public void Tagesablaeufe_zeigen_auf_vorhandene_Plaetze()
        {
            KarethV4Inhalte.Daten d = Daten();
            var plaetze = new HashSet<string>(d.plaetze.Select(p => p.id));
            foreach (KarethV4Inhalte.Figur f in d.figuren)
            {
                Assert.Greater(f.schritte.Length, 0, f.name);
                Assert.AreEqual(0f, f.schritte.Min(s => s.ab), 1e-4f, f.name + ": Tagesablauf beginnt um 0 Uhr");
                foreach (KarethV4Inhalte.Schritt s in f.schritte)
                {
                    Assert.IsTrue(plaetze.Contains(s.platz), $"{f.name}: Platz {s.platz} fehlt");
                    Assert.That(s.ab, Is.InRange(0f, 24f), f.name);
                }
            }
            foreach (KarethV4Inhalte.Tier t in d.tiere)
                foreach (KarethV4Inhalte.TierSchritt s in t.schritte)
                    Assert.IsTrue(plaetze.Contains(s.platz), $"{t.name}: Platz {s.platz} fehlt");
        }

        [Test]
        public void Saat_und_Namen_sind_eindeutig()
        {
            KarethV4Inhalte.Daten d = Daten();
            Assert.AreEqual(d.figuren.Length, d.figuren.Select(f => f.saat).Distinct().Count(), "Saat der Figuren");
            Assert.AreEqual(d.figuren.Length, d.figuren.Select(f => f.name).Distinct().Count(), "Namen der Figuren");
            Assert.AreEqual(d.tiere.Length, d.tiere.Select(t => t.saat).Distinct().Count(), "Saat der Tiere");
            Assert.AreEqual(d.orte.Length, d.orte.Select(o => o.id).Distinct().Count(), "Kennungen der Orte");
        }

        [Test]
        public void Jeder_Auftrag_von_Welle_0_hat_seine_Ziele_in_Kareth_v4()
        {
            KarethV4Inhalte.Daten d = Daten();
            var npcs = new HashSet<string>(KarethV4Inhalte.FigurKennungen(d));
            var bereiche = new HashSet<string>(KarethV4Inhalte.BereichKennungen(d));
            var funde = new HashSet<string>(KarethV4Inhalte.FundAktionen(d));
            List<QuestDefinition> quests = Auftraege();
            Assert.AreEqual(Welle0Auftraege.Length, quests.Count, "Auftrags-Assets von Welle 0 gefunden");
            foreach (QuestDefinition q in quests)
            {
                Assert.IsTrue(npcs.Contains(q.giverNpc), $"{q.Id}: Geber {q.giverNpc} fehlt");
                Assert.IsTrue(npcs.Contains(q.turnInNpc), $"{q.Id}: Abgabe {q.turnInNpc} fehlt");
                foreach (QuestObjective o in q.objectives)
                {
                    if (o.type == QuestObjectiveType.TalkTo) Assert.IsTrue(npcs.Contains(o.targetId), $"{q.Id}: Gespräch mit {o.targetId} fehlt");
                    if (o.type == QuestObjectiveType.Explore) Assert.IsTrue(bereiche.Contains(o.targetId), $"{q.Id}: Erkundungsziel {o.targetId} fehlt");
                }
            }
            foreach (C.Node n in C.Nodes) Assert.IsTrue(funde.Contains(n.Action), "Fundstelle " + n.Action + " fehlt");
            Assert.IsFalse(npcs.Contains("npc.vela_salk"), "Vela gehört nicht zu Welle 0 (Q6–Q10 in der Region Salzsee)");
        }

        /// <summary>
        /// 21.10.2026 (AUFTRAEGE-01, WORLD-096): Keine Figur von Kareth v4 mit Kennung, die Geber, Abgabe oder Gesprächsziel eines
        /// Auftrags ist, hat einen Schritt „Sleep“ – schlafend ist sie ausgeblendet (ohne Körper, Namensschild mit „!“/„?“ und
        /// Klickfläche, NpcRoutine.SetHidden) und der Auftrag weder annehmbar noch abgebbar; im Build läuft die Weltuhr in Echtzeit.
        /// Auftragsfiguren dösen nachts sitzend. Grete (N01, N11, B6, B8) schlief 21–6 Uhr. Nur die Beidatei von Kareth v4: in der
        /// Sandbox schlafen Oswald und Tamo absichtlich (CampLife-Suite). Kennung wie beim Bau (Sandbox-Figur gleichen Namens).
        /// </summary>
        [Test]
        public void Auftragsfiguren_schlafen_nicht()
        {
            KarethV4Inhalte.Daten d = Daten();
            var rollen = new Dictionary<string, List<string>>();
            void Rolle(string npc, string wozu)
            {
                if (string.IsNullOrEmpty(npc)) return;
                if (!rollen.TryGetValue(npc, out List<string> l)) rollen[npc] = l = new List<string>();
                l.Add(wozu);
            }
            foreach (string guid in AssetDatabase.FindAssets("t:QuestDefinition"))
            {
                var q = AssetDatabase.LoadAssetAtPath<QuestDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (q == null) continue;
                Rolle(q.giverNpc, q.Id + " Geber");
                Rolle(q.turnInNpc, q.Id + " Abgabe");
                if (q.objectives != null)
                    foreach (QuestObjective o in q.objectives)
                        if (o.type == QuestObjectiveType.TalkTo) Rolle(o.targetId, q.Id + " Gespräch");
            }
            Assert.Greater(rollen.Count, 0, "Auftrags-Assets gefunden");

            var geprueft = new HashSet<string>();
            var fehler = new List<string>();
            foreach (KarethV4Inhalte.Figur f in d.figuren)
            {
                // je Figur einzeln: FigurKennungen überspringt Figuren ohne Kennung, die Liste wäre sonst nicht zuzuordnen
                List<string> kennung = KarethV4Inhalte.FigurKennungen(new KarethV4Inhalte.Daten { figuren = new[] { f } });
                if (kennung.Count == 0 || !rollen.TryGetValue(kennung[0], out List<string> wozu)) continue;
                geprueft.Add(kennung[0]);
                foreach (KarethV4Inhalte.Schritt s in f.schritte)
                    if ((s.tun ?? "").Split('|').Any(t => t.Trim() == "Sleep"))
                        fehler.Add($"{f.name} ({kennung[0]}; {string.Join(", ", wozu.Take(4))}): ab {s.ab} Uhr „{s.tun}“ an {s.platz}");
            }
            Assert.IsTrue(geprueft.Contains(KarethCatalog.Grete), "Grete (Geberin N01/N11/B6, Abgabe B8) wird geprüft – Kennung aus dem Namen gefunden");
            Assert.Greater(geprueft.Count, 10, "Auftragsfiguren mit Kennung in der Beidatei");
            Assert.IsEmpty(fehler, "Auftragsfiguren schlafen (WORLD-096: dösen sitzend statt ausgeblendet):\n" + string.Join("\n", fehler));
        }
    }
}
