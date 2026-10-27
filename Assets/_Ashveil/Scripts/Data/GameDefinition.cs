using System.Text;
using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>
    /// Basisklasse fuer alle Inhaltsdefinitionen (Items, Waffen, Gegner, Quests ...).
    ///
    /// Jede Definition hat eine stabile, lesbare ID (z. B. "item.wolf_pelt").
    /// Netzwerk, Speicherstaende und Datenbank referenzieren ausschliesslich diese ID,
    /// niemals das Asset selbst. Dadurch koennen Assets umbenannt oder verschoben werden,
    /// ohne Speicherstaende zu zerstoeren.
    ///
    /// ID-Regel: einmal vergeben, nie wieder aendern.
    /// </summary>
    public abstract class GameDefinition : ScriptableObject
    {
        [Tooltip("Stabile ID, z. B. item.wolf_pelt. Nach Veroeffentlichung NIE aendern.")]
        [SerializeField] private string id;

        [SerializeField] private string displayName;

        [TextArea(2, 5)]
        [SerializeField] private string description;

        public string Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public string Description => description;

        /// <summary>Praefix der ID je Definitionstyp, z. B. "item", "weapon", "enemy".</summary>
        protected abstract string IdPrefix { get; }

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(id))
                id = IdPrefix + "." + ToSnakeCase(name);
        }
#endif

        /// <summary>"Wolf Pelt" / "WolfPelt" -> "wolf_pelt"</summary>
        public static string ToSnakeCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return "unnamed";
            var sb = new StringBuilder(input.Length + 8);
            char prev = '\0';
            foreach (char c in input)
            {
                if (char.IsLetterOrDigit(c))
                {
                    if (char.IsUpper(c) && sb.Length > 0 && prev != '_' && char.IsLower(prev)) sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                    prev = c;
                }
                else if (sb.Length > 0 && prev != '_')
                {
                    sb.Append('_');
                    prev = '_';
                }
            }
            return sb.ToString().Trim('_');
        }
    }
}
