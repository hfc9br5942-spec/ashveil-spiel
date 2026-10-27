using Ashveil.Rendering.Sky;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Dynamic Sky V1: legt die Himmelsdaten an (Klima, 7 Wolkenprofile, 2 Monde, Sternbilder, Beispiel-Biome und
    /// Sternschnuppenschauer fuer spaeter), das Himmelsmaterial "Ashveil/DynamicSky" und den SkyDirector in der Szene.
    /// Werte in den Assets duerfen im Inspector veraendert werden; sie werden nur neu geschrieben, wenn sich der
    /// Stand dieses Bauplans aendert (<see cref="Version"/>) oder ueber das Menue ausdruecklich zurueckgesetzt.
    /// </summary>
    public static class SkyContentBuilder
    {
        public const int Version = 2; // 2: Gebietsluft (Pass 11) und Sternschnuppen (Pass 12) aktiv
        private const string Folder = ProjectSetup.Root + "/Data/Sky";
        private const string MaterialPath = ProjectSetup.Root + "/Art/Rendering/Sky_Ashveil_Dynamic.mat";

        [MenuItem("Ashveil/Grafik/Himmel-Daten zuruecksetzen (Standard)", priority = 140)]
        public static void ResetData()
        {
            Build(true);
            Debug.Log("[Ashveil] Himmel-Daten auf den Standard zurueckgesetzt: " + Folder);
        }

        /// <summary>Beim Szenenbau: Daten sicherstellen, Material setzen, SkyDirector an das Atmosphaeren-Objekt haengen.</summary>
        public static void Install(GameObject host, Light sun)
        {
            SkyClimateProfile climate = Build(false);
            Material mat = SkyMaterial();
            if (mat != null) RenderSettings.skybox = mat;
            var director = host.GetComponent<SkyDirector>();
            if (director == null) director = host.AddComponent<SkyDirector>();
            GreyboxKit.SetRef(director, "climate", climate);
            GreyboxKit.SetRef(director, "sun", sun);
        }

        private static Material SkyMaterial()
        {
            Shader sh = Shader.Find("Ashveil/DynamicSky");
            if (sh == null)
            {
                Debug.LogWarning("[Ashveil] Shader Ashveil/DynamicSky fehlt – Himmel bleibt beim alten Verlauf.");
                return null;
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(sh) { name = "Sky_Ashveil_Dynamic" };
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            mat.shader = sh;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        public static SkyClimateProfile Build(bool force)
        {
            ProjectSetup.EnsureFolder(Folder);
            var climate = Load<SkyClimateProfile>("SkyClimate");
            if (climate != null && !force && climate.builderVersion >= Version) return climate;
            climate ??= Create<SkyClimateProfile>("SkyClimate");

            climate.worldSeed = 7719;
            climate.monthDays = 30;
            climate.firstState = SkyState.Scattered; // Tag 0 = gestaltetes Kareth-Licht mit lockeren Wolken
            climate.transitionHours = new Vector2(3f, 7f);
            climate.lowWind = new Vector2(0.0022f, 0.0009f);   // aus Suedwest, langsam
            climate.highWind = new Vector2(0.0055f, -0.0006f); // aus West, schneller (§ 40)
            climate.windSwing = 0.7f;
            climate.windSwingDays = 5.3f;
            climate.evolution = 0.0016f;
            climate.latitude = 48f;
            climate.moonLight = new Vector2(0.55f, 1.05f);
            climate.starDensity = 0.055f;
            climate.milkyWay = 0.35f;

            climate.clouds = new[]
            {
                Cloud(SkyState.Clear,       0.04f, 0.40f, 1.0f, 0.5f, 0.10f, 0.06f, 0.40f, new Vector2(0.97f, 1.00f), 0.15f, 0.14f, 0f,   new Vector2(10f, 30f)),
                Cloud(SkyState.ClearCirrus, 0.06f, 0.50f, 1.0f, 0.5f, 0.10f, 0.55f, 0.55f, new Vector2(0.92f, 0.99f), 0.20f, 0.14f, 1f,   new Vector2(8f, 24f)),
                Cloud(SkyState.Scattered,   0.36f, 0.75f, 1.0f, 0.55f, 0.20f, 0.22f, 0.45f, new Vector2(0.80f, 1.00f), 0.20f, 0.22f, 2f,  new Vector2(8f, 26f)),
                Cloud(SkyState.Broken,      0.60f, 0.85f, 1.2f, 0.5f, 0.35f, 0.25f, 0.40f, new Vector2(0.60f, 0.95f), 0.28f, 0.15f, 3f,   new Vector2(6f, 20f)),
                Cloud(SkyState.Dramatic,    0.52f, 1.00f, 1.9f, 0.3f, 0.65f, 0.35f, 0.50f, new Vector2(0.50f, 0.97f), 0.30f, 0.06f, 3.5f, new Vector2(4f, 10f)),
                Cloud(SkyState.Overcast,    0.93f, 0.90f, 0.85f, 0.35f, 0.45f, 0.10f, 0.30f, new Vector2(0.35f, 0.60f), 0.45f, 0.15f, 4f, new Vector2(8f, 26f)),
                Cloud(SkyState.Haze,        0.10f, 0.50f, 1.0f, 0.6f, 0.10f, 0.15f, 0.35f, new Vector2(0.78f, 0.90f), 0.80f, 0.12f, 1.5f, new Vector2(8f, 20f))
            };

            climate.moonA = Moon("Moon_A", "Silbermond", 12.4f, 7f, 40f, 0.18f, 1.5f, new Color(0.93f, 0.91f, 0.86f), 1f, 11);
            climate.moonB = Moon("Moon_B", "Kupfermond", 31.3f, 18f, 200f, 0.62f, 0.8f, new Color(0.86f, 0.66f, 0.48f), 0.45f, 23);

            // Farben ueber den Tag: kaltes Blau gegen warmes Licht, abends Teal gegen Bernstein, nachts Indigo (§ 11)
            Color nightZ = C(0.020f, 0.028f, 0.065f), nightH = C(0.065f, 0.08f, 0.13f), nightG = C(0.015f, 0.015f, 0.02f), noGlow = new Color(0.3f, 0.35f, 0.6f, 0f);
            climate.colorKeys = new[]
            {
                new SkyColorKey(0f, nightZ, nightH, nightG, noGlow),
                new SkyColorKey(4.6f, nightZ, nightH, nightG, noGlow),
                new SkyColorKey(5.5f, C(0.12f, 0.15f, 0.27f), C(0.55f, 0.42f, 0.42f), C(0.06f, 0.05f, 0.05f), new Color(1f, 0.5f, 0.35f, 0.85f)),
                new SkyColorKey(6.6f, C(0.27f, 0.35f, 0.52f), C(0.78f, 0.64f, 0.55f), C(0.10f, 0.09f, 0.08f), new Color(1f, 0.66f, 0.42f, 0.65f)),
                new SkyColorKey(9f, C(0.32f, 0.44f, 0.62f), C(0.66f, 0.70f, 0.75f), C(0.12f, 0.12f, 0.11f), new Color(1f, 0.9f, 0.78f, 0.25f)),
                new SkyColorKey(12.5f, C(0.30f, 0.43f, 0.63f), C(0.66f, 0.71f, 0.77f), C(0.13f, 0.13f, 0.12f), new Color(1f, 0.95f, 0.85f, 0.2f)),
                new SkyColorKey(15.5f, C(0.33f, 0.42f, 0.57f), C(0.69f, 0.69f, 0.70f), C(0.12f, 0.11f, 0.10f), new Color(1f, 0.84f, 0.62f, 0.35f)),
                new SkyColorKey(18.2f, C(0.26f, 0.32f, 0.47f), C(0.80f, 0.60f, 0.46f), C(0.10f, 0.08f, 0.07f), new Color(1f, 0.58f, 0.33f, 0.75f)),
                new SkyColorKey(19.5f, C(0.11f, 0.13f, 0.25f), C(0.52f, 0.33f, 0.30f), C(0.05f, 0.04f, 0.04f), new Color(0.95f, 0.42f, 0.28f, 0.75f)),
                new SkyColorKey(20.8f, nightZ, nightH, nightG, noGlow)
            };

            climate.constellations = new[]
            {
                Con("Die Glocke", "Das Zeichen der verbrannten Glockenstadt – steht im Herbst hoch ueber Kareth.",
                    S(30, 44, 1f), S(36, 46, 0.6f), S(27, 38, 0.7f), S(39, 38, 0.7f), S(25, 31, 0.8f), S(41, 31, 0.8f), S(33, 28, 0.5f)),
                Con("Der Waechter", "Eine grosse Gestalt mit gesenktem Kopf; manche sagen, sie sehe aus wie der Beschuetzer.",
                    S(86, 30, 1f), S(84, 22, 0.7f), S(88, 22, 0.7f), S(86, 14, 0.8f), S(82, 5, 0.6f), S(90, 5, 0.6f)),
                Con("Die Sense", "Erntezeichen der Moorbauern.",
                    S(130, 50, 0.9f), S(138, 54, 0.6f), S(146, 56, 0.6f), S(154, 55, 0.7f), S(128, 42, 0.5f), S(126, 34, 0.8f)),
                Con("Das Wachtfeuer", "Drei Sterne wie Scheite, darueber zwei Funken.",
                    S(175, 10, 0.9f), S(181, 4, 0.7f), S(169, 4, 0.7f), S(175, 17, 0.5f), S(175, 23, 0.4f)),
                Con("Der Aschenvogel", "Ausgebreitete Schwingen; bringt nach altem Glauben den Morgen.",
                    S(215, 35, 1f), S(205, 40, 0.6f), S(196, 43, 0.5f), S(225, 40, 0.6f), S(234, 43, 0.5f), S(215, 27, 0.7f), S(215, 20, 0.5f)),
                Con("Die Waage", "Zwei Schalen und der Balken.",
                    S(250, -5, 0.8f), S(262, -5, 0.8f), S(256, 2, 0.9f), S(247, -14, 0.5f), S(265, -14, 0.5f)),
                Con("Die Kette", "Fuenf Glieder in einer Reihe.",
                    S(285, 20, 0.6f), S(291, 24, 0.6f), S(297, 21, 0.7f), S(303, 25, 0.6f), S(309, 22, 0.8f)),
                Con("Der Hirte", "Mit Stab, nahe dem Pol.",
                    S(322, 55, 0.9f), S(328, 48, 0.6f), S(318, 46, 0.6f), S(323, 39, 0.7f), S(331, 36, 0.5f), S(315, 62, 0.5f)),
                Con("Das Tor", "Zwei Saeulen und ein Sturz – tief im Sueden.",
                    S(345, -18, 0.8f), S(345, -8, 0.6f), S(357, -18, 0.8f), S(357, -8, 0.6f), S(351, -3, 0.9f)),
                Con("Die Schlange", "Windet sich tief ueber dem Suedhorizont.",
                    S(60, -20, 0.6f), S(66, -16, 0.7f), S(72, -20, 0.6f), S(78, -16, 0.7f), S(84, -22, 0.8f), S(90, -26, 0.5f)),
                Con("Der Erste", "Ein einzelner heller Stern nahe dem Himmelspol, um den sich alles dreht.", S(0, 80, 1.4f))
            };

            // Pass 12: einzelne Sternschnuppen jede klare Nacht, selten ein Schauer (etwa alle 20 Tage, aus dem Seed)
            climate.sporadicMeteorsPerHour = 8f;
            SkyEventDefinition shower = Load<SkyEventDefinition>("SkyEvent_MeteorShower") ?? Create<SkyEventDefinition>("SkyEvent_MeteorShower");
            shower.id = "meteor_shower";
            shower.displayName = "Sternschnuppenschauer";
            shower.chancePerDay = 0.05f;
            shower.startHour = new Vector2(22f, 1.5f);
            shower.durationHours = new Vector2(1.5f, 3.5f);
            shower.intensity = 0.7f;
            shower.frequency = 3f;
            shower.radiant = new Vector2(60f, 40f);
            shower.trailLifetime = 0.8f;
            EditorUtility.SetDirty(shower);
            climate.events = new[] { shower };
            // Pass 11: Gebietsluft – verschiebt nur Nebel/Dunst/Wolken, der Himmel bleibt global
            Biome("Biome_Startlager", "Startlager", Color.white, 1f, 0f, 0f, 1f, 1f, 6f);
            Biome("Biome_Glockenmoor", "Glockenmoor", new Color(0.86f, 0.93f, 0.88f), 1.55f, 0.3f, 0.12f, 0.82f, 0.72f, 8f);

            climate.builderVersion = Version;
            EditorUtility.SetDirty(climate);
            AssetDatabase.SaveAssets();
            SkyMonthSchedule.ClearCache();
            return climate;
        }

        // ------------------------------------------------------------------ Hilfen

        private static Color C(float r, float g, float b) => new Color(r, g, b, 1f);
        private static ConstellationStar S(float ra, float dec, float b) => new ConstellationStar(ra, dec, b);
        private static Constellation Con(string name, string lore, params ConstellationStar[] stars) => new Constellation { name = name, lore = lore, stars = stars };

        private static CloudProfile Cloud(SkyState st, float cov, float dens, float scale, float erosion, float dark, float hiCov, float hiDens,
            Vector2 trans, float haze, float weight, float order, Vector2 dwell)
        {
            var p = Load<CloudProfile>("Cloud_" + st) ?? Create<CloudProfile>("Cloud_" + st);
            p.state = st;
            p.lowCoverage = cov; p.lowDensity = dens; p.lowScale = scale; p.lowErosion = erosion; p.darkness = dark;
            p.highCoverage = hiCov; p.highDensity = hiDens; p.sunTransmittance = trans; p.horizonHaze = haze;
            p.weight = weight; p.order = order; p.dwellHours = dwell;
            EditorUtility.SetDirty(p);
            return p;
        }

        private static CelestialBodyDefinition Moon(string file, string name, float period, float incl, float lon, float phase, float radius, Color color, float light, int seed)
        {
            var m = Load<CelestialBodyDefinition>(file) ?? Create<CelestialBodyDefinition>(file);
            m.displayName = name; m.orbitalPeriodDays = period; m.inclination = incl; m.longitude = lon; m.phaseOffset = phase;
            m.apparentRadius = radius; m.color = color; m.lightShare = light; m.surfaceSeed = seed;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static void Biome(string file, string name, Color tint, float fog, float haze, float cloud, float sun, float vis, float blend)
        {
            var b = Load<BiomeAtmosphereProfile>(file) ?? Create<BiomeAtmosphereProfile>(file);
            b.displayName = name; b.fogTint = tint; b.fogDensity = fog; b.hazeBias = haze; b.cloudBias = cloud; b.sunTransmission = sun; b.visibility = vis;
            b.blendSeconds = blend;
            EditorUtility.SetDirty(b);
        }

        /// <summary>Pass 11: Gebiete mit eigener Luft in der Kareth-Welt (Startlager, Glockenmoor) mit breiten Blendzonen.</summary>
        public static void PlaceBiomes(Transform env, Vector3 camp, Vector3 moor)
        {
            Build(false);
            Transform root = env.Find("Gebietsluft");
            if (root != null) Object.DestroyImmediate(root.gameObject);
            root = new GameObject("Gebietsluft").transform;
            root.SetParent(env, false);
            Volume(root, "Biome_Startlager", camp, 90f, 60f);
            Volume(root, "Biome_Glockenmoor", moor, 125f, 90f);
        }

        /// <summary>
        /// Grenzgebirge (27.09.): Talpass feucht und dunstig, Gebirgspass klar und kalt, Klippenschenke mit weiter Sicht
        /// (Sichttest 3/4), Lichtung mit leichtem Bodendunst. Wird vom Grenzgebirge-Bauer angelegt, nicht vom Himmel-Neuaufbau.
        /// </summary>
        public static void EnsureBorderBiomes()
        {
            ProjectSetup.EnsureFolder(Folder);
            Biome("Biome_Grenzgebirge_Tal", "Talpass", new Color(0.84f, 0.9f, 0.93f), 1.35f, 0.25f, 0.08f, 0.85f, 0.8f, 8f);
            Biome("Biome_Grenzgebirge_Hoehe", "Gebirgspass", new Color(0.86f, 0.9f, 1f), 0.75f, 0.05f, 0.05f, 1.05f, 1.3f, 10f);
            Biome("Biome_Klippenschenke", "Klippenschenke", new Color(0.95f, 0.93f, 0.9f), 0.6f, 0f, 0f, 1.05f, 1.45f, 6f);
            Biome("Biome_Lichtung", "Lichtung", new Color(0.9f, 0.95f, 0.9f), 1.15f, 0.15f, 0f, 0.95f, 0.95f, 8f);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Gebietsluft an einer Stelle (Kreis mit Blendzone) – auch fuer Zellen-Szenen.</summary>
        public static void PlaceVolume(Transform root, string file, Vector3 pos, float radius, float falloff) => Volume(root, file, pos, radius, falloff);

        private static void Volume(Transform root, string file, Vector3 pos, float radius, float falloff)
        {
            var p = Load<BiomeAtmosphereProfile>(file);
            var go = new GameObject("Luft " + (p != null ? p.displayName : file));
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            go.AddComponent<BiomeAtmosphereVolume>().Configure(p, radius, falloff);
        }

        private static T Load<T>(string name) where T : ScriptableObject => AssetDatabase.LoadAssetAtPath<T>($"{Folder}/{name}.asset");

        private static T Create<T>(string name) where T : ScriptableObject
        {
            var a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, $"{Folder}/{name}.asset");
            return a;
        }
    }
}
