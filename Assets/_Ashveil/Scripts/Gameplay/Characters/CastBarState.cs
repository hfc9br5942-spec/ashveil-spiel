using UnityEngine;

namespace Ashveil.Gameplay.Characters
{
    /// <summary>
    /// Zauberleiste der eigenen Figur (Aufsitzen, spaeter Zauber/Bergen). Nur Anzeige: Beginn und Abbruch kommen vom Server.
    /// </summary>
    public static class CastBarState
    {
        public static string Label { get; private set; } = "";
        public static float Duration { get; private set; }
        private static float _start = -1f;
        public static bool Interrupted { get; private set; }
        private static float _interruptedAt = -1f;

        public static bool Active => _start >= 0f && Time.time - _start < Duration;
        public static float Progress => _start < 0f ? 0f : Mathf.Clamp01((Time.time - _start) / Mathf.Max(0.01f, Duration));
        /// <summary>„Unterbrochen“ bleibt kurz stehen.</summary>
        public static bool ShowInterrupted => Interrupted && Time.time - _interruptedAt < 0.8f;

        public static void Begin(string label, float seconds)
        {
            Label = label ?? "";
            Duration = Mathf.Max(0.05f, seconds);
            _start = Time.time;
            Interrupted = false;
        }

        public static void End(bool interrupted)
        {
            if (_start < 0f) return;
            _start = -1f;
            Interrupted = interrupted;
            _interruptedAt = Time.time;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _start = -1f;
            Interrupted = false;
            Label = "";
        }
    }
}
