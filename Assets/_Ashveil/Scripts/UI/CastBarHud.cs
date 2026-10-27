using Ashveil.Gameplay.Characters;
using TMPro;
using UnityEngine;

namespace Ashveil.UI
{
    /// <summary>Zauberleiste ueber der Actionbar (Aufsitzen, spaeter Zauber/Bergen). Liest <see cref="CastBarState"/>.</summary>
    public sealed class CastBarHud : MonoBehaviour
    {
        private UiKit.Bar _bar;

        public static CastBarHud Ensure(Transform host)
        {
            var existing = host.GetComponentInChildren<CastBarHud>(true);
            if (existing != null) return existing;
            var go = new GameObject("Zauberleiste");
            go.transform.SetParent(host, false);
            var hud = go.AddComponent<CastBarHud>();
            Canvas canvas = UiKit.CreateCanvas("Zauberleiste Canvas", go.transform, 13);
            hud._bar = UiKit.CreateBar("Zauber", canvas.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 160f), new Vector2(280f, 20f),
                new Color(0.9f, 0.7f, 0.3f), 13f);
            hud._bar.Root.gameObject.SetActive(false);
            return hud;
        }

        private void LateUpdate()
        {
            if (_bar == null) return;
            bool active = CastBarState.Active, interrupted = CastBarState.ShowInterrupted;
            bool show = active || interrupted;
            if (_bar.Root.gameObject.activeSelf != show) _bar.Root.gameObject.SetActive(show);
            if (!show) return;
            if (active)
            {
                _bar.Set(CastBarState.Progress);
                _bar.FillImage.color = new Color(0.9f, 0.7f, 0.3f);
                _bar.Label.text = CastBarState.Label;
            }
            else
            {
                _bar.Set(1f);
                _bar.FillImage.color = new Color(0.75f, 0.2f, 0.15f);
                _bar.Label.text = "Unterbrochen";
            }
        }
    }
}
