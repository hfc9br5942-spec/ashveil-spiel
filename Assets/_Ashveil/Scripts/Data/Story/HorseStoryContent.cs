using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>
    /// Vorlagen der Pferde-Geschichte (vom Editor-Bau gefuellt, liegt unter Resources/Story, damit der Server sie laden kann):
    /// Herden-Objekt, Quest-Raubtier und die bestehenden Gegner fuer die drei Bedrohungen im Weidegang.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Geschichte/Pferde-Geschichte Inhalte", fileName = "HorseStoryContent")]
    public sealed class HorseStoryContent : ScriptableObject
    {
        public const string ResourcePath = "Story/HorseStoryContent";

        [Tooltip("Netz-Objekt der Herde (HerdEncounter)")]
        public GameObject herdPrefab;
        [Tooltip("Q1: Maehnenreisser (Quest-Variante des Aschwolfs)")]
        public GameObject predatorPrefab;
        [Tooltip("Q2: Aschwolf-Rudel am Waldrand")]
        public GameObject wolfPrefab;
        [Tooltip("Q2: Nadelspeier-Nest am Bach")]
        public GameObject spitterPrefab;
        [Tooltip("Q2: staerkerer Einzelgaenger am Hang")]
        public GameObject lonePrefab;

        private static HorseStoryContent _cached;
        public static HorseStoryContent Load() => _cached != null ? _cached : (_cached = Resources.Load<HorseStoryContent>(ResourcePath));
    }
}
