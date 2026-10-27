using System;
using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Items;

namespace Ashveil.Gameplay.Quests
{
    /// <summary>Angenommener Auftrag, wie der Client ihn sieht (Stand vom Server).</summary>
    public sealed class QuestView
    {
        public QuestDefinition Def;
        public int[] Progress = Array.Empty<int>();

        public bool Complete
        {
            get
            {
                if (Def == null) return false;
                for (int i = 0; i < Def.objectives.Count; i++)
                    if (i >= Progress.Length || Progress[i] < Def.objectives[i].count) return false;
                return true;
            }
        }
    }

    /// <summary>Zurueckkaufbarer, verkaufter Gegenstand.</summary>
    public struct BuybackEntry
    {
        public ItemStack Stack;
        public int Price;
    }

    /// <summary>
    /// Was die Oberflaeche ueber Erfahrung, Geld, Auftraege und Handel wissen und ausloesen darf.
    /// Jede Aktion ist nur eine Bitte an den Server.
    /// </summary>
    public interface IProgressionService
    {
        bool Ready { get; }
        int Level { get; }
        int Xp { get; }
        int XpNeeded { get; }
        long Money { get; }
        IReadOnlyList<QuestView> Quests { get; }
        bool IsCompleted(string questId);
        bool IsActive(string questId);

        /// <summary>Gespraech, das der Server bestaetigt hat (null = keins).</summary>
        NpcDefinition DialogNpc { get; }
        IReadOnlyList<BuybackEntry> Buyback { get; }

        void Talk(string npcId);
        void CloseDialog();
        void Accept(string questId);
        void TurnIn(string questId, int choice);
        void Abandon(string questId);
        void Buy(int entryIndex, int times);
        void Sell(int bagIndex);
        void BuyBack(int index);
    }

    public static class ProgressionClient
    {
        public static IProgressionService Current { get; private set; }

        /// <summary>Ausgeruht: Zusatz-EP, die die naechsten Siege verdoppeln (Stand vom Server).</summary>
        public static int RestedXp { get; private set; }
        /// <summary>Steht der eigene Spieler in einem Gasthaus (Ruheort)?</summary>
        public static bool InRestArea { get; private set; }

        public static void SetRested(int rested, bool inRestArea)
        {
            if (RestedXp == rested && InRestArea == inRestArea) return;
            RestedXp = rested;
            InRestArea = inRestArea;
            Changed?.Invoke();
        }

        /// <summary>Stand geaendert (EP, Geld, Auftraege, Dialog).</summary>
        public static event Action Changed;
        /// <summary>Stufenaufstieg des eigenen Spielers (neue Stufe).</summary>
        public static event Action<int> LeveledUp;
        /// <summary>Auftrag angenommen / abgeschlossen (Titel, true = abgeschlossen).</summary>
        public static event Action<string, bool> QuestEvent;
        /// <summary>Erfahrung erhalten (Menge, Weltpunkt fuer schwebenden Text).</summary>
        public static event Action<int, UnityEngine.Vector3> XpGained;
        /// <summary>Kurze Meldung (z. B. "Aussichtspunkt erkundet.").</summary>
        public static event Action<string> Message;

        public static void Register(IProgressionService s)
        {
            Current = s;
            Changed?.Invoke();
        }

        public static void Unregister(IProgressionService s)
        {
            if (Current != s) return;
            Current = null;
            Changed?.Invoke();
        }

        public static void RaiseChanged() => Changed?.Invoke();
        public static void RaiseLevelUp(int level) => LeveledUp?.Invoke(level);
        public static void RaiseQuest(string title, bool completed) => QuestEvent?.Invoke(title, completed);
        public static void RaiseXp(int amount, UnityEngine.Vector3 where) => XpGained?.Invoke(amount, where);
        public static void RaiseMessage(string text) => Message?.Invoke(text);
    }
}
