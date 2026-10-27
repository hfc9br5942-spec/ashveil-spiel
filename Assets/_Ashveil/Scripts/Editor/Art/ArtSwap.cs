using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ashveil.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Modelle spaeter austauschen, ohne Kareth neu zu bauen (Kanon 26.09.: gezielt ersetzen statt neu erzeugen).
    ///
    /// Ablauf fuer eigene Modelle:
    ///   1. "Ashveil &gt; Grafik &gt; Modellliste schreiben" – zeigt alle Slots mit Groesse, Anzahl und Kollision.
    ///   2. Eigenes Modell mit GENAU dem Slot-Namen nach Assets/_Ashveil/Art/Models_Eigene legen (.fbx oder .prefab;
    ///      Drehpunkt unten mittig, 1 Einheit = 1 m, Y oben, etwa gleiche Groesse wie der Platzhalter).
    ///   3. "Ashveil &gt; Grafik &gt; Modelle austauschen" – sichert Kareth, ersetzt alle Vorkommen an Ort und Stelle
    ///      (Lage, Drehung, Groesse bleiben), setzt Kollision neu, aktualisiert Gras/Farn und backt das NavMesh.
    /// Loeschen des eigenen Modells + erneutes Austauschen = zurueck zum Blender-Platzhalter.
    /// </summary>
    public static class ArtSwap
    {
        [MenuItem("Ashveil/Grafik/Modelle austauschen (ohne Neuaufbau)", priority = 404)]
        public static void SwapMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play beenden.", "OK");
                return;
            }
            string report = SwapAll();
            EditorUtility.DisplayDialog("Ashveil", report, "OK");
        }

        [MenuItem("Ashveil/Grafik/Modellliste schreiben", priority = 405)]
        public static void ListMenu()
        {
            string path = WriteModelList();
            Debug.Log("[Ashveil] Modellliste -> " + path);
            EditorUtility.DisplayDialog("Ashveil", "Modellliste geschrieben:\n" + path, "OK");
        }

        public static string SwapAll()
        {
            ProjectSetup.EnsureFolder(ArtModels.OverrideFolder);
            KarethBackup.Run();
            ArtSlot[] slots = Object.FindObjectsByType<ArtSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int swapped = 0, custom = 0, missing = 0;
            var lodHosts = new HashSet<LODGroup>();
            try
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    ArtSlot slot = slots[i];
                    if (slot == null) continue;
                    string slotName = slot.SlotName;
                    if (i % 50 == 0) EditorUtility.DisplayProgressBar("Ashveil", $"Modelle austauschen ({i}/{slots.Length}) ...", (float)i / slots.Length);
                    string source = ArtModels.SourceOf(slotName);
                    if (source == null) { missing++; continue; }
                    GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(source);
                    Transform old = slot.transform;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, old.gameObject.scene);
                    go.name = old.name;
                    go.transform.SetParent(old.parent, false);
                    go.transform.SetSiblingIndex(old.GetSiblingIndex());
                    go.transform.localPosition = old.localPosition;
                    go.transform.localRotation = old.localRotation;
                    go.transform.localScale = old.localScale;
                    ArtModels.Finish(go, slotName, (ArtModels.ColliderKind)slot.ColliderKind, source);
                    LODGroup group = old.parent != null ? old.parent.GetComponent<LODGroup>() : null;
                    if (group != null) lodHosts.Add(group);
                    Object.DestroyImmediate(old.gameObject);
                    swapped++;
                    if (ArtModels.IsOverridden(slotName)) custom++;
                }
                foreach (LODGroup g in lodHosts) RebuildLods(g);
                int details = RefreshTerrainDetails();

                EditorUtility.DisplayProgressBar("Ashveil", "NavMesh neu backen ...", 0.95f);
                NavMeshSurface surface = Object.FindAnyObjectByType<NavMeshSurface>();
                if (surface != null) EnemyBuilder.BakeNavMesh(surface, ProjectSetup.Root + "/Scenes/Sandbox_M1_NavMesh.asset");
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                EditorSceneManager.SaveOpenScenes();
                string msg = $"{swapped} Modelle ausgetauscht ({custom} davon eigene aus Models_Eigene), {details} Gras-/Farnarten aktualisiert" +
                             (missing > 0 ? $", {missing} Slots ohne Modell uebersprungen" : "") + ". NavMesh neu gebacken, Szene gespeichert.";
                Debug.Log("[Ashveil] " + msg);
                return msg;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void RebuildLods(LODGroup group)
        {
            var lod0 = new List<Renderer>();
            var lod1 = new List<Renderer>();
            foreach (ArtSlot s in group.GetComponentsInChildren<ArtSlot>())
                (s.SlotName.EndsWith("_LOD1") ? lod1 : lod0).AddRange(s.GetComponentsInChildren<Renderer>());
            var lods = new List<LOD> { new LOD(lod1.Count > 0 ? 0.1f : 0f, lod0.ToArray()) };
            if (lod1.Count > 0) lods.Add(new LOD(0f, lod1.ToArray()));
            group.SetLODs(lods.ToArray());
            group.RecalculateBounds();
        }

        private static int RefreshTerrainDetails()
        {
            int changed = 0;
            foreach (Terrain t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                TerrainData data = t.terrainData;
                DetailPrototype[] protos = data.detailPrototypes;
                for (int i = 0; i < protos.Length; i++)
                {
                    if (protos[i].prototype == null) continue;
                    GameObject now = ArtModels.Get(protos[i].prototype.name);
                    if (now == null || now == protos[i].prototype) continue;
                    protos[i].prototype = now;
                    changed++;
                }
                if (changed == 0) continue;
                data.detailPrototypes = protos;
                data.RefreshPrototypes();
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssetIfDirty(data);
            }
            return changed;
        }

        /// <summary>Liste aller Slots fuer Modellbauer: Name, Quelle, Groesse, Dreiecke, Anzahl, Kollision.</summary>
        public static string WriteModelList()
        {
            var counts = new Dictionary<string, int>();
            var colliders = new Dictionary<string, string>();
            foreach (ArtSlot s in Object.FindObjectsByType<ArtSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                counts[s.SlotName] = counts.TryGetValue(s.SlotName, out int c) ? c + 1 : 1;
                colliders[s.SlotName] = ((ArtModels.ColliderKind)s.ColliderKind).ToString();
            }
            var names = new SortedSet<string>(counts.Keys);
            foreach (string dir in new[] { ArtPipeline.ModelFolder, ArtModels.OverrideFolder })
                foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { dir }))
                    names.Add(Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid)));

            var sb = new StringBuilder();
            sb.AppendLine("# ASHVEIL – Modellliste (austauschbare Slots)");
            sb.AppendLine();
            sb.AppendLine("Eigenes Modell mit genau diesem Namen nach `Assets/_Ashveil/Art/Models_Eigene/` legen (.fbx oder .prefab),");
            sb.AppendLine("dann **Ashveil > Grafik > Modelle austauschen**. Drehpunkt unten mittig, 1 Einheit = 1 m, Y oben;");
            sb.AppendLine("Groesse etwa wie angegeben (die Szene behaelt Lage, Drehung und Skalierung jedes Vorkommens).");
            sb.AppendLine("Riesen-Module liegen in Weltkoordinaten (Drehpunkt = Wachtfeuer) und muessen deckungsgleich bleiben.");
            sb.AppendLine();
            sb.AppendLine("| Slot | Quelle | Groesse B x H x T (m) | Dreiecke | Anzahl in Kareth | Kollision |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- |");
            foreach (string n in names)
            {
                GameObject go = ArtModels.Get(n);
                if (go == null) continue;
                Bounds b = new Bounds();
                bool first = true;
                int tris = 0;
                foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    Bounds mb = mf.sharedMesh.bounds;
                    if (first) { b = mb; first = false; } else b.Encapsulate(mb);
                    tris += mf.sharedMesh.triangles.Length / 3;
                }
                string src = ArtModels.IsOverridden(n) ? "**eigenes**" : "Blender-Platzhalter";
                counts.TryGetValue(n, out int count);
                colliders.TryGetValue(n, out string col);
                sb.AppendLine($"| `{n}` | {src} | {b.size.x:0.0} x {b.size.y:0.0} x {b.size.z:0.0} | {tris} | {count} | {col ?? "–"} |");
            }
            string path = Ashveil.Core.ProjectPaths.SourceArt("Modellliste.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString());
            return path;
        }
    }
}
