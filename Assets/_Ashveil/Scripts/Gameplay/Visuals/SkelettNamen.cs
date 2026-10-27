using System.Collections.Generic;

namespace Ashveil.Gameplay.Visuals
{
    /// <summary>
    /// 21.10.2026 (Max: „Übernehme bitte die Logik, die Mixamo hat“): Körper aus Tripo behalten ihr Auto-Rig 1:1 – Frau mit
    /// Mixamo-Namen (mixamorig:*), Mann mit Unity-Humanoid-Namen (Hips, Left_UpperArm, Left_IndexProximal …). Inhalte und Code
    /// sprechen weiter unsere Namen (pelvis, spine_01, upperarm_l, Head …) – diese Tabelle übersetzt beide. Unbekannte Namen
    /// bleiben, wie sie sind (MPFB/Quaternius heißen bereits so).
    /// </summary>
    public static class SkelettNamen
    {
        private const string Mixamo = "mixamorig:";
        private static readonly Dictionary<string, string> Fest = new Dictionary<string, string>
        {
            { "Hips", "pelvis" }, { "Spine", "spine_01" }, { "Spine1", "spine_02" }, { "Spine2", "spine_03" },
            { "Neck", "neck_01" }, { "Head", "Head" }, { "HeadTop_End", "head_leaf" },
        };
        private static readonly Dictionary<string, string> Glied = new Dictionary<string, string>
        {
            { "Shoulder", "clavicle" }, { "Arm", "upperarm" }, { "ForeArm", "lowerarm" }, { "Hand", "hand" },
            { "UpLeg", "thigh" }, { "Leg", "calf" }, { "Foot", "foot" }, { "ToeBase", "ball" }, { "Toe_End", "ball_leaf" },
        };
        private static readonly string[] Finger = { "Index", "Middle", "Ring", "Pinky", "Thumb" };

        // Unity-Humanoid-Namen (Tripo-Export „Unity“): ohne Präfix, Seite als „Left_“/„Right_“
        private static readonly Dictionary<string, string> UnityFest = new Dictionary<string, string>
        {
            { "Hips", "pelvis" }, { "Spine", "spine_01" }, { "Chest", "spine_02" }, { "UpperChest", "spine_03" },
            { "Neck", "neck_01" }, { "Head", "Head" },
        };
        private static readonly Dictionary<string, string> UnityGlied = new Dictionary<string, string>
        {
            { "Shoulder", "clavicle" }, { "UpperArm", "upperarm" }, { "LowerArm", "lowerarm" }, { "Hand", "hand" },
            { "UpperLeg", "thigh" }, { "LowerLeg", "calf" }, { "Foot", "foot" }, { "Toes", "ball" }, { "ToesEnd", "ball_leaf" },
        };
        private static readonly (string glied, string nr)[] UnityFingerGlied =
            { ("Proximal", "_01"), ("Intermediate", "_02"), ("DistalEnd", "_04_leaf"), ("Distal", "_03") };

        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>();

        /// <summary>Unser Name für einen Knochennamen (Mixamo/Unity → Ashveil); sonst der Name selbst.</summary>
        public static string Kanonisch(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (Cache.TryGetValue(name, out string c)) return c;
            c = Uebersetzen(name) ?? UebersetzenUnity(name) ?? name;
            Cache[name] = c;
            return c;
        }

        private static string Uebersetzen(string name)
        {
            // Unity/FBX schreibt den Namensraum manchmal mit '_' statt ':'
            string n = name.StartsWith(Mixamo) ? name.Substring(Mixamo.Length)
                : name.StartsWith("mixamorig_") ? name.Substring("mixamorig_".Length) : null;
            if (n == null) return null;
            if (Fest.TryGetValue(n, out string f)) return f;
            string seite = n.StartsWith("Left") ? "_l" : n.StartsWith("Right") ? "_r" : null;
            if (seite == null) return null;
            string rest = n.Substring(seite == "_l" ? 4 : 5);
            if (Glied.TryGetValue(rest, out string g)) return g + seite;
            foreach (string finger in Finger)
            {
                string k = "Hand" + finger;
                if (!rest.StartsWith(k) || !int.TryParse(rest.Substring(k.Length), out int i)) continue;
                return finger.ToLowerInvariant() + "_0" + i + (i == 4 ? "_leaf" : "") + seite;
            }
            return null;
        }

        private static string UebersetzenUnity(string name)
        {
            if (UnityFest.TryGetValue(name, out string f)) return f;
            string seite = name.StartsWith("Left_") ? "_l" : name.StartsWith("Right_") ? "_r" : null;
            if (seite == null) return null;
            string rest = name.Substring(seite == "_l" ? 5 : 6);
            if (UnityGlied.TryGetValue(rest, out string g)) return g + seite;
            foreach (string finger in Finger)
            {
                if (!rest.StartsWith(finger)) continue;
                string teil = rest.Substring(finger.Length);
                foreach ((string glied, string nr) in UnityFingerGlied)
                    if (teil == glied) return finger.ToLowerInvariant() + nr + seite;
            }
            return null;   // z. B. Left_Eye, Neck_Twist_A – bleiben, wie sie sind
        }
    }
}
