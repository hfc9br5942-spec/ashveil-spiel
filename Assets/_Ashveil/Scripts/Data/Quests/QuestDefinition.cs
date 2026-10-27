using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Data
{
    public enum QuestObjectiveType
    {
        /// <summary>Gegner einer Art besiegen (targetId = enemy.*), zaehlt fuer jeden Beteiligten.</summary>
        Kill,
        /// <summary>Gegenstaende im Rucksack haben (targetId = item.*) – wird bei Abgabe entfernt.</summary>
        Collect,
        /// <summary>Mit einem NPC sprechen (targetId = npc.*).</summary>
        TalkTo,
        /// <summary>Einen Ort erreichen (targetId = area.*).</summary>
        Explore,
        /// <summary>
        /// Geschichtsschritt (targetId = story.*): der Server setzt den Fortschritt aus seiner Geschichts-Logik
        /// (Fohlen befreit, Herde heimgebracht, Weidetor geoeffnet …). Wird nie durch Toeten/Sammeln/Orte erfuellt.
        /// </summary>
        Story
    }

    [Serializable]
    public sealed class QuestObjective
    {
        public QuestObjectiveType type;
        [Tooltip("enemy.* / item.* / npc.* / area.* / story.*")]
        public string targetId;
        [Min(1)] public int count = 1;
        [Tooltip("Anzeige im Questlog, z. B. \"Aschwolfpelze\"")]
        public string label;
        [Tooltip("Optional: Texttabellen-Schluessel statt label (Loc)")]
        public string labelKey;

        public string Label => !string.IsNullOrEmpty(labelKey) ? Loc.T(labelKey) : label;
    }

    [Serializable]
    public sealed class QuestItemReward
    {
        public ItemDefinition item;
        [Min(1)] public int count = 1;
    }

    /// <summary>
    /// Auftrag: Geber, Abgabe-NPC, Voraussetzungen, Ziele und Belohnung. Fortschritt haelt der Server
    /// (NetworkProgression); Ziele reagieren auf Ereignisse (Gegner besiegt, Ort erreicht, Gespraech),
    /// Sammelziele zaehlen direkt den Rucksack.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Quest", fileName = "Quest_")]
    public sealed class QuestDefinition : GameDefinition
    {
        protected override string IdPrefix => "quest";

        [Header("Wer")]
        [Tooltip("npc.* – vergibt den Auftrag")]
        public string giverNpc;
        [Tooltip("npc.* – nimmt ihn ab (leer = Geber)")]
        public string turnInNpc;

        [Header("Voraussetzungen")]
        [Min(1)] public int minLevel = 1;
        public List<QuestDefinition> prerequisites = new List<QuestDefinition>();
        [Tooltip("race.* – nur dieses Volk (leer = alle)")]
        public string requiredRace;
        [Tooltip("Nur Klassen mit dieser Rolle (None = alle)")]
        public ClassRole requiredRole = ClassRole.None;
        [Tooltip("class.* – nur diese Klasse (leer = alle)")]
        public string requiredClass;
        [Tooltip("Geschichtsmarken, die gesetzt sein muessen (Meilensteine)")]
        public List<string> requiredFlags = new List<string>();
        [Tooltip("Wird nur ueber eine Geschichts-Szene angeboten (nicht in der normalen Angebotsliste des Gebers)")]
        public bool offeredByStory;
        [Tooltip("Unmarkiert (Quest Design Bible §4, Stufe C): kein „!“ ueber dem Geber – man erfaehrt davon nur im Gespraech. " +
                 "Im Gespraech steht der Auftrag wie jeder andere in der Liste.")]
        public bool unmarked;
        [Tooltip("Nur in Szenen, deren Szenenregeln (SceneQuestFilter) dieses Kennzeichen tragen, z. B. kareth_v4 – leer = ueberall. " +
                 "Auftraege einer Region mit eigenen Orten werden so in anderen Szenen (Sandbox) nicht vergeben.")]
        public string requiredSceneTag;

        [Header("Geschichtsmarken")]
        [Tooltip("Beim Abgeben gesetzt (Meilensteine fuer spaetere Inhalte)")]
        public List<string> grantsFlags = new List<string>();

        [Header("Texttabelle (optional)")]
        [Tooltip("Praefix fuer Name/Texte aus der Texttabelle, z. B. quest.human_tank.foal_rescue → .name .offer .progress .complete .summary")]
        public string textKey;

        [Header("Texte")]
        [TextArea(3, 8)] public string offerText;
        [TextArea(2, 5)] public string progressText;
        [TextArea(2, 6)] public string completeText;
        [Tooltip("Kurzform fuer die Auftragsverfolgung")]
        public string summary;

        [Header("Ziele")]
        public List<QuestObjective> objectives = new List<QuestObjective>();
        [Tooltip("Wird beim Annehmen gegeben (z. B. ein Brief), bei Abgabe/Abbruch entfernt")]
        public List<QuestItemReward> startItems = new List<QuestItemReward>();

        [Header("Belohnung")]
        [Min(0)] public int xp = 100;
        [Min(0)] public int money;
        [Tooltip("Bekommt man immer (nur was die eigene Klasse benutzen kann, wenn es Ausruestung ist)")]
        public List<QuestItemReward> rewardItems = new List<QuestItemReward>();
        [Tooltip("Eins davon waehlen (auf die eigene Klasse gefiltert)")]
        public List<QuestItemReward> choiceItems = new List<QuestItemReward>();

        public string TurnInNpc => string.IsNullOrEmpty(turnInNpc) ? giverNpc : turnInNpc;

        /// <summary>Hat der Auftrag Zugangsbedingungen jenseits von Stufe und Vorgaengern?</summary>
        public bool HasGate => !string.IsNullOrEmpty(requiredRace) || requiredRole != ClassRole.None || !string.IsNullOrEmpty(requiredClass) ||
                               (requiredFlags != null && requiredFlags.Count > 0);

        private bool Keyed(string suffix) => !string.IsNullOrEmpty(textKey) && Loc.Has(textKey + suffix);

        public string Title => Keyed(".name") ? Loc.T(textKey + ".name") : DisplayName;
        public string OfferText => Keyed(".offer") ? Loc.T(textKey + ".offer") : offerText;
        public string ProgressText => Keyed(".progress") ? Loc.T(textKey + ".progress") : progressText;
        public string CompleteText => Keyed(".complete") ? Loc.T(textKey + ".complete") : completeText;
        public string Summary => Keyed(".summary") ? Loc.T(textKey + ".summary") : summary;
    }
}
