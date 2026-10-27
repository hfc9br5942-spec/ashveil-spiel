using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Dauerhafte Layout-Einstellungen fuer Kareth (Asset: _Ashveil/Layout/KarethLayout.asset). Der Szenenbau liest
    /// sie bei jedem "Sandbox-Szene erstellen" – Aenderungen hier ueberstehen also jeden Neuaufbau.
    /// Fuer frei platzierte Handarbeit gibt es zusaetzlich das Szenenobjekt "Handarbeit (bleibt beim Neuaufbau
    /// erhalten)" (siehe <see cref="HandworkKeeper"/>).
    /// </summary>
    public sealed class KarethLayout : ScriptableObject
    {
        public const string AssetPath = ProjectSetup.Root + "/Layout/KarethLayout.asset";

        [Header("Der versteinerte Beschuetzer (Greybox, reversibel)")]
        [Tooltip("Aus = Szene wird wie vor dem Nachtrag gebaut (mit dem kleinen Gefallenen Waechter).")]
        public bool giantEnabled = true;
        [Tooltip("Verschiebt den ganzen Riesen samt Halle, See, Schaedeldecke und Armresten.")]
        public Vector3 giantOffset = Vector3.zero;
        [Tooltip("Dreht den ganzen Riesen um das Wachtfeuer (Grad).")]
        public float giantYaw;
        public bool lakeEnabled = true;
        [Tooltip("Hebt oder senkt den Kratersee (Meter) gegenueber der berechneten Hoehe.")]
        public float lakeLevelOffset;
        [Tooltip("Hauptmann Bralt sitzt in der Halle in der Brust des Riesen statt am Feuer.")]
        public bool leaderInHall = true;

        [Header("Relief (P2.5, 01.10.2026)")]
        [Tooltip("Komponiertes Relief ueber dem bisherigen Kareth (KarethRelief). Aus = Kareth wie bis 30.09. Danach 'Sandbox-Szene erstellen'.")]
        public bool reliefEnabled = true;

        [Header("Handarbeit")]
        [Tooltip("Objekte unter 'Handarbeit (bleibt beim Neuaufbau erhalten)' werden gesichert und wiederhergestellt.")]
        public bool keepHandwork = true;

        public static KarethLayout LoadOrCreate()
        {
            var layout = AssetDatabase.LoadAssetAtPath<KarethLayout>(AssetPath);
            if (layout != null) return layout;
            ProjectSetup.EnsureFolder(ProjectSetup.Root + "/Layout");
            layout = CreateInstance<KarethLayout>();
            AssetDatabase.CreateAsset(layout, AssetPath);
            AssetDatabase.SaveAssets();
            return layout;
        }

        [MenuItem("Ashveil/Layout/Kareth-Layout auswaehlen", priority = 420)]
        private static void Select() => Selection.activeObject = LoadOrCreate();
    }
}
