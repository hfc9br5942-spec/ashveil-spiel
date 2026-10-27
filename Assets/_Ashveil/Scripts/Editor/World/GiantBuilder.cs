using System.Collections.Generic;
using Ashveil.World;
using UnityEditor;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Nachtrag 26.09.2026 – Der versteinerte Beschuetzer (Greybox, reversibel ueber <see cref="KarethLayout"/>).
    ///
    /// Orientierung (verbindlich): Der Riese blickt nach NORDEN (+z); anatomisch LINKS = WESTEN (-x).
    ///   Huefte im Suedkamm (z ~ -275), Ruecken als Bogen nach Norden, Brust ruht auf einem Schuttkegel suedlich des
    ///   Lagers (Halle des Hauptmanns darin), Kopf haengt ~30 m ueber dem Wachtfeuer, rechter Arm (Osten) stuetzt sich
    ///   vor dem Lager auf (Ostweg laeuft unter dem Ellenbogen durch). Linker Arm abgetrennt: Armreste im Westen,
    ///   der Ellenbogenbogen ueberspannt den Salzweg. Schaedeldecke liegt nordoestlich vor dem Lager.
    ///   Meteorit: kam aus Westnordwest, riss linke Schulter und oberen Ruecken auf, schlug im Osten ein (Aschenfeld).
    ///   Im Einschlagtrichter auf dem Ruecken liegt ein kleiner See.
    ///
    /// Modelle: Tools/Blender/ashveil_build_giant.py (Module Ruecken/Brust/Kopf/Arm + LOD1, Schaedeldecke, 3 Armreste).
    /// Alle Koordinaten hier sind Riesen-Koordinaten (= Welt, solange das Layout nicht verschoben/gedreht ist).
    /// </summary>
    public static class GiantBuilder
    {
        public const string RootName = "Der versteinerte Beschuetzer";
        public static readonly string[] BodyModules = { "Back", "Chest", "Head", "ArmR" };
        public static readonly string[] FragmentModels = { "SM_Giant_SkullCap", "SM_Giant_ArmForearm", "SM_Giant_ArmElbow", "SM_Giant_ArmPillar" };

        public const string PoiHall = "poi.kareth.halle_des_hauptmanns";
        public const string PoiSkull = "poi.kareth.schaedeldecke";
        public const string PoiLake = "poi.kareth.kratersee";
        public const string PoiArch = "poi.kareth.bogen_am_salzweg";
        public const string PoiPillar = "poi.kareth.einsamer_pfeiler";
        public const string PoiForearm = "poi.kareth.schraeger_stein";

        public static readonly Vector3 HallSeat = new Vector3(0f, 1.2f, -76f);
        public static readonly Vector3 HallCenter = new Vector3(0f, 1.2f, -68f);
        public static readonly Vector3 LakeCenter = new Vector3(-40f, 0f, -125f);
        public static readonly Vector3 SkullInside = new Vector3(80f, 0f, 92f);
        public static readonly Vector3 ArchCenter = new Vector3(-130f, 0f, -106f);
        public static readonly Vector3 PillarPos = new Vector3(-202f, 0f, -4f);
        public static readonly Vector3 ForearmPos = new Vector3(-161f, 0f, -22f);
        public static readonly Vector3 HeadCenter = new Vector3(0f, 68f, 10f);

        /// <summary>Bodenflaeche des Riesen als Ellipsen (x, z, Halbachse x, Halbachse z) – dort keine Baeume/Felsen.</summary>
        private static readonly Vector4[] Footprint =
        {
            new Vector4(0f, -275f, 112f, 72f), new Vector4(70f, -200f, 52f, 62f), new Vector4(-70f, -205f, 48f, 58f),
            new Vector4(0f, -320f, 115f, 45f), new Vector4(0f, -88f, 66f, 46f), new Vector4(70f, -110f, 38f, 42f),
            new Vector4(116f, 34f, 26f, 30f), new Vector4(-120f, -121f, 10f, 10f), new Vector4(-140f, -91f, 10f, 10f),
            new Vector4(-203f, -6f, 22f, 22f), new Vector4(-155f, -30f, 16f, 16f)
        };

        private static readonly Vector4 SkullFootprint = new Vector4(80f, 92f, 36f, 31f);

        private static KarethLayout _layout;
        private static Matrix4x4 _m = Matrix4x4.identity, _inv = Matrix4x4.identity;

        public static bool Active { get; private set; }
        public static KarethLayout Layout => _layout;

        /// <summary>Zu Beginn jedes Szenenbaus: Layout lesen. Ohne Modelle bleibt der Riese aus (alte Szene).</summary>
        public static void Prepare()
        {
            _layout = KarethLayout.LoadOrCreate();
            _m = Matrix4x4.TRS(_layout.giantOffset, Quaternion.Euler(0f, _layout.giantYaw, 0f), Vector3.one);
            _inv = _m.inverse;
            Active = _layout.giantEnabled && ArtModels.Has("SM_Giant_Chest");
            if (_layout.giantEnabled && !Active)
                Debug.LogWarning("[Ashveil] Riese eingeschaltet, aber Modelle fehlen (Ashveil > Grafik > Riesen erzeugen).");
        }

        public static Vector3 World(Vector3 local) => _m.MultiplyPoint3x4(local);

        /// <summary>Liegt der Punkt auf der Bodenflaeche des Riesen (plus Rand)?</summary>
        public static bool Blocks(float x, float z, float margin = 0f, bool includeSkull = true)
        {
            if (!Active) return false;
            Vector3 p = _inv.MultiplyPoint3x4(new Vector3(x, 0f, z));
            foreach (Vector4 e in Footprint)
                if (InEllipse(p, e, margin)) return true;
            return includeSkull && InEllipse(p, SkullFootprint, margin);
        }

        private static bool InEllipse(Vector3 p, Vector4 e, float margin)
        {
            float dx = (p.x - e.x) / (e.z + margin), dz = (p.z - e.y) / (e.w + margin);
            return dx * dx + dz * dz < 1f;
        }

        /// <summary>Zusaetzliche Wege (werden ins Terrain gemalt): Lager -> Halle, Moorweg -> Schaedeldecke.</summary>
        public static IEnumerable<Vector2[]> ExtraPaths()
        {
            if (!Active) yield break;
            yield return new[] { P2(0f, -20f), P2(0f, -40f) };
            yield return new[] { P2(22f, 88f), P2(38f, 93f), P2(52f, 95f) };
        }

        private static Vector2 P2(float x, float z)
        {
            Vector3 w = World(new Vector3(x, 0f, z));
            return new Vector2(w.x, w.z);
        }

        /// <summary>Fehlen die Modelle, Blender einmal laufen lassen.</summary>
        public static void EnsureModels()
        {
            KarethLayout layout = KarethLayout.LoadOrCreate();
            if (!layout.giantEnabled || ArtModels.Has("SM_Giant_Chest") || ArtPipeline.FindBlender() == null) return;
            EditorUtility.DisplayProgressBar("Ashveil", "Blender erzeugt den Riesen (einmalig, ~1-3 min) ...", 0.09f);
            if (!ArtPipeline.BuildGiantWithBlender(out string summary)) Debug.LogWarning("[Ashveil] " + summary);
        }

        // ================================================================== Aufbau

        public static Transform Build(Transform env)
        {
            if (!Active) return null;
            Transform root = K.Group(RootName, env, _layout.giantOffset, new Vector3(0f, _layout.giantYaw, 0f));

            // Koerpermodule mit Detailstufen; begehbare Teile (Brust mit Halle, Arm) kollidieren mit dem vollen Modell,
            // hohe, unerreichbare Teile (Ruecken, Kopf) mit der vereinfachten Fassung.
            foreach (string key in BodyModules)
            {
                string name = "SM_Giant_" + key;
                if (!ArtModels.Has(name)) continue;
                bool walkable = key == "Chest" || key == "ArmR";
                Transform module = K.Group("Riese " + key, root);
                GameObject lod0 = ArtModels.Place(name, module, Vector3.zero, 0f, 1f,
                    walkable ? ArtModels.ColliderKind.Mesh : ArtModels.ColliderKind.None);
                GameObject lod1 = ArtModels.Has(name + "_LOD1")
                    ? ArtModels.Place(name + "_LOD1", module, Vector3.zero, 0f, 1f, walkable ? ArtModels.ColliderKind.None : ArtModels.ColliderKind.Mesh)
                    : null;
                if (lod1 == null && !walkable && lod0 != null) AddMeshCollider(lod0);
                SetupLods(module.gameObject, lod0, lod1);
            }

            BuildFragments(root);
            BuildHall(root);
            Physics.SyncTransforms();
            if (_layout.lakeEnabled) BuildLake(root);
            K.Label(root, new Vector3(-150f, 28f, -60f), "Der versteinerte Beschuetzer", "", WorldLabelStyle.Place, 400f, 1.3f);
            Debug.Log("[Ashveil] Riese aufgebaut (Greybox): Koerper, Halle, Schaedeldecke, Armreste" + (_layout.lakeEnabled ? ", Kratersee." : "."));
            return root;
        }

        private static void AddMeshCollider(GameObject go)
        {
            var mf = go.GetComponentInChildren<MeshFilter>();
            if (mf != null) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        }

        private static void SetupLods(GameObject host, GameObject lod0, GameObject lod1)
        {
            if (lod0 == null) return;
            var group = host.AddComponent<LODGroup>();
            var lods = new List<LOD> { new LOD(lod1 != null ? 0.1f : 0f, lod0.GetComponentsInChildren<Renderer>()) };
            if (lod1 != null) lods.Add(new LOD(0f, lod1.GetComponentsInChildren<Renderer>()));
            group.SetLODs(lods.ToArray());
            group.RecalculateBounds();
        }

        // ------------------------------------------------------------------ Schaedeldecke und Armreste

        private static void BuildFragments(Transform root)
        {
            Transform frag = K.Group("Armreste und Schaedeldecke", root);
            foreach (string name in FragmentModels)
                ArtModels.Place(name, frag, Vector3.zero, 0f, 1f, ArtModels.ColliderKind.Mesh);

            Poi(frag, PoiSkull, "Die Schaedeldecke", SkullInside + new Vector3(0f, 2f, 0f), 34f,
                "Entdeckung und kleine Geschichte (z. B. Fundstuecke der ersten Siedler, Blick in die hohle Innenseite). Noch keine Pflichtquest.");
            K.Label(frag, SkullInside + new Vector3(-24f, 20f, 2f), "Die Schaedeldecke", "", WorldLabelStyle.Place, 160f);
            Poi(frag, PoiArch, "Bogen am Salzweg", ArchCenter + new Vector3(0f, 2f, 0f), 16f,
                "Ellenbogenrest ueber dem Weg zu den Salzgruben – Durchgang, spaeter Aussichts- oder Kletterziel.");
            K.Label(frag, ArchCenter + new Vector3(0f, 31f, 0f), "Bogen am Salzweg", "", WorldLabelStyle.Place, 140f);
            Poi(frag, PoiPillar, "Einsamer Pfeiler", PillarPos + new Vector3(0f, 2f, 0f), 20f, "Aufragender Oberarmrest – Landmarke im Westen.");
            Poi(frag, PoiForearm, "Schraeger Stein", ForearmPos + new Vector3(0f, 2f, 0f), 16f, "Schraeg stehender Unterarmrest.");
        }

        private static void Poi(Transform parent, string id, string name, Vector3 pos, float radius, string use)
        {
            var go = new GameObject("POI " + name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<WorldPoi>().Configure(id, name, radius, use);
        }

        // ------------------------------------------------------------------ Halle des Hauptmanns (in der Brust)

        private static void BuildHall(Transform root)
        {
            Material stone = K.Mat("Stone_Light", new Color(0.53f, 0.52f, 0.49f));
            Material stoneDark = K.Mat("Stone_Dark", new Color(0.26f, 0.26f, 0.29f));
            Material wood = K.Mat("Wood", new Color(0.36f, 0.25f, 0.16f));
            Material woodDark = K.Mat("Wood_Dark", new Color(0.2f, 0.14f, 0.1f));
            Material cloth = K.Mat("Cloth_Red", new Color(0.5f, 0.2f, 0.16f));
            Material fire = K.Mat("Glow_Fire", new Color(1f, 0.5f, 0.15f), 0f, new Color(4f, 1.6f, 0.4f));

            Transform hall = K.Group("Halle des Hauptmanns", root);
            // Boden (leicht erhoeht) und breite Rampe vom Suedtor
            K.Box("Hallenboden", hall, new Vector3(0f, 0.6f, -69f), new Vector3(23.6f, 1.2f, 25.6f), stone);
            K.Box("Eingangsboden", hall, new Vector3(0f, 0.6f, -48f), new Vector3(12.6f, 1.2f, 18f), stone);
            K.Ramp("Rampe", hall, new Vector3(0f, 0f, -30.8f), 180f, 8.5f, 8.2f, 12f, stone);

            // Holzstuetzen: Eingang und zwei Joche in der Halle
            float[] yokeZ = { -41.5f, -58f, -70f };
            float[] yokeHalf = { 5.6f, 10.6f, 10.6f };
            float[] yokeTop = { 8.6f, 11.8f, 11.8f };
            for (int i = 0; i < yokeZ.Length; i++)
            {
                for (int s = -1; s <= 1; s += 2)
                    K.Block("Stuetze", hall, new Vector3(s * yokeHalf[i], 1.2f, yokeZ[i]), new Vector3(0.5f, yokeTop[i] - 1.2f, 0.5f), woodDark);
                K.Box("Balken", hall, new Vector3(0f, yokeTop[i] - 0.3f, yokeZ[i]), new Vector3(yokeHalf[i] * 2f + 0.8f, 0.6f, 0.6f), woodDark);
            }

            // Geruest am Schuttkegel neben dem Eingang (hier wird weiter gemeisselt)
            Transform scaffold = K.Group("Geruest", hall, new Vector3(11f, 0f, -44.5f), Vector3.zero);
            for (int ix = 0; ix < 3; ix++)
            for (int iz = 0; iz < 2; iz++)
                K.Block("Stange", scaffold, new Vector3(-3f + ix * 3f, 0f, iz * 1.6f), new Vector3(0.18f, 9f, 0.18f), wood);
            for (int lvl = 1; lvl <= 3; lvl++)
                K.Box("Bretter", scaffold, new Vector3(0f, lvl * 2.8f, 0.8f), new Vector3(6.6f, 0.12f, 1.8f), wood);

            // Geordnet gelagerte Steinbloecke (Aushub)
            Transform blocks = K.Group("Steinbloecke", hall, new Vector3(-12f, 0f, -38f), new Vector3(0f, 12f, 0f));
            for (int i = 0; i < 6; i++)
                K.Block("Block", blocks, new Vector3((i % 3) * 1.3f, (i / 3) * 1.0f, 0f), new Vector3(1.2f, 1f, 1.2f), stone, (i * 7) % 5);

            // Sitz des Hauptmanns: Podest, Stuhl, Tisch, Banner
            K.Block("Podest", hall, new Vector3(0f, 1.2f, -79.5f), new Vector3(8f, 0.5f, 4f), stoneDark);
            K.Block("Sitz", hall, new Vector3(0f, 1.7f, -80.2f), new Vector3(1.6f, 0.6f, 1.2f), woodDark);
            K.Block("Lehne", hall, new Vector3(0f, 1.7f, -80.9f), new Vector3(1.6f, 2.2f, 0.25f), woodDark);
            K.Block("Tisch", hall, new Vector3(0f, 1.2f, -72.5f), new Vector3(4.5f, 0.9f, 1.6f), wood);
            K.Box("Banner", hall, new Vector3(0f, 6.2f, -82.9f), new Vector3(3f, 5f, 0.08f), cloth, default, false);

            // Warmes Licht: Feuerschalen am Eingang, Lampen in der Halle
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 bowl = new Vector3(s * 4.6f, 1.2f, -38.5f);
                K.Block("Feuerschale", hall, bowl, new Vector3(0.8f, 1.1f, 0.8f), stoneDark);
                K.Box("Glut", hall, bowl + new Vector3(0f, 1.15f, 0f), new Vector3(0.6f, 0.1f, 0.6f), fire, default, false);
                K.PointLight("Feuerschale Licht", hall, bowl + new Vector3(0f, 2f, 0f), new Color(1f, 0.55f, 0.22f), 11f, 2.6f, true);
                K.Particles("Flammen", hall, bowl + new Vector3(0f, 1.2f, 0f), new Color(1f, 0.55f, 0.2f, 0.85f), 0.45f, 14f, 0.9f, 0.8f, 10f, 0.2f);
            }
            K.PointLight("Hallenlicht", hall, new Vector3(0f, 8.5f, -66f), new Color(1f, 0.62f, 0.3f), 22f, 3.2f, true, true);
            K.PointLight("Sitzlicht", hall, new Vector3(0f, 5f, -77f), new Color(1f, 0.58f, 0.28f), 12f, 2.4f, true);
            K.PointLight("Nischenlicht", hall, new Vector3(-12f, 3.4f, -64f), new Color(1f, 0.6f, 0.3f), 5f, 1.4f, true);

            Poi(hall, PoiHall, "Halle des Hauptmanns", HallCenter, 14f, "Sitz von Hauptmann Oskar Bralt (Auftraege). Spaeter: Lagerrat, Geschichte der Siedler.");
            K.Label(hall, new Vector3(0f, 10.8f, -45f), "Halle des Hauptmanns", "In der Brust des Beschuetzers", WorldLabelStyle.Place, 60f);
        }

        // ------------------------------------------------------------------ Kratersee

        private static void BuildLake(Transform root)
        {
            Vector3 c = root.TransformPoint(LakeCenter);
            if (!Physics.Raycast(new Vector3(c.x, 600f, c.z), Vector3.down, out RaycastHit floorHit, 800f) || !IsGiant(floorHit.collider))
            {
                Debug.LogWarning("[Ashveil] Kratersee: kein Kraterboden gefunden – See ausgelassen.");
                return;
            }
            float floor = floorHit.point.y;

            // Randhoehe: tiefste Stelle auf dem Ring um den Trichter
            float rim = float.MaxValue;
            const int ringSamples = 32;
            for (int i = 0; i < ringSamples; i++)
            {
                float a = i * Mathf.PI * 2f / ringSamples;
                float best = float.MinValue;
                for (float r = 30f; r <= 64f; r += 2f) // hoechster Punkt entlang des Strahls = Kammlinie
                {
                    Vector3 p = new Vector3(c.x + Mathf.Cos(a) * r, 600f, c.z + Mathf.Sin(a) * r);
                    if (Physics.Raycast(p, Vector3.down, out RaycastHit h, 800f) && IsGiant(h.collider)) best = Mathf.Max(best, h.point.y);
                }
                rim = Mathf.Min(rim, best);
            }
            float level = Mathf.Min(rim - 2f, floor + 14f) + _layout.lakeLevelOffset;
            if (level < floor + 0.8f)
            {
                Debug.LogWarning($"[Ashveil] Kratersee: Rand zu niedrig (Boden {floor:0.0} m, Rand {rim:0.0} m) – See ausgelassen.");
                return;
            }

            // Uferlinie: je Richtung der Abstand, an dem der Trichter die Wasserhoehe erreicht
            const int dirs = 48;
            var verts = new List<Vector3> { new Vector3(c.x, level, c.z) };
            float sum = 0f;
            for (int i = 0; i < dirs; i++)
            {
                float a = i * Mathf.PI * 2f / dirs;
                float shore = 2f;
                for (float r = 1f; r <= 64f; r += 0.75f)
                {
                    Vector3 p = new Vector3(c.x + Mathf.Cos(a) * r, 600f, c.z + Mathf.Sin(a) * r);
                    if (!Physics.Raycast(p, Vector3.down, out RaycastHit h, 800f) || !IsGiant(h.collider) || h.point.y >= level)
                    {
                        shore = r + 0.6f; // Wasserflaeche endet knapp im Ufer
                        break;
                    }
                    shore = r;
                }
                sum += shore;
                verts.Add(new Vector3(c.x + Mathf.Cos(a) * shore, level, c.z + Mathf.Sin(a) * shore));
            }
            var tris = new List<int>();
            for (int i = 0; i < dirs; i++)
            {
                tris.Add(0);
                tris.Add(1 + (i + 1) % dirs);
                tris.Add(1 + i);
            }
            var mesh = new Mesh { name = "Kratersee" };
            Vector3 origin = new Vector3(c.x, level, c.z);
            for (int i = 0; i < verts.Count; i++) verts[i] -= origin;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var lake = new GameObject("Kratersee");
            lake.transform.SetParent(root, true);
            lake.transform.position = origin;
            lake.AddComponent<MeshFilter>().sharedMesh = mesh;
            lake.AddComponent<MeshRenderer>().sharedMaterial = K.Mat("Water_Lake", new Color(0.17f, 0.27f, 0.29f), 0.9f); // klarer als das Moor
            GameObjectUtility.SetStaticEditorFlags(lake, StaticEditorFlags.BatchingStatic);
            var poi = new GameObject("POI Kratersee");
            poi.transform.SetParent(lake.transform, false);
            poi.AddComponent<WorldPoi>().Configure(PoiLake, "Kratersee", sum / dirs, "Idyllischer See im Einschlagtrichter auf dem Ruecken. Spaeter: Aufstieg, Rastplatz, Angelplatz.");
            K.Label(lake.transform, new Vector3(0f, 6f, 0f), "Kratersee", "", WorldLabelStyle.Place, 250f);
            Debug.Log($"[Ashveil] Kratersee: Boden {floor:0.0} m, Rand {rim:0.0} m, Wasser {level:0.0} m, mittlerer Radius {sum / dirs:0} m.");
        }

        private static bool IsGiant(Collider c)
        {
            if (c == null) return false;
            var slot = c.GetComponentInParent<ArtSlot>();
            return slot != null ? slot.SlotName.StartsWith("SM_Giant_") : c.name.StartsWith("SM_Giant_");
        }
    }
}
