using UnityEngine;

namespace Ashveil.Gameplay.Combat
{
    /// <summary>
    /// Kurzes "Einfrieren" bei wuchtigen Treffern (typisches Action-Spielgefuehl).
    ///
    /// Achtung Multiplayer: Das verlangsamt nur die lokale Darstellung. Sobald der Netzwerk-Tick
    /// die Simulation uebernimmt (Milestone 3), betrifft es nur noch Animation und Effekte.
    /// </summary>
    public static class HitStop
    {
        private static HitStopRunner _runner;

        /// <summary>Friert fuer 'duration' Sekunden (Echtzeit) fast ein. Laengere Anfragen gewinnen.</summary>
        public static void Request(float duration)
        {
            if (duration <= 0f) return;
            if (_runner == null)
            {
                var go = new GameObject("HitStop") { hideFlags = HideFlags.HideInHierarchy };
                Object.DontDestroyOnLoad(go);
                _runner = go.AddComponent<HitStopRunner>();
            }
            _runner.Begin(duration);
        }
    }
}
