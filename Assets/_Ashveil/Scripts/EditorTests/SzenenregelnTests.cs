using Ashveil.Data;
using Ashveil.Gameplay.Quests;
using Ashveil.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// Kareth v4, Welle 0 (#27 Schritt 3b): Szenenregeln ohne Weltzellen – gespeicherte Position gilt nur in derselben Szene und
    /// Weltversion; der Auftragsfilter versteckt „Salz und Schweigen“ (Q6, Salzgruben erst in Welle 1) und nur diesen.
    /// </summary>
    public class SzenenregelnTests
    {
        [Test]
        public void Gespeicherte_Stelle_gilt_nur_in_derselben_Szene_und_Weltversion()
        {
            var go = new GameObject("Test Positionsschluessel");
            try
            {
                ScenePositionKey key = go.AddComponent<ScenePositionKey>();
                key.Configure("kareth_v4", 4242);
                WorldPosition wp = key.Capture(new Vector3(12f, 3f, -40f), 90f);
                Assert.AreEqual("szene:kareth_v4", wp.cellId);
                Assert.IsTrue(key.Accepts(wp), "eigene Stelle");
                WorldPosition alt = wp;
                alt.worldVersion = 4241;
                Assert.IsFalse(key.Accepts(alt), "Gelände neu gebaut: Start am Bindepunkt");
                WorldPosition zelle = wp;
                zelle.cellId = "kareth";
                Assert.IsFalse(key.Accepts(zelle), "Stelle aus der Sandbox (Weltzelle)");
                Assert.IsFalse(key.Accepts(default), "keine Stelle");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Auftragsfilter_versteckt_nur_Salz_und_Schweigen()
        {
            var q6 = AssetDatabase.LoadAssetAtPath<QuestDefinition>("Assets/_Ashveil/Data/Quests/Quest_06_SalzUndSchweigen.asset");
            var q1 = AssetDatabase.LoadAssetAtPath<QuestDefinition>("Assets/_Ashveil/Data/Quests/Quest_01_KlingeFuerDieWacht.asset");
            Assert.IsNotNull(q6, "Q6-Asset");
            Assert.IsNotNull(q1, "Q1-Asset");
            var go = new GameObject("Test Auftragsfilter");
            try
            {
                go.AddComponent<SceneQuestFilter>().Configure("Noch nicht verfügbar.", "quest.salt_and_silence");
                Assert.AreEqual("Noch nicht verfügbar.", QuestRules.CanAccept(q6, 20, _ => false, _ => false, 0), "Q6 in Kareth v4");
                Assert.IsFalse(QuestRules.Offered(q6, q6.giverNpc, 20, _ => false, _ => false), "Bralt bietet Q6 nicht an");
                Assert.IsNull(QuestRules.CanAccept(q1, 20, _ => false, _ => false, 0), "Q1 bleibt");
                Assert.AreEqual("Auftrag läuft bereits.", QuestRules.CanAccept(q6, 20, _ => false, id => id == q6.Id, 0), "laufender Q6 bleibt unberührt");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
