using System.Collections.Generic;
using Ashveil.Gameplay.Npcs;
using Ashveil.World;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// M8.6: Leben auf den Wegen von Kareth (Vorgaben: Feature-Update § 26/§ 34, Kanon § 22, Karrenreise § 19–21).
    /// Kareth ist eine gefaehrliche Senke – Reisende nutzen nur sichere Strecken, weg von Wolfsrudeln, Moor und der
    /// Route des Glockenhirten:
    ///   Salzweg  – Salzfuhrmann Oswald faehrt zweimal am Tag mit Maultier und Wagen von den Salzgruben zum Wachtfeuer,
    ///              laedt Salz an der Vorratskiste ab und rastet dazwischen bei den Grubenleuten (Fuhrwerk folgt der Strasse)
    ///   Nordweg  – Wegschrein und Pilgerrast; zwei Pilger beten dort und gehen tagsueber zum Beschuetzer ins Lager
    ///   Ostweg   – liegengebliebener Wagen; Wagner Tamo repariert das Rad, abends sitzt er an seinem kleinen Feuer
    /// Alles wie die Lagerbewohner aus der Weltuhr (NpcRoutine) – kein Netzverkehr. Nicht jede Begegnung ist eine Quest.
    /// </summary>
    public static partial class SandboxSceneBuilder
    {
        // Salzweg: Stellplatz am Stollenmund, Wegpunkte auf der Strasse, Stellplatz vor dem Suedwesttor
        private static readonly Vector2 WagonAtMine = new Vector2(-278.7f, -189.3f);
        private static readonly Vector2 WagonAtCamp = new Vector2(-42f, -38f);
        private static readonly Vector2[] SaltRoad = { new Vector2(-220f, -160f), new Vector2(-150f, -120f), new Vector2(-97f, -82f), new Vector2(-60f, -56f) };
        private static readonly Vector2 Shrine = new Vector2(7f, 168f);
        private static readonly Vector2 PilgrimRest = new Vector2(14f, 178f);
        private static readonly Vector2 BrokenWagon = new Vector2(153f, -55f);
        private static readonly Vector2 MinersFire = new Vector2(-295.7f, -172.4f);

        private static NpcCart _saltWagon;

        private static Vector3 GroundV(Vector2 p) => new Vector3(p.x, Ground(p.x, p.y), p.y);

        private static Vector3[] Via(bool toCamp)
        {
            var list = new List<Vector3>();
            foreach (Vector2 p in SaltRoad) list.Add(GroundV(p));
            if (!toCamp) list.Reverse();
            return list.ToArray();
        }

        private static RoutineStep Ride(float from, (Vector3, float) spot, Vector3[] via, string activity, string note)
        {
            RoutineStep r = At(from, spot, activity, WagonSeat, note: note);
            r.ride = true;
            r.via = via;
            return r;
        }

        private const float WagonSeat = 1.05f;

        // ================================================================== Aufbau

        private static void BuildRoadLife(Transform env)
        {
            Transform root = K.Group("Unterwegs (Reisende)", env);
            BuildShrine(K.Group("Wegschrein und Pilgerrast", root));
            BuildBrokenWagon(K.Group("Liegengebliebener Wagen", root));
            _saltWagon = BuildSaltWagon(root);
            List<NpcSpec> list = RoadNpcs();
            for (int i = 0; i < list.Count; i++) NpcFigure(root, list[i], 100 + i);
        }

        private static void Poi(Transform at, string id, string name, float radius, string use)
        {
            var go = new GameObject("POI " + name);
            go.transform.SetParent(at, false);
            go.AddComponent<WorldPoi>().Configure(id, name, radius, use);
        }

        private static void BuildShrine(Transform parent)
        {
            Material candle = K.Mat("Candle_Wax", new Color(0.86f, 0.82f, 0.7f));
            Material flame = K.Mat("Candle_Flame", new Color(1f, 0.62f, 0.25f), 0.1f, new Color(1f, 0.5f, 0.15f) * 2.5f);
            Material ribbon = K.Mat("Cloth_Wine", new Color(0.4f, 0.12f, 0.12f));
            // Stele mit gemeisselter Riesenhand, zur Strasse (Westen) gewandt
            Transform s = K.Group("Wegschrein", parent, Lage("req:wegschrein", GroundV(Shrine)), new Vector3(0f, LageYaw("req:wegschrein", 270f), 0f));
            K.Block("Sockel", s, Vector3.zero, new Vector3(1.6f, 0.4f, 1.1f), _stoneDark);
            K.Block("Stele", s, new Vector3(0f, 0.4f, 0f), new Vector3(0.9f, 2.1f, 0.45f), _stone);
            K.Box("Relief Hand", s, new Vector3(0f, 1.7f, 0.24f), new Vector3(0.5f, 0.62f, 0.05f), _stoneDark, default, false);
            for (int f = 0; f < 4; f++)
                K.Box("Finger", s, new Vector3(-0.18f + f * 0.12f, 2.12f, 0.25f), new Vector3(0.07f, 0.26f, 0.05f), _stoneDark, default, false);
            K.Box("Band", s, new Vector3(0f, 1.1f, 0.24f), new Vector3(0.95f, 0.08f, 0.02f), ribbon, default, false);
            for (int i = 0; i < 5; i++)
            {
                Vector3 c = new Vector3(-0.6f + i * 0.3f, 0.4f, 0.42f + (i % 2) * 0.1f);
                K.Prim(PrimitiveType.Cylinder, "Kerze", s, c + Vector3.up * 0.08f, new Vector3(0.06f, 0.08f, 0.06f), Vector3.zero, candle, false);
                K.Prim(PrimitiveType.Sphere, "Flamme", s, c + Vector3.up * 0.2f, new Vector3(0.035f, 0.06f, 0.035f), Vector3.zero, flame, false);
            }
            K.Box("Gaben", s, new Vector3(0.45f, 0.44f, 0.38f), new Vector3(0.25f, 0.08f, 0.18f), _straw, new Vector3(0f, 20f, 0f), false);
            K.PointLight("Kerzenlicht", s, new Vector3(0f, 0.9f, 0.7f), new Color(1f, 0.6f, 0.3f), 5f, 0.8f, true);
            K.Label(s, new Vector3(0f, 3.1f, 0f), "Wegschrein", "Nordweg", WorldLabelStyle.Place, 40f, 0.8f);
            Poi(s, "poi.kareth.wegschrein", "Wegschrein am Nordweg", 12f, "Pilger beten hier auf dem Weg zum Beschuetzer. Spaeter: Segen, kleine Geschichte der Pilger.");

            // Pilgerrast: Unterstand, zwei Schlaflager, kleines Feuer, Stamm zum Sitzen
            Transform r = K.Group("Pilgerrast", parent, Lage("req:pilgerrast", GroundV(PilgrimRest)), new Vector3(0f, LageYaw("req:pilgerrast", 200f), 0f));
            for (int ix = -1; ix <= 1; ix += 2)
                K.Block("Pfosten", r, new Vector3(ix * 1.5f, 0f, 2.2f), new Vector3(0.14f, 1.8f, 0.14f), _woodDark);
            K.Box("Plane", r, new Vector3(0f, 1.25f, 2.9f), new Vector3(3.4f, 0.05f, 1.8f), _canvas, new Vector3(-35f, 0f, 0f), false);
            for (int ix = -1; ix <= 1; ix += 2)
                K.Box("Schlaflager", r, new Vector3(ix * 0.75f, 0.06f, 3.0f), new Vector3(0.7f, 0.12f, 1.8f), ribbon, default, false);
            for (int i = 0; i < 6; i++)
                K.Box("Stein", r, OnRingAt(Vector3.zero, i * 60f, 0.55f, 0.1f), new Vector3(0.25f, 0.2f, 0.22f), _stoneDark, new Vector3(0f, i * 60f, 0f), false);
            K.Prim(PrimitiveType.Sphere, "Glut", r, new Vector3(0f, 0.08f, 0f), new Vector3(0.5f, 0.15f, 0.5f), Vector3.zero, _fire, false);
            K.PointLight("Feuer", r, new Vector3(0f, 0.8f, 0f), new Color(1f, 0.55f, 0.22f), 7f, 1.6f, true);
            K.Particles("Flammen", r, new Vector3(0f, 0.1f, 0f), new Color(1f, 0.55f, 0.2f, 0.85f), 0.35f, 10f, 0.7f, 0.5f, 7f, 0.15f);
            K.Box("Pilgerstab", r, new Vector3(1.9f, 0.95f, 2.0f), new Vector3(0.05f, 1.9f, 0.05f), _wood, new Vector3(0f, 0f, 12f), false);
            K.Label(r, new Vector3(0f, 2.4f, 1.5f), "Pilgerrast", "", WorldLabelStyle.Place, 30f, 0.75f);
            Poi(r, "poi.kareth.pilgerrast", "Pilgerrast", 10f, "Zwei Pilger schlafen hier. Spaeter: Gespraeche ueber den Beschuetzer, Hinweise auf alte Orte.");
        }

        private static Vector3 OnRingAt(Vector3 c, float angleDeg, float radius, float y)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            return c + new Vector3(Mathf.Sin(a) * radius, y, Mathf.Cos(a) * radius);
        }

        private static void BuildBrokenWagon(Transform parent)
        {
            Transform w = BrokenWagonBody(parent, GroundV(BrokenWagon), 75f);
            // kleines Feuer und Schlafplatz daneben
            Vector3 f = GroundV(BrokenWagon + new Vector2(-6f, -3f));
            Transform fire = K.Group("Feuerstelle", parent, f, Vector3.zero);
            for (int i = 0; i < 6; i++)
                K.Box("Stein", fire, OnRingAt(Vector3.zero, i * 60f, 0.5f, 0.1f), new Vector3(0.22f, 0.18f, 0.2f), _stoneDark, new Vector3(0f, i * 60f, 0f), false);
            K.Prim(PrimitiveType.Sphere, "Glut", fire, new Vector3(0f, 0.08f, 0f), new Vector3(0.45f, 0.14f, 0.45f), Vector3.zero, _fire, false);
            K.PointLight("Feuer", fire, new Vector3(0f, 0.8f, 0f), new Color(1f, 0.55f, 0.22f), 7f, 1.5f, true);
            K.Particles("Flammen", fire, new Vector3(0f, 0.1f, 0f), new Color(1f, 0.55f, 0.2f, 0.85f), 0.3f, 9f, 0.7f, 0.5f, 7f, 0.15f);
            K.Label(w, new Vector3(0f, 2.6f, 0f), "Liegengebliebener Wagen", "Ostweg", WorldLabelStyle.Place, 40f, 0.75f);
            Poi(w, "poi.kareth.liegengebliebener_wagen", "Liegengebliebener Wagen am Ostweg", 12f, "Wagner Tamo repariert sein Rad. Spaeter: kleiner Auftrag (Ersatzrad aus dem Holzfaellerlager).");
        }

        /// <summary>Der schiefe Wagen mit gebrochenem Rad und verrutschter Ladung (ohne Feuer und Schild; auch Kareth v4, #27).</summary>
        private static Transform BrokenWagonBody(Transform parent, Vector3 pos, float yaw)
        {
            Transform w = K.Group("Wagen (gebrochenes Rad)", parent, pos, new Vector3(0f, yaw, 0f));
            // schief: rechte Seite abgesackt
            Transform tilt = K.Group("Aufbau", w, Vector3.zero, new Vector3(0f, 0f, -9f));
            K.Box("Ladeflaeche", tilt, new Vector3(0f, 0.75f, 0f), new Vector3(1.6f, 0.12f, 2.8f), _wood, default, false);
            K.Box("Bord L", tilt, new Vector3(-0.78f, 1.05f, 0f), new Vector3(0.06f, 0.5f, 2.8f), _woodDark, default, false);
            K.Box("Bord R", tilt, new Vector3(0.78f, 1.05f, 0f), new Vector3(0.06f, 0.5f, 2.8f), _woodDark, default, false);
            K.Prim(PrimitiveType.Cylinder, "Rad L", tilt, new Vector3(-0.9f, 0.5f, -0.6f), new Vector3(1f, 0.06f, 1f), new Vector3(0f, 0f, 90f), _woodDark, false);
            K.Prim(PrimitiveType.Cylinder, "Rad L vorn", tilt, new Vector3(-0.9f, 0.5f, 0.9f), new Vector3(1f, 0.06f, 1f), new Vector3(0f, 0f, 90f), _woodDark, false);
            K.Block("Holzbock", w, new Vector3(0.95f, 0f, -0.6f), new Vector3(0.35f, 0.42f, 0.5f), _woodDark);
            // gebrochenes Rad liegt daneben, Speichen verstreut
            K.Prim(PrimitiveType.Cylinder, "Gebrochenes Rad", w, new Vector3(1.9f, 0.05f, -1.0f), new Vector3(1f, 0.05f, 1f), new Vector3(0f, 25f, 0f), _woodDark, false);
            for (int i = 0; i < 3; i++)
                K.Box("Speiche", w, new Vector3(2.2f + i * 0.25f, 0.03f, -0.2f + i * 0.2f), new Vector3(0.05f, 0.05f, 0.45f), _wood, new Vector3(0f, 30f + i * 40f, 0f), false);
            // verrutschte Ladung
            K.Block("Kiste", w, new Vector3(1.6f, 0f, 1.2f), new Vector3(0.7f, 0.6f, 0.6f), _wood, 20f);
            K.Prim(PrimitiveType.Cylinder, "Fass", w, new Vector3(2.4f, 0.35f, 0.9f), new Vector3(0.6f, 0.35f, 0.6f), new Vector3(90f, 40f, 0f), _woodDark, false);
            K.Box("Plane", w, new Vector3(0.2f, 1.1f, -0.2f), new Vector3(1.4f, 0.04f, 1.6f), _canvas, new Vector3(-12f, 0f, -9f), false);
            return w;
        }

        /// <summary>Salzfuhrwerk: Wagen mit Kutschbock (Ursprung = Sitz des Fahrers), Maultier davor, Salzsaecke und Faesser.</summary>
        private static NpcCart BuildSaltWagon(Transform parent)
        {
            Material sack = K.Mat("Salt_Sack", new Color(0.74f, 0.71f, 0.64f));
            Transform wagon = K.Group("Salzfuhrwerk (Oswald)", parent, Lage("req:salzfuhrwerk", GroundV(WagonAtMine)), new Vector3(0f, LageYaw("req:salzfuhrwerk", 45f), 0f));
            Transform body = K.Group("Aufbau", wagon);
            K.Box("Kutschbock", body, new Vector3(0f, 0.98f, 0.05f), new Vector3(1.3f, 0.1f, 0.4f), _wood, default, false);
            K.Box("Lehne", body, new Vector3(0f, 1.25f, -0.18f), new Vector3(1.3f, 0.45f, 0.06f), _woodDark, default, false);
            K.Box("Ladeflaeche", body, new Vector3(0f, 0.85f, -1.5f), new Vector3(1.5f, 0.12f, 2.4f), _wood, default, false);
            K.Box("Bord L", body, new Vector3(-0.74f, 1.1f, -1.5f), new Vector3(0.06f, 0.45f, 2.4f), _woodDark, default, false);
            K.Box("Bord R", body, new Vector3(0.74f, 1.1f, -1.5f), new Vector3(0.06f, 0.45f, 2.4f), _woodDark, default, false);
            K.Box("Bord hinten", body, new Vector3(0f, 1.1f, -2.7f), new Vector3(1.5f, 0.45f, 0.06f), _woodDark, default, false);
            for (int i = 0; i < 4; i++)
                K.Prim(PrimitiveType.Capsule, "Salzsack", body, new Vector3(-0.35f + (i % 2) * 0.7f, 1.1f, -0.9f - (i / 2) * 0.8f), new Vector3(0.5f, 0.32f, 0.5f),
                    new Vector3(90f, i * 15f, 0f), sack, false);
            for (int i = 0; i < 2; i++)
                K.Prim(PrimitiveType.Cylinder, "Salzfass", body, new Vector3(-0.35f + i * 0.7f, 1.2f, -2.3f), new Vector3(0.55f, 0.3f, 0.55f), Vector3.zero, _woodDark, false);
            K.Box("Laterne", body, new Vector3(0.62f, 1.55f, 0.15f), new Vector3(0.14f, 0.2f, 0.14f), _warmWindow, default, false);
            // Deichsel zum Maultier
            for (int s = -1; s <= 1; s += 2)
                K.Box("Deichsel", wagon, new Vector3(s * 0.45f, 0.8f, 1.25f), new Vector3(0.07f, 0.07f, 2.1f), _wood, new Vector3(4f, 0f, 0f), false);
            var wheels = new List<Transform>();
            foreach (float z in new[] { -0.6f, -2.3f })
            for (int s = -1; s <= 1; s += 2)
            {
                GameObject w = K.Prim(PrimitiveType.Cylinder, "Rad", wagon, new Vector3(s * 0.84f, 0.45f, z), new Vector3(0.9f, 0.05f, 0.9f), new Vector3(0f, 0f, 90f), _woodDark, false);
                K.Box("Speiche", w.transform, Vector3.zero, new Vector3(0.9f, 2.4f, 0.1f), _wood, default, false);
                wheels.Add(w.transform);
            }
            Transform mule = K.Group("Zugtier (Maultier)", wagon, new Vector3(0f, 0f, 3.0f), Vector3.zero);
            AnimalVisual vis = AnimalModel(mule, MuleBody(), false, out _);
            var cart = wagon.gameObject.AddComponent<NpcCart>();
            cart.Configure(wheels.ToArray(), 0.45f);
            cart.ConfigureWagon(WagonSeat, vis, body);
            K.MakeDynamic(wagon.gameObject);
            return cart;
        }

        // ================================================================== Reisende

        private static List<NpcSpec> RoadNpcs()
        {
            var atMine = Spot(WagonAtMine.x, WagonAtMine.y, 225f);
            var atCamp = Spot(WagonAtCamp.x, WagonAtCamp.y, 45f);
            var stores = Spot(-17.0f, -32.2f, 200f);
            var minersFire = Facing(MinersFire.x - 1.9f, MinersFire.y + 1.8f, MinersFire);
            var minersTent = Spot(MinersFire.x - 5f, MinersFire.y + 4f, 45f);

            (Vector3, float) PilgrimFire(int k) => Facing(PilgrimRest.x + (k == 0 ? -1.3f : 1.3f), PilgrimRest.y - 0.6f, PilgrimRest);
            (Vector3, float) PilgrimBed(int k) => Spot(PilgrimRest.x + (k == 0 ? -0.8f : 0.8f), PilgrimRest.y + 2.6f, 20f);
            (Vector3, float) Pray(int k) => Spot(Shrine.x - 2.4f, Shrine.y + (k == 0 ? -0.8f : 0.8f), 90f);
            (Vector3, float) Forecourt(int k) => Spot(k == 0 ? -1.3f : 1.3f, -20.5f, 180f);

            var tamoWheel = Spot(BrokenWagon.x - 0.4f, BrokenWagon.y + 1.9f, 165f);
            Vector2 tf = BrokenWagon + new Vector2(-6f, -3f);
            var tamoFire = Facing(tf.x - 1.2f, tf.y + 0.9f, tf);
            var tamoBed = Spot(BrokenWagon.x - 1.5f, BrokenWagon.y - 2.2f, 75f);

            var list = new List<NpcSpec>
            {
                new NpcSpec
                {
                    // #28: Gesprächsziel in U-2 (Kareth v4)
                    Name = "Fuhrmann Oswald", Title = "Salzfuhrwerk", NpcId = Ashveil.Data.KarethCatalog.Oswald, Look = "1|b0|s2|h2|c4|d1|a1|e3|m1",
                    Gear = new[] { "grey_wool_cloak", "ashen_leather_jerkin", "hunter_breeches", "stalker_boots" },
                    Cart = _saltWagon, Speed = 1.9f,
                    Steps = new[]
                    {
                        At(0f, minersTent, "Sleep", note: "schlaeft bei den Grubenleuten"),
                        Ride(6.5f, atCamp, Via(true), "Sit", "faehrt Salz zum Wachtfeuer"),
                        At(7.1f, stores, "Kneel|PickUp", carry: true, note: "laedt Salzsaecke an der Vorratskiste ab"),
                        At(7.35f, atCamp, "Stand", note: "holt den naechsten Sack"),
                        At(7.5f, stores, "Kneel|PickUp", carry: true, note: "laedt Salzsaecke an der Vorratskiste ab"),
                        At(7.75f, atCamp, "Stand", note: "holt den naechsten Sack"),
                        At(7.9f, stores, "Kneel", carry: true, note: "laedt Salzsaecke an der Vorratskiste ab"),
                        At(8.2f, TableSeat(1, true, 1), "SitTalk", BenchSeat, note: "rastet an den Langtischen"),
                        At(9.5f, atCamp, "Stand|Talk", note: "spannt das Maultier an"),
                        Ride(9.75f, atMine, Via(false), "Sit", "faehrt zurueck zu den Salzgruben"),
                        At(10.35f, minersFire, "SitTalk|Sit", note: "rastet bei den Grubenleuten"),
                        At(14.4f, atMine, "Stand", note: "spannt das Maultier an"),
                        Ride(14.5f, atCamp, Via(true), "Sit", "faehrt Salz zum Wachtfeuer"),
                        At(15.1f, stores, "Kneel|PickUp", carry: true, note: "laedt Salzsaecke an der Vorratskiste ab"),
                        At(15.4f, atCamp, "Stand", note: "holt den naechsten Sack"),
                        At(15.6f, stores, "Kneel", carry: true, note: "laedt Salzsaecke an der Vorratskiste ab"),
                        At(15.9f, atCamp, "Stand|Talk", note: "spannt das Maultier an"),
                        Ride(16.4f, atMine, Via(false), "Sit", "faehrt zurueck zu den Salzgruben"),
                        At(17f, minersFire, "SitTalk|Sit", note: "rastet bei den Grubenleuten"),
                        At(22f, minersTent, "Sleep", note: "schlaeft bei den Grubenleuten")
                    }
                }
            };
            string[] pilgrimNames = { "Pilgerin Ada", "Pilger Rune" };
            string[] pilgrimLooks = { "1|b1|s0|h7|c2|a1|e2|m0", "1|b0|s1|h1|c3|d5|a2|e4|m0" };
            string[][] pilgrimGear = { new[] { "travel_mantle", "weaver_robe", "foot_wraps", "travel_hood" }, new[] { "travel_mantle", "frayed_gambeson", "travel_trousers", "foot_wraps" } };
            for (int k = 0; k < 2; k++)
            {
                list.Add(new NpcSpec
                {
                    Name = pilgrimNames[k], Title = "Pilger", Female = k == 0, Look = pilgrimLooks[k], Gear = pilgrimGear[k],
                    Steps = new[]
                    {
                        At(0f, PilgrimBed(k), "Sleep", note: "schlaeft in der Pilgerrast"),
                        At(6.5f, PilgrimFire(k), "SitTalk|Sit", note: "sitzt am Feuer der Pilgerrast"),
                        At(7.5f + k * 0.1f, Pray(k), "Kneel", note: "betet am Wegschrein"),
                        At(8.5f + k * 0.05f, Forecourt(k), "Kneel", note: "betet vor dem Beschuetzer"),
                        At(11f, FireSeat(8 + k), "SitTalk|Sit", LogSeat, note: "hoert am Wachtfeuer Heddas Geschichten"),
                        At(12.5f, TableSeat(1, false, 1 + k), "SitTalk", BenchSeat, note: "Mittag an den Langtischen"),
                        At(13.25f, FireSeat(8 + k), "Sit|SitTalk", LogSeat, note: "rastet am Wachtfeuer"),
                        At(15.5f + k * 0.05f, PilgrimFire(k), "SitTalk", note: "zurueck in der Pilgerrast"),
                        At(16.5f, Pray(k), "Kneel", note: "betet am Wegschrein"),
                        At(18f, PilgrimFire(k), "SitTalk|Sit", note: "sitzt am Feuer der Pilgerrast"),
                        At(22f, PilgrimBed(k), "Sleep", note: "schlaeft in der Pilgerrast")
                    }
                });
            }
            list.Add(new NpcSpec
            {
                // P3 Glockenrast: Tamo vergibt „Ein Rad für Tamo“ (G4)
                Name = "Wagner Tamo", Title = "Liegengebliebener Wagen", NpcId = Ashveil.Data.GlockenrastCatalog.Tamo, Quest = true, Look = "1|b0|s5|h6|c0|d3|a3|e1|m2",
                Gear = new[] { "ashen_leather_jerkin", "travel_trousers", "stalker_boots", "stalker_gloves" },
                Steps = new[]
                {
                    At(0f, tamoBed, "Sleep", note: "schlaeft unter der Plane"),
                    At(6f, tamoFire, "Sit", note: "waermt sich am Feuer"),
                    At(7f, tamoWheel, "Kneel|Chop|Kneel", note: "repariert das gebrochene Rad"),
                    At(12f, tamoFire, "Sit|SitTalk", note: "isst Brot am Feuer"),
                    At(13f, tamoWheel, "Kneel|Chop", note: "repariert das gebrochene Rad"),
                    At(18f, tamoFire, "Sit", note: "sitzt an seinem Feuer"),
                    At(22f, tamoBed, "Sleep", note: "schlaeft unter der Plane")
                }
            });
            return list;
        }
    }
}
