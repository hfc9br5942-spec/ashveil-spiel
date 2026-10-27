namespace Ashveil.World
{
    /// <summary>
    /// Territoriale Zugehoerigkeit einer Region, eines Ortes oder eines Spielers (Weltprogressions-Plan 30.09.; Bedeutung
    /// geschaerft durch die Fraktionskrieg-Direktive vom 05.10., §3A): wer den Raum grundsaetzlich beansprucht, verwaltet oder
    /// kontrolliert. Sagt NICHT, wie viel Kampf dort stattfindet – Konflikt entsteht nur an lokalen Konfliktknoten
    /// (<see cref="ConflictNodeDefinition"/>). Es gibt keinen regionsweiten Kriegszustand (WarPresenceState ist verworfen).
    /// Namen intern, PROVISIONAL – die Fraktionsnamen entscheidet Max. Werte werden als Zahl gespeichert: nie umnummerieren.
    /// </summary>
    public enum FactionAffinity
    {
        Unknown = 0,
        /// <summary>Seite A: Menschen, Zwerge, Hulae.</summary>
        HumanAlliance = 1,
        /// <summary>Seite B: Nharim, Olmari (Arbeitsname), Brakkun. Kharai bleiben DEFERRED (spaeterer Weltteil).</summary>
        NharimAlliance = 2,
        /// <summary>Keine Seite beansprucht den Raum (z. B. Riesental, Sundhafen, Offene See).</summary>
        Neutral = 3,
        /// <summary>Beide Seiten beanspruchen den Raum („geteilt“, WORLD-218). Heisst nicht „ueberall Schlacht“.</summary>
        Contested = 4
    }

    /// <summary>
    /// Stufe im klassischen MMO-Bogen (Start → Heimat → Konvergenz → Grenzland → Umkaempft → Alte Welt → Weltgebiet).
    /// Nur Reise- und Stufenplanung, KEIN Kriegszustand: „Frontier“ und „ContestedWorld“ meinen Abschnitte des Stufenbogens,
    /// nicht Kampfintensitaet (Fraktionskrieg-Direktive 05.10.: kein starres Heimat → Grenzland → Front-Schema).
    /// </summary>
    public enum ProgressionTier
    {
        RaceStart = 0,
        RaceHome = 1,
        FactionConvergence = 2,
        Frontier = 3,
        ContestedWorld = 4,
        DangerousOldWorld = 5,
        WorldScale = 6
    }

    /// <summary>Wie weit/aufwendig die Anreise ist – fuer Reisezeit-Schaetzung und spaetere Karren/Flug.</summary>
    public enum TravelTier
    {
        Local = 0,
        Regional = 1,
        Frontier = 2,
        Remote = 3
    }

    /// <summary>Bedeutung eines Ortes als Treffpunkt.</summary>
    public enum HubTier
    {
        None = 0,
        Outpost = 1,
        Camp = 2,
        Town = 3,
        /// <summary>Platzhalter fuer eine spaetere Hauptstadt – nicht erfunden, nur reserviert.</summary>
        CapitalSlot = 4
    }

    /// <summary>Routenklassen (Terrain-Vorgabe §45). MAIN + SECONDARY formen das Gelaende stark, SECRET kaum.</summary>
    public enum RouteClass
    {
        Main = 0,
        Secondary = 1,
        Exploration = 2,
        Secret = 3
    }

    /// <summary>Technische Biom-Kategorien (Terrain-Vorgabe §62) – reine Datenkategorien, keine Lore.</summary>
    public enum MacroBiome : byte
    {
        Ocean = 0,
        TemperatePlains = 1,
        MixedForest = 2,
        PineHighlands = 3,
        Wetland = 4,
        Moor = 5,
        GranitePlateau = 6,
        CoastalCliff = 7,
        Ashland = 8,
        Alpine = 9,
        DeepValley = 10,
        AncientForest = 11,
        VeilScar = 12,
        CanyonRim = 13
    }
}
