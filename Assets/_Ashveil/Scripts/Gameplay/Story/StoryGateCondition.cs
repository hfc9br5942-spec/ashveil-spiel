using Ashveil.Data;
using Ashveil.Gameplay.Quests;
using UnityEngine;

namespace Ashveil.Gameplay.Story
{
    /// <summary>Weidetor-Handlung (Weidegang): nur, solange die Weide sicher ist und das Tor noch zu.</summary>
    public sealed class StoryGateCondition : MonoBehaviour
    {
        /// <summary>Schritt „Tor oeffnen“ (NetworkProgression Q2Gate).</summary>
        public const int GateStep = 5;

        private void Awake()
        {
            var si = GetComponent<StoryInteractable>();
            if (si != null) si.Condition = () => StoryClient.Ready && StoryClient.HorseState == HorseStoryState.AdultAtFarm && StoryClient.HorseStep == GateStep;
        }
    }
}
