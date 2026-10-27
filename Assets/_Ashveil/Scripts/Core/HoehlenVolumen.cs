using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Core
{
    /// <summary>
    /// Höhlen unter dem Gelände (Westweiden, Kreidekeller, 12.10.2026): Quader, in denen das Gelände über einem nicht der Boden ist.
    /// Sturzgrenze (<see cref="WorldGround.UnterDerWelt"/>) und Wiedereinstieg nach dem Anmelden richten sich darin nach der Höhle,
    /// nicht nach der Geländeoberfläche. Liegt in der Zellen-Szene, ist da, sobald die Zelle geladen ist (Server und Client).
    /// </summary>
    public sealed class HoehlenVolumen : MonoBehaviour
    {
        private static readonly List<HoehlenVolumen> Alle = new List<HoehlenVolumen>();

        [SerializeField] private Bounds bereich = new Bounds(Vector3.zero, Vector3.one);

        public Bounds Bereich => bereich;
        public static IReadOnlyList<HoehlenVolumen> Liste => Alle;

        public void Configure(Bounds weltBereich) => bereich = weltBereich;

        /// <summary>Liegt der Punkt in einer Höhle? (Weltkoordinaten)</summary>
        public static bool Enthaelt(Vector3 p, out Bounds b)
        {
            for (int i = 0; i < Alle.Count; i++)
            {
                HoehlenVolumen v = Alle[i];
                if (v != null && v.bereich.Contains(p)) { b = v.bereich; return true; }
            }
            b = default;
            return false;
        }

        public static bool Enthaelt(Vector3 p) => Enthaelt(p, out _);

        private void OnEnable() { if (!Alle.Contains(this)) Alle.Add(this); }
        private void OnDisable() => Alle.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Alle.Clear();

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.9f, 0.6f, 0.2f, 0.6f);
            Gizmos.DrawWireCube(bereich.center, bereich.size);
        }
    }
}
