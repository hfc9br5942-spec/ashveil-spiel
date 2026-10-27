using System.Collections.Generic;
using System.Diagnostics;
using Ashveil.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// #28 Kareth v4, Welle 0 – Auftrag „inhalte_28“ im Auftragsordner (Bauplan #28 v2, Teil 5): baut die Daten der elf
    /// Ergänzungsaufträge, ohne eine Szene neu zu bauen (Sandbox_M1 bleibt unverändert):
    ///   1. Gegenstände und Beutetabellen (neue Assets; vorhandene nur bei neuer DefaultsVersion, wie immer)
    ///   2. die vier Greybox-Tiere und zwei Kareth-Varianten (Definition, Prefab, hinten an die Netz-Prefabs)
    ///   3. Deserteure der Grenzgebirge-Platzhalter mit Menschen-Platzhalter statt Asche-Haut (Prefabs an Ort und Stelle)
    ///   4. Beute an die neuen Gegner, Balance-Rollen, Aufträge und Figuren (Texte aus kareth.txt), Datenbank
    ///   Seit 11.10.2026 (Classic-Paket): sechs seltene Tiere nach den Gegnern, ihre Beute nach der Kareth-Beute
    /// Die Prefabs entstehen in einer leeren Hilfsszene, die danach ohne Speichern geschlossen wird – die offene Szene bleibt
    /// unverändert (sonst lehnt der nächste szenensichere Auftrag ab). Danach: „kareth_v4_bauen“ (Figuren, Orte, Gebiete).
    /// Fehler im Protokoll (Validierung) machen das Ergebnis zu „fehler“. Mehrfach aufrufbar.
    /// </summary>
    public static class Kareth28Inhalte
    {
        public static (bool ok, string meldung) Run()
        {
            var sw = Stopwatch.StartNew();
            var errors = new List<string>();
            void Log(string text, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception) errors.Add(text);
            }

            Scene before = SceneManager.GetActiveScene();
            bool temp = before.IsValid() && !string.IsNullOrEmpty(before.path);
            Scene helper = default;
            if (temp)
            {
                helper = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(helper);
            }
            Application.logMessageReceived += Log;
            try
            {
                K.ResetCache();
                Loc.Reload();
                ItemContentBuilder.Build();
                Dictionary<string, EnemyBuilder.BorderEnemy> enemies = EnemyBuilder.BuildKarethCreatures();
                string deserters = EnemyBuilder.ApplyDeserterLook();
                ItemContentBuilder.AssignKarethLoot();
                // Classic-Paket (Max 11.10.2026 abends): sechs seltene Tiere, danach ihre Beute
                Dictionary<string, EnemyBuilder.BorderEnemy> rares = EnemyBuilder.BuildKarethRares();
                ItemContentBuilder.AssignKarethRareLoot();
                foreach (var p in rares) enemies[p.Key] = p.Value;
                int roles = BalanceRoleBuilder.Apply();
                QuestContentBuilder.Build();
                DefinitionValidator.ValidateAndRebuild();
                AssetDatabase.SaveAssets();
                int prefabs = 0;
                foreach (EnemyBuilder.BorderEnemy e in enemies.Values) if (e != null && e.Def != null && e.Prefab != null) prefabs++;
                string msg = $"#28: {prefabs}/{enemies.Count} Gegner mit Definition und Prefab, Beute und Balance ({roles} Rollen neu), " +
                             $"11 Aufträge und 5 Figuren, Classic-Paket (11 Aufträge, 6 seltene Tiere) · Deserteure: {deserters} · {sw.Elapsed.TotalSeconds:0} s";
                if (errors.Count > 0) msg += $" · {errors.Count} Fehler im Protokoll, z. B.: {errors[0]}";
                Debug.Log("[Ashveil] " + msg);
                return (errors.Count == 0 && prefabs == enemies.Count, msg);
            }
            finally
            {
                Application.logMessageReceived -= Log;
                if (temp)
                {
                    if (before.IsValid() && before.isLoaded) SceneManager.SetActiveScene(before);
                    EditorSceneManager.CloseScene(helper, true);
                }
            }
        }
    }
}
