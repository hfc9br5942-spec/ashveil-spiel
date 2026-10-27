using System;
using System.Collections.Generic;
using Ashveil.Data;

namespace Ashveil.Gameplay.Mastery
{
    /// <summary>
    /// Praxis-Meisterschaften, wie der eigene Client sie sieht (Stand vom Server, nur fuer den Besitzer – andere Spieler
    /// bekommen keine Meisterschaftswerte, §107/§108). Das Fenster „Erfahrung“ liest hier.
    /// </summary>
    public static class MasteryClient
    {
        private static readonly Dictionary<string, double> PointsById = new Dictionary<string, double>();

        public static bool Ready { get; private set; }
        public static double MetersOnFoot { get; private set; }
        public static int MeaningfulDodges { get; private set; }
        public static double DamageBlocked { get; private set; }
        public static int Revision { get; private set; }
        public static event Action Changed;
        /// <summary>Meilenstein erreicht (alle 10 Raenge): Kopfzeile oben und Chatzeile (Max 28.09.).</summary>
        public static event Action<string, int, string> Milestone;
        /// <summary>Letzter Meilenstein-Text (Selbsttest).</summary>
        public static string LastMilestone { get; private set; } = "";

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PointsById.Clear();
            Ready = false;
            MetersOnFoot = 0;
            MeaningfulDodges = 0;
            DamageBlocked = 0;
            Revision = 0;
            LastMilestone = "";
        }

        public static void RaiseMilestone(string id, int rank)
        {
            MasteryDefinition d = MasteryCatalog.Get(id);
            if (d == null) return;
            string name = d.displayName;
            foreach (ClassMountDefinition m in MountCatalog.All)
                if (m.bond == d) name = (Ashveil.Gameplay.Mounts.MountClient.HasMount ? Ashveil.Gameplay.Mounts.MountClient.Name : m.displayName) + " – " + d.displayName;
            foreach (CompanionSpecies c in CompanionCatalog.All)
                if (c.bond == d) name = (string.IsNullOrEmpty(Ashveil.Gameplay.Companions.CompanionClient.Name) ? c.displayName : Ashveil.Gameplay.Companions.CompanionClient.Name) + " – " + d.displayName;
            LastMilestone = d.displayName + " " + rank;
            Milestone?.Invoke(name, rank, d.EffectsText(rank));
        }

        public static void Apply(string[] ids, double[] points, double meters, int dodges, double blocked = 0)
        {
            DamageBlocked = blocked;
            PointsById.Clear();
            for (int i = 0; ids != null && i < ids.Length; i++)
                PointsById[ids[i]] = points != null && i < points.Length ? points[i] : 0;
            MetersOnFoot = meters;
            MeaningfulDodges = dodges;
            Ready = true;
            Revision++;
            Changed?.Invoke();
        }

        public static double Points(string id) => PointsById.TryGetValue(id ?? "", out double p) ? p : 0;

        public static int Rank(string id)
        {
            MasteryDefinition d = MasteryCatalog.Get(id);
            return d != null ? d.RankFor(Points(id)) : 0;
        }
    }
}
