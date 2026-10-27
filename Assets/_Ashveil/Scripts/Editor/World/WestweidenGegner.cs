using System.Collections.Generic;
using System.IO;
using Ashveil.Data;
using Ashveil.Gameplay.Enemies;
using UnityEditor;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Westweiden spielbar (12.10.2026): Gegner-Weltfülle der Westweiden-Zelle nach der Leveling-Basis (Classic-Streuung wie Kareth v4:
    /// überwiegend Einzelpunkte, Lager aus Einzelpunkten, Straßen frei, Sperrzonen um Orte). Daten `Data/Westweiden/Entwurf/WW_Gegner.json`
    /// (lokale Westweiden-Koordinaten, erzeugt von `Tools/claude/topo/westweiden_gegner.py` aus den Lebensräumen des Füllplans);
    /// gesetzt nach dem Verschieben der Zelle an ihre Lage in Kareth. Die Gebiete sind reine MonoBehaviours (Zellen-Regel: keine
    /// Netzobjekte in der Zelle) – der Server erzeugt die Gegner in der Welt-Ebene, sobald die Zelle bereit ist (EnemyDirector).
    /// </summary>
    public static class WestweidenGegner
    {
        public const string RootName = "Gegner (Westweiden, Weltfülle)";
        public const string DatenPfad = ProjectSetup.Root + "/Data/Westweiden/Entwurf/WW_Gegner.json";

        /// <summary>Arten des letzten Aufbaus (Kreidekeller nutzt sie, statt die Prefabs ein zweites Mal zu bauen).</summary>
        public static Dictionary<string, EnemyBuilder.BorderEnemy> LetzteArten;

        [System.Serializable] private sealed class Eintrag { public string name, enemy, gruppe; public float x, z, radius, respawn; public int count, min, max, aggro; public float[] alt; }
        [System.Serializable] private sealed class Datei { public int version; public string stand; public Eintrag[] gebiete; }

        public static string Build(Transform parent, Terrain terrain, Vector3 versatz)
        {
            if (!File.Exists(DatenPfad)) return "WW_Gegner.json fehlt – keine Gegner";
            Datei d = JsonUtility.FromJson<Datei>(File.ReadAllText(DatenPfad));
            if (d?.gebiete == null) return "WW_Gegner.json leer";
            Dictionary<string, EnemyBuilder.BorderEnemy> arten = EnemyBuilder.BuildWestweidenCreatures();
            LetzteArten = arten;
            int beute = ItemContentBuilder.AssignWestweidenLoot();   // Aufträge W04/W18/W27/W28 und Beute (Punkt 2)
            Transform root = K.Group(RootName, parent);
            float Boden(float wx, float wz) => terrain.SampleHeight(new Vector3(wx, 0f, wz)) + terrain.transform.position.y;
            int areas = 0, enemies = 0;
            var fehlt = new HashSet<string>();
            foreach (Eintrag e in d.gebiete)
            {
                if (!arten.TryGetValue(e.enemy, out EnemyBuilder.BorderEnemy art) || art?.Prefab == null) { fehlt.Add(e.enemy); continue; }
                float wx = e.x + versatz.x, wz = e.z + versatz.z;
                Vector3 local = root.InverseTransformPoint(new Vector3(wx, Boden(wx, wz) + 0.2f, wz));
                EnemySpawnArea area = EnemyBuilder.PlaceArea(root, e.name, local, art, Mathf.Max(1, e.count), Mathf.Max(1f, e.radius), e.gruppe ?? "", e.min, e.max);
                if (e.aggro >= 0)
                {
                    K.SetBool(area, "overrideAggro", true);
                    K.SetInt(area, "aggroProfile", e.aggro);
                }
                if (e.respawn > 0.1f && Mathf.Abs(e.respawn - 1f) > 0.01f) K.SetFloat(area, "respawnMultiplier", e.respawn);
                if (e.alt != null && e.alt.Length >= 2)
                {
                    var so = new SerializedObject(area);
                    SerializedProperty ap = so.FindProperty("alternatePoints");
                    ap.arraySize = e.alt.Length / 2;
                    for (int i = 0; i < ap.arraySize; i++)
                    {
                        float ax = e.alt[2 * i] + versatz.x, az = e.alt[2 * i + 1] + versatz.z;
                        ap.GetArrayElementAtIndex(i).vector3Value = new Vector3(ax, Boden(ax, az) + 0.2f, az);
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                areas++;
                enemies += e.count;
            }
            string r = $"Gegner: {areas} Gebiete, {enemies} Gegner ({d.stand}), Beute an {beute} Arten";
            if (fehlt.Count > 0) r += " – unbekannt: " + string.Join(", ", fehlt);
            return r;
        }
    }
}
