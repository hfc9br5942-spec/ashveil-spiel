using System;

namespace Ashveil.Data
{
    /// <summary>
    /// Spielbares Volk (Weltlore-Vorgabe §Rasse: „zunächst je eine spielbare Kernrasse“ – Mensch und Nharim).
    /// Die Rasse wird im Charakter-Editor gewaehlt und steht danach fest (Klassen- und Geschichtsquests pruefen sie).
    /// Anzeige ueber die Texttabelle (<see cref="Loc"/>), nie als fester Text im Spielcode.
    /// </summary>
    [Serializable]
    public sealed class RaceDefinition
    {
        public string id;
        /// <summary>Texttabellen-Schluessel fuer Name und Kurzbeschreibung im Editor.</summary>
        public string nameKey;
        public string descriptionKey;
        /// <summary>
        /// Seitenkennung: "faction.human" = Seite A (Menschen; spaeter Zwerge, Hulae), "faction.nharim" = Seite B (Nharim;
        /// spaeter Olmari, Brakkun). Kennungen bleiben aus Kompatibilitaet so benannt – Fraktion und Volk sind zwei
        /// Identitaeten. Zuordnung zum Territorium: Ashveil.World.TerritoryRules.SideOf (Fraktionskrieg-Direktive 05.10.).
        /// </summary>
        public string faction;
        /// <summary>Eigener Koerper vorhanden? Nein = vorlaeufig menschlicher Koerper (Aussehen folgt).</summary>
        public bool ownBody;

        public string DisplayName => Loc.T(nameKey);
        public string Description => Loc.T(descriptionKey);
    }

    /// <summary>
    /// Alle Voelker an einer Stelle (Code-Katalog wie Reittiere/Gefaehrten). Der Index ist Teil des gespeicherten Aussehens –
    /// neue Voelker nur hinten anhaengen, nie umsortieren.
    /// </summary>
    public static class RaceCatalog
    {
        public const string HumanId = "race.human";
        public const string NharimId = "race.nharim";

        public static readonly RaceDefinition Human = new RaceDefinition
        {
            id = HumanId, nameKey = "race.human.name", descriptionKey = "race.human.desc", faction = "faction.human", ownBody = true
        };

        /// <summary>Arbeitstitel (Weltlore §5). Koerper, Creator-Kategorien und Startgebiet folgen.</summary>
        public static readonly RaceDefinition Nharim = new RaceDefinition
        {
            id = NharimId, nameKey = "race.nharim.name", descriptionKey = "race.nharim.desc", faction = "faction.nharim", ownBody = false
        };

        public static readonly RaceDefinition[] All = { Human, Nharim };

        /// <summary>
        /// Im Charakter-Editor waehlbare Voelker; der Server prueft dasselbe (AppearanceCatalog.Sanitize). Standard: alle.
        /// Build fuer Spieltest r2 (#30, Max 06.10.2026, Klick „Nur Mensch“): nur Mensch – ueber das Kennzeichen
        /// ASHVEIL_NUR_MENSCH, das nur dieser Build setzt (SpieltestBuild). Editor, Tests und andere Builds bleiben unveraendert.
        /// </summary>
#if ASHVEIL_NUR_MENSCH
        public static readonly RaceDefinition[] Playable = { Human };
#else
        public static readonly RaceDefinition[] Playable = All;
#endif

        /// <summary>Ist das Volk mit diesem Index (in <see cref="All"/>) hier waehlbar?</summary>
        public static bool IsPlayable(int index)
        {
            if (index < 0 || index >= All.Length) return false;
            foreach (RaceDefinition r in Playable) if (r.id == All[index].id) return true;
            return false;
        }

        /// <summary>Kennungen der waehlbaren Voelker, durch Komma getrennt (Protokoll, Kurztest des Spieltest-Builds).</summary>
        public static string PlayableIds()
        {
            var ids = new string[Playable.Length];
            for (int i = 0; i < Playable.Length; i++) ids[i] = Playable[i].id;
            return string.Join(", ", ids);
        }

        public static RaceDefinition Get(string id)
        {
            foreach (RaceDefinition r in All) if (r.id == id) return r;
            return null;
        }

        public static RaceDefinition At(int index) => index >= 0 && index < All.Length ? All[index] : Human;

        public static int IndexOf(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].id == id) return i;
            return 0;
        }

        /// <summary>Gueltige Kennung oder Mensch.</summary>
        public static string Normalize(string id) => Get(id) != null ? id : HumanId;
    }
}
