using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>
    /// Nachschlagetabelle ID -> Definition. Server und Client laden dieselbe Datenbank,
    /// damit eine uebers Netz empfangene ID ("weapon.wheellock_pistol") aufgeloest werden kann.
    ///
    /// Befuellung: Menue Ashveil > Daten > Definitionen pruefen & Datenbank aktualisieren.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Definition Database", fileName = "DefinitionDatabase")]
    public sealed class DefinitionDatabase : ScriptableObject
    {
        [SerializeField] private List<GameDefinition> definitions = new List<GameDefinition>();

        private Dictionary<string, GameDefinition> _lookup;

        public IReadOnlyList<GameDefinition> All => definitions;

        public bool TryGet<T>(string id, out T definition) where T : GameDefinition
        {
            EnsureLookup();
            if (id != null && _lookup.TryGetValue(id, out GameDefinition def) && def is T typed)
            {
                definition = typed;
                return true;
            }
            definition = null;
            return false;
        }

        private void EnsureLookup()
        {
            if (_lookup != null) return;
            _lookup = new Dictionary<string, GameDefinition>(definitions.Count);
            foreach (GameDefinition def in definitions)
            {
                if (def == null || string.IsNullOrEmpty(def.Id)) continue;
                if (_lookup.ContainsKey(def.Id))
                {
                    Debug.LogError($"[DefinitionDatabase] Doppelte ID '{def.Id}' ({def.name}).", def);
                    continue;
                }
                _lookup.Add(def.Id, def);
            }
        }

        private void OnEnable()
        {
            _lookup = null;
        }

#if UNITY_EDITOR
        /// <summary>Nur fuer Editor-Werkzeuge.</summary>
        public void EditorSetDefinitions(List<GameDefinition> defs)
        {
            definitions = defs;
            _lookup = null;
        }
#endif
    }
}
