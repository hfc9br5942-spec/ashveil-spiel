using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Leistung (Claude 14.10.2026, Lücke 9): Was läuft beim Laden einer Zellen-Szene an? Auftrag „szene_komponenten|Szene“
    /// öffnet die Szene zusätzlich (ohne zu speichern) und zählt je Skript-Typ die Komponenten, mit Kennzeichen, ob der Typ
    /// Awake / OnEnable / Start / Update hat – damit sieht man, wessen Start-Methoden das Laden der Zelle im Hauptfaden
    /// aufhalten (Messung: ≈ 55 ms „ScriptRunDelayedStartupFrame“). Bericht: Logs/Ashveil/Szene_Komponenten_&lt;Szene&gt;.txt.
    /// Ändert nichts.
    /// </summary>
    [InitializeOnLoad]
    public static class AuftragSzenenAnalyse
    {
        static AuftragSzenenAnalyse()
        {
            Auftragsordner.Registrieren("szene_komponenten", Zaehlen, true, 300);
        }

        private const BindingFlags Ms = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static bool Hat(Type t, string name)
        {
            for (Type x = t; x != null && x != typeof(MonoBehaviour); x = x.BaseType)
                if (x.GetMethod(name, Ms, null, Type.EmptyTypes, null) != null) return true;
            return false;
        }

        private static void Zaehlen(Auftragsordner.Auftrag a, Action<bool, string> fertig)
        {
            string pfad = (a.parameter ?? "").Trim();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(pfad) == null) { fertig(false, "Szene nicht gefunden: " + pfad); return; }
            Scene vorher = SceneManager.GetActiveScene();
            Scene s = SceneManager.GetSceneByPath(pfad);
            bool selbst = !s.isLoaded;
            if (selbst) s = EditorSceneManager.OpenScene(pfad, OpenSceneMode.Additive);
            try
            {
                var zahl = new Dictionary<Type, int>();
                int objekte = 0, aktiv = 0, renderer = 0, kollider = 0, lichter = 0, tiere = 0;
                foreach (GameObject r in s.GetRootGameObjects())
                {
                    foreach (Transform t in r.GetComponentsInChildren<Transform>(true))
                    {
                        objekte++;
                        if (t.gameObject.activeInHierarchy) aktiv++;
                    }
                    renderer += r.GetComponentsInChildren<Renderer>(true).Length;
                    kollider += r.GetComponentsInChildren<Collider>(true).Length;
                    lichter += r.GetComponentsInChildren<Light>(true).Length;
                    tiere += r.GetComponentsInChildren<Animator>(true).Length;
                    foreach (MonoBehaviour m in r.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (m == null) continue;
                        Type ty = m.GetType();
                        zahl[ty] = zahl.TryGetValue(ty, out int n) ? n + 1 : 1;
                    }
                }
                var sb = new StringBuilder();
                sb.AppendLine($"Szene {pfad}: {objekte} Objekte ({aktiv} aktiv), {renderer} Renderer, {kollider} Kollider, {lichter} Lichter, {tiere} Animatoren");
                sb.AppendLine("Anzahl · Typ · [A=Awake O=OnEnable S=Start U=Update L=LateUpdate]");
                foreach (var kv in zahl.OrderByDescending(k => k.Value))
                {
                    Type t = kv.Key;
                    string f = (Hat(t, "Awake") ? "A" : "-") + (Hat(t, "OnEnable") ? "O" : "-") + (Hat(t, "Start") ? "S" : "-") +
                               (Hat(t, "Update") ? "U" : "-") + (Hat(t, "LateUpdate") ? "L" : "-");
                    sb.AppendLine($"{kv.Value,6} · {t.FullName} · [{f}]");
                }
                string datei = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Logs", "Ashveil",
                    "Szene_Komponenten_" + s.name + ".txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(datei));
                System.IO.File.WriteAllText(datei, sb.ToString());
                var top = zahl.Where(k => Hat(k.Key, "Start") || Hat(k.Key, "Awake") || Hat(k.Key, "OnEnable"))
                              .OrderByDescending(k => k.Value).Take(12).Select(k => $"{k.Key.Name} {k.Value}");
                fertig(true, $"{s.name}: {objekte} Objekte, {renderer} Renderer, {kollider} Kollider, {lichter} Lichter, {tiere} Animatoren · " +
                             "mit Awake/OnEnable/Start: " + string.Join(", ", top) + " · Bericht " + datei);
            }
            finally
            {
                if (selbst) EditorSceneManager.CloseScene(s, true);
                if (vorher.IsValid()) SceneManager.SetActiveScene(vorher);
            }
        }
    }
}
