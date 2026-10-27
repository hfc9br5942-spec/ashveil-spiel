using Ashveil.Data;
using Ashveil.Gameplay.Quests;
using UnityEngine;

namespace Ashveil.Gameplay.Story
{
    /// <summary>
    /// Statische, nicht vernetzte Requisite, die je nach eigenem Geschichtsstand sichtbar ist (Phasing, Vorgabe: Hinweise,
    /// Kadaver, Spuren, gebrochener Zaun, Grab). Jeder Spieler sieht seinen eigenen Stand – Kosten: nur ein Aktiv-Schalter.
    ///   phaseMask   : in welchen <see cref="FarmPhase"/>n sichtbar (Bit je Phase)
    ///   activeStep  : ≥ 0 = nur sichtbar, solange genau dieser Suchschritt laeuft (Suchgebiet-Markierung)
    /// </summary>
    public sealed class StoryPhasedProp : MonoBehaviour
    {
        [SerializeField] private int phaseMask = 1;
        [SerializeField] private int activeStep = -1;
        [SerializeField] private GameObject target;

        private int _rev = -1;

        public void Configure(int mask, int step = -1, GameObject content = null)
        {
            phaseMask = mask;
            activeStep = step;
            target = content;
        }

        public static int Mask(params FarmPhase[] phases)
        {
            int m = 0;
            foreach (FarmPhase p in phases) m |= 1 << (int)p;
            return m;
        }

        public bool VisibleNow => Evaluate();

        private bool Evaluate()
        {
            if (!StoryClient.Ready) return (phaseMask & 1) != 0 && activeStep < 0; // ohne Stand: Alltag
            if ((phaseMask & (1 << (int)StoryClient.Farm)) == 0) return false;
            if (activeStep >= 0 && (StoryClient.HorseState != HorseStoryState.FoalMissing || StoryClient.HorseStep != activeStep)) return false;
            return true;
        }

        private void Start() => Apply(true);

        private void Update()
        {
            if (_rev == StoryClient.Revision) return;
            Apply(false);
        }

        private void Apply(bool force)
        {
            _rev = StoryClient.Revision;
            GameObject t = target != null ? target : (transform.childCount > 0 ? transform.GetChild(0).gameObject : null);
            if (t == null) return;
            bool on = Evaluate();
            if (force || t.activeSelf != on) t.SetActive(on);
        }
    }
}
