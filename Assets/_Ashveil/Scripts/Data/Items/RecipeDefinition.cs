using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>
    /// Herstellungsrezept. Zutaten und Ergebnis sind ganz normale Items – Materialien, Rezepte und hergestellte
    /// Ausruestung teilen ein System.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Recipe", fileName = "Recipe_New")]
    public sealed class RecipeDefinition : GameDefinition
    {
        public ProfessionDefinition profession;
        [Min(0)] public int requiredSkill = 1;
        public List<ItemAmount> reagents = new List<ItemAmount>();
        public ItemDefinition output;
        [Min(1)] public int outputCount = 1;

        protected override string IdPrefix => "recipe";
    }
}
