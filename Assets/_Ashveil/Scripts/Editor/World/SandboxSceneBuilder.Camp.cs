using System.Collections.Generic;
using Ashveil.World;
using UnityEngine;
using L = Ashveil.EditorTools.StartCampLayout;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Startlager „Wachtfeuer“ unter dem Kopf des versteinerten Beschuetzers (Bauplan: <see cref="StartCampLayout"/>).
    /// Uebersichtlich um den offenen Feuerplatz; ein Bereich je Himmelsrichtung:
    ///   Norden  – Haupttor mit zwei Wachtuermen, Kartograf, Wegweiser draussen
    ///   Nordost – Markt (Kraeuterin Maelis, Pfeilmacher Gerrit, Proviant), dahinter der Aussichtsfels
    ///   Osten   – Osttor (Aschenfeld)
    ///   Suedost – Trainingsplatz (Waffenmeisterin Ilse), Schiessscheiben; am Fels Pferch und Wagen
    ///   Sueden  – Vorplatz und Rampe zur Halle des Hauptmanns; der Fels der Brust schliesst das Lager ab
    ///   Suedwest– Kochstelle mit Langtischen, Vorraete am Fels, Suedwesttor (Salzweg, Testparcours)
    ///   Westen  – Schmiede (Dorn), Holzplatz, Werkbank, Westtor (Kiefernhang)
    ///   Nordwest– Zelte um eine kleine Feuerstelle (Schlafplaetze fuer die Tagesablaeufe)
    /// Laternen an Toren und Wegen leuchten nachts kraeftiger (LightFlicker.NightBoost).
    /// </summary>
    public static partial class SandboxSceneBuilder
    {
        private static Transform _campRoot;

        private static Vector3 W(Vector2 p, float y = 0f) => new Vector3(p.x, y, p.y);

        private static float YawTowardFire(Vector2 p) => Quaternion.LookRotation(new Vector3(-p.x, 0f, -p.y)).eulerAngles.y;

        private static void BuildCamp(Transform env)
        {
            Transform camp = K.Group("Wachtfeuer (Spawn)", env);
            ClearRocksInCamp();

            BuildCampFire(camp);
            BuildPalisade(K.Group("Palisade", camp));
            BuildGatesAndTowers(K.Group("Tore und Tuerme", camp));

            // NPCs mit Tagesablaeufen (M8.3) baut BuildCampNpcs – erst nach dem Animator (der wird beim Spieleraufbau neu erzeugt)
            _campRoot = camp;

            BuildMarket(K.Group("Markt", camp));
            BuildLookout(camp, W(L.Lookout)); // Auftrag 3 – steht in der Ostwand
            BuildForgeYard(K.Group("Schmiede und Holzplatz", camp));
            BuildTents(K.Group("Zelte", camp));
            BuildKitchen(K.Group("Kochstelle", camp));
            BuildTraining(K.Group("Trainingsplatz", camp));
            BuildPen(K.Group("Pferch und Wagen", camp));
            BuildCampAnimals(camp); // M8.4: Katze, Hund, Ziegen, Maultier
            BuildCampScenes(camp);  // M8.5: Lazarett, Schreibpult, Palisade in Arbeit, Handkarren
            BuildLanterns(K.Group("Laternen", camp));

            // --- Anschlagbrett (Gruppensuche, Nebenauftraege) am Feuerplatz
            Vector3 bp = new Vector3(-6.5f, 0f, 6.5f);
            Transform board = K.Group("Anschlagbrett", camp, bp, new Vector3(0f, YawTowardFire(new Vector2(bp.x, bp.z)) + 180f, 0f));
            K.Block("Post L", board, new Vector3(-1f, 0f, 0f), new Vector3(0.15f, 2.4f, 0.15f), _wood);
            K.Block("Post R", board, new Vector3(1f, 0f, 0f), new Vector3(0.15f, 2.4f, 0.15f), _wood);
            K.Box("Board", board, new Vector3(0f, 1.6f, 0f), new Vector3(2.2f, 1.1f, 0.1f), _woodDark);
            K.Box("Dach", board, new Vector3(0f, 2.5f, 0f), new Vector3(2.6f, 0.08f, 0.6f), _woodDark, new Vector3(12f, 0f, 0f), false);
            K.Label(board, new Vector3(0f, 2.9f, 0f), "Anschlagbrett", "Gruppensuche & Auftraege", WorldLabelStyle.Place, 30f, 0.8f);

            // --- Wegweiser draussen vor dem Nordtor
            BuildSignpost(camp, new Vector3(4.5f, 0f, L.OnArc(L.GateNorth, 4f).y)); // seitlich, der Blick durchs Tor bleibt frei

            // --- Spawnpunkt
            Transform spawn = K.Group("PlayerSpawn", camp, SpawnPosition, Vector3.zero);
            _playerSpawn = spawn;
            K.Box("Spawn Stone", spawn, new Vector3(0f, -0.03f, 0f), new Vector3(2.2f, 0.08f, 2.2f), _stone, new Vector3(0f, 45f, 0f), false);
        }

        /// <summary>Zufaellig gestreute Felsbrocken aus dem Gelaendeaufbau im (vergroesserten) Lager entfernen.</summary>
        private static void ClearRocksInCamp()
        {
            int removed = 0;
            foreach (ArtSlot slot in Object.FindObjectsByType<ArtSlot>())
            {
                if (!slot.SlotName.StartsWith("SM_Rock")) continue;
                Vector3 p = slot.transform.position;
                if (!L.Inside(p.x, p.z, -4f)) continue;
                Object.DestroyImmediate(slot.gameObject);
                removed++;
            }
            if (removed > 0) Debug.Log($"[Ashveil] Lager: {removed} Felsbrocken aus der Lagerflaeche entfernt.");
        }

        // ------------------------------------------------------------------ Feuerplatz

        private static void BuildCampFire(Transform camp)
        {
            Transform fire = K.Group("Campfire", camp);
            if (ArtModels.Place("SM_Campfire", fire, Vector3.zero, 0f, 1f, ArtModels.ColliderKind.Mesh) == null)
            {
                for (int i = 0; i < 9; i++)
                {
                    float ang = i * 40f;
                    K.Box("Stone", fire, OnRing(ang, 1.5f, 0.2f), new Vector3(0.6f, 0.4f, 0.5f), _stoneDark, new Vector3(0f, ang + 10f, 0f));
                }
                for (int i = 0; i < 3; i++)
                    K.Prim(PrimitiveType.Cylinder, "Log", fire, new Vector3(0f, 0.25f, 0f), new Vector3(0.22f, 0.8f, 0.22f),
                        new Vector3(75f, i * 60f, 0f), _woodDark, false);
            }
            K.Prim(PrimitiveType.Sphere, "Embers", fire, new Vector3(0f, 0.25f, 0f), new Vector3(1.1f, 0.35f, 1.1f), Vector3.zero, _fire, false);
            K.PointLight("Fire Light", fire, new Vector3(0f, 1.4f, 0f), new Color(1f, 0.55f, 0.22f), 18f, 4f, true, true);
            K.Particles("Flames", fire, new Vector3(0f, 0.3f, 0f), new Color(1f, 0.55f, 0.2f, 0.9f), 0.7f, 28f, 1.4f, 1.0f, 12f, 0.35f);
            K.Particles("Smoke", fire, new Vector3(0f, 1.6f, 0f), new Color(0.35f, 0.35f, 0.37f, 0.45f), 1.6f, 6f, 1.2f, 6f, 8f, 0.3f);

            // Sechs Sitzstaemme im Kreis – Platz fuer Spieler und NPCs am Abend
            for (int i = 0; i < 6; i++)
            {
                float ang = 30f + i * 60f;
                if (ArtModels.Place("SM_Log", camp, OnRing(ang, 3.8f), ang, 0.8f, ArtModels.ColliderKind.Capsule) != null) continue;
                K.Prim(PrimitiveType.Cylinder, "Bench Log", camp, OnRing(ang, 3.8f, 0.3f), new Vector3(0.6f, 1.2f, 0.6f),
                    new Vector3(90f, ang + 90f, 0f), _wood);
            }
        }

        // ------------------------------------------------------------------ Palisade

        private static void Stake(Transform parent, Vector3 pos, float yaw, float scale = 1f)
        {
            if (ArtModels.Place("SM_Palisade", parent, pos, yaw, scale, ArtModels.ColliderKind.Box) != null) return;
            K.Block("Stakes", parent, pos, new Vector3(4.7f * scale, 2.8f, 0.45f), _woodDark, yaw);
        }

        private static void BuildPalisade(Transform palisade)
        {
            const float piece = 4.5f;
            float gateHalfDeg = 3.4f / L.Radius * Mathf.Rad2Deg, northHalfDeg = 4.2f / L.Radius * Mathf.Rad2Deg;
            float lookoutDeg = Mathf.Atan2(L.Lookout.x - L.Center.x, L.Lookout.y - L.Center.y) * Mathf.Rad2Deg;
            var cuts = new List<(float a, float b)>
            {
                (L.GateNorth - northHalfDeg, L.GateNorth + northHalfDeg),
                (L.GateEast - gateHalfDeg, L.GateEast + gateHalfDeg),
                (Mathf.DeltaAngle(0f, L.GateWest) - gateHalfDeg, Mathf.DeltaAngle(0f, L.GateWest) + gateHalfDeg),
                (lookoutDeg - 8.5f, lookoutDeg + 8.5f) // der Aussichtsfels bildet hier die Wand
            };
            cuts.Sort((x, y) => x.a.CompareTo(y.a));
            float start = -L.ArcHalf;
            var spans = new List<(float a, float b)>();
            foreach ((float a, float b) in cuts)
            {
                if (a > start) spans.Add((start, a));
                start = Mathf.Max(start, b);
            }
            spans.Add((start, L.ArcHalf));
            int count = 0;
            foreach ((float a, float b) in spans)
            {
                float len = (b - a) * Mathf.Deg2Rad * L.Radius;
                int n = Mathf.Max(1, Mathf.CeilToInt(len / piece));
                float step = (b - a) / n;
                for (int i = 0; i < n; i++)
                {
                    float ang = a + (i + 0.5f) * step;
                    Vector2 p = L.OnArc(ang);
                    Stake(palisade, W(p), ang);
                    count++;
                }
            }

            // Gerade Waende vom Bogen hinunter zum Fels der Brust (West mit Suedwesttor)
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 top = L.OnArc(side * L.ArcHalf);
                Vector2 bottom = new Vector2(side * L.SouthEndEast.x, L.SouthEndEast.y);
                Vector2 d = bottom - top;
                float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg + (side > 0 ? -90f : 90f);
                int n = Mathf.CeilToInt(d.magnitude / piece);
                Vector2 gate = L.WestWallAt(L.GateSouthWestZ);
                for (int i = 0; i < n; i++)
                {
                    Vector2 p = top + d * ((i + 0.5f) / n);
                    if (side < 0 && Vector2.Distance(p, gate) < 3.6f) continue;         // Suedwesttor
                    if (GiantBuilder.Blocks(p.x, p.y, 0.5f)) continue;                    // hier ist schon Fels
                    Stake(palisade, W(p), yaw);
                    count++;
                }
                // Anschluss an den Fels: Geroell aus Brocken und gebrochenen Bloecken
                Transform rubble = K.Group(side > 0 ? "Geroell Ost" : "Geroell West", palisade, W(bottom - d.normalized * 2.5f), Vector3.zero);
                for (int i = 0; i < 4; i++)
                {
                    Vector3 o = new Vector3(side * (i % 2) * 1.6f, 0f, -i * 1.4f);
                    float s = 1.6f + i * 0.35f;
                    if (ArtModels.Place(i % 2 == 0 ? "SM_Rock_B" : "SM_Rock_C", rubble, o, i * 67f, s, ArtModels.ColliderKind.Mesh) == null)
                        K.Block("Brocken", rubble, o, new Vector3(1.8f, 1.4f, 1.6f) * (s * 0.7f), _stoneDark, i * 31f);
                }
            }

            // Ohne Riesen: das Lager im Sueden mit Palisade schliessen (Tor zur Rampe)
            if (!GiantBuilder.Active)
            {
                Vector2 a = new Vector2(-L.SouthEndEast.x, L.SouthEndEast.y), b = L.SouthEndEast;
                int n = Mathf.CeilToInt((b - a).magnitude / piece);
                for (int i = 0; i < n; i++)
                {
                    Vector2 p = Vector2.Lerp(a, b, (i + 0.5f) / n);
                    if (Mathf.Abs(p.x) < 4f) continue;
                    Stake(palisade, W(p), 180f);
                    count++;
                }
            }
            Debug.Log($"[Ashveil] Lager: Palisade aus {count} Teilen (r = {L.Radius} m, Felswand im Sueden).");
        }

        // ------------------------------------------------------------------ Tore, Tuerme

        private static void GateArch(Transform parent, string name, Vector2 a, Vector2 b, string label)
        {
            Vector2 mid = (a + b) * 0.5f, d = b - a;
            float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg + 90f;
            Transform g = K.Group(name, parent, W(mid), new Vector3(0f, yaw, 0f));
            float half = d.magnitude * 0.5f;
            for (int s = -1; s <= 1; s += 2)
                K.Block("Torpfosten", g, new Vector3(s * half, 0f, 0f), new Vector3(0.5f, 4.8f, 0.5f), _woodDark);
            K.Box("Querbalken", g, new Vector3(0f, 4.55f, 0f), new Vector3(d.magnitude + 1.2f, 0.45f, 0.45f), _woodDark);
            if (!string.IsNullOrEmpty(label))
            {
                K.Box("Schild", g, new Vector3(0f, 3.95f, 0.3f), new Vector3(2.6f, 0.7f, 0.08f), _wood, default, false);
                K.Label(g, new Vector3(0f, 5.6f, 0f), label, "", WorldLabelStyle.Place, 70f, 1f);
            }
        }

        private static void Watchtower(Transform parent, string name, Vector2 pos, float yaw)
        {
            Transform t = K.Group(name, parent, W(pos), new Vector3(0f, yaw, 0f));
            const float h = 4.4f, half = 1.4f;
            for (int ix = -1; ix <= 1; ix += 2)
            for (int iz = -1; iz <= 1; iz += 2)
                K.Block("Pfosten", t, new Vector3(ix * half, 0f, iz * half), new Vector3(0.3f, h + 2.2f, 0.3f), _woodDark);
            K.Box("Plattform", t, new Vector3(0f, h, 0f), new Vector3(half * 2f + 0.5f, 0.2f, half * 2f + 0.5f), _wood);
            for (int s = 0; s < 4; s++)
            {
                Quaternion r = Quaternion.Euler(0f, s * 90f, 0f);
                K.Box("Bruestung", t, r * new Vector3(0f, h + 0.55f, half + 0.2f), new Vector3(half * 2f + 0.5f, 0.9f, 0.1f), _woodDark, new Vector3(0f, s * 90f, 0f), false);
            }
            K.Box("Dach", t, new Vector3(0f, h + 2.3f, 0f), new Vector3(half * 2f + 1.1f, 0.14f, half * 2f + 1.1f), _woodDark, new Vector3(0f, 0f, 6f), false);
            // Leiter auf der Lagerseite (nur Optik)
            for (int s = -1; s <= 1; s += 2)
                K.Box("Leiterholm", t, new Vector3(s * 0.3f, h * 0.5f, -half - 0.45f), new Vector3(0.08f, h + 0.2f, 0.08f), _wood, new Vector3(-12f, 0f, 0f), false);
            for (int i = 0; i < 9; i++)
                K.Box("Sprosse", t, new Vector3(0f, 0.35f + i * 0.46f, -half - 0.45f + (h * 0.5f - (0.35f + i * 0.46f)) * 0.21f), new Vector3(0.62f, 0.06f, 0.06f), _wood, default, false);
            K.PointLight("Turmlaterne", t, new Vector3(0f, h + 1.4f, 0f), new Color(1f, 0.6f, 0.28f), 9f, 1.3f, true);
        }

        private static void BuildGatesAndTowers(Transform parent)
        {
            float north = 4.2f / L.Radius * Mathf.Rad2Deg, gate = 3.4f / L.Radius * Mathf.Rad2Deg;
            GateArch(parent, "Nordtor", L.OnArc(-north), L.OnArc(north), "Wachtfeuer");
            GateArch(parent, "Osttor", L.OnArc(L.GateEast - gate), L.OnArc(L.GateEast + gate), null);
            GateArch(parent, "Westtor", L.OnArc(L.GateWest - gate), L.OnArc(L.GateWest + gate), null);
            GateArch(parent, "Suedwesttor", L.WestWallAt(L.GateSouthWestZ + 3.4f), L.WestWallAt(L.GateSouthWestZ - 3.4f), null);

            // Zwei Tuerme am Haupttor, zwei an den Ecken zum Fels (knapp innerhalb der Palisade)
            // lokal +z zeigt nach aussen, die Leiter (−z) ins Lager
            Watchtower(parent, "Wachturm Nordwest", L.OnArc(-11.5f, -2.4f), -11.5f);
            Watchtower(parent, "Wachturm Nordost", L.OnArc(11.5f, -2.4f), 11.5f);
            Watchtower(parent, "Wachturm Suedost", L.OnArc(L.ArcHalf, -2.6f), L.ArcHalf);
            Watchtower(parent, "Wachturm Suedwest", L.OnArc(-L.ArcHalf, -2.6f), -L.ArcHalf);
        }

        // ------------------------------------------------------------------ Markt

        private static void BuildMarket(Transform m)
        {
            Stall(m, "Stand Kraeuter", OnRing(28f, 15f), 28f, K.Mat("Cloth_Green", new Color(0.25f, 0.4f, 0.28f)));
            Stall(m, "Stand Munition", OnRing(50f, 15f), 50f, K.Mat("Cloth_Red", new Color(0.5f, 0.2f, 0.16f)));
            Stall(m, "Stand Proviant", OnRing(72f, 15f), 72f, K.Mat("Cloth_Ochre", new Color(0.6f, 0.46f, 0.2f)));
            // Ware: Kraeuterbuendel, Pulverfaesser, Saecke und Koerbe
            Material herb = K.Mat("Herb_Green", new Color(0.3f, 0.45f, 0.24f));
            for (int i = 0; i < 4; i++)
                K.Box("Kraeuterbuendel", m, OnRing(28f - 3f + i * 2f, 15.1f, 1.08f), new Vector3(0.25f, 0.18f, 0.35f), herb, new Vector3(0f, 28f + i * 20f, 0f), false);
            for (int i = 0; i < 3; i++)
                K.Prim(PrimitiveType.Cylinder, "Pulverfass", m, OnRing(50f + 6f + i * 1.6f, 16.6f, 0.4f), new Vector3(0.55f, 0.4f, 0.55f), Vector3.zero, _woodDark);
            for (int i = 0; i < 5; i++)
                K.Block("Sack", m, OnRing(72f - 5f + i * 2.2f, 16.8f), new Vector3(0.6f, 0.7f, 0.45f), _canvas, 72f + i * 23f);
            K.Label(m, W(L.Market, 4.2f) + new Vector3(3f, 0f, 3f), "Markt", "", WorldLabelStyle.Place, 40f, 0.85f);
        }

        // ------------------------------------------------------------------ Schmiede, Holzplatz, Werkbank

        private static void BuildForgeYard(Transform y)
        {
            float yaw = Mathf.Atan2(L.Forge.x, L.Forge.y) * Mathf.Rad2Deg; // lokal +z zeigt vom Feuer weg
            Transform forge = K.Group("Schmiede", y, W(L.Forge), new Vector3(0f, yaw, 0f));
            K.Block("Esse", forge, new Vector3(0f, 0f, 2.2f), new Vector3(2.4f, 1.4f, 1.8f), _stoneDark);
            K.Box("Glut", forge, new Vector3(0f, 1.42f, 2.2f), new Vector3(1.6f, 0.06f, 1.1f), _emberGlow, default, false);
            K.Block("Rauchfang", forge, new Vector3(0f, 1.4f, 2.7f), new Vector3(1.2f, 2.6f, 0.8f), _stoneDark);
            K.PointLight("Forge Light", forge, new Vector3(0f, 2f, 1.6f), new Color(1f, 0.45f, 0.15f), 9f, 2.5f, true);
            K.Block("Anvil Base", forge, new Vector3(0f, 0f, -0.4f), new Vector3(0.5f, 0.6f, 0.5f), _woodDark);
            K.Block("Anvil", forge, new Vector3(0f, 0.6f, -0.4f), new Vector3(0.45f, 0.35f, 1.0f), _metal);
            K.Block("Waffenstaender", forge, new Vector3(2.4f, 0f, 1.0f), new Vector3(0.2f, 1.8f, 2.2f), _wood);
            K.Prim(PrimitiveType.Cylinder, "Loeschfass", forge, new Vector3(-1.9f, 0.45f, 1.4f), new Vector3(0.8f, 0.45f, 0.8f), Vector3.zero, _woodDark);
            K.Prim(PrimitiveType.Cylinder, "Schleifstein", forge, new Vector3(-2.2f, 0.75f, -0.9f), new Vector3(0.9f, 0.1f, 0.9f), new Vector3(0f, 0f, 90f), _stone);
            // Unterstand: vier Pfosten, schraeges Dach
            for (int ix = -1; ix <= 1; ix += 2)
            for (int iz = 0; iz <= 1; iz++)
                K.Block("Pfosten", forge, new Vector3(ix * 2.8f, 0f, iz == 0 ? -1.4f : 3.4f), new Vector3(0.25f, iz == 0 ? 3.2f : 2.7f, 0.25f), _woodDark);
            K.Box("Dach", forge, new Vector3(0f, 3.05f, 1.0f), new Vector3(6.4f, 0.14f, 5.6f), _woodDark, new Vector3(-6f, 0f, 0f), false);
            K.Label(forge, new Vector3(0f, 4.3f, 0.5f), "Schmiede", "", WorldLabelStyle.Place, 35f, 0.85f);

            // Holzplatz (spaeter: NPC hackt Holz)
            Transform wood = K.Group("Holzplatz", y, new Vector3(-21f, 0f, -11f), new Vector3(0f, 35f, 0f));
            K.Prim(PrimitiveType.Cylinder, "Hackklotz", wood, new Vector3(0f, 0.35f, 0f), new Vector3(0.75f, 0.35f, 0.75f), Vector3.zero, _wood);
            for (int i = 0; i < 9; i++)
            {
                int row = i < 4 ? 0 : i < 7 ? 1 : 2;
                int col = row == 0 ? i : row == 1 ? i - 4 : i - 7;
                K.Prim(PrimitiveType.Cylinder, "Scheit", wood, new Vector3(-2.6f + col * 0.42f + row * 0.21f, 0.2f + row * 0.36f, 1.4f),
                    new Vector3(0.38f, 0.8f, 0.38f), new Vector3(90f, 0f, 0f), i % 2 == 0 ? _wood : _woodDark, row == 0);
            }
            K.Block("Saegebock", wood, new Vector3(1.6f, 0f, 1.2f), new Vector3(0.3f, 0.9f, 1.6f), _woodDark, 10f);

            // Werkbank und Gerberrahmen
            Transform bench = K.Group("Werkbank", y, new Vector3(-19f, 0f, 9.5f), new Vector3(0f, 120f, 0f));
            K.Block("Tisch", bench, Vector3.zero, new Vector3(2.4f, 0.9f, 0.9f), _wood);
            K.Box("Werkzeug", bench, new Vector3(0.4f, 0.95f, 0f), new Vector3(0.5f, 0.06f, 0.2f), _metal, default, false);
            for (int s = -1; s <= 1; s += 2)
                K.Block("Rahmen", bench, new Vector3(s * 1.0f, 0f, 1.6f), new Vector3(0.12f, 2.2f, 0.12f), _woodDark);
            K.Box("Fell", bench, new Vector3(0f, 1.3f, 1.6f), new Vector3(1.7f, 1.2f, 0.05f), _canvas, default, false);
        }

        // ------------------------------------------------------------------ Zelte

        private static void BuildTents(Transform t)
        {
            float[] angles = { 20f, 90f, 160f, 230f, 300f };
            foreach (float ang in angles)
            {
                float a = ang * Mathf.Deg2Rad;
                Vector2 p = L.Tents + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * 5.2f;
                Transform tent = K.Group("Tent", t, W(p), new Vector3(0f, ang, 0f));
                if (ArtModels.Place("SM_Tent", tent, Vector3.zero, 0f, 1f, ArtModels.ColliderKind.Mesh) != null) continue;
                K.Box("Side L", tent, new Vector3(-0.95f, 1.1f, 0f), new Vector3(0.08f, 2.9f, 3.6f), _canvas, new Vector3(0f, 0f, -40f));
                K.Box("Side R", tent, new Vector3(0.95f, 1.1f, 0f), new Vector3(0.08f, 2.9f, 3.6f), _canvas, new Vector3(0f, 0f, 40f));
                K.Box("Back", tent, new Vector3(0f, 0.8f, 1.8f), new Vector3(2.2f, 1.6f, 0.06f), _canvas, default, false);
            }
            // Kleine Feuerstelle in der Mitte
            Transform f = K.Group("Feuerstelle", t, W(L.Tents), Vector3.zero);
            for (int i = 0; i < 7; i++)
                K.Box("Stein", f, OnRing(i * 51f, 0.75f, 0.12f), new Vector3(0.35f, 0.25f, 0.3f), _stoneDark, new Vector3(0f, i * 51f, 0f), false);
            K.Prim(PrimitiveType.Sphere, "Glut", f, new Vector3(0f, 0.1f, 0f), new Vector3(0.7f, 0.2f, 0.7f), Vector3.zero, _fire, false);
            K.PointLight("Licht", f, new Vector3(0f, 0.9f, 0f), new Color(1f, 0.55f, 0.22f), 7f, 1.5f, true);
            K.Particles("Flammen", f, new Vector3(0f, 0.15f, 0f), new Color(1f, 0.55f, 0.2f, 0.85f), 0.4f, 12f, 0.8f, 0.6f, 8f, 0.18f);
        }

        // ------------------------------------------------------------------ Kochstelle, Langtische, Vorraete

        private static void BuildKitchen(Transform k)
        {
            Transform cook = K.Group("Kochfeuer", k, new Vector3(-9f, 0f, -21f), Vector3.zero);
            for (int i = 0; i < 8; i++)
                K.Box("Stein", cook, OnRing(i * 45f, 0.8f, 0.14f), new Vector3(0.4f, 0.28f, 0.34f), _stoneDark, new Vector3(0f, i * 45f, 0f));
            K.Prim(PrimitiveType.Sphere, "Glut", cook, new Vector3(0f, 0.12f, 0f), new Vector3(0.8f, 0.22f, 0.8f), Vector3.zero, _fire, false);
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f;
                K.Box("Dreibein", cook, OnRing(a, 0.55f, 1.0f), new Vector3(0.07f, 2.1f, 0.07f), _woodDark, new Vector3(-17f, a, 0f), false);
            }
            K.Prim(PrimitiveType.Sphere, "Kessel", cook, new Vector3(0f, 0.75f, 0f), new Vector3(0.75f, 0.55f, 0.75f), Vector3.zero, _metal, false);
            K.PointLight("Kochfeuer Licht", cook, new Vector3(0f, 1.1f, 0f), new Color(1f, 0.55f, 0.22f), 8f, 1.8f, true);
            K.Particles("Dampf", cook, new Vector3(0f, 1.1f, 0f), new Color(0.75f, 0.75f, 0.78f, 0.25f), 0.8f, 3f, 0.8f, 3f, 5f, 0.2f);

            for (int t = 0; t < 2; t++)
            {
                Transform table = K.Group("Langtisch", k, new Vector3(-15f, 0f, -25f - t * 5f), Vector3.zero);
                K.Box("Platte", table, new Vector3(0f, 0.8f, 0f), new Vector3(5.2f, 0.1f, 0.95f), _wood);
                for (int s = -1; s <= 1; s += 2)
                {
                    K.Block("Bock", table, new Vector3(s * 2.1f, 0f, 0f), new Vector3(0.12f, 0.75f, 0.8f), _woodDark);
                    K.Block("Bank", table, new Vector3(0f, 0.3f, s * 0.95f), new Vector3(4.8f, 0.14f, 0.32f), _wood);
                    K.Block("Bankbein", table, new Vector3(-2f, 0f, s * 0.95f), new Vector3(0.1f, 0.3f, 0.28f), _woodDark);
                    K.Block("Bankbein", table, new Vector3(2f, 0f, s * 0.95f), new Vector3(0.1f, 0.3f, 0.28f), _woodDark);
                }
                for (int i = 0; i < 3; i++)
                    K.Prim(PrimitiveType.Cylinder, "Becher", table, new Vector3(-1.5f + i * 1.4f, 0.93f, (i % 2 == 0 ? 0.2f : -0.2f)), new Vector3(0.12f, 0.08f, 0.12f), Vector3.zero, _woodDark, false);
            }
            // Grosse Vorratskiste – Lieblingsplatz der Katze
            K.Block("Vorratskiste", k, new Vector3(-18.2f, KarethTerrainBuilder.Height(-18.2f, -34.5f), -34.5f), new Vector3(1.2f, 0.72f, 0.9f), _wood, 15f);
            Crates(k, new Vector2(-20.5f, -37f), 8, 2.6f, 21);
        }

        // ------------------------------------------------------------------ Trainingsplatz

        private static void BuildTraining(Transform tr)
        {
            Transform training = K.Group("Platz", tr, W(L.Training), Vector3.zero);
            for (int i = 0; i < 3; i++)
            {
                Transform dummy = K.Group("Training Dummy", training, new Vector3(-3f + i * 3f, 0f, 1.5f), new Vector3(0f, 180f, 0f));
                Transform dv = K.Group("Visual", dummy);
                K.Prim(PrimitiveType.Cylinder, "Post", dv, new Vector3(0f, 0.9f, 0f), new Vector3(0.15f, 0.9f, 0.15f), Vector3.zero, _wood);
                K.Prim(PrimitiveType.Cylinder, "Body", dv, new Vector3(0f, 1.3f, 0f), new Vector3(0.5f, 0.45f, 0.5f), Vector3.zero, _straw);
                K.Box("Arms", dv, new Vector3(0f, 1.45f, 0f), new Vector3(1.4f, 0.12f, 0.12f), _wood);
                K.Prim(PrimitiveType.Sphere, "Head", dv, new Vector3(0f, 1.95f, 0f), new Vector3(0.38f, 0.38f, 0.38f), Vector3.zero, _straw);
                PlayerRigBuilder.SetupDummy(dummy, dv, i + 1);
            }
            // Schiessscheiben am Ostrand, Blick nach Westen
            Material red = K.Mat("Cloth_Red", new Color(0.5f, 0.2f, 0.16f));
            for (int i = 0; i < 2; i++)
            {
                Transform target = K.Group("Schiessscheibe", training, new Vector3(7.5f, 0f, -3.5f + i * 4f), new Vector3(0f, 270f, 0f));
                K.Block("Bein L", target, new Vector3(-0.5f, 0f, 0.3f), new Vector3(0.1f, 1.6f, 0.1f), _woodDark);
                K.Block("Bein R", target, new Vector3(0.5f, 0f, 0.3f), new Vector3(0.1f, 1.6f, 0.1f), _woodDark);
                K.Prim(PrimitiveType.Cylinder, "Scheibe", target, new Vector3(0f, 1.35f, 0f), new Vector3(1.2f, 0.06f, 1.2f), new Vector3(80f, 0f, 0f), _straw);
                K.Prim(PrimitiveType.Cylinder, "Mitte", target, new Vector3(0f, 1.35f, -0.07f), new Vector3(0.4f, 0.02f, 0.4f), new Vector3(80f, 0f, 0f), red, false);
            }
            K.Block("Waffenstaender", training, new Vector3(-6f, 0f, -2.5f), new Vector3(2.2f, 1.7f, 0.25f), _wood, 90f);
            K.Label(training, new Vector3(0f, 3.2f, 1.5f), "Trainingsplatz", "", WorldLabelStyle.Place, 35f, 0.85f);
        }

        // ------------------------------------------------------------------ Pferch und Wagen (spaeter: Reisende, Tiere)

        private static void BuildPen(Transform p)
        {
            Transform pen = K.Group("Pferch", p, W(L.Pen), Vector3.zero);
            const float hx = 4.5f, hz = 3f;
            for (int ix = -2; ix <= 2; ix++)
            for (int iz = -1; iz <= 1; iz += 2)
            {
                if (iz > 0 && Mathf.Abs(ix) == 0) continue; // Gatter nach Norden
                K.Block("Pfosten", pen, new Vector3(ix * hx * 0.5f, 0f, iz * hz), new Vector3(0.18f, 1.3f, 0.18f), _woodDark);
            }
            for (int ix = -1; ix <= 1; ix += 2)
                K.Block("Pfosten", pen, new Vector3(ix * hx, 0f, 0f), new Vector3(0.18f, 1.3f, 0.18f), _woodDark);
            K.Box("Latte Sued", pen, new Vector3(0f, 0.95f, -hz), new Vector3(hx * 2f, 0.12f, 0.08f), _wood);
            K.Box("Latte West", pen, new Vector3(-hx, 0.95f, 0f), new Vector3(0.08f, 0.12f, hz * 2f), _wood);
            K.Box("Latte Ost", pen, new Vector3(hx, 0.95f, 0f), new Vector3(0.08f, 0.12f, hz * 2f), _wood);
            K.Box("Latte Nord L", pen, new Vector3(-hx * 0.62f, 0.95f, hz), new Vector3(hx * 0.76f, 0.12f, 0.08f), _wood);
            K.Box("Latte Nord R", pen, new Vector3(hx * 0.62f, 0.95f, hz), new Vector3(hx * 0.76f, 0.12f, 0.08f), _wood);
            for (int i = 0; i < 3; i++)
                K.Block("Heuballen", pen, new Vector3(-2.8f + i * 1.3f, 0f, -2f), new Vector3(1.1f, 0.7f, 0.8f), _straw, i * 8f);
            K.Block("Traenke", pen, new Vector3(2.8f, 0f, -2.2f), new Vector3(1.6f, 0.5f, 0.6f), _woodDark);

            // Wagen (Zugtiere und Fahrer folgen mit den Reisenden)
            Transform cart = K.Group("Wagen", p, new Vector3(10.5f, 0f, -33.5f), new Vector3(0f, 12f, 0f));
            K.Box("Ladeflaeche", cart, new Vector3(0f, 0.8f, 0f), new Vector3(1.6f, 0.14f, 2.8f), _wood);
            K.Box("Bord L", cart, new Vector3(-0.78f, 1.1f, 0f), new Vector3(0.06f, 0.5f, 2.8f), _woodDark, default, false);
            K.Box("Bord R", cart, new Vector3(0.78f, 1.1f, 0f), new Vector3(0.06f, 0.5f, 2.8f), _woodDark, default, false);
            for (int s = -1; s <= 1; s += 2)
                K.Prim(PrimitiveType.Cylinder, "Rad", cart, new Vector3(s * 0.92f, 0.55f, -0.4f), new Vector3(1.1f, 0.06f, 1.1f), new Vector3(0f, 0f, 90f), _woodDark);
            K.Box("Deichsel", cart, new Vector3(0f, 0.55f, 2.3f), new Vector3(0.1f, 0.1f, 2f), _wood, new Vector3(8f, 0f, 0f), false);
            K.Block("Ladung", cart, new Vector3(0f, 0.87f, -0.5f), new Vector3(1.2f, 0.6f, 1.1f), _canvas);
            K.Block("Stuetze", cart, new Vector3(0f, 0f, 1.4f), new Vector3(0.12f, 0.8f, 0.12f), _woodDark);
        }

        // ------------------------------------------------------------------ Laternen und Kisten

        private static void Lantern(Transform parent, Vector2 p)
        {
            Transform l = K.Group("Laterne", parent, W(p), Vector3.zero);
            K.Prim(PrimitiveType.Cylinder, "Pfahl", l, new Vector3(0f, 1.3f, 0f), new Vector3(0.14f, 1.3f, 0.14f), Vector3.zero, _woodDark);
            K.Box("Arm", l, new Vector3(0f, 2.5f, 0.25f), new Vector3(0.08f, 0.08f, 0.55f), _woodDark, default, false);
            K.Box("Laterne", l, new Vector3(0f, 2.28f, 0.48f), new Vector3(0.24f, 0.32f, 0.24f), _warmWindow, default, false);
            K.PointLight("Licht", l, new Vector3(0f, 2.2f, 0.48f), new Color(1f, 0.62f, 0.3f), 8f, 1.1f, true);
        }

        private static void BuildLanterns(Transform parent)
        {
            // Feuerplatz (zwischen den Wegen)
            foreach (float a in new[] { 80f, 160f, 250f, 340f })
            {
                Vector3 r = OnRing(a, 10.5f);
                Lantern(parent, new Vector2(r.x, r.z));
            }
            // Tore (innen, beidseits)
            float gate = 3.4f / L.Radius * Mathf.Rad2Deg + 2.5f;
            Lantern(parent, L.OnArc(L.GateNorth - 6f, -4.5f)); // am Nordtor stehen daneben die Wachtuerme
            Lantern(parent, L.OnArc(L.GateNorth + 6f, -4.5f));
            foreach (float g in new[] { L.GateEast, L.GateWest })
            {
                Lantern(parent, L.OnArc(g - gate, -2.2f));
                Lantern(parent, L.OnArc(g + gate, -2.2f));
            }
            Lantern(parent, L.WestWallAt(L.GateSouthWestZ + 5f) + new Vector2(2.2f, 0f));
            Lantern(parent, L.WestWallAt(L.GateSouthWestZ - 5f) + new Vector2(2.2f, 0f));
            // Vorplatz der Halle, Markt, Pferch
            Lantern(parent, new Vector2(-5.5f, -23f));
            Lantern(parent, new Vector2(5.5f, -23f));
            Lantern(parent, new Vector2(17.5f, 15.5f));
            Lantern(parent, new Vector2(13.5f, -30f));
        }

        private static void Crates(Transform parent, Vector2 center, int count, float radius, int seed)
        {
            var rng = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = (float)rng.NextDouble() * radius;
                Vector3 p = W(center + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * d);
                float size = 0.7f + (float)rng.NextDouble() * 0.3f;
                float yaw = (float)rng.NextDouble() * 90f;
                if (i % 3 != 1)
                {
                    if (ArtModels.Place("SM_Crate", parent, p, yaw, size / 0.8f, ArtModels.ColliderKind.Box) == null)
                        K.Block("Crate", parent, p, Vector3.one * size, _wood, yaw);
                }
                else if (ArtModels.Place("SM_Barrel", parent, p, yaw, 1f, ArtModels.ColliderKind.Capsule) == null)
                    K.Prim(PrimitiveType.Cylinder, "Barrel", parent, p + Vector3.up * 0.5f, new Vector3(0.7f, 0.5f, 0.7f), Vector3.zero, _woodDark);
            }
        }
    }
}
