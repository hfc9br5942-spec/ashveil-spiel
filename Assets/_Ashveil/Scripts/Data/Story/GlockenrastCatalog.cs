using System.Collections.Generic;

namespace Ashveil.Data
{
    /// <summary>
    /// P3 Glockenrast (Stufe 5–9, Vorgabe Max 30.09.): Kennungen der dreizehn Auftraege, Figuren, Gegenstaende, Orte,
    /// Geschichtsschritte und Rechtsklick-Handlungen („Knoten“) an EINER Stelle – Inhaltsaufbau (Editor), Szene, Server und
    /// Test lesen dieselben Konstanten. Zwei Geschichten und Weltauftraege:
    ///   „Was die Glocken nicht sagen“ (Anselm, Runa) · „Harz und Bronze“ (Merle, Jasko) · Tamo, Pilger/Veit, Torfschrate.
    /// Texte stehen in der Texttabelle (Resources/Text/de/glockenrast.txt).
    /// </summary>
    public static class GlockenrastCatalog
    {
        // ------------------------------------------------------------------ Figuren
        public const string Bralt = "npc.oskar_bralt";
        public const string Maelis = "npc.maelis";
        public const string Anselm = "npc.glockenrast.anselm";
        public const string Runa = "npc.glockenrast.runa";
        public const string Merle = "npc.glockenrast.merle";
        public const string Jasko = "npc.glockenrast.jasko";
        public const string Pilgrim = "npc.glockenrast.pilger";
        /// <summary>Wagner Tamo am liegengebliebenen Wagen (Figur gab es schon, jetzt mit Auftrag).</summary>
        public const string Tamo = "npc.tamo";
        /// <summary>Schreiber Veit am Nordtor des Wachtfeuers (Figur gab es schon, jetzt Abgabe fuer G7).</summary>
        public const string Veit = "npc.veit";

        // ------------------------------------------------------------------ Auftraege (Reihenfolge = Tabelle G1–G13)
        public const string G1 = "quest.glockenrast.weg_nach_norden";
        public const string G2 = "quest.glockenrast.nadeln_im_schilf";
        public const string G3 = "quest.glockenrast.gegengift";
        public const string G4 = "quest.glockenrast.rad_fuer_tamo";
        public const string G5 = "quest.glockenrast.leeres_lager";
        public const string G6 = "quest.glockenrast.harz_ohne_haende";
        public const string G7 = "quest.glockenrast.schaedeldecke";
        public const string G8 = "quest.glockenrast.unter_wasser";
        public const string G9 = "quest.glockenrast.torf_und_zorn";
        public const string G10 = "quest.glockenrast.zeichen_im_buch";
        public const string G11 = "quest.glockenrast.schweigender_turm";
        public const string G12 = "quest.glockenrast.nordmoor";
        public const string G13 = "quest.glockenrast.heute_nacht";

        public static readonly string[] Quests = { G1, G2, G3, G4, G5, G6, G7, G8, G9, G10, G11, G12, G13 };

        /// <summary>Summe der Auftrags-Erfahrung G1–G13 (Tabelle der Vorgabe).</summary>
        public const int QuestXpTotal = 400 + 700 + 650 + 700 + 900 + 900 + 800 + 1000 + 1100 + 900 + 1200 + 1400 + 1000;

        // ------------------------------------------------------------------ Gegner (bestehende Definitionen)
        public const string EnemySpitter = "enemy.needle_spitter";
        public const string EnemyFaded = "enemy.faded_one";
        public const string EnemyPeatBrute = "enemy.peat_brute";

        // ------------------------------------------------------------------ Gegenstaende (Auftrag)
        public const string ItemVenomGland = "item.gr_venom_gland";
        public const string ItemSpareWheel = "item.gr_spare_wheel";
        public const string ItemLetter = "item.gr_jasko_letter";
        public const string ItemLedger = "item.gr_resin_ledger";
        public const string ItemRubbing = "item.gr_rubbing";
        public const string ItemBellFragment = "item.gr_bell_fragment";

        // ------------------------------------------------------------------ Orte (Erkundungsziele, QuestArea)
        public const string AreaLumberCamp = "area.glockenrast.holzfaellerlager";
        public const string AreaResinCamp = "area.glockenrast.harzerlager";
        public const string AreaSkullcap = "area.glockenrast.schaedeldecke";
        public const string AreaBellTower = "area.glockenrast.glockenturm";
        public const string AreaNorthMoor = "area.glockenrast.nordmoor";

        // ------------------------------------------------------------------ Geschichtsschritte (Zieltyp Story)
        public const string StoryBellStruck = "story.glocke_angeschlagen";
        public const string StoryJaskoFound = "story.jasko_gefunden";

        // ------------------------------------------------------------------ Geschichtsmarken (storyFlags)
        /// <summary>G10 abgegeben: die Aussprache an der Glockenrast hat stattgefunden (Runa glaubt Anselm nicht mehr blind).</summary>
        public const string FlagConfrontation = "story.glockenrast.aussprache";
        /// <summary>G13 abgegeben: Jasko ist heim. Runa haelt den Schluessel zum Glockengalgen, Anselm betet laut,
        /// Merle und Jasko arbeiten an entgegengesetzten Enden des Holzfaellerlagers.</summary>
        public const string FlagHomecoming = "story.glockenrast.heimgekehrt";
        public const string FlagRunaHoldsKey = "story.glockenrast.runa_schluessel";
        public const string FlagAnselmPraysAloud = "story.glockenrast.anselm_betet_laut";
        public const string FlagSeparateEnds = "story.glockenrast.getrennte_enden";

        /// <summary>Marke „dieses Fundstueck ist fuer mich genommen“ (je Knoten, beim Neuannehmen des Auftrags geloescht).</summary>
        public static string TakenFlag(string action) => "story.glockenrast.genommen." + action;

        // ------------------------------------------------------------------ Rechtsklick-Handlungen (StoryInteractable + StoryAnchor)

        /// <summary>
        /// Eine Handlung in der Welt: Fundstueck aufheben (Gegenstand) oder Geschichtsschritt ausloesen. Die Kennung ist zugleich
        /// die Aktion (StoryClient.Send) und die Kennung des <c>StoryAnchor</c>, an dem der Server die Entfernung prueft.
        /// </summary>
        public struct Node
        {
            public string Action;
            public string Quest;
            /// <summary>item.* – Fundstueck (Sammelziel des Auftrags).</summary>
            public string Item;
            /// <summary>story.* – Geschichtsschritt (Zieltyp Story).</summary>
            public string Story;
            /// <summary>Texttabellen-Schluessel des Hinweises („Rechtsklick: …“).</summary>
            public string LabelKey;
            public float Range;
            /// <summary>Optional (Akt I „Das Register“, 13.10.2026): Geschichtsschritt desselben Auftrags, der vorher erledigt sein muss.</summary>
            public string After;
            /// <summary>Texttabellen-Schlüssel des Hinweises, wenn <see cref="After"/> noch fehlt.</summary>
            public string AfterKey;
            /// <summary>Optional: Texttabellen-Schlüssel einer Meldung an den Spieler nach dem Geschichtsschritt (was er sieht).</summary>
            public string MessageKey;
        }

        public const string NodeWheel = "gr.rad";
        public const string NodeLetter = "gr.brief";
        public const string NodeLedger = "gr.kassenbuch";
        public const string NodeStrike = "gr.anschlagen";
        public const string NodeJasko = "gr.jasko";
        public static string NodeRubbing(int i) => "gr.abrieb" + (i + 1);
        public static string NodeFragment(int i) => "gr.scherbe" + (i + 1);
        public const int RubbingCount = 3;
        public const int FragmentCount = 5;

        private static Node[] _nodes;

        public static IReadOnlyList<Node> Nodes
        {
            get
            {
                if (_nodes != null) return _nodes;
                var l = new List<Node>
                {
                    new Node { Action = NodeWheel, Quest = G4, Item = ItemSpareWheel, LabelKey = "story.glockenrast.action.wheel", Range = 4f },
                    new Node { Action = NodeLetter, Quest = G6, Item = ItemLetter, LabelKey = "story.glockenrast.action.letter", Range = 4f },
                    new Node { Action = NodeLedger, Quest = G6, Item = ItemLedger, LabelKey = "story.glockenrast.action.ledger", Range = 4f },
                    new Node { Action = NodeStrike, Quest = G11, Story = StoryBellStruck, LabelKey = "story.glockenrast.action.strike", Range = 4.5f },
                    new Node { Action = NodeJasko, Quest = G12, Story = StoryJaskoFound, LabelKey = "story.glockenrast.action.jasko", Range = 4.5f }
                };
                for (int i = 0; i < RubbingCount; i++)
                    l.Add(new Node { Action = NodeRubbing(i), Quest = G7, Item = ItemRubbing, LabelKey = "story.glockenrast.action.rubbing", Range = 4f });
                for (int i = 0; i < FragmentCount; i++)
                    l.Add(new Node { Action = NodeFragment(i), Quest = G8, Item = ItemBellFragment, LabelKey = "story.glockenrast.action.fragment", Range = 4f });
                _nodes = l.ToArray();
                return _nodes;
            }
        }

        public static bool TryNode(string action, out Node node)
        {
            // Westweiden (12.10.2026): Fundstellen „ww.*“ laufen über dieselbe Mechanik
            if (WestweidenCatalog.TryNode(action, out node)) return true;
            // Akt I „Das Register“ (13.10.2026): Fundstellen „rg.*“
            if (RegisterCatalog.TryNode(action, out node)) return true;
            foreach (Node n in Nodes)
                if (n.Action == action) { node = n; return true; }
            node = default;
            return false;
        }

        public static bool IsNodeAction(string action) =>
            action != null && (action.StartsWith("gr.", System.StringComparison.Ordinal) || WestweidenCatalog.IsNodeAction(action) ||
                                   RegisterCatalog.IsNodeAction(action));
    }
}
