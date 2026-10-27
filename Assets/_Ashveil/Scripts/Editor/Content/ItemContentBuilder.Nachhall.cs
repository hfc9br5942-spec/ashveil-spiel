using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using UnityEditor;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Legendaeres Schwert „Nachhall“, Revision E (Max 04.10.2026, Master ITEM-096 [PROVISIONAL]):
    ///   · zwei verwandte Klingen derselben Herkunft – Kurzschwert (Einhand, Eidwaechter + Aschenjaeger-Haupthand) und
    ///     echter Zweihaender (Eidwaechter); am Ende derselben Questreihe waehlt jeder Spieler eine (persoenliche Belohnung)
    ///   · Grundwerte aus dem EPISCHEN Waffenbudget (<see cref="WeaponBudget"/>), nicht darueber – die Besonderheit ist die
    ///     legendaere Eigenschaft „Nachhall“ (<see cref="WeaponEcho"/>, nur Server)
    ///   · Gegenstandsstufe 20 / Mindeststufe 18 und alle Nachhall-Zahlen sind PLATZHALTER bis zur Questreihe und zum
    ///     Balancepass. Bewusst in keiner Beutetabelle, bei keinem Haendler und in keiner Startausruestung.
    ///   · Aussehen: das Spielmodell (LOD0–2) aus Art/Entwurf/Nachhall am Griff-Knochen – nicht in appearances.json,
    ///     damit der JSON-Abgleich es nicht ueberschreibt.
    /// </summary>
    public static partial class ItemContentBuilder
    {
        /// <summary>Zweihandschwert (Tempo 1,3) relativ zu <see cref="Balance.WeaponAverage"/>: kalibriert an Glockenlaeuterin
        /// (×1,40, legendaer) und Herzkristallklinge (×1,52, episch); fuer Nachhall die untere Referenz.</summary>
        private const float TwoHandSwordFactor = 1.4f;

        private const int NachhallItemLevel = 20, NachhallRequiredLevel = 18;   // Platzhalter (Questreihe offen)
        private const string NachhallPrefabDir = "Assets/_Ashveil/Art/Entwurf/Nachhall/Prefabs";
        private static readonly Color NachhallSilver = new Color(0.66f, 0.72f, 0.80f);

        private static void BuildNachhallItems()
        {
            Vector2 shortDmg = WeaponBudget(NachhallItemLevel, ItemQuality.Epic, SwordFactor);
            Vector2 greatDmg = WeaponBudget(NachhallItemLevel, ItemQuality.Epic, TwoHandSwordFactor);

            WeaponDefinition shortW = Weapon("Weapon_NachhallShort", "weapon.nachhall_short", "Nachhall",
                w =>
                {
                    Melee(w, WeaponClass.Sword, shortDmg.x, shortDmg.y, 2.4f, 1f, "Widerhall");
                    w.quality = ItemQuality.Legendary;
                    w.hasAirAttacks = true;
                    w.effectColor = NachhallSilver;
                    Echo(w);
                });
            WeaponDefinition greatW = Weapon("Weapon_NachhallGreat", "weapon.nachhall_great", "Nachhall",
                w =>
                {
                    Melee(w, WeaponClass.TwoHandedSword, greatDmg.x, greatDmg.y, 3f, 1.3f, "Widerhall");
                    w.quality = ItemQuality.Legendary;
                    w.hasAirAttacks = true;
                    w.effectColor = NachhallSilver;
                    Echo(w);
                });

            ItemDefinition iShort = WeaponItem("nachhall_short", "Nachhall, Kurzklinge", shortW, ItemQuality.Legendary,
                NachhallItemLevel, NachhallRequiredLevel, IconShape.Sword, NachhallSilver, 2600,
                "Altes Holz, alter Knochen und ein Stahl, der der Länge nach gebrochen ist. In den Spalten klingt etwas nach.",
                BindType.OnPickup, (StatType.Strength, 8), (StatType.Stamina, 6));
            ItemDefinition iGreat = WeaponItem("nachhall_great", "Nachhall, Großklinge", greatW, ItemQuality.Legendary,
                NachhallItemLevel, NachhallRequiredLevel, IconShape.Sword, NachhallSilver, 3200,
                "Dieselbe Herkunft, die längere Klinge. Die Bruchstücke liegen eng beieinander, als wollten sie sich nicht trennen.",
                BindType.OnPickup, (StatType.Strength, 12), (StatType.Stamina, 9));
            foreach ((ItemDefinition item, WeaponDefinition w) in new[] { (iShort, shortW), (iGreat, greatW) })
            {
                WeaponDefinition wd = w;
                item.Post(d =>
                {
                    d.unique = true;
                    d.effects.Add(new ItemEffect { type = ItemEffectType.Custom, text = EchoText(wd) });
                });
            }

            LinkNachhallAppearance(iShort, "nachhall_short", "Nachhall_Kurzschwert_Spiel", WeaponSheath.HipLeft);
            LinkNachhallAppearance(iGreat, "nachhall_great", "Nachhall_Grossschwert_Spiel", WeaponSheath.Back);
        }

        private static void Echo(WeaponDefinition w)
        {
            w.echoChance = 0.15f;    // Platzhalter (Balancepass)
            w.echoFraction = 0.35f;
            w.echoDelay = 0.6f;
            w.echoCooldown = 6f;
        }

        private static string EchoText(WeaponDefinition w) =>
            $"Nachhall: Treffer hallen gelegentlich nach ({w.echoChance * 100f:0} %) und treffen {w.echoDelay:0.0} s später " +
            $"erneut mit {w.echoFraction * 100f:0} % ihres Schadens. Höchstens einmal alle {w.echoCooldown:0} s.";

        /// <summary>Eigenes Aussehen je Klinge: Spielmodell am Griff (Klinge +Z, Ursprung = Griffmitte, wie WeaponGrip misst).</summary>
        private static void LinkNachhallAppearance(ItemDefinition item, string key, string prefabName, WeaponSheath sheath)
        {
            if (item == null) return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{NachhallPrefabDir}/{prefabName}.prefab");
            if (prefab == null)
            {
                Debug.LogWarning($"[Ashveil] Nachhall: {prefabName}.prefab fehlt – zuerst „Ashveil/Waffen/Nachhall/1 Import“ ausführen.");
                return;
            }
            EnsureEchoVisual(prefab);
            string path = AppearanceContentBuilder.Folder + "/Appearance_" + key + ".asset";
            var a = AssetDatabase.LoadAssetAtPath<AppearanceDefinition>(path);
            if (a == null)
            {
                a = ScriptableObject.CreateInstance<AppearanceDefinition>();
                AssetDatabase.CreateAsset(a, path);
            }
            a.slot = EquipType.Weapon;
            a.bodies = BodyVariantMask.All;
            a.sheath = sheath;
            a.placeholder = false;
            a.detailDistance = 25f;
            a.hides = AppearanceHides.None;
            a.shells = new List<ShellLayer>();
            a.parts = new List<RigidPart> { new RigidPart { bone = "Grip", prefab = prefab, size = Vector3.one, offset = Vector3.zero, euler = Vector3.zero } };
            K.SetString(a, "id", "appearance." + key);
            K.SetString(a, "displayName", key);
            EditorUtility.SetDirty(a);
            if (item.appearance != a)
            {
                item.appearance = a;
                EditorUtility.SetDirty(item);
            }
        }

        /// <summary>Lichtpuls des Nachhall auf dem Spielmodell (falls das Prefab vor dieser Komponente gebaut wurde).</summary>
        private static void EnsureEchoVisual(GameObject prefab)
        {
            var have = prefab.GetComponent<WeaponEchoVisual>();
            if (have != null && Mathf.Approximately(have.peakFactor, WeaponEchoVisual.PeakDefault) &&
                Mathf.Approximately(have.decayTime, WeaponEchoVisual.DecayDefault)) return;
            string p = AssetDatabase.GetAssetPath(prefab);
            GameObject root = PrefabUtility.LoadPrefabContents(p);
            try
            {
                var v = root.GetComponent<WeaponEchoVisual>();
                if (v == null) v = root.AddComponent<WeaponEchoVisual>();
                v.peakFactor = WeaponEchoVisual.PeakDefault;
                v.decayTime = WeaponEchoVisual.DecayDefault;
                PrefabUtility.SaveAsPrefabAsset(root, p);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [MenuItem("Ashveil/Waffen/Nachhall/7 Items und Aussehen anlegen", priority = 706)]
        public static void BuildNachhallOnly()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Ashveil] Nachhall: nicht im Play-Modus."); return; }
            foreach (string f in new[] { ItemFolder, PlayerRigBuilder.WeaponFolder, AppearanceContentBuilder.Folder })
                ProjectSetup.EnsureFolder(f);
            _refresh = false;   // nur fehlende Assets mit Standardwerten anlegen
            Written.Clear();
            BuildNachhallItems();
            AssetDatabase.SaveAssets();
            DefinitionValidator.ValidateAndRebuild();
            Debug.Log("[Ashveil] Nachhall: Waffen, Items und Aussehen angelegt bzw. aktualisiert.");
        }
    }
}
