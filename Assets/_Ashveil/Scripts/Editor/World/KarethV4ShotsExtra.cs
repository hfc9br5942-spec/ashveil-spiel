using System;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Kareth v4 – Review-Bilder in einen eigenen Unterordner mit Zeitstempel (Logs/Ashveil/KarethV4/Lauf_JJJJ-MM-TT_HHMM),
    /// damit Vergleiche (Sonne, Felsen, Licht) keine älteren Bilder überschreiben. Nutzt <see cref="KarethV4Shots.Render(string)"/>.
    /// </summary>
    public static class KarethV4ShotsExtra
    {
        [MenuItem("Ashveil/Welt/Kareth v4: Review-Bilder (neuer Unterordner mit Zeitstempel)", priority = 564)]
        public static void RenderDated()
        {
            string sub = "Lauf_" + DateTime.Now.ToString("yyyy-MM-dd_HHmm");
            string dir = KarethV4Shots.Render(sub);
            if (dir != null) Debug.Log("[Ashveil] Kareth v4: Review-Bilder in " + dir);
        }
    }
}
