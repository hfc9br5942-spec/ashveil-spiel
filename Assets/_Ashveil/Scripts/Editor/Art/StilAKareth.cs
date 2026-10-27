using System.IO;
using Ashveil.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Stil A „Gereifte Heldenfantasy“ (Max 05.10.2026, Präzisierung von ART-107): erster Licht- und Atmosphäre-Pass
    /// für die Szene Kareth_v4. Nur Licht, Luft und Farbe – keine Formen, Texturen oder Inhalte.
    ///
    /// Was der Pass tut:
    /// - Höhennebel mit Luftperspektive (vorhandener <see cref="AtmosphereController"/>, auf Talmaßstab eingestellt)
    /// - eigenes Volume-Profil: gedämpftere Sättigung, kühle Schatten, warme Lichter, ACES, milder Bloom, leichte Vignette
    /// - tieferer Sonnenstand (längere Schatten), weniger und kühleres Umgebungslicht (mehr Tiefe)
    /// - eigener Himmel (Kopie), damit Sandbox_M1 und Spieltest r2 unverändert bleiben
    /// - Tageslauf (Welle 0, 06.10.2026, NET-020 Echtzeit): <see cref="DayNightCycle"/> am selben Objekt; 15:30 zeigt genau
    ///   dieses Licht. Seit #79 (07.10.2026) mit dem Profil „Stil A“: Morgen, Abend und Nacht auf Stil A abgestimmt.
    ///   Himmel V1 (Wolken, zwei Monde, Sterne) nur mit dem Schalter „Himmel V1 beim Bau“ (Standard aus: der Stil-A-Himmel
    ///   bleibt, nachts nur abgedunkelt).
    ///
    /// Umkehrbar: „aus“ stellt Sonne, Umgebungslicht und Himmel auf den Stand vor dem Einschalten zurück.
    /// Der Schalter gilt auch für den Neubau (<see cref="KarethV4Builder"/>).
    /// Menü: Ashveil › Welt › Stil A (gereifte Heldenfantasy).
    /// </summary>
    public static class StilAKareth
    {
        public const string Key = "Ashveil.StilA.KarethV4";
        public const string HimmelKey = "Ashveil.StilA.KarethV4.HimmelV1";
        private const string SnapshotKey = "Ashveil.StilA.KarethV4.Vorher";
        private const string Folder = ProjectSetup.Root + "/Art/Rendering";
        private const string ProfilePath = Folder + "/Ashveil_StilA_Kareth.asset";
        private const string SkyPath = Folder + "/Sky_StilA_Kareth.mat";
        private const string FogMaterialPath = Folder + "/M_AtmosphereFog.mat";
        public const string RootName = "Stil A (Licht und Atmosphaere)";

        public static bool Enabled => EditorPrefs.GetBool(Key, false);

        /// <summary>Himmel V1 (SkyDirector: Wolken, Monde, Sterne) in Kareth v4 – Standard aus (06.10., bis Max entscheidet).</summary>
        public static bool HimmelV1
        {
            get => EditorPrefs.GetBool(HimmelKey, false);
            set => EditorPrefs.SetBool(HimmelKey, value);
        }

        private const string MenuHimmel = "Ashveil/Welt/Stil A (gereifte Heldenfantasy)/Himmel V1 beim Bau (Wolken, Monde, Sterne)";

        [MenuItem(MenuHimmel, priority = 573)]
        private static void HimmelMenu()
        {
            HimmelV1 = !HimmelV1;
            Debug.Log("[Ashveil] Stil A: Himmel V1 beim Bau " + (HimmelV1 ? "an" : "aus") + " (gilt beim nächsten Bau oder „Stil A an“).");
        }

        [MenuItem(MenuHimmel, true)]
        private static bool HimmelMenuPruefen()
        {
            Menu.SetChecked(MenuHimmel, HimmelV1);
            return true;
        }

        // ================================================================== Menü

        [MenuItem("Ashveil/Welt/Stil A (gereifte Heldenfantasy)/An (Kareth v4)", priority = 570)]
        public static void OnMenu()
        {
            if (!OpenKareth()) return;
            EditorPrefs.SetBool(Key, true);
            string r = Apply();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log("[Ashveil] Stil A an: " + r);
        }

        [MenuItem("Ashveil/Welt/Stil A (gereifte Heldenfantasy)/Aus (Kareth v4)", priority = 571)]
        public static void OffMenu()
        {
            if (!OpenKareth()) return;
            EditorPrefs.SetBool(Key, false);
            string r = Remove();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log("[Ashveil] Stil A aus: " + r);
        }

        [MenuItem("Ashveil/Welt/Stil A (gereifte Heldenfantasy)/Vergleich vorher - nachher (Review-Bilder)", priority = 572)]
        public static void CompareMenu()
        {
            string r = Compare();
            if (r != null) Debug.Log("[Ashveil] Stil A Vergleich: " + r);
        }

        private static bool OpenKareth()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play beenden.", "OK");
                return false;
            }
            if (!File.Exists(KarethV4Builder.ScenePath))
            {
                EditorUtility.DisplayDialog("Ashveil", "Kareth v4 ist noch nicht gebaut.", "OK");
                return false;
            }
            if (EditorSceneManager.GetActiveScene().path != KarethV4Builder.ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
                EditorSceneManager.OpenScene(KarethV4Builder.ScenePath, OpenSceneMode.Single);
            }
            return true;
        }

        /// <summary>Review-Bilder ohne und mit Stil A: Logs/Ashveil/KarethV4/StilA_vorher und StilA_nachher.</summary>
        public static string Compare()
        {
            if (!OpenKareth()) return null;
            bool was = Enabled;
            try
            {
                Remove();
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                string a = KarethV4Shots.Render("StilA_vorher");
                Apply();
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                string b = KarethV4Shots.Render("StilA_nachher");
                return $"{a} | {b}";
            }
            finally
            {
                // Schalterstand wie vorher herstellen
                if (!was)
                {
                    if (EditorSceneManager.GetActiveScene().path != KarethV4Builder.ScenePath)
                        EditorSceneManager.OpenScene(KarethV4Builder.ScenePath, OpenSceneMode.Single);
                    Remove();
                    EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                }
                EditorPrefs.SetBool(Key, was);
            }
        }

        // ================================================================== Anwenden / Entfernen

        [System.Serializable]
        private class Snapshot
        {
            public Vector3 sunEuler;
            public Color sunColor;
            public float sunIntensity, sunShadowStrength;
            public Color ambSky, ambEquator, ambGround;
            public bool fog;
            public string skyPath;
        }

        private static Light FindSun()
        {
            if (RenderSettings.sun != null) return RenderSettings.sun;
            Light any = Object.FindAnyObjectByType<Light>();   // TECH-007: FindObjectsByType ist in 6.6 veraltet
            return any != null && any.type == LightType.Directional ? any : null;
        }

        /// <summary>Stil A in der offenen Szene anwenden (erneutes Anwenden ersetzt den alten Stand).</summary>
        public static string Apply()
        {
            ProjectSetup.EnsureFolder(Folder);
            Light sun = FindSun();
            if (sun == null) return "keine Sonne in der Szene gefunden – nichts geändert.";

            GameObject old = GameObject.Find(RootName);
            if (old == null)
            {
                // Erst beim ersten Einschalten den Ausgangsstand merken
                var s = new Snapshot
                {
                    sunEuler = sun.transform.eulerAngles, sunColor = sun.color, sunIntensity = sun.intensity,
                    sunShadowStrength = sun.shadowStrength, ambSky = RenderSettings.ambientSkyColor,
                    ambEquator = RenderSettings.ambientEquatorColor, ambGround = RenderSettings.ambientGroundColor,
                    fog = RenderSettings.fog,
                    skyPath = RenderSettings.skybox != null ? AssetDatabase.GetAssetPath(RenderSettings.skybox) : ""
                };
                EditorPrefs.SetString(SnapshotKey, JsonUtility.ToJson(s));
            }
            else Object.DestroyImmediate(old);

            // ---------- Sonne: tiefer (längere Schatten, Streiflicht), aus Südwest (Max 05.10., KarethV4Builder.SunYaw)
            float yaw = KarethV4Builder.SunYaw;
            sun.transform.rotation = Quaternion.Euler(28f, yaw, 0f);
            sun.color = new Color(1f, 0.84f, 0.66f);
            sun.intensity = 2.0f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.8f;

            // ---------- Umgebungslicht: kühler (Tiefe), aber hell genug, dass Schattenseiten lesbar bleiben (Stil A, nicht B)
            // 05.10. abends: Mit Südwest-Sonne liegen Nordwände (Felsstirn, Halle) ganz im Schatten und waren fast schwarz →
            // mehr Himmelslicht von der Seite und warmes Bodenlicht (vom besonnten Hof zurückgeworfen). Vorher Horizont
            // 0,40/0,41/0,40, Boden 0,17/0,15/0,13.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.54f, 0.60f, 0.70f);
            RenderSettings.ambientEquatorColor = new Color(0.47f, 0.48f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.27f, 0.23f, 0.19f);
            RenderSettings.fog = false;   // Nebel macht der Höhennebel-Pass, sonst doppelt
            RenderSettings.skybox = SkyMaterial();
            RenderSettings.sun = sun;

            // ---------- Objekt mit Höhennebel und Volume
            var go = new GameObject(RootName);
            var atmo = go.AddComponent<AtmosphereController>();
            var so = new SerializedObject(atmo);
            Ref(so, "fogMaterial", FogMaterial());
            Ref(so, "sun", sun);
            // Talmaßstab Kareth (Höhen ≈ −30 … +600 m): Dunst nimmt nach oben langsam ab, Berge bleiben Silhouette
            Col(so, "fogColor", new Color(0.58f, 0.62f, 0.68f));
            F(so, "density", 0.0006f);
            F(so, "heightFalloff", 0.006f);
            F(so, "baseHeight", 0f);
            F(so, "maxOpacity", 0.82f);
            F(so, "startDistance", 25f);
            F(so, "haze", 0.00012f);
            F(so, "horizonSharpness", 5f);
            F(so, "skyDistance", 2500f);
            Col(so, "sunScatter", new Color(1.05f, 0.82f, 0.58f));
            F(so, "sunPower", 8f);
            so.ApplyModifiedPropertiesWithoutUndo();
            atmo.Apply();

            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 5f;
            volume.sharedProfile = Profile();

            // ---------- Tageslauf (Welle 0): Weltuhr in Echtzeit, 15:30 = dieses Licht; läuft nur im Spiel.
            // Seit #79 (07.10.2026) mit den auf Stil A abgestimmten Stützstellen (Morgen, Abend, Nacht).
            var cycle = go.AddComponent<DayNightCycle>();
            var cso = new SerializedObject(cycle);
            Ref(cso, "sun", sun);
            SerializedProperty profil = cso.FindProperty("profil");
            if (profil != null) profil.enumValueIndex = (int)DayNightCycle.LichtProfil.StilA;
            else Debug.LogWarning("[Ashveil] Stil A: Feld fehlt profil (DayNightCycle)");
            cso.ApplyModifiedPropertiesWithoutUndo();
            string himmel = SkyPath;
            if (HimmelV1)
            {
                SkyContentBuilder.Install(go, sun);   // setzt den dynamischen Himmel als Skybox
                himmel = "Himmel V1 (dynamisch)";
            }

            EditorSceneManager.MarkSceneDirty(go.scene);
            return $"Sonne 28° (Richtung {yaw:0}°), Höhennebel, Profil {ProfilePath}, Himmel {himmel}, Tageslauf (Weltuhr, Stützstellen {cycle.Profil})";
        }

        /// <summary>Stil A aus der offenen Szene entfernen und den gemerkten Ausgangsstand herstellen.</summary>
        public static string Remove()
        {
            GameObject old = GameObject.Find(RootName);
            if (old == null) return "Stil A war nicht aktiv.";
            Object.DestroyImmediate(old);
            string json = EditorPrefs.GetString(SnapshotKey, "");
            Light sun = FindSun();
            if (!string.IsNullOrEmpty(json) && sun != null)
            {
                var s = JsonUtility.FromJson<Snapshot>(json);
                sun.transform.rotation = Quaternion.Euler(s.sunEuler);
                sun.color = s.sunColor;
                sun.intensity = s.sunIntensity;
                sun.shadowStrength = s.sunShadowStrength;
                RenderSettings.ambientSkyColor = s.ambSky;
                RenderSettings.ambientEquatorColor = s.ambEquator;
                RenderSettings.ambientGroundColor = s.ambGround;
                RenderSettings.fog = s.fog;
                if (!string.IsNullOrEmpty(s.skyPath))
                {
                    var sky = AssetDatabase.LoadAssetAtPath<Material>(s.skyPath);
                    if (sky != null) RenderSettings.skybox = sky;
                }
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            return string.IsNullOrEmpty(json) ? "Objekt entfernt (kein gemerkter Ausgangsstand)." : "Ausgangsstand wiederhergestellt.";
        }

        // ================================================================== Assets

        private static Material SkyMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
            Shader sh = Shader.Find("Skybox/Procedural");
            if (mat == null)
            {
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, SkyPath);
            }
            mat.shader = sh;
            mat.SetFloat("_SunDisk", 2f);
            mat.SetFloat("_SunSize", 0.025f);
            mat.SetFloat("_SunSizeConvergence", 7f);
            mat.SetFloat("_AtmosphereThickness", 1.15f);
            mat.SetColor("_SkyTint", new Color(0.46f, 0.50f, 0.58f));
            mat.SetColor("_GroundColor", new Color(0.30f, 0.30f, 0.30f));
            mat.SetFloat("_Exposure", 1.0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material FogMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(FogMaterialPath);
            if (mat != null) return mat;
            Shader sh = Shader.Find("Hidden/Ashveil/AtmosphereFog");
            if (sh == null)
            {
                Debug.LogWarning("[Ashveil] Stil A: Nebel-Shader fehlt (Hidden/Ashveil/AtmosphereFog) – ohne Höhennebel.");
                return null;
            }
            mat = new Material(sh);
            AssetDatabase.CreateAsset(mat, FogMaterialPath);
            return mat;
        }

        private static VolumeProfile Profile()
        {
            AssetDatabase.DeleteAsset(ProfilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);

            var tone = Add<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.ACES);

            var color = Add<ColorAdjustments>(profile);
            color.postExposure.Override(0.7f);
            color.contrast.Override(10f);
            color.saturation.Override(-12f);         // gedämpfter, aber nicht grau (Stil A, nicht B)
            color.colorFilter.Override(new Color(1f, 0.98f, 0.95f));

            var split = Add<SplitToning>(profile);
            split.shadows.Override(new Color(0.42f, 0.48f, 0.58f));     // kühle Schatten
            split.highlights.Override(new Color(0.62f, 0.55f, 0.45f));  // warme Lichter
            split.balance.Override(-10f);

            var bloom = Add<Bloom>(profile);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.6f);
            bloom.tint.Override(new Color(1f, 0.88f, 0.74f));

            var vignette = Add<Vignette>(profile);
            vignette.intensity.Override(0.18f);
            vignette.smoothness.Override(0.5f);
            vignette.color.Override(Color.black);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            T c = profile.Add<T>(true);
            c.name = typeof(T).Name;
            c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }

        private static void Ref(SerializedObject so, string name, Object v)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.objectReferenceValue = v;
            else Debug.LogWarning("[Ashveil] Stil A: Feld fehlt " + name);
        }

        private static void F(SerializedObject so, string name, float v)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.floatValue = v;
            else Debug.LogWarning("[Ashveil] Stil A: Feld fehlt " + name);
        }

        private static void Col(SerializedObject so, string name, Color v)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.colorValue = v;
            else Debug.LogWarning("[Ashveil] Stil A: Feld fehlt " + name);
        }
    }
}
