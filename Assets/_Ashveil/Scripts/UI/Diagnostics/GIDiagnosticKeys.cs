using Ashveil.Gameplay.Controls;
using Ashveil.Rendering.GI;
using UnityEngine;

namespace Ashveil.UI.Diagnostics
{
    /// <summary>
    /// Entwickler-Tasten fuer das dynamische GI, auch im fertigen Spiel (Leistung pruefen ohne Editor):
    /// Strg+Umschalt+G Anzeige an/aus, Strg+Umschalt+H Kostendiagnose weiterschalten.
    /// </summary>
    public sealed class GIDiagnosticKeys : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("GI-Diagnosetasten");
            DontDestroyOnLoad(go);
            go.AddComponent<GIDiagnosticKeys>();
        }

        private void Update()
        {
            if (UiPointer.KeyboardCaptured || !RawInput.CtrlHeld || !RawInput.ShiftHeld) return;   // nicht beim Tippen
            AshveilDynamicGI gi = AshveilDynamicGI.Instance;
            if (gi == null) return;
            if (RawInput.WasPressed("Key:G")) gi.showHud = !gi.showHud;
            if (RawInput.WasPressed("Key:H")) { gi.costMode = (gi.costMode + 1) % 8; gi.showHud = true; }
        }
    }
}
