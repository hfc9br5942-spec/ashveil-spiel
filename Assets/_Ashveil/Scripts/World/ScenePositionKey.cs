using UnityEngine;

namespace Ashveil.World
{
    /// <summary>
    /// Gespeicherte Position in einer Szene ohne Weltzellen (Kareth v4, Welle 0). Die Weltzellen-Logik (WorldStreamer, Masterplan
    /// §47–49) bleibt unverändert; diese Szene schreibt dieselbe <see cref="WorldPosition"/> mit der Zelle „szene:&lt;Schlüssel&gt;“
    /// und einer Weltversion aus Höhenkarte und Plan. Beim Login gilt die Stelle nur, wenn Zelle und Version passen – sonst
    /// (andere Szene, Gelände neu gebaut) beginnt ein bekannter Charakter am <see cref="BindPoint"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScenePositionKey : MonoBehaviour
    {
        public const string CellPrefix = "szene:";

        [SerializeField] private string key = "";
        [SerializeField] private int version;

        /// <summary>Schlüssel der geladenen Szene (null = keiner).</summary>
        public static ScenePositionKey Current { get; private set; }

        public string Key => key;
        public int Version => version;
        public string CellId => CellPrefix + key;

        public void Configure(string sceneKey, int worldVersion)
        {
            key = sceneKey;
            version = worldVersion;
        }

        /// <summary>Position zum Speichern.</summary>
        public WorldPosition Capture(Vector3 position, float yaw) =>
            new WorldPosition { regionId = key, cellId = CellId, localPosition = position, yaw = yaw, worldVersion = version };

        /// <summary>Gilt die gespeicherte Position in dieser Szene (gleiche Zelle, gleiche Weltversion)?</summary>
        public bool Accepts(WorldPosition wp) => wp.IsSet && wp.cellId == CellId && wp.worldVersion == version;

        private void OnEnable() => Current = this;

        private void OnDisable()
        {
            if (Current == this) Current = null;
        }
    }
}
