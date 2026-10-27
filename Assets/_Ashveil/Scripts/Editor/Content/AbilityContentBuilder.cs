using Ashveil.Data;
using UnityEditor;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// M6.6 – Faehigkeiten fuer Actionbar und Faehigkeitenbuch. Heute die fuenf Waffen-/Bewegungsaktionen, die es
    /// schon gab (Angriff, Spezial, Hechtrolle, Waffe wechseln, Nachladen). Zauber folgen spaeter im selben Format.
    /// Standardwerte werden nur beim ersten Anlegen geschrieben – danach im Inspector frei aenderbar.
    /// </summary>
    public static class AbilityContentBuilder
    {
        public const string Folder = ProjectSetup.Root + "/Data/Abilities";

        public static void Build()
        {
            ProjectSetup.EnsureFolder(Folder);
            Ability("Ability_AutoAttack", ActionBarLayout.AutoAttack, "Angriff", AbilityKind.AutoAttack, IconShape.Sword, new Color(0.85f, 0.82f, 0.74f), true, 0,
                "Greift das Ziel mit der aktiven Waffe an, bis du etwas anderes tust (Nahkampf: Schlagfolge, Fernkampf: Schüsse).");
            Ability("Ability_WeaponSpecial", ActionBarLayout.WeaponSpecial, "Waffenfähigkeit", AbilityKind.WeaponSpecial, IconShape.Axe, new Color(1f, 0.6f, 0.25f), true, 1,
                "Die besondere Fähigkeit der aktiven Waffe (in der Luft: Sturzangriff). Abklingzeit je Waffe.");
            Ability("Ability_Dodge", ActionBarLayout.Dodge, "Hechtrolle", AbilityKind.Dodge, IconShape.Boots, new Color(0.55f, 0.8f, 0.95f), false, 2,
                "Rolle in Laufrichtung. Weicht Treffern, Stampfern und Einschlägen aus. Kostet Ausdauer.");
            Ability("Ability_SwapWeapon", ActionBarLayout.SwapWeapon, "Waffe wechseln", AbilityKind.SwapWeapon, IconShape.Shield, new Color(0.75f, 0.75f, 0.8f), false, 3,
                "Zwischen Nahkampf- und Fernkampfwaffe wechseln (auch mit F).");
            Ability("Ability_Reload", ActionBarLayout.Reload, "Nachladen", AbilityKind.Reload, IconShape.Bullet, new Color(0.9f, 0.75f, 0.35f), false, 4,
                "Feuerwaffe nachladen (auch mit R).");
            AssetDatabase.SaveAssets();
        }

        private static void Ability(string file, string id, string name, AbilityKind kind, IconShape shape, Color color, bool target, int order, string description)
        {
            string path = Folder + "/" + file + ".asset";
            var def = AssetDatabase.LoadAssetAtPath<AbilityDefinition>(path);
            if (def != null) return;
            def = ScriptableObject.CreateInstance<AbilityDefinition>();
            def.kind = kind;
            def.iconShape = shape;
            def.iconColor = color;
            def.requiresTarget = target;
            def.sortOrder = order;
            AssetDatabase.CreateAsset(def, path);
            K.SetString(def, "id", id);
            K.SetString(def, "displayName", name);
            K.SetString(def, "description", description);
            EditorUtility.SetDirty(def);
        }
    }
}
