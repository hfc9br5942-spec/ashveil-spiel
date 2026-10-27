using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.EditorTools;
using Ashveil.Gameplay.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTests
{
    public sealed class AppearanceEyeTests
    {
        private GameObject _instance;

        private static IEnumerable<TestCaseData> BodyAndEyeColors()
        {
            // Die Frau hat noch keine separaten Augaepfel. Dieser Test belegt ausschliesslich den vorhandenen Mann.
            for (int eye = 0; eye < 8; eye++)
                yield return new TestCaseData(0, eye).SetName($"MPFB_Augen_Mann_Farbe_{eye}");
        }

        [TearDown]
        public void TearDown()
        {
            if (_instance != null) Object.DestroyImmediate(_instance);
        }

        [TestCaseSource(nameof(BodyAndEyeColors))]
        public void Auswahl_setzt_die_passende_Textur_am_echten_Augenrenderer_und_meldet_sie(int body, int eye)
        {
            AppearanceCatalog catalog = AssetDatabase.LoadAssetAtPath<AppearanceCatalog>(AppearanceContentBuilder.CatalogPath);
            Assert.IsNotNull(catalog, "Vorhandener Aussehen-Katalog muss geladen werden.");
            Assert.Greater(catalog.eyeColors.Count, eye);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(body == 0 ? CharacterBodies.MpfbMalePath : CharacterBodies.MpfbFemalePath);
            Assert.IsNotNull(model, "Die gegenwaertigen MPFB-Modelle muessen vorhanden sein.");
            Texture2D expected = Resources.Load<Texture2D>("Augen/T_Augen_MPFB_" + eye);
            Assert.IsNotNull(expected, "Jede angebotene Augenfarbe braucht eine MPFB-Textur.");

            _instance = Object.Instantiate(model);
            SkinnedMeshRenderer eyes = null;
            foreach (SkinnedMeshRenderer candidate in _instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (candidate.name.ToLowerInvariant().EndsWith("_augen")) eyes = candidate;
            Assert.IsNotNull(eyes, "Das echte Modell muss seinen separaten Augenrenderer besitzen.");
            var rig = _instance.AddComponent<AppearanceRig>();
            rig.Init(_instance.transform);
            Assert.IsTrue(rig.Ready);
            rig.Apply(null, new CharacterAppearanceData { body = body, eyeColor = eye, hair = 0, beard = 0 }, catalog);

            Material material = eyes.sharedMaterial;
            Assert.IsNotNull(material);
            Texture actual = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : material.mainTexture;
            Assert.AreSame(expected, actual, "Prueft die sichtbare Materialzuweisung, nicht nur einen gespeicherten Index.");
            Assert.AreEqual(expected.name, rig.EyeTextureName, "Die Diagnose muss dasselbe Material wie der Augenrenderer auslesen.");
        }
    }
}
