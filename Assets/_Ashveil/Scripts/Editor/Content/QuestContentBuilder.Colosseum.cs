using UnityEditor;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Kolosseum Pass 2: die drei Leute am Vorplatz des Rings (Kanon §54: Ringmeister, Haendler, Reparatur).
    /// PLATZHALTER: Anmeldung, Rekorde und Wellen kommen mit Pass 3/4 – der Ringmeister sagt das in der Welt auch so.
    /// Eine Reparatur gibt es im Spiel nicht (keine Haltbarkeit); der Waffenwart ist bis dahin ein Ruestungshaendler.
    /// </summary>
    public static partial class QuestContentBuilder
    {
        public const string Ringmaster = "npc.ringmaster";
        public const string Sutler = "npc.ring_sutler";
        public const string Armorer = "npc.ring_armorer";

        /// <summary>Legt die NPC-Definitionen an bzw. aktualisiert sie (immer – die Texte gehoeren zum Greybox-Stand).</summary>
        public static void BuildColosseumNpcs()
        {
            ProjectSetup.EnsureFolder(NpcFolder);
            bool keep = _refresh;
            _refresh = true;
            try
            {
                Npc("Npc_Ringmeister", Ringmaster, "Ringmeister Brannoc", "Der Ring von Kareth",
                    "Den Ring hat keiner von uns gebaut. Wir haben nur den Sand hineingekippt und die Tore geölt.\n\n" +
                    "Bis zu fünf von euch, eine Stunde, zwölf Prüfungen – so ist es gedacht. Geht durchs Tor, wenn ihr bereit seid: " +
                    "Zehn Atemzüge später öffne ich die Tiergänge. Was herauskommt, wird mit jeder Prüfung mehr. " +
                    "Uhr, Prüfpunkte und die Belohnung richte ich noch ein – bis dahin zählt nur, wie lange ihr steht.", false);
                Npc("Npc_Marketenderin", Sutler, "Marketenderin Oda", "Proviant & Pulver",
                    "Wer in den Ring geht, kauft vorher. Wer rauskommt, kauft danach. Ich hab für beide was.", true,
                    (I("healing_draught"), 1, 0), (I("strong_healing_draught"), 1, 0), (I("dry_bread"), 1, 0),
                    (I("ash_tea"), 1, 0), (I("black_powder"), 5, 0));
                Npc("Npc_Waffenwart", Armorer, "Waffenwart Hesk", "Rüstung (Reparatur folgt)",
                    "Flicken? Gern, sobald du mir etwas bringst, das sich zu flicken lohnt. Bis dahin verkauf ich dir lieber gleich was Ganzes.", true,
                    (I("miner_helmet"), 1, 0), (I("hewer_gloves"), 1, 0), (I("brine_robe"), 1, 0), (I("shift_belt"), 1, 0),
                    (I("oak_shield"), 1, 0)); // PLATZHALTER: eigenes Ring-Sortiment 14–16 im Inhalts-Pass
            }
            finally
            {
                _refresh = keep;
            }
            AssetDatabase.SaveAssets();
        }
    }
}
