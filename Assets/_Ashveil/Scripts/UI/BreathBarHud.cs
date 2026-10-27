using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Story;
using UnityEngine;

namespace Ashveil.UI
{
    /// <summary>
    /// Atemleiste (MOV-169, Welle 0): erscheint oben in der Mitte (über Fehlermeldung und Gruppeneinladung), sobald die eigene Figur unter Wasser Atem verbraucht,
    /// und verschwindet, wenn der Atem wieder voll ist. Unter 20 % wird sie rot (Atemnot), bei 0 steht „Ertrinken“.
    /// Liest nur den Motor der eigenen Figur – den Schaden verteilt der Server.
    /// </summary>
    public sealed class BreathBarHud : MonoBehaviour
    {
        private UiKit.Bar _bar;
        private CharacterMotor _motor;
        private Transform _for;

        public static BreathBarHud Ensure(Transform host)
        {
            var existing = host.GetComponentInChildren<BreathBarHud>(true);
            if (existing != null) return existing;
            var go = new GameObject("Atemleiste");
            go.transform.SetParent(host, false);
            var hud = go.AddComponent<BreathBarHud>();
            Canvas canvas = UiKit.CreateCanvas("Atemleiste Canvas", go.transform, 13);
            hud._bar = UiKit.CreateBar("Atem", canvas.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(260f, 16f),
                new Color(0.35f, 0.65f, 0.95f), 12f);
            hud._bar.Root.gameObject.SetActive(false);
            return hud;
        }

        private void LateUpdate()
        {
            if (_bar == null) return;
            Transform player = LocalPlayerRef.Transform;
            if (player != _for)
            {
                _for = player;
                _motor = player != null ? player.GetComponentInParent<CharacterMotor>() : null;
            }
            float breath = _motor != null ? _motor.Breath01 : 1f;
            bool show = _motor != null && (breath < 0.999f || _motor.IsUnderwater);
            if (_bar.Root.gameObject.activeSelf != show) _bar.Root.gameObject.SetActive(show);
            if (!show) return;
            _bar.Set(breath);
            _bar.FillImage.color = breath < 0.2f ? new Color(0.85f, 0.25f, 0.2f) : new Color(0.35f, 0.65f, 0.95f);
            _bar.Label.text = breath <= 0f ? "Ertrinken" : "Atem";
        }
    }
}
