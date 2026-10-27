using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>Beruf (Sammeln oder Herstellen). Fertigkeit 1..maxSkill.</summary>
    [CreateAssetMenu(menuName = "Ashveil/Profession", fileName = "Profession_New")]
    public sealed class ProfessionDefinition : GameDefinition
    {
        public ProfessionKind kind = ProfessionKind.Crafting;
        [Min(1)] public int maxSkill = 300;

        protected override string IdPrefix => "profession";
    }
}
