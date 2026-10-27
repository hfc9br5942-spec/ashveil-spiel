using Ashveil.Data;
using UnityEngine;
using RC = Ashveil.Data.RegisterCatalog;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Hauptquest der Menschen, Akt I „Das Register“ (freigegeben 12.10.2026): die zwei Auftragsgegenstände. Beschreibungen sind
    /// Vorschläge (Claude, einfache Sprache), Namen aus den freigegebenen Texten. Bei jedem Aufbau neu geschrieben.
    /// </summary>
    public static partial class ItemContentBuilder
    {
        private static void BuildRegisterItems()
        {
            bool keep = _refresh;
            _refresh = true;
            try
            {
                QuestItem("rg_register", "Das Register", IconShape.Scroll, new Color(0.16f, 0.30f, 0.18f), RC.R2, 1,
                    "Grüner Rücken, abgegriffene Ecken. Jeder Name in derselben kleinen, genauen Schrift. Es ist schwerer, als es aussieht.");
                QuestItem("rg_versiegelte_seite", "Versiegelte Seite", IconShape.Letter, new Color(0.82f, 0.77f, 0.64f), RC.R6, 1,
                    "Eine Seite aus einem alten Band, zweimal gefaltet. Das Siegel der Halle hält sie zu. Nur für Fenna.");
            }
            finally
            {
                _refresh = keep;
            }
        }
    }
}
