using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.World
{
    /// <summary>Welche Rolle ein Quest-Ort fuer Auftraege spielt.</summary>
    public enum QuestSiteKind
    {
        /// <summary>Stuetzpunkt mit Questgebern, Haendlern, Rast (wie ein Dorf/Lager in WoW Classic).</summary>
        Hub = 0,
        /// <summary>Gegner besiegen.</summary>
        Kill = 1,
        /// <summary>Gegenstaende sammeln (Beute oder Fundstuecke).</summary>
        Collect = 2,
        /// <summary>Ort erreichen/erkunden.</summary>
        Explore = 3,
        /// <summary>Geschichte, Fundstuecke, Gespraeche, Foreshadowing.</summary>
        Story = 4,
        /// <summary>Seltener Elite-Gegner.</summary>
        Elite = 5,
        /// <summary>Weltboss.</summary>
        WorldBoss = 6,
        /// <summary>Eingang einer Instanz.</summary>
        Dungeon = 7,
        /// <summary>Uebergang in eine andere Region.</summary>
        Transition = 8,
        /// <summary>Versteckt, ohne Kartenmarke und Schild (z. B. Oedis Huette).</summary>
        Hidden = 9
    }

    /// <summary>Wie weit ein Quest-Ort heute ist.</summary>
    public enum QuestSiteStatus
    {
        /// <summary>Hat heute schon einen Auftrag.</summary>
        Active = 0,
        /// <summary>Der Ort steht in der Welt, es haengt noch kein Auftrag daran.</summary>
        Ready = 1,
        /// <summary>Nur reserviert (Anker) – wird spaeter gebaut.</summary>
        Planned = 2
    }

    /// <summary>
    /// Questrelevanter Ort mit stabiler Kennung (<c>site.&lt;region&gt;.&lt;name&gt;</c>), Stufenband und Zone.
    /// Auftraege, Karte, Entdeckungen und spaeter das Streaming beziehen sich auf die Kennung, nicht auf Szenenobjekte
    /// (Large-World-Masterplan §86–88). Die Verteilung aller Orte steht in einer Tabelle
    /// (<c>Editor/World/KarethQuestSites.cs</c>) und in <c>Docs/Design/ashveil-quest-orte-kareth.md</c>.
    /// </summary>
    public sealed class WorldQuestSite : MonoBehaviour
    {
        [SerializeField] private string id = "";
        [SerializeField] private string displayName = "";
        [SerializeField] private string zone = "";
        [SerializeField] private string hubId = "";
        [SerializeField] private QuestSiteKind kind;
        [SerializeField] private QuestSiteStatus status;
        [SerializeField] private int minLevel = 1;
        [SerializeField] private int maxLevel = 1;
        [SerializeField] private float radius = 20f;
        [SerializeField] private bool reachable = true;
        [SerializeField, TextArea] private string notes = "";

        private static readonly List<WorldQuestSite> Registered = new List<WorldQuestSite>();

        public string Id => id;
        public string DisplayName => displayName;
        public string Zone => zone;
        /// <summary>Kennung des Stuetzpunkts, von dem aus die Auftraege fuer diesen Ort vergeben werden.</summary>
        public string HubId => hubId;
        public QuestSiteKind Kind => kind;
        public QuestSiteStatus Status => status;
        public int MinLevel => minLevel;
        public int MaxLevel => maxLevel;
        public float Radius => radius;
        /// <summary>Heute zu Fuss vom Startlager erreichbar (false z. B. Kratersee auf dem Ruecken, Aufstieg geplant).</summary>
        public bool Reachable => reachable;
        public string Notes => notes;
        public static IReadOnlyList<WorldQuestSite> All => Registered;

        public void Configure(string siteId, string name, string zoneName, string hub, QuestSiteKind siteKind, QuestSiteStatus siteStatus,
            int levelMin, int levelMax, float siteRadius, bool canReach, string siteNotes)
        {
            id = siteId;
            displayName = name;
            zone = zoneName;
            hubId = hub;
            kind = siteKind;
            status = siteStatus;
            minLevel = levelMin;
            maxLevel = levelMax;
            radius = siteRadius;
            reachable = canReach;
            notes = siteNotes;
        }

        public static WorldQuestSite Find(string siteId)
        {
            foreach (WorldQuestSite s in Registered)
                if (s != null && s.id == siteId) return s;
            return null;
        }

        private void OnEnable()
        {
            if (!Registered.Contains(this)) Registered.Add(this);
        }

        private void OnDisable() => Registered.Remove(this);

        private void OnDrawGizmos()
        {
            Color c = kind == QuestSiteKind.Hub ? new Color(1f, 0.85f, 0.3f) :
                status == QuestSiteStatus.Planned ? new Color(0.6f, 0.6f, 0.7f) : new Color(0.4f, 0.9f, 1f);
            c.a = 0.55f;
            Gizmos.color = c;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
