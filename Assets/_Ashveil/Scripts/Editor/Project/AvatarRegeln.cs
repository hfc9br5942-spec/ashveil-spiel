using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Gemeinsame Avatar-Regel fuer alle Humanoid-Figuren und Bewegungsdateien (15.10.2026, Mocap-Asymmetrie-Suche):
    /// Unity verteilt die Laengsdrehung („Twist“) von Oberschenkel/Oberarm standardmaessig zur Haelfte auf das naechste
    /// Glied (Upper/Lower Leg/Arm Twist 0,5). Bei gebeugtem Knie schwenkt das den Unterschenkel um die Oberschenkelachse –
    /// der Fuss wanderte seitlich (Joggen: 40 cm Fuss-Spreizung in Unity statt 23 cm in Blender, Standfuss rutschte).
    /// Darum: keine Verteilung (1,0) – jede Drehung bleibt in dem Knochen, in dem sie aufgenommen wurde. Gilt fuer die
    /// Spielfiguren (Quaternius, eigene Koerper, Voelker) und die CMU-Clips gleichermassen, sonst passen Avatar und Clip
    /// nicht zusammen.
    /// </summary>
    public static class AvatarRegeln
    {
        public static void OhneVerdrehungsverteilung(ref HumanDescription d)
        {
            d.upperArmTwist = 1f; d.lowerArmTwist = 1f; d.upperLegTwist = 1f; d.lowerLegTwist = 1f;
        }

        /// <summary>Auf einen Importer anwenden – nur wenn schon eine Knochenzuordnung existiert (sonst wuerde Unity die
        /// automatische Zuordnung beim ersten Import ueberspringen).</summary>
        public static void OhneVerdrehungsverteilung(ModelImporter mi)
        {
            HumanDescription d = mi.humanDescription;
            if (d.human == null || d.human.Length == 0) return;
            if (Mathf.Approximately(d.upperLegTwist, 1f) && Mathf.Approximately(d.lowerLegTwist, 1f) && Mathf.Approximately(d.upperArmTwist, 1f) && Mathf.Approximately(d.lowerArmTwist, 1f)) return;
            OhneVerdrehungsverteilung(ref d);
            mi.humanDescription = d;
        }
    }
}
