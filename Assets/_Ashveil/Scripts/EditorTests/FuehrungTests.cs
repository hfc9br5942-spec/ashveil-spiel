using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashveil.Data;
using Ashveil.EditorTools;
using Ashveil.Maps;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using GC = Ashveil.Data.GlockenrastCatalog;
using KC = Ashveil.Data.KarethCatalog;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// Auftragsführung A/B/C in Kareth v4 (Max 10.10.2026: Liste freigegeben, G7 unmarkiert, G12 = B mit Unterzone Glockenmoor,
    /// Kreise auch auf der Minimap). Prüft die Tabelle (<see cref="KarethV4Fuehrung"/>), die geschriebene Datei und das
    /// Szenenraster (Nordmoor gehört zum Glockenmoor).
    /// </summary>
    public class FuehrungTests
    {
        private const string Ordner = "Assets/StreamingAssets/Ashveil/Karten";
        private static readonly Vector2 Versatz = new Vector2(8800f, 17270f);

        private static QuestDefinition Quest(string id)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:QuestDefinition"))
            {
                var q = AssetDatabase.LoadAssetAtPath<QuestDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (q != null && q.Id == id) return q;
            }
            return null;
        }

        [Test]
        public void Tabelle_60_Auftraege_53A_5B_2C()
        {
            // 29 freigegebene (22 A, 5 B, 2 C) + 15 Nebenaufträge N01–N15 (Max 10.10.2026 spät) + 11 Classic-Paket (Max 11.10.2026 abends), alle A
            // + Akt I „Das Register“ R1–R5 (13.10.2026), alle A (R6 führt in die Westweiden, außerhalb der Kareth-Karte)
            FuehrungDatei f = KarethV4Fuehrung.Bauen(out string warnung);
            Assert.AreEqual("", warnung, "Tabelle ohne Warnungen");
            Assert.AreEqual(60, f.auftraege.Length);
            Assert.AreEqual(60, f.auftraege.Select(a => a.quest).Distinct().Count(), "Jeder Auftrag einmal");
            foreach (string n in KC.NebenQuests) Assert.IsTrue(f.auftraege.Any(a => a.quest == n && a.stufe == "A"), n + ": geführt (A)");
            foreach (string n in KC.ClassicQuests) Assert.IsTrue(f.auftraege.Any(a => a.quest == n && a.stufe == "A"), n + ": geführt (A)");
            Assert.AreEqual(53, f.auftraege.Count(a => a.stufe == "A"));
            Assert.AreEqual(5, f.auftraege.Count(a => a.stufe == "B"));
            Assert.AreEqual(2, f.auftraege.Count(a => a.stufe == "C"));
            CollectionAssert.AreEquivalent(new[] { KC.U7, GC.G7 }, f.auftraege.Where(a => a.stufe == "C").Select(a => a.quest), "C: U-7 und G7");
            CollectionAssert.AreEquivalent(new[] { KC.U3, KC.U5, GC.G6, GC.G11, GC.G12 }, f.auftraege.Where(a => a.stufe == "B").Select(a => a.quest), "B: U-3, U-5, G6, G11, G12");
        }

        [Test]
        public void A_Kreise_weich_und_im_Gebiet()
        {
            FuehrungDatei f = KarethV4Fuehrung.Bauen(out _);
            float[] rahmen = { 6550f, 15270f, 10646f, 19366f };          // Kartenrahmen Kareth (Welt-m)
            foreach (FuehrungAuftrag a in f.auftraege.Where(a => a.stufe == "A"))
            {
                Assert.IsTrue(a.kreise != null && a.kreise.Length >= 1, a.quest + ": mindestens ein Kreis");
                foreach (FuehrungKreis k in a.kreise)
                {
                    Assert.GreaterOrEqual(k.radius, 60f, a.quest + ": Kreis mindestens 60 m (UI-202)");
                    Assert.LessOrEqual(k.radius, 200f, a.quest + ": Kreis höchstens 200 m (sonst ist es keine Führung mehr)");
                    Assert.AreEqual(0f, k.radius % 10f, a.quest + ": auf 10 m gerundet");
                    Assert.IsTrue(k.x > rahmen[0] && k.x < rahmen[2] && k.z > rahmen[1] && k.z < rahmen[3], $"{a.quest}: Kreis {k.x:0}/{k.z:0} liegt in Kareth");
                }
                Assert.AreEqual(0, a.unterzone, a.quest + ": A hat keine Unterzone");
                if (a.abgabe != null && a.abgabe.radius > 0f) Assert.AreEqual(60f, a.abgabe.radius, a.quest + ": Abgabekreis 60 m");
            }
            // Ein Kreis je weit auseinanderliegendem Ziel: U-2 erst Oswald (Ziel 0), dann die Schlickwerfer (Ziel 1 und 2, gleiche Lage)
            FuehrungAuftrag u2 = f.auftraege.First(a => a.quest == KC.U2);
            Assert.AreEqual(3, u2.kreise.Length);
            Assert.AreEqual(0, u2.kreise[0].ziel);
            Assert.Greater(Vector2.Distance(new Vector2(u2.kreise[0].x, u2.kreise[0].z), new Vector2(u2.kreise[1].x, u2.kreise[1].z)), 100f, "U-2: Oswald und Schlickwerfer getrennt");
            // Nach dem Erfüllen zurück: K-B (Ufer, 1,3 km vom Lager) hat einen Abgabekreis bei Wenna, G1 (Anselm steht am Ziel) keinen
            Assert.IsTrue(f.auftraege.First(a => a.quest == KC.KB).abgabe.radius > 0f, "K-B: Abgabekreis im Lager");
            Assert.IsTrue(f.auftraege.First(a => a.quest == GC.G1).abgabe == null || f.auftraege.First(a => a.quest == GC.G1).abgabe.radius == 0f, "G1: kein Abgabekreis (Anselm ist das Ziel)");
        }

        [Test]
        public void Kreismitte_versetzt_Ziel_bleibt_innen()
        {
            // Die Mitte liegt um ein Viertel des Radius neben dem Ziel (Richtung fest je Auftrag): Q3 Aussichtsfels −193/230, Radius 60
            FuehrungDatei f = KarethV4Fuehrung.Bauen(out _);
            FuehrungKreis k = f.auftraege.First(a => a.quest == "quest.view_over_the_hollow").kreise[0];
            Vector2 ziel = new Vector2(-193f, 230f) + Versatz;
            float d = Vector2.Distance(ziel, new Vector2(k.x, k.z));
            Assert.AreEqual(60f, k.radius);
            Assert.That(d, Is.EqualTo(15f).Within(1.5f), "Versatz r/4 (gerundet)");
            Assert.Less(d + 20f, k.radius, "Aussichtsfels (Radius 20) liegt ganz im Kreis");
            // Zweimal rechnen gibt dieselbe Richtung (feste Streuung, kein Zufall)
            FuehrungKreis k2 = KarethV4Fuehrung.Bauen(out _).auftraege.First(a => a.quest == "quest.view_over_the_hollow").kreise[0];
            Assert.AreEqual(k.x, k2.x); Assert.AreEqual(k.z, k2.z);
        }

        [Test]
        public void B_Unterzonen_aus_dem_Szenenraster_Nordmoor_im_Glockenmoor()
        {
            FuehrungDatei f = KarethV4Fuehrung.Bauen(out string warnung);
            Assert.AreEqual("", warnung);
            var soll = new Dictionary<string, int> { { KC.U3, 4 }, { KC.U5, 6 }, { GC.G6, 4 }, { GC.G11, 5 }, { GC.G12, 5 } };
            foreach (var p in soll)
            {
                FuehrungAuftrag a = f.auftraege.First(x => x.quest == p.Key);
                Assert.AreEqual(p.Value, a.unterzone, p.Key + ": Unterzone");
                Assert.IsTrue(a.kreise == null || a.kreise.Length == 0, p.Key + ": B hat keinen Kreis");
            }
            // Szenenraster: die drei Gegnergruppen des Nordmoors (J7) liegen im Glockenmoor, nicht am Salzsee-Ufer (Max 10.10.)
            ZonenDienst d = ZonenDienst.Laden(Path.GetFullPath(Ordner), out string fehler);
            Assert.IsNotNull(d, fehler);
            Assert.IsTrue(d.LadeSzene("kareth_v4", out fehler), fehler);
            foreach (var p in new[] { (-820f, 1250f), (-888f, 1166f), (-760f, 1180f) })
                Assert.AreEqual("Glockenmoor", d.UnterzoneBei(p.Item1 + Versatz.x, p.Item2 + Versatz.y)?.name, $"Nordmoor {p.Item1:0}/{p.Item2:0}");
            Assert.AreEqual("Salzsee-Ufer und Wrack", d.UnterzoneBei(-1265f + Versatz.x, -165f + Versatz.y)?.name, "Wrack bleibt am Salzsee-Ufer");
            Assert.AreEqual("Glockenmoor", d.UnterzoneBei(330f + Versatz.x, 1375f + Versatz.y)?.name, "Glockenrast bleibt im Glockenmoor");
        }

        [Test]
        public void Datei_geschrieben_und_lesbar()
        {
            string pfad = Path.Combine(Path.GetFullPath(Ordner), "szenen", KarethV4Fuehrung.DateiName);
            Assert.IsTrue(File.Exists(pfad), "Datei fehlt (Menü „Kareth v4: Auftragsführung schreiben“ oder Kareth-Bau): " + pfad);
            FuehrungDatei f = QuestFuehrung.Laden(Path.GetFullPath(Ordner), "kareth_v4", out string fehler);
            Assert.IsNotNull(f, fehler);
            Assert.AreEqual(KarethV4Fuehrung.Stand, f.stand, "Datei auf dem Stand der Tabelle (sonst neu schreiben)");
            FuehrungDatei neu = KarethV4Fuehrung.Bauen(out _);
            Assert.AreEqual(JsonUtility.ToJson(neu), JsonUtility.ToJson(f), "Datei entspricht der Tabelle");
        }

        [Test]
        public void C_Auftraege_sind_unmarkiert_und_jeder_Auftrag_hat_ein_Asset()
        {
            FuehrungDatei f = KarethV4Fuehrung.Bauen(out _);
            foreach (FuehrungAuftrag a in f.auftraege)
            {
                QuestDefinition q = Quest(a.quest);
                Assert.IsNotNull(q, a.quest + ": Auftrags-Asset (erst Kareth-Bau bzw. Sandbox-Bau)");
                Assert.AreEqual(a.stufe == "C", q.unmarked, a.quest + ": unmarkiert genau bei Stufe C (G7 und U-7, Max 10.10.)");
            }
        }
    }
}
