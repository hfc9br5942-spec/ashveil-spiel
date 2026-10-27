using System;
using UnityEngine;

namespace Ashveil.UI
{
    /// <summary>
    /// Uebergabe beim Ziehen zwischen Fenstern (Rucksack/Faehigkeitenbuch -&gt; Actionbar). Das Quellfenster zieht
    /// selbst und meldet beim Loslassen ausserhalb seiner Plaetze, was es gezogen hat; wer darunter liegt, nimmt es an.
    /// </summary>
    public static class UiDrag
    {
        public enum Kind { None, Item, Ability, Emote, Mount, Spell }

        /// <summary>Losgelassen: Art, ID, Bildschirmposition.</summary>
        public static event Action<Kind, string, Vector2> Dropped;

        public static void Drop(Kind kind, string id, Vector2 screen)
        {
            if (kind == Kind.None || string.IsNullOrEmpty(id)) return;
            Dropped?.Invoke(kind, id, screen);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Dropped = null;
    }
}
