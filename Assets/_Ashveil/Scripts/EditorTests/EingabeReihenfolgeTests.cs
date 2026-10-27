using System;
using NUnit.Framework;
using UnityEngine;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// EINSTIEG-01 (21.10.2026): Getippte Zeichen liegen in einem gemeinsamen Puffer (RawInput.ConsumeTypedText leert ihn).
    /// Der Charakter-Editor muss ihn vor dem Chat (SocialUi) abholen, sonst bleibt das Namensfeld bei echter Tastatur leer.
    /// Ohne Szene, ohne Play; die UI-Typen werden über den Namen gesucht (dieses Assembly verweist nicht auf Ashveil.UI).
    /// </summary>
    public sealed class EingabeReihenfolgeTests
    {
        private static int Reihenfolge(string typName)
        {
            Type t = Type.GetType(typName + ", Ashveil.UI");
            Assert.IsNotNull(t, typName + " nicht gefunden");
            var a = (DefaultExecutionOrder)Attribute.GetCustomAttribute(t, typeof(DefaultExecutionOrder));
            Assert.IsNotNull(a, typName + " ohne DefaultExecutionOrder");
            return a.order;
        }

        [Test]
        public void Charakter_Editor_liest_getippte_Zeichen_vor_dem_Chat()
        {
            int editor = Reihenfolge("Ashveil.UI.CharacterCreatorUi");
            int chat = Reihenfolge("Ashveil.UI.SocialUi");
            Assert.Less(editor, chat, "Editor muss vor SocialUi laufen, sonst leert der Chat den Zeichenpuffer");
        }

        [Test]
        public void Charakter_Editor_laeuft_nach_den_Fenstern_die_OverUi_zuruecksetzen()
        {
            int editor = Reihenfolge("Ashveil.UI.CharacterCreatorUi");
            Assert.Greater(editor, Reihenfolge("Ashveil.UI.InventoryUi"), "InventoryUi setzt UiPointer.OverUi zurück");
            Assert.Greater(editor, Reihenfolge("Ashveil.UI.QuestUi"));
        }
    }
}
