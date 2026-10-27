using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>
    /// Beutetabelle eines Gegners. Wird pro berechtigtem Spieler einmal gewuerfelt.
    ///   entries : unabhaengige Wuerfe (z. B. Wolfspelz 70 %)
    ///   groups  : aus der Gruppe wird gewichtet gezogen (z. B. "Ausruestung": 1 aus 12)
    /// Gruppen mit classAware bevorzugen Gegenstaende, die der Empfaenger tragen kann
    /// (Anteil classBias) – Materialien, Rezepte und Fundstuecke fallen klassenuebergreifend.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Loot Table", fileName = "Loot_New")]
    public sealed class LootTableDefinition : GameDefinition
    {
        [Serializable]
        public struct Entry
        {
            public ItemDefinition item;
            [Range(0f, 1f)] public float chance;
            [Min(1)] public int minCount;
            [Min(1)] public int maxCount;
        }

        [Serializable]
        public struct Weighted
        {
            public ItemDefinition item;
            [Min(0f)] public float weight;
            [Min(1)] public int minCount;
            [Min(1)] public int maxCount;
        }

        [Serializable]
        public class Group
        {
            public string name = "Gruppe";
            [Range(0f, 1f)] public float chance = 0.25f;
            [Min(1)] public int picks = 1;
            [Tooltip("Ausruestung: ueberwiegend fuer die Klasse des Empfaengers")]
            public bool classAware;
            [Range(0f, 1f)] public float classBias = 0.85f;
            public List<Weighted> items = new List<Weighted>();
        }

        public List<Entry> entries = new List<Entry>();
        public List<Group> groups = new List<Group>();

        protected override string IdPrefix => "loot";
    }
}
