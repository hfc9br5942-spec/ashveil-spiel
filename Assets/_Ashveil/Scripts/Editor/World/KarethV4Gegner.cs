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
    /// Kareth v4, Welle 0: Gegner in die Jagdgebiete J1–J7 (Kareth-Pass Teil 7 und Teil 13 Schritt 3, von Max freigegeben) –
    /// zunächst nur mit vorhandenen Kreaturen (Aschwolf, Verblichene, Nadelspeier, Torfschrat, Rußluchs). Dazu die Auftragsorte
    /// aus Teil 10: Gehöft am Bergfuß (Q4/Q5), Holzfällerlager (G5) und der Hort der Grauen Mutter.
    ///
    /// Lagen: je Gebiet die nächste begehbare, trockene Stelle abseits der Wege zum Wunschpunkt (Höhenkarte W0, geprüft mit
    /// claude/kareth_v4/integration-Werkzeug). Stufen nach den Leitern in Teil 7.
    /// #28 (07.10., Bauplan v2): die neuen Greybox-Tiere (Furchenwühler, Uferklapper, Schlickwerfer, Flüsterbalg) und die
    /// Kareth-Varianten (Ertrunkene Matrosen am Wrack, hungriger Deserteur) sowie die Deserteure der Wüstungshöfe (Stufe 8–10 über
    /// das Gebiet; Definition aus dem Grenzgebirge). Lagen mit dem #28-Platzwerkzeug geprüft; Spiegel in inhalte_w0.py (GEGNER).
    /// Definitionen und Prefabs werden geladen, nicht neu gebaut (Sandbox_M1 bleibt unverändert; die #28-Gegner baut „inhalte_28“).
    /// </summary>
    public static class KarethV4Gegner
    {
        public const string RootName = "Gegner (Jagdgebiete, Welle 0)";

        private struct Gebiet
        {
            public string Name, Enemy, Group;
            public float X, Z, Radius;
            public int Count, Min, Max;
            public AggroProfile? Aggro;   // null = wie in der Definition
            public float Respawn;          // Faktor auf die Wiederkehrzeit (0 = 1)
            public float[] Alt;            // weitere Verstecke (x, z, x, z, …) – seltene Gegner (Classic-Paket)

            public Gebiet(string name, string enemy, float x, float z, int count, float radius, int min, int max, string group = "", AggroProfile? aggro = null)
            {
                Name = name; Enemy = enemy; X = x; Z = z; Count = count; Radius = radius; Min = min; Max = max; Group = group; Aggro = aggro; Respawn = 0f; Alt = null;
            }
        }

        // Gegner-ID → Dateiname der Definition und des Prefabs (EnemyBuilder: Definition und Prefab teilen den Namen)
        private static readonly Dictionary<string, string> Files = new Dictionary<string, string>
        {
            { "enemy.ash_wolf", "Enemy_Aschwolf" },
            { "enemy.faded_one", "Enemy_Verblichener" },
            { "enemy.faded_gunner", "Enemy_VerblicheneSchuetzin" },
            { "enemy.peat_brute", "Enemy_Torfschrat" },
            { "enemy.needle_spitter", "Enemy_Nadelspeier" },
            { "enemy.soot_lynx", "Enemy_Russluchs" },
            // #28
            { KarethCatalog.EnemyRooter, EnemyBuilder.FileRooter },
            { KarethCatalog.EnemyClacker, EnemyBuilder.FileClacker },
            { KarethCatalog.EnemySlinger, EnemyBuilder.FileSlinger },
            { KarethCatalog.EnemyBellows, EnemyBuilder.FileBellows },
            { KarethCatalog.EnemyReedDeserter, EnemyBuilder.FileReedDeserter },
            { KarethCatalog.EnemyWreckSailor, EnemyBuilder.FileWreckSailor },
            { KarethCatalog.EnemyDeserter, "Enemy_Deserteur" },
            { KarethCatalog.EnemyDeserterArcher, "Enemy_DeserteurSchuetzin" },
            // Classic-Paket (11.10.): seltene Tiere
            { KarethCatalog.RareAltborste, EnemyBuilder.FileAltborste },
            { KarethCatalog.RareRostschere, EnemyBuilder.FileRostschere },
            { KarethCatalog.RareNarbenfang, EnemyBuilder.FileNarbenfang },
            { KarethCatalog.RareRaunebalg, EnemyBuilder.FileRaunebalg },
            { KarethCatalog.RareKrummnadel, EnemyBuilder.FileKrummnadel },
            { KarethCatalog.RareMoosbuckel, EnemyBuilder.FileMoosbuckel },
        };

        private const string Wolf = "enemy.ash_wolf", Faded = "enemy.faded_one", Gunner = "enemy.faded_gunner",
            Brute = "enemy.peat_brute", Spitter = "enemy.needle_spitter", Lynx = "enemy.soot_lynx";

        private static readonly Gebiet[] Gebiete =
        {
            // J1 Stumpfgürtel / NW-Wäldchen (1–3): Streuner, defensiv wie die Lager-Rudel bisher. Q2 „Zähe Felle“ (4 Pelze).
            new Gebiet("J1 Streunende Aschwölfe, NW-Wäldchen (Q2)", Wolf, -293f, 330f, 4, 10f, 1, 2, "", AggroProfile.Defensive),
            new Gebiet("J1 Streunende Aschwölfe, Westsattel", Wolf, -230f, 120f, 2, 8f, 1, 2, "", AggroProfile.Defensive),
            // J2 Kiefernschlag (3–5): Aschwölfe und kräftige Aschwölfe. K-D (6 Wölfe, kommt mit #28).
            new Gebiet("J2 Aschwölfe, Kiefernschlag Nord", Wolf, -497f, -632f, 3, 10f, 3, 4),
            new Gebiet("J2 Aschwölfe, Kiefernschlag Süd", Wolf, -472f, -702f, 3, 10f, 3, 4),
            // Gehöft am Bergfuß, ausgebrannt (Teil 10): Q4 „Die Verblichenen“ (4), Q5 „Federn für die Wacht“
            new Gebiet("Gehöft am Bergfuß: Verblichene (Q4)", Faded, 616f, -280f, 4, 6f, 3, 4, "Gehöft am Bergfuß"),   // 13 m vom Bergfußweg
            new Gebiet("Gehöft am Bergfuß: Schützinnen (Q5)", Gunner, 630f, -284f, 2, 4f, 3, 4, "Gehöft am Bergfuß"),
            // Holzfällerlager auf der Terrasse: G5 „Das leere Lager“ (6 Verblichene), angriffslustig wie bisher.
            // 06.10.: Terrasse 12 m vom Kiefernweg abgerückt (Mitte −785/−646), die Gruppen rücken mit (eben, 32 und 49 m vom Weg).
            new Gebiet("Holzfällerlager: Verblichene (G5)", Faded, -785f, -646f, 3, 6f, 5, 7, "Holzfällerlager", AggroProfile.Aggressive),
            new Gebiet("Holzfällerlager: Schützinnen (G5)", Gunner, -770f, -661f, 2, 4f, 5, 7, "Holzfällerlager", AggroProfile.Aggressive),
            // J4 Speiergrund / Moorrand (5–8): Nadelspeier. G2 „Nadeln im Schilf“ (8).
            new Gebiet("J4 Nadelspeier, Speiergrund", Spitter, -232f, 884f, 3, 8f, 5, 7),
            new Gebiet("J4 Nadelspeier, Moorrand West", Spitter, -320f, 820f, 3, 8f, 5, 7),
            new Gebiet("J4 Nadelspeier, Moorrand Ost", Spitter, -120f, 960f, 2, 8f, 6, 7),
            // J5 Moorkern / Kuppeln (6–9): Torfschrate. G9 „Torf und Zorn“ (5).
            new Gebiet("J5 Torfschrate, Kuppeln Nord", Brute, 300f, 1080f, 3, 9f, 6, 8),
            new Gebiet("J5 Torfschrate, Kuppeln Süd", Brute, 210f, 950f, 2, 9f, 6, 8),
            // J6 Werkstätten / Aschenfeld (7–10): Verblichene (Arbeiter, Wachtposten). U-4 „Arbeitszeichen“ (8, kommt mit #28).
            new Gebiet("J6 Verblichene, Werkstätten", Faded, 1434f, -365f, 3, 7f, 7, 9, "Werkstätten"),
            new Gebiet("J6 Verblichene, Werkstätten oben", Faded, 1448f, -342f, 3, 6f, 7, 9, "Werkstätten"),
            new Gebiet("J6 Schützinnen, Werkstätten", Gunner, 1405f, -395f, 2, 4f, 7, 9, "Werkstätten"),
            new Gebiet("J6 Wachtposten, Aschenfeld", Faded, 1380f, -300f, 2, 7f, 8, 10),
            // J7 Nordmoor (8–10, 0,5 km² Salzwiese zwischen Moor und See): schwere Torfschrate, Breitkopfspeier. G12 „Nordmoor“.
            new Gebiet("J7 Schwere Torfschrate, Nordmoor", Brute, -820f, 1250f, 2, 9f, 8, 9),
            new Gebiet("J7 Schwere Torfschrate, Nordmoor West", Brute, -888f, 1166f, 2, 9f, 8, 9),
            new Gebiet("J7 Breitkopfspeier, Nordmoor", Spitter, -760f, 1180f, 3, 8f, 9, 10),
            // Hort der Grauen Mutter (Elite 11, drei Spieler; in Welle 0 Vorschau über Stufe 10)
            new Gebiet("Die Graue Mutter (Hort)", Lynx, -1701f, -1866f, 1, 1f, 11, 11),

            // ---------------------------------------------------------- #28 Ergänzungsaufträge (Bauplan #28 v2, Teil 4)
            // J1 Vorratswiesen (1–2): Furchenwühler, neutral (Passiv aus der Definition). K-A „Die Vorratsgruben“ (6).
            new Gebiet("K-A Furchenwühler, Vorratswiesen Nord", KarethCatalog.EnemyRooter, 200f, -190f, 4, 9f, 1, 2),
            new Gebiet("K-A Furchenwühler, Vorratswiesen Süd", KarethCatalog.EnemyRooter, 207f, -218f, 3, 9f, 1, 2),
            // Waldrand vor dem Westtor (3–4): Flüsterbälge, neutral. K-C „Der Waldrand“ (6).
            new Gebiet("K-C Flüsterbälge, Waldrand vor dem Westtor", KarethCatalog.EnemyBellows, -215f, 25f, 3, 8f, 3, 4),
            new Gebiet("K-C Flüsterbälge, Waldrand West", KarethCatalog.EnemyBellows, -255f, 50f, 3, 8f, 3, 4),
            // J3 Salzufer: Uferklapper an der Kiefernbach-Mündung (2–3) und am Ufer (3–4), neutral. K-B „Die Wasserstelle“ (8).
            new Gebiet("K-B Uferklapper, Kiefernbach-Mündung", KarethCatalog.EnemyClacker, -1300f, -65f, 4, 8f, 2, 3),
            new Gebiet("K-B Uferklapper, Salzufer", KarethCatalog.EnemyClacker, -1385f, 15f, 4, 8f, 3, 4),
            // Salzufer südlich des Dammbruchs (5–6): Schlickwerfer, territorial (nördlich statt südlich: Oswald spült am Damm, U-2)
            new Gebiet("U-2 Schlickwerfer, Salzufer am Damm", KarethCatalog.EnemySlinger, -1356f, 400f, 3, 7f, 5, 6),
            new Gebiet("U-2 Schlickwerfer, Salzufer Süd", KarethCatalog.EnemySlinger, -1354f, 330f, 3, 7f, 5, 6),
            // Wrack im Salz (6–7): Ertrunkene Matrosen als normale Gegner (der Kapitän bleibt in den Salzgruben). U-3 (6).
            new Gebiet("U-3 Ertrunkene Matrosen, Wrack Süd", KarethCatalog.EnemyWreckSailor, -1245f, -192f, 3, 6f, 6, 7, "Wrack im Salz"),
            new Gebiet("U-3 Ertrunkene Matrosen, Wrack Nord", KarethCatalog.EnemyWreckSailor, -1287f, -150f, 3, 6f, 6, 7, "Wrack im Salz"),
            // Nest am Wurzelhang beim Wagen (5): Flüsterbälge, territorial. U-1 „Tamos Wagen“ (6).
            new Gebiet("U-1 Flüsterbälge, Nest am Wurzelhang", KarethCatalog.EnemyBellows, -1035f, -978f, 3, 7f, 5, 5, "Flüsterbalg-Nest", AggroProfile.Territorial),
            new Gebiet("U-1 Flüsterbälge, Nest West", KarethCatalog.EnemyBellows, -1062f, -990f, 3, 6f, 5, 5, "Flüsterbalg-Nest", AggroProfile.Territorial),
            // Wüstungshöfe (8–10): Deserteure und Deserteurinnen beiderseits der Straße. U-6 „Die leeren Höfe“ (5 + 3).
            new Gebiet("U-6 Deserteure, Wüstungshof West", KarethCatalog.EnemyDeserter, -1512f, -1548f, 3, 6f, 8, 9, "Wüstungshöfe"),
            new Gebiet("U-6 Deserteurinnen, Wüstungshof West", KarethCatalog.EnemyDeserterArcher, -1528f, -1562f, 2, 4f, 8, 10, "Wüstungshöfe"),
            new Gebiet("U-6 Deserteure, Wüstungshof Ost", KarethCatalog.EnemyDeserter, -1386f, -1530f, 3, 6f, 9, 10, "Wüstungshöfe"),
            new Gebiet("U-6 Deserteurinnen, Wüstungshof Ost", KarethCatalog.EnemyDeserterArcher, -1376f, -1544f, 2, 4f, 9, 10, "Wüstungshöfe"),
            // Versteck im Schilf beim alten Rastplatz (7): ein hungriger Deserteur verteidigt es. U-7 „Die verschwundenen Gaben“.
            new Gebiet("U-7 Hungriger Deserteur, Versteck im Schilf", KarethCatalog.EnemyReedDeserter, -45f, 855f, 1, 1f, 7, 7),

            // ---------------------------------------------------------- Classic-Paket (Max 11.10.2026 abends): seltene Tiere
            // Classic ≈ 1 % „Rares“: je eines, drei Verstecke (jedes Erscheinen wählt eines), Wiederkehr 20–45 min aus der Definition.
            // Lagen mit claude/kareth_v4/integration/gegnerdichte.py-Werkzeug geprüft: begehbar, ≥ 35 m vom Weg, nicht an Orten oder Lagern.
            Selten("Selten: Altborste (Mulde, Vorratswiesen und Feldrand)", KarethCatalog.RareAltborste, 184.7f, -29.0f, 4, 495.5f, -252.2f, 410.8f, -201.1f),
            Selten("Selten: Rostschere (Salzsee-Ufer)", KarethCatalog.RareRostschere, -1417.6f, -25.0f, 7, -1443.6f, 71.5f, -1237.8f, 390.3f),
            Selten("Selten: Narbenfang (Heuwiesen und östliche Feldmark)", KarethCatalog.RareNarbenfang, 1085.4f, 621.4f, 7, 1086.6f, 785.0f, 953.7f, 513.2f),
            Selten("Selten: Raunebalg (Kiefernhang)", KarethCatalog.RareRaunebalg, -245.1f, -1137.4f, 7, -615.3f, -823.8f, -223.5f, -1276.1f),
            Selten("Selten: Krummnadel (Nordmoor und Moorrand)", KarethCatalog.RareKrummnadel, -790.2f, 952.4f, 9, -679.6f, 1390.0f, -458.7f, 1048.2f),
            Selten("Selten: Moosbuckel (Kuppeln im Glockenmoor)", KarethCatalog.RareMoosbuckel, 167.4f, 1154.4f, 9, 405.5f, 988.6f, -4.0f, 1007.4f),
        };

        private static Gebiet Selten(string name, string enemy, float x, float z, int level, params float[] alt) =>
            new Gebiet(name, enemy, x, z, 1, 1f, level, level) { Alt = alt };

        // ------------------------------------------------------------------ Weltfülle (Max 10.10.2026 spät: Verteilung nach WoW Classic/Forever)
        // Beidatei KarethV4_Gegnerdichte.json aus claude/kareth_v4/integration/gegnerdichte.py: verstreute Wildtiere, Streuner und Lager
        // je Unterzone und Lebensraum, dazu die Lager der Nebenaufträge N01–N15. Fehlt die Datei, bleibt es bei der Liste oben.
        [System.Serializable] private sealed class DichteEintrag { public string name, enemy, gruppe; public float x, z, radius, respawn; public int count, min, max, aggro; }
        [System.Serializable] private sealed class DichteDatei { public int version; public string stand; public DichteEintrag[] gebiete; }
        public static string DichtePfad => KarethV4Builder.DataFolder + "/KarethV4_Gegnerdichte.json";

        private static List<Gebiet> AlleGebiete(out int dichte)
        {
            var l = new List<Gebiet>(Gebiete);
            dichte = 0;
            if (!File.Exists(DichtePfad)) return l;
            DichteDatei d = JsonUtility.FromJson<DichteDatei>(File.ReadAllText(DichtePfad));
            if (d?.gebiete == null) return l;
            foreach (DichteEintrag e in d.gebiete)
            {
                var g = new Gebiet(e.name, e.enemy, e.x, e.z, Mathf.Max(1, e.count), Mathf.Max(1f, e.radius), e.min, e.max, e.gruppe ?? "",
                    e.aggro >= 0 ? (AggroProfile?)e.aggro : null) { Respawn = e.respawn };
                l.Add(g);
                dichte += g.Count;
            }
            return l;
        }

        /// <summary>Spawngebiete in die Szene setzen (Gegner erscheinen zur Laufzeit über den EnemyDirector des Netzes).</summary>
        public static string Build(Transform root)
        {
            List<Gebiet> alle = AlleGebiete(out int dichte);
            Transform parent = K.Group(RootName, root);
            var cache = new Dictionary<string, EnemyBuilder.BorderEnemy>();
            var missing = new HashSet<string>();
            int areas = 0, enemies = 0;
            Terrain terrain = Object.FindAnyObjectByType<Terrain>();
            foreach (Gebiet a in alle)
            {
                if (!cache.TryGetValue(a.Enemy, out EnemyBuilder.BorderEnemy e))
                {
                    e = Load(a.Enemy);
                    cache[a.Enemy] = e;
                }
                if (e == null) { missing.Add(a.Enemy); continue; }
                float y = terrain != null ? terrain.SampleHeight(new Vector3(a.X, 0f, a.Z)) + terrain.transform.position.y : 0f;
                Vector3 local = parent.InverseTransformPoint(new Vector3(a.X, y + 0.2f, a.Z));
                EnemySpawnArea area = EnemyBuilder.PlaceArea(parent, a.Name, local, e, a.Count, a.Radius, a.Group, a.Min, a.Max);
                if (a.Aggro.HasValue)
                {
                    K.SetBool(area, "overrideAggro", true);
                    K.SetInt(area, "aggroProfile", (int)a.Aggro.Value);
                }
                if (a.Respawn > 0.1f && Mathf.Abs(a.Respawn - 1f) > 0.01f) K.SetFloat(area, "respawnMultiplier", a.Respawn);
                if (a.Alt != null && a.Alt.Length >= 2)
                {
                    var so = new SerializedObject(area);
                    SerializedProperty ap = so.FindProperty("alternatePoints");
                    ap.arraySize = a.Alt.Length / 2;
                    for (int i = 0; i < ap.arraySize; i++)
                    {
                        float ax = a.Alt[2 * i], az = a.Alt[2 * i + 1];
                        float ay = terrain != null ? terrain.SampleHeight(new Vector3(ax, 0f, az)) + terrain.transform.position.y : 0f;
                        ap.GetArrayElementAtIndex(i).vector3Value = new Vector3(ax, ay + 0.2f, az);
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                areas++;
                enemies += a.Count;
            }
            // Netzobjekte sicherstellen (dieselbe Liste wie die Sandbox; doppelte Einträge werden übersprungen)
            var prefabs = new List<GameObject>();
            foreach (EnemyBuilder.BorderEnemy e in cache.Values) if (e != null && e.Prefab != null) prefabs.Add(e.Prefab);
            if (prefabs.Count > 0) NetworkSetupBuilder.AddSpawnables(prefabs);
            string report = $"{areas} Spawngebiete, {enemies} Gegner" + (dichte > 0 ? $" (davon Weltfülle und Nebenauftragslager {dichte})" : "");
            if (missing.Count > 0)
            {
                report += " – fehlende Gegner (erst „Sandbox-Szene erstellen“): " + string.Join(", ", missing);
                Debug.LogWarning("[Ashveil] Kareth v4: " + report);
            }
            return report;
        }

        private static EnemyBuilder.BorderEnemy Load(string id)
        {
            if (!Files.TryGetValue(id, out string file)) return null;
            var def = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(EnemyBuilder.DefFolder + "/" + file + ".asset");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyBuilder.PrefabFolder + "/" + file + ".prefab");
            if (def == null || prefab == null) return null;
            if (def.Id != id) Debug.LogWarning($"[Ashveil] Kareth v4: {file} hat die ID {def.Id}, erwartet {id}.");
            return new EnemyBuilder.BorderEnemy { Def = def, Prefab = prefab };
        }
    }
}
