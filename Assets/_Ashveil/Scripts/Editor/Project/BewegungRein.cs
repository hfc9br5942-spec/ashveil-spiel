using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Neustart Bewegung Menschen (18.10.2026, Max: „die bewegungen ansich sind nicht sauber“, Klick „100STYLE“, „Gehen langsamer“):
    /// reine Bewegungsaufnahmen ohne eigene Übertragung und ohne Korrekturschichten. Die BVH-Schleifen (100STYLE CC BY 4.0,
    /// CMU) schneidet Tools/Mocap/style100_schleife.py, Blender macht daraus FBX mit dem Original-Skelett
    /// (Tools/Blender/ashveil_bvh_rein_fbx.py). Unity überträgt sie per Humanoid (Mecanim) auf Mann und Frau – der übliche Weg
    /// in professionellen Studios. Dieser Importer gibt jedem Quell-Skelett seine feste Knochenzuordnung.
    /// </summary>
    public sealed class BewegungReinImport : AssetPostprocessor
    {
        public const string Ordner = "Assets/_Ashveil/Animations/Mocap/Rein";
        public override uint GetVersion() => 1;   // NICHT erhöhen (Neu-Import außerhalb des Importschutzes kann Unity abstürzen lassen) – neu exportieren genügt
        private bool Hier => assetPath.Replace('\\', '/').StartsWith(Ordner + "/") && assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);

        // 100STYLE (Mason/Starke/Komura): Hips · Chest…Chest4 · Neck · Head · Left/Right Collar/Shoulder/Elbow/Wrist · Hip/Knee/Ankle/Toe
        private static readonly (string knochen, string human)[] Style100 =
        {
            ("Hips", "Hips"), ("Chest", "Spine"), ("Chest2", "Chest"), ("Chest4", "UpperChest"), ("Neck", "Neck"), ("Head", "Head"),
            ("LeftCollar", "LeftShoulder"), ("LeftShoulder", "LeftUpperArm"), ("LeftElbow", "LeftLowerArm"), ("LeftWrist", "LeftHand"),
            ("RightCollar", "RightShoulder"), ("RightShoulder", "RightUpperArm"), ("RightElbow", "RightLowerArm"), ("RightWrist", "RightHand"),
            ("LeftHip", "LeftUpperLeg"), ("LeftKnee", "LeftLowerLeg"), ("LeftAnkle", "LeftFoot"), ("LeftToe", "LeftToes"),
            ("RightHip", "RightUpperLeg"), ("RightKnee", "RightLowerLeg"), ("RightAnkle", "RightFoot"), ("RightToe", "RightToes"),
        };
        // CMU-Aufnahmen liegen seit 18.10. ebenfalls auf dem 100STYLE-Skelett (Tools/Mocap/cmu_zu_s100.py): Mit dem eigenen
        // CMU-Skelett (Hüftgelenke und Lendenwirbel mit Länge 0 im Wurzelpunkt) lehnte Unity die Figur beim Rennen nach hinten.

        private void OnPreprocessModel()
        {
            if (!Hier) return;
            var mi = (ModelImporter)assetImporter;
            string datei = Path.GetFileNameWithoutExtension(assetPath);
            var tabelle = Style100;   // alle reinen Aufnahmen nutzen das 100STYLE-Skelett
            mi.globalScale = 1f; mi.useFileScale = true; mi.bakeAxisConversion = true;
            mi.importAnimation = true; mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            var d = new HumanDescription
            {
                human = tabelle.Select(t => new HumanBone { boneName = t.knochen, humanName = t.human, limit = new HumanLimit { useDefaultValues = true } }).ToArray(),
                skeleton = new SkeletonBone[0],   // Ruhelage (T-Pose, Bild 0) nimmt Unity aus der Datei
                armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
            };
            AvatarRegeln.OhneVerdrehungsverteilung(ref d);   // wie Körper und alle Clips (15.10.)
            mi.humanDescription = d;
            mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importCameras = false; mi.importLights = false;
        }

        private void OnPreprocessAnimation()
        {
            if (!Hier) return;
            var mi = (ModelImporter)assetImporter;
            var takes = mi.importedTakeInfos;
            if (takes == null || takes.Length == 0) return;
            string datei = Path.GetFileNameWithoutExtension(assetPath);
            var clips = takes.Select(t => new ModelImporterClipAnimation
            {
                name = datei, takeName = t.name,
                firstFrame = Mathf.Round(t.startTime * t.sampleRate), lastFrame = Mathf.Round(t.stopTime * t.sampleRate),
                // Schleife ist in den Daten schon nahtlos (letztes Bild = erstes Bild) – Unity soll nichts angleichen
                loopTime = true, loopPose = false,
                // Am Platz: Drehung, Höhe und Seitenpendeln der Hüfte bleiben wie aufgenommen (in die Pose gebacken)
                lockRootRotation = true, keepOriginalOrientation = true,
                lockRootHeightY = true, keepOriginalPositionY = true, heightFromFeet = false,
                lockRootPositionXZ = true, keepOriginalPositionXZ = true,
            }).ToArray();
            mi.clipAnimations = clips;
            mi.animationCompression = ModelImporterAnimationCompression.Off;   // Lehre 16.10.: keine Kurvenreduktion an der Naht
            mi.resampleCurves = true;
        }
    }
}
