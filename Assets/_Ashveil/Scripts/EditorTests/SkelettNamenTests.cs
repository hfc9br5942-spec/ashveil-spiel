using Ashveil.Data;
using Ashveil.Gameplay.Visuals;
using NUnit.Framework;

namespace Ashveil.EditorTests
{
    /// <summary>21.10.2026: Mixamo-Knochen (Tripo-Körper CHR_W_TR01) werden auf unsere Namen übersetzt.</summary>
    public class SkelettNamenTests
    {
        [TestCase("mixamorig:Hips", "pelvis")]
        [TestCase("mixamorig:Spine2", "spine_03")]
        [TestCase("mixamorig:Head", "Head")]
        [TestCase("mixamorig:LeftShoulder", "clavicle_l")]
        [TestCase("mixamorig:RightForeArm", "lowerarm_r")]
        [TestCase("mixamorig:LeftUpLeg", "thigh_l")]
        [TestCase("mixamorig:RightToeBase", "ball_r")]
        [TestCase("mixamorig:LeftHandIndex1", "index_01_l")]
        [TestCase("mixamorig:RightHandThumb4", "thumb_04_leaf_r")]
        [TestCase("mixamorig_LeftHand", "hand_l")]
        [TestCase("Hips", "pelvis")]
        [TestCase("Chest", "spine_02")]
        [TestCase("UpperChest", "spine_03")]
        [TestCase("Left_UpperArm", "upperarm_l")]
        [TestCase("Right_LowerLeg", "calf_r")]
        [TestCase("Left_Toes", "ball_l")]
        [TestCase("Left_IndexProximal", "index_01_l")]
        [TestCase("Right_ThumbDistal", "thumb_03_r")]
        [TestCase("Right_PinkyDistalEnd", "pinky_04_leaf_r")]
        [TestCase("Left_Eye", "Left_Eye")]
        [TestCase("Neck_Twist_A", "Neck_Twist_A")]
        [TestCase("upperarm_l", "upperarm_l")]
        [TestCase("Grip", "Grip")]
        public void Uebersetzt(string mixamo, string erwartet) => Assert.AreEqual(erwartet, SkelettNamen.Kanonisch(mixamo));

        [Test]
        public void BereicheWieUnsereKnochen()
        {
            Assert.AreEqual(BodyRegion.Pelvis, BodyShells.RegionOfBone(SkelettNamen.Kanonisch("mixamorig:Hips")));
            Assert.AreEqual(BodyRegion.Thighs, BodyShells.RegionOfBone(SkelettNamen.Kanonisch("mixamorig:RightUpLeg")));
            Assert.AreEqual(BodyRegion.Hands, BodyShells.RegionOfBone(SkelettNamen.Kanonisch("mixamorig:LeftHandPinky2")));
            Assert.AreEqual(BodyRegion.Head, BodyShells.RegionOfBone(SkelettNamen.Kanonisch("mixamorig:HeadTop_End")));
        }
    }
}
