using UnityEngine;

namespace Ashveil.EditorTools.TerrainGen
{
    /// <summary>Verknuepfung eines Modifiers mit der vorhandenen Hoehe (Vorgabe §77).</summary>
    public enum ModifierBlend
    {
        Add,
        Subtract,
        Max,
        Min,
        LerpToTarget,
        FlattenTowards
    }

    /// <summary>
    /// Ein Schritt der Pipeline (Vorgabe §3, §76). Prioritaet bestimmt die Reihenfolge:
    /// 0 Basis · 100 Gebirge · 200 Taeler/Paesse/Plateaus · 300 Kerbe · 400 Fluesse · 500 Wege · 600 Landmarken ·
    /// 700 Detail · 800 Erosion · 900 Glaetten · 1000 Schutz/Uebergang.
    /// </summary>
    public abstract class TerrainModifier
    {
        public abstract string Name { get; }
        public abstract int Priority { get; }
        public virtual ModifierBlend Blend => ModifierBlend.Add;

        public abstract void Apply(TerrainGenerationContext ctx);

        /// <summary>value = Beitrag des Modifiers, weight = 0…1 (Maske).</summary>
        public static float Combine(float current, float value, float weight, ModifierBlend mode)
        {
            if (weight <= 0f) return current;
            switch (mode)
            {
                case ModifierBlend.Add: return current + value * weight;
                case ModifierBlend.Subtract: return current - value * weight;
                case ModifierBlend.Max: return Mathf.Lerp(current, Mathf.Max(current, value), weight);
                case ModifierBlend.Min: return Mathf.Lerp(current, Mathf.Min(current, value), weight);
                case ModifierBlend.LerpToTarget: return Mathf.Lerp(current, value, weight);
                case ModifierBlend.FlattenTowards:
                    // nur bis zur Haelfte des Unterschieds je Durchlauf – weiche Verflachung
                    return Mathf.Lerp(current, value, weight * 0.5f);
                default: return current;
            }
        }

        protected static float S(float a, float b, float x) => (float)Ashveil.World.AshNoise.SmoothStep(a, b, x);
    }
}
