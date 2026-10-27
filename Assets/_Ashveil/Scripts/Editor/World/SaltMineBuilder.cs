using System.Collections.Generic;
using Ashveil.Core;
using Ashveil.Networking;
using Ashveil.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// M6 – Dungeon 1 „Die Salzgruben“ als Greybox (Design: Docs/Design/ashveil-salzgruben.md).
    ///
    /// Baut <see cref="SlotCount"/> identische Instanz-Plaetze weit ausserhalb von Kareth (x ab <see cref="SlotBaseX"/>),
    /// den Eingang (<see cref="DungeonPortal"/>) am Stollenmund der Oberwelt, den <see cref="DungeonDirector"/> und
    /// eine eigene, im Editor gebackene NavMesh-Flaeche fuer alle Plaetze. Raumkoordinaten sind lokal zum Platz
    /// und stehen als Konstanten hier – die Gegner (EnemyBuilder.SaltMines) benutzen dieselben Punkte.
    /// </summary>
    public static class SaltMineBuilder
    {
        public const string DungeonId = "salzgruben";
        public const int SlotCount = 6;
        public const float SlotBaseX = -26000f;   // = InstanceLayout.Origin.x (nur noch Dokumentation)
        public const float SlotSpacing = InstanceLayout.Spacing;
        public const string NavMeshAssetPath = ProjectSetup.Root + "/Scenes/Salzgruben_NavMesh.asset";

        // Raeume (Mittelpunkt auf Bodenhoehe, lokal) und Masse
        public static readonly Vector3 HallA = new Vector3(0f, 0f, 0f);           // Foerderhalle 18 x 18
        public static readonly Vector3 HallC = new Vector3(0f, 0f, 72f);          // Salzhalle 40 x 40 (Hask)
        public static readonly Vector3 GrottoD = new Vector3(85f, -8f, 80f);      // Salzsee-Grotte 44 x 44 (Salzmutter)
        public static readonly Vector3 WreckE = new Vector3(-80f, -6f, 66f);      // Wrackhoehle 40 x 36 (Kapitaen)
        public static readonly Vector3 HeartF = new Vector3(85f, -14f, 170f);     // Herzkaverne 50 x 50 (Grubenherz)
        public static readonly Vector3 LoreJunction = new Vector3(85f, -8f, 0f);  // Knick der Lorenbahn

        // Wichtige Punkte fuer Gegner und Tests
        public static readonly Vector3 Arrival = new Vector3(0f, 0f, -2f);
        public static readonly Vector3 Exit = new Vector3(0f, 0f, -6.5f);
        public static readonly Vector3 MainTunnelPack1 = new Vector3(0f, 0f, 22f);
        public static readonly Vector3 MainTunnelPack2 = new Vector3(0f, 0f, 40f);
        public static readonly Vector3 HallCGuards = new Vector3(0f, 0f, 60f);
        public static readonly Vector3 FloodTunnel = new Vector3(40f, -4f, 80f);
        public static readonly Vector3 GrottoEdge = new Vector3(72f, -8f, 95f);
        public static readonly Vector3 WreckTunnel = new Vector3(-40f, -3f, 66f);
        public static readonly Vector3 WreckCave = new Vector3(-72f, -6f, 58f);
        public static readonly Vector3 CrystalTunnel = new Vector3(85f, -11f, 124f);
        public static readonly Vector3 HeartEntry = new Vector3(85f, -14f, 152f);
        public static readonly Vector3 HaskSpot = new Vector3(0f, 0f, 80f);       // Hask vor dem Arbeitsgeruest
        public static readonly Vector3 HaskCallWest = new Vector3(-17f, 0f, 72f); // Arbeiter kommen aus dem Wrackstollen
        public static readonly Vector3 HaskCallEast = new Vector3(17f, 0f, 72f);  // ... und aus dem Flutstollen
        public static readonly Vector3 SaltMotherSpot = new Vector3(85f, -8f, 84f);   // Mitte des Salzsees
        public static readonly Vector3 BroodWest = new Vector3(73f, -8f, 80f);        // Krebsbrut schluepft am Seeufer
        public static readonly Vector3 BroodEast = new Vector3(97f, -8f, 80f);
        public static readonly Vector3 BroodNorth = new Vector3(85f, -8f, 96f);
        public static readonly Vector3 LoreGatePos = new Vector3(85f, -8f, 56.5f);    // Gitter am Anfang der Lorenbahn
        public static readonly Vector3 HeartSpot = new Vector3(85f, -14f, 176f);      // Grubenherz im Kristallkranz
        public static readonly Vector3 CaptainSpot = new Vector3(-70f, -6f, 74f);      // Kapitaen vor dem Wrack (optional)
        public static readonly Vector3 CrewNorth = new Vector3(-80f, -6f, 80f);        // Matrosen klettern aus dem Wrack
        public static readonly Vector3 CrewSouth = new Vector3(-88f, -6f, 62f);
        public static readonly Vector3 HeartEaters = new Vector3(85f, -14f, 182f);    // hinter dem Herzen kriechen die Lichtfresser heraus

        /// <summary>Alle Raummitten – Tests pruefen, dass jeder vom Eingang aus erreichbar ist.</summary>
        public static readonly (string name, Vector3 local)[] Rooms =
        {
            ("Foerderhalle", HallA), ("Salzhalle", HallC), ("Salzsee-Grotte", GrottoD), ("Wrackhoehle", WreckE),
            ("Herzkaverne", HeartF), ("Lorenbahn", LoreJunction)
        };

        public static readonly Vector3 BoundsCenter = new Vector3(8f, 0f, 95f);
        public static readonly Vector3 BoundsSize = new Vector3(240f, 70f, 230f);

        private static Material _rock, _rockDark, _salt, _saltGlow, _saltVein, _wood, _woodDark, _metal, _water, _lampGlass, _lampDark, _rail;

        public static Vector3 SlotOrigin(int index) => InstanceLayout.Slot(InstanceLayout.SaltMineRow, index);

        public static readonly List<Transform> Slots = new List<Transform>();

        // ================================================================== Einstieg

        /// <summary>Plaetze, Eingang und Direktor in die offene Szene bauen (vor den Gegnern und dem NavMesh).</summary>
        public static void Build(Transform env, Transform entranceGroup)
        {
            CreateMaterials();
            Slots.Clear();
            Transform root = K.Group("Salzgruben – Instanzen (M6)", env);
            for (int i = 0; i < SlotCount; i++)
            {
                Transform slot = K.Group($"Salzgruben Platz {i}", root, SlotOrigin(i), Vector3.zero);
                BuildSlot(slot, i);
                Slots.Add(slot);
            }

            // Eingang in der Oberwelt: vor dem dunklen Stollenmund, Rueckkehr ein paar Meter davor
            if (entranceGroup != null)
            {
                var portalGo = new GameObject("Eingang (Instanz)");
                portalGo.transform.SetParent(entranceGroup, false);
                portalGo.transform.localPosition = new Vector3(0f, 0f, -4.5f);
                portalGo.AddComponent<DungeonPortal>().Configure(DungeonId, "Salzgruben", 2.6f, new Vector3(0f, 0f, 8.5f), 0f);
            }

            var director = new GameObject("Dungeon-Direktor Salzgruben");
            director.transform.SetParent(root, false);
            director.AddComponent<DungeonDirector>();

            BuildNavMeshSurface(root);
        }

        // ================================================================== Ein Platz

        private static void BuildSlot(Transform slot, int index)
        {
            slot.gameObject.AddComponent<DungeonSlot>().Configure(DungeonId, index, BoundsCenter, BoundsSize, Arrival, 0f, Exit, 2.2f);
            Transform g = K.Group("Geometrie", slot);

            // A Foerderhalle (Ein-/Ausgang)
            Room(g, "A Foerderhalle", HallA, 18f, 18f, 9f, _salt, new[] { Door.N(0f, 9f, 7f), Door.E(0f, 8f, 6f) });
            Tunnel(g, "B Hauptstollen", HallA + new Vector3(0f, 0f, 9f), HallC + new Vector3(0f, 0f, -20f), 9f, 7f);
            // C Salzhalle (Hask)
            Room(g, "C Salzhalle", HallC, 40f, 40f, 14f, _salt,
                new[] { Door.S(0f, 9f, 7f), Door.E(8f, 8f, 7f), Door.W(-6f, 8f, 7f) });
            // Flutstollen -> D Salzsee-Grotte (Salzmutter)
            Tunnel(g, "Flutstollen", HallC + new Vector3(20f, 0f, 8f), GrottoD + new Vector3(-22f, 0f, 0f), 8f, 7f);
            Room(g, "D Salzsee-Grotte", GrottoD, 44f, 44f, 16f, _salt,
                new[] { Door.W(0f, 8f, 7f), Door.N(0f, 8f, 7f), Door.S(0f, 8f, 6f) });
            // Wrackstollen -> E Wrackhoehle (optional: Kapitaen)
            Tunnel(g, "Wrackstollen", HallC + new Vector3(-20f, 0f, -6f), WreckE + new Vector3(20f, 0f, 0f), 8f, 7f);
            Room(g, "E Wrackhoehle", WreckE, 40f, 36f, 15f, _rock, new[] { Door.E(0f, 8f, 7f) });
            // Kristallstollen -> F Herzkaverne (Grubenherz)
            Tunnel(g, "Kristallstollen", GrottoD + new Vector3(0f, 0f, 22f), HeartF + new Vector3(0f, 0f, -25f), 8f, 8f);
            Room(g, "F Herzkaverne", HeartF, 50f, 50f, 22f, _salt, new[] { Door.S(0f, 8f, 8f) });
            // Lorenbahn: Grotte (Sued) -> Knick -> Foerderhalle (Ost)
            Tunnel(g, "Lorenbahn Sued", GrottoD + new Vector3(0f, 0f, -22f), LoreJunction + new Vector3(0f, 0f, 5f), 8f, 6f);
            Room(g, "Lorenbahn Knick", LoreJunction, 10f, 10f, 6f, _rock, new[] { Door.N(0f, 8f, 6f), Door.W(0f, 8f, 6f) });
            Tunnel(g, "Lorenbahn West", LoreJunction + new Vector3(-5f, 0f, 0f), HallA + new Vector3(9f, 0f, 0f), 8f, 6f);

            Dress(slot, index);
        }

        // ================================================================== Ausstattung

        private static void Dress(Transform slot, int index)
        {
            Transform d = K.Group("Ausstattung", slot);
            Color lamp = new Color(1f, 0.72f, 0.42f);

            // Foerderkorb (Ausgang) an der Suedwand der Foerderhalle
            Transform cage = K.Group("Foerderkorb (Ausgang)", d, Exit, Vector3.zero);
            for (int i = 0; i < 4; i++)
                K.Block("Korb Pfosten", cage, new Vector3(i < 2 ? -1.6f : 1.6f, 0f, i % 2 == 0 ? -1.2f : 1.2f), new Vector3(0.25f, 3.2f, 0.25f), _metal);
            K.Block("Korb Dach", cage, new Vector3(0f, 3.2f, 0f), new Vector3(3.6f, 0.2f, 2.8f), _metal);
            K.Box("Seil", cage, new Vector3(0f, 6.5f, 0f), new Vector3(0.12f, 6.4f, 0.12f), _rail, default, false);
            K.PointLight("Korb Lampe", cage, new Vector3(0f, 2.8f, 1.6f), lamp, 14f, 28f, true);
            K.Label(d, Exit + new Vector3(0f, 4.2f, 0f), "Förderkorb", "Hinauf zum Stollenmund", WorldLabelStyle.Info, 25f);
            K.Label(d, HallA + new Vector3(0f, 6f, 6f), "Die Salzgruben", "Stufe 10–14 · Instanz " + (index + 1), WorldLabelStyle.Place, 40f);
            K.Block("Feuerstelle", d, HallA + new Vector3(-5f, 0f, 3f), new Vector3(1.4f, 0.4f, 1.4f), _rockDark);
            K.PointLight("Glut", d, HallA + new Vector3(-5f, 0.8f, 3f), new Color(1f, 0.45f, 0.2f), 10f, 18f, true);

            // Grubenlampen und Stuetzbalken in den Stollen
            LampsAlong(d, HallA + new Vector3(0f, 0f, 9f), HallC + new Vector3(0f, 0f, -20f), 4.5f, lamp);
            LampsAlong(d, HallC + new Vector3(20f, 0f, 8f), GrottoD + new Vector3(-22f, 0f, 0f), 4f, lamp);
            LampsAlong(d, HallC + new Vector3(-20f, 0f, -6f), WreckE + new Vector3(20f, 0f, 0f), 4f, lamp);
            LampsAlong(d, GrottoD + new Vector3(0f, 0f, 22f), HeartF + new Vector3(0f, 0f, -25f), 4f, new Color(0.75f, 0.85f, 1f));
            LampsAlong(d, GrottoD + new Vector3(0f, 0f, -22f), LoreJunction + new Vector3(0f, 0f, 5f), 4f, lamp);
            LampsAlong(d, LoreJunction + new Vector3(-5f, 0f, 0f), HallA + new Vector3(9f, 0f, 0f), 4f, lamp);

            // Lorenbahn: Schienen und eine Lore
            Rails(d, GrottoD + new Vector3(0f, 0f, -22f), LoreJunction + new Vector3(0f, 0f, 5f));
            Rails(d, LoreJunction + new Vector3(-5f, 0f, 0f), HallA + new Vector3(9f, 0f, 0f));
            K.Block("Lore", d, LoreJunction + new Vector3(0f, 0.3f, 2f), new Vector3(1.6f, 1.2f, 2.4f), _woodDark);
            K.Label(d, LoreJunction + new Vector3(0f, 4f, 0f), "Lorenbahn", "Abkürzung zum Förderkorb", WorldLabelStyle.Info, 25f);

            // C Salzhalle: Salzstoecke, Faesser (Hask), Arbeitsgeruest
            RoomLights(d, HallC, 12f, 7.5f, lamp, 4);
            SaltSpikes(d, HallC, 17f, 12, 101 + index);
            for (int i = 0; i < 6; i++)
                K.Prim(PrimitiveType.Cylinder, "Sprengfass", d, HallC + new Vector3(-12f + i * 4.5f, 0.7f, 14f), new Vector3(1f, 0.7f, 1f), Vector3.zero, _woodDark);
            K.Block("Geruest", d, HallC + new Vector3(12f, 0f, -12f), new Vector3(6f, 4f, 3f), _wood);
            K.Label(d, HallC + new Vector3(0f, 10f, 0f), "Salzhalle", "Vorarbeiter Hask", WorldLabelStyle.Place, 45f);
            // Zeichen der Arbeiter: rote Adern im Salz (Mythologie „Fleisch der Alten“)
            Veins(d, HallC, 20f, 8, 7 + index);

            // D Salzsee-Grotte: Wasser und Plattformen (Flut folgt mit dem Boss)
            K.Box("Salzsee", d, GrottoD + new Vector3(0f, 0.35f, 4f), new Vector3(36f, 0.1f, 30f), _water, default, false);
            Vector3[] platforms = { new Vector3(-10f, 0f, -6f), new Vector3(10f, 0f, -4f), new Vector3(0f, 0f, 10f), new Vector3(-12f, 0f, 14f), new Vector3(13f, 0f, 15f) };
            foreach (Vector3 p in platforms)
            {
                K.Block("Plattform", d, GrottoD + p, new Vector3(7f, 1.2f, 7f), _salt);
                // Rampe an der Suedseite: bei steigender Flut schnell hinauf (auch ohne Sprung)
                K.Ramp("Plattform Rampe", d, GrottoD + p + new Vector3(0f, 0f, -6.1f), 0f, 25f, 2.9f, 2.6f, _salt);
            }
            // M6.3 Flut: flache Wasserflaeche, die bei der Salzmutter bis knapp unter die Plattformen steigt
            GameObject flood = K.Box("Flutwasser", d, GrottoD + new Vector3(0f, 0.12f, 0f), new Vector3(42f, 0.1f, 42f), _water, default, false);
            flood.AddComponent<FloodWater>();
            // Gitter vor der Lorenbahn: oeffnet sich erst, wenn die Salzmutter faellt
            Transform gate = K.Group("Lorenbahn-Gitter", d, LoreGatePos, Vector3.zero);
            gate.gameObject.AddComponent<LoreGate>();
            for (int i = 0; i < 10; i++)
                K.Box("Gitterstab", gate, new Vector3(-3.6f + i * 0.8f, 2.9f, 0f), new Vector3(0.14f, 5.8f, 0.14f), _metal, default, false);
            K.Box("Querstrebe", gate, new Vector3(0f, 1.2f, 0f), new Vector3(7.8f, 0.18f, 0.18f), _metal, default, false);
            K.Box("Querstrebe", gate, new Vector3(0f, 4.4f, 0f), new Vector3(7.8f, 0.18f, 0.18f), _metal, default, false);
            GameObject block = K.Box("Gitter Sperre", gate, new Vector3(0f, 2.95f, 0f), new Vector3(7.9f, 5.9f, 0.5f), _metal);
            block.GetComponent<MeshRenderer>().enabled = false;
            K.Label(d, LoreGatePos + new Vector3(0f, 4.6f, 1.5f), "Verriegeltes Gitter", "Öffnet sich, wenn die Salzmutter fällt", WorldLabelStyle.Info, 25f);
            RoomLights(d, GrottoD, 14f, 8f, new Color(0.8f, 0.9f, 1f), 4);
            SaltSpikes(d, GrottoD, 19f, 10, 201 + index);
            K.Label(d, GrottoD + new Vector3(0f, 11f, 0f), "Salzsee-Grotte", "Die Salzmutter", WorldLabelStyle.Place, 45f);

            // E Wrackhoehle: gestrandetes Schiff (optional)
            Transform wreck = K.Group("Wrack", d, WreckE + new Vector3(-4f, 0f, 2f), new Vector3(4f, 25f, 10f));
            K.Box("Rumpf", wreck, new Vector3(0f, 2f, 0f), new Vector3(7f, 4f, 22f), _woodDark);
            K.Box("Mast", wreck, new Vector3(0f, 8f, 3f), new Vector3(0.5f, 12f, 0.5f), _wood, new Vector3(0f, 0f, 14f), false);
            K.Box("Pfuetze", d, WreckE + new Vector3(0f, 0.3f, 0f), new Vector3(34f, 0.1f, 30f), _water, default, false);
            RoomLights(d, WreckE, 12f, 7f, new Color(0.55f, 0.7f, 0.8f), 3);
            K.Label(d, WreckE + new Vector3(0f, 10f, 0f), "Wrackhöhle", "Der Ertrunkene Kapitän (optional)", WorldLabelStyle.Place, 45f);

            // F Herzkaverne: das „Herz“ in der Mitte und vier Grubenlampen, die der Endboss loeschen will
            Transform heart = K.Group("Grubenherz (Platzhalter)", d, HeartF + new Vector3(0f, 0f, 6f), Vector3.zero);
            for (int i = 0; i < 7; i++)
            {
                float a = i * 51f;
                K.Prim(PrimitiveType.Cylinder, "Herzkristall", heart, new Vector3(Mathf.Sin(a) * 1.4f, 3f + (i % 3), Mathf.Cos(a) * 1.4f),
                    new Vector3(1.4f, 3.5f + (i % 3), 1.4f), new Vector3(Mathf.Sin(a) * 25f, a, Mathf.Cos(a) * 25f), _saltVein, false);
            }
            K.PointLight("Herzglut", heart, new Vector3(0f, 4f, 0f), new Color(1f, 0.35f, 0.3f), 34f, 140f, true);
            for (int i = 0; i < 4; i++)
            {
                float a = 45f + i * 90f;
                Vector3 p = HeartF + new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * 15f, 0f, Mathf.Cos(a * Mathf.Deg2Rad) * 15f);
                // M6.4: Lampe als eigenes Objekt – der Endboss loescht sie, Spieler zuenden sie wieder an
                Transform lampT = K.Group("Herzlampe " + (i + 1), d, p, Vector3.zero);
                K.Block("Lampenmast", lampT, Vector3.zero, new Vector3(0.5f, 4f, 0.5f), _metal);
                GameObject glass = K.Box("Lampe", lampT, new Vector3(0f, 4.3f, 0f), new Vector3(0.7f, 0.9f, 0.7f), _lampGlass, default, false);
                K.PointLight("Grubenlampe", lampT, new Vector3(0f, 4.3f, 0f), lamp, 18f, 60f, false);
                K.Block("Sockel", lampT, Vector3.zero, new Vector3(2.2f, 0.12f, 2.2f), _rockDark);
                var ml = lampT.gameObject.AddComponent<MineLamp>();
                K.SetInt(ml, "index", i);
                K.SetRef(ml, "litMaterial", _lampGlass);
                K.SetRef(ml, "darkMaterial", _lampDark);
                K.SetRef(ml, "glass", glass.GetComponent<Renderer>());
            }
            SaltSpikes(d, HeartF, 22f, 16, 301 + index);
            Veins(d, HeartF, 25f, 14, 17 + index);
            K.Label(d, HeartF + new Vector3(0f, 14f, 0f), "Herzkaverne", "Das Grubenherz", WorldLabelStyle.Place, 55f);
        }

        // ================================================================== Bausteine

        private enum Side { N, S, E, W }

        private readonly struct Door
        {
            public readonly Side Side;
            public readonly float Offset;
            public readonly float Width;
            public readonly float Height;

            private Door(Side side, float offset, float width, float height)
            {
                Side = side;
                Offset = offset;
                Width = width;
                Height = height;
            }

            public static Door N(float offset, float width, float height) => new Door(Side.N, offset, width, height);
            public static Door S(float offset, float width, float height) => new Door(Side.S, offset, width, height);
            public static Door E(float offset, float width, float height) => new Door(Side.E, offset, width, height);
            public static Door W(float offset, float width, float height) => new Door(Side.W, offset, width, height);
        }

        private const float WallThickness = 2f;

        /// <summary>Rechteckiger Raum mit Boden, Decke und Waenden; Tueren sind Luecken (mit Sturz darueber).</summary>
        private static void Room(Transform parent, string name, Vector3 center, float sx, float sz, float height, Material wallMat, Door[] doors)
        {
            Transform r = K.Group(name, parent);
            K.Box("Boden", r, center + new Vector3(0f, -0.5f, 0f), new Vector3(sx + 2f * WallThickness, 1f, sz + 2f * WallThickness), _rockDark);
            K.Box("Decke", r, center + new Vector3(0f, height + 0.5f, 0f), new Vector3(sx + 2f * WallThickness, 1f, sz + 2f * WallThickness), _rock);
            foreach (Side side in new[] { Side.N, Side.S, Side.E, Side.W })
            {
                bool alongX = side == Side.N || side == Side.S;
                float length = alongX ? sx : sz;
                float sign = side == Side.N || side == Side.E ? 1f : -1f;
                // Wandmitte (ausserhalb der Raumkante)
                Vector3 wallCenter = alongX
                    ? center + new Vector3(0f, 0f, sign * (sz * 0.5f + WallThickness * 0.5f))
                    : center + new Vector3(sign * (sx * 0.5f + WallThickness * 0.5f), 0f, 0f);
                var gaps = new List<(float from, float to, float h)>();
                foreach (Door dr in doors)
                    if (dr.Side == side) gaps.Add((dr.Offset - dr.Width * 0.5f, dr.Offset + dr.Width * 0.5f, dr.Height));
                gaps.Sort((a, b) => a.from.CompareTo(b.from));
                float cursor = -length * 0.5f - WallThickness;
                float end = length * 0.5f + WallThickness;
                foreach (var gap in gaps)
                {
                    WallPiece(r, wallCenter, alongX, cursor, gap.from, 0f, height, wallMat);
                    WallPiece(r, wallCenter, alongX, gap.from, gap.to, gap.h, height, wallMat); // Sturz
                    cursor = gap.to;
                }
                WallPiece(r, wallCenter, alongX, cursor, end, 0f, height, wallMat);
            }
        }

        private static void WallPiece(Transform r, Vector3 wallCenter, bool alongX, float from, float to, float bottom, float top, Material mat)
        {
            float len = to - from;
            float h = top - bottom + 1f;
            if (len < 0.05f || h < 0.05f) return;
            float mid = (from + to) * 0.5f;
            Vector3 c = wallCenter + (alongX ? new Vector3(mid, 0f, 0f) : new Vector3(0f, 0f, mid)) + new Vector3(0f, bottom + h * 0.5f, 0f);
            Vector3 size = alongX ? new Vector3(len, h, WallThickness) : new Vector3(WallThickness, h, len);
            K.Box("Wand", r, c, size, mat);
        }

        /// <summary>Gerader (auch geneigter) Stollen von a nach b: Boden, Waende, Decke, Stuetzbalken.</summary>
        private static void Tunnel(Transform parent, string name, Vector3 a, Vector3 b, float width, float height)
        {
            Vector3 flat = new Vector3(b.x - a.x, 0f, b.z - a.z);
            float run = flat.magnitude;
            float rise = b.y - a.y;
            float slope = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            float length = Mathf.Sqrt(run * run + rise * rise);
            float yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            Transform t = K.Group(name, parent, (a + b) * 0.5f, new Vector3(0f, yaw, 0f));
            Transform p = K.Group("Neigung", t, Vector3.zero, new Vector3(-slope, 0f, 0f));
            K.Box("Boden", p, new Vector3(0f, -0.5f, 0f), new Vector3(width + 2f * WallThickness, 1f, length), _rockDark);
            K.Box("Decke", p, new Vector3(0f, height + 0.5f, 0f), new Vector3(width + 2f * WallThickness, 1f, length), _rock);
            K.Box("Wand L", p, new Vector3(-(width * 0.5f + WallThickness * 0.5f), height * 0.5f, 0f), new Vector3(WallThickness, height + 1f, length), _rock);
            K.Box("Wand R", p, new Vector3(width * 0.5f + WallThickness * 0.5f, height * 0.5f, 0f), new Vector3(WallThickness, height + 1f, length), _rock);
            for (float z = -length * 0.5f + 3f; z < length * 0.5f - 2f; z += 8f)
            {
                K.Box("Stuetze L", p, new Vector3(-width * 0.5f + 0.25f, height * 0.5f, z), new Vector3(0.4f, height, 0.4f), _wood, default, false);
                K.Box("Stuetze R", p, new Vector3(width * 0.5f - 0.25f, height * 0.5f, z), new Vector3(0.4f, height, 0.4f), _wood, default, false);
                K.Box("Kappe", p, new Vector3(0f, height - 0.3f, z), new Vector3(width, 0.45f, 0.45f), _wood, default, false);
            }
        }

        private static void LampsAlong(Transform parent, Vector3 a, Vector3 b, float height, Color color)
        {
            float len = Vector3.Distance(a, b);
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 13f));
            for (int i = 0; i < n; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, (i + 0.5f) / n) + Vector3.up * height;
                K.Box("Lampe", parent, p, new Vector3(0.35f, 0.5f, 0.35f), _lampGlass, default, false);
                K.PointLight("Grubenlampe", parent, p + Vector3.down * 0.4f, color, 16f, 32f, i % 2 == 0);
            }
        }

        private static void RoomLights(Transform parent, Vector3 center, float spread, float height, Color color, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float a = (45f + i * 360f / count) * Mathf.Deg2Rad;
                Vector3 p = center + new Vector3(Mathf.Sin(a) * spread, height, Mathf.Cos(a) * spread);
                K.Box("Haengelampe", parent, p, new Vector3(0.5f, 0.7f, 0.5f), _lampGlass, default, false);
                K.PointLight("Hallenlampe", parent, p + Vector3.down * 0.5f, color, 30f, 120f, true);
            }
        }

        /// <summary>Leuchtende Salzkristalle an den Raendern (ohne Kollision, damit nichts im Weg steht).</summary>
        private static void SaltSpikes(Transform parent, Vector3 center, float radius, int count, int seed)
        {
            var rng = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = radius * (0.85f + (float)rng.NextDouble() * 0.15f);
                float h = 1.5f + (float)rng.NextDouble() * 3.5f;
                Vector3 p = center + new Vector3(Mathf.Sin(a) * r, h * 0.5f, Mathf.Cos(a) * r);
                K.Prim(PrimitiveType.Cylinder, "Salzkristall", parent, p, new Vector3(0.7f, h * 0.5f, 0.7f),
                    new Vector3((float)rng.NextDouble() * 30f - 15f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 30f - 15f),
                    i % 3 == 0 ? _saltGlow : _salt, false);
            }
        }

        /// <summary>Rote Adern im Salz – wie Muskelfasern („das Fleisch der Alten“).</summary>
        private static void Veins(Transform parent, Vector3 center, float radius, int count, int seed)
        {
            var rng = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                Vector3 p = center + new Vector3(Mathf.Sin(a) * radius, 2f + (float)rng.NextDouble() * 6f, Mathf.Cos(a) * radius);
                K.Box("Ader", parent, p, new Vector3(0.25f, 0.12f, 3f + (float)rng.NextDouble() * 4f), _saltVein,
                    new Vector3((float)rng.NextDouble() * 60f - 30f, a * Mathf.Rad2Deg + 90f, (float)rng.NextDouble() * 40f), false);
            }
        }

        private static void Rails(Transform parent, Vector3 a, Vector3 b)
        {
            Vector3 side = Vector3.Cross(Vector3.up, (b - a).normalized) * 0.55f;
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 from = a + side * s + Vector3.up * 0.06f, to = b + side * s + Vector3.up * 0.06f;
                Vector3 mid = (from + to) * 0.5f;
                Vector3 dir = to - from;
                K.Box("Schiene", parent, mid, new Vector3(0.1f, 0.1f, dir.magnitude), _rail, Quaternion.LookRotation(dir).eulerAngles, false);
            }
        }

        // ================================================================== Material und NavMesh

        private static void CreateMaterials()
        {
            _rock = K.Mat("Mine_Rock", new Color(0.3f, 0.28f, 0.26f));
            _rockDark = K.Mat("Mine_RockDark", new Color(0.2f, 0.19f, 0.18f));
            _salt = K.Mat("Mine_Salt", new Color(0.78f, 0.77f, 0.74f), 0.35f);
            _saltGlow = K.Mat("Mine_SaltGlow", new Color(0.86f, 0.9f, 0.95f), 0.5f, new Color(0.35f, 0.42f, 0.55f));
            _saltVein = K.Mat("Mine_SaltVein", new Color(0.62f, 0.2f, 0.18f), 0.4f, new Color(0.35f, 0.06f, 0.05f));
            _wood = K.Mat("Mine_Wood", new Color(0.36f, 0.26f, 0.17f));
            _woodDark = K.Mat("Mine_WoodDark", new Color(0.22f, 0.16f, 0.11f));
            _metal = K.Mat("Mine_Metal", new Color(0.3f, 0.3f, 0.32f), 0.4f);
            _rail = K.Mat("Mine_Rail", new Color(0.25f, 0.22f, 0.2f), 0.5f);
            _water = K.Mat("Mine_Brine", new Color(0.12f, 0.16f, 0.18f), 0.9f);
            _lampDark = K.Mat("Mine_LampDark", new Color(0.12f, 0.1f, 0.09f), 0.3f);
            _lampGlass = K.Mat("Mine_Lamp", new Color(1f, 0.8f, 0.5f), 0.2f, new Color(1.6f, 1f, 0.45f));
        }

        private static readonly List<NavMeshSurface> Surfaces = new List<NavMeshSurface>();

        /// <summary>
        /// Jeder Platz bekommt eine NavMesh-Flaeche (lokal zum Platz). Alle Plaetze sind gleich gebaut – deshalb wird
        /// nur Platz 0 gebacken und dieselben Daten an allen Plaetzen eingesetzt (sechsmal schneller, ein Asset).
        /// </summary>
        private static void BuildNavMeshSurface(Transform root)
        {
            Surfaces.Clear();
            int exclude = LayerMask.GetMask(GameLayers.Player, GameLayers.Enemy, GameLayers.Interactable, "Ignore Raycast", "UI");
            foreach (Transform slot in Slots)
            {
                var surface = slot.gameObject.AddComponent<NavMeshSurface>();
                surface.collectObjects = CollectObjects.Volume;
                surface.center = BoundsCenter;
                surface.size = BoundsSize;
                surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
                surface.layerMask = ~exclude;
                surface.overrideVoxelSize = true;
                surface.voxelSize = 0.2f;
                surface.overrideTileSize = true;
                surface.tileSize = 256;
                Surfaces.Add(surface);
            }
        }

        /// <summary>NavMesh von Platz 0 backen, speichern und an allen Plaetzen einsetzen (nach dem kompletten Szenenaufbau).</summary>
        public static void BakeNavMesh()
        {
            if (Surfaces.Count == 0 || Surfaces[0] == null) return;
            float t0 = Time.realtimeSinceStartup;
            AssetDatabase.DeleteAsset(NavMeshAssetPath);
            NavMeshSurface first = Surfaces[0];
            // Frisch gebaute Kollider erst an die Physik uebergeben – sonst findet das Backen nichts
            Physics.SyncTransforms();
            // Diagnose: wie viele Kollider und Quellen liegen im Bereich von Platz 0?
            Bounds wb = new Bounds(first.transform.TransformPoint(first.center), first.size);
            int colliders = Physics.OverlapBox(wb.center, wb.extents, Quaternion.identity, first.layerMask, QueryTriggerInteraction.Ignore).Length;
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(wb, first.layerMask, first.useGeometry, first.defaultArea, new List<NavMeshBuildMarkup>(), sources);
            Debug.Log($"[Ashveil] Salzgruben-NavMesh: {colliders} Kollider, {sources.Count} Quellen im Bereich {wb.center:F0} / {wb.size:F0}");
            first.BuildNavMesh();
            if (first.navMeshData == null)
            {
                Debug.LogWarning("[Ashveil] NavMesh Salzgruben: Backen fehlgeschlagen.");
                return;
            }
            AssetDatabase.CreateAsset(first.navMeshData, NavMeshAssetPath);
            foreach (NavMeshSurface s in Surfaces)
            {
                if (s == null) continue;
                s.navMeshData = first.navMeshData;
                EditorUtility.SetDirty(s);
            }
            Debug.Log($"[Ashveil] NavMesh Salzgruben gebacken ({(Time.realtimeSinceStartup - t0):0.0} s, {Surfaces.Count} Plaetze, " +
                      $"Bounds {first.navMeshData.sourceBounds.size:F0}) -> {NavMeshAssetPath}");
        }

        [MenuItem("Ashveil/Salzgruben/NavMesh neu backen", priority = 430)]
        public static void RebakeMenu()
        {
            Surfaces.Clear();
            foreach (DungeonSlot slot in Object.FindObjectsByType<DungeonSlot>(FindObjectsInactive.Include))
            {
                var surface = slot.GetComponent<NavMeshSurface>();
                if (surface != null) Surfaces.Add(surface);
            }
            Surfaces.Sort((a, b) => a.GetComponent<DungeonSlot>().Index.CompareTo(b.GetComponent<DungeonSlot>().Index));
            BakeNavMesh();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        }

        /// <summary>Welt-Punkt eines lokalen Raumpunkts in Platz <paramref name="index"/>.</summary>
        public static Vector3 World(int index, Vector3 local) => SlotOrigin(index) + local;
    }
}
