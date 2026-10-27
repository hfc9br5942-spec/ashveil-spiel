using System.Collections.Generic;

namespace Ashveil.Data
{
    /// <summary>
    /// Hauptquest der Menschen, Akt I „Das Register“ (freigegeben von Max am 12.10.2026 abends: Akt, Texte v1.1 in einfacher Sprache,
    /// Bralt hat selbst geschnitten, Fenna kam vor elf Jahren aus der Asche, Nessa bekommt ihre Seite erst in Akt II). Quelle:
    /// `claude/ashveil-hauptquest-menschen-akt1-das-register-v1-2026-10-12.md`. Kennungen an EINER Stelle – Inhaltsaufbau (Editor),
    /// Szene, Server und Tests lesen dieselben Konstanten. Texte: Resources/Text/de/register.txt.
    ///   R1 Ein Bündel zu viel (Veit, 4) · R2 Das Buch geht zur Halle (Veit → Bralt, 5) · R3 Die Singende (Veit → Ansa, 7) ·
    ///   R4 Eine Seite für Nessa (Nessa, 8) · R5 Unter seinem Messer (Nessa → Bralt, 10) · R6 Wem die Seite gehört (Bralt → Fenna, 11)
    /// Nur für Menschen (Hauptquest je Volk). R1–R5 gelten in Kareth v4, R6 braucht die Westweiden (Fenna am Kornwagen).
    /// Bewusst NICHT: keine Erklärung der Ankunft, keine Begründung der Schleier-Nächte, keine Auflösung von Nessas Herkunft.
    /// </summary>
    public static class RegisterCatalog
    {
        // ------------------------------------------------------------------ Figuren
        public const string Veit = KarethCatalog.Veit;
        public const string Bralt = KarethCatalog.Bralt;
        /// <summary>Heilerin Nessa (Lazarett) – seit Akt I Auftragsfigur.</summary>
        public const string Nessa = "npc.nessa";
        /// <summary>Mutter Ansa (Untersteg, am Herd der Schale) – seit Akt I Auftragsfigur.</summary>
        public const string Ansa = "npc.untersteg.ansa";
        public const string Fenna = WestweidenCatalog.Fenna;

        // ------------------------------------------------------------------ Aufträge
        public const string R1 = "quest.register.r1_ein_buendel_zu_viel";
        public const string R2 = "quest.register.r2_das_buch_geht_zur_halle";
        public const string R3 = "quest.register.r3_die_singende";
        public const string R4 = "quest.register.r4_eine_seite_fuer_nessa";
        public const string R5 = "quest.register.r5_unter_seinem_messer";
        public const string R6 = "quest.register.r6_wem_die_seite_gehoert";
        public static readonly string[] Quests = { R1, R2, R3, R4, R5, R6 };

        /// <summary>EP nach der Kareth-Kurve (VORSCHLAG Claude): Ort- und Fundauftrag voll, Botengänge der Hauptkette 45 %.</summary>
        public const int QuestXpTotal = 700 + 400 + 450 + 1000 + 500 + 600;

        // ------------------------------------------------------------------ Gegenstände (Auftrag)
        public const string ItemRegister = "item.rg_register";          // R2: das Register (Veit gibt es mit, bleibt bei Bralt)
        public const string ItemSealedPage = "item.rg_versiegelte_seite"; // R6: Fennas Seite (bleibt bei Fenna, ungelesen)

        // ------------------------------------------------------------------ Geschichtsschritte (Zieltyp Story)
        public const string StoryBoard = "story.register.brett_gezaehlt";
        public const string StoryBook = "story.register.buch_gezaehlt";
        public const string StoryShelf = "story.register.alte_baende";
        public const string StoryChest = "story.register.truhe_gefunden";

        // ------------------------------------------------------------------ Geschichtsmarken (storyFlags)
        /// <summary>R5 abgegeben: Bralt hat das Schneiden zugegeben (Akt II knüpft hier an).</summary>
        public const string FlagConfession = "story.register.bralt_gestanden";
        /// <summary>R6 abgegeben: Fenna hat ihre Seite – ungelesen im Fahrtenbuch.</summary>
        public const string FlagFennasPage = "story.register.fennas_seite";

        // ------------------------------------------------------------------ Fundstellen (Rechtsklick, Mechanik wie GlockenrastCatalog)
        public const string NodeBoard = "rg.brett";
        public const string NodeBook = "rg.buch";
        public const string NodeShelf = "rg.regal";
        public const string NodeChest = "rg.truhe";

        private static GlockenrastCatalog.Node[] _nodes;

        public static IReadOnlyList<GlockenrastCatalog.Node> Nodes =>
            _nodes ??= new[]
            {
                new GlockenrastCatalog.Node { Action = NodeBoard, Quest = R1, Story = StoryBoard, LabelKey = "story.register.action.board", Range = 4f,
                    MessageKey = "story.register.board.counted" },
                // „Erst das Brett, dann das Buch.“ (Veit)
                new GlockenrastCatalog.Node { Action = NodeBook, Quest = R1, Story = StoryBook, LabelKey = "story.register.action.book", Range = 4f,
                    After = StoryBoard, AfterKey = "story.register.book.first_board", MessageKey = "story.register.book.counted" },
                new GlockenrastCatalog.Node { Action = NodeShelf, Quest = R2, Story = StoryShelf, LabelKey = "story.register.action.shelf", Range = 4f,
                    MessageKey = "story.register.shelf.seen" },
                new GlockenrastCatalog.Node { Action = NodeChest, Quest = R4, Story = StoryChest, LabelKey = "story.register.action.chest", Range = 4f,
                    MessageKey = "story.register.chest.seen" },
            };

        public static bool IsNodeAction(string action) => action != null && action.StartsWith("rg.", System.StringComparison.Ordinal);

        public static bool TryNode(string action, out GlockenrastCatalog.Node node)
        {
            if (IsNodeAction(action))
                foreach (GlockenrastCatalog.Node n in Nodes)
                    if (n.Action == action) { node = n; return true; }
            node = default;
            return false;
        }
    }
}
