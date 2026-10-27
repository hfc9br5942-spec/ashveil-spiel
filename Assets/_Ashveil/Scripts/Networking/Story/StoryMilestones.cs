using System.Collections.Generic;
using Ashveil.Data;

namespace Ashveil.Networking
{
    /// <summary>
    /// Geschichtsmarken aus abgeschlossenen Auftraegen (QuestDefinition.grantsFlags). Beim Abgeben gesetzt – und beim Laden
    /// nachgezogen, damit auch alte Speicherstaende (vor v8) ihre Meilensteine haben.
    /// </summary>
    public static class StoryMilestones
    {
        public static void Derive(CharacterSaveData d)
        {
            if (d == null || d.completedQuests == null) return;
            if (d.storyFlags == null) d.storyFlags = new List<string>();
            foreach (string id in d.completedQuests)
            {
                QuestDefinition q = GameData.Quest(id);
                if (q == null || q.grantsFlags == null) continue;
                foreach (string f in q.grantsFlags)
                    if (!string.IsNullOrEmpty(f) && !d.storyFlags.Contains(f)) d.storyFlags.Add(f);
            }
        }

        /// <summary>Marke setzen; true = neu.</summary>
        public static bool Grant(CharacterSaveData d, string flag)
        {
            if (d == null || string.IsNullOrEmpty(flag)) return false;
            if (d.storyFlags == null) d.storyFlags = new List<string>();
            if (d.storyFlags.Contains(flag)) return false;
            d.storyFlags.Add(flag);
            return true;
        }
    }
}
