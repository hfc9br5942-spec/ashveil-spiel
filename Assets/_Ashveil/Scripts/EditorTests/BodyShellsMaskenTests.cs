using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// 22.10.2026 (Codex Paket 2 Bänder): die Hautmaske hängt nur an dem, was die Abdeckung wirklich bestimmt. Zwei Schalen mit gleicher
    /// Region/Höhe, aber anderem Abstand zur Haut, verdecken dieselben Dreiecke → dieselbe Masken-Instanz; eine andere Höhengrenze → andere Maske.
    /// </summary>
    public sealed class BodyShellsMaskenTests
    {
        private GameObject _g;

        [TearDown]
        public void TearDown()
        {
            if (_g != null) Object.DestroyImmediate(_g);
        }

        private BodyShells Aufbauen()
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Ashveil/Art/Characters/Bodies/CHR_M_TR01.fbx");
            if (p == null) Assert.Ignore("Tripo-Körper fehlt");
            _g = Object.Instantiate(p);
            SkinnedMeshRenderer koerper = null;
            foreach (SkinnedMeshRenderer s in _g.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (s.sharedMesh != null && (koerper == null || s.sharedMesh.vertexCount > koerper.sharedMesh.vertexCount)) koerper = s;
            if (koerper == null) Assert.Ignore("kein Körpernetz");
            BodyShells b = BodyShells.For(koerper, _g.transform);
            if (b == null || !b.Valid) Assert.Ignore("BodyShells nicht gültig");
            return b;
        }

        /// <summary>
        /// 22.10.2026 (Codex Paket 2): ein ungespiegeltes Band am linken Oberschenkel nimmt nur Haut der linken Seite (oder Mitte) auf –
        /// nie Dreiecke des rechten Beins, auch wenn es radial nahe liegt.
        /// </summary>
        [Test]
        public void Einseitiges_Band_nimmt_keine_Haut_der_Gegenseite()
        {
            BodyShells b = Aufbauen();
            SkinnedMeshRenderer koerper = null;
            foreach (SkinnedMeshRenderer s in _g.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (s.sharedMesh != null && (koerper == null || s.sharedMesh.vertexCount > koerper.sharedMesh.vertexCount)) koerper = s;
            var teil = new RigidPart { shape = ProcShape.Cylinder, bone = "thigh_l", size = new Vector3(0.2f, 0.06f, 0.2f), offset = Vector3.zero };
            if (!b.Band(teil, false, 0.006f, out ShellLayer band)) Assert.Ignore("kein Band am Oberschenkel");
            Assert.AreEqual(-1, band.bandSide, "Band am linken Knochen");
            Mesh m = b.Shell(band);
            Assert.IsNotNull(m, "Bandschale");
            BoneWeight[] w = m.boneWeights;
            Transform[] bones = koerper.bones;
            int links = 0, rechts = 0;
            foreach (int v in m.triangles)
            {
                BoneWeight bw = w[v];
                int best = bw.boneIndex0; float bestW = bw.weight0;
                if (bw.weight1 > bestW) { best = bw.boneIndex1; bestW = bw.weight1; }
                if (bw.weight2 > bestW) { best = bw.boneIndex2; bestW = bw.weight2; }
                if (bw.weight3 > bestW) best = bw.boneIndex3;
                string n = SkelettNamen.Kanonisch(bones[best].name);
                if (n.EndsWith("_l")) links++; else if (n.EndsWith("_r")) rechts++;
            }
            Assert.Greater(links, 0, "Band liegt am linken Bein");
            Assert.AreEqual(0, rechts, "keine Haut des rechten Beins im linken Band");
        }

        private static ShellLayer Schale(float yMin, float yMax, float offset) =>
            new ShellLayer { regions = BodyRegion.Chest, yMin = yMin, yMax = yMax, offset = offset, hideBody = true };

        [Test]
        public void Gleiche_Abdeckung_teilt_eine_Maske()
        {
            BodyShells b = Aufbauen();
            Mesh m1 = b.Masked(new List<ShellLayer> { Schale(0.55f, 0.8f, 0.006f) });
            Mesh m2 = b.Masked(new List<ShellLayer> { Schale(0.55f, 0.8f, 0.008f) });
            Mesh m3 = b.Masked(new List<ShellLayer> { Schale(0.55f, 0.8f, 0.006f), Schale(0.55f, 0.8f, 0.012f) });
            Assert.AreSame(m1, m2, "anderer Abstand, gleiche Abdeckung → dieselbe Maske");
            Assert.AreSame(m1, m3, "doppelte gleiche Abdeckung wird nicht als neue Kombination gezählt");
            Mesh m4 = b.Masked(new List<ShellLayer> { Schale(0.6f, 0.8f, 0.006f) });
            Assert.AreNotSame(m1, m4, "andere Höhengrenze → andere Maske");
        }
    }
}
