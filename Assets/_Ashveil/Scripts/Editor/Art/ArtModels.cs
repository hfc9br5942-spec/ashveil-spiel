using Ashveil.World;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Platziert Modelle ueber feste Slot-Namen (z. B. "SM_Pine_A") – mit passendem Kollider und statischen Flags.
    ///
    /// Austauschbar (Wunsch Max, 26.09.): Liegt unter <see cref="OverrideFolder"/> ein eigenes Modell mit demselben
    /// Namen (.prefab oder .fbx), wird es statt des Blender-Platzhalters benutzt. Blender fasst diesen Ordner nie an.
    /// Jedes platzierte Modell bekommt ein <see cref="ArtSlot"/> – damit tauscht "Ashveil &gt; Grafik &gt; Modelle austauschen"
    /// alle Vorkommen in der bestehenden Szene aus, ohne sie neu zu bauen.
    /// Fehlt ein Modell ganz, liefert <see cref="Has"/> false und die Bauer nutzen weiter ihre Greybox-Formen.
    /// </summary>
    public static class ArtModels
    {
        public enum ColliderKind { None, Mesh, Box, Capsule, Trunk }

        /// <summary>Eigene Modelle: gleicher Name wie der Slot, Drehpunkt unten mittig, 1 Einheit = 1 m, Y oben.</summary>
        public const string OverrideFolder = ArtPipeline.ArtRoot + "/Models_Eigene";

        /// <summary>Pfad des Modells, das fuer diesen Slot gilt (eigenes vor generiertem), oder null.</summary>
        public static string SourceOf(string name)
        {
            foreach (string p in new[]
                     {
                         $"{OverrideFolder}/{name}.prefab", $"{OverrideFolder}/{name}.fbx",
                         $"{ArtPipeline.ModelFolder}/{name}.prefab", $"{ArtPipeline.ModelFolder}/{name}.fbx"
                     })
                if (AssetDatabase.LoadAssetAtPath<GameObject>(p) != null) return p;
            return null;
        }

        public static bool IsOverridden(string name) => SourceOf(name)?.StartsWith(OverrideFolder) == true;

        public static GameObject Get(string name)
        {
            string path = SourceOf(name);
            return path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
        }

        public static bool Has(string name) => Get(name) != null;

        public static GameObject Place(string name, Transform parent, Vector3 localPos, float yaw, float scale, ColliderKind collider,
            bool worldPosition = false)
        {
            string source = SourceOf(name);
            if (source == null) return null;
            GameObject src = AssetDatabase.LoadAssetAtPath<GameObject>(source);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.name = name;
            go.transform.SetParent(parent, false);
            if (worldPosition) go.transform.position = localPos;
            else go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
            Finish(go, name, collider, source);
            return go;
        }

        /// <summary>Kollider, statische Flags und Slot-Markierung setzen (auch beim Austauschen benutzt).</summary>
        public static void Finish(GameObject go, string name, ColliderKind collider, string source)
        {
            AddCollider(go, collider);
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
            var slot = go.GetComponent<ArtSlot>() ?? go.AddComponent<ArtSlot>();
            slot.Configure(name, (int)collider, source);
        }

        private static void AddCollider(GameObject go, ColliderKind kind)
        {
            if (kind == ColliderKind.None) return;
            var mf = go.GetComponentInChildren<MeshFilter>();
            Bounds b = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.up * 0.5f, Vector3.one);
            GameObject host = mf != null ? mf.gameObject : go;
            switch (kind)
            {
                case ColliderKind.Mesh:
                    if (mf != null && mf.sharedMesh != null) host.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                    break;
                case ColliderKind.Box:
                    var box = host.AddComponent<BoxCollider>();
                    box.center = b.center;
                    box.size = b.size;
                    break;
                case ColliderKind.Capsule:
                    var cap = host.AddComponent<CapsuleCollider>();
                    cap.center = b.center;
                    cap.height = b.size.y;
                    cap.radius = Mathf.Max(b.size.x, b.size.z) * 0.5f;
                    break;
                case ColliderKind.Trunk:
                    // Nur der Stamm blockiert (Kronen darf man unterlaufen)
                    var trunk = host.AddComponent<CapsuleCollider>();
                    trunk.radius = 0.35f;
                    trunk.height = b.size.y * 0.6f;
                    trunk.center = new Vector3(0f, trunk.height * 0.5f, 0f);
                    break;
            }
        }
    }
}
