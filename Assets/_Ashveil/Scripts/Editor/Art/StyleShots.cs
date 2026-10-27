using System.Collections.Generic;
using System.IO;
using Ashveil.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Stilbilder fuer die Abnahme: rendert die offene Szene aus festen Blickwinkeln (mit Nebel und
    /// Nachbearbeitung) nach Logs/Stil/*.png. Stellt dafuer kurz Gegner-Modelle als Staffage auf.
    /// Menue: Ashveil &gt; Grafik &gt; Stilbilder rendern.
    /// </summary>
    public static class StyleShots
    {
        private const int Width = 1920, Height = 1080;

        private struct Shot
        {
            public string Name;
            public Vector3 Eye;
            public Vector3 Target;
            public float Fov;
            public float Ortho; // > 0: senkrechte Draufsicht mit dieser halben Hoehe (m)
            public float Far;   // > 0: eigene Sichtweite (Gebirge)
        }

        private static Vector3 G(float x, float z, float lift) => KarethTerrainBuilder.OnGround(x, z, lift);

        [MenuItem("Ashveil/Grafik/Stilbilder rendern", priority = 401)]
        public static void RenderMenu()
        {
            string dir = RenderAll();
            if (dir != null) Debug.Log("[Ashveil] Stilbilder gespeichert: " + dir);
        }

        [MenuItem("Ashveil/Grafik/Riesen-Vorschau rendern", priority = 403)]
        public static void RenderGiantMenu()
        {
            string dir = RenderGiantPreview();
            if (dir != null) Debug.Log("[Ashveil] Riesen-Vorschau gespeichert: " + dir);
        }

        [MenuItem("Ashveil/Grafik/Lager-Vorschau rendern", priority = 406)]
        public static void RenderCampMenu()
        {
            string dir = RenderCampPreview();
            if (dir != null) Debug.Log("[Ashveil] Lager-Vorschau gespeichert: " + dir);
        }

        [MenuItem("Ashveil/Welt/Grenzgebirge: Review-Bilder", priority = 512)]
        public static void RenderBorderMenu()
        {
            string dir = RenderBorderPreview();
            if (dir != null) Debug.Log("[Ashveil] Grenzgebirge-Review gespeichert: " + dir);
        }

        /// <summary>
        /// Review-Kameras des Grenzgebirges (Checkliste neue Region; Sichttests 1–4 aus dem Design): oeffnet beide Zellen
        /// additiv, rendert und schliesst sie wieder. Bilder: Logs/Ashveil/Grenzgebirge.
        /// </summary>
        public static string RenderBorderPreview()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return null;
            var opened = new List<UnityEngine.SceneManagement.Scene>();
            foreach (string path in new[] { WorldManifestBuilder.BorderWestScene, WorldManifestBuilder.BorderEastScene })
            {
                var sc = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
                if (sc.IsValid() && sc.isLoaded) continue;
                if (!File.Exists(path)) { Debug.LogWarning("[Ashveil] Grenzgebirge fehlt – erst 'Grenzgebirge bauen'."); return null; }
                opened.Add(UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Additive));
            }
            try
            {
                // 30.09.: Kern suedoestlich von Kareth, gedreht – Entwurfspunkte in die Welt (W)
                Vector3 W(Vector3 l) => Ashveil.World.BorderMountainsFrame.ToWorld(l);
                Vector3 GB(float x, float z, float lift) => W(BorderMountainsLayout.OnGround(x, z, lift));
                Vector3 P(float bearing, float r, float y) { Vector2 p = BorderMountainsLayout.FromPlateau(bearing, r); return W(new Vector3(p.x, y, p.y)); }
                Vector2 fc = BorderMountainsLayout.FootCenter, pc = BorderMountainsLayout.PlateauCenter;
                float ph = BorderMountainsLayout.PlateauHeight;
                var foot = W(new Vector3(fc.x, BorderMountainsLayout.BasinFloor, fc.y));
                var shots = new List<Shot>
                {
                    new Shot { Name = "G01_Terrasse_Fussabdruck", Eye = P(BorderMountainsLayout.SchenkeBearing, 88f, ph + 2.2f), Target = foot, Fov = 60f, Far = 4000f },
                    new Shot { Name = "G02_Aussichtsfelsen", Eye = P(BorderMountainsLayout.SchenkeBearing + 22f, 89f, ph + 2.3f), Target = foot, Fov = 55f, Far = 4000f },
                    new Shot { Name = "G03_Rueckblick_Tal_Kareth", Eye = P(225f, 89f, ph + 2.2f), Target = W(new Vector3(640f, 20f, 70f)), Fov = 60f, Far = 4000f },
                    new Shot { Name = "G04_Talpass_T2_zur_Schenke", Eye = GB(845f, 205f, 1.7f), Target = W(new Vector3(pc.x, ph + 10f, pc.y)), Fov = 65f, Far = 4000f },
                    new Shot { Name = "G05_Zehen_am_Boden", Eye = GB(1150f, 380f, 1.7f), Target = GB(1300f, 530f, 3f), Fov = 70f, Far = 4000f },
                    new Shot { Name = "G06_Lichtung_Ost", Eye = GB(1540f, 470f, 1.7f), Target = GB(1300f, 440f, 5f), Fov = 70f, Far = 4000f },
                    new Shot { Name = "G07_Tunnel_Westportal", Eye = G(470f, 42f, 1.8f), Target = new Vector3(515f, 5f, 60f), Fov = 65f, Far = 4000f },
                    new Shot { Name = "G08_Osttunnel_Ausgang_Eschenfeld", Eye = new Vector3(705f, TerrainGen.GeneratedHeights.Sample(705f, 82f, 10f) + 1.8f, 82f), Target = new Vector3(656f, 9f, 60f), Fov = 65f, Far = 4000f },
                    new Shot { Name = "G09_Hochpass", Eye = GB(1750f, 1050f, 2f), Target = W(new Vector3(2400f, 380f, 900f)), Fov = 65f, Far = 4000f },
                    new Shot { Name = "G10_Kareth_Blick_Suedost_Grenzgebirge", Eye = new Vector3(150f, 12f, -40f), Target = W(new Vector3(1500f, 300f, 850f)), Fov = 60f, Far = 4000f },
                    new Shot { Name = "G11_Draufsicht", Eye = W(new Vector3(1500f, 1600f, 850f)), Target = W(new Vector3(1500f, 0f, 850.01f)), Ortho = 1250f, Far = 2500f },
                };
                return Render(shots, "Grenzgebirge", false);
            }
            finally
            {
                foreach (var sc in opened) UnityEditor.SceneManagement.EditorSceneManager.CloseScene(sc, true);
            }
        }

        [MenuItem("Ashveil/Welt/Kareth-Relief: Blickpunkt-Bilder", priority = 518)]
        public static void RenderKarethReliefMenu()
        {
            string dir = RenderKarethRelief();
            if (dir != null) Debug.Log("[Ashveil] Kareth-Relief-Blickpunkte gespeichert: " + dir);
        }

        /// <summary>
        /// P2.5 Kareth-Relief: Blickpunkte aus MMO-Kamerahoehe (≈ 3,5 m ueber dem Boden) auf die neuen Formen und eine
        /// Draufsicht. Gleiche Punkte vor und nach dem Relief – Bilder: Logs/Ashveil/Relief.
        /// </summary>
        public static string RenderKarethRelief()
        {
            float H(float x, float z)
            {
                if (Physics.Raycast(new Vector3(x, 4000f, z), Vector3.down, out RaycastHit hit, 6000f, ~0, QueryTriggerInteraction.Ignore))
                    return hit.point.y;
                return KarethTerrainBuilder.Height(x, z);
            }
            Vector3 E(float x, float z, float lift = 3.5f) => new Vector3(x, H(x, z) + lift, z);
            var shots = new List<Shot>
            {
                new Shot { Name = "K01_Nordtor_Blick_Moor_Plateau", Eye = E(0f, 45f), Target = G(18f, 470f, 12f), Fov = 62f, Far = 3000f },
                new Shot { Name = "K02_Glockenrast_Blick_Nordwest", Eye = E(-20f, 175f), Target = G(-210f, 520f, 10f), Fov = 62f, Far = 3000f },
                new Shot { Name = "K03_Holzfaellerlager_Blick_Nord", Eye = E(-250f, 255f), Target = G(-410f, 430f, 10f), Fov = 62f, Far = 3000f },
                new Shot { Name = "K04_Moor_Blick_Nordost", Eye = E(20f, 300f, 3f), Target = G(330f, 470f, 10f), Fov = 62f, Far = 3000f },
                new Shot { Name = "K05_Ostwacht_Blick_West", Eye = E(250f, 175f), Target = G(110f, 175f, 6f), Fov = 62f, Far = 3000f },
                new Shot { Name = "K06_Aschenfeld_Krater", Eye = E(330f, -85f), Target = G(420f, -150f, 0f), Fov = 62f, Far = 3000f },
                new Shot { Name = "K07_Rampe_oben_Blick_Sued", Eye = E(30f, 612f, 4f), Target = G(0f, 220f, 0f), Fov = 65f, Far = 3000f },
                new Shot { Name = "K08_Weideweg_Blick_Ost", Eye = E(-525f, 250f), Target = G(-300f, 250f, 8f), Fov = 62f, Far = 3000f },
                new Shot { Name = "K09_Schlachtfeld_Blick_Sued", Eye = E(330f, 470f), Target = G(230f, 150f, 0f), Fov = 62f, Far = 3000f },
                new Shot { Name = "K10_Draufsicht", Eye = new Vector3(0f, 900f, 250f), Target = new Vector3(0f, 0f, 250.01f), Ortho = 610f, Far = 2000f }
            };
            return Render(shots, "Relief", false);
        }

        [MenuItem("Ashveil/Welt/Terrain-Generator: Blickpunkt-Bilder", priority = 545)]
        public static void RenderWorldViewpointsMenu()
        {
            string dir = RenderWorldViewpoints();
            if (dir != null) Debug.Log("[Ashveil] Welt-Blickpunkte gespeichert: " + dir);
        }

        /// <summary>
        /// Terrain-Generator V1, Vorgabe §90/§128: Blickpunkte in Spielerhoehe (Kareth Nord/Sued/Ost, Hoher Pass, Talpass-Tor,
        /// Schenke, Fussabdruck, Bruchmark-Eingang, Grosse Kerbe) und eine Draufsicht. Bilder: Logs/Ashveil/Welt.
        /// </summary>
        public static string RenderWorldViewpoints()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return null;
            var opened = new List<UnityEngine.SceneManagement.Scene>();
            foreach (string path in new[] { WorldManifestBuilder.BorderWestScene, WorldManifestBuilder.BorderEastScene })
            {
                var sc = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
                if (sc.IsValid() && sc.isLoaded) continue;
                if (File.Exists(path)) opened.Add(UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Additive));
            }
            try
            {
                float H(float x, float z)
                {
                    // hoechste Oberflaeche (Felsen, Mauern, Gelaende) – die Kamera soll nie in Geometrie stecken
                    if (Physics.Raycast(new Vector3(x, 4000f, z), Vector3.down, out RaycastHit hit, 6000f, ~0, QueryTriggerInteraction.Ignore))
                        return hit.point.y;
                    Terrain t = Ashveil.Core.WorldGround.TerrainAt(new Vector3(x, 0f, z));
                    return t != null ? t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y : Ashveil.World.MacroWorld.SampleMacroHeight(new Vector3(x, 0f, z));
                }
                Vector3 E(float x, float z, float lift = 1.8f) => new Vector3(x, H(x, z) + lift, z);
                Vector3 T(float x, float z, float y) => new Vector3(x, y, z);
                Vector2 M(float px, float py) => TerrainGen.MacroWorldDefaults.M(px, py);
                Vector3 W(Vector3 l) => Ashveil.World.BorderMountainsFrame.ToWorld(l);
                Vector2 sch = Ashveil.World.BorderMountainsFrame.ToWorld(BorderMountainsLayout.FromPlateau(BorderMountainsLayout.SchenkeBearing, 88f));
                Vector2 gate = Ashveil.World.BorderMountainsFrame.ToWorld(BorderMountainsLayout.TalNode(0).x, BorderMountainsLayout.TalNode(0).z);
                Vector2 hp = M(395, 540), be = M(660, 630), kr = M(505, 790), kr2 = M(690, 940);
                Vector3 foot = W(new Vector3(BorderMountainsLayout.FootCenter.x, BorderMountainsLayout.BasinFloor, BorderMountainsLayout.FootCenter.y));
                var shots = new List<Shot>
                {
                    new Shot { Name = "T01_Kareth_Nord_Eiszahn", Eye = E(-60f, 640f, 4f), Target = T(-300f, 6000f, 700f), Fov = 60f, Far = 4000f },
                    new Shot { Name = "T02_Kareth_Sued_Salzsee_Grenzgebirge", Eye = E(250f, -330f, 2.5f), Target = T(-300f, -2600f, 450f), Fov = 65f, Far = 4000f },
                    new Shot { Name = "T03_Osttunnel_Eschenfeld", Eye = E(720f, 64f, 2f), Target = T(3800f, -400f, 60f), Fov = 65f, Far = 4000f },
                    new Shot { Name = "T04_Hoher_Pass_Bruchmark", Eye = E(hp.x, hp.y, 2.5f), Target = T(-400f, -6200f, 60f), Fov = 60f, Far = 4000f },
                    new Shot { Name = "T05_Talpass_Tor", Eye = E(gate.x, gate.y + 320f, 2f), Target = T(gate.x + 200f, gate.y - 600f, 80f), Fov = 60f, Far = 4000f },
                    new Shot { Name = "T06_Schenke_Blick_Bruchmark_Kerbe", Eye = new Vector3(sch.x, BorderMountainsLayout.PlateauHeight + 2.2f, sch.y), Target = T(1500f, -8500f, 60f), Fov = 60f, Far = 4000f },
                    new Shot { Name = "T07_Schenke_Fussabdruck", Eye = new Vector3(sch.x, BorderMountainsLayout.PlateauHeight + 2.2f, sch.y), Target = foot, Fov = 60f, Far = 4000f },
                    new Shot { Name = "T08_Bruchmark_Eingang", Eye = E(be.x, be.y, 2f), Target = T(kr.x, kr.y, 40f), Fov = 60f, Far = 4000f },
                    new Shot { Name = "T09_Grosse_Kerbe_Rand", Eye = E(kr.x, kr.y + 350f, 45f), Target = T(kr2.x, kr2.y, 0f), Fov = 65f, Far = 4000f },
                    new Shot { Name = "T10_Kareth_Draufsicht_Umland", Eye = new Vector3(1800f, 3000f, -900f), Target = new Vector3(1800f, 0f, -899.99f), Ortho = 4400f, Far = 4000f },
                };
                return Render(shots, "Welt", false);
            }
            finally
            {
                foreach (var sc in opened) UnityEditor.SceneManagement.EditorSceneManager.CloseScene(sc, true);
            }
        }

        [MenuItem("Ashveil/Welt/Kolosseum: Review-Bilder", priority = 531)]
        public static void RenderColosseumMenu()
        {
            string dir = RenderColosseumPreview();
            if (dir != null) Debug.Log("[Ashveil] Kolosseum-Review gespeichert: " + dir);
        }

        /// <summary>Kolosseum Pass 2: Anmarsch, Vorplatz, Fernblick uebers Aschenfeld und Blicke in Arena 0. Nach Logs/Ashveil/Kolosseum.</summary>
        public static string RenderColosseumPreview()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return null;
            Vector3 o = ColosseumBuilder.SlotOrigin(0);
            Vector3 A(Vector3 local, float lift = 0f) => o + local + Vector3.up * lift;
            float y0 = ColosseumBuilder.ForecourtY;
            var shots = new List<Shot>
            {
                new Shot { Name = "K01_Anmarsch_Ruinen", Eye = G(493f, 206f, 1.7f), Target = new Vector3(512f, y0 + 10f, 285f), Fov = 65f, Far = 3000f },
                new Shot { Name = "K02_Vorplatz_Tor", Eye = G(462f, 283f, 1.7f), Target = new Vector3(517f, y0 + 8f, 285f), Fov = 65f, Far = 3000f },
                new Shot { Name = "K03_Ringmeister_Feuer", Eye = G(482f, 278f, 1.7f), Target = new Vector3(504f, y0 + 1.5f, 293f), Fov = 60f, Far = 3000f },
                new Shot { Name = "K04_Fernblick_Aschenfeld", Eye = G(330f, 150f, 1.8f), Target = new Vector3(520f, y0 + 18f, 285f), Fov = 55f, Far = 3000f },
                new Shot { Name = "K05_Arena_Kaempferpforte", Eye = A(ColosseumBuilder.Arrival, 1.8f), Target = A(ColosseumBuilder.Polar(0f, 40f, 4f)), Fov = 72f, Far = 1500f },
                new Shot { Name = "K06_Arena_Galerie", Eye = A(ColosseumBuilder.Polar(55f, 37.6f, ColosseumBuilder.PodiumTop), 1.8f), Target = A(Vector3.zero), Fov = 72f, Far = 1500f },
                new Shot { Name = "K07_Arena_Nordtor", Eye = A(ColosseumBuilder.Polar(0f, 22f, 1.8f)), Target = A(ColosseumBuilder.Polar(0f, 50f, 3f)), Fov = 65f, Far = 1500f },
                new Shot { Name = "K08_Arena_Draufsicht", Eye = A(new Vector3(0f, 110f, 0f)), Target = A(new Vector3(0f, 0f, 0.01f)), Ortho = 68f, Far = 400f },
                new Shot { Name = "K09_Vorplatz_Draufsicht", Eye = new Vector3(495f, 150f, 272f), Target = new Vector3(495f, 0f, 272.01f), Ortho = 75f, Far = 400f },
            };
            return Render(shots, "Kolosseum", false);
        }

        /// <summary>Startlager (Wachtfeuer unter dem Beschuetzer): Draufsicht unterhalb des Kopfes plus Blicke auf Augenhoehe.</summary>
        public static string RenderCampPreview()
        {
            var shots = new List<Shot>
            {
                // Kamera auf 24 m Hoehe: der Kopf des Riesen (ab ~30 m) liegt ueber ihr und verdeckt nichts
                new Shot { Name = "L0_Draufsicht", Eye = new Vector3(0f, 24f, -8f), Target = new Vector3(0f, 0f, -7.99f), Ortho = 50f },
                new Shot { Name = "L1_Nordtor", Eye = new Vector3(2f, 1.8f, 40f), Target = new Vector3(0f, 7f, -30f), Fov = 70f },
                new Shot { Name = "L2_Feuerplatz", Eye = new Vector3(-7f, 1.8f, -12f), Target = new Vector3(4f, 3f, 20f), Fov = 70f },
                new Shot { Name = "L3_Vom_Aussichtsfels", Eye = new Vector3(30f, 7f, 12f), Target = new Vector3(-10f, 0f, -12f), Fov = 70f },
                new Shot { Name = "L4_Schraeg_von_Nordost", Eye = new Vector3(44f, 20f, 48f), Target = new Vector3(0f, 0f, -8f), Fov = 60f },
                new Shot { Name = "L5_Vor_der_Halle", Eye = new Vector3(-16f, 2f, -22f), Target = new Vector3(8f, 5f, -40f), Fov = 70f }
            };
            return Render(shots, "Lager", false);
        }

        [MenuItem("Ashveil/Salzgruben/Vorschau rendern", priority = 431)]
        public static void RenderSaltMineMenu()
        {
            string dir = RenderSaltMinePreview();
            if (dir != null) Debug.Log("[Ashveil] Salzgruben-Vorschau gespeichert: " + dir);
        }

        private static bool _indoorShot;

        /// <summary>
        /// Blicke in Platz 0 der Salzgruben – mit der Stimmung unter Tage (Sonne aus, dunkles Umgebungslicht,
        /// Grubenstaub) und einigen Gegnern als Staffage. Nach Logs/Ashveil/Salzgruben.
        /// </summary>
        public static string RenderSaltMinePreview()
        {
            Vector3 o = SaltMineBuilder.SlotOrigin(0);
            Vector3 P(float x, float y, float z) => o + new Vector3(x, y, z);
            var shots = new List<Shot>
            {
                new Shot { Name = "S1_Foerderhalle", Eye = P(6f, 2.2f, -7f), Target = P(-1f, 1.5f, 8f), Fov = 70f },
                new Shot { Name = "S2_Hauptstollen", Eye = P(2f, 1.9f, 11f), Target = P(0f, 1.2f, 30f), Fov = 65f },
                new Shot { Name = "S3_Salzhalle", Eye = P(-3f, 2.2f, 55f), Target = P(3f, 4f, 85f), Fov = 72f },
                new Shot { Name = "S4_Flutstollen", Eye = P(22f, 1.8f, 79f), Target = P(50f, -4f, 81f), Fov = 65f },
                new Shot { Name = "S5_Salzsee_Grotte", Eye = P(66f, -5f, 66f), Target = P(92f, -7f, 92f), Fov = 72f },
                new Shot { Name = "S6_Wrackhoehle", Eye = P(-62f, -3.5f, 72f), Target = P(-86f, -4f, 62f), Fov = 70f },
                new Shot { Name = "S7_Herzkaverne", Eye = P(78f, -11.5f, 148f), Target = P(85f, -8f, 176f), Fov = 72f }
            };
            // Stimmung unter Tage (wie IndoorAtmosphere im Spiel), danach zuruecksetzen
            Light sun = RenderSettings.sun;
            bool sunOn = sun != null && sun.enabled;
            var mode = RenderSettings.ambientMode;
            Color sky = RenderSettings.ambientSkyColor, eq = RenderSettings.ambientEquatorColor, gr = RenderSettings.ambientGroundColor;
            var props = new List<GameObject>();
            try
            {
                if (sun != null) sun.enabled = false;
                RenderSettings.ambientMode = AmbientMode.Trilight;
                Ashveil.World.DungeonSlot slot = null;
                foreach (Ashveil.World.DungeonSlot ds in UnityEngine.Object.FindObjectsByType<Ashveil.World.DungeonSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (ds.Index == 0 && ds.DungeonId == SaltMineBuilder.DungeonId) slot = ds;
                if (slot != null)
                {
                    RenderSettings.ambientSkyColor = slot.AmbientSky;
                    RenderSettings.ambientEquatorColor = slot.AmbientEquator;
                    RenderSettings.ambientGroundColor = slot.AmbientGround;
                    AtmosphereController.IndoorFogColor = slot.FogColor;
                    AtmosphereController.IndoorFogDensity = slot.FogDensity;
                }
                AtmosphereController.IndoorOverride = true;
                _indoorShot = true;
                void Put(string prefab, Vector3 pos, float yaw)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectSetup.Root + "/Prefabs/Enemies/" + prefab + ".prefab");
                    if (asset == null) return;
                    var go = (GameObject)Object.Instantiate(asset, pos, Quaternion.Euler(0f, yaw, 0f));
                    go.hideFlags = HideFlags.DontSave;
                    props.Add(go);
                }
                Put("Enemy_Grubenarbeiter", P(-1.5f, 0f, 24f), 180f);
                Put("Enemy_Grubenarbeiter", P(2f, 0f, 27f), 200f);
                Put("Enemy_Grubenarbeiter", P(1f, 0f, 62f), 170f);
                Put("Enemy_Salzkrebs", P(42f, -4.3f, 80f), 260f);
                Put("Enemy_Salzkrebs", P(74f, -8f, 90f), 220f);
                Put("Enemy_NadelspeierBrut", P(-74f, -6f, 60f), 90f);
                Put("Enemy_Grubenschrat", P(85f, -14f, 158f), 180f);
                var player = AssetDatabase.LoadAssetAtPath<GameObject>(SandboxSceneBuilder.PlayerPrefabPath);
                if (player != null)
                {
                    var pg = (GameObject)Object.Instantiate(player, P(1.5f, 0f, -1f), Quaternion.Euler(0f, 10f, 0f));
                    pg.hideFlags = HideFlags.DontSave;
                    props.Add(pg);
                }
                return Render(shots, "Salzgruben", false);
            }
            finally
            {
                foreach (GameObject p in props) if (p != null) Object.DestroyImmediate(p);
                _indoorShot = false;
                AtmosphereController.IndoorOverride = false;
                if (sun != null) sun.enabled = sunOn;
                RenderSettings.ambientMode = mode;
                RenderSettings.ambientSkyColor = sky;
                RenderSettings.ambientEquatorColor = eq;
                RenderSettings.ambientGroundColor = gr;
                AtmosphereController.Active?.Apply();
            }
        }

        /// <summary>Vier Pflicht-Perspektiven des Nachtrags (Lager, Zugang, Schaedeldecke, Fernsilhouette) plus Draufsicht.</summary>
        public static string RenderGiantPreview()
        {
            var shots = new List<Shot>
            {
                new Shot { Name = "R1_Lager_unter_dem_Beschuetzer", Eye = new Vector3(9f, 1.8f, 19f), Target = new Vector3(-2f, 26f, -40f), Fov = 72f },
                new Shot { Name = "R2_Halle_des_Hauptmanns", Eye = new Vector3(3f, 1.9f, -14f), Target = new Vector3(0f, 5f, -60f), Fov = 62f },
                new Shot { Name = "R3_Zugang_Ostweg", Eye = G(215f, -62f, 1.8f), Target = new Vector3(40f, 38f, -20f), Fov = 60f },
                new Shot { Name = "R4_Bogen_am_Salzweg", Eye = G(-78f, -70f, 1.8f), Target = G(-132f, -108f, 12f), Fov = 62f },
                new Shot { Name = "R5_Schaedeldecke", Eye = G(24f, 112f, 3f), Target = G(78f, 92f, 5f), Fov = 62f },
                new Shot { Name = "R6_Fernsilhouette_Plateau", Eye = G(60f, 598f, 4f), Target = new Vector3(0f, 75f, -110f), Fov = 42f },
                new Shot { Name = "R7_Fernsilhouette_Ost", Eye = G(470f, 120f, 6f), Target = new Vector3(0f, 80f, -110f), Fov = 40f },
                new Shot { Name = "R8_Kratersee", Eye = new Vector3(40f, 215f, -40f), Target = new Vector3(-40f, 150f, -125f), Fov = 55f },
                new Shot { Name = "R9_Draufsicht", Eye = new Vector3(0f, 900f, 150f), Target = new Vector3(0f, 0f, 150.01f), Ortho = 520f }
            };
            return Render(shots, "Riese", false);
        }

        public static string RenderAll()
        {
            var shots = new List<Shot>
            {
                new Shot { Name = "01_Wachtfeuer", Eye = new Vector3(9f, 3.4f, -17f), Target = new Vector3(0f, 1.6f, 1f), Fov = 55f },
                new Shot { Name = "02_Lager_Abend", Eye = new Vector3(-4f, 1.9f, 15f), Target = new Vector3(1.5f, 1.1f, -1f), Fov = 58f },
                new Shot { Name = "03_Kiefernhang", Eye = G(-120f, 95f, 2.2f), Target = G(-185f, 150f, 5f), Fov = 60f },
                new Shot { Name = "04_Glockenmoor", Eye = G(-5f, 235f, 4f), Target = G(70f, 340f, 6f), Fov = 55f },
                new Shot { Name = "05_Aschenfeld_Glockenhirte", Eye = G(255f, -20f, 3f), Target = G(330f, -15f, 9f), Fov = 55f },
                new Shot { Name = "06_Blick_vom_Plateau", Eye = G(55f, 596f, 3f), Target = G(10f, 250f, 0f), Fov = 60f },
                new Shot { Name = "07_Steilkueste", Eye = G(-195f, -305f, 6f), Target = G(-430f, -110f, 0f), Fov = 60f }
            };
            return Render(shots, "Stil", true);
        }

        private static string Render(List<Shot> shots, string folder, bool extras)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return null;
            string dir = Ashveil.Core.ProjectPaths.Logs(folder);
            Directory.CreateDirectory(dir);
            var props = new List<GameObject>();
            GameObject camGo = null;
            RenderTexture rt = null;
            try
            {
                if (extras) props.AddRange(PlaceExtras());
                foreach (ParticleSystem ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                    ps.Simulate(6f, true, true);
                AtmosphereController.Active?.Apply();
                Terrain terrain = Ashveil.Core.WorldGround.TerrainAt(new Vector3(255f, 0f, -20f));
                if (terrain != null)
                {
                    TerrainData td = terrain.terrainData;
                    long sum = 0;
                    if (td.detailPrototypes.Length > 0)
                        foreach (int v in td.GetDetailLayer(0, 0, td.detailWidth, td.detailHeight, 0)) sum += v;
                    Vector3 local = new Vector3(255f, 0f, -20f) - terrain.transform.position;
                    int ax = Mathf.Clamp((int)(local.x / td.size.x * td.alphamapWidth), 0, td.alphamapWidth - 1);
                    int az = Mathf.Clamp((int)(local.z / td.size.z * td.alphamapHeight), 0, td.alphamapHeight - 1);
                    float[,,] probe = td.GetAlphamaps(ax, az, 1, 1);
                    var weights = new System.Text.StringBuilder();
                    for (int l = 0; l < probe.GetLength(2); l++) weights.Append(probe[0, 0, l].ToString("0.00")).Append(' ');
                    Debug.Log($"[Ashveil] Stilbilder: Splat im Aschenfeld [{weights}], Splat-Texturen {td.alphamapTextureCount}");
                    Debug.Log($"[Ashveil] Stilbilder: Terrain-Details {td.detailPrototypes.Length} Arten, Gras-Summe {sum}, " +
                              $"Aufloesung {td.detailResolution}, Distanz {terrain.detailObjectDistance}, zeichnen {terrain.drawTreesAndFoliage}");
                }

                camGo = new GameObject("Stilbild-Kamera") { hideFlags = HideFlags.HideAndDontSave };
                var cam = camGo.AddComponent<Camera>();
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 2500f;
                cam.allowHDR = true;
                if (_indoorShot)
                {
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = Color.black;
                }
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                int preview = LayerMask.NameToLayer("Preview");
                if (preview >= 0) cam.cullingMask &= ~(1 << preview);

                rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                int i = 0;
                foreach (Shot s in shots)
                {
                    EditorUtility.DisplayProgressBar("Ashveil", "Stilbild " + s.Name, (float)i++ / shots.Count);
                    cam.transform.position = s.Eye;
                    cam.transform.rotation = Quaternion.LookRotation(s.Target - s.Eye);
                    cam.orthographic = s.Ortho > 0f;
                    if (s.Ortho > 0f)
                    {
                        cam.orthographicSize = s.Ortho;
                        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                        cam.farClipPlane = s.Far > 0f ? s.Far : 1400f;
                    }
                    else
                    {
                        cam.fieldOfView = s.Fov;
                        cam.farClipPlane = s.Far > 0f ? s.Far : 2500f;
                    }
                    cam.targetTexture = rt;
                    var request = new RenderPipeline.StandardRequest { destination = rt };
                    if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
                    else cam.Render();
                    RenderTexture prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(dir, s.Name + ".png"), tex.EncodeToPNG());
                }
                Object.DestroyImmediate(tex);
                Debug.Log($"[Ashveil] {shots.Count} Stilbilder -> {dir}");
                return dir;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                foreach (GameObject p in props) if (p != null) Object.DestroyImmediate(p);
                if (camGo != null) Object.DestroyImmediate(camGo);
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            }
        }

        /// <summary>Staffage nur fuer die Bilder: Gegner-Modelle und eine Spielfigur (werden danach entfernt).</summary>
        private static IEnumerable<GameObject> PlaceExtras()
        {
            var list = new List<GameObject>();
            void Put(string prefabPath, Vector3 pos, float yaw)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) return;
                var go = (GameObject)Object.Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f));
                go.hideFlags = HideFlags.DontSave;
                list.Add(go);
            }
            string e = ProjectSetup.Root + "/Prefabs/Enemies/";
            Put(e + "Enemy_Glockenhirte.prefab", G(330f, -15f, 0f), 250f);
            Put(e + "Enemy_Torfschrat.prefab", G(62f, 318f, 0f), 200f);
            Put(e + "Enemy_Aschwolf.prefab", G(-178f, 142f, 0f), 120f);
            Put(e + "Enemy_Aschwolf.prefab", G(-181f, 146f, 0f), 150f);
            Put(SandboxSceneBuilder.PlayerPrefabPath, new Vector3(2.5f, 0f, -3f), 200f);
            return list;
        }
    }
}
