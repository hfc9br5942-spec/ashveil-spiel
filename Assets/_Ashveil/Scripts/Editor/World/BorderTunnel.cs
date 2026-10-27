using Ashveil.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using K = Ashveil.EditorTools.GreyboxKit;
using L = Ashveil.EditorTools.BorderMountainsLayout;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Welt-Ebene (Sandbox_M1) fuer das Grenzgebirge: Tunnel durch die Kareth-Ostwand am Ende des Ostwegs (Max 27.09.:
    /// "Tunnel durch die Wand") und die Fernansicht der beiden Gebirgszellen. Das ist die einzige Aenderung an Kareth:
    /// eine schmale Kerbe im Gelaende am Westportal (keine Terrain-Holes – die machten das NavMesh-Backen extrem langsam),
    /// sonst bleibt Kareth unangetastet. Das Kareth-NavMesh reicht bis ans Ende der Roehre (x = 658).
    /// Der Tunnel gehoert zur Welt-Ebene (immer geladen), damit man ihn betreten kann, bevor die Zelle geladen ist.
    /// </summary>
    public static class BorderTunnel
    {
        public const string GroupName = "Grenzgebirge (Welt-Ebene)";
        private static Material _stone, _stoneDark, _wood, _lamp;

        /// <summary>Boden am Westportal (Kareth) und am Ostportal (Grenzgebirge = Talboden T0).</summary>
        public static float WestFloor => KarethTerrainBuilder.Height(L.TunnelWestX, L.TunnelZ);
        public static float EastFloor => L.TalNode(0).y;

        /// <summary>Boden im Tunnel an einer x-Position (linear zwischen den Portalen).</summary>
        public static float FloorAt(float x) => Mathf.Lerp(WestFloor, EastFloor, Mathf.InverseLerp(L.TunnelWestX, L.TunnelEastX, x));

        // ================================================================== Kareth: Kerbe am Westportal

        /// <summary>Bereich der Kerbe in der Kareth-Ostwand (x), in den die Roehre hineinlaeuft.</summary>
        public const float KarethNotchX0 = L.TunnelWestX - 1f, KarethNotchX1 = L.TunnelWestX + 21f;

        /// <summary>
        /// Portal ohne Terrain-Loecher (Loecher machten das NavMesh-Backen extrem langsam): im Kareth-Gelaende wird eine
        /// schmale Kerbe (13 m breit) unter den Tunnelboden abgesenkt; die Roehre liegt darin, Felsbloecke schliessen den
        /// Spalt ueber ihrer Decke. Frueher gesetzte Loecher werden entfernt. Kareths Hoehenfunktion bleibt unveraendert.
        /// </summary>
        public static void ApplyKarethPortal(TerrainData data)
        {
            var origin = new Vector3(KarethTerrainBuilder.MinX, -40f, KarethTerrainBuilder.MinZ);
            ClearHoles(data, new Vector2(origin.x, origin.z), KarethTerrainBuilder.Size);
            LowerNotch(data, origin, KarethTerrainBuilder.Size, KarethNotchX0, KarethNotchX1, WestFloor - 1.5f);
        }

        /// <summary>Halbe Breite der Kerbe: Roehre (4 m) + Wand (1,5 m) + eine Gelaendezelle (2,34 m) Luft.</summary>
        public const float NotchHalfWidth = 6.4f;

        private static void LowerNotch(TerrainData data, Vector3 origin, float size, float x0, float x1, float floorY)
        {
            int res = data.heightmapResolution;
            float cell = size / (res - 1);
            int ix0 = Mathf.Clamp(Mathf.CeilToInt((x0 - origin.x) / cell), 0, res - 1);
            int ix1 = Mathf.Clamp(Mathf.FloorToInt((x1 - origin.x) / cell), 0, res - 1);
            int iz0 = Mathf.Clamp(Mathf.CeilToInt((L.TunnelZ - NotchHalfWidth - origin.z) / cell), 0, res - 1);
            int iz1 = Mathf.Clamp(Mathf.FloorToInt((L.TunnelZ + NotchHalfWidth - origin.z) / cell), 0, res - 1);
            if (ix1 < ix0 || iz1 < iz0) return;
            float[,] h = data.GetHeights(ix0, iz0, ix1 - ix0 + 1, iz1 - iz0 + 1);
            float target = Mathf.Clamp01((floorY - origin.y) / data.size.y);
            for (int z = 0; z < h.GetLength(0); z++)
            for (int x = 0; x < h.GetLength(1); x++)
                h[z, x] = Mathf.Min(h[z, x], target);
            data.SetHeights(ix0, iz0, h);
            EditorUtility.SetDirty(data);
        }

        private static void ClearHoles(TerrainData data, Vector2 origin, float size)
        {
            int res = data.holesResolution;
            float cell = size / res;
            int ix0 = Mathf.Clamp(Mathf.FloorToInt((L.TunnelWestX - 10f - origin.x) / cell), 0, res - 1);
            int ix1 = Mathf.Clamp(Mathf.CeilToInt((L.TunnelWestX + 30f - origin.x) / cell), 0, res - 1);
            int iz0 = Mathf.Clamp(Mathf.FloorToInt((L.TunnelZ - 12f - origin.y) / cell), 0, res - 1);
            int iz1 = Mathf.Clamp(Mathf.CeilToInt((L.TunnelZ + 12f - origin.y) / cell), 0, res - 1);
            bool[,] cur = data.GetHoles(ix0, iz0, ix1 - ix0 + 1, iz1 - iz0 + 1);
            bool any = false;
            foreach (bool solid in cur) any |= !solid;
            if (!any) return;
            var solidAll = new bool[iz1 - iz0 + 1, ix1 - ix0 + 1];
            for (int z = 0; z < solidAll.GetLength(0); z++)
            for (int x = 0; x < solidAll.GetLength(1); x++) solidAll[z, x] = true;
            data.SetHoles(ix0, iz0, solidAll);
            EditorUtility.SetDirty(data);
        }

        // ================================================================== Aufbau in der Welt-Ebene

        /// <summary>Tunnel und Fernansicht (ersetzt eine vorhandene Gruppe gleichen Namens).</summary>
        public static Transform Build(Transform env)
        {
            Transform old = env.Find(GroupName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            Transform root = K.Group(GroupName, env);
            _stone = K.Mat("GG_Stein", new Color(0.42f, 0.41f, 0.4f));
            _stoneDark = K.Mat("GG_Stein_Dunkel", new Color(0.26f, 0.25f, 0.25f));
            _wood = K.Mat("GG_Holz", new Color(0.3f, 0.21f, 0.14f));
            _lamp = K.Mat("GG_Laterne", new Color(1f, 0.7f, 0.35f), 0.2f, new Color(1f, 0.55f, 0.2f) * 2.2f);
            BuildTunnel(root);
            BuildProxies(root);
            return root;
        }

        private static void BuildTunnel(Transform root)
        {
            Vector3 w = new Vector3(L.TunnelWestX, WestFloor, L.TunnelZ);
            Vector3 e = new Vector3(L.TunnelEastX, EastFloor, L.TunnelZ);
            float len = Vector3.Distance(w, e);
            float pitch = Mathf.Atan2(e.y - w.y, e.x - w.x) * Mathf.Rad2Deg;
            Transform t = K.Group("Grenztunnel", root, w, new Vector3(-pitch, 90f, 0f));
            float wd = L.TunnelWidth, h = L.TunnelHeight;
            // Roehre (Waende reichen unter den Boden, damit das Gelaendeloch nirgends durchscheint)
            K.Box("Boden", t, new Vector3(0f, -0.4f, len * 0.5f), new Vector3(wd + 2f, 0.8f, len), _stone);
            K.Box("Wand Nord", t, new Vector3(-(wd * 0.5f + 0.75f), h * 0.5f - 1.5f, len * 0.5f), new Vector3(1.5f, h + 3f, len), _stoneDark);
            K.Box("Wand Sued", t, new Vector3(wd * 0.5f + 0.75f, h * 0.5f - 1.5f, len * 0.5f), new Vector3(1.5f, h + 3f, len), _stoneDark);
            K.Box("Decke", t, new Vector3(0f, h + 0.75f, len * 0.5f), new Vector3(wd + 3f, 1.5f, len), _stoneDark);
            // Stuetzen und Laternen (Bergleute der Salzstrasse haben den Durchgang ausgebaut)
            int n = 0;
            for (float z = 6f; z < len - 4f; z += 12f, n++)
            {
                K.Box("Stuetze", t, new Vector3(-wd * 0.5f + 0.25f, h * 0.5f, z), new Vector3(0.5f, h, 0.5f), _wood);
                K.Box("Stuetze", t, new Vector3(wd * 0.5f - 0.25f, h * 0.5f, z), new Vector3(0.5f, h, 0.5f), _wood);
                K.Box("Balken", t, new Vector3(0f, h - 0.3f, z), new Vector3(wd, 0.6f, 0.6f), _wood);
                if (n % 2 == 0)
                {
                    K.Box("Laterne", t, new Vector3(wd * 0.5f - 0.6f, h * 0.55f, z + 0.5f), new Vector3(0.3f, 0.45f, 0.3f), _lamp, default, false);
                    K.PointLight("Laternenlicht", t, new Vector3(wd * 0.5f - 1.2f, h * 0.55f, z + 0.5f), new Color(1f, 0.66f, 0.35f), 11f, 1.6f, true);
                }
            }
            // Portale: breite Rahmen verdecken die Kanten des Gelaendelochs
            foreach (float z in new[] { 0f, len })
            {
                Transform p = K.Group(z < 1f ? "Westportal" : "Ostportal", t, new Vector3(0f, 0f, z), Vector3.zero);
                K.Box("Pfeiler Nord", p, new Vector3(-(wd * 0.5f + 1.6f), h * 0.5f + 0.5f, 0f), new Vector3(3.2f, h + 5f, 3.5f), _stone);
                K.Box("Pfeiler Sued", p, new Vector3(wd * 0.5f + 1.6f, h * 0.5f + 0.5f, 0f), new Vector3(3.2f, h + 5f, 3.5f), _stone);
                K.Box("Sturz", p, new Vector3(0f, h + 2f, 0f), new Vector3(wd + 7f, 3f, 3.5f), _stone);
                K.Box("Laterne", p, new Vector3(wd * 0.5f + 0.2f, h * 0.6f, z < 1f ? -1.9f : 1.9f), new Vector3(0.35f, 0.5f, 0.35f), _lamp, default, false);
                K.PointLight("Portallicht", p, new Vector3(wd * 0.5f - 0.6f, h * 0.6f, z < 1f ? -2.6f : 2.6f), new Color(1f, 0.66f, 0.35f), 14f, 1.8f, true);
            }
            // Felsdach: schliesst den Spalt der Kerbe ueber der Roehre (Stufen bis zur Hoehe des Nachbargelaendes)
            Transform roof = K.Group("Felsdach", root);
            FillNotch(roof, KarethNotchX0, KarethNotchX1, x => KarethTerrainBuilder.Height(x, L.TunnelZ + NotchHalfWidth + 1.5f));
            // 30.09.: hinter dem Ostportal liegt jetzt erzeugtes Gelaende (Eschenfeld) statt des Grenzgebirges
            FillNotch(roof, L.TunnelNotchX0, L.TunnelEastX + 0.5f, x => TerrainGen.GeneratedHeights.Sample(x, L.TunnelZ + NotchHalfWidth + 1.5f, KarethTerrainBuilder.Height(599f, L.TunnelZ + NotchHalfWidth + 1.5f)));
            // Am Ende der Kerbe schneidet die Gelaendeflaeche schraeg durch die Roehre (Terrain ist ein Hoehenfeld). Figuren
            // laufen durch, das NavMesh-Backen sieht dort aber eine Wand: eine Verbindung ueberbrueckt die Stelle.
            var link = new GameObject("NavMesh-Verbindung Kerbenende").AddComponent<NavMeshLink>();
            link.transform.SetParent(root, false);
            link.startPoint = new Vector3(KarethNotchX1 - 7f, FloorAt(KarethNotchX1 - 7f), L.TunnelZ);
            link.endPoint = new Vector3(KarethNotchX1 + 9f, FloorAt(KarethNotchX1 + 9f), L.TunnelZ);
            link.width = L.TunnelWidth - 2f;
            link.bidirectional = true;
            // Gleiches am Ostportal: dort faellt das erzeugte Gelaende vom Fels (ueber der Decke) auf die Grabensohle
            var linkEast = new GameObject("NavMesh-Verbindung Ostportal").AddComponent<NavMeshLink>();
            linkEast.transform.SetParent(root, false);
            linkEast.startPoint = new Vector3(L.TunnelEastX - 10f, FloorAt(L.TunnelEastX - 10f), L.TunnelZ);
            linkEast.endPoint = new Vector3(L.TunnelEastX + 3f, EastFloor, L.TunnelZ);
            linkEast.width = L.TunnelWidth - 2f;
            linkEast.bidirectional = true;
            // Westportal ragt aus dem flachen Aschenfeld: Portalbau bis in die Wand
            K.Label(t, new Vector3(0f, h + 5f, -3f), "Osttunnel", "Eschenfeld · Talpass", WorldLabelStyle.Place, 120f);
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
        }

        private static void FillNotch(Transform parent, float x0, float x1, System.Func<float, float> sideHeight)
        {
            const float step = 2f;
            for (float x = x0; x < x1; x += step)
            {
                float xm = x + step * 0.5f;
                float ceiling = FloorAt(Mathf.Clamp(xm, L.TunnelWestX, L.TunnelEastX)) + L.TunnelHeight + 1.5f;
                float top = Mathf.Max(sideHeight(x), sideHeight(x + step)) + 0.3f;
                if (top <= ceiling + 0.2f) continue;
                K.Box("Fels", parent, new Vector3(xm, (ceiling + top) * 0.5f, L.TunnelZ), new Vector3(step + 0.3f, top - ceiling, NotchHalfWidth * 2f + 1.5f), _stone);
            }
        }

        // ================================================================== NavMesh im Tunnel

        /// <summary>Frueheres eigenes Tunnel-NavMesh (wird entfernt).</summary>
        public const string TunnelNavMeshPath = ProjectSetup.Root + "/Scenes/Grenztunnel_NavMesh.asset";

        // ================================================================== Fernansicht (Proxy)

        private const float ProxyStep = 16f;

        private static void BuildProxies(Transform root)
        {
            Transform g = K.Group("Fernansicht Grenzgebirge", root);
            BuildProxy(g, WorldManifestBuilder.CellBorderWest, WorldManifestBuilder.BorderWestScene, L.MinX, L.MinX + L.TileSize);
            BuildProxy(g, WorldManifestBuilder.CellBorderEast, WorldManifestBuilder.BorderEastScene, L.MinX + L.TileSize, L.MaxX);
        }

        private static void BuildProxy(Transform parent, string cellId, string scenePath, float x0, float x1)
        {
            float z0 = L.MinZ, z1 = L.MaxZ;
            int nx = Mathf.RoundToInt((x1 - x0) / ProxyStep) + 1, nz = Mathf.RoundToInt((z1 - z0) / ProxyStep) + 1;
            var hs = new float[nz, nx];
            for (int iz = 0; iz < nz; iz++)
            for (int ix = 0; ix < nx; ix++)
                hs[iz, ix] = L.Height(x0 + ix * ProxyStep, z0 + iz * ProxyStep);

            var verts = new Vector3[nx * nz];
            var uvs = new Vector2[nx * nz];
            var tex = new Texture2D(nx, nz, TextureFormat.RGB24, true) { name = "GG_Proxy_" + cellId, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[nx * nz];
            for (int iz = 0; iz < nz; iz++)
            for (int ix = 0; ix < nx; ix++)
            {
                int i = iz * nx + ix;
                // 30.09.: Entwurfsraum → Welt (Kern suedoestlich von Kareth, BorderMountainsFrame)
                verts[i] = BorderMountainsFrame.ToWorld(new Vector3(x0 + ix * ProxyStep, hs[iz, ix] - 1.5f, z0 + iz * ProxyStep));
                uvs[i] = new Vector2(ix / (float)(nx - 1), iz / (float)(nz - 1));
                float sx = (hs[iz, Mathf.Min(ix + 1, nx - 1)] - hs[iz, Mathf.Max(ix - 1, 0)]) / (2f * ProxyStep);
                float sz = (hs[Mathf.Min(iz + 1, nz - 1), ix] - hs[Mathf.Max(iz - 1, 0), ix]) / (2f * ProxyStep);
                px[i] = BorderMountainsBuilder.GroundColor(hs[iz, ix], Mathf.Sqrt(sx * sx + sz * sz));
            }
            tex.SetPixels(px);
            tex.Apply(true);
            var tris = new int[(nx - 1) * (nz - 1) * 6];
            int k = 0;
            for (int iz = 0; iz < nz - 1; iz++)
            for (int ix = 0; ix < nx - 1; ix++)
            {
                int a = iz * nx + ix, b = a + 1, c = a + nx, d = c + 1;
                tris[k++] = a; tris[k++] = c; tris[k++] = b;
                tris[k++] = b; tris[k++] = c; tris[k++] = d;
            }
            var mesh = new Mesh { name = "GG_Proxy_" + cellId, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            string folder = ProjectSetup.Root + "/Terrain";
            ProjectSetup.EnsureFolder(folder);
            string meshPath = $"{folder}/GG_Proxy_{cellId}.asset", texPath = $"{folder}/GG_Proxy_{cellId}_Farbe.asset", matPath = $"{folder}/GG_Proxy_{cellId}.mat";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.DeleteAsset(texPath);
            AssetDatabase.CreateAsset(mesh, meshPath);
            AssetDatabase.CreateAsset(tex, texPath);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(K.LitShader());
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.shader = K.LitShader();
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0f);
            EditorUtility.SetDirty(mat);

            var go = new GameObject("Fernansicht " + cellId);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;
            go.AddComponent<WorldCellProxy>().Configure(cellId, System.IO.Path.GetFileNameWithoutExtension(scenePath));
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }

        // ================================================================== In die offene Welt-Ebene einsetzen

        /// <summary>
        /// Tunnel + Fernansicht in die offene Sandbox_M1 setzen, Kerbe ins Kareth-Gelaende senken und das Kareth-NavMesh
        /// neu backen (der Tunnelboden gehoert dazu). Kein Neuaufbau von Kareth.
        /// </summary>
        public static bool ApplyToWorld(Scene world)
        {
            Transform env = null;
            foreach (GameObject go in world.GetRootGameObjects())
                if (go.name == "Environment") env = go.transform;
            if (env == null)
            {
                Debug.LogError("[Ashveil] Grenzgebirge: 'Environment' fehlt in Sandbox_M1 – bitte zuerst 'Sandbox-Szene erstellen'.");
                return false;
            }
            Build(env);

            Terrain kareth = null;
            foreach (Terrain t in env.GetComponentsInChildren<Terrain>(true))
                if (t.name == "Terrain Kareth") kareth = t;
            if (kareth != null)
            {
                ApplyKarethPortal(kareth.terrainData);
                kareth.allowAutoConnect = true;
                kareth.groupingID = 0;
                AssetDatabase.SaveAssetIfDirty(kareth.terrainData);
            }
            else Debug.LogWarning("[Ashveil] Grenzgebirge: 'Terrain Kareth' nicht gefunden – Westportal ohne Gelaendeloch.");

            WorldManifestBuilder.EnsureStreamer();
            // Kareth-NavMesh neu backen (ohne Terrain-Loecher unter einer Sekunde), Volumen bis x = 662 fuer den Tunnel
            foreach (NavMeshSurface s in env.GetComponentsInChildren<NavMeshSurface>(true))
                if (s.name.StartsWith("NavMesh Kareth"))
                {
                    s.center = EnemyBuilder.KarethNavMeshCenter;
                    s.size = EnemyBuilder.KarethNavMeshSize;
                    EnemyBuilder.BakeNavMesh(s, ProjectSetup.Root + "/Scenes/Sandbox_M1_NavMesh.asset");
                }
            AssetDatabase.DeleteAsset(TunnelNavMeshPath); // frueherer Versuch: eigenes Tunnel-NavMesh (doppelt ueberlappend)

            EditorSceneManager.MarkSceneDirty(world);
            EditorSceneManager.SaveScene(world);
            return true;
        }
    }
}
