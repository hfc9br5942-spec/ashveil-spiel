using System;
using System.Collections.Generic;
using System.IO;
using Ashveil.Data;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Sichtbare Ausruestung (Erweiterung 3.4): liest <c>Data/Appearances/appearances.json</c> (Quelle aller
    /// Prototyp-Modelle: Schalen + Grundformen je Gegenstand), legt daraus AppearanceDefinition-Assets an, haengt sie an
    /// die Items und baut den Aussehen-Katalog (Koerper, Hauttoene, Frisuren, Bart, Haar- und Faerbefarben).
    /// Aenderungen an der JSON-Datei werden beim naechsten Szenenaufbau uebernommen.
    /// </summary>
    public static class AppearanceContentBuilder
    {
        public const string Folder = ProjectSetup.Root + "/Data/Appearances";
        public const string JsonPath = Folder + "/appearances.json";
        public const string CatalogPath = Folder + "/AppearanceCatalog.asset";
        private const string TemplatePath = Folder + "/M_EquipmentLit.mat";
        private const string HashKey = "Ashveil.Appearances.JsonHash";

        // ---- JSON-Format (JsonUtility)
        [Serializable] private class JFile { public List<JEntry> entries = new List<JEntry>(); }
        [Serializable] private class JEntry
        {
            public string key, slot, primary, secondary, detail, sheath, bodies = "All";
            public List<string> hides = new List<string>();
            public List<JShell> shells = new List<JShell>();
            public List<JPart> parts = new List<JPart>();
            public List<JModel> modelle = new List<JModel>();   // 21.10.: echte Kleidungsmodelle je Koerper (Tripo)
        }
        /// <summary>21.10.2026: Kleidungsmodell fuer genau einen Koerper. model = Pfad relativ zu Assets/_Ashveil;
        /// hide = Hautbereiche, die darunter ausgeblendet werden (Werte aus KLD_*_verdeckung.json).</summary>
        [Serializable] private class JModel
        {
            public string body, model, surface, channel;
            public string quelle;   // nur Doku: woher die hide-Baender stammen (Messung/Schaetzung)
            public List<JShell> hide = new List<JShell>();
        }
        [Serializable] private class JShell
        {
            public List<string> regions = new List<string>();
            public float yMin, yMax = 1f, offset, flare, flareStart = 1f;
            public string surface, channel;
        }
        [Serializable] private class JPart
        {
            public string shape, bone, surface, channel;
            /// <summary>22.10.: echtes Anbauteil (Schulter, Helm, Schnalle) statt Grundform – Pfad unter Assets/_Ashveil/, z. B. Art/Models/Ruestung/ANB_Helm_Topf.fbx</summary>
            public string model;
            public float[] offset, euler, size;
            public bool mirror, detail, capped = true;
            public float taperX = 1f, taperZ = 1f, arc = 360f, facing, thickness, cut = 0.5f;
            public int segments = 16;
        }

        /// <summary>M7: nur den Aussehen-Katalog (Charakter-Editor) neu aufbauen – ohne Szenenaufbau.</summary>
        [MenuItem("Ashveil/Daten/Aussehen-Katalog aktualisieren (Charakter-Editor)", priority = 202)]
        public static void BuildFromMenu()
        {
            AppearanceCatalog cat = Build();
            Debug.Log($"[Ashveil] Aussehen-Katalog: {cat.faces.Count} Gesichter, {cat.skinTones.Count} Hauttöne, {cat.eyeColors.Count} Augenfarben, " +
                      $"{cat.features.Count} Merkmale, {cat.hairstyles.Count} Frisuren, {cat.beards.Count} Bärte.");
        }

        public static AppearanceCatalog Build()
        {
            ProjectSetup.EnsureFolder(Folder);
            var defs = new Dictionary<string, AppearanceDefinition>();
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
            if (json == null)
            {
                Debug.LogWarning("[Ashveil] " + JsonPath + " fehlt – keine sichtbare Ausruestung.");
            }
            else
            {
                string hash = Hash128.Compute(json.text).ToString();
                bool rewrite = EditorPrefs.GetString(HashKey, "") != hash;
                JFile file = JsonUtility.FromJson<JFile>(json.text);
                foreach (JEntry e in file.entries)
                {
                    string path = Folder + "/Appearance_" + e.key + ".asset";
                    var d = AssetDatabase.LoadAssetAtPath<AppearanceDefinition>(path);
                    bool fresh = d == null;
                    if (fresh)
                    {
                        d = ScriptableObject.CreateInstance<AppearanceDefinition>();
                        AssetDatabase.CreateAsset(d, path);
                    }
                    if (fresh || rewrite)
                    {
                        Fill(d, e);
                        GreyboxKit.SetString(d, "id", "appearance." + e.key);
                        GreyboxKit.SetString(d, "displayName", e.key);
                        EditorUtility.SetDirty(d);
                    }
                    // 21.10.: Kleidungsmodelle bei JEDEM Bau nachladen – das FBX kann nach der JSON-Aenderung importiert worden sein
                    if (ModelleAufloesen(d)) EditorUtility.SetDirty(d);
                    defs[e.key] = d;
                }
                EditorPrefs.SetString(HashKey, hash);
            }

            // An Items haengen (gleicher Schluessel wie item.<key>)
            int linked = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition"))
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (item == null || string.IsNullOrEmpty(item.Id) || !item.Id.StartsWith("item.")) continue;
                string key = item.Id.Substring(5);
                if (defs.TryGetValue(key, out AppearanceDefinition d) && item.appearance != d)
                {
                    item.appearance = d;
                    EditorUtility.SetDirty(item);
                }
                if (item.appearance != null) linked++;
            }

            AppearanceCatalog cat = BuildCatalog(defs);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Ashveil] Sichtbare Ausruestung: {defs.Count} Aussehen, {linked} Items verknuepft, " +
                      $"{cat.hairstyles.Count - 1} Frisuren, {cat.beards.Count - 1} Bart, {cat.dyes.Count} Faerbefarben.");
            return cat;
        }

        private static void Fill(AppearanceDefinition d, JEntry e)
        {
            d.slot = Enum.TryParse(e.slot, out EquipType t) ? t : EquipType.Chest;
            d.primary = Col(e.primary);
            d.secondary = Col(e.secondary);
            d.detail = Col(e.detail);
            d.sheath = Enum.TryParse(e.sheath, out WeaponSheath sh) ? sh : WeaponSheath.None;
            d.hides = AppearanceHides.None;
            foreach (string h in e.hides) if (Enum.TryParse(h, out AppearanceHides x)) d.hides |= x;
            // Art-Pass 4: Varianten je Koerper (Maenner-/Frauenschnitt) – fehlt die Angabe, passt es an beide
            d.bodies = Enum.TryParse(e.bodies, out BodyVariantMask bm) && bm != BodyVariantMask.None ? bm : BodyVariantMask.All;
            d.placeholder = true;
            d.detailDistance = 25f;
            d.shells = new List<ShellLayer>();
            foreach (JShell s in e.shells) d.shells.Add(Schale(s));
            // 21.10.: echte Kleidungsmodelle je Koerper (Modell selbst wird in ModelleAufloesen geladen)
            d.modelle = new List<SkinnedModel>();
            if (e.modelle != null)
                foreach (JModel jm in e.modelle)
                {
                    if (jm == null || string.IsNullOrEmpty(jm.body)) continue;
                    var km = new SkinnedModel { body = jm.body.Trim(), modelPath = (jm.model ?? "").Trim(), surface = Surf(jm.surface), channel = Chan(jm.channel) };
                    if (jm.hide != null) foreach (JShell s in jm.hide) km.hide.Add(Schale(s));
                    d.modelle.Add(km);
                }
            d.parts = new List<RigidPart>();
            foreach (JPart p in e.parts)
            {
                d.parts.Add(new RigidPart
                {
                    shape = Shape(p.shape), bone = p.bone, offset = V(p.offset), euler = V(p.euler), size = V(p.size),
                    surface = Surf(p.surface), channel = Chan(p.channel), mirror = p.mirror, detail = p.detail,
                    taperX = p.taperX, taperZ = p.taperZ, arc = p.arc, facing = p.facing, thickness = p.thickness,
                    cut = Mathf.Clamp(p.cut, 0.05f, 1f), segments = Mathf.Clamp(p.segments, 3, 48), capped = p.capped,
                    prefab = string.IsNullOrWhiteSpace(p.model) ? null : AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Ashveil/" + p.model.Trim())
                });
                if (!string.IsNullOrWhiteSpace(p.model) && d.parts[d.parts.Count - 1].prefab == null) Debug.LogWarning("[Ashveil] Anbauteil fehlt: " + p.model + " (" + e.key + ")");
            }
        }

        private static ShellLayer Schale(JShell s)
        {
            var l = new ShellLayer
            {
                yMin = s.yMin, yMax = s.yMax, offset = s.offset, flare = s.flare, flareStart = s.flareStart,
                surface = Surf(s.surface), channel = Chan(s.channel), hideBody = true
            };
            if (s.regions != null) foreach (string r in s.regions) if (Enum.TryParse(r, out BodyRegion br)) l.regions |= br;
            return l;
        }

        /// <summary>21.10.2026: Modellverweise der Kleidungsmodelle aus modelPath (relativ zu Assets/_Ashveil) laden. Fehlt das FBX
        /// (noch nicht importiert), bleibt model null – das Spiel zeigt dann still die Schalen. true = etwas geaendert.</summary>
        public static bool ModelleAufloesen(AppearanceDefinition d)
        {
            if (d == null || d.modelle == null) return false;
            bool changed = false;
            foreach (SkinnedModel km in d.modelle)
            {
                if (km == null || string.IsNullOrEmpty(km.modelPath)) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectSetup.Root + "/" + km.modelPath.TrimStart('/'));
                if (go == km.model) continue;
                if (go == null && km.model != null) Debug.LogWarning($"[Ashveil] Aussehen {d.name}: Kleidungsmodell {km.modelPath} fehlt – Schalen statt Modell.");
                km.model = go;
                changed = true;
            }
            return changed;
        }

        /// <summary>21.10.2026 (Tests, nur lesen): einen Eintrag aus appearances.json in ein neues, NICHT gespeichertes Aussehen
        /// uebersetzen (wie der Katalogbau, inklusive Modellverweise). null = Schluessel fehlt.</summary>
        public static AppearanceDefinition LeseEintrag(string key)
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
            if (json == null) return null;
            JFile file = JsonUtility.FromJson<JFile>(json.text);
            foreach (JEntry e in file.entries)
            {
                if (e.key != key) continue;
                var d = ScriptableObject.CreateInstance<AppearanceDefinition>();
                d.hideFlags = HideFlags.DontSave;
                d.name = "Appearance_" + key;
                Fill(d, e);
                GreyboxKit.SetString(d, "id", "appearance." + key);
                ModelleAufloesen(d);
                return d;
            }
            return null;
        }

        private static Vector3 V(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
        private static Color Col(string hex) => ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.gray;
        private static SurfaceKind Surf(string s) => Enum.TryParse(s, out SurfaceKind k) ? k : SurfaceKind.Cloth;
        private static DyeChannel Chan(string s) => s == "secondary" ? DyeChannel.Secondary : s == "detail" ? DyeChannel.Detail : DyeChannel.Primary;

        private static ProcShape Shape(string s)
        {
            switch (s)
            {
                case "cyl": return ProcShape.Cylinder;
                case "dome": return ProcShape.Dome;
                case "sphere": return ProcShape.Sphere;
                case "torus": return ProcShape.Torus;
                default: return ProcShape.Box;
            }
        }

        // ================================================================== Katalog

        private static AppearanceCatalog BuildCatalog(Dictionary<string, AppearanceDefinition> defs)
        {
            var cat = AssetDatabase.LoadAssetAtPath<AppearanceCatalog>(CatalogPath);
            if (cat == null)
            {
                cat = ScriptableObject.CreateInstance<AppearanceCatalog>();
                AssetDatabase.CreateAsset(cat, CatalogPath);
            }
            string bc = QuaterniusImport.BaseCharactersFolder + "/";
            string hs = QuaterniusImport.HairstylesFolder + "/";
            cat.bodies = new List<AppearanceCatalog.BodyEntry>
            {
                Body("Männlich", BodyVariantMask.Male, CharacterBodies.MalePath,
                    ("Hell", bc + "T_Superhero_Male_Ligh.png"), ("Dunkel", bc + "T_Superhero_Male_Dark.png")),
                Body("Weiblich", BodyVariantMask.Female, CharacterBodies.FemalePath,
                    ("Hell", bc + "T_Superhero_Female_Light_BaseColor.png"), ("Dunkel", bc + "T_Superhero_Female_Dark_BaseColor.png"))
            };
            // Nur vorhandene Modelle: Frisuren sind je Koerperbau geriggt
            cat.hairstyles = new List<AppearanceCatalog.StyleEntry> { Style("Kahl", null, BodyVariantMask.All) };
            AddStyle(cat.hairstyles, "Kurz geschoren", hs + "Hair_Buzzed.fbx", BodyVariantMask.Male);
            AddStyle(cat.hairstyles, "Gescheitelt", hs + "Hair_SimpleParted.fbx", BodyVariantMask.Male);
            AddStyle(cat.hairstyles, "Kurz", hs + "Hair_BuzzedFemale.fbx", BodyVariantMask.Female);
            AddStyle(cat.hairstyles, "Dutt", hs + "Hair_Buns.fbx", BodyVariantMask.Female);
            AddStyle(cat.hairstyles, "Lang", hs + "Hair_Long.fbx", BodyVariantMask.Female);
            cat.beards = new List<AppearanceCatalog.StyleEntry> { Style("Ohne", null, BodyVariantMask.All) };
            AddStyle(cat.beards, "Vollbart", hs + "Hair_Beard.fbx", BodyVariantMask.Male);
            AddCreatorContent(cat, hs, bc);

            // Haartexturen sind neutral grau (~0,56) – Farben daher hell angesetzt
            cat.hairColors = new List<AppearanceCatalog.NamedColor>
            {
                Named("Schwarz", "#2a2622"), Named("Dunkelbraun", "#6b4c36"), Named("Kastanie", "#b56a42"),
                Named("Aschblond", "#f0e0b8"), Named("Kupferrot", "#e0703a"), Named("Grau", "#d0cfca"), Named("Weiß", "#ffffff")
            };
            cat.dyes = new List<AppearanceCatalog.NamedColor>
            {
                Named("Knochenweiß", "#d8d0bc"), Named("Aschgrau", "#77736c"), Named("Kohlschwarz", "#2a2826"),
                Named("Rostrot", "#8a3a22"), Named("Blutrot", "#6e1a18"), Named("Tannengrün", "#2f4a34"),
                Named("Moorgrün", "#5a6238"), Named("Nachtblau", "#243048"), Named("Stahlblau", "#4f6a80"),
                Named("Ocker", "#b08a3a"), Named("Umbra", "#5a4030"), Named("Violett", "#5a3a6e")
            };

            var template = AssetDatabase.LoadAssetAtPath<Material>(TemplatePath);
            if (template == null)
            {
                Shader lit = Shader.Find("Universal Render Pipeline/Lit");
                template = new Material(lit != null ? lit : Shader.Find("Standard")) { name = "M_EquipmentLit" };
                AssetDatabase.CreateAsset(template, TemplatePath);
            }
            cat.litTemplate = template;
            cat.previewController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerAnimatorBuilder.ControllerPath);
            cat.overlayTemplate = OverlayTemplate();

            AnpassenFuerMpfb(cat);
            AnpassenFuerTripo(cat);

            cat.fallbacks = new List<AppearanceDefinition>();
            foreach (KeyValuePair<string, AppearanceDefinition> kv in defs)
                if (kv.Key.StartsWith("fallback_")) cat.fallbacks.Add(kv.Value);
            EditorUtility.SetDirty(cat);
            return cat;
        }

        // ================================================================== M7 Charakter-Editor

        public const string GeneratedFolder = ProjectSetup.Root + "/Art/Characters/Generated";
        private const string OverlayTemplatePath = Folder + "/M_FaceOverlay.mat";

        /// <summary>
        /// Neue Auswahl fuer den Charakter-Editor. Reihenfolge der bestehenden Listen bleibt (alte Speicherstaende),
        /// Neues wird hinten angehaengt. Gesichts-/Augen-Texturen erzeugt Tools/Characters/make_character_textures.py.
        /// </summary>
        private static void AddCreatorContent(AppearanceCatalog cat, string hs, string bc)
        {
            ImportGenerated();
            string[] bodyKeys = { "male", "female" };

            // Hauttoene: Index 0/1 = die bisherigen Texturen (alte Speicherstaende), dann eingefaerbte Varianten
            cat.skinTones = new List<AppearanceCatalog.SkinTone>
            {
                Tone("Hell", 0, "#ffffff", "#d9a07a"), Tone("Dunkel", 1, "#ffffff", "#7a4a30"),
                Tone("Blass", 0, "#ffeeea", "#efc6a8"), Tone("Aschfahl", 0, "#c9d8ec", "#c3a595"),
                Tone("Sonnengebräunt", 0, "#e6c7a8", "#c08256"), Tone("Olivbraun", 0, "#d6cc9e", "#b08958"),
                Tone("Tiefbraun", 1, "#b8ada8", "#563222")
            };
            // Blass hellt auf: Farbwerte ueber 1 (Multiplikator)
            cat.skinTones[2].tint = new Color(1.2f, 1.13f, 1.1f);

            cat.faces = new List<AppearanceCatalog.FaceEntry>
            {
                Face("Oval", null, new Vector3(1f, 1f, 1f), bodyKeys),
                Face("Kantig", "kantig", new Vector3(1.04f, 0.99f, 1.01f), bodyKeys),
                Face("Weich", "weich", new Vector3(1.03f, 0.98f, 1.02f), bodyKeys),
                Face("Hager", "hager", new Vector3(0.95f, 1.03f, 0.98f), bodyKeys),
                Face("Wettergegerbt", "wettergegerbt", new Vector3(1.01f, 1f, 1f), bodyKeys),
                Face("Alt", "alt", new Vector3(0.99f, 0.99f, 1f), bodyKeys)
            };

            cat.eyeColors = new List<AppearanceCatalog.EyeEntry>
            {
                Eye("Braun", bc + "T_Eye_Brown.png", "#6a3e1a"), Eye("Dunkelbraun", Gen("T_Eye_dunkelbraun"), "#3a2210"),
                Eye("Haselnuss", Gen("T_Eye_haselnuss"), "#7a5c28"), Eye("Grün", Gen("T_Eye_gruen"), "#467a3a"),
                Eye("Blau", Gen("T_Eye_blau"), "#3c6caa"), Eye("Grau", Gen("T_Eye_grau"), "#7a8690"),
                Eye("Bernstein", Gen("T_Eye_bernstein"), "#c48a22"), Eye("Glut", Gen("T_Eye_glut"), "#e45416")
            };

            cat.features = new List<AppearanceCatalog.OverlayEntry> { new AppearanceCatalog.OverlayEntry { name = "Keins" } };
            foreach ((string name, string key) in new[] { ("Narbe über dem Auge", "narbe_auge"), ("Wangennarbe", "narbe_wange"),
                         ("Sommersprossen", "sommersprossen"), ("Aschebemalung", "aschebemalung"), ("Glutzeichen", "glutzeichen"), ("Tätowierung", "taetowierung") })
                cat.features.Add(new AppearanceCatalog.OverlayEntry { name = name, overlays = PerBody("T_Feature_" + key, bodyKeys) });

            // Frisuren aus Grundformen (Haarfarbe wie die Modelle) – hinten angehaengt
            // Blender-Modelle (Tools/Blender/ashveil_build_hair.py), sonst Grundformen als Ersatz
            var tail = Model("SM_Hair_Ponytail") ?? new List<RigidPart>
            {
                Part(ProcShape.Sphere, new Vector3(0f, 0.1f, -0.135f), Vector3.zero, new Vector3(0.05f, 0.05f, 0.05f)),
                Part(ProcShape.Cylinder, new Vector3(0f, -0.005f, -0.16f), new Vector3(12f, 0f, 0f), new Vector3(0.022f, 0.2f, 0.018f), taper: 2.6f)
            };
            AddStyle(cat.hairstyles, "Pferdeschwanz", hs + "Hair_Buzzed.fbx", BodyVariantMask.Male, tail);
            AddStyle(cat.hairstyles, "Pferdeschwanz", hs + "Hair_BuzzedFemale.fbx", BodyVariantMask.Female, tail);
            cat.hairstyles.Add(new AppearanceCatalog.StyleEntry
            {
                name = "Irokese", bodies = BodyVariantMask.All,
                parts = Model("SM_Hair_Mohawk") ?? new List<RigidPart> { Part(ProcShape.Dome, new Vector3(0f, 0.11f, -0.02f), Vector3.zero, new Vector3(0.04f, 0.27f, 0.25f), cut: 0.5f) }
            });

            // Baerte: Stoppeln (gemalt) und Grundformen
            var stubble = PerBody("T_Beard_stoppeln", bodyKeys);
            cat.beards.Add(new AppearanceCatalog.StyleEntry { name = "Stoppeln", bodies = BodyVariantMask.Male, overlays = stubble });
            cat.beards.Add(new AppearanceCatalog.StyleEntry { name = "Dreitagebart", bodies = BodyVariantMask.Male, overlays = PerBody("T_Beard_dreitagebart", bodyKeys) });
            RigidPart goatee = Model("SM_Beard_Goatee")?[0] ?? Part(ProcShape.Cylinder, new Vector3(0f, 0.004f, 0.094f), new Vector3(-14f, 0f, 0f), new Vector3(0.022f, 0.05f, 0.018f), taper: 2.2f);
            RigidPart moustache = Model("SM_Beard_Moustache")?[0] ?? Part(ProcShape.Cylinder, new Vector3(0f, 0.043f, 0.014f), new Vector3(6f, 0f, 0f), new Vector3(0.19f, 0.012f, 0.2f), arc: 62f, thickness: 0.01f);
            cat.beards.Add(new AppearanceCatalog.StyleEntry { name = "Kinnbart", bodies = BodyVariantMask.Male, overlays = stubble, parts = new List<RigidPart> { goatee } });
            cat.beards.Add(new AppearanceCatalog.StyleEntry { name = "Schnauzer", bodies = BodyVariantMask.Male, parts = new List<RigidPart> { moustache } });
            cat.beards.Add(new AppearanceCatalog.StyleEntry { name = "Kinn- und Schnauzbart", bodies = BodyVariantMask.Male, overlays = stubble, parts = new List<RigidPart> { goatee, moustache } });
        }

        /// <summary>Blender-Modell am Kopfknochen (Ursprung = Knochen, keine Verschiebung). Eigenes Modell in
        /// Art/Models_Eigene geht vor. null = gibt es (noch) nicht.</summary>
        private static List<RigidPart> Model(string name)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectSetup.Root + "/Art/Models_Eigene/" + name + ".fbx")
                     ?? AssetDatabase.LoadAssetAtPath<GameObject>(ArtPipeline.ModelFolder + "/" + name + ".fbx");
            if (go == null) return null;
            return new List<RigidPart>
            {
                new RigidPart { shape = ProcShape.Box, bone = "Head", offset = Vector3.zero, euler = Vector3.zero, size = Vector3.one,
                                surface = SurfaceKind.Fur, channel = DyeChannel.Primary, prefab = go }
            };
        }

        private static string Gen(string file) => GeneratedFolder + "/" + file + ".png";

        private static List<Texture2D> PerBody(string prefix, string[] bodyKeys)
        {
            var list = new List<Texture2D>();
            foreach (string b in bodyKeys) list.Add(AssetDatabase.LoadAssetAtPath<Texture2D>(Gen(prefix + "_" + b)));
            return list;
        }

        private static AppearanceCatalog.SkinTone Tone(string name, int baseTex, string tint, string swatch) =>
            new AppearanceCatalog.SkinTone { name = name, baseTexture = baseTex, tint = Col(tint), swatch = Col(swatch) };

        private static AppearanceCatalog.FaceEntry Face(string name, string key, Vector3 scale, string[] bodyKeys) =>
            new AppearanceCatalog.FaceEntry { name = name, headScale = scale, overlays = key == null ? new List<Texture2D>() : PerBody("T_Face_" + key, bodyKeys) };

        private static AppearanceCatalog.EyeEntry Eye(string name, string path, string swatch) =>
            new AppearanceCatalog.EyeEntry { name = name, texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path), swatch = Col(swatch) };

        private static RigidPart Part(ProcShape shape, Vector3 offset, Vector3 euler, Vector3 size, float taper = 1f, float cut = 0.5f, float arc = 360f, float thickness = 0f) =>
            new RigidPart
            {
                shape = shape, bone = "Head", offset = offset, euler = euler, size = size, surface = SurfaceKind.Fur,
                channel = DyeChannel.Primary, taperX = taper, taperZ = taper, cut = cut, arc = arc, thickness = thickness, segments = 16
            };

        /// <summary>Gesichtsebenen: Rand klemmen (kein Uebersprechen), Alpha behalten; Augen wie die Originaltextur.</summary>
        private static void ImportGenerated()
        {
            if (!AssetDatabase.IsValidFolder(GeneratedFolder)) { Debug.LogWarning("[Ashveil] " + GeneratedFolder + " fehlt – Charakter-Editor ohne Gesichtsebenen."); return; }
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { GeneratedFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                bool overlay = !Path.GetFileName(path).StartsWith("T_Eye_");
                bool dirty = false;
                if (ti.wrapMode != TextureWrapMode.Clamp) { ti.wrapMode = TextureWrapMode.Clamp; dirty = true; }
                if (!ti.alphaIsTransparency && overlay) { ti.alphaIsTransparency = true; dirty = true; }
                if (ti.maxTextureSize != 512) { ti.maxTextureSize = 512; dirty = true; }
                if (ti.mipmapEnabled != true) { ti.mipmapEnabled = true; dirty = true; }
                // Gesichtsebenen scharf (27.09.): anisotrop, hochwertige Kompression, Trilinear
                if (ti.anisoLevel != 8) { ti.anisoLevel = 8; dirty = true; }
                if (ti.textureCompression != TextureImporterCompression.CompressedHQ) { ti.textureCompression = TextureImporterCompression.CompressedHQ; dirty = true; }
                if (ti.filterMode != FilterMode.Trilinear) { ti.filterMode = FilterMode.Trilinear; dirty = true; }
                if (dirty) ti.SaveAndReimport();
            }
        }

        /// <summary>URP Lit, durchsichtig (Alpha), ohne Schatten – als Asset, damit die Shader-Variante im Build bleibt.</summary>
        private static Material OverlayTemplate()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(OverlayTemplatePath);
            if (m == null)
            {
                Shader lit = Shader.Find("Universal Render Pipeline/Lit");
                m = new Material(lit != null ? lit : Shader.Find("Standard")) { name = "M_FaceOverlay" };
                AssetDatabase.CreateAsset(m, OverlayTemplatePath);
            }
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 2f);
            m.SetFloat("_Smoothness", 0.22f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_ReceiveShadows", 1f);
            m.SetColor("_BaseColor", Color.white);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.SetShaderPassEnabled("DepthOnly", false);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>17.10.2026 spätabends: MPFB-Mann (CHR_M_MPFB01, Schalter „Ashveil.MPFBMann“) hat ein anderes Texturlayout
        /// (MakeHuman) als die Quaternius-Figur. Hauttöne aus den MakeHuman-System-Assets (CC0); Quaternius-Überlagerungen
        /// (Gesicht, Merkmale, Bartschatten) und Quaternius-Frisuren/-Bart (anderes Skelett) für den Mann aus. Eigene Haare,
        /// Augen, Brauen, Wimpern bringt das Modell mit. Schalter aus → Katalog unverändert.</summary>
        private static void AnpassenFuerMpfb(AppearanceCatalog cat)
        {
            if (!CharacterBodies.UseMpfbMale || cat.bodies.Count == 0) return;
            string t = CharacterBodies.Folder + "/Textures/";
            cat.bodies[0] = Body("Männlich", BodyVariantMask.Male, CharacterBodies.MpfbMalePath,
                ("Hell", Vorlage(t, "M")), ("Dunkel", t + "T_CHR_M_MPFB01_Haut_Dunkel.png"));
            void OhneMann(List<Texture2D> o) { if (o != null && o.Count > 0) o[0] = null; }
            foreach (var f in cat.faces) OhneMann(f.overlays);
            foreach (var f in cat.features) OhneMann(f.overlays);
            foreach (var b in cat.beards) { OhneMann(b.overlays); if (b.model != null) b.bodies &= ~BodyVariantMask.Male; }
            foreach (var h in cat.hairstyles) if (h.model != null) h.bodies &= ~BodyVariantMask.Male;
            Debug.Log("[Ashveil] Aussehen-Katalog: MPFB-Mann – MakeHuman-Hauttöne, Quaternius-Überlagerungen/Frisuren für den Mann aus");
            // 18.10.: MPFB-Frau (CHR_W_MPFB01) genauso – eigene Haare (short03), Augen, Brauen, Wimpern; Quaternius-Teile für die Frau aus
            if (CharacterBodies.UseMpfbFemale && cat.bodies.Count > 1)
            {
                cat.bodies[1] = Body("Weiblich", BodyVariantMask.Female, CharacterBodies.MpfbFemalePath,
                    ("Hell", Vorlage(t, "W")), ("Dunkel", t + "T_CHR_W_MPFB01_Haut_Dunkel.png"));
                void OhneFrau(List<Texture2D> o) { if (o != null && o.Count > 1) o[1] = null; }
                foreach (var f in cat.faces) OhneFrau(f.overlays);
                foreach (var f in cat.features) OhneFrau(f.overlays);
                foreach (var h in cat.hairstyles) if (h.model != null) h.bodies &= ~BodyVariantMask.Female;
                Debug.Log("[Ashveil] Aussehen-Katalog: MPFB-Frau – MakeHuman-Hauttöne, Quaternius-Überlagerungen/Frisuren für die Frau aus");
            }
        }

        /// <summary>21.10.2026: Tripo-Frau (CHR_W_TR01, Mixamo-Skelett, Schalter „Ashveil.TripoFrau“) – eigene UV, eine Haut aus
        /// Tripo; Quaternius-Überlagerungen und Modell-Frisuren (anderes Skelett/UV) für die Frau aus. Kahl, bis Frisuren folgen.</summary>
        private static void AnpassenFuerTripo(AppearanceCatalog cat)
        {
            // 21.10.: Mann genauso (CHR_M_TR01, Schalter „Ashveil.TripoMann“) – Index 0 = Mann, 1 = Frau
            if (TripoKoerper.UseMale && cat.bodies.Count > 0)
            {
                cat.bodies[0] = Body("Männlich", BodyVariantMask.Male, TripoKoerper.MalePath, ("Hell", TripoKoerper.HautPfad(TripoKoerper.MaleName)));
                void OhneMann(List<Texture2D> o) { if (o != null && o.Count > 0) o[0] = null; }
                foreach (var f in cat.faces) OhneMann(f.overlays);
                foreach (var f in cat.features) OhneMann(f.overlays);
                foreach (var b in cat.beards) { OhneMann(b.overlays); if (b.model != null) b.bodies &= ~BodyVariantMask.Male; }
                foreach (var h in cat.hairstyles) if (h.model != null) h.bodies &= ~BodyVariantMask.Male;
                Debug.Log("[Ashveil] Aussehen-Katalog: Tripo-Mann (Mixamo-Skelett) – eine Haut, Überlagerungen/Modell-Frisuren/-Bärte für den Mann aus");
            }
            if (!TripoKoerper.UseFemale || cat.bodies.Count < 2) return;
            cat.bodies[1] = Body("Weiblich", BodyVariantMask.Female, TripoKoerper.FemalePath, ("Hell", TripoKoerper.HautPfad(TripoKoerper.FemaleName)));
            void OhneFrau(List<Texture2D> o) { if (o != null && o.Count > 1) o[1] = null; }
            foreach (var f in cat.faces) OhneFrau(f.overlays);
            foreach (var f in cat.features) OhneFrau(f.overlays);
            foreach (var h in cat.hairstyles) if (h.model != null) h.bodies &= ~BodyVariantMask.Female;
            Debug.Log("[Ashveil] Aussehen-Katalog: Tripo-Frau (Mixamo-Skelett) – eine Haut, Überlagerungen/Modell-Frisuren für die Frau aus");
        }

        /// <summary>18.10. nachts (ART-122): Haut nach Max' Vorlage, falls vorhanden (aus mpfb_import), sonst die MakeHuman-Haut.
        /// Der Hautton „Hell“ (ungefärbt) zeigt damit genau die Vorlage; die gefärbten Töne bauen auf ihr auf.</summary>
        /// 19.10. abends (DEC-20261019-002): gebackene Higgsfield/Meshy-Haut (T_…_HF_Basis aus mpfb_import) hat Vorrang.
        private static string Vorlage(string ordner, string g)
        {
            string hf = ordner + "T_CHR_" + g + "_MPFB01_HF_Basis.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(hf) != null) return hf;
            string v = ordner + "T_CHR_" + g + "_MPFB01_Vorlage_Haut.png";
            return AssetDatabase.LoadAssetAtPath<Texture2D>(v) != null ? v : ordner + "T_CHR_" + g + "_MPFB01_Haut.png";
        }

        private static AppearanceCatalog.BodyEntry Body(string name, BodyVariantMask v, string model, params (string, string)[] skins)
        {
            var b = new AppearanceCatalog.BodyEntry { name = name, variant = v, model = AssetDatabase.LoadAssetAtPath<GameObject>(model) };
            foreach ((string n, string path) in skins)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) continue; // nur vorhandene Hauttoene anbieten
                b.skinNames.Add(n);
                b.skinTextures.Add(tex);
            }
            return b;
        }

        private static AppearanceCatalog.StyleEntry Style(string name, GameObject model, BodyVariantMask bodies) =>
            new AppearanceCatalog.StyleEntry { name = name, model = model, bodies = bodies };

        private static void AddStyle(List<AppearanceCatalog.StyleEntry> list, string name, string path, BodyVariantMask bodies, List<RigidPart> parts = null)
        {
            var m = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (m == null) return; // fehlt das Modell, gibt es die Option nicht
            AppearanceCatalog.StyleEntry e = Style(name, m, bodies);
            if (parts != null) e.parts = new List<RigidPart>(parts);
            list.Add(e);
        }

        private static AppearanceCatalog.NamedColor Named(string name, string hex) =>
            new AppearanceCatalog.NamedColor { name = name, color = Col(hex) };
    }
}
