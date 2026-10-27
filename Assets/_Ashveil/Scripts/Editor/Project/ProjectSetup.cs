using System.Text;
using Ashveil.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Projektweite Einrichtung: Ordner, Layer, Pruefung der Einstellungen.
    /// Alles idempotent – mehrfaches Ausfuehren schadet nicht.
    /// </summary>
    public static class ProjectSetup
    {
        /// <summary>Alle eigenen Inhalte liegen hier. Aufbau: Docs/Ordnerstruktur.md.</summary>
        public const string Root = "Assets/_Ashveil";

        /// <summary>Greybox-Materialien (Platzhalter-Farben fuer Formen ohne Modell).</summary>
        public const string GreyboxMaterials = Root + "/Art/Materials/Greybox";

        private static readonly string[] Folders =
        {
            Root + "/Animations",
            Root + "/Art/Materials/Greybox",
            Root + "/Art/Models",
            Root + "/Art/Models_Eigene",
            Root + "/Art/Rendering",
            Root + "/Art/Textures",
            Root + "/Audio",
            Root + "/Data/Enemies",
            Root + "/Data/Items",
            Root + "/Data/Loot",
            Root + "/Data/Quests",
            Root + "/Data/Weapons",
            Root + "/Layout",
            Root + "/Prefabs/Characters",
            Root + "/Prefabs/Enemies",
            Root + "/Prefabs/World",
            Root + "/Scenes",
            Root + "/Settings",
            Root + "/Terrain",
            Root + "/VFX"
        };

        [MenuItem("Ashveil/Projekt/Ordner & Layer einrichten", priority = 100)]
        public static void RunSetup()
        {
            EnsureFolders();
            EnsureLayers();
            Debug.Log("[Ashveil] Ordner und Layer eingerichtet.");
        }

        public static void EnsureFolders()
        {
            foreach (string folder in Folders) EnsureFolder(folder);
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        public static void EnsureLayers()
        {
            EnsureLayer(GameLayers.Player);
            EnsureLayer(GameLayers.Enemy);
            EnsureLayer(GameLayers.Interactable);
            EnsureLayer(Ashveil.Gameplay.Visuals.CharacterPreviewStage.LayerName); // Charaktervorschau (nur eigene Kamera)
        }

        private static void EnsureLayer(string layerName)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogError("[Ashveil] TagManager.asset nicht gefunden.");
                return;
            }

            var tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            for (int i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == layerName) return;

            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;
                slot.stringValue = layerName;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"[Ashveil] Layer '{layerName}' auf Platz {i} angelegt.");
                return;
            }
            Debug.LogError($"[Ashveil] Kein freier Layer-Platz fuer '{layerName}'.");
        }

        [MenuItem("Ashveil/Projekt/Projekt-Check (Console)", priority = 101)]
        public static void CheckProject()
        {
            var sb = new StringBuilder("[Ashveil] Projekt-Check\n");
            int problems = 0;

            RenderPipelineAsset rp = GraphicsSettings.currentRenderPipeline;
            bool urp = rp != null && rp.GetType().Name.Contains("Universal");
            sb.AppendLine(urp ? "OK   Render Pipeline: URP" : "WARN Render Pipeline ist nicht URP (" + (rp == null ? "Built-in" : rp.GetType().Name) + ")");
            if (!urp) problems++;

            bool linear = PlayerSettings.colorSpace == ColorSpace.Linear;
            sb.AppendLine(linear ? "OK   Color Space: Linear" : "WARN Color Space ist Gamma – Project Settings > Player > Other Settings > Color Space = Linear");
            if (!linear) problems++;

#if ASHVEIL_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            sb.AppendLine("OK   Input: Input System Package aktiv");
#elif ENABLE_LEGACY_INPUT_MANAGER
            sb.AppendLine("INFO Input: alter Input Manager (funktioniert; empfohlen ist das Input System Package)");
#else
            sb.AppendLine("FEHLER Kein Input-Backend aktiv");
            problems++;
#endif

            foreach (string layer in new[] { GameLayers.Player, GameLayers.Enemy, GameLayers.Interactable })
            {
                bool ok = LayerMask.NameToLayer(layer) >= 0;
                sb.AppendLine(ok ? $"OK   Layer {layer}" : $"WARN Layer {layer} fehlt – Menue Ashveil > Projekt > Ordner & Layer einrichten");
                if (!ok) problems++;
            }

            sb.AppendLine(problems == 0 ? "Alles bereit." : $"{problems} Hinweis(e).");
            if (problems == 0) Debug.Log(sb.ToString());
            else Debug.LogWarning(sb.ToString());
        }
    }
}
