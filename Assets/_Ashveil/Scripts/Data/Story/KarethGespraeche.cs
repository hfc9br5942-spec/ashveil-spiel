namespace Ashveil.Data
{
    /// <summary>
    /// „Lockrufe nach draußen“ (WORLD-239, Max 10.10.2026): Gesprächsschnipsel zwischen Figuren des Wachtfeuer-Lagers, die
    /// laut Tagesablauf ohnehin beieinanderstehen. Sie nennen nur Orte, die es in Kareth v4 gibt, damit der Spieler Ziele hört,
    /// bevor ein Auftrag sie nennt. Texte in Resources/Text/de/kareth.txt (talk.kareth.*), Wortlaut VORSCHLAG Claude (LORE-109).
    /// Abgespielt von <c>NpcGespraeche</c> (Ashveil.UI), rein am Client – kein Netzverkehr, kein Servertakt.
    /// </summary>
    public static class KarethGespraeche
    {
        public struct Zeile
        {
            /// <summary>Wer spricht (Nummer in <see cref="Gespraech.Figuren"/>).</summary>
            public int Wer;
            public string Key;
            public Zeile(int wer, string key) { Wer = wer; Key = key; }
        }

        public struct Gespraech
        {
            public string Id;
            /// <summary>Namen der Figuren (GameObject-Namen der Tagesabläufe, <c>NpcRoutineRegistry.Find</c>).</summary>
            public string[] Figuren;
            public Zeile[] Zeilen;
        }

        private static Gespraech G(string id, string[] figuren, params Zeile[] zeilen) => new Gespraech { Id = id, Figuren = figuren, Zeilen = zeilen };
        private static Zeile Z(int wer, string key) => new Zeile(wer, "talk.kareth." + key);

        /// <summary>Wachtfeuer-Lager: Nordtor (Brenn, Pell, Kartograf), Markt (Maelis, Gerrit), Kochstelle (Wenna, Jorn), abends am Feuer.</summary>
        public static readonly Gespraech[] Lager =
        {
            G("nordtor.1", new[] { "Brenn", "Händler Pell" }, Z(0, "nordtor.1.1"), Z(1, "nordtor.1.2")),           // Westsattel, Westtor (Q2, K-C)
            G("nordtor.2", new[] { "Händler Pell", "Brenn" }, Z(0, "nordtor.2.1"), Z(1, "nordtor.2.2")),           // Aussichtsfels (Q3)
            G("nordtor.3", new[] { "Kartograf", "Brenn" }, Z(0, "nordtor.3.1"), Z(1, "nordtor.3.2")),              // alte Straße, Werkstätten (U-4)
            G("markt.1", new[] { "Kräuterin Maelis", "Pfeilmacher Gerrit" }, Z(0, "markt.1.1"), Z(1, "markt.1.2")),   // Gehöft am Bergfuß (Q4/Q5)
            G("markt.2", new[] { "Pfeilmacher Gerrit", "Kräuterin Maelis" }, Z(0, "markt.2.1"), Z(1, "markt.2.2")),   // Kiefernschlag (K-D)
            G("kueche.1", new[] { "Wenna", "Jorn" }, Z(0, "kueche.1.1"), Z(1, "kueche.1.2")),                       // Vorratsgruben (K-A)
            G("kueche.2", new[] { "Jorn", "Wenna" }, Z(0, "kueche.2.1"), Z(1, "kueche.2.2")),                       // Wasserstelle (K-B)
            G("feuer.1", new[] { "Kartograf", "Schmied Dorn" }, Z(0, "feuer.1.1"), Z(1, "feuer.1.2")),             // Glockenrast, Nordmoor (G1, G12)
            G("feuer.2", new[] { "Pfeilmacher Gerrit", "Kräuterin Maelis" }, Z(0, "feuer.2.1"), Z(1, "feuer.2.2")),   // Untersteg, Damm (U-2)
            G("feuer.3", new[] { "Waffenmeisterin Ilse Harrow", "Kartograf" }, Z(0, "feuer.3.1"), Z(1, "feuer.3.2")), // Wrack im Salz (U-3)
        };

        /// <summary>Untersteg (Sheets v1, Max 10.10.): Grete und Tamo beim Mittag, Hennek, Idun und Ansa mit Ebba an der Schale.</summary>
        public static readonly Gespraech[] Untersteg =
        {
            G("untersteg.1", new[] { "Grete Bachmann", "Wagner Tamo" }, Z(0, "untersteg.1.1"), Z(1, "untersteg.1.2")),     // Tamos Wagen (U-1)
            G("untersteg.2", new[] { "Hennek Doll", "Ebba Salweg" }, Z(0, "untersteg.2.1"), Z(1, "untersteg.2.2")),        // Nordmoor (G12)
            G("untersteg.3", new[] { "Idun Vesper", "Ebba Salweg" }, Z(0, "untersteg.3.1"), Z(1, "untersteg.3.2")),        // Oswald am Damm (U-2)
            G("untersteg.4", new[] { "Mutter Ansa", "Ebba Salweg" }, Z(0, "untersteg.4.1"), Z(1, "untersteg.4.2"), Z(0, "untersteg.4.3")),   // Schweigender Turm (G11)
            G("untersteg.5", new[] { "Hennek Doll", "Idun Vesper" }, Z(0, "untersteg.5.1"), Z(1, "untersteg.5.2")),        // alte Straße, Werkstätten
        };

        /// <summary>Alle Gespräche der Szene Kareth v4 (Lager und Untersteg) – das Szenenobjekt spielt nach Nähe.</summary>
        public static Gespraech[] Alle()
        {
            var l = new System.Collections.Generic.List<Gespraech>(Lager);
            l.AddRange(Untersteg);
            return l.ToArray();
        }
    }
}
