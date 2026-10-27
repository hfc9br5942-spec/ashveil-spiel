using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Gameplay.Characters
{
    /// <summary>
    /// Laeuft gerade eine Aktion im Animator (Angriff, Treffer, Nachladen, Rolle, Luftschlag, Tod)? Die prozeduralen
    /// Schichten lassen der Aktion dann den Koerper – auch bei fremden Spielern, deren Waffen-/Bewegungszustand nur
    /// dargestellt wird (dort meldet die Waffe keinen Angriff, der Animator aber schon).
    /// </summary>
    public static class AnimatorActions
    {
        private static readonly int Empty = Animator.StringToHash("Empty");
        private static readonly int Death = Animator.StringToHash("Death");
        private static readonly Dictionary<RuntimeAnimatorController, (int upper, int full)> Layers =
            new Dictionary<RuntimeAnimatorController, (int, int)>();

        // Selbstgebaute Bewegungen (Kampf-Choreografie): sie melden sich hier genauso wie Animator-Aktionen
        private static readonly Dictionary<Animator, (bool upper, bool full)> Procedural = new Dictionary<Animator, (bool, bool)>();

        /// <summary>Eine prozedurale Aktion laeuft (upper = Oberkoerper, full = ganzer Koerper) bzw. ist vorbei.</summary>
        public static void SetProcedural(Animator a, bool upper, bool full)
        {
            if (a == null) return;
            if (!upper && !full) Procedural.Remove(a);
            else Procedural[a] = (upper, full);
        }

        public static bool ProceduralUpper(Animator a) => a != null && Procedural.TryGetValue(a, out var p) && p.upper;
        public static bool ProceduralFull(Animator a) => a != null && Procedural.TryGetValue(a, out var p) && p.full;

        /// <summary>Oberkoerper-Aktion (Schlag, Schuss, Nachladen, Treffer).</summary>
        public static bool UpperBody(Animator a) => a != null && (ProceduralUpper(a) || Busy(a, Indices(a).upper));

        /// <summary>Ganzkoerper-Aktion (Rolle, Luftschlag, Sturzangriff) oder Tod.</summary>
        public static bool FullBody(Animator a)
        {
            if (a == null) return false;
            if (ProceduralFull(a)) return true;
            if (!a.isActiveAndEnabled || a.runtimeAnimatorController == null) return false;
            if (Busy(a, Indices(a).full)) return true;
            AnimatorStateInfo s = a.GetCurrentAnimatorStateInfo(0);
            if (s.shortNameHash == Death) return true;
            return a.IsInTransition(0) && a.GetNextAnimatorStateInfo(0).shortNameHash == Death;
        }

        private static (int upper, int full) Indices(Animator a)
        {
            RuntimeAnimatorController c = a.runtimeAnimatorController;
            if (c == null) return (-1, -1);
            if (!Layers.TryGetValue(c, out var l))
            {
                l = (a.GetLayerIndex("UpperBody"), a.GetLayerIndex("FullBody"));
                Layers[c] = l;
            }
            return l;
        }

        private static bool Busy(Animator a, int layer)
        {
            if (layer < 0 || !a.isActiveAndEnabled || layer >= a.layerCount) return false;
            if (a.GetLayerWeight(layer) < 0.01f) return false;
            if (a.GetCurrentAnimatorStateInfo(layer).shortNameHash != Empty) return true;
            return a.IsInTransition(layer) && a.GetNextAnimatorStateInfo(layer).shortNameHash != Empty;
        }
    }
}
