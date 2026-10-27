using UnityEngine;

namespace Ashveil.Gameplay.Quests
{
    /// <summary>
    /// Aufträge, die in dieser Szene nicht vergeben werden (Kareth v4, Welle 0: „Salz und Schweigen“ schickt zu den Salzgruben,
    /// die erst mit der Region Salzsee in Welle 1 kommen – Kareth-Pass §6, EP-Rechnung „ohne Q6“). Der Geber bietet sie hier
    /// nicht an und zeigt kein „!“; Server und Client fragen dieselbe Regel (<see cref="QuestRules.CanAccept"/>). Laufende und
    /// erledigte Aufträge bleiben unberührt. Ohne Filter (Sandbox) gilt alles wie bisher. Gilt auch im Editor bei offener Szene
    /// (ExecuteAlways), damit Werkzeuge und Tests dieselbe Regel sehen.
    /// #28: Kennzeichen der Szene (<see cref="tags"/>, z. B. „kareth_v4“). Aufträge mit <c>requiredSceneTag</c> gibt es nur in
    /// Szenen mit diesem Kennzeichen – ihre Orte und Figuren stehen nur dort (in der Sandbox fehlen sie).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class SceneQuestFilter : MonoBehaviour
    {
        [SerializeField] private string[] hiddenQuests = new string[0];
        [SerializeField] private string reason = "Nicht in dieser Gegend.";
        [Tooltip("Kennzeichen dieser Szene (z. B. kareth_v4) für Aufträge mit requiredSceneTag")]
        [SerializeField] private string[] tags = new string[0];

        public const string ElsewhereReason = "Nicht in dieser Gegend.";

        private static SceneQuestFilter _current;

        public void Configure(string why, params string[] questIds)
        {
            reason = why;
            hiddenQuests = questIds ?? new string[0];
        }

        public void SetTags(params string[] sceneTags) => tags = sceneTags ?? new string[0];

        /// <summary>Trägt die geladene Szene dieses Kennzeichen?</summary>
        public static bool HasTag(string tag)
        {
            if (_current == null || string.IsNullOrEmpty(tag) || _current.tags == null) return false;
            foreach (string t in _current.tags)
                if (t == tag) return true;
            return false;
        }

        /// <summary>Grund, wenn der Auftrag in der geladenen Szene nicht vergeben wird (Szenenkennzeichen oder Liste), sonst null.</summary>
        public static string Hidden(Ashveil.Data.QuestDefinition q)
        {
            if (q == null) return null;
            if (!string.IsNullOrEmpty(q.requiredSceneTag) && !HasTag(q.requiredSceneTag)) return ElsewhereReason;
            return Hidden(q.Id);
        }

        /// <summary>Grund, wenn der Auftrag in der geladenen Szene nicht vergeben wird, sonst null.</summary>
        public static string Hidden(string questId)
        {
            if (_current == null || string.IsNullOrEmpty(questId)) return null;
            foreach (string q in _current.hiddenQuests)
                if (q == questId) return _current.reason;
            return null;
        }

        private void OnEnable() => _current = this;

        private void OnDisable()
        {
            if (_current == this) _current = null;
        }
    }
}
