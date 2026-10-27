using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Data
{
    [Serializable]
    public sealed class VendorEntry
    {
        public ItemDefinition item;
        [Tooltip("Menge pro Kauf")]
        [Min(1)] public int count = 1;
        [Tooltip("Preis in Kupfer fuer die ganze Menge (0 = vierfacher Verkaufswert)")]
        [Min(0)] public int price;
    }

    /// <summary>
    /// Nicht-Spieler-Figur: Name, Gruss, Handelsangebot. Die Figur in der Szene traegt
    /// <c>NpcIdentity</c> mit dieser ID; Auftraege verweisen per ID auf den NPC.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/NPC", fileName = "Npc_")]
    public sealed class NpcDefinition : GameDefinition
    {
        protected override string IdPrefix => "npc";

        public string title;
        [TextArea(2, 5)] public string greeting;
        [Tooltip("Hat ein Handelsangebot")]
        public bool vendor;
        public List<VendorEntry> stock = new List<VendorEntry>();
        [Tooltip("Klassenlehrer (P1): bildet diese Klasse aus (class.warden …); leer = kein Lehrer")]
        public string trainerClass;
        public bool IsTrainer => !string.IsNullOrEmpty(trainerClass);
        [Tooltip("Maximale Entfernung fuer Gespraech/Handel (m)")]
        [Min(1f)] public float interactRange = 5f;
        [Tooltip("Optional: Texttabellen-Praefix (z. B. npc.human.horsebreeder_01 → .name .title .greeting)")]
        public string textKey;

        [Tooltip("P3: Geschichtsmarken, die den Gruss aendern (spaetere zuerst geprueft). Gesetzt → Texttabelle textKey + \".greeting.\" + Marke")]
        public List<string> greetingFlags = new List<string>();

        private bool Keyed(string suffix) => !string.IsNullOrEmpty(textKey) && Loc.Has(textKey + suffix);
        public string Name => Keyed(".name") ? Loc.T(textKey + ".name") : DisplayName;
        public string Title => Keyed(".title") ? Loc.T(textKey + ".title") : title;
        public string Greeting => Keyed(".greeting") ? Loc.T(textKey + ".greeting") : greeting;

        /// <summary>Gruss je eigenem Geschichtsstand (Phasing): die letzte gesetzte Marke aus <see cref="greetingFlags"/> gewinnt.</summary>
        public string GreetingFor(Func<string, bool> hasFlag)
        {
            if (hasFlag != null && greetingFlags != null)
                for (int i = greetingFlags.Count - 1; i >= 0; i--)
                {
                    string f = greetingFlags[i];
                    if (string.IsNullOrEmpty(f) || !hasFlag(f)) continue;
                    if (Keyed(".greeting." + f)) return Loc.T(textKey + ".greeting." + f);
                }
            return Greeting;
        }
    }
}
