using Ashveil.Gameplay.Quests;
using UnityEngine;

namespace Ashveil.Gameplay.Story
{
    /// <summary>
    /// Die Hofherde, wie DIESER Spieler sie sieht (lokal, nicht vernetzt – jeder sieht seinen eigenen Stand):
    ///   Alltag: Herde auf der Koppel · Krise/Rueckkehr: leer (die Begegnung zeigt die Herde) · nach Q1: mit Fohlen bei der
    ///   Stute · Weidegang: leer (Begegnung) · danach: auf der grossen Weide ohne ihn · Epilog: zwei alte Tiere.
    /// </summary>
    public sealed class FarmHerdLocal : MonoBehaviour
    {
        [SerializeField] private Transform pasture;
        [SerializeField] private Transform bigPasture;

        private HerdPresenter _presenter;
        private FarmPhase _shown = (FarmPhase)255;
        private int _rev = -1;

        public void Bind(Transform paddock, Transform open)
        {
            pasture = paddock;
            bigPasture = open;
        }

        public HerdPresenter Presenter => _presenter;

        private void Update()
        {
            if (_rev == StoryClient.Revision && _presenter != null) { Follow(); return; }
            _rev = StoryClient.Revision;
            FarmPhase phase = StoryClient.Ready ? StoryClient.Farm : FarmPhase.Normal;
            if (phase == _shown) return;
            _shown = phase;
            if (_presenter != null) Destroy(_presenter.gameObject);
            _presenter = null;
            int adults;
            bool story = false, foal = false;
            Transform place = pasture;
            switch (phase)
            {
                case FarmPhase.Normal: adults = 6; break;
                case FarmPhase.Growing: adults = 6; story = true; foal = true; break;
                case FarmPhase.After: adults = 5; place = bigPasture != null ? bigPasture : pasture; break;
                case FarmPhase.Epilogue: adults = 2; break;
                default: return; // Krise, Rueckkehr, Weidegang: die vernetzte Begegnung zeigt die Herde
            }
            if (place == null) return;
            var go = new GameObject("Hofherde (lokal)");
            go.transform.SetParent(transform, false);
            go.transform.position = place.position;
            _presenter = go.AddComponent<HerdPresenter>();
            _presenter.Center = place.position;
            _presenter.Radius = phase == FarmPhase.After ? 26f : 11f;
            _presenter.Mode = HerdMode.Graze;
            _presenter.Build(adults, story, foal, 4711);
        }

        private void Follow()
        {
            _presenter.LookAt = LocalPlayerRef.Transform;
        }
    }
}
