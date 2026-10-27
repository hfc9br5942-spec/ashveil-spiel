using System.IO;
using Ashveil.Data;
using Ashveil.Maps;
using NUnit.Framework;
using UnityEngine;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// #100 Karten r2 (08.10.2026): Kartendaten unter StreamingAssets/Ashveil/Karten, Szenenraster Kareth v4 (Unterzonen wie gebaut),
    /// Schlüssel im Speicherstand, Entdeckungs-EP (PROG-013), Liste der Entdeckungen am Client und der Pergament-Schleier (UI-203).
    /// Seit 09.10.2026 (CORE-005): Weltschleier-Gebiete und Schleier auf einem Hilfsfaden, nebenbei geladene Bilder im Bildspeicher.
    /// </summary>
    public class KartenTests
    {
        private const string Ordner = "Assets/StreamingAssets/Ashveil/Karten";
        private static readonly Vector2 Versatz = new Vector2(8800f, 17270f);

        private static ZonenDienst Laden()
        {
            ZonenDienst d = ZonenDienst.Laden(Path.GetFullPath(Ordner), out string fehler);
            Assert.IsNotNull(d, fehler);
            Assert.IsTrue(d.LadeSzene("kareth_v4", out fehler), fehler);
            return d;
        }

        [Test]
        public void Daten_GebieteUndSzenenraster()
        {
            ZonenDienst d = Laden();
            Assert.AreEqual(45, d.Daten.zonen.Length);
            Assert.AreEqual("kareth", d.SzenenZone.key);
            Assert.AreEqual(512, d.Szene.breite);
            Assert.AreEqual(8f, d.Szene.zelle_m);
            Assert.AreEqual(Versatz.x, d.Szene.weltversatz_x);
            Assert.AreEqual(Versatz.y, d.Szene.weltversatz_z);
            Assert.AreEqual(1, d.Szene.start_unterzone);
            Assert.IsFalse(d.HatWeltUnterzonen, "r2: Unterzonen nur im Szenenraster");
        }

        [TestCase(0f, 0f, 1, "Wachtfeuer-Mulde")]
        [TestCase(715f, 275f, 2, "Untersteg und Feldmark")]
        [TestCase(-785f, -646f, 3, "Kiefernhang")]
        [TestCase(-1155f, 275f, 4, "Salzsee-Ufer und Wrack")]
        [TestCase(330f, 1375f, 5, "Glockenmoor")]
        [TestCase(1536f, 386f, 6, "Aschenfeld")]
        [TestCase(-1430f, -1540f, 7, "Wüstungshöfe und Südkiefern")]
        [TestCase(770f, 1595f, 8, "Nördlicher Aufstieg")]
        public void Unterzonen_AnDenOrtenWieGebaut(float sx, float sz, int id, string name)
        {
            ZonenDienst d = Laden();
            UnterzoneDaten u = d.UnterzoneBei(sx + Versatz.x, sz + Versatz.y);
            Assert.IsNotNull(u);
            Assert.AreEqual(id, u.id);
            Assert.AreEqual(name, u.name);
            Assert.AreEqual(id, d.AufdeckungBei(sx + Versatz.x, sz + Versatz.y));
        }

        [Test]
        public void Gebiet_ImGanzenSzenenrasterKareth()
        {
            ZonenDienst d = Laden();
            float x0 = d.Szene.x0, z0 = d.Szene.z0, b = d.Szene.breite * d.Szene.zelle_m;
            foreach (Vector2 p in new[] { new Vector2(x0 + 1f, z0 + 1f), new Vector2(x0 + b - 1f, z0 + 1f), new Vector2(x0 + 1f, z0 + b - 1f), new Vector2(x0 + b - 1f, z0 + b - 1f) })
            {
                Assert.IsTrue(d.InSzene(p.x, p.y));
                Assert.AreEqual("kareth", d.ZoneBei(p.x, p.y).key);
                Assert.IsNotNull(d.UnterzoneBei(p.x, p.y));
            }
            Assert.IsFalse(d.InSzene(x0 - 10f, z0 + 100f));
            Assert.IsNull(d.UnterzoneBei(x0 - 10f, z0 + 100f), "außerhalb der Szene keine Unterzonen (kein Weltraster in r2)");
            Assert.IsNull(d.ZoneBei(5f, 5f), "Südwestecke der Welt: offene See");
        }

        [Test]
        public void Namenspunkte_LiegenInIhrerFlaeche()
        {
            ZonenDienst d = Laden();
            foreach (UnterzoneDaten u in d.SzenenZone.unterzonen)
            {
                Vector2 p = d.Namenspunkt(u);
                Assert.AreEqual(u.id, d.UnterzoneBei(p.x, p.y)?.id, u.name);
            }
        }

        [Test]
        public void Schluessel_HinUndZurueck()
        {
            ZonenDienst d = Laden();
            UnterzoneDaten u = d.UnterzoneNachId(2);
            Assert.AreEqual("uz.kareth.2", ZonenDienst.Schluessel(d.ZoneDerUnterzone(2), u));
            Assert.AreSame(u, d.AusSchluessel("uz.kareth.2"));
            Assert.IsNull(d.AusSchluessel("uz.salzsee.2"), "falsches Gebiet");
            Assert.IsNull(d.AusSchluessel("uz.kareth.x"));
            Assert.IsNull(d.AusSchluessel("uz.kareth.99999"));
            Assert.IsNull(d.AusSchluessel("site.kareth.hub.wachtfeuer"), "Quest-Orte bleiben eigene Schlüssel");
            Assert.IsNull(d.AusSchluessel(null));
        }

        [Test]
        public void Pvp_HeimatFreundlichOffeneSeeNeutral()
        {
            ZonenDienst d = Laden();
            Assert.AreEqual(PvpArt.Freundlich, ZonenDienst.PvpFuer(d.SzenenZone, "A"));
            Assert.AreEqual(PvpArt.Feindlich, ZonenDienst.PvpFuer(d.SzenenZone, "B"));
            Assert.AreEqual(PvpArt.Neutral, ZonenDienst.PvpFuer(null, "A"), "Offene See: neutral, kein PvP (WORLD-227)");
        }

        [Test]
        public void EntdeckungsEp_JeUnterzone()
        {
            // 20 + 5 × niedrigste Stufe der Unterzone (EP-Bilanz Kareth, wie die Quest-Orte)
            ZonenDienst d = Laden();
            Assert.AreEqual(40, Progression.DiscoveryXp(d.UnterzoneNachId(2).stufe_von));
            Assert.AreEqual(70, Progression.DiscoveryXp(d.UnterzoneNachId(8).stufe_von));
            int summe = 0;
            foreach (UnterzoneDaten u in d.SzenenZone.unterzonen) if (u.id != d.Szene.start_unterzone) summe += Progression.DiscoveryXp(u.stufe_von);
            Assert.AreEqual(345, summe, "sieben Unterzonen außer der Startmulde");
        }

        [Test]
        public void Minimap_KachelnDeckenDieSzene()
        {
            ZonenDienst d = Laden();
            float T = d.Daten.minimap_kachel_m;
            float x0 = d.Szene.x0, z0 = d.Szene.z0, b = d.Szene.breite * d.Szene.zelle_m;
            for (float z = z0 + 5f; z < z0 + b; z += 250f)
                for (float x = x0 + 5f; x < x0 + b; x += 250f)
                    Assert.IsTrue(d.HatMinimapKachel(Mathf.FloorToInt(x / T), Mathf.FloorToInt(z / T)), $"Kachel für {x:0}/{z:0}");
            Assert.AreEqual(46, d.Daten.minimap_kacheln_ij.Length / 2, "25 Kareth + 21 Westweiden-Zelle (13.10.2026)");
        }

        [Test]
        public void Westweiden_WeiteresSzenenraster()
        {
            // Lückenliste Punkt 4 (13.10.2026): die Zelle hat ein eigenes Raster (Weltkoordinaten), Kareth bleibt unverändert
            ZonenDienst d = Laden();
            Assert.IsTrue(d.LadeWeitereSzene("westweiden_v2", out string fehler), fehler);
            var weitere = new System.Collections.Generic.List<SzenenRasterDatei>(d.WeitereSzenen);
            Assert.AreEqual(1, weitere.Count);
            Assert.AreEqual("westweiden", d.ZoneNachKey(weitere[0].zone_key)?.key);
            Assert.AreEqual(20, d.UnterzoneBei(4664f, 15612f)?.id, "Langer Rain liegt im Mühlenrain");
            Assert.AreEqual("westweiden", d.ZoneBei(4664f, 15612f)?.key);
            Assert.AreEqual(1, d.UnterzoneBei(Versatz.x, Versatz.y)?.id, "Wachtfeuer bleibt Wachtfeuer-Mulde");
            Assert.AreEqual("kareth", d.ZoneBei(Versatz.x, Versatz.y)?.key);
            float T = d.Daten.minimap_kachel_m;
            ZoneDaten ww = d.ZoneNachKey("westweiden");
            for (float z = ww.rahmen[1] + 5f; z < ww.rahmen[3]; z += 250f)
                for (float x = ww.rahmen[0] + 5f; x < ww.rahmen[2]; x += 250f)
                    Assert.IsTrue(d.HatMinimapKachel(Mathf.FloorToInt(x / T), Mathf.FloorToInt(z / T)), $"Westweiden-Kachel für {x:0}/{z:0}");
        }

        [Test]
        public void Westweiden_AuftragsfuehrungAusDerZelle()
        {
            // geschrieben beim Bau der Zelle (WestweidenFuehrung): alle Aufträge mit Kennzeichen „westweiden“, Kreise im Gebietsrahmen
            string pfad = Path.Combine(Path.GetFullPath(Ordner), "szenen", "westweiden_v2_fuehrung.json");
            Assert.IsTrue(File.Exists(pfad), "westweiden_v2_fuehrung.json fehlt – „westweiden_zelle“ bauen");
            FuehrungDatei f = JsonUtility.FromJson<FuehrungDatei>(File.ReadAllText(pfad));
            Assert.AreEqual("westweiden_v2", f.szene);
            Assert.GreaterOrEqual(f.auftraege.Length, 29, "W01–W28 und R6");
            ZoneDaten ww = Laden().ZoneNachKey("westweiden");
            foreach (FuehrungAuftrag a in f.auftraege)
            {
                if (a.stufe != "A") continue;
                Assert.Greater(a.kreise.Length, 0, $"{a.quest}: ohne Kreis");
                foreach (FuehrungKreis k in a.kreise)
                    Assert.IsTrue(k.x >= ww.rahmen[0] && k.x <= ww.rahmen[2] && k.z >= ww.rahmen[1] && k.z <= ww.rahmen[3] && k.radius >= 60f,
                        $"{a.quest}: Kreis {k.x:0}/{k.z:0} r {k.radius:0} außerhalb der Westweiden");
            }
        }

        [Test]
        public void Entdeckungen_MeldenUndLeeren()
        {
            KartenEntdeckungen.Leeren();
            KartenEntdeckungen.Melden(new[] { 1, 2 }, true, 0, "");
            Assert.AreEqual(2, KartenEntdeckungen.Anzahl);
            int rev = KartenEntdeckungen.Revision;
            KartenEntdeckungen.Melden(new[] { 3 }, false, 40, "Kiefernhang");
            Assert.AreEqual(3, KartenEntdeckungen.Anzahl);
            Assert.Greater(KartenEntdeckungen.Revision, rev);
            Assert.AreEqual("Kiefernhang+40", KartenEntdeckungen.LetzteMeldung);
            KartenEntdeckungen.Melden(new[] { 5 }, true, 0, "");
            Assert.AreEqual(1, KartenEntdeckungen.Anzahl, "volle Liste ersetzt");
            Assert.IsTrue(KartenEntdeckungen.Enthaelt(5));
            KartenEntdeckungen.Leeren();
            Assert.AreEqual(0, KartenEntdeckungen.Anzahl);
        }

        [Test]
        public void Schleier_VerhuelltWeichUndLaesstFreiesFrei()
        {
            const int w = 40, h = 10;
            var deckung = new float[w * h];
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                    deckung[j * w + i] = i < 20 ? 1f : 0f;
            var px = new Color32[w * h];
            KartenGrafik.Schleier(deckung, w, h, px, 2, 1f);
            Assert.Greater(px[5 * w + 2].a, 200, "verhüllt");
            Assert.AreEqual(0, px[5 * w + 37].a, "frei bleibt frei");
            byte rand = px[5 * w + 20].a;
            Assert.IsTrue(rand > 0 && rand < px[5 * w + 2].a, "weicher Übergang an der Grenze");
        }

        [Test]
        public void Weltschleier_GebieteNebenbeiWieImHauptfaden()
        {
            // CORE-005 (09.10.2026): Die Gebiete des Weltschleiers rechnet die Karte nebenbei auf einem Hilfsfaden.
            ZonenDienst d = Laden();
            KartenDatei k = d.Daten;
            const int b = 400;
            int h = Mathf.Max(8, Mathf.RoundToInt(b * k.welt_hoehe_m / Mathf.Max(k.welt_breite_m, 1f)));
            int[] hier = KartenFensterUi.WeltGebieteRechnen(d, k, b, h);
            int[] nebenbei = System.Threading.Tasks.Task.Run(() => KartenFensterUi.WeltGebieteRechnen(d, k, b, h)).Result;
            Assert.AreEqual(b * h, hier.Length);
            CollectionAssert.AreEqual(hier, nebenbei, "gleiches Ergebnis auf dem Hilfsfaden");
            float dx = k.welt_breite_m / b, dz = k.welt_hoehe_m / h;
            int land = 0, saum = 0, see = 0;
            for (int j = 0; j < h; j++)
                for (int i = 0; i < b; i++)
                {
                    int g = d.WeltZonenIndexBei((i + 0.5f) * dx, (j + 0.5f) * dz);
                    int p = j * b + i;
                    if (g > 0) { Assert.AreEqual(g, hier[p], "Land behält sein Gebiet"); land++; }
                    else if (hier[p] > 0) saum++;
                    else see++;
                }
            Assert.Greater(land, 0);
            Assert.Greater(saum, 0, "die See im Küstensaum gehört zum nächsten Land");
            Assert.Greater(see, 0, "offene See weiter draußen bleibt frei");
        }

        [Test]
        public void Schleier_NebenbeiWieImHauptfaden()
        {
            // CORE-005 (09.10.2026): Gebiets- und Weltschleier rechnet die Karte nebenbei auf einem Hilfsfaden, solange sie zu ist.
            ZonenDienst d = Laden();
            ZoneDaten kareth = d.SzenenZone;
            var entdeckt = new System.Collections.Generic.HashSet<int> { 1, 2 };
            KartenFensterUi.SchleierBild hier = KartenFensterUi.GebietSchleierRechnen(d, kareth, kareth.rahmen, entdeckt);
            KartenFensterUi.SchleierBild nebenbei = System.Threading.Tasks.Task.Run(() => KartenFensterUi.GebietSchleierRechnen(d, kareth, kareth.rahmen, entdeckt)).Result;
            Assert.AreEqual(6, hier.Verhuellt, "Kareth: acht Unterzonen, zwei entdeckt");
            Assert.AreEqual(hier.Breite * hier.Hoehe, hier.Pixel.Length);
            CollectionAssert.AreEqual(hier.Pixel, nebenbei.Pixel, "Gebietsschleier gleich auf dem Hilfsfaden");

            KartenDatei k = d.Daten;
            const int b = 400;
            int h = Mathf.Max(8, Mathf.RoundToInt(b * k.welt_hoehe_m / Mathf.Max(k.welt_breite_m, 1f)));
            int[] gebiet = KartenFensterUi.WeltGebieteRechnen(d, k, b, h);
            var bekannt = new bool[256];
            bekannt[kareth.index] = true;
            Color32[] w1 = KartenFensterUi.WeltSchleierRechnen(gebiet, bekannt, b, h);
            Color32[] w2 = System.Threading.Tasks.Task.Run(() => KartenFensterUi.WeltSchleierRechnen(gebiet, bekannt, b, h)).Result;
            CollectionAssert.AreEqual(w1, w2, "Weltschleier gleich auf dem Hilfsfaden");
            int frei = 0;
            for (int i = 0; i < gebiet.Length; i++) if (gebiet[i] == kareth.index && w1[i].a < 40) frei++;
            Assert.Greater(frei, 0, "Kareth ist auf der Weltkarte frei");
        }

        [Test]
        public void Banner_PvpOhneDoppelteKlammern()
        {
            Assert.AreEqual("Zuflucht, kein PvP", ZonenBannerUi.BannerText(PvpArt.Zuflucht));
            Assert.AreEqual("Neutral, kein PvP", ZonenBannerUi.BannerText(PvpArt.Neutral));
            Assert.AreEqual("Freundliches Gebiet", ZonenBannerUi.BannerText(PvpArt.Freundlich));
            Assert.AreEqual("Umkämpftes Gebiet", ZonenBannerUi.BannerText(PvpArt.Umkaempft));
        }

        [Test]
        public void Bildspeicher_NebenbeiGeladenesAblegen()
        {
            var s = new BildSpeicher(2);
            var tex = new Texture2D(4, 4);
            try
            {
                s.Ablegen("a.jpg", null);
                Assert.IsFalse(s.Hat("a.jpg"), "gescheitert: nicht merken, dann lädt Holen es später selbst");
                s.Ablegen("a.jpg", tex);
                Assert.IsTrue(s.Hat("a.jpg"));
                Assert.AreSame(tex, s.Holen("a.jpg"));
                Assert.IsFalse(s.Hat("b.jpg"));
            }
            finally { Object.DestroyImmediate(tex); }
        }
    }
}
