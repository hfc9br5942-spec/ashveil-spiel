using System.Collections.Generic;
using System.IO;
using Ashveil.Data;
using Ashveil.Maps;
using UnityEditor;
using UnityEngine;
using GC = Ashveil.Data.GlockenrastCatalog;
using KC = Ashveil.Data.KarethCatalog;
using RC = Ashveil.Data.RegisterCatalog;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Führung der 29 Kareth-Aufträge auf Karte und Minimap (Quest Design Bible §4; Liste A/B/C von Max 10.10.2026 freigegeben,
    /// `claude/ashveil-kareth-auftraege-fuehrung-abc-v1-2026-10-09.md`): Diese Tabelle ist die eine Stelle, an der steht, welcher
    /// Auftrag wie geführt wird. Geschrieben wird StreamingAssets/Ashveil/Karten/szenen/kareth_v4_fuehrung.json
    /// (<see cref="FuehrungDatei"/>), gelesen vom Client (<see cref="QuestFuehrung"/>). Der Server kennt die Datei nicht.
    ///
    /// Regeln (Vorschlag Claude im Papier, Abschnitt 3):
    ///   A  Kreis = Zielgebiet + 40 m Rand, mindestens 60 m (UI-202), auf 10 m gerundet. Die Mitte liegt um ein Viertel des
    ///      Radius neben dem Ziel, Richtung je Auftrag fest (aus der Kennung), damit „zur Mitte laufen“ nicht genau hinführt.
    ///      Je weit auseinanderliegendem Ziel ein Kreis (U-2). Sind alle Ziele erfüllt und steht die Abgabefigur weiter als
    ///      250 m vom letzten Ziel, ein Kreis (60 m) um sie.
    ///   B  nur die Unterzone (aus dem Szenenraster an der Zielstelle); kein Kreis.
    ///   C  nichts (G7 und U-7, Max 10.10.: Edo erzählt nur dem, der ihn anspricht).
    /// Lagen in Szenen-Metern (Kareth v4): Gegnergebiete aus <see cref="KarethV4Gegner"/>, Orte und Figuren aus KarethV4_Inhalte.json.
    /// </summary>
    public static class KarethV4Fuehrung
    {
        public const string DateiName = "kareth_v4_fuehrung.json";
        public const string Stand = "10.10.2026 (A/B/C freigegeben, G7 = C, G12 = B Glockenmoor, Kreise auch auf der Minimap; Nebenaufträge N01–N15 = A; 11.10.2026: Classic-Paket B1–B8, H1–H3 = A; 13.10.2026: Akt I „Das Register“ R1–R5 = A, R6 führt in die Westweiden)";
        private const float AbgabeAbstand = 250f, AbgabeRadius = 60f;

        private struct Ziel
        {
            public int Nr;                // Nummer des Ziels, −1 = alle
            public float X, Z, R;         // Zielgebiet (Szene): Mitte und Radius
            public Ziel(int nr, float x, float z, float r) { Nr = nr; X = x; Z = z; R = r; }
        }

        private struct Eintrag
        {
            public string Quest, Stufe, Abgabe;
            public Ziel[] Ziele;          // A: Zielgebiete; B: ein Ziel, dessen Unterzone gilt
        }

        private static Eintrag A(string quest, string abgabe, params Ziel[] ziele) => new Eintrag { Quest = quest, Stufe = "A", Abgabe = abgabe, Ziele = ziele };
        private static Eintrag B(string quest, float x, float z) => new Eintrag { Quest = quest, Stufe = "B", Ziele = new[] { new Ziel(-1, x, z, 0f) } };
        private static Eintrag C(string quest) => new Eintrag { Quest = quest, Stufe = "C" };

        // Abgabefiguren (Szene): Lagen aus KarethV4_Inhalte.json (Mittel der Tagesabläufe) – für den Kreis nach dem Erfüllen
        private static readonly Dictionary<string, Vector2> Figuren = new Dictionary<string, Vector2>
        {
            { "npc.ilse_harrow", new Vector2(6f, 57f) }, { "npc.maelis", new Vector2(6f, 7f) }, { "npc.oskar_bralt", new Vector2(0f, -50f) },
            { "npc.gerrit", new Vector2(4f, 8f) }, { "npc.cartographer", new Vector2(0f, 7f) }, { KC.Wenna, new Vector2(-25f, -29f) },
            { KC.Jorn, new Vector2(-24f, -23f) }, { KC.Ulf, new Vector2(44f, -9f) }, { KC.Veit, new Vector2(-5f, 5f) },
            { KC.Oswald, new Vector2(-1476f, 509f) }, { KC.Tamo, new Vector2(718f, 275f) }, { KC.Ebba, new Vector2(706f, 296f) },
            { GC.Anselm, new Vector2(340f, 1383f) }, { GC.Runa, new Vector2(340f, 1382f) }, { GC.Merle, new Vector2(-794f, -606f) },
            { GC.Jasko, new Vector2(-900f, 1230f) }, { GC.Pilgrim, new Vector2(713f, 1005f) },
            { "npc.dorn", new Vector2(-18f, -5f) }, { KC.Grete, new Vector2(651f, 633f) }, { KC.Hennek, new Vector2(680f, 297f) },
            // Akt I „Das Register“ (13.10.2026): Nessa im Lazarett, Mutter Ansa am Herd der Schale
            { RC.Nessa, new Vector2(-29f, 15f) }, { RC.Ansa, new Vector2(703f, 298f) }
        };

        /// <summary>Die freigegebene Liste (22 × A, 5 × B, 2 × C) plus 15 Nebenaufträge (A) und das Classic-Paket (11 × A, 11.10.2026).</summary>
        private static readonly Eintrag[] Liste =
        {
            // 2.1 Wachtfeuer-Mulde
            A("quest.blade_for_the_watch", "npc.ilse_harrow", new Ziel(-1, 4f, 44f, 40f)),                // Q1: Ilse vor dem Nordtor (WORLD-240); nachts am Feuer – der Kreis deckt beides
            A("quest.tough_hides", "npc.maelis", new Ziel(-1, -261f, 224f, 130f)),                       // Q2: beide Rudel (NW-Wäldchen, Westsattel)
            A("quest.view_over_the_hollow", "npc.cartographer", new Ziel(-1, -193f, 230f, 20f)),         // Q3: Aussichtsfels am Muldenrand
            A("quest.the_faded", "npc.oskar_bralt", new Ziel(-1, 622f, -282f, 20f)),                     // Q4: Gehöft am Bergfuß
            A("quest.powder_for_the_watch", "npc.gerrit", new Ziel(-1, 622f, -282f, 20f)),               // Q5: Federn der Schützinnen am Gehöft
            A(KC.KA, KC.Wenna, new Ziel(-1, 204f, -204f, 30f)),                                          // K-A Vorratswiesen
            A(KC.KB, KC.Wenna, new Ziel(-1, -1343f, -25f, 70f)),                                         // K-B Mündung und Ufer
            A(KC.KC, KC.Jorn, new Ziel(-1, -235f, 38f, 40f)),                                            // K-C Waldrand vor dem Westtor
            A(KC.KD, KC.Ulf, new Ziel(-1, -504f, -674f, 60f)),                                           // K-D Kiefernschlag (Holzplatz und Wölfe)
            // 2.2 Untersteg, Ufer, Kiefernhang, Aschenfeld, Wüstung
            A(KC.U1, KC.Tamo, new Ziel(-1, -1023f, -990f, 50f)),                                         // U-1 Nest und Wagen
            A(KC.U2, KC.Ebba, new Ziel(0, -1473f, 508f, 20f), new Ziel(1, -1355f, 365f, 50f), new Ziel(2, -1355f, 365f, 50f)),   // U-2 erst Oswald, dann die Schlickwerfer (Krug fällt dort)
            B(KC.U3, -1265f, -165f),                                                                     // U-3 Wrack: Spurensuche (Mast über der Salzkruste)
            A(KC.U4, KC.Veit, new Ziel(-1, 1427f, -368f, 40f)),                                          // U-4 Werkstätten
            B(KC.U5, 1310f, -340f),                                                                      // U-5 Zeichensteine: Hinweise im Text
            A(KC.U6, "npc.oskar_bralt", new Ziel(-1, -1451f, -1553f, 90f)),                              // U-6 beide Höfe
            C(KC.U7),                                                                                    // U-7 Begegnung
            // 2.3 Glockenmoor und die Kette „Glockenrast“
            A(GC.G1, GC.Anselm, new Ziel(-1, 340f, 1382f, 30f)),                                         // G1 Glockenrast
            A(GC.G2, GC.Runa, new Ziel(-1, -219f, 891f, 140f)),                                          // G2 Moorrand und Speiergrund
            A(GC.G3, "npc.maelis", new Ziel(-1, -219f, 891f, 150f)),                                     // G3 Giftdrüsen der Nadelspeier → Maelis im Lager
            A(GC.G4, GC.Tamo, new Ziel(-1, -785f, -646f, 40f)),                                          // G4 Ersatzrad im Holzfällerlager
            A(GC.G5, GC.Merle, new Ziel(-1, -785f, -646f, 40f)),                                         // G5 Verblichene im Holzfällerlager
            B(GC.G6, -1069f, -375f),                                                                     // G6 Harzerlager: Spurensuche
            C(GC.G7),                                                                                    // G7 Der älteste Herd (Max 10.10.: unmarkiert)
            A(GC.G8, GC.Anselm, new Ziel(-1, 262f, 1012f, 30f)),                                         // G8 Versunkene Kuppeln
            A(GC.G9, GC.Runa, new Ziel(-1, 292f, 1076f, 20f)),                                           // G9 Aschenweide
            A(GC.G10, GC.Runa, new Ziel(-1, 340f, 1382f, 30f)),                                          // G10 Gespräche in der Glockenrast
            B(GC.G11, 165f, 1595f),                                                                      // G11 Schweigender Turm: Erkundung
            B(GC.G12, -820f, 1250f),                                                                     // G12 Nordmoor: Suche (Unterzone Glockenmoor seit 10.10.)
            A(GC.G13, GC.Merle, new Ziel(-1, -785f, -646f, 40f)),                                        // G13 Merle im Holzfällerlager
            // 2.4 Nebenaufträge N01–N15 (Max 10.10.2026 spät): alle A – Kreis um das Gegnergebiet, in dem Ziel und Beute zu finden sind
            A(KC.N01, KC.Grete, new Ziel(-1, 785f, 0f, 120f)),                                           // N01 Feldmark Süd (beide Wühler-Lager)
            A(KC.N02, "npc.gerrit", new Ziel(-1, -261f, 224f, 130f)),                                    // N02 Wolfszähne: Wäldchen und Westsattel
            A(KC.N03, KC.Hennek, new Ziel(-1, 728f, 495f, 40f)),                                         // N03 Muldenbach-Rinne
            A(KC.N04, "npc.maelis", new Ziel(-1, 622f, -282f, 20f)),                                     // N04 Leinen: Gehöft am Bergfuß
            A(KC.N05, "npc.oskar_bralt", new Ziel(-1, 630f, -284f, 20f)),                                // N05 Schützinnen am Gehöft
            A(KC.N06, "npc.gerrit", new Ziel(-1, -219f, 891f, 140f)),                                    // N06 Speiernadeln: Speiergrund und Moorrand
            A(KC.N07, KC.Ebba, new Ziel(-1, 255f, 1015f, 80f)),                                          // N07 Torf: Kuppeln im Glockenmoor
            A(KC.N08, KC.Hennek, new Ziel(-1, 915f, 775f, 60f)),                                         // N08 Heuwiesen
            A(KC.N09, KC.Oswald, new Ziel(-1, -1355f, 365f, 50f)),                                       // N09 Salzufer am Damm
            A(KC.N10, "npc.dorn", new Ziel(-1, -219f, 891f, 140f)),                                      // N10 Chitin: Speiergrund und Moorrand
            A(KC.N11, KC.Grete, new Ziel(-1, 744f, 666f, 20f)),                                          // N11 Deserteure über der Mühle
            A(KC.N12, KC.Veit, new Ziel(-1, 1427f, -368f, 40f)),                                         // N12 Grabsalz: Werkstätten
            A(KC.N13, "npc.oskar_bralt", new Ziel(-1, -840f, 1205f, 70f)),                               // N13 Nordmoor
            A(KC.N14, "npc.maelis", new Ziel(-1, -800f, 1190f, 70f)),                                    // N14 Breitkopfspeier im Nordmoor
            A(KC.N15, "npc.dorn", new Ziel(-1, 1427f, -368f, 40f)),                                      // N15 Altmetall: Werkstätten
            // 2.5 Classic-Paket (Max 11.10.2026 abends): Botengänge – Kreis um die Abgabefigur; „Kopf von …“ – ein Kreis je Versteck
            A(KC.B1, KC.Hennek, new Ziel(-1, 680f, 297f, 20f)),                                          // B1 Bralt → Hennek an der Brücke
            A(KC.B2, KC.Ebba, new Ziel(-1, 706f, 296f, 20f)),                                            // B2 Hennek → Ebba in der Schale
            A(KC.B3, KC.Tamo, new Ziel(-1, 718f, 275f, 20f)),                                            // B3 Ebba → Tamo am Karrenhof
            A(KC.B4, GC.Runa, new Ziel(-1, 340f, 1382f, 30f)),                                           // B4 Kartograf → Runa in der Glockenrast
            A(KC.B5, KC.Jorn, new Ziel(-1, -24f, -23f, 20f)),                                            // B5 Ulf → Jorn im Lager
            A(KC.B6, KC.Oswald, new Ziel(-1, -1476f, 509f, 20f)),                                        // B6 Grete → Oswald am Dammbruch
            A(KC.B7, "npc.cartographer", new Ziel(-1, 0f, 7f, 20f)),                                     // B7 Veit → Kartograf im Lager
            A(KC.B8, KC.Grete, new Ziel(-1, 651f, 633f, 20f)),                                           // B8 Maelis → Grete an der Mühle
            A(KC.H1, KC.Wenna, new Ziel(-1, 184.7f, -29.0f, 40f), new Ziel(-1, 495.5f, -252.2f, 40f), new Ziel(-1, 410.8f, -201.1f, 40f)),   // H1 Altborste: drei Verstecke
            A(KC.H2, KC.Hennek, new Ziel(-1, 1085.4f, 621.4f, 40f), new Ziel(-1, 1086.6f, 785.0f, 40f), new Ziel(-1, 953.7f, 513.2f, 40f)),   // H2 Narbenfang
            A(KC.H3, "npc.gerrit", new Ziel(-1, -790.2f, 952.4f, 40f), new Ziel(-1, -679.6f, 1390.0f, 40f), new Ziel(-1, -458.7f, 1048.2f, 40f)),   // H3 Krummnadel
            // 2.6 Hauptquest der Menschen, Akt I „Das Register“ (13.10.2026): alle A – Kreis um Fundstelle bzw. Abgabefigur
            A(RC.R1, KC.Veit, new Ziel(-1, -7.5f, 37.5f, 10f)),                                          // R1 Zählbrett und Register am Nordtor
            A(RC.R2, "npc.oskar_bralt", new Ziel(-1, 2.5f, -58f, 10f)),                                  // R2 Halle: Regal, dann Bralt am Kartentisch
            A(RC.R3, RC.Ansa, new Ziel(-1, 703f, 298f, 10f)),                                            // R3 Mutter Ansa am Herd der Schale
            A(RC.R4, RC.Nessa, new Ziel(-1, 2.5f, -76f, 10f)),                                           // R4 Salzkeller unter der Halle
            A(RC.R5, "npc.oskar_bralt", new Ziel(-1, 2f, -50f, 10f)),                                    // R5 Bralt vor der Halle
            // R6 (Bralt → Fenna am Kornwagen) führt in die Westweiden – außerhalb dieser Karte; die Westweiden haben noch keine Führung
            // (Lückenliste Punkt 4, Karten für mehrere Regionen). Der Weg steht im Auftragstext: Weidensteig, Klamm.
        };

        public static string Pfad => Path.Combine(Application.streamingAssetsPath, "Ashveil", "Karten", "szenen", DateiName);

        [MenuItem("Ashveil/Welt/Kareth v4: Auftragsführung schreiben (A/B/C)", priority = 568)]
        private static void Menu()
        {
            string bericht = Schreiben();
            Debug.Log("[Ashveil] Kareth v4: " + bericht);
            AssetDatabase.Refresh();
        }

        /// <summary>Datei bauen und schreiben; gibt eine Zusammenfassung zurück.</summary>
        public static string Schreiben()
        {
            FuehrungDatei f = Bauen(out string warnung);
            Directory.CreateDirectory(Path.GetDirectoryName(Pfad));
            File.WriteAllText(Pfad, JsonUtility.ToJson(f, true).Replace("\r\n", "\n"));
            int a = 0, b = 0, c = 0;
            foreach (FuehrungAuftrag e in f.auftraege) { if (e.stufe == "A") a++; else if (e.stufe == "B") b++; else c++; }
            return $"Auftragsführung {DateiName}: {a} × A, {b} × B, {c} × C" + (warnung.Length > 0 ? " – " + warnung : "");
        }

        /// <summary>Die Datei aus der Tabelle rechnen (ohne zu schreiben). Unterzonen der B-Aufträge aus dem Szenenraster.</summary>
        public static FuehrungDatei Bauen(out string warnung)
        {
            var warn = new List<string>();
            ZonenDienst dienst = ZonenDienst.Laden(Path.Combine(Application.streamingAssetsPath, "Ashveil", "Karten"), out string fehler);
            if (dienst == null || !dienst.LadeSzene(KarethV4Spiel.KartenSzenenRaster, out fehler)) { warn.Add(fehler); dienst = null; }
            Vector2 v = KarethV4Spiel.KartenWeltversatz;
            var auftraege = new List<FuehrungAuftrag>();
            foreach (Eintrag e in Liste)
            {
                var a = new FuehrungAuftrag { quest = e.Quest, stufe = e.Stufe, kreise = new FuehrungKreis[0] };
                if (e.Stufe == "A")
                {
                    var kreise = new List<FuehrungKreis>();
                    Vector2 letztes = Vector2.zero;
                    foreach (Ziel z in e.Ziele)
                    {
                        kreise.Add(Kreis(e.Quest, z.Nr, z.X, z.Z, z.R, v));
                        letztes = new Vector2(z.X, z.Z);
                    }
                    a.kreise = kreise.ToArray();
                    if (!string.IsNullOrEmpty(e.Abgabe))
                    {
                        if (!Figuren.TryGetValue(e.Abgabe, out Vector2 fig)) warn.Add($"{e.Quest}: Abgabefigur {e.Abgabe} ohne Lage");
                        else if (Vector2.Distance(fig, letztes) > AbgabeAbstand)
                            a.abgabe = new FuehrungKreis { ziel = -1, x = Mathf.Round(fig.x + v.x), z = Mathf.Round(fig.y + v.y), radius = AbgabeRadius };
                    }
                }
                else if (e.Stufe == "B")
                {
                    Ziel z = e.Ziele[0];
                    UnterzoneDaten u = dienst?.UnterzoneBei(z.X + v.x, z.Z + v.y);
                    if (u == null) warn.Add($"{e.Quest}: keine Unterzone bei {z.X:0}/{z.Z:0}");
                    else a.unterzone = u.id;
                }
                auftraege.Add(a);
            }
            warnung = string.Join("; ", warn);
            return new FuehrungDatei { version = 1, stand = Stand, szene = KarethV4Spiel.KartenSzenenRaster, auftraege = auftraege.ToArray() };
        }

        /// <summary>Kreis um ein Zielgebiet: Radius = Ziel + 40 m, mindestens 60 m, auf 10 m gerundet; Mitte um r/4 versetzt (Richtung aus der Kennung).</summary>
        internal static FuehrungKreis Kreis(string quest, int nr, float x, float z, float r, Vector2 v)
        {
            float radius = Mathf.Max(60f, Mathf.Ceil((r + 40f) / 10f) * 10f);
            float winkel = (Hash(quest + "#" + nr) % 360u) * Mathf.Deg2Rad;
            float d = radius * 0.25f;
            return new FuehrungKreis
            {
                ziel = nr,
                x = Mathf.Round(x + Mathf.Cos(winkel) * d + v.x),
                z = Mathf.Round(z + Mathf.Sin(winkel) * d + v.y),
                radius = radius
            };
        }

        /// <summary>FNV-1a, damit die Richtung je Auftrag fest bleibt (nicht String.GetHashCode: das wechselt je Lauf).</summary>
        private static uint Hash(string s)
        {
            uint h = 2166136261u;
            foreach (char c in s) { h ^= c; h *= 16777619u; }
            return h;
        }
    }
}
