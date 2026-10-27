using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Westweiden – Orte als Greybox ausformen (Entwurf v1, Claude 12.10.2026; Gebietsblatt v2 §3/§4, alles VORSCHLAG).
    /// Baukörper mit Satteldach in echten Maßen, Plätze, Brücke, Zäune, Koppeln – noch keine Modelle, keine Figuren.
    /// Wände tragen das Trim-Sheet der Westweiden (Bruchstein/Kalk/Eiche), Dächer Greybox-Farbe (Stroh/Reet kommt als eigene Familie).
    /// Langer Rain, Hallgrund, Dreispannhof, Heckenhof, Hirtenlager, Bürgerwehr-Posten, Kornwagen, Alte Zollhütte. Nur für die Szene Westweiden_Entwurf.
    /// </summary>
    public static class WestweidenOrte
    {
        private struct Bau
        {
            public string Name; public float X, Z, Yaw, B, T, H, Dach; public bool Turm, Ruine;
            public Bau(string n, float x, float z, float yaw, float b, float t, float h, float dach = 0.55f, bool turm = false, bool ruine = false)
            { Name = n; X = x; Z = z; Yaw = yaw; B = b; T = t; H = h; Dach = dach; Turm = turm; Ruine = ruine; }
        }

        // Langer Rain: Mitte (2300/1500) = Mühlplatz; Hofstraße läuft West–Ost durch, Vorspannrampe nach Süden über den Mühlbach.
        // Koordinaten relativ zur Ortsmitte, Meter; Yaw = Firstrichtung.
        private static readonly Bau[] LangerRain =
        {
            new Bau("Gasthaus Zum Mühlstein", 30f, 28f, 90f, 18f, 11f, 6.5f),   // 12.10. Weg-Prüfung: stand auf dem Weg
            new Bau("Bürgerwehr-Haus", 38f, 10f, 0f, 12f, 9f, 5.5f),   // 12.10. Weg-Prüfung: stand auf dem Weg
            new Bau("Wachturm Bürgerwehr", 49f, 19f, 0f, 4.5f, 4.5f, 11f, 0.4f, true),
            new Bau("Händler und Post", -20f, 18f, 90f, 11f, 8f, 5f),
            new Bau("Bäckerei", 18f, 19f, 90f, 9f, 8f, 5f),
            new Bau("Schmiede", -46f, -6f, 0f, 10f, 8f, 4.5f),
            new Bau("Sattlerei", -52f, -15f, 0f, 9f, 7f, 4.5f),   // 12.10. Weg-Prüfung: stand auf dem Weg
            new Bau("Stall und Karrenhalt", 30f, -30f, 0f, 20f, 9f, 5f),
            new Bau("Lehrerhaus", -30f, 40f, 90f, 10f, 8f, 5.5f),
            new Bau("Kräuterhaus", 22f, 46f, 70f, 8f, 7f, 4.5f),
            new Bau("Fischerhütte am Mühlbach", 30f, -50f, 80f, 7f, 6f, 4f),       // 12.10.: Westufer des Bachs (Mühlbach v4)
            new Bau("Wohnhaus 1", -69f, 8f, 0f, 10f, 8f, 5f),   // 12.10. Weg-Prüfung: stand auf dem Weg
            new Bau("Wohnhaus 2", -72f, -16f, 10f, 9f, 8f, 5f),
            new Bau("Wohnhaus 3", 84f, -30f, 0f, 10f, 8f, 5f),   // 12.10.: stand im Bach (v4)
            new Bau("Wohnhaus 4", 70f, 22f, 0f, 9f, 7f, 5f),
            new Bau("Wohnhaus 5", 4f, 52f, 90f, 10f, 8f, 5f),
            new Bau("Wohnhaus 6", -52f, 36f, 80f, 9f, 7f, 5f),
            new Bau("Kornspeicher", 90f, -60f, 0f, 14f, 10f, 7f, 0.7f),   // 12.10.: stand im Bach (v4)
        };

        // Hallgrund: Pferdehof (3350/2350) – Wohnhaus, großer Stall, Scheune um einen Hof; Koppeln talwärts am Bach.
        private static readonly Bau[] Hallgrund =
        {
            new Bau("Hallgrund Wohnhaus", -14f, 10f, 90f, 13f, 9f, 6f),
            new Bau("Hallgrund Pferdestall", 1f, 35f, 0f, 26f, 10f, 5.5f),   // 12.10. Weg-Prüfung: stand auf dem Weg
            new Bau("Hallgrund Scheune", -8f, -17f, 90f, 18f, 12f, 8f, 0.7f),   // 12.10. Weg-Prüfung: stand auf dem Weg
            new Bau("Hallgrund Remise", -22f, -12f, 0f, 8f, 6f, 4f),
        };

        // Dreispannhof – Winddach (1650/1080, Rinderhof): drei Stallflügel aus drei Baualtern um einen Hof, darüber ein schiefes
        // Winddach (offenes Dach auf Pfosten); Hof öffnet sich nach Südost zur Kornspur. Altbau niedrig und steil, Jungbau höher und flach.
        private static readonly Bau[] Dreispannhof =
        {
            new Bau("Dreispannhof Stallflügel Nord (Altbau)", 0f, 22f, 0f, 26f, 9f, 3.6f, 0.80f),
            new Bau("Dreispannhof Stallflügel West (Mittelbau)", -21f, 2f, 90f, 22f, 9f, 4.4f, 0.60f),
            new Bau("Dreispannhof Stallflügel Ost (Jungbau)", 21f, 6f, 90f, 18f, 10f, 5.2f, 0.42f),
            new Bau("Dreispannhof Wohnhaus und Hofladen", -10f, -26f, 0f, 13f, 9f, 6f),
        };

        // Heckenhof im Rosenhag (2250/720, Schafhof): Wege kommen von N, NW, O und SSO – Gebäude im Südwesten, Scherschuppen im Nordosten,
        // Pferch und Heckenring im Südwesten (stiller Raum hinter Hecken).
        private static readonly Bau[] Heckenhof =
        {
            new Bau("Heckenhof Wohnhaus", -24f, -10f, 20f, 12f, 8f, 5.5f),
            new Bau("Heckenhof Schafstall", -8f, -32f, 20f, 24f, 8f, 3.8f, 0.65f),
            new Bau("Heckenhof Scherschuppen", 22f, 22f, 45f, 10f, 7f, 3.2f),
        };

        // Hirtenlager am Nordrücken (1900/2860): Sommerweide – zwei Hütten, Zelte, Feuer, Feldschmied unter Dach, Pferch auf dem Rücken.
        private static readonly Bau[] Hirtenlager =
        {
            new Bau("Hirtenlager Oberhirtenhütte", 14f, 16f, 10f, 7f, 5f, 3.0f, 0.7f),
            new Bau("Hirtenlager Käserei", -6f, 22f, 100f, 6f, 5f, 3.0f, 0.7f),
        };

        // Bürgerwehr-Posten (3130/810, Kuppe über der Vorspannrampe): Wachhaus, Turm mit Blick zum Passfuß, Unterstand für Vorspannpferde.
        private static readonly Bau[] Posten =
        {
            new Bau("Posten Wachhaus", 0f, 18f, 0f, 9f, 7f, 4.2f),
            new Bau("Posten Wachturm", -12f, 15f, 0f, 4.2f, 4.2f, 10f, 0.4f, true),
        };

        // Alte Zollhütte (2700/300, Ruine an der Kornspur, Habbos Hecke): Mauerreste ohne Dach, zwei Torpfeiler am Weg, Lagerzelte.
        private static readonly Bau[] Zollhuette =
        {
            new Bau("Zollhaus Ruine Hauptbau", -11f, 16f, 15f, 12f, 9f, 2.0f, 0f, false, true),   // 12.10. Weg-Prüfung: stand auf dem Weg
            new Bau("Zollhaus Ruine Nebenbau", -22f, 2f, 15f, 7f, 6f, 1.2f, 0f, false, true),
        };

        /// <summary>
        /// Lage eines Baus relativ zu seiner Ortsmitte (Lückenliste Punkt 3, 13.10.2026: Tagesabläufe der Bewohner brauchen Haustüren und
        /// Arbeitsplätze aus denselben Bauplänen). ort = Schlüssel in WW_Orte.csv.
        /// </summary>
        internal static bool HausLage(string name, out string ort, out Vector2 rel, out float halbeGroesse)
        {
            foreach ((string o, Bau[] liste) in new[]
                     {
                         ("L3 Langer Rain", LangerRain), ("L4 Hallgrund", Hallgrund), ("L6 Dreispannhof", Dreispannhof),
                         ("L12 Heckenhof (Rosenhag)", Heckenhof), ("L7 Hirtenlager", Hirtenlager), ("L10 Bürgerwehr-Posten", Posten),
                         ("L9 Alte Zollhütte", Zollhuette)
                     })
                foreach (Bau b in liste)
                    if (b.Name == name) { ort = o; rel = new Vector2(b.X, b.Z); halbeGroesse = Mathf.Max(b.B, b.T) * 0.5f; return true; }
            ort = null; rel = default; halbeGroesse = 0f;
            return false;
        }

        public static int Bauen(Terrain t, Dictionary<string, Vector3> orte, System.Func<string, Color, Material> farbe)
        {
            var root = new GameObject("Orte ausgeformt (Greybox v1)").transform;
            WegeLaden();
            Material wand = AssetDatabase.LoadAssetAtPath<Material>(ProjectSetup.Root + "/Art/Westweiden/Proben/WW_Trim_Probe.mat")
                            ?? farbe("Wand", new Color(0.78f, 0.75f, 0.68f));
            Material dach = farbe("Dach", new Color(0.46f, 0.40f, 0.30f));
            // Dachfamilie (Claude 12.10. nachmittags, mat_dach.py): Reet alt = Standard der Hofdächer, Reet neu für gepflegte Häuser
            // im Ort, Eichenschindel für Turm, Wache, Schmiede und offene Dächer (kein Reet über Esse und Feuer)
            WestweidenTexturprobe.Texturen(new System.Text.StringBuilder());
            ReetAlt = WestweidenTexturprobe.LitMat("WW_Dach_Reet_Alt", "WW_Dach_Reet_Alt", false);
            ReetNeu = WestweidenTexturprobe.LitMat("WW_Dach_Reet_Neu", "WW_Dach_Reet_Neu", false);
            Schindel = WestweidenTexturprobe.LitMat("WW_Dach_Schindel", "WW_Dach_Schindel", false);
            WandMat = wand;
            dach = Schindel;   // offene Dächer (Unterstand, Winddach, Feldschmied)
            Material holz = farbe("Holz", new Color(0.36f, 0.30f, 0.23f));
            Material platz = farbe("Platz", new Color(0.55f, 0.53f, 0.48f));
            Material wasser = farbe("Wasser", new Color(0.22f, 0.32f, 0.36f));
            int n = 0;
            if (orte.TryGetValue("L3 Langer Rain", out Vector3 lr))
            {
                var g = new GameObject("Langer Rain").transform; g.SetParent(root, false);
                foreach (Bau b in LangerRain) { Haus(t, g, lr, b, wand, dach); n++; }
                Zylinder(t, g, "Brunnen", lr + new Vector3(0f, 0f, 0f), 1.4f, 1.0f, platz);
                Zylinder(t, g, "Wachtfeuer", lr + new Vector3(-8f, 0f, -10f), 1.2f, 0.4f, holz);
                // Brücken über den Mühlbach baut WestweidenWasser an jeder Kreuzung von Bach und Weg (12.10.)
                Zaun(t, g, "Gartenzaun Nord", lr + new Vector3(-40f, 0f, 58f), lr + new Vector3(40f, 0f, 62f), holz);
                Zaun(t, g, "Gartenzaun West", lr + new Vector3(-84f, 0f, -30f), lr + new Vector3(-84f, 0f, 30f), holz);
            }
            if (orte.TryGetValue("L4 Hallgrund", out Vector3 hg))
            {
                var g = new GameObject("Hallgrund").transform; g.SetParent(root, false);
                foreach (Bau b in Hallgrund) { Haus(t, g, hg, b, wand, dach); n++; }
                // Koppeln talwärts (Bach läuft nach Südwesten): zwei Weiden mit Holzzaun, eine Tränke
                Koppel(t, g, "Koppel Stuten", hg + new Vector3(-70f, 0f, -40f), 60f, 40f, 35f, holz);
                Koppel(t, g, "Koppel Fohlen", hg + new Vector3(-20f, 0f, -80f), 45f, 35f, 35f, holz);
                Kiste(t, g, "Tränke", hg + new Vector3(-40f, 0f, -62f), new Vector3(3f, 0.6f, 1f), 35f, holz, 0.3f);
                Zylinder(t, g, "Grab am Hof (Epilog Q2)", hg + new Vector3(26f, 0f, 30f), 0.5f, 0.9f, platz);
            }
            Material tuch = farbe("Zelttuch", new Color(0.62f, 0.56f, 0.44f));
            Material hecke = farbe("Hecke", new Color(0.20f, 0.30f, 0.17f));
            Material stein = farbe("Stein", new Color(0.58f, 0.57f, 0.53f));
            if (orte.TryGetValue("L6 Dreispannhof", out Vector3 dh))
            {
                var g = new GameObject("Dreispannhof").transform; g.SetParent(root, false);
                foreach (Bau b in Dreispannhof) { Haus(t, g, dh, b, wand, dach); n++; }
                // Das schiefe Winddach: über dem Hof, auf Pfosten, 6° gekippt – die Landmarke, die man vom Kammsattel sieht
                Unterstand(t, g, "Winddach (schief)", dh + new Vector3(0f, 0f, 6f), 22f, 14f, 6.5f, 8f, 6f, dach, holz);
                Koppel(t, g, "Rinderweide", dh + new Vector3(-70f, 0f, -10f), 55f, 45f, 10f, holz);
                // Tränke mit Zulauf 300 m westlich (Nebenfaden „Die gemiedene Tränke“, Frage 5 offen)
                Kiste(t, g, "Tränke West", dh + new Vector3(-300f, 0f, 0f), new Vector3(4f, 0.7f, 1.2f), 0f, holz, 0.35f);
                Zylinder(t, g, "Quellfassung", dh + new Vector3(-306f, 0f, 4f), 1.0f, 0.8f, stein);
            }
            if (orte.TryGetValue("L12 Heckenhof (Rosenhag)", out Vector3 hh))
            {
                var g = new GameObject("Heckenhof").transform; g.SetParent(root, false);
                foreach (Bau b in Heckenhof) { Haus(t, g, hh, b, wand, dach); n++; }
                Koppel(t, g, "Schafpferch", hh + new Vector3(-52f, 0f, -48f), 36f, 26f, 40f, holz);
                // Heckenring im Südwesten (Weißdorn; Greybox-Körper, begehbar nur über die Lücken – Hecken sind sichtbar, keine unsichtbare Grenze)
                Hecke(t, g, "Hecke Süd", hh + new Vector3(-10f, 0f, -78f), hh + new Vector3(-62f, 0f, -86f), hecke);
                Hecke(t, g, "Hecke Südwest", hh + new Vector3(-74f, 0f, -80f), hh + new Vector3(-92f, 0f, -30f), hecke);
                Hecke(t, g, "Hecke West", hh + new Vector3(-92f, 0f, -18f), hh + new Vector3(-70f, 0f, 14f), hecke);
                Hecke(t, g, "Hecke Nordost", hh + new Vector3(40f, 0f, 40f), hh + new Vector3(10f, 0f, 62f), hecke);
            }
            if (orte.TryGetValue("L7 Hirtenlager", out Vector3 hl))
            {
                var g = new GameObject("Hirtenlager").transform; g.SetParent(root, false);
                foreach (Bau b in Hirtenlager) { Haus(t, g, hl, b, wand, dach); n++; }
                Zelt(t, g, "Zelt 1", hl + new Vector3(22f, 0f, -4f), 4f, 3.5f, 2.4f, 30f, tuch);
                Zelt(t, g, "Zelt 2", hl + new Vector3(28f, 0f, 8f), 4f, 3.5f, 2.4f, 60f, tuch);
                Zelt(t, g, "Zelt 3", hl + new Vector3(6f, 0f, 30f), 4.5f, 3.5f, 2.5f, 100f, tuch);
                Zylinder(t, g, "Lagerfeuer", hl + new Vector3(8f, 0f, 6f), 1.2f, 0.4f, holz);
                Unterstand(t, g, "Feldschmied (Reparatur)", hl + new Vector3(-8f, 0f, 8f), 6f, 4f, 3f, 0f, 0f, dach, holz);
                Kiste(t, g, "Amboss", hl + new Vector3(-8f, 0f, 8f), new Vector3(0.8f, 0.8f, 0.4f), 0f, stein, 0.4f);
                Koppel(t, g, "Pferch auf dem Rücken", hl + new Vector3(44f, 0f, 40f), 36f, 26f, 20f, holz);
            }
            if (orte.TryGetValue("L10 Bürgerwehr-Posten", out Vector3 bp))
            {
                var g = new GameObject("Bürgerwehr-Posten").transform; g.SetParent(root, false);
                foreach (Bau b in Posten) { Haus(t, g, bp, b, wand, dach); n++; }
                Unterstand(t, g, "Unterstand Vorspannpferde", bp + new Vector3(10f, 0f, -22f), 12f, 6f, 3.2f, 0f, 0f, dach, holz);
                Koppel(t, g, "Koppel Vorspannpferde", bp + new Vector3(-20f, 0f, -38f), 40f, 24f, 0f, holz);
                // Schlagbaum hochgeklappt neben dem Weg (offen; kein Hindernis)
                Kiste(t, g, "Schlagbaum-Pfosten", bp + new Vector3(4f, 0f, 5f), new Vector3(0.4f, 1.4f, 0.4f), 0f, holz, 0.7f);
                var sb = GameObject.CreatePrimitive(PrimitiveType.Cube); sb.name = "Schlagbaum (offen)"; sb.transform.SetParent(g, false);
                Vector3 sp = bp + new Vector3(4f, 0f, 5f); sp.y = Boden(t, sp.x, sp.z) + 4.2f;
                sb.transform.SetPositionAndRotation(sp, Quaternion.Euler(0f, 0f, 80f)); sb.transform.localScale = new Vector3(6f, 0.15f, 0.15f);
                sb.GetComponent<Renderer>().sharedMaterial = holz; Object.DestroyImmediate(sb.GetComponent<Collider>());
                Zylinder(t, g, "Feuerkorb", bp + new Vector3(6f, 0f, 12f), 0.5f, 1.1f, holz);
            }
            if (orte.TryGetValue("Kornwagen", out Vector3 kw))
            {
                var g = new GameObject("Liegengebliebener Kornwagen").transform; g.SetParent(root, false);
                Wagen(t, g, kw + new Vector3(7f, 0f, 2f), 25f, holz, tuch);
            }
            if (orte.TryGetValue("L9 Alte Zollhütte", out Vector3 zh))
            {
                var g = new GameObject("Alte Zollhütte (Habbos Hecke)").transform; g.SetParent(root, false);
                foreach (Bau b in Zollhuette) { Haus(t, g, zh, b, wand, dach); n++; }
                // Torpfeiler der alten Zollstelle beiderseits der Kornspur (Weg läuft NW → SSO)
                Kiste(t, g, "Torpfeiler West", zh + new Vector3(-5f, 0f, -3f), new Vector3(1.4f, 4.5f, 1.4f), 30f, stein, 2.0f);
                Kiste(t, g, "Torpfeiler Ost", zh + new Vector3(5f, 0f, 3f), new Vector3(1.4f, 3.2f, 1.4f), 30f, stein, 1.4f);
                Zelt(t, g, "Lagerzelt 1", zh + new Vector3(16f, 0f, 18f), 4.5f, 3.5f, 2.4f, 10f, tuch);
                Zelt(t, g, "Lagerzelt 2", zh + new Vector3(26f, 0f, 6f), 4f, 3.5f, 2.3f, 80f, tuch);
                Zelt(t, g, "Lagerzelt 3", zh + new Vector3(8f, 0f, 28f), 4f, 3f, 2.2f, 140f, tuch);
                Zylinder(t, g, "Lagerfeuer", zh + new Vector3(14f, 0f, 8f), 1.2f, 0.4f, holz);
                Hecke(t, g, "Habbos Hecke", zh + new Vector3(36f, 0f, -10f), zh + new Vector3(30f, 0f, 44f), hecke);
            }
            Debug.Log($"[Ashveil] Westweiden: {n} Baukörper ausgeformt");
            return n;
        }

        private static Material ReetAlt, ReetNeu, Schindel, WandMat;

        private static Material DachFuer(string name)
        {
            foreach (string k in new[] { "Wachturm", "Wachhaus", "Schmiede", "Bürgerwehr", "Remise", "Käserei" })
                if (name.Contains(k)) return Schindel;
            foreach (string k in new[] { "Gasthaus", "Bäckerei", "Händler", "Jungbau", "Lehrerhaus" })
                if (name.Contains(k)) return ReetNeu;
            return ReetAlt;
        }

        // Max 12.10.: „Zäune, die über Wege gehen“ – Zäune lassen an jedem Weg eine Lücke (Tor), Pfosten nie auf dem Weg
        private static readonly List<(List<Vector2> p, float halb)> Wege = new List<(List<Vector2>, float)>();

        private static void WegeLaden()
        {
            Wege.Clear();
            string pfad = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), ProjectSetup.Root + "/Data/Westweiden/Entwurf/WW_Wege.csv");
            if (!System.IO.File.Exists(pfad)) return;
            var d = new Dictionary<string, List<Vector2>>();
            foreach (string line in System.IO.File.ReadAllLines(pfad, System.Text.Encoding.UTF8))
            {
                string[] f = line.Split(';');
                if (f.Length < 3) continue;
                if (!d.TryGetValue(f[0], out var l)) d[f[0]] = l = new List<Vector2>();
                l.Add(new Vector2(float.Parse(f[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture)));
            }
            foreach (var kv in d) Wege.Add((kv.Value, kv.Key.StartsWith("Hofstraße") || kv.Key.StartsWith("Vorspannrampe") ? 1.5f : 0.8f));
        }

        /// <summary>Abstand zum nächsten Weg minus dessen halbe Breite (≤ 0 = auf dem Weg).</summary>
        internal static float WegAbstand(float x, float z)
        {
            float best = float.MaxValue; var q = new Vector2(x, z);
            foreach (var (p, halb) in Wege)
                for (int i = 1; i < p.Count; i++)
                {
                    Vector2 a = p[i - 1], ab = p[i] - a;
                    float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                    best = Mathf.Min(best, Vector2.Distance(q, a + ab * t) - halb);
                }
            return best;
        }

        private static float Boden(Terrain t, float x, float z) => t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y;

        /// <summary>Haus: Körper (Sockel reicht 1,5 m in den Boden) + Satteldach als eigenes Netz; Turm mit Zeltdach.</summary>
        private static void Haus(Terrain t, Transform parent, Vector3 mitte, Bau b, Material wand, Material dach)
        {
            Quaternion q = Quaternion.Euler(0f, b.Yaw, 0f);
            Vector3 p = mitte + new Vector3(b.X, 0f, b.Z);
            float y = Mathf.Min(Boden(t, p.x, p.z), Boden(t, p.x + b.B * 0.5f, p.z + b.T * 0.5f), Boden(t, p.x - b.B * 0.5f, p.z - b.T * 0.5f));
            var go = new GameObject(b.Name); go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(new Vector3(p.x, y, p.z), q);
            var koerper = new GameObject("Körper"); koerper.transform.SetParent(go.transform, false);
            koerper.AddComponent<MeshFilter>().sharedMesh = Waende(b.B, b.T, b.H);
            koerper.AddComponent<MeshRenderer>().sharedMaterial = wand;
            koerper.AddComponent<MeshCollider>();
            if (b.Ruine) return;
            Fassade(go.transform, b, mitte);
            var dg = new GameObject("Dach"); dg.transform.SetParent(go.transform, false);
            Mesh dm; float senk;
            if (b.Turm)
            {
                float bR = b.B + 0.8f, hR = b.B * 0.9f;
                senk = hR * 0.4f / (bR * 0.5f);                    // Traufe so tief, dass die Dachfläche die Wandkrone trifft
                dm = Zeltdach(bR, b.T + 0.8f, hR);
            }
            else
            {
                float tR = b.T + 1.0f, hR = b.T * b.Dach;
                senk = hR * (tR - b.T) / tR;
                dm = Satteldach(b.B + 0.8f, tR, hR, b.B, b.T);
            }
            dg.transform.localPosition = new Vector3(0f, b.H - senk, 0f);
            dg.AddComponent<MeshFilter>().sharedMesh = dm;
            Material dmat = ReetAlt != null ? DachFuer(b.Name) : dach;
            dg.AddComponent<MeshRenderer>().sharedMaterials = b.Turm ? new[] { dmat } : new[] { dmat, WandMat != null ? WandMat : wand };
            dg.AddComponent<MeshCollider>();
        }

        // ------------------------------------------------------------------ Fassade (14.10.2026, Lückenliste Punkt 6 „Fachwerk-Module“)
        private static Material _fachwerk, _dunkel;

        /// <summary>Fachwerkholz: Eichenband des Trim-Sheets, nachgedunkelt (auf dem hellen Kalkputz sonst zu schwach).</summary>
        private static Material FachwerkMat()
        {
            if (_fachwerk != null) return _fachwerk;
            const string Pfad = ProjectSetup.Root + "/Art/Westweiden/Holz/WW_Fachwerk.mat";
            _fachwerk = AssetDatabase.LoadAssetAtPath<Material>(Pfad);
            Material trim = HolzBau.Holz;
            if (_fachwerk == null && trim != null)
            {
                ProjectSetup.EnsureFolder(ProjectSetup.Root + "/Art/Westweiden/Holz");
                _fachwerk = new Material(trim) { name = "WW_Fachwerk" };
                AssetDatabase.CreateAsset(_fachwerk, Pfad);
            }
            if (_fachwerk != null) { _fachwerk.SetColor("_BaseColor", new Color(0.50f, 0.42f, 0.35f, 1f)); EditorUtility.SetDirty(_fachwerk); }
            return _fachwerk;
        }

        /// <summary>Fensteröffnung: fast schwarz (Raum dahinter), leichter Glanz (Butzenglas).</summary>
        private static Material DunkelMat()
        {
            if (_dunkel != null) return _dunkel;
            const string Pfad = ProjectSetup.Root + "/Art/Westweiden/Holz/WW_Fenster_Dunkel.mat";
            _dunkel = AssetDatabase.LoadAssetAtPath<Material>(Pfad);
            Material trim = HolzBau.Holz;
            if (_dunkel == null && trim != null)
            {
                ProjectSetup.EnsureFolder(ProjectSetup.Root + "/Art/Westweiden/Holz");
                _dunkel = new Material(trim) { name = "WW_Fenster_Dunkel" };
                AssetDatabase.CreateAsset(_dunkel, Pfad);
            }
            if (_dunkel != null)
            {
                _dunkel.SetColor("_BaseColor", new Color(0.07f, 0.065f, 0.06f, 1f));
                if (_dunkel.HasProperty("_Smoothness")) _dunkel.SetFloat("_Smoothness", 0.55f);
                EditorUtility.SetDirty(_dunkel);
            }
            return _dunkel;
        }

        private static bool Wirtschaftsbau(string n)
        {
            foreach (string k in new[] { "Stall", "Scheune", "Speicher", "Remise", "Schuppen", "Karrenhalt" })
                if (n.Contains(k)) return true;
            return false;
        }

        /// <summary>
        /// Fachwerk über dem Steinsockel (ab der Eichenschwelle 2,45 m bis zur Traufe): Eck- und Zwischenständer (≈ 1,8 m), Riegel,
        /// Rähm, Streben in den Endfeldern; Fenster mit Rahmen, Sohlbank und offenen Läden; Tür (Wirtschaftsbau: Tor) auf der Seite
        /// zur Ortsmitte. Zwei Netze je Haus (Holz, Öffnungen), ohne Kollision (der Körper sperrt). Fehlt das Material: nichts.
        /// </summary>
        private static void Fassade(Transform haus, Bau b, Vector3 ortMitte)
        {
            Material holz = FachwerkMat(), dunkel = DunkelMat();
            if (holz == null || dunkel == null) return;
            int saat = 31; foreach (char c in b.Name) saat = saat * 37 + c;
            var nh = new HolzBau.Netz(haus, saat);
            var nd = new HolzBau.Netz(haus, saat + 1);
            bool wirt = Wirtschaftsbau(b.Name);
            float hx = b.B * 0.5f, hz = b.T * 0.5f;
            const float Vor = 0.05f;                                    // Hölzer stehen etwas vor dem Putz
            float y0 = 2.45f, y1 = b.H - 0.12f;
            // Türseite: Wand, deren Außenseite am meisten zur Ortsmitte zeigt
            Vector3 zm = haus.InverseTransformDirection(ortMitte - haus.position); zm.y = 0f;
            var seiten = new[] { (Vector3.back, hx), (Vector3.right, hz), (Vector3.forward, hx), (Vector3.left, hz) };   // (Normale, halbe Länge)
            int tuerSeite = 0; float best = -2f;
            for (int i = 0; i < 4; i++)
            {
                float d = zm.sqrMagnitude > 1f ? Vector3.Dot(seiten[i].Item1, zm.normalized) : (i == 0 ? 1f : 0f);
                if (d > best) { best = d; tuerSeite = i; }
            }
            for (int si = 0; si < 4; si++)
            {
                Vector3 n = seiten[si].Item1;
                float halb = seiten[si].Item2;
                Vector3 entlang = Vector3.Cross(Vector3.up, n);          // Wandrichtung (lokal)
                float abst = (n.x != 0f ? hx : hz);
                Vector3 W(float s, float y, float vor = Vor) => haus.TransformPoint(n * (abst + vor) + entlang * s + Vector3.up * y);
                Vector3 nWelt = haus.TransformDirection(n);
                // Felder (Ständer-Lagen) entlang der Wand
                int felder = Mathf.Max(2, Mathf.RoundToInt(2f * halb / 1.8f));
                var lagen = new float[felder + 1];
                for (int i = 0; i <= felder; i++) lagen[i] = Mathf.Lerp(-halb + 0.12f, halb - 0.12f, i / (float)felder);
                bool tuerWand = si == tuerSeite;
                float tuerB = wirt ? Mathf.Min(3.2f, halb) : 1.15f, tuerH = wirt ? Mathf.Min(3.1f, b.H - 0.4f) : 2.15f;
                float tuerS = tuerWand && halb > 3f ? -halb * 0.25f : 0f;
                bool UeberTuer(float sx, float yy) => tuerWand && Mathf.Abs(sx - tuerS) < tuerB * 0.5f + 0.15f && yy < tuerH + 0.25f;
                // Fachwerk nur, wo über dem Sockel genug Wand ist
                if (y1 - y0 > 1.0f)
                {
                    int stock = Mathf.Max(1, Mathf.RoundToInt((y1 - y0) / 2.4f));
                    nh.Prisma(W(-halb - 0.1f, y1), W(halb + 0.1f, y1), 0.20f, 0.22f, 4, nWelt);                    // Rähm
                    for (int i = 0; i <= felder; i++)
                    {
                        bool eck = i == 0 || i == felder;
                        float yu = UeberTuer(lagen[i], y0) ? tuerH + 0.25f : y0;                                   // über dem Tor: Ständer erst ab dem Sturz
                        if (yu < y1 - 0.3f) nh.Prisma(W(lagen[i], yu), W(lagen[i], y1), eck ? 0.24f : 0.18f, 0.20f, 4, nWelt);   // Ständer
                    }
                    for (int k = 1; k < stock * 2; k++)                                                                // Riegel je halbem Stockwerk
                    {
                        float yr = Mathf.Lerp(y0, y1, k / (float)(stock * 2));
                        for (int i = 0; i < felder; i++)
                        {
                            if (k == 1 && FensterFeld(i, felder, wirt, b.Turm)) continue;                             // Fensterfeld: dort sitzt das Fenster
                            if (UeberTuer((lagen[i] + lagen[i + 1]) * 0.5f, yr)) continue;
                            nh.Prisma(W(lagen[i], yr), W(lagen[i + 1], yr), 0.16f, 0.16f, 4, nWelt, false);
                        }
                    }
                    // Streben im ersten und letzten Feld (je Stockwerk), steigend zur Ecke hin
                    for (int st = 0; st < stock; st++)
                    {
                        float ya = Mathf.Lerp(y0, y1, st / (float)stock), yb = Mathf.Lerp(y0, y1, (st + 1) / (float)stock);
                        if (!UeberTuer(lagen[0], ya) && !UeberTuer(lagen[1], ya)) nh.Prisma(W(lagen[1], ya), W(lagen[0], yb), 0.15f, 0.16f, 4, nWelt, false);
                        if (!UeberTuer(lagen[felder], ya) && !UeberTuer(lagen[felder - 1], ya)) nh.Prisma(W(lagen[felder - 1], ya), W(lagen[felder], yb), 0.15f, 0.16f, 4, nWelt, false);
                    }
                    // Fenster im Obergeschoss (nicht bei Wirtschaftsbauten)
                    if (!wirt)
                        for (int i = 0; i < felder; i++)
                        {
                            if (!FensterFeld(i, felder, wirt, b.Turm)) continue;
                            float s = (lagen[i] + lagen[i + 1]) * 0.5f;
                            float ym = Mathf.Lerp(y0, y1, b.Turm ? 0.8f : 0.5f / stock);
                            if (b.Turm && !tuerWand && si % 2 == 1) continue;
                            Fenster(nh, nd, W, nWelt, s, ym, 0.62f, 0.78f, false);
                        }
                }
                // Giebel (Wände quer zum First, ±x): Kehlbalken, Mittelständer und zwei Streben im Giebeldreieck
                if (!b.Turm && n.x != 0f && b.Dach > 0.05f && y1 - y0 > 0.6f)
                {
                    float tR = b.T + 1.0f, hR = b.T * b.Dach, senk = hR * (tR - b.T) / tR;
                    float yFuss = b.H - senk, yFirst = yFuss + hR;
                    float yK = b.H + (yFirst - b.H) * 0.45f;
                    float zK = tR * 0.5f * (1f - (yK - yFuss) / hR) - 0.2f;      // halbe Breite des Giebels auf Kehlbalkenhöhe
                    if (zK > 0.5f)
                    {
                        nh.Prisma(W(-zK, yK), W(zK, yK), 0.18f, 0.18f, 4, nWelt);
                        nh.Prisma(W(0f, b.H), W(0f, yFirst - 0.35f), 0.18f, 0.18f, 4, nWelt);
                        nh.Prisma(W(-halb * 0.7f, b.H), W(-zK * 0.35f, yK), 0.15f, 0.16f, 4, nWelt, false);
                        nh.Prisma(W(halb * 0.7f, b.H), W(zK * 0.35f, yK), 0.15f, 0.16f, 4, nWelt, false);
                    }
                }
                // Erdgeschoss im Steinsockel: Fenster (Wirtschaftsbau: Lüftungsschlitze), Tür
                for (float s = -halb + 1.6f; s <= halb - 1.4f; s += wirt ? 3.4f : 2.6f)
                {
                    if (tuerWand && Mathf.Abs(s - tuerS) < tuerB * 0.5f + 0.9f) continue;
                    if (b.Turm && si % 2 == 1) continue;
                    if (wirt) Fenster(nh, nd, W, nWelt, s, 1.85f, 0.55f, 0.28f, false);
                    else Fenster(nh, nd, W, nWelt, s, 1.45f, 0.75f, 0.85f, true);
                }
                if (tuerWand)
                {
                    // Türöffnung dunkel, Bretter davor (Tor: zwei Flügel), Türstock und Sturz
                    nd.Prisma(W(tuerS - tuerB * 0.5f, tuerH * 0.5f, 0.015f), W(tuerS + tuerB * 0.5f, tuerH * 0.5f, 0.015f), tuerH, 0.03f, 4, nWelt);
                    int bretter = Mathf.Max(4, Mathf.RoundToInt(tuerB / 0.2f));
                    for (int i = 0; i < bretter; i++)
                    {
                        float s = Mathf.Lerp(tuerS - tuerB * 0.5f + 0.1f, tuerS + tuerB * 0.5f - 0.1f, i / (float)(bretter - 1));
                        if (wirt && Mathf.Abs(s - tuerS) < 0.08f) continue;    // Fuge zwischen den Torflügeln
                        nh.Prisma(W(s, 0.02f, 0.05f), W(s, tuerH - 0.04f, 0.05f), 0.19f, 0.045f, 4, nWelt);
                    }
                    nh.Prisma(W(tuerS - tuerB * 0.5f - 0.1f, 0f), W(tuerS - tuerB * 0.5f - 0.1f, tuerH + 0.12f), 0.18f, 0.2f, 4, nWelt);
                    nh.Prisma(W(tuerS + tuerB * 0.5f + 0.1f, 0f), W(tuerS + tuerB * 0.5f + 0.1f, tuerH + 0.12f), 0.18f, 0.2f, 4, nWelt);
                    nh.Prisma(W(tuerS - tuerB * 0.5f - 0.3f, tuerH + 0.14f), W(tuerS + tuerB * 0.5f + 0.3f, tuerH + 0.14f), 0.22f, 0.22f, 4, nWelt);
                    nh.Prisma(W(tuerS - tuerB * 0.5f - 0.1f, 0.04f, 0.35f), W(tuerS + tuerB * 0.5f + 0.1f, 0.04f, 0.35f), 0.6f, 0.1f, 4, Vector3.up);   // Trittbohle vor der Tür
                }
            }
            var gh = nh.Fertig("Fachwerk und Türen", holz);
            var gd = nd.Fertig("Öffnungen", dunkel);
            if (gd != null) gd.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Fenster in Feld i? Endfelder tragen Streben; bei Türmen nur das mittlere Feld.</summary>
        private static bool FensterFeld(int i, int felder, bool wirt, bool turm)
        {
            if (wirt) return false;
            if (turm) return i == felder / 2;
            return i > 0 && i < felder - 1 && (i % 2 == 1 || felder <= 4);
        }

        private static void Fenster(HolzBau.Netz nh, HolzBau.Netz nd, System.Func<float, float, float, Vector3> W, Vector3 n, float s, float y, float b, float h, bool laeden)
        {
            Vector3 P(float a, float y2, float vor = 0.05f) => W(a, y2, vor);
            nd.Prisma(P(s - b * 0.5f, y, 0.015f), P(s + b * 0.5f, y, 0.015f), h, 0.03f, 4, n);                             // Öffnung
            nh.Prisma(P(s - b * 0.5f - 0.06f, y - h * 0.5f - 0.06f), P(s - b * 0.5f - 0.06f, y + h * 0.5f + 0.06f), 0.11f, 0.12f, 4, n);   // Rahmen
            nh.Prisma(P(s + b * 0.5f + 0.06f, y - h * 0.5f - 0.06f), P(s + b * 0.5f + 0.06f, y + h * 0.5f + 0.06f), 0.11f, 0.12f, 4, n);
            nh.Prisma(P(s - b * 0.5f - 0.12f, y + h * 0.5f + 0.06f), P(s + b * 0.5f + 0.12f, y + h * 0.5f + 0.06f), 0.12f, 0.13f, 4, n);
            nh.Prisma(P(s - b * 0.5f - 0.14f, y - h * 0.5f - 0.07f, 0.09f), P(s + b * 0.5f + 0.14f, y - h * 0.5f - 0.07f, 0.09f), 0.09f, 0.2f, 4, n);   // Sohlbank
            if (h > 0.5f) nh.Prisma(P(s, y - h * 0.5f, 0.04f), P(s, y + h * 0.5f, 0.04f), 0.05f, 0.05f, 4, n);                      // Sprosse
            if (!laeden) return;
            // offene Läden links und rechts der Öffnung
            foreach (float sg in new[] { -1f, 1f })
            {
                float sm = s + sg * (b * 0.5f + 0.1f + b * 0.25f);
                nh.Prisma(P(sm, y - h * 0.5f, 0.09f), P(sm, y + h * 0.5f, 0.09f), b * 0.5f, 0.04f, 4, n);
            }
        }

        private static readonly Dictionary<string, Mesh> Netze = new Dictionary<string, Mesh>();

        /// <summary>Vier Wände mit den Bändern des Trim-Sheets (unten Sockelquader 0,6 m, Bruchstein bis 2,2 m, Eichenschwelle 0,25 m,
        /// darüber Kalkputz bis zur Traufe); u = Meter / 2 (Kachelbreite), Sockel reicht 1,5 m in den Boden. Bänder in Bildzeilen
        /// (oben = 0): Bruchstein 0–0,40, Kalk 0,40–0,60, Eiche 0,60–0,76, Sockel 0,76–0,92 → Unity-v = 1 − Bildzeile.</summary>
        private static Mesh Waende(float b, float t, float h)
        {
            string key = $"W{b:0.0}_{t:0.0}_{h:0.0}";
            if (Netze.TryGetValue(key, out Mesh m) && m != null) return m;
            var baender = new List<(float y0, float y1, float v0, float v1)>
            {
                (-1.5f, 0.6f, 0.76f, 0.92f), (0.6f, 2.2f, 0.0f, 0.40f), (2.2f, 2.45f, 0.60f, 0.76f), (2.45f, h, 0.40f, 0.60f),
            };
            float x = b * 0.5f, z = t * 0.5f;
            var ecken = new[] { new Vector3(-x, 0, -z), new Vector3(x, 0, -z), new Vector3(x, 0, z), new Vector3(-x, 0, z) };
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int s = 0; s < 4; s++)
            {
                Vector3 a = ecken[s], c = ecken[(s + 1) % 4];
                float len = Vector3.Distance(a, c);
                foreach (var (y0, y1o, v0, v1o) in baender)
                {
                    float y1 = Mathf.Min(y1o, h);
                    if (y1 <= y0) continue;
                    float v1 = v0 + (v1o - v0) * (y1 - y0) / (y1o - y0);   // gekappte Ruinenwand: Band anteilig
                    int i = v.Count;
                    v.Add(a + Vector3.up * y0); v.Add(c + Vector3.up * y0); v.Add(c + Vector3.up * y1); v.Add(a + Vector3.up * y1);
                    float u1 = len / 2f;
                    uv.Add(new Vector2(0, 1 - v1)); uv.Add(new Vector2(u1, 1 - v1)); uv.Add(new Vector2(u1, 1 - v0)); uv.Add(new Vector2(0, 1 - v0));
                    tri.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
                }
            }
            m = new Mesh { name = "WW Wände " + key };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            Netze[key] = m;
            return m;
        }

        /// <summary>Satteldach: First entlang der lokalen x-Achse (Breite b), Traufen an ±t/2 auf y = 0, Firsthöhe h.
        /// Teilnetz 0 = Dachflächen (UV: u längs der Traufe, v hangaufwärts, 3 m je Kachel = mat_dach.py),
        /// Teilnetz 1 = Giebeldreiecke in der Wandebene (±bW/2, Breite tW) mit dem Putzband des Trim-Sheets (v 0,41–0,59).
        /// bW ≤ 0: keine Giebel (offene Dächer). Netze werden geteilt.</summary>
        private static Mesh Satteldach(float b, float t, float h, float bW = 0f, float tW = 0f)
        {
            string key = $"S{b:0.0}_{t:0.0}_{h:0.0}_{bW:0.0}_{tW:0.0}";
            if (Netze.TryGetValue(key, out Mesh m) && m != null) return m;
            float x = b * 0.5f, z = t * 0.5f, L = Mathf.Sqrt(z * z + h * h) / 3f, U = b / 3f;
            var v = new List<Vector3>
            {
                new Vector3(-x, 0, -z), new Vector3(x, 0, -z), new Vector3(x, h, 0), new Vector3(-x, h, 0),   // Südfläche
                new Vector3(x, 0, z), new Vector3(-x, 0, z), new Vector3(-x, h, 0), new Vector3(x, h, 0),     // Nordfläche
            };
            var uv = new List<Vector2>
            {
                new Vector2(0, 0), new Vector2(U, 0), new Vector2(U, L), new Vector2(0, L),
                new Vector2(0, 0), new Vector2(U, 0), new Vector2(U, L), new Vector2(0, L),
            };
            var t0 = new List<int> { 0, 3, 2, 0, 2, 1, 4, 7, 6, 4, 6, 5 };
            var t1 = new List<int>();
            if (bW > 0f)
            {
                float gx = bW * 0.5f, gz = tW * 0.5f, gy = h * (1f - tW / t);   // Giebelfuß = Wandkrone (Dach ist um gy abgesenkt)
                foreach (float sx in new[] { -1f, 1f })
                {
                    int i = v.Count;
                    v.Add(new Vector3(sx * gx, gy, sx < 0 ? gz : -gz)); v.Add(new Vector3(sx * gx, gy, sx < 0 ? -gz : gz)); v.Add(new Vector3(sx * gx, h, 0));
                    uv.Add(new Vector2(0, 0.41f)); uv.Add(new Vector2(tW / 2f, 0.41f)); uv.Add(new Vector2(tW / 4f, 0.59f));
                    t1.AddRange(new[] { i, i + 2, i + 1 });
                }
            }
            m = new Mesh { name = "WW Satteldach " + key, subMeshCount = t1.Count > 0 ? 2 : 1 };
            m.SetVertices(v); m.SetUVs(0, uv);
            m.SetTriangles(t0, 0);
            if (t1.Count > 0) m.SetTriangles(t1, 1);
            m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            Netze[key] = m;
            return m;
        }

        private static Mesh Zeltdach(float b, float t, float h)
        {
            string key = $"Z{b:0.0}_{t:0.0}_{h:0.0}";
            if (Netze.TryGetValue(key, out Mesh m) && m != null) return m;
            float x = b * 0.5f, z = t * 0.5f;
            Vector3 s = new Vector3(0, h, 0);
            Vector3 a = new Vector3(-x, 0, -z), c = new Vector3(x, 0, -z), d = new Vector3(x, 0, z), e = new Vector3(-x, 0, z);
            var v = new List<Vector3> { a, s, c, c, s, d, d, s, e, e, s, a };
            var tri = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
            float sl = Mathf.Sqrt(x * x + h * h) / 3f;
            var uv = new List<Vector2>();
            for (int k = 0; k < 4; k++) { float w = (k % 2 == 0 ? b : t) / 3f; uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(w * 0.5f, sl)); uv.Add(new Vector2(w, 0)); }
            m = new Mesh { name = "WW Zeltdach " + key };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            Netze[key] = m;
            return m;
        }

        private static void Kiste(Terrain t, Transform parent, string name, Vector3 p, Vector3 s, float yaw, Material m, float ueberBoden)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name; g.transform.SetParent(parent, false);
            g.transform.SetPositionAndRotation(new Vector3(p.x, Boden(t, p.x, p.z) + ueberBoden, p.z), Quaternion.Euler(0f, yaw, 0f));
            g.transform.localScale = s;
            g.GetComponent<Renderer>().sharedMaterial = m;
        }

        private static void Zylinder(Terrain t, Transform parent, string name, Vector3 p, float r, float h, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            g.name = name; g.transform.SetParent(parent, false);
            g.transform.position = new Vector3(p.x, Boden(t, p.x, p.z) + h * 0.5f, p.z);
            g.transform.localScale = new Vector3(r * 2f, h * 0.5f, r * 2f);
            g.GetComponent<Renderer>().sharedMaterial = m;
        }

        /// <summary>
        /// Holzzaun: Pfosten alle 2,5 m und zwei Stangen, dem Gelände folgend; Tor (Lücke), wo ein Weg kreuzt.
        /// 14.10.2026 (Lückenliste Punkt 6): Modell aus <see cref="HolzBau"/> – gespaltene, leicht schiefe Pfosten und Stangen im
        /// Eichenholz des Trim-Sheets, ein Mesh je Zaunstück. Fehlt das Material, bleibt die Greybox.
        /// </summary>
        private static void Zaun(Terrain t, Transform parent, string name, Vector3 a, Vector3 b, Material m)
        {
            Material holz = HolzBau.Holz;
            if (holz != null) { ZaunModell(t, parent, name, a, b, holz); return; }
            var g = new GameObject(name).transform; g.SetParent(parent, false);
            float len = Vector3.Distance(new Vector3(a.x, 0, a.z), new Vector3(b.x, 0, b.z));
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 2.5f));
            Vector3 prev = Vector3.zero;
            bool luecke = false;
            for (int i = 0; i <= n; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / (float)n);
                p.y = Boden(t, p.x, p.z);
                if (WegAbstand(p.x, p.z) < 1.5f) { prev = p; luecke = true; continue; }   // Tor im Zaun, wo ein Weg kreuzt
                var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post.name = "Pfosten"; post.transform.SetParent(g, false);
                post.transform.position = p + Vector3.up * 0.6f; post.transform.localScale = new Vector3(0.15f, 1.2f, 0.15f);
                post.GetComponent<Renderer>().sharedMaterial = m;
                Object.DestroyImmediate(post.GetComponent<Collider>());
                if (i > 0 && !luecke)
                    foreach (float hy in new[] { 0.55f, 1.0f })
                    {
                        var latte = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        latte.name = "Latte"; latte.transform.SetParent(g, false);
                        Vector3 m0 = prev + Vector3.up * hy, m1 = p + Vector3.up * hy;
                        latte.transform.position = (m0 + m1) * 0.5f;
                        latte.transform.rotation = Quaternion.LookRotation(m1 - m0);
                        latte.transform.localScale = new Vector3(0.08f, 0.12f, Vector3.Distance(m0, m1));
                        latte.GetComponent<Renderer>().sharedMaterial = m;
                        Object.DestroyImmediate(latte.GetComponent<Collider>());
                    }
                prev = p;
                luecke = false;
            }
            // Greybox: Zäune ohne Kollision (keine unsichtbaren Wände, WORLD-114; echte Zäune bekommen später schmale Kollider)
        }

        private static void ZaunModell(Terrain t, Transform parent, string name, Vector3 a, Vector3 b, Material holz)
        {
            var g = new GameObject(name).transform; g.SetParent(parent, false);
            int saat = 17; foreach (char c in name) saat = saat * 31 + c;   // fester Zufall je Zaun (string.GetHashCode ist nicht stabil)
            var netz = new HolzBau.Netz(g, saat);
            float len = Vector3.Distance(new Vector3(a.x, 0, a.z), new Vector3(b.x, 0, b.z));
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 2.5f));
            Vector3 dir = new Vector3(b.x - a.x, 0f, b.z - a.z).normalized;
            Vector3 prev = Vector3.zero;
            bool luecke = true;
            for (int i = 0; i <= n; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / (float)n);
                p += dir * netz.Zufall(-0.25f, 0.25f) * (i > 0 && i < n ? 1f : 0f);   // von Hand gesetzt, nicht abgemessen
                p.y = Boden(t, p.x, p.z);
                if (WegAbstand(p.x, p.z) < 1.5f) { luecke = true; continue; }   // Tor im Zaun, wo ein Weg kreuzt
                bool torpfosten = luecke && i > 0;
                float h = torpfosten ? 1.45f : netz.Zufall(1.12f, 1.32f);
                Vector3 kopf = p + new Vector3(netz.Zufall(-0.05f, 0.05f), h, netz.Zufall(-0.05f, 0.05f));
                netz.Prisma(p - Vector3.up * 0.25f, kopf, torpfosten ? 0.24f : netz.Zufall(0.15f, 0.19f), torpfosten ? 0.24f : 0.17f, 6, dir);
                if (i > 0 && !luecke)
                {
                    Vector3 r = (p - prev); r.y = 0f; r.Normalize();
                    foreach (float hy in new[] { 0.55f, 1.0f })
                    {
                        Vector3 m0 = prev + Vector3.up * (hy + netz.Zufall(-0.04f, 0.04f)) - r * netz.Zufall(0.06f, 0.18f);
                        Vector3 m1 = p + Vector3.up * (hy + netz.Zufall(-0.04f, 0.04f)) + r * netz.Zufall(0.06f, 0.18f);
                        netz.Prisma(m0, m1, netz.Zufall(0.10f, 0.13f), 0.09f, 5, Vector3.up);
                    }
                    // Max 14.10.2026: Zäune sperren, man kann darüberspringen – Kollision je Feld bis zur oberen Stange (1,05 m;
                    // Sprunghöhe 1,48 m, Stufenhöhe 0,4 m), dem Gelände folgend; Tore (Weglücken) bleiben frei
                    var feld = new GameObject("Zaunfeld").transform;
                    feld.SetParent(g, false);
                    Vector3 fa = prev, fb = p;
                    feld.position = (fa + fb) * 0.5f + Vector3.up * 0.525f;
                    feld.rotation = Quaternion.LookRotation(fb - fa);
                    var bc = feld.gameObject.AddComponent<BoxCollider>();
                    bc.size = new Vector3(0.2f, 1.05f, Vector3.Distance(fa, fb) + 0.1f);
                    GameObjectUtility.SetStaticEditorFlags(feld.gameObject, StaticEditorFlags.NavigationStatic);
                }
                prev = p;
                luecke = false;
            }
            netz.Fertig(name + " (Holz)", holz);
        }

        /// <summary>Offenes Dach auf vier Pfosten (Unterstand, Winddach). kipp = Neigung um die Längsachse in Grad (schiefes Dach).</summary>
        private static void Unterstand(Terrain t, Transform parent, string name, Vector3 p, float b, float tt, float h, float kipp, float yaw,
                                       Material dach, Material holz)
        {
            var g = new GameObject(name).transform; g.SetParent(parent, false);
            float y = Boden(t, p.x, p.z);
            g.SetPositionAndRotation(new Vector3(p.x, y, p.z), Quaternion.Euler(0f, yaw, 0f));
            foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            {
                Vector3 w = g.TransformPoint(new Vector3(sx * b * 0.45f, 0f, sz * tt * 0.45f));
                float py = Boden(t, w.x, w.z);
                float top = y + h - Mathf.Tan(kipp * Mathf.Deg2Rad) * sz * tt * 0.45f;   // Euler(+x) senkt +z
                var post = GameObject.CreatePrimitive(PrimitiveType.Cube); post.name = "Pfosten"; post.transform.SetParent(g, true);
                post.transform.position = new Vector3(w.x, (py - 0.5f + top) * 0.5f, w.z);
                post.transform.rotation = g.rotation;
                post.transform.localScale = new Vector3(0.3f, top - py + 0.5f, 0.3f);
                post.GetComponent<Renderer>().sharedMaterial = holz;
            }
            var d = new GameObject("Dach"); d.transform.SetParent(g, false);
            d.transform.localPosition = new Vector3(0f, h, 0f); d.transform.localRotation = Quaternion.Euler(kipp, 0f, 0f);
            d.AddComponent<MeshFilter>().sharedMesh = Satteldach(b + 1f, tt + 1.2f, tt * 0.35f);
            d.AddComponent<MeshRenderer>().sharedMaterial = dach;
            d.AddComponent<MeshCollider>();
        }

        /// <summary>Firstzelt direkt am Boden (Tuch); geteiltes Satteldach-Netz.</summary>
        private static void Zelt(Terrain t, Transform parent, string name, Vector3 p, float b, float tt, float h, float yaw, Material tuch)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false);
            g.transform.SetPositionAndRotation(new Vector3(p.x, Boden(t, p.x, p.z) - 0.1f, p.z), Quaternion.Euler(0f, yaw, 0f));
            g.AddComponent<MeshFilter>().sharedMesh = Satteldach(b, tt, h, b, tt);
            g.AddComponent<MeshRenderer>().sharedMaterials = new[] { tuch, tuch };
            g.AddComponent<MeshCollider>();
        }

        /// <summary>Hecke als Greybox-Körper (1,6 m breit, 2,4 m hoch) zwischen zwei Punkten, dem Gelände folgend in 10-m-Stücken.</summary>
        private static void Hecke(Terrain t, Transform parent, string name, Vector3 a, Vector3 b, Material m)
        {
            var g = new GameObject(name).transform; g.SetParent(parent, false);
            float len = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
            int n = Mathf.Max(1, Mathf.CeilToInt(len / 10f));
            Vector3 dir = (b - a); dir.y = 0f;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, (i + 0.5f) / n);
                if (WegAbstand(p.x, p.z) < len / n * 0.5f + 1.0f) continue;   // keine Hecke auf dem Weg
                Kiste(t, g, "Heckenstück", p, new Vector3(1.6f, 2.4f + 0.3f * Mathf.Sin(i * 1.7f), len / n + 0.4f), yaw, m, 1.0f);
            }
        }

        /// <summary>Kornwagen mit gebrochenem Rad: Kasten, drei Räder, ein Rad liegt, Kornsäcke daneben.</summary>
        private static void Wagen(Terrain t, Transform parent, Vector3 p, float yaw, Material holz, Material tuch)
        {
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            float y = Boden(t, p.x, p.z);
            var kasten = GameObject.CreatePrimitive(PrimitiveType.Cube); kasten.name = "Wagenkasten"; kasten.transform.SetParent(parent, false);
            kasten.transform.SetPositionAndRotation(new Vector3(p.x, y + 1.1f, p.z), q * Quaternion.Euler(0f, 0f, -8f));
            kasten.transform.localScale = new Vector3(2.0f, 0.9f, 4.5f);
            kasten.GetComponent<Renderer>().sharedMaterial = holz;
            foreach (var (sx, sz, liegt) in new[] { (1f, 1.4f, false), (1f, -1.4f, false), (-1f, 1.4f, false), (-1f, -1.4f, true) })
            {
                var r = GameObject.CreatePrimitive(PrimitiveType.Cylinder); r.name = liegt ? "Rad (gebrochen)" : "Rad"; r.transform.SetParent(parent, false);
                Vector3 rp = p + q * new Vector3(sx * (liegt ? 2.6f : 1.1f), 0f, sz);
                float ry = Boden(t, rp.x, rp.z);
                r.transform.SetPositionAndRotation(liegt ? new Vector3(rp.x, ry + 0.06f, rp.z) : new Vector3(rp.x, ry + 0.6f - (sx < 0 ? 0.25f : 0f), rp.z),
                                                   liegt ? q : q * Quaternion.Euler(0f, 0f, 90f));
                r.transform.localScale = new Vector3(1.2f, 0.06f, 1.2f);
                r.GetComponent<Renderer>().sharedMaterial = holz;
                Object.DestroyImmediate(r.GetComponent<Collider>());
            }
            for (int i = 0; i < 4; i++)
                Kiste(t, parent, "Kornsack", p + q * new Vector3(-2.2f - (i % 2) * 0.7f, 0f, -0.8f + i * 0.6f), new Vector3(0.6f, 0.5f, 0.9f), yaw + i * 17f, tuch, 0.25f);
        }

        private static void Koppel(Terrain t, Transform parent, string name, Vector3 mitte, float b, float tt, float yaw, Material m)
        {
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            Vector3[] ecken =
            {
                mitte + q * new Vector3(-b / 2, 0, -tt / 2), mitte + q * new Vector3(b / 2, 0, -tt / 2),
                mitte + q * new Vector3(b / 2, 0, tt / 2), mitte + q * new Vector3(-b / 2, 0, tt / 2),
            };
            var g = new GameObject(name).transform; g.SetParent(parent, false);
            for (int i = 0; i < 4; i++)
            {
                if (i == 0) continue;   // Tor: eine Seite offen (Greybox)
                Zaun(t, g, name + " Seite " + i, ecken[i], ecken[(i + 1) % 4], m);
            }
        }
    }
}
