using Ashveil.Gameplay.Quests;
using UnityEngine;

namespace Ashveil.Gameplay.Story
{
    /// <summary>
    /// P3 Glockenrast: statische, nicht vernetzte Darstellung, die je nach eigener Geschichtsmarke sichtbar ist (Phasing):
    /// z. B. das Schloss am Glockengalgen, sobald Runa den Schluessel haelt, oder Jasko am Ende des Holzfaellerlagers.
    /// Schaltet nur die Ziele (Darstellung, Namensschild) – nie etwas, woran der Server haengt (NpcIdentity, Anker).
    /// </summary>
    public sealed class StoryFlagProp : MonoBehaviour
    {
        [SerializeField] private string flag;
        [Tooltip("true = sichtbar, solange die Marke NICHT gesetzt ist")]
        [SerializeField] private bool invert;
        [SerializeField] private GameObject[] targets = new GameObject[0];

        private int _rev = -1;

        public void Configure(string storyFlag, bool hideWhenSet, params GameObject[] content)
        {
            flag = storyFlag;
            invert = hideWhenSet;
            targets = content ?? new GameObject[0];
        }

        /// <summary>Ohne Geschichtsstand (Server ohne Spieler, Vorschau): Ausgangszustand = Marke nicht gesetzt.</summary>
        public bool VisibleNow => (StoryClient.Ready && StoryClient.HasFlag(flag)) != invert;

        private void Start() => Apply(true);

        private void Update()
        {
            if (_rev == StoryClient.Revision) return;
            Apply(false);
        }

        private void Apply(bool force)
        {
            _rev = StoryClient.Revision;
            bool on = VisibleNow;
            foreach (GameObject t in targets)
                if (t != null && (force || t.activeSelf != on)) t.SetActive(on);
        }
    }
}
