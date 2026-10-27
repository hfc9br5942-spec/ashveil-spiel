using System.Collections.Generic;
using Ashveil.Data;
using UnityEditor;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;
using KC = Ashveil.Data.KarethCatalog;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Classic-Paket (Max 11.10.2026 abends, Classic-Bauplan §5.2): sechs seltene, benannte Tiere in Kareth – in Classic ist etwa
    /// jeder hundertste Gegner der Stufen 1–20 so ein „Rare“ (Emulator-Datenbank, nur Zahlen). Jedes ist eine kräftigere Spielart
    /// einer vorhandenen Art: Werte der Grundart kopiert, dann Stufe fest, Leben ×1,8 (Max 11.10. nachts: „zu stark für allein“ bei ×2,2), Schaden ×1,3, Gestalt ×1,3, eigene Fellfarbe,
    /// Wiederkehr 20–45 min (die dynamische Wiederkehr gilt nicht: Gebiete mit mehreren Verstecken sind ausgenommen).
    /// Keine neue Spielmechanik im Kampf – nur seltener, stärker, mit besserer Beute (<see cref="ItemContentBuilder.AssignKarethRareLoot"/>).
    /// Verstecke und Spawngebiete: <see cref="KarethV4Gegner"/>. Neue Prefabs kommen hinten an die Netz-Prefabs (IDs der alten bleiben).
    /// Namen und Farben: VORSCHLAG (Claude).
    /// </summary>
    public static partial class EnemyBuilder
    {
        public const string FileAltborste = "Enemy_Altborste", FileRostschere = "Enemy_Rostschere", FileNarbenfang = "Enemy_Narbenfang",
            FileRaunebalg = "Enemy_Raunebalg", FileKrummnadel = "Enemy_Krummnadel", FileMoosbuckel = "Enemy_Moosbuckel";

        /// <summary>Gegner-ID → Dateiname der seltenen Tiere (auch für <see cref="KarethV4Gegner"/>).</summary>
        public static readonly Dictionary<string, string> KarethRareFiles = new Dictionary<string, string>
        {
            { KC.RareAltborste, FileAltborste }, { KC.RareRostschere, FileRostschere }, { KC.RareNarbenfang, FileNarbenfang },
            { KC.RareRaunebalg, FileRaunebalg }, { KC.RareKrummnadel, FileKrummnadel }, { KC.RareMoosbuckel, FileMoosbuckel },
        };

        private const float RareHealth = 1.8f, RareDamage = 1.3f, RareScale = 1.3f, RareRespawnMin = 1200f, RareRespawnMax = 2700f;

        /// <summary>Definitionen und Prefabs der sechs seltenen Tiere (nach <see cref="BuildKarethCreatures"/>; idempotent).</summary>
        public static Dictionary<string, BorderEnemy> BuildKarethRares()
        {
            ProjectSetup.EnsureFolder(DefFolder);
            ProjectSetup.EnsureFolder(PrefabFolder);
            CreateMaterials();
            CreateKarethMaterials();
            var r = new Dictionary<string, BorderEnemy>();

            Material bleach = K.Mat("Enemy_Rare_AltborsteFur", new Color(0.62f, 0.58f, 0.52f), 0.05f);        // ausgeblichene, fast weiße Borsten
            Material rust = K.Mat("Enemy_Rare_RostschereShell", new Color(0.5f, 0.27f, 0.15f), 0.4f);         // rostroter Panzer
            Material scar = K.Mat("Enemy_Rare_NarbenfangFur", new Color(0.31f, 0.27f, 0.24f), 0.05f);         // hellgrau mit dunklen Narben
            Material scarDark = K.Mat("Enemy_Rare_NarbenfangScar", new Color(0.05f, 0.045f, 0.04f), 0.05f);
            Material dusk = K.Mat("Enemy_Rare_RaunebalgSkin", new Color(0.26f, 0.28f, 0.24f), 0.12f);         // dunkel, moosgrün
            Material bone = K.Mat("Enemy_Rare_KrummnadelSpine", new Color(0.82f, 0.78f, 0.66f), 0.3f);        // knochenhelle Stacheln
            Material moss = K.Mat("Enemy_Rare_MoosbuckelMoss", new Color(0.24f, 0.33f, 0.16f), 0.15f);        // dicht bemoost

            BorderEnemy Rare(string id, string baseFile, string file, string name, int level, System.Func<EnemyDefinition, GameObject> build)
            {
                EnemyDefinition baseDef = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(DefFolder + "/" + baseFile + ".asset");
                if (baseDef == null) { Debug.LogError($"[Ashveil] Seltene Tiere: Grundart {baseFile} fehlt (erst „inhalte_28“ bzw. Sandbox-Bau)."); return null; }
                string path = DefFolder + "/" + file + ".asset";
                var def = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);
                if (def == null)
                {
                    def = ScriptableObject.CreateInstance<EnemyDefinition>();
                    AssetDatabase.CreateAsset(def, path);
                }
                // Werte der Grundart (Kampf, Anatomie, Bewegungen, Aggro) – dann die seltene Spielart darüber
                EditorUtility.CopySerialized(baseDef, def);
                def.name = file;
                K.SetString(def, "id", id);
                K.SetString(def, "displayName", name);
                def.minLevel = level;
                def.maxLevel = level;
                def.healthFactor = baseDef.healthFactor * RareHealth;
                def.damageFactor = baseDef.damageFactor * RareDamage;
                if (!def.UsesBalance)
                {
                    def.baseHealth = baseDef.baseHealth * RareHealth;
                    def.healthPerLevel = baseDef.healthPerLevel * RareHealth;
                    def.minDamage = baseDef.minDamage * RareDamage;
                    def.maxDamage = baseDef.maxDamage * RareDamage;
                }
                def.scale = baseDef.scale * RareScale;
                def.respawnTime = RareRespawnMin;
                def.respawnTimeMax = RareRespawnMax;
                def.elite = false;
                def.lootTable = null;                 // eigene Tabelle: ItemContentBuilder.AssignKarethRareLoot
                EditorUtility.SetDirty(def);
                GameObject prefab = build(def);
                return new BorderEnemy { Def = def, Prefab = prefab };
            }

            r[KC.RareAltborste] = Rare(KC.RareAltborste, FileRooter, FileAltborste, "Altborste", 4,
                d => Tint(BuildRooterPrefab(d), (_kRooter, bleach)));
            r[KC.RareRostschere] = Rare(KC.RareRostschere, FileClacker, FileRostschere, "Rostschere", 7,
                d => Tint(BuildClackerPrefab(d, false), (_kClacker, rust), (_kClackerDark, _kClackerRust)));
            r[KC.RareNarbenfang] = Rare(KC.RareNarbenfang, "Enemy_Aschwolf", FileNarbenfang, "Narbenfang", 7,
                d => Tint(BuildBeastPrefab(d), (_wolfFur, scar), (_wolfDark, scarDark)));
            r[KC.RareRaunebalg] = Rare(KC.RareRaunebalg, FileBellows, FileRaunebalg, "Raunebalg", 7,
                d => Tint(BuildBellowsPrefab(d), (_kBellows, dusk)));
            r[KC.RareKrummnadel] = Rare(KC.RareKrummnadel, "Enemy_Nadelspeier", FileKrummnadel, "Krummnadel", 9,
                d => Tint(BuildNeedleSpitterPrefab(d), (_spine, bone)));
            r[KC.RareMoosbuckel] = Rare(KC.RareMoosbuckel, "Enemy_Torfschrat", FileMoosbuckel, "Moosbuckel", 9,
                d => Tint(BuildColossusPrefab(d, false), (_mossGreen, moss), (_peat, moss)));

            var prefabs = new List<GameObject>();
            foreach (BorderEnemy e in r.Values) if (e != null && e.Prefab != null) prefabs.Add(e.Prefab);
            NetworkSetupBuilder.AddSpawnables(prefabs);
            AssetDatabase.SaveAssets();
            return r;
        }
    }
}
