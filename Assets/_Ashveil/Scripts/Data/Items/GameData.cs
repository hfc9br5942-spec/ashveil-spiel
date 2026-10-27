using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>
    /// Zugriff auf alle Definitionen zur Laufzeit (Server und Client laden dieselbe Datenbank).
    /// Wird von <see cref="GameDataBootstrap"/> beim Szenenstart registriert.
    /// </summary>
    public static class GameData
    {
        private static DefinitionDatabase _db;

        public static DefinitionDatabase Database => _db;
        public static bool Ready => _db != null;

        public static void Register(DefinitionDatabase db)
        {
            if (db != null) _db = db;
            _quests = null;
        }

        private static System.Collections.Generic.List<QuestDefinition> _quests;

        /// <summary>Alle Auftraege (fuer Markierungen und Questlog).</summary>
        public static System.Collections.Generic.IReadOnlyList<QuestDefinition> Quests
        {
            get
            {
                if (_quests == null)
                {
                    _quests = new System.Collections.Generic.List<QuestDefinition>();
                    if (_db != null)
                        foreach (GameDefinition d in _db.All)
                            if (d is QuestDefinition q) _quests.Add(q);
                }
                return _quests;
            }
        }

        public static T Get<T>(string id) where T : GameDefinition
        {
            if (_db == null || string.IsNullOrEmpty(id)) return null;
            return _db.TryGet(id, out T def) ? def : null;
        }

        public static ItemDefinition Item(string id) => Get<ItemDefinition>(id);
        public static CharacterClassDefinition Class(string id) => Get<CharacterClassDefinition>(id);
        public static QuestDefinition Quest(string id) => Get<QuestDefinition>(id);
        public static NpcDefinition Npc(string id) => Get<NpcDefinition>(id);
        public static EnemyDefinition Enemy(string id) => Get<EnemyDefinition>(id);
    }
}
