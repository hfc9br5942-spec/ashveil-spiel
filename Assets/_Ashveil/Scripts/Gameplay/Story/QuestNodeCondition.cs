using Ashveil.Data;
using Ashveil.Gameplay.Quests;
using UnityEngine;

namespace Ashveil.Gameplay.Story
{
    /// <summary>
    /// P3 Glockenrast: Bedingung einer Rechtsklick-Handlung, die an einem Auftrag haengt (Fundstueck aufheben, Scherbe
    /// anschlagen, nach Jasko sehen). Anklickbar nur, solange der eigene Auftrag laeuft und das Fundstueck fuer mich noch
    /// nicht genommen bzw. der Geschichtsschritt noch offen ist. Ein genommenes Fundstueck blendet nur seine Darstellung aus
    /// (<see cref="visual"/>) – Anker und Klickflaeche bleiben fuer den Server und die anderen Spieler bestehen (Phasing).
    /// Der Server prueft alles noch einmal selbst.
    /// </summary>
    [RequireComponent(typeof(StoryInteractable))]
    public sealed class QuestNodeCondition : MonoBehaviour
    {
        [SerializeField] private string questId;
        [Tooltip("story.* – Geschichtsschritt: nur solange offen (leer = keiner)")]
        [SerializeField] private string storyKey;
        [Tooltip("Geschichtsmarke „fuer mich genommen“ (leer = keine)")]
        [SerializeField] private string takenFlag;
        [Tooltip("Darstellung, die verschwindet, sobald das Fundstueck fuer mich genommen ist")]
        [SerializeField] private GameObject visual;

        private float _next;

        public void Configure(string quest, string story, string taken, GameObject content)
        {
            questId = quest;
            storyKey = story;
            takenFlag = taken;
            visual = content;
        }

        private void Awake()
        {
            var si = GetComponent<StoryInteractable>();
            if (si != null) si.Condition = Available;
        }

        public bool Taken => !string.IsNullOrEmpty(takenFlag) && StoryClient.HasFlag(takenFlag);

        public bool Available()
        {
            IProgressionService p = ProgressionClient.Current;
            if (p == null || !p.Ready || string.IsNullOrEmpty(questId) || !p.IsActive(questId)) return false;
            if (Taken) return false;
            if (string.IsNullOrEmpty(storyKey)) return true;
            foreach (QuestView v in p.Quests)
            {
                if (v.Def == null || v.Def.Id != questId) continue;
                for (int i = 0; i < v.Def.objectives.Count; i++)
                {
                    QuestObjective o = v.Def.objectives[i];
                    if (o.type == QuestObjectiveType.Story && o.targetId == storyKey)
                        return i >= v.Progress.Length || v.Progress[i] < o.count;
                }
            }
            return false;
        }

        private void Update()
        {
            if (visual == null || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            bool show = !Taken;
            if (visual.activeSelf != show) visual.SetActive(show);
        }
    }
}
