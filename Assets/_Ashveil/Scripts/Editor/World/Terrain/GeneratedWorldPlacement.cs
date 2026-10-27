using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Ashveil.Rendering;
using Ashveil.Rendering.PathTracing;
using Ashveil.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashveil.EditorTools.TerrainGen
{
    /// <summary>
    /// Setzt die erzeugten Weltteile in die Welt-Ebene (Sandbox_M1): GEN-Kacheln mit Kollider, das Meer um Kareth und
    /// die Fernansicht. Alles unter EINEM Knoten "GEN_World", eindeutig als erzeugt markiert und jederzeit entfernbar
    /// (Vorgabe §74, §97, §105). Liest nur vorhandene Assets – rechnet nichts neu. Der Sandbox-Neuaufbau ruft das
    /// nach dem Kareth-NavMesh auf, damit Kareths NavMesh unveraendert bleibt.
    /// </summary>
    public static class GeneratedWorldPlacement
    {
        public const string RootName = "GEN_World (erzeugt, loeschbar)";

        public static bool HasGeneratedContent =>
            AssetDatabase.IsValidFolder(TerrainGenPaths.TerrainFolder) || AssetDatabase.IsValidFolder(TerrainGenPaths.ProxyFolder);

        public static void RemoveFromOpenScene()
        {
            GameObject old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);
        }

        public static GameObject PlaceIntoOpenScene(bool undo = true)
        {
            RemoveFromOpenScene();
            if (!HasGeneratedContent) return null;
            var root = new GameObject(RootName);
            if (undo) Undo.RegisterCreatedObjectUndo(root, "Erzeugte Welt setzen");

            // ---- Kacheln
            var terrains = new List<UnityEngine.Terrain>();
            var terrainRoot = new GameObject("GEN_Terrain").transform;
            terrainRoot.SetParent(root.transform, false);
            var terrainMat = AssetDatabase.LoadAssetAtPath<Material>(ProjectSetup.Root + "/Terrain/Kareth_Terrain.mat");
            if (AssetDatabase.IsValidFolder(TerrainGenPaths.TerrainFolder))
                foreach (string guid in AssetDatabase.FindAssets("t:TerrainData", new[] { TerrainGenPaths.TerrainFolder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!TryParseCoord(Path.GetFileNameWithoutExtension(path), out Vector2Int coord)) continue;
                    var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
                    GameObject go = UnityEngine.Terrain.CreateTerrainGameObject(data);
                    go.name = $"GEN_Terrain_{coord.x}_{coord.y}";
                    go.transform.SetParent(terrainRoot, false);
                    Vector2 o = TerrainTileGenerator.TileOrigin(coord);
                    go.transform.position = new Vector3(o.x, TerrainTileGenerator.BaseY, o.y);
                    var t = go.GetComponent<UnityEngine.Terrain>();
                    if (terrainMat != null) t.materialTemplate = terrainMat;
                    t.heightmapPixelError = 8;
                    t.basemapDistance = 500;
                    t.drawInstanced = true;
                    t.allowAutoConnect = true;
                    t.groupingID = 0;
                    t.treeDistance = 0;
                    t.detailObjectDistance = 0;
                    terrains.Add(t);
                }

            // ---- Vorlaeufiger Weltrand: hinter dem begehbaren Umland liegt nur Fernansicht (kein Boden)
            if (terrains.Count > 0) BuildBoundary(root.transform);

            // ---- Meer um das Umland (Kareths eigenes Meer bleibt, hier ein Loch)
            Material sea = FindSeaMaterial();
            if (sea != null && terrains.Count > 0) BuildSea(root.transform, sea);

            // ---- Fernansicht
            if (AssetDatabase.IsValidFolder(TerrainGenPaths.ProxyFolder))
            {
                var proxy = new GameObject("GEN_Fernansicht");
                proxy.transform.SetParent(root.transform, false);
                var pr = proxy.AddComponent<DistantTerrainProxyRoot>();
                var profile = AssetDatabase.LoadAssetAtPath<MacroWorldProfile>(MacroWorldProfile.AssetPath);
                if (profile != null) pr.worldBounds = profile.bounds;
                Material mat = ProxyMeshGenerator.EnsureMaterial();
                foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { TerrainGenPaths.ProxyFolder }))
                {
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(guid));
                    if (mesh == null) continue;
                    var go = new GameObject(mesh.name);
                    go.transform.SetParent(proxy.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    mr.lightProbeUsage = LightProbeUsage.Off;
                    mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    mr.allowOcclusionWhenDynamic = false;
                    go.AddComponent<PathTraceSurface>().kind = PathTraceSurface.Kind.Ausschliessen;
                }
            }
            return root;
        }

        public static bool TryParseCoord(string file, out Vector2Int coord)
        {
            coord = default;
            // GEN_Terrain_{x}_{z}_TerrainData
            string[] parts = file.Split('_');
            if (parts.Length < 4 || parts[0] != "GEN" || parts[1] != "Terrain") return false;
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)) return false;
            if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int z)) return false;
            coord = new Vector2Int(x, z);
            return true;
        }

        private static void BuildBoundary(Transform root)
        {
            Rect a = TerrainTileGenerator.RingArea;
            var g = new GameObject("GEN_Weltrand (vorlaeufig)");
            g.transform.SetParent(root, false);
            void Wall(string name, Vector3 c, Vector3 size)
            {
                var w = new GameObject(name);
                w.transform.SetParent(g.transform, false);
                w.transform.position = c;
                w.AddComponent<BoxCollider>().size = size;
            }
            const float h = 3000f, y = 1000f;
            Wall("Rand Nord", new Vector3(a.center.x, y, a.yMax + 1f), new Vector3(a.width, h, 2f));
            Wall("Rand Sued", new Vector3(a.center.x, y, a.yMin - 1f), new Vector3(a.width, h, 2f));
            Wall("Rand West", new Vector3(a.xMin - 1f, y, a.center.y), new Vector3(2f, h, a.height));
            Wall("Rand Ost", new Vector3(a.xMax + 1f, y, a.center.y), new Vector3(2f, h, a.height));
        }

        private static Material FindSeaMaterial()
        {
            foreach (string guid in AssetDatabase.FindAssets("Water_Sea t:Material"))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null) return m;
            }
            return null;
        }

        /// <summary>Meeresflaeche ueber dem Umland, mit Loch ueber Kareth (dort liegt Kareths eigenes Meer).</summary>
        private static void BuildSea(Transform root, Material mat)
        {
            Rect a = TerrainTileGenerator.RingArea;
            Rect hole = new Rect(-600f, -350f, 1200f, 1200f);
            float y = KarethTerrainBuilder.SeaLevel - 0.12f;
            var rects = new List<Rect>
            {
                Rect.MinMaxRect(a.xMin, a.yMin, a.xMax, hole.yMin),
                Rect.MinMaxRect(a.xMin, hole.yMax, a.xMax, a.yMax),
                Rect.MinMaxRect(a.xMin, hole.yMin, hole.xMin, hole.yMax),
                Rect.MinMaxRect(hole.xMax, hole.yMin, a.xMax, hole.yMax),
            };
            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (Rect r in rects)
            {
                int b = verts.Count;
                verts.Add(new Vector3(r.xMin, y, r.yMin));
                verts.Add(new Vector3(r.xMin, y, r.yMax));
                verts.Add(new Vector3(r.xMax, y, r.yMax));
                verts.Add(new Vector3(r.xMax, y, r.yMin));
                tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            var mesh = new Mesh { name = "GEN_Meer_Umland" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            ProjectSetup.EnsureFolder(TerrainGenPaths.ProxyFolder);
            string path = TerrainGenPaths.GeneratedRoot + "/GEN_Meer_Umland.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject("GEN_Meer_Umland");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
