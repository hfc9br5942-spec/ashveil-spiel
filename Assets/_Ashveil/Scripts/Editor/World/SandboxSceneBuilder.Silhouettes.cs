using System.Collections.Generic;
using Ashveil.Gameplay.Npcs;
using Ashveil.World;
using UnityEditor;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Art-Pass 4 (Art-Direction-Bible § 29–39, § 77): Referenzsilhouetten auf einer eigenen Buehne im Westteil des
    /// Aschenfelds (eben, Blick nach Osten auf die Felswand). Nur grosse Formen, grau-dunkle Blockouts aus dem
    /// Aussehen-System (appearances.json, Schluessel "ref_…"). Standardmaessig ausgeblendet – der Silhouettentest
    /// (Ashveil › Test › Figuren-Silhouetten) schaltet sie ein. Die neuen Basiskoerper stehen hier immer, auch wenn sie im
    /// Spiel noch ausgeschaltet sind.
    /// </summary>
    public static partial class SandboxSceneBuilder
    {
        public static readonly Vector2 SilhouetteStagePos = new Vector2(215f, 120f);

        private struct RefFigure
        {
            public string Name, Path, Look;
            public bool Female;
            public string[] Gear;
        }

        private static void BuildSilhouetteStage(Transform env)
        {
            if (!CharacterBodies.NewBodiesAvailable)
            {
                Debug.Log("[Ashveil] Silhouetten-Buehne ausgelassen: neue Basiskoerper noch nicht erzeugt.");
                return;
            }
            string qm = QuaterniusImport.BaseCharactersImported ? QuaterniusImport.BaseMalePath : QuaterniusImport.MaleModelPath;
            string qf = QuaterniusImport.BaseCharactersImported ? QuaterniusImport.BaseFemalePath : QuaterniusImport.FemaleModelPath;
            string[] aschewachtShared = { "appearance.ref_aschewacht_hands", "appearance.ref_aschewacht_feet", "appearance.ref_aschewacht_belt",
                                          "appearance.ref_eidwaechter_langschwert" };
            var list = new[]
            {
                new RefFigure { Name = "Basis Mann", Path = CharacterBodies.NewMalePath, Look = "1|b0|s1|h0|c1|a0|e2|m0", Gear = new string[0] },
                new RefFigure { Name = "Basis Frau", Path = CharacterBodies.NewFemalePath, Female = true, Look = "1|b1|s1|h0|c2|a0|e5|m0", Gear = new string[0] },
                new RefFigure
                {
                    Name = "Cael", Path = CharacterBodies.NewMalePath, Look = "1|b0|s1|h2|c1|d1|a1|e2|m0",
                    Gear = Concat(new[] { "appearance.ref_aschewacht_chest_m", "appearance.ref_aschewacht_shoulders_m", "appearance.ref_aschewacht_legs_m" }, aschewachtShared)
                },
                new RefFigure
                {
                    Name = "Elara", Path = CharacterBodies.NewFemalePath, Female = true, Look = "1|b1|s1|h7|c2|a0|e5|m0",
                    Gear = Concat(new[] { "appearance.ref_aschewacht_chest_f", "appearance.ref_aschewacht_shoulders_f", "appearance.ref_aschewacht_legs_f" }, aschewachtShared)
                },
                new RefFigure
                {
                    Name = "Seris", Path = CharacterBodies.NewFemalePath, Female = true, Look = "1|b1|s0|h5|c0|a0|e3|m0",
                    Gear = new[] { "appearance.ref_schleier_chest_f", "appearance.ref_schleier_legs_f", "appearance.ref_schleier_belt_f",
                                   "appearance.ref_schleier_hands_f", "appearance.ref_schleier_feet_f", "appearance.ref_aschenweber_fokus" }
                },
                new RefFigure { Name = "Quaternius Mann", Path = qm, Look = "1|b0|s1|h0|c1|a0|e2|m0", Gear = new string[0] },
                new RefFigure { Name = "Quaternius Frau", Path = qf, Female = true, Look = "1|b1|s1|h0|c2|a0|e5|m0", Gear = new string[0] },
            };

            Vector3 center = G(SilhouetteStagePos.x, SilhouetteStagePos.y);
            Transform stage = K.Group("Silhouetten-Buehne (Art-Test)", env, center, new Vector3(0f, -90f, 0f)); // Figuren schauen nach Westen
            Transform figuresRoot = K.Group("Figuren", stage);
            var male = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerAnimatorBuilder.ControllerPath);
            var female = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerAnimatorBuilder.FemaleOverridePath) ?? male;
            var figures = new List<Transform>();
            var names = new List<string>();
            const float spacing = 1.5f;
            for (int i = 0; i < list.Length; i++)
            {
                RefFigure f = list[i];
                float x = (i - (list.Length - 1) * 0.5f) * spacing;
                Vector3 world = stage.TransformPoint(new Vector3(x, 0f, 0f));
                world.y = KarethTerrainBuilder.Height(world.x, world.z);
                Transform root = K.Group("Figur " + f.Name, figuresRoot, new Vector3(x, world.y - center.y, 0f), Vector3.zero); // lokal zur Buehne
                Transform visual = K.Group("Visual", root);
                GameObject model = PlayerRigBuilder.InstantiateModel(f.Path, "Model", visual, f.Female ? female : male);
                if (model == null) continue;
                PlayerRigBuilder.AddStance(model, PlayerRigBuilder.EnsureStance(f.Female ? PlayerRigBuilder.FemaleStancePath : PlayerRigBuilder.MaleStancePath, f.Female));
                model.AddComponent<NpcBody>().Configure(f.Look, f.Gear, root);
                figures.Add(root);
                names.Add(f.Name);
            }
            figuresRoot.gameObject.SetActive(false);
            stage.gameObject.AddComponent<SilhouetteStage>().Configure(figuresRoot.gameObject, figures, names);
        }

        private static string[] Concat(string[] a, string[] b)
        {
            var r = new string[a.Length + b.Length];
            a.CopyTo(r, 0);
            b.CopyTo(r, a.Length);
            return r;
        }
    }
}
