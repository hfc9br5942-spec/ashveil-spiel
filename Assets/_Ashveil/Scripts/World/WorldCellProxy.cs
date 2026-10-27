using Ashveil.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashveil.World
{
    /// <summary>
    /// Fernansicht einer Zelle in der Welt-Ebene (Masterplan §37: Horizont ohne geladene Zelle). Grobes Gelaende ohne
    /// Kollision; verschwindet, sobald die echte Zelle bereit ist, und kommt beim Entladen zurueck. Liegt 1,5 m tiefer als
    /// das echte Gelaende, damit sich beide beim Uebergang nicht ueberlagern.
    /// </summary>
    public sealed class WorldCellProxy : MonoBehaviour
    {
        [SerializeField] private string cellId = "";
        [SerializeField] private string sceneName = "";

        private Renderer[] _renderers;

        public string CellId => cellId;

        public void Configure(string cell, string scene)
        {
            cellId = cell;
            sceneName = scene;
        }

        private void Awake() => _renderers = GetComponentsInChildren<Renderer>(true);

        private void OnEnable()
        {
            WorldCells.CellReady += OnReady;
            WorldCells.CellUnloading += OnUnloading;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            Refresh();
        }

        private void OnDisable()
        {
            WorldCells.CellReady -= OnReady;
            WorldCells.CellUnloading -= OnUnloading;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
        }

        private void OnReady(string id)
        {
            if (id == cellId) Show(false);
        }

        private void OnUnloading(string id)
        {
            if (id == cellId) Show(true);
        }

        private void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            if (s.name == sceneName) Refresh();
        }

        private void OnSceneUnloaded(Scene s)
        {
            if (s.name == sceneName) Show(true);
        }

        /// <summary>Echte Zelle bereit (Streamer) oder ihre Szene schon offen (im Editor additiv geoeffnet)?</summary>
        private void Refresh()
        {
            bool loaded = WorldCells.IsReady(cellId);
            if (!loaded && !string.IsNullOrEmpty(sceneName))
            {
                Scene s = SceneManager.GetSceneByName(sceneName);
                loaded = s.IsValid() && s.isLoaded;
            }
            Show(!loaded);
        }

        private void Show(bool on)
        {
            if (_renderers == null) _renderers = GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in _renderers)
                if (r != null) r.enabled = on;
        }
    }
}
